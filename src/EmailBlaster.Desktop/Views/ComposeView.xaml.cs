using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using EmailBlaster.Desktop.Editor;

namespace EmailBlaster.Desktop.Views;

public partial class ComposeView : UserControl, IRefreshable
{
    private readonly SessionState _session;
    private bool _editorReady;
    private bool _sourceMode;
    private bool _suppressSync;
    private bool _lastFocusSubject;

    public ComposeView(SessionState session)
    {
        _session = session;
        InitializeComponent();

        SubjectBox.Text = _session.Template.Subject;
        BuildPlaceholderChips();
        Loaded += OnLoadedOnce;
    }

    private async void OnLoadedOnce(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedOnce;
        await InitializeEditorAsync();
    }

    public void OnShown()
    {
        SubjectBox.Text = _session.Template.Subject;
        BuildPlaceholderChips();
        if (_editorReady && !_sourceMode)
            _ = SetEditorHtmlAsync(_session.Template.HtmlBody);
    }

    // -------------------- WebView2 lifecycle --------------------

    private async Task InitializeEditorAsync()
    {
        try
        {
            await Editor.EnsureCoreWebView2Async();

            var settings = Editor.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = true;
            settings.IsStatusBarEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsZoomControlEnabled = false;

            Editor.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            Editor.NavigationCompleted += OnNavigationCompleted;
            Editor.CoreWebView2.NavigateToString(HtmlEditorDocument.Html);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "The rich HTML editor could not start. The Microsoft Edge WebView2 Runtime may be missing.\n\n"
                + "You can still edit the message using the 'Edit HTML source' view.\n\nDetails: " + ex.Message,
                "Editor unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            // Fall back to source editing.
            ForceSourceMode();
        }
    }

    private async void OnNavigationCompleted(object? sender, EventArgs e)
    {
        _editorReady = true;
        await SetEditorHtmlAsync(_session.Template.HtmlBody);
    }

    private void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var raw = e.TryGetWebMessageAsString();
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "html")
            {
                var html = root.GetProperty("value").GetString() ?? string.Empty;
                _session.Template.HtmlBody = html;
            }
        }
        catch
        {
            // Ignore malformed messages.
        }
    }

    // -------------------- Editor interop --------------------

    private async Task SetEditorHtmlAsync(string html)
    {
        if (!_editorReady)
            return;
        _suppressSync = true;
        await Editor.CoreWebView2.ExecuteScriptAsync($"setHtml({JsonSerializer.Serialize(html)})");
        _suppressSync = false;
    }

    private async Task<string> GetEditorHtmlAsync()
    {
        if (!_editorReady)
            return _session.Template.HtmlBody;
        var json = await Editor.CoreWebView2.ExecuteScriptAsync("getHtml()");
        return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
    }

    private async void InsertPlaceholder(string token)
    {
        if (_lastFocusSubject)
        {
            InsertIntoSubject(token);
            return;
        }

        if (_sourceMode)
        {
            var idx = SourceBox.CaretIndex;
            SourceBox.Text = SourceBox.Text.Insert(idx, token);
            SourceBox.CaretIndex = idx + token.Length;
            SourceBox.Focus();
            return;
        }

        if (_editorReady)
        {
            await Editor.CoreWebView2.ExecuteScriptAsync($"insertHtml({JsonSerializer.Serialize(token)})");
            Editor.Focus();
        }
    }

    private void InsertIntoSubject(string token)
    {
        var idx = SubjectBox.CaretIndex;
        SubjectBox.Text = SubjectBox.Text.Insert(idx, token);
        SubjectBox.CaretIndex = idx + token.Length;
        SubjectBox.Focus();
    }

    // -------------------- Placeholder chips --------------------

    private void BuildPlaceholderChips()
    {
        PlaceholderPanel.Children.Clear();
        foreach (var column in _session.Recipients.Columns)
        {
            var token = "{{" + column + "}}";
            var button = new Button
            {
                Style = (Style)FindResource("ChipButton"),
                Content = token,
                FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas, monospace")
            };
            button.Click += (_, _) => InsertPlaceholder(token);
            PlaceholderPanel.Children.Add(button);
        }
    }

    // -------------------- Subject --------------------

    private void Subject_GotFocus(object sender, RoutedEventArgs e) => _lastFocusSubject = true;

    private void Subject_TextChanged(object sender, TextChangedEventArgs e) =>
        _session.Template.Subject = SubjectBox.Text;

    // -------------------- Source toggle --------------------

    private async void ToggleSource_Click(object sender, RoutedEventArgs e)
    {
        if (!_sourceMode)
        {
            // Visual -> Source
            var html = await GetEditorHtmlAsync();
            _suppressSync = true;
            SourceBox.Text = html;
            _suppressSync = false;
            SourceBox.Visibility = Visibility.Visible;
            Editor.Visibility = Visibility.Collapsed;
            ToggleSourceButton.Content = "Visual editor";
            _sourceMode = true;
        }
        else
        {
            // Source -> Visual
            _session.Template.HtmlBody = SourceBox.Text;
            await SetEditorHtmlAsync(SourceBox.Text);
            SourceBox.Visibility = Visibility.Collapsed;
            Editor.Visibility = Visibility.Visible;
            ToggleSourceButton.Content = "Edit HTML source";
            _sourceMode = false;
        }
    }

    private void ForceSourceMode()
    {
        _sourceMode = true;
        _suppressSync = true;
        SourceBox.Text = _session.Template.HtmlBody;
        _suppressSync = false;
        SourceBox.Visibility = Visibility.Visible;
        Editor.Visibility = Visibility.Collapsed;
        ToggleSourceButton.Content = "Visual editor";
        ToggleSourceButton.IsEnabled = false;
        _lastFocusSubject = false;
    }

    private void Source_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSync)
            return;
        _session.Template.HtmlBody = SourceBox.Text;
        _lastFocusSubject = false;
    }
}
