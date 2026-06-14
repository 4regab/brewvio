using System.Text.Json.Serialization;

namespace Brewvio.Dtos;

// Status: "In Stock" | "Low Stock" | "Out of Stock" (derived from stock vs threshold).
public record IngredientDto(int Id, string Code, string Name, string Category, string Unit,
    decimal StockLevel, decimal Threshold, decimal CostPerUnit, bool LowStock, string Status);

public record IngredientRequest(string Code, string Name, string Category, string Unit,
    [property: JsonRequired] decimal StockLevel, [property: JsonRequired] decimal Threshold, [property: JsonRequired] decimal CostPerUnit);

// Manual stock-take: set the new absolute quantity and supply a mandatory reason (audited).
public record StockAdjustRequest([property: JsonRequired] decimal NewQuantity, string Reason);

// Stock In / Stock Out: a positive quantity delta applied to current stock. Reason is optional
// for Stock In (receipts) and required for Stock Out (losses). Audited as a per-ingredient movement.
public record StockMovementRequest([property: JsonRequired] decimal Quantity, string? Reason);

// One row of stock-movement history (projected from AuditLog, newest first). Quantity is the signed
// change (+ for in/refund, - for out/sale, +/- for an adjustment); BalanceAfter is the resulting stock.
public record StockMovementDto(int Id, DateTime Timestamp, int? IngredientId, string IngredientName,
    string IngredientCode, string Username, string Action, decimal? Quantity, decimal? BalanceAfter, string Details);

// A page of stock movements for the dedicated Stock Movements view (Total = full filtered count).
public record PagedStockMovementsDto(int Total, int Skip, int Take, List<StockMovementDto> Items);
