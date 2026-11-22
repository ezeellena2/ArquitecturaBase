using System.Globalization;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Channels;

/// <summary>
/// La invitación por correo, el canal que trae el núcleo: un botón a /login, y la persona entra con el código de
/// siempre. Pide solo que la cuenta tenga correo y no guarda consentimiento. AddApplication lo registra con
/// TryAddEnumerable, al lado de los canales de los módulos. No abre ni confirma transacciones. El correo lleva su
/// invitación: el worker de la cola anota si salió, si no o si no se sabe (<see cref="EmailInvitationDeliveryStatusSource"/>).
/// </summary>
internal sealed partial class EmailInvitationChannel(
    IEmailQueue emailQueue,
    IEmailTemplateRenderer emailTemplates,
    IPublicOrigin publicOrigin,
    ILogger<EmailInvitationChannel> logger) : IInvitationChannel
{
    /// <summary>La pantalla de ingreso del SPA, a la que lleva el botón del correo, como el "Ir a la web" del bot.</summary>
    private const string WebLoginPath = "login";

    public UserInvitationChannel Channel => UserInvitationChannel.Email;

    public bool RecordsConsent => false;

    public Result Check(InvitationCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        return check.HasEmail
            ? Result.Success()
            : FieldErrors.Validation(check.ChannelField, ValidationMessages.InvitationEmailRequired);
    }

    public async Task EnqueueAsync(UserAccount user, UserInvitation invitation, string culture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(invitation);

        // N5: the email carries its invitation, so the worker records whether it went out, did not or may have arrived.
        var rendered = await emailTemplates.RenderInvitationAsync(user.Email!, user.DisplayName, LoginUrl(), CultureInfo.GetCultureInfo(culture),
            cancellationToken);
        var email = rendered with { Invitation = new EmailInvitationReference(user.Id, invitation.Id) };

        if (!emailQueue.TryEnqueue(email))
        {
            invitation.MarkSendFailed();
            LogEmailNotQueued(logger);
        }
    }

    private string LoginUrl()
    {
        var origin = publicOrigin.Value
            ?? throw new InvalidOperationException(
                "Authentication:Issuer must be set to the public origin of the web app to send invitations by email.");

        return new Uri(origin, WebLoginPath).AbsoluteUri;
    }

    // Sin el correo ni el nombre: solo que pasó.
    [LoggerMessage(Level = LogLevel.Warning, Message = "The email queue did not take an invitation; it was recorded as not sent")]
    private static partial void LogEmailNotQueued(ILogger logger);
}
