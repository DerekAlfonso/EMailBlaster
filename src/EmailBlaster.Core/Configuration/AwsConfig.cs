namespace EmailBlaster.Core.Configuration;

/// <summary>
/// AWS SES transport settings. Only used when <see cref="EmailBlasterConfig.Provider"/> is <see cref="SendProvider.Aws"/>.
/// </summary>
public sealed class AwsConfig
{
    /// <summary>AWS region system name, e.g. <c>us-east-1</c>. Required.</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Determines whether credentials come from a named profile or an explicit access key pair.
    /// </summary>
    public AwsAuthMode AuthMode { get; set; } = AwsAuthMode.Profile;

    /// <summary>
    /// Named profile from the shared AWS credentials file. Used when <see cref="AuthMode"/> is
    /// <see cref="AwsAuthMode.Profile"/>. When null/blank the default credential provider chain is used
    /// (environment, ECS/EC2 roles, etc.), which is the recommended setup inside AWS Lambda.
    /// </summary>
    public string? Profile { get; set; }

    /// <summary>Access key id. Used when <see cref="AuthMode"/> is <see cref="AwsAuthMode.AccessKey"/>.</summary>
    public string? AccessKeyId { get; set; }

    /// <summary>Secret access key. Used when <see cref="AuthMode"/> is <see cref="AwsAuthMode.AccessKey"/>.</summary>
    public string? SecretAccessKey { get; set; }

    /// <summary>Optional session token for temporary (STS) credentials.</summary>
    public string? SessionToken { get; set; }

    /// <summary>Optional SES configuration set name to associate with sent messages.</summary>
    public string? ConfigurationSetName { get; set; }
}
