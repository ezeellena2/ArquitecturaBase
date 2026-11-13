using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

// VC1-A, task 28: each row of a spreadsheet that becomes a load is an entry of the inbox, with its sheet (only when the book has more than
// one) and its number, the copy of its spreadsheet and, when it was not sent to its person, why: the form cannot be sent to them (CE4-A) or
// it went over the loads of its spreadsheet.
public sealed class FormReceivedDocumentRowTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Sha = new('a', 64);

    [Fact]
    public void A_row_keeps_its_sheet_and_number_with_the_copy_of_its_spreadsheet_and_no_pages()
    {
        var row = Row("Ventas", 5).Value;

        Assert.Equal(("Ventas", (int?)5, true, (int?)null), (row.SheetName, row.RowNumber, row.IsRow, row.Pages));
        Assert.Equal(("forms/documents/x.xlsx", "cobranzas.xlsx"), (row.StorageKey, row.FileName));
        Assert.Null(Row(null, 2).Value.SheetName);
        Assert.False(Document().IsRow);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(null, 1_048_577)]
    [InlineData(" ", 5)]
    [InlineData("", 5)]
    public void A_row_needs_a_number_excel_can_have_and_a_sheet_with_text_when_it_has_one(string? sheet, int number) =>
        Assert.True(Row(sheet, number).IsFailure);

    [Fact]
    public void A_row_over_the_loads_of_its_spreadsheet_is_for_the_team_at_once_with_its_reason()
    {
        var row = Row(null, 7, Now.AddHours(24)).Value;

        Assert.True(row.Propose(Guid.NewGuid(), Guid.NewGuid(), [1], FormReceivedDocumentNotSentReason.OverLimit).IsSuccess);

        Assert.Equal((FormReceivedDocumentState.Proposed, true, FormReceivedDocumentNotSentReason.OverLimit), (row.State, row.NotSendable, row.NotSentReason));
        Assert.True(row.IsForReview(Now));
        Assert.Null(FormReceivedDocument.AwaitedUntil(row.State, row.NotSendable, row.ReplyExpiresAtUtc, Now));
        // Nobody waits for its person: a reviewer discards it at once.
        Assert.True(row.Discard(row.Revision, Now).IsSuccess);
    }

    [Fact]
    public void Only_a_row_goes_over_the_loads_and_a_form_that_cannot_be_sent_keeps_that_reason()
    {
        var document = Document();
        Assert.True(document.Propose(Guid.NewGuid(), Guid.NewGuid(), [1], FormReceivedDocumentNotSentReason.OverLimit).IsFailure);
        Assert.True(document.Propose(Guid.NewGuid(), Guid.NewGuid(), [1], true).IsSuccess);
        Assert.Equal((true, FormReceivedDocumentNotSentReason.NotSendable), (document.NotSendable, document.NotSentReason));

        var load = Row(null, 2, Now.AddHours(24)).Value;
        Assert.True(load.Propose(Guid.NewGuid(), Guid.NewGuid(), [1], null).IsSuccess);
        Assert.Equal((false, (FormReceivedDocumentNotSentReason?)null), (load.NotSendable, load.NotSentReason));
        Assert.False(load.IsForReview(Now));
        Assert.Equal(FormReceivedDocumentErrors.AwaitingSender, load.Discard(load.Revision, Now).Error);
        Assert.True(load.Propose(Guid.NewGuid(), Guid.NewGuid(), [1], (FormReceivedDocumentNotSentReason)9).IsFailure);
    }

    [Fact]
    public void A_discarded_row_with_its_reading_and_proposal_is_reopened_for_the_team_and_a_repeated_one_without_a_form()
    {
        var row = Row(null, 3).Value;
        Assert.True(row.Describe([7], "planilla de cobranzas", "61250").IsSuccess);
        Assert.True(row.Propose(Guid.NewGuid(), Guid.NewGuid(), [1], FormReceivedDocumentNotSentReason.OverLimit).IsSuccess);
        Assert.True(row.Discard(row.Revision, Now).IsSuccess);
        Assert.True(row.Reopen(row.Revision, Now).IsSuccess);
        Assert.Equal((FormReceivedDocumentState.Proposed, true, "planilla de cobranzas"), (row.State, row.IsForReview(Now), row.DocumentType));

        var repeated = Row(null, 4).Value;
        Assert.True(repeated.Describe([7], null, "").IsSuccess);
        Assert.True(repeated.Reject("duplicate", [1], Now).IsSuccess);
        Assert.True(repeated.Reopen(repeated.Revision, Now).IsSuccess);
        Assert.Equal(FormReceivedDocumentState.Unclassified, repeated.State);
    }

    [Fact]
    public void A_row_keeps_its_place_in_its_book_and_what_became_of_it_there_whatever_the_team_does_later()
    {
        var row = Row("Ventas", 5, order: 4, kind: FormReceivedDocumentRowKind.OverLimit).Value;
        Assert.True(row.Propose(Guid.NewGuid(), Guid.NewGuid(), [1], FormReceivedDocumentNotSentReason.OverLimit).IsSuccess);

        Assert.True(row.Discard(row.Revision, Now).IsSuccess);

        Assert.Equal((4, FormReceivedDocumentRowKind.OverLimit), (row.RowOrder, row.RowKind));
        Assert.Null(Document().RowOrder);
        Assert.Null(Document().RowKind);
        Assert.True(Row(null, 5, order: 0).IsFailure);
        Assert.True(Row(null, 5, kind: (FormReceivedDocumentRowKind)9).IsFailure);
        Assert.Equal(5, (int)FormReceivedDocumentRowKind.Unseen);
    }

    [Fact]
    public void The_steps_of_a_row_are_stored_as_text_after_the_ones_before_them()
    {
        Assert.Equal(9, (int)FormReceivedDocumentEventKind.Sent);
        Assert.Equal(10, (int)FormReceivedDocumentEventKind.OverLimit);
        Assert.True(FormReceivedDocumentEvent.Record(Guid.NewGuid(), FormReceivedDocumentEventKind.Sent, null, Now, [1]).IsSuccess);
    }

    // A spreadsheet nobody recognized (its single entry, unclassified) becomes the first of its rows for the form its person chose: its place,
    // what became of it and the deadline of the new loads, one revision more. A row, a file with pages, «Otro» or an extraction in course do not.
    [Fact]
    public void A_spreadsheet_nobody_recognized_becomes_its_first_row_and_nothing_else_does()
    {
        var spreadsheet = Spreadsheet();

        Assert.True(spreadsheet.RecognizeRow("Ventas", 2, 1, FormReceivedDocumentRowKind.Load, Now.AddHours(24)).IsSuccess);

        Assert.Equal(("Ventas", (int?)2, (int?)1, (FormReceivedDocumentRowKind?)FormReceivedDocumentRowKind.Load, (DateTime?)Now.AddHours(24), 2L, true),
            (spreadsheet.SheetName, spreadsheet.RowNumber, spreadsheet.RowOrder, spreadsheet.RowKind, spreadsheet.ReplyExpiresAtUtc, spreadsheet.Revision, spreadsheet.IsRow));
        Assert.True(spreadsheet.Propose(Guid.NewGuid(), Guid.NewGuid(), [1], FormReceivedDocumentNotSentReason.OverLimit).IsSuccess);
        var other = Spreadsheet();
        Assert.True(other.ChooseOther(Now).IsSuccess);
        var extracting = Spreadsheet();
        Assert.True(extracting.Describe([1], null, "").IsSuccess);
        Assert.True(extracting.RequestExtraction(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "es", "UTC", Now).IsSuccess);
        var pdf = Document();
        Assert.True(pdf.LeaveUnclassified("unknown", null, Now).IsSuccess);
        Assert.All([Row(null, 3).Value, other, extracting, pdf], entry => Assert.Equal(FormReceivedDocumentErrors.StateConflict.Code,
            entry.RecognizeRow(null, 3, 1, FormReceivedDocumentRowKind.Load, null).Error.Code));
        Assert.True(Spreadsheet().RecognizeRow(" ", 2, 1, FormReceivedDocumentRowKind.Load, null).IsFailure);
        Assert.True(Spreadsheet().RecognizeRow(null, 0, 1, FormReceivedDocumentRowKind.Load, null).IsFailure);
    }

    private static FormReceivedDocument Spreadsheet()
    {
        var spreadsheet = FormReceivedDocument.Receive(Guid.NewGuid(), "whatsapp", "wamid.3", Guid.NewGuid(), Now, "forms/documents/z.xlsx", "cobranzas.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 18_000, Sha, null, Now.AddHours(1)).Value;
        Assert.True(spreadsheet.LeaveUnclassified("unknown", null, Now).IsSuccess);
        return spreadsheet;
    }

    private static Result<FormReceivedDocument> Row(string? sheet, int number, DateTime? replyExpiresAtUtc = null, int order = 1,
        FormReceivedDocumentRowKind kind = FormReceivedDocumentRowKind.Load) =>
        FormReceivedDocument.ReceiveRow(Guid.NewGuid(), "whatsapp", "wamid.1", Guid.NewGuid(), Now, "forms/documents/x.xlsx", "cobranzas.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 18_000, Sha, sheet, number, order, kind, replyExpiresAtUtc);

    private static FormReceivedDocument Document() =>
        FormReceivedDocument.Receive(Guid.NewGuid(), "whatsapp", "wamid.2", Guid.NewGuid(), Now, "forms/documents/y.pdf", "a.pdf", "application/pdf", 10, Sha, 1,
            null).Value;
}
