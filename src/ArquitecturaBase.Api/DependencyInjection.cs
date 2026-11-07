using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Api.Authentication;
using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Json;
using ArquitecturaBase.Api.Localization;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Api.RequestContext;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
            ProblemDetailsMapper.AddTraceId(context.ProblemDetails, context.HttpContext);
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Los esquemas (cookie de Identity y validación de OpenIddict) los registra Infrastructure.
        // Las políticas "permission:*" se arman al vuelo para los atributos [HasPermission(...)] de los controllers.
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // Brotli o gzip, según pida el navegador, para el SPA y el JSON; los tipos por defecto dejan afuera los PDF,
        // los audios y las imágenes, que ya vienen comprimidos o se piden por rangos. Detrás del proxy toda petición
        // es HTTPS, y sin EnableForHttps no se comprimiría nada. No abre BREACH porque otro sitio no puede hacer que el
        // navegador pida algo con sesión: la Api se autentica con el token en un encabezado y /account sólo acepta JSON.
        services.AddResponseCompression(options => options.EnableForHttps = true);

        services.AddRequestLocalizationDefaults();
        services.AddRateLimitingPolicies();
        services.AddOpenApiDocumentation();

        // Los controllers leen y escriben con las opciones de MVC; los ProblemDetails que se arman fuera de un controller
        // y el documento de OpenAPI, con las de Http. Las dos salen de ConfigureJson: si divergen, una fecha o un enum
        // sale distinto según quién arme la respuesta.
        services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));

        services.AddControllers(options => options.Filters.Add(new EmptyJsonBodyContentTypeFilter()))
            .AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions));
        services.Configure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = MvcInvalidModelStateResponseFactory.Create);

        return services;
    }

    private static void ConfigureJson(JsonSerializerOptions options)
    {
        options.Converters.Add(new UtcDateTimeConverter());

        // Los enums viajan por su nombre ("InviteOnly", "Open"): el front no tiene que conocer los números,
        // y un valor nuevo no corre la numeración de los que ya estaban.
        options.Converters.Add(new JsonStringEnumConverter());
    }
}
