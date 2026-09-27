using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Persistence;
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

    /// <summary>
    /// La fábrica del caché lee en su propio scope, con otra conexión: un cambio que quien lee todavía no confirmó no se
    /// cachea. Con la protección contra estampidas, la fábrica puede seguir sirviendo a otros pedidos después de que el
    /// que la arrancó terminó o se canceló. Sobre el contexto de ese pedido correría adentro de su transacción, con sus
    /// locks, y moriría con su scope.
    /// </summary>
    [Fact]
    public async Task The_cache_factory_reads_on_its_own_connection_and_never_caches_an_uncommitted_mode()
    {
        // Al salir del scope, la fila y el caché vuelven a como estaban; SetAsync ya dejó el caché vacío.
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<ISystemSettingsReader>();

        // Quien lee tiene una transacción abierta con un cambio sin confirmar, como un límite a mitad de camino.
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        var settings = await db.SystemSettings.SingleAsync(Ct);
        settings.SetRegistrationMode(RegistrationMode.Open);
        await db.SaveChangesAsync(Ct);

        var read = await reader.FindRegistrationModeAsync(Ct);
        await transaction.RollbackAsync(Ct);

        Assert.Equal(RegistrationMode.InviteOnly, read);
        Assert.Equal(RegistrationMode.InviteOnly, await ReadModeAsync());
    }

    private Task<RegistrationMode> ReadModeAsync() =>
        factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<ISystemSettingsReader>().FindRegistrationModeAsync(Ct));

    private Task UpdateRowAsync(RegistrationMode mode) =>
        factory.ExecuteDbContextAsync(async dbContext =>
        {
            var settings = await dbContext.SystemSettings.SingleAsync(Ct);
            settings.SetRegistrationMode(mode);
            await dbContext.SaveChangesAsync(Ct);
        });

    private Task InvalidateAsync() =>
        factory.ExecuteScopeAsync(services => services.GetRequiredService<ISystemSettingsReader>().InvalidateAsync(Ct));
}
