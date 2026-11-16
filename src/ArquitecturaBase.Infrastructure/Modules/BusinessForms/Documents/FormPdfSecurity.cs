using System.Security.Cryptography;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Documents;

// The standard security handler of a PDF (ISO 32000-1 §7.6.3, revisions 2 to 4, and the AES-256 extension, revisions 5 and 6),
// read from the raw bytes with the empty user password. A file encrypted only for permissions (an owner password) opens with it, and
// its streams are decrypted here, in memory and only to measure them, so that the cap on what they inflate to holds for them too.
// A file whose empty password does not open (the /U entry says so), or whose handler is not the standard one, is Protected.
internal static class FormPdfSecurity
{
    private const int MaximumDepth = 16;
    private const int MaximumItems = 4096;

    private static readonly byte[] Padding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    public enum Outcome { NotEncrypted, Protected, Opens }

    public enum Method { None, Rc4, Aes128, Aes256 }

    public readonly record struct Resolution(Outcome Outcome, StreamDecryptor? Decryptor = null);

    // Whether some dictionary names /Encrypt (also with #xx escapes): the file asks for a password, empty or not.
    public static bool IsEncrypted(byte[] pdf) => LastEncrypt(pdf) >= 0;

    public static Resolution Resolve(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var at = LastEncrypt(pdf);
        if (at < 0) return new(Outcome.NotEncrypted);
        try
        {
            return Open(pdf, at) is { } decryptor ? new(Outcome.Opens, decryptor) : new(Outcome.Protected);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException or IndexOutOfRangeException
            or OverflowException or InvalidCastException)
        {
            return new(Outcome.Protected);
        }
    }

    // The position after the last /Encrypt name.
    private static int LastEncrypt(byte[] pdf)
    {
        var span = pdf.AsSpan();
        var found = -1;
        for (var index = 0; index < span.Length; index++)
        {
            if (span[index] != (byte)'/') continue;
            var end = index + 1;
            while (end < span.Length && !IsSeparator(span[end])) end++;
            if (end - index - 1 is >= 7 and <= 21 && DecodeName(span[(index + 1)..end]) == "Encrypt") found = end;
            index = end - 1;
        }
        return found;
    }

    private static StreamDecryptor? Open(byte[] pdf, int afterName)
    {
        var parser = new Parser(pdf, afterName);
        var value = parser.ReadValue(0);
        if (value is Reference reference)
        {
            var objectStart = FindObject(pdf, reference.Number, reference.Generation);
            if (objectStart < 0) return null;
            value = new Parser(pdf, objectStart).ReadValue(0);
        }
        if (value is not Dictionary<string, object?> encrypt) return null;
        if (!encrypt.TryGetValue("Filter", out var filter) || filter is not Name { Value: "Standard" }) return null;
        var version = Integer(encrypt, "V", 0);
        var revision = Integer(encrypt, "R", 0);
        if (revision is < 2 or > 6 || version is not (1 or 2 or 4 or 5)) return null;
        if (!encrypt.TryGetValue("O", out var owner) || owner is not byte[] ownerEntry
            || !encrypt.TryGetValue("U", out var user) || user is not byte[] userEntry) return null;
        var permissions = unchecked((int)Integer(encrypt, "P", 0));
        var encryptMetadata = !encrypt.TryGetValue("EncryptMetadata", out var metadata) || metadata is not false;

        if (revision >= 5)
        {
            if (version != 5 || userEntry.Length < 48 || !encrypt.TryGetValue("UE", out var keyEntry) || keyEntry is not byte[] { Length: >= 32 } wrapped)
                return null;
            var validation = userEntry[32..40];
            var keySalt = userEntry[40..48];
            var expected = revision == 5 ? SHA256.HashData(validation) : Hash2B([], validation);
            if (!CryptographicOperations.FixedTimeEquals(expected, userEntry.AsSpan(0, 32))) return null;
            using var aes = Aes.Create();
            aes.Key = revision == 5 ? SHA256.HashData(keySalt) : Hash2B([], keySalt);
            var fileKey = aes.DecryptCbc(wrapped.AsSpan(0, 32), new byte[16], PaddingMode.None);
            return new(fileKey, StreamMethod(encrypt, version));
        }

        var length = version == 1 ? 40 : (int)Integer(encrypt, "Length", version == 4 ? 128 : 40);
        if (revision == 2) length = 40;
        if (length is < 40 or > 128 || length % 8 != 0) return null;
        var size = length / 8;
        var identifier = FirstIdentifier(pdf, afterName);
        if (ownerEntry.Length < 32) return null;
        var hash = MD5Hash(Padding, ownerEntry[..32], BitConverter.GetBytes(permissions), identifier,
            revision >= 4 && !encryptMetadata ? [0xFF, 0xFF, 0xFF, 0xFF] : []);
        if (revision >= 3)
            for (var round = 0; round < 50; round++) hash = MD5.HashData(hash.AsSpan(0, size));
        var key = hash[..size];

        // Algorithms 4 and 5: the entry the empty password gives has to be the one the file carries.
        if (revision == 2)
        {
            if (userEntry.Length < 32 || !CryptographicOperations.FixedTimeEquals(Rc4Transform(key, Padding), userEntry.AsSpan(0, 32))) return null;
        }
        else
        {
            if (userEntry.Length < 16) return null;
            var proof = Rc4Transform(key, MD5Hash(Padding, identifier));
            for (var round = 1; round <= 19; round++) proof = Rc4Transform(key.Select(item => (byte)(item ^ round)).ToArray(), proof);
            if (!CryptographicOperations.FixedTimeEquals(proof, userEntry.AsSpan(0, 16))) return null;
        }
        return new(key, StreamMethod(encrypt, version));
    }

    // RC4 for the first versions; for V4 what the crypt filter named by /StmF says (none by default).
    private static Method StreamMethod(Dictionary<string, object?> encrypt, long version)
    {
        if (version < 4) return Method.Rc4;
        if (!encrypt.TryGetValue("StmF", out var name) || name is not Name { Value: var filterName } || filterName == "Identity") return Method.None;
        if (!encrypt.TryGetValue("CF", out var filters) || filters is not Dictionary<string, object?> table
            || !table.TryGetValue(filterName, out var entry) || entry is not Dictionary<string, object?> filter
            || !filter.TryGetValue("CFM", out var cfm) || cfm is not Name { Value: var method })
            throw new FormatException("Unknown crypt filter.");
        return method switch
        {
            "V2" => Method.Rc4,
            "AESV2" => Method.Aes128,
            "AESV3" => Method.Aes256,
            "None" => Method.None,
            _ => throw new FormatException("Unknown crypt filter method."),
        };
    }

    private static long Integer(Dictionary<string, object?> dictionary, string key, long fallback) =>
        dictionary.TryGetValue(key, out var value) && value is long number ? number : fallback;

    // The first file identifier of the trailer nearest to the /Encrypt entry; empty when there is none.
    private static byte[] FirstIdentifier(byte[] pdf, int near)
    {
        var span = pdf.AsSpan();
        var best = -1;
        for (var index = 0; index < span.Length; index++)
        {
            if (span[index] != (byte)'/') continue;
            var end = index + 1;
            while (end < span.Length && !IsSeparator(span[end])) end++;
            if (end - index - 1 == 2 && span[index + 1] == (byte)'I' && span[index + 2] == (byte)'D'
                && (best < 0 || Math.Abs(end - near) < Math.Abs(best - near)))
            {
                var probe = end;
                while (probe < span.Length && span[probe] is 0 or 9 or 10 or 12 or 13 or 32) probe++;
                if (probe < span.Length && span[probe] == (byte)'[') best = end;
            }
            index = end - 1;
        }
        if (best < 0) return [];
        return new Parser(pdf, best).ReadValue(0) is List<object?> { Count: > 0 } list && list[0] is byte[] first ? first : [];
    }

    // The position of «number generation obj»: the last one in the file wins, as the newest revision does.
    private static int FindObject(byte[] pdf, long number, long generation)
    {
        var span = pdf.AsSpan();
        var found = -1;
        var from = 0;
        while (from < span.Length)
        {
            var at = span[from..].IndexOf("obj"u8);
            if (at < 0) break;
            var position = from + at;
            from = position + 3;
            if (position > 0 && IsLetter(pdf[position - 1]) || position + 3 < pdf.Length && IsLetter(pdf[position + 3])) continue;
            if (TryReadHeader(span, position, out var foundNumber, out var foundGeneration) && foundNumber == number && foundGeneration == generation)
                found = position + 3;
        }
        return found;
    }

    // The number and generation written before an «obj» keyword at «position».
    public static bool TryReadHeader(ReadOnlySpan<byte> bytes, int position, out long number, out long generation)
    {
        number = generation = 0;
        var cursor = position;
        if (!ReadIntegerBackwards(bytes, ref cursor, out generation) || !ReadIntegerBackwards(bytes, ref cursor, out number)) return false;
        return true;

        static bool ReadIntegerBackwards(ReadOnlySpan<byte> bytes, ref int cursor, out long value)
        {
            value = 0;
            var end = cursor;
            while (end > 0 && bytes[end - 1] is 0 or 9 or 10 or 12 or 13 or 32) end--;
            var start = end;
            while (start > 0 && bytes[start - 1] is >= (byte)'0' and <= (byte)'9') start--;
            if (start == end || end - start > 10 || end == cursor) return false;
            value = long.Parse(System.Text.Encoding.ASCII.GetString(bytes[start..end]), System.Globalization.CultureInfo.InvariantCulture);
            cursor = start;
            return true;
        }
    }

    private static byte[] MD5Hash(params byte[][] parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        foreach (var part in parts) hash.AppendData(part);
        return hash.GetHashAndReset();
    }

    // ISO 32000-2 algorithm 2.B, with no user data.
    private static byte[] Hash2B(byte[] password, byte[] salt)
    {
        var key = SHA256.HashData([.. password, .. salt]);
        byte[] last = [0];
        for (var round = 0; round < 64 || last[^1] > round - 32; round++)
        {
            var unit = new byte[(password.Length + key.Length) * 64];
            for (var part = 0; part < 64; part++)
            {
                password.CopyTo(unit, part * (password.Length + key.Length));
                key.CopyTo(unit, part * (password.Length + key.Length) + password.Length);
            }
            using var aes = Aes.Create();
            aes.Key = key[..16];
            last = aes.EncryptCbc(unit, key[16..32], PaddingMode.None);
            var sum = 0;
            for (var index = 0; index < 16; index++) sum += last[index];
            key = (sum % 3) switch { 0 => SHA256.HashData(last), 1 => SHA384.HashData(last), _ => SHA512.HashData(last) };
        }
        return key[..32];
    }

    private static byte[] Rc4Transform(byte[] key, byte[] data)
    {
        var result = new byte[data.Length];
        new Rc4(key).Transform(data, result, data.Length);
        return result;
    }

    // The key of an encrypted stream and how to decrypt it, as the file key says for the object it belongs to.
    internal sealed class StreamDecryptor(byte[] fileKey, Method method)
    {
        public bool Decrypts => method != Method.None;

        // The plaintext of the ciphertext that starts where «source» does, read in pieces, for the stream of object «number».
        public Stream Open(Stream source, long number, long generation)
        {
            switch (method)
            {
                case Method.Aes256:
                    return Aes(source, fileKey);
                case Method.Aes128:
                    return Aes(source, ObjectKey(number, generation, aes: true));
                default:
                    return new Rc4Stream(source, new Rc4(ObjectKey(number, generation, aes: false)));
            }
        }

        private byte[] ObjectKey(long number, long generation, bool aes)
        {
            var input = new List<byte>(fileKey) { (byte)number, (byte)(number >> 8), (byte)(number >> 16), (byte)generation, (byte)(generation >> 8) };
            if (aes) input.AddRange("sAlT"u8.ToArray());
            return MD5.HashData(input.ToArray())[..Math.Min(fileKey.Length + 5, 16)];
        }

        // The first block is the initialization vector; no padding is removed: only what is measured matters, not where it ends.
        private static CryptoStream Aes(Stream source, byte[] key)
        {
            var vector = new byte[16];
            var read = 0;
            while (read < 16)
            {
                var count = source.Read(vector, read, 16 - read);
                if (count == 0) break;
                read += count;
            }
            var aes = System.Security.Cryptography.Aes.Create();
            aes.Padding = PaddingMode.None;
            return new CryptoStream(source, aes.CreateDecryptor(key, vector), CryptoStreamMode.Read);
        }
    }

    private sealed class Rc4
    {
        private readonly byte[] state = new byte[256];
        private int left, right;

        public Rc4(byte[] key)
        {
            for (var index = 0; index < 256; index++) state[index] = (byte)index;
            for (int index = 0, other = 0; index < 256; index++)
            {
                other = (other + state[index] + key[index % key.Length]) & 255;
                (state[index], state[other]) = (state[other], state[index]);
            }
        }

        public void Transform(byte[] buffer, byte[] result, int count)
        {
            for (var position = 0; position < count; position++)
            {
                left = (left + 1) & 255;
                right = (right + state[left]) & 255;
                (state[left], state[right]) = (state[right], state[left]);
                result[position] = (byte)(buffer[position] ^ state[(state[left] + state[right]) & 255]);
            }
        }
    }

    private sealed class Rc4Stream(Stream source, Rc4 cipher) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, count);
            if (read <= 0) return read;
            var piece = new byte[read];
            Array.Copy(buffer, offset, piece, 0, read);
            var plain = new byte[read];
            cipher.Transform(piece, plain, read);
            Array.Copy(plain, 0, buffer, offset, read);
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // A reader of just what the encryption dictionary and the trailer use: numbers, names, strings, booleans, arrays, dictionaries and
    // references; anything else is a FormatException.
    private sealed record Name(string Value);

    private sealed record Reference(long Number, long Generation);

    private sealed class Parser(byte[] data, int start)
    {
        private int position = start;

        public object? ReadValue(int depth)
        {
            if (depth > MaximumDepth) throw new FormatException("Too deep.");
            SkipSpace();
            if (position >= data.Length) throw new FormatException("Unexpected end.");
            var current = data[position];
            if (current == (byte)'<' && position + 1 < data.Length && data[position + 1] == (byte)'<') return ReadDictionary(depth);
            if (current == (byte)'<') return ReadHexadecimal();
            if (current == (byte)'(') return ReadLiteral();
            if (current == (byte)'[') return ReadArray(depth);
            if (current == (byte)'/') return new Name(ReadName());
            var token = ReadToken();
            if (token is "true") return true;
            if (token is "false") return false;
            if (token is "null") return null;
            if (!long.TryParse(token, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                if (double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var real)) return (long)real;
                throw new FormatException("Unknown token.");
            }
            var saved = position;
            SkipSpace();
            var generationToken = ReadToken();
            if (long.TryParse(generationToken, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var generation))
            {
                SkipSpace();
                if (position < data.Length && data[position] == (byte)'R' && (position + 1 == data.Length || IsSeparator(data[position + 1])))
                {
                    position++;
                    return new Reference(number, generation);
                }
            }
            position = saved;
            return number;
        }

        private Dictionary<string, object?> ReadDictionary(int depth)
        {
            position += 2;
            var result = new Dictionary<string, object?>();
            while (true)
            {
                SkipSpace();
                if (position + 1 < data.Length && data[position] == (byte)'>' && data[position + 1] == (byte)'>')
                {
                    position += 2;
                    return result;
                }
                if (position >= data.Length || data[position] != (byte)'/' || result.Count > MaximumItems) throw new FormatException("Bad dictionary.");
                var key = ReadName();
                result[key] = ReadValue(depth + 1);
            }
        }

        private List<object?> ReadArray(int depth)
        {
            position++;
            var result = new List<object?>();
            while (true)
            {
                SkipSpace();
                if (position < data.Length && data[position] == (byte)']')
                {
                    position++;
                    return result;
                }
                if (position >= data.Length || result.Count > MaximumItems) throw new FormatException("Bad array.");
                result.Add(ReadValue(depth + 1));
            }
        }

        private string ReadName()
        {
            var begin = ++position;
            while (position < data.Length && !IsSeparator(data[position])) position++;
            return DecodeName(data.AsSpan(begin, position - begin));
        }

        private string ReadToken()
        {
            var begin = position;
            while (position < data.Length && !IsSeparator(data[position])) position++;
            return System.Text.Encoding.Latin1.GetString(data, begin, position - begin);
        }

        private byte[] ReadHexadecimal()
        {
            position++;
            var bytes = new List<byte>();
            var high = -1;
            while (true)
            {
                if (position >= data.Length) throw new FormatException("Bad string.");
                var current = data[position++];
                if (current == (byte)'>')
                {
                    if (high >= 0) bytes.Add((byte)(high << 4));
                    return bytes.ToArray();
                }
                if (current is 0 or 9 or 10 or 12 or 13 or 32) continue;
                var digit = HexValue(current);
                if (digit < 0 || bytes.Count > MaximumItems) throw new FormatException("Bad string.");
                if (high < 0) high = digit;
                else
                {
                    bytes.Add((byte)(high << 4 | digit));
                    high = -1;
                }
            }
        }

        private byte[] ReadLiteral()
        {
            position++;
            var bytes = new List<byte>();
            var depth = 1;
            while (true)
            {
                if (position >= data.Length || bytes.Count > MaximumItems) throw new FormatException("Bad string.");
                var current = data[position++];
                if (current == (byte)'(') depth++;
                else if (current == (byte)')' && --depth == 0) return bytes.ToArray();
                else if (current == (byte)'\\')
                {
                    if (position >= data.Length) throw new FormatException("Bad string.");
                    current = data[position++];
                    switch (current)
                    {
                        case (byte)'n': current = 10; break;
                        case (byte)'r': current = 13; break;
                        case (byte)'t': current = 9; break;
                        case (byte)'b': current = 8; break;
                        case (byte)'f': current = 12; break;
                        case 13:
                            if (position < data.Length && data[position] == 10) position++;
                            continue;
                        case 10:
                            continue;
                        case >= (byte)'0' and <= (byte)'7':
                            var value = current - '0';
                            for (var digits = 0; digits < 2 && position < data.Length && data[position] is >= (byte)'0' and <= (byte)'7'; digits++)
                                value = value * 8 + data[position++] - '0';
                            current = (byte)value;
                            break;
                    }
                }
                bytes.Add(current);
            }
        }

        private void SkipSpace()
        {
            while (position < data.Length)
            {
                if (data[position] is 0 or 9 or 10 or 12 or 13 or 32) position++;
                else if (data[position] == (byte)'%')
                    while (position < data.Length && data[position] is not (10 or 13)) position++;
                else break;
            }
        }
    }

    private static string DecodeName(ReadOnlySpan<byte> name)
    {
        var chars = new char[name.Length];
        var length = 0;
        for (var index = 0; index < name.Length; index++)
        {
            if (name[index] == (byte)'#' && index + 2 < name.Length && HexValue(name[index + 1]) is var high and >= 0 && HexValue(name[index + 2]) is var low and >= 0)
            {
                chars[length++] = (char)(high * 16 + low);
                index += 2;
            }
            else chars[length++] = (char)name[index];
        }
        return new string(chars, 0, length);
    }

    private static int HexValue(byte value) => value switch
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
