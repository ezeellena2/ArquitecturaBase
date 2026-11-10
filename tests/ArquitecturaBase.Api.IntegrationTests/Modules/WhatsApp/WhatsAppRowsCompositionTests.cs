using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Caching;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// The block of rows through the real WhatsApp channel (RG3-A, RG8-A): forty questions and a block are checked, accepted
// and published, and the sealed projection keeps the block's rows, the mark of each question of a row and a screen per row.
public sealed partial class WhatsAppFormSendingTests
{
    [Fact]
    public async Task A_block_with_forty_questions_publishes_a_screen_per_row_and_seals_its_rows()
    {
        Assert.SkipUnless(factory.Services.GetService<IStructuredFormPublicationDriver>() is not null,
            "The optional structured form publisher must be installed for this composition test.");
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var host = FormsHost(new CapturingFlowProvider(clock), new CapturingWhatsAppCloudClient(), null, clock);
        using var author = await OperatorAsync(host, "formtemplates.read", "formtemplates.manage", "formtemplates.publish");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fields = new List<object>
        {
            new { localId = "supplier", type = "shortText", label = new { es = "Proveedor", en = "Supplier" }, maxLength = 40 },
            new { localId = "items", type = "group", label = new { es = "Artículos", en = "Items" }, required = false, rows = new { minRows = 0, maxRows = 20 } },
            new { localId = "quantity", type = "decimal", label = new { es = "Cantidad", en = "Quantity" }, repeated = true },
            new { localId = "unit", type = "choice", label = new { es = "Unidad", en = "Unit" }, repeated = true,
                options = new[] { new { localId = "liter", label = new { es = "Litros", en = "Liters" } } } },
        };
        fields.AddRange(Enumerable.Range(1, 37).Select(index => (object)new
        {
            localId = "datum-" + index, type = "shortText", label = new { es = "Dato " + index, en = "Datum " + index }, maxLength = 20,
        }));
        using var created = await author.PostAsJsonAsync("/api/forms/definitions", new
        {
            requestId = Guid.NewGuid(), name = new { es = "Recepción", en = "Receipt" }, prefix = "RG" + suffix.ToUpperInvariant(),
            draft = new { format = 1, culture = "es", submitLabel = new { es = "Enviar", en = "Send" }, fields },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var definition = (await created.ReadJsonAsync()).GetProperty("definition");
        var definitionId = definition.GetProperty("id").GetGuid();
        var checkBody = JsonNode.Parse(definition.GetRawText())!.AsObject();
        foreach (var key in new[] { "id", "prefixLocked", "revision", "offerWhen" }) checkBody.Remove(key);
        checkBody["expectedRevision"] = definition.GetProperty("revision").GetInt64();
        var checkFields = checkBody["draft"]!["fields"]!.AsArray();
        for (var index = 0; index < checkFields.Count; index++)
        {
            checkFields[index]!["localId"] = "field-" + index;
            checkFields[index]!.AsObject().Remove("key");
            if (checkFields[index]!["options"] is JsonArray options)
                for (var option = 0; option < options.Count; option++) options[option]!["localId"] = "option-" + option;
        }

        using var check = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publication-checks", checkBody, Ct);
        Assert.True(check.StatusCode == HttpStatusCode.OK, await check.Content.ReadAsStringAsync(Ct));
        var checkedBody = await check.ReadJsonAsync();
        Assert.True(checkedBody.GetProperty("canAccept").GetBoolean(), checkedBody.GetRawText());
        Assert.Contains(checkedBody.GetProperty("warnings").EnumerateArray(),
            warning => warning.GetProperty("code").GetString() == "Forms.Publication.RowsOverChannelLimit");
        checkBody["requestId"] = Guid.NewGuid().ToString();
        checkBody["checkToken"] = checkedBody.GetProperty("checkToken").GetString();
        using var accepted = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", checkBody, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        var versionId = receipt.GetProperty("versionId").GetGuid();
        var operationId = receipt.GetProperty("operationId").GetGuid();
        var driver = host.Services.GetRequiredService<IStructuredFormPublicationDriver>();
        var publisher = host.Services.GetRequiredService<WhatsAppFlowBackgroundService>();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await driver.ProcessAsync(operationId, Ct);
            await publisher.ProcessNextAsync(operationId, Ct);
            clock.Advance(TimeSpan.FromSeconds(16));
        }

        using var status = await author.GetAsync(receipt.GetProperty("recoveryPath").GetString(), Ct);
        Assert.Equal("Published", (await status.ReadJsonAsync()).GetProperty("state").GetString());
        var bindings = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>()
            .Where(binding => binding.VersionId == versionId).ToArrayAsync(Ct));
        Assert.Equal(2, bindings.Length);
        await using var scope = host.Services.CreateAsyncScope();
        var protector = scope.ServiceProvider.GetRequiredService<IWhatsAppFlowProtector>();
        foreach (var binding in bindings)
        {
            var projection = protector.ReadProjection(binding.OperationId, binding.Id, binding.VersionId, binding.Culture, binding.ProtectedProjection);
            Assert.Equal("flow-static-7.3-r13", projection.ProfileRevision);
            Assert.Equal(41, projection.Mapping.Count);
            Assert.Equal(new StructuredFormRowSettings(0, 20), projection.Mapping[1].Rows);
            Assert.Equal(new bool?[] { null, null, true, true, null }, projection.Mapping.Take(5).Select(field => field.Repeated));
            using var json = JsonDocument.Parse(projection.Json);
            // The supplier, «¿Agregás un ítem?», ten rows and the rest of the questions.
            Assert.Equal(13, json.RootElement.GetProperty("screens").GetArrayLength());
        }
    }

    // A reply with three items («Sí», «Sí» and the last one without the question) through the signed webhook and the real
    // capture: the owner accepts the items of the block and numbers the response once; reading it leaves the block out until
    // the items are shown (Etapa 3 of the rows).
    [Fact]
    public async Task A_reply_with_three_items_is_captured_by_the_real_sink()
    {
        Assert.SkipUnless(factory.Services.GetService<IStructuredFormPublicationDriver>() is not null,
            "The optional structured form publisher must be installed for this composition test.");
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var cloud = new CapturingWhatsAppCloudClient();
        await using var host = FormsHost(new CapturingFlowProvider(clock), cloud, null, clock).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWhatsAppDispatchCoordinator>();
            services.AddSingleton<IWhatsAppDispatchCoordinator>(sp => new WhatsAppDispatchCoordinator(sp.GetRequiredService<RedisCache>(), TimeProvider.System));
        }));
        using var author = await OperatorAsync(host, "formtemplates.read", "formtemplates.manage", "formtemplates.publish",
            "formrecords.read", "whatsapp.read", "whatsapp.send");
        using var employee = await OperatorAsync(host, "formrecords.create");
        var actorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var created = await author.PostAsJsonAsync("/api/forms/definitions", new
        {
            requestId = Guid.NewGuid(), name = new { es = "Recepción por ítems", en = "Itemized receipt" }, prefix = "RI" + suffix.ToUpperInvariant(),
            draft = new
            {
                format = 1, culture = "es", submitLabel = new { es = "Enviar", en = "Send" },
                fields = new object[]
                {
                    new { localId = "supplier", type = "shortText", label = new { es = "Proveedor", en = "Supplier" }, maxLength = 40 },
                    new { localId = "items", type = "group", label = new { es = "Artículos", en = "Items" }, required = true, rows = new { minRows = 1, maxRows = 3 } },
                    new { localId = "quantity", type = "decimal", label = new { es = "Cantidad", en = "Quantity" }, repeated = true },
                    new { localId = "unit", type = "choice", label = new { es = "Unidad", en = "Unit" }, repeated = true,
                        options = new[] { new { localId = "liter", label = new { es = "Litros", en = "Liters" } } } },
                },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var definition = (await created.ReadJsonAsync()).GetProperty("definition");
        var definitionId = definition.GetProperty("id").GetGuid();
        var fields = definition.GetProperty("draft").GetProperty("fields").EnumerateArray().ToArray();
        var keys = fields.Select(field => field.GetProperty("key").GetString()!).ToArray();
        var liter = fields[3].GetProperty("options")[0].GetProperty("choiceId").GetGuid();
        var checkBody = JsonNode.Parse(definition.GetRawText())!.AsObject();
        foreach (var key in new[] { "id", "prefixLocked", "revision", "offerWhen" }) checkBody.Remove(key);
        checkBody["expectedRevision"] = definition.GetProperty("revision").GetInt64();
        var checkFields = checkBody["draft"]!["fields"]!.AsArray();
        for (var index = 0; index < checkFields.Count; index++)
        {
            checkFields[index]!["localId"] = "field-" + index;
            checkFields[index]!.AsObject().Remove("key");
            if (checkFields[index]!["options"] is JsonArray options)
                for (var option = 0; option < options.Count; option++) options[option]!["localId"] = "option-" + option;
        }
        using var check = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publication-checks", checkBody, Ct);
        Assert.True(check.StatusCode == HttpStatusCode.OK, await check.Content.ReadAsStringAsync(Ct));
        var checkedBody = await check.ReadJsonAsync();
        Assert.True(checkedBody.GetProperty("canAccept").GetBoolean(), checkedBody.GetRawText());
        checkBody["requestId"] = Guid.NewGuid().ToString();
        checkBody["checkToken"] = checkedBody.GetProperty("checkToken").GetString();
        using var accepted = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", checkBody, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        var versionId = receipt.GetProperty("versionId").GetGuid();
        var operationId = receipt.GetProperty("operationId").GetGuid();
        var driver = host.Services.GetRequiredService<IStructuredFormPublicationDriver>();
        var publisher = host.Services.GetRequiredService<WhatsAppFlowBackgroundService>();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await driver.ProcessAsync(operationId, Ct);
            await publisher.ProcessNextAsync(operationId, Ct);
            clock.Advance(TimeSpan.FromSeconds(16));
        }
        using var status = await author.GetAsync(receipt.GetProperty("recoveryPath").GetString(), Ct);
        Assert.Equal("Published", (await status.ReadJsonAsync()).GetProperty("state").GetString());

        var contactId = await CreateLinkedContactAsync(employeeId, clock);
        var waId = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppContact>().Where(contact => contact.Id == contactId)
            .Select(contact => contact.WaId).SingleAsync(Ct));
        using var choices = await author.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
        Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        var context = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
        using var sent = await author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", new
        {
            requestId = Guid.NewGuid(), definitionId, versionId, expectedModeRevision = 0,
            expectedRecipientContextRevision = context, message = "Completá la recepción",
        }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        var flow = Assert.IsType<WhatsAppFlowMessageResponse>(Assert.Single(cloud.Messages));
        var session = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().SingleAsync(value => value.ContactId == contactId, Ct));
        var response = new JsonObject { ["flow_token"] = flow.FlowToken, [keys[0]] = "Proveedor del norte" };
        foreach (var (row, quantity) in new[] { (1, "200,5"), (2, "100"), (3, "7") })
        {
            response[keys[2] + "_r" + row] = quantity;
            response[keys[3] + "_r" + row] = liter.ToString("D");
            if (row < 3) response[keys[1] + "_r" + row] = "true";
        }

        var inbound = MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), waId, null, clock.GetUtcNow().UtcDateTime, "unused");
        inbound.Remove("text"); inbound["type"] = "interactive";
        inbound["interactive"] = new JsonObject { ["type"] = "nfm_reply", ["nfm_reply"] = new JsonObject
        {
            ["name"] = "flow", ["body"] = "Sent", ["response_json"] = response.ToJsonString(),
        } };
        var payload = MetaWebhook.Build(contacts: [MetaWebhook.Contact(waId, null, "Persona de prueba")], messages: [inbound]);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(payload) };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(payload));
        using var webhook = await author.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IWhatsAppFlowReplyService>().ProcessPendingAsync(Ct);

        var completed = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().SingleAsync(value => value.Id == session.Id, Ct));
        Assert.Equal(WhatsAppFlowCaptureState.Completed, completed.CaptureState);
        await using var readScope = host.Services.CreateAsyncScope();
        var read = await readScope.ServiceProvider.GetRequiredService<IStructuredRecordSource>().ReadAsync(actorId, "es", [session.Id], Ct);
        var reply = Assert.Single(read.Replies.Values);
        Assert.Equal(completed.CompletedRecordId, reply.RecordId);
        Assert.EndsWith("-000001", reply.Number, StringComparison.Ordinal);
        Assert.Equal(2, reply.Values.Count);
        Assert.Equal("Proveedor del norte", reply.Values[0].Value);
        Assert.Equal(3, Assert.IsAssignableFrom<IReadOnlyList<StructuredRecordReadRow>>(reply.Values[1].Value).Count);

        // The card of the chat brings the block with its items, in the language of whoever reads it.
        foreach (var culture in new[] { "es", "en" })
        {
            author.DefaultRequestHeaders.AcceptLanguage.Clear();
            author.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
            using var thread = await author.GetAsync($"/api/whatsapp/chats/{contactId}/messages", Ct);
            Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
            var bytes = await thread.Content.ReadAsByteArrayAsync(Ct);
            using var captured = JsonDocument.Parse(bytes);
            var card = captured.RootElement.GetProperty("messages").EnumerateArray()
                .Where(message => message.TryGetProperty("formReply", out var value) && value.ValueKind == JsonValueKind.Object
                    && value.TryGetProperty("recordId", out var recordId) && recordId.ValueKind == JsonValueKind.String && recordId.GetGuid() == reply.RecordId)
                .Select(message => message.GetProperty("formReply")).First();
            var block = Assert.Single(card.GetProperty("values").EnumerateArray(), value => value.GetProperty("type").GetString() == "group");
            Assert.Equal(culture == "es" ? "Artículos" : "Items", block.GetProperty("label").GetString());
            var items = block.GetProperty("value").EnumerateArray().ToArray();
            Assert.Equal(["200.5", "100", "7"], items.Select(item => item.GetProperty("values")[0].GetProperty("value").GetString()));
            Assert.All(items, item => Assert.Equal(culture == "es" ? "Litros" : "Liters",
                item.GetProperty("values")[1].GetProperty("value").GetProperty("label").GetString()));
            var output = Environment.GetEnvironmentVariable("WHATSAPP_FORM_REPLIES_HTTP_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                var directory = Path.Combine(output, culture);
                Directory.CreateDirectory(directory);
                await File.WriteAllBytesAsync(Path.Combine(directory, "reply-rows.json"), bytes, Ct);
            }
        }
    }
}
