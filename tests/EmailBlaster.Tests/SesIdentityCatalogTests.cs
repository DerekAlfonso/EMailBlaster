using System.Net;
using Amazon.Runtime;
using EmailBlaster.Core.Configuration;
using EmailBlaster.Core.Delivery;
using Xunit;

namespace EmailBlaster.Tests;

public class FromAddressValidationTests
{
    private static readonly string[] Emails = { "sales@example.com", "Support@Example.org" };
    private static readonly string[] Domains = { "verified.example", "Corp.Example.COM" };

    private static FromAddressVerdict Validate(string? address) =>
        SesIdentityCatalog.ValidateFromAddress(address, Emails, Domains);

    [Theory]
    [InlineData("sales@example.com")]
    [InlineData("SALES@EXAMPLE.COM")]
    [InlineData("support@example.org")]
    public void ExactEmailMatch_IsVerifiedEmail(string address) =>
        Assert.Equal(FromAddressVerdict.VerifiedEmail, Validate(address));

    [Theory]
    [InlineData("anyone@verified.example")]
    [InlineData("news@CORP.example.com")]
    public void DomainMatch_IsVerifiedDomain(string address) =>
        Assert.Equal(FromAddressVerdict.VerifiedDomain, Validate(address));

    [Fact]
    public void UnknownAddress_IsNotVerified() =>
        Assert.Equal(FromAddressVerdict.NotVerified, Validate("someone@elsewhere.net"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("dangling@")]
    [InlineData("@no-local-part.com")]
    public void BlankOrPartialAddress_IsIncomplete(string? address) =>
        Assert.Equal(FromAddressVerdict.Incomplete, Validate(address));

    [Fact]
    public void ExactEmailWinsOverDomain()
    {
        // sales@example.com matches the email list even though example.com is not a verified domain.
        Assert.Equal(FromAddressVerdict.VerifiedEmail, Validate("sales@example.com"));
    }
}

public class DefaultChainClassificationTests
{
    private static AwsConfig DefaultChainConfig() =>
        new() { Region = "us-east-1", AuthMode = AwsAuthMode.Profile, Profile = null };

    private static AwsConfig NamedProfileConfig() =>
        new() { Region = "us-east-1", AuthMode = AwsAuthMode.Profile, Profile = "dev" };

    [Fact]
    public void UsesDefaultChain_OnlyWhenProfileModeWithBlankProfile()
    {
        Assert.True(AwsAccessTester.UsesDefaultChain(DefaultChainConfig()));
        Assert.False(AwsAccessTester.UsesDefaultChain(NamedProfileConfig()));
        Assert.False(AwsAccessTester.UsesDefaultChain(
            new AwsConfig { AuthMode = AwsAuthMode.AccessKey }));
    }

    [Fact]
    public void AccessDenied_OnDefaultChain_NamesTheDefaultProfileAndOperation()
    {
        var ex = new AmazonServiceException("denied") { StatusCode = HttpStatusCode.Forbidden };
        var result = AwsAccessTester.Classify(ex, DefaultChainConfig(), "", isSsoProfile: false,
            operation: "ses:ListEmailIdentities");

        Assert.Equal(AwsAccessProblem.SendAccessDenied, result.Problem);
        Assert.Contains("The default profile does not have the ses:ListEmailIdentities permission",
            result.Message);
    }

    [Fact]
    public void AccessDenied_WithExplicitCredentials_NamesTheOperation()
    {
        var ex = new AmazonServiceException("denied") { StatusCode = HttpStatusCode.Forbidden };
        var result = AwsAccessTester.Classify(ex, NamedProfileConfig(), "", isSsoProfile: false,
            operation: "ses:ListEmailIdentities");

        Assert.Contains("not authorized to call ses:ListEmailIdentities", result.Message);
        Assert.DoesNotContain("default profile", result.Message);
    }

    [Theory]
    [InlineData("Unable to get IAM security credentials from EC2 Instance Metadata Service.")]
    [InlineData("Failed to retrieve credentials from the configured sources.")]
    [InlineData("Unable to find credentials for the request.")]
    public void CredentialResolutionFailure_NoDefaultProfile_MapsToNoCredentials(string message)
    {
        var result = AwsAccessTester.Classify(new AmazonServiceException(message), DefaultChainConfig(),
            "", isSsoProfile: false, defaultProfileExists: false);

        Assert.Equal(AwsAccessProblem.NoCredentials, result.Problem);
        Assert.Contains("No AWS credentials or profile supplied and no default profile exists",
            result.Message);
    }

    [Fact]
    public void CredentialRejection_NoDefaultProfile_MapsToNoCredentials()
    {
        // With nothing to sign the request, AWS rejects it (e.g. UnrecognizedClientException);
        // when no default profile exists that still means "no credentials anywhere".
        var ex = new AmazonServiceException("unknown client") { ErrorCode = "UnrecognizedClientException" };
        var result = AwsAccessTester.Classify(ex, DefaultChainConfig(), "", isSsoProfile: false,
            defaultProfileExists: false);

        Assert.Equal(AwsAccessProblem.NoCredentials, result.Problem);
        Assert.Contains("No AWS credentials or profile supplied and no default profile exists",
            result.Message);
    }

    [Fact]
    public void CredentialRejection_DefaultProfileExists_ReportsBrokenDefaultProfile()
    {
        var ex = new AmazonServiceException("rejected") { ErrorCode = "InvalidClientTokenId" };
        var result = AwsAccessTester.Classify(ex, DefaultChainConfig(), "", isSsoProfile: false,
            defaultProfileExists: true);

        Assert.Equal(AwsAccessProblem.InvalidCredentials, result.Problem);
        Assert.Contains("default profile was used", result.Message);
        Assert.Contains("did not produce working credentials", result.Message);
    }

    [Fact]
    public void CredentialResolutionFailure_WithNamedProfile_IsNotNoCredentials()
    {
        var result = AwsAccessTester.Classify(
            new AmazonServiceException("Unable to find credentials for the request."),
            NamedProfileConfig(), "", isSsoProfile: false, defaultProfileExists: false);

        Assert.NotEqual(AwsAccessProblem.NoCredentials, result.Problem);
    }
}
