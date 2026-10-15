using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Configuration;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormFileRecorderTests
{
    [Fact]
    public async Task Deterministic_keys_keep_response_field_position_and_hash_and_never_use_the_filename()
    {
        var store = new MemoryPrivateFileStore();
        var preparer = new BusinessFormFileRecorder(store, new FakeFileContentProcessor(), Options.Create(new BusinessFormsOptions()));
        var request = Request();
        var first = await preparer.PrepareAsync(request, CancellationToken.None);
        var replay = await preparer.PrepareAsync(request, CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value[0].File.StorageKey, replay.Value[0].File.StorageKey);
        Assert.DoesNotContain("private-photo", first.Value[0].File.StorageKey);
        Assert.Contains(request.SourceReceiptId.ToString("N"), first.Value[0].File.StorageKey);
        Assert.Equal(request.ActorUserId, first.Value[0].File.OwnerUserId);
    }
    [Fact]
    public async Task A_bad_file_rejects_the_whole_preparation_before_any_upload()
    {
        var store = new MemoryPrivateFileStore();
        var preparer = new BusinessFormFileRecorder(store, new FakeFileContentProcessor(), Options.Create(new BusinessFormsOptions()));
        var request = Request();
        request = request with { Files = [request.Files![0], request.Files[0] with { Position = 1, Sha256 = "bad" }] };
        var rejected = await preparer.PrepareAsync(request, CancellationToken.None);
        Assert.True(rejected.IsFailure);
        Assert.Equal(request.Files[1].FieldId, rejected.Error.Metadata?["fieldId"]);
        Assert.Equal(0, store.WriteCount);
    }
    [Theory]
    [InlineData("acta.pdf.html", "application/pdf", "acta.pdf.pdf")]
    [InlineData("acta \"parcial\".exe", "application/pdf", "acta parcial.pdf")]
    [InlineData("informe\u202Efdp.exe", "application/pdf", "informefdp.pdf")]
    [InlineData("José – revisión.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "José – revisión.xlsx")]
    public async Task The_stored_and_returned_name_has_no_format_characters_and_uses_the_verified_extension(string name, string contentType, string expected)
    {
        var store = new MemoryPrivateFileStore();
        var recorder = new BusinessFormFileRecorder(store, new FakeFileContentProcessor(), Options.Create(new BusinessFormsOptions()));
        var request = Request();
        request = request with { Files = [request.Files![0] with { Type = "document", Name = name, ContentType = contentType }] };
        var result = await recorder.PrepareAsync(request, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value[0].File.Name);
    }
    [Fact]
    public async Task A_name_rejected_after_assigning_the_verified_extension_keeps_its_question_identity()
    {
        var store = new MemoryPrivateFileStore();
        var recorder = new BusinessFormFileRecorder(store, new FakeFileContentProcessor(), Options.Create(new BusinessFormsOptions()));
        var request = Request();
        request = request with { Files = [request.Files![0] with { Name = new string('x', 253) + ".x" }] };
        var result = await recorder.PrepareAsync(request, CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(request.Files[0].FieldId, result.Error.Metadata?["fieldId"]);
        Assert.Equal(0, store.WriteCount);
    }
    private static StructuredRecordCaptureRequest Request() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc), new Dictionary<string, string>(), Files: [new(Guid.CreateVersion7(), 0, "123", "photo", "private-photo.png", "image/png", new string('a', 64), [1, 2, 3])]);
}
