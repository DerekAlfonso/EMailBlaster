using EmailBlaster.Core;
using EmailBlaster.Core.Configuration;
using Xunit;

namespace EmailBlaster.Tests;

public class ConfigurationLoaderTests
{
    private static EmailBlasterConfig SampleConfig() => new()
    {
        SendRatePerSecond = 2.5,
        Provider = SendProvider.Aws,
        FromName = "Sender",
        FromEmail = "sender@example.com",
        ReplyToEmail = "reply@example.com",
        Smtp =
        {
            Host = "smtp.example.com",
            Port = 465,
            Security = SmtpSecurity.SslOnConnect,
            Username = "user",
            Password = "hunter2"
        },
        Aws =
        {
            Region = "eu-west-1",
            AuthMode = AwsAuthMode.AccessKey,
            AccessKeyId = "AKIAEXAMPLE",
            SecretAccessKey = "secret",
            ConfigurationSetName = "tracking"
        }
    };

    [Fact]
    public void SerializeDeserialize_RoundTripsAllValues()
    {
        var restored = ConfigurationLoader.Deserialize(ConfigurationLoader.Serialize(SampleConfig()));

        Assert.Equal(2.5, restored.SendRatePerSecond);
        Assert.Equal(SendProvider.Aws, restored.Provider);
        Assert.Equal("Sender", restored.FromName);
        Assert.Equal("sender@example.com", restored.FromEmail);
        Assert.Equal("reply@example.com", restored.ReplyToEmail);
        Assert.Equal("smtp.example.com", restored.Smtp.Host);
        Assert.Equal(465, restored.Smtp.Port);
        Assert.Equal(SmtpSecurity.SslOnConnect, restored.Smtp.Security);
        Assert.Equal("user", restored.Smtp.Username);
        Assert.Equal("hunter2", restored.Smtp.Password);
        Assert.Equal("eu-west-1", restored.Aws.Region);
        Assert.Equal(AwsAuthMode.AccessKey, restored.Aws.AuthMode);
        Assert.Equal("AKIAEXAMPLE", restored.Aws.AccessKeyId);
        Assert.Equal("secret", restored.Aws.SecretAccessKey);
        Assert.Equal("tracking", restored.Aws.ConfigurationSetName);
    }

    [Fact]
    public void Serialize_WritesEnumsAsStrings()
    {
        var json = ConfigurationLoader.Serialize(SampleConfig());
        Assert.Contains("\"Aws\"", json);
        Assert.Contains("\"SslOnConnect\"", json);
    }

    [Fact]
    public void Serialize_OmitsComputedProperties()
    {
        var json = ConfigurationLoader.Serialize(SampleConfig());
        Assert.DoesNotContain("RequiresAuthentication", json);
        Assert.DoesNotContain("IsUnlimitedRate", json);
    }

    [Fact]
    public void Deserialize_IsCaseInsensitiveAndAllowsCommentsAndTrailingCommas()
    {
        const string json = """
            {
              // a comment
              "provider": "aws",
              "fromEmail": "x@y.zz",
            }
            """;
        var config = ConfigurationLoader.Deserialize(json);
        Assert.Equal(SendProvider.Aws, config.Provider);
        Assert.Equal("x@y.zz", config.FromEmail);
    }

    [Fact]
    public void Deserialize_InvalidJsonThrows()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ConfigurationLoader.Deserialize("not json"));
    }

    [Fact]
    public void SaveToFile_LoadFromFile_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emailblaster-test-{Guid.NewGuid():N}.json");
        try
        {
            ConfigurationLoader.SaveToFile(SampleConfig(), path);
            var restored = ConfigurationLoader.LoadFromFile(path);
            Assert.Equal("sender@example.com", restored.FromEmail);
            Assert.Equal(SendProvider.Aws, restored.Provider);
            Assert.Equal("hunter2", restored.Smtp.Password);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// Environment-variable tests are isolated in their own non-parallel collection because they mutate
/// process-wide state that <see cref="ConfigurationLoader"/> reads.
/// </summary>
[Collection("environment")]
public class ConfigurationLoaderEnvironmentTests : IDisposable
{
    private static readonly string[] Keys =
    {
        "EMAILBLASTER_PROVIDER", "EMAILBLASTER_FROM_EMAIL", "EMAILBLASTER_SEND_RATE_PER_SECOND",
        "EMAILBLASTER_SMTP_PORT", "EMAILBLASTER_AWS_REGION", "EMAILBLASTER_SMTP_SECURITY"
    };

    public void Dispose()
    {
        foreach (var key in Keys)
            Environment.SetEnvironmentVariable(key, null);
    }

    [Fact]
    public void LoadFromEnvironment_AppliesTypedOverrides()
    {
        Environment.SetEnvironmentVariable("EMAILBLASTER_PROVIDER", "aws");
        Environment.SetEnvironmentVariable("EMAILBLASTER_FROM_EMAIL", "env@example.com");
        Environment.SetEnvironmentVariable("EMAILBLASTER_SEND_RATE_PER_SECOND", "1.5");
        Environment.SetEnvironmentVariable("EMAILBLASTER_SMTP_PORT", "2525");
        Environment.SetEnvironmentVariable("EMAILBLASTER_AWS_REGION", "ap-southeast-2");
        Environment.SetEnvironmentVariable("EMAILBLASTER_SMTP_SECURITY", "SslOnConnect");

        var config = ConfigurationLoader.LoadFromEnvironment();

        Assert.Equal(SendProvider.Aws, config.Provider);
        Assert.Equal("env@example.com", config.FromEmail);
        Assert.Equal(1.5, config.SendRatePerSecond);
        Assert.Equal(2525, config.Smtp.Port);
        Assert.Equal("ap-southeast-2", config.Aws.Region);
        Assert.Equal(SmtpSecurity.SslOnConnect, config.Smtp.Security);
    }

    [Fact]
    public void LoadFromEnvironment_IgnoresMalformedValues()
    {
        Environment.SetEnvironmentVariable("EMAILBLASTER_SMTP_PORT", "not-a-number");
        Environment.SetEnvironmentVariable("EMAILBLASTER_PROVIDER", "carrier-pigeon");

        var config = ConfigurationLoader.LoadFromEnvironment();

        Assert.Equal(587, config.Smtp.Port);                  // default retained
        Assert.Equal(SendProvider.Smtp, config.Provider);     // default retained
    }
}
