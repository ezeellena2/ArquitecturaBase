using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// VC1-A, task 28, with PostgreSQL: each row of a spreadsheet a channel registers is an entry of «Documentos recibidos» (its sheet, its number,
// its row as its reading and the copy of its spreadsheet, which its rows share), the loads wait for their person and the rest is for the team,
// a repeated one points to its row, the same spreadsheet again is discarded, the copy stays while a row needs it, a row changes form from what
// it kept, and its response says it came from the spreadsheet. Bodies for the cohort forms-documents-http.
[Collection(ApiTestGroup.Name)]
public sealed class FormReceivedDocumentRowHttpTests(ApiFactory factory)
{
    private const string SearchPath = "/api/forms/documents/search";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_row_is_an_entry_with_its_number_its_reading_and_the_copy_of_its_spreadsheet()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var arrived = factory.Clock.GetUtcNow().UtcDateTime;
        var content = FormReceivedDocumentRowScenario.Workbook(("150000", "778899"), ("2000", "111"), ("150000", "778899"), ("3000", "222"), ("4000", "333"),
            ("nota", ""));
        var proposal = FormReceivedDocumentRowScenario.Proposal(scenario.Form, scenario.Sender, [2, 3, 4, 5, 6], [7], [2, 3, 4, 5, 6, 7]);

        var (reply, request) = await scenario.SendRowsAsync(content, proposal, Ct, arrived.AddHours(1));

        Assert.Equal(StructuredDocumentReplyKind.Sure, reply.Kind);
        Assert.Equal([(2, StructuredDocumentRowKind.Load, (int?)1), (3, StructuredDocumentRowKind.Load, 2), (4, StructuredDocumentRowKind.Repeated, null),
            (5, StructuredDocumentRowKind.Load, 3), (6, StructuredDocumentRowKind.OverLimit, null), (7, StructuredDocumentRowKind.Unmatched, null)],
            reply.Rows.Select(row => (row.Number, row.Kind, row.Position)));
        Assert.Equal((3, (string?)null), (reply.Loads, reply.Rows[0].Sheet));
        Assert.Equal(new StructuredDocumentRowReference(null, 2), reply.Rows[2].SameAs);
        Assert.Equal((scenario.Form.DefinitionId, scenario.Form.Name), (reply.Rows[0].Form!.DefinitionId, reply.Rows[0].Form!.Name));
        Assert.Contains("778899", reply.Rows[0].Summary);
        var stored = await scenario.DbAsync(db => db.Set<FormReceivedDocument>().AsNoTracking().Where(document => document.SourceId == request.SourceId)
            .OrderBy(document => document.RowNumber).ToListAsync(Ct));
        Assert.Equal([2, 3, 4, 5, 6, 7], stored.Select(document => document.RowNumber!.Value));
        // One copy for the whole spreadsheet, named after its first entry.
        Assert.Equal(FormReceivedDocumentCopyPolicy.Key(reply.Rows[0].DocumentId, FormReceivedDocumentRowScenario.MediaType),
            Assert.Single(stored.Select(document => document.StorageKey).Distinct()));
        Assert.Equal([FormReceivedDocumentState.Proposed, FormReceivedDocumentState.Proposed, FormReceivedDocumentState.Discarded, FormReceivedDocumentState.Proposed,
            FormReceivedDocumentState.Proposed, FormReceivedDocumentState.Unclassified], stored.Select(document => document.State));
        Assert.Equal([null, null, null, null, FormReceivedDocumentNotSentReason.OverLimit, null], stored.Select(document => document.NotSentReason));
        Assert.Equal([true, true, false, true, false, false], stored.Select(document => document.ReplyExpiresAtUtc is not null));
        Assert.All(stored, document => Assert.Null(document.SheetName));

        // A repeated registration answers what was stored, with the same entries.
        var replayed = await scenario.RegisterAsync(request, Ct);
        Assert.True(replayed.WasAlreadyRegistered);
        Assert.Equal(reply.Rows.Select(row => (row.DocumentId, row.Kind, row.Position)), replayed.Rows.Select(row => (row.DocumentId, row.Kind, row.Position)));
        // The same spreadsheet in another message enters discarded, once, as a repeated document.
        var (duplicate, duplicateRequest) = await scenario.SendRowsAsync(content, proposal, Ct);
        Assert.Equal((StructuredDocumentReplyKind.Duplicate, arrived), (duplicate.Kind, duplicate.Duplicate!.AtUtc));
        var single = await scenario.DbAsync(db => db.Set<FormReceivedDocument>().AsNoTracking().SingleAsync(document => document.SourceId == duplicateRequest.SourceId, Ct));
        Assert.Equal((FormReceivedDocumentState.Discarded, "duplicate", (int?)null, duplicate.DocumentId), (single.State, single.ReasonCode, single.RowNumber, (Guid?)single.Id));

        using var reviewer = scenario.Client(scenario.Reviewer);
        using var page = await reviewer.PostAsJsonAsync(SearchPath, new { receivedFromUtc = arrived, receivedToUtc = arrived.AddMinutes(1), pageSize = 100 }, Ct);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(page, "documents-rows-es", Ct);
        var items = (await page.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetGuid());
        var load = items[reply.Rows[0].DocumentId];
        Assert.Equal((JsonValueKind.Null, 2, false, JsonValueKind.String), (load.GetProperty("row").GetProperty("sheet").ValueKind,
            load.GetProperty("row").GetProperty("number").GetInt32(), load.GetProperty("forReview").GetBoolean(), load.GetProperty("replyExpiresAtUtc").ValueKind));
        var over = items[reply.Rows[4].DocumentId];
        Assert.Equal((true, JsonValueKind.Null), (over.GetProperty("forReview").GetBoolean(), over.GetProperty("replyExpiresAtUtc").ValueKind));
        Assert.False(items[single.Id].TryGetProperty("row", out _));
        // «Propuestos» brings the ones waiting for their person and the ones for the team; the numbers read find a row.
        using var proposed = await reviewer.PostAsJsonAsync(SearchPath, new { receivedFromUtc = arrived, receivedToUtc = arrived.AddMinutes(1), pageSize = 100, state = "proposed" }, Ct);
        var proposedIds = (await proposed.ReadJsonAsync()).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToHashSet();
        Assert.True(proposedIds.IsSupersetOf([reply.Rows[0].DocumentId, reply.Rows[1].DocumentId, reply.Rows[3].DocumentId, reply.Rows[4].DocumentId]));
        using var searched = await reviewer.PostAsJsonAsync(SearchPath, new { receivedFromUtc = arrived, receivedToUtc = arrived.AddMinutes(1), pageSize = 100, search = "4000" }, Ct);
        Assert.Equal(reply.Rows[4].DocumentId, Assert.Single((await searched.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());

        // «La fila»: its cells with the headers of its sheet instead of a reading, and its history as one of the loads.
        using var detail = await reviewer.GetAsync($"/api/forms/documents/{reply.Rows[0].DocumentId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(detail, "document-row-detail-es", Ct);
        var body = await detail.ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("reading").ValueKind);
        Assert.Equal([("Monto", "150000"), ("Operación", "778899")], body.GetProperty("rowCells").EnumerateArray()
            .Select(cell => (cell.GetProperty("header").GetString(), cell.GetProperty("value").GetString())));
        Assert.Equal([("received", "sure"), ("sent", "1/3")], body.GetProperty("history").EnumerateArray()
            .Select(step => (step.GetProperty("kind").GetString(), step.GetProperty("detail").GetString())));
        using var repeated = await reviewer.GetAsync($"/api/forms/documents/{reply.Rows[2].DocumentId}", Ct);
        await FormReceivedDocumentContractCapture.WriteAsync(repeated, "document-row-repeated-es", Ct);
        var repetition = (await repeated.ReadJsonAsync()).GetProperty("proposal").GetProperty("repetition");
        Assert.Equal((reply.Rows[0].DocumentId, JsonValueKind.Null, JsonValueKind.Null, 2), (repetition.GetProperty("documentId").GetGuid(),
            repetition.GetProperty("recordNumber").ValueKind, repetition.GetProperty("row").GetProperty("sheet").ValueKind, repetition.GetProperty("row").GetProperty("number").GetInt32()));
        using var overDetail = await reviewer.GetAsync($"/api/forms/documents/{reply.Rows[4].DocumentId}", Ct);
        Assert.Equal(["received", "overLimit"], (await overDetail.ReadJsonAsync()).GetProperty("history").EnumerateArray().Select(step => step.GetProperty("kind").GetString()));

        // A row that waits for its person gives 409 with its reason; its file is the spreadsheet.
        using var waiting = await reviewer.PostAsJsonAsync($"/api/forms/documents/{reply.Rows[1].DocumentId}/discard", new { expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, waiting.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(waiting, "document-row-awaiting-sender-es", Ct);
        Assert.Equal(FormReceivedDocumentErrors.AwaitingSender.Code, (await waiting.ReadJsonAsync()).GetProperty("code").GetString());
        using var file = await reviewer.GetAsync($"/api/forms/documents/{reply.Rows[3].DocumentId}/file", Ct);
        Assert.Equal((HttpStatusCode.OK, FormReceivedDocumentRowScenario.MediaType), (file.StatusCode, file.Content.Headers.ContentType!.MediaType));
        Assert.Equal(content, await file.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task A_row_passed_to_the_form_takes_no_file_and_its_response_says_it_came_from_the_spreadsheet()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var content = FormReceivedDocumentRowScenario.Workbook(("150001", "10"), ("2001", "20"), ("3001", "30"), ("4001", "40"));
        var (reply, _) = await scenario.SendRowsAsync(content, FormReceivedDocumentRowScenario.Proposal(scenario.Form, scenario.Sender, [2, 3, 4, 5], [], [2, 3, 4, 5]),
            Ct, factory.Clock.GetUtcNow().UtcDateTime.AddHours(1));
        var over = reply.Rows[3];
        Assert.Equal(StructuredDocumentRowKind.OverLimit, over.Kind);
        using var reviewer = scenario.Client(scenario.Reviewer);

        using var passed = await reviewer.PostAsJsonAsync($"/api/forms/documents/{over.DocumentId}/records", new
        {
            requestId = Guid.NewGuid(), versionId = scenario.Form.VersionId, expectedRevision = 1,
            answers = new object[] { new { fieldId = scenario.Form.AmountId, value = "4001" }, new { fieldId = scenario.Form.OperationId, value = "40" } },
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, passed.StatusCode);
        var recordId = (await passed.ReadJsonAsync()).GetProperty("recordId").GetGuid();
        Assert.Empty(await scenario.DbAsync(db => db.Set<BusinessFormRecordFile>().AsNoTracking().Where(value => value.RecordId == recordId).ToListAsync(Ct)));
        using var detail = await reviewer.GetAsync($"/api/forms/records/{recordId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(detail, "document-row-record-detail-es", Ct);
        var document = (await detail.ReadJsonAsync()).GetProperty("origin").GetProperty("document");
        Assert.Equal((over.DocumentId, "cobranzas.xlsx", 5, JsonValueKind.Null), (document.GetProperty("id").GetGuid(), document.GetProperty("name").GetString(),
            document.GetProperty("row").GetProperty("number").GetInt32(), document.GetProperty("row").GetProperty("sheet").ValueKind));
        Assert.Equal(JsonValueKind.String, document.GetProperty("row").GetProperty("receivedAtUtc").ValueKind);
    }

    [Fact]
    public async Task The_copy_of_a_spreadsheet_stays_while_any_of_its_rows_is_in_the_inbox()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var content = FormReceivedDocumentRowScenario.Workbook(("150002", "1"), ("150002", "1"), ("nota", ""));
        var (reply, request) = await scenario.SendRowsAsync(content, FormReceivedDocumentRowScenario.Proposal(scenario.Form, scenario.Sender, [2, 3], [4], [2, 3, 4]),
            Ct, factory.Clock.GetUtcNow().UtcDateTime.AddHours(1));
        var key = FormReceivedDocumentCopyPolicy.Key(reply.Rows[0].DocumentId, FormReceivedDocumentRowScenario.MediaType);
        var past = factory.Clock.GetUtcNow().UtcDateTime.AddDays(-1);

        // The repeated and the unclassified rows are purged; the load still keeps the copy.
        await scenario.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"FormReceivedDocuments\" SET \"DeleteAfterUtc\" = {past} WHERE \"SourceId\" = {request.SourceId} AND \"DeleteAfterUtc\" IS NOT NULL", Ct));
        Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IFormReceivedDocumentRetentionService>().PurgeExpiredAsync(Ct))).IsSuccess);
        Assert.Equal([reply.Rows[0].DocumentId], await scenario.DbAsync(db => db.Set<FormReceivedDocument>().AsNoTracking()
            .Where(document => document.SourceId == request.SourceId).Select(document => document.Id).ToListAsync(Ct)));
        Assert.Equal([key], await ReferencedAsync(scenario, key));

        // When its person is no longer waited for, the team discards it and, once purged, nothing keeps the copy.
        await scenario.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"FormReceivedDocuments\" SET \"ReplyExpiresAtUtc\" = {past} WHERE \"SourceId\" = {request.SourceId}", Ct));
        using var reviewer = scenario.Client(scenario.Reviewer);
        using var discarded = await reviewer.PostAsJsonAsync($"/api/forms/documents/{reply.Rows[0].DocumentId}/discard", new { expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, discarded.StatusCode);
        await scenario.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"FormReceivedDocuments\" SET \"DeleteAfterUtc\" = {past} WHERE \"SourceId\" = {request.SourceId}", Ct));
        Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IFormReceivedDocumentRetentionService>().PurgeExpiredAsync(Ct))).IsSuccess);
        Assert.Empty(await ReferencedAsync(scenario, key));
    }

    [Fact]
    public async Task A_row_changes_form_mapped_again_from_what_it_kept_without_reading_its_spreadsheet()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var content = FormReceivedDocumentRowScenario.Workbook(("150003", "778899"), ("nota", "12"));
        var (reply, _) = await scenario.SendRowsAsync(content, FormReceivedDocumentRowScenario.Proposal(scenario.Form, scenario.Sender, [2], [3], [2, 3]),
            Ct, factory.Clock.GetUtcNow().UtcDateTime.AddHours(1));
        var unmatched = reply.Rows[1];
        Assert.Equal(StructuredDocumentRowKind.Unmatched, unmatched.Kind);
        using var reviewer = scenario.Client(scenario.Reviewer);
        var path = $"/api/forms/documents/{unmatched.DocumentId}";
        var requestId = Guid.CreateVersion7();

        using var accepted = await reviewer.PutAsJsonAsync(path + "/form", new { requestId, definitionId = scenario.Other.DefinitionId, expectedRevision = 1 }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        // The engine maps the row again with propose-rows, on a sample of its own row with the headers of its sheet.
        var operation = Assert.Single(scenario.Engine.Submitted);
        Assert.Equal([BusinessFormDocumentAssistantCapabilitySource.FormId, BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId],
            operation.Context.AllowedCapabilities.Select(capability => capability.Id));
        Assert.Equal(AssistantAttachmentKind.Spreadsheet, operation.Attachment!.Kind);
        var sample = operation.Attachment.Spreadsheet!;
        Assert.Equal([1, 3], sample.Rows.Select(row => row.Number));
        Assert.Equal(1, sample.DataRowCount);
        Assert.Equal(("nota", "12"), (sample.Rows[1].Cells["A"], sample.Rows[1].Cells["B"]));
        var mapped = FormReceivedDocumentRowScenario.Proposal(scenario.Other, scenario.Reviewer, [3], [], [3], formRead: true);
        scenario.Engine.Complete(scenario.Reviewer, new(requestId, "completed", mapped, null, null) { Proposals = [mapped] });
        using var completed = await reviewer.GetAsync($"{path}/extraction?requestId={requestId}", Ct);

        Assert.Equal("completed", (await completed.ReadJsonAsync()).GetProperty("state").GetString());
        var changed = await (await reviewer.GetAsync(path, Ct)).ReadJsonAsync();
        Assert.Equal((scenario.Other.DefinitionId, "proposed", true), (changed.GetProperty("document").GetProperty("form").GetProperty("definitionId").GetGuid(),
            changed.GetProperty("document").GetProperty("state").GetString(), changed.GetProperty("document").GetProperty("forReview").GetBoolean()));
        var values = Assert.Single(changed.GetProperty("proposal").GetProperty("candidates").EnumerateArray()).GetProperty("values").EnumerateArray()
            .ToDictionary(value => value.GetProperty("fieldId").GetGuid(), value => value.GetProperty("value").GetString());
        Assert.Equal(("12", null), (values[scenario.Other.OperationId], values[scenario.Other.AmountId]));
        Assert.Equal(2, changed.GetProperty("rowCells").GetArrayLength());
    }

    [Fact]
    public async Task A_row_without_text_is_no_entry_and_only_the_loads_its_person_can_receive_count_for_the_three()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var arrived = factory.Clock.GetUtcNow().UtcDateTime;
        // The other form is only in the app: its person cannot receive it (CE4-A).
        await scenario.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"BusinessFormVersions\" SET \"AppOnly\" = TRUE WHERE \"Id\" = {scenario.Other.VersionId}", Ct));
        var sheet = FormReceivedDocumentRowScenario.Sheet;
        var content = FormReceivedDocumentRowScenario.Book((sheet, [["Monto", "Operación"], ["71001", "1"], ["71002", "2"], ["71003", "3"], ["71004", "4"],
            [XLError.NoValueAvailable, XLError.CellReference], [98765.25, "sin formulario"]]));
        var proposal = FormReceivedDocumentRowScenario.Proposal([(scenario.Other, sheet, [2, 3, 4]), (scenario.Form, sheet, [5])], scenario.Sender,
            [(sheet, [2, 3, 4, 5])]);

        var (reply, request) = await scenario.SendRowsAsync(content, proposal, Ct, arrived.AddHours(1));

        // The three it cannot receive do not take the three loads, the row of errors is no entry and the one the model did not see stays unclassified.
        Assert.Equal([(2, StructuredDocumentRowKind.NotSendable, (int?)null), (3, StructuredDocumentRowKind.NotSendable, null),
            (4, StructuredDocumentRowKind.NotSendable, null), (5, StructuredDocumentRowKind.Load, 1), (7, StructuredDocumentRowKind.Unseen, null)],
            reply.Rows.Select(row => (row.Number, row.Kind, row.Position)));
        Assert.Equal(1, reply.Loads);
        var stored = await scenario.DbAsync(db => db.Set<FormReceivedDocument>().AsNoTracking().Where(document => document.SourceId == request.SourceId)
            .OrderBy(document => document.RowOrder).ToListAsync(Ct));
        Assert.Equal([(2, 1), (3, 2), (4, 3), (5, 4), (7, 5)], stored.Select(document => (document.RowNumber!.Value, document.RowOrder!.Value)));
        Assert.Equal([FormReceivedDocumentNotSentReason.NotSendable, FormReceivedDocumentNotSentReason.NotSendable, FormReceivedDocumentNotSentReason.NotSendable,
            null, null], stored.Select(document => document.NotSentReason));
        Assert.Equal((FormReceivedDocumentState.Unclassified, "unseen"), (stored[4].State, stored[4].ReasonCode));
        // A repeated registration answers each row as it was registered, the one the model did not see too.
        var replayed = await scenario.RegisterAsync(request, Ct);
        Assert.Equal(reply.Rows.Select(row => (row.DocumentId, row.Kind, row.Position)), replayed.Rows.Select(row => (row.DocumentId, row.Kind, row.Position)));

        // The row without a form is found by the number of its cell (decision 16) and «La fila» shows its cells.
        using var reviewer = scenario.Client(scenario.Reviewer);
        using var searched = await reviewer.PostAsJsonAsync(SearchPath, new { receivedFromUtc = arrived, receivedToUtc = arrived.AddMinutes(1), pageSize = 100, search = "98765" }, Ct);
        Assert.Equal(reply.Rows[4].DocumentId, Assert.Single((await searched.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var detail = await (await reviewer.GetAsync($"/api/forms/documents/{reply.Rows[4].DocumentId}", Ct)).ReadJsonAsync();
        Assert.Equal([("Monto", "98765.25"), ("Operación", "sin formulario")], detail.GetProperty("rowCells").EnumerateArray()
            .Select(cell => (cell.GetProperty("header").GetString(), cell.GetProperty("value").GetString())));
        Assert.Equal([("received", "unseen")], detail.GetProperty("history").EnumerateArray()
            .Select(step => (step.GetProperty("kind").GetString(), step.GetProperty("detail").GetString())));
    }

    // A spreadsheet nobody recognized becomes its rows when its person chooses a form from the list: with the proposal of the interpretation that
    // continued with that form fixed, its single entry is the first row and keeps the copy, the others enter with the rules of a first
    // registration and its arrival (not the tap's), and the loads wait for their person until the new deadline; a choice that fits no row leaves
    // it as it was, and recognizing
    // it again (a retry) answers what was stored.
    [Fact]
    public async Task A_spreadsheet_nobody_recognized_becomes_its_rows_for_the_form_its_person_chose()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var arrived = factory.Clock.GetUtcNow().UtcDateTime;
        var content = FormReceivedDocumentRowScenario.Workbook(("74001", "1"), ("74002", "2"), ("nota", ""));
        var (unknown, request) = await scenario.SendRowsAsync(content,
            FormReceivedDocumentRowScenario.Proposal(scenario.Form, scenario.Sender, [], [2, 3, 4], [2, 3, 4]), Ct, arrived.AddHours(1));
        Assert.Equal(StructuredDocumentReplyKind.Unknown, unknown.Kind);
        var single = unknown.DocumentId!.Value;
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var deadline = factory.Clock.GetUtcNow().UtcDateTime.AddHours(1);

        var unchanged = await scenario.RecognizeAsync(request with
        {
            FocusDefinitionId = scenario.Other.DefinitionId, ReplyExpiresAtUtc = deadline,
            Proposal = FormReceivedDocumentRowScenario.Proposal(scenario.Other, scenario.Sender, [], [2, 3, 4], [2, 3, 4]),
        }, Ct);
        var chosen = request with
        {
            FocusDefinitionId = scenario.Form.DefinitionId, ReplyExpiresAtUtc = deadline, ReceivedAtUtc = factory.Clock.GetUtcNow().UtcDateTime,
            Proposal = FormReceivedDocumentRowScenario.Proposal(scenario.Form, scenario.Sender, [2, 3], [4], [2, 3, 4]),
        };
        var reply = await scenario.RecognizeAsync(chosen, Ct);

        Assert.Equal((StructuredDocumentReplyKind.Unknown, (Guid?)single, true), (unchanged.Kind, unchanged.DocumentId, unchanged.WasAlreadyRegistered));
        Assert.Equal((StructuredDocumentReplyKind.Sure, single, 2), (reply.Kind, reply.Rows[0].DocumentId, reply.Loads));
        Assert.Equal([(2, StructuredDocumentRowKind.Load, (int?)1), (3, StructuredDocumentRowKind.Load, 2), (4, StructuredDocumentRowKind.Unmatched, null)],
            reply.Rows.Select(row => (row.Number, row.Kind, row.Position)));
        Assert.Equal((scenario.Form.DefinitionId, deadline), (reply.Rows[0].Form!.DefinitionId, reply.ReplyExpiresAtUtc));
        var stored = await scenario.DbAsync(db => db.Set<FormReceivedDocument>().AsNoTracking().Where(document => document.SourceId == request.SourceId)
            .OrderBy(document => document.RowOrder).ToListAsync(Ct));
        Assert.Equal([(single, 2), (reply.Rows[1].DocumentId, 3), (reply.Rows[2].DocumentId, 4)], stored.Select(document => (document.Id, document.RowNumber!.Value)));
        Assert.Single(stored.Select(document => document.ReceivedAtUtc).Distinct());
        Assert.Equal(FormReceivedDocumentCopyPolicy.Key(single, FormReceivedDocumentRowScenario.MediaType), Assert.Single(stored.Select(document => document.StorageKey).Distinct()));
        Assert.Equal([(FormReceivedDocumentState.Proposed, (DateTime?)deadline), (FormReceivedDocumentState.Proposed, deadline), (FormReceivedDocumentState.Unclassified, null)],
            stored.Select(document => (document.State, document.ReplyExpiresAtUtc)));
        using var reviewer = scenario.Client(scenario.Reviewer);
        var detail = await (await reviewer.GetAsync($"/api/forms/documents/{single}", Ct)).ReadJsonAsync();
        Assert.Equal([("received", "unknown"), ("formChosen", "sure"), ("sent", "1/2")], detail.GetProperty("history").EnumerateArray()
            .Select(step => (step.GetProperty("kind").GetString(), step.GetProperty("detail").GetString())));

        var again = await scenario.RecognizeAsync(chosen, Ct);
        Assert.True(again.WasAlreadyRegistered);
        Assert.Equal(reply.Rows.Select(row => (row.DocumentId, row.Kind, row.Position)), again.Rows.Select(row => (row.DocumentId, row.Kind, row.Position)));
    }

    // Rule B4 (review of task 29b): the same spreadsheet sent twice before choosing a form is loaded once. Choosing the form for the second one
    // after the first became its rows discards it as a repeated document with when the first arrived, as at a first registration, and choosing
    // again answers the same.
    [Fact]
    public async Task The_same_spreadsheet_sent_twice_before_choosing_a_form_is_loaded_once()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var content = FormReceivedDocumentRowScenario.Workbook(("74501", "1"), ("74502", "2"), ("nota", ""));
        var none = FormReceivedDocumentRowScenario.Proposal(scenario.Form, scenario.Sender, [], [2, 3, 4], [2, 3, 4]);
        var (firstUnknown, first) = await scenario.SendRowsAsync(content, none, Ct, factory.Clock.GetUtcNow().UtcDateTime.AddHours(1));
        var (secondUnknown, second) = await scenario.SendRowsAsync(content, none, Ct, factory.Clock.GetUtcNow().UtcDateTime.AddHours(1));
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var choice = FormReceivedDocumentRowScenario.Proposal(scenario.Form, scenario.Sender, [2, 3], [4], [2, 3, 4]);

        var loaded = await scenario.RecognizeAsync(first with { FocusDefinitionId = scenario.Form.DefinitionId, Proposal = choice }, Ct);
        var chosen = second with { FocusDefinitionId = scenario.Form.DefinitionId, Proposal = choice };
        var repeated = await scenario.RecognizeAsync(chosen, Ct);

        Assert.Equal((StructuredDocumentReplyKind.Unknown, StructuredDocumentReplyKind.Unknown, StructuredDocumentReplyKind.Sure),
            (firstUnknown.Kind, secondUnknown.Kind, loaded.Kind));
        var rows = await scenario.DbAsync(db => db.Set<FormReceivedDocument>().AsNoTracking().Where(document => document.SourceId == first.SourceId).ToListAsync(Ct));
        Assert.Equal((StructuredDocumentReplyKind.Duplicate, secondUnknown.DocumentId, false, (DateTime?)rows[0].ReceivedAtUtc),
            (repeated.Kind, repeated.DocumentId, repeated.WasAlreadyRegistered, repeated.Duplicate?.AtUtc));
        var entry = await scenario.DbAsync(db => db.Set<FormReceivedDocument>().AsNoTracking().SingleAsync(document => document.SourceId == second.SourceId, Ct));
        Assert.Equal((FormReceivedDocumentState.Discarded, "duplicate", (int?)null), (entry.State, entry.ReasonCode, entry.RowNumber));
        using var reviewer = scenario.Client(scenario.Reviewer);
        var detail = await (await reviewer.GetAsync($"/api/forms/documents/{entry.Id}", Ct)).ReadJsonAsync();
        Assert.Equal([("received", "unknown"), ("formChosen", "duplicate")], detail.GetProperty("history").EnumerateArray()
            .Select(step => (step.GetProperty("kind").GetString(), step.GetProperty("detail").GetString())));
        var again = await scenario.RecognizeAsync(chosen, Ct);
        Assert.Equal((StructuredDocumentReplyKind.Duplicate, true), (again.Kind, again.WasAlreadyRegistered));
    }

    [Fact]
    public async Task A_book_of_several_sheets_answers_in_its_order_and_a_repeated_registration_keeps_what_became_of_each_row()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var content = FormReceivedDocumentRowScenario.Book(("Zeta", [["Monto", "Operación"], ["72001", "1"], ["72002", "2"], ["72003", "3"]]),
            ("Alfa", [["Monto", "Operación"], ["72004", "4"]]));
        var proposal = FormReceivedDocumentRowScenario.Proposal([(scenario.Form, "Zeta", [2, 3, 4]), (scenario.Form, "Alfa", [2])], scenario.Sender,
            [("Zeta", [2, 3, 4]), ("Alfa", [2])]);

        var (reply, request) = await scenario.SendRowsAsync(content, proposal, Ct, factory.Clock.GetUtcNow().UtcDateTime.AddHours(1));

        Assert.Equal([("Zeta", 2, StructuredDocumentRowKind.Load, (int?)1), ("Zeta", 3, StructuredDocumentRowKind.Load, 2), ("Zeta", 4, StructuredDocumentRowKind.Load, 3),
            ("Alfa", 2, StructuredDocumentRowKind.OverLimit, null)], reply.Rows.Select(row => (row.Sheet, row.Number, row.Kind, row.Position)));
        // The team passes the one over the limit to the form; a repeated registration still answers it as it was registered, in the order of the book.
        using var reviewer = scenario.Client(scenario.Reviewer);
        using var passed = await reviewer.PostAsJsonAsync($"/api/forms/documents/{reply.Rows[3].DocumentId}/records", new
        {
            requestId = Guid.NewGuid(), versionId = scenario.Form.VersionId, expectedRevision = 1,
            answers = new object[] { new { fieldId = scenario.Form.AmountId, value = "72004" }, new { fieldId = scenario.Form.OperationId, value = "4" } },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, passed.StatusCode);
        var replayed = await scenario.RegisterAsync(request, Ct);
        Assert.Equal(reply.Rows.Select(row => (row.DocumentId, row.Sheet, row.Kind, row.Position)),
            replayed.Rows.Select(row => (row.DocumentId, row.Sheet, row.Kind, row.Position)));
    }

    [Fact]
    public async Task Without_rows_a_spreadsheet_is_one_entry_and_only_rows_not_discarded_make_the_same_one_repeated()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var content = FormReceivedDocumentRowScenario.Workbook(("73001", "1"), ("73002", "2"));
        var authentic = FormReceivedDocumentRowScenario.Proposal(scenario.Form, scenario.Sender, [2, 3], [], [2, 3]);
        // Over the day's quota; a mapping without its read; one from the read of someone else (decision 8); one where no row fits.
        var singles = new List<(StructuredDocumentRowsRegistration Reply, StructuredDocumentRowsRegistrationRequest Request)>
        {
            await scenario.SendRowsAsync(content, null, Ct),
            await scenario.SendRowsAsync(content, authentic with { ObservedReads = [] }, Ct),
            await scenario.SendRowsAsync(content, FormReceivedDocumentRowScenario.Proposal(scenario.Form, scenario.Reviewer, [2, 3], [], [2, 3]), Ct),
            await scenario.SendRowsAsync(content, FormReceivedDocumentRowScenario.Proposal(scenario.Form, scenario.Sender, [], [2, 3], [2, 3]), Ct),
        };
        // And a sample that is not this copy.
        var other = singles[^1].Request with { SourceId = "wamid." + Guid.NewGuid().ToString("N"), Proposal = authentic, SampleSha256 = new string('0', 64) };
        singles.Add((await scenario.RegisterAsync(other, Ct), other));

        Assert.Equal([StructuredDocumentReplyKind.Quota, StructuredDocumentReplyKind.Unknown, StructuredDocumentReplyKind.Unknown, StructuredDocumentReplyKind.Unknown,
            StructuredDocumentReplyKind.Unknown], singles.Select(single => single.Reply.Kind));
        Assert.All(singles.Skip(1), single => Assert.NotEmpty(single.Reply.Forms));
        var entries = await scenario.DbAsync(db => db.Set<FormReceivedDocument>().AsNoTracking().Where(document => document.Sha256 == other.Copy.Sha256).ToListAsync(Ct));
        Assert.Equal(5, entries.Count);
        Assert.All(entries, entry => Assert.Equal((FormReceivedDocumentState.Unclassified, (int?)null), (entry.State, entry.RowNumber)));
        Assert.Equal(["quota", "unknown", "unknown", "unknown", "unknown"], entries.Select(entry => entry.ReasonCode).Order(StringComparer.Ordinal));

        // None of them makes the spreadsheet repeated: its rows enter, and then the same spreadsheet is repeated from when they arrived.
        var (rows, rowsRequest) = await scenario.SendRowsAsync(content, authentic, Ct);
        Assert.Equal((StructuredDocumentReplyKind.Sure, 2), (rows.Kind, rows.Rows.Count));
        var (again, _) = await scenario.SendRowsAsync(content, authentic, Ct);
        Assert.Equal((StructuredDocumentReplyKind.Duplicate, rowsRequest.ReceivedAtUtc), (again.Kind, again.Duplicate!.AtUtc));
        // A document of the same channel source is not registered beside its rows.
        var document = await scenario.SendAsync(Ct, sourceId: rowsRequest.SourceId);
        Assert.Equal(StructuredDocumentReplyKind.Unavailable, document.Reply.Kind);
        Assert.Equal(2, await scenario.DbAsync(db => db.Set<FormReceivedDocument>().CountAsync(entry => entry.SourceId == rowsRequest.SourceId, Ct)));
        // Once its rows are discarded, the same spreadsheet enters again.
        var past = factory.Clock.GetUtcNow().UtcDateTime.AddDays(1);
        await scenario.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"FormReceivedDocuments\" SET \"State\" = 'Discarded', \"ReasonCode\" = 'reviewer', \"DeleteAfterUtc\" = {past} WHERE \"SourceId\" = {rowsRequest.SourceId}", Ct));
        Assert.Equal(StructuredDocumentReplyKind.Sure, (await scenario.SendRowsAsync(content, authentic, Ct)).Reply.Kind);
    }

    [Fact]
    public async Task A_row_with_the_data_of_a_response_is_repeated_with_the_questions_that_matched()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var transfer = await scenario.TransferAsync(Ct);
        var number = await scenario.RespondAsync(transfer, "556677", 1500m, new DateOnly(2026, 11, 1), Ct);
        const string sheet = "Transferencias";
        var content = FormReceivedDocumentRowScenario.Book((sheet, [["Operación", "Monto", "Fecha"], ["556677", "1500", "01/11/2026"], ["556678", "1600", "02/11/2026"]]));
        var read = new FormDocumentReadPayload("complete", scenario.Sender, "es", true, [new FormDocumentCandidate(transfer.DefinitionId, transfer.VersionId,
            transfer.Revision, transfer.Name, "Un comprobante de transferencia.", [new(transfer.OperationId, "shortText", "Número de operación", false, 40, [], null),
                new(transfer.AmountId, "decimal", "Monto", true, null, [], null), new(transfer.DateId, "date", "Fecha", false, null, [], null)])]);
        var mapping = new FormDocumentRowsProposal("proposed", 1, [new(transfer.DefinitionId, transfer.VersionId, transfer.Revision, sheet, [2, 3],
            [new(transfer.OperationId, "shortText", "A"), new(transfer.AmountId, "decimal", "B"), new(transfer.DateId, "date", "C")], [])], [], [new(sheet, [2, 3])],
            "planilla de transferencias");
        var turn = Guid.CreateVersion7();
        var proposal = new AssistantObservedProposal(turn, Guid.CreateVersion7(), new(BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId,
            BusinessFormDocumentAssistantCapabilitySource.Version), mapping)
        {
            ObservedReads = [new(turn, 1, new(BusinessFormDocumentAssistantCapabilitySource.CandidatesId, BusinessFormDocumentAssistantCapabilitySource.Version), read)],
        };

        var (reply, _) = await scenario.SendRowsAsync(content, proposal, Ct, factory.Clock.GetUtcNow().UtcDateTime.AddHours(1));

        Assert.Equal([(StructuredDocumentRowKind.Repeated, (int?)null), (StructuredDocumentRowKind.Load, 1)], reply.Rows.Select(row => (row.Kind, row.Position)));
        Assert.Equal((number, (StructuredDocumentRowReference?)null), (reply.Rows[0].Duplicate!.RecordNumber, reply.Rows[0].SameAs));
        using var reviewer = scenario.Client(scenario.Reviewer);
        using var detail = await reviewer.GetAsync($"/api/forms/documents/{reply.Rows[0].DocumentId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(detail, "document-row-repeated-record-es", Ct);
        var repetition = (await detail.ReadJsonAsync()).GetProperty("proposal").GetProperty("repetition");
        Assert.Equal(number, repetition.GetProperty("recordNumber").GetString());
        Assert.Equal(["Número de operación", "Monto", "Fecha"], repetition.GetProperty("labels").EnumerateArray().Select(label => label.GetString()));
        Assert.False(repetition.TryGetProperty("row", out _));
    }

    [Fact]
    public async Task A_row_changes_form_only_with_the_read_of_the_form_fixed_for_it()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var content = FormReceivedDocumentRowScenario.Workbook(("74001", "1"), ("nota", "12"));
        var (reply, _) = await scenario.SendRowsAsync(content, FormReceivedDocumentRowScenario.Proposal(scenario.Form, scenario.Sender, [2], [3], [2, 3]), Ct,
            factory.Clock.GetUtcNow().UtcDateTime.AddHours(1));
        using var reviewer = scenario.Client(scenario.Reviewer);
        var path = $"/api/forms/documents/{reply.Rows[1].DocumentId}";
        var requestId = Guid.CreateVersion7();
        using var accepted = await reviewer.PutAsJsonAsync(path + "/form", new { requestId, definitionId = scenario.Other.DefinitionId, expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

        // The engine closed on a mapping from a candidates read, not from the read of the form fixed for the reviewer (decision 8).
        var mapped = FormReceivedDocumentRowScenario.Proposal(scenario.Other, scenario.Reviewer, [3], [], [3]);
        scenario.Engine.Complete(scenario.Reviewer, new(requestId, "completed", mapped, null, null) { Proposals = [mapped] });
        using var completed = await reviewer.GetAsync($"{path}/extraction?requestId={requestId}", Ct);

        Assert.Equal("failed", (await completed.ReadJsonAsync()).GetProperty("state").GetString());
        Assert.Equal("unclassified", (await (await reviewer.GetAsync(path, Ct)).ReadJsonAsync()).GetProperty("document").GetProperty("state").GetString());
    }

    private static Task<IReadOnlyList<string>> ReferencedAsync(FormReceivedDocumentScenario scenario, string key) =>
        scenario.ScopeAsync(services => services.GetServices<IPrivateFileRetentionSource>().OfType<FormReceivedDocumentRetentionSource>().Single()
            .ListReferencedKeysAsync([key], Ct));
}
