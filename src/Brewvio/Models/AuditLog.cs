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
}
