using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
namespace ArquitecturaBase.Application.UnitTests.Resources;
public sealed partial class ResourceParityTests
{
    [Fact]
    public void Flow_control_texts_have_the_same_keys_in_spanish_and_english() => AssertSameKeys(FlowTexts.ResourceManager);
}