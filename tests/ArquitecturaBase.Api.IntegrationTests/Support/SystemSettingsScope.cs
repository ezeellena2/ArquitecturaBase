using ArquitecturaBase.Application.Interfaces.Integrations.Caching;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// RC6-B y RC1-A: cambia el nombre, el remitente, el logo y los plazos de retención de la base (y descarta el caché de los
/// ajustes) mientras dure el scope, y los deja como estaban al salir, aunque el test los cambie con el endpoint. Los tests de la
/// colección corren en serie, así que no se pisan entre ellos.
/// </summary>
internal sealed class SystemSettingsScope(ApiFactory factory, SystemBranding branding, IdentityRetentionPeriods retention) : IAsyncDisposable
{
    public static async Task<SystemSettingsScope> ChangeAsync(ApiFactory factory, SystemBranding? branding = null,
        IdentityRetentionPeriods? retention = null)
    {
        var (previousBranding, previousRetention) = await ApplyAsync(factory, branding, retention);
        return new(factory, previousBranding, previousRetention);
    }

    public ValueTask DisposeAsync() => new(ApplyAsync(factory, branding, retention));

    private static Task<(SystemBranding Branding, IdentityRetentionPeriods Retention)> ApplyAsync(ApiFactory factory,
        SystemBranding? branding, IdentityRetentionPeriods? retention) =>
        factory.ExecuteScopeAsync(async services =>
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var dbContext = services.GetRequiredService<ApplicationDbContext>();
            var settings = await dbContext.SystemSettings.SingleAsync(cancellationToken);
            var previous = (new SystemBranding(settings.AppName, settings.EmailSenderName, settings.EmailLogoUrl),
                new IdentityRetentionPeriods(settings.LoginAuditRetentionMonths, settings.LoginCodeRetentionDays, settings.InvitationRetentionDays));

            if (branding is not null || retention is not null)
            {
                Assert.True(settings.Update(null, null, null, null, branding: branding, retention: retention).IsSuccess);
                await dbContext.SaveChangesAsync(cancellationToken);
                await services.GetRequiredService<ISystemSettingsCache>().InvalidateAsync(cancellationToken);
            }

            return previous;
        });
}
