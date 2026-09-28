using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Integrations.WhatsApp;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.WhatsApp;

/// <summary>
/// La invitación de un administrador por WhatsApp (sección 6.6 del spec del ingreso con WhatsApp): la plantilla con
/// «Quiero entrar», que no lleva nada que sirva para entrar. Al tocarlo, el bot manda el enlace (fila 7 de la sección 8).
/// La usa <see cref="Users.UserInvitationIssuer"/>, que guarda la invitación y toma antes el lock de invitaciones de la
/// cuenta; esta pieza solo dice si WhatsApp está configurado y encola el mensaje. No abre ni confirma transacciones.
/// </summary>
internal sealed partial class WhatsAppInvitationIssuer(
    IWhatsAppSendQueue sendQueue,
    IWhatsAppAvailability whatsApp,
    IAppName appName,
    ILogger<WhatsAppInvitationIssuer> logger)
{
    /// <summary>Si WhatsApp está configurado: sin él no hay por dónde mandar la plantilla.</summary>
    public bool IsEnabled => whatsApp.IsEnabled;

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
