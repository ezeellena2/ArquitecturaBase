using System.Text;
using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Channels;

public sealed class UnavailableStructuredAttachmentReaderTests
{
    [Fact]
    public async Task Without_the_owner_nobody_attaches_and_preparing_is_unavailable()
    {
        using var provider = new ServiceCollection().AddApplication().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var reader = Assert.IsType<UnavailableStructuredAttachmentReader>(scope.ServiceProvider.GetRequiredService<IStructuredAttachmentReader>());
        var ct = TestContext.Current.CancellationToken;

        Assert.False(await reader.CanAttachAsync(Guid.NewGuid(), ct));
        var preparation = await reader.PrepareAsync(Encoding.UTF8.GetBytes("Code,Text\nA,North\n"), "options.csv", ct);
        Assert.Equal(StructuredAttachmentRejection.Unavailable, preparation.Rejection);
        Assert.Null(preparation.Spreadsheet);
    }

    [Fact]
    public async Task Cancellation_remains_observable_without_the_owner()
    {
        var reader = new UnavailableStructuredAttachmentReader();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.CanAttachAsync(Guid.NewGuid(), cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.PrepareAsync([], "options.csv", cancelled.Token));
    }
}
