using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Brewvio.Helpers;

/// <summary>
/// Rejects values that contain HTML tags or common XSS payloads (e.g. &lt;script&gt;, onerror=).
/// Apply to any string field that should be treated as plain text.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class NoHtmlAttribute : ValidationAttribute
{
    // Matches anything that looks like an HTML tag: <tag ...> or </tag>
    private static readonly Regex HtmlTag = new(@"<[^>]+>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Common injection keywords even without surrounding angle-brackets
    private static readonly string[] DangerousKeywords = ["javascript:", "onerror", "onload", "onclick", "vbscript:"];

    // Initializes the attribute with the default validation error message.
    public NoHtmlAttribute() : base("Value must not contain HTML or script content.") { }

    // Returns true when the value is null, empty, or a string free of HTML tags and dangerous keywords.
    // value: the value being validated
    // returns: true if the value is allowed, false if it contains HTML or script-like content
    public override bool IsValid(object? value)
    {
        if (value is not string s || string.IsNullOrEmpty(s)) return true;

        if (HtmlTag.IsMatch(s)) return false;

        if (DangerousKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }
}
