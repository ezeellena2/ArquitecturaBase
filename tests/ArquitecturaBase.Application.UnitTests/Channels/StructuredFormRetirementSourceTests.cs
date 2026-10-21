using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Channels;

public sealed class StructuredFormRetirementSourceTests
{
    [Fact]
    public async Task Core_without_the_owner_never_declares_a_candidate_unused()
    {
        using var provider = new ServiceCollection().AddApplication().BuildServiceProvider();
        var source = provider.GetRequiredService<IStructuredFormRetirementSource>();
        var first = new StructuredFormVersionReference(Guid.NewGuid(), Guid.NewGuid());
        var second = new StructuredFormVersionReference(first.DefinitionId, Guid.NewGuid());
        var result = await source.ListUnusedAsync([first, second], TestContext.Current.CancellationToken);
        Assert.Empty(result);
    }
    [Fact]
    public async Task The_disabled_owner_lock_has_no_effect()
    {
        using var provider = new ServiceCollection().AddApplication().BuildServiceProvider();
        var source = provider.GetRequiredService<IStructuredFormRetirementSource>();
        await source.LockSelectionAsync(TestContext.Current.CancellationToken);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_disabled_owner_honors_cancellation_for_lock_and_read(bool locking)
    {
        using var provider = new ServiceCollection().AddApplication().BuildServiceProvider();
        var source = provider.GetRequiredService<IStructuredFormRetirementSource>();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        if (locking) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.LockSelectionAsync(cancellation.Token));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ListUnusedAsync([], cancellation.Token));
    }
}
