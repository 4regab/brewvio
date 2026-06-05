using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Services;

namespace Brewvio.Tests;

/// <summary>
/// Extended reporting tests covering:
/// weekly / yearly period buckets, empty date range, AverageOrderValue,
/// SlowSellers ordering, TotalDiscounts + TotalTax in summary,
/// zero-sale range returns safe defaults, and multiple trend points.
///
/// Note: ReportingService only counts "Completed" transactions, so every
/// order must be advanced (Preparing → Completed) before asserting.
/// </summary>
public class ReportingServiceExtendedTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static OrderService BuildOrders(TestScope t)
    {
        var cashier = t.Db.Users.First(u => u.Username == "cashier");
        var cur = TestSupport.Cur(cashier.Id, cashier.Username, cashier.Role);
        var audit = new AuditService(t.Db, cur);
        return new OrderService(t.Db, cur, audit, new SettingsService(t.Db, audit));
    }

    /// <summary>Creates an order AND advances it to Completed so the reporter sees it.</summary>
    private static async Task<ReceiptDto> CompleteOrder(OrderService svc, CreateOrderRequest req)
    {
        var receipt = await svc.CreateAsync(req);
        await svc.AdvanceStatusAsync(receipt.TransactionId);
        return receipt;
    }

    private static List<CartItemInput> Cart(int id, int qty = 1) =>
        new() { new CartItemInput(id, qty, new List<int>(), null) };

    // ── Empty range ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Report_with_no_transactions_returns_zero_summary()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date.AddDays(-7), DateTime.UtcNow.Date.AddDays(-1));

        Assert.Equal(0m, report.Summary.TotalSales);
        Assert.Equal(0, report.Summary.TransactionCount);
        Assert.Equal(0m, report.Summary.AverageOrderValue);
        Assert.Equal(0, report.Summary.ItemsSold);
        Assert.Empty(report.Trend);
        Assert.Empty(report.MenuPerformance);
        Assert.Empty(report.BestSellers);
        Assert.Empty(report.CategoryBreakdown);
        Assert.Equal(0m, report.Summary.ProfitMarginPercent);
    }

    // ── AverageOrderValue ─────────────────────────────────────────────────────

    [Fact]
    public async Task Report_computes_average_order_value_correctly()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var orders = BuildOrders(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");
        var espresso = t.Db.MenuItems.First(m => m.Name == "Espresso");

        await CompleteOrder(orders, new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));
        await CompleteOrder(orders, new CreateOrderRequest(Cart(espresso.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));

        var expected = Math.Round(report.Summary.TotalSales / 2, 2);
        Assert.Equal(expected, report.Summary.AverageOrderValue);
    }

    // ── TotalDiscounts + TotalTax ─────────────────────────────────────────────

    [Fact]
    public async Task Report_accumulates_total_discounts_and_tax()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var orders = BuildOrders(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte"); // ₱140

        // First order with ₱10 discount, second without
        await CompleteOrder(orders, new CreateOrderRequest(Cart(latte.Id), 10m, new List<PaymentInput> { new("Cash", 500m) }));
        await CompleteOrder(orders, new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));

        Assert.Equal(10m, report.Summary.TotalDiscounts);
        Assert.True(report.Summary.TotalTax > 0m);
    }

    // ── SlowSellers ordering ──────────────────────────────────────────────────

    [Fact]
    public async Task Report_slow_sellers_are_ordered_ascending_by_quantity_sold()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var orders = BuildOrders(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");
        var espresso = t.Db.MenuItems.First(m => m.Name == "Espresso");

        for (var i = 0; i < 3; i++)
            await CompleteOrder(orders, new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));
        await CompleteOrder(orders, new CreateOrderRequest(Cart(espresso.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));

        Assert.Equal("Espresso", report.SlowSellers.First().Name);
        for (var i = 1; i < report.SlowSellers.Count; i++)
            Assert.True(report.SlowSellers[i - 1].QuantitySold <= report.SlowSellers[i].QuantitySold);
    }

    // ── Weekly period buckets ─────────────────────────────────────────────────

    [Fact]
    public async Task Report_weekly_trend_labels_are_formatted_as_mon_dd()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var orders = BuildOrders(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");

        await CompleteOrder(orders, new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date.AddDays(-7), DateTime.UtcNow.Date.AddDays(1), "weekly");

        Assert.Equal("weekly", report.Period);
        Assert.NotEmpty(report.Trend);
        Assert.All(report.Trend, p => Assert.Matches(@"^[A-Z][a-z]{2} \d{2}$", p.Label));
    }

    // ── Yearly period buckets ─────────────────────────────────────────────────

    [Fact]
    public async Task Report_yearly_trend_label_is_current_year()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var orders = BuildOrders(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");

        await CompleteOrder(orders, new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date.AddDays(-365), DateTime.UtcNow.Date.AddDays(1), "yearly");

        Assert.Equal("yearly", report.Period);
        Assert.All(report.Trend, p => Assert.Matches(@"^\d{4}$", p.Label));
        Assert.Contains(report.Trend, p => p.Label == DateTime.UtcNow.Year.ToString());
    }

    // ── Daily period with multiple points ─────────────────────────────────────

    [Fact]
    public async Task Report_daily_trend_has_one_point_per_transaction_day()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var orders = BuildOrders(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");

        await CompleteOrder(orders, new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));
        await CompleteOrder(orders, new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1), "daily");

        Assert.Single(report.Trend);
        Assert.Equal(2, report.Trend[0].TransactionCount);
    }

    // ── CategoryBreakdown ─────────────────────────────────────────────────────

    [Fact]
    public async Task Report_category_breakdown_sums_revenue_correctly()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var orders = BuildOrders(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte"); // Espresso, ₱140

        for (var i = 0; i < 3; i++)
            await CompleteOrder(orders, new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));

        var espressoCat = report.CategoryBreakdown.First(c => c.Category == "Espresso");
        Assert.Equal(3, espressoCat.QuantitySold);
        Assert.Equal(3 * 140m, espressoCat.Revenue);
    }

    [Fact]
    public async Task Report_category_breakdown_ordered_by_revenue_descending()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var orders = BuildOrders(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");
        var espresso = t.Db.MenuItems.First(m => m.Name == "Espresso");

        for (var i = 0; i < 5; i++)
            await CompleteOrder(orders, new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));
        await CompleteOrder(orders, new CreateOrderRequest(Cart(espresso.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));

        for (var i = 1; i < report.CategoryBreakdown.Count; i++)
            Assert.True(report.CategoryBreakdown[i - 1].Revenue >= report.CategoryBreakdown[i].Revenue);
    }

    // ── Profit margin ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Report_profit_margin_is_between_0_and_100_for_normal_orders()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var orders = BuildOrders(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");

        await CompleteOrder(orders, new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));

        Assert.InRange(report.Summary.ProfitMarginPercent, 0m, 100m);
    }

    // ── Trend sales values ────────────────────────────────────────────────────

    [Fact]
    public async Task Report_daily_trend_sales_match_transaction_totals()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var orders = BuildOrders(t);
        var espresso = t.Db.MenuItems.First(m => m.Name == "Espresso"); // ₱100 + 12% = ₱112

        await CompleteOrder(orders, new CreateOrderRequest(Cart(espresso.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));

        Assert.Equal(112m, report.Trend[0].Sales);
        Assert.Equal(report.Summary.TotalSales, report.Trend.Sum(p => p.Sales));
    }

    // ── Best seller count ─────────────────────────────────────────────────────

    [Fact]
    public async Task Report_best_sellers_limited_to_five()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var orders = BuildOrders(t);

        var items = t.Db.MenuItems.Where(m => m.IsActive).Take(10).ToList();
        foreach (var item in items)
            await CompleteOrder(orders, new CreateOrderRequest(Cart(item.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));

        Assert.True(report.BestSellers.Count <= 5);
    }

    // ── Unknown period defaults to daily ─────────────────────────────────────

    [Fact]
    public async Task Report_unknown_period_defaults_to_daily()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var orders = BuildOrders(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");

        await CompleteOrder(orders, new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 500m) }));

        var report = await new ReportingService(t.Db).GenerateAsync(
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1), "bogus");

        Assert.Equal("bogus", report.Period);
        Assert.All(report.Trend, p => Assert.Matches(@"^[A-Z][a-z]{2} \d{2}$", p.Label));
    }
}
