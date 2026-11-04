using System.Threading.RateLimiting;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.Modules.Arca;

/// Los límites de pedidos del módulo ARCA; sus controllers los descubre MVC.
public static class ArcaApiRegistration
{
    public const string ReadPolicy = "arca-read";
    public const string WritePolicy = "arca-write";
    public const string RemotePolicy = "arca-remote";

    public static IServiceCollection AddArcaApi(this IServiceCollection services)
    {
        services.AddOptions<ArcaRateLimitOptions>().BindConfiguration("Arca:RateLimiting").ValidateDataAnnotations().ValidateOnStart();
        services.Configure<RateLimiterOptions>(options =>
        {
            options.AddPolicy(ReadPolicy, context => Limit(context, limits => limits.ReadPermitLimit));
            options.AddPolicy(WritePolicy, context => Limit(context, limits => limits.WritePermitLimit));
            options.AddPolicy(RemotePolicy, context => Limit(context, limits => limits.RemotePermitLimit));
        });
        return services;
    }

    private static RateLimitPartition<string> Limit(HttpContext context, Func<ArcaRateLimitOptions, int> permitLimit)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<ArcaRateLimitOptions>>().Value;
        var operatorId = context.RequestServices.GetRequiredService<ICurrentUser>().UserId;
        return RateLimitPartition.GetFixedWindowLimiter(operatorId?.ToString() ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit(options), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    }
}
