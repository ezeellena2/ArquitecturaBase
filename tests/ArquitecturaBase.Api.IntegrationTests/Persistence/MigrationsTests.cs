using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// Usan el ApplicationDbContext de producción armado con las opciones registradas. El TestDbContext del arnés suma
/// la tabla de Widgets, que no es parte de las migraciones.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class MigrationsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Model_has_no_pending_changes()
    {
        var pending = await factory.ExecuteScopeAsync(services =>
        {
            using var dbContext = new ApplicationDbContext(services.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
            return Task.FromResult(dbContext.Database.HasPendingModelChanges());
        });

        Assert.False(pending);
    }

    [Fact]
    public async Task Migrations_create_the_schema_on_an_empty_database()
    {
        await using var emptyDatabase = factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("migrations")));
        await using var scope = emptyDatabase.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());

        try
        {
            await dbContext.Database.MigrateAsync(Ct);

            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync(Ct));
            Assert.False(await dbContext.LoginCodes.AnyAsync(Ct));
            Assert.False(await dbContext.Users.AnyAsync(Ct));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }
}
