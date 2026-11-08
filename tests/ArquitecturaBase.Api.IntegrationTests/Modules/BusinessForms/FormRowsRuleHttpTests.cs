using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task The_rule_options_bring_the_block_with_its_questions_behind_it_and_the_choices_of_their_catalog(string culture)
    {
        using var reader = await RowsReaderAsync(culture);
        var rows = await SeedRowsReadAsync(reader);

        using var options = await reader.GetAsync($"/api/forms/definitions/{rows.DefinitionId}/rules/options", Ct);

        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        await FormRuleContractCapture.WriteAsync(options, "rows-options-" + culture, Ct);
        var body = await options.ReadJsonAsync();
        var questions = body.GetProperty("questions").EnumerateArray().ToArray();
        Assert.Equal(new[] { rows.Supplier, rows.Group, rows.Item, rows.Quantity, rows.Note }, questions.Select(question => question.GetProperty("fieldId").GetGuid()));
        Assert.Equal("group", questions[1].GetProperty("type").GetString());
        Assert.Equal(3, questions[1].GetProperty("rows").GetProperty("maxRows").GetInt32());
        Assert.All(questions[2..], question => Assert.True(question.GetProperty("repeated").GetBoolean()));
        Assert.False(questions[0].TryGetProperty("repeated", out _));
        Assert.Equal(culture == "en" ? ["Seed", "Fertilizer"] : ["Semilla", "Fertilizante"], body.GetProperty("conditionChoices")
            .GetProperty(rows.Item.ToString("D")).EnumerateArray().Select(choice => choice.GetProperty("label").GetString()));
    }

    [Fact]
    public async Task A_rule_on_the_block_notifies_with_one_item_per_line_only_when_one_same_item_meets_its_conditions()
    {
        using var reader = await RowsReaderAsync("es");
        var rows = await SeedRowsReadAsync(reader);
        var route = $"/api/forms/definitions/{rows.DefinitionId}/rules";
        static object Condition(Guid fieldId, string operation, params string[]? values) => new { fieldId, @operator = operation, values };
        static object Rules(object[] conditions, string text) => new
        {
            expectedRevision = 0,
            rules = new[]
            {
                new
                {
                    id = Guid.NewGuid(), @event = "Received", conditions,
                    actions = new[] { new { id = Guid.NewGuid(), kind = "NotifyAuthor", text = new { es = text }, includePdf = false } },
                },
            },
        };
        // «Algún ítem con Artículo es Semilla y Cantidad desde 200»: both in one same item.
        var sameItem = new[] { Condition(rows.Item, "in", rows.Seed.ToString("D")), Condition(rows.Quantity, "gte", "200") };
        var block = $"Llegó: {{{rows.Group:D}}}";

        using var onTheBlock = await reader.PostAsJsonAsync(route, Rules([Condition(rows.Group, "answered", null)], block), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, onTheBlock.StatusCode);
        Assert.True((await onTheBlock.ReadJsonAsync()).GetProperty("errors").TryGetProperty("rules[0].conditions", out _));
        using var aQuestionOfTheBlock = await reader.PostAsJsonAsync(route, Rules(sameItem, $"Llegó: {{{rows.Quantity:D}}}"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, aQuestionOfTheBlock.StatusCode);
        var invalid = await aQuestionOfTheBlock.ReadJsonAsync();
        Assert.Equal(FormRuleErrors.PlaceholderInvalid.Code, invalid.GetProperty("code").GetString());
        Assert.True(invalid.GetProperty("errors").TryGetProperty("rules[0].actions[0].text.es", out _));

        using var saved = await reader.PostAsJsonAsync(route, Rules(sameItem, block), Ct);

        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        await FormRuleContractCapture.WriteAsync(saved, "rows-saved-es", Ct);
        var records = (await CaptureRowsAsync(reader, rows, [new("seed", "200.5", "Sin daños"), new("seed", "100"), new("fertilizer", "7")],
            [new("fertilizer", "200.5"), new("seed", "5", "Con daños")])).ToArray();
        var runs = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>().Where(run => records.Contains(run.RecordId))
            .Select(run => new { run.RecordId, run.State, run.SnapshotJson }).ToArrayAsync(Ct));
        var notified = Assert.Single(runs, run => run.RecordId == records[0]);
        Assert.Equal(FormActionRunState.Waiting, notified.State);
        Assert.Equal("Llegó: \n1. Semilla · 200,5 · Sin daños\n2. Semilla · 100\n3. Fertilizante · 7",
            Assert.Single(FormDraftPolicy.Read<FormActionSnapshot>(notified.SnapshotJson).Recipients).Body);
        // The second response has the seed and 200.5 in different items.
        Assert.Equal(FormActionRunState.NotApplicable, Assert.Single(runs, run => run.RecordId == records[1]).State);
    }
}
