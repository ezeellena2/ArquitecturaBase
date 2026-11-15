using System.IO.Compression;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Documents;

// How much the compressed streams of some PDF bytes inflate to, measured on the raw bytes before PdfPig opens them: PdfPig decodes the
// cross-reference stream and every object stream into memory without a limit, so a few hundred KiB of compressed zeros can hold
// gigabytes. Every «stream» keyword whose dictionary names /FlateDecode (also written /Fl or with #xx escapes), or whose data starts
// like a zlib stream, is inflated into nothing and only counted, against one budget for the whole file. A chain that decodes
// ASCII85 or hexadecimal before Flate (what ReportLab writes) is measured by decoding it in pieces into the inflater; one that puts
// Flate together with LZW or RunLength cannot be measured here, so it is not read. A PDF with an /Encrypt entry is Protected before
// PdfPig sees it: its streams are ciphertext, so no measure of the raw bytes says what PdfPig would inflate after decrypting them.
internal static class FormPdfStreams
{
    private static readonly string[] Flate = ["FlateDecode", "Fl"];
    private static readonly string[] Ascii85 = ["ASCII85Decode", "A85"];
    private static readonly string[] Hexadecimal = ["ASCIIHexDecode", "AHx"];
    private static readonly string[] Unmeasurable = ["LZWDecode", "LZW", "RunLengthDecode", "RL"];

    // Whether some dictionary names /Encrypt (also with #xx escapes): the file asks for a password, empty or not.
    public static bool IsEncrypted(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var span = pdf.AsSpan();
        for (var index = 0; index < span.Length; index++)
        {
            if (span[index] != (byte)'/') continue;
            var end = index + 1;
            while (end < span.Length && !IsSeparator(span[end])) end++;
            if (end - index - 1 is >= 7 and <= 21 && Decode(span[(index + 1)..end]) == "Encrypt") return true;
            index = end - 1;
        }
        return false;
    }

    // False when the streams inflate to more than the budget, or there are more compressed ones than the limit, or a chain of
    // filters hides how much they inflate to.
    public static bool Fits(byte[] pdf, long budget, int maximumStreams)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var remaining = budget;
        var streams = 0;
        var regionStart = 0;
        var buffer = new byte[64 * 1024];
        for (var index = 0; index < pdf.Length; index++)
        {
            if (pdf[index] != (byte)'s' || !IsKeyword(pdf, index)) continue;
            var data = index + 6;
            if (data < pdf.Length && pdf[data] == (byte)'\r') data++;
            if (data < pdf.Length && pdf[data] == (byte)'\n') data++;
            var filters = Filters(pdf.AsSpan(regionStart, index - regionStart));
            regionStart = data;
            index = data - 1;
            if (filters.Flate && filters.Unmeasurable) return false;
            if (filters.Flate && filters.Decoding != Encoding.None)
            {
                // The decoders come before Flate in the array, as PdfPig applies them; Flate first cannot be measured.
                if (!filters.DecodedFirst) return false;
                if (++streams > maximumStreams) return false;
                var decoded = InflateDecoded(pdf, data, filters.Decoding, remaining, buffer);
                if (decoded < 0) return false;
                remaining -= decoded;
                continue;
            }
            var header = IsZlibHeader(pdf, data);
            if (!filters.Flate && !header) continue;
            if (++streams > maximumStreams) return false;
            var inflated = Inflate(pdf, header ? data + 2 : data, remaining, buffer);
            if (inflated < 0) return false;
            remaining -= inflated;
        }
        return true;
    }

    // The bytes the stream inflates to, or -1 over the limit. A stream that is not valid deflate data ends where it breaks, as it does
    // for PdfPig, which would not read more than that either.
    private static long Inflate(byte[] pdf, int start, long limit, byte[] buffer)
    {
        if (start >= pdf.Length) return 0;
        using var source = new MemoryStream(pdf, start, pdf.Length - start, writable: false);
        return Count(source, limit, buffer);
    }

    // The same, for a stream that is ASCII85 or hexadecimal text of zlib data: it is decoded in pieces into the inflater.
    private static long InflateDecoded(byte[] pdf, int start, Encoding encoding, long limit, byte[] buffer)
    {
        if (start >= pdf.Length) return 0;
        using var source = new MemoryStream(pdf, start, pdf.Length - start, writable: false);
        using var decoder = new AsciiDecodingStream(source, encoding == Encoding.Hexadecimal);
        var header = new byte[2];
        if (decoder.Read(header, 0, 2) < 2) return 0;
        // A zlib header is skipped; anything else is raw deflate data that starts with those two bytes.
        if (!((header[0] & 0x0F) == 8 && (header[0] * 256 + header[1]) % 31 == 0)) decoder.Unread(header);
        return Count(decoder, limit, buffer);
    }

    private static long Count(Stream source, long limit, byte[] buffer)
    {
        long total = 0;
        try
        {
            using var deflate = new DeflateStream(source, CompressionMode.Decompress, leaveOpen: true);
            int read;
            while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > limit) return -1;
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException)
        {
        }
        return total;
    }

    private static bool IsKeyword(byte[] pdf, int index) =>
        pdf.AsSpan(index).StartsWith("stream"u8) && (index == 0 || !IsLetter(pdf[index - 1]))
        && index + 6 < pdf.Length && pdf[index + 6] is (byte)'\r' or (byte)'\n';

    private static bool IsZlibHeader(byte[] pdf, int index) =>
        index + 1 < pdf.Length && (pdf[index] & 0x0F) == 8 && (pdf[index] * 256 + pdf[index + 1]) % 31 == 0;

    // The names of the dictionary before a stream, read the way PdfPig reads them: with their #xx escapes decoded.
    private static StreamFilters Filters(ReadOnlySpan<byte> region)
    {
        var flate = -1;
        var ascii = -1;
        var encoding = Encoding.None;
        var unmeasurable = false;
        for (var index = 0; index < region.Length; index++)
        {
            if (region[index] != (byte)'/') continue;
            var end = index + 1;
            while (end < region.Length && !IsSeparator(region[end])) end++;
            var name = Decode(region[(index + 1)..end]);
            if (flate < 0 && Array.IndexOf(Flate, name) >= 0) flate = index;
            var kind = Array.IndexOf(Ascii85, name) >= 0 ? Encoding.Ascii85 : Array.IndexOf(Hexadecimal, name) >= 0 ? Encoding.Hexadecimal : Encoding.None;
            if (kind != Encoding.None)
            {
                // Two different text decoders in one chain are not measured.
                if (encoding != Encoding.None && encoding != kind) unmeasurable = true;
                encoding = kind;
                if (ascii < 0) ascii = index;
            }
            unmeasurable |= Array.IndexOf(Unmeasurable, name) >= 0;
            index = end - 1;
        }
        return new(flate >= 0, encoding, unmeasurable, flate >= 0 && ascii >= 0 && ascii < flate);
    }

    private enum Encoding { None, Ascii85, Hexadecimal }

    private readonly record struct StreamFilters(bool Flate, Encoding Decoding, bool Unmeasurable, bool DecodedFirst);

    private static string Decode(ReadOnlySpan<byte> name)
    {
        var chars = new char[name.Length];
        var length = 0;
        for (var index = 0; index < name.Length; index++)
        {
            if (name[index] == (byte)'#' && index + 2 < name.Length && Hex(name[index + 1]) is var high and >= 0 && Hex(name[index + 2]) is var low and >= 0)
            {
                chars[length++] = (char)(high * 16 + low);
                index += 2;
            }
            else chars[length++] = (char)name[index];
        }
        return new string(chars, 0, length);
    }

    private static int Hex(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - '0',
        >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
        _ => -1,
    };

    private static bool IsLetter(byte value) => value is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z';

    private static bool IsSeparator(byte value) =>
        value is 0 or 9 or 10 or 12 or 13 or 32 or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}'
            or (byte)'/' or (byte)'%';
}
