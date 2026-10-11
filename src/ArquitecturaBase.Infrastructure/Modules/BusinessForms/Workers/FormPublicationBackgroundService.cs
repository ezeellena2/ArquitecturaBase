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
        FormPublicationWorkResponse? work;
        await using (var reservation = scopes.CreateAsyncScope())
        {
            var result = await reservation.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>().ReserveAsync(operationId, cancellationToken);
            work = result.IsSuccess ? result.Value : null;
        }
        if (work is null) return;
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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ProcessInBackground) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessNextAsync(null, stoppingToken); }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested) { LogUnknown(logger, exception.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, stoppingToken);
        }
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "The form publication outcome could not be confirmed after {ExceptionType}; durable recovery will inspect it.")]
    private static partial void LogUnknown(ILogger logger, string exceptionType);
}
