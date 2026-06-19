using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Models;
using Brewvio.Services;

namespace Brewvio.Tests;

public class AuthServiceTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static AuthService Build(TestScope t) =>
        new(t.Db, TestSupport.Config(), new AuditService(t.Db, TestSupport.Cur(0, "system", "")));

    [Fact]
    public async Task Valid_credentials_return_token_and_role()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var result = await Build(t).LoginAsync("manager", "Manager@123");

        Assert.NotNull(result.Response);
        Assert.Equal("Manager", result.Response!.Role);
        Assert.False(string.IsNullOrWhiteSpace(result.Response.Token));
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var result = await Build(t).LoginAsync("manager", "wrong-password");
        Assert.Null(result.Response);
        Assert.Equal("Invalid username or password.", result.Error);
    }

    [Fact]
    public async Task Inactive_user_cannot_sign_in()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var cashier = t.Db.Users.First(u => u.Username == "cashier");
        cashier.IsActive = false;
        cashier.Status = UserStatus.Rejected;
        await t.Db.SaveChangesAsync();

        var result = await Build(t).LoginAsync("cashier", "Cashier@123");
        Assert.Null(result.Response);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Pending_account_cannot_sign_in_with_clear_message()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var result = await Build(t).LoginAsync("newcashier", "Pending@123");

        Assert.Null(result.Response);
        Assert.Contains("approval", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_creates_a_pending_account()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var auth = Build(t);

        var res = await auth.RegisterAsync(new RegisterRequest("jamie", "Jamie Cruz", "secret123", "Cashier"));

        Assert.Equal("Pending", res.Status);
        var status = await auth.GetAccountStatusAsync("jamie");
        Assert.Equal("Pending", status!.Status);
        Assert.Null((await auth.LoginAsync("jamie", "secret123")).Response);
    }

    [Fact]
    public async Task Register_rejects_duplicate_username()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Build(t).RegisterAsync(new RegisterRequest("manager", "Imposter", "secret123", "Manager")));
    }

    [Fact]
    public async Task Register_rejects_short_password()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Build(t).RegisterAsync(new RegisterRequest("shorty", "Short Pw", "123", "Cashier")));
    }

    [Fact]
    public async Task Approved_account_can_sign_in()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var auth = Build(t);
        var users = new UserService(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

        var reg = await auth.RegisterAsync(new RegisterRequest("jamie", "Jamie Cruz", "secret123", "Cashier"));
        await users.ApproveAsync(reg.Id);

        var result = await auth.LoginAsync("jamie", "secret123");
        Assert.NotNull(result.Response);
        Assert.Equal("Cashier", result.Response!.Role);
    }

    [Fact]
    public async Task Rejected_account_cannot_sign_in()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var auth = Build(t);
        var users = new UserService(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

        var reg = await auth.RegisterAsync(new RegisterRequest("jamie", "Jamie Cruz", "secret123", "Cashier"));
        await users.RejectAsync(reg.Id);

        var result = await auth.LoginAsync("jamie", "secret123");
        Assert.Null(result.Response);
        Assert.Contains("declined", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAccountStatus_returns_null_for_unknown_username()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        Assert.Null(await Build(t).GetAccountStatusAsync("does-not-exist"));
    }

    [Fact]
    public async Task GetAccountStatus_reflects_approval_transition()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var auth = Build(t);
        var users = new UserService(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

        var reg = await auth.RegisterAsync(new RegisterRequest("statustest", "Status Tester", "secret123", "Cashier"));
        Assert.Equal("Pending", (await auth.GetAccountStatusAsync("statustest"))!.Status);

        await users.ApproveAsync(reg.Id);
        Assert.Equal("Active", (await auth.GetAccountStatusAsync("statustest"))!.Status);
    }
}
