using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

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
