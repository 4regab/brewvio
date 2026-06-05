using Brewvio.Data;
using Brewvio.Services;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Tests;

public class AuditServiceTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static AuditService Build(TestScope t, int userId = 1, string username = "manager") =>
        new(t.Db, TestSupport.Cur(userId, username, "Manager"));

    // ── ListAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_returns_entries_ordered_newest_first()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);

        await svc.LogAsync("ActionA", "first");
        await svc.LogAsync("ActionB", "second");

        var logs = await svc.ListAsync();

        Assert.Equal("ActionB", logs.First().Action);
        Assert.Equal("ActionA", logs.Last().Action);
    }

    [Fact]
    public async Task ListAsync_respects_take_limit()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);

        for (var i = 0; i < 10; i++)
            await svc.LogAsync("BulkAction", $"entry {i}");

        var limited = await svc.ListAsync(take: 3);

        Assert.Equal(3, limited.Count);
    }

    [Fact]
    public async Task ListAsync_returns_correct_dto_shape()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);

        await svc.LogAsync("TestAction", "some details");

        var entry = (await svc.ListAsync()).First();

        Assert.Equal("TestAction", entry.Action);
        Assert.Equal("some details", entry.Details);
        Assert.Equal("manager", entry.Username);
        Assert.True(entry.Timestamp > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task ListAsync_returns_empty_list_when_no_entries()
    {
        using var t = fixture.Begin();
        // No seed — audit logs table is empty in this rollback scope.

        var logs = await Build(t).ListAsync();

        Assert.Empty(logs);
    }

    // ── Add + LogAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task LogAsync_persists_entry_immediately()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);

        await svc.LogAsync("LoginEvent", "user signed in");

        var verify = t.NewContext();
        Assert.Contains(await verify.AuditLogs.ToListAsync(),
            a => a.Action == "LoginEvent" && a.Details == "user signed in");
    }

    [Fact]
    public async Task Add_records_acting_user_details()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t, userId: 42, username: "specific_user");

        await svc.LogAsync("SomeAction", "details");

        var verify = t.NewContext();
        var entry = await verify.AuditLogs.FirstAsync(a => a.Action == "SomeAction");
        Assert.Equal("specific_user", entry.Username);
        Assert.Equal(42, entry.UserId);
    }

    [Fact]
    public async Task Add_with_zero_user_id_stores_null_user_id()
    {
        // CurrentUser with id=0 should record null UserId (system-level action)
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = new AuditService(t.Db, TestSupport.Cur(0, "system", ""));

        await svc.LogAsync("SystemAction", "background work");

        var verify = t.NewContext();
        var entry = await verify.AuditLogs.FirstAsync(a => a.Action == "SystemAction");
        Assert.Null(entry.UserId);
    }

    [Fact]
    public async Task Default_take_is_200()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);

        for (var i = 0; i < 205; i++)
            await svc.LogAsync("BulkLog", $"item {i}");

        var results = await svc.ListAsync(); // default take = 200

        Assert.Equal(200, results.Count);
    }
}
