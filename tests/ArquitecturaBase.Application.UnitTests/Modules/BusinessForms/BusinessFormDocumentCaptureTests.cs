using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Task 7 (DOC5-A): the common capture of a received document. A reviewer passes it as the response of whoever sent it; under the document's
// lock the capture checks again the revision the reviewer saw, the form and that its person no longer waits, and inside its unit of work
// that the sender is still active and the reviewer still has «Revisar documentos»; whoever confirms it in a channel completes the form the
// document follows.
public sealed class BusinessFormDocumentCaptureTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_reviewer_passes_it_for_whoever_sent_it_and_it_is_confirmed_with_the_form_and_version_of_the_response()
    {
        var capture = new BusinessFormCaptureFixture("shortText", false);
        var reviewer = capture.Users.AddUser("reviewer@example.invalid").Id;
        var document = Document(capture, capture.Definition.Id, Guid.NewGuid());

        var result = await capture.Sink.CaptureDocumentAsync(Pass(capture, document, reviewer), Ct);

        Assert.Equal("Completed", result.State);
        var record = Assert.Single(capture.Repository.Records);
        Assert.Equal((capture.Actor, false), (record.ActorUserId, record.EnteredInApp));
        Assert.Equal((FormReceivedDocumentState.Confirmed, record.Id, capture.Definition.Id, capture.VersionId, reviewer),
            (document.State, document.RecordId!.Value, document.DefinitionId!.Value, document.VersionId!.Value, document.ReviewedByUserId!.Value));
        Assert.Contains(capture.Documents.Events, item => item.Kind == FormReceivedDocumentEventKind.Confirmed && item.ActorUserId == reviewer);
    }

    [Fact]
    public async Task A_reviewer_who_loses_review_after_the_check_made_before_the_unit_of_work_is_rejected_inside_it()
    {
        var capture = new BusinessFormCaptureFixture("shortText", false);
        var reviewer = capture.Users.AddUser("reviewer@example.invalid").Id;
        var document = Document(capture, capture.Definition.Id);
        capture.Permission.RevokeOnSecondCheck = true;

        var result = await capture.Sink.CaptureDocumentAsync(Pass(capture, document, reviewer), Ct);

        Assert.Equal(("Rejected", FormRecordErrors.AuthorizationChanged.Code), (result.State, result.ErrorCode));
        Assert.Empty(capture.Repository.Records);
        Assert.Equal(FormReceivedDocumentState.Proposed, document.State);
    }

    [Fact]
    public async Task Whoever_sent_it_deactivated_after_the_check_made_before_the_unit_of_work_is_rejected_inside_it()
    {
        var capture = new BusinessFormCaptureFixture("shortText", false);
        var reviewer = capture.Users.AddUser("reviewer@example.invalid").Id;
        var document = Document(capture, capture.Definition.Id);
        capture.Repository.OnLockSourceReceipt = () => capture.Users.SetActiveAsync(capture.Actor, false, Ct);

        var result = await capture.Sink.CaptureDocumentAsync(Pass(capture, document, reviewer), Ct);

        Assert.Equal(("Rejected", FormRecordErrors.AuthorizationChanged.Code), (result.State, result.ErrorCode));
        Assert.Empty(capture.Repository.Records);
        Assert.Equal(FormReceivedDocumentState.Proposed, document.State);
    }

    [Fact]
    public async Task Under_the_document_lock_a_changed_revision_another_form_or_a_person_still_waiting_reject_the_pass()
    {
        var stale = new BusinessFormCaptureFixture("shortText", false);
        var staleDocument = Document(stale, stale.Definition.Id);
        var staleResult = await stale.Sink.CaptureDocumentAsync(Pass(stale, staleDocument, stale.Users.AddUser("a@example.invalid").Id, staleDocument.Revision + 1), Ct);
        var moved = new BusinessFormCaptureFixture("shortText", false);
        var movedDocument = Document(moved, Guid.NewGuid());
        var movedResult = await moved.Sink.CaptureDocumentAsync(Pass(moved, movedDocument, moved.Users.AddUser("c@example.invalid").Id), Ct);
        var waiting = new BusinessFormCaptureFixture("shortText", false);
        var waitingDocument = Document(waiting, waiting.Definition.Id, replyExpiresAtUtc: waiting.Documents.Clock.GetUtcNow().UtcDateTime.AddHours(1));
        var waitingResult = await waiting.Sink.CaptureDocumentAsync(Pass(waiting, waitingDocument, waiting.Users.AddUser("d@example.invalid").Id), Ct);

        Assert.Equal(("Rejected", FormReceivedDocumentErrors.RevisionConflict.Code), (staleResult.State, staleResult.ErrorCode));
        Assert.Equal(("Rejected", FormReceivedDocumentErrors.StateConflict.Code), (movedResult.State, movedResult.ErrorCode));
        Assert.Equal(("Rejected", FormReceivedDocumentErrors.AwaitingSender.Code), (waitingResult.State, waitingResult.ErrorCode));
        Assert.All([staleDocument, movedDocument, waitingDocument], document => Assert.Equal(FormReceivedDocumentState.Proposed, document.State));
        Assert.Empty(stale.Repository.Records.Concat(moved.Repository.Records).Concat(waiting.Repository.Records));
    }

    [Fact]
    public async Task Whoever_sent_it_confirms_in_a_channel_only_the_form_the_document_follows_and_is_not_a_reviewer()
    {
        var capture = new BusinessFormCaptureFixture("shortText", false);
        var document = Document(capture, capture.Definition.Id, replyExpiresAtUtc: capture.Documents.Clock.GetUtcNow().UtcDateTime.AddHours(1));
        var other = new BusinessFormCaptureFixture("shortText", false);
        var otherDocument = Document(other, Guid.NewGuid(), replyExpiresAtUtc: other.Documents.Clock.GetUtcNow().UtcDateTime.AddHours(1));

        var confirmed = await capture.Sink.CaptureAsync(Channel(capture, document), Ct);
        var refused = await other.Sink.CaptureAsync(Channel(other, otherDocument), Ct);

        Assert.Equal("Completed", confirmed.State);
        Assert.Equal((FormReceivedDocumentState.Confirmed, (Guid?)null), (document.State, document.ReviewedByUserId));
        Assert.Equal(("Rejected", FormReceivedDocumentErrors.StateConflict.Code), (refused.State, refused.ErrorCode));
        Assert.Equal(FormReceivedDocumentState.Proposed, otherDocument.State);
    }

    // Task 9b: a confirmation in a channel is checked again with what the person corrected and says which response it repeats; task 9c:
    // its replay says it again from the receipt, and a reviewer who passes a document from the inbox decides it themselves.
    [Fact]
    public async Task A_channel_confirmation_that_repeats_a_saved_response_says_which_one_its_replay_too_and_a_reviewer_pass_does_not()
    {
        var capture = new BusinessFormCaptureFixture("shortText", false);
        var savedAtUtc = new DateTime(2026, 11, 10, 15, 30, 0, DateTimeKind.Utc);
        var fieldId = Follow(capture, savedAtUtc, "local");
        var document = Document(capture, capture.Definition.Id, replyExpiresAtUtc: capture.Documents.Clock.GetUtcNow().UtcDateTime.AddHours(1));
        var request = Channel(capture, document);

        var rejected = await capture.Sink.CaptureAsync(request, Ct);
        var replay = await capture.Sink.CaptureAsync(request, Ct);

        Assert.Equal(("Rejected", FormRecordErrors.Repeated.Code), (rejected.State, rejected.ErrorCode));
        Assert.Equal(new StructuredRecordRepetition("FORM-000007", savedAtUtc), rejected.Repetition);
        Assert.Equal(("Rejected", FormRecordErrors.Repeated.Code, rejected.Repetition, (bool?)null),
            (replay.State, replay.ErrorCode, replay.Repetition, replay.FixedValidation));
        var key = Assert.Single(Assert.Single(capture.Documents.RepetitionQueries).Keys);
        Assert.Equal((fieldId, "shortText", "local"), (key.FieldId, key.Type, key.Value));
        Assert.Equal(FormReceivedDocumentState.Proposed, document.State);
        Assert.Empty(capture.Repository.Records);

        var reviewed = Document(capture, capture.Definition.Id);
        var passed = await capture.Sink.CaptureDocumentAsync(Pass(capture, reviewed, capture.Users.AddUser("reviewer@example.invalid").Id)
            with { SourceReceiptId = Guid.CreateVersion7() }, Ct);

        Assert.Equal(("Completed", (StructuredRecordRepetition?)null), (passed.State, passed.Repetition));
        Assert.Single(capture.Documents.RepetitionQueries);
    }

    // The form copies a standard whose duplicate key is its question, and a response with this value was saved (number 7).
    private static Guid Follow(BusinessFormCaptureFixture capture, DateTime savedAtUtc, string value)
    {
        var questions = FormDraftPolicy.Read<FormVersionSchema>(capture.Version.SchemaJson).Questions;
        capture.Documents.Standards.Add(FormReceivedDocumentFixture.Standard("test.transfer", "operation"));
        var form = capture.Documents.AddForm(questions, standardKey: "test.transfer", bindings: new Dictionary<string, Guid> { ["operation"] = questions[0].FieldId });
        capture.Documents.Forms[^1] = form with { DefinitionId = capture.Definition.Id, VersionId = capture.VersionId, Prefix = "FORM" };
        capture.Documents.SavedKeys.Add((new(savedAtUtc, Guid.CreateVersion7(), 7, null), [new(questions[0].FieldId, "shortText", value)]));
        return questions[0].FieldId;
    }

    // A document of the capture's person proposed for a form (the capture's own, or another the person did not choose).
    private static FormReceivedDocument Document(BusinessFormCaptureFixture capture, Guid definitionId, Guid? versionId = null, DateTime? replyExpiresAtUtc = null)
    {
        var document = FormReceivedDocument.Receive(Guid.NewGuid(), "whatsapp", "wamid." + Guid.NewGuid().ToString("N"), capture.Actor,
            capture.Documents.Clock.GetUtcNow().UtcDateTime, "forms/documents/x.pdf", "comprobante.pdf", "application/pdf", 10, new string('a', 64), 1,
            replyExpiresAtUtc).Value;
        Assert.True(document.Propose(definitionId, definitionId == capture.Definition.Id && versionId is null ? capture.VersionId : versionId ?? Guid.NewGuid(), [1], false).IsSuccess);
        capture.Documents.Documents.Add(document);
        return document;
    }

    // The reviewer's pass with the revision they saw (the document's own unless a test says otherwise).
    private static StructuredRecordCaptureRequest Pass(BusinessFormCaptureFixture capture, FormReceivedDocument document, Guid reviewer, long? revision = null) =>
        capture.Request(new Dictionary<string, string> { [capture.Key] = "local" }) with
        {
            Origin = new StructuredRecordCaptureOrigin(StructuredRecordSenderKind.Manual, reviewer, null, null, [], [])
                { Document = document.Id, DocumentRevision = revision ?? document.Revision },
        };

    private static StructuredRecordCaptureRequest Channel(BusinessFormCaptureFixture capture, FormReceivedDocument document) =>
        capture.Request(new Dictionary<string, string> { [capture.Key] = "local" }) with
        {
            Origin = new StructuredRecordCaptureOrigin(StructuredRecordSenderKind.Manual, capture.Actor, null, null, [], []) { Document = document.Id },
        };
}
