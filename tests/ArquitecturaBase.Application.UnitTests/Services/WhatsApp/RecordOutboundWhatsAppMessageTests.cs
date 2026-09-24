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

/// <summary>
/// El historial de lo que manda el bot (sección 9 del spec del ingreso con WhatsApp): cada saliente se guarda con el id
/// de Meta, para cruzarlo con los estados, y con su resumen seguro, nunca con el código ni con la URL del enlace.
/// </summary>
public sealed class RecordOutboundWhatsAppMessageTests
{
    private const string WaId = "5493413654813";
    private const string WaMessageId = "wamid.HBgNNTQ5MzQxMzY1NDgxMxUCABEYEjBBQTQ1";

    private static readonly PhoneNumber Phone = PhoneNumber.Create("+" + WaId).Value;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly LockLog _locks = new();
    private readonly InMemoryWhatsAppContactRepository _contacts;
    private readonly InMemoryWhatsAppMessageRepository _messages;
    private readonly FakeIdentityService _identity = new();
    private readonly InMemoryUserInvitationRepository _invitations = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    public RecordOutboundWhatsAppMessageTests()
    {
        _contacts = new InMemoryWhatsAppContactRepository(_locks);
        _messages = new InMemoryWhatsAppMessageRepository(_locks);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task The_sign_in_link_is_saved_with_the_meta_id_and_its_safe_summary_to_the_contact_of_the_number()
    {
        var contact = WhatsAppContact.Create(WaId, "AR.1102953142229032", "Ana", Now.AddMinutes(-1));
        _contacts.Contacts.Add(contact);
        var sent = new WhatsAppLinkButtonMessage(Phone, "Hola, Ana. Para entrar, tocá Entrar.", "Entrar", "https://app.test/ingresar#t=secret-token");

        var result = await RecordAsync(sent);

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(_messages.Added);
        Assert.Equal(WhatsAppMessageDirection.Outbound, saved.Direction);
        Assert.Equal(WaMessageId, saved.WaMessageId);
        Assert.Equal(contact.Id, saved.ContactId);
        Assert.Equal(WhatsAppMessageKind.Interactive, saved.Kind);
        Assert.Equal(sent.SafeSummary, saved.Body);
        Assert.DoesNotContain("secret-token", saved.Body, StringComparison.Ordinal);
        Assert.Equal(Now, saved.OccurredAtUtc);
        Assert.Null(saved.Status);
    }

    /// <summary>El código se manda a números que quizás nunca le escribieron al bot: se guarda sin contacto.</summary>
    [Fact]
    public async Task A_login_code_to_a_number_that_never_wrote_is_saved_as_a_template_without_contact_or_code()
    {
        var result = await RecordAsync(new WhatsAppLoginCodeMessage(Phone, "es", "482913"));

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(_messages.Added);
        Assert.Null(saved.ContactId);
        Assert.Equal(WhatsAppMessageKind.Template, saved.Kind);
        Assert.Equal("[código]", saved.Body);
    }

    [Fact]
    public async Task Texts_and_reply_buttons_keep_their_kind()
    {
        await RecordAsync(new WhatsAppTextMessage(Phone, "Tu cuenta está deshabilitada."));
        await RecordAsync(
            new WhatsAppReplyButtonsMessage(Phone, "¿Querés crear una?", [new WhatsAppReplyButton("CREATE_ACCOUNT", "Crear cuenta")]),
            "wamid.second");

        Assert.Equal([WhatsAppMessageKind.Text, WhatsAppMessageKind.Interactive], _messages.Added.Select(message => message.Kind));
        Assert.Equal("Tu cuenta está deshabilitada.", _messages.Added[0].Body);
    }

    /// <summary>
    /// Si el contacto no se encuentra por su número (WhatsApp lo mandó distinto), se lo busca por la cuenta que tiene ese
    /// número: el contacto de la cuenta es el de la persona.
    /// </summary>
    [Fact]
    public async Task The_contact_is_found_through_the_account_of_the_number_when_its_wa_id_differs()
    {
        var ana = _identity.AddUser(email: null, phoneNumber: Phone.Value);
        var contact = WhatsAppContact.Create("543413654813", "AR.1102953142229032", "Ana", Now.AddMinutes(-1));
        contact.LinkUser(ana.Id);
        _contacts.Contacts.Add(contact);

        await RecordAsync(new WhatsAppTextMessage(Phone, "Hola"));

        Assert.Equal(contact.Id, Assert.Single(_messages.Added).ContactId);
    }

    /// <summary>
    /// La invitación por WhatsApp se guarda como una plantilla, con su resumen seguro, y además le deja a la invitación el
    /// id de Meta: con él, el detalle del usuario lee el estado de entrega que avisa el webhook. Primero toma el lock de
    /// la cuenta, el mismo que el alta y el reenvío: así, si la cola la mandó antes de que se confirmara la invitación, la
    /// espera en lugar de no encontrarla.
    /// </summary>
    [Fact]
    public async Task An_invitation_is_saved_as_a_template_and_leaves_its_meta_id_in_the_invitation()
    {
        var userId = Guid.CreateVersion7();
        var invitation = UserInvitation.ByWhatsApp(userId, Guid.CreateVersion7(), Now.AddSeconds(-2));
        _invitations.Invitations.Add(invitation);

        var result = await RecordAsync(
            new WhatsAppInvitationMessage(Phone, userId, invitation.Id, "es", "Laura Ríos", "Arquitectura Base", "WANT_TO_ENTER"));

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(_messages.Added);
        Assert.Equal(WhatsAppMessageKind.Template, saved.Kind);
        Assert.Equal("[invitación]", saved.Body);
        Assert.Equal(WaMessageId, invitation.WaMessageId);
        Assert.Equal(["lock:" + userId, "read:GetByIdAsync"], _invitations.Events);
    }

    /// <summary>Si la invitación no está (el alta que la encoló no llegó a guardarse), el mensaje se guarda igual.</summary>
    [Fact]
    public async Task An_invitation_that_is_not_there_does_not_stop_saving_the_message()
    {
        var result = await RecordAsync(new WhatsAppInvitationMessage(
            Phone, Guid.CreateVersion7(), Guid.CreateVersion7(), "es", "Laura Ríos", "Arquitectura Base", "WANT_TO_ENTER"));

        Assert.True(result.IsSuccess);
        Assert.Single(_messages.Added);
    }

    [Fact]
    public async Task Other_messages_do_not_touch_the_invitations()
    {
        await RecordAsync(new WhatsAppTextMessage(Phone, "Hola"));

        Assert.Empty(_invitations.Events);
    }

    private Task<Domain.Results.Result> RecordAsync(WhatsAppOutboundMessage message, string waMessageId = WaMessageId) =>
        new WhatsAppDeliveryService(
            _contacts, _messages, _identity, _invitations, _unitOfWork, _clock,
            NullLogger<WhatsAppDeliveryService>.Instance)
            .RecordSentAsync(message, waMessageId, Ct);
}
