using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

public interface IWhatsAppFlowEnvironment
{
    bool IsConfigured { get; }
    TimeSpan SessionLifetime { get; }
    Task<Result<WhatsAppFlowAccountContext>> VerifyAccountAsync(CancellationToken cancellationToken);
}
