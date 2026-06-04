using Brewvio.Dtos;
using Brewvio.Services;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

// POS endpoints — available to any authenticated user (cashier or manager).
[ApiController]
[Route("api/orders")]
public class OrdersController(OrderService orders) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest req) => Ok(await orders.CreateAsync(req));

    [HttpGet("recent")]
    public async Task<IActionResult> Recent([FromQuery] int take = 50) => Ok(await orders.RecentAsync(take));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => await orders.GetReceiptAsync(id) is { } r ? Ok(r) : NotFound();

    [HttpPost("{id:int}/refund")]
    public async Task<IActionResult> Refund(int id, RefundRequest req) =>
        await orders.RefundAsync(id, req.Reason) is { } r ? Ok(r) : NotFound();

    // Pre-payment cancellation — logs the reason for audit, clears the cart client-side.
    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(CancelOrderRequest req)
    {
        await orders.CancelAsync(req.Reason);
        return NoContent();
    }
}
