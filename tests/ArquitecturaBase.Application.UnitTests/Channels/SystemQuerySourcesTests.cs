using ArquitecturaBase.Application.Channels.SystemQueries;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.SystemQueries;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Channels;

public sealed class SystemQuerySourcesTests
{
    [Fact]
    public async Task User_summary_counts_all_accounts_and_never_contains_personal_information()
    {
        var users = new InMemoryUserAccounts();
        var actor = users.AddUser("private@example.com");
        users.AddUser("inactive@example.com", isActive: false);
        var permissions = new PermissionDouble { Allowed = true };
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
        var source = new UsersSystemQuerySource(users, permissions, clock);
        var result = await source.ExecuteAsync(new(actor.Id, "users.summary", "en", "UTC"), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Evidence);
        var evidence = result.Value.Evidence;
        Assert.Equal(clock.GetUtcNow().UtcDateTime, evidence.ObservedAtUtc);
        Assert.Equal(["1", "1"], evidence.Rows.Single().Values.Select(value => value.Value));
        Assert.DoesNotContain("example.com", System.Text.Json.JsonSerializer.Serialize(evidence), StringComparison.Ordinal);
        Assert.Equal([Permissions.Users.Read], evidence.RequiredPermissions);
    }

    [Fact]
    public async Task Revoked_permission_prevents_querying_a_source()
    {
        var users = new InMemoryUserAccounts();
        var actor = users.AddUser("actor@example.com");
        var source = new UsersSystemQuerySource(users, new PermissionDouble(), new FakeTimeProvider());
        var result = await source.ExecuteAsync(new(actor.Id, "users.summary", "es", "UTC"), TestContext.Current.CancellationToken);
        Assert.Equal(SystemQueryErrors.Forbidden, result.Error);
    }

    private sealed class PermissionDouble : IPermissionService
    {
        public bool Allowed { get; init; }
        public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) => Task.FromResult(Allowed);
        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>(Allowed ? Permissions.All : []);
        public Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
