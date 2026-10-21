using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("", HttpStatusCode.Forbidden)]
    [InlineData("formtemplates.read", HttpStatusCode.Forbidden)]
    [InlineData("formtemplates.manage", HttpStatusCode.Forbidden)]
    [InlineData("formtemplates.read|formtemplates.publish", HttpStatusCode.Forbidden)]
    [InlineData("formtemplates.read|formtemplates.manage", HttpStatusCode.NotFound)]
    public async Task Deleting_requires_read_and_manage_without_requiring_publish(string permissions, HttpStatusCode expected)
    {
        using var actor = await OperatorAsync(permissions.Split('|', StringSplitOptions.RemoveEmptyEntries));
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/forms/definitions/{Guid.NewGuid()}")
        { Content = JsonContent.Create(new { expectedRevision = 1 }) };
        using var response = await actor.SendAsync(request, Ct);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_rejects_an_anonymous_request_before_reading_the_definition()
    {
        using var anonymous = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/forms/definitions/{Guid.NewGuid()}")
        { Content = JsonContent.Create(new { expectedRevision = 1 }) };
        using var response = await anonymous.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"expectedRevision\":0}")]
    [InlineData("{\"expectedRevision\":9007199254740992}")]
    [InlineData("{\"expectedRevision\":1,\"actorId\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{\"expectedRevision\":1,\"deletedAtUtc\":\"2026-10-20T12:00:00Z\"}")]
    public async Task Deleting_only_accepts_a_valid_revision_and_never_client_attribution(string body)
    {
        using var actor = await OperatorAsync(["formtemplates.read", "formtemplates.manage"]);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/forms/definitions/{Guid.NewGuid()}")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        using var response = await actor.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}