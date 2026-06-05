namespace Brewvio.Dtos;

public record SalesSummaryDto(decimal TotalSales, int TransactionCount, decimal AverageOrderValue,
    decimal TotalDiscounts, decimal TotalTax, int ItemsSold, decimal ProfitMarginPercent = 0m);

public record SalesTrendPointDto(string Label, decimal Sales, int TransactionCount);

// Used by the Menu Performance view — includes cost-based profit fields.
public record MenuPerformanceDto(int MenuItemId, string Name, string Category, int QuantitySold,
    decimal Revenue, decimal Profit = 0m, decimal MarginPercent = 0m);

// Combined dashboard payload for a date range. Trend is grouped by the requested period
// (daily | weekly | monthly | yearly). BestSellers/SlowSellers are convenience slices of
// MenuPerformance for the Menu Performance screen.
public record ReportDto(string Period, SalesSummaryDto Summary, IReadOnlyList<SalesTrendPointDto> Trend,
    IReadOnlyList<MenuPerformanceDto> MenuPerformance,
    IReadOnlyList<MenuPerformanceDto> BestSellers, IReadOnlyList<MenuPerformanceDto> SlowSellers,
    IReadOnlyList<CategorySalesDto> CategoryBreakdown);

// Sales grouped by menu category (for the dashboard's category chart).
public record CategorySalesDto(string Category, int QuantitySold, decimal Revenue);
