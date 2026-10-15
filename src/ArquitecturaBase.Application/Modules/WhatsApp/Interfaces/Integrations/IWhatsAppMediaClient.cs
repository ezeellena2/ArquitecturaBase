using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

public interface IWhatsAppMediaClient
{
    Task<Result<WhatsAppMediaContent>> DownloadAsync(string mediaId, string contentType, string sha256, CancellationToken cancellationToken);
}
