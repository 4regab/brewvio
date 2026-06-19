using Brewvio.Helpers;
using Brewvio.Models;
using Brewvio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

// Manager-only sales reporting and report exports (Excel/PDF).
[ApiController]
[Route("api/reports")]
[Authorize(Roles = Roles.Manager)]
public class ReportsController(ReportingService reports, SettingsService settings) : ControllerBase
{
    // Generates a sales report for the requested date range and period grouping.
    // from: optional start date (defaults to 7 days ago)
    // to: optional end date (inclusive day)
    // period: grouping granularity (e.g. "daily")
    // ct: cancellation token
    // returns: 200 OK with the generated report
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string period = "daily", CancellationToken ct = default)
    {
        var (f, t) = Range(from, to);
        return Ok(await reports.GenerateAsync(f, t, period, ct));
    }

    // Exports the sales report as an Excel workbook.
    // from: optional start date (defaults to 7 days ago)
    // to: optional end date (inclusive day)
    // period: grouping granularity (e.g. "daily")
    // ct: cancellation token
    // returns: a sales_<range>.xlsx file download
    [HttpGet("export/csv")]
    public async Task<IActionResult> Csv([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string period = "daily", CancellationToken ct = default)
    {
        var (f, t) = Range(from, to);
        var report = await reports.GenerateAsync(f, t, period, ct);
        return File(ExportHelper.SalesReportXlsx(report, f, t),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"sales_{f:yyyyMMdd}-{t.AddDays(-1):yyyyMMdd}.xlsx");
    }

    // Exports the sales report as a PDF, including store name and currency from settings.
    // from: optional start date (defaults to 7 days ago)
    // to: optional end date (inclusive day)
    // period: grouping granularity (e.g. "daily")
    // ct: cancellation token
    // returns: a sales_<range>.pdf file download
    [HttpGet("export/pdf")]
    public async Task<IActionResult> Pdf([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string period = "daily", CancellationToken ct = default)
    {
        var (f, t) = Range(from, to);
        var report = await reports.GenerateAsync(f, t, period, ct);
        var s = await settings.GetAsync(ct);
        return File(ExportHelper.SalesReportPdf(report, s.StoreName, s.Currency, f, t),
            "application/pdf", $"sales_{f:yyyyMMdd}-{t.AddDays(-1):yyyyMMdd}.pdf");
    }

    // Defaults to the last 7 days; end date is treated as an inclusive day. Postgres needs UTC.
    private static (DateTime from, DateTime to) Range(DateTime? from, DateTime? to)
    {
        var f = DateTime.SpecifyKind((from ?? DateTime.UtcNow.Date.AddDays(-6)).Date, DateTimeKind.Utc);
        var t = DateTime.SpecifyKind((to ?? DateTime.UtcNow.Date).Date.AddDays(1), DateTimeKind.Utc);
        return (f, t);
    }
}
