using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// VC1-A, task 28: the rows of a spreadsheet in the inbox. Each row keeps its row with the headers of its sheet as its reading, under a kind of
// its own, and shares the copy of its spreadsheet; only a load waits for its person and each step of its history says what became of it. A
// spreadsheet without rows is one entry. A row changes form with propose-rows instead of propose-entry.
public sealed class FormReceivedDocumentRowTests
{
    private const string Sha = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    [Fact]
    public void A_row_reading_keeps_its_headers_and_cells_by_column_and_gives_back_the_row_as_the_copy_had_it()
    {
        var row = new FormDocumentSheetRow("Ventas", 5, [new("Fecha"), new(null), new("#N/A", FormRecordErrors.InvalidResponse)],
            [new("2026-11-11", null, true), new("Almacén"), new(null, FormRecordErrors.InvalidResponse), new(null)]);

        var reading = FormDocumentRowReading.From(row, 3, "es");
        var back = reading.SheetRow();

        Assert.Equal(("Ventas", 3, 5, "es"), (reading.Sheet, reading.HeaderNumber, reading.Number, reading.Culture));
        Assert.Equal(["Fecha", null, null], reading.Headers);
        Assert.Equal([("2026-11-11", false, true), ("Almacén", false, false), (null, true, false), (null, false, false)],
            reading.Cells.Select(cell => (cell.Text, cell.Error, cell.Canonical)));
        Assert.Equal((row.Sheet, row.Number), (back.Sheet, back.Number));
        Assert.Equal([("2026-11-11", false, true), ("Almacén", false, false), (null, true, false), (null, false, false)],
            back.Cells.Select(cell => (cell.Text, cell.Error is not null, cell.IsCanonicalValue)));
        Assert.Equal("FormDocumentRowReading { Content = [REDACTED] }", reading.ToString());
    }

    [Fact]
    public void The_rows_of_a_spreadsheet_share_its_copy_keep_their_row_and_only_a_load_waits_for_its_person()
    {
        var fixture = new FormReceivedDocumentFixture();
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        var request = Request(now.AddHours(1));
        var definitionId = Guid.CreateVersion7();
        var versionId = Guid.CreateVersion7();
        var candidate = new FormDocumentPreparedCandidate(definitionId, versionId, 2, [new(Guid.CreateVersion7(), "decimal", "61250", null) { Searchable = true }]);
        var proposal = new FormDocumentPreparedProposal(FormDocumentEntryProposal.Sure, "planilla de cobranzas", [candidate]);
        var reading = FormDocumentRowReading.From(new("Hoja1", 2, [new("Importe")], [new("61250")]), 1, "es");
        FormReceivedDocumentRowEntry[] entries =
        [
            new(Guid.CreateVersion7(), null, 2, 1, FormReceivedDocumentRowKind.Load, FormReceivedDocumentState.Proposed, null, proposal, null, true, reading,
                "planilla de cobranzas",
                [(FormReceivedDocumentEventKind.Received, "sure"), (FormReceivedDocumentEventKind.Sent, "1/1")]),
            new(Guid.CreateVersion7(), null, 3, 2, FormReceivedDocumentRowKind.OverLimit, FormReceivedDocumentState.Proposed, null, proposal,
                FormReceivedDocumentNotSentReason.OverLimit, false, reading,
                "planilla de cobranzas", [(FormReceivedDocumentEventKind.Received, "sure"), (FormReceivedDocumentEventKind.OverLimit, null)]),
            new(Guid.CreateVersion7(), null, 4, 3, FormReceivedDocumentRowKind.Repeated, FormReceivedDocumentState.Discarded, FormReceivedDocumentReplyPolicy.Duplicate,
                proposal with { Repetition = new(now, null, null, Guid.CreateVersion7()) { RowNumber = 2 } }, null, false, reading, "planilla de cobranzas",
                [(FormReceivedDocumentEventKind.Received, "duplicate")]),
        ];

        var registered = fixture.Recorder().RegisterRows(request, "forms/documents/a.xlsx", entries);

        Assert.True(registered.IsSuccess);
        Assert.Equal([2, 3, 4], fixture.Documents.Select(document => document.RowNumber!.Value));
        Assert.All(fixture.Documents, document => Assert.Equal(("forms/documents/a.xlsx", "cobranzas.xlsx", (int?)null, "61250"),
            (document.StorageKey, document.FileName, document.Pages, document.SearchText)));
        Assert.Equal([request.ReplyExpiresAtUtc, null, null], fixture.Documents.Select(document => document.ReplyExpiresAtUtc));
        Assert.Equal([false, true, false], fixture.Documents.Select(document => document.IsForReview(now)));
        Assert.Equal([null, FormReceivedDocumentNotSentReason.OverLimit, null], fixture.Documents.Select(document => document.NotSentReason));
        // Each keeps its place in the book and what became of it, which a repeated registration answers.
        Assert.Equal([(1, FormReceivedDocumentRowKind.Load), (2, FormReceivedDocumentRowKind.OverLimit), (3, FormReceivedDocumentRowKind.Repeated)],
            fixture.Documents.Select(document => (document.RowOrder!.Value, document.RowKind!.Value)));
        var contents = new FormReceivedDocumentContentPolicy(fixture);
        var first = fixture.Documents[0];
        // The row is its reading under its own kind: it never reads as the reading of a document.
        Assert.Equal(("Hoja1", 2), (contents.ReadRow(first.Id, first.ProtectedReading)!.Sheet, contents.ReadRow(first.Id, first.ProtectedReading)!.Number));
        Assert.Null(contents.ReadReading(first.Id, first.ProtectedReading));
        Assert.Equal(2, contents.ReadProposal(fixture.Documents[2].Id, fixture.Documents[2].ProtectedProposal)!.Repetition!.RowNumber);
        Assert.Equal([(FormReceivedDocumentEventKind.Received, "sure"), (FormReceivedDocumentEventKind.Sent, "1/1"), (FormReceivedDocumentEventKind.Received, "sure"),
            (FormReceivedDocumentEventKind.OverLimit, null), (FormReceivedDocumentEventKind.Received, "duplicate")],
            fixture.Events.Select(step => (step.Kind, contents.ReadDetail(step.DocumentId, step.ProtectedDetail))));
    }

    [Fact]
    public void A_row_without_a_form_is_searched_by_the_numbers_of_its_cells_like_a_document_by_its_reading()
    {
        var fixture = new FormReceivedDocumentFixture();
        var reading = FormDocumentRowReading.From(new("Hoja1", 7, [new("Importe"), new("CUIT"), new("Fecha"), new("Nota")],
            [new("98765.25", null, true), new("20-12345678-6"), new("2026-11-11", null, true), new("#N/A", FormRecordErrors.InvalidResponse)]), 1, "es");
        FormReceivedDocumentRowEntry[] entries =
        [
            new(Guid.CreateVersion7(), null, 7, 1, FormReceivedDocumentRowKind.Unseen, FormReceivedDocumentState.Unclassified, FormReceivedDocumentReplyPolicy.Unknown,
                null, null, false, reading, "planilla de cobranzas", [(FormReceivedDocumentEventKind.Received, FormReceivedDocumentReplyPolicy.Unknown)]),
        ];

        Assert.True(fixture.Recorder().RegisterRows(Request(null), "forms/documents/c.xlsx", entries).IsSuccess);

        // Decision 16: a number the spreadsheet typed, with and without its cents, and a CUIT whose check digit passes; never a date.
        Assert.Equal("9876525 98765 20123456786", Assert.Single(fixture.Documents).SearchText);
    }

    [Fact]
    public void A_row_with_only_empty_cells_or_errors_has_no_text_and_is_no_entry()
    {
        Assert.False(FormReceivedDocumentRowPolicy.HasText(new("Hoja1", 4, [new("Monto")],
            [new("#N/A", FormRecordErrors.InvalidResponse), new(" "), new(null), new("#REF!", FormRecordErrors.InvalidResponse)])));
        Assert.True(FormReceivedDocumentRowPolicy.HasText(new("Hoja1", 5, [new("Monto")], [new("#REF!", FormRecordErrors.InvalidResponse), new("12")])));
    }

    [Fact]
    public void A_spreadsheet_without_rows_is_one_entry_with_its_copy_and_type_and_no_reading()
    {
        var fixture = new FormReceivedDocumentFixture();
        var request = Request(null);
        var id = Guid.CreateVersion7();
        var unknown = new FormReceivedDocumentClassification(FormReceivedDocumentState.Unclassified, FormReceivedDocumentReplyPolicy.Unknown,
            new FormDocumentPreparedProposal(FormDocumentEntryProposal.Unknown, "planilla de cobranzas", []), false, new(id, StructuredDocumentReplyKind.Unknown));

        Assert.True(fixture.Recorder().RegisterSpreadsheet(id, "forms/documents/b.xlsx", request, unknown).IsSuccess);

        var document = Assert.Single(fixture.Documents);
        Assert.Equal((FormReceivedDocumentState.Unclassified, "unknown", "planilla de cobranzas", false, (byte[]?)null),
            (document.State, document.ReasonCode, document.DocumentType, document.IsRow, document.ProtectedReading));
        Assert.Equal(FormReceivedDocumentEventKind.Received, Assert.Single(fixture.Events).Kind);
    }

    [Fact]
    public void A_row_changes_form_with_the_rows_mapping_and_a_document_with_its_entry()
    {
        var row = FormReceivedDocumentExtractionPolicy.Context(Guid.NewGuid(), "es", "America/Argentina/Buenos_Aires", Guid.NewGuid(), Guid.NewGuid(), row: true);
        var document = FormReceivedDocumentExtractionPolicy.Context(Guid.NewGuid(), "es", "America/Argentina/Buenos_Aires", Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal([BusinessFormDocumentAssistantCapabilitySource.FormId, BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId],
            row.AllowedCapabilities.Select(capability => capability.Id));
        Assert.Equal([BusinessFormDocumentAssistantCapabilitySource.FormId, BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId],
            document.AllowedCapabilities.Select(capability => capability.Id));
        Assert.Equal((FormSpreadsheetFormat.Xlsx, FormSpreadsheetFormat.Csv, (FormSpreadsheetFormat?)null), (FormReceivedDocumentRowPolicy.Format(FormReceivedDocumentRowPolicy.XlsxMediaType),
            FormReceivedDocumentRowPolicy.Format("text/csv"), FormReceivedDocumentRowPolicy.Format("application/pdf")));
        Assert.True(FormReceivedDocumentCopyPolicy.TryParseKey(FormReceivedDocumentCopyPolicy.Key(Guid.NewGuid(), FormReceivedDocumentRowPolicy.XlsxMediaType), out _));
        Assert.EndsWith(".csv", FormReceivedDocumentCopyPolicy.Key(Guid.NewGuid(), "text/csv"), StringComparison.Ordinal);
    }

    private static StructuredDocumentRowsRegistrationRequest Request(DateTime? replyExpiresAtUtc) =>
        new("whatsapp", "wamid.rows", Guid.NewGuid(), "es", new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc),
            new("channel/documents/a.xlsx", "cobranzas.xlsx", FormReceivedDocumentRowPolicy.XlsxMediaType, 18_000, Sha, null)) { ReplyExpiresAtUtc = replyExpiresAtUtc };
}
