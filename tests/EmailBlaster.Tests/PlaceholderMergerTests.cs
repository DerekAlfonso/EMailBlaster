using EmailBlaster.Core.Models;
using EmailBlaster.Core.Templating;
using Xunit;

namespace EmailBlaster.Tests;

public class PlaceholderMergerTests
{
    private static Dictionary<string, string> Fields(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Merge_ReplacesKnownPlaceholder()
    {
        var result = PlaceholderMerger.Merge("Hi {{Name}}!", Fields(("Name", "Ada")));
        Assert.Equal("Hi Ada!", result);
    }

    [Fact]
    public void Merge_IsCaseInsensitive()
    {
        var result = PlaceholderMerger.Merge("Hi {{name}}!", Fields(("NAME", "Ada")));
        Assert.Equal("Hi Ada!", result);
    }

    [Fact]
    public void Merge_UsesDefaultWhenFieldMissing()
    {
        var result = PlaceholderMerger.Merge("Hi {{Name|there}}!", Fields());
        Assert.Equal("Hi there!", result);
    }

    [Fact]
    public void Merge_UsesDefaultWhenFieldEmpty()
    {
        var result = PlaceholderMerger.Merge("Hi {{Name|there}}!", Fields(("Name", "")));
        Assert.Equal("Hi there!", result);
    }

    [Fact]
    public void Merge_MissingFieldWithoutDefaultRendersEmpty()
    {
        var result = PlaceholderMerger.Merge("Hi {{Name}}!", Fields());
        Assert.Equal("Hi !", result);
    }

    [Fact]
    public void Merge_ToleratesWhitespaceInsideToken()
    {
        var result = PlaceholderMerger.Merge("Hi {{ Name | there }}!", Fields(("Name", "Ada")));
        Assert.Equal("Hi Ada!", result);
    }

    [Fact]
    public void Merge_EmptyTemplateReturnsEmpty()
    {
        Assert.Equal("", PlaceholderMerger.Merge("", Fields()));
    }

    [Fact]
    public void Merge_TemplateAgainstRecipient_FillsAllParts()
    {
        var template = new EmailTemplate
        {
            Subject = "Hello {{Name}}",
            HtmlBody = "<p>{{Company}}</p>",
            TextBody = "{{Company}} plain"
        };
        var recipient = new Recipient { Name = "Ada", Email = "ada@example.com" };
        recipient.Fields["Company"] = "Acme";

        var message = PlaceholderMerger.Merge(template, recipient);

        Assert.Equal("ada@example.com", message.ToEmail);
        Assert.Equal("Ada", message.ToName);
        Assert.Equal("Hello Ada", message.Subject);
        Assert.Equal("<p>Acme</p>", message.HtmlBody);
        Assert.Equal("Acme plain", message.TextBody);
    }

    [Fact]
    public void Merge_TemplateWithoutTextBody_LeavesTextNull()
    {
        var template = new EmailTemplate { Subject = "s", HtmlBody = "<p>h</p>", TextBody = null };
        var message = PlaceholderMerger.Merge(template, new Recipient { Email = "a@b.co" });
        Assert.Null(message.TextBody);
    }

    [Fact]
    public void ExtractPlaceholders_ReturnsDistinctInFirstSeenOrder()
    {
        var found = PlaceholderMerger.ExtractPlaceholders("{{B}} {{A|x}} {{b}} {{C}}");
        Assert.Equal(new[] { "B", "A", "C" }, found);
    }

    [Fact]
    public void ExtractPlaceholders_EmptyTemplateReturnsEmpty()
    {
        Assert.Empty(PlaceholderMerger.ExtractPlaceholders(""));
    }

    [Fact]
    public void HtmlToPlainText_StripsTagsAndDecodesEntities()
    {
        var text = PlaceholderMerger.HtmlToPlainText("<p>Tom &amp; Jerry</p><p>&quot;quoted&quot;</p>");
        Assert.Contains("Tom & Jerry", text);
        Assert.Contains("\"quoted\"", text);
        Assert.DoesNotContain("<p>", text);
    }

    [Fact]
    public void HtmlToPlainText_TurnsBlockBreaksIntoNewlines()
    {
        var text = PlaceholderMerger.HtmlToPlainText("<p>line one</p><p>line two</p>");
        Assert.Equal("line one\nline two", text);
    }

    [Fact]
    public void HtmlToPlainText_BlankInputReturnsEmpty()
    {
        Assert.Equal(string.Empty, PlaceholderMerger.HtmlToPlainText("   "));
    }
}
