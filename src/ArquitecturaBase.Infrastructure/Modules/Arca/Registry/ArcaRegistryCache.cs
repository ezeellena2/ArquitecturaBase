using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Registry;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Registry;
using ArquitecturaBase.Infrastructure.Caching;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Registry;

/// <summary>
/// La constancia de un tercero en Redis, diez minutos, con la conexión y el SHA-256 de la CUIT como clave: la CUIT no queda
/// escrita en la clave y una conexión nueva empieza de cero. No tiene descarte, porque nada de la app cambia el padrón. La lectura
/// corre en su propio scope con el cliente de la constancia; el estado lleva la sesión (el certificado, con la clave cifrada, y el
/// ticket), que son valores. Sólo se guarda una respuesta lista con datos.
/// </summary>
internal sealed class ArcaRegistryCache(RedisCache cache) : IArcaRegistryCache
{
    private static readonly TimeSpan Expiration = TimeSpan.FromMinutes(10);

    public Task<ArcaServiceReply<ArcaRegistryPerson>> FindPersonAsync(ArcaServiceSession session, string cuit, CancellationToken cancellationToken) =>
        cache.GetOrCreateInOwnScopeAsync<IArcaRegistryClient, (ArcaServiceSession Session, string Cuit), ArcaServiceReply<ArcaRegistryPerson>>(
            Key(session.Certificate.ConnectionId, cuit), (session, cuit),
            static (client, state, ct) => client.FindPersonAsync(state.Session.Certificate, state.Session.Ticket, state.Cuit, ct),
            Expiration, static reply => reply is { Status: ArcaServiceCheckStatus.Ready, Value: not null }, cancellationToken);

    private static RedisCacheKey Key(Guid connectionId, string cuit) =>
        new($"arca:registry:person:{connectionId:N}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(cuit)))}");
}
