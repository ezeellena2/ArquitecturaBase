using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>Cómo está el chat de WhatsApp del número de la sesión, que es lo que decide si le llegan los avisos.</summary>
public interface IProfileWhatsAppChatService
{
    Task<Result<ProfileWhatsAppChatResponse>> GetChatAsync(CancellationToken cancellationToken);
}
