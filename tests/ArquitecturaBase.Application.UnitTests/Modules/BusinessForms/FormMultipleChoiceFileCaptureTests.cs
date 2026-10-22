using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormMultipleChoiceFileCaptureTests
{
    private static readonly Guid First = Guid.Parse("00000000-0000-7000-8000-000000000011");
    private static readonly Guid Second = Guid.Parse("00000000-0000-7000-8000-000000000012");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static BusinessFormCaptureFixture Fixture() => new("multiChoice", true, withPhoto: true, selection: new(1, 2),
        choices: [new(First, new Dictionary<string,string> { ["es"] = "Primera" }), new(Second, new Dictionary<string,string> { ["es"] = "Segunda" })]);
    private static StructuredRecordFileInput Photo(BusinessFormCaptureFixture f) => new(f.PhotoFieldId!.Value, 0,
        "fictitious-media", "photo", "photo.png", "image/png", new string('a', 64), [1, 2, 3]);
    private static StructuredRecordCaptureRequest Request(BusinessFormCaptureFixture f) => f.Request(
        new Dictionary<string,string> { [f.Key] = JsonSerializer.Serialize(new[] { Second, First }) }) with { Files = [Photo(f)] };

    [Fact]
    public async Task Checkboxes_and_photo_are_captured_in_the_same_record_with_independent_positions()
    {
        var f = Fixture();
        var request = Request(f);
        var result = await f.Sink.CaptureAsync(request, Ct);
        Assert.Equal("Completed", result.State);
        var record = Assert.Single(f.Repository.Records);
        Assert.Equal(new[] { Second, First }, f.Repository.Values.OrderBy(value => value.Position).Select(value => value.ChoiceId!.Value));
        Assert.Equal(new[] { 0, 1 }, f.Repository.Values.OrderBy(value => value.Position).Select(value => value.Position));
        var file = Assert.Single(f.Repository.FileReferences);
        Assert.Equal(record.Id, file.RecordId);
        Assert.Equal(f.PhotoFieldId, file.FieldId);
        Assert.Equal(0, file.Position);
        Assert.Single(f.StoredFiles.Files);
        Assert.Equal(1, f.Definition.LastRecordNumber);
        var writes = f.FileStore.WriteCount;
        Assert.True(writes > 0);
        Assert.Equal(result, await f.Sink.CaptureAsync(request, Ct));
        Assert.Equal(writes, f.FileStore.WriteCount);
        Assert.Single(f.StoredFiles.Files);
        Assert.Single(f.Repository.FileReferences);
        Assert.Equal(2, f.Repository.Values.Count);
        Assert.Equal(1, f.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task A_bad_photo_does_not_commit_a_valid_multiple_choice_or_allocate_a_number()
    {
        var f = Fixture();
        var request = Request(f) with { Files = [Photo(f) with { Sha256 = "bad" }] };
        var result = await f.Sink.CaptureAsync(request, Ct);
        Assert.Equal("Rejected", result.State);
        Assert.Equal(f.PhotoFieldId, result.RejectedFieldId);
        Assert.Empty(f.Repository.Records);
        Assert.Empty(f.Repository.Values);
        Assert.Empty(f.Repository.FileReferences);
        Assert.Empty(f.StoredFiles.Files);
        Assert.Equal(0, f.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task An_invalid_checkbox_array_does_not_commit_photo_metadata_or_other_values()
    {
        var f = Fixture();
        var request = Request(f) with { Answers = new Dictionary<string,string> { [f.Key] = JsonSerializer.Serialize(new[] { First, First }) } };
        var result = await f.Sink.CaptureAsync(request, Ct);
        Assert.Equal("Rejected", result.State);
        Assert.Equal(FormRecordErrors.InvalidResponse.Code, result.ErrorCode);
        Assert.Empty(f.Repository.Records);
        Assert.Empty(f.Repository.Values);
        Assert.Empty(f.Repository.FileReferences);
        Assert.Empty(f.StoredFiles.Files);
        Assert.Empty(f.Actions.Runs.Runs);
        Assert.Equal(0, f.Definition.LastRecordNumber);
        // Files are prepared before the write boundary; this asserts atomic metadata, not a new upload protocol.
    }

    [Fact]
    public async Task A_required_photo_still_blocks_an_otherwise_valid_multiple_choice()
    {
        var f = Fixture();
        var request = Request(f) with { Files = [] };
        var result = await f.Sink.CaptureAsync(request, Ct);
        Assert.Equal("Rejected", result.State);
        Assert.Equal(FormRecordErrors.InvalidResponse.Code, result.ErrorCode);
        Assert.Empty(f.Repository.Records);
        Assert.Empty(f.Repository.Values);
        Assert.Empty(f.Repository.FileReferences);
        Assert.Equal(0, f.Definition.LastRecordNumber);
    }
}
