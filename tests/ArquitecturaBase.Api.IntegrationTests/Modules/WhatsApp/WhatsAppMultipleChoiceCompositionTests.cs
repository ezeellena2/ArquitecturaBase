using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Files;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Caching;
using ArquitecturaBase.Infrastructure.Files;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFormSendingTests
{
    [Fact]
    public async Task Twenty_checkbox_choices_and_two_files_capture_once_after_the_current_catalog_grows_past_its_send_limit()
    {
        Assert.SkipUnless(factory.Services.GetService<IStructuredFormPublicationDriver>() is not null,
            "The optional structured form publisher must be installed for this composition test.");
        await using var isolated = new ApiFactory();
        await isolated.InitializeAsync();
        await new WhatsAppFormSendingTests(isolated).AssertTwentyCheckboxChoicesAndFilesAsync();
    }

    private async Task AssertTwentyCheckboxChoicesAndFilesAsync()
    {
        await using var emulator = new AzuriteFixture();
        await emulator.StartAsync(Ct);
        var store = new AzureBlobFileStore(Options.Create(new FileStorageOptions
        {
            Enabled = true, CredentialMode = "ConnectionString", ConnectionString = emulator.ConnectionString,
        }));
        byte[] photo;
        using (var surface = SKSurface.Create(new SKImageInfo(64, 64)))
        {
            surface.Canvas.Clear(SKColors.SteelBlue);
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
            photo = data.ToArray();
        }
        var document = "%PDF-1.7\n1 0 obj<<>>endobj\n%%EOF"u8.ToArray();
        using var handler = new StubFlowHttpHandler(request =>
        {
            var id = request.RequestUri!.Host == "graph.facebook.com"
                ? request.RequestUri.Segments.Last() : request.RequestUri.AbsolutePath.Trim('/');
            var bytes = id == "123" ? photo : document;
            var type = id == "123" ? "image/jpeg" : "application/pdf";
            if (request.RequestUri.Host == "graph.facebook.com")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new
                {
                    id, mime_type = type, sha256 = Convert.ToBase64String(SHA256.HashData(bytes)),
                    file_size = bytes.Length, url = "https://lookaside.fbsbx.com/" + id,
                }) };
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        using var mediaHttp = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        var cloud = new CapturingWhatsAppCloudClient();
        await using var host = FormsHost(provider, cloud, null, clock).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPrivateFileStore>(); services.AddSingleton<IPrivateFileStore>(store);
            services.RemoveAll<IWhatsAppMediaClient>();
            services.AddSingleton<IWhatsAppMediaClient>(new WhatsAppMediaClient(mediaHttp, Options.Create(new WhatsAppOptions
            {
                PhoneNumberId = ApiFactory.WhatsAppPhoneNumberId, AccessToken = "synthetic-media-token", GraphApiVersion = "v25.0",
            }), TimeProvider.System));
            services.RemoveAll<IWhatsAppDispatchCoordinator>();
            services.AddSingleton<IWhatsAppDispatchCoordinator>(sp => new WhatsAppDispatchCoordinator(
                sp.GetRequiredService<RedisCache>(), TimeProvider.System));
        }));
        using var author = await OperatorAsync(host, "formtemplates.read", "formtemplates.manage", "formtemplates.publish",
            "formcatalogs.read", "formcatalogs.manage", "formrecords.read", "whatsapp.read", "whatsapp.send");
        using var employee = await OperatorAsync(host, "formrecords.create");
        var actorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var catalogNames = new { es = "Sectores " + suffix, en = "Areas " + suffix };
        using var catalogCreated = await author.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = catalogNames, description = catalogNames,
            items = Enumerable.Range(1, 20).Select(index => new
            {
                localId = "item-" + index, code = "C" + index,
                text = new { es = "Opción " + index, en = "Option " + index }, isActive = true,
            }).ToArray(),
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, catalogCreated.StatusCode);
        var catalog = await catalogCreated.ReadJsonAsync();
        var catalogId = catalog.GetProperty("catalog").GetProperty("id").GetGuid();
        Assert.Equal(20, catalog.GetProperty("itemIdentityMap").EnumerateObject().Count());
        using var created = await author.PostAsJsonAsync("/api/forms/definitions", new
        {
            requestId = Guid.NewGuid(), name = new { es = "Visita múltiple", en = "Multiple visit" },
            prefix = "MC" + suffix.ToUpperInvariant(), draft = new
            {
                format = 1, culture = "es", submitLabel = new { es = "Confirmar", en = "Confirm" },
                fields = new object[]
                {
                    new { localId = "areas", type = "multiChoice", label = new { es = "Sectores", en = "Areas" },
                        required = true, selection = new { minSelected = 1, maxSelected = 20 }, source = new { catalog = catalogId } },
                    new { localId = "photo", type = "photo", label = new { es = "Foto", en = "Photo" }, required = true,
                        files = new { minFiles = 1, maxFiles = 1, photoSource = "cameraOrGallery" } },
                    new { localId = "document", type = "document", label = new { es = "Documento", en = "Document" }, required = true,
                        files = new { minFiles = 1, maxFiles = 1 } },
                },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var definition = (await created.ReadJsonAsync()).GetProperty("definition");
        var definitionId = definition.GetProperty("id").GetGuid();
        var fields = definition.GetProperty("draft").GetProperty("fields").EnumerateArray().ToArray();
        var keys = fields.Select(field => field.GetProperty("key").GetString()!).ToArray();
        var multipleFieldId = fields[0].GetProperty("fieldId").GetGuid();
        var checkBody = JsonNode.Parse(definition.GetRawText())!.AsObject();
        foreach (var key in new[] { "id", "prefixLocked", "revision", "offerWhen" }) checkBody.Remove(key);
        checkBody["expectedRevision"] = definition.GetProperty("revision").GetInt64();
        for (var index = 0; index < fields.Length; index++)
        {
            var field = checkBody["draft"]!["fields"]![index]!;
            field["localId"] = "field-" + index;
            field.AsObject().Remove("key");
        }
        using var check = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publication-checks", checkBody, Ct);
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
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
        var bindings = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>()
            .Where(binding => binding.VersionId == versionId).ToArrayAsync(Ct));
        Assert.Equal(2, bindings.Length);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var protector = scope.ServiceProvider.GetRequiredService<IWhatsAppFlowProtector>();
            foreach (var binding in bindings)
            {
                var projection = protector.ReadProjection(binding.OperationId, binding.Id, binding.VersionId, binding.Culture, binding.ProtectedProjection);
                Assert.Equal("flow-static-7.3-r9", projection.ProfileRevision);
                var mapping = Assert.Single(projection.Mapping, field => field.Type == "multiChoice");
                Assert.Equal(20, mapping.Selection!.MaxSelected);
                using var json = JsonDocument.Parse(projection.Json);
                var checkbox = Assert.Single(json.RootElement.GetProperty("screens").EnumerateArray()
                    .SelectMany(screen => screen.GetProperty("layout").GetProperty("children")[0].GetProperty("children").EnumerateArray()),
                    component => component.GetProperty("type").GetString() == "CheckboxGroup");
                Assert.Equal(20, checkbox.GetProperty("max-selected-items").GetInt32());
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
            expectedRecipientContextRevision = context, message = "Completá la visita múltiple",
        }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        var flow = Assert.IsType<WhatsAppFlowMessageResponse>(Assert.Single(cloud.Messages));
        var offered = Assert.Single(flow.Catalogs);
        Assert.Equal(20, offered.Options.Count);
        var selected = offered.Options.Reverse().ToArray();
        var session = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().SingleAsync(value => value.ContactId == contactId, Ct));
        var sessionPayload = session.ProtectedPayload.ToArray();
        var sessionFingerprint = session.RequestFingerprint;
        using var grown = await author.PutAsJsonAsync("/api/forms/catalogs/" + catalogId, new
        {
            expectedRevision = 1, name = catalogNames, description = catalogNames,
            items = new[] { new { localId = "twenty-one", code = "C21", text = new { es = "Opción 21", en = "Option 21" }, isActive = true } },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, grown.StatusCode);
        using var blocked = await author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", new
        {
            requestId = Guid.NewGuid(), definitionId, versionId, expectedModeRevision = 0,
            expectedRecipientContextRevision = context, message = "No debe enviarse una lista parcial",
        }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("WhatsApp.Flow.CatalogPreparationBlocked", (await blocked.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Single(cloud.Messages);
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().CountAsync(value => value.ContactId == contactId, Ct)));
        var response = new JsonObject { ["flow_token"] = flow.FlowToken };
        response[keys[0]] = new JsonArray(selected.Select(option => JsonValue.Create(option.Id.ToString("D"))).ToArray());
        response[keys[1]] = new JsonArray(new JsonObject { ["id"] = "123", ["file_name"] = "site.jpg", ["mime_type"] = "image/jpeg", ["sha256"] = Convert.ToBase64String(SHA256.HashData(photo)) });
        response[keys[2]] = new JsonArray(new JsonObject { ["id"] = "124", ["file_name"] = "receipt.pdf", ["mime_type"] = "application/pdf", ["sha256"] = Convert.ToBase64String(SHA256.HashData(document)) });
        var firstMessage = MetaWebhook.UniqueWaMessageId();
        await DeliverAsync(firstMessage);
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IWhatsAppFlowReplyService>().ProcessPendingAsync(Ct);
        var completed = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().SingleAsync(value => value.Id == session.Id, Ct));
        Assert.Equal(WhatsAppFlowCaptureState.Completed, completed.CaptureState);
        Assert.NotNull(completed.CompletedRecordId);
        await using var readScope = host.Services.CreateAsyncScope();
        var source = readScope.ServiceProvider.GetRequiredService<IStructuredRecordSource>();
        var read = await source.ReadAsync(actorId, "es", [session.Id], Ct);
        Assert.True(read.CanReadRecords);
        var reply = Assert.Single(read.Replies.Values);
        Assert.Equal(completed.CompletedRecordId, reply.RecordId);
        Assert.EndsWith("-000001", reply.Number, StringComparison.Ordinal);
        var multiple = Assert.Single(reply.Values, value => value.FieldId == multipleFieldId);
        var received = Assert.IsAssignableFrom<IReadOnlyList<StructuredRecordChoiceValue>>(multiple.Value);
        Assert.Equal(selected.Select(option => option.Id.ToString("D")), received.Select(value => value.ChoiceId));
        Assert.Equal(selected.Select(option => option.Text), received.Select(value => value.Label));
        Assert.All(received, value => Assert.Equal(catalogId, value.CatalogId));
        var fileValues = reply.Values.Where(value => value.Type is "photo" or "document")
            .SelectMany(value => Assert.IsAssignableFrom<IReadOnlyList<StructuredRecordFileValue>>(value.Value)).ToArray();
        Assert.Equal(2, fileValues.Length);
        foreach (var file in fileValues)
        {
            using var content = await author.GetAsync($"/api/forms/records/{reply.RecordId}/files/{file.FileId}/content", Ct);
            Assert.Equal(HttpStatusCode.OK, content.StatusCode);
            Assert.Equal(file.ContentType == "image/jpeg" ? photo : document, await content.Content.ReadAsByteArrayAsync(Ct));
        }
        var mediaCalls = handler.Requests.Count;
        await DeliverAsync(MetaWebhook.UniqueWaMessageId());
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IWhatsAppFlowReplyService>().ProcessPendingAsync(Ct);
        var replay = await source.ReadAsync(actorId, "es", [session.Id], Ct);
        Assert.Equal(reply.RecordId, Assert.Single(replay.Replies.Values).RecordId);
        Assert.Equal(reply.Number, Assert.Single(replay.Replies.Values).Number);
        Assert.Equal(mediaCalls, handler.Requests.Count);
        using var sheet = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/records/search", new { pageSize = 20 }, Ct);
        Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
        Assert.Equal(1, (await sheet.ReadJsonAsync()).GetProperty("records").GetProperty("totalCount").GetInt32());
        var after = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().SingleAsync(value => value.Id == session.Id, Ct));
        Assert.Equal(sessionPayload, after.ProtectedPayload);
        Assert.Equal(sessionFingerprint, after.RequestFingerprint);
        foreach (var binding in bindings)
        {
            var current = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>().SingleAsync(value => value.Id == binding.Id, Ct));
            Assert.Equal(binding.ProtectedProjection, current.ProtectedProjection);
            Assert.Equal(WhatsAppFlowPublicationState.Published, current.State);
        }
        using var thread = await author.GetAsync($"/api/whatsapp/chats/{contactId}/messages", Ct);
        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
        var publicThread = (await thread.ReadJsonAsync()).GetRawText();
        Assert.DoesNotContain("flow_token", publicThread, StringComparison.Ordinal);
        Assert.DoesNotContain("protectedPayload", publicThread, StringComparison.OrdinalIgnoreCase);

        async Task DeliverAsync(string messageId)
        {
            var inbound = MetaWebhook.Text(messageId, waId, null, clock.GetUtcNow().UtcDateTime, "unused");
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
        }
    }
}
