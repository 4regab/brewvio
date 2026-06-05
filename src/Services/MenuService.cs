using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Services;

// Menu & recipe management plus modifiers. Recipe cost is derived from ingredient unit costs.
public class MenuService(BrewvioDbContext db, AuditService audit)
{
    public async Task<List<MenuItemDto>> ListAsync(bool includeInactive = false)
    {
        var q = db.MenuItems.AsNoTracking().Include(m => m.Recipe).ThenInclude(r => r.Ingredient).AsQueryable();
        if (!includeInactive) q = q.Where(m => m.IsActive);
        var items = await q.OrderBy(m => m.Category).ThenBy(m => m.Name).ToListAsync();
        return items.Select(ToDto).ToList();
    }

    public async Task<MenuItemDto?> GetAsync(int id)
    {
        var m = await db.MenuItems.Include(x => x.Recipe).ThenInclude(r => r.Ingredient)
            .FirstOrDefaultAsync(x => x.Id == id);
        return m is null ? null : ToDto(m);
    }

    public async Task<MenuItemDto?> CreateAsync(MenuItemRequest r)
    {
        var item = new MenuItem
        {
            Name = r.Name, Category = r.Category, Price = r.Price, IsActive = r.IsActive,
            Recipe = r.Recipe.Select(l => new RecipeIngredient { IngredientId = l.IngredientId, Quantity = l.Quantity }).ToList()
        };
        db.MenuItems.Add(item);
        audit.Add("MenuItemCreated", $"{r.Name} @ {r.Price} ({r.Recipe.Count} recipe line(s))");
        await db.SaveChangesAsync();
        return await GetAsync(item.Id);
    }

    public async Task<MenuItemDto?> UpdateAsync(int id, MenuItemRequest r)
    {
        var item = await db.MenuItems.Include(m => m.Recipe).FirstOrDefaultAsync(m => m.Id == id);
        if (item is null) return null;
        item.Name = r.Name; item.Category = r.Category; item.Price = r.Price; item.IsActive = r.IsActive;
        db.RecipeIngredients.RemoveRange(item.Recipe);
        item.Recipe = r.Recipe.Select(l => new RecipeIngredient { IngredientId = l.IngredientId, Quantity = l.Quantity }).ToList();
        audit.Add("MenuItemUpdated", $"{r.Name} @ {r.Price}");
        await db.SaveChangesAsync();
        return await GetAsync(id);
    }

    public async Task<bool> SetActiveAsync(int id, bool active)
    {
        var item = await db.MenuItems.FindAsync(id);
        if (item is null) return false;
        item.IsActive = active;
        audit.Add("MenuItemStatus", $"{item.Name} set {(active ? "active" : "inactive")}");
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteMenuItemAsync(int id)
    {
        var item = await db.MenuItems.Include(m => m.Recipe).FirstOrDefaultAsync(m => m.Id == id);
        if (item is null) return false;
        db.RecipeIngredients.RemoveRange(item.Recipe);
        db.MenuItems.Remove(item);
        audit.Add("MenuItemDeleted", $"{item.Name} permanently deleted");
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteModifierAsync(int id)
    {
        var m = await db.Modifiers.FindAsync(id);
        if (m is null) return false;
        db.Modifiers.Remove(m);
        audit.Add("ModifierDeleted", $"{m.GroupName}/{m.Name} permanently deleted");
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<ModifierDto>> ListModifiersAsync(bool includeInactive = false)
    {
        var q = db.Modifiers.AsQueryable();
        if (!includeInactive) q = q.Where(m => m.IsActive);
        return await q.OrderBy(m => m.GroupName).ThenBy(m => m.Name)
            .Select(m => new ModifierDto(m.Id, m.Name, m.GroupName, m.PriceDelta, m.IsActive)).ToListAsync();
    }

    public async Task<ModifierDto> CreateModifierAsync(ModifierRequest r)
    {
        var m = new Modifier { Name = r.Name, GroupName = r.GroupName, PriceDelta = r.PriceDelta, IsActive = r.IsActive };
        db.Modifiers.Add(m);
        audit.Add("ModifierCreated", $"{r.GroupName}/{r.Name} ({r.PriceDelta:+0.00;-0.00;0})");
        await db.SaveChangesAsync();
        return new ModifierDto(m.Id, m.Name, m.GroupName, m.PriceDelta, m.IsActive);
    }

    public async Task<ModifierDto?> UpdateModifierAsync(int id, ModifierRequest r)
    {
        var m = await db.Modifiers.FindAsync(id);
        if (m is null) return null;
        m.Name = r.Name; m.GroupName = r.GroupName; m.PriceDelta = r.PriceDelta; m.IsActive = r.IsActive;
        audit.Add("ModifierUpdated", $"{r.GroupName}/{r.Name}");
        await db.SaveChangesAsync();
        return new ModifierDto(m.Id, m.Name, m.GroupName, m.PriceDelta, m.IsActive);
    }

    private static MenuItemDto ToDto(MenuItem m)
    {
        var recipe = m.Recipe.Select(ri =>
            new RecipeLineDto(ri.IngredientId, ri.Ingredient?.Name ?? "", ri.Ingredient?.Unit ?? "", ri.Quantity)).ToList();
        var cost = m.Recipe.Sum(ri => ri.Quantity * (ri.Ingredient?.CostPerUnit ?? 0));
        return new MenuItemDto(m.Id, m.Name, m.Category, m.Price, m.IsActive, Math.Round(cost, 2), recipe);
    }
}
