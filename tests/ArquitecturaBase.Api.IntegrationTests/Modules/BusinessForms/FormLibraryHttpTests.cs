using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// BI1-A with PostgreSQL: the library lists the installed standards, «Agregar» creates an own draft that remembers its standard and
// its bound questions (once per request) in the folder chosen or the standard's, and a save that removes a bound question or changes its type is refused at it, while
// its text and the value of its toggles (IV2-A) change. A catalog the standard brings comes with its first options, and a condition names a catalog option by its code.
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task A_standard_is_added_as_an_own_draft_whose_bound_questions_keep_what_the_standard_needs()
    {
        using var reader = await OperatorAsync(["formtemplates.read"]);
        using var designer = await OperatorAsync(["formtemplates.read", "formtemplates.manage"]);

        using var library = await reader.GetAsync("/api/forms/library", Ct);
        var standard = (await CaptureContractAsync(library, HttpStatusCode.OK, "library", "list-es")).GetProperty("standards").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == TestFormStandardSource.Key);
        Assert.Equal("Remito de prueba", standard.GetProperty("name").GetString());
        Assert.Equal("Pruebas", standard.GetProperty("source").GetString());
        Assert.Equal("es", standard.GetProperty("culture").GetString());
        var copiesBefore = standard.GetProperty("copies").GetInt32();

        using var forbidden = await reader.PostAsJsonAsync($"/api/forms/library/{TestFormStandardSource.Key}/copies", Add(Guid.NewGuid(), "RTA"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var missing = await designer.PostAsJsonAsync("/api/forms/library/test.missing/copies", Add(Guid.NewGuid(), "RTB"), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var requestId = Guid.NewGuid();
        var prefix = "R" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        using var added = await designer.PostAsJsonAsync($"/api/forms/library/{TestFormStandardSource.Key}/copies", Add(requestId, prefix), Ct);
        var definition = (await CaptureContractAsync(added, HttpStatusCode.Created, "library", "copy-created")).GetProperty("definition");
        var fields = definition.GetProperty("draft").GetProperty("fields");
        var bound = definition.GetProperty("standard").GetProperty("bound");
        Assert.Equal(TestFormStandardSource.Key, definition.GetProperty("standard").GetProperty("key").GetString());
        Assert.Equal(6, bound.EnumerateObject().Count());
        Assert.Equal(fields[5].GetProperty("fieldId").GetGuid(), bound.GetProperty("total").GetGuid());
        Assert.Equal([fields[2].GetProperty("fieldId").GetGuid(), fields[3].GetProperty("fieldId").GetGuid()],
            fields[4].GetProperty("calculation").GetProperty("operands").EnumerateArray().Select(value => value.GetGuid()));

        using var repeated = await designer.PostAsJsonAsync($"/api/forms/library/{TestFormStandardSource.Key}/copies", Add(requestId, prefix), Ct);
        Assert.Equal(HttpStatusCode.Created, repeated.StatusCode);
        Assert.Equal(definition.GetProperty("id").GetGuid(), (await repeated.ReadJsonAsync()).GetProperty("definition").GetProperty("id").GetGuid());
        using var after = await reader.GetAsync("/api/forms/library", Ct);
        Assert.Equal(copiesBefore + 1, (await after.ReadJsonAsync()).GetProperty("standards").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == TestFormStandardSource.Key).GetProperty("copies").GetInt32());

        var id = definition.GetProperty("id").GetGuid();
        var revision = definition.GetProperty("revision").GetInt64();
        async Task<HttpResponseMessage> SaveAsync(Func<JsonArray, JsonArray> change) => await designer.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", new JsonObject
        {
            ["requestId"] = Guid.NewGuid(), ["name"] = new JsonObject { ["es"] = "Remito propio" }, ["prefix"] = prefix, ["expectedRevision"] = revision,
            ["draft"] = new JsonObject { ["format"] = 1, ["culture"] = "es", ["submitLabel"] = new JsonObject { ["es"] = "Enviar", ["en"] = "Send" }, ["fields"] = change(Inputs(fields)) },
        }, Ct);

        using var removed = await SaveAsync(inputs => { inputs.RemoveAt(5); return inputs; });
        Assert.Equal(HttpStatusCode.BadRequest, removed.StatusCode);
        Assert.Equal("Forms.Draft.BoundQuestionChanged", (await removed.ReadJsonAsync()).GetProperty("code").GetString());
        using var required = await SaveAsync(inputs => { inputs[0]!["required"] = false; return inputs; });
        Assert.Equal(HttpStatusCode.BadRequest, required.StatusCode);
        Assert.True((await required.ReadJsonAsync()).GetProperty("errors").TryGetProperty("draft.fields[0]", out _));
        // IV2-A: the toggle the standard declares on «Precio unitario» comes in the copy, unchecked; only its value changes, and
        // no other question has one.
        var toggle = Assert.Single(fields[3].GetProperty("toggles").EnumerateArray());
        Assert.Equal(("pricesIncludeTax", false), (toggle.GetProperty("key").GetString(), toggle.GetProperty("value").GetBoolean()));
        using var relabeled = await SaveAsync(inputs => { inputs[3]!["toggles"]![0]!["label"] = new JsonObject { ["es"] = "Con impuestos" }; return inputs; });
        Assert.Equal(HttpStatusCode.BadRequest, relabeled.StatusCode);
        Assert.True((await relabeled.ReadJsonAsync()).GetProperty("errors").TryGetProperty("draft.fields[3]", out _));
        using var unbound = await SaveAsync(inputs => { inputs[6]!["toggles"] = inputs[3]!["toggles"]!.DeepClone(); return inputs; });
        Assert.Equal(HttpStatusCode.BadRequest, unbound.StatusCode);
        Assert.True((await unbound.ReadJsonAsync()).GetProperty("errors").TryGetProperty("draft.fields[6].toggles", out _));
        using var renamed = await SaveAsync(inputs =>
        {
            inputs[0]!["label"] = new JsonObject { ["es"] = "Razón social", ["en"] = "Customer" };
            inputs[3]!["toggles"]![0]!["value"] = true;
            inputs.RemoveAt(6);
            return inputs;
        });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.True((await renamed.ReadJsonAsync()).GetProperty("definition").GetProperty("draft").GetProperty("fields")[3]
            .GetProperty("toggles")[0].GetProperty("value").GetBoolean());
    }

    [Fact]
    public async Task A_standard_brings_its_catalog_once_and_one_that_names_a_missing_catalog_is_refused()
    {
        using var designer = await OperatorAsync(["formtemplates.read", "formtemplates.manage"]);
        string Prefix() => "V" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        using var first = await designer.PostAsJsonAsync($"/api/forms/library/{TestFormStandardSource.CustomersKey}/copies", Add(Guid.NewGuid(), Prefix()), Ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await designer.PostAsJsonAsync($"/api/forms/library/{TestFormStandardSource.CustomersKey}/copies", Add(Guid.NewGuid(), Prefix()), Ct);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var catalogs = await factory.ExecuteDbContextAsync(db => db.Set<ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormCatalog>()
            .Where(catalog => catalog.Key == TestFormStandardSource.CustomersCatalog).Select(catalog => new { catalog.Id, catalog.FieldsJson }).ToListAsync(Ct));
        var catalog = Assert.Single(catalogs);
        Assert.Contains("\"cuit\"", catalog.FieldsJson);
        // The catalog comes once with the options the standard declares, in their order; the second copy does not add them again.
        var options = await factory.ExecuteDbContextAsync(db => db.Set<ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormCatalogItem>()
            .Where(item => item.CatalogId == catalog.Id).OrderBy(item => item.Position).Select(item => new { item.Id, item.Code, item.TextEs, item.IsActive }).ToListAsync(Ct));
        Assert.Equal([("final", "Consumidor final", true), ("other", "Otro: escribir la CUIT", true)], options.Select(item => (item.Code, item.TextEs, item.IsActive)));
        var copies = new List<Guid>();
        foreach (var created in new[] { first, second })
        {
            var definition = (await created.ReadJsonAsync()).GetProperty("definition");
            var fields = definition.GetProperty("draft").GetProperty("fields");
            Assert.Equal(catalog.Id, fields[0].GetProperty("source").GetProperty("catalog").GetGuid());
            // «code:other» became that option's identity: in the copy that created the catalog and in the one that found it.
            var showWhen = fields[1].GetProperty("showWhen");
            Assert.Equal(fields[0].GetProperty("fieldId").GetGuid(), showWhen.GetProperty("questionId").GetGuid());
            Assert.Equal([options[1].Id.ToString("D")], showWhen.GetProperty("values").EnumerateArray().Select(value => value.GetString()));
            // IV1-A: the option «Cliente» starts with, declared by its code, is that option's identity too.
            Assert.Equal(options[0].Id, fields[0].GetProperty("source").GetProperty("default").GetGuid());
            copies.Add(definition.GetProperty("id").GetGuid());
        }
        // Both copies and the catalog they brought go to the standard's folder, created once and found again by its key.
        var folder = Assert.Single(await factory.ExecuteDbContextAsync(db => db.Set<ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormFolder>()
            .Where(value => value.SystemKey == TestFormStandardSource.SalesFolder).Select(value => new { value.Id, value.Name, value.ParentId }).ToListAsync(Ct)));
        Assert.Equal(("Ventas de prueba", (Guid?)null), (folder.Name, folder.ParentId));
        var filed = await factory.ExecuteDbContextAsync(db => db.Set<ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormFolderMembership>()
            .Where(value => value.FolderId == folder.Id).Select(value => value.ItemId).ToListAsync(Ct));
        Assert.Equal(copies.Append(catalog.Id).Order(), filed.Order());

        using var missing = await designer.PostAsJsonAsync($"/api/forms/library/{TestFormStandardSource.MissingKey}/copies", Add(Guid.NewGuid(), Prefix()), Ct);
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        Assert.Equal("Forms.Library.CatalogUnavailable", (await missing.ReadJsonAsync()).GetProperty("code").GetString());
        // An option code the catalog does not have stops the copy the same way.
        using var missingCode = await designer.PostAsJsonAsync($"/api/forms/library/{TestFormStandardSource.MissingCodeKey}/copies", Add(Guid.NewGuid(), Prefix()), Ct);
        Assert.Equal(HttpStatusCode.Conflict, missingCode.StatusCode);
        Assert.Equal("Forms.Library.CatalogUnavailable", (await missingCode.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_standard_goes_to_the_folder_chosen_when_it_is_added_and_a_folder_that_is_gone_is_not_found()
    {
        using var designer = await OperatorAsync(["formtemplates.read", "formtemplates.manage"]);
        string Prefix() => "C" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        using var createdFolder = await designer.PostAsJsonAsync("/api/forms/folders", new { name = "Remitos " + Prefix() }, Ct);
        Assert.Equal(HttpStatusCode.OK, createdFolder.StatusCode);
        var folderId = (await createdFolder.ReadJsonAsync()).GetProperty("id").GetGuid();

        using var added = await designer.PostAsJsonAsync($"/api/forms/library/{TestFormStandardSource.Key}/copies",
            new { requestId = Guid.NewGuid(), name = new { es = "Remito en carpeta" }, prefix = Prefix(), folderId }, Ct);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var id = (await added.ReadJsonAsync()).GetProperty("definition").GetProperty("id").GetGuid();
        using var folders = await designer.GetAsync($"/api/forms/definitions/{id}/folders", Ct);
        Assert.Equal([folderId], (await folders.ReadJsonAsync()).GetProperty("folderIds").EnumerateArray().Select(value => value.GetGuid()));

        using var gone = await designer.PostAsJsonAsync($"/api/forms/library/{TestFormStandardSource.Key}/copies",
            new { requestId = Guid.NewGuid(), name = new { es = "Remito sin carpeta" }, prefix = Prefix(), folderId = Guid.NewGuid() }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("Forms.Folder.NotFound", (await gone.ReadJsonAsync()).GetProperty("code").GetString());
    }

    private static object Add(Guid requestId, string prefix) => new { requestId, name = new { es = "Remito propio " + prefix }, prefix };

    // The saved questions as the draft's input: their identity as both local id and field id, and what the editor sends back.
    private static JsonArray Inputs(JsonElement fields) => new(fields.EnumerateArray().Select(field =>
    {
        var input = new JsonObject
        {
            ["localId"] = field.GetProperty("fieldId").GetGuid().ToString(), ["fieldId"] = field.GetProperty("fieldId").GetGuid(),
            ["type"] = field.GetProperty("type").GetString(), ["label"] = JsonNode.Parse(field.GetProperty("label").GetRawText()),
            ["required"] = field.GetProperty("required").GetBoolean(),
        };
        foreach (var name in new[] { "maxLength", "rows", "repeated", "calculation", "toggles" })
            if (field.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null) input[name] = JsonNode.Parse(value.GetRawText());
        return (JsonNode?)input;
    }).ToArray());
}
