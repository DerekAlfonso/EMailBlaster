namespace EmailBlaster.Core.Models;

/// <summary>
/// A fully-resolved message ready to be handed to a transport. Placeholders have already been merged.
/// </summary>
public sealed class EmailMessage
{
    /// <summary>Recipient address.</summary>
    public string ToEmail { get; set; } = string.Empty;

    /// <summary>Recipient display name (optional).</summary>
    public string ToName { get; set; } = string.Empty;

    /// <summary>Subject line (merged).</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>HTML body (merged).</summary>
    public string HtmlBody { get; set; } = string.Empty;

    /// <summary>
    /// Optional plain-text alternative. When null, transports derive a basic text part from the HTML.
    /// </summary>
    public string? TextBody { get; set; }
}
