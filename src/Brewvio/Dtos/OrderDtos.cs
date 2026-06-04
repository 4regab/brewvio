namespace Brewvio.Dtos;

// ----- Placing an order -----
public record CartItemInput(int MenuItemId, int Quantity, IReadOnlyList<int> ModifierIds, string? Notes);
public record PaymentInput(string Method, decimal Amount);          // Method: Cash | Card
// Discount is an absolute amount; PaymentMethod is derived from the payments (1=that method, >1=Split).
public record CreateOrderRequest(IReadOnlyList<CartItemInput> Items, decimal DiscountAmount,
    IReadOnlyList<PaymentInput> Payments);

// ----- Receipt / order result -----
public record ReceiptLineDto(string Name, int Quantity, decimal UnitPrice, decimal LineTotal, string? Modifiers);
public record ReceiptDto(int TransactionId, DateTime Timestamp, string Cashier, string PaymentMethod,
    decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal TotalAmount,
    decimal AmountTendered, decimal Change, string Status,
    IReadOnlyList<ReceiptLineDto> Items, IReadOnlyList<PaymentInput> Payments,
    IReadOnlyList<string> StockWarnings);

// ----- Cancel (pre-payment, audit only) & refund (existing transaction) -----
public record CancelOrderRequest(string Reason);
public record RefundRequest(string Reason);

// ----- Transaction history -----
public record TransactionSummaryDto(int Id, DateTime Timestamp, decimal TotalAmount, string PaymentMethod,
    string Status, string Cashier, int ItemCount);
