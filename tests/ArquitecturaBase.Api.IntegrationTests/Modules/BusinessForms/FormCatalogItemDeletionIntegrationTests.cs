using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// Eliminar opciones: PostgreSQL encuentra cada uso (una respuesta, el campo de otra opción, el dato de una persona, el filtro de
// un formulario) y sólo se borra la que nadie usa; las demás cierran el hueco de su posición.
[Collection(ApiTestGroup.Name)]
public sealed class FormCatalogItemDeletionIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_an_option_nothing_uses_is_deleted_and_the_conflict_names_each_use()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        await scenario.PublishForPreparationAsync(factory, Ct);
        await RecordAsync(scenario);
        using var client = scenario.Client();
        var zones = $"/api/forms/catalogs/{scenario.ZoneCatalogId}";
        var added = Zones(1);
        added["items"] = new JsonArray(Item("west", "WEST", "Oeste", "West"), Item("north", "NORTH", "Norte", "North"));
        using var adding = await client.PutAsJsonAsync(zones, added, Ct);
        Assert.Equal(HttpStatusCode.OK, adding.StatusCode);
        var identities = (await adding.ReadJsonAsync()).GetProperty("itemIdentityMap");
        var west = identities.GetProperty("west").GetGuid();
        var north = identities.GetProperty("north").GetGuid();
        // El borrador pasa a filtrar por una zona fija, el Norte, en lugar de la zona de quien lo recibe. Los filtros vuelven
        // ordenados por el id del campo, y los dos campos nacieron en el mismo milisegundo: el de la zona se busca por su id.
        await scenario.SaveDraftAsync(payload =>
        {
            var match = payload["draft"]!["fields"]![0]!["source"]!["match"]!.AsArray();
            var zone = match.Single(filter => filter!["fieldId"]!.GetValue<string>() == scenario.ZoneFieldId.ToString("D"));
            match[match.IndexOf(zone)] = new JsonObject { ["fieldId"] = scenario.ZoneFieldId.ToString("D"), ["value"] = north.ToString("D") };
        }, Ct);

        using var chosen = await client.PutAsJsonAsync($"/api/forms/catalogs/{scenario.CatalogId}", Deleting(scenario.CatalogEdit(2), scenario.FirstItemId), Ct);
        Assert.Equal(["records"], await UsesAsync(chosen, "deleteItemIds[0]"));
        using var held = await client.PutAsJsonAsync(zones, Deleting(Zones(2), west, scenario.CentralId), Ct);
        Assert.Equal(["options", "people"], await UsesAsync(held, "deleteItemIds[1]"));
        using var filtered = await client.PutAsJsonAsync(zones, Deleting(Zones(2), north), Ct);
        Assert.Equal(["forms"], await UsesAsync(filtered, "deleteItemIds[0]"));
        Assert.Equal(new[] { ("CENTRAL", 1), ("SOUTH", 2), ("WEST", 3), ("NORTH", 4) }, await ItemsAsync(scenario, scenario.ZoneCatalogId));

        using var deleted = await client.PutAsJsonAsync(zones, Deleting(Zones(2), west), Ct);

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var catalog = (await deleted.ReadJsonAsync()).GetProperty("catalog");
        Assert.Equal(3, catalog.GetProperty("revision").GetInt64());
        Assert.Equal(3, catalog.GetProperty("itemsCount").GetInt32());
        Assert.Equal(new[] { ("CENTRAL", 1), ("SOUTH", 2), ("NORTH", 3) }, await ItemsAsync(scenario, scenario.ZoneCatalogId));
        Assert.Equal(3, (await ItemsAsync(scenario, scenario.CatalogId)).Length);
    }

    private static JsonObject Zones(long revision) => new()
    {
        ["expectedRevision"] = revision, ["name"] = new JsonObject { ["es"] = "Zonas", ["en"] = "Zones" },
        ["description"] = new JsonObject { ["es"] = "Zonas", ["en"] = "Zones" }, ["items"] = new JsonArray(),
    };

    private static JsonObject Item(string localId, string code, string es, string en) => new()
    {
        ["localId"] = localId, ["code"] = code, ["text"] = new JsonObject { ["es"] = es, ["en"] = en }, ["isActive"] = true,
    };

    private static JsonObject Deleting(JsonObject edit, params Guid[] itemIds)
    {
        edit["deleteItemIds"] = new JsonArray(itemIds.Select(itemId => (JsonNode)itemId.ToString("D")).ToArray());
        return edit;
    }

    // Una respuesta real del formulario publicado que eligió al primer proveedor.
    private static Task<bool> RecordAsync(FormRecipientScenario scenario) => scenario.ScopeAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == scenario.DefinitionId, Ct);
        var receipt = Guid.NewGuid(); var version = definition.ActiveVersionId!.Value;
        var record = BusinessFormRecord.Create(definition.Id, version, definition.AllocateRecordNumber(), scenario.FirstId, receipt, now,
            JsonSerializer.Serialize(new[] { new { fieldId = scenario.QuestionId, value = scenario.FirstItemId } }));
        db.Add(record);
        db.Add(BusinessFormCaptureReceipt.Complete(receipt, definition.Id, version, "catalog-item-deletion", record.Id, record.Number, now));
        db.Add(BusinessFormRecordReference.Create(record.Id, definition.Id, scenario.QuestionId, scenario.CatalogId, 2, scenario.FirstItemId,
            "Proveedor Centro", null, "Proveedores"));
        await db.SaveChangesAsync(Ct);
        return true;
    });

    private static async Task<string[]> UsesAsync(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.Equal(FormCatalogErrors.ItemInUse.Code, problem.GetProperty("code").GetString());
        Assert.Equal([path], problem.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        return problem.GetProperty("uses").EnumerateArray().Select(use => use.GetString()!).ToArray();
    }

    private static Task<(string Code, int Position)[]> ItemsAsync(FormRecipientScenario scenario, Guid catalogId) => scenario.ScopeAsync(async services =>
        (await services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormCatalogItem>().AsNoTracking()
            .Where(item => item.CatalogId == catalogId).OrderBy(item => item.Position).Select(item => new { item.Code, item.Position }).ToListAsync(Ct))
        .Select(item => (item.Code, item.Position)).ToArray());
}
