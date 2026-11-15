namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Documents;

// Decodes the text of an ASCII85 or ASCIIHex PDF stream as it is read, in pieces, so that what it holds is never kept whole: it ends
// at «~>» or «>», at the end of the data or at the first byte that does not belong, as the decoders of a PDF reader do.
internal sealed class AsciiDecodingStream(Stream source, bool hexadecimal) : Stream
{
    private readonly byte[] pending = new byte[4];
    private int pendingStart;
    private int pendingCount;
    private bool ended;
    private byte[] returned = [];
    private int returnedStart;

    // Puts back bytes just read, to be read again first.
    public void Unread(byte[] bytes)
    {
        returned = [.. bytes, .. returned.AsSpan(returnedStart)];
        returnedStart = 0;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var written = 0;
        while (written < count)
        {
            if (returnedStart < returned.Length) { buffer[offset + written++] = returned[returnedStart++]; continue; }
            if (pendingCount > 0) { buffer[offset + written++] = pending[pendingStart++]; pendingCount--; continue; }
            if (ended || !Fill()) break;
        }
        return written;
    }

    private bool Fill() => hexadecimal ? FillHexadecimal() : FillAscii85();

    private bool FillAscii85()
    {
        long group = 0;
        var digits = 0;
        while (digits < 5)
        {
            var value = source.ReadByte();
            if (value < 0 || value == '~') { ended = true; break; }
            if (IsSpace(value)) continue;
            if (value == 'z' && digits == 0)
            {
                Array.Clear(pending);
                pendingStart = 0;
                pendingCount = 4;
                return true;
            }
            if (value is < '!' or > 'u') { ended = true; break; }
            group = group * 85 + (value - '!');
            digits++;
        }
        if (digits < 2) return false;
        // A last group of n digits gives n - 1 bytes, as if it were padded with the highest digit.
        for (var padding = digits; padding < 5; padding++) group = group * 85 + 84;
        for (var index = 0; index < 4; index++) pending[index] = (byte)(group >> (24 - 8 * index));
        pendingStart = 0;
        pendingCount = digits < 5 ? digits - 1 : 4;
        return true;
    }

    private bool FillHexadecimal()
    {
        var high = -1;
        while (true)
        {
            var value = source.ReadByte();
            if (value < 0 || value == '>')
            {
                ended = true;
                if (high < 0) return false;
                pending[0] = (byte)(high << 4);
                pendingStart = 0;
                pendingCount = 1;
                return true;
            }
            if (IsSpace(value)) continue;
            var digit = value switch
            {
                >= '0' and <= '9' => value - '0',
                >= 'a' and <= 'f' => value - 'a' + 10,
                >= 'A' and <= 'F' => value - 'A' + 10,
                _ => -1,
            };
            if (digit < 0) { ended = true; return false; }
            if (high < 0) { high = digit; continue; }
            pending[0] = (byte)(high << 4 | digit);
            pendingStart = 0;
            pendingCount = 1;
            return true;
        }
    }

    private static bool IsSpace(int value) => value is 0 or 9 or 10 or 12 or 13 or 32;

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
