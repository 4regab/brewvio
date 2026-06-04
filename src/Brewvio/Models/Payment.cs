namespace Brewvio.Models;

// One tender against a transaction; multiple rows for a single transaction = split payment.
public class Payment
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;
    public string Method { get; set; } = "";          // Cash | Card
    public decimal Amount { get; set; }
}
