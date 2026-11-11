using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormRecordAmendmentTests
{
    private static readonly DateTime Received = new(2026, 10, 24, 9, 40, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 11, 5, 11, 20, 0, DateTimeKind.Utc);

    [Fact]
    public void A_correction_keeps_the_number_who_answered_the_reception_and_the_channel_and_replaces_the_response()
    {
        var original = Original(enteredInApp: true);
        var corrector = Guid.NewGuid();
        var version = Guid.NewGuid();
        var receipt = Guid.NewGuid();
        var rules = Guid.NewGuid();

        var correction = original.Correct(version, receipt, "[{\"value\":1.5}]", rules, "en", corrector, "  La dosis era 1,5.  ");

        Assert.True(correction.IsSuccess);
        var value = correction.Value;
        Assert.NotEqual(original.Id, value.Id);
        Assert.Equal((original.DefinitionId, original.Number, original.ActorUserId, Received, true),
            (value.DefinitionId, value.Number, value.ActorUserId, value.SubmittedAtUtc, value.EnteredInApp));
        Assert.Equal((version, receipt, rules, "en", "[{\"value\":1.5}]"),
            (value.VersionId, value.SourceReceiptId, value.RuleSetId, value.PresentationCulture, value.ValuesJson));
        Assert.Equal((original.Id, corrector, "La dosis era 1,5."), (value.ReplacesRecordId, value.CorrectedByUserId, value.CorrectionReason));
        Assert.Equal(FormRecordState.Received, value.State);
        Assert.Equal(original.SourceReceiptId, value.OriginalReceiptId);
        Assert.Equal(original.SourceReceiptId, value.OriginReceiptId);
        Assert.Equal(value.Id, original.ReplacedByRecordId);
        Assert.False(original.CanBeAmended);
        Assert.True(value.CanBeAmended);
    }

    [Fact]
    public void A_second_correction_keeps_the_receipt_of_the_first_version()
    {
        var original = Original();
        var first = original.Correct(Guid.NewGuid(), Guid.NewGuid(), "[]", null, null, Guid.NewGuid(), "Primera").Value;

        var second = first.Correct(Guid.NewGuid(), Guid.NewGuid(), "[]", null, null, Guid.NewGuid(), "Segunda").Value;

        Assert.Equal(original.SourceReceiptId, second.OriginalReceiptId);
        Assert.Equal(first.Id, second.ReplacesRecordId);
        Assert.Equal(second.Id, first.ReplacedByRecordId);
        Assert.Equal(original.Number, second.Number);
    }

    [Fact]
    public void An_original_response_answers_its_own_receipt_and_is_not_a_correction()
    {
        var original = Original();
        Assert.Null(original.ReplacesRecordId);
        Assert.Null(original.OriginalReceiptId);
        Assert.Equal(original.SourceReceiptId, original.OriginReceiptId);
        Assert.True(original.CanBeAmended);
    }

    [Fact]
    public void A_replaced_response_is_not_corrected_or_voided_again()
    {
        var original = Original();
        var correction = original.Correct(Guid.NewGuid(), Guid.NewGuid(), "[]", null, null, Guid.NewGuid(), "Motivo").Value;

        Assert.Equal(FormRecordErrors.AmendUnavailable, original.Correct(Guid.NewGuid(), Guid.NewGuid(), "[]", null, null, Guid.NewGuid(), "Otra").Error);
        Assert.Equal(FormRecordErrors.AmendUnavailable, original.Void(Guid.NewGuid(), "Otra", Now).Error);
        Assert.Equal(correction.Id, original.ReplacedByRecordId);
        Assert.NotEqual(FormRecordState.Voided, original.State);
    }

    [Fact]
    public void Voiding_keeps_the_number_who_when_and_why_and_cannot_be_undone()
    {
        var record = Original();
        var actor = Guid.NewGuid();

        Assert.True(record.Void(actor, " Cargada dos veces. ", Now).IsSuccess);

        Assert.Equal(FormRecordState.Voided, record.State);
        Assert.Equal((actor, Now, "Cargada dos veces."), (record.VoidedByUserId, record.VoidedAtUtc, record.VoidReason));
        Assert.Equal(1, record.Number);
        Assert.False(record.CanBeAmended);
        Assert.Equal(FormRecordErrors.AmendUnavailable, record.Void(Guid.NewGuid(), "Otra vez", Now).Error);
        Assert.Equal(FormRecordErrors.AmendUnavailable, record.Correct(Guid.NewGuid(), Guid.NewGuid(), "[]", null, null, actor, "Motivo").Error);
        Assert.Equal(FormRecordErrors.StateConflict, record.MarkPendingApproval().Error);
        Assert.Equal(FormRecordErrors.StateConflict, record.CompleteApproval(true).Error);
        Assert.Equal(actor, record.VoidedByUserId);
    }

    [Theory]
    [InlineData(FormRecordState.PendingApproval)]
    [InlineData(FormRecordState.Approved)]
    [InlineData(FormRecordState.Rejected)]
    public void Any_current_response_can_be_voided_whatever_its_approval(FormRecordState state)
    {
        var record = Original();
        record.MarkPendingApproval();
        if (state != FormRecordState.PendingApproval) record.CompleteApproval(state == FormRecordState.Approved);

        Assert.True(record.Void(Guid.NewGuid(), "Motivo", Now).IsSuccess);
        Assert.Equal(FormRecordState.Voided, record.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Correcting_and_voiding_require_a_visible_reason(string? reason)
    {
        var record = Original();
        Assert.Equal(FormRecordErrors.ReasonRequired, record.Void(Guid.NewGuid(), reason, Now).Error);
        Assert.Equal(FormRecordErrors.ReasonRequired, record.Correct(Guid.NewGuid(), Guid.NewGuid(), "[]", null, null, Guid.NewGuid(), reason).Error);
        Assert.True(record.CanBeAmended);
        Assert.Equal(FormRecordState.Received, record.State);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void The_reason_has_at_most_five_hundred_characters(int length, bool valid)
    {
        Assert.Equal(valid, Original().Void(Guid.NewGuid(), new string('a', length), Now).IsSuccess);
        var correction = Original().Correct(Guid.NewGuid(), Guid.NewGuid(), "[]", null, null, Guid.NewGuid(), new string('a', length));
        Assert.Equal(valid, correction.IsSuccess);
        if (!valid) Assert.Equal(FormRecordErrors.ReasonInvalid, correction.Error);
    }

    [Fact]
    public void The_reason_must_be_storable()
    {
        Assert.Equal(FormRecordErrors.ReasonInvalid, Original().Void(Guid.NewGuid(), "a" + (char)0, Now).Error);
        Assert.Equal(FormRecordErrors.ReasonInvalid, Original().Correct(Guid.NewGuid(), Guid.NewGuid(), "[]", null, null, Guid.NewGuid(), "a" + (char)0).Error);
    }

    // A void the system decides («No se emitió») keeps the stable code of its reason, translated when read, and no text; it is
    // still a void, once, of a response that stands.
    [Fact]
    public void A_void_the_system_decides_keeps_the_code_of_its_reason_and_no_text()
    {
        var record = Original();
        var actor = Guid.NewGuid();

        Assert.True(record.VoidFor(actor, "Forms.Action.NotIssued", Now).IsSuccess);

        Assert.Equal((FormRecordState.Voided, actor, Now), (record.State, record.VoidedByUserId, record.VoidedAtUtc));
        Assert.Equal(("Forms.Action.NotIssued", (string?)null), (record.VoidReasonCode, record.VoidReason));
        Assert.Equal(FormRecordErrors.AmendUnavailable, record.VoidFor(actor, "Forms.Action.NotIssued", Now).Error);
        Assert.Null(Original().VoidReasonCode);
        Assert.Throws<ArgumentException>(() => Original().VoidFor(actor, " ", Now));
    }

    private static BusinessFormRecord Original(bool enteredInApp = false) =>
        BusinessFormRecord.Create(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Received, "[]", enteredInApp: enteredInApp);
}
