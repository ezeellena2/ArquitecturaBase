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
        // Task 9: a document that was not recognized, registered again with a proposal for the form its person chose in the channel, is
        // recognized for it (below); any other repeated registration answers as before.
        var replay = await replies.FindReplayAsync(request, cancellationToken);
        Guid? recognizing = replay is { Kind: StructuredDocumentReplyKind.Unknown, DocumentId: { } existing }
            && request is { FocusDefinitionId: not null, Proposal: not null } ? existing : null;
        if (replay is not null && recognizing is null) return replay;
        if (recognizing is null && !await guard.HasAsync(request.SenderUserId, BusinessFormsPermissions.RecordsCreate, cancellationToken)) return Unavailable;
        var documentId = recognizing ?? Guid.CreateVersion7();
        // A file that cannot be opened may come without a copy (task 8): the inbox keeps what its person sent and no file. One recognized again
        // keeps the copy it has.
        string? storageKey = null;
        if (recognizing is null && request.Copy is { } copy && (storageKey = await copies.CopyAsync(documentId, copy, cancellationToken)) is null) return Unavailable;
        var classification = await replies.ClassifyAsync(request, cancellationToken, recognizing);
        // It changes only with a proposal for the form chosen that recognizes it.
        if (recognizing is not null && (classification.Proposal is not { Candidates: [var first, ..] } || first.DefinitionId != request.FocusDefinitionId
            || classification.State is not (FormReceivedDocumentState.Proposed or FormReceivedDocumentState.Discarded))) return replay!;
        var saved = await unitOfWork.ExecuteInTransactionAsync<Result<StructuredDocumentRegistration?>>(async ct =>
        {
            if (recognizing is { } recognized) return await RecognizeCoreAsync(recognized, request, classification, ct);
            await repository.LockSourceAsync(request.ChannelId, request.SourceId, ct);
            if (await repository.GetBySourceAsync(request.ChannelId, request.SourceId, ct) is not null)
                return Result.Success<StructuredDocumentRegistration?>(null);
            var registered = recorder.Register(documentId, storageKey, request, classification);
            if (registered.IsFailure) return registered.Error;
            return classification.Reply with { DocumentId = documentId, ReplyExpiresAtUtc = request.ReplyExpiresAtUtc };
        }, CommitPolicy.OnSuccess, cancellationToken);
        if (saved.IsFailure) return recognizing is null ? Unavailable : replay!;
        return saved.Value ?? await replies.FindReplayAsync(request, cancellationToken) ?? Unavailable;
    }

    // Task 9: under the document's lock it is still unclassified for not being recognized, and of the same person and channel source.
    private async Task<Result<StructuredDocumentRegistration?>> RecognizeCoreAsync(Guid documentId, StructuredDocumentRegistrationRequest request,
        FormReceivedDocumentClassification classification, CancellationToken ct)
    {
        await repository.LockAsync(documentId, ct);
        if (await repository.GetAsync(documentId, ct) is not { State: FormReceivedDocumentState.Unclassified, ReasonCode: FormReceivedDocumentReplyPolicy.Unknown } document
            || document.SenderUserId != request.SenderUserId || document.ChannelId != request.ChannelId || document.SourceId != request.SourceId)
            return Result.Success<StructuredDocumentRegistration?>(null);
        var recognized = recorder.Recognize(document, request, classification);
        return recognized.IsFailure ? recognized.Error : classification.Reply with { DocumentId = documentId, ReplyExpiresAtUtc = request.ReplyExpiresAtUtc };
    }

    public async Task<StructuredDocumentSendPreparation> PrepareSendAsync(StructuredDocumentSendRequest request, CancellationToken cancellationToken)
    {
        // The person's choice is only a candidate of the stored proposal; the document follows it, under its lock, before the form is prepared,
        // so a reviewer who saw the previous form gets a revision conflict and the capture checks the form the person completes.
        if (await sends.ChoiceAsync(request, cancellationToken) is not { } choice) return new(StructuredDocumentSendState.Unavailable);
        // Review of task 9: only while its person is waited for; after its deadline it is for the team, and no form goes to them.
        if (!Waits(choice.Row.ReplyExpiresAtUtc)) return new(StructuredDocumentSendState.Expired);
        if (choice.Row.DefinitionId != request.DefinitionId || choice.Row.VersionId != choice.Candidate.VersionId)
        {
            var chosen = await unitOfWork.ExecuteInTransactionAsync<Result<bool>>(async ct =>
            {
                await repository.LockAsync(request.DocumentId, ct);
                if (await repository.GetAsync(request.DocumentId, ct) is not { } document || document.SenderUserId != request.RecipientUserId)
                    return FormReceivedDocumentErrors.NotFound;
                if (!Waits(document.ReplyExpiresAtUtc)) return false;
                if (document.DefinitionId == request.DefinitionId && document.VersionId == choice.Candidate.VersionId) return true;
                var changed = document.Choose(request.DefinitionId, choice.Candidate.VersionId);
                if (changed.IsFailure) return changed.Error;
                var recorded = recorder.Record(document.Id, FormReceivedDocumentEventKind.FormChosen, null);
                return recorded.IsFailure ? recorded.Error : true;
            }, CommitPolicy.OnSuccess, cancellationToken);
            if (chosen.IsFailure) return new(StructuredDocumentSendState.Unavailable);
            if (!chosen.Value) return new(StructuredDocumentSendState.Expired);
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

    // A channel's document carries its deadline. Closing task 9: one without a deadline waits for nobody (a reviewer reopened it, or it was
    // registered without one), as IsForReview reads it: it is for the team and no form goes to its person.
    private bool Waits(DateTime? replyExpiresAtUtc) => replyExpiresAtUtc is { } deadline && deadline > clock.GetUtcNow().UtcDateTime;

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
