using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;

/// <summary>
/// Refreshes the External catalogs that are due, each in its own scope: one that fails does not stop the others, and its
/// failure stays recorded on the catalog. Tests turn it off and call <see cref="ProcessDueAsync"/>.
/// </summary>
internal sealed partial class FormCatalogFeedBackgroundService(IServiceScopeFactory scopes, TimeProvider timeProvider,
    IOptions<FormCatalogFeedWorkerOptions> options, ILogger<FormCatalogFeedBackgroundService> logger) : BackgroundService
{
    public async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> keys;
        await using (var scope = scopes.CreateAsyncScope())
            keys = await scope.ServiceProvider.GetRequiredService<IFormCatalogFeedService>().ListDueAsync(cancellationToken);
        foreach (var key in keys)
        {
            try
            {
                await using (var reservation = scopes.CreateAsyncScope())
                {
                    var reserved = await reservation.ServiceProvider.GetRequiredService<IFormCatalogFeedService>().ReserveAsync(key, cancellationToken);
                    if (reserved.IsFailure || !reserved.Value) continue;
                }
                Application.Models.StructuredData.StructuredFeedReadResponse read;
                await using (var reading = scopes.CreateAsyncScope())
                    read = await reading.ServiceProvider.GetRequiredService<IFormCatalogFeedService>().ReadAsync(key, cancellationToken);
                await using var applying = scopes.CreateAsyncScope();
                await applying.ServiceProvider.GetRequiredService<IFormCatalogFeedService>().ApplyAsync(key, read, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogFailed(logger, key, exception.GetType().Name);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ProcessInBackground) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogCycleFailed(logger, exception.GetType().Name);
            }
            await Task.Delay(options.Value.PollInterval, timeProvider, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The round of fed catalogs failed after {ExceptionType}; the next one tries again.")]
    private static partial void LogCycleFailed(ILogger logger, string exceptionType);
    [LoggerMessage(Level = LogLevel.Warning, Message = "The fed catalog {CatalogKey} could not be refreshed after {ExceptionType}; it is tried again later.")]
    private static partial void LogFailed(ILogger logger, string catalogKey, string exceptionType);
}
