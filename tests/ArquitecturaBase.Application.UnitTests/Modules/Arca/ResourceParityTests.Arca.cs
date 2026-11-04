using ArquitecturaBase.Application.Modules.Arca.Channels;
namespace ArquitecturaBase.Application.UnitTests.Resources;
public sealed partial class ResourceParityTests
{
    [Fact]
    public void Arca_permission_texts_have_the_same_keys_in_both_languages() => AssertSameKeys(ArcaPermissionCatalogSource.ResourceManager);
}
