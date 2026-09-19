using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Application;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// La Api real contra un Postgres en contenedor, con un reloj controlable y las features de prueba
/// (entidad Widget y endpoints /test) que existen solo en este proyecto.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // La misma imagen que usa Aspire 13.5.4.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        // Sin migraciones en los tests: el esquema sale del modelo de TestDbContext.
        await ExecuteDbContextAsync(dbContext => dbContext.Database.EnsureCreatedAsync());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public async Task<T> ExecuteDbContextAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = Services.CreateAsyncScope();

        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing": no aplica migraciones ni mapea OpenAPI, que son solo de Development.
        builder.UseEnvironment("Testing");

        // La registración del DbContext de producción lee la cadena de conexión de acá.
        builder.UseSetting(
            $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}",
            _postgres.GetConnectionString());

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            // Mismas opciones que producción (Npgsql, interceptores y lo que se agregue después);
            // solo cambia el tipo de contexto, que suma la tabla de Widgets.
            services.Replace(ServiceDescriptor.Scoped<ApplicationDbContext>(serviceProvider =>
                new TestDbContext(serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>())));

            services.AddFeaturesFromAssembly(typeof(ApiFactory).Assembly);
            services.AddEndpoints(typeof(ApiFactory).Assembly);

            // Con un esquema registrado, WebApplication agrega UseAuthentication solo.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }
}
