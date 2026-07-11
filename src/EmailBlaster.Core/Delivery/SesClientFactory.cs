using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.SimpleEmailV2;
using EmailBlaster.Core.Configuration;

namespace EmailBlaster.Core.Delivery;

/// <summary>
/// Raised when an SSO profile has no valid cached token and would need an interactive sign-in.
/// The apps catch this (via <see cref="AwsAccessTester"/>) and offer to run <c>aws sso login</c>
/// rather than letting the SDK silently start its own device-authorization flow.
/// </summary>
public sealed class SsoSignInRequiredException : Exception
{
    public SsoSignInRequiredException()
        : base("The SSO sign-in session is missing or expired; an interactive sign-in is required.") { }
}

/// <summary>
/// Builds SES v2 clients from <see cref="AwsConfig"/>. Shared by <see cref="SesEmailSender"/> and
/// <see cref="AwsAccessTester"/> so both resolve credentials identically.
/// </summary>
public static class SesClientFactory
{
    public static AmazonSimpleEmailServiceV2Client Create(AwsConfig aws)
    {
        var region = RegionEndpoint.GetBySystemName(aws.Region);

        if (aws.AuthMode == AwsAuthMode.AccessKey)
        {
            AWSCredentials creds = string.IsNullOrWhiteSpace(aws.SessionToken)
                ? new BasicAWSCredentials(aws.AccessKeyId, aws.SecretAccessKey)
                : new SessionAWSCredentials(aws.AccessKeyId, aws.SecretAccessKey, aws.SessionToken);
            return new AmazonSimpleEmailServiceV2Client(creds, region);
        }

        // Profile mode. A named profile is looked up in the shared credentials store; otherwise fall
        // back to the default provider chain (environment vars, EC2/ECS/Lambda roles, etc.).
        if (!string.IsNullOrWhiteSpace(aws.Profile))
        {
            var chain = new CredentialProfileStoreChain();
            if (chain.TryGetAWSCredentials(aws.Profile, out var profileCreds))
            {
                if (profileCreds is SSOAWSCredentials sso)
                {
                    // The SDK requires both options before it will resolve SSO credentials, even from
                    // a valid cached token. The callback only fires when a NEW interactive sign-in
                    // would be needed; failing fast there lets the apps offer 'aws sso login' instead.
                    sso.Options.ClientName = "EmailBlaster";
                    sso.Options.SsoVerificationCallback = _ => throw new SsoSignInRequiredException();
                }
                return new AmazonSimpleEmailServiceV2Client(profileCreds, region);
            }

            throw new InvalidOperationException(
                $"AWS profile '{aws.Profile}' was not found in the shared credentials store.");
        }

        return new AmazonSimpleEmailServiceV2Client(region);
    }
}
