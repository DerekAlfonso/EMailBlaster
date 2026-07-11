namespace EmailBlaster.Cli;

/// <summary>
/// A tiny, dependency-free command-line parser. The first positional token is the command; the rest
/// are <c>--key value</c> pairs or boolean <c>--flag</c> switches. Known flags never consume a value.
/// </summary>
public sealed class Arguments
{
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "dry-run", "yes", "help", "no-color", "quiet"
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["y"] = "yes",
        ["h"] = "help"
    };

    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public string Command { get; }

    public Arguments(string[] args)
    {
        Command = args.Length > 0 && !args[0].StartsWith('-')
            ? args[0].ToLowerInvariant()
            : "help";

        var start = Command == "help" && (args.Length == 0 || args[0].StartsWith('-')) ? 0 : 1;

        for (var i = start; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith('-'))
                continue;

            var key = token.TrimStart('-');
            if (Aliases.TryGetValue(key, out var canonical))
                key = canonical;

            if (Flags.Contains(key))
            {
                _flags.Add(key);
                continue;
            }

            // Expect a value; if the next token is another option, treat this as a bare flag.
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            {
                _values[key] = args[++i];
            }
            else
            {
                _flags.Add(key);
            }
        }
    }

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    public string GetOrDefault(string key, string fallback) => Get(key) ?? fallback;

    public bool Has(string key) => _flags.Contains(key) || _values.ContainsKey(key);

    public bool Flag(string key) => _flags.Contains(key);
}
