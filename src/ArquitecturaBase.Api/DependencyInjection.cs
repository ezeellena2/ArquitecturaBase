using System.Diagnostics;
using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Json;
using ArquitecturaBase.Api.Localization;
using ArquitecturaBase.Api.Services;
using ArquitecturaBase.Application.Abstractions.Identity;

namespace ArquitecturaBase.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

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

        // Sin esquemas ni políticas en la Fase 1: la Fase 2 agrega OpenIddict y los permisos.
        services.AddAuthentication();
        services.AddAuthorization();

        services.AddRequestLocalizationDefaults();
        services.AddOpenApi();

        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new UtcDateTimeConverter()));

        services.AddEndpoints(typeof(DependencyInjection).Assembly);

        return services;
    }
}
