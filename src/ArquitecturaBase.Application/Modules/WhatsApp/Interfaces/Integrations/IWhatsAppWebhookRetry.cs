
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

/// <summary>Retries a concurrent webhook conflict with a fresh persistence scope.</summary>
public interface IWhatsAppWebhookRetry
{
    Task RetryAsync(WhatsAppWebhookBatch batch, CancellationToken cancellationToken);
}
