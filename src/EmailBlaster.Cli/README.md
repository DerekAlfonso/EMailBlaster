# Email Blaster CLI

A command-line front-end for the Email Blaster engine. It shares the exact same
[`EmailBlaster.Core`](../EmailBlaster.Core) library as the Windows desktop and Web front-ends, so
delivery, import, merge and rate-limiting behave identically everywhere.

## Build & run

```bash
# From the repository root
dotnet build src/EmailBlaster.Cli

# Run (the assembly name is "emailblaster")
dotnet run --project src/EmailBlaster.Cli -- <command> [options]

# Or publish a standalone tool
dotnet publish src/EmailBlaster.Cli -c Release -o ./dist
./dist/emailblaster <command> [options]
```

## Commands

| Command | Purpose |
|---|---|
| `send` | Merge and send a campaign to an imported recipient list. |
| `test` | Send a single test email. |
| `test-connection` | Verify transport connectivity / credentials (no mail sent). |
| `preview` | Render merged messages for the first N recipients (stdout or files). |
| `validate` | Validate the resolved configuration. |
| `config` | Print the resolved configuration with secrets redacted. |
| `help` | Show usage. |

## Configuration

Configuration resolves in the same order as every front-end (later overrides earlier):

1. `emailblaster.json` (beside the executable, or `--config <path>`, or `EMAILBLASTER_CONFIG_PATH`).
2. `EMAILBLASTER_*` environment variables.
3. Command-line overrides: `--provider`, `--rate`, `--from-name`, `--from-email`, `--reply-to`.

See the [root README](../../README.md) for the full config schema and environment-variable list.

## Template options

Used by `send`, `test` and `preview`:

- `--subject <text>` or `--subject-file <path>`
- `--html <text>` or `--html-file <path>`
- `--text-file <path>` (optional plain-text alternative)

Subjects and bodies may contain `{{Column}}` placeholders, including `{{Name|fallback}}` defaults.

## Examples

```bash
# Check settings and connectivity
emailblaster validate
emailblaster test-connection

# Send yourself a test using the current settings
emailblaster test --to me@example.com \
  --subject "Hi {{Name|there}}" \
  --html "<p>Hello from the CLI</p>"

# Preview the first 5 merged messages as HTML files
emailblaster preview --recipients audience.csv \
  --subject "Hi {{Name}}" --html-file body.html \
  --count 5 --out ./previews

# Dry-run a campaign (merges, shows the first message, sends nothing)
emailblaster send --recipients audience.xlsx \
  --subject "Hi {{Name}}" --html-file body.html --dry-run

# Send for real, non-interactively, capped at 3 messages/second
emailblaster send --recipients audience.xlsx \
  --subject "Hi {{Name}}" --html-file body.html \
  --rate 3 --yes
```

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (all messages sent, or validation passed). |
| `1` | Completed with failures, cancelled, or nothing to do. |
| `2` | Invalid configuration. |
| `64` | Usage error (unknown command). |

`Ctrl+C` during `send` stops gracefully after the in-flight message and reports what was sent.

## Feature parity

The CLI is one of three front-ends (CLI, Windows desktop, Web) that must stay in parity. See the
[Feature parity matrix](../../README.md#feature-parity) in the root README before adding features.
