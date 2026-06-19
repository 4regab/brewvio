using Brewvio.Helpers;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Data;

// Seeds the database with demo users, ingredients, menu items, modifiers, settings, and sales history.
public static class DatabaseInitializer
{
    // Seeds the full demo data set on first run, unless users already exist; logs and skips on failure.
    // app: the web application providing the service scope and logger
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

    // Seeds the legacy test data set (alias for SeedTestDataAsync).
    // db: the database context to seed
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

        // Builds a recipe-ingredient line for a menu item.
        // i: the ingredient consumed
        // qty: the quantity consumed per unit sold
        // returns: a new RecipeIngredient
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

    // Seeds the production demo data: users, ingredients, menu items, modifiers, settings, and 3 months of sales.
    // db: the database context to seed
    public static async Task SeedAllAsync(BrewvioDbContext db)
    {
        // ----- Users -----
        db.Users.AddRange(
            new User { Username = "manager", FullName = "Store Manager", Role = Roles.Manager, Status = UserStatus.Active, IsActive = true, PasswordHash = PasswordHasher.Hash("Manager@123") },
            new User { Username = "cashier", FullName = "Front Cashier", Role = Roles.Cashier, Status = UserStatus.Active, IsActive = true, PasswordHash = PasswordHasher.Hash("Cashier@123") });

        // ----- Ingredients -----
        // Food
        var rice          = new Ingredient { Code = "FOOD-01", Name = "Rice",              Category = "Food",     Unit = "cup",     StockLevel = 500,   Threshold = 50,  CostPerUnit = 8m };
        var porkChop      = new Ingredient { Code = "FOOD-02", Name = "Breaded Pork Chop", Category = "Food",     Unit = "pc",      StockLevel = 100,   Threshold = 20,  CostPerUnit = 80m };
        var chickenFillet = new Ingredient { Code = "FOOD-03", Name = "Chicken Fillet",    Category = "Food",     Unit = "pc",      StockLevel = 100,   Threshold = 20,  CostPerUnit = 60m };
        var chickenPoppers= new Ingredient { Code = "FOOD-04", Name = "Chicken Poppers",   Category = "Food",     Unit = "g",       StockLevel = 3000,  Threshold = 500, CostPerUnit = 0.60m };
        var chickenFing   = new Ingredient { Code = "FOOD-05", Name = "Chicken Fingers",   Category = "Food",     Unit = "pc",      StockLevel = 300,   Threshold = 50,  CostPerUnit = 20m };
        var crabstick     = new Ingredient { Code = "FOOD-06", Name = "Crabstick",         Category = "Food",     Unit = "pc",      StockLevel = 300,   Threshold = 50,  CostPerUnit = 10m };
        var spam          = new Ingredient { Code = "FOOD-07", Name = "Spam",              Category = "Food",     Unit = "slice",   StockLevel = 200,   Threshold = 30,  CostPerUnit = 20m };
        var hungarian     = new Ingredient { Code = "FOOD-08", Name = "Hungarian Sausage", Category = "Food",     Unit = "pc",      StockLevel = 100,   Threshold = 20,  CostPerUnit = 25m };
        var tocino        = new Ingredient { Code = "FOOD-09", Name = "Tocino",            Category = "Food",     Unit = "g",       StockLevel = 3000,  Threshold = 300, CostPerUnit = 0.40m };
        var tapa          = new Ingredient { Code = "FOOD-10", Name = "Tapa",              Category = "Food",     Unit = "g",       StockLevel = 3000,  Threshold = 300, CostPerUnit = 0.45m };
        var korSausage    = new Ingredient { Code = "FOOD-11", Name = "Korean Sausage",    Category = "Food",     Unit = "pc",      StockLevel = 200,   Threshold = 30,  CostPerUnit = 15m };
        var bacon         = new Ingredient { Code = "FOOD-12", Name = "Bacon",             Category = "Food",     Unit = "pc",      StockLevel = 200,   Threshold = 30,  CostPerUnit = 18m };
        var egg           = new Ingredient { Code = "FOOD-13", Name = "Egg",               Category = "Food",     Unit = "pc",      StockLevel = 300,   Threshold = 50,  CostPerUnit = 8m };
        var onionSpring   = new Ingredient { Code = "FOOD-14", Name = "Spring Onion",      Category = "Food",     Unit = "pinch",   StockLevel = 100,   Threshold = 10,  CostPerUnit = 2m };
        var noodles       = new Ingredient { Code = "FOOD-15", Name = "Noodles",           Category = "Food",     Unit = "g",       StockLevel = 5000,  Threshold = 500, CostPerUnit = 0.20m };
        var sprouts       = new Ingredient { Code = "FOOD-16", Name = "Mung Bean Sprouts", Category = "Food",     Unit = "g",       StockLevel = 3000,  Threshold = 300, CostPerUnit = 0.05m };
        var porkSiomai    = new Ingredient { Code = "FOOD-17", Name = "Pork Siomai",       Category = "Food",     Unit = "pc",      StockLevel = 300,   Threshold = 50,  CostPerUnit = 8m };
        var japSiomai     = new Ingredient { Code = "FOOD-18", Name = "Japanese Siomai",   Category = "Food",     Unit = "pc",      StockLevel = 300,   Threshold = 50,  CostPerUnit = 10m };
        // Beverages
        var coldBrew      = new Ingredient { Code = "BVRG-01", Name = "Cold Brew Coffee",  Category = "Beverage", Unit = "ml",      StockLevel = 10000, Threshold = 2000,CostPerUnit = 0.08m };
        var milk          = new Ingredient { Code = "BVRG-02", Name = "Milk",              Category = "Beverage", Unit = "ml",      StockLevel = 10000, Threshold = 2000,CostPerUnit = 0.06m };
        var coldFoam      = new Ingredient { Code = "BVRG-03", Name = "Cold Foam",         Category = "Beverage", Unit = "ml",      StockLevel = 3000,  Threshold = 500, CostPerUnit = 0.15m };
        var condensedMilk = new Ingredient { Code = "BVRG-04", Name = "Condensed Milk",    Category = "Beverage", Unit = "g",       StockLevel = 2000,  Threshold = 300, CostPerUnit = 0.30m };
        var vanillaSyrup  = new Ingredient { Code = "BVRG-05", Name = "Vanilla Syrup",     Category = "Beverage", Unit = "ml",      StockLevel = 2000,  Threshold = 300, CostPerUnit = 0.25m };
        var caramelDrizzle= new Ingredient { Code = "BVRG-06", Name = "Caramel Drizzle",   Category = "Beverage", Unit = "g",       StockLevel = 1000,  Threshold = 150, CostPerUnit = 0.35m };
        var chocSauce     = new Ingredient { Code = "BVRG-07", Name = "Chocolate Sauce",   Category = "Beverage", Unit = "ml",      StockLevel = 2000,  Threshold = 300, CostPerUnit = 0.30m };
        var sigSyrup      = new Ingredient { Code = "BVRG-08", Name = "Signature Syrup",   Category = "Beverage", Unit = "ml",      StockLevel = 2000,  Threshold = 300, CostPerUnit = 0.20m };
        var strawJam      = new Ingredient { Code = "BVRG-09", Name = "Strawberry Jam",    Category = "Beverage", Unit = "scoop",   StockLevel = 500,   Threshold = 50,  CostPerUnit = 3m };
        var blueJam       = new Ingredient { Code = "BVRG-10", Name = "Blueberry Jam",     Category = "Beverage", Unit = "scoop",   StockLevel = 500,   Threshold = 50,  CostPerUnit = 3m };
        var mangoJam      = new Ingredient { Code = "BVRG-11", Name = "Mango Jam",         Category = "Beverage", Unit = "scoop",   StockLevel = 500,   Threshold = 50,  CostPerUnit = 3m };
        var chocPowder    = new Ingredient { Code = "BVRG-12", Name = "Chocolate Powder",  Category = "Beverage", Unit = "scoop",   StockLevel = 500,   Threshold = 50,  CostPerUnit = 5m };
        var oreo          = new Ingredient { Code = "BVRG-13", Name = "Crushed Oreo",      Category = "Beverage", Unit = "scoop",   StockLevel = 200,   Threshold = 30,  CostPerUnit = 8m };
        var berrySyrup    = new Ingredient { Code = "BVRG-14", Name = "Berry Syrup",       Category = "Beverage", Unit = "ml",      StockLevel = 1000,  Threshold = 150, CostPerUnit = 0.25m };
        var matchaPowder  = new Ingredient { Code = "BVRG-15", Name = "Matcha Powder",     Category = "Beverage", Unit = "scoop",   StockLevel = 500,   Threshold = 50,  CostPerUnit = 6m };
        var frappeBase    = new Ingredient { Code = "BVRG-16", Name = "Frappe Base",       Category = "Beverage", Unit = "g",       StockLevel = 3000,  Threshold = 500, CostPerUnit = 0.30m };
        var miloPowder    = new Ingredient { Code = "BVRG-17", Name = "Milo Powder",       Category = "Beverage", Unit = "scoop",   StockLevel = 500,   Threshold = 50,  CostPerUnit = 4m };
        var chocChips     = new Ingredient { Code = "BVRG-18", Name = "Chocolate Chips",   Category = "Beverage", Unit = "g",       StockLevel = 500,   Threshold = 50,  CostPerUnit = 0.50m };
        var yogurtPowder  = new Ingredient { Code = "BVRG-19", Name = "Yogurt Powder",     Category = "Beverage", Unit = "scoop",   StockLevel = 200,   Threshold = 30,  CostPerUnit = 5m };
        var sodaBase      = new Ingredient { Code = "BVRG-20", Name = "Soda Water",        Category = "Beverage", Unit = "ml",      StockLevel = 10000, Threshold = 1000,CostPerUnit = 0.03m };
        var fruitSyrup    = new Ingredient { Code = "BVRG-21", Name = "Fruit Soda Syrup",  Category = "Beverage", Unit = "pump",    StockLevel = 1000,  Threshold = 100, CostPerUnit = 2m };
        var cup           = new Ingredient { Code = "SUPP-01", Name = "Cup",               Category = "Supplies", Unit = "pc",      StockLevel = 1000,  Threshold = 200, CostPerUnit = 3m };

        db.Ingredients.AddRange(
            rice, porkChop, chickenFillet, chickenPoppers, chickenFing, crabstick,
            spam, hungarian, tocino, tapa, korSausage, bacon, egg, onionSpring,
            noodles, sprouts, porkSiomai, japSiomai,
            coldBrew, milk, coldFoam, condensedMilk, vanillaSyrup, caramelDrizzle,
            chocSauce, sigSyrup, strawJam, blueJam, mangoJam, chocPowder, oreo,
            berrySyrup, matchaPowder, frappeBase, miloPowder, chocChips, yogurtPowder,
            sodaBase, fruitSyrup, cup);

        // Builds a recipe-ingredient line for a menu item.
        // i: the ingredient consumed
        // qty: the quantity consumed per unit sold
        // returns: a new RecipeIngredient
        RecipeIngredient R(Ingredient i, decimal qty) => new() { Ingredient = i, Quantity = qty };
        // Builds a menu item with its recipe.
        // name: the item name
        // cat: the menu category
        // price: the sale price
        // recipe: the recipe-ingredient lines for the item
        // returns: a new MenuItem
        MenuItem Item(string name, string cat, decimal price, params RecipeIngredient[] recipe) =>
            new() { Name = name, Category = cat, Price = price, Recipe = recipe.ToList() };

        db.MenuItems.AddRange(
            // ── Food ──────────────────────────────────────────────────────────
            // All food items include rice (1 cup) + spring onion garnish; silog items include egg.
            Item("Pork Tonkatsu",      "Food",  149m, R(porkChop, 1),      R(rice, 1), R(onionSpring, 1)),
            Item("Chicken Tonkatsu",   "Food",  149m, R(chickenFillet, 1), R(rice, 1), R(onionSpring, 1)),
            Item("Chicken Poppers",    "Food",  109m, R(chickenPoppers, 100), R(rice, 1), R(onionSpring, 1)),
            Item("Chicken Fingers",    "Food",  129m, R(chickenFing, 3),   R(rice, 1), R(onionSpring, 1)),
            Item("Crabstick Katsu",    "Food",   99m, R(crabstick, 4),     R(rice, 1), R(onionSpring, 1)),
            Item("Spamsilog",          "Food",   99m, R(spam, 2),          R(egg, 1), R(rice, 1)),
            Item("Hungariansilog",     "Food",   99m, R(hungarian, 1),     R(egg, 1), R(rice, 1)),
            Item("Tocilog",            "Food",   99m, R(tocino, 100),      R(egg, 1), R(rice, 1)),
            Item("Tapsilog",           "Food",  109m, R(tapa, 100),        R(egg, 1), R(rice, 1)),
            Item("Sausilog",           "Food",  109m, R(korSausage, 2),    R(egg, 1), R(rice, 1)),
            Item("Bacsilog",           "Food",   99m, R(bacon, 2),         R(egg, 1), R(rice, 1)),
            // Food extras
            Item("Rice",               "Food",   20m, R(rice, 1)),
            Item("Egg",                "Food",   18m, R(egg, 1)),
            Item("Tonkatsu Sauce",     "Food",   15m),

            // ── Cold Brew Coffee (16oz base quantities) ───────────────────────
            Item("Americano",         "Cold Brew Coffee",  55m, R(coldBrew, 80),  R(cup, 1)),
            Item("Latte",             "Cold Brew Coffee",  59m, R(coldBrew, 80),  R(milk, 100), R(cup, 1)),
            Item("Spanish Latte",     "Cold Brew Coffee",  65m, R(coldBrew, 80),  R(milk, 100), R(condensedMilk, 30), R(cup, 1)),
            Item("Vanilla Latte",     "Cold Brew Coffee",  65m, R(coldBrew, 80),  R(milk, 100), R(vanillaSyrup, 25),  R(cup, 1)),
            Item("Caramel Macchiato", "Cold Brew Coffee",  69m, R(coldBrew, 80),  R(milk, 100), R(vanillaSyrup, 10),  R(caramelDrizzle, 15), R(cup, 1)),
            Item("Cold Brew Latte",   "Cold Brew Coffee",  69m, R(coldBrew, 80),  R(coldFoam, 70), R(cup, 1)),
            Item("Mocha",             "Cold Brew Coffee",  69m, R(coldBrew, 80),  R(milk, 100), R(chocSauce, 20),     R(cup, 1)),
            Item("Chao's Coldbrew",   "Cold Brew Coffee",  79m, R(coldBrew, 80),  R(sigSyrup, 20), R(cup, 1)),

            // ── Non-Coffee (16oz base quantities) ────────────────────────────
            Item("Strawberry Milk",   "Non-Coffee",  65m, R(milk, 140), R(strawJam, 2),   R(cup, 1)),
            Item("Blueberry Milk",    "Non-Coffee",  65m, R(milk, 140), R(blueJam, 2),    R(cup, 1)),
            Item("Mango Cream",       "Non-Coffee",  69m, R(milk, 140), R(mangoJam, 2),   R(cup, 1)),
            Item("Iced Choco",        "Non-Coffee",  65m, R(milk, 140), R(chocPowder, 2), R(chocSauce, 30), R(cup, 1)),
            Item("Milky Oreo",        "Non-Coffee",  69m, R(milk, 140), R(oreo, 2),       R(cup, 1)),
            Item("Berry Choco Latte", "Non-Coffee",  75m, R(milk, 140), R(chocPowder, 2), R(berrySyrup, 20), R(cup, 1)),

            // ── Matcha Series (16oz base quantities) ─────────────────────────
            Item("Matcha Latte",      "Matcha Series",  69m, R(matchaPowder, 2), R(milk, 150), R(cup, 1)),
            Item("Dirty Matcha",      "Matcha Series",  75m, R(matchaPowder, 2), R(milk, 100), R(coldBrew, 80),  R(cup, 1)),
            Item("Strawberry Matcha", "Matcha Series",  75m, R(matchaPowder, 2), R(milk, 120), R(strawJam, 2),  R(cup, 1)),
            Item("Matcha Frappe",     "Matcha Series",  79m, R(matchaPowder, 2), R(frappeBase, 35), R(milk, 40), R(cup, 1)),

            // ── Frappe (16oz base quantities) ────────────────────────────────
            Item("Frappe (Strawberry)",       "Frappe",  69m, R(frappeBase, 35), R(milk, 40), R(strawJam, 2),   R(cup, 1)),
            Item("Frappe (Blueberry)",        "Frappe",  69m, R(frappeBase, 35), R(milk, 40), R(blueJam, 2),    R(yogurtPowder, 2), R(cup, 1)),
            Item("Frappe (Cookies & Cream)",  "Frappe",  69m, R(frappeBase, 35), R(milk, 40), R(oreo, 2),       R(cup, 1)),
            Item("Frappe (Chocolate)",        "Frappe",  69m, R(frappeBase, 35), R(milk, 40), R(chocPowder, 2), R(cup, 1)),
            Item("Frappuccino Mocha",         "Frappe",  75m, R(frappeBase, 35), R(milk, 40), R(coldBrew, 80),  R(cup, 1)),
            Item("Milo Dinosaur",             "Frappe",  79m, R(frappeBase, 35), R(milk, 40), R(miloPowder, 2), R(cup, 1)),
            Item("Java Chip",                 "Frappe",  85m, R(frappeBase, 35), R(milk, 40), R(coldBrew, 80),  R(chocChips, 20), R(chocSauce, 15), R(cup, 1)),

            // ── Fruit Soda (16oz base quantities) ────────────────────────────
            Item("Fruit Soda (16oz)",  "Fruit Soda",  39m, R(sodaBase, 120), R(fruitSyrup, 2), R(cup, 1)),
            Item("Fruit Soda (22oz)",  "Fruit Soda",  49m, R(sodaBase, 160), R(fruitSyrup, 3), R(cup, 1)),

            // ── Qik's Fried Noodles ───────────────────────────────────────────
            // Base recipe: 60g noodles + 30g sprouts
            Item("Plain Noodles",                        "Qik's Fried Noodles",  40m, R(noodles, 60),  R(sprouts, 30)),
            Item("Noodles with Egg",                     "Qik's Fried Noodles",  55m, R(noodles, 60),  R(sprouts, 30), R(egg, 1)),
            Item("Noodles with Pork Siomai",             "Qik's Fried Noodles",  55m, R(noodles, 60),  R(sprouts, 30), R(porkSiomai, 2)),
            Item("Noodles with Japanese Siomai",         "Qik's Fried Noodles",  58m, R(noodles, 60),  R(sprouts, 30), R(japSiomai, 2)),
            Item("Noodles with Korean Sausage",          "Qik's Fried Noodles",  70m, R(noodles, 60),  R(sprouts, 30), R(korSausage, 1)),
            // Overload base: 120g noodles + 60g sprouts
            Item("Overload Noodles",                     "Qik's Fried Noodles",  78m, R(noodles, 120), R(sprouts, 60)),
            Item("Overload Noodles with 2 Eggs",         "Qik's Fried Noodles", 108m, R(noodles, 120), R(sprouts, 60), R(egg, 2)),
            Item("Overload Noodles w/ 4 Pork Siomai",   "Qik's Fried Noodles", 108m, R(noodles, 120), R(sprouts, 60), R(porkSiomai, 4)),
            Item("Overload Noodles w/ 4 Jap Siomai",    "Qik's Fried Noodles", 110m, R(noodles, 120), R(sprouts, 60), R(japSiomai, 4)),
            Item("Overload Noodles w/ 2 Korean Sausage","Qik's Fried Noodles", 138m, R(noodles, 120), R(sprouts, 60), R(korSausage, 2)));

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
            // Drop the previously-seeded stock-movement rows so the backfill below cannot duplicate
            // them. Only sale/refund rows are seed-generated; manual StockIn/StockOut/InventoryAdjust
            // rows are real operator history and are left untouched.
            await db.AuditLogs
                .Where(a => a.Action == StockActions.Sale || a.Action == StockActions.Refund)
                .ExecuteDeleteAsync();
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

                    string method = rng.NextDouble() < 0.60 ? "Cash" : "GCash";
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
        await db.SaveChangesAsync();   // assigns the transaction ids referenced in the ledger below

        // Backfill the per-ingredient stock-movement ledger for this seeded sales history so the
        // Stock Movements page shows past transactions, not just live POS orders.
        await BackfillSaleMovementsAsync(db, transactions, cashier);
    }

    /// <summary>
    /// Records one <see cref="StockActions.Sale"/> ledger row per consumed ingredient for each of
    /// the supplied (already-persisted) seeded transactions, mirroring
    /// <c>OrderService.LogSaleMovementsAsync</c> byte-for-byte so live and seeded rows are
    /// indistinguishable. To keep the running balance non-negative while leaving today's stock on
    /// the clean declared levels, each ingredient's starting stock is first raised by its total
    /// historical consumption; the sales are then replayed in chronological order, deducting that
    /// same total back out. The net effect on the current <c>StockLevel</c> is therefore zero.
    /// </summary>
    private static async Task BackfillSaleMovementsAsync(
        BrewvioDbContext db, List<Transaction> transactions, User cashier)
    {
        // Recipe map: menu item id -> [(ingredient id, qty consumed per unit sold)].
        var recipeByItem = (await db.RecipeIngredients.AsNoTracking().ToListAsync())
            .GroupBy(r => r.MenuItemId)
            .ToDictionary(g => g.Key, g => g.Select(r => (r.IngredientId, r.Quantity)).ToList());

        // Per-transaction ingredient usage (an order line can repeat an ingredient via its recipe).
        static Dictionary<int, decimal> UsageOf(Transaction tx,
            Dictionary<int, List<(int IngredientId, decimal Quantity)>> recipes)
        {
            var usage = new Dictionary<int, decimal>();
            foreach (var item in tx.Items)
                if (recipes.TryGetValue(item.MenuItemId, out var lines))
                    foreach (var (ingId, qtyPerUnit) in lines)
                        usage[ingId] = usage.GetValueOrDefault(ingId) + qtyPerUnit * item.Quantity;
            return usage;
        }

        // Total consumption per ingredient across the whole seeded history.
        var totalUsage = new Dictionary<int, decimal>();
        foreach (var tx in transactions)
            foreach (var (ingId, qty) in UsageOf(tx, recipeByItem))
                totalUsage[ingId] = totalUsage.GetValueOrDefault(ingId) + qty;

        if (totalUsage.Count == 0) return;

        // Pre-load each touched ingredient's stock by the total it will consume, so the replayed
        // balance never dips below its declared level and lands back exactly where it started.
        var ingById = (await db.Ingredients.Where(i => totalUsage.Keys.Contains(i.Id)).ToListAsync())
            .ToDictionary(i => i.Id);
        foreach (var (ingId, total) in totalUsage)
            if (ingById.TryGetValue(ingId, out var ing)) ing.StockLevel += total;

        // Replay chronologically so each ingredient's BalanceAfter decreases monotonically over time.
        foreach (var tx in transactions.OrderBy(t => t.Timestamp).ThenBy(t => t.Id))
        {
            foreach (var (ingId, qty) in UsageOf(tx, recipeByItem))
            {
                if (qty <= 0 || !ingById.TryGetValue(ingId, out var ing)) continue;
                var before = ing.StockLevel;
                ing.StockLevel -= qty;
                var after = ing.StockLevel;
                db.AuditLogs.Add(new AuditLog
                {
                    Timestamp    = tx.Timestamp,
                    UserId       = cashier.Id,
                    Username     = cashier.Username,
                    Action       = StockActions.Sale,
                    Details      = $"{ing.Name}: -{qty:0.###} {ing.Unit} (sale Txn #{tx.Id}). {before:0.###} -> {after:0.###} {ing.Unit}",
                    IngredientId = ing.Id,
                    Quantity     = -qty,
                    BalanceAfter = after,
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
