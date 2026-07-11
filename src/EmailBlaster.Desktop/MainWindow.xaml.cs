using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EmailBlaster.Core;
using EmailBlaster.Core.Configuration;
using EmailBlaster.Desktop.Views;
using Microsoft.Win32;

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

    // ---------------- menu: File ----------------

    private void LoadConfig_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Load configuration",
            Filter = "JSON configuration (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            _session.Config = ConfigurationLoader.LoadFromFile(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not load the configuration:\n{ex.Message}",
                "Load configuration", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // The Configuration screen holds copies of the values in its controls, so refresh it even when
        // it is not the visible view — otherwise a later save would write the stale on-screen values.
        _configView.OnShown();
        if (!ReferenceEquals(MainContent.Content, _configView) && MainContent.Content is IRefreshable refreshable)
            refreshable.OnShown();
    }

    private void SaveConfigAs_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        // Capture any edits sitting in the Configuration screen before writing the file.
        var errors = _configView.CommitEdits();
        if (errors.Count > 0)
        {
            var proceed = MessageBox.Show(this,
                "The configuration has validation problems:\n• " + string.Join("\n• ", errors) +
                "\n\nSave it anyway?",
                "Save configuration", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (proceed != MessageBoxResult.Yes)
                return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Save configuration as",
            Filter = "JSON configuration (*.json)|*.json|All files (*.*)|*.*",
            FileName = ConfigurationLoader.DefaultFileName,
            DefaultExt = ".json"
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            ConfigurationLoader.SaveToFile(_session.Config, dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save the configuration:\n{ex.Message}",
                "Save configuration", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // ---------------- theme ----------------

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
