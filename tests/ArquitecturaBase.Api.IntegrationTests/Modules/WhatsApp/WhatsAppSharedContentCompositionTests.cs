using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
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

// W3 (task 23) through the real WhatsApp channel and BusinessForms: a form whose text questions receive the location and the contact
// publishes with WhatsApp and goes out twice; what the person shares goes to the oldest open send, one resend per send and pass with
// what was shared over its prefill and the same token, and the reply to it saves those values; the origin of that response, read by its
// detail and the list, is the first Flow of the send. Task 24: the chat shows «Prellenado con» on each resend (queued and sent) and the
// number of the kept response on the second one, and the detail tells what was shared and that the form went out again. With
// WHATSAPP_FORMS_HTTP_CONTRACT_OUTPUT and FORM_RESPONSES_HTTP_OUTPUT it keeps those bodies for the front.
public sealed partial class WhatsAppFormSendingTests
{
    [Fact]
    public async Task A_shared_location_and_contact_go_to_the_oldest_open_send_and_its_resend_is_captured_with_them()
    {
        Assert.SkipUnless(factory.Services.GetService<IStructuredFormPublicationDriver>() is not null,
            "The optional structured form publisher must be installed for this composition test.");
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var cloud = new CapturingWhatsAppCloudClient(uniqueIds: true, prefix: "wamid.shared-" + Guid.NewGuid().ToString("N") + "-");
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
        var (definitionId, versionId, keys) = await PublishSharedFormAsync(host, author, clock);

        var contactId = await CreateLinkedContactAsync(employeeId, clock);
        var waId = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppContact>().Where(contact => contact.Id == contactId)
            .Select(contact => contact.WaId).SingleAsync(Ct));
        var outgoingWorker = host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        using var choices = await author.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
        var context = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
        foreach (var message in new[] { "Completá la entrega de hoy", "Completá la entrega de mañana" })
        {
            using var sent = await author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", new
            {
                requestId = Guid.NewGuid(), definitionId, versionId, expectedModeRevision = 0, expectedRecipientContextRevision = context, message,
            }, Ct);
            Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
            await outgoingWorker.ProcessNextAsync(Ct);
            clock.Advance(TimeSpan.FromMinutes(1));
        }
        var sessions = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().AsNoTracking().Where(session => session.ContactId == contactId)
            .OrderBy(session => session.CreatedAtUtc).ToArrayAsync(Ct));
        Assert.Equal(2, sessions.Length);
        var firstFlows = cloud.Messages.Cast<WhatsAppFlowMessageResponse>().ToArray();

        // A place and a contact in one batch: the oldest send takes both and goes out again once, with both.
        await WebhookAsync(MetaWebhook.Location(MetaWebhook.UniqueWaMessageId(), waId, null, clock.GetUtcNow().UtcDateTime,
                JsonValue.Create(-32.9471024m), JsonValue.Create(-60.6389551m), "Depósito", "Bv. Oroño 1234, S2000 Rosario"),
            ContactsMessage(clock.GetUtcNow().UtcDateTime, "Ana Gómez", "+54 9 341 555-0101", "+54 341 444-0202"));
        // The current location then goes to the other send, and a third one to none.
        clock.Advance(TimeSpan.FromMinutes(1));
        await WebhookAsync(MetaWebhook.Location(MetaWebhook.UniqueWaMessageId(), waId, null, clock.GetUtcNow().UtcDateTime,
            JsonValue.Create(-32.947102m), JsonValue.Create(-60.638955m)));
        clock.Advance(TimeSpan.FromMinutes(1));
        await WebhookAsync(MetaWebhook.Location(MetaWebhook.UniqueWaMessageId(), waId, null, clock.GetUtcNow().UtcDateTime,
            JsonValue.Create(-31.4m), JsonValue.Create(-64.18m)));
        var shared = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppMessage>().AsNoTracking()
            .Where(message => message.ContactId == contactId && (message.Kind == WhatsAppMessageKind.Location || message.Kind == WhatsAppMessageKind.Contact))
            .OrderBy(message => message.InboundSequence).Select(message => message.FlowSessionId).ToArrayAsync(Ct));
        Assert.Equal([sessions[0].Id, sessions[0].Id, sessions[1].Id, null], shared);
        // Task 24: the queued resends say what they carry, the location before the contact: three values and one.
        var pending = await ThreadAsync("thread-shared-pending");
        Assert.Equal([Prefilled(("Dirección de entrega", "Bv. Oroño 1234, S2000 Rosario"), ("Recibió", "Ana Gómez"), ("Tel. de quien recibe", "+54 9 341 555-0101")),
                Prefilled(("Dirección de entrega", "-32.947102, -60.638955"))],
            pending.GetProperty("outgoing").EnumerateArray().Where(item => item.TryGetProperty("prefill", out _)).OrderBy(item => item.GetProperty("requestedAtUtc").GetDateTime())
                .Select(item => Prefill(item.GetProperty("prefill"))));

        await outgoingWorker.ProcessNextAsync(Ct);
        await outgoingWorker.ProcessNextAsync(Ct);
        await outgoingWorker.ProcessNextAsync(Ct);
        Assert.Equal(4, cloud.Messages.Count);
        var resend = Assert.IsType<WhatsAppFlowMessageResponse>(cloud.Messages[2]);
        Assert.Equal("Puse tu ubicación en «Dirección de entrega». Puse el contacto en «Recibió» y «Tel. de quien recibe». "
            + "Revisá el formulario «Registrar entrega» y confirmalo.", resend.Body);
        Assert.Equal(firstFlows[0].FlowToken, resend.FlowToken);
        var values = resend.InitialValues.Values.SelectMany(screen => screen).ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal(("Bv. Oroño 1234, S2000 Rosario", "Ana Gómez", "+54 9 341 555-0101"), (values[keys[1]], values[keys[2]], values[keys[3]]));
        var other = Assert.IsType<WhatsAppFlowMessageResponse>(cloud.Messages[3]);
        Assert.Equal("Puse tu ubicación en «Dirección de entrega». Revisá el formulario «Registrar entrega» y confirmalo.", other.Body);
        Assert.Equal((firstFlows[1].FlowToken, "-32.947102, -60.638955"), (other.FlowToken, Assert.Single(other.InitialValues.Values.Single()).Value));
        Assert.Equal(2, await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppMessage>().CountAsync(message => message.ContactId == contactId
            && message.Direction == WhatsAppMessageDirection.Outbound && message.Kind == WhatsAppMessageKind.Flow && message.IsResend, Ct)));
        // Once they went out, each resend keeps what it carried; the first Flows and the rest of the messages say nothing of it.
        var resent = await ThreadAsync("thread-shared-resend");
        var withPrefill = resent.GetProperty("messages").EnumerateArray().Where(item => item.TryGetProperty("prefill", out _)).ToArray();
        Assert.All(withPrefill, item => Assert.Equal("Flow", item.GetProperty("kind").GetString()));
        Assert.Equal([Prefilled(("Dirección de entrega", "Bv. Oroño 1234, S2000 Rosario"), ("Recibió", "Ana Gómez"), ("Tel. de quien recibe", "+54 9 341 555-0101")),
            Prefilled(("Dirección de entrega", "-32.947102, -60.638955"))], withPrefill.Select(item => Prefill(item.GetProperty("prefill"))));

        // The reply to the resend carries what the person confirmed: the shared values are saved as text answers.
        clock.Advance(TimeSpan.FromMinutes(2));
        var reply = MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), waId, null, clock.GetUtcNow().UtcDateTime, "unused");
        reply.Remove("text"); reply["type"] = "interactive";
        reply["interactive"] = new JsonObject { ["type"] = "nfm_reply", ["nfm_reply"] = new JsonObject
        {
            ["name"] = "flow", ["body"] = "Sent", ["response_json"] = new JsonObject
            {
                ["flow_token"] = resend.FlowToken, [keys[0]] = "Ferretería Norte", [keys[1]] = values[keys[1]], [keys[2]] = values[keys[2]], [keys[3]] = values[keys[3]],
            }.ToJsonString(),
        } };
        await WebhookAsync(reply);
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IWhatsAppFlowReplyService>().ProcessPendingAsync(Ct);
        var completed = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().AsNoTracking().SingleAsync(value => value.Id == sessions[0].Id, Ct));
        Assert.Equal(WhatsAppFlowCaptureState.Completed, completed.CaptureState);
        Assert.NotNull(completed.SharedLocation);
        Assert.Equal(2, completed.SharedContact!.FieldIds.Count);
        await using (var readScope = host.Services.CreateAsyncScope())
        {
            var read = await readScope.ServiceProvider.GetRequiredService<IStructuredRecordSource>().ReadAsync(actorId, "es", [sessions[0].Id], Ct);
            var saved = Assert.Single(read.Replies.Values).Values.ToDictionary(value => value.Key, value => value.Value);
            Assert.Equal(("Bv. Oroño 1234, S2000 Rosario", "Ana Gómez", "+54 9 341 555-0101"), (saved[keys[1]], saved[keys[2]], saved[keys[3]]));
        }

        // Task 24: a second reply to the same send is not saved, and the chat says which response it kept.
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), waId, null, clock.GetUtcNow().UtcDateTime, "unused");
        second.Remove("text"); second["type"] = "interactive";
        second["interactive"] = new JsonObject { ["type"] = "nfm_reply", ["nfm_reply"] = new JsonObject
        {
            ["name"] = "flow", ["body"] = "Sent", ["response_json"] = new JsonObject { ["flow_token"] = firstFlows[0].FlowToken, [keys[0]] = "Ferretería Sur" }.ToJsonString(),
        } };
        await WebhookAsync(second);
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IWhatsAppFlowReplyService>().ProcessPendingAsync(Ct);
        var replies = (await ThreadAsync("thread-shared-second-reply")).GetProperty("messages").EnumerateArray()
            .Where(item => item.GetProperty("kind").GetString() == "FlowReply").Select(item => item.GetProperty("formReply")).ToArray();
        Assert.Equal(("Completed", completed.CompletedNumber), (replies[0].GetProperty("state").GetString(), replies[0].GetProperty("number").GetString()));
        Assert.False(replies[0].TryGetProperty("savedNumber", out _));
        Assert.Equal(("Rejected", "Forms.Record.ReplayConflict", completed.CompletedNumber), (replies[1].GetProperty("state").GetString(),
            replies[1].GetProperty("rejectionCode").GetString(), replies[1].GetProperty("savedNumber").GetString()));

        // The origin of the response is the first Flow of its send, also after a resend: the detail and the list of responses read it. Its
        // activity tells what the person shared, with the questions it completed, and that the form went out again.
        using (var detail = await author.GetAsync($"/api/forms/records/{completed.CompletedRecordId}", Ct))
        {
            Assert.True(detail.StatusCode == HttpStatusCode.OK, await detail.Content.ReadAsStringAsync(Ct));
            await ResponseContractCapture.WriteAsync(detail, "detail-shared-whatsapp-es", Ct);
            var history = (await detail.ReadJsonAsync()).GetProperty("history").EnumerateArray().ToArray();
            Assert.Equal(["Sent", "LocationReceived", "ContactReceived", "SentAgain", "Submitted", "Saved"], history.Select(item => item.GetProperty("kind").GetString()));
            Assert.Equal(["Dirección de entrega"], history[1].GetProperty("questions").EnumerateArray().Select(item => item.GetString()));
            Assert.Equal(["Recibió", "Tel. de quien recibe"], history[2].GetProperty("questions").EnumerateArray().Select(item => item.GetString()));
            Assert.Equal((actorId, actorId), (history[0].GetProperty("actor").GetProperty("userId").GetGuid(), history[3].GetProperty("actor").GetProperty("userId").GetGuid()));
            Assert.All(history[1..3], item => Assert.Equal(JsonValueKind.Null, item.GetProperty("actor").ValueKind));
            Assert.False(history[3].TryGetProperty("questions", out _));
        }
        using (var list = await author.GetAsync($"/api/forms/definitions/{definitionId}/records", Ct))
        {
            var body = await list.Content.ReadAsStringAsync(Ct);
            Assert.True(list.StatusCode == HttpStatusCode.OK, body);
            Assert.Contains(completed.CompletedRecordId!.Value.ToString(), body, StringComparison.OrdinalIgnoreCase);
        }

        async Task WebhookAsync(params JsonObject[] inbound)
        {
            var payload = MetaWebhook.Build(contacts: [MetaWebhook.Contact(waId, null, "Persona de prueba")], messages: inbound);
            using var post = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(payload) };
            post.Content.Headers.ContentType = new("application/json");
            post.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(payload));
            using var webhook = await author.SendAsync(post, Ct);
            Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
            await host.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);
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

        // «Prellenado con» as the front's closed contract reads it: a label and a value, nothing else.
        static string Prefilled(params (string Label, string Value)[] values) => string.Join(" | ", values.Select(value => value.Label + ": " + value.Value));
        static string Prefill(JsonElement prefill) => string.Join(" | ", prefill.EnumerateArray().Select(value =>
        {
            Assert.Equal(["label", "value"], value.EnumerateObject().Select(property => property.Name));
            return value.GetProperty("label").GetString() + ": " + value.GetProperty("value").GetString();
        }));

        JsonObject ContactsMessage(DateTime sentAtUtc, string name, params string[] phones) => new()
        {
            ["from"] = waId, ["id"] = MetaWebhook.UniqueWaMessageId(), ["timestamp"] = MetaWebhook.Timestamp(sentAtUtc), ["type"] = "contacts",
            ["contacts"] = new JsonArray(new JsonObject
            {
                ["name"] = new JsonObject { ["formatted_name"] = name, ["first_name"] = name.Split(' ')[0] },
                ["phones"] = new JsonArray([.. phones.Select(phone => (JsonNode)new JsonObject { ["phone"] = phone, ["type"] = "CELL" })]),
            }),
        };
    }

    // «Registrar entrega»: a plain question and the three that receive what is shared (UC1-A), published through the real driver.
    private async Task<(Guid DefinitionId, Guid VersionId, string[] Keys)> PublishSharedFormAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, HttpClient author, FakeTimeProvider clock)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var created = await author.PostAsJsonAsync("/api/forms/definitions", new
        {
            requestId = Guid.NewGuid(), name = new { es = "Registrar entrega", en = "Register delivery" }, prefix = "EN" + suffix.ToUpperInvariant(),
            draft = new
            {
                format = 1, culture = "es", submitLabel = new { es = "Enviar", en = "Send" },
                fields = new object[]
                {
                    new { localId = "client", type = "shortText", required = true, maxLength = 80, label = new { es = "Cliente", en = "Client" } },
                    new { localId = "address", type = "shortText", required = false, maxLength = 120, label = new { es = "Dirección de entrega", en = "Delivery address" }, receives = "location" },
                    new { localId = "receiver", type = "shortText", required = false, maxLength = 60, label = new { es = "Recibió", en = "Received by" }, receives = "contactName" },
                    new { localId = "phone", type = "shortText", required = false, maxLength = 30, label = new { es = "Tel. de quien recibe", en = "Recipient phone" }, receives = "contactPhone" },
                },
            },
        }, Ct);
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var definition = (await created.ReadJsonAsync()).GetProperty("definition");
        var definitionId = definition.GetProperty("id").GetGuid();
        var keys = definition.GetProperty("draft").GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("key").GetString()!).ToArray();
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
        // The sealed projection says which question receives what; the Flow itself does not change.
        var versionId = receipt.GetProperty("versionId").GetGuid();
        var binding = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>().AsNoTracking().FirstAsync(value => value.VersionId == versionId, Ct));
        await using var scope = host.Services.CreateAsyncScope();
        var projection = scope.ServiceProvider.GetRequiredService<IWhatsAppFlowProtector>()
            .ReadProjection(binding.OperationId, binding.Id, binding.VersionId, binding.Culture, binding.ProtectedProjection);
        Assert.Equal([null, "location", "contactName", "contactPhone"], projection.Mapping.Select(field => field.Receives));
        return (definitionId, versionId, keys);
    }
}
