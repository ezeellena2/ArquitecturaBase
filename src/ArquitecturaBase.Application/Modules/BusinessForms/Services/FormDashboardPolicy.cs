using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
/// <summary>
/// What the dashboard of a form counts (DOC7-A), from the types of its questions, and how it shows what SQL added up: the number
/// questions outside a block (by currency when the form's standard says which choice question tells it), the choice, yes-or-no and
/// catalog questions outside a block and the date questions outside a block, with the labels the sheet and its counts use in the
/// request's language. Every total goes by number and currency, in the order of the numbers, and a quiet bucket or option is in zero.
/// </summary>
internal static class FormDashboardPolicy
{
    /// <summary>The options of a distribution and the people of «Quién cargó» that go by name; the rest goes in «Otros».</summary>
    public const int Top = 8;

    /// <summary>The numbers, the questions to distribute and the dates, with the currency question of each number by the number's field.</summary>
    public static FormDashboardPlan Plan(FormResponseDefinitionRow definition, string culture, IReadOnlyDictionary<Guid, Guid> currencies)
    {
        var columns = FormResponsePresentationPolicy.Columns(definition, culture);
        // The catalog of a question, as its latest version names it.
        var catalogs = definition.Versions.OrderByDescending(version => version.Number).SelectMany(version => definition.Schemas[version.Id].Questions)
            .Where(question => question.Source?.Catalog is not null).DistinctBy(question => question.FieldId)
            .ToDictionary(question => question.FieldId, question => question.Source!.Catalog!.Value);
        var singles = columns.Where(column => column.Type == "choice").Select(column => column.FieldId).ToHashSet();
        return new(
            columns.Where(column => column.Type == "decimal").Select(column => new FormDashboardNumber(column.FieldId, column.Label,
                currencies.TryGetValue(column.FieldId, out var currency) && singles.Contains(currency) ? currency : null)).ToArray(),
            columns.Where(column => column.Type is "choice" or "multiChoice" or "boolean").Select(column => new FormDashboardQuestion(column.FieldId,
                column.Label, column.Type, catalogs.TryGetValue(column.FieldId, out var catalog) ? catalog : null, column.Options)).ToArray(),
            columns.Where(column => column.Type == "date").Select(column => new FormDashboardDate(column.FieldId, column.Label)).ToArray());
    }

    /// <summary>The labels of the fixed options in the request's language, which break a tie at the cut of the first options.</summary>
    public static IReadOnlyList<FormDashboardChoiceLabel> ChoiceLabels(FormDashboardPlan plan) =>
        plan.Questions.Where(question => question.Type != "boolean" && question.CatalogId is null)
            .SelectMany(question => question.Options.Select(option => new FormDashboardChoiceLabel(question.FieldId, option.Id.ToString(), option.Label))).ToArray();

    /// <summary>The catalog options whose labels the dashboard shows: the ones its distributions name and the currencies of its numbers.</summary>
    public static (IReadOnlyCollection<Guid> FieldIds, IReadOnlyCollection<Guid> ItemIds) CatalogItems(FormDashboardPlan plan,
        IReadOnlyList<FormDashboardDistributionRow> distributions, FormDashboardTotalsRow totals)
    {
        var catalogs = plan.Questions.Where(question => question.CatalogId is not null).Select(question => question.FieldId).ToHashSet();
        var currencies = plan.Numbers.Where(number => number.CurrencyFieldId is { } field && catalogs.Contains(field))
            .ToDictionary(number => number.FieldId, number => number.CurrencyFieldId!.Value);
        var items = distributions.Where(distribution => catalogs.Contains(distribution.FieldId))
            .SelectMany(distribution => distribution.Options.Select(option => (distribution.FieldId, ItemId: Guid.Parse(option.Key))))
            .Concat(totals.Measures.Where(measure => measure.CurrencyId is not null && currencies.ContainsKey(measure.FieldId))
                .Select(measure => (FieldId: currencies[measure.FieldId], ItemId: measure.CurrencyId!.Value)))
            .ToArray();
        return (items.Select(item => item.FieldId).Distinct().ToArray(), items.Select(item => item.ItemId).Distinct().ToArray());
    }

    /// <summary>
    /// One number per number question, or per currency of it (the most answered first and the answers without one last), compared
    /// with the previous range when there is one. A number without answers shows zero; one that does not fit, null and marked.
    /// </summary>
    public static IReadOnlyList<FormDashboardNumberResponse> Numbers(FormDashboardPlan plan, FormDashboardTotalsRow totals, FormDashboardTotalsRow? previous,
        IReadOnlyList<FormDashboardLabelRow> labels)
    {
        var catalog = Labels(labels);
        var questions = plan.Questions.ToDictionary(question => question.FieldId);
        var numbers = new List<FormDashboardNumberResponse>();
        foreach (var number in plan.Numbers)
        {
            var currency = number.CurrencyFieldId is { } field ? questions[field] : null;
            var measures = totals.Measures.Where(measure => measure.FieldId == number.FieldId).ToArray();
            if (measures.Length == 0) numbers.Add(Number(number, currency, null, previous, catalog));
            else numbers.AddRange(measures.Select(measure => Number(number, currency, measure, previous, catalog))
                .OrderBy(item => item.CurrencyId is null).ThenByDescending(item => item.Count)
                .ThenBy(item => item.CurrencyLabel, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true)));
        }
        return numbers;
    }

    /// <summary>Every bucket of the received dates, the quiet ones in zero, with the total of each number in the order of the numbers.</summary>
    public static FormDashboardSeriesResponse Series(FormDashboardWindow window, IReadOnlyList<FormDashboardBucketRow> buckets,
        IReadOnlyList<FormDashboardNumberResponse> numbers)
    {
        var rows = buckets.ToDictionary(bucket => bucket.Start);
        return new(Buckets(window, start => rows.GetValueOrDefault(start) is { } row ? (row.Count, row.Totals) : null, numbers), window.Truncated);
    }

    /// <summary>The series of each date question, in its own window from its first to its last answered date.</summary>
    public static IReadOnlyList<FormDashboardDateSeriesResponse> DateSeries(FormDashboardPlan plan, IReadOnlyDictionary<Guid, FormDashboardWindow> windows,
        IReadOnlyList<FormDashboardDateBucketRow> buckets, IReadOnlyList<FormDashboardNumberResponse> numbers)
    {
        var rows = buckets.ToDictionary(bucket => (bucket.FieldId, bucket.Start));
        return plan.Dates.Select(date =>
        {
            var window = windows.GetValueOrDefault(date.FieldId) ?? FormDashboardWindow.Empty;
            return new FormDashboardDateSeriesResponse(date.FieldId, date.Label,
                Buckets(window, start => rows.GetValueOrDefault((date.FieldId, start)) is { } row ? (row.Count, row.Totals) : null, numbers), window.Truncated);
        }).ToArray();
    }

    /// <summary>
    /// The distribution of each question, its options by count and then by label, each number's total among them, and the rest
    /// apart; a catalog question names its catalog in the request's language.
    /// </summary>
    public static IReadOnlyList<FormDashboardDistributionResponse> Distributions(FormDashboardPlan plan, FormResponseDefinitionRow definition,
        IReadOnlyList<FormDashboardDistributionRow> rows, IReadOnlyList<FormDashboardLabelRow> labels, IReadOnlyList<FormDashboardNumberResponse> numbers,
        string culture)
    {
        var catalog = Labels(labels);
        var counted = rows.ToDictionary(row => row.FieldId);
        return plan.Questions.Select(question =>
        {
            var row = counted.GetValueOrDefault(question.FieldId);
            var options = (row?.Options ?? []).Select(option =>
                new FormDashboardOptionResponse(option.Key, Label(question, option.Key, catalog), option.Count, Totals(numbers, option.Totals)));
            return new FormDashboardDistributionResponse(question.FieldId, question.Label, question.Type,
                question.CatalogId is { } catalogId ? new FormDashboardCatalogResponse(catalogId, CatalogName(definition, catalogId, culture)) : null,
                options.OrderByDescending(option => option.Count).ThenBy(option => option.Label, Comparer(culture)).ThenBy(option => option.Id, StringComparer.Ordinal).ToArray(),
                row?.Others is { Count: > 0 } others ? new FormDashboardOthersResponse(others.Count, others.OptionCount, Totals(numbers, others.Totals)) : null);
        }).ToArray();
    }

    /// <summary>Who loaded how many, by count and then by name; a person without a name (deleted) goes without one.</summary>
    public static FormDashboardActorsResponse Actors(FormDashboardActorsRow row, string culture) =>
        new(row.People.Select(person => new FormResponseFacetOptionResponse(person.UserId.ToString(), person.DisplayName ?? "", person.Count))
                .OrderByDescending(option => option.Count).ThenBy(option => option.Label, Comparer(culture)).ThenBy(option => option.Id, StringComparer.Ordinal).ToArray(),
            row.OthersCount > 0 ? new FormDashboardActorOthersResponse(row.OthersCount, row.OthersPeople) : null);

    /// <summary>The states by count and, on a tie, in the order of the workflow.</summary>
    public static IReadOnlyList<FormDashboardStateResponse> States(IReadOnlyList<FormDashboardStateRow> rows) =>
        rows.OrderByDescending(row => row.Count).ThenBy(row => Enum.TryParse<FormRecordState>(row.State, out var state) ? (int)state : int.MaxValue)
            .Select(row => new FormDashboardStateResponse(row.State, row.Count)).ToArray();

    private static FormDashboardBucketResponse[] Buckets(FormDashboardWindow window,
        Func<DateOnly, (int Count, IReadOnlyList<FormDashboardTotalRow> Totals)?> find, IReadOnlyList<FormDashboardNumberResponse> numbers) =>
        window.Starts.Select(start => find(start) is { } found
            ? new FormDashboardBucketResponse(start, found.Count, Totals(numbers, found.Totals))
            : new FormDashboardBucketResponse(start, 0, Totals(numbers, []))).ToArray();

    // A total for every number of the dashboard, in its order: zero where nothing was answered, null and marked where it does not fit.
    private static FormDashboardTotalResponse[] Totals(IReadOnlyList<FormDashboardNumberResponse> numbers, IReadOnlyList<FormDashboardTotalRow> totals) =>
        numbers.Select(number => totals.FirstOrDefault(total => total.FieldId == number.FieldId && total.CurrencyId == number.CurrencyId) is { } found
            ? new FormDashboardTotalResponse(number.FieldId, number.CurrencyId, found.Total, found.Overflow)
            : new FormDashboardTotalResponse(number.FieldId, number.CurrencyId, 0m, false)).ToArray();

    private static FormDashboardNumberResponse Number(FormDashboardNumber number, FormDashboardQuestion? currency, FormDashboardMeasureRow? measure,
        FormDashboardTotalsRow? previous, IReadOnlyDictionary<(Guid, Guid), string> catalog)
    {
        var choiceId = measure?.CurrencyId;
        var before = previous?.Measures.FirstOrDefault(item => item.FieldId == number.FieldId && item.CurrencyId == choiceId);
        return new(number.FieldId, number.Label, choiceId, currency is not null && choiceId is { } id ? Label(currency, id.ToString(), catalog) : null,
            measure?.Count ?? 0, measure is null ? 0m : measure.Total, measure?.Average, measure?.Minimum, measure?.Maximum,
            previous is null ? null : before is null ? 0m : before.Total, before?.Average, (measure?.Overflow ?? false) || (before?.Overflow ?? false));
    }

    private static string Label(FormDashboardQuestion question, string key, IReadOnlyDictionary<(Guid, Guid), string> catalog) =>
        question.Type == "boolean" ? key
        : question.CatalogId is not null ? (Guid.TryParse(key, out var item) ? catalog.GetValueOrDefault((question.FieldId, item)) : null) ?? ""
        : question.Options.FirstOrDefault(option => option.Id.ToString() == key)?.Label ?? "";

    private static string CatalogName(FormResponseDefinitionRow definition, Guid catalogId, string culture) =>
        definition.CatalogNames?.GetValueOrDefault(catalogId) is { Count: > 0 } name ? FormRecordPresentationPolicy.Text(name, culture) : "";

    private static Dictionary<(Guid, Guid), string> Labels(IReadOnlyList<FormDashboardLabelRow> labels) =>
        labels.DistinctBy(label => (label.FieldId, label.ItemId)).ToDictionary(label => (label.FieldId, label.ItemId), label => label.Label);

    private static StringComparer Comparer(string culture) => StringComparer.Create(CultureInfo.GetCultureInfo(culture), ignoreCase: true);
}

/// <summary>What the dashboard counts: its numbers, its questions to distribute and its dates, all outside a block.</summary>
internal sealed record FormDashboardPlan(IReadOnlyList<FormDashboardNumber> Numbers, IReadOnlyList<FormDashboardQuestion> Questions,
    IReadOnlyList<FormDashboardDate> Dates)
{
    public IReadOnlyList<FormDashboardMeasure> Measures { get; } = [.. Numbers.Select(number => new FormDashboardMeasure(number.FieldId, number.CurrencyFieldId))];
}

internal sealed record FormDashboardNumber(Guid FieldId, string Label, Guid? CurrencyFieldId);

/// <summary>A question to distribute: its type, the catalog its options come from (or none) and, if fixed, its options with their labels.</summary>
internal sealed record FormDashboardQuestion(Guid FieldId, string Label, string Type, Guid? CatalogId, IReadOnlyList<FormResponseOption> Options);

internal sealed record FormDashboardDate(Guid FieldId, string Label);
