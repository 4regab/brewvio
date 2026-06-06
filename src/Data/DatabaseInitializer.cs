using Brewvio.Helpers;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Data;

public static class DatabaseInitializer
{
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

    public static Task SeedAllOriginalAsync(BrewvioDbContext db) => SeedTestDataAsync(db);

    /// <summary>
    /// Legacy test seed data — provides the old menu items/ingredients that existing tests depend on.
    /// </summary>
    public static async Task SeedTestDataAsync(BrewvioDbContext db)
    {
        // ----- Users (same across both seeds) -----
        db.Users.AddRange(
            new User { Username = "manager", FullName = "Store Manager", Role = Roles.Manager, Status = UserStatus.Active, IsActive = true, PasswordHash = PasswordHasher.Hash("Manager@123") },
            new User { Username = "cashier", FullName = "Front Cashier", Role = Roles.Cashier, Status = UserStatus.Active, IsActive = true, PasswordHash = PasswordHasher.Hash("Cashier@123") },
            new User { Username = "newcashier", FullName = "Jamie Pending", Role = Roles.Cashier, Status = UserStatus.Pending, IsActive = false, PasswordHash = PasswordHasher.Hash("Pending@123") });

        // ----- Ingredients (test set) -----
        var milk = new Ingredient { Code = "ING-01", Name = "Whole Milk", Category = "Dairy", Unit = "ml", StockLevel = 5000, Threshold = 1000, CostPerUnit = 0.08m };
        var beans = new Ingredient { Code = "ING-02", Name = "Espresso Beans", Category = "Coffee", Unit = "g", StockLevel = 3000, Threshold = 500, CostPerUnit = 0.50m };
        var cup = new Ingredient { Code = "ING-03", Name = "Paper Cup (12oz)", Category = "Supplies", Unit = "pc", StockLevel = 500, Threshold = 100, CostPerUnit = 2.50m };
        var sugar = new Ingredient { Code = "ING-04", Name = "Sugar", Category = "Condiments", Unit = "g", StockLevel = 5000, Threshold = 1000, CostPerUnit = 0.02m };
        db.Ingredients.AddRange(milk, beans, cup, sugar);

        RecipeIngredient R(Ingredient i, decimal qty) => new() { Ingredient = i, Quantity = qty };

        // ----- Menu Items (test set) -----
        db.MenuItems.AddRange(
            new MenuItem { Name = "Caffe Latte", Category = "Espresso", Price = 140m, Recipe = new List<RecipeIngredient> { R(milk, 200), R(beans, 18), R(cup, 1) } },
            new MenuItem { Name = "Espresso", Category = "Espresso", Price = 100m, Recipe = new List<RecipeIngredient> { R(beans, 18), R(cup, 1) } });

        // ----- Modifiers -----
        db.Modifiers.AddRange(
            new Modifier { Name = "Extra Shot", GroupName = "Add-ons", PriceDelta = 30m },
            new Modifier { Name = "Oat Milk", GroupName = "Milk", PriceDelta = 25m });

        // ----- Settings -----
        db.Settings.AddRange(
            new AppSetting { Key = "StoreName", Value = "Chao & Brew" },
            new AppSetting { Key = "Address", Value = "PUP Quezon City Campus" },
            new AppSetting { Key = "Currency", Value = "PHP" },
            new AppSetting { Key = "TaxRatePercent", Value = "12" });

        await db.SaveChangesAsync();
    }

    public static async Task SeedAllAsync(BrewvioDbContext db)
    {
        // ----- Users -----
        db.Users.AddRange(
            new User { Username = "manager", FullName = "Store Manager", Role = Roles.Manager, Status = UserStatus.Active, IsActive = true, PasswordHash = PasswordHasher.Hash("Manager@123") },
            new User { Username = "cashier", FullName = "Front Cashier", Role = Roles.Cashier, Status = UserStatus.Active, IsActive = true, PasswordHash = PasswordHasher.Hash("Cashier@123") });

        // ----- Ingredients -----
        var rice     = new Ingredient { Code = "FOOD-01", Name = "Rice",           Category = "Food",     Unit = "serving", StockLevel = 500,   Threshold = 50,  CostPerUnit = 8m };
        var pork     = new Ingredient { Code = "FOOD-02", Name = "Pork",           Category = "Food",     Unit = "serving", StockLevel = 200,   Threshold = 30,  CostPerUnit = 50m };
        var chicken  = new Ingredient { Code = "FOOD-03", Name = "Chicken",        Category = "Food",     Unit = "serving", StockLevel = 200,   Threshold = 30,  CostPerUnit = 40m };
        var egg      = new Ingredient { Code = "FOOD-04", Name = "Egg",            Category = "Food",     Unit = "pc",      StockLevel = 300,   Threshold = 50,  CostPerUnit = 8m };
        var noodles  = new Ingredient { Code = "FOOD-05", Name = "Noodles",        Category = "Food",     Unit = "serving", StockLevel = 300,   Threshold = 50,  CostPerUnit = 15m };
        var coffee   = new Ingredient { Code = "BVRG-01", Name = "Cold Brew Coffee",Category = "Beverage",Unit = "ml",      StockLevel = 10000, Threshold = 2000,CostPerUnit = 0.08m };
        var milk     = new Ingredient { Code = "BVRG-02", Name = "Milk",           Category = "Beverage", Unit = "ml",      StockLevel = 10000, Threshold = 2000,CostPerUnit = 0.06m };
        var matcha   = new Ingredient { Code = "BVRG-03", Name = "Matcha Powder",  Category = "Beverage", Unit = "g",       StockLevel = 1000,  Threshold = 200, CostPerUnit = 1.20m };
        var syrup    = new Ingredient { Code = "BVRG-04", Name = "Flavored Syrup", Category = "Beverage", Unit = "ml",      StockLevel = 3000,  Threshold = 500, CostPerUnit = 0.10m };
        var cup      = new Ingredient { Code = "SUPP-01", Name = "Cup",            Category = "Supplies", Unit = "pc",      StockLevel = 1000,  Threshold = 200, CostPerUnit = 3m };
        db.Ingredients.AddRange(rice, pork, chicken, egg, noodles, coffee, milk, matcha, syrup, cup);

        RecipeIngredient R(Ingredient i, decimal qty) => new() { Ingredient = i, Quantity = qty };
        MenuItem Item(string name, string cat, decimal price, params RecipeIngredient[] recipe) =>
            new() { Name = name, Category = cat, Price = price, Recipe = recipe.ToList() };

        db.MenuItems.AddRange(
            // ── Food ──────────────────────────────────────────────────────────
            Item("Pork Tonkatsu",      "Food",  149m, R(pork, 1), R(rice, 1)),
            Item("Chicken Tonkatsu",   "Food",  149m, R(chicken, 1), R(rice, 1)),
            Item("Chicken Poppers",    "Food",  109m, R(chicken, 1)),
            Item("Chicken Fingers",    "Food",  129m, R(chicken, 1)),
            Item("Crabstick Katsu",    "Food",   99m),
            Item("Spamsilog",          "Food",   99m, R(rice, 1), R(egg, 1)),
            Item("Hungariansilog",     "Food",   99m, R(rice, 1), R(egg, 1)),
            Item("Tocilog",            "Food",   99m, R(rice, 1), R(egg, 1)),
            Item("Tapsilog",           "Food",  109m, R(rice, 1), R(egg, 1)),
            Item("Sausilog",           "Food",  109m, R(rice, 1), R(egg, 1)),
            Item("Bacsilog",           "Food",   99m, R(rice, 1), R(egg, 1)),
            // Food extras (sold as standalone add-ons)
            Item("Rice",               "Food",   20m, R(rice, 1)),
            Item("Egg",                "Food",   18m, R(egg, 1)),
            Item("Tonkatsu Sauce",     "Food",   15m),

            // ── Cold Brew Coffee ──────────────────────────────────────────────
            Item("Americano",         "Cold Brew Coffee",  55m, R(coffee, 240), R(cup, 1)),
            Item("Latte",             "Cold Brew Coffee",  59m, R(coffee, 150), R(milk, 100), R(cup, 1)),
            Item("Spanish Latte",     "Cold Brew Coffee",  65m, R(coffee, 150), R(milk, 120), R(cup, 1)),
            Item("Vanilla Latte",     "Cold Brew Coffee",  65m, R(coffee, 150), R(milk, 100), R(syrup, 20), R(cup, 1)),
            Item("Caramel Macchiato", "Cold Brew Coffee",  69m, R(coffee, 150), R(milk, 100), R(syrup, 20), R(cup, 1)),
            Item("Cold Brew Latte",   "Cold Brew Coffee",  69m, R(coffee, 200), R(milk, 100), R(cup, 1)),
            Item("Mocha",             "Cold Brew Coffee",  69m, R(coffee, 150), R(milk, 100), R(syrup, 20), R(cup, 1)),
            Item("Chao's Coldbrew",   "Cold Brew Coffee",  79m, R(coffee, 240), R(cup, 1)),

            // ── Non-Coffee ────────────────────────────────────────────────────
            Item("Strawberry Milk",   "Non-Coffee",  65m, R(milk, 200), R(syrup, 20), R(cup, 1)),
            Item("Blueberry Milk",    "Non-Coffee",  65m, R(milk, 200), R(syrup, 20), R(cup, 1)),
            Item("Mango Cream",       "Non-Coffee",  69m, R(milk, 180), R(syrup, 20), R(cup, 1)),
            Item("Iced Choco",        "Non-Coffee",  65m, R(milk, 200), R(syrup, 25), R(cup, 1)),
            Item("Milky Oreo",        "Non-Coffee",  69m, R(milk, 200), R(cup, 1)),
            Item("Berry Choco Latte", "Non-Coffee",  75m, R(milk, 180), R(syrup, 20), R(cup, 1)),

            // ── Matcha Series ─────────────────────────────────────────────────
            Item("Matcha Latte",      "Matcha Series",  69m, R(matcha, 8),  R(milk, 200), R(cup, 1)),
            Item("Dirty Matcha",      "Matcha Series",  75m, R(matcha, 8),  R(milk, 180), R(coffee, 60), R(cup, 1)),
            Item("Strawberry Matcha", "Matcha Series",  75m, R(matcha, 8),  R(milk, 180), R(syrup, 20), R(cup, 1)),
            Item("Matcha Frappe",     "Matcha Series",  79m, R(matcha, 10), R(milk, 180), R(cup, 1)),

            // ── Frappe ────────────────────────────────────────────────────────
            // Frappe comes in 4 flavors but is a single POS item; flavour chosen at order time
            Item("Frappe (Strawberry)",       "Frappe",  69m, R(milk, 200), R(cup, 1)),
            Item("Frappe (Blueberry)",        "Frappe",  69m, R(milk, 200), R(cup, 1)),
            Item("Frappe (Cookies & Cream)",  "Frappe",  69m, R(milk, 200), R(cup, 1)),
            Item("Frappe (Chocolate)",        "Frappe",  69m, R(milk, 200), R(cup, 1)),
            Item("Frappuccino Mocha",         "Frappe",  75m, R(coffee, 100), R(milk, 180), R(syrup, 20), R(cup, 1)),
            Item("Milo Dinosaur",             "Frappe",  79m, R(milk, 200), R(cup, 1)),
            Item("Java Chip",                 "Frappe",  85m, R(coffee, 100), R(milk, 180), R(cup, 1)),

            // ── Fruit Soda ────────────────────────────────────────────────────
            // One entry per size; flavour is a modifier/preference
            Item("Fruit Soda (16oz)",  "Fruit Soda",  39m, R(cup, 1)),
            Item("Fruit Soda (22oz)",  "Fruit Soda",  49m, R(cup, 1)),

            // ── Qik's Fried Noodles ───────────────────────────────────────────
            Item("Plain Noodles",                        "Qik's Fried Noodles",  40m, R(noodles, 1)),
            Item("Noodles with Egg",                     "Qik's Fried Noodles",  55m, R(noodles, 1), R(egg, 1)),
            Item("Noodles with Pork Siomai",             "Qik's Fried Noodles",  55m, R(noodles, 1)),
            Item("Noodles with Japanese Siomai",         "Qik's Fried Noodles",  58m, R(noodles, 1)),
            Item("Noodles with Korean Sausage",          "Qik's Fried Noodles",  70m, R(noodles, 1)),
            Item("Overload Noodles",                     "Qik's Fried Noodles",  78m, R(noodles, 1)),
            Item("Overload Noodles with 2 Eggs",         "Qik's Fried Noodles", 108m, R(noodles, 1), R(egg, 2)),
            Item("Overload Noodles w/ 4 Pork Siomai",   "Qik's Fried Noodles", 108m, R(noodles, 1)),
            Item("Overload Noodles w/ 4 Jap Siomai",    "Qik's Fried Noodles", 110m, R(noodles, 1)),
            Item("Overload Noodles w/ 2 Korean Sausage","Qik's Fried Noodles", 138m, R(noodles, 1)));

        // ── Modifiers ─────────────────────────────────────────────────────────
        // Beverage categories that share drink add-ons / size / preference
        const string BEV = "Cold Brew Coffee,Non-Coffee,Matcha Series,Frappe,Fruit Soda";

        db.Modifiers.AddRange(
            // Size (beverages only — 16oz base price, +₱20 for 22oz)
            new Modifier { Name = "16oz",          GroupName = "Size",       PriceDelta =   0m, AppliesTo = BEV },
            new Modifier { Name = "22oz",          GroupName = "Size",       PriceDelta =  20m, AppliesTo = BEV },

            // Drink add-ons (beverages only)
            new Modifier { Name = "Coldbrew",      GroupName = "Add-ons",    PriceDelta =  25m, AppliesTo = BEV },
            new Modifier { Name = "Milk",          GroupName = "Add-ons",    PriceDelta =  25m, AppliesTo = BEV },
            new Modifier { Name = "Drizzle/Syrup", GroupName = "Add-ons",    PriceDelta =  15m, AppliesTo = BEV },
            new Modifier { Name = "Cold Foam",     GroupName = "Add-ons",    PriceDelta =  25m, AppliesTo = BEV },
            new Modifier { Name = "Whip Cream",    GroupName = "Add-ons",    PriceDelta =  25m, AppliesTo = BEV },

            // Drink preferences (beverages only)
            new Modifier { Name = "Less Sugar",    GroupName = "Preference", PriceDelta =   0m, AppliesTo = BEV },
            new Modifier { Name = "No Ice",        GroupName = "Preference", PriceDelta =   0m, AppliesTo = BEV },

            // Fruit soda flavour (fruit soda only)
            new Modifier { Name = "Strawberry",    GroupName = "Flavor",     PriceDelta =   0m, AppliesTo = "Fruit Soda" },
            new Modifier { Name = "Lemon",         GroupName = "Flavor",     PriceDelta =   0m, AppliesTo = "Fruit Soda" },
            new Modifier { Name = "Blueberry",     GroupName = "Flavor",     PriceDelta =   0m, AppliesTo = "Fruit Soda" },
            new Modifier { Name = "Mixed Berries", GroupName = "Flavor",     PriceDelta =   0m, AppliesTo = "Fruit Soda" },
            new Modifier { Name = "Green Apple",   GroupName = "Flavor",     PriceDelta =   0m, AppliesTo = "Fruit Soda" },
            new Modifier { Name = "Lychee",        GroupName = "Flavor",     PriceDelta =   0m, AppliesTo = "Fruit Soda" },
            new Modifier { Name = "Kiwi",          GroupName = "Flavor",     PriceDelta =   0m, AppliesTo = "Fruit Soda" },

            // Food extras (Food category only)
            new Modifier { Name = "Rice",          GroupName = "Extras",     PriceDelta =  20m, AppliesTo = "Food" },
            new Modifier { Name = "Egg",           GroupName = "Extras",     PriceDelta =  18m, AppliesTo = "Food" },
            new Modifier { Name = "Tonkatsu Sauce",GroupName = "Extras",     PriceDelta =  15m, AppliesTo = "Food" },

            // Noodle extras (Qik's Fried Noodles only)
            new Modifier { Name = "Toge",          GroupName = "Extras",     PriceDelta =  10m, AppliesTo = "Qik's Fried Noodles" },
            new Modifier { Name = "Egg",           GroupName = "Extras",     PriceDelta =  15m, AppliesTo = "Qik's Fried Noodles" },
            new Modifier { Name = "Pork Siomai",   GroupName = "Extras",     PriceDelta =  15m, AppliesTo = "Qik's Fried Noodles" },
            new Modifier { Name = "Japanese Siomai",GroupName = "Extras",    PriceDelta =  18m, AppliesTo = "Qik's Fried Noodles" });

        // ----- Settings -----
        db.Settings.AddRange(
            new AppSetting { Key = "StoreName",      Value = "Chao & Brew" },
            new AppSetting { Key = "Address",        Value = "PUP Quezon City Campus" },
            new AppSetting { Key = "Currency",       Value = "PHP" },
            new AppSetting { Key = "TaxRatePercent", Value = "0" });

        await db.SaveChangesAsync();

        // Seed 3 months of sales history (March – May 2026)
        await SeedSalesAsync(db);
    }

    /// <summary>
    /// Seeds realistic transaction history for March, April, and May 2026.
    /// Targets ₱1,000–₱5,000 total daily revenue by accumulating small single-order
    /// transactions (1–2 items, qty 1) until the daily budget is reached.
    /// </summary>
    public static async Task SeedSalesAsync(BrewvioDbContext db, bool force = false)
    {
        // Skip if sales data already exists (unless force override)
        if (!force && await db.Transactions.AnyAsync()) return;

        // Force mode: wipe existing sales data before re-seeding
        if (force)
        {
            await db.Payments.ExecuteDeleteAsync();
            await db.TransactionItems.ExecuteDeleteAsync();
            await db.Transactions.ExecuteDeleteAsync();
        }

        var cashier = await db.Users.FirstAsync(u => u.Role == Roles.Cashier && u.IsActive);
        var menu    = await db.MenuItems.ToListAsync();
        var rng     = new Random(42);

        // Beverages sell more than food
        string[] bevCategories = ["Cold Brew Coffee", "Non-Coffee", "Matcha Series", "Frappe", "Fruit Soda"];
        var bevItems  = menu.Where(m => bevCategories.Contains(m.Category)).ToList();
        var foodItems = menu.Where(m => !bevCategories.Contains(m.Category)).ToList();

        var periods = new[]
        {
            (new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)),
            (new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)),
            (new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)),
        };

        var transactions = new List<Transaction>();

        foreach (var (start, end) in periods)
        {
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                // Daily revenue target: ₱1,000 – ₱5,000
                decimal dailyTarget = rng.Next(1000, 5001);
                decimal dailyTotal  = 0m;

                while (dailyTotal < dailyTarget)
                {
                    int hour   = rng.Next(8, 20);
                    int minute = rng.Next(0, 60);
                    int second = rng.Next(0, 60);
                    var ts = new DateTime(date.Year, date.Month, date.Day, hour, minute, second, DateTimeKind.Utc);

                    // Most orders: 1 item, qty 1. Occasionally 2 items.
                    int itemCount = rng.NextDouble() < 0.75 ? 1 : 2;
                    var items = new List<TransactionItem>();

                    for (int i = 0; i < itemCount; i++)
                    {
                        MenuItem picked;
                        if (rng.NextDouble() < 0.70 && bevItems.Count > 0)
                            picked = bevItems[rng.Next(bevItems.Count)];
                        else if (foodItems.Count > 0)
                            picked = foodItems[rng.Next(foodItems.Count)];
                        else
                            picked = menu[rng.Next(menu.Count)];

                        items.Add(new TransactionItem
                        {
                            MenuItemId = picked.Id,
                            ItemName   = picked.Name,
                            UnitPrice  = picked.Price,
                            Quantity   = 1,
                            LineTotal  = picked.Price,
                        });
                    }

                    decimal subtotal = items.Sum(x => x.LineTotal);
                    decimal discountAmount = rng.NextDouble() < 0.10
                        ? Math.Round(subtotal * 0.10m, 2) : 0m;
                    decimal total = Math.Round(subtotal - discountAmount, 2);

                    string method = rng.NextDouble() < 0.60 ? "Cash" : "Card";
                    decimal cashTendered = method == "Cash"
                        ? Math.Ceiling(total / 50m) * 50m : total;

                    transactions.Add(new Transaction
                    {
                        Timestamp      = ts,
                        Subtotal       = subtotal,
                        DiscountAmount = discountAmount,
                        TaxAmount      = 0m,
                        TotalAmount    = total,
                        PaymentMethod  = method,
                        CashierId      = cashier.Id,
                        Status         = "Completed",
                        Items          = items,
                        Payments       = [new Payment { Method = method, Amount = cashTendered }],
                    });

                    dailyTotal += total;
                }
            }
        }

        db.Transactions.AddRange(transactions);
        await db.SaveChangesAsync();
    }
}
