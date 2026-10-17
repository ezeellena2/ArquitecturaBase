using System.Diagnostics.Metrics;
using System.Globalization;
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
    IServiceScopeFactory scopes, TimeProvider timeProvider, IOptions<AiAssistantOptions> options,
    ILogger<AiAssistantCoordinator> logger)
{
    private static readonly Meter Meter = new("ArquitecturaBase.AiAssistant", "1.0");
    private static readonly Counter<long> Claims = Meter.CreateCounter<long>("ai.assistant.claims");
    private static readonly Counter<long> ClaimFailures = Meter.CreateCounter<long>("ai.assistant.claim.failures");
    private static readonly Counter<long> Steps = Meter.CreateCounter<long>("ai.assistant.provider.steps");
    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>("ai.assistant.outcomes");
    private static readonly Counter<long> CloseFailures = Meter.CreateCounter<long>("ai.assistant.persistence.failures");
    private static readonly Counter<long> UnknownUsage = Meter.CreateCounter<long>("ai.assistant.usage.unknown");
    private static readonly Counter<long> InputTokens = Meter.CreateCounter<long>("ai.assistant.tokens.input");
    private static readonly Counter<long> OutputTokens = Meter.CreateCounter<long>("ai.assistant.tokens.output");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("ai.assistant.duration", "s");

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        AiAssistantClaimResponse? claim;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var claimed = await scope.ServiceProvider.GetRequiredService<IAiAssistantDispatchService>().ClaimAsync(cancellationToken);
            if (claimed.IsFailure) { RecordClaimFailure(); LogUnitRejected(logger, "claim", claimed.Error.Code); return false; }
            claim = claimed.Value;
        }
        if (claim is null) return false;
        Claims.Add(1);
        var started = timeProvider.GetTimestamp();
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(claim.Culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(claim.Culture);
        using var deadline = new CancellationTokenSource(Remaining(claim), timeProvider);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var monitoring = CancellationTokenSource.CreateLinkedTokenSource(execution.Token);
        var monitor = ObserveCancellationAsync(claim, execution, monitoring.Token);
        var stepReserved = false;
        var providerMayHaveBeenSent = false;
        LlmConversationUsage? knownUsage = null;
        try
        {
            Result<AiAssistantExecutionContextResponse> prepared;
            await using (var scope = scopes.CreateAsyncScope())
                prepared = await scope.ServiceProvider.GetRequiredService<IAiAssistantExecutionService>().PrepareAsync(claim, execution.Token);
            if (prepared.IsFailure) { await FailAsync(claim, prepared.Error.Code, false); return true; }
            var context = prepared.Value;
            while (!execution.IsCancellationRequested)
            {
                Result<bool> reserved;
                await using (var scope = scopes.CreateAsyncScope())
                    reserved = await scope.ServiceProvider.GetRequiredService<IAiAssistantDispatchService>().BeginProviderStepAsync(claim, execution.Token);
                if (reserved.IsFailure) { await FailAsync(claim, reserved.Error.Code, false); return true; }
                if (!reserved.Value) return true;
                stepReserved = true;
                providerMayHaveBeenSent = true;
                knownUsage = null;
                Steps.Add(1);
                Result<LlmConversationResponse> response;
                await using (var scope = scopes.CreateAsyncScope())
                    response = await scope.ServiceProvider.GetRequiredService<IAiAssistantExecutionService>().CallProviderAsync(context, execution.Token);
                if (response.IsFailure)
                {
                    await FailAsync(claim, response.Error.Code, response.Error.Code == AiAssistantErrors.ProviderUncertain.Code);
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
                if (advanced.IsFailure) { await FailAsync(claim, advanced.Error.Code, false, knownUsage); return true; }
                var progress = advanced.Value;
                if (progress.IsComplete)
                {
                    var completed = await PersistFinalAsync((service, token) => service.CompleteAsync(claim, progress, token));
                    if (completed?.IsFailure == true) await FailAsync(claim, completed.Error.Code, false, knownUsage);
                    else if (completed?.IsSuccess == true) await ObserveOutcomeAsync(claim);
                    return true;
                }
                Result recorded;
                await using (var scope = scopes.CreateAsyncScope())
                    recorded = await scope.ServiceProvider.GetRequiredService<IAiAssistantDispatchService>().RecordProgressAsync(claim, progress, execution.Token);
                if (recorded.IsFailure) { await FailAsync(claim, recorded.Error.Code, false, knownUsage); return true; }
                knownUsage = null;
                context = progress.Context;
            }
            await FailAsync(claim, deadline.IsCancellationRequested ? AiAssistantErrors.Expired.Code : AiAssistantErrors.Canceled.Code, stepReserved);
        }
        catch (Exception exception)
        {
            // After a committed external-step reservation, absence of a response cannot establish that it was not sent.
            LogExecutionFailed(logger, claim.TurnId, exception.GetType().Name);
            await FailAsync(claim, providerMayHaveBeenSent ? AiAssistantErrors.ProviderUncertain.Code
                : execution.IsCancellationRequested ? AiAssistantErrors.Canceled.Code : AiAssistantErrors.ProviderUnavailable.Code, providerMayHaveBeenSent, knownUsage);
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
        try
        {
            while (!cancellationToken.IsCancellationRequested)
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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            LogExecutionFailed(logger, claim.TurnId, exception.GetType().Name);
            await execution.CancelAsync();
        }
    }

    private async Task FailAsync(AiAssistantClaimResponse claim, string code, bool uncertain, LlmConversationUsage? usage = null)
    {
        var failed = await PersistFinalAsync((service, token) => service.FailAsync(claim, code, uncertain, token, usage));
        if (failed?.IsSuccess == true) await ObserveOutcomeAsync(claim);
    }

    private async Task ObserveOutcomeAsync(AiAssistantClaimResponse claim)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1), timeProvider);
            await using var scope = scopes.CreateAsyncScope();
            var durable = await scope.ServiceProvider.GetRequiredService<IAiAssistantReader>()
                .FindTurnByRequestAsync(claim.OwnerUserId, claim.RequestId, timeout.Token);
            if (durable is not null) Outcomes.Add(1, new KeyValuePair<string, object?>("state", durable.State.ToString().ToLowerInvariant()));
        }
        catch (Exception exception) { LogExecutionFailed(logger, claim.TurnId, exception.GetType().Name); }
    }

    private async Task<Result?> PersistFinalAsync(Func<IAiAssistantDispatchService, CancellationToken, Task<Result>> persist)
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Assistant unit {Unit} was rejected with {Code}.")]
    private static partial void LogUnitRejected(ILogger logger, string unit, string code);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Assistant turn {TurnId} stopped with {ExceptionType}; no provider request is repeated.")]
    private static partial void LogExecutionFailed(ILogger logger, Guid turnId, string exceptionType);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Assistant result persistence failed with {ExceptionType}; only its commit is retried.")]
    private static partial void LogCompletionFailed(ILogger logger, string exceptionType);
}
