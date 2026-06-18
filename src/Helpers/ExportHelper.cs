using System.Text;
using Brewvio.Dtos;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Brewvio.Helpers;

// Builds CSV, XLSX and PDF exports for reports and inventory.
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
        sb.AppendLine("sep=,");
        sb.AppendLine("\"Sales Summary\"");
        sb.AppendLine("\"Metric\",\"Value\"");
        sb.AppendLine($"\"Total Sales\",{r.Summary.TotalSales:F2}");
        sb.AppendLine($"\"Transactions\",{r.Summary.TransactionCount}");
        sb.AppendLine($"\"Items Sold\",{r.Summary.ItemsSold}");
        sb.AppendLine($"\"Average Order Value\",{r.Summary.AverageOrderValue:F2}");
        sb.AppendLine($"\"Total Discounts\",{r.Summary.TotalDiscounts:F2}");
        sb.AppendLine($"\"Total Tax\",{r.Summary.TotalTax:F2}");
        sb.AppendLine();
        sb.AppendLine($"\"Revenue Trend ({r.Period})\"");
        sb.AppendLine("\"Period\",\"Sales\",\"Transactions\"");
        foreach (var t in r.Trend) sb.AppendLine($"\"{F(t.Label)}\",{t.Sales:F2},{t.TransactionCount}");
        sb.AppendLine();
        sb.AppendLine("\"Menu Performance\"");
        sb.AppendLine("\"Item\",\"Category\",\"Qty Sold\",\"Revenue\"");
        foreach (var m in r.MenuPerformance)
            sb.AppendLine($"\"{F(m.Name)}\",\"{F(m.Category)}\",{m.QuantitySold},{m.Revenue:F2}");
        return Utf8(sb.ToString());
    }

    public static byte[] InventoryCsv(IEnumerable<IngredientDto> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("sep=,");
        sb.AppendLine("\"Code\",\"Name\",\"Category\",\"Unit\",\"Stock Level\",\"Threshold\",\"Cost Per Unit\",\"Status\"");
        foreach (var i in items)
            sb.AppendLine($"\"{F(i.Code)}\",\"{F(i.Name)}\",\"{F(i.Category)}\",\"{F(i.Unit)}\",{i.StockLevel:F2},{i.Threshold:F2},{i.CostPerUnit:F2},\"{F(i.Status)}\"");
        return Utf8(sb.ToString());
    }

    // Friendly label for a stock-movement action code (matches the UI badges).
    private static string MovementTypeLabel(string action) => action switch
    {
        "StockIn" => "Stock In",
        "StockOut" => "Stock Out",
        "InventoryAdjust" => "Adjust",
        "StockSale" => "Sale",
        "StockRefund" => "Refund",
        _ => action
    };

    public static byte[] StockMovementsCsv(IEnumerable<StockMovementDto> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("sep=,");
        sb.AppendLine("\"Timestamp (UTC)\",\"Item Code\",\"Item\",\"Type\",\"Quantity\",\"Balance After\",\"User\",\"Details\"");
        foreach (var m in rows)
            sb.AppendLine(string.Join(",",
                $"\"{m.Timestamp:yyyy-MM-dd HH:mm:ss}\"",
                $"\"{F(m.IngredientCode)}\"",
                $"\"{F(m.IngredientName)}\"",
                $"\"{F(MovementTypeLabel(m.Action))}\"",
                m.Quantity?.ToString("0.###") ?? "",
                m.BalanceAfter?.ToString("0.###") ?? "",
                $"\"{F(m.Username)}\"",
                $"\"{F(m.Details)}\""));
        return Utf8(sb.ToString());
    }

    public static byte[] OrdersXlsx(IEnumerable<TransactionSummaryDto> orders)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Orders");

        // Header row — bold only, no custom colors (avoids ClosedXML corruption on Linux)
        var headers = new[] { "Order #", "Timestamp", "Cashier", "Items", "Status", "Payment", "Total" };
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
        }

        // Data rows
        var row = 2;
        foreach (var o in orders)
        {
            ws.Cell(row, 1).Value = o.Id;
            ws.Cell(row, 2).Value = DateTime.SpecifyKind(o.Timestamp, DateTimeKind.Unspecified);
            ws.Cell(row, 2).Style.NumberFormat.Format = "yyyy-mm-dd hh:mm:ss";
            ws.Cell(row, 3).Value = o.Cashier;
            ws.Cell(row, 4).Value = o.ItemCount;
            ws.Cell(row, 5).Value = o.Status;
            ws.Cell(row, 6).Value = o.PaymentMethod;
            ws.Cell(row, 7).Value = o.TotalAmount;
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00";
            row++;
        }

        ws.Column(1).Width = 10;
        ws.Column(2).Width = 22;
        ws.Column(3).Width = 20;
        ws.Column(4).Width = 8;
        ws.Column(5).Width = 14;
        ws.Column(6).Width = 12;
        ws.Column(7).Width = 14;
        ws.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static byte[] SalesReportXlsx(ReportDto r, DateTime fromUtc, DateTime toUtc)
    {
        using var wb = new XLWorkbook();

        // ── Sheet 1: Summary ──────────────────────────────────────────────
        var sum = wb.Worksheets.Add("Summary");

        // Report title + the period it covers (toUtc is exclusive, so show the inclusive last day).
        sum.Cell(1, 1).Value = "Sales & Analytics Report";
        sum.Cell(1, 1).Style.Font.Bold = true;
        sum.Cell(1, 1).Style.Font.FontSize = 14;
        sum.Cell(2, 1).Value = $"Period: {fromUtc:yyyy-MM-dd} to {toUtc.AddDays(-1):yyyy-MM-dd}";
        sum.Cell(3, 1).Value = $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";

        const int headerRow = 5;   // leave a blank row (4) between the title block and the table
        var summaryRows = new[]
        {
            ("Total Sales",         r.Summary.TotalSales.ToString("N2")),
            ("Transactions",        r.Summary.TransactionCount.ToString()),
            ("Items Sold",          r.Summary.ItemsSold.ToString()),
            ("Avg Order Value",     r.Summary.AverageOrderValue.ToString("N2")),
            ("Total Discounts",     r.Summary.TotalDiscounts.ToString("N2")),
            ("Total Tax",           r.Summary.TotalTax.ToString("N2")),
        };
        sum.Cell(headerRow, 1).Value = "Metric";   sum.Cell(headerRow, 1).Style.Font.Bold = true;
        sum.Cell(headerRow, 2).Value = "Value";    sum.Cell(headerRow, 2).Style.Font.Bold = true;
        for (var i = 0; i < summaryRows.Length; i++)
        {
            sum.Cell(headerRow + 1 + i, 1).Value = summaryRows[i].Item1;
            sum.Cell(headerRow + 1 + i, 2).Value = summaryRows[i].Item2;
        }
        sum.Column(1).Width = 28;
        sum.Column(2).Width = 18;

        // ── Sheet 2: Revenue Trend ────────────────────────────────────────
        var trend = wb.Worksheets.Add("Revenue Trend");
        trend.Cell(1, 1).Value = "Period";       trend.Cell(1, 1).Style.Font.Bold = true;
        trend.Cell(1, 2).Value = "Sales";        trend.Cell(1, 2).Style.Font.Bold = true;
        trend.Cell(1, 3).Value = "Transactions"; trend.Cell(1, 3).Style.Font.Bold = true;
        for (var i = 0; i < r.Trend.Count; i++)
        {
            trend.Cell(i + 2, 1).Value = r.Trend[i].Label;
            trend.Cell(i + 2, 2).Value = r.Trend[i].Sales;
            trend.Cell(i + 2, 2).Style.NumberFormat.Format = "#,##0.00";
            trend.Cell(i + 2, 3).Value = r.Trend[i].TransactionCount;
        }
        trend.Column(1).Width = 16;
        trend.Column(2).Width = 16;
        trend.Column(3).Width = 16;
        trend.SheetView.FreezeRows(1);

        // ── Sheet 3: Menu Performance ─────────────────────────────────────
        var perf = wb.Worksheets.Add("Menu Performance");
        perf.Cell(1, 1).Value = "Product";    perf.Cell(1, 1).Style.Font.Bold = true;
        perf.Cell(1, 2).Value = "Category";   perf.Cell(1, 2).Style.Font.Bold = true;
        perf.Cell(1, 3).Value = "Qty Sold";   perf.Cell(1, 3).Style.Font.Bold = true;
        perf.Cell(1, 4).Value = "Revenue";    perf.Cell(1, 4).Style.Font.Bold = true;
        for (var i = 0; i < r.MenuPerformance.Count; i++)
        {
            var m = r.MenuPerformance[i];
            perf.Cell(i + 2, 1).Value = m.Name;
            perf.Cell(i + 2, 2).Value = m.Category;
            perf.Cell(i + 2, 3).Value = m.QuantitySold;
            perf.Cell(i + 2, 4).Value = m.Revenue;
            perf.Cell(i + 2, 4).Style.NumberFormat.Format = "#,##0.00";
        }
        perf.Column(1).Width = 32;
        perf.Column(2).Width = 22;
        perf.Column(3).Width = 12;
        perf.Column(4).Width = 16;
        perf.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ---------- PDF ----------
    public static byte[] SalesReportPdf(ReportDto r, string storeName, string currency, DateTime fromUtc, DateTime toUtc) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Column(col =>
            {
                col.Spacing(4);
                col.Item().Text(storeName).FontSize(18).Bold();
                col.Item().Text("Sales & Analytics Report").FontSize(12).FontColor(Colors.Grey.Darken1);
                col.Item().Text($"Period: {fromUtc:yyyy-MM-dd} to {toUtc.AddDays(-1):yyyy-MM-dd}").FontColor(Colors.Grey.Darken1);
                col.Item().PaddingTop(8).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
            });

            page.Content().PaddingVertical(15).Column(col =>
            {
                col.Spacing(18);

                // Summary section
                col.Item().Text("Summary").FontSize(13).Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                    });
                    void Row(string k, string v)
                    {
                        t.Cell().PaddingVertical(4).PaddingLeft(8).Text(k);
                        t.Cell().PaddingVertical(4).PaddingRight(8).AlignRight().Text(v);
                    }
                    Row("Total Sales", $"{currency} {r.Summary.TotalSales:N2}");
                    Row("Transactions", r.Summary.TransactionCount.ToString());
                    Row("Items Sold", r.Summary.ItemsSold.ToString());
                    Row("Average Order Value", $"{currency} {r.Summary.AverageOrderValue:N2}");
                    Row("Total Discounts", $"{currency} {r.Summary.TotalDiscounts:N2}");
                    Row("Total Tax", $"{currency} {r.Summary.TotalTax:N2}");
                });

                // Menu Performance section
                col.Item().PaddingTop(8).Text("Menu Performance").FontSize(13).Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(4);
                        c.RelativeColumn(3);
                        c.ConstantColumn(60);
                        c.ConstantColumn(90);
                    });
                    t.Header(h =>
                    {
                        h.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingVertical(5).PaddingLeft(8).Text("Item").Bold();
                        h.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingVertical(5).Text("Category").Bold();
                        h.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingVertical(5).AlignRight().Text("Qty").Bold();
                        h.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingVertical(5).PaddingRight(8).AlignRight().Text("Revenue").Bold();
                    });
                    foreach (var m in r.MenuPerformance)
                    {
                        t.Cell().PaddingVertical(4).PaddingLeft(8).Text(m.Name);
                        t.Cell().PaddingVertical(4).Text(m.Category);
                        t.Cell().PaddingVertical(4).AlignRight().Text(m.QuantitySold.ToString());
                        t.Cell().PaddingVertical(4).PaddingRight(8).AlignRight().Text($"{currency} {m.Revenue:N2}");
                    }
                });
            });

            page.Footer().AlignCenter().Text($"Generated by Brewvio \u2022 {DateTime.Now:yyyy-MM-dd HH:mm}")
                .FontSize(8).FontColor(Colors.Grey.Medium);
        })).GeneratePdf();
}
