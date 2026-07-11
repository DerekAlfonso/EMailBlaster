namespace EmailBlaster.Core.Models;

/// <summary>Outcome of attempting to send a single message.</summary>
public sealed class SendResult
{
    public string ToEmail { get; init; } = string.Empty;
    public bool Success { get; init; }

    /// <summary>Transport-assigned message id when available (e.g. SES message id).</summary>
    public string? MessageId { get; init; }

    /// <summary>Failure reason when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }

    public static SendResult Ok(string toEmail, string? messageId = null) =>
        new() { ToEmail = toEmail, Success = true, MessageId = messageId };

    public static SendResult Fail(string toEmail, string error) =>
        new() { ToEmail = toEmail, Success = false, Error = error };
}

/// <summary>Progress notification raised as a bulk send proceeds.</summary>
public sealed class SendProgress
{
    /// <summary>Number of messages processed so far (successes + failures).</summary>
    public int Processed { get; init; }

    /// <summary>Total messages in the batch.</summary>
    public int Total { get; init; }

    /// <summary>Running count of successful sends.</summary>
    public int Succeeded { get; init; }

    /// <summary>Running count of failed sends.</summary>
    public int Failed { get; init; }

    /// <summary>The most recent result.</summary>
    public SendResult Last { get; init; } = default!;

    /// <summary>Completion fraction from 0 to 1.</summary>
    public double Fraction => Total == 0 ? 1 : (double)Processed / Total;
}

/// <summary>Aggregate summary of a completed bulk send.</summary>
public sealed class SendSummary
{
    public IReadOnlyList<SendResult> Results { get; init; } = Array.Empty<SendResult>();
    public int Total => Results.Count;
    public int Succeeded => Results.Count(r => r.Success);
    public int Failed => Results.Count(r => !r.Success);
    public bool Cancelled { get; init; }
}
