using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Services;

namespace Brewvio.Tests;

public class ReportingServiceTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static OrderService BuildOrders(TestScope t)
    {
        var cashier = t.Db.Users.First(u => u.Username == "cashier");
        var cur = TestSupport.Cur(cashier.Id, cashier.Username, cashier.Role);
        var audit = new AuditService(t.Db, cur);
        return new OrderService(t.Db, cur, audit, new SettingsService(t.Db, audit));
    }

    [Fact]
    public async Task Report_aggregates_sales_and_menu_performance()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        SettingsService.ResetTaxRateCache();
        var orders = BuildOrders(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");

        for (var i = 0; i < 2; i++)
        {
            var r = await orders.CreateAsync(new CreateOrderRequest(
                new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) }, 0m,
                new List<PaymentInput> { new("Cash", 200m) }));
            await orders.AdvanceStatusAsync(r.TransactionId); // → Completed
        }

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));

        Assert.Equal(2, report.Summary.TransactionCount);
        Assert.Equal(280m, report.Summary.TotalSales);
        Assert.Contains(report.MenuPerformance, m => m.Name == "Caffe Latte" && m.QuantitySold == 2 && m.Profit > 0);
    }

    [Fact]
    public async Task Report_includes_margin_best_sellers_and_category_breakdown()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        SettingsService.ResetTaxRateCache();
        var orders = BuildOrders(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");
        var espresso = t.Db.MenuItems.First(m => m.Name == "Espresso");

        for (var i = 0; i < 3; i++)
        {
            var r = await orders.CreateAsync(new CreateOrderRequest(
                new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) }, 0m,
                new List<PaymentInput> { new("Cash", 200m) }));
            await orders.AdvanceStatusAsync(r.TransactionId);
        }
        var r2 = await orders.CreateAsync(new CreateOrderRequest(
            new List<CartItemInput> { new(espresso.Id, 1, new List<int>(), null) }, 0m,
            new List<PaymentInput> { new("Cash", 200m) }));
        await orders.AdvanceStatusAsync(r2.TransactionId);

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1), "daily");

        Assert.Equal("Caffe Latte", report.BestSellers.First().Name);
        Assert.True(report.Summary.ProfitMarginPercent > 0);
        Assert.Equal(4, report.Summary.ItemsSold);
        Assert.NotEmpty(report.CategoryBreakdown);
        Assert.All(report.MenuPerformance, m => Assert.InRange(m.MarginPercent, -100m, 100m));
    }

    [Fact]
    public async Task Report_computes_costs_on_a_fresh_context_without_tracked_ingredients()
    {
        // Regression: ReportingService computes recipe cost via an in-memory Sum over
        // r.Ingredient.CostPerUnit, so the Ingredient navigation must be eagerly loaded.
        // The other tests run GenerateAsync on the SAME context that seeded the data, so EF
        // relationship-fixup populates r.Ingredient and masks a missing Include. Production uses
        // a fresh per-request context (no fixup) — exactly what NewContext() reproduces here.
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        SettingsService.ResetTaxRateCache();
        var orders = BuildOrders(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");

        var r = await orders.CreateAsync(new CreateOrderRequest(
            new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) }, 0m,
            new List<PaymentInput> { new("Cash", 200m) }));
        await orders.AdvanceStatusAsync(r.TransactionId); // → Completed

        // Fresh context: Ingredients are NOT tracked, so a missing Include would NRE here.
        await using var fresh = t.NewContext();
        var report = await new ReportingService(fresh).GenerateAsync(
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));

        var latteRow = Assert.Single(report.MenuPerformance, m => m.Name == "Caffe Latte");
        Assert.Equal(1, latteRow.QuantitySold);
        Assert.True(latteRow.Profit > 0, "recipe cost should be computed (Ingredient loaded), giving a positive profit");
    }

    [Fact]
    public async Task Report_period_buckets_trend_by_month()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        SettingsService.ResetTaxRateCache();
        var orders = BuildOrders(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");

        var r = await orders.CreateAsync(new CreateOrderRequest(
            new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) }, 0m,
            new List<PaymentInput> { new("Cash", 200m) }));
        await orders.AdvanceStatusAsync(r.TransactionId);

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date.AddDays(-40), DateTime.UtcNow.Date.AddDays(1), "monthly");

        Assert.Equal("monthly", report.Period);
        Assert.All(report.Trend, p => Assert.Matches(@"^\d{4}-\d{2}$", p.Label));
    }
}
