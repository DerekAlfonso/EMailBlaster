using System.Text;
using EmailBlaster.Cli;

// Ensure the status glyphs (✓ ✗ • !) render correctly on the Windows console.
try { Console.OutputEncoding = Encoding.UTF8; } catch { /* redirected or unsupported */ }

var args2 = new Arguments(args);

if (args2.Flag("no-color"))
    ConsoleEx.ColorEnabled = false;

try
{
    return args2.Command switch
    {
        "send" => await Commands.Send(args2),
        "test" => await Commands.Test(args2),
        "test-connection" => await Commands.TestConnection(args2),
        "preview" => Commands.Preview(args2),
        "validate" => Commands.Validate(args2),
        "config" => Commands.ShowConfig(args2),
        "save-config" => Commands.SaveConfig(args2),
        "help" or "--help" or "-h" => Help(),
        _ => Unknown(args2.Command)
    };
}
catch (Exception ex)
{
    ConsoleEx.Error(ex.Message);
    return 1;
}

static int Unknown(string command)
{
    ConsoleEx.Error($"Unknown command '{command}'.");
    Help();
    return 64; // EX_USAGE
}

static int Help()
{
    Console.WriteLine(
        """
        Email Blaster CLI — bulk email over SMTP or AWS SES.

        USAGE
          emailblaster <command> [options]

        COMMANDS
          send             Merge and send a campaign to an imported recipient list.
          test             Send a single test email.
          test-connection  Verify transport connectivity / credentials.
          preview          Render merged messages for the first N recipients.
          validate         Validate the resolved configuration.
          config           Print the resolved configuration (secrets redacted).
          save-config      Write the resolved configuration to a JSON file.
          help             Show this help.

        GLOBAL OPTIONS
          --config <path>          Path to emailblaster.json (else auto-resolved / env vars).
          --provider <smtp|aws>    Override the send provider.
          --rate <n>               Override send rate (messages/sec; 0 = unlimited).
          --from-name <text>       Override the From name.
          --from-email <email>     Override the From email.
          --reply-to <email>       Override the Reply-To email.
          --no-color               Disable coloured output.

        TEMPLATE OPTIONS (send / test / preview)
          --subject <text>         Subject line (may contain {{placeholders}}).
          --subject-file <path>    Read the subject from a file.
          --html <text>            Inline HTML body.
          --html-file <path>       Read the HTML body from a file.
          --text-file <path>       Optional plain-text alternative body.

        SEND OPTIONS
          --recipients <path>      CSV or XLSX recipient file (required).
          --dry-run                Merge and show the first message without sending.
          --yes, -y                Skip the confirmation prompt.
          --quiet                  Suppress the live progress line.

        TEST OPTIONS
          --to <email>             Destination address (required).
          --sample-from <path>     Use the first row of this file to fill placeholders.

        PREVIEW OPTIONS
          --recipients <path>      CSV or XLSX recipient file (required).
          --count <n>              Number of recipients to preview (default 3).
          --out <dir>              Write merged HTML files here instead of stdout.

        SAVE-CONFIG OPTIONS
          --out <path>             Destination JSON file (required). Global overrides apply.

        EXAMPLES
          emailblaster validate
          emailblaster save-config --out backup.json
          emailblaster test --to me@example.com --subject "Hi {{Name|there}}" --html "<p>Hello</p>"
          emailblaster preview --recipients audience.csv --subject "Hi {{Name}}" --html-file body.html
          emailblaster send --recipients audience.xlsx --subject "Hi {{Name}}" --html-file body.html --yes

        Configuration is read from emailblaster.json (beside the app or --config) and can be
        overridden by EMAILBLASTER_* environment variables.
        """);
    return 0;
}
