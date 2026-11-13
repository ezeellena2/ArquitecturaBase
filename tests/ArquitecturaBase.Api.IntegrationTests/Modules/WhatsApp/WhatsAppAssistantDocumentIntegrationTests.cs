using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// Task 9 of the documents with PostgreSQL: a PDF with its request goes from the signed webhook to its reading and to an interpretation that
// carries it with the documents' capabilities; its proposal goes to the inbox and the doubt asks with three buttons, which the reader takes
// back as the latest document question; «Otro» leaves it for the team. A document not recognized offers a list kept with its button. Meta's
// download and the private storage are the probe's, the engine is the scenario's double and the inbox a neutral capturing owner.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantDocumentIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly StructuredDocumentReading Reading = new(StructuredDocumentReadability.Readable, null, "es", "Transferencia $ 150.000", false);

    [Fact]
    public async Task A_document_with_its_request_is_interpreted_with_its_reading_and_a_doubt_asks_and_another_leaves_it_for_the_team()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var clients = new StructuredDocumentFormChoice(Guid.NewGuid(), Guid.NewGuid(), "Pagos de clientes");
        var providers = new StructuredDocumentFormChoice(Guid.NewGuid(), Guid.NewGuid(), "Pagos a proveedores");
        var inbox = new CapturingStructuredDocumentChannel { ReadReply = new(null, StructuredDocumentReplyKind.Doubt) { Forms = [clients, providers] } };
        await using var host = await CreateAsync(probe, inbox);
        var person = await host.ContactAsync(Ct, automatic: true);
        var waMessageId = await ReceiveAsync(host, person, probe, "Cargalo en pagos");

        var operation = await ReadAndInterpretAsync(host, waMessageId);

        var message = await MessageAsync(host, waMessageId);
        Assert.Equal("Cargalo en pagos", operation.Text);
        Assert.Equal(($"whatsapp/documents/{message.Id:N}.pdf", AssistantAttachmentKind.Document, Reading),
            (operation.Attachment!.StorageKey, operation.Attachment.Kind, operation.Attachment.Document!.Reading));
        Assert.Equal(WhatsAppAssistantAccessPolicy.DocumentCapabilities, operation.Context.AllowedCapabilities.TakeLast(2));
        Assert.Contains(new AssistantCapabilityReference("forms.authoring.propose-form", 2), operation.Context.AllowedCapabilities);
        Assert.Equal((Guid?)message.Id, (await ReferenceAsync(host, operation.RequestId)).AttachmentMessageId);

        var proposal = await CompleteAsync(host, person, operation);

        var registered = inbox.Registrations.Single(request => request.Reading is not null);
        Assert.Equal(("whatsapp", message.Id.ToString("N"), person.RecipientId, proposal.Id, Reading),
            (registered.ChannelId, registered.SourceId, registered.SenderUserId, registered.Proposal!.Id, registered.Reading));
        Assert.Equal(host.Clock.GetUtcNow().UtcDateTime.AddHours(24), registered.ReplyExpiresAtUtc);
        var reference = await ReferenceAsync(host, operation.RequestId);
        var question = await OutgoingAsync(host, reference.OutgoingId!.Value);
        Assert.Equal((WhatsAppOutgoingMessageKind.QuickReplies, "¿Qué es este documento?"), (question.Kind, question.Body));
        Assert.Equal(["Pagos de clientes", "Pagos a proveedores", "Otro"], question.QuickReplies.Select(reply => reply.Title));
        Assert.Equal(operation.RequestId, await host.ScopeAsync(services =>
            services.GetRequiredService<IWhatsAppAssistantReader>().FindLatestDocumentRequestAsync(person.ContactId, Ct)));

        // «Otro»: it stays for the team, with its fixed text, without another interpretation.
        var submits = host.Operations.SubmitRequests.Count;
        var tap = await TapAsync(host, person.ContactId, question.QuickReplies[2].Id, "Otro");
        await ProcessAsync(host);

        var other = Assert.Single(inbox.Others);
        Assert.Equal((message.Id.ToString("N"), person.RecipientId, "es"), (other.SourceId, other.SenderUserId, other.Culture));
        var reply = await ReplyAsync(host, person.ContactId, tap);
        Assert.Equal("Listo, lo dejé para que lo revise el equipo. Si hace falta, te escriben por acá.", reply.Body);
        Assert.Equal(submits, host.Operations.SubmitRequests.Count);
        Assert.NotNull((await MessageByIdAsync(host, tap)).AssistantProcessedAtUtc);
    }

    [Fact]
    public async Task A_document_not_recognized_offers_the_list_kept_with_its_button()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var forms = Enumerable.Range(1, 3).Select(index => new StructuredDocumentFormChoice(Guid.NewGuid(), Guid.NewGuid(), "Formulario " + index)).ToArray();
        var inbox = new CapturingStructuredDocumentChannel { ReadReply = new(null, StructuredDocumentReplyKind.Unknown) { Forms = forms } };
        await using var host = await CreateAsync(probe, inbox);
        var person = await host.ContactAsync(Ct, automatic: true);
        var waMessageId = await ReceiveAsync(host, person, probe, "Cargalo");

        var operation = await ReadAndInterpretAsync(host, waMessageId);
        await CompleteAsync(host, person, operation);

        var list = await OutgoingAsync(host, (await ReferenceAsync(host, operation.RequestId)).OutgoingId!.Value);
        Assert.Equal((WhatsAppOutgoingMessageKind.List, "Elegir formulario", "No reconocí este documento. ¿Qué querés cargar con él?"),
            (list.Kind, list.ListButton, list.Body));
        Assert.Equal(["Formulario 1", "Formulario 2", "Formulario 3", "Otro"], list.QuickReplies.Select(row => row.Title));
    }

    // Review of task 9: a candidate chosen sends its form, which goes out although the person once declined that form as an offer (it
    // answers the document the person sent), and is captured with Meta simulated: the response names the inbox's document. The question is
    // answered once: a later tap is told it changed and asks the inbox nothing.
    [Fact]
    public async Task A_chosen_candidate_sends_its_form_which_is_captured_with_the_document_and_a_later_tap_changes_nothing()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var inbox = new CapturingStructuredDocumentChannel();
        await using var host = await CreateAsync(probe, inbox, capture: true);
        var visit = new StructuredDocumentFormChoice(host.Forms.Form.DefinitionId, host.Forms.Form.VersionId, "Registrar visita");
        var maintenance = new StructuredDocumentFormChoice(host.Forms.AlternativeForm.DefinitionId, host.Forms.AlternativeForm.VersionId, "Registrar mantenimiento");
        inbox.ReadReply = new(null, StructuredDocumentReplyKind.Doubt) { Forms = [visit, maintenance] };
        inbox.Preparation = Prepared(host);
        var person = await host.ContactAsync(Ct, automatic: true);
        await DeclineAsync(host, person, host.Forms.Form.DefinitionId);
        var waMessageId = await ReceiveAsync(host, person, probe, "Cargalo");
        var operation = await ReadAndInterpretAsync(host, waMessageId);
        await CompleteAsync(host, person, operation);
        var question = await OutgoingAsync(host, (await ReferenceAsync(host, operation.RequestId)).OutgoingId!.Value);

        var tap = await TapAsync(host, person.ContactId, question.QuickReplies[0].Id, "Registrar visita");
        await ProcessAsync(host);

        var flow = await ReplyAsync(host, person.ContactId, tap);
        Assert.Equal((WhatsAppOutgoingMessageKind.Flow, "Listo: te preparé el formulario con el documento. Revisalo y confirmalo."), (flow.Kind, flow.Body));
        var sent = await DispatchFlowAsync(host);
        await WhatsAppAssistantCaptureFixture.ReplyAsync(host, person, sent, host.Forms.FieldId, Ct);
        var capture = host.Host.Services.GetRequiredService<CapturingStructuredRecordPort>().Captures[^1];
        var document = await MessageAsync(host, waMessageId);
        Assert.Equal((inbox.DocumentOf("whatsapp", document.Id.ToString("N")), StructuredRecordSenderKind.Assistant), (capture.Origin!.Document, capture.Origin.SenderKind));

        var asked = (inbox.Asked.Count, inbox.Sends.Count);
        var later = await TapAsync(host, person.ContactId, question.QuickReplies[1].Id, "Registrar mantenimiento");
        await ProcessAsync(host);

        Assert.Equal("Esa pregunta ya cambió. Usá la del último documento.", (await ReplyAsync(host, person.ContactId, later)).Body);
        Assert.Equal(asked, (inbox.Asked.Count, inbox.Sends.Count));
    }

    // Review of task 9: a tap after an operator took over the conversation changes nothing: the inbox is not asked and nothing is said.
    [Fact]
    public async Task A_tap_after_an_operator_took_over_changes_nothing()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var inbox = new CapturingStructuredDocumentChannel
        {
            ReadReply = new(null, StructuredDocumentReplyKind.Unknown) { Forms = [new(Guid.NewGuid(), Guid.NewGuid(), "Gastos")] },
        };
        await using var host = await CreateAsync(probe, inbox);
        var person = await host.ContactAsync(Ct, automatic: true);
        var waMessageId = await ReceiveAsync(host, person, probe, "Cargalo");
        var operation = await ReadAndInterpretAsync(host, waMessageId);
        await CompleteAsync(host, person, operation);
        var question = await OutgoingAsync(host, (await ReferenceAsync(host, operation.RequestId)).OutgoingId!.Value);
        await host.InTransactionAsync(async services =>
        {
            var contact = await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().SingleAsync(item => item.Id == person.ContactId, Ct);
            contact.ChangeMode(WhatsAppConversationMode.Manual);
            return true;
        }, Ct);
        var asked = inbox.Asked.Count;

        var tap = await TapAsync(host, person.ContactId, question.QuickReplies[^1].Id, "Otro");
        Assert.True((await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantDocumentService>().ProcessTapAsync(person.ContactId, Ct))).IsSuccess);

        Assert.Equal((asked, 0), (inbox.Asked.Count, inbox.Others.Count));
        Assert.False(await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .AnyAsync(row => row.ContactId == person.ContactId && row.SourceMessageId == tap, Ct)));
        Assert.NotNull((await MessageByIdAsync(host, tap)).AssistantProcessedAtUtc);
    }

    // Review of task 9: a tap on a document question that was only answered is no turn of the conversation, so a text still being
    // interpreted keeps its interpretation instead of being replaced by it.
    [Fact]
    public async Task An_answered_tap_does_not_replace_a_text_still_being_interpreted()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var inbox = new CapturingStructuredDocumentChannel
        {
            ReadReply = new(null, StructuredDocumentReplyKind.Unknown) { Forms = [new(Guid.NewGuid(), Guid.NewGuid(), "Gastos")] },
        };
        await using var host = await CreateAsync(probe, inbox);
        var person = await host.ContactAsync(Ct, automatic: true);
        var waMessageId = await ReceiveAsync(host, person, probe, "Cargalo");
        var operation = await ReadAndInterpretAsync(host, waMessageId);
        await CompleteAsync(host, person, operation);
        var question = await OutgoingAsync(host, (await ReferenceAsync(host, operation.RequestId)).OutgoingId!.Value);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var text = await InboundAsync(host, person.ContactId, WhatsAppMessageKind.Text, "¿Me avisás cuando lo vean?", null);
        await ProcessAsync(host);
        var pending = host.Operations.SubmitRequests[^1];
        Assert.Equal(text, (await ReferenceAsync(host, pending.RequestId)).SourceMessageId);

        var tap = await TapAsync(host, person.ContactId, question.QuickReplies[^1].Id, "Otro");
        await ProcessAsync(host);

        Assert.Equal("Listo, lo dejé para que lo revise el equipo. Si hace falta, te escriben por acá.", (await ReplyAsync(host, person.ContactId, tap)).Body);
        Assert.Equal(WhatsAppAssistantRequestState.Pending, (await ReferenceAsync(host, pending.RequestId)).State);
    }

    // Review of task 9: under each document the chat says why the assistant did what it did: «Por qué lo propuso» (the form, the data read
    // with doubts and, only with formrecords.read, what it read) and «Por qué preguntó» (the forms of the list it sent and the one chosen),
    // and the body of a form sent for a document, which may say what was read, is hidden without formrecords.read. The thread is the
    // `document-why-thread` of the whatsapp-audio-http cohort.
    [Fact]
    public async Task The_chat_says_why_it_proposed_and_why_it_asked_under_each_document()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var inbox = new CapturingStructuredDocumentChannel();
        await using var host = await CreateAsync(probe, inbox);
        var person = await host.ContactAsync(Ct, automatic: true);
        inbox.ReadReply = new(null, StructuredDocumentReplyKind.Sure)
            { Forms = [new(host.Forms.Form.DefinitionId, host.Forms.Form.VersionId, "Registrar visita")], Summary = "Recibí una visita a Sucursal Norte. ¿La cargo?" };
        inbox.Preparation = Prepared(host);
        var proposedId = await ReceiveAsync(host, person, probe, "Cargalo");
        await CompleteAsync(host, person, await ReadAndInterpretAsync(host, proposedId));
        host.Clock.Advance(TimeSpan.FromMinutes(11));
        var forms = new[] { "Pagos de clientes", "Pagos a proveedores", "Gastos de viaje", "Comprobantes de transferencia bancaria" }
            .Select(name => new StructuredDocumentFormChoice(Guid.NewGuid(), Guid.NewGuid(), name)).ToArray();
        inbox.ReadReply = new(null, StructuredDocumentReplyKind.Unknown) { Forms = forms };
        var other = new WhatsAppDocumentProbe { Bytes = Pdf() };
        probe.Bytes = other.Bytes;
        var askedId = await ReceiveAsync(host, person, probe, "Y este");
        var asked = await ReadAndInterpretAsync(host, askedId, readings: 2);
        await CompleteAsync(host, person, asked);
        var list = await OutgoingAsync(host, (await ReferenceAsync(host, asked.RequestId)).OutgoingId!.Value);
        Assert.Equal(WhatsAppOutgoingMessageKind.List, list.Kind);
        await TapAsync(host, person.ContactId, list.QuickReplies[1].Id, list.QuickReplies[1].Title);
        await ProcessAsync(host);

        using var reader = host.Client(host.FirstOperator);
        using var thread = await reader.GetAsync($"/api/whatsapp/chats/{person.ContactId}/messages", Ct);
        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
        var body = await thread.ReadJsonAsync();
        var proposed = await MessageAsync(host, proposedId);
        var askedDocument = await MessageAsync(host, askedId);
        var messages = body.GetProperty("messages").EnumerateArray().ToArray();
        var proposedWhy = messages.Single(message => message.GetProperty("id").GetGuid() == proposed.Id).GetProperty("document").GetProperty("why");
        Assert.Equal(("proposed", "Registrar visita", 1), (proposedWhy.GetProperty("kind").GetString(), proposedWhy.GetProperty("forms")[0].GetString(),
            proposedWhy.GetProperty("doubtful").GetInt32()));
        Assert.Equal(("Lugar", "Sucursal Norte"), (proposedWhy.GetProperty("fields")[0].GetProperty("label").GetString(),
            proposedWhy.GetProperty("fields")[0].GetProperty("displayValue").GetString()));
        var askedWhy = messages.Single(message => message.GetProperty("id").GetGuid() == askedDocument.Id).GetProperty("document").GetProperty("why");
        Assert.Equal(("asked", "Pagos a proveedores"), (askedWhy.GetProperty("kind").GetString(), askedWhy.GetProperty("chosen").GetString()));
        Assert.Equal(forms.Select(form => form.Name), askedWhy.GetProperty("forms").EnumerateArray().Select(name => name.GetString()));
        var outgoing = body.GetProperty("outgoing").EnumerateArray().ToArray();
        var listRow = outgoing.Single(row => row.GetProperty("id").GetGuid() == list.Id);
        Assert.Equal(("QuickReplies", 5, "Elegir formulario"), (listRow.GetProperty("kind").GetString(), listRow.GetProperty("quickReplies").GetArrayLength(),
            listRow.GetProperty("listButton").GetString()));
        var flowId = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .Where(row => row.ContactId == person.ContactId && row.AttachmentMessageId == proposed.Id && row.OutgoingId != null)
            .Select(row => row.OutgoingId!.Value).SingleAsync(Ct));
        var flowRow = outgoing.Single(row => row.GetProperty("id").GetGuid() == flowId);
        Assert.Equal(("Recibí una visita a Sucursal Norte. ¿La cargo?", true, false), (flowRow.GetProperty("body").GetString(), flowRow.GetProperty("loadsFile").GetBoolean(),
            flowRow.TryGetProperty("listButton", out _)));
        await WhatsAppAudioContractCapture.WriteAsync(thread, "document-why-thread", Ct);

        using var restricted = host.Client(host.RestrictedOperator);
        using var hidden = await restricted.GetAsync($"/api/whatsapp/chats/{person.ContactId}/messages", Ct);
        var withoutValues = await hidden.ReadJsonAsync();
        var hiddenWhy = withoutValues.GetProperty("messages").EnumerateArray().Single(message => message.GetProperty("id").GetGuid() == proposed.Id)
            .GetProperty("document").GetProperty("why");
        Assert.Equal((1, false), (hiddenWhy.GetProperty("doubtful").GetInt32(), hiddenWhy.TryGetProperty("fields", out _)));
        Assert.Equal(JsonValueKind.Null, withoutValues.GetProperty("outgoing").EnumerateArray().Single(row => row.GetProperty("id").GetGuid() == flowId)
            .GetProperty("body").ValueKind);
    }

    // Review of task 9: the forms of a document not recognized, with names longer than a row, go out through the outgoing worker to Meta's
    // double as rows of 24 characters with the full name under each, and the chat reads the list back as it went, queued and once sent:
    // its titles and the label of its button.
    [Fact]
    public async Task A_list_of_long_form_names_goes_out_to_meta_and_reads_back_in_the_chat_with_its_button()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        string[] names = ["Comprobantes de transferencia bancaria", "Pagos a proveedores del exterior", "Rendición de viáticos del personal",
            "Facturas de servicios públicos", "Recibos de sueldo firmados por el personal"];
        var forms = names.Select(name => new StructuredDocumentFormChoice(Guid.NewGuid(), Guid.NewGuid(), name)).ToArray();
        var inbox = new CapturingStructuredDocumentChannel { ReadReply = new(null, StructuredDocumentReplyKind.Unknown) { Forms = forms } };
        await using var host = await CreateAsync(probe, inbox);
        var person = await host.ContactAsync(Ct, automatic: true);
        var waMessageId = await ReceiveAsync(host, person, probe, "Cargalo");
        var operation = await ReadAndInterpretAsync(host, waMessageId);
        await CompleteAsync(host, person, operation);
        var list = await OutgoingAsync(host, (await ReferenceAsync(host, operation.RequestId)).OutgoingId!.Value);
        using var reader = host.Client(host.FirstOperator);

        var queued = ThreadOutgoing(await ThreadAsync(reader, person.ContactId), list.Id);
        Assert.Equal(("QuickReplies", "Elegir formulario", 6), (queued.GetProperty("kind").GetString(), queued.GetProperty("listButton").GetString(),
            queued.GetProperty("quickReplies").GetArrayLength()));

        var worker = host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        for (var round = 0; round < 5 && !host.Cloud.Messages.OfType<WhatsAppListMessage>().Any(); round++) await worker.ProcessNextAsync(Ct);
        var sent = Assert.Single(host.Cloud.Messages.OfType<WhatsAppListMessage>());
        Assert.Equal(("No reconocí este documento. ¿Qué querés cargar con él?", "Elegir formulario", 6), (sent.Body, sent.Button, sent.Rows.Count));
        Assert.All(sent.Rows.Take(5), row => Assert.Equal(24, row.Title.Length));
        Assert.Equal(names, sent.Rows.Take(5).Select(row => row.Description));
        Assert.Equal(("Otro", (string?)null), (sent.Rows[^1].Title, sent.Rows[^1].Description));

        var thread = await ThreadAsync(reader, person.ContactId);
        var message = ThreadMessageOfKind(thread, "Interactive");
        Assert.Equal(sent.Rows.Select(row => row.Title), message.GetProperty("quickReplies").EnumerateArray().Select(title => title.GetString()));
        Assert.Equal(("Elegir formulario", false), (message.GetProperty("listButton").GetString(), message.TryGetProperty("loadsFile", out _)));
        Assert.DoesNotContain(thread.GetProperty("outgoing").EnumerateArray(), row => row.GetProperty("id").GetGuid() == list.Id);
    }

    // Review of task 9: a form with a location question opens with its location request, which is what the reference keeps. «Por qué lo
    // propuso» still shows under the document, and the Flow that follows the location, whose body says what was read, carries the mark of
    // a file and hides that body without formrecords.read, queued and once it went out; the location request carries no mark.
    [Fact]
    public async Task A_form_that_opens_with_its_location_request_says_why_it_proposed_and_marks_the_flow_that_follows()
    {
        const string summary = "Recibí una visita a Sucursal Norte. ¿La cargo?";
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var inbox = new CapturingStructuredDocumentChannel();
        await using var host = await CreateAsync(probe, inbox, withLocation: true);
        var person = await host.ContactAsync(Ct, automatic: true);
        inbox.ReadReply = new(null, StructuredDocumentReplyKind.Sure)
            { Forms = [new(host.Forms.Form.DefinitionId, host.Forms.Form.VersionId, "Registrar visita")], Summary = summary };
        inbox.Preparation = Prepared(host);
        var waMessageId = await ReceiveAsync(host, person, probe, "Cargalo");
        var operation = await ReadAndInterpretAsync(host, waMessageId);
        await CompleteAsync(host, person, operation);
        Assert.Equal(WhatsAppOutgoingMessageKind.LocationRequest, (await OutgoingAsync(host, (await ReferenceAsync(host, operation.RequestId)).OutgoingId!.Value)).Kind);
        var worker = host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        await worker.ProcessNextAsync(Ct);
        Assert.IsType<WhatsAppLocationRequestMessage>(Assert.Single(host.Cloud.Messages));
        await LocationAsync(host, person.ContactId);
        Assert.Equal(1, (await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppFlowLocationService>().ProcessAsync(person.ContactId, Ct))).Value);
        var flowId = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking()
            .Where(row => row.ContactId == person.ContactId && row.Kind == WhatsAppOutgoingMessageKind.Flow).Select(row => row.Id).SingleAsync(Ct));
        var document = await MessageAsync(host, waMessageId);
        using var reader = host.Client(host.FirstOperator);
        using var restricted = host.Client(host.RestrictedOperator);

        var queued = await ThreadAsync(reader, person.ContactId);
        var why = ThreadMessage(queued, document.Id).GetProperty("document").GetProperty("why");
        Assert.Equal(("proposed", "Registrar visita", 1), (why.GetProperty("kind").GetString(), why.GetProperty("forms")[0].GetString(), why.GetProperty("doubtful").GetInt32()));
        Assert.Equal((summary, true), (ThreadOutgoing(queued, flowId).GetProperty("body").GetString(), ThreadOutgoing(queued, flowId).GetProperty("loadsFile").GetBoolean()));
        var hiddenQueued = ThreadOutgoing(await ThreadAsync(restricted, person.ContactId), flowId);
        Assert.Equal((JsonValueKind.Null, true), (hiddenQueued.GetProperty("body").ValueKind, hiddenQueued.GetProperty("loadsFile").GetBoolean()));

        await worker.ProcessNextAsync(Ct);
        Assert.IsType<WhatsAppFlowMessageResponse>(host.Cloud.Messages[1]);
        var sent = await ThreadAsync(reader, person.ContactId);
        Assert.Equal((summary, true), (ThreadMessageOfKind(sent, "Flow").GetProperty("body").GetString(), ThreadMessageOfKind(sent, "Flow").GetProperty("loadsFile").GetBoolean()));
        Assert.False(ThreadMessageOfKind(sent, "LocationRequest").TryGetProperty("loadsFile", out _));
        var hiddenSent = ThreadMessageOfKind(await ThreadAsync(restricted, person.ContactId), "Flow");
        Assert.Equal((JsonValueKind.Null, true), (hiddenSent.GetProperty("body").ValueKind, hiddenSent.GetProperty("loadsFile").GetBoolean()));
    }

    // Review of task 9: the option chosen is the first tap on each question, resolved per question, so a person with two hundred answered
    // taps on older questions still sees, under the latest document, the one chosen there.
    [Fact]
    public async Task The_option_chosen_shows_under_the_latest_document_after_two_hundred_answered_taps_on_older_questions()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var forms = new[] { "Pagos de clientes", "Pagos a proveedores", "Gastos de viaje", "Comprobantes de transferencia bancaria" }
            .Select(name => new StructuredDocumentFormChoice(Guid.NewGuid(), Guid.NewGuid(), name)).ToArray();
        var inbox = new CapturingStructuredDocumentChannel { ReadReply = new(null, StructuredDocumentReplyKind.Unknown) { Forms = forms } };
        await using var host = await CreateAsync(probe, inbox);
        var person = await host.ContactAsync(Ct, automatic: true);
        await OlderTapsAsync(host, person, 200);
        var waMessageId = await ReceiveAsync(host, person, probe, "Cargalo");
        var operation = await ReadAndInterpretAsync(host, waMessageId);
        await CompleteAsync(host, person, operation);
        var list = await OutgoingAsync(host, (await ReferenceAsync(host, operation.RequestId)).OutgoingId!.Value);
        await TapAsync(host, person.ContactId, list.QuickReplies[1].Id, list.QuickReplies[1].Title);
        await ProcessAsync(host);
        using var reader = host.Client(host.FirstOperator);

        var why = ThreadMessage(await ThreadAsync(reader, person.ContactId), (await MessageAsync(host, waMessageId)).Id).GetProperty("document").GetProperty("why");

        Assert.Equal(("asked", "Pagos a proveedores"), (why.GetProperty("kind").GetString(), why.GetProperty("chosen").GetString()));
    }

    // Closing task 9: an answered tap is no turn when the processor looks for whom to process either: a text sent just before tapping an
    // older question (with a main agent, its burst still waits when the tap is answered) is found again and interpreted.
    [Fact]
    public async Task A_text_sent_before_an_answered_tap_is_found_again_and_interpreted()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var inbox = new CapturingStructuredDocumentChannel
        {
            ReadReply = new(null, StructuredDocumentReplyKind.Unknown) { Forms = [new(Guid.NewGuid(), Guid.NewGuid(), "Gastos")] },
        };
        await using var host = await CreateAsync(probe, inbox);
        var person = await host.ContactAsync(Ct, automatic: true);
        var waMessageId = await ReceiveAsync(host, person, probe, "Cargalo");
        var operation = await ReadAndInterpretAsync(host, waMessageId);
        await CompleteAsync(host, person, operation);
        var question = await OutgoingAsync(host, (await ReferenceAsync(host, operation.RequestId)).OutgoingId!.Value);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var text = await InboundAsync(host, person.ContactId, WhatsAppMessageKind.Text, "¿Me avisás cuando lo vean?", null);
        var tap = await TapAsync(host, person.ContactId, question.QuickReplies[^1].Id, "Otro");
        // The tap's pass answers it while the text waits.
        Assert.True((await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantDocumentService>().ProcessTapAsync(person.ContactId, Ct))).IsSuccess);
        Assert.NotNull((await MessageByIdAsync(host, tap)).AssistantProcessedAtUtc);

        await ProcessAsync(host);

        var interpreted = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.ContactId == person.ContactId && row.SourceMessageId == text, Ct));
        Assert.NotNull(interpreted);
        Assert.Contains(host.Operations.SubmitRequests, request => request.RequestId == interpreted.RequestId);
    }

    // Closing task 9: a form chosen from the list of a document not recognized continues the operation: the tap is its source with the
    // document its reference carries, the interpretation fixes that form, the inbox recognizes the document for it with the new proposal, and
    // the Flow that follows goes out to Meta's double and is captured with the document.
    [Fact]
    public async Task A_form_chosen_from_the_list_is_interpreted_with_the_document_and_its_flow_is_captured_with_it()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var inbox = new CapturingStructuredDocumentChannel();
        await using var host = await CreateAsync(probe, inbox, capture: true);
        var visit = new StructuredDocumentFormChoice(host.Forms.Form.DefinitionId, host.Forms.Form.VersionId, "Registrar visita");
        inbox.ReadReply = new(null, StructuredDocumentReplyKind.Unknown) { Forms = [visit, new(Guid.NewGuid(), Guid.NewGuid(), "Gastos")] };
        inbox.Preparation = Prepared(host);
        var person = await host.ContactAsync(Ct, automatic: true);
        var waMessageId = await ReceiveAsync(host, person, probe, "Cargalo");
        var operation = await ReadAndInterpretAsync(host, waMessageId);
        await CompleteAsync(host, person, operation);
        var list = await OutgoingAsync(host, (await ReferenceAsync(host, operation.RequestId)).OutgoingId!.Value);
        Assert.Equal(WhatsAppOutgoingMessageKind.List, list.Kind);

        var tap = await TapAsync(host, person.ContactId, list.QuickReplies[0].Id, list.QuickReplies[0].Title);
        await ProcessAsync(host);

        var document = await MessageAsync(host, waMessageId);
        var continuation = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .SingleAsync(row => row.ContactId == person.ContactId && row.SourceMessageId == tap, Ct));
        Assert.Equal(((Guid?)operation.RequestId, (Guid?)document.Id), (continuation.PreviousRequestId, continuation.AttachmentMessageId));
        var focused = host.Operations.SubmitRequests.Last(request => request.RequestId == continuation.RequestId);
        Assert.Equal(("Registrar visita", (Guid?)operation.RequestId, $"whatsapp/documents/{document.Id:N}.pdf", (Guid?)visit.DefinitionId),
            (focused.Text, focused.PreviousRequestId, focused.Attachment!.StorageKey, focused.Context.FocusDefinitionId));

        inbox.ReadReply = new(null, StructuredDocumentReplyKind.Sure) { Forms = [visit] };
        var proposal = await CompleteAsync(host, person, focused);

        var recognized = inbox.Asked[^1];
        Assert.Equal(((Guid?)visit.DefinitionId, proposal.Id), (recognized.FocusDefinitionId, recognized.Proposal!.Id));
        var flow = await ReplyAsync(host, person.ContactId, tap);
        Assert.Equal((WhatsAppOutgoingMessageKind.Flow, "Recibí un documento para «Registrar visita». ¿Lo cargo?"), (flow.Kind, flow.Body));
        var sent = await DispatchFlowAsync(host);
        await WhatsAppAssistantCaptureFixture.ReplyAsync(host, person, sent, host.Forms.FieldId, Ct);
        var capture = host.Host.Services.GetRequiredService<CapturingStructuredRecordPort>().Captures[^1];
        Assert.Equal((inbox.DocumentOf("whatsapp", document.Id.ToString("N")), StructuredRecordSenderKind.Assistant), (capture.Origin!.Document, capture.Origin.SenderKind));
    }

    // Closing task 9: only a tap whose choice was applied is the option chosen: a tap on a question that was no longer the latest is told it
    // changed, and its document shows no option chosen.
    [Fact]
    public async Task A_tap_told_the_question_changed_is_not_the_option_chosen()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var inbox = new CapturingStructuredDocumentChannel
        {
            ReadReply = new(null, StructuredDocumentReplyKind.Unknown) { Forms = [new(Guid.NewGuid(), Guid.NewGuid(), "Gastos")] },
        };
        await using var host = await CreateAsync(probe, inbox);
        var person = await host.ContactAsync(Ct, automatic: true);
        var firstId = await ReceiveAsync(host, person, probe, "Cargalo");
        var first = await ReadAndInterpretAsync(host, firstId);
        await CompleteAsync(host, person, first);
        var older = await OutgoingAsync(host, (await ReferenceAsync(host, first.RequestId)).OutgoingId!.Value);
        host.Clock.Advance(TimeSpan.FromMinutes(11));
        probe.Bytes = Pdf();
        var secondId = await ReceiveAsync(host, person, probe, "Y este");
        await CompleteAsync(host, person, await ReadAndInterpretAsync(host, secondId, readings: 2));

        var tap = await TapAsync(host, person.ContactId, older.QuickReplies[0].Id, older.QuickReplies[0].Title);
        await ProcessAsync(host);

        Assert.Equal("Esa pregunta ya cambió. Usá la del último documento.", (await ReplyAsync(host, person.ContactId, tap)).Body);
        using var reader = host.Client(host.FirstOperator);
        var why = ThreadMessage(await ThreadAsync(reader, person.ContactId), (await MessageAsync(host, firstId)).Id).GetProperty("document").GetProperty("why");
        Assert.Equal("asked", why.GetProperty("kind").GetString());
        Assert.False(why.TryGetProperty("chosen", out var chosen) && chosen.ValueKind != JsonValueKind.Null);
    }

    // Closing task 9: a tap the inbox could not take now says so and closes nothing: the next tap on the same question is taken as a choice,
    // not told the question changed.
    [Fact]
    public async Task A_tap_the_inbox_could_not_take_leaves_the_question_open_for_the_next_one()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var unknown = new StructuredDocumentRegistration(null, StructuredDocumentReplyKind.Unknown) { Forms = [new(Guid.NewGuid(), Guid.NewGuid(), "Gastos")] };
        var inbox = new CapturingStructuredDocumentChannel { ReadReply = unknown };
        await using var host = await CreateAsync(probe, inbox);
        var person = await host.ContactAsync(Ct, automatic: true);
        var waMessageId = await ReceiveAsync(host, person, probe, "Cargalo");
        var operation = await ReadAndInterpretAsync(host, waMessageId);
        await CompleteAsync(host, person, operation);
        var list = await OutgoingAsync(host, (await ReferenceAsync(host, operation.RequestId)).OutgoingId!.Value);
        // Without a reply to read, the capturing inbox answers «Otro» as unavailable.
        inbox.ReadReply = null;
        var failed = await TapAsync(host, person.ContactId, list.QuickReplies[^1].Id, "Otro");
        await ProcessAsync(host);
        Assert.Equal("No pude preparar el formulario. Lo dejé para que lo revise el equipo.", (await ReplyAsync(host, person.ContactId, failed)).Body);

        inbox.ReadReply = unknown;
        var again = await TapAsync(host, person.ContactId, list.QuickReplies[^1].Id, "Otro");
        await ProcessAsync(host);

        Assert.Equal("Listo, lo dejé para que lo revise el equipo. Si hace falta, te escriben por acá.", (await ReplyAsync(host, person.ContactId, again)).Body);
    }

    // The form the inbox prepares for the scenario's published form: the place read, with doubts.
    private static StructuredDocumentSendPreparation Prepared(WhatsAppAssistantScenario host) => new(StructuredDocumentSendState.Ready)
    {
        Prefill = new(host.Forms.Form, 1, [new(host.Forms.FieldId, "field_" + host.Forms.FieldId.ToString("N"), "shortText", "Lugar", "Sucursal Norte", "Sucursal Norte")],
            [], "Leyó el documento.", "es"),
        Marks = [new(host.Forms.FieldId, StructuredDocumentMarkKind.Doubtful)],
    };

    // The person declined that form as an offer before (a durable decline of the automatic assistant).
    private static Task<bool> DeclineAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact person, Guid definitionId) => host.InTransactionAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == person.ContactId, Ct);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var source = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Text, "No, gracias.", null, now.AddMinutes(-30));
        source.Classify(WhatsAppMessagePurpose.Business); source.SetInboundSequence(contact.RegisterInboundMessage()); source.MarkProcessed(now);
        var declined = WhatsAppAssistantRequest.Create(Guid.NewGuid(), contact.Id, source.Id, person.RecipientId, person.RecipientId, WhatsAppAssistantRequestKind.Automatic,
            contact.ModeRevision, contact.LinkRevision, "{\"channelId\":\"fixture\"}", now.AddMinutes(-30), now.AddDays(30));
        Assert.True(declined.RecordDecline(definitionId, now.AddMinutes(-29)));
        db.AddRange(source, declined);
        return true;
    }, Ct);

    // The outgoing worker runs until the Flow goes out to Meta's double.
    private static async Task<WhatsAppFlowMessageResponse> DispatchFlowAsync(WhatsAppAssistantScenario host)
    {
        var worker = host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        for (var round = 0; round < 5 && !host.Cloud.Messages.OfType<WhatsAppFlowMessageResponse>().Any(); round++) await worker.ProcessNextAsync(Ct);
        return Assert.Single(host.Cloud.Messages.OfType<WhatsAppFlowMessageResponse>());
    }

    // The document is downloaded and read, its reading completes and the interpretation of its request is submitted.
    private static async Task<AssistantOperationRequest> ReadAndInterpretAsync(WhatsAppAssistantScenario host, string waMessageId, int readings = 1)
    {
        host.Operations.AllowSubmit = true;
        await ProcessAsync(host);
        var reading = host.Operations.SubmitRequests.Where(request => request.Document is not null).ElementAt(readings - 1);
        Assert.Equal(readings, host.Operations.SubmitRequests.Count(request => request.Document is not null));
        Assert.NotNull(reading.Document);
        host.Operations.Complete(new AssistantOperationResponse(reading.RequestId, "completed", null, null, null) { DocumentReading = Reading });
        await ProcessAsync(host);
        Assert.Equal(WhatsAppDocumentState.Read, (await MessageAsync(host, waMessageId)).DocumentState);
        return host.Operations.SubmitRequests.Last(request => request.Document is null);
    }

    private static async Task<AssistantObservedProposal> CompleteAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact person, AssistantOperationRequest operation)
    {
        var proposal = new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), WhatsAppAssistantDocumentPolicy.ProposeEntry, new EntryPayload());
        var outcome = new AssistantOperationResponse(operation.RequestId, "completed", proposal,
            new("http-fixture", 1, "http-fixture", 1, operation.OccurredAtUtc, operation.Context.ChannelId), null);
        host.Operations.Complete(outcome);
        await host.CompleteOutcomeAsync(person.ContactId, outcome, Ct);
        return proposal;
    }

    private sealed record EntryPayload : AssistantCapabilityPayload;

    private Task<WhatsAppAssistantScenario> CreateAsync(WhatsAppDocumentProbe probe, CapturingStructuredDocumentChannel inbox, bool capture = false,
        bool withLocation = false) =>
        WhatsAppAssistantScenario.CreateAsync(factory, Ct, builder => builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IPrivateFileStore>(probe));
            services.Replace(ServiceDescriptor.Singleton<IWhatsAppMediaClient>(probe));
            services.Replace(ServiceDescriptor.Singleton<IStructuredDocumentChannel>(inbox));
            if (capture) WhatsAppAssistantCaptureFixture.Configure(services);
        }), withLocation: withLocation);

    private static async Task<JsonElement> ThreadAsync(HttpClient client, Guid contactId)
    {
        using var response = await client.GetAsync($"/api/whatsapp/chats/{contactId}/messages", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static JsonElement ThreadMessage(JsonElement thread, Guid id) =>
        thread.GetProperty("messages").EnumerateArray().Single(message => message.GetProperty("id").GetGuid() == id);

    private static JsonElement ThreadOutgoing(JsonElement thread, Guid id) =>
        thread.GetProperty("outgoing").EnumerateArray().Single(row => row.GetProperty("id").GetGuid() == id);

    private static JsonElement ThreadMessageOfKind(JsonElement thread, string kind) =>
        thread.GetProperty("messages").EnumerateArray().Single(message => message.GetProperty("kind").GetString() == kind);

    // Answered taps on older document questions: each one is a tap of the person with its own reference, closed long ago.
    private static Task<bool> OlderTapsAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact person, int count) => host.InTransactionAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == person.ContactId, Ct);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        for (var index = 0; index < count; index++)
        {
            var at = now.AddMinutes(-60).AddSeconds(index);
            var tap = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.ButtonReply, "Gastos",
                WhatsAppAssistantDocumentReplyCodec.Encode(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), at);
            tap.Classify(WhatsAppMessagePurpose.Business); tap.SetInboundSequence(contact.RegisterInboundMessage()); tap.MarkProcessed(at); tap.MarkAssistantProcessed(at);
            var answered = WhatsAppAssistantRequest.Create(Guid.NewGuid(), contact.Id, tap.Id, person.RecipientId, person.RecipientId, WhatsAppAssistantRequestKind.Automatic,
                contact.ModeRevision, contact.LinkRevision, "{\"channelId\":\"fixture\"}", at, now.AddDays(30));
            Assert.True(answered.FinishWithoutReply(false, at));
            db.AddRange(tap, answered);
        }
        return true;
    }, Ct);

    // The person sends a location some time after the request went out.
    private static Task<Guid> LocationAsync(WhatsAppAssistantScenario host, Guid contactId) => host.InTransactionAsync(async services =>
    {
        host.Clock.Advance(TimeSpan.FromSeconds(30));
        var db = services.GetRequiredService<ApplicationDbContext>();
        var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == contactId, Ct);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var message = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Location, null, null, now);
        message.RecordLocation(-34.603722m, -58.381592m);
        message.Classify(WhatsAppMessagePurpose.Business); message.SetInboundSequence(contact.RegisterInboundMessage()); message.MarkProcessed(now);
        contact.RecordInbound(contact.WaId, contact.UserIdentifier, contact.ProfileName, now); db.Add(message);
        return message.Id;
    }, Ct);

    /// <summary>A one-page PDF with a title of its own, so no two documents repeat each other.</summary>
    private static byte[] Pdf()
    {
        using var output = new MemoryStream();
        using (var pdf = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata { Title = Guid.NewGuid().ToString("N") }))
        {
            using var paint = new SKPaint { Color = SKColors.Black };
            pdf.BeginPage(200, 200).DrawRect(10, 10, 40, 20, paint);
            pdf.EndPage();
            pdf.Close();
        }
        return output.ToArray();
    }

    private static async Task<string> ReceiveAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact person, WhatsAppDocumentProbe probe, string? caption)
    {
        var contact = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().AsNoTracking()
            .SingleAsync(row => row.Id == person.ContactId, Ct));
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var body = MetaWebhook.Build(contacts: [MetaWebhook.Contact(contact.WaId, contact.UserIdentifier, contact.ProfileName)],
            messages: [MetaWebhook.Document(waMessageId, contact.WaId, contact.UserIdentifier, host.Clock.GetUtcNow().UtcDateTime, "comprobante.pdf",
                "application/pdf", caption, sha256: Convert.ToBase64String(SHA256.HashData(probe.Bytes)))]);
        using var client = host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return waMessageId;
    }

    private static Task<Guid> TapAsync(WhatsAppAssistantScenario host, Guid contactId, string replyId, string title) =>
        InboundAsync(host, contactId, WhatsAppMessageKind.ButtonReply, title, replyId);

    private static Task<Guid> InboundAsync(WhatsAppAssistantScenario host, Guid contactId, WhatsAppMessageKind kind, string body, string? replyId) => host.InTransactionAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == contactId, Ct);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var message = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), kind, body, replyId, now);
        message.Classify(WhatsAppMessagePurpose.Business); message.SetInboundSequence(contact.RegisterInboundMessage()); message.MarkProcessed(now);
        contact.RecordInbound(contact.WaId, contact.UserIdentifier, contact.ProfileName, now); db.Add(message);
        return message.Id;
    }, Ct);

    private static Task ProcessAsync(WhatsAppAssistantScenario host) =>
        host.Host.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);

    private static Task<WhatsAppMessage> MessageAsync(WhatsAppAssistantScenario host, string waMessageId) =>
        host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
            .SingleAsync(row => row.WaMessageId == waMessageId, Ct));

    private static Task<WhatsAppMessage> MessageByIdAsync(WhatsAppAssistantScenario host, Guid id) =>
        host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking().SingleAsync(row => row.Id == id, Ct));

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
}
