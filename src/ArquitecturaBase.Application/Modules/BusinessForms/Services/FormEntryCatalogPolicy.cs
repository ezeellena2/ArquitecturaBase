using System.Globalization;
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
internal sealed class FormEntryCatalogPolicy(FormCatalogOptionsPolicy options, IFormEntryReader reader, IFormCatalogReader catalogs)
{
    public async Task<(FormEntryCatalogResponse Catalog, FormPersonRecipientDataRow? PersonData)> DescribeAsync(Guid recipient,
        CanonicalFormField question, string culture, FormPersonRecipientDataRow? personData, CancellationToken ct)
    {
        var source = await options.ResolveAsync(recipient, question, personData, ct);
        var groupBy = await GroupByAsync(question, culture, ct);
        // A list that depends on other answers waits for them: what it can offer is known only once they are chosen.
        if (source.Issue == FormRecordErrors.CatalogDependsOnAnswer.Code)
            return (new(null, null, null) { DependsOn = question.Source!.Match!.Where(match => match.QuestionId is not null)
                .Select(match => match.QuestionId!.Value).Distinct().ToArray(), GroupBy = groupBy }, source.PersonData);
        if (source.Issue is { } issue) return (new(null, issue, source.MissingData), source.PersonData);
        var page = await PageAsync(recipient, source, new(source.Catalog!.Id, source.Containment, null, null, 1, 2, culture), culture, ct);
        if (page.TotalCount == 0 && question.Required) return (new(null, FormRecordErrors.CatalogEmpty.Code, null), source.PersonData);
        if (question.Selection is { } selection && page.TotalCount < selection.MinSelected)
            return (new(null, FormRecordErrors.CatalogInsufficientOptions.Code, null), source.PersonData);
        return (new(page.TotalCount == 1 ? page.Items[0] : null, null, null) { GroupBy = groupBy }, source.PersonData);
    }

    public async Task<Result<PagedResult<FormEntryOptionResponse>>> ListAsync(Guid recipient, CanonicalFormField question, string culture,
        ListFormEntryOptionsRequest request, CancellationToken ct)
    {
        var source = await options.ResolveAsync(recipient, question, null, ct, request.Answers);
        if (source.Issue is { } issue) return Blocking(issue);
        var containment = source.Containment;
        string? without = null;
        // LD1-A: the value chosen first is one more filter, on the field the question chooses first by.
        if (question.Source!.GroupBy is { } groupBy && source.Catalog!.Kind == "Manual")
        {
            if (request.WithoutGroup) without = groupBy.ToString("D");
            else if (request.Group is { } group)
            {
                containment = FormCatalogGroupPolicy.Containment(containment, groupBy, group);
                if (containment is null) return new PagedResult<FormEntryOptionResponse>([], request.Page, request.PageSize, 0);
            }
        }
        return await PageAsync(recipient, source, new(source.Catalog!.Id, containment,
            string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(), null, request.Page, request.PageSize, culture, without), culture, ct);
    }

    /// <summary>LD1-A: the values the options of the question have in the field it chooses first by, after its filters.</summary>
    public async Task<Result<FormEntryGroupsResponse>> ListGroupsAsync(Guid recipient, CanonicalFormField question, string culture,
        ListFormEntryGroupsRequest request, CancellationToken ct)
    {
        var source = await options.ResolveAsync(recipient, question, null, ct, request.Answers);
        if (source.Issue is { } issue) return Blocking(issue);
        var field = source.Fields.SingleOrDefault(value => value.Id == question.Source!.GroupBy);
        if (source.Catalog!.Kind != "Manual" || field is null) return FormRecordErrors.CatalogFilterUnavailable;
        var rows = await reader.ListGroupsAsync(source.Catalog.Id, source.Containment, field.Id, ct);
        var sourceItems = field.Type == "catalog" && field.SourceCatalogId is { } sourceId ? await catalogs.ListAllItemsAsync(sourceId, ct) : [];
        return new FormEntryGroupsResponse(FormCatalogGroupPolicy.Present(field, rows, sourceItems, culture));
    }

    // The field a question chooses first by, named in the content language; none if its catalog no longer has it.
    private async Task<FormEntryGroupByResponse?> GroupByAsync(CanonicalFormField question, string culture, CancellationToken ct)
    {
        if (question.Source?.GroupBy is not { } groupBy || await catalogs.FindAsync(question.Source.Catalog!.Value, ct) is not { Kind: "Manual" } catalog)
            return null;
        var field = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson).SingleOrDefault(value => value.Id == groupBy);
        return field is null ? null : new(field.Id, FormRecordPresentationPolicy.Text(field.Name, culture));
    }

    /// <summary>
    /// LD1-A: a correction starts the first list with the value the saved option has now in the field to choose first, presented
    /// like the values of the list, in the items of the block too. An option the catalog no longer has gets no value.
    /// </summary>
    public async Task<IReadOnlyList<FormEntryCorrectionAnswer>> WithGroupsAsync(FormVersionSchema schema, IReadOnlyList<FormEntryCorrectionAnswer> answers,
        string culture, CancellationToken ct)
    {
        var questions = schema.Questions.Where(question => question.Source is { Catalog: not null, GroupBy: not null }).ToDictionary(question => question.FieldId);
        if (questions.Count == 0) return answers;
        var values = new Dictionary<Guid, Func<Guid, FormEntryGroupResponse?>?>();
        async Task<FormEntryCorrectionAnswer> Grouped(FormEntryCorrectionAnswer answer)
        {
            if (answer.Rows is { } rows)
            {
                var items = new List<IReadOnlyList<FormEntryCorrectionAnswer>>();
                foreach (var row in rows)
                {
                    var item = new List<FormEntryCorrectionAnswer>();
                    foreach (var inner in row) item.Add(await Grouped(inner));
                    items.Add(item);
                }
                return answer with { Rows = items };
            }
            if (!questions.TryGetValue(answer.FieldId, out var question) || answer.Options is not [var option]) return answer;
            if (!values.TryGetValue(question.FieldId, out var group)) values[question.FieldId] = group = await GroupValuesAsync(question, culture, ct);
            return group?.Invoke(option.Id) is { } value ? answer with { Group = value } : answer;
        }
        var grouped = new List<FormEntryCorrectionAnswer>();
        foreach (var answer in answers) grouped.Add(await Grouped(answer));
        return grouped;
    }

    // The value each option of the question's catalog has in its field to choose first, or null without that field.
    private async Task<Func<Guid, FormEntryGroupResponse?>?> GroupValuesAsync(CanonicalFormField question, string culture, CancellationToken ct)
    {
        if (await catalogs.FindAsync(question.Source!.Catalog!.Value, ct) is not { Kind: "Manual" } catalog) return null;
        var field = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson).SingleOrDefault(value => value.Id == question.Source.GroupBy);
        if (field is null) return null;
        var items = (await catalogs.ListAllItemsAsync(catalog.Id, ct)).ToDictionary(item => item.Id);
        var sourceItems = field.Type == "catalog" && field.SourceCatalogId is { } sourceId ? await catalogs.ListAllItemsAsync(sourceId, ct) : [];
        var key = field.Id.ToString("D");
        return itemId => items.TryGetValue(itemId, out var item)
            ? FormCatalogGroupPolicy.Present(field, [new FormEntryGroupRow(FormCatalogGroupPolicy.Value(item.FieldValuesJson, key), 1)], sourceItems, culture).Single() : null;
    }

    /// <summary>The offer of each answered catalog question with only the chosen options, still active and within the filters.</summary>
    public async Task<Result<IReadOnlyList<StructuredFormCatalogOffer>>> OffersAsync(Guid recipient, FormVersionSchema schema,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> chosen, string culture, string originalCulture, CancellationToken ct,
        IReadOnlyList<IReadOnlyDictionary<Guid, Guid>>? items = null)
    {
        var offers = new List<StructuredFormCatalogOffer>();
        FormPersonRecipientDataRow? personData = null;
        // A dependent list filters by the option chosen in its earlier question, which is checked first in this same order.
        var answers = chosen.Where(pair => pair.Value.Count == 1).ToDictionary(pair => pair.Key, pair => pair.Value[0]);
        foreach (var question in schema.Questions.Where(question => question.Source?.Catalog is not null && chosen.ContainsKey(question.FieldId)))
        {
            var field = "answers." + question.FieldId.ToString("D");
            var offer = await OfferAsync(recipient, question, chosen[question.FieldId].Distinct().ToArray(), answers, personData, culture,
                originalCulture, _ => field, ct);
            if (offer.IsFailure) return offer.Error;
            personData = offer.Value.PersonData;
            offers.Add(offer.Value.Offer);
        }
        if (items is not { Count: > 0 }) return offers;
        // LD3-A: a list of the block filters by the answer of its own item when the filter names a question of the block, and by the
        // answer outside the block otherwise. The items go together, once per distinct answer of their own they depend on, with the
        // options chosen in them, in a single offer per question; an option that does not correspond goes to the first item that chose it.
        var group = schema.Questions.First(question => question.Type == "group");
        var block = schema.Questions.SkipWhile(question => question.FieldId != group.FieldId).Skip(1)
            .TakeWhile(question => question.Repeated == true).ToArray();
        foreach (var question in block.Where(question => question.Source?.Catalog is not null))
        {
            var own = (question.Source!.Match ?? []).Select(match => match.QuestionId).OfType<Guid>()
                .Where(id => block.Any(other => other.FieldId == id)).Distinct().ToArray();
            var rows = Enumerable.Range(0, items.Count).Where(row => items[row].ContainsKey(question.FieldId)).ToArray();
            StructuredFormCatalogOffer? merged = null;
            foreach (var batch in rows.GroupBy(row => string.Join("|", own.Select(id => items[row].TryGetValue(id, out var choice) ? choice.ToString("D") : ""))))
            {
                var dependencies = new Dictionary<Guid, Guid>(answers);
                foreach (var id in own)
                    if (items[batch.First()].TryGetValue(id, out var choice)) dependencies[id] = choice;
                var offer = await OfferAsync(recipient, question, batch.Select(row => items[row][question.FieldId]).Distinct().ToArray(), dependencies,
                    personData, culture, originalCulture, missing => Cell(group, question,
                        missing is { } id ? batch.First(row => items[row][question.FieldId] == id) : batch.First()), ct);
                if (offer.IsFailure) return offer.Error;
                personData = offer.Value.PersonData;
                merged = merged is null ? offer.Value.Offer
                    : merged with { Options = [.. merged.Options, .. offer.Value.Offer.Options.Where(option => merged.Options.All(known => known.Id != option.Id))] };
            }
            if (merged is not null) offers.Add(merged);
        }
        return offers;
    }

    private static string Cell(CanonicalFormField group, CanonicalFormField question, int row) =>
        $"answers.{group.FieldId:D}.{row.ToString(CultureInfo.InvariantCulture)}.{question.FieldId:D}";

    // The offer of a catalog question with the options chosen in it, for the person whose data its filters use. An error goes to
    // field: given the option that does not correspond, or null when the whole list does not.
    private async Task<Result<(StructuredFormCatalogOffer Offer, FormPersonRecipientDataRow? PersonData)>> OfferAsync(Guid recipient,
        CanonicalFormField question, Guid[] ids, IReadOnlyDictionary<Guid, Guid> answers, FormPersonRecipientDataRow? personData,
        string culture, string originalCulture, Func<Guid?, string> field, CancellationToken ct)
    {
        var source = await options.ResolveAsync(recipient, question, personData, ct, answers);
        // Without the answer it depends on, no option of the list corresponds to it.
        if (source.Issue == FormRecordErrors.CatalogDependsOnAnswer.Code) return FieldErrors.On(FormRecordErrors.CatalogItemInactive, field(null));
        if (source.Issue is { } issue) return FieldErrors.On(Blocking(issue), field(null));
        var catalog = source.Catalog!;
        var query = new FormEntryItemsQuery(catalog.Id, source.Containment, null, ids, 1, ids.Length, culture);
        IReadOnlyList<StructuredFormCatalogOption> offered;
        if (FormCatalogKindPolicy.HasItems(catalog.Kind))
            offered = await options.ManualOptionsAsync(source, (await reader.ListItemsAsync(query, ct)).Items, culture, ct);
        else
        {
            var definition = FormDraftPolicy.Read<FormCatalogFilterDefinition>(catalog.FiltersJson!);
            offered = FormCatalogOptionsPolicy.FilterOptions((await reader.ListPeopleAsync(recipient, definition, query, ct)).Items, definition, culture);
        }
        if (ids.Where(id => offered.All(option => option.Id != id)).Select(id => (Guid?)id).FirstOrDefault() is { } missing)
            return FieldErrors.On(FormRecordErrors.CatalogItemInactive, field(missing));
        var offer = new StructuredFormCatalogOffer(question.FieldId, question.Key, FormRecordPresentationPolicy.Text(question.Label, culture),
            catalog.Id, catalog.Revision, catalog.Kind, FormCatalogOptionsPolicy.Name(catalog.NameJson, culture, originalCulture), offered,
            FormDraftPolicy.Read<Dictionary<string, string>>(catalog.NameJson), FormCatalogOptionsPolicy.ValueType(catalog));
        if (FormCatalogOfferPolicy.InvalidValues(offer) || FormCatalogOfferPolicy.Validate(false, offered) is not null)
            return FieldErrors.On(FormRecordErrors.CatalogOptionInvalid, field(null));
        return (offer, source.PersonData);
    }

    private async Task<PagedResult<FormEntryOptionResponse>> PageAsync(Guid recipient, FormCatalogQuestionSource source,
        FormEntryItemsQuery query, string culture, CancellationToken ct)
    {
        var catalog = source.Catalog!;
        if (FormCatalogKindPolicy.HasItems(catalog.Kind))
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
