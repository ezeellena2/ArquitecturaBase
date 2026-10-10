using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
internal sealed partial class FormDraftRetentionBackgroundService(IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
    ILogger<FormDraftRetentionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IFormDraftRetentionService>().PurgeExpiredResultsAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested) { RetentionFailed(logger, exception); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    [LoggerMessage(Level = LogLevel.Error, Message = "Form draft result retention failed.")]
    private static partial void RetentionFailed(ILogger logger, Exception exception);
}
