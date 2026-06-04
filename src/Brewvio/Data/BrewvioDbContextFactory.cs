using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Brewvio.Data;

// Used only by `dotnet ef` at design time so it doesn't run Program.cs (or the seeder).
// The connection string is a placeholder; `migrations add` doesn't connect to a database.
public class BrewvioDbContextFactory : IDesignTimeDbContextFactory<BrewvioDbContext>
{
    public BrewvioDbContext CreateDbContext(string[] args)
    {
        // `migrations add` never connects, but `database update` does — honor the same
        // ConnectionStrings__Default / DATABASE_URL overrides the app uses, falling back to
        // the local default. (Local dev Postgres runs on 5433; see scripts/pg-setup.sh.)
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Host=localhost;Port=5433;Database=brewvio;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<BrewvioDbContext>()
            .UseNpgsql(cs)
            .Options;
        return new BrewvioDbContext(options);
    }
}
