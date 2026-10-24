using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// Cambiar un dato de las personas que filtra un formulario, con los usos que PostgreSQL lee del borrador y de la versión activa.
[Collection(ApiTestGroup.Name)]
public sealed class FormPersonAttributeInUseIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_attribute_filtered_by_a_form_draft_keeps_its_type_source_and_activity()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        using var client = scenario.Client();
        var path = $"/api/forms/person-attributes/{scenario.AttributeId}";

        Assert.Equal(new[] { (scenario.DefinitionId, "Pedir repuesto") }, await UsedByAsync(client, scenario));
        // Pasar a texto, aunque se confirme borrar los valores, o desactivarlo: el borrador lo usa.
        await AssertInUseAsync(await client.PutAsJsonAsync(path, Edit(scenario, type: "Text"), Ct));
        await AssertInUseAsync(await client.PutAsJsonAsync(path, Edit(scenario, isActive: false), Ct));
        await AssertStoredAsync(scenario, 1, "Zona");

        using var renamed = await client.PutAsJsonAsync(path, Edit(scenario, name: "Zona de atención"), Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var attribute = await renamed.ReadJsonAsync();
        Assert.Equal(2, attribute.GetProperty("revision").GetInt64());
        Assert.Equal(new[] { (scenario.DefinitionId, "Pedir repuesto") }, Uses(attribute));
        await AssertStoredAsync(scenario, 2, "Zona de atención");
    }

    [Fact]
    public async Task An_attribute_filtered_only_by_the_active_version_is_still_in_use()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        await scenario.PublishForPreparationAsync(factory, Ct);
        // El borrador deja de filtrar por la zona de la persona; la versión activa la sigue usando.
        await scenario.SaveDraftAsync(payload => FormRecipientScenario.DropMatch(payload, scenario.ZoneFieldId), Ct);
        using var client = scenario.Client();
        var path = $"/api/forms/person-attributes/{scenario.AttributeId}";

        Assert.Equal(new[] { (scenario.DefinitionId, "Pedir repuesto") }, await UsedByAsync(client, scenario));
        await AssertInUseAsync(await client.PutAsJsonAsync(path, Edit(scenario, sourceCatalogId: scenario.CatalogId), Ct));
        await AssertInUseAsync(await client.PutAsJsonAsync(path, Edit(scenario, type: "Text"), Ct));
        await AssertStoredAsync(scenario, 1, "Zona");
    }

    // Lo guardado del dato Zona (fuente Zonas, salvo otra o texto), con lo que cambie cada caso; confirma borrar los valores para que sólo el uso lo frene.
    private static object Edit(FormRecipientScenario scenario, string type = "Catalog", Guid? sourceCatalogId = null, bool isActive = true, string name = "Zona") => new
    {
        expectedRevision = 1, name = new { es = name, en = "Zone" }, type,
        sourceCatalogId = type == "Text" ? null : sourceCatalogId ?? (Guid?)scenario.ZoneCatalogId, position = 0, isActive, confirmValueRemoval = true,
    };

    private static async Task AssertInUseAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(FormPersonDataErrors.AttributeInUse.Code, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        }
    }

    private static async Task<(Guid, string)[]> UsedByAsync(HttpClient client, FormRecipientScenario scenario)
    {
        using var response = await client.GetAsync("/api/forms/person-attributes", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Uses((await response.ReadJsonAsync()).GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == scenario.AttributeId));
    }

    private static (Guid, string)[] Uses(JsonElement attribute) => attribute.GetProperty("usedBy").EnumerateArray()
        .Select(use => (use.GetProperty("definitionId").GetGuid(), use.GetProperty("name").GetString()!)).ToArray();

    // Tipo, fuente y actividad no cambian, y Juan y María conservan su zona sin otra revisión.
    private static async Task AssertStoredAsync(FormRecipientScenario scenario, long revision, string name) => await scenario.ScopeAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var attribute = await db.Set<BusinessFormPersonAttribute>().AsNoTracking().SingleAsync(value => value.Id == scenario.AttributeId, Ct);
        Assert.Equal((revision, BusinessFormPersonAttributeType.Catalog, (Guid?)scenario.ZoneCatalogId, true), (attribute.Revision, attribute.Type, attribute.SourceCatalogId, attribute.IsActive));
        Assert.Equal(name, JsonSerializer.Deserialize<Dictionary<string, string>>(attribute.NameJson)!["es"]);
        var people = await db.Set<BusinessFormPersonData>().AsNoTracking().Where(value => value.UserId == scenario.FirstId || value.UserId == scenario.SecondId)
            .ToDictionaryAsync(value => value.UserId, Ct);
        foreach (var (person, option) in new[] { (scenario.FirstId, scenario.CentralId), (scenario.SecondId, scenario.SouthId) })
        {
            Assert.Equal(1, people[person].Revision);
            Assert.Equal(option.ToString("D"), JsonSerializer.Deserialize<Dictionary<string, string>>(people[person].ValuesJson)![scenario.AttributeId.ToString("D")]);
        }
        return true;
    });
}
