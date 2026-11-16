using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// I2: removing a Home conversation purges, in the same unit, the readings and the transcripts its turns named, while another conversation
// that took the same reading keeps its sealed copy; a reading or a recording no turn named expires after 24 hours, like an orphan copy.
// PostgreSQL, the real engine and worker, fictional readers and providers, private storage in memory and, for the 24 hours, a clock of its own.
[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantHomeRemovalTests(ApiFactory factory)
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private static readonly byte[] Webm = [0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 0x01, 0x42, 0xF7, 0x81];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Removing_a_conversation_purges_the_reading_and_the_transcript_its_turns_named_and_another_conversation_keeps_its_copy()
    {
        using var provider = DocumentReadingHandler.Json(DocumentReadingHandler.Completed(DocumentReadingHandler.Reading()));
        await using var scenario = await CreateAsync(provider);
        var (documentRequestId, photoKey) = await ReadPhotoAsync(scenario);
        var (audioRequestId, audioKey) = await NormalizeAsync(scenario);

        // One conversation names the photo and the recording; another takes the same photo, with its own sealed copy.
        var removedId = (await SubmitAsync(scenario, Guid.NewGuid(), documentRequestId, audioRequestId)).GetProperty("conversationId").GetGuid();
        Assert.True(await scenario.ProcessNextAsync());
        var keptTurn = Guid.NewGuid();
        var keptId = (await SubmitAsync(scenario, keptTurn, documentRequestId)).GetProperty("conversationId").GetGuid();
        Assert.True(await scenario.ProcessNextAsync());
        Assert.Equal([audioKey, photoKey], (await RetainedAsync(scenario, photoKey, audioKey)).Order(StringComparer.Ordinal));

        using (var removed = await scenario.Client.DeleteAsync($"/api/ai/assistant/conversations/{removedId}", Ct))
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        // The reading and the transcription are left without content, in the database and for their person.
        Assert.Equal(2, await ScopeDbAsync(scenario, db => db.Set<AiAssistantTurn>().IgnoreQueryFilters().CountAsync(turn =>
            (turn.RequestId == documentRequestId || turn.RequestId == audioRequestId) && turn.State == AiAssistantTurnState.Completed
            && turn.ProtectedQuestion == null && turn.ProtectedAnswer == null, Ct)));
        Assert.Equal(("completed", JsonValueKind.Null), await DocumentAsync(scenario, documentRequestId));
        Assert.Equal(("completed", JsonValueKind.Null), await TranscriptAsync(scenario, audioRequestId));
        using (var recording = await scenario.Client.GetAsync($"/api/ai/assistant/audio/{audioRequestId}/file", Ct))
            Assert.Equal("Ai.Assistant.Expired", (await recording.ReadJsonAsync()).GetProperty("code").GetString());
        // A new turn that names them no longer takes them.
        foreach (var (document, audio) in new (Guid?, Guid?)[] { (documentRequestId, null), (null, audioRequestId) })
        {
            using var again = await PostTurnAsync(scenario, Guid.NewGuid(), document, audio);
            Assert.Equal("Ai.Assistant.Expired", (await again.ReadJsonAsync()).GetProperty("code").GetString());
        }

        // The cleanup no longer keeps the recording; the photo stays for the other conversation, whose turn still shows it.
        Assert.Equal([photoKey], await RetainedAsync(scenario, photoKey, audioKey));
        using (var kept = await scenario.Client.GetAsync($"/api/ai/assistant/turns/{keptTurn}", Ct))
            Assert.Equal("foto.jpg", (await kept.ReadJsonAsync()).GetProperty("attachment").GetProperty("name").GetString());
        // Removing that one too releases the photo.
        using (var removed = await scenario.Client.DeleteAsync($"/api/ai/assistant/conversations/{keptId}", Ct))
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty(await RetainedAsync(scenario, photoKey, audioKey));
    }

    [Fact]
    public async Task A_reading_or_a_recording_that_no_turn_named_expires_after_24_hours()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        using var provider = DocumentReadingHandler.Json(DocumentReadingHandler.Completed(DocumentReadingHandler.Reading()));
        await using var scenario = await CreateAsync(provider, clock);
        var (unnamedId, unnamedKey) = await ReadPhotoAsync(scenario);
        var (audioRequestId, _) = await NormalizeAsync(scenario);
        var (namedId, namedKey) = await ReadPhotoAsync(scenario);
        await SubmitAsync(scenario, Guid.NewGuid(), namedId);
        Assert.True(await scenario.ProcessNextAsync());

        // A minute before the 24 hours, the purge keeps everything.
        clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
        await PurgeAsync(scenario);
        Assert.Equal(("completed", JsonValueKind.Object), await DocumentAsync(scenario, unnamedId));
        Assert.Equal(("completed", JsonValueKind.String), await TranscriptAsync(scenario, audioRequestId));

        // Past them, the reading and the transcription no turn named go, and with them the copy; the reading a turn named stays.
        clock.Advance(TimeSpan.FromMinutes(2));
        await PurgeAsync(scenario);
        Assert.Equal(("completed", JsonValueKind.Null), await DocumentAsync(scenario, unnamedId));
        Assert.Equal(("completed", JsonValueKind.Null), await TranscriptAsync(scenario, audioRequestId));
        Assert.Equal(("completed", JsonValueKind.Object), await DocumentAsync(scenario, namedId));
        Assert.Equal([namedKey], await RetainedAsync(scenario, unnamedKey, namedKey));
        using var late = await PostTurnAsync(scenario, Guid.NewGuid(), unnamedId, null);
        Assert.Equal("Ai.Assistant.Expired", (await late.ReadJsonAsync()).GetProperty("code").GetString());
    }

    private async Task<AiAssistantExecutionScenario> CreateAsync(DocumentReadingHandler provider, TimeProvider? clock = null)
    {
        var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary", services =>
        {
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(new HomeAudioFiles());
            services.RemoveAll<IStructuredAttachmentReader>();
            services.AddSingleton<IStructuredAttachmentReader>(new HomeAttachmentReader());
            services.RemoveAll<ILlmAudioTranscriptionClient>();
            services.AddSingleton<ILlmAudioTranscriptionClient>(new AudioNormalizationClient());
            services.AddHttpClient(AiAssistantProvidersRegistration.OpenAiDocumentHttpClientName).ConfigurePrimaryHttpMessageHandler(() => provider);
            services.PostConfigure<AiAssistantOptions>(options =>
            {
                options.AudioModelProfiles = [new(AiProvider.OpenAI, "fictional-audio", "browser-audio", 1, ["audio/webm", "audio/mp4"], 1_000_000)];
                options.DocumentModelProfiles = [new(AiProvider.OpenAI, "fictional-vision", "fixture-document", 1,
                    ["application/pdf", "image/jpeg", "image/png"], StructuredAttachmentLimits.MaximumBytes, StructuredAttachmentLimits.MaximumPages)];
            });
            if (clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
            }
        });
        scenario.Client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        return scenario;
    }

    private static async Task<(Guid RequestId, string Key)> ReadPhotoAsync(AiAssistantExecutionScenario scenario)
    {
        var requestId = Guid.NewGuid();
        var file = new ByteArrayContent(Jpeg);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        using var upload = new MultipartFormDataContent { { new StringContent(requestId.ToString()), "requestId" }, { file, "file", "foto.jpg" } };
        using (var accepted = await scenario.Client.PostAsync("/api/ai/assistant/documents", upload, Ct))
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.True(await scenario.ProcessNextAsync());
        Assert.Equal(("completed", JsonValueKind.Object), await DocumentAsync(scenario, requestId));
        return (requestId, $"ai/assistant/documents/{requestId:N}.jpg");
    }

    private static async Task<(Guid RequestId, string Key)> NormalizeAsync(AiAssistantExecutionScenario scenario)
    {
        var requestId = Guid.NewGuid();
        var file = new ByteArrayContent(Webm);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        using var upload = new MultipartFormDataContent { { new StringContent(requestId.ToString()), "requestId" }, { file, "file", "recording.webm" } };
        using (var accepted = await scenario.Client.PostAsync("/api/ai/assistant/audio", upload, Ct))
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.True(await scenario.ProcessNextAsync());
        Assert.Equal(("completed", JsonValueKind.String), await TranscriptAsync(scenario, requestId));
        return (requestId, $"ai/assistant/audio/{requestId:N}.webm");
    }

    private static async Task<JsonElement> SubmitAsync(AiAssistantExecutionScenario scenario, Guid requestId, Guid? documentRequestId,
        Guid? audioRequestId = null)
    {
        using var response = await PostTurnAsync(scenario, requestId, documentRequestId, audioRequestId);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static Task<HttpResponseMessage> PostTurnAsync(AiAssistantExecutionScenario scenario, Guid requestId, Guid? documentRequestId,
        Guid? audioRequestId) => scenario.Client.PostAsJsonAsync("/api/ai/assistant/turns", new
    {
        requestId, question = "", documentRequestId, audioRequestId, expectedConnectionRevision = scenario.ConnectionRevision,
    }, Ct);

    private static async Task<(string? State, JsonValueKind Document)> DocumentAsync(AiAssistantExecutionScenario scenario, Guid requestId)
    {
        using var response = await scenario.Client.GetAsync($"/api/ai/assistant/documents/{requestId}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        return (body.GetProperty("state").GetString(), body.GetProperty("document").ValueKind);
    }

    private static async Task<(string? State, JsonValueKind Transcript)> TranscriptAsync(AiAssistantExecutionScenario scenario, Guid requestId)
    {
        using var response = await scenario.Client.GetAsync($"/api/ai/assistant/audio/{requestId}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        return (body.GetProperty("state").GetString(), body.GetProperty("transcript").ValueKind);
    }

    private static Task<HashSet<string>> RetainedAsync(AiAssistantExecutionScenario scenario, params string[] keys) => scenario.ScopeAsync(async services =>
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in services.GetServices<IPrivateFileRetentionSource>())
            referenced.UnionWith(await source.ListReferencedKeysAsync(keys, Ct));
        return referenced;
    });

    private static Task<bool> PurgeAsync(AiAssistantExecutionScenario scenario) => scenario.ScopeAsync(async services =>
    {
        Assert.True((await services.GetRequiredService<IAiAssistantRetentionService>().PurgeBatchAsync(Ct)).IsSuccess);
        return true;
    });

    private static Task<T> ScopeDbAsync<T>(AiAssistantExecutionScenario scenario, Func<ApplicationDbContext, Task<T>> query) =>
        scenario.ScopeAsync(services => query(services.GetRequiredService<ApplicationDbContext>()));
}
