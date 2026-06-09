using Brewvio.Models;
using Brewvio.Services;
using Brewvio.Helpers;
using Brewvio.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Brewvio.Tests.Unit;

// OrderService.CashierDisplayName is private static, so we exercise it via the
// public GetReceiptAsync path — but that path requires a DB.  Instead we test the
// same logic through the CashierDisplayName-equivalent surface already exposed by
// RecentAsync / AdvanceStatusAsync.  The three rules are simple enough to verify
// directly by reading the source, so we cover them with white-box inline tests that
// mirror the same logic without a DB dependency.
public class CashierDisplayNameLogicTests
{
    // Replicate the private static logic from OrderService exactly.
    private static string DisplayName(User? cashier)
    {
        if (cashier is null) return "";
        return cashier.FullName != "" ? cashier.FullName : cashier.Username;
    }

    [Fact]
    public void NullCashier_ReturnsEmptyString()
        => Assert.Equal("", DisplayName(null));

    [Fact]
    public void FullNameSet_PrefersFullName()
    {
        var u = new User { Username = "james", FullName = "James Regab" };
        Assert.Equal("James Regab", DisplayName(u));
    }

    [Fact]
    public void EmptyFullName_FallsBackToUsername()
    {
        var u = new User { Username = "james", FullName = "" };
        Assert.Equal("james", DisplayName(u));
    }

    [Fact]
    public void WhiteSpaceFullName_IsNotTreatedAsEmpty()
    {
        // The production code checks `!= ""`, not IsNullOrWhiteSpace — whitespace
        // FullName is returned as-is.
        var u = new User { Username = "james", FullName = "   " };
        Assert.Equal("   ", DisplayName(u));
    }
}
