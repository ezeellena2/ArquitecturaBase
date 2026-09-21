using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// Cambia el modo de registro de la base (y descarta el caché del lector) mientras dure el scope, y lo deja como
/// estaba al salir. Los tests de la colección corren en serie, así que no se pisan entre ellos.
/// </summary>
internal sealed class RegistrationModeScope(ApiFactory factory, RegistrationMode previous) : IAsyncDisposable
{
    public static async Task<RegistrationModeScope> SetAsync(ApiFactory factory, RegistrationMode mode) =>
        new(factory, await ApplyAsync(factory, mode));

    public ValueTask DisposeAsync() => new(ApplyAsync(factory, previous));

    private static Task<RegistrationMode> ApplyAsync(ApiFactory factory, RegistrationMode mode) =>
        factory.ExecuteScopeAsync(async services =>
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var dbContext = services.GetRequiredService<ApplicationDbContext>();
            var settings = await dbContext.SystemSettings.SingleAsync(cancellationToken);
            var previousMode = settings.RegistrationMode;

            settings.SetRegistrationMode(mode);
            await dbContext.SaveChangesAsync(cancellationToken);
            await services.GetRequiredService<ISystemSettingsReader>().InvalidateAsync(cancellationToken);

            return previousMode;
        });
}
