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
    public const string ProposeImportId = "forms.authoring.propose-import";
    public const string DeclineId = "forms.authoring.decline";
    public const int Version = 1;
    // Stage 2: propose-catalog@2 maps the turn's spreadsheet. Home keeps offering version 1, the one today's front draws, until
    // the front draws the spreadsheet (task 9 flips OfferedInHome); version 1's turns, cards and summaries keep creating.
    public const int MappedCatalogVersion = 2;

    /// <summary>A proposed catalog and an import into one that exists replace each other in their conversation.</summary>
    public static IReadOnlyList<string> CatalogGroup { get; } = [ProposeCatalogId, ProposeImportId];

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
        new(ProposeCatalogId, MappedCatalogVersion, AssistantCapabilityKind.Proposal, "Propose one new manual catalog for a person to review, in the conversation language: name, description, value type, what the code and the visible text of each option are, and its extra fields with their type. With the conversation's spreadsheet, also map where its options come from: whether row 1 holds headers and the column letter of the code, of the visible text (the same column for a simple list), of the value (only when the value type is not text), of the detail (optional) and of each field (optional); without a spreadsheet the mapping is null. Read the existing catalogs first and never repeat a name. A rejected proposal lists the paths to fix: propose it again corrected. A person creates it inactive and reviews the import of the spreadsheet; you never create, activate or import anything.",
            FormAuthoringAssistantCodec.MappedCatalogArgumentsSchema, FormAuthoringAssistantCodec.MappedCatalogProposalSchema, new([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage], []), new(8192, FormAuthoringAssistantCodec.ResponseBudget, 1, 5)),
        new(ProposeImportId, Version, AssistantCapabilityKind.Proposal, "Propose importing the conversation's spreadsheet into an existing manual catalog, for a person to review: the catalog id as the catalogs read shows it, whether row 1 holds headers, the column letter of the code of each option, of its visible text (the same column for a simple list), of its value (only when the catalog's value type is not text) and of its detail (optional), and the column of each field to fill, named as the catalogs read names it. The options the spreadsheet does not bring are deactivated by the import. A rejected proposal lists the paths to fix, and a required field left out comes back named: propose it again corrected. A person reviews the import and imports it; you never import or change anything.",
            FormAuthoringAssistantCodec.ImportArgumentsSchema, FormAuthoringAssistantCodec.ImportProposalSchema, new([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage], []), new(8192, FormAuthoringAssistantCodec.ResponseBudget, 1, 5)) { RequiresAttachment = true },
        // A channel says only fixed texts: this tells it why nothing is built yet, or that it has to ask what to build first
        // («pregunta y espera»). Home answers in its own words instead.
        new(DeclineId, Version, AssistantCapabilityKind.Proposal, "Say why no form or catalog is proposed yet, with one reason: noPermission when the person asks to build one and the tool to propose it is not offered; formExists with the prefix of an existing form, or catalogExists with the id of an existing catalog, when the person asks to build or change one that already exists, because existing ones are changed in the app (an existing catalog never changes its fields, but with the conversation's spreadsheet you can propose importing it into that catalog instead); unclear when the person asks to build something but does not say enough to propose it (what it is for, what it asks or holds, or whether it is a form or a catalog): the person is asked, and the answer comes as their next message, so never guess a proposal. Never use it for anything else. A rejected one lists the paths to fix.",
            FormAuthoringAssistantCodec.DeclineArgumentsSchema, FormAuthoringAssistantCodec.DeclineSchema, new([], []), new(1024, 2048, 1, 5)),
    ];

    public async Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken ct)
    {
        if (Definitions.SingleOrDefault(definition => definition.Id == request.Capability.Id && definition.Version == request.Capability.Version) is not { } definition ||
            request.Context.Culture is not ("es" or "en")) return FormDefinitionErrors.ContentInvalid;
        if (await users.FindByIdAsync(request.Context.ActorUserId, ct) is not { IsActive: true }) return FormDefinitionErrors.Forbidden;
        foreach (var permission in definition.RequiredPermissions.Actor)
            if (!await permissions.HasPermissionAsync(request.Context.ActorUserId, permission, ct)) return FormDefinitionErrors.Forbidden;
        if (request.Capability.Id == ProposeFormId) return await ProposeFormAsync(request, ct);
        if (request.Capability.Id == DeclineId) return await DeclineAsync(request, ct);
        if (request.Capability.Id == ProposeImportId) return await ProposeImportAsync(request, ct);
        if (request.Capability is { Id: ProposeCatalogId, Version: MappedCatalogVersion }) return ProposeMappedCatalog(request);
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
        if (!Definitions.Any(definition => definition.Id == capability.Id && definition.Version == capability.Version)) return FormDefinitionErrors.ContentInvalid;
        // The spreadsheet proposals read back only their own payload or a rejection.
        if (capability.Version == MappedCatalogVersion || capability.Id == ProposeImportId)
        {
            AssistantCapabilityPayload? proposal = capability.Id == ProposeImportId
                ? FormAuthoringAssistantCodec.ParseImportProposal(outputJson) is { IsSuccess: true } import ? import.Value : null
                : FormAuthoringAssistantCodec.ParseMappedCatalogProposal(outputJson) is { IsSuccess: true } mapped ? mapped.Value : null;
            if (proposal is not null) return proposal;
            var rejected = FormAuthoringAssistantCodec.ParseRejection(outputJson);
            return rejected.IsFailure ? rejected.Error : rejected.Value;
        }
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

    // Version 1's catalog with, when it maps the turn's spreadsheet, its columns checked against the prepared sheet and the card's
    // summary. A spreadsheet without a mapping is a catalog without importing; a mapping without one is rejected.
    private static Result<AssistantCapabilityResponse> ProposeMappedCatalog(AssistantCapabilityRequest request)
    {
        var parsed = FormAuthoringAssistantCodec.ParseMappedCatalogArguments(request.ArgumentsJson, request.Context.Culture);
        if (parsed.IsFailure) return parsed.Error;
        var catalog = FormAuthoringProposalPolicy.Normalize(parsed.Value.Catalog);
        var issues = FormAuthoringProposalPolicy.Issues(catalog).ToList();
        FormAuthoringSpreadsheetSummary? summary = null;
        if (parsed.Value.Mapping is { } mapping)
        {
            if (request.Attachment is not { } sheet) issues.Add(new("mapping", "noSpreadsheet"));
            else
            {
                // One letter (or null) for each field, in their order.
                var aligned = mapping.Fields.Count == catalog.Fields.Count;
                if (!aligned) issues.Add(new("mapping.fields", "invalid"));
                var fields = mapping.Fields.Select((letter, index) => new FormAuthoringSpreadsheetPolicy.Column("field", $"mapping.fields[{index}]",
                    letter, aligned ? catalog.Fields[index].Name : null)).Where(_ => aligned);
                var mapped = FormAuthoringSpreadsheetPolicy.Map(sheet, "mapping.", mapping.Code, mapping.Option, mapping.Value, mapping.Detail, catalog.ValueType, fields);
                issues.AddRange(mapped.Issues);
                summary = FormAuthoringSpreadsheetPolicy.Summary(sheet, mapping.HasHeaders, mapped.Columns);
            }
        }
        var proposal = new FormAuthoringMappedCatalogProposal(catalog.Culture, catalog.Name, catalog.Description, catalog.ValueType, catalog.Code,
            catalog.Option, catalog.Fields, summary);
        if (issues.Count == 0 && summary is not null && FormAssistantCapabilityCodec.ResponseBytes(proposal, "proposed", true) > FormAuthoringAssistantCodec.ResponseBudget)
            issues.Add(new("mapping", "tooLarge"));
        return issues.Count != 0 ? Rejected(issues) : new AssistantCapabilityResponse("proposed", proposal, [], 1);
    }

    // The turn's spreadsheet into a Manual catalog that exists, read with the reader the capabilities already use: its fields are
    // named as the catalogs read names them (or in their original language, without case), and the required ones are mapped.
    private async Task<Result<AssistantCapabilityResponse>> ProposeImportAsync(AssistantCapabilityRequest request, CancellationToken ct)
    {
        var parsed = FormAuthoringAssistantCodec.ParseImportArguments(request.ArgumentsJson);
        if (parsed.IsFailure) return parsed.Error;
        // The engine offers it only in a turn with a spreadsheet.
        if (request.Attachment is not { } sheet) return FormCatalogErrors.ContentInvalid;
        var arguments = parsed.Value;
        var culture = request.Context.Culture;
        if (!Guid.TryParse(arguments.CatalogId, out var catalogId) || (await reader.ListCatalogsAsync([catalogId], ct)).SingleOrDefault() is not { } catalog)
            return Rejected([new("catalogId", "notFound")]);
        if (catalog.Kind != BusinessFormCatalogKind.Manual) return Rejected([new("catalogId", "notManual")]);
        var active = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson).Where(field => field.IsActive).OrderBy(field => field.Position).ToArray();
        var issues = new List<FormAuthoringIssue>();
        var chosen = new List<(BusinessFormCatalogField Field, int Index, string? Letter)>();
        for (var index = 0; index < arguments.Fields.Count; index++)
        {
            var name = arguments.Fields[index].Name.Trim();
            var matches = active.Where(field => string.Equals(Text(field.Name, culture, catalog.OriginalCulture), name, StringComparison.OrdinalIgnoreCase) ||
                field.Name.TryGetValue(catalog.OriginalCulture, out var original) && string.Equals(original, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) issues.Add(new($"fields[{index}].name", "notFound"));
            else if (chosen.Any(item => item.Field.Id == matches[0].Id)) issues.Add(new($"fields[{index}].name", "repeated"));
            else
            {
                chosen.Add((matches[0], index, arguments.Fields[index].Column));
                if (FormAuthoringSpreadsheetPolicy.Given(arguments.Fields[index].Column) is null) issues.Add(new($"fields[{index}].column", "required"));
            }
        }
        foreach (var missing in active.Where(field => field.Required && chosen.All(item => item.Field.Id != field.Id)))
            issues.Add(new("fields", "required") { Name = Text(missing.Name, culture, catalog.OriginalCulture) });
        var valueType = catalog.ValueType.ToString().ToLowerInvariant();
        var mapped = FormAuthoringSpreadsheetPolicy.Map(sheet, "", arguments.Code, arguments.Option, arguments.Value, arguments.Detail, valueType,
            chosen.OrderBy(item => item.Field.Position).Select(item => new FormAuthoringSpreadsheetPolicy.Column("field", $"fields[{item.Index}].column",
                item.Letter, Text(item.Field.Name, culture, catalog.OriginalCulture), item.Field.Key)));
        issues.AddRange(mapped.Issues);
        if (issues.Count != 0) return Rejected(issues);
        var proposal = new FormAuthoringImportProposal(new(catalog.Id, Catalog(catalog, culture).Name, valueType),
            FormAuthoringSpreadsheetPolicy.Summary(sheet, arguments.HasHeaders, mapped.Columns));
        return FormAssistantCapabilityCodec.ResponseBytes(proposal, "proposed", true) > FormAuthoringAssistantCodec.ResponseBudget
            ? Rejected([new("fields", "tooLarge")]) : new AssistantCapabilityResponse("proposed", proposal, [], 1);
    }

    // Each reason is checked here: the person really cannot build forms or catalogs, the one named exists and the person can
    // read it, or, to ask what to build, the person can build a form or a catalog. A wrong one comes back rejected, so the model
    // proposes instead or names another.
    private async Task<Result<AssistantCapabilityResponse>> DeclineAsync(AssistantCapabilityRequest request, CancellationToken ct)
    {
        var parsed = FormAuthoringAssistantCodec.ParseDeclineArguments(request.ArgumentsJson);
        if (parsed.IsFailure) return parsed.Error;
        var (reason, prefix, catalogId) = parsed.Value;
        var actor = request.Context.ActorUserId;
        if (reason == "noPermission")
            return prefix is not null || catalogId is not null ? Rejected([new(prefix is not null ? "prefix" : "catalogId", "invalid")])
                : await CanBuildAsync(actor, ct) ? Rejected([new("reason", "permissionGranted")]) : Declined(new(reason, null, null, ""));
        if (reason == "unclear")
            return prefix is not null || catalogId is not null ? Rejected([new(prefix is not null ? "prefix" : "catalogId", "invalid")])
                : await CanBuildAnyAsync(actor, ct) ? Declined(new(reason, null, null, "")) : Rejected([new("reason", "permissionMissing")]);
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

    // Asking what to build only helps whoever can build a form (read and manage forms) or a catalog (read and manage catalogs).
    private async Task<bool> CanBuildAnyAsync(Guid actor, CancellationToken ct) =>
        await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Read, ct) && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Manage, ct)
        || await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.CatalogsRead, ct) && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.CatalogsManage, ct);

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
