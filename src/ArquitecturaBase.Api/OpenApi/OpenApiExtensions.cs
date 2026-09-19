namespace ArquitecturaBase.Api.OpenApi;

internal static class OpenApiExtensions
{
    private const string DocumentUrl = "/openapi/v1.json";

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
