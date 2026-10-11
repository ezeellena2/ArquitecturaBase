using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Caching;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFormSendingTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Real_http_reply_cards_preserve_exact_values_and_hide_them_without_read_permission(string culture)
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var snapshot = ReplySnapshot();
        var records = new CapturingStructuredRecordPort(snapshot);
        var selection = new CapturingStructuredFormSource();
        var transport = new CapturingFormReplyTransport();
        var preview = WhatsAppFormAppPreview.FromEnvironment(culture);
        await using var host = FormsHost(new CapturingFlowProvider(clock), new CapturingWhatsAppCloudClient(), selection, clock)
            .WithWebHostBuilder(builder =>
            {
                preview?.Configure(builder);
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IWhatsAppCloudClient>(); services.AddSingleton<IWhatsAppCloudClient>(transport);
                    // Redis measures its cooldown in real time; a stopped fake timer would never retry acquisition.
                    services.RemoveAll<IWhatsAppDispatchCoordinator>();
                    services.AddSingleton<IWhatsAppDispatchCoordinator>(provider =>
                        new WhatsAppDispatchCoordinator(provider.GetRequiredService<RedisCache>(), TimeProvider.System));
                    services.RemoveAll<IStructuredRecordSink>(); services.AddSingleton<IStructuredRecordSink>(records);
                    services.RemoveAll<IStructuredRecordSource>();
                    services.AddScoped<IStructuredRecordSource>(provider => records.Source(provider.GetRequiredService<IPermissionService>()));
                });
            });
        if (preview is not null) WhatsAppFormAppPreview.Listen(host);
        using var author = await OperatorAsync(host, WhatsAppPermissions.Read, WhatsAppPermissions.Send,
            "formtemplates.publish", "formrecords.read");
        using var withoutValues = await OperatorAsync(host, WhatsAppPermissions.Read);
        using var withoutChat = await OperatorAsync(host, "formrecords.read");
        author.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        withoutValues.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var contactId = await CreateLinkedContactAsync(await FixtureEmployeeAsync(culture), clock);
        var waId = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppContact>()
            .Where(contact => contact.Id == contactId).Select(contact => contact.WaId).SingleAsync(Ct));
        var definitionId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        selection.Form = new(definitionId, versionId, 1, "VIS", snapshot.Name, snapshot.Questions.Count, snapshot.SubmitLabel);
        var channel = Assert.Single(host.Services.GetServices<IStructuredFormChannel>(), item => item.ChannelId == "phone");
        var check = (await channel.CheckAsync(snapshot, Ct)).Value;
        Assert.Empty(check.Issues);
        var operationId = Guid.NewGuid();
        Assert.True((await channel.AcceptAsync(new(operationId, definitionId, versionId, authorId,
            ["formtemplates.publish"], "reply-fixture-publication", check.ProfileRevision,
            check.Cultures.Select(item => new StructuredFormPublicationCulture(item.Culture, item.CompilationFingerprint)).ToArray(), snapshot), Ct)).IsSuccess);
        for (var index = 0; index < 12; index++)
        {
            await host.Services.GetRequiredService<WhatsAppFlowBackgroundService>().ProcessNextAsync(operationId, Ct);
            clock.Advance(TimeSpan.FromSeconds(16));
        }
        var route = $"/api/whatsapp/chats/{contactId}/messages";
        var receivedIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var flow = await SendFormAsync();
        var answers = new JsonObject
        {
            ["flow_token"] = flow.FlowToken,
            [snapshot.Questions[0].Key] = "Equipo de prueba",
            // Long text intentionally absent: null must not become an empty string, false or zero.
            [snapshot.Questions[2].Key] = culture == "es"
                ? "12345678901234567890,12345678" : "12345678901234567890.12345678",
            [snapshot.Questions[3].Key] = "2026-10-11",
            [snapshot.Questions[4].Key] = "false",
            [snapshot.Questions[5].Key] = snapshot.Questions[5].Options![0].ChoiceId.ToString("D"),
        };
        var replyId = await ReceiveAsync(answers);
        var pending = await CaptureAsync("reply-pending", author.GetAsync(route, Ct), HttpStatusCode.OK);
        var pendingCard = Card(pending, replyId);
        Assert.Equal("Pending", pendingCard.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, pendingCard.GetProperty("values").ValueKind);
        await ProcessRepliesAsync();
        var completed = await CaptureAsync("reply-completed", author.GetAsync(route, Ct), HttpStatusCode.OK);
        Assert.True(completed.GetProperty("canReadFormRecords").GetBoolean());
        var card = Card(completed, replyId);
        Assert.Equal("Completed", card.GetProperty("state").GetString());
        Assert.Equal("VIS-000024", card.GetProperty("number").GetString());
        Assert.Equal(snapshot.Name[culture], card.GetProperty("name").GetString());
        var values = card.GetProperty("values").EnumerateArray().ToArray();
        Assert.Equal(6, values.Length);
        Assert.Equal(JsonValueKind.Null, values[1].GetProperty("value").ValueKind);
        Assert.Equal("12345678901234567890.12345678", values[2].GetProperty("value").GetString());
        Assert.Equal("2026-10-11", values[3].GetProperty("value").GetString());
        Assert.False(values[4].GetProperty("value").GetBoolean());
        Assert.Equal(snapshot.Questions[5].Options![0].Label[culture], values[5].GetProperty("value").GetProperty("label").GetString());
        Assert.All(values, value => Assert.NotEmpty(value.GetProperty("label").GetString()!));
        var reloaded = await CaptureAsync("reply-reloaded", author.GetAsync(route, Ct), HttpStatusCode.OK);
        Assert.Equal(card.GetRawText(), Card(reloaded, replyId).GetRawText());
        var privateCard = await CaptureAsync("reply-without-read", withoutValues.GetAsync(route, Ct), HttpStatusCode.OK);
        Assert.False(privateCard.GetProperty("canReadFormRecords").GetBoolean());
        Assert.Equal("VIS-000024", Card(privateCard, replyId).GetProperty("number").GetString());
        Assert.Equal(JsonValueKind.Null, Card(privateCard, replyId).GetProperty("values").ValueKind);
        foreach (var (client, suffix) in new[] { (author, "read"), (withoutValues, "without-read") })
        {
            var inbox = await CaptureAsync("reply-inbox-" + suffix,
                client.GetAsync("/api/whatsapp/chats?pageSize=100", Ct), HttpStatusCode.OK);
            var contact = inbox.GetProperty("page").GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == contactId);
            Assert.Equal(culture == "en" ? "Form response" : "Respuesta del formulario",
                contact.GetProperty("preview").GetString());
        }
        using var forbidden = await withoutChat.GetAsync(route, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.DoesNotContain("Equipo de prueba", await forbidden.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        var rejectedFlow = await SendFormAsync();
        var rejectedId = await ReceiveAsync(new JsonObject { ["flow_token"] = rejectedFlow.FlowToken,
            [snapshot.Questions[0].Key] = "reject-fixture" });
        await ProcessRepliesAsync();
        var rejected = await CaptureAsync("reply-rejected", author.GetAsync(route, Ct), HttpStatusCode.OK);
        Assert.Equal("Rejected", Card(rejected, rejectedId).GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, Card(rejected, rejectedId).GetProperty("values").ValueKind);
        var unknownId = await ReceiveAsync(new JsonObject { ["flow_token"] = "unknown-fixture-token" });
        await ProcessRepliesAsync();
        var unrecognized = await CaptureAsync("reply-unrecognized", author.GetAsync(route, Ct), HttpStatusCode.OK);
        var unknown = Card(unrecognized, unknownId);
        Assert.Equal("Unrecognized", unknown.GetProperty("state").GetString());
        foreach (var field in new[] { "definitionId", "versionId", "versionNumber", "name", "recordId", "number", "values" })
            Assert.Equal(JsonValueKind.Null, unknown.GetProperty(field).ValueKind);
        var stillPending = await SendFormAsync();
        await ReceiveAsync(new JsonObject { ["flow_token"] = stillPending.FlowToken, [snapshot.Questions[0].Key] = "Pendiente" });
        await CaptureAsync("reply-all-states", author.GetAsync(route, Ct), HttpStatusCode.OK);
        var latest = await CaptureAsync("reply-latest-page", author.GetAsync(route + "?limit=2", Ct), HttpStatusCode.OK);
        Assert.Equal(2, latest.GetProperty("messages").GetArrayLength());
        var beforeAt = latest.GetProperty("beforeAtUtc").GetString();
        Assert.NotNull(beforeAt);
        var beforeId = latest.GetProperty("beforeId").GetGuid();
        await CaptureAsync("reply-older-page", author.GetAsync(route + "?limit=2&beforeAtUtc="
            + Uri.EscapeDataString(beforeAt) + "&beforeId=" + beforeId.ToString("D"), Ct), HttpStatusCode.OK);

        if (preview is not null)
            await preview.HoldAsync(factory, host,
                [("reply-read-" + culture, authorId, contactId, culture),
                 ("reply-denied-" + culture, Guid.Parse(withoutValues.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single()), contactId, culture)],
                clock, new ControlledWhatsAppSendClient(), Ct);

        async Task<ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppFlowMessageResponse> SendFormAsync()
        {
            using var choices = await author.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
            Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
            var context = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
            using var accepted = await author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages",
                new { requestId = Guid.NewGuid(), definitionId, versionId, expectedModeRevision = 0,
                    expectedRecipientContextRevision = context, message = "Formulario de prueba" }, Ct);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
            return transport.Messages.Last();
        }

        async Task<string> ReceiveAsync(JsonObject response)
        {
            var id = MetaWebhook.UniqueWaMessageId();
            var message = MetaWebhook.Text(id, waId, null, clock.GetUtcNow().UtcDateTime, "unused");
            message.Remove("text"); message["type"] = "interactive";
            message["interactive"] = new JsonObject { ["type"] = "nfm_reply",
                ["nfm_reply"] = new JsonObject { ["name"] = "flow", ["body"] = "Sent", ["response_json"] = response.ToJsonString() } };
            var body = MetaWebhook.Build(contacts: [MetaWebhook.Contact(waId, null, "Empleado de prueba")], messages: [message]);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new("application/json"); request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));
            using var result = await author.SendAsync(request, Ct);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            receivedIds.Add(id, await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppMessage>()
                .Where(item => item.WaMessageId == id).Select(item => item.Id).SingleAsync(Ct)));
            return id;
        }

        async Task ProcessRepliesAsync()
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IWhatsAppFlowReplyService>().ProcessPendingAsync(Ct);
        }

        async Task<JsonElement> CaptureAsync(string name, Task<HttpResponseMessage> responseTask, HttpStatusCode status)
        {
            using var response = await responseTask; Assert.Equal(status, response.StatusCode);
            var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
            var output = Environment.GetEnvironmentVariable("WHATSAPP_FORM_REPLIES_HTTP_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                var directory = Path.Combine(output, culture); Directory.CreateDirectory(directory);
                await File.WriteAllBytesAsync(Path.Combine(directory, name + ".json"), bytes, Ct);
            }
            using var document = JsonDocument.Parse(bytes); return document.RootElement.Clone();
        }

        JsonElement Card(JsonElement thread, string waMessageId)
        {
            var id = receivedIds[waMessageId];
            var message = Assert.Single(thread.GetProperty("messages").EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);
            Assert.Equal("FlowReply", message.GetProperty("kind").GetString());
            Assert.Equal(JsonValueKind.Null, message.GetProperty("body").ValueKind);
            return message.GetProperty("formReply");
        }
    }

    private static StructuredFormCheckRequest ReplySnapshot()
    {
        var types = new[] { "shortText", "longText", "decimal", "date", "boolean", "choice" };
        var spanish = new[] { "Equipo", "Observaciones", "Cantidad", "Fecha", "Seguimiento", "Tipo" };
        var english = new[] { "Equipment", "Notes", "Quantity", "Date", "Follow up", "Type" };
        var questions = types.Select((type, index) =>
        {
            var id = Guid.NewGuid();
            return new StructuredFormQuestion(id, "field_" + id.ToString("N", CultureInfo.InvariantCulture), type,
                Texts(spanish[index], english[index]), null, false, type == "shortText" ? 100 : type == "longText" ? 600 : null,
                type == "choice" ? [new StructuredFormChoice(Guid.NewGuid(), Texts("Preventiva", "Preventive"))] : null);
        }).ToArray();
        return new(1, "es", Texts("Visita de prueba", "Test visit"), Texts("Confirmar visita", "Confirm visit"), questions, ["es", "en"]);

        static IReadOnlyDictionary<string, string> Texts(string es, string en) => new Dictionary<string, string> { ["es"] = es, ["en"] = en };
    }
}
