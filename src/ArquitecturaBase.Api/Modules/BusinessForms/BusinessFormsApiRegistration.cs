using System.Threading.RateLimiting;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using Microsoft.AspNetCore.RateLimiting;
namespace ArquitecturaBase.Api.Modules.BusinessForms;
public static class BusinessFormsApiRegistration
{
    public const string ReadPolicy = "forms-read";
    public const string WritePolicy = "forms-write";
    public static IServiceCollection AddBusinessFormsApi(this IServiceCollection services)
    {
        services.Configure<RateLimiterOptions>(options =>
        {
            foreach (var policy in new[] { ReadPolicy, WritePolicy })
                options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
                    context.RequestServices.GetRequiredService<ICurrentUser>().UserId?.ToString() ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = policy == WritePolicy ? 30 : 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }
}
