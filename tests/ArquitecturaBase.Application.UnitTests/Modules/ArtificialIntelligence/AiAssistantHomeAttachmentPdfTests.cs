using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// F6: the route of a file turn takes no PDF or photo, and decides it by the bytes, so the reader never opens one.
public sealed class AiAssistantHomeAttachmentPdfTests
{
    [Theory]
    [InlineData("%PDF-1.7\n%%EOF")]
    [InlineData("ÿØÿà photo")]
    public async Task A_pdf_or_a_photo_is_a_request_error_without_the_reader_opening_it(string text)
    {
        var reader = new CountingReader();
        var policy = new AiAssistantHomeAttachmentPolicy(null!, reader, null!, null!);

        var result = await policy.PrepareAsync(System.Text.Encoding.Latin1.GetBytes(text), "archivo.pdf", new(true, true), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Ai.Assistant.Invalid", result.Error.Code);
        Assert.Equal(0, reader.Prepared);
    }

    [Fact]
    public async Task A_file_that_is_neither_goes_to_the_reader()
    {
        var reader = new CountingReader();
        var policy = new AiAssistantHomeAttachmentPolicy(null!, reader, null!, null!);

        await policy.PrepareAsync("a,b\n1,2\n"u8.ToArray(), "datos.csv", new(true, true), TestContext.Current.CancellationToken);

        Assert.Equal(1, reader.Prepared);
    }

    private sealed class CountingReader : IStructuredAttachmentReader
    {
        public int Prepared { get; private set; }

        public Task<StructuredAttachmentAccess> CanAttachAsync(Guid actorUserId, CancellationToken cancellationToken) => Task.FromResult(new StructuredAttachmentAccess(true, true));

        public Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken cancellationToken)
        {
            Prepared++;
            return Task.FromResult(StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Unreadable));
        }
    }
}
