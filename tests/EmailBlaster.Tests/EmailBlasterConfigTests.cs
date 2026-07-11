using EmailBlaster.Core.Configuration;
using Xunit;

namespace EmailBlaster.Tests;

public class EmailBlasterConfigTests
{
    private static EmailBlasterConfig ValidSmtpConfig() => new()
    {
        FromEmail = "sender@example.com",
        Provider = SendProvider.Smtp,
        Smtp = { Host = "smtp.example.com", Port = 587 }
    };

    [Fact]
    public void Validate_ValidSmtpConfig_HasNoErrors()
    {
        Assert.Empty(ValidSmtpConfig().Validate());
    }

    [Fact]
    public void Validate_MissingFromEmail_Fails()
    {
        var config = ValidSmtpConfig();
        config.FromEmail = "";
        Assert.Contains(config.Validate(), e => e.Contains("From email is required"));
    }

    [Theory]
    [InlineData("plainaddress")]
    [InlineData("@nolocal.com")]
    [InlineData("nodomain@")]
    [InlineData("nodot@domain")]
    public void Validate_MalformedFromEmail_Fails(string email)
    {
        var config = ValidSmtpConfig();
        config.FromEmail = email;
        Assert.Contains(config.Validate(), e => e.Contains("does not look like a valid address"));
    }

    [Fact]
    public void Validate_MalformedReplyTo_Fails()
    {
        var config = ValidSmtpConfig();
        config.ReplyToEmail = "not-an-email";
        Assert.Contains(config.Validate(), e => e.Contains("Reply-To"));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.49)]
    public void Validate_RateBelowMinimum_Fails(double rate)
    {
        var config = ValidSmtpConfig();
        config.SendRatePerSecond = rate;
        Assert.Contains(config.Validate(), e => e.Contains("Send rate"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ZeroOrNegativeRateMeansUnlimited_Passes(double rate)
    {
        var config = ValidSmtpConfig();
        config.SendRatePerSecond = rate;
        Assert.True(config.IsUnlimitedRate);
        Assert.Empty(config.Validate());
    }

    [Fact]
    public void Validate_SmtpMissingHost_Fails()
    {
        var config = ValidSmtpConfig();
        config.Smtp.Host = "";
        Assert.Contains(config.Validate(), e => e.Contains("SMTP host"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void Validate_SmtpPortOutOfRange_Fails(int port)
    {
        var config = ValidSmtpConfig();
        config.Smtp.Port = port;
        Assert.Contains(config.Validate(), e => e.Contains("SMTP port"));
    }

    [Fact]
    public void Validate_AwsAccessKeyMode_RequiresKeyAndSecret()
    {
        var config = new EmailBlasterConfig
        {
            FromEmail = "sender@example.com",
            Provider = SendProvider.Aws,
            Aws = { Region = "us-east-1", AuthMode = AwsAuthMode.AccessKey }
        };

        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("access key id"));
        Assert.Contains(errors, e => e.Contains("secret access key"));
    }

    [Fact]
    public void Validate_AwsProfileMode_NeedsNoCredentials()
    {
        var config = new EmailBlasterConfig
        {
            FromEmail = "sender@example.com",
            Provider = SendProvider.Aws,
            Aws = { Region = "us-east-1", AuthMode = AwsAuthMode.Profile }
        };
        Assert.Empty(config.Validate());
    }

    [Fact]
    public void Validate_AwsMissingRegion_Fails()
    {
        var config = new EmailBlasterConfig
        {
            FromEmail = "sender@example.com",
            Provider = SendProvider.Aws,
            Aws = { Region = "" }
        };
        Assert.Contains(config.Validate(), e => e.Contains("AWS region"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("user", true)]
    public void SmtpConfig_RequiresAuthentication_TracksUsername(string? username, bool expected)
    {
        var smtp = new SmtpConfig { Username = username };
        Assert.Equal(expected, smtp.RequiresAuthentication);
    }
}
