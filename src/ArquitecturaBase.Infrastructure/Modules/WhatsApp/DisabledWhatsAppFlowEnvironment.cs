using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

internal sealed class DisabledWhatsAppFlowEnvironment : IWhatsAppFlowEnvironment
{
    public bool IsConfigured => false;
    public Task<Result<WhatsAppFlowAccountContext>> VerifyAccountAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Result<WhatsAppFlowAccountContext>>(FlowErrors.AccountUnavailable);
}
