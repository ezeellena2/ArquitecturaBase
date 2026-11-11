using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantHttpContractTests(ApiFactory factory)
{
    private static readonly byte[] Csv = "Código,Cliente\nA1,Cliente ficticio\nA2,Otro cliente ficticio\n"u8.ToArray();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Home_assistant_http_bodies_keep_the_published_lowercase_states()
    {
        // Private storage in memory and the owner's fictional port: Home offers the clip and a turn may bring a spreadsheet.
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary", services =>
        {
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(new HomeAudioFiles());
            services.RemoveAll<IStructuredAttachmentReader>();
            services.AddSingleton<IStructuredAttachmentReader>(new HomeAttachmentReader());
        });
        var client = scenario.Client;
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");

        using var bootstrap = await client.GetAsync("/api/ai/assistant/bootstrap", Ct);
        Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
        var offered = await bootstrap.ReadJsonAsync();
        Assert.Equal("available", offered.GetProperty("availability").GetString());
        Assert.True(offered.GetProperty("attachments").GetBoolean());
        await AiAssistantContractCapture.WriteAsync(bootstrap, "bootstrap", Ct);

        var requestId = Guid.NewGuid();
        using var submitted = await client.PostAsJsonAsync("/api/ai/assistant/turns", new
        {
            requestId, question = "Hola", conversationId = (Guid?)null, expectedRevision = (long?)null,
            expectedConnectionRevision = scenario.ConnectionRevision,
        }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);
        var accepted = await submitted.ReadJsonAsync();
        Assert.Equal("pending", accepted.GetProperty("state").GetString());
        Assert.Equal("pending", accepted.GetProperty("stage").GetString());
        await AiAssistantContractCapture.WriteAsync(submitted, "submitted", Ct);
        var conversationId = accepted.GetProperty("conversationId").GetGuid();

        using var pending = await client.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        var pendingTurn = await pending.ReadJsonAsync();
        Assert.Equal("pending", pendingTurn.GetProperty("state").GetString());
        Assert.Equal("pending", pendingTurn.GetProperty("stage").GetString());
        await AiAssistantContractCapture.WriteAsync(pending, "turn-pending", Ct);

        Assert.True(await scenario.ProcessNextAsync());
        using var completed = await client.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var completedTurn = await completed.ReadJsonAsync();
        Assert.Equal("completed", completedTurn.GetProperty("state").GetString());
        Assert.Equal("finished", completedTurn.GetProperty("stage").GetString());
        var evidence = Assert.Single(completedTurn.GetProperty("evidences").EnumerateArray());
        Assert.Equal(evidence.GetProperty("id").GetGuid(), Assert.Single(completedTurn.GetProperty("answer").GetProperty("evidenceIds").EnumerateArray()).GetGuid());
        await AiAssistantContractCapture.WriteAsync(completed, "turn-completed", Ct);

        using var turns = await client.GetAsync($"/api/ai/assistant/conversations/{conversationId}/turns?page=1&pageSize=20&sort=-ordinal", Ct);
        Assert.Equal(HttpStatusCode.OK, turns.StatusCode);
        var item = Assert.Single((await turns.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal("completed", item.GetProperty("state").GetString());
        await AiAssistantContractCapture.WriteAsync(turns, "turns", Ct);

        using var conversation = await client.GetAsync($"/api/ai/assistant/conversations/{conversationId}", Ct);
        Assert.Equal(HttpStatusCode.OK, conversation.StatusCode);
        var revision = (await conversation.ReadJsonAsync()).GetProperty("revision").GetInt64();
        var secondId = Guid.NewGuid();
        using var second = await client.PostAsJsonAsync("/api/ai/assistant/turns", new
        {
            requestId = secondId, question = "¿Y las inactivas?", conversationId = (Guid?)conversationId, expectedRevision = (long?)revision,
            expectedConnectionRevision = scenario.ConnectionRevision,
        }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        using var canceled = await client.PostAsync($"/api/ai/assistant/turns/{secondId}/cancel", null, Ct);
        Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);
        var canceledTurn = await canceled.ReadJsonAsync();
        Assert.Equal("canceled", canceledTurn.GetProperty("state").GetString());
        Assert.Equal("finished", canceledTurn.GetProperty("stage").GetString());
        await AiAssistantContractCapture.WriteAsync(canceled, "canceled", Ct);

        // A turn with a spreadsheet is accepted like any other, and only the turn that brought it shows the file.
        var fileId = Guid.NewGuid();
        using var fileSubmitted = await client.PostAsync("/api/ai/assistant/file-turns", FileTurn(scenario, fileId, "Armame un catálogo con esta planilla"), Ct);
        Assert.Equal(HttpStatusCode.Accepted, fileSubmitted.StatusCode);
        var fileAccepted = await fileSubmitted.ReadJsonAsync();
        Assert.Equal(("pending", "pending"), (fileAccepted.GetProperty("state").GetString(), fileAccepted.GetProperty("stage").GetString()));
        await AiAssistantContractCapture.WriteAsync(fileSubmitted, "file-submitted", Ct);

        Assert.True(await scenario.ProcessNextAsync());
        using var withAttachment = await client.GetAsync($"/api/ai/assistant/turns/{fileId}", Ct);
        Assert.Equal(HttpStatusCode.OK, withAttachment.StatusCode);
        var fileTurn = await withAttachment.ReadJsonAsync();
        Assert.Equal(("completed", "finished"), (fileTurn.GetProperty("state").GetString(), fileTurn.GetProperty("stage").GetString()));
        var attachment = fileTurn.GetProperty("attachment");
        Assert.Equal(("clientes.csv", "csv", 3, Csv.LongLength), (attachment.GetProperty("name").GetString(), attachment.GetProperty("kind").GetString(),
            attachment.GetProperty("rows").GetInt32(), attachment.GetProperty("size").GetInt64()));
        await AiAssistantContractCapture.WriteAsync(withAttachment, "turn-attachment", Ct);
    }

    // The fields of a new Home turn with its written request and the spreadsheet.
    private static MultipartFormDataContent FileTurn(AiAssistantExecutionScenario scenario, Guid requestId, string question)
    {
        var file = new ByteArrayContent(Csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent
        {
            { new StringContent(requestId.ToString()), "requestId" },
            { new StringContent(scenario.ConnectionRevision.ToString(CultureInfo.InvariantCulture)), "expectedConnectionRevision" },
            { new StringContent(question), "question" },
            { file, "file", "clientes.csv" },
        };
    }
}
