using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

// Without the transport nothing is sent, but the time to answer a form is still the one of Configuración › WhatsApp (RC3-A), never a
// fixed one.
internal sealed class DisabledWhatsAppFlowEnvironment(IWhatsAppSettingsReader settings) : IWhatsAppFlowEnvironment
{
    public bool IsConfigured => false;
    public async Task<TimeSpan> FindSessionLifetimeAsync(CancellationToken cancellationToken) =>
        TimeSpan.FromHours((await settings.FindAsync(cancellationToken)).FlowSessionLifetimeHours);
    public Result<WhatsAppFlowAccountContext> ConfiguredAccount() => FlowErrors.AccountUnavailable;
    public Task<Result<WhatsAppFlowAccountContext>> VerifyAccountAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Result<WhatsAppFlowAccountContext>>(FlowErrors.AccountUnavailable);
}
