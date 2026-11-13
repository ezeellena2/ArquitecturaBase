using System.Text.Json;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// «Crear borrador» of DOC6 (decision 17): the draft of a request's proposal as the reviewer edited it (name, prefix, button, «Cómo
/// reconocerlo» and questions), with «Revisar documentos» and «Diseñar formularios». The same request again gets the draft it created; the
/// proposal must be the one its operation closed on. The edited form obeys the authoring rules (each question's kind, then
/// <see cref="SaveFormDraftRequestValidator"/>, answered by the request's own paths). The owner of creating from a proposal
/// (<see cref="IFormDraftProposalService"/>) then checks the catalogs and the name and creates it by the editor's own save with the reading
/// on, the proposal's provenance as its evidence (the form as it was proposed, never its samples or its standard) and, in that same save,
/// the draft its request created.
/// </summary>
internal sealed class FormDocumentDraftService(FormDocumentAuthoringPolicy policy, IFormDocumentProposalReader reader, IAssistantOperations operations,
    IFormDraftProposalService proposals, FormDocumentProposalRecorder recorder) : IFormDocumentDraftService
{
    private static readonly SaveFormDraftRequestValidator DraftValidator = new();

    public async Task<Result<FormDraftAssistantProposalResponse>> CreateAsync(CreateFormDocumentDraftRequest request, CancellationToken cancellationToken)
    {
        var access = await policy.CheckAsync(cancellationToken);
        if (access.IsFailure) return access.Error;
        var actor = access.Value.Id;
        if (request.RequestId is not { } requestId || requestId == Guid.Empty) return FieldErrors.On(FormDefinitionErrors.ContentInvalid, "requestId");
        if (request.ProposalId is not { } proposalId || proposalId == Guid.Empty) return FieldErrors.On(FormDefinitionErrors.ContentInvalid, "proposalId");
        if (await reader.FindByRequestAsync(actor, requestId, cancellationToken) is not { } row) return FormReceivedDocumentErrors.ProposalNotFound;
        // The same request again gets the draft it created and the proposal it came from, whatever the body names, before reading anything.
        if (row is { DefinitionId: { } created, ProposalId: { } source }) return new FormDraftAssistantProposalResponse(source, created);
        var found = await operations.GetAsync(FormDocumentAuthoringPolicy.Context(row), requestId, cancellationToken);
        if (found.IsFailure && found.Error.Type == ErrorType.Forbidden) return FormDefinitionErrors.Forbidden;
        if (found.IsFailure || found.Value is not { State: "completed", Provenance: { } provenance } operation
            || FormDocumentAuthoringPolicy.Proposal(operation) is not { Payload: FormAuthoringDocumentFormProposal original } observed || observed.Id != proposalId)
            return FormReceivedDocumentErrors.ProposalNotFound;
        var edited = Edited(request, original.Culture);
        if (edited.IsFailure) return edited.Error;
        var (form, reading) = edited.Value;
        if (DraftValidator.Validate(FormAuthoringProposalPolicy.Draft(observed.Id, form, reading)).Errors is [var failure, ..])
            return FieldErrors.Validation(Path(failure.PropertyName), failure.ErrorMessage);
        return await proposals.CreateFromDocumentsAsync(new(actor, observed, provenance, form, reading, recorder.Draft(row.Id, observed.Id)), cancellationToken);
    }

    // The edited form in the proposal's language and terms: every question with its text and type, the reading on with «Cómo reconocerlo».
    private static Result<(FormAuthoringFormProposal Form, FormAuthoringProposedReading Reading)> Edited(CreateFormDocumentDraftRequest request, string culture)
    {
        if (request.Questions is not { } questions) return FieldErrors.On(FormDefinitionErrors.ContentInvalid, "questions");
        var proposed = new List<FormAuthoringProposedQuestion>();
        for (var index = 0; index < questions.Count; index++)
        {
            if (questions[index] is not { Label: { } label, Type: { } type } question || question.Options?.Any(option => option is null) == true)
                return FieldErrors.On(FormDefinitionErrors.ContentInvalid, $"questions[{index}]");
            proposed.Add(new(label, type, question.Required ?? false, question.Options?.Select(option => option!).ToArray() ?? [],
                question.CatalogId is { } catalogId ? new(catalogId, "") : null));
        }
        var form = FormAuthoringProposalPolicy.Normalize(new FormAuthoringFormProposal(culture, request.Name ?? "", request.Prefix ?? "",
            request.SubmitLabel ?? "", proposed));
        if (FormAuthoringProposalPolicy.ShapeIssues(form) is [var issue, ..]) return FieldErrors.On(FormDefinitionErrors.ContentInvalid, issue.Path);
        if (string.IsNullOrWhiteSpace(request.Recognition)) return FieldErrors.On(FormDefinitionErrors.ContentInvalid, "recognition");
        return (form, new FormAuthoringProposedReading(request.Recognition.Trim()));
    }

    // The draft's path as the request names it: the name, the prefix, the button, «Cómo reconocerlo» or the question.
    private static string Path(string propertyName)
    {
        var path = FormAuthoringProposalPolicy.IssuePath(string.Join('.', propertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName)));
        return path.StartsWith("documentReading", StringComparison.Ordinal) ? "recognition" : path;
    }
}
