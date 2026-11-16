using System.Globalization;
using ArquitecturaBase.Domain.Authentication;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// Enlaces de ingreso pendientes, como los que dejó el bot antes de que se retiraran (B4, 2026-11-16): nada los emite ni
/// los canjea, pero los cambios del número y del acceso de la cuenta todavía los invalidan. Se guardan directo en la base.
/// </summary>
internal static class TestLoginLinks
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Guarda un enlace activo de la cuenta, emitido en la hora del reloj de los tests, y devuelve su Id.</summary>
    public static Task<Guid> AddPendingAsync(ApiFactory factory, Guid userId) =>
        factory.ExecuteDbContextAsync(async db =>
        {
            var link = LoginLink.Issue(
                userId, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture), factory.Clock.GetUtcNow().UtcDateTime);
            db.LoginLinks.Add(link);
            await db.SaveChangesAsync(Ct);

            return link.Id;
        });

    /// <summary>Si el enlace todavía sirve en la hora del reloj de los tests: ni consumido, ni invalidado, ni vencido.</summary>
    public static async Task<bool> IsActiveAsync(ApiFactory factory, Guid linkId)
    {
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
        var link = await factory.ExecuteDbContextAsync(db => db.LoginLinks.AsNoTracking().SingleAsync(link => link.Id == linkId, Ct));

        return link.IsActive(nowUtc);
    }
}
