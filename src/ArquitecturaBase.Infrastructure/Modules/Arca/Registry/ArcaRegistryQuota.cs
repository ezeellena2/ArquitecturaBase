using System.Collections.Concurrent;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Registry;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Registry;

/// <summary>
/// El cupo del padrón de un tercero en memoria, por host: guarda, por persona, la última vez que consultó cada CUIT y descarta lo
/// que pasó hace un minuto o más. Es un singleton; cada persona tiene su lista y su propio lock.
/// </summary>
internal sealed class ArcaRegistryQuota(IOptions<ArcaRegistryOptions> options, TimeProvider timeProvider) : IArcaRegistryQuota
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<Guid, Dictionary<string, DateTimeOffset>> recent = new();

    public bool Admit(Guid? operatorId, string cuit)
    {
        if (operatorId is not { } id) return true;
        var now = timeProvider.GetUtcNow();
        var lookups = recent.GetOrAdd(id, static _ => new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal));
        lock (lookups)
        {
            foreach (var stale in lookups.Where(lookup => now - lookup.Value >= Window).Select(lookup => lookup.Key).ToArray()) lookups.Remove(stale);
            if (!lookups.ContainsKey(cuit) && lookups.Count >= options.Value.LookupsPerMinute) return false;
            lookups[cuit] = now;
            return true;
        }
    }
}
