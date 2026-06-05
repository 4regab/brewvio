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
    public async Task<IActionResult> List() => Ok(await inv.ListAsync());

    [HttpGet("low-stock")]
    public async Task<IActionResult> LowStock() => Ok(await inv.LowStockAsync());

    [HttpPost]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Create(IngredientRequest req) => Ok(await inv.CreateAsync(req));

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Update(int id, IngredientRequest req) =>
        await inv.UpdateAsync(id, req) is { } i ? Ok(i) : NotFound();

    // Manual stock-take with a mandatory reason (service throws -> 400 if missing).
    [HttpPost("{id:int}/adjust")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Adjust(int id, StockAdjustRequest req) =>
        await inv.AdjustAsync(id, req) is { } i ? Ok(i) : NotFound();

    [HttpGet("export")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Export() =>
        File(ExportHelper.InventoryCsv(await inv.ListAsync()), "text/csv", "inventory.csv");
}
