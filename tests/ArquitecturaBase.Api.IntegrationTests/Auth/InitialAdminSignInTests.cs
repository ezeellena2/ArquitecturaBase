using System.Globalization;
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

/// <summary>
/// El primer ingreso de una instalación nueva: base vacía, el modo de registro por defecto (InviteOnly) y nadie con
/// cuenta. La cuenta del administrador inicial (Seed:AdminEmail) se crea en su primer ingreso, así que el modo no se
/// la puede negar: si se la negara, no entraría nadie, ni él, y no habría quién diera de alta a los demás.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class InitialAdminSignInTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task On_a_new_install_closed_by_default_the_initial_admin_signs_in_with_a_code_as_admin()
    {
        var adminEmail = TestEmails.Unique("initialadmin");
        await using var install = await NewInstallation.StartAsync(factory, adminEmail);
        using var client = install.Api.CreateClient();

        // El punto de partida del bug: nadie configuró el modo y quedó el de por defecto.
        Assert.Equal(
            RegistrationMode.InviteOnly,
            await install.ExecuteDbContextAsync(db => db.SystemSettings.Select(settings => settings.RegistrationMode).SingleAsync(Ct)));

        // El ingreso completo: el código llega por correo, se verifica y se canjea por un token.
        var tokens = await client.LoginAsync(factory, adminEmail);
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var profile = await me.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(adminEmail, profile.GetProperty("email").GetString());
        Assert.Equal([SystemRoles.Admin], Strings(profile, "roles"));

        // Con el panel de ajustes puede abrir el registro o dar de alta a los demás.
        Assert.Contains(Permissions.Settings.Manage, Strings(profile, "permissions"));
        Assert.Contains(Permissions.Users.Manage, Strings(profile, "permissions"));
    }

    [Fact]
    public async Task On_a_new_install_closed_by_default_the_initial_admin_signs_in_with_google_as_admin()
    {
        var adminEmail = TestEmails.Unique("initialadmingoogle");
        await using var install = await NewInstallation.StartAsync(factory, adminEmail);
        using var client = install.Api.CreateClient();
        var providerKey = "google-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        using var external = await client.PostJsonAsync(
            "/test/external-login", new { providerKey, email = adminEmail, name = "Admin", emailVerified = true });

        using var callback = await client.SendAsync(
            HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(AuthFlow.AuthorizeReturnUrl));

        Assert.True(external.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(AuthFlow.AuthorizeReturnUrl, callback.Headers.Location!.OriginalString);

        var roles = await install.ExecuteScopeAsync(async services =>
        {
            var identity = services.GetRequiredService<IIdentityService>();
            var admin = await identity.FindByEmailAsync(Email.Create(adminEmail).Value, Ct);

            return admin is null ? [] : await identity.GetRolesAsync(admin.Id, Ct);
        });

        Assert.Equal([SystemRoles.Admin], roles);
    }

    [Fact]
    public async Task On_a_new_install_closed_by_default_any_other_email_is_answered_the_same_and_gets_no_account()
    {
        var adminEmail = TestEmails.Unique("initialadmin");
        var stranger = TestEmails.Unique("stranger");
        await using var install = await NewInstallation.StartAsync(factory, adminEmail);
        using var client = install.Api.CreateClient();

        using var strangerResponse = await client.PostJsonAsync("/account/login-code", new { email = stranger });
        using var adminResponse = await client.PostJsonAsync("/account/login-code", new { email = adminEmail });

        // La cola de emails es FIFO y la vacía un solo lector: cuando llega el del administrador, el del otro correo
        // ya habría llegado si se hubiera encolado.
        await factory.EmailSender.WaitForAsync(adminEmail);

        // Byte a byte la misma respuesta: lo único distinto es que el código le llega a la casilla del administrador.
        Assert.Equal(HttpStatusCode.Accepted, strangerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, adminResponse.StatusCode);
        Assert.Equal(
            await adminResponse.Content.ReadAsStringAsync(Ct),
            await strangerResponse.Content.ReadAsStringAsync(Ct));
        Assert.Equal(0, factory.EmailSender.CountFor(stranger));

        // Aunque consiguiera un código válido, la cuenta no se crea: la excepción es solo para el administrador.
        // El pedido de arriba ya guardó un código para este correo, y el reloj del arnés no avanza solo: sin moverlo, los
        // dos quedarían con el mismo CreatedAtUtc y cuál se verifica lo decidiría el plan de Postgres. Así el de acá es
        // el más nuevo sin discusión.
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        var code = await install.IssueSignInCodeAsync(factory, stranger);
        using var verify = await client.PostJsonAsync(
            "/account/login-code/verify", new { email = stranger, code, returnUrl = AuthFlow.AuthorizeReturnUrl });
        var problem = await verify.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, verify.StatusCode);
        Assert.Equal(AccountErrors.NotInvitedCode, problem.GetProperty("code").GetString());
        Assert.False(await install.ExecuteDbContextAsync(db => db.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .AnyAsync(user => user.Email == stranger, Ct)));
    }

    private static string[] Strings(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();

    /// <summary>
    /// Una instalación recién hecha: su propia base, vacía y con el seed, sin <c>Registration:Mode</c> (vale el valor
    /// por defecto, InviteOnly) y con un <c>Seed:AdminEmail</c> propio. Nadie tiene cuenta todavía. Comparte con el
    /// arnés el reloj y los emails capturados.
    /// </summary>
    private sealed class NewInstallation : IAsyncDisposable
    {
        private NewInstallation(WebApplicationFactory<Program> api)
        {
            Api = api;
        }

        public WebApplicationFactory<Program> Api { get; }

        public static async Task<NewInstallation> StartAsync(ApiFactory factory, string adminEmail)
        {
            // ApiFactory fija Registration:Mode = Open y WithWebHostBuilder lo hereda. Como en SystemSettingsSeedTests,
            // la clave queda sin valor: una fuente posterior la tapa y el binder la saltea.
            var installation = new NewInstallation(factory.WithWebHostBuilder(builder => builder
                .UseSetting(
                    $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("install"))
                .UseSetting("Seed:AdminEmail", adminEmail)
                .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Registration:Mode"] = null }))));

            // Igual que el arnés: el esquema sale del modelo de TestDbContext, y después los mismos datos base.
            await installation.ExecuteDbContextAsync(db => db.Database.EnsureCreatedAsync(Ct));
            await installation.Api.Services.SeedDatabaseAsync(Ct);

            return installation;
        }

        public async Task<T> ExecuteScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
        {
            await using var scope = Api.Services.CreateAsyncScope();

            return await action(scope.ServiceProvider);
        }

        public Task<T> ExecuteDbContextAsync<T>(Func<ApplicationDbContext, Task<T>> action) =>
            ExecuteScopeAsync(services => action(services.GetRequiredService<ApplicationDbContext>()));

        /// <summary>
        /// Emite y guarda un código de ingreso válido para <paramref name="email"/> sin pasar por el pedido, así existe
        /// aunque el pedido no lo haya mandado. Devuelve el código en claro.
        /// </summary>
        public Task<string> IssueSignInCodeAsync(ApiFactory factory, string email) =>
            ExecuteScopeAsync(async services =>
            {
                const string code = "482913";
                var destination = LoginCodeDestination.ForEmail(Email.Create(email).Value);
                var codeHash = services.GetRequiredService<ILoginCodeHasher>().Hash(destination, LoginCodePurpose.SignIn, code);

                services.GetRequiredService<ILoginCodeRepository>().Add(LoginCode.Issue(
                    destination,
                    LoginCodePurpose.SignIn,
                    requestedByUserId: null,
                    codeHash,
                    factory.Clock.GetUtcNow().UtcDateTime,
                    TimeSpan.FromMinutes(10),
                    maxAttempts: 5));
                await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);

                return code;
            });

        public async ValueTask DisposeAsync()
        {
            try
            {
                await ExecuteDbContextAsync(db => db.Database.EnsureDeletedAsync(Ct));
            }
            finally
            {
                await Api.DisposeAsync();
            }
        }
    }
}
