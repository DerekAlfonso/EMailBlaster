using System.Net;
using Amazon.Runtime;
using Amazon.SimpleEmailV2.Model;
using EmailBlaster.Core.Configuration;
using EmailBlaster.Core.Delivery;
using Xunit;

namespace EmailBlaster.Tests;

public class AwsAccessTesterTests
{
    private static AwsConfig ProfileConfig(string profile = "some-profile") =>
        new() { Region = "us-east-1", AuthMode = AwsAuthMode.Profile, Profile = profile };

    private static AwsConfig KeyConfig() =>
        new() { Region = "us-east-1", AuthMode = AwsAuthMode.AccessKey, AccessKeyId = "AKIA", SecretAccessKey = "s" };

    private static AwsAccessTestResult Classify(Exception ex, AwsConfig aws, bool isSso = false) =>
        AwsAccessTester.Classify(ex, aws, "from@example.com", isSso);

    [Fact]
    public void UnverifiedIdentity_MapsToIdentityNotVerified()
    {
        var ex = new MessageRejectedException(
            "Email address is not verified. The following identities failed the check in region US-EAST-1: from@example.com");
        var result = Classify(ex, KeyConfig());

        Assert.Equal(AwsAccessProblem.IdentityNotVerified, result.Problem);
        Assert.Contains("from@example.com", result.Message);
        Assert.Contains("ses:SendEmail permission are working", result.Message);
    }

    [Fact]
    public void OtherMessageRejection_StillReportsApiIsCallable()
    {
        var result = Classify(new MessageRejectedException("Something else."), KeyConfig());
        Assert.Equal(AwsAccessProblem.Unknown, result.Problem);
        Assert.Contains("callable", result.Message);
    }

    [Theory]
    [InlineData("UnrecognizedClientException")]
    [InlineData("InvalidClientTokenId")]
    [InlineData("InvalidAccessKeyId")]
    [InlineData("SignatureDoesNotMatch")]
    public void CredentialErrorCodes_MapToInvalidCredentials(string errorCode)
    {
        var ex = new AmazonServiceException("rejected") { ErrorCode = errorCode };
        var result = Classify(ex, KeyConfig());

        Assert.Equal(AwsAccessProblem.InvalidCredentials, result.Problem);
        Assert.Contains("access key", result.Message);
    }

    [Fact]
    public void InvalidCredentials_ProfileMode_MentionsTheProfile()
    {
        var ex = new AmazonServiceException("rejected") { ErrorCode = "InvalidClientTokenId" };
        var result = Classify(ex, ProfileConfig("dev"));
        Assert.Equal(AwsAccessProblem.InvalidCredentials, result.Problem);
        Assert.Contains("profile 'dev'", result.Message);
    }

    [Fact]
    public void ExpiredToken_MapsToExpiredCredentials()
    {
        var ex = new AmazonServiceException("expired") { ErrorCode = "ExpiredToken" };
        var result = Classify(ex, KeyConfig());
        Assert.Equal(AwsAccessProblem.ExpiredCredentials, result.Problem);
    }

    [Fact]
    public void ExpiredToken_OnSsoProfile_MapsToSsoLoginRequired()
    {
        var ex = new AmazonServiceException("expired") { ErrorCode = "ExpiredToken" };
        var result = Classify(ex, ProfileConfig("corp-sso"), isSso: true);

        Assert.Equal(AwsAccessProblem.SsoLoginRequired, result.Problem);
        Assert.True(result.CanAttemptSsoLogin);
        Assert.Contains("aws sso login --profile corp-sso", result.Message);
    }

    [Fact]
    public void Forbidden_MapsToSendAccessDenied()
    {
        var ex = new AmazonServiceException("denied")
        {
            ErrorCode = "AccessDeniedException",
            StatusCode = HttpStatusCode.Forbidden
        };
        var result = Classify(ex, KeyConfig());

        Assert.Equal(AwsAccessProblem.SendAccessDenied, result.Problem);
        Assert.Contains("ses:SendEmail", result.Message);
        Assert.Contains("administrator", result.Message);
    }

    [Fact]
    public void SsoSignInRequired_MapsToSsoLoginRequired_EvenWhenWrapped()
    {
        var direct = Classify(new SsoSignInRequiredException(), ProfileConfig("corp-sso"));
        Assert.Equal(AwsAccessProblem.SsoLoginRequired, direct.Problem);
        Assert.Contains("aws sso login --profile corp-sso", direct.Message);

        var wrapped = Classify(
            new AmazonClientException("credential generation failed", new SsoSignInRequiredException()),
            ProfileConfig("corp-sso"));
        Assert.Equal(AwsAccessProblem.SsoLoginRequired, wrapped.Problem);
        Assert.True(wrapped.CanAttemptSsoLogin);
    }

    [Fact]
    public void ClientException_OnSsoProfile_MapsToSsoLoginRequired()
    {
        var ex = new AmazonClientException("Error loading SSO Token: Token file not found");
        var result = Classify(ex, ProfileConfig("corp-sso"), isSso: true);

        Assert.Equal(AwsAccessProblem.SsoLoginRequired, result.Problem);
        Assert.True(result.CanAttemptSsoLogin);
    }

    [Fact]
    public void ClientExceptionMentioningSso_WithoutSsoProfileFlag_StillMapsToSsoLoginRequired()
    {
        var ex = new AmazonClientException("The SSO session associated with this profile has expired.");
        var result = Classify(ex, ProfileConfig(), isSso: false);
        Assert.Equal(AwsAccessProblem.SsoLoginRequired, result.Problem);
    }

    [Fact]
    public void HttpFailure_MapsToNetwork()
    {
        var result = Classify(new HttpRequestException("no route"), KeyConfig());
        Assert.Equal(AwsAccessProblem.Network, result.Problem);
        Assert.Contains("us-east-1", result.Message);
    }

    [Fact]
    public void UnrecognizedException_MapsToUnknownWithOriginalMessage()
    {
        var result = Classify(new InvalidOperationException("weird"), KeyConfig());
        Assert.Equal(AwsAccessProblem.Unknown, result.Problem);
        Assert.Contains("weird", result.Message);
    }

    [Fact]
    public async Task MissingProfile_ReportsProfileNotFound()
    {
        var config = new EmailBlasterConfig
        {
            FromEmail = "from@example.com",
            Provider = SendProvider.Aws,
            Aws = ProfileConfig("emailblaster-test-profile-that-does-not-exist")
        };

        var result = await AwsAccessTester.TestSendAccessAsync(config);

        Assert.False(result.Success);
        Assert.Equal(AwsAccessProblem.ProfileNotFound, result.Problem);
        Assert.Contains("emailblaster-test-profile-that-does-not-exist", result.Message);
    }

    [Fact]
    public void SuccessResult_HasNoProblemAndNoSsoOffer()
    {
        var result = AwsAccessTestResult.Ok("all good");
        Assert.True(result.Success);
        Assert.Equal(AwsAccessProblem.None, result.Problem);
        Assert.False(result.CanAttemptSsoLogin);
    }
}
