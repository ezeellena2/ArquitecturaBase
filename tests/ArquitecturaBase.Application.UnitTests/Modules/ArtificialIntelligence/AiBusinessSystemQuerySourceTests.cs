using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiBusinessSystemQuerySourceTests
{
    [Fact]
    public async Task An_inactive_account_cannot_read_business_facts_even_with_a_source_permission()
    {
        var accounts = new InMemoryUserAccounts();
        var actor = accounts.AddUser("fictional@example.test", isActive: false);
        var permissions = new FakePermissionService();
        permissions.Permissions[actor.Id] = [ArtificialIntelligencePermissions.BusinessRead];
        var source = new AiBusinessSystemQuerySource(new Reader(), permissions, new FakeTimeProvider(), accounts);
        var result = await source.ExecuteAsync(new(actor.Id, "business.information", "es", "UTC"), TestContext.Current.CancellationToken);
        Assert.Equal(AiAssistantErrors.ToolRejected, result.Error);
    }

    [Theory]
    [InlineData("es", "Nombre")]
    [InlineData("en", "Name")]
    public async Task Business_rows_have_localized_labels_and_do_not_publish_raw_details_json(string culture, string nameLabel)
    {
        var accounts = new InMemoryUserAccounts();
        var actor = accounts.AddUser("fictional@example.test");
        var permissions = new FakePermissionService();
        permissions.Permissions[actor.Id] = [ArtificialIntelligencePermissions.BusinessRead];
        var source = new AiBusinessSystemQuerySource(new Reader(), permissions, new FakeTimeProvider(), accounts);
        var result = await source.ExecuteAsync(new(actor.Id, "business.information", culture, "UTC"), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value.Evidence!.Rows.SelectMany(row => row.Values), value => value.Label == nameLabel && value.Value == "Fictional shop");
        Assert.DoesNotContain(result.Value.Evidence.Rows.SelectMany(row => row.Values), value => value.Label == "details" || value.Value.Contains("\"openingHours\"", StringComparison.Ordinal));
    }

    private sealed class Reader : IAiBusinessInformationReader
    {
        public Task<AiBusinessInformationResponse> FindAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AiBusinessInformationResponse("Fictional shop", "Repairs", "", "", "", "", "", 1));
        public Task<AiBusinessInformationChangeResponse?> FindChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult<AiBusinessInformationChangeResponse?>(null);
    }
}
