using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// F31: the context sealed with an extraction is read back by later deployments. Its serialized shape is fixed here, so a change to
// AssistantChannelContext cannot make an old copy fall back, silently, to the current context.
public sealed class FormReceivedDocumentSealedContextTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Definition = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Document = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string Expected =
        "{\"channelId\":\"forms-documents\",\"actorUserId\":\"11111111-1111-1111-1111-111111111111\",\"subjectUserId\":null,\"culture\":\"es\",\"timeZoneId\":\"UTC\",\"requiredPermissions\":{\"actor\":[\"formdocuments.review\"],\"subject\":[]},\"allowedCapabilities\":[{\"id\":\"forms.documents.form\",\"version\":1},{\"id\":\"forms.documents.propose-entry\",\"version\":1}],\"allowedTerminals\":[{\"id\":\"assistant_proposal\",\"version\":1}],\"channelConversationId\":\"33333333-3333-3333-3333-333333333333\",\"focusDefinitionId\":\"22222222-2222-2222-2222-222222222222\"}";

    [Fact]
    public void The_sealed_context_keeps_its_serialized_shape()
    {
        var sealedJson = FormReceivedDocumentExtractionPolicy.Seal(FormReceivedDocumentExtractionPolicy.Context(Actor, "es", "UTC", Definition, Document));
        Assert.Equal(Expected, sealedJson);
    }

    [Fact]
    public void A_context_sealed_with_the_shape_of_this_version_is_read_back_as_it_was()
    {
        var original = FormReceivedDocumentExtractionPolicy.Context(Actor, "en", "America/Argentina/Buenos_Aires", Definition, Document, row: true);
        var read = JsonSerializer.Deserialize<AssistantChannelContext>(FormReceivedDocumentExtractionPolicy.Seal(original), JsonSerializerOptions.Web)!;
        Assert.Equal(original.ChannelId, read.ChannelId);
        Assert.Equal(original.ActorUserId, read.ActorUserId);
        Assert.Equal("en", read.Culture);
        Assert.Equal(original.AllowedCapabilities, read.AllowedCapabilities);
        Assert.Equal(original.AllowedTerminals, read.AllowedTerminals);
        Assert.Equal(Definition, read.FocusDefinitionId);
        Assert.Equal(Document, read.ChannelConversationId);
    }
}
