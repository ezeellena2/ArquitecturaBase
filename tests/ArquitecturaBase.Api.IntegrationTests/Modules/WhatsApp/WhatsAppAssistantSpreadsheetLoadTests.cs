using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// VC1-A (task 29) with PostgreSQL: a spreadsheet of someone who loads, alone, goes from the signed webhook to its download prepared to load its
// rows and to an interpretation with only the loading capabilities; its proposal goes to the inbox, which says what became of each row, and the
// channel sends the notice and up to three loads, each its Flow with its body and its footer, which go out to Meta simulated after the notice.
// The loads spend no offers. Meta's download and the private storage are the probe's, the engine is the scenario's double, the owner of the
// rows and the inbox are neutral capturing owners and the owner of the spreadsheets (who prepares the rows) is the real one.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantSpreadsheetLoadTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Seven rows: three loads, one past the limit, one that fits no form, one that cannot be sent and one equal to the first. The thread is the
    // `spreadsheet-loads-thread` of the whatsapp-audio-http cohort.
    [Fact]
    public async Task Seven_rows_send_the_notice_and_three_loads_with_their_footer_and_the_chat_says_why_without_spending_offers()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv(7) };
        var (rows, inbox) = (new CapturingStructuredDocumentRowsChannel(), new CapturingStructuredDocumentChannel());
        await using var host = await CreateAsync(probe, rows, inbox);
        var visit = new StructuredDocumentFormChoice(host.Forms.Form.DefinitionId, host.Forms.Form.VersionId, "Registrar visita");
        var loads = new[] { Load(2, 1, visit, "$ 95.000", "Ferretería Norte"), Load(3, 2, visit, "$ 12.500", "Kiosco Sur"), Load(4, 3, visit, "$ 7.000", "Bar Centro") };
        rows.Reply = _ => new(StructuredDocumentReplyKind.Sure)
        {
            Rows = [.. loads, new(Guid.NewGuid(), null, 5, StructuredDocumentRowKind.OverLimit) { Form = visit },
                new(Guid.NewGuid(), null, 6, StructuredDocumentRowKind.Unmatched), new(Guid.NewGuid(), null, 7, StructuredDocumentRowKind.NotSendable) { Form = visit },
                new(Guid.NewGuid(), null, 8, StructuredDocumentRowKind.Repeated) { Form = visit, SameAs = new(null, 2) }],
            ReplyExpiresAtUtc = host.Clock.GetUtcNow().UtcDateTime.AddHours(24),
        };
        inbox.Preparation = Prepared(host);
        var person = await PersonAsync(host);
        var waMessageId = await ReceiveSpreadsheetAsync(host, person, probe);

        var operation = await InterpretAsync(host);

        var sheet = await MessageAsync(host, waMessageId);
        Assert.Equal(WhatsAppAssistantAccessPolicy.SpreadsheetCapabilities, operation.Context.AllowedCapabilities);
        Assert.Equal((7, $"whatsapp/documents/{sheet.Id:N}.csv"), (operation.Attachment!.Spreadsheet!.DataRowCount, operation.Attachment.StorageKey));
        await CompleteAsync(host, person, operation);

        var request = Assert.Single(rows.Requests);
        Assert.Equal(("whatsapp", sheet.Id.ToString("N"), person.RecipientId, "text/csv"), (request.ChannelId, request.SourceId, request.SenderUserId, request.Copy.MediaType));
        var reference = await ReferenceAsync(host, operation.RequestId);
        var notice = await OutgoingAsync(host, reference.OutgoingId!.Value);
        Assert.Equal((WhatsAppOutgoingMessageKind.Text, "Recibí una planilla con 7 filas de «Registrar visita». Te preparé un formulario para cada una de las 3 primeras: "
            + "revisalos y confirmalos. La fila 8 es igual a la fila 2: no la cargo de nuevo. Las otras 3 se las pasé a una persona del equipo."), (notice.Kind, notice.Body));
        var references = await LoadReferencesAsync(host, operation.RequestId);
        Assert.Equal([1, 2, 3], references.Select(row => row.ActionOrdinal));
        Assert.All(references, row => Assert.Equal(sheet.Id, row.AttachmentMessageId));
        var flows = new List<WhatsAppOutgoingMessage>();
        foreach (var row in references) flows.Add(await OutgoingAsync(host, row.OutgoingId!.Value));
        Assert.Equal(["1 de 3 · $ 95.000 · Ferretería Norte", "2 de 3 · $ 12.500 · Kiosco Sur", "3 de 3 · $ 7.000 · Bar Centro"], flows.Select(flow => flow.Body));
        Assert.All(flows, flow => Assert.Equal((WhatsAppOutgoingMessageKind.Flow, "Registrar visita · prellenado"), (flow.Kind, flow.Footer)));
        Assert.Equal(loads.Select(row => row.DocumentId), inbox.Sends.Select(send => send.DocumentId));
        // The loads answer the file the person sent: no offer of the day is spent.
        var now = host.Clock.GetUtcNow().UtcDateTime;
        Assert.Equal(0, await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantReader>().CountOffersAsync(person.ContactId, now.AddDays(-1), now.AddDays(1), Ct)));

        var worker = host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        for (var round = 0; round < 12 && host.Cloud.Messages.OfType<WhatsAppFlowMessageResponse>().Count() < 3; round++) await worker.ProcessNextAsync(Ct);
        Assert.IsType<WhatsAppTextMessage>(host.Cloud.Messages[0]);
        Assert.Equal(["1 de 3 · $ 95.000 · Ferretería Norte", "2 de 3 · $ 12.500 · Kiosco Sur", "3 de 3 · $ 7.000 · Bar Centro"],
            host.Cloud.Messages.OfType<WhatsAppFlowMessageResponse>().Select(flow => flow.Body));
        Assert.All(host.Cloud.Messages.OfType<WhatsAppFlowMessageResponse>(), flow => Assert.Equal("Registrar visita · prellenado", flow.Footer));

        using var reader = host.Client(host.FirstOperator);
        using var response = await reader.GetAsync($"/api/whatsapp/chats/{person.ContactId}/messages", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("\"footer\"", raw, StringComparison.Ordinal);
        var thread = JsonDocument.Parse(raw).RootElement;
        var messages = thread.GetProperty("messages").EnumerateArray().ToArray();
        var why = messages.Single(message => message.GetProperty("body").ValueKind == JsonValueKind.String
            && message.GetProperty("body").GetString() == notice.Body).GetProperty("spreadsheetRows");
        Assert.Equal("""{"recognized":[{"form":"Registrar visita","count":6}],"sent":[{"sheet":null,"number":2},{"sheet":null,"number":3},{"sheet":null,"number":4}],"forTeam":[{"sheet":null,"number":5},{"sheet":null,"number":6},{"sheet":null,"number":7}],"repeated":[{"row":{"sheet":null,"number":8},"recordNumber":null,"sameAs":{"sheet":null,"number":2}}]}""",
            why.GetRawText());
        Assert.Equal(3, messages.Count(message => message.GetProperty("kind").GetString() == "Flow" && message.GetProperty("loadsFile").GetBoolean()));
        await WhatsAppAudioContractCapture.WriteAsync(response, "spreadsheet-loads-thread", Ct);
    }

    // Two spreadsheets in a row, the first with rows of two forms and a repeated one: each is a message with its own notice and up to three loads.
    [Fact]
    public async Task Two_spreadsheets_in_a_row_each_send_their_notice_and_their_loads_and_rows_of_two_forms_name_no_form()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv(4) };
        var (rows, inbox) = (new CapturingStructuredDocumentRowsChannel(), new CapturingStructuredDocumentChannel());
        await using var host = await CreateAsync(probe, rows, inbox);
        var visit = new StructuredDocumentFormChoice(host.Forms.Form.DefinitionId, host.Forms.Form.VersionId, "Registrar visita");
        var maintenance = new StructuredDocumentFormChoice(host.Forms.AlternativeForm.DefinitionId, host.Forms.AlternativeForm.VersionId, "Registrar mantenimiento");
        var repeatedAt = new DateTime(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc);
        rows.Reply = request => rows.Requests.Count == 1
            ? new(StructuredDocumentReplyKind.Sure)
            {
                Rows = [Load(2, 1, visit, "$ 95.000"), Load(3, 2, maintenance, "Caldera"),
                    new(Guid.NewGuid(), null, 4, StructuredDocumentRowKind.Repeated) { Form = visit, Duplicate = new(repeatedAt, "VIS-000012") }, Load(5, 3, visit, "$ 7.000")],
            }
            : new(StructuredDocumentReplyKind.Sure) { Rows = [Load(2, 1, visit, "$ 1.000"), Load(3, 2, visit, "$ 2.000"), Load(4, 3, visit, "$ 3.000")] };
        inbox.Preparation = Prepared(host);
        var person = await PersonAsync(host);
        await ReceiveSpreadsheetAsync(host, person, probe);
        var first = await InterpretAsync(host);
        await CompleteAsync(host, person, first);
        probe.Bytes = Csv(3);
        await ReceiveSpreadsheetAsync(host, person, probe);
        var second = await InterpretAsync(host);
        await CompleteAsync(host, person, second);

        Assert.Equal("Recibí una planilla con 4 filas. Te preparé un formulario por cada una: revisalos y confirmalos. La fila 4 ya se cargó el 10/11/2026 en VIS-000012: "
            + "no la cargo de nuevo.", (await OutgoingAsync(host, (await ReferenceAsync(host, first.RequestId)).OutgoingId!.Value)).Body);
        Assert.Equal("Recibí una planilla con 3 filas de «Registrar visita». Te preparé un formulario por cada una: revisalos y confirmalos.",
            (await OutgoingAsync(host, (await ReferenceAsync(host, second.RequestId)).OutgoingId!.Value)).Body);
        var footers = new List<string?>();
        foreach (var row in (await LoadReferencesAsync(host, first.RequestId)).Concat(await LoadReferencesAsync(host, second.RequestId)))
            footers.Add((await OutgoingAsync(host, row.OutgoingId!.Value)).Footer);
        Assert.Equal(["Registrar visita · prellenado", "Registrar mantenimiento · prellenado", "Registrar visita · prellenado", "Registrar visita · prellenado",
            "Registrar visita · prellenado", "Registrar visita · prellenado"], footers);
        Assert.Equal(2, rows.Requests.Select(request => request.SourceId).Distinct().Count());
    }

    // None fits: «No lo reconoce» with the list and «Otro», which leaves it for the team.
    [Fact]
    public async Task A_spreadsheet_none_of_whose_rows_fit_offers_the_list_and_another_leaves_it_for_the_team()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv(2) };
        var (rows, inbox) = (new CapturingStructuredDocumentRowsChannel(), new CapturingStructuredDocumentChannel());
        await using var host = await CreateAsync(probe, rows, inbox);
        var entry = Guid.NewGuid();
        rows.Reply = _ => new(StructuredDocumentReplyKind.Unknown) { DocumentId = entry, Forms = [new(Guid.NewGuid(), Guid.NewGuid(), "Cobranzas"), new(Guid.NewGuid(), Guid.NewGuid(), "Gastos")] };
        // The capturing inbox records «Otro» only while it has something to answer.
        inbox.ReadReply = new(entry, StructuredDocumentReplyKind.Unknown);
        var person = await PersonAsync(host);
        await ReceiveSpreadsheetAsync(host, person, probe);
        var operation = await InterpretAsync(host);
        await CompleteAsync(host, person, operation);

        var list = await OutgoingAsync(host, (await ReferenceAsync(host, operation.RequestId)).OutgoingId!.Value);
        Assert.Equal((WhatsAppOutgoingMessageKind.List, "Elegir formulario", "No reconocí las filas de esta planilla. ¿Qué querés cargar con ella?"),
            (list.Kind, list.ListButton, list.Body));
        Assert.Equal(["Cobranzas", "Gastos", "Otro"], list.QuickReplies.Select(row => row.Title));
        var tap = await TapAsync(host, person.ContactId, list.QuickReplies[^1].Id, "Otro");
        await ProcessAsync(host);

        Assert.Equal((entry, person.RecipientId), (Assert.Single(inbox.Others).DocumentId, inbox.Others[0].SenderUserId));
        Assert.Equal("Listo, lo dejé para que lo revise el equipo. Si hace falta, te escriben por acá.", (await ReplyAsync(host, person.ContactId, tap)).Body);
    }

    // DOC4: a number without a linked account is neither downloaded, nor registered nor answered; a spreadsheet over the fifty rows to load is
    // answered with its limit and enters the inbox discarded, without spending an interpretation.
    [Fact]
    public async Task A_number_without_account_is_left_alone_and_a_spreadsheet_over_fifty_rows_is_answered_and_discarded()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv(2) };
        var (rows, inbox) = (new CapturingStructuredDocumentRowsChannel(), new CapturingStructuredDocumentChannel());
        await using var host = await CreateAsync(probe, rows, inbox);
        var unlinked = await PersonAsync(host);
        await host.InTransactionAsync(async services =>
        {
            (await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().SingleAsync(row => row.Id == unlinked.ContactId, Ct)).UnlinkUser();
            return true;
        }, Ct);
        await ReceiveSpreadsheetAsync(host, unlinked, probe);
        await ProcessAsync(host);
        Assert.Equal((0, 0, 0, 0), (probe.Downloads, rows.Requests.Count, inbox.Registrations.Count, host.Operations.SubmitRequests.Count));

        var person = await PersonAsync(host);
        probe.Bytes = Csv(51);
        var waMessageId = await ReceiveSpreadsheetAsync(host, person, probe);
        await ProcessAsync(host);

        var sheet = await MessageAsync(host, waMessageId);
        var registered = Assert.Single(inbox.Registrations);
        Assert.Equal((sheet.Id.ToString("N"), (StructuredDocumentNotRead?)StructuredDocumentNotRead.Rejected, (StructuredAttachmentRejection?)StructuredAttachmentRejection.TooManyRows,
            "planilla.csv", null), (registered.SourceId, registered.NotRead, registered.Rejection, registered.File!.Name, registered.Copy));
        var reply = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .SingleAsync(row => row.ContactId == person.ContactId && row.SourceMessageId == sheet.Id, Ct));
        Assert.Equal("Por acá cargo planillas de hasta 50 filas. Dividila y mandámela de nuevo.", (await OutgoingAsync(host, reply.OutgoingId!.Value)).Body);
        Assert.Equal((0, 0), (rows.Requests.Count, host.Operations.SubmitRequests.Count));
    }

    // Review of task 29: a text («cargalas») that arrives while the outcome of a spreadsheet alone registers its rows replaces that reference,
    // and the text's own interpretation answers them from what the inbox kept: the notice and the loads are not lost.
    [Fact]
    public async Task A_text_that_arrives_while_a_spreadsheet_alone_registers_its_rows_gets_its_notice_and_its_loads()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv(3) };
        var (rows, inbox) = (new CapturingStructuredDocumentRowsChannel(), new CapturingStructuredDocumentChannel());
        await using var host = await CreateAsync(probe, rows, inbox);
        var person = await PersonAsync(host);
        rows.Reply = _ =>
        {
            host.AddNewerSourceAsync(person, Ct).GetAwaiter().GetResult();
            return Three(host);
        };
        inbox.Preparation = Prepared(host);
        await ReceiveSpreadsheetAsync(host, person, probe);
        var alone = await InterpretAsync(host);

        await CompleteAsync(host, person, alone);

        Assert.Single(rows.Requests);
        Assert.Null((await ReferenceAsync(host, alone.RequestId)).OutgoingId);
        var text = await InterpretAsync(host);
        await CompleteAsync(host, person, text);
        Assert.Equal(2, rows.Requests.Count);
        Assert.Equal(AllThree, (await OutgoingAsync(host, (await ReferenceAsync(host, text.RequestId)).OutgoingId!.Value)).Body);
        Assert.Equal([1, 2, 3], (await LoadReferencesAsync(host, text.RequestId)).Select(row => row.ActionOrdinal));
    }

    // Review of task 29: rows registered by an attempt whose unit of work did not commit are answered from the registration the owner kept.
    [Fact]
    public async Task Rows_registered_by_an_attempt_that_did_not_commit_are_answered_from_the_registration()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv(3) };
        var (rows, inbox) = (new CapturingStructuredDocumentRowsChannel(), new CapturingStructuredDocumentChannel());
        await using var host = await CreateAsync(probe, rows, inbox);
        rows.Reply = _ => Three(host);
        inbox.Preparation = Prepared(host);
        var person = await PersonAsync(host);
        var waMessageId = await ReceiveSpreadsheetAsync(host, person, probe);
        var operation = await InterpretAsync(host);
        await RegisterEarlierAsync(host, rows, person, waMessageId);

        await CompleteAsync(host, person, operation);

        Assert.Equal(2, rows.Requests.Count);
        Assert.Equal(AllThree, (await OutgoingAsync(host, (await ReferenceAsync(host, operation.RequestId)).OutgoingId!.Value)).Body);
        Assert.Equal([1, 2, 3], (await LoadReferencesAsync(host, operation.RequestId)).Select(row => row.ActionOrdinal));
    }

    // Review of task 29: two hosts on the same outcome of rows another one registered: one answers them and the other adds nothing.
    [Fact]
    public async Task Two_hosts_on_the_same_outcome_send_one_notice_and_its_loads()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv(3) };
        var (rows, inbox) = (new CapturingStructuredDocumentRowsChannel(), new CapturingStructuredDocumentChannel());
        await using var host = await CreateAsync(probe, rows, inbox);
        rows.Reply = _ => Three(host);
        inbox.Preparation = Prepared(host);
        var person = await PersonAsync(host);
        var waMessageId = await ReceiveSpreadsheetAsync(host, person, probe);
        var operation = await InterpretAsync(host);
        await RegisterEarlierAsync(host, rows, person, waMessageId);
        var outcome = Outcome(operation);
        host.Operations.Complete(outcome);

        await Task.WhenAll(host.CompleteOutcomeAsync(person.ContactId, outcome, Ct), host.CompleteOutcomeAsync(person.ContactId, outcome, Ct));

        var sent = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking()
            .Where(row => row.ContactId == person.ContactId).ToListAsync(Ct));
        Assert.Equal(1, sent.Count(row => row.Body == AllThree));
        Assert.Equal(3, sent.Count(row => row.Kind == WhatsAppOutgoingMessageKind.Flow));
    }

    // Review of task 29: a load whose form the inbox cannot prepare now does not go, and the notice, «Por qué lo propuso» and «i de k» say
    // only what went: the row goes in the team's sentence.
    [Fact]
    public async Task A_load_that_cannot_be_prepared_goes_to_the_team_and_the_notice_and_the_loads_say_only_what_went()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv(3) };
        var (rows, inbox) = (new CapturingStructuredDocumentRowsChannel(), new CapturingStructuredDocumentChannel());
        await using var host = await CreateAsync(probe, rows, inbox);
        var registration = Three(host);
        rows.Reply = _ => registration;
        inbox.Prepare = request => request.DocumentId == registration.Rows[1].DocumentId ? new(StructuredDocumentSendState.NotSendable) : Prepared(host);
        var person = await PersonAsync(host);
        await ReceiveSpreadsheetAsync(host, person, probe);
        var operation = await InterpretAsync(host);

        await CompleteAsync(host, person, operation);

        var notice = await OutgoingAsync(host, (await ReferenceAsync(host, operation.RequestId)).OutgoingId!.Value);
        Assert.Equal("Recibí una planilla con 3 filas de «Registrar visita». Te preparé un formulario por cada una: revisalos y confirmalos. "
            + "La otra se la pasé a una persona del equipo.", notice.Body);
        // The column is jsonb: what it keeps is read, not its spelling.
        var why = JsonDocument.Parse(notice.SpreadsheetRowsJson!).RootElement;
        Assert.Equal([2, 4], why.GetProperty("sent").EnumerateArray().Select(row => row.GetProperty("number").GetInt32()));
        Assert.Equal([3], why.GetProperty("forTeam").EnumerateArray().Select(row => row.GetProperty("number").GetInt32()));
        var flows = new List<string?>();
        foreach (var row in await LoadReferencesAsync(host, operation.RequestId)) flows.Add((await OutgoingAsync(host, row.OutgoingId!.Value)).Body);
        Assert.Equal(["1 de 2 · $ 1.000", "2 de 2 · $ 3.000"], flows);
    }

    // VC1-A (rule B7): over the day's quota a spreadsheet to load goes to the inbox unclassified, outside the transaction, and the person is
    // told with the documents' text.
    [Fact]
    public async Task Over_the_quota_a_spreadsheet_to_load_goes_to_the_inbox_unclassified_and_the_person_is_told()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv(3) };
        var (rows, inbox) = (new CapturingStructuredDocumentRowsChannel(), new CapturingStructuredDocumentChannel());
        await using var host = await CreateAsync(probe, rows, inbox);
        rows.Reply = request => new(StructuredDocumentReplyKind.Quota) { DocumentId = Guid.NewGuid() };
        var person = await PersonAsync(host);
        var waMessageId = await ReceiveSpreadsheetAsync(host, person, probe);
        host.Operations.AllowSubmit = true;
        host.Operations.SubmissionFailure = Error.TooManyRequests("Ai.Assistant.QuotaExceeded", "Fictitious quota.", new Dictionary<string, object?> { ["retryAfter"] = 60 });

        await ProcessAsync(host);

        var sheet = await MessageAsync(host, waMessageId);
        var registered = Assert.Single(rows.Requests);
        Assert.Equal((sheet.Id.ToString("N"), (StructuredDocumentNotRead?)StructuredDocumentNotRead.Quota, (AssistantObservedProposal?)null),
            (registered.SourceId, registered.NotRead, registered.Proposal));
        var reference = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .SingleAsync(row => row.ContactId == person.ContactId && row.SourceMessageId == sheet.Id, Ct));
        Assert.Equal("Por hoy llegué al tope de documentos que puedo leer. Lo dejé para que lo revise el equipo.", (await OutgoingAsync(host, reference.OutgoingId!.Value)).Body);
    }

    // VC1-A (task 29): the retention takes «Por qué lo propuso» with the body, from a notice that went out and from its message; a recent one stays.
    [Fact]
    public async Task Retention_takes_why_it_proposed_with_the_body_from_the_notice_and_its_message()
    {
        const string Why = """{"recognized":[],"sent":[{"sheet":null,"number":2}],"forTeam":[],"repeated":[]}""";
        const string Body = "Recibí una planilla con 1 fila de «Cobranzas».";
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var waId = MetaWebhook.UniqueWaId();
        var contact = WhatsAppContact.Create(waId, MetaWebhook.UniqueBsuid(), "Persona ficticia", now.AddDays(-100));
        WhatsAppOutgoingMessage Notice(DateTime at)
        {
            var notice = WhatsAppOutgoingMessage.CreateAssistantText(Guid.NewGuid(), contact.Id, Guid.NewGuid(), Body, at, 1, waId, 1);
            Assert.True(notice.ExplainSpreadsheetRows(Why));
            return notice;
        }
        WhatsAppMessage Sent(DateTime at)
        {
            var message = WhatsAppMessage.Outbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Text, Body, at);
            Assert.True(message.RecordSpreadsheetRows(Why));
            return message;
        }
        var (oldNotice, newNotice, oldMessage, newMessage) = (Notice(now.AddDays(-91)), Notice(now), Sent(now.AddDays(-91)), Sent(now));
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.AddRange(contact, oldNotice, newNotice, oldMessage, newMessage);
            await db.SaveChangesAsync(Ct);
            // Both went out: the retention leaves an outgoing message alone while it is pending or sending.
            await db.Database.ExecuteSqlAsync($"""UPDATE "WhatsAppOutgoingMessages" SET "State" = 'Accepted' WHERE "ContactId" = {contact.Id}""", Ct);
        });

        await factory.ExecuteScopeAsync(services => services.GetRequiredService<IWhatsAppMessageRetentionRepository>().ClearExpiredBodiesAsync(now.AddDays(-90), Ct));

        var outgoing = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppOutgoingMessage>().AsNoTracking().Where(row => row.ContactId == contact.Id)
            .ToDictionaryAsync(row => row.Id, row => (row.Body, row.SpreadsheetRowsJson), Ct));
        Assert.Equal(((string?)null, (string?)null), outgoing[oldNotice.Id]);
        // The column is jsonb: a recent one keeps what it says, not its spelling.
        Assert.Equal((Body, true), (outgoing[newNotice.Id].Body, outgoing[newNotice.Id].SpreadsheetRowsJson is not null));
        var messages = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppMessage>().AsNoTracking().Where(row => row.ContactId == contact.Id)
            .ToDictionaryAsync(row => row.Id, row => (row.Body, row.SpreadsheetRowsJson), Ct));
        Assert.Equal(((string?)null, (string?)null), messages[oldMessage.Id]);
        Assert.Equal((Body, true), (messages[newMessage.Id].Body, messages[newMessage.Id].SpreadsheetRowsJson is not null));
    }

    private const string AllThree = "Recibí una planilla con 3 filas de «Registrar visita». Te preparé un formulario por cada una: revisalos y confirmalos.";

    private static StructuredDocumentRowsRegistration Three(WhatsAppAssistantScenario host)
    {
        var visit = new StructuredDocumentFormChoice(host.Forms.Form.DefinitionId, host.Forms.Form.VersionId, "Registrar visita");
        return new(StructuredDocumentReplyKind.Sure)
        {
            Rows = [Load(2, 1, visit, "$ 1.000"), Load(3, 2, visit, "$ 2.000"), Load(4, 3, visit, "$ 3.000")],
            ReplyExpiresAtUtc = host.Clock.GetUtcNow().UtcDateTime.AddHours(24),
        };
    }

    // What an earlier attempt (or another host) registered: the owner keeps it by channel and source id.
    private static async Task RegisterEarlierAsync(WhatsAppAssistantScenario host, CapturingStructuredDocumentRowsChannel rows, WhatsAppAssistantContact person,
        string waMessageId)
    {
        var sheet = await MessageAsync(host, waMessageId);
        await rows.RegisterRowsAsync(new("whatsapp", sheet.Id.ToString("N"), person.RecipientId, "es", sheet.OccurredAtUtc,
            new StructuredDocumentCopy($"whatsapp/documents/{sheet.Id:N}.csv", "planilla.csv", "text/csv", 1, new string('a', 64), null)), Ct);
    }

    private static AssistantOperationResponse Outcome(AssistantOperationRequest operation) =>
        new(operation.RequestId, "completed", new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), WhatsAppAssistantSpreadsheetPolicy.ProposeRows, new RowsPayload()),
            new("http-fixture", 1, "http-fixture", 1, operation.OccurredAtUtc, operation.Context.ChannelId), null);

    private static StructuredDocumentRowRegistration Load(int number, int position, StructuredDocumentFormChoice form, params string[] summary) =>
        new(Guid.NewGuid(), null, number, StructuredDocumentRowKind.Load) { Form = form, Position = position, Summary = summary };

    // The form the inbox prepares for each load: the scenario's published form with the place of the row.
    private static StructuredDocumentSendPreparation Prepared(WhatsAppAssistantScenario host) => new(StructuredDocumentSendState.Ready)
    {
        Prefill = new(host.Forms.Form, 1, [new(host.Forms.FieldId, "field_" + host.Forms.FieldId.ToString("N"), "shortText", "Lugar", "Sucursal Norte", "Sucursal Norte")],
            [], "Leyó la fila.", "es"),
    };

    private static byte[] Csv(int rows) =>
        Encoding.UTF8.GetBytes("Fecha,Cliente,Monto\n" + string.Concat(Enumerable.Range(1, rows).Select(index => $"01/11/2026,Cliente {index},{index * 1000}\n")));

    private async Task<WhatsAppAssistantScenario> CreateAsync(WhatsAppDocumentProbe probe, CapturingStructuredDocumentRowsChannel rows, CapturingStructuredDocumentChannel inbox) =>
        await WhatsAppAssistantScenario.CreateAsync(factory, Ct, builder => builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IPrivateFileStore>(probe));
            services.Replace(ServiceDescriptor.Singleton<IWhatsAppMediaClient>(probe));
            services.Replace(ServiceDescriptor.Singleton<IStructuredDocumentChannel>(inbox));
            services.Replace(ServiceDescriptor.Singleton<IStructuredDocumentRowsChannel>(rows));
        }));

    // A linked person of the automatic assistant, far from the contact's first message: the inbox says they can load.
    private static async Task<WhatsAppAssistantContact> PersonAsync(WhatsAppAssistantScenario host)
    {
        var person = await host.ContactAsync(Ct, automatic: true);
        host.Clock.Advance(TimeSpan.FromMinutes(11));
        return person;
    }

    // The spreadsheet is downloaded, prepared to load its rows and interpreted alone.
    private static async Task<AssistantOperationRequest> InterpretAsync(WhatsAppAssistantScenario host)
    {
        host.Operations.AllowSubmit = true;
        var before = host.Operations.SubmitRequests.Count;
        await ProcessAsync(host);
        Assert.Equal(before + 1, host.Operations.SubmitRequests.Count);
        return host.Operations.SubmitRequests[^1];
    }

    private static async Task CompleteAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact person, AssistantOperationRequest operation)
    {
        var proposal = new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), WhatsAppAssistantSpreadsheetPolicy.ProposeRows, new RowsPayload());
        var outcome = new AssistantOperationResponse(operation.RequestId, "completed", proposal,
            new("http-fixture", 1, "http-fixture", 1, operation.OccurredAtUtc, operation.Context.ChannelId), null);
        host.Operations.Complete(outcome);
        await host.CompleteOutcomeAsync(person.ContactId, outcome, Ct);
    }

    private sealed record RowsPayload : AssistantCapabilityPayload;

    private static async Task<string> ReceiveSpreadsheetAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact person, WhatsAppDocumentProbe probe)
    {
        var contact = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().AsNoTracking()
            .SingleAsync(row => row.Id == person.ContactId, Ct));
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var body = MetaWebhook.Build(contacts: [MetaWebhook.Contact(contact.WaId, contact.UserIdentifier, contact.ProfileName)],
            messages: [MetaWebhook.Document(waMessageId, contact.WaId, contact.UserIdentifier, host.Clock.GetUtcNow().UtcDateTime, "planilla.csv", "text/csv", null,
                sha256: Convert.ToBase64String(SHA256.HashData(probe.Bytes)))]);
        using var client = host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return waMessageId;
    }

    private static Task<Guid> TapAsync(WhatsAppAssistantScenario host, Guid contactId, string replyId, string title) => host.InTransactionAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == contactId, Ct);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var message = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.ButtonReply, title, replyId, now);
        message.Classify(WhatsAppMessagePurpose.Business); message.SetInboundSequence(contact.RegisterInboundMessage()); message.MarkProcessed(now);
        contact.RecordInbound(contact.WaId, contact.UserIdentifier, contact.ProfileName, now); db.Add(message);
        return message.Id;
    }, Ct);

    private static Task ProcessAsync(WhatsAppAssistantScenario host) =>
        host.Host.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);

    private static Task<WhatsAppMessage> MessageAsync(WhatsAppAssistantScenario host, string waMessageId) =>
        host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
            .SingleAsync(row => row.WaMessageId == waMessageId, Ct));

    private static Task<WhatsAppAssistantRequest> ReferenceAsync(WhatsAppAssistantScenario host, Guid requestId) => host.ScopeAsync(services =>
        services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking().SingleAsync(row => row.RequestId == requestId, Ct));

    private static Task<List<WhatsAppAssistantRequest>> LoadReferencesAsync(WhatsAppAssistantScenario host, Guid requestId) => host.ScopeAsync(services =>
        services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .Where(row => row.OperationRequestId == requestId && row.ActionOrdinal > 0).OrderBy(row => row.ActionOrdinal).ToListAsync(Ct));

    private static Task<WhatsAppOutgoingMessage> OutgoingAsync(WhatsAppAssistantScenario host, Guid outgoingId) => host.ScopeAsync(services =>
        services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking().SingleAsync(row => row.Id == outgoingId, Ct));

    private static async Task<WhatsAppOutgoingMessage> ReplyAsync(WhatsAppAssistantScenario host, Guid contactId, Guid tapId)
    {
        var reference = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .SingleAsync(row => row.ContactId == contactId && row.SourceMessageId == tapId, Ct));
        return await OutgoingAsync(host, reference.OutgoingId!.Value);
    }
}
