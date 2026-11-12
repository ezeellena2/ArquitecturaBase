namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

/// <summary>
/// The dashboard of a form (DOC7-A): the sheet's filter, with its grammar and its restriction to one's own responses, and the
/// period every series groups by: <c>day</c>, <c>week</c> (from Monday) or <c>month</c>.
/// </summary>
public sealed record FormDashboardRequest
{
    public static readonly IReadOnlyCollection<string> Periods = ["day", "week", "month"];
    public ListFormRecordsRequest Filter { get; init; } = new();
    public string Period { get; init; } = "day";
    public override string ToString() => "FormDashboardRequest { Content = [REDACTED] }";
}

/// <summary>
/// The dashboard of a form: how many arrived (and in the previous range, with both received bounds), the numbers of each number
/// question, the series by received date and by each date question, the distribution of each choice, yes-or-no or catalog
/// question, who loaded how many and the states. The received buckets are local dates of <see cref="TimeZoneId"/>, the zone of
/// the profile of whoever reads, the same the sheet's filter is built in.
/// </summary>
public sealed record FormDashboardResponse(Guid DefinitionId, string Period, string TimeZoneId, int Count, FormDashboardPreviousResponse? Previous,
    IReadOnlyList<FormDashboardNumberResponse> Numbers, FormDashboardSeriesResponse Series, IReadOnlyList<FormDashboardDateSeriesResponse> DateSeries,
    IReadOnlyList<FormDashboardDistributionResponse> Distributions, FormDashboardActorsResponse Actors, IReadOnlyList<FormDashboardStateResponse> States)
{
    public override string ToString() => "FormDashboardResponse { Content = [REDACTED] }";
}

/// <summary>The range right before the received one, by the calendar of the zone, and how many responses arrived in it.</summary>
public sealed record FormDashboardPreviousResponse(DateTime FromUtc, DateTime ToUtc, int Count);

/// <summary>
/// A number question outside a block, or one currency of it when the form's standard says which choice question tells it
/// (<see cref="CurrencyId"/> and its label; null for no currency or an empty one): how many answered it, the total, the average,
/// the minimum and the maximum, and the total and the average of the previous range. Without answers the total is zero and the
/// rest null; what does not fit in a decimal is null too and <see cref="Overflow"/> says so.
/// </summary>
public sealed record FormDashboardNumberResponse(Guid FieldId, string Label, Guid? CurrencyId, string? CurrencyLabel, int Count, decimal? Total,
    decimal? Average, decimal? Minimum, decimal? Maximum, decimal? PreviousTotal, decimal? PreviousAverage, bool Overflow)
{
    public override string ToString() => "FormDashboardNumberResponse { Content = [REDACTED] }";
}

/// <summary>The buckets of the received dates, from the start of the range (or the first response) to its end (or now).</summary>
public sealed record FormDashboardSeriesResponse(IReadOnlyList<FormDashboardBucketResponse> Buckets, bool Truncated);

/// <summary>The buckets of a date question outside a block, from its first to its last answered date among the responses.</summary>
public sealed record FormDashboardDateSeriesResponse(Guid FieldId, string Label, IReadOnlyList<FormDashboardBucketResponse> Buckets, bool Truncated)
{
    public override string ToString() => "FormDashboardDateSeriesResponse { Content = [REDACTED] }";
}

/// <summary>A day, a week from Monday or a month, by the date it starts on, with how many responses and each number's total.</summary>
public sealed record FormDashboardBucketResponse(DateOnly Start, int Count, IReadOnlyList<FormDashboardTotalResponse> Totals)
{
    public override string ToString() => "FormDashboardBucketResponse { Content = [REDACTED] }";
}

/// <summary>The total of a number of the dashboard, by its question and its currency as in the numbers; null if it does not fit.</summary>
public sealed record FormDashboardTotalResponse(Guid FieldId, Guid? CurrencyId, decimal? Total, bool Overflow)
{
    public override string ToString() => "FormDashboardTotalResponse { Total = [REDACTED] }";
}

/// <summary>
/// How many responses chose each option of a choice, yes-or-no (<c>true</c> and <c>false</c>) or catalog question outside a
/// block: the first ones by count (a tie, by label), with the labels the sheet's counts use and each number's total among them,
/// and the rest in <see cref="Others"/>. <see cref="Catalog"/> names the catalog of a catalog question.
/// </summary>
public sealed record FormDashboardDistributionResponse(Guid FieldId, string Label, string Type, FormDashboardCatalogResponse? Catalog,
    IReadOnlyList<FormDashboardOptionResponse> Options, FormDashboardOthersResponse? Others)
{
    public override string ToString() => "FormDashboardDistributionResponse { Content = [REDACTED] }";
}

/// <summary>The catalog of a question, with its name in the request's language.</summary>
public sealed record FormDashboardCatalogResponse(Guid Id, string Name)
{
    public override string ToString() => "FormDashboardCatalogResponse { Name = [REDACTED] }";
}

public sealed record FormDashboardOptionResponse(string Id, string Label, int Count, IReadOnlyList<FormDashboardTotalResponse> Totals)
{
    public override string ToString() => "FormDashboardOptionResponse { Content = [REDACTED] }";
}

/// <summary>What is left after the first options: how many responses chose any of them (each once), how many options they are and their totals.</summary>
public sealed record FormDashboardOthersResponse(int Count, int OptionCount, IReadOnlyList<FormDashboardTotalResponse> Totals)
{
    public override string ToString() => "FormDashboardOthersResponse { Content = [REDACTED] }";
}

/// <summary>Who loaded how many: the first people by count (a tie, by name), each an option with their name, and the rest.</summary>
public sealed record FormDashboardActorsResponse(IReadOnlyList<FormResponseFacetOptionResponse> Options, FormDashboardActorOthersResponse? Others)
{
    public override string ToString() => "FormDashboardActorsResponse { Content = [REDACTED] }";
}

/// <summary>The responses of the people after the first ones, and how many people they are.</summary>
public sealed record FormDashboardActorOthersResponse(int Count, int PeopleCount);

/// <summary>How many responses are in a state: Received, PendingApproval, Approved, Rejected or Voided.</summary>
public sealed record FormDashboardStateResponse(string State, int Count);

/// <summary>A number question the dashboard adds up and, when the form's standard says so, the choice question that tells its currency.</summary>
public sealed record FormDashboardMeasure(Guid FieldId, Guid? CurrencyFieldId = null);

/// <summary>The series to read: the period, the IANA zone of the buckets and the first bucket it shows; the ones before it are left out.</summary>
public sealed record FormDashboardSeries(string Period, string TimeZoneId, DateOnly First);

/// <summary>A date question to group by and the first bucket its series shows.</summary>
public sealed record FormDashboardDateWindow(Guid FieldId, DateOnly First);

/// <summary>The label of a fixed option, in the request's language, that breaks a tie at the cut of the first options.</summary>
public sealed record FormDashboardChoiceLabel(Guid FieldId, string Key, string Label)
{
    public override string ToString() => "FormDashboardChoiceLabel { Label = [REDACTED] }";
}

/// <summary>How many responses match, when the first and the last arrived, each measure by currency and each date question's range.</summary>
public sealed record FormDashboardTotalsRow(int Count, DateTime? FirstAtUtc, DateTime? LastAtUtc, IReadOnlyList<FormDashboardMeasureRow> Measures,
    IReadOnlyList<FormDashboardDateRangeRow> Dates)
{
    public override string ToString() => "FormDashboardTotalsRow { Content = [REDACTED] }";
}

/// <summary>The answers of a measure with one currency (null: no currency question, or it was left empty); null what does not fit.</summary>
public sealed record FormDashboardMeasureRow(Guid FieldId, Guid? CurrencyId, int Count, decimal? Total, decimal? Average, decimal? Minimum,
    decimal? Maximum, bool Overflow)
{
    public override string ToString() => "FormDashboardMeasureRow { Content = [REDACTED] }";
}

/// <summary>The first and the last answered date of a date question among the matching responses.</summary>
public sealed record FormDashboardDateRangeRow(Guid FieldId, DateOnly First, DateOnly Last)
{
    public override string ToString() => "FormDashboardDateRangeRow { Content = [REDACTED] }";
}

/// <summary>A bucket of received dates with responses, by its local start, with the total of each measure and currency in it.</summary>
public sealed record FormDashboardBucketRow(DateOnly Start, int Count, IReadOnlyList<FormDashboardTotalRow> Totals)
{
    public override string ToString() => "FormDashboardBucketRow { Content = [REDACTED] }";
}

/// <summary>A bucket of a date question with responses, with the total of each measure and currency in it.</summary>
public sealed record FormDashboardDateBucketRow(Guid FieldId, DateOnly Start, int Count, IReadOnlyList<FormDashboardTotalRow> Totals)
{
    public override string ToString() => "FormDashboardDateBucketRow { Content = [REDACTED] }";
}

public sealed record FormDashboardTotalRow(Guid FieldId, Guid? CurrencyId, decimal? Total, bool Overflow)
{
    public override string ToString() => "FormDashboardTotalRow { Total = [REDACTED] }";
}

/// <summary>
/// The first options of a question by count (a choice or catalog option id, or <c>true</c> and <c>false</c>), with the total of
/// each measure among them, and what is left.
/// </summary>
public sealed record FormDashboardDistributionRow(Guid FieldId, IReadOnlyList<FormDashboardOptionRow> Options, FormDashboardOthersRow? Others)
{
    public override string ToString() => "FormDashboardDistributionRow { Content = [REDACTED] }";
}

public sealed record FormDashboardOptionRow(string Key, int Count, IReadOnlyList<FormDashboardTotalRow> Totals)
{
    public override string ToString() => "FormDashboardOptionRow { Content = [REDACTED] }";
}

/// <summary>The responses that chose any option after the first ones, each once, how many options those are and their totals.</summary>
public sealed record FormDashboardOthersRow(int Count, int OptionCount, IReadOnlyList<FormDashboardTotalRow> Totals)
{
    public override string ToString() => "FormDashboardOthersRow { Content = [REDACTED] }";
}

/// <summary>The label of a catalog option as the sheet's counts read it: its latest captured reference, in the version's culture.</summary>
public sealed record FormDashboardLabelRow(Guid FieldId, Guid ItemId, string Label)
{
    public override string ToString() => "FormDashboardLabelRow { Label = [REDACTED] }";
}

/// <summary>The first people by how many responses they loaded, and how many responses and people are left.</summary>
public sealed record FormDashboardActorsRow(IReadOnlyList<FormDashboardActorRow> People, int OthersCount, int OthersPeople)
{
    public override string ToString() => "FormDashboardActorsRow { Content = [REDACTED] }";
}

public sealed record FormDashboardActorRow(Guid UserId, string? DisplayName, int Count)
{
    public override string ToString() => "FormDashboardActorRow { DisplayName = [REDACTED] }";
}

public sealed record FormDashboardStateRow(string State, int Count);
