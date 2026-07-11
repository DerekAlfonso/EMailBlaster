using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using EmailBlaster.Core.Models;

namespace EmailBlaster.Core.Import;

/// <summary>Imports recipients from a CSV file. The first row is treated as the header row.</summary>
public sealed class CsvRecipientImporter : IRecipientImporter
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".csv", ".txt" };

    public RecipientList Import(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim,
            DetectDelimiter = true
        };

        using var csv = new CsvReader(reader, config);

        if (!csv.Read() || !csv.ReadHeader())
            return RecipientList.Empty;

        var headers = (csv.HeaderRecord ?? Array.Empty<string>()).ToList();
        var map = new HeaderMap(headers);

        if (!map.HasEmail)
            throw new InvalidDataException(
                "Could not find an email column. Expected a header such as 'Email' or 'E-mail'.");

        var recipients = new List<Recipient>();
        var skipped = 0;

        while (csv.Read())
        {
            var email = SafeField(csv, map.EmailIndex);
            if (string.IsNullOrWhiteSpace(email))
            {
                skipped++;
                continue;
            }

            var recipient = new Recipient
            {
                Email = email.Trim(),
                Name = map.NameIndex >= 0 ? SafeField(csv, map.NameIndex).Trim() : string.Empty
            };

            for (var i = 0; i < headers.Count; i++)
            {
                if (i == map.EmailIndex || i == map.NameIndex)
                    continue;
                var header = headers[i].Trim();
                if (!string.IsNullOrWhiteSpace(header))
                    recipient.Fields[header] = SafeField(csv, i).Trim();
            }

            recipients.Add(recipient);
        }

        return new RecipientList(recipients, map.PlaceholderColumns(), skipped);
    }

    private static string SafeField(CsvReader csv, int index)
    {
        if (index < 0)
            return string.Empty;
        return csv.TryGetField<string>(index, out var value) ? value ?? string.Empty : string.Empty;
    }
}
