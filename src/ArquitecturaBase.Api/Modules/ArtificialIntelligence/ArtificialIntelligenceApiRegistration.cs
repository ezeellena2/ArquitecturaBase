using System.Threading.RateLimiting;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using Microsoft.AspNetCore.Http.Metadata;
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
    // I9: the partition of the copy's limit on Home's uploads, shared by file-turns, audio and documents.
    public const string UploadLimit = "ai-assistant-uploads";
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
        // The copy has one global limiter and another module may set it: after every Configure, this one is chained to it, never
        // replacing it. The middleware chains it with the endpoint policy, so the per-person window of sends stays as it is.
        services.PostConfigure<RateLimiterOptions>(options =>
        {
            var uploads = PartitionedRateLimiter.Create<HttpContext, string>(Uploads);
            options.GlobalLimiter = options.GlobalLimiter is { } other ? PartitionedRateLimiter.CreateChained(other, uploads) : uploads;
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
    // I9: the sends that receive a file (the submit policy with a multipart body) take one of the copy's slots before the body is read,
    // whoever sends them; with the slots and the short queue taken, 429 with its wait. Every other request goes on without a limit here.
    private static RateLimitPartition<string> Uploads(HttpContext context)
    {
        var metadata = context.GetEndpoint()?.Metadata;
        if (metadata?.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName != AssistantSubmitPolicy
            || !metadata.GetOrderedMetadata<IAcceptsMetadata>().Any(accepts => accepts.ContentTypes.Contains("multipart/form-data")))
            return RateLimitPartition.GetNoLimiter(string.Empty);
        var options = context.RequestServices.GetRequiredService<IOptions<AiRateLimitOptions>>().Value;
        return RateLimitPartition.GetConcurrencyLimiter(UploadLimit, _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = options.UploadConcurrencyLimit, QueueLimit = options.UploadQueueLimit, QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }
}
