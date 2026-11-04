using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

internal sealed class BusinessFormAuthoringAssistantCapabilitySource(IFormAuthoringReader reader, IUserReader users,
    IPermissionService permissions) : IAssistantCapabilitySource
{
    private static readonly SaveFormDraftRequestValidator DraftValidator = new();
    public const string FormsId = "forms.authoring.forms";
    public const string CatalogsId = "forms.authoring.catalogs";
    public const string ProposeFormId = "forms.authoring.propose-form";
    public const string ProposeCatalogId = "forms.authoring.propose-catalog";
    public const int Version = 1;
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } =
    [
        new(FormsId, Version, AssistantCapabilityKind.Read, "Read the names, prefixes, states and questions (label and type) of the forms that already exist, so a proposal does not repeat a name or a prefix. No answers, values or people are available.",
            FormAuthoringAssistantCodec.EmptySchema, FormAuthoringAssistantCodec.FormsSchema, new([BusinessFormsPermissions.Read], []), new(1024, FormAuthoringAssistantCodec.ResponseBudget, FormAuthoringAssistantCodec.MaximumRows, 5)),
        new(CatalogsId, Version, AssistantCapabilityKind.Read, "Read the IDs, names, kinds, value types and fields of the catalogs that already exist, to reuse one in a proposed form or to avoid repeating a name. No options, values or people are available.",
            FormAuthoringAssistantCodec.EmptySchema, FormAuthoringAssistantCodec.CatalogsSchema, new([BusinessFormsPermissions.CatalogsRead], []), new(1024, FormAuthoringAssistantCodec.ResponseBudget, FormAuthoringAssistantCodec.MaximumRows, 5)),
        new(ProposeFormId, Version, AssistantCapabilityKind.Proposal, "Propose one complete new form for a person to review, in the conversation language: name, prefix, final button text and every question with its type, whether it is required and its own options or an existing catalog. Read the existing forms and catalogs first; never repeat a name or a prefix, and use an existing catalog instead of a long list. A person creates the draft; you never create, publish or change anything.",
            FormAuthoringAssistantCodec.FormArgumentsSchema, FormAuthoringAssistantCodec.FormProposalSchema, new([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage], []), new(FormAuthoringAssistantCodec.ResponseBudget, FormAuthoringAssistantCodec.ResponseBudget, 1, 5)),
        new(ProposeCatalogId, Version, AssistantCapabilityKind.Proposal, "Propose one new manual catalog without options for a person to review, in the conversation language: name, description, value type, what the code and the visible text of each option are, and its extra fields with their type. Read the existing catalogs first and never repeat a name. A person creates it inactive; you never create, activate or import anything.",
            FormAuthoringAssistantCodec.CatalogArgumentsSchema, FormAuthoringAssistantCodec.CatalogProposalSchema, new([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage], []), new(8192, FormAuthoringAssistantCodec.ResponseBudget, 1, 5)),
    ];

    public async Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken ct)
    {
        if (request.Capability.Version != Version || Definitions.SingleOrDefault(definition => definition.Id == request.Capability.Id) is not { } definition ||
            request.Context.Culture is not ("es" or "en")) return FormDefinitionErrors.ContentInvalid;
        if (await users.FindByIdAsync(request.Context.ActorUserId, ct) is not { IsActive: true }) return FormDefinitionErrors.Forbidden;
        foreach (var permission in definition.RequiredPermissions.Actor)
            if (!await permissions.HasPermissionAsync(request.Context.ActorUserId, permission, ct)) return FormDefinitionErrors.Forbidden;
        if (request.Capability.Id == ProposeFormId) return await ProposeFormAsync(request, ct);
        if (request.Capability.Id == ProposeCatalogId)
        {
            var parsed = FormAuthoringAssistantCodec.ParseCatalogArguments(request.ArgumentsJson, request.Context.Culture);
            var catalog = parsed.IsFailure ? parsed : FormAuthoringProposalPolicy.Validate(parsed.Value);
            return catalog.IsFailure ? catalog.Error : new AssistantCapabilityResponse("proposed", catalog.Value, [], 1);
        }
        if (!FormAuthoringAssistantCodec.EmptyArguments(request.ArgumentsJson)) return FormDefinitionErrors.ContentInvalid;
        if (request.Capability.Id == FormsId)
        {
            var rows = await reader.ListDefinitionsAsync(FormAuthoringAssistantCodec.MaximumRows + 1, ct);
            var forms = Bound(rows.Take(FormAuthoringAssistantCodec.MaximumRows).Select(Form).ToArray(), rows.Count <= FormAuthoringAssistantCodec.MaximumRows,
                (selected, complete) => new FormAuthoringFormsPayload(complete, selected));
            return new AssistantCapabilityResponse(forms.IsComplete ? "complete" : "partial", forms, [], forms.Forms.Count);
        }
        var stored = await reader.ListCatalogsAsync(FormAuthoringAssistantCodec.MaximumRows + 1, ct);
        var catalogs = Bound(stored.Take(FormAuthoringAssistantCodec.MaximumRows).Select(Catalog).ToArray(), stored.Count <= FormAuthoringAssistantCodec.MaximumRows,
            (selected, complete) => new FormAuthoringCatalogsPayload(complete, selected));
        return new AssistantCapabilityResponse(catalogs.IsComplete ? "complete" : "partial", catalogs, [], catalogs.Catalogs.Count);
    }

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson)
    {
        if (capability.Version != Version) return FormDefinitionErrors.ContentInvalid;
        if (capability.Id == FormsId)
        {
            var forms = FormAuthoringAssistantCodec.ParseForms(outputJson);
            return forms.IsFailure ? forms.Error : forms.Value;
        }
        if (capability.Id == CatalogsId)
        {
            var catalogs = FormAuthoringAssistantCodec.ParseCatalogs(outputJson);
            return catalogs.IsFailure ? catalogs.Error : catalogs.Value;
        }
        if (capability.Id == ProposeFormId)
        {
            var form = FormAuthoringAssistantCodec.ParseFormProposal(outputJson);
            return form.IsFailure ? form.Error : form.Value;
        }
        if (capability.Id == ProposeCatalogId)
        {
            var catalog = FormAuthoringAssistantCodec.ParseCatalogProposal(outputJson);
            return catalog.IsFailure ? catalog.Error : catalog.Value;
        }
        return FormDefinitionErrors.ContentInvalid;
    }

    // The proposal obeys the authoring rules and only uses active catalogs the person can read; it is never trimmed to fit.
    private async Task<Result<AssistantCapabilityResponse>> ProposeFormAsync(AssistantCapabilityRequest request, CancellationToken ct)
    {
        var arguments = FormAuthoringAssistantCodec.ParseFormArguments(request.ArgumentsJson);
        if (arguments.IsFailure) return arguments.Error;
        var ids = arguments.Value.Questions.Where(question => question.Type == "catalog" && question.CatalogId is not null)
            .Select(question => question.CatalogId!.Value).Distinct().ToArray();
        if (ids.Length != 0 && !await permissions.HasPermissionAsync(request.Context.ActorUserId, BusinessFormsPermissions.CatalogsRead, ct))
            return FormDefinitionErrors.Forbidden;
        var catalogs = (await reader.ListCatalogsAsync(ids, ct)).Where(catalog => catalog.IsActive).ToDictionary(catalog => catalog.Id);
        if (ids.Any(id => !catalogs.ContainsKey(id))) return FormDefinitionErrors.ContentInvalid;
        var proposal = FormAuthoringProposalPolicy.Normalize(new FormAuthoringFormProposal(request.Context.Culture, arguments.Value.Name,
            arguments.Value.Prefix, arguments.Value.SubmitLabel, arguments.Value.Questions.Select(question => new FormAuthoringProposedQuestion(
                question.Label, question.Type, question.Required, question.Options,
                question.Type == "catalog" && question.CatalogId is { } id ? new(id, Catalog(catalogs[id]).Name) : null)).ToArray()));
        if (!FormAuthoringProposalPolicy.HasValidShape(proposal)) return FormDefinitionErrors.ContentInvalid;
        if (DraftValidator.Validate(FormAuthoringProposalPolicy.Draft(request.TurnId, proposal)).Errors is [var failure, ..])
            return FieldErrors.On(FormDefinitionErrors.ContentInvalid, failure.PropertyName);
        return FormAssistantCapabilityCodec.ResponseBytes(proposal, "proposed", true) > FormAuthoringAssistantCodec.ResponseBudget
            ? FormDefinitionErrors.ContentInvalid : new AssistantCapabilityResponse("proposed", proposal, [], 1);
    }

    // Whatever does not fit the budget is left out and the read says it is partial: it never proves absence.
    private static TPayload Bound<TRow, TPayload>(IReadOnlyList<TRow> rows, bool complete, Func<IReadOnlyList<TRow>, bool, TPayload> create)
        where TPayload : AssistantCapabilityPayload
    {
        var selected = new List<TRow>();
        foreach (var row in rows)
        {
            if (FormAssistantCapabilityCodec.ResponseBytes(create([.. selected, row], false), "partial", false) > FormAuthoringAssistantCodec.ResponseBudget)
            {
                complete = false;
                continue;
            }
            selected.Add(row);
        }
        return create(selected, complete);
    }

    private static FormAuthoringFormRow Form(FormAuthoringDefinitionStoredRow row)
    {
        var schema = FormDraftPolicy.Read<FormQuestionSchema>(row.SchemaJson);
        return new(Text(FormDraftPolicy.Read<Dictionary<string, string>>(row.NameJson), row.Culture), row.Prefix,
            row.IsInactive ? "inactive" : row.IsPublished ? "published" : "draft",
            schema.Fields.Select(field => new FormAuthoringQuestionRow(Text(field.Label, row.Culture), field.Type)).ToArray());
    }

    private static FormAuthoringCatalogRow Catalog(FormAuthoringCatalogStoredRow row) =>
        new(row.Id, Text(FormDraftPolicy.Read<Dictionary<string, string>>(row.NameJson), row.OriginalCulture), row.Kind.ToString().ToLowerInvariant(),
            row.ValueType.ToString().ToLowerInvariant(), row.IsActive,
            FormCatalogFieldPolicy.ReadFields(row.FieldsJson).Where(field => field.IsActive).OrderBy(field => field.Position)
                .Select(field => new FormAuthoringCatalogFieldRow(Text(field.Name, row.OriginalCulture), field.Type)).ToArray());

    private static string Text(IReadOnlyDictionary<string, string> texts, string culture) =>
        texts.TryGetValue(culture, out var text) && !string.IsNullOrWhiteSpace(text) ? text
            : texts.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
}
