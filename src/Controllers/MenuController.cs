using Brewvio.Dtos;
using Brewvio.Models;
using Brewvio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

// Menu items and modifiers — reads for all authenticated users, mutations for managers.
[ApiController]
[Route("api/menu")]
public class MenuController(MenuService menu) : ControllerBase
{
    // Reads are available to any authenticated user (cashiers need the menu on the POS).
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        Ok(await menu.ListAsync(includeInactive, ct));

    // Gets a single menu item by id.
    // id: menu item id
    // ct: cancellation token
    // returns: 200 OK with the menu item, or 404 if not found
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => await menu.GetAsync(id, ct) is { } m ? Ok(m) : NotFound();

    // Lists available modifiers.
    // includeInactive: when true, also returns deactivated modifiers
    // ct: cancellation token
    // returns: 200 OK with the list of modifiers
    [HttpGet("modifiers")]
    public async Task<IActionResult> Modifiers([FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        Ok(await menu.ListModifiersAsync(includeInactive, ct));

    // Mutations are Manager-only.
    [HttpPost, Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Create(MenuItemRequest req, CancellationToken ct) => Ok(await menu.CreateAsync(req, ct));

    // Updates an existing menu item (Manager).
    // id: menu item id
    // req: updated menu item details
    // ct: cancellation token
    // returns: 200 OK with the updated menu item, or 404 if not found
    [HttpPut("{id:int}"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Update(int id, MenuItemRequest req, CancellationToken ct) =>
        await menu.UpdateAsync(id, req, ct) is { } m ? Ok(m) : NotFound();

    // Activates or deactivates a menu item (Manager).
    // id: menu item id
    // active: true to activate, false to deactivate
    // ct: cancellation token
    // returns: 204 No Content on success, or 404 if not found
    [HttpPost("{id:int}/active"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> SetActive(int id, [FromQuery] bool active, CancellationToken ct = default) =>
        await menu.SetActiveAsync(id, active, ct) ? NoContent() : NotFound();

    // Creates a new modifier (Manager).
    // req: modifier details
    // ct: cancellation token
    // returns: 200 OK with the created modifier
    [HttpPost("modifiers"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> CreateModifier(ModifierRequest req, CancellationToken ct) => Ok(await menu.CreateModifierAsync(req, ct));

    // Updates an existing modifier (Manager).
    // id: modifier id
    // req: updated modifier details
    // ct: cancellation token
    // returns: 200 OK with the updated modifier, or 404 if not found
    [HttpPut("modifiers/{id:int}"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> UpdateModifier(int id, ModifierRequest req, CancellationToken ct) =>
        await menu.UpdateModifierAsync(id, req, ct) is { } m ? Ok(m) : NotFound();
}
