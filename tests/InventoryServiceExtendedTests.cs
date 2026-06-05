using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Services;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Tests;

/// <summary>
/// Extended coverage for InventoryService: ListAsync, DeleteAsync,
/// UpdateAsync null path, AdjustAsync null path, stock status derivation edge cases.
/// </summary>
public class InventoryServiceExtendedTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static InventoryService Build(TestScope t) =>
        new(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

    // ── ListAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_returns_all_ingredients_ordered_by_name()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var list = await Build(t).ListAsync();

        Assert.NotEmpty(list);
        for (var i = 1; i < list.Count; i++)
            Assert.True(string.Compare(list[i - 1].Name, list[i].Name, StringComparison.OrdinalIgnoreCase) <= 0);
    }

    [Fact]
    public async Task ListAsync_includes_in_stock_and_low_stock_items()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");

        // Drive milk to low-stock (stock = threshold)
        await svc.AdjustAsync(milk.Id, new StockAdjustRequest(milk.Threshold, "test"));

        var list = await svc.ListAsync();

        Assert.Contains(list, i => i.Name == "Whole Milk" && i.LowStock);
        Assert.Contains(list, i => !i.LowStock); // other ingredients are still fine
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_removes_ingredient_with_no_recipe_references()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);

        // Create a standalone ingredient that's not in any recipe
        var ing = await svc.CreateAsync(
            new IngredientRequest("DEL-01", "Deletable Herb", "Spice", "g", 100m, 10m, 0.05m));

        var result = await svc.DeleteAsync(ing.Id);

        Assert.True(result);
        Assert.Null(await t.NewContext().Ingredients.FindAsync(ing.Id));
    }

    [Fact]
    public async Task Delete_returns_false_for_missing_ingredient()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        Assert.False(await Build(t).DeleteAsync(999_999));
    }

    [Fact]
    public async Task Delete_writes_audit_entry()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var ing = await svc.CreateAsync(
            new IngredientRequest("DEL-02", "Temp Ingredient", "Other", "pc", 5m, 1m, 1m));

        await svc.DeleteAsync(ing.Id);

        var verify = t.NewContext();
        Assert.Contains(await verify.AuditLogs.ToListAsync(),
            a => a.Action == "IngredientDeleted" && a.Details.Contains("Temp Ingredient"));
    }

    // ── UpdateAsync null path ────────────────────────────────────────────────

    [Fact]
    public async Task Update_returns_null_for_missing_ingredient()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var result = await Build(t).UpdateAsync(999_999,
            new IngredientRequest("X", "X", "X", "g", 0m, 0m, 0m));

        Assert.Null(result);
    }

    // ── AdjustAsync null path ────────────────────────────────────────────────

    [Fact]
    public async Task Adjust_returns_null_for_missing_ingredient()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var result = await Build(t).AdjustAsync(999_999, new StockAdjustRequest(10m, "test"));

        Assert.Null(result);
    }

    // ── Stock status derivation ──────────────────────────────────────────────

    [Fact]
    public async Task StockStatus_in_stock_when_level_above_threshold()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");

        // Drive milk well above threshold
        await svc.AdjustAsync(milk.Id, new StockAdjustRequest(milk.Threshold + 500m, "restock"));

        var dto = (await svc.ListAsync()).First(i => i.Name == "Whole Milk");

        Assert.Equal("In Stock", dto.Status);
        Assert.False(dto.LowStock);
    }

    [Fact]
    public async Task StockStatus_out_of_stock_when_level_is_zero()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var sugar = t.Db.Ingredients.First(i => i.Name == "Sugar");

        await svc.AdjustAsync(sugar.Id, new StockAdjustRequest(0m, "fully depleted"));

        var dto = (await svc.ListAsync()).First(i => i.Name == "Sugar");

        Assert.Equal("Out of Stock", dto.Status);
        Assert.True(dto.LowStock);
    }

    [Fact]
    public async Task StockStatus_low_stock_when_level_equals_threshold_exactly()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");

        await svc.AdjustAsync(milk.Id, new StockAdjustRequest(milk.Threshold, "exact threshold test"));

        var dto = (await svc.ListAsync()).First(i => i.Name == "Whole Milk");

        Assert.Equal("Low Stock", dto.Status);
        Assert.True(dto.LowStock);
    }

    [Fact]
    public async Task Adjust_sets_exact_value_ignoring_previous_stock()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");

        // Double-adjust: second call should win absolutely, not add
        await svc.AdjustAsync(milk.Id, new StockAdjustRequest(999m, "first"));
        await svc.AdjustAsync(milk.Id, new StockAdjustRequest(50m, "second"));

        var final = (await svc.ListAsync()).First(i => i.Name == "Whole Milk");
        Assert.Equal(50m, final.StockLevel);
    }

    [Fact]
    public async Task LowStock_excludes_items_above_threshold()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);

        // Ensure all seeded items are well above threshold
        foreach (var ing in t.Db.Ingredients.ToList())
            await svc.AdjustAsync(ing.Id, new StockAdjustRequest(ing.Threshold + 1000m, "top-up"));

        var low = await svc.LowStockAsync();

        Assert.Empty(low);
    }
}
