using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Services;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Tests;

/// <summary>
/// Extended SettingsService tests: backup content depth, empty-DB defaults,
/// currency round-trip, zero-tax edge case, and UpdateAsync audit entry.
/// </summary>
public class SettingsServiceExtendedTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static SettingsService Build(TestScope t) =>
        new(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

    // ── GetAsync defaults when table is empty ─────────────────────────────────

    [Fact]
    public async Task GetAsync_returns_defaults_when_settings_table_is_empty()
    {
        using var t = fixture.Begin();
        // No seed

        var s = await Build(t).GetAsync();

        Assert.Equal("Chao & Brew", s.StoreName);
        Assert.Equal("PHP", s.Currency);
        Assert.Equal(0m, s.TaxRatePercent);
        Assert.Equal("", s.Address);
    }

    // ── UpdateAsync with zero tax ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_persists_zero_tax_rate()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);

        await svc.UpdateAsync(new StoreSettingsDto("Store X", "123 St", "PHP", 0m));

        Assert.Equal(0m, (await svc.GetAsync()).TaxRatePercent);
    }

    // ── Currency round-trip ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_persists_non_php_currency()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);

        await svc.UpdateAsync(new StoreSettingsDto("Store Y", "Addr", "USD", 8m));

        Assert.Equal("USD", (await svc.GetAsync()).Currency);
    }

    // ── UpdateAsync writes audit entry ────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_writes_settings_updated_audit_entry()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);

        await svc.UpdateAsync(new StoreSettingsDto("Audit Store", "Somewhere", "PHP", 10m));

        var verify = t.NewContext();
        Assert.Contains(await verify.AuditLogs.ToListAsync(),
            a => a.Action == "SettingsUpdated" && a.Details.Contains("Audit Store"));
    }

    // ── ExportBackupAsync content completeness ────────────────────────────────

    [Fact]
    public async Task ExportBackupAsync_includes_ingredients_section()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var backup = await Build(t).ExportBackupAsync();

        var ingProp = backup.GetType().GetProperty("ingredients");
        Assert.NotNull(ingProp);
        var ingredients = ((System.Collections.IEnumerable)ingProp!.GetValue(backup)!).Cast<object>().ToList();
        Assert.NotEmpty(ingredients);
    }

    [Fact]
    public async Task ExportBackupAsync_includes_modifiers_section()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var backup = await Build(t).ExportBackupAsync();

        var modProp = backup.GetType().GetProperty("modifiers");
        Assert.NotNull(modProp);
        var mods = ((System.Collections.IEnumerable)modProp!.GetValue(backup)!).Cast<object>().ToList();
        Assert.NotEmpty(mods);
    }

    [Fact]
    public async Task ExportBackupAsync_includes_menu_items_section()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var backup = await Build(t).ExportBackupAsync();

        var menuProp = backup.GetType().GetProperty("menuItems");
        Assert.NotNull(menuProp);
        var items = ((System.Collections.IEnumerable)menuProp!.GetValue(backup)!).Cast<object>().ToList();
        Assert.NotEmpty(items);
    }

    [Fact]
    public async Task ExportBackupAsync_includes_audit_logs_section()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);

        // Generate at least one audit log
        await svc.UpdateAsync(new StoreSettingsDto("S", "A", "PHP", 0m));
        var backup = await svc.ExportBackupAsync();

        var auditProp = backup.GetType().GetProperty("auditLogs");
        Assert.NotNull(auditProp);
        var logs = ((System.Collections.IEnumerable)auditProp!.GetValue(backup)!).Cast<object>().ToList();
        Assert.NotEmpty(logs);
    }

    [Fact]
    public async Task ExportBackupAsync_includes_transactions_section()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var backup = await Build(t).ExportBackupAsync();

        var txProp = backup.GetType().GetProperty("transactions");
        Assert.NotNull(txProp);
        // Might be empty (no orders placed), but the property must exist
    }

    [Fact]
    public async Task ExportBackupAsync_exported_at_is_recent()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var backup = await Build(t).ExportBackupAsync();

        var tsProp = backup.GetType().GetProperty("exportedAt");
        Assert.NotNull(tsProp);
        var ts = (DateTime)tsProp!.GetValue(backup)!;
        Assert.True(ts > DateTime.UtcNow.AddMinutes(-1));
    }

    // ── GetTaxRateAsync with non-numeric stored value ─────────────────────────

    [Fact]
    public async Task GetTaxRateAsync_returns_zero_when_stored_value_is_non_numeric()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var existing = await t.Db.Settings.FindAsync("TaxRatePercent");
        if (existing != null) existing.Value = "not-a-number";
        else t.Db.Settings.Add(new Brewvio.Models.AppSetting { Key = "TaxRatePercent", Value = "not-a-number" });
        await t.Db.SaveChangesAsync();

        // Reset the static cache so GetTaxRateAsync does a fresh DB read
        SettingsService.ResetTaxRateCache();

        var rate = await Build(t).GetTaxRateAsync();

        Assert.Equal(0m, rate);
    }

    // ── Fractional tax rate precision ─────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_preserves_fractional_tax_rate_precision()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);

        await svc.UpdateAsync(new StoreSettingsDto("Store", "Addr", "PHP", 7.5m));

        Assert.Equal(7.5m, (await svc.GetAsync()).TaxRatePercent);
    }
}
