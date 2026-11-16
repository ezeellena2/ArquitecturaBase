using System.Threading.RateLimiting;
using ArquitecturaBase.Application.Common.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.RateLimiting;

/// <summary>
/// Los límites que valen para todos los pedidos, además de las políticas de cada ruta (N6):
/// <list type="bullet">
/// <item>una ventana por IP para las rutas anónimas que no tienen política propia: /connect, Google, los métodos de
/// ingreso, la presentación y /health;</item>
/// <item>un tope de pedidos a la vez en la copia, con una cola corta, salvo los probes (/health y /alive): si el orquestador
/// recibiera un 429, sacaría la copia del tráfico o la reiniciaría justo cuando está cargada;</item>
/// <item>el tope del cuerpo en Kestrel.</item>
/// </list>
/// Corre antes de la autenticación, porque OpenIddict ya consulta la base ahí (el cliente de /connect y la entrada de cada
/// token): por eso lo anónimo se decide por la ruta y no por quién pide.
/// </summary>
internal static class GlobalRequestLimits
{
    public static IServiceCollection AddGlobalRequestLimits(this IServiceCollection services)
    {
        services.AddOptions<RequestLimitsOptions>()
            .BindConfiguration(RequestLimitsOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<KestrelServerOptions>()
            .Configure<IOptions<RequestLimitsOptions>>((kestrel, limits) =>
                kestrel.Limits.MaxRequestBodySize = limits.Value.MaxRequestBodyBytes);

        services.AddSingleton<GlobalRequestLimiter>();

        return services;
    }

    /// <summary>Va antes de UseAuthentication y después de UseStatusCodePages, para que el 429 salga como los demás.</summary>
    public static WebApplication UseGlobalRequestLimits(this WebApplication app)
    {
        var limiter = app.Services.GetRequiredService<GlobalRequestLimiter>();

        app.Use(async (context, next) =>
        {
            RateLimitLease lease;
            try
            {
                lease = await limiter.Limiter.AcquireAsync(context, 1, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // Se fue mientras esperaba en la cola: no hay a quién responderle.
                return;
            }

            using (lease)
            {
                if (!lease.IsAcquired)
                {
                    await RateLimitingExtensions.WriteRejectionAsync(context, lease);
                    return;
                }

                await next(context);
            }
        });

        return app;
    }
}

/// <summary>El limitador de <see cref="GlobalRequestLimits"/>: uno por copia, con la ventana primero y el tope después.</summary>
internal sealed class GlobalRequestLimiter : IDisposable
{
    private const string InFlightPartition = "in-flight";

    public GlobalRequestLimiter(IOptions<RequestLimitsOptions> options)
    {
        var limits = options.Value;

        Limiter = PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<HttpContext, string>(context => AnonymousWindow(context, limits)),
            PartitionedRateLimiter.Create<HttpContext, string>(context => IsProbe(context)
                ? RateLimitPartition.GetNoLimiter(string.Empty)
                : RateLimitPartition.GetConcurrencyLimiter(InFlightPartition, _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = limits.ConcurrentRequests,
                    QueueLimit = limits.QueuedRequests,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                })));
    }

    public PartitionedRateLimiter<HttpContext> Limiter { get; }

    public void Dispose() => Limiter.Dispose();

    private static RateLimitPartition<string> AnonymousWindow(HttpContext context, RequestLimitsOptions limits)
    {
        // Sin endpoint son archivos del SPA o rutas que no existen: no tocan la base. Con política propia, ya tiene la suya.
        var endpoint = context.GetEndpoint();
        if (endpoint is null
            || endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>() is not null
            || !IsAnonymous(endpoint))
        {
            return RateLimitPartition.GetNoLimiter(string.Empty);
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            AddressPartition.Of(context.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.AnonymousPermitLimit,
                Window = TimeSpan.FromSeconds(limits.AnonymousWindowSeconds),
                QueueLimit = 0,
            });
    }

    /// <summary>Una ruta que no pide sesión: sin [Authorize], o con [AllowAnonymous].</summary>
    private static bool IsAnonymous(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
        || endpoint.Metadata.GetMetadata<IAuthorizeData>() is null;

    private static bool IsProbe(HttpContext context) =>
        context.Request.Path.StartsWithSegments(Extensions.HealthEndpointPath)
        || context.Request.Path.StartsWithSegments(Extensions.AlivenessEndpointPath);
}
