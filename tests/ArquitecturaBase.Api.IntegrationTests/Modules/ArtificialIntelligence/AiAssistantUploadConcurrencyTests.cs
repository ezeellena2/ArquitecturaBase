using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Api.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// I9: the three routes that receive a file share one limit of uploads at once per copy, with a short queue; with both taken the next
// one answers 429 with its wait, before its body is read. One slot and one place in the queue here; the copy's figures are a setting.
[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantUploadConcurrencyTests(ApiFactory factory)
{
    private const string Documents = "/api/ai/assistant/documents";
    private static readonly byte[] Pdf = "%PDF-1.7 pages=1 comprobante ficticio"u8.ToArray();
    private static readonly byte[] Webm = [0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 0x01, 0x42, 0xF7, 0x81];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_the_copy_s_uploads_and_queue_taken_the_next_upload_gets_429_with_its_wait_and_a_freed_slot_accepts_again()
    {
        var reader = new HeldReader(new HomeAttachmentReader());
        await using var scenario = await CreateAsync(reader);
        try
        {
            // The first upload keeps the only slot while its file is prepared.
            var first = scenario.Client.PostAsync(Documents, Document(Guid.NewGuid()), Ct);
            await reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

            // Of two more, one waits in the queue and the other finds it full: 429 with Retry-After and retryAfter, as any limit.
            var second = scenario.Client.PostAsync(Documents, Document(Guid.NewGuid()), Ct);
            var third = scenario.Client.PostAsync(Documents, Document(Guid.NewGuid()), Ct);
            var answered = await Task.WhenAny(second, third).WaitAsync(TimeSpan.FromSeconds(30), Ct);
            using (var rejected = await answered)
                await AssertWaitAsync(rejected);
            var queued = answered == second ? third : second;
            Assert.False(queued.IsCompleted);

            // A recording and a turn with a file share the copy's limit; a JSON turn does not, and reaches its validation.
            using (var audio = await scenario.Client.PostAsync("/api/ai/assistant/audio", Recording(Guid.NewGuid()), Ct))
                await AssertWaitAsync(audio);
            using (var fileTurn = await scenario.Client.PostAsync("/api/ai/assistant/file-turns", FileTurn(scenario, Guid.NewGuid()), Ct))
                await AssertWaitAsync(fileTurn);
            using (var turn = await scenario.Client.PostAsJsonAsync("/api/ai/assistant/turns", new
            {
                requestId = Guid.NewGuid(), question = "", expectedConnectionRevision = scenario.ConnectionRevision,
            }, Ct))
                Assert.Equal(HttpStatusCode.BadRequest, turn.StatusCode);

            // Freed, the slot goes to the one in the queue, and after both the copy takes a new upload.
            reader.Release.TrySetResult();
            using (var firstResponse = await first) Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
            using (var queuedResponse = await queued) Assert.Equal(HttpStatusCode.Accepted, queuedResponse.StatusCode);
            using var again = await scenario.Client.PostAsync(Documents, Document(Guid.NewGuid()), Ct);
            Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
            Assert.Equal(3, reader.Prepared);
        }
        finally
        {
            reader.Release.TrySetResult();
        }
    }

    private static async Task AssertWaitAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
        var body = await response.ReadJsonAsync();
        Assert.Equal("Http.TooManyRequests", body.GetProperty("code").GetString());
        Assert.Equal(1, body.GetProperty("retryAfter").GetInt32());
    }

    private async Task<AiAssistantExecutionScenario> CreateAsync(HeldReader reader)
    {
        var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary", services =>
        {
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(new HomeAudioFiles());
            services.RemoveAll<IStructuredAttachmentReader>();
            services.AddSingleton<IStructuredAttachmentReader>(reader);
            services.PostConfigure<AiAssistantOptions>(options => options.DocumentModelProfiles = [new(AiProvider.OpenAI, "fictional-vision", "fixture-document",
                1, ["application/pdf", "image/jpeg", "image/png"], StructuredAttachmentLimits.MaximumBytes, StructuredAttachmentLimits.MaximumPages)]);
            services.PostConfigure<AiRateLimitOptions>(options => (options.UploadConcurrencyLimit, options.UploadQueueLimit) = (1, 1));
        });
        scenario.Client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        return scenario;
    }

    private static MultipartFormDataContent Document(Guid requestId)
    {
        var file = new ByteArrayContent(Pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent { { new StringContent(requestId.ToString()), "requestId" }, { file, "file", "comprobante.pdf" } };
    }

    private static MultipartFormDataContent Recording(Guid requestId)
    {
        var file = new ByteArrayContent(Webm);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        return new MultipartFormDataContent { { new StringContent(requestId.ToString()), "requestId" }, { file, "file", "recording.webm" } };
    }

    private static MultipartFormDataContent FileTurn(AiAssistantExecutionScenario scenario, Guid requestId)
    {
        var file = new ByteArrayContent("Código,Cliente\nA1,Cliente ficticio\n"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent
        {
            { new StringContent(requestId.ToString()), "requestId" },
            { new StringContent(scenario.ConnectionRevision.ToString(CultureInfo.InvariantCulture)), "expectedConnectionRevision" },
            { file, "file", "clientes.csv" },
        };
    }

    // The owner's fictional port, whose first preparation waits until the test frees it: that upload keeps its slot meanwhile.
    private sealed class HeldReader(HomeAttachmentReader inner) : IStructuredAttachmentReader
    {
        private int calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Prepared => inner.Prepared;

        public Task<StructuredAttachmentAccess> CanAttachAsync(Guid actorUserId, CancellationToken cancellationToken) =>
            inner.CanAttachAsync(actorUserId, cancellationToken);

        public async Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await inner.PrepareAsync(content, fileName, cancellationToken);
        }
    }
}
