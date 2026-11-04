using ArquitecturaBase.Application.Modules.Arca.Interfaces.Channels.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;

/// <summary>
/// La facturación ante la conexión. WSFEv1 es opcional: su prueba pide el ticket, que dice si el certificado está
/// autorizado, y llama a <c>FEDummy</c>. Está completa cuando la empresa eligió su punto de venta.
/// </summary>
internal sealed class ArcaInvoicingServiceDefinition(IArcaInvoicingClient client, IArcaInvoicingReader reader) : IArcaServiceDefinition
{
    private static readonly ArcaServiceCode Wsfe = ArcaServiceCode.Create("wsfe")!.Value;

    public IReadOnlyCollection<ArcaInstalledService> Services { get; } = [new(Wsfe, "Invoicing", Required: false)];

    public async Task<ArcaServiceCheckResponse> CheckAsync(ArcaServiceCode service, ArcaServiceContext context, CancellationToken cancellationToken)
    {
        var outcome = await context.Tickets.GetAsync(context.Certificate, service, cancellationToken);
        if (outcome is not ArcaTicketOutcome.Ready ticket) return ArcaServiceCheckResponse.FromTicket(service, outcome);

        var reply = await client.CheckServerAsync(context.Certificate.Environment, cancellationToken);
        if (reply.Status != ArcaServiceCheckStatus.Ready)
            return ArcaServiceCheckResponse.Failed(service, reply.Status, reply.FailureCode, ticket.ExpiresAtUtc);
        return reply.Value
            ? ArcaServiceCheckResponse.Passed(service, ticket.ExpiresAtUtc)
            : ArcaServiceCheckResponse.Failed(service, ArcaServiceCheckStatus.Unavailable, null, ticket.ExpiresAtUtc);
    }

    public async Task<bool> IsCompleteAsync(Guid connectionId, CancellationToken cancellationToken) =>
        (await reader.FindSettingsAsync(connectionId, cancellationToken))?.PointOfSale is not null;
}
