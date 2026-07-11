using EmailBlaster.Core.Models;

namespace EmailBlaster.Core.Import;

/// <summary>Reads a recipient dataset from a stream into a <see cref="RecipientList"/>.</summary>
public interface IRecipientImporter
{
    /// <summary>File extensions this importer handles (including the leading dot, lower-case).</summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <summary>Imports recipients from the given stream.</summary>
    RecipientList Import(Stream stream);
}
