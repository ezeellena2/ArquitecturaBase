using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Caching;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed record AssistantLatencyContact(Guid UserId, Guid ContactId, string WaId);
internal sealed record AssistantLatencySend(WhatsAppOutboundMessage Message, long Timestamp);

internal sealed class WhatsAppAssistantLatencyCloud : IWhatsAppCloudClient
{
    public ConcurrentQueue<AssistantLatencySend> Sent { get; } = new();
    public Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Sent.Enqueue(new(message, Stopwatch.GetTimestamp()));
        return Task.FromResult(WhatsAppSendResult.Sent("wamid.latency-" + Guid.NewGuid().ToString("N")));
    }
}

internal sealed class WhatsAppAssistantLatencyScenario : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> setup;
    private readonly WebApplicationFactory<Program> host;
    public WhatsAppAssistantLatencyCloud Cloud { get; } = new();
    public WhatsAppAssistantLatencyHttpTrace Trace { get; } = new();
    public WhatsAppAudioDispatchProbe Audio { get; } = new();
    public Guid DefinitionId { get; private set; }
    public Guid VersionId { get; private set; }
    public string FieldKey { get; private set; } = "";
    private Guid actor;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WhatsAppAssistantLatencyScenario(ApiFactory factory)
    {
        var clock = new FakeTimeProvider(TimeProvider.System.GetUtcNow().AddHours(-1));
        var flow = new CapturingFlowProvider(clock);
        var connection = factory.NewDatabaseConnectionString("assistant_latency");
        setup = factory.WithWebHostBuilder(builder => builder
            .UseSetting("ConnectionStrings:appdb", connection)
            .UseSetting("WhatsApp:WabaId", "222")
            .UseSetting("Logging:LogLevel:Default", "Warning")
            .ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
                services.Replace(ServiceDescriptor.Singleton<IWhatsAppFlowProvider>(flow));
            }));
        host = setup.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ArtificialIntelligence:Assistant:ProcessInBackground", "true")
                .UseSetting("WhatsApp:ProcessInboundInBackground", "true")
                .UseSetting("WhatsApp:ProcessOutgoingInBackground", "true")
                .UseSetting("WhatsApp:ProcessFlowsInBackground", "true")
                .UseSetting("ArtificialIntelligence:Assistant:AudioModelProfiles:0:Id", "latency-audio")
                .UseSetting("ArtificialIntelligence:Assistant:AudioModelProfiles:0:Version", "1")
                .UseSetting("ArtificialIntelligence:Assistant:AudioModelProfiles:0:Provider", "OpenAI")
                .UseSetting("ArtificialIntelligence:Assistant:AudioModelProfiles:0:Model", "fictional-audio")
                .UseSetting("ArtificialIntelligence:Assistant:AudioModelProfiles:0:MaximumAudioBytes", "1000000")
                .UseSetting("ArtificialIntelligence:Assistant:AudioModelProfiles:0:ContentTypes:0", "audio/ogg");
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(TimeProvider.System));
                services.PostConfigure<WhatsAppOptions>(options => options.ProcessFlowsInBackground = true);
                services.Replace(ServiceDescriptor.Singleton<IWhatsAppCloudClient>(Cloud));
                services.Replace(ServiceDescriptor.Singleton<IWhatsAppMediaClient>(Audio));
                services.Replace(ServiceDescriptor.Singleton<IPrivateFileStore>(Audio));
                services.Replace(ServiceDescriptor.Singleton<IWhatsAppDispatchCoordinator>(provider =>
                    new WhatsAppDispatchCoordinator(provider.GetRequiredService<RedisCache>(), TimeProvider.System)));
                services.AddHttpClient("AiAssistantOpenAI").ConfigurePrimaryHttpMessageHandler(() => new WhatsAppAssistantLatencyHttpHandler(Trace));
                services.AddHttpClient("AiAssistantOpenAIAudio").ConfigurePrimaryHttpMessageHandler(() => new WhatsAppAssistantLatencyHttpHandler(Trace));
            });
        });
    }

    public static async Task<WhatsAppAssistantLatencyScenario> CreateAsync(ApiFactory factory, bool publishForm = true)
    {
        var scenario = new WhatsAppAssistantLatencyScenario(factory);
        try
        {
            using var setupClient = scenario.setup.CreateClient();
            await using (var scope = scenario.setup.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync(Ct);
            await scenario.setup.Services.SeedDatabaseAsync(Ct);
            scenario.actor = await ScopeAsync(scenario.setup, services =>
                WriteAsync(services, async token =>
                {
                    var name = "latency-" + Guid.NewGuid().ToString("N");
                    await services.GetRequiredService<IRoleRepository>().CreateAsync(name, null,
                        ["whatsapp.read", "whatsapp.send", "whatsapp.configureassistant", "formtemplates.read", "formtemplates.manage",
                         "formtemplates.publish", "formrecords.read", "formrecords.create", "ai.read", "ai.manage"], token);
                    var user = await services.GetRequiredService<IUserRepository>().CreateAsync(
                        Email.Create(name + "@example.com").Value, null, false, "Operador ficticio", "es", token);
                    await services.GetRequiredService<IUserRepository>().SetRolesAsync(user.Id, [name], token);
                    return user.Id;
                }));
            setupClient.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, scenario.actor.ToString());
            using (var current = await setupClient.GetAsync("/api/ai/connection", Ct))
            {
                var revision = (await current.ReadJsonAsync()).GetProperty("revision").GetInt64();
                using var configured = await setupClient.PutAsJsonAsync("/api/ai/connection", new
                {
                    requestId = Guid.NewGuid(), provider = "OpenAI", model = "fictional-model", credentialChange = "Replace",
                    apiKey = "fictional-assistant-key", enabled = true, expectedRevision = revision,
                }, Ct);
                Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
            }
            if (publishForm) await scenario.PublishAsync(setupClient);
            scenario.measurementClient = scenario.host.CreateClient();
            scenario.measurementClient.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, scenario.actor.ToString());
            return scenario;
        }
        catch { await scenario.DisposeAsync(); throw; }
    }

    private HttpClient? measurementClient;
    public HttpClient Client => measurementClient ?? throw new InvalidOperationException("The measurement host has not started.");

    public Task<AssistantLatencyContact> ContactAsync(bool automatic) => ScopeAsync(host, services => WriteAsync(services, async token =>
    {
        var phone = PhoneNumber.Create("+" + MetaWebhook.UniqueWaId()).Value;
        var users = services.GetRequiredService<IUserRepository>();
        var user = await users.CreateAsync(null, phone, true, "Persona ficticia", "es", token);
        var role = "latency-recipient-" + Guid.NewGuid().ToString("N");
        await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, ["formrecords.create"], token);
        await users.SetRolesAsync(user.Id, [role], token);
        var contact = WhatsAppContact.Create(phone.Value[1..], MetaWebhook.UniqueBsuid(), "Persona ficticia", TimeProvider.System.GetUtcNow().UtcDateTime);
        contact.LinkUser(user.Id);
        if (automatic) contact.ChangeMode(WhatsAppConversationMode.Assistant);
        services.GetRequiredService<ApplicationDbContext>().Add(contact);
        return new AssistantLatencyContact(user.Id, contact.Id, phone.Value[1..]);
    }));

    public async Task PostTextAsync(AssistantLatencyContact contact)
    {
        var message = MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), contact.WaId, null,
            TimeProvider.System.GetUtcNow().UtcDateTime, "Visité Taller Central.");
        await PostWebhookAsync(contact, message);
    }

    public async Task PostAudioAsync(AssistantLatencyContact contact)
    {
        var message = MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), contact.WaId, null,
            TimeProvider.System.GetUtcNow().UtcDateTime, "unused");
        message.Remove("text");
        message["type"] = "audio";
        message["audio"] = new JsonObject
        {
            ["id"] = "123456789", ["mime_type"] = "audio/ogg; codecs=opus", ["sha256"] = Convert.ToBase64String(Convert.FromHexString(Audio.Sha256)), ["voice"] = true,
        };
        await PostWebhookAsync(contact, message);
        var stored = await ReadAsync(db => db.Set<WhatsAppMessage>().AsNoTracking()
            .SingleAsync(row => row.ContactId == contact.ContactId && row.Kind == WhatsAppMessageKind.Audio, Ct));
        Assert.NotEqual(WhatsAppAudioState.Unavailable, stored.AudioState);
        Assert.NotNull(stored.AudioMediaId);
    }

    private async Task PostWebhookAsync(AssistantLatencyContact contact, JsonObject message)
    {
        var body = MetaWebhook.Build(contacts: [MetaWebhook.Contact(contact.WaId, null, "Persona ficticia")], messages: [message]);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));
        using var response = await Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public async Task<WhatsAppFlowSession> SendInitialFlowAsync(AssistantLatencyContact contact)
    {
        using var choices = await Client.GetAsync($"/api/whatsapp/chats/{contact.ContactId}/forms", Ct);
        Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        var context = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
        using var sent = await Client.PostAsJsonAsync($"/api/whatsapp/chats/{contact.ContactId}/form-messages", new
        {
            requestId = Guid.NewGuid(), definitionId = DefinitionId, versionId = VersionId, expectedModeRevision = 0,
            expectedRecipientContextRevision = context, message = (string?)null,
        }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        await WaitAsync(() => Task.FromResult(Cloud.Sent.Any(item => item.Message is WhatsAppFlowMessageResponse flow && flow.To.Value[1..] == contact.WaId)));
        using var changed = await Client.PutAsJsonAsync($"/api/whatsapp/chats/{contact.ContactId}/mode",
            new { mode = "Assistant", expectedRevision = 0 }, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        return await ReadAsync(db => db.Set<WhatsAppFlowSession>().AsNoTracking().SingleAsync(row => row.ContactId == contact.ContactId, Ct));
    }

    public async Task<string> PostReplyAsync(AssistantLatencyContact contact)
    {
        var flow = Assert.IsType<WhatsAppFlowMessageResponse>(Assert.Single(Cloud.Sent).Message);
        var response = new JsonObject { ["flow_token"] = flow.FlowToken, [FieldKey] = "Taller Central" };
        var id = MetaWebhook.UniqueWaMessageId();
        var message = MetaWebhook.Text(id, contact.WaId, null, TimeProvider.System.GetUtcNow().UtcDateTime, "unused");
        message.Remove("text"); message["type"] = "interactive";
        message["interactive"] = new JsonObject { ["type"] = "nfm_reply", ["nfm_reply"] = new JsonObject
        {
            ["name"] = "flow", ["body"] = "Sent", ["response_json"] = response.ToJsonString(),
        }};
        await PostWebhookAsync(contact, message);
        return id;
    }

    public async Task SendManualTextAsync(AssistantLatencyContact contact)
    {
        using var sent = await Client.PostAsJsonAsync($"/api/whatsapp/chats/{contact.ContactId}/messages",
            new { requestId = Guid.NewGuid(), body = "Mensaje ficticio de medición." }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
    }

    public Task<T> ReadAsync<T>(Func<ApplicationDbContext, Task<T>> read) => ScopeAsync(host, services => read(services.GetRequiredService<ApplicationDbContext>()));

    public static async Task WaitAsync(Func<Task<bool>> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        while (!await condition())
            await Task.Delay(TimeSpan.FromMilliseconds(100), TimeProvider.System, deadline.Token);
    }

    public static async Task RecordAsync(string name, long started, object phases)
    {
        var output = Environment.GetEnvironmentVariable("ASSISTANT_LATENCY_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var line = JsonSerializer.Serialize(new { scenario = name, elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds, phases });
        Directory.CreateDirectory(output);
        await File.AppendAllTextAsync(Path.Combine(output, "measurements.jsonl"), line + Environment.NewLine, Ct);
    }

    private async Task PublishAsync(HttpClient author)
    {
        using var created = await author.PostAsJsonAsync("/api/forms/definitions", new
        {
            requestId = Guid.NewGuid(), name = new { es = "Visita de medición" }, prefix = "L" + Guid.NewGuid().ToString("N")[..8],
            offerWhen = "Cuando la persona indique que visitó un taller.",
            draft = new { format = 1, culture = "es", submitLabel = new { es = "Enviar" }, fields = new[]
            {
                new { localId = "location", type = "shortText", label = new { es = "Lugar" }, required = true, maxLength = 120 },
            }},
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var definition = (await created.ReadJsonAsync()).GetProperty("definition");
        DefinitionId = definition.GetProperty("id").GetGuid();
        FieldKey = definition.GetProperty("draft").GetProperty("fields")[0].GetProperty("key").GetString()!;
        var attempt = JsonNode.Parse(definition.GetRawText())!.AsObject();
        foreach (var property in new[] { "id", "prefixLocked", "revision", "offerWhen" }) attempt.Remove(property);
        attempt["expectedRevision"] = definition.GetProperty("revision").GetInt64();
        var fields = attempt["draft"]!["fields"]!.AsArray();
        fields[0]!["localId"] = "location"; fields[0]!.AsObject().Remove("key");
        using var checkedResponse = await author.PostAsJsonAsync($"/api/forms/definitions/{DefinitionId}/publication-checks", attempt, Ct);
        Assert.Equal(HttpStatusCode.OK, checkedResponse.StatusCode);
        attempt["requestId"] = Guid.NewGuid().ToString();
        attempt["checkToken"] = (await checkedResponse.ReadJsonAsync()).GetProperty("checkToken").GetString();
        using var accepted = await author.PostAsJsonAsync($"/api/forms/definitions/{DefinitionId}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync(); VersionId = receipt.GetProperty("versionId").GetGuid();
        var operationId = receipt.GetProperty("operationId").GetGuid();
        var clock = (FakeTimeProvider)setup.Services.GetRequiredService<TimeProvider>();
        for (var step = 0; step < 20; step++)
        {
            await setup.Services.GetRequiredService<IStructuredFormPublicationDriver>().ProcessAsync(operationId, Ct);
            await setup.Services.GetRequiredService<WhatsAppFlowBackgroundService>().ProcessNextAsync(operationId, Ct);
            clock.Advance(TimeSpan.FromSeconds(16));
            using var status = await author.GetAsync(receipt.GetProperty("recoveryPath").GetString(), Ct);
            if ((await status.ReadJsonAsync()).GetProperty("state").GetString() == "Published") return;
        }
        Assert.Fail("The real publication workers must complete fixture setup.");
    }

    private static async Task<T> ScopeAsync<T>(WebApplicationFactory<Program> factory, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private static async Task<T> WriteAsync<T>(IServiceProvider services, Func<CancellationToken, Task<T>> action)
    {
        T value = default!;
        var result = await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct =>
        {
            value = await action(ct); return Result.Success();
        }, CommitPolicy.OnSuccess, Ct);
        Assert.True(result.IsSuccess);
        return value;
    }

    public async ValueTask DisposeAsync()
    {
        measurementClient?.Dispose();
        await host.DisposeAsync();
        try
        {
            await using var scope = setup.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync(CancellationToken.None);
        }
        finally { await setup.DisposeAsync(); }
    }
}
