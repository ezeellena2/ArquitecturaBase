using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ArquitecturaBase.Api.IntegrationTests;

/// <summary>
/// Los tests usan la misma registración del DbContext que producción: solo cambia el tipo de contexto
/// (que agrega la tabla de Widgets) y la cadena de conexión.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class DbContextRegistrationTests(ApiFactory factory)
{
    [Fact]
    public async Task Test_context_is_built_from_the_production_options()
    {
        var (contextType, optionsContextType, interceptorTypes) = await factory.ExecuteDbContextAsync(dbContext =>
        {
            var options = (DbContextOptions)dbContext.GetService<IDbContextOptions>();
            var interceptors = options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];

            return Task.FromResult((dbContext.GetType(), options.ContextType, interceptors.Select(i => i.GetType()).ToArray()));
        });

        Assert.Equal(typeof(TestDbContext), contextType);
        Assert.Equal(typeof(ApplicationDbContext), optionsContextType);
        Assert.Equal([typeof(SoftDeleteInterceptor), typeof(AuditableEntityInterceptor)], interceptorTypes);
    }
}
