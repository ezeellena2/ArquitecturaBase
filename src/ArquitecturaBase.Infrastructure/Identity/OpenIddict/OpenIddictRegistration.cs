using System.Security.Cryptography.X509Certificates;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Infrastructure.Identity.OpenIddict;

internal static class OpenIddictRegistration
{
    public const string TestingEnvironment = "Testing";
    public const string CertificatesSection = "Authentication:Certificates";

    public static IServiceCollection AddOpenIddictServer(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<WebClientOptions>()
            .BindConfiguration(WebClientOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOpenIddict()
            .AddCore(options => options
                .UseEntityFrameworkCore()
                .UseDbContext<ApplicationDbContext>()
                .ReplaceDefaultEntities<Guid>())
            .AddServer(options =>
            {
                options
                    .SetAuthorizationEndpointUris(AuthServerDefaults.AuthorizationEndpoint)
                    .SetTokenEndpointUris(AuthServerDefaults.TokenEndpoint)
                    .SetEndSessionEndpointUris(AuthServerDefaults.EndSessionEndpoint)
                    .SetUserInfoEndpointUris(AuthServerDefaults.UserInfoEndpoint)
                    .SetRevocationEndpointUris(AuthServerDefaults.RevocationEndpoint)
                    .SetIntrospectionEndpointUris(AuthServerDefaults.IntrospectionEndpoint);

                // Authorization code con PKCE obligatorio y refresh token. Client credentials queda para más adelante.
                options
                    .AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange()
                    .AllowRefreshTokenFlow();

                options.RegisterScopes(
                    Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Roles, Scopes.OfflineAccess, AuthServerDefaults.ApiScope);

                // Los defaults de OpenIddict contradicen el spec: access token de 1 h, refresh token de 14 días y 30 s
                // en los que un refresh token ya usado se acepta de nuevo. Sin ese margen, reusarlo revoca toda la cadena.
                options
                    .SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(5))
                    .SetAccessTokenLifetime(TimeSpan.FromMinutes(15))
                    .SetRefreshTokenLifetime(TimeSpan.FromDays(30))
                    .SetRefreshTokenReuseLeeway(null);

                AddCredentials(options, configuration, environment);

                // Passthrough: los endpoints de /connect de la Api deciden quién es el usuario y qué claims lleva.
                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();

                // Va después de UseLocalServer: así un token revocado (logout o reuso de un refresh token) deja de
                // valer en el momento, sin esperar a que venza.
                options.EnableTokenEntryValidation();
                options.UseAspNetCore();
            });

        services.AddScoped<OpenIddictSeeder>();

        return services;
    }

    private static void AddCredentials(OpenIddictServerBuilder options, IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            options.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
        }
        else if (environment.IsEnvironment(TestingEnvironment))
        {
            options.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
        }
        else
        {
            options
                .AddEncryptionCertificate(LoadCertificate(configuration, "Encryption"))
                .AddSigningCertificate(LoadCertificate(configuration, "Signing"));
        }
    }

    // Producción: un PFX por uso, con ruta y contraseña en Authentication:Certificates:{Encryption|Signing}.
    // Los constructores de X509Certificate2 están obsoletos (SYSLIB0057).
    private static X509Certificate2 LoadCertificate(IConfiguration configuration, string purpose)
    {
        var section = configuration.GetSection(CertificatesSection).GetSection(purpose);
        var path = section["Path"];

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                $"Missing '{CertificatesSection}:{purpose}:Path': OpenIddict needs PFX certificates outside Development and Testing.");
        }

        return X509CertificateLoader.LoadPkcs12FromFile(path, section["Password"]);
    }
}
