using System.Text;
using OpenCommander.Text;

namespace OpenCommander.Tests;

public class EncodingDetectorBomTests
{
    [Fact]
    public void Utf8BomIsRecognisedAndRoundTripsItsPreamble()
    {
        byte[] data = [0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i'];

        (Encoding encoding, bool hasBom) = EncodingDetector.Detect(data);

        Assert.True(hasBom);
        Assert.Equal(65001, encoding.CodePage);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, encoding.GetPreamble());
        Assert.Equal(3, EncodingDetector.BomLength(data));
    }

    [Fact]
    public void Utf16LittleEndianBom()
    {
        byte[] data = [0xFF, 0xFE, (byte)'h', 0x00];

        (Encoding encoding, bool hasBom) = EncodingDetector.Detect(data);

        Assert.True(hasBom);
        Assert.Equal(1200, encoding.CodePage);
        Assert.Equal(2, EncodingDetector.BomLength(data));
        Assert.Equal(2, EncodingDetector.CodeUnitSize(encoding));
        Assert.False(EncodingDetector.IsBigEndian(encoding));
    }

    [Fact]
    public void Utf16BigEndianBom()
    {
        byte[] data = [0xFE, 0xFF, 0x00, (byte)'h'];

        (Encoding encoding, bool hasBom) = EncodingDetector.Detect(data);

        Assert.True(hasBom);
        Assert.Equal(1201, encoding.CodePage);
        Assert.True(EncodingDetector.IsBigEndian(encoding));
    }

    [Fact]
    public void Utf32LittleEndianBomWinsOverTheUtf16BomItStartsWith()
    {
        byte[] data = [0xFF, 0xFE, 0x00, 0x00, (byte)'h', 0x00, 0x00, 0x00];

        (Encoding encoding, bool hasBom) = EncodingDetector.Detect(data);

        Assert.True(hasBom);
        Assert.Equal(12000, encoding.CodePage);
        Assert.Equal(4, EncodingDetector.BomLength(data));
        Assert.Equal(4, EncodingDetector.CodeUnitSize(encoding));
    }

    [Fact]
    public void Utf32BigEndianBom()
    {
        byte[] data = [0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, (byte)'h'];

        (Encoding encoding, bool hasBom) = EncodingDetector.Detect(data);

        Assert.True(hasBom);
        Assert.Equal(12001, encoding.CodePage);
        Assert.True(EncodingDetector.IsBigEndian(encoding));
    }

    [Fact]
    public void ATwoByteUtf16BomIsNotMistakenForUtf32WhenOnlyThreeBytesFollow()
    {
        // FF FE 00 41 is UTF-16 LE for U+4100, not a truncated UTF-32 mark.
        byte[] data = [0xFF, 0xFE, 0x00, 0x41];

        (Encoding encoding, _) = EncodingDetector.Detect(data);

        Assert.Equal(1200, encoding.CodePage);
    }

    [Fact]
    public void NoBomMeansBomLengthZero()
    {
        Assert.Equal(0, EncodingDetector.BomLength("plain"u8));
        Assert.Equal(0, EncodingDetector.BomLength([]));
    }
}

public class EncodingDetectorUtf8HeuristicTests
{
    [Fact]
    public void EmptyContentIsBomlessUtf8()
    {
        (Encoding encoding, bool hasBom) = EncodingDetector.Detect([]);

        Assert.False(hasBom);
        Assert.Equal(65001, encoding.CodePage);
        Assert.Empty(encoding.GetPreamble());
    }

    [Fact]
    public void PlainAsciiIsBomlessUtf8()
    {
        (Encoding encoding, bool hasBom) = EncodingDetector.Detect("hello world\r\n"u8);

        Assert.False(hasBom);
        Assert.Equal(65001, encoding.CodePage);
    }

    [Fact]
    public void WellFormedMultiByteSequencesAreUtf8()
    {
        byte[] data = Encoding.UTF8.GetBytes("naïve — 日本語 🙂");

        (Encoding encoding, bool hasBom) = EncodingDetector.Detect(data);

        Assert.False(hasBom);
        Assert.Equal(65001, encoding.CodePage);
        Assert.True(EncodingDetector.IsValidUtf8(data));
    }

    [Theory]
    [InlineData(new byte[] { 0x41, 0x80, 0x42 })]                    // stray continuation byte
    [InlineData(new byte[] { 0xC0, 0x80 })]                          // over-long two byte NUL
    [InlineData(new byte[] { 0xC1, 0xBF })]                          // over-long two byte
    [InlineData(new byte[] { 0xE0, 0x80, 0x80 })]                    // over-long three byte
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 })]                    // UTF-16 surrogate D800
    [InlineData(new byte[] { 0xF5, 0x80, 0x80, 0x80 })]              // beyond U+10FFFF
    [InlineData(new byte[] { 0xFF, 0x41 })]                          // never a valid lead byte
    [InlineData(new byte[] { 0xE2, 0x28, 0xA1 })]                    // bad continuation
    public void MalformedSequencesAreRejected(byte[] data)
    {
        Assert.False(EncodingDetector.IsValidUtf8(data));

        (Encoding encoding, bool hasBom) = EncodingDetector.Detect(data);
        Assert.False(hasBom);
        Assert.Equal(EncodingDetector.AnsiFallback.CodePage, encoding.CodePage);
    }

    [Fact]
    public void ASequenceCutOffByTheEndOfTheSampleIsAcceptedButNotWhenTheWholeFileIsGiven()
    {
        // The lead byte of "é" with its continuation byte chopped off.
        byte[] truncated = [0x41, 0xC3];

        Assert.True(EncodingDetector.IsValidUtf8(truncated));
        Assert.False(EncodingDetector.IsValidUtf8(truncated, allowTruncatedTail: false));
    }

    [Fact]
    public void ATruncatedTailStillHasToLookLikeOne()
    {
        // A three byte lead followed by a byte that is not a continuation is broken, not truncated.
        Assert.False(EncodingDetector.IsValidUtf8([0xE2, 0x41]));
    }

    [Fact]
    public void TheAnsiFallbackRoundTripsEveryByteValue()
    {
        // The fallback has to be loss-free or an unrecognised file is corrupted by a save.
        var bytes = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            bytes[i] = (byte)i;
        }

        var fallback = EncodingDetector.AnsiFallback;
        Assert.Equal(bytes, fallback.GetBytes(fallback.GetString(bytes)));
    }
}

public class EncodingDetectorBinaryAndNamingTests
{
    [Fact]
    public void NulBytesMakeContentLookBinary()
    {
        Assert.True(EncodingDetector.LooksBinary([0x4D, 0x5A, 0x00, 0x01]));
        Assert.False(EncodingDetector.LooksBinary("just text"u8));
    }

    [Fact]
    public void Utf16TextIsNotCalledBinaryDespiteItsNulBytes()
    {
        byte[] data = [0xFF, 0xFE, (byte)'h', 0x00, (byte)'i', 0x00];

        Assert.False(EncodingDetector.LooksBinary(data));
    }

    [Theory]
    [InlineData(65001, false, "UTF-8")]
    [InlineData(65001, true, "UTF-8 BOM")]
    [InlineData(1200, true, "UTF-16LE BOM")]
    [InlineData(1201, true, "UTF-16BE BOM")]
    [InlineData(12000, true, "UTF-32LE BOM")]
    [InlineData(12001, true, "UTF-32BE BOM")]
    public void DisplayNamesAreTheOnesTheStatusLineShows(int codePage, bool hasBom, string expected)
    {
        Encoding encoding = codePage switch
        {
            65001 => new UTF8Encoding(hasBom),
            1200 => new UnicodeEncoding(false, hasBom),
            1201 => new UnicodeEncoding(true, hasBom),
            12000 => new UTF32Encoding(false, hasBom),
            _ => new UTF32Encoding(true, hasBom),
        };

        Assert.Equal(expected, EncodingDetector.DisplayName(encoding, hasBom));
    }

    [Fact]
    public void TheFallbackEncodingIsNamedAnsi()
    {
        Assert.Equal("ANSI", EncodingDetector.DisplayName(EncodingDetector.AnsiFallback, false));
    }

    [Fact]
    public void DecodingDropsTheByteOrderMark()
    {
        byte[] data = [0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i'];
        (Encoding encoding, _) = EncodingDetector.Detect(data);

        Assert.Equal("hi", EncodingDetector.DecodeSkippingBom(data, encoding));
    }

    [Fact]
    public void ReadSampleRewindsTheStreamAndCapsAtTheRequestedSize()
    {
        using var stream = new MemoryStream(new byte[100]);

        byte[] sample = EncodingDetector.ReadSample(stream, 10);

        Assert.Equal(10, sample.Length);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void ReadSampleOfAShortStreamReturnsOnlyWhatIsThere()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        Assert.Equal(3, EncodingDetector.ReadSample(stream, 4096).Length);
    }
}

public class LineEndingsTests
{
    [Theory]
    [InlineData("", LineEndingStyle.None)]
    [InlineData("one line", LineEndingStyle.None)]
    [InlineData("a\r\nb", LineEndingStyle.Crlf)]
    [InlineData("a\nb\nc", LineEndingStyle.Lf)]
    [InlineData("a\rb\rc", LineEndingStyle.Cr)]
    [InlineData("a\r\nb\nc", LineEndingStyle.Mixed)]
    [InlineData("a\rb\nc", LineEndingStyle.Mixed)]
    public void DetectClassifiesTheConventionsPresent(string text, LineEndingStyle expected)
    {
        Assert.Equal(expected, LineEndings.Detect(text));
    }

    [Fact]
    public void ACrLfPairIsCountedOnceAndNotAsALoneCrPlusALoneLf()
    {
        LineEndings.Count("a\r\nb", out int crlf, out int lf, out int cr);

        Assert.Equal(1, crlf);
        Assert.Equal(0, lf);
        Assert.Equal(0, cr);
    }

    [Fact]
    public void DominantPicksTheMostFrequentAndFallsBackToThePlatform()
    {
        Assert.Equal(LineEndingStyle.Lf, LineEndings.Dominant("a\nb\nc\r\nd"));
        Assert.Equal(LineEndingStyle.Crlf, LineEndings.Dominant("a\r\nb\r\nc\nd"));
        Assert.Equal(LineEndings.Platform, LineEndings.Dominant("no terminators"));
    }

    [Fact]
    public void SequenceNeverReturnsAnEmptyTerminator()
    {
        Assert.Equal("\r\n", LineEndings.Sequence(LineEndingStyle.Crlf));
        Assert.Equal("\n", LineEndings.Sequence(LineEndingStyle.Lf));
        Assert.Equal("\r", LineEndings.Sequence(LineEndingStyle.Cr));
        Assert.NotEmpty(LineEndings.Sequence(LineEndingStyle.None));
        Assert.NotEmpty(LineEndings.Sequence(LineEndingStyle.Mixed));
    }

    [Theory]
    [InlineData(LineEndingStyle.Crlf, "CRLF")]
    [InlineData(LineEndingStyle.Lf, "LF")]
    [InlineData(LineEndingStyle.Cr, "CR")]
    [InlineData(LineEndingStyle.Mixed, "Mixed")]
    [InlineData(LineEndingStyle.None, "None")]
    public void NamesAreTheOnesTheStatusLineShows(LineEndingStyle style, string expected)
    {
        Assert.Equal(expected, LineEndings.Name(style));
    }

    [Fact]
    public void OfClassifiesASingleTerminator()
    {
        Assert.Equal(LineEndingStyle.Crlf, LineEndings.Of("\r\n"));
        Assert.Equal(LineEndingStyle.Lf, LineEndings.Of("\n"));
        Assert.Equal(LineEndingStyle.Cr, LineEndings.Of("\r"));
        Assert.Equal(LineEndingStyle.None, LineEndings.Of(""));
        Assert.Equal(LineEndingStyle.None, LineEndings.Of(null));
    }

    [Fact]
    public void CombineFoldsObservationsAndMixesOnDisagreement()
    {
        Assert.Equal(LineEndingStyle.Lf, LineEndings.Combine(LineEndingStyle.None, LineEndingStyle.Lf));
        Assert.Equal(LineEndingStyle.Lf, LineEndings.Combine(LineEndingStyle.Lf, LineEndingStyle.None));
        Assert.Equal(LineEndingStyle.Lf, LineEndings.Combine(LineEndingStyle.Lf, LineEndingStyle.Lf));
        Assert.Equal(LineEndingStyle.Mixed, LineEndings.Combine(LineEndingStyle.Lf, LineEndingStyle.Crlf));
    }

    [Fact]
    public void SplitDropsTerminatorsAndKeepsTheTrailingEmptyElement()
    {
        Assert.Equal(new[] { "a", "b", string.Empty }, LineEndings.Split("a\r\nb\n"));
        Assert.Equal(new[] { "a", "b" }, LineEndings.Split("a\rb"));
        Assert.Equal(new[] { string.Empty }, LineEndings.Split(string.Empty));
    }

    [Fact]
    public void NormalizeRewritesEveryConventionToOne()
    {
        Assert.Equal("a\nb\nc", LineEndings.Normalize("a\r\nb\rc", LineEndingStyle.Lf));
        Assert.Equal("a\r\nb", LineEndings.Join(new[] { "a", "b" }, LineEndingStyle.Crlf));
    }
}
