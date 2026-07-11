# Email Blaster Web

A browser-based front-end for the Email Blaster engine, built on ASP.NET Core. It shares the exact
same [`EmailBlaster.Core`](../EmailBlaster.Core) library as the CLI and Windows desktop front-ends,
so delivery, import, merge and rate-limiting behave identically everywhere.

## Build & run

```bash
# From the repository root
dotnet run --project src/EmailBlaster.Web
```

Then open the printed URL (typically `http://localhost:5xxx`). The single-page UI mirrors the desktop
tabs: **Configuration → Recipients → Compose → Preview → Send**.

## What it does

- **Configuration** — edit sender identity, provider (SMTP or AWS SES), send rate, SMTP settings and
  AWS credentials. Includes **Test connection** and **Send test email**. Saving writes to
  `emailblaster.json` beside the app.
- **Recipients** — upload a CSV or XLSX; the server parses it with the shared importers and returns a
  preview table plus the placeholder columns.
- **Compose** — a rich contenteditable HTML editor (bold/italic/lists/headings/links/colours) with
  clickable placeholder chips and an HTML-source toggle.
- **Preview** — renders each merged message in an iframe using the server's merge engine (so previews
  match exactly what will be sent).
- **Send** — runs the campaign as a background job and polls live progress (sent/failed counts, a
  progress bar and a failure log), with cancel support.
- **Theme** — a sidebar toggle switches between dark (default) and light. The choice is saved to the
  browser's `localStorage` (`emailblaster-theme`) and applied before first paint to avoid a flash.
  The email canvas stays light on purpose (true-to-recipient preview).

## HTTP API

The UI is a thin client over a small JSON API (useful for automation too):

| Method & path | Purpose |
|---|---|
| `GET /api/config` | Current config (secrets redacted). |
| `POST /api/config` | Save config (blank secret fields keep the stored value). |
| `POST /api/recipients` | Upload a CSV/XLSX (multipart `file`); returns summary + preview rows. |
| `GET /api/recipients` | Current recipient summary. |
| `POST /api/recipients/clear` | Clear the loaded recipients. |
| `GET` / `POST /api/template` | Get / set the working subject + HTML body. |
| `POST /api/preview` | `{ index }` → merged `{ to, subject, html }`. |
| `POST /api/test-connection` | Verify transport; `{ ok, error }`. |
| `POST /api/test` | `{ to }` → send one test; `{ success, messageId, error }`. |
| `POST /api/send` | Start a background send; `{ jobId, total }`. |
| `GET /api/send/{jobId}` | Live job progress. |
| `POST /api/send/{jobId}/cancel` | Cancel a running job. |

## Configuration

Configuration resolves in the same order as every front-end: `emailblaster.json` beside the app
(overridable via `EMAILBLASTER_CONFIG_PATH`) then `EMAILBLASTER_*` environment variables. See the
[root README](../../README.md) for the full schema and variable list.

## Deployment notes

- State (config, recipients, template) is held **in memory as a singleton** — this is designed as a
  **single-user local tool**. For a shared/multi-tenant deployment, move `AppSession` to a per-user
  store and put the app behind authentication.
- The API accepts credentials over the wire when saving config. Run it over `localhost` or behind TLS
  and authentication; do not expose it publicly as-is.

## Feature parity

The Web app is one of three front-ends (CLI, Windows desktop, Web) that must stay in parity. See the
[Feature parity matrix](../../README.md#feature-parity) in the root README before adding features.
