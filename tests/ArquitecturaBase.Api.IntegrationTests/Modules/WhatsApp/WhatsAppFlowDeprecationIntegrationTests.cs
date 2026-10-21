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
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Caching;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFormSendingTests
{
    [Fact]
    public Task Replaced_and_deactivated_versions_keep_open_sessions_and_timely_pending_replies_until_capture() =>
        RunDeprecationCompositionAsync(tests => tests.AssertRetiredSessionCaptureAsync());

    [Theory]
    [InlineData(WhatsAppFlowRemoteState.Deprecated)]
    [InlineData(WhatsAppFlowRemoteState.Published)]
    [InlineData(WhatsAppFlowRemoteState.Unknown)]
    public Task Two_hosts_reconcile_an_uncertain_deprecation_before_any_repeated_post(WhatsAppFlowRemoteState observedState) =>
        RunDeprecationCompositionAsync(tests => tests.AssertDeprecationRecoveryAsync(observedState));

    [Fact]
    public Task Deleting_a_published_form_without_responses_retires_its_flow_without_an_open_session() =>
        RunDeprecationCompositionAsync(tests => tests.AssertDeletedFlowRetirementAsync());

    private async Task RunDeprecationCompositionAsync(Func<WhatsAppFormSendingTests, Task> run)
    {
        Assert.SkipUnless(factory.Services.GetService<IStructuredFormPublicationDriver>() is not null,
            "The optional structured form publisher must be installed for this composition test.");
        await using var isolated = new ApiFactory();
        await isolated.InitializeAsync();
        await run(new WhatsAppFormSendingTests(isolated));
    }

    private async Task AssertRetiredSessionCaptureAsync()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        var transport = new CapturingFormReplyTransport();
        await using var host = RetirementHost(provider, transport, clock);
        using var author = await RetirementAuthorAsync(host);
        using var employee = await OperatorAsync(host, "formrecords.create");
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var actorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var contactId = await CreateLinkedContactAsync(employeeId, clock);
        var saved = await CreateRetirementFormAsync(author, "Formulario anterior");
        var definitionId = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var key = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("key").GetString()!;
        var firstVersion = await PublishRetirementFormAsync(host, author, saved, clock);
        var firstBinding = await RetirementBindingAsync(firstVersion);
        var first = await SendRetirementSessionAsync(host, author, contactId, definitionId, firstVersion, transport);
        var edit = LanguageDraftAttempt(saved);
        edit["name"]!["es"] = "Formulario vigente";
        using var changed = await author.PutAsJsonAsync($"/api/forms/definitions/{definitionId}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        saved = await changed.ReadJsonAsync();
        var secondVersion = await PublishRetirementFormAsync(host, author, saved, clock);
        var secondBinding = await RetirementBindingAsync(secondVersion);
        var second = await SendRetirementSessionAsync(host, author, contactId, definitionId, secondVersion, transport);
        Assert.True(first.Session.ExpiresAtUtc < second.Session.ExpiresAtUtc);
        var worker = host.Services.GetRequiredService<WhatsAppFlowDeprecationBackgroundService>();
        await worker.ProcessNextAsync(firstBinding.Id, Ct);
        await worker.ProcessNextAsync(secondBinding.Id, Ct);
        Assert.Equal(0, provider.Deprecation.Posts);
        using (var scope = host.Services.CreateScope())
        {
            var unused = await scope.ServiceProvider.GetRequiredService<IStructuredFormRetirementSource>()
                .ListUnusedAsync([new(definitionId, firstVersion), new(definitionId, secondVersion)], Ct);
            Assert.Equal(firstVersion, Assert.Single(unused).VersionId);
        }
        using var deactivated = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/deactivation", new { expectedRevision = 2 }, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        using var choices = await author.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
        Assert.DoesNotContain((await choices.ReadJsonAsync()).GetProperty("page").GetProperty("items").EnumerateArray(),
            row => row.GetProperty("definitionId").GetGuid() == definitionId);
        await PostRetirementReplyAsync(author, first.Session, first.Message, key, clock);
        clock.SetUtcNow(new DateTimeOffset(first.Session.ExpiresAtUtc.AddSeconds(1)));
        Assert.True(clock.GetUtcNow().UtcDateTime < second.Session.ExpiresAtUtc);
        await worker.ProcessNextAsync(firstBinding.Id, Ct);
        await worker.ProcessNextAsync(secondBinding.Id, Ct);
        Assert.Equal(0, provider.Deprecation.Posts);
        using (var scope = host.Services.CreateScope())
        {
            var channelState = Assert.Single(await scope.ServiceProvider.GetRequiredService<IStructuredFormChannelStateSource>()
                .ListAsync([definitionId], clock.GetUtcNow().UtcDateTime, Ct));
            Assert.True(channelState.HasPendingReplies);
            Assert.Equal(second.Session.ExpiresAtUtc, channelState.LastOpenSessionExpiresAtUtc);
            await scope.ServiceProvider.GetRequiredService<IWhatsAppFlowReplyService>().ProcessPendingAsync(Ct);
        }
        var captured = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().AsNoTracking()
            .SingleAsync(row => row.Id == first.Session.Id, Ct));
        Assert.Equal(WhatsAppFlowCaptureState.Completed, captured.CaptureState);
        Assert.NotNull(captured.CompletedRecordId);
        using (var scope = host.Services.CreateScope())
        {
            var read = await scope.ServiceProvider.GetRequiredService<IStructuredRecordSource>().ReadAsync(actorId, "es", [captured.Id], Ct);
            Assert.True(read.CanReadRecords);
            var reply = Assert.Single(read.Replies.Values);
            Assert.Equal(firstVersion, reply.VersionId);
            Assert.Equal(captured.CompletedRecordId, reply.RecordId);
            Assert.Equal("Formulario anterior", reply.Name);
            Assert.Equal("Respuesta posterior a la baja", Assert.IsType<string>(Assert.Single(reply.Values).Value));
        }
        using var record = await author.GetAsync($"/api/forms/records/{captured.CompletedRecordId}", Ct);
        Assert.Equal(HttpStatusCode.OK, record.StatusCode);
        Assert.Equal(firstVersion, (await record.ReadJsonAsync()).GetProperty("versionId").GetGuid());
        await worker.ProcessNextAsync(firstBinding.Id, Ct);
        var retiredFirst = await RetirementBindingAsync(firstVersion);
        Assert.Equal(WhatsAppFlowDeprecationState.Deprecated, retiredFirst.DeprecationState);
        Assert.Equal(1, provider.Deprecation.Posts);
        await worker.ProcessNextAsync(secondBinding.Id, Ct);
        Assert.Equal(1, provider.Deprecation.Posts);
        Assert.Equal(WhatsAppFlowDeprecationState.Pending, (await RetirementBindingAsync(secondVersion)).DeprecationState);
        await AssertRetirementChannelStateAsync(host, definitionId, clock, StructuredFormDeprecationState.Pending,
            null, second.Session.ExpiresAtUtc);
        clock.SetUtcNow(new DateTimeOffset(second.Session.ExpiresAtUtc.AddSeconds(1)));
        await worker.ProcessNextAsync(secondBinding.Id, Ct);
        var retiredSecond = await RetirementBindingAsync(secondVersion);
        Assert.Equal(WhatsAppFlowDeprecationState.Deprecated, retiredSecond.DeprecationState);
        Assert.Equal(2, provider.Deprecation.Posts);
        Assert.Equal(new[] { "post:" + firstBinding.MetaFlowId, "post:" + secondBinding.MetaFlowId }, provider.Deprecation.Events);
        await AssertRetirementChannelStateAsync(host, definitionId, clock, StructuredFormDeprecationState.Deprecated,
            new[] { retiredFirst.DeprecatedAtUtc, retiredSecond.DeprecatedAtUtc }.Max());
        await AssertRetirementSealsAsync(host, firstBinding, first.Session);
        await AssertRetirementSealsAsync(host, secondBinding, second.Session);
    }

    private async Task AssertDeprecationRecoveryAsync(WhatsAppFlowRemoteState observedState)
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        provider.Deprecation.HoldNextPost = true;
        provider.Deprecation.UncertainOnce = true;
        provider.Deprecation.ApplyRemoteEffect = observedState != WhatsAppFlowRemoteState.Published;
        if (observedState == WhatsAppFlowRemoteState.Unknown) provider.Deprecation.ReadOverride = observedState;
        var transport = new CapturingFormReplyTransport();
        await using var firstHost = RetirementHost(provider, transport, clock);
        await using var secondHost = RetirementHost(provider, transport, clock);
        using var author = await RetirementAuthorAsync(firstHost);
        var saved = await CreateRetirementFormAsync(author, "Retiro incierto");
        var definitionId = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var versionId = await PublishRetirementFormAsync(firstHost, author, saved, clock);
        var before = await RetirementBindingAsync(versionId);
        using var deactivated = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/deactivation", new { expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        var firstWorker = firstHost.Services.GetRequiredService<WhatsAppFlowDeprecationBackgroundService>();
        var secondWorker = secondHost.Services.GetRequiredService<WhatsAppFlowDeprecationBackgroundService>();
        var firstAttempt = firstWorker.ProcessNextAsync(before.Id, Ct);
        try
        {
            await provider.Deprecation.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            var reserved = await RetirementBindingAsync(versionId);
            Assert.True(reserved.DeprecationInFlight);
            Assert.True(reserved.DeprecationFence > before.DeprecationFence);
            Assert.True(reserved.DeprecationLeaseExpiresAtUtc > clock.GetUtcNow().UtcDateTime);
            await secondWorker.ProcessNextAsync(before.Id, Ct);
            Assert.Equal(1, provider.Deprecation.Posts);
            Assert.Equal(0, provider.Deprecation.Reads);
        }
        finally { provider.Deprecation.Release.TrySetResult(); }
        await firstAttempt.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var uncertain = await RetirementBindingAsync(versionId);
        Assert.Equal(WhatsAppFlowDeprecationState.Uncertain, uncertain.DeprecationState);
        Assert.Null(uncertain.DeprecatedAtUtc);
        await AssertRetirementChannelStateAsync(firstHost, definitionId, clock, StructuredFormDeprecationState.Uncertain, null);
        AdvanceRetirementCheck(clock, uncertain);
        await secondWorker.ProcessNextAsync(before.Id, Ct);
        var checkedBinding = await RetirementBindingAsync(versionId);
        Assert.True(checkedBinding.DeprecationFence > uncertain.DeprecationFence);
        Assert.Equal(1, provider.Deprecation.Posts);
        Assert.Equal(1, provider.Deprecation.Reads);
        if (observedState == WhatsAppFlowRemoteState.Published)
        {
            Assert.Equal(WhatsAppFlowDeprecationState.Pending, checkedBinding.DeprecationState);
            provider.Deprecation.ApplyRemoteEffect = true;
            AdvanceRetirementCheck(clock, checkedBinding);
            await secondWorker.ProcessNextAsync(before.Id, Ct);
            Assert.Equal(2, provider.Deprecation.Posts);
            Assert.Equal(new[] { "post:" + before.MetaFlowId, "get:" + before.MetaFlowId, "post:" + before.MetaFlowId }, provider.Deprecation.Events);
        }
        else if (observedState == WhatsAppFlowRemoteState.Unknown)
        {
            Assert.Equal(WhatsAppFlowDeprecationState.Uncertain, checkedBinding.DeprecationState);
            AdvanceRetirementCheck(clock, checkedBinding);
            await secondWorker.ProcessNextAsync(before.Id, Ct);
            Assert.Equal(1, provider.Deprecation.Posts);
            Assert.Equal(2, provider.Deprecation.Reads);
            checkedBinding = await RetirementBindingAsync(versionId);
            Assert.Equal(WhatsAppFlowDeprecationState.Uncertain, checkedBinding.DeprecationState);
            provider.Deprecation.ReadOverride = null;
            AdvanceRetirementCheck(clock, checkedBinding);
            await secondWorker.ProcessNextAsync(before.Id, Ct);
            Assert.Equal(1, provider.Deprecation.Posts);
            Assert.Equal(3, provider.Deprecation.Reads);
        }
        var completed = await RetirementBindingAsync(versionId);
        Assert.Equal(WhatsAppFlowDeprecationState.Deprecated, completed.DeprecationState);
        Assert.NotNull(completed.DeprecatedAtUtc);
        await AssertRetirementChannelStateAsync(secondHost, definitionId, clock, StructuredFormDeprecationState.Deprecated, completed.DeprecatedAtUtc);
        await firstWorker.ProcessNextAsync(before.Id, Ct);
        Assert.Equal(observedState == WhatsAppFlowRemoteState.Published ? 2 : 1, provider.Deprecation.Posts);
        await AssertRetirementSealsAsync(firstHost, before);
    }

    private async Task AssertDeletedFlowRetirementAsync()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        var transport = new CapturingFormReplyTransport();
        await using var host = RetirementHost(provider, transport, clock);
        using var author = await RetirementAuthorAsync(host);
        var saved = await CreateRetirementFormAsync(author, "Publicado sin respuestas");
        var definitionId = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var versionId = await PublishRetirementFormAsync(host, author, saved, clock);
        var before = await RetirementBindingAsync(versionId);
        var deletedAt = clock.GetUtcNow();
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/forms/definitions/{definitionId}")
        { Content = JsonContent.Create(new { expectedRevision = 1 }) };
        using var deleted = await author.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var unavailable = await author.GetAsync($"/api/forms/definitions/{definitionId}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
        await host.Services.GetRequiredService<WhatsAppFlowDeprecationBackgroundService>().ProcessNextAsync(before.Id, Ct);
        Assert.Equal(deletedAt, clock.GetUtcNow());
        var completed = await RetirementBindingAsync(versionId);
        Assert.Equal(WhatsAppFlowDeprecationState.Deprecated, completed.DeprecationState);
        Assert.Equal(1, provider.Deprecation.Posts);
        Assert.Equal(0, provider.Deprecation.Reads);
        Assert.Equal("post:" + before.MetaFlowId, Assert.Single(provider.Deprecation.Events));
        await AssertRetirementChannelStateAsync(host, definitionId, clock, StructuredFormDeprecationState.Deprecated, completed.DeprecatedAtUtc);
        await AssertRetirementSealsAsync(host, before);
    }

    private WebApplicationFactory<Program> RetirementHost(CapturingFlowProvider provider, CapturingFormReplyTransport transport,
        FakeTimeProvider clock) => FormsHost(provider, new CapturingWhatsAppCloudClient(), null, clock)
        .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IWhatsAppCloudClient>(transport));
            services.Replace(ServiceDescriptor.Singleton<IWhatsAppDispatchCoordinator>(provider =>
                new WhatsAppDispatchCoordinator(provider.GetRequiredService<RedisCache>(), TimeProvider.System)));
        }));

    private Task<HttpClient> RetirementAuthorAsync(WebApplicationFactory<Program> host) => OperatorAsync(host,
        "formtemplates.read", "formtemplates.manage", "formtemplates.publish", "formrecords.read", WhatsAppPermissions.Read, WhatsAppPermissions.Send);

    private static async Task<JsonElement> CreateRetirementFormAsync(HttpClient author, string name)
    {
        using var created = await author.PostAsJsonAsync("/api/forms/definitions", new
        {
            requestId = Guid.NewGuid(), name = new { es = name }, prefix = "R" + Guid.NewGuid().ToString("N")[..8],
            draft = new { format = 1, culture = "es", submitLabel = new { es = "Enviar" }, fields = new[]
                { new { localId = "location", type = "shortText", label = new { es = "Lugar" }, required = true } } }
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await created.ReadJsonAsync();
    }

    private static async Task<Guid> PublishRetirementFormAsync(WebApplicationFactory<Program> host, HttpClient author,
        JsonElement saved, FakeTimeProvider clock)
    {
        var definitionId = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var attempt = LanguageDraftAttempt(saved);
        attempt.Remove("requestId");
        using var check = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publication-checks", attempt, Ct);
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var checkedBody = await check.ReadJsonAsync();
        Assert.True(checkedBody.GetProperty("canAccept").GetBoolean());
        attempt["requestId"] = Guid.NewGuid().ToString();
        attempt["checkToken"] = checkedBody.GetProperty("checkToken").GetString();
        using var accepted = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        var operationId = receipt.GetProperty("operationId").GetGuid();
        for (var step = 0; step < 25; step++)
        {
            await host.Services.GetRequiredService<IStructuredFormPublicationDriver>().ProcessAsync(operationId, Ct);
            await host.Services.GetRequiredService<WhatsAppFlowBackgroundService>().ProcessNextAsync(operationId, Ct);
            clock.Advance(TimeSpan.FromSeconds(16));
            using var status = await author.GetAsync(receipt.GetProperty("recoveryPath").GetString(), Ct);
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            if ((await status.ReadJsonAsync()).GetProperty("state").GetString() == "Published") return receipt.GetProperty("versionId").GetGuid();
        }
        Assert.Fail("Both real publication workers must finish the version.");
        return Guid.Empty;
    }

    private Task<WhatsAppFlowBinding> RetirementBindingAsync(Guid versionId) => factory.ExecuteDbContextAsync(db =>
        db.Set<WhatsAppFlowBinding>().AsNoTracking().SingleAsync(row => row.VersionId == versionId, Ct));

    private async Task<(WhatsAppFlowSession Session, WhatsAppFlowMessageResponse Message)> SendRetirementSessionAsync(
        WebApplicationFactory<Program> host, HttpClient author, Guid contactId, Guid definitionId, Guid versionId, CapturingFormReplyTransport transport)
    {
        using var choices = await author.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
        Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        var context = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
        using var sent = await author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", new
        {
            requestId = Guid.NewGuid(), definitionId, versionId, expectedModeRevision = 0,
            expectedRecipientContextRevision = context, message = (string?)null
        }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        var before = transport.Messages.Count;
        await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        Assert.Equal(before + 1, transport.Messages.Count);
        var session = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().AsNoTracking()
            .SingleAsync(row => row.ContactId == contactId && row.VersionId == versionId, Ct));
        return (session, transport.Messages.Last());
    }

    private async Task PostRetirementReplyAsync(HttpClient author, WhatsAppFlowSession session, WhatsAppFlowMessageResponse sent,
        string key, FakeTimeProvider clock)
    {
        var waId = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppContact>().Where(row => row.Id == session.ContactId).Select(row => row.WaId).SingleAsync(Ct));
        var response = new JsonObject { ["flow_token"] = sent.FlowToken, [key] = "Respuesta posterior a la baja" };
        var inbound = MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), waId, null, clock.GetUtcNow().UtcDateTime, "unused");
        inbound.Remove("text");
        inbound["type"] = "interactive";
        inbound["interactive"] = new JsonObject { ["type"] = "nfm_reply", ["nfm_reply"] = new JsonObject
            { ["name"] = "flow", ["body"] = "Sent", ["response_json"] = response.ToJsonString() } };
        var body = MetaWebhook.Build(contacts: [MetaWebhook.Contact(waId, null, "Persona de prueba")], messages: [inbound]);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));
        using var webhook = await author.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
    }

    private async Task AssertRetirementSealsAsync(WebApplicationFactory<Program> host, WhatsAppFlowBinding before,
        WhatsAppFlowSession? beforeSession = null)
    {
        var after = await RetirementBindingAsync(before.VersionId);
        Assert.Equal(before.State, after.State);
        Assert.Equal(before.Step, after.Step);
        Assert.Equal(before.Fence, after.Fence);
        Assert.Equal(before.MetaFlowId, after.MetaFlowId);
        Assert.Equal(before.TechnicalName, after.TechnicalName);
        Assert.Equal(before.ProtectedProjection, after.ProtectedProjection);
        using var scope = host.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IWhatsAppFlowProtector>();
        var original = protector.ReadProjection(before.OperationId, before.Id, before.VersionId, before.Culture, before.ProtectedProjection);
        var current = protector.ReadProjection(after.OperationId, after.Id, after.VersionId, after.Culture, after.ProtectedProjection);
        Assert.Equal(original.Json, current.Json);
        Assert.Equal(original.Hash, current.Hash);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(current.Json))), current.Hash);
        if (beforeSession is null) return;
        var session = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().AsNoTracking().SingleAsync(row => row.Id == beforeSession.Id, Ct));
        Assert.Equal(beforeSession.VersionId, session.VersionId);
        Assert.Equal(beforeSession.BindingId, session.BindingId);
        Assert.Equal(beforeSession.LinkRevision, session.LinkRevision);
        Assert.Equal(beforeSession.Culture, session.Culture);
        Assert.Equal(beforeSession.NameJson, session.NameJson);
        Assert.Equal(beforeSession.TokenHash, session.TokenHash);
        Assert.Equal(beforeSession.RequestFingerprint, session.RequestFingerprint);
        Assert.Equal(beforeSession.ProtectedPayload, session.ProtectedPayload);
    }

    private static async Task AssertRetirementChannelStateAsync(WebApplicationFactory<Program> host, Guid definitionId,
        FakeTimeProvider clock, StructuredFormDeprecationState expectedState, DateTime? expectedConfirmedAtUtc,
        DateTime? expectedOpenExpiryUtc = null)
    {
        using var scope = host.Services.CreateScope();
        var state = Assert.Single(await scope.ServiceProvider.GetRequiredService<IStructuredFormChannelStateSource>()
            .ListAsync([definitionId], clock.GetUtcNow().UtcDateTime, Ct));
        Assert.Equal(definitionId, state.DefinitionId);
        Assert.Equal(expectedState, state.DeprecationState);
        Assert.Equal(expectedConfirmedAtUtc, state.DeprecatedAtUtc);
        Assert.Equal(expectedOpenExpiryUtc, state.LastOpenSessionExpiresAtUtc);
        Assert.False(state.HasPendingReplies);
    }
    private static void AdvanceRetirementCheck(FakeTimeProvider clock, WhatsAppFlowBinding binding)
    {
        Assert.NotNull(binding.DeprecationNextCheckAtUtc);
        var next = binding.DeprecationNextCheckAtUtc.Value.AddSeconds(1);
        Assert.True(next > clock.GetUtcNow().UtcDateTime);
        clock.SetUtcNow(new DateTimeOffset(next));
    }
}
