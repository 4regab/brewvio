using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Services;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Tests;

public class InventoryServiceTests
{
    private static InventoryService Build(TestDb t) =>
        new(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

    [Fact]
    public async Task Adjust_without_reason_is_rejected()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var inv = Build(t);
        var ing = t.Db.Ingredients.First();

        await Assert.ThrowsAsync<ArgumentException>(() => inv.AdjustAsync(ing.Id, new StockAdjustRequest(50m, "  ")));
    }

    [Fact]
    public async Task Adjust_sets_stock_and_writes_audit_entry()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var inv = Build(t);
        var ing = t.Db.Ingredients.First(i => i.Name == "Whole Milk");

        await inv.AdjustAsync(ing.Id, new StockAdjustRequest(123m, "New delivery"));

        using var v = t.NewContext();
        Assert.Equal(123m, (await v.Ingredients.FindAsync(ing.Id))!.StockLevel);
        Assert.Contains(await v.AuditLogs.ToListAsync(), a => a.Action == "InventoryAdjust" && a.Details.Contains("New delivery"));
    }

    [Fact]
    public async Task LowStock_lists_items_at_or_below_threshold()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var inv = Build(t);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk"); // threshold 2000

        await inv.AdjustAsync(milk.Id, new StockAdjustRequest(100m, "Stock count"));

        var low = await inv.LowStockAsync();
        Assert.Contains(low, i => i.Name == "Whole Milk" && i.LowStock);
    }

    [Fact]
    public async Task Create_stores_code_category_and_derives_status()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var inv = Build(t);

        var created = await inv.CreateAsync(new IngredientRequest("TEST-09", "Test Powder", "Powder", "g", 5m, 10m, 0.5m));

        Assert.Equal("TEST-09", created.Code);
        Assert.Equal("Powder", created.Category);
        Assert.Equal("Low Stock", created.Status);   // 5 <= threshold 10

        var depleted = await inv.AdjustAsync(created.Id, new StockAdjustRequest(0m, "Used up"));
        Assert.Equal("Out of Stock", depleted!.Status);
    }

    [Fact]
    public async Task Update_keeps_stock_and_edits_metadata()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var inv = Build(t);
        var ing = t.Db.Ingredients.First(i => i.Name == "Sugar");
        var stock = ing.StockLevel;

        var updated = await inv.UpdateAsync(ing.Id, new IngredientRequest("SUGR-99", "Cane Sugar", "Pantry", "g", 0m, 1500m, 0.03m));

        Assert.Equal("SUGR-99", updated!.Code);
        Assert.Equal("Cane Sugar", updated.Name);
        Assert.Equal(stock, updated.StockLevel);   // stock unchanged by Update
    }
}
