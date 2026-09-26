using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.OpenApi;

internal static class OpenApiExtensions
{
    private const string DocumentUrl = "/openapi/v1.json";

    /// <summary>
    /// El generador del documento, en todos los ambientes, y la convención que le declara a cada acción sus errores como
    /// ProblemDetails (<see cref="ProblemResponsesConvention"/>).
    /// </summary>
    public static IServiceCollection AddOpenApiDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi();
        services.Configure<MvcOptions>(options => options.Conventions.Add(new ProblemResponsesConvention()));

        return services;
    }

    /// <summary>
    /// Documento en /openapi/v1.json (Microsoft.AspNetCore.OpenApi) y Swagger UI en /swagger, que lo lee.
    /// Program.cs lo mapea solo en desarrollo.
    /// </summary>
    public static WebApplication MapOpenApiDocumentation(this WebApplication app)
    {
        app.MapOpenApi();
        app.UseSwaggerUI(options => options.SwaggerEndpoint(DocumentUrl, "ArquitecturaBase v1"));

        return app;
    }
}
