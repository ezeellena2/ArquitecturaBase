using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
namespace ArquitecturaBase.Application.UnitTests.Resources;
public sealed partial class ResourceParityTests
{
    [Fact]
    public void Flow_control_texts_have_the_same_keys_in_spanish_and_english() => AssertSameKeys(FlowTexts.ResourceManager);

    [Theory]
    [InlineData("es", "Abrir formulario")]
    [InlineData("en", "Open form")]
    public void Open_form_button_uses_the_recipient_language(string culture, string expected) =>
        Assert.Equal(expected, FlowTexts.Get("OpenForm", culture));
}
