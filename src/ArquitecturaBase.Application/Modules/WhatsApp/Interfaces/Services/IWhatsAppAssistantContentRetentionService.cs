using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
public interface IWhatsAppAssistantContentRetentionService
{
    Task<Result<int>> ClearAsync(CancellationToken cancellationToken);
}
