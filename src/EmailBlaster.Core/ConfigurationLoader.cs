using System.Text.Json;
using System.Text.Json.Serialization;
using EmailBlaster.Core.Configuration;

namespace EmailBlaster.Core;

/// <summary>
/// Loads <see cref="EmailBlasterConfig"/> from a JSON file located beside the application or from
/// environment variables. The file is preferred when present; environment variables are the fallback
/// (and the recommended source when the engine is hosted as a library inside AWS Lambda).
/// </summary>
public static class ConfigurationLoader
{
    /// <summary>Default JSON file name searched for next to the executing assembly.</summary>
    public const string DefaultFileName = "emailblaster.json";

    /// <summary>Prefix for every environment variable read by <see cref="LoadFromEnvironment"/>.</summary>
    public const string EnvPrefix = "EMAILBLASTER_";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Loads configuration using the standard resolution order:
    /// <list type="number">
    ///   <item>An explicit <paramref name="explicitPath"/> if supplied.</item>
    ///   <item><c>emailblaster.json</c> in the application's base directory.</item>
    ///   <item>Environment variables prefixed with <c>EMAILBLASTER_</c>.</item>
    /// </list>
    /// Values from environment variables always override values already loaded from a file, so a file can
    /// hold defaults while secrets are injected via the environment.
    /// </summary>
    public static EmailBlasterConfig Load(string? explicitPath = null)
    {
        EmailBlasterConfig config;

        var path = ResolveFilePath(explicitPath);
        if (path is not null && File.Exists(path))
        {
            config = LoadFromFile(path);
        }
        else
        {
            config = new EmailBlasterConfig();
        }

        ApplyEnvironmentOverrides(config);
        return config;
    }

    /// <summary>Returns the JSON file path that <see cref="Load"/> would use, or null if none found.</summary>
    public static string? ResolveFilePath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath;

        var envPath = Environment.GetEnvironmentVariable(EnvPrefix + "CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
            return envPath;

        var beside = Path.Combine(AppContext.BaseDirectory, DefaultFileName);
        if (File.Exists(beside))
            return beside;

        var cwd = Path.Combine(Directory.GetCurrentDirectory(), DefaultFileName);
        return File.Exists(cwd) ? cwd : beside;
    }

    /// <summary>Deserializes a configuration from a JSON file.</summary>
    public static EmailBlasterConfig LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<EmailBlasterConfig>(json, JsonOptions)
               ?? new EmailBlasterConfig();
    }

    /// <summary>Serializes a configuration to a JSON file (used by the desktop settings screen).</summary>
    public static void SaveToFile(EmailBlasterConfig config, string path)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>Builds a configuration purely from environment variables.</summary>
    public static EmailBlasterConfig LoadFromEnvironment()
    {
        var config = new EmailBlasterConfig();
        ApplyEnvironmentOverrides(config);
        return config;
    }

    /// <summary>Serializes to a JSON string (handy for diagnostics / previewing what would be written).</summary>
    public static string Serialize(EmailBlasterConfig config) =>
        JsonSerializer.Serialize(config, JsonOptions);

    private static void ApplyEnvironmentOverrides(EmailBlasterConfig config)
    {
        // Top-level settings.
        SetDouble(EnvPrefix + "SEND_RATE_PER_SECOND", v => config.SendRatePerSecond = v);
        SetEnum<SendProvider>(EnvPrefix + "PROVIDER", v => config.Provider = v);
        SetString(EnvPrefix + "FROM_NAME", v => config.FromName = v);
        SetString(EnvPrefix + "FROM_EMAIL", v => config.FromEmail = v);
        SetString(EnvPrefix + "REPLY_TO_EMAIL", v => config.ReplyToEmail = v);

        // SMTP.
        SetString(EnvPrefix + "SMTP_HOST", v => config.Smtp.Host = v);
        SetInt(EnvPrefix + "SMTP_PORT", v => config.Smtp.Port = v);
        SetEnum<SmtpSecurity>(EnvPrefix + "SMTP_SECURITY", v => config.Smtp.Security = v);
        SetString(EnvPrefix + "SMTP_USERNAME", v => config.Smtp.Username = v);
        SetString(EnvPrefix + "SMTP_PASSWORD", v => config.Smtp.Password = v);

        // AWS.
        SetString(EnvPrefix + "AWS_REGION", v => config.Aws.Region = v);
        SetEnum<AwsAuthMode>(EnvPrefix + "AWS_AUTH_MODE", v => config.Aws.AuthMode = v);
        SetString(EnvPrefix + "AWS_PROFILE", v => config.Aws.Profile = v);
        SetString(EnvPrefix + "AWS_ACCESS_KEY_ID", v => config.Aws.AccessKeyId = v);
        SetString(EnvPrefix + "AWS_SECRET_ACCESS_KEY", v => config.Aws.SecretAccessKey = v);
        SetString(EnvPrefix + "AWS_SESSION_TOKEN", v => config.Aws.SessionToken = v);
        SetString(EnvPrefix + "AWS_CONFIGURATION_SET", v => config.Aws.ConfigurationSetName = v);
    }

    private static void SetString(string key, Action<string> apply)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(value))
            apply(value);
    }

    private static void SetInt(string key, Action<int> apply)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(value) && int.TryParse(value, out var parsed))
            apply(parsed);
    }

    private static void SetDouble(string key, Action<double> apply)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(value) &&
            double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            apply(parsed);
    }

    private static void SetEnum<TEnum>(string key, Action<TEnum> apply) where TEnum : struct, Enum
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
            apply(parsed);
    }
}
