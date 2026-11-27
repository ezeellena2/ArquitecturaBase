using System.Diagnostics.CodeAnalysis;

namespace ArquitecturaBase.Domain.Settings;

public enum BrandImageKind
{
    Logo,
    Icon,
}

public enum BrandImageFormat
{
    Png,
    Jpeg,
}

/// <summary>
/// ME2: the logo and the icon of the installation, stored as private files with a new key per upload
/// (<c>branding/logo/{guid}.png</c>). The key is the version the public address carries, so a new upload never reuses a cached
/// image. The logo is PNG or JPEG and wider than tall (emails need one of those two: Gmail does not show SVG); the icon is a
/// square PNG, for the browser tab and the collapsed menu.
/// </summary>
public static class BrandImage
{
    public const string Prefix = "branding/";
    public const int MaxKeyLength = 64;
    public const long MaxBytes = 1024 * 1024;
    public const int MaxLogoSide = 4000;
    public const int MinIconSide = 64;
    public const int MaxIconSide = 1024;

    public static string NewKey(BrandImageKind kind, Guid id, BrandImageFormat format) =>
        $"{Prefix}{Folder(kind)}/{id:N}{Extension(format)}";

    /// <summary>The shape and the size each image needs; the format of the icon is only PNG.</summary>
    public static bool Allows(BrandImageKind kind, BrandImageFormat format, int width, int height) => kind switch
    {
        BrandImageKind.Logo => width >= height && height >= 1 && width <= MaxLogoSide,
        _ => format == BrandImageFormat.Png && width == height && width is >= MinIconSide and <= MaxIconSide,
    };

    /// <summary>A key this type made for <paramref name="kind"/>, with a format that kind accepts.</summary>
    public static bool IsKey(BrandImageKind kind, [NotNullWhen(true)] string? key)
    {
        var folder = $"{Prefix}{Folder(kind)}/";
        if (key is null || key.Length > MaxKeyLength || !key.StartsWith(folder, StringComparison.Ordinal)) return false;
        var name = key[folder.Length..];
        var format = FormatOf(name);
        return format is not null && (kind == BrandImageKind.Logo || format == BrandImageFormat.Png)
            && Guid.TryParseExact(name[..^Extension(format.Value).Length], "N", out _);
    }

    /// <summary>Any key of either kind: what the file cleanup must keep while the settings point to it.</summary>
    public static bool IsAnyKey([NotNullWhen(true)] string? key) => IsKey(BrandImageKind.Logo, key) || IsKey(BrandImageKind.Icon, key);

    /// <summary>The version of a valid key: its file name without the extension.</summary>
    public static string VersionOf(string key)
    {
        var name = key[(key.LastIndexOf('/') + 1)..];
        return name[..name.IndexOf('.', StringComparison.Ordinal)];
    }

    public static string ContentTypeOf(string key) =>
        FormatOf(key) == BrandImageFormat.Jpeg ? "image/jpeg" : "image/png";

    private static string Folder(BrandImageKind kind) => kind == BrandImageKind.Logo ? "logo" : "icon";

    private static string Extension(BrandImageFormat format) => format == BrandImageFormat.Png ? ".png" : ".jpg";

    private static BrandImageFormat? FormatOf(string name) =>
        name.EndsWith(".png", StringComparison.Ordinal) ? BrandImageFormat.Png
        : name.EndsWith(".jpg", StringComparison.Ordinal) ? BrandImageFormat.Jpeg : null;
}
