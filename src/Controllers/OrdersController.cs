using Brewvio.Dtos;
using Brewvio.Helpers;
using Brewvio.Services;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

// POS endpoints — available to any authenticated user (cashier or manager).
[ApiController]
[Route("api/orders")]
public class OrdersController(OrderService orders, SettingsService settings) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest req) => Ok(await orders.CreateAsync(req));

    [HttpGet("recent")]
    public async Task<IActionResult> Recent([FromQuery] int take = 50, [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null) =>
        Ok(await orders.RecentAsync(take,
            from != null ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc) : null,
            // `to` is treated as an inclusive day -> add a day for an exclusive upper bound (matches reports).
            to != null ? DateTime.SpecifyKind(to.Value.Date, DateTimeKind.Utc).AddDays(1) : null));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int take = 200) =>
        File(ExportHelper.OrdersXlsx(await orders.RecentAsync(take)),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"orders_{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => await orders.GetReceiptAsync(id) is { } r ? Ok(r) : NotFound();

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> Pdf(int id)
    {
        var r = await orders.GetReceiptAsync(id);
        if (r is null) return NotFound();
        var store = await settings.GetAsync();
        var pdf = ExportHelper.ReceiptPdf(r, store.StoreName, store.Address, store.Currency);
        return File(pdf, "application/pdf", $"receipt_{id}.pdf");
    }

    [HttpPost("{id:int}/refund")]
    public async Task<IActionResult> Refund(int id, RefundRequest req) =>
        await orders.RefundAsync(id, req.Reason) is { } r ? Ok(r) : NotFound();

    [HttpPost("{id:int}/advance")]
    public async Task<IActionResult> Advance(int id) =>
        await orders.AdvanceStatusAsync(id) is { } r ? Ok(r) : NotFound();

    [HttpGet("queue/count")]
    public async Task<IActionResult> QueueCount() => Ok(new { count = await orders.ActiveQueueCountAsync() });

    [HttpGet("next-number")]
    public async Task<IActionResult> NextNumber() => Ok(new { nextId = await orders.NextOrderNumberAsync() });

    // Pre-payment cancellation — logs the reason for audit, clears the cart client-side.
    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(CancelOrderRequest req)
    {
        await orders.CancelAsync(req.Reason);
        return NoContent();
    }

    // Draft order — save cart without payment or stock deduction.
    [HttpPost("draft")]
    public async Task<IActionResult> SaveDraft(SaveDraftRequest req) => Ok(await orders.SaveDraftAsync(req));

    [HttpGet("drafts")]
    public async Task<IActionResult> GetDrafts() => Ok(await orders.GetDraftsAsync());

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> ConfirmDraft(int id, ConfirmDraftRequest req) =>
        Ok(await orders.ConfirmDraftAsync(id, req));

    [HttpDelete("{id:int}/draft")]
    public async Task<IActionResult> DeleteDraft(int id)
    {
        await orders.DeleteDraftAsync(id);
        return NoContent();
    }
}
