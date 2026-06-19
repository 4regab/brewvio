using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Models;
using Brewvio.Services;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Tests;

/// <summary>
/// Additional edge-case tests for UserService covering methods with no existing test coverage:
/// ListAsync, DeleteAsync (happy path + transaction guard), null-return paths for
/// ApproveAsync/RejectAsync, and UpdateAsync status-sync behaviour.
/// </summary>
public class UserServiceExtendedTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static UserService Build(TestScope t) =>
        new(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

    // ── ListAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_returns_all_users_ordered_by_username()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var users = await Build(t).ListAsync();

        Assert.True(users.Count >= 3); // manager, cashier, newcashier
        for (var i = 1; i < users.Count; i++)
            Assert.True(string.Compare(users[i - 1].Username, users[i].Username, StringComparison.OrdinalIgnoreCase) <= 0);
    }

    [Fact]
    public async Task ListAsync_includes_all_statuses()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var users = await Build(t).ListAsync();

        Assert.Contains(users, u => u.Status == UserStatus.Active);
        Assert.Contains(users, u => u.Status == UserStatus.Pending);
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_removes_user_with_no_transactions()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);

        // Create a fresh user that has never placed any orders
        var newUser = await svc.CreateAsync(new CreateUserRequest("todelete", "To Delete", "P@ssword1!", "Cashier"));

        var result = await svc.DeleteAsync(newUser.Id);

        Assert.True(result);
        Assert.Null(await t.NewContext().Users.FindAsync(newUser.Id));
    }

    [Fact]
    public async Task Delete_returns_false_for_missing_user()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        Assert.False(await Build(t).DeleteAsync(999_999));
    }

    [Fact]
    public async Task Delete_throws_when_user_has_transactions()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var cashier = t.Db.Users.First(u => u.Username == "cashier");

        // Insert a transaction referencing the cashier so FK guard fires
        t.Db.Transactions.Add(new Transaction
        {
            Timestamp = DateTime.UtcNow, Subtotal = 100m, DiscountAmount = 0m,
            TaxAmount = 0m, TotalAmount = 100m, PaymentMethod = "Cash",
            CashierId = cashier.Id, Status = "Completed"
        });
        await t.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Build(t).DeleteAsync(cashier.Id));
    }

    [Fact]
    public async Task Delete_writes_audit_entry()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var newUser = await svc.CreateAsync(new CreateUserRequest("auditcheck", "Audit Check", "P@ssword1!", "Cashier"));

        await svc.DeleteAsync(newUser.Id);

        var verify = t.NewContext();
        Assert.Contains(await verify.AuditLogs.ToListAsync(),
            a => a.Action == "UserDeleted" && a.Details.Contains("auditcheck"));
    }

    // ── ApproveAsync null path ───────────────────────────────────────────────

    [Fact]
    public async Task Approve_returns_null_for_missing_user()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        Assert.Null(await Build(t).ApproveAsync(999_999));
    }

    // ── RejectAsync null path ────────────────────────────────────────────────

    [Fact]
    public async Task Reject_returns_null_for_missing_user()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        Assert.Null(await Build(t).RejectAsync(999_999));
    }

    [Fact]
    public async Task Reject_throws_for_non_pending_account()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var manager = t.Db.Users.First(u => u.Username == "manager");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Build(t).RejectAsync(manager.Id));
    }

    // ── UpdateAsync status sync ──────────────────────────────────────────────

    [Fact]
    public async Task Update_deactivating_active_user_sets_rejected_status()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var cashier = t.Db.Users.First(u => u.Username == "cashier");

        var result = await Build(t).UpdateAsync(cashier.Id, new UpdateUserRequest("Front Cashier", "Cashier", false));

        Assert.NotNull(result);
        Assert.False(result!.IsActive);
        Assert.Equal(UserStatus.Rejected, result.Status);
    }

    [Fact]
    public async Task Update_reactivating_user_restores_active_status()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var cashier = t.Db.Users.First(u => u.Username == "cashier");

        // Deactivate first
        await svc.UpdateAsync(cashier.Id, new UpdateUserRequest("Front Cashier", "Cashier", false));
        // Reactivate
        var result = await svc.UpdateAsync(cashier.Id, new UpdateUserRequest("Front Cashier", "Cashier", true));

        Assert.True(result!.IsActive);
        Assert.Equal(UserStatus.Active, result.Status);
    }

    [Fact]
    public async Task Create_with_blank_username_throws()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Build(t).CreateAsync(new CreateUserRequest("  ", "Full Name", "P@ssword1!", "Cashier")));
    }

    [Fact]
    public async Task Create_with_blank_password_throws()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Build(t).CreateAsync(new CreateUserRequest("validuser", "Full Name", "", "Cashier")));
    }

    [Fact]
    public async Task Create_unknown_role_defaults_to_cashier()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var dto = await Build(t).CreateAsync(
            new CreateUserRequest("badrole", "Bad Role", "P@ssword1!", "Admin"));

        Assert.Equal(Roles.Cashier, dto.Role);
    }

    [Fact]
    public async Task ResetPassword_minimum_8_chars_succeeds()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var cashier = t.Db.Users.First(u => u.Username == "cashier");

        var result = await Build(t).ResetPasswordAsync(cashier.Id, "12345678");

        Assert.True(result);
    }

    [Fact]
    public async Task ResetPassword_exactly_7_chars_throws()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var cashier = t.Db.Users.First(u => u.Username == "cashier");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Build(t).ResetPasswordAsync(cashier.Id, "1234567"));
    }
}
