using Brewvio.Dtos;
using Brewvio.Models;
using Brewvio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

// Manager-only user management — listing, registration approval, and account maintenance.
[ApiController]
[Route("api/users")]
[Authorize(Roles = Roles.Manager)]
public class UsersController(UserService users) : ControllerBase
{
    // Lists all users.
    // ct: cancellation token
    // returns: 200 OK with the list of users
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await users.ListAsync(ct));

    // ----- Registration/approval workflow (Manager only) -----
    // Lists users awaiting registration approval.
    // ct: cancellation token
    // returns: 200 OK with the list of pending users
    [HttpGet("pending")]
    public async Task<IActionResult> Pending(CancellationToken ct) => Ok(await users.ListPendingAsync(ct));

    // Approves a pending user registration.
    // id: user id
    // ct: cancellation token
    // returns: 200 OK with the approved user, or 404 if not found
    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, CancellationToken ct) =>
        await users.ApproveAsync(id, ct) is { } u ? Ok(u) : NotFound();

    // Rejects a pending user registration.
    // id: user id
    // ct: cancellation token
    // returns: 200 OK with the rejected user, or 404 if not found
    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, CancellationToken ct) =>
        await users.RejectAsync(id, ct) is { } u ? Ok(u) : NotFound();

    // Creates a new user.
    // req: new user details
    // ct: cancellation token
    // returns: 200 OK with the created user
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest req, CancellationToken ct) => Ok(await users.CreateAsync(req, ct));

    // Updates an existing user.
    // id: user id
    // req: updated user details
    // ct: cancellation token
    // returns: 200 OK with the updated user, or 404 if not found
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest req, CancellationToken ct) =>
        await users.UpdateAsync(id, req, ct) is { } u ? Ok(u) : NotFound();

    // Resets a user's password.
    // id: user id
    // req: the new password
    // ct: cancellation token
    // returns: 204 No Content on success, or 404 if not found
    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest req, CancellationToken ct) =>
        await users.ResetPasswordAsync(id, req.NewPassword, ct) ? NoContent() : NotFound();

    // Deletes a user.
    // id: user id
    // ct: cancellation token
    // returns: 204 No Content on success, or 404 if not found
    [HttpPost("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await users.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
