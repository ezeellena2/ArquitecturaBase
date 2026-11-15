using System.Threading.RateLimiting;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using Microsoft.AspNetCore.RateLimiting;
namespace ArquitecturaBase.Api.Modules.BusinessForms;
public static class BusinessFormsApiRegistration
{
    public const string ReadPolicy = "forms-read";
    public const string ExportPolicy = "forms-export";
    public const string FilesReadPolicy = "forms-files-read";
    public const string WritePolicy = "forms-write";
    // The official preview and the status of a publication ask Meta several times per request: ten a minute per person (F17).
    public const string MetaReadPolicy = "forms-meta-read";
    public static IServiceCollection AddBusinessFormsApi(this IServiceCollection services)
    {
        services.Configure<RateLimiterOptions>(options =>
        {
            options.AddPolicy(ExportPolicy, context => RateLimitPartition.GetConcurrencyLimiter(
                context.RequestServices.GetRequiredService<ICurrentUser>().UserId?.ToString() ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 }));
            foreach (var policy in new[] { ReadPolicy, WritePolicy, FilesReadPolicy, MetaReadPolicy })
                options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
                    context.RequestServices.GetRequiredService<ICurrentUser>().UserId?.ToString() ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = policy == WritePolicy ? 30 : policy == FilesReadPolicy ? 600 : policy == MetaReadPolicy ? 10 : 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }
}
