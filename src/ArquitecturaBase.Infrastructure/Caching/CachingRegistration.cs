using ArquitecturaBase.Application.Interfaces.Integrations.Caching;
using ArquitecturaBase.Infrastructure.Identity.OpenIddict;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StackExchange.Redis;

namespace ArquitecturaBase.Infrastructure.Caching;

public static class CachingRegistration
{
    public const string ConnectionName = "cache";

    /// <summary>El chequeo que registra el cliente de Aspire.</summary>
    public const string HealthCheckName = "StackExchange.Redis";

    /// <summary>Sin un valor: dice qué clave falta, para qué sirve y cómo se elige.</summary>
    public const string MissingKeyPrefixMessage =
        "Missing Caching:KeyPrefix: outside Development and Testing each installation and environment sets its own Redis "
        + "key prefix, the same in all its replicas, so that two installations that share a Redis server do not read each "
        + "other's settings. Set it to something like '<installation>:<environment>'.";

    public static TBuilder AddRedisCaching<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(ConnectionName)))
        {
            throw new InvalidOperationException("Missing connection string 'ConnectionStrings:cache'. Start the API from the AppHost.");
        }

        builder.AddRedisClient(ConnectionName, configureOptions: static options =>
        {
            options.ConnectTimeout = 3_000;
            options.AsyncTimeout = 3_000;
            options.SyncTimeout = 3_000;
            options.AbortOnConnectFail = false;
            options.BacklogPolicy = BacklogPolicy.FailFast;
        });
        builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(RedisCache.MeterName));

        // N9: sin Redis la app sigue (los permisos y los ajustes se leen de la base), así que su caída deja /health en
        // Degraded, que responde 200, como WhatsApp y el correo: la copia sigue en el tráfico. Sigue sin el tag "live".
        builder.Services.PostConfigure<HealthCheckServiceOptions>(options =>
        {
            foreach (var registration in options.Registrations.Where(registration => registration.Name == HealthCheckName))
            {
                registration.FailureStatus = HealthStatus.Degraded;
            }
        });

        return builder;
    }

    internal static IServiceCollection AddCaching(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // Sólo Development y Testing derivan el prefijo (N19): aplicación, ambiente y base no distinguen dos instalaciones
        // que comparten Redis con bases del mismo nombre, y una leería el modo de registro y la presentación de la otra.
        // En otro ambiente, sin Caching:KeyPrefix la Api no arranca.
        var derivesKeyPrefix = environment.IsDevelopment()
            || environment.IsEnvironment(OpenIddictRegistration.TestingEnvironment);

        services.AddOptions<RedisCacheOptions>()
            .BindConfiguration(RedisCacheOptions.SectionName)
            .Configure(options =>
            {
                if (derivesKeyPrefix && string.IsNullOrWhiteSpace(options.KeyPrefix))
                {
                    var database = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString(DependencyInjection.DatabaseConnectionName)).Database;
                    options.KeyPrefix = $"{environment.ApplicationName}:{environment.EnvironmentName}:{database}";
                }
            })
            .ValidateDataAnnotations()
            .Validate(options => !string.IsNullOrWhiteSpace(options.KeyPrefix), MissingKeyPrefixMessage)
            // Un null ("KeyPrefix": null) ya lo rechaza la anterior; sin derivarlo, puede llegar hasta acá.
            .Validate(options => options.KeyPrefix is null || (options.KeyPrefix.Length <= 256
                && !options.KeyPrefix.Contains('{', StringComparison.Ordinal)
                && !options.KeyPrefix.Contains('}', StringComparison.Ordinal)),
                "Caching:KeyPrefix must be at most 256 characters and cannot contain hash-tag delimiters.")
            .ValidateOnStart();

        services.AddSingleton<RedisCache>();
        services.AddSingleton<ISystemSettingsCache, SystemSettingsCache>();
        return services;
    }
}
