using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Helpers;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Services;

// User administration — create / update / deactivate accounts and reset passwords (Manager only).
public class UserService(BrewvioDbContext db, AuditService audit)
{
    public async Task<List<UserDto>> ListAsync() =>
        await db.Users.AsNoTracking().OrderBy(u => u.Username)
            .Select(u => new UserDto(u.Id, u.Username, u.FullName, u.Role, u.IsActive, u.Status, u.CreatedAt)).ToListAsync();

    // Pending sign-ups awaiting a Manager decision (registration/approval workflow).
    public async Task<List<PendingUserDto>> ListPendingAsync() =>
        await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Pending).OrderBy(u => u.CreatedAt)
            .Select(u => new PendingUserDto(u.Id, u.Username, u.FullName, u.Role, u.CreatedAt)).ToListAsync();

    // Approve a pending account -> Active + can sign in.
    public async Task<UserDto?> ApproveAsync(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return null;
        if (user.Status != UserStatus.Pending)
            throw new InvalidOperationException("Only pending accounts can be approved.");
        user.Status = UserStatus.Active;
        user.IsActive = true;
        audit.Add("UserApproved", $"{user.Username} ({user.Role}) approved and activated.");
        await db.SaveChangesAsync();
        return new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.Status, user.CreatedAt);
    }

    // Reject a pending account -> Rejected + cannot sign in.
    public async Task<UserDto?> RejectAsync(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return null;
        if (user.Status != UserStatus.Pending)
            throw new InvalidOperationException("Only pending accounts can be rejected.");
        user.Status = UserStatus.Rejected;
        user.IsActive = false;
        audit.Add("UserRejected", $"{user.Username} ({user.Role}) request declined.");
        await db.SaveChangesAsync();
        return new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.Status, user.CreatedAt);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Username) || string.IsNullOrWhiteSpace(r.Password))
            throw new ArgumentException("Username and password are required.");
        if (await db.Users.AnyAsync(u => u.Username == r.Username))
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
        await db.SaveChangesAsync();
        return new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.Status, user.CreatedAt);
    }

    public async Task<UserDto?> UpdateAsync(int id, UpdateUserRequest r)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return null;
        user.FullName = r.FullName;
        user.Role = r.Role == Roles.Manager ? Roles.Manager : Roles.Cashier;
        user.IsActive = r.IsActive;
        // Keep Status in sync for already-decided accounts (don't resurrect Pending/Rejected here).
        if (user.Status == UserStatus.Active || user.Status == UserStatus.Rejected)
            user.Status = r.IsActive ? UserStatus.Active : UserStatus.Rejected;
        audit.Add("UserUpdated", $"{user.Username}: role {user.Role}, active={user.IsActive}");
        await db.SaveChangesAsync();
        return new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.Status, user.CreatedAt);
    }

    public async Task<bool> ResetPasswordAsync(int id, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters.");
        var user = await db.Users.FindAsync(id);
        if (user is null) return false;
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        audit.Add("PasswordReset", $"Password reset for {user.Username}");
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return false;
        // Prevent deleting if user has transactions (integrity)
        if (await db.Transactions.AnyAsync(t => t.CashierId == id))
            throw new InvalidOperationException("Cannot delete a user with existing transactions. Deactivate instead.");
        audit.Add("UserDeleted", $"{user.Username} ({user.Role}) deleted.");
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return true;
    }
}
