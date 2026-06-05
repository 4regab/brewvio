using System.Text;
using Brewvio.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Brewvio.Helpers;

// Builds CSV and PDF exports for reports and inventory.
public static class ExportHelper
{
    static ExportHelper() => QuestPDF.Settings.License = LicenseType.Community;

    // ---------- CSV ----------
    private static string F(object? v)
    {
        var s = v?.ToString() ?? "";
        return s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
    private static byte[] Utf8(string s) => new UTF8Encoding(true).GetBytes(s); // BOM => opens cleanly in Excel

    public static byte[] SalesReportCsv(ReportDto r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Sales Summary");
        sb.AppendLine("Metric,Value");
        sb.AppendLine($"Total Sales,{r.Summary.TotalSales}");
        sb.AppendLine($"Transactions,{r.Summary.TransactionCount}");
        sb.AppendLine($"Items Sold,{r.Summary.ItemsSold}");
        sb.AppendLine($"Average Order Value,{r.Summary.AverageOrderValue}");
        sb.AppendLine($"Total Discounts,{r.Summary.TotalDiscounts}");
        sb.AppendLine($"Total Tax,{r.Summary.TotalTax}");
        sb.AppendLine($"Ingredient Cost,{r.Summary.TotalCost}");
        sb.AppendLine($"Gross Profit,{r.Summary.GrossProfit}");
        sb.AppendLine($"Profit Margin %,{r.Summary.ProfitMarginPercent}");
        sb.AppendLine();
        sb.AppendLine($"Revenue Trend ({r.Period})");
        sb.AppendLine("Period,Sales,Transactions");
        foreach (var t in r.Trend) sb.AppendLine($"{F(t.Label)},{t.Sales},{t.TransactionCount}");
        sb.AppendLine();
        sb.AppendLine("Menu Performance");
        sb.AppendLine("Item,Category,Qty Sold,Revenue,Cost,Profit,Margin %");
        foreach (var m in r.MenuPerformance)
            sb.AppendLine($"{F(m.Name)},{F(m.Category)},{m.QuantitySold},{m.Revenue},{m.Cost},{m.Profit},{m.MarginPercent}");
        return Utf8(sb.ToString());
    }

    public static byte[] InventoryCsv(IEnumerable<IngredientDto> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Code,Name,Category,Unit,Stock Level,Threshold,Cost Per Unit,Status");
        foreach (var i in items)
            sb.AppendLine($"{F(i.Code)},{F(i.Name)},{F(i.Category)},{F(i.Unit)},{i.StockLevel},{i.Threshold},{i.CostPerUnit},{F(i.Status)}");
        return Utf8(sb.ToString());
    }

    public static byte[] OrdersCsv(IEnumerable<TransactionSummaryDto> orders)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Order #,Timestamp,Cashier,Items,Status,Payment,Total");
        foreach (var o in orders)
            sb.AppendLine($"{o.Id},{o.Timestamp:yyyy-MM-dd HH:mm:ss},{F(o.Cashier)},{o.ItemCount},{F(o.Status)},{F(o.PaymentMethod)},{o.TotalAmount}");
        return Utf8(sb.ToString());
    }

    // ---------- PDF ----------
    public static byte[] SalesReportPdf(ReportDto r, string storeName, string currency, DateTime fromUtc, DateTime toUtc) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(30);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Column(col =>
            {
                col.Item().Text(storeName).FontSize(18).Bold();
                col.Item().Text("Sales & Analytics Report").FontSize(12).FontColor(Colors.Grey.Darken1);
                col.Item().Text($"Period: {fromUtc:yyyy-MM-dd} to {toUtc.AddDays(-1):yyyy-MM-dd}").FontColor(Colors.Grey.Darken1);
            });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(12);
                col.Item().Text("Summary").FontSize(13).Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                    void Row(string k, string v)
                    {
                        t.Cell().PaddingVertical(2).Text(k);
                        t.Cell().PaddingVertical(2).AlignRight().Text(v);
                    }
                    Row("Total Sales", $"{currency} {r.Summary.TotalSales:N2}");
                    Row("Transactions", r.Summary.TransactionCount.ToString());
                    Row("Items Sold", r.Summary.ItemsSold.ToString());
                    Row("Average Order Value", $"{currency} {r.Summary.AverageOrderValue:N2}");
                    Row("Total Discounts", $"{currency} {r.Summary.TotalDiscounts:N2}");
                    Row("Total Tax", $"{currency} {r.Summary.TotalTax:N2}");
                    Row("Ingredient Cost", $"{currency} {r.Summary.TotalCost:N2}");
                    Row("Gross Profit", $"{currency} {r.Summary.GrossProfit:N2}");
                    Row("Profit Margin", $"{r.Summary.ProfitMarginPercent:N1}%");
                });

                col.Item().Text("Menu Performance").FontSize(13).Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3); c.RelativeColumn(2);
                        c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn();
                    });
                    t.Header(h =>
                    {
                        void H(string s) => h.Cell().BorderBottom(1).PaddingVertical(3).Text(s).Bold();
                        H("Item"); H("Category"); H("Qty"); H("Revenue"); H("Cost"); H("Profit"); H("Margin");
                    });
                    foreach (var m in r.MenuPerformance)
                    {
                        t.Cell().PaddingVertical(2).Text(m.Name);
                        t.Cell().PaddingVertical(2).Text(m.Category);
                        t.Cell().PaddingVertical(2).AlignRight().Text(m.QuantitySold.ToString());
                        t.Cell().PaddingVertical(2).AlignRight().Text($"{m.Revenue:N2}");
                        t.Cell().PaddingVertical(2).AlignRight().Text($"{m.Cost:N2}");
                        t.Cell().PaddingVertical(2).AlignRight().Text($"{m.Profit:N2}");
                        t.Cell().PaddingVertical(2).AlignRight().Text($"{m.MarginPercent:N1}%");
                    }
                });
            });

            page.Footer().AlignCenter().Text($"Generated by Brewvio • {DateTime.Now:yyyy-MM-dd HH:mm}")
                .FontColor(Colors.Grey.Medium);
        })).GeneratePdf();
}
