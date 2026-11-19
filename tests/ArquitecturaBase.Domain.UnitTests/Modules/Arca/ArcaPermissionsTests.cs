using ArquitecturaBase.Domain.Modules.Arca;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca;

public sealed class ArcaPermissionsTests
{
    /// <summary>La conexión se configura en su pantalla, que pide «Ver conexión»; emitir no la incluye (2026-11-19).</summary>
    [Fact]
    public void Managing_the_connection_includes_seeing_it()
    {
        Assert.Equal(
            [(ArcaPermissions.Manage, ArcaPermissions.Read)],
            ArcaPermissions.Includes.SelectMany(pair => pair.Value.Select(included => (pair.Key, included))));
    }
}
