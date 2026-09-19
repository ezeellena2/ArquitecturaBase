using Scalar.AspNetCore;

namespace ArquitecturaBase.Api.OpenApi;

internal static class OpenApiExtensions
{
    /// <summary>Documento en /openapi/v1.json y referencia de Scalar en /scalar. Program.cs lo mapea solo en desarrollo.</summary>
    public static WebApplication MapOpenApiDocumentation(this WebApplication app)
    {
        app.MapOpenApi();
        app.MapScalarApiReference();

        return app;
    }
}
