using Microsoft.EntityFrameworkCore;
using Brewvio.Models;

namespace Brewvio.Data;

public class BrewvioDbContext(DbContextOptions<BrewvioDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<Modifier> Modifiers => Set<Modifier>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionItem> TransactionItems => Set<TransactionItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<AppSetting>().HasKey(s => s.Key);

        // Indexes for the hot read paths (reporting, order history, the POS queue, and the
        // audit log). These columns are filtered/sorted on frequently but were previously
        // unindexed (only the auto FK index on Transaction.CashierId existed):
        //   • Transaction.Timestamp         — ReportingService range scans, RecentAsync ordering, exports
        //   • Transaction (CashierId,Status) — GetDraftsAsync (WHERE CashierId = x AND Status = 'Draft')
        //   • Transaction.Status            — ActiveQueueCountAsync / queue count
        //   • AuditLog.Timestamp            — AuditService.ListAsync ORDER BY Timestamp DESC
        modelBuilder.Entity<Transaction>().HasIndex(t => t.Timestamp);
        modelBuilder.Entity<Transaction>().HasIndex(t => new { t.CashierId, t.Status });
        modelBuilder.Entity<Transaction>().HasIndex(t => t.Status);
        modelBuilder.Entity<AuditLog>().HasIndex(a => a.Timestamp);
        // Per-ingredient stock history reads: WHERE IngredientId = @id ORDER BY Timestamp DESC.
        // Composite (IngredientId, Timestamp) so the filter + ordering are served by one index.
        modelBuilder.Entity<AuditLog>().HasIndex(a => new { a.IngredientId, a.Timestamp });
        // Stock-ledger amounts use the same precision as ingredient quantities (12,3).
        modelBuilder.Entity<AuditLog>().Property(x => x.Quantity).HasPrecision(12, 3);
        modelBuilder.Entity<AuditLog>().Property(x => x.BalanceAfter).HasPrecision(12, 3);

        // Decimal precision — money: (12,2); quantities: (12,3); unit cost: (12,4).
        modelBuilder.Entity<Ingredient>(e =>
        {
            e.Property(x => x.StockLevel).HasPrecision(12, 3);
            e.Property(x => x.Threshold).HasPrecision(12, 3);
            e.Property(x => x.CostPerUnit).HasPrecision(12, 4);
            // Optimistic concurrency on the PostgreSQL system column `xmin` so concurrent stock
            // deductions can't silently overwrite each other (lost update / oversell). A conflicting
            // SaveChanges throws DbUpdateConcurrencyException, which OrderService reloads and retries.
            // Npgsql 10 removed the entity-level UseXminAsConcurrencyToken() shortcut, so we map the
            // `xmin` system column explicitly as a uint shadow property. This is exactly what the
            // shortcut emitted (see the migration snapshot) — it tracks the existing system column
            // without adding a physical column.
            e.Property<uint>("xmin")
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });
        modelBuilder.Entity<MenuItem>().Property(x => x.Price).HasPrecision(12, 2);
        modelBuilder.Entity<Modifier>().Property(x => x.PriceDelta).HasPrecision(12, 2);
        modelBuilder.Entity<RecipeIngredient>().Property(x => x.Quantity).HasPrecision(12, 3);
        modelBuilder.Entity<Transaction>(e =>
        {
            e.Property(x => x.Subtotal).HasPrecision(12, 2);
            e.Property(x => x.DiscountAmount).HasPrecision(12, 2);
            e.Property(x => x.TaxAmount).HasPrecision(12, 2);
            e.Property(x => x.TotalAmount).HasPrecision(12, 2);
        });
        modelBuilder.Entity<TransactionItem>(e =>
        {
            e.Property(x => x.UnitPrice).HasPrecision(12, 2);
            e.Property(x => x.LineTotal).HasPrecision(12, 2);
        });
        modelBuilder.Entity<Payment>().Property(x => x.Amount).HasPrecision(12, 2);

        // Recipe: deleting a menu item removes its recipe rows; ingredients in use can't be deleted.
        modelBuilder.Entity<RecipeIngredient>()
            .HasOne(r => r.MenuItem).WithMany(m => m.Recipe)
            .HasForeignKey(r => r.MenuItemId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<RecipeIngredient>()
            .HasOne(r => r.Ingredient).WithMany(i => i.RecipeIngredients)
            .HasForeignKey(r => r.IngredientId).OnDelete(DeleteBehavior.Restrict);

        // Transaction graph: items/payments cascade; menu item & cashier are protected from delete.
        modelBuilder.Entity<TransactionItem>()
            .HasOne(t => t.Transaction).WithMany(x => x.Items)
            .HasForeignKey(t => t.TransactionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TransactionItem>()
            .HasOne(t => t.MenuItem).WithMany()
            .HasForeignKey(t => t.MenuItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Transaction).WithMany(x => x.Payments)
            .HasForeignKey(p => p.TransactionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Cashier).WithMany()
            .HasForeignKey(t => t.CashierId).OnDelete(DeleteBehavior.Restrict);
    }
}
