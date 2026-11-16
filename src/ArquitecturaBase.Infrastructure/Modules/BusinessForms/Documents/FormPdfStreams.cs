using System.IO.Compression;
using System.Security.Cryptography;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Documents;

// How much the compressed streams of some PDF bytes inflate to, measured on the raw bytes before PdfPig opens them: PdfPig decodes the
// cross-reference stream and every object stream into memory without a limit, so a few hundred KiB of compressed zeros can hold
// gigabytes. Every «stream» keyword whose dictionary names /FlateDecode (also written /Fl or with #xx escapes), or whose data starts
// like a zlib stream, is inflated into nothing and only counted, against one budget for the whole file. A chain that decodes
// ASCII85 or hexadecimal before Flate (what ReportLab writes) is measured by decoding it in pieces into the inflater; one that puts
// Flate together with LZW or RunLength cannot be measured here, so it is not read. In a PDF encrypted only for permissions (the empty
// password opens it, FormPdfSecurity) the streams are ciphertext: each one that names Flate is also decrypted, as its object's key
// says, and measured, against the same budget. One that decrypts to something without a zlib header (and had none in the clear) is
// not read: a wrong key here would be a stream PdfPig inflates and this does not.
internal static class FormPdfStreams
{
    private static readonly string[] Flate = ["FlateDecode", "Fl"];
    private static readonly string[] Ascii85 = ["ASCII85Decode", "A85"];
    private static readonly string[] Hexadecimal = ["ASCIIHexDecode", "AHx"];
    private static readonly string[] Unmeasurable = ["LZWDecode", "LZW", "RunLengthDecode", "RL"];

    // False when the streams inflate to more than the budget, or there are more compressed ones than the limit, or a chain of
    // filters hides how much they inflate to.
    public static bool Fits(byte[] pdf, long budget, int maximumStreams, FormPdfSecurity.StreamDecryptor? decryptor = null)
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
            var region = pdf.AsSpan(regionStart, index - regionStart);
            var filters = Filters(region);
            regionStart = data;
            index = data - 1;
            if (filters.Flate && filters.Unmeasurable) return false;
            var decrypting = decryptor is { Decrypts: true } && filters.Flate;
            // A stream with its own crypt filter is not decrypted with the document's key: it cannot be measured.
            if (decrypting && filters.Crypt) return false;
            long plain;
            bool plainZlib;
            if (filters.Flate && filters.Decoding != Encoding.None)
            {
                // The decoders come before Flate in the array, as PdfPig applies them; Flate first cannot be measured.
                if (!filters.DecodedFirst) return false;
                if (++streams > maximumStreams) return false;
                plain = InflateDecoded(pdf, data, filters.Decoding, null, 0, 0, remaining, buffer, out plainZlib);
            }
            else
            {
                plainZlib = IsZlibHeader(pdf, data);
                if (!filters.Flate && !plainZlib) continue;
                if (++streams > maximumStreams) return false;
                plain = Inflate(pdf, plainZlib ? data + 2 : data, remaining, buffer);
            }
            if (plain < 0) return false;
            remaining -= plain;
            if (!decrypting) continue;
            if (!TryFindObject(region, out var number, out var generation) || ++streams > maximumStreams) return false;
            var decrypted = InflateDecoded(pdf, data, filters.Decoding, decryptor, number, generation, remaining, buffer, out var decryptedZlib);
            if (decrypted < 0) return false;
            remaining -= decrypted;
            if (!decryptedZlib && !plainZlib && !IsTiny(pdf, data)) return false;
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
    // With a decryptor the stream is ciphertext of the object «number generation», decrypted first and then decoded, in pieces.
    // «zlib» says whether it had a header.
    private static long InflateDecoded(byte[] pdf, int start, Encoding encoding, FormPdfSecurity.StreamDecryptor? decryptor, long number, long generation,
        long limit, byte[] buffer, out bool zlib)
    {
        zlib = false;
        if (start >= pdf.Length) return 0;
        try
        {
            using var source = new MemoryStream(pdf, start, pdf.Length - start, writable: false);
            using var plain = decryptor is null ? source : decryptor.Open(source, number, generation);
            using var decoder = encoding == Encoding.None ? null : new AsciiDecodingStream(plain, encoding == Encoding.Hexadecimal);
            Stream text = decoder ?? plain;
            var header = new byte[2];
            var read = 0;
            while (read < 2)
            {
                var count = text.Read(header, read, 2 - read);
                if (count == 0) break;
                read += count;
            }
            if (read < 2) return 0;
            // A zlib header is skipped; anything else is raw deflate data that starts with those two bytes, but only in the clear.
            zlib = (header[0] & 0x0F) == 8 && (header[0] * 256 + header[1]) % 31 == 0;
            if (!zlib)
            {
                if (decryptor is not null) return 0;
                decoder!.Unread(header);
            }
            return Count(text, limit, buffer);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException or CryptographicException)
        {
            return 0;
        }
    }

    // The ciphertext is at most one block: an empty stream, whose decryption has no header to show.
    private static bool IsTiny(byte[] pdf, int data)
    {
        var at = pdf.AsSpan(data, Math.Min(pdf.Length - data, 64)).IndexOf("endstream"u8);
        return at is >= 0 and <= 18;
    }

    // The «number generation obj» that precedes the dictionary of a stream: the last one in the region.
    private static bool TryFindObject(ReadOnlySpan<byte> region, out long number, out long generation)
    {
        number = generation = 0;
        var end = region.Length;
        while (end > 0)
        {
            var at = region[..end].LastIndexOf("obj"u8);
            if (at < 0) return false;
            end = at;
            if (at > 0 && IsLetter(region[at - 1]) || at + 3 < region.Length && IsLetter(region[at + 3])) continue;
            return FormPdfSecurity.TryReadHeader(region, at, out number, out generation);
        }
        return false;
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
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException or CryptographicException)
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
        var crypt = false;
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
            crypt |= name == "Crypt";
            index = end - 1;
        }
        return new(flate >= 0, encoding, unmeasurable, flate >= 0 && ascii >= 0 && ascii < flate, crypt);
    }

    private enum Encoding { None, Ascii85, Hexadecimal }

    private readonly record struct StreamFilters(bool Flate, Encoding Decoding, bool Unmeasurable, bool DecodedFirst, bool Crypt);

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
