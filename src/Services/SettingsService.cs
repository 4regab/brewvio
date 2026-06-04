using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Services;

// Configurable store settings (key/value) plus a JSON data-export "backup" mechanism.
public class SettingsService(BrewvioDbContext db, AuditService audit)
{
    private const string StoreName = "StoreName", Address = "Address",
        Currency = "Currency", TaxRate = "TaxRatePercent";

    public async Task<StoreSettingsDto> GetAsync()
    {
        var map = await db.Settings.ToDictionaryAsync(s => s.Key, s => s.Value);
        return new StoreSettingsDto(
            map.GetValueOrDefault(StoreName, "Chao & Brew"),
            map.GetValueOrDefault(Address, ""),
            map.GetValueOrDefault(Currency, "PHP"),
            decimal.TryParse(map.GetValueOrDefault(TaxRate), out var t) ? t : 0m);
    }

    // skill: optimizing-ef-core-queries — replaced 4 sequential FindAsync calls (one per key)
    // with a single WHERE ... IN query, then upsert in memory before one SaveChangesAsync.
    public async Task<StoreSettingsDto> UpdateAsync(StoreSettingsDto dto)
    {
        var keys = new[] { StoreName, Address, Currency, TaxRate };
        var existing = await db.Settings.Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key);

        void Upsert(string key, string value)
        {
            if (existing.TryGetValue(key, out var row)) row.Value = value;
            else db.Settings.Add(new AppSetting { Key = key, Value = value });
        }

        Upsert(StoreName, dto.StoreName);
        Upsert(Address, dto.Address);
        Upsert(Currency, dto.Currency);
        Upsert(TaxRate, dto.TaxRatePercent.ToString("0.####"));
        audit.Add("SettingsUpdated", $"Store='{dto.StoreName}', Tax={dto.TaxRatePercent}%, Currency={dto.Currency}");
        await db.SaveChangesAsync();
        return dto;
    }

    public async Task<decimal> GetTaxRateAsync() =>
        decimal.TryParse((await db.Settings.FindAsync(TaxRate))?.Value, out var t) ? t : 0m;

    // "USB backup" adapted to the web stack: a downloadable JSON snapshot of core tables.
    // Users are projected explicitly (Id/Username/FullName/Role/IsActive) to avoid leaking PasswordHash.
    public async Task<object> ExportBackupAsync() => new
    {
        exportedAt = DateTime.UtcNow,
        users = await db.Users.Select(u => new { u.Id, u.Username, u.FullName, u.Role, u.IsActive }).ToListAsync(),
        ingredients = await db.Ingredients.ToListAsync(),
        menuItems = await db.MenuItems.Include(m => m.Recipe).ToListAsync(),
        modifiers = await db.Modifiers.ToListAsync(),
        transactions = await db.Transactions.Include(t => t.Items).Include(t => t.Payments).ToListAsync(),
        auditLogs = await db.AuditLogs.ToListAsync(),
        settings = await db.Settings.ToListAsync()
    };
}
