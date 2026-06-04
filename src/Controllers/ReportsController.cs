using Brewvio.Helpers;
using Brewvio.Models;
using Brewvio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = Roles.Manager)]
public class ReportsController(ReportingService reports, SettingsService settings) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string period = "daily")
    {
        var (f, t) = Range(from, to);
        return Ok(await reports.GenerateAsync(f, t, period));
    }

    [HttpGet("export/csv")]
    public async Task<IActionResult> Csv([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string period = "daily")
    {
        var (f, t) = Range(from, to);
        var report = await reports.GenerateAsync(f, t, period);
        return File(ExportHelper.SalesReportCsv(report), "text/csv", $"sales_{f:yyyyMMdd}-{t.AddDays(-1):yyyyMMdd}.csv");
    }

    [HttpGet("export/pdf")]
    public async Task<IActionResult> Pdf([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string period = "daily")
    {
        var (f, t) = Range(from, to);
        var report = await reports.GenerateAsync(f, t, period);
        var s = await settings.GetAsync();
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
