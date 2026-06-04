using Brewvio.Helpers;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Data;

public static class DatabaseInitializer
{
    // Seeds a full demo dataset when the database is empty. Never crashes startup:
    // if the database/table isn't reachable yet, it logs and continues (run EF migrations first).
    public static async Task SeedAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewvioDbContext>();
        try
        {
            if (await db.Users.AnyAsync()) return;
            await SeedAllAsync(db);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Seeding skipped (database not ready? run EF migrations).");
        }
    }

    public static async Task SeedAllAsync(BrewvioDbContext db)
    {
        // ----- Users -----
        db.Users.AddRange(
            new User { Username = "manager", FullName = "Store Manager", Role = Roles.Manager, Status = UserStatus.Active, IsActive = true, PasswordHash = PasswordHasher.Hash("Manager@123") },
            new User { Username = "cashier", FullName = "Front Cashier", Role = Roles.Cashier, Status = UserStatus.Active, IsActive = true, PasswordHash = PasswordHasher.Hash("Cashier@123") },
            // A demo pending sign-up so the Manager's approval queue isn't empty on first run.
            new User { Username = "newcashier", FullName = "Jamie Pending", Role = Roles.Cashier, Status = UserStatus.Pending, IsActive = false, PasswordHash = PasswordHasher.Hash("Pending@123") });

        // ----- Ingredients (Inventory) -----
        Ingredient Ing(string code, string name, string category, string unit, decimal stock, decimal threshold, decimal cost) =>
            new() { Code = code, Name = name, Category = category, Unit = unit, StockLevel = stock, Threshold = threshold, CostPerUnit = cost };

        var beans = Ing("BEAN-01", "Espresso Beans", "Coffee", "g", 5000, 1000, 0.50m);
        var brewed = Ing("BREW-01", "Brewed Coffee", "Coffee", "ml", 8000, 2000, 0.02m);
        var milk = Ing("MILK-01", "Whole Milk", "Dairy", "ml", 10000, 2000, 0.08m);
        var oat = Ing("MILK-02", "Oat Milk", "Dairy", "ml", 4000, 1000, 0.15m);
        var almond = Ing("MILK-03", "Almond Milk", "Dairy", "ml", 3000, 800, 0.14m);
        var vanilla = Ing("SYRP-01", "Vanilla Syrup", "Syrup", "ml", 2000, 500, 0.10m);
        var caramel = Ing("SYRP-02", "Caramel Syrup", "Syrup", "ml", 2000, 500, 0.10m);
        var chocolate = Ing("SYRP-03", "Chocolate Syrup", "Syrup", "ml", 2000, 500, 0.12m);
        var matcha = Ing("POWD-01", "Matcha Powder", "Powder", "g", 800, 300, 1.20m);
        var tea = Ing("TEA-01", "Tea Leaves", "Tea", "g", 1500, 400, 0.30m);
        var sugar = Ing("SUGR-01", "Sugar", "Pantry", "g", 6000, 1000, 0.02m);
        var ice = Ing("ICE-01", "Ice", "Pantry", "g", 20000, 5000, 0.005m);
        var whip = Ing("CREM-01", "Whipped Cream", "Dairy", "ml", 2000, 500, 0.05m);
        var cup = Ing("SUPP-01", "Paper Cup (12oz)", "Supplies", "pc", 500, 100, 2.50m);
        db.Ingredients.AddRange(beans, brewed, milk, oat, almond, vanilla, caramel, chocolate,
            matcha, tea, sugar, ice, whip, cup);

        // ----- Menu items & recipes -----
        RecipeIngredient R(Ingredient i, decimal qty) => new() { Ingredient = i, Quantity = qty };
        MenuItem Item(string name, string cat, decimal price, params RecipeIngredient[] recipe) =>
            new() { Name = name, Category = cat, Price = price, Recipe = recipe.ToList() };

        db.MenuItems.AddRange(
            Item("Espresso", "Espresso", 90m, R(beans, 18), R(cup, 1)),
            Item("Americano", "Espresso", 110m, R(beans, 18), R(cup, 1)),
            Item("Cappuccino", "Espresso", 130m, R(beans, 18), R(milk, 120), R(cup, 1)),
            Item("Caffe Latte", "Espresso", 140m, R(beans, 18), R(milk, 200), R(cup, 1)),
            Item("Flat White", "Espresso", 150m, R(beans, 27), R(milk, 150), R(cup, 1)),
            Item("Caramel Macchiato", "Espresso", 160m, R(beans, 18), R(milk, 200), R(caramel, 20), R(cup, 1)),
            Item("Cafe Mocha", "Espresso", 160m, R(beans, 18), R(milk, 180), R(chocolate, 25), R(cup, 1)),
            Item("Iced Latte", "Cold", 150m, R(beans, 18), R(milk, 150), R(ice, 150), R(cup, 1)),
            Item("Brewed Coffee", "Coffee", 100m, R(brewed, 240), R(cup, 1)),
            Item("Matcha Latte", "Non-Coffee", 150m, R(matcha, 8), R(milk, 200), R(sugar, 10), R(cup, 1)),
            Item("Hot Chocolate", "Non-Coffee", 130m, R(chocolate, 40), R(milk, 220), R(cup, 1)),
            Item("Iced Tea", "Tea", 90m, R(tea, 5), R(ice, 200), R(sugar, 15), R(cup, 1)));

        // ----- Modifiers (price-only add-ons/choices) -----
        Modifier Mod(string name, string group, decimal delta) =>
            new() { Name = name, GroupName = group, PriceDelta = delta };
        db.Modifiers.AddRange(
            Mod("Oat Milk", "Milk", 20m), Mod("Almond Milk", "Milk", 20m),
            Mod("Vanilla Syrup", "Syrup", 15m), Mod("Caramel Syrup", "Syrup", 15m),
            Mod("Upsize (Large)", "Size", 30m), Mod("Extra Shot", "Extras", 25m),
            Mod("Whipped Cream", "Extras", 20m), Mod("Less Sugar", "Preference", 0m));

        // ----- Settings -----
        db.Settings.AddRange(
            new AppSetting { Key = "StoreName", Value = "Brewvio Coffee" },
            new AppSetting { Key = "Address", Value = "PUP Quezon City Campus" },
            new AppSetting { Key = "Currency", Value = "PHP" },
            new AppSetting { Key = "TaxRatePercent", Value = "12" });

        await db.SaveChangesAsync();
    }
}
