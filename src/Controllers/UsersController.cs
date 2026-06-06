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
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await users.ListAsync(ct));

    // ----- Registration/approval workflow (Manager only) -----
    [HttpGet("pending")]
    public async Task<IActionResult> Pending(CancellationToken ct) => Ok(await users.ListPendingAsync(ct));

    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, CancellationToken ct) =>
        await users.ApproveAsync(id, ct) is { } u ? Ok(u) : NotFound();

    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, CancellationToken ct) =>
        await users.RejectAsync(id, ct) is { } u ? Ok(u) : NotFound();

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest req, CancellationToken ct) => Ok(await users.CreateAsync(req, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest req, CancellationToken ct) =>
        await users.UpdateAsync(id, req, ct) is { } u ? Ok(u) : NotFound();

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest req, CancellationToken ct) =>
        await users.ResetPasswordAsync(id, req.NewPassword, ct) ? NoContent() : NotFound();

    [HttpPost("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await users.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
