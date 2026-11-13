using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// Task 9 of the documents (decision 11): a read document goes to an interpretation with the request of its source (or alone, only to
// load it), formularios registers it in the inbox and says what to answer, and WhatsApp says it with fixed texts in the person's language:
// the prefilled form with the document the server adds, «¿Qué es este documento?», the list of what the person can complete, what cannot be
// read, when it was loaded, the handoff of a form that cannot be sent and the day's quota. A tap answers without another interpretation, or
// continues the operation with the form chosen.
public sealed class WhatsAppAssistantDocumentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly StructuredDocumentReading Reading = new(StructuredDocumentReadability.Readable, null, "es", "Transferencia $ 150.000", false);

    private sealed record Arranged(WhatsAppAssistantSuggestionFixture Host, Documents Documents, WhatsAppAssistantDispatchService Dispatch,
        WhatsAppAssistantDocumentService Taps, WhatsAppMessage Document, WhatsAppAssistantDocumentRow Row);

    private static async Task<Arranged> ArrangeAsync(string caption = "Cargalo en pagos", string culture = "es")
    {
        var host = await WhatsAppAssistantSuggestionFixture.ReadyAsync(culture);
        host.Requests.Rows.Clear();
        host.Reader.Reference = null;
        host.Conversation.Outgoing.Clear();
        host.Context.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var documents = new Documents(host.UnitOfWork);
        var owner = new WhatsAppAssistantTerminalFixture();
        var sending = WhatsAppAssistantAcceptanceFixture.CreateSending(host);
        var policy = WhatsAppAssistantAcceptanceFixture.CreateDocuments(host, documents, sending);
        var automatic = new WhatsAppAssistantAutomaticPolicy(WhatsAppAssistantAcceptanceFixture.Create(host, sending), host.Reader, host.Context.Clock, host.Conversation,
            host.Messages, new(owner, owner, owner, host.Reader, policy, WhatsAppAssistantAcceptanceFixture.CreateSpreadsheets(host, documents, sending),
                WhatsAppAssistantAcceptanceFixture.CreateLoads(host, sending)), new(host.Reader, host.Context.Clock), WhatsAppAssistantAcceptanceFixture.CreateReply(host));
        var dispatch = new WhatsAppAssistantDispatchService(host.Context.Policy, new(host.Reader, host.Assistant, host.Forms, host.Context.Clock, host.Presentation),
            host.Reader, host.Requests, host.Conversation, host.UnitOfWork, automatic, NullLogger<WhatsAppAssistantDispatchService>.Instance);
        var taps = new WhatsAppAssistantDocumentService(host.Reader, host.Requests, host.Conversation, host.UnitOfWork, automatic, policy, host.Context.Clock,
            NullLogger<WhatsAppAssistantDocumentService>.Instance);
        var now = host.Context.Clock.GetUtcNow().UtcDateTime;
        var message = WhatsAppMessage.Inbound(host.Context.Contact.Id, "wamid." + Guid.NewGuid().ToString("N"), WhatsAppMessageKind.ReceivedDocument, caption, null, now);
        host.Messages.Messages.Add(message);
        var row = new WhatsAppAssistantDocumentRow(message.Id, $"whatsapp/documents/{message.Id:N}.pdf", "comprobante.pdf", 2048, new string('d', 64), 2,
            now.AddDays(90), WhatsAppReceivedDocumentReadingPolicy.SerializeReading(Reading)) { ReceivedAtUtc = now };
        host.Reader.Documents[message.Id] = row;
        host.Reader.Source = new WhatsAppAssistantSourceRow(message.Id, host.Context.Contact.Id, caption, now, now.AddDays(90))
            { WaMessageId = message.WaMessageId, Kind = WhatsAppMessageKind.ReceivedDocument, Document = row,
                State = caption.Length == 0 ? WhatsAppAssistantSourceState.Alone : WhatsAppAssistantSourceState.Ready };
        return new(host, documents, dispatch, taps, message, row);
    }

    // The interpretation's work, with the operation the engine closed on (a documents' proposal unless the test gives another).
    private static async Task<(Guid RequestId, AssistantObservedProposal Proposal)> InterpretAsync(Arranged a, AssistantObservedProposal? proposal = null)
    {
        var work = (await a.Dispatch.PrepareAsync(a.Host.Context.Contact.Id, Ct)).Value!;
        var observed = proposal ?? new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), WhatsAppAssistantDocumentPolicy.ProposeEntry, new EntryPayload());
        var outcome = new AssistantOperationResponse(work.RequestId, "completed", observed,
            new("qualified-text", 1, "capabilities-v1", 1, a.Host.Context.Clock.GetUtcNow().UtcDateTime, "whatsapp-assistant"), null);
        a.Host.Assistant.Outcome = outcome;
        Assert.True((await a.Dispatch.CompleteAsync(a.Host.Context.Contact.Id, work.RequestId, outcome, Ct)).IsSuccess);
        return (work.RequestId, observed);
    }

    private static StructuredDocumentFormChoice Form(string name) => new(Guid.NewGuid(), Guid.NewGuid(), name);

    [Fact]
    public async Task A_document_with_its_request_goes_with_the_interpretation_that_also_offers_to_build_and_the_reference_carries_it()
    {
        var a = await ArrangeAsync();

        var work = (await a.Dispatch.PrepareAsync(a.Host.Context.Contact.Id, Ct)).Value!;

        var operation = work.Operation!;
        Assert.Equal("Cargalo en pagos", operation.Text);
        Assert.Equal((a.Row.StorageKey, "comprobante.pdf", "application/pdf", AssistantAttachmentKind.Document),
            (operation.Attachment!.StorageKey, operation.Attachment.Name, operation.Attachment.ContentType, operation.Attachment.Kind));
        Assert.Equal((StructuredDocumentKind.Pdf, (int?)2, Reading), (operation.Attachment.Document!.Kind, operation.Attachment.Document.Pages, operation.Attachment.Document.Reading));
        Assert.Contains(new AssistantCapabilityReference("forms.authoring.propose-form", 2), operation.Context.AllowedCapabilities);
        Assert.Equal(WhatsAppAssistantAccessPolicy.DocumentCapabilities, operation.Context.AllowedCapabilities.TakeLast(2));
        Assert.Equal((a.Document.Id, (Guid?)a.Document.Id), (Assert.Single(a.Host.Requests.Rows).SourceMessageId, a.Host.Requests.Rows[0].AttachmentMessageId));
    }

    [Fact]
    public async Task A_document_alone_goes_to_an_interpretation_that_only_loads_it_named_by_the_server()
    {
        var a = await ArrangeAsync(caption: "");

        var work = (await a.Dispatch.PrepareAsync(a.Host.Context.Contact.Id, Ct)).Value!;

        Assert.Equal("(Mandó el documento «comprobante.pdf» sin mensaje)", work.Operation!.Text);
        Assert.Equal(WhatsAppAssistantAccessPolicy.DocumentCapabilities, work.Operation.Context.AllowedCapabilities);
        Assert.Null(work.Operation.Context.PreloadedCapabilities);
        Assert.Equal(a.Row.StorageKey, work.Operation.Attachment!.StorageKey);
    }

    [Fact]
    public async Task The_proposal_goes_to_the_inbox_with_the_copy_the_reading_and_the_deadline_of_the_send()
    {
        var a = await ArrangeAsync();
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Quota);

        var (_, proposal) = await InterpretAsync(a);

        var request = Assert.Single(a.Documents.Registrations);
        var now = a.Host.Context.Clock.GetUtcNow().UtcDateTime;
        Assert.Equal(("whatsapp", a.Document.Id.ToString("N"), a.Host.Context.Subject.Id, "es", now, now.AddHours(24)),
            (request.ChannelId, request.SourceId, request.SenderUserId, request.Culture, request.ReceivedAtUtc, request.ReplyExpiresAtUtc));
        Assert.Equal(new StructuredDocumentCopy(a.Row.StorageKey, "comprobante.pdf", "application/pdf", 2048, new string('d', 64), 2), request.Copy);
        Assert.Equal((Reading, proposal.Id, (Guid?)null), (request.Reading, request.Proposal!.Id, request.FocusDefinitionId));
        Assert.Equal("Por hoy llegué al tope de documentos que puedo leer. Lo dejé para que lo revise el equipo.", Assert.Single(a.Host.Conversation.Outgoing).Body);
    }

    [Theory]
    [InlineData("es", "¿Qué es este documento?", "Otro")]
    [InlineData("en", "What is this document?", "Other")]
    public async Task A_doubt_asks_with_two_buttons_and_another_and_the_ids_name_the_request_proposal_and_form(string culture, string body, string other)
    {
        var a = await ArrangeAsync(culture: culture);
        var clients = Form("Pagos de clientes");
        var providers = Form("Pagos a proveedores");
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Doubt) { Forms = [clients, providers] };

        var (requestId, proposal) = await InterpretAsync(a);

        var question = Assert.Single(a.Host.Conversation.Outgoing);
        Assert.Equal((WhatsAppOutgoingMessageKind.QuickReplies, body), (question.Kind, question.Body));
        Assert.Equal(["Pagos de clientes", "Pagos a proveedores", other], question.QuickReplies.Select(reply => reply.Title));
        Assert.Equal([WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, clients.DefinitionId),
            WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, providers.DefinitionId), WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, null)],
            question.QuickReplies.Select(reply => reply.Id));
        // The question is the reference's own outgoing, and the reference carries the document: it is never an offer.
        Assert.Equal((question.Id, (Guid?)a.Document.Id, WhatsAppAssistantRequestState.Sent),
            (a.Host.Requests.Rows[0].OutgoingId!.Value, a.Host.Requests.Rows[0].AttachmentMessageId, a.Host.Requests.Rows[0].State));
    }

    [Fact]
    public async Task Three_candidates_go_as_three_buttons_and_a_name_that_does_not_fit_a_button_as_a_list()
    {
        var a = await ArrangeAsync();
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Doubt) { Forms = [Form("Pagos de clientes"), Form("Cobranzas"), Form("Gastos")] };
        await InterpretAsync(a);
        Assert.Equal(["Pagos de clientes", "Cobranzas", "Gastos"], Assert.Single(a.Host.Conversation.Outgoing).QuickReplies.Select(reply => reply.Title));

        var b = await ArrangeAsync();
        b.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Doubt) { Forms = [Form("Pagos de clientes mayoristas"), Form("Cobranzas")] };
        await InterpretAsync(b);
        var list = Assert.Single(b.Host.Conversation.Outgoing);
        Assert.Equal((WhatsAppOutgoingMessageKind.List, "Elegir formulario"), (list.Kind, list.ListButton));
        Assert.Equal(["Pagos de clientes mayor…", "Cobranzas", "Otro"], list.QuickReplies.Select(reply => reply.Title));
    }

    [Theory]
    [InlineData("es", "No reconocí este documento. ¿Qué querés cargar con él?", "Elegir formulario", "Otro")]
    [InlineData("en", "I didn't recognize this document. What do you want to load with it?", "Choose form", "Other")]
    public async Task A_document_not_recognized_offers_the_list_of_what_the_person_can_complete_and_another(string culture, string body, string button, string other)
    {
        var a = await ArrangeAsync(culture: culture);
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Unknown) { Forms = Enumerable.Range(1, 9).Select(index => Form("Formulario " + index)).ToArray() };

        await InterpretAsync(a);

        var list = Assert.Single(a.Host.Conversation.Outgoing);
        Assert.Equal((WhatsAppOutgoingMessageKind.List, body, button, 10), (list.Kind, list.Body, list.ListButton, list.QuickReplies.Count));
        Assert.Equal(other, list.QuickReplies[^1].Title);
    }

    [Theory]
    [InlineData("es", "Monto", "No se lee «Monto». ¿Me lo reenviás? Sacale otra foto con buena luz, sin mover el celular.")]
    [InlineData("en", "Amount", "“Amount” can't be read. Can you send it again? Take another photo with good light, without moving the phone.")]
    [InlineData("es", null, "No se lee el documento. ¿Me lo reenviás? Sacale otra foto con buena luz, sin mover el celular.")]
    public async Task A_document_that_cannot_be_read_asks_for_it_again_naming_the_datum(string culture, string? datum, string text)
    {
        var a = await ArrangeAsync(culture: culture);
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Unreadable) { MissingDatum = datum, Problem = StructuredDocumentProblem.Blurry };
        await InterpretAsync(a);
        Assert.Equal(text, Assert.Single(a.Host.Conversation.Outgoing).Body);
    }

    // Dates only with numbers, in the conversation's zone.
    [Theory]
    [InlineData("es", "REC-000123", "Este comprobante ya se cargó el 20/10/2026 en REC-000123. No lo cargo de nuevo.")]
    [InlineData("en", "REC-000123", "This receipt was already loaded on 10/20/2026 in REC-000123. I won't load it again.")]
    [InlineData("es", null, "Este comprobante ya se cargó el 20/10/2026. No lo cargo de nuevo.")]
    public async Task A_repeated_document_says_when_and_where_it_was_loaded(string culture, string? number, string text)
    {
        var a = await ArrangeAsync(culture: culture);
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Duplicate)
            { Duplicate = new(new DateTime(2026, 10, 21, 1, 30, 0, DateTimeKind.Utc), number) };
        await InterpretAsync(a);
        Assert.Equal(text, Assert.Single(a.Host.Conversation.Outgoing).Body);
    }

    // CE4-A: it is said and the conversation goes to a person, marked with the reason; the inbox keeps it proposed for the team.
    [Fact]
    public async Task A_form_that_cannot_be_sent_is_said_and_hands_the_conversation_to_a_person()
    {
        var a = await ArrangeAsync();
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.NotSendable) { Forms = [Form("Pagos a proveedores")] };

        await InterpretAsync(a);

        var contact = a.Host.Context.Contact;
        Assert.Equal((WhatsAppConversationMode.Manual, true, WhatsAppAssistantDocumentPolicy.NotSendableCode),
            (contact.Mode, contact.NeedsPerson, contact.HandoffReasonCode));
        Assert.Equal("Mandó un documento de «Pagos a proveedores» y no se le puede mandar ese formulario.", contact.HandoffReason);
        var handoff = Assert.Single(a.Host.Conversation.Outgoing);
        Assert.True(handoff.IsAssistantHandoff);
        Assert.Equal("Gracias, recibí tu documento para «Pagos a proveedores». No lo puedo cargar desde acá: se lo pasé a una persona del equipo para que lo revise.",
            handoff.Body);
    }

    [Theory]
    [InlineData(StructuredDocumentReplyKind.Rejected)]
    [InlineData(StructuredDocumentReplyKind.Unavailable)]
    public async Task What_was_answered_when_it_arrived_or_could_not_be_registered_is_not_said(StructuredDocumentReplyKind kind)
    {
        var a = await ArrangeAsync();
        a.Documents.Registration = new(kind == StructuredDocumentReplyKind.Unavailable ? null : Guid.NewGuid(), kind);
        await InterpretAsync(a);
        Assert.Empty(a.Host.Conversation.Outgoing);
        Assert.Equal(WhatsAppAssistantRequestState.NoReply, Assert.Single(a.Host.Requests.Rows).State);
    }

    // DOC3: sure, the standard's summary with «Abrir formulario»; the session seals the inbox's document as the origin, the copy the server adds
    // on capture to its question and the marks, and expires when the inbox stops waiting.
    [Fact]
    public async Task A_sure_document_sends_the_prefilled_form_with_the_document_the_server_adds()
    {
        var a = await ArrangeAsync();
        var prefill = a.Host.Forms.Prefill.Value;
        var documentId = Guid.NewGuid();
        var documentField = Guid.NewGuid();
        var choice = new StructuredDocumentFormChoice(prefill.Form.DefinitionId, prefill.Form.VersionId, "Visita");
        a.Documents.Registration = new(documentId, StructuredDocumentReplyKind.Sure)
            { Forms = [choice], Summary = "Recibí un comprobante de transferencia de $150.000 del Galicia. ¿Lo cargo?" };
        a.Documents.Preparation = new(StructuredDocumentSendState.Ready)
        {
            Prefill = prefill, DocumentFieldId = documentField,
            Marks = [new(prefill.Fields[0].FieldId, StructuredDocumentMarkKind.Doubtful)],
        };

        var (requestId, proposal) = await InterpretAsync(a);

        var send = Assert.Single(a.Documents.Sends);
        Assert.Equal((documentId, "whatsapp", a.Document.Id.ToString("N"), prefill.Form.DefinitionId), (send.DocumentId, send.ChannelId, send.SourceId, send.DefinitionId));
        var flow = Assert.Single(a.Host.Conversation.Outgoing);
        Assert.Equal((WhatsAppOutgoingMessageKind.Flow, "Recibí un comprobante de transferencia de $150.000 del Galicia. ¿Lo cargo?"), (flow.Kind, flow.Body));
        var session = Assert.Single(a.Host.Sessions.Rows);
        Assert.Equal(a.Documents.Registrations[0].ReplyExpiresAtUtc, session.ExpiresAtUtc);
        var content = a.Host.Protector.Content!;
        Assert.Equal((WhatsAppAssistantDocumentPolicy.ProposeEntry.Id, proposal.Id, (Guid?)documentId, "Leyó el documento «comprobante.pdf» que mandó la persona."),
            (content.Origin.Proposal!.CapabilityId, content.Origin.Proposal.ProposalId, content.Origin.Document, content.Origin.Reason));
        Assert.Equal(prefill.Fields.Select(field => field.FieldId), content.Origin.IncludedFieldIds);
        Assert.Equal(new WhatsAppFlowServerFile(documentField, a.Row.StorageKey, "comprobante.pdf", "application/pdf", 2048, new string('d', 64)), content.Server!.File);
        Assert.Equal(StructuredDocumentMarkKind.Doubtful, Assert.Single(content.Server.Marks!).Kind);
        var reference = Assert.Single(a.Host.Requests.Rows);
        Assert.Equal((requestId, flow.Id, (Guid?)proposal.Id), (reference.RequestId, reference.OutgoingId!.Value, reference.ProposalId));
    }

    [Fact]
    public async Task A_sure_document_without_a_summary_names_the_form()
    {
        var a = await ArrangeAsync();
        var prefill = a.Host.Forms.Prefill.Value;
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Sure) { Forms = [new(prefill.Form.DefinitionId, prefill.Form.VersionId, "Visita")] };
        a.Documents.Preparation = new(StructuredDocumentSendState.Ready) { Prefill = prefill };
        await InterpretAsync(a);
        Assert.Equal("Recibí un documento para «Visita». ¿Lo cargo?", Assert.Single(a.Host.Conversation.Outgoing).Body);
        Assert.Null(a.Host.Protector.Content!.Server!.File);
    }

    private static WhatsAppAssistantChoiceRow Tap(Arranged a, string replyId, string title)
    {
        var message = WhatsAppMessage.Inbound(a.Host.Context.Contact.Id, "wamid." + Guid.NewGuid().ToString("N"), WhatsAppMessageKind.ButtonReply, title, replyId,
            a.Host.Context.Clock.GetUtcNow().UtcDateTime);
        a.Host.Messages.Messages.Add(message);
        return new(message.Id, message.WaMessageId, replyId, message.OccurredAtUtc);
    }

    // The question went out (its reference accepted it, Sent) and is the latest document question.
    private static void Sent(Arranged a, Guid requestId)
    {
        var row = a.Host.Requests.Rows.Single(item => item.RequestId == requestId);
        Assert.Equal(WhatsAppAssistantRequestState.Sent, row.State);
        a.Host.Reader.References[requestId] = WhatsAppAssistantSuggestionPolicy.Reference(row);
        a.Host.Reader.LatestDocumentRequest = requestId;
    }

    [Fact]
    public async Task Choosing_a_candidate_sends_its_form_without_another_interpretation()
    {
        var a = await ArrangeAsync();
        var prefill = a.Host.Forms.Prefill.Value;
        var chosen = new StructuredDocumentFormChoice(prefill.Form.DefinitionId, prefill.Form.VersionId, "Visita");
        var documentId = Guid.NewGuid();
        a.Documents.Registration = new(documentId, StructuredDocumentReplyKind.Doubt) { Forms = [chosen, Form("Gastos")] };
        a.Documents.Preparation = new(StructuredDocumentSendState.Ready) { Prefill = prefill };
        var (requestId, proposal) = await InterpretAsync(a);
        Sent(a, requestId);
        a.Host.Assistant.ResetCalls();
        a.Host.Reader.DocumentTap = Tap(a, WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, chosen.DefinitionId), "Visita");

        Assert.True((await a.Taps.ProcessTapAsync(a.Host.Context.Contact.Id, Ct)).IsSuccess);

        Assert.Equal(0, a.Host.Assistant.Submits);
        var flow = a.Host.Conversation.Outgoing[^1];
        Assert.Equal((WhatsAppOutgoingMessageKind.Flow, "Listo: te preparé el formulario con el documento. Revisalo y confirmalo."), (flow.Kind, flow.Body));
        Assert.Equal((documentId, chosen.DefinitionId), (a.Documents.Sends[^1].DocumentId, a.Documents.Sends[^1].DefinitionId));
        Assert.Equal((proposal.Id, (Guid?)documentId), (a.Host.Protector.Content!.Origin.Proposal!.ProposalId, a.Host.Protector.Content.Origin.Document));
        var answer = a.Host.Requests.Rows[^1];
        Assert.Equal((a.Host.Reader.DocumentTap.Id, (Guid?)a.Document.Id, flow.Id), (answer.SourceMessageId, answer.AttachmentMessageId, answer.OutgoingId!.Value));
        Assert.NotNull(a.Host.Messages.Messages.Single(message => message.Id == a.Host.Reader.DocumentTap.Id).AssistantProcessedAtUtc);
    }

    [Theory]
    [InlineData("es", "Listo, lo dejé para que lo revise el equipo. Si hace falta, te escriben por acá.")]
    [InlineData("en", "Done, I left it for the team to review. If needed, they'll write to you here.")]
    public async Task Another_leaves_it_for_the_team(string culture, string text)
    {
        var a = await ArrangeAsync(culture: culture);
        var documentId = Guid.NewGuid();
        a.Documents.Registration = new(documentId, StructuredDocumentReplyKind.Unknown) { Forms = [Form("Gastos")] };
        var (requestId, proposal) = await InterpretAsync(a);
        Sent(a, requestId);
        a.Host.Reader.DocumentTap = Tap(a, WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, null), "Otro");

        Assert.True((await a.Taps.ProcessTapAsync(a.Host.Context.Contact.Id, Ct)).IsSuccess);

        var other = Assert.Single(a.Documents.Others);
        Assert.Equal((documentId, "whatsapp", a.Document.Id.ToString("N"), a.Host.Context.Subject.Id, culture),
            (other.DocumentId, other.ChannelId, other.SourceId, other.SenderUserId, other.Culture));
        Assert.Equal(text, a.Host.Conversation.Outgoing[^1].Body);
    }

    // Only the latest question's options count.
    [Fact]
    public async Task An_option_of_an_earlier_question_says_the_question_changed()
    {
        var a = await ArrangeAsync();
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Unknown) { Forms = [Form("Gastos")] };
        var (requestId, proposal) = await InterpretAsync(a);
        Sent(a, requestId);
        a.Host.Reader.LatestDocumentRequest = Guid.NewGuid();
        a.Host.Reader.DocumentTap = Tap(a, WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, null), "Otro");

        Assert.True((await a.Taps.ProcessTapAsync(a.Host.Context.Contact.Id, Ct)).IsSuccess);

        Assert.Empty(a.Documents.Others);
        Assert.Equal("Esa pregunta ya cambió. Usá la del último documento.", a.Host.Conversation.Outgoing[^1].Body);
    }

    // A form chosen when it was not recognized continues the operation with that form fixed and only the documents' capabilities; its
    // proposal registers the document again for that form.
    [Fact]
    public async Task A_form_chosen_from_the_list_continues_the_operation_with_that_form_fixed()
    {
        var a = await ArrangeAsync();
        var chosen = Form("Gastos");
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Unknown) { Forms = [chosen, Form("Visitas")] };
        var (requestId, proposal) = await InterpretAsync(a);
        Sent(a, requestId);
        var tap = Tap(a, WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, chosen.DefinitionId), "Gastos");
        a.Host.Reader.DocumentTap = tap;

        Assert.True((await a.Taps.ProcessTapAsync(a.Host.Context.Contact.Id, Ct)).IsSuccess);

        var continuation = a.Host.Requests.Rows[^1];
        Assert.Equal((tap.Id, (Guid?)requestId, (Guid?)a.Document.Id, WhatsAppAssistantRequestState.Pending),
            (continuation.SourceMessageId, continuation.PreviousRequestId, continuation.AttachmentMessageId, continuation.State));
        var context = WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(continuation));
        Assert.Equal((chosen.DefinitionId, true), (context.FocusDefinitionId, WhatsAppAssistantAccessPolicy.LoadsDocumentOnly(context)));
        // The interpretation takes it with the tap's title as its text and the document.
        a.Host.Reader.Source = new WhatsAppAssistantSourceRow(tap.Id, a.Host.Context.Contact.Id, "Gastos", tap.OccurredAtUtc, tap.OccurredAtUtc.AddDays(90))
            { WaMessageId = tap.WaMessageId, Kind = WhatsAppMessageKind.ButtonReply, Document = a.Row };
        var work = (await a.Dispatch.PrepareAsync(a.Host.Context.Contact.Id, Ct)).Value!;
        Assert.Equal((continuation.RequestId, "Gastos", (Guid?)requestId, a.Row.StorageKey),
            (work.RequestId, work.Operation!.Text, work.Operation.PreviousRequestId, work.Operation.Attachment!.StorageKey));
        // Its proposal registers the document again, naming the form chosen.
        var focused = new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), WhatsAppAssistantDocumentPolicy.ProposeEntry, new EntryPayload());
        var outcome = new AssistantOperationResponse(work.RequestId, "completed", focused,
            new("qualified-text", 1, "capabilities-v1", 1, a.Host.Context.Clock.GetUtcNow().UtcDateTime, "whatsapp-assistant"), null);
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Quota);
        Assert.True((await a.Dispatch.CompleteAsync(a.Host.Context.Contact.Id, work.RequestId, outcome, Ct)).IsSuccess);
        Assert.Equal((chosen.DefinitionId, focused.Id), (a.Documents.Registrations[^1].FocusDefinitionId, a.Documents.Registrations[^1].Proposal!.Id));
    }

    private const string Expired = "Ese documento ya quedó para que lo revise el equipo. Si hace falta, te escriben por acá.";

    // Review of task 9: a document question is answered once, like a clarification: a later tap, on any of its options, is told the
    // question changed and asks the inbox nothing (it would move the document to another form or leave a second form open).
    [Fact]
    public async Task A_second_tap_on_the_same_question_says_it_changed_and_asks_the_inbox_nothing()
    {
        var a = await ArrangeAsync();
        var prefill = a.Host.Forms.Prefill.Value;
        var chosen = new StructuredDocumentFormChoice(prefill.Form.DefinitionId, prefill.Form.VersionId, "Visita");
        var other = Form("Gastos");
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Doubt) { Forms = [chosen, other] };
        a.Documents.Preparation = new(StructuredDocumentSendState.Ready) { Prefill = prefill };
        var (requestId, proposal) = await InterpretAsync(a);
        Sent(a, requestId);
        a.Host.Reader.DocumentTap = Tap(a, WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, chosen.DefinitionId), "Visita");
        Assert.True((await a.Taps.ProcessTapAsync(a.Host.Context.Contact.Id, Ct)).IsSuccess);
        Assert.Equal(WhatsAppOutgoingMessageKind.Flow, a.Host.Conversation.Outgoing[^1].Kind);
        // What the reader finds once that tap got its own reference.
        a.Host.Reader.AnsweredDocumentRequests.Add(requestId);
        var asked = (a.Documents.Registrations.Count, a.Documents.Sends.Count, a.Documents.Others.Count);

        foreach (var (replyId, title) in new[] { (WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, other.DefinitionId), "Gastos"),
            (WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, chosen.DefinitionId), "Visita"),
            (WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, null), "Otro") })
        {
            a.Host.Reader.DocumentTap = Tap(a, replyId, title);
            Assert.True((await a.Taps.ProcessTapAsync(a.Host.Context.Contact.Id, Ct)).IsSuccess);
            Assert.Equal((WhatsAppOutgoingMessageKind.Text, "Esa pregunta ya cambió. Usá la del último documento."),
                (a.Host.Conversation.Outgoing[^1].Kind, a.Host.Conversation.Outgoing[^1].Body));
        }
        Assert.Equal(asked, (a.Documents.Registrations.Count, a.Documents.Sends.Count, a.Documents.Others.Count));
    }

    private const string NotPrepared = "No pude preparar el formulario. Lo dejé para que lo revise el equipo.";

    // Closing task 9: what the inbox could not take now («Otro» unavailable) is said, and the tap's reference names itself as the proposal,
    // so it closes nothing: the question stays open for another tap. An applied choice names the question's proposal.
    [Fact]
    public async Task A_choice_the_inbox_cannot_take_says_it_could_not_prepare_it_and_leaves_the_question_open()
    {
        var a = await ArrangeAsync();
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Unknown) { Forms = [Form("Gastos")] };
        var (requestId, proposal) = await InterpretAsync(a);
        Sent(a, requestId);
        a.Documents.OtherState = StructuredDocumentChoiceState.Unavailable;
        a.Host.Reader.DocumentTap = Tap(a, WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, null), "Otro");

        Assert.True((await a.Taps.ProcessTapAsync(a.Host.Context.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((WhatsAppOutgoingMessageKind.Text, NotPrepared), (a.Host.Conversation.Outgoing[^1].Kind, a.Host.Conversation.Outgoing[^1].Body));
        var failed = a.Host.Requests.Rows[^1];
        Assert.Equal((WhatsAppAssistantRequestState.Sent, (Guid?)failed.RequestId), (failed.State, failed.ProposalId));

        a.Documents.OtherState = StructuredDocumentChoiceState.Recorded;
        a.Host.Reader.DocumentTap = Tap(a, WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, null), "Otro");
        Assert.True((await a.Taps.ProcessTapAsync(a.Host.Context.Contact.Id, Ct)).IsSuccess);
        Assert.Equal((Guid?)proposal.Id, a.Host.Requests.Rows[^1].ProposalId);
    }

    // Closing task 9: a candidate whose operation the engine cannot read now sends no form and says it could not prepare it.
    [Fact]
    public async Task A_candidate_whose_operation_the_engine_cannot_read_says_it_could_not_prepare_it()
    {
        var a = await ArrangeAsync();
        var prefill = a.Host.Forms.Prefill.Value;
        var chosen = new StructuredDocumentFormChoice(prefill.Form.DefinitionId, prefill.Form.VersionId, "Visita");
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Doubt) { Forms = [chosen, Form("Gastos")] };
        a.Documents.Preparation = new(StructuredDocumentSendState.Ready) { Prefill = prefill };
        var (requestId, proposal) = await InterpretAsync(a);
        Sent(a, requestId);
        a.Host.Assistant.Outcome = WhatsAppChatErrors.NotFound;
        a.Host.Reader.DocumentTap = Tap(a, WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, chosen.DefinitionId), "Visita");

        Assert.True((await a.Taps.ProcessTapAsync(a.Host.Context.Contact.Id, Ct)).IsSuccess);

        Assert.Empty(a.Documents.Sends);
        Assert.Equal((WhatsAppOutgoingMessageKind.Text, NotPrepared), (a.Host.Conversation.Outgoing[^1].Kind, a.Host.Conversation.Outgoing[^1].Body));
    }

    // Closing task 9: another tap on the same question may get its own reference while the inbox is asked: the check under the contact's lock
    // finds it, and this tap is told the question changed, with a reference that closes nothing.
    [Fact]
    public async Task A_tap_answered_by_another_while_the_inbox_was_asked_is_told_the_question_changed()
    {
        var a = await ArrangeAsync();
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Unknown) { Forms = [Form("Gastos")] };
        var (requestId, proposal) = await InterpretAsync(a);
        Sent(a, requestId);
        a.Documents.OnRegister = () => a.Host.Reader.AnsweredDocumentRequests.Add(requestId);
        a.Host.Reader.DocumentTap = Tap(a, WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, null), "Otro");

        Assert.True((await a.Taps.ProcessTapAsync(a.Host.Context.Contact.Id, Ct)).IsSuccess);

        Assert.Equal("Esa pregunta ya cambió. Usá la del último documento.", a.Host.Conversation.Outgoing[^1].Body);
        Assert.Equal((Guid?)a.Host.Requests.Rows[^1].RequestId, a.Host.Requests.Rows[^1].ProposalId);
    }

    // Review of task 9: a tap after an operator took over the conversation, or after the number was unlinked, changes nothing: the inbox is
    // not asked, nothing is said and the tap is marked.
    [Theory]
    [InlineData("manual")]
    [InlineData("unlinked")]
    public async Task A_tap_after_an_operator_took_over_or_the_number_was_unlinked_changes_nothing(string change)
    {
        var a = await ArrangeAsync();
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Unknown) { Forms = [Form("Gastos")] };
        var (requestId, proposal) = await InterpretAsync(a);
        Sent(a, requestId);
        if (change == "manual") a.Host.Context.Contact.ChangeMode(WhatsAppConversationMode.Manual);
        else a.Host.Context.Contact.UnlinkUser();
        a.Host.Reader.DocumentTapState = WhatsAppAssistantDocumentTapState.Closed;
        var before = (a.Documents.Registrations.Count, a.Host.Conversation.Outgoing.Count);
        var tap = Tap(a, WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, null), "Otro");
        a.Host.Reader.DocumentTap = tap;

        Assert.True((await a.Taps.ProcessTapAsync(a.Host.Context.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((before.Item1, 0, before.Item2), (a.Documents.Registrations.Count, a.Documents.Others.Count, a.Host.Conversation.Outgoing.Count));
        Assert.NotNull(a.Host.Messages.Messages.Single(message => message.Id == tap.Id).AssistantProcessedAtUtc);
    }

    // Review of task 9: the inbox keeps until when its person is waited for; a tap after it sends no form, which would outlive it, and says
    // the team has it.
    [Fact]
    public async Task A_tap_after_the_inbox_stopped_waiting_says_the_team_has_it_and_sends_no_form()
    {
        var a = await ArrangeAsync();
        var prefill = a.Host.Forms.Prefill.Value;
        var chosen = new StructuredDocumentFormChoice(prefill.Form.DefinitionId, prefill.Form.VersionId, "Visita");
        var now = a.Host.Context.Clock.GetUtcNow().UtcDateTime;
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Doubt) { Forms = [chosen, Form("Gastos")], ReplyExpiresAtUtc = now.AddHours(2) };
        a.Documents.Preparation = new(StructuredDocumentSendState.Ready) { Prefill = prefill };
        var (requestId, proposal) = await InterpretAsync(a);
        Sent(a, requestId);
        a.Host.Context.Clock.Advance(TimeSpan.FromHours(3));
        a.Host.Reader.DocumentTap = Tap(a, WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposal.Id, chosen.DefinitionId), "Visita");

        Assert.True((await a.Taps.ProcessTapAsync(a.Host.Context.Contact.Id, Ct)).IsSuccess);

        Assert.Empty(a.Documents.Sends);
        Assert.Equal((WhatsAppOutgoingMessageKind.Text, Expired), (a.Host.Conversation.Outgoing[^1].Kind, a.Host.Conversation.Outgoing[^1].Body));
    }

    // Review of task 9: the form of a document ends when the inbox stops waiting for its person, as the inbox kept it.
    [Fact]
    public async Task The_form_of_a_document_ends_when_the_inbox_stops_waiting_for_its_person()
    {
        var a = await ArrangeAsync();
        var prefill = a.Host.Forms.Prefill.Value;
        var now = a.Host.Context.Clock.GetUtcNow().UtcDateTime;
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Sure)
            { Forms = [new(prefill.Form.DefinitionId, prefill.Form.VersionId, "Visita")], ReplyExpiresAtUtc = now.AddHours(2) };
        a.Documents.Preparation = new(StructuredDocumentSendState.Ready) { Prefill = prefill };

        await InterpretAsync(a);

        Assert.Equal(now.AddHours(2), Assert.Single(a.Host.Sessions.Rows).ExpiresAtUtc);
    }

    // Review of task 9: a send the inbox no longer waits for (its deadline passed under the document's lock) says the team has it.
    [Fact]
    public async Task A_send_the_inbox_no_longer_waits_for_says_the_team_has_it()
    {
        var a = await ArrangeAsync();
        var prefill = a.Host.Forms.Prefill.Value;
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Sure) { Forms = [new(prefill.Form.DefinitionId, prefill.Form.VersionId, "Visita")] };
        a.Documents.Preparation = new(StructuredDocumentSendState.Expired);

        await InterpretAsync(a);

        Assert.Equal((WhatsAppOutgoingMessageKind.Text, Expired), (Assert.Single(a.Host.Conversation.Outgoing).Kind, a.Host.Conversation.Outgoing[0].Body));
        Assert.Empty(a.Host.Sessions.Rows);
    }

    // Review of task 9: the rows of a list stay apart: a cut title keeps the full name under it, and the forms whose titles still repeat
    // (the same name, or the same start) are numbered.
    [Fact]
    public async Task The_rows_of_a_list_stay_apart_with_the_full_name_under_a_cut_title_and_a_number()
    {
        var a = await ArrangeAsync();
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Unknown)
            { Forms = [Form("Comprobante de transferencia bancaria"), Form("Comprobante de transferencia interna"), Form("Pagos"), Form("Pagos")] };

        await InterpretAsync(a);

        var rows = Assert.Single(a.Host.Conversation.Outgoing).QuickReplies;
        Assert.Equal(rows.Count, rows.Select(row => row.Title).Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, row => Assert.InRange(row.Title.Length, 1, WhatsAppOutgoingMessage.MaxListRowTitleLength));
        Assert.Equal(["Comprobante de transferencia bancaria", "Comprobante de transferencia interna", null, null, null], rows.Select(row => row.Description));
        Assert.Equal(["Pagos (1)", "Pagos (2)", "Otro"], rows.Skip(2).Select(row => row.Title));
    }

    // Review of task 9: a form that answers a document waits its turn after an outgoing that is not resolved yet (another reply, an uncertain
    // send): the person is never left without a reply.
    [Fact]
    public async Task A_document_form_waits_its_turn_after_an_unresolved_outgoing()
    {
        var a = await ArrangeAsync();
        var prefill = a.Host.Forms.Prefill.Value;
        a.Documents.Registration = new(Guid.NewGuid(), StructuredDocumentReplyKind.Sure) { Forms = [new(prefill.Form.DefinitionId, prefill.Form.VersionId, "Visita")] };
        a.Documents.Preparation = new(StructuredDocumentSendState.Ready) { Prefill = prefill };
        a.Host.Conversation.Unresolved = true;

        await InterpretAsync(a);

        Assert.Equal(WhatsAppOutgoingMessageKind.Flow, Assert.Single(a.Host.Conversation.Outgoing).Kind);
        Assert.Equal(WhatsAppAssistantRequestState.Sent, Assert.Single(a.Host.Requests.Rows).State);
    }

    private sealed record EntryPayload : AssistantCapabilityPayload;

    // The inbox's port as the channel sees it, called outside any transaction of the channel.
    internal sealed class Documents(FakeUnitOfWork transaction) : IStructuredDocumentChannel
    {
        public List<StructuredDocumentRegistrationRequest> Registrations { get; } = [];
        public List<StructuredDocumentSendRequest> Sends { get; } = [];
        public List<StructuredDocumentChoiceRequest> Others { get; } = [];
        public StructuredDocumentRegistration Registration { get; set; } = new(Guid.NewGuid(), StructuredDocumentReplyKind.Unavailable);
        public StructuredDocumentSendPreparation Preparation { get; set; } = new(StructuredDocumentSendState.Unavailable);
        public StructuredDocumentChoiceState OtherState { get; set; } = StructuredDocumentChoiceState.Recorded;
        // What happens elsewhere while the inbox is asked (another tap's commit, for instance).
        public Action? OnRegister { get; set; }

        public Task<StructuredDocumentLoading> FindLoadingAsync(Guid personUserId, CancellationToken cancellationToken) =>
            Task.FromResult(new StructuredDocumentLoading(true, true));

        public Task<StructuredDocumentRegistration> RegisterAsync(StructuredDocumentRegistrationRequest request, CancellationToken cancellationToken)
        {
            Assert.False(transaction.InTransaction);
            Registrations.Add(request);
            OnRegister?.Invoke();
            return Task.FromResult(Registration);
        }

        public Task<StructuredDocumentSendPreparation> PrepareSendAsync(StructuredDocumentSendRequest request, CancellationToken cancellationToken)
        {
            Assert.False(transaction.InTransaction);
            Sends.Add(request);
            return Task.FromResult(Preparation);
        }

        public Task<StructuredDocumentChoiceState> ChooseOtherAsync(StructuredDocumentChoiceRequest request, CancellationToken cancellationToken)
        {
            Assert.False(transaction.InTransaction);
            Others.Add(request);
            return Task.FromResult(OtherState);
        }
    }
}
