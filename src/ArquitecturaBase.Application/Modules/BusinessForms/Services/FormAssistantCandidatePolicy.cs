using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormAssistantCandidatePolicy(IFormSelectionReader reader, IUserReader users,
    IPermissionService permissions, FormCatalogOptionsPolicy options)
{
    public async Task<bool> IsActiveRecipientAsync(Guid recipientUserId, string culture, CancellationToken ct) =>
        recipientUserId != Guid.Empty && culture is "es" or "en" &&
        await users.FindByIdAsync(recipientUserId, ct) is { IsActive: true } user && user.Culture == culture;

    public async Task<bool> IsEligibleAsync(Guid recipientUserId, string culture, CancellationToken ct) =>
        recipientUserId != Guid.Empty && culture is "es" or "en" &&
        await users.FindByIdAsync(recipientUserId, ct) is { IsActive: true } user && user.Culture == culture &&
        await permissions.HasPermissionAsync(recipientUserId, BusinessFormsPermissions.RecordsCreate, ct);

    public async Task<Result<FormPreparedAssistantCandidates>> ListAsync(Guid recipientUserId, string recipientCulture, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!await IsEligibleAsync(recipientUserId, recipientCulture, ct)) return new FormPreparedAssistantCandidates([], true);
        var rows = await reader.ListAssistantAsync(ct);
        // A bounded discovery snapshot may authorize only its included references; absence requires completeness.
        var result = new List<FormPreparedAssistantCandidate>();
        foreach (var row in rows.Take(100).Where(row => !string.IsNullOrWhiteSpace(row.OfferWhen)))
        {
            var prepared = await PrepareAsync(row, recipientUserId, recipientCulture, ct);
            if (prepared.IsFailure) continue;
            result.Add(prepared.Value);
        }
        return await IsEligibleAsync(recipientUserId, recipientCulture, ct)
            ? new FormPreparedAssistantCandidates(result, rows.Count <= 100) : new FormPreparedAssistantCandidates([], true);
    }

    public async Task<Result<FormPreparedAssistantCandidate>> PrepareAsync(FormAssistantCandidateRow row, Guid recipient, string culture, CancellationToken ct)
    {
        var schema = FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson);
        var prepared = await options.PrepareAsync(recipient, culture, schema, row.Culture, ct);
        return Project(row, prepared);
    }

    internal static Result<FormPreparedAssistantCandidate> Project(FormAssistantCandidateRow row, StructuredFormPreparationResponse prepared)
    {
        if (!prepared.CanSend) return FormRecordErrors.InvalidResponse;
        var schema = FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson);
        var questions = schema.Questions.Where(field => field.Type is "shortText" or "longText" or "decimal" or "date" or "boolean" or "choice")
            .Select(field => new StructuredFormQuestion(field.FieldId, field.Key, field.Type, field.Label, field.Help, field.Required, field.MaxLength,
                field.Options?.Select(option => new StructuredFormChoice(option.ChoiceId, option.Label)).ToArray(), field.Source?.Catalog)).ToArray();
        var form = new StructuredFormSelectionRow(row.DefinitionId, row.VersionId, row.VersionNumber, row.Prefix,
            FormDraftPolicy.Read<Dictionary<string, string>>(row.NameJson), row.QuestionCount,
            FormDraftPolicy.Read<Dictionary<string, string>>(row.SubmitLabelJson), schema.Questions.Any(field => field.Source?.Catalog is not null)) { DefinitionRevision = row.DefinitionRevision };
        return new FormPreparedAssistantCandidate(form, row.DefinitionRevision, row.OfferWhen, row.Culture, questions, prepared.Catalogs);
    }

    internal static string PresentationCulture(FormPreparedAssistantCandidate form, string requested) =>
        Has(form.Form.Name, requested) && Has(form.Form.SubmitLabel, requested)
        && form.Questions.All(question => Has(question.Label, requested) && (question.Options?.All(option => Has(option.Label, requested)) ?? true))
        && form.Catalogs.All(catalog => catalog.CatalogNames is { } names && Has(names, requested)
            && catalog.Options.All(option => option.Translations is { } translations && translations.TryGetValue(requested, out var text)
                && !string.IsNullOrWhiteSpace(text.Text)
                && (option.Translations.TryGetValue(form.OriginalCulture, out var original) && string.IsNullOrWhiteSpace(original.Detail) || !string.IsNullOrWhiteSpace(text.Detail))))
        ? requested : form.OriginalCulture;

    internal static string Text(IReadOnlyDictionary<string, string> texts, string culture, string original) =>
        Has(texts, culture) ? texts[culture] : texts[original];
    private static bool Has(IReadOnlyDictionary<string, string> texts, string culture) =>
        texts.TryGetValue(culture, out var value) && !string.IsNullOrWhiteSpace(value);
}
