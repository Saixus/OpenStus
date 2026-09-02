using System.Runtime.InteropServices;
using System.Text;

namespace Dvopan.Text;

/// <summary>
/// Works out how a file's bytes should be decoded, from a byte order mark when there is one and
/// from the shape of the bytes themselves when there is not.
/// </summary>
/// <remarks>
/// <para>
/// The rule is deliberately short and predictable, because an editor that guesses cleverly and
/// wrongly corrupts files on save:
/// </para>
/// <list type="number">
/// <item><description>A recognised BOM wins outright.</description></item>
/// <item><description>Otherwise, bytes that form well-formed UTF-8 are UTF-8. Pure ASCII qualifies.</description></item>
/// <item><description>Otherwise <see cref="AnsiFallback"/>, a single byte encoding.</description></item>
/// </list>
/// <para>
/// Every returned <see cref="Encoding"/> is constructed so that its preamble matches the reported
/// <c>HasBom</c> flag. Writing the text back through that instance therefore reproduces the
/// original BOM - or the original absence of one - without the caller having to think about it.
/// </para>
/// <para>
/// Note that BOM-less UTF-16 is deliberately <em>not</em> detected. The heuristics for it
/// (counting NUL bytes at even or odd offsets) misfire on binaries and on Latin-1 text, and a
/// wrong answer there is unrecoverable.
/// </para>
/// </remarks>
public static class EncodingDetector
{
    /// <summary>How many bytes of a file are enough to classify it.</summary>
    public const int DefaultSampleSize = 64 * 1024;

    /// <summary>How many bytes the binary-content check looks at.</summary>
    public const int BinaryProbeSize = 8 * 1024;

    /// <summary>UTF-8 with no byte order mark - the encoding new files are created in.</summary>
    public static Encoding Utf8NoBom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The single byte encoding used for files that are not valid UTF-8: the operating system ANSI
    /// code page on Windows - the page legacy DOS-era files were written in - or Latin-1 when that
    /// page is unavailable or not single byte.
    /// </summary>
    /// <remarks>
    /// The legacy pages are served by the <see cref="CodePagesEncodingProvider"/> that the program
    /// registers on startup. Whatever is chosen must map all 256 byte values to distinct
    /// characters and back, so an unrecognised file still round-trips byte for byte through a
    /// load, an edit and a save - which is why a double byte ANSI page (the CJK ones) is refused
    /// in favour of Latin-1 until per-file code page switching exists. On Unix the fallback is
    /// UTF-8.
    /// </remarks>
    public static Encoding AnsiFallback { get; } = ResolveAnsiFallback();

    /// <summary>
    /// Classifies a sample of bytes taken from the start of a file.
    /// </summary>
    /// <param name="sample">
    /// The first bytes of the file. A few kilobytes is plenty; passing the whole file is fine too.
    /// </param>
    /// <returns>
    /// The encoding to decode with, and whether the sample started with a byte order mark. The
    /// encoding's own preamble matches that flag, so it can be handed straight to a
    /// <see cref="StreamWriter"/> when saving.
    /// </returns>
    public static (Encoding Encoding, bool HasBom) Detect(ReadOnlySpan<byte> sample)
    {
        if (StartsWith(sample, [0xEF, 0xBB, 0xBF]))
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), true);
        }

        // UTF-32 LE begins FF FE 00 00, which also matches the UTF-16 LE mark; test the longer
        // mark first or every UTF-32 LE file is misread as UTF-16.
        if (StartsWith(sample, [0xFF, 0xFE, 0x00, 0x00]))
        {
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: true), true);
        }

        if (StartsWith(sample, [0x00, 0x00, 0xFE, 0xFF]))
        {
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: true), true);
        }

        if (StartsWith(sample, [0xFF, 0xFE]))
        {
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true), true);
        }

        if (StartsWith(sample, [0xFE, 0xFF]))
        {
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true), true);
        }

        return IsValidUtf8(sample) ? (Utf8NoBom, false) : (AnsiFallback, false);
    }

    /// <summary>
    /// Reads the head of a file and classifies it. A file that cannot be read is reported as
    /// BOM-less UTF-8 rather than throwing, so callers can report the real I/O failure themselves.
    /// </summary>
    /// <param name="path">The file to inspect.</param>
    /// <param name="sampleSize">How many bytes to read; clamped to at least four.</param>
    /// <returns>The encoding and BOM flag, exactly as <see cref="Detect(ReadOnlySpan{byte})"/> reports them.</returns>
    public static (Encoding Encoding, bool HasBom) DetectFile(string path, int sampleSize = DefaultSampleSize)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            return Detect(ReadSample(stream, sampleSize));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (Utf8NoBom, false);
        }
    }

    /// <summary>Reads up to <paramref name="sampleSize"/> bytes from the start of a stream.</summary>
    /// <param name="stream">A readable, seekable stream; it is left rewound to its start.</param>
    /// <param name="sampleSize">How many bytes to read; clamped to at least four.</param>
    /// <returns>The bytes actually read, which may be fewer for a short file.</returns>
    public static byte[] ReadSample(Stream stream, int sampleSize = DefaultSampleSize)
    {
        ArgumentNullException.ThrowIfNull(stream);

        int want = Math.Max(4, sampleSize);
        if (stream.CanSeek)
        {
            stream.Position = 0;
            want = (int)Math.Min(want, stream.Length);
        }

        var buffer = new byte[want];
        int read = stream.ReadAtLeast(buffer, want, throwOnEndOfStream: false);
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        return read == buffer.Length ? buffer : buffer[..read];
    }

    /// <summary>The length in bytes of the byte order mark at the start of a sample, or zero.</summary>
    /// <param name="sample">The bytes to inspect.</param>
    /// <returns>2, 3, 4 or 0.</returns>
    public static int BomLength(ReadOnlySpan<byte> sample)
    {
        if (StartsWith(sample, [0xEF, 0xBB, 0xBF]))
        {
            return 3;
        }

        if (StartsWith(sample, [0xFF, 0xFE, 0x00, 0x00]) || StartsWith(sample, [0x00, 0x00, 0xFE, 0xFF]))
        {
            return 4;
        }

        if (StartsWith(sample, [0xFF, 0xFE]) || StartsWith(sample, [0xFE, 0xFF]))
        {
            return 2;
        }

        return 0;
    }

    /// <summary>The byte order mark an encoding writes, or an empty span when it writes none.</summary>
    /// <param name="encoding">The encoding.</param>
    /// <returns>The preamble bytes.</returns>
    public static ReadOnlySpan<byte> Preamble(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return encoding.GetPreamble();
    }

    /// <summary>
    /// How many bytes one code unit of an encoding occupies, which is the alignment a byte-level
    /// scan of the file has to respect.
    /// </summary>
    /// <param name="encoding">The encoding.</param>
    /// <returns>4 for UTF-32, 2 for UTF-16, otherwise 1.</returns>
    public static int CodeUnitSize(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return encoding.CodePage switch
        {
            12000 or 12001 => 4,
            1200 or 1201 => 2,
            _ => 1,
        };
    }

    /// <summary><see langword="true"/> when an encoding stores its code units most significant byte first.</summary>
    /// <param name="encoding">The encoding.</param>
    /// <returns><see langword="true"/> for UTF-16BE and UTF-32BE.</returns>
    public static bool IsBigEndian(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return encoding.CodePage is 1201 or 12001;
    }

    /// <summary>
    /// Tests whether bytes form well-formed UTF-8: correct continuation bytes, no over-long
    /// encodings, no surrogate code points and nothing above U+10FFFF.
    /// </summary>
    /// <param name="data">The bytes to validate.</param>
    /// <param name="allowTruncatedTail">
    /// When set (the default), a multi-byte sequence cut off by the end of the buffer is accepted,
    /// because a sample is only a prefix of the file.
    /// </param>
    /// <returns><see langword="true"/> when the bytes decode as UTF-8.</returns>
    public static bool IsValidUtf8(ReadOnlySpan<byte> data, bool allowTruncatedTail = true)
    {
        int i = 0;
        while (i < data.Length)
        {
            byte b = data[i];

            if (b < 0x80)
            {
                i++;
                continue;
            }

            int extra;
            int min;
            int value;

            if (b is >= 0xC2 and <= 0xDF)
            {
                extra = 1;
                min = 0x80;
                value = b & 0x1F;
            }
            else if (b is >= 0xE0 and <= 0xEF)
            {
                extra = 2;
                min = 0x800;
                value = b & 0x0F;
            }
            else if (b is >= 0xF0 and <= 0xF4)
            {
                extra = 3;
                min = 0x10000;
                value = b & 0x07;
            }
            else
            {
                // 0x80..0xBF is a stray continuation byte; 0xC0/0xC1 are over-long two byte
                // starters; 0xF5..0xFF would exceed U+10FFFF.
                return false;
            }

            if (i + extra >= data.Length)
            {
                return allowTruncatedTail && ContinuationRun(data[(i + 1)..]);
            }

            for (int k = 1; k <= extra; k++)
            {
                byte c = data[i + k];
                if ((c & 0xC0) != 0x80)
                {
                    return false;
                }

                value = (value << 6) | (c & 0x3F);
            }

            if (value < min || value > 0x10FFFF || (value >= 0xD800 && value <= 0xDFFF))
            {
                return false;
            }

            i += extra + 1;
        }

        return true;
    }

    /// <summary>
    /// <see langword="true"/> when a sample contains a NUL byte, which is the test the editor uses
    /// to refuse a binary file. UTF-16 and UTF-32 text is full of NUL bytes, so a sample that
    /// starts with one of their byte order marks is never called binary.
    /// </summary>
    /// <param name="sample">The bytes to inspect; only the first <see cref="BinaryProbeSize"/> matter.</param>
    /// <returns><see langword="true"/> when the content looks like something other than text.</returns>
    public static bool LooksBinary(ReadOnlySpan<byte> sample)
    {
        int bom = BomLength(sample);
        if (bom is 2 or 4)
        {
            return false;
        }

        var probe = sample[bom..];
        if (probe.Length > BinaryProbeSize)
        {
            probe = probe[..BinaryProbeSize];
        }

        return probe.IndexOf((byte)0) >= 0;
    }

    /// <summary>
    /// The name shown on the viewer and editor status lines, for example <c>"UTF-8"</c>,
    /// <c>"UTF-8 BOM"</c> or <c>"UTF-16LE BOM"</c>.
    /// </summary>
    /// <param name="encoding">The encoding.</param>
    /// <param name="hasBom">Whether the file carries a byte order mark.</param>
    /// <returns>The display name.</returns>
    public static string DisplayName(Encoding encoding, bool hasBom)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        string name = encoding.CodePage switch
        {
            65001 => "UTF-8",
            1200 => "UTF-16LE",
            1201 => "UTF-16BE",
            12000 => "UTF-32LE",
            12001 => "UTF-32BE",
            20127 => "ASCII",
            _ when encoding.CodePage == AnsiFallback.CodePage => "ANSI",
            _ => encoding.WebName.ToUpperInvariant(),
        };

        return hasBom ? name + " BOM" : name;
    }

    /// <summary>
    /// Decodes bytes, skipping any byte order mark so that the mark never shows up as a stray
    /// U+FEFF at the start of the first line.
    /// </summary>
    /// <param name="bytes">The bytes to decode.</param>
    /// <param name="encoding">The encoding to decode with.</param>
    /// <returns>The decoded text.</returns>
    public static string DecodeSkippingBom(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        int skip = BomLength(bytes);
        string text = encoding.GetString(bytes[skip..]);

        // A decoder can still surface U+FEFF when the caller supplied an encoding whose preamble
        // did not match the bytes; drop it, it is never content.
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) =>
        data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);

    /// <summary>Every byte in the span is a UTF-8 continuation byte (used for a truncated tail).</summary>
    private static bool ContinuationRun(ReadOnlySpan<byte> tail)
    {
        foreach (byte b in tail)
        {
            if ((b & 0xC0) != 0x80)
            {
                return false;
            }
        }

        return true;
    }

    private static Encoding ResolveAnsiFallback()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Utf8NoBom;
        }

        try
        {
            int cp = NativeMethods.AnsiCodePage();

            // The legacy pages come from the CodePagesEncodingProvider registered at startup (see
            // Program). Only a single byte page is accepted: the CJK ANSI pages are double byte,
            // and decoding an arbitrary unrecognised file with one of those would not round-trip
            // byte for byte on save.
            if (cp > 0 && cp != 65001)
            {
                Encoding ansi = Encoding.GetEncoding(cp);
                if (ansi.IsSingleByte)
                {
                    return ansi;
                }
            }
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PlatformNotSupportedException or EntryPointNotFoundException or DllNotFoundException)
        {
            // Fall through to Latin-1.
        }

        return Encoding.Latin1;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern uint GetACP();

        internal static int AnsiCodePage()
        {
            try
            {
                return (int)GetACP();
            }
            catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
            {
                return 0;
            }
        }
    }
}
