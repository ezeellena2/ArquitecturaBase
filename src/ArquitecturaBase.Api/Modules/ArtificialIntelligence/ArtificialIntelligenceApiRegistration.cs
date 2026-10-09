using System.Threading.RateLimiting;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence;
public static class ArtificialIntelligenceApiRegistration
{
    public const string ReadPolicy = "ai-read";
    public const string WritePolicy = "ai-write";
    public const string TestPolicy = "ai-test";
    public const string PlacesPolicy = "ai-places";
    public const string AssistantSubmitPolicy = "ai-assistant-submit";
    public static IServiceCollection AddArtificialIntelligenceApi(this IServiceCollection services)
    {
        services.AddOptions<AiRateLimitOptions>().BindConfiguration("ArtificialIntelligence:RateLimiting").ValidateDataAnnotations().ValidateOnStart();
        services.Configure<RateLimiterOptions>(options =>
        {
            options.AddPolicy(ReadPolicy, context => Limit(context, ReadPolicy));
            options.AddPolicy(WritePolicy, context => Limit(context, WritePolicy));
            options.AddPolicy(TestPolicy, context => Limit(context, TestPolicy));
            options.AddPolicy(PlacesPolicy, context => Limit(context, PlacesPolicy));
            options.AddPolicy(AssistantSubmitPolicy, context => Limit(context, AssistantSubmitPolicy));
        });
        return services;
    }
    private static RateLimitPartition<string> Limit(HttpContext context, string policy)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<AiRateLimitOptions>>().Value;
        var operatorId = context.RequestServices.GetRequiredService<ICurrentUser>().UserId;
        return RateLimitPartition.GetFixedWindowLimiter(operatorId?.ToString() ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = policy == AssistantSubmitPolicy ? options.AssistantSubmitPermitLimit : policy == PlacesPolicy ? options.PlacesPermitLimit : policy == TestPolicy ? options.TestPermitLimit : policy == WritePolicy ? options.WritePermitLimit : options.ReadPermitLimit,
                Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            });
    }
}
