using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Helpers;
using Brewvio.Models;
using Brewvio.Services;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Tests.Unit;

// BuildLineItems is private static inside OrderService — we test its behaviour
// through the public CreateAsync entry point, backed by an in-memory EF database
// so no PostgreSQL is required. Each test gets a fresh database.
public class BuildLineItemsTests : IAsyncDisposable
{
    private readonly BrewvioDbContext _db;
    private readonly OrderService _svc;

    private static readonly User Cashier = new()
    {
        Id = 1, Username = "cashier", FullName = "", Role = Roles.Cashier,
        Status = UserStatus.Active, IsActive = true,
        PasswordHash = PasswordHasher.Hash("password123")
    };

    public BuildLineItemsTests()
    {
        var opts = new DbContextOptionsBuilder<BrewvioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new BrewvioDbContext(opts);
        _db.Database.EnsureCreated();
        _db.Users.Add(Cashier);
        _db.SaveChanges();

        var currentUser = TestSupport.Cur(Cashier.Id, Cashier.Username, Cashier.Role);
        var audit = new AuditService(_db, currentUser);
        var settings = new SettingsService(_db, audit);
        _svc = new OrderService(_db, currentUser, audit, settings);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private MenuItem ActiveItem(decimal price = 10m, string name = "Latte") => new()
    {
        Name = name, Category = "Drinks", Price = price, IsActive = true,
        Recipe = new List<RecipeIngredient>()
    };

    // Positional record: CartItemInput(MenuItemId, Quantity, ModifierIds, Notes)
    private static CreateOrderRequest CashOrder(int menuItemId, int qty = 1, decimal payment = 100m,
        IReadOnlyList<int>? modIds = null) =>
        new(
            Items: new List<CartItemInput>
            {
                new(menuItemId, qty, modIds ?? Array.Empty<int>(), null)
            },
            DiscountAmount: 0m,
            Payments: new List<PaymentInput> { new("Cash", payment) }
        );

    // ── Quantity validation ───────────────────────────────────────────────────

    [Fact]
    public async Task ZeroQuantity_Throws()
    {
        var item = ActiveItem();
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _svc.CreateAsync(CashOrder(item.Id, qty: 0)));
    }

    [Fact]
    public async Task NegativeQuantity_Throws()
    {
        var item = ActiveItem();
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _svc.CreateAsync(CashOrder(item.Id, qty: -1)));
    }

    // ── Inactive / missing menu item ──────────────────────────────────────────

    [Fact]
    public async Task InactiveMenuItem_Throws()
    {
        var item = new MenuItem
        {
            Name = "Old Item", Category = "Drinks", Price = 5m,
            IsActive = false, Recipe = new List<RecipeIngredient>()
        };
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _svc.CreateAsync(CashOrder(item.Id)));
    }

    [Fact]
    public async Task NonExistentMenuItemId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.CreateAsync(CashOrder(menuItemId: 9999)));
    }

    // ── Modifier price deltas ─────────────────────────────────────────────────

    [Fact]
    public async Task SingleModifier_AddsPositiveDelta()
    {
        var item = ActiveItem(price: 10m);
        _db.MenuItems.Add(item);
        var mod = new Modifier { Name = "Extra Shot", PriceDelta = 1.5m, GroupName = "Add-ons", AppliesTo = "All" };
        _db.Modifiers.Add(mod);
        await _db.SaveChangesAsync();

        var receipt = await _svc.CreateAsync(CashOrder(item.Id, modIds: new[] { mod.Id }));

        Assert.Equal(11.5m, receipt.Items[0].UnitPrice);
        Assert.Equal(11.5m, receipt.Subtotal);
    }

    [Fact]
    public async Task SingleModifier_NegativeDelta_ReducesPrice()
    {
        var item = ActiveItem(price: 10m);
        _db.MenuItems.Add(item);
        var mod = new Modifier { Name = "Small Size", PriceDelta = -2m, GroupName = "Size", AppliesTo = "All" };
        _db.Modifiers.Add(mod);
        await _db.SaveChangesAsync();

        var receipt = await _svc.CreateAsync(CashOrder(item.Id, modIds: new[] { mod.Id }));

        Assert.Equal(8m, receipt.Items[0].UnitPrice);
    }

    [Fact]
    public async Task MultipleModifiers_DeltasSummed()
    {
        var item = ActiveItem(price: 10m);
        _db.MenuItems.Add(item);
        var m1 = new Modifier { Name = "Extra Shot", PriceDelta = 1m, GroupName = "Add-ons", AppliesTo = "All" };
        var m2 = new Modifier { Name = "Oat Milk", PriceDelta = 0.5m, GroupName = "Milk", AppliesTo = "All" };
        _db.Modifiers.AddRange(m1, m2);
        await _db.SaveChangesAsync();

        var receipt = await _svc.CreateAsync(CashOrder(item.Id, modIds: new[] { m1.Id, m2.Id }));

        // 10 + 1 + 0.5 = 11.5
        Assert.Equal(11.5m, receipt.Items[0].UnitPrice);
    }

    [Fact]
    public async Task UnknownModifierId_IsIgnored()
    {
        // Modifier IDs not in the DB are silently skipped (no delta applied).
        var item = ActiveItem(price: 10m);
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        var receipt = await _svc.CreateAsync(CashOrder(item.Id, modIds: new[] { 9999 }));

        Assert.Equal(10m, receipt.Items[0].UnitPrice);
    }

    // ── Subtotal calculation ──────────────────────────────────────────────────

    [Fact]
    public async Task MultipleQuantity_LineTotal_IsUnitPriceTimesQty()
    {
        var item = ActiveItem(price: 5m);
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync();

        var receipt = await _svc.CreateAsync(CashOrder(item.Id, qty: 3, payment: 100m));

        Assert.Equal(15m, receipt.Items[0].LineTotal);
        Assert.Equal(15m, receipt.Subtotal);
    }

    // ── Empty cart ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyCart_Throws()
    {
        var req = new CreateOrderRequest(
            Items: new List<CartItemInput>(),
            DiscountAmount: 0m,
            Payments: new List<PaymentInput> { new("Cash", 100m) }
        );
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.CreateAsync(req));
    }
}
