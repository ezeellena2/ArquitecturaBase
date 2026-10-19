using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
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

public sealed partial class WhatsAppFormSendingTests
{
    [Fact]
    public async Task Published_language_changes_keep_accepted_sessions_and_capture_each_original_version()
    {
        Assert.SkipUnless(factory.Services.GetService<IStructuredFormPublicationDriver>() is not null,
            "The optional structured form publisher must be installed for this composition test.");
        await using var isolated = new ApiFactory();
        await isolated.InitializeAsync();
        await new WhatsAppFormSendingTests(isolated).AssertPublishedLanguageChangesAsync();
    }

    private async Task AssertPublishedLanguageChangesAsync()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        var transport = new CapturingFormReplyTransport();
        await using var host = FormsHost(provider, new CapturingWhatsAppCloudClient(), null, clock)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IWhatsAppCloudClient>(transport));
                // Redis expires the recipient cooldown in real time; lease waits use the same clock.
                services.Replace(ServiceDescriptor.Singleton<IWhatsAppDispatchCoordinator>(provider =>
                    new WhatsAppDispatchCoordinator(provider.GetRequiredService<RedisCache>(), TimeProvider.System)));
            }));
        using var author = await OperatorAsync(host, "formtemplates.read", "formtemplates.manage", "formtemplates.publish",
            WhatsAppPermissions.Read, WhatsAppPermissions.Send, "formrecords.read");
        using var employee = await OperatorAsync(host, "formrecords.create");
        var actorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        await factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IUserRepository>().UpdateProfileAsync(employeeId, "English recipient", "en", "UTC", Ct);
            return true;
        });
        var contactId = await CreateLinkedContactAsync(employeeId, clock);
        var waId = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppContact>()
            .Where(contact => contact.Id == contactId).Select(contact => contact.WaId).SingleAsync(Ct));
        using var created = await author.PostAsJsonAsync("/api/forms/definitions", new
        {
            requestId = Guid.NewGuid(), name = new { es = "Visita original" }, prefix = "L" + Guid.NewGuid().ToString("N")[..8],
            draft = new
            {
                format = 1, culture = "es", submitLabel = new { es = "Enviar visita" },
                fields = new[] { new { localId = "location", type = "shortText", label = new { es = "Lugar" }, required = true } }
            }
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.ReadJsonAsync();
        var definitionId = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var answerKey = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("key").GetString()!;
        var sessions = new List<(WhatsAppFlowSession Session, WhatsAppFlowMessageResponse Sent)>();
        var bindingSnapshots = new Dictionary<Guid, (string? MetaFlowId, byte[] ProtectedProjection, string Hash, string Json)>();

        var firstVersion = await PublishAndSendAsync(saved, 1, ["es"], "es");
        var translated = LanguageDraftAttempt(saved);
        translated["name"]!["en"] = "Translated visit";
        translated["draft"]!["submitLabel"]!["en"] = "Submit visit";
        translated["draft"]!["fields"]![0]!["label"]!["en"] = "Location";
        using var translationSaved = await author.PutAsJsonAsync($"/api/forms/definitions/{definitionId}/draft", translated, Ct);
        Assert.Equal(HttpStatusCode.OK, translationSaved.StatusCode);
        saved = await translationSaved.ReadJsonAsync();
        var secondVersion = await PublishAndSendAsync(saved, 2, ["en", "es"], "en");

        var incomplete = LanguageDraftAttempt(saved);
        incomplete["draft"]!["submitLabel"]!["en"] = "";
        using var incompleteSaved = await author.PutAsJsonAsync($"/api/forms/definitions/{definitionId}/draft", incomplete, Ct);
        Assert.Equal(HttpStatusCode.OK, incompleteSaved.StatusCode);
        saved = await incompleteSaved.ReadJsonAsync();
        var thirdVersion = await PublishAndSendAsync(saved, 3, ["es"], "es");
        Assert.Equal(3, new[] { firstVersion, secondVersion, thirdVersion }.Distinct().Count());
        Assert.Equal(4, provider.Creates);
        Assert.Equal(4, provider.Uploads);
        Assert.Equal(4, provider.Publishes);
        await AssertSealedContentAsync();
        Assert.Equal(3, await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>()
            .CountAsync(session => session.ContactId == contactId && session.CaptureState == WhatsAppFlowCaptureState.Pending, Ct)));

        var linkRevision = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppContact>()
            .Where(contact => contact.Id == contactId).Select(contact => contact.LinkRevision).SingleAsync(Ct));
        await factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IUserRepository>().UpdateProfileAsync(employeeId, "English recipient", "es", "UTC", Ct);
            return true;
        });
        Assert.Equal("es", await factory.ExecuteScopeAsync(async services =>
            (await services.GetRequiredService<IUserReader>().FindByIdAsync(employeeId, Ct))?.Culture));
        Assert.Equal(linkRevision, await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppContact>()
            .Where(contact => contact.Id == contactId).Select(contact => contact.LinkRevision).SingleAsync(Ct)));
        Assert.All(sessions, entry => Assert.Equal(linkRevision, entry.Session.LinkRevision));
        Assert.Equal("en", sessions[1].Session.Culture);
        await AssertSealedContentAsync();

        // Replies keep the delivered session culture after both the active version and the account language changed.
        foreach (var (session, sent) in sessions)
        {
            var response = new JsonObject { ["flow_token"] = sent.FlowToken, [answerKey] = "Visit " + session.VersionNumber };
            var waMessageId = MetaWebhook.UniqueWaMessageId();
            var inbound = MetaWebhook.Text(waMessageId, waId, null, clock.GetUtcNow().UtcDateTime, "unused");
            inbound.Remove("text");
            inbound["type"] = "interactive";
            inbound["interactive"] = new JsonObject { ["type"] = "nfm_reply", ["nfm_reply"] = new JsonObject
                { ["name"] = "flow", ["body"] = "Sent", ["response_json"] = response.ToJsonString() } };
            var body = MetaWebhook.Build(contacts: [MetaWebhook.Contact(waId, null, "English recipient")], messages: [inbound]);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new("application/json");
            request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));
            using var webhook = await author.SendAsync(request, Ct);
            Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
            await using (var captureScope = host.Services.CreateAsyncScope())
                await captureScope.ServiceProvider.GetRequiredService<IWhatsAppFlowReplyService>().ProcessPendingAsync(Ct);
            var captured = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>()
                .SingleAsync(row => row.Id == session.Id, Ct));
            Assert.Equal(WhatsAppFlowCaptureState.Completed, captured.CaptureState);
            Assert.NotNull(captured.CompletedRecordId);
            await using var readScope = host.Services.CreateAsyncScope();
            var read = await readScope.ServiceProvider.GetRequiredService<IStructuredRecordSource>()
                .ReadAsync(actorId, session.Culture, [session.Id], Ct);
            Assert.True(read.CanReadRecords);
            var reply = Assert.Single(read.Replies.Values);
            Assert.Equal(session.VersionId, reply.VersionId);
            Assert.Equal(session.VersionNumber, reply.VersionNumber);
            Assert.Equal(captured.CompletedRecordId, reply.RecordId);
            Assert.Equal(session.Culture == "en" ? "Translated visit" : "Visita original", reply.Name);
            var value = Assert.Single(reply.Values);
            Assert.Equal(session.Culture == "en" ? "Location" : "Lugar", value.Label);
            Assert.Equal("Visit " + session.VersionNumber, Assert.IsType<string>(value.Value));
            using var detail = await author.GetAsync($"/api/forms/records/{reply.RecordId}", Ct);
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var record = await detail.ReadJsonAsync();
            Assert.Equal(session.VersionId, record.GetProperty("versionId").GetGuid());
            Assert.Equal(session.VersionNumber, record.GetProperty("versionNumber").GetInt64());
        }
        await AssertSealedContentAsync();
        Assert.Equal(3, await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>()
            .CountAsync(session => session.ContactId == contactId && session.CaptureState == WhatsAppFlowCaptureState.Completed, Ct)));

        async Task<Guid> PublishAndSendAsync(JsonElement draft, int number, string[] cultures, string expectedCulture)
        {
            var attempt = LanguageDraftAttempt(draft);
            attempt.Remove("requestId");
            using var check = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publication-checks", attempt, Ct);
            Assert.Equal(HttpStatusCode.OK, check.StatusCode);
            var checkedBody = await check.ReadJsonAsync();
            Assert.True(checkedBody.GetProperty("canAccept").GetBoolean(), checkedBody.GetRawText());
            Assert.Equal(cultures, checkedBody.GetProperty("channels")[0].GetProperty("cultures").EnumerateArray()
                .Select(culture => culture.GetString()).Order(StringComparer.Ordinal));
            attempt["requestId"] = Guid.NewGuid().ToString();
            attempt["checkToken"] = checkedBody.GetProperty("checkToken").GetString();
            using var accepted = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", attempt, Ct);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            var receipt = await accepted.ReadJsonAsync();
            var versionId = receipt.GetProperty("versionId").GetGuid();
            var operationId = receipt.GetProperty("operationId").GetGuid();
            var driver = host.Services.GetRequiredService<IStructuredFormPublicationDriver>();
            var published = false;
            for (var step = 0; step < 25; step++)
            {
                await driver.ProcessAsync(operationId, Ct);
                await host.Services.GetRequiredService<WhatsAppFlowBackgroundService>().ProcessNextAsync(operationId, Ct);
                clock.Advance(TimeSpan.FromSeconds(16));
                using var status = await author.GetAsync(receipt.GetProperty("recoveryPath").GetString(), Ct);
                Assert.Equal(HttpStatusCode.OK, status.StatusCode);
                if ((await status.ReadJsonAsync()).GetProperty("state").GetString() == "Published") { published = true; break; }
            }
            Assert.True(published, "Both real publication workers must finish the version.");
            var bindings = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>()
                .Where(binding => binding.VersionId == versionId).ToArrayAsync(Ct));
            Assert.Equal(cultures, bindings.Select(binding => binding.Culture).Order(StringComparer.Ordinal));
            using (var projectionScope = host.Services.CreateScope())
            {
                var protector = projectionScope.ServiceProvider.GetRequiredService<IWhatsAppFlowProtector>();
                foreach (var binding in bindings)
                {
                    Assert.Equal(WhatsAppFlowPublicationState.Published, binding.State);
                    var projection = protector.ReadProjection(binding.OperationId, binding.Id, versionId, binding.Culture, binding.ProtectedProjection);
                    Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(projection.Json))), projection.Hash);
                    bindingSnapshots.Add(binding.Id, (binding.MetaFlowId, binding.ProtectedProjection.ToArray(), projection.Hash, projection.Json));
                }
            }
            using var choices = await author.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
            Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
            if (number == 1)
            {
                using var searchedChoices = await author.GetAsync($"/api/whatsapp/chats/{contactId}/forms?search=Visita%20original", Ct);
                Assert.Equal(HttpStatusCode.OK, searchedChoices.StatusCode);
                var searched = await searchedChoices.ReadJsonAsync();
                Assert.Equal("en", searched.GetProperty("recipientCulture").GetString());
                var choice = Assert.Single(searched.GetProperty("page").GetProperty("items").EnumerateArray());
                Assert.Equal(definitionId, choice.GetProperty("definitionId").GetGuid());
                Assert.Equal(versionId, choice.GetProperty("versionId").GetGuid());
                Assert.Equal("Visita original", choice.GetProperty("name").GetProperty("es").GetString());
            }
            using var preparation = await author.GetAsync($"/api/whatsapp/chats/{contactId}/forms/{definitionId}/versions/{versionId}/preparation", Ct);
            Assert.Equal(HttpStatusCode.OK, preparation.StatusCode);
            var preparationBody = await preparation.ReadJsonAsync();
            var prepared = preparationBody.GetProperty("preparation");
            Assert.True(prepared.GetProperty("canSend").GetBoolean());
            Assert.Equal(expectedCulture, preparationBody.GetProperty("culture").GetString());
            using var send = await author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", new
            {
                requestId = Guid.NewGuid(), definitionId, versionId, expectedModeRevision = 0,
                expectedRecipientContextRevision = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString(),
                message = (string?)null
            }, Ct);
            Assert.Equal(HttpStatusCode.Accepted, send.StatusCode);
            var previousCount = transport.Messages.Count;
            await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
            Assert.Equal(previousCount + 1, transport.Messages.Count);
            var sent = transport.Messages.Last();
            Assert.Equal(expectedCulture == "en" ? "Complete this form." : "Completá este formulario.", sent.Body);
            Assert.Equal(expectedCulture == "en" ? "Open form" : "Abrir formulario", sent.ButtonText);
            var session = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>()
                .SingleAsync(row => row.ContactId == contactId && row.VersionId == versionId, Ct));
            Assert.Equal(number, session.VersionNumber);
            Assert.Equal(expectedCulture, session.Culture);
            Assert.Equal(WhatsAppFlowCaptureState.Pending, session.CaptureState);
            Assert.Equal(bindings.Single(binding => binding.Culture == expectedCulture).Id, session.BindingId);
            Assert.Equal(bindings.Single(binding => binding.Culture == expectedCulture).MetaFlowId, sent.FlowId);
            sessions.Add((session, sent));
            return versionId;
        }

        async Task AssertSealedContentAsync()
        {
            using var projectionScope = host.Services.CreateScope();
            var protector = projectionScope.ServiceProvider.GetRequiredService<IWhatsAppFlowProtector>();
            foreach (var (id, before) in bindingSnapshots)
            {
                var binding = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>().SingleAsync(row => row.Id == id, Ct));
                Assert.Equal(WhatsAppFlowPublicationState.Published, binding.State);
                Assert.Equal(before.MetaFlowId, binding.MetaFlowId);
                Assert.Equal(before.ProtectedProjection, binding.ProtectedProjection);
                var projection = protector.ReadProjection(binding.OperationId, binding.Id, binding.VersionId, binding.Culture, binding.ProtectedProjection);
                Assert.Equal(before.Hash, projection.Hash);
                Assert.Equal(before.Json, projection.Json);
            }
            foreach (var (before, _) in sessions)
            {
                var session = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().SingleAsync(row => row.Id == before.Id, Ct));
                Assert.Equal(before.VersionId, session.VersionId);
                Assert.Equal(before.BindingId, session.BindingId);
                Assert.Equal(before.LinkRevision, session.LinkRevision);
                Assert.Equal(before.Culture, session.Culture);
                Assert.Equal(before.TokenHash, session.TokenHash);
                Assert.Equal(before.RequestFingerprint, session.RequestFingerprint);
                Assert.Equal(before.ProtectedPayload, session.ProtectedPayload);
                Assert.Equal(before.NameJson, session.NameJson);
            }
        }
    }

    private static JsonObject LanguageDraftAttempt(JsonElement saved)
    {
        var definition = saved.GetProperty("definition");
        var attempt = JsonNode.Parse(definition.GetRawText())!.AsObject();
        foreach (var key in new[] { "id", "prefixLocked", "revision", "offerWhen" }) attempt.Remove(key);
        attempt["expectedRevision"] = definition.GetProperty("revision").GetInt64();
        attempt["requestId"] = Guid.NewGuid().ToString();
        var fields = attempt["draft"]!["fields"]!.AsArray();
        for (var index = 0; index < fields.Count; index++)
        {
            fields[index]!["localId"] = "field-" + index;
            fields[index]!.AsObject().Remove("key");
        }
        return attempt;
    }
}
