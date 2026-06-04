using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Models;
using Brewvio.Services;

namespace Brewvio.Tests;

public class AuthServiceTests
{
    private static AuthService Build(TestDb t) =>
        new(t.Db, TestSupport.Config(), new AuditService(t.Db, TestSupport.Cur(0, "system", "")));

    [Fact]
    public async Task Valid_credentials_return_token_and_role()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);

        var result = await Build(t).LoginAsync("manager", "Manager@123");

        Assert.NotNull(result.Response);
        Assert.Equal("Manager", result.Response!.Role);
        Assert.False(string.IsNullOrWhiteSpace(result.Response.Token));
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);

        var result = await Build(t).LoginAsync("manager", "wrong-password");
        Assert.Null(result.Response);
        Assert.Equal("Invalid username or password.", result.Error);
    }

    [Fact]
    public async Task Inactive_user_cannot_sign_in()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
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
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);

        // The seeded "newcashier" is Pending.
        var result = await Build(t).LoginAsync("newcashier", "Pending@123");

        Assert.Null(result.Response);
        Assert.Contains("approval", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_creates_a_pending_account()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var auth = Build(t);

        var res = await auth.RegisterAsync(new RegisterRequest("jamie", "Jamie Cruz", "secret123", "Cashier"));

        Assert.Equal("Pending", res.Status);
        var status = await auth.GetAccountStatusAsync("jamie");
        Assert.Equal("Pending", status!.Status);
        // A pending account cannot sign in yet.
        Assert.Null((await auth.LoginAsync("jamie", "secret123")).Response);
    }

    [Fact]
    public async Task Register_rejects_duplicate_username()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var auth = Build(t);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            auth.RegisterAsync(new RegisterRequest("manager", "Imposter", "secret123", "Manager")));
    }

    [Fact]
    public async Task Register_rejects_short_password()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var auth = Build(t);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            auth.RegisterAsync(new RegisterRequest("shorty", "Short Pw", "123", "Cashier")));
    }

    [Fact]
    public async Task Approved_account_can_sign_in()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
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
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var auth = Build(t);
        var users = new UserService(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

        var reg = await auth.RegisterAsync(new RegisterRequest("jamie", "Jamie Cruz", "secret123", "Cashier"));
        await users.RejectAsync(reg.Id);

        var result = await auth.LoginAsync("jamie", "secret123");
        Assert.Null(result.Response);
        Assert.Contains("declined", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
