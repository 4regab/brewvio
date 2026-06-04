using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Services;

// Inventory Engine — stock listing, low-stock alerts, audited manual adjustments, and CRUD.
public class InventoryService(BrewvioDbContext db, AuditService audit)
{
    public async Task<List<IngredientDto>> ListAsync() =>
        ToDtos(await db.Ingredients.OrderBy(i => i.Name).ToListAsync());

    public async Task<List<IngredientDto>> LowStockAsync() =>
        ToDtos(await db.Ingredients.Where(i => i.StockLevel <= i.Threshold).OrderBy(i => i.Name).ToListAsync());

    public async Task<IngredientDto> CreateAsync(IngredientRequest r)
    {
        var ing = new Ingredient
        {
            Code = r.Code, Name = r.Name, Category = r.Category, Unit = r.Unit, StockLevel = r.StockLevel,
            Threshold = r.Threshold, CostPerUnit = r.CostPerUnit
        };
        db.Ingredients.Add(ing);
        audit.Add("IngredientCreated", $"{r.Name} ({r.Unit}); stock {r.StockLevel}, threshold {r.Threshold}");
        await db.SaveChangesAsync();
        return ToDto(ing);
    }

    public async Task<IngredientDto?> UpdateAsync(int id, IngredientRequest r)
    {
        var ing = await db.Ingredients.FindAsync(id);
        if (ing is null) return null;
        // Stock changes must go through AdjustAsync (which records a reason); only edit metadata here.
        ing.Code = r.Code; ing.Name = r.Name; ing.Category = r.Category; ing.Unit = r.Unit;
        ing.Threshold = r.Threshold; ing.CostPerUnit = r.CostPerUnit;
        audit.Add("IngredientUpdated", $"{r.Name} ({r.Unit}); threshold {r.Threshold}, cost {r.CostPerUnit}");
        await db.SaveChangesAsync();
        return ToDto(ing);
    }

    // Manual stock-take: requires a non-empty reason (Adjust Inventory exception path).
    public async Task<IngredientDto?> AdjustAsync(int id, StockAdjustRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Reason))
            throw new ArgumentException("A reason is required for inventory adjustments.");
        var ing = await db.Ingredients.FindAsync(id);
        if (ing is null) return null;
        var old = ing.StockLevel;
        ing.StockLevel = r.NewQuantity;
        audit.Add("InventoryAdjust", $"{ing.Name}: {old} -> {r.NewQuantity} {ing.Unit}. Reason: {r.Reason}");
        await db.SaveChangesAsync();
        return ToDto(ing);
    }

    private static IngredientDto ToDto(Ingredient i)
    {
        var status = i.StockLevel <= 0 ? "Out of Stock"
            : i.StockLevel <= i.Threshold ? "Low Stock" : "In Stock";
        return new IngredientDto(i.Id, i.Code, i.Name, i.Category, i.Unit, i.StockLevel, i.Threshold,
            i.CostPerUnit, i.StockLevel <= i.Threshold, status);
    }
    private static List<IngredientDto> ToDtos(IEnumerable<Ingredient> items) => items.Select(ToDto).ToList();
}
