namespace Brewvio.Models;

// ── Discount polymorphism (OOP exhibit) ──────────────────────────────────────────
// A small abstract hierarchy used by OrderService to resolve the discount to apply
// against a cart subtotal. Subclasses override Apply(subtotal) so OrderService can
// hold a single Discount reference and let the runtime dispatch pick the right
// calculation (fixed peso amount today, percent for promotions tomorrow).
//
// Pricing is rounded to 2dp (peso/centavo) and clamped to [0, subtotal] so a
// discount can never exceed the cart or go negative — exactly matching the
// previous inline behavior in OrderService, so totals are byte-identical.
public abstract class Discount
{
    // Returns the peso amount to subtract from {subtotal}. Always in [0, subtotal],
    // rounded to 2 decimal places.
    public abstract decimal Apply(decimal subtotal);

    // Shared clamp/round used by every concrete discount; kept protected so subclasses
    // (and only subclasses) can opt into the same rounding convention.
    protected static decimal ClampAndRound(decimal amount, decimal subtotal) =>
        Math.Clamp(Math.Round(amount, 2), 0m, Math.Round(subtotal, 2));
}

// A flat peso discount (e.g. PHP 50 off). The historical default — preserves the
// exact behavior OrderService had before the Discount hierarchy was introduced.
public sealed class FixedAmountDiscount : Discount
{
    private readonly decimal _amount;
    // Stores the flat peso discount amount to apply.
    // amount: the fixed peso amount to subtract from a subtotal.
    public FixedAmountDiscount(decimal amount) => _amount = amount;
    // Returns the fixed amount, clamped/rounded against the subtotal.
    // subtotal: the cart subtotal to discount against.
    // returns: peso amount to subtract, in [0, subtotal] rounded to 2dp.
    public override decimal Apply(decimal subtotal) => ClampAndRound(_amount, subtotal);
}

// A percent-based discount (e.g. 10% senior). Demonstrates polymorphic dispatch:
// OrderService doesn't change to support this — only the Discount instance does.
// Kept available for future promotions; not currently constructed at runtime.
public sealed class PercentDiscount : Discount
{
    private readonly decimal _percent;
    // Stores the discount percentage, clamped to [0, 100].
    // percent: the percentage to discount.
    public PercentDiscount(decimal percent) =>
        _percent = Math.Clamp(percent, 0m, 100m);
    // Returns the percent of the subtotal, clamped/rounded against the subtotal.
    // subtotal: the cart subtotal to discount against.
    // returns: peso amount to subtract, in [0, subtotal] rounded to 2dp.
    public override decimal Apply(decimal subtotal) =>
        ClampAndRound(subtotal * _percent / 100m, subtotal);
}
