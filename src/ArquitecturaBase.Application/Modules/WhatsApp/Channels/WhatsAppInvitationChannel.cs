using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Channels;

/// <summary>
/// La invitación de un administrador por WhatsApp (sección 6.6 del spec del ingreso con WhatsApp): la plantilla con
/// «Quiero entrar», que no lleva nada que sirva para entrar. Al tocarlo, el bot manda el enlace (fila 7 de la sección 8).
/// La usa <c>UserInvitationIssuer</c>, que guarda la invitación y toma antes el lock de invitaciones de la cuenta; este
/// canal pone las reglas de WhatsApp y encola el mensaje. No abre ni confirma transacciones.
/// </summary>
internal sealed partial class WhatsAppInvitationChannel(
    IWhatsAppSendQueue sendQueue,
    IWhatsAppAvailability whatsApp,
    IAppName appName,
    ILogger<WhatsAppInvitationChannel> logger) : IInvitationChannel
{
    public UserInvitationChannel Channel => UserInvitationChannel.WhatsApp;

    /// <summary>Quien la manda confirma que la persona aceptó recibir mensajes por WhatsApp.</summary>
    public bool RecordsConsent => true;

    /// <summary>
    /// Que WhatsApp esté configurado, un número, el consentimiento de la persona y su nombre, en ese orden, porque el
    /// front muestra el primer error. Sin WhatsApp no hay por dónde mandar la plantilla: aceptarla daría un éxito y una
    /// invitación fallida sin decir por qué, así que se rechaza como el ingreso, que ni ofrece la opción. El nombre hace
    /// falta porque la plantilla saluda con él y Meta no acepta un parámetro vacío.
    /// </summary>
    public Result Check(InvitationCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (!whatsApp.IsEnabled)
        {
            return FieldErrors.Validation(check.ChannelField, ValidationMessages.InvitationWhatsAppUnavailable);
        }

        if (!check.HasPhone)
        {
            return FieldErrors.Validation(check.ChannelField, ValidationMessages.InvitationPhoneRequired);
        }

        if (!check.Consent)
        {
            return FieldErrors.On(UserInvitationErrors.ConsentRequired, check.ConsentField);
        }

        return string.IsNullOrWhiteSpace(check.DisplayName)
            ? FieldErrors.On(UserInvitationErrors.NameRequired, check.DisplayNameField)
            : Result.Success();
    }

    /// <summary>
    /// Encola la plantilla de <paramref name="invitation"/>, ya agregada, en el idioma <paramref name="culture"/> de la
    /// cuenta. Si la cola no toma el mensaje, la invitación queda marcada como no enviada: el alta no se deshace por un
    /// envío que falló, y el admin lo ve y la reenvía.
    /// </summary>
    public void Enqueue(UserAccount user, UserInvitation invitation, string culture)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(invitation);

        var message = new WhatsAppInvitationMessage(
            PhoneNumber.Create(user.PhoneNumber).Value,
            user.Id,
            invitation.Id,
            culture,
            user.DisplayName!,
            appName.Value,
            BotButtons.WantToEnter);

        if (!sendQueue.TryEnqueue(message))
        {
            invitation.MarkSendFailed();
            LogNotQueued(logger);
        }
    }

    // Sin el número ni el nombre: solo que pasó.
    [LoggerMessage(Level = LogLevel.Warning, Message = "The WhatsApp queue did not take an invitation; it was recorded as not sent")]
    private static partial void LogNotQueued(ILogger logger);
}
