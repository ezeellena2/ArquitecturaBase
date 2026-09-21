using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Settings;

[Collection(ApiTestGroup.Name)]
public sealed class SystemSettingsReaderTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_registration_mode_is_cached_until_it_is_invalidated()
    {
        // Al salir del scope, la fila y el caché vuelven a como estaban (el arnés arranca en Open).
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        Assert.Equal(RegistrationMode.InviteOnly, await ReadModeAsync());

        // Se cambia la fila por afuera del panel: el lector sigue devolviendo lo que tiene cacheado.
        await UpdateRowAsync(RegistrationMode.Open);
        Assert.Equal(RegistrationMode.InviteOnly, await ReadModeAsync());

        await InvalidateAsync();

        Assert.Equal(RegistrationMode.Open, await ReadModeAsync());
    }

    private Task<RegistrationMode> ReadModeAsync() =>
        factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<ISystemSettingsReader>().GetRegistrationModeAsync(Ct));

    private Task<bool> UpdateRowAsync(RegistrationMode mode) =>
        factory.ExecuteDbContextAsync(async dbContext =>
        {
            var settings = await dbContext.SystemSettings.SingleAsync(Ct);
            settings.SetRegistrationMode(mode);
            await dbContext.SaveChangesAsync(Ct);

            return true;
        });

    private Task<bool> InvalidateAsync() =>
        factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<ISystemSettingsReader>().InvalidateAsync(Ct);

            return true;
        });
}
