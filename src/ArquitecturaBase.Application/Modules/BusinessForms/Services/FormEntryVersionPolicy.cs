using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Configuration;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The active version as the app loads it, in its content language, with each catalog question described for the person whose
/// data its filters use. Only reads.
/// </summary>
internal sealed class FormEntryVersionPolicy(IFormEntryReader reader, FormEntryCatalogPolicy catalogs, FormPresentationCulturePolicy presentation,
    IOptions<BusinessFormsOptions> options, FormIssueEntryPolicy issues)
{
    public async Task<Result<(FormEntryResponse Entry, FormVersionSchema Schema)>> DescribeAsync(Guid definitionId, Guid person,
        CancellationToken cancellationToken)
    {
        var definition = await reader.FindDefinitionAsync(definitionId, null, cancellationToken);
        if (definition is null) return FormDefinitionErrors.NotFound;
        if (definition.Deactivated) return FormEntryErrors.Unavailable;
        if (definition.Version is not { } version) return FormDefinitionErrors.NotFound;
        var schema = FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson);
        var culture = await CultureAsync(definitionId, version, schema, cancellationToken);
        FormPersonRecipientDataRow? personData = null;
        var questions = new List<FormEntryQuestionResponse>(schema.Questions.Count);
        // A published section has questions below it: its title goes on the first one.
        var sections = (schema.Sections ?? []).GroupBy(section => section.Start).ToDictionary(group => group.Key, group => group.Last().Title);
        foreach (var question in schema.Questions)
        {
            FormEntryCatalogResponse? catalog = null;
            if (question.Source?.Catalog is not null)
                (catalog, personData) = await catalogs.DescribeAsync(person, question, culture, personData, cancellationToken);
            questions.Add(new(question.FieldId, question.Type, FormRecordPresentationPolicy.Text(question.Label, culture),
                Help(question, culture), question.Required, question.MaxLength,
                question.Options?.Select(option => new FormEntryChoiceResponse(option.ChoiceId, FormRecordPresentationPolicy.Text(option.Label, culture))).ToArray(),
                question.Selection, question.Files, catalog)
            {
                Section = sections.TryGetValue(questions.Count, out var title) ? FormRecordPresentationPolicy.Text(title, culture) : null,
                Rows = question.Rows, Repeated = question.Repeated,
                Calculation = question.Calculation, ShowWhen = question.ShowWhen,
            });
        }
        return (new FormEntryResponse(definitionId, version.VersionId, version.Number, definition.Prefix,
            FormRecordPresentationPolicy.Text(FormDraftPolicy.Read<Dictionary<string, string>>(version.NameJson), culture), culture,
            FormRecordPresentationPolicy.Text(FormDraftPolicy.Read<Dictionary<string, string>>(version.SubmitLabelJson), culture),
            options.Value.MaxFileSizeMiB, questions) { IssuePreview = await issues.FindAsync(definitionId, cancellationToken) is not null }, schema);
    }

    // The (i) of a question: its own help and, after it, what each toggle of a bound question means with the value the form chose
    // (IV1-A: «Sin IVA: …» or «Con IVA: …» in «Precio unitario»).
    internal static string? Help(CanonicalFormField question, string culture)
    {
        var texts = new List<string>();
        if (question.Help is { Count: > 0 } help) texts.Add(FormRecordPresentationPolicy.Text(help, culture));
        foreach (var toggle in question.Toggles ?? [])
            if ((toggle.Value ? toggle.WhenOn : toggle.WhenOff) is { Count: > 0 } said) texts.Add(FormRecordPresentationPolicy.Text(said, culture));
        return texts.Count == 0 ? null : string.Join(" ", texts);
    }

    // The menu language when the version is complete in it; the original otherwise.
    public Task<string> CultureAsync(Guid definitionId, FormEntryVersionRow version, FormVersionSchema schema, CancellationToken cancellationToken) =>
        presentation.CultureAsync(definitionId, version.Culture, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es",
            version.NameJson, version.SubmitLabelJson, schema, cancellationToken);
}
