using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

internal interface IWhatsAppMediaUploadClient
{
    Task<string?> UploadAsync(WhatsAppOutgoingDocument document, Stream content, CancellationToken cancellationToken);
}
