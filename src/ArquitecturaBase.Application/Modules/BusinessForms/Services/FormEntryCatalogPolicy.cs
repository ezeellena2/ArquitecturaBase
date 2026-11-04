using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Catalog questions loaded in the app. The list is read page by page with the same fixed and person filters as WhatsApp,
/// for the person who loads, and nothing is sealed until sending: then the offer holds only the chosen options, checked
/// again against those filters.
/// </summary>
internal sealed class FormEntryCatalogPolicy(FormCatalogOptionsPolicy options, IFormEntryReader reader)
{
    public async Task<(FormEntryCatalogResponse Catalog, FormPersonRecipientDataRow? PersonData)> DescribeAsync(Guid recipient,
        CanonicalFormField question, string culture, FormPersonRecipientDataRow? personData, CancellationToken ct)
    {
        var source = await options.ResolveAsync(recipient, question, personData, ct);
        // A list that depends on other answers waits for them: what it can offer is known only once they are chosen.
        if (source.Issue == FormRecordErrors.CatalogDependsOnAnswer.Code)
            return (new(null, null, null) { DependsOn = question.Source!.Match!.Where(match => match.QuestionId is not null)
                .Select(match => match.QuestionId!.Value).Distinct().ToArray() }, source.PersonData);
        if (source.Issue is { } issue) return (new(null, issue, source.MissingData), source.PersonData);
        var page = await PageAsync(recipient, source, new(source.Catalog!.Id, source.Containment, null, null, 1, 2, culture), culture, ct);
        if (page.TotalCount == 0 && question.Required) return (new(null, FormRecordErrors.CatalogEmpty.Code, null), source.PersonData);
        if (question.Selection is { } selection && page.TotalCount < selection.MinSelected)
            return (new(null, FormRecordErrors.CatalogInsufficientOptions.Code, null), source.PersonData);
        return (new(page.TotalCount == 1 ? page.Items[0] : null, null, null), source.PersonData);
    }

    public async Task<Result<PagedResult<FormEntryOptionResponse>>> ListAsync(Guid recipient, CanonicalFormField question, string culture,
        ListFormEntryOptionsRequest request, CancellationToken ct)
    {
        var source = await options.ResolveAsync(recipient, question, null, ct, request.Answers);
        if (source.Issue is { } issue) return Blocking(issue);
        return await PageAsync(recipient, source, new(source.Catalog!.Id, source.Containment,
            string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(), null, request.Page, request.PageSize, culture), culture, ct);
    }

    /// <summary>The offer of each answered catalog question with only the chosen options, still active and within the filters.</summary>
    public async Task<Result<IReadOnlyList<StructuredFormCatalogOffer>>> OffersAsync(Guid recipient, FormVersionSchema schema,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> chosen, string culture, string originalCulture, CancellationToken ct)
    {
        var offers = new List<StructuredFormCatalogOffer>();
        FormPersonRecipientDataRow? personData = null;
        // A dependent list filters by the option chosen in its earlier question, which is checked first in this same order.
        var answers = chosen.Where(pair => pair.Value.Count == 1).ToDictionary(pair => pair.Key, pair => pair.Value[0]);
        foreach (var question in schema.Questions.Where(question => question.Source?.Catalog is not null && chosen.ContainsKey(question.FieldId)))
        {
            var field = "answers." + question.FieldId.ToString("D");
            var ids = chosen[question.FieldId].Distinct().ToArray();
            var source = await options.ResolveAsync(recipient, question, personData, ct, answers);
            personData = source.PersonData;
            // Without the answer it depends on, no option of the list corresponds to it.
            if (source.Issue == FormRecordErrors.CatalogDependsOnAnswer.Code) return FieldErrors.On(FormRecordErrors.CatalogItemInactive, field);
            if (source.Issue is { } issue) return FieldErrors.On(Blocking(issue), field);
            var catalog = source.Catalog!;
            var query = new FormEntryItemsQuery(catalog.Id, source.Containment, null, ids, 1, ids.Length, culture);
            IReadOnlyList<StructuredFormCatalogOption> offered;
            if (catalog.Kind == "Manual")
                offered = await options.ManualOptionsAsync(source, (await reader.ListItemsAsync(query, ct)).Items, culture, ct);
            else
            {
                var definition = FormDraftPolicy.Read<FormCatalogFilterDefinition>(catalog.FiltersJson!);
                offered = FormCatalogOptionsPolicy.FilterOptions((await reader.ListPeopleAsync(recipient, definition, query, ct)).Items, definition, culture);
            }
            if (ids.Any(id => offered.All(option => option.Id != id))) return FieldErrors.On(FormRecordErrors.CatalogItemInactive, field);
            var offer = new StructuredFormCatalogOffer(question.FieldId, question.Key, FormRecordPresentationPolicy.Text(question.Label, culture),
                catalog.Id, catalog.Revision, catalog.Kind, FormCatalogOptionsPolicy.Name(catalog.NameJson, culture, originalCulture), offered,
                FormDraftPolicy.Read<Dictionary<string, string>>(catalog.NameJson), FormCatalogOptionsPolicy.ValueType(catalog));
            if (FormCatalogOfferPolicy.InvalidValues(offer) || FormCatalogOfferPolicy.Validate(false, offered) is not null)
                return FieldErrors.On(FormRecordErrors.CatalogOptionInvalid, field);
            offers.Add(offer);
        }
        return offers;
    }

    private async Task<PagedResult<FormEntryOptionResponse>> PageAsync(Guid recipient, FormCatalogQuestionSource source,
        FormEntryItemsQuery query, string culture, CancellationToken ct)
    {
        var catalog = source.Catalog!;
        if (catalog.Kind == "Manual")
        {
            var items = await reader.ListItemsAsync(query, ct);
            return new((await options.ManualOptionsAsync(source, items.Items, culture, ct)).Select(Present).ToArray(), items.Page, items.PageSize, items.TotalCount);
        }
        var definition = FormDraftPolicy.Read<FormCatalogFilterDefinition>(catalog.FiltersJson!);
        var people = await reader.ListPeopleAsync(recipient, definition, query, ct);
        return new(FormCatalogOptionsPolicy.FilterOptions(people.Items, definition, culture).Select(Present).ToArray(), people.Page, people.PageSize, people.TotalCount);
    }

    // The question's presentation decides title and description, as in WhatsApp: an empty description stays empty.
    private static FormEntryOptionResponse Present(StructuredFormCatalogOption option) =>
        new(option.Id, option.Shown?.Title ?? option.Text, option.Shown is null ? option.Detail : option.Shown.Description);

    private static Error Blocking(string issue) => issue == FormRecordErrors.RecipientDataMissing.Code ? FormRecordErrors.RecipientDataMissing
        : issue == FormRecordErrors.CatalogFilterUnavailable.Code ? FormRecordErrors.CatalogFilterUnavailable
        : issue == FormRecordErrors.CatalogDependsOnAnswer.Code ? FormRecordErrors.CatalogDependsOnAnswer : FormRecordErrors.CatalogUnavailable;
}
