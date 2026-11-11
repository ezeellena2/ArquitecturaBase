using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>
/// Calculations (CA1-A) with PostgreSQL: the draft seals the operands named by their local ids, the entry describes how each
/// calculation is computed, the server computes the amount of each item and the total when saving, and an answer sent for a
/// calculation is rejected.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class FormCalculationHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Quantity = "0196f2a0-0000-7000-8000-0000000000a1";
    private const string Price = "0196f2a0-0000-7000-8000-0000000000a2";
    private const string Amount = "0196f2a0-0000-7000-8000-0000000000a3";

    [Fact]
    public async Task The_server_computes_the_amount_of_each_item_and_the_total_from_the_app()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var saved = await scenario.SaveDraftAsync(payload =>
        {
            var fields = payload["draft"]!["fields"]!.AsArray();
            fields.Add(new JsonObject { ["localId"] = "items", ["type"] = "group", ["required"] = true,
                ["label"] = new JsonObject { ["es"] = "Artículos", ["en"] = "Items" }, ["rows"] = new JsonObject { ["minRows"] = 1, ["maxRows"] = 3 } });
            fields.Add(Number(Quantity, "Cantidad", "Quantity", repeated: true));
            fields.Add(Number(Price, "Precio unitario", "Unit price", repeated: true));
            fields.Add(Number(Amount, "Importe", "Amount", repeated: true, ("multiply", [Quantity, Price])));
            fields.Add(Number("total", "Total", "Total", repeated: false, ("sum", [Amount])));
        }, Ct);
        var fields = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields");
        Guid Id(int index) => fields[index].GetProperty("fieldId").GetGuid();
        var (groupId, quantityId, priceId, amountId, totalId) = (Id(1), Id(2), Id(3), Id(4), Id(5));
        Assert.Equal([quantityId, priceId], fields[4].GetProperty("calculation").GetProperty("operands").EnumerateArray().Select(value => value.GetGuid()));
        Assert.Equal(amountId, fields[5].GetProperty("calculation").GetProperty("operands")[0].GetGuid());
        Assert.False(fields[1].TryGetProperty("calculation", out _));

        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        using var client = scenario.Client("es", scenario.FirstId);
        using var entry = await client.GetAsync($"/api/forms/entries/{scenario.DefinitionId}", Ct);
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        await FormEntryContractCapture.WriteAsync(entry, "entry-calculation-es", Ct);
        var questions = (await entry.ReadJsonAsync()).GetProperty("questions");
        Assert.Equal("multiply", questions[4].GetProperty("calculation").GetProperty("operation").GetString());
        Assert.Equal("sum", questions[5].GetProperty("calculation").GetProperty("operation").GetString());
        Assert.False(questions[2].TryGetProperty("calculation", out _));

        var supplier = new { fieldId = scenario.QuestionId, value = scenario.FirstItemId.ToString("D") };
        object Items(params (string Quantity, string Price)[] items) => new
        {
            fieldId = groupId,
            rows = items.Select(item => new object[] { new { fieldId = quantityId, value = item.Quantity }, new { fieldId = priceId, value = item.Price } }).ToArray(),
        };
        using var sent = await client.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(Guid.NewGuid(), versionId, supplier, Items(("12", "850"), ("1", "5100.005"))), Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var recordId = (await sent.ReadJsonAsync()).GetProperty("recordId").GetGuid();
        var values = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormRecordValue>()
            .Where(value => value.RecordId == recordId && (value.FieldId == amountId || value.FieldId == totalId))
            .OrderBy(value => value.FieldId == totalId).ThenBy(value => value.Row).Select(value => value.DecimalValue!.Value).ToArrayAsync(Ct));
        Assert.Equal([10200.00m, 5100.00m, 15300.00m], values);

        using var forged = await client.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(Guid.NewGuid(), versionId, supplier, Items(("1", "1")), new { fieldId = totalId, value = "999" }), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
    }

    private static JsonObject Number(string localId, string es, string en, bool repeated, (string Operation, string[] Operands)? calculation = null)
    {
        var field = new JsonObject { ["localId"] = localId, ["type"] = "decimal", ["required"] = false,
            ["label"] = new JsonObject { ["es"] = es, ["en"] = en } };
        if (repeated) field["repeated"] = true;
        if (calculation is { } value)
            field["calculation"] = new JsonObject { ["operation"] = value.Operation, ["operands"] = new JsonArray(value.Operands.Select(operand => (JsonNode)operand).ToArray()) };
        return field;
    }

    private static MultipartFormDataContent Send(Guid requestId, Guid versionId, params object[] answers) => new()
    {
        { new StringContent(requestId.ToString("D")), "requestId" },
        { new StringContent(versionId.ToString("D")), "versionId" },
        { new StringContent(JsonSerializer.Serialize(answers, JsonSerializerOptions.Web)), "answers" },
    };
}
