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

// W1-A through the real WhatsApp channel: a form with a location question publishes with WhatsApp, sending it opens with the location
// request, the location that arrives lets the Flow go out without the question, and the reply reaches the real sink with the point.
// With WHATSAPP_FORMS_HTTP_CONTRACT_OUTPUT and FORM_RESPONSES_HTTP_OUTPUT it keeps the chat and the detail as HTTP bodies for the front.
public sealed partial class WhatsAppFormSendingTests
{
    [Fact]
    public async Task A_location_is_asked_before_the_flow_and_the_reply_saves_it_with_the_point()
    {
        Assert.SkipUnless(factory.Services.GetService<IStructuredFormPublicationDriver>() is not null,
            "The optional structured form publisher must be installed for this composition test.");
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var cloud = new CapturingWhatsAppCloudClient(uniqueIds: true);
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
            requestId = Guid.NewGuid(), name = new { es = "Visita de prueba", en = "Test visit" }, prefix = "VI" + suffix.ToUpperInvariant(),
            draft = new
            {
                format = 1, culture = "es", submitLabel = new { es = "Enviar", en = "Send" },
                fields = new object[]
                {
                    new { localId = "client", type = "shortText", required = true, maxLength = 80, label = new { es = "Cliente", en = "Client" } },
                    new { localId = "place", type = "location", required = true, label = new { es = "Dónde estás", en = "Where you are" } },
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

        // The sealed projection keeps the location in its place, outside the Flow; the profile stays r13.
        var bindings = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>().Where(binding => binding.VersionId == versionId).ToArrayAsync(Ct));
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var protector = scope.ServiceProvider.GetRequiredService<IWhatsAppFlowProtector>();
            foreach (var binding in bindings)
            {
                var projection = protector.ReadProjection(binding.OperationId, binding.Id, binding.VersionId, binding.Culture, binding.ProtectedProjection);
                Assert.Equal("flow-static-7.3-r14", projection.ProfileRevision);
                Assert.Equal(["shortText", "location"], projection.Mapping.Select(field => field.Type));
                Assert.DoesNotContain(keys[1], projection.Json, StringComparison.Ordinal);
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
            expectedRecipientContextRevision = context, message = "Completá la visita de hoy",
        }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        Assert.Equal("LocationRequest", (await sent.ReadJsonAsync()).GetProperty("kind").GetString());
        var outgoingWorker = host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        await outgoingWorker.ProcessNextAsync(Ct);
        var request = Assert.IsType<WhatsAppLocationRequestMessage>(Assert.Single(cloud.Messages));
        Assert.Equal("Para completar «Visita de prueba», mandanos tu ubicación.", request.Body);
        var requested = await ThreadAsync("thread-location-requested");
        Assert.Equal("Requested", Assert.Single(requested.GetProperty("messages").EnumerateArray(),
            message => message.GetProperty("kind").GetString() == "LocationRequest").GetProperty("form").GetProperty("location").GetProperty("state").GetString());

        // The person writes instead: nothing goes out, the send keeps waiting. Then the location arrives and lets the Flow go out.
        clock.Advance(TimeSpan.FromMinutes(1));
        await WebhookAsync(MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), waId, null, clock.GetUtcNow().UtcDateTime, "Ahora te la mando"));
        await host.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);
        await outgoingWorker.ProcessNextAsync(Ct);
        Assert.Single(cloud.Messages);
        clock.Advance(TimeSpan.FromMinutes(1));
        await WebhookAsync(MetaWebhook.Location(MetaWebhook.UniqueWaMessageId(), waId, null, clock.GetUtcNow().UtcDateTime,
            JsonValue.Create(-34.6037224m), JsonValue.Create(-58.3815916m)));
        await host.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);
        var session = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().SingleAsync(value => value.ContactId == contactId, Ct));
        Assert.Equal((WhatsAppFlowLocationState.Received, -34.603722m, -58.381592m), (session.LocationState, session.LocationLatitude, session.LocationLongitude));
        clock.Advance(TimeSpan.FromSeconds(5));
        await outgoingWorker.ProcessNextAsync(Ct);
        var flow = Assert.IsType<WhatsAppFlowMessageResponse>(cloud.Messages[1]);
        Assert.Equal("Completá la visita de hoy", flow.Body);
        var received = await ThreadAsync("thread-location-received");
        var messages = received.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal("Received", messages.Single(message => message.GetProperty("kind").GetString() == "LocationRequest")
            .GetProperty("form").GetProperty("location").GetProperty("state").GetString());
        var point = messages.Single(message => message.GetProperty("kind").GetString() == "Location").GetProperty("location");
        Assert.Equal((-34.603722m, -58.381592m), (point.GetProperty("latitude").GetDecimal(), point.GetProperty("longitude").GetDecimal()));
        Assert.Single(messages, message => message.GetProperty("kind").GetString() == "Flow");

        // The reply carries only the Flow's question; the owner gets the point of the send with it.
        clock.Advance(TimeSpan.FromMinutes(2));
        var reply = MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), waId, null, clock.GetUtcNow().UtcDateTime, "unused");
        reply.Remove("text"); reply["type"] = "interactive";
        reply["interactive"] = new JsonObject { ["type"] = "nfm_reply", ["nfm_reply"] = new JsonObject
        {
            ["name"] = "flow", ["body"] = "Sent", ["response_json"] = new JsonObject { ["flow_token"] = flow.FlowToken, [keys[0]] = "Ferretería Norte" }.ToJsonString(),
        } };
        await WebhookAsync(reply);
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IWhatsAppFlowReplyService>().ProcessPendingAsync(Ct);
        var completed = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().SingleAsync(value => value.Id == session.Id, Ct));
        Assert.Equal(WhatsAppFlowCaptureState.Completed, completed.CaptureState);
        await using (var readScope = host.Services.CreateAsyncScope())
        {
            var read = await readScope.ServiceProvider.GetRequiredService<IStructuredRecordSource>().ReadAsync(actorId, "es", [session.Id], Ct);
            var values = Assert.Single(read.Replies.Values).Values.ToDictionary(value => value.Key);
            Assert.Equal("Ferretería Norte", values[keys[0]].Value);
            Assert.Equal(new StructuredRecordLocationValue(-34.603722m, -58.381592m), values[keys[1]].Value);
        }

        // The detail tells the location before the form: asked, arrived, then the form sent, answered and saved.
        using var detail = await author.GetAsync($"/api/forms/records/{completed.CompletedRecordId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await ResponseContractCapture.WriteAsync(detail, "detail-location-whatsapp-es", Ct);
        Assert.Equal(["LocationRequested", "LocationReceived", "Sent", "Submitted", "Saved"],
            (await detail.ReadJsonAsync()).GetProperty("history").EnumerateArray().Select(item => item.GetProperty("kind").GetString()));

        async Task WebhookAsync(JsonObject inbound)
        {
            var payload = MetaWebhook.Build(contacts: [MetaWebhook.Contact(waId, null, "Persona de prueba")], messages: [inbound]);
            using var post = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(payload) };
            post.Content.Headers.ContentType = new("application/json");
            post.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(payload));
            using var webhook = await author.SendAsync(post, Ct);
            Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        }

        async Task<JsonElement> ThreadAsync(string name)
        {
            using var thread = await author.GetAsync($"/api/whatsapp/chats/{contactId}/messages", Ct);
            Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
            var body = await thread.Content.ReadAsByteArrayAsync(Ct);
            if (Environment.GetEnvironmentVariable("WHATSAPP_FORMS_HTTP_CONTRACT_OUTPUT") is { Length: > 0 } output)
            {
                var directory = Path.Combine(output, "es");
                Directory.CreateDirectory(directory);
                await File.WriteAllBytesAsync(Path.Combine(directory, name + ".json"), body, Ct);
            }
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
    }
}
