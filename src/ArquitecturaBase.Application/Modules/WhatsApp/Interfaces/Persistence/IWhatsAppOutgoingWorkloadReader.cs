using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

/// <summary>H9: a snapshot of what waits in the durable outgoing, which the worker reads once a minute for the gauges.</summary>
public interface IWhatsAppOutgoingWorkloadReader
{
    Task<WhatsAppOutgoingWorkloadRow> FindAsync(CancellationToken cancellationToken);
}
