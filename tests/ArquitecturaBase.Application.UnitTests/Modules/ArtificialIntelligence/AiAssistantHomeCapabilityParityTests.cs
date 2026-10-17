using System.Text.Json;
using ArquitecturaBase.Application.Channels.SystemQueries;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantHomeCapabilityParityTests
{
    [Fact]
    public void The_same_four_sources_expose_the_same_five_version_one_home_queries()
    {
        var clock = new FakeTimeProvider();
        var sources = new IAssistantCapabilitySource[] {
            Assert.IsAssignableFrom<IAssistantCapabilitySource>(new UsersSystemQuerySource(null!, null!, clock)),
            Assert.IsAssignableFrom<IAssistantCapabilitySource>(new RolesSystemQuerySource(null!, null!, null!, null!, clock)),
            Assert.IsAssignableFrom<IAssistantCapabilitySource>(new HelpSystemQuerySource(null!, null!, clock)),
            Assert.IsAssignableFrom<IAssistantCapabilitySource>(new AiBusinessSystemQuerySource(null!, null!, clock, null!)) };
        var definitions = sources.SelectMany(source => source.Definitions).OrderBy(definition => definition.Id, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]{"business.information", "roles.list", "roles.permissions", "system.help", "users.summary"}, definitions.Select(definition => definition.Id));
        Assert.All(definitions, definition => { Assert.Equal(1, definition.Version); Assert.Equal(AssistantCapabilityKind.Read, definition.Kind); Assert.Empty(definition.RequiredPermissions.Subject); });
        Assert.Contains("roleId", Assert.Single(definitions, definition => definition.Id == "roles.permissions").InputSchemaJson, StringComparison.Ordinal);
        Assert.Contains("topic", Assert.Single(definitions, definition => definition.Id == "system.help").InputSchemaJson, StringComparison.Ordinal);
    }

    [Fact]
    public void The_home_tool_payload_keeps_state_and_evidence_without_an_envelope_change()
    {
        var payload = JsonSerializer.Serialize(new AssistantSystemQueryPayload("notConnected", null), JsonSerializerOptions.Web);
        Assert.Equal("{\"state\":\"notConnected\",\"evidence\":null}", payload);
    }
}
