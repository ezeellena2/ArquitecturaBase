using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;
internal sealed partial class WhatsAppFlowDeprecationBackgroundService(IServiceScopeFactory scopes, IOptions<WhatsAppOptions> options,
    TimeProvider timeProvider, ILogger<WhatsAppFlowDeprecationBackgroundService> logger) : BackgroundService
{
    public async Task ProcessNextAsync(Guid? bindingId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> bindingIds;
        if (bindingId is { } requestedId) bindingIds = [requestedId];
        else
        {
            await using var candidates = scopes.CreateAsyncScope();
            bindingIds = await candidates.ServiceProvider.GetRequiredService<IWhatsAppFlowDeprecationService>()
                .ListDueBindingIdsAsync(cancellationToken);
        }
        WhatsAppFlowDeprecationWorkResponse? work = null;
        foreach (var candidateId in bindingIds)
        {
            await using var reservation = scopes.CreateAsyncScope();
            var result = await reservation.ServiceProvider.GetRequiredService<IWhatsAppFlowDeprecationService>()
                .ReserveAsync(candidateId, cancellationToken);
            if (result.IsFailure) return;
            if (result.Value is null) continue;
            work = result.Value; break;
        }
        if (work is null) return;
        var remaining = work.LeaseExpiresAtUtc - timeProvider.GetUtcNow().UtcDateTime;
        if (remaining <= TimeSpan.Zero) return;
        using var deadline = new CancellationTokenSource(remaining, timeProvider);
        using var dispatchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var dispatchToken = dispatchCancellation.Token;
        var effectAttemptStarted = false;
        WhatsAppFlowEffectResponse? effect = null; WhatsAppFlowStateResponse? verification = null;
        try
        {
            await using var dispatch = scopes.CreateAsyncScope();
            var account = await dispatch.ServiceProvider.GetRequiredService<IWhatsAppFlowEnvironment>().VerifyAccountAsync(dispatchToken);
            if (account.IsFailure) effect = new(false, false, null, [], account.Error.Code);
            else if (!account.Value.HasSameIdentity(work.Manifest.Account)) effect = new(false, false, null, [], FlowErrors.AccountMismatch.Code);
            else
            {
                dispatchToken.ThrowIfCancellationRequested();
                if (work.LeaseExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime) return;
                var provider = dispatch.ServiceProvider.GetRequiredService<IWhatsAppFlowProvider>();
                if (work.Step == WhatsAppFlowDeprecationStep.Deprecate)
                {
                    var marked = await dispatch.ServiceProvider.GetRequiredService<IWhatsAppFlowDeprecationService>()
                        .MarkEffectStartedAsync(work, dispatchToken);
                    if (marked.IsFailure) return;
                    dispatchToken.ThrowIfCancellationRequested();
                    effectAttemptStarted = true;
                    effect = await provider.DeprecateAsync(account.Value, work.MetaFlowId, dispatchToken);
                }
                else verification = await provider.GetStateAsync(account.Value, work.MetaFlowId, work.TechnicalName, dispatchToken);
            }
        }
        catch (OperationCanceledException) when (dispatchToken.IsCancellationRequested)
        {
            if (effectAttemptStarted) effect = new(false, true, null, []);
            else if (work.Step == WhatsAppFlowDeprecationStep.Deprecate) effect = new(false, false, null, [], FlowErrors.AccountUnavailable.Code);
            else verification = new(false, WhatsAppFlowRemoteState.Unknown);
        }
        catch (Exception exception)
        {
            LogUnknown(logger, exception.GetType().Name);
            if (effectAttemptStarted) effect = new(false, true, null, []);
            else if (work.Step == WhatsAppFlowDeprecationStep.Deprecate) effect = new(false, false, null, [], FlowErrors.AccountUnavailable.Code);
            else verification = new(false, WhatsAppFlowRemoteState.Unknown);
        }
        await using var completion = scopes.CreateAsyncScope();
        var service = completion.ServiceProvider.GetRequiredService<IWhatsAppFlowDeprecationService>();
        using var completionDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10), timeProvider);
        if (effect is not null) await service.CompleteEffectAsync(work, effect, completionDeadline.Token);
        else if (verification is not null) await service.CompleteVerificationAsync(work, verification, completionDeadline.Token);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ProcessFlowsInBackground) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessNextAsync(null, stoppingToken); }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested) { LogUnknown(logger, exception.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, stoppingToken);
        }
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "The WhatsApp Flow retirement outcome could not be confirmed after {ExceptionType}; durable recovery will inspect its state before another effect.")]
    private static partial void LogUnknown(ILogger logger, string exceptionType);
}
