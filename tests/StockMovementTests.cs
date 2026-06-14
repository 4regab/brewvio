using System.Text;
using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Helpers;
using Brewvio.Models;
using Brewvio.Services;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Tests;

// ── InventoryService: Stock In / Stock Out / History ─────────────────────────
public class StockMovementInventoryTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static InventoryService Build(TestScope t) =>
        new(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

    [Fact]
    public async Task StockIn_adds_quantity_and_logs_movement_with_ingredient_id()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");
        var before = milk.StockLevel;

        var dto = await svc.StockInAsync(milk.Id, new StockMovementRequest(250m, "Delivery"));

        Assert.Equal(before + 250m, dto!.StockLevel);
        var verify = t.NewContext();
        Assert.Equal(before + 250m, (await verify.Ingredients.FindAsync(milk.Id))!.StockLevel);
        Assert.Contains(await verify.AuditLogs.ToListAsync(),
            a => a.Action == "StockIn" && a.IngredientId == milk.Id && a.Details.Contains("+250"));
    }

    [Fact]
    public async Task StockOut_removes_quantity_and_logs_movement_with_ingredient_id()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");
        var before = milk.StockLevel;

        var dto = await svc.StockOutAsync(milk.Id, new StockMovementRequest(300m, "Spillage"));

        Assert.Equal(before - 300m, dto!.StockLevel);
        var verify = t.NewContext();
        Assert.Contains(await verify.AuditLogs.ToListAsync(),
            a => a.Action == "StockOut" && a.IngredientId == milk.Id && a.Details.Contains("Spillage"));
    }

    [Fact]
    public async Task StockOut_rejects_more_than_on_hand()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");

        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            svc.StockOutAsync(milk.Id, new StockMovementRequest(milk.StockLevel + 1m, "too much")));

        // Stock unchanged; no movement row written.
        var verify = t.NewContext();
        Assert.Equal(milk.StockLevel, (await verify.Ingredients.FindAsync(milk.Id))!.StockLevel);
        Assert.DoesNotContain(await verify.AuditLogs.ToListAsync(), a => a.Action == "StockOut");
    }

    [Fact]
    public async Task StockOut_requires_a_reason()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Build(t).StockOutAsync(milk.Id, new StockMovementRequest(10m, "   ")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Stock_moves_reject_non_positive_quantity(decimal qty)
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Build(t).StockInAsync(milk.Id, new StockMovementRequest(qty, "x")));
    }

    [Fact]
    public async Task Stock_moves_return_null_for_missing_ingredient()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        Assert.Null(await Build(t).StockInAsync(999_999, new StockMovementRequest(5m, null)));
        Assert.Null(await Build(t).StockOutAsync(999_999, new StockMovementRequest(5m, "r")));
    }

    [Fact]
    public async Task History_returns_only_this_ingredients_stock_movements()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");
        var sugar = t.Db.Ingredients.First(i => i.Name == "Sugar");

        await svc.StockInAsync(milk.Id, new StockMovementRequest(100m, "in"));
        await svc.StockOutAsync(milk.Id, new StockMovementRequest(40m, "out"));
        await svc.StockInAsync(sugar.Id, new StockMovementRequest(10m, "sugar in"));

        var history = await svc.HistoryAsync(milk.Id);

        Assert.Equal(2, history.Count);
        Assert.Contains(history, m => m.Action == "StockIn");
        Assert.Contains(history, m => m.Action == "StockOut");
        Assert.DoesNotContain(history, m => m.Details.Contains("sugar in"));
    }

    [Fact]
    public async Task History_excludes_non_stock_actions_even_when_tagged_with_ingredient()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");

        // A non-stock audit row tagged with the ingredient must NOT appear in stock history.
        t.Db.AuditLogs.Add(new AuditLog { Action = "IngredientUpdated", Details = "edited", IngredientId = milk.Id });
        await t.Db.SaveChangesAsync();
        await svc.StockInAsync(milk.Id, new StockMovementRequest(5m, "in"));

        var history = await svc.HistoryAsync(milk.Id);

        Assert.All(history, m => Assert.NotEqual("IngredientUpdated", m.Action));
        Assert.Contains(history, m => m.Action == "StockIn");
    }
}

// ── AuditService: per-ingredient sale/refund rows are hidden from the general log ──
public class StockMovementAuditTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    [Fact]
    public async Task ListAsync_excludes_stock_sale_and_refund_rows()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var audit = new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager"));

        audit.Add("StockSale", "milk -200 (sale)", 1);
        audit.Add("StockRefund", "milk +200 (refund)", 1);
        audit.Add("StockIn", "milk +500", 1);
        audit.Add("OrderPlaced", "txn total");
        await t.Db.SaveChangesAsync();

        var logs = await audit.ListAsync();

        Assert.DoesNotContain(logs, a => a.Action == "StockSale");
        Assert.DoesNotContain(logs, a => a.Action == "StockRefund");
        Assert.Contains(logs, a => a.Action == "StockIn");
        Assert.Contains(logs, a => a.Action == "OrderPlaced");
    }
}

// ── OrderService: per-ingredient sale & refund movement rows ──────────────────
public class StockMovementOrderTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static (OrderService svc, BrewvioDbContext db) Build(TestScope t)
    {
        var cashier = t.Db.Users.First(u => u.Username == "cashier");
        var cur = TestSupport.Cur(cashier.Id, cashier.Username, cashier.Role);
        var audit = new AuditService(t.Db, cur);
        return (new OrderService(t.Db, cur, audit, new SettingsService(t.Db, audit)), t.Db);
    }

    private static List<CartItemInput> Cart(int menuItemId, int qty) =>
        new() { new CartItemInput(menuItemId, qty, new List<int>(), null) };

    [Fact]
    public async Task Sale_writes_one_StockSale_movement_per_consumed_ingredient()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        SettingsService.ResetTaxRateCache();
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");
        var milkId = db.Ingredients.First(i => i.Name == "Whole Milk").Id;

        var receipt = await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id, 1), 0m,
            new List<PaymentInput> { new("Cash", 200m) }));

        var verify = t.NewContext();
        var sales = await verify.AuditLogs.Where(a => a.Action == "StockSale").ToListAsync();
        Assert.NotEmpty(sales);
        Assert.All(sales, s => Assert.NotNull(s.IngredientId));
        Assert.Contains(sales, s => s.IngredientId == milkId
            && s.Details.Contains($"Txn #{receipt.TransactionId}"));
    }

    [Fact]
    public async Task Refund_writes_one_StockRefund_movement_per_restored_ingredient_and_restores_stock()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        SettingsService.ResetTaxRateCache();
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");
        var milkId = db.Ingredients.First(i => i.Name == "Whole Milk").Id;
        decimal Stock() => t.NewContext().Ingredients.First(i => i.Id == milkId).StockLevel;
        var milk0 = Stock();

        var receipt = await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id, 1), 0m,
            new List<PaymentInput> { new("Cash", 200m) }));
        await svc.AdvanceStatusAsync(receipt.TransactionId);          // Preparing -> Completed
        await svc.RefundAsync(receipt.TransactionId, "customer changed mind");

        Assert.Equal(milk0, Stock());                                 // stock fully restored
        var verify = t.NewContext();
        var refunds = await verify.AuditLogs.Where(a => a.Action == "StockRefund").ToListAsync();
        Assert.NotEmpty(refunds);
        Assert.All(refunds, r => Assert.NotNull(r.IngredientId));
        Assert.Contains(refunds, r => r.IngredientId == milkId
            && r.Details.Contains($"Txn #{receipt.TransactionId}"));
    }
}


// ── Structured ledger columns: signed Quantity + BalanceAfter ────────────────
public class StockLedgerColumnsTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static InventoryService Build(TestScope t) =>
        new(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

    [Fact]
    public async Task StockIn_records_positive_quantity_and_balance()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");
        var before = milk.StockLevel;

        await Build(t).StockInAsync(milk.Id, new StockMovementRequest(250m, "delivery"));

        var row = (await Build(t).HistoryAsync(milk.Id)).First();
        Assert.Equal("StockIn", row.Action);
        Assert.Equal(250m, row.Quantity);
        Assert.Equal(before + 250m, row.BalanceAfter);
    }

    [Fact]
    public async Task StockOut_records_negative_quantity_and_balance()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");
        var before = milk.StockLevel;

        await Build(t).StockOutAsync(milk.Id, new StockMovementRequest(40m, "spill"));

        var row = (await Build(t).HistoryAsync(milk.Id)).First();
        Assert.Equal(-40m, row.Quantity);
        Assert.Equal(before - 40m, row.BalanceAfter);
    }

    [Fact]
    public async Task Adjust_records_signed_delta_and_new_balance()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");
        var before = milk.StockLevel;

        await Build(t).AdjustAsync(milk.Id, new StockAdjustRequest(before - 100m, "stock count"));

        var row = (await Build(t).HistoryAsync(milk.Id)).First();
        Assert.Equal("InventoryAdjust", row.Action);
        Assert.Equal(-100m, row.Quantity);          // signed delta (new - old)
        Assert.Equal(before - 100m, row.BalanceAfter);
    }
}

// ── Global ledger: filters + pagination + ingredient projection ──────────────
public class StockMovementsLedgerTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static InventoryService Build(TestScope t) =>
        new(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

    private static async Task SeedMoves(InventoryService svc, int milkId, int sugarId)
    {
        await svc.StockInAsync(milkId, new StockMovementRequest(100m, "in1"));
        await svc.StockOutAsync(milkId, new StockMovementRequest(20m, "out1"));
        await svc.StockInAsync(sugarId, new StockMovementRequest(50m, "in2"));
    }

    [Fact]
    public async Task Movements_filters_by_type()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");
        var sugar = t.Db.Ingredients.First(i => i.Name == "Sugar");
        await SeedMoves(svc, milk.Id, sugar.Id);

        var page = await svc.MovementsAsync(null, null, "StockOut", null, 0, 50);

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, m => Assert.Equal("StockOut", m.Action));
    }

    [Fact]
    public async Task Movements_filters_by_ingredient_and_projects_name_and_code()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");
        var sugar = t.Db.Ingredients.First(i => i.Name == "Sugar");
        await SeedMoves(svc, milk.Id, sugar.Id);

        var page = await svc.MovementsAsync(null, null, null, milk.Id, 0, 50);

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, m => Assert.Equal(milk.Id, m.IngredientId));
        Assert.All(page.Items, m => Assert.Equal("Whole Milk", m.IngredientName));
        Assert.All(page.Items, m => Assert.Equal(milk.Code, m.IngredientCode));
    }

    [Fact]
    public async Task Movements_paginates_and_reports_total()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");
        for (var i = 0; i < 5; i++)
            await svc.StockInAsync(milk.Id, new StockMovementRequest(1m, "x"));

        var page = await svc.MovementsAsync(null, null, null, milk.Id, 0, 2);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(5, page.Total);
        Assert.Equal(0, page.Skip);
        Assert.Equal(2, page.Take);

        var page2 = await svc.MovementsAsync(null, null, null, milk.Id, 4, 2);
        Assert.Single(page2.Items);   // last page has the 5th row only
    }

    [Fact]
    public async Task Movements_date_range_filter_is_respected()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");
        await svc.StockInAsync(milk.Id, new StockMovementRequest(10m, "now"));

        var included = await svc.MovementsAsync(DateTime.UtcNow.AddMinutes(-5), null, null, milk.Id, 0, 50);
        var excluded = await svc.MovementsAsync(DateTime.UtcNow.AddMinutes(5), null, null, milk.Id, 0, 50);

        Assert.NotEmpty(included.Items);
        Assert.Empty(excluded.Items);
    }
}

// ── CSV export ───────────────────────────────────────────────────────────────
public class StockMovementsCsvTests
{
    [Fact]
    public void StockMovementsCsv_has_header_and_signed_rows()
    {
        var rows = new List<StockMovementDto>
        {
            new(1, new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc), 7, "Whole Milk", "ING-01",
                "manager", "StockIn", 250m, 5250m, "Whole Milk: +250 ml (delivery)."),
            new(2, new DateTime(2026, 6, 13, 11, 0, 0, DateTimeKind.Utc), 7, "Whole Milk", "ING-01",
                "manager", "StockOut", -40m, 5210m, "Whole Milk: -40 ml. Reason: spill."),
        };

        var csv = Encoding.UTF8.GetString(ExportHelper.StockMovementsCsv(rows));

        Assert.Contains("\"Quantity\",\"Balance After\"", csv);
        Assert.Contains("Stock In", csv);
        Assert.Contains("Stock Out", csv);
        Assert.Contains("250", csv);
        Assert.Contains("-40", csv);
        Assert.Contains("ING-01", csv);
    }
}
