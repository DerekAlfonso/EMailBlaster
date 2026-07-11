namespace EmailBlaster.Core.Models;

/// <summary>
/// The result of importing recipients: the recipients themselves plus the ordered set of column
/// names discovered in the source (so the UI can offer them as insertable placeholders).
/// </summary>
public sealed class RecipientList
{
    /// <summary>Imported recipients.</summary>
    public IReadOnlyList<Recipient> Recipients { get; }

    /// <summary>
    /// Distinct column names available as placeholders, in source order. Always includes
    /// "Name" and "Email" first.
    /// </summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Rows skipped because they had no usable email address.</summary>
    public int SkippedRows { get; }

    public RecipientList(IReadOnlyList<Recipient> recipients, IReadOnlyList<string> columns, int skippedRows)
    {
        Recipients = recipients;
        Columns = columns;
        SkippedRows = skippedRows;
    }

    /// <summary>Number of usable recipients.</summary>
    public int Count => Recipients.Count;

    /// <summary>An empty list.</summary>
    public static RecipientList Empty { get; } =
        new(Array.Empty<Recipient>(), new[] { "Name", "Email" }, 0);
}
