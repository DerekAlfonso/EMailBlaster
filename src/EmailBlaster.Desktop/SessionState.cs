using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using EmailBlaster.Core;
using EmailBlaster.Core.Configuration;
using EmailBlaster.Core.Models;

namespace EmailBlaster.Desktop;

/// <summary>
/// Application-wide state shared across the views: the loaded configuration, the imported recipients
/// and the working template. A single instance is created by <see cref="MainWindow"/> and handed to
/// every view, so edits in one screen are visible in the others.
/// </summary>
public sealed class SessionState : INotifyPropertyChanged
{
    public SessionState()
    {
        ConfigPath = ConfigurationLoader.ResolveFilePath();
        try
        {
            Config = ConfigurationLoader.Load(ConfigPath);
        }
        catch
        {
            Config = new EmailBlasterConfig();
        }
    }

    private EmailBlasterConfig _config = new();
    public EmailBlasterConfig Config
    {
        get => _config;
        set { _config = value; OnPropertyChanged(); }
    }

    /// <summary>Path the config is read from / written to (next to the app by default).</summary>
    public string? ConfigPath { get; set; }

    private RecipientList _recipients = RecipientList.Empty;
    public RecipientList Recipients
    {
        get => _recipients;
        set
        {
            _recipients = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecipientCount));
        }
    }

    public int RecipientCount => _recipients.Count;

    /// <summary>The working message template edited in the Compose screen.</summary>
    public EmailTemplate Template { get; } = new()
    {
        Subject = "Hello {{Name|there}}",
        HtmlBody =
            "<p>Hi {{Name|there}},</p>" +
            "<p>Write your message here. Use the <b>Insert placeholder</b> chips to personalise it.</p>" +
            "<p>Best regards,<br>Your team</p>"
    };

    /// <summary>Saves the current config back to disk (falls back to the app folder).</summary>
    public string SaveConfig()
    {
        var path = string.IsNullOrWhiteSpace(ConfigPath)
            ? Path.Combine(AppContext.BaseDirectory, ConfigurationLoader.DefaultFileName)
            : ConfigPath!;
        ConfigurationLoader.SaveToFile(Config, path);
        ConfigPath = path;
        return path;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
