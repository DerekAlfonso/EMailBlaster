using Amazon.Runtime.CredentialManagement;

namespace EmailBlaster.Core.Configuration;

/// <summary>
/// Enumerates the AWS profile names available on the machine running the app: the shared
/// credentials/config files (~/.aws) plus, on Windows, the encrypted SDK credential store.
/// Interactive front-ends use this to offer auto-completion for <see cref="AwsConfig.Profile"/>.
/// </summary>
public static class AwsProfileCatalog
{
    /// <summary>
    /// Returns the discovered profile names sorted alphabetically. Discovery is best-effort:
    /// any failure (missing files, malformed config, locked-down environment) yields an empty list
    /// rather than an exception, since auto-completion must never break the UI.
    /// </summary>
    public static IReadOnlyList<string> ListProfileNames()
    {
        try
        {
            return new CredentialProfileStoreChain()
                .ListProfiles()
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
