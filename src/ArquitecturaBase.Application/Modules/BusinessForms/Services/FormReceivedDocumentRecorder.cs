using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The inbox's part that runs inside its caller's unit of work: a registered document with its protected reading and proposal and the
/// numbers to search it by; a step of a document's history, with its detail protected; and the missing form a document left
/// unclassified by «Otro» notes, by its type of document (decision 11), reusing the missing-forms recorder under its lock. It never saves.
/// </summary>
internal sealed class FormReceivedDocumentRecorder(IFormReceivedDocumentRepository documents, IFormMissingRequestRepository missing,
    IFormReceivedDocumentProtector protector, FormReceivedDocumentContentPolicy contents, TimeProvider clock)
{
    public const string DetailKind = "event";

    public Result<FormReceivedDocument> Register(Guid documentId, string? storageKey, StructuredDocumentRegistrationRequest request,
        FormReceivedDocumentClassification classification)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // Without a copy, a file that cannot be opened (task 8): discarded at once with what its person sent, and nothing to read.
        if (storageKey is null)
            return request is { Copy: null, File: { } file } && classification.State == FormReceivedDocumentState.Discarded
                ? Rejected(documentId, request, file, classification, now) : FormReceivedDocumentErrors.ContentInvalid;
        if (request.Copy is not { } copy) return FormReceivedDocumentErrors.ContentInvalid;
        var received = FormReceivedDocument.Receive(documentId, request.ChannelId, request.SourceId, request.SenderUserId, request.ReceivedAtUtc,
            storageKey, FileName(copy.Name), copy.MediaType, copy.Size, copy.Sha256, copy.Pages, request.ReplyExpiresAtUtc);
        if (received.IsFailure) return received.Error;
        var document = received.Value;
        var described = document.Describe(request.Reading is { } reading ? contents.ProtectReading(documentId, reading) : null,
            classification.Proposal?.DocumentType, FormReceivedDocumentSearchPolicy.Build(request.Reading, classification.Proposal));
        if (described.IsFailure) return described.Error;
        var proposal = classification.Proposal is { } prepared ? contents.ProtectProposal(documentId, prepared) : null;
        var classified = classification.State switch
        {
            FormReceivedDocumentState.Proposed when classification.Proposal is { Candidates: [var first, ..] } =>
                document.Propose(first.DefinitionId, first.VersionId, proposal!, classification.NotSendable),
            FormReceivedDocumentState.Unclassified => document.LeaveUnclassified(classification.ReasonCode ?? FormReceivedDocumentReplyPolicy.Unknown, proposal, now),
            FormReceivedDocumentState.Discarded => document.Reject(classification.ReasonCode ?? FormReceivedDocumentReplyPolicy.Unreadable, proposal, now),
            _ => Result.Failure(FormReceivedDocumentErrors.ContentInvalid),
        };
        if (classified.IsFailure) return classified.Error;
        documents.Add(document);
        var recorded = Record(documentId, FormReceivedDocumentEventKind.Received, null, JsonNamingPolicy.CamelCase.ConvertName(classification.Reply.Kind.ToString()));
        return recorded.IsFailure ? recorded.Error : document;
    }

    private Result<FormReceivedDocument> Rejected(Guid documentId, StructuredDocumentRegistrationRequest request, StructuredDocumentFile file,
        FormReceivedDocumentClassification classification, DateTime now)
    {
        var rejected = FormReceivedDocument.ReceiveRejected(documentId, request.ChannelId, request.SourceId, request.SenderUserId, request.ReceivedAtUtc,
            FileName(file.Name), file.MediaType, file.Size, classification.ReasonCode ?? FormReceivedDocumentReplyPolicy.Unreadable, now);
        if (rejected.IsFailure) return rejected.Error;
        documents.Add(rejected.Value);
        var recorded = Record(documentId, FormReceivedDocumentEventKind.Received, null, JsonNamingPolicy.CamelCase.ConvertName(classification.Reply.Kind.ToString()));
        return recorded.IsFailure ? recorded.Error : rejected.Value;
    }

    /// <summary>A step of the history; its detail is a short text (a code, a form's name or a response's number), protected.</summary>
    public Result Record(Guid documentId, FormReceivedDocumentEventKind kind, Guid? actorUserId, string? detail = null)
    {
        var protectedDetail = detail is null ? null : protector.Protect(documentId, DetailKind, JsonSerializer.Serialize(detail));
        var recorded = FormReceivedDocumentEvent.Record(documentId, kind, actorUserId, clock.GetUtcNow().UtcDateTime, protectedDetail);
        if (recorded.IsFailure) return recorded.Error;
        documents.Add(recorded.Value);
        return Result.Success();
    }

    /// <summary>The missing requests' lock, which «Otro» takes before the document's: the document's lock is always the last one.</summary>
    public Task LockMissingRequestsAsync(CancellationToken ct) => missing.LockAsync(ct);

    /// <summary>
    /// «Otro», under the missing requests' lock its caller took: the missing form as a request of the person with the document's id as its
    /// receipt and its type as the topic (a generic name without one); the same document notes it once, so a second «Otro» after it was
    /// reopened finds the request already there.
    /// </summary>
    public async Task<Result> NoteMissingFormAsync(FormReceivedDocument document, string culture, DateTime chosenAtUtc, CancellationToken ct)
    {
        if (await missing.GetRequestAsync(document.Id, ct) is { } prior)
            return prior.SubjectUserId == document.SenderUserId ? Result.Success() : FormReceivedDocumentErrors.StateConflict;
        var name = document.DocumentType ?? FormReceivedDocumentTexts.UnknownTopic(culture);
        var noted = await FormMissingRequestRecorder.RecordAsync(missing, document.Id, null, name, FormReceivedDocumentTexts.TopicDescription(culture, name),
            culture, document.SenderUserId, chosenAtUtc, ct);
        return noted.IsFailure ? noted.Error : Result.Success();
    }

    // The person's file name as a row keeps it: without control characters, within its length, never empty.
    private static string FileName(string name)
    {
        var clean = new string(name.Where(c => !char.IsControl(c)).Select(c => c is '/' or '\\' ? '_' : c).ToArray()).Trim();
        if (clean.Length > FormReceivedDocument.MaxFileNameLength) clean = clean[..FormReceivedDocument.MaxFileNameLength];
        if (clean.Length > 0 && char.IsHighSurrogate(clean[^1])) clean = clean[..^1];
        return clean.Length == 0 || clean is "." or ".." ? "document" : clean;
    }

}
