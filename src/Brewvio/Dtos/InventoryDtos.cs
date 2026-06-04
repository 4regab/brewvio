namespace Brewvio.Dtos;

// Status: "In Stock" | "Low Stock" | "Out of Stock" (derived from stock vs threshold).
public record IngredientDto(int Id, string Code, string Name, string Category, string Unit,
    decimal StockLevel, decimal Threshold, decimal CostPerUnit, bool LowStock, string Status);

public record IngredientRequest(string Code, string Name, string Category, string Unit,
    decimal StockLevel, decimal Threshold, decimal CostPerUnit);

// Manual stock-take: set the new absolute quantity and supply a mandatory reason (audited).
public record StockAdjustRequest(decimal NewQuantity, string Reason);
