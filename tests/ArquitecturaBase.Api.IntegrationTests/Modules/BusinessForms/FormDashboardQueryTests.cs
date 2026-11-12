using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// DOC7-A with PostgreSQL: when the policy says which choice question tells the currency of a number (the form's standard declares
// it, «Total de Monto (pesos)»), the totals, the series and the totals of each option add up each currency apart and never mix them;
// and the dashboard reads in a fixed number of statements, whatever the number of questions.
[Collection(ApiTestGroup.Name)]
public sealed class FormDashboardQueryTests(ApiFactory factory)
{
    private const string Zone = "America/Argentina/Buenos_Aires";
    private static readonly DateTime October = new(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime November = new(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_declared_currency_splits_each_number_its_series_and_its_option_totals_by_the_chosen_option()
    {
        var publisher = await FormDashboardScenario.PersonAsync(factory, "Moneda Tablero", [BusinessFormsPermissions.RecordsRead], Ct);
        var (pesos, dollars) = (FormDashboardScenario.Choice("Pesos", "Pesos"), FormDashboardScenario.Choice("Dólares", "Dollars"));
        var (galicia, nacion) = (FormDashboardScenario.Choice("Galicia", "Galicia"), FormDashboardScenario.Choice("Nación", "Nación"));
        var amount = FormDashboardScenario.Field("decimal", "Monto", "Amount");
        var currency = FormDashboardScenario.Field("choice", "Moneda", "Currency", [pesos, dollars]);
        var bank = FormDashboardScenario.Field("choice", "Banco", "Bank", [galicia, nacion]);
        var definitionId = await FormDashboardScenario.FormAsync(factory, publisher, [amount, currency, bank], Ct);
        await FormDashboardScenario.AddAsync(factory, definitionId,
        [
            new(publisher, new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc), [new(amount, Number: 1000m), new(currency, Choice: pesos.ChoiceId), new(bank, Choice: galicia.ChoiceId)]),
            new(publisher, new DateTime(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc), [new(amount, Number: 3000m), new(currency, Choice: pesos.ChoiceId), new(bank, Choice: nacion.ChoiceId)]),
            new(publisher, new DateTime(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc), [new(amount, Number: 1200m), new(currency, Choice: dollars.ChoiceId), new(bank, Choice: galicia.ChoiceId)]),
            new(publisher, new DateTime(2026, 10, 13, 15, 0, 0, DateTimeKind.Utc), [new(amount, Number: 7m), new(bank, Choice: nacion.ChoiceId)]),
            new(publisher, new DateTime(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc), [new(currency, Choice: dollars.ChoiceId)]),
        ], Ct);

        await factory.ExecuteScopeAsync(async services =>
        {
            var definition = await services.GetRequiredService<IFormResponseReader>().FindDefinitionAsync(definitionId, Ct);
            Assert.NotNull(definition);
            var plan = FormDashboardPolicy.Plan(definition, "es", new Dictionary<Guid, Guid> { [amount.FieldId] = currency.FieldId });
            var reader = services.GetRequiredService<IFormDashboardReader>();
            var filter = new ListFormRecordsRequest { DisplayCulture = "es" };

            var totals = await reader.FindTotalsAsync(definition, filter, plan.Measures, [], Ct);
            var buckets = await reader.ListBucketsAsync(definition, filter, new("week", Zone, new DateOnly(2026, 10, 5)), plan.Measures, Ct);
            var rows = await reader.ListDistributionsAsync(definition, filter, plan.Questions.Select(question => question.FieldId).ToArray(),
                FormDashboardPolicy.ChoiceLabels(plan), plan.Measures, FormDashboardPolicy.Top, Ct);
            var numbers = FormDashboardPolicy.Numbers(plan, totals, null, []);
            var series = FormDashboardPolicy.Series(new([new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12)], false), buckets, numbers);
            var distributions = FormDashboardPolicy.Distributions(plan, definition, rows, [], numbers, "es");

            Assert.Equal(5, totals.Count);
            Assert.Equal([("Pesos", (Guid?)pesos.ChoiceId, 2, (decimal?)4000m, (decimal?)2000m), ("Dólares", dollars.ChoiceId, 1, 1200m, 1200m), (null, null, 1, 7m, 7m)],
                numbers.Select(number => (number.CurrencyLabel, number.CurrencyId, number.Count, number.Total, number.Average)));
            Assert.Equal([2, 3], series.Buckets.Select(bucket => bucket.Count));
            Assert.Equal([4000m, 0m, 0m], series.Buckets[0].Totals.Select(total => total.Total));
            Assert.Equal([0m, 1200m, 7m], series.Buckets[1].Totals.Select(total => total.Total));
            var banks = distributions.Single(distribution => distribution.FieldId == bank.FieldId).Options;
            Assert.Equal([("Galicia", 2), ("Nación", 2)], banks.Select(option => (option.Label, option.Count)));
            Assert.Equal([1000m, 1200m, 0m], banks[0].Totals.Select(total => total.Total));
            Assert.Equal([3000m, 0m, 7m], banks[1].Totals.Select(total => total.Total));
            Assert.All(banks.SelectMany(option => option.Totals), total => Assert.Equal(amount.FieldId, total.FieldId));
        });
    }

    [Fact]
    public async Task The_dashboard_reads_the_same_statements_for_ten_numbers_ten_choices_and_three_dates_as_for_one_of_each()
    {
        var publisher = await FormDashboardScenario.PersonAsync(factory, "Costo Tablero", [BusinessFormsPermissions.RecordsRead], Ct);
        var small = await CostFormAsync(publisher, 1, 1, 1);
        var large = await CostFormAsync(publisher, 10, 10, 3);

        var few = await DashboardCommandsAsync(small);
        var many = await DashboardCommandsAsync(large);

        Assert.Equal(few.Count, many.Count);
        // Two totals (the range and the one before), the received buckets, the date buckets, the distributions and the people.
        var statements = many.Where(command => command.Sql.Contains("WITH matching", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, statements.Length);
        Assert.Equal(6, few.Count(command => command.Sql.Contains("WITH matching", StringComparison.Ordinal)));
        await ExplainAsync(statements.MaxBy(command => command.Sql.Length)!);
    }

    // A form with that many numbers, choices of three options and dates, and forty responses in October with every question answered.
    private async Task<Guid> CostFormAsync(Guid publisher, int numbers, int choices, int dates)
    {
        var numberFields = Enumerable.Range(1, numbers).Select(index => FormDashboardScenario.Field("decimal", $"Número {index}", $"Number {index}")).ToArray();
        var choiceFields = Enumerable.Range(1, choices).Select(index => FormDashboardScenario.Field("choice", $"Opción {index}", $"Choice {index}",
            [FormDashboardScenario.Choice("Uno", "One"), FormDashboardScenario.Choice("Dos", "Two"), FormDashboardScenario.Choice("Tres", "Three")])).ToArray();
        var dateFields = Enumerable.Range(1, dates).Select(index => FormDashboardScenario.Field("date", $"Fecha {index}", $"Date {index}")).ToArray();
        var definitionId = await FormDashboardScenario.FormAsync(factory, publisher, [.. numberFields, .. choiceFields, .. dateFields], Ct);
        await FormDashboardScenario.AddAsync(factory, definitionId, [.. Enumerable.Range(0, 40).Select(response => new FormDashboardRecord(publisher,
            October.AddHours(response * 17), [
                .. numberFields.Select((field, index) => new FormDashboardAnswer(field, Number: response + index + 1m)),
                .. choiceFields.Select(field => new FormDashboardAnswer(field, Choice: field.Options![response % 3].ChoiceId)),
                .. dateFields.Select((field, index) => new FormDashboardAnswer(field, Date: new DateOnly(2026, 9, 1).AddDays(response + index))),
            ]))], Ct);
        return definitionId;
    }

    // The reads of FormDashboardService, in its order, for October by day: each one recorded as PostgreSQL received it.
    private Task<IReadOnlyList<FormResponseRecordedCommand>> DashboardCommandsAsync(Guid definitionId) => factory.ExecuteScopeAsync(async services =>
    {
        var recorder = new FormResponseCommandRecorder();
        await using var db = new TestDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(
            services.GetRequiredService<DbContextOptions<ApplicationDbContext>>()).AddInterceptors(recorder).Options);
        var responses = new FormResponseReader(db, new FormRecordReader(db, services.GetRequiredService<IFormPublicationProtector>()));
        var reader = new FormDashboardReader(db);
        var definition = await responses.FindDefinitionAsync(definitionId, Ct);
        Assert.NotNull(definition);
        var plan = FormDashboardPolicy.Plan(definition, "es", new Dictionary<Guid, Guid>());
        var filter = new ListFormRecordsRequest { DisplayCulture = "es", ReceivedFromUtc = October, ReceivedToUtc = November };
        var totals = await reader.FindTotalsAsync(definition, filter, plan.Measures, plan.Dates.Select(date => date.FieldId).ToArray(), Ct);
        await reader.FindTotalsAsync(definition, filter with { ReceivedFromUtc = new DateTime(2026, 9, 1, 3, 0, 0, DateTimeKind.Utc), ReceivedToUtc = October },
            plan.Measures, [], Ct);
        var buckets = await reader.ListBucketsAsync(definition, filter, new("day", Zone, new DateOnly(2026, 10, 1)), plan.Measures, Ct);
        var dated = await reader.ListDateBucketsAsync(definition, filter, "day",
            totals.Dates.Select(date => new FormDashboardDateWindow(date.FieldId, date.First)).ToArray(), plan.Measures, Ct);
        var distributions = await reader.ListDistributionsAsync(definition, filter, plan.Questions.Select(question => question.FieldId).ToArray(),
            FormDashboardPolicy.ChoiceLabels(plan), plan.Measures, FormDashboardPolicy.Top, Ct);
        var actors = await reader.FindActorsAsync(definition, filter, FormDashboardPolicy.Top, Ct);
        await reader.ListStatesAsync(definition, filter, Ct);
        // Everything was read: every number by its own, every choice and every date.
        Assert.Equal((40, plan.Numbers.Count, plan.Dates.Count), (totals.Count, totals.Measures.Count, totals.Dates.Count));
        Assert.Equal(40, buckets.Sum(bucket => bucket.Count));
        Assert.Equal(40 * plan.Dates.Count, dated.Sum(bucket => bucket.Count));
        Assert.Equal(plan.Questions.Count, distributions.Count);
        Assert.Equal(40, Assert.Single(actors.People).Count);
        return (IReadOnlyList<FormResponseRecordedCommand>)recorder.Commands.ToArray();
    });

    // EXPLAIN (ANALYZE, BUFFERS) of the largest statement against the test database; the plan goes to the capture folder, if any.
    private Task ExplainAsync(FormResponseRecordedCommand statement) => factory.ExecuteDbContextAsync(async db =>
    {
        await db.Database.OpenConnectionAsync(Ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + statement.Sql;
        foreach (var parameter in statement.Parameters) command.Parameters.Add(parameter.Clone());
        var output = (string)(await command.ExecuteScalarAsync(Ct))!;
        using var document = JsonDocument.Parse(output);
        Assert.True(document.RootElement[0].GetProperty("Plan").GetProperty("Actual Rows").GetDouble() > 0);
        var destination = Environment.GetEnvironmentVariable("FORM_DASHBOARD_HTTP_OUTPUT");
        if (!string.IsNullOrWhiteSpace(destination))
        {
            Directory.CreateDirectory(destination);
            await File.WriteAllTextAsync(Path.Combine(destination, "explain.json"), output, Ct);
        }
    });
}
