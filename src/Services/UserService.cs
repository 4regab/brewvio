using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Helpers;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Services;

// User administration — create / update / deactivate accounts and reset passwords (Manager only).
public class UserService(BrewvioDbContext db, AuditService audit)
{
    // Lists all users (alphabetical by username).
    // ct: cancellation token.
    // returns: all users as DTOs.
    public async Task<List<UserDto>> ListAsync(CancellationToken ct = default) =>
        await db.Users.AsNoTracking().OrderBy(u => u.Username)
            .Select(u => new UserDto(u.Id, u.Username, u.FullName, u.Role, u.IsActive, u.Status, u.CreatedAt)).ToListAsync(ct);

    // Pending sign-ups awaiting a Manager decision (registration/approval workflow).
    public async Task<List<PendingUserDto>> ListPendingAsync(CancellationToken ct = default) =>
        await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Pending).OrderBy(u => u.CreatedAt)
            .Select(u => new PendingUserDto(u.Id, u.Username, u.FullName, u.Role, u.CreatedAt)).ToListAsync(ct);

    // Approve a pending account -> Active + can sign in.
    public async Task<UserDto?> ApproveAsync(int id, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return null;
        if (user.Status != UserStatus.Pending)
            throw new InvalidOperationException("Only pending accounts can be approved.");
        user.Status = UserStatus.Active;
        user.IsActive = true;
        audit.Add("UserApproved", $"{user.Username} ({user.Role}) approved and activated.");
        await db.SaveChangesAsync(ct);
        return new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.Status, user.CreatedAt);
    }

    // Reject a pending account -> Rejected + cannot sign in.
    public async Task<UserDto?> RejectAsync(int id, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return null;
        if (user.Status != UserStatus.Pending)
            throw new InvalidOperationException("Only pending accounts can be rejected.");
        user.Status = UserStatus.Rejected;
        user.IsActive = false;
        // Invalidate any token that may have been issued before this rejection.
        user.TokenIssuedAt = DateTime.UtcNow;
        audit.Add("UserRejected", $"{user.Username} ({user.Role}) request declined.");
        await db.SaveChangesAsync(ct);
        return new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.Status, user.CreatedAt);
    }

    // Creates an active user account (Manager or Cashier) and audits it.
    // r: the new user's username, full name, role, and password.
    // ct: cancellation token.
    // returns: the created user as a DTO.
    // throws ArgumentException: when username or password is missing.
    // throws InvalidOperationException: when the username already exists.
    public async Task<UserDto> CreateAsync(CreateUserRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Username) || string.IsNullOrWhiteSpace(r.Password))
            throw new ArgumentException("Username and password are required.");
        if (await db.Users.AnyAsync(u => u.Username == r.Username, ct))
            throw new InvalidOperationException("Username already exists.");

        var role = r.Role == Roles.Manager ? Roles.Manager : Roles.Cashier;
        var user = new User
        {
            Username = r.Username, FullName = r.FullName, Role = role,
            Status = UserStatus.Active, IsActive = true,
            PasswordHash = PasswordHasher.Hash(r.Password)
        };
        db.Users.Add(user);
        audit.Add("UserCreated", $"{r.Username} ({role})");
        await db.SaveChangesAsync(ct);
        return new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.Status, user.CreatedAt);
    }

    // Updates a user's full name, role, and active state (revoking tokens on deactivation) and audits it.
    // id: the user id to update.
    // r: the new full name, role, and active flag.
    // ct: cancellation token.
    // returns: the updated user DTO, or null if not found.
    public async Task<UserDto?> UpdateAsync(int id, UpdateUserRequest r, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return null;
        user.FullName = r.FullName;
        user.Role = r.Role == Roles.Manager ? Roles.Manager : Roles.Cashier;
        user.IsActive = r.IsActive;
        // Keep Status in sync for already-decided accounts (don't resurrect Pending/Rejected here).
        if (user.Status == UserStatus.Active || user.Status == UserStatus.Rejected)
            user.Status = r.IsActive ? UserStatus.Active : UserStatus.Rejected;
        // If the account is being deactivated, bump TokenIssuedAt so any live token is rejected
        // immediately by the revocation middleware — no need to wait for the 2h JWT expiry.
        if (!r.IsActive)
            user.TokenIssuedAt = DateTime.UtcNow;
        audit.Add("UserUpdated", $"{user.Username}: role {user.Role}, active={user.IsActive}");
        await db.SaveChangesAsync(ct);
        return new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.Status, user.CreatedAt);
    }

    // Resets a user's password and invalidates all existing sessions, then audits it.
    // id: the user id.
    // newPassword: the new password (minimum 8 characters).
    // ct: cancellation token.
    // returns: true if reset, false if the user is not found.
    // throws ArgumentException: when the new password is missing or shorter than 8 characters.
    public async Task<bool> ResetPasswordAsync(int id, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters.");
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return false;
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        // Invalidate all existing sessions — the old password (and its token) is no longer valid.
        user.TokenIssuedAt = DateTime.UtcNow;
        audit.Add("PasswordReset", $"Password reset for {user.Username}");
        await db.SaveChangesAsync(ct);
        return true;
    }

    // Deletes a user, unless they have transactions (deactivate instead), and audits it.
    // id: the user id to delete.
    // ct: cancellation token.
    // returns: true if deleted, false if not found.
    // throws InvalidOperationException: when the user has existing transactions.
    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return false;
        // Prevent deleting if user has transactions (integrity)
        if (await db.Transactions.AnyAsync(t => t.CashierId == id, ct))
            throw new InvalidOperationException("Cannot delete a user with existing transactions. Deactivate instead.");
        audit.Add("UserDeleted", $"{user.Username} ({user.Role}) deleted.");
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
