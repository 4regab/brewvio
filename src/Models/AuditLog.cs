namespace Brewvio.Models;

// Immutable record of a critical action (inventory change, cancel/refund, user mgmt, login).
public class AuditLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int? UserId { get; set; }
    public string Username { get; set; } = "";
    public string Action { get; set; } = "";          // e.g., InventoryAdjust, OrderCancelled
    public string Details { get; set; } = "";
    // Optional link to the ingredient this entry concerns, set on stock-movement actions
    // (StockIn/StockOut/InventoryAdjust/StockSale/StockRefund) so per-ingredient stock history
    // is queryable. No FK: audit rows are immutable history and must survive ingredient deletion.
    public int? IngredientId { get; set; }
    // Structured stock-ledger fields (set only on stock-movement rows): the signed change applied
    // (+ for in/refund, - for out/sale, +/- for an adjustment) and the resulting stock level.
    public decimal? Quantity { get; set; }
    public decimal? BalanceAfter { get; set; }
}
