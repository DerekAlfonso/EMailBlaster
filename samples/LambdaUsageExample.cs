// ---------------------------------------------------------------------------
// Example: reusing EmailBlaster.Core inside an AWS Lambda function.
//
// This file is illustrative (not compiled as part of the solution). It shows how
// the same engine that powers the desktop app is driven headlessly. In Lambda:
//   * Configuration comes from environment variables (EMAILBLASTER_*), so no JSON
//     file needs to be deployed.
//   * The default AWS credential chain (the Lambda execution role) is used simply
//     by leaving the AWS profile blank in Profile mode.
//
// Suggested Lambda environment variables:
//   EMAILBLASTER_PROVIDER            = Aws
//   EMAILBLASTER_AWS_REGION          = us-east-1
//   EMAILBLASTER_AWS_AUTH_MODE       = Profile      (blank profile -> execution role)
//   EMAILBLASTER_FROM_NAME           = Your Company
//   EMAILBLASTER_FROM_EMAIL          = hello@yourdomain.com
//   EMAILBLASTER_SEND_RATE_PER_SECOND= 14           (stay under your SES quota)
// ---------------------------------------------------------------------------

using EmailBlaster.Core;
using EmailBlaster.Core.Models;

public sealed class BulkEmailRequest
{
    public string Subject { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public List<RecipientDto> Recipients { get; set; } = new();
}

public sealed class RecipientDto
{
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, string>? Fields { get; set; }
}

public sealed class BulkEmailResponse
{
    public int Sent { get; set; }
    public int Failed { get; set; }
}

public class Function
{
    /// <summary>Lambda handler: sends a personalised campaign using env-var configuration.</summary>
    public async Task<BulkEmailResponse> Handler(BulkEmailRequest request)
    {
        // 1. Configuration is read entirely from environment variables.
        var config = ConfigurationLoader.LoadFromEnvironment();

        // 2. Build the template and recipients from the incoming payload.
        var template = new EmailTemplate
        {
            Subject = request.Subject,
            HtmlBody = request.HtmlBody
        };

        var recipients = request.Recipients.Select(r =>
        {
            var recipient = new Recipient { Email = r.Email, Name = r.Name };
            if (r.Fields is not null)
                foreach (var kvp in r.Fields)
                    recipient.Fields[kvp.Key] = kvp.Value;
            return recipient;
        }).ToList();

        // 3. Send. The same EmailCampaign the desktop app uses, no UI dependencies.
        var campaign = new EmailCampaign(config);
        var summary = await campaign.SendAsync(template, recipients);

        return new BulkEmailResponse { Sent = summary.Succeeded, Failed = summary.Failed };
    }
}
