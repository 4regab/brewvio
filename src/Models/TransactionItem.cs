namespace Brewvio.Models;

// A line item on a transaction (snapshots name/price so historical receipts stay stable).
public class TransactionItem
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public string ItemName { get; set; } = "";
    public decimal UnitPrice { get; set; }            // base price + modifier deltas
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public string? Modifiers { get; set; }            // human-readable chosen modifiers
}
