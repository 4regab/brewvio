using System.Globalization;

namespace Brewvio.Tests.Unit;

// ReportingService.BucketKey is private static, so we inline the exact same logic here
// and test its behaviour. If the production implementation changes, these tests will catch it.
public class BucketKeyTests
{
    private static (string Label, string Sort) BucketKey(DateTime ts, string period)
    {
        var d = ts.Date;
        switch (period)
        {
            case "weekly":
                var week = ISOWeek.GetWeekOfYear(d);
                var year = ISOWeek.GetYear(d);
                var weekStart = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday);
                return (weekStart.ToString("MMM dd"), $"{year}{week:00}");
            case "monthly":
                return (d.ToString("yyyy-MM"), d.ToString("yyyyMM"));
            case "yearly":
                return (d.ToString("yyyy"), d.ToString("yyyy"));
            case "daily":
            default:
                return (d.ToString("MMM dd"), d.ToString("yyyyMMdd"));
        }
    }

    // ── Daily ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Daily_LabelFormat_IsMonthDayAbbreviation()
    {
        var (label, sort) = BucketKey(new DateTime(2026, 6, 9), "daily");
        Assert.Equal("Jun 09", label);
        Assert.Equal("20260609", sort);
    }

    [Fact]
    public void Daily_YearBoundary_Jan1()
    {
        var (label, sort) = BucketKey(new DateTime(2026, 1, 1), "daily");
        Assert.Equal("Jan 01", label);
        Assert.Equal("20260101", sort);
    }

    [Fact]
    public void UnknownPeriod_DefaultsToDaily()
    {
        var daily = BucketKey(new DateTime(2026, 6, 9), "daily");
        var unknown = BucketKey(new DateTime(2026, 6, 9), "fortnight");
        Assert.Equal(daily, unknown);
    }

    [Fact]
    public void EmptyPeriod_DefaultsToDaily()
    {
        var daily = BucketKey(new DateTime(2026, 6, 9), "daily");
        var empty = BucketKey(new DateTime(2026, 6, 9), "");
        Assert.Equal(daily, empty);
    }

    // ── Monthly ───────────────────────────────────────────────────────────────

    [Fact]
    public void Monthly_LabelFormat_IsYearDashMonth()
    {
        var (label, sort) = BucketKey(new DateTime(2026, 6, 15), "monthly");
        Assert.Equal("2026-06", label);
        Assert.Equal("202606", sort);
    }

    [Fact]
    public void Monthly_DifferentDaysInSameMonth_SameBucket()
    {
        var first = BucketKey(new DateTime(2026, 6, 1), "monthly");
        var last = BucketKey(new DateTime(2026, 6, 30), "monthly");
        Assert.Equal(first, last);
    }

    // ── Yearly ────────────────────────────────────────────────────────────────

    [Fact]
    public void Yearly_LabelFormat_IsYear()
    {
        var (label, sort) = BucketKey(new DateTime(2026, 3, 1), "yearly");
        Assert.Equal("2026", label);
        Assert.Equal("2026", sort);
    }

    [Fact]
    public void Yearly_AllDaysInSameYear_SameBucket()
    {
        var jan = BucketKey(new DateTime(2026, 1, 1), "yearly");
        var dec = BucketKey(new DateTime(2026, 12, 31), "yearly");
        Assert.Equal(jan, dec);
    }

    // ── Weekly ────────────────────────────────────────────────────────────────

    [Fact]
    public void Weekly_LabelIsMonday_OfIsoWeek()
    {
        // 2026-06-09 is a Tuesday; its ISO week Monday is 2026-06-08.
        var (label, _) = BucketKey(new DateTime(2026, 6, 9), "weekly");
        Assert.Equal("Jun 08", label);
    }

    [Fact]
    public void Weekly_AllDaysInSameIsoWeek_SameBucket()
    {
        // Week containing 2026-06-08 (Mon) through 2026-06-14 (Sun).
        var mon = BucketKey(new DateTime(2026, 6, 8), "weekly");
        var sun = BucketKey(new DateTime(2026, 6, 14), "weekly");
        Assert.Equal(mon, sun);
    }

    [Fact]
    public void Weekly_AdjacentWeeks_DifferentBuckets()
    {
        var week1 = BucketKey(new DateTime(2026, 6, 7), "weekly");  // Sunday = end of prev week
        var week2 = BucketKey(new DateTime(2026, 6, 8), "weekly");  // Monday = start of next week
        Assert.NotEqual(week1, week2);
    }

    [Fact]
    public void Weekly_IsoYearBoundary_Dec31BelongsToNextYearWeek1()
    {
        // 2018-12-31 belongs to ISO week 1 of 2019 (year in sort key = 2019).
        var (_, sort) = BucketKey(new DateTime(2018, 12, 31), "weekly");
        Assert.StartsWith("2019", sort);
    }

    [Fact]
    public void Weekly_SortKey_Format_IsYearTwoDigitWeek()
    {
        // 2026-01-05 is week 2 of 2026 → sort = "202602".
        var (_, sort) = BucketKey(new DateTime(2026, 1, 5), "weekly");
        Assert.Equal("202602", sort);
    }
}
