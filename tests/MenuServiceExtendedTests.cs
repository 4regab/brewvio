using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Services;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Tests;

/// <summary>
/// Additional tests for MenuService covering untested methods:
/// GetAsync, DeleteMenuItemAsync, DeleteModifierAsync, ListModifiersAsync(includeInactive:true).
/// </summary>
public class MenuServiceExtendedTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static MenuService Build(TestScope t) =>
        new(t.Db, new AuditService(t.Db, TestSupport.Cur(1, "manager", "Manager")));

    // ── GetAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_returns_correct_item_with_recipe()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");

        var dto = await Build(t).GetAsync(latte.Id);

        Assert.NotNull(dto);
        Assert.Equal("Caffe Latte", dto!.Name);
        Assert.NotEmpty(dto.Recipe);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_missing_id()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        Assert.Null(await Build(t).GetAsync(999_999));
    }

    [Fact]
    public async Task GetAsync_returns_inactive_items_too()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");
        await svc.SetActiveAsync(latte.Id, false);

        var dto = await svc.GetAsync(latte.Id);

        Assert.NotNull(dto);
        Assert.False(dto!.IsActive);
    }

    // ── DeleteMenuItemAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task DeleteMenuItem_removes_item_and_recipe()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var latte = t.Db.MenuItems.Include(m => m.Recipe).First(m => m.Name == "Caffe Latte");
        var latteId = latte.Id;
        var recipeIngredientIds = latte.Recipe.Select(r => r.Id).ToList();

        var result = await svc.DeleteMenuItemAsync(latteId);

        Assert.True(result);
        var verify = t.NewContext();
        Assert.Null(await verify.MenuItems.FindAsync(latteId));
        // Recipe rows must be cascade-deleted
        foreach (var rid in recipeIngredientIds)
            Assert.Null(await verify.RecipeIngredients.FindAsync(rid));
    }

    [Fact]
    public async Task DeleteMenuItem_returns_false_for_missing_item()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        Assert.False(await Build(t).DeleteMenuItemAsync(999_999));
    }

    [Fact]
    public async Task DeleteMenuItem_writes_audit_entry()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var espresso = t.Db.MenuItems.First(m => m.Name == "Espresso");

        await svc.DeleteMenuItemAsync(espresso.Id);

        var verify = t.NewContext();
        Assert.Contains(await verify.AuditLogs.ToListAsync(),
            a => a.Action == "MenuItemDeleted" && a.Details.Contains("Espresso"));
    }

    // ── DeleteModifierAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task DeleteModifier_removes_modifier()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var mod = t.Db.Modifiers.First(m => m.Name == "Extra Shot");
        var modId = mod.Id;

        var result = await svc.DeleteModifierAsync(modId);

        Assert.True(result);
        Assert.Null(await t.NewContext().Modifiers.FindAsync(modId));
    }

    [Fact]
    public async Task DeleteModifier_returns_false_for_missing_modifier()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        Assert.False(await Build(t).DeleteModifierAsync(999_999));
    }

    [Fact]
    public async Task DeleteModifier_writes_audit_entry()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var mod = t.Db.Modifiers.First(m => m.Name == "Oat Milk");

        await svc.DeleteModifierAsync(mod.Id);

        var verify = t.NewContext();
        Assert.Contains(await verify.AuditLogs.ToListAsync(),
            a => a.Action == "ModifierDeleted" && a.Details.Contains("Oat Milk"));
    }

    // ── ListModifiersAsync(includeInactive: true) ────────────────────────────

    [Fact]
    public async Task ListModifiers_with_include_inactive_returns_all()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var mod = t.Db.Modifiers.First();

        await svc.UpdateModifierAsync(mod.Id,
            new ModifierRequest(mod.Name, mod.GroupName, mod.PriceDelta, false));

        var all = await svc.ListModifiersAsync(includeInactive: true);

        Assert.Contains(all, m => m.Id == mod.Id && !m.IsActive);
    }

    [Fact]
    public async Task ListModifiers_default_excludes_inactive()
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

    // ── CreateModifier with negative PriceDelta (discount modifier) ──────────

    [Fact]
    public async Task CreateModifier_with_negative_price_delta_persists_correctly()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);

        var mod = await Build(t).CreateModifierAsync(
            new ModifierRequest("Senior Discount", "Discount", -20m, true));

        Assert.Equal(-20m, mod.PriceDelta);
    }

    // ── Update item cost recalculation ───────────────────────────────────────

    [Fact]
    public async Task Update_new_recipe_recalculates_cost()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");
        var sugar = t.Db.Ingredients.First(i => i.Name == "Sugar"); // 0.02/g

        // Replace entire recipe with just 10g of sugar → cost = 10 * 0.02 = 0.20
        var updated = await svc.UpdateAsync(latte.Id,
            new MenuItemRequest("Caffe Latte", "Espresso", 140m, true,
                new List<RecipeLineInput> { new(sugar.Id, 10m) }));

        Assert.NotNull(updated);
        Assert.Equal(0.20m, updated!.Cost);
    }

    // ── SetActive: reactivating a deactivated item ───────────────────────────

    [Fact]
    public async Task SetActive_can_reactivate_deactivated_item()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var svc = Build(t);
        var latte = t.Db.MenuItems.First(m => m.Name == "Caffe Latte");

        await svc.SetActiveAsync(latte.Id, false);
        await svc.SetActiveAsync(latte.Id, true);

        Assert.True(t.NewContext().MenuItems.Find(latte.Id)!.IsActive);
    }
}
