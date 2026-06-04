using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Models;
using Brewvio.Services;

namespace Brewvio.Tests;

public class UserServiceTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static UserService Build(TestScope t) =>
        new(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

    [Fact]
    public async Task Pending_list_contains_seeded_signup()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);

        var pending = await Build(t).ListPendingAsync();

        Assert.Contains(pending, p => p.Username == "newcashier");
    }

    [Fact]
    public async Task Approve_activates_pending_account()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var users = Build(t);
        var pending = t.Db.Users.First(u => u.Username == "newcashier");

        var dto = await users.ApproveAsync(pending.Id);

        Assert.NotNull(dto);
        Assert.Equal(UserStatus.Active, dto!.Status);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public async Task Approving_a_non_pending_account_throws()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var manager = t.Db.Users.First(u => u.Username == "manager");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Build(t).ApproveAsync(manager.Id));
    }

    [Fact]
    public async Task Reject_marks_account_rejected()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var pending = t.Db.Users.First(u => u.Username == "newcashier");

        var dto = await Build(t).RejectAsync(pending.Id);

        Assert.Equal(UserStatus.Rejected, dto!.Status);
        Assert.False(dto.IsActive);
    }

    [Fact]
    public async Task Create_adds_active_account_with_correct_role()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);

        var dto = await Build(t).CreateAsync(new CreateUserRequest("barista", "Barista Joe", "P@ssword1", "Cashier"));

        Assert.NotNull(dto);
        Assert.Equal("barista", dto.Username);
        Assert.Equal("Cashier", dto.Role);
        Assert.True(dto.IsActive);
        Assert.Equal(UserStatus.Active, dto.Status);
    }

    [Fact]
    public async Task Create_throws_on_duplicate_username()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Build(t).CreateAsync(new CreateUserRequest("manager", "Imposter", "P@ssword1", "Cashier")));
    }

    [Fact]
    public async Task Update_changes_fullname_and_role()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var cashier = t.Db.Users.First(u => u.Username == "cashier");

        var dto = await Build(t).UpdateAsync(cashier.Id, new UpdateUserRequest("Senior Cashier", "Cashier", true));

        Assert.NotNull(dto);
        Assert.Equal("Senior Cashier", dto!.FullName);
    }

    [Fact]
    public async Task Update_returns_null_for_missing_user()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);

        Assert.Null(await Build(t).UpdateAsync(999_999, new UpdateUserRequest("X", "Cashier", true)));
    }

    [Fact]
    public async Task ResetPassword_updates_hash_so_new_password_works()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var cashier = t.Db.Users.First(u => u.Username == "cashier");

        var ok = await Build(t).ResetPasswordAsync(cashier.Id, "NewP@ss1");
        Assert.True(ok);

        var auth = new AuthService(t.Db, TestSupport.Config(),
            new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));
        var result = await auth.LoginAsync("cashier", "NewP@ss1");
        Assert.NotNull(result.Response);
    }

    [Fact]
    public async Task ResetPassword_rejects_too_short_password()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var cashier = t.Db.Users.First(u => u.Username == "cashier");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Build(t).ResetPasswordAsync(cashier.Id, "12345"));
    }

    [Fact]
    public async Task ResetPassword_returns_false_for_missing_user()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllAsync(t.Db);

        Assert.False(await Build(t).ResetPasswordAsync(999_999, "P@ssword1!"));
    }
}
