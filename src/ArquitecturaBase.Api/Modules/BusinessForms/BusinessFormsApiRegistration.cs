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
    // The official preview asks Meta several times per request: ten a minute per person (F17). The status of a publication only asks Meta when the operation failed or is uncertain, so its polling stays on ReadPolicy.
    public const string MetaReadPolicy = "forms-meta-read";
    // Exports and imports (records and catalogs) heavy enough to hold the copy's memory: at most this many at once per copy (F11, F12).
    public const int GlobalExportLimit = 2;
    public static IServiceCollection AddBusinessFormsApi(this IServiceCollection services)
    {
        services.Configure<RateLimiterOptions>(options =>
        {
            // Chained with the endpoint policy by the middleware: the per-person slot stays, and the whole copy shares a small cap.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == ExportPolicy
                    ? RateLimitPartition.GetConcurrencyLimiter(ExportPolicy, _ => new ConcurrencyLimiterOptions { PermitLimit = GlobalExportLimit, QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter(string.Empty));
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
