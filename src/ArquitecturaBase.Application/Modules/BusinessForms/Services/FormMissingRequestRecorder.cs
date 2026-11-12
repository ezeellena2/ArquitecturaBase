using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The part of «Formularios que faltan» that runs inside its caller's unit of work, under the lock of the missing requests that the
/// caller takes: the request of one source receipt, once, in a topic found by its id or its normalized name, or a new one. The assistant's
/// proposal and the inbox's «Otro» (task 7, by the type of document) record the same way.
/// </summary>
internal static class FormMissingRequestRecorder
{
    public static async Task<Result<StructuredMissingFormRequestCaptureResponse>> RecordAsync(IFormMissingRequestRepository repository, Guid sourceReceiptId,
        Guid? existingTopicId, string? name, string? description, string culture, Guid subjectUserId, DateTime requestedAtUtc, CancellationToken ct)
    {
        if (await repository.GetRequestAsync(sourceReceiptId, ct) is { } prior)
            return prior.SubjectUserId == subjectUserId && prior.RequestedAtUtc == requestedAtUtc
                ? new StructuredMissingFormRequestCaptureResponse(prior.Id, prior.TopicId, true) : FormMissingRequestErrors.ContentInvalid;
        if (existingTopicId is null && string.IsNullOrWhiteSpace(name)) return FormMissingRequestErrors.ContentInvalid;
        var topic = existingTopicId is { } id ? await repository.GetAsync(id, ct)
            : await repository.GetByNameAsync(BusinessFormMissingTopic.NormalizeName(name!), ct);
        if (existingTopicId.HasValue && topic is null) return FormMissingRequestErrors.ContentInvalid;
        var isNew = topic is null;
        if (topic is null)
        {
            var created = BusinessFormMissingTopic.Create(name!, description ?? "", culture);
            if (created.IsFailure) return created.Error;
            topic = created.Value;
        }
        else
        {
            var registered = topic.RegisterRequest();
            if (registered.IsFailure) return registered.Error;
        }
        var captured = BusinessFormMissingRequest.Create(sourceReceiptId, topic.Id, subjectUserId, requestedAtUtc);
        if (captured.IsFailure) return captured.Error;
        if (isNew) repository.Add(topic);
        repository.Add(captured.Value);
        return new StructuredMissingFormRequestCaptureResponse(captured.Value.Id, topic.Id, false);
    }
}
