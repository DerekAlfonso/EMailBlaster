using Amazon.Runtime.CredentialManagement;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using EmailBlaster.Core.Configuration;

namespace EmailBlaster.Core.Delivery;

/// <summary>How a From address relates to the account's verified SES identities.</summary>
public enum FromAddressVerdict
{
    /// <summary>The address is blank or has no domain part yet.</summary>
    Incomplete,

    /// <summary>The exact email address is a verified SES identity.</summary>
    VerifiedEmail,

    /// <summary>The address's domain is a verified SES identity.</summary>
    VerifiedDomain,

    /// <summary>Neither the address nor its domain is verified; SES would reject sends.</summary>
    NotVerified
}

/// <summary>Outcome of listing the verified SES identities for the configured credentials.</summary>
public sealed class SesIdentityResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> EmailIdentities { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DomainIdentities { get; init; } = Array.Empty<string>();

    /// <summary>User-friendly explanation when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }
    public AwsAccessProblem Problem { get; init; }
}

/// <summary>
/// Lists the verified SES identities (email addresses and domains) visible to the configured AWS
/// credentials, so the apps can offer them for the From address and validate what the user typed.
/// </summary>
public static class SesIdentityCatalog
{
    public static async Task<SesIdentityResult> ListVerifiedIdentitiesAsync(
        AwsConfig aws, CancellationToken cancellationToken = default)
    {
        var isSso = false;
        if (aws.AuthMode == AwsAuthMode.Profile && !string.IsNullOrWhiteSpace(aws.Profile))
        {
            var chain = new CredentialProfileStoreChain();
            if (!chain.TryGetProfile(aws.Profile, out var profile))
            {
                return new SesIdentityResult
                {
                    Success = false,
                    Problem = AwsAccessProblem.ProfileNotFound,
                    Error = $"AWS profile '{aws.Profile}' was not found in the shared credentials store."
                };
            }
            isSso = !string.IsNullOrEmpty(profile.Options.SsoStartUrl)
                    || !string.IsNullOrEmpty(profile.Options.SsoSession)
                    || !string.IsNullOrEmpty(profile.Options.SsoAccountId);
        }

        try
        {
            using var client = SesClientFactory.Create(aws);
            var emails = new List<string>();
            var domains = new List<string>();

            string? nextToken = null;
            do
            {
                var response = await client.ListEmailIdentitiesAsync(
                    new ListEmailIdentitiesRequest { NextToken = nextToken, PageSize = 100 },
                    cancellationToken).ConfigureAwait(false);

                foreach (var identity in response.EmailIdentities)
                {
                    // Only verified identities are usable as a From address.
                    if (identity.VerificationStatus != VerificationStatus.SUCCESS)
                        continue;

                    if (identity.IdentityType == IdentityType.EMAIL_ADDRESS)
                        emails.Add(identity.IdentityName);
                    else
                        domains.Add(identity.IdentityName);
                }

                nextToken = response.NextToken;
            } while (!string.IsNullOrEmpty(nextToken));

            emails.Sort(StringComparer.OrdinalIgnoreCase);
            domains.Sort(StringComparer.OrdinalIgnoreCase);

            return new SesIdentityResult
            {
                Success = true,
                EmailIdentities = emails,
                DomainIdentities = domains
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var classified = AwsAccessTester.Classify(ex, aws, fromEmail: "", isSso,
                operation: "ses:ListEmailIdentities");
            return new SesIdentityResult
            {
                Success = false,
                Problem = classified.Problem,
                Error = classified.Message
            };
        }
    }

    /// <summary>
    /// Checks a From address against the verified identities. The exact-email match wins over the
    /// domain match, mirroring how SES authorizes the address.
    /// </summary>
    public static FromAddressVerdict ValidateFromAddress(
        string? fromEmail, IEnumerable<string> verifiedEmails, IEnumerable<string> verifiedDomains)
    {
        var address = fromEmail?.Trim();
        if (string.IsNullOrEmpty(address))
            return FromAddressVerdict.Incomplete;

        if (verifiedEmails.Contains(address, StringComparer.OrdinalIgnoreCase))
            return FromAddressVerdict.VerifiedEmail;

        var at = address.LastIndexOf('@');
        if (at <= 0 || at == address.Length - 1)
            return FromAddressVerdict.Incomplete;

        var domain = address[(at + 1)..];
        return verifiedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase)
            ? FromAddressVerdict.VerifiedDomain
            : FromAddressVerdict.NotVerified;
    }
}
