using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
internal sealed partial class FormPublicationBackgroundService(IServiceScopeFactory scopes, TimeProvider timeProvider,
    IOptions<FormPublicationWorkerOptions> options, ILogger<FormPublicationBackgroundService> logger) : BackgroundService
{
    public async Task ProcessNextAsync(Guid? operationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> operationIds;
        if (operationId is { } requestedId) operationIds = [requestedId];
        else
        {
            await using var candidates = scopes.CreateAsyncScope();
            operationIds = await candidates.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>()
                .ListDueOperationIdsAsync(cancellationToken);
        }
        FormPublicationWorkResponse? work = null;
        foreach (var candidateId in operationIds)
        {
            await using var reservation = scopes.CreateAsyncScope();
            var result = await reservation.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>()
                .ReserveAsync(candidateId, cancellationToken);
            if (result.IsFailure) return;
            if (result.Value is null) continue;
            work = result.Value;
            break;
        }
        if (work is null) return;
        // F18: the dispatch and its result happen inside the trace of the request that accepted the publication.
        using var activity = FormsTelemetry.StartPublication(work.OperationId, work.TraceParent);
        try
        {
            IReadOnlyList<FormPublicationChannelProgress> progress;
            await using (var dispatch = scopes.CreateAsyncScope())
            {
                var dispatched = await dispatch.ServiceProvider.GetRequiredService<IFormPublicationChannelDispatchService>()
                    .DispatchAsync(work, cancellationToken);
                if (dispatched.IsFailure) return;
                progress = dispatched.Value.Progress;
            }
            await using var completion = scopes.CreateAsyncScope();
            await completion.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>().CompleteAsync(work, progress, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // F18: the log names the operation; the round that called this one logs the cycle.
            LogUnknown(logger, work.OperationId, exception.GetType().Name);
            throw;
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ProcessInBackground) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessNextAsync(null, stoppingToken); }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested) { LogCycleFailed(logger, exception.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, stoppingToken);
        }
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "The outcome of form publication operation {OperationId} could not be confirmed after {ExceptionType}; durable recovery will inspect it.")]
    private static partial void LogUnknown(ILogger logger, Guid operationId, string exceptionType);
    [LoggerMessage(Level = LogLevel.Warning, Message = "A round of form publications failed after {ExceptionType}; the next one tries again.")]
    private static partial void LogCycleFailed(ILogger logger, string exceptionType);
}
