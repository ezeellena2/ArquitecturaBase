using System.IO.Compression;
using System.Security.Cryptography;
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
    // «encoding» is «», «ascii85» or «hex»: the bomb as text, with the chain of filters that ReportLab and others write.
    public static byte[] BombInReferences(string encoding = "")
    {
        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.Latin1.GetBytes(text));
        Write("%PDF-1.5\n");
        var offsets = Pages(output);
        var xrefOffset = output.Position;
        var bomb = Encode(Zeros.Value, encoding);
        Write($"4 0 obj\n<< /Type /XRef /Size 5 /W [1 4 2] /Root 1 0 R /Filter {Filter(encoding)} /Length {bomb.Length} >>\nstream\n");
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

    // A readable PDF of one page whose cross-reference stream is written with the same chains: it must still be read, not refused.
    public static byte[] ReadableWithEncodedReferences(string encoding)
    {
        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.Latin1.GetBytes(text));
        Write("%PDF-1.5\n");
        var offsets = Pages(output);
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
        foreach (var offset in offsets) Entry(1, offset, 0);
        Entry(1, xrefOffset, 0);
        using var packed = new MemoryStream();
        using (var deflate = new ZLibStream(packed, CompressionLevel.SmallestSize, true)) deflate.Write(entries.ToArray());
        var body = Encode(packed.ToArray(), encoding);
        Write($"4 0 obj\n<< /Type /XRef /Size 5 /W [1 4 2] /Root 1 0 R /Filter {Filter(encoding)} /Length {body.Length} >>\nstream\n");
        output.Write(body);
        Write($"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    // A PDF with an /Encrypt entry in its trailer: it asks for a password, empty or not.
    public static byte[] Encrypted()
    {
        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.Latin1.GetBytes(text));
        Write("%PDF-1.4\n");
        var offsets = Pages(output);
        var xref = output.Position;
        Write("xref\n0 4\n0000000000 65535 f \n");
        foreach (var offset in offsets) Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size 4 /Root 1 0 R /Encrypt << /Filter /Standard /V 1 /R 2 /O (x) /U (y) /P -4 >> >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    // The schemes of the standard security handler: RC4 of 40 bits (R2), RC4 of 128 (R3), AES-128 (R4) and AES-256 (R6).
    public static readonly string[] Schemes = ["rc4-40", "rc4-128", "aes-128", "aes-256"];

    // A PDF whose only page lives in an object stream, encrypted by the standard security handler (ISO 32000-1 §7.6.3, ISO 32000-2
    // for AES-256) with the user password «openingPassword», empty by default: with an empty one the file only restricts permissions
    // and opens without asking. «bomb» fills the object stream with InflatedBytes of spaces. Made here with its own RC4, AES and
    // hash, not the reader's, so that the two agree only if both follow the standard.
    public static byte[] EncryptedObjectStream(string scheme, bool bomb, string openingPassword = "")
    {
        var password = Encoding.Latin1.GetBytes(openingPassword);
        var id = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();
        var owner = Enumerable.Range(0, 32).Select(value => (byte)(value * 7 + 3)).ToArray();
        const int permissions = -4;
        var (version, revision, bits) = scheme switch { "rc4-40" => (1, 2, 40), "rc4-128" => (2, 3, 128), "aes-128" => (4, 4, 128), _ => (5, 6, 256) };
        var aes = version >= 4;
        byte[] fileKey;
        string entries;
        if (version < 5)
        {
            fileKey = StandardKey(password, owner, permissions, id, revision, bits / 8);
            var user = UserEntry(fileKey, id, revision);
            entries = $"/O <{Convert.ToHexString(owner)}> /U <{Convert.ToHexString(user)}> /P {permissions}";
        }
        else
        {
            fileKey = Enumerable.Range(0, 32).Select(value => (byte)(value * 11 + 5)).ToArray();
            byte[] validation = [1, 2, 3, 4, 5, 6, 7, 8], keySalt = [8, 7, 6, 5, 4, 3, 2, 1];
            byte[] user = [.. Hash2B(password, validation), .. validation, .. keySalt];
            using var cipher = Aes.Create();
            cipher.Key = Hash2B(password, keySalt);
            var userKey = cipher.EncryptCbc(fileKey, new byte[16], PaddingMode.None);
            entries = $"/O <{Convert.ToHexString(new byte[48])}> /OE <{Convert.ToHexString(new byte[32])}> /U <{Convert.ToHexString(user)}> "
                + $"/UE <{Convert.ToHexString(userKey)}> /Perms <{Convert.ToHexString(new byte[16])}> /P {permissions}";
        }
        var handler = version switch
        {
            1 => "/V 1 /R 2 /Length 40",
            2 => "/V 2 /R 3 /Length 128",
            4 => "/V 4 /R 4 /Length 128 /CF << /StdCF << /CFM /AESV2 /AuthEvent /DocOpen /Length 16 >> >> /StmF /StdCF /StrF /StdCF",
            _ => "/V 5 /R 6 /Length 256 /CF << /StdCF << /CFM /AESV3 /AuthEvent /DocOpen /Length 32 >> >> /StmF /StdCF /StrF /StdCF",
        };

        byte[] body;
        if (bomb) body = Spaces.Value;
        else
        {
            using var packed = new MemoryStream();
            using (var deflate = new ZLibStream(packed, CompressionLevel.SmallestSize, true))
                deflate.Write("3 0 << /Type /Page /MediaBox [0 0 10 10] >> "u8);
            body = packed.ToArray();
        }
        var objectKey = version == 5 ? fileKey : ObjectKey(fileKey, 4, 0, aes);
        var encrypted = aes ? EncryptAes(objectKey, body) : Rc4(objectKey, body);

        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.Latin1.GetBytes(text));
        Write("%PDF-1.7\n");
        var offsets = new long[2];
        offsets[0] = output.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[1] = output.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        var stream = output.Position;
        Write($"4 0 obj\n<< /Type /ObjStm /N 1 /First 4 /Filter /FlateDecode /Length {encrypted.Length} >>\nstream\n");
        output.Write(encrypted);
        Write("\nendstream\nendobj\n");
        var encryptObject = output.Position;
        Write($"6 0 obj\n<< /Filter /Standard {handler} {entries} >>\nendobj\n");
        var xrefOffset = output.Position;
        using var references = new MemoryStream();
        void Entry(byte type, long second, int third)
        {
            references.WriteByte(type);
            for (var shift = 24; shift >= 0; shift -= 8) references.WriteByte((byte)(second >> shift));
            references.WriteByte((byte)(third >> 8));
            references.WriteByte((byte)third);
        }
        Entry(0, 0, 65535);
        Entry(1, offsets[0], 0);
        Entry(1, offsets[1], 0);
        Entry(2, 4, 0);
        Entry(1, stream, 0);
        Entry(1, xrefOffset, 0);
        Entry(1, encryptObject, 0);
        Write($"5 0 obj\n<< /Type /XRef /Size 7 /W [1 4 2] /Root 1 0 R /Encrypt 6 0 R /ID [<{Convert.ToHexString(id)}> <{Convert.ToHexString(id)}>] /Length {references.Length} >>\nstream\n");
        output.Write(references.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    private static readonly byte[] Padding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    // Algorithm 2: the file key from the user password, the owner entry, the permissions and the first file identifier.
    private static byte[] StandardKey(byte[] password, byte[] owner, int permissions, byte[] id, int revision, int length)
    {
        var padded = password.Concat(Padding).Take(32).ToArray();
        var hash = MD5.HashData([.. padded, .. owner, .. BitConverter.GetBytes(permissions), .. id]);
        if (revision >= 3)
            for (var round = 0; round < 50; round++) hash = MD5.HashData(hash[..length]);
        return hash[..length];
    }

    // Algorithms 4 and 5: the user entry of a file whose key is known.
    private static byte[] UserEntry(byte[] key, byte[] id, int revision)
    {
        if (revision == 2) return Rc4(key, Padding);
        var value = Rc4(key, MD5.HashData([.. Padding, .. id]));
        for (var round = 1; round <= 19; round++) value = Rc4(key.Select(item => (byte)(item ^ round)).ToArray(), value);
        return [.. value, .. new byte[16]];
    }

    private static byte[] ObjectKey(byte[] fileKey, int number, int generation, bool aes)
    {
        byte[] input = [.. fileKey, (byte)number, (byte)(number >> 8), (byte)(number >> 16), (byte)generation, (byte)(generation >> 8)];
        if (aes) input = [.. input, 0x73, 0x41, 0x6C, 0x54];
        return MD5.HashData(input)[..Math.Min(fileKey.Length + 5, 16)];
    }

    private static byte[] EncryptAes(byte[] key, byte[] data)
    {
        var vector = Enumerable.Range(0, 16).Select(value => (byte)(value * 13 + 1)).ToArray();
        using var cipher = Aes.Create();
        cipher.Key = key;
        return [.. vector, .. cipher.EncryptCbc(data, vector, PaddingMode.PKCS7)];
    }

    private static byte[] Rc4(byte[] key, byte[] data)
    {
        var state = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        for (int index = 0, other = 0; index < 256; index++)
        {
            other = (other + state[index] + key[index % key.Length]) & 255;
            (state[index], state[other]) = (state[other], state[index]);
        }
        var result = new byte[data.Length];
        for (int position = 0, left = 0, right = 0; position < data.Length; position++)
        {
            left = (left + 1) & 255;
            right = (right + state[left]) & 255;
            (state[left], state[right]) = (state[right], state[left]);
            result[position] = (byte)(data[position] ^ state[(state[left] + state[right]) & 255]);
        }
        return result;
    }

    // ISO 32000-2 algorithm 2.B, with no user data: the hash of an AES-256 (R6) password and a salt.
    private static byte[] Hash2B(byte[] password, byte[] salt)
    {
        var key = SHA256.HashData([.. password, .. salt]);
        byte[] last = [0];
        for (var round = 0; round < 64 || last[^1] > round - 32; round++)
        {
            var unit = password.Concat(key).ToArray();
            var repeated = Enumerable.Repeat(unit, 64).SelectMany(part => part).ToArray();
            using var cipher = Aes.Create();
            cipher.Key = key[..16];
            last = cipher.EncryptCbc(repeated, key[16..32], PaddingMode.None);
            var remainder = last.Take(16).Sum(item => item) % 3;
            key = remainder == 0 ? SHA256.HashData(last) : remainder == 1 ? SHA384.HashData(last) : SHA512.HashData(last);
        }
        return key[..32];
    }

    private static string Filter(string encoding) => encoding switch
    {
        "ascii85" => "[/ASCII85Decode /FlateDecode]",
        "hex" => "[/ASCIIHexDecode /FlateDecode]",
        _ => "/FlateDecode",
    };

    private static byte[] Encode(byte[] data, string encoding) => encoding switch
    {
        "ascii85" => Ascii85(data),
        "hex" => Encoding.ASCII.GetBytes(Convert.ToHexString(data) + ">"),
        _ => data,
    };

    private static byte[] Ascii85(byte[] data)
    {
        var text = new StringBuilder(data.Length * 5 / 4 + 8);
        for (var index = 0; index < data.Length; index += 4)
        {
            var count = Math.Min(4, data.Length - index);
            uint group = 0;
            for (var offset = 0; offset < 4; offset++) group = group << 8 | (offset < count ? data[index + offset] : (byte)0);
            if (group == 0 && count == 4) { text.Append('z'); continue; }
            var digits = new char[5];
            for (var digit = 4; digit >= 0; digit--) { digits[digit] = (char)(group % 85 + '!'); group /= 85; }
            text.Append(digits, 0, count + 1);
        }
        text.Append("~>");
        return Encoding.ASCII.GetBytes(text.ToString());
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
