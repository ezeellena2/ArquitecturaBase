using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// Quitar un campo que usa un formulario, con los usos que PostgreSQL lee del borrador y de la versión activa.
[Collection(ApiTestGroup.Name)]
public sealed class FormCatalogFieldInUseIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Removing_a_field_used_by_a_form_draft_is_rejected_with_its_draft_use_and_saves_nothing()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        using var client = scenario.Client();

        using var rejected = await client.PutAsJsonAsync($"/api/forms/catalogs/{scenario.CatalogId}", Removal(scenario, 1, scenario.KindFieldId), Ct);

        // Sólo el borrador filtra por el tipo: un uso Draft con la pregunta y su identidad, aunque se confirme borrar sus valores.
        Assert.Equal(new[] { new FormCatalogFieldUsageResponse(scenario.DefinitionId, "Pedir repuesto", "Proveedor", scenario.QuestionId, "Draft") },
            await UsesAsync(rejected, "fields[1].isActive"));
        var (revision, fields) = await StoredAsync(scenario);
        Assert.Equal(2, revision);
        Assert.True(fields.Single(field => field.Id == scenario.KindFieldId).IsActive);
    }

    [Fact]
    public async Task Removing_a_field_used_by_the_active_version_is_rejected_even_after_the_draft_drops_it()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        await scenario.PublishForPreparationAsync(factory, Ct);
        // El borrador deja de filtrar por el tipo; la versión activa lo sigue usando.
        await scenario.SaveDraftAsync(payload => FormRecipientScenario.DropMatch(payload, scenario.KindFieldId), Ct);
        using var client = scenario.Client();
        var path = $"/api/forms/catalogs/{scenario.CatalogId}";
        FormCatalogFieldUsageResponse Use(string stage) => new(scenario.DefinitionId, "Pedir repuesto", "Proveedor", scenario.QuestionId, stage);

        using var kind = await client.PutAsJsonAsync(path, Removal(scenario, 1, scenario.KindFieldId), Ct);
        Assert.Equal(new[] { Use("Active") }, await UsesAsync(kind, "fields[1].isActive"));
        using var zone = await client.PutAsJsonAsync(path, Removal(scenario, 0, scenario.ZoneFieldId), Ct);
        Assert.Equal(new[] { Use("Active"), Use("Draft") }, await UsesAsync(zone, "fields[0].isActive"));
        await FormCatalogContractCapture.WriteAsync(zone, "fields-in-use", Ct);

        using var detail = await client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var usedBy = (await detail.ReadJsonAsync()).GetProperty("fields").EnumerateArray().ToDictionary(field => field.GetProperty("id").GetGuid(),
            field => field.GetProperty("usedBy").EnumerateArray().Select(FormRecipientScenario.FieldUse).ToArray());
        Assert.Equal(new[] { Use("Active"), Use("Draft") }, usedBy[scenario.ZoneFieldId]);
        Assert.Equal(new[] { Use("Active") }, usedBy[scenario.KindFieldId]);
        Assert.Equal(new[] { new FormCatalogFieldUsageResponse(null, "Proveedores", null, null, "Catalog") }, usedBy[scenario.DataFieldId]);
        await FormCatalogContractCapture.WriteAsync(detail, "fields-usage-detail", Ct);
        var (revision, fields) = await StoredAsync(scenario);
        Assert.Equal(2, revision);
        Assert.All(fields, field => Assert.True(field.IsActive));
    }

    private static JsonObject Removal(FormRecipientScenario scenario, int index, Guid fieldId)
    {
        var edit = scenario.CatalogEdit(2);
        edit["fields"]![index]!["isActive"] = false;
        edit["confirmFieldRemovals"] = new JsonArray(fieldId.ToString("D"));
        return edit;
    }

    private static async Task<FormCatalogFieldUsageResponse[]> UsesAsync(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.Equal(FormCatalogErrors.FieldInUse.Code, problem.GetProperty("code").GetString());
        Assert.Equal([path], problem.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        return problem.GetProperty("uses").EnumerateArray().Select(FormRecipientScenario.FieldUse).ToArray();
    }

    private static Task<(long Revision, BusinessFormCatalogField[] Fields)> StoredAsync(FormRecipientScenario scenario) => scenario.ScopeAsync(async services =>
    {
        var catalog = await services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormCatalog>().AsNoTracking()
            .Where(value => value.Id == scenario.CatalogId).Select(value => new { value.Revision, value.FieldsJson }).SingleAsync(Ct);
        return (catalog.Revision, JsonSerializer.Deserialize<BusinessFormCatalogField[]>(catalog.FieldsJson!, JsonSerializerOptions.Web)!);
    });
}
