using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
// DOC7-A: what the dashboard counts, from the types of the questions, and how it shows what SQL added up.
public sealed class FormDashboardPolicyTests
{
    private static readonly CanonicalFormChoice Pesos = new(Guid.NewGuid(), Text("Pesos", "Pesos"));
    private static readonly CanonicalFormChoice Dollars = new(Guid.NewGuid(), Text("Dólares", "Dollars"));

    [Fact]
    public void The_plan_counts_the_numbers_the_choices_and_the_dates_outside_a_block_with_the_sheet_labels()
    {
        var amount = Field("decimal", "Monto", "Amount");
        var currency = Field("choice", "Moneda", "Currency") with { Options = [Pesos, Dollars] };
        var several = Field("multiChoice", "Etiquetas", "Tags") with { Options = [new(Guid.NewGuid(), Text("Urgente", "Urgent"))] };
        var paid = Field("boolean", "Pagado", "Paid");
        var catalog = Guid.NewGuid();
        var client = Field("choice", "Cliente", "Client") with { Source = new(catalog) };
        var paidOn = Field("date", "Fecha de pago", "Payment date");
        var block = Field("group", "Artículos", "Items") with { Rows = new FormRowSettings(1, 5) };
        var quantity = Field("decimal", "Cantidad", "Quantity") with { Repeated = true };
        var item = Field("choice", "Artículo", "Item") with { Repeated = true, Options = [new(Guid.NewGuid(), Text("Semilla", "Seed"))] };
        var delivered = Field("date", "Entrega", "Delivery") with { Repeated = true };
        var definition = Definition(amount, currency, several, paid, client, paidOn, block, quantity, item, delivered, Field("location", "Lugar", "Place"),
            Field("shortText", "Nota", "Note"));

        var plan = FormDashboardPolicy.Plan(definition, "en", new Dictionary<Guid, Guid>());

        var number = Assert.Single(plan.Numbers);
        Assert.Equal((amount.FieldId, "Amount", (Guid?)null), (number.FieldId, number.Label, number.CurrencyFieldId));
        Assert.Equal([new FormDashboardMeasure(amount.FieldId)], plan.Measures);
        Assert.Equal([currency.FieldId, several.FieldId, paid.FieldId, client.FieldId], plan.Questions.Select(question => question.FieldId));
        Assert.Equal(["choice", "multiChoice", "boolean", "choice"], plan.Questions.Select(question => question.Type));
        Assert.Equal(["Currency", "Tags", "Paid", "Client"], plan.Questions.Select(question => question.Label));
        Assert.Equal([null, null, null, catalog], plan.Questions.Select(question => question.CatalogId));
        Assert.Equal([(paidOn.FieldId, "Payment date")], plan.Dates.Select(date => (date.FieldId, date.Label)));
    }

    [Fact]
    public void The_fixed_options_carry_their_labels_to_break_a_tie_at_the_cut()
    {
        var currency = Field("choice", "Moneda", "Currency") with { Options = [Pesos, Dollars] };
        var plan = FormDashboardPolicy.Plan(Definition(currency, Field("boolean", "Pagado", "Paid"), Field("choice", "Cliente", "Client") with { Source = new(Guid.NewGuid()) }),
            "en", new Dictionary<Guid, Guid>());

        Assert.Equal([new FormDashboardChoiceLabel(currency.FieldId, Pesos.ChoiceId.ToString(), "Pesos"), new FormDashboardChoiceLabel(currency.FieldId, Dollars.ChoiceId.ToString(), "Dollars")],
            FormDashboardPolicy.ChoiceLabels(plan));
    }

    [Fact]
    public void A_declared_currency_goes_with_its_number_only_when_it_is_a_single_choice_outside_a_block()
    {
        var amount = Field("decimal", "Monto", "Amount");
        var fee = Field("decimal", "Comisión", "Fee");
        var tax = Field("decimal", "Impuesto", "Tax");
        var currency = Field("choice", "Moneda", "Currency") with { Options = [Pesos, Dollars] };
        var tags = Field("multiChoice", "Etiquetas", "Tags") with { Options = [Pesos] };
        var definition = Definition(amount, fee, tax, currency, tags);

        var plan = FormDashboardPolicy.Plan(definition, "es", new Dictionary<Guid, Guid>
        {
            [amount.FieldId] = currency.FieldId, [fee.FieldId] = tags.FieldId, [tax.FieldId] = Guid.NewGuid(),
        });

        Assert.Equal([new FormDashboardMeasure(amount.FieldId, currency.FieldId), new FormDashboardMeasure(fee.FieldId), new FormDashboardMeasure(tax.FieldId)],
            plan.Measures);
    }

    [Fact]
    public void Numbers_by_currency_carry_its_label_and_compare_with_the_previous_range()
    {
        var amount = Field("decimal", "Monto", "Amount");
        var currency = Field("choice", "Moneda", "Currency") with { Options = [Pesos, Dollars] };
        var plan = FormDashboardPolicy.Plan(Definition(amount, currency), "es", new Dictionary<Guid, Guid> { [amount.FieldId] = currency.FieldId });
        var totals = Totals(5, Measure(amount.FieldId, Dollars.ChoiceId, 1, 1200m), Measure(amount.FieldId, Pesos.ChoiceId, 3, 300m, 100m, 50m, 150m),
            Measure(amount.FieldId, null, 1, 7m));
        var previous = Totals(2, Measure(amount.FieldId, Pesos.ChoiceId, 2, 150m, 75m, 50m, 100m));

        var numbers = FormDashboardPolicy.Numbers(plan, totals, previous, []);

        Assert.Equal([Pesos.ChoiceId, Dollars.ChoiceId, null], numbers.Select(number => number.CurrencyId));
        Assert.Equal(["Pesos", "Dólares", null], numbers.Select(number => number.CurrencyLabel));
        Assert.All(numbers, number => Assert.Equal((amount.FieldId, "Monto", false), (number.FieldId, number.Label, number.Overflow)));
        Assert.Equal((3, (decimal?)300m, (decimal?)100m, (decimal?)50m, (decimal?)150m), (numbers[0].Count, numbers[0].Total, numbers[0].Average, numbers[0].Minimum, numbers[0].Maximum));
        Assert.Equal(((decimal?)150m, (decimal?)75m), (numbers[0].PreviousTotal, numbers[0].PreviousAverage));
        Assert.Equal(((decimal?)0m, (decimal?)null), (numbers[1].PreviousTotal, numbers[1].PreviousAverage));
    }

    [Fact]
    public void A_number_without_answers_shows_zero_and_without_a_previous_range_nothing_to_compare()
    {
        var amount = Field("decimal", "Monto", "Amount");
        var plan = FormDashboardPolicy.Plan(Definition(amount), "es", new Dictionary<Guid, Guid>());

        var number = Assert.Single(FormDashboardPolicy.Numbers(plan, Totals(0), null, []));

        Assert.Equal(((Guid?)null, (string?)null), (number.CurrencyId, number.CurrencyLabel));
        Assert.Equal((0, (decimal?)0m, (decimal?)null, (decimal?)null, (decimal?)null, (decimal?)null, (decimal?)null, false),
            (number.Count, number.Total, number.Average, number.Minimum, number.Maximum, number.PreviousTotal, number.PreviousAverage, number.Overflow));
    }

    [Fact]
    public void What_does_not_fit_in_a_decimal_comes_null_and_marked_without_stopping_the_rest()
    {
        var amount = Field("decimal", "Monto", "Amount");
        var plan = FormDashboardPolicy.Plan(Definition(amount), "es", new Dictionary<Guid, Guid>());
        var huge = new FormDashboardMeasureRow(amount.FieldId, null, 2, null, decimal.MaxValue / 2, 1m, decimal.MaxValue, true);

        var number = Assert.Single(FormDashboardPolicy.Numbers(plan, Totals(2, huge), Totals(1, new FormDashboardMeasureRow(amount.FieldId, null, 1, null, null, null, null, true)), []));
        var series = FormDashboardPolicy.Series(new([new DateOnly(2026, 10, 1)], false),
            [new FormDashboardBucketRow(new DateOnly(2026, 10, 1), 2, [new(amount.FieldId, null, null, true)])], [number]);

        Assert.Equal(((decimal?)null, decimal.MaxValue / 2, (decimal?)null, true), (number.Total, number.Average, number.PreviousTotal, number.Overflow));
        Assert.Equal(new FormDashboardTotalResponse(amount.FieldId, null, null, true), Assert.Single(Assert.Single(series.Buckets).Totals));
    }

    [Fact]
    public void The_series_fills_quiet_buckets_with_zero_for_the_count_and_every_number()
    {
        var amount = Field("decimal", "Monto", "Amount");
        var plan = FormDashboardPolicy.Plan(Definition(amount), "es", new Dictionary<Guid, Guid>());
        var numbers = FormDashboardPolicy.Numbers(plan, Totals(3, Measure(amount.FieldId, null, 3, 60m, 20m, 10m, 30m)), null, []);
        var window = new FormDashboardWindow([new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 3)], true);

        var series = FormDashboardPolicy.Series(window, [new FormDashboardBucketRow(new DateOnly(2026, 10, 1), 2, [new(amount.FieldId, null, 40m, false)]),
            new FormDashboardBucketRow(new DateOnly(2026, 10, 3), 1, [new(amount.FieldId, null, 20m, false)])], numbers);

        Assert.True(series.Truncated);
        Assert.Equal([2, 0, 1], series.Buckets.Select(bucket => bucket.Count));
        Assert.Equal([40m, 0m, 20m], series.Buckets.Select(bucket => Assert.Single(bucket.Totals).Total));
        Assert.All(series.Buckets, bucket => Assert.Equal((amount.FieldId, (Guid?)null, false), (bucket.Totals[0].FieldId, bucket.Totals[0].CurrencyId, bucket.Totals[0].Overflow)));
    }

    [Fact]
    public void Each_date_question_has_its_own_series_in_zero_where_nothing_fell()
    {
        var amount = Field("decimal", "Monto", "Amount");
        var paidOn = Field("date", "Fecha de pago", "Payment date");
        var dueOn = Field("date", "Vencimiento", "Due date");
        var plan = FormDashboardPolicy.Plan(Definition(amount, paidOn, dueOn), "es", new Dictionary<Guid, Guid>());
        var numbers = FormDashboardPolicy.Numbers(plan, Totals(2, Measure(amount.FieldId, null, 2, 30m, 15m, 10m, 20m)), null, []);
        var windows = new Dictionary<Guid, FormDashboardWindow> { [paidOn.FieldId] = new([new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)], false) };

        var series = FormDashboardPolicy.DateSeries(plan, windows, [new FormDashboardDateBucketRow(paidOn.FieldId, new DateOnly(2026, 10, 1), 2, [new(amount.FieldId, null, 30m, false)])], numbers);

        Assert.Equal([("Fecha de pago", 2), ("Vencimiento", 0)], series.Select(date => (date.Label, date.Buckets.Count)));
        Assert.Equal([(0, 0m), (2, 30m)], series[0].Buckets.Select(bucket => (bucket.Count, Assert.Single(bucket.Totals).Total!.Value)));
        Assert.False(series[1].Truncated);
    }

    [Fact]
    public void Distributions_name_the_options_like_the_counts_of_the_sheet_with_their_totals_and_keep_the_rest_apart()
    {
        var amount = Field("decimal", "Monto", "Amount");
        var currency = Field("choice", "Moneda", "Currency") with { Options = [Pesos, Dollars] };
        var paid = Field("boolean", "Pagado", "Paid");
        var catalog = Guid.NewGuid();
        var client = Field("choice", "Cliente", "Client") with { Source = new(catalog) };
        var definition = Definition(amount, currency, paid, client) with { CatalogNames = new Dictionary<Guid, IReadOnlyDictionary<string, string>> { [catalog] = Text("Clientes", "Clients") } };
        var plan = FormDashboardPolicy.Plan(definition, "en", new Dictionary<Guid, Guid>());
        var numbers = FormDashboardPolicy.Numbers(plan, Totals(9, Measure(amount.FieldId, null, 9, 900m)), null, []);
        var (first, second, unnamed) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var rows = new[]
        {
            new FormDashboardDistributionRow(currency.FieldId, [new(Dollars.ChoiceId.ToString(), 2, [new(amount.FieldId, null, 50m, false)]), new(Pesos.ChoiceId.ToString(), 2, [])], null),
            new FormDashboardDistributionRow(paid.FieldId, [new("false", 1, []), new("true", 3, [])], null),
            new FormDashboardDistributionRow(client.FieldId, [new(first.ToString(), 5, []), new(second.ToString(), 1, []), new(unnamed.ToString(), 1, [])],
                new(9, 7, [new(amount.FieldId, null, 700m, false)])),
        };

        var distributions = FormDashboardPolicy.Distributions(plan, definition, rows, [new(client.FieldId, first, "Supermercado La Esquina"), new(client.FieldId, second, "Hotel Plaza")],
            numbers, "en");

        Assert.Equal(["Currency", "Paid", "Client"], distributions.Select(distribution => distribution.Label));
        Assert.Equal(["Dollars", "Pesos"], distributions[0].Options.Select(option => option.Label));
        Assert.Equal([50m, 0m], distributions[0].Options.Select(option => Assert.Single(option.Totals).Total!.Value));
        Assert.Equal([("true", "true", 3), ("false", "false", 1)], distributions[1].Options.Select(option => (option.Id, option.Label, option.Count)));
        Assert.Equal([("Supermercado La Esquina", 5), ("", 1), ("Hotel Plaza", 1)], distributions[2].Options.Select(option => (option.Label, option.Count)));
        Assert.Null(distributions[0].Others);
        Assert.Equal((9, 7, 700m), (distributions[2].Others!.Count, distributions[2].Others!.OptionCount, Assert.Single(distributions[2].Others!.Totals).Total!.Value));
        Assert.Equal([null, null, new FormDashboardCatalogResponse(catalog, "Clients")], distributions.Select(distribution => distribution.Catalog));
    }

    [Fact]
    public void A_question_without_answers_has_an_empty_distribution()
    {
        var currency = Field("choice", "Moneda", "Currency") with { Options = [Pesos, Dollars] };
        var definition = Definition(currency);
        var plan = FormDashboardPolicy.Plan(definition, "es", new Dictionary<Guid, Guid>());

        var distribution = Assert.Single(FormDashboardPolicy.Distributions(plan, definition, [], [], [], "es"));

        Assert.Empty(distribution.Options);
        Assert.Null(distribution.Others);
    }

    [Fact]
    public void Catalog_labels_are_read_for_the_named_options_and_the_currencies_of_the_numbers()
    {
        var amount = Field("decimal", "Monto", "Amount");
        var currency = Field("choice", "Moneda", "Currency") with { Source = new(Guid.NewGuid()) };
        var client = Field("choice", "Cliente", "Client") with { Source = new(Guid.NewGuid()) };
        var paid = Field("boolean", "Pagado", "Paid");
        var plan = FormDashboardPolicy.Plan(Definition(amount, currency, client, paid), "es", new Dictionary<Guid, Guid> { [amount.FieldId] = currency.FieldId });
        var (named, peso, dollar) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var items = FormDashboardPolicy.CatalogItems(plan, [new(client.FieldId, [new(named.ToString(), 3, [])], null), new(paid.FieldId, [new("true", 1, [])], null),
            new(currency.FieldId, [new(peso.ToString(), 2, [])], null)], Totals(3, Measure(amount.FieldId, dollar, 1, 1m), Measure(amount.FieldId, null, 1, 1m)));

        Assert.Equal(new[] { client.FieldId, currency.FieldId }.Order(), items.FieldIds.Order());
        Assert.Equal(new[] { named, peso, dollar }.Order(), items.ItemIds.Order());
    }

    [Fact]
    public void Actors_are_named_by_count_and_states_follow_their_count()
    {
        var (juan, marta, deleted) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var actors = FormDashboardPolicy.Actors(new FormDashboardActorsRow([new(marta, "Marta Ruiz", 35), new(juan, "Juan Gómez", 64), new(deleted, null, 35)], 24, 3), "es");
        var states = FormDashboardPolicy.States([new("Rejected", 8), new("PendingApproval", 14), new("Approved", 101), new("Received", 14)]);

        Assert.Equal([(juan.ToString(), "Juan Gómez", 64), (deleted.ToString(), "", 35), (marta.ToString(), "Marta Ruiz", 35)],
            actors.Options.Select(option => (option.Id, option.Label, option.Count)));
        Assert.Equal(new FormDashboardActorOthersResponse(24, 3), actors.Others);
        Assert.Null(FormDashboardPolicy.Actors(new FormDashboardActorsRow([], 0, 0), "es").Others);
        Assert.Equal(["Approved", "Received", "PendingApproval", "Rejected"], states.Select(state => state.State));
    }

    [Fact]
    public void What_carries_answer_values_does_not_show_them_in_its_text()
    {
        var id = Guid.NewGuid();
        object[] values =
        [
            new FormDashboardMeasureRow(id, null, 1, 12345m, 12345m, 12345m, 12345m, false), Totals(1, new FormDashboardMeasureRow(id, null, 1, 12345m, 12345m, 12345m, 12345m, false)),
            new FormDashboardTotalRow(id, null, 12345m, false), new FormDashboardTotalResponse(id, null, 12345m, false),
            new FormDashboardBucketResponse(new DateOnly(2026, 10, 1), 1, [new(id, null, 12345m, false)]),
            new FormDashboardBucketRow(new DateOnly(2026, 10, 1), 1, [new(id, null, 12345m, false)]),
            new FormDashboardDateBucketRow(id, new DateOnly(2026, 10, 1), 1, [new(id, null, 12345m, false)]),
            new FormDashboardDateRangeRow(id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)),
            new FormDashboardOptionRow("12345", 1, [new(id, null, 12345m, false)]), new FormDashboardOthersRow(1, 1, [new(id, null, 12345m, false)]),
            new FormDashboardOptionResponse("12345", "12345", 1, [new(id, null, 12345m, false)]), new FormDashboardOthersResponse(1, 1, [new(id, null, 12345m, false)]),
            new FormDashboardDistributionRow(id, [new("12345", 1, [])], null),
            new FormDashboardNumberResponse(id, "12345", null, "12345", 1, 12345m, 12345m, 12345m, 12345m, 12345m, 12345m, false),
        ];

        Assert.All(values, value => Assert.DoesNotContain("12345", value.ToString(), StringComparison.Ordinal));
    }

    private static FormDashboardMeasureRow Measure(Guid fieldId, Guid? currencyId, int count, decimal total, decimal? average = null, decimal? minimum = null,
        decimal? maximum = null) => new(fieldId, currencyId, count, total, average ?? total / count, minimum ?? total, maximum ?? total, false);
    private static FormDashboardTotalsRow Totals(int count, params FormDashboardMeasureRow[] measures) =>
        new(count, count == 0 ? null : new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), count == 0 ? null : new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc), measures, []);
    private static FormResponseDefinitionRow Definition(params CanonicalFormField[] fields)
    {
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, JsonSerializer.Serialize(Text("Formulario", "Form")),
            JsonSerializer.Serialize(new FormVersionSchema(1, fields), JsonSerializerOptions.Web), "es", ["es", "en"]);
        return new(Guid.NewGuid(), "TEST", version.Id, [version]);
    }
    private static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
    private static CanonicalFormField Field(string type, string es, string en)
    {
        var id = Guid.NewGuid();
        return new(id, "field_" + id.ToString("N"), type, Text(es, en), null, false, type == "shortText" ? 80 : null, null);
    }
}
