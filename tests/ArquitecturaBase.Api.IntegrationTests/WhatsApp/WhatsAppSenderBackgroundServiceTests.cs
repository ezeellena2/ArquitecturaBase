using System.Collections.Concurrent;
using System.Globalization;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Application.Features.WhatsApp.RecordOutboundMessage;
using ArquitecturaBase.Application.Features.WhatsApp.RecordUnsentMessage;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Phones;
using ArquitecturaBase.Infrastructure.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// La cola de salida: reintenta lo que Meta rechaza por un rato (131056, 130429, 5xx) con espera, no insiste con lo que
/// no va a cambiar, y un token inválido o sin permisos se ve en la salud de la app (sección 9 del spec). El reloj es de
/// mentira: las esperas pasan cuando el test adelanta el reloj.
/// </summary>
public sealed class WhatsAppSenderBackgroundServiceTests
{
    private static readonly PhoneNumber Ana = PhoneNumber.Create("+5493515550101").Value;
    private static readonly PhoneNumber Beto = PhoneNumber.Create("+5493515550202").Value;

    /// <summary>La espera de los reintentos con la configuración de fábrica.</summary>
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(new WhatsAppOptions().RetryDelaySeconds);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Meta limita a un mensaje cada 6 segundos a la misma persona: antes de eso, reintentar es perder el intento. Es la
    /// espera por defecto, y la configuración no acepta una menor (<see cref="WhatsAppRegistrationTests"/>).
    /// </summary>
    [Fact]
    public async Task Pair_rate_limit_is_retried_after_six_seconds_and_not_before()
    {
        var clock = new TimerCountingTimeProvider();
        var client = new ScriptedCloudClient().Script(Ana, Failed(WhatsAppSendFailure.PairRateLimited, 131056), Sent());

        await RunAsync(client, clock, async (outbox, _) =>
        {
            outbox.TryEnqueue(Text(Ana));
            await WaitUntilAsync(() => clock.TimersCreated == 1);

            clock.Advance(TimeSpan.FromSeconds(6) - TimeSpan.FromMilliseconds(1));
            await Task.Delay(100, Ct);
            Assert.Equal(1, client.AttemptsFor(Ana));

            clock.Advance(TimeSpan.FromMilliseconds(1));
            await WaitUntilAsync(() => client.AttemptsFor(Ana) == 2);
        });

        Assert.Single(client.Delivered);
    }

    [Fact]
    public async Task Transient_failures_wait_the_configured_delay_before_retrying()
    {
        var clock = new TimerCountingTimeProvider();
        var client = new ScriptedCloudClient().Script(Ana, Failed(WhatsAppSendFailure.Transient), Sent());

        await RunAsync(
            client,
            clock,
            async (outbox, _) =>
            {
                outbox.TryEnqueue(Text(Ana));
                await WaitUntilAsync(() => clock.TimersCreated == 1);

                clock.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromMilliseconds(1));
                await Task.Delay(100, Ct);
                Assert.Equal(1, client.AttemptsFor(Ana));

                clock.Advance(TimeSpan.FromMilliseconds(1));
                await WaitUntilAsync(() => client.AttemptsFor(Ana) == 2);
            },
            retryDelaySeconds: 10);

        Assert.Single(client.Delivered);
    }

    [Fact]
    public async Task After_three_attempts_the_message_is_dropped_and_the_next_one_is_sent()
    {
        var clock = new TimerCountingTimeProvider();
        var client = new ScriptedCloudClient()
            .Script(Ana, Failed(WhatsAppSendFailure.RateLimited, 130429), Failed(WhatsAppSendFailure.Transient), Failed(WhatsAppSendFailure.Transient), Sent());

        await RunAsync(client, clock, async (outbox, _) =>
        {
            outbox.TryEnqueue(Text(Ana));
            outbox.TryEnqueue(Text(Beto));

            // Dos esperas entre los tres intentos; después del tercero ya no se espera.
            await WaitUntilAsync(() => clock.TimersCreated == 1);
            clock.Advance(DefaultRetryDelay);
            await WaitUntilAsync(() => clock.TimersCreated == 2);
            clock.Advance(DefaultRetryDelay);

            await WaitUntilAsync(() => client.Delivered.Count == 1);
        });

        Assert.Equal(WhatsAppSenderBackgroundService.MaxAttempts, client.AttemptsFor(Ana));
        Assert.Equal(Beto, Assert.Single(client.Delivered).To);
    }

    [Theory]
    [InlineData("OutsideCustomerServiceWindow")]
    [InlineData("Undeliverable")]
    [InlineData("RecipientNotAllowed")]
    [InlineData("InvalidToken")]
    [InlineData("MissingPermission")]
    [InlineData("Other")]
    public async Task Failures_that_would_fail_again_are_not_retried(string failure)
    {
        var clock = new TimerCountingTimeProvider();
        var client = new ScriptedCloudClient().Script(Ana, Failed(Enum.Parse<WhatsAppSendFailure>(failure)), Sent());

        await RunAsync(client, clock, async (outbox, _) =>
        {
            outbox.TryEnqueue(Text(Ana));
            outbox.TryEnqueue(Text(Beto));

            // Un reintento se quedaría esperando al reloj de mentira: el timer lo delata sin esperar al timeout.
            await WaitUntilAsync(() => client.Delivered.Count == 1 || clock.TimersCreated > 0);
        });

        Assert.Equal(0, clock.TimersCreated);
        Assert.Equal(1, client.AttemptsFor(Ana));
        Assert.Equal(Beto, Assert.Single(client.Delivered).To);
    }

    /// <summary>
    /// El token rechazado y el token sin permisos son configuración: van al log como error y dejan la salud en Degraded,
    /// que dice cuál de los dos fue y que después de arreglarlo hay que reiniciar la Api (las opciones se leen al
    /// arrancar). Un envío aceptado la devuelve a Healthy.
    /// </summary>
    [Theory]
    [InlineData("InvalidToken", 190, "rejected the access token")]
    [InlineData("MissingPermission", 10, "lacks a permission")]
    public async Task A_token_problem_is_logged_as_an_error_and_degrades_health_until_a_message_is_sent(
        string failure,
        int metaCode,
        string whichProblem)
    {
        var client = new ScriptedCloudClient().Script(Ana, Failed(Enum.Parse<WhatsAppSendFailure>(failure), metaCode));
        var logger = new FakeLogger<WhatsAppSenderBackgroundService>();
        var health = new WhatsAppHealth();
        var check = new WhatsAppHealthCheck(new WhatsAppAvailability(IsEnabled: true), health);

        await RunAsync(client, new FakeTimeProvider(), async (outbox, _) =>
        {
            outbox.TryEnqueue(Text(Ana));
            await WaitUntilAsync(() => client.AttemptsFor(Ana) == 1 && health.TokenProblem is not null);

            var degraded = await check.CheckHealthAsync(new HealthCheckContext(), Ct);
            Assert.Equal(HealthStatus.Degraded, degraded.Status);
            Assert.Contains(whichProblem, degraded.Description, StringComparison.Ordinal);
            Assert.Contains("restart the Api", degraded.Description, StringComparison.Ordinal);

            outbox.TryEnqueue(Text(Beto));
            await WaitUntilAsync(() => client.Delivered.Count == 1 && health.TokenProblem is null);
        }, logger, health);

        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext(), Ct)).Status);

        var error = Assert.Single(logger.Collector.GetSnapshot(), record => record.Level == LogLevel.Error);
        Assert.Contains(whichProblem, error.Message, StringComparison.Ordinal);
        Assert.Contains("restart the Api", error.Message, StringComparison.Ordinal);

        // El fragmento entero: el "10" solo ya está en el número enmascarado de Ana ("•••• 0101").
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"(Meta code {metaCode})"),
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logs_show_the_masked_number_and_never_the_full_one()
    {
        var client = new ScriptedCloudClient().Script(Ana, Failed(WhatsAppSendFailure.OutsideCustomerServiceWindow, 131047));
        var logger = new FakeLogger<WhatsAppSenderBackgroundService>();

        await RunAsync(client, new FakeTimeProvider(), async (outbox, _) =>
        {
            outbox.TryEnqueue(Text(Ana));
            outbox.TryEnqueue(Text(Beto));
            await WaitUntilAsync(() => client.Delivered.Count == 1 && logger.Collector.Count >= 2);
        }, logger);

        var messages = logger.Collector.GetSnapshot().Select(record => record.Message).ToList();

        Assert.Contains(messages, message => message.Contains("+54 9 351 •••• 0101", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, message => message.Contains(Ana.Value, StringComparison.Ordinal));
        Assert.DoesNotContain(messages, message => message.Contains(Beto.Value, StringComparison.Ordinal));
    }

    /// <summary>
    /// Cada mensaje que sale se guarda en el historial con el id que devolvió Meta, para cruzarlo con los estados del
    /// webhook (sección 9 del spec). Lo que no salió no se guarda.
    /// </summary>
    [Fact]
    public async Task A_sent_message_is_recorded_with_the_id_meta_returned_and_one_not_sent_is_not()
    {
        var client = new ScriptedCloudClient().Script(Ana, Failed(WhatsAppSendFailure.Undeliverable, 131026));
        var recorder = new RecordingHandler<RecordOutboundWhatsAppMessageCommand>();

        await RunAsync(client, new FakeTimeProvider(), async (outbox, _) =>
        {
            outbox.TryEnqueue(Text(Ana));
            outbox.TryEnqueue(Text(Beto));
            await WaitUntilAsync(() => !recorder.Recorded.IsEmpty);
        }, recorder: recorder);

        var recorded = Assert.Single(recorder.Recorded);
        Assert.Equal("wamid.test", recorded.WaMessageId);
        Assert.Equal(Beto, recorded.Message.To);
    }

    /// <summary>
    /// Lo que no salió se informa, una sola vez y después del último intento, sea cual sea el motivo: lo rechazó Meta, el
    /// token no sirve, el cliente falló de una forma que no esperaba o se agotaron los reintentos. Así una invitación
    /// queda como fallida en lugar de pendiente para siempre. Lo que salió no se informa como fallido.
    /// </summary>
    [Fact]
    public async Task A_message_that_was_not_sent_is_reported_once_whatever_the_reason()
    {
        var (carla, dario, elena) = (
            PhoneNumber.Create("+5493515550303").Value,
            PhoneNumber.Create("+5493515550404").Value,
            PhoneNumber.Create("+5493515550505").Value);
        var clock = new TimerCountingTimeProvider();
        var client = new ScriptedCloudClient()
            .Script(Ana, Failed(WhatsAppSendFailure.Other, 132001))
            .Script(Beto, Failed(WhatsAppSendFailure.InvalidToken, 190))
            .Throws(carla)
            .Script(dario, Failed(WhatsAppSendFailure.Transient), Failed(WhatsAppSendFailure.Transient), Failed(WhatsAppSendFailure.Transient));
        var unsent = new RecordingHandler<RecordUnsentWhatsAppMessageCommand>();

        await RunAsync(client, clock, async (outbox, _) =>
        {
            outbox.TryEnqueue(Text(Ana));
            outbox.TryEnqueue(Text(Beto));
            outbox.TryEnqueue(Text(carla));
            outbox.TryEnqueue(Text(dario));
            await WaitUntilAsync(() => clock.TimersCreated == 1);
            clock.Advance(DefaultRetryDelay);
            await WaitUntilAsync(() => clock.TimersCreated == 2);
            clock.Advance(DefaultRetryDelay);
            outbox.TryEnqueue(Text(elena));
            await WaitUntilAsync(() => client.Delivered.Count == 1);
        }, unsent: unsent);

        Assert.Equal(1, client.AttemptsFor(carla));
        Assert.Equal([Ana, Beto, carla, dario], unsent.Recorded.Select(command => command.Message.To));
        Assert.Equal(elena, Assert.Single(client.Delivered).To);
    }

    /// <summary>
    /// Si informar que no salió falla, queda un aviso con el número enmascarado y la cola sigue. El catch es lo único que
    /// lo sostiene: una excepción que se escapara de ExecuteAsync pararía el sender y, con el comportamiento por defecto
    /// de BackgroundService (StopHost), la Api entera.
    /// </summary>
    [Fact]
    public async Task A_failure_to_report_a_message_that_was_not_sent_is_logged_and_the_queue_goes_on()
    {
        var client = new ScriptedCloudClient().Script(Ana, Failed(WhatsAppSendFailure.Other, 132001));
        var unsent = new RecordingHandler<RecordUnsentWhatsAppMessageCommand> { Fails = command => command.Message.To == Ana };
        var logger = new FakeLogger<WhatsAppSenderBackgroundService>();

        await RunAsync(client, new FakeTimeProvider(), async (outbox, _) =>
        {
            outbox.TryEnqueue(Text(Ana));
            outbox.TryEnqueue(Text(Beto));
            await WaitUntilAsync(() => client.Delivered.Count == 1);
        }, logger, unsent: unsent);

        Assert.Equal(1, client.AttemptsFor(Ana));
        Assert.Equal(Beto, Assert.Single(client.Delivered).To);
        Assert.Empty(unsent.Recorded);

        var warning = Assert.Single(
            logger.Collector.GetSnapshot(),
            record => record.Message.Contains("could not be saved", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("was not sent", warning.Message, StringComparison.Ordinal);
        Assert.Contains("+54 9 351 •••• 0101", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Ana.Value, warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Si guardar falla, el mensaje ya salió: no se vuelve a mandar (la persona lo recibiría dos veces), queda un aviso
    /// con el número enmascarado y la cola sigue.
    /// </summary>
    [Fact]
    public async Task A_failure_to_record_does_not_send_the_message_again()
    {
        var clock = new TimerCountingTimeProvider();
        var client = new ScriptedCloudClient();
        var recorder = new RecordingHandler<RecordOutboundWhatsAppMessageCommand> { Fails = command => command.Message.To == Ana };
        var logger = new FakeLogger<WhatsAppSenderBackgroundService>();

        await RunAsync(client, clock, async (outbox, _) =>
        {
            outbox.TryEnqueue(Text(Ana));
            outbox.TryEnqueue(Text(Beto));
            await WaitUntilAsync(() => client.Delivered.Count == 2 && !recorder.Recorded.IsEmpty);
        }, logger, recorder: recorder);

        Assert.Equal(1, client.AttemptsFor(Ana));
        Assert.Equal(0, clock.TimersCreated);
        Assert.Equal(Beto, Assert.Single(recorder.Recorded).Message.To);

        var warning = Assert.Single(logger.Collector.GetSnapshot(), record => record.Level == LogLevel.Warning);
        Assert.Contains("could not be saved", warning.Message, StringComparison.Ordinal);
        Assert.Contains("+54 9 351 •••• 0101", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Ana.Value, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_is_healthy_and_says_disabled_when_whatsapp_is_off()
    {
        var check = new WhatsAppHealthCheck(new WhatsAppAvailability(IsEnabled: false), new WhatsAppHealth());

        var result = await check.CheckHealthAsync(new HealthCheckContext(), Ct);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("disabled", result.Description);
    }

    /// <summary>
    /// Sin <paramref name="retryDelaySeconds"/>, la espera de fábrica (<see cref="DefaultRetryDelay"/>). Sin
    /// <paramref name="recorder"/>, uno que guarda en memoria: el historial de verdad se prueba contra la base, en
    /// <see cref="WhatsAppBotTests"/>.
    /// </summary>
    private static async Task RunAsync(
        ScriptedCloudClient client,
        TimeProvider clock,
        Func<WhatsAppOutbox, WhatsAppHealth, Task> act,
        ILogger<WhatsAppSenderBackgroundService>? logger = null,
        WhatsAppHealth? health = null,
        int? retryDelaySeconds = null,
        RecordingHandler<RecordOutboundWhatsAppMessageCommand>? recorder = null,
        RecordingHandler<RecordUnsentWhatsAppMessageCommand>? unsent = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWhatsAppCloudClient>(client);
        services.AddSingleton<ICommandHandler<RecordOutboundWhatsAppMessageCommand>>(recorder ?? new RecordingHandler<RecordOutboundWhatsAppMessageCommand>());
        services.AddSingleton<ICommandHandler<RecordUnsentWhatsAppMessageCommand>>(unsent ?? new RecordingHandler<RecordUnsentWhatsAppMessageCommand>());
        await using var provider = services.BuildServiceProvider();

        var options = Options.Create(new WhatsAppOptions
        {
            PhoneNumberId = "1234567890",
            AccessToken = "test-access-token",
            RetryDelaySeconds = retryDelaySeconds ?? new WhatsAppOptions().RetryDelaySeconds,
        });
        health ??= new WhatsAppHealth();
        var outbox = new WhatsAppOutbox(options, NullLogger<WhatsAppOutbox>.Instance);
        using var service = new WhatsAppSenderBackgroundService(
            outbox,
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            new LibPhoneNumberParser(),
            health,
            clock,
            logger ?? NullLogger<WhatsAppSenderBackgroundService>.Instance);

        await service.StartAsync(Ct);

        try
        {
            await act(outbox, health);
        }
        finally
        {
            await service.StopAsync(Ct);
        }
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

    private static WhatsAppTextMessage Text(PhoneNumber to) => new(to, "Hola");

    private static WhatsAppSendResult Sent() => WhatsAppSendResult.Sent("wamid.test");

    private static WhatsAppSendResult Failed(WhatsAppSendFailure failure, int? metaErrorCode = null) =>
        WhatsAppSendResult.Failed(failure, metaErrorCode);

    /// <summary>
    /// Cuenta los timers: Task.Delay con un TimeProvider crea uno, así el test sabe que el envío ya está esperando y
    /// puede adelantar el reloj sin carrera.
    /// </summary>
    private sealed class TimerCountingTimeProvider : FakeTimeProvider
    {
        private int _timersCreated;

        public int TimersCreated => Volatile.Read(ref _timersCreated);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            Interlocked.Increment(ref _timersCreated);

            return timer;
        }
    }

    /// <summary>
    /// El caso de uso que guarda el historial, en memoria. Con <see cref="Fails"/>, falla para esos comandos, como si la
    /// base no respondiera. Es genérico a propósito: el arnés registra todos los handlers concretos de este ensamblado
    /// (AddFeaturesFromAssembly), y uno concreto reemplazaría al de verdad en la Api de todos los tests.
    /// </summary>
    private sealed class RecordingHandler<TCommand> : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public ConcurrentQueue<TCommand> Recorded { get; } = new();

        public Func<TCommand, bool> Fails { get; init; } = _ => false;

        public Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
        {
            if (Fails(command))
            {
                throw new InvalidOperationException("The database is down.");
            }

            Recorded.Enqueue(command);

            return Task.FromResult(Result.Success());
        }
    }

    /// <summary>
    /// El cliente de Meta con las respuestas que dice cada test, por destinatario; sin guion, todo sale bien. Con
    /// <see cref="Throws"/>, lanza para ese destinatario: el cliente de verdad traduce las fallas de Meta y de la red, así
    /// que es lo que pasaría con un bug.
    /// </summary>
    private sealed class ScriptedCloudClient : IWhatsAppCloudClient
    {
        private readonly ConcurrentDictionary<PhoneNumber, ConcurrentQueue<WhatsAppSendResult>> _scripts = new();
        private readonly ConcurrentDictionary<PhoneNumber, int> _attempts = new();
        private readonly ConcurrentDictionary<PhoneNumber, bool> _throwing = new();

        public ConcurrentQueue<WhatsAppOutboundMessage> Delivered { get; } = new();

        public ScriptedCloudClient Script(PhoneNumber to, params WhatsAppSendResult[] results)
        {
            _scripts[to] = new ConcurrentQueue<WhatsAppSendResult>(results);

            return this;
        }

        public ScriptedCloudClient Throws(PhoneNumber to)
        {
            _throwing[to] = true;

            return this;
        }

        public int AttemptsFor(PhoneNumber to) => _attempts.GetValueOrDefault(to);

        public Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
        {
            _attempts.AddOrUpdate(message.To, 1, (_, previous) => previous + 1);

            if (_throwing.ContainsKey(message.To))
            {
                throw new InvalidOperationException("The client has a bug.");
            }

            var result = _scripts.TryGetValue(message.To, out var script) && script.TryDequeue(out var next) ? next : Sent();

            if (result.IsSent)
            {
                Delivered.Enqueue(message);
            }

            return Task.FromResult(result);
        }
    }
}
