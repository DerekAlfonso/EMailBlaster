# Email Blaster

A C# email / messaging application built as a **reusable core library** with **three front-ends that
share it** — a Windows desktop app (WPF), a command-line tool, and a web app (ASP.NET Core). Send
personalised bulk email through standard SMTP or the AWS SES API, with recipient import, HTML
composition, merge previews and rate-limited delivery.

Because every front-end drives the same `EmailBlaster.Core` engine, delivery, import, merge and
rate-limiting behave identically everywhere. See [Feature parity](#feature-parity) below.

## Solution layout

```
EmailBlaster.slnx
├── src/
│   ├── EmailBlaster.Core/          Reusable engine (net8.0) — no UI dependencies
│   │   ├── Configuration/          Config models (SMTP, AWS, provider, rate, identity)
│   │   ├── Import/                 CSV + XLSX recipient importers, header detection
│   │   ├── Templating/             {{Placeholder}} merge engine
│   │   ├── Delivery/               IEmailSender + SMTP (MailKit) & SES (AWS SDK v2)
│   │   ├── ConfigurationLoader.cs  JSON-file + environment-variable loading
│   │   ├── RateLimiter.cs          0.5/sec → unlimited pacing
│   │   └── EmailCampaign.cs        Orchestrates merge + send + progress (library entry point)
│   ├── EmailBlaster.Desktop/       WPF desktop app (net8.0-windows)   → src/EmailBlaster.Desktop
│   │   ├── Views/                  Configuration, Recipients, Compose, Preview, Send
│   │   ├── Editor/                 Self-contained WebView2 rich HTML editor
│   │   └── Themes/                 Modern flat design system
│   ├── EmailBlaster.Cli/           Command-line front-end (net8.0)    → README in src/EmailBlaster.Cli
│   └── EmailBlaster.Web/           ASP.NET Core web front-end (net8.0)→ README in src/EmailBlaster.Web
│       └── wwwroot/                Single-page UI (HTML/CSS/JS)
└── samples/
    ├── recipients-sample.csv       Example dataset
    └── LambdaUsageExample.cs       How to reuse the core library in AWS Lambda
```

## Front-ends

| Front-end | Project | How to run | Docs |
|---|---|---|---|
| Windows desktop (WPF) | `EmailBlaster.Desktop` | `dotnet run --project src/EmailBlaster.Desktop` | this file |
| Command line | `EmailBlaster.Cli` | `dotnet run --project src/EmailBlaster.Cli -- help` | [CLI README](src/EmailBlaster.Cli/README.md) |
| Web (browser) | `EmailBlaster.Web` | `dotnet run --project src/EmailBlaster.Web` | [Web README](src/EmailBlaster.Web/README.md) |

## Requirements coverage

| Requirement | Where |
|---|---|
| Reusable library, Lambda-adaptable | `EmailBlaster.Core` targets `net8.0`, zero UI deps; see `samples/LambdaUsageExample.cs` |
| SMTP **or** AWS SES delivery | `Delivery/SmtpEmailSender.cs`, `Delivery/SesEmailSender.cs`, selected via `Provider` |
| CSV / XLSX recipient import (email + name + extra columns) | `Import/CsvRecipientImporter.cs`, `Import/XlsxRecipientImporter.cs` |
| Well-designed Windows front-end | `EmailBlaster.Desktop` (WPF, sidebar nav, cards, custom theme) |
| Config from JSON file **or** environment variables | `ConfigurationLoader.cs` |
| Send rate 0.5 → unlimited | `SendRatePerSecond` (0 = unlimited) + `RateLimiter.cs` |
| SMTP server + auth | `SmtpConfig` |
| SMTP vs AWS toggle | `Provider` enum |
| AWS profile **OR** access key/secret | `AwsConfig.AuthMode` |
| From name / From email / Reply-To | `EmailBlasterConfig` |
| Rich HTML editing + placeholder insertion | `Views/ComposeView`, `Editor/HtmlEditorDocument.cs` |
| Merge preview | `Views/PreviewView` |
| Config UI with test email send | `Views/ConfigurationView` (Test connection + Send test) |

## Feature parity

All three front-ends are peers over the same engine and are expected to stay in **feature parity**.
When adding or changing a feature, implement it in `EmailBlaster.Core` where possible, then surface it
in **all three** front-ends and update this matrix.

| Capability | Core | Desktop | CLI | Web |
|---|:--:|:--:|:--:|:--:|
| Load config (JSON file) | ✅ | ✅ | ✅ | ✅ |
| Load config (env vars) | ✅ | ✅ | ✅ | ✅ |
| Edit & save config | — | ✅ | ➖¹ | ✅ |
| SMTP provider | ✅ | ✅ | ✅ | ✅ |
| AWS SES provider | ✅ | ✅ | ✅ | ✅ |
| AWS profile / access-key auth | ✅ | ✅ | ✅ | ✅ |
| Send rate 0.5 → unlimited | ✅ | ✅ | ✅ | ✅ |
| Import CSV | ✅ | ✅ | ✅ | ✅ |
| Import XLSX | ✅ | ✅ | ✅ | ✅ |
| Placeholder merge (`{{Col}}`, defaults) | ✅ | ✅ | ✅ | ✅ |
| Rich HTML editor | — | ✅ (WebView2) | ➖² | ✅ (contenteditable) |
| HTML source editing | — | ✅ | ✅ (`--html-file`) | ✅ |
| Insert placeholders into body | — | ✅ | ➖² | ✅ |
| Merge preview | ✅ | ✅ | ✅ (`preview`) | ✅ |
| Test connection | ✅ | ✅ | ✅ | ✅ |
| Send test email | ✅ | ✅ | ✅ | ✅ |
| Bulk send with live progress | ✅ | ✅ | ✅ | ✅ |
| Cancel in-flight send | ✅ | ✅ | ✅ (Ctrl+C) | ✅ |
| Dark / light theme (dark default, persisted) | — | ✅ | ➖³ | ✅ |

✅ supported · ➖ intentional platform difference · — not applicable (engine has no UI)

¹ The CLI edits config by editing `emailblaster.json` / env vars directly; `config` prints the
resolved settings (secrets redacted). ² The CLI composes via `--html` / `--html-file`; placeholders
are typed directly into the HTML rather than inserted through a UI. ³ A CLI has no chrome to theme —
its colours come from the terminal; `NO_COLOR` / `--no-color` disables ANSI colour.

### Theming

Both GUI front-ends default to **dark** and remember the user's choice:

- **Desktop** — a sidebar toggle swaps the palette live (`ThemeManager` swaps
  `Themes/Palette.{Dark,Light}.xaml`); the choice is stored in
  `%APPDATA%\EmailBlaster\preferences.json`.
- **Web** — a sidebar toggle flips `data-theme` on `<html>` (CSS variables in `wwwroot/styles.css`);
  the choice is stored in the browser's `localStorage` under `emailblaster-theme`. An inline
  bootstrap script applies the saved theme before first paint to avoid a flash.

In both, the email canvas (compose editor + preview) stays light on purpose — it is a true WYSIWYG of
the email, which recipients read on a light background.

**Parity checklist for new work:** add the behaviour to `EmailBlaster.Core` → wire it into Desktop,
CLI and Web → update this matrix → extend the Core smoke checks if the behaviour is testable headlessly.

## Configuration

The engine reads settings in this order (later overrides earlier):

1. `emailblaster.json` beside the executable (or a path in `EMAILBLASTER_CONFIG_PATH`).
2. Environment variables prefixed `EMAILBLASTER_`.

This lets a JSON file hold defaults while secrets are injected via the environment — ideal for Lambda.

### JSON example (`emailblaster.json`)

```json
{
  "SendRatePerSecond": 5,
  "Provider": "Smtp",
  "FromName": "Your Company",
  "FromEmail": "hello@example.com",
  "ReplyToEmail": null,
  "Smtp": {
    "Host": "smtp.example.com",
    "Port": 587,
    "Security": "StartTls",
    "Username": "apikey",
    "Password": "••••••"
  },
  "Aws": {
    "Region": "us-east-1",
    "AuthMode": "Profile",
    "Profile": "",
    "AccessKeyId": "",
    "SecretAccessKey": ""
  }
}
```

`SendRatePerSecond`: `0` (or negative) means **unlimited**; otherwise the minimum is `0.5`.
`Security`: `None` | `StartTls` (587) | `SslOnConnect` (465) | `Auto`.
`Provider`: `Smtp` | `Aws`. `AuthMode`: `Profile` | `AccessKey`.

### Environment variables

```
EMAILBLASTER_SEND_RATE_PER_SECOND   EMAILBLASTER_PROVIDER (Smtp|Aws)
EMAILBLASTER_FROM_NAME              EMAILBLASTER_FROM_EMAIL           EMAILBLASTER_REPLY_TO_EMAIL
EMAILBLASTER_SMTP_HOST              EMAILBLASTER_SMTP_PORT            EMAILBLASTER_SMTP_SECURITY
EMAILBLASTER_SMTP_USERNAME          EMAILBLASTER_SMTP_PASSWORD
EMAILBLASTER_AWS_REGION             EMAILBLASTER_AWS_AUTH_MODE (Profile|AccessKey)
EMAILBLASTER_AWS_PROFILE            EMAILBLASTER_AWS_ACCESS_KEY_ID   EMAILBLASTER_AWS_SECRET_ACCESS_KEY
EMAILBLASTER_AWS_SESSION_TOKEN      EMAILBLASTER_AWS_CONFIGURATION_SET
EMAILBLASTER_CONFIG_PATH            (override the JSON file location)
```

## Placeholders

Reference any imported column with `{{ColumnName}}`. `{{Name}}` and `{{Email}}` always resolve.
Provide a fallback with a pipe: `{{Name|there}}` renders `there` when the field is empty.

## Build & run

```bash
# Build everything (solution is EmailBlaster.slnx — the .NET solution format)
dotnet build EmailBlaster.slnx

# Windows desktop app
dotnet run --project src/EmailBlaster.Desktop

# Command-line tool
dotnet run --project src/EmailBlaster.Cli -- help

# Web app (then open the printed http://localhost:5xxx URL)
dotnet run --project src/EmailBlaster.Web
```

**Prerequisites:** .NET 8+ SDK. The desktop app additionally needs the Microsoft Edge WebView2
Runtime, which ships with Windows 11 (if missing, Compose falls back to a raw HTML source editor).
The CLI and Web front-ends have no extra runtime requirements and are cross-platform.

## Using the library directly

```csharp
var config = ConfigurationLoader.Load();               // JSON + env vars
var recipients = RecipientImporterFactory.ImportFile("audience.xlsx");
var template = new EmailTemplate {
    Subject = "Hi {{Name|there}}",
    HtmlBody = "<p>Hello {{Name}}, welcome to {{Company}}.</p>"
};

var campaign = new EmailCampaign(config);
var summary = await campaign.SendAsync(
    template,
    recipients.Recipients,
    new Progress<SendProgress>(p => Console.WriteLine($"{p.Processed}/{p.Total}")));

Console.WriteLine($"{summary.Succeeded} sent, {summary.Failed} failed");
```
