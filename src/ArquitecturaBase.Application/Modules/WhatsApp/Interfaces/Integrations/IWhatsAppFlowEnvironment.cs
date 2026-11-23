using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

public interface IWhatsAppFlowEnvironment
{
    bool IsConfigured { get; }

    /// <summary>
    /// How long a form sent by WhatsApp accepts its answer: «Tiempo para contestar un formulario» of Configuración › WhatsApp (RC3-A, 24 hours
    /// until it is saved), read on every send.
    /// </summary>
    Task<TimeSpan> FindSessionLifetimeAsync(CancellationToken cancellationToken);

    /// <summary>H33: the configured account, without asking Meta. Sending or proposing a form compares it with the published
    /// manifest; for a send, only the dispatch worker verifies with Meta that the number still belongs to the account.</summary>
    Result<WhatsAppFlowAccountContext> ConfiguredAccount();

    Task<Result<WhatsAppFlowAccountContext>> VerifyAccountAsync(CancellationToken cancellationToken);
}
