using System.Text;
using System.Text.RegularExpressions;
using EmailBlaster.Core.Models;

namespace EmailBlaster.Core.Templating;

/// <summary>
/// Merges <c>{{Placeholder}}</c> tokens in a template against a recipient's fields. Placeholder
/// names are case-insensitive. An optional default can be supplied with a pipe:
/// <c>{{Name|there}}</c> renders "there" when the Name field is missing or empty.
/// Use <c>{{{{</c> / <c>}}}}</c> to emit literal braces.
/// </summary>
public static class PlaceholderMerger
{
    // Matches {{ Field }} or {{ Field | default text }} — non-greedy, tolerant of surrounding spaces.
    private static readonly Regex TokenRegex = new(
        @"\{\{\s*(?<key>[^{}|]+?)\s*(\|\s*(?<default>[^{}]*?)\s*)?\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Merges a raw template string against the supplied fields.</summary>
    public static string Merge(string template, IReadOnlyDictionary<string, string> fields)
    {
        if (string.IsNullOrEmpty(template))
            return template ?? string.Empty;

        return TokenRegex.Replace(template, match =>
        {
            var key = match.Groups["key"].Value.Trim();
            var fallback = match.Groups["default"].Success ? match.Groups["default"].Value : string.Empty;

            if (fields.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                return value;

            return fallback;
        });
    }

    /// <summary>Merges a full <see cref="EmailTemplate"/> against a recipient into a ready-to-send message.</summary>
    public static EmailMessage Merge(EmailTemplate template, Recipient recipient)
    {
        var fields = recipient.ToMergeFields();
        return new EmailMessage
        {
            ToEmail = recipient.Email,
            ToName = recipient.Name,
            Subject = Merge(template.Subject, fields),
            HtmlBody = Merge(template.HtmlBody, fields),
            TextBody = template.TextBody is null ? null : Merge(template.TextBody, fields)
        };
    }

    /// <summary>
    /// Returns the distinct placeholder names referenced in a template, in first-seen order.
    /// Useful for warning the user about placeholders that no column will satisfy.
    /// </summary>
    public static IReadOnlyList<string> ExtractPlaceholders(string template)
    {
        var seen = new List<string>();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(template))
            return seen;

        foreach (Match match in TokenRegex.Matches(template))
        {
            var key = match.Groups["key"].Value.Trim();
            if (set.Add(key))
                seen.Add(key);
        }
        return seen;
    }

    /// <summary>
    /// Produces a very simple plain-text rendering of an HTML body: strips tags, decodes a handful of
    /// common entities and collapses whitespace. Transports use this when no explicit text body exists.
    /// </summary>
    public static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        // Turn block-level breaks into newlines before stripping tags.
        var text = Regex.Replace(html, @"<\s*(br|/p|/div|/h[1-6]|/li)\s*>", "\n",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);

        var sb = new StringBuilder(text)
            .Replace("&nbsp;", " ")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'");

        // Collapse runs of blank lines / trailing spaces.
        var collapsed = Regex.Replace(sb.ToString(), @"[ \t]+\n", "\n");
        collapsed = Regex.Replace(collapsed, @"\n{3,}", "\n\n");
        return collapsed.Trim();
    }
}
