using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Delivery;
using ArquitecturaBase.Infrastructure.Emails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

/// <summary>
/// N5 and N8: the email worker sends one attempt per claim and settles each email by what the sender says: what failed before the
/// body goes back to the queue to wait, until it expires; what the server refused or may have received is not sent again; what
/// the shutdown cut before the body goes back to the queue. The durable queue under it is in TransactionalEmailQueueTests and
/// EmailQueueShutdownTests.
/// </summary>
public sealed class EmailBackgroundServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_send_that_failed_before_the_body_goes_back_to_wait_and_the_next_email_is_sent()
    {
        var down = Message("down@example.com");
        var source = new ListSource(down, Message("ana@example.com"));
        var sender = new ScriptedSender(message => message.To == down.To
            ? EmailSendResult.Failed(EmailSendOutcome.ConnectionFailed)
            : Sent(message));
        var logger = new FakeLogger<EmailBackgroundService>();
        using var outcomes = new OutcomeListener();

        await RunAsync(source, sender, async () => await WaitUntilAsync(() => source.Events.Count == 2), logger: logger, outcomes: outcomes);

        Assert.Equal(["retry:down@example.com", "completed:ana@example.com"], source.Events);
        Assert.Equal([(TimeSpan.FromSeconds(2), EmailBackgroundService.LongestRetryWait)], source.Waits);
        Assert.Equal(1, sender.AttemptsFor(down.To));
        Assert.Equal(["retry", "sent"], outcomes.Outcomes);
        var retry = Assert.Single(logger.Collector.GetSnapshot(), record => record.Message.Contains("retried", StringComparison.Ordinal));
        Assert.Contains(down.Id.ToString(), retry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), record => record.Message.Contains("@example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_invitation_that_would_expire_before_its_next_attempt_is_dropped_as_failed()
    {
        var invitation = Message("laura@example.com") with { Invitation = new EmailInvitationReference(Guid.CreateVersion7(), Guid.CreateVersion7()) };
        var source = new ListSource(invitation) { Retry = DeliveryRetry.Expired };
        var deliveries = new RecordingDeliveryService();
        using var outcomes = new OutcomeListener();

        await RunAsync(source, new ScriptedSender(_ => EmailSendResult.Failed(EmailSendOutcome.TemporarilyRejected, 421)),
            async () => await WaitUntilAsync(() => deliveries.Records.Count == 1), deliveries, outcomes: outcomes);

        Assert.Equal(["retry:laura@example.com"], source.Events);
        Assert.Equal(["unsent:" + invitation.Invitation!.InvitationId], deliveries.Records);
        Assert.Equal(["expired"], outcomes.Outcomes);
    }

    // N5: when putting it back to wait fails (the database, or its bounded token), it did not expire: nothing is recorded or
    // counted as expired, and its row stays claimed, as a crash leaves it, so it is never sent twice.
    [Fact]
    public async Task An_email_that_could_not_go_back_to_wait_is_neither_expired_nor_recorded()
    {
        var invitation = Message("laura@example.com") with { Invitation = new EmailInvitationReference(Guid.CreateVersion7(), Guid.CreateVersion7()) };
        var source = new ListSource(invitation, Message("ana@example.com")) { Retry = DeliveryRetry.NotReturned };
        var deliveries = new RecordingDeliveryService();
        var logger = new FakeLogger<EmailBackgroundService>();
        using var outcomes = new OutcomeListener();
        var sender = new ScriptedSender(message => message.To == invitation.To
            ? EmailSendResult.Failed(EmailSendOutcome.ConnectionFailed)
            : Sent(message));

        await RunAsync(source, sender, async () => await WaitUntilAsync(() => source.Events.Count == 2), deliveries, logger, outcomes);

        Assert.Equal(["retry:laura@example.com", "completed:ana@example.com"], source.Events);
        Assert.Empty(deliveries.Records);
        Assert.Equal(["sent"], outcomes.Outcomes);
        var notReturned = Assert.Single(logger.Collector.GetSnapshot(), record => record.Message.Contains("could not go back", StringComparison.Ordinal));
        Assert.Contains(invitation.Id.ToString(), notReturned.Message, StringComparison.Ordinal);
    }

    // N5: a server that refuses the sender (Gmail answers 550 5.4.5 to MAIL FROM once the daily quota is spent) refuses every email
    // the same: none is retried, and three in a row degrade the email check, as the connection does.
    [Fact]
    public async Task A_sender_the_server_refuses_is_not_retried_and_three_in_a_row_degrade_the_email_check()
    {
        var invitation = Message("laura@example.com") with { Invitation = new EmailInvitationReference(Guid.CreateVersion7(), Guid.CreateVersion7()) };
        var source = new ListSource(invitation, Message("ana@example.com"), Message("eva@example.com"));
        var deliveries = new RecordingDeliveryService();
        var health = new EmailHealth();
        using var outcomes = new OutcomeListener();
        var sender = new ScriptedSender(_ => EmailSendResult.Failed(EmailSendOutcome.SenderRejected, 550));

        await RunAsync(source, sender, async () => await WaitUntilAsync(() => source.Events.Count == 3), deliveries, outcomes: outcomes, health: health);

        Assert.Equal(["completed:laura@example.com", "completed:ana@example.com", "completed:eva@example.com"], source.Events);
        Assert.Equal(["unsent:" + invitation.Invitation!.InvitationId], deliveries.Records);
        Assert.Equal(["rejected", "rejected", "rejected"], outcomes.Outcomes);
        Assert.Equal(EmailHealthProblem.ServerUnavailable, health.Problem);
    }

    [Fact]
    public async Task An_email_the_server_refused_for_good_is_not_retried_and_its_invitation_is_failed()
    {
        var invitation = Message("nadie@example.com") with { Invitation = new EmailInvitationReference(Guid.CreateVersion7(), Guid.CreateVersion7()) };
        var source = new ListSource(invitation);
        var deliveries = new RecordingDeliveryService();
        var sender = new ScriptedSender(_ => EmailSendResult.Failed(EmailSendOutcome.Rejected, 550));

        await RunAsync(source, sender, async () => await WaitUntilAsync(() => deliveries.Records.Count == 1), deliveries);

        Assert.Equal(["completed:nadie@example.com"], source.Events);
        Assert.Equal(["unsent:" + invitation.Invitation!.InvitationId], deliveries.Records);
        Assert.Equal(1, sender.AttemptsFor(invitation.To));
    }

    [Fact]
    public async Task An_email_cut_while_its_body_went_out_is_not_sent_again_and_its_invitation_is_uncertain()
    {
        var invitation = Message("laura@example.com") with { Invitation = new EmailInvitationReference(Guid.CreateVersion7(), Guid.CreateVersion7()) };
        var source = new ListSource(invitation);
        var deliveries = new RecordingDeliveryService();
        var sender = new ScriptedSender(_ => EmailSendResult.Failed(EmailSendOutcome.Uncertain));

        await RunAsync(source, sender, async () => await WaitUntilAsync(() => deliveries.Records.Count == 1), deliveries);

        Assert.Equal(["completed:laura@example.com"], source.Events);
        Assert.Equal(["uncertain:" + invitation.Invitation!.InvitationId], deliveries.Records);
        Assert.Equal(1, sender.AttemptsFor(invitation.To));
    }

    [Fact]
    public async Task A_sent_invitation_records_the_message_id_it_went_out_with()
    {
        var invitation = Message("laura@example.com") with { Invitation = new EmailInvitationReference(Guid.CreateVersion7(), Guid.CreateVersion7()) };
        var source = new ListSource(invitation);
        var deliveries = new RecordingDeliveryService();

        await RunAsync(source, new ScriptedSender(Sent), async () => await WaitUntilAsync(() => deliveries.Records.Count == 1), deliveries);

        Assert.Equal([$"sent:{invitation.Invitation!.InvitationId}:<{invitation.Id:N}@example.test>"], deliveries.Records);
        Assert.Equal(["completed:laura@example.com"], source.Events);
    }

    // N8: the shutdown cuts the send only before the body: the email goes back to the queue and nothing new is taken.
    [Fact]
    public async Task When_the_host_stops_before_the_body_the_email_goes_back_to_the_queue()
    {
        var source = new ListSource(Message("laura@example.com"), Message("ana@example.com"));
        var connecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new ScriptedSender(async (_, stopping) =>
        {
            connecting.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, stopping).ContinueWith(_ => { }, TaskScheduler.Default);
            return EmailSendResult.Failed(EmailSendOutcome.Stopped);
        });

        await RunAsync(source, sender, async () => await connecting.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct));

        Assert.Equal(["returned:laura@example.com"], source.Events);
        Assert.Equal(0, sender.AttemptsFor("ana@example.com"));
    }

    // N8: once the body is going out, the shutdown does not cut it: it ends with the sender's own deadline and is completed.
    [Fact]
    public async Task When_the_host_stops_while_the_body_goes_out_the_send_ends_and_is_completed()
    {
        var source = new ListSource(Message("laura@example.com"));
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new ScriptedSender(async (message, _) =>
        {
            sending.TrySetResult();
            await accepted.Task;
            return Sent(message);
        });
        var service = Worker(source, sender, new RecordingDeliveryService(), out var provider);
        await using (provider)
        {
            await service.StartAsync(Ct);
            await sending.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

            var stopping = service.StopAsync(Ct);
            Assert.False(stopping.IsCompleted);
            accepted.SetResult();
            await stopping;
            service.Dispose();
        }

        Assert.Equal(["completed:laura@example.com"], source.Events);
    }

    // N5: credentials rejected (a 535) degrade the email check at once; the connection, after three failures in a row; a send
    // clears both. Degraded, never Unhealthy: /health stays 200 and the copy stays in the traffic.
    [Fact]
    public async Task The_email_check_is_degraded_at_once_by_rejected_credentials_and_after_three_failures_by_the_connection()
    {
        var health = new EmailHealth();
        var check = new EmailHealthCheck(health);
        async Task<HealthStatus> StatusAsync() => (await check.CheckHealthAsync(new HealthCheckContext(), Ct)).Status;

        health.ReportFailure(EmailSendOutcome.ConnectionFailed);
        health.ReportFailure(EmailSendOutcome.TemporarilyRejected);
        Assert.Equal(HealthStatus.Healthy, await StatusAsync());
        health.ReportFailure(EmailSendOutcome.ConnectionFailed);
        Assert.Equal(HealthStatus.Degraded, await StatusAsync());
        health.ReportSent();
        Assert.Equal(HealthStatus.Healthy, await StatusAsync());

        health.ReportFailure(EmailSendOutcome.AuthenticationFailed);
        var degraded = await check.CheckHealthAsync(new HealthCheckContext(), Ct);
        Assert.Equal(HealthStatus.Degraded, degraded.Status);
        Assert.Contains("Email:Smtp:Password", degraded.Description, StringComparison.Ordinal);
        health.ReportSent();
        Assert.Equal(HealthStatus.Healthy, await StatusAsync());
    }

    private static async Task RunAsync(
        ListSource source, ScriptedSender sender, Func<Task> act, RecordingDeliveryService? deliveries = null,
        FakeLogger<EmailBackgroundService>? logger = null, OutcomeListener? outcomes = null, EmailHealth? health = null)
    {
        using var ownOutcomes = outcomes is null ? new OutcomeListener() : null;
        var service = Worker(source, sender, deliveries ?? new RecordingDeliveryService(), out var provider, logger, outcomes ?? ownOutcomes, health);
        await using (provider)
        {
            await service.StartAsync(Ct);
            try
            {
                await act();
            }
            finally
            {
                await service.StopAsync(Ct);
                service.Dispose();
            }
        }
    }

    private static EmailBackgroundService Worker(
        ListSource source, ScriptedSender sender, RecordingDeliveryService deliveries, out ServiceProvider provider,
        FakeLogger<EmailBackgroundService>? logger = null, OutcomeListener? outcomes = null, EmailHealth? health = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEmailSender>(sender);
        services.AddSingleton<IEmailDeliveryService>(deliveries);
        services.AddMetrics();
        provider = services.BuildServiceProvider();

        return new EmailBackgroundService(
            source,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new EmailOptions { RetryDelaySeconds = 2 }),
            health ?? new EmailHealth(),
            outcomes?.Metrics ?? new EmailMetrics(provider.GetRequiredService<IMeterFactory>()),
            logger ?? new FakeLogger<EmailBackgroundService>());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private static EmailMessage Message(string to) => new(to, "Asunto", "<p>Hola</p>", "Hola");

    private static EmailSendResult Sent(EmailMessage message) => EmailSendResult.Sent($"<{message.Id:N}@example.test>");

    /// <summary>A queue that hands out its emails once and writes down how the worker settled each one.</summary>
    private sealed class ListSource(params EmailMessage[] messages) : IMessageSource<EmailMessage>
    {
        private bool _givenBack;
        private bool _settled;

        public ConcurrentQueue<string> Events { get; } = new();

        public ConcurrentQueue<(TimeSpan First, TimeSpan Longest)> Waits { get; } = new();

        public DeliveryRetry Retry { get; init; } = DeliveryRetry.Waits;

        private EmailMessage? Current { get; set; }

        public async IAsyncEnumerable<EmailMessage> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var message in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (Current, _givenBack, _settled) = (message, false, false);
                var finished = false;
                try
                {
                    yield return message;
                    finished = true;
                }
                finally
                {
                    if (_givenBack) Events.Enqueue("returned:" + message.To);
                }
                if (_givenBack) yield break;
                if (finished && !_settled) Events.Enqueue("completed:" + message.To);
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public void GiveBackCurrent() => _givenBack = true;

        public Task<DeliveryRetry> RetryCurrentLaterAsync(TimeSpan firstWait, TimeSpan longestWait, CancellationToken cancellationToken)
        {
            _settled = true;
            Waits.Enqueue((firstWait, longestWait));
            Events.Enqueue("retry:" + Current!.To);
            return Task.FromResult(Retry);
        }

        public Task CompleteCurrentAsync(CancellationToken cancellationToken)
        {
            if (_settled) return Task.CompletedTask;
            _settled = true;
            Events.Enqueue("completed:" + Current!.To);
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedSender(Func<EmailMessage, CancellationToken, Task<EmailSendResult>> respond) : IEmailSender
    {
        private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);

        public ScriptedSender(Func<EmailMessage, EmailSendResult> respond) : this((message, _) => Task.FromResult(respond(message)))
        {
        }

        public int AttemptsFor(string to) => _attempts.GetValueOrDefault(to);

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            _attempts.AddOrUpdate(message.To, 1, (_, previous) => previous + 1);
            return respond(message, cancellationToken);
        }
    }

    private sealed class RecordingDeliveryService : IEmailDeliveryService
    {
        public ConcurrentQueue<string> Records { get; } = new();

        public Task<Result> RecordSentAsync(EmailMessage message, string messageId, CancellationToken cancellationToken) =>
            Record($"sent:{message.Invitation!.InvitationId}:{messageId}");

        public Task<Result> RecordUnsentAsync(EmailMessage message, CancellationToken cancellationToken) =>
            Record("unsent:" + message.Invitation!.InvitationId);

        public Task<Result> RecordUncertainAsync(EmailMessage message, CancellationToken cancellationToken) =>
            Record("uncertain:" + message.Invitation!.InvitationId);

        private Task<Result> Record(string record)
        {
            Records.Enqueue(record);
            return Task.FromResult(Result.Success());
        }
    }

    /// <summary>
    /// The outcomes one worker counted (<see cref="EmailMetrics"/>), in order: only those of the meter its own factory created, so
    /// the workers of other tests running at the same time do not get in.
    /// </summary>
    private sealed class OutcomeListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ServiceProvider _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();

        public OutcomeListener()
        {
            var factory = _provider.GetRequiredService<IMeterFactory>();
            Metrics = new EmailMetrics(factory);
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == EmailMetrics.MeterName && ReferenceEquals(instrument.Meter.Scope, factory))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                foreach (var tag in tags) if (tag.Key == "outcome") Outcomes.Enqueue((string)tag.Value!);
            });
            _listener.Start();
        }

        public ConcurrentQueue<string> Outcomes { get; } = new();

        public EmailMetrics Metrics { get; }

        public void Dispose()
        {
            _listener.Dispose();
            _provider.Dispose();
        }
    }
}
