using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Interfaces.Integrations.Files;

/// <summary>
/// ME2: reads the format and the size of an image from its bytes, without decoding its pixels. Null when the bytes are not a
/// PNG or a JPEG the codec can open: the declared name or type of the upload never counts.
/// </summary>
public interface IImageInspector
{
    InspectedImage? Inspect(byte[] content);
}

public sealed record InspectedImage(BrandImageFormat Format, int Width, int Height);
