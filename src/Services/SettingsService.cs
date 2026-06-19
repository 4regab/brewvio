using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Services;

// Configurable store settings (key/value) plus a JSON data-export "backup" mechanism.
public class SettingsService(BrewvioDbContext db, AuditService audit)
{
    private const string StoreName = "StoreName", Address = "Address",
        Currency = "Currency", TaxRate = "TaxRatePercent", MaxDiscountPct = "MaxDiscountPercent";

    // Upper bound on the discount a single order may receive, as a percent of its subtotal.
    // Stored as an optional AppSetting key ("MaxDiscountPercent"); when absent this secure
    // default applies. It deliberately caps below 100% so a discount can never zero out an
    // order (the "free order" fraud vector), while still allowing statutory (20%) and generous
    // promotional discounts. Ops can override it by inserting/updating the setting key — it is
    // intentionally NOT part of StoreSettingsDto so the store-settings save flow can't reset it.
    internal const decimal DefaultMaxDiscountPercent = 50m;

    // Reads the store settings, applying defaults for any missing keys.
    // ct: cancellation token.
    // returns: the current store settings DTO.
    public async Task<StoreSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var map = await db.Settings.ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        return new StoreSettingsDto(
            map.GetValueOrDefault(StoreName, "Chao & Brew"),
            map.GetValueOrDefault(Address, ""),
            map.GetValueOrDefault(Currency, "PHP"),
            decimal.TryParse(map.GetValueOrDefault(TaxRate), out var t) ? t : 0m);
    }

    // skill: optimizing-ef-core-queries — replaced 4 sequential FindAsync calls (one per key)
    // with a single WHERE ... IN query, then upsert in memory before one SaveChangesAsync.
    public async Task<StoreSettingsDto> UpdateAsync(StoreSettingsDto dto, CancellationToken ct = default)
    {
        var keys = new[] { StoreName, Address, Currency, TaxRate };
        var existing = await db.Settings.Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, ct);

        // Updates the existing setting row in place, or adds a new one for the given key.
        // key: the setting key.
        // value: the setting value to store.
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
        await db.SaveChangesAsync(ct);
        return dto;
    }

    // In-process cache for tax rate — avoids a DB round-trip on every order.
    private static decimal? _cachedTaxRate;
    private static DateTime _taxRateCachedAt = DateTime.MinValue;

    // Process-global caching is correct on Lambda (warm containers reuse it across invocations)
    // but causes cross-test contamination since tests run in parallel against a shared static.
    // Tests flip this off (see Brewvio.Tests module initializer) so every read hits their own
    // transaction-isolated DB value. Always true in production.
    internal static bool TaxRateCacheEnabled { get; set; } = true;

    // Gets the tax rate percent, using the 5-minute in-process cache when enabled.
    // ct: cancellation token.
    // returns: the tax rate as a percent, or 0 when unset/unparseable.
    public async Task<decimal> GetTaxRateAsync(CancellationToken ct = default)
    {
        if (TaxRateCacheEnabled && _cachedTaxRate.HasValue && (DateTime.UtcNow - _taxRateCachedAt).TotalMinutes < 5)
            return _cachedTaxRate.Value;
        var val = (await db.Settings.FindAsync([TaxRate], ct))?.Value;
        var rate = decimal.TryParse(val, out var t) ? t : 0m;
        if (TaxRateCacheEnabled) SetTaxRateCache(rate);
        return rate;
    }

    // Stores the tax rate in the in-process cache and stamps the cache time.
    // rate: the tax rate percent to cache.
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

    // Maximum discount allowed on an order, as a percent of subtotal. Reads the optional
    // "MaxDiscountPercent" setting key, clamped to [0, 100], falling back to the secure default
    // when unset or unparseable. Cheap single-key lookup; not cached (orders aren't hot enough
    // to need it, and avoiding a static cache keeps tests isolated).
    public async Task<decimal> GetMaxDiscountPercentAsync(CancellationToken ct = default)
    {
        var val = (await db.Settings.FindAsync([MaxDiscountPct], ct))?.Value;
        return decimal.TryParse(val, out var p) ? Math.Clamp(p, 0m, 100m) : DefaultMaxDiscountPercent;
    }

    // "USB backup" adapted to the web stack: a downloadable JSON snapshot of core tables.
    // Users are projected explicitly (Id/Username/FullName/Role/IsActive) to avoid leaking PasswordHash.
    // Transactions are projected to avoid circular reference via Cashier navigation property.
    public async Task<object> ExportBackupAsync(CancellationToken ct = default) => new
    {
        exportedAt = DateTime.UtcNow,
        users = await db.Users.AsNoTracking().Select(u => new { u.Id, u.Username, u.FullName, u.Role, u.IsActive }).ToListAsync(ct),
        ingredients = await db.Ingredients.AsNoTracking()
            .Select(i => new { i.Id, i.Code, i.Name, i.Category, i.Unit, i.StockLevel, i.Threshold, i.CostPerUnit }).ToListAsync(ct),
        menuItems = await db.MenuItems.AsNoTracking()
            .Include(m => m.Recipe).ThenInclude(r => r.Ingredient)
            .Select(m => new {
                m.Id, m.Name, m.Category, m.Price, m.IsActive, m.ImageUrl,
                Recipe = m.Recipe.Select(r => new { r.IngredientId, r.Quantity, IngredientName = r.Ingredient.Name })
            }).ToListAsync(ct),
        modifiers = await db.Modifiers.AsNoTracking().ToListAsync(ct),
        transactions = await db.Transactions.AsNoTracking()
            .Include(t => t.Items)
            .Include(t => t.Payments)
            .Select(t => new {
                t.Id, t.Timestamp, t.Subtotal, t.DiscountAmount, t.TaxAmount, t.TotalAmount,
                t.PaymentMethod, t.CashierId, t.Status, t.Notes,
                Items = t.Items.Select(i => new { i.Id, i.ItemName, i.Quantity, i.UnitPrice, i.LineTotal, i.Modifiers }),
                Payments = t.Payments.Select(p => new { p.Id, p.Method, p.Amount })
            }).ToListAsync(ct),
        auditLogs = await db.AuditLogs.AsNoTracking().ToListAsync(ct),
        settings = await db.Settings.AsNoTracking().ToListAsync(ct)
    };
}
