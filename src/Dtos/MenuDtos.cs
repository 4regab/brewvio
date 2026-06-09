using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Brewvio.Helpers;

namespace Brewvio.Dtos;

// ----- Menu items & recipes -----
public record RecipeLineDto(int IngredientId, string IngredientName, string Unit, decimal Quantity);
public record MenuItemDto(int Id, string Name, string Category, decimal Price, bool IsActive,
    decimal Cost, IReadOnlyList<RecipeLineDto> Recipe, bool Available);

public record RecipeLineInput([property: JsonRequired] int IngredientId, [property: JsonRequired] decimal Quantity);
public record MenuItemRequest(
    [property: Required, MaxLength(120), NoHtml] string Name,
    [property: MaxLength(60),  NoHtml] string Category,
    [property: JsonRequired] decimal Price,
    [property: JsonRequired] bool IsActive,
    IReadOnlyList<RecipeLineInput> Recipe);

// ----- Modifiers -----
public record ModifierDto(int Id, string Name, string GroupName, decimal PriceDelta, bool IsActive, string? AppliesTo);
public record ModifierRequest(
    [property: Required, MaxLength(120), NoHtml] string Name,
    [property: MaxLength(60),  NoHtml] string GroupName,
    [property: JsonRequired] decimal PriceDelta,
    [property: JsonRequired] bool IsActive,
    string? AppliesTo = null);
