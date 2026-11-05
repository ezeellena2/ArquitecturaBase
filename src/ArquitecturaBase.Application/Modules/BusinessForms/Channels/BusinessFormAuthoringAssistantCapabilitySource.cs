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
    public const string DeclineId = "forms.authoring.decline";
    public const int Version = 1;
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } =
    [
        new(FormsId, Version, AssistantCapabilityKind.Read, "Read the names, prefixes, states and questions (label and type) of the forms that already exist, so a proposal does not repeat a name or a prefix. No answers, values or people are available.",
            FormAuthoringAssistantCodec.EmptySchema, FormAuthoringAssistantCodec.FormsSchema, new([BusinessFormsPermissions.Read], []), new(1024, FormAuthoringAssistantCodec.ReadBudget, FormAuthoringAssistantCodec.MaximumRows, 5)) { OfferedInHome = true },
        new(CatalogsId, Version, AssistantCapabilityKind.Read, "Read the IDs, names, kinds, value types and fields of the catalogs that already exist, to reuse one in a proposed form or to avoid repeating a name. No options, values or people are available.",
            FormAuthoringAssistantCodec.EmptySchema, FormAuthoringAssistantCodec.CatalogsSchema, new([BusinessFormsPermissions.CatalogsRead], []), new(1024, FormAuthoringAssistantCodec.ReadBudget, FormAuthoringAssistantCodec.MaximumRows, 5)) { OfferedInHome = true },
        new(ProposeFormId, Version, AssistantCapabilityKind.Proposal, "Propose one complete new form for a person to review, in the conversation language: name, prefix, final button text and every question with its type, whether it is required and its own options or an existing catalog. Read the existing forms and catalogs first; never repeat a name or a prefix, and use an existing catalog instead of a long list. A rejected proposal lists the paths to fix: propose it again corrected. A person creates the draft; you never create, publish or change anything.",
            FormAuthoringAssistantCodec.FormArgumentsSchema, FormAuthoringAssistantCodec.FormProposalSchema, new([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage], []), new(FormAuthoringAssistantCodec.ResponseBudget, FormAuthoringAssistantCodec.ResponseBudget, 1, 5)) { OfferedInHome = true },
        new(ProposeCatalogId, Version, AssistantCapabilityKind.Proposal, "Propose one new manual catalog without options for a person to review, in the conversation language: name, description, value type, what the code and the visible text of each option are, and its extra fields with their type. Read the existing catalogs first and never repeat a name. A rejected proposal lists the paths to fix: propose it again corrected. A person creates it inactive; you never create, activate or import anything.",
            FormAuthoringAssistantCodec.CatalogArgumentsSchema, FormAuthoringAssistantCodec.CatalogProposalSchema, new([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage], []), new(8192, FormAuthoringAssistantCodec.ResponseBudget, 1, 5)) { OfferedInHome = true },
        // A channel says only fixed texts: this tells it why nothing can be built. Home answers in its own words instead.
        new(DeclineId, Version, AssistantCapabilityKind.Proposal, "Say that a form or catalog cannot be built here, with one reason: noPermission when the person asks to build one and the tool to propose it is not offered; formExists with the prefix of an existing form, or catalogExists with the id of an existing catalog, when the person asks to build or change one that already exists, because existing ones are changed in the app. Never use it for anything else. A rejected one lists the paths to fix.",
            FormAuthoringAssistantCodec.DeclineArgumentsSchema, FormAuthoringAssistantCodec.DeclineSchema, new([], []), new(1024, 2048, 1, 5)),
    ];

    public async Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken ct)
    {
        if (request.Capability.Version != Version || Definitions.SingleOrDefault(definition => definition.Id == request.Capability.Id) is not { } definition ||
            request.Context.Culture is not ("es" or "en")) return FormDefinitionErrors.ContentInvalid;
        if (await users.FindByIdAsync(request.Context.ActorUserId, ct) is not { IsActive: true }) return FormDefinitionErrors.Forbidden;
        foreach (var permission in definition.RequiredPermissions.Actor)
            if (!await permissions.HasPermissionAsync(request.Context.ActorUserId, permission, ct)) return FormDefinitionErrors.Forbidden;
        if (request.Capability.Id == ProposeFormId) return await ProposeFormAsync(request, ct);
        if (request.Capability.Id == DeclineId) return await DeclineAsync(request, ct);
        if (request.Capability.Id == ProposeCatalogId)
        {
            var parsed = FormAuthoringAssistantCodec.ParseCatalogArguments(request.ArgumentsJson, request.Context.Culture);
            if (parsed.IsFailure) return parsed.Error;
            var catalog = FormAuthoringProposalPolicy.Normalize(parsed.Value);
            var issues = FormAuthoringProposalPolicy.Issues(catalog);
            return issues.Count != 0 ? Rejected(issues) : new AssistantCapabilityResponse("proposed", catalog, [], 1);
        }
        if (!FormAuthoringAssistantCodec.EmptyArguments(request.ArgumentsJson)) return FormDefinitionErrors.ContentInvalid;
        if (request.Capability.Id == FormsId)
        {
            var rows = await reader.ListDefinitionsAsync(FormAuthoringAssistantCodec.MaximumRows + 1, ct);
            var forms = Bound(rows.Take(FormAuthoringAssistantCodec.MaximumRows).Select(row => Form(row, request.Context.Culture)).ToArray(),
                rows.Count <= FormAuthoringAssistantCodec.MaximumRows,
                (selected, complete) => new FormAuthoringFormsPayload(complete, selected));
            return new AssistantCapabilityResponse(forms.IsComplete ? "complete" : "partial", forms, [], forms.Forms.Count);
        }
        var stored = await reader.ListCatalogsAsync(FormAuthoringAssistantCodec.MaximumRows + 1, ct);
        var catalogs = Bound(stored.Take(FormAuthoringAssistantCodec.MaximumRows).Select(row => Catalog(row, request.Context.Culture)).ToArray(),
            stored.Count <= FormAuthoringAssistantCodec.MaximumRows,
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
        if (capability.Id is not (ProposeFormId or ProposeCatalogId or DeclineId)) return FormDefinitionErrors.ContentInvalid;
        if (capability.Id == DeclineId && FormAuthoringAssistantCodec.ParseDecline(outputJson) is { IsSuccess: true } decline) return decline.Value;
        if (capability.Id == ProposeFormId && FormAuthoringAssistantCodec.ParseFormProposal(outputJson) is { IsSuccess: true } form) return form.Value;
        if (capability.Id == ProposeCatalogId && FormAuthoringAssistantCodec.ParseCatalogProposal(outputJson) is { IsSuccess: true } catalog) return catalog.Value;
        var rejection = FormAuthoringAssistantCodec.ParseRejection(outputJson);
        return rejection.IsFailure ? rejection.Error : rejection.Value;
    }

    // The proposal obeys the authoring rules and only uses active catalogs the person can read; it is never trimmed to fit.
    private async Task<Result<AssistantCapabilityResponse>> ProposeFormAsync(AssistantCapabilityRequest request, CancellationToken ct)
    {
        var arguments = FormAuthoringAssistantCodec.ParseFormArguments(request.ArgumentsJson);
        if (arguments.IsFailure) return arguments.Error;
        var questions = arguments.Value.Questions;
        var ids = questions.Where(question => question.Type == "catalog" && question.CatalogId is not null).Select(question => question.CatalogId!.Value).Distinct().ToArray();
        var readable = ids.Length == 0 || await permissions.HasPermissionAsync(request.Context.ActorUserId, BusinessFormsPermissions.CatalogsRead, ct);
        var catalogs = readable ? (await reader.ListCatalogsAsync(ids, ct)).Where(catalog => catalog.IsActive).ToDictionary(catalog => catalog.Id) : [];
        var issues = new List<FormAuthoringIssue>();
        for (var index = 0; index < questions.Count; index++)
            if (questions[index] is { Type: "catalog", CatalogId: { } catalogId } && !catalogs.ContainsKey(catalogId))
                issues.Add(new($"questions[{index}].catalogId", readable ? "catalogUnavailable" : "catalogNotReadable"));
        var proposal = FormAuthoringProposalPolicy.Normalize(new FormAuthoringFormProposal(request.Context.Culture, arguments.Value.Name,
            arguments.Value.Prefix, arguments.Value.SubmitLabel, questions.Select(question => new FormAuthoringProposedQuestion(
                question.Label, question.Type, question.Required, question.Options, question.Type == "catalog" && question.CatalogId is { } id
                    ? new(id, catalogs.TryGetValue(id, out var catalog) ? Catalog(catalog, request.Context.Culture).Name : "") : null)).ToArray()));
        issues.AddRange(FormAuthoringProposalPolicy.ShapeIssues(proposal).Where(issue => !issues.Contains(issue)));
        if (issues.Count == 0)
            issues.AddRange(DraftValidator.Validate(FormAuthoringProposalPolicy.Draft(request.TurnId, proposal)).Errors
                .Select(failure => new FormAuthoringIssue(FormAuthoringProposalPolicy.IssuePath(failure.PropertyName), "invalid")).Distinct());
        if (issues.Count == 0 && FormAssistantCapabilityCodec.ResponseBytes(proposal, "proposed", true) > FormAuthoringAssistantCodec.ResponseBudget)
            issues.Add(new("questions", "tooLarge"));
        return issues.Count != 0 ? Rejected(issues) : new AssistantCapabilityResponse("proposed", proposal, [], 1);
    }

    // Each reason is checked here: the person really cannot build forms or catalogs, or the one named exists and the person can
    // read it. A wrong one comes back rejected, so the model proposes instead or names another.
    private async Task<Result<AssistantCapabilityResponse>> DeclineAsync(AssistantCapabilityRequest request, CancellationToken ct)
    {
        var parsed = FormAuthoringAssistantCodec.ParseDeclineArguments(request.ArgumentsJson);
        if (parsed.IsFailure) return parsed.Error;
        var (reason, prefix, catalogId) = parsed.Value;
        var actor = request.Context.ActorUserId;
        if (reason == "noPermission")
            return prefix is not null || catalogId is not null ? Rejected([new(prefix is not null ? "prefix" : "catalogId", "invalid")])
                : await CanBuildAsync(actor, ct) ? Rejected([new("reason", "permissionGranted")]) : Declined(new(reason, null, null, ""));
        if (reason == "formExists")
        {
            if (catalogId is not null) return Rejected([new("catalogId", "invalid")]);
            if (!await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Read, ct)) return Rejected([new("reason", "notReadable")]);
            return prefix is { Length: > 0 } && await reader.FindDefinitionByPrefixAsync(prefix.Trim().ToUpperInvariant(), ct) is { } form
                ? Declined(new(reason, form.Id, null, Text(FormDraftPolicy.Read<Dictionary<string, string>>(form.NameJson), request.Context.Culture, form.Culture)))
                : Rejected([new("prefix", "notFound")]);
        }
        if (prefix is not null) return Rejected([new("prefix", "invalid")]);
        if (!await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.CatalogsRead, ct)) return Rejected([new("reason", "notReadable")]);
        return Guid.TryParse(catalogId, out var id) && (await reader.ListCatalogsAsync([id], ct)).SingleOrDefault() is { } catalog
            ? Declined(new(reason, null, catalog.Id, Catalog(catalog, request.Context.Culture).Name)) : Rejected([new("catalogId", "notFound")]);
    }

    // Building needs both areas' read and manage permissions; missing any of them is a real reason to decline.
    private async Task<bool> CanBuildAsync(Guid actor, CancellationToken ct)
    {
        foreach (var permission in new[] { BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage })
            if (!await permissions.HasPermissionAsync(actor, permission, ct)) return false;
        return true;
    }

    private static AssistantCapabilityResponse Declined(FormAuthoringDecline decline) => new("proposed", decline, [], 1);

    private static AssistantCapabilityResponse Rejected(IReadOnlyList<FormAuthoringIssue> issues) =>
        new("rejected", new FormAuthoringRejection(issues.Take(50).ToArray()), [], 0);

    // Whatever does not fit the budget is left out and the read says it is partial: it never proves absence.
    private static TPayload Bound<TRow, TPayload>(IReadOnlyList<TRow> rows, bool complete, Func<IReadOnlyList<TRow>, bool, TPayload> create)
        where TPayload : AssistantCapabilityPayload
    {
        var selected = new List<TRow>();
        foreach (var row in rows)
        {
            if (FormAssistantCapabilityCodec.ResponseBytes(create([.. selected, row], false), "partial", false) > FormAuthoringAssistantCodec.ReadBudget)
            {
                complete = false;
                continue;
            }
            selected.Add(row);
        }
        return create(selected, complete);
    }

    // Each text reads in the conversation language, or in its original without a translation: the same name creating compares.
    private static FormAuthoringFormRow Form(FormAuthoringDefinitionStoredRow row, string culture)
    {
        var schema = FormDraftPolicy.Read<FormQuestionSchema>(row.SchemaJson);
        return new(Text(FormDraftPolicy.Read<Dictionary<string, string>>(row.NameJson), culture, row.Culture), row.Prefix,
            row.IsInactive ? "inactive" : row.IsPublished ? "published" : "draft",
            // The block of rows is not one of the forty questions: its questions are listed like the others.
            schema.Fields.Where(field => field.Type != "group").Select(field => new FormAuthoringQuestionRow(Text(field.Label, culture, row.Culture), field.Type)).ToArray());
    }

    private static FormAuthoringCatalogRow Catalog(FormAuthoringCatalogStoredRow row, string culture) =>
        new(row.Id, Text(FormDraftPolicy.Read<Dictionary<string, string>>(row.NameJson), culture, row.OriginalCulture), row.Kind.ToString().ToLowerInvariant(),
            row.ValueType.ToString().ToLowerInvariant(), row.IsActive,
            FormCatalogFieldPolicy.ReadFields(row.FieldsJson).Where(field => field.IsActive).OrderBy(field => field.Position)
                .Select(field => new FormAuthoringCatalogFieldRow(Text(field.Name, culture, row.OriginalCulture), field.Type)).ToArray());

    private static string Text(IReadOnlyDictionary<string, string> texts, string culture, string original) =>
        texts.TryGetValue(culture, out var text) && !string.IsNullOrWhiteSpace(text) ? text
            : texts.TryGetValue(original, out var source) && !string.IsNullOrWhiteSpace(source) ? source
            : texts.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
}
