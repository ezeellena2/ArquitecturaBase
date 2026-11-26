using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>
/// Ayuda común de las pruebas HTTP de Formularios, partidas en cuatro clases por tema (borradores, publicación, respuestas
/// y reglas) para que cada una vaya a su colección de <see cref="ApiTestCollectionFactory"/> y corran a la vez. Cada
/// clase da la <see cref="ApiFactory"/> de su colección.
/// </summary>
public abstract class FormEndpointsTestBase
{
    private protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    private protected static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>La de la colección de cada clase: con ella arman actores, hosts y datos las ayudas de acá.</summary>
    private protected abstract ApiFactory Factory { get; }

    // Actores y borradores.
    private protected async Task<HttpClient> OperatorAsync(IReadOnlyCollection<string> codes, string? displayName = null)
    {
        var actor = await Factory.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>(); var name = "forms-" + Guid.NewGuid().ToString("N");
            await roles.CreateAsync(name, null, codes, Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("forms-operator")).Value, null, false, displayName, "es", Ct);
            await users.SetRolesAsync(user.Id, [name], Ct); return user.Id;
        });
        var client = Factory.CreateClient(); client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.ToString()); return client;
    }

    private protected Task<HttpClient> AdminAsync() => OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage,
        BusinessFormsPermissions.Publish, Permissions.Roles.Manage]);

    private protected static JsonObject Payload(string name = "Draft") => new()
    {
        ["requestId"] = Guid.NewGuid().ToString(), ["name"] = new JsonObject { ["es"] = name, ["en"] = name }, ["prefix"] = "T" + Guid.NewGuid().ToString("N")[..10],
        ["draft"] = new JsonObject { ["format"] = 1, ["culture"] = "es", ["submitLabel"] = new JsonObject { ["es"] = "Enviar", ["en"] = "Submit" },
            ["fields"] = new JsonArray(new JsonObject { ["localId"] = "first", ["type"] = "shortText", ["label"] = new JsonObject { ["es"] = "Nombre", ["en"] = "Name" },
                ["help"] = new JsonObject { ["es"] = "Tu nombre", ["en"] = "Your name" } }) },
    };

    private protected static async Task<JsonElement> CreateAsync(HttpClient client, JsonObject payload)
    {
        using var response = await client.PostAsJsonAsync("/api/forms/definitions", payload, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return await response.ReadJsonAsync();
    }

    private protected static JsonObject EditPayload(JsonElement saved)
    {
        var definition = saved.GetProperty("definition"); var payload = JsonNode.Parse(definition.GetRawText())!.AsObject();
        payload.Remove("id"); payload.Remove("prefixLocked"); payload.Remove("revision");
        payload["requestId"] = Guid.NewGuid().ToString(); payload["expectedRevision"] = definition.GetProperty("revision").GetInt64();
        var fields = payload["draft"]!["fields"]!.AsArray();
        for (var i = 0; i < fields.Count; i++)
        {
            fields[i]!["localId"] = "f" + i; fields[i]!.AsObject().Remove("key"); fields[i]!.AsObject().Remove("replacesFieldId");
            if (fields[i]!["options"] is JsonArray options)
                for (var j = 0; j < options.Count; j++) options[j]!["localId"] = "o" + j;
        }
        return payload;
    }

    private protected static JsonObject CheckPayload(JsonElement saved)
    {
        var payload = EditPayload(saved); payload.Remove("requestId"); payload.Remove("offerWhen"); return payload;
    }

    private protected static JsonObject RowsBlock(int minimum = 1, int maximum = 20) => new()
    {
        ["localId"] = "articulos", ["type"] = "group", ["required"] = minimum > 0,
        ["label"] = new JsonObject { ["es"] = "Artículos", ["en"] = "Items" },
        ["rows"] = new JsonObject { ["minRows"] = minimum, ["maxRows"] = maximum },
    };

    private protected static object Add(Guid requestId, string prefix) => new { requestId, name = new { es = "Remito propio " + prefix }, prefix };

    // Publicación, baja y eliminación.
    private protected Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> PublicationHost(TimeProvider? clock = null) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStructuredFormChannel>(); services.AddSingleton<IStructuredFormChannel, ReadyStructuredFormChannel>();
            if (clock is not null) { services.RemoveAll<TimeProvider>(); services.AddSingleton(clock); }
        }));

    private protected static async Task<JsonObject> PublicationAttemptAsync(HttpClient client, Guid id, System.Text.Json.JsonElement saved)
    {
        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var checkedResult = await check.ReadJsonAsync(); Assert.True(checkedResult.GetProperty("canAccept").GetBoolean());
        var attempt = CheckPayload(saved); attempt["requestId"] = Guid.NewGuid().ToString();
        attempt["checkToken"] = checkedResult.GetProperty("checkToken").GetString(); return attempt;
    }

    private protected static async Task<Guid> CompletePublicationAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host,
        Guid operationId, Guid definitionId)
    {
        using var scope = host.Services.CreateScope();
        var dispatch = scope.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>();
        var work = (await dispatch.ReserveAsync(operationId, Ct)).Value!;
        var published = new StructuredFormPublicationResponse(work.OperationId, definitionId, work.VersionId,
            work.Manifest.PublicationFingerprint, "Published", "Verify", [], null, false);
        Assert.True((await dispatch.CompleteAsync(work, [new("test", published)], Ct)).IsSuccess);
        return work.VersionId;
    }

    private protected static async Task<JsonElement> CaptureContractAsync(HttpResponseMessage response, HttpStatusCode expected, string scenario, string name)
    {
        Assert.Equal(expected, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(Ct);
        using var parsed = JsonDocument.Parse(body);
        var json = parsed.RootElement.Clone();
        Assert.True(!json.TryGetProperty("checkToken", out var ticket) || ticket.ValueKind == JsonValueKind.Null);
        var output = Environment.GetEnvironmentVariable("FORMS_HTTP_CONTRACT_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            var directory = Path.Combine(output, scenario);
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, name + ".json"), body, Ct);
        }
        return json;
    }

    private protected static async Task<(Guid Id,Guid VersionId,JsonElement Saved)> PublishSuggestionFormAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host,HttpClient client,string name)
    {
        var saved=await CreateAsync(client,Payload(name));var id=saved.GetProperty("definition").GetProperty("id").GetGuid();var attempt=await PublicationAttemptAsync(client,id,saved);
        using var accepted=await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications",attempt,Ct);Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);
        var version=await CompletePublicationAsync(host,(await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid(),id);return(id,version,saved);
    }

    private protected static HttpClient DeactivationClient(WebApplicationFactory<Program> host, HttpClient actor, string culture)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        return client;
    }

    private protected static object DeactivationSuggestion(Guid destinationId, Guid fieldId, bool isActive = true) => new
    {
        id = Guid.NewGuid(), destinationDefinitionId = destinationId, mode = "Always", when = (string?)null, isActive,
        conditions = Array.Empty<object>(), mappings = new[] { new { source = new { kind = "RecordNumber" }, destinationFieldId = fieldId } }
    };

    private protected static async Task<JsonElement> SaveDeactivationSuggestionsAsync(HttpClient client, Guid definitionId,
        long expectedRevision, params object[] suggestions)
    {
        using var saved = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/suggestions", new { expectedRevision, suggestions }, Ct);
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        return await saved.ReadJsonAsync();
    }

    private protected static async Task<HttpResponseMessage> DeleteDefinitionAsync(HttpClient client, Guid definitionId, long expectedRevision)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/forms/definitions/{definitionId}")
        { Content = JsonContent.Create(new { expectedRevision }) };
        return await client.SendAsync(request, Ct);
    }

    // Respuestas, planilla y bloques.
    private protected static CanonicalFormField Field(string type, bool required, IReadOnlyList<CanonicalFormChoice>? options = null)
    {
        var id = Guid.NewGuid();
        return new(id, "field_" + id.ToString("N"), type,
            new Dictionary<string, string> { ["es"] = type, ["en"] = type }, null, required,
            type == "shortText" ? 80 : type == "longText" ? 600 : null, options);
    }

    // What each question receives, null where the key is absent; a key with a null value would not be the same bytes.
    private protected static string?[] Received(JsonElement questions) => questions.EnumerateArray()
        .Select(question => question.TryGetProperty("receives", out var receives) ? receives.GetString() ?? "null" : null).ToArray();

    private protected Task<(Guid Id, string Prefix, CanonicalFormField[] Fields, IReadOnlyList<Guid> RecordIds)> SeedSheetAsync(HttpClient reader, int count, params CanonicalFormField[] extra) =>
        SeedSheetAsync(reader, count, null, extra);

    private protected Task<(Guid Id, string Prefix, CanonicalFormField[] Fields, IReadOnlyList<Guid> RecordIds)> SeedSheetAsync(HttpClient reader, int count, Guid? prefixIdentity, params CanonicalFormField[] extra) =>
        SeedSheetCoreAsync(reader, count, prefixIdentity, extra);

    private protected async Task<(Guid Id, string Prefix, CanonicalFormField[] Fields, IReadOnlyList<Guid> RecordIds)> SeedSheetCoreAsync(HttpClient reader, int count, Guid? prefixIdentity, CanonicalFormField[] extra, string? onlyCulture = null, bool secondaryButtonMissing = false)
    {
        var actor = Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var prefix = "T" + (prefixIdentity ?? Guid.NewGuid()).ToString("N")[..8].ToUpperInvariant();
        var fields = new[] { Field("decimal", false), Field("longText", false), Field("boolean", false), Field("date", false) };
        if (extra.Length != 0) fields = [..fields, ..extra];
        var definition = BusinessFormDefinition.Create();
        var version = BusinessFormVersion.Create(definition.Id, 1, "test-sheet");
        var culture = onlyCulture ?? "es";
        var names = new Dictionary<string, string> { ["es"] = prefix + " Planilla", ["en"] = prefix + " Sheet" };
        var buttons = new Dictionary<string, string> { ["es"] = "Enviar", ["en"] = "Submit" };
        if (onlyCulture is not null && !secondaryButtonMissing)
        {
            names = names.Where(pair => pair.Key == culture).ToDictionary(pair => pair.Key, pair => pair.Value);
            buttons = buttons.Where(pair => pair.Key == culture).ToDictionary(pair => pair.Key, pair => pair.Value);
            fields = fields.Select(field => field with
            {
                Label = new Dictionary<string, string> { [culture] = field.Label[culture] }
            }).ToArray();
        }
        if (secondaryButtonMissing) buttons = new Dictionary<string, string> { [culture] = buttons[culture] };
        var name = JsonSerializer.Serialize(names);
        var submit = JsonSerializer.Serialize(buttons);
        Assert.True(definition.SetDraft(1, names[culture], name, prefix, culture, submit, JsonSerializer.Serialize(new FormQuestionSchema(1, fields), SerializerOptions), fields.Length, true).IsSuccess);
        version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, fields), SerializerOptions));
        version.SealPresentation(name, submit, culture, fields.Length);
        var ids = new List<Guid>();
        await Factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormDefinition>().Add(definition);
            foreach (var field in fields) db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definition.Id, field.Key, field.Type));
            await db.SaveChangesAsync(Ct);
            db.Set<BusinessFormVersion>().Add(version);
            db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(Factory, definition, version, actor, onlyCulture is null ? ["es", "en"] : [culture]));
            Assert.True(definition.ActivateVersion(null, version.Id).IsSuccess); await db.SaveChangesAsync(Ct);
            var now = DateTime.SpecifyKind(new DateTime(2026, 10, 16, 12, 0, 0), DateTimeKind.Utc);
            for (var index = 0; index < count; index++)
            {
                var receiptId = Guid.NewGuid();
                var values = JsonSerializer.Serialize(new object[] { new { fieldId = fields[0].FieldId, value = index }, new { fieldId = fields[1].FieldId, value = "private " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) }, new { fieldId = fields[2].FieldId, value = index % 2 == 0 }, new { fieldId = fields[3].FieldId, value = "2026-10-16" } }, SerializerOptions);
                var record = BusinessFormRecord.Create(definition.Id, version.Id, definition.AllocateRecordNumber(), actor, receiptId, now, values);
                db.Set<BusinessFormRecord>().Add(record); ids.Add(record.Id);
                db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(receiptId, definition.Id, version.Id, "test", record.Id, record.Number, now));
                db.Set<BusinessFormRecordValue>().AddRange(BusinessFormRecordValue.Create(record.Id, definition.Id, fields[0].FieldId, "decimal", decimalValue: index),
                    BusinessFormRecordValue.Create(record.Id, definition.Id, fields[1].FieldId, "longText", textValue: "private " + index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    BusinessFormRecordValue.Create(record.Id, definition.Id, fields[2].FieldId, "boolean", booleanValue: index % 2 == 0),
                    BusinessFormRecordValue.Create(record.Id, definition.Id, fields[3].FieldId, "date", dateValue: new DateOnly(2026, 10, 16)));
            }
            await db.SaveChangesAsync(Ct);
        });
        return (definition.Id, prefix, fields, ids);
    }

    private protected static async Task<JsonElement> ReadRecordNavigationAsync(HttpClient reader, Guid definitionId,
        Guid recordId, object filter, string? captureName = null)
    {
        using var response = await reader.PostAsJsonAsync(
            $"/api/forms/definitions/{definitionId}/records/{recordId}/navigation", filter, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (captureName is not null) await ResponseContractCapture.WriteAsync(response, captureName, Ct);
        return await response.ReadJsonAsync();
    }

    private protected static void AssertRecordNavigation(JsonElement response, Guid recordId, Guid? previousId,
        Guid? nextId, int position, int totalCount)
    {
        Assert.Equal(new[] { "nextId", "position", "previousId", "recordId", "totalCount" },
            response.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(recordId, response.GetProperty("recordId").GetGuid());
        Assert.Equal(previousId, response.GetProperty("previousId").ValueKind == JsonValueKind.Null ? (Guid?)null : response.GetProperty("previousId").GetGuid());
        Assert.Equal(nextId, response.GetProperty("nextId").ValueKind == JsonValueKind.Null ? (Guid?)null : response.GetProperty("nextId").GetGuid());
        Assert.Equal(position, response.GetProperty("position").GetInt32());
        Assert.Equal(totalCount, response.GetProperty("totalCount").GetInt32());
    }

    private protected async Task<(Guid DefinitionId, Guid VersionId)> SeedVersionAsync(Guid actor, IReadOnlyList<CanonicalFormField> fields)
    {
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, "Renglones", "{\"es\":\"Renglones\",\"en\":\"Rows\"}", "G" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), "es",
            "{\"es\":\"Enviar\",\"en\":\"Submit\"}", JsonSerializer.Serialize(new FormQuestionSchema(1, fields), SerializerOptions),
            fields.Count(field => field.Type != "group"), true).IsSuccess);
        var version = BusinessFormVersion.Create(definition.Id, 1, new string('a', 64));
        version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, fields), SerializerOptions));
        version.SealPresentation("{\"es\":\"Renglones\",\"en\":\"Rows\"}", "{\"es\":\"Enviar\",\"en\":\"Submit\"}", "es", fields.Count(field => field.Type != "group"));
        var operation = FormPublishedVersionFixture.Create(Factory, definition, version, actor, ["es", "en"]);
        await Factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormDefinition>().Add(definition);
            foreach (var field in fields) db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definition.Id, field.Key, field.Type));
            await db.SaveChangesAsync(Ct);
            db.Set<BusinessFormVersion>().Add(version);
            db.Set<BusinessFormPublicationOperation>().Add(operation);
            await db.SaveChangesAsync(Ct);
            Assert.True(definition.ActivateVersion(null, version.Id).IsSuccess);
            await db.SaveChangesAsync(Ct);
        });
        return (definition.Id, version.Id);
    }

    private protected async Task<HttpClient> RowsReaderAsync(string culture)
    {
        var reader = await OperatorAsync([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage,
            BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport,
            BusinessFormsPermissions.RulesManage]);
        reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        return reader;
    }

    /// <summary>An item to capture: the option of the catalog by its local id, the quantity and an optional note.</summary>
    private protected sealed record RowsItem(string Option, string Quantity, string? Note = null);

    /// <summary>The form of the block and its captured responses; `Keys` holds the key of each question by its FieldId.</summary>
    private protected sealed record RowsReadForm(Guid DefinitionId, Guid Supplier, Guid Group, Guid Item, Guid Quantity, Guid Note, Guid Seed, Guid Fertilizer,
        IReadOnlyList<Guid> Records, Guid VersionId, IReadOnlyDictionary<Guid, string> Keys);

    /// <summary>A form with a block of 0 to 3 items (an option of a catalog, a quantity and a long note) and its responses, captured for real.</summary>
    private protected async Task<RowsReadForm> SeedRowsReadAsync(HttpClient reader, params RowsItem[][] responses)
    {
        static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
        var actorId = Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        using var created = await reader.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Insumos " + Guid.NewGuid().ToString("N"), en = "Supplies" }, description = new { es = "Lista", en = "List" },
            items = new[]
            {
                new { localId = "seed", code = "SEED", text = new { es = "Semilla", en = "Seed" }, isActive = true },
                new { localId = "fertilizer", code = "FERT", text = new { es = "Fertilizante", en = "Fertilizer" }, isActive = true },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalog = await created.ReadJsonAsync();
        var catalogId = catalog.GetProperty("catalog").GetProperty("id").GetGuid();
        var options = catalog.GetProperty("itemIdentityMap");
        var supplier = Field("shortText", true) with { Label = Text("Proveedor", "Supplier") };
        var group = Field("group", false) with { Label = Text("Artículos", "Items"), Rows = new(0, 3) };
        var item = Field("choice", true) with { Label = Text("Artículo", "Product"), Source = new(catalogId), Repeated = true };
        var quantity = Field("decimal", true) with { Label = Text("Cantidad", "Quantity"), Repeated = true };
        var note = Field("longText", false) with { Label = Text("Observación", "Note"), Repeated = true };
        var fields = new[] { supplier, group, item, quantity, note };
        var form = await SeedVersionAsync(actorId, fields);
        var rows = new RowsReadForm(form.DefinitionId, supplier.FieldId, group.FieldId, item.FieldId, quantity.FieldId, note.FieldId,
            options.GetProperty("seed").GetGuid(), options.GetProperty("fertilizer").GetGuid(), [], form.VersionId,
            fields.ToDictionary(field => field.FieldId, field => field.Key));
        return rows with { Records = await CaptureRowsAsync(reader, rows, responses) };
    }

    /// <summary>
    /// Captures these responses of the form for real, through the sink and as the reader, with «Proveedor del norte» as the
    /// supplier: their ids, in order. Apart from the seed, so the rules can be saved before.
    /// </summary>
    private protected async Task<IReadOnlyList<Guid>> CaptureRowsAsync(HttpClient reader, RowsReadForm rows, params RowsItem[][] responses)
    {
        var actorId = Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var options = new Dictionary<string, Guid> { ["seed"] = rows.Seed, ["fertilizer"] = rows.Fertilizer };
        var records = new List<Guid>();
        foreach (var response in responses)
        {
            using var scope = Factory.Services.CreateScope();
            var preparation = await scope.ServiceProvider.GetRequiredService<IStructuredFormSource>()
                .PrepareAsync(actorId, "es", rows.DefinitionId, rows.VersionId, false, Ct);
            var items = response.Select(value => value.Note is null
                ? new Dictionary<string, string> { [rows.Keys[rows.Item]] = options[value.Option].ToString("D"), [rows.Keys[rows.Quantity]] = value.Quantity }
                : new Dictionary<string, string> { [rows.Keys[rows.Item]] = options[value.Option].ToString("D"), [rows.Keys[rows.Quantity]] = value.Quantity, [rows.Keys[rows.Note]] = value.Note });
            var captured = await scope.ServiceProvider.GetRequiredService<IStructuredRecordSink>().CaptureAsync(new(Guid.NewGuid(), rows.DefinitionId,
                rows.VersionId, actorId, Factory.Clock.GetUtcNow().UtcDateTime, new Dictionary<string, string>
                {
                    [rows.Keys[rows.Supplier]] = "Proveedor del norte", [rows.Keys[rows.Group]] = JsonSerializer.Serialize(items),
                }, [Assert.Single(preparation.Catalogs)]), Ct);
            Assert.Equal("Completed", captured.State);
            records.Add(captured.RecordId!.Value);
        }
        return records;
    }

    // Reglas, acciones y aprobaciones.
    private protected async Task<(Guid TaskId, Guid RecordId, Guid DefinitionId, Guid RuleSetId, Guid DeciderId)> SeedApprovalAsync(HttpClient author, HttpClient decider, int hour, TimeSpan? approvalDelay = null, string? originalCulture = null, bool secondaryButtonMissing = false,
        bool authorMayDecide = false, bool reasonOptional = false)
    {
        var sheet = await SeedSheetCoreAsync(author, 0, null, [], originalCulture, secondaryButtonMissing);
        var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var deciderId = Guid.Parse(decider.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        return await Factory.ExecuteDbContextAsync(async db =>
        {
            var roleId = await db.UserRoles.Where(link => link.UserId == deciderId).Select(link => link.RoleId).SingleAsync(Ct);
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == sheet.Id, Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(value => value.Id == definition.ActiveVersionId, Ct);
            var approval = new FormRuleAction(Guid.NewGuid(), "RequestApproval", roleId, null, false, AuthorMayDecide: authorMayDecide, ReasonOptional: reasonOptional);
            var received = new FormRule(Guid.NewGuid(), "Received", [], [approval]);
            var notify = new FormRuleAction(Guid.NewGuid(), "NotifyAuthor", null, new Dictionary<string, string> { { version.Culture!, version.Culture == "en" ? "Response {número}: {motivo}" : "Respuesta {número}: {motivo}" } }, false);
            var rules = new[] { received, new FormRule(Guid.NewGuid(), "Approved", [], [notify]), new FormRule(Guid.NewGuid(), "Rejected", [], [notify with { Id = Guid.NewGuid() }]) };
            var ruleSet = BusinessFormRuleSet.Create(definition.Id, 1, version.Culture!, FormDraftPolicy.Serialize(rules), true, version.SchemaJson); db.Set<BusinessFormRuleSet>().Add(ruleSet); await db.SaveChangesAsync(Ct); definition.SetActiveRuleSetId(ruleSet.Id);
            var at = new DateTime(2026, 10, 16, hour, 0, 0, DateTimeKind.Utc);
            var record = BusinessFormRecord.Create(definition.Id, version.Id, definition.AllocateRecordNumber(), authorId, Guid.NewGuid(), at, "[]", ruleSet.Id); record.MarkPendingApproval(); db.Set<BusinessFormRecord>().Add(record);
            db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(record.SourceReceiptId, definition.Id, version.Id, "test-approval", record.Id, record.Number, at));
            var config = new FormActionSnapshot(approval.RoleId, null, null, [], ApprovalAuthorMayDecide: authorMayDecide, ApprovalReasonOptional: reasonOptional);
            var run = BusinessFormActionRun.Create(definition.Id, record.Id, ruleSet.Id, received.Id, approval.Id, "Received", "RequestApproval", 0, null, FormDraftPolicy.Serialize(config), true, at); db.Set<BusinessFormActionRun>().Add(run);
            var task = BusinessFormTask.Open(definition.Id, record.Id, ruleSet.Id, run.Id, roleId, authorId, at.Add(approvalDelay ?? TimeSpan.Zero), authorMayDecide, reasonOptional); db.Set<BusinessFormTask>().Add(task); await db.SaveChangesAsync(Ct);
            return (task.Id, record.Id, definition.Id, ruleSet.Id, deciderId);
        });
    }

    private protected static async Task<T> InActionScopeAsync<T>(IServiceProvider services,Func<IServiceProvider,Task<T>> action)
    {await using var scope=services.CreateAsyncScope();return await action(scope.ServiceProvider);}

    private protected WebApplicationFactory<Program> IssuerHost(TestRecordIssuer issuer) => Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IStructuredRecordIssuer>();
        services.AddSingleton<IStructuredRecordIssuer>(issuer);
    }));

    private protected static HttpClient InHost(WebApplicationFactory<Program> host, HttpClient source)
    {
        var client = host.CreateClient();
        foreach (var header in source.DefaultRequestHeaders)
            client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        return client;
    }

    // A form copied from the standard of the issuer (its first question, a decimal, holds «first») with an «IssueDocument» rule.
    private protected async Task<(Guid DefinitionId, Guid VersionId, Guid FieldId)> SeedIssueFormAsync(HttpClient actorClient)
    {
        var sheet = await SeedSheetAsync(actorClient, 0);
        return await Factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(item => item.Id == sheet.Id, Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(item => item.Id == definition.ActiveVersionId, Ct);
            definition.RecordStandard(TestRecordIssuer.Key, 1, FormDraftPolicy.Serialize(new Dictionary<string, Guid> { ["first"] = sheet.Fields[0].FieldId }));
            var action = new FormRuleAction(Guid.NewGuid(), "IssueDocument", null, null, false, TestRecordIssuer.Key);
            var rule = new FormRule(Guid.NewGuid(), "Received", [], [action]);
            var ruleSet = BusinessFormRuleSet.Create(definition.Id, 1, "es", FormDraftPolicy.Serialize(new[] { rule }), false, version.SchemaJson);
            db.Set<BusinessFormRuleSet>().Add(ruleSet);
            await db.SaveChangesAsync(Ct);
            definition.SetActiveRuleSetId(ruleSet.Id);
            await db.SaveChangesAsync(Ct);
            return (definition.Id, version.Id, sheet.Fields[0].FieldId);
        });
    }

    private protected Task<BusinessFormActionRun> RunAsync(Guid runId) =>
        Factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>().AsNoTracking().SingleAsync(run => run.Id == runId, Ct));

    // A form copied from the issuer's standard, with an «IssueDocument» rule, a response and its run waiting.
    // Loaded in the app by default (it was confirmed before sending); one from a channel waits for «¿La emito?».
    private protected async Task<(Guid RecordId, Guid RunId)> SeedIssueRunAsync(HttpClient actorClient, bool enteredInApp = true)
    {
        var (definitionId, _, _) = await SeedIssueFormAsync(actorClient);
        var actor = Guid.Parse(actorClient.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        return await Factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(item => item.Id == definitionId, Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(item => item.Id == definition.ActiveVersionId, Ct);
            var ruleSet = await db.Set<BusinessFormRuleSet>().SingleAsync(item => item.Id == definition.ActiveRuleSetId, Ct);
            var rule = FormDraftPolicy.Read<FormRule[]>(ruleSet.RulesJson).Single();
            var action = rule.Actions.Single();
            var now = Factory.Clock.GetUtcNow().UtcDateTime;
            var record = BusinessFormRecord.Create(definition.Id, version.Id, definition.AllocateRecordNumber(), actor, Guid.NewGuid(), now, "[]", ruleSet.Id,
                "es", enteredInApp);
            db.Set<BusinessFormRecord>().Add(record);
            db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(record.SourceReceiptId, definition.Id, version.Id, "test-issue", record.Id, record.Number, now));
            var run = BusinessFormActionRun.Create(definition.Id, record.Id, ruleSet.Id, rule.Id, action.Id, "Received", "IssueDocument", 0, null,
                FormDraftPolicy.Serialize(new FormActionSnapshot(null, null, null, [], IssuerId: TestRecordIssuer.Key)), true, now);
            db.Set<BusinessFormActionRun>().Add(run);
            await db.SaveChangesAsync(Ct);
            return (record.Id, run.Id);
        });
    }
}
