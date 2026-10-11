using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;
internal sealed partial class WhatsAppFlowBackgroundService(IServiceScopeFactory scopes, IOptions<WhatsAppOptions> options,
    TimeProvider timeProvider, ILogger<WhatsAppFlowBackgroundService> logger) : BackgroundService
{
    public async Task ProcessNextAsync(Guid? operationId, CancellationToken cancellationToken)
    {
        WhatsAppFlowWorkResponse? work;
        await using (var reservation = scopes.CreateAsyncScope())
        {
            var result = await reservation.ServiceProvider.GetRequiredService<IWhatsAppFlowDispatchService>().ReserveAsync(operationId, cancellationToken);
            work = result.IsSuccess ? result.Value : null;
        }
        if (work is null) return;
        var remaining = work.LeaseExpiresAtUtc - timeProvider.GetUtcNow().UtcDateTime;
        if (remaining <= TimeSpan.Zero) return;
        using var deadline = new CancellationTokenSource(remaining, timeProvider);
        using var dispatchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var dispatchToken = dispatchCancellation.Token;
        var effectAttemptStarted = false;
        WhatsAppFlowEffectResponse? effect = null; WhatsAppFlowVerificationResponse? verification = null;
        try
        {
            await using var dispatch = scopes.CreateAsyncScope();
            var environment = dispatch.ServiceProvider.GetRequiredService<IWhatsAppFlowEnvironment>();
            var account = await environment.VerifyAccountAsync(dispatchToken);
            if (account.IsFailure)
                effect = new(false, false, null, [], account.Error.Code);
            else if (!account.Value.HasSameIdentity(work.Manifest.Account))
                effect = new(false, false, null, [], FlowErrors.AccountMismatch.Code);
            else
            {
                dispatchToken.ThrowIfCancellationRequested();
                if (work.LeaseExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime) return;
                var provider = dispatch.ServiceProvider.GetRequiredService<IWhatsAppFlowProvider>();
                if (work.Step is WhatsAppFlowStep.Create or WhatsAppFlowStep.Upload or WhatsAppFlowStep.Publish)
                {
                    var marked = await dispatch.ServiceProvider.GetRequiredService<IWhatsAppFlowDispatchService>()
                        .MarkEffectStartedAsync(work, dispatchToken);
                    if (marked.IsFailure) return;
                    dispatchToken.ThrowIfCancellationRequested();
                    effectAttemptStarted = true;
                }
                switch (work.Step)
                {
                    case WhatsAppFlowStep.Create: effect = await provider.CreateAsync(account.Value, work.TechnicalName, dispatchToken); break;
                    case WhatsAppFlowStep.Upload: effect = await provider.UploadAsync(account.Value, work.MetaFlowId!, work.Projection.Json, dispatchToken); break;
                    case WhatsAppFlowStep.Publish: effect = await provider.PublishAsync(account.Value, work.MetaFlowId!, dispatchToken); break;
                    default:
                        verification = work.MetaFlowId is null ? await provider.InspectUnknownCreationAsync(account.Value, work.TechnicalName, dispatchToken)
                            : await provider.VerifyAsync(account.Value, work.MetaFlowId, work.TechnicalName, work.Projection.Hash, dispatchToken); break;
                }
            }
        }
        catch (OperationCanceledException) when (dispatchToken.IsCancellationRequested)
        {
            if (effectAttemptStarted) effect = new(false, true, null, []);
            else if (work.Step is WhatsAppFlowStep.Create or WhatsAppFlowStep.Upload or WhatsAppFlowStep.Publish)
                effect = new(false, false, null, [], FlowErrors.AccountUnavailable.Code);
            else verification = new(false, false, false, false, false);
        }
        await using var completion = scopes.CreateAsyncScope();
        var service = completion.ServiceProvider.GetRequiredService<IWhatsAppFlowDispatchService>();
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
    [LoggerMessage(Level = LogLevel.Warning, Message = "The WhatsApp Flow outcome could not be confirmed after {ExceptionType}; durable recovery will inspect it without repeating an unknown creation.")]
    private static partial void LogUnknown(ILogger logger, string exceptionType);
}
