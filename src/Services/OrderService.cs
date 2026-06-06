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
    public async Task<ReceiptDto> CreateAsync(CreateOrderRequest req, CancellationToken ct = default)
    {
        if (req.Items is null || req.Items.Count == 0)
            throw new ArgumentException("The cart is empty.");

        var menuIds = req.Items.Select(i => i.MenuItemId).Distinct().ToList();
        var modIds = req.Items.SelectMany(i => i.ModifierIds ?? Array.Empty<int>()).Distinct().ToList();

        // Sequential awaits — EF Core DbContext is not thread-safe; Task.WhenAll on the same
        // context causes "second operation started before previous completed" errors.
        var menuItems = await db.MenuItems.Include(m => m.Recipe).Where(m => menuIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        var mods      = modIds.Count > 0 ? await db.Modifiers.Where(m => modIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct) : new Dictionary<int, Modifier>();
        var taxRate   = await settings.GetTaxRateAsync(ct);

        var lineItems = new List<TransactionItem>();
        var usage = new Dictionary<int, decimal>();      // ingredientId -> quantity to deduct
        var subtotal = BuildLineItems(req.Items, menuItems, mods, lineItems, usage);

        subtotal = Math.Round(subtotal, 2);
        await ValidateDiscountAsync(subtotal, req.DiscountAmount, ct);
        Discount discount = new FixedAmountDiscount(req.DiscountAmount);
        var discountAmount = discount.Apply(subtotal);
        // VAT-inclusive pricing: menu prices already include tax, so the tax is the portion
        // contained within the (discounted) subtotal — not added on top. Total = what the
        // customer pays = discounted subtotal.
        var tax = Math.Round((subtotal - discountAmount) * taxRate / (100m + taxRate), 2);
        var total = subtotal - discountAmount;

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

        // Deduct recipe ingredients (rejecting the order if stock can't cover it) and collect
        // low-stock warnings.
        var tx = new Transaction
        {
            Timestamp = DateTime.UtcNow, Subtotal = subtotal, DiscountAmount = discountAmount,
            TaxAmount = tax, TotalAmount = total, PaymentMethod = method,
            CashierId = current.Id, Status = "Preparing",
            Items = lineItems, Payments = payRows
        };
        db.Transactions.Add(tx);
        audit.Add("OrderPlaced", $"Txn total {total:0.00} via {method}; {lineItems.Count} line(s).");

        var warnings = await PersistWithStockAsync(usage, ct);

        return ToReceipt(tx, current.Username, tendered, change, warnings);
    }

    // Applies ingredient deductions and commits the order, retrying on optimistic-concurrency
    // conflicts. Each attempt re-reads the conflicting ingredients' current stock and re-applies the
    // deduction on top of it, so two concurrent orders can never overwrite each other's deduction.
    // Returns the low-stock warnings computed from the committed stock levels. Throws
    // InsufficientStockException (without persisting anything) if any ingredient is short.
    private async Task<List<string>> PersistWithStockAsync(Dictionary<int, decimal> usage, CancellationToken ct = default)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var warnings = new List<string>();
            List<Ingredient> ings = usage.Count > 0
                ? await db.Ingredients.Where(i => usage.Keys.Contains(i.Id)).ToListAsync(ct)
                : new List<Ingredient>();

            // Never sell into negative stock. If any ingredient can't cover the order's usage,
            // reject the whole order before deducting anything so it stays consistent. This runs
            // against freshly-read stock on every retry, so it's safe under concurrent orders.
            var shortages = ings
                .Where(i => i.StockLevel < usage[i.Id])
                .Select(i => $"{i.Name} (need {usage[i.Id]} {i.Unit}, have {i.StockLevel} {i.Unit})")
                .ToList();
            if (shortages.Count > 0)
                throw new InsufficientStockException(
                    "Not enough stock to complete this order: " + string.Join("; ", shortages) + ".");

            foreach (var ing in ings)
            {
                ing.StockLevel -= usage[ing.Id];
                if (ing.StockLevel <= ing.Threshold) warnings.Add($"{ing.Name} is low ({ing.StockLevel} {ing.Unit}).");
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return warnings;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < maxAttempts)
            {
                // A concurrent write changed one of these ingredients. Refresh the conflicting
                // entries to the latest DB values (and current xmin) and loop to re-apply.
                foreach (var entry in ex.Entries)
                {
                    var dbValues = await entry.GetDatabaseValuesAsync(ct);
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

    public async Task<ReceiptDto?> RefundAsync(int id, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required for a refund.");
        var tx = await db.Transactions.Include(t => t.Items).Include(t => t.Payments)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tx is null) return null;
        if (tx.Status != "Completed") throw new InvalidOperationException("Only completed transactions can be refunded.");

        tx.Status = "Refunded";
        tx.Notes = reason;
        // Restock ingredients from the current recipes of the sold items.
        var menuIds = tx.Items.Select(i => i.MenuItemId).Distinct().ToList();
        var recipeLines = await db.RecipeIngredients.Where(r => menuIds.Contains(r.MenuItemId)).ToListAsync(ct);
        var ingIds = recipeLines.Select(r => r.IngredientId).Distinct().ToList();
        var ingById = await db.Ingredients.Where(i => ingIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        foreach (var item in tx.Items)
            foreach (var rl in recipeLines.Where(r => r.MenuItemId == item.MenuItemId))
                if (ingById.TryGetValue(rl.IngredientId, out var ing)) ing.StockLevel += rl.Quantity * item.Quantity;

        audit.Add("OrderRefunded", $"Txn #{tx.Id} ({tx.TotalAmount:0.00}) refunded. Reason: {reason}");
        await db.SaveChangesAsync(ct);
        return ToReceipt(tx, current.Username, 0, 0, new List<string>());
    }

    // Pre-payment cancellation: only the audited reason is persisted (cart cleared client-side).
    public Task CancelAsync(string reason, CancellationToken ct = default) =>
        audit.LogAsync("OrderCancelled", string.IsNullOrWhiteSpace(reason) ? "(no reason given)" : reason, ct);

    // Saves the current cart as a Draft — no payment, no stock deduction, excluded from sales.
    public async Task<DraftDto> SaveDraftAsync(SaveDraftRequest req, CancellationToken ct = default)
    {
        if (req.Items is null || req.Items.Count == 0)
            throw new ArgumentException("The cart is empty.");

        var menuIds = req.Items.Select(i => i.MenuItemId).Distinct().ToList();
        var modIds = req.Items.SelectMany(i => i.ModifierIds ?? Array.Empty<int>()).Distinct().ToList();
        var menuItems = await db.MenuItems.Include(m => m.Recipe).Where(m => menuIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        var mods = modIds.Count > 0 ? await db.Modifiers.Where(m => modIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct) : new Dictionary<int, Modifier>();

        var lineItems = new List<TransactionItem>();
        var usage = new Dictionary<int, decimal>();
        var subtotal = BuildLineItems(req.Items, menuItems, mods, lineItems, usage);
        subtotal = Math.Round(subtotal, 2);
        await ValidateDiscountAsync(subtotal, req.DiscountAmount, ct);
        Discount discount = new FixedAmountDiscount(req.DiscountAmount);
        var discountAmount = discount.Apply(subtotal);

        var tx = new Transaction
        {
            Timestamp = DateTime.UtcNow, Subtotal = subtotal, DiscountAmount = discountAmount,
            TaxAmount = 0, TotalAmount = 0, PaymentMethod = req.PaymentMethod ?? "Cash",
            CashierId = current.Id, Status = "Draft",
            Items = lineItems
        };
        db.Transactions.Add(tx);
        audit.Add("OrderDrafted", $"Draft saved; {lineItems.Count} item(s), discount {discountAmount:0.00}.");
        await db.SaveChangesAsync(ct);
        return ToDraft(tx);
    }

    // Confirms a Draft — runs full payment/stock logic and moves it to Preparing.
    public async Task<ReceiptDto> ConfirmDraftAsync(int id, ConfirmDraftRequest req, CancellationToken ct = default)
    {
        var tx = await db.Transactions.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id && t.Status == "Draft", ct);
        if (tx is null) throw new InvalidOperationException($"Draft #{id} not found.");

        // Re-check the cap at the point money changes hands: a draft saved before the cap
        // existed (or under an older limit) must not be confirmable above the current cap.
        await ValidateDiscountAsync(tx.Subtotal, tx.DiscountAmount, ct);

        var taxRate = await settings.GetTaxRateAsync(ct);
        var payments = req.Payments ?? new List<PaymentInput>();
        var cashTendered = payments.Where(p => p.Method == "Cash").Sum(p => p.Amount);
        var gcashTendered = payments.Where(p => p.Method == "GCash").Sum(p => p.Amount);
        var totalTendered = cashTendered + gcashTendered;

        var discountedSubtotal = tx.Subtotal - tx.DiscountAmount;
        // VAT-inclusive: tax is contained within the discounted subtotal, total = discounted subtotal.
        var tax = Math.Round(discountedSubtotal * taxRate / (100m + taxRate), 2);
        var total = discountedSubtotal;

        if (totalTendered + 0.005m < total) throw new ArgumentException("Insufficient payment.");
        var change = Math.Round(totalTendered - total, 2);
        var tendered = Math.Round(totalTendered, 2);

        var payRows = new List<Payment>();
        if (gcashTendered > 0) payRows.Add(new Payment { Method = "GCash", Amount = Math.Min(gcashTendered, total) });
        var cashApplied = Math.Max(0, total - Math.Min(gcashTendered, total));
        if (cashApplied > 0 || payRows.Count == 0) payRows.Add(new Payment { Method = "Cash", Amount = cashApplied });
        var method = gcashTendered > 0 ? "GCash" : "Cash";

        tx.TaxAmount = tax;
        tx.TotalAmount = total;
        tx.PaymentMethod = method;
        tx.Status = "Preparing";
        tx.Payments = payRows;

        // Deduct stock
        var menuIds = tx.Items.Select(i => i.MenuItemId).Distinct().ToList();
        var menuItems = await db.MenuItems.Include(m => m.Recipe).Where(m => menuIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        var usage = new Dictionary<int, decimal>();
        foreach (var item in tx.Items)
            if (menuItems.TryGetValue(item.MenuItemId, out var mi))
                foreach (var ri in mi.Recipe)
                    usage[ri.IngredientId] = usage.GetValueOrDefault(ri.IngredientId) + ri.Quantity * item.Quantity;

        audit.Add("DraftConfirmed", $"Draft #{tx.Id} confirmed; total {total:0.00} via {method}.");
        var warnings = await PersistWithStockAsync(usage, ct);
        return ToReceipt(tx, current.Username, tendered, change, warnings);
    }

    public async Task<List<DraftDto>> GetDraftsAsync(CancellationToken ct = default) =>
        (await db.Transactions.AsNoTracking()
            .Where(t => t.Status == "Draft" && t.CashierId == current.Id)
            .Include(t => t.Items)
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync(ct))
        .Select(ToDraft).ToList();

    public async Task DeleteDraftAsync(int id, CancellationToken ct = default)
    {
        var tx = await db.Transactions.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id && t.Status == "Draft", ct);
        if (tx is null) return;
        db.Transactions.Remove(tx);
        audit.Add("DraftDeleted", $"Draft #{id} deleted.");
        await db.SaveChangesAsync(ct);
    }

    private static DraftDto ToDraft(Transaction tx)
    {
        var summary = string.Join(", ", tx.Items.Select(i => i.Quantity > 1 ? $"{i.Quantity}x {i.ItemName}" : i.ItemName));
        return new DraftDto(tx.Id, tx.Timestamp, "", tx.PaymentMethod, tx.Subtotal, tx.DiscountAmount,
            tx.Items.Count, summary,
            tx.Items.Select(i => new ReceiptLineDto(i.ItemName, i.Quantity, i.UnitPrice, i.LineTotal, i.Modifiers)).ToList());
    }

    // Advances order through the queue: Preparing → Completed.
    private static readonly Dictionary<string, string> NextStatus = new()
    {
        ["Pending"] = "Preparing",    // legacy: kept for old orders
        ["Preparing"] = "Completed"
    };

    public async Task<TransactionSummaryDto?> AdvanceStatusAsync(int id, CancellationToken ct = default)
    {
        var tx = await db.Transactions.Include(t => t.Cashier)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tx is null) return null;
        if (!NextStatus.TryGetValue(tx.Status, out var next))
            throw new InvalidOperationException($"Order #{id} cannot be advanced from status '{tx.Status}'.");

        tx.Status = next;
        audit.Add("OrderStatusAdvanced", $"Txn #{tx.Id} moved to {next}.");
        await db.SaveChangesAsync(ct);

        var items = await db.TransactionItems.Where(ti => ti.TransactionId == id).ToListAsync(ct);
        var itemSummary = string.Join(", ", items.Select(i => i.Quantity > 1 ? $"{i.Quantity}x {i.ItemName}" : i.ItemName));
        var name = tx.Cashier.FullName != "" ? tx.Cashier.FullName : tx.Cashier.Username;
        return new TransactionSummaryDto(tx.Id, tx.Timestamp, tx.TotalAmount, tx.PaymentMethod, tx.Status, name, items.Count, itemSummary);
    }

    public async Task<int> ActiveQueueCountAsync(CancellationToken ct = default) =>
        await db.Transactions.CountAsync(t => t.Status == "Pending" || t.Status == "Preparing", ct);

    public async Task<int> NextOrderNumberAsync(CancellationToken ct = default) =>
        (await db.Transactions.MaxAsync(t => (int?)t.Id, ct) ?? 0) + 1;

    public async Task<List<TransactionSummaryDto>> RecentAsync(int take = 50, DateTime? from = null, DateTime? to = null,
        CancellationToken ct = default) =>
        await db.Transactions.AsNoTracking()
            .Where(t => (from == null || t.Timestamp >= from) && (to == null || t.Timestamp < to))
            .OrderByDescending(t => t.Timestamp)
            .Take(take)
            .Select(t => new TransactionSummaryDto(t.Id, t.Timestamp, t.TotalAmount, t.PaymentMethod, t.Status,
                t.Cashier.FullName != "" ? t.Cashier.FullName : t.Cashier.Username,
                t.Items.Count,
                string.Join(", ", t.Items.Select(i => i.Quantity > 1 ? i.Quantity + "x " + i.ItemName : i.ItemName))))
            .ToListAsync(ct);

    public async Task<ReceiptDto?> GetReceiptAsync(int id, CancellationToken ct = default)
    {
        var tx = await db.Transactions.Include(t => t.Items).Include(t => t.Payments).Include(t => t.Cashier)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tx is null) return null;
        var name = CashierDisplayName(tx.Cashier);
        return ToReceipt(tx, name, tx.TotalAmount, 0, new List<string>());
    }

    // Enforces the configurable maximum discount (percent of subtotal). Rejects orders whose
    // requested discount would exceed the cap — primarily to stop the discount being used to
    // zero out an order ("free order" fraud). The small epsilon mirrors the payment tolerance
    // elsewhere so floating rounding at the boundary doesn't spuriously reject a valid discount.
    private async Task ValidateDiscountAsync(decimal subtotal, decimal discountAmount, CancellationToken ct)
    {
        if (discountAmount <= 0m) return;   // negative/zero is already clamped to 0 by the discount
        var maxPercent = await settings.GetMaxDiscountPercentAsync(ct);
        var maxDiscount = Math.Round(subtotal * maxPercent / 100m, 2);
        if (discountAmount > maxDiscount + 0.005m)
            throw new ArgumentException(
                $"Discount of {discountAmount:0.00} exceeds the maximum allowed " +
                $"({maxPercent:0.##}% of the subtotal = {maxDiscount:0.00}).");
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
