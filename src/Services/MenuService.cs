using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Services;

// Menu & recipe management plus modifiers. Recipe cost is derived from ingredient unit costs.
public class MenuService(BrewvioDbContext db, AuditService audit)
{
    // Lists menu items with their recipes (active only unless includeInactive), ordered by category then name.
    // includeInactive: when true, includes inactive menu items.
    // ct: cancellation token.
    // returns: the menu items as DTOs.
    public async Task<List<MenuItemDto>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var q = db.MenuItems.AsNoTracking().Include(m => m.Recipe).ThenInclude(r => r.Ingredient).AsQueryable();
        if (!includeInactive) q = q.Where(m => m.IsActive);
        var items = await q.OrderBy(m => m.Category).ThenBy(m => m.Name).ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    // Gets a single menu item (with recipe) by id.
    // id: the menu item id.
    // ct: cancellation token.
    // returns: the menu item DTO, or null if not found.
    public async Task<MenuItemDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var m = await db.MenuItems.Include(x => x.Recipe).ThenInclude(r => r.Ingredient)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return m is null ? null : ToDto(m);
    }

    // Creates a menu item with its recipe lines and audits the creation.
    // r: the menu item details and recipe to create.
    // ct: cancellation token.
    // returns: the created menu item DTO, or null if it can't be reloaded.
    public async Task<MenuItemDto?> CreateAsync(MenuItemRequest r, CancellationToken ct = default)
    {
        var item = new MenuItem
        {
            Name = r.Name, Category = r.Category, Price = r.Price, IsActive = r.IsActive,
            Recipe = r.Recipe.Select(l => new RecipeIngredient { IngredientId = l.IngredientId, Quantity = l.Quantity }).ToList()
        };
        db.MenuItems.Add(item);
        audit.Add("MenuItemCreated", $"{r.Name} @ {r.Price} ({r.Recipe.Count} recipe line(s))");
        await db.SaveChangesAsync(ct);
        return await GetAsync(item.Id, ct);
    }

    // Updates a menu item's fields, replaces its recipe lines, and audits the change.
    // id: the menu item id to update.
    // r: the new menu item details and recipe.
    // ct: cancellation token.
    // returns: the updated menu item DTO, or null if not found.
    public async Task<MenuItemDto?> UpdateAsync(int id, MenuItemRequest r, CancellationToken ct = default)
    {
        var item = await db.MenuItems.Include(m => m.Recipe).FirstOrDefaultAsync(m => m.Id == id, ct);
        if (item is null) return null;
        item.Name = r.Name; item.Category = r.Category; item.Price = r.Price; item.IsActive = r.IsActive;
        db.RecipeIngredients.RemoveRange(item.Recipe);
        item.Recipe = r.Recipe.Select(l => new RecipeIngredient { IngredientId = l.IngredientId, Quantity = l.Quantity }).ToList();
        audit.Add("MenuItemUpdated", $"{r.Name} @ {r.Price}");
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // Activates or deactivates a menu item and audits the status change.
    // id: the menu item id.
    // active: true to activate, false to deactivate.
    // ct: cancellation token.
    // returns: true if updated, false if not found.
    public async Task<bool> SetActiveAsync(int id, bool active, CancellationToken ct = default)
    {
        var item = await db.MenuItems.FindAsync([id], ct);
        if (item is null) return false;
        item.IsActive = active;
        audit.Add("MenuItemStatus", $"{item.Name} set {(active ? "active" : "inactive")}");
        await db.SaveChangesAsync(ct);
        return true;
    }

    // Permanently deletes a menu item and its recipe lines, and audits the deletion.
    // id: the menu item id to delete.
    // ct: cancellation token.
    // returns: true if deleted, false if not found.
    public async Task<bool> DeleteMenuItemAsync(int id, CancellationToken ct = default)
    {
        var item = await db.MenuItems.Include(m => m.Recipe).FirstOrDefaultAsync(m => m.Id == id, ct);
        if (item is null) return false;
        db.RecipeIngredients.RemoveRange(item.Recipe);
        db.MenuItems.Remove(item);
        audit.Add("MenuItemDeleted", $"{item.Name} permanently deleted");
        await db.SaveChangesAsync(ct);
        return true;
    }

    // Permanently deletes a modifier and audits the deletion.
    // id: the modifier id to delete.
    // ct: cancellation token.
    // returns: true if deleted, false if not found.
    public async Task<bool> DeleteModifierAsync(int id, CancellationToken ct = default)
    {
        var m = await db.Modifiers.FindAsync([id], ct);
        if (m is null) return false;
        db.Modifiers.Remove(m);
        audit.Add("ModifierDeleted", $"{m.GroupName}/{m.Name} permanently deleted");
        await db.SaveChangesAsync(ct);
        return true;
    }

    // Lists modifiers (active only unless includeInactive), ordered by group then name.
    // includeInactive: when true, includes inactive modifiers.
    // ct: cancellation token.
    // returns: the modifiers as DTOs.
    public async Task<List<ModifierDto>> ListModifiersAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var q = db.Modifiers.AsQueryable();
        if (!includeInactive) q = q.Where(m => m.IsActive);
        return await q.OrderBy(m => m.GroupName).ThenBy(m => m.Name)
            .Select(m => new ModifierDto(m.Id, m.Name, m.GroupName, m.PriceDelta, m.IsActive, m.AppliesTo)).ToListAsync(ct);
    }

    // Creates a modifier and audits the creation.
    // r: the modifier details to create.
    // ct: cancellation token.
    // returns: the created modifier DTO.
    public async Task<ModifierDto> CreateModifierAsync(ModifierRequest r, CancellationToken ct = default)
    {
        var m = new Modifier { Name = r.Name, GroupName = r.GroupName, PriceDelta = r.PriceDelta, IsActive = r.IsActive, AppliesTo = r.AppliesTo };
        db.Modifiers.Add(m);
        audit.Add("ModifierCreated", $"{r.GroupName}/{r.Name} ({r.PriceDelta:+0.00;-0.00;0})");
        await db.SaveChangesAsync(ct);
        return new ModifierDto(m.Id, m.Name, m.GroupName, m.PriceDelta, m.IsActive, m.AppliesTo);
    }

    // Updates a modifier's fields and audits the change.
    // id: the modifier id to update.
    // r: the new modifier details.
    // ct: cancellation token.
    // returns: the updated modifier DTO, or null if not found.
    public async Task<ModifierDto?> UpdateModifierAsync(int id, ModifierRequest r, CancellationToken ct = default)
    {
        var m = await db.Modifiers.FindAsync([id], ct);
        if (m is null) return null;
        m.Name = r.Name; m.GroupName = r.GroupName; m.PriceDelta = r.PriceDelta; m.IsActive = r.IsActive; m.AppliesTo = r.AppliesTo;
        audit.Add("ModifierUpdated", $"{r.GroupName}/{r.Name}");
        await db.SaveChangesAsync(ct);
        return new ModifierDto(m.Id, m.Name, m.GroupName, m.PriceDelta, m.IsActive, m.AppliesTo);
    }

    // Maps a menu item to its DTO, computing recipe cost and stock availability.
    // m: the menu item (with recipe and ingredients loaded) to map.
    // returns: the menu item DTO.
    private static MenuItemDto ToDto(MenuItem m)
    {
        var recipe = m.Recipe.Select(ri =>
            new RecipeLineDto(ri.IngredientId, ri.Ingredient?.Name ?? "", ri.Ingredient?.Unit ?? "", ri.Quantity)).ToList();
        var cost = m.Recipe.Sum(ri => ri.Quantity * (ri.Ingredient?.CostPerUnit ?? 0));
        // Available when every recipe ingredient has enough stock to make at least one unit.
        // Items with no recipe are always available.
        var available = m.Recipe.All(ri => ri.Ingredient is not null && ri.Ingredient.StockLevel >= ri.Quantity);
        return new MenuItemDto(m.Id, m.Name, m.Category, m.Price, m.IsActive, Math.Round(cost, 2), recipe, available);
    }
}
