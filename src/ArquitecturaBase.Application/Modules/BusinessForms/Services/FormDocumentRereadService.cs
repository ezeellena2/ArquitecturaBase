using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Configuration;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// DOC6 after publishing, out of the publication's unit of work: a request whose draft has an active version published in a channel with its
/// reading on is reserved for that version under its lock: the first time all its documents are pending, and a later round takes the ones
/// still pending once nothing holds them. Each round holds them for <see cref="Hold"/> (another host leaves them alone), and the worker asks
/// each one's extraction for the form with the context of whoever asked for it and records what became of it: read again, or left as it is
/// (no longer unclassified, its form gone, or a failure that will not pass), it leaves the pending ones; when the engine cannot take it now
/// (its quota or its capacity), they all wait as long as it says. A step that fails halfway leaves the document pending for a later round.
/// The re-reads run as the asker, out of their own daily quota of the channel: a person's requests read at most
/// <see cref="FormDocumentRereadOptions.DailyPerAsker"/> a UTC day between them (only a reading the engine took counts) and the rest continue
/// the next day. Under that person's lock their rounds go one at a time, and a newer version waits for the round that holds the pending
/// ones, so the count stays at most about the share: a round that outlives its hold can overlap the next one, and a reading the engine took
/// goes uncounted when its step is cut before it is recorded or its save loses to a change of the document. What the worker lists also
/// brings the extractions that still run.
/// </summary>
internal sealed class FormDocumentRereadService(IFormDocumentProposalReader reader, IFormDocumentProposalRepository repository,
    IFormReceivedDocumentReader documents, IUnitOfWork unitOfWork, TimeProvider clock, IOptions<FormDocumentRereadOptions> options)
    : IFormDocumentRereadService
{
    /// <summary>How many requests to start and extractions to apply one round looks at.</summary>
    public const int BatchSize = 20;
    /// <summary>How long a round holds the pending documents it took.</summary>
    public static readonly TimeSpan Hold = TimeSpan.FromMinutes(2);
    /// <summary>How long the pending ones wait when the engine cannot take them now and says nothing about when.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    public async Task<FormDocumentRereadDueResponse> ListDueAsync(CancellationToken cancellationToken) =>
        new(await reader.ListRereadsDueAsync(clock.GetUtcNow().UtcDateTime, BatchSize, cancellationToken),
            await reader.ListRereadingDocumentIdsAsync(BatchSize, cancellationToken));

    public async Task<Result<FormDocumentRereadWorkResponse?>> ReserveAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        if (await reader.FindAsync(proposalId, cancellationToken) is not { DefinitionId: { } definitionId } row
            || await documents.FindActiveFormAsync(definitionId, cancellationToken) is not { AppOnly: false } form
            || FormDocumentFormPolicy.Reading(form) is not { Enabled: true })
            return Result.Success<FormDocumentRereadWorkResponse?>(null);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(nowUtc);
        return await unitOfWork.ExecuteInTransactionAsync<Result<FormDocumentRereadWorkResponse?>>(async ct =>
        {
            await repository.LockAskerAsync(row.ActorUserId, ct);
            // One round of a person's re-reads at a time: another of their requests held by its round, or waiting for the engine, goes first.
            if (await repository.ExistsRereadHeldAsync(row.ActorUserId, proposalId, nowUtc, ct)) return Result.Success<FormDocumentRereadWorkResponse?>(null);
            await repository.LockAsync(proposalId, ct);
            if (await repository.GetAsync(proposalId, ct) is not { } proposal || proposal.DefinitionId != definitionId)
                return Result.Success<FormDocumentRereadWorkResponse?>(null);
            // A version not read again yet starts over with all of them once nothing holds the pending ones; the same one continues with the
            // pending ones nothing holds.
            var held = proposal.RereadsFor(form.VersionId) ? proposal.ClaimReread(nowUtc, nowUtc.Add(Hold))
                : proposal.StartReread(form.VersionId, nowUtc, nowUtc.Add(Hold));
            if (held.IsFailure) return Result.Success<FormDocumentRereadWorkResponse?>(null);
            // The asker's daily share between all their requests; with none left, the pending ones wait for the next UTC day.
            var left = options.Value.DailyPerAsker - await repository.CountRereadsOnDayAsync(proposal.ActorUserId, today, ct);
            if (left <= 0)
            {
                var deferred = proposal.DeferReread(today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                return deferred.IsFailure ? deferred.Error : Result.Success<FormDocumentRereadWorkResponse?>(null);
            }
            return new FormDocumentRereadWorkResponse(proposal.Id, definitionId, form.VersionId, proposal.ActorUserId, proposal.Culture, proposal.TimeZoneId,
                [.. proposal.PendingRereadIds.Take(left)]);
        }, CommitPolicy.OnSuccess, cancellationToken);
    }

    public async Task<Result<bool>> RecordAsync(FormDocumentRereadOutcomeRequest request, CancellationToken cancellationToken)
    {
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var wait = request.Failure is { } failure && IsTransient(failure) ? RetryAfter(failure) : (TimeSpan?)null;
        return await unitOfWork.ExecuteInTransactionAsync<Result<bool>>(async ct =>
        {
            await repository.LockAsync(request.ProposalId, ct);
            if (await repository.GetAsync(request.ProposalId, ct) is not { } proposal) return true;
            // Only a reading the engine took uses the asker's daily share, also one of a round that a newer version started over meanwhile.
            if (request.Failure is null)
            {
                var counted = proposal.CountRereads(DateOnly.FromDateTime(nowUtc), 1);
                if (counted.IsFailure) return counted.Error;
            }
            // A newer version started over (this round outlived its hold): the pending ones are that version's, and this round stops.
            if (!proposal.RereadsFor(request.VersionId)) return false;
            if (wait is not { } delay)
            {
                proposal.Reread(request.DocumentId);
                return true;
            }
            var deferred = proposal.DeferReread(nowUtc.Add(delay));
            return deferred.IsFailure ? deferred.Error : false;
        }, CommitPolicy.OnSuccess, cancellationToken);
    }

    // What passes: the engine's quota and capacity (too many requests) and what it says may be retried (a turn still active).
    private static bool IsTransient(Error error) =>
        error.Type == ErrorType.TooManyRequests || error.Metadata?.TryGetValue("retryable", out var retryable) == true && retryable is true;

    // As long as the engine says (its quota resets the next day), or a minute when it does not say.
    private static TimeSpan RetryAfter(Error error) =>
        error.Metadata?.TryGetValue("retryAfter", out var seconds) == true && seconds is int value && value > 0 ? TimeSpan.FromSeconds(value) : RetryDelay;
}
