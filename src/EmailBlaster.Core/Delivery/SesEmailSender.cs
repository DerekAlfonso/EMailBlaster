using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using EmailBlaster.Core.Configuration;
using EmailBlaster.Core.Models;
using EmailBlaster.Core.Templating;

namespace EmailBlaster.Core.Delivery;

/// <summary>
/// Sends mail through the AWS Simple Email Service (SES) v2 API. Credentials are resolved from either
/// a named profile, an explicit access key pair, or — when neither is supplied in Profile mode — the
/// default AWS credential provider chain (which is what you want inside AWS Lambda).
/// </summary>
public sealed class SesEmailSender : IEmailSender
{
    private readonly EmailBlasterConfig _config;
    private readonly AmazonSimpleEmailServiceV2Client _client;

    public SesEmailSender(EmailBlasterConfig config)
    {
        _config = config;
        _client = BuildClient(config.Aws);
    }

    public async Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new SendEmailRequest
            {
                FromEmailAddress = FormatFrom(_config.FromName, _config.FromEmail),
                Destination = new Destination { ToAddresses = new List<string> { message.ToEmail } },
                Content = new EmailContent
                {
                    Simple = new Message
                    {
                        Subject = new Content { Data = message.Subject, Charset = "UTF-8" },
                        Body = new Body
                        {
                            Html = new Content { Data = message.HtmlBody, Charset = "UTF-8" },
                            Text = new Content
                            {
                                Data = message.TextBody ?? PlaceholderMerger.HtmlToPlainText(message.HtmlBody),
                                Charset = "UTF-8"
                            }
                        }
                    }
                }
            };

            if (!string.IsNullOrWhiteSpace(_config.ReplyToEmail))
                request.ReplyToAddresses = new List<string> { _config.ReplyToEmail! };

            if (!string.IsNullOrWhiteSpace(_config.Aws.ConfigurationSetName))
                request.ConfigurationSetName = _config.Aws.ConfigurationSetName;

            var response = await _client.SendEmailAsync(request, cancellationToken).ConfigureAwait(false);
            return SendResult.Ok(message.ToEmail, response.MessageId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return SendResult.Fail(message.ToEmail, ex.Message);
        }
    }

    public async Task<string?> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Lightweight, no-send call that still exercises credentials + region + connectivity.
            await _client.GetAccountAsync(new GetAccountRequest(), cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static AmazonSimpleEmailServiceV2Client BuildClient(AwsConfig aws)
    {
        var region = RegionEndpoint.GetBySystemName(aws.Region);

        if (aws.AuthMode == AwsAuthMode.AccessKey)
        {
            AWSCredentials creds = string.IsNullOrWhiteSpace(aws.SessionToken)
                ? new BasicAWSCredentials(aws.AccessKeyId, aws.SecretAccessKey)
                : new SessionAWSCredentials(aws.AccessKeyId, aws.SecretAccessKey, aws.SessionToken);
            return new AmazonSimpleEmailServiceV2Client(creds, region);
        }

        // Profile mode. A named profile is looked up in the shared credentials store; otherwise fall
        // back to the default provider chain (environment vars, EC2/ECS/Lambda roles, etc.).
        if (!string.IsNullOrWhiteSpace(aws.Profile))
        {
            var chain = new CredentialProfileStoreChain();
            if (chain.TryGetAWSCredentials(aws.Profile, out var profileCreds))
                return new AmazonSimpleEmailServiceV2Client(profileCreds, region);

            throw new InvalidOperationException(
                $"AWS profile '{aws.Profile}' was not found in the shared credentials store.");
        }

        return new AmazonSimpleEmailServiceV2Client(region);
    }

    private static string FormatFrom(string name, string email)
    {
        if (string.IsNullOrWhiteSpace(name))
            return email;

        // Quote the display name so commas/specials are handled, per RFC 5322.
        var escaped = name.Replace("\"", "\\\"");
        return $"\"{escaped}\" <{email}>";
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
