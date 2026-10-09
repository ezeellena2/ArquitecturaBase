using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;
namespace ArquitecturaBase.Application.UnitTests.Resources;
public sealed partial class ResourceParityTests
{
    [Fact]
    public void Ai_permission_texts_have_the_same_keys_in_both_languages() => AssertSameKeys(AiPermissionCatalogSource.ResourceManager);
    [Fact]
    public void Ai_assistant_texts_have_the_same_keys_in_both_languages() =>
        AssertSameKeys(ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources.AssistantTexts.Manager);
    [Fact]
    public void Ai_business_validation_texts_have_the_same_keys_in_both_languages() =>
        AssertSameKeys(ArquitecturaBase.Application.Modules.ArtificialIntelligence.Validation.AiBusinessInformationValidationMessages.ResourceManager);
}
