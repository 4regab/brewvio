using System.Security.Claims;
using Brewvio.Data;
using Brewvio.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Brewvio.Tests;

// Spins up an isolated PostgreSQL database per test (README architecture: EF Core / Npgsql — no SQLite).
// Override the server via the BREWVIO_TEST_PG environment variable; defaults to the local Docker container.
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
