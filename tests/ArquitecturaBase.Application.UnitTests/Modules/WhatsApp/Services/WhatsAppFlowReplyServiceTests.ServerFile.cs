using System.Security.Cryptography;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// Task 9 of the documents: the document a send carries goes to its question of a photo or a document when the person left it empty, read
// from this module's own private copy (never from Meta) and checked against its size and SHA-256; a file the person chose replaces it.
public sealed partial class WhatsAppFlowReplyServiceTests
{
    private static readonly byte[] Receipt = [9, 8, 7, 6];

    private static async Task<Fixture> WithServerFileAsync(byte[] stored, bool optional = true)
    {
        var host = new Fixture(media: true, assisted: true, automatic: true, optionalMedia: optional);
        var key = "whatsapp/documents/" + Guid.NewGuid().ToString("N") + ".jpg";
        await host.Files.WriteAsync(key, new MemoryStream(stored), "image/jpeg", CancellationToken.None);
        host.SessionProtector.Content = host.SessionProtector.Content! with
        {
            Server = new(new WhatsAppFlowServerFile(host.Mapping[0].FieldId, key, "comprobante.jpg", "image/jpeg", Receipt.Length,
                Convert.ToHexStringLower(SHA256.HashData(Receipt))), null),
        };
        return host;
    }

    [Fact]
    public async Task The_document_the_send_carries_goes_to_its_empty_question_from_this_modules_copy()
    {
        var host = await WithServerFileAsync(Receipt);
        var reply = host.AddReply("wamid.server-file", new Dictionary<string, string>());

        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Completed, reply.FlowCaptureState);
        var file = Assert.Single(Assert.IsType<StructuredRecordCaptureRequest>(host.Sink.Request).Files!);
        Assert.Equal((host.Mapping[0].FieldId, "comprobante.jpg", "image/jpeg"), (file.FieldId, file.Name, file.ContentType));
        Assert.Equal(Receipt, file.Content);
        Assert.Equal(0, host.Media.DownloadCount);
    }

    // The question may ask for one file, like the library's receipt: its twin in the Flow is optional, and Meta returns no array when
    // the person keeps the document the send carries. The server adds it instead of rejecting the reply.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_question_that_asks_for_one_file_takes_the_document_the_send_carries_when_the_flow_returns_none(bool emptyArray)
    {
        var host = await WithServerFileAsync(Receipt, optional: false);
        var reply = emptyArray ? host.AddEmptyFileReply("wamid.server-file-required-empty")
            : host.AddReply("wamid.server-file-required-absent", new Dictionary<string, string>());

        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Completed, reply.FlowCaptureState);
        var file = Assert.Single(Assert.IsType<StructuredRecordCaptureRequest>(host.Sink.Request).Files!);
        Assert.Equal(host.Mapping[0].FieldId, file.FieldId);
        Assert.Equal(Receipt, file.Content);
        Assert.Equal(0, host.Media.DownloadCount);
    }

    [Fact]
    public async Task A_copy_that_changed_is_not_taken_and_nothing_is_captured()
    {
        var host = await WithServerFileAsync([9, 8, 7, 5]);
        var reply = host.AddReply("wamid.server-file-changed", new Dictionary<string, string>());

        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal((WhatsAppFlowCaptureState.Rejected, ArquitecturaBase.Domain.Files.FileErrors.HashMismatch.Code), (reply.FlowCaptureState, reply.FlowCaptureErrorCode));
        Assert.Equal(0, host.Sink.CaptureCount);
    }

    [Fact]
    public async Task A_file_the_person_chose_replaces_the_one_the_send_carries()
    {
        var host = await WithServerFileAsync(Receipt);
        var reply = host.AddFileReply("wamid.person-file");

        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Completed, reply.FlowCaptureState);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(Assert.IsType<StructuredRecordCaptureRequest>(host.Sink.Request).Files!).Content);
        Assert.Equal(1, host.Media.DownloadCount);
    }
}
