using EmailBlaster.Core.Configuration;

namespace EmailBlaster.Web;

// JSON is serialized camelCase by ASP.NET Core defaults, so these PascalCase C# members map to
// camelCase on the wire (e.g. SendRatePerSecond -> sendRatePerSecond).

public sealed class SmtpDto
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Security { get; set; } = "StartTls";
    public string? Username { get; set; }
    public string? Password { get; set; }
    /// <summary>True on GET when a password is already stored (the value itself is never returned).</summary>
    public bool HasPassword { get; set; }
}

public sealed class AwsDto
{
    public string Region { get; set; } = "us-east-1";
    public string AuthMode { get; set; } = "Profile";
    public string? Profile { get; set; }
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public bool HasSecret { get; set; }
    public string? SessionToken { get; set; }
    public string? ConfigurationSetName { get; set; }
}

public sealed class ConfigDto
{
    public double SendRatePerSecond { get; set; } = 5;
    public string Provider { get; set; } = "Smtp";
    public string FromName { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string? ReplyToEmail { get; set; }
    public SmtpDto Smtp { get; set; } = new();
    public AwsDto Aws { get; set; } = new();

    /// <summary>Projects a config to a DTO for the browser, with secrets redacted.</summary>
    public static ConfigDto FromConfig(EmailBlasterConfig c) => new()
    {
        SendRatePerSecond = c.SendRatePerSecond,
        Provider = c.Provider.ToString(),
        FromName = c.FromName,
        FromEmail = c.FromEmail,
        ReplyToEmail = c.ReplyToEmail,
        Smtp = new SmtpDto
        {
            Host = c.Smtp.Host,
            Port = c.Smtp.Port,
            Security = c.Smtp.Security.ToString(),
            Username = c.Smtp.Username,
            Password = "",                                   // never leave the server
            HasPassword = !string.IsNullOrEmpty(c.Smtp.Password)
        },
        Aws = new AwsDto
        {
            Region = c.Aws.Region,
            AuthMode = c.Aws.AuthMode.ToString(),
            Profile = c.Aws.Profile,
            AccessKeyId = c.Aws.AccessKeyId,
            SecretAccessKey = "",                            // never leave the server
            HasSecret = !string.IsNullOrEmpty(c.Aws.SecretAccessKey),
            SessionToken = "",
            ConfigurationSetName = c.Aws.ConfigurationSetName
        }
    };

    /// <summary>
    /// Applies this DTO onto an existing config. Blank secret fields keep the currently-stored value,
    /// so the redacted values returned by <see cref="FromConfig"/> never wipe real credentials.
    /// </summary>
    public void ApplyTo(EmailBlasterConfig c)
    {
        c.SendRatePerSecond = SendRatePerSecond;
        c.Provider = Enum.TryParse<SendProvider>(Provider, true, out var p) ? p : SendProvider.Smtp;
        c.FromName = FromName?.Trim() ?? "";
        c.FromEmail = FromEmail?.Trim() ?? "";
        c.ReplyToEmail = string.IsNullOrWhiteSpace(ReplyToEmail) ? null : ReplyToEmail!.Trim();

        c.Smtp.Host = Smtp.Host?.Trim() ?? "";
        c.Smtp.Port = Smtp.Port;
        c.Smtp.Security = Enum.TryParse<SmtpSecurity>(Smtp.Security, true, out var s) ? s : SmtpSecurity.StartTls;
        c.Smtp.Username = string.IsNullOrWhiteSpace(Smtp.Username) ? null : Smtp.Username!.Trim();
        if (!string.IsNullOrEmpty(Smtp.Password))
            c.Smtp.Password = Smtp.Password;

        c.Aws.Region = Aws.Region?.Trim() ?? "us-east-1";
        c.Aws.AuthMode = Enum.TryParse<AwsAuthMode>(Aws.AuthMode, true, out var a) ? a : AwsAuthMode.Profile;
        c.Aws.Profile = string.IsNullOrWhiteSpace(Aws.Profile) ? null : Aws.Profile!.Trim();
        c.Aws.AccessKeyId = string.IsNullOrWhiteSpace(Aws.AccessKeyId) ? null : Aws.AccessKeyId!.Trim();
        if (!string.IsNullOrEmpty(Aws.SecretAccessKey))
            c.Aws.SecretAccessKey = Aws.SecretAccessKey;
        if (!string.IsNullOrEmpty(Aws.SessionToken))
            c.Aws.SessionToken = Aws.SessionToken;
        c.Aws.ConfigurationSetName =
            string.IsNullOrWhiteSpace(Aws.ConfigurationSetName) ? null : Aws.ConfigurationSetName!.Trim();
    }
}

public sealed class TemplateDto
{
    public string Subject { get; set; } = "";
    public string Html { get; set; } = "";
    public string? Text { get; set; }
}

public sealed class PreviewRequest
{
    public int Index { get; set; }
}

public sealed class TestRequest
{
    public string To { get; set; } = "";
}
