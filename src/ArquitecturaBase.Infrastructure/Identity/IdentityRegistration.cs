using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Validation.AspNetCore;

namespace ArquitecturaBase.Infrastructure.Identity;

internal static class IdentityRegistration
{
    public const string GoogleSection = "Authentication:Google";

    public static IServiceCollection AddIdentityServices(this IServiceCollection services, IConfiguration configuration)
    {
        // AddIdentityCore y no AddIdentity: AddIdentity fija la cookie como esquema por defecto para autenticar y
        // desafiar. /api usa la validación de OpenIddict (bearer); la cookie solo la usan /account y /connect.
        var authentication = services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        authentication.AddIdentityCookies();
        AddGoogle(services, authentication, configuration);

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;

            // Sin redirecciones a una página de login: 401/403 y UseStatusCodePages arma el ProblemDetails.
            // Se asignan de a uno: reemplazar options.Events borraría la validación del security stamp.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                // Con true, Identity exige que toda cuenta tenga correo, y una cuenta puede tener solo el número
                // (sección 6.1 del spec del ingreso con WhatsApp). Que el correo no se repita lo sigue garantizando
                // el índice único sobre NormalizedEmail, que en Postgres admite varios NULL.
                options.User.RequireUniqueEmail = false;

                // El UserName es el Id de la cuenta, que arma IdentityService: nadie lo escribe, así que no hace
                // falta restringir sus caracteres.
                options.User.AllowedUserNameCharacters = string.Empty;

                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager();

        // El bloqueo sale de Authentication:LoginCode (sección 5.3), como el resto de las reglas del código.
        services.AddOptions<IdentityOptions>().Configure<IOptions<LoginCodeOptions>>((identity, loginCode) =>
        {
            identity.Lockout.MaxFailedAccessAttempts = loginCode.Value.LockoutMaxFailedAttempts;
            identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(loginCode.Value.LockoutMinutes);
        });

        // Desactivar o eliminar corta el acceso en el momento (sección 7 del spec de la Fase 4): el security stamp
        // se revisa en cada petición y no cada 30 minutos. Solo /account y /connect usan la cookie, así que la
        // consulta extra no pesa.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

        // Claves en Postgres: la cookie y los tokens siguen valiendo con varias instancias o tras reiniciar.
        services.AddDataProtection()
            .SetApplicationName("ArquitecturaBase")
            .PersistKeysToDbContext<ApplicationDbContext>();

        services.AddHybridCache();

        services.AddOptions<SeedOptions>()
            .BindConfiguration(SeedOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IInitialAdmin, InitialAdmin>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<RoleSeeder>();

        return services;
    }

    // Solo si hay ClientId: registrado con un ClientId vacío, Google rompe todas las peticiones al validar sus opciones.
    // Esté o no, Application sabe si se ofrece por IGoogleAvailability, sin conocer los esquemas de ASP.NET Core.
    private static void AddGoogle(IServiceCollection services, AuthenticationBuilder authentication, IConfiguration configuration)
    {
        var clientId = configuration[GoogleSection + ":ClientId"];

        services.AddSingleton<IGoogleAvailability>(new GoogleAvailability(!string.IsNullOrWhiteSpace(clientId)));

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        authentication.AddGoogle(options =>
        {
            options.ClientId = clientId;

            // En desarrollo viene de user-secrets; en producción, de variables de entorno o un almacén de secretos.
            // Se controla acá, antes que la validación de Google, que solo diría que el valor está vacío.
            var clientSecret = configuration[GoogleSection + ":ClientSecret"];

            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException(
                    "Missing Authentication:Google:ClientSecret. In development, load it with dotnet user-secrets (see the README).");
            }

            options.ClientSecret = clientSecret;
            options.SignInScheme = IdentityConstants.ExternalScheme;

            // Google no lo mapea por defecto; sin él no se puede vincular por email (sección 5.4).
            options.ClaimActions.MapJsonKey(ExternalClaimTypes.EmailVerified, "email_verified");

            // Si el usuario cancela en Google o algo falla, vuelve al login del SPA con el código del error.
            options.Events.OnRemoteFailure = context =>
            {
                context.Response.Redirect(
                    ReturnUrls.LoginPath + "?error=" + Uri.EscapeDataString(ExternalLoginErrors.FailedCode));
                context.HandleResponse();

                return Task.CompletedTask;
            };
        });

        // Crea las opciones al arrancar: sin el secreto, la Api no arranca, en lugar de fallar en el primer ingreso.
        services.AddOptions<GoogleOptions>(GoogleDefaults.AuthenticationScheme).ValidateOnStart();
    }
}
