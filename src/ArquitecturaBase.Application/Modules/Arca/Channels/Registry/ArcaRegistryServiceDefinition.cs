using ArquitecturaBase.Application.Modules.Arca.Interfaces.Channels.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Registry;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Registry;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Services.Registry;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Channels.Registry;

/// <summary>
/// El padrón ante la conexión. La constancia de inscripción es obligatoria: su prueba consulta la CUIT de la conexión y
/// guarda los datos de la empresa, que la factura necesita. No tiene configuración propia.
/// </summary>
internal sealed class ArcaRegistryServiceDefinition(IArcaRegistryClient client, IArcaRegistryCompanyService companies) : IArcaServiceDefinition
{
    private static readonly ArcaServiceCode Constancia = ArcaServiceCode.Create("ws_sr_constancia_inscripcion")!.Value;

    // Una consulta: en modo delegación la hace la plataforma con su CUIT, sin que la empresa la delegue (ADR 0027).
    public IReadOnlyCollection<ArcaInstalledService> Services { get; } = [new(Constancia, "Registry", Required: true, Delegated: false)];

    public async Task<ArcaServiceCheckResponse> CheckAsync(ArcaServiceCode service, ArcaServiceContext context, CancellationToken cancellationToken)
    {
        var outcome = await context.Tickets.GetAsync(context.Certificate, service, cancellationToken);
        if (outcome is not ArcaTicketOutcome.Ready ticket) return ArcaServiceCheckResponse.FromTicket(service, outcome);

        var reply = await client.FindPersonAsync(context.Certificate, ticket, context.Certificate.Cuit, cancellationToken);
        if (reply is not { Status: ArcaServiceCheckStatus.Ready, Value: { } person })
            return ArcaServiceCheckResponse.Failed(service, reply.Status, reply.FailureCode, ticket.ExpiresAtUtc);
        if (!ArcaCompanyDataPolicy.HasData(person))
            return ArcaServiceCheckResponse.Failed(service, ArcaServiceCheckStatus.Failed, "registry.withoutData", ticket.ExpiresAtUtc);

        var saved = await companies.SaveAsync(context.Certificate.ConnectionId, person, cancellationToken);
        return saved.IsSuccess
            ? ArcaServiceCheckResponse.Passed(service, ticket.ExpiresAtUtc)
            : ArcaServiceCheckResponse.Failed(service, ArcaServiceCheckStatus.Failed, saved.Error.Code, ticket.ExpiresAtUtc);
    }

    public Task<bool> IsCompleteAsync(Guid connectionId, CancellationToken cancellationToken) => Task.FromResult(true);
}
