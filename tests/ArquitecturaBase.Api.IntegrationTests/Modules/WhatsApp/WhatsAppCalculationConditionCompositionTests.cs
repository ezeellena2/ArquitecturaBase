using System.Globalization;
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

// A calculation (CA1-A) and a condition (CO1-A) through the real WhatsApp channel (r13): the publication seals both in the
// projection, the Flow leaves the calculations out and puts the question with a condition inside an If, and a reply whose
// condition does not hold reaches the real sink without the hidden question, with the amount and the total computed by the owner.
public sealed partial class WhatsAppFormSendingTests
{
    private const string ServiceLocalId = "0196f2a0-0000-7000-8000-0000000000e1";
    private const string QuantityLocalId = "0196f2a0-0000-7000-8000-0000000000e2";
    private const string PriceLocalId = "0196f2a0-0000-7000-8000-0000000000e3";
    private const string AmountLocalId = "0196f2a0-0000-7000-8000-0000000000e4";

    [Fact]
    public async Task A_calculation_and_a_condition_publish_in_the_flow_and_a_reply_is_computed_by_the_real_sink()
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
            requestId = Guid.NewGuid(), name = new { es = "Factura de prueba", en = "Test invoice" }, prefix = "FC" + suffix.ToUpperInvariant(),
            draft = new
            {
                format = 1, culture = "es", submitLabel = new { es = "Enviar", en = "Send" },
                fields = new object[]
                {
                    new { localId = ServiceLocalId, type = "boolean", required = true, label = new { es = "¿Es un servicio?", en = "Is it a service?" } },
                    new { localId = "period", type = "date", required = true, label = new { es = "Período desde", en = "Period from" },
                        showWhen = new { questionId = ServiceLocalId, values = new[] { "true" } } },
                    new { localId = "items", type = "group", required = true, label = new { es = "Artículos", en = "Items" }, rows = new { minRows = 1, maxRows = 2 } },
                    new { localId = QuantityLocalId, type = "decimal", label = new { es = "Cantidad", en = "Quantity" }, repeated = true },
                    new { localId = PriceLocalId, type = "decimal", label = new { es = "Precio", en = "Price" }, repeated = true },
                    new { localId = AmountLocalId, type = "decimal", label = new { es = "Importe", en = "Amount" }, repeated = true,
                        calculation = new { operation = "multiply", operands = new[] { QuantityLocalId, PriceLocalId } } },
                    new { localId = "total", type = "decimal", label = new { es = "Total", en = "Total" },
                        calculation = new { operation = "sum", operands = new[] { AmountLocalId } } },
                },
            },
        }, Ct);
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var definition = (await created.ReadJsonAsync()).GetProperty("definition");
        var definitionId = definition.GetProperty("id").GetGuid();
        var fields = definition.GetProperty("draft").GetProperty("fields").EnumerateArray().ToArray();
        var keys = fields.Select(field => field.GetProperty("key").GetString()!).ToArray();
        var checkBody = JsonNode.Parse(definition.GetRawText())!.AsObject();
        foreach (var key in new[] { "id", "prefixLocked", "revision", "offerWhen" }) checkBody.Remove(key);
        checkBody["expectedRevision"] = definition.GetProperty("revision").GetInt64();
        var checkFields = checkBody["draft"]!["fields"]!.AsArray();
        for (var index = 0; index < checkFields.Count; index++)
        {
            checkFields[index]!["localId"] = "field-" + index;
            checkFields[index]!.AsObject().Remove("key");
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

        // The check and the dispatch send the same request: the sealed projection keeps the calculations and the condition.
        var bindings = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>().Where(binding => binding.VersionId == versionId).ToArrayAsync(Ct));
        Assert.Equal(2, bindings.Length);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var protector = scope.ServiceProvider.GetRequiredService<IWhatsAppFlowProtector>();
            foreach (var binding in bindings)
            {
                var projection = protector.ReadProjection(binding.OperationId, binding.Id, binding.VersionId, binding.Culture, binding.ProtectedProjection);
                Assert.Equal("flow-static-7.3-r14", projection.ProfileRevision);
                Assert.Equal(new bool?[] { null, null, null, null, null, true, true }, projection.Mapping.Select(field => field.Calculated));
                Assert.Equal(fields[0].GetProperty("fieldId").GetGuid(), projection.Mapping[1].ShowWhen!.FieldId);
                Assert.Equal(["true"], projection.Mapping[1].ShowWhen!.Values);
                using var json = JsonDocument.Parse(projection.Json);
                // The first screen, two items and, as only the total comes after the block, the closing screen.
                Assert.Equal(4, json.RootElement.GetProperty("screens").GetArrayLength());
                Assert.DoesNotContain(keys[5], projection.Json, StringComparison.Ordinal);
                Assert.DoesNotContain(keys[6], projection.Json, StringComparison.Ordinal);
                Assert.Contains("${form." + keys[0] + "} == 'true'", Conditions(json.RootElement));
            }
        }

        var contactId = await CreateLinkedContactAsync(employeeId, clock);
        var waId = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppContact>().Where(contact => contact.Id == contactId)
            .Select(contact => contact.WaId).SingleAsync(Ct));
        using var choices = await author.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
        Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        var context = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
        using var sent = await author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", new
        {
            requestId = Guid.NewGuid(), definitionId, versionId, expectedModeRevision = 0,
            expectedRecipientContextRevision = context, message = "Completá la factura",
        }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        var flow = Assert.IsType<WhatsAppFlowMessageResponse>(Assert.Single(cloud.Messages));
        var session = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().SingleAsync(value => value.ContactId == contactId, Ct));
        // «No» after writing the period: what is left in the hidden question does not reach the owner.
        var response = new JsonObject
        {
            ["flow_token"] = flow.FlowToken, [keys[0]] = "false", [keys[1]] = "2026-11-01",
            [keys[3] + "_r1"] = "2", [keys[4] + "_r1"] = "10,505", [keys[2] + "_r1"] = "true",
            [keys[3] + "_r2"] = "1", [keys[4] + "_r2"] = "4",
        };
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
        var values = reply.Values.ToDictionary(value => value.Key);
        Assert.False(values.TryGetValue(keys[1], out var period) && period.Value is not null);
        // 2 × 10.505 is 21.01 and 1 × 4 is 4; the total adds the amounts.
        var items = Assert.IsAssignableFrom<IReadOnlyList<StructuredRecordReadRow>>(values[keys[2]].Value);
        Assert.Equal([21.01m, 4m], items.Select(item => Number(item.Values.Single(value => value.Key == keys[5]).Value)));
        Assert.Equal(25.01m, Number(values[keys[6]].Value));

        static IEnumerable<string> Conditions(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().SelectMany(property => property.Name == "condition" && property.Value.ValueKind == JsonValueKind.String
                ? [property.Value.GetString()!] : Conditions(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().SelectMany(Conditions),
            _ => [],
        };
        static decimal Number(object? value) => decimal.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, NumberStyles.Number, CultureInfo.InvariantCulture);
    }
}
