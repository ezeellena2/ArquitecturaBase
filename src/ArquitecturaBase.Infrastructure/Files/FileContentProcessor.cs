using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Files;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;
using OpenMcdf;
using SkiaSharp;

namespace ArquitecturaBase.Infrastructure.Files;

internal sealed class FileContentProcessor : IFileContentProcessor
{
    public Result<ProcessedPrivateFile> Process(byte[] content, string contentType, string expectedSha256, long maximumBytes, bool imageOnly)
    {
        if (content.Length == 0 || content.LongLength > maximumBytes) return FileErrors.TooLarge;
        byte[] expected;
        try { expected = expectedSha256.Length == 64 ? Convert.FromHexString(expectedSha256) : Convert.FromBase64String(expectedSha256); }
        catch (FormatException) { return FileErrors.HashMismatch; }
        var hash = SHA256.HashData(content);
        if (expected.Length != 32 || !CryptographicOperations.FixedTimeEquals(hash, expected)) return FileErrors.HashMismatch;
        try
        {
            byte[]? thumbnail = null;
            if (contentType is "image/jpeg" or "image/png")
            {
                using var stream = new SKMemoryStream(content);
                using var codec = SKCodec.Create(stream);
                if (codec is null || (contentType == "image/jpeg" ? codec.EncodedFormat != SKEncodedImageFormat.Jpeg : codec.EncodedFormat != SKEncodedImageFormat.Png))
                    return FileErrors.InvalidType;
                var size = codec.GetScaledDimensions(Math.Min(1f, 320f / Math.Max(codec.Info.Width, codec.Info.Height)));
                // Bound the decoder allocation independently of the compressed file size.
                if (size.Width < 1 || size.Height < 1 || (long)size.Width * size.Height > 32L * 1024 * 1024) return FileErrors.InvalidType;
                using var bitmap = SKBitmap.Decode(codec, new SKImageInfo(size.Width, size.Height));
                if (bitmap is null) return FileErrors.InvalidType;
                if (imageOnly) thumbnail = Thumbnail(bitmap, codec.EncodedOrigin);
            }
            else if (imageOnly || !IsDocument(content, contentType)) return FileErrors.InvalidType;
            return new ProcessedPrivateFile(Convert.ToHexStringLower(hash), thumbnail);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException or ArgumentException or XmlException or InvalidOperationException)
        {
            return FileErrors.InvalidType;
        }
    }

    private static bool IsDocument(byte[] content, string type) => type switch
    {
        "application/pdf" => content.AsSpan().StartsWith("%PDF-"u8) && content.AsSpan(Math.Max(0, content.Length - 1024)).IndexOf("%%EOF"u8) >= 0,
        "application/msword" => IsCompound(content, "WordDocument"),
        "application/vnd.ms-excel" => IsCompound(content, "Workbook") || IsCompound(content, "Book"),
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => IsOpenXml(content, "word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => IsOpenXml(content, "xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
        _ => false,
    };

    private static bool IsCompound(byte[] content, string name)
    {
        ReadOnlySpan<byte> signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        if (!content.AsSpan().StartsWith(signature)) return false;
        using var stream = new MemoryStream(content, false);
        using var root = RootStorage.Open(stream);
        if (!root.TryOpenStream(name, out var document)) return false;
        using (document) return document.Length > 0;
    }

    private static bool IsOpenXml(byte[] content, string mainPart, string contentType)
    {
        using var stream = new MemoryStream(content, false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var manifest = archive.GetEntry("[Content_Types].xml");
        if (manifest is null || manifest.Length > 64 * 1024 || archive.GetEntry(mainPart) is null) return false;
        using var xml = manifest.Open();
        using var reader = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64 * 1024 });
        while (reader.Read())
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Override"
                && reader.GetAttribute("PartName") == "/" + mainPart && reader.GetAttribute("ContentType") == contentType) return true;
        return false;
    }

    private static byte[] Thumbnail(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        var scale = Math.Min(1d, 320d / Math.Max(bitmap.Width, bitmap.Height));
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        using var surface = SKSurface.Create(new SKImageInfo(swap ? height : width, swap ? width : height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Translate(width, 0); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight: canvas.Translate(width, height); canvas.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft: canvas.Translate(0, height); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(height, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(height, width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, width); canvas.RotateDegrees(270); break;
        }
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawBitmap(bitmap, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear), paint);
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 75);
        return encoded.ToArray();
    }
}
