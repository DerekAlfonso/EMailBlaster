using System.Collections.Concurrent;
using EmailBlaster.Core;
using EmailBlaster.Core.Configuration;
using EmailBlaster.Core.Models;

namespace EmailBlaster.Web;

/// <summary>
/// Runs bulk sends as background jobs and exposes their live progress so the browser can poll for
/// updates. Jobs are kept in memory and are safe to poll and cancel concurrently.
/// </summary>
public sealed class SendJobManager
{
    private readonly ConcurrentDictionary<string, SendJob> _jobs = new();
    private int _counter;

    public SendJob Start(EmailBlasterConfig config, EmailTemplate template, IReadOnlyList<Recipient> recipients)
    {
        var id = $"job-{Interlocked.Increment(ref _counter)}";
        var job = new SendJob(id, recipients.Count);
        _jobs[id] = job;

        // Snapshot the template so later edits don't affect an in-flight run.
        var snapshot = new EmailTemplate
        {
            Subject = template.Subject,
            HtmlBody = template.HtmlBody,
            TextBody = template.TextBody
        };

        job.Task = Task.Run(async () =>
        {
            var progress = new Progress<SendProgress>(p =>
            {
                job.Processed = p.Processed;
                job.Succeeded = p.Succeeded;
                job.Failed = p.Failed;
                if (!p.Last.Success)
                    job.AddError(p.Last.ToEmail, p.Last.Error ?? "Unknown error");
            });

            try
            {
                var campaign = new EmailCampaign(config);
                var summary = await campaign.SendAsync(snapshot, recipients, progress, job.Cancellation.Token);
                job.Cancelled = summary.Cancelled;
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
            }
            finally
            {
                job.Done = true;
            }
        });

        return job;
    }

    public SendJob? Get(string id) => _jobs.TryGetValue(id, out var job) ? job : null;

    public bool Cancel(string id)
    {
        if (_jobs.TryGetValue(id, out var job))
        {
            job.Cancellation.Cancel();
            return true;
        }
        return false;
    }
}

/// <summary>Live state of a single background send job.</summary>
public sealed class SendJob
{
    private readonly List<FailureInfo> _errors = new();
    private readonly object _errorLock = new();

    public SendJob(string id, int total)
    {
        Id = id;
        Total = total;
    }

    public string Id { get; }
    public int Total { get; }
    public volatile int Processed;
    public volatile int Succeeded;
    public volatile int Failed;
    public volatile bool Done;
    public volatile bool Cancelled;
    public string? Error;
    public CancellationTokenSource Cancellation { get; } = new();
    public Task? Task { get; set; }

    public void AddError(string email, string message)
    {
        lock (_errorLock)
        {
            if (_errors.Count < 500) // cap memory for very large failure runs
                _errors.Add(new FailureInfo(email, message));
        }
    }

    public IReadOnlyList<FailureInfo> RecentErrors(int max = 100)
    {
        lock (_errorLock)
        {
            return _errors.Count <= max ? _errors.ToList() : _errors.Skip(_errors.Count - max).ToList();
        }
    }
}

public readonly record struct FailureInfo(string Email, string Message);
