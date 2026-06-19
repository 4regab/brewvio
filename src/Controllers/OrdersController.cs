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
public class OrdersController(OrderService orders) : ControllerBase
{
    // Creates and finalizes a new order (records sale, deducts stock).
    // req: order line items and payment details
    // ct: cancellation token
    // returns: 200 OK with the created order
    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest req, CancellationToken ct) => Ok(await orders.CreateAsync(req, ct));

    // Lists recent orders, optionally filtered by an inclusive date range.
    // take: max number of orders to return (clamped to 1-1000)
    // from: optional start date (inclusive, treated as UTC day)
    // to: optional end date (inclusive day; converted to an exclusive upper bound)
    // ct: cancellation token
    // returns: 200 OK with the list of recent orders
    [HttpGet("recent")]
    public async Task<IActionResult> Recent([FromQuery] int take = 50, [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null, CancellationToken ct = default) =>
        Ok(await orders.RecentAsync(Math.Clamp(take, 1, 1000),
            from != null ? DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc) : null,
            // `to` is treated as an inclusive day -> add a day for an exclusive upper bound (matches reports).
            to != null ? DateTime.SpecifyKind(to.Value.Date, DateTimeKind.Utc).AddDays(1) : null, ct));

    // Exports recent orders as an Excel workbook.
    // take: max number of orders to include (clamped to 1-1000)
    // ct: cancellation token
    // returns: an orders_<timestamp>.xlsx file download
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int take = 200, CancellationToken ct = default) =>
        File(ExportHelper.OrdersXlsx(await orders.RecentAsync(Math.Clamp(take, 1, 1000), ct: ct)),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"orders_{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");

    // Gets the receipt for a single order by id.
    // id: order id
    // ct: cancellation token
    // returns: 200 OK with the order receipt, or 404 if not found
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => await orders.GetReceiptAsync(id, ct) is { } r ? Ok(r) : NotFound();

    // Refunds an order and restores stock.
    // id: order id
    // req: refund reason
    // ct: cancellation token
    // returns: 200 OK with the updated order, or 404 if not found
    [HttpPost("{id:int}/refund")]
    public async Task<IActionResult> Refund(int id, RefundRequest req, CancellationToken ct) =>
        await orders.RefundAsync(id, req.Reason, ct) is { } r ? Ok(r) : NotFound();

    // Advances an order to its next status in the workflow.
    // id: order id
    // ct: cancellation token
    // returns: 200 OK with the updated order, or 404 if not found
    [HttpPost("{id:int}/advance")]
    public async Task<IActionResult> Advance(int id, CancellationToken ct) =>
        await orders.AdvanceStatusAsync(id, ct) is { } r ? Ok(r) : NotFound();

    // Manager-only free status override from Order History (Preparing/Completed/Refunded).
    // A reason is required when the target is Refunded; that path also restores stock.
    [HttpPost("{id:int}/status"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> SetStatus(int id, SetStatusRequest req, CancellationToken ct) =>
        await orders.SetStatusAsync(id, req.Status, req.Reason, ct) is { } r ? Ok(r) : NotFound();

    // Returns the number of orders currently active in the preparation queue.
    // ct: cancellation token
    // returns: 200 OK with { count }
    [HttpGet("queue/count")]
    public async Task<IActionResult> QueueCount(CancellationToken ct) => Ok(new { count = await orders.ActiveQueueCountAsync(ct) });

    // Returns the next order number that will be assigned.
    // ct: cancellation token
    // returns: 200 OK with { nextId }
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

    // Lists saved draft orders.
    // ct: cancellation token
    // returns: 200 OK with the list of draft orders
    [HttpGet("drafts")]
    public async Task<IActionResult> GetDrafts(CancellationToken ct) => Ok(await orders.GetDraftsAsync(ct));

    // Confirms a draft order, finalizing it into a paid order.
    // id: draft order id
    // req: confirmation/payment details
    // ct: cancellation token
    // returns: 200 OK with the confirmed order
    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> ConfirmDraft(int id, ConfirmDraftRequest req, CancellationToken ct) =>
        Ok(await orders.ConfirmDraftAsync(id, req, ct));

    // Deletes a draft order.
    // id: draft order id
    // ct: cancellation token
    // returns: 204 No Content
    [HttpDelete("{id:int}/draft")]
    public async Task<IActionResult> DeleteDraft(int id, CancellationToken ct)
    {
        await orders.DeleteDraftAsync(id, ct);
        return NoContent();
    }
}
