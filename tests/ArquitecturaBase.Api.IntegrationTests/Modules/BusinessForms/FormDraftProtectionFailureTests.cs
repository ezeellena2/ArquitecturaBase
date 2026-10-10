using System.Text.Json.Nodes;
using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Protection_failure_rolls_back_a_mutated_existing_draft_without_creating_an_attempt()
    {
        using var client = await OperatorAsync(["formtemplates.read", "formtemplates.manage"]); var original = await CreateAsync(client, Payload());
        var id = original.GetProperty("definition").GetProperty("id").GetGuid(); var update = EditPayload(original); update["name"] = new JsonObject { ["es"] = "Uncommitted protection change", ["en"] = "Uncommitted protection change" };
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<IFormChangeProtector, FailingFormChangeProtector>())));
        using var failing = host.CreateClient(); failing.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        using var response = await failing.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", update, Ct); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var detail = await client.GetAsync($"/api/forms/definitions/{id}", Ct); var stored = (await detail.ReadJsonAsync()).GetProperty("definition");
        Assert.Equal(original.GetProperty("definition").GetProperty("name").GetRawText(), stored.GetProperty("name").GetRawText()); Assert.Equal(1, stored.GetProperty("revision").GetInt64());
        using var missing = await client.GetAsync("/api/forms/draft-changes/" + update["requestId"]!.GetValue<string>(), Ct); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
