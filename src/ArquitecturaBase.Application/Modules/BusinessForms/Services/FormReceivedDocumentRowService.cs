using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The inbox's port for the rows of a spreadsheet (VC1-A, decision 12), in a unit of work of its own that the channel calls outside any of its
/// own. Registering is idempotent by channel and source id: a repeated one answers what was stored. The spreadsheet is copied once, named after
/// its first entry, and read back before the unit of work (<see cref="FormReceivedDocumentRowPolicy"/>); its entries are stored with their
/// history under the source's lock. Nothing crosses as an <c>Error</c>: what cannot be done is Unavailable.
/// </summary>
internal sealed class FormReceivedDocumentRowService(IFormReceivedDocumentRepository repository, IFormReceivedDocumentReader reader,
    FormReceivedDocumentRowPolicy rows, FormReceivedDocumentCopyPolicy copies, FormReceivedDocumentAccessGuard guard,
    FormReceivedDocumentRecorder recorder, IUnitOfWork unitOfWork) : IFormReceivedDocumentRowService
{
    private static readonly StructuredDocumentRowsRegistration Unavailable = new(StructuredDocumentReplyKind.Unavailable);

    public async Task<StructuredDocumentRowsRegistration> RegisterRowsAsync(StructuredDocumentRowsRegistrationRequest request, CancellationToken cancellationToken)
    {
        if (!Valid(request)) return Unavailable;
        if (await ReplayAsync(request, cancellationToken) is { } replay) return replay;
        if (!await guard.HasAsync(request.SenderUserId, BusinessFormsPermissions.RecordsCreate, cancellationToken)) return Unavailable;
        var copyId = Guid.CreateVersion7();
        if (await copies.CopyAsync(copyId, request.Copy, cancellationToken) is not { } storageKey) return Unavailable;
        if (await rows.ClassifyAsync(request, copyId, storageKey, cancellationToken) is not { } classification) return Unavailable;
        var saved = await unitOfWork.ExecuteInTransactionAsync<Result<bool>>(async ct =>
        {
            await repository.LockSourceAsync(request.ChannelId, request.SourceId, ct);
            if (await repository.ExistsSourceAsync(request.ChannelId, request.SourceId, ct)) return false;
            var registered = classification.Single is { } single ? recorder.RegisterSpreadsheet(copyId, storageKey, request, single)
                : recorder.RegisterRows(request, storageKey, classification.Entries);
            return registered.IsFailure ? registered.Error : true;
        }, CommitPolicy.OnSuccess, cancellationToken);
        if (saved.IsFailure) return Unavailable;
        return saved.Value ? classification.Reply : await ReplayAsync(request, cancellationToken) ?? Unavailable;
    }

    // What this channel source already has, from its sender (another sender's is not theirs to hear about).
    private async Task<StructuredDocumentRowsRegistration?> ReplayAsync(StructuredDocumentRowsRegistrationRequest request, CancellationToken ct)
    {
        var stored = await reader.ListBySourceAsync(request.ChannelId, request.SourceId, ct);
        if (stored.Count == 0) return null;
        return stored.Any(row => row.SenderUserId != request.SenderUserId) ? Unavailable : await rows.ReplayAsync(stored, request.Culture, ct);
    }

    private static bool Valid(StructuredDocumentRowsRegistrationRequest request) =>
        request.ChannelId is { Length: > 0 and <= FormReceivedDocument.MaxChannelIdLength } channel && channel.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')
        && request.SourceId is { Length: > 0 and <= FormReceivedDocument.MaxSourceIdLength } && !request.SourceId.Any(char.IsControl)
        && request.SenderUserId != Guid.Empty && request.Culture is "es" or "en" && request.ReceivedAtUtc.Kind == DateTimeKind.Utc
        && request.Copy is { StorageKey.Length: > 0, Name.Length: > 0, Pages: null } copy && FormReceivedDocumentRowPolicy.Format(copy.MediaType) is not null
        && copy.Size is > 0 and <= StructuredAttachmentLimits.MaximumBytes && copy.Sha256 is { Length: 64 } sha && sha.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')
        && (request.Proposal is not null ? request is { NotRead: null, SampleSha256: not null } : request.NotRead == StructuredDocumentNotRead.Quota)
        && request.ReplyExpiresAtUtc is null or { Kind: DateTimeKind.Utc };
}
