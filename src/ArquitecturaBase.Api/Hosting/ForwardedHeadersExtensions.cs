using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace ArquitecturaBase.Api.Hosting;

internal static class ForwardedHeadersExtensions
{
    private const string SectionName = "ForwardedHeaders";

    public static IServiceCollection AddTrustedForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = configuration.GetSection(SectionName).Get<ForwardedHeadersSettings>() ?? new();

        services.Configure<ForwardedHeadersOptions>(options => Configure(options, settings));

        return services;
    }

    private static void Configure(ForwardedHeadersOptions options, ForwardedHeadersSettings settings)
    {
        var knownProxies = settings.KnownProxies ?? [];
        var knownNetworks = settings.KnownNetworks ?? [];

        if (settings.TrustAll && (knownProxies.Length > 0 || knownNetworks.Length > 0))
        {
            throw new InvalidOperationException(
                $"'{SectionName}:TrustAll' cannot be combined with explicit known proxies or networks.");
        }

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;

        if (settings.TrustAll)
        {
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            return;
        }

        AddKnownProxies(options, knownProxies);
        AddKnownNetworks(options, knownNetworks);
    }

    private static void AddKnownProxies(ForwardedHeadersOptions options, string[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (!IPAddress.TryParse(values[index], out var address))
            {
                throw InvalidEntry("KnownProxies", index, "an IPv4 or IPv6 address");
            }

            options.KnownProxies.Add(address);
        }
    }

    private static void AddKnownNetworks(ForwardedHeadersOptions options, string[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (!System.Net.IPNetwork.TryParse(values[index], out var network))
            {
                throw InvalidEntry("KnownNetworks", index, "a network in CIDR notation");
            }

            options.KnownIPNetworks.Add(network);
        }
    }

    private static InvalidOperationException InvalidEntry(string collection, int index, string expected) =>
        new($"'{SectionName}:{collection}:{index}' must be {expected}.");

    private sealed class ForwardedHeadersSettings
    {
        public bool TrustAll { get; init; }

        public string[]? KnownProxies { get; init; }

        public string[]? KnownNetworks { get; init; }
    }
}
