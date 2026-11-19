using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Domain.UnitTests.Modules.ArtificialIntelligence;

public sealed class ArtificialIntelligencePermissionsTests
{
    /// <summary>Cada configuración se hace en su pantalla, que pide su «Ver»; usar el asistente no incluye nada (2026-11-19).</summary>
    [Fact]
    public void Managing_the_connection_or_the_business_information_includes_seeing_it()
    {
        Assert.Equal(
            [
                (ArtificialIntelligencePermissions.Manage, ArtificialIntelligencePermissions.Read),
                (ArtificialIntelligencePermissions.BusinessManage, ArtificialIntelligencePermissions.BusinessRead),
            ],
            ArtificialIntelligencePermissions.Includes.SelectMany(pair => pair.Value.Select(included => (pair.Key, included))));
    }
}
