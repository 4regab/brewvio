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

    // In-process cache for tax rate — avoids a DB round-trip on every order.
    private static decimal? _cachedTaxRate;
    private static DateTime _taxRateCachedAt = DateTime.MinValue;

    public async Task<decimal> GetTaxRateAsync()
    {
        if (_cachedTaxRate.HasValue && (DateTime.UtcNow - _taxRateCachedAt).TotalMinutes < 5)
            return _cachedTaxRate.Value;
        var val = (await db.Settings.FindAsync(TaxRate))?.Value;
        SetTaxRateCache(decimal.TryParse(val, out var t) ? t : 0m);
        return _cachedTaxRate!.Value;
    }

    private static void SetTaxRateCache(decimal rate)
    {
        _cachedTaxRate = rate;
        _taxRateCachedAt = DateTime.UtcNow;
    }

    // Clears the in-process tax rate cache. Used by tests to prevent cross-test contamination.
    internal static void ResetTaxRateCache()
    {
        _cachedTaxRate = null;
        _taxRateCachedAt = DateTime.MinValue;
    }

    // "USB backup" adapted to the web stack: a downloadable JSON snapshot of core tables.
    // Users are projected explicitly (Id/Username/FullName/Role/IsActive) to avoid leaking PasswordHash.
    // Transactions are projected to avoid circular reference via Cashier navigation property.
    public async Task<object> ExportBackupAsync() => new
    {
        exportedAt = DateTime.UtcNow,
        users = await db.Users.Select(u => new { u.Id, u.Username, u.FullName, u.Role, u.IsActive }).ToListAsync(),
        ingredients = await db.Ingredients.AsNoTracking()
            .Select(i => new { i.Id, i.Code, i.Name, i.Category, i.Unit, i.StockLevel, i.Threshold, i.CostPerUnit }).ToListAsync(),
        menuItems = await db.MenuItems.AsNoTracking()
            .Include(m => m.Recipe).ThenInclude(r => r.Ingredient)
            .Select(m => new {
                m.Id, m.Name, m.Category, m.Price, m.IsActive, m.ImageUrl,
                Recipe = m.Recipe.Select(r => new { r.IngredientId, r.Quantity, IngredientName = r.Ingredient.Name })
            }).ToListAsync(),
        modifiers = await db.Modifiers.AsNoTracking().ToListAsync(),
        transactions = await db.Transactions.AsNoTracking()
            .Include(t => t.Items)
            .Include(t => t.Payments)
            .Select(t => new {
                t.Id, t.Timestamp, t.Subtotal, t.DiscountAmount, t.TaxAmount, t.TotalAmount,
                t.PaymentMethod, t.CashierId, t.Status, t.Notes,
                Items = t.Items.Select(i => new { i.Id, i.ItemName, i.Quantity, i.UnitPrice, i.LineTotal, i.Modifiers }),
                Payments = t.Payments.Select(p => new { p.Id, p.Method, p.Amount })
            }).ToListAsync(),
        auditLogs = await db.AuditLogs.AsNoTracking().ToListAsync(),
        settings = await db.Settings.AsNoTracking().ToListAsync()
    };
}
