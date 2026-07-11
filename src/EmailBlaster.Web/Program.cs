using EmailBlaster.Core;
using EmailBlaster.Core.Configuration;
using EmailBlaster.Core.Import;
using EmailBlaster.Core.Models;
using EmailBlaster.Core.Templating;
using EmailBlaster.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<AppSession>();
builder.Services.AddSingleton<SendJobManager>();

var app = builder.Build();

app.UseDefaultFiles();   // serve wwwroot/index.html at "/"
app.UseStaticFiles();

// ------------------------------------------------------------------ Config

app.MapGet("/api/config", (AppSession session) =>
{
    var dto = ConfigDto.FromConfig(session.Config);
    return Results.Ok(new { config = dto, savedTo = session.ConfigPath });
});

app.MapPost("/api/config", (ConfigDto dto, AppSession session) =>
{
    dto.ApplyTo(session.Config);
    var errors = session.Config.Validate();
    string? savedTo = null;
    if (errors.Count == 0)
        savedTo = session.SaveConfig();
    return Results.Ok(new { ok = errors.Count == 0, errors, savedTo });
});

// AWS profiles available on the server host, for auto-completing the profile name field.
app.MapGet("/api/aws-profiles", () => Results.Ok(new { profiles = AwsProfileCatalog.ListProfileNames() }));

// ------------------------------------------------------------------ Recipients

app.MapPost("/api/recipients", async (HttpRequest request, AppSession session) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Expected a multipart file upload." });

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "No file was uploaded." });

    var importer = RecipientImporterFactory.ForExtension(Path.GetExtension(file.FileName));
    if (importer is null)
        return Results.BadRequest(new
        {
            error = $"Unsupported file type. Supported: {string.Join(", ", RecipientImporterFactory.SupportedExtensions)}."
        });

    try
    {
        await using var stream = file.OpenReadStream();
        var list = importer.Import(stream);
        session.Recipients = list;
        return Results.Ok(RecipientSummary(list, previewRows: 50));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/recipients", (AppSession session) => Results.Ok(RecipientSummary(session.Recipients, 50)));

app.MapPost("/api/recipients/clear", (AppSession session) =>
{
    session.Recipients = RecipientList.Empty;
    return Results.Ok(RecipientSummary(session.Recipients, 50));
});

// ------------------------------------------------------------------ Template

app.MapGet("/api/template", (AppSession session) => Results.Ok(new TemplateDto
{
    Subject = session.Template.Subject,
    Html = session.Template.HtmlBody,
    Text = session.Template.TextBody
}));

app.MapPost("/api/template", (TemplateDto dto, AppSession session) =>
{
    session.Template.Subject = dto.Subject ?? "";
    session.Template.HtmlBody = dto.Html ?? "";
    session.Template.TextBody = string.IsNullOrWhiteSpace(dto.Text) ? null : dto.Text;
    return Results.Ok(new { ok = true });
});

// ------------------------------------------------------------------ Preview

app.MapPost("/api/preview", (PreviewRequest req, AppSession session) =>
{
    var recipient = ResolvePreviewRecipient(session, req.Index);
    var merged = PlaceholderMerger.Merge(session.Template, recipient);
    return Results.Ok(new
    {
        to = recipient.ToString(),
        subject = merged.Subject,
        html = merged.HtmlBody
    });
});

// ------------------------------------------------------------------ Test / connection

app.MapPost("/api/test-connection", async (AppSession session) =>
{
    var campaign = new EmailCampaign(session.Config);
    var error = await campaign.TestConnectionAsync();
    return Results.Ok(new { ok = error is null, error });
});

app.MapPost("/api/test", async (TestRequest req, AppSession session) =>
{
    if (string.IsNullOrWhiteSpace(req.To))
        return Results.BadRequest(new { error = "A destination address is required." });

    var sample = session.Recipients.Recipients.FirstOrDefault();
    var campaign = new EmailCampaign(session.Config);
    var result = await campaign.SendTestAsync(req.To.Trim(), session.Template, sample);
    return Results.Ok(new { success = result.Success, messageId = result.MessageId, error = result.Error });
});

// ------------------------------------------------------------------ Send (background jobs)

app.MapPost("/api/send", (AppSession session, SendJobManager jobs) =>
{
    var recipients = session.Recipients.Recipients;
    if (recipients.Count == 0)
        return Results.BadRequest(new { error = "Import recipients before sending." });

    var errors = session.Config.Validate();
    if (errors.Count > 0)
        return Results.BadRequest(new { error = "Configuration is invalid.", errors });

    var job = jobs.Start(session.Config, session.Template, recipients);
    return Results.Ok(new { jobId = job.Id, total = job.Total });
});

app.MapGet("/api/send/{jobId}", (string jobId, SendJobManager jobs) =>
{
    var job = jobs.Get(jobId);
    if (job is null)
        return Results.NotFound(new { error = "Unknown job." });

    return Results.Ok(new
    {
        job.Id,
        job.Total,
        processed = job.Processed,
        succeeded = job.Succeeded,
        failed = job.Failed,
        done = job.Done,
        cancelled = job.Cancelled,
        error = job.Error,
        recentErrors = job.RecentErrors(100).Select(e => new { email = e.Email, message = e.Message })
    });
});

app.MapPost("/api/send/{jobId}/cancel", (string jobId, SendJobManager jobs) =>
{
    var ok = jobs.Cancel(jobId);
    return ok ? Results.Ok(new { ok = true }) : Results.NotFound(new { error = "Unknown job." });
});

app.Run();

// ------------------------------------------------------------------ helpers

static object RecipientSummary(RecipientList list, int previewRows)
{
    var rows = list.Recipients.Take(previewRows).Select(r =>
    {
        var dict = new Dictionary<string, string>();
        foreach (var col in list.Columns)
            dict[col] = r.ToMergeFields().TryGetValue(col, out var v) ? v : "";
        return dict;
    });

    return new
    {
        count = list.Count,
        skipped = list.SkippedRows,
        columns = list.Columns,
        rows
    };
}

static Recipient ResolvePreviewRecipient(AppSession session, int index)
{
    var source = session.Recipients.Recipients;
    if (source.Count > 0)
        return source[Math.Clamp(index, 0, source.Count - 1)];

    // No import yet: synthesise a sample so the preview always renders.
    var sample = new Recipient { Name = "Alex Sample", Email = "alex.sample@example.com" };
    foreach (var col in session.Recipients.Columns)
        if (col is not ("Name" or "Email"))
            sample.Fields[col] = $"[{col}]";
    return sample;
}
