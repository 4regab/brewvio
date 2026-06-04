using Brewvio.Dtos;
using Brewvio.Models;
using Brewvio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = Roles.Manager)]
public class UsersController(UserService users) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await users.ListAsync());

    // ----- Registration/approval workflow (Manager only) -----
    [HttpGet("pending")]
    public async Task<IActionResult> Pending() => Ok(await users.ListPendingAsync());

    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id) =>
        await users.ApproveAsync(id) is { } u ? Ok(u) : NotFound();

    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id) =>
        await users.RejectAsync(id) is { } u ? Ok(u) : NotFound();

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest req) => Ok(await users.CreateAsync(req));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest req) =>
        await users.UpdateAsync(id, req) is { } u ? Ok(u) : NotFound();

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest req) =>
        await users.ResetPasswordAsync(id, req.NewPassword) ? NoContent() : NotFound();
}
