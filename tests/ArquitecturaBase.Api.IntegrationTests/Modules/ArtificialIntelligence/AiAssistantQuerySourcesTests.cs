using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantQuerySourcesTests(ApiFactory factory)
{
    [Fact]
    public async Task User_summary_counts_the_full_dataset_and_excludes_deleted_accounts()
    {
        var ct = TestContext.Current.CancellationToken;
        var before = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserReader>().FindSummaryAsync(ct));
        await factory.InTransactionAsync(async services =>
        {
            var users = services.GetRequiredService<IUserRepository>();
            await users.CreateAsync(Email.Create(TestEmails.Unique("summary-active")).Value, null, false, null, "es", ct);
            var inactive = await users.CreateAsync(Email.Create(TestEmails.Unique("summary-inactive")).Value, null, false, null, "es", ct);
            await users.SetActiveAsync(inactive.Id, false, ct);
            var removed = await users.CreateAsync(Email.Create(TestEmails.Unique("summary-deleted")).Value, null, false, null, "es", ct);
            await users.DeleteAsync(removed.Id, ct);
        });
        var after = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserReader>().FindSummaryAsync(ct));
        Assert.Equal(before.ActiveCount + 1, after.ActiveCount);
        Assert.Equal(before.InactiveCount + 1, after.InactiveCount);
    }

    [Fact]
    public async Task Roles_are_found_by_exact_normalized_name_instead_of_partial_search()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = "support-" + Guid.NewGuid().ToString("N");
        await factory.InTransactionAsync(services => services.GetRequiredService<IRoleRepository>().CreateAsync(name, "Curated role", [], ct));
        await factory.ExecuteScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IRoleReader>();
            var role = await reader.FindByNameAsync("  " + name.ToUpperInvariant() + "  ", ct);
            Assert.NotNull(role);
            Assert.Equal(name, role.Name);
            Assert.Null(await reader.FindByNameAsync("support", ct));
        });
    }
}
