namespace Brewvio.Models;

// Audit "Action" names for stock movements. Centralised so the writers (InventoryService,
// OrderService) and the readers (AuditService general log + per-ingredient stock history)
// agree on the exact strings. Arrays are used for EF Core `Contains` -> SQL `IN` translation.
public static class StockActions
{
    public const string StockIn = "StockIn";
    public const string StockOut = "StockOut";
    public const string Adjust = "InventoryAdjust";   // existing stock-take action (kept as-is)
    public const string Sale = "StockSale";           // per-ingredient deduction at sale time
    public const string Refund = "StockRefund";       // per-ingredient restock at refund time

    // Every action shown in an ingredient's stock-movement history.
    public static readonly string[] All = { StockIn, StockOut, Adjust, Sale, Refund };

    // High-volume per-ingredient sale/refund rows are hidden from the general audit log view
    // (they remain available via the per-ingredient stock history endpoint).
    public static readonly string[] AuditExcluded = { Sale, Refund };
}
