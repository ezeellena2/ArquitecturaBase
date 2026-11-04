using System.Globalization;
using ArquitecturaBase.Application.Modules.Arca.Channels;
using ArquitecturaBase.Domain.Modules.Arca;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca;

public sealed class ArcaPermissionCatalogSourceTests
{
    [Fact]
    public void The_module_contributes_reading_and_managing_the_connection()
    {
        Assert.Equal(["arca.read", "arca.manage"], new ArcaPermissionCatalogSource().Codes);
    }

    [Theory]
    [InlineData("es", "Ver conexión", "Administrar conexión")]
    [InlineData("en", "View connection", "Manage connection")]
    public void Texts_follow_the_culture_of_the_request(string culture, string read, string manage)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            var source = new ArcaPermissionCatalogSource();
            Assert.Equal("ARCA", source.Area("arca"));
            Assert.Equal(read, source.Name(ArcaPermissions.Read));
            Assert.Equal(manage, source.Name(ArcaPermissions.Manage));
            Assert.All(ArcaPermissions.All, code => Assert.False(string.IsNullOrWhiteSpace(source.Description(code))));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
