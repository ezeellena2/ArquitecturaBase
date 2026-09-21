using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Settings;

/// <summary>
/// El seed de los ajustes se prueba sobre bases vacías: la de la Api compartida ya tiene la fila creada y los
/// tests de los modos de registro la usan.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class SystemSettingsSeedTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seed_creates_a_single_row_with_the_configured_mode()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting($"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("settings"))
            .UseSetting("Registration:Mode", "Open"));
        await using var scope = api.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());

        try
        {
            await dbContext.Database.MigrateAsync(Ct);
            await api.Services.SeedDatabaseAsync(Ct);

            var settings = await dbContext.SystemSettings.AsNoTracking().SingleAsync(Ct);
            Assert.Equal(SystemSettings.SingletonId, settings.Id);
            Assert.Equal(RegistrationMode.Open, settings.RegistrationMode);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }

    [Fact]
    public async Task Without_configuration_the_system_starts_closed_and_seeding_again_does_not_overwrite_it()
    {
        // ApiFactory fija Registration:Mode = Open y WithWebHostBuilder hereda esa configuración. Acá se prueba el
        // valor por defecto, el de una instalación que no configura nada, así que hay que dejar la clave sin valor:
        // una fuente posterior la tapa y el binder la saltea. Con UseSetting quedaría en cadena vacía, que no es lo
        // mismo (el binder intenta parsearla como enum y falla).
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting($"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("settings"))
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Registration:Mode"] = null })));
        await using var scope = api.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());

        try
        {
            await dbContext.Database.MigrateAsync(Ct);
            await api.Services.SeedDatabaseAsync(Ct);

            var seeded = await dbContext.SystemSettings.SingleAsync(Ct);
            Assert.Equal(RegistrationMode.InviteOnly, seeded.RegistrationMode);

            // Lo que se cambió desde el panel: un despliegue posterior no lo puede pisar.
            seeded.SetRegistrationMode(RegistrationMode.Open);
            await dbContext.SaveChangesAsync(Ct);

            await api.Services.SeedDatabaseAsync(Ct);

            var current = await dbContext.SystemSettings.AsNoTracking().SingleAsync(Ct);
            Assert.Equal(RegistrationMode.Open, current.RegistrationMode);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }

    [Fact]
    public async Task The_shared_api_is_seeded_from_its_configuration()
    {
        var settings = await factory.ExecuteDbContextAsync(db => db.SystemSettings.AsNoTracking().SingleAsync(Ct));

        // El arnés fija Registration:Mode = Open (ApiFactory).
        Assert.Equal(SystemSettings.SingletonId, settings.Id);
        Assert.Equal(RegistrationMode.Open, settings.RegistrationMode);
    }
}
