using System.Net;
using System.Net.Http.Json;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("", HttpStatusCode.Forbidden)]
    [InlineData("formtemplates.read", HttpStatusCode.Forbidden)]
    [InlineData("formtemplates.publish", HttpStatusCode.Forbidden)]
    [InlineData("formtemplates.read|formtemplates.manage", HttpStatusCode.Forbidden)]
    [InlineData("formtemplates.read|formtemplates.publish", HttpStatusCode.NotFound)]
    public async Task Deactivation_routes_require_read_and_publish_without_requiring_manage(string grantedPermissions, HttpStatusCode expected)
    {
        using var actor = await OperatorAsync(grantedPermissions.Split('|', StringSplitOptions.RemoveEmptyEntries));
        var path = $"/api/forms/definitions/{Guid.NewGuid()}/deactivation";
        using var preview = await actor.GetAsync(path, Ct);
        using var deactivate = await actor.PostAsJsonAsync(path, new { expectedRevision = 1 }, Ct);
        Assert.Equal(expected, preview.StatusCode);
        Assert.Equal(expected, deactivate.StatusCode);
    }

    [Fact]
    public async Task Deactivation_routes_reject_anonymous_requests_before_reading_or_changing_a_definition()
    {
        using var anonymous = factory.CreateClient();
        var path = $"/api/forms/definitions/{Guid.NewGuid()}/deactivation";
        using var preview = await anonymous.GetAsync(path, Ct);
        using var deactivate = await anonymous.PostAsJsonAsync(path, new { expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, preview.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, deactivate.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"expectedRevision\":0}")]
    [InlineData("{\"expectedRevision\":9007199254740992}")]
    [InlineData("{\"expectedRevision\":1,\"actorId\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{\"expectedRevision\":1,\"deactivatedAtUtc\":\"2026-10-20T12:00:00Z\"}")]
    public async Task Deactivation_only_accepts_a_valid_revision_and_never_client_attribution(string body)
    {
        using var actor = await OperatorAsync(["formtemplates.read", "formtemplates.publish"]);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var result = await actor.PostAsync($"/api/forms/definitions/{Guid.NewGuid()}/deactivation", content, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }
}
