using EmailBlaster.Core.Models;

namespace EmailBlaster.Core.Import;

/// <summary>Chooses the correct <see cref="IRecipientImporter"/> for a file based on its extension.</summary>
public static class RecipientImporterFactory
{
    private static readonly IRecipientImporter[] Importers =
    {
        new CsvRecipientImporter(),
        new XlsxRecipientImporter()
    };

    /// <summary>All file extensions supported across every importer (for open-file dialog filters).</summary>
    public static IReadOnlyCollection<string> SupportedExtensions { get; } =
        Importers.SelectMany(i => i.SupportedExtensions).Distinct().ToArray();

    /// <summary>Returns the importer that handles the given file extension, or null if unsupported.</summary>
    public static IRecipientImporter? ForExtension(string extension)
    {
        var ext = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        return Importers.FirstOrDefault(i => i.SupportedExtensions.Contains(ext));
    }

    /// <summary>Imports recipients from a file path, selecting the importer by extension.</summary>
    public static RecipientList ImportFile(string path)
    {
        var importer = ForExtension(Path.GetExtension(path))
                       ?? throw new NotSupportedException(
                           $"No importer is registered for '{Path.GetExtension(path)}' files. " +
                           $"Supported types: {string.Join(", ", SupportedExtensions)}.");

        using var stream = File.OpenRead(path);
        return importer.Import(stream);
    }
}
