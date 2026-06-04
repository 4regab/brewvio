using System.Text.Json.Serialization;

namespace Brewvio.Dtos;

// Status: "In Stock" | "Low Stock" | "Out of Stock" (derived from stock vs threshold).
public record IngredientDto(int Id, string Code, string Name, string Category, string Unit,
    decimal StockLevel, decimal Threshold, decimal CostPerUnit, bool LowStock, string Status);

public record IngredientRequest(string Code, string Name, string Category, string Unit,
    [property: JsonRequired] decimal StockLevel, [property: JsonRequired] decimal Threshold, [property: JsonRequired] decimal CostPerUnit);

// Manual stock-take: set the new absolute quantity and supply a mandatory reason (audited).
public record StockAdjustRequest([property: JsonRequired] decimal NewQuantity, string Reason);
