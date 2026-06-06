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

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await inv.DeleteAsync(id, ct) ? NoContent() : NotFound();

    [HttpGet("export")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Export(CancellationToken ct) =>
        File(ExportHelper.InventoryCsv(await inv.ListAsync(ct)), "text/csv", "inventory.csv");
}
