using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
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

    // Decision 12: a row changes form with the fixed form's read and propose-rows on a sample of its own row, so the engine offers that read
    // with a spreadsheet too, never without a file.
    [Fact]
    public void The_fixed_forms_read_goes_with_the_sample_of_a_row_as_with_a_document()
    {
        var form = new BusinessFormDocumentAssistantCapabilitySource(new FormReceivedDocumentFixture(), new InMemoryUserAccounts(), new FakePermissionService())
            .Definitions.Single(definition => definition.Id == BusinessFormDocumentAssistantCapabilitySource.FormId);

        Assert.Equal((true, true, false), (form.OfferedWith(AssistantAttachmentKind.Spreadsheet), form.OfferedWith(AssistantAttachmentKind.Document),
            form.OfferedWith(AssistantAttachmentKind.None)));
    }

    // What the inbox keeps of each row: one the model did not see is unclassified as unseen (reason and history), apart from one that fits no form.
    [Fact]
    public void A_row_the_model_did_not_see_is_kept_as_unseen_and_one_that_fits_no_form_as_unknown()
    {
        var unseen = FormReceivedDocumentRowPolicy.Classified(StructuredDocumentRowKind.Unseen, null, 1);
        var unmatched = FormReceivedDocumentRowPolicy.Classified(StructuredDocumentRowKind.Unmatched, null, 1);
        var load = FormReceivedDocumentRowPolicy.Classified(StructuredDocumentRowKind.Load, 2, 3);

        Assert.Equal((FormReceivedDocumentState.Unclassified, "unseen", (FormReceivedDocumentNotSentReason?)null), (unseen.State, unseen.ReasonCode, unseen.NotSent));
        Assert.Equal([(FormReceivedDocumentEventKind.Received, "unseen")], unseen.Steps);
        Assert.Equal((FormReceivedDocumentState.Unclassified, "unknown"), (unmatched.State, unmatched.ReasonCode));
        Assert.Equal([(FormReceivedDocumentEventKind.Received, "unknown")], unmatched.Steps);
        Assert.Equal([(FormReceivedDocumentEventKind.Received, "sure"), (FormReceivedDocumentEventKind.Sent, "2/3")], load.Steps);
    }

    // A spreadsheet over the fifty rows to load enters discarded with its own reason, not as one that cannot be read.
    [Fact]
    public void A_spreadsheet_with_too_many_rows_is_discarded_as_too_many_rows() =>
        Assert.Equal(("tooManyRows", "unreadable"), (FormReceivedDocumentReplyPolicy.RejectionCode(StructuredAttachmentRejection.TooManyRows),
            FormReceivedDocumentReplyPolicy.RejectionCode(StructuredAttachmentRejection.Unreadable)));

    // The rows' port (VC1-A): a spreadsheet nobody recognized becomes its rows for the form its person chose. Its single entry is the first row,
    // with its copy and a step saying the form was chosen instead of arriving again; the others enter with the steps of a first registration and
    // arrived with the spreadsheet, not with the tap that chose the form.
    [Fact]
    public void A_spreadsheet_nobody_recognized_becomes_its_rows_and_its_single_entry_is_the_first_one()
    {
        var fixture = new FormReceivedDocumentFixture();
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        var request = Request(now.AddHours(2)) with { FocusDefinitionId = Guid.CreateVersion7() };
        var single = FormReceivedDocument.Receive(Guid.CreateVersion7(), request.ChannelId, request.SourceId, request.SenderUserId, request.ReceivedAtUtc,
            "forms/documents/d.xlsx", "cobranzas.xlsx", FormReceivedDocumentRowPolicy.XlsxMediaType, 18_000, Sha, null, now.AddHours(1)).Value;
        Assert.True(single.LeaveUnclassified(FormReceivedDocumentReplyPolicy.Unknown, null, now).IsSuccess);
        var candidate = new FormDocumentPreparedCandidate(request.FocusDefinitionId!.Value, Guid.CreateVersion7(), 2,
            [new(Guid.CreateVersion7(), "decimal", "61250", null) { Searchable = true }]);
        var proposal = new FormDocumentPreparedProposal(FormDocumentEntryProposal.Sure, "planilla de cobranzas", [candidate]);
        var reading = FormDocumentRowReading.From(new("Hoja1", 2, [new("Importe")], [new("61250")]), 1, "es");
        var load = FormReceivedDocumentRowPolicy.Classified(StructuredDocumentRowKind.Load, 1, 1);
        var unseen = FormReceivedDocumentRowPolicy.Classified(StructuredDocumentRowKind.Unseen, null, 1);
        FormReceivedDocumentRowEntry[] entries =
        [
            new(single.Id, null, 2, 1, FormReceivedDocumentRowKind.Load, load.State, load.ReasonCode, proposal, load.NotSent, true, reading, "planilla de cobranzas", load.Steps),
            new(Guid.CreateVersion7(), null, 3, 2, FormReceivedDocumentRowKind.Unseen, unseen.State, unseen.ReasonCode, null, unseen.NotSent, false, reading,
                "planilla de cobranzas", unseen.Steps),
        ];

        var tapped = request with { ReceivedAtUtc = request.ReceivedAtUtc.AddMinutes(5) };
        Assert.True(fixture.Recorder().RecognizeRows(single, tapped, entries).IsSuccess);

        var added = Assert.Single(fixture.Documents);
        Assert.Equal(single.ReceivedAtUtc, added.ReceivedAtUtc);
        Assert.Equal((2, FormReceivedDocumentState.Proposed, request.FocusDefinitionId, request.ReplyExpiresAtUtc, 2L),
            (single.RowNumber!.Value, single.State, single.DefinitionId, single.ReplyExpiresAtUtc, single.Revision));
        Assert.Equal(("forms/documents/d.xlsx", 3, FormReceivedDocumentState.Unclassified, "unseen", (DateTime?)null),
            (added.StorageKey, added.RowNumber!.Value, added.State, added.ReasonCode, added.ReplyExpiresAtUtc));
        var contents = new FormReceivedDocumentContentPolicy(fixture);
        Assert.Equal(2, contents.ReadRow(single.Id, single.ProtectedReading)!.Number);
        Assert.Equal([(single.Id, FormReceivedDocumentEventKind.FormChosen, "sure"), (single.Id, FormReceivedDocumentEventKind.Sent, "1/1"),
            (added.Id, FormReceivedDocumentEventKind.Received, "unseen")],
            fixture.Events.Select(step => (step.DocumentId, step.Kind, contents.ReadDetail(step.DocumentId, step.ProtectedDetail))));
        // Only an entry nobody recognized becomes rows.
        Assert.True(fixture.Recorder().RecognizeRows(single, tapped, entries).IsFailure);
    }

    // Rule B4 (review of task 29b): the same spreadsheet sent twice before choosing a form. Once the first became its rows, choosing a form for the
    // second does not load them again: it is discarded as a repeated document with when the first arrived, as at a first registration, under the
    // source's lock and then its entry's; choosing again answers what was stored.
    [Fact]
    public async Task Choosing_a_form_for_the_same_spreadsheet_whose_rows_the_inbox_has_discards_it_as_repeated_once()
    {
        var inbox = new FormReceivedDocumentFixture();
        var (service, unitOfWork, sender) = Service(inbox);
        var request = Chosen(sender);
        var earlier = Row(inbox, request, "wamid.before");
        var single = Single(inbox, request);

        var reply = await service.RecognizeRowsAsync(request, Ct);

        Assert.Equal((StructuredDocumentReplyKind.Duplicate, (Guid?)single.Id, (DateTime?)earlier.ReceivedAtUtc, false),
            (reply.Kind, reply.DocumentId, reply.Duplicate?.AtUtc, reply.WasAlreadyRegistered));
        Assert.Equal((FormReceivedDocumentState.Discarded, "duplicate", (int?)null, 2), (single.State, single.ReasonCode, single.RowNumber, inbox.Documents.Count));
        var contents = new FormReceivedDocumentContentPolicy(inbox);
        var repetition = contents.ReadProposal(single.Id, single.ProtectedProposal)!.Repetition!;
        Assert.Equal((earlier.ReceivedAtUtc, (Guid?)earlier.Id), (repetition.AtUtc, repetition.DocumentId));
        Assert.Equal([(single.Id, FormReceivedDocumentEventKind.FormChosen, "duplicate")],
            inbox.Events.Select(step => (step.DocumentId, step.Kind, contents.ReadDetail(step.DocumentId, step.ProtectedDetail))));
        Assert.Equal([$"source:whatsapp:{request.SourceId}", $"document:{single.Id:N}"], inbox.LockOrder);
        Assert.Equal((1, (CommitPolicy?)CommitPolicy.OnSuccess), (unitOfWork.Commits, unitOfWork.LastPolicy));

        var again = await service.RecognizeRowsAsync(request, Ct);

        Assert.Equal((StructuredDocumentReplyKind.Duplicate, (DateTime?)earlier.ReceivedAtUtc, true), (again.Kind, again.Duplicate?.AtUtc, again.WasAlreadyRegistered));
        Assert.Single(inbox.Events);
    }

    // Review of task 29b: recognizing takes its entry's lock after the source's, the last one, like a reviewer's change and task 9's, and reads it
    // again under it: a «Descartar» that committed while it waited is not overwritten.
    [Fact]
    public async Task Recognizing_reads_the_entry_again_under_its_lock_and_keeps_what_a_reviewer_did_meanwhile()
    {
        var inbox = new FormReceivedDocumentFixture();
        var (service, _, sender) = Service(inbox);
        var request = Chosen(sender);
        Row(inbox, request, "wamid.before");
        var single = Single(inbox, request);
        inbox.WhileLocking = id => Assert.True(inbox.Documents.Single(document => document.Id == id)
            .Discard(single.Revision, inbox.Clock.GetUtcNow().UtcDateTime).IsSuccess);

        var reply = await service.RecognizeRowsAsync(request, Ct);

        Assert.Equal([$"source:whatsapp:{request.SourceId}", $"document:{single.Id:N}"], inbox.LockOrder);
        Assert.Equal((FormReceivedDocumentState.Discarded, FormReceivedDocument.DiscardedByReviewer), (single.State, single.ReasonCode));
        Assert.Empty(inbox.Events);
        Assert.Equal((StructuredDocumentReplyKind.Rejected, true), (reply.Kind, reply.WasAlreadyRegistered));
    }

    // Only the single entry of a spreadsheet nobody recognized, of the person who chooses, becomes rows: «Otro», the quota, a file that is not a
    // spreadsheet and an entry already classified (by the team, or already its rows) answer as stored, and another sender's is not theirs to hear
    // about. None takes a lock or changes anything.
    [Theory]
    [InlineData("other", StructuredDocumentReplyKind.Unknown)]
    [InlineData("quota", StructuredDocumentReplyKind.Quota)]
    [InlineData("document", StructuredDocumentReplyKind.Unknown)]
    [InlineData("discarded", StructuredDocumentReplyKind.Rejected)]
    [InlineData("rows", StructuredDocumentReplyKind.Sure)]
    [InlineData("sender", StructuredDocumentReplyKind.Unavailable)]
    public async Task Only_a_spreadsheet_nobody_recognized_of_the_person_who_chooses_becomes_its_rows(string stored, StructuredDocumentReplyKind expected)
    {
        var inbox = new FormReceivedDocumentFixture();
        var (service, unitOfWork, sender) = Service(inbox);
        var request = Chosen(sender);
        if (stored == "rows") Row(inbox, request, request.SourceId);
        else
        {
            var single = Single(inbox, request, stored switch
            {
                "other" => FormReceivedDocument.ChoseOther,
                "quota" => FormReceivedDocumentReplyPolicy.Quota,
                _ => FormReceivedDocumentReplyPolicy.Unknown,
            }, stored == "document" ? "application/pdf" : FormReceivedDocumentRowPolicy.XlsxMediaType, stored == "sender" ? Guid.NewGuid() : null);
            if (stored == "discarded") Assert.True(single.Discard(single.Revision, inbox.Clock.GetUtcNow().UtcDateTime).IsSuccess);
        }
        var before = inbox.Documents.Select(document => (document.State, document.ReasonCode, document.Revision)).ToArray();

        var reply = await service.RecognizeRowsAsync(request, Ct);

        Assert.Equal((expected, expected != StructuredDocumentReplyKind.Unavailable), (reply.Kind, reply.WasAlreadyRegistered));
        Assert.Equal(before, inbox.Documents.Select(document => (document.State, document.ReasonCode, document.Revision)));
        Assert.Empty(inbox.Events);
        Assert.Empty(inbox.LockOrder);
        Assert.Equal(0, unitOfWork.Transactions);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The rows' port over the inbox in memory, for a sender who can load; what arms rows from the copy is not reached here.
    private static (FormReceivedDocumentRowService Service, FakeUnitOfWork UnitOfWork, Guid Sender) Service(FormReceivedDocumentFixture inbox)
    {
        var users = new InMemoryUserAccounts();
        var permissions = new FakePermissionService();
        var sender = users.AddUser("carga@example.test").Id;
        permissions.Permissions[sender] = [BusinessFormsPermissions.RecordsCreate];
        var contents = new FormReceivedDocumentContentPolicy(inbox);
        var replies = new FormReceivedDocumentReplyPolicy(inbox, new FormDocumentProposalPolicy(inbox, inbox, []), null!, contents);
        var unitOfWork = new FakeUnitOfWork();
        var service = new FormReceivedDocumentRowService(inbox, inbox, new FormReceivedDocumentRowPolicy(inbox, null!, null!, null!, null!, replies, contents),
            null!, new FormReceivedDocumentAccessGuard(new FakeCurrentUser(), users, permissions, inbox), inbox.Recorder(), unitOfWork);
        return (service, unitOfWork, sender);
    }

    // A form chosen from the list for the spreadsheet of the request's source, with the proposal of the interpretation that continued.
    private static StructuredDocumentRowsRegistrationRequest Chosen(Guid sender) => Request(null) with
    {
        SenderUserId = sender, FocusDefinitionId = Guid.CreateVersion7(), SampleSha256 = Sha,
        Proposal = new(Guid.CreateVersion7(), Guid.CreateVersion7(),
            new(BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId, BusinessFormDocumentAssistantCapabilitySource.Version),
            new FormDocumentRowsProposal("proposed", 0, [], [], [], "planilla de cobranzas")),
    };

    // The single entry a spreadsheet left at the request's source (none of its rows fit, «Otro», the quota), from its sender or another.
    private static FormReceivedDocument Single(FormReceivedDocumentFixture inbox, StructuredDocumentRowsRegistrationRequest request,
        string reason = FormReceivedDocumentReplyPolicy.Unknown, string mediaType = FormReceivedDocumentRowPolicy.XlsxMediaType, Guid? sender = null)
    {
        var single = FormReceivedDocument.Receive(Guid.CreateVersion7(), request.ChannelId, request.SourceId, sender ?? request.SenderUserId, request.ReceivedAtUtc,
            "forms/documents/d.xlsx", "cobranzas.xlsx", mediaType, 18_000, Sha, null, request.ReceivedAtUtc.AddHours(1)).Value;
        Assert.True(single.LeaveUnclassified(reason, null, inbox.Clock.GetUtcNow().UtcDateTime).IsSuccess);
        inbox.Documents.Add(single);
        return single;
    }

    // A row of the same spreadsheet in the inbox, five minutes older: at a source of its own (the same file sent before) or at the request's.
    private static FormReceivedDocument Row(FormReceivedDocumentFixture inbox, StructuredDocumentRowsRegistrationRequest request, string sourceId)
    {
        var row = FormReceivedDocument.ReceiveRow(Guid.CreateVersion7(), request.ChannelId, sourceId, request.SenderUserId, request.ReceivedAtUtc.AddMinutes(-5),
            "forms/documents/b.xlsx", "cobranzas.xlsx", FormReceivedDocumentRowPolicy.XlsxMediaType, 18_000, Sha, null, 2, 1, FormReceivedDocumentRowKind.Unmatched,
            null).Value;
        Assert.True(row.LeaveUnclassified(FormReceivedDocumentReplyPolicy.Unknown, null, inbox.Clock.GetUtcNow().UtcDateTime).IsSuccess);
        inbox.Documents.Add(row);
        return row;
    }

    private static StructuredDocumentRowsRegistrationRequest Request(DateTime? replyExpiresAtUtc) =>
        new("whatsapp", "wamid.rows", Guid.NewGuid(), "es", new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc),
            new("channel/documents/a.xlsx", "cobranzas.xlsx", FormReceivedDocumentRowPolicy.XlsxMediaType, 18_000, Sha, null)) { ReplyExpiresAtUtc = replyExpiresAtUtc };
}
