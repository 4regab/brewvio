using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Services;

namespace Brewvio.Tests;

public class MenuServiceTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static MenuService Build(TestScope t) =>
        new(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

    [Fact]
    public async Task List_returns_only_active_items_by_default()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        await svc.SetActiveAsync(t.Db.MenuItems.First().Id, false);

        var items = await svc.ListAsync();

        Assert.All(items, i => Assert.True(i.IsActive));
    }

    [Fact]
    public async Task List_returns_inactive_items_when_flag_is_set()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        await svc.SetActiveAsync(t.Db.MenuItems.First().Id, false);

        var all = await svc.ListAsync(includeInactive: true);

        Assert.Contains(all, i => !i.IsActive);
    }

    [Fact]
    public async Task List_marks_item_unavailable_when_a_recipe_ingredient_is_out_of_stock()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        // Drain milk so Caffe Latte (uses 200ml milk) can't be made.
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");
        milk.StockLevel = 0m;
        await t.Db.SaveChangesAsync();

        var latte = (await svc.ListAsync()).First(m => m.Name == "Caffe Latte");

        Assert.False(latte.Available);
    }

    [Fact]
    public async Task List_marks_item_available_when_all_ingredients_have_enough_stock()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);

        var latte = (await svc.ListAsync()).First(m => m.Name == "Caffe Latte");

        Assert.True(latte.Available);
    }

    [Fact]
    public async Task Create_persists_item_with_recipe_and_cost()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var milk = t.Db.Ingredients.First(i => i.Name == "Whole Milk");       // 0.08/ml
        var cup = t.Db.Ingredients.First(i => i.Name == "Paper Cup (12oz)");  // 2.50/pc

        var dto = await Build(t).CreateAsync(new MenuItemRequest("Test Brew", "TestCat", 99m, true,
            new List<RecipeLineInput> { new(milk.Id, 200m), new(cup.Id, 1m) }));

        Assert.NotNull(dto);
        Assert.Equal("Test Brew", dto!.Name);
        Assert.Equal(99m, dto.Price);
        Assert.Equal(18.50m, dto.Cost);   // 200*0.08 + 1*2.50
        Assert.Equal(2, dto.Recipe.Count);

        Assert.NotNull(t.NewContext().MenuItems.Find(dto.Id));
    }

    [Fact]
    public async Task Create_allows_item_with_no_recipe()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var dto = await Build(t).CreateAsync(
            new MenuItemRequest("Simple Item", "Other", 50m, true, new List<RecipeLineInput>()));

        Assert.NotNull(dto);
        Assert.Empty(dto!.Recipe);
        Assert.Equal(0m, dto.Cost);
    }

    [Fact]
    public async Task Update_replaces_recipe_entirely()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");
        var sugar = t.Db.Ingredients.First(i => i.Name == "Sugar");

        var updated = await svc.UpdateAsync(latte.Id, new MenuItemRequest(
            "Caffe Latte", "Espresso", 145m, true, new List<RecipeLineInput> { new(sugar.Id, 5m) }));

        Assert.NotNull(updated);
        Assert.Equal(145m, updated!.Price);
        Assert.Single(updated.Recipe);
        Assert.Equal(sugar.Id, updated.Recipe[0].IngredientId);
    }

    [Fact]
    public async Task Update_returns_null_for_missing_item()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        Assert.Null(await Build(t).UpdateAsync(999_999,
            new MenuItemRequest("X", "Y", 1m, true, new List<RecipeLineInput>())));
    }

    [Fact]
    public async Task SetActive_toggles_item_visibility()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");

        Assert.True(await Build(t).SetActiveAsync(latte.Id, false));
        Assert.False(t.NewContext().MenuItems.Find(latte.Id)!.IsActive);
    }

    [Fact]
    public async Task SetActive_returns_false_for_missing_item()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        Assert.False(await Build(t).SetActiveAsync(999_999, true));
    }

    [Fact]
    public async Task CreateModifier_persists_with_price_delta()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var mod = await Build(t).CreateModifierAsync(new ModifierRequest("Soy Milk", "Milk", 25m, true));

        Assert.Equal("Soy Milk", mod.Name);
        Assert.Equal(25m, mod.PriceDelta);
    }

    [Fact]
    public async Task UpdateModifier_changes_fields_and_returns_dto()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var original = t.Db.Modifiers.First(m => m.Name == "Extra Shot");

        var updated = await Build(t).UpdateModifierAsync(original.Id,
            new ModifierRequest("Double Shot", "Extras", 30m, true));

        Assert.NotNull(updated);
        Assert.Equal("Double Shot", updated!.Name);
        Assert.Equal(30m, updated.PriceDelta);
    }

    [Fact]
    public async Task UpdateModifier_returns_null_for_missing_modifier()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        Assert.Null(await Build(t).UpdateModifierAsync(999_999,
            new ModifierRequest("X", "Y", 0m, true)));
    }

    [Fact]
    public async Task ListModifiers_excludes_inactive_by_default()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var mod = t.Db.Modifiers.First();
        await svc.UpdateModifierAsync(mod.Id,
            new ModifierRequest(mod.Name, mod.GroupName, mod.PriceDelta, false));

        var active = await svc.ListModifiersAsync();

        Assert.DoesNotContain(active, m => m.Id == mod.Id);
    }
}
