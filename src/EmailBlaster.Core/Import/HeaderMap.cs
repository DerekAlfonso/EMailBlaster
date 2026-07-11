namespace EmailBlaster.Core.Import;

/// <summary>
/// Inspects a set of column headers and works out which one holds the email address and which holds
/// the name. Matching is case-insensitive and tolerant of common variations. Shared by every importer
/// so CSV and XLSX behave identically.
/// </summary>
public sealed class HeaderMap
{
    private static readonly string[] EmailAliases =
        { "email", "e-mail", "email address", "e-mail address", "mail", "emailaddress" };

    private static readonly string[] NameAliases =
        { "name", "full name", "fullname", "recipient", "display name", "contact", "first name", "firstname" };

    /// <summary>Index of the email column, or -1 if none was identified.</summary>
    public int EmailIndex { get; }

    /// <summary>Index of the name column, or -1 if none was identified.</summary>
    public int NameIndex { get; }

    /// <summary>Original header text for each column (trimmed).</summary>
    public IReadOnlyList<string> Headers { get; }

    public HeaderMap(IReadOnlyList<string> headers)
    {
        Headers = headers.Select(h => (h ?? string.Empty).Trim()).ToList();

        EmailIndex = FindColumn(Headers, EmailAliases);
        NameIndex = FindColumn(Headers, NameAliases);

        // If no header matched "email" exactly, fall back to the first header that contains "mail".
        if (EmailIndex < 0)
            EmailIndex = IndexOfContaining(Headers, "mail");
    }

    /// <summary>True when an email column could be located.</summary>
    public bool HasEmail => EmailIndex >= 0;

    /// <summary>
    /// Column names exposed as placeholders in the UI: always "Name" and "Email" first, then every
    /// non-name/non-email header in source order.
    /// </summary>
    public IReadOnlyList<string> PlaceholderColumns()
    {
        var result = new List<string> { "Name", "Email" };
        for (var i = 0; i < Headers.Count; i++)
        {
            if (i == EmailIndex || i == NameIndex)
                continue;
            var header = Headers[i];
            if (!string.IsNullOrWhiteSpace(header) &&
                !result.Contains(header, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(header);
            }
        }
        return result;
    }

    private static int FindColumn(IReadOnlyList<string> headers, string[] aliases)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            foreach (var alias in aliases)
            {
                if (string.Equals(headers[i], alias, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return -1;
    }

    private static int IndexOfContaining(IReadOnlyList<string> headers, string fragment)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
