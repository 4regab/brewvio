using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Models;
using Brewvio.Services;

namespace Brewvio.Tests;

public class UserServiceTests
{
    private static UserService Build(TestDb t) =>
        new(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

    [Fact]
    public async Task Pending_list_contains_seeded_signup()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);

        var pending = await Build(t).ListPendingAsync();

        Assert.Contains(pending, p => p.Username == "newcashier");
    }

    [Fact]
    public async Task Approve_activates_pending_account()
    {
        using var t = new TestDb();
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
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var users = Build(t);
        var manager = t.Db.Users.First(u => u.Username == "manager");

        await Assert.ThrowsAsync<InvalidOperationException>(() => users.ApproveAsync(manager.Id));
    }

    [Fact]
    public async Task Reject_marks_account_rejected()
    {
        using var t = new TestDb();
        await DatabaseInitializer.SeedAllAsync(t.Db);
        var users = Build(t);
        var pending = t.Db.Users.First(u => u.Username == "newcashier");

        var dto = await users.RejectAsync(pending.Id);

        Assert.Equal(UserStatus.Rejected, dto!.Status);
        Assert.False(dto.IsActive);
    }
}
