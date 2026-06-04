using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brewvio.Migrations
{
    /// <inheritdoc />
    public partial class AddIngredientConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op: `xmin` is a PostgreSQL system column that already exists on every table.
            // EF scaffolds an AddColumn because the model now maps it as a concurrency token, but
            // the physical column is implicit — adding it would fail. The mapping lives in the
            // model snapshot (used for change detection); there is no schema change to apply.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: the `xmin` system column cannot be dropped.
        }
    }
}
