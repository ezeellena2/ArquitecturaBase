using System.Globalization;
using System.Threading.RateLimiting;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.RateLimiting;

internal static class RateLimitingExtensions
{
    public const string LoginCodePolicy = "login-code";
    public const string LoginVerifyPolicy = "login-verify";

    public static IServiceCollection AddRateLimitingPolicies(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            // El default es 503; el spec pide 429 con retryAfter.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;

            options.AddPolicy(LoginCodePolicy, context =>
            {
                var settings = SettingsOf(context);
                return FixedWindowByIp(context, settings.LoginCodePermitLimit, settings.LoginCodeWindowMinutes);
            });

            options.AddPolicy(LoginVerifyPolicy, context =>
            {
                var settings = SettingsOf(context);
                return FixedWindowByIp(context, settings.LoginVerifyPermitLimit, settings.LoginVerifyWindowMinutes);
            });
        });

        return services;
    }

    private static RateLimitingOptions SettingsOf(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    private static RateLimitPartition<string> FixedWindowByIp(HttpContext context, int permitLimit, int windowMinutes) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(windowMinutes),
                QueueLimit = 0,
            });

    // Mismo formato que los demás errores, con retryAfter en segundos (encabezado y cuerpo).
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        var problem = ProblemDetailsMapper.Create(
            ErrorType.TooManyRequests, ApiErrorCodes.TooManyRequests, ErrorMessages.Get(ApiErrorCodes.TooManyRequests));

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            problem.Extensions[LoginCodeErrors.RetryAfterKey] = seconds;
        }

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });
    }
}
