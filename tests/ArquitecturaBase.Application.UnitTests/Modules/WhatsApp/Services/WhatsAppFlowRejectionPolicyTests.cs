using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// Task 9c of the documents (point 6): what a capture through WhatsApp rejected answers, in the unit of work that records the rejection.
public sealed class WhatsAppFlowRejectionPolicyTests
{
    private static readonly DateTime Now = WhatsAppAssistantContextFixture.Now.UtcDateTime;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_fixed_validation_sends_the_form_again_in_a_new_send_with_what_was_sent_its_mark_and_the_reason()
    {
        var host = await Host.CreateAsync(automatic: false);

        await host.Policy.QueueAsync(host.Reply, host.Session, host.Rejection(Host.Fixed(host.Account.FieldId)), Ct);

        var flow = Assert.Single(host.Conversations.Outgoing);
        var session = flow.FlowSession!;
        Assert.Equal((WhatsAppOutgoingMessageKind.Flow, WhatsAppMessageOrigin.Conversation, host.Session.OperatorUserId, false, session.Id),
            (flow.Kind, flow.Origin, flow.OperatorUserId, flow.IsResend, flow.FlowSessionId));
        Assert.Equal("Revisá la cuenta: los números no dan. Copiala del comprobante. Corregilo en el formulario «Comprobante» y confirmalo.", flow.Body);
        Assert.NotEqual(host.Session.Id, session.Id);
        Assert.Same(session, Assert.Single(host.Sessions.Rows));
        Assert.Equal((host.Session.ExpiresAtUtc, host.Session.DefinitionId, host.Session.VersionId, host.Session.BindingId, host.Session.OperatorUserId,
                WhatsAppFlowCaptureState.Pending),
            (session.ExpiresAtUtc, session.DefinitionId, session.VersionId, session.BindingId, session.OperatorUserId, session.CaptureState));
        // A token of its own: the person answers the new send, and its owner keeps another receipt.
        var token = host.Protector.Payload!.Token;
        Assert.NotEqual("old-token", token);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token))), session.TokenHash);
        var content = host.Protector.Content!;
        Assert.Equal([(host.Account.FieldId, "123"), (host.Place.FieldId, "Sucursal Norte")], content.Answers!.Select(answer => (answer.FieldId, answer.Value)));
        var mark = Assert.Single(content.Server!.Marks!);
        Assert.Equal((host.Account.FieldId, StructuredDocumentMarkKind.Invalid, "Forms.Record.BankAccountInvalid", true),
            (mark.FieldId, mark.Kind, mark.ErrorCode, content.Server.Correction));
        // A send of the operator without a proposal keeps the operator as its sender, with nothing proposed.
        Assert.Equal((StructuredRecordSenderKind.Manual, host.Session.OperatorUserId, 0), (content.Origin.SenderKind, content.Origin.OperatorUserId, content.Origin.ProposedValues.Count));
        Assert.Empty(host.Requests.Rows);
    }

    [Fact]
    public async Task An_assistant_send_answers_with_a_reference_of_its_own_on_the_reply_that_carries_the_one_that_sent_the_form()
    {
        var host = await Host.CreateAsync(automatic: true);

        await host.Policy.QueueAsync(host.Reply, host.Session, host.Rejection(Host.Fixed(host.Account.FieldId)), Ct);

        var flow = Assert.Single(host.Conversations.Outgoing, outgoing => outgoing.Id != host.First!.Id);
        Assert.Equal((WhatsAppOutgoingMessageKind.Flow, WhatsAppMessageOrigin.Assistant, host.First!.RecipientUserId, host.First.RecipientLinkRevision,
                host.First.RecipientWaId, host.First.AssistantModeRevision),
            (flow.Kind, flow.Origin, flow.RecipientUserId, flow.RecipientLinkRevision, flow.RecipientWaId, flow.AssistantModeRevision));
        var reference = Assert.Single(host.Requests.Rows, row => row.SourceMessageId == host.Reply.Id);
        Assert.Equal((WhatsAppAssistantRequestState.Sent, flow.Id, reference.RequestId, host.Sent!.ContextJson, host.Sent.AttachmentMessageId, host.Sent.ContentExpiresAtUtc),
            (reference.State, reference.OutgoingId, reference.ProposalId, reference.ContextJson, reference.AttachmentMessageId, reference.ContentExpiresAtUtc));
        // The new send keeps the origin of the first one: what was proposed and the document it came from.
        Assert.Same(host.Content!.Origin, host.Protector.Content!.Origin);
        Assert.Same(flow.FlowSession, Assert.Single(host.Sessions.Rows));
        Assert.Equal(host.Content.Server!.File, host.Protector.Content.Server!.File);
    }

    [Fact]
    public async Task A_repeated_capture_only_says_when_and_where_it_was_loaded()
    {
        var host = await Host.CreateAsync(automatic: true);
        var repeated = new StructuredRecordCaptureResponse("Rejected", null, null, "Forms.Record.Repeated")
        { Repetition = new("REC-000012", new DateTime(2026, 11, 5, 15, 0, 0, DateTimeKind.Utc)) };

        await host.Policy.QueueAsync(host.Reply, host.Session, host.Rejection(repeated), Ct);

        var notice = Assert.Single(host.Conversations.Outgoing, outgoing => outgoing.Id != host.First!.Id);
        Assert.Equal((WhatsAppOutgoingMessageKind.Text, "Este comprobante ya se cargó el 05/11/2026 en REC-000012. No lo cargo de nuevo."), (notice.Kind, notice.Body));
        Assert.Equal(notice.Id, Assert.Single(host.Requests.Rows, row => row.SourceMessageId == host.Reply.Id).OutgoingId);
        Assert.Null(host.Protector.Content);
        Assert.Empty(host.Sessions.Rows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Another_rejection_answers_nothing(bool automatic)
    {
        var host = await Host.CreateAsync(automatic);

        await host.Policy.QueueAsync(host.Reply, host.Session, host.Rejection(new("Rejected", null, null, "Forms.Record.Required", host.Place.FieldId)), Ct);

        Assert.Equal(automatic ? 1 : 0, host.Conversations.Outgoing.Count);
        Assert.Equal(automatic ? 1 : 0, host.Requests.Rows.Count);
        Assert.Null(host.Protector.Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_same_reply_is_answered_once(bool automatic)
    {
        var host = await Host.CreateAsync(automatic);
        await host.Policy.QueueAsync(host.Reply, host.Session, host.Rejection(Host.Fixed(host.Account.FieldId)), Ct);
        var queued = host.Conversations.Outgoing.Count;

        // Again before it is marked processed (the reference or the request of what answers it) and after it.
        await host.Policy.QueueAsync(host.Reply, host.Session, host.Rejection(Host.Fixed(host.Account.FieldId)), Ct);
        host.Reply.MarkProcessed(Now);
        await host.Policy.QueueAsync(host.Reply, host.Session, host.Rejection(Host.Fixed(host.Account.FieldId)), Ct);

        Assert.Equal(automatic ? 2 : 1, queued);
        Assert.Equal(queued, host.Conversations.Outgoing.Count);
    }

    // Review of task 9c: an operator's send had no content, so the new one keeps what the person sent as long as the reply that brought
    // it, as an assistant's content lives as long as its source: past the deadline, so a reply sent in time and processed late is read.
    [Fact]
    public async Task An_operator_new_send_keeps_what_was_sent_as_long_as_the_reply_that_brought_it()
    {
        var host = await Host.CreateAsync(automatic: false);

        await host.Policy.QueueAsync(host.Reply, host.Session, host.Rejection(Host.Fixed(host.Account.FieldId)), Ct);

        var session = Assert.Single(host.Sessions.Rows);
        Assert.Equal(host.Reply.OccurredAtUtc.AddDays(Host.RetentionDays), session.AssistantContentExpiresAtUtc);
        Assert.True(session.AssistantContentExpiresAtUtc > session.ExpiresAtUtc);
    }

    // Review of task 9c: like W3, nothing is queued that the reservation would refuse for its sender: an operator's send once the chat
    // went back to the assistant, or the assistant's once an operator took it (another mode revision).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nothing_is_queued_when_the_reservation_would_refuse_its_sender(bool automatic)
    {
        var host = await Host.CreateAsync(automatic);
        host.Contact.ChangeMode(automatic ? WhatsAppConversationMode.Manual : WhatsAppConversationMode.Assistant);

        await host.Policy.QueueAsync(host.Reply, host.Session, host.Rejection(Host.Fixed(host.Account.FieldId)), Ct);

        Assert.Equal(automatic ? 1 : 0, host.Conversations.Outgoing.Count);
        Assert.Equal(automatic ? 1 : 0, host.Requests.Rows.Count);
        Assert.Empty(host.Sessions.Rows);
    }

    [Fact]
    public void Only_a_rejection_by_a_fixed_validation_or_a_repetition_answers()
    {
        Assert.Equal(WhatsAppFlowRejectionAnswer.Resend, WhatsAppFlowRejectionPolicy.Decide(Host.Fixed(Guid.NewGuid())));
        Assert.Equal(WhatsAppFlowRejectionAnswer.Notice, WhatsAppFlowRejectionPolicy.Decide(new("Rejected", null, null, "Forms.Record.Repeated") { Repetition = new(null, Now) }));
        Assert.Equal(WhatsAppFlowRejectionAnswer.None, WhatsAppFlowRejectionPolicy.Decide(new("Rejected", null, null, "Forms.Record.BankAccountInvalid")));
        Assert.Equal(WhatsAppFlowRejectionAnswer.None, WhatsAppFlowRejectionPolicy.Decide(new("Completed", Guid.NewGuid(), 3, null) { FixedValidation = true }));
    }

    private sealed class Host
    {
        public const int RetentionDays = 30;

        // The contact, the person and the operator are the context's real accounts, so the reservation's own checks (W3) run for real.
        private Host(WhatsAppAssistantContextFixture context, bool automatic)
        {
            var contact = context.Contact;
            Contact = contact;
            contact.RecordInbound(contact.WaId, contact.UserIdentifier, contact.ProfileName, Now);
            var userId = context.Subject.Id;
            var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("old-token")));
            Session = automatic
                ? WhatsAppFlowSession.CreateAssistant(contact.Id, contact.LinkRevision, userId, Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), "es",
                    "{\"es\":\"Comprobante\"}", "REC", tokenHash, new string('a', 64), Now.AddMinutes(30))
                : WhatsAppFlowSession.Create(contact.Id, contact.LinkRevision, userId, context.Operator.Id, Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), "es",
                    "{\"es\":\"Comprobante\"}", "REC", tokenHash, new string('a', 64), Now.AddMinutes(30));
            Session.SealPayload([1]);
            Account = new(Guid.CreateVersion7(), "", "shortText", true, 40, []);
            Account = Account with { Key = "field_" + Account.FieldId.ToString("N") };
            Place = new(Guid.CreateVersion7(), "", "shortText", true, 40, []);
            Place = Place with { Key = "field_" + Place.FieldId.ToString("N") };
            _photo = new WhatsAppFlowProjectionField(Guid.CreateVersion7(), "", "photo", false, null, []) { Files = new(0, 1, "camera") };
            _photo = _photo with { Key = "field_" + _photo.FieldId.ToString("N") };
            _projection = new(1, "{}", new string('b', 64), "flow-static-7.3-r13", 1, "es", [Account, Place, _photo]);
            _payload = new(automatic ? 2 : 1, contact.Id, userId, Session.OperatorUserId, Session.DefinitionId, Session.VersionId, Session.BindingId, "es",
                new string('c', 64), 4, "1", "flow-name", _projection.Hash, "old-token", "Abrir", "START", new("waba", "phone", "v25.0", new string('d', 64)), [],
                Session.Origin);
            Reply = WhatsAppMessage.Inbound(contact.Id, "wamid.rejected", WhatsAppMessageKind.FlowReply, null, null, Now, [1]);
            Assert.True(Session.CompleteCapture(Reply.WaMessageId, WhatsAppFlowCaptureState.Rejected, null, null, "Forms.Record.BankAccountInvalid", "Cuenta"));
            Conversations = new(contact);
            Policy = new(Requests, Conversations, Sessions, new WhatsAppFlowSessionIssuer(Protector, context.Clock),
                WhatsAppAssistantDispatchFixture.CreateGuard(context, Reader, Forms, Dispatches), new WhatsAppContentRetention(RetentionDays), context.Clock);
        }

        public static async Task<Host> CreateAsync(bool automatic)
        {
            var context = new WhatsAppAssistantContextFixture();
            if (automatic) context.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
            var host = new Host(context, automatic);
            if (automatic) await host.SendAsync(context);
            return host;
        }

        // The assistant's send of the form: its reference, its first message and the content sealed with it, as the reservation reads them.
        private async Task SendAsync(WhatsAppAssistantContextFixture context)
        {
            var contact = context.Contact;
            var userId = context.Subject.Id;
            var captured = await context.Policy.CaptureAsync(contact, null, Ct);
            Assert.True(captured.IsSuccess);
            var contextJson = JsonSerializer.Serialize(captured.Value.Context, JsonSerializerOptions.Web);
            Sent = WhatsAppAssistantRequest.Create(Guid.CreateVersion7(), contact.Id, Guid.CreateVersion7(), userId, userId, WhatsAppAssistantRequestKind.Automatic,
                contact.ModeRevision, contact.LinkRevision, contextJson, Now.AddMinutes(-10), Now.AddDays(1));
            Assert.True(Sent.CarryAttachment(Guid.CreateVersion7()));
            First = WhatsAppOutgoingMessage.CreateAssistantFlow(userId, contact.Id, Sent.RequestId, "Listo.", Session.Id, Now.AddMinutes(-9), contact.LinkRevision,
                contact.WaId!, contact.ModeRevision);
            Assert.True(Sent.Ready(Guid.CreateVersion7(), Session.DefinitionId, Now.AddMinutes(-9)));
            Assert.True(Sent.Accept(First.Id, new string('e', 64), null, Now.AddMinutes(-9)));
            Requests.Rows.Add(Sent);
            Requests.SessionOutgoings[Session.Id] = [First.Id];
            Conversations.Outgoing.Add(First);
            var origin = new StructuredRecordCaptureOrigin(StructuredRecordSenderKind.Assistant, null, "Leyó el comprobante.",
                new(Guid.CreateVersion7(), "forms.documents.propose-entry", 1, new("qualified", 1, "instructions", 1, Now, "whatsapp-assistant")),
                [new(Account.FieldId, "shortText", "leído")], [Account.FieldId]) { Document = Guid.CreateVersion7() };
            Content = new(1, origin, []) { Server = new(new(_photo.FieldId, "whatsapp/document", "comprobante.pdf", "application/pdf", 10, new string('f', 64)),
                [new(Account.FieldId, StructuredDocumentMarkKind.Doubtful)]) };
            Assert.True(Session.SealAssistantContent([2], Now.AddDays(1)));
            Reader.Reference = new(Sent.RequestId, contact.Id, Sent.SourceMessageId, userId, userId, WhatsAppAssistantRequestKind.Automatic, contact.ModeRevision,
                contact.LinkRevision, contextJson, Sent.ContentExpiresAtUtc, WhatsAppAssistantRequestState.Sent, Sent.ProposalId, Sent.DefinitionId, First.Id);
            Dispatches.Dispatch = new(First.Id, Session.Id, Session.TokenHash, contact.Id, contact.WaId, userId, null, Session.DefinitionId, Session.VersionId,
                Session.BindingId, "es", [1], Session.ExpiresAtUtc, WhatsAppOutgoingMessageState.Accepted, First.Body, contact.Mode, contact.ModeRevision, Now)
            { Origin = WhatsAppMessageOrigin.Assistant, ProtectedAssistantContent = [2], AssistantContentExpiresAtUtc = Now.AddDays(1) };
            Forms.AcceptedForms = [new(Session.DefinitionId, Session.VersionId, 2, "REC", new Dictionary<string, string> { ["es"] = "Comprobante" }, 3,
                new Dictionary<string, string> { ["es"] = "Enviar" })];
        }

        private readonly WhatsAppFlowProjectionField _photo;
        private readonly WhatsAppFlowProjection _projection;
        private readonly WhatsAppFlowSessionPayload _payload;
        public WhatsAppFlowSession Session { get; }
        public WhatsAppFlowProjectionField Account { get; }
        public WhatsAppFlowProjectionField Place { get; }
        public WhatsAppMessage Reply { get; }
        public WhatsAppContact Contact { get; }
        public WhatsAppAssistantRequest? Sent { get; private set; }
        public WhatsAppOutgoingMessage? First { get; private set; }
        public WhatsAppFlowAssistantContent? Content { get; private set; }
        public FakeWhatsAppAssistantReader Reader { get; } = new();
        public FakeAssistantForms Forms { get; } = new();
        public WhatsAppAssistantDispatchFixture.Sessions Dispatches { get; } = new();
        public WhatsAppAssistantSuggestionFixture.Repository Requests { get; } = new();
        public WhatsAppAssistantSuggestionFixture.Conversations Conversations { get; }
        public WhatsAppAssistantSuggestionFixture.SessionProtector Protector { get; } = new();
        public WhatsAppAssistantSuggestionFixture.SessionRepository Sessions { get; } = new();
        public WhatsAppFlowRejectionPolicy Policy { get; }

        public static StructuredRecordCaptureResponse Fixed(Guid fieldId) =>
            new("Rejected", null, null, "Forms.Record.BankAccountInvalid", fieldId) { FixedValidation = true };

        // What the reply carried: the account and the place as the capture decoded them, and a photo the prefill never takes.
        public WhatsAppFlowRejection Rejection(StructuredRecordCaptureResponse capture) => new(capture, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Account.Key] = "123",
            [Place.Key] = "Sucursal Norte",
            ["field_" + Guid.CreateVersion7().ToString("N")] = "ajena",
        }, _projection, _payload, Content);
    }
}
