using ClosedXML.Excel;
using EmailBlaster.Core.Models;

namespace EmailBlaster.Core.Import;

/// <summary>
/// Imports recipients from an XLSX workbook. Reads the first worksheet and treats its first
/// populated row as the header row.
/// </summary>
public sealed class XlsxRecipientImporter : IRecipientImporter
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".xlsx", ".xlsm" };

    public RecipientList Import(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault()
                        ?? throw new InvalidDataException("The workbook contains no worksheets.");

        var range = worksheet.RangeUsed();
        if (range is null)
            return RecipientList.Empty;

        var rows = range.RowsUsed().ToList();
        if (rows.Count == 0)
            return RecipientList.Empty;

        var headerRow = rows[0];
        var headers = headerRow.Cells().Select(c => c.GetString().Trim()).ToList();
        var map = new HeaderMap(headers);

        if (!map.HasEmail)
            throw new InvalidDataException(
                "Could not find an email column. Expected a header such as 'Email' or 'E-mail'.");

        var recipients = new List<Recipient>();
        var skipped = 0;

        // Cell indexes on an IXLRangeRow are 1-based and relative to the range, so header index
        // i (0-based) maps to row.Cell(i + 1).
        foreach (var row in rows.Skip(1))
        {
            var email = CellText(row, map.EmailIndex + 1);
            if (string.IsNullOrWhiteSpace(email))
            {
                skipped++;
                continue;
            }

            var recipient = new Recipient
            {
                Email = email.Trim(),
                Name = map.NameIndex >= 0 ? CellText(row, map.NameIndex + 1).Trim() : string.Empty
            };

            for (var i = 0; i < headers.Count; i++)
            {
                if (i == map.EmailIndex || i == map.NameIndex)
                    continue;
                var header = headers[i].Trim();
                if (!string.IsNullOrWhiteSpace(header))
                    recipient.Fields[header] = CellText(row, i + 1).Trim();
            }

            recipients.Add(recipient);
        }

        return new RecipientList(recipients, map.PlaceholderColumns(), skipped);
    }

    private static string CellText(IXLRangeRow row, int cellIndex)
    {
        if (cellIndex < 1)
            return string.Empty;
        var cell = row.Cell(cellIndex);
        return cell is null ? string.Empty : cell.GetString();
    }
}
