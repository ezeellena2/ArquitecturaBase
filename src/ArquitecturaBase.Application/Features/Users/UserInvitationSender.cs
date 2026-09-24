using System.Globalization;
using ArquitecturaBase.Application.Abstractions.Branding;
using ArquitecturaBase.Application.Abstractions.Emails;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Features.WhatsApp.HandleInboundMessage;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Features.Users;

/// <summary>
/// Las invitaciones de un administrador (sección 6.6 del spec del ingreso con WhatsApp), en un solo lugar: las usan el
/// alta, que puede mandar una, y el reenvío. Ninguna lleva algo que sirva para entrar. Por correo, un botón a /login, y
/// la persona entra con el código de siempre. Por WhatsApp, la plantilla con «Quiero entrar»: al tocarlo, el bot le manda
/// el enlace (fila 7 de la sección 8), que nace recién ahí y dura 10 minutos. Las dos salen en el idioma de la cuenta.
/// </summary>
internal sealed partial class UserInvitationSender(
    IUserInvitationRepository invitations,
    IWhatsAppOutbox outbox,
    IWhatsAppAvailability whatsApp,
    IEmailQueue emailQueue,
    IEmailTemplateRenderer emailTemplates,
    IPublicOrigin publicOrigin,
    IAppName appName,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<UserInvitationSender> logger)
{
    /// <summary>La pantalla de ingreso del SPA, a la que lleva el botón del correo, como el "Ir a la web" del bot.</summary>
    private const string WebLoginPath = "login";

    /// <summary>
    /// Las reglas de la invitación, antes de tocar nada. Por correo, la cuenta necesita un correo; por WhatsApp, que
    /// WhatsApp esté configurado, un número, el consentimiento de la persona y su nombre, porque la plantilla la saluda
    /// con él y Meta no acepta un parámetro vacío. Sin WhatsApp no hay por dónde mandar la plantilla: aceptarla daría un
    /// éxito y una invitación fallida sin decir por qué, así que se rechaza como el ingreso, que ni ofrece la opción. Cada
    /// error va al campo que lo arregla (<paramref name="fields"/>).
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

        return channel switch
        {
            UserInvitationChannel.Email when !hasEmail =>
                FieldErrors.Validation(fields.Channel, ValidationMessages.InvitationEmailRequired),
            UserInvitationChannel.WhatsApp when !whatsApp.IsEnabled =>
                FieldErrors.Validation(fields.Channel, ValidationMessages.InvitationWhatsAppUnavailable),
            UserInvitationChannel.WhatsApp when !hasPhone =>
                FieldErrors.Validation(fields.Channel, ValidationMessages.InvitationPhoneRequired),
            UserInvitationChannel.WhatsApp when !consent =>
                FieldErrors.On(UserInvitationErrors.ConsentRequired, fields.Consent),
            UserInvitationChannel.WhatsApp when string.IsNullOrWhiteSpace(displayName) =>
                FieldErrors.On(UserInvitationErrors.NameRequired, fields.DisplayName),
            _ => Result.Success(),
        };
    }

    /// <summary>
    /// Guarda la invitación y encola el mensaje; quien llama ya controló las reglas (<see cref="Check"/>). Toma antes el
    /// lock de invitaciones de la cuenta, que dura hasta que se confirma la invitación: la cola de WhatsApp lo pide antes
    /// de buscarla para dejarle el id de Meta, así que la encuentra aunque haya mandado el mensaje antes de que se
    /// confirme. Si la cola de WhatsApp no toma el mensaje, la invitación queda guardada como no enviada: el alta no se
    /// deshace por un envío que falló, y el admin lo ve y la reenvía.
    /// </summary>
    public async Task SendAsync(UserAccount user, UserInvitationChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var sentBy = currentUser.UserId
            ?? throw new InvalidOperationException("An invitation is sent by an authenticated administrator.");
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var culture = UserCultures.Of(user);

        await invitations.LockAccountAsync(user.Id, cancellationToken);

        if (channel is UserInvitationChannel.Email)
        {
            invitations.Add(UserInvitation.ByEmail(user.Id, sentBy, nowUtc));

            await emailQueue.EnqueueAsync(
                emailTemplates.RenderInvitation(user.Email!, user.DisplayName, LoginUrl(), CultureInfo.GetCultureInfo(culture)),
                cancellationToken);

            return;
        }

        var invitation = UserInvitation.ByWhatsApp(user.Id, sentBy, nowUtc);
        invitations.Add(invitation);

        var message = new WhatsAppInvitationMessage(
            PhoneNumber.Create(user.PhoneNumber).Value,
            user.Id,
            invitation.Id,
            culture,
            user.DisplayName!,
            appName.Value,
            BotButtons.WantToEnter);

        if (!outbox.TryEnqueue(message))
        {
            invitation.MarkSendFailed();
            LogNotQueued(logger);
        }
    }

    private string LoginUrl()
    {
        var origin = publicOrigin.Value
            ?? throw new InvalidOperationException(
                "Authentication:Issuer must be set to the public origin of the web app to send invitations by email.");

        return new Uri(origin, WebLoginPath).AbsoluteUri;
    }

    // Sin el número ni el nombre: solo que pasó.
    [LoggerMessage(Level = LogLevel.Warning, Message = "The WhatsApp queue did not take an invitation; it was recorded as not sent")]
    private static partial void LogNotQueued(ILogger logger);
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
