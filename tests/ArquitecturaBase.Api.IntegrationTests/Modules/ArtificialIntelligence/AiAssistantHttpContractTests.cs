using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
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
        // Private storage in memory, the owner's fictional port and a fictitious reading: Home offers the clip, and a turn may bring a
        // spreadsheet, a Word or a document read before it.
        using var reading = DocumentReadingHandler.Json(DocumentReadingHandler.Completed(DocumentReadingHandler.Reading("| Monto | $ 1.000 |\n| Fecha | 11/11/2026 |")));
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary", services =>
        {
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(new HomeAudioFiles());
            services.RemoveAll<IStructuredAttachmentReader>();
            services.AddSingleton<IStructuredAttachmentReader>(new HomeAttachmentReader());
            services.AddHttpClient(AiAssistantProvidersRegistration.OpenAiDocumentHttpClientName).ConfigurePrimaryHttpMessageHandler(() => reading);
            services.PostConfigure<AiAssistantOptions>(options => options.DocumentModelProfiles = [new(AiProvider.OpenAI, "fictional-vision", "fixture-document",
                1, ["application/pdf", "image/jpeg", "image/png"], StructuredAttachmentLimits.MaximumBytes, StructuredAttachmentLimits.MaximumPages)]);
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

        // A PDF is read before its turn: the status of its reading, and the turn that names it shows the document with its pages.
        var documentId = Guid.NewGuid();
        using (var uploaded = await client.PostAsync("/api/ai/assistant/documents", Document(documentId), Ct))
            Assert.Equal(HttpStatusCode.Accepted, uploaded.StatusCode);
        Assert.True(await scenario.ProcessNextAsync());
        using var documentStatus = await client.GetAsync($"/api/ai/assistant/documents/{documentId}", Ct);
        Assert.Equal(HttpStatusCode.OK, documentStatus.StatusCode);
        var status = await documentStatus.ReadJsonAsync();
        Assert.Equal(("completed", "pdf", 2, "readable"), (status.GetProperty("state").GetString(), status.GetProperty("document").GetProperty("kind").GetString(),
            status.GetProperty("document").GetProperty("pages").GetInt32(), status.GetProperty("document").GetProperty("readability").GetString()));
        await AiAssistantContractCapture.WriteAsync(documentStatus, "document-status", Ct);
        var documentTurnId = Guid.NewGuid();
        using (var documentSubmitted = await client.PostAsJsonAsync("/api/ai/assistant/turns", new
        {
            requestId = documentTurnId, question = "Armame un formulario con este comprobante", documentRequestId = documentId,
            expectedConnectionRevision = scenario.ConnectionRevision,
        }, Ct)) Assert.Equal(HttpStatusCode.Accepted, documentSubmitted.StatusCode);
        Assert.True(await scenario.ProcessNextAsync());
        using var withDocument = await client.GetAsync($"/api/ai/assistant/turns/{documentTurnId}", Ct);
        var documentTurn = (await withDocument.ReadJsonAsync()).GetProperty("attachment");
        Assert.Equal(("comprobante.pdf", "pdf", 2), (documentTurn.GetProperty("name").GetString(), documentTurn.GetProperty("kind").GetString(),
            documentTurn.GetProperty("pages").GetInt32()));
        await AiAssistantContractCapture.WriteAsync(withDocument, "turn-document", Ct);

        // A Word goes with its turn and shows no pages.
        var wordId = Guid.NewGuid();
        using (var wordSubmitted = await client.PostAsync("/api/ai/assistant/file-turns", FileTurn(scenario, wordId, "Armame un formulario con este acta",
            "DOCX | Fecha | Lugar |\n| 11/11/2026 | Sucursal ficticia |"u8.ToArray(), "acta.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"), Ct)) Assert.Equal(HttpStatusCode.Accepted, wordSubmitted.StatusCode);
        Assert.True(await scenario.ProcessNextAsync());
        using var withWord = await client.GetAsync($"/api/ai/assistant/turns/{wordId}", Ct);
        var wordTurn = (await withWord.ReadJsonAsync()).GetProperty("attachment");
        Assert.Equal(("acta.docx", "docx", false), (wordTurn.GetProperty("name").GetString(), wordTurn.GetProperty("kind").GetString(),
            wordTurn.TryGetProperty("pages", out _)));
        await AiAssistantContractCapture.WriteAsync(withWord, "turn-word", Ct);
    }

    // The fields of a new Home turn with its written request and its file, the spreadsheet unless another is given.
    private static MultipartFormDataContent FileTurn(AiAssistantExecutionScenario scenario, Guid requestId, string question, byte[]? bytes = null,
        string fileName = "clientes.csv", string mediaType = "text/csv")
    {
        var file = new ByteArrayContent(bytes ?? Csv);
        file.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return new MultipartFormDataContent
        {
            { new StringContent(requestId.ToString()), "requestId" },
            { new StringContent(scenario.ConnectionRevision.ToString(CultureInfo.InvariantCulture)), "expectedConnectionRevision" },
            { new StringContent(question), "question" },
            { file, "file", fileName },
        };
    }

    // A two-page PDF of a fictitious transfer receipt, as the owner's fictional port reads it.
    private static MultipartFormDataContent Document(Guid requestId)
    {
        var file = new ByteArrayContent("%PDF-1.7 pages=2 comprobante ficticio"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent { { new StringContent(requestId.ToString()), "requestId" }, { file, "file", "comprobante.pdf" } };
    }
}
