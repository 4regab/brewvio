namespace Brewvio.Dtos;

public record StartShiftRequest(decimal StartingCash);
public record EndShiftRequest(decimal EndingCash);
public record ShiftDto(int Id, string Cashier, DateTime StartTime, DateTime? EndTime,
    decimal StartingCash, decimal? EndingCash, string Status,
    decimal TotalSales, int TransactionCount, decimal CashSales, decimal CardSales,
    decimal ExpectedCash, decimal? CashVariance);
