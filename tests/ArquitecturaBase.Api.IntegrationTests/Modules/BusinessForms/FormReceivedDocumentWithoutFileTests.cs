using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// Task 8 (decision 10): every file a channel could not open enters the inbox discarded, also one the channel must not keep (an old Word,
// one with macros, a file over the limit, an unknown type): with what its person sent and no file. Its file is not stored, opening it
// answers 404 with its own code, and only a file that cannot be opened may come without a copy.
[Collection(ApiTestGroup.Name)]
public sealed class FormReceivedDocumentWithoutFileTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_file_that_cannot_be_opened_is_kept_discarded_without_its_file_and_opening_it_says_so()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var legacy = Rejected(scenario, new("informe.doc", "application/msword", 2048), StructuredAttachmentRejection.Legacy);
        var tooLarge = Rejected(scenario, new("video.pdf", "application/pdf", null), StructuredAttachmentRejection.TooLarge);

        var reply = await scenario.PortAsync(port => port.RegisterAsync(legacy, Ct));
        var again = await scenario.PortAsync(port => port.RegisterAsync(legacy, Ct));
        var withoutSize = await scenario.PortAsync(port => port.RegisterAsync(tooLarge, Ct));

        Assert.Equal((StructuredDocumentReplyKind.Rejected, false), (reply.Kind, reply.WasAlreadyRegistered));
        Assert.Equal((reply.DocumentId, StructuredDocumentReplyKind.Rejected, true), (again.DocumentId, again.Kind, again.WasAlreadyRegistered));
        using var reader = scenario.Client(scenario.Reader);
        using var detail = await reader.GetAsync($"/api/forms/documents/{reply.DocumentId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var document = (await detail.ReadJsonAsync()).GetProperty("document");
        Assert.Equal(("discarded", "legacy"), (document.GetProperty("state").GetString(), document.GetProperty("reasonCode").GetString()));
        Assert.Equal("{\"name\":\"informe.doc\",\"mediaType\":\"application/msword\",\"size\":2048,\"pages\":null,\"stored\":false}",
            document.GetProperty("file").GetRawText());
        using var opened = await reader.GetAsync($"/api/forms/documents/{reply.DocumentId}/file", Ct);
        Assert.Equal(HttpStatusCode.NotFound, opened.StatusCode);
        Assert.Equal("Forms.ReceivedDocument.FileNotStored", (await opened.ReadJsonAsync()).GetProperty("code").GetString());
        // Over the limit before its bytes arrived, its size was never known.
        using var unknownSize = await reader.GetAsync($"/api/forms/documents/{withoutSize.DocumentId}", Ct);
        var file = (await unknownSize.ReadJsonAsync()).GetProperty("document").GetProperty("file");
        Assert.Equal((JsonValueKind.Null, false), (file.GetProperty("size").ValueKind, file.GetProperty("stored").GetBoolean()));
    }

    [Fact]
    public async Task Only_a_file_that_cannot_be_opened_comes_without_a_copy_and_never_with_both()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var sent = await scenario.SendAsync(Ct, notRead: StructuredDocumentNotRead.Rejected);
        var file = new StructuredDocumentFile("informe.doc", "application/msword", 2048);
        var rejected = Rejected(scenario, file, StructuredAttachmentRejection.Legacy);

        var quota = await scenario.PortAsync(port => port.RegisterAsync(rejected with { NotRead = StructuredDocumentNotRead.Quota, Rejection = null }, Ct));
        var withoutFile = await scenario.PortAsync(port => port.RegisterAsync(rejected with { File = null }, Ct));
        var withoutReason = await scenario.PortAsync(port => port.RegisterAsync(rejected with { Rejection = null }, Ct));
        var invalidSize = await scenario.PortAsync(port => port.RegisterAsync(rejected with { File = file with { Size = 0 } }, Ct));
        var both = await scenario.PortAsync(port => port.RegisterAsync(sent.Request with { SourceId = "wamid.both-" + Guid.NewGuid().ToString("N"), File = file }, Ct));

        Assert.All([quota, withoutFile, withoutReason, invalidSize, both], reply => Assert.Equal((StructuredDocumentReplyKind.Unavailable, (Guid?)null),
            (reply.Kind, reply.DocumentId)));
        Assert.Equal(StructuredDocumentReplyKind.Rejected, sent.Reply.Kind);
    }

    private static StructuredDocumentRegistrationRequest Rejected(FormReceivedDocumentScenario scenario, StructuredDocumentFile file,
        StructuredAttachmentRejection rejection) =>
        new(FormReceivedDocumentScenario.Channel, "wamid." + Guid.NewGuid().ToString("N"), scenario.Sender, "es", scenario.Factory.Clock.GetUtcNow().UtcDateTime, null)
        {
            File = file, NotRead = StructuredDocumentNotRead.Rejected, Rejection = rejection,
        };
}
