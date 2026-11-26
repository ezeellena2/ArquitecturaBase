using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>Publicación, vista previa y listado. La ayuda común está en <see cref="FormEndpointsTestBase"/>.</summary>
[Collection(ApiTestGroup.Name)]
public sealed partial class FormPublicationEndpointsTests(ApiFactory factory) : FormEndpointsTestBase
{
    private protected override ApiFactory Factory => factory;
}
