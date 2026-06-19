using Brewvio.Models;

namespace Brewvio.Tests;

/// <summary>
/// Unit tests for the Discount hierarchy — no DB needed, pure in-process logic.
/// Covers FixedAmountDiscount and PercentDiscount including all clamp/rounding edge cases.
/// </summary>
public class DiscountTests
{
    // ── FixedAmountDiscount ──────────────────────────────────────────────────

    [Fact]
    public void FixedAmount_subtracts_exact_amount()
    {
        Assert.Equal(50m, new FixedAmountDiscount(50m).Apply(200m));
    }

    [Fact]
    public void FixedAmount_rounds_to_two_decimal_places()
    {
        // 33.333... rounds to 33.33
        Assert.Equal(33.33m, new FixedAmountDiscount(33.333m).Apply(200m));
    }

    [Fact]
    public void FixedAmount_clamped_to_subtotal_when_discount_exceeds_it()
    {
        // Discount of 500 on a 100 subtotal → capped at 100
        Assert.Equal(100m, new FixedAmountDiscount(500m).Apply(100m));
    }

    [Fact]
    public void FixedAmount_clamped_to_zero_when_amount_is_negative()
    {
        Assert.Equal(0m, new FixedAmountDiscount(-20m).Apply(100m));
    }

    [Fact]
    public void FixedAmount_zero_discount_returns_zero()
    {
        Assert.Equal(0m, new FixedAmountDiscount(0m).Apply(150m));
    }

    [Fact]
    public void FixedAmount_on_zero_subtotal_returns_zero()
    {
        // Subtotal is 0; discount must be clamped to [0, 0]
        Assert.Equal(0m, new FixedAmountDiscount(50m).Apply(0m));
    }

    [Fact]
    public void FixedAmount_exact_subtotal_returns_full_subtotal()
    {
        Assert.Equal(99.99m, new FixedAmountDiscount(99.99m).Apply(99.99m));
    }

    // ── PercentDiscount ──────────────────────────────────────────────────────

    [Fact]
    public void PercentDiscount_ten_percent_of_200_is_20()
    {
        Assert.Equal(20m, new PercentDiscount(10m).Apply(200m));
    }

    [Fact]
    public void PercentDiscount_100_percent_equals_full_subtotal()
    {
        Assert.Equal(150m, new PercentDiscount(100m).Apply(150m));
    }

    [Fact]
    public void PercentDiscount_zero_percent_returns_zero()
    {
        Assert.Equal(0m, new PercentDiscount(0m).Apply(300m));
    }

    [Fact]
    public void PercentDiscount_clamps_percent_above_100_to_100()
    {
        // 150% is illegal; clamped to 100% → discount = subtotal
        Assert.Equal(200m, new PercentDiscount(150m).Apply(200m));
    }

    [Fact]
    public void PercentDiscount_clamps_negative_percent_to_zero()
    {
        Assert.Equal(0m, new PercentDiscount(-10m).Apply(200m));
    }

    [Fact]
    public void PercentDiscount_rounds_to_two_decimal_places()
    {
        // 10% of 99.99 = 9.999 → rounds to 10.00
        Assert.Equal(10.00m, new PercentDiscount(10m).Apply(99.99m));
    }

    [Fact]
    public void PercentDiscount_on_zero_subtotal_returns_zero()
    {
        Assert.Equal(0m, new PercentDiscount(50m).Apply(0m));
    }

    [Fact]
    public void PercentDiscount_fractional_percent_is_computed_correctly()
    {
        // 12.5% of 80 = 10.00
        Assert.Equal(10.00m, new PercentDiscount(12.5m).Apply(80m));
    }
}
