using System.Text;
using ClosedXML.Excel;
using EmailBlaster.Core.Import;
using Xunit;

namespace EmailBlaster.Tests;

public class HeaderMapTests
{
    [Theory]
    [InlineData("Email")]
    [InlineData("e-mail")]
    [InlineData("EMAIL ADDRESS")]
    [InlineData("mail")]
    public void FindsEmailColumnByAlias(string header)
    {
        var map = new HeaderMap(new[] { "Other", header });
        Assert.True(map.HasEmail);
        Assert.Equal(1, map.EmailIndex);
    }

    [Fact]
    public void FallsBackToHeaderContainingMail()
    {
        var map = new HeaderMap(new[] { "Work Mail", "Other" });
        Assert.True(map.HasEmail);
        Assert.Equal(0, map.EmailIndex);
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("full name")]
    [InlineData("First Name")]
    public void FindsNameColumnByAlias(string header)
    {
        var map = new HeaderMap(new[] { header, "Email" });
        Assert.Equal(0, map.NameIndex);
    }

    [Fact]
    public void NoEmailColumn_HasEmailIsFalse()
    {
        var map = new HeaderMap(new[] { "Foo", "Bar" });
        Assert.False(map.HasEmail);
    }

    [Fact]
    public void PlaceholderColumns_AlwaysStartWithNameAndEmail_ThenExtras()
    {
        var map = new HeaderMap(new[] { "Email", "Name", "Company", "City" });
        Assert.Equal(new[] { "Name", "Email", "Company", "City" }, map.PlaceholderColumns());
    }

    [Fact]
    public void PlaceholderColumns_SkipsBlankAndDuplicateHeaders()
    {
        var map = new HeaderMap(new[] { "Email", "", "Company", "company" });
        Assert.Equal(new[] { "Name", "Email", "Company" }, map.PlaceholderColumns());
    }
}

public class CsvRecipientImporterTests
{
    private static Stream StreamOf(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public void Import_MapsColumnsAndExtraFields()
    {
        var csv = "Name,Email,Company\nAda Lovelace,ada@example.com,Analytical Engines\n";
        var list = new CsvRecipientImporter().Import(StreamOf(csv));

        var recipient = Assert.Single(list.Recipients);
        Assert.Equal("Ada Lovelace", recipient.Name);
        Assert.Equal("ada@example.com", recipient.Email);
        Assert.Equal("Analytical Engines", recipient.Fields["Company"]);
        Assert.Equal(new[] { "Name", "Email", "Company" }, list.Columns);
        Assert.Equal(0, list.SkippedRows);
    }

    [Fact]
    public void Import_SkipsRowsWithoutEmail()
    {
        var csv = "Email\nada@example.com\n\n   \nbob@example.com\n";
        var list = new CsvRecipientImporter().Import(StreamOf(csv));

        Assert.Equal(2, list.Count);
        // CsvHelper drops fully blank lines; the whitespace-only email row is counted as skipped.
        Assert.True(list.SkippedRows >= 1);
    }

    [Fact]
    public void Import_MissingEmailColumn_Throws()
    {
        var csv = "Name,Company\nAda,Acme\n";
        Assert.Throws<InvalidDataException>(() => new CsvRecipientImporter().Import(StreamOf(csv)));
    }

    [Fact]
    public void Import_DetectsSemicolonDelimiter()
    {
        var csv = "Name;Email\nAda;ada@example.com\n";
        var list = new CsvRecipientImporter().Import(StreamOf(csv));

        var recipient = Assert.Single(list.Recipients);
        Assert.Equal("ada@example.com", recipient.Email);
    }

    [Fact]
    public void Import_EmptyStream_ReturnsEmptyList()
    {
        var list = new CsvRecipientImporter().Import(StreamOf(""));
        Assert.Equal(0, list.Count);
    }
}

public class XlsxRecipientImporterTests
{
    [Fact]
    public void Import_ReadsRowsFromWorkbook()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Recipients");
            sheet.Cell(1, 1).Value = "Name";
            sheet.Cell(1, 2).Value = "Email";
            sheet.Cell(1, 3).Value = "Company";
            sheet.Cell(2, 1).Value = "Ada";
            sheet.Cell(2, 2).Value = "ada@example.com";
            sheet.Cell(2, 3).Value = "Acme";
            sheet.Cell(3, 1).Value = "No Email";
            workbook.SaveAs(stream);
        }
        stream.Position = 0;

        var list = new XlsxRecipientImporter().Import(stream);

        var recipient = Assert.Single(list.Recipients);
        Assert.Equal("Ada", recipient.Name);
        Assert.Equal("ada@example.com", recipient.Email);
        Assert.Equal("Acme", recipient.Fields["Company"]);
        Assert.Equal(1, list.SkippedRows);
    }
}

public class RecipientImporterFactoryTests
{
    [Theory]
    [InlineData(".csv", typeof(CsvRecipientImporter))]
    [InlineData(".TXT", typeof(CsvRecipientImporter))]
    [InlineData(".xlsx", typeof(XlsxRecipientImporter))]
    public void ForExtension_ReturnsMatchingImporter(string extension, Type expected)
    {
        var importer = RecipientImporterFactory.ForExtension(extension);
        Assert.NotNull(importer);
        Assert.IsType(expected, importer);
    }

    [Fact]
    public void ForExtension_UnknownExtension_ReturnsNull()
    {
        Assert.Null(RecipientImporterFactory.ForExtension(".pdf"));
    }
}
