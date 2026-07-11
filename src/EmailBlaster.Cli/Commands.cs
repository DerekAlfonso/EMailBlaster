using System.Globalization;
using EmailBlaster.Core;
using EmailBlaster.Core.Configuration;
using EmailBlaster.Core.Import;
using EmailBlaster.Core.Models;
using EmailBlaster.Core.Templating;

namespace EmailBlaster.Cli;

/// <summary>Implements each CLI verb on top of the shared <see cref="EmailBlaster.Core"/> engine.</summary>
public static class Commands
{
    // ---------------- shared helpers ----------------

    /// <summary>Loads config via the standard resolution order and applies global CLI overrides.</summary>
    private static EmailBlasterConfig LoadConfig(Arguments args)
    {
        var config = ConfigurationLoader.Load(args.Get("config"));

        if (args.Get("provider") is { } provider &&
            Enum.TryParse<SendProvider>(provider, ignoreCase: true, out var p))
            config.Provider = p;

        if (args.Get("rate") is { } rate &&
            double.TryParse(rate, NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
            config.SendRatePerSecond = r;

        if (args.Get("from-email") is { } fe) config.FromEmail = fe;
        if (args.Get("from-name") is { } fn) config.FromName = fn;
        if (args.Get("reply-to") is { } rt) config.ReplyToEmail = rt;

        return config;
    }

    private static EmailTemplate BuildTemplate(Arguments args)
    {
        var subject =
            args.Get("subject") ??
            (args.Get("subject-file") is { } sf ? File.ReadAllText(sf).Trim() : null) ??
            throw new ArgumentException("A subject is required: pass --subject or --subject-file.");

        var html =
            args.Get("html") ??
            (args.Get("html-file") is { } hf ? File.ReadAllText(hf) : null) ??
            throw new ArgumentException("An HTML body is required: pass --html or --html-file.");

        var text = args.Get("text-file") is { } tf ? File.ReadAllText(tf) : null;

        return new EmailTemplate { Subject = subject, HtmlBody = html, TextBody = text };
    }

    private static RecipientList LoadRecipients(Arguments args)
    {
        var path = args.Get("recipients")
                   ?? throw new ArgumentException("A recipients file is required: pass --recipients <file>.");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Recipients file not found: {path}");
        return RecipientImporterFactory.ImportFile(path);
    }

    private static bool ReportValidation(EmailBlasterConfig config)
    {
        var errors = config.Validate();
        if (errors.Count == 0)
            return true;
        ConsoleEx.Error("Configuration is invalid:");
        foreach (var e in errors)
            Console.Error.WriteLine("    - " + e);
        return false;
    }

    // ---------------- verbs ----------------

    public static async Task<int> Send(Arguments args)
    {
        var config = LoadConfig(args);
        var template = BuildTemplate(args);
        var list = LoadRecipients(args);

        if (list.Count == 0)
        {
            ConsoleEx.Warn("No recipients with an email address were found. Nothing to send.");
            return 1;
        }

        ConsoleEx.Heading("Campaign summary");
        Console.WriteLine($"    Provider    : {config.Provider}");
        Console.WriteLine($"    From        : {config.FromName} <{config.FromEmail}>");
        Console.WriteLine($"    Rate        : {(config.IsUnlimitedRate ? "unlimited" : config.SendRatePerSecond + "/sec")}");
        Console.WriteLine($"    Recipients  : {list.Count:N0}"
                          + (list.SkippedRows > 0 ? $" ({list.SkippedRows} skipped, no email)" : ""));
        Console.WriteLine($"    Subject     : {template.Subject}");

        if (args.Flag("dry-run"))
        {
            ConsoleEx.Info("Dry run: showing the first merged message, no email will be sent.");
            var sample = PlaceholderMerger.Merge(template, list.Recipients[0]);
            ConsoleEx.Heading($"Preview → {sample.ToEmail}");
            Console.WriteLine($"    Subject: {sample.Subject}");
            Console.WriteLine();
            Console.WriteLine(PlaceholderMerger.HtmlToPlainText(sample.HtmlBody));
            return 0;
        }

        // Validation only gates the real send — a dry run above never touches the transport.
        if (!ReportValidation(config))
            return 2;

        if (!args.Flag("yes") && !ConsoleEx.Confirm($"\nSend to {list.Count:N0} recipient(s)?"))
        {
            ConsoleEx.Warn("Cancelled.");
            return 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var quiet = args.Flag("quiet");
        var progress = new Progress<SendProgress>(p =>
        {
            if (quiet) return;
            Console.Write($"\r  Sending {p.Processed:N0}/{p.Total:N0}  " +
                          $"(ok {p.Succeeded:N0}, failed {p.Failed:N0})   ");
        });

        Console.WriteLine();
        var campaign = new EmailCampaign(config);
        var summary = await campaign.SendAsync(template, list.Recipients, progress, cts.Token);
        Console.WriteLine();
        Console.WriteLine();

        // Show failures explicitly.
        foreach (var fail in summary.Results.Where(x => !x.Success))
            ConsoleEx.Error($"{fail.ToEmail}: {fail.Error}");

        if (summary.Cancelled)
            ConsoleEx.Warn($"Cancelled. {summary.Succeeded:N0} sent, {summary.Failed:N0} failed before stopping.");
        else if (summary.Failed == 0)
            ConsoleEx.Success($"Done. {summary.Succeeded:N0} sent, 0 failed.");
        else
            ConsoleEx.Warn($"Done with errors. {summary.Succeeded:N0} sent, {summary.Failed:N0} failed.");

        return summary.Failed == 0 && !summary.Cancelled ? 0 : 1;
    }

    public static async Task<int> Test(Arguments args)
    {
        var config = LoadConfig(args);
        if (!ReportValidation(config))
            return 2;

        var to = args.Get("to")
                 ?? throw new ArgumentException("A destination is required: pass --to <email>.");
        var template = BuildTemplate(args);

        Recipient? sample = null;
        if (args.Get("sample-from") is { } sampleFile && File.Exists(sampleFile))
        {
            var list = RecipientImporterFactory.ImportFile(sampleFile);
            sample = list.Recipients.FirstOrDefault();
        }

        ConsoleEx.Info($"Sending test email to {to} via {config.Provider}…");
        var campaign = new EmailCampaign(config);
        var result = await campaign.SendTestAsync(to, template, sample);

        if (result.Success)
        {
            ConsoleEx.Success($"Test sent to {to}."
                              + (result.MessageId is null ? "" : $" (id: {result.MessageId})"));
            return 0;
        }

        ConsoleEx.Error($"Test failed: {result.Error}");
        return 1;
    }

    public static async Task<int> TestConnection(Arguments args)
    {
        var config = LoadConfig(args);
        if (!ReportValidation(config))
            return 2;

        ConsoleEx.Info($"Testing {config.Provider} connectivity…");
        var campaign = new EmailCampaign(config);
        var error = await campaign.TestConnectionAsync();

        if (error is null)
        {
            ConsoleEx.Success("Connection succeeded. Credentials and transport look good.");
            return 0;
        }

        ConsoleEx.Error($"Connection failed: {error}");
        return 1;
    }

    public static int Preview(Arguments args)
    {
        var template = BuildTemplate(args);
        var list = LoadRecipients(args);

        var count = int.TryParse(args.Get("count"), out var c) ? c : 3;
        var previews = list.Recipients.Take(count).ToList();

        if (previews.Count == 0)
        {
            ConsoleEx.Warn("No recipients to preview.");
            return 1;
        }

        var outDir = args.Get("out");
        if (outDir is not null)
            Directory.CreateDirectory(outDir);

        var index = 0;
        foreach (var recipient in previews)
        {
            index++;
            var merged = PlaceholderMerger.Merge(template, recipient);

            if (outDir is not null)
            {
                var file = Path.Combine(outDir, $"preview-{index:D3}.html");
                File.WriteAllText(file, merged.HtmlBody);
                ConsoleEx.Success($"{recipient.Email} → {file}");
            }
            else
            {
                ConsoleEx.Heading($"[{index}] To: {merged.ToEmail}");
                Console.WriteLine($"    Subject: {merged.Subject}");
                Console.WriteLine();
                Console.WriteLine(PlaceholderMerger.HtmlToPlainText(merged.HtmlBody));
            }
        }

        return 0;
    }

    public static int Validate(Arguments args)
    {
        var config = LoadConfig(args);
        if (ReportValidation(config))
        {
            ConsoleEx.Success("Configuration is valid.");
            return 0;
        }
        return 2;
    }

    public static int SaveConfig(Arguments args)
    {
        var path = args.Get("out")
                   ?? throw new ArgumentException("An output file is required: pass --out <path>.");

        var config = LoadConfig(args);
        ConfigurationLoader.SaveToFile(config, path);

        ConsoleEx.Success($"Configuration written to {Path.GetFullPath(path)}");
        if (!string.IsNullOrEmpty(config.Smtp.Password) || !string.IsNullOrEmpty(config.Aws.SecretAccessKey))
            Console.WriteLine("    Note: the file contains credentials in plain text. Store it accordingly.");
        return 0;
    }

    public static int ShowConfig(Arguments args)
    {
        var config = LoadConfig(args);
        var path = ConfigurationLoader.ResolveFilePath(args.Get("config"));

        ConsoleEx.Heading("Resolved configuration");
        Console.WriteLine($"    Source file : {(path is not null && File.Exists(path) ? path : "(none — using defaults/env)")}");
        Console.WriteLine($"    Provider    : {config.Provider}");
        Console.WriteLine($"    Send rate   : {(config.IsUnlimitedRate ? "unlimited" : config.SendRatePerSecond.ToString(CultureInfo.InvariantCulture) + "/sec")}");
        Console.WriteLine($"    From name   : {config.FromName}");
        Console.WriteLine($"    From email  : {config.FromEmail}");
        Console.WriteLine($"    Reply-To    : {config.ReplyToEmail ?? "(from address)"}");
        Console.WriteLine();
        Console.WriteLine("    SMTP");
        Console.WriteLine($"      Host      : {config.Smtp.Host}");
        Console.WriteLine($"      Port      : {config.Smtp.Port}");
        Console.WriteLine($"      Security  : {config.Smtp.Security}");
        Console.WriteLine($"      Username  : {config.Smtp.Username}");
        Console.WriteLine($"      Password  : {Redact(config.Smtp.Password)}");
        Console.WriteLine();
        Console.WriteLine("    AWS SES");
        Console.WriteLine($"      Region    : {config.Aws.Region}");
        Console.WriteLine($"      Auth mode : {config.Aws.AuthMode}");
        Console.WriteLine($"      Profile   : {config.Aws.Profile}");
        Console.WriteLine($"      Access key: {Redact(config.Aws.AccessKeyId)}");
        Console.WriteLine($"      Secret    : {Redact(config.Aws.SecretAccessKey)}");
        return 0;
    }

    private static string Redact(string? secret) =>
        string.IsNullOrEmpty(secret) ? "(not set)" : "•••••• (set)";
}
