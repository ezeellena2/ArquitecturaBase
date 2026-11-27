using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Domain.Settings;
using SkiaSharp;

namespace ArquitecturaBase.Infrastructure.Files;

/// <summary>ME2: the codec reads the format and the size from the header; the pixels are never decoded.</summary>
internal sealed class SkiaImageInspector : IImageInspector
{
    public InspectedImage? Inspect(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0) return null;
        try
        {
            using var stream = new SKMemoryStream(content);
            using var codec = SKCodec.Create(stream);
            if (codec is null || codec.Info.Width < 1 || codec.Info.Height < 1) return null;
            BrandImageFormat? format = codec.EncodedFormat switch
            {
                SKEncodedImageFormat.Png => BrandImageFormat.Png,
                SKEncodedImageFormat.Jpeg => BrandImageFormat.Jpeg,
                _ => null,
            };
            return format is { } known ? new InspectedImage(known, codec.Info.Width, codec.Info.Height) : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
