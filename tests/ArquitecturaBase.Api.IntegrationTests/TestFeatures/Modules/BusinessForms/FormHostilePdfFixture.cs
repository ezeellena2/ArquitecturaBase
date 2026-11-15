using System.IO.Compression;
using System.Text;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

// PDFs made to attack the reader (F6): a few hundred KiB that inflate to half a GiB, in the cross-reference stream or in an object
// stream, which the object count of the raw bytes cannot see.
public static class FormHostilePdfFixture
{
    public const long InflatedBytes = 512L * 1024 * 1024;

    private static readonly Lazy<byte[]> Zeros = new(() => Compress(0), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<byte[]> Spaces = new(() => Compress((byte)' '), LazyThreadSafetyMode.ExecutionAndPublication);

    // The cross-reference stream of the file holds zeros: InflatedBytes of them, as free entries.
    public static byte[] BombInReferences()
    {
        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.Latin1.GetBytes(text));
        Write("%PDF-1.5\n");
        var offsets = Pages(output);
        var xrefOffset = output.Position;
        var bomb = Zeros.Value;
        Write($"4 0 obj\n<< /Type /XRef /Size 5 /W [1 4 2] /Root 1 0 R /Filter /FlateDecode /Length {bomb.Length} >>\nstream\n");
        output.Write(bomb);
        Write($"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    // The only page lives inside an object stream whose body is the page followed by InflatedBytes of spaces.
    public static byte[] BombInObjectStream()
    {
        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.Latin1.GetBytes(text));
        Write("%PDF-1.5\n");
        var offsets = new long[2];
        offsets[0] = output.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[1] = output.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        var stream = output.Position;
        var bomb = Spaces.Value;
        Write($"4 0 obj\n<< /Type /ObjStm /N 1 /First 4 /Filter /FlateDecode /Length {bomb.Length} >>\nstream\n");
        output.Write(bomb);
        Write("\nendstream\nendobj\n");
        var xrefOffset = output.Position;
        using var entries = new MemoryStream();
        void Entry(byte type, long second, int third)
        {
            entries.WriteByte(type);
            for (var shift = 24; shift >= 0; shift -= 8) entries.WriteByte((byte)(second >> shift));
            entries.WriteByte((byte)(third >> 8));
            entries.WriteByte((byte)third);
        }
        Entry(0, 0, 65535);
        Entry(1, offsets[0], 0);
        Entry(1, offsets[1], 0);
        Entry(2, 4, 0);
        Entry(1, stream, 0);
        Entry(1, xrefOffset, 0);
        Write($"5 0 obj\n<< /Type /XRef /Size 6 /W [1 4 2] /Root 1 0 R /Length {entries.Length} >>\nstream\n");
        output.Write(entries.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    private static long[] Pages(MemoryStream output)
    {
        var offsets = new long[3];
        string[] objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] >>"];
        for (var index = 0; index < objects.Length; index++)
        {
            offsets[index] = output.Position;
            output.Write(Encoding.Latin1.GetBytes($"{index + 1} 0 obj\n{objects[index]}\nendobj\n"));
        }
        return offsets;
    }

    // «header» first (the object stream's), then filler up to InflatedBytes.
    private static byte[] Compress(byte filler)
    {
        using var packed = new MemoryStream();
        using (var deflate = new ZLibStream(packed, CompressionLevel.SmallestSize, true))
        {
            if (filler != 0) deflate.Write("3 0 << /Type /Page /MediaBox [0 0 10 10] >> "u8);
            var block = new byte[1024 * 1024];
            Array.Fill(block, filler);
            for (long written = 0; written < InflatedBytes; written += block.Length) deflate.Write(block);
        }
        return packed.ToArray();
    }
}
