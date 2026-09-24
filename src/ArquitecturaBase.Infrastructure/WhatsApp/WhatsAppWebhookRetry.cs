using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.WhatsApp;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>Resolves persistence again after the original transaction was rolled back.</summary>
internal sealed class WhatsAppWebhookRetry(IServiceScopeFactory scopes) : IWhatsAppWebhookRetry
{
    public async Task RetryAsync(WhatsAppWebhookBatch batch, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IWhatsAppWebhookPersistence>()
            .PersistAsync(batch, cancellationToken);
    }
}
