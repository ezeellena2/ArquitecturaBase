using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

/// <summary>
/// forms.library.standards@1 (DOC6): the library standards that read documents, so propose-form@2 names a similar one by the key this
/// read showed in the same turn. Home never offers it; the documents channel does, only with a document. It lists what the installed
/// modules bring as data, in the conversation language (a standard of a next stage has no draft and is not listed); the documents
/// channel only exists with private storage, which the library asks to offer a standard with a document question.
/// </summary>
internal sealed class BusinessFormLibraryAssistantCapabilitySource(IEnumerable<IStructuredFormStandardSource> sources, IUserReader users,
    IPermissionService permissions) : IAssistantCapabilitySource
{
    public const string StandardsId = "forms.library.standards";
    public const int Version = 1;

    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } =
    [
        new(StandardsId, Version, AssistantCapabilityKind.Read, "Read the standards of the forms library that read documents: the key, name and description of each one and the data it reads. Use it to tell whether the conversation's documents look like one of them; a proposal names a similar standard only by the key this read shows. No forms, answers or people are available.",
            FormAuthoringAssistantCodec.EmptySchema, FormAuthoringAssistantCodec.StandardsSchema, new([BusinessFormsPermissions.Read], []),
            new(1024, FormAuthoringAssistantCodec.ReadBudget, FormAuthoringAssistantCodec.MaximumRows, 5)) { Attachment = AssistantCapabilityAttachment.Document },
    ];

    public async Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken)
    {
        if (request.Capability != new AssistantCapabilityReference(StandardsId, Version) || request.Context.Culture is not ("es" or "en"))
            return FormDefinitionErrors.ContentInvalid;
        if (await users.FindByIdAsync(request.Context.ActorUserId, cancellationToken) is not { IsActive: true }
            || !await permissions.HasPermissionAsync(request.Context.ActorUserId, BusinessFormsPermissions.Read, cancellationToken))
            return FormDefinitionErrors.Forbidden;
        if (!FormAuthoringAssistantCodec.EmptyArguments(request.ArgumentsJson)) return FormDefinitionErrors.ContentInvalid;
        var culture = request.Context.Culture;
        var rows = sources.SelectMany(source => source.Standards).Where(standard => standard.Reading is not null)
            .Select(standard => new FormLibraryStandardRow(standard.Key, FormRecordPresentationPolicy.Text(standard.Name, culture),
                FormRecordPresentationPolicy.Text(standard.Description, culture), FormStandardPolicy.Reads(standard, culture)?.Select(read => read.Name).ToArray() ?? []))
            .ToArray();
        // Whatever does not fit the budget is left out and the read says it is partial: it never proves absence.
        var complete = rows.Length <= FormAuthoringAssistantCodec.MaximumRows;
        var selected = new List<FormLibraryStandardRow>();
        foreach (var row in rows.Take(FormAuthoringAssistantCodec.MaximumRows))
        {
            if (FormAssistantCapabilityCodec.ResponseBytes(new FormLibraryStandardsPayload(false, [.. selected, row]), "partial", false) > FormAuthoringAssistantCodec.ReadBudget)
            {
                complete = false;
                continue;
            }
            selected.Add(row);
        }
        return new AssistantCapabilityResponse(complete ? "complete" : "partial", new FormLibraryStandardsPayload(complete, selected), [], selected.Count);
    }

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson)
    {
        if (capability != new AssistantCapabilityReference(StandardsId, Version)) return FormDefinitionErrors.ContentInvalid;
        var standards = FormAuthoringAssistantCodec.ParseStandards(outputJson);
        return standards.IsFailure ? standards.Error : standards.Value;
    }
}
