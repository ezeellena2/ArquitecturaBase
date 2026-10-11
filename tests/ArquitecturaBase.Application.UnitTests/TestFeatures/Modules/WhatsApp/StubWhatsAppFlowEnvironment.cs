using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class StubWhatsAppFlowEnvironment : IWhatsAppFlowEnvironment
{
    public bool IsConfigured => false;
    public TimeSpan SessionLifetime => TimeSpan.FromHours(24);
    public Task<Result<WhatsAppFlowAccountContext>> VerifyAccountAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Result<WhatsAppFlowAccountContext>>(FlowErrors.AccountUnavailable);
}
