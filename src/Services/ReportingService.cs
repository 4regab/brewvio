using System.Globalization;
using Brewvio.Data;
using Brewvio.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Services;

// Reporting Engine — aggregates sales metrics, a revenue trend bucketed by period
// (daily/weekly/monthly/yearly), and menu profitability for a date range. Aggregation is
// done in memory after targeted queries (provider-agnostic).
public class ReportingService(BrewvioDbContext db)
{
    public async Task<ReportDto> GenerateAsync(DateTime fromUtc, DateTime toUtc, string period = "daily")
    {
        period = (period ?? "daily").Trim().ToLowerInvariant();

        var txns = await db.Transactions
            .Where(t => t.Status == "Completed" && t.Timestamp >= fromUtc && t.Timestamp < toUtc)
            .Select(t => new { t.Timestamp, t.DiscountAmount, t.TaxAmount, t.TotalAmount })
            .ToListAsync();

        var items = await db.TransactionItems
            .Where(ti => ti.Transaction.Status == "Completed"
                && ti.Transaction.Timestamp >= fromUtc && ti.Transaction.Timestamp < toUtc)
            .Select(ti => new { ti.MenuItemId, ti.ItemName, ti.Quantity, ti.LineTotal })
            .ToListAsync();
        var category = await db.MenuItems.ToDictionaryAsync(m => m.Id, m => m.Category);

        var totalSales = txns.Sum(t => t.TotalAmount);
        var totalTax = txns.Sum(t => t.TaxAmount);
        var totalDiscounts = txns.Sum(t => t.DiscountAmount);
        var count = txns.Count;
        var itemsSold = items.Sum(i => i.Quantity);
        var aov = count == 0 ? 0 : Math.Round(totalSales / count, 2);
        var summary = new SalesSummaryDto(totalSales, count, aov, totalDiscounts, totalTax, itemsSold);

        // Revenue trend bucketed by the requested period.
        var trend = txns
            .GroupBy(t => BucketKey(t.Timestamp, period))
            .OrderBy(g => g.Key.Sort)
            .Select(g => new SalesTrendPointDto(g.Key.Label, g.Sum(x => x.TotalAmount), g.Count()))
            .ToList();

        var performance = items.GroupBy(i => new { i.MenuItemId, i.ItemName })
            .Select(g =>
            {
                var qty = g.Sum(x => x.Quantity);
                var revenue = g.Sum(x => x.LineTotal);
                return new MenuPerformanceDto(g.Key.MenuItemId, g.Key.ItemName,
                    category.GetValueOrDefault(g.Key.MenuItemId, ""), qty, revenue);
            })
            .OrderByDescending(p => p.QuantitySold).ToList();

        var bestSellers = performance.Take(5).ToList();
        var slowSellers = performance.OrderBy(p => p.QuantitySold).Take(5).ToList();

        var categoryBreakdown = performance
            .GroupBy(p => string.IsNullOrEmpty(p.Category) ? "Uncategorized" : p.Category)
            .Select(g => new CategorySalesDto(g.Key, g.Sum(p => p.QuantitySold), g.Sum(p => p.Revenue)))
            .OrderByDescending(c => c.Revenue).ToList();

        return new ReportDto(period, summary, trend, performance, bestSellers, slowSellers, categoryBreakdown);
    }

    // Maps a timestamp to a trend bucket (label + sortable key) for the requested period.
    private static (string Label, string Sort) BucketKey(DateTime ts, string period)
    {
        var d = ts.Date;
        switch (period)
        {
            case "weekly":
                // ISO week (Mon-anchored) -> "yyyy-Www".
                var week = ISOWeek.GetWeekOfYear(d);
                var year = ISOWeek.GetYear(d);
                return ($"{year}-W{week:00}", $"{year}{week:00}");
            case "monthly":
                return (d.ToString("yyyy-MM"), d.ToString("yyyyMM"));
            case "yearly":
                return (d.ToString("yyyy"), d.ToString("yyyy"));
            case "daily":
            default:
                return (d.ToString("yyyy-MM-dd"), d.ToString("yyyyMMdd"));
        }
    }
}
