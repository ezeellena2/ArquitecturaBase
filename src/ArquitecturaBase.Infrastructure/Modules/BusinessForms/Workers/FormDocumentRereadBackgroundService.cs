using System.Collections.Concurrent;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;

/// <summary>
/// DOC6 after publishing, out of the publication's unit of work: each round applies the extractions of the documents read again that
/// finished, then reserves each request whose form has a published version not read again yet, or with documents still pending for it,
/// asks each pending document's extraction and records what became of it, every step in its own scope. A failed step is logged (a result by
/// its code, an exception by its type) and never stops the other requests; a document whose step threw stays pending for a later round, up
/// to <see cref="MaxThrows"/> times in a row, and when the engine cannot take more now the rest of that request waits with it. Tests turn it
/// off and call <see cref="ProcessDueAsync"/>.
/// </summary>
internal sealed partial class FormDocumentRereadBackgroundService(IServiceScopeFactory scopes, TimeProvider timeProvider,
    IOptions<FormDocumentRereadWorkerOptions> options, ILogger<FormDocumentRereadBackgroundService> logger) : BackgroundService
{
    /// <summary>
    /// How many times in a row the steps of one document may throw before it leaves the pending ones, logged by its code: otherwise its
    /// request would stay held for good, and with it that person's other requests.
    /// </summary>
    public const int MaxThrows = 3;
    // The steps that threw in a row for each document of a request, on this host; a record that ends clears them.
    private readonly ConcurrentDictionary<(Guid ProposalId, Guid DocumentId), int> throws = new();

    public async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        FormDocumentRereadDueResponse due;
        await using (var scope = scopes.CreateAsyncScope())
            due = await scope.ServiceProvider.GetRequiredService<IFormDocumentRereadService>().ListDueAsync(cancellationToken);
        foreach (var documentId in due.DocumentIds)
            await StepAsync(services => services.GetRequiredService<IFormReceivedDocumentExtractionService>().ApplyAsync(documentId, cancellationToken),
                cancellationToken);
        foreach (var proposalId in due.ProposalIds)
        {
            FormDocumentRereadWorkResponse? work = null;
            await StepAsync(async services =>
            {
                var reserved = await services.GetRequiredService<IFormDocumentRereadService>().ReserveAsync(proposalId, cancellationToken);
                work = reserved.IsSuccess ? reserved.Value : null;
                return reserved;
            }, cancellationToken);
            if (work is null) continue;
            foreach (var documentId in work.DocumentIds)
            {
                var key = (work.ProposalId, documentId);
                Result? read = null;
                if (throws.GetValueOrDefault(key) < MaxThrows)
                {
                    await StepAsync(async services => read = await services.GetRequiredService<IFormReceivedDocumentExtractionService>()
                        .RereadAsync(new(documentId, work.DefinitionId, work.ActorUserId, work.Culture, work.TimeZoneId), cancellationToken), cancellationToken);
                    // A step that threw left the document pending: once the round's hold passes, a later one tries it again.
                    if (read is null && Threw(key) < MaxThrows) continue;
                }
                if (read is null)
                {
                    // Its steps threw MaxThrows times in a row: it leaves the pending ones, as one whose extraction could not be asked.
                    read = FormReceivedDocumentErrors.ExtractionUnavailable;
                    LogLeftOut(logger, MaxThrows, read.Error.Code);
                }
                else if (read.IsFailure) LogNotRead(logger, read.Error.Code);
                var goOn = true;
                var recorded = false;
                await StepAsync(async services =>
                {
                    var outcome = await services.GetRequiredService<IFormDocumentRereadService>()
                        .RecordAsync(new(work.ProposalId, work.VersionId, documentId, read.IsFailure ? read.Error : null), cancellationToken);
                    recorded = true;
                    goOn = outcome.IsFailure || outcome.Value;
                    return outcome;
                }, cancellationToken);
                // A record that threw leaves it pending too, one more in a row; one that ended clears them.
                if (recorded) throws.TryRemove(key, out _);
                else Threw(key);
                if (!goOn) break;
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
                LogFailed(logger, exception.GetType().Name);
            }
            await Task.Delay(options.Value.PollInterval, timeProvider, stoppingToken);
        }
    }

    // One step in a scope of its own: its failure, an error or an exception, never stops the next one.
    private async Task StepAsync<T>(Func<IServiceProvider, Task<T>> step, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await step(scope.ServiceProvider);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, exception.GetType().Name);
        }
    }

    private int Threw((Guid ProposalId, Guid DocumentId) key) => throws.AddOrUpdate(key, 1, (_, count) => count + 1);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A step of reading again the documents of a form built from documents failed with {ExceptionType}; a later round tries again.")]
    private static partial void LogFailed(ILogger logger, string exceptionType);

    [LoggerMessage(Level = LogLevel.Information, Message = "A document of a form built from documents was not read again: {ErrorCode}.")]
    private static partial void LogNotRead(ILogger logger, string errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A document of a form built from documents left the ones to read again after {Throws} steps in a row threw: {ErrorCode}.")]
    private static partial void LogLeftOut(ILogger logger, int throws, string errorCode);
}
