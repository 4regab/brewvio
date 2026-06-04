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
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionItem> TransactionItems => Set<TransactionItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Username).IsUnique();
        b.Entity<AppSetting>().HasKey(s => s.Key);

        // Decimal precision — money: (12,2); quantities: (12,3); unit cost: (12,4).
        b.Entity<Ingredient>(e =>
        {
            e.Property(x => x.StockLevel).HasPrecision(12, 3);
            e.Property(x => x.Threshold).HasPrecision(12, 3);
            e.Property(x => x.CostPerUnit).HasPrecision(12, 4);
            // Optimistic concurrency on the PostgreSQL system column `xmin` so concurrent stock
            // deductions can't silently overwrite each other (lost update / oversell). A conflicting
            // SaveChanges throws DbUpdateConcurrencyException, which OrderService reloads and retries.
            // UseXminAsConcurrencyToken is the idiomatic Npgsql 8 mapping (it tracks the existing
            // system column without adding one); the obsolete hint points at a future EF direction.
#pragma warning disable CS0618
            e.UseXminAsConcurrencyToken();
#pragma warning restore CS0618
        });
        b.Entity<MenuItem>().Property(x => x.Price).HasPrecision(12, 2);
        b.Entity<Modifier>().Property(x => x.PriceDelta).HasPrecision(12, 2);
        b.Entity<RecipeIngredient>().Property(x => x.Quantity).HasPrecision(12, 3);
        b.Entity<Shift>(e =>
        {
            e.Property(x => x.StartingCash).HasPrecision(12, 2);
            e.Property(x => x.EndingCash).HasPrecision(12, 2);
        });
        b.Entity<Transaction>(e =>
        {
            e.Property(x => x.Subtotal).HasPrecision(12, 2);
            e.Property(x => x.DiscountAmount).HasPrecision(12, 2);
            e.Property(x => x.TaxAmount).HasPrecision(12, 2);
            e.Property(x => x.TotalAmount).HasPrecision(12, 2);
        });
        b.Entity<TransactionItem>(e =>
        {
            e.Property(x => x.UnitPrice).HasPrecision(12, 2);
            e.Property(x => x.LineTotal).HasPrecision(12, 2);
        });
        b.Entity<Payment>().Property(x => x.Amount).HasPrecision(12, 2);

        // Recipe: deleting a menu item removes its recipe rows; ingredients in use can't be deleted.
        b.Entity<RecipeIngredient>()
            .HasOne(r => r.MenuItem).WithMany(m => m.Recipe)
            .HasForeignKey(r => r.MenuItemId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RecipeIngredient>()
            .HasOne(r => r.Ingredient).WithMany(i => i.RecipeIngredients)
            .HasForeignKey(r => r.IngredientId).OnDelete(DeleteBehavior.Restrict);

        // Transaction graph: items/payments cascade; menu item & cashier are protected from delete.
        b.Entity<TransactionItem>()
            .HasOne(t => t.Transaction).WithMany(x => x.Items)
            .HasForeignKey(t => t.TransactionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<TransactionItem>()
            .HasOne(t => t.MenuItem).WithMany()
            .HasForeignKey(t => t.MenuItemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Payment>()
            .HasOne(p => p.Transaction).WithMany(x => x.Payments)
            .HasForeignKey(p => p.TransactionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Transaction>()
            .HasOne(t => t.Cashier).WithMany()
            .HasForeignKey(t => t.CashierId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Transaction>()
            .HasOne(t => t.Shift).WithMany(s => s.Transactions)
            .HasForeignKey(t => t.ShiftId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Shift>()
            .HasOne(s => s.Cashier).WithMany()
            .HasForeignKey(s => s.CashierId).OnDelete(DeleteBehavior.Restrict);
    }
}
