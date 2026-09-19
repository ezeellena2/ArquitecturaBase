using System.Diagnostics;
using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.Endpoints.Connect;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Json;
using ArquitecturaBase.Api.Localization;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Api.Services;
using ArquitecturaBase.Application.Abstractions.Identity;

namespace ArquitecturaBase.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IRequestInfo, RequestInfo>();
        services.AddScoped<OpenIdPrincipalFactory>();

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            // Los errores que arma el propio framework salen con el mismo formato que los nuestros.
            ProblemDetailsMapper.CompleteFrameworkProblem(context.ProblemDetails);

            // Todas las respuestas de error llevan el traceId para buscarlas en el dashboard de Aspire.
            context.ProblemDetails.Extensions.TryAdd(ProblemDetailsMapper.TraceIdExtension, Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Los errores de binding lanzan BadHttpRequestException y los formatea GlobalExceptionHandler.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        // Los esquemas (cookie de Identity y, desde la Tarea 17, OpenIddict) los registra Infrastructure.
        services.AddAuthorization();

        services.AddRequestLocalizationDefaults();
        services.AddRateLimitingPolicies();
        services.AddOpenApi();

        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new UtcDateTimeConverter()));

        services.AddEndpoints(typeof(DependencyInjection).Assembly);

        return services;
    }
}
