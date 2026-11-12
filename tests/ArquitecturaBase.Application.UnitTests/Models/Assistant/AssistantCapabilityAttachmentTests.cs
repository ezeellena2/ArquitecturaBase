using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.UnitTests.Models.Assistant;

// Documents (task 3): the owner of a capability says which file of the turn it needs, and this one rule decides it for the catalog and
// for whoever reads the definitions.
public sealed class AssistantCapabilityAttachmentTests
{
    [Theory]
    [InlineData(AssistantCapabilityAttachment.Any, true, true, true)]
    [InlineData(AssistantCapabilityAttachment.Spreadsheet, false, true, false)]
    [InlineData(AssistantCapabilityAttachment.Document, false, false, true)]
    [InlineData(AssistantCapabilityAttachment.NoDocument, true, true, false)]
    [InlineData((AssistantCapabilityAttachment)9, false, false, false)]
    public void A_capability_is_offered_only_with_the_file_its_owner_admits(AssistantCapabilityAttachment requirement, bool none, bool spreadsheet,
        bool document)
    {
        var definition = new AssistantCapabilityDefinition("fictional.capability", 1, AssistantCapabilityKind.Proposal, "Fictional capability", "{}", "{}",
            new([], []), new(1, 1, 1, 1)) { Attachment = requirement };

        Assert.Equal([none, spreadsheet, document], new[] { AssistantAttachmentKind.None, AssistantAttachmentKind.Spreadsheet, AssistantAttachmentKind.Document }
            .Select(definition.OfferedWith));
    }

    [Fact]
    public void A_definition_that_says_nothing_admits_any_file()
    {
        var definition = new AssistantCapabilityDefinition("fictional.capability", 1, AssistantCapabilityKind.Read, "Fictional capability", "{}", "{}",
            new([], []), new(1, 1, 1, 1));

        Assert.Equal(AssistantCapabilityAttachment.Any, definition.Attachment);
    }
}
