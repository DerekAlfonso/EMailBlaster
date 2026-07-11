using System.Text.Json.Serialization;

namespace EmailBlaster.Core.Configuration;

/// <summary>
/// Root configuration for the email engine. Populated from a JSON file living next to the
/// application, or from environment variables (see <see cref="EmailBlaster.Core.ConfigurationLoader"/>).
/// </summary>
public sealed class EmailBlasterConfig
{
    /// <summary>
    /// Target send rate in messages per second. The valid range is 0.5 through unlimited.
    /// A value of 0 (or negative) means unlimited: send as fast as the transport allows.
    /// </summary>
    public double SendRatePerSecond { get; set; } = 5.0;

    /// <summary>Which transport to send through.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SendProvider Provider { get; set; } = SendProvider.Smtp;

    /// <summary>Display name that appears in the recipient's From header.</summary>
    public string FromName { get; set; } = string.Empty;

    /// <summary>From address. Must be a verified identity when sending via SES.</summary>
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>Optional Reply-To address. When blank, replies go to <see cref="FromEmail"/>.</summary>
    public string? ReplyToEmail { get; set; }

    /// <summary>SMTP settings (used when <see cref="Provider"/> is <see cref="SendProvider.Smtp"/>).</summary>
    public SmtpConfig Smtp { get; set; } = new();

    /// <summary>AWS SES settings (used when <see cref="Provider"/> is <see cref="SendProvider.Aws"/>).</summary>
    public AwsConfig Aws { get; set; } = new();

    /// <summary>True when the rate is uncapped.</summary>
    [JsonIgnore]
    public bool IsUnlimitedRate => SendRatePerSecond <= 0;

    /// <summary>Lowest permitted positive send rate (messages/second).</summary>
    public const double MinSendRatePerSecond = 0.5;

    /// <summary>
    /// Validates the configuration and returns a list of human-readable problems.
    /// An empty list means the configuration is usable.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!IsUnlimitedRate && SendRatePerSecond < MinSendRatePerSecond)
            errors.Add($"Send rate must be 0 (unlimited) or at least {MinSendRatePerSecond} messages/second.");

        if (string.IsNullOrWhiteSpace(FromEmail))
            errors.Add("From email is required.");
        else if (!IsProbablyEmail(FromEmail))
            errors.Add($"From email '{FromEmail}' does not look like a valid address.");

        if (!string.IsNullOrWhiteSpace(ReplyToEmail) && !IsProbablyEmail(ReplyToEmail!))
            errors.Add($"Reply-To email '{ReplyToEmail}' does not look like a valid address.");

        switch (Provider)
        {
            case SendProvider.Smtp:
                if (string.IsNullOrWhiteSpace(Smtp.Host))
                    errors.Add("SMTP host is required when using the SMTP provider.");
                if (Smtp.Port is <= 0 or > 65535)
                    errors.Add("SMTP port must be between 1 and 65535.");
                break;

            case SendProvider.Aws:
                if (string.IsNullOrWhiteSpace(Aws.Region))
                    errors.Add("AWS region is required when using the AWS provider.");
                if (Aws.AuthMode == AwsAuthMode.AccessKey)
                {
                    if (string.IsNullOrWhiteSpace(Aws.AccessKeyId))
                        errors.Add("AWS access key id is required when auth mode is AccessKey.");
                    if (string.IsNullOrWhiteSpace(Aws.SecretAccessKey))
                        errors.Add("AWS secret access key is required when auth mode is AccessKey.");
                }
                break;
        }

        return errors;
    }

    private static bool IsProbablyEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at < value.Length - 1 && value.IndexOf('.', at) > at;
    }
}
