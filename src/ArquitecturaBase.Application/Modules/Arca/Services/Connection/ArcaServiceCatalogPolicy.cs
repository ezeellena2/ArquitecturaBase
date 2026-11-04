using ArquitecturaBase.Application.Modules.Arca.Interfaces.Channels.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Connection;

/// <summary>
/// Los servicios que traen las familias instaladas, sin nombrar a ninguna (ADR 0025): cuáles hay, cuáles son
/// obligatorios, quién prueba cada uno elegido y si las familias elegidas tienen completa su configuración.
/// </summary>
internal sealed class ArcaServiceCatalogPolicy(IEnumerable<IArcaServiceDefinition> definitions)
{
    private readonly IArcaServiceDefinition[] families = [.. definitions];

    public IReadOnlyList<ArcaInstalledService> Installed => [.. families.SelectMany(family => family.Services)];

    public IReadOnlyCollection<ArcaServiceCode> InstalledCodes => [.. Installed.Select(service => service.Code)];

    public IReadOnlyCollection<ArcaServiceCode> RequiredCodes => [.. Installed.Where(service => service.Required).Select(service => service.Code)];

    /// <summary>Los servicios elegidos que siguen instalados, cada uno con la familia que lo prueba.</summary>
    public IEnumerable<(ArcaServiceCode Service, IArcaServiceDefinition Family)> Chosen(IReadOnlyCollection<string> enabled) =>
        families.SelectMany(family => family.Services
            .Where(service => enabled.Contains(service.Code.Value, StringComparer.Ordinal))
            .Select(service => (service.Code, family)));

    public async Task<bool> FamiliesCompleteAsync(Guid connectionId, IReadOnlyCollection<string> enabled, CancellationToken cancellationToken)
    {
        foreach (var family in families.Where(family => family.Services.Any(service => enabled.Contains(service.Code.Value, StringComparer.Ordinal))))
        {
            if (!await family.IsCompleteAsync(connectionId, cancellationToken)) return false;
        }

        return true;
    }
}
