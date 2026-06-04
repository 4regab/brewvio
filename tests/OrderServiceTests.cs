using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Services;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Tests;

public class OrderServiceTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
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
    public async Task CreateOrder_computes_totals_with_tax_and_deducts_recipe_ingredients()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");
        decimal Stock(string n) => t.NewContext().Ingredients.First(i => i.Name == n).StockLevel;
        var milk0 = Stock("Whole Milk"); var beans0 = Stock("Espresso Beans"); var cup0 = Stock("Paper Cup (12oz)");

        var receipt = await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id, 2), 0m,
            new List<PaymentInput> { new("Cash", 500m) }));

        Assert.Equal("Completed", receipt.Status);
        Assert.Equal(280m, receipt.Subtotal);
        Assert.Equal(33.60m, receipt.TaxAmount);
        Assert.Equal(313.60m, receipt.TotalAmount);
        Assert.Equal(186.40m, receipt.Change);
        Assert.Equal("Cash", receipt.PaymentMethod);

        Assert.Equal(milk0 - 400m, Stock("Whole Milk"));
        Assert.Equal(beans0 - 36m, Stock("Espresso Beans"));
        Assert.Equal(cup0 - 2m, Stock("Paper Cup (12oz)"));

        var verify = t.NewContext();
        Assert.Equal(1, await verify.Transactions.CountAsync());
        Assert.Single(verify.Payments);
    }

    [Fact]
    public async Task CreateOrder_supports_split_payment()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        var receipt = await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id, 1), 0m,
            new List<PaymentInput> { new("Cash", 100m), new("Card", 56.80m) }));

        Assert.Equal("Split", receipt.PaymentMethod);
        Assert.Equal(156.80m, receipt.TotalAmount);
        Assert.Equal(0m, receipt.Change);
        Assert.Equal(2, receipt.Payments.Count);
    }

    [Fact]
    public async Task CreateOrder_rejects_insufficient_payment()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(
            new CreateOrderRequest(Cart(latte.Id, 1), 0m, new List<PaymentInput> { new("Cash", 10m) })));

        var verify = t.NewContext();
        Assert.Equal(0, await verify.Transactions.CountAsync());
    }

    [Fact]
    public async Task CreateOrder_applies_discount_before_tax()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        var receipt = await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id, 1), 40m,
            new List<PaymentInput> { new("Cash", 200m) }));

        Assert.Equal(40m, receipt.DiscountAmount);
        Assert.Equal(12.00m, receipt.TaxAmount);
        Assert.Equal(112.00m, receipt.TotalAmount);
    }

    [Fact]
    public async Task Refund_restocks_ingredients_and_marks_refunded()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");
        decimal Stock(string n) => t.NewContext().Ingredients.First(i => i.Name == n).StockLevel;

        var sale = await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id, 1), 0m,
            new List<PaymentInput> { new("Cash", 200m) }));
        var afterSale = Stock("Whole Milk");

        var refunded = await svc.RefundAsync(sale.TransactionId, "Customer changed their mind");

        Assert.NotNull(refunded);
        Assert.Equal("Refunded", refunded!.Status);
        Assert.Equal(afterSale + 200m, Stock("Whole Milk"));
    }

    [Fact]
    public async Task CreateOrder_rejects_empty_cart()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (svc, _) = Build(t);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateAsync(new CreateOrderRequest(new List<CartItemInput>(), 0m,
                new List<PaymentInput> { new("Cash", 100m) })));
    }

    [Fact]
    public async Task CreateOrder_rejects_inactive_menu_item()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");
        latte.IsActive = false;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id, 1), 0m,
                new List<PaymentInput> { new("Cash", 200m) })));
    }

    [Fact]
    public async Task Refund_of_already_refunded_transaction_throws()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");
        var sale = await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id, 1), 0m,
            new List<PaymentInput> { new("Cash", 200m) }));

        await svc.RefundAsync(sale.TransactionId, "First refund");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefundAsync(sale.TransactionId, "Double refund attempt"));
    }

    [Fact]
    public async Task Refund_with_empty_reason_throws()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");
        var sale = await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id, 1), 0m,
            new List<PaymentInput> { new("Cash", 200m) }));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.RefundAsync(sale.TransactionId, "   "));
    }

    [Fact]
    public async Task Cancel_writes_audit_entry()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (svc, _) = Build(t);

        await svc.CancelAsync("Customer walked away");

        var verify = t.NewContext();
        Assert.Contains(await verify.AuditLogs.ToListAsync(),
            a => a.Action == "OrderCancelled" && a.Details.Contains("Customer walked away"));
    }

    // Concurrent orders test requires two INDEPENDENT connections — keep using TestDb for this one.
    [Fact]
    public async Task Concurrent_orders_on_same_ingredient_do_not_lose_a_deduction()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var cashierId = t.Db.Users.First(u => u.Username == "cashier").Id;
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");
        decimal Stock(string n) { using var v = t.NewContext(); return v.Ingredients.First(i => i.Name == n).StockLevel; }
        var milk0 = Stock("Whole Milk");

        using var ctxA = t.NewContext();
        using var ctxB = t.NewContext();

        OrderService BuildOn(BrewvioDbContext db)
        {
            var cur = TestSupport.Cur(cashierId, "cashier", "Cashier");
            var audit = new AuditService(db, cur);
            return new OrderService(db, cur, audit, new SettingsService(db, audit));
        }

        var ingA = ctxA.Ingredients.First(i => i.Name == "Whole Milk");
        var ingB = ctxB.Ingredients.First(i => i.Name == "Whole Milk");
        Assert.Equal(ingA.StockLevel, ingB.StockLevel);

        await BuildOn(ctxA).CreateAsync(new CreateOrderRequest(
            new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) }, 0m,
            new List<PaymentInput> { new("Cash", 200m) }));
        await BuildOn(ctxB).CreateAsync(new CreateOrderRequest(
            new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) }, 0m,
            new List<PaymentInput> { new("Cash", 200m) }));

        Assert.Equal(milk0 - 400m, Stock("Whole Milk"));
        using var verify = t.NewContext();
        Assert.Equal(2, await verify.Transactions.CountAsync());
    }
}
