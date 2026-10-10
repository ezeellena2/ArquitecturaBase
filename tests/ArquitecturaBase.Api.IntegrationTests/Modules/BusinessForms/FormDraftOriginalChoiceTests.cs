using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Http_original_choice_text_cannot_be_empty_even_when_another_language_is_complete()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var request = Payload("Original choice validation");
        var field = request["draft"]!["fields"]![0]!;
        field["type"] = "choice";
        field.AsObject().Remove("maxLength");
        field["options"] = new JsonArray(new JsonObject
        {
            ["localId"] = "first",
            ["label"] = new JsonObject { ["es"] = "", ["en"] = "Yes" },
        });
        using var rejected = await actor.PostAsJsonAsync("/api/forms/definitions", request, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.True((await rejected.ReadJsonAsync()).GetProperty("errors").TryGetProperty("draft.fields[0].options[0].label.es", out _));
        using var missingReceipt = await actor.GetAsync("/api/forms/draft-changes/" + request["requestId"]!.GetValue<string>(), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missingReceipt.StatusCode);
    }
}
