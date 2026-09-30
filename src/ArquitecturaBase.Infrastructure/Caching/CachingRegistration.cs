using ArquitecturaBase.Application.Interfaces.Integrations.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StackExchange.Redis;

namespace ArquitecturaBase.Infrastructure.Caching;

public static class CachingRegistration
{
    public const string ConnectionName = "cache";

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

        return builder;
    }

    internal static IServiceCollection AddCaching(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<RedisCacheOptions>()
            .BindConfiguration(RedisCacheOptions.SectionName)
            .Configure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.KeyPrefix))
                {
                    var database = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString(DependencyInjection.DatabaseConnectionName)).Database;
                    options.KeyPrefix = $"{environment.ApplicationName}:{environment.EnvironmentName}:{database}";
                }
            })
            .ValidateDataAnnotations()
            .Validate(options => options.KeyPrefix.Length <= 256
                && !options.KeyPrefix.Contains('{', StringComparison.Ordinal)
                && !options.KeyPrefix.Contains('}', StringComparison.Ordinal),
                "Caching:KeyPrefix must be at most 256 characters and cannot contain hash-tag delimiters.")
            .ValidateOnStart();

        services.AddSingleton<RedisCache>();
        services.AddSingleton<ISystemSettingsCache, SystemSettingsCache>();
        return services;
    }
}
