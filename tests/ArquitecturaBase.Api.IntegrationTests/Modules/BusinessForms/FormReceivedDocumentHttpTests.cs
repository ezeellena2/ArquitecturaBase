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
// whoever sent it, and the purge. No provider is called.
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
    public async Task The_inbox_lists_filters_searches_and_counts_only_with_its_permission()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        using var reviewer = scenario.Client(scenario.Reviewer);
        using var before = await reviewer.GetAsync("/api/forms/documents/counts", Ct);
        var initial = await before.ReadJsonAsync();
        var proposed = await scenario.SendAsync(Ct, amount: "250000", operation: "445566");
        var waiting = await scenario.SendAsync(Ct, amount: "99000", operation: "112233", replyExpiresAtUtc: factory.Clock.GetUtcNow().UtcDateTime.AddDays(1));
        var quota = await scenario.SendAsync(Ct, notRead: StructuredDocumentNotRead.Quota);
        var rejected = await scenario.SendAsync(Ct, notRead: StructuredDocumentNotRead.Rejected);
        var mine = $"senderUserId={scenario.Sender}";

        using var anonymous = scenario.Host.CreateClient();
        using var sender = scenario.Client(scenario.Sender);
        using var reader = scenario.Client(scenario.Reader);
        using var missing = await anonymous.GetAsync("/api/forms/documents", Ct);
        using var denied = await sender.GetAsync("/api/forms/documents", Ct);
        using var deniedCounts = await sender.GetAsync("/api/forms/documents/counts", Ct);
        using var all = await reader.GetAsync($"/api/forms/documents?{mine}", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedCounts.StatusCode);
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        Assert.True(all.Headers.CacheControl!.NoStore);
        await FormReceivedDocumentContractCapture.WriteAsync(all, "documents-list-es", Ct);
        var page = await all.ReadJsonAsync();
        Assert.Equal(4, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(new HashSet<Guid> { proposed.Id, waiting.Id, quota.Id, rejected.Id },
            page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToHashSet());
        var counts = page.GetProperty("counts");
        Assert.Equal((2, 1, 1, 0, 1), (counts.GetProperty("proposed").GetInt32(), counts.GetProperty("forReview").GetInt32(),
            counts.GetProperty("unclassified").GetInt32(), counts.GetProperty("confirmed").GetInt32(), counts.GetProperty("discarded").GetInt32()));
        var item = page.GetProperty("items").EnumerateArray().Single(value => value.GetProperty("id").GetGuid() == proposed.Id);
        Assert.Equal(("proposed", true, "Juan Remitente", scenario.Form.Name), (item.GetProperty("state").GetString(), item.GetProperty("forReview").GetBoolean(),
            item.GetProperty("sender").GetProperty("displayName").GetString(), item.GetProperty("form").GetProperty("name").GetString()));
        var highlight = Assert.Single(item.GetProperty("highlights").EnumerateArray());
        Assert.Equal(("Monto", "250.000"), (highlight.GetProperty("label").GetString(), highlight.GetProperty("value").GetString()));

        Assert.Equal([proposed.Id], await IdsAsync(reviewer, $"{mine}&state=forReview"));
        Assert.Equal([proposed.Id, waiting.Id], (await IdsAsync(reviewer, $"{mine}&state=proposed")).Order());
        Assert.Equal([quota.Id], await IdsAsync(reviewer, $"{mine}&state=unclassified"));
        Assert.Equal([rejected.Id], await IdsAsync(reviewer, $"{mine}&state=discarded"));
        Assert.Empty(await IdsAsync(reviewer, $"{mine}&state=confirmed"));
        Assert.Equal([proposed.Id, waiting.Id], (await IdsAsync(reviewer, $"definitionId={scenario.Form.DefinitionId}")).Order());
        // The search is the digits typed, as the beginning of a typed number: «250.0» finds the amount 250000; the operation of a form that
        // is not a standard's copy is free text, not kept, and neither is the date of the reading.
        Assert.Equal([proposed.Id], await IdsAsync(reviewer, $"{mine}&search=250.0"));
        Assert.Equal([proposed.Id], await IdsAsync(reviewer, $"{mine}&search=250000"));
        Assert.Empty(await IdsAsync(reviewer, $"{mine}&search=445566"));
        Assert.Empty(await IdsAsync(reviewer, $"{mine}&search=11112026"));
        var arrived = Uri.EscapeDataString(factory.Clock.GetUtcNow().UtcDateTime.ToString("O"));
        Assert.Empty(await IdsAsync(reviewer, $"{mine}&receivedToUtc={arrived}"));
        Assert.Equal(4, (await IdsAsync(reviewer, $"{mine}&receivedFromUtc={arrived}")).Count);

        using var letters = await reviewer.GetAsync($"/api/forms/documents?{mine}&search=abc", Ct);
        using var state = await reviewer.GetAsync($"/api/forms/documents?{mine}&state=archived", Ct);
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
        Assert.Equal(("comprobante.pdf", "application/pdf", sent.Content.LongLength, 1), (file.GetProperty("name").GetString(),
            file.GetProperty("mediaType").GetString(), file.GetProperty("size").GetInt64(), file.GetProperty("pages").GetInt32()));
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
        Assert.Equal(JsonValueKind.Null, body.GetProperty("extraction").ValueKind);
        var received = Assert.Single(body.GetProperty("history").EnumerateArray());
        Assert.Equal(("received", "sure", JsonValueKind.Null), (received.GetProperty("kind").GetString(), received.GetProperty("detail").GetString(),
            received.GetProperty("actor").ValueKind));
        Assert.True(body.GetProperty("canReview").GetBoolean());
        var bodyEn = await detailEn.ReadJsonAsync();
        var highlightEn = Assert.Single(bodyEn.GetProperty("document").GetProperty("highlights").EnumerateArray());
        Assert.Equal(("Amount", "150,000"), (highlightEn.GetProperty("label").GetString(), highlightEn.GetProperty("value").GetString()));
        Assert.False((await readOnly.ReadJsonAsync()).GetProperty("canReview").GetBoolean());
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
        var colleague = await FormDashboardScenario.PersonAsync(factory, "Eva Colega",
            [BusinessFormsPermissions.DocumentsRead, BusinessFormsPermissions.DocumentsReview], ct: Ct);
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
        Assert.Equal(scenario.Other.Name, changed.GetProperty("history").EnumerateArray().Last().GetProperty("detail").GetString());
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
        using var unrecognized = await reviewer.GetAsync($"{path}/extraction?requestId={unrecognizedId}", Ct);
        Assert.Equal(HttpStatusCode.OK, unrecognized.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(unrecognized, "document-extraction-failed-es", Ct);
        var failure = await unrecognized.ReadJsonAsync();
        Assert.Equal(("failed", FormReceivedDocumentExtractionPolicy.Unrecognized), (failure.GetProperty("state").GetString(), failure.GetProperty("failureCode").GetString()));

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
        // The response says where it came from: the document, passed by the reviewer.
        using var detail = await reviewer.GetAsync($"/api/forms/records/{recordId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var origin = (await detail.ReadJsonAsync()).GetProperty("origin");
        Assert.Equal((FormReceivedDocumentCapturePolicy.SourceId, "Ana Revisora"), (origin.GetProperty("channelId").GetString(), origin.GetProperty("senderName").GetString()));

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
        Assert.Equal([other.Id], await IdsAsync(reviewer, $"senderUserId={scenario.Sender}&search=4455"));
        Assert.Equal([repeated.Id], await IdsAsync(reviewer, $"senderUserId={scenario.Sender}&search=0011&state=discarded"));

        // A response of that other operation appears later: a repeated registration answers what was decided, never classified again.
        await scenario.RespondAsync(transfer, "44556677", 150000m, new DateOnly(2026, 11, 11), Ct);
        var replayed = await scenario.PortAsync(port => port.RegisterAsync(other.Request, Ct));
        Assert.Equal((other.Id, StructuredDocumentReplyKind.Sure, true), (replayed.DocumentId!.Value, replayed.Kind, replayed.WasAlreadyRegistered));
        Assert.Equal(transfer.DefinitionId, Assert.Single(replayed.Forms).DefinitionId);
    }

    private static async Task<IReadOnlyList<Guid>> IdsAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync("/api/forms/documents?" + query, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();
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
