using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EmailBlaster.Core;
using EmailBlaster.Core.Configuration;
using EmailBlaster.Core.Models;

namespace EmailBlaster.Desktop.Views;

public partial class SendView : UserControl, IRefreshable
{
    private readonly SessionState _session;
    private readonly ObservableCollection<LogEntry> _log = new();
    private CancellationTokenSource? _cts;

    public SendView(SessionState session)
    {
        _session = session;
        InitializeComponent();
        LogGrid.ItemsSource = _log;
        RefreshSummary();
    }

    public void OnShown() => RefreshSummary();

    private void RefreshSummary()
    {
        var c = _session.Config;
        SummaryRecipients.Text = _session.RecipientCount.ToString("N0", CultureInfo.CurrentCulture);
        SummaryProvider.Text = c.Provider == SendProvider.Aws ? "AWS SES" : "SMTP";
        SummaryRate.Text = c.IsUnlimitedRate
            ? "Unlimited"
            : $"{c.SendRatePerSecond.ToString(CultureInfo.CurrentCulture)} / sec";
        SummaryFrom.Text = string.IsNullOrWhiteSpace(c.FromEmail) ? "—" : c.FromEmail;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        RefreshSummary();

        var recipients = _session.Recipients.Recipients;
        if (recipients.Count == 0)
        {
            MessageBox.Show("Import recipients before sending.", "Nothing to send",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var errors = _session.Config.Validate();
        if (errors.Count > 0)
        {
            MessageBox.Show("Please fix the configuration first:\n\n• " + string.Join("\n• ", errors),
                "Configuration invalid", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Send this message to {recipients.Count:N0} recipient(s) using {SummaryProvider.Text}?",
            "Confirm send", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        _log.Clear();
        _cts = new CancellationTokenSource();
        SetRunning(true);

        var progress = new Progress<SendProgress>(OnProgress);

        try
        {
            var campaign = new EmailCampaign(_session.Config);
            var summary = await campaign.SendAsync(_session.Template, recipients, progress, _cts.Token);

            ProgressLabel.Text = summary.Cancelled ? "Cancelled" : "Completed";
            ProgressCounts.Text =
                $"{summary.Succeeded:N0} sent · {summary.Failed:N0} failed · {summary.Total:N0} total";

            if (!summary.Cancelled)
                MessageBox.Show(
                    $"Finished. {summary.Succeeded:N0} sent, {summary.Failed:N0} failed.",
                    "Send complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ProgressLabel.Text = "Error";
            MessageBox.Show(ex.Message, "Send failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetRunning(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnProgress(SendProgress p)
    {
        Progress.Value = p.Fraction * 100;
        ProgressLabel.Text = "Sending…";
        ProgressCounts.Text =
            $"{p.Succeeded:N0} sent · {p.Failed:N0} failed · {p.Processed:N0} / {p.Total:N0}";

        _log.Add(new LogEntry
        {
            Status = p.Last.Success ? "Sent" : "Failed",
            Email = p.Last.ToEmail,
            Detail = p.Last.Success
                ? (p.Last.MessageId ?? "Delivered to transport")
                : (p.Last.Error ?? "Unknown error")
        });

        // Keep the newest row visible.
        if (_log.Count > 0)
            LogGrid.ScrollIntoView(_log[^1]);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        CancelButton.IsEnabled = false;
        ProgressLabel.Text = "Cancelling…";
    }

    private void SetRunning(bool running)
    {
        StartButton.IsEnabled = !running;
        CancelButton.IsEnabled = running;
        StartButton.Content = running ? "Sending…" : "Start sending";
        if (running)
        {
            Progress.Value = 0;
            ProgressLabel.Text = "Starting…";
            ProgressCounts.Text = "";
        }
    }

    private sealed class LogEntry
    {
        public string Status { get; init; } = "";
        public string Email { get; init; } = "";
        public string Detail { get; init; } = "";
    }
}
