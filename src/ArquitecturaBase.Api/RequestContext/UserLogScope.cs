using ArquitecturaBase.Application.Interfaces.Integrations.Request;

namespace ArquitecturaBase.Api.RequestContext;

/// <summary>
/// Los logs de un pedido con sesión llevan el id de quien pide en un scope (N10): con el código que ve la persona (el
/// traceId, que la consola escribe con los scopes del pedido) se encuentran el error, la ruta y la cuenta. Sólo el id,
/// nunca el correo ni el número.
/// </summary>
internal static class UserLogScope
{
    private static readonly Func<ILogger, Guid, IDisposable?> BeginUserScope =
        LoggerMessage.DefineScope<Guid>("UserId:{UserId}");

    /// <summary>
    /// Va justo después de UseAuthentication: antes no se sabe quién pide. Una excepción sale del scope antes de llegar al
    /// manejador de errores, que abre el suyo con <see cref="Begin"/>.
    /// </summary>
    public static WebApplication UseUserLogScope(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(UserLogScope).FullName!);

        app.Use(async (context, next) =>
        {
            using (Begin(logger, context))
            {
                await next(context);
            }
        });

        return app;
    }

    /// <summary>El scope con el id de quien pide; sin sesión, ninguno.</summary>
    public static IDisposable? Begin(ILogger logger, HttpContext context)
    {
        var userId = context.RequestServices.GetRequiredService<ICurrentUser>().UserId;

        return userId is null ? null : BeginUserScope(logger, userId.Value);
    }
}
