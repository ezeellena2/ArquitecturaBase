using System.Text.Json;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
// DOC7-A: the dashboard reads like the sheet (permissions, filter, own responses) and adds up in its reader.
public sealed class FormDashboardServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly CanonicalFormField Amount = new(Guid.NewGuid(), "amount", "decimal", Text("Monto", "Amount"), null, false, null, null);
    private static readonly CanonicalFormField Bank = new(Guid.NewGuid(), "bank", "choice", Text("Banco", "Bank"), null, false, null, null,
        Source: new(Guid.NewGuid()));
    private static readonly CanonicalFormField PaidOn = new(Guid.NewGuid(), "paid_on", "date", Text("Fecha de pago", "Payment date"), null, false, null, null);

    [Fact]
    public async Task Whoever_neither_reads_nor_loads_responses_reads_no_dashboard()
    {
        var (service, reader, user, _) = Create([BusinessFormsPermissions.Read]);

        Assert.Equal(FormDefinitionErrors.Forbidden, (await service.SearchAsync(Guid.NewGuid(), new(), Ct)).Error);
        user.UserId = null;
        Assert.Equal(FormDefinitionErrors.Unauthorized, (await service.SearchAsync(Guid.NewGuid(), new(), Ct)).Error);
        Assert.Empty(reader.Reads);
    }

    [Fact]
    public async Task Whoever_only_loads_reads_every_part_of_the_dashboard_from_their_own_responses()
    {
        var (service, reader, user, _) = Create([BusinessFormsPermissions.RecordsCreate]);
        var item = Guid.NewGuid();
        reader.Totals = Totals(2, dates: [new(PaidOn.FieldId, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 9))]);
        reader.Distributions = [new(Bank.FieldId, [new(item.ToString(), 2, [])], null)];
        var someoneElse = new ListFormRecordsRequest { ActorIds = [Guid.NewGuid()], ReceivedFromUtc = Utc(2026, 10, 1, 3), ReceivedToUtc = Utc(2026, 11, 1, 3) };

        var result = await service.SearchAsync(Guid.NewGuid(), new() { Filter = someoneElse }, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["totals", "totals", "buckets", "dates", "distributions", "labels", "actors", "states"], reader.Reads.Select(read => read.Family));
        // The labels of a catalog come only from the creator's own responses, like everything else.
        Assert.All(reader.Reads, read => Assert.Equal((user.UserId, someoneElse.ActorIds), (read.Filter.OwnerUserId, read.Filter.ActorIds)));
    }

    [Fact]
    public async Task Whoever_reads_responses_keeps_every_person_in_the_dashboard()
    {
        var (service, reader, _, _) = Create([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsCreate]);

        Assert.True((await service.SearchAsync(Guid.NewGuid(), new(), Ct)).IsSuccess);

        Assert.Null(Assert.Single(reader.Reads).Filter.OwnerUserId);
    }

    [Fact]
    public async Task Losing_the_permission_while_reading_discards_the_dashboard()
    {
        var (service, reader, user, permissions) = Create([BusinessFormsPermissions.RecordsRead]);
        reader.Totals = Totals(1);
        reader.BeforeReturn = () => permissions.Permissions.Remove(user.UserId!.Value);

        Assert.Equal(FormDefinitionErrors.Forbidden, (await service.SearchAsync(Guid.NewGuid(), new(), Ct)).Error);
    }

    [Theory]
    [InlineData("year")]
    [InlineData("")]
    [InlineData("Day")]
    public async Task An_unknown_period_is_its_own_error_before_reading(string period)
    {
        var (service, reader, _, _) = Create([BusinessFormsPermissions.RecordsRead]);

        var result = await service.SearchAsync(Guid.NewGuid(), new() { Period = period }, Ct);

        Assert.Equal(FormRecordErrors.PeriodInvalid, result.Error);
        Assert.Empty(reader.Reads);
    }

    [Fact]
    public async Task The_filter_follows_the_rules_of_the_sheet_and_an_unknown_form_is_not_found()
    {
        var (service, reader, _, _) = Create([BusinessFormsPermissions.RecordsRead]);

        var invalid = await service.SearchAsync(Guid.NewGuid(), new() { Filter = new() { Conditions = [new(Guid.NewGuid(), "answered", null)] } }, Ct);
        var backwards = await service.SearchAsync(Guid.NewGuid(), new() { Filter = new() { ReceivedFromUtc = Utc(2026, 11, 1, 3), ReceivedToUtc = Utc(2026, 10, 1, 3) } }, Ct);

        Assert.Equal(FormRecordErrors.FilterInvalid, invalid.Error);
        Assert.Equal(FormRecordErrors.FilterInvalid, backwards.Error);
        Assert.Empty(reader.Reads);
        var (unknown, _, _, _) = Create([BusinessFormsPermissions.RecordsRead], definition: false);
        Assert.Equal(FormRecordErrors.NotFound, (await unknown.SearchAsync(Guid.NewGuid(), new(), Ct)).Error);
    }

    [Fact]
    public async Task The_previous_range_is_read_only_with_both_bounds_and_october_compares_with_september()
    {
        var (service, reader, _, _) = Create([BusinessFormsPermissions.RecordsRead]);
        reader.Totals = Totals(123);
        reader.PreviousTotals = Totals(104);
        var october = new ListFormRecordsRequest { ReceivedFromUtc = Utc(2026, 10, 1, 3), ReceivedToUtc = Utc(2026, 11, 1, 3), State = "Approved" };

        var compared = (await service.SearchAsync(Guid.NewGuid(), new() { Filter = october }, Ct)).Value;

        Assert.Equal((123, new FormDashboardPreviousResponse(Utc(2026, 9, 1, 3), Utc(2026, 10, 1, 3), 104)), (compared.Count, compared.Previous));
        var previous = reader.Reads.Where(read => read.Family == "totals").Skip(1).Single().Filter;
        Assert.Equal((Utc(2026, 9, 1, 3), Utc(2026, 10, 1, 3), "Approved"), (previous.ReceivedFromUtc, previous.ReceivedToUtc, previous.State));
        // Only the current range asks for the dates of its date questions.
        Assert.Equal([[PaidOn.FieldId], []], reader.DateFields.Select(fields => fields.ToArray()));
        var (open, openReader, _, _) = Create([BusinessFormsPermissions.RecordsRead]);
        Assert.Null((await open.SearchAsync(Guid.NewGuid(), new() { Filter = new() { ReceivedFromUtc = Utc(2026, 10, 1, 3) } }, Ct)).Value.Previous);
        Assert.Single(openReader.Reads, read => read.Family == "totals");
    }

    [Fact]
    public async Task The_series_is_read_in_the_zone_of_the_business_without_a_profile_zone_from_its_first_bucket()
    {
        var (service, reader, _, _) = Create([BusinessFormsPermissions.RecordsRead]);
        reader.Totals = Totals(3, [new(Amount.FieldId, null, 3, 60m, 20m, 10m, 30m, false)]);
        reader.Buckets = [new(new DateOnly(2026, 10, 12), 2, [new(Amount.FieldId, null, 40m, false)])];

        var dashboard = (await service.SearchAsync(Guid.NewGuid(), new() { Period = "week",
            Filter = new() { ReceivedFromUtc = Utc(2026, 10, 1, 3), ReceivedToUtc = Utc(2026, 11, 1, 3) } }, Ct)).Value;

        Assert.Equal(new FormDashboardSeries("week", "America/Argentina/Buenos_Aires", new DateOnly(2026, 9, 28)), reader.Series);
        Assert.Equal(("week", "America/Argentina/Buenos_Aires"), (dashboard.Period, dashboard.TimeZoneId));
        // Today is 2026-10-22: the weeks of the month in course end in the current one.
        Assert.Equal([new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 19)],
            dashboard.Series.Buckets.Select(bucket => bucket.Start));
        Assert.Equal([0, 0, 2, 0], dashboard.Series.Buckets.Select(bucket => bucket.Count));
        Assert.Equal([0m, 0m, 40m, 0m], dashboard.Series.Buckets.Select(bucket => Assert.Single(bucket.Totals).Total!.Value));
    }

    [Fact]
    public async Task The_buckets_follow_the_zone_of_the_profile_of_whoever_reads()
    {
        var accounts = new InMemoryUserAccounts();
        var reader = await accounts.CreateAsync(Email.Create("lectura.tablero@example.com").Value, null, false, "Lectura", "es", Ct, "UTC");
        var (service, dashboardReader, _, _) = Create([BusinessFormsPermissions.RecordsRead], accounts: accounts, actor: reader.Id);
        // 02:30 UTC of the 12th is still the 11th in Buenos Aires, but the profile is in UTC.
        dashboardReader.Totals = Totals(1, first: Utc(2026, 10, 12, 2, 30));

        var dashboard = (await service.SearchAsync(Guid.NewGuid(), new(), Ct)).Value;

        Assert.Equal("UTC", dashboard.TimeZoneId);
        Assert.Equal(new FormDashboardSeries("day", "UTC", new DateOnly(2026, 10, 12)), dashboardReader.Series);
    }

    [Fact]
    public async Task A_zone_that_cannot_be_read_counts_in_utc()
    {
        var (service, reader, _, _) = Create([BusinessFormsPermissions.RecordsRead], zone: "Not/AZone");
        reader.Totals = Totals(1, first: Utc(2026, 10, 5, 2));

        var dashboard = (await service.SearchAsync(Guid.NewGuid(), new(), Ct)).Value;

        Assert.Equal("UTC", dashboard.TimeZoneId);
        Assert.Equal(new DateOnly(2026, 10, 5), reader.Series!.First);
    }

    [Fact]
    public async Task Each_date_question_reads_its_buckets_from_the_first_of_its_window()
    {
        var (service, reader, _, _) = Create([BusinessFormsPermissions.RecordsRead]);
        reader.Totals = Totals(2, dates: [new(PaidOn.FieldId, new DateOnly(2026, 9, 3), new DateOnly(2026, 10, 9))]);
        reader.DateBuckets = [new(PaidOn.FieldId, new DateOnly(2026, 10, 1), 2, [])];

        var dashboard = (await service.SearchAsync(Guid.NewGuid(), new() { Period = "month" }, Ct)).Value;

        Assert.Equal([new FormDashboardDateWindow(PaidOn.FieldId, new DateOnly(2026, 9, 1))], reader.DateWindows);
        var series = Assert.Single(dashboard.DateSeries);
        Assert.Equal((PaidOn.FieldId, "Fecha de pago", false), (series.FieldId, series.Label, series.Truncated));
        Assert.Equal([(new DateOnly(2026, 9, 1), 0), (new DateOnly(2026, 10, 1), 2)], series.Buckets.Select(bucket => (bucket.Start, bucket.Count)));
    }

    [Fact]
    public async Task Without_responses_only_the_totals_are_read_and_the_range_still_shows_its_days()
    {
        var (service, reader, _, _) = Create([BusinessFormsPermissions.RecordsRead]);

        var dashboard = (await service.SearchAsync(Guid.NewGuid(), new() { Filter = new() { ReceivedFromUtc = Utc(2026, 9, 1, 3), ReceivedToUtc = Utc(2026, 9, 4, 3) } }, Ct)).Value;

        Assert.Equal(["totals", "totals"], reader.Reads.Select(read => read.Family));
        Assert.Equal([0, 0, 0], dashboard.Series.Buckets.Select(bucket => bucket.Count));
        Assert.Equal(0, Assert.Single(dashboard.Numbers).Count);
        Assert.Empty(Assert.Single(dashboard.DateSeries).Buckets);
        Assert.Empty(Assert.Single(dashboard.Distributions).Options);
        Assert.Empty(dashboard.Actors.Options);
        Assert.Empty(dashboard.States);
    }

    [Fact]
    public async Task Without_a_declared_currency_each_number_is_added_up_as_a_whole_and_catalog_labels_are_read_once()
    {
        var (service, reader, _, _) = Create([BusinessFormsPermissions.RecordsRead]);
        var item = Guid.NewGuid();
        reader.Totals = Totals(2, [new(Amount.FieldId, null, 2, 30m, 15m, 10m, 20m, false)]);
        reader.Distributions = [new(Bank.FieldId, [new(item.ToString(), 2, [new(Amount.FieldId, null, 30m, false)])], null)];
        reader.Labels = [new(Bank.FieldId, item, "Banco Galicia")];
        reader.Actors = new([new(Guid.NewGuid(), "Ana Pérez", 2)], 0, 0);
        reader.States = [new("Received", 2)];

        var dashboard = (await service.SearchAsync(Guid.NewGuid(), new(), Ct)).Value;

        Assert.All(reader.Measures, measures => Assert.Equal([new FormDashboardMeasure(Amount.FieldId)], measures));
        Assert.Equal([Bank.FieldId], reader.DistributedFields);
        Assert.Empty(reader.ChoiceLabels!);
        Assert.Equal([Bank.FieldId], reader.CatalogItems!.Value.FieldIds);
        Assert.Equal([item], reader.CatalogItems.Value.ItemIds);
        var option = Assert.Single(dashboard.Distributions[0].Options);
        Assert.Equal(("Banco Galicia", 2, 30m), (option.Label, option.Count, Assert.Single(option.Totals).Total!.Value));
        Assert.Equal(("Ana Pérez", 2), (dashboard.Actors.Options[0].Label, dashboard.Actors.Options[0].Count));
        Assert.Equal(new FormDashboardStateResponse("Received", 2), Assert.Single(dashboard.States));
        Assert.Null(Assert.Single(dashboard.Numbers).CurrencyId);
    }

    // DOC1: the reading of «Comprobante de transferencia» says the currency goes with the amount; the copy names both questions in its
    // sealed bound map. A version of the standard the module does not bring declares nothing.
    [Fact]
    public async Task A_copy_of_a_standard_adds_up_each_amount_by_the_currency_its_reading_declares()
    {
        var currency = new CanonicalFormField(Guid.NewGuid(), "currency", "choice", Text("Moneda", "Currency"), null, false, null,
            [new(Guid.NewGuid(), Text("Pesos", "Pesos")), new(Guid.NewGuid(), Text("Dólares", "Dollars"))]);
        var bindings = JsonSerializer.Serialize(new Dictionary<string, Guid> { ["amount"] = Amount.FieldId, ["currency"] = currency.FieldId });
        var (copy, copyReader, _, _) = Create([BusinessFormsPermissions.RecordsRead], questions: [Amount, currency], standard: ("forms.transfer-receipt", 1, bindings));
        var (other, otherReader, _, _) = Create([BusinessFormsPermissions.RecordsRead], questions: [Amount, currency], standard: ("forms.transfer-receipt", 2, bindings));

        Assert.True((await copy.SearchAsync(Guid.NewGuid(), new(), Ct)).IsSuccess);
        Assert.True((await other.SearchAsync(Guid.NewGuid(), new(), Ct)).IsSuccess);

        Assert.NotEmpty(copyReader.Measures);
        Assert.All(copyReader.Measures, measures => Assert.Equal([new FormDashboardMeasure(Amount.FieldId, currency.FieldId)], measures));
        Assert.All(otherReader.Measures, measures => Assert.Equal([new FormDashboardMeasure(Amount.FieldId)], measures));
    }

    private static FormDashboardTotalsRow Totals(int count, IReadOnlyList<FormDashboardMeasureRow>? measures = null, DateTime? first = null,
        IReadOnlyList<FormDashboardDateRangeRow>? dates = null) =>
        new(count, count == 0 ? null : first ?? Utc(2026, 10, 5, 12), count == 0 ? null : first ?? Utc(2026, 10, 21, 12), measures ?? [], dates ?? []);

    private static (FormDashboardService Service, FakeFormDashboardReader Reader, FakeCurrentUser User, FakePermissionService Permissions) Create(
        IReadOnlyList<string> granted, bool definition = true, string zone = "America/Argentina/Buenos_Aires", InMemoryUserAccounts? accounts = null,
        Guid? actor = null, IReadOnlyList<CanonicalFormField>? questions = null, (string Key, int Version, string BindingsJson)? standard = null)
    {
        var user = new FakeCurrentUser { UserId = actor ?? Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [.. granted];
        var responses = new FakeFormResponseReader();
        if (definition)
        {
            var version = new FormResponseVersionRow(Guid.NewGuid(), 1, JsonSerializer.Serialize(Text("Pagos", "Payments")),
                JsonSerializer.Serialize(new FormVersionSchema(1, questions ?? [Amount, Bank, PaidOn]), JsonSerializerOptions.Web), "es", ["es", "en"]);
            responses.Definition = new(Guid.NewGuid(), "PAG", version.Id, [version])
            {
                StandardKey = standard?.Key, StandardVersion = standard?.Version, StandardBindingsJson = standard?.BindingsJson,
            };
        }
        var reader = new FakeFormDashboardReader();
        var settings = new FakeSystemSettingsReader { Presentation = SystemPresentationResponse.Defaults with { DefaultTimeZoneId = zone } };
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 15, 0, 0, TimeSpan.Zero));
        var service = new FormDashboardService(responses, reader, new(user, permissions, responses),
            RequestValidators.For(new FormDashboardRequestValidator(), new ListFormRecordsRequestValidator()),
            new FormDashboardZonePolicy(accounts ?? new InMemoryUserAccounts(), settings), [new BusinessFormStandardSource()], clock,
            NullLogger<FormDashboardService>.Instance);
        return (service, reader, user, permissions);
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    private static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
}
