using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Infrastructure.Modules.Arca;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca;

public sealed class ArcaCompositionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void The_permission_catalog_includes_the_module_permissions_with_their_texts()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("es");
        try
        {
            var catalog = factory.Services.GetRequiredService<IPermissionCatalog>();
            Assert.All(ArcaPermissions.All, code => Assert.True(catalog.Contains(code), code));
            Assert.Equal("ARCA", catalog.Area("arca"));
            Assert.Equal("Administrar conexión", catalog.Name(ArcaPermissions.Manage));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void The_shipped_addresses_cover_both_environments_and_the_services_of_the_first_families()
    {
        var options = factory.Services.GetRequiredService<IOptions<ArcaOptions>>().Value;

        Assert.True(options.IsValid());
        foreach (var environment in new[] { options.Testing, options.Production })
        {
            Assert.Contains("wsfe", environment.Services.Keys);
            Assert.Contains("ws_sr_constancia_inscripcion", environment.Services.Keys);
        }
        Assert.NotEqual(options.Testing.Wsaa, options.Production.Wsaa);
        Assert.True(options.Production.TicketRetryWindow < options.Testing.TicketRetryWindow);
    }
}
