using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
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
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task An_inactive_account_cannot_read_business_facts_even_with_a_source_permission()
    {
        var accounts = new InMemoryUserAccounts();
        var actor = accounts.AddUser("fictional@example.test", isActive: false);
        var permissions = new FakePermissionService();
        permissions.Permissions[actor.Id] = [ArtificialIntelligencePermissions.BusinessRead];
        var reader = new Reader();
        var source = new AiBusinessSystemQuerySource(reader, permissions, new FakeTimeProvider(), accounts);
        var result = await source.ExecuteAsync(Request(actor.Id), Ct);
        Assert.Equal(AiAssistantErrors.ToolRejected, result.Error);
        Assert.Equal(0, reader.Calls);
    }

    [Theory]
    [InlineData("es", "Nombre")]
    [InlineData("en", "Name")]
    public async Task Business_rows_have_localized_labels_and_do_not_publish_raw_details_json(string culture, string nameLabel)
    {
        var fixture = Create();
        var result = await fixture.Source.ExecuteAsync(Request(fixture.ActorId, culture), Ct);
        Assert.True(result.IsSuccess);
        var evidence = Assert.Single(result.Value.Evidences);
        Assert.Contains(evidence.Rows.SelectMany(row => row.Values), value => value.Label == nameLabel && value.Value == "Fictional shop");
        Assert.DoesNotContain(evidence.Rows.SelectMany(row => row.Values), value => value.Label == "details" || value.Value.Contains("\"openingHours\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_business_read_preserves_the_Home_shape_and_uses_its_exact_output_codec()
    {
        var fixture = Create();
        var definition = Assert.Single(fixture.Source.Definitions);
        Assert.Equal("business.information", definition.Id);
        Assert.Equal(AssistantCapabilityKind.Read, definition.Kind);
        Assert.Equal([ArtificialIntelligencePermissions.BusinessRead], definition.RequiredPermissions.Actor);
        Assert.Empty(definition.RequiredPermissions.Subject);
        var result = await fixture.Source.ExecuteAsync(Request(fixture.ActorId), Ct);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<AssistantSystemQueryPayload>(result.Value.Payload);
        var json = JsonSerializer.Serialize(result.Value.Payload, result.Value.Payload.GetType(), JsonOptions);
        Assert.Equal(JsonSerializer.Serialize(new { payload.State, payload.Evidence }, JsonOptions), json);
        var parsed = fixture.Source.ParseOutput(new("business.information", 1), json);
        Assert.True(parsed.IsSuccess);
        Assert.Equal(json, JsonSerializer.Serialize(parsed.Value, parsed.Value.GetType(), JsonOptions));
        Assert.True(fixture.Source.ParseOutput(new("business.information", 2), json).IsFailure);
        Assert.True(fixture.Source.ParseOutput(new("users.summary", 1), json).IsFailure);
        Assert.True(fixture.Source.ParseOutput(new("business.information", 1), json.Insert(1, "\"extra\":true,")).IsFailure);
        Assert.True(fixture.Source.ParseOutput(new("business.information", 1), json.Replace("\"sourceVersion\":1", "\"sourceVersion\":2", StringComparison.Ordinal)).IsFailure);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"scope\":\"all\"}")]
    [InlineData("{\"roleId\":null}")]
    [InlineData("{\"subjectUserId\":null}")]
    [InlineData("{")]
    public async Task Business_arguments_are_closed_before_any_business_read(string arguments)
    {
        var fixture = Create();
        var result = await fixture.Source.ExecuteAsync(Request(fixture.ActorId) with { ArgumentsJson = arguments }, Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(0, fixture.Reader.Calls);
    }

    [Fact]
    public async Task Missing_business_information_preserves_not_connected_without_evidence()
    {
        var fixture = Create();
        fixture.Reader.Revision = 0;
        var result = await fixture.Source.ExecuteAsync(Request(fixture.ActorId), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("notConnected", result.Value.State);
        Assert.Null(Assert.IsType<AssistantSystemQueryPayload>(result.Value.Payload).Evidence);
        Assert.Empty(result.Value.Evidences);
        Assert.Equal(0, result.Value.ItemCount);
        var json = JsonSerializer.Serialize(result.Value.Payload, result.Value.Payload.GetType(), JsonOptions);
        Assert.True(fixture.Source.ParseOutput(new("business.information", 1), json).IsSuccess);
    }

    private static AssistantCapabilityRequest Request(Guid actorId, string culture = "es") =>
        new(Guid.CreateVersion7(), new("home", actorId, null, culture, "UTC", new([], []), [new("business.information", 1)], []),
            new("business.information", 1), "{}", []);

    private static (IAssistantCapabilitySource Source, Guid ActorId, Reader Reader) Create()
    {
        var accounts = new InMemoryUserAccounts();
        var actor = accounts.AddUser("fictional@example.test");
        var permissions = new FakePermissionService();
        permissions.Permissions[actor.Id] = [ArtificialIntelligencePermissions.BusinessRead];
        var reader = new Reader();
        return (new AiBusinessSystemQuerySource(reader, permissions, new FakeTimeProvider(), accounts), actor.Id, reader);
    }

    private sealed class Reader : IAiBusinessInformationReader
    {
        public int Calls { get; private set; }
        public long Revision { get; set; } = 1;
        public Task<AiBusinessInformationResponse> FindAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AiBusinessInformationResponse("Fictional shop", "Repairs", "", "", "", "", "", Revision));
        }
        public Task<AiBusinessInformationChangeResponse?> FindChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult<AiBusinessInformationChangeResponse?>(null);
    }
}
