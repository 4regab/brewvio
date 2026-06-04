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
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false) =>
        Ok(await menu.ListAsync(includeInactive));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => await menu.GetAsync(id) is { } m ? Ok(m) : NotFound();

    [HttpGet("modifiers")]
    public async Task<IActionResult> Modifiers([FromQuery] bool includeInactive = false) =>
        Ok(await menu.ListModifiersAsync(includeInactive));

    // Mutations are Manager-only.
    [HttpPost, Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Create(MenuItemRequest req) => Ok(await menu.CreateAsync(req));

    [HttpPut("{id:int}"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Update(int id, MenuItemRequest req) =>
        await menu.UpdateAsync(id, req) is { } m ? Ok(m) : NotFound();

    [HttpPost("{id:int}/active"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> SetActive(int id, [FromQuery] bool active) =>
        await menu.SetActiveAsync(id, active) ? NoContent() : NotFound();

    [HttpPost("modifiers"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> CreateModifier(ModifierRequest req) => Ok(await menu.CreateModifierAsync(req));

    [HttpPut("modifiers/{id:int}"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> UpdateModifier(int id, ModifierRequest req) =>
        await menu.UpdateModifierAsync(id, req) is { } m ? Ok(m) : NotFound();
}
