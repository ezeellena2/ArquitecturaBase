using System.Net;
using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// IA, Stage 2 with PostgreSQL: the automatic assistant takes a received spreadsheet with its caption or its contiguous text, in either
// order; a catalog says its rows and «Crear catálogo» keeps the request for the app's import; an import answers «Revisar importación»
// with its link; a question with a spreadsheet asks what to do with it. Meta's download, the private storage and the engine are
// doubles; the owner of the spreadsheets and of the proposals is BusinessForms, reached through the neutral ports.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantSpreadsheetIntegrationTests(ApiFactory factory)
{
    private static readonly byte[] Csv = Encoding.UTF8.GetBytes("Código,Texto\nA1,Uno\nA2,Dos\n");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_spreadsheet_with_its_request_gets_a_summary_with_its_rows_and_creating_keeps_the_request_for_its_import()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv };
        var lookup = new ProposalLookup();
        await using var host = await CreateAsync(probe, lookup);
        var person = await PersonAsync(host);
        var waMessageId = await ReceiveSpreadsheetAsync(host, person, probe, "Armame un catálogo de clientes con esta planilla");

        await ProcessAsync(host);

        var message = await MessageAsync(host, waMessageId);
        var submitted = Assert.Single(host.Operations.SubmitRequests);
        Assert.Equal("Armame un catálogo de clientes con esta planilla", submitted.Text);
        var attachment = Assert.IsType<AssistantAttachmentInput>(submitted.Attachment);
        Assert.Equal(($"whatsapp/documents/{message.Id:N}.csv", "clientes.csv", "text/csv", (long)Csv.Length, Convert.ToHexStringLower(SHA256.HashData(Csv))),
            (attachment.StorageKey, attachment.Name, attachment.ContentType, attachment.Size, attachment.Sha256));
        Assert.Equal(message.OccurredAtUtc.AddDays(90), attachment.ExpiresAtUtc);
        Assert.Contains(new AssistantCapabilityReference("forms.authoring.propose-catalog", 2), submitted.Context.AllowedCapabilities);
        Assert.Contains(new AssistantCapabilityReference("forms.authoring.propose-import", 1), submitted.Context.AllowedCapabilities);
        Assert.Equal((Guid?)message.Id, (await ReferenceAsync(host, submitted.RequestId)).AttachmentMessageId);

        var proposal = await ProposeAsync(host, submitted, new("forms.authoring.propose-catalog", 2),
            """{"name":"Clientes ficticios","description":"Clientes del negocio.","valueType":"text","code":"el código","option":"el nombre","fields":[],"mapping":{"hasHeaders":true,"code":"A","option":"B","value":null,"detail":null,"fields":[]}}""");
        await CompleteAsync(host, person, submitted, proposal);

        var summary = await ReferenceAsync(host, submitted.RequestId);
        var buttons = await OutgoingAsync(host, summary.OutgoingId!.Value);
        Assert.Equal("Te propongo el catálogo Clientes ficticios: el código es el código y cada opción el nombre. Son 2 filas. ¿Lo armo?", buttons.Body);
        Assert.Equal(["Crear catálogo", "Cambiar algo"], buttons.QuickReplies.Select(reply => reply.Title));

        var tap = await TapAsync(host, person.ContactId, buttons.QuickReplies[0].Id);
        await ProcessTapAsync(host, person.ContactId);
        var created = (await ReplyAsync(host, person.ContactId, tap)).Body!;
        Assert.StartsWith("Listo: armé el catálogo Clientes ficticios. Revisalo y activalo desde la app: http", created, StringComparison.Ordinal);
        var catalogId = Guid.Parse(created[(created.LastIndexOf('/') + 1)..]);

        // The app's pending import names the request the channel kept with the catalog.
        lookup.Answer = new(submitted.Context, proposal, new("http-fixture", 1, "http-fixture", 1, submitted.OccurredAtUtc, "whatsapp-assistant"), true)
            { Attachment = new(attachment.StorageKey, attachment.Name, attachment.ContentType, attachment.Size, attachment.Sha256) };
        using var client = host.Client(person.RecipientId);
        using var response = await client.GetAsync($"/api/forms/catalogs/{catalogId}/assistant-import", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pending = (await response.ReadJsonAsync()).GetProperty("pending");
        Assert.Equal((submitted.RequestId, proposal.Id, "clientes.csv", 2),
            (pending.GetProperty("requestId").GetGuid(), pending.GetProperty("proposalId").GetGuid(), pending.GetProperty("fileName").GetString(),
                pending.GetProperty("rowCount").GetInt32()));
        Assert.Equal((person.RecipientId, submitted.RequestId), (lookup.Actor, lookup.RequestId));

        // An import into that catalog is reviewed in the app, from its own link: nothing is created.
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await ReceiveSpreadsheetAsync(host, person, probe, "Importala en Clientes ficticios");
        await ProcessAsync(host);
        var importing = host.Operations.SubmitRequests[^1];
        var import = await ProposeAsync(host, importing, new("forms.authoring.propose-import", 1),
            $$"""{"catalogId":"{{catalogId}}","hasHeaders":true,"code":"A","option":"B","value":null,"detail":null,"fields":[]}""");
        await CompleteAsync(host, person, importing, import);
        var review = await OutgoingAsync(host, (await ReferenceAsync(host, importing.RequestId)).OutgoingId!.Value);
        Assert.Equal("Te propongo importar la planilla en el catálogo Clientes ficticios: el código de la columna A (Código) y cada opción de la columna B (Texto). Son 2 filas. ¿La revisás?",
            review.Body);
        Assert.Equal(["Revisar importación", "Cambiar algo"], review.QuickReplies.Select(reply => reply.Title));
        // The import's summary passes the dispatch check of its reference and goes out, after what was queued before it.
        var worker = host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        for (var turn = 0; turn < 10 && (await OutgoingAsync(host, review.Id)).State == WhatsAppOutgoingMessageState.Pending; turn++)
            await worker.ProcessNextAsync(Ct);
        Assert.Equal(WhatsAppOutgoingMessageState.Accepted, (await OutgoingAsync(host, review.Id)).State);
        var reviewTap = await TapAsync(host, person.ContactId, review.QuickReplies[0].Id);
        await ProcessTapAsync(host, person.ContactId);
        var link = (await ReplyAsync(host, person.ContactId, reviewTap)).Body!;
        Assert.StartsWith("Revisá la importación desde la app: http", link, StringComparison.Ordinal);
        Assert.EndsWith($"/catalogos/{catalogId:D}?pedido={importing.RequestId:N}&propuesta={import.Id:N}", link, StringComparison.Ordinal);
        Assert.Equal(1, await CatalogCountAsync(host, catalogId));
    }

    [Fact]
    public async Task A_spreadsheet_and_then_its_text_and_a_text_and_then_its_spreadsheet_are_interpreted_together()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv };
        await using var host = await CreateAsync(probe, new ProposalLookup());
        var first = await PersonAsync(host);

        // The spreadsheet alone stays in the chat; its text, a minute later, carries it.
        var spreadsheetFirst = await ReceiveSpreadsheetAsync(host, first, probe, null);
        await ProcessAsync(host);
        Assert.Empty(host.Operations.SubmitRequests);
        Assert.Empty(await ReferencesAsync(host, first.ContactId));
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var textAfter = await ReceiveTextAsync(host, first, "Armame un catálogo de clientes");
        await ProcessAsync(host);
        var paired = Assert.Single(host.Operations.SubmitRequests);
        var spreadsheet = await MessageAsync(host, spreadsheetFirst);
        Assert.Equal(("Armame un catálogo de clientes", $"whatsapp/documents/{spreadsheet.Id:N}.csv"), (paired.Text, paired.Attachment!.StorageKey));
        var reference = Assert.Single(await ReferencesAsync(host, first.ContactId));
        Assert.Equal(((await MessageAsync(host, textAfter)).Id, (Guid?)spreadsheet.Id), (reference.SourceMessageId, reference.AttachmentMessageId));

        // The text goes first and its interpretation is in progress when the spreadsheet arrives: it is replaced by the pair.
        var second = await PersonAsync(host);
        await ReceiveTextAsync(host, second, "Armame un catálogo de proveedores");
        await ProcessAsync(host);
        var alone = host.Operations.SubmitRequests[^1];
        Assert.Null(alone.Attachment);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var spreadsheetAfter = await ReceiveSpreadsheetAsync(host, second, probe, null);
        await ProcessAsync(host);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked, (await ReferenceAsync(host, alone.RequestId)).State);
        await ProcessAsync(host);
        var replaced = host.Operations.SubmitRequests[^1];
        var stored = await MessageAsync(host, spreadsheetAfter);
        Assert.Equal(("Armame un catálogo de proveedores", $"whatsapp/documents/{stored.Id:N}.csv"), (replaced.Text, replaced.Attachment!.StorageKey));
        Assert.Equal((stored.Id, (Guid?)stored.Id), ((await ReferenceAsync(host, replaced.RequestId)).SourceMessageId, (await ReferenceAsync(host, replaced.RequestId)).AttachmentMessageId));
    }

    [Fact]
    public async Task A_question_with_a_spreadsheet_asks_what_to_do_with_it_and_the_answer_continues_with_it()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv };
        await using var host = await CreateAsync(probe, new ProposalLookup());
        var person = await PersonAsync(host);
        await ReceiveSpreadsheetAsync(host, person, probe, "Mirá esta planilla");
        await ProcessAsync(host);
        var asked = Assert.Single(host.Operations.SubmitRequests);
        await CompleteAsync(host, person, asked, await ProposeAsync(host, asked, new("forms.authoring.decline", 1), """{"reason":"unclear","prefix":null,"catalogId":null}"""));

        var question = await ReferenceAsync(host, asked.RequestId);
        var text = await OutgoingAsync(host, question.OutgoingId!.Value);
        Assert.Equal((WhatsAppOutgoingMessageKind.Text, (Guid?)asked.RequestId), (text.Kind, question.ChangeRequestId));
        Assert.Equal("¿Qué querés que haga con esta planilla? Puedo armar un catálogo con ella o importarla en uno que ya existe. Contame por escrito o con un audio.", text.Body);

        // The answer continues the same operation, which keeps the spreadsheet: the engine inherits it, so it does not travel again.
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await ReceiveTextAsync(host, person, "No sé");
        await ProcessAsync(host);
        var answer = host.Operations.SubmitRequests[^1];
        Assert.Equal(("No sé", (Guid?)asked.RequestId, (AssistantAttachmentInput?)null), (answer.Text, answer.PreviousRequestId, answer.Attachment));
        Assert.Null((await ReferenceAsync(host, answer.RequestId)).AttachmentMessageId);

        // Asked again, the conversation still has its spreadsheet.
        await CompleteAsync(host, person, answer, await ProposeAsync(host, answer, new("forms.authoring.decline", 1), """{"reason":"unclear","prefix":null,"catalogId":null}"""));
        var again = await OutgoingAsync(host, (await ReferenceAsync(host, answer.RequestId)).OutgoingId!.Value);
        Assert.StartsWith("¿Qué querés que haga con esta planilla?", again.Body, StringComparison.Ordinal);
    }

    // «Cambiar algo» and then the spreadsheet and its text, in either order: the second message replaces the answer in progress, which
    // never answered, and the pair continues the summary's operation, like the answer it replaced. The engine adds that turn at the end of
    // the conversation, with the summary's proposal in its history (AiAssistantOperationContinuationTests, with the real engine).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task After_change_something_the_pair_that_replaces_an_answer_in_progress_continues_the_summary(bool spreadsheetFirst)
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv };
        await using var host = await CreateAsync(probe, new ProposalLookup());
        var person = await PersonAsync(host);
        var summary = await SummaryAsync(host, person, probe);
        var change = await TapAsync(host, person.ContactId, summary.Buttons.QuickReplies[1].Id);
        await ProcessTapAsync(host, person.ContactId);
        Assert.Equal((Guid?)summary.RequestId, (await TapReferenceAsync(host, person.ContactId, change)).ChangeRequestId);

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var first = spreadsheetFirst ? await ReceiveSpreadsheetAsync(host, person, probe, null) : await ReceiveTextAsync(host, person, "Agregá el campo Rubro");
        await ProcessAsync(host);
        var answering = host.Operations.SubmitRequests[^1];
        Assert.Equal(((Guid?)summary.RequestId, spreadsheetFirst ? "" : "Agregá el campo Rubro"), (answering.PreviousRequestId, answering.Text));

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = spreadsheetFirst ? await ReceiveTextAsync(host, person, "Agregá el campo Rubro") : await ReceiveSpreadsheetAsync(host, person, probe, null);
        await ProcessAsync(host);
        var replaced = await ReferenceAsync(host, answering.RequestId);
        Assert.Equal((WhatsAppAssistantRequestState.Blocked, (Guid?)summary.RequestId), (replaced.State, replaced.ChangeRequestId));
        await ProcessAsync(host);

        var replacing = host.Operations.SubmitRequests[^1];
        var spreadsheet = await MessageAsync(host, spreadsheetFirst ? first : second);
        Assert.Equal(("Agregá el campo Rubro", (Guid?)summary.RequestId, $"whatsapp/documents/{spreadsheet.Id:N}.csv"),
            (replacing.Text, replacing.PreviousRequestId, replacing.Attachment!.StorageKey));
        Assert.Equal((Guid?)spreadsheet.Id, (await ReferenceAsync(host, replacing.RequestId)).AttachmentMessageId);
    }

    // After a question the summary is still the latest one: its «Cambiar algo» works, and the next message continues the summary's
    // operation, which the engine adds after the question's turn.
    [Fact]
    public async Task After_a_question_change_something_of_the_latest_summary_still_works()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv };
        await using var host = await CreateAsync(probe, new ProposalLookup());
        var person = await PersonAsync(host);
        var summary = await SummaryAsync(host, person, probe);
        await TapAsync(host, person.ContactId, summary.Buttons.QuickReplies[1].Id);
        await ProcessTapAsync(host, person.ContactId);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await ReceiveTextAsync(host, person, "Cambiá eso");
        await ProcessAsync(host);
        var asked = host.Operations.SubmitRequests[^1];
        Assert.Equal((Guid?)summary.RequestId, asked.PreviousRequestId);
        await CompleteAsync(host, person, asked, await ProposeAsync(host, asked, new("forms.authoring.decline", 1), """{"reason":"unclear","prefix":null,"catalogId":null}"""));
        Assert.Equal(WhatsAppOutgoingMessageKind.Text, (await OutgoingAsync(host, (await ReferenceAsync(host, asked.RequestId)).OutgoingId!.Value)).Kind);

        var again = await TapAsync(host, person.ContactId, summary.Buttons.QuickReplies[1].Id);
        await ProcessTapAsync(host, person.ContactId);
        Assert.Equal((Guid?)summary.RequestId, (await TapReferenceAsync(host, person.ContactId, again)).ChangeRequestId);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await ReceiveTextAsync(host, person, "Agregá el campo Rubro");
        await ProcessAsync(host);

        var changed = host.Operations.SubmitRequests[^1];
        Assert.Equal(("Agregá el campo Rubro", (Guid?)summary.RequestId), (changed.Text, changed.PreviousRequestId));
    }

    // A summary of the assistant between a text and a spreadsheet keeps them apart: the spreadsheet alone stays in the chat.
    [Fact]
    public async Task A_summary_between_a_text_and_a_spreadsheet_keeps_them_apart()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv };
        await using var host = await CreateAsync(probe, new ProposalLookup());
        var person = await PersonAsync(host);
        await ReceiveTextAsync(host, person, "Armame un catálogo de clientes");
        await ProcessAsync(host);
        var interpreted = Assert.Single(host.Operations.SubmitRequests);
        host.Clock.Advance(TimeSpan.FromSeconds(5));
        await CompleteAsync(host, person, interpreted, await ProposeAsync(host, interpreted, new("forms.authoring.propose-catalog", 2),
            """{"name":"Clientes ficticios","description":"Clientes del negocio.","valueType":"text","code":"el código","option":"el nombre","fields":[],"mapping":null}"""));
        Assert.Equal(WhatsAppOutgoingMessageKind.QuickReplies, (await OutgoingAsync(host, (await ReferenceAsync(host, interpreted.RequestId)).OutgoingId!.Value)).Kind);

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var spreadsheet = await ReceiveSpreadsheetAsync(host, person, probe, null);
        await ProcessAsync(host);

        var message = await MessageAsync(host, spreadsheet);
        Assert.Equal(WhatsAppSpreadsheetState.Stored, message.SpreadsheetState);
        Assert.NotNull(message.AssistantProcessedAtUtc);
        Assert.Single(host.Operations.SubmitRequests);
        Assert.Single(await ReferencesAsync(host, person.ContactId));
    }

    // A text that arrives while its spreadsheet is read is answered by the rejection, which is requested after it: no reply in between
    // keeps them apart, and the text is not interpreted.
    [Fact]
    public async Task A_text_whose_spreadsheet_is_rejected_after_it_is_not_interpreted()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Encoding.Latin1.GetBytes("Código;Texto\nA1;Año\n") };
        await using var host = await CreateAsync(probe, new ProposalLookup());
        var person = await PersonAsync(host);
        await ReceiveSpreadsheetAsync(host, person, probe, null);
        host.Clock.Advance(TimeSpan.FromSeconds(5));
        var text = await ReceiveTextAsync(host, person, "Armame un catálogo con esta planilla");
        host.Clock.Advance(TimeSpan.FromSeconds(5));

        await ProcessAsync(host);
        await ProcessAsync(host);

        var rejection = Assert.Single(await ReferencesAsync(host, person.ContactId));
        Assert.Equal((Guid?)rejection.RequestId, rejection.ProposalId);
        Assert.StartsWith("No puedo leer la planilla.", (await OutgoingAsync(host, rejection.OutgoingId!.Value)).Body, StringComparison.Ordinal);
        Assert.NotNull((await MessageAsync(host, text)).AssistantProcessedAtUtc);
        Assert.Empty(host.Operations.SubmitRequests);
    }

    // A text waits for the spreadsheet before it while it is still downloaded, and then goes with it.
    [Fact]
    public async Task A_text_waits_for_its_spreadsheet_while_it_is_downloaded()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv, Failures = 1 };
        await using var host = await CreateAsync(probe, new ProposalLookup());
        var person = await PersonAsync(host);
        var spreadsheet = await ReceiveSpreadsheetAsync(host, person, probe, null);
        var text = await ReceiveTextAsync(host, person, "Armame un catálogo de clientes");

        await ProcessAsync(host);

        Assert.Equal(WhatsAppSpreadsheetState.Pending, (await MessageAsync(host, spreadsheet)).SpreadsheetState);
        Assert.Null((await MessageAsync(host, text)).AssistantProcessedAtUtc);
        Assert.Empty(host.Operations.SubmitRequests);
        Assert.Empty(await ReferencesAsync(host, person.ContactId));

        host.Clock.Advance(TimeSpan.FromSeconds(31));
        await ProcessAsync(host);

        var stored = await MessageAsync(host, spreadsheet);
        var submitted = Assert.Single(host.Operations.SubmitRequests);
        Assert.Equal(("Armame un catálogo de clientes", $"whatsapp/documents/{stored.Id:N}.csv"), (submitted.Text, submitted.Attachment!.StorageKey));
        Assert.Equal(2, probe.Downloads);
    }

    // A summary with its buttons, for a spreadsheet with its request.
    private static async Task<(Guid RequestId, WhatsAppOutgoingMessage Buttons)> SummaryAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact person,
        WhatsAppDocumentProbe probe)
    {
        await ReceiveSpreadsheetAsync(host, person, probe, "Armame un catálogo de clientes con esta planilla");
        await ProcessAsync(host);
        var submitted = host.Operations.SubmitRequests[^1];
        await CompleteAsync(host, person, submitted, await ProposeAsync(host, submitted, new("forms.authoring.propose-catalog", 2),
            """{"name":"Clientes ficticios","description":"Clientes del negocio.","valueType":"text","code":"el código","option":"el nombre","fields":[],"mapping":{"hasHeaders":true,"code":"A","option":"B","value":null,"detail":null,"fields":[]}}"""));
        var buttons = await OutgoingAsync(host, (await ReferenceAsync(host, submitted.RequestId)).OutgoingId!.Value);
        Assert.Equal(["Crear catálogo", "Cambiar algo"], buttons.QuickReplies.Select(reply => reply.Title));
        return (submitted.RequestId, buttons);
    }

    private static Task<WhatsAppAssistantRequest> TapReferenceAsync(WhatsAppAssistantScenario host, Guid contactId, Guid tapId) => host.ScopeAsync(services =>
        services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .SingleAsync(row => row.ContactId == contactId && row.SourceMessageId == tapId, Ct));

    private async Task<WhatsAppAssistantScenario> CreateAsync(WhatsAppDocumentProbe probe, ProposalLookup lookup)
    {
        var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, builder => builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IPrivateFileStore>(probe));
            services.Replace(ServiceDescriptor.Singleton<IWhatsAppMediaClient>(probe));
            services.Replace(ServiceDescriptor.Singleton<IAssistantProposalLookup>(lookup));
        }));
        host.Operations.AllowSubmit = true;
        return host;
    }

    // A linked person of the automatic assistant who manages catalogs, far from the contact's first message.
    private static async Task<WhatsAppAssistantContact> PersonAsync(WhatsAppAssistantScenario host)
    {
        var person = await host.ContactAsync(Ct, automatic: true);
        await host.InTransactionAsync(async services =>
        {
            var role = "spreadsheet-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, ["formrecords.create", "formcatalogs.read", "formcatalogs.manage"], Ct);
            await services.GetRequiredService<IUserRepository>().SetRolesAsync(person.RecipientId, [role], Ct);
            return true;
        }, Ct);
        host.Clock.Advance(TimeSpan.FromMinutes(11));
        return person;
    }

    private static Task<string> ReceiveSpreadsheetAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact person, WhatsAppDocumentProbe probe,
        string? caption) => ReceiveAsync(host, person, (waId, userIdentifier, waMessageId, at) => MetaWebhook.Document(waMessageId, waId, userIdentifier, at,
            "clientes.csv", "text/csv", caption, sha256: Convert.ToBase64String(SHA256.HashData(probe.Bytes))));

    private static Task<string> ReceiveTextAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact person, string body) =>
        ReceiveAsync(host, person, (waId, userIdentifier, waMessageId, at) => MetaWebhook.Text(waMessageId, waId, userIdentifier, at, body));

    private static async Task<string> ReceiveAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact person,
        Func<string?, string?, string, DateTime, System.Text.Json.Nodes.JsonObject> build)
    {
        var contact = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().AsNoTracking()
            .SingleAsync(row => row.Id == person.ContactId, Ct));
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var body = MetaWebhook.Build(contacts: [MetaWebhook.Contact(contact.WaId, contact.UserIdentifier, contact.ProfileName)],
            messages: [build(contact.WaId, contact.UserIdentifier, waMessageId, host.Clock.GetUtcNow().UtcDateTime)]);
        using var client = host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return waMessageId;
    }

    private static Task ProcessAsync(WhatsAppAssistantScenario host) =>
        host.Host.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);

    // What the engine would observe: the owner's capability runs with the prepared sheet the operation carried, or inherited, if any.
    private static Task<AssistantObservedProposal> ProposeAsync(WhatsAppAssistantScenario host, AssistantOperationRequest operation,
        AssistantCapabilityReference capability, string arguments) => host.ScopeAsync(async services =>
    {
        var source = services.GetServices<IAssistantCapabilitySource>()
            .Single(item => item.Definitions.Any(definition => definition.Id == capability.Id && definition.Version == capability.Version));
        var sheet = operation.Attachment?.Spreadsheet ?? host.Operations.SubmitRequests.LastOrDefault(request => request.Attachment is not null)?.Attachment?.Spreadsheet;
        var executed = await source.ExecuteAsync(new AssistantCapabilityRequest(Guid.NewGuid(), operation.Context, capability, arguments, []) { Attachment = sheet }, Ct);
        Assert.True(executed.IsSuccess);
        Assert.Equal("proposed", executed.Value.State);
        return new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), capability, executed.Value.Payload);
    });

    private static async Task CompleteAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact person, AssistantOperationRequest operation,
        AssistantObservedProposal proposal)
    {
        var outcome = new AssistantOperationResponse(operation.RequestId, "completed", proposal,
            new("http-fixture", 1, "http-fixture", 1, operation.OccurredAtUtc, operation.Context.ChannelId), null);
        host.Operations.Complete(outcome);
        await host.CompleteOutcomeAsync(person.ContactId, outcome, Ct);
    }

    private static Task<WhatsAppMessage> MessageAsync(WhatsAppAssistantScenario host, string waMessageId) =>
        host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
            .SingleAsync(row => row.WaMessageId == waMessageId, Ct));

    private static Task<List<WhatsAppAssistantRequest>> ReferencesAsync(WhatsAppAssistantScenario host, Guid contactId) =>
        host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .Where(row => row.ContactId == contactId).ToListAsync(Ct));

    private static Task<WhatsAppAssistantRequest> ReferenceAsync(WhatsAppAssistantScenario host, Guid requestId) => host.ScopeAsync(services =>
        services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking().SingleAsync(row => row.RequestId == requestId, Ct));

    private static Task<WhatsAppOutgoingMessage> OutgoingAsync(WhatsAppAssistantScenario host, Guid outgoingId) => host.ScopeAsync(services =>
        services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking().SingleAsync(row => row.Id == outgoingId, Ct));

    // The reply to a tap is the outgoing of the reference whose source is that tap.
    private static async Task<WhatsAppOutgoingMessage> ReplyAsync(WhatsAppAssistantScenario host, Guid contactId, Guid tapId)
    {
        var reference = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .SingleAsync(row => row.ContactId == contactId && row.SourceMessageId == tapId, Ct));
        return await OutgoingAsync(host, reference.OutgoingId!.Value);
    }

    private static async Task ProcessTapAsync(WhatsAppAssistantScenario host, Guid contactId) =>
        Assert.True((await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantAuthoringService>().ProcessTapAsync(contactId, Ct))).IsSuccess);

    private static Task<Guid> TapAsync(WhatsAppAssistantScenario host, Guid contactId, string replyId) => host.InTransactionAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == contactId, Ct);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var message = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.ButtonReply, "Botón", replyId, now);
        message.Classify(WhatsAppMessagePurpose.Business); message.SetInboundSequence(contact.RegisterInboundMessage()); message.MarkProcessed(now);
        contact.RecordInbound(contact.WaId, contact.UserIdentifier, contact.ProfileName, now); db.Add(message);
        Assert.True(WhatsAppAssistantAuthoringReplyCodec.TryParse(replyId, out _, out _, out _));
        return message.Id;
    }, Ct);

    // A catalog is counted by its own table, without naming the module's types.
    private static Task<int> CatalogCountAsync(WhatsAppAssistantScenario host, Guid catalogId) => host.ScopeAsync(services =>
        services.GetRequiredService<ApplicationDbContext>().Database.SqlQuery<int>(
            $"""SELECT COUNT(*)::int AS "Value" FROM "BusinessFormCatalogs" WHERE "Id" = {catalogId}""").SingleAsync(Ct));

    // The engine's lookup for the import the app asks for: the operations double never keeps turns of its own.
    private sealed class ProposalLookup : IAssistantProposalLookup
    {
        public AssistantProposalLookupResponse? Answer { get; set; }
        public Guid? Actor { get; private set; }
        public Guid? RequestId { get; private set; }
        public Task<Result<AssistantProposalLookupResponse>> FindAsync(Guid actorUserId, Guid requestId, Guid proposalId,
            IReadOnlyCollection<string> capabilityGroup, CancellationToken cancellationToken)
        {
            (Actor, RequestId) = (actorUserId, requestId);
            return Task.FromResult(Answer is { } answer && answer.Proposal.Id == proposalId
                ? Result.Success(answer) : Result.Failure<AssistantProposalLookupResponse>(SystemQueryErrors.NotFound));
        }
    }
}
