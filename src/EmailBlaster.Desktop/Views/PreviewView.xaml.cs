using System.Windows;
using System.Windows.Controls;
using EmailBlaster.Core.Models;
using EmailBlaster.Core.Templating;

namespace EmailBlaster.Desktop.Views;

public partial class PreviewView : UserControl, IRefreshable
{
    private readonly SessionState _session;
    private bool _browserReady;
    private List<Recipient> _recipients = new();

    public PreviewView(SessionState session)
    {
        _session = session;
        InitializeComponent();
        Loaded += OnLoadedOnce;
    }

    private async void OnLoadedOnce(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedOnce;
        try
        {
            await PreviewBrowser.EnsureCoreWebView2Async();
            PreviewBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            PreviewBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _browserReady = true;
        }
        catch
        {
            _browserReady = false;
        }
        Reload();
    }

    public void OnShown() => Reload();

    private void Reload()
    {
        // Use imported recipients, or a single sample so the user always sees a rendering.
        var source = _session.Recipients.Recipients;
        _recipients = source.Count > 0
            ? source.Take(200).ToList()
            : new List<Recipient> { SampleRecipient() };

        var previousIndex = RecipientSelector.SelectedIndex;

        RecipientSelector.Items.Clear();
        foreach (var r in _recipients)
            RecipientSelector.Items.Add(r.ToString());

        if (RecipientSelector.Items.Count > 0)
        {
            RecipientSelector.SelectedIndex =
                previousIndex >= 0 && previousIndex < RecipientSelector.Items.Count ? previousIndex : 0;
        }

        RenderCurrent();
    }

    private Recipient SampleRecipient()
    {
        var sample = new Recipient { Name = "Alex Sample", Email = "alex.sample@example.com" };
        // Populate any custom columns with obvious placeholder values.
        foreach (var column in _session.Recipients.Columns)
        {
            if (column is "Name" or "Email")
                continue;
            sample.Fields[column] = $"[{column}]";
        }
        return sample;
    }

    private void RenderCurrent()
    {
        var index = RecipientSelector.SelectedIndex;
        if (index < 0 || index >= _recipients.Count)
            return;

        var recipient = _recipients[index];
        var message = PlaceholderMerger.Merge(_session.Template, recipient);

        ToText.Text = recipient.ToString();
        SubjectText.Text = message.Subject;

        PrevButton.IsEnabled = index > 0;
        NextButton.IsEnabled = index < _recipients.Count - 1;

        if (_browserReady)
            PreviewBrowser.CoreWebView2.NavigateToString(WrapHtml(message.HtmlBody));
    }

    private static string WrapHtml(string bodyHtml) =>
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\">"
        + "<style>body{font-family:'Segoe UI',system-ui,sans-serif;color:#0f172a;"
        + "font-size:14px;line-height:1.55;padding:22px;margin:0;}"
        + "a{color:#2563eb;} img{max-width:100%;}</style></head><body>"
        + bodyHtml + "</body></html>";

    private void Recipient_Changed(object sender, SelectionChangedEventArgs e) => RenderCurrent();

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (RecipientSelector.SelectedIndex > 0)
            RecipientSelector.SelectedIndex--;
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (RecipientSelector.SelectedIndex < RecipientSelector.Items.Count - 1)
            RecipientSelector.SelectedIndex++;
    }
}
