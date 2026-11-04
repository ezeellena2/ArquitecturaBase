using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// El chat del número de la cuenta, para Mi perfil (tablero PF2). Sólo lee, sin abrir transacción, y vale lo mismo que al
/// mandar un aviso (<see cref="WhatsAppFormActionPolicy"/>): el chat tiene que estar vinculado a la cuenta y ser del
/// número confirmado que tiene hoy, y los avisos salen dentro de la ventana de 24 horas desde el último mensaje de la
/// persona. Un chat que existe sin vincular se arregla confirmando el número otra vez: un mensaje nunca vincula.
/// </summary>
internal sealed class ProfileWhatsAppChatService(
    ICurrentUser currentUser,
    IUserReader users,
    IWhatsAppContactReader contacts,
    TimeProvider timeProvider) : IProfileWhatsAppChatService
{
    public async Task<Result<ProfileWhatsAppChatResponse>> GetChatAsync(CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await users.FindByIdAsync(userId, cancellationToken)
            : null;
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (user.PhoneNumber is not { } phone)
        {
            return new ProfileWhatsAppChatResponse(ProfileWhatsAppChatState.NoChat, NoticesUntilUtc: null);
        }

        // WhatsApp manda el número sin el "+", y así se guarda en el contacto.
        var waId = phone[1..];
        var chat = await contacts.FindProfileChatAsync(user.Id, waId, cancellationToken);
        if (user.PhoneNumberConfirmed && chat.LinkedWaId == waId && chat.LinkedLastInboundAtUtc is { } lastMessage)
        {
            var noticesOpen = WhatsAppChatService.IsReplyWindowOpen(lastMessage, timeProvider.GetUtcNow().UtcDateTime);

            return new ProfileWhatsAppChatResponse(ProfileWhatsAppChatState.Linked, noticesOpen ? lastMessage.AddHours(24) : null);
        }

        return new ProfileWhatsAppChatResponse(
            chat.NumberHasChat ? ProfileWhatsAppChatState.NotLinked : ProfileWhatsAppChatState.NoChat, NoticesUntilUtc: null);
    }
}
