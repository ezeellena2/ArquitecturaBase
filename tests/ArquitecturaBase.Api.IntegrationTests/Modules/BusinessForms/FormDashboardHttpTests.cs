using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// DOC7-A with PostgreSQL: the dashboard adds up in SQL over the sheet's filter and groups its series by day, week or month in the
// zone of the profile of whoever reads (Buenos Aires, UTC-3, unless a test says otherwise), with the permissions and the restriction
// to one's own responses of the sheet.
[Collection(ApiTestGroup.Name)]
public sealed class FormDashboardHttpTests(ApiFactory factory)
{
    private const string Zone = "America/Argentina/Buenos_Aires";
    private const int Top = 8;
    private static readonly object October = new { receivedFromUtc = "2026-10-01T03:00:00Z", receivedToUtc = "2026-11-01T03:00:00Z" };
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_dashboard_needs_reading_or_loading_responses_and_is_not_stored()
    {
        var payments = await PaymentsAsync();
        using var anonymous = factory.CreateClient();
        using var designer = FormDashboardScenario.Client(factory, await FormDashboardScenario.PersonAsync(factory, "Diseño Tablero", [BusinessFormsPermissions.Read], Ct));
        var path = $"/api/forms/definitions/{payments.Id}/dashboard/search";
        var body = new { filter = new { }, period = "day" };

        using var missing = await anonymous.PostAsJsonAsync(path, body, Ct);
        using var denied = await designer.PostAsJsonAsync(path, body, Ct);
        using var unknown = await payments.Reader.PostAsJsonAsync($"/api/forms/definitions/{Guid.NewGuid()}/dashboard/search", body, Ct);
        using var invalid = await payments.Reader.PostAsJsonAsync(path, new { filter = new { }, period = "year" }, Ct);
        using var allowed = await payments.Reader.PostAsJsonAsync(path, body, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await invalid.ReadJsonAsync();
        Assert.Equal(("Forms.Record.PeriodInvalid", "Elegí cómo agrupar el tablero: por día, por semana o por mes."),
            (problem.GetProperty("code").GetString(), problem.GetProperty("detail").GetString()));
        await FormDashboardContractCapture.WriteAsync(invalid, "dashboard-invalid-period-es", Ct);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.True(allowed.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task By_day_the_dashboard_counts_compares_with_the_previous_month_and_fills_the_quiet_days()
    {
        var payments = await PaymentsAsync();

        using var response = await payments.Reader.PostAsJsonAsync($"/api/forms/definitions/{payments.Id}/dashboard/search", new { filter = October, period = "day" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await FormDashboardContractCapture.WriteAsync(response, "dashboard-day-es", Ct);
        var dashboard = await response.ReadJsonAsync();
        Assert.Equal((Zone, "day", 6), (dashboard.GetProperty("timeZoneId").GetString(), dashboard.GetProperty("period").GetString(), dashboard.GetProperty("count").GetInt32()));
        // October, from a 1st to a 1st in the zone, compares with all of September.
        var previous = dashboard.GetProperty("previous");
        Assert.Equal(("2026-09-01T03:00:00Z", "2026-10-01T03:00:00Z", 2), (previous.GetProperty("fromUtc").GetString(), previous.GetProperty("toUtc").GetString(),
            previous.GetProperty("count").GetInt32()));
        var amount = Assert.Single(dashboard.GetProperty("numbers").EnumerateArray());
        Assert.Equal(("Monto", 5, 2550m, 510m, 50m, 1000m, 400m, 200m), (amount.GetProperty("label").GetString(), amount.GetProperty("count").GetInt32(),
            amount.GetProperty("total").GetDecimal(), amount.GetProperty("average").GetDecimal(), amount.GetProperty("minimum").GetDecimal(),
            amount.GetProperty("maximum").GetDecimal(), amount.GetProperty("previousTotal").GetDecimal(), amount.GetProperty("previousAverage").GetDecimal()));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null, false), (amount.GetProperty("currencyId").ValueKind, amount.GetProperty("currencyLabel").ValueKind,
            amount.GetProperty("overflow").GetBoolean()));
        var buckets = dashboard.GetProperty("series").GetProperty("buckets").EnumerateArray().ToArray();
        Assert.False(dashboard.GetProperty("series").GetProperty("truncated").GetBoolean());
        Assert.Equal(31, buckets.Length);
        Assert.Equal(("2026-10-01", "2026-10-31"), (buckets[0].GetProperty("start").GetString(), buckets[^1].GetProperty("start").GetString()));
        // 23:30 of the 11th in Buenos Aires is 02:30 UTC of the 12th, and 23:59 of the 31st is 02:59 UTC of November 1st.
        var counts = Counts(buckets);
        Assert.Equal((2, 1, 0, 1, 2), (counts["2026-10-01"], counts["2026-10-11"], counts["2026-10-12"], counts["2026-10-20"], counts["2026-10-31"]));
        Assert.Equal(6, counts.Values.Sum());
        Assert.Equal([1500m, 250m, 750m, 50m], buckets.Select(bucket => FirstTotal(bucket)).Where(total => total != 0m));
        var distributions = dashboard.GetProperty("distributions").EnumerateArray().ToDictionary(item => item.GetProperty("label").GetString()!);
        Assert.Equal([("Pesos", 4, 2050m), ("Dólares", 1, 500m)], OptionTotals(distributions["Moneda"]));
        Assert.Equal([("true", 3, 1500m), ("false", 1, 250m)], OptionTotals(distributions["Pagado"]));
        Assert.Equal([("Banco Galicia", 2, 1500m), ("Banco Nación", 1, 250m), ("Mercado Pago", 1, 750m)], OptionTotals(distributions["Banco"]));
        Assert.Equal(["choice", "boolean", "choice"], dashboard.GetProperty("distributions").EnumerateArray().Select(item => item.GetProperty("type").GetString()));
        Assert.Equal(JsonValueKind.Null, distributions["Moneda"].GetProperty("catalog").ValueKind);
        var catalog = distributions["Banco"].GetProperty("catalog");
        Assert.Equal(payments.CatalogId, catalog.GetProperty("id").GetGuid());
        Assert.StartsWith("Bancos ", catalog.GetProperty("name").GetString(), StringComparison.Ordinal);
        // The date question has its own window, from its first to its last answered date among the responses, by day.
        var paidOn = Assert.Single(dashboard.GetProperty("dateSeries").EnumerateArray());
        Assert.Equal((payments.PaidOn.FieldId, "Fecha de pago", false), (paidOn.GetProperty("fieldId").GetGuid(), paidOn.GetProperty("label").GetString(),
            paidOn.GetProperty("truncated").GetBoolean()));
        var dated = paidOn.GetProperty("buckets").EnumerateArray().ToArray();
        Assert.Equal((34, "2026-09-28", "2026-10-31"), (dated.Length, dated[0].GetProperty("start").GetString(), dated[^1].GetProperty("start").GetString()));
        Assert.Equal([("2026-09-28", 1, 1000m), ("2026-10-01", 1, 500m), ("2026-10-10", 1, 250m), ("2026-10-20", 1, 750m), ("2026-10-31", 1, 50m)],
            dated.Where(bucket => bucket.GetProperty("count").GetInt32() > 0)
                .Select(bucket => (bucket.GetProperty("start").GetString(), bucket.GetProperty("count").GetInt32(), FirstTotal(bucket))));
        Assert.Equal([("Ana Tablero", 3), ("Bruno Tablero", 3)], Options(dashboard.GetProperty("actors")));
        Assert.Equal(JsonValueKind.Null, dashboard.GetProperty("actors").GetProperty("others").ValueKind);
        Assert.Equal([("Received", 4), ("PendingApproval", 1), ("Approved", 1)], dashboard.GetProperty("states").EnumerateArray()
            .Select(state => (state.GetProperty("state").GetString(), state.GetProperty("count").GetInt32())));
    }

    [Fact]
    public async Task By_week_the_buckets_start_on_monday_in_the_zone_also_for_the_date_question()
    {
        var payments = await PaymentsAsync();
        payments.Reader.DefaultRequestHeaders.AcceptLanguage.Clear();
        payments.Reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");

        using var response = await payments.Reader.PostAsJsonAsync($"/api/forms/definitions/{payments.Id}/dashboard/search", new { filter = October, period = "week" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await FormDashboardContractCapture.WriteAsync(response, "dashboard-week-en", Ct);
        var dashboard = await response.ReadJsonAsync();
        // Sunday the 11th at 23:30 is in the week of Monday the 5th, although in UTC it is already Monday the 12th.
        Assert.Equal([("2026-09-28", 2), ("2026-10-05", 1), ("2026-10-12", 0), ("2026-10-19", 1), ("2026-10-26", 2)],
            dashboard.GetProperty("series").GetProperty("buckets").EnumerateArray().Select(bucket => (bucket.GetProperty("start").GetString(), bucket.GetProperty("count").GetInt32())));
        Assert.Equal("Amount", dashboard.GetProperty("numbers")[0].GetProperty("label").GetString());
        var distributions = dashboard.GetProperty("distributions").EnumerateArray().ToDictionary(item => item.GetProperty("label").GetString()!);
        Assert.Equal([("Pesos", 4), ("Dollars", 1)], Options(distributions["Currency"]));
        Assert.Equal([("Galicia bank", 2), ("Mercado Pago", 1), ("Nación bank", 1)], Options(distributions["Bank"]));
        Assert.Equal("Banks", distributions["Bank"].GetProperty("catalog").GetProperty("name").GetString());
        // Saturday the 10th and Saturday the 31st go in the weeks of their Mondays.
        var paidOn = Assert.Single(dashboard.GetProperty("dateSeries").EnumerateArray());
        Assert.Equal("Paid on", paidOn.GetProperty("label").GetString());
        Assert.Equal([("2026-09-28", 2, 1500m), ("2026-10-05", 1, 250m), ("2026-10-12", 0, 0m), ("2026-10-19", 1, 750m), ("2026-10-26", 1, 50m)],
            paidOn.GetProperty("buckets").EnumerateArray().Select(bucket => (bucket.GetProperty("start").GetString(), bucket.GetProperty("count").GetInt32(), FirstTotal(bucket))));
    }

    [Fact]
    public async Task Without_a_range_months_go_from_the_first_response_to_today_and_nothing_compares()
    {
        var payments = await PaymentsAsync();

        using var response = await payments.Reader.PostAsJsonAsync($"/api/forms/definitions/{payments.Id}/dashboard/search", new { filter = new { }, period = "month" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await FormDashboardContractCapture.WriteAsync(response, "dashboard-month-es", Ct);
        var dashboard = await response.ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, dashboard.GetProperty("previous").ValueKind);
        Assert.Equal(9, dashboard.GetProperty("count").GetInt32());
        var buckets = dashboard.GetProperty("series").GetProperty("buckets").EnumerateArray().ToArray();
        Assert.Equal([("2026-09-01", 2, 400m), ("2026-10-01", 6, 2550m), ("2026-11-01", 1, 10m)], buckets.Take(3)
            .Select(bucket => (bucket.GetProperty("start").GetString(), bucket.GetProperty("count").GetInt32(), FirstTotal(bucket))));
        // The months after the last response, up to today, are in zero.
        Assert.All(buckets.Skip(3), bucket => Assert.Equal(0, bucket.GetProperty("count").GetInt32()));
        var amount = dashboard.GetProperty("numbers")[0];
        Assert.Equal(JsonValueKind.Null, amount.GetProperty("previousTotal").ValueKind);
        Assert.Equal(2960m, amount.GetProperty("total").GetDecimal());
        // A date question's series ends at its last answered date, not today.
        Assert.Equal([("2026-09-01", 3, 1400m), ("2026-10-01", 4, 1550m), ("2026-11-01", 1, 10m)],
            Assert.Single(dashboard.GetProperty("dateSeries").EnumerateArray()).GetProperty("buckets").EnumerateArray()
                .Select(bucket => (bucket.GetProperty("start").GetString(), bucket.GetProperty("count").GetInt32(), FirstTotal(bucket))));
    }

    [Fact]
    public async Task The_previous_range_and_the_buckets_follow_the_zone_of_the_profile_of_whoever_reads()
    {
        var payments = await PaymentsAsync("UTC");

        using var response = await payments.Reader.PostAsJsonAsync($"/api/forms/definitions/{payments.Id}/dashboard/search", new { filter = October, period = "day" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.ReadJsonAsync();
        Assert.Equal(("UTC", 6), (dashboard.GetProperty("timeZoneId").GetString(), dashboard.GetProperty("count").GetInt32()));
        // 03:00 UTC is not a midnight of the profile's zone: the previous range has the same length.
        var previous = dashboard.GetProperty("previous");
        Assert.Equal(("2026-08-31T03:00:00Z", "2026-10-01T03:00:00Z", 2), (previous.GetProperty("fromUtc").GetString(), previous.GetProperty("toUtc").GetString(),
            previous.GetProperty("count").GetInt32()));
        // In UTC the response of 02:30 of the 12th is on the 12th, and the one of 02:59 of November 1st on that day.
        var buckets = dashboard.GetProperty("series").GetProperty("buckets").EnumerateArray().ToArray();
        var counts = Counts(buckets);
        Assert.Equal((32, 2, 0, 1, 1, 1), (buckets.Length, counts["2026-10-01"], counts["2026-10-11"], counts["2026-10-12"], counts["2026-10-31"], counts["2026-11-01"]));
    }

    [Fact]
    public async Task The_dashboard_adds_up_only_what_the_sheet_filter_takes()
    {
        var payments = await PaymentsAsync();
        var path = $"/api/forms/definitions/{payments.Id}/dashboard/search";

        using var paid = await payments.Reader.PostAsJsonAsync(path, new { filter = new { receivedFromUtc = "2026-10-01T03:00:00Z", receivedToUtc = "2026-11-01T03:00:00Z",
            conditions = new[] { new { fieldId = payments.Paid.FieldId, @operator = "in", values = new[] { "true" } } } }, period = "day" }, Ct);
        using var approved = await payments.Reader.PostAsJsonAsync(path, new { filter = new { state = "Approved" }, period = "day" }, Ct);
        using var bruno = await payments.Reader.PostAsJsonAsync(path, new { filter = new { actorIds = new[] { payments.Bruno } }, period = "day" }, Ct);
        using var voided = await payments.Reader.PostAsJsonAsync(path, new { filter = new { state = "Voided" }, period = "day" }, Ct);

        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        await FormDashboardContractCapture.WriteAsync(paid, "dashboard-filtered-es", Ct);
        var filtered = await paid.ReadJsonAsync();
        Assert.Equal((3, 1), (filtered.GetProperty("count").GetInt32(), filtered.GetProperty("previous").GetProperty("count").GetInt32()));
        Assert.Equal((2, 1500m), (filtered.GetProperty("numbers")[0].GetProperty("count").GetInt32(), filtered.GetProperty("numbers")[0].GetProperty("total").GetDecimal()));
        Assert.Equal([("true", 3)], Options(filtered.GetProperty("distributions").EnumerateArray().Single(item => item.GetProperty("type").GetString() == "boolean")));
        Assert.Equal([("Ana Tablero", 3)], Options(filtered.GetProperty("actors")));
        var single = await approved.ReadJsonAsync();
        Assert.Equal((1, 750m), (single.GetProperty("count").GetInt32(), single.GetProperty("numbers")[0].GetProperty("total").GetDecimal()));
        Assert.Equal([("Approved", 1)], single.GetProperty("states").EnumerateArray().Select(state => (state.GetProperty("state").GetString(), state.GetProperty("count").GetInt32())));
        var person = await bruno.ReadJsonAsync();
        Assert.Equal((5, 1360m), (person.GetProperty("count").GetInt32(), person.GetProperty("numbers")[0].GetProperty("total").GetDecimal()));
        Assert.Equal([("Bruno Tablero", 5)], Options(person.GetProperty("actors")));
        var discarded = await voided.ReadJsonAsync();
        Assert.Equal((1, 999m), (discarded.GetProperty("count").GetInt32(), discarded.GetProperty("numbers")[0].GetProperty("total").GetDecimal()));
    }

    [Fact]
    public async Task Whoever_only_loads_sees_a_dashboard_of_their_own_responses()
    {
        var payments = await PaymentsAsync();
        using var ana = FormDashboardScenario.Client(factory, payments.Ana);

        using var response = await ana.PostAsJsonAsync($"/api/forms/definitions/{payments.Id}/dashboard/search",
            new { filter = new { receivedFromUtc = "2026-10-01T03:00:00Z", receivedToUtc = "2026-11-01T03:00:00Z", actorIds = new[] { payments.Ana, payments.Bruno } }, period = "day" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await FormDashboardContractCapture.WriteAsync(response, "dashboard-own-es", Ct);
        var dashboard = await response.ReadJsonAsync();
        Assert.Equal((3, 1), (dashboard.GetProperty("count").GetInt32(), dashboard.GetProperty("previous").GetProperty("count").GetInt32()));
        Assert.Equal((2, 1500m, 100m), (dashboard.GetProperty("numbers")[0].GetProperty("count").GetInt32(), dashboard.GetProperty("numbers")[0].GetProperty("total").GetDecimal(),
            dashboard.GetProperty("numbers")[0].GetProperty("previousTotal").GetDecimal()));
        Assert.Equal([("Ana Tablero", 3)], Options(dashboard.GetProperty("actors")));
        Assert.Equal([("Banco Galicia", 2)], Options(dashboard.GetProperty("distributions").EnumerateArray().Single(item => item.GetProperty("label").GetString() == "Banco")));
    }

    [Fact]
    public async Task Whoever_only_loads_keeps_the_label_of_her_own_responses_after_someone_else_renames_the_option()
    {
        var reader = await FormDashboardScenario.PersonAsync(factory, "Lectura Tablero", [BusinessFormsPermissions.RecordsRead], Ct);
        var ana = await FormDashboardScenario.PersonAsync(factory, "Ana Tablero", [BusinessFormsPermissions.RecordsCreate], Ct);
        var bruno = await FormDashboardScenario.PersonAsync(factory, "Bruno Tablero", [BusinessFormsPermissions.RecordsCreate], Ct);
        var catalogId = await FormDashboardScenario.CatalogAsync(factory, Ct);
        var bank = FormDashboardScenario.Field("choice", "Banco", "Bank", catalog: catalogId);
        var definitionId = await FormDashboardScenario.FormAsync(factory, reader, [bank], Ct);
        var item = Guid.NewGuid();
        await FormDashboardScenario.AddAsync(factory, definitionId, [new(ana, new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc),
            [new(bank, Item: new(catalogId, item, "Banco Galicia", "Galicia bank"))])], Ct);
        // Later, the option was renamed in the catalog and someone else chose it with its new name.
        await FormDashboardScenario.AddAsync(factory, definitionId, [new(bruno, new DateTime(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc),
            [new(bank, Item: new(catalogId, item, "Galicia renombrado", "Renamed Galicia"))])], Ct);
        var path = $"/api/forms/definitions/{definitionId}/dashboard/search";
        var body = new { filter = new { }, period = "month" };
        using var everyone = FormDashboardScenario.Client(factory, reader);
        using var own = FormDashboardScenario.Client(factory, ana);

        using var all = await everyone.PostAsJsonAsync(path, body, Ct);
        using var hers = await own.PostAsJsonAsync(path, body, Ct);

        Assert.Equal([("Galicia renombrado", 2)], Options(Assert.Single((await all.ReadJsonAsync()).GetProperty("distributions").EnumerateArray())));
        Assert.Equal([("Banco Galicia", 1)], Options(Assert.Single((await hers.ReadJsonAsync()).GetProperty("distributions").EnumerateArray())));
    }

    [Fact]
    public async Task A_form_without_responses_has_a_dashboard_in_zero()
    {
        var publisher = await FormDashboardScenario.PersonAsync(factory, "Lectura Tablero", [BusinessFormsPermissions.RecordsRead], Ct);
        var definitionId = await FormDashboardScenario.FormAsync(factory, publisher, [FormDashboardScenario.Field("decimal", "Monto", "Amount"),
            FormDashboardScenario.Field("choice", "Moneda", "Currency", [FormDashboardScenario.Choice("Pesos", "Pesos")]),
            FormDashboardScenario.Field("date", "Fecha de pago", "Paid on")], Ct);
        using var reader = FormDashboardScenario.Client(factory, publisher);

        using var response = await reader.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/dashboard/search",
            new { filter = new { receivedFromUtc = "2026-09-01T03:00:00Z", receivedToUtc = "2026-09-04T03:00:00Z" }, period = "day" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await FormDashboardContractCapture.WriteAsync(response, "dashboard-empty-es", Ct);
        var dashboard = await response.ReadJsonAsync();
        Assert.Equal((0, 0), (dashboard.GetProperty("count").GetInt32(), dashboard.GetProperty("previous").GetProperty("count").GetInt32()));
        var amount = Assert.Single(dashboard.GetProperty("numbers").EnumerateArray());
        Assert.Equal((0, 0m, JsonValueKind.Null, 0m), (amount.GetProperty("count").GetInt32(), amount.GetProperty("total").GetDecimal(),
            amount.GetProperty("average").ValueKind, amount.GetProperty("previousTotal").GetDecimal()));
        Assert.Equal([0, 0, 0], dashboard.GetProperty("series").GetProperty("buckets").EnumerateArray().Select(bucket => bucket.GetProperty("count").GetInt32()));
        Assert.Empty(Assert.Single(dashboard.GetProperty("distributions").EnumerateArray()).GetProperty("options").EnumerateArray());
        Assert.Empty(Assert.Single(dashboard.GetProperty("dateSeries").EnumerateArray()).GetProperty("buckets").EnumerateArray());
        Assert.Empty(dashboard.GetProperty("actors").GetProperty("options").EnumerateArray());
        Assert.Empty(dashboard.GetProperty("states").EnumerateArray());
        using var open = await reader.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/dashboard/search", new { filter = new { }, period = "day" }, Ct);
        Assert.Empty((await open.ReadJsonAsync()).GetProperty("series").GetProperty("buckets").EnumerateArray());
    }

    [Fact]
    public async Task A_catalog_question_names_its_catalog_and_its_first_eight_options_by_count_and_label_and_keeps_the_rest()
    {
        var publisher = await FormDashboardScenario.PersonAsync(factory, "Carga Tablero", [BusinessFormsPermissions.RecordsRead], Ct);
        var catalogId = await FormDashboardScenario.CatalogAsync(factory, Ct);
        var client = FormDashboardScenario.Field("choice", "Cliente", "Client", catalog: catalogId);
        var amount = FormDashboardScenario.Field("decimal", "Monto", "Amount");
        var definitionId = await FormDashboardScenario.FormAsync(factory, publisher, [client, amount], Ct);
        var items = Enumerable.Range(1, 11).Select(number => new FormDashboardItem(catalogId, Guid.NewGuid(), $"Cliente {number:00}", $"Client {number:00}")).ToArray();
        var at = new DateTime(2026, 10, 10, 15, 0, 0, DateTimeKind.Utc);
        // The first client chose three times and the second twice; the other nine, once each: the tie goes by label.
        await FormDashboardScenario.AddAsync(factory, definitionId, [.. new[] { 0, 0, 0, 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }
            .Select((item, index) => new FormDashboardRecord(publisher, at.AddMinutes(index), [new(client, Item: items[item]), new(amount, Number: 10m)]))], Ct);
        using var reader = FormDashboardScenario.Client(factory, publisher, "en");

        using var response = await reader.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/dashboard/search", new { filter = new { }, period = "month" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await FormDashboardContractCapture.WriteAsync(response, "dashboard-catalog-en", Ct);
        var distribution = Assert.Single((await response.ReadJsonAsync()).GetProperty("distributions").EnumerateArray());
        Assert.Equal((catalogId, "Banks"), (distribution.GetProperty("catalog").GetProperty("id").GetGuid(), distribution.GetProperty("catalog").GetProperty("name").GetString()));
        Assert.Equal([("Client 01", 3, 30m), ("Client 02", 2, 20m), ("Client 03", 1, 10m), ("Client 04", 1, 10m), ("Client 05", 1, 10m), ("Client 06", 1, 10m),
            ("Client 07", 1, 10m), ("Client 08", 1, 10m)], OptionTotals(distribution));
        var others = distribution.GetProperty("others");
        Assert.Equal((3, 3, 30m), (others.GetProperty("count").GetInt32(), others.GetProperty("optionCount").GetInt32(), FirstTotal(others)));
    }

    [Fact]
    public async Task Ties_at_the_cut_go_by_label_and_by_name_and_the_rest_goes_in_others()
    {
        var reader = await FormDashboardScenario.PersonAsync(factory, "Lectura Tablero", [BusinessFormsPermissions.RecordsRead], Ct);
        // The options and the people are created backwards, so neither their order nor their ids follow the labels.
        var options = Enumerable.Range(0, 10).Select(index => (char)('J' - index)).Select(letter => FormDashboardScenario.Choice($"Opción {letter}", $"Option {letter}")).ToArray();
        var tag = FormDashboardScenario.Field("choice", "Etiqueta", "Tag", options);
        var definitionId = await FormDashboardScenario.FormAsync(factory, reader, [tag], Ct);
        var people = new List<Guid>();
        for (var number = 10; number >= 1; number--) people.Add(await FormDashboardScenario.PersonAsync(factory, $"Persona {number:00}", [], Ct));
        await FormDashboardScenario.AddAsync(factory, definitionId, [.. people.Select((person, index) =>
            new FormDashboardRecord(person, new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc).AddHours(index), [new(tag, Choice: options[index].ChoiceId)]))], Ct);
        using var client = FormDashboardScenario.Client(factory, reader);

        using var response = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/dashboard/search", new { filter = October, period = "day" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await FormDashboardContractCapture.WriteAsync(response, "dashboard-people-es", Ct);
        var dashboard = await response.ReadJsonAsync();
        var distribution = Assert.Single(dashboard.GetProperty("distributions").EnumerateArray());
        Assert.Equal("ABCDEFGH".Select(letter => ($"Opción {letter}", 1)), Options(distribution));
        Assert.Equal((2, 2), (distribution.GetProperty("others").GetProperty("count").GetInt32(), distribution.GetProperty("others").GetProperty("optionCount").GetInt32()));
        var actors = dashboard.GetProperty("actors");
        Assert.Equal(Enumerable.Range(1, Top).Select(number => ($"Persona {number:00}", 1)), Options(actors));
        Assert.Equal((2, 2), (actors.GetProperty("others").GetProperty("count").GetInt32(), actors.GetProperty("others").GetProperty("peopleCount").GetInt32()));
    }

    [Fact]
    public async Task A_response_with_two_options_in_others_counts_once_and_adds_its_numbers_once()
    {
        var reader = await FormDashboardScenario.PersonAsync(factory, "Lectura Tablero", [BusinessFormsPermissions.RecordsRead], Ct);
        var tags = "ABCDEFGHIJ".Select(letter => FormDashboardScenario.Choice($"Etiqueta {letter}", $"Tag {letter}")).ToArray();
        var amount = FormDashboardScenario.Field("decimal", "Monto", "Amount");
        var chosen = FormDashboardScenario.Field("multiChoice", "Etiquetas", "Tags", tags);
        var definitionId = await FormDashboardScenario.FormAsync(factory, reader, [amount, chosen], Ct);
        var first = tags.Take(Top).Select(tag => tag.ChoiceId).ToArray();
        var at = new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc);
        await FormDashboardScenario.AddAsync(factory, definitionId,
        [
            new(reader, at, [new(amount, Number: 1m), new(chosen, Choices: first)]),
            new(reader, at.AddHours(1), [new(amount, Number: 2m), new(chosen, Choices: first)]),
            new(reader, at.AddHours(2), [new(amount, Number: 7m), new(chosen, Choices: [tags[8].ChoiceId, tags[9].ChoiceId])]),
        ], Ct);
        using var client = FormDashboardScenario.Client(factory, reader);

        using var response = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/dashboard/search", new { filter = October, period = "day" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var distribution = Assert.Single((await response.ReadJsonAsync()).GetProperty("distributions").EnumerateArray());
        Assert.Equal("multiChoice", distribution.GetProperty("type").GetString());
        Assert.Equal("ABCDEFGH".Select(letter => ($"Etiqueta {letter}", 2, 3m)), OptionTotals(distribution));
        var others = distribution.GetProperty("others");
        Assert.Equal((1, 2, 7m), (others.GetProperty("count").GetInt32(), others.GetProperty("optionCount").GetInt32(), FirstTotal(others)));
    }

    [Fact]
    public async Task A_value_past_the_decimal_range_comes_back_null_and_marked_without_stopping_the_rest()
    {
        var reader = await FormDashboardScenario.PersonAsync(factory, "Lectura Tablero", [BusinessFormsPermissions.RecordsRead], Ct);
        var (pesos, dollars) = (FormDashboardScenario.Choice("Pesos", "Pesos"), FormDashboardScenario.Choice("Dólares", "Dollars"));
        var amount = FormDashboardScenario.Field("decimal", "Monto", "Amount");
        var quantity = FormDashboardScenario.Field("decimal", "Cantidad", "Quantity");
        var currency = FormDashboardScenario.Field("choice", "Moneda", "Currency", [pesos, dollars]);
        var definitionId = await FormDashboardScenario.FormAsync(factory, reader, [amount, quantity, currency], Ct);
        var ids = await FormDashboardScenario.AddAsync(factory, definitionId,
        [
            new(reader, new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc), [new(amount, Number: 1m), new(quantity, Number: 1m), new(currency, Choice: pesos.ChoiceId)]),
            new(reader, new DateTime(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc), [new(amount, Number: 10m), new(quantity, Number: 2m), new(currency, Choice: pesos.ChoiceId)]),
            new(reader, new DateTime(2026, 10, 7, 15, 0, 0, DateTimeKind.Utc), [new(amount, Number: 20m), new(quantity, Number: 3m), new(currency, Choice: dollars.ChoiceId)]),
        ], Ct);
        await FormDashboardScenario.PastTheDecimalRangeAsync(factory, ids[0], amount.FieldId, Ct);
        using var client = FormDashboardScenario.Client(factory, reader);

        using var response = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/dashboard/search", new { filter = October, period = "day" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await FormDashboardContractCapture.WriteAsync(response, "dashboard-overflow-es", Ct);
        var dashboard = await response.ReadJsonAsync();
        var numbers = dashboard.GetProperty("numbers").EnumerateArray().ToArray();
        // The average still fits: its 28 significant digits come back; the total and the maximum do not.
        Assert.Equal((3, JsonValueKind.Null, 26409387504754779197847983455m, 10m, JsonValueKind.Null, true), (numbers[0].GetProperty("count").GetInt32(),
            numbers[0].GetProperty("total").ValueKind, numbers[0].GetProperty("average").GetDecimal(), numbers[0].GetProperty("minimum").GetDecimal(),
            numbers[0].GetProperty("maximum").ValueKind, numbers[0].GetProperty("overflow").GetBoolean()));
        Assert.Equal((3, 6m, 2m, 1m, 3m, false), (numbers[1].GetProperty("count").GetInt32(), numbers[1].GetProperty("total").GetDecimal(),
            numbers[1].GetProperty("average").GetDecimal(), numbers[1].GetProperty("minimum").GetDecimal(), numbers[1].GetProperty("maximum").GetDecimal(),
            numbers[1].GetProperty("overflow").GetBoolean()));
        var buckets = dashboard.GetProperty("series").GetProperty("buckets").EnumerateArray().ToDictionary(bucket => bucket.GetProperty("start").GetString()!);
        Assert.Equal((JsonValueKind.Null, true, 1m), (buckets["2026-10-05"].GetProperty("totals")[0].GetProperty("total").ValueKind,
            buckets["2026-10-05"].GetProperty("totals")[0].GetProperty("overflow").GetBoolean(), buckets["2026-10-05"].GetProperty("totals")[1].GetProperty("total").GetDecimal()));
        Assert.Equal((10m, 20m), (FirstTotal(buckets["2026-10-06"]), FirstTotal(buckets["2026-10-07"])));
        var options = Assert.Single(dashboard.GetProperty("distributions").EnumerateArray()).GetProperty("options").EnumerateArray().ToArray();
        Assert.Equal(("Pesos", JsonValueKind.Null, true, 3m), (options[0].GetProperty("label").GetString(), options[0].GetProperty("totals")[0].GetProperty("total").ValueKind,
            options[0].GetProperty("totals")[0].GetProperty("overflow").GetBoolean(), options[0].GetProperty("totals")[1].GetProperty("total").GetDecimal()));
        Assert.Equal(("Dólares", 20m, 3m), (options[1].GetProperty("label").GetString(), FirstTotal(options[1]), options[1].GetProperty("totals")[1].GetProperty("total").GetDecimal()));
    }

    [Fact]
    public async Task A_series_longer_than_its_cap_keeps_its_latest_buckets()
    {
        var publisher = await FormDashboardScenario.PersonAsync(factory, "Serie Tablero", [BusinessFormsPermissions.RecordsRead], Ct);
        var amount = FormDashboardScenario.Field("decimal", "Monto", "Amount");
        var definitionId = await FormDashboardScenario.FormAsync(factory, publisher, [amount], Ct);
        await FormDashboardScenario.AddAsync(factory, definitionId, [new(publisher, new DateTime(2024, 1, 10, 15, 0, 0, DateTimeKind.Utc), [new(amount, Number: 5m)]),
            new(publisher, new DateTime(2025, 12, 31, 15, 0, 0, DateTimeKind.Utc), [new(amount, Number: 7m)])], Ct);
        using var reader = FormDashboardScenario.Client(factory, publisher);

        using var response = await reader.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/dashboard/search",
            new { filter = new { receivedFromUtc = "2024-01-01T03:00:00Z", receivedToUtc = "2026-01-01T03:00:00Z" }, period = "day" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var series = (await response.ReadJsonAsync()).GetProperty("series");
        var buckets = series.GetProperty("buckets").EnumerateArray().ToArray();
        Assert.True(series.GetProperty("truncated").GetBoolean());
        Assert.Equal(400, buckets.Length);
        Assert.Equal(("2024-11-27", "2025-12-31"), (buckets[0].GetProperty("start").GetString(), buckets[^1].GetProperty("start").GetString()));
        Assert.Equal((1, 7m), (buckets[^1].GetProperty("count").GetInt32(), FirstTotal(buckets[^1])));
        Assert.Equal(1, buckets.Sum(bucket => bucket.GetProperty("count").GetInt32()));
    }

    private static Dictionary<string, int> Counts(IEnumerable<JsonElement> buckets) =>
        buckets.ToDictionary(bucket => bucket.GetProperty("start").GetString()!, bucket => bucket.GetProperty("count").GetInt32());

    // The total of the first number: of a bucket, an option or «Otros».
    private static decimal FirstTotal(JsonElement counted) => counted.GetProperty("totals")[0].GetProperty("total").GetDecimal();

    private static List<(string Label, int Count)> Options(JsonElement counted) =>
        [.. counted.GetProperty("options").EnumerateArray().Select(option => (option.GetProperty("label").GetString()!, option.GetProperty("count").GetInt32()))];

    private static List<(string Label, int Count, decimal Total)> OptionTotals(JsonElement counted) =>
        [.. counted.GetProperty("options").EnumerateArray().Select(option => (option.GetProperty("label").GetString()!, option.GetProperty("count").GetInt32(), FirstTotal(option)))];

    /// <summary>
    /// «Pagos»: a number, a currency, a yes-or-no, a bank from a catalog and the date it was paid. In September (the month before
    /// October) two responses; in October, local time, six, with a Sunday at 23:30 and the 31st at 23:59 (already November in UTC);
    /// in November one, and a voided one in October that only the «Voided» state takes. Whoever reads has the zone it is given.
    /// </summary>
    private async Task<Payments> PaymentsAsync(string readerZone = SystemSettings.InitialTimeZoneId)
    {
        var reader = await FormDashboardScenario.PersonAsync(factory, "Lectura Tablero", [BusinessFormsPermissions.RecordsRead], Ct, readerZone);
        var ana = await FormDashboardScenario.PersonAsync(factory, "Ana Tablero", [BusinessFormsPermissions.RecordsCreate], Ct);
        var bruno = await FormDashboardScenario.PersonAsync(factory, "Bruno Tablero", [BusinessFormsPermissions.RecordsCreate], Ct);
        var catalogId = await FormDashboardScenario.CatalogAsync(factory, Ct);
        var (pesos, dollars) = (FormDashboardScenario.Choice("Pesos", "Pesos"), FormDashboardScenario.Choice("Dólares", "Dollars"));
        var amount = FormDashboardScenario.Field("decimal", "Monto", "Amount");
        var currency = FormDashboardScenario.Field("choice", "Moneda", "Currency", [pesos, dollars]);
        var paid = FormDashboardScenario.Field("boolean", "Pagado", "Paid");
        var bank = FormDashboardScenario.Field("choice", "Banco", "Bank", catalog: catalogId);
        var paidOn = FormDashboardScenario.Field("date", "Fecha de pago", "Paid on");
        var definitionId = await FormDashboardScenario.FormAsync(factory, reader, [amount, currency, paid, bank, paidOn], Ct);
        var (galicia, nacion, mercado) = (new FormDashboardItem(catalogId, Guid.NewGuid(), "Banco Galicia", "Galicia bank"),
            new FormDashboardItem(catalogId, Guid.NewGuid(), "Banco Nación", "Nación bank"), new FormDashboardItem(catalogId, Guid.NewGuid(), "Mercado Pago", "Mercado Pago"));
        FormDashboardRecord Record(Guid actor, string atUtc, decimal? total, CanonicalFormChoice? money, bool? flag, FormDashboardItem? item, string? day,
            FormRecordState state = FormRecordState.Received) =>
            new(actor, DateTime.Parse(atUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                [.. new FormDashboardAnswer?[] { total is { } number ? new(amount, Number: number) : null, money is { } choice ? new(currency, Choice: choice.ChoiceId) : null,
                    flag is { } value ? new(paid, Flag: value) : null, item is { } chosen ? new(bank, Item: chosen) : null,
                    day is { } date ? new(paidOn, Date: DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)) : null }.OfType<FormDashboardAnswer>()], state);
        await FormDashboardScenario.AddAsync(factory, definitionId,
        [
            Record(ana, "2026-09-10T15:00:00Z", 100m, pesos, true, galicia, "2026-09-05"),
            Record(bruno, "2026-09-20T15:00:00Z", 300m, pesos, false, nacion, "2026-09-18"),
            Record(ana, "2026-10-01T03:00:00Z", 1000m, pesos, true, galicia, "2026-09-28"),
            Record(ana, "2026-10-01T15:00:00Z", 500m, dollars, true, galicia, "2026-10-01"),
            Record(bruno, "2026-10-12T02:30:00Z", 250m, pesos, false, nacion, "2026-10-10"),
            Record(bruno, "2026-10-20T12:00:00Z", 750m, pesos, null, mercado, "2026-10-20", FormRecordState.Approved),
            Record(ana, "2026-10-31T23:00:00Z", null, null, true, null, null, FormRecordState.PendingApproval),
            Record(bruno, "2026-11-01T02:59:00Z", 50m, pesos, null, null, "2026-10-31"),
            Record(bruno, "2026-11-01T03:00:00Z", 10m, pesos, null, null, "2026-11-01"),
            Record(ana, "2026-10-15T12:00:00Z", 999m, pesos, true, galicia, "2026-10-15", FormRecordState.Voided),
        ], Ct);
        return new(definitionId, FormDashboardScenario.Client(factory, reader), ana, bruno, paid, paidOn, catalogId);
    }

    private sealed record Payments(Guid Id, HttpClient Reader, Guid Ana, Guid Bruno, CanonicalFormField Paid, CanonicalFormField PaidOn, Guid CatalogId);
}
