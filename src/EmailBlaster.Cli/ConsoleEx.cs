namespace EmailBlaster.Cli;

/// <summary>Small helpers for coloured, structured console output. Honours <c>NO_COLOR</c> / --no-color.</summary>
public static class ConsoleEx
{
    public static bool ColorEnabled { get; set; } =
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

    private static void Write(string text, ConsoleColor color)
    {
        if (!ColorEnabled)
        {
            Console.Write(text);
            return;
        }
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = prev;
    }

    public static void Success(string message) => Line("✓ ", message, ConsoleColor.Green);
    public static void Error(string message) => LineErr("✗ ", message, ConsoleColor.Red);
    public static void Warn(string message) => Line("! ", message, ConsoleColor.Yellow);
    public static void Info(string message) => Line("• ", message, ConsoleColor.Cyan);

    public static void Heading(string message)
    {
        Console.WriteLine();
        Write(message + Environment.NewLine, ConsoleColor.White);
    }

    private static void Line(string prefix, string message, ConsoleColor color)
    {
        Write(prefix, color);
        Console.WriteLine(message);
    }

    private static void LineErr(string prefix, string message, ConsoleColor color)
    {
        if (ColorEnabled)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Error.Write(prefix);
            Console.ForegroundColor = prev;
        }
        else
        {
            Console.Error.Write(prefix);
        }
        Console.Error.WriteLine(message);
    }

    /// <summary>Reads a yes/no answer from the console, defaulting to no.</summary>
    public static bool Confirm(string question)
    {
        Console.Write(question + " [y/N] ");
        var response = Console.ReadLine();
        return response is not null &&
               (response.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ||
                response.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase));
    }
}
