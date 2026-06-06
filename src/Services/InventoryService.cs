using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Services;

// Inventory Engine — stock listing, low-stock alerts, audited manual adjustments, and CRUD.
public class InventoryService(BrewvioDbContext db, AuditService audit)
{
    public async Task<List<IngredientDto>> ListAsync(CancellationToken ct = default) =>
        ToDtos(await db.Ingredients.AsNoTracking().OrderBy(i => i.Name).ToListAsync(ct));

    public async Task<List<IngredientDto>> LowStockAsync(CancellationToken ct = default) =>
        ToDtos(await db.Ingredients.AsNoTracking().Where(i => i.StockLevel <= i.Threshold).OrderBy(i => i.Name).ToListAsync(ct));

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
        audit.Add("InventoryAdjust", $"{ing.Name}: {old} -> {r.NewQuantity} {ing.Unit}. Reason: {r.Reason}");
        await db.SaveChangesAsync(ct);
        return ToDto(ing);
    }

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
    private static List<IngredientDto> ToDtos(IEnumerable<Ingredient> items) => items.Select(ToDto).ToList();
}
