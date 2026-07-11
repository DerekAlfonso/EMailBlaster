namespace EmailBlaster.Core.Models;

/// <summary>
/// A single message recipient. Every recipient has an email and (optionally) a name, plus any number
/// of extra columns carried over from the imported dataset. Extra columns are exposed to the merge
/// engine as placeholders.
/// </summary>
public sealed class Recipient
{
    /// <summary>The recipient's email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>The recipient's display name (may be empty).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Additional per-recipient fields keyed by column name (case-insensitive). These become
    /// available as merge placeholders, e.g. a column "Company" is referenced as <c>{{Company}}</c>.
    /// </summary>
    public IDictionary<string, string> Fields { get; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the full set of merge fields for this recipient, combining <see cref="Name"/> and
    /// <see cref="Email"/> with every extra column. Standard keys "Name" and "Email" always resolve.
    /// </summary>
    public IReadOnlyDictionary<string, string> ToMergeFields()
    {
        var merged = new Dictionary<string, string>(Fields, StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = Name,
            ["Email"] = Email
        };
        return merged;
    }

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Name) ? Email : $"{Name} <{Email}>";
}
