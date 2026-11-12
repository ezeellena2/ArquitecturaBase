using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

// Task 7: the inbox «Documentos recibidos»: how a received document is classified, reviewed, changes form, becomes a response and expires.
public sealed class FormReceivedDocumentTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Sha = new('a', 64);

    [Fact]
    public void A_received_document_needs_its_channel_source_sender_and_a_copy_described_exactly()
    {
        Assert.True(Received().IsSuccess);
        Assert.True(FormReceivedDocument.Receive(Guid.Empty, "whatsapp", "m1", Guid.NewGuid(), Now, "forms/documents/x.pdf", "a.pdf", "application/pdf", 10, Sha, 1, null).IsFailure);
        Assert.True(FormReceivedDocument.Receive(Guid.NewGuid(), "", "m1", Guid.NewGuid(), Now, "k", "a.pdf", "application/pdf", 10, Sha, 1, null).IsFailure);
        Assert.True(FormReceivedDocument.Receive(Guid.NewGuid(), "whatsapp", "m1", Guid.Empty, Now, "k", "a.pdf", "application/pdf", 10, Sha, 1, null).IsFailure);
        Assert.True(FormReceivedDocument.Receive(Guid.NewGuid(), "whatsapp", "m1", Guid.NewGuid(), DateTime.SpecifyKind(Now, DateTimeKind.Local), "k", "a.pdf",
            "application/pdf", 10, Sha, 1, null).IsFailure);
        Assert.True(FormReceivedDocument.Receive(Guid.NewGuid(), "whatsapp", "m1", Guid.NewGuid(), Now, "k", "a.pdf", "application/pdf", 10, Sha.ToUpperInvariant(), 1, null).IsFailure);
        Assert.True(FormReceivedDocument.Receive(Guid.NewGuid(), "whatsapp", "m1", Guid.NewGuid(), Now, "k", "a.pdf", "application/pdf", 10, Sha, 11, null).IsFailure);
    }

    [Fact]
    public void A_proposed_document_has_no_deletion_date_and_one_left_unclassified_or_rejected_is_deleted_ninety_days_later()
    {
        var proposed = Proposed();
        Assert.Equal(FormReceivedDocumentState.Proposed, proposed.State);
        Assert.Null(proposed.DeleteAfterUtc);
        var unclassified = Received().Value;
        Assert.True(unclassified.LeaveUnclassified("quota", null, Now).IsSuccess);
        Assert.Equal((FormReceivedDocumentState.Unclassified, "quota", Now.AddDays(90)), (unclassified.State, unclassified.ReasonCode, unclassified.DeleteAfterUtc));
        var rejected = Received().Value;
        Assert.True(rejected.Reject("protected", null, Now).IsSuccess);
        Assert.Equal((FormReceivedDocumentState.Discarded, Now.AddDays(90)), (rejected.State, rejected.DeleteAfterUtc));
        Assert.True(Received().Value.Reject("bad code!", null, Now).IsFailure);
    }

    // Task 8 (decision 10): a file that cannot be opened enters discarded even when the inbox must not keep it: what its person sent, without
    // a copy, a key or a SHA-256, and the size only when it was known.
    [Fact]
    public void A_file_the_inbox_does_not_keep_is_received_discarded_without_a_copy()
    {
        var received = FormReceivedDocument.ReceiveRejected(Guid.NewGuid(), "whatsapp", "m1", Guid.NewGuid(), Now, "informe.doc", "application/msword", 2048,
            "legacy", Now);
        var withoutSize = FormReceivedDocument.ReceiveRejected(Guid.NewGuid(), "whatsapp", "m2", Guid.NewGuid(), Now, "video.pdf", "application/pdf", null,
            "tooLarge", Now);

        Assert.True(received.IsSuccess);
        var document = received.Value;
        Assert.Equal((FormReceivedDocumentState.Discarded, "legacy", Now.AddDays(90)), (document.State, document.ReasonCode, document.DeleteAfterUtc));
        Assert.Equal(((string?)null, (string?)null, (long?)2048, (int?)null, false),
            (document.StorageKey, document.Sha256, document.Size, document.Pages, document.IsStored));
        Assert.Equal(("informe.doc", "application/msword"), (document.FileName, document.MediaType));
        Assert.True(withoutSize.IsSuccess);
        Assert.Null(withoutSize.Value.Size);
        Assert.True(Received().Value.IsStored);
    }

    [Fact]
    public void A_file_without_a_copy_needs_its_name_type_reason_and_a_positive_size_when_it_has_one()
    {
        Assert.True(FormReceivedDocument.ReceiveRejected(Guid.NewGuid(), "whatsapp", "m1", Guid.NewGuid(), Now, " ", "application/pdf", 1, "legacy", Now).IsFailure);
        Assert.True(FormReceivedDocument.ReceiveRejected(Guid.NewGuid(), "whatsapp", "m1", Guid.NewGuid(), Now, "a.doc", "", 1, "legacy", Now).IsFailure);
        Assert.True(FormReceivedDocument.ReceiveRejected(Guid.NewGuid(), "whatsapp", "m1", Guid.NewGuid(), Now, "a.doc", "application/msword", 0, "legacy", Now).IsFailure);
        Assert.True(FormReceivedDocument.ReceiveRejected(Guid.NewGuid(), "whatsapp", "m1", Guid.NewGuid(), Now, "a.doc", "application/msword", 1, "bad code!", Now).IsFailure);
        Assert.True(FormReceivedDocument.ReceiveRejected(Guid.NewGuid(), "", "m1", Guid.NewGuid(), Now, "a.doc", "application/msword", 1, "legacy", Now).IsFailure);
        Assert.True(FormReceivedDocument.ReceiveRejected(Guid.NewGuid(), "whatsapp", "m1", Guid.NewGuid(), Now, "a.doc", "application/msword", 1, "legacy",
            DateTime.SpecifyKind(Now, DateTimeKind.Local)).IsFailure);
    }

    [Fact]
    public void A_proposed_document_is_for_review_when_it_cannot_be_sent_nobody_waits_or_its_reply_expired()
    {
        var waiting = Proposed(Now.AddHours(1));
        Assert.False(waiting.IsForReview(Now));
        Assert.True(waiting.IsForReview(Now.AddHours(1)));
        Assert.True(Proposed().IsForReview(Now));
        var notSendable = Received(Now.AddHours(1)).Value;
        Assert.True(notSendable.Propose(Guid.NewGuid(), Guid.NewGuid(), [1], true).IsSuccess);
        Assert.True(notSendable.IsForReview(Now));
        var unclassified = Received().Value;
        Assert.True(unclassified.LeaveUnclassified("unknown", null, Now).IsSuccess);
        Assert.False(unclassified.IsForReview(Now));
    }

    [Fact]
    public void Its_person_is_waited_for_until_the_reply_expires_and_nobody_is_waited_for_otherwise()
    {
        var until = Now.AddHours(1);
        Assert.Equal(until, FormReceivedDocument.AwaitedUntil(FormReceivedDocumentState.Proposed, false, until, Now));
        Assert.Null(FormReceivedDocument.AwaitedUntil(FormReceivedDocumentState.Proposed, false, until, until));
        Assert.Null(FormReceivedDocument.AwaitedUntil(FormReceivedDocumentState.Proposed, true, until, Now));
        Assert.Null(FormReceivedDocument.AwaitedUntil(FormReceivedDocumentState.Proposed, false, null, Now));
        Assert.Null(FormReceivedDocument.AwaitedUntil(FormReceivedDocumentState.Unclassified, false, until, Now));
        Assert.Null(FormReceivedDocument.AwaitedUntil(FormReceivedDocumentState.Confirmed, false, until, Now));
    }

    [Fact]
    public void Discarding_needs_the_revision_seen_and_a_proposed_or_unclassified_document_and_advances_the_revision()
    {
        var document = Proposed();
        Assert.Equal(FormReceivedDocumentErrors.RevisionConflict, document.Discard(2, Now).Error);
        Assert.True(document.Discard(1, Now).IsSuccess);
        Assert.Equal((FormReceivedDocumentState.Discarded, FormReceivedDocument.DiscardedByReviewer, 2L, Now.AddDays(90)),
            (document.State, document.ReasonCode, document.Revision, document.DeleteAfterUtc));
        Assert.Equal(FormReceivedDocumentErrors.StateConflict, document.Discard(2, Now).Error);
        var confirmed = Proposed();
        Assert.True(confirmed.Confirm(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now).IsSuccess);
        Assert.Equal(FormReceivedDocumentErrors.StateConflict, confirmed.Discard(confirmed.Revision, Now).Error);
    }

    [Fact]
    public void While_its_person_can_still_complete_it_a_proposed_document_is_neither_discarded_nor_read_again_for_another_form()
    {
        var document = Proposed(Now.AddHours(1));
        Assert.True(document.Describe([9], null, "").IsSuccess);
        Assert.Equal(FormReceivedDocumentErrors.AwaitingSender, document.Discard(1, Now).Error);
        Assert.Equal(FormReceivedDocumentErrors.AwaitingSender, document.RequestExtraction(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "es", "UTC", Now).Error);
        Assert.Equal(1L, document.Revision);
        Assert.True(document.RequestExtraction(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "es", "UTC", Now.AddHours(1)).IsSuccess);
        Assert.True(document.Discard(2, Now.AddHours(1)).IsSuccess);
    }

    [Fact]
    public void Reopening_a_discarded_document_proposes_it_again_with_its_form_for_review_here_or_leaves_it_unclassified_without_one()
    {
        var proposed = Proposed(Now.AddHours(5));
        Assert.True(proposed.Discard(1, Now.AddHours(5)).IsSuccess);
        Assert.True(proposed.Reopen(2, Now).IsSuccess);
        Assert.Equal((FormReceivedDocumentState.Proposed, null, (DateTime?)null, 3L), (proposed.State, proposed.ReasonCode, proposed.DeleteAfterUtc, proposed.Revision));
        Assert.True(proposed.IsForReview(Now));
        var rejected = Received().Value;
        Assert.True(rejected.Reject("protected", null, Now).IsSuccess);
        Assert.True(rejected.Reopen(1, Now.AddDays(1)).IsSuccess);
        Assert.Equal((FormReceivedDocumentState.Unclassified, Now.AddDays(91)), (rejected.State, rejected.DeleteAfterUtc));
        Assert.Equal(FormReceivedDocumentErrors.StateConflict, rejected.Reopen(2, Now).Error);
    }

    [Fact]
    public void Changing_the_form_needs_a_reading_waits_for_its_request_and_applies_only_that_request()
    {
        var withoutReading = Received().Value;
        Assert.True(withoutReading.LeaveUnclassified("unknown", null, Now).IsSuccess);
        Assert.Equal(FormReceivedDocumentErrors.ReadingUnavailable, withoutReading.RequestExtraction(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "es", "UTC", Now).Error);
        var document = Received().Value;
        Assert.True(document.Describe([9], null, "").IsSuccess);
        Assert.True(document.LeaveUnclassified("unknown", null, Now).IsSuccess);
        var requestId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        Assert.Equal(FormReceivedDocumentErrors.RevisionConflict, document.RequestExtraction(5, requestId, definitionId, reviewer, "es", "UTC", Now).Error);
        Assert.True(document.RequestExtraction(1, requestId, definitionId, reviewer, "xx", "UTC", Now).IsFailure);
        Assert.True(document.RequestExtraction(1, requestId, definitionId, reviewer, "en", "", Now).IsFailure);
        Assert.True(document.RequestExtraction(1, requestId, definitionId, reviewer, "en", "America/Argentina/Buenos_Aires", Now).IsSuccess);
        Assert.Equal((requestId, definitionId, Now), (document.ExtractionRequestId, document.ExtractionDefinitionId, document.ExtractionRequestedAtUtc));
        // Who asked, in which language and zone: the engine's operation is always read with that sealed context.
        Assert.Equal((reviewer, "en", "America/Argentina/Buenos_Aires"), (document.ExtractionActorUserId, document.ExtractionCulture, document.ExtractionTimeZoneId));
        Assert.Equal(FormReceivedDocumentErrors.ExtractionNotFound, document.ApplyExtraction(Guid.NewGuid(), Guid.NewGuid(), [1], null, "").Error);
        var versionId = Guid.NewGuid();
        Assert.True(document.ApplyExtraction(requestId, versionId, [1], "comprobante", "150000").IsSuccess);
        Assert.Equal((FormReceivedDocumentState.Proposed, definitionId, versionId, (Guid?)null, (DateTime?)null, "150000", 3L),
            (document.State, document.DefinitionId, document.VersionId, document.ExtractionRequestId, document.DeleteAfterUtc, document.SearchText, document.Revision));
        Assert.Equal(((Guid?)null, (string?)null, (string?)null), (document.ExtractionActorUserId, document.ExtractionCulture, document.ExtractionTimeZoneId));
        Assert.True(document.IsForReview(Now));
    }

    [Fact]
    public void A_failed_extraction_drops_its_request_and_keeps_the_document_as_it_was()
    {
        var document = Received().Value;
        Assert.True(document.Describe([9], null, "").IsSuccess);
        Assert.True(document.LeaveUnclassified("unknown", null, Now).IsSuccess);
        var requestId = Guid.NewGuid();
        Assert.True(document.RequestExtraction(1, requestId, Guid.NewGuid(), Guid.NewGuid(), "es", "UTC", Now).IsSuccess);
        Assert.True(document.FailExtraction(requestId).IsSuccess);
        Assert.Equal((FormReceivedDocumentState.Unclassified, (Guid?)null, 3L), (document.State, document.ExtractionRequestId, document.Revision));
        Assert.Equal(FormReceivedDocumentErrors.ExtractionNotFound, document.FailExtraction(requestId).Error);
    }

    [Fact]
    public void Only_a_proposed_document_is_confirmed_with_the_form_and_version_of_its_response_and_a_reviewer_is_recorded_with_the_moment()
    {
        var document = Proposed();
        var reviewer = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        Assert.True(document.Confirm(recordId, Guid.Empty, versionId, reviewer, Now).IsFailure);
        Assert.True(document.Confirm(recordId, definitionId, versionId, reviewer, Now).IsSuccess);
        Assert.Equal((FormReceivedDocumentState.Confirmed, recordId, reviewer, Now, (DateTime?)null),
            (document.State, document.RecordId, document.ReviewedByUserId, document.ReviewedAtUtc, document.DeleteAfterUtc));
        Assert.Equal((definitionId, versionId), (document.DefinitionId!.Value, document.VersionId!.Value));
        Assert.Equal(FormReceivedDocumentErrors.StateConflict, document.Confirm(Guid.NewGuid(), definitionId, versionId, null, Now).Error);
        var byPerson = Proposed();
        Assert.True(byPerson.Confirm(recordId, definitionId, versionId, null, Now).IsSuccess);
        Assert.Null(byPerson.ReviewedByUserId);
    }

    [Fact]
    public void A_person_chooses_another_candidate_only_while_it_is_proposed_and_the_choice_advances_the_revision()
    {
        var document = Proposed(Now.AddHours(1));
        var definitionId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        Assert.True(document.Choose(Guid.Empty, versionId).IsFailure);
        Assert.True(document.Choose(definitionId, versionId).IsSuccess);
        Assert.Equal((definitionId, versionId, 2L, FormReceivedDocumentState.Proposed),
            (document.DefinitionId!.Value, document.VersionId!.Value, document.Revision, document.State));
        var unclassified = Received().Value;
        Assert.True(unclassified.LeaveUnclassified("unknown", null, Now).IsSuccess);
        Assert.Equal(FormReceivedDocumentErrors.StateConflict, unclassified.Choose(definitionId, versionId).Error);
    }

    [Fact]
    public void Choosing_other_leaves_it_unclassified_with_its_reason_and_never_from_a_final_state()
    {
        var document = Proposed();
        Assert.True(document.ChooseOther(Now).IsSuccess);
        Assert.Equal((FormReceivedDocumentState.Unclassified, FormReceivedDocument.ChoseOther, Now.AddDays(90)), (document.State, document.ReasonCode, document.DeleteAfterUtc));
        var discarded = Proposed();
        Assert.True(discarded.Discard(1, Now).IsSuccess);
        Assert.Equal(FormReceivedDocumentErrors.StateConflict, discarded.ChooseOther(Now).Error);
    }

    [Fact]
    public void A_step_of_the_history_needs_its_document_a_known_kind_a_real_actor_and_a_UTC_moment()
    {
        Assert.True(FormReceivedDocumentEvent.Record(Guid.NewGuid(), FormReceivedDocumentEventKind.Discarded, Guid.NewGuid(), Now).IsSuccess);
        Assert.True(FormReceivedDocumentEvent.Record(Guid.NewGuid(), FormReceivedDocumentEventKind.Received, null, Now, [1]).IsSuccess);
        Assert.True(FormReceivedDocumentEvent.Record(Guid.Empty, FormReceivedDocumentEventKind.Received, null, Now).IsFailure);
        Assert.True(FormReceivedDocumentEvent.Record(Guid.NewGuid(), (FormReceivedDocumentEventKind)99, null, Now).IsFailure);
        Assert.True(FormReceivedDocumentEvent.Record(Guid.NewGuid(), FormReceivedDocumentEventKind.Received, Guid.Empty, Now).IsFailure);
        Assert.True(FormReceivedDocumentEvent.Record(Guid.NewGuid(), FormReceivedDocumentEventKind.Received, null, Now, []).IsFailure);
    }

    [Fact]
    public void Seeing_and_reviewing_received_documents_are_two_permissions_at_the_end_of_the_module_catalog()
    {
        Assert.Equal(("formdocuments.read", "formdocuments.review"), (BusinessFormsPermissions.DocumentsRead, BusinessFormsPermissions.DocumentsReview));
        Assert.Equal([BusinessFormsPermissions.DocumentsRead, BusinessFormsPermissions.DocumentsReview], BusinessFormsPermissions.All.TakeLast(2));
        Assert.All([BusinessFormsPermissions.DocumentsRead, BusinessFormsPermissions.DocumentsReview], code => Assert.Matches("^[a-z]+\\.[a-z]+$", code));
    }

    private static Result<FormReceivedDocument> Received(DateTime? replyExpiresAtUtc = null) =>
        FormReceivedDocument.Receive(Guid.NewGuid(), "whatsapp", "wamid." + Guid.NewGuid().ToString("N"), Guid.NewGuid(), Now,
            "forms/documents/x.pdf", "comprobante.pdf", "application/pdf", 1234, Sha, 1, replyExpiresAtUtc);

    private static FormReceivedDocument Proposed(DateTime? replyExpiresAtUtc = null)
    {
        var document = Received(replyExpiresAtUtc).Value;
        Assert.True(document.Propose(Guid.NewGuid(), Guid.NewGuid(), [1, 2], false).IsSuccess);
        return document;
    }
}
