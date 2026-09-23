namespace ArquitecturaBase.Api.Hosting;

internal static class SpaExtensions
{
    /// <summary>
    /// Rutas que atiende el backend: nunca caen en el index.html del SPA.
    /// <para>
    /// Es una lista a mano y hay que mantenerla. Un prefijo de backend nuevo que no esté acá solo se nota en sus
    /// rutas inexistentes: en vez del 404 con ProblemDetails devuelven el index.html con 200, y el cliente recibe
    /// HTML donde esperaba JSON. Al agregar un prefijo, sumalo acá y a `Backend_routes_keep_returning_a_problem`
    /// (SpaHostingTests). El proxy de desarrollo del front (`vite.config.ts`, `server.proxy`) lleva la misma
    /// lista: los dos se cambian juntos.
    /// </para>
    /// </summary>
    private static readonly string[] BackendPrefixes =
        ["/api", "/account", "/connect", "/signin-google", "/.well-known", "/webhooks", "/swagger", "/openapi", "/health", "/alive"];

    /// <summary>
    /// Sirve el index.html del build del SPA en las rutas del navegador, para que las resuelva su router
    /// (sección 5.1). Va después de UseStaticFiles: los archivos que existen se sirven como archivos.
    /// <para>
    /// Es un middleware y no un MapFallback a propósito. Un endpoint catch-all entra en el grafo del routing y se
    /// come el 405 del método incorrecto y el 415 del contenido que no es JSON de *toda* la Api: como acepta
    /// cualquier método y cualquier contenido, reemplaza a los endpoints de rechazo que arma el routing. Desde
    /// acá el routing queda intacto y solo se atienden los pedidos que no matchearon nada.
    /// </para>
    /// </summary>
    public static WebApplication UseSpaFallback(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var webRoot = app.Environment.WebRootPath;
        var index = string.IsNullOrEmpty(webRoot) ? null : Path.Combine(webRoot, "index.html");

        if (index is null || !File.Exists(index))
        {
            // En desarrollo el SPA lo sirve Vite: no hay nada que servir desde acá.
            return app;
        }

        app.Use(async (context, next) =>
        {
            if (!IsSpaRequest(context))
            {
                await next(context);

                return;
            }

            // El index.html nombra los assets por hash: si se cachea, después de un despliegue el navegador pide
            // archivos que ya no existen.
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.ContentType = "text/html";

            await context.Response.SendFileAsync(index, context.RequestAborted);
        });

        return app;
    }

    private static bool IsSpaRequest(HttpContext context)
    {
        // Con un endpoint elegido el pedido ya tiene dueño: un endpoint de la Api, un archivo estático o el
        // rechazo que arma el routing (405, 415). Ninguno es asunto del SPA.
        if (context.GetEndpoint() is not null)
        {
            return false;
        }

        // Solo una navegación del navegador abre el SPA. Un POST a una ruta que no existe es un 404.
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            return false;
        }

        var path = context.Request.Path;

        // Un archivo que no está es un 404, no el index.html: si /assets/main-<hash>.js devolviera HTML, el error
        // en el navegador sería "el módulo no se pudo cargar" en lugar de un 404 claro.
        return !LooksLikeAFile(path)
            && !BackendPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeAFile(PathString path)
    {
        var value = path.Value ?? string.Empty;

        return value.AsSpan(value.LastIndexOf('/') + 1).Contains('.');
    }
}
