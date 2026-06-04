using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Services;

namespace Brewvio.Tests;

public class ShiftServiceTests
{
    [Fact]
    public async Task Shift_tracks_sales_and_closes_with_zero_variance()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var cashier = t.Db.Users.First(u => u.Username == "cashier");
        var cur = TestSupport.Cur(cashier.Id, cashier.Username, cashier.Role);
        var audit = new AuditService(t.Db, cur);
        var shifts = new ShiftService(t.Db, cur, audit);
        var orders = new OrderService(t.Db, cur, audit, new SettingsService(t.Db, audit));
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");

        await shifts.StartAsync(1000m);
        await orders.CreateAsync(new CreateOrderRequest(
            new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) }, 0m,
            new List<PaymentInput> { new("Cash", 156.80m) }));

        var current = await shifts.GetCurrentAsync();
        Assert.NotNull(current);
        Assert.Equal(1, current!.TransactionCount);
        Assert.Equal(156.80m, current.CashSales);
        Assert.Equal(1156.80m, current.ExpectedCash);   // starting 1000 + cash sales

        var closed = await shifts.EndAsync(current.ExpectedCash);
        Assert.NotNull(closed);
        Assert.Equal("Closed", closed!.Status);
        Assert.Equal(0m, closed.CashVariance);
    }
}
