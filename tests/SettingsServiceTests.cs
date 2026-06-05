using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Services;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Tests;

public class SettingsServiceTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static SettingsService Build(TestScope t) =>
        new(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

    [Fact]
    public async Task GetAsync_returns_seeded_defaults()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var s = await Build(t).GetAsync();

        Assert.Equal("Chao & Brew", s.StoreName);
        Assert.Equal("PHP", s.Currency);
        Assert.Equal(12m, s.TaxRatePercent);
    }

    [Fact]
    public async Task UpdateAsync_persists_all_fields()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        await Build(t).UpdateAsync(new StoreSettingsDto("New Name", "New Address", "USD", 8.5m));

        var loaded = await Build(t).GetAsync();
        Assert.Equal("New Name", loaded.StoreName);
        Assert.Equal("USD", loaded.Currency);
        Assert.Equal(8.5m, loaded.TaxRatePercent);
    }

    [Fact]
    public async Task UpdateAsync_is_idempotent_on_repeated_calls()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var dto = new StoreSettingsDto("Shop A", "Addr", "PHP", 10m);

        await svc.UpdateAsync(dto);
        await svc.UpdateAsync(dto);

        Assert.Equal(4, await t.Db.Settings.CountAsync());
    }

    [Fact]
    public async Task GetTaxRateAsync_matches_stored_value()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        SettingsService.ResetTaxRateCache();

        Assert.Equal(12m, await Build(t).GetTaxRateAsync());
    }

    [Fact]
    public async Task GetTaxRateAsync_returns_zero_when_key_missing()
    {
        using var t = fixture.Begin();
        // No seed — settings table is empty.
        SettingsService.ResetTaxRateCache();

        Assert.Equal(0m, await Build(t).GetTaxRateAsync());
    }

    [Fact]
    public async Task ExportBackupAsync_projects_users_without_password_hashes()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var backup = await Build(t).ExportBackupAsync();

        var type = backup.GetType();
        Assert.NotNull(type.GetProperty("users"));
        Assert.NotNull(type.GetProperty("ingredients"));
        Assert.NotNull(type.GetProperty("settings"));

        var usersProp = type.GetProperty("users")!.GetValue(backup);
        var firstUser = ((System.Collections.IEnumerable)usersProp!).Cast<object>().First();
        Assert.Null(firstUser.GetType().GetProperty("PasswordHash"));
        Assert.NotNull(firstUser.GetType().GetProperty("Username"));
    }
}
