using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormRecipientFilterIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Recipient_preview_uses_the_saved_source_and_returns_ready_missing_and_unavailable_states_without_exposing_other_values(string culture)
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        using var client = scenario.Client(culture);
        var edit = scenario.CheckPayload();
        edit["requestId"] = Guid.NewGuid().ToString("D");
        var matches = edit["draft"]!["fields"]![0]!["source"]!["match"]!.AsArray();
        edit["draft"]!["fields"]![0]!["source"]!["match"] = new JsonArray(matches.Reverse().Select(value => value!.DeepClone()).ToArray());
        using var updated = await client.PutAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/draft", edit, Ct);
        var saved = await CaptureAsync(updated, "source-draft-" + culture);
        Assert.Equal(scenario.QuestionId, saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("fieldId").GetGuid());
        var ordered = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("source").GetProperty("match")
            .EnumerateArray().Select(value => value.GetProperty("fieldId").GetGuid()).ToArray();
        Assert.Equal(ordered.Order(), ordered);
        scenario.RememberSaved(saved);
        using var samples = await client.GetAsync("/api/forms/recipient-samples?search=Juan", Ct);
        var samplePage = await CaptureAsync(samples, "recipient-samples-" + culture);
        Assert.Equal(scenario.FirstId, Assert.Single(samplePage.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var path = $"/api/forms/definitions/{scenario.DefinitionId}/recipient-previews";
        object Preview(Guid userId, JsonObject source) => new { type = "choice", source, sampleUserId = userId, culture };
        using var ready = await client.PostAsJsonAsync(path, Preview(scenario.FirstId, scenario.Source()), Ct);
        var preview = await CaptureAsync(ready, "recipient-ready-" + culture);
        Assert.Equal("Ready", preview.GetProperty("state").GetString());
        Assert.Equal(1, preview.GetProperty("optionCount").GetInt32());
        var option = Assert.Single(preview.GetProperty("options").EnumerateArray());
        Assert.Equal(scenario.FirstItemId, option.GetProperty("id").GetGuid());
        Assert.Equal("FIRST", option.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, option.GetProperty("description").ValueKind);
        Assert.Single(preview.GetProperty("recipient").GetProperty("values").EnumerateArray());
        Assert.DoesNotContain("30712345678", preview.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Private detail", preview.GetRawText(), StringComparison.Ordinal);
        using var missing = await client.PostAsJsonAsync(path, Preview(scenario.MissingId, scenario.Source()), Ct);
        var missingResult = await CaptureAsync(missing, "recipient-missing-" + culture);
        Assert.Equal("RecipientDataMissing", missingResult.GetProperty("state").GetString());
        Assert.Empty(missingResult.GetProperty("options").EnumerateArray());
        var invalidSource = scenario.Source();
        invalidSource["match"]![0]!["fieldId"] = Guid.NewGuid().ToString("D");
        using var unavailable = await client.PostAsJsonAsync(path, Preview(scenario.FirstId, invalidSource), Ct);
        var unavailableResult = await CaptureAsync(unavailable, "recipient-unavailable-" + culture);
        Assert.Equal("FilterUnavailable", unavailableResult.GetProperty("state").GetString());
        Assert.Empty(unavailableResult.GetProperty("options").EnumerateArray());
        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/publication-checks", scenario.CheckPayload(), Ct);
        var assessment = await CaptureAsync(check, "source-check-warnings-" + culture);
        Assert.Empty(assessment.GetProperty("issues").EnumerateArray());
        Assert.Equal("LocallyValidated", assessment.GetProperty("compatibility").GetString());
        var warnings = assessment.GetProperty("warnings").EnumerateArray().ToArray();
        Assert.Equal(1, warnings.Single(value => value.GetProperty("code").GetString() == FormPublicationErrors.OptionsPerRecipientHigh.Code).GetProperty("maximum").GetInt32());
        var withoutData = warnings.Single(value => value.GetProperty("code").GetString() == FormPublicationErrors.RecipientsWithoutData.Code);
        Assert.Equal(1, withoutData.GetProperty("count").GetInt32());
        Assert.Equal("Pablo Sosa", Assert.Single(withoutData.GetProperty("people").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task Checking_and_accepting_unsorted_matches_uses_the_same_hash_and_replays_the_in_progress_manifest()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        await using var ready = scenario.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IStructuredFormChannel>(); services.AddSingleton<IStructuredFormChannel, ReadyStructuredFormChannel>(); }));
        using var client = ready.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, scenario.ManagerId.ToString("D"));
        var input = scenario.CheckPayload();
        var matches = input["draft"]!["fields"]![0]!["source"]!["match"]!.AsArray();
        input["draft"]!["fields"]![0]!["source"]!["match"] = new JsonArray(matches.Reverse().Select(value => value!.DeepClone()).ToArray());
        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/publication-checks", input, Ct);
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var assessment = await check.ReadJsonAsync();
        Assert.True(assessment.GetProperty("canAccept").GetBoolean());
        Assert.Equal(2, assessment.GetProperty("warnings").GetArrayLength());
        input["requestId"] = Guid.NewGuid().ToString("D");
        input["checkToken"] = assessment.GetProperty("checkToken").GetString();
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/publications", input, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        var versionId = receipt.GetProperty("versionId").GetGuid();
        var before = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormVersion>()
            .Where(version => version.Id == versionId).Select(version => new { version.SchemaJson, version.ContentHash }).SingleAsync(Ct));
        using var replay = await client.PostAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/publications", input, Ct);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(receipt.GetRawText(), (await replay.ReadJsonAsync()).GetRawText());
        var after = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormVersion>()
            .Where(version => version.Id == versionId).Select(version => new { version.SchemaJson, version.ContentHash }).SingleAsync(Ct));
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Sending_filters_each_recipient_blocks_missing_data_and_rejects_changing_the_role_of_a_filtered_field()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        async Task<StructuredFormPreparationResponse> Prepare(Guid user) => await scenario.ScopeAsync(services =>
            services.GetRequiredService<IStructuredFormSource>().PrepareAsync(user, "es", scenario.DefinitionId, versionId, false, Ct));
        var first = await Prepare(scenario.FirstId);
        var second = await Prepare(scenario.SecondId);
        Assert.True(first.CanSend); Assert.True(second.CanSend);
        Assert.Equal(scenario.FirstItemId, Assert.Single(Assert.Single(first.Catalogs).Options).Id);
        Assert.Equal(scenario.SecondItemId, Assert.Single(Assert.Single(second.Catalogs).Options).Id);
        Assert.Equal("FIRST", Assert.Single(first.Catalogs[0].Options).Shown!.Title);
        Assert.Null(first.Catalogs[0].Options[0].Shown!.Description);
        var missing = await Prepare(scenario.MissingId);
        Assert.False(missing.CanSend);
        Assert.Empty(missing.Catalogs);
        Assert.Equal(FormRecordErrors.RecipientDataMissing.Code, Assert.Single(missing.Issues).Code);
        using var client = scenario.Client();
        var edit = scenario.CatalogEdit(2);
        edit["fields"]![0]!["role"] = "data";
        using var rejected = await client.PutAsJsonAsync($"/api/forms/catalogs/{scenario.CatalogId}", edit, Ct);
        // La versión publicada y el borrador filtran por la zona: son dos usos y el papel queda como estaba.
        AssertFieldInUse(await rejected.ReadJsonAsync(), rejected.StatusCode, "fields[0].role", "Active", "Draft");
        var stillReady = await Prepare(scenario.FirstId);
        Assert.True(stillReady.CanSend);
        Assert.Equal(scenario.FirstItemId, Assert.Single(Assert.Single(stillReady.Catalogs).Options).Id);
    }

    [Fact]
    public async Task A_field_shown_only_in_a_question_presentation_can_change_its_role_while_a_filtered_field_cannot()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        using var client = scenario.Client();
        var draft = scenario.CheckPayload();
        draft["requestId"] = Guid.NewGuid().ToString("D");
        draft["draft"]!["fields"]![0]!["source"]!["presentation"] = new JsonObject
        { ["title"] = new JsonArray("code"), ["description"] = new JsonArray(scenario.DataFieldId.ToString("D")) };
        using var saved = await client.PutAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/draft", draft, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        // La pregunta del borrador sólo muestra el CUIT: PostgreSQL marca ese uso como no filtro y el papel puede cambiar.
        var shown = scenario.CatalogEdit(2);
        shown["fields"]![2]!["role"] = "filter";
        using var changed = await client.PutAsJsonAsync($"/api/forms/catalogs/{scenario.CatalogId}", shown, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var filtered = scenario.CatalogEdit(3);
        filtered["fields"]![2]!["role"] = "filter";
        filtered["fields"]![0]!["role"] = "data";
        using var rejected = await client.PutAsJsonAsync($"/api/forms/catalogs/{scenario.CatalogId}", filtered, Ct);
        AssertFieldInUse(await rejected.ReadJsonAsync(), rejected.StatusCode, "fields[0].role", "Draft");
    }

    private static void AssertFieldInUse(JsonElement problem, HttpStatusCode status, string path, params string[] stages)
    {
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(FormCatalogErrors.FieldInUse.Code, problem.GetProperty("code").GetString());
        Assert.Equal([path], problem.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        var uses = problem.GetProperty("uses").EnumerateArray().ToArray();
        Assert.Equal(stages, uses.Select(use => use.GetProperty("stage").GetString()));
        Assert.All(uses, use => Assert.Equal("Pedir repuesto", use.GetProperty("formName").GetString()));
    }

    [Fact]
    public async Task Catalog_detail_reads_from_postgres_the_question_identity_and_stage_of_each_field_use()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        await scenario.PublishForPreparationAsync(factory, Ct);
        using var client = scenario.Client();
        // Después de publicar, el borrador suma una pregunta que filtra sólo por zona: la versión activa no la tiene.
        var edit = scenario.CheckPayload();
        edit["requestId"] = Guid.NewGuid().ToString("D");
        edit["draft"]!["fields"]!.AsArray().Add(new JsonObject
        {
            ["localId"] = "other", ["type"] = "choice", ["required"] = false,
            ["label"] = new JsonObject { ["es"] = "Otro proveedor", ["en"] = "Another supplier" },
            ["source"] = new JsonObject
            {
                ["catalog"] = scenario.CatalogId.ToString("D"),
                ["match"] = new JsonArray(new JsonObject { ["fieldId"] = scenario.ZoneFieldId.ToString("D"), ["personAttributeId"] = scenario.AttributeId.ToString("D") }),
                ["presentation"] = new JsonObject { ["title"] = new JsonArray("code"), ["description"] = new JsonArray() },
            },
        });
        using var updated = await client.PutAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var draftFields = (await updated.ReadJsonAsync()).GetProperty("definition").GetProperty("draft").GetProperty("fields");
        Assert.Equal(scenario.QuestionId, draftFields[0].GetProperty("fieldId").GetGuid());
        var draftOnly = draftFields[1].GetProperty("fieldId").GetGuid();

        using var detail = await client.GetAsync($"/api/forms/catalogs/{scenario.CatalogId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var usedBy = (await detail.ReadJsonAsync()).GetProperty("fields").EnumerateArray().ToDictionary(field => field.GetProperty("id").GetGuid(),
            field => field.GetProperty("usedBy").EnumerateArray().Select(FieldUse).ToArray());
        var form = scenario.DefinitionId;
        // La misma pregunta en la versión activa y en el borrador son dos usos con la misma identidad; la que sólo está en el
        // borrador trae la suya y la etapa Draft.
        FormCatalogFieldUsageResponse[] zone = [new(form, "Pedir repuesto", "Otro proveedor", draftOnly, "Draft"),
            new(form, "Pedir repuesto", "Proveedor", scenario.QuestionId, "Active"), new(form, "Pedir repuesto", "Proveedor", scenario.QuestionId, "Draft")];
        Assert.Equal(zone, usedBy[scenario.ZoneFieldId]);
        FormCatalogFieldUsageResponse[] kind = [new(form, "Pedir repuesto", "Proveedor", scenario.QuestionId, "Active"),
            new(form, "Pedir repuesto", "Proveedor", scenario.QuestionId, "Draft")];
        Assert.Equal(kind, usedBy[scenario.KindFieldId]);
        FormCatalogFieldUsageResponse[] own = [new(null, "Proveedores", null, null, "Catalog")];
        Assert.Equal(own, usedBy[scenario.DataFieldId]);

        static FormCatalogFieldUsageResponse FieldUse(JsonElement use) => new(
            use.GetProperty("definitionId").ValueKind == JsonValueKind.Null ? null : use.GetProperty("definitionId").GetGuid(),
            use.GetProperty("formName").GetString()!, use.GetProperty("question").GetString(),
            use.GetProperty("questionId").ValueKind == JsonValueKind.Null ? null : use.GetProperty("questionId").GetGuid(),
            use.GetProperty("stage").GetString()!);
    }

    [Fact]
    public async Task Recipient_preview_routes_require_a_session_and_both_form_permissions()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        using var anonymous = scenario.Host.CreateClient();
        using var list = await anonymous.GetAsync("/api/forms/recipient-samples", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        using var preview = await anonymous.PostAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/recipient-previews",
            new { type = "choice", source = scenario.Source(), sampleUserId = scenario.FirstId, culture = "es" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, preview.StatusCode);
        using var denied = scenario.Client(actor: scenario.FirstId);
        using var deniedList = await denied.GetAsync("/api/forms/recipient-samples", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, deniedList.StatusCode);
        using var deniedPreview = await denied.PostAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/recipient-previews",
            new { type = "choice", source = scenario.Source(), sampleUserId = scenario.FirstId, culture = "es" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, deniedPreview.StatusCode);
    }

    private static async Task<JsonElement> CaptureAsync(HttpResponseMessage response, string name)
    {
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        using var document = JsonDocument.Parse(bytes);
        var json = document.RootElement.Clone();
        Assert.True(!json.TryGetProperty("checkToken", out var token) || token.ValueKind == JsonValueKind.Null);
        var output = Environment.GetEnvironmentVariable("FORMS_HTTP_CONTRACT_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        { var path = Path.Combine(output, "recipient-filters"); Directory.CreateDirectory(path); await File.WriteAllBytesAsync(Path.Combine(path, name + ".json"), bytes, Ct); }
        return json;
    }
}
