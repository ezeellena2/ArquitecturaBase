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
        WhatsAppFlowEffectResponse? effect = null; WhatsAppFlowVerificationResponse? verification = null;
        await using (var dispatch = scopes.CreateAsyncScope())
        {
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
        await using var completion = scopes.CreateAsyncScope();
        var service = completion.ServiceProvider.GetRequiredService<IWhatsAppFlowDispatchService>();
        if (effect is not null) await service.CompleteEffectAsync(work, effect, cancellationToken);
        else if (verification is not null) await service.CompleteVerificationAsync(work, verification, cancellationToken);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ProcessFlowsInBackground) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessNextAsync(null, stoppingToken); }
            catch (Exception) when (!stoppingToken.IsCancellationRequested) { LogUnknown(logger); }
            await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, stoppingToken);
        }
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "The WhatsApp Flow outcome could not be confirmed; durable recovery will inspect it without repeating an unknown creation.")]
    private static partial void LogUnknown(ILogger logger);
}
