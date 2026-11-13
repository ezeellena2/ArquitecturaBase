using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// Task 14 (DOC6) with PostgreSQL and a scripted engine: «Armar formulario con la IA» asked from one unclassified document or from the group
// of a topic (the notice of similar documents in the inbox's page), read like a dictation, «Crear borrador» from its proposal as the
// reviewer edited it, and the reading of its documents again once that form is published. No provider is called.
[Collection(ApiTestGroup.Name)]
public sealed class FormDocumentProposalHttpTests(ApiFactory factory)
{
    private const string Proposals = "/api/forms/documents/form-proposals";
    private const string Drafts = "/api/forms/definitions/document-proposals";
    private static readonly string[] Designer = [BusinessFormsPermissions.DocumentsRead, BusinessFormsPermissions.DocumentsReview,
        BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.CatalogsRead];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Asking_from_one_unclassified_document_submits_the_documents_channel_and_is_read_like_a_dictation()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var designer = await scenario.PersonAsync("Dora Diseña", Designer, Ct);
        var colleague = await scenario.PersonAsync("Eva Colega", Designer, Ct);
        var sent = await UnclassifiedAsync(scenario, Topic(), Ct);
        var proposed = await scenario.SendAsync(Ct);
        using var reviewer = scenario.Client(scenario.Reviewer);
        using var client = scenario.Client(designer);
        var requestId = Guid.NewGuid();

        // «Revisar documentos» alone does not build forms; one body asks for one document or one group, and only an unclassified one.
        using var denied = await reviewer.PostAsJsonAsync(Proposals, new { requestId, documentId = sent.Id }, Ct);
        using var both = await client.PostAsJsonAsync(Proposals, new { requestId, documentId = sent.Id, topicId = Guid.NewGuid() }, Ct);
        using var notUnclassified = await client.PostAsJsonAsync(Proposals, new { requestId, documentId = proposed.Id }, Ct);
        using var accepted = await client.PostAsJsonAsync(Proposals, new { requestId, documentId = sent.Id }, Ct);
        using var repeated = await client.PostAsJsonAsync(Proposals, new { requestId, documentId = sent.Id }, Ct);
        using var reused = await client.PostAsJsonAsync(Proposals, new { requestId, topicId = Guid.NewGuid() }, Ct);
        using var pending = await client.GetAsync($"{Proposals}/{requestId}", Ct);
        using var unknown = await client.GetAsync($"{Proposals}/{Guid.NewGuid()}", Ct);
        using var stranger = await scenario.Client(colleague).GetAsync($"{Proposals}/{requestId}", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.True((await both.ReadJsonAsync()).GetProperty("errors").TryGetProperty("topicId", out _));
        Assert.Equal((HttpStatusCode.Conflict, "Forms.ReceivedDocument.StateConflict"), (notUnclassified.StatusCode, await CodeAsync(notUnclassified)));
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(accepted, "form-proposal-accepted-es", Ct);
        var body = await accepted.ReadJsonAsync();
        Assert.Equal((requestId, "pending", JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null),
            (body.GetProperty("requestId").GetGuid(), body.GetProperty("state").GetString(), body.GetProperty("failureCode").ValueKind,
                body.GetProperty("topic").ValueKind, body.GetProperty("proposal").ValueKind, body.GetProperty("definitionId").ValueKind));
        var document = Assert.Single(body.GetProperty("documents").EnumerateArray());
        Assert.Equal((sent.Id, "unclassified"), (document.GetProperty("id").GetGuid(), document.GetProperty("state").GetString()));
        Assert.Equal(HttpStatusCode.Accepted, repeated.StatusCode);
        Assert.Equal((HttpStatusCode.Conflict, "Forms.ReceivedDocument.RequestConflict"), (reused.StatusCode, await CodeAsync(reused)));
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(pending, "form-proposal-pending-es", Ct);
        Assert.Equal("pending", (await pending.ReadJsonAsync()).GetProperty("state").GetString());
        Assert.Equal((HttpStatusCode.NotFound, "Forms.ReceivedDocument.ProposalNotFound"), (unknown.StatusCode, await CodeAsync(unknown)));
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        // One submission: an interpretation of forms-documents, the designer as Actor without a Subject, in the request's own conversation,
        // with the authoring reads, the library and propose-form@2, and the document's reading as the conversation's document.
        var operation = Assert.Single(scenario.Engine.Submitted);
        Assert.Equal((requestId, "forms-documents", designer, (Guid?)null, (Guid?)requestId, (Guid?)null), (operation.RequestId, operation.Context.ChannelId,
            operation.Context.ActorUserId, operation.Context.SubjectUserId, operation.Context.ChannelConversationId, operation.Context.FocusDefinitionId));
        Assert.Equal(["forms.authoring.forms", "forms.authoring.catalogs", "forms.library.standards", "forms.authoring.propose-form"],
            operation.Context.AllowedCapabilities.Select(capability => capability.Id));
        Assert.Equal([new AssistantTerminalReference("assistant_proposal", 1)], operation.Context.AllowedTerminals);
        Assert.Equal((AssistantAttachmentKind.Document, FormReceivedDocumentCopyPolicy.Key(sent.Id, "application/pdf")),
            (operation.Attachment!.Kind, operation.Attachment.StorageKey));
        Assert.Contains("778899", operation.Attachment.Document!.Reading!.Text, StringComparison.Ordinal);
        Assert.Null(operation.Attachment.MoreDocuments);

        var observed = FormProposal();
        scenario.Engine.Complete(designer, new(requestId, "completed", observed, Provenance(), null) { Proposals = [observed] });
        using var completed = await client.GetAsync($"{Proposals}/{requestId}", Ct);

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(completed, "form-proposal-completed-es", Ct);
        var shown = (await completed.ReadJsonAsync()).GetProperty("proposal");
        Assert.Equal((observed.Id, "forms.authoring.propose-form", 2), (shown.GetProperty("id").GetGuid(), shown.GetProperty("capability").GetString(),
            shown.GetProperty("version").GetInt32()));
        var content = shown.GetProperty("content");
        Assert.Equal(("Tickets de combustible", "Un comprobante de pago.", "150000"), (content.GetProperty("name").GetString(),
            content.GetProperty("documentReading").GetProperty("recognition").GetString(), content.GetProperty("questions")[0].GetProperty("sample").GetString()));
        // Every read used the context it was asked with.
        Assert.All(scenario.Engine.Read, read => Assert.Equal(("forms-documents", designer, "es"), (read.ChannelId, read.ActorUserId, read.Culture)));
    }

    [Fact]
    public async Task An_engine_that_refuses_or_fails_keeps_nothing_and_a_failed_proposal_says_why()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var designer = await scenario.PersonAsync("Dora Diseña", Designer, Ct);
        var sent = await UnclassifiedAsync(scenario, Topic(), Ct);
        var unread = await scenario.SendAsync(Ct, notRead: StructuredDocumentNotRead.Quota);
        using var client = scenario.Client(designer);
        var (refusedId, failedId, readId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        scenario.Engine.SubmitFailure = FormDefinitionErrors.Forbidden;
        using var refused = await client.PostAsJsonAsync(Proposals, new { requestId = refusedId, documentId = sent.Id }, Ct);
        scenario.Engine.SubmitFailure = FormReceivedDocumentErrors.ExtractionUnavailable;
        using var unavailable = await client.PostAsJsonAsync(Proposals, new { requestId = failedId, documentId = sent.Id }, Ct);
        scenario.Engine.SubmitFailure = null;
        using var withoutReading = await client.PostAsJsonAsync(Proposals, new { requestId = Guid.NewGuid(), documentId = unread.Id }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal((HttpStatusCode.InternalServerError, "Forms.ReceivedDocument.ProposalUnavailable"), (unavailable.StatusCode, await CodeAsync(unavailable)));
        await FormReceivedDocumentContractCapture.WriteAsync(unavailable, "form-proposal-unavailable-es", Ct);
        Assert.Equal((HttpStatusCode.Conflict, "Forms.ReceivedDocument.ReadingUnavailable"), (withoutReading.StatusCode, await CodeAsync(withoutReading)));
        // Nothing half-made: neither request was kept.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Proposals}/{refusedId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Proposals}/{failedId}", Ct)).StatusCode);
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<FormDocumentProposal>().CountAsync(value => value.ActorUserId == designer, Ct)));

        using (var asked = await client.PostAsJsonAsync(Proposals, new { requestId = readId, documentId = sent.Id }, Ct))
            Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        scenario.Engine.Complete(designer, new(readId, "failed", null, null, "Ai.Assistant.ProviderUnavailable"));
        using var failed = await client.GetAsync($"{Proposals}/{readId}", Ct);

        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(failed, "form-proposal-failed-es", Ct);
        var body = await failed.ReadJsonAsync();
        Assert.Equal(("failed", "Ai.Assistant.ProviderUnavailable", JsonValueKind.Null), (body.GetProperty("state").GetString(),
            body.GetProperty("failureCode").GetString(), body.GetProperty("proposal").ValueKind));
    }

    [Fact]
    public async Task Three_or_more_unclassified_documents_of_a_topic_are_a_group_and_asking_for_it_reads_the_newest_five()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var designer = await scenario.PersonAsync("Dora Diseña", Designer, Ct);
        var topic = Topic();
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var sent = new List<FormReceivedDocumentSent>();
        for (var index = 0; index < 6; index++) sent.Add(await UnclassifiedAsync(scenario, topic, Ct, now.AddMinutes(index - 10)));
        var lonely = Topic();
        for (var index = 0; index < 2; index++) await UnclassifiedAsync(scenario, lonely, Ct, now.AddMinutes(index - 20));
        using var client = scenario.Client(designer);
        var newestFirst = sent.Select(value => value.Id).Reverse().ToArray();

        using var page = await client.PostAsJsonAsync("/api/forms/documents/search", new { senderUserId = scenario.Sender }, Ct);
        using var otherSender = await client.PostAsJsonAsync("/api/forms/documents/search", new { senderUserId = scenario.Reviewer }, Ct);
        using var confirmedOnly = await client.PostAsJsonAsync("/api/forms/documents/search", new { senderUserId = scenario.Sender, state = "confirmed" }, Ct);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(page, "documents-groups-es", Ct);
        // Only the topic with three or more, with how many they are and the newest first; two of another topic are not a group.
        var group = Assert.Single((await page.ReadJsonAsync()).GetProperty("groups").EnumerateArray());
        Assert.Equal((topic, 6), (group.GetProperty("topic").GetProperty("name").GetString(), group.GetProperty("count").GetInt32()));
        Assert.Equal(newestFirst, group.GetProperty("documentIds").EnumerateArray().Select(id => id.GetGuid()));
        // Under the page's other filters, never its state's: another sender has none, and filtering by a state still shows it.
        Assert.Empty((await otherSender.ReadJsonAsync()).GetProperty("groups").EnumerateArray());
        Assert.Single((await confirmedOnly.ReadJsonAsync()).GetProperty("groups").EnumerateArray());

        var topicId = group.GetProperty("topic").GetProperty("id").GetGuid();
        var requestId = Guid.NewGuid();
        using var accepted = await client.PostAsJsonAsync(Proposals, new { requestId, topicId }, Ct);
        using var emptyTopic = await client.PostAsJsonAsync(Proposals, new { requestId = Guid.NewGuid(), topicId = Guid.NewGuid() }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var body = await accepted.ReadJsonAsync();
        Assert.Equal((topicId, topic), (body.GetProperty("topic").GetProperty("id").GetGuid(), body.GetProperty("topic").GetProperty("name").GetString()));
        Assert.Equal(newestFirst, body.GetProperty("documents").EnumerateArray().Select(document => document.GetProperty("id").GetGuid()));
        Assert.Equal((HttpStatusCode.Conflict, "Forms.ReceivedDocument.GroupUnavailable"), (emptyTopic.StatusCode, await CodeAsync(emptyTopic)));
        // The newest one goes with its copy and half of the budget, the next four share the rest; the sixth is a source, not a reading.
        var operation = Assert.Single(scenario.Engine.Submitted);
        Assert.Equal(FormReceivedDocumentCopyPolicy.Key(newestFirst[0], "application/pdf"), operation.Attachment!.StorageKey);
        Assert.Equal(4, operation.Attachment.MoreDocuments!.Count);
        var kept = await factory.ExecuteDbContextAsync(db => db.Set<FormDocumentProposal>().AsNoTracking()
            .SingleAsync(value => value.ActorUserId == designer && value.RequestId == requestId, Ct));
        Assert.Equal(topicId, kept.TopicId);
        Assert.Equal(newestFirst, kept.DocumentIds);
    }

    [Fact]
    public async Task Creating_the_draft_keeps_the_edited_form_with_its_reading_once_and_publishing_it_reads_its_documents_again()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var designer = await scenario.PersonAsync("Dora Diseña", Designer, Ct);
        var topic = Topic();
        var sent = new List<FormReceivedDocumentSent>();
        for (var index = 0; index < 3; index++) sent.Add(await UnclassifiedAsync(scenario, topic, Ct));
        using var client = scenario.Client(designer);
        var topicId = (await GroupsAsync(client, scenario.Sender)).Single().GetProperty("topic").GetProperty("id").GetGuid();
        var requestId = Guid.NewGuid();
        using (var asked = await client.PostAsJsonAsync(Proposals, new { requestId, topicId }, Ct))
            Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        var observed = FormProposal();
        scenario.Engine.Complete(designer, new(requestId, "completed", observed, Provenance(), null) { Proposals = [observed] });
        var name = "Tickets " + Letters();
        var prefix = "T" + Letters().ToUpperInvariant();
        object Body(string recognition) => new
        {
            requestId, proposalId = observed.Id, name, prefix, submitLabel = "Cargar ticket", recognition,
            questions = new object[]
            {
                new { label = "Monto", type = "number", required = true, options = Array.Empty<string>(), catalogId = (Guid?)null },
                new { label = "Operación", type = "shortText", required = false, options = Array.Empty<string>(), catalogId = (Guid?)null },
                new { label = "Comprobante", type = "document", required = false, options = Array.Empty<string>(), catalogId = (Guid?)null },
            },
        };

        using var invalid = await client.PostAsJsonAsync(Drafts, Body(" "), Ct);
        using var created = await client.PostAsJsonAsync(Drafts, Body("Un comprobante de pago de combustible."), Ct);
        using var again = await client.PostAsJsonAsync(Drafts, Body("Un comprobante de pago de combustible."), Ct);
        using var drafted = await client.GetAsync($"{Proposals}/{requestId}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(invalid, "form-proposal-draft-invalid-es", Ct);
        Assert.True((await invalid.ReadJsonAsync()).GetProperty("errors").TryGetProperty("recognition", out _));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await FormReceivedDocumentContractCapture.WriteAsync(created, "form-proposal-draft-created-es", Ct);
        Assert.EndsWith($"{Proposals}/{requestId}", created.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        var definitionId = (await created.ReadJsonAsync()).GetProperty("definitionId").GetGuid();
        Assert.Equal((HttpStatusCode.Created, definitionId), (again.StatusCode, (await again.ReadJsonAsync()).GetProperty("definitionId").GetGuid()));
        await FormReceivedDocumentContractCapture.WriteAsync(drafted, "form-proposal-drafted-es", Ct);
        Assert.Equal(definitionId, (await drafted.ReadJsonAsync()).GetProperty("definitionId").GetGuid());
        // The draft has its reading on and the proposal's provenance as evidence; the request records it, and its group is no longer offered.
        var definition = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().AsNoTracking().SingleAsync(value => value.Id == definitionId, Ct));
        var reading = JsonSerializer.Deserialize<FormDocumentReading>(definition.DocumentReadingJson!, JsonSerializerOptions.Web)!;
        Assert.Equal((true, "Un comprobante de pago de combustible.", observed.Id), (reading.Enabled, reading.Recognition, definition.ProposalId!.Value));
        using (var evidence = JsonDocument.Parse(definition.ProposalEvidenceJson!))
            Assert.Equal((FormDraftProposalService.DocumentsRequestedIn, true), (evidence.RootElement.GetProperty("requestedIn").GetString(),
                evidence.RootElement.GetProperty("documentReading").GetBoolean()));
        Assert.Equal(definitionId, (await factory.ExecuteDbContextAsync(db => db.Set<FormDocumentProposal>().AsNoTracking()
            .SingleAsync(value => value.RequestId == requestId, Ct))).DefinitionId);
        Assert.Empty(await GroupsAsync(client, scenario.Sender));

        // Published as a publication leaves it: the worker reads each document again for the form, once, with the designer's context.
        var (versionId, fields) = await PublishAsync(scenario, definitionId, Ct);
        var worker = scenario.Host.Services.GetRequiredService<FormDocumentRereadBackgroundService>();
        await worker.ProcessDueAsync(Ct);
        await worker.ProcessDueAsync(Ct);

        var rereads = scenario.Engine.Submitted.Where(operation => operation.Context.FocusDefinitionId == definitionId).ToArray();
        Assert.Equal(sent.Select(value => value.Id).Order(), rereads.Select(operation => operation.Context.ChannelConversationId!.Value).Order());
        Assert.All(rereads, operation => Assert.Equal((designer, "forms-documents"), (operation.Context.ActorUserId, operation.Context.ChannelId)));
        var form = new FormReceivedDocumentScenarioForm(definitionId, versionId, definition.Revision, prefix, name, fields["Monto"], fields["Operación"],
            fields["Comprobante"], name);
        foreach (var operation in rereads)
        {
            var proposal = FormReceivedDocumentScenario.Proposal([form], designer, "sure",
                value => [(value.AmountId, "decimal", "150000", false), (value.OperationId, "shortText", "778899", false)], formRead: true);
            scenario.Engine.Complete(designer, new(operation.RequestId, "completed", proposal, null, null) { Proposals = [proposal] });
        }
        await worker.ProcessDueAsync(Ct);

        var documents = await factory.ExecuteDbContextAsync(db => db.Set<FormReceivedDocument>().AsNoTracking()
            .Where(value => value.SenderUserId == scenario.Sender).ToListAsync(Ct));
        Assert.All(documents, document => Assert.Equal((FormReceivedDocumentState.Proposed, (Guid?)definitionId, (Guid?)versionId, (Guid?)null),
            (document.State, document.DefinitionId, document.VersionId, document.ExtractionRequestId)));
        Assert.Equal(versionId, (await factory.ExecuteDbContextAsync(db => db.Set<FormDocumentProposal>().AsNoTracking()
            .SingleAsync(value => value.RequestId == requestId, Ct))).RereadVersionId);
    }

    [Fact]
    public async Task A_document_the_engine_cannot_take_now_is_read_again_by_a_later_round_once_it_can()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var designer = await scenario.PersonAsync("Dora Diseña", Designer, Ct);
        var topic = Topic();
        for (var index = 0; index < 3; index++) await UnclassifiedAsync(scenario, topic, Ct);
        using var client = scenario.Client(designer);
        var topicId = (await GroupsAsync(client, scenario.Sender)).Single().GetProperty("topic").GetProperty("id").GetGuid();
        var definitionId = await DraftAsync(scenario, client, designer, topicId);
        await PublishAsync(scenario, definitionId, Ct);
        var worker = scenario.Host.Services.GetRequiredService<FormDocumentRereadBackgroundService>();
        Guid[] Rereads() => [.. scenario.Engine.Submitted.Where(operation => operation.Context.FocusDefinitionId == definitionId)
            .Select(operation => operation.Context.ChannelConversationId!.Value)];
        Task<FormDocumentProposal> RequestAsync() => factory.ExecuteDbContextAsync(db => db.Set<FormDocumentProposal>().AsNoTracking()
            .SingleAsync(value => value.DefinitionId == definitionId, Ct));
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var attempts = scenario.Engine.Attempts;

        // The engine's daily quota says when it passes: nothing is read, every document waits that long, and a round before then tries nothing.
        scenario.Engine.SubmitFailure = Error.TooManyRequests("Ai.Assistant.QuotaExceeded", "The acceptance quota is exhausted.",
            new Dictionary<string, object?> { ["retryAfter"] = 3600 });
        await worker.ProcessDueAsync(Ct);
        // The round stopped at the first one the engine refused: the others were not even asked.
        Assert.Equal(attempts + 1, scenario.Engine.Attempts);
        scenario.Engine.SubmitFailure = null;
        await worker.ProcessDueAsync(Ct);

        Assert.Empty(Rereads());
        var waiting = await RequestAsync();
        Assert.Equal(3, waiting.PendingRereadIds.Length);
        Assert.Equal(now.AddSeconds(3600), waiting.RereadAfterUtc!.Value, TimeSpan.FromMilliseconds(1));

        factory.Clock.Advance(TimeSpan.FromSeconds(3600));
        await worker.ProcessDueAsync(Ct);

        Assert.Equal(waiting.DocumentIds.Order(), Rereads().Order());
        var done = await RequestAsync();
        Assert.Empty(done.PendingRereadIds);
        Assert.Null(done.RereadAfterUtc);
    }

    [Fact]
    public async Task A_document_whose_step_failed_halfway_stays_pending_and_a_round_after_the_hold_reads_it()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var designer = await scenario.PersonAsync("Dora Diseña", Designer, Ct);
        var topic = Topic();
        for (var index = 0; index < 3; index++) await UnclassifiedAsync(scenario, topic, Ct);
        using var client = scenario.Client(designer);
        var topicId = (await GroupsAsync(client, scenario.Sender)).Single().GetProperty("topic").GetProperty("id").GetGuid();
        var definitionId = await DraftAsync(scenario, client, designer, topicId);
        await PublishAsync(scenario, definitionId, Ct);
        var worker = scenario.Host.Services.GetRequiredService<FormDocumentRereadBackgroundService>();
        Guid[] Rereads() => [.. scenario.Engine.Submitted.Where(operation => operation.Context.FocusDefinitionId == definitionId)
            .Select(operation => operation.Context.ChannelConversationId!.Value)];
        Task<FormDocumentProposal> RequestAsync() => factory.ExecuteDbContextAsync(db => db.Set<FormDocumentProposal>().AsNoTracking()
            .SingleAsync(value => value.DefinitionId == definitionId, Ct));

        // The first one's step throws (the database or the network went away): it stays pending, and the round goes on with the others.
        scenario.Engine.ThrowNext = new HttpRequestException("The engine went away.");
        await worker.ProcessDueAsync(Ct);
        var held = await RequestAsync();

        Assert.Equal([held.DocumentIds[0]], held.PendingRereadIds);
        Assert.Equal(held.DocumentIds.Skip(1).Order(), Rereads().Order());
        // Held by that round: nothing until the hold passes, and then a round reads it.
        await worker.ProcessDueAsync(Ct);
        Assert.Equal(2, Rereads().Length);
        factory.Clock.Advance(FormDocumentRereadService.Hold);
        await worker.ProcessDueAsync(Ct);
        Assert.Equal(held.DocumentIds.Order(), Rereads().Order());
        Assert.Empty((await RequestAsync()).PendingRereadIds);
    }

    [Fact]
    public async Task The_rounds_leave_out_a_person_s_requests_while_one_of_theirs_is_held_and_list_another_person_s()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct, isolated: true);
        var designer = await scenario.PersonAsync("Dora Diseña", Designer, Ct);
        var colleague = await scenario.PersonAsync("Eva Colega", Designer, Ct);
        var (first, _) = await PublishedRequestAsync(scenario, designer);
        var (second, _) = await PublishedRequestAsync(scenario, designer);
        var (other, _) = await PublishedRequestAsync(scenario, colleague);
        Task<FormDocumentRereadDueResponse> DueAsync() => RereadAsync(scenario.Host, service => service.ListDueAsync(Ct));

        Assert.Equal([first, second, other], (await DueAsync()).ProposalIds);
        Assert.NotNull((await RereadAsync(scenario.Host, service => service.ReserveAsync(first, Ct))).Value);

        // While the first one's round holds its documents, the person's other request is not due: it would only be reserved again for
        // nothing every few seconds, and twenty of them would fill every batch. Another person's still is.
        Assert.Equal([other], (await DueAsync()).ProposalIds);
        Assert.Null((await RereadAsync(scenario.Host, service => service.ReserveAsync(second, Ct))).Value);
        factory.Clock.Advance(FormDocumentRereadService.Hold);
        Assert.Equal([first, second, other], (await DueAsync()).ProposalIds);
    }

    [Fact]
    public async Task Two_hosts_read_again_a_person_s_requests_one_round_at_a_time_within_their_daily_share()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct, isolated: true);
        var designer = await scenario.PersonAsync("Dora Diseña", Designer, Ct);
        var (first, firstForm) = await PublishedRequestAsync(scenario, designer);
        var (second, secondForm) = await PublishedRequestAsync(scenario, designer);
        // Two copies of the server over the same database, each with a share of three re-reads a UTC day per person.
        await using var one = scenario.Host.WithWebHostBuilder(builder => builder.UseSetting("BusinessForms:DocumentReread:DailyPerAsker", "3"));
        await using var two = scenario.Host.WithWebHostBuilder(builder => builder.UseSetting("BusinessForms:DocumentReread:DailyPerAsker", "3"));
        static FormDocumentRereadBackgroundService Worker(WebApplicationFactory<Program> host) =>
            host.Services.GetRequiredService<FormDocumentRereadBackgroundService>();
        int Rereads(Guid definitionId) => scenario.Engine.Submitted.Count(operation => operation.Context.FocusDefinitionId == definitionId);
        Task<FormDocumentProposal> RequestAsync(Guid id) =>
            scenario.DbAsync(db => db.Set<FormDocumentProposal>().AsNoTracking().SingleAsync(value => value.Id == id, Ct));

        // One round of a person at a time: while one copy holds the first request, the other copy takes neither of them.
        Assert.NotNull((await RereadAsync(one, service => service.ReserveAsync(first, Ct))).Value);
        Assert.Null((await RereadAsync(two, service => service.ReserveAsync(second, Ct))).Value);
        Assert.Null((await RereadAsync(two, service => service.ReserveAsync(first, Ct))).Value);
        Assert.Null((await RequestAsync(second)).RereadVersionId);

        // Once that hold passes both copies run their rounds at once: three re-reads between them, the first request's, and the second
        // waits for the next UTC day.
        factory.Clock.Advance(FormDocumentRereadService.Hold);
        var today = DateOnly.FromDateTime(factory.Clock.GetUtcNow().UtcDateTime);
        await Task.WhenAll(Worker(one).ProcessDueAsync(Ct), Worker(two).ProcessDueAsync(Ct));

        Assert.Equal((3, 0), (Rereads(firstForm), Rereads(secondForm)));
        var (done, waiting) = (await RequestAsync(first), await RequestAsync(second));
        Assert.Equal((3, 0), (done.RereadsOn(today), waiting.RereadsOn(today)));
        Assert.Empty(done.PendingRereadIds);
        Assert.Equal(3, waiting.PendingRereadIds.Length);
        var tomorrow = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        Assert.Equal(tomorrow, waiting.RereadAfterUtc);

        factory.Clock.Advance(tomorrow - factory.Clock.GetUtcNow().UtcDateTime);
        await Task.WhenAll(Worker(one).ProcessDueAsync(Ct), Worker(two).ProcessDueAsync(Ct));

        Assert.Equal((3, 3), (Rereads(firstForm), Rereads(secondForm)));
        Assert.Empty((await RequestAsync(second)).PendingRereadIds);
    }

    [Fact]
    public async Task A_document_whose_step_throws_three_rounds_in_a_row_leaves_the_pending_ones_and_its_person_s_other_request_goes_on()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct, isolated: true);
        var designer = await scenario.PersonAsync("Dora Diseña", Designer, Ct);
        var (first, firstForm) = await PublishedRequestAsync(scenario, designer);
        var (_, secondForm) = await PublishedRequestAsync(scenario, designer);
        var worker = scenario.Host.Services.GetRequiredService<FormDocumentRereadBackgroundService>();
        int Rereads(Guid definitionId) => scenario.Engine.Submitted.Count(operation => operation.Context.FocusDefinitionId == definitionId);
        Task<FormDocumentProposal> RequestAsync() =>
            scenario.DbAsync(db => db.Set<FormDocumentProposal>().AsNoTracking().SingleAsync(value => value.Id == first, Ct));

        // The first request's first document throws every round (data its step cannot handle): it stays pending and its round holds the
        // request, and with it the person's other one...
        for (var round = 1; round < 3; round++)
        {
            scenario.Engine.ThrowNext = new InvalidOperationException("The step cannot handle this document.");
            await worker.ProcessDueAsync(Ct);
            Assert.Single((await RequestAsync()).PendingRereadIds);
            Assert.Equal((2, 0), (Rereads(firstForm), Rereads(secondForm)));
            factory.Clock.Advance(FormDocumentRereadService.Hold);
        }
        // ...until its third step in a row throws: it leaves them, as one whose extraction could not be asked, and the other request is read.
        scenario.Engine.ThrowNext = new InvalidOperationException("The step cannot handle this document.");
        await worker.ProcessDueAsync(Ct);

        var left = await RequestAsync();
        Assert.Empty(left.PendingRereadIds);
        Assert.Null(left.RereadAfterUtc);
        Assert.Equal((2, 3), (Rereads(firstForm), Rereads(secondForm)));
    }

    [Fact]
    public async Task Asking_while_the_engine_s_quota_is_exhausted_answers_its_429_with_its_code_and_wait()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var designer = await scenario.PersonAsync("Dora Diseña", Designer, Ct);
        var sent = await UnclassifiedAsync(scenario, Topic(), Ct);
        using var client = scenario.Client(designer);
        scenario.Engine.SubmitFailure = Error.TooManyRequests("Ai.Assistant.QuotaExceeded", "The acceptance quota is exhausted.",
            new Dictionary<string, object?> { ["retryAfter"] = 600 });

        using var refused = await client.PostAsJsonAsync(Proposals, new { requestId = Guid.NewGuid(), documentId = sent.Id }, Ct);
        scenario.Engine.SubmitFailure = null;

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        var body = await refused.ReadJsonAsync();
        Assert.Equal(("Ai.Assistant.QuotaExceeded", 600), (body.GetProperty("code").GetString(), body.GetProperty("retryAfter").GetInt32()));
    }

    [Fact]
    public async Task Asking_reading_and_creating_need_to_see_the_inbox_too()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var designer = await scenario.PersonAsync("Dora Diseña", Designer, Ct);
        var blind = await scenario.PersonAsync("Bruno Diseña", [.. Designer.Except([BusinessFormsPermissions.DocumentsRead])], Ct);
        var sent = await UnclassifiedAsync(scenario, Topic(), Ct);
        var requestId = Guid.NewGuid();
        using (var asked = await scenario.Client(designer).PostAsJsonAsync(Proposals, new { requestId, documentId = sent.Id }, Ct))
            Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        using var client = scenario.Client(blind);

        // Its answers carry the inbox's documents: who cannot see the inbox can neither ask, read nor create.
        using var ask = await client.PostAsJsonAsync(Proposals, new { requestId = Guid.NewGuid(), documentId = sent.Id }, Ct);
        using var read = await client.GetAsync($"{Proposals}/{requestId}", Ct);
        using var create = await client.PostAsJsonAsync(Drafts, new
        {
            requestId, proposalId = Guid.NewGuid(), name = "Tickets", prefix = "TCK", submitLabel = "Cargar", recognition = "Un ticket.",
            questions = Array.Empty<object>(),
        }, Ct);

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (ask.StatusCode, read.StatusCode, create.StatusCode));
    }

    [Fact]
    public async Task A_deleted_draft_gives_its_documents_and_its_topic_back_to_the_groups()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var designer = await scenario.PersonAsync("Dora Diseña", Designer, Ct);
        var topic = Topic();
        for (var index = 0; index < 3; index++) await UnclassifiedAsync(scenario, topic, Ct);
        using var client = scenario.Client(designer);
        var topicId = (await GroupsAsync(client, scenario.Sender)).Single().GetProperty("topic").GetProperty("id").GetGuid();
        var definitionId = await DraftAsync(scenario, client, designer, topicId);
        Assert.Empty(await GroupsAsync(client, scenario.Sender));
        var revision = (await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().AsNoTracking()
            .SingleAsync(value => value.Id == definitionId, Ct))).Revision;

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/forms/definitions/{definitionId}")
            { Content = JsonContent.Create(new { expectedRevision = revision }) };
        using var deleted = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        // The draft that no longer exists keeps neither its documents nor its topic: the group is back, and a request takes them again.
        var group = Assert.Single(await GroupsAsync(client, scenario.Sender));
        Assert.Equal((topicId, 3), (group.GetProperty("topic").GetProperty("id").GetGuid(), group.GetProperty("count").GetInt32()));
        using var again = await client.PostAsJsonAsync(Proposals, new { requestId = Guid.NewGuid(), topicId }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(3, (await again.ReadJsonAsync()).GetProperty("documents").GetArrayLength());
    }

    [Fact]
    public async Task A_topic_that_is_not_pending_is_not_a_group()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var designer = await scenario.PersonAsync("Dora Diseña", Designer, Ct);
        var topic = Topic();
        for (var index = 0; index < 3; index++) await UnclassifiedAsync(scenario, topic, Ct);
        using var client = scenario.Client(designer);
        var topicId = (await GroupsAsync(client, scenario.Sender)).Single().GetProperty("topic").GetProperty("id").GetGuid();
        var revision = (await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormMissingTopic>().AsNoTracking()
            .SingleAsync(value => value.Id == topicId, Ct))).Revision;

        using var discarded = await client.PostAsJsonAsync($"/api/forms/missing-requests/{topicId}/discard", new { expectedRevision = revision }, Ct);

        Assert.Equal(HttpStatusCode.OK, discarded.StatusCode);
        Assert.Empty(await GroupsAsync(client, scenario.Sender));
    }

    [Fact]
    public async Task The_notice_shows_the_five_largest_groups_and_a_group_names_its_twenty_newest_documents()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var designer = await scenario.PersonAsync("Dora Diseña", Designer, Ct);
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var largest = Topic();
        var sent = new List<FormReceivedDocumentSent>();
        for (var index = 0; index < 21; index++) sent.Add(await UnclassifiedAsync(scenario, largest, Ct, now.AddMinutes(index - 60)));
        string[] others = [Topic(), Topic(), Topic(), Topic(), Topic()];
        int[] sizes = [5, 4, 3, 3, 3];
        for (var topic = 0; topic < others.Length; topic++)
            for (var index = 0; index < sizes[topic]; index++) await UnclassifiedAsync(scenario, others[topic], Ct, now.AddMinutes(index - 30));
        using var client = scenario.Client(designer);

        var groups = await GroupsAsync(client, scenario.Sender);

        // Five, the largest first; the twenty-one count as they are and name the twenty newest.
        Assert.Equal([21, 5, 4, 3, 3], groups.Select(group => group.GetProperty("count").GetInt32()));
        Assert.Equal(largest, groups[0].GetProperty("topic").GetProperty("name").GetString());
        var newest = sent.Select(value => value.Id).Reverse().Take(FormDocumentProposal.MaxDocuments).ToArray();
        Assert.Equal(newest, groups[0].GetProperty("documentIds").EnumerateArray().Select(id => id.GetGuid()));
        Assert.All(groups.Skip(1), group => Assert.Contains(group.GetProperty("topic").GetProperty("name").GetString(), others));
        // Asking for that group takes those twenty, in that order.
        using var asked = await client.PostAsJsonAsync(Proposals,
            new { requestId = Guid.NewGuid(), topicId = groups[0].GetProperty("topic").GetProperty("id").GetGuid() }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.Equal(newest, (await asked.ReadJsonAsync()).GetProperty("documents").EnumerateArray().Select(document => document.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task A_draft_whose_request_cannot_record_it_saves_nothing()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var designer = await scenario.PersonAsync("Dora Diseña", Designer, Ct);
        var proposalId = Guid.CreateVersion7();
        var form = new FormAuthoringFormProposal("es", "Tickets " + Letters(), "T" + Letters().ToUpperInvariant(), "Cargar ticket",
            [new("Monto", "number", true, [], null)]);
        var draft = FormAuthoringProposalPolicy.Draft(proposalId, form, new FormAuthoringProposedReading("Un comprobante de pago.")) with
        {
            Proposal = new FormDraftProposalOrigin(proposalId, "{}")
                { RecordAsync = (_, _) => Task.FromResult(Result.Failure(FormReceivedDocumentErrors.RequestConflict)) },
            ActorUserId = designer,
        };

        Result<FormDraftCommitResponse> saved;
        await using (var scope = scenario.Host.Services.CreateAsyncScope())
            saved = await scope.ServiceProvider.GetRequiredService<IFormDraftService>().CreateAsync(draft, Ct);

        // The editor's own save: the request's refusal is its answer, and neither the draft nor its change was kept.
        Assert.Equal(FormReceivedDocumentErrors.RequestConflict, saved.Error);
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().IgnoreQueryFilters()
            .AnyAsync(value => value.ProposalId == proposalId || value.Prefix == form.Prefix, Ct)));
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDraftChange>()
            .AnyAsync(value => value.OperatorUserId == designer && value.RequestId == proposalId, Ct)));
    }

    [Fact]
    public async Task The_routes_declare_their_bodies_and_errors()
    {
        var paths = (await OpenApiTests.ReadDocumentAsync(factory)).GetProperty("paths");

        AssertResponses(paths.GetProperty(Proposals).GetProperty("post"), "202", ["400", "401", "403", "404", "409", "429", "500"]);
        AssertResponses(paths.GetProperty(Proposals + "/{requestId}").GetProperty("get"), "200", ["401", "403", "404", "429", "500"]);
        AssertResponses(paths.GetProperty(Drafts).GetProperty("post"), "201", ["400", "401", "403", "404", "409", "429", "500"]);
    }

    private static void AssertResponses(JsonElement operation, string success, string[] errors)
    {
        var responses = operation.GetProperty("responses").EnumerateObject().Select(response => response.Name).ToArray();
        Assert.Contains(success, responses);
        Assert.Equal(errors, responses.Where(name => name[0] is '4' or '5').Order(StringComparer.Ordinal));
    }

    // A request of that person from a topic of three unclassified documents of the scenario's sender, with its draft created and published:
    // the request and its form. The clock moves a second after it, so the requests keep the order they were asked in.
    private async Task<(Guid ProposalId, Guid DefinitionId)> PublishedRequestAsync(FormReceivedDocumentScenario scenario, Guid designer)
    {
        var topic = Topic();
        for (var index = 0; index < 3; index++) await UnclassifiedAsync(scenario, topic, Ct);
        using var client = scenario.Client(designer);
        var topicId = (await GroupsAsync(client, scenario.Sender)).Single().GetProperty("topic").GetProperty("id").GetGuid();
        var definitionId = await DraftAsync(scenario, client, designer, topicId);
        await PublishAsync(scenario, definitionId, Ct);
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        var proposalId = await scenario.DbAsync(db => db.Set<FormDocumentProposal>().AsNoTracking().Where(value => value.DefinitionId == definitionId)
            .Select(value => value.Id).SingleAsync(Ct));
        return (proposalId, definitionId);
    }

    // One call of the re-reading's service, in a scope of that copy of the server.
    private static async Task<T> RereadAsync<T>(WebApplicationFactory<Program> host, Func<IFormDocumentRereadService, Task<T>> call)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<IFormDocumentRereadService>());
    }

    // A document of the scenario's sender proposed for «Pagos», read as that type of document, whose person then chose «Otro»: unclassified,
    // and noted in «Formularios que faltan» with its type as the topic.
    private static async Task<FormReceivedDocumentSent> UnclassifiedAsync(FormReceivedDocumentScenario scenario, string topic, CancellationToken ct,
        DateTime? receivedAtUtc = null)
    {
        var proposal = FormReceivedDocumentScenario.Proposal([scenario.Form], scenario.Sender, "sure",
            form => [(form.AmountId, "decimal", "150000", false), (form.OperationId, "shortText", "778899", false)]);
        var sent = await scenario.SendAsync(ct, proposal: proposal with { Payload = ((FormDocumentEntryProposal)proposal.Payload) with { DocumentType = topic } },
            receivedAtUtc: receivedAtUtc);
        var chosen = await scenario.PortAsync(port => port.ChooseOtherAsync(new(sent.Id, FormReceivedDocumentScenario.Channel, sent.Request.SourceId,
            scenario.Sender, "es", scenario.Factory.Clock.GetUtcNow().UtcDateTime), ct));
        Assert.Equal(StructuredDocumentChoiceState.Recorded, chosen);
        return sent;
    }

    // The draft's form becomes active as a publication leaves it: a version of its questions with its reading sealed.
    private static Task<(Guid VersionId, IReadOnlyDictionary<string, Guid> Fields)> PublishAsync(FormReceivedDocumentScenario scenario, Guid definitionId,
        CancellationToken ct) => scenario.DbAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == definitionId, ct);
            var schema = JsonSerializer.Deserialize<FormQuestionSchema>(definition.SchemaJson, JsonSerializerOptions.Web)!;
            foreach (var field in schema.Fields) db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definitionId, field.Key, field.Type));
            await db.SaveChangesAsync(ct);
            var version = BusinessFormVersion.Create(definitionId, 1, "documents-proposal");
            version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, schema.Fields), JsonSerializerOptions.Web));
            version.SealPresentation(definition.NameJson, definition.SubmitLabelJson, definition.Culture, schema.Fields.Count);
            version.SealDocumentReading(definition.DocumentReadingJson!);
            db.Set<BusinessFormVersion>().Add(version);
            db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(scenario.Factory, definition, version, scenario.Publisher, ["es"]));
            Assert.True(definition.ActivateVersion(null, version.Id).IsSuccess);
            await db.SaveChangesAsync(ct);
            return (version.Id, (IReadOnlyDictionary<string, Guid>)schema.Fields.ToDictionary(field => field.Label["es"], field => field.FieldId));
        });

    // A topic's request, its proposal completed and «Crear borrador» as the proposal came: the draft it created.
    private async Task<Guid> DraftAsync(FormReceivedDocumentScenario scenario, HttpClient client, Guid designer, Guid topicId)
    {
        var requestId = Guid.NewGuid();
        using (var asked = await client.PostAsJsonAsync(Proposals, new { requestId, topicId }, Ct))
            Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        var observed = FormProposal();
        scenario.Engine.Complete(designer, new(requestId, "completed", observed, Provenance(), null) { Proposals = [observed] });
        using var created = await client.PostAsJsonAsync(Drafts, new
        {
            requestId, proposalId = observed.Id, name = "Tickets " + Letters(), prefix = "T" + Letters().ToUpperInvariant(), submitLabel = "Cargar ticket",
            recognition = "Un comprobante de pago.",
            questions = new object[] { new { label = "Monto", type = "number", required = true, options = Array.Empty<string>(), catalogId = (Guid?)null } },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.ReadJsonAsync()).GetProperty("definitionId").GetGuid();
    }

    private static async Task<JsonElement[]> GroupsAsync(HttpClient client, Guid sender)
    {
        using var page = await client.PostAsJsonAsync("/api/forms/documents/search", new { senderUserId = sender }, Ct);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        return [.. (await page.ReadJsonAsync()).GetProperty("groups").EnumerateArray()];
    }

    // What the assistant proposes from the documents: the form with what each question read in the first one and its reading on.
    private static AssistantObservedProposal FormProposal() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), FormDocumentAuthoringPolicy.ProposeForm,
        new FormAuthoringDocumentFormProposal("es", "Tickets de combustible", "TCB", "Cargar ticket",
            [new("Monto", "number", true, [], null, "150000"), new("Operación", "shortText", false, [], null, "778899"),
                new("Comprobante", "document", false, [], null, null)],
            new("Un comprobante de pago."), null));

    private AssistantOperationProvenance Provenance() =>
        new("fictitious-profile", 1, "capabilities-v1", 1, factory.Clock.GetUtcNow().UtcDateTime, "forms-documents");

    // A topic of its own, only letters (a number would look like personal data to the missing forms' protection).
    private static string Topic() => "tickets " + Letters();

    private static string Letters() => new([.. Guid.NewGuid().ToString("N").Where(char.IsAsciiLetter).Take(8)]);

    private static async Task<string?> CodeAsync(HttpResponseMessage response) => (await response.ReadJsonAsync()).GetProperty("code").GetString();
}
