namespace EmailBlaster.Core.Models;

/// <summary>
/// An unresolved message template containing placeholders such as <c>{{Name}}</c>. It is merged
/// against each recipient to produce an <see cref="EmailMessage"/>.
/// </summary>
public sealed class EmailTemplate
{
    /// <summary>Subject line, may contain placeholders.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>HTML body, may contain placeholders.</summary>
    public string HtmlBody { get; set; } = string.Empty;

    /// <summary>Optional plain-text body. When null, a text part is generated from the HTML at send time.</summary>
    public string? TextBody { get; set; }
}
