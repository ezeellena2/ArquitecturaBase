using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Caching;

[Collection(ApiTestGroup.Name)]
public sealed class RedisSeedTests(ApiFactory factory)
{
    [Fact]
    public async Task Seed_invalidates_a_preexisting_default_cached_before_the_row_was_created()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("cache_seed"))
            .UseSetting("Registration:Mode", "Open"));
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        try
        {
            await db.Database.EnsureCreatedAsync(ct);
            var reader = scope.ServiceProvider.GetRequiredService<ISystemSettingsReader>();
            Assert.Equal(RegistrationMode.InviteOnly, await reader.FindRegistrationModeAsync(ct));

            await api.Services.SeedDatabaseAsync(ct);

            Assert.Equal(RegistrationMode.Open, await reader.FindRegistrationModeAsync(ct));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(ct);
        }
    }
}
