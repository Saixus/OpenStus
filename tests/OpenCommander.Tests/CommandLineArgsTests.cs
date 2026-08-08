using System.Runtime.CompilerServices;
using OpenCommander.Core;

namespace OpenCommander.Tests;

/// <summary>
/// Checks of the option combinations <c>oc</c> accepts and refuses. The general parsing contract
/// lives in <c>CommandLineArgsTests</c>; this file is about <c>--size</c>, which only means anything
/// alongside <c>--screenshot</c>.
/// </summary>
public class CommandLineArgsOptionTests
{
    [Theory]
    [InlineData("--size", "80x25")]
    [InlineData("--size=80x25", null)]
    public void SizeWithoutScreenshotIsRejected(string first, string? second)
    {
        string[] argv = second is null ? [first] : [first, second];

        CommandLineArgs args = CommandLineArgs.Parse(argv);

        // Terminal.Create treats a forced size as headless, so an interactive run would render one
        // frame into a screen nobody can see and exit 0 having printed nothing.
        Assert.True(args.HasError);
        Assert.Equal("--size requires --screenshot", args.Error);
    }

    [Fact]
    public void SizeWithAPositionalPathButNoScreenshotIsStillRejected()
    {
        CommandLineArgs args = CommandLineArgs.Parse(["C:/Temp", "--size", "80x25"]);

        Assert.True(args.HasError);
        Assert.Contains("--screenshot", args.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--screenshot", "--size", "160x50")]
    [InlineData("--size", "160x50", "--screenshot")] // order must not matter
    public void SizeWithScreenshotIsAccepted(string a, string b, string c)
    {
        CommandLineArgs args = CommandLineArgs.Parse([a, b, c]);

        Assert.False(args.HasError);
        Assert.True(args.Screenshot);
        Assert.Equal(160, args.Width);
        Assert.Equal(50, args.Height);
        Assert.Equal(160, args.EffectiveWidth);
        Assert.Equal(50, args.EffectiveHeight);
    }

    [Fact]
    public void ScreenshotWithoutSizeKeepsTheDocumentedDefault()
    {
        CommandLineArgs args = CommandLineArgs.Parse(["--screenshot"]);

        Assert.False(args.HasError);
        Assert.Null(args.Width);
        Assert.Equal(CommandLineArgs.DefaultScreenshotWidth, args.EffectiveWidth);
        Assert.Equal(CommandLineArgs.DefaultScreenshotHeight, args.EffectiveHeight);
    }

    [Fact]
    public void AMalformedSizeIsReportedAsMalformedNotAsMissingScreenshot()
    {
        CommandLineArgs args = CommandLineArgs.Parse(["--screenshot", "--size", "wide"]);

        Assert.True(args.HasError);
        Assert.Contains("WxH", args.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    public void HelpAndVersionStillWinOverTheSizeRule(string option)
    {
        // "oc --size 80x25 --help" should print the usage text, not a parse error.
        CommandLineArgs args = CommandLineArgs.Parse(["--size", "80x25", option]);

        Assert.False(args.HasError);
    }

    [Fact]
    public void TheUsageTextTiesSizeToScreenshotTheWayAnsiIsTied()
    {
        string usage = CommandLineArgs.UsageText;

        Assert.Contains("--ansi                 with --screenshot", usage, StringComparison.Ordinal);
        Assert.Contains("--size <WxH>           with --screenshot", usage, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReadmeDocumentsSizeTheSameWayTheUsageTextDoes()
    {
        string? root = FindRepositoryRoot();
        if (root is null)
        {
            return; // the sources are not next to the test assembly; nothing to check
        }

        string readme = File.ReadAllText(Path.Combine(root, "README.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        string usage = CommandLineArgs.UsageText.Replace("\r\n", "\n", StringComparison.Ordinal);
        int optionsAt = usage.IndexOf("  --left", StringComparison.Ordinal);
        Assert.True(optionsAt > 0);

        // The README's option block is the usage text verbatim, so the two cannot drift apart.
        Assert.Contains(usage[optionsAt..].Trim(), readme, StringComparison.Ordinal);
    }

    private static string? FindRepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        // The compiled test assembly lives under an artifacts folder that may sit anywhere, so the
        // source path is the only reliable way back to the repository.
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? string.Empty);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenCommander.sln")) &&
                File.Exists(Path.Combine(directory.FullName, "README.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
