using System.Globalization;
using OpenCommander.Files;

namespace OpenCommander.Tests;

public class SizeFormatterShortTests
{
    private const long K = 1024L;
    private const long M = K * 1024;
    private const long G = M * 1024;
    private const long T = G * 1024;
    private const long P = T * 1024;

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(512, "512 B")]
    [InlineData(999, "999 B")]
    [InlineData(1023, "1023 B")]
    public void BelowAKilobyteTheExactCountIsShown(long bytes, string expected) =>
        Assert.Equal(expected, SizeFormatter.Short(bytes));

    [Fact]
    public void ExactlyOneKilobyteIsOnePointZeroK() => Assert.Equal("1.0 K", SizeFormatter.Short(1024));

    [Theory]
    [InlineData(1536, "1.5 K")]
    [InlineData(10 * 1024, "10.0 K")]
    [InlineData(1024 * 1024, "1.0 M")]
    [InlineData(1024 * 1024 * 3 / 2, "1.5 M")]
    public void KilobytesAndMegabytesCarryOneDecimal(long bytes, string expected) =>
        Assert.Equal(expected, SizeFormatter.Short(bytes));

    [Fact]
    public void TheReferenceScreenshotValueRoundTrips() =>
        Assert.Equal("52.0 G", SizeFormatter.Short(52 * G));

    [Fact]
    public void EveryUnitSuffixIsReachable()
    {
        Assert.Equal("2.0 K", SizeFormatter.Short(2 * K));
        Assert.Equal("2.0 M", SizeFormatter.Short(2 * M));
        Assert.Equal("2.0 G", SizeFormatter.Short(2 * G));
        Assert.Equal("2.0 T", SizeFormatter.Short(2 * T));
        Assert.Equal("2.0 P", SizeFormatter.Short(2 * P));
    }

    [Fact]
    public void AFourDigitMantissaDropsTheDecimalToKeepTheWidth()
    {
        // 1000 K has no room for ".0" inside seven columns.
        Assert.Equal("1000 K", SizeFormatter.Short(1000 * K));
        Assert.Equal(6, SizeFormatter.Short(1000 * K).Length);

        // Just below the threshold the decimal is still there...
        Assert.Equal("999.9 K", SizeFormatter.Short(1023948));

        // ...and just above it, rounding must not produce "1000.0 K".
        Assert.Equal("1000 K", SizeFormatter.Short(1023999));
    }

    [Fact]
    public void TheLargestPossibleSizeStillFits()
    {
        string formatted = SizeFormatter.Short(long.MaxValue);

        Assert.Equal("8192 P", formatted);
        Assert.True(formatted.Length <= SizeFormatter.ShortMaxWidth);
    }

    [Fact]
    public void NothingEverExceedsSevenColumns()
    {
        var probes = new List<long> { 0, 1, 999, 1000, 1023, 1024, 1025, long.MaxValue };

        for (int shift = 0; shift < 63; shift++)
        {
            long value = 1L << shift;
            probes.Add(value);
            probes.Add(value - 1);
            probes.Add(value + (value / 2));
            probes.Add(value + (value / 3));
        }

        foreach (long value in probes.Where(v => v >= 0))
        {
            string formatted = SizeFormatter.Short(value);
            Assert.True(
                formatted.Length <= SizeFormatter.ShortMaxWidth,
                $"Short({value}) produced \"{formatted}\", which is {formatted.Length} columns wide");
        }
    }

    [Fact]
    public void NegativeSizesGetAMinusRatherThanAnOverflow()
    {
        Assert.Equal("-1.0 K", SizeFormatter.Short(-1024));
        Assert.Equal("-512 B", SizeFormatter.Short(-512));
        Assert.Equal("-8192 P", SizeFormatter.Short(long.MinValue));
    }

    [Fact]
    public void TheDecimalPointDoesNotFollowTheUsersCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // A culture that writes 1,5 rather than 1.5 must not shift the panel columns.
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

            Assert.Equal("1.5 K", SizeFormatter.Short(1536));
            Assert.Equal("1 234 567", SizeFormatter.Grouped(1234567));
            Assert.Equal("1,234,567", SizeFormatter.Commas(1234567));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}

public class SizeFormatterGroupingTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(999, "999")]
    [InlineData(1000, "1 000")]
    [InlineData(1024, "1 024")]
    [InlineData(1234567, "1 234 567")]
    [InlineData(1000000, "1 000 000")]
    public void GroupedSeparatesThousandsWithASpace(long bytes, string expected) =>
        Assert.Equal(expected, SizeFormatter.Grouped(bytes));

    [Fact]
    public void TheGroupSeparatorIsAPlainSpace()
    {
        Assert.Equal(' ', SizeFormatter.GroupSeparator);
        Assert.Equal(' ', SizeFormatter.Grouped(1000)[1]);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1,000")]
    [InlineData(1234567, "1,234,567")]
    public void CommasSeparateThousandsWithACommaInstead(long bytes, string expected) =>
        Assert.Equal(expected, SizeFormatter.Commas(bytes));

    [Fact]
    public void TheExtremesAreGroupedWithoutOverflowing()
    {
        Assert.Equal("9 223 372 036 854 775 807", SizeFormatter.Grouped(long.MaxValue));
        Assert.Equal("-9 223 372 036 854 775 808", SizeFormatter.Grouped(long.MinValue));
        Assert.Equal("9,223,372,036,854,775,807", SizeFormatter.Commas(long.MaxValue));
        Assert.Equal("-1 234", SizeFormatter.Grouped(-1234));
        Assert.Equal("-1", SizeFormatter.Grouped(-1));
    }
}
