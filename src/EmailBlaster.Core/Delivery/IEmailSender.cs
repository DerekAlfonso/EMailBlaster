using EmailBlaster.Core.Models;

namespace EmailBlaster.Core.Delivery;

/// <summary>
/// A transport capable of sending a single fully-merged message. Implementations should be safe to
/// reuse across many sends. Callers dispose the sender when finished.
/// </summary>
public interface IEmailSender : IAsyncDisposable
{
    /// <summary>Sends one message and returns the outcome. Should not throw for ordinary send failures.</summary>
    Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies connectivity/credentials without sending mail where the transport supports it.
    /// Returns null on success or an error message on failure.
    /// </summary>
    Task<string?> TestConnectionAsync(CancellationToken cancellationToken = default);
}
