using OpenMcdf;

namespace ArquitecturaBase.Infrastructure.Files;

// An OLE2 (CFB) container: Word and Excel before 2007, and an Office file with a password. OpenMcdf opens it without running
// or rendering anything; a damaged container throws its FileFormatException (a FormatException) or an IOException.
internal static class CompoundDocument
{
    private static ReadOnlySpan<byte> Signature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public static bool IsCompound(ReadOnlySpan<byte> content) => content.StartsWith(Signature);

    // Whether the container has a non-empty stream with one of those names at its root.
    public static bool HasStream(byte[] content, params string[] names)
    {
        if (!IsCompound(content)) return false;
        using var stream = new MemoryStream(content, false);
        using var root = RootStorage.Open(stream);
        foreach (var name in names)
        {
            if (!root.TryOpenStream(name, out var document)) continue;
            using (document)
                if (document.Length > 0) return true;
        }
        return false;
    }
}
