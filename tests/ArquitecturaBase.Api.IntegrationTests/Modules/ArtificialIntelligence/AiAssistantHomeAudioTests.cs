using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// IA4: a Home recording is kept privately, normalized by the engine and named by the turn, which takes its transcript from the
// server. Fictional bytes and a fictional transcription: nothing here qualifies a real codec or provider.
[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantHomeAudioTests(ApiFactory factory)
{
    private static readonly byte[] Webm = [0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 0x01, 0x42, 0xF7, 0x81];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_recording_is_normalized_and_its_turn_takes_the_transcript_that_only_its_person_can_hear()
    {
        var files = new HomeAudioFiles();
        var transcription = new AudioNormalizationClient();
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "fixture.proposal", services =>
        {
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(files);
            services.RemoveAll<ILlmAudioTranscriptionClient>();
            services.AddSingleton<ILlmAudioTranscriptionClient>(transcription);
            services.PostConfigure<AiAssistantOptions>(options =>
                options.AudioModelProfiles = [new(AiProvider.OpenAI, "fictional-audio", "browser-audio", 1, ["audio/webm", "audio/mp4"], 1_000_000)]);
        });
        using (var bootstrap = await scenario.Client.GetAsync("/api/ai/assistant/bootstrap", Ct))
            Assert.True((await bootstrap.ReadJsonAsync()).GetProperty("audio").GetBoolean());
        using (var notAudio = await scenario.Client.PostAsync("/api/ai/assistant/audio", Recording(Guid.NewGuid(), "fictional text"u8.ToArray()), Ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, notAudio.StatusCode);
            Assert.True((await notAudio.ReadJsonAsync()).GetProperty("errors").TryGetProperty("file", out _));
        }

        var audioRequestId = Guid.NewGuid();
        var key = $"ai/assistant/audio/{audioRequestId:N}.webm";
        using (var uploaded = await scenario.Client.PostAsync("/api/ai/assistant/audio", Recording(audioRequestId, Webm), Ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, uploaded.StatusCode);
            var body = await uploaded.ReadJsonAsync();
            Assert.Equal("pending", body.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("transcript").ValueKind);
        }
        Assert.Equal([key], files.Keys);
        Assert.True(await scenario.ProcessNextAsync());
        Assert.Equal(1, transcription.Calls);
        using (var normalized = await scenario.Client.GetAsync($"/api/ai/assistant/audio/{audioRequestId}", Ct))
        {
            var body = await normalized.ReadJsonAsync();
            Assert.Equal(("completed", "Visit at North branch"), (body.GetProperty("state").GetString(), body.GetProperty("transcript").GetString()));
        }

        var turnRequestId = Guid.NewGuid();
        using (var submitted = await scenario.Client.PostAsJsonAsync("/api/ai/assistant/turns", new
        {
            requestId = turnRequestId, question = "", audioRequestId, expectedConnectionRevision = scenario.ConnectionRevision,
        }, Ct)) Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);
        using (var turn = await scenario.Client.GetAsync($"/api/ai/assistant/turns/{turnRequestId}", Ct))
        {
            var audio = (await turn.ReadJsonAsync()).GetProperty("audio");
            Assert.Equal(audioRequestId, audio.GetProperty("requestId").GetGuid());
            Assert.Equal("Visit at North branch", audio.GetProperty("transcript").GetString());
        }
        using (var heard = await scenario.Client.GetAsync($"/api/ai/assistant/audio/{audioRequestId}/file", Ct))
        {
            Assert.Equal(HttpStatusCode.OK, heard.StatusCode);
            Assert.Equal("audio/webm", heard.Content.Headers.ContentType?.MediaType);
            Assert.True(heard.Headers.CacheControl?.NoStore);
            Assert.Equal(Webm, await heard.Content.ReadAsByteArrayAsync(Ct));
        }

        await using var otherHost = scenario.CreateAdditionalHost();
        using var other = otherHost.CreateClient();
        other.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, (await OtherOperatorAsync(scenario)).ToString());
        using (var foreignFile = await other.GetAsync($"/api/ai/assistant/audio/{audioRequestId}/file", Ct))
            Assert.Equal(HttpStatusCode.NotFound, foreignFile.StatusCode);
        using (var foreignStatus = await other.GetAsync($"/api/ai/assistant/audio/{audioRequestId}", Ct))
            Assert.Equal(HttpStatusCode.NotFound, foreignStatus.StatusCode);
        using (var foreignTurn = await other.PostAsJsonAsync("/api/ai/assistant/turns", new
        {
            requestId = Guid.NewGuid(), question = "", audioRequestId, expectedConnectionRevision = scenario.ConnectionRevision,
        }, Ct)) Assert.Equal(HttpStatusCode.NotFound, foreignTurn.StatusCode);

        var retained = await scenario.ScopeAsync(async services =>
        {
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in services.GetServices<IPrivateFileRetentionSource>())
                referenced.UnionWith(await source.ListReferencedKeysAsync([key, $"ai/assistant/audio/{Guid.NewGuid():N}.webm"], Ct));
            return referenced;
        });
        Assert.Equal([key], retained);
    }

    private static MultipartFormDataContent Recording(Guid requestId, byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        return new MultipartFormDataContent { { new StringContent(requestId.ToString()), "requestId" }, { file, "file", "recording.webm" } };
    }

    private static Task<Guid> OtherOperatorAsync(AiAssistantExecutionScenario scenario) => scenario.ScopeAsync(async services =>
    {
        var id = Guid.Empty;
        var result = await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct =>
        {
            var users = services.GetRequiredService<IUserRepository>();
            var other = await users.CreateAsync(Email.Create(TestEmails.Unique("assistant-audio-other")).Value, null, false, null, "es", ct);
            await users.SetRolesAsync(other.Id, ["Assistant operator"], ct);
            id = other.Id;
            return Result.Success();
        }, CommitPolicy.OnSuccess, Ct);
        Assert.True(result.IsSuccess);
        return id;
    });
}
