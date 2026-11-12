using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The inbox's port for a channel that receives documents (decisions 9 and 11), implemented by the service because it decides in a unit
/// of work of its own, which the channel calls outside any transaction of its own. Registering is idempotent by channel and source id:
/// a repeated one answers the same; the copy is made and read back before the unit of work, and the document is stored with its history
/// under the source's lock; only a PDF, a photo (JPEG or PNG) or a Word is taken. Choosing a form aligns the document to that candidate of
/// its proposal. «Otro» leaves it unclassified and notes the missing form in the same unit of work. Nothing crosses as an <c>Error</c>: what
/// cannot be done is Unavailable.
/// </summary>
internal sealed class FormReceivedDocumentChannelService(IFormReceivedDocumentRepository repository, FormReceivedDocumentReplyPolicy replies,
    FormReceivedDocumentSendPolicy sends, FormReceivedDocumentCopyPolicy copies, FormReceivedDocumentAccessGuard guard, FormReceivedDocumentRecorder recorder,
    IUnitOfWork unitOfWork, TimeProvider clock) : IFormReceivedDocumentChannelService
{
    private static readonly StructuredDocumentRegistration Unavailable = new(null, StructuredDocumentReplyKind.Unavailable);

    public Task<StructuredDocumentLoading> FindLoadingAsync(Guid personUserId, CancellationToken cancellationToken) =>
        guard.FindLoadingAsync(personUserId, cancellationToken);

    public async Task<StructuredDocumentRegistration> RegisterAsync(StructuredDocumentRegistrationRequest request, CancellationToken cancellationToken)
    {
        if (!Valid(request)) return Unavailable;
        if (await replies.FindReplayAsync(request, cancellationToken) is { } replay) return replay;
        if (!await guard.HasAsync(request.SenderUserId, BusinessFormsPermissions.RecordsCreate, cancellationToken)) return Unavailable;
        var documentId = Guid.CreateVersion7();
        // A file that cannot be opened may come without a copy (task 8): the inbox keeps what its person sent and no file.
        string? storageKey = null;
        if (request.Copy is { } copy && (storageKey = await copies.CopyAsync(documentId, copy, cancellationToken)) is null) return Unavailable;
        var classification = await replies.ClassifyAsync(request, cancellationToken);
        var saved = await unitOfWork.ExecuteInTransactionAsync<Result<StructuredDocumentRegistration?>>(async ct =>
        {
            await repository.LockSourceAsync(request.ChannelId, request.SourceId, ct);
            if (await repository.GetBySourceAsync(request.ChannelId, request.SourceId, ct) is not null)
                return Result.Success<StructuredDocumentRegistration?>(null);
            var registered = recorder.Register(documentId, storageKey, request, classification);
            if (registered.IsFailure) return registered.Error;
            return classification.Reply with { DocumentId = documentId };
        }, CommitPolicy.OnSuccess, cancellationToken);
        if (saved.IsFailure) return Unavailable;
        return saved.Value ?? await replies.FindReplayAsync(request, cancellationToken) ?? Unavailable;
    }

    public async Task<StructuredDocumentSendPreparation> PrepareSendAsync(StructuredDocumentSendRequest request, CancellationToken cancellationToken)
    {
        // The person's choice is only a candidate of the stored proposal; the document follows it, under its lock, before the form is prepared,
        // so a reviewer who saw the previous form gets a revision conflict and the capture checks the form the person completes.
        if (await sends.ChoiceAsync(request, cancellationToken) is not { } choice) return new(StructuredDocumentSendState.Unavailable);
        if (choice.Row.DefinitionId != request.DefinitionId || choice.Row.VersionId != choice.Candidate.VersionId)
        {
            var chosen = await unitOfWork.ExecuteInTransactionAsync<Result>(async ct =>
            {
                await repository.LockAsync(request.DocumentId, ct);
                if (await repository.GetAsync(request.DocumentId, ct) is not { } document || document.SenderUserId != request.RecipientUserId)
                    return FormReceivedDocumentErrors.NotFound;
                if (document.DefinitionId == request.DefinitionId && document.VersionId == choice.Candidate.VersionId) return Result.Success();
                var changed = document.Choose(request.DefinitionId, choice.Candidate.VersionId);
                return changed.IsFailure ? changed : recorder.Record(document.Id, FormReceivedDocumentEventKind.FormChosen, null);
            }, CommitPolicy.OnSuccess, cancellationToken);
            if (chosen.IsFailure) return new(StructuredDocumentSendState.Unavailable);
        }
        return await sends.PrepareAsync(request, cancellationToken);
    }

    public async Task<StructuredDocumentChoiceState> ChooseOtherAsync(StructuredDocumentChoiceRequest request, CancellationToken cancellationToken)
    {
        if (request.DocumentId == Guid.Empty || request.SenderUserId == Guid.Empty || request.Culture is not ("es" or "en")
            || request.ChosenAtUtc.Kind != DateTimeKind.Utc) return StructuredDocumentChoiceState.Unavailable;
        var result = await unitOfWork.ExecuteInTransactionAsync<Result<StructuredDocumentChoiceState>>(async ct =>
        {
            // The missing requests' lock first: the document's is always the last one taken.
            await recorder.LockMissingRequestsAsync(ct);
            await repository.LockAsync(request.DocumentId, ct);
            var document = await repository.GetAsync(request.DocumentId, ct);
            if (document is null || document.ChannelId != request.ChannelId || document.SourceId != request.SourceId
                || document.SenderUserId != request.SenderUserId) return StructuredDocumentChoiceState.Unavailable;
            if (document is { State: FormReceivedDocumentState.Unclassified, ReasonCode: FormReceivedDocument.ChoseOther })
                return StructuredDocumentChoiceState.AlreadyRecorded;
            var chosen = document.ChooseOther(clock.GetUtcNow().UtcDateTime);
            if (chosen.IsFailure) return StructuredDocumentChoiceState.Unavailable;
            var noted = await recorder.NoteMissingFormAsync(document, request.Culture, request.ChosenAtUtc, ct);
            if (noted.IsFailure) return noted.Error;
            var recorded = recorder.Record(document.Id, FormReceivedDocumentEventKind.ChoseOther, null);
            return recorded.IsFailure ? recorded.Error : StructuredDocumentChoiceState.Recorded;
        }, CommitPolicy.OnSuccess, cancellationToken);
        return result.IsSuccess ? result.Value : StructuredDocumentChoiceState.Unavailable;
    }

    private static bool Valid(StructuredDocumentRegistrationRequest request) =>
        request.ChannelId is { Length: > 0 and <= FormReceivedDocument.MaxChannelIdLength } channel && channel.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')
        && request.SourceId is { Length: > 0 and <= FormReceivedDocument.MaxSourceIdLength } && !request.SourceId.Any(char.IsControl)
        && request.SenderUserId != Guid.Empty && request.Culture is "es" or "en" && request.ReceivedAtUtc.Kind == DateTimeKind.Utc
        && (request.Copy is { } copy ? request.File is null && ValidCopy(copy) : ValidFile(request))
        && (request.Reading is null) == (request.NotRead is not null) && request.ReplyExpiresAtUtc is null or { Kind: DateTimeKind.Utc };

    private static bool ValidCopy(StructuredDocumentCopy copy) =>
        copy is { StorageKey.Length: > 0, Name.Length: > 0 } && FormReceivedDocumentExtractionPolicy.Kind(copy.MediaType) is not null
        && copy.Size is > 0 and <= StructuredAttachmentLimits.MaximumBytes && copy.Pages is null or (>= 1 and <= StructuredAttachmentLimits.MaximumPages)
        && copy.Sha256 is { Length: 64 } sha && sha.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    // Without a copy, only a file that cannot be opened (task 8): its name, the media type the channel declared and its size when known.
    private static bool ValidFile(StructuredDocumentRegistrationRequest request) =>
        request is { NotRead: StructuredDocumentNotRead.Rejected, Rejection: not null, Reading: null, Proposal: null, File: { Name.Length: > 0 } file }
        && file.MediaType is { Length: > 0 and <= StructuredDocumentFile.MaxMediaTypeLength } type && !type.Any(char.IsControl) && file.Size is null or > 0;
}
