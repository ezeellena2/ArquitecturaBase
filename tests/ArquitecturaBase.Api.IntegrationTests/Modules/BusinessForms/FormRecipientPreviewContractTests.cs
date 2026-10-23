using System.Text.Json;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormRecipientPreviewContractTests
{
    [Fact]
    public void Recipient_preview_preserves_fixed_and_person_matches_and_presentation_through_the_shared_draft_mapping()
    {
        const string json = """{"type":"choice","source":{"catalog":"01990000-0000-7000-8000-000000000301","match":[{"fieldId":"01990000-0000-7000-8000-000000000302","value":"Privado"},{"fieldId":"01990000-0000-7000-8000-000000000303","personAttributeId":"01990000-0000-7000-8000-000000000304"}],"presentation":{"title":["code","text"],"description":["detail"]}},"sampleUserId":"01990000-0000-7000-8000-000000000305","culture":"es"}""";
        var request = JsonSerializer.Deserialize<FormRecipientPreviewHttpRequest>(json, JsonSerializerOptions.Web)!;
        var source = FormDraftRequestMapping.Map(request.Source)!;
        Assert.Equal(request.Source!.Catalog, source.Catalog);
        Assert.Equal("Privado", source.Match![0].Value);
        Assert.Null(source.Match[0].PersonAttributeId);
        Assert.Null(source.Match[1].Value);
        Assert.Equal(request.Source.Match![1].PersonAttributeId, source.Match[1].PersonAttributeId);
        Assert.Equal(["code", "text"], source.Presentation!.Title);
        Assert.Equal(["detail"], source.Presentation.Description);
        Assert.DoesNotContain("Privado", request.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"type\":\"choice\",\"source\":null,\"sampleUserId\":\"01990000-0000-7000-8000-000000000305\",\"culture\":\"es\",\"actorUserId\":null}")]
    [InlineData("{\"type\":\"choice\",\"source\":{\"catalog\":null,\"match\":[{\"fieldId\":\"01990000-0000-7000-8000-000000000302\",\"value\":\"x\",\"personUserId\":null}]},\"sampleUserId\":\"01990000-0000-7000-8000-000000000305\",\"culture\":\"es\"}")]
    public void Recipient_preview_rejects_client_actor_and_unknown_nested_match_properties(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FormRecipientPreviewHttpRequest>(json, JsonSerializerOptions.Web));
}
