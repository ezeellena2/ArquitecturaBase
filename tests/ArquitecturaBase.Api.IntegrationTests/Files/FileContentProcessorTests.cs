using System.Security.Cryptography;
using SkiaSharp;
using ArquitecturaBase.Infrastructure.Files;
using ArquitecturaBase.Domain.Files;

namespace ArquitecturaBase.Api.IntegrationTests.Files;

public sealed class FileContentProcessorTests
{
    private static readonly byte[] Png = CreatePng();
    private static byte[] CreatePng()
    {
        using var surface = SKSurface.Create(new SKImageInfo(3, 2));
        surface.Canvas.Clear(SKColors.Red);
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
    [Fact]
    public void A_real_png_is_verified_and_gets_a_small_thumbnail_without_changing_the_original()
    {
        var hash = Convert.ToBase64String(SHA256.HashData(Png));
        var result = new FileContentProcessor().Process(Png, "image/png", hash, 25L * 1024 * 1024, true);
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.Thumbnail!);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Png)), result.Value.Sha256);
    }
    [Fact]
    public void Hash_size_and_actual_type_fail_before_any_file_can_be_stored()
    {
        var processor = new FileContentProcessor();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Png));
        Assert.Equal(FileErrors.HashMismatch, processor.Process(Png, "image/png", new string('0', 64), 1024, true).Error);
        Assert.Equal(FileErrors.TooLarge, processor.Process(Png, "image/png", hash, Png.Length - 1, true).Error);
        Assert.Equal(FileErrors.InvalidType, processor.Process(Png, "application/pdf", hash, 1024, false).Error);
        var csv = "name,value\nAlice,1"u8.ToArray();
        Assert.Equal(FileErrors.InvalidType, processor.Process(csv, "text/csv", Convert.ToHexStringLower(SHA256.HashData(csv)), 1024, false).Error);
    }
    [Fact]
    public void Documents_never_get_an_inline_preview_and_images_only_reject_documents()
    {
        var pdf = "%PDF-1.7\n1 0 obj<<>>endobj\n%%EOF"u8.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(pdf));
        var processor = new FileContentProcessor();
        var result = processor.Process(pdf, "application/pdf", hash, 1024, false);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Thumbnail);
        Assert.Equal(FileErrors.InvalidType, processor.Process(pdf, "application/pdf", hash, 1024, true).Error);
    }
}
