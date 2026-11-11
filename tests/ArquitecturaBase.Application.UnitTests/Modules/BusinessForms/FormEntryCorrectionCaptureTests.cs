using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

/// <summary>A correction is another capture (CW10-A): the same validations and copies, the replaced response's number.</summary>
public sealed class FormEntryCorrectionCaptureTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task A_correction_keeps_the_number_and_author_closes_the_open_approval_and_replaces_the_response()
    {
        var fixture = new BusinessFormCaptureFixture("shortText", true);
        var replaced = await OriginalAsync(fixture, "12");
        replaced.MarkPendingApproval();
        var task = BusinessFormTask.Open(replaced.DefinitionId, replaced.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), replaced.ActorUserId,
            new DateTime(2026, 11, 5, 9, 41, 0, DateTimeKind.Utc));
        fixture.Repository.Tasks.Add(task);

        var result = await fixture.Sink.CaptureCorrectionAsync(Correction(fixture, "15"), new(replaced.Id, "La dosis era 15", []), Ct);

        Assert.Equal("Completed", result.State);
        var corrected = Assert.Single(fixture.Repository.Records, record => record.Id == result.RecordId);
        Assert.Equal((replaced.Number, replaced.ActorUserId, replaced.SubmittedAtUtc, true),
            (corrected.Number, corrected.ActorUserId, corrected.SubmittedAtUtc, corrected.EnteredInApp));
        Assert.Equal(replaced.Number, result.Number);
        Assert.Equal(1, fixture.Definition.LastRecordNumber);
        Assert.Equal((replaced.Id, fixture.Actor, "La dosis era 15"), (corrected.ReplacesRecordId, corrected.CorrectedByUserId, corrected.CorrectionReason));
        Assert.Equal(corrected.Id, replaced.ReplacedByRecordId);
        Assert.Equal(FormTaskState.Closed, task.State);
        Assert.Equal(FormRecordState.PendingApproval, replaced.State);
        Assert.Contains("15", corrected.ValuesJson, StringComparison.Ordinal);
        Assert.Contains("12", replaced.ValuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeating_a_correction_returns_its_first_result_and_another_reason_is_a_conflict()
    {
        var fixture = new BusinessFormCaptureFixture("shortText", true);
        var replaced = await OriginalAsync(fixture, "12");
        var request = Correction(fixture, "15");

        var first = await fixture.Sink.CaptureCorrectionAsync(request, new(replaced.Id, "Motivo", []), Ct);
        var again = await fixture.Sink.CaptureCorrectionAsync(request, new(replaced.Id, "Motivo", []), Ct);
        var found = await fixture.Sink.FindExistingCorrectionAsync(request, new(replaced.Id, "Motivo", []), Ct);
        var changed = await fixture.Sink.CaptureCorrectionAsync(request, new(replaced.Id, "Otro motivo", []), Ct);

        Assert.Equal(first, again);
        Assert.Equal(first, found);
        Assert.Equal(FormRecordErrors.ReplayConflict.Code, changed.ErrorCode);
        Assert.Equal(2, fixture.Repository.Records.Count);
    }

    [Fact]
    public async Task A_replaced_response_is_not_corrected_again()
    {
        var fixture = new BusinessFormCaptureFixture("shortText", true);
        var replaced = await OriginalAsync(fixture, "12");
        await fixture.Sink.CaptureCorrectionAsync(Correction(fixture, "15"), new(replaced.Id, "Primera", []), Ct);

        var second = await fixture.Sink.CaptureCorrectionAsync(Correction(fixture, "16"), new(replaced.Id, "Segunda", []), Ct);

        Assert.Equal(FormRecordErrors.AmendUnavailable.Code, second.ErrorCode);
        Assert.Equal(2, fixture.Repository.Records.Count);
    }

    [Fact]
    public async Task Whoever_only_loads_does_not_correct_a_decided_response()
    {
        var fixture = new BusinessFormCaptureFixture("shortText", true);
        var replaced = await OriginalAsync(fixture, "12");
        replaced.MarkPendingApproval();
        replaced.CompleteApproval(true);
        fixture.Permission.OnlyCreate = true;

        var result = await fixture.Sink.CaptureCorrectionAsync(Correction(fixture, "15"), new(replaced.Id, "Motivo", []), Ct);

        Assert.Equal(FormRecordErrors.AmendUnavailable.Code, result.ErrorCode);
        Assert.Null(replaced.ReplacedByRecordId);
    }

    // An issuance born after the correction was opened (an approval that issues, under the same lock) is seen where it is saved.
    [Fact]
    public async Task A_response_with_a_live_issuance_is_not_corrected_and_it_is_checked_under_the_lock()
    {
        var fixture = new BusinessFormCaptureFixture("shortText", true);
        var replaced = await OriginalAsync(fixture, "12");
        fixture.Issuances.Live.Add(replaced.Id);
        var request = Correction(fixture, "15");

        var result = await fixture.Sink.CaptureCorrectionAsync(request, new(replaced.Id, "Motivo", []), Ct);
        var again = await fixture.Sink.FindExistingCorrectionAsync(request, new(replaced.Id, "Motivo", []), Ct);

        Assert.Equal(("Rejected", FormRecordErrors.IssuedNotAmendable.Code), (result.State, result.ErrorCode));
        Assert.Equal(result, again);
        Assert.Equal([(replaced.Id, true)], fixture.Issuances.Reads);
        Assert.Null(replaced.ReplacedByRecordId);
        Assert.Single(fixture.Repository.Records);
    }

    [Fact]
    public async Task The_photos_nobody_touched_stay_and_count_for_the_question_limits()
    {
        var fixture = new BusinessFormCaptureFixture("shortText", true, withPhoto: true);
        var original = fixture.Request(new Dictionary<string, string> { [fixture.Key] = "12" }) with { Files = [Photo(fixture, 0), Photo(fixture, 1)] };
        Assert.Equal("Completed", (await fixture.Sink.CaptureEntryAsync(original, Ct)).State);
        var replaced = Assert.Single(fixture.Repository.Records);
        var photos = fixture.Repository.FileReferences.Where(file => file.RecordId == replaced.Id).OrderBy(file => file.Position).ToArray();

        var result = await fixture.Sink.CaptureCorrectionAsync(Correction(fixture, "15") with { Files = [Photo(fixture, 0)] },
            new(replaced.Id, "Otra foto", [photos[1].FileId]), Ct);

        Assert.Equal("Completed", result.State);
        var kept = fixture.Repository.FileReferences.Where(file => file.RecordId == result.RecordId).OrderBy(file => file.Position).ToArray();
        Assert.Equal(2, kept.Length);
        Assert.Equal((photos[1].FileId, 0), (kept[0].FileId, kept[0].Position));
        Assert.NotEqual(photos[0].FileId, kept[1].FileId);
        Assert.Equal(1, kept[1].Position);
    }

    [Fact]
    public async Task A_kept_file_of_another_response_rejects_the_correction()
    {
        var fixture = new BusinessFormCaptureFixture("shortText", true, withPhoto: true);
        var original = fixture.Request(new Dictionary<string, string> { [fixture.Key] = "12" }) with { Files = [Photo(fixture, 0)] };
        await fixture.Sink.CaptureEntryAsync(original, Ct);
        var replaced = Assert.Single(fixture.Repository.Records);

        var result = await fixture.Sink.CaptureCorrectionAsync(Correction(fixture, "15"), new(replaced.Id, "Motivo", [Guid.NewGuid()]), Ct);

        Assert.Equal(FormRecordErrors.InvalidResponse.Code, result.ErrorCode);
        Assert.Null(replaced.ReplacedByRecordId);
    }

    private static async Task<BusinessFormRecord> OriginalAsync(BusinessFormCaptureFixture fixture, string value)
    {
        var captured = await fixture.Sink.CaptureEntryAsync(fixture.Request(new Dictionary<string, string> { [fixture.Key] = value }), Ct);
        Assert.Equal("Completed", captured.State);
        return Assert.Single(fixture.Repository.Records);
    }

    private static StructuredRecordCaptureRequest Correction(BusinessFormCaptureFixture fixture, string value) =>
        fixture.Request(new Dictionary<string, string> { [fixture.Key] = value }) with { SourceReceiptId = Guid.CreateVersion7() };

    private static StructuredRecordFileInput Photo(BusinessFormCaptureFixture fixture, int position) => new(fixture.PhotoFieldId!.Value, position,
        "app", "photo", "photo.png", "image/png", new string((char)('a' + position), 64), [1, 2, (byte)position]);
}
