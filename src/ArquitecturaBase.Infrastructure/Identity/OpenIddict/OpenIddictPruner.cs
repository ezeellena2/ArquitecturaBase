using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;

namespace ArquitecturaBase.Infrastructure.Identity.OpenIddict;

/// <summary>
/// N16: la poda que trae OpenIddict, de a tramos de un día de tokens. Sus stores de EF borran con su propio
/// <c>DELETE … LIMIT</c> sobre el contexto scoped, así que cada tramo corre en la transacción del llamador, bajo el lock de
/// la retención: un apagado a mitad no deshace los tramos anteriores, y otra copia no encuentra el lock tomado más que un
/// tramo.
/// </summary>
internal sealed class OpenIddictPruner(
    ApplicationDbContext dbContext,
    IOpenIddictTokenManager tokens,
    IOpenIddictAuthorizationManager authorizations) : IOpenIddictPruner
{
    /// <summary>Cuánto avanza el umbral en cada tramo, desde el token más viejo que queda por mirar.</summary>
    internal static readonly TimeSpan TrancheLength = TimeSpan.FromDays(1);

    public async Task<OpenIddictPruneTranche> PruneTrancheAsync(
        DateTime expiredBeforeUtc, DateTime? fromUtc, CancellationToken cancellationToken)
    {
        // OpenIddict poda por la fecha de creación lo que ya no sirve (vencido, revocado o canjeado). Corriendo el umbral lo
        // que dura el token más largo, todo lo que poda venció antes de expiredBeforeUtc: un refresh token canjeado sigue
        // hasta entonces y su reuso todavía revoca la cadena. Lo de vida corta queda un poco más; nunca menos.
        var finalThreshold = expiredBeforeUtc - AuthServerDefaults.RefreshTokenLifetime;

        // El tramo va del token más viejo desde fromUtc a un día después, sin pasar el umbral final. El próximo sigue desde
        // ahí aunque este no haya podado ese token (uno que todavía sirve): así la corrida siempre avanza, y los días sin
        // tokens no cuestan un tramo.
        var candidates = dbContext.Set<OpenIddictEntityFrameworkCoreToken<Guid>>()
            .Where(token => token.CreationDate < finalThreshold);
        if (fromUtc is { } from) candidates = candidates.Where(token => token.CreationDate >= from);

        var oldest = await candidates.MinAsync(token => token.CreationDate, cancellationToken);
        var threshold = oldest is { } start && start + TrancheLength < finalThreshold ? start + TrancheLength : finalThreshold;
        var offset = new DateTimeOffset(DateTime.SpecifyKind(threshold, DateTimeKind.Utc), TimeSpan.Zero);

        var prunedTokens = await tokens.PruneAsync(offset, cancellationToken);

        // Después de los tokens: una autorización se poda cuando ya no le queda ninguno. Su umbral es el mismo, así que el
        // primer tramo ya alcanza a las autorizaciones sin tokens más viejas que el token más viejo.
        var prunedAuthorizations = await authorizations.PruneAsync(offset, cancellationToken);

        return new OpenIddictPruneTranche(prunedTokens, prunedAuthorizations, threshold < finalThreshold ? threshold : null);
    }
}
