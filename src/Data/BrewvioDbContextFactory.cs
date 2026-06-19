using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Brewvio.Data;

// Used only by `dotnet ef` at design time so it doesn't run Program.cs (or the seeder).
// The connection string is a placeholder; `migrations add` doesn't connect to a database.
public class BrewvioDbContextFactory : IDesignTimeDbContextFactory<BrewvioDbContext>
{
    // Creates a design-time DbContext using connection-string env overrides or a local default.
    // args: design-time arguments passed by the EF tooling (unused)
    // returns: a configured BrewvioDbContext for migrations
    public BrewvioDbContext CreateDbContext(string[] args)
    {
        // `migrations add` never connects, but `database update` does — honor the same
        // ConnectionStrings__Default / DATABASE_URL overrides the app uses, falling back to
        // the local default (local dev Postgres runs on port 5433; see DEPLOYMENT.md §2a).
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Host=localhost;Port=5433;Database=brewvio;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<BrewvioDbContext>()
            .UseNpgsql(cs)
            .Options;
        return new BrewvioDbContext(options);
    }
}
