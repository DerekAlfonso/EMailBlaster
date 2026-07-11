namespace EmailBlaster.Core.Configuration;

/// <summary>
/// Selects how AWS credentials are resolved when <see cref="SendProvider.Aws"/> is used.
/// </summary>
public enum AwsAuthMode
{
    /// <summary>Use a named profile from the shared AWS credentials/config files.</summary>
    Profile = 0,

    /// <summary>Use an explicit access key id and secret access key.</summary>
    AccessKey = 1
}
