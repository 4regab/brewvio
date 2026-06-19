using Brewvio.Helpers;

namespace Brewvio.Tests.Unit;

public class NoHtmlAttributeTests
{
    private static bool IsValid(string? value) => new NoHtmlAttribute().IsValid(value);

    // ── Null / empty — always valid (opt-in) ─────────────────────────────────

    [Fact]
    public void Null_IsValid() => Assert.True(IsValid(null));

    [Fact]
    public void EmptyString_IsValid() => Assert.True(IsValid(""));

    // ── Plain text — always valid ─────────────────────────────────────────────

    [Theory]
    [InlineData("Hello world")]
    [InlineData("Chao & Brew POS")]
    [InlineData("Price: $9.99")]
    [InlineData("100% Arabica")]
    [InlineData("Room 2 / Table 4")]
    public void PlainText_IsValid(string value) => Assert.True(IsValid(value));

    // ── HTML tags — invalid ───────────────────────────────────────────────────

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<b>bold</b>")]
    [InlineData("<img src=x>")]
    [InlineData("</div>")]
    [InlineData("<a href='http://evil.com'>click</a>")]
    [InlineData("<INPUT type=text>")]          // uppercase tag
    [InlineData("before<br>after")]
    public void HtmlTags_AreInvalid(string value) => Assert.False(IsValid(value));

    // ── Dangerous keywords — invalid regardless of angle brackets ────────────

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JAVASCRIPT:void(0)")]         // uppercase — case-insensitive check
    [InlineData("img onerror=alert(1)")]
    [InlineData("body onload=steal()")]
    [InlineData("div onclick=hack()")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("VBSCRIPT:run")]               // uppercase
    public void DangerousKeywords_AreInvalid(string value) => Assert.False(IsValid(value));

    // ── Borderline / tricky cases ─────────────────────────────────────────────

    [Fact]
    public void ArithmeticLessThan_IsValid()
    {
        // "1 < 2" has a lone < but no closing >, so the regex <[^>]+> doesn't match.
        Assert.True(IsValid("1 < 2"));
    }

    [Fact]
    public void ArithmeticGreaterThan_IsValid()
    {
        Assert.True(IsValid("stock > 0"));
    }

    [Fact]
    public void MixedCaseTag_IsInvalid()
    {
        // Regex uses IgnoreCase — <Script> must still fail.
        Assert.False(IsValid("<Script>alert(1)</Script>"));
    }

    [Fact]
    public void KeywordEmbeddedInWord_IsInvalid()
    {
        // "onerror" inside a longer string is still caught.
        Assert.False(IsValid("image onerror handler"));
    }

    [Fact]
    public void TagWithAttributes_IsInvalid()
    {
        Assert.False(IsValid("<input type='text' name='q'>"));
    }

    [Fact]
    public void ErrorMessageSet_Correctly()
    {
        var attr = new NoHtmlAttribute();
        // ErrorMessage is null (it's only set when overridden explicitly); the
        // actual message comes from ErrorMessageString, which resolves the base
        // constructor argument.
        var result = attr.GetValidationResult("", new System.ComponentModel.DataAnnotations.ValidationContext(""));
        // Valid input — no error.  Use an invalid value to confirm the error text.
        var err = attr.GetValidationResult("<b>bold</b>", new System.ComponentModel.DataAnnotations.ValidationContext(""));
        Assert.NotNull(err);
        Assert.Equal("Value must not contain HTML or script content.", err!.ErrorMessage);
    }
}
