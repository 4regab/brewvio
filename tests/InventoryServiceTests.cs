using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Services;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Tests;

public class InventoryServiceTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static InventoryService Build(TestScope t) =>
        new(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

    [Fact]
    public async Task Adjust_without_reason_is_rejected()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var ing = t.Db.Ingredients.First();

        await Assert.ThrowsAsync<ArgumentException>(() => Build(t).AdjustAsync(ing.Id, new StockAdjustRequest(50m, "  ")));
    }

    [Fact]
    public async Task Adjust_sets_stock_and_writes_audit_entry()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var ing = t.Db.Ingredients.First(i => i.Name == "Whole Milk");

        await Build(t).AdjustAsync(ing.Id, new StockAdjustRequest(123m, "New delivery"));

        var v = t.NewContext();
        Assert.Equal(123m, (await v.Ingredients.FindAsync(ing.Id))!.StockLevel);
        Assert.Contains(await v.AuditLogs.ToListAsync(), a => a.Action == "InventoryAdjust" && a.Details.Contains("New delivery"));
    }

    [Fact]
    public async Task LowStock_lists_items_at_or_below_threshold()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");

        await Build(t).AdjustAsync(milk.Id, new StockAdjustRequest(100m, "Stock count"));

        var low = await Build(t).LowStockAsync();
        Assert.Contains(low, i => i.Name == "Whole Milk" && i.LowStock);
    }

    [Fact]
    public async Task Create_stores_code_category_and_derives_status()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var created = await Build(t).CreateAsync(new IngredientRequest("TEST-09", "Test Powder", "Powder", "g", 5m, 10m, 0.5m));

        Assert.Equal("TEST-09", created.Code);
        Assert.Equal("Powder", created.Category);
        Assert.Equal("Low Stock", created.Status);

        var depleted = await Build(t).AdjustAsync(created.Id, new StockAdjustRequest(0m, "Used up"));
        Assert.Equal("Out of Stock", depleted!.Status);
    }

    [Fact]
    public async Task Update_keeps_stock_and_edits_metadata()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var ing = t.Db.Ingredients.First(i => i.Name == "Sugar");
        var stock = ing.StockLevel;

        var updated = await Build(t).UpdateAsync(ing.Id, new IngredientRequest("SUGR-99", "Cane Sugar", "Pantry", "g", 0m, 1500m, 0.03m));

        Assert.Equal("SUGR-99", updated!.Code);
        Assert.Equal("Cane Sugar", updated.Name);
        Assert.Equal(stock, updated.StockLevel);
    }
}
