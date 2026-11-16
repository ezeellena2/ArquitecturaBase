using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Servicios comunes de Aspire: service discovery, health checks y OpenTelemetry.
// Basado en la plantilla aspire-servicedefaults 13.5.4: https://aka.ms/aspire/service-defaults. A diferencia de la
// plantilla, los HttpClient no reciben la resiliencia estándar (N18): reintenta un POST ante un 5xx o un plazo vencido, y
// eso duplica un efecto en el proveedor. Cada cliente declara la suya.
public static class Extensions
{
    public const string HealthEndpointPath = "/health";
    public const string AlivenessEndpointPath = "/alive";
    private static readonly string[] LiveTags = ["live"];

    /// <summary>El ambiente del arnés de integración.</summary>
    private const string TestingEnvironment = "Testing";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        // Fuera de Development y Testing, la consola escribe una línea JSON por log con sus scopes (N10): el traceId y la
        // ruta que pone ASP.NET Core en cada pedido, y el id de quien pide. Así el código que ve la persona en un error se
        // encuentra en los logs de la plataforma aunque no haya exportador.
        if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment(TestingEnvironment))
        {
            builder.Logging.AddJsonConsole(console =>
            {
                console.IncludeScopes = true;
                console.UseUtcTimestamp = true;
                console.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            });
        }

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(options =>
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                    .AddHttpClientInstrumentation()
                    // Spans de cada comando SQL, dentro de la traza del request que los provocó.
                    .AddNpgsql();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), LiveTags);

        // La readiness se guarda unos segundos (N6): se envuelve el servicio que registró AddHealthChecks. Los AddHealthChecks
        // que vienen después (la base, Redis, los módulos) suman checks y no lo reemplazan, porque lo registran con TryAdd.
        var standard = builder.Services.Single(service => service.ServiceType == typeof(HealthCheckService));
        builder.Services.Remove(standard);
        builder.Services.AddSingleton<HealthCheckService>(services => new CachedHealthCheckService(
            (HealthCheckService)ActivatorUtilities.CreateInstance(services, standard.ImplementationType!),
            services.GetService<TimeProvider>() ?? TimeProvider.System));

        return builder;
    }

    // En todos los entornos: los probes del orquestador los consultan en producción, que es donde importan. La
    // respuesta por defecto es una sola palabra ("Healthy"/"Unhealthy"), sin el detalle de cada check: no cuenta
    // qué dependencias tiene la aplicación ni por qué falló.
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Readiness: corre todos los checks, incluido el de la base. Si falla, que no nos manden tráfico.
        app.MapHealthChecks(HealthEndpointPath);

        // Liveness: solo los checks con el tag "live", que no tocan dependencias externas. Responde si el proceso
        // sigue en pie; si esto falla, reiniciar el contenedor es la reacción correcta.
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live")
        });

        return app;
    }
}
