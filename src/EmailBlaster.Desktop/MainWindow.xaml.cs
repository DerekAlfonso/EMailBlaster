using System.Windows;
using System.Windows.Controls;
using EmailBlaster.Core.Configuration;
using EmailBlaster.Desktop.Views;

namespace EmailBlaster.Desktop;

public partial class MainWindow : Window
{
    private readonly SessionState _session = new();

    private readonly ConfigurationView _configView;
    private readonly RecipientsView _recipientsView;
    private readonly ComposeView _composeView;
    private readonly PreviewView _previewView;
    private readonly SendView _sendView;

    public MainWindow()
    {
        InitializeComponent();

        _configView = new ConfigurationView(_session);
        _recipientsView = new RecipientsView(_session);
        _composeView = new ComposeView(_session);
        _previewView = new PreviewView(_session);
        _sendView = new SendView(_session);

        // Keep the sidebar footer in sync with session changes.
        _session.PropertyChanged += (_, _) => RefreshStatus();
        RefreshStatus();
        UpdateThemeToggleLabel();

        MainContent.Content = _configView;
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle();
        UpdateThemeToggleLabel();
    }

    private void UpdateThemeToggleLabel() =>
        ThemeToggle.Content = ThemeManager.Current == AppTheme.Dark ? "🌙  Dark theme" : "☀  Light theme";

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (MainContent is null || sender is not RadioButton rb)
            return;

        // Views refresh themselves when shown so cross-screen edits are always reflected.
        UserControl view = rb.Name switch
        {
            nameof(NavConfig) => _configView,
            nameof(NavRecipients) => _recipientsView,
            nameof(NavCompose) => _composeView,
            nameof(NavPreview) => _previewView,
            nameof(NavSend) => _sendView,
            _ => _configView
        };

        if (view is IRefreshable refreshable)
            refreshable.OnShown();

        MainContent.Content = view;
    }

    private void RefreshStatus()
    {
        var provider = _session.Config.Provider == SendProvider.Aws ? "AWS SES" : "SMTP";
        StatusProvider.Text = $"Provider: {provider}";
        StatusRecipients.Text = $"{_session.RecipientCount:N0} recipients loaded";
    }
}

/// <summary>Implemented by views that need to refresh their bindings each time they are shown.</summary>
public interface IRefreshable
{
    void OnShown();
}
