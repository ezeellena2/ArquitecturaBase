using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// Task 7 with PostgreSQL: the documents a channel registers through the neutral port (idempotent by source, with the inbox's own copy and
// what formularios decided), the inbox «Documentos recibidos» with its permissions, filters, search and counts, the detail and its file,
// discarding and reopening with the revision seen, changing the form with a scripted engine, passing it to the form as the response of
// whoever sent it, and the purge. What counts all the forms or needs the people's data uses a database of its own. No provider is called.
[Collection(ApiTestGroup.Name)]
public sealed class FormReceivedDocumentHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Registering_a_document_copies_it_once_and_the_same_source_answers_the_same()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);

        var sent = await scenario.SendAsync(Ct);
        var again = await scenario.PortAsync(port => port.RegisterAsync(sent.Request, Ct));
        var stranger = await scenario.PortAsync(port => port.RegisterAsync(sent.Request with { SenderUserId = scenario.Reviewer }, Ct));
        var invalid = await scenario.PortAsync(port => port.RegisterAsync(sent.Request with { SourceId = "" }, Ct));
        // Only a PDF, a photo (JPEG or PNG) or a Word is a document of the inbox.
        var text = await scenario.PortAsync(port => port.RegisterAsync(sent.Request with
        {
            SourceId = "wamid.text-" + Guid.NewGuid().ToString("N"), Copy = sent.Request.Copy with { MediaType = "text/plain" },
        }, Ct));

        Assert.Equal(StructuredDocumentReplyKind.Sure, sent.Reply.Kind);
        Assert.False(sent.Reply.WasAlreadyRegistered);
        var form = Assert.Single(sent.Reply.Forms);
        Assert.Equal((scenario.Form.DefinitionId, scenario.Form.VersionId, scenario.Form.Name), (form.DefinitionId, form.VersionId, form.Name));
        Assert.Equal((sent.Id, StructuredDocumentReplyKind.Sure, true), (again.DocumentId!.Value, again.Kind, again.WasAlreadyRegistered));
        Assert.Equal(scenario.Form.DefinitionId, Assert.Single(again.Forms).DefinitionId);
        // Another person's source is not theirs to hear about, and a request the port cannot take is unavailable.
        Assert.Null(stranger.DocumentId);
        Assert.Equal(StructuredDocumentReplyKind.Unavailable, stranger.Kind);
        Assert.Equal(StructuredDocumentReplyKind.Unavailable, invalid.Kind);
        Assert.Equal((null, StructuredDocumentReplyKind.Unavailable), (text.DocumentId, text.Kind));
        await using (var copy = await scenario.Store.OpenReadAsync(FormReceivedDocumentCopyPolicy.Key(sent.Id, "application/pdf"), Ct))
        {
            Assert.NotNull(copy);
            using var bytes = new MemoryStream();
            await copy.CopyToAsync(bytes, Ct);
            Assert.Equal(sent.Content, bytes.ToArray());
        }
        var stored = await factory.ExecuteDbContextAsync(db => db.Set<FormReceivedDocument>().AsNoTracking()
            .Where(document => document.ChannelId == FormReceivedDocumentScenario.Channel && document.SourceId == sent.Request.SourceId).ToListAsync(Ct));
        var document = Assert.Single(stored);
        Assert.Equal((sent.Id, FormReceivedDocumentState.Proposed, scenario.Form.DefinitionId, scenario.Form.VersionId, scenario.Sender),
            (document.Id, document.State, document.DefinitionId!.Value, document.VersionId!.Value, document.SenderUserId));
        Assert.Equal((FormReceivedDocumentCopyPolicy.Key(sent.Id, "application/pdf"), FormReceivedDocumentScenario.Sha256(sent.Content), 1L),
            (document.StorageKey, document.Sha256, document.Revision));
        Assert.NotNull(document.ProtectedReading);
        Assert.DoesNotContain("778899", System.Text.Encoding.UTF8.GetString(document.ProtectedReading!), StringComparison.Ordinal);
        // Only the typed amount is kept to search by: the operation of a form that is not a standard's copy is free text.
        Assert.Equal("150000", document.SearchText);
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<FormReceivedDocumentEvent>().CountAsync(item => item.DocumentId == sent.Id, Ct)));
    }

    [Fact]
    public async Task The_reading_decides_the_state_the_reason_and_what_the_channel_says()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);

        var quota = await scenario.SendAsync(Ct, notRead: StructuredDocumentNotRead.Quota);
        var rejected = await scenario.SendAsync(Ct, notRead: StructuredDocumentNotRead.Rejected);
        var unknown = await scenario.SendAsync(Ct, outcome: "unknown");
        var unreadable = await scenario.SendAsync(Ct, amount: null, readability: StructuredDocumentReadability.Partial);
        var first = await scenario.SendAsync(Ct);
        var repeated = await scenario.SendAsync(Ct, content: first.Content);
        var doubt = await scenario.SendAsync(Ct, outcome: "doubt", candidates: [scenario.Form, scenario.Other]);

        Assert.Equal(StructuredDocumentReplyKind.Quota, quota.Reply.Kind);
        Assert.Equal(StructuredDocumentReplyKind.Rejected, rejected.Reply.Kind);
        Assert.Equal(StructuredDocumentReplyKind.Unknown, unknown.Reply.Kind);
        // «No lo reconoce» lists forms with reading on the person can complete, never one without reading.
        Assert.InRange(unknown.Reply.Forms.Count, 1, FormReceivedDocumentReplyPolicy.MaxForms);
        Assert.DoesNotContain(unknown.Reply.Forms, form => form.DefinitionId == scenario.Other.DefinitionId);
        Assert.Equal((StructuredDocumentReplyKind.Unreadable, "Monto", StructuredDocumentProblem.Blurry),
            (unreadable.Reply.Kind, unreadable.Reply.MissingDatum, unreadable.Reply.Problem));
        Assert.Equal(StructuredDocumentReplyKind.Duplicate, repeated.Reply.Kind);
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime, repeated.Reply.Duplicate!.AtUtc);
        Assert.Null(repeated.Reply.Duplicate.RecordNumber);
        Assert.Equal(StructuredDocumentReplyKind.Doubt, doubt.Reply.Kind);
        Assert.Equal([scenario.Form.DefinitionId, scenario.Other.DefinitionId], doubt.Reply.Forms.Select(form => form.DefinitionId));
        var stored = await factory.ExecuteDbContextAsync(db => db.Set<FormReceivedDocument>().AsNoTracking()
            .Where(document => document.SenderUserId == scenario.Sender).ToDictionaryAsync(document => document.Id, Ct));
        var deleteAfter = factory.Clock.GetUtcNow().UtcDateTime.AddDays(FormReceivedDocument.RetentionDays);
        Assert.Equal((FormReceivedDocumentState.Unclassified, "quota", deleteAfter), (stored[quota.Id].State, stored[quota.Id].ReasonCode, stored[quota.Id].DeleteAfterUtc!.Value));
        Assert.Null(stored[quota.Id].ProtectedReading);
        Assert.Equal((FormReceivedDocumentState.Discarded, "protected"), (stored[rejected.Id].State, stored[rejected.Id].ReasonCode));
        Assert.Equal((FormReceivedDocumentState.Unclassified, "unknown"), (stored[unknown.Id].State, stored[unknown.Id].ReasonCode));
        Assert.Null(stored[unknown.Id].DefinitionId);
        Assert.Equal((FormReceivedDocumentState.Discarded, "unreadable"), (stored[unreadable.Id].State, stored[unreadable.Id].ReasonCode));
        Assert.Equal((FormReceivedDocumentState.Discarded, "duplicate"), (stored[repeated.Id].State, stored[repeated.Id].ReasonCode));
        Assert.Equal((FormReceivedDocumentState.Proposed, scenario.Form.DefinitionId), (stored[doubt.Id].State, stored[doubt.Id].DefinitionId!.Value));
        // A repeated registration answers what was decided, from what the document kept.
        var replayed = await scenario.PortAsync(port => port.RegisterAsync(repeated.Request, Ct));
        Assert.Equal((repeated.Id, StructuredDocumentReplyKind.Duplicate, true), (replayed.DocumentId!.Value, replayed.Kind, replayed.WasAlreadyRegistered));
    }

    [Fact]
    public async Task A_chosen_form_is_prepared_to_send_and_another_one_notes_the_missing_form_once()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var sent = await scenario.SendAsync(Ct);
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var send = new StructuredDocumentSendRequest(sent.Id, FormReceivedDocumentScenario.Channel, sent.Request.SourceId, scenario.Sender, "es", scenario.Form.DefinitionId);

        var ready = await scenario.PortAsync(port => port.PrepareSendAsync(send, Ct));
        var stranger = await scenario.PortAsync(port => port.PrepareSendAsync(send with { RecipientUserId = scenario.Reviewer }, Ct));
        var other = await scenario.PortAsync(port => port.PrepareSendAsync(send with { DefinitionId = scenario.Other.DefinitionId }, Ct));

        Assert.Equal(StructuredDocumentSendState.Ready, ready.State);
        Assert.Equal((scenario.Form.DefinitionId, scenario.Form.VersionId), (ready.Prefill!.Form.DefinitionId, ready.Prefill.Form.VersionId));
        Assert.Equal([(scenario.Form.AmountId, "150000", "150.000"), (scenario.Form.OperationId, "778899", "778899")],
            ready.Prefill.Fields.Select(field => (field.FieldId, field.Value, field.DisplayValue)));
        var mark = Assert.Single(ready.Marks);
        Assert.Equal((scenario.Form.OperationId, StructuredDocumentMarkKind.Doubtful), (mark.FieldId, mark.Kind));
        Assert.Equal(scenario.Form.ReceiptId, ready.DocumentFieldId);
        Assert.Equal(StructuredDocumentSendState.Unavailable, stranger.State);
        Assert.Equal(StructuredDocumentSendState.Unavailable, other.State);

        var choice = new StructuredDocumentChoiceRequest(sent.Id, FormReceivedDocumentScenario.Channel, sent.Request.SourceId, scenario.Sender, "es", now);
        var chosen = await scenario.PortAsync(port => port.ChooseOtherAsync(choice, Ct));
        var chosenAgain = await scenario.PortAsync(port => port.ChooseOtherAsync(choice, Ct));
        var notTheirs = await scenario.PortAsync(port => port.ChooseOtherAsync(choice with { SenderUserId = scenario.Reviewer }, Ct));

        Assert.Equal(StructuredDocumentChoiceState.Recorded, chosen);
        Assert.Equal(StructuredDocumentChoiceState.AlreadyRecorded, chosenAgain);
        Assert.Equal(StructuredDocumentChoiceState.Unavailable, notTheirs);
        var document = await factory.ExecuteDbContextAsync(db => db.Set<FormReceivedDocument>().AsNoTracking().SingleAsync(value => value.Id == sent.Id, Ct));
        Assert.Equal((FormReceivedDocumentState.Unclassified, FormReceivedDocument.ChoseOther), (document.State, document.ReasonCode));
        var request = Assert.Single(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormMissingRequest>().AsNoTracking()
            .Where(value => value.SourceReceiptId == sent.Id).ToListAsync(Ct)));
        Assert.Equal((scenario.Sender, now), (request.SubjectUserId, request.RequestedAtUtc));
        var topic = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormMissingTopic>().AsNoTracking().SingleAsync(value => value.Id == request.TopicId, Ct));
        Assert.Equal("comprobante de pago", topic.Name);
        Assert.Equal(StructuredDocumentSendState.Unavailable, (await scenario.PortAsync(port => port.PrepareSendAsync(send, Ct))).State);
        var replayed = await scenario.PortAsync(port => port.RegisterAsync(sent.Request, Ct));
        Assert.Equal((StructuredDocumentReplyKind.Unknown, true), (replayed.Kind, replayed.WasAlreadyRegistered));

        // Discarded and reopened (proposed again for its form), a second «Otro» works and finds the missing form already noted.
        using var reviewer = scenario.Client(scenario.Reviewer);
        using var discarded = await reviewer.PostAsJsonAsync($"/api/forms/documents/{sent.Id}/discard", new { expectedRevision = document.Revision }, Ct);
        using var reopened = await reviewer.PostAsJsonAsync($"/api/forms/documents/{sent.Id}/reopen", new { expectedRevision = document.Revision + 1 }, Ct);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (discarded.StatusCode, reopened.StatusCode));
        Assert.Equal("proposed", (await reopened.ReadJsonAsync()).GetProperty("document").GetProperty("state").GetString());
        var again = await scenario.PortAsync(port => port.ChooseOtherAsync(choice with { ChosenAtUtc = now.AddMinutes(5) }, Ct));
        Assert.Equal(StructuredDocumentChoiceState.Recorded, again);
        Assert.Single(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormMissingRequest>().AsNoTracking()
            .Where(value => value.SourceReceiptId == sent.Id).ToListAsync(Ct)));
    }

    [Fact]
    public async Task A_person_who_chooses_the_second_form_of_a_doubt_moves_the_document_to_it_and_the_reviewer_passes_it_there()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var doubt = await scenario.SendAsync(Ct, outcome: "doubt", candidates: [scenario.Form, scenario.Other]);
        var send = new StructuredDocumentSendRequest(doubt.Id, FormReceivedDocumentScenario.Channel, doubt.Request.SourceId, scenario.Sender, "es",
            scenario.Other.DefinitionId);
        var outsider = await scenario.TransferAsync(Ct);

        var notCandidate = await scenario.PortAsync(port => port.PrepareSendAsync(send with { DefinitionId = outsider.DefinitionId }, Ct));
        var ready = await scenario.PortAsync(port => port.PrepareSendAsync(send, Ct));

        Assert.Equal(StructuredDocumentSendState.Unavailable, notCandidate.State);
        Assert.Equal(StructuredDocumentSendState.Ready, ready.State);
        Assert.Equal(scenario.Other.VersionId, ready.Prefill!.Form.VersionId);
        var moved = await factory.ExecuteDbContextAsync(db => db.Set<FormReceivedDocument>().AsNoTracking().SingleAsync(value => value.Id == doubt.Id, Ct));
        Assert.Equal((scenario.Other.DefinitionId, scenario.Other.VersionId, 2L), (moved.DefinitionId!.Value, moved.VersionId!.Value, moved.Revision));
        using var reviewer = scenario.Client(scenario.Reviewer);
        Assert.Equal(["received", "formChosen"], await HistoryAsync(reviewer, doubt.Id));

        // The reviewer who saw the first form is late; passed to the second, it is confirmed there.
        object Pass(Guid versionId, long revision) => new
        {
            requestId = Guid.NewGuid(), versionId, expectedRevision = revision,
            answers = new object[] { new { fieldId = scenario.Other.AmountId, value = "150000" } },
        };
        using var late = await reviewer.PostAsJsonAsync($"/api/forms/documents/{doubt.Id}/records", Pass(scenario.Form.VersionId, 1), Ct);
        using var passed = await reviewer.PostAsJsonAsync($"/api/forms/documents/{doubt.Id}/records", Pass(scenario.Other.VersionId, 2), Ct);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal(HttpStatusCode.OK, passed.StatusCode);
        var recordId = (await passed.ReadJsonAsync()).GetProperty("recordId").GetGuid();
        var record = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().AsNoTracking().SingleAsync(value => value.Id == recordId, Ct));
        Assert.Equal(scenario.Other.DefinitionId, record.DefinitionId);
        var confirmed = await factory.ExecuteDbContextAsync(db => db.Set<FormReceivedDocument>().AsNoTracking().SingleAsync(value => value.Id == doubt.Id, Ct));
        Assert.Equal((FormReceivedDocumentState.Confirmed, scenario.Other.DefinitionId), (confirmed.State, confirmed.DefinitionId!.Value));
    }

    [Fact]
    public async Task The_inbox_searches_in_a_body_filters_and_counts_only_with_its_permission()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        using var reviewer = scenario.Client(scenario.Reviewer);
        using var before = await reviewer.GetAsync("/api/forms/documents/counts", Ct);
        var initial = await before.ReadJsonAsync();
        // The scenario's documents arrive in a minute of their own, so a search by when they arrived sees only them.
        var arrived = factory.Clock.GetUtcNow().UtcDateTime.AddDays(-400).AddSeconds(-Random.Shared.Next(1, 30_000_000));
        var waitedUntil = factory.Clock.GetUtcNow().UtcDateTime.AddDays(1);
        var proposed = await scenario.SendAsync(Ct, amount: "250000", operation: "445566", receivedAtUtc: arrived);
        var waiting = await scenario.SendAsync(Ct, amount: "99000", operation: "112233", replyExpiresAtUtc: waitedUntil, receivedAtUtc: arrived.AddSeconds(1));
        var quota = await scenario.SendAsync(Ct, notRead: StructuredDocumentNotRead.Quota, receivedAtUtc: arrived.AddSeconds(2));
        var rejected = await scenario.SendAsync(Ct, notRead: StructuredDocumentNotRead.Rejected, receivedAtUtc: arrived.AddSeconds(3));
        // Someone else sends one too, still waited for in their channel.
        var marta = await scenario.PersonAsync("Marta Segunda", [BusinessFormsPermissions.RecordsCreate], Ct);
        var second = await scenario.SendAsync(Ct, amount: "75000", operation: "998877", sender: marta, replyExpiresAtUtc: waitedUntil, receivedAtUtc: arrived.AddSeconds(4));
        var minute = new { receivedFromUtc = arrived, receivedToUtc = arrived.AddMinutes(1) };

        using var anonymous = scenario.Host.CreateClient();
        using var sender = scenario.Client(scenario.Sender);
        using var reader = scenario.Client(scenario.Reader);
        using var missing = await anonymous.PostAsJsonAsync(SearchPath, minute, Ct);
        using var denied = await sender.PostAsJsonAsync(SearchPath, minute, Ct);
        using var deniedCounts = await sender.GetAsync("/api/forms/documents/counts", Ct);
        using var all = await reader.PostAsJsonAsync(SearchPath, minute, Ct);
        // The private filters never go in the address: there is no list by GET, and the body is closed.
        using var address = await reader.GetAsync($"/api/forms/documents?senderUserId={scenario.Sender}", Ct);
        using var unknownMember = await reader.PostAsJsonAsync(SearchPath, new { minute.receivedFromUtc, phone = "3515550101" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedCounts.StatusCode);
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        Assert.True(all.Headers.CacheControl!.NoStore);
        Assert.NotEqual(HttpStatusCode.OK, address.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownMember.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(all, "documents-list-es", Ct);
        var page = await all.ReadJsonAsync();
        Assert.Equal(5, page.GetProperty("totalCount").GetInt32());
        var items = page.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetGuid());
        Assert.Equal(new HashSet<Guid> { proposed.Id, waiting.Id, quota.Id, rejected.Id, second.Id }, items.Keys.ToHashSet());
        Assert.Equal((3, 1, 1, 0, 1), Counts(page));
        var item = items[proposed.Id];
        Assert.All(items.Values, value => Assert.True(value.GetProperty("file").GetProperty("stored").GetBoolean()));
        Assert.Equal(("proposed", true, "Juan Remitente", scenario.Form.Name), (item.GetProperty("state").GetString(), item.GetProperty("forReview").GetBoolean(),
            item.GetProperty("sender").GetProperty("displayName").GetString(), item.GetProperty("form").GetProperty("name").GetString()));
        var highlight = Assert.Single(item.GetProperty("highlights").EnumerateArray());
        Assert.Equal(("Monto", "250.000"), (highlight.GetProperty("label").GetString(), highlight.GetProperty("value").GetString()));
        // Until when its person is waited for: the one that can still be completed in the channel; nobody for the rest.
        Assert.Equal(waitedUntil, items[waiting.Id].GetProperty("replyExpiresAtUtc").GetDateTime());
        Assert.All([proposed.Id, quota.Id, rejected.Id], id => Assert.Equal(JsonValueKind.Null, items[id].GetProperty("replyExpiresAtUtc").ValueKind));
        // The «Formulario» and «Mandó» filters with their counts, the most first, each under the other filters (a document without a form
        // has no form); neither list was cut.
        Assert.Equal([(scenario.Form.DefinitionId, scenario.Form.Name, 3)], Forms(page));
        Assert.Equal([(scenario.Sender, "Juan Remitente", 4), (marta, "Marta Segunda", 1)], Senders(page));
        Assert.Equal((false, false), Truncated(page));

        using var reviewing = await reviewer.PostAsJsonAsync(SearchPath, new { minute.receivedFromUtc, minute.receivedToUtc, state = "forReview" }, Ct);
        var forReview = await reviewing.ReadJsonAsync();
        Assert.Equal([(scenario.Form.DefinitionId, scenario.Form.Name, 1)], Forms(forReview));
        Assert.Equal([(scenario.Sender, "Juan Remitente", 1)], Senders(forReview));
        // The counts per state under a form leave out the documents without one.
        using var ofForm = await reviewer.PostAsJsonAsync(SearchPath, new { minute.receivedFromUtc, minute.receivedToUtc, definitionId = scenario.Form.DefinitionId }, Ct);
        var formPage = await ofForm.ReadJsonAsync();
        Assert.Equal((3, 1, 0, 0, 0), Counts(formPage));
        Assert.Equal([(scenario.Sender, "Juan Remitente", 2), (marta, "Marta Segunda", 1)], Senders(formPage));
        // A form without documents: nothing is listed, and its own facet still offers the forms the other filters leave, with the chosen
        // one in it even without documents.
        using var otherForm = await reviewer.PostAsJsonAsync(SearchPath, new { minute.receivedFromUtc, minute.receivedToUtc, definitionId = scenario.Other.DefinitionId }, Ct);
        var empty = await otherForm.ReadJsonAsync();
        Assert.Equal(0, empty.GetProperty("totalCount").GetInt32());
        Assert.Equal([(scenario.Form.DefinitionId, scenario.Form.Name, 3), (scenario.Other.DefinitionId, scenario.Other.Name, 0)], Forms(empty));
        Assert.Empty(Senders(empty));

        var mine = scenario.Sender;
        Assert.Equal([proposed.Id], await IdsAsync(reviewer, new { senderUserId = mine, state = "forReview" }));
        Assert.Equal([proposed.Id, waiting.Id], (await IdsAsync(reviewer, new { senderUserId = mine, state = "proposed" })).Order());
        Assert.Equal([quota.Id], await IdsAsync(reviewer, new { senderUserId = mine, state = "unclassified" }));
        Assert.Equal([rejected.Id], await IdsAsync(reviewer, new { senderUserId = mine, state = "discarded" }));
        Assert.Empty(await IdsAsync(reviewer, new { senderUserId = mine, state = "confirmed" }));
        Assert.Equal(new[] { proposed.Id, waiting.Id, second.Id }.Order(), (await IdsAsync(reviewer, new { definitionId = scenario.Form.DefinitionId })).Order());
        // The search is the digits typed, as the beginning of a typed number: «250.0» finds the amount 250000; the operation of a form that
        // is not a standard's copy is free text, not kept, and neither is the date of the reading.
        Assert.Equal([proposed.Id], await IdsAsync(reviewer, new { senderUserId = mine, search = "250.0" }));
        Assert.Equal([proposed.Id], await IdsAsync(reviewer, new { senderUserId = mine, search = "250000" }));
        Assert.Empty(await IdsAsync(reviewer, new { senderUserId = mine, search = "445566" }));
        Assert.Empty(await IdsAsync(reviewer, new { senderUserId = mine, search = "11112026" }));
        Assert.Empty(await IdsAsync(reviewer, new { senderUserId = mine, receivedToUtc = arrived }));
        Assert.Equal(4, (await IdsAsync(reviewer, new { senderUserId = mine, receivedFromUtc = arrived })).Count);

        using var letters = await reviewer.PostAsJsonAsync(SearchPath, new { senderUserId = mine, search = "abc" }, Ct);
        using var state = await reviewer.PostAsJsonAsync(SearchPath, new { senderUserId = mine, state = "archived" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, letters.StatusCode);
        var problem = await letters.ReadJsonAsync();
        Assert.Equal("Forms.ReceivedDocument.ContentInvalid", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("search", out _));
        await FormReceivedDocumentContractCapture.WriteAsync(letters, "documents-invalid-search-es", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, state.StatusCode);

        using var after = await reader.GetAsync("/api/forms/documents/counts", Ct);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(after, "documents-counts-es", Ct);
        var final = await after.ReadJsonAsync();
        Assert.Equal((initial.GetProperty("forReview").GetInt32() + 1, initial.GetProperty("unclassified").GetInt32() + 1, initial.GetProperty("total").GetInt32() + 2),
            (final.GetProperty("forReview").GetInt32(), final.GetProperty("unclassified").GetInt32(), final.GetProperty("total").GetInt32()));
    }

    [Fact]
    public async Task The_detail_shows_the_reading_the_proposal_and_the_history_and_the_file_is_never_stored()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var sent = await scenario.SendAsync(Ct);
        using var reviewer = scenario.Client(scenario.Reviewer);
        using var english = scenario.Client(scenario.Reviewer, "en");
        using var reader = scenario.Client(scenario.Reader);

        using var detail = await reviewer.GetAsync($"/api/forms/documents/{sent.Id}", Ct);
        using var detailEn = await english.GetAsync($"/api/forms/documents/{sent.Id}", Ct);
        using var readOnly = await reader.GetAsync($"/api/forms/documents/{sent.Id}", Ct);
        using var unknown = await reviewer.GetAsync($"/api/forms/documents/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.True(detail.Headers.CacheControl!.NoStore);
        await FormReceivedDocumentContractCapture.WriteAsync(detail, "document-detail-es", Ct);
        await FormReceivedDocumentContractCapture.WriteAsync(detailEn, "document-detail-en", Ct);
        var body = await detail.ReadJsonAsync();
        var document = body.GetProperty("document");
        Assert.Equal(("proposed", true, FormReceivedDocumentScenario.Channel, "comprobante de pago", 1L), (document.GetProperty("state").GetString(),
            document.GetProperty("forReview").GetBoolean(), document.GetProperty("channelId").GetString(), document.GetProperty("documentType").GetString(),
            document.GetProperty("revision").GetInt64()));
        Assert.Equal((scenario.Form.DefinitionId, scenario.Form.Name), (document.GetProperty("form").GetProperty("definitionId").GetGuid(),
            document.GetProperty("form").GetProperty("name").GetString()));
        var file = document.GetProperty("file");
        // Every document so far keeps its copy: «stored».
        Assert.Equal(("comprobante.pdf", "application/pdf", sent.Content.LongLength, 1, true), (file.GetProperty("name").GetString(),
            file.GetProperty("mediaType").GetString(), file.GetProperty("size").GetInt64(), file.GetProperty("pages").GetInt32(), file.GetProperty("stored").GetBoolean()));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (document.GetProperty("record").ValueKind, document.GetProperty("deleteAfterUtc").ValueKind));
        var reading = body.GetProperty("reading");
        Assert.Equal(("readable", "es", false), (reading.GetProperty("readability").GetString(), reading.GetProperty("language").GetString(),
            reading.GetProperty("truncated").GetBoolean()));
        Assert.Contains("778899", reading.GetProperty("text").GetString(), StringComparison.Ordinal);
        var proposal = body.GetProperty("proposal");
        Assert.Equal(("sure", JsonValueKind.Null), (proposal.GetProperty("outcome").GetString(), proposal.GetProperty("repetition").ValueKind));
        var candidate = Assert.Single(proposal.GetProperty("candidates").EnumerateArray());
        Assert.Equal((scenario.Form.VersionId, scenario.Form.Name), (candidate.GetProperty("versionId").GetGuid(), candidate.GetProperty("name").GetString()));
        Assert.Equal([("Monto", "150000", "150.000", null, true), ("Operación", "778899", "778899", "doubtful", false)],
            candidate.GetProperty("values").EnumerateArray().Select(value => (value.GetProperty("label").GetString(), value.GetProperty("value").GetString(),
                value.GetProperty("text").GetString(), value.GetProperty("mark").GetString(), value.GetProperty("required").GetBoolean())));
        // Only a choice has options to pick from.
        Assert.All(candidate.GetProperty("values").EnumerateArray(), value => Assert.Equal(JsonValueKind.Null, value.GetProperty("options").ValueKind));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("extraction").ValueKind);
        var received = Assert.Single(body.GetProperty("history").EnumerateArray());
        Assert.Equal(("received", "sure", JsonValueKind.Null), (received.GetProperty("kind").GetString(), received.GetProperty("detail").GetString(),
            received.GetProperty("actor").ValueKind));
        Assert.True(body.GetProperty("canReview").GetBoolean());
        // The forms with reading the reviewer can change it to, never «Gastos», which does not read documents. How many depends on the
        // shared database (at most 100): the cap is tested with a database of its own.
        var forms = FormsToChange(body);
        Assert.InRange(forms.Length, 1, 100);
        Assert.DoesNotContain(forms, form => form.DefinitionId == scenario.Other.DefinitionId);
        var bodyEn = await detailEn.ReadJsonAsync();
        Assert.Equal(scenario.Form.NameEn, bodyEn.GetProperty("document").GetProperty("form").GetProperty("name").GetString());
        var highlightEn = Assert.Single(bodyEn.GetProperty("document").GetProperty("highlights").EnumerateArray());
        Assert.Equal(("Amount", "150,000"), (highlightEn.GetProperty("label").GetString(), highlightEn.GetProperty("value").GetString()));
        // Whoever only sees the inbox cannot change its form: no forms to choose.
        var readOnlyBody = await readOnly.ReadJsonAsync();
        Assert.False(readOnlyBody.GetProperty("canReview").GetBoolean());
        Assert.Empty(readOnlyBody.GetProperty("forms").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("Forms.ReceivedDocument.NotFound", (await unknown.ReadJsonAsync()).GetProperty("code").GetString());

        using var opened = await reader.GetAsync($"/api/forms/documents/{sent.Id}/file", Ct);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal("application/pdf", opened.Content.Headers.ContentType!.MediaType);
        Assert.True(opened.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff", Assert.Single(opened.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("comprobante.pdf", opened.Content.Headers.ContentDisposition!.FileNameStar ?? opened.Content.Headers.ContentDisposition.FileName);
        Assert.Equal(sent.Content, await opened.Content.ReadAsByteArrayAsync(Ct));
        using var notFound = await reader.GetAsync($"/api/forms/documents/{Guid.NewGuid()}/file", Ct);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        using var sender = scenario.Client(scenario.Sender);
        using var forbidden = await sender.GetAsync($"/api/forms/documents/{sent.Id}/file", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await scenario.Store.DeleteAsync(FormReceivedDocumentCopyPolicy.Key(sent.Id, "application/pdf"), Ct);
        using var gone = await reader.GetAsync($"/api/forms/documents/{sent.Id}/file", Ct);
        Assert.Equal(HttpStatusCode.Conflict, gone.StatusCode);
        Assert.Equal("Forms.ReceivedDocument.FileUnavailable", (await gone.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Discarding_and_reopening_need_review_and_the_revision_seen()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var sent = await scenario.SendAsync(Ct);
        using var reviewer = scenario.Client(scenario.Reviewer);
        using var reader = scenario.Client(scenario.Reader);
        var path = $"/api/forms/documents/{sent.Id}";

        using var denied = await reader.PostAsJsonAsync(path + "/discard", new { expectedRevision = 1 }, Ct);
        using var invalid = await reviewer.PostAsJsonAsync(path + "/discard", new { }, Ct);
        using var discarded = await reviewer.PostAsJsonAsync(path + "/discard", new { expectedRevision = 1 }, Ct);
        using var stale = await reviewer.PostAsJsonAsync(path + "/discard", new { expectedRevision = 1 }, Ct);
        using var twice = await reviewer.PostAsJsonAsync(path + "/discard", new { expectedRevision = 2 }, Ct);
        using var unknown = await reviewer.PostAsJsonAsync($"/api/forms/documents/{Guid.NewGuid()}/discard", new { expectedRevision = 1 }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("Forms.ReceivedDocument.ContentInvalid", (await invalid.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, discarded.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(discarded, "document-discarded-es", Ct);
        var body = await discarded.ReadJsonAsync();
        var document = body.GetProperty("document");
        Assert.Equal(("discarded", "reviewer", false, 2L), (document.GetProperty("state").GetString(), document.GetProperty("reasonCode").GetString(),
            document.GetProperty("forReview").GetBoolean(), document.GetProperty("revision").GetInt64()));
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime.AddDays(FormReceivedDocument.RetentionDays), document.GetProperty("deleteAfterUtc").GetDateTime());
        var step = body.GetProperty("history").EnumerateArray().Last();
        Assert.Equal(("discarded", "Ana Revisora"), (step.GetProperty("kind").GetString(), step.GetProperty("actor").GetProperty("displayName").GetString()));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(("Forms.ReceivedDocument.RevisionConflict", "El documento cambió. Recargalo y probá de nuevo."),
            await ProblemAsync(stale));
        await FormReceivedDocumentContractCapture.WriteAsync(stale, "document-revision-conflict-es", Ct);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("Forms.ReceivedDocument.StateConflict", (await ProblemAsync(twice)).Code);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var reopened = await reviewer.PostAsJsonAsync(path + "/reopen", new { expectedRevision = 2 }, Ct);
        using var reopenedAgain = await reviewer.PostAsJsonAsync(path + "/reopen", new { expectedRevision = 3 }, Ct);

        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        var again = (await reopened.ReadJsonAsync()).GetProperty("document");
        Assert.Equal(("proposed", true, 3L, JsonValueKind.Null, JsonValueKind.Null), (again.GetProperty("state").GetString(), again.GetProperty("forReview").GetBoolean(),
            again.GetProperty("revision").GetInt64(), again.GetProperty("reasonCode").ValueKind, again.GetProperty("deleteAfterUtc").ValueKind));
        Assert.Equal(HttpStatusCode.Conflict, reopenedAgain.StatusCode);
        Assert.Equal(["received", "discarded", "reopened"], await HistoryAsync(reviewer, sent.Id));
    }

    [Fact]
    public async Task Changing_the_form_reads_the_stored_reading_for_the_fixed_form_with_the_context_of_whoever_asked()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var sent = await scenario.SendAsync(Ct);
        // A colleague whose account is in English: whatever they poll is still the step of whoever asked, in their language.
        var colleague = await scenario.PersonAsync("Eva Colega", [BusinessFormsPermissions.DocumentsRead, BusinessFormsPermissions.DocumentsReview], Ct,
            culture: "en");
        using var reviewer = scenario.Client(scenario.Reviewer);
        using var second = scenario.Client(colleague, "en");
        using var reader = scenario.Client(scenario.Reader);
        var path = $"/api/forms/documents/{sent.Id}";
        var requestId = Guid.NewGuid();

        using var denied = await reader.PutAsJsonAsync(path + "/form", new { requestId, definitionId = scenario.Other.DefinitionId, expectedRevision = 1 }, Ct);
        using var stale = await reviewer.PutAsJsonAsync(path + "/form", new { requestId, definitionId = scenario.Other.DefinitionId, expectedRevision = 5 }, Ct);
        using var unavailable = await reviewer.PutAsJsonAsync(path + "/form", new { requestId, definitionId = Guid.NewGuid(), expectedRevision = 1 }, Ct);
        using var accepted = await reviewer.PutAsJsonAsync(path + "/form", new { requestId, definitionId = scenario.Other.DefinitionId, expectedRevision = 1 }, Ct);
        using var repeated = await reviewer.PutAsJsonAsync(path + "/form", new { requestId, definitionId = scenario.Other.DefinitionId, expectedRevision = 1 }, Ct);
        using var reused = await second.PutAsJsonAsync(path + "/form", new { requestId, definitionId = scenario.Form.DefinitionId, expectedRevision = 2 }, Ct);
        // Another reviewer, in another language, polls the extraction the first one asked for.
        using var pending = await second.GetAsync($"{path}/extraction?requestId={requestId}", Ct);
        using var otherRequest = await reviewer.GetAsync($"{path}/extraction?requestId={Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Forms.ReceivedDocument.RevisionConflict", (await ProblemAsync(stale)).Code);
        Assert.Equal(HttpStatusCode.Conflict, unavailable.StatusCode);
        Assert.Equal("Forms.ReceivedDocument.FormUnavailable", (await ProblemAsync(unavailable)).Code);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(accepted, "document-extraction-accepted-es", Ct);
        Assert.Equal((requestId, "pending"), await ExtractionAsync(accepted));
        Assert.Equal(HttpStatusCode.Accepted, repeated.StatusCode);
        // The same request for another form is not the same request.
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal("Forms.ReceivedDocument.RequestConflict", (await ProblemAsync(reused)).Code);
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(pending, "document-extraction-pending-es", Ct);
        Assert.Equal((requestId, "pending"), await ExtractionAsync(pending));
        Assert.Equal(HttpStatusCode.NotFound, otherRequest.StatusCode);
        // One submission: an interpretation of forms-documents with the reviewer as Actor, no Subject, the form fixed and the stored reading.
        var operation = Assert.Single(scenario.Engine.Submitted);
        Assert.Equal((requestId, FormReceivedDocumentExtractionPolicy.ChannelId, scenario.Reviewer, scenario.Other.DefinitionId),
            (operation.RequestId, operation.Context.ChannelId, operation.Context.ActorUserId, operation.Context.FocusDefinitionId!.Value));
        Assert.Null(operation.Context.SubjectUserId);
        Assert.Equal([BusinessFormDocumentAssistantCapabilitySource.FormId, BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId],
            operation.Context.AllowedCapabilities.Select(capability => capability.Id));
        Assert.Equal((AssistantAttachmentKind.Document, FormReceivedDocumentCopyPolicy.Key(sent.Id, "application/pdf")),
            (operation.Attachment!.Kind, operation.Attachment.StorageKey));
        Assert.Contains("778899", operation.Attachment.Document!.Reading!.Text, StringComparison.Ordinal);
        var waiting = await (await reviewer.GetAsync(path, Ct)).ReadJsonAsync();
        Assert.Equal(requestId, waiting.GetProperty("extraction").GetProperty("requestId").GetGuid());
        Assert.Equal(2L, waiting.GetProperty("document").GetProperty("revision").GetInt64());

        var proposal = FormReceivedDocumentScenario.Proposal([scenario.Other], scenario.Reviewer, "sure",
            form => [(form.AmountId, "decimal", "150000", false), (form.OperationId, "shortText", "778899", false)], formRead: true);
        scenario.Engine.Complete(scenario.Reviewer, new(requestId, "completed", proposal, null, null) { Proposals = [proposal] });
        using var completed = await second.GetAsync($"{path}/extraction?requestId={requestId}", Ct);

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(completed, "document-extraction-completed-es", Ct);
        Assert.Equal((requestId, "completed"), await ExtractionAsync(completed));
        // Every read used the context sealed when it was asked: the first reviewer's, in their language and zone, whoever polled.
        Assert.Equal(2, scenario.Engine.Read.Count);
        Assert.All(scenario.Engine.Read, read => Assert.Equal((FormReceivedDocumentExtractionPolicy.ChannelId, scenario.Reviewer, "es", scenario.Other.DefinitionId),
            (read.ChannelId, read.ActorUserId, read.Culture, read.FocusDefinitionId!.Value)));
        var changed = await (await reviewer.GetAsync(path, Ct)).ReadJsonAsync();
        Assert.Equal((scenario.Other.DefinitionId, "proposed", 3L, JsonValueKind.Null), (changed.GetProperty("document").GetProperty("form").GetProperty("definitionId").GetGuid(),
            changed.GetProperty("document").GetProperty("state").GetString(), changed.GetProperty("document").GetProperty("revision").GetInt64(),
            changed.GetProperty("extraction").ValueKind));
        Assert.Equal(scenario.Other.VersionId, Assert.Single(changed.GetProperty("proposal").GetProperty("candidates").EnumerateArray()).GetProperty("versionId").GetGuid());
        Assert.Equal(["received", "extractionRequested", "formChanged"], await HistoryAsync(reviewer, sent.Id));
        // The change is the step of whoever asked for it, even if a colleague's poll applied it: the form by its Spanish name, never the
        // English one of the colleague's account.
        var formChanged = changed.GetProperty("history").EnumerateArray().Last();
        Assert.Equal((scenario.Other.Name, scenario.Reviewer), (formChanged.GetProperty("detail").GetString(),
            formChanged.GetProperty("actor").GetProperty("userId").GetGuid()));
        Assert.NotEqual(scenario.Other.Name, scenario.Other.NameEn);
    }

    [Fact]
    public async Task An_extraction_that_finds_nothing_or_a_form_that_changed_fails_with_its_reason_and_an_engine_failure_answers_its_own_error()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var sent = await scenario.SendAsync(Ct);
        using var reviewer = scenario.Client(scenario.Reviewer);
        var path = $"/api/forms/documents/{sent.Id}";

        // Nothing recognized for the fixed form: the request is dropped and the document stays as it was.
        var unrecognizedId = Guid.NewGuid();
        using var first = await reviewer.PutAsJsonAsync(path + "/form", new { requestId = unrecognizedId, definitionId = scenario.Other.DefinitionId, expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var unknown = FormReceivedDocumentScenario.Proposal([scenario.Other], scenario.Reviewer, "unknown", _ => [], formRead: true);
        scenario.Engine.Complete(scenario.Reviewer, new(unrecognizedId, "completed", unknown, null, null) { Proposals = [unknown] });
        // A colleague's poll, from an account in English, finds the failure: it is still the step of whoever asked, read in their language.
        var colleague = await scenario.PersonAsync("Eva Colega", [BusinessFormsPermissions.DocumentsRead, BusinessFormsPermissions.DocumentsReview], Ct,
            culture: "en");
        using var colleagueClient = scenario.Client(colleague, "en");
        using var unrecognized = await colleagueClient.GetAsync($"{path}/extraction?requestId={unrecognizedId}", Ct);
        Assert.Equal(HttpStatusCode.OK, unrecognized.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(unrecognized, "document-extraction-failed-es", Ct);
        var failure = await unrecognized.ReadJsonAsync();
        Assert.Equal(("failed", FormReceivedDocumentExtractionPolicy.Unrecognized), (failure.GetProperty("state").GetString(), failure.GetProperty("failureCode").GetString()));
        var failedStep = (await (await reviewer.GetAsync(path, Ct)).ReadJsonAsync()).GetProperty("history").EnumerateArray().Last();
        Assert.Equal(("extractionFailed", FormReceivedDocumentExtractionPolicy.Unrecognized, scenario.Reviewer), (failedStep.GetProperty("kind").GetString(),
            failedStep.GetProperty("detail").GetString(), failedStep.GetProperty("actor").GetProperty("userId").GetGuid()));
        var colleagueRead = Assert.Single(scenario.Engine.Read);
        Assert.Equal((scenario.Reviewer, "es"), (colleagueRead.ActorUserId, colleagueRead.Culture));

        // The engine read a version that is no longer the form's active one: the form changed, its own reason.
        var changedId = Guid.NewGuid();
        using var second = await reviewer.PutAsJsonAsync(path + "/form", new { requestId = changedId, definitionId = scenario.Other.DefinitionId, expectedRevision = 3 }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var old = FormReceivedDocumentScenario.Proposal([scenario.Other with { VersionId = Guid.NewGuid() }], scenario.Reviewer, "sure",
            form => [(form.AmountId, "decimal", "150000", false)], formRead: true);
        scenario.Engine.Complete(scenario.Reviewer, new(changedId, "completed", old, null, null) { Proposals = [old] });
        using var changed = await reviewer.GetAsync($"{path}/extraction?requestId={changedId}", Ct);
        var changedFailure = await changed.ReadJsonAsync();
        Assert.Equal(("failed", FormReceivedDocumentExtractionPolicy.FormChanged), (changedFailure.GetProperty("state").GetString(),
            changedFailure.GetProperty("failureCode").GetString()));

        // An engine that cannot take the request answers the inbox's own error, documented, and leaves the document as it was.
        // Whatever the engine answers (here an error of this module, never its code in the body), the inbox answers its own.
        scenario.Engine.SubmitFailure = FormRecordErrors.InvalidResponse;
        using var refused = await reviewer.PutAsJsonAsync(path + "/form", new { requestId = Guid.NewGuid(), definitionId = scenario.Other.DefinitionId, expectedRevision = 5 }, Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, refused.StatusCode);
        Assert.Equal(("Forms.ReceivedDocument.ExtractionUnavailable", "No pudimos leer el documento para otro formulario. Probá de nuevo en un rato."),
            await ProblemAsync(refused));
        await FormReceivedDocumentContractCapture.WriteAsync(refused, "document-extraction-unavailable-es", Ct);
        var kept = (await (await reviewer.GetAsync(path, Ct)).ReadJsonAsync()).GetProperty("document");
        Assert.Equal((scenario.Form.DefinitionId, 5L), (kept.GetProperty("form").GetProperty("definitionId").GetGuid(), kept.GetProperty("revision").GetInt64()));
        Assert.Equal(["received", "extractionRequested", "extractionFailed", "extractionRequested", "extractionFailed"], await HistoryAsync(reviewer, sent.Id));
    }

    [Fact]
    public async Task While_its_person_can_still_complete_it_a_proposed_document_is_not_discarded_read_again_or_passed()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var sent = await scenario.SendAsync(Ct, replyExpiresAtUtc: factory.Clock.GetUtcNow().UtcDateTime.AddHours(1));
        using var reviewer = scenario.Client(scenario.Reviewer);
        var path = $"/api/forms/documents/{sent.Id}";

        using var discard = await reviewer.PostAsJsonAsync(path + "/discard", new { expectedRevision = 1 }, Ct);
        using var change = await reviewer.PutAsJsonAsync(path + "/form", new { requestId = Guid.NewGuid(), definitionId = scenario.Other.DefinitionId, expectedRevision = 1 }, Ct);
        using var pass = await reviewer.PostAsJsonAsync(path + "/records", new
        {
            requestId = Guid.NewGuid(), versionId = scenario.Form.VersionId, expectedRevision = 1,
            answers = new object[] { new { fieldId = scenario.Form.AmountId, value = "150000" } },
        }, Ct);

        Assert.All([discard, change, pass], response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
        Assert.Equal(("Forms.ReceivedDocument.AwaitingSender", "Quien lo mandó todavía puede completarlo por su canal. Esperá a que venza su respuesta."),
            await ProblemAsync(discard));
        await FormReceivedDocumentContractCapture.WriteAsync(discard, "document-awaiting-sender-es", Ct);
        Assert.Equal("Forms.ReceivedDocument.AwaitingSender", (await ProblemAsync(change)).Code);
        Assert.Equal("Forms.ReceivedDocument.AwaitingSender", (await ProblemAsync(pass)).Code);
        Assert.Empty(scenario.Engine.Submitted);
        var document = await factory.ExecuteDbContextAsync(db => db.Set<FormReceivedDocument>().AsNoTracking().SingleAsync(value => value.Id == sent.Id, Ct));
        Assert.Equal((FormReceivedDocumentState.Proposed, 1L, (Guid?)null), (document.State, document.Revision, document.RecordId));
    }

    [Fact]
    public async Task Passing_to_the_form_loads_the_response_of_the_sender_with_the_document_and_confirms_it()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var sent = await scenario.SendAsync(Ct);
        using var reviewer = scenario.Client(scenario.Reviewer);
        using var reader = scenario.Client(scenario.Reader);
        var path = $"/api/forms/documents/{sent.Id}/records";
        var requestId = Guid.NewGuid();
        object Body(Guid id, long revision) => new
        {
            requestId = id, versionId = scenario.Form.VersionId, expectedRevision = revision,
            answers = new object[] { new { fieldId = scenario.Form.AmountId, value = "150000" }, new { fieldId = scenario.Form.OperationId, value = "778800" } },
        };

        using var denied = await reader.PostAsJsonAsync(path, Body(requestId, 1), Ct);
        using var stale = await reviewer.PostAsJsonAsync(path, Body(Guid.NewGuid(), 4), Ct);
        using var passed = await reviewer.PostAsJsonAsync(path, Body(requestId, 1), Ct);
        using var replayed = await reviewer.PostAsJsonAsync(path, Body(requestId, 1), Ct);
        using var another = await reviewer.PostAsJsonAsync(path, Body(Guid.NewGuid(), 2), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Forms.ReceivedDocument.RevisionConflict", (await ProblemAsync(stale)).Code);
        Assert.Equal(HttpStatusCode.OK, passed.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(passed, "document-record-es", Ct);
        var outcome = await passed.ReadJsonAsync();
        var recordId = outcome.GetProperty("recordId").GetGuid();
        Assert.Equal((sent.Id, scenario.Form.Prefix + "-000001"), (outcome.GetProperty("documentId").GetGuid(), outcome.GetProperty("number").GetString()));
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        Assert.Equal(recordId, (await replayed.ReadJsonAsync()).GetProperty("recordId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, another.StatusCode);
        Assert.Equal("Forms.ReceivedDocument.StateConflict", (await ProblemAsync(another)).Code);

        var record = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().AsNoTracking().SingleAsync(value => value.Id == recordId, Ct));
        Assert.Equal((scenario.Sender, scenario.Form.VersionId, false), (record.ActorUserId, record.VersionId, record.EnteredInApp));
        var files = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordFile>().AsNoTracking().Where(value => value.RecordId == recordId).ToListAsync(Ct));
        Assert.Equal(scenario.Form.ReceiptId, Assert.Single(files).FieldId);
        var document = await factory.ExecuteDbContextAsync(db => db.Set<FormReceivedDocument>().AsNoTracking().SingleAsync(value => value.Id == sent.Id, Ct));
        Assert.Equal((FormReceivedDocumentState.Confirmed, recordId, scenario.Reviewer), (document.State, document.RecordId!.Value, document.ReviewedByUserId!.Value));
        Assert.Equal(["received", "confirmed"], await HistoryAsync(reviewer, sent.Id));
        var confirmed = (await (await reviewer.GetAsync($"/api/forms/documents/{sent.Id}", Ct)).ReadJsonAsync()).GetProperty("document");
        Assert.Equal(("confirmed", scenario.Form.Prefix + "-000001"), (confirmed.GetProperty("state").GetString(),
            confirmed.GetProperty("record").GetProperty("number").GetString()));
        // The response says where it came from: the document of the inbox and its file, passed by the reviewer.
        using var detail = await reviewer.GetAsync($"/api/forms/records/{recordId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(detail, "document-record-detail-es", Ct);
        var origin = (await detail.ReadJsonAsync()).GetProperty("origin");
        Assert.Equal((FormReceivedDocumentCapturePolicy.SourceId, "Ana Revisora"), (origin.GetProperty("channelId").GetString(), origin.GetProperty("senderName").GetString()));
        Assert.Equal((sent.Id, "comprobante.pdf"), (origin.GetProperty("document").GetProperty("id").GetGuid(),
            origin.GetProperty("document").GetProperty("name").GetString()));
        // Whoever reads responses without seeing the inbox gets the same origin without the document.
        var responses = await scenario.PersonAsync("Rita Respuestas", [BusinessFormsPermissions.RecordsRead], Ct);
        using var responsesClient = scenario.Client(responses);
        using var withoutInbox = await responsesClient.GetAsync($"/api/forms/records/{recordId}", Ct);
        Assert.Equal(HttpStatusCode.OK, withoutInbox.StatusCode);
        var plainOrigin = (await withoutInbox.ReadJsonAsync()).GetProperty("origin");
        Assert.Equal((FormReceivedDocumentCapturePolicy.SourceId, "Ana Revisora"), (plainOrigin.GetProperty("channelId").GetString(),
            plainOrigin.GetProperty("senderName").GetString()));
        Assert.False(plainOrigin.TryGetProperty("document", out _));

        // Whoever sent it must still be active to load a response in their name.
        var other = await scenario.SendAsync(Ct);
        await scenario.SetActiveAsync(scenario.Sender, false, Ct);
        using var inactive = await reviewer.PostAsJsonAsync($"/api/forms/documents/{other.Id}/records", Body(Guid.NewGuid(), 1), Ct);
        Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);
        Assert.Equal(("Forms.ReceivedDocument.SenderInactive", "Quien mandó el documento ya no está activo."), await ProblemAsync(inactive));
        await FormReceivedDocumentContractCapture.WriteAsync(inactive, "document-sender-inactive-es", Ct);
    }

    [Fact]
    public async Task The_purge_deletes_what_expired_and_its_copy_is_no_longer_kept()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var expired = await scenario.SendAsync(Ct, notRead: StructuredDocumentNotRead.Quota);
        var recent = await scenario.SendAsync(Ct, notRead: StructuredDocumentNotRead.Quota);
        var proposed = await scenario.SendAsync(Ct);
        var keys = new[] { expired, recent, proposed }.Select(sent => FormReceivedDocumentCopyPolicy.Key(sent.Id, "application/pdf")).ToArray();
        var past = factory.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
        await factory.ExecuteDbContextAsync(db => db.Database.ExecuteSqlAsync(
            $"""UPDATE "FormReceivedDocuments" SET "DeleteAfterUtc" = {past} WHERE "Id" = {expired.Id}""", Ct));

        var kept = await scenario.ScopeAsync(services => services.GetServices<IPrivateFileRetentionSource>().OfType<FormReceivedDocumentRetentionSource>().Single()
            .ListReferencedKeysAsync([.. keys, "forms/documents/not-a-key.pdf", "ai/assistant/attachments/other.pdf"], Ct));
        var purged = await scenario.ScopeAsync(services => services.GetRequiredService<IFormReceivedDocumentRetentionService>().PurgeExpiredAsync(Ct));
        var keptAfter = await scenario.ScopeAsync(services => services.GetServices<IPrivateFileRetentionSource>().OfType<FormReceivedDocumentRetentionSource>().Single()
            .ListReferencedKeysAsync(keys, Ct));

        Assert.Equal(keys.Order(), kept.Order());
        Assert.True(purged.IsSuccess);
        Assert.Equal(keys[1..].Order(), keptAfter.Order());
        var left = await factory.ExecuteDbContextAsync(db => db.Set<FormReceivedDocument>().AsNoTracking()
            .Where(document => document.SenderUserId == scenario.Sender).Select(document => document.Id).ToListAsync(Ct));
        Assert.Equal(new[] { recent.Id, proposed.Id }.Order(), left.Order());
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<FormReceivedDocumentEvent>().CountAsync(item => item.DocumentId == expired.Id, Ct)));
        using var reviewer = scenario.Client(scenario.Reviewer);
        using var gone = await reviewer.GetAsync($"/api/forms/documents/{expired.Id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task A_reviewer_passes_the_document_of_a_sender_who_can_no_longer_load_responses()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var sent = await scenario.SendAsync(Ct);
        // «Revisar documentos» replaces the sender's «Completar formularios» for this path only: without any role, the pass still goes.
        await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().SetRolesAsync(scenario.Sender, [], Ct));
        // The reviewer reads in English; the response is in the language of whoever sent it.
        using var reviewer = scenario.Client(scenario.Reviewer, "en");

        using var passed = await reviewer.PostAsJsonAsync($"/api/forms/documents/{sent.Id}/records", new
        {
            requestId = Guid.NewGuid(), versionId = scenario.Form.VersionId, expectedRevision = 1,
            answers = new object[] { new { fieldId = scenario.Form.AmountId, value = "150000" } },
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, passed.StatusCode);
        var recordId = (await passed.ReadJsonAsync()).GetProperty("recordId").GetGuid();
        var record = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().AsNoTracking().SingleAsync(value => value.Id == recordId, Ct));
        Assert.Equal((scenario.Sender, "es"), (record.ActorUserId, record.PresentationCulture));
    }

    [Fact]
    public async Task A_copy_of_the_transfer_receipt_finds_a_repeated_operation_among_its_responses_and_searches_by_its_operation_number()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var transfer = await scenario.TransferAsync(Ct);
        var number = await scenario.RespondAsync(transfer, "00112233", 150000m, new DateOnly(2026, 11, 11), Ct);

        // The same operation, amount and date of a current response: repeated, with its number, and discarded.
        var repeated = await scenario.SendAsync(Ct, proposal: scenario.TransferProposal(transfer, "00112233", "150000", "2026-11-11"));
        // Another operation of the same amount and date: proposed, and found by its operation number, which the standard names.
        var other = await scenario.SendAsync(Ct, proposal: scenario.TransferProposal(transfer, "44556677", "150000", "2026-11-11"));

        Assert.Equal(StructuredDocumentReplyKind.Duplicate, repeated.Reply.Kind);
        Assert.Equal(number, repeated.Reply.Duplicate!.RecordNumber);
        Assert.Equal(StructuredDocumentReplyKind.Sure, other.Reply.Kind);
        using var reviewer = scenario.Client(scenario.Reviewer);
        Assert.Equal([other.Id], await IdsAsync(reviewer, new { senderUserId = scenario.Sender, search = "4455" }));
        Assert.Equal([repeated.Id], await IdsAsync(reviewer, new { senderUserId = scenario.Sender, search = "0011", state = "discarded" }));

        // A response of that other operation appears later: a repeated registration answers what was decided, never classified again.
        await scenario.RespondAsync(transfer, "44556677", 150000m, new DateOnly(2026, 11, 11), Ct);
        var replayed = await scenario.PortAsync(port => port.RegisterAsync(other.Request, Ct));
        Assert.Equal((other.Id, StructuredDocumentReplyKind.Sure, true), (replayed.DocumentId!.Value, replayed.Kind, replayed.WasAlreadyRegistered));
        Assert.Equal(transfer.DefinitionId, Assert.Single(replayed.Forms).DefinitionId);
    }

    [Fact]
    public async Task Each_choice_offers_its_options_the_catalog_ones_as_the_sender_could_choose_them_and_the_pass_checks_them()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var cobros = await scenario.ChoicesAsync(Ct);
        var sent = await scenario.SendAsync(Ct, proposal: scenario.ChoicesProposal(cobros, "150000", cobros.TransferId));
        using var reviewer = scenario.Client(scenario.Reviewer);

        using var detail = await reviewer.GetAsync($"/api/forms/documents/{sent.Id}", Ct);

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(detail, "document-detail-choices-es", Ct);
        var values = Values(await detail.ReadJsonAsync());
        Assert.Equal(JsonValueKind.Null, values[cobros.AmountId].GetProperty("options").ValueKind);
        // A choice of its own offers its options, in the reviewer's language, with the one read chosen; no option has a group.
        Assert.Equal([(cobros.TransferId, "Transferencia", null), (cobros.DepositId, "Depósito", null)], Options(values[cobros.KindId]));
        Assert.Equal((cobros.TransferId.ToString("D"), "Transferencia"), (values[cobros.KindId].GetProperty("value").GetString(),
            values[cobros.KindId].GetProperty("text").GetString()));
        // A catalog question, left to the server, offers what the sender could choose: never a deactivated option.
        Assert.Equal(JsonValueKind.Null, values[cobros.BankId].GetProperty("value").ValueKind);
        Assert.Equal([(cobros.GaliciaId, "Galicia", null), (cobros.NacionId, "Nación", null)], Options(values[cobros.BankId]));
        // A question that chooses first by a field is never read from a document, so the inbox never lists its options.
        Assert.False(values.ContainsKey(cobros.BranchId));
        // Whoever only sees the inbox cannot pass it: an empty list for each choice, nothing for another type.
        using var reader = scenario.Client(scenario.Reader);
        var readOnly = Values(await (await reader.GetAsync($"/api/forms/documents/{sent.Id}", Ct)).ReadJsonAsync());
        Assert.Equal((0, 0), (readOnly[cobros.KindId].GetProperty("options").GetArrayLength(), readOnly[cobros.BankId].GetProperty("options").GetArrayLength()));
        Assert.Equal(JsonValueKind.Null, readOnly[cobros.AmountId].GetProperty("options").ValueKind);

        object Pass(Guid bank) => new
        {
            requestId = Guid.NewGuid(), versionId = cobros.VersionId, expectedRevision = 1,
            answers = new object[]
            {
                new { fieldId = cobros.AmountId, value = "150000" }, new { fieldId = cobros.KindId, value = cobros.TransferId.ToString("D") },
                new { fieldId = cobros.BankId, value = bank.ToString("D") },
            },
        };
        using var offered = await reviewer.PostAsJsonAsync($"/api/forms/documents/{sent.Id}/records", Pass(cobros.OldBankId), Ct);
        await AssertFieldErrorAsync(offered, "Forms.Record.CatalogItemInactive", $"answers.{cobros.BankId:D}");
        Assert.Equal(FormReceivedDocumentState.Proposed,
            (await factory.ExecuteDbContextAsync(db => db.Set<FormReceivedDocument>().AsNoTracking().SingleAsync(value => value.Id == sent.Id, Ct))).State);
        using var passed = await reviewer.PostAsJsonAsync($"/api/forms/documents/{sent.Id}/records", Pass(cobros.GaliciaId), Ct);
        Assert.Equal(HttpStatusCode.OK, passed.StatusCode);
        var recordId = (await passed.ReadJsonAsync()).GetProperty("recordId").GetGuid();
        var references = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordReference>().AsNoTracking()
            .Where(value => value.RecordId == recordId).Select(value => value.ItemId).ToListAsync(Ct));
        Assert.Equal([cobros.GaliciaId], references);
        // Once passed, nobody can act on it: no options to choose.
        var confirmed = Values(await (await reviewer.GetAsync($"/api/forms/documents/{sent.Id}", Ct)).ReadJsonAsync());
        Assert.Equal((0, 0), (confirmed[cobros.KindId].GetProperty("options").GetArrayLength(), confirmed[cobros.BankId].GetProperty("options").GetArrayLength()));
    }

    [Fact]
    public async Task A_catalog_filtered_by_a_datum_of_the_person_offers_and_accepts_what_the_sender_could_choose_never_what_the_reviewer_could()
    {
        // The data of the people are limited for the whole database: a database of its own.
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct, isolated: true);
        var deposits = await scenario.BranchesAsync(Ct);
        var (center, south) = (deposits.ItemIds[0], deposits.ItemIds[1]);
        var sent = await scenario.SendAsync(Ct, proposal: scenario.CatalogProposal(deposits));
        using var reviewer = scenario.Client(scenario.Reviewer);

        using var detail = await reviewer.GetAsync($"/api/forms/documents/{sent.Id}", Ct);

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        // The sender is in the center and the reviewer in the south: the branches are the sender's, all of them.
        var branch = Values(await detail.ReadJsonAsync())[deposits.ChoiceId];
        Assert.Equal([(center, "Sucursal Centro", null)], Options(branch));
        Assert.Equal(JsonValueKind.Null, branch.GetProperty("optionsUnavailable").ValueKind);
        object Pass(Guid option) => new
        {
            requestId = Guid.NewGuid(), versionId = deposits.VersionId, expectedRevision = 1,
            answers = new object[] { new { fieldId = deposits.AmountId, value = "150000" }, new { fieldId = deposits.ChoiceId, value = option.ToString("D") } },
        };
        using var reviewers = await reviewer.PostAsJsonAsync($"/api/forms/documents/{sent.Id}/records", Pass(south), Ct);
        await AssertFieldErrorAsync(reviewers, "Forms.Record.CatalogItemInactive", $"answers.{deposits.ChoiceId:D}");
        using var senders = await reviewer.PostAsJsonAsync($"/api/forms/documents/{sent.Id}/records", Pass(center), Ct);
        Assert.Equal(HttpStatusCode.OK, senders.StatusCode);
    }

    [Fact]
    public async Task A_catalog_the_sender_cannot_be_offered_for_lack_of_a_datum_names_that_datum_in_the_language_of_whoever_reads()
    {
        // The data of the people are limited for the whole database: a database of its own.
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct, isolated: true);
        var deposits = await scenario.BranchesAsync(Ct);
        // Someone without a zone: «no se le puede mandar» until the team completes it in «Datos de personas».
        var withoutZone = await scenario.PersonAsync("Pedro Sin Zona", [BusinessFormsPermissions.RecordsCreate], Ct);
        var sent = await scenario.SendAsync(Ct, sender: withoutZone, proposal: scenario.CatalogProposal(deposits, withoutZone));
        using var reviewer = scenario.Client(scenario.Reviewer);
        using var english = scenario.Client(scenario.Reviewer, "en");
        using var reader = scenario.Client(scenario.Reader);

        var spanish = Values(await (await reviewer.GetAsync($"/api/forms/documents/{sent.Id}", Ct)).ReadJsonAsync())[deposits.ChoiceId];
        var inEnglish = Values(await (await english.GetAsync($"/api/forms/documents/{sent.Id}", Ct)).ReadJsonAsync())[deposits.ChoiceId];
        var readOnly = Values(await (await reader.GetAsync($"/api/forms/documents/{sent.Id}", Ct)).ReadJsonAsync())[deposits.ChoiceId];

        Assert.Empty(Options(spanish));
        Assert.Equal(("missingData", "Zona"), Unavailable(spanish));
        Assert.Equal(("missingData", "Zone"), Unavailable(inEnglish));
        // Whoever cannot act gets neither the options nor why.
        Assert.Empty(Options(readOnly));
        Assert.Equal(JsonValueKind.Null, readOnly.GetProperty("optionsUnavailable").ValueKind);
    }

    [Fact]
    public async Task A_list_of_more_than_two_hundred_options_is_searched_in_the_body_up_to_fifty_at_a_time()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var purchases = await scenario.SuppliersAsync(201, Ct);
        var sent = await scenario.SendAsync(Ct, proposal: scenario.CatalogProposal(purchases));
        using var reviewer = scenario.Client(scenario.Reviewer);
        var path = $"/api/forms/documents/{sent.Id}/options";

        var supplier = Values(await (await reviewer.GetAsync($"/api/forms/documents/{sent.Id}", Ct)).ReadJsonAsync())[purchases.ChoiceId];
        using var hundreds = await reviewer.PostAsJsonAsync(path, new { fieldId = purchases.ChoiceId, search = "Proveedor 1" }, Ct);
        using var last = await reviewer.PostAsJsonAsync(path, new { fieldId = purchases.ChoiceId, search = "proveedor 20" }, Ct);
        using var first = await reviewer.PostAsJsonAsync(path, new { fieldId = purchases.ChoiceId }, Ct);

        // More than 200 for the sender: no options, and why; the reviewer searches the list.
        Assert.Empty(Options(supplier));
        Assert.Equal(("tooMany", null), Unavailable(supplier));
        Assert.False(supplier.GetProperty("optionsUnavailable").TryGetProperty("datum", out _));
        Assert.Equal(HttpStatusCode.OK, hundreds.StatusCode);
        Assert.True(hundreds.Headers.CacheControl!.NoStore);
        await FormReceivedDocumentContractCapture.WriteAsync(hundreds, "document-options-es", Ct);
        var page = await hundreds.ReadJsonAsync();
        var items = page.GetProperty("items").EnumerateArray().Select(item => (Id: item.GetProperty("id").GetGuid(), Text: item.GetProperty("text").GetString(),
            Group: item.GetProperty("group").GetString())).ToArray();
        Assert.Equal(purchases.ItemIds.Skip(99).Take(50), items.Select(item => item.Id));
        Assert.All(items, item => Assert.Null(item.Group));
        Assert.Equal(("Proveedor 100", "Proveedor 149"), (items[0].Text, items[^1].Text));
        Assert.True(page.GetProperty("truncated").GetBoolean());
        var ending = await last.ReadJsonAsync();
        Assert.Equal(["Proveedor 200", "Proveedor 201"], ending.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("text").GetString()));
        Assert.False(ending.GetProperty("truncated").GetBoolean());
        var start = await first.ReadJsonAsync();
        Assert.Equal((50, true), (start.GetProperty("items").GetArrayLength(), start.GetProperty("truncated").GetBoolean()));

        // Only a reviewer; only a catalog choice of the document's form; a closed body, with the text never in the address; only while
        // someone can act on the document.
        using var readerClient = scenario.Client(scenario.Reader);
        using var denied = await readerClient.PostAsJsonAsync(path, new { fieldId = purchases.ChoiceId }, Ct);
        using var amount = await reviewer.PostAsJsonAsync(path, new { fieldId = purchases.AmountId }, Ct);
        using var unknown = await reviewer.PostAsJsonAsync(path, new { fieldId = Guid.NewGuid() }, Ct);
        using var missing = await reviewer.PostAsJsonAsync(path, new { search = "Proveedor" }, Ct);
        using var unknownMember = await reviewer.PostAsJsonAsync(path, new { fieldId = purchases.ChoiceId, page = 2 }, Ct);
        using var tooLong = await reviewer.PostAsJsonAsync(path, new { fieldId = purchases.ChoiceId, search = new string('a', 101) }, Ct);
        using var noDocument = await reviewer.PostAsJsonAsync($"/api/forms/documents/{Guid.NewGuid()}/options", new { fieldId = purchases.ChoiceId }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal((HttpStatusCode.NotFound, "Forms.Entry.QuestionNotFound"), (amount.StatusCode, (await ProblemAsync(amount)).Code));
        Assert.Equal((HttpStatusCode.NotFound, "Forms.Entry.QuestionNotFound"), (unknown.StatusCode, (await ProblemAsync(unknown)).Code));
        await AssertFieldErrorAsync(missing, "Forms.ReceivedDocument.ContentInvalid", "fieldId");
        Assert.Equal(HttpStatusCode.BadRequest, unknownMember.StatusCode);
        await AssertFieldErrorAsync(tooLong, "Forms.ReceivedDocument.ContentInvalid", "search");
        Assert.Equal((HttpStatusCode.NotFound, "Forms.ReceivedDocument.NotFound"), (noDocument.StatusCode, (await ProblemAsync(noDocument)).Code));
        using var discarded = await reviewer.PostAsJsonAsync($"/api/forms/documents/{sent.Id}/discard", new { expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, discarded.StatusCode);
        using var late = await reviewer.PostAsJsonAsync(path, new { fieldId = purchases.ChoiceId }, Ct);
        Assert.Equal((HttpStatusCode.Conflict, "Forms.ReceivedDocument.StateConflict"), (late.StatusCode, (await ProblemAsync(late)).Code));

        // Its OpenAPI: a body, the page of options and only the errors it can answer.
        var operation = (await OpenApiTests.ReadDocumentAsync(factory)).GetProperty("paths").GetProperty("/api/forms/documents/{id}/options").GetProperty("post");
        Assert.True(operation.TryGetProperty("requestBody", out _));
        var responses = operation.GetProperty("responses").EnumerateObject().Select(response => response.Name).ToArray();
        Assert.Contains("200", responses);
        Assert.Equal(["400", "401", "403", "404", "409", "429", "500"], responses.Where(name => name[0] is '4' or '5').Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Only_the_current_form_of_a_proposed_document_offers_options_and_has_them_searched()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var first = await scenario.ChoicesAsync(Ct);
        var second = await scenario.ChoicesAsync(Ct);
        // A doubt between two forms: proposed for the first one, the only one it can be passed to.
        var sent = await scenario.SendAsync(Ct, proposal: scenario.ChoicesDoubtProposal(first, second));
        using var reviewer = scenario.Client(scenario.Reviewer);
        var path = $"/api/forms/documents/{sent.Id}";

        var proposed = Candidates(await (await reviewer.GetAsync(path, Ct)).ReadJsonAsync());
        using var current = await reviewer.PostAsJsonAsync(path + "/options", new { fieldId = first.BankId }, Ct);
        using var otherCandidate = await reviewer.PostAsJsonAsync(path + "/options", new { fieldId = second.BankId }, Ct);
        var choice = new StructuredDocumentChoiceRequest(sent.Id, FormReceivedDocumentScenario.Channel, sent.Request.SourceId, scenario.Sender, "es",
            factory.Clock.GetUtcNow().UtcDateTime);
        Assert.Equal(StructuredDocumentChoiceState.Recorded, await scenario.PortAsync(port => port.ChooseOtherAsync(choice, Ct)));
        var unclassified = Candidates(await (await reviewer.GetAsync(path, Ct)).ReadJsonAsync());
        using var afterOther = await reviewer.PostAsJsonAsync(path + "/options", new { fieldId = first.BankId }, Ct);

        Assert.Equal(2, Options(proposed[first.DefinitionId][first.KindId]).Length);
        Assert.Equal([first.GaliciaId, first.NacionId], Options(proposed[first.DefinitionId][first.BankId]).Select(option => option.Id));
        // The other candidate offers nothing, and says no reason: nobody can pass the document to it.
        Assert.All([second.KindId, second.BankId], fieldId => Assert.Empty(Options(proposed[second.DefinitionId][fieldId])));
        Assert.Equal(JsonValueKind.Null, proposed[second.DefinitionId][second.BankId].GetProperty("optionsUnavailable").ValueKind);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(2, (await current.ReadJsonAsync()).GetProperty("items").GetArrayLength());
        Assert.Equal((HttpStatusCode.NotFound, "Forms.Entry.QuestionNotFound"), (otherCandidate.StatusCode, (await ProblemAsync(otherCandidate)).Code));
        // Its person chose «Otro»: unclassified, it is passed to no form, so no candidate offers options and none is searched.
        Assert.All(unclassified.Values.SelectMany(values => values.Values).Where(value => value.GetProperty("type").GetString() == "choice"), value =>
        {
            Assert.Empty(Options(value));
            Assert.Equal(JsonValueKind.Null, value.GetProperty("optionsUnavailable").ValueKind);
        });
        Assert.Equal((HttpStatusCode.Conflict, "Forms.ReceivedDocument.StateConflict"), (afterOther.StatusCode, (await ProblemAsync(afterOther)).Code));
    }

    [Fact]
    public async Task A_chosen_sender_without_documents_takes_the_last_place_of_a_full_list_and_marks_the_cut()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        // Exactly one hundred people sent a document, in a minute of their own.
        var arrived = factory.Clock.GetUtcNow().UtcDateTime.AddDays(-400).AddSeconds(-Random.Shared.Next(1, 30_000_000));
        await scenario.SeedDocumentsAsync(Enumerable.Range(0, 100).Select(_ => (Guid.CreateVersion7(), (FormReceivedDocumentScenarioForm?)null)).ToArray(), Ct, arrived);
        using var reviewer = scenario.Client(scenario.Reviewer);
        var minute = new { receivedFromUtc = arrived, receivedToUtc = arrived.AddMinutes(1) };

        var whole = await PageAsync(reviewer, minute);
        var chosen = await PageAsync(reviewer, new { minute.receivedFromUtc, minute.receivedToUtc, senderUserId = scenario.Reader });

        // The hundred fit: nothing is cut.
        Assert.Equal((100, false), (Senders(whole).Length, Truncated(whole).Senders));
        // Someone chosen without documents takes the last place, so one of the hundred is left out and the list says so.
        Assert.Equal(0, chosen.GetProperty("totalCount").GetInt32());
        Assert.Equal(100, Senders(chosen).Length);
        Assert.Equal((scenario.Reader, "Luis Lector", 0), Senders(chosen)[^1]);
        Assert.True(Truncated(chosen).Senders);
    }

    [Fact]
    public async Task An_engine_that_refuses_an_extraction_for_good_drops_it_and_a_transient_failure_keeps_it()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var sent = await scenario.SendAsync(Ct);
        using var reviewer = scenario.Client(scenario.Reviewer);
        var path = $"/api/forms/documents/{sent.Id}";
        var requestId = Guid.NewGuid();
        using var accepted = await reviewer.PutAsJsonAsync(path + "/form", new { requestId, definitionId = scenario.Other.DefinitionId, expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

        // A transient failure (unavailable, a network error, 429): the document keeps reading.
        scenario.Engine.ReadFailure = FormReceivedDocumentErrors.ExtractionUnavailable;
        using var transient = await reviewer.GetAsync($"{path}/extraction?requestId={requestId}", Ct);
        var reading = await (await reviewer.GetAsync(path, Ct)).ReadJsonAsync();
        // The engine refuses it for good: the request is dropped, as a failure of whoever asked, and the refusal is answered.
        scenario.Engine.ReadFailure = FormDefinitionErrors.Forbidden;
        using var refused = await reviewer.GetAsync($"{path}/extraction?requestId={requestId}", Ct);
        var dropped = await (await reviewer.GetAsync(path, Ct)).ReadJsonAsync();

        Assert.Equal((HttpStatusCode.InternalServerError, "Forms.ReceivedDocument.ExtractionUnavailable"), (transient.StatusCode, (await ProblemAsync(transient)).Code));
        Assert.Equal((requestId, 2L), (reading.GetProperty("extraction").GetProperty("requestId").GetGuid(), reading.GetProperty("document").GetProperty("revision").GetInt64()));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal((JsonValueKind.Null, 3L), (dropped.GetProperty("extraction").ValueKind, dropped.GetProperty("document").GetProperty("revision").GetInt64()));
        var step = dropped.GetProperty("history").EnumerateArray().Last();
        Assert.Equal(("extractionFailed", FormReceivedDocumentExtractionPolicy.Refused, scenario.Reviewer), (step.GetProperty("kind").GetString(),
            step.GetProperty("detail").GetString(), step.GetProperty("actor").GetProperty("userId").GetGuid()));
        // The document can be read for another form again.
        scenario.Engine.ReadFailure = null;
        using var again = await reviewer.PutAsJsonAsync(path + "/form", new { requestId = Guid.NewGuid(), definitionId = scenario.Other.DefinitionId, expectedRevision = 3 }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
    }

    [Fact]
    public async Task The_filters_and_the_forms_to_change_to_are_capped_say_when_they_cut_and_keep_what_is_chosen()
    {
        // The forms to change to are all the forms with reading: a database of its own.
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct, isolated: true);
        // Five forms with their reading off, older than the rest: never offered, and they take no place in the cap.
        var off = new HashSet<Guid>();
        for (var index = 0; index < 5; index++)
            off.Add((await scenario.AddFormAsync($"Apagado {index}", $"Off {index}", Ct, reading: false)).DefinitionId);
        var forms = new List<FormReceivedDocumentScenarioForm>();
        for (var index = 0; index < 101; index++) forms.Add(await scenario.AddFormAsync($"Lee {index}", $"Reads {index}", Ct));
        // A hundred people with two documents each, each pair for a form of its own, and one more person with one, for one more form.
        var senders = Enumerable.Range(0, 101).Select(_ => Guid.CreateVersion7()).ToArray();
        await scenario.SeedDocumentsAsync(senders.SelectMany((sender, index) =>
            Enumerable.Repeat<(Guid, FormReceivedDocumentScenarioForm?)>((sender, forms[index]), index < 100 ? 2 : 1)).ToArray(), Ct);
        using var reviewer = scenario.Client(scenario.Reviewer);

        var all = await PageAsync(reviewer, new { });
        var byForm = await PageAsync(reviewer, new { definitionId = forms[100].DefinitionId });
        var bySender = await PageAsync(reviewer, new { senderUserId = senders[100] });

        // Each list has at most 100, the most first, and says it cut; the one cut comes back when it is the one chosen, with its count.
        Assert.Equal(201, all.GetProperty("totalCount").GetInt32());
        Assert.Equal((100, 100), (Forms(all).Length, Senders(all).Length));
        Assert.Equal((true, true), Truncated(all));
        Assert.DoesNotContain(Forms(all), form => form.DefinitionId == forms[100].DefinitionId);
        Assert.DoesNotContain(Senders(all), sender => sender.UserId == senders[100]);
        Assert.Equal(1, byForm.GetProperty("totalCount").GetInt32());
        Assert.Equal(100, Forms(byForm).Length);
        Assert.Equal((forms[100].DefinitionId, forms[100].Name, 1), Forms(byForm)[^1]);
        Assert.True(Truncated(byForm).Forms);
        Assert.Equal(100, Senders(bySender).Length);
        Assert.Equal((senders[100], null, 1), Senders(bySender)[^1]);
        Assert.True(Truncated(bySender).Senders);

        // The forms a reviewer can change a document to: the first 100 with their reading on, never one with it off.
        var sent = await scenario.SendAsync(Ct);
        var changeTo = FormsToChange(await (await reviewer.GetAsync($"/api/forms/documents/{sent.Id}", Ct)).ReadJsonAsync());
        Assert.Equal(100, changeTo.Length);
        Assert.Contains((scenario.Form.DefinitionId, scenario.Form.Name), changeTo);
        Assert.DoesNotContain(changeTo, form => off.Contains(form.DefinitionId) || form.DefinitionId == scenario.Other.DefinitionId);
    }

    [Fact]
    public async Task The_form_filter_names_each_form_once_by_its_active_version_or_by_its_documents_when_it_was_deleted()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var arrived = factory.Clock.GetUtcNow().UtcDateTime.AddDays(-400).AddSeconds(-Random.Shared.Next(1, 30_000_000));
        var old = await scenario.AddFormAsync("Viejo", "Old", Ct);
        await scenario.SendAsync(Ct, receivedAtUtc: arrived);
        await scenario.SendAsync(Ct, candidates: [old], receivedAtUtc: arrived.AddSeconds(1));
        var minute = new { receivedFromUtc = arrived, receivedToUtc = arrived.AddMinutes(1) };
        using var reviewer = scenario.Client(scenario.Reviewer);
        // A newer version being published does not rename the form until it is the active one.
        var renamed = await scenario.SealVersionAsync(scenario.Form, "Pagos nuevos", "New payments", Ct);

        var sealedOnly = Forms(await PageAsync(reviewer, minute));
        await scenario.ActivateVersionAsync(scenario.Form.DefinitionId, renamed, Ct);
        await scenario.SendAsync(Ct, candidates: [scenario.Form with { VersionId = renamed }], receivedAtUtc: arrived.AddSeconds(2));
        var activated = Forms(await PageAsync(reviewer, minute));
        await scenario.DeleteFormAsync(old.DefinitionId, Ct);
        var deleted = Forms(await PageAsync(reviewer, minute));

        Assert.Equal(new (Guid, string?, int)[] { (scenario.Form.DefinitionId, scenario.Form.Name, 1), (old.DefinitionId, old.Name, 1) }.Order(), sealedOnly.Order());
        // One row for the form, with the documents of both versions, by the name of the active one.
        var newName = scenario.Form.Prefix + " Pagos nuevos";
        Assert.Equal([(scenario.Form.DefinitionId, newName, 2), (old.DefinitionId, old.Name, 1)], activated);
        // A deleted form is still named, by the version of its documents.
        Assert.Equal([(scenario.Form.DefinitionId, newName, 2), (old.DefinitionId, old.Name, 1)], deleted);
    }

    [Fact]
    public async Task A_catalog_option_is_found_by_the_value_of_one_of_its_fields()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var catalog = await scenario.CatalogAsync("Entidades", "Código", [("Banco Siete", "007"), ("Banco Ocho", "008"), ("Banco sin código", null)], Ct);
        var field = catalog.FieldId.ToString("D");

        var found = await scenario.ScopeAsync(services => services.GetRequiredService<IFormReceivedDocumentReader>().FindCatalogItemAsync(catalog.CatalogId, field, " 008 ", Ct));
        var missing = await scenario.ScopeAsync(services => services.GetRequiredService<IFormReceivedDocumentReader>().FindCatalogItemAsync(catalog.CatalogId, field, "009", Ct));

        Assert.Equal((catalog.ItemIds[1], "Banco Ocho"), (found?.ItemId, found?.TextEs));
        Assert.Null(missing);
    }

    private const string SearchPath = "/api/forms/documents/search";

    private static async Task<IReadOnlyList<Guid>> IdsAsync(HttpClient client, object body) =>
        (await PageAsync(client, body)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();

    private static async Task<JsonElement> PageAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync(SearchPath, body, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static (int Proposed, int ForReview, int Unclassified, int Confirmed, int Discarded) Counts(JsonElement page)
    {
        var counts = page.GetProperty("counts");
        return (counts.GetProperty("proposed").GetInt32(), counts.GetProperty("forReview").GetInt32(), counts.GetProperty("unclassified").GetInt32(),
            counts.GetProperty("confirmed").GetInt32(), counts.GetProperty("discarded").GetInt32());
    }

    private static (Guid DefinitionId, string? Name, int Count)[] Forms(JsonElement page) =>
        page.GetProperty("facets").GetProperty("forms").EnumerateArray().Select(form => (form.GetProperty("definitionId").GetGuid(),
            form.GetProperty("name").GetString(), form.GetProperty("count").GetInt32())).ToArray();

    private static (Guid UserId, string? DisplayName, int Count)[] Senders(JsonElement page) =>
        page.GetProperty("facets").GetProperty("senders").EnumerateArray().Select(sender => (sender.GetProperty("userId").GetGuid(),
            sender.GetProperty("displayName").GetString(), sender.GetProperty("count").GetInt32())).ToArray();

    private static (bool Forms, bool Senders) Truncated(JsonElement page) =>
        (page.GetProperty("facets").GetProperty("formsTruncated").GetBoolean(), page.GetProperty("facets").GetProperty("sendersTruncated").GetBoolean());

    // The forms a reviewer can change a document to, from its detail.
    private static (Guid DefinitionId, string? Name)[] FormsToChange(JsonElement detail) =>
        detail.GetProperty("forms").EnumerateArray().Select(form => (form.GetProperty("definitionId").GetGuid(), form.GetProperty("name").GetString())).ToArray();

    // The values of each candidate of the detail, by form and question.
    private static Dictionary<Guid, Dictionary<Guid, JsonElement>> Candidates(JsonElement detail) =>
        detail.GetProperty("proposal").GetProperty("candidates").EnumerateArray().ToDictionary(candidate => candidate.GetProperty("definitionId").GetGuid(),
            candidate => candidate.GetProperty("values").EnumerateArray().ToDictionary(value => value.GetProperty("fieldId").GetGuid()));

    // The values of the detail's only candidate, by question.
    private static Dictionary<Guid, JsonElement> Values(JsonElement detail) =>
        Assert.Single(detail.GetProperty("proposal").GetProperty("candidates").EnumerateArray()).GetProperty("values").EnumerateArray()
            .ToDictionary(value => value.GetProperty("fieldId").GetGuid());

    // Why a choice lists no options: the reason and the datum, when there is one.
    private static (string? Reason, string? Datum) Unavailable(JsonElement value)
    {
        var unavailable = value.GetProperty("optionsUnavailable");
        return (unavailable.GetProperty("reason").GetString(), unavailable.TryGetProperty("datum", out var datum) ? datum.GetString() : null);
    }

    // Each option with its group, which is always there (null when the list is not chosen first by a field).
    private static (Guid Id, string? Text, string? Group)[] Options(JsonElement value) =>
        value.GetProperty("options").EnumerateArray().Select(option => (option.GetProperty("id").GetGuid(), option.GetProperty("text").GetString(),
            option.GetProperty("group").GetString())).ToArray();

    private static async Task AssertFieldErrorAsync(HttpResponseMessage response, string code, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    private static async Task<IReadOnlyList<string?>> HistoryAsync(HttpClient client, Guid documentId)
    {
        using var response = await client.GetAsync($"/api/forms/documents/{documentId}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("history").EnumerateArray().Select(step => step.GetProperty("kind").GetString()).ToArray();
    }

    private static async Task<(string? Code, string? Detail)> ProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.ReadJsonAsync();
        return (problem.GetProperty("code").GetString(), problem.GetProperty("detail").GetString());
    }

    private static async Task<(Guid RequestId, string? State)> ExtractionAsync(HttpResponseMessage response)
    {
        var body = await response.ReadJsonAsync();
        return (body.GetProperty("requestId").GetGuid(), body.GetProperty("state").GetString());
    }
}
