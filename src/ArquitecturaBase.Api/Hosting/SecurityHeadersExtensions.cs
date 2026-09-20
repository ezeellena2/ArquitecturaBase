namespace ArquitecturaBase.Api.Hosting;

internal static class SecurityHeadersExtensions
{
    /// <summary>
    /// Encabezados de seguridad de la sección 6.9 del spec. La CSP permite exactamente lo que hace el SPA: sus
    /// propios archivos (scripts, estilos y fuentes con hash, servidos desde este mismo origen), los estilos en
    /// línea que escriben los componentes accesibles, sus llamadas a esta misma Api, el iframe de renovación
    /// silenciosa (/silent-renew.html, del mismo origen) y el formulario que puede terminar en Google.
    /// </summary>
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-src 'self'; " +
        "frame-ancestors 'self'; " +
        "form-action 'self' https://accounts.google.com; " +
        "base-uri 'self'";

    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use(async (context, next) =>
        {
            // Los encabezados se escriben recién cuando arranca la respuesta, no acá: UseExceptionHandler limpia
            // la respuesta antes de armar el 500 y se llevaría puesto todo lo que hubiéramos puesto antes.
            context.Response.OnStarting(static state =>
            {
                var headers = ((HttpResponse)state).Headers;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                headers.ContentSecurityPolicy = ContentSecurityPolicy;

                return Task.CompletedTask;
            }, context.Response);

            await next(context);
        });

        return app;
    }
}
