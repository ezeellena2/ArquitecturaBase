using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Application.Services.WhatsApp;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles.WhatsApp;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.WhatsApp;

public sealed class WhatsAppDeliveryServiceTests
{
    private const string WaId = "5493413654813";
    private static readonly PhoneNumber Phone = PhoneNumber.Create("+" + WaId).Value;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryWhatsAppContactRepository _contacts;
    private readonly InMemoryWhatsAppMessageRepository _messages;
    private readonly FakeIdentityService _users = new();
    private readonly InMemoryUserInvitationRepository _invitations = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    public WhatsAppDeliveryServiceTests()
    {
        var locks = new LockLog();
        _contacts = new InMemoryWhatsAppContactRepository(locks);
        _messages = new InMemoryWhatsAppMessageRepository(locks);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sent_link_is_recorded_by_meta_id_without_its_secret()
    {
        var contact = WhatsAppContact.Create(WaId, "AR.123", "Ana", _clock.GetUtcNow().UtcDateTime);
        _contacts.Contacts.Add(contact);
        var message = new WhatsAppLinkButtonMessage(Phone, "Ingresá", "Entrar", "https://app.test/#t=secret-token");

        var result = await Service().RecordSentAsync(message, "wamid.123", Ct);

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(_messages.Added);
        Assert.Equal(contact.Id, saved.ContactId);
        Assert.Equal("wamid.123", saved.WaMessageId);
        Assert.DoesNotContain("secret-token", saved.Body, StringComparison.Ordinal);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Sent_invitation_attaches_meta_id_before_commit()
    {
        var userId = Guid.CreateVersion7();
        var invitation = UserInvitation.ByWhatsApp(userId, Guid.CreateVersion7(), _clock.GetUtcNow().UtcDateTime);
        _invitations.Invitations.Add(invitation);

        await Service().RecordSentAsync(
            new WhatsAppInvitationMessage(Phone, userId, invitation.Id, "es", "Ana", "Arquitectura Base", "WANT_TO_ENTER"),
            "wamid.invitation", Ct);

        Assert.Equal("wamid.invitation", invitation.WaMessageId);
        Assert.Equal(["lock:" + userId, "read:GetByIdAsync"], _invitations.Events);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Unsent_invitation_is_marked_failed_and_saved()
    {
        var userId = Guid.CreateVersion7();
        var invitation = UserInvitation.ByWhatsApp(userId, Guid.CreateVersion7(), _clock.GetUtcNow().UtcDateTime);
        _invitations.Invitations.Add(invitation);

        var result = await Service().RecordUnsentAsync(
            new WhatsAppInvitationMessage(Phone, userId, invitation.Id, "es", "Ana", "Arquitectura Base", "WANT_TO_ENTER"), Ct);

        Assert.True(result.IsSuccess);
        Assert.True(invitation.SendFailed);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    private WhatsAppDeliveryService Service() => new(
        _contacts,
        _messages,
        _users,
        _invitations,
        _unitOfWork,
        _clock,
        NullLogger<WhatsAppDeliveryService>.Instance);
}
