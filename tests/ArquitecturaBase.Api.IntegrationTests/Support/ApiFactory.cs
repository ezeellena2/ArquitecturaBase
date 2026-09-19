using System.Globalization;
using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Application;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using OpenIddict.Validation.AspNetCore;
using Testcontainers.PostgreSql;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// La Api real contra un Postgres en contenedor, con un reloj controlable y las features de prueba
/// (entidad Widget y endpoints /test) que existen solo en este proyecto.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Clave HMAC de los tests: los bytes 0 a 31 en base64. Nunca se usa fuera de los tests.</summary>
    public const string TestHashKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    /// <summary>Recibe el rol Admin al crearse (Seed:AdminEmail).</summary>
    public const string AdminEmail = "admin@arquitecturabase.test";

    public const string WebRedirectUri = "https://localhost/auth/callback";
    public const string PostLogoutRedirectUri = "https://localhost/login";

    // La misma imagen que usa Aspire 13.5.4.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    public ApiFactory()
    {
        // OpenIddict exige HTTPS y la cookie de Identity es Secure. Las redirecciones se leen, no se siguen.
        ClientOptions.BaseAddress = new Uri("https://localhost");
        ClientOptions.AllowAutoRedirect = false;
    }

    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>Cadena de conexión a una base nueva y vacía en el mismo contenedor. EF la crea al migrar.</summary>
    public string NewDatabaseConnectionString(string prefix) =>
        new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = prefix + "_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8],
        }.ConnectionString;

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        // Sin migraciones en los tests: el esquema sale del modelo de TestDbContext.
        await ExecuteDbContextAsync(dbContext => dbContext.Database.EnsureCreatedAsync());

        // Los mismos datos base que en desarrollo: roles, permisos y, desde la Tarea 17, el cliente "web".
        await Services.SeedDatabaseAsync();
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

    public async Task<T> ExecuteScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = Services.CreateAsyncScope();

        return await action(scope.ServiceProvider);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing": no aplica migraciones ni mapea OpenAPI, que son solo de Development.
        builder.UseEnvironment("Testing");

        // La registración del DbContext de producción lee la cadena de conexión de acá.
        builder.UseSetting(
            $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}",
            _postgres.GetConnectionString());

        builder.UseSetting("Authentication:LoginCode:HashKey", TestHashKey);

        builder.UseSetting("Seed:AdminEmail", AdminEmail);

        builder.UseSetting("Authentication:Clients:Web:RedirectUris:0", WebRedirectUri);
        builder.UseSetting("Authentication:Clients:Web:PostLogoutRedirectUris:0", PostLogoutRedirectUri);

        // Los tests no envían emails de verdad; la Tarea 19 reemplaza IEmailSender por uno que los guarda en memoria.
        builder.UseSetting("Email:Delivery", "PickupDirectory");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            // Claves en memoria: las de Postgres se leen al arrancar el host, antes de que exista el esquema.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            // Mismas opciones que producción (Npgsql, interceptores y lo que se agregue después);
            // solo cambia el tipo de contexto, que suma la tabla de Widgets.
            services.Replace(ServiceDescriptor.Scoped<ApplicationDbContext>(serviceProvider =>
                new TestDbContext(serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>())));

            services.AddFeaturesFromAssembly(typeof(ApiFactory).Assembly);
            services.AddEndpoints(typeof(ApiFactory).Assembly);

            // Con el header X-Test-UserId, el usuario de prueba; sin él, la validación real de OpenIddict.
            services.AddAuthentication(TestAuthHandler.PolicySchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { })
                .AddPolicyScheme(TestAuthHandler.PolicySchemeName, TestAuthHandler.PolicySchemeName, options =>
                    options.ForwardDefaultSelector = context =>
                        context.Request.Headers.ContainsKey(TestAuthHandler.UserIdHeader)
                            ? TestAuthHandler.SchemeName
                            : OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        });
    }
}
