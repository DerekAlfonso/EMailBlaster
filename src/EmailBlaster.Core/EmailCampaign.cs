using EmailBlaster.Core.Configuration;
using EmailBlaster.Core.Delivery;
using EmailBlaster.Core.Models;
using EmailBlaster.Core.Templating;

namespace EmailBlaster.Core;

/// <summary>
/// The primary library entry point. Given a configuration, a template and a set of recipients, it
/// merges each message, paces delivery to the configured rate, and reports progress. It is transport
/// agnostic (SMTP or SES) and has no UI dependencies, so the same code path serves the desktop app and
/// an AWS Lambda function.
/// </summary>
public sealed class EmailCampaign
{
    private readonly EmailBlasterConfig _config;

    public EmailCampaign(EmailBlasterConfig config) => _config = config;

    /// <summary>
    /// Merges and sends <paramref name="template"/> to every recipient, honouring the configured send
    /// rate. Progress is reported through <paramref name="progress"/> after each message.
    /// </summary>
    public async Task<SendSummary> SendAsync(
        EmailTemplate template,
        IReadOnlyList<Recipient> recipients,
        IProgress<SendProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = _config.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Configuration is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors));

        var limiter = new RateLimiter(_config.SendRatePerSecond);
        var results = new List<SendResult>(recipients.Count);
        var succeeded = 0;
        var failed = 0;
        var cancelled = false;

        await using var sender = EmailSenderFactory.Create(_config);

        foreach (var recipient in recipients)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            try
            {
                await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }

            var message = PlaceholderMerger.Merge(template, recipient);

            SendResult result;
            try
            {
                result = await sender.SendAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }

            results.Add(result);
            if (result.Success) succeeded++; else failed++;

            progress?.Report(new SendProgress
            {
                Processed = results.Count,
                Total = recipients.Count,
                Succeeded = succeeded,
                Failed = failed,
                Last = result
            });
        }

        return new SendSummary { Results = results, Cancelled = cancelled };
    }

    /// <summary>
    /// Sends a single test message to <paramref name="toEmail"/> using the current configuration and,
    /// optionally, a sample recipient so placeholders resolve. Returns the send outcome.
    /// </summary>
    public async Task<SendResult> SendTestAsync(
        string toEmail,
        EmailTemplate template,
        Recipient? sample = null,
        CancellationToken cancellationToken = default)
    {
        var errors = _config.Validate();
        if (errors.Count > 0)
            return SendResult.Fail(toEmail,
                "Configuration is invalid: " + string.Join(" ", errors));

        var recipient = sample ?? new Recipient { Email = toEmail, Name = "Test Recipient" };
        var message = PlaceholderMerger.Merge(template, recipient);
        // Always deliver the test to the requested address regardless of the sample's own address.
        message.ToEmail = toEmail;

        await using var sender = EmailSenderFactory.Create(_config);
        return await sender.SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tests connectivity/credentials for the configured transport without sending mail.</summary>
    public async Task<string?> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var errors = _config.Validate();
        if (errors.Count > 0)
            return "Configuration is invalid: " + string.Join(" ", errors);

        await using var sender = EmailSenderFactory.Create(_config);
        return await sender.TestConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Produces merged previews for the first <paramref name="count"/> recipients without sending.
    /// Handy for the desktop preview pane.
    /// </summary>
    public IReadOnlyList<EmailMessage> Preview(
        EmailTemplate template, IReadOnlyList<Recipient> recipients, int count = 10)
    {
        return recipients.Take(count)
            .Select(r => PlaceholderMerger.Merge(template, r))
            .ToList();
    }
}
