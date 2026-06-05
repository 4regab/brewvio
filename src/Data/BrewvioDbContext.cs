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
