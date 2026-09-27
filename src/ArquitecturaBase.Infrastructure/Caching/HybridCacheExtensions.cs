using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Caching;

/// <summary>
/// La única forma de llenar HybridCache (lo verifica TransactionBoundaryTests): la fábrica lee en un scope propio, con su
/// contexto y su conexión, nunca con los de quien llama.
/// </summary>
internal static class HybridCacheExtensions
{
    /// <summary>
    /// Lo cacheado en <paramref name="key"/>, o lo que lee <paramref name="read"/> con un <typeparamref name="TReader"/> de
    /// un scope nuevo. Sobre el contexto de quien llama, la fábrica correría adentro de su límite: vería lo que todavía no
    /// se confirmó y lo cachearía. Con la protección contra estampidas, además, la fábrica sigue sirviendo a otros pedidos
    /// después de que el que la arrancó terminó o se canceló: le ocuparía la conexión que su rollback necesita para soltar
    /// los locks y moriría con su scope. El precio es que, con el caché frío, la fábrica pide una segunda conexión al pool
    /// mientras quien llama tiene la suya tomada por su límite. Si el pool se agotara con límites que esperan justo esta
    /// fábrica, ella esperaría el timeout de conexión y todos esos pedidos terminarían en un 500. Con una fábrica por clave
    /// y un caché de un minuto (los ajustes) o de una hora (los permisos de cada rol) es improbable; no sirve para una
    /// fábrica que corra por fila o por pedido.
    /// </summary>
    public static ValueTask<TValue> GetOrCreateInOwnScopeAsync<TReader, TState, TValue>(
        this HybridCache cache,
        string key,
        IServiceScopeFactory scopes,
        TState state,
        Func<TReader, TState, CancellationToken, Task<TValue>> read,
        HybridCacheEntryOptions options,
        CancellationToken cancellationToken)
        where TReader : notnull
    {
        ArgumentNullException.ThrowIfNull(cache);

        return cache.GetOrCreateAsync(
            key,
            (scopes, state, read),
            static async (factory, token) =>
            {
                await using var scope = factory.scopes.CreateAsyncScope();

                return await factory.read(scope.ServiceProvider.GetRequiredService<TReader>(), factory.state, token);
            },
            options,
            cancellationToken: cancellationToken);
    }

    /// <summary>Como la sobrecarga con estado, para una lectura que no necesita ninguno.</summary>
    public static ValueTask<TValue> GetOrCreateInOwnScopeAsync<TReader, TValue>(
        this HybridCache cache,
        string key,
        IServiceScopeFactory scopes,
        Func<TReader, CancellationToken, Task<TValue>> read,
        HybridCacheEntryOptions options,
        CancellationToken cancellationToken)
        where TReader : notnull =>
        cache.GetOrCreateInOwnScopeAsync<TReader, Func<TReader, CancellationToken, Task<TValue>>, TValue>(
            key, scopes, read, static (reader, readWith, token) => readWith(reader, token), options, cancellationToken);
}
