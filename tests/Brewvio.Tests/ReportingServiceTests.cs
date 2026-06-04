using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Services;

namespace Brewvio.Tests;

public class ReportingServiceTests
{
    [Fact]
    public async Task Report_aggregates_sales_and_menu_performance()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var cashier = t.Db.Users.First(u => u.Username == "cashier");
        var cur = TestSupport.Cur(cashier.Id, cashier.Username, cashier.Role);
        var audit = new AuditService(t.Db, cur);
        var orders = new OrderService(t.Db, cur, audit, new SettingsService(t.Db, audit));
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");

        for (var i = 0; i < 2; i++)
            await orders.CreateAsync(new CreateOrderRequest(
                new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) }, 0m,
                new List<PaymentInput> { new("Cash", 200m) }));

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));

        Assert.Equal(2, report.Summary.TransactionCount);
        Assert.Equal(313.60m, report.Summary.TotalSales);   // 2 x 156.80
        Assert.Contains(report.MenuPerformance, m => m.Name == "Caffe Latte" && m.QuantitySold == 2 && m.Profit > 0);
    }

    [Fact]
    public async Task Report_includes_margin_best_sellers_and_category_breakdown()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var cashier = t.Db.Users.First(u => u.Username == "cashier");
        var cur = TestSupport.Cur(cashier.Id, cashier.Username, cashier.Role);
        var audit = new AuditService(t.Db, cur);
        var orders = new OrderService(t.Db, cur, audit, new SettingsService(t.Db, audit));
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");
        var espresso = t.Db.MenuItems.First(m => m.Name == "Espresso");

        for (var i = 0; i < 3; i++)
            await orders.CreateAsync(new CreateOrderRequest(
                new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) }, 0m,
                new List<PaymentInput> { new("Cash", 200m) }));
        await orders.CreateAsync(new CreateOrderRequest(
            new List<CartItemInput> { new(espresso.Id, 1, new List<int>(), null) }, 0m,
            new List<PaymentInput> { new("Cash", 200m) }));

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1), "daily");

        // Best seller is the latte (sold 3x).
        Assert.Equal("Caffe Latte", report.BestSellers.First().Name);
        Assert.True(report.Summary.ProfitMarginPercent > 0);
        Assert.Equal(4, report.Summary.ItemsSold);
        Assert.NotEmpty(report.CategoryBreakdown);
        Assert.All(report.MenuPerformance, m => Assert.InRange(m.MarginPercent, -100m, 100m));
    }

    [Fact]
    public async Task Report_period_buckets_trend_by_month()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var cashier = t.Db.Users.First(u => u.Username == "cashier");
        var cur = TestSupport.Cur(cashier.Id, cashier.Username, cashier.Role);
        var audit = new AuditService(t.Db, cur);
        var orders = new OrderService(t.Db, cur, audit, new SettingsService(t.Db, audit));
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");

        await orders.CreateAsync(new CreateOrderRequest(
            new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) }, 0m,
            new List<PaymentInput> { new("Cash", 200m) }));

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date.AddDays(-40), DateTime.UtcNow.Date.AddDays(1), "monthly");

        Assert.Equal("monthly", report.Period);
        // A monthly label looks like "yyyy-MM".
        Assert.All(report.Trend, p => Assert.Matches(@"^\d{4}-\d{2}$", p.Label));
    }
}
