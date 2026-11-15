using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Channels.SystemQueries;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.SystemQueries;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Channels;

public sealed class SystemQuerySourcesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task User_summary_counts_all_accounts_and_never_contains_personal_information()
    {
        var users = new InMemoryUserAccounts();
        var actor = users.AddUser("private@example.com");
        users.AddUser("inactive@example.com", isActive: false);
        var permissions = new PermissionDouble { Allowed = true };
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
        var source = new UsersSystemQuerySource(users, permissions, clock);
        var result = await source.ExecuteAsync(Request(actor.Id, "users.summary"), Ct);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<AssistantSystemQueryPayload>(result.Value.Payload);
        var evidence = Assert.IsType<SystemQueryEvidence>(payload.Evidence);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, evidence.ObservedAtUtc);
        Assert.Equal(["1", "1"], evidence.Rows.Single().Values.Select(value => value.Value));
        Assert.DoesNotContain("example.com", JsonSerializer.Serialize(evidence), StringComparison.Ordinal);
        Assert.Equal([Permissions.Users.Read], evidence.RequiredPermissions);
        Assert.Same(evidence, Assert.Single(result.Value.Evidences));
        Assert.Equal(1, result.Value.ItemCount);
    }

    [Fact]
    public async Task Revoked_permission_prevents_querying_a_source()
    {
        var users = new InMemoryUserAccounts();
        var actor = users.AddUser("actor@example.com");
        var source = new UsersSystemQuerySource(users, new PermissionDouble(), new FakeTimeProvider());
        var result = await source.ExecuteAsync(Request(actor.Id, "users.summary"), Ct);
        Assert.Equal(SystemQueryErrors.Forbidden, result.Error);
    }

    [Theory]
    [InlineData("users.summary", "{}")]
    [InlineData("roles.list", "{}")]
    [InlineData("roles.permissions", "{\"roleId\":null,\"roleName\":\"Readers\"}")]
    [InlineData("system.help", "{\"topic\":\"profile\"}")]
    public async Task A_read_capability_keeps_the_Home_payload_and_decodes_its_exact_version(string id, string arguments)
    {
        var fixture = Create(id);
        var definition = Assert.Single(fixture.Source.Definitions, candidate => candidate.Id == id);
        Assert.Equal(AssistantCapabilityKind.Read, definition.Kind);
        Assert.Empty(definition.RequiredPermissions.Subject);
        Assert.True(definition.Budget.InputBytes > 0);
        Assert.True(definition.Budget.OutputBytes > 0);
        Assert.Equal(50, definition.Budget.MaximumItems);
        Assert.True(definition.Budget.TimeoutSeconds > 0);
        using var schema = JsonDocument.Parse(definition.OutputSchemaJson);
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());

        var result = await fixture.Source.ExecuteAsync(Request(fixture.ActorId, id, arguments), Ct);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<AssistantSystemQueryPayload>(result.Value.Payload);
        var json = JsonSerializer.Serialize(result.Value.Payload, result.Value.Payload.GetType(), JsonOptions);
        Assert.Equal(JsonSerializer.Serialize(new { payload.State, payload.Evidence }, JsonOptions), json);
        using var output = JsonDocument.Parse(json);
        Assert.Equal(["state", "evidence"], output.RootElement.EnumerateObject().Select(property => property.Name));
        var parsed = fixture.Source.ParseOutput(new(id, 1), json);
        Assert.True(parsed.IsSuccess);
        Assert.Equal(json, JsonSerializer.Serialize(parsed.Value, parsed.Value.GetType(), JsonOptions));
        Assert.True(fixture.Source.ParseOutput(new(id, 2), json).IsFailure);
        Assert.True(fixture.Source.ParseOutput(new("another.source", 1), json).IsFailure);
    }

    [Theory]
    [InlineData("users.summary", "[]")]
    [InlineData("users.summary", "{\"scope\":\"all\"}")]
    [InlineData("users.summary", "{\"actorUserId\":null}")]
    [InlineData("roles.list", "{\"roleId\":null}")]
    [InlineData("roles.permissions", "{}")]
    [InlineData("roles.permissions", "{\"roleId\":null,\"roleName\":null}")]
    [InlineData("roles.permissions", "{\"roleId\":\"not-a-guid\",\"roleName\":null}")]
    [InlineData("roles.permissions", "{\"roleId\":\"00000000-0000-0000-0000-000000000000\",\"roleName\":null}")]
    [InlineData("roles.permissions", "{\"roleId\":null,\"roleName\":\"Readers\",\"roleName\":\"Writers\"}")]
    [InlineData("roles.permissions", "{\"roleId\":null,\"roleName\":\"Readers\",\"sql\":\"select\"}")]
    [InlineData("roles.permissions", "{\"roleId\":null,\"roleName\":42}")]
    [InlineData("system.help", "{\"topic\":\"unknown\"}")]
    [InlineData("system.help", "{\"topic\":\"profile\",\"topic\":\"profile\"}")]
    [InlineData("system.help", "{\"topic\":\"profile\",\"url\":\"https://example.test\"}")]
    [InlineData("system.help", "{\"topic\":null}")]
    [InlineData("system.help", "{")]
    public async Task Closed_arguments_are_rejected_before_authorization_or_data_reads(string id, string arguments)
    {
        var fixture = Create(id);
        var result = await fixture.Source.ExecuteAsync(Request(fixture.ActorId, id, arguments), Ct);
        Assert.Equal(SystemQueryErrors.InvalidArguments, result.Error);
        Assert.Equal(0, fixture.Permissions.Calls);
        Assert.Equal(0, fixture.Roles.Calls);
    }

    [Theory]
    [InlineData("users.summary", "{}")]
    [InlineData("roles.list", "{}")]
    [InlineData("system.help", "{\"topic\":\"profile\"}")]
    public async Task An_unregistered_version_is_rejected_before_authorization(string id, string arguments)
    {
        var fixture = Create(id);
        var result = await fixture.Source.ExecuteAsync(Request(fixture.ActorId, id, arguments) with { Capability = new(id, 2) }, Ct);
        Assert.Equal(SystemQueryErrors.InvalidArguments, result.Error);
        Assert.Equal(0, fixture.Permissions.Calls);
        Assert.Equal(0, fixture.Roles.Calls);
    }

    [Fact]
    public async Task Role_permissions_resolves_one_exact_selector_and_keeps_only_installed_permissions()
    {
        var fixture = Create("roles.permissions");
        var role = Assert.Single(fixture.Roles.Roles);
        var arguments = JsonSerializer.Serialize(new { roleId = role.Id, roleName = (string?)null });
        var result = await fixture.Source.ExecuteAsync(Request(fixture.ActorId, "roles.permissions", arguments), Ct);
        Assert.True(result.IsSuccess);
        var evidence = Assert.Single(result.Value.Evidences);
        var row = Assert.Single(evidence.Rows);
        Assert.Equal(role.Id, row.Id);
        Assert.Contains(row.Values, value => value.Label == Permissions.Users.Read);
        Assert.DoesNotContain(row.Values, value => value.Label == "removed.permission");
        Assert.Equal(role.Id, fixture.Roles.FoundId);
        Assert.Null(fixture.Roles.FoundName);
    }

    [Fact]
    public async Task A_truncated_role_catalog_preserves_the_state_without_inventing_evidence()
    {
        var fixture = Create("roles.list");
        fixture.Roles.TotalCount = 51;
        var result = await fixture.Source.ExecuteAsync(Request(fixture.ActorId, "roles.list"), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("truncated", result.Value.State);
        var payload = Assert.IsType<AssistantSystemQueryPayload>(result.Value.Payload);
        Assert.Null(payload.Evidence);
        Assert.Empty(result.Value.Evidences);
        Assert.Equal(0, result.Value.ItemCount);
        Assert.Equal(50, fixture.Roles.LastPage!.PageSize);
    }

    [Fact]
    public async Task Help_does_not_offer_a_destination_the_actor_cannot_read()
    {
        var fixture = Create("system.help");
        fixture.Permissions.Allowed = false;
        var result = await fixture.Source.ExecuteAsync(Request(fixture.ActorId, "system.help", "{\"topic\":\"userRoles\"}"), Ct);
        Assert.True(result.IsSuccess);
        var evidence = Assert.Single(result.Value.Evidences);
        Assert.Null(evidence.Destination);
        Assert.Equal(1, evidence.Revision);
        Assert.Empty(evidence.RequiredPermissions);
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("nestedExtra")]
    [InlineData("nestedDuplicate")]
    [InlineData("rowExtra")]
    [InlineData("wrongSource")]
    [InlineData("wrongVersion")]
    [InlineData("wrongType")]
    [InlineData("unknownState")]
    public async Task Output_codecs_reject_unclosed_or_mismatched_recorded_payloads(string mutation)
    {
        var fixture = Create("users.summary");
        var result = await fixture.Source.ExecuteAsync(Request(fixture.ActorId, "users.summary"), Ct);
        var json = JsonSerializer.Serialize(result.Value.Payload, result.Value.Payload.GetType(), JsonOptions);
        var wrongType = JsonNode.Parse(json)!;
        wrongType["evidence"]!["rows"] = new JsonObject();
        json = mutation switch
        {
            "extra" => json.Insert(1, "\"unexpected\":true,"),
            "duplicate" => json.Insert(1, "\"state\":\"complete\","),
            "nestedExtra" => json.Replace("\"evidence\":{", "\"evidence\":{\"unexpected\":true,", StringComparison.Ordinal),
            "nestedDuplicate" => json.Replace("\"evidence\":{", "\"evidence\":{\"sourceVersion\":1,", StringComparison.Ordinal),
            "rowExtra" => json.Replace("\"values\":[", "\"unexpected\":true,\"values\":[", StringComparison.Ordinal),
            "wrongSource" => json.Replace("\"sourceId\":\"users.summary\"", "\"sourceId\":\"roles.list\"", StringComparison.Ordinal),
            "wrongVersion" => json.Replace("\"sourceVersion\":1", "\"sourceVersion\":2", StringComparison.Ordinal),
            "wrongType" => wrongType.ToJsonString(),
            _ => json.Replace("\"state\":\"complete\"", "\"state\":\"invented\"", StringComparison.Ordinal),
        };
        Assert.True(fixture.Source.ParseOutput(new("users.summary", 1), json).IsFailure);
    }

    private static AssistantCapabilityRequest Request(Guid actorId, string id, string arguments = "{}") =>
        new(Guid.CreateVersion7(), new("home", actorId, null, "en", "UTC", new([], []), [new(id, 1)], []), new(id, 1), arguments, []);

    private static (IAssistantCapabilitySource Source, Guid ActorId, PermissionDouble Permissions, RoleReader Roles) Create(string id)
    {
        var users = new InMemoryUserAccounts();
        var actor = users.AddUser("fictional@example.test");
        var permissions = new PermissionDouble { Allowed = true };
        var roles = new RoleReader();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
        IAssistantCapabilitySource source = id switch
        {
            "users.summary" => new UsersSystemQuerySource(users, permissions, clock),
            "roles.list" or "roles.permissions" => new RolesSystemQuerySource(roles, users, permissions, new PermissionCatalog([]), clock),
            _ => new HelpSystemQuerySource(users, permissions, clock),
        };
        return (source, actor.Id, permissions, roles);
    }

    private sealed class PermissionDouble : IPermissionService
    {
        public bool Allowed { get; set; }
        public int Calls { get; private set; }
        public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Allowed);
        }
        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>(Allowed ? Permissions.All : []);

    }

    private sealed class RoleReader : IRoleReader
    {
        public IReadOnlyList<RoleRow> Roles { get; } = [new(Guid.CreateVersion7(), "Readers", "Read only", false, 2, [Permissions.Users.Read, "removed.permission"])];
        public int Calls { get; private set; }
        public int TotalCount { get; set; } = 1;
        public Guid? FoundId { get; private set; }
        public string? FoundName { get; private set; }
        public ListRolesRequest? LastPage { get; private set; }
        public Task<RoleRow?> FindByNameAsync(string name, CancellationToken cancellationToken)
        {
            Calls++;
            FoundName = name;
            return Task.FromResult(Roles.FirstOrDefault(role => role.Name == name));
        }
        public Task<RoleRow?> FindByIdAsync(Guid roleId, CancellationToken cancellationToken)
        {
            Calls++;
            FoundId = roleId;
            return Task.FromResult(Roles.FirstOrDefault(role => role.Id == roleId));
        }
        public Task<PagedResult<RoleRow>> ListRolesAsync(ListRolesRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            LastPage = request;
            return Task.FromResult(new PagedResult<RoleRow>(Roles, request.Page, request.PageSize, TotalCount));
        }
        public Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RoleRow>> ListAllRolesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExistsByNameAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
