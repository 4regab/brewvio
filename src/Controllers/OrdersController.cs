using Brewvio.Dtos;
using Brewvio.Helpers;
using Brewvio.Models;
using Brewvio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

// POS endpoints — available to any authenticated user (cashier or manager).
[ApiController]
[Route("api/orders")]
public class OrdersController(OrderService orders, SettingsService settings) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest req, CancellationToken ct) => Ok(await orders.CreateAsync(req, ct));

    [HttpGet("recent")]
    public async Task<IActionResult> Recent([FromQuery] int take = 50, [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null, CancellationToken ct = default) =>
        Ok(await orders.RecentAsync(Math.Clamp(take, 1, 1000),
            from != null ? DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc) : null,
            // `to` is treated as an inclusive day -> add a day for an exclusive upper bound (matches reports).
            to != null ? DateTime.SpecifyKind(to.Value.Date, DateTimeKind.Utc).AddDays(1) : null, ct));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int take = 200, CancellationToken ct = default) =>
        File(ExportHelper.OrdersXlsx(await orders.RecentAsync(Math.Clamp(take, 1, 1000), ct: ct)),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"orders_{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => await orders.GetReceiptAsync(id, ct) is { } r ? Ok(r) : NotFound();

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> Pdf(int id, CancellationToken ct)
    {
        var r = await orders.GetReceiptAsync(id, ct);
        if (r is null) return NotFound();
        var store = await settings.GetAsync(ct);
        var pdf = ExportHelper.ReceiptPdf(r, store.StoreName, store.Address, store.Currency);
        return File(pdf, "application/pdf", $"receipt_{id}.pdf");
    }

    [HttpPost("{id:int}/refund")]
    public async Task<IActionResult> Refund(int id, RefundRequest req, CancellationToken ct) =>
        await orders.RefundAsync(id, req.Reason, ct) is { } r ? Ok(r) : NotFound();

    [HttpPost("{id:int}/advance")]
    public async Task<IActionResult> Advance(int id, CancellationToken ct) =>
        await orders.AdvanceStatusAsync(id, ct) is { } r ? Ok(r) : NotFound();

    // Manager-only free status override from Order History (Preparing/Completed/Refunded).
    // A reason is required when the target is Refunded; that path also restores stock.
    [HttpPost("{id:int}/status"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> SetStatus(int id, SetStatusRequest req, CancellationToken ct) =>
        await orders.SetStatusAsync(id, req.Status, req.Reason, ct) is { } r ? Ok(r) : NotFound();

    [HttpGet("queue/count")]
    public async Task<IActionResult> QueueCount(CancellationToken ct) => Ok(new { count = await orders.ActiveQueueCountAsync(ct) });

    [HttpGet("next-number")]
    public async Task<IActionResult> NextNumber(CancellationToken ct) => Ok(new { nextId = await orders.NextOrderNumberAsync(ct) });

    // Pre-payment cancellation — logs the reason for audit, clears the cart client-side.
    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(CancelOrderRequest req, CancellationToken ct)
    {
        await orders.CancelAsync(req.Reason, ct);
        return NoContent();
    }

    // Draft order — save cart without payment or stock deduction.
    [HttpPost("draft")]
    public async Task<IActionResult> SaveDraft(SaveDraftRequest req, CancellationToken ct) => Ok(await orders.SaveDraftAsync(req, ct));

    [HttpGet("drafts")]
    public async Task<IActionResult> GetDrafts(CancellationToken ct) => Ok(await orders.GetDraftsAsync(ct));

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> ConfirmDraft(int id, ConfirmDraftRequest req, CancellationToken ct) =>
        Ok(await orders.ConfirmDraftAsync(id, req, ct));

    [HttpDelete("{id:int}/draft")]
    public async Task<IActionResult> DeleteDraft(int id, CancellationToken ct)
    {
        await orders.DeleteDraftAsync(id, ct);
        return NoContent();
    }
}
