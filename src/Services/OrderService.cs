using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Helpers;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Services;

// Order Engine — builds & prices the order (modifiers, discount, tax), validates payment
// (incl. split), deducts recipe ingredients, records the sale, and produces the receipt.
public class OrderService(BrewvioDbContext db, CurrentUser current, AuditService audit, SettingsService settings)
{
    public async Task<ReceiptDto> CreateAsync(CreateOrderRequest req)
    {
        if (req.Items is null || req.Items.Count == 0)
            throw new ArgumentException("The cart is empty.");

        var menuIds = req.Items.Select(i => i.MenuItemId).Distinct().ToList();
        var menuItems = await db.MenuItems.Include(m => m.Recipe)
            .Where(m => menuIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
        var modIds = req.Items.SelectMany(i => i.ModifierIds ?? Array.Empty<int>()).Distinct().ToList();
        var mods = await db.Modifiers.Where(m => modIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id);

        var lineItems = new List<TransactionItem>();
        var usage = new Dictionary<int, decimal>();      // ingredientId -> quantity to deduct
        var subtotal = BuildLineItems(req.Items, menuItems, mods, lineItems, usage);

        subtotal = Math.Round(subtotal, 2);
        var discount = Math.Clamp(Math.Round(req.DiscountAmount, 2), 0, subtotal);
        var taxRate = await settings.GetTaxRateAsync();
        var tax = Math.Round((subtotal - discount) * taxRate / 100m, 2);
        var total = subtotal - discount + tax;

        // Payment: a single tender (Cash or GCash). GCash settles like cash for change/accounting.
        var payments = req.Payments ?? new List<PaymentInput>();
        var cashTendered = payments.Where(p => p.Method == "Cash").Sum(p => p.Amount);
        var gcashTendered = payments.Where(p => p.Method == "GCash").Sum(p => p.Amount);
        var totalTendered = cashTendered + gcashTendered;
        if (totalTendered + 0.005m < total) throw new ArgumentException("Insufficient payment.");
        var change = Math.Round(totalTendered - total, 2);
        var tendered = Math.Round(totalTendered, 2);

        var payRows = new List<Payment>();
        if (gcashTendered > 0) payRows.Add(new Payment { Method = "GCash", Amount = Math.Min(gcashTendered, total) });
        var cashApplied = Math.Max(0, total - Math.Min(gcashTendered, total));
        if (cashApplied > 0 || payRows.Count == 0) payRows.Add(new Payment { Method = "Cash", Amount = cashApplied });
        var method = gcashTendered > 0 ? "GCash" : "Cash";

        // Deduct recipe ingredients and collect low/negative-stock warnings.
        // The actual deduction + persistence happens in PersistWithStockAsync, which re-applies the
        // deductions against fresh values and retries if a concurrent order changed stock first
        // (optimistic concurrency via the Ingredient xmin token) — preventing oversell.
        var shiftId = (await db.Shifts.FirstOrDefaultAsync(s => s.CashierId == current.Id && s.Status == "Open"))?.Id;
        var tx = new Transaction
        {
            Timestamp = DateTime.UtcNow, Subtotal = subtotal, DiscountAmount = discount,
            TaxAmount = tax, TotalAmount = total, PaymentMethod = method,
            CashierId = current.Id, ShiftId = shiftId, Status = "Completed",
            Items = lineItems, Payments = payRows
        };
        db.Transactions.Add(tx);
        audit.Add("OrderCompleted", $"Txn total {total:0.00} via {method}; {lineItems.Count} line(s).");

        var warnings = await PersistWithStockAsync(usage);

        return ToReceipt(tx, current.Username, tendered, change, warnings);
    }

    // Applies ingredient deductions and commits the order, retrying on optimistic-concurrency
    // conflicts. Each attempt re-reads the conflicting ingredients' current stock and re-applies the
    // deduction on top of it, so two concurrent orders can never overwrite each other's deduction.
    // Returns the low/negative-stock warnings computed from the committed stock levels.
    private async Task<List<string>> PersistWithStockAsync(Dictionary<int, decimal> usage)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var warnings = new List<string>();
            List<Ingredient> ings = usage.Count > 0
                ? await db.Ingredients.Where(i => usage.Keys.Contains(i.Id)).ToListAsync()
                : new List<Ingredient>();
            foreach (var ing in ings)
            {
                ing.StockLevel -= usage[ing.Id];
                if (ing.StockLevel < 0) warnings.Add($"{ing.Name} is now negative ({ing.StockLevel} {ing.Unit}).");
                else if (ing.StockLevel <= ing.Threshold) warnings.Add($"{ing.Name} is low ({ing.StockLevel} {ing.Unit}).");
            }

            try
            {
                await db.SaveChangesAsync();
                return warnings;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < maxAttempts)
            {
                // A concurrent write changed one of these ingredients. Refresh the conflicting
                // entries to the latest DB values (and current xmin) and loop to re-apply.
                foreach (var entry in ex.Entries)
                {
                    var dbValues = await entry.GetDatabaseValuesAsync();
                    if (dbValues is null) continue;            // deleted concurrently — skip re-apply
                    entry.OriginalValues.SetValues(dbValues);
                    entry.CurrentValues.SetValues(dbValues);
                    entry.State = EntityState.Unchanged;
                }
            }
        }

        // Exhausted retries under sustained contention — surface as a domain error (-> HTTP 400/500).
        throw new InvalidOperationException(
            "The order could not be completed due to high inventory contention. Please try again.");
    }

    public async Task<ReceiptDto?> RefundAsync(int id, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required for a refund.");
        var tx = await db.Transactions.Include(t => t.Items).Include(t => t.Payments)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tx is null) return null;
        if (tx.Status != "Completed") throw new InvalidOperationException("Only completed transactions can be refunded.");

        tx.Status = "Refunded";
        tx.Notes = reason;
        // Restock ingredients from the current recipes of the sold items.
        var menuIds = tx.Items.Select(i => i.MenuItemId).Distinct().ToList();
        var recipeLines = await db.RecipeIngredients.Where(r => menuIds.Contains(r.MenuItemId)).ToListAsync();
        var ingIds = recipeLines.Select(r => r.IngredientId).Distinct().ToList();
        var ingById = await db.Ingredients.Where(i => ingIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        foreach (var item in tx.Items)
            foreach (var rl in recipeLines.Where(r => r.MenuItemId == item.MenuItemId))
                if (ingById.TryGetValue(rl.IngredientId, out var ing)) ing.StockLevel += rl.Quantity * item.Quantity;

        audit.Add("OrderRefunded", $"Txn #{tx.Id} ({tx.TotalAmount:0.00}) refunded. Reason: {reason}");
        await db.SaveChangesAsync();
        return ToReceipt(tx, current.Username, 0, 0, new List<string>());
    }

    // Pre-payment cancellation: only the audited reason is persisted (cart cleared client-side).
    public Task CancelAsync(string reason) =>
        audit.LogAsync("OrderCancelled", string.IsNullOrWhiteSpace(reason) ? "(no reason given)" : reason);

    public async Task<List<TransactionSummaryDto>> RecentAsync(int take = 50) =>
        await db.Transactions.AsNoTracking().OrderByDescending(t => t.Timestamp).Take(take)
            .Select(t => new TransactionSummaryDto(t.Id, t.Timestamp, t.TotalAmount, t.PaymentMethod, t.Status,
                t.Cashier.FullName != "" ? t.Cashier.FullName : t.Cashier.Username, t.Items.Count))
            .ToListAsync();

    public async Task<ReceiptDto?> GetReceiptAsync(int id)
    {
        var tx = await db.Transactions.Include(t => t.Items).Include(t => t.Payments).Include(t => t.Cashier)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tx is null) return null;
        var name = CashierDisplayName(tx.Cashier);
        return ToReceipt(tx, name, tx.TotalAmount, 0, new List<string>());
    }

    // Validates each cart item, builds its TransactionItem line, accumulates ingredient usage,
    // and returns the running subtotal. Extracted from CreateAsync to keep that method simple.
    private static decimal BuildLineItems(
        IReadOnlyList<CartItemInput> items,
        IReadOnlyDictionary<int, MenuItem> menuItems,
        IReadOnlyDictionary<int, Modifier> mods,
        List<TransactionItem> lineItems,
        Dictionary<int, decimal> usage)
    {
        decimal subtotal = 0;
        foreach (var ci in items)
        {
            if (ci.Quantity <= 0) throw new ArgumentException("Quantity must be positive.");
            if (!menuItems.TryGetValue(ci.MenuItemId, out var mi) || !mi.IsActive)
                throw new ArgumentException($"Menu item {ci.MenuItemId} is unavailable.");

            var chosen = (ci.ModifierIds ?? Array.Empty<int>()).Where(mods.ContainsKey).Select(id => mods[id]).ToList();
            var unitPrice = mi.Price + chosen.Sum(m => m.PriceDelta);
            var lineTotal = unitPrice * ci.Quantity;
            subtotal += lineTotal;

            lineItems.Add(new TransactionItem
            {
                MenuItemId = mi.Id, ItemName = mi.Name, UnitPrice = unitPrice,
                Quantity = ci.Quantity, LineTotal = lineTotal,
                Modifiers = chosen.Count == 0 ? null : string.Join(", ", chosen.Select(m => m.Name))
            });
            foreach (var ri in mi.Recipe)
                usage[ri.IngredientId] = usage.GetValueOrDefault(ri.IngredientId) + ri.Quantity * ci.Quantity;
        }
        return subtotal;
    }

    // Prefers full name, falls back to username, empty when no cashier is attached.
    private static string CashierDisplayName(User? cashier)
    {
        if (cashier is null) return "";
        return cashier.FullName != "" ? cashier.FullName : cashier.Username;
    }

    private static ReceiptDto ToReceipt(Transaction tx, string cashier, decimal tendered, decimal change, List<string> warnings) =>
        new(tx.Id, tx.Timestamp, cashier, tx.PaymentMethod, tx.Subtotal, tx.DiscountAmount, tx.TaxAmount,
            tx.TotalAmount, tendered, change, tx.Status,
            tx.Items.Select(i => new ReceiptLineDto(i.ItemName, i.Quantity, i.UnitPrice, i.LineTotal, i.Modifiers)).ToList(),
            tx.Payments.Select(p => new PaymentInput(p.Method, p.Amount)).ToList(),
            warnings);
}
