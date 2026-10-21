using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormRetirementSourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task The_owner_lock_uses_the_callers_transaction_without_reading_or_writing_a_definition()
    {
        var f = new FormRetirementFixture(); var locks = 0; f.Drafts.BeforeLock = () => locks++;
        var result = await f.Drafts.UnitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await f.Source.LockSelectionAsync(ct);
            return Result.Success();
        }, ArquitecturaBase.Application.Interfaces.Persistence.CommitPolicy.OnSuccess, Ct);
        Assert.True(result.IsSuccess); Assert.Equal(1, locks); Assert.Equal(1, f.Drafts.UnitOfWork.Transactions);
        Assert.Equal(0, f.Reads); Assert.Equal(0, f.Drafts.EntityReads); Assert.Empty(f.Drafts.Removed);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_single_owner_read_preserves_exact_pairs_and_does_not_start_a_transaction(bool hasUnused)
    {
        var f = new FormRetirementFixture(); var definition = Guid.NewGuid();
        StructuredFormVersionReference[] candidates = [new(definition, Guid.NewGuid()), new(definition, Guid.NewGuid()), new(Guid.NewGuid(), Guid.NewGuid())];
        f.Unused = hasUnused ? [candidates[1]] : [];
        var result = await f.Source.ListUnusedAsync(candidates, Ct);
        Assert.Equal(f.Unused, result); Assert.Equal(candidates, f.Candidates); Assert.Equal(1, f.Reads);
        Assert.Equal(Ct, f.ReadCancellation); Assert.Equal(0, f.Drafts.UnitOfWork.Transactions); Assert.Equal(0, f.Drafts.EntityReads);
    }
    [Fact]
    public async Task An_owner_read_failure_is_never_converted_into_retirement_authority()
    {
        var f = new FormRetirementFixture { Failure = new InvalidOperationException("Read failed") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Source.ListUnusedAsync([new(Guid.NewGuid(), Guid.NewGuid())], Ct));
        Assert.Equal(1, f.Reads); Assert.Equal(0, f.Drafts.UnitOfWork.Transactions); Assert.Empty(f.Drafts.Removed);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_owner_replaces_the_disabled_source_once_in_either_registration_order(bool coreFirst)
    {
        var services = new ServiceCollection();
        if (coreFirst) services.AddApplication().AddBusinessFormsApplication();
        else services.AddBusinessFormsApplication().AddApplication();
        services.AddBusinessFormsApplication();
        var registration = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IStructuredFormRetirementSource));
        Assert.Equal(typeof(BusinessFormRetirementSource), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
    }
}
