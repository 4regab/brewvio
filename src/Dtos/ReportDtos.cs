namespace Brewvio.Dtos;

public record SalesSummaryDto(decimal TotalSales, int TransactionCount, decimal AverageOrderValue,
    decimal TotalDiscounts, decimal TotalTax, decimal TotalCost, decimal GrossProfit,
    int ItemsSold, decimal ProfitMarginPercent);

public record SalesTrendPointDto(string Label, decimal Sales, int TransactionCount);

// MarginPercent = Profit / Revenue * 100. Used by the Menu Performance profitability view.
public record MenuPerformanceDto(int MenuItemId, string Name, string Category, int QuantitySold,
    decimal Revenue, decimal Cost, decimal Profit, decimal MarginPercent);

// Combined dashboard payload for a date range. Trend is grouped by the requested period
// (daily | weekly | monthly | yearly). BestSellers/SlowSellers are convenience slices of
// MenuPerformance for the Menu Performance screen.
public record ReportDto(string Period, SalesSummaryDto Summary, IReadOnlyList<SalesTrendPointDto> Trend,
    IReadOnlyList<MenuPerformanceDto> MenuPerformance,
    IReadOnlyList<MenuPerformanceDto> BestSellers, IReadOnlyList<MenuPerformanceDto> SlowSellers,
    IReadOnlyList<CategorySalesDto> CategoryBreakdown);

// Sales grouped by menu category (for the dashboard's category chart).
public record CategorySalesDto(string Category, int QuantitySold, decimal Revenue);
