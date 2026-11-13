using System.Security.Cryptography;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// W3 (A6): the open sends that can take what the person shared pass the reservation's own checks on their first message, which the
// resend continues; a send whose seals cannot be read takes nothing and is logged by the type of the failure, never with its values.
public sealed class WhatsAppSharedContentGuardTests
{
    private static readonly byte[] Unreadable = [9, 9, 9];
    private static DateTime Now => WhatsAppAssistantContextFixture.Now.UtcDateTime;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly WhatsAppAssistantContextFixture _context = new();
    private readonly FakeLogger<WhatsAppSharedContentGuard> _logger = new();

    public WhatsAppSharedContentGuardTests() =>
        _context.Contact.RecordInbound(_context.Contact.WaId, _context.Contact.UserIdentifier, _context.Contact.ProfileName, Now);

    [Fact]
    public async Task An_operator_who_is_inactive_or_lost_whatsapp_send_leaves_the_send_out()
    {
        var inactive = _context.Users.AddUser("inactive-operator@example.test", isActive: false);
        _context.Permissions.Permissions[inactive.Id] = [WhatsAppPermissions.Read, WhatsAppPermissions.Send];
        var readOnly = _context.Users.AddUser("read-only-operator@example.test");
        _context.Permissions.Permissions[readOnly.Id] = [WhatsAppPermissions.Read];
        WhatsAppFlowOpenSend[] open = [Open(inactive.Id), Open(readOnly.Id), Open(_context.Operator.Id)];

        var usable = await Guard().ListUsableAsync(_context.Contact, open, Ct);

        Assert.Equal([open[2].Session], usable.Select(send => send.Session));
        Assert.Equal((_context.Contact.ModeRevision, 0), (usable[0].ModeRevision, usable[0].Previous.Count));
        Assert.Empty(_logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task A_send_whose_shared_values_cannot_be_read_is_left_out_and_logged_by_type()
    {
        var unreadable = Open(_context.Operator.Id);
        Assert.True(unreadable.Session.TakeShared(WhatsAppMessageKind.Location, [Guid.CreateVersion7()], Now, Now));
        unreadable.Session.SealSharedValues(Unreadable);
        var kept = Open(_context.Operator.Id);
        Assert.True(kept.Session.TakeShared(WhatsAppMessageKind.Location, [Guid.CreateVersion7()], Now, Now));
        kept.Session.SealSharedValues([7]);

        var usable = await Guard().ListUsableAsync(_context.Contact, [unreadable, kept], Ct);

        var send = Assert.Single(usable);
        Assert.Equal((kept.Session, "Bv. Oroño 1234"), (send.Session, Assert.Single(send.Previous).Value));
        var record = Assert.Single(_logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Null(record.Exception);
        Assert.Contains(nameof(CryptographicException), record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Oroño", record.Message, StringComparison.Ordinal);
    }

    private WhatsAppSharedContentGuard Guard() => new(new Projections(), new Protector(_context.Contact.ModeRevision),
        WhatsAppAssistantDispatchFixture.CreateGuard(_context), _logger);

    // A send by an operator whose Flow went out: the session sealed for the linked account and its first message.
    private WhatsAppFlowOpenSend Open(Guid operatorId)
    {
        var contact = _context.Contact;
        var session = WhatsAppFlowSession.Create(contact.Id, contact.LinkRevision, contact.UserId!.Value, operatorId, Guid.CreateVersion7(),
            Guid.CreateVersion7(), 1, Guid.CreateVersion7(), "es", "{\"es\":\"Registrar entrega\"}", "ENT", new('a', 64), new('b', 64), Now.AddHours(20));
        var first = WhatsAppOutgoingMessage.CreateFlow(operatorId, contact.Id, Guid.CreateVersion7(), "Completá el formulario.", session.Id, Now);
        first.AttachFlowSession(session);
        return new(session, first, Guid.CreateVersion7(), [1]);
    }

    private sealed class Projections : IWhatsAppFlowProtector
    {
        public byte[] ProtectManifest(Guid operationId, Guid neutralOperationId, Guid versionId, WhatsAppFlowStoredManifest manifest) =>
            throw new NotSupportedException();
        public WhatsAppFlowStoredManifest ReadManifest(Guid operationId, Guid neutralOperationId, Guid versionId, byte[] protectedManifest) =>
            throw new NotSupportedException();
        public byte[] ProtectProjection(Guid operationId, Guid bindingId, Guid versionId, string culture, WhatsAppFlowProjection projection) =>
            throw new NotSupportedException();
        public WhatsAppFlowProjection ReadProjection(Guid operationId, Guid bindingId, Guid versionId, string culture, byte[] protectedProjection) =>
            new(1, "{}", new string('c', 64), WhatsAppFlowProfile.Revision, 1, culture, []);
    }

    // The payload carries the mode revision the send sealed; what a send took is unreadable when its bytes are, and the failure would
    // carry the value in its message.
    private sealed class Protector(long modeRevision) : IWhatsAppFlowSessionProtector
    {
        public byte[] Protect(Guid sessionId, Guid contactId, Guid userId, Guid versionId, WhatsAppFlowSessionPayload payload) =>
            throw new NotSupportedException();
        public WhatsAppFlowSessionPayload Read(Guid sessionId, Guid contactId, Guid userId, Guid versionId, byte[] payload) =>
            new(1, contactId, userId, null, Guid.Empty, versionId, Guid.Empty, "es", "revision", modeRevision, "meta-flow", "technical", "hash",
                "token", "Abrir", "FORM", new("waba", "phone", "v23.0", "fingerprint"));
        public byte[] ProtectAssistant(Guid sessionId, Guid contactId, Guid userId, Guid versionId, WhatsAppFlowAssistantContent content) =>
            throw new NotSupportedException();
        public WhatsAppFlowAssistantContent ReadAssistant(Guid sessionId, Guid contactId, Guid userId, Guid versionId, byte[] content) =>
            throw new NotSupportedException();
        public byte[] ProtectShared(Guid sessionId, Guid contactId, Guid userId, Guid versionId, WhatsAppFlowSharedValues values) =>
            throw new NotSupportedException();
        public WhatsAppFlowSharedValues ReadShared(Guid sessionId, Guid contactId, Guid userId, Guid versionId, byte[] values) =>
            values.SequenceEqual(Unreadable) ? throw new CryptographicException("Bv. Oroño 1234")
                : new(1, [new(Guid.CreateVersion7(), "Dirección de entrega", "Bv. Oroño 1234")]);
    }
}
