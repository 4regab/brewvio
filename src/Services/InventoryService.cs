using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Services;

// Inventory Engine — stock listing, low-stock alerts, audited manual adjustments, and CRUD.
public class InventoryService(BrewvioDbContext db, AuditService audit)
{
    // Lists all ingredients (alphabetical) as DTOs.
    // ct: cancellation token.
    // returns: all ingredients mapped to DTOs.
    public async Task<List<IngredientDto>> ListAsync(CancellationToken ct = default) =>
        ToDtos(await db.Ingredients.AsNoTracking().OrderBy(i => i.Name).ToListAsync(ct));

    // Lists ingredients at or below their low-stock threshold (alphabetical) as DTOs.
    // ct: cancellation token.
    // returns: the low-stock ingredients mapped to DTOs.
    public async Task<List<IngredientDto>> LowStockAsync(CancellationToken ct = default) =>
        ToDtos(await db.Ingredients.AsNoTracking().Where(i => i.StockLevel <= i.Threshold).OrderBy(i => i.Name).ToListAsync(ct));

    // Creates a new ingredient from the request and writes an audit entry.
    // r: the ingredient details to create.
    // ct: cancellation token.
    // returns: the created ingredient as a DTO.
    public async Task<IngredientDto> CreateAsync(IngredientRequest r, CancellationToken ct = default)
    {
        var ing = new Ingredient
        {
            Code = r.Code, Name = r.Name, Category = r.Category, Unit = r.Unit, StockLevel = r.StockLevel,
            Threshold = r.Threshold, CostPerUnit = r.CostPerUnit
        };
        db.Ingredients.Add(ing);
        audit.Add("IngredientCreated", $"{r.Name} ({r.Unit}); stock {r.StockLevel}, threshold {r.Threshold}");
        await db.SaveChangesAsync(ct);
        return ToDto(ing);
    }

    // Updates an ingredient's metadata (not its stock level) and audits the change.
    // id: the ingredient id to update.
    // r: the new ingredient metadata.
    // ct: cancellation token.
    // returns: the updated ingredient DTO, or null if not found.
    public async Task<IngredientDto?> UpdateAsync(int id, IngredientRequest r, CancellationToken ct = default)
    {
        var ing = await db.Ingredients.FindAsync([id], ct);
        if (ing is null) return null;
        // Stock changes must go through AdjustAsync (which records a reason); only edit metadata here.
        ing.Code = r.Code; ing.Name = r.Name; ing.Category = r.Category; ing.Unit = r.Unit;
        ing.Threshold = r.Threshold; ing.CostPerUnit = r.CostPerUnit;
        audit.Add("IngredientUpdated", $"{r.Name} ({r.Unit}); threshold {r.Threshold}, cost {r.CostPerUnit}");
        await db.SaveChangesAsync(ct);
        return ToDto(ing);
    }

    // Deletes an ingredient and audits the removal.
    // id: the ingredient id to delete.
    // ct: cancellation token.
    // returns: true if deleted, false if not found.
    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var ing = await db.Ingredients.FindAsync([id], ct);
        if (ing is null) return false;
        db.Ingredients.Remove(ing);
        audit.Add("IngredientDeleted", $"{ing.Name} ({ing.Unit}) removed from inventory");
        await db.SaveChangesAsync(ct);
        return true;
    }

    // Manual stock-take: requires a non-empty reason (Adjust Inventory exception path).
    public async Task<IngredientDto?> AdjustAsync(int id, StockAdjustRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Reason))
            throw new ArgumentException("A reason is required for inventory adjustments.");
        var ing = await db.Ingredients.FindAsync([id], ct);
        if (ing is null) return null;
        var old = ing.StockLevel;
        ing.StockLevel = r.NewQuantity;
        var log = audit.Add(StockActions.Adjust, $"{ing.Name}: {old:0.###} -> {r.NewQuantity:0.###} {ing.Unit}. Reason: {r.Reason}", ing.Id);
        log.Quantity = r.NewQuantity - old;   // signed delta of the stock-take
        log.BalanceAfter = r.NewQuantity;
        await db.SaveChangesAsync(ct);
        return ToDto(ing);
    }

    // Stock In: add a positive quantity to current stock (a delivery/receipt). Reason optional.
    public Task<IngredientDto?> StockInAsync(int id, StockMovementRequest r, CancellationToken ct = default) =>
        MoveStockAsync(id, r, StockActions.StockIn, ct);

    // Stock Out: remove a positive quantity from current stock (wastage/spoilage). A reason is
    // required, and the move is rejected (no negative stock) if it exceeds what's on hand.
    public Task<IngredientDto?> StockOutAsync(int id, StockMovementRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Reason))
            throw new ArgumentException("A reason is required for stock-out.");
        return MoveStockAsync(id, r, StockActions.StockOut, ct);
    }

    // Applies a signed stock delta (+ for StockIn, - for StockOut) and records a per-ingredient
    // audit movement, atomically. Because this is a DELTA (unlike the absolute AdjustAsync), a lost
    // update would corrupt the running total — so on an optimistic-concurrency conflict we reload
    // the current stock, re-apply the delta on top of it, and retry. We must NOT begin an explicit
    // transaction here (the production EnableRetryOnFailure execution strategy forbids it); a plain
    // loop around SaveChanges composes correctly, exactly as OrderService does.
    private async Task<IngredientDto?> MoveStockAsync(int id, StockMovementRequest r, string action, CancellationToken ct)
    {
        if (r.Quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.");
        var ing = await db.Ingredients.FindAsync([id], ct);
        if (ing is null) return null;

        var isOut = action == StockActions.StockOut;
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (isOut && ing.StockLevel < r.Quantity)
                throw new InsufficientStockException(
                    $"Cannot remove {r.Quantity} {ing.Unit} of {ing.Name}: only {ing.StockLevel} {ing.Unit} on hand.");

            var old = ing.StockLevel;
            ing.StockLevel = isOut ? old - r.Quantity : old + r.Quantity;
            var reason = string.IsNullOrWhiteSpace(r.Reason) ? "" : $" Reason: {r.Reason.Trim()}.";
            var log = audit.Add(action, $"{ing.Name}: {(isOut ? "-" : "+")}{r.Quantity:0.###} {ing.Unit}.{reason} {old:0.###} -> {ing.StockLevel:0.###} {ing.Unit}", ing.Id);
            log.Quantity = isOut ? -r.Quantity : r.Quantity;   // signed: + in, - out
            log.BalanceAfter = ing.StockLevel;

            try
            {
                await db.SaveChangesAsync(ct);
                return ToDto(ing);
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < maxAttempts)
            {
                // A concurrent write changed this ingredient. Refresh it to the latest DB values
                // (and current xmin) so the next iteration re-applies the delta on top of the real
                // stock, and drop this attempt's staged audit row so the retry doesn't double-log.
                db.Entry(log).State = EntityState.Detached;
                foreach (var entry in ex.Entries)
                {
                    var dbValues = await entry.GetDatabaseValuesAsync(ct);
                    if (dbValues is null) return null;             // deleted concurrently
                    entry.OriginalValues.SetValues(dbValues);
                    entry.CurrentValues.SetValues(dbValues);
                    entry.State = EntityState.Unchanged;
                }
            }
        }
        throw new InvalidOperationException(
            "The stock movement could not be completed due to high inventory contention. Please try again.");
    }

    // Per-ingredient stock-movement history (newest first), read-only. Returns only the stock
    // movement actions (StockIn/StockOut/InventoryAdjust/StockSale/StockRefund) for this ingredient.
    public async Task<List<StockMovementDto>> HistoryAsync(int id, int take = 100, CancellationToken ct = default)
    {
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.IngredientId == id && StockActions.All.Contains(a.Action))
            .OrderByDescending(a => a.Timestamp)
            .Take(take)
            .ToListAsync(ct);
        return await ProjectAsync(rows, ct);
    }

    // Global stock-movement ledger across all ingredients, filtered + paginated for the dedicated
    // Stock Movements page. Filters: date range [from, to) (to is exclusive), movement type (action),
    // and a single ingredient. Returns the page plus the full filtered total for the pager.
    public async Task<PagedStockMovementsDto> MovementsAsync(DateTime? from, DateTime? to, string? type,
        int? ingredientId, int skip, int take, CancellationToken ct = default)
    {
        var q = FilteredMovements(from, to, type, ingredientId);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(a => a.Timestamp).Skip(skip).Take(take).ToListAsync(ct);
        return new PagedStockMovementsDto(total, skip, take, await ProjectAsync(rows, ct));
    }

    // Same filter set as MovementsAsync but returns all matching rows (capped) for CSV export.
    public async Task<List<StockMovementDto>> MovementsForExportAsync(DateTime? from, DateTime? to, string? type,
        int? ingredientId, CancellationToken ct = default)
    {
        var rows = await FilteredMovements(from, to, type, ingredientId)
            .OrderByDescending(a => a.Timestamp).Take(10_000).ToListAsync(ct);
        return await ProjectAsync(rows, ct);
    }

    // Builds the base query for stock-movement audit rows, applying optional date-range,
    // movement-type, and ingredient filters.
    // from: inclusive lower bound on timestamp, or null for no lower bound.
    // to: exclusive upper bound on timestamp, or null for no upper bound.
    // type: movement action to filter by, or null/blank for all types.
    // ingredientId: ingredient to filter by, or null for all ingredients.
    // returns: the filtered (unordered) query of movement rows.
    private IQueryable<AuditLog> FilteredMovements(DateTime? from, DateTime? to, string? type, int? ingredientId)
    {
        var q = db.AuditLogs.AsNoTracking().Where(a => a.IngredientId != null && StockActions.All.Contains(a.Action));
        if (from is not null) q = q.Where(a => a.Timestamp >= from);
        if (to is not null) q = q.Where(a => a.Timestamp < to);
        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(a => a.Action == type);
        if (ingredientId is not null) q = q.Where(a => a.IngredientId == ingredientId);
        return q;
    }

    // Resolves ingredient name/code for a set of movement rows (one extra query, deleted items show
    // a placeholder) and maps to the DTO. Audit rows are immutable so the name is looked up live.
    private async Task<List<StockMovementDto>> ProjectAsync(List<AuditLog> rows, CancellationToken ct)
    {
        var ids = rows.Where(r => r.IngredientId != null).Select(r => r.IngredientId!.Value).Distinct().ToList();
        var ingById = ids.Count == 0
            ? new Dictionary<int, Ingredient>()
            : await db.Ingredients.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        return rows.Select(r =>
        {
            Ingredient? ing = r.IngredientId is int iid && ingById.TryGetValue(iid, out var found) ? found : null;
            return new StockMovementDto(r.Id, r.Timestamp, r.IngredientId,
                ing?.Name ?? "(deleted item)", ing?.Code ?? "—",
                r.Username, r.Action, r.Quantity, r.BalanceAfter, r.Details);
        }).ToList();
    }

    // Maps an ingredient to its DTO, computing the low-stock flag and status text.
    // i: the ingredient to map.
    // returns: the ingredient DTO.
    private static IngredientDto ToDto(Ingredient i)
    {
        return new IngredientDto(i.Id, i.Code, i.Name, i.Category, i.Unit, i.StockLevel, i.Threshold,
            i.CostPerUnit, i.StockLevel <= i.Threshold, StockStatus(i.StockLevel, i.Threshold));
    }

    // Derives the human-readable stock status from the current level vs the low-stock threshold.
    private static string StockStatus(decimal stockLevel, decimal threshold)
    {
        if (stockLevel <= 0) return "Out of Stock";
        if (stockLevel <= threshold) return "Low Stock";
        return "In Stock";
    }
    // Maps a sequence of ingredients to DTOs.
    // items: the ingredients to map.
    // returns: the ingredients as a list of DTOs.
    private static List<IngredientDto> ToDtos(IEnumerable<Ingredient> items) => items.Select(ToDto).ToList();
}
