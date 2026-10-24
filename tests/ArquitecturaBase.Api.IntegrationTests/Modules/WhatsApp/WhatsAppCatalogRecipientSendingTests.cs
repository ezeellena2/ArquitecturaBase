using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFormSendingTests
{
    [Fact]
    public async Task Real_recipient_catalogs_send_only_the_chosen_presentation_block_missing_data_and_keep_the_role_of_a_filtered_field()
    {
        Assert.SkipUnless(factory.Services.GetService<IStructuredFormPublicationDriver>() is not null,
            "The optional structured form publisher must be installed for this composition test.");
        await using var isolated = new ApiFactory();
        await isolated.InitializeAsync();
        var scenario = new WhatsAppFormSendingTests(isolated);
        foreach (var culture in new[] { "es", "en" }) await scenario.AssertRecipientCatalogSendAsync(culture);
    }

    private async Task AssertRecipientCatalogSendAsync(string culture)
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        var cloud = new CapturingWhatsAppCloudClient();
        await using var host = FormsHost(provider, cloud, null, clock);
        using var author = await OperatorAsync(host, "formtemplates.read", "formtemplates.manage", "formtemplates.publish",
            "formcatalogs.read", "formcatalogs.manage", "formpersondata.manage", "users.manage");
        using var sender = await OperatorAsync(host, "whatsapp.read", "whatsapp.send");
        author.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        sender.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var recipient = await FixtureEmployeeAsync(culture);
        var missingRecipient = await FixtureEmployeeAsync(culture);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var zoneResponse = await author.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Zonas", en = "Zones" }, description = new { es = "Zonas", en = "Zones" },
            items = new[] { RecipientItem("zone", "CENTRAL", "Centro", "Central") },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, zoneResponse.StatusCode);
        var zones = await zoneResponse.ReadJsonAsync();
        var zoneCatalogId = zones.GetProperty("catalog").GetProperty("id").GetGuid();
        var zoneId = zones.GetProperty("itemIdentityMap").GetProperty("zone").GetGuid();
        object Field(string key, Guid? id, int position, string role) => new
        {
            localId = id is null ? key : null, id, key, name = new { es = key, en = key },
            type = key == "zone" ? "catalog" : "text", sourceCatalogId = key == "zone" ? zoneCatalogId : (Guid?)null,
            required = false, unique = false, min = (string?)null, max = (string?)null,
            maxLength = key == "zone" ? (int?)null : 300, role, position, isActive = true,
        };
        using var catalogResponse = await author.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Proveedores", en = "Suppliers" }, description = new { es = "Proveedores", en = "Suppliers" },
            fields = new[] { Field("zone", null, 0, "filter"), Field("public_detail", null, 1, "data"), Field("private_value", null, 2, "data") },
            items = new[] { RecipientItem("supplier", "VISIBLE", "Texto privado", "Private item text") },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, catalogResponse.StatusCode);
        var created = await catalogResponse.ReadJsonAsync();
        var catalogId = created.GetProperty("catalog").GetProperty("id").GetGuid();
        var itemId = created.GetProperty("itemIdentityMap").GetProperty("supplier").GetGuid();
        var identities = created.GetProperty("fieldIdentityMap");
        var zoneField = identities.GetProperty("zone").GetGuid();
        var publicField = identities.GetProperty("public_detail").GetGuid();
        var privateField = identities.GetProperty("private_value").GetGuid();
        object CatalogEdit(long revision, string role, object[] items) => new
        {
            expectedRevision = revision, name = new { es = "Proveedores", en = "Suppliers" }, description = new { es = "Proveedores", en = "Suppliers" },
            fields = new[] { Field("zone", zoneField, 0, role), Field("public_detail", publicField, 1, "data"), Field("private_value", privateField, 2, "data") }, items,
        };
        object item = new
        {
            localId = "supplier", itemId, code = "VISIBLE", text = new { es = "Texto privado", en = "Private item text" },
            detail = new { es = "Detalle privado original", en = "Private original detail" }, isActive = true,
            fields = new Dictionary<string, string>
            {
                [zoneField.ToString("D")] = zoneId.ToString("D"), [publicField.ToString("D")] = "Chosen description",
                [privateField.ToString("D")] = "Never shown private value",
            },
        };
        using var updated = await author.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", CatalogEdit(1, "filter", [item]), Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var attributeResponse = await author.PostAsJsonAsync("/api/forms/person-attributes", new
        {
            key = "zone_" + suffix, name = new { es = "Zona", en = "Zone" }, type = "Catalog", sourceCatalogId = zoneCatalogId,
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, attributeResponse.StatusCode);
        var attributeId = (await attributeResponse.ReadJsonAsync()).GetProperty("id").GetGuid();
        using var savedData = await author.PutAsJsonAsync($"/api/forms/person-data/{recipient}", new
        {
            expectedRevision = (long?)null, values = new Dictionary<string, string> { [attributeId.ToString("D")] = zoneId.ToString("D") },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, savedData.StatusCode);
        using var draftResponse = await author.PostAsJsonAsync("/api/forms/definitions", new
        {
            requestId = Guid.NewGuid(), name = new { es = "Elegir proveedor", en = "Choose supplier" }, prefix = "CR" + suffix.ToUpperInvariant(),
            draft = new
            {
                format = 1, culture = "es", submitLabel = new { es = "Enviar", en = "Submit" },
                fields = new[] { new
                {
                    localId = "supplier", type = "choice", label = new { es = "Proveedor", en = "Supplier" }, required = true,
                    source = new { catalog = catalogId, match = new[] { new { fieldId = zoneField, personAttributeId = attributeId } },
                        presentation = new { title = new[] { "code" }, description = new[] { publicField.ToString("D") } } },
                } },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var definition = (await draftResponse.ReadJsonAsync()).GetProperty("definition");
        var definitionId = definition.GetProperty("id").GetGuid();
        var key = definition.GetProperty("draft").GetProperty("fields")[0].GetProperty("key").GetString()!;
        var versionId = await PublishRecipientCatalogAsync(author, host.Services, definition, clock);
        var contactId = await CreateLinkedContactAsync(recipient, clock);
        using var choices = await sender.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
        Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        var context = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
        using var preparation = await sender.GetAsync($"/api/whatsapp/chats/{contactId}/forms/{definitionId}/versions/{versionId}/preparation", Ct);
        Assert.Equal(HttpStatusCode.OK, preparation.StatusCode);
        Assert.True((await preparation.ReadJsonAsync()).GetProperty("preparation").GetProperty("canSend").GetBoolean());
        using var accepted = await sender.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", Send(context), Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        var sent = Assert.IsType<WhatsAppFlowMessageResponse>(Assert.Single(cloud.Messages));
        var payload = WhatsAppMessagePayload.Build(sent, new WhatsAppOptions
        { WabaId = sent.Account.WabaId, PhoneNumberId = sent.Account.PhoneNumberId, GraphApiVersion = sent.Account.GraphApiVersion });
        var option = Assert.IsType<JsonObject>(Assert.Single(payload["interactive"]!["action"]!["parameters"]!["flow_action_payload"]!["data"]![key + "_options"]!.AsArray()));
        Assert.Equal(new[] { "id", "title", "description" }, option.Select(value => value.Key));
        Assert.Equal(itemId.ToString("D"), option["id"]!.GetValue<string>());
        Assert.Equal("VISIBLE", option["title"]!.GetValue<string>());
        Assert.Equal("Chosen description", option["description"]!.GetValue<string>());
        foreach (var hidden in new[] { "Texto privado", "Private item text", "Detalle privado original", "Private original detail", "Never shown private value", privateField.ToString("D") })
            Assert.DoesNotContain(hidden, payload.ToJsonString(), StringComparison.Ordinal);
        var missingContact = await CreateLinkedContactAsync(missingRecipient, clock);
        await AssertBlockedAsync(missingContact, "Forms.Record.RecipientDataMissing", culture == "en" ? "Zone" : "Zona", "preparation-recipient-data-missing");
        using var changedRole = await author.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", CatalogEdit(2, "data", []), Ct);
        Assert.Equal(HttpStatusCode.Conflict, changedRole.StatusCode);
        Assert.Equal("Forms.Catalog.FieldInUse", (await changedRole.ReadJsonAsync()).GetProperty("code").GetString());
        using var stillReady = await sender.GetAsync($"/api/whatsapp/chats/{contactId}/forms/{definitionId}/versions/{versionId}/preparation", Ct);
        Assert.Equal(HttpStatusCode.OK, stillReady.StatusCode);
        Assert.True((await stillReady.ReadJsonAsync()).GetProperty("preparation").GetProperty("canSend").GetBoolean());
        Assert.Single(cloud.Messages);

        object Send(string? revision) => new { requestId = Guid.NewGuid(), definitionId, versionId,
            expectedModeRevision = 0, expectedRecipientContextRevision = revision, message = "Complete this form" };
        async Task AssertBlockedAsync(Guid contact, string expectedCode, string expectedMissingData, string capture)
        {
            using var list = await sender.GetAsync($"/api/whatsapp/chats/{contact}/forms", Ct);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var revision = (await list.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
            using var blocked = await sender.GetAsync($"/api/whatsapp/chats/{contact}/forms/{definitionId}/versions/{versionId}/preparation", Ct);
            Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
            var bytes = await blocked.Content.ReadAsByteArrayAsync(Ct);
            var output = Environment.GetEnvironmentVariable("WHATSAPP_CATALOG_HTTP_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                var directory = Path.Combine(output, culture); Directory.CreateDirectory(directory);
                await File.WriteAllBytesAsync(Path.Combine(directory, capture + ".json"), bytes, Ct);
            }
            using var json = JsonDocument.Parse(bytes);
            var body = json.RootElement.GetProperty("preparation");
            Assert.False(body.GetProperty("canSend").GetBoolean());
            Assert.Empty(body.GetProperty("catalogs").EnumerateArray());
            var issue = Assert.Single(body.GetProperty("issues").EnumerateArray());
            Assert.Equal(expectedCode, issue.GetProperty("code").GetString());
            Assert.Equal(expectedMissingData, issue.GetProperty("missingData").GetString());
            using var denied = await sender.PostAsJsonAsync($"/api/whatsapp/chats/{contact}/form-messages", Send(revision), Ct);
            Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
            Assert.Equal("WhatsApp.Flow.CatalogPreparationBlocked", (await denied.ReadJsonAsync()).GetProperty("code").GetString());
        }
    }

    private static object RecipientItem(string localId, string code, string es, string en) =>
        new { localId, code, text = new { es, en }, isActive = true };

    private static async Task<Guid> PublishRecipientCatalogAsync(HttpClient author, IServiceProvider services, JsonElement definition, FakeTimeProvider clock)
    {
        var id = definition.GetProperty("id").GetGuid();
        var payload = JsonNode.Parse(definition.GetRawText())!.AsObject();
        foreach (var property in new[] { "id", "prefixLocked", "revision", "offerWhen" }) payload.Remove(property);
        payload["expectedRevision"] = definition.GetProperty("revision").GetInt64();
        var question = payload["draft"]!["fields"]![0]!.AsObject(); question["localId"] = "supplier"; question.Remove("key");
        using var check = await author.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", payload, Ct);
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var assessment = await check.ReadJsonAsync();
        Assert.True(assessment.GetProperty("canAccept").GetBoolean(), assessment.GetRawText());
        payload["requestId"] = Guid.NewGuid().ToString("D"); payload["checkToken"] = assessment.GetProperty("checkToken").GetString();
        using var accepted = await author.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", payload, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        var operationId = receipt.GetProperty("operationId").GetGuid();
        var driver = services.GetRequiredService<IStructuredFormPublicationDriver>();
        var worker = services.GetRequiredService<WhatsAppFlowBackgroundService>();
        for (var attempt = 0; attempt < 25; attempt++)
        {
            await driver.ProcessAsync(operationId, Ct); await worker.ProcessNextAsync(operationId, Ct); clock.Advance(TimeSpan.FromSeconds(16));
            using var status = await author.GetAsync(receipt.GetProperty("recoveryPath").GetString(), Ct);
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            if ((await status.ReadJsonAsync()).GetProperty("state").GetString() == "Published") return receipt.GetProperty("versionId").GetGuid();
        }
        Assert.Fail("The real publication must finish through both modules.");
        return Guid.Empty;
    }
}
