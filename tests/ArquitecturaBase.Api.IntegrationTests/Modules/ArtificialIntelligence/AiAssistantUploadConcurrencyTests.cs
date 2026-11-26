using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Api.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// I9: Home's three routes that receive a file (documents, audio and file-turns), after the session, the permission and the person's window
// of sends, take one slot of their person, with no queue, and one of the copy's, with a short queue; with them taken, 429 with its wait
// before the body is read. With the slots taken the body has UploadBodySeconds to arrive, and they are freed however the upload ends.
[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantUploadConcurrencyTests(ApiFactory factory)
{
    private const string Documents = "/api/ai/assistant/documents";
    private static readonly byte[] Pdf = "%PDF-1.7 pages=1 comprobante ficticio"u8.ToArray();
    // The fictional reader keeps a body ending in «hold» until the test frees it, and throws on one ending in «boom».
    private static readonly byte[] Held = "%PDF-1.7 pages=1 hold"u8.ToArray();
    private static readonly byte[] Refused = "reject:Protected:Pdf"u8.ToArray();
    private static readonly byte[] Webm = [0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 0x01, 0x42, 0xF7, 0x81];
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(15);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_request_without_a_session_or_without_the_permission_takes_no_slot_nor_a_place_in_the_queue()
    {
        var reader = new GateReader(new HomeAttachmentReader());
        await using var scenario = await CreateAsync(reader, slots: 1, queue: 1);
        using var anonymous = scenario.CreateClient(null);
        using var support = scenario.CreateClient(await PersonAsync(scenario, "Support"));
        using var other = scenario.CreateClient(await PersonAsync(scenario));
        try
        {
            var first = scenario.Client.PostAsync(Documents, Document(Held), Ct);
            await reader.Entered.Task.WaitAsync(Soon, Ct);

            // With the copy's only slot taken, both answer at once and leave the queue's only place free.
            using (var unauthenticated = await anonymous.PostAsync(Documents, Document(Pdf), Ct).WaitAsync(Soon, Ct))
                Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            using (var forbidden = await support.PostAsync(Documents, Document(Pdf), Ct).WaitAsync(Soon, Ct))
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            // A legitimate upload waits in it and enters when the slot is freed.
            var queued = other.PostAsync(Documents, Document(Pdf), Ct);
            reader.Release.TrySetResult();
            using (var firstResponse = await first) Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
            using (var queuedResponse = await queued.WaitAsync(Soon, Ct)) Assert.Equal(HttpStatusCode.Accepted, queuedResponse.StatusCode);
        }
        finally
        {
            reader.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task A_second_upload_of_the_same_person_at_once_gets_429_at_once_while_another_person_still_uploads()
    {
        var reader = new GateReader(new HomeAttachmentReader());
        await using var scenario = await CreateAsync(reader, slots: 2, queue: 1);
        using var other = scenario.CreateClient(await PersonAsync(scenario));
        try
        {
            var first = scenario.Client.PostAsync(Documents, Document(Held), Ct);
            await reader.Entered.Task.WaitAsync(Soon, Ct);

            // The copy has a free slot and room in its queue, but the person has only one, with no queue.
            using (var second = await scenario.Client.PostAsync(Documents, Document(Pdf), Ct).WaitAsync(Soon, Ct))
                await AssertWaitAsync(second);
            using (var recording = await scenario.Client.PostAsync("/api/ai/assistant/audio", Recording(), Ct).WaitAsync(Soon, Ct))
                await AssertWaitAsync(recording);
            using (var another = await other.PostAsync(Documents, Document(Pdf), Ct).WaitAsync(Soon, Ct))
                Assert.Equal(HttpStatusCode.Accepted, another.StatusCode);

            reader.Release.TrySetResult();
            using (var firstResponse = await first) Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        }
        finally
        {
            reader.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task With_the_copy_s_slots_taken_by_other_people_the_next_upload_waits_in_the_queue_or_gets_429_and_a_json_turn_does_not()
    {
        var reader = new GateReader(new HomeAttachmentReader());
        await using var scenario = await CreateAsync(reader, slots: 1, queue: 1);
        using var second = scenario.CreateClient(await PersonAsync(scenario));
        using var third = scenario.CreateClient(await PersonAsync(scenario));
        try
        {
            // The first upload keeps the only slot while its file is prepared.
            var first = scenario.Client.PostAsync(Documents, Document(Held), Ct);
            await reader.Entered.Task.WaitAsync(Soon, Ct);

            // Of two more people, one waits in the queue and the other finds it full: 429 with Retry-After and retryAfter, as any limit.
            var fromSecond = second.PostAsync(Documents, Document(Pdf), Ct);
            var fromThird = third.PostAsync(Documents, Document(Pdf), Ct);
            var answered = await Task.WhenAny(fromSecond, fromThird).WaitAsync(Soon, Ct);
            using (var rejected = await answered)
                await AssertWaitAsync(rejected);
            var (queued, refusedClient) = answered == fromSecond ? (fromThird, second) : (fromSecond, third);
            Assert.False(queued.IsCompleted);

            // A recording and a turn with a file share the copy's limit; a JSON turn does not, and reaches its validation.
            using (var audio = await refusedClient.PostAsync("/api/ai/assistant/audio", Recording(), Ct))
                await AssertWaitAsync(audio);
            using (var fileTurn = await refusedClient.PostAsync("/api/ai/assistant/file-turns", FileTurn(scenario), Ct))
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
            using var again = await refusedClient.PostAsync(Documents, Document(Pdf), Ct);
            Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
            Assert.Equal(3, reader.Prepared);
        }
        finally
        {
            reader.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task Whoever_spent_their_window_of_sends_gets_its_429_with_its_wait_and_takes_no_place_in_the_queue()
    {
        var reader = new GateReader(new HomeAttachmentReader());
        await using var scenario = await CreateAsync(reader, slots: 1, queue: 1, sends: 1);
        using var spent = scenario.CreateClient(await PersonAsync(scenario));
        using var waiting = scenario.CreateClient(await PersonAsync(scenario));
        try
        {
            var first = scenario.Client.PostAsync(Documents, Document(Held), Ct);
            await reader.Entered.Task.WaitAsync(Soon, Ct);

            // A JSON turn spends the person's only send of the minute; the upload after it gets that window's 429 at once, with its wait.
            using (var turn = await spent.PostAsJsonAsync("/api/ai/assistant/turns", new
            {
                requestId = Guid.NewGuid(), question = "", expectedConnectionRevision = scenario.ConnectionRevision,
            }, Ct))
                Assert.Equal(HttpStatusCode.BadRequest, turn.StatusCode);
            using (var refused = await spent.PostAsync(Documents, Document(Pdf), Ct).WaitAsync(Soon, Ct))
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
                Assert.True(refused.Headers.RetryAfter?.Delta > TimeSpan.FromSeconds(1));
                var body = await refused.ReadJsonAsync();
                Assert.Equal("Http.TooManyRequests", body.GetProperty("code").GetString());
                Assert.True(body.GetProperty("retryAfter").GetInt32() > 1);
            }

            // The queue's only place is still free: another person waits in it and enters when the slot is freed.
            var queued = waiting.PostAsync(Documents, Document(Pdf), Ct);
            reader.Release.TrySetResult();
            using (var firstResponse = await first) Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
            using (var queuedResponse = await queued.WaitAsync(Soon, Ct)) Assert.Equal(HttpStatusCode.Accepted, queuedResponse.StatusCode);
        }
        finally
        {
            reader.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task The_slots_are_freed_after_a_400_an_exception_of_the_reader_and_a_body_that_does_not_arrive_in_time()
    {
        var clock = new FakeTimeProvider(TimeProvider.System.GetUtcNow());
        var reader = new GateReader(new HomeAttachmentReader());
        await using var scenario = await CreateAsync(reader, slots: 1, queue: 0, sends: 100, clock: clock);

        // A file the owner refuses is a 400 from the service, after both slots: answered, it frees them, so the next one gets there too.
        await AssertFreeAsync(scenario.Client);
        await AssertFreeAsync(scenario.Client);

        // A body over the route's multipart limit gives the same 400 as before, and a reader that throws a 500; both free the slots.
        // A body that is not a form takes no slot and gets the 400 of its missing fields, as always, never a 500.
        using (var notForm = await scenario.Client.PostAsync(Documents, new ByteArrayContent(Pdf), Ct))
            Assert.Equal(HttpStatusCode.BadRequest, notForm.StatusCode);
        await AssertFreeAsync(scenario.Client);

        // The form is read with the route's limits, so the binding refuses it and the service never sees it.
        using (var oversized = await scenario.Client.PostAsync(Documents, Document(new byte[StructuredAttachmentLimits.MaximumBytes + 128 * 1024]), Ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
            Assert.DoesNotContain("Ai.Assistant", (await oversized.ReadJsonAsync()).GetProperty("code").GetString());
        }
        await AssertFreeAsync(scenario.Client);
        using (var failed = await scenario.Client.PostAsync(Documents, Document("%PDF-1.7 pages=1 boom"u8.ToArray()), Ct))
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await AssertFreeAsync(scenario.Client);

        // A body that stops arriving keeps the slots until UploadBodySeconds pass on the clock: then the request is cut and they are free.
        using var stalled = new StalledUpload();
        var cut = scenario.Client.PostAsync(Documents, stalled, Ct);
        for (var attempt = 0; attempt < 300 && !cut.IsCompleted; attempt++)
        {
            clock.Advance(TimeSpan.FromSeconds(120));
            await Task.WhenAny(cut, Task.Delay(100, Ct));
        }
        // Cut by the server on the clock, long before the client's own timeout of 100 seconds.
        Assert.True(cut.IsCompleted);
        await Assert.ThrowsAnyAsync<Exception>(() => cut);
        await AssertFreedSoonAsync(scenario.Client);
        using var accepted = await scenario.Client.PostAsync(Documents, Document(Pdf), Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
    }

    // With one slot and no queue, a refused file reaches the service (400) only if nothing holds the person's slot or the copy's.
    private static async Task AssertFreeAsync(HttpClient client)
    {
        using var response = await client.PostAsync(Documents, Document(Refused), Ct).WaitAsync(Soon, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Ai.Assistant.AttachmentProtectedPdf", (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    // The server frees the slots of a cut upload as it finishes the request, a moment after the client sees the cut: with the machine
    // busy, the next upload can still find them taken. They have to be free before Soon: a try every 250 ms is at most 60 tries, within
    // the 100 sends of the scenario.
    private static async Task AssertFreedSoonAsync(HttpClient client)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            using var response = await client.PostAsync(Documents, Document(Refused), Ct).WaitAsync(Soon, Ct);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || waited.Elapsed > Soon)
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal("Ai.Assistant.AttachmentProtectedPdf", (await response.ReadJsonAsync()).GetProperty("code").GetString());
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), Ct);
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

    private async Task<AiAssistantExecutionScenario> CreateAsync(GateReader reader, int slots, int queue, int sends = 6, TimeProvider? clock = null)
    {
        var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary", services =>
        {
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(new HomeAudioFiles());
            services.RemoveAll<IStructuredAttachmentReader>();
            services.AddSingleton<IStructuredAttachmentReader>(reader);
            services.PostConfigure<AiAssistantOptions>(options => options.DocumentModelProfiles = [new(AiProvider.OpenAI, "fictional-vision", "fixture-document",
                1, ["application/pdf", "image/jpeg", "image/png"], StructuredAttachmentLimits.MaximumBytes, StructuredAttachmentLimits.MaximumPages)]);
            services.PostConfigure<AiRateLimitOptions>(options =>
                (options.UploadConcurrencyLimit, options.UploadQueueLimit, options.AssistantSubmitPermitLimit) = (slots, queue, sends));
            if (clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
            }
        });
        scenario.Client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        return scenario;
    }

    // Another person of the same copy: an operator of the assistant, or with only the given role.
    private static Task<Guid> PersonAsync(AiAssistantExecutionScenario scenario, string role = "Assistant operator") => scenario.ScopeAsync(async services =>
    {
        var id = Guid.Empty;
        var result = await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct =>
        {
            var users = services.GetRequiredService<IUserRepository>();
            var person = await users.CreateAsync(Email.Create(TestEmails.Unique("assistant-upload")).Value, null, false, null, "es", ct);
            await users.SetRolesAsync(person.Id, [role], ct);
            id = person.Id;
            return Result.Success();
        }, CommitPolicy.OnSuccess, Ct);
        Assert.True(result.IsSuccess);
        return id;
    });

    private static MultipartFormDataContent Document(byte[] content)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent { { new StringContent(Guid.NewGuid().ToString()), "requestId" }, { file, "file", "comprobante.pdf" } };
    }

    private static MultipartFormDataContent Recording()
    {
        var file = new ByteArrayContent(Webm);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        return new MultipartFormDataContent { { new StringContent(Guid.NewGuid().ToString()), "requestId" }, { file, "file", "recording.webm" } };
    }

    private static MultipartFormDataContent FileTurn(AiAssistantExecutionScenario scenario)
    {
        var file = new ByteArrayContent("Código,Cliente\nA1,Cliente ficticio\n"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "requestId" },
            { new StringContent(scenario.ConnectionRevision.ToString(CultureInfo.InvariantCulture)), "expectedConnectionRevision" },
            { file, "file", "clientes.csv" },
        };
    }

    // The owner's fictional port: a body ending in «hold» waits until the test frees it, so its upload keeps its slots, and one ending in
    // «boom» throws, as a reader that fails.
    private sealed class GateReader(HomeAttachmentReader inner) : IStructuredAttachmentReader
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Prepared => inner.Prepared;

        public Task<StructuredAttachmentAccess> CanAttachAsync(Guid actorUserId, CancellationToken cancellationToken) =>
            inner.CanAttachAsync(actorUserId, cancellationToken);

        public async Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken cancellationToken)
        {
            var text = Encoding.UTF8.GetString(content);
            if (text.EndsWith("boom", StringComparison.Ordinal))
                throw new InvalidOperationException("Fictional reader failure.");
            if (text.EndsWith("hold", StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await inner.PrepareAsync(content, fileName, cancellationToken);
        }
    }

    // A multipart body that sends its fields and the start of its file and then stops, until it is disposed.
    private sealed class StalledUpload : HttpContent
    {
        private const string Boundary = "stalled-upload";
        private readonly TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StalledUpload() => Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={Boundary}");

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            var start = $"--{Boundary}\r\nContent-Disposition: form-data; name=\"requestId\"\r\n\r\n{Guid.NewGuid()}\r\n"
                + $"--{Boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"comprobante.pdf\"\r\nContent-Type: application/pdf\r\n\r\n%PDF-1.7";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(start));
            await stream.FlushAsync();
            await disposed.Task;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            disposed.TrySetResult();
            base.Dispose(disposing);
        }
    }
}
