using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

public interface IWhatsAppMediaClient
{
    /// <summary>
    /// Up to <paramref name="maximumBytes"/>, never more than <see cref="WhatsAppMediaContent.MaximumBytes"/>: a larger size declared
    /// by Meta is TooLarge before downloading, and so are larger bytes.
    /// </summary>
    Task<Result<WhatsAppMediaContent>> DownloadAsync(string mediaId, string contentType, string sha256, long maximumBytes, CancellationToken cancellationToken);
}
