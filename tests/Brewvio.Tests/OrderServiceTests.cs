using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Services;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Tests;

public class OrderServiceTests
{
    private static (OrderService svc, BrewvioDbContext db) Build(TestDb t)
    {
        var cashier = t.Db.Users.First(u => u.Username == "cashier");
        var cur = TestSupport.Cur(cashier.Id, cashier.Username, cashier.Role);
        var audit = new AuditService(t.Db, cur);
        return (new OrderService(t.Db, cur, audit, new SettingsService(t.Db, audit)), t.Db);
    }

    // Builds an OrderService over a specific context (used to simulate two concurrent requests,
    // each with its own DbContext, mirroring two Lambda instances hitting the same inventory).
    private static OrderService BuildOn(BrewvioDbContext db, int cashierId)
    {
        var cur = TestSupport.Cur(cashierId, "cashier", "Cashier");
        var audit = new AuditService(db, cur);
        return new OrderService(db, cur, audit, new SettingsService(db, audit));
    }

    private static List<CartItemInput> Cart(int menuItemId, int qty) =>
        new() { new CartItemInput(menuItemId, qty, new List<int>(), null) };

    [Fact]
    public async Task CreateOrder_computes_totals_with_tax_and_deducts_recipe_ingredients()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte"); // 140, recipe: milk 200, beans 18, cup 1
        decimal Stock(string n) { using var v = t.NewContext(); return v.Ingredients.First(i => i.Name == n).StockLevel; }
        var milk0 = Stock("Whole Milk"); var beans0 = Stock("Espresso Beans"); var cup0 = Stock("Paper Cup (12oz)");

        var receipt = await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id, 2), 0m,
            new List<PaymentInput> { new("Cash", 500m) }));

        Assert.Equal("Completed", receipt.Status);
        Assert.Equal(280m, receipt.Subtotal);
        Assert.Equal(33.60m, receipt.TaxAmount);    // 12% of 280
        Assert.Equal(313.60m, receipt.TotalAmount);
        Assert.Equal(186.40m, receipt.Change);      // 500 tendered
        Assert.Equal("Cash", receipt.PaymentMethod);

        Assert.Equal(milk0 - 400m, Stock("Whole Milk"));
        Assert.Equal(beans0 - 36m, Stock("Espresso Beans"));
        Assert.Equal(cup0 - 2m, Stock("Paper Cup (12oz)"));

        using var verify = t.NewContext();
        Assert.Equal(1, await verify.Transactions.CountAsync());
        Assert.Single(verify.Payments);
    }

    [Fact]
    public async Task CreateOrder_supports_split_payment()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte"); // total 156.80

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
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(
            new CreateOrderRequest(Cart(latte.Id, 1), 0m, new List<PaymentInput> { new("Cash", 10m) })));

        using var verify = t.NewContext();
        Assert.Equal(0, await verify.Transactions.CountAsync()); // nothing persisted on failure
    }

    [Fact]
    public async Task CreateOrder_applies_discount_before_tax()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte"); // 140

        var receipt = await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id, 1), 40m,
            new List<PaymentInput> { new("Cash", 200m) }));

        Assert.Equal(40m, receipt.DiscountAmount);
        Assert.Equal(12.00m, receipt.TaxAmount);     // 12% of (140 - 40)
        Assert.Equal(112.00m, receipt.TotalAmount);
    }

    [Fact]
    public async Task Refund_restocks_ingredients_and_marks_refunded()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");
        decimal Stock(string n) { using var v = t.NewContext(); return v.Ingredients.First(i => i.Name == n).StockLevel; }

        var sale = await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id, 1), 0m, new List<PaymentInput> { new("Cash", 200m) }));
        var afterSale = Stock("Whole Milk");

        var refunded = await svc.RefundAsync(sale.TransactionId, "Customer changed their mind");

        Assert.NotNull(refunded);
        Assert.Equal("Refunded", refunded!.Status);
        Assert.Equal(afterSale + 200m, Stock("Whole Milk"));
    }

    [Fact]
    public async Task Concurrent_orders_on_same_ingredient_do_not_lose_a_deduction()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var cashierId = t.Db.Users.First(u => u.Username == "cashier").Id;
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte"); // recipe: Whole Milk 200ml
        decimal Stock(string n) { using var v = t.NewContext(); return v.Ingredients.First(i => i.Name == n).StockLevel; }
        var milk0 = Stock("Whole Milk");

        // Two independent contexts read the same starting stock, then both commit a sale. Without
        // the xmin concurrency token one write would silently clobber the other (lost update);
        // with it, the second SaveChanges conflicts, reloads, and re-applies — so both deductions land.
        using var ctxA = t.NewContext();
        using var ctxB = t.NewContext();
        var svcA = BuildOn(ctxA, cashierId);
        var svcB = BuildOn(ctxB, cashierId);

        var ingA = ctxA.Ingredients.First(i => i.Name == "Whole Milk");
        var ingB = ctxB.Ingredients.First(i => i.Name == "Whole Milk");  // both load before either saves
        Assert.Equal(ingA.StockLevel, ingB.StockLevel);

        await svcA.CreateAsync(new CreateOrderRequest(Cart(latte.Id, 1), 0m, new List<PaymentInput> { new("Cash", 200m) }));
        await svcB.CreateAsync(new CreateOrderRequest(Cart(latte.Id, 1), 0m, new List<PaymentInput> { new("Cash", 200m) }));

        // Both 200ml deductions must be reflected — total 400ml off, not 200ml.
        Assert.Equal(milk0 - 400m, Stock("Whole Milk"));
        using var verify = t.NewContext();
        Assert.Equal(2, await verify.Transactions.CountAsync());
    }
}
