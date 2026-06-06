using Brewvio.Dtos;
using Brewvio.Models;
using Brewvio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

[ApiController]
[Route("api/menu")]
public class MenuController(MenuService menu) : ControllerBase
{
    // Reads are available to any authenticated user (cashiers need the menu on the POS).
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        Ok(await menu.ListAsync(includeInactive, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => await menu.GetAsync(id, ct) is { } m ? Ok(m) : NotFound();

    [HttpGet("modifiers")]
    public async Task<IActionResult> Modifiers([FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        Ok(await menu.ListModifiersAsync(includeInactive, ct));

    // Mutations are Manager-only.
    [HttpPost, Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Create(MenuItemRequest req, CancellationToken ct) => Ok(await menu.CreateAsync(req, ct));

    [HttpPut("{id:int}"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Update(int id, MenuItemRequest req, CancellationToken ct) =>
        await menu.UpdateAsync(id, req, ct) is { } m ? Ok(m) : NotFound();

    [HttpPost("{id:int}/active"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> SetActive(int id, [FromQuery] bool active, CancellationToken ct = default) =>
        await menu.SetActiveAsync(id, active, ct) ? NoContent() : NotFound();

    [HttpPost("modifiers"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> CreateModifier(ModifierRequest req, CancellationToken ct) => Ok(await menu.CreateModifierAsync(req, ct));

    [HttpPut("modifiers/{id:int}"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> UpdateModifier(int id, ModifierRequest req, CancellationToken ct) =>
        await menu.UpdateModifierAsync(id, req, ct) is { } m ? Ok(m) : NotFound();
}
