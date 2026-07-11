using EmailBlaster.Core.Configuration;
using EmailBlaster.Core.Models;
using EmailBlaster.Core.Templating;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace EmailBlaster.Core.Delivery;

/// <summary>
/// Sends mail through a standard SMTP server using MailKit. The underlying connection is opened lazily
/// and kept alive across sends, which is what makes rate-limited bulk sending efficient.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailBlasterConfig _config;
    private readonly SmtpClient _client = new();
    private bool _connected;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SmtpEmailSender(EmailBlasterConfig config) => _config = config;

    public async Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            var mime = BuildMime(message);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }

            return SendResult.Ok(message.ToEmail);
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
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connected && _client.IsConnected)
            return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connected && _client.IsConnected)
                return;

            var secure = _config.Smtp.Security switch
            {
                SmtpSecurity.None => SecureSocketOptions.None,
                SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
                SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
                _ => SecureSocketOptions.Auto
            };

            await _client.ConnectAsync(_config.Smtp.Host, _config.Smtp.Port, secure, cancellationToken)
                .ConfigureAwait(false);

            if (_config.Smtp.RequiresAuthentication)
            {
                await _client.AuthenticateAsync(_config.Smtp.Username, _config.Smtp.Password, cancellationToken)
                    .ConfigureAwait(false);
            }

            _connected = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private MimeMessage BuildMime(EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_config.FromName, _config.FromEmail));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToEmail));

        if (!string.IsNullOrWhiteSpace(_config.ReplyToEmail))
            mime.ReplyTo.Add(new MailboxAddress(_config.FromName, _config.ReplyToEmail));

        mime.Subject = message.Subject;

        var builder = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody ?? PlaceholderMerger.HtmlToPlainText(message.HtmlBody)
        };
        mime.Body = builder.ToMessageBody();
        return mime;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync(true).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort disconnect; nothing actionable on teardown.
        }
        _client.Dispose();
        _gate.Dispose();
    }
}
