namespace Brewvio.Models;

// A completed (or refunded/cancelled) sale.
public class Transaction
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }          // includes tax, net of discount
    public string PaymentMethod { get; set; } = "";   // Cash | Card | Split
    public int CashierId { get; set; }
    public User Cashier { get; set; } = null!;
    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }
    public string Status { get; set; } = "Completed"; // Completed | Refunded | Cancelled
    public string? Notes { get; set; }                // refund/cancel reason
    public ICollection<TransactionItem> Items { get; set; } = new List<TransactionItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
