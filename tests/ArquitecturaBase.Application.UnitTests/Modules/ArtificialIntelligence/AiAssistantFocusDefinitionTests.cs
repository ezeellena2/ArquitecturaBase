using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// Task 7: the form the server fixes for an extraction of the inbox (FocusDefinitionId) is sealed with the context, compared like the origin
// of a capture when an operation is read or canceled, and left out of the JSON when there is none, so prior contexts keep their bytes.
public sealed class AiAssistantFocusDefinitionTests
{
    private static readonly AssistantChannelContext Context = new("forms-documents", Guid.NewGuid(), null, "es", "UTC", new(["formdocuments.review"], []),
        [new("forms.documents.form", 1), new("forms.documents.propose-entry", 1)], [new("assistant_proposal", 1)]);

    [Fact]
    public void An_operation_is_read_only_with_the_same_fixed_form_it_was_sealed_with()
    {
        var focus = Guid.NewGuid();
        var sealedContext = Context with { FocusDefinitionId = focus };
        Assert.True(AiAssistantChannelPolicy.SameScope(sealedContext, sealedContext));
        Assert.False(AiAssistantChannelPolicy.SameScope(Context with { FocusDefinitionId = Guid.NewGuid() }, sealedContext));
        Assert.False(AiAssistantChannelPolicy.SameScope(Context, sealedContext));
        Assert.False(AiAssistantChannelPolicy.SameScope(sealedContext, Context));
    }

    [Fact]
    public void The_fixed_form_is_sealed_with_the_context_and_omitted_when_there_is_none()
    {
        var focus = Guid.NewGuid();
        var json = JsonSerializer.Serialize(Context with { FocusDefinitionId = focus }, JsonSerializerOptions.Web);
        Assert.Equal(focus, JsonSerializer.Deserialize<AssistantChannelContext>(json, JsonSerializerOptions.Web)!.FocusDefinitionId);
        Assert.DoesNotContain("focusDefinitionId", JsonSerializer.Serialize(Context, JsonSerializerOptions.Web), StringComparison.Ordinal);
    }
}
