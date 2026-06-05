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

    public static byte[] OrdersXlsx(IEnumerable<TransactionSummaryDto> orders)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Orders");

        // Header row — bold, light grey background
        var headers = new[] { "Order #", "Timestamp", "Cashier", "Items", "Status", "Payment", "Total" };
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#CCCCCC");
        }

        // Data rows
        var row = 2;
        foreach (var o in orders)
        {
            ws.Cell(row, 1).Value = o.Id;
            // Use Unspecified kind — ClosedXML serialises DateTime correctly on Linux
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

        // Fixed column widths — AdjustToContents can corrupt on Lambda/Linux
        ws.Column(1).Width = 10;  // Order #
        ws.Column(2).Width = 22;  // Timestamp
        ws.Column(3).Width = 20;  // Cashier
        ws.Column(4).Width = 8;   // Items
        ws.Column(5).Width = 14;  // Status
        ws.Column(6).Width = 12;  // Payment
        ws.Column(7).Width = 14;  // Total

        // Freeze the header row
        ws.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ---------- PDF ----------
    public static byte[] ReceiptPdf(ReceiptDto r, string storeName, string address, string currency) =>
        Document.Create(doc => doc.Page(page =>
        {
            // 80 mm thermal receipt width, height auto-sized to content
            page.Size(227, 9999, Unit.Point); // 80 mm = ~227pt; height is trimmed by QuestPDF
            page.Margin(14);
            page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Courier New"));

            page.Content().Column(col =>
            {
                col.Spacing(3);

                // Store header
                col.Item().AlignCenter().Text(storeName).FontSize(11).Bold();
                if (!string.IsNullOrWhiteSpace(address))
                    col.Item().AlignCenter().Text(address).FontSize(8).FontColor(Colors.Grey.Darken1);

                col.Item().PaddingVertical(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                // Meta
                void Meta(string label, string value) =>
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text(label).FontSize(8);
                        row.RelativeItem().AlignRight().Text(value).FontSize(8);
                    });

                Meta("Receipt #", r.TransactionId.ToString());
                Meta("Date", r.Timestamp.ToLocalTime().ToString("MM/dd/yyyy h:mm tt"));
                Meta("Cashier", r.Cashier);

                col.Item().PaddingVertical(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                // Line items
                foreach (var item in r.Items)
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"{item.Quantity}x {item.Name}").FontSize(8);
                        row.ConstantItem(60).AlignRight()
                            .Text($"{currency} {item.LineTotal:N2}").FontSize(8);
                    });
                    if (!string.IsNullOrWhiteSpace(item.Modifiers))
                        col.Item().PaddingLeft(10).Text($"+ {item.Modifiers}")
                            .FontSize(7).FontColor(Colors.Grey.Darken1);
                }

                col.Item().PaddingVertical(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                // Totals
                void TotalRow(string label, string value, bool bold = false)
                {
                    col.Item().Row(row =>
                    {
                        var l = row.RelativeItem().Text(label).FontSize(8);
                        var v = row.ConstantItem(70).AlignRight().Text(value).FontSize(8);
                        if (bold) { l.Bold(); v.Bold(); }
                    });
                }

                TotalRow("Subtotal", $"{currency} {r.Subtotal:N2}");
                if (r.DiscountAmount > 0)
                    TotalRow("Discount", $"-{currency} {r.DiscountAmount:N2}");
                TotalRow("Tax", $"{currency} {r.TaxAmount:N2}");
                TotalRow("TOTAL", $"{currency} {r.TotalAmount:N2}", bold: true);

                col.Item().PaddingVertical(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                // Payments
                foreach (var p in r.Payments)
                    TotalRow(p.Method, $"{currency} {p.Amount:N2}");
                if (r.Change > 0)
                    TotalRow("Change", $"{currency} {r.Change:N2}");

                col.Item().PaddingVertical(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                col.Item().AlignCenter().PaddingTop(4)
                    .Text("Thank you! Please come again.").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        })).GeneratePdf();

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
