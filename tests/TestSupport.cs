using System.Security.Claims;
using Brewvio.Data;
using Brewvio.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Brewvio.Tests;

// ── Shared database fixture ───────────────────────────────────────────────────
// Creates the schema ONCE per test class (via xUnit IClassFixture<SharedTestDb>)
// and seeds it ONCE. Each individual test wraps its work in a savepoint that is
// rolled back on dispose — giving full isolation without the cost of creating and
// dropping a database for every test.
//
// Usage in a test class:
//
//   public class MyTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
//   {
//       [Fact]
//       public async Task SomeTest()
//       {
//           using var t = fixture.Begin();   // rolls back on dispose
//           await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
//           ...
//       }
//   }
//
// Override the server with BREWVIO_TEST_PG; defaults to the local Docker container.
public sealed class SharedTestDb : IAsyncLifetime
{
    private static readonly string BaseCs =
        Environment.GetEnvironmentVariable("BREWVIO_TEST_PG")
        ?? "Host=localhost;Port=5433;Username=postgres;Password=postgres";

    public string ConnectionString { get; } =
        $"{BaseCs};Database=brewvio_shared_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        using var db = NewContext();
        try { await db.Database.EnsureDeletedAsync(); } catch { /* best-effort */ }
    }

    public BrewvioDbContext NewContext() =>
        new(new DbContextOptionsBuilder<BrewvioDbContext>().UseNpgsql(ConnectionString).Options);

    // Returns a scope that wraps all DB work in a transaction rolled back on dispose.
    // Seeding happens inside the transaction so it's also rolled back.
    public TestScope Begin() => new(this);
}

// A per-test scope: opens a real transaction, seeds the DB, and rolls back on dispose.
// The rollback means no data leaks between tests and there's no DROP/CREATE overhead.
public sealed class TestScope : IDisposable
{
    private readonly NpgsqlConnection _conn;
    private readonly NpgsqlTransaction _tx;
    public BrewvioDbContext Db { get; }

    internal TestScope(SharedTestDb fixture)
    {
        // Open a physical connection and start a transaction manually so we can roll it back.
        _conn = new NpgsqlConnection(fixture.ConnectionString);
        _conn.Open();
        _tx = _conn.BeginTransaction();

        // Share the same connection+transaction with EF so all EF operations join it.
        Db = new BrewvioDbContext(
            new DbContextOptionsBuilder<BrewvioDbContext>()
                .UseNpgsql(_conn)
                .Options);
        Db.Database.UseTransaction(_tx);
    }

    // Opens a second EF context on the SAME connection+transaction so you can verify
    // persisted state without a first-level-cache hit, exactly as the old NewContext() did.
    public BrewvioDbContext NewContext() =>
        new(new DbContextOptionsBuilder<BrewvioDbContext>()
            .UseNpgsql(_conn)
            .Options);

    public void Dispose()
    {
        try { _tx.Rollback(); } catch { /* ignore if already rolled back */ }
        Db.Dispose();
        _tx.Dispose();
        _conn.Dispose();
    }
}

// ── Legacy per-test database (kept for reference / backward compat) ───────────
// Creates and drops an isolated PostgreSQL database per test. Correct but slow
// (~2 s/test). Prefer SharedTestDb + TestScope for new tests.
public sealed class TestDb : IDisposable
{
    private readonly string _cs;
    public BrewvioDbContext Db { get; }

    public TestDb()
    {
        var baseCs = Environment.GetEnvironmentVariable("BREWVIO_TEST_PG")
            ?? "Host=localhost;Port=5433;Username=postgres;Password=postgres";
        _cs = $"{baseCs};Database=brewvio_test_{Guid.NewGuid():N}";
        Db = NewContext();
        Db.Database.EnsureCreated();
    }

    public BrewvioDbContext NewContext() =>
        new(new DbContextOptionsBuilder<BrewvioDbContext>().UseNpgsql(_cs).Options);

    public void Dispose()
    {
        try { Db.Database.EnsureDeleted(); } catch { /* best-effort cleanup */ }
        Db.Dispose();
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────
public static class TestSupport
{
    // Builds a CurrentUser backed by a claims principal, mirroring what AuthService issues.
    public static CurrentUser Cur(int id, string name, string role)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("sub", id.ToString()), new Claim("name", name), new Claim("role", role),
            }, "test")),
        };
        return new CurrentUser(new HttpContextAccessor { HttpContext = ctx });
    }

    public static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "brewvio-test-signing-key-minimum-32-bytes!!",
            ["Jwt:Issuer"] = "brewvio",
            ["Jwt:Audience"] = "brewvio",
        }).Build();
}
