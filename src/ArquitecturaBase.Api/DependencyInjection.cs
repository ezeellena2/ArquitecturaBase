using System.Diagnostics;
using System.Text.Json.Serialization;
using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.Endpoints.Connect;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Json;
using ArquitecturaBase.Api.Localization;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Api.Services;
using ArquitecturaBase.Application.Interfaces.Integrations;
using Microsoft.AspNetCore.Authorization;

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

        // Los esquemas (cookie de Identity y validación de OpenIddict) los registra Infrastructure.
        // Las políticas "permission:*" se arman al vuelo: .RequirePermission(Permissions.Users.Read).
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddRequestLocalizationDefaults();
        services.AddRateLimitingPolicies();
        services.AddOpenApi();

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new UtcDateTimeConverter());

            // Los enums viajan por su nombre ("InviteOnly", "Open"): el front no tiene que conocer los números,
            // y un valor nuevo no corre la numeración de los que ya estaban.
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services.AddControllers(options => options.Filters.Add(new EmptyJsonBodyContentTypeFilter())).AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services.AddEndpoints(typeof(DependencyInjection).Assembly);

        return services;
    }
}
