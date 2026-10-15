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
    [Theory]
    [InlineData("application/msword")]
    [InlineData("application/vnd.ms-excel")]
    public void Malformed_compound_documents_are_rejected_instead_of_throwing(string type)
    {
        byte[] bytes = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0];
        var result = new FileContentProcessor().Process(bytes, type, Convert.ToHexStringLower(SHA256.HashData(bytes)), 1024, false);
        Assert.Equal(FileErrors.InvalidType, result.Error);
    }
    [Theory]
    [InlineData("application/msword", true)]
    [InlineData("application/vnd.ms-excel", true)]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", false)]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", false)]
    public void Damaged_office_files_of_realistic_size_are_terminal_type_rejections(string type, bool compound)
    {
        var bytes = new byte[4096];
        (compound ? new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 } : "%PDF-1.7"u8.ToArray()).CopyTo(bytes, 0);
        var result = new FileContentProcessor().Process(bytes, type, Convert.ToHexStringLower(SHA256.HashData(bytes)), 8192, false);
        Assert.Equal(FileErrors.InvalidType, result.Error);
    }
    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg", true, false)]
    [InlineData(SKEncodedImageFormat.Png, "image/png", false, true)]
    public void Original_dimensions_limit_photo_decoding_but_do_not_reject_large_image_documents(SKEncodedImageFormat format, string type, bool imageOnly, bool accepted)
    {
        using var surface = SKSurface.Create(new SKImageInfo(8192, 4097));
        surface.Canvas.Clear(SKColors.White);
        using var image = surface.Snapshot();
        using var encoded = image.Encode(format, 80);
        var bytes = encoded.ToArray();
        var result = new FileContentProcessor().Process(bytes, type, Convert.ToHexStringLower(SHA256.HashData(bytes)), 25L * 1024 * 1024, imageOnly);
        Assert.Equal(accepted, result.IsSuccess);
        if (accepted) Assert.Null(result.Value.Thumbnail);
        else Assert.Equal(FileErrors.InvalidType, result.Error);
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
