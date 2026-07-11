using EmailBlaster.Cli;
using Xunit;

namespace EmailBlaster.Tests;

public class CliArgumentsTests
{
    [Fact]
    public void FirstToken_BecomesLowercaseCommand()
    {
        var args = new Arguments(new[] { "SEND", "--yes" });
        Assert.Equal("send", args.Command);
    }

    [Fact]
    public void NoArguments_DefaultsToHelp()
    {
        Assert.Equal("help", new Arguments(Array.Empty<string>()).Command);
    }

    [Fact]
    public void LeadingOption_DefaultsToHelpCommand()
    {
        Assert.Equal("help", new Arguments(new[] { "--config", "x.json" }).Command);
    }

    [Fact]
    public void KeyValueOptions_AreParsed()
    {
        var args = new Arguments(new[] { "send", "--config", "my.json", "--rate", "2" });
        Assert.Equal("my.json", args.Get("config"));
        Assert.Equal("2", args.Get("rate"));
        Assert.Null(args.Get("missing"));
    }

    [Fact]
    public void KnownFlags_NeverConsumeValues()
    {
        var args = new Arguments(new[] { "send", "--dry-run", "--recipients", "list.csv" });
        Assert.True(args.Flag("dry-run"));
        Assert.Equal("list.csv", args.Get("recipients"));
    }

    [Fact]
    public void Aliases_ResolveToCanonicalNames()
    {
        var args = new Arguments(new[] { "send", "-y" });
        Assert.True(args.Flag("yes"));
    }

    [Fact]
    public void TrailingOptionWithoutValue_BecomesFlag()
    {
        var args = new Arguments(new[] { "send", "--quiet" });
        Assert.True(args.Flag("quiet"));
    }

    [Fact]
    public void GetOrDefault_FallsBackWhenMissing()
    {
        var args = new Arguments(new[] { "preview" });
        Assert.Equal("3", args.GetOrDefault("count", "3"));
    }

    [Fact]
    public void Has_TrueForBothFlagsAndValues()
    {
        var args = new Arguments(new[] { "send", "--dry-run", "--rate", "1" });
        Assert.True(args.Has("dry-run"));
        Assert.True(args.Has("rate"));
        Assert.False(args.Has("config"));
    }

    [Fact]
    public void OptionNames_AreCaseInsensitive()
    {
        var args = new Arguments(new[] { "send", "--Config", "x.json" });
        Assert.Equal("x.json", args.Get("config"));
    }
}
