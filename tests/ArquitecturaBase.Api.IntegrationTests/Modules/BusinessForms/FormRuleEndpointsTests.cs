using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>Reglas, acciones y aprobaciones, con sus trazas y resultados. La ayuda común está en <see cref="FormEndpointsTestBase"/>.</summary>
[Collection(ApiTestGroup.Name)]
public sealed partial class FormRuleEndpointsTests(ApiFactory factory) : FormEndpointsTestBase
{
    private protected override ApiFactory Factory => factory;
}
