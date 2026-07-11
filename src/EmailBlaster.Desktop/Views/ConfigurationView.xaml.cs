using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EmailBlaster.Core;
using EmailBlaster.Core.Configuration;

namespace EmailBlaster.Desktop.Views;

public partial class ConfigurationView : UserControl, IRefreshable
{
    private readonly SessionState _session;
    private bool _loading;

    public ConfigurationView(SessionState session)
    {
        _session = session;
        InitializeComponent();
        InitSecurityChoices();
        InitAwsProfileChoices();
        LoadFromConfig();
    }

    public void OnShown()
    {
        // Re-discover on every visit so profiles added while the app is open show up.
        InitAwsProfileChoices();
        LoadFromConfig();
    }

    private void InitAwsProfileChoices() => AwsProfileBox.ItemsSource = AwsProfileCatalog.ListProfileNames();

    private void InitSecurityChoices()
    {
        SmtpSecurityBox.Items.Clear();
        foreach (var value in Enum.GetValues<SmtpSecurity>())
        {
            SmtpSecurityBox.Items.Add(new ComboBoxItem
            {
                Content = value switch
                {
                    SmtpSecurity.None => "None (plain)",
                    SmtpSecurity.StartTls => "STARTTLS (port 587)",
                    SmtpSecurity.SslOnConnect => "SSL on connect (port 465)",
                    _ => "Auto"
                },
                Tag = value
            });
        }
    }

    private void LoadFromConfig()
    {
        _loading = true;
        var c = _session.Config;

        FromNameBox.Text = c.FromName;
        FromEmailBox.Text = c.FromEmail;
        ReplyToBox.Text = c.ReplyToEmail ?? string.Empty;

        // Rate / unlimited.
        UnlimitedCheck.IsChecked = c.IsUnlimitedRate;
        RateBox.Text = c.IsUnlimitedRate
            ? string.Empty
            : c.SendRatePerSecond.ToString(CultureInfo.CurrentCulture);
        RateBox.IsEnabled = !c.IsUnlimitedRate;

        // Provider.
        ProviderSmtp.IsChecked = c.Provider == SendProvider.Smtp;
        ProviderAws.IsChecked = c.Provider == SendProvider.Aws;

        // SMTP.
        SmtpHostBox.Text = c.Smtp.Host;
        SmtpPortBox.Text = c.Smtp.Port.ToString(CultureInfo.InvariantCulture);
        SmtpUserBox.Text = c.Smtp.Username ?? string.Empty;
        SmtpPassBox.Password = c.Smtp.Password ?? string.Empty;
        SelectSecurity(c.Smtp.Security);

        // AWS.
        AwsRegionBox.Text = c.Aws.Region;
        AwsConfigSetBox.Text = c.Aws.ConfigurationSetName ?? string.Empty;
        AwsProfileMode.IsChecked = c.Aws.AuthMode == AwsAuthMode.Profile;
        AwsKeyMode.IsChecked = c.Aws.AuthMode == AwsAuthMode.AccessKey;
        AwsProfileBox.Text = c.Aws.Profile ?? string.Empty;
        AwsAccessKeyBox.Text = c.Aws.AccessKeyId ?? string.Empty;
        AwsSecretBox.Password = c.Aws.SecretAccessKey ?? string.Empty;
        AwsSessionTokenBox.Text = c.Aws.SessionToken ?? string.Empty;

        if (string.IsNullOrWhiteSpace(TestToBox.Text))
            TestToBox.Text = c.FromEmail;

        _loading = false;
        UpdateProviderVisibility();
        UpdateAwsAuthVisibility();
    }

    private void SelectSecurity(SmtpSecurity security)
    {
        foreach (ComboBoxItem item in SmtpSecurityBox.Items)
        {
            if (item.Tag is SmtpSecurity s && s == security)
            {
                SmtpSecurityBox.SelectedItem = item;
                return;
            }
        }
    }

    /// <summary>
    /// Commits the on-screen values into the shared config without persisting to disk. Used by the
    /// File menu so "Save Configuration As" includes edits that have not been saved yet.
    /// </summary>
    public IReadOnlyList<string> CommitEdits() => ApplyToConfig();

    /// <summary>Reads every control back into the shared config. Returns validation errors (empty = ok).</summary>
    private IReadOnlyList<string> ApplyToConfig()
    {
        var c = _session.Config;

        c.FromName = FromNameBox.Text.Trim();
        c.FromEmail = FromEmailBox.Text.Trim();
        c.ReplyToEmail = string.IsNullOrWhiteSpace(ReplyToBox.Text) ? null : ReplyToBox.Text.Trim();

        if (UnlimitedCheck.IsChecked == true)
        {
            c.SendRatePerSecond = 0;
        }
        else if (double.TryParse(RateBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var rate) ||
                 double.TryParse(RateBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out rate))
        {
            c.SendRatePerSecond = rate;
        }

        c.Provider = ProviderAws.IsChecked == true ? SendProvider.Aws : SendProvider.Smtp;

        c.Smtp.Host = SmtpHostBox.Text.Trim();
        if (int.TryParse(SmtpPortBox.Text, out var port))
            c.Smtp.Port = port;
        c.Smtp.Username = string.IsNullOrWhiteSpace(SmtpUserBox.Text) ? null : SmtpUserBox.Text.Trim();
        c.Smtp.Password = string.IsNullOrEmpty(SmtpPassBox.Password) ? null : SmtpPassBox.Password;
        if (SmtpSecurityBox.SelectedItem is ComboBoxItem { Tag: SmtpSecurity sec })
            c.Smtp.Security = sec;

        c.Aws.Region = AwsRegionBox.Text.Trim();
        c.Aws.ConfigurationSetName =
            string.IsNullOrWhiteSpace(AwsConfigSetBox.Text) ? null : AwsConfigSetBox.Text.Trim();
        c.Aws.AuthMode = AwsKeyMode.IsChecked == true ? AwsAuthMode.AccessKey : AwsAuthMode.Profile;
        c.Aws.Profile = string.IsNullOrWhiteSpace(AwsProfileBox.Text) ? null : AwsProfileBox.Text.Trim();
        c.Aws.AccessKeyId = string.IsNullOrWhiteSpace(AwsAccessKeyBox.Text) ? null : AwsAccessKeyBox.Text.Trim();
        c.Aws.SecretAccessKey = string.IsNullOrEmpty(AwsSecretBox.Password) ? null : AwsSecretBox.Password;
        c.Aws.SessionToken =
            string.IsNullOrWhiteSpace(AwsSessionTokenBox.Text) ? null : AwsSessionTokenBox.Text.Trim();

        // Notify the shell so the sidebar footer updates.
        _session.Config = c;

        return c.Validate();
    }

    // ---------------- UI toggles ----------------

    private void Provider_Changed(object sender, RoutedEventArgs e) => UpdateProviderVisibility();

    private void UpdateProviderVisibility()
    {
        if (SmtpCard is null || AwsCard is null)
            return;
        var smtp = ProviderSmtp.IsChecked == true;
        SmtpCard.Visibility = smtp ? Visibility.Visible : Visibility.Collapsed;
        AwsCard.Visibility = smtp ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AwsAuth_Changed(object sender, RoutedEventArgs e) => UpdateAwsAuthVisibility();

    private void UpdateAwsAuthVisibility()
    {
        if (AwsProfilePanel is null || AwsKeyPanel is null)
            return;
        var profile = AwsProfileMode.IsChecked == true;
        AwsProfilePanel.Visibility = profile ? Visibility.Visible : Visibility.Collapsed;
        AwsKeyPanel.Visibility = profile ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Unlimited_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || RateBox is null)
            return;
        var unlimited = UnlimitedCheck.IsChecked == true;
        RateBox.IsEnabled = !unlimited;
        if (unlimited)
            RateBox.Text = string.Empty;
        else if (string.IsNullOrWhiteSpace(RateBox.Text))
            RateBox.Text = "5";
    }

    // ---------------- Actions ----------------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var errors = ApplyToConfig();
        if (errors.Count > 0)
        {
            ShowStatus(false, "Please fix these settings first:\n• " + string.Join("\n• ", errors));
            return;
        }

        try
        {
            var path = _session.SaveConfig();
            ShowStatus(true, $"Settings saved to {path}");
        }
        catch (Exception ex)
        {
            ShowStatus(false, $"Could not save settings: {ex.Message}");
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var errors = ApplyToConfig();
        if (errors.Count > 0)
        {
            ShowStatus(false, "Please fix these settings first:\n• " + string.Join("\n• ", errors));
            return;
        }

        SetBusy(TestConnectionButton, true, "Testing…");
        try
        {
            var campaign = new EmailCampaign(_session.Config);
            var error = await campaign.TestConnectionAsync();
            if (error is null)
                ShowStatus(true, "Connection succeeded. Credentials and transport look good.");
            else
                ShowStatus(false, $"Connection failed: {error}");
        }
        catch (Exception ex)
        {
            ShowStatus(false, $"Connection failed: {ex.Message}");
        }
        finally
        {
            SetBusy(TestConnectionButton, false, "Test connection");
        }
    }

    private async void SendTest_Click(object sender, RoutedEventArgs e)
    {
        var errors = ApplyToConfig();
        if (errors.Count > 0)
        {
            ShowStatus(false, "Please fix these settings first:\n• " + string.Join("\n• ", errors));
            return;
        }

        var to = TestToBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(to))
        {
            ShowStatus(false, "Enter an address to send the test to.");
            return;
        }

        SetBusy(SendTestButton, true, "Sending…");
        try
        {
            var campaign = new EmailCampaign(_session.Config);
            var result = await campaign.SendTestAsync(to, _session.Template);
            if (result.Success)
                ShowStatus(true, $"Test email sent to {to}."
                                 + (result.MessageId is null ? "" : $" (id: {result.MessageId})"));
            else
                ShowStatus(false, $"Test failed: {result.Error}");
        }
        catch (Exception ex)
        {
            ShowStatus(false, $"Test failed: {ex.Message}");
        }
        finally
        {
            SetBusy(SendTestButton, false, "Send test");
        }
    }

    // ---------------- Helpers ----------------

    private void SetBusy(Button button, bool busy, string text)
    {
        button.IsEnabled = !busy;
        button.Content = text;
    }

    private void ShowStatus(bool success, string message)
    {
        StatusBar.Visibility = Visibility.Visible;
        // Use the themed status brushes so the banner tracks light/dark like the rest of the UI.
        StatusBar.Background = (Brush)FindResource(success ? "StatusOkBackgroundBrush" : "StatusErrBackgroundBrush");
        StatusText.Foreground = (Brush)FindResource(success ? "SuccessBrush" : "DangerBrush");
        StatusText.Text = message;
    }
}
