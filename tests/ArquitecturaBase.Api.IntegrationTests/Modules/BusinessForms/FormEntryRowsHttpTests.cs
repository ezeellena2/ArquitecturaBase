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
/// The block of rows loaded from the app (RG2-A) with PostgreSQL: the entry describes the block and its questions, a send with three
/// items is saved with a value and a reference per item, repeats with the same request, rejects an option of an item in that item's
/// cell, and a correction opens and saves the items. A list of the block that depends on another of the block takes its own item.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class FormEntryRowsHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Items_are_loaded_sent_repeated_rejected_in_their_cell_and_corrected_from_the_app()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var saved = await scenario.SaveDraftAsync(payload =>
        {
            var fields = payload["draft"]!["fields"]!.AsArray();
            fields.Add(new JsonObject { ["localId"] = "items", ["type"] = "group", ["required"] = true,
                ["label"] = new JsonObject { ["es"] = "Artículos", ["en"] = "Items" }, ["rows"] = new JsonObject { ["minRows"] = 1, ["maxRows"] = 3 } });
            fields.Add(new JsonObject { ["localId"] = "product", ["type"] = "choice", ["required"] = true, ["repeated"] = true,
                ["label"] = new JsonObject { ["es"] = "Artículo", ["en"] = "Item" }, ["source"] = scenario.Source() });
            fields.Add(new JsonObject { ["localId"] = "quantity", ["type"] = "decimal", ["required"] = true, ["repeated"] = true,
                ["label"] = new JsonObject { ["es"] = "Cantidad", ["en"] = "Quantity" } });
        }, Ct);
        var fields = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields");
        var (groupId, productId, quantityId) = (fields[1].GetProperty("fieldId").GetGuid(), fields[2].GetProperty("fieldId").GetGuid(),
            fields[3].GetProperty("fieldId").GetGuid());
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        using var client = scenario.Client("es", scenario.FirstId);
        object Items(params (Guid Product, string Quantity)[] items) => new
        {
            fieldId = groupId,
            rows = items.Select(item => new object[] { new { fieldId = productId, value = item.Product.ToString("D") }, new { fieldId = quantityId, value = item.Quantity } }).ToArray(),
        };
        var supplier = Choice(scenario.QuestionId, scenario.FirstItemId);

        using var entry = await client.GetAsync($"/api/forms/entries/{scenario.DefinitionId}", Ct);
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        await FormEntryContractCapture.WriteAsync(entry, "entry-rows-es", Ct);
        var questions = (await entry.ReadJsonAsync()).GetProperty("questions");
        Assert.Equal("group", questions[1].GetProperty("type").GetString());
        Assert.Equal((1, 3), (questions[1].GetProperty("rows").GetProperty("minRows").GetInt32(), questions[1].GetProperty("rows").GetProperty("maxRows").GetInt32()));
        Assert.True(questions[2].GetProperty("repeated").GetBoolean() && questions[3].GetProperty("repeated").GetBoolean());
        Assert.False(questions[0].TryGetProperty("repeated", out _));

        var requestId = Guid.NewGuid();
        using var sent = await client.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(requestId, versionId, supplier, Items((scenario.FirstItemId, "200.5"), (scenario.FirstItemId, "100"), (scenario.FirstItemId, "7"))), Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var record = await sent.ReadJsonAsync();
        var recordId = record.GetProperty("recordId").GetGuid();
        using var replay = await client.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(requestId, versionId, supplier, Items((scenario.FirstItemId, "200.5"), (scenario.FirstItemId, "100"), (scenario.FirstItemId, "7"))), Ct);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(recordId, (await replay.ReadJsonAsync()).GetProperty("recordId").GetGuid());
        var quantities = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormRecordValue>()
            .Where(value => value.RecordId == recordId && value.FieldId == quantityId).OrderBy(value => value.Row)
            .Select(value => new { value.Row, value.DecimalValue }).ToArrayAsync(Ct));
        Assert.Equal([0, 1, 2], quantities.Select(value => value.Row));
        Assert.Equal([200.5m, 100m, 7m], quantities.Select(value => value.DecimalValue!.Value));
        var references = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormRecordReference>()
            .Where(reference => reference.RecordId == recordId && reference.FieldId == productId).Select(reference => reference.Row).OrderBy(row => row).ToArrayAsync(Ct));
        Assert.Equal([0, 1, 2], references);

        // The other zone's supplier is not offered to this person: the error goes to the cell of the second item.
        using var rejected = await client.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(Guid.NewGuid(), versionId, supplier, Items((scenario.FirstItemId, "1"), (scenario.SecondItemId, "2"))), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await FormEntryContractCapture.WriteAsync(rejected, "entry-rows-error-es", Ct);
        var error = await rejected.ReadJsonAsync();
        Assert.Equal(FormRecordErrors.CatalogItemInactive.Code, error.GetProperty("code").GetString());
        Assert.Equal([$"answers.{groupId:D}.1.{productId:D}"], error.GetProperty("errors").EnumerateObject().Select(property => property.Name));
        using var empty = await client.PostAsync($"/api/forms/entries/{scenario.DefinitionId}", Send(Guid.NewGuid(), versionId, supplier, Items()), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(FormRecordErrors.InvalidResponse.Code, (await empty.ReadJsonAsync()).GetProperty("code").GetString());

        using var opened = await client.GetAsync($"/api/forms/entries/{scenario.DefinitionId}/records/{recordId}", Ct);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        await FormEntryContractCapture.WriteAsync(opened, "correction-rows-es", Ct);
        var block = Assert.Single((await opened.ReadJsonAsync()).GetProperty("answers").EnumerateArray(), answer => answer.GetProperty("fieldId").GetGuid() == groupId);
        var rows = block.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(["200.5", "100", "7"], rows.Select(row => row[1].GetProperty("value").GetString()));
        Assert.All(rows, row => Assert.Equal(scenario.FirstItemId.ToString("D"), row[0].GetProperty("value").GetString()));
        using var corrected = await client.PostAsync($"/api/forms/entries/{scenario.DefinitionId}/records/{recordId}",
            Correct(Guid.NewGuid(), versionId, "Faltaba corregir un artículo.", supplier, Items((scenario.FirstItemId, "200.5"), (scenario.FirstItemId, "150"))), Ct);
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var correction = await corrected.ReadJsonAsync();
        Assert.Equal(record.GetProperty("number").GetString(), correction.GetProperty("number").GetString());
        var correctionId = correction.GetProperty("recordId").GetGuid();
        var correctedQuantities = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormRecordValue>()
            .Where(value => value.RecordId == correctionId && value.FieldId == quantityId).OrderBy(value => value.Row)
            .Select(value => value.DecimalValue!.Value).ToArrayAsync(Ct));
        Assert.Equal([200.5m, 150m], correctedQuantities);
    }

    [Fact]
    public async Task A_supplier_list_of_the_block_follows_the_zone_of_its_own_item()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var zoneLocal = Guid.NewGuid().ToString("D");
        var saved = await scenario.SaveDraftAsync(payload => payload["draft"]!["fields"] = new JsonArray(
            new JsonObject { ["localId"] = "items", ["type"] = "group", ["required"] = true,
                ["label"] = new JsonObject { ["es"] = "Pedidos", ["en"] = "Orders" }, ["rows"] = new JsonObject { ["minRows"] = 1, ["maxRows"] = 3 } },
            new JsonObject { ["localId"] = zoneLocal, ["type"] = "choice", ["required"] = true, ["repeated"] = true,
                ["label"] = new JsonObject { ["es"] = "Zona", ["en"] = "Zone" }, ["source"] = new JsonObject { ["catalog"] = scenario.ZoneCatalogId.ToString("D") } },
            new JsonObject { ["localId"] = "supplier-by-zone", ["type"] = "choice", ["required"] = true, ["repeated"] = true,
                ["label"] = new JsonObject { ["es"] = "Proveedor", ["en"] = "Supplier" }, ["source"] = new JsonObject
                {
                    ["catalog"] = scenario.CatalogId.ToString("D"),
                    ["match"] = new JsonArray(new JsonObject { ["fieldId"] = scenario.ZoneFieldId.ToString("D"), ["questionId"] = zoneLocal }),
                } }), Ct);
        var fields = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields");
        var (groupId, zoneId, supplierId) = (fields[0].GetProperty("fieldId").GetGuid(), fields[1].GetProperty("fieldId").GetGuid(),
            fields[2].GetProperty("fieldId").GetGuid());
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        using var client = scenario.Client("es", scenario.FirstId);
        object Items(params (Guid Zone, Guid Supplier)[] items) => new
        {
            fieldId = groupId,
            rows = items.Select(item => new object[] { new { fieldId = zoneId, value = item.Zone.ToString("D") }, new { fieldId = supplierId, value = item.Supplier.ToString("D") } }).ToArray(),
        };

        using var mismatch = await client.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(Guid.NewGuid(), versionId, Items((scenario.CentralId, scenario.FirstItemId), (scenario.SouthId, scenario.FirstItemId))), Ct);
        using var sent = await client.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(Guid.NewGuid(), versionId, Items((scenario.CentralId, scenario.FirstItemId), (scenario.SouthId, scenario.SecondItemId))), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Equal([$"answers.{groupId:D}.1.{supplierId:D}"], (await mismatch.ReadJsonAsync()).GetProperty("errors").EnumerateObject().Select(property => property.Name));
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
    }

    private static object Choice(Guid fieldId, Guid itemId) => new { fieldId, value = itemId.ToString("D") };

    private static MultipartFormDataContent Send(Guid requestId, Guid versionId, params object[] answers) => new()
    {
        { new StringContent(requestId.ToString("D")), "requestId" },
        { new StringContent(versionId.ToString("D")), "versionId" },
        { new StringContent(JsonSerializer.Serialize(answers, JsonSerializerOptions.Web)), "answers" },
    };

    private static MultipartFormDataContent Correct(Guid requestId, Guid versionId, string reason, params object[] answers)
    {
        var content = Send(requestId, versionId, answers);
        content.Add(new StringContent(reason), "reason");
        return content;
    }
}
