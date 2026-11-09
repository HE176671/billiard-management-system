using System.Text.RegularExpressions;

namespace Bms.Web.Services;

public static partial class SePayPaymentReference
{
    // Content is supplied by the authenticated bank webhook. Accept the old 32-character
    // suffix for requests already issued, as well as the current 30-character suffix.
    [GeneratedRegex(@"(?<![A-Z0-9])NAPCK(?:[A-F0-9]{32}|[A-F0-9]{30})(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WalletReferencePattern();

    [GeneratedRegex(@"(?<![A-Z0-9])HD[A-F0-9]{32}(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CheckoutReferencePattern();

    public static string? Resolve(string? code, string? content)
    {
        var supplied = string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
        var references = WalletReferencePattern().Matches(content ?? "").Cast<Match>()
            .Concat(CheckoutReferencePattern().Matches(content ?? "").Cast<Match>())
            .Select(match => match.Value.ToUpperInvariant()).Distinct().ToArray();
        // Never guess which customer's wallet to credit if bank content is ambiguous.
        if (references.Length > 1) return null;
        if (references.Length == 1)
            return supplied == null || supplied == references[0] ? references[0] : null;
        return supplied;
    }
}
