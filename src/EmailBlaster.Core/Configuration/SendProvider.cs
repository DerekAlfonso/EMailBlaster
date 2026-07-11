namespace EmailBlaster.Core.Configuration;

/// <summary>
/// Selects the delivery transport used to send messages.
/// </summary>
public enum SendProvider
{
    /// <summary>Send via a standard SMTP server.</summary>
    Smtp = 0,

    /// <summary>Send via the AWS Simple Email Service (SES) API.</summary>
    Aws = 1
}
