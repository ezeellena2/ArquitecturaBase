using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Las invitaciones de un administrador (sección 6.6 del spec del ingreso con WhatsApp), en un solo lugar: las usan el
/// alta, que puede mandar una, y el reenvío. Ninguna lleva algo que sirva para entrar. Cada canal es un adaptador de
/// <see cref="IInvitationChannel"/>, que pone sus reglas y encola el mensaje en el idioma de la cuenta: el correo, que
/// trae el núcleo (un botón a /login, y la persona entra con el código de siempre), y WhatsApp, que trae su módulo (la
/// plantilla con «Quiero entrar»: al tocarlo, el bot le manda el enlace, fila 7 de la sección 8, que nace recién ahí y
/// dura 10 minutos). Expone pasos separados para que el servicio los intercale: las reglas (<see cref="Check"/>), el lock
/// del reenvío (<see cref="LockAsync"/>), la espera entre dos (<see cref="WaitBeforeAnotherAsync"/>) y el envío
/// (<see cref="SendAsync"/>). No abre ni confirma transacciones.
/// </summary>
internal sealed class UserInvitationIssuer(
    IUserInvitationRepository invitations,
    IEnumerable<IInvitationChannel> channels,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Las reglas de la invitación, antes de tocar nada: las del canal pedido (<see cref="IInvitationChannel.Check"/>),
    /// cada error en el campo que lo arregla (<paramref name="fields"/>). Sin un canal para el pedido (WhatsApp sin su
    /// módulo) se responde lo mismo que con WhatsApp apagado: no hay por dónde mandarla.
    /// </summary>
    public Result Check(
        UserInvitationChannel channel,
        bool consent,
        string? displayName,
        bool hasEmail,
        bool hasPhone,
        InvitationFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (FindAdapter(channel) is not { } adapter)
        {
            return FieldErrors.Validation(fields.Channel, ValidationMessages.InvitationWhatsAppUnavailable);
        }

        return adapter.Check(new InvitationCheck(
            hasEmail, hasPhone, consent, displayName, fields.Channel, fields.Consent, fields.DisplayName));
    }

    /// <summary>
    /// El primer lock del reenvío, antes de leer la cuenta: dos reenvíos de la misma cuenta pasan de a uno y el segundo ve
    /// el guardado del primero. <see cref="SendAsync"/> lo vuelve a tomar (es reentrante).
    /// </summary>
    public Task LockAsync(Guid userId, CancellationToken cancellationToken) =>
        invitations.LockAccountAsync(userId, cancellationToken);

    /// <summary>
    /// Cuánto falta para poder mandarle otra invitación a <paramref name="userId"/>, contado desde la última que salió;
    /// cero si ya puede. Quien llama ya tomó el lock (<see cref="LockAsync"/>).
    /// </summary>
    public async Task<TimeSpan> WaitBeforeAnotherAsync(Guid userId, CancellationToken cancellationToken) =>
        (await invitations.GetLatestSentAsync(userId, cancellationToken))
            ?.WaitBeforeAnother(timeProvider.GetUtcNow().UtcDateTime) ?? TimeSpan.Zero;

    /// <summary>
    /// Guarda la invitación y le pide al canal que encole el mensaje; quien llama ya controló las reglas
    /// (<see cref="Check"/>). Toma antes el lock de invitaciones de la cuenta, que dura hasta que se confirma la
    /// invitación: la cola de WhatsApp lo pide antes de buscarla para dejarle el id de Meta, así que la encuentra aunque
    /// haya mandado el mensaje antes de que se confirme. Si la cola del canal no toma el mensaje, el canal deja la
    /// invitación guardada como no enviada: el alta no se deshace por un envío que falló, el admin la reenvía y el reenvío
    /// no espera, porque la espera se cuenta solo desde una invitación que salió (<see cref="WaitBeforeAnotherAsync"/>).
    /// </summary>
    public async Task SendAsync(UserAccount user, UserInvitationChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var adapter = FindAdapter(channel)
            ?? throw new InvalidOperationException($"There is no invitation channel for {channel}; Check rejects it first.");
        var sentBy = currentUser.UserId
            ?? throw new InvalidOperationException("An invitation is sent by an authenticated administrator.");
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var culture = UserCultures.Of(user);

        await invitations.LockAccountAsync(user.Id, cancellationToken);

        var invitation = UserInvitation.Send(user.Id, channel, sentBy, nowUtc, adapter.RecordsConsent);
        invitations.Add(invitation);

        adapter.Enqueue(user, invitation, culture);
    }

    // Uno por canal, o ninguno: dos del mismo canal serían un error de registro, y SingleOrDefault lo hace saltar.
    private IInvitationChannel? FindAdapter(UserInvitationChannel channel) =>
        channels.SingleOrDefault(adapter => adapter.Channel == channel);
}

/// <summary>
/// Dónde está cada dato de la invitación en el cuerpo de la petición: en el alta van dentro de <c>invitation</c>; en el
/// reenvío, sueltos. El nombre es siempre el de la cuenta.
/// </summary>
internal sealed record InvitationFields(string Channel, string Consent, string DisplayName)
{
    public static InvitationFields OfCreate { get; } = new("invitation.channel", "invitation.consent", "displayName");

    public static InvitationFields OfResend { get; } = new("channel", "consent", "displayName");
}
