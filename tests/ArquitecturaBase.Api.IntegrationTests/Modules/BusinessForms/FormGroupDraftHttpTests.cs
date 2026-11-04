using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// The block of rows over HTTP (RG1-A, RG6-A): a "group" question and, right after it, its questions with "repeated".
public sealed partial class FormDraftEndpointsTests
{
    private static JsonObject RowsBlock(int minimum = 1, int maximum = 20) => new()
    {
        ["localId"] = "articulos", ["type"] = "group", ["required"] = minimum > 0,
        ["label"] = new JsonObject { ["es"] = "Artículos", ["en"] = "Items" },
        ["rows"] = new JsonObject { ["minRows"] = minimum, ["maxRows"] = maximum },
    };

    private static JsonObject RowQuestion(string localId, string type, string spanish, string english) => new()
    {
        ["localId"] = localId, ["type"] = type, ["repeated"] = true,
        ["label"] = new JsonObject { ["es"] = spanish, ["en"] = english },
    };

    [Fact]
    public async Task A_block_of_rows_is_saved_and_a_question_entering_it_gets_a_new_identity()
    {
        using var client = await AdminAsync();
        var draft = Payload("Rows " + Guid.NewGuid().ToString("N"));
        var fields = draft["draft"]!["fields"]!.AsArray();
        fields.Add(RowsBlock());
        fields.Add(RowQuestion("cantidad", "decimal", "Cantidad", "Quantity"));
        fields.Add(RowQuestion("observaciones", "longText", "Observaciones", "Notes"));
        using var created = await client.PostAsJsonAsync("/api/forms/definitions", draft, Ct);
        var saved = await CaptureContractAsync(created, HttpStatusCode.Created, "rows", "draft");
        var savedFields = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields");
        Assert.Equal(4, savedFields.GetArrayLength());
        var block = savedFields[1];
        Assert.Equal("group", block.GetProperty("type").GetString());
        Assert.Equal(1, block.GetProperty("rows").GetProperty("minRows").GetInt32());
        Assert.Equal(20, block.GetProperty("rows").GetProperty("maxRows").GetInt32());
        Assert.False(block.TryGetProperty("repeated", out _));
        Assert.False(savedFields[0].TryGetProperty("repeated", out _));
        Assert.True(savedFields[2].GetProperty("repeated").GetBoolean());
        Assert.True(savedFields[3].GetProperty("repeated").GetBoolean());
        Assert.False(savedFields[3].TryGetProperty("rows", out _));
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var oldFieldId = savedFields[0].GetProperty("fieldId").GetGuid();

        var edit = EditPayload(saved);
        var editFields = edit["draft"]!["fields"]!.AsArray();
        var entering = editFields[0]!.AsObject();
        editFields.RemoveAt(0);
        entering["replacesFieldId"] = entering["fieldId"]!.DeepClone();
        entering.Remove("fieldId");
        entering["localId"] = "entering";
        entering["repeated"] = true;
        editFields.Add(entering);
        using var moved = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var afterMove = await moved.ReadJsonAsync();
        var movedField = afterMove.GetProperty("definition").GetProperty("draft").GetProperty("fields")[3];
        Assert.True(movedField.GetProperty("repeated").GetBoolean());
        Assert.NotEqual(oldFieldId, movedField.GetProperty("fieldId").GetGuid());
        Assert.Equal(oldFieldId, afterMove.GetProperty("identityMap").GetProperty("fields")[3].GetProperty("replacesFieldId").GetGuid());

        var keep = EditPayload(afterMove);
        keep["draft"]!["fields"]![3]!.AsObject().Remove("repeated");
        using var kept = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", keep, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, kept.StatusCode);
        var problem = await kept.ReadJsonAsync();
        Assert.Equal("Forms.Draft.QuestionConfigurationInvalid", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("draft.fields[3].repeated", out _));
    }

    [Fact]
    public async Task Invalid_blocks_of_rows_return_field_errors()
    {
        using var client = await AdminAsync();
        var draft = Payload("Invalid rows " + Guid.NewGuid().ToString("N"));
        var fields = draft["draft"]!["fields"]!.AsArray();
        fields.Add(RowsBlock(minimum: 0, maximum: 0));
        fields.Add(RowQuestion("foto", "photo", "Foto", "Photo"));
        var second = RowsBlock();
        second["localId"] = "otro";
        fields.Add(second);

        using var response = await client.PostAsJsonAsync("/api/forms/definitions", draft, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("draft.fields[1].rows", out _));
        Assert.True(errors.TryGetProperty("draft.fields[2].repeated", out _));
        Assert.True(errors.TryGetProperty("draft.fields[3].type", out _));
    }
}
