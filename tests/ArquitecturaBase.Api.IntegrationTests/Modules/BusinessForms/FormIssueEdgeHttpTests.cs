using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// «¿La emito?» at the HTTP edge: confirming needs a session and «Ver respuestas» or «Completar formularios» (whoever loaded the
// response decides, also with only the second), and the issue preview of a load needs «Completar formularios».
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Confirming_an_issuance_and_previewing_one_need_a_session_and_their_permissions()
    {
        using var anonymous = factory.CreateClient();
        using var outsider = await OperatorAsync([BusinessFormsPermissions.RulesManage]);
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var creator = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        var confirmation = $"/api/forms/records/{Guid.NewGuid()}/actions/{Guid.NewGuid()}/confirmation";
        var preview = $"/api/forms/entries/{Guid.NewGuid()}/issue-preview";

        foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (outsider, HttpStatusCode.Forbidden) })
        {
            using var confirmed = await client.PostAsJsonAsync(confirmation, new { issue = true }, Ct);
            Assert.Equal(status, confirmed.StatusCode);
        }
        foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (reader, HttpStatusCode.Forbidden) })
        {
            using var previewed = await client.PostAsJsonAsync(preview, new { versionId = Guid.NewGuid(), answers = Array.Empty<object>() }, Ct);
            Assert.Equal(status, previewed.StatusCode);
        }

        // «Completar formularios» alone gets past the edge: the run does not exist for it.
        using var unknown = await creator.PostAsJsonAsync(confirmation, new { issue = true }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
