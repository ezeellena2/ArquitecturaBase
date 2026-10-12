using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
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
    public async Task Published_r3_flow_remains_sendable_and_its_open_session_is_captured_after_r5_publication()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var flowProvider = new CapturingFlowProvider(clock);
        var cloud = new CapturingWhatsAppCloudClient();
        var source = new CapturingStructuredFormSource();
        var legacySnapshot = LegacyR3Snapshot();
        var r3VersionId = Guid.NewGuid();
        var records = new CapturingStructuredRecordPort(legacySnapshot);
        await using var host = FormsHost(flowProvider, cloud, source, clock)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStructuredRecordSink>();
                services.AddSingleton<IStructuredRecordSink>(records);
                services.RemoveAll<IStructuredRecordSource>();
                services.AddScoped<IStructuredRecordSource>(provider =>
                    records.Source(provider.GetRequiredService<IPermissionService>()));
            }));
        using var author = await OperatorAsync(host, WhatsAppPermissions.Read, WhatsAppPermissions.Send,
            "formtemplates.publish");
        using var employee = await OperatorAsync(host, "formrecords.create");
        var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var definitionId = Guid.NewGuid();
        var r5VersionId = Guid.NewGuid();
        var r3OperationId = Guid.NewGuid();
        var r5OperationId = Guid.NewGuid();
        var publicationFingerprint = Hash("r3 publication");
        var bindings = new List<WhatsAppFlowBinding>();
        var legacyHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var legacyProtectedProjections = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        Guid? contactId = null;
        Guid? outgoingId = null;
        string? replyMessageId = null;
        string? flowToken = null;

        try
        {
            using var scope = host.Services.CreateScope();
            var protector = scope.ServiceProvider.GetRequiredService<IWhatsAppFlowProtector>();
            var environment = scope.ServiceProvider.GetRequiredService<IWhatsAppFlowEnvironment>();
            var accountResult = await environment.VerifyAccountAsync(Ct);
            Assert.True(accountResult.IsSuccess);
            var currentAccount = accountResult.Value;
            var account = currentAccount with
            {
                ConfigurationFingerprint = Hash($"{currentAccount.WabaId}\n{currentAccount.PhoneNumberId}\n{currentAccount.GraphApiVersion}\nflow-static-7.3-r3"),
            };
            Assert.NotEqual(currentAccount.ConfigurationFingerprint, account.ConfigurationFingerprint);
            var r3Operation = WhatsAppFlowOperation.Accept(r3OperationId, definitionId, r3VersionId, authorId);
            var manifest = new WhatsAppFlowStoredManifest(1, Hash("r3 request"), publicationFingerprint,
                account, ["formtemplates.publish"]);
            r3Operation.SealManifest(protector.ProtectManifest(r3Operation.Id, r3Operation.NeutralOperationId,
                r3Operation.VersionId, manifest));

            var flowIds = new Dictionary<string, string>(StringComparer.Ordinal)
                { ["es"] = "771003", ["en"] = "771004" };
            var fixtureAssembly = typeof(WhatsAppFormSendingTests).Assembly;
            foreach (var culture in new[] { "es", "en" })
            {
                var json = await ReadFixtureAsync(fixtureAssembly, $"FlowProfile.flow-static-7.3-r3-{culture}.json");
                var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
                var technicalName = $"legacy-r3-{culture}";
                var binding = WhatsAppFlowBinding.Create(r3Operation.Id, r3VersionId, culture, technicalName, []);
                var mapping = legacySnapshot.Questions.Select(question => new WhatsAppFlowProjectionField(
                    question.FieldId, question.Key, question.Type, question.Required, question.MaxLength,
                    question.Options?.Select(option => option.ChoiceId).ToArray() ?? [])).ToArray();
                var projection = new WhatsAppFlowProjection(1, json, hash, "flow-static-7.3-r3", 1, culture, mapping);
                var protectedProjection = protector.ProtectProjection(r3Operation.Id, binding.Id, r3VersionId, culture, projection);
                binding.SealProjection(protectedProjection);
                legacyHashes.Add(culture, hash);
                legacyProtectedProjections.Add(culture, protectedProjection.ToArray());
                SealAsPublished(binding, flowIds[culture], clock.GetUtcNow().UtcDateTime);
                bindings.Add(binding);
                flowProvider.SeedPublished(flowIds[culture], technicalName, json);
            }
            r3Operation.Release(clock.GetUtcNow().UtcDateTime, completed: true, requiresReview: false);

            await factory.ExecuteDbContextAsync(async db =>
            {
                db.Set<WhatsAppFlowOperation>().Add(r3Operation);
                db.Set<WhatsAppFlowBinding>().AddRange(bindings);
                await db.SaveChangesAsync(Ct);
            });

            var oldSelection = Selection(definitionId, r3VersionId, 3, legacySnapshot);
            source.Form = oldSelection;
            source.PublishedForms.Add(oldSelection);
            var channel = Assert.Single(host.Services.GetServices<IStructuredFormChannel>(), item => item.ChannelId == "phone");
            var query = new StructuredFormPublicationQuery(r3Operation.NeutralOperationId, definitionId, r3VersionId, publicationFingerprint);
            var beforeR5 = await channel.GetPublicationAsync(query, Ct);
            Assert.True(beforeR5.IsSuccess);
            Assert.Equal("Published", beforeR5.Value.State);
            Assert.False(beforeR5.Value.RequiresReview);

            var contact = await CreateLinkedContactAsync(employeeId, clock);
            contactId = contact;
            var waId = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppContact>()
                .Where(item => item.Id == contact).Select(item => item.WaId).SingleAsync(Ct));
            using var choices = await author.GetAsync($"/api/whatsapp/chats/{contact}/forms", Ct);
            Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
            var recipientContext = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
            using var acceptedSend = await author.PostAsJsonAsync($"/api/whatsapp/chats/{contact}/form-messages",
                new { requestId = Guid.NewGuid(), definitionId, versionId = r3VersionId, expectedModeRevision = 0,
                    expectedRecipientContextRevision = recipientContext, message = "Completá el formulario" }, Ct);
            Assert.Equal(HttpStatusCode.Accepted, acceptedSend.StatusCode);
            outgoingId = (await acceptedSend.ReadJsonAsync()).GetProperty("id").GetGuid();
            await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
            var sent = Assert.IsType<WhatsAppFlowMessageResponse>(Assert.Single(cloud.Messages));
            Assert.Equal("771003", sent.FlowId);
            flowToken = sent.FlowToken;

            var openedSessionId = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>()
                .Where(item => item.ContactId == contact && item.VersionId == r3VersionId)
                .Select(item => item.Id).SingleAsync(Ct));
            records.AcceptedReceiptId = openedSessionId;

            var r5Snapshot = Snapshot();
            var checkResult = await channel.CheckAsync(r5Snapshot, Ct);
            Assert.True(checkResult.IsSuccess);
            Assert.Equal("flow-static-7.3-r5", checkResult.Value.ProfileRevision);
            Assert.Empty(checkResult.Value.Issues);
            var r5Request = new StructuredFormPublicationRequest(r5OperationId, definitionId, r5VersionId, authorId,
                ["formtemplates.publish"], Hash("r5 publication"), checkResult.Value.ProfileRevision,
                checkResult.Value.Cultures.Select(item => new StructuredFormPublicationCulture(item.Culture,
                    item.CompilationFingerprint)).ToArray(), r5Snapshot);
            Assert.True((await channel.AcceptAsync(r5Request, Ct)).IsSuccess);
            var worker = host.Services.GetRequiredService<WhatsAppFlowBackgroundService>();
            for (var attempt = 0; attempt < 12; attempt++)
            {
                await worker.ProcessNextAsync(r5OperationId, Ct);
                clock.Advance(TimeSpan.FromSeconds(16));
            }

            var r5Publication = await channel.GetPublicationAsync(new(r5OperationId, definitionId, r5VersionId,
                r5Request.PublicationFingerprint), Ct);
            Assert.True(r5Publication.IsSuccess);
            Assert.Equal("Published", r5Publication.Value.State);
            Assert.False(r5Publication.Value.RequiresReview);
            var r5Bindings = await factory.ExecuteDbContextAsync(async db =>
            {
                var operation = await db.Set<WhatsAppFlowOperation>().SingleAsync(item => item.NeutralOperationId == r5OperationId, Ct);
                return await db.Set<WhatsAppFlowBinding>().Where(item => item.OperationId == operation.Id).ToListAsync(Ct);
            });
            foreach (var binding in r5Bindings)
            {
                var projection = scope.ServiceProvider.GetRequiredService<IWhatsAppFlowProtector>().ReadProjection(
                    binding.OperationId, binding.Id, binding.VersionId, binding.Culture, binding.ProtectedProjection);
                Assert.Equal("flow-static-7.3-r5", projection.ProfileRevision);
                Assert.NotEqual(legacyHashes[binding.Culture], projection.Hash);
            }
            var r3AfterR5 = await factory.ExecuteDbContextAsync(async db => await db.Set<WhatsAppFlowBinding>()
                .Where(item => item.OperationId == r3Operation.Id).ToListAsync(Ct));
            foreach (var binding in r3AfterR5)
            {
                Assert.Equal(legacyProtectedProjections[binding.Culture], binding.ProtectedProjection);
                var projection = protector.ReadProjection(binding.OperationId, binding.Id, binding.VersionId,
                    binding.Culture, binding.ProtectedProjection);
                Assert.Equal("flow-static-7.3-r3", projection.ProfileRevision);
                Assert.Equal(legacyHashes[binding.Culture], projection.Hash);
            }

            source.Form = Selection(definitionId, r5VersionId, 4, r5Snapshot);
            source.PublishedForms.Add(source.Form);
            var afterR5 = await channel.GetPublicationAsync(query, Ct);
            Assert.True(afterR5.IsSuccess);
            Assert.Equal("Published", afterR5.Value.State);
            Assert.False(afterR5.Value.RequiresReview);
            var preview = await channel.GetPreviewAsync(new(r3Operation.NeutralOperationId, definitionId, r3VersionId,
                publicationFingerprint, "es"), Ct);
            Assert.True(preview.IsSuccess);
            Assert.Equal("771003", flowProvider.LastPreviewFlowId);

            var responseJson = new JsonObject
            {
                ["flow_token"] = flowToken,
                [legacySnapshot.Questions[0].Key] = "Equipo legado",
                [legacySnapshot.Questions[1].Key] = "Observación del Flow r3",
                [legacySnapshot.Questions[2].Key] = "12,50",
                [legacySnapshot.Questions[3].Key] = "2026-10-11",
                [legacySnapshot.Questions[4].Key] = "false",
                [legacySnapshot.Questions[5].Key] = LegacyChoiceId.ToString("D"),
            };
            replyMessageId = MetaWebhook.UniqueWaMessageId();
            var inbound = MetaWebhook.Text(replyMessageId, waId, null, clock.GetUtcNow().UtcDateTime, "ignored");
            inbound.Remove("text");
            inbound["type"] = "interactive";
            inbound["interactive"] = new JsonObject
            {
                ["type"] = "nfm_reply",
                ["nfm_reply"] = new JsonObject
                    { ["name"] = "flow", ["body"] = "Sent", ["response_json"] = responseJson.ToJsonString() },
            };
            var body = MetaWebhook.Build(contacts: [MetaWebhook.Contact(waId, null, "Empleado r3")], messages: [inbound]);
            using (var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp")
                   { Content = new ByteArrayContent(body) })
            {
                request.Content.Headers.ContentType = new("application/json");
                request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));
                using var webhook = await author.SendAsync(request, Ct);
                Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
            }
            await using (var replyScope = host.Services.CreateAsyncScope())
                await replyScope.ServiceProvider.GetRequiredService<IWhatsAppFlowReplyService>().ProcessPendingAsync(Ct);

            var capture = Assert.Single(records.Captures);
            Assert.Equal(openedSessionId, capture.SourceReceiptId);
            Assert.All(records.IgnoredCaptures, ignored => Assert.NotEqual(openedSessionId, ignored.SourceReceiptId));
            Assert.All(records.IgnoredCaptures, ignored => Assert.NotEqual(r3VersionId, ignored.VersionId));
            Assert.Equal(r3VersionId, capture.VersionId);
            Assert.Equal(definitionId, capture.DefinitionId);
            Assert.Equal("12.5", capture.Answers[legacySnapshot.Questions[2].Key]);
            Assert.Equal("false", capture.Answers[legacySnapshot.Questions[4].Key]);
            Assert.Equal("2026-10-11", capture.Answers[legacySnapshot.Questions[3].Key]);
            var reply = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppMessage>()
                .SingleAsync(item => item.WaMessageId == replyMessageId, Ct));
            Assert.Equal(WhatsAppFlowCaptureState.Completed, reply.FlowCaptureState);
        }
        finally
        {
            if (contactId is { } testContactId)
            {
                await factory.ExecuteDbContextAsync(async db =>
                {
                    if (replyMessageId is not null)
                    {
                        var replies = await db.Set<WhatsAppMessage>().Where(item => item.WaMessageId == replyMessageId).ToListAsync(Ct);
                        db.RemoveRange(replies);
                    }
                    var sessions = await db.Set<WhatsAppFlowSession>()
                        .Where(item => item.ContactId == testContactId && item.VersionId == r3VersionId).ToListAsync(Ct);
                    var sessionIds = sessions.Select(item => item.Id).ToArray();
                    var linkedMessages = await db.Set<WhatsAppMessage>()
                        .Where(item => item.FlowSessionId.HasValue && sessionIds.Contains(item.FlowSessionId.Value)).ToListAsync(Ct);
                    db.RemoveRange(linkedMessages);
                    if (outgoingId is { } testOutgoingId)
                    {
                        var outgoing = await db.Set<WhatsAppOutgoingMessage>().Where(item => item.Id == testOutgoingId).ToListAsync(Ct);
                        db.RemoveRange(outgoing);
                    }
                    db.RemoveRange(sessions);
                    await db.SaveChangesAsync(Ct);
                });
            }
            await factory.ExecuteDbContextAsync(async db =>
            {
                var operations = await db.Set<WhatsAppFlowOperation>()
                    .Where(item => item.NeutralOperationId == r3OperationId || item.NeutralOperationId == r5OperationId)
                    .ToListAsync(Ct);
                var operationIds = operations.Select(item => item.Id).ToArray();
                var ownBindings = await db.Set<WhatsAppFlowBinding>()
                    .Where(item => operationIds.Contains(item.OperationId)).ToListAsync(Ct);
                db.RemoveRange(ownBindings);
                db.RemoveRange(operations);
                await db.SaveChangesAsync(Ct);
            });
        }
    }

    private static readonly Guid LegacyChoiceId = Guid.Parse("019a0000-0000-7000-8000-000000000100");

    private static StructuredFormCheckRequest LegacyR3Snapshot()
    {
        var types = new[] { "shortText", "longText", "decimal", "date", "boolean", "choice" };
        var questions = types.Select((type, index) =>
        {
            var id = Guid.Parse($"019a0000-0000-7000-8000-{index + 1:000000000000}");
            var help = type == "decimal"
                ? new Dictionary<string, string> { ["es"] = "Ingresá un número; usá punto o coma para los decimales.", ["en"] = "Enter a number; use a dot or comma for decimals." }
                : null;
            IReadOnlyList<StructuredFormChoice>? options = type == "choice"
                ? [new StructuredFormChoice(LegacyChoiceId, Text("Opción", "Option"))] : null;
            return new StructuredFormQuestion(id, "field_" + id.ToString("N", CultureInfo.InvariantCulture), type,
                Text("Pregunta", "Question"), help, true, type switch { "shortText" => 80, "longText" => 600, _ => null }, options);
        }).ToArray();
        return new(1, "es", Text("Formulario", "Form"), Text("Enviar", "Submit"), questions, ["es", "en"]);

        static IReadOnlyDictionary<string, string> Text(string es, string en) => new Dictionary<string, string>
            { ["es"] = es, ["en"] = en };
    }

    private static StructuredFormSelectionRow Selection(Guid definitionId, Guid versionId, int versionNumber,
        StructuredFormCheckRequest snapshot) => new(definitionId, versionId, versionNumber, "LEG", snapshot.Name,
        snapshot.Questions.Count, snapshot.SubmitLabel);

    private static void SealAsPublished(WhatsAppFlowBinding binding, string flowId, DateTime nowUtc)
    {
        Assert.Equal(WhatsAppFlowStep.Create, binding.Begin(1, nowUtc).Value);
        Assert.True(binding.MarkEffectStarted(1).IsSuccess);
        Assert.True(binding.RecordEffect(1, nowUtc, flowId, true, false, false).IsSuccess);
        Assert.Equal(WhatsAppFlowStep.Upload, binding.Begin(2, nowUtc).Value);
        Assert.True(binding.MarkEffectStarted(2).IsSuccess);
        Assert.True(binding.RecordEffect(2, nowUtc, null, true, false, false).IsSuccess);
        Assert.Equal(WhatsAppFlowStep.Validate, binding.Begin(3, nowUtc).Value);
        Assert.True(binding.RecordVerification(3, nowUtc, false, true, true, true, true).IsSuccess);
        Assert.Equal(WhatsAppFlowStep.Publish, binding.Begin(4, nowUtc).Value);
        Assert.True(binding.MarkEffectStarted(4).IsSuccess);
        Assert.True(binding.RecordEffect(4, nowUtc, null, true, false, false).IsSuccess);
        Assert.Equal(WhatsAppFlowStep.Verify, binding.Begin(5, nowUtc).Value);
        Assert.True(binding.RecordVerification(5, nowUtc, true, false, true, true, true).IsSuccess);
        Assert.Equal(WhatsAppFlowPublicationState.Published, binding.State);
    }

    private static async Task<string> ReadFixtureAsync(System.Reflection.Assembly assembly, string resourceName)
    {
        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing legacy Flow fixture {resourceName}.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(Ct);
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
