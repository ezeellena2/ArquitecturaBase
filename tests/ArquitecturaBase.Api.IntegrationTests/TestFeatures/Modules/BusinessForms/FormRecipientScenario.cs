using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormRecipientScenario(WebApplicationFactory<Program> host) : IAsyncDisposable
{
    public WebApplicationFactory<Program> Host { get; } = host;
    public Guid ManagerId { get; private set; }
    // Administra catálogos sin leer formularios (sin formtemplates.*).
    public Guid CatalogEditorId { get; private set; }
    // Administra formularios sin leer catálogos (sin formcatalogs.read).
    public Guid FormsOnlyId { get; private set; }
    public Guid FirstId { get; private set; }
    public Guid SecondId { get; private set; }
    public Guid MissingId { get; private set; }
    public Guid CatalogId { get; private set; }
    public Guid ZoneCatalogId { get; private set; }
    public Guid CentralId { get; private set; }
    public Guid SouthId { get; private set; }
    public Guid ZoneFieldId { get; private set; }
    public Guid KindFieldId { get; private set; }
    public Guid DataFieldId { get; private set; }
    public Guid AttributeId { get; private set; }
    public Guid FirstItemId { get; private set; }
    public Guid SecondItemId { get; private set; }
    public JsonElement Saved { get; private set; }
    public Guid DefinitionId => Saved.GetProperty("definition").GetProperty("id").GetGuid();
    public Guid QuestionId => Saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("fieldId").GetGuid();

    public static async Task<FormRecipientScenario> CreateAsync(ApiFactory factory, CancellationToken ct)
    {
        var connectionString = factory.NewDatabaseConnectionString("form_recipient");
        var scenario = new FormRecipientScenario(factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:appdb", connectionString)));
        await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync(ct));
        await scenario.Host.Services.SeedDatabaseAsync(ct);
        await scenario.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            var managerRole = "recipient-manager-" + Guid.NewGuid().ToString("N");
            await roles.CreateAsync(managerRole, null, [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish,
                BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage, BusinessFormsPermissions.PersonDataManage, Permissions.Users.Manage], ct);
            var catalogEditorRole = "recipient-catalog-editor-" + Guid.NewGuid().ToString("N");
            await roles.CreateAsync(catalogEditorRole, null, [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage], ct);
            var formsOnlyRole = "recipient-forms-only-" + Guid.NewGuid().ToString("N");
            await roles.CreateAsync(formsOnlyRole, null, [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage], ct);
            var recipientRole = "recipient-subject-" + Guid.NewGuid().ToString("N");
            await roles.CreateAsync(recipientRole, null, [BusinessFormsPermissions.RecordsCreate], ct);
            var users = services.GetRequiredService<IUserRepository>();
            async Task<Guid> Add(string name, string role)
            {
                var account = await users.CreateAsync(Email.Create(TestEmails.Unique("recipient")).Value, null, false, name, "es", ct);
                await users.SetRolesAsync(account.Id, [role], ct);
                return account.Id;
            }
            scenario.ManagerId = await Add("Administración de formularios", managerRole);
            scenario.CatalogEditorId = await Add("Edición de catálogos", catalogEditorRole);
            scenario.FormsOnlyId = await Add("Edición de formularios", formsOnlyRole);
            scenario.FirstId = await Add("Juan Gómez", recipientRole);
            scenario.SecondId = await Add("María López", recipientRole);
            scenario.MissingId = await Add("Pablo Sosa", recipientRole);
            return true;
        }, ct);
        using var client = scenario.Client();
        var source = await PostAsync(client, "/api/forms/catalogs", new
        {
            name = new { es = "Zonas", en = "Zones" }, description = new { es = "Zonas", en = "Zones" },
            items = new[] { Item("central", "CENTRAL", "Centro", "Central"), Item("south", "SOUTH", "Sur", "South") },
        }, HttpStatusCode.Created, ct);
        scenario.ZoneCatalogId = source.GetProperty("catalog").GetProperty("id").GetGuid();
        scenario.CentralId = source.GetProperty("itemIdentityMap").GetProperty("central").GetGuid();
        scenario.SouthId = source.GetProperty("itemIdentityMap").GetProperty("south").GetGuid();
        var catalog = await PostAsync(client, "/api/forms/catalogs", new
        {
            name = new { es = "Proveedores", en = "Suppliers" }, description = new { es = "Proveedores", en = "Suppliers" },
            fields = new[] { scenario.Field("zone", null, "catalog", "filter"), scenario.Field("kind", null, "text", "filter"), scenario.Field("tax_id", null, "text", "data") },
            items = new[] { Item("first", "FIRST", "Proveedor Centro", "Central supplier"), Item("second", "SECOND", "Proveedor Sur", "South supplier"), Item("other", "OTHER", "Otro proveedor", "Other supplier") },
        }, HttpStatusCode.Created, ct);
        scenario.CatalogId = catalog.GetProperty("catalog").GetProperty("id").GetGuid();
        scenario.ZoneFieldId = catalog.GetProperty("fieldIdentityMap").GetProperty("zone").GetGuid();
        scenario.KindFieldId = catalog.GetProperty("fieldIdentityMap").GetProperty("kind").GetGuid();
        scenario.DataFieldId = catalog.GetProperty("fieldIdentityMap").GetProperty("tax_id").GetGuid();
        scenario.FirstItemId = catalog.GetProperty("itemIdentityMap").GetProperty("first").GetGuid();
        scenario.SecondItemId = catalog.GetProperty("itemIdentityMap").GetProperty("second").GetGuid();
        var otherId = catalog.GetProperty("itemIdentityMap").GetProperty("other").GetGuid();
        using var updated = await client.PutAsJsonAsync($"/api/forms/catalogs/{scenario.CatalogId}", new
        {
            expectedRevision = 1, name = new { es = "Proveedores", en = "Suppliers" }, description = new { es = "Proveedores", en = "Suppliers" },
            fields = new[] { scenario.Field("zone", scenario.ZoneFieldId, "catalog", "filter"), scenario.Field("kind", scenario.KindFieldId, "text", "filter"), scenario.Field("tax_id", scenario.DataFieldId, "text", "data") },
            presentation = new { title = new[] { "text" }, description = new[] { scenario.DataFieldId.ToString("D") } },
            items = new[] { scenario.Values("first", scenario.FirstItemId, "FIRST", "Proveedor Centro", "Central supplier", scenario.CentralId, "cold"),
                scenario.Values("second", scenario.SecondItemId, "SECOND", "Proveedor Sur", "South supplier", scenario.SouthId, "cold"),
                scenario.Values("other", otherId, "OTHER", "Otro proveedor", "Other supplier", scenario.CentralId, "hot") },
        }, ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var attribute = await PostAsync(client, "/api/forms/person-attributes", new
        { key = "zone", name = new { es = "Zona", en = "Zone" }, type = "Catalog", sourceCatalogId = scenario.ZoneCatalogId }, HttpStatusCode.OK, ct);
        scenario.AttributeId = attribute.GetProperty("id").GetGuid();
        foreach (var (person, option) in new[] { (scenario.FirstId, scenario.CentralId), (scenario.SecondId, scenario.SouthId) })
        {
            using var data = await client.PutAsJsonAsync($"/api/forms/person-data/{person}", new
            { expectedRevision = (long?)null, values = new Dictionary<string, string> { [scenario.AttributeId.ToString("D")] = option.ToString("D") } }, ct);
            Assert.Equal(HttpStatusCode.OK, data.StatusCode);
        }
        scenario.Saved = await PostAsync(client, "/api/forms/definitions", scenario.CreateDraft(), HttpStatusCode.Created, ct);
        return scenario;
    }

    public Task<Guid> PublishForPreparationAsync(ApiFactory factory, CancellationToken ct) => ScopeAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == DefinitionId, ct);
        var questions = JsonSerializer.Deserialize<FormQuestionSchema>(definition.SchemaJson, JsonSerializerOptions.Web)!.Fields;
        var schema = JsonSerializer.Serialize(new FormVersionSchema(1, questions), JsonSerializerOptions.Web);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(schema)));
        var version = BusinessFormVersion.Create(definition.Id, 1, hash);
        version.SealSchema(schema);
        version.SealPresentation(definition.NameJson, definition.SubmitLabelJson, definition.Culture, questions.Count);
        foreach (var field in questions) db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definition.Id, field.Key, field.Type));
        await db.SaveChangesAsync(ct);
        db.Set<BusinessFormVersion>().Add(version);
        db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(factory, definition, version, ManagerId, ["es", "en"]));
        await db.SaveChangesAsync(ct);
        Assert.True(definition.ActivateVersion(null, version.Id).IsSuccess);
        await db.SaveChangesAsync(ct);
        return version.Id;
    });

    public void RememberSaved(JsonElement saved) => Saved = saved;
    public async Task<JsonElement> SaveDraftAsync(Action<JsonObject> change, CancellationToken ct)
    {
        var payload = CheckPayload();
        payload["requestId"] = Guid.NewGuid().ToString("D");
        change(payload);
        using var client = Client();
        using var response = await client.PutAsJsonAsync($"/api/forms/definitions/{DefinitionId}/draft", payload, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await response.ReadJsonAsync();
        RememberSaved(saved);
        return saved;
    }
    public static void DropMatch(JsonObject payload, Guid fieldId)
    {
        var match = payload["draft"]!["fields"]![0]!["source"]!["match"]!.AsArray();
        match.Remove(match.Single(filter => Guid.Parse(filter!["fieldId"]!.GetValue<string>()) == fieldId));
    }
    // Un uso de campo tal como lo devuelven el detalle del catálogo (usedBy) y el 409 FieldInUse (uses).
    public static FormCatalogFieldUsageResponse FieldUse(JsonElement use) => new(
        use.GetProperty("definitionId").ValueKind == JsonValueKind.Null ? null : use.GetProperty("definitionId").GetGuid(),
        use.GetProperty("formName").GetString(), use.GetProperty("question").GetString(),
        use.GetProperty("questionId").ValueKind == JsonValueKind.Null ? null : use.GetProperty("questionId").GetGuid(),
        use.GetProperty("stage").GetString()!);
    public JsonObject CatalogEdit(long revision) => JsonSerializer.SerializeToNode(new
    {
        expectedRevision = revision, name = new { es = "Proveedores", en = "Suppliers" }, description = new { es = "Proveedores", en = "Suppliers" },
        fields = new[] { Field("zone", ZoneFieldId, "catalog", "filter"), Field("kind", KindFieldId, "text", "filter"), Field("tax_id", DataFieldId, "text", "data") },
        presentation = new { title = new[] { "text" }, description = new[] { DataFieldId.ToString("D") } }, items = Array.Empty<object>(),
    }, JsonSerializerOptions.Web)!.AsObject();

    public JsonObject Source() => new()
    {
        ["catalog"] = CatalogId.ToString("D"),
        ["match"] = new JsonArray(new JsonObject { ["fieldId"] = ZoneFieldId.ToString("D"), ["personAttributeId"] = AttributeId.ToString("D") },
            new JsonObject { ["fieldId"] = KindFieldId.ToString("D"), ["value"] = "cold" }),
        ["presentation"] = new JsonObject { ["title"] = new JsonArray("code"), ["description"] = new JsonArray() },
    };
    public JsonObject CheckPayload() => new()
    {
        ["expectedRevision"] = Saved.GetProperty("definition").GetProperty("revision").GetInt64(),
        ["name"] = JsonNode.Parse(Saved.GetProperty("definition").GetProperty("name").GetRawText()),
        ["prefix"] = Saved.GetProperty("definition").GetProperty("prefix").GetString(),
        ["draft"] = EditableDraft(),
    };
    public JsonObject EditableDraft()
    {
        var draft = JsonNode.Parse(Saved.GetProperty("definition").GetProperty("draft").GetRawText())!.AsObject();
        var field = draft["fields"]![0]!.AsObject(); field["localId"] = "supplier"; field.Remove("key");
        return draft;
    }
    private JsonObject CreateDraft() => new()
    {
        ["requestId"] = Guid.NewGuid().ToString("D"), ["prefix"] = "RF" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
        ["name"] = new JsonObject { ["es"] = "Pedir repuesto", ["en"] = "Request a spare part" },
        ["draft"] = new JsonObject { ["format"] = 1, ["culture"] = "es", ["submitLabel"] = new JsonObject { ["es"] = "Enviar", ["en"] = "Submit" },
            ["fields"] = new JsonArray(new JsonObject { ["localId"] = "supplier", ["type"] = "choice", ["required"] = true,
                ["label"] = new JsonObject { ["es"] = "Proveedor", ["en"] = "Supplier" }, ["source"] = Source() }) },
    };
    private object Field(string key, Guid? id, string type, string role) => new
    {
        localId = id is null ? key : null, id, key, name = new { es = key, en = key }, type,
        sourceCatalogId = type == "catalog" ? ZoneCatalogId : (Guid?)null, required = false, unique = false, min = (string?)null, max = (string?)null,
        maxLength = type == "text" ? 300 : (int?)null, role, position = key == "zone" ? 0 : key == "kind" ? 1 : 2, isActive = true,
    };
    private object Values(string localId, Guid itemId, string code, string es, string en, Guid zone, string kind) => new
    {
        localId, itemId, code, text = new { es, en }, detail = new { es = "Dato privado", en = "Private detail" }, isActive = true,
        fields = new Dictionary<string, string> { [ZoneFieldId.ToString("D")] = zone.ToString("D"), [KindFieldId.ToString("D")] = kind, [DataFieldId.ToString("D")] = "30712345678" },
    };
    private static object Item(string localId, string code, string es, string en) => new { localId, code, text = new { es, en }, isActive = true };
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object payload, HttpStatusCode status, CancellationToken ct)
    { using var response = await client.PostAsJsonAsync(path, payload, ct); Assert.Equal(status, response.StatusCode); return await response.ReadJsonAsync(); }
    public HttpClient Client(string culture = "es", Guid? actor = null)
    { var client = Host.CreateClient(); client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, (actor ?? ManagerId).ToString("D")); client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture); return client; }
    public async Task<T> ScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    { await using var scope = Host.Services.CreateAsyncScope(); return await action(scope.ServiceProvider); }
    private Task<T> InTransactionAsync<T>(Func<IServiceProvider, Task<T>> action, CancellationToken ct) => ScopeAsync(async services =>
    {
        T value = default!; var saved = await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
        { token.ThrowIfCancellationRequested(); value = await action(services); return Result.Success(); }, CommitPolicy.OnSuccess, ct);
        Assert.True(saved.IsSuccess); return value;
    });
    public async ValueTask DisposeAsync()
    { await ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync()); await Host.DisposeAsync(); }
}
