using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// La última invitación de una cuenta (IUserInvitationReader) contra Postgres: cuál gana, el canal, si falló y el id que
/// le dio el proveedor. El estado de entrega no sale de acá: lo da la fuente del canal (con WhatsApp,
/// WhatsAppMessageReaderTests). Las invitaciones no tienen clave foránea a la cuenta, así que cada test usa un Id de
/// cuenta propio sin crearla.
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
        var tiedByWhatsApp = ByWhatsApp(userId);
        var tiedByEmail = UserInvitation.ByEmail(userId, Guid.CreateVersion7(), SentAt);
        var otherUser = UserInvitation.ByEmail(Guid.CreateVersion7(), Guid.CreateVersion7(), SentAt.AddMinutes(5));
        await AddAsync(tiedByEmail, older, otherUser, tiedByWhatsApp);

        var latest = await FindLatestAsync(userId);

        // Mismo momento: gana el Id más alto. Dos Guid v7 del mismo milisegundo no siempre salen en orden, así que el
        // esperado se calcula como lo compara Postgres, byte a byte, que es el orden del texto en minúsculas.
        var winner = string.CompareOrdinal(tiedByWhatsApp.Id.ToString("D", CultureInfo.InvariantCulture), tiedByEmail.Id.ToString("D", CultureInfo.InvariantCulture)) > 0
            ? UserInvitationChannel.WhatsApp
            : UserInvitationChannel.Email;
        Assert.Equal(new UserInvitationRow(winner, SentAt, SendFailed: false, ProviderMessageId: null), latest);
    }

    [Fact]
    public async Task An_invitation_that_could_not_be_sent_says_so()
    {
        var userId = Guid.CreateVersion7();
        var invitation = UserInvitation.ByEmail(userId, Guid.CreateVersion7(), SentAt);
        invitation.MarkSendFailed();
        await AddAsync(invitation);

        Assert.Equal(
            new UserInvitationRow(UserInvitationChannel.Email, SentAt, SendFailed: true, ProviderMessageId: null),
            await FindLatestAsync(userId));
    }

    [Fact]
    public async Task An_invitation_that_has_not_left_has_no_provider_id()
    {
        var userId = Guid.CreateVersion7();
        await AddAsync(ByWhatsApp(userId));

        Assert.Equal(
            new UserInvitationRow(UserInvitationChannel.WhatsApp, SentAt, SendFailed: false, ProviderMessageId: null),
            await FindLatestAsync(userId));
    }

    [Fact]
    public async Task An_invitation_that_left_carries_the_id_of_the_provider()
    {
        var userId = Guid.CreateVersion7();
        var providerMessageId = "provider." + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        var invitation = ByWhatsApp(userId);
        invitation.AttachProviderMessage(providerMessageId);
        await AddAsync(invitation);

        Assert.Equal(
            new UserInvitationRow(UserInvitationChannel.WhatsApp, SentAt, SendFailed: false, providerMessageId),
            await FindLatestAsync(userId));
    }

    private static UserInvitation ByWhatsApp(Guid userId) =>
        UserInvitation.Send(userId, UserInvitationChannel.WhatsApp, Guid.CreateVersion7(), SentAt, consentConfirmed: true);

    private Task<int> AddAsync(params UserInvitation[] invitations) =>
        factory.ExecuteDbContextAsync(db =>
        {
            db.UserInvitations.AddRange(invitations);
            return db.SaveChangesAsync(Ct);
        });

    private Task<UserInvitationRow?> FindLatestAsync(Guid userId) =>
        factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserInvitationReader>().FindLatestAsync(userId, Ct));
}
