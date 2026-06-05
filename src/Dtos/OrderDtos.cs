using System.Text.Json.Serialization;

namespace Brewvio.Dtos;

// ----- Placing an order -----
public record CartItemInput([property: JsonRequired] int MenuItemId, [property: JsonRequired] int Quantity, IReadOnlyList<int> ModifierIds, string? Notes);
public record PaymentInput(string Method, [property: JsonRequired] decimal Amount);          // Method: Cash | Card
// Discount is an absolute amount; PaymentMethod is derived from the payments (1=that method, >1=Split).
public record CreateOrderRequest(IReadOnlyList<CartItemInput> Items, [property: JsonRequired] decimal DiscountAmount,
    IReadOnlyList<PaymentInput> Payments);

// ----- Receipt / order result -----
public record ReceiptLineDto(string Name, int Quantity, decimal UnitPrice, decimal LineTotal, string? Modifiers);
public record ReceiptDto(int TransactionId, DateTime Timestamp, string Cashier, string PaymentMethod,
    decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal TotalAmount,
    decimal AmountTendered, decimal Change, string Status,
    IReadOnlyList<ReceiptLineDto> Items, IReadOnlyList<PaymentInput> Payments,
    IReadOnlyList<string> StockWarnings);

// ----- Draft order -----
public record SaveDraftRequest(IReadOnlyList<CartItemInput> Items, decimal DiscountAmount, string PaymentMethod);
public record ConfirmDraftRequest(IReadOnlyList<PaymentInput> Payments);
public record DraftDto(int Id, DateTime Timestamp, string Cashier, string PaymentMethod,
    decimal Subtotal, decimal DiscountAmount, int ItemCount, string ItemSummary,
    IReadOnlyList<ReceiptLineDto> Items);

// ----- Cancel (pre-payment, audit only) & refund (existing transaction) -----
public record CancelOrderRequest(string Reason);
public record RefundRequest(string Reason);

// ----- Transaction history -----
public record TransactionSummaryDto(int Id, DateTime Timestamp, decimal TotalAmount, string PaymentMethod,
    string Status, string Cashier, int ItemCount, string ItemSummary);
