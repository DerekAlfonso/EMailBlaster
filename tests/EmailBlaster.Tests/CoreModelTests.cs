using System.Diagnostics;
using EmailBlaster.Core;
using EmailBlaster.Core.Configuration;
using EmailBlaster.Core.Delivery;
using EmailBlaster.Core.Models;
using Xunit;

namespace EmailBlaster.Tests;

public class RecipientTests
{
    [Fact]
    public void ToMergeFields_AlwaysIncludesNameAndEmail()
    {
        var recipient = new Recipient { Name = "Ada", Email = "ada@example.com" };
        var fields = recipient.ToMergeFields();
        Assert.Equal("Ada", fields["Name"]);
        Assert.Equal("ada@example.com", fields["Email"]);
    }

    [Fact]
    public void ToMergeFields_MergesExtraFieldsCaseInsensitively()
    {
        var recipient = new Recipient { Email = "a@b.co" };
        recipient.Fields["Company"] = "Acme";
        var fields = recipient.ToMergeFields();
        Assert.Equal("Acme", fields["company"]);
    }

    [Fact]
    public void ToString_UsesNameWhenPresent()
    {
        Assert.Equal("Ada <ada@example.com>",
            new Recipient { Name = "Ada", Email = "ada@example.com" }.ToString());
        Assert.Equal("ada@example.com",
            new Recipient { Email = "ada@example.com" }.ToString());
    }
}

public class RecipientListTests
{
    [Fact]
    public void Empty_HasNoRecipientsButStandardColumns()
    {
        Assert.Equal(0, RecipientList.Empty.Count);
        Assert.Equal(new[] { "Name", "Email" }, RecipientList.Empty.Columns);
        Assert.Equal(0, RecipientList.Empty.SkippedRows);
    }
}

public class EmailSenderFactoryTests
{
    [Fact]
    public void Create_SmtpProvider_ReturnsSmtpSender()
    {
        var sender = EmailSenderFactory.Create(new EmailBlasterConfig { Provider = SendProvider.Smtp });
        Assert.IsType<SmtpEmailSender>(sender);
    }

    [Fact]
    public void Create_AwsProvider_ReturnsSesSender()
    {
        var sender = EmailSenderFactory.Create(new EmailBlasterConfig { Provider = SendProvider.Aws });
        Assert.IsType<SesEmailSender>(sender);
    }
}

public class RateLimiterTests
{
    [Fact]
    public void ZeroOrNegativeRate_IsUnlimited()
    {
        Assert.True(new RateLimiter(0).IsUnlimited);
        Assert.True(new RateLimiter(-5).IsUnlimited);
        Assert.False(new RateLimiter(10).IsUnlimited);
    }

    [Fact]
    public async Task Unlimited_DoesNotDelay()
    {
        var limiter = new RateLimiter(0);
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
            await limiter.WaitAsync();
        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task LimitedRate_SpacesOperationsOut()
    {
        // 20 ops/sec = 50ms interval. Four extra waits after the first must take at least ~3 intervals;
        // the generous lower bound keeps this stable on busy CI machines.
        var limiter = new RateLimiter(20);
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++)
            await limiter.WaitAsync();
        Assert.True(stopwatch.ElapsedMilliseconds >= 150, $"took only {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task WaitAsync_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var limiter = new RateLimiter(0);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limiter.WaitAsync(cts.Token));
    }
}
