using System.Text.Json.Serialization;

namespace EmailBlaster.Core.Configuration;

/// <summary>
/// SMTP transport settings. Only used when <see cref="EmailBlasterConfig.Provider"/> is <see cref="SendProvider.Smtp"/>.
/// </summary>
public sealed class SmtpConfig
{
    /// <summary>SMTP server host name, e.g. <c>smtp.gmail.com</c>.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>SMTP server port. Common values: 587 (STARTTLS), 465 (implicit SSL), 25 (plain).</summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// How TLS is negotiated. Defaults to <see cref="SmtpSecurity.StartTls"/> which suits port 587.
    /// </summary>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    /// <summary>User name for SMTP authentication. Leave blank for anonymous relays.</summary>
    public string? Username { get; set; }

    /// <summary>Password for SMTP authentication.</summary>
    public string? Password { get; set; }

    /// <summary>True when a user name is supplied and authentication should be attempted.</summary>
    [JsonIgnore]
    public bool RequiresAuthentication => !string.IsNullOrWhiteSpace(Username);
}

/// <summary>
/// TLS negotiation strategy for the SMTP connection.
/// </summary>
public enum SmtpSecurity
{
    /// <summary>No transport encryption (plain text). Not recommended.</summary>
    None = 0,

    /// <summary>Upgrade the plain connection to TLS via the STARTTLS command (typically port 587).</summary>
    StartTls = 1,

    /// <summary>Connect using implicit SSL/TLS from the first byte (typically port 465).</summary>
    SslOnConnect = 2,

    /// <summary>Let the client pick the most appropriate option automatically.</summary>
    Auto = 3
}
