using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Channels.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Registry;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Services.Registry;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Modules.Arca.Channels.Registry;

/// <summary>
/// El padrón presenta un tercero a las familias que emiten: la constancia de inscripción de su CUIT, consultada en nombre de quien
/// firma. En delegación la consulta la plataforma con su CUIT, sin que la empresa delegue la constancia (DG4). Antes de ARCA, el
/// cupo de CUIT distintas de la persona que pregunta; después, la caché corta, así lo que se repite no vuelve a ARCA.
/// </summary>
internal sealed class ArcaRegistryPartySource(ArcaServiceSessionIssuer access, IArcaRegistryCache registry, IArcaRegistryQuota quota,
    ICurrentUser currentUser) : IArcaPartySource
{
    private static readonly ArcaServiceCode Constancia = ArcaServiceCode.Create("ws_sr_constancia_inscripcion")!.Value;

    public async Task<Result<ArcaParty>> FindAsync(string taxId, CancellationToken cancellationToken)
    {
        if (CuitNumber.Normalize(taxId) is not { } cuit) return ArcaRegistryErrors.CuitInvalid;
        if (!quota.Admit(currentUser.UserId, cuit)) return ArcaRegistryErrors.TooManyLookups;
        var session = await access.OpenAsSignerAsync(Constancia, cancellationToken);
        if (session.IsFailure) return session.Error;
        var reply = await registry.FindPersonAsync(session.Value, cuit, cancellationToken);
        // El padrón contesta con una falla una CUIT que no conoce.
        if (reply.FailureCode == "registry.fault") return ArcaRegistryErrors.PersonNotFound;
        if (reply is not { Status: ArcaServiceCheckStatus.Ready, Value: { } person }) return ArcaServiceSessionIssuer.Failure(reply.Status);
        if (!ArcaCompanyDataPolicy.HasData(person)) return ArcaRegistryErrors.PersonNotFound;
        return new ArcaParty(cuit, ArcaCompanyDataPolicy.LegalName(person), ArcaCompanyDataPolicy.ReceiverVatCondition(person),
            ArcaCompanyDataPolicy.FiscalAddress(person) is { Length: > 0 } address ? address : null);
    }
}
