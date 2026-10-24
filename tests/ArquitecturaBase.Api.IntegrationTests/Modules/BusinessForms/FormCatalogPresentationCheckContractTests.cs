using System.Text.Json;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// The body of POST /api/forms/catalogs/{id}/presentation-checks is closed: only the question's presentation travels.
public sealed class FormCatalogPresentationCheckContractTests
{
    [Fact]
    public void Presentation_check_contract_accepts_only_the_presentation()
    {
        const string json = """{"presentation":{"title":["code","text"],"description":["detail"]}}""";
        var request = JsonSerializer.Deserialize<FormCatalogPresentationCheckHttpRequest>(json, JsonSerializerOptions.Web);

        Assert.NotNull(request?.Presentation);
        Assert.Equal(["code", "text"], request.Presentation.Title!);
        Assert.Equal(["detail"], request.Presentation.Description!);
    }

    [Fact]
    public void Presentation_check_contract_rejects_an_unknown_property()
    {
        const string json = """{"presentation":{"title":["text"],"description":[]},"culture":"es"}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FormCatalogPresentationCheckHttpRequest>(json, JsonSerializerOptions.Web));
    }
}
