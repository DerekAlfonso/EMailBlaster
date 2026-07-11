using EmailBlaster.Core;
using EmailBlaster.Core.Configuration;
using EmailBlaster.Core.Delivery;
using EmailBlaster.Core.Models;

namespace EmailBlaster.Web;

/// <summary>
/// In-memory application state shared across requests, mirroring the desktop app's SessionState: the
/// loaded configuration, the imported recipients and the working template. Registered as a singleton
/// because this is a single-user local tool. (For a multi-tenant deployment this would move to a
/// per-user store.)
/// </summary>
public sealed class AppSession
{
    private readonly object _lock = new();

    public AppSession()
    {
        ConfigPath = ConfigurationLoader.ResolveFilePath();
        try { Config = ConfigurationLoader.Load(ConfigPath); }
        catch { Config = new EmailBlasterConfig(); }
    }

    public string? ConfigPath { get; set; }
    public EmailBlasterConfig Config { get; set; } = new();
    public RecipientList Recipients { get; set; } = RecipientList.Empty;

    public EmailTemplate Template { get; } = new()
    {
        Subject = "Hello {{Name|there}}",
        HtmlBody =
            "<p>Hi {{Name|there}},</p>" +
            "<p>Write your message here. Use the placeholder chips to personalise it.</p>" +
            "<p>Best regards,<br>Your team</p>"
    };

    private CancellationTokenSource? _ssoLoginCts;

    /// <summary>
    /// Runs <c>aws sso login</c> for the profile, cancelling any sign-in already in flight so a
    /// "relaunch" from the browser UI replaces the previous attempt instead of stacking CLI
    /// processes and browser windows.
    /// </summary>
    public async Task<AwsAccessTestResult> RunSsoLoginAsync(string profile)
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            _ssoLoginCts?.Cancel();
            cts = new CancellationTokenSource();
            _ssoLoginCts = cts;
        }

        try
        {
            return await AwsAccessTester.RunSsoLoginAsync(profile, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return AwsAccessTestResult.Fail(AwsAccessProblem.SsoLoginRequired,
                "This sign-in attempt was replaced by a newer one.");
        }
        finally
        {
            lock (_lock)
            {
                if (ReferenceEquals(_ssoLoginCts, cts))
                    _ssoLoginCts = null;
            }
            cts.Dispose();
        }
    }

    /// <summary>Persists the current config next to the app (or to the resolved path).</summary>
    public string SaveConfig()
    {
        lock (_lock)
        {
            var path = string.IsNullOrWhiteSpace(ConfigPath)
                ? Path.Combine(AppContext.BaseDirectory, ConfigurationLoader.DefaultFileName)
                : ConfigPath!;
            ConfigurationLoader.SaveToFile(Config, path);
            ConfigPath = path;
            return path;
        }
    }
}
