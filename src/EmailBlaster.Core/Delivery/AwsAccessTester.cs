using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using EmailBlaster.Core.Configuration;

namespace EmailBlaster.Core.Delivery;

/// <summary>Categorizes what went wrong (or right) when probing AWS SES send access.</summary>
public enum AwsAccessProblem
{
    /// <summary>No problem: the SendEmail call succeeded.</summary>
    None = 0,

    /// <summary>The configured named profile does not exist in the shared credentials store.</summary>
    ProfileNotFound,

    /// <summary>The profile is an SSO profile whose cached token is missing or expired.</summary>
    SsoLoginRequired,

    /// <summary>AWS rejected the credentials themselves (bad key id, bad secret, unknown client).</summary>
    InvalidCredentials,

    /// <summary>The credentials were valid once but the session/token has expired.</summary>
    ExpiredCredentials,

    /// <summary>Credentials are valid but lack permission to call ses:SendEmail.</summary>
    SendAccessDenied,

    /// <summary>SendEmail works, but the From identity is not verified in SES.</summary>
    IdentityNotVerified,

    /// <summary>The AWS endpoint could not be reached (network / DNS / proxy).</summary>
    Network,

    /// <summary>
    /// No usable credentials anywhere: nothing entered, no profile selected, and the default
    /// credential chain (default profile, environment, instance roles) produced nothing.
    /// </summary>
    NoCredentials,

    /// <summary>Anything that did not match a known category.</summary>
    Unknown
}

/// <summary>Outcome of an AWS access test or an SSO login attempt.</summary>
public sealed class AwsAccessTestResult
{
    public bool Success { get; init; }
    public AwsAccessProblem Problem { get; init; }
    public string Message { get; init; } = string.Empty;

    /// <summary>True when running <c>aws sso login</c> is the fix, so UIs can offer to do it.</summary>
    public bool CanAttemptSsoLogin => Problem == AwsAccessProblem.SsoLoginRequired;

    public static AwsAccessTestResult Ok(string message) =>
        new() { Success = true, Problem = AwsAccessProblem.None, Message = message };

    public static AwsAccessTestResult Fail(AwsAccessProblem problem, string message) =>
        new() { Success = false, Problem = problem, Message = message };
}

/// <summary>
/// Verifies that the configured AWS credentials can actually call the SES v2 SendEmail API, by
/// sending to the SES mailbox simulator (a real API call that never delivers mail to a real inbox).
/// Failures are translated into user-friendly categories, including detecting SSO profiles whose
/// token needs a fresh <c>aws sso login</c>.
/// </summary>
public static class AwsAccessTester
{
    /// <summary>SES-provided address that accepts mail and discards it; nothing is delivered.</summary>
    public const string SimulatorAddress = "success@simulator.amazonses.com";

    public static async Task<AwsAccessTestResult> TestSendAccessAsync(
        EmailBlasterConfig config, CancellationToken cancellationToken = default)
    {
        var aws = config.Aws;
        var isSso = false;

        if (aws.AuthMode == AwsAuthMode.Profile && !string.IsNullOrWhiteSpace(aws.Profile))
        {
            var chain = new CredentialProfileStoreChain();
            if (!chain.TryGetProfile(aws.Profile, out var profile))
            {
                return AwsAccessTestResult.Fail(AwsAccessProblem.ProfileNotFound,
                    $"AWS profile '{aws.Profile}' was not found in the shared credentials store " +
                    "(~/.aws/credentials or ~/.aws/config). Check the spelling or configure the profile first.");
            }
            isSso = IsSsoProfile(profile);
        }

        var fromEmail = string.IsNullOrWhiteSpace(config.FromEmail)
            ? "emailblaster-access-test@example.com"
            : config.FromEmail;

        try
        {
            using var client = SesClientFactory.Create(aws);
            var request = new SendEmailRequest
            {
                FromEmailAddress = fromEmail,
                Destination = new Destination { ToAddresses = new List<string> { SimulatorAddress } },
                Content = new EmailContent
                {
                    Simple = new Message
                    {
                        Subject = new Content { Data = "Email Blaster AWS access test", Charset = "UTF-8" },
                        Body = new Body
                        {
                            Text = new Content
                            {
                                Data = "This message verifies SES send access. It was sent to the SES " +
                                       "mailbox simulator and was not delivered to anyone.",
                                Charset = "UTF-8"
                            }
                        }
                    }
                }
            };

            await client.SendEmailAsync(request, cancellationToken).ConfigureAwait(false);

            return AwsAccessTestResult.Ok(
                $"AWS access verified. ses:SendEmail succeeded in {aws.Region} as '{fromEmail}' " +
                "(the test went to the SES mailbox simulator; no real email was delivered).");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Classify(ex, aws, fromEmail, isSso);
        }
    }

    /// <summary>True when neither explicit keys nor a named profile are set, so the SDK's default
    /// credential chain (default profile, environment variables, instance roles) is in play.</summary>
    public static bool UsesDefaultChain(AwsConfig aws) =>
        aws.AuthMode == AwsAuthMode.Profile && string.IsNullOrWhiteSpace(aws.Profile);

    /// <summary>
    /// Maps an exception from an AWS probe to a user-friendly result. <paramref name="operation"/>
    /// names the IAM action being attempted (e.g. <c>ses:SendEmail</c>) so permission messages are
    /// specific. <paramref name="defaultProfileExists"/> lets tests pin the default-profile lookup;
    /// when null it is read from the shared credentials store. Public so the mapping rules are
    /// unit-testable without calling AWS.
    /// </summary>
    public static AwsAccessTestResult Classify(Exception ex, AwsConfig aws, string fromEmail, bool isSsoProfile,
        string operation = "ses:SendEmail", bool? defaultProfileExists = null)
    {
        var profileHint = string.IsNullOrWhiteSpace(aws.Profile) ? "the profile" : $"profile '{aws.Profile}'";

        // Default-chain cases: distinguish "nothing to authenticate with at all" from "a default
        // profile exists but is broken". (Permission problems are handled further down: they mean
        // the chain produced working credentials.)
        if (UsesDefaultChain(aws) && (IsCredentialResolutionFailure(ex) || IsCredentialRejection(ex)))
        {
            var hasDefaultProfile = defaultProfileExists ?? DefaultChainProfileExists();

            if (!hasDefaultProfile)
            {
                return AwsAccessTestResult.Fail(AwsAccessProblem.NoCredentials,
                    "No AWS credentials or profile supplied and no default profile exists. Enter an access " +
                    "key pair, choose a named profile, or configure a default profile in ~/.aws/credentials.");
            }

            return AwsAccessTestResult.Fail(AwsAccessProblem.InvalidCredentials,
                "No credentials or profile were supplied, so the default profile was used — but it did not " +
                "produce working credentials. Its stored credentials appear to be invalid, revoked, or expired.");
        }

        // The factory's SSO callback aborts with this marker when an interactive sign-in would be
        // needed; the SDK may surface it directly or wrapped, so walk the inner-exception chain.
        for (Exception? walk = ex; walk is not null; walk = walk.InnerException)
        {
            // The marker's own message adds nothing beyond the friendly text, so no detail suffix.
            if (walk is SsoSignInRequiredException && !string.IsNullOrWhiteSpace(aws.Profile))
                return SsoLoginNeeded(aws.Profile!);
        }

        switch (ex)
        {
            case MessageRejectedException rejected when Mentions(rejected.Message, "not verified"):
                return AwsAccessTestResult.Fail(AwsAccessProblem.IdentityNotVerified,
                    $"Credentials and the ses:SendEmail permission are working, but the sender identity " +
                    $"'{fromEmail}' is not verified in SES region {aws.Region}. Verify that email address " +
                    "(or its domain) in the SES console, or change the From email to a verified identity.");

            case MessageRejectedException rejected:
                return AwsAccessTestResult.Fail(AwsAccessProblem.Unknown,
                    $"ses:SendEmail is callable with these credentials, but SES rejected the test message: {rejected.Message}");

            case AccountSuspendedException:
                return AwsAccessTestResult.Fail(AwsAccessProblem.Unknown,
                    "Credentials and permissions are working, but sending is suspended for this AWS account. " +
                    "Check the account's SES sending status in the AWS console.");

            case SendingPausedException:
                return AwsAccessTestResult.Fail(AwsAccessProblem.Unknown,
                    "Credentials and permissions are working, but SES sending is currently paused for this account.");

            case AmazonServiceException service:
                return ClassifyServiceException(service, aws, isSsoProfile, profileHint, operation);

            // Client-side failures: credential resolution happens here, which is where SSO tokens fail.
            case AmazonClientException client when isSsoProfile:
                return SsoLoginNeeded(aws.Profile!, client.Message);

            case AmazonClientException client when Mentions(client.Message, "sso"):
                return AwsAccessTestResult.Fail(AwsAccessProblem.SsoLoginRequired,
                    $"The SSO token for {profileHint} is missing or expired: {client.Message}");

            case HttpRequestException or WebException:
                return AwsAccessTestResult.Fail(AwsAccessProblem.Network,
                    $"Could not reach the SES endpoint for region {aws.Region}. Check the network connection, " +
                    $"proxy settings, and that '{aws.Region}' is a valid AWS region.");

            case InvalidOperationException op when Mentions(op.Message, "was not found in the shared credentials store"):
                return AwsAccessTestResult.Fail(AwsAccessProblem.ProfileNotFound, op.Message);

            default:
                return AwsAccessTestResult.Fail(AwsAccessProblem.Unknown, $"AWS access test failed: {ex.Message}");
        }
    }

    private static AwsAccessTestResult ClassifyServiceException(
        AmazonServiceException ex, AwsConfig aws, bool isSsoProfile, string profileHint, string operation)
    {
        var code = ex.ErrorCode ?? string.Empty;

        if (CredentialErrorCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
        {
            return AwsAccessTestResult.Fail(AwsAccessProblem.InvalidCredentials,
                aws.AuthMode == AwsAuthMode.AccessKey
                    ? "AWS rejected the access key credentials. Check the access key id and secret access key " +
                      "(and the session token, if one is required)."
                    : $"AWS rejected the credentials resolved from {profileHint}. The stored credentials appear " +
                      "to be invalid or revoked.");
        }

        if (code is "ExpiredToken" or "ExpiredTokenException" or "TokenRefreshRequired" or "RequestExpired")
        {
            return isSsoProfile
                ? SsoLoginNeeded(aws.Profile!, ex.Message)
                : AwsAccessTestResult.Fail(AwsAccessProblem.ExpiredCredentials,
                    "The AWS credentials have expired. Refresh the session token (or re-run the process that " +
                    "issues the temporary credentials) and try again.");
        }

        if (ex.StatusCode == HttpStatusCode.Forbidden || code.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase))
        {
            // The requirement's "default profile lacks the permission" case gets its own wording.
            return AwsAccessTestResult.Fail(AwsAccessProblem.SendAccessDenied,
                UsesDefaultChain(aws)
                    ? $"The default profile does not have the {operation} permission in region {aws.Region}. " +
                      "Ask your AWS administrator to grant it, or switch to credentials that have it."
                    : $"The credentials are valid, but they are not authorized to call {operation} in region " +
                      $"{aws.Region}. Ask your AWS administrator to grant the {operation} action to this identity.");
        }

        return AwsAccessTestResult.Fail(AwsAccessProblem.Unknown,
            $"AWS returned an unexpected error ({(string.IsNullOrEmpty(code) ? ex.StatusCode.ToString() : code)}): {ex.Message}");
    }

    private static AwsAccessTestResult SsoLoginNeeded(string profile, string? detail = null) =>
        AwsAccessTestResult.Fail(AwsAccessProblem.SsoLoginRequired,
            $"Profile '{profile}' uses AWS IAM Identity Center (SSO) and its sign-in session is missing or " +
            $"expired. Sign in with:  aws sso login --profile {profile}" +
            (string.IsNullOrWhiteSpace(detail) ? "" : $"  ({Shorten(detail)})"));

    /// <summary>
    /// Runs <c>aws sso login --profile &lt;name&gt;</c> via the AWS CLI, which opens the system browser
    /// for the Identity Center sign-in. Returns once the CLI reports success or failure.
    /// </summary>
    public static async Task<AwsAccessTestResult> RunSsoLoginAsync(
        string profileName, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "aws",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("sso");
        psi.ArgumentList.Add("login");
        psi.ArgumentList.Add("--profile");
        psi.ArgumentList.Add(profileName);

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception)
        {
            return AwsAccessTestResult.Fail(AwsAccessProblem.SsoLoginRequired,
                "The AWS CLI ('aws') was not found on PATH, so the app cannot start the SSO sign-in. " +
                $"Install AWS CLI v2, or run 'aws sso login --profile {profileName}' in a terminal yourself.");
        }

        if (process is null)
            return AwsAccessTestResult.Fail(AwsAccessProblem.SsoLoginRequired, "Could not start the AWS CLI.");

        using (process)
        {
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            _ = process.StandardOutput.ReadToEndAsync(cancellationToken);

            // The CLI waits for the user to finish signing in via the browser; cap the wait.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                if (cancellationToken.IsCancellationRequested)
                    throw;
                return AwsAccessTestResult.Fail(AwsAccessProblem.SsoLoginRequired,
                    "Timed out after 5 minutes waiting for the SSO sign-in to complete in the browser.");
            }

            if (process.ExitCode == 0)
                return AwsAccessTestResult.Ok($"SSO sign-in for profile '{profileName}' completed.");

            var stderr = (await stderrTask.ConfigureAwait(false)).Trim();
            return AwsAccessTestResult.Fail(AwsAccessProblem.SsoLoginRequired,
                $"'aws sso login --profile {profileName}' failed" +
                (string.IsNullOrWhiteSpace(stderr) ? $" (exit code {process.ExitCode})." : $": {Shorten(stderr)}"));
        }
    }

    private static bool IsSsoProfile(CredentialProfile profile)
    {
        var options = profile.Options;
        return !string.IsNullOrEmpty(options.SsoStartUrl)
               || !string.IsNullOrEmpty(options.SsoSession)
               || !string.IsNullOrEmpty(options.SsoAccountId);
    }

    /// <summary>
    /// Whether the profile the default chain would use actually exists. Honors the same environment
    /// variables the chain honors (AWS_PROFILE, AWS_SHARED_CREDENTIALS_FILE) — the parameterless
    /// CredentialProfileStoreChain constructor does not, which would make this check disagree with
    /// what credential resolution actually saw.
    /// </summary>
    private static bool DefaultChainProfileExists()
    {
        var profileName = Environment.GetEnvironmentVariable("AWS_PROFILE");
        if (string.IsNullOrWhiteSpace(profileName))
            profileName = "default";

        var location = Environment.GetEnvironmentVariable("AWS_SHARED_CREDENTIALS_FILE");
        var chain = string.IsNullOrWhiteSpace(location)
            ? new CredentialProfileStoreChain()
            : new CredentialProfileStoreChain(location);

        return chain.TryGetProfile(profileName, out _);
    }

    private static readonly string[] CredentialErrorCodes =
    {
        "UnrecognizedClientException", "InvalidClientTokenId", "InvalidAccessKeyId",
        "SignatureDoesNotMatch", "InvalidSignatureException", "MissingAuthenticationToken",
        "IncompleteSignature"
    };

    /// <summary>AWS received the request but rejected the credentials that signed it.</summary>
    private static bool IsCredentialRejection(Exception ex) =>
        ex is AmazonServiceException service &&
        CredentialErrorCodes.Contains(service.ErrorCode ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Heuristics for "the SDK could not produce credentials at all" (as opposed to AWS rejecting
    /// them). These surface with varying types and texts depending on which chain link failed last.
    /// </summary>
    private static bool IsCredentialResolutionFailure(Exception ex)
    {
        for (Exception? walk = ex; walk is not null; walk = walk.InnerException)
        {
            if (Mentions(walk.Message, "unable to get iam security credentials") ||
                Mentions(walk.Message, "unable to find credentials") ||
                Mentions(walk.Message, "failed to retrieve credentials") ||
                Mentions(walk.Message, "instance metadata") ||
                Mentions(walk.Message, "credential profile") ||
                Mentions(walk.Message, "no credentials specified"))
            {
                return true;
            }
        }
        return false;
    }

    private static bool Mentions(string? text, string fragment) =>
        text?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true;

    private static string Shorten(string text)
    {
        var firstLine = text.ReplaceLineEndings("\n").Split('\n', 2)[0].Trim();
        return firstLine.Length <= 200 ? firstLine : firstLine[..200] + "…";
    }
}
