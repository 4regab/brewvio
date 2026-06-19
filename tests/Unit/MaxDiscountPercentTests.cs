using Brewvio.Data;
using Brewvio.Models;
using Brewvio.Services;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Tests.Unit;

// Tests for SettingsService.GetMaxDiscountPercentAsync — specifically the
// clamping and fallback logic not exercised by the existing integration tests.
// Uses an in-memory database (no PostgreSQL required).
public class MaxDiscountPercentTests : IAsyncDisposable
{
    private readonly BrewvioDbContext _db;
    private readonly SettingsService _svc;

    public MaxDiscountPercentTests()
    {
        var opts = new DbContextOptionsBuilder<BrewvioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new BrewvioDbContext(opts);
        _db.Database.EnsureCreated();

        var currentUser = TestSupport.Cur(0, "system", "Manager");
        var audit = new AuditService(_db, currentUser);
        _svc = new SettingsService(_db, audit);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private async Task SetSetting(string key, string value)
    {
        _db.Settings.Add(new AppSetting { Key = key, Value = value });
        await _db.SaveChangesAsync();
    }

    // ── Fallback ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task MissingKey_ReturnsSecureDefault()
    {
        var result = await _svc.GetMaxDiscountPercentAsync();
        Assert.Equal(SettingsService.DefaultMaxDiscountPercent, result);
    }

    [Fact]
    public async Task NonNumericValue_ReturnsSecureDefault()
    {
        await SetSetting("MaxDiscountPercent", "unlimited");
        var result = await _svc.GetMaxDiscountPercentAsync();
        Assert.Equal(SettingsService.DefaultMaxDiscountPercent, result);
    }

    [Fact]
    public async Task EmptyValue_ReturnsSecureDefault()
    {
        await SetSetting("MaxDiscountPercent", "");
        var result = await _svc.GetMaxDiscountPercentAsync();
        Assert.Equal(SettingsService.DefaultMaxDiscountPercent, result);
    }

    // ── Normal range ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidValue_ReturnsStoredValue()
    {
        await SetSetting("MaxDiscountPercent", "20");
        Assert.Equal(20m, await _svc.GetMaxDiscountPercentAsync());
    }

    [Fact]
    public async Task Zero_IsAllowed()
    {
        await SetSetting("MaxDiscountPercent", "0");
        Assert.Equal(0m, await _svc.GetMaxDiscountPercentAsync());
    }

    [Fact]
    public async Task Hundred_IsAllowed()
    {
        await SetSetting("MaxDiscountPercent", "100");
        Assert.Equal(100m, await _svc.GetMaxDiscountPercentAsync());
    }

    [Fact]
    public async Task FractionalValue_ReturnedAsIs()
    {
        await SetSetting("MaxDiscountPercent", "33.5");
        Assert.Equal(33.5m, await _svc.GetMaxDiscountPercentAsync());
    }

    // ── Clamping — zero coverage before this ──────────────────────────────────

    [Fact]
    public async Task AboveHundred_ClampedToHundred()
    {
        await SetSetting("MaxDiscountPercent", "150");
        Assert.Equal(100m, await _svc.GetMaxDiscountPercentAsync());
    }

    [Fact]
    public async Task NegativeValue_ClampedToZero()
    {
        await SetSetting("MaxDiscountPercent", "-10");
        Assert.Equal(0m, await _svc.GetMaxDiscountPercentAsync());
    }

    [Fact]
    public async Task ExtremelyLargeValue_ClampedToHundred()
    {
        await SetSetting("MaxDiscountPercent", "999999");
        Assert.Equal(100m, await _svc.GetMaxDiscountPercentAsync());
    }
}
