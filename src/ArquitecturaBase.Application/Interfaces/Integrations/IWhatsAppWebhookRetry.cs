using ArquitecturaBase.Application.Models.WhatsApp;

namespace ArquitecturaBase.Application.Interfaces.Integrations;

/// <summary>Retries a concurrent webhook conflict with a fresh persistence scope.</summary>
public interface IWhatsAppWebhookRetry
{
    Task RetryAsync(WhatsAppWebhookBatch batch, CancellationToken cancellationToken);
}
