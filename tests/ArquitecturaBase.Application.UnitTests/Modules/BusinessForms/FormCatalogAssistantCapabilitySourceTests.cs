using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogAssistantCapabilitySourceTests
{
    private const string Read = BusinessFormCatalogAssistantCapabilitySource.MetadataId;
    private const string Propose = BusinessFormCatalogAssistantCapabilitySource.ProposeId;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
    private static string Arguments(FormCatalogProposalPayload value) => Json(new { value.Name, value.Description, value.Filters, value.Limitations });

    [Fact]
    public void The_catalog_declares_only_fixed_metadata_and_a_human_reviewed_proposal()
    {
        var definitions = new CatalogAssistantFixture().Source.Definitions;
        Assert.Equal(2, definitions.Count);
        Assert.Collection(definitions, read => { Assert.Equal(Read, read.Id); Assert.Equal(AssistantCapabilityKind.Read, read.Kind); Assert.Equal([BusinessFormsPermissions.CatalogsRead], read.RequiredPermissions.Actor); Assert.Empty(read.RequiredPermissions.Subject); },
            propose => { Assert.Equal(Propose, propose.Id); Assert.Equal(AssistantCapabilityKind.Proposal, propose.Kind); Assert.Contains(BusinessFormsPermissions.CatalogsManage, propose.RequiredPermissions.Actor); Assert.Empty(propose.RequiredPermissions.Subject); });
        Assert.All(definitions, definition => { Assert.Equal(1, definition.Version); using var schema = JsonDocument.Parse(definition.InputSchemaJson); Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean()); });
    }

    [Fact]
    public void The_model_does_not_choose_an_internal_metadata_ordinal()
    {
        var definition = new CatalogAssistantFixture().Source.Definitions.Single(item => item.Id == Propose);
        using var schema = JsonDocument.Parse(definition.InputSchemaJson);
        Assert.False(schema.RootElement.GetProperty("properties").TryGetProperty("metadataReadOrdinal", out _));
    }

    [Fact]
    public async Task The_owner_stamps_the_latest_complete_metadata_read_on_the_proposal()
    {
        var host = new CatalogAssistantFixture();
        var proposal = CatalogAssistantFixture.Proposal;
        var arguments = Json(new { proposal.Name, proposal.Description, proposal.Filters, proposal.Limitations });
        var result = await host.Source.ExecuteAsync(host.Request(Propose, arguments, [host.Observed(3)]), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(3, Assert.IsType<FormCatalogProposalPayload>(result.Value.Payload).MetadataReadOrdinal);
    }

    [Fact]
    public async Task Metadata_exposes_roles_and_the_fixed_grammar_without_people_or_samples()
    {
        var host = new CatalogAssistantFixture([BusinessFormsPermissions.CatalogsRead]);
        var result = await host.Source.ExecuteAsync(host.Request(Read), Ct);
        Assert.True(result.IsSuccess);
        var metadata = Assert.IsType<FormCatalogMetadataPayload>(result.Value.Payload);
        Assert.Equal("users", metadata.Source);
        Assert.Equal(["name", "role", "active"], metadata.Columns.Select(column => column.Key));
        Assert.Equal(CatalogAssistantFixture.RoleId.ToString(), Assert.Single(metadata.Columns[1].Choices!).Key);
        Assert.DoesNotContain("sample", Json(metadata), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("catalog-proposal@example.test", Json(metadata), StringComparison.Ordinal);
        Assert.Equal(1, host.Reader.Reads);
    }

    [Theory]
    [InlineData(false, true, true, Read)]
    [InlineData(true, false, true, Read)]
    [InlineData(true, true, false, Propose)]
    public async Task The_real_actor_keeps_the_capability_permission_before_any_metadata_read(bool active, bool read, bool manage, string capability)
    {
        var permissions = new List<string>();
        if (read) permissions.Add(BusinessFormsPermissions.CatalogsRead);
        if (manage) permissions.Add(BusinessFormsPermissions.CatalogsManage);
        var host = new CatalogAssistantFixture(permissions.ToArray(), active);
        var result = await host.Source.ExecuteAsync(host.Request(capability, capability == Propose ? Arguments(CatalogAssistantFixture.Proposal) : "{}", [host.Observed()]), Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(0, host.Reader.Reads);
    }

    [Fact]
    public async Task A_complete_prior_metadata_read_allows_a_typed_filters_proposal()
    {
        var host = new CatalogAssistantFixture();
        var result = await host.Source.ExecuteAsync(host.Request(Propose, Arguments(CatalogAssistantFixture.Proposal), [host.Observed()]), Ct);
        Assert.True(result.IsSuccess);
        var proposal = Assert.IsType<FormCatalogProposalPayload>(result.Value.Payload);
        Assert.Equal(2, proposal.MetadataReadOrdinal);
        Assert.Equal("Supervisores", proposal.Name);
        Assert.Equal("name", proposal.Filters.TextColumn);
        Assert.Collection(proposal.Filters.Conditions!, condition => { Assert.Equal("role", condition!.Column); Assert.Equal(CatalogAssistantFixture.RoleId.ToString(), condition.Value); },
            condition => { Assert.Equal("active", condition!.Column); Assert.Equal("true", condition.Value); });
        Assert.Equal(0, host.Reader.Reads);
    }

    [Theory]
    [InlineData("no-read")]
    [InlineData("other-turn")]
    [InlineData("other-version")]
    [InlineData("duplicate-ordinal")]
    public async Task A_proposal_cannot_mix_another_turn_version_or_metadata_snapshot(string mismatch)
    {
        var host = new CatalogAssistantFixture();
        var observed = mismatch switch
        {
            "other-turn" => host.Observed() with { TurnId = Guid.NewGuid() },
            "other-version" => host.Observed() with { Capability = new(Read, 2) },
            _ => host.Observed(),
        };
        var result = await host.Source.ExecuteAsync(host.Request(Propose, Arguments(CatalogAssistantFixture.Proposal), mismatch == "no-read" ? [] : mismatch == "duplicate-ordinal" ? [observed, observed] : [observed]), Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(0, host.Reader.Reads);
    }

    [Fact]
    public async Task A_role_must_come_from_the_one_metadata_read_that_was_selected()
    {
        var host = new CatalogAssistantFixture();
        var otherRole = CatalogAssistantFixture.Proposal with { Filters = CatalogAssistantFixture.Proposal.Filters with { Conditions = [new("role", "has", Guid.NewGuid().ToString())] } };
        var result = await host.Source.ExecuteAsync(host.Request(Propose, Arguments(otherRole), [host.Observed()]), Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(0, host.Reader.Reads);
    }

    [Fact]
    public async Task Too_many_roles_fail_instead_of_pretending_to_have_complete_metadata()
    {
        var host = new CatalogAssistantFixture();
        host.Reader.Roles = Enumerable.Range(0, 201).Select(_ => new FormCatalogFilterChoiceResponse(Guid.NewGuid().ToString(), "Fictitious role")).ToArray();
        var result = await host.Source.ExecuteAsync(host.Request(Read), Ct);
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("{\"sampleUserId\":\"8373c8e5-1e0d-4878-93eb-394e81f597e2\"}")]
    [InlineData("{\"source\":\"other_table\"}")]
    public async Task The_model_cannot_choose_a_sample_person_or_a_table(string arguments)
    {
        var host = new CatalogAssistantFixture();
        var result = await host.Source.ExecuteAsync(host.Request(Read, arguments), Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(0, host.Reader.Reads);
    }

    [Theory]
    [InlineData(Read)]
    [InlineData(Propose)]
    public void Durable_outputs_round_trip_through_the_exact_owner_codec(string id)
    {
        var host = new CatalogAssistantFixture();
        var json = id == Read ? Json(CatalogAssistantFixture.Metadata) : Json(CatalogAssistantFixture.Proposal);
        var result = host.Source.ParseOutput(new(id, 1), json);
        Assert.True(result.IsSuccess);
        Assert.Equal(json, JsonSerializer.Serialize(result.Value, result.Value.GetType(), JsonSerializerOptions.Web));
        Assert.True(host.Source.ParseOutput(new(id, 2), json).IsFailure);
    }

    [Theory]
    [InlineData("{\"source\":\"users\",\"source\":\"users\",\"columns\":[]}")]
    [InlineData("{\"source\":\"users\",\"columns\":[],\"sampleUsers\":[]}")]
    public void The_metadata_codec_rejects_unknown_and_duplicate_properties(string json) =>
        Assert.True(new CatalogAssistantFixture().Source.ParseOutput(new(Read, 1), json).IsFailure);
}
