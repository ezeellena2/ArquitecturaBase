using ArquitecturaBase.Application.Modules.WhatsApp.Resources;

namespace ArquitecturaBase.Application.UnitTests.Resources;

/// <summary>
/// La parte del módulo WhatsApp de ResourceParityTests: los textos de orientación de acceso (Access.resx) en los dos idiomas. Lleva el
/// namespace de la clase del núcleo, no el de su carpeta, para ser otra parte de la misma clase y usar su AssertSameKeys.
/// </summary>
public sealed partial class ResourceParityTests
{
    [Fact]
    public void Chat_permission_texts_have_the_same_keys_in_spanish_and_english() =>
        AssertSameKeys(new System.Resources.ResourceManager(
            "ArquitecturaBase.Application.Modules.WhatsApp.Resources.Permissions",
            typeof(AccessTexts).Assembly));

    [Fact]
    public void Access_texts_have_the_same_keys_in_spanish_and_english()
    {
        AssertSameKeys(AccessTexts.ResourceManager);
    }
}
