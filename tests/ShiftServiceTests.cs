using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Services;

namespace Brewvio.Tests;

public class ShiftServiceTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static (ShiftService shifts, OrderService orders) Build(TestScope t)
    {
        var cashier = t.Db.Users.First(u => u.Username == "cashier");
        var cur = TestSupport.Cur(cashier.Id, cashier.Username, cashier.Role);
        var audit = new AuditService(t.Db, cur);
        return (new ShiftService(t.Db, cur, audit),
                new OrderService(t.Db, cur, audit, new SettingsService(t.Db, audit)));
    }

    [Fact]
    public async Task Shift_tracks_sales_and_closes_with_zero_variance()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (shifts, orders) = Build(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");

        await shifts.StartAsync(1000m);
        await orders.CreateAsync(new CreateOrderRequest(
            new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) }, 0m,
            new List<PaymentInput> { new("Cash", 156.80m) }));

        var current = await shifts.GetCurrentAsync();
        Assert.NotNull(current);
        Assert.Equal(1, current!.TransactionCount);
        Assert.Equal(156.80m, current.CashSales);
        Assert.Equal(1156.80m, current.ExpectedCash);

        var closed = await shifts.EndAsync(current.ExpectedCash);
        Assert.NotNull(closed);
        Assert.Equal("Closed", closed!.Status);
        Assert.Equal(0m, closed.CashVariance);
    }

    [Fact]
    public async Task Start_is_idempotent_when_shift_already_open()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (shifts, _) = Build(t);

        var first = await shifts.StartAsync(500m);
        var second = await shifts.StartAsync(999m);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(500m, second.StartingCash);
        Assert.Equal(1, t.Db.Shifts.Count());
    }

    [Fact]
    public async Task End_returns_null_when_no_open_shift()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (shifts, _) = Build(t);

        Assert.Null(await shifts.EndAsync(500m));
    }

    [Fact]
    public async Task GetCurrent_returns_null_when_no_open_shift()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (shifts, _) = Build(t);

        Assert.Null(await shifts.GetCurrentAsync());
    }

    [Fact]
    public async Task Closed_shift_reports_non_zero_variance_when_cash_differs()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (shifts, _) = Build(t);

        await shifts.StartAsync(1000m);
        var closed = await shifts.EndAsync(900m);

        Assert.NotNull(closed);
        Assert.Equal(-100m, closed!.CashVariance);
    }
}
