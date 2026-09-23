using System.Net;
using ArquitecturaBase.Application.Abstractions.Phones;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Phones;
using ArquitecturaBase.Infrastructure.WhatsApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// Nunca se registra el token, el código, la URL del enlace ni el número completo (sección 9 del spec y reglas del
/// plan). Corre la registración real, con la resiliencia de ServiceDefaults y todos los niveles de log, para que
/// también cuenten los logs del HttpClient y de la resiliencia.
/// </summary>
public sealed class WhatsAppLogPrivacyTests
{
    private const string AccessToken = "test-access-token";
    private const string Code = "482913";
    private const string LinkToken = "test-link-token";
    private const string LinkUrl = "https://localhost:5173/ingresar#t=" + LinkToken;

    private static readonly PhoneNumber To = PhoneNumber.Create("+5493515550303").Value;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_log_carries_the_token_the_code_the_link_or_the_full_number()
    {
        // En el orden de la cola: el código choca con el token, el enlace con la ventana de 24 horas, los botones
        // fallan tres veces, el segundo código choca con un permiso que falta y el texto sale.
        var handler = new FakeMetaHandler(call => call switch
        {
            1 => FakeMetaHandler.Error(HttpStatusCode.Unauthorized, 190),
            2 => FakeMetaHandler.Error(HttpStatusCode.BadRequest, 131047),
            3 or 4 or 5 => FakeMetaHandler.Error(HttpStatusCode.InternalServerError, 131000),
            6 => FakeMetaHandler.Error(HttpStatusCode.Forbidden, 10),
            _ => FakeMetaHandler.Success(),
        });
        var clock = new RetrySkippingTimeProvider(TimeSpan.FromSeconds(new WhatsAppOptions().RetryDelaySeconds));

        await using var provider = BuildProvider(handler, clock);
        var collector = provider.GetFakeLogCollector();
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        foreach (var service in hostedServices)
        {
            await service.StartAsync(Ct);
        }

        try
        {
            var outbox = provider.GetRequiredService<IWhatsAppOutbox>();
            Assert.True(outbox.TryEnqueue(new WhatsAppLoginCodeMessage(To, "es", Code)));
            Assert.True(outbox.TryEnqueue(new WhatsAppLinkButtonMessage(To, "Tocá Entrar para ingresar.", "Entrar", LinkUrl)));
            Assert.True(outbox.TryEnqueue(new WhatsAppReplyButtonsMessage(To, "¿Creamos tu cuenta?", [new WhatsAppReplyButton("signup:yes", "Sí")])));
            Assert.True(outbox.TryEnqueue(new WhatsAppLoginCodeMessage(To, "en", Code)));
            Assert.True(outbox.TryEnqueue(new WhatsAppTextMessage(To, "Listo.")));

            await WaitUntilAsync(() =>
            {
                // Las esperas de los reintentos pasan en el reloj de mentira, sin esperar de verdad.
                clock.SkipRetryDelays();

                return handler.Calls == 7
                    && collector.GetSnapshot().Any(record => record.Message.Contains("was sent", StringComparison.Ordinal));
            });
        }
        finally
        {
            foreach (var service in hostedServices)
            {
                await service.StopAsync(Ct);
            }
        }

        var logged = collector.GetSnapshot()
            .Select(record => string.Join(
                "\n",
                [
                    record.Message,
                    record.Exception?.ToString() ?? string.Empty,
                    .. record.StructuredState?.Select(pair => pair.Value ?? string.Empty) ?? [],
                    .. record.Scopes.Select(scope => scope?.ToString() ?? string.Empty),
                ]))
            .ToList();

        Assert.NotEmpty(logged);
        Assert.Contains(logged, text => text.Contains("+54 9 351 •••• 0303", StringComparison.Ordinal));

        string[] secrets = [AccessToken, Code, LinkToken, LinkUrl, To.Value, To.Value[1..]];
        var leaks = secrets
            .SelectMany(secret => logged.Where(text => text.Contains(secret, StringComparison.Ordinal)).Select(text => secret + " in: " + text))
            .ToList();

        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine + "---" + Environment.NewLine, leaks));
    }

    private static ServiceProvider BuildProvider(FakeMetaHandler handler, TimeProvider clock)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WhatsApp:PhoneNumberId"] = "1234567890",
                ["WhatsApp:AccessToken"] = AccessToken,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddFakeLogging());
        services.AddSingleton(clock);
        services.AddSingleton<IPhoneNumberParser, LibPhoneNumberParser>();

        // Lo mismo que ServiceDefaults le pone a todos los HttpClient.
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        services.AddWhatsApp(configuration);
        services.AddHttpClient(WhatsAppRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);

        return services.BuildServiceProvider();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    /// <summary>
    /// Un reloj de mentira que solo avanza cuando la cola espera para reintentar: esos timers son los únicos con la
    /// espera de los reintentos. Los timeouts de la resiliencia usan el mismo reloj, pero sus timers viven lo que dura
    /// un pedido a Meta, y mientras tanto el reloj no se mueve: nunca vencen a mitad de un envío.
    /// </summary>
    private sealed class RetrySkippingTimeProvider(TimeSpan retryDelay) : FakeTimeProvider
    {
        private int _pendingRetryDelays;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);

            if (dueTime == retryDelay)
            {
                Interlocked.Increment(ref _pendingRetryDelays);
            }

            return timer;
        }

        /// <summary>Adelanta el reloj una vez por cada espera de reintento que ya empezó.</summary>
        public void SkipRetryDelays()
        {
            while (Volatile.Read(ref _pendingRetryDelays) > 0)
            {
                Interlocked.Decrement(ref _pendingRetryDelays);
                Advance(retryDelay);
            }
        }
    }
}
