using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
namespace ArquitecturaBase.Application.UnitTests.Resources;
public sealed partial class ResourceParityTests
{
    [Theory]
    [InlineData("es", "Abrir formulario")]
    [InlineData("en", "Open form")]
    public void Open_form_button_uses_the_recipient_language(string culture, string expected) =>
        Assert.Equal(expected, FlowTexts.Get("OpenForm", culture));
}
