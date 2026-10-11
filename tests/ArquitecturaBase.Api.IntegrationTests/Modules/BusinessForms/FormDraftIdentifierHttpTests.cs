using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData(false, "draft.fields[0].localId")]
    [InlineData(true, "draft.fields[0].options[0].localId")]
    public async Task Http_local_identifiers_reject_a_trailing_line_break_without_committing(bool choice, string path)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var request = ChoiceTextPayload();
        var field = request["draft"]!["fields"]![0]!;
        if (choice) field["options"]![0]!["localId"] = "option\n";
        else field["localId"] = "question\n";

        using var response = await actor.PostAsJsonAsync("/api/forms/definitions", request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty(path, out _));
        var requestId = Guid.Parse(request["requestId"]!.GetValue<string>());
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDraftChange>()
            .CountAsync(change => change.RequestId == requestId, Ct)));
    }
}
