namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Documents;

// How many indirect objects some PDF bytes can define, counted on the raw bytes before PdfPig opens them. PdfPig follows the page
// tree and chains of references recursively, and no chain is longer than the objects it is made of, so this count bounds how deep
// it can go, and how long and how much memory it takes. It is an upper bound that formatting cannot hide:
// - every «obj» keyword that does not end a longer word (endobj), whatever comes before it, counts as an object header;
// - every name «N», also written with #xx escapes as PdfPig decodes it, adds the number that follows it, after any whitespace or
//   comment: an object stream declares its objects so, with or without its /Type, and any other /N (the colors of an ICC profile,
//   a function) only adds a little. A reference (/N 12 0 R, the normal appearance of an annotation) adds nothing: PdfPig does not
//   take one as the count of an object stream.
internal static class FormPdfObjects
{
    // Counting stops just past the limit: the bytes are rejected anyway.
    public static long Count(ReadOnlySpan<byte> pdf, long limit)
    {
        long count = 0;
        for (var index = 0; index < pdf.Length && count <= limit; index++)
        {
            if (pdf[index] == (byte)'o' && IsKeyword(pdf, index))
            {
                count++;
                index += 2;
            }
            else if (pdf[index] == (byte)'/')
            {
                var end = index + 1;
                while (end < pdf.Length && !IsWhiteSpace(pdf[end]) && !IsDelimiter(pdf[end])) end++;
                if (IsNameN(pdf[(index + 1)..end])) count += Declared(pdf, end);
                index = end - 1;
            }
        }
        return count;
    }

    private static bool IsKeyword(ReadOnlySpan<byte> pdf, int index) =>
        pdf[index..].StartsWith("obj"u8) && (index == 0 || !IsLetter(pdf[index - 1]))
        && (index + 3 == pdf.Length || IsWhiteSpace(pdf[index + 3]) || IsDelimiter(pdf[index + 3]));

    private static bool IsNameN(ReadOnlySpan<byte> name) =>
        name is [(byte)'N'] || (name is [(byte)'#', var high, var low] && Hex(high) * 16 + Hex(low) == 'N');

    // The number after the name, its integer part when it is a real, and nothing when it is not a number or is a reference.
    private static long Declared(ReadOnlySpan<byte> pdf, int index)
    {
        index = SkipSpace(pdf, index);
        if (index < pdf.Length && pdf[index] is (byte)'+' or (byte)'-')
        {
            if (pdf[index] == (byte)'-') return 0;
            index++;
        }
        var start = index;
        long value = 0;
        for (; index < pdf.Length && IsDigit(pdf[index]); index++) value = Math.Min(value * 10 + pdf[index] - '0', int.MaxValue);
        if (index == start) return 0;
        if (index < pdf.Length && pdf[index] == (byte)'.')
        {
            while (++index < pdf.Length && IsDigit(pdf[index])) { }
            return value;
        }
        var generation = SkipSpace(pdf, index);
        var digits = generation;
        while (digits < pdf.Length && IsDigit(pdf[digits])) digits++;
        if (digits == generation) return value;
        var keyword = SkipSpace(pdf, digits);
        var reference = keyword < pdf.Length && pdf[keyword] == (byte)'R'
            && (keyword + 1 == pdf.Length || IsWhiteSpace(pdf[keyword + 1]) || IsDelimiter(pdf[keyword + 1]));
        return reference ? 0 : value;
    }

    private static int SkipSpace(ReadOnlySpan<byte> pdf, int index)
    {
        while (index < pdf.Length)
        {
            if (IsWhiteSpace(pdf[index])) index++;
            else if (pdf[index] == (byte)'%')
                while (index < pdf.Length && pdf[index] is not ((byte)'\n' or (byte)'\r')) index++;
            else break;
        }
        return index;
    }

    private static int Hex(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - '0',
        >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
        _ => -256,
    };

    private static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';

    private static bool IsLetter(byte value) => value is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z';

    private static bool IsWhiteSpace(byte value) => value is 0 or 9 or 10 or 12 or 13 or 32;

    private static bool IsDelimiter(byte value) =>
        value is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';
}
