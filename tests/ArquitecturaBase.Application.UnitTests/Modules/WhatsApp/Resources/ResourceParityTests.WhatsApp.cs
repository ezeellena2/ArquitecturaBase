using ArquitecturaBase.Application.Modules.WhatsApp.Resources;

namespace ArquitecturaBase.Application.UnitTests.Resources;

/// <summary>
/// La parte del módulo WhatsApp de ResourceParityTests: los textos del bot (Bot.resx) en los dos idiomas. Lleva el
/// namespace de la clase del núcleo, no el de su carpeta, para ser otra parte de la misma clase y usar su AssertSameKeys.
/// </summary>
public sealed partial class ResourceParityTests
{
    [Fact]
    public void Bot_texts_have_the_same_keys_in_spanish_and_english()
    {
        AssertSameKeys(BotTexts.ResourceManager);
    }
}
