using EmailBlaster.Core.Configuration;
using EmailBlaster.Web;
using Xunit;

namespace EmailBlaster.Tests;

public class WebConfigDtoTests
{
    private static EmailBlasterConfig StoredConfig() => new()
    {
        Provider = SendProvider.Smtp,
        FromEmail = "stored@example.com",
        Smtp = { Host = "smtp.example.com", Username = "user", Password = "stored-password" },
        Aws = { SecretAccessKey = "stored-secret", SessionToken = "stored-token" }
    };

    [Fact]
    public void FromConfig_NeverReturnsSecrets()
    {
        var dto = ConfigDto.FromConfig(StoredConfig());

        Assert.Equal("", dto.Smtp.Password);
        Assert.Equal("", dto.Aws.SecretAccessKey);
        Assert.Equal("", dto.Aws.SessionToken);
        Assert.True(dto.Smtp.HasPassword);
        Assert.True(dto.Aws.HasSecret);
    }

    [Fact]
    public void FromConfig_ReportsMissingSecrets()
    {
        var dto = ConfigDto.FromConfig(new EmailBlasterConfig());
        Assert.False(dto.Smtp.HasPassword);
        Assert.False(dto.Aws.HasSecret);
    }

    [Fact]
    public void ApplyTo_BlankSecrets_KeepStoredValues()
    {
        var config = StoredConfig();
        var dto = ConfigDto.FromConfig(config);   // secrets come back blank, mimicking the browser

        dto.ApplyTo(config);

        Assert.Equal("stored-password", config.Smtp.Password);
        Assert.Equal("stored-secret", config.Aws.SecretAccessKey);
        Assert.Equal("stored-token", config.Aws.SessionToken);
    }

    [Fact]
    public void ApplyTo_NewSecrets_ReplaceStoredValues()
    {
        var config = StoredConfig();
        var dto = ConfigDto.FromConfig(config);
        dto.Smtp.Password = "new-password";
        dto.Aws.SecretAccessKey = "new-secret";

        dto.ApplyTo(config);

        Assert.Equal("new-password", config.Smtp.Password);
        Assert.Equal("new-secret", config.Aws.SecretAccessKey);
    }

    [Fact]
    public void ApplyTo_ParsesEnumsCaseInsensitively()
    {
        var config = new EmailBlasterConfig();
        var dto = ConfigDto.FromConfig(config);
        dto.Provider = "aws";
        dto.Smtp.Security = "sslonconnect";
        dto.Aws.AuthMode = "accesskey";

        dto.ApplyTo(config);

        Assert.Equal(SendProvider.Aws, config.Provider);
        Assert.Equal(SmtpSecurity.SslOnConnect, config.Smtp.Security);
        Assert.Equal(AwsAuthMode.AccessKey, config.Aws.AuthMode);
    }

    [Fact]
    public void ApplyTo_UnknownEnumValues_FallBackToDefaults()
    {
        var config = new EmailBlasterConfig();
        var dto = ConfigDto.FromConfig(config);
        dto.Provider = "carrier-pigeon";
        dto.Smtp.Security = "telepathy";

        dto.ApplyTo(config);

        Assert.Equal(SendProvider.Smtp, config.Provider);
        Assert.Equal(SmtpSecurity.StartTls, config.Smtp.Security);
    }

    [Fact]
    public void ApplyTo_TrimsAndNullsBlankOptionals()
    {
        var config = new EmailBlasterConfig();
        var dto = ConfigDto.FromConfig(config);
        dto.FromEmail = "  padded@example.com  ";
        dto.ReplyToEmail = "   ";
        dto.Aws.Profile = "  dev  ";

        dto.ApplyTo(config);

        Assert.Equal("padded@example.com", config.FromEmail);
        Assert.Null(config.ReplyToEmail);
        Assert.Equal("dev", config.Aws.Profile);
    }
}
