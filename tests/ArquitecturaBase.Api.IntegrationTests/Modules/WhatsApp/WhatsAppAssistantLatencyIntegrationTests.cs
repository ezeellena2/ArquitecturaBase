using System.Diagnostics;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantLatencyIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Text_runs_the_real_assistant_and_background_dispatch_with_simulated_provider_http()
    {
        await using var scenario = await WhatsAppAssistantLatencyScenario.CreateAsync(factory);
        var contact = await scenario.ContactAsync(automatic: true);
        var started = Stopwatch.GetTimestamp();
        await scenario.PostTextAsync(contact);
        await WhatsAppAssistantLatencyScenario.WaitAsync(() => Task.FromResult(!scenario.Cloud.Sent.IsEmpty));
        var sent = Assert.Single(scenario.Cloud.Sent);
        Assert.IsType<WhatsAppFlowMessageResponse>(sent.Message);
        Assert.Equal(3, scenario.Trace.TextCalls);
        Assert.Equal(0, scenario.Trace.AudioCalls);
        await scenario.ReadAsync(async db =>
        {
            var reference = await db.Set<WhatsAppAssistantRequest>().AsNoTracking().SingleAsync(row => row.ContactId == contact.ContactId, Ct);
            Assert.NotNull(reference.OutgoingId);
            Assert.Equal(1, await db.Set<WhatsAppFlowSession>().CountAsync(row => row.ContactId == contact.ContactId, Ct));
            return true;
        });
        await WhatsAppAssistantLatencyScenario.RecordAsync("text", started, new
        {
            providerTerminalMs = Elapsed(started, scenario.Trace.TextTerminalReturnedTimestamp),
            providerToSendMs = Elapsed(scenario.Trace.TextTerminalReturnedTimestamp, sent.Timestamp),
            sendMs = Elapsed(started, sent.Timestamp),
            textCalls = scenario.Trace.TextCalls, audioCalls = scenario.Trace.AudioCalls, sends = scenario.Cloud.Sent.Count,
        });
    }

    [Fact]
    public async Task Audio_runs_real_normalization_then_interpretation_and_dispatch_with_simulated_provider_http()
    {
        await using var scenario = await WhatsAppAssistantLatencyScenario.CreateAsync(factory);
        var contact = await scenario.ContactAsync(automatic: true);
        var started = Stopwatch.GetTimestamp();
        await scenario.PostAudioAsync(contact);
        await WhatsAppAssistantLatencyScenario.WaitAsync(() => Task.FromResult(!scenario.Cloud.Sent.IsEmpty));
        var sent = Assert.Single(scenario.Cloud.Sent);
        Assert.IsType<WhatsAppFlowMessageResponse>(sent.Message);
        Assert.Equal(1, scenario.Trace.AudioCalls);
        Assert.Equal(3, scenario.Trace.TextCalls);
        var audio = await scenario.ReadAsync(db => db.Set<WhatsAppMessage>().AsNoTracking()
            .SingleAsync(row => row.ContactId == contact.ContactId && row.Kind == WhatsAppMessageKind.Audio, Ct));
        Assert.Equal(WhatsAppAudioState.Completed, audio.AudioState);
        Assert.NotNull(audio.AudioRequestId);
        Assert.Equal(1, scenario.Audio.Downloads);
        await WhatsAppAssistantLatencyScenario.RecordAsync("audio", started, new
        {
            normalizationProviderMs = Elapsed(started, scenario.Trace.AudioReturnedTimestamp),
            interpretationProviderMs = Elapsed(started, scenario.Trace.TextTerminalReturnedTimestamp),
            normalizationToInterpretationMs = Elapsed(scenario.Trace.AudioReturnedTimestamp, scenario.Trace.TextTerminalReturnedTimestamp),
            providerToSendMs = Elapsed(scenario.Trace.TextTerminalReturnedTimestamp, sent.Timestamp),
            sendMs = Elapsed(started, sent.Timestamp),
            textCalls = scenario.Trace.TextCalls, audioCalls = scenario.Trace.AudioCalls, sends = scenario.Cloud.Sent.Count,
        });
    }

    [Fact]
    public async Task A_real_nfm_reply_is_captured_then_observed_by_the_assistant_background_processor()
    {
        await using var scenario = await WhatsAppAssistantLatencyScenario.CreateAsync(factory);
        var contact = await scenario.ContactAsync(automatic: false);
        var session = await scenario.SendInitialFlowAsync(contact);
        var started = Stopwatch.GetTimestamp();
        var messageId = await scenario.PostReplyAsync(contact);
        await WhatsAppAssistantLatencyScenario.WaitAsync(() => scenario.ReadAsync(db => db.Set<WhatsAppFlowSession>()
            .AnyAsync(row => row.Id == session.Id && row.CaptureState == WhatsAppFlowCaptureState.Completed, Ct)));
        var captured = Stopwatch.GetTimestamp();
        await WhatsAppAssistantLatencyScenario.WaitAsync(() => scenario.ReadAsync(db => db.Set<WhatsAppMessage>()
            .AnyAsync(row => row.WaMessageId == messageId && row.AssistantProcessedAtUtc != null, Ct)));
        var observed = Stopwatch.GetTimestamp();
        var stored = await scenario.ReadAsync(db => db.Set<WhatsAppFlowSession>().AsNoTracking().SingleAsync(row => row.Id == session.Id, Ct));
        Assert.NotNull(stored.CompletedRecordId);
        Assert.Single(scenario.Cloud.Sent);
        Assert.Equal(0, scenario.Trace.TextCalls);
        Assert.Equal(0, scenario.Trace.AudioCalls);
        await WhatsAppAssistantLatencyScenario.RecordAsync("nfm_reply", started, new
        {
            captureObservedMs = Elapsed(started, captured), assistantObservedMs = Elapsed(started, observed),
            captureToAssistantObservedMs = Elapsed(captured, observed),
            textCalls = scenario.Trace.TextCalls, audioCalls = scenario.Trace.AudioCalls,
        });
    }

    [Fact]
    public async Task Two_real_outgoing_rows_for_different_destinations_are_dispatched_without_simulating_reservations()
    {
        await using var scenario = await WhatsAppAssistantLatencyScenario.CreateAsync(factory, publishForm: false);
        var first = await scenario.ContactAsync(automatic: false);
        var second = await scenario.ContactAsync(automatic: false);
        var started = Stopwatch.GetTimestamp();
        await Task.WhenAll(scenario.SendManualTextAsync(first), scenario.SendManualTextAsync(second));
        await WhatsAppAssistantLatencyScenario.WaitAsync(() => Task.FromResult(scenario.Cloud.Sent.Count == 2));
        await WhatsAppAssistantLatencyScenario.WaitAsync(async () => await scenario.ReadAsync(db => db.Set<WhatsAppOutgoingMessage>()
            .CountAsync(row => row.State == WhatsAppOutgoingMessageState.Accepted, Ct)) == 2);
        var sends = scenario.Cloud.Sent.ToArray();
        Assert.All(sends, item => Assert.IsType<WhatsAppTextMessage>(item.Message));
        Assert.NotEqual(sends[0].Message.To, sends[1].Message.To);
        Assert.Equal(2, await scenario.ReadAsync(db => db.Set<WhatsAppOutgoingMessage>()
            .CountAsync(row => row.State == WhatsAppOutgoingMessageState.Accepted, Ct)));
        Assert.Equal(0, scenario.Trace.TextCalls);
        Assert.Equal(0, scenario.Trace.AudioCalls);
        await WhatsAppAssistantLatencyScenario.RecordAsync("outgoing", started, new
        {
            firstSendMs = Elapsed(started, sends[0].Timestamp), secondSendMs = Elapsed(started, sends[1].Timestamp),
            betweenSendsMs = Elapsed(sends[0].Timestamp, sends[1].Timestamp), sends = sends.Length,
        });
    }

    private static double Elapsed(long? started, long? ended)
    {
        var from = Assert.IsType<long>(started);
        var to = Assert.IsType<long>(ended);
        Assert.True(from > 0 && to >= from, "The real-time checkpoint must exist and follow its predecessor.");
        return Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;
    }
}
