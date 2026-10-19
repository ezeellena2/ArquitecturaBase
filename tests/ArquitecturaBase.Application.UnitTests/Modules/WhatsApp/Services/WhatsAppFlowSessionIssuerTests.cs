using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppFlowSessionIssuerTests
{
    [Fact]
    public void A_proposed_form_uses_the_complete_presentation_culture_for_its_session_and_technical_payload()
    {
        var host = new Fixture(); var issue = host.Issue();
        Assert.Equal("es", issue.Session.Culture); Assert.Equal("es", host.Protector.Payload!.Culture);
    }
    [Fact]
    public void Proposed_values_are_sealed_once_separately_from_the_legacy_token_and_catalog_payload()
    {
        var host = new Fixture(); var issue = host.Issue();
        Assert.NotEmpty(issue.Session.ProtectedAssistantContent); Assert.Same(host.Content, host.Protector.Content);
        var technical = JsonSerializer.Serialize(host.Protector.Payload, JsonSerializerOptions.Web);
        Assert.DoesNotContain("valor privado", technical, StringComparison.Ordinal);
        Assert.DoesNotContain("Motivo privado", technical, StringComparison.Ordinal);
    }
    [Fact]
    public void A_source_expiring_before_the_flow_lifetime_limits_the_opening_token()
    {
        var host = new Fixture(); var expiry = host.Now.AddMinutes(3);
        var issue = host.Issue(expiry);
        Assert.Equal(expiry, issue.Session.ExpiresAtUtc); Assert.Equal(expiry, issue.Session.AssistantContentExpiresAtUtc);
    }
    [Fact]
    public void Business_evidence_survives_the_opening_token_only_until_the_original_message_retention()
    {
        var host = new Fixture(); var expiry = host.Now.AddDays(90); var issue = host.Issue(expiry);
        Assert.Equal(host.Now.AddHours(1), issue.Session.ExpiresAtUtc);
        Assert.Equal(expiry, issue.Session.AssistantContentExpiresAtUtc);
    }
    [Fact]
    public void An_automatic_session_has_explicit_assistant_origin_and_no_invented_operator()
    {
        var host = new Fixture(); var issue = host.Issue(automatic: true);
        Assert.Null(issue.Session.OperatorUserId); Assert.Equal(WhatsAppMessageOrigin.Assistant, issue.Session.Origin);
        Assert.Null(host.Protector.Payload!.OperatorUserId); Assert.Equal(2, host.Protector.Payload.Format);
    }
    [Fact]
    public void An_ordinary_manual_session_keeps_its_existing_payload_and_real_operator()
    {
        var host = new Fixture(); var context = host.Context with { Binding = host.Context.Binding with { Culture = "en" }, Projection = host.Context.Projection with { Culture = "en" } };
        var issue = host.Issuer.Create(context, new(context.Recipient.ContactId, Guid.NewGuid(), context.Form.DefinitionId,
            context.Form.VersionId, 4, context.RecipientRevision, null), host.Actor, host.Fingerprint, "token");
        Assert.Equal(host.Actor, issue.Session.OperatorUserId); Assert.Equal(WhatsAppMessageOrigin.Conversation, issue.Session.Origin);
        Assert.Equal(1, host.Protector.Payload!.Format); Assert.Equal("en", issue.Session.Culture);
        Assert.Empty(issue.Session.ProtectedAssistantContent); Assert.Null(issue.Session.AssistantContentExpiresAtUtc);
    }

    [Fact]
    public void An_ordinary_manual_session_uses_the_prepared_original_binding_when_the_account_is_English()
    {
        var host = new Fixture();
        var context = host.Context;
        var issue = host.Issuer.Create(context, new(context.Recipient.ContactId, Guid.NewGuid(), context.Form.DefinitionId,
            context.Form.VersionId, 4, context.RecipientRevision, null), host.Actor, host.Fingerprint, "token");
        Assert.Equal("en", context.Recipient.Culture);
        Assert.Equal("es", issue.Session.Culture);
        Assert.Equal("es", host.Protector.Payload!.Culture);
        Assert.Equal(context.RecipientRevision, host.Protector.Payload.RecipientRevision);
    }

    private sealed class Fixture
    {
        public DateTime Now { get; } = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        public Guid Actor { get; } = Guid.NewGuid();
        public string Fingerprint { get; } = new('a', 64);
        public CapturingProtector Protector { get; } = new();
        public WhatsAppFormSendContext Context { get; }
        public WhatsAppFlowAssistantContent Content { get; }
        public WhatsAppFlowSessionIssuer Issuer { get; }
        public Fixture()
        {
            var contact = Guid.NewGuid(); var user = Guid.NewGuid(); var definition = Guid.NewGuid(); var version = Guid.NewGuid();
            var recipient = new WhatsAppFormRecipientRow(contact, 3, "5493413654800", "5493413654800", user,
                "Persona ficticia", null, true, "en", "stamp", "roles");
            var binding = new WhatsAppFormBindingRow(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), definition, version,
                "es", "visit", "123", [1], [2]);
            Context = new(recipient, new string('b',64), new(definition, version, 1, "VIS", new Dictionary<string,string>{{"es","Visita"}},1,
                new Dictionary<string,string>{{"es","Guardar"}}), binding, new(1,"{}",new string('c',64),"flow-static-7.3-r8",1,"es",[]),
                "Abrir", "START", new("1","2","v25.0",new string('d',64)), TimeSpan.FromHours(1));
            var field = Guid.NewGuid();
            Content = new(1,new(StructuredRecordSenderKind.Manual,Actor,"Motivo privado",null,[new(field,"shortText","valor privado")],[field]),
                [new(field,"Dato","valor privado")]);
            Issuer = new(Protector, new FakeTimeProvider(Now));
        }
        public WhatsAppFlowSessionIssue Issue(DateTime? expiry = null, bool automatic = false) => Issuer.CreateProposed(Context,
            Guid.NewGuid(),4,automatic?null:Actor,Fingerprint,"token",automatic ? Content with { Origin = Content.Origin with { SenderKind = StructuredRecordSenderKind.Assistant, OperatorUserId = null } } : Content,expiry??Now.AddDays(90));
    }
    private sealed class CapturingProtector : IWhatsAppFlowSessionProtector
    {
        public WhatsAppFlowSessionPayload? Payload { get; private set; }
        public WhatsAppFlowAssistantContent? Content { get; private set; }
        public byte[] Protect(Guid sessionId,Guid contactId,Guid userId,Guid versionId,WhatsAppFlowSessionPayload payload)
        { Payload=payload;return [1]; }
        public WhatsAppFlowSessionPayload Read(Guid sessionId,Guid contactId,Guid userId,Guid versionId,byte[] payload)=>throw new NotSupportedException();
        public byte[] ProtectAssistant(Guid sessionId,Guid contactId,Guid userId,Guid versionId,WhatsAppFlowAssistantContent content)
        { Content=content;return [2]; }
        public WhatsAppFlowAssistantContent ReadAssistant(Guid sessionId,Guid contactId,Guid userId,Guid versionId,byte[] content)=>throw new NotSupportedException();
    }
}
