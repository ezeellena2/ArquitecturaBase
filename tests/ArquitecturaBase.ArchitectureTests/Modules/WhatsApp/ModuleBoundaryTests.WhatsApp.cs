using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests;

public sealed partial class ModuleBoundaryTests
{
    private static readonly string[] WhatsAppAssistantChannelIds = ["whatsapp-assistant", "whatsapp-manual"];

    [Fact]
    public void Private_channel_ids_are_interpreted_only_by_their_own_module()
    {
        var literals = Assemblies.SelectMany(CallSites.Literals).ToArray();
        Assert.All(WhatsAppAssistantChannelIds, id => Assert.Contains(literals,
            literal => literal.Value == id && ModuleNamespaces.ModuleOf(literal.Owner) == "WhatsApp"));

        var violations = WhatsAppChannelIdViolations(literals)
            .Select(literal => literal.Owner + ": " + literal.Value).Distinct(StringComparer.Ordinal).ToArray();

        Assert.True(violations.Length == 0,
            "A private WhatsApp channel ID is interpreted outside its owner:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void The_channel_id_boundary_detects_foreign_literals_and_preserves_the_owner_and_opaque_strings()
    {
        const string Owner = "ArquitecturaBase.Application.Modules.WhatsApp.Services.ChannelControl";
        const string ForeignOwner = "ArquitecturaBase.Application.Modules.Control.Services.ChannelControl";
        const string CoreOwner = "ArquitecturaBase.Application.Services.ChannelControl";
        foreach (var id in WhatsAppAssistantChannelIds)
        {
            Assert.Empty(WhatsAppChannelIdViolations([new CallSites.Literal(Owner, id)]));
            Assert.Single(WhatsAppChannelIdViolations([new CallSites.Literal(ForeignOwner, id)]));
            Assert.Single(WhatsAppChannelIdViolations([new CallSites.Literal(CoreOwner, id)]));
        }
        Assert.Empty(WhatsAppChannelIdViolations([
            new CallSites.Literal(ForeignOwner, "forms.assistant-candidates"),
            new CallSites.Literal(ForeignOwner, "mentions whatsapp-assistant"),
            new CallSites.Literal(ForeignOwner, "whatsapp-assistant-history")
        ]));
    }

    private static IEnumerable<CallSites.Literal> WhatsAppChannelIdViolations(IEnumerable<CallSites.Literal> literals) =>
        literals.Where(literal => WhatsAppAssistantChannelIds.Contains(literal.Value, StringComparer.Ordinal)
            && ModuleNamespaces.ModuleOf(literal.Owner) != "WhatsApp");
}
