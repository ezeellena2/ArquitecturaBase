using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.LoginLinks;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;
using ArquitecturaBase.Application;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
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
using Testcontainers.Redis;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// La Api real contra un Postgres en contenedor, con un reloj controlable, los emails en memoria y las features de
/// prueba (entidad Widget y controllers /test) que existen solo en este proyecto. Un módulo opcional suma su
/// configuración y sus servicios con los ganchos de abajo, en la parte de esta clase que vive en su carpeta
/// <c>Modules/&lt;M&gt;</c> (con WhatsApp, sus claves, la cola en memoria y el cliente de Meta sin red): sin el módulo,
/// el compilador borra las llamadas.
/// </summary>
public sealed partial class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Recibe el rol Admin al crearse (Seed:AdminEmail).</summary>
    public const string AdminEmail = "admin@arquitecturabase.test";

    public const string WebRedirectUri = "https://localhost/auth/callback";
    public const string PostLogoutRedirectUri = "https://localhost/login";

    /// <summary>Clave HMAC de los tests: los bytes 0 a 31 en base64. Nunca se usa fuera de los tests.</summary>
    public const string TestHashKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    /// <summary>El index.html del SPA de mentira: lo devuelve el fallback en las rutas del navegador.</summary>
    public const string SpaMarker = "<!doctype html><title>spa</title>";

    /// <summary>La página del iframe de renovación silenciosa, que se sirve como archivo estático.</summary>
    public const string SilentRenewMarker = "<!doctype html><title>silent-renew</title>";

    /// <summary>Un asset con hash, como los que genera Vite: nunca tiene que caer en el index.html.</summary>
    public const string AssetMarker = "export const marker = 'asset';";

    // La misma imagen que usa Aspire 13.5.4.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8.6").Build();

    /// <summary>Raíz web de los tests: un SPA de mentira, para probar el fallback sin el build del front.</summary>
    private readonly string _webRoot = Directory.CreateTempSubdirectory("arquitecturabase-wwwroot").FullName;

    public ApiFactory()
    {
        // OpenIddict exige HTTPS y la cookie de Identity es Secure. Las redirecciones se leen, no se siguen.
        ClientOptions.BaseAddress = new Uri("https://localhost");
        ClientOptions.AllowAutoRedirect = false;
    }

    // Arranca en la hora real, truncada a segundos (Postgres guarda microsegundos y algunos tests comparan igualdad).
    // Con una fecha fija, el CookieContainer del cliente descartaría la cookie de sesión cuando la fecha real pase
    // su vencimiento de 30 días, y los tests que usan la sesión fallarían solos.
    public FakeTimeProvider Clock { get; } = new(StartOfTestClock());

    public CapturingEmailSender EmailSender { get; } = new();

    public string ConnectionString => _postgres.GetConnectionString();
    public string RedisConnectionString => _redis.GetConnectionString();

    /// <summary>Cadena de conexión a una base nueva y vacía en el mismo contenedor. EF la crea al migrar.</summary>
    public string NewDatabaseConnectionString(string prefix) =>
        new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = prefix + "_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8],
        }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // Sin migraciones en los tests: el esquema sale del modelo de TestDbContext.
        await ExecuteDbContextAsync(dbContext => dbContext.Database.EnsureCreatedAsync());

        // Los mismos datos base que en desarrollo: roles, permisos y el cliente "web".
        await Services.SeedDatabaseAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();

        Directory.Delete(_webRoot, recursive: true);
    }

    public async Task<T> ExecuteDbContextAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = Services.CreateAsyncScope();

        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    /// <summary>Como la sobrecarga genérica, para una acción que no devuelve nada.</summary>
    public async Task ExecuteDbContextAsync(Func<ApplicationDbContext, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = Services.CreateAsyncScope();

        await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public async Task<T> ExecuteScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = Services.CreateAsyncScope();

        return await action(scope.ServiceProvider);
    }

    /// <summary>Como la sobrecarga genérica, para una acción que no devuelve nada.</summary>
    public async Task ExecuteScopeAsync(Func<IServiceProvider, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = Services.CreateAsyncScope();

        await action(scope.ServiceProvider);
    }

    /// <summary>
    /// Prepara datos como lo haría un caso de uso: en un scope nuevo y dentro de un límite real
    /// (IUnitOfWork.ExecuteInTransactionAsync con OnSuccess). Las escrituras de cuentas y de roles exigen esa transacción,
    /// así que fuera de acá lanzan. Si la acción lanza, no queda nada y la excepción sale tal cual, salvo un 23505 que
    /// escapa, que sale como UniqueConstraintViolationException, igual que en producción.
    /// </summary>
    public async Task<T> InTransactionAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var value = default(T)!;

        await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            async _ =>
            {
                value = await action(services);

                return Result.Success();
            },
            CommitPolicy.OnSuccess,
            TestContext.Current.CancellationToken);

        return value;
    }

    /// <inheritdoc cref="InTransactionAsync{T}(Func{IServiceProvider, Task{T}})"/>
    public async Task InTransactionAsync(Func<IServiceProvider, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            async _ =>
            {
                await action(services);

                return Result.Success();
            },
            CommitPolicy.OnSuccess,
            TestContext.Current.CancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing": no aplica migraciones, no siembra al arrancar ni mapea OpenAPI (DatabaseInitialization y Program.cs).
        // El seed lo corre InitializeAsync de acá, después de crear el esquema.
        builder.UseEnvironment("Testing");

        // La registración del DbContext de producción lee la cadena de conexión de acá.
        builder.UseSetting($"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:cache", _redis.GetConnectionString());

        builder.UseSetting("Authentication:LoginCode:HashKey", TestHashKey);

        // Sin espera entre pedidos ni límite por email: muchos tests piden códigos seguidos para el mismo email.
        // LoginCodeEndpointsTests prueba esos límites con una Api aparte (WithWebHostBuilder).
        builder.UseSetting("Authentication:LoginCode:ResendCooldownSeconds", "0");
        builder.UseSetting("Authentication:LoginCode:MaxRequestsPerWindow", "100");

        // Lo mismo con los enlaces de ingreso: los tests emiten varios seguidos para la misma cuenta. Los límites se
        // prueban en LoginLinkIssuerTests.
        builder.UseSetting("Authentication:LoginLink:ResendCooldownSeconds", "0");
        builder.UseSetting("Authentication:LoginLink:MaxRequestsPerWindow", "100");

        // El origen público de los enlaces de ingreso y del botón de las invitaciones por correo. Es el mismo que
        // OpenIddict ya deducía del pedido (la dirección base del cliente de los tests), así que los tokens no cambian.
        builder.UseSetting("Authentication:Issuer", "https://localhost/");

        builder.UseSetting("Authentication:Clients:Web:RedirectUris:0", WebRedirectUri);
        builder.UseSetting("Authentication:Clients:Web:PostLogoutRedirectUris:0", PostLogoutRedirectUri);

        // Bajo TestServer no hay IP remota: todos los tests caen en la misma partición del rate limiter.
        builder.UseSetting("RateLimiting:LoginCodePermitLimit", "100000");
        builder.UseSetting("RateLimiting:LoginVerifyPermitLimit", "100000");

        // Sin validación de SMTP: los emails quedan en memoria (EmailSender).
        builder.UseSetting("Email:Delivery", "PickupDirectory");

        builder.UseSetting("Seed:AdminEmail", AdminEmail);

        // El arnés arranca abierto: casi todos los tests de las fases 1 a 3 ingresan con un correo nuevo y esperan
        // que la cuenta se cree sola. Los tests del modo de registro lo cambian con RegistrationModeScope, y el
        // valor por defecto (InviteOnly) se prueba sobre bases vacías en SystemSettingsSeedTests.
        builder.UseSetting("Registration:Mode", "Open");

        // El ClientId sale de appsettings.json; el secreto real nunca llega a los tests.
        builder.UseSetting("Authentication:Google:ClientSecret", "test-google-client-secret");

        // El SPA de mentira: el index.html que devuelve el fallback, la página del iframe de renovación y un asset
        // con hash. Alcanza para probar el hosting sin compilar el front.
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), SpaMarker);
        File.WriteAllText(Path.Combine(_webRoot, "silent-renew.html"), SilentRenewMarker);
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "assets", "main.js"), AssetMarker);
        builder.UseWebRoot(_webRoot);

        ConfigureModuleSettings(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);

            // Claves en memoria: las de Postgres se leen al arrancar el host, antes de que exista el esquema.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            // Mismas opciones que producción (Npgsql, interceptores, OpenIddict); solo cambia el tipo de contexto,
            // que suma la tabla de Widgets.
            services.Replace(ServiceDescriptor.Scoped<ApplicationDbContext>(serviceProvider =>
                new TestDbContext(serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>())));

            services.AddApplicationValidatorsFromAssembly(typeof(ApiFactory).Assembly);
            services.AddScoped<IWidgetTestService, WidgetTestService>();
            services.AddScoped<ILoginLinkTestService, LoginLinkTestService>();
            services.AddControllers().ConfigureApplicationPartManager(parts =>
                parts.ApplicationParts.Add(new TestControllerApplicationPart()));

            // Con el header X-Test-UserId, el usuario de prueba; sin él, la validación real de OpenIddict.
            services.AddAuthentication(TestAuthHandler.PolicySchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { })
                .AddPolicyScheme(TestAuthHandler.PolicySchemeName, TestAuthHandler.PolicySchemeName, options =>
                    options.ForwardDefaultSelector = context =>
                        context.Request.Headers.ContainsKey(TestAuthHandler.UserIdHeader)
                            ? TestAuthHandler.SchemeName
                            : OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

            ConfigureModuleServices(services);
        });
    }

    /// <summary>La configuración de los tests para cada módulo (sus claves de appsettings).</summary>
    static partial void ConfigureModuleSettings(IWebHostBuilder builder);

    /// <summary>Los reemplazos de los tests para cada módulo (sus colas y clientes sin red).</summary>
    partial void ConfigureModuleServices(IServiceCollection services);

    private static DateTimeOffset StartOfTestClock()
    {
        var now = TimeProvider.System.GetUtcNow();

        return new DateTimeOffset(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }
}
