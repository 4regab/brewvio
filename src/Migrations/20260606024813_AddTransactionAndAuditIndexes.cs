using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brewvio.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionAndAuditIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_CashierId",
                table: "Transactions");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CashierId_Status",
                table: "Transactions",
                columns: new[] { "CashierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Status",
                table: "Transactions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Timestamp",
                table: "Transactions",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_CashierId_Status",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_Status",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_Timestamp",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CashierId",
                table: "Transactions",
                column: "CashierId");
        }
    }
}
