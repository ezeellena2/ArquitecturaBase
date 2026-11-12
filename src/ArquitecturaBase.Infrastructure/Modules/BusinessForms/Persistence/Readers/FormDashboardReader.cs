using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
/// <summary>
/// The dashboard of a form (DOC7-A) over the sheet's filter (<see cref="FormResponseQuery.Filter"/>): it takes the command of the
/// filtered response ids without running it, checks its shape (<see cref="FormRecordNavigationSql.Ids"/>), keeps its typed
/// parameters and adds up in fixed SQL, one statement per family. The measures, the period, the zone, the first buckets, the labels
/// and the cap of named options go as parameters, never as text. Every sum, average, minimum and maximum goes through
/// <see cref="Fit"/>: what a C# decimal cannot hold comes back null with its mark, so one value never breaks the rest.
/// </summary>
internal sealed class FormDashboardReader(ApplicationDbContext dbContext) : IFormDashboardReader
{
    // The largest C# decimal, as a PostgreSQL numeric.
    private const string DecimalMaximum = "79228162514264337593543950335";

    public async Task<FormDashboardTotalsRow> FindTotalsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        IReadOnlyList<FormDashboardMeasure> measures, IReadOnlyCollection<Guid> dateFieldIds, CancellationToken cancellationToken)
    {
        const string total = "measured.\"Total\"", average = "measured.\"Average\"", minimum = "measured.\"Minimum\"", maximum = "measured.\"Maximum\"";
        var rows = await Query<TotalsSqlRow>(definition, filter, $"""
            SELECT 'responses' AS "Kind", NULL::uuid AS "FieldId", NULL::uuid AS "CurrencyId", COUNT(*)::integer AS "Count",
                MIN(record."SubmittedAtUtc") AS "FirstAtUtc", MAX(record."SubmittedAtUtc") AS "LastAtUtc", NULL::numeric AS "Total",
                NULL::numeric AS "Average", NULL::numeric AS "Minimum", NULL::numeric AS "Maximum", false AS "Overflow",
                NULL::date AS "FirstDate", NULL::date AS "LastDate"
            FROM matching JOIN "BusinessFormRecords" AS record ON record."Id" = matching."Id"
            UNION ALL
            SELECT 'measure', measured."FieldId", measured."CurrencyId", measured."Count", NULL, NULL, {Fit(total)}, {Fit(average)},
                {Fit(minimum)}, {Fit(maximum)}, {Over(total)} OR {Over(average)} OR {Over(minimum)} OR {Over(maximum)}, NULL, NULL
            FROM (
                SELECT measure."FieldId", currency."ChoiceId" AS "CurrencyId", COUNT(*)::integer AS "Count", SUM(answer."DecimalValue") AS "Total",
                    AVG(answer."DecimalValue") AS "Average", MIN(answer."DecimalValue") AS "Minimum", MAX(answer."DecimalValue") AS "Maximum"
                FROM matching
                {MeasureJoin("matching.\"Id\"")}
                WHERE answer."DecimalValue" IS NOT NULL
                GROUP BY measure."FieldId", currency."ChoiceId") AS measured
            UNION ALL
            SELECT 'date', answer."FieldId", NULL, COUNT(*)::integer, NULL, NULL, NULL, NULL, NULL, NULL, false,
                MIN(answer."DateValue"), MAX(answer."DateValue")
            FROM matching JOIN "BusinessFormRecordValues" AS answer ON answer."RecordId" = matching."Id"
            WHERE answer."FieldId" = ANY(@dashboard_dates) AND answer."DateValue" IS NOT NULL
            GROUP BY answer."FieldId"
            """, [.. Measures(measures), Uuids("dashboard_dates", dateFieldIds)]).ToListAsync(cancellationToken);
        var responses = rows.Single(row => row.Kind == "responses");
        return new(responses.Count, responses.FirstAtUtc, responses.LastAtUtc,
            rows.Where(row => row.Kind == "measure").Select(row => new FormDashboardMeasureRow(row.FieldId!.Value, row.CurrencyId, row.Count,
                row.Total, row.Average, row.Minimum, row.Maximum, row.Overflow)).ToArray(),
            rows.Where(row => row.Kind == "date")
                .Select(row => new FormDashboardDateRangeRow(row.FieldId!.Value, row.FirstDate!.Value, row.LastDate!.Value)).ToArray());
    }

    public async Task<IReadOnlyList<FormDashboardBucketRow>> ListBucketsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        FormDashboardSeries series, IReadOnlyList<FormDashboardMeasure> measures, CancellationToken cancellationToken)
    {
        // The local date a response starts its bucket on: date_trunc in the zone of the buckets, with weeks from Monday.
        const string total = "summed.\"Total\"";
        var rows = await Query<BucketSqlRow>(definition, filter, $"""
            , bucketed AS (
                SELECT record."Id", (date_trunc(@dashboard_period, record."SubmittedAtUtc" AT TIME ZONE @dashboard_zone))::date AS "Start"
                FROM matching JOIN "BusinessFormRecords" AS record ON record."Id" = matching."Id")
            SELECT bucketed."Start", NULL::uuid AS "MeasureId", NULL::uuid AS "CurrencyId", COUNT(*)::integer AS "Count", NULL::numeric AS "Total",
                false AS "Overflow"
            FROM bucketed
            WHERE bucketed."Start" >= @dashboard_first
            GROUP BY bucketed."Start"
            UNION ALL
            SELECT summed."Start", summed."MeasureId", summed."CurrencyId", summed."Count", {Fit(total)}, {Over(total)}
            FROM (
                SELECT bucketed."Start", measure."FieldId" AS "MeasureId", currency."ChoiceId" AS "CurrencyId", COUNT(*)::integer AS "Count",
                    SUM(answer."DecimalValue") AS "Total"
                FROM bucketed
                {MeasureJoin("bucketed.\"Id\"")}
                WHERE bucketed."Start" >= @dashboard_first AND answer."DecimalValue" IS NOT NULL
                GROUP BY bucketed."Start", measure."FieldId", currency."ChoiceId") AS summed
            """, [.. Measures(measures), new("dashboard_period", NpgsqlDbType.Text) { Value = series.Period },
                new("dashboard_zone", NpgsqlDbType.Text) { Value = series.TimeZoneId }, new("dashboard_first", NpgsqlDbType.Date) { Value = series.First }])
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.Start).OrderBy(bucket => bucket.Key).Select(bucket => new FormDashboardBucketRow(bucket.Key,
            bucket.Single(row => row.MeasureId is null).Count, bucket.Where(row => row.MeasureId is not null)
                .Select(row => new FormDashboardTotalRow(row.MeasureId!.Value, row.CurrencyId, row.Total, row.Overflow)).ToArray())).ToArray();
    }

    public async Task<IReadOnlyList<FormDashboardDateBucketRow>> ListDateBucketsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        string period, IReadOnlyList<FormDashboardDateWindow> windows, IReadOnlyList<FormDashboardMeasure> measures, CancellationToken cancellationToken)
    {
        // A date answer is a date without a zone: its bucket is date_trunc of the date itself, from each question's first shown bucket on.
        const string total = "summed.\"Total\"";
        var rows = await Query<DateBucketSqlRow>(definition, filter, $"""
            , dated AS (
                SELECT answer."RecordId", answer."FieldId", (date_trunc(@dashboard_period, answer."DateValue"::timestamp))::date AS "Start"
                FROM matching
                JOIN "BusinessFormRecordValues" AS answer ON answer."RecordId" = matching."Id"
                JOIN UNNEST(@dashboard_dates, @dashboard_firsts) AS shown("FieldId", "First") ON shown."FieldId" = answer."FieldId"
                WHERE answer."DateValue" IS NOT NULL AND (date_trunc(@dashboard_period, answer."DateValue"::timestamp))::date >= shown."First")
            SELECT dated."FieldId", dated."Start", NULL::uuid AS "MeasureId", NULL::uuid AS "CurrencyId", COUNT(*)::integer AS "Count",
                NULL::numeric AS "Total", false AS "Overflow"
            FROM dated
            GROUP BY dated."FieldId", dated."Start"
            UNION ALL
            SELECT summed."FieldId", summed."Start", summed."MeasureId", summed."CurrencyId", summed."Count", {Fit(total)}, {Over(total)}
            FROM (
                SELECT dated."FieldId", dated."Start", measure."FieldId" AS "MeasureId", currency."ChoiceId" AS "CurrencyId", COUNT(*)::integer AS "Count",
                    SUM(answer."DecimalValue") AS "Total"
                FROM dated
                {MeasureJoin("dated.\"RecordId\"")}
                WHERE answer."DecimalValue" IS NOT NULL
                GROUP BY dated."FieldId", dated."Start", measure."FieldId", currency."ChoiceId") AS summed
            """, [.. Measures(measures), new("dashboard_period", NpgsqlDbType.Text) { Value = period },
                Uuids("dashboard_dates", windows.Select(window => window.FieldId)),
                new("dashboard_firsts", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = windows.Select(window => window.First).ToArray() }])
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => (row.FieldId, row.Start)).OrderBy(bucket => bucket.Key.FieldId).ThenBy(bucket => bucket.Key.Start)
            .Select(bucket => new FormDashboardDateBucketRow(bucket.Key.FieldId, bucket.Key.Start, bucket.Single(row => row.MeasureId is null).Count,
                bucket.Where(row => row.MeasureId is not null)
                    .Select(row => new FormDashboardTotalRow(row.MeasureId!.Value, row.CurrencyId, row.Total, row.Overflow)).ToArray())).ToArray();
    }

    public async Task<IReadOnlyList<FormDashboardDistributionRow>> ListDistributionsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        IReadOnlyCollection<Guid> fieldIds, IReadOnlyList<FormDashboardChoiceLabel> labels, IReadOnlyList<FormDashboardMeasure> measures, int top,
        CancellationToken cancellationToken)
    {
        // Each option counts the responses that chose it (an option of «elegir varias» once per response) and adds up their numbers.
        // The cut keeps the first ones by count and, on a tie, by label: a fixed option's from the parameter, a catalog option's from
        // its latest captured reference (only among the owner's responses when there is one), then by key. «Otros» counts each
        // response once, also one that chose several of its options, and adds up its numbers once.
        const string total = "summed.\"Total\"";
        var rows = await Query<OptionSqlRow>(definition, filter, $"""
            , picked AS (
                SELECT DISTINCT answer."FieldId", COALESCE(answer."ChoiceId"::text, CASE WHEN answer."BooleanValue" THEN 'true' ELSE 'false' END) AS "Key",
                    answer."RecordId"
                FROM matching JOIN "BusinessFormRecordValues" AS answer ON answer."RecordId" = matching."Id"
                WHERE answer."FieldId" = ANY(@dashboard_fields) AND (answer."ChoiceId" IS NOT NULL OR answer."BooleanValue" IS NOT NULL)),
            labeled AS (
                SELECT named."FieldId", named."Key", MIN(named."Label") AS "Label"
                FROM (
                    SELECT (fixed.value ->> 'fieldId')::uuid AS "FieldId", fixed.value ->> 'key' AS "Key", fixed.value ->> 'label' AS "Label"
                    FROM jsonb_array_elements(@dashboard_labels) AS fixed(value)
                    UNION ALL
                    (SELECT DISTINCT ON (reference."FieldId", reference."ItemId") reference."FieldId", reference."ItemId"::text, reference."Label"
                    FROM ({FormResponseQuery.ReferenceLabelSql("@dashboard_presentations")}) AS reference
                    WHERE reference."DefinitionId" = @dashboard_definition AND reference."FieldId" = ANY(@dashboard_fields)
                        AND (@dashboard_owner IS NULL OR EXISTS (SELECT 1 FROM "BusinessFormRecords" AS owned
                            WHERE owned."Id" = reference."RecordId" AND owned."ActorUserId" = @dashboard_owner))
                    ORDER BY reference."FieldId", reference."ItemId", reference."Id" DESC)) AS named
                GROUP BY named."FieldId", named."Key"),
            counted AS (
                SELECT picked."FieldId", picked."Key", COUNT(*)::integer AS "Count",
                    ROW_NUMBER() OVER (PARTITION BY picked."FieldId"
                        ORDER BY COUNT(*) DESC, lower(COALESCE(labeled."Label", '')), picked."Key" COLLATE "C") AS "Rank"
                FROM picked LEFT JOIN labeled ON labeled."FieldId" = picked."FieldId" AND labeled."Key" = picked."Key"
                GROUP BY picked."FieldId", picked."Key", labeled."Label"),
            grouped AS (
                SELECT DISTINCT picked."FieldId", CASE WHEN counted."Rank" <= @dashboard_top THEN picked."Key" END AS "Key", picked."RecordId"
                FROM picked JOIN counted ON counted."FieldId" = picked."FieldId" AND counted."Key" = picked."Key")
            SELECT grouped."FieldId", grouped."Key", NULL::uuid AS "MeasureId", NULL::uuid AS "CurrencyId", COUNT(*)::integer AS "Count",
                CASE WHEN grouped."Key" IS NULL THEN (SELECT COUNT(*)::integer FROM counted
                    WHERE counted."FieldId" = grouped."FieldId" AND counted."Rank" > @dashboard_top) ELSE 0 END AS "OptionCount",
                NULL::numeric AS "Total", false AS "Overflow"
            FROM grouped
            GROUP BY grouped."FieldId", grouped."Key"
            UNION ALL
            SELECT summed."FieldId", summed."Key", summed."MeasureId", summed."CurrencyId", summed."Count", 0, {Fit(total)}, {Over(total)}
            FROM (
                SELECT grouped."FieldId", grouped."Key", measure."FieldId" AS "MeasureId", currency."ChoiceId" AS "CurrencyId", COUNT(*)::integer AS "Count",
                    SUM(answer."DecimalValue") AS "Total"
                FROM grouped
                {MeasureJoin("grouped.\"RecordId\"")}
                WHERE answer."DecimalValue" IS NOT NULL
                GROUP BY grouped."FieldId", grouped."Key", measure."FieldId", currency."ChoiceId") AS summed
            """, [.. Measures(measures), Uuids("dashboard_fields", fieldIds), new("dashboard_top", NpgsqlDbType.Integer) { Value = top },
                new("dashboard_labels", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(labels.Select(label =>
                    new { fieldId = label.FieldId, key = label.Key, label = label.Label }), JsonSerializerOptions.Web) },
                new("dashboard_presentations", NpgsqlDbType.Jsonb) { Value = FormResponseQuery.Presentations(definition, filter.DisplayCulture) },
                new("dashboard_definition", NpgsqlDbType.Uuid) { Value = definition.Id },
                new("dashboard_owner", NpgsqlDbType.Uuid) { Value = (object?)filter.OwnerUserId ?? DBNull.Value }])
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.FieldId).Select(question => new FormDashboardDistributionRow(question.Key,
            question.Where(row => row.Key is not null && row.MeasureId is null).Select(option => new FormDashboardOptionRow(option.Key!, option.Count,
                Totals(question.Where(row => row.Key == option.Key)))).ToArray(),
            question.SingleOrDefault(row => row.Key is null && row.MeasureId is null) is { } rest
                ? new FormDashboardOthersRow(rest.Count, rest.OptionCount, Totals(question.Where(row => row.Key is null))) : null)).ToArray();
    }

    public async Task<IReadOnlyList<FormDashboardLabelRow>> ListCatalogLabelsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        IReadOnlyCollection<Guid> fieldIds, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken)
    {
        // As the sheet's counts read them: the latest captured reference of each option, among the owner's responses when there is one.
        var fields = fieldIds.ToArray();
        var items = itemIds.ToArray();
        var labels = FormResponseQuery.ReferenceLabels(dbContext, definition, filter.DisplayCulture)
            .Where(label => fields.Contains(label.FieldId) && items.Contains(label.ItemId));
        if (filter.OwnerUserId is { } owner)
            labels = labels.Where(label => dbContext.Set<BusinessFormRecord>().Any(record => record.Id == label.RecordId && record.ActorUserId == owner));
        return await labels.GroupBy(label => new { label.FieldId, label.ItemId })
            .Select(group => new FormDashboardLabelRow(group.Key.FieldId, group.Key.ItemId,
                group.OrderByDescending(label => label.Id).Select(label => label.Label).First()))
            .ToListAsync(cancellationToken);
    }

    public async Task<FormDashboardActorsRow> FindActorsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter, int top,
        CancellationToken cancellationToken)
    {
        // A tie at the cut goes by name; a deleted account keeps its responses without a name, as in the sheet.
        var rows = await Query<ActorSqlRow>(definition, filter, """
            , counted AS (
                SELECT record."ActorUserId", COUNT(*)::integer AS "Count"
                FROM matching JOIN "BusinessFormRecords" AS record ON record."Id" = matching."Id"
                GROUP BY record."ActorUserId"),
            ranked AS (
                SELECT counted."ActorUserId", person."DisplayName", counted."Count",
                    ROW_NUMBER() OVER (ORDER BY counted."Count" DESC, lower(COALESCE(person."DisplayName", '')), counted."ActorUserId") AS "Rank"
                FROM counted LEFT JOIN "AspNetUsers" AS person ON person."Id" = counted."ActorUserId" AND NOT person."IsDeleted")
            SELECT ranked."ActorUserId" AS "UserId", ranked."DisplayName", ranked."Count", 1 AS "People"
            FROM ranked WHERE ranked."Rank" <= @dashboard_top
            UNION ALL
            SELECT NULL, NULL, SUM(ranked."Count")::integer, COUNT(*)::integer
            FROM ranked WHERE ranked."Rank" > @dashboard_top
            HAVING COUNT(*) > 0
            """, new NpgsqlParameter("dashboard_top", NpgsqlDbType.Integer) { Value = top }).ToListAsync(cancellationToken);
        var others = rows.SingleOrDefault(row => row.UserId is null);
        return new(rows.Where(row => row.UserId is not null).Select(row => new FormDashboardActorRow(row.UserId!.Value, row.DisplayName, row.Count)).ToArray(),
            others?.Count ?? 0, others?.People ?? 0);
    }

    public async Task<IReadOnlyList<FormDashboardStateRow>> ListStatesAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        CancellationToken cancellationToken) =>
        (await FormResponseQuery.Filter(dbContext, definition, filter).GroupBy(record => record.State)
            .Select(group => new { group.Key, Count = group.Count() }).ToListAsync(cancellationToken))
            .Select(state => new FormDashboardStateRow(state.Key.ToString(), state.Count)).ToArray();

    /// <summary>
    /// A statement over the filtered responses: <c>matching</c> holds their ids, from the sheet's filter compiled by EF (its shape
    /// checked) with its parameters cloned, and <paramref name="sql"/> follows it (with <c>, name AS (…)</c> it adds its own common
    /// table expressions).
    /// </summary>
    private IQueryable<TRow> Query<TRow>(FormResponseDefinitionRow definition, ListFormRecordsRequest filter, string sql, params NpgsqlParameter[] parameters)
    {
        using var command = FormResponseQuery.Filter(dbContext, definition, filter).Select(record => record.Id).CreateDbCommand();
        var matching = FormRecordNavigationSql.Ids(command.CommandText);
        var arguments = command.Parameters.Cast<NpgsqlParameter>().Select(parameter => (object)parameter.Clone()).Concat(parameters).ToArray();
        // EF reads the text as a format string: the braces of the compiled filter are escaped, as in the sheet's navigation.
        var text = ("WITH matching (\"Id\") AS (\n" + matching + "\n)\n" + sql).ReplaceLineEndings("\n")
            .Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);
        return dbContext.Database.SqlQueryRaw<TRow>(text, arguments);
    }

    // The answers of the measures of each response with its currency: null without a currency question or with it left empty. The
    // currency is one index probe by response and question (LIMIT 1 keeps it a lookup of its own; without a currency question it
    // finds nothing at once).
    private static string MeasureJoin(string recordId) => $"""
        JOIN "BusinessFormRecordValues" AS answer ON answer."RecordId" = {recordId}
        JOIN UNNEST(@dashboard_measures, @dashboard_currencies) AS measure("FieldId", "CurrencyFieldId") ON measure."FieldId" = answer."FieldId"
        LEFT JOIN LATERAL (
            SELECT chosen."ChoiceId" FROM "BusinessFormRecordValues" AS chosen
            WHERE chosen."RecordId" = answer."RecordId" AND chosen."FieldId" = measure."CurrencyFieldId"
            LIMIT 1) AS currency ON true
        """;

    // A numeric a C# decimal holds: rounded to its 28 significant digits and without trailing zeros; past its range, null.
    private static string Fit(string value) =>
        $"CASE WHEN abs({value}) > {DecimalMaximum} THEN NULL ELSE trim_scale(round({value}, " +
        $"GREATEST(0, 28 - CASE WHEN abs({value}) < 1 THEN 0 ELSE floor(log(abs({value})))::integer + 1 END))) END";

    private static string Over(string value) => $"COALESCE(abs({value}) > {DecimalMaximum}, false)";

    private static FormDashboardTotalRow[] Totals(IEnumerable<OptionSqlRow> rows) =>
        rows.Where(row => row.MeasureId is not null).Select(row => new FormDashboardTotalRow(row.MeasureId!.Value, row.CurrencyId, row.Total, row.Overflow)).ToArray();

    private static NpgsqlParameter Uuids(string name, IEnumerable<Guid> ids) => new(name, NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids.ToArray() };

    private static NpgsqlParameter[] Measures(IReadOnlyList<FormDashboardMeasure> measures) =>
    [
        Uuids("dashboard_measures", measures.Select(measure => measure.FieldId)),
        new("dashboard_currencies", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = measures.Select(measure => measure.CurrencyFieldId).ToArray() },
    ];

    private sealed class TotalsSqlRow
    {
        public string Kind { get; init; } = "";
        public Guid? FieldId { get; init; }
        public Guid? CurrencyId { get; init; }
        public int Count { get; init; }
        public DateTime? FirstAtUtc { get; init; }
        public DateTime? LastAtUtc { get; init; }
        public decimal? Total { get; init; }
        public decimal? Average { get; init; }
        public decimal? Minimum { get; init; }
        public decimal? Maximum { get; init; }
        public bool Overflow { get; init; }
        public DateOnly? FirstDate { get; init; }
        public DateOnly? LastDate { get; init; }
    }

    private sealed class BucketSqlRow
    {
        public DateOnly Start { get; init; }
        public Guid? MeasureId { get; init; }
        public Guid? CurrencyId { get; init; }
        public int Count { get; init; }
        public decimal? Total { get; init; }
        public bool Overflow { get; init; }
    }

    private sealed class DateBucketSqlRow
    {
        public Guid FieldId { get; init; }
        public DateOnly Start { get; init; }
        public Guid? MeasureId { get; init; }
        public Guid? CurrencyId { get; init; }
        public int Count { get; init; }
        public decimal? Total { get; init; }
        public bool Overflow { get; init; }
    }

    private sealed class OptionSqlRow
    {
        public Guid FieldId { get; init; }
        public string? Key { get; init; }
        public Guid? MeasureId { get; init; }
        public Guid? CurrencyId { get; init; }
        public int Count { get; init; }
        public int OptionCount { get; init; }
        public decimal? Total { get; init; }
        public bool Overflow { get; init; }
    }

    private sealed class ActorSqlRow
    {
        public Guid? UserId { get; init; }
        public string? DisplayName { get; init; }
        public int Count { get; init; }
        public int People { get; init; }
    }
}
