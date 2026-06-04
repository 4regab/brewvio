namespace Brewvio.Models;

// A cashier work period; transactions are grouped under the open shift.
public class Shift
{
    public int Id { get; set; }
    public int CashierId { get; set; }
    public User Cashier { get; set; } = null!;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public decimal StartingCash { get; set; }
    public decimal? EndingCash { get; set; }
    public string Status { get; set; } = "Open";     // Open | Closed
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
