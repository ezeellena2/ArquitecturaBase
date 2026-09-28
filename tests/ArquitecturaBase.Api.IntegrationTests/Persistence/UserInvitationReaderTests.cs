using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// La última invitación de una cuenta (IUserInvitationReader) contra Postgres: cuál gana, el canal y el estado del mensaje
/// saliente, que sale de una subconsulta por el id de Meta. Las invitaciones no tienen clave foránea a la cuenta, así que
/// cada test usa un Id de cuenta propio sin crearla.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class UserInvitationReaderTests(ApiFactory factory)
{
    private static readonly DateTime SentAt = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_user_never_invited_has_no_latest_invitation()
    {
        Assert.Null(await FindLatestAsync(Guid.CreateVersion7()));
    }

    [Fact]
    public async Task The_newest_invitation_wins_and_the_id_breaks_a_tie()
    {
        var userId = Guid.CreateVersion7();
        var older = UserInvitation.ByEmail(userId, Guid.CreateVersion7(), SentAt.AddMinutes(-5));
        var tiedByWhatsApp = UserInvitation.ByWhatsApp(userId, Guid.CreateVersion7(), SentAt);
        var tiedByEmail = UserInvitation.ByEmail(userId, Guid.CreateVersion7(), SentAt);
        var otherUser = UserInvitation.ByEmail(Guid.CreateVersion7(), Guid.CreateVersion7(), SentAt.AddMinutes(5));
        await AddAsync(invitations: [tiedByEmail, older, otherUser, tiedByWhatsApp]);

        var latest = await FindLatestAsync(userId);

        // Mismo momento: gana el Id más alto. Dos Guid v7 del mismo milisegundo no siempre salen en orden, así que el
        // esperado se calcula como lo compara Postgres, byte a byte, que es el orden del texto en minúsculas.
        var winner = string.CompareOrdinal(tiedByWhatsApp.Id.ToString("D", CultureInfo.InvariantCulture), tiedByEmail.Id.ToString("D", CultureInfo.InvariantCulture)) > 0
            ? UserInvitationChannel.WhatsApp
            : UserInvitationChannel.Email;
        Assert.Equal(new UserInvitationRow(winner, SentAt, SendFailed: false, HasWaMessageId: false, OutboundStatus: null), latest);
    }

    [Fact]
    public async Task An_email_invitation_has_no_message()
    {
        var userId = Guid.CreateVersion7();
        var invitation = UserInvitation.ByEmail(userId, Guid.CreateVersion7(), SentAt);
        invitation.MarkSendFailed();
        await AddAsync(invitations: [invitation]);

        Assert.Equal(
            new UserInvitationRow(UserInvitationChannel.Email, SentAt, SendFailed: true, HasWaMessageId: false, OutboundStatus: null),
            await FindLatestAsync(userId));
    }

    [Fact]
    public async Task A_whatsapp_invitation_that_has_not_left_has_no_meta_id()
    {
        var userId = Guid.CreateVersion7();
        await AddAsync(invitations: [UserInvitation.ByWhatsApp(userId, Guid.CreateVersion7(), SentAt)]);

        Assert.Equal(
            new UserInvitationRow(UserInvitationChannel.WhatsApp, SentAt, SendFailed: false, HasWaMessageId: false, OutboundStatus: null),
            await FindLatestAsync(userId));
    }

    [Fact]
    public async Task A_whatsapp_invitation_without_a_saved_message_has_no_status()
    {
        var userId = Guid.CreateVersion7();
        var invitation = UserInvitation.ByWhatsApp(userId, Guid.CreateVersion7(), SentAt);
        invitation.AttachWhatsAppMessage(MetaWebhook.UniqueWaMessageId());
        await AddAsync(invitations: [invitation]);

        Assert.Equal(
            new UserInvitationRow(UserInvitationChannel.WhatsApp, SentAt, SendFailed: false, HasWaMessageId: true, OutboundStatus: null),
            await FindLatestAsync(userId));
    }

    [Fact]
    public async Task A_saved_message_without_a_status_yet_has_no_status()
    {
        var userId = Guid.CreateVersion7();
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var invitation = UserInvitation.ByWhatsApp(userId, Guid.CreateVersion7(), SentAt);
        invitation.AttachWhatsAppMessage(waMessageId);
        await AddAsync(invitations: [invitation], messages: [Outbound(waMessageId)]);

        Assert.Equal(
            new UserInvitationRow(UserInvitationChannel.WhatsApp, SentAt, SendFailed: false, HasWaMessageId: true, OutboundStatus: null),
            await FindLatestAsync(userId));
    }

    [Theory]
    [InlineData(WhatsAppMessageStatus.Sent)]
    [InlineData(WhatsAppMessageStatus.Delivered)]
    [InlineData(WhatsAppMessageStatus.Read)]
    [InlineData(WhatsAppMessageStatus.Failed)]
    public async Task A_whatsapp_invitation_carries_the_status_of_its_message(WhatsAppMessageStatus status)
    {
        var userId = Guid.CreateVersion7();
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var invitation = UserInvitation.ByWhatsApp(userId, Guid.CreateVersion7(), SentAt);
        invitation.AttachWhatsAppMessage(waMessageId);
        var message = Outbound(waMessageId);
        message.ApplyStatus(status, SentAt.AddMinutes(1), status is WhatsAppMessageStatus.Failed ? 131026 : null);
        // Otro saliente con estado, de otra invitación: la subconsulta no lo tiene que mirar.
        var other = Outbound(MetaWebhook.UniqueWaMessageId());
        other.ApplyStatus(WhatsAppMessageStatus.Delivered, SentAt.AddMinutes(1), errorCode: null);
        await AddAsync(invitations: [invitation], messages: [message, other]);

        Assert.Equal(
            new UserInvitationRow(UserInvitationChannel.WhatsApp, SentAt, SendFailed: false, HasWaMessageId: true, status),
            await FindLatestAsync(userId));
    }

    private static WhatsAppMessage Outbound(string waMessageId) =>
        WhatsAppMessage.Outbound(contactId: null, waMessageId, WhatsAppMessageKind.Template, "[invitación]", SentAt);

    private Task<int> AddAsync(UserInvitation[] invitations, WhatsAppMessage[]? messages = null) =>
        factory.ExecuteDbContextAsync(db =>
        {
            db.UserInvitations.AddRange(invitations);
            db.WhatsAppMessages.AddRange(messages ?? []);
            return db.SaveChangesAsync(Ct);
        });

    private Task<UserInvitationRow?> FindLatestAsync(Guid userId) =>
        factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserInvitationReader>().FindLatestAsync(userId, Ct));
}
