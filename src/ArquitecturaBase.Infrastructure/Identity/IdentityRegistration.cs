using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Identity;

internal static class IdentityRegistration
{
    public static IServiceCollection AddIdentityServices(this IServiceCollection services)
    {
        // AddIdentityCore y no AddIdentity: AddIdentity fija la cookie como esquema por defecto para autenticar y
        // desafiar, y /api tiene que usar la validación de OpenIddict (Tarea 17).
        services.AddAuthentication().AddIdentityCookies();

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
                options.User.RequireUniqueEmail = true;

                // El UserName es el email, ya validado por el value object Email.
                options.User.AllowedUserNameCharacters = string.Empty;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 10;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager();

        // Claves en Postgres: la cookie y los tokens siguen valiendo con varias instancias o tras reiniciar.
        services.AddDataProtection()
            .SetApplicationName("ArquitecturaBase")
            .PersistKeysToDbContext<ApplicationDbContext>();

        services.AddHybridCache();

        services.AddOptions<SeedOptions>()
            .BindConfiguration(SeedOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<RoleSeeder>();

        return services;
    }
}
