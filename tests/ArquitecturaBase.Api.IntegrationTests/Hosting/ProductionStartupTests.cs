using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Infrastructure.Identity.OpenIddict;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.EntityFrameworkCore.Models;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Hosting;

/// <summary>
/// La Api arrancando en Production (Etapa 7, tarea 9; P1 = A). Las migraciones las aplica el bundle antes de la imagen,
/// así que el test migra una base nueva por su cuenta y recién después arranca el host; al arrancar, la Api siembra. Con
/// migraciones pendientes no arranca y dice que se corra el bundle.
/// <para>
/// Hereda la configuración de <see cref="ApiFactory"/> (correo en memoria, la clave HMAC, el issuer, el secreto de Google,
/// el cliente web, Data Protection efímero, el reloj falso y lo que sume la parte de cada módulo): prueba las ramas por
/// ambiente (los certificados de OpenIddict, la validación del origen público, sin migrar ni OpenAPI, HSTS, el chequeo
/// de migraciones y el seed), no la lista de configuración obligatoria de producción. Lo único que Production pide y el
/// arnés no trae son los certificados, que se generan acá.
/// </para>
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ProductionStartupTests(ApiFactory factory)
{
    private const string CertificatePassword = "production-startup-tests";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Starting_in_production_seeds_a_migrated_empty_database_and_starting_again_duplicates_nothing()
    {
        var connectionString = factory.NewDatabaseConnectionString("production");
        await using var dbContext = CreateDbContext(connectionString);

        try
        {
            // Lo que hace el bundle en el pipeline, antes de que arranque la imagen.
            await dbContext.Database.MigrateAsync(Ct);

            await using (var api = ProductionApi(connectionString))
            {
                // Services arranca el host: Program.cs corre InitializeDatabaseAsync antes de RunAsync.
                _ = api.Services;
            }

            await AssertSeededOnceAsync(dbContext);

            // Un segundo despliegue, o una réplica que arranca después, sobre la misma base.
            await using (var again = ProductionApi(connectionString))
            {
                _ = again.Services;
            }

            await AssertSeededOnceAsync(dbContext);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }

    [Fact]
    public async Task Starting_in_production_against_an_unmigrated_database_fails()
    {
        // Una base que no existe: nadie corrió el bundle. EF la cuenta como una base con todas las migraciones pendientes.
        var connectionString = factory.NewDatabaseConnectionString("unmigrated");
        await using var dbContext = CreateDbContext(connectionString);

        try
        {
            await using var api = ProductionApi(connectionString);

            var exception = Assert.ThrowsAny<Exception>(() => api.Services);

            var messages = string.Join(" | ", Chain(exception).Select(inner => inner.Message));
            Assert.Contains("pending migrations", messages, StringComparison.Ordinal);
            Assert.Contains("bundle", messages, StringComparison.Ordinal);

            // No migró por su cuenta: la base sigue sin existir.
            Assert.False(await dbContext.Database.CanConnectAsync(Ct));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }

    [Fact]
    public async Task Starting_in_production_against_a_server_that_does_not_respond_says_so()
    {
        // Nadie escucha en ese puerto: la base no responde. El error tiene que decirlo, y no mandar a correr el bundle
        // por unas migraciones "pendientes" que EF inventa cuando no puede preguntar.
        var connectionString = "Host=127.0.0.1;Port=1;Database=unreachable;Username=postgres;Password=unused;Timeout=5";
        await using var api = ProductionApi(connectionString);

        var exception = Assert.ThrowsAny<Exception>(() => api.Services);

        var messages = string.Join(" | ", Chain(exception).Select(inner => inner.Message));
        Assert.Contains("does not respond", messages, StringComparison.Ordinal);
        Assert.DoesNotContain("pending migrations", messages, StringComparison.Ordinal);
    }

    /// <summary>
    /// La Api en Production sobre la base dada, con el ApplicationDbContext de producción (el TestDbContext del arnés suma
    /// Widgets, que no están en las migraciones), como OpenApiTests.DevelopmentApi. Fuera de Development y Testing,
    /// OpenIddict firma y cifra con PFX propios (CertificateLoader): se pasan en base64, como en un contenedor.
    /// </summary>
    private WebApplicationFactory<Program> ProductionApi(string connectionString) => factory.WithWebHostBuilder(builder => builder
        .UseEnvironment("Production")
        .UseSetting($"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", connectionString)
        .UseSetting($"{CertificateLoader.CertificatesSection}:Signing:Base64", CreatePfx(X509KeyUsageFlags.DigitalSignature))
        .UseSetting($"{CertificateLoader.CertificatesSection}:Signing:Password", CertificatePassword)
        .UseSetting($"{CertificateLoader.CertificatesSection}:Encryption:Base64", CreatePfx(X509KeyUsageFlags.KeyEncipherment))
        .UseSetting($"{CertificateLoader.CertificatesSection}:Encryption:Password", CertificatePassword)
        .ConfigureTestServices(services => services.Replace(ServiceDescriptor.Scoped<ApplicationDbContext>(serviceProvider =>
            new ApplicationDbContext(serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>())))));

    /// <summary>Un contexto propio, fuera del host: migra antes de arrancarlo y revisa lo que dejó.</summary>
    private static ApplicationDbContext CreateDbContext(string connectionString)
    {
        // UseOpenIddict devuelve el builder no genérico: las opciones tipadas salen del builder original.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        options.UseNpgsql(connectionString).UseOpenIddict<Guid>();

        return new ApplicationDbContext(options.Options);
    }

    /// <summary>Admin con todos los permisos y User, una fila de ajustes, el scope "api" y el cliente "web", una vez cada uno.</summary>
    private static async Task AssertSeededOnceAsync(ApplicationDbContext dbContext)
    {
        var roles = await dbContext.Roles.AsNoTracking().Select(role => role.Name!).ToListAsync(Ct);
        Assert.Equal([SystemRoles.Admin, SystemRoles.User], roles.Order(StringComparer.Ordinal));

        // Sin Distinct: un permiso repetido también es un rojo.
        var adminPermissions = await dbContext.RoleClaims
            .AsNoTracking()
            .Where(claim => claim.ClaimType == Permissions.ClaimType
                && dbContext.Roles.Any(role => role.Id == claim.RoleId && role.Name == SystemRoles.Admin))
            .Select(claim => claim.ClaimValue!)
            .ToListAsync(Ct);
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), adminPermissions.Order(StringComparer.Ordinal));

        Assert.Equal(1, await dbContext.SystemSettings.CountAsync(Ct));
        Assert.Equal(1, await dbContext.Set<OpenIddictEntityFrameworkCoreScope<Guid>>()
            .CountAsync(scope => scope.Name == AuthServerDefaults.ApiScope, Ct));
        Assert.Equal(1, await dbContext.Set<OpenIddictEntityFrameworkCoreApplication<Guid>>()
            .CountAsync(application => application.ClientId == AuthServerDefaults.WebClientId, Ct));
    }

    /// <summary>
    /// Un PFX autofirmado en base64, con el uso de clave que OpenIddict revisa: DigitalSignature para firmar y
    /// KeyEncipherment para cifrar. Las fechas salen del reloj real (DateTimeOffset.UtcNow está prohibido): OpenIddict
    /// descarta los certificados vencidos o todavía no válidos, y el reloj del arnés arranca en la hora real.
    /// </summary>
    private static string CreatePfx(X509KeyUsageFlags usage)
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=ArquitecturaBase Production Startup Tests", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));

        var now = TimeProvider.System.GetUtcNow();
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));

        return Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, CertificatePassword));
    }

    /// <summary>La excepción y todas las de adentro: el arranque puede envolver la del programa.</summary>
    private static IEnumerable<Exception> Chain(Exception exception)
    {
        var pending = new Stack<Exception>([exception]);

        while (pending.TryPop(out var current))
        {
            yield return current;

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    pending.Push(inner);
                }
            }
            else if (current.InnerException is not null)
            {
                pending.Push(current.InnerException);
            }
        }
    }
}
