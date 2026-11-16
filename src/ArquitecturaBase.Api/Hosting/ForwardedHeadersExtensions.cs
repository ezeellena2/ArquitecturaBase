using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.Hosting;

internal static class ForwardedHeadersExtensions
{
    private const string SectionName = "ForwardedHeaders";

    // El ambiente del arnés de integración (OpenIddictRegistration.TestingEnvironment, interno de Infrastructure).
    private const string TestingEnvironment = "Testing";

    /// <summary>Sin un proxy de confianza: dice qué falta, qué se rompe y qué poner en Container Apps.</summary>
    public const string MissingTrustedProxyMessage =
        "Missing ForwardedHeaders: outside Development and Testing the Api runs behind a proxy, and without trusting it "
        + "the scheme and the client IP are the proxy's (OpenIddict rejects the login requests that arrive over HTTP, and "
        + "the rate limiter puts everybody in one partition). Set ForwardedHeaders:TrustAll=true when Kestrel is only "
        + "reachable through the ingress (Azure Container Apps), or ForwardedHeaders:KnownProxies or KnownNetworks.";

    public static IServiceCollection AddTrustedForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // Al arrancar, con las demás opciones (N17): una entrada mal escrita o la falta de un proxy de confianza frenan
        // la Api antes de comprobar las migraciones y sembrar, y no recién al armar el pipeline. En Development no hay
        // proxy, y el arnés de Testing prueba cada caso con su propia configuración.
        var requireTrustedProxy = !environment.IsDevelopment() && !environment.IsEnvironment(TestingEnvironment);

        services.AddOptions<ForwardedHeadersSettings>()
            .Bind(configuration.GetSection(SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ForwardedHeadersSettings>>(
            new ForwardedHeadersSettingsValidator(requireTrustedProxy));

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ForwardedHeadersSettings>>((options, settings) => Configure(options, settings.Value));

        return services;
    }

    private static void Configure(ForwardedHeadersOptions options, ForwardedHeadersSettings settings)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;

        if (settings.TrustAll)
        {
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            return;
        }

        // Ya validadas: el validador rechaza lo que no se interpreta.
        foreach (var proxy in settings.KnownProxies ?? [])
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        foreach (var network in settings.KnownNetworks ?? [])
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
    }

    private static string InvalidEntry(string collection, int index, string expected) =>
        $"'{SectionName}:{collection}:{index}' must be {expected}.";

    private sealed class ForwardedHeadersSettings
    {
        public bool TrustAll { get; set; }

        public string[]? KnownProxies { get; set; }

        public string[]? KnownNetworks { get; set; }
    }

    private sealed class ForwardedHeadersSettingsValidator(bool requireTrustedProxy)
        : IValidateOptions<ForwardedHeadersSettings>
    {
        public ValidateOptionsResult Validate(string? name, ForwardedHeadersSettings options)
        {
            var knownProxies = options.KnownProxies ?? [];
            var knownNetworks = options.KnownNetworks ?? [];
            var failures = new List<string>();

            if (options.TrustAll && (knownProxies.Length > 0 || knownNetworks.Length > 0))
            {
                failures.Add($"'{SectionName}:TrustAll' cannot be combined with explicit known proxies or networks.");
            }

            for (var index = 0; index < knownProxies.Length; index++)
            {
                if (!IPAddress.TryParse(knownProxies[index], out _))
                {
                    failures.Add(InvalidEntry("KnownProxies", index, "an IPv4 or IPv6 address"));
                }
            }

            for (var index = 0; index < knownNetworks.Length; index++)
            {
                if (!System.Net.IPNetwork.TryParse(knownNetworks[index], out _))
                {
                    failures.Add(InvalidEntry("KnownNetworks", index, "a network in CIDR notation"));
                }
            }

            if (requireTrustedProxy && !options.TrustAll && knownProxies.Length == 0 && knownNetworks.Length == 0)
            {
                failures.Add(MissingTrustedProxyMessage);
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }
}
