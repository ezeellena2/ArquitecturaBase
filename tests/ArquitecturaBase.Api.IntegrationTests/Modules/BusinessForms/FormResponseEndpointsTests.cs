using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>Respuestas (captura, planilla, detalle, bloques y PDF), catálogos, lectura de documentos, asistente y sugerencias. La ayuda común está en <see cref="FormEndpointsTestBase"/>.</summary>
[Collection(ApiTestGroup.Name)]
public sealed partial class FormResponseEndpointsTests(ApiFactory factory) : FormEndpointsTestBase
{
    private protected override ApiFactory Factory => factory;
}
