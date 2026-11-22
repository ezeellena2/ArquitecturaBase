using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>
/// The detail of a response read with the form of its version (VF1-A) with PostgreSQL: besides its values, the detail brings that
/// version as the app loads it, described for whoever answered, with the answers and files in its shape, as a correction opens
/// them. A response of an earlier version keeps its own, also once the form is deactivated.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class FormResponseEntryHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Shipping = "0196f2a0-0000-7000-8000-0000000000e1";
    private const string Quantity = "0196f2a0-0000-7000-8000-0000000000e2";

    [Fact]
    public async Task The_detail_brings_the_form_of_its_version_with_the_answers_and_files_in_its_shape()
    {
        var files = new AssistantCopyFiles();
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct, services =>
        {
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(files);
        });
        var saved = await scenario.SaveDraftAsync(payload =>
        {
            var fields = payload["draft"]!["fields"]!.AsArray();
            fields.Add(new JsonObject { ["localId"] = "by-zone", ["type"] = "choice", ["required"] = true,
                ["label"] = new JsonObject { ["es"] = "Proveedor por zona", ["en"] = "Supplier by zone" },
                ["source"] = new JsonObject { ["catalog"] = scenario.CatalogId.ToString("D"), ["groupBy"] = scenario.ZoneFieldId.ToString("D") } });
            fields.Add(new JsonObject { ["localId"] = Shipping, ["type"] = "choice", ["required"] = true,
                ["label"] = new JsonObject { ["es"] = "¿Lleva envío?", ["en"] = "Shipping?" },
                ["options"] = new JsonArray(
                    new JsonObject { ["localId"] = "yes", ["label"] = new JsonObject { ["es"] = "Sí", ["en"] = "Yes" } },
                    new JsonObject { ["localId"] = "no", ["label"] = new JsonObject { ["es"] = "No", ["en"] = "No" } }) });
            fields.Add(new JsonObject { ["localId"] = "address", ["type"] = "shortText", ["required"] = true,
                ["label"] = new JsonObject { ["es"] = "Dirección de envío", ["en"] = "Shipping address" },
                ["showWhen"] = new JsonObject { ["questionId"] = Shipping, ["values"] = new JsonArray("yes") } });
            fields.Add(new JsonObject { ["localId"] = "items", ["type"] = "group", ["required"] = true,
                ["label"] = new JsonObject { ["es"] = "Artículos", ["en"] = "Items" }, ["rows"] = new JsonObject { ["minRows"] = 1, ["maxRows"] = 3 } });
            fields.Add(new JsonObject { ["localId"] = "product", ["type"] = "choice", ["required"] = true, ["repeated"] = true,
                ["label"] = new JsonObject { ["es"] = "Artículo", ["en"] = "Item" }, ["source"] = scenario.Source() });
            fields.Add(new JsonObject { ["localId"] = Quantity, ["type"] = "decimal", ["required"] = true, ["repeated"] = true,
                ["label"] = new JsonObject { ["es"] = "Cantidad", ["en"] = "Quantity" } });
            fields.Add(new JsonObject { ["localId"] = "total", ["type"] = "decimal", ["required"] = false,
                ["label"] = new JsonObject { ["es"] = "Total", ["en"] = "Total" },
                ["calculation"] = new JsonObject { ["operation"] = "sum", ["operands"] = new JsonArray(Quantity) } });
            fields.Add(new JsonObject { ["localId"] = "photos", ["type"] = "photo", ["required"] = false,
                ["label"] = new JsonObject { ["es"] = "Fotos", ["en"] = "Photos" },
                ["files"] = new JsonObject { ["minFiles"] = 0, ["maxFiles"] = 3, ["photoSource"] = "cameraOrGallery" } });
            payload["draft"]!["sections"] = new JsonArray(new JsonObject { ["start"] = 4, ["title"] = new JsonObject { ["es"] = "Pedido", ["en"] = "Order" } });
        }, Ct);
        var fields = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields");
        Guid Id(int index) => fields[index].GetProperty("fieldId").GetGuid();
        var (byZoneId, shippingId, addressId, groupId, productId, quantityId, totalId, photosId) = (Id(1), Id(2), Id(3), Id(4), Id(5), Id(6), Id(7), Id(8));
        var no = fields[2].GetProperty("options")[1].GetProperty("choiceId").GetGuid();
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        using var first = scenario.Client("es", scenario.FirstId);
        object Item(string quantity) => new object[] { new { fieldId = productId, value = scenario.FirstItemId.ToString("D") }, new { fieldId = quantityId, value = quantity } };
        // The address stays hidden by its condition and the total is the server's.
        var content = Send(versionId, [new { fieldId = scenario.QuestionId, value = scenario.FirstItemId.ToString("D") },
            new { fieldId = byZoneId, value = scenario.SecondItemId.ToString("D") }, new { fieldId = shippingId, value = no.ToString("D") },
            new { fieldId = groupId, rows = new[] { Item("2"), Item("3") } }]);
        var photo = new ByteArrayContent(new FormPdfRendererFixture().PhotoContent);
        photo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(photo, "files", "lote.jpg");
        content.Add(new StringContent(photosId.ToString("D")), "fileFieldIds");
        using var sent = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}", content, Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var recordId = (await sent.ReadJsonAsync()).GetProperty("recordId").GetGuid();

        using var detail = await first.GetAsync($"/api/forms/records/{recordId}", Ct);

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var body = await detail.ReadJsonAsync();
        Assert.Equal(7, body.GetProperty("values").GetArrayLength());
        var entry = body.GetProperty("entry");
        var form = entry.GetProperty("entry");
        Assert.Equal((versionId, 1L, "es"), (form.GetProperty("versionId").GetGuid(), form.GetProperty("versionNumber").GetInt64(), form.GetProperty("culture").GetString()));
        var questions = form.GetProperty("questions").EnumerateArray().ToArray();
        Assert.Equal([scenario.QuestionId, byZoneId, shippingId, addressId, groupId, productId, quantityId, totalId, photosId],
            questions.Select(question => question.GetProperty("fieldId").GetGuid()));
        Assert.Equal(["choice", "choice", "choice", "shortText", "group", "choice", "decimal", "decimal", "photo"], questions.Select(question => question.GetProperty("type").GetString()));
        Assert.Equal("Pedido", questions[4].GetProperty("section").GetString());
        Assert.Equal(shippingId, questions[3].GetProperty("showWhen").GetProperty("questionId").GetGuid());
        Assert.Equal(("sum", quantityId), (questions[7].GetProperty("calculation").GetProperty("operation").GetString(),
            questions[7].GetProperty("calculation").GetProperty("operands")[0].GetGuid()));
        Assert.True(questions[5].GetProperty("repeated").GetBoolean() && questions[6].GetProperty("repeated").GetBoolean());
        // Each catalog question is described for whoever answered: Juan's zone leaves him only the first supplier.
        Assert.Equal(scenario.FirstItemId, questions[0].GetProperty("catalog").GetProperty("onlyOption").GetProperty("id").GetGuid());
        Assert.Equal("zone", questions[1].GetProperty("catalog").GetProperty("groupBy").GetProperty("name").GetString());

        // The answers as a correction opens them: the hidden address has none, and the block goes last.
        var answers = entry.GetProperty("answers").EnumerateArray().ToArray();
        Assert.Equal([scenario.QuestionId, byZoneId, shippingId, totalId, groupId], answers.Select(answer => answer.GetProperty("fieldId").GetGuid()));
        Assert.Equal((scenario.FirstItemId.ToString("D"), "Proveedor Centro"), (answers[0].GetProperty("value").GetString(),
            answers[0].GetProperty("options")[0].GetProperty("title").GetString()));
        Assert.Equal("Proveedor Sur", answers[1].GetProperty("options")[0].GetProperty("title").GetString());
        Assert.Equal((scenario.SouthId.ToString("D"), "Sur"), (answers[1].GetProperty("group").GetProperty("id").GetString(),
            answers[1].GetProperty("group").GetProperty("text").GetString()));
        Assert.Equal(no.ToString("D"), answers[2].GetProperty("value").GetString());
        Assert.Equal("5", answers[3].GetProperty("value").GetString());
        var rows = answers[4].GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(["2", "3"], rows.Select(row => row[1].GetProperty("value").GetString()));
        Assert.All(rows, row => Assert.Equal((productId, "Proveedor Centro"), (row[0].GetProperty("fieldId").GetGuid(),
            row[0].GetProperty("options")[0].GetProperty("title").GetString())));
        var file = Assert.Single(entry.GetProperty("files").EnumerateArray());
        Assert.Equal((photosId, "lote.jpg", "image/jpeg"), (file.GetProperty("fieldId").GetGuid(), file.GetProperty("name").GetString(),
            file.GetProperty("contentType").GetString()));
    }

    [Fact]
    public async Task A_response_is_read_with_its_own_version_after_a_new_one_and_once_the_form_is_deactivated()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        var readerId = await scenario.AddPersonAsync("Laura Ríos", [BusinessFormsPermissions.RecordsRead], Ct);
        using var first = scenario.Client("es", scenario.FirstId);
        using var sent = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(versionId, [new { fieldId = scenario.QuestionId, value = scenario.FirstItemId.ToString("D") }]), Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var recordId = (await sent.ReadJsonAsync()).GetProperty("recordId").GetGuid();
        var nextId = await PublishNextVersionAsync(scenario, versionId);
        using var active = await first.GetAsync($"/api/forms/entries/{scenario.DefinitionId}", Ct);
        Assert.Equal(nextId, (await active.ReadJsonAsync()).GetProperty("versionId").GetGuid());
        using var reader = scenario.Client("es", readerId);

        foreach (var deactivated in new[] { false, true })
        {
            if (deactivated) await DeactivateAsync(scenario);
            using var detail = await reader.GetAsync($"/api/forms/records/{recordId}", Ct);

            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var entry = (await detail.ReadJsonAsync()).GetProperty("entry");
            var form = entry.GetProperty("entry");
            Assert.Equal((versionId, 1L), (form.GetProperty("versionId").GetGuid(), form.GetProperty("versionNumber").GetInt64()));
            var question = Assert.Single(form.GetProperty("questions").EnumerateArray());
            Assert.Equal("Proveedor", question.GetProperty("label").GetString());
            // Described for Juan, who answered, and not for Laura, who reads without a zone: his filters leave only his supplier.
            Assert.Equal(scenario.FirstItemId, question.GetProperty("catalog").GetProperty("onlyOption").GetProperty("id").GetGuid());
            Assert.Equal(scenario.FirstItemId.ToString("D"), Assert.Single(entry.GetProperty("answers").EnumerateArray()).GetProperty("value").GetString());
            Assert.Empty(entry.GetProperty("files").EnumerateArray());
        }
    }

    [Fact]
    public async Task Openapi_declares_the_form_of_the_detail_as_always_present()
    {
        var schemas = (await OpenApiTests.ReadDocumentAsync(factory)).GetProperty("components").GetProperty("schemas");
        var detail = schemas.GetProperty("FormResponseDetailResponse");

        Assert.Contains("entry", detail.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("FormResponseEntryResponse", detail.GetProperty("properties").GetProperty("entry").GetRawText(), StringComparison.Ordinal);
        Assert.Equal(["answers", "entry", "files"], schemas.GetProperty("FormResponseEntryResponse").GetProperty("properties").EnumerateObject()
            .Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    // A second version, active, whose question says something else; its fields are the same.
    private async Task<Guid> PublishNextVersionAsync(FormRecipientScenario scenario, Guid activeVersionId) => await scenario.ScopeAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == scenario.DefinitionId, Ct);
        var active = await db.Set<BusinessFormVersion>().SingleAsync(value => value.Id == activeVersionId, Ct);
        var schema = JsonNode.Parse(active.SchemaJson)!;
        schema["questions"]![0]!["label"] = new JsonObject { ["es"] = "Proveedor elegido", ["en"] = "Chosen supplier" };
        var next = BusinessFormVersion.Create(definition.Id, 2, new string('b', 64));
        next.SealSchema(schema.ToJsonString());
        next.SealPresentation(active.NameJson!, active.SubmitLabelJson!, active.Culture!, active.QuestionCount!.Value);
        db.Set<BusinessFormVersion>().Add(next);
        await db.SaveChangesAsync(Ct);
        db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(factory, definition, next, scenario.ManagerId, ["es", "en"]));
        await db.SaveChangesAsync(Ct);
        Assert.True(definition.ActivateVersion(activeVersionId, next.Id).IsSuccess);
        await db.SaveChangesAsync(Ct);
        return next.Id;
    });

    private Task<int> DeactivateAsync(FormRecipientScenario scenario) => scenario.ScopeAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == scenario.DefinitionId, Ct);
        Assert.True(definition.Deactivate(definition.Revision, scenario.ManagerId, factory.Clock.GetUtcNow().UtcDateTime).IsSuccess);
        return await db.SaveChangesAsync(Ct);
    });

    private static MultipartFormDataContent Send(Guid versionId, object[] answers) => new()
    {
        { new StringContent(Guid.NewGuid().ToString("D")), "requestId" },
        { new StringContent(versionId.ToString("D")), "versionId" },
        { new StringContent(JsonSerializer.Serialize(answers, JsonSerializerOptions.Web)), "answers" },
    };
}
