using ArquitecturaBase.Application.Modules.Arca.Services.Connection;
namespace ArquitecturaBase.Application.UnitTests.Resources;
public sealed partial class ResourceParityTests
{
    [Fact]
    public void Arca_robot_texts_have_the_same_keys_in_both_languages() => AssertSameKeys(ArcaRobotAlertPolicy.ResourceManager);
}
