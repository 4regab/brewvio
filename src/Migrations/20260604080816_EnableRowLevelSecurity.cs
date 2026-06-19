using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brewvio.Migrations
{
    /// <summary>
    /// Secures the PostgREST data API on Supabase. The app itself connects as the `postgres`
    /// role (BYPASSRLS) so these policies don't affect its EF Core access; they exist to stop
    /// the `anon`/public Supabase API key from reading or writing application tables directly.
    ///
    /// Every statement is idempotent and guarded so the migration is a safe no-op on a plain
    /// local PostgreSQL (via `dotnet ef database update`), where the Supabase `authenticated`,
    /// `anon` roles and the `rls_auto_enable` function do not exist. (Tests use EnsureCreated()
    /// and never run migrations, so they are unaffected either way.)
    /// </summary>
    /// <inheritdoc />
    public partial class EnableRowLevelSecurity : Migration
    {
        // Application data tables exposed via PostgREST. __EFMigrationsHistory is intentionally
        // excluded (EF bookkeeping, not application data).
        private static readonly string[] Tables =
        {
            "Users", "Ingredients", "MenuItems", "Modifiers", "Settings", "AuditLogs",
            "RecipeIngredients", "Shifts", "Transactions", "Payments", "TransactionItems"
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Enable RLS on every data table, and add a single permissive policy granting full
            //    access ONLY to the `authenticated` role. With RLS on and no policy for `anon`,
            //    the public API key is blocked entirely (deny-by-default). The policy is only
            //    created when the `authenticated` role exists (i.e. on Supabase), so this stays a
            //    no-op on a vanilla local Postgres.
            foreach (var t in Tables)
            {
                migrationBuilder.Sql(
                    $"ALTER TABLE public.\"{t}\" ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql(
                    $"DROP POLICY IF EXISTS authenticated_all_access ON public.\"{t}\";");
                migrationBuilder.Sql(
                    "DO $$ BEGIN " +
                    "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN " +
                    $"CREATE POLICY authenticated_all_access ON public.\"{t}\" " +
                    "AS PERMISSIVE FOR ALL TO authenticated USING (true) WITH CHECK (true); " +
                    "END IF; END $$;");
            }

            // 2) Lock down the pre-existing `rls_auto_enable()` SECURITY DEFINER event-trigger
            //    function: it should never be callable from the public PostgREST API. Revoke
            //    EXECUTE from PUBLIC and the Supabase API roles (guarded so it's a no-op when the
            //    function or roles are absent).
            migrationBuilder.Sql(
                "DO $$ BEGIN " +
                "IF EXISTS (SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace " +
                "WHERE n.nspname = 'public' AND p.proname = 'rls_auto_enable') THEN " +
                "REVOKE EXECUTE ON FUNCTION public.rls_auto_enable() FROM PUBLIC; " +
                "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN " +
                "REVOKE EXECUTE ON FUNCTION public.rls_auto_enable() FROM anon; END IF; " +
                "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN " +
                "REVOKE EXECUTE ON FUNCTION public.rls_auto_enable() FROM authenticated; END IF; " +
                "END IF; END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the policy + RLS changes.
            foreach (var t in Tables)
            {
                migrationBuilder.Sql(
                    $"DROP POLICY IF EXISTS authenticated_all_access ON public.\"{t}\";");
                migrationBuilder.Sql(
                    $"ALTER TABLE public.\"{t}\" DISABLE ROW LEVEL SECURITY;");
            }

            // Restore the prior (default) EXECUTE grants on rls_auto_enable so the rollback is a
            // faithful inverse. Guarded for environments where the function/roles don't exist.
            migrationBuilder.Sql(
                "DO $$ BEGIN " +
                "IF EXISTS (SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace " +
                "WHERE n.nspname = 'public' AND p.proname = 'rls_auto_enable') THEN " +
                "GRANT EXECUTE ON FUNCTION public.rls_auto_enable() TO PUBLIC; " +
                "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN " +
                "GRANT EXECUTE ON FUNCTION public.rls_auto_enable() TO anon; END IF; " +
                "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN " +
                "GRANT EXECUTE ON FUNCTION public.rls_auto_enable() TO authenticated; END IF; " +
                "END IF; END $$;");
        }
    }
}
