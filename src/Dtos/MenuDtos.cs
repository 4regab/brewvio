using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Brewvio.Helpers;

namespace Brewvio.Dtos;

// ----- Menu items & recipes -----
public record RecipeLineDto(int IngredientId, string IngredientName, string Unit, decimal Quantity);
// A menu item with its recipe lines, computed cost, and availability flag.
public record MenuItemDto(int Id, string Name, string Category, decimal Price, bool IsActive,
    decimal Cost, IReadOnlyList<RecipeLineDto> Recipe, bool Available);

// One recipe line in a menu-item create/update request (ingredient + required quantity).
public record RecipeLineInput([property: JsonRequired] int IngredientId, [property: JsonRequired] decimal Quantity);
// Create/update payload for a menu item, including its recipe lines.
public record MenuItemRequest(
    [property: Required, MaxLength(120), NoHtml] string Name,
    [property: MaxLength(60),  NoHtml] string Category,
    [property: JsonRequired] decimal Price,
    [property: JsonRequired] bool IsActive,
    IReadOnlyList<RecipeLineInput> Recipe);

// ----- Modifiers -----
public record ModifierDto(int Id, string Name, string GroupName, decimal PriceDelta, bool IsActive, string? AppliesTo);
// Create/update payload for a modifier (an add-on/option that adjusts price by a delta).
public record ModifierRequest(
    [property: Required, MaxLength(120), NoHtml] string Name,
    [property: MaxLength(60),  NoHtml] string GroupName,
    [property: JsonRequired] decimal PriceDelta,
    [property: JsonRequired] bool IsActive,
    string? AppliesTo = null);
