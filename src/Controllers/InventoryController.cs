using Brewvio.Dtos;
using Brewvio.Helpers;
using Brewvio.Models;
using Brewvio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventoryController(InventoryService inv) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await inv.ListAsync(ct));

    [HttpGet("low-stock")]
    public async Task<IActionResult> LowStock(CancellationToken ct) => Ok(await inv.LowStockAsync(ct));

    [HttpPost]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Create(IngredientRequest req, CancellationToken ct) => Ok(await inv.CreateAsync(req, ct));

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Update(int id, IngredientRequest req, CancellationToken ct) =>
        await inv.UpdateAsync(id, req, ct) is { } i ? Ok(i) : NotFound();

    // Manual stock-take with a mandatory reason (service throws -> 400 if missing).
    [HttpPost("{id:int}/adjust")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Adjust(int id, StockAdjustRequest req, CancellationToken ct) =>
        await inv.AdjustAsync(id, req, ct) is { } i ? Ok(i) : NotFound();

    // Stock In — add a received/delivered quantity to stock (Manager). Reason optional.
    [HttpPost("{id:int}/stock-in")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> StockIn(int id, StockMovementRequest req, CancellationToken ct) =>
        await inv.StockInAsync(id, req, ct) is { } i ? Ok(i) : NotFound();

    // Stock Out — remove a quantity from stock with a mandatory reason; rejected (400) if it would
    // drive stock below zero (Manager).
    [HttpPost("{id:int}/stock-out")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> StockOut(int id, StockMovementRequest req, CancellationToken ct) =>
        await inv.StockOutAsync(id, req, ct) is { } i ? Ok(i) : NotFound();

    // Per-ingredient stock-movement history (Manager + Cashier — inherits class-level [Authorize]).
    [HttpGet("{id:int}/history")]
    public async Task<IActionResult> History(int id, [FromQuery] int take = 100, CancellationToken ct = default) =>
        Ok(await inv.HistoryAsync(id, Math.Clamp(take, 1, 500), ct));

    // Global stock-movement ledger (Manager + Cashier): paged + filterable by date range [from,to),
    // movement type (action), and ingredient. take clamped 1-200, skip >= 0.
    [HttpGet("movements")]
    public async Task<IActionResult> Movements([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? type, [FromQuery] int? ingredientId, [FromQuery] int skip = 0,
        [FromQuery] int take = 50, CancellationToken ct = default) =>
        Ok(await inv.MovementsAsync(from, to, type, ingredientId, Math.Max(0, skip), Math.Clamp(take, 1, 200), ct));

    // CSV export of the filtered stock-movement ledger (Manager + Cashier).
    [HttpGet("movements/export")]
    public async Task<IActionResult> MovementsExport([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? type, [FromQuery] int? ingredientId, CancellationToken ct = default) =>
        File(ExportHelper.StockMovementsCsv(await inv.MovementsForExportAsync(from, to, type, ingredientId, ct)),
            "text/csv", "stock-movements.csv");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await inv.DeleteAsync(id, ct) ? NoContent() : NotFound();

    [HttpGet("export")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Export(CancellationToken ct) =>
        File(ExportHelper.InventoryCsv(await inv.ListAsync(ct)), "text/csv", "inventory.csv");
}
