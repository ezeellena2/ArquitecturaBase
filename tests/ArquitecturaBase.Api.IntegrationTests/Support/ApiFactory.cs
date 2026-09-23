using System.Globalization;
using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Application;
using ArquitecturaBase.Application.Abstractions.Emails;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using ArquitecturaBase.Infrastructure.WhatsApp;
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
/// La Api real contra un Postgres en contenedor, con un reloj controlable, los emails y los mensajes de WhatsApp en
/// memoria y las features de prueba (entidad Widget y endpoints /test) que existen solo en este proyecto.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
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

    /// <summary>El número de WhatsApp de la Api de los tests: los webhooks de otro número se ignoran.</summary>
    public const string WhatsAppPhoneNumberId = "100000000000001";

    /// <summary>El secreto de la app de Meta de los tests, con el que se firman los webhooks. Inventado.</summary>
    public const string WhatsAppAppSecret = "test-app-secret-9f8e7d6c";

    /// <summary>La palabra de verificación del webhook de los tests. Inventada.</summary>
    public const string WhatsAppVerifyToken = "test-verify-token-ab12";

    // La misma imagen que usa Aspire 13.5.4.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();

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

    /// <summary>Los mensajes de WhatsApp que encolaron los casos de uso: nada sale hacia Meta.</summary>
    public CapturingWhatsAppOutbox WhatsApp { get; } = new();

    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>Cadena de conexión a una base nueva y vacía en el mismo contenedor. EF la crea al migrar.</summary>
    public string NewDatabaseConnectionString(string prefix) =>
        new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = prefix + "_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8],
        }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        // Sin migraciones en los tests: el esquema sale del modelo de TestDbContext.
        await ExecuteDbContextAsync(dbContext => dbContext.Database.EnsureCreatedAsync());

        // Los mismos datos base que en desarrollo: roles, permisos y el cliente "web".
        await Services.SeedDatabaseAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();

        Directory.Delete(_webRoot, recursive: true);
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
        builder.UseSetting($"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", _postgres.GetConnectionString());

        builder.UseSetting("Authentication:LoginCode:HashKey", TestHashKey);

        // Sin espera entre pedidos ni límite por email: muchos tests piden códigos seguidos para el mismo email.
        // LoginCodeEndpointsTests prueba esos límites con una Api aparte (WithWebHostBuilder).
        builder.UseSetting("Authentication:LoginCode:ResendCooldownSeconds", "0");
        builder.UseSetting("Authentication:LoginCode:MaxRequestsPerWindow", "100");

        // Lo mismo con los enlaces de ingreso: los tests emiten varios seguidos para la misma cuenta. Los límites se
        // prueban en LoginLinkIssuerTests.
        builder.UseSetting("Authentication:LoginLink:ResendCooldownSeconds", "0");
        builder.UseSetting("Authentication:LoginLink:MaxRequestsPerWindow", "100");

        // El origen público de los enlaces del bot. Es el mismo que OpenIddict ya deducía del pedido (la dirección
        // base del cliente de los tests), así que los tokens no cambian.
        builder.UseSetting("Authentication:Issuer", "https://localhost/");

        builder.UseSetting("Authentication:Clients:Web:RedirectUris:0", WebRedirectUri);
        builder.UseSetting("Authentication:Clients:Web:PostLogoutRedirectUris:0", PostLogoutRedirectUri);

        // Bajo TestServer no hay IP remota: todos los tests caen en la misma partición del rate limiter.
        builder.UseSetting("RateLimiting:LoginCodePermitLimit", "100000");
        builder.UseSetting("RateLimiting:LoginVerifyPermitLimit", "100000");
        builder.UseSetting("RateLimiting:WhatsAppWebhookPermitLimit", "100000");

        // Sin validación de SMTP: los emails quedan en memoria (EmailSender).
        builder.UseSetting("Email:Delivery", "PickupDirectory");

        builder.UseSetting("Seed:AdminEmail", AdminEmail);

        // El arnés arranca abierto: casi todos los tests de las fases 1 a 3 ingresan con un correo nuevo y esperan
        // que la cuenta se cree sola. Los tests del modo de registro lo cambian con RegistrationModeScope, y el
        // valor por defecto (InviteOnly) se prueba sobre bases vacías en SystemSettingsSeedTests.
        builder.UseSetting("Registration:Mode", "Open");

        // El ClientId sale de appsettings.json; el secreto real nunca llega a los tests.
        builder.UseSetting("Authentication:Google:ClientSecret", "test-google-client-secret");

        // WhatsApp prendido, con valores inventados: los mensajes quedan en memoria (WhatsApp) y el cliente de Meta
        // no tiene salida a internet. WhatsAppRegistrationTests prueba la Api con WhatsApp apagado.
        builder.UseSetting("WhatsApp:PhoneNumberId", WhatsAppPhoneNumberId);
        builder.UseSetting("WhatsApp:AccessToken", "test-access-token");

        // Con los dos secretos del webhook, así que el webhook también está prendido: los tests firman los webhooks
        // con el secreto de la app (MetaWebhook.Sign).
        builder.UseSetting("WhatsApp:AppSecret", WhatsAppAppSecret);
        builder.UseSetting("WhatsApp:VerifyToken", WhatsAppVerifyToken);

        // El tope diario es global y todos los tests comparten la base y el reloj: con el valor real, sumar tests que
        // mandan códigos por WhatsApp terminaría en 429 intermitentes. WhatsAppLoginCodeTests lo prueba con una Api
        // aparte (WithWebHostBuilder) y un tope chico.
        builder.UseSetting("WhatsApp:DailyAuthCodeLimit", "100000");

        // El SPA de mentira: el index.html que devuelve el fallback, la página del iframe de renovación y un asset
        // con hash. Alcanza para probar el hosting sin compilar el front.
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), SpaMarker);
        File.WriteAllText(Path.Combine(_webRoot, "silent-renew.html"), SilentRenewMarker);
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "assets", "main.js"), AssetMarker);
        builder.UseWebRoot(_webRoot);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);

            services.RemoveAll<IWhatsAppOutbox>();
            services.AddSingleton<IWhatsAppOutbox>(WhatsApp);

            // Por si algo llegara al cliente de Meta sin pasar por la cola: falla acá en lugar de salir a internet. Un
            // test que necesite respuestas de Meta cambia este handler con WithWebHostBuilder.
            services.AddHttpClient(WhatsAppRegistration.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new NoNetworkHandler());

            // Claves en memoria: las de Postgres se leen al arrancar el host, antes de que exista el esquema.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            // Mismas opciones que producción (Npgsql, interceptores, OpenIddict); solo cambia el tipo de contexto,
            // que suma la tabla de Widgets.
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

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The tests never call the WhatsApp Cloud API.");
    }

    private static DateTimeOffset StartOfTestClock()
    {
        var now = TimeProvider.System.GetUtcNow();

        return new DateTimeOffset(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }
}
