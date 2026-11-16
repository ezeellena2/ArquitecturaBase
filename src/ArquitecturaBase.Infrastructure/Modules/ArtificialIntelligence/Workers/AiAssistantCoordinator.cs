using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Workers;

/// <summary>Coordinates committed units. No scoped service or provider continuation survives a process restart.</summary>
internal sealed partial class AiAssistantCoordinator(
    IServiceScopeFactory scopes, IAiKeyRingReadiness keyRing, TimeProvider timeProvider, IOptions<AiAssistantOptions> options,
    ILogger<AiAssistantCoordinator> logger)
{
    private static readonly Meter Meter = new("ArquitecturaBase.AiAssistant", "1.0");
    private static readonly Counter<long> Claims = Meter.CreateCounter<long>("ai.assistant.claims");
    private static readonly Counter<long> ClaimFailures = Meter.CreateCounter<long>("ai.assistant.claim.failures");
    private static readonly Counter<long> Steps = Meter.CreateCounter<long>("ai.assistant.provider.steps");
    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>("ai.assistant.outcomes");
    private static readonly Counter<long> CloseFailures = Meter.CreateCounter<long>("ai.assistant.persistence.failures");
    private static readonly Counter<long> Releases = Meter.CreateCounter<long>("ai.assistant.releases");
    private static readonly Counter<long> UnknownUsage = Meter.CreateCounter<long>("ai.assistant.usage.unknown");
    private static readonly Counter<long> InputTokens = Meter.CreateCounter<long>("ai.assistant.tokens.input");
    private static readonly Counter<long> OutputTokens = Meter.CreateCounter<long>("ai.assistant.tokens.output");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("ai.assistant.duration", "s");

    /// <summary>
    /// Takes and runs the next turn. I12: <paramref name="stoppingToken"/> is the copy's stop: it takes no turn, and one that has not
    /// reserved a step goes back to pending for another copy. <paramref name="drainToken"/> is the end of the drain: it cuts what still
    /// runs, a step included, with the shutdown code. Without it, a turn that reserved a step runs until it ends or its deadline.
    /// </summary>
    public async Task<bool> ProcessNextAsync(CancellationToken stoppingToken, CancellationToken drainToken = default)
    {
        if (stoppingToken.IsCancellationRequested) return false;
        // I17, review: the readiness only takes a copy out of HTTP traffic. One whose key ring does not match the others' (or could not
        // verify it yet) takes no turn: it could not read the saved key, or would answer encrypting with a key the others do not have.
        if (!await keyRing.MatchesAsync(stoppingToken)) return false;
        AiAssistantClaimResponse? claim;
        await using (var scope = scopes.CreateAsyncScope())
        {
            // I12, review: the stop, already checked, does not cut the claim: a commit that lands as it arrives would leave the turn running
            // without an owner until it expires. A turn taken then goes back below; the drain's end still bounds the claim.
            var claimed = await scope.ServiceProvider.GetRequiredService<IAiAssistantDispatchService>().ClaimAsync(drainToken);
            if (claimed.IsFailure) { RecordClaimFailure(); LogUnitRejected(logger, "claim", claimed.Error.Code); return false; }
            claim = claimed.Value;
        }
        if (claim is null) return false;
        Claims.Add(1);
        // I10: one activity per turn continues the trace its caller left in it, around its capabilities and the provider call, and every
        // log of the worker says which turn.
        using var activity = AiAssistantTracing.StartTurn(claim.TurnId, claim.TraceParent);
        using var turnScope = TurnScope(logger, claim.TurnId);
        var started = timeProvider.GetTimestamp();
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(claim.Culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(claim.Culture);
        using var deadline = new CancellationTokenSource(Remaining(claim), timeProvider);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(drainToken, deadline.Token);
        // I12: until this attempt reserves a step, the stop also cuts it, and the turn goes back to pending.
        using var beforeStep = CancellationTokenSource.CreateLinkedTokenSource(execution.Token, stoppingToken);
        using var monitoring = CancellationTokenSource.CreateLinkedTokenSource(execution.Token);
        var monitor = ObserveCancellationAsync(claim, execution, monitoring.Token);
        var anyStep = false;
        var stepReserved = false;
        var providerMayHaveBeenSent = false;
        LlmConversationUsage? knownUsage = null;
        // I12: stopped before reserving a step (the release closes a canceled or expired one as it is), or cut by the drain's end after one.
        bool Stopped() => !anyStep && stoppingToken.IsCancellationRequested;
        bool Drained() => drainToken.IsCancellationRequested && !deadline.IsCancellationRequested;
        // I12, review: a failure once the drain ended is its cut, whatever code it brings: the shutdown code (a turn its person canceled
        // keeps its usual code, AiAssistantTurn.Fail). Any other failure keeps its own.
        string Cut(string code) => Drained() ? AiAssistantErrors.Interrupted.Code : code;
        try
        {
            Result<AiAssistantExecutionContextResponse> prepared;
            await using (var scope = scopes.CreateAsyncScope())
                prepared = await scope.ServiceProvider.GetRequiredService<IAiAssistantExecutionService>().PrepareAsync(claim, beforeStep.Token);
            if (Stopped()) { await ReleaseAsync(claim, activity); return true; }
            if (prepared.IsFailure) { await FailAsync(claim, activity, Cut(prepared.Error.Code), false); return true; }
            var context = prepared.Value;
            AiAssistantTracing.Describe(activity, context);
            while (!execution.IsCancellationRequested)
            {
                if (Stopped()) { await ReleaseAsync(claim, activity); return true; }
                Result<bool> reserved;
                await using (var scope = scopes.CreateAsyncScope())
                    reserved = await scope.ServiceProvider.GetRequiredService<IAiAssistantDispatchService>()
                        .BeginProviderStepAsync(claim, anyStep ? execution.Token : beforeStep.Token);
                if (reserved.IsFailure && Stopped()) { await ReleaseAsync(claim, activity); return true; }
                if (reserved.IsFailure) { await FailAsync(claim, activity, Cut(reserved.Error.Code), false); return true; }
                // The engine may have closed the turn itself (a step limit, lost access, a cancellation or a deadline found on reserving).
                if (!reserved.Value) { await ObserveOutcomeAsync(claim, activity); return true; }
                anyStep = true;
                stepReserved = true;
                providerMayHaveBeenSent = true;
                knownUsage = null;
                Steps.Add(1);
                Result<LlmConversationResponse> response;
                await using (var scope = scopes.CreateAsyncScope())
                    response = await scope.ServiceProvider.GetRequiredService<IAiAssistantExecutionService>().CallProviderAsync(context, execution.Token);
                if (response.IsFailure)
                {
                    // I6: the code and the HTTP status the provider answered, with the turn and the provider; never its body.
                    LogProviderFailed(logger, claim.TurnId, context.ProviderRequest.Provider, response.Error.Code, AiAssistantErrors.ProviderStatus(response.Error));
                    // I12: the drain's end cut the step: the shutdown code, still uncertain if it may have been sent.
                    var uncertain = response.Error.Code == AiAssistantErrors.ProviderUncertain.Code;
                    await FailAsync(claim, activity, Cut(response.Error.Code), uncertain);
                    return true;
                }
                stepReserved = false;
                providerMayHaveBeenSent = false;
                knownUsage = response.Value.Usage;
                if (response.Value.Usage?.InputTokens is null || response.Value.Usage.OutputTokens is null) UnknownUsage.Add(1);
                if (knownUsage?.InputTokens is { } inputTokens) InputTokens.Add(inputTokens);
                if (knownUsage?.OutputTokens is { } outputTokens) OutputTokens.Add(outputTokens);
                Result<AiAssistantAdvanceResponse> advanced;
                await using (var scope = scopes.CreateAsyncScope())
                    advanced = await scope.ServiceProvider.GetRequiredService<IAiAssistantExecutionService>().AdvanceAsync(context, response.Value, execution.Token);
                if (advanced.IsFailure) { await FailAsync(claim, activity, Cut(advanced.Error.Code), false, knownUsage); return true; }
                var progress = advanced.Value;
                if (progress.IsComplete)
                {
                    var completed = await PersistFinalAsync((service, token) => service.CompleteAsync(claim, progress, token));
                    // Closing may also fail the turn (lost access, an answer it could not verify), and a commit that timed out may have landed.
                    if (completed?.IsFailure == true) await FailAsync(claim, activity, completed.Error.Code, false, knownUsage);
                    else await ObserveOutcomeAsync(claim, activity);
                    return true;
                }
                Result recorded;
                await using (var scope = scopes.CreateAsyncScope())
                    recorded = await scope.ServiceProvider.GetRequiredService<IAiAssistantDispatchService>().RecordProgressAsync(claim, progress, execution.Token);
                if (recorded.IsFailure) { await FailAsync(claim, activity, Cut(recorded.Error.Code), false, knownUsage); return true; }
                knownUsage = null;
                context = progress.Context;
            }
            await FailAsync(claim, activity, deadline.IsCancellationRequested ? AiAssistantErrors.Expired.Code : Cut(AiAssistantErrors.Canceled.Code), stepReserved);
        }
        catch (Exception exception)
        {
            // After a committed external-step reservation, absence of a response cannot establish that it was not sent. Anything else is
            // the engine's own failure (I5): ProviderUnavailable comes only from the provider call. I12, review: the stop's cut is no
            // failure: the turn goes back to pending and the release logs it.
            if (Stopped()) { await ReleaseAsync(claim, activity); return true; }
            LogExecutionFailed(logger, claim.TurnId, exception.GetType().Name);
            await FailAsync(claim, activity, Drained() ? AiAssistantErrors.Interrupted.Code : providerMayHaveBeenSent ? AiAssistantErrors.ProviderUncertain.Code
                : execution.IsCancellationRequested ? AiAssistantErrors.Canceled.Code : AiAssistantErrors.ExecutionFailed.Code, providerMayHaveBeenSent, knownUsage);
        }
        finally
        {
            await monitoring.CancelAsync();
            await monitor;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
            Duration.Record(timeProvider.GetElapsedTime(started).TotalSeconds);
        }
        return true;
    }

    internal static void RecordClaimFailure() => ClaimFailures.Add(1);

    private TimeSpan Remaining(AiAssistantClaimResponse claim) =>
        TimeSpan.FromTicks(Math.Max(1, (claim.DeadlineAtUtc - timeProvider.GetUtcNow().UtcDateTime).Ticks));

    private async Task ObserveCancellationAsync(AiAssistantClaimResponse claim, CancellationTokenSource execution, CancellationToken cancellationToken)
    {
        // I10: the check runs outside the turn's activity (only inside this method), or its queries every half second would bury the
        // capabilities and the call to the provider in the turn's trace.
        Activity.Current = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeProvider, cancellationToken);
                await using var scope = scopes.CreateAsyncScope();
                var turn = await scope.ServiceProvider.GetRequiredService<IAiAssistantReader>().FindTurnAsync(claim.TurnId, cancellationToken);
                if (turn is null || turn.CancelRequestedAtUtc is not null || turn.Fence != claim.Fence || turn.State != AiAssistantTurnState.Running)
                {
                    await execution.CancelAsync();
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                // A failed check (a brief database outage) is not a cancellation: the next check follows, and the final
                // confirmation verifies fence, state and cancellation again.
                LogCancellationCheckFailed(logger, claim.TurnId, exception.GetType().Name);
            }
        }
    }

    private async Task FailAsync(AiAssistantClaimResponse claim, Activity? activity, string code, bool uncertain, LlmConversationUsage? usage = null)
    {
        var failed = await PersistFinalAsync((service, token) => service.FailAsync(claim, code, uncertain, token, usage));
        if (failed?.IsFailure != true) await ObserveOutcomeAsync(claim, activity);
    }

    // I12: back to pending for another copy, with the bounded persistence of a close; the activity ends without an outcome while it waits.
    // Review: only a turn that went back is counted and logged as given back; one the release closed (canceled, expired) is not.
    private async Task ReleaseAsync(AiAssistantClaimResponse claim, Activity? activity)
    {
        var released = await PersistFinalAsync((service, token) => service.ReleaseAsync(claim, token));
        if (released is { IsSuccess: true, Value: true }) { Releases.Add(1); LogReleased(logger, claim.TurnId); }
        if (released?.IsFailure != true) await ObserveOutcomeAsync(claim, activity);
    }

    // I10: the outcome and the turn's activity follow what was saved, whoever closed the turn; one not yet finished is neither counted nor marked.
    private async Task ObserveOutcomeAsync(AiAssistantClaimResponse claim, Activity? activity)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1), timeProvider);
            await using var scope = scopes.CreateAsyncScope();
            var durable = await scope.ServiceProvider.GetRequiredService<IAiAssistantReader>()
                .FindTurnByRequestAsync(claim.OwnerUserId, claim.RequestId, timeout.Token);
            if (durable is null || durable.State is AiAssistantTurnState.Pending or AiAssistantTurnState.Running) return;
            Outcomes.Add(1, new KeyValuePair<string, object?>("state", durable.State.ToString().ToLowerInvariant()));
            AiAssistantTracing.MarkOutcome(activity, durable.State, durable.FailureCode);
        }
        catch (Exception exception) { LogExecutionFailed(logger, claim.TurnId, exception.GetType().Name); }
    }

    private async Task<TResult?> PersistFinalAsync<TResult>(Func<IAiAssistantDispatchService, CancellationToken, Task<TResult>> persist)
        where TResult : Result
    {
        // A result already exists: only persistence is repeated, in a fresh scope with the original fence.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.CompletionPersistenceSeconds), timeProvider);
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                return await persist(scope.ServiceProvider.GetRequiredService<IAiAssistantDispatchService>(), timeout.Token);
            }
            catch (Exception exception)
            {
                CloseFailures.Add(1);
                LogCompletionFailed(logger, exception.GetType().Name);
                try { await Task.Delay(TimeSpan.FromMilliseconds(100), timeProvider, timeout.Token); }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested) { return null; }
            }
        }
        return null;
    }

    private static readonly Func<ILogger, Guid, IDisposable?> TurnScope = LoggerMessage.DefineScope<Guid>("Assistant turn {AssistantTurnId}");

    [LoggerMessage(Level = LogLevel.Information, Message = "Assistant turn {TurnId} was given back: the copy stopped before it reserved a step.")]
    private static partial void LogReleased(ILogger logger, Guid turnId);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Assistant unit {Unit} was rejected with {Code}.")]
    private static partial void LogUnitRejected(ILogger logger, string unit, string code);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Assistant turn {TurnId} stopped with {ExceptionType}; no provider request is repeated.")]
    private static partial void LogExecutionFailed(ILogger logger, Guid turnId, string exceptionType);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Assistant turn {TurnId} cancellation check failed with {ExceptionType}; the next check follows.")]
    private static partial void LogCancellationCheckFailed(ILogger logger, Guid turnId, string exceptionType);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Assistant result persistence failed with {ExceptionType}; only its commit is retried.")]
    private static partial void LogCompletionFailed(ILogger logger, string exceptionType);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Assistant turn {TurnId} provider call to {Provider} failed with {Code} (HTTP status {ProviderStatus}).")]
    private static partial void LogProviderFailed(ILogger logger, Guid turnId, AiProvider provider, string code, int? providerStatus);
}
