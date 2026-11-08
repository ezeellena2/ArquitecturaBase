using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// Publishing the block of rows over HTTP: the empty block blocks, a maximum over the channel rows warns and the published
// questions that entered the block come back in diff.movedToRows («Pasan al bloque»).
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Checking_a_block_of_rows_blocks_it_empty_warns_over_the_channel_rows_and_names_the_questions_that_enter_it()
    {
        using var writer = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        await using var host = PublicationHost();
        using var client = DeactivationClient(host, writer, "es");
        var unique = Guid.NewGuid().ToString("N")[..10];

        var empty = Payload("Rows check " + unique);
        empty["draft"]!["fields"]!.AsArray().Add(RowsBlock(maximum: 20));
        var savedEmpty = await CreateAsync(client, empty);
        var emptyId = savedEmpty.GetProperty("definition").GetProperty("id").GetGuid();
        var blockId = savedEmpty.GetProperty("definition").GetProperty("draft").GetProperty("fields")[1].GetProperty("fieldId").GetGuid();
        using var emptyCheck = await client.PostAsJsonAsync($"/api/forms/definitions/{emptyId}/publication-checks", CheckPayload(savedEmpty), Ct);
        var check = await CaptureContractAsync(emptyCheck, HttpStatusCode.OK, "rows", "publication-check");
        Assert.False(check.GetProperty("canAccept").GetBoolean());
        var issue = Assert.Single(check.GetProperty("issues").EnumerateArray());
        Assert.Equal("Forms.Publication.GroupEmpty", issue.GetProperty("code").GetString());
        Assert.Equal(blockId, issue.GetProperty("fieldId").GetGuid());
        Assert.Equal("draft.fields[1]", issue.GetProperty("path").GetString());
        var warning = Assert.Single(check.GetProperty("warnings").EnumerateArray());
        Assert.Equal("Forms.Publication.RowsOverChannelLimit", warning.GetProperty("code").GetString());
        Assert.Equal(blockId, warning.GetProperty("fieldId").GetGuid());
        Assert.Equal("draft.fields[1].rows", warning.GetProperty("path").GetString());
        Assert.Equal(10, warning.GetProperty("limit").GetInt32());
        Assert.Equal(20, warning.GetProperty("maximum").GetInt32());
        Assert.False(check.GetProperty("diff").TryGetProperty("movedToRows", out _));

        var form = Payload("Rows move " + unique);
        form["draft"]!["fields"]!.AsArray().Add(new JsonObject
        {
            ["localId"] = "cantidad", ["type"] = "decimal",
            ["label"] = new JsonObject { ["es"] = "Cantidad", ["en"] = "Quantity" },
        });
        var published = await CreateAsync(client, form);
        var formId = published.GetProperty("definition").GetProperty("id").GetGuid();
        var oldQuantity = published.GetProperty("definition").GetProperty("draft").GetProperty("fields")[1].GetProperty("fieldId").GetGuid();
        var attempt = await PublicationAttemptAsync(client, formId, published);
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{formId}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await CompletePublicationAsync(host, (await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid(), formId);

        var edit = EditPayload(published);
        var fields = edit["draft"]!["fields"]!.AsArray();
        var entering = fields[1]!.AsObject();
        fields.RemoveAt(1);
        entering["replacesFieldId"] = entering["fieldId"]!.DeepClone();
        entering.Remove("fieldId");
        entering["localId"] = "cantidad-fila";
        entering["repeated"] = true;
        fields.Add(RowsBlock(maximum: 10));
        fields.Add(entering);
        using var moved = await client.PutAsJsonAsync($"/api/forms/definitions/{formId}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var afterMove = await moved.ReadJsonAsync();
        var movedFields = afterMove.GetProperty("definition").GetProperty("draft").GetProperty("fields");
        var newBlock = movedFields[1].GetProperty("fieldId").GetGuid();
        var newQuantity = movedFields[2].GetProperty("fieldId").GetGuid();

        // With its publication open the check carries no ticket, so the body with movedToRows can be exported.
        using var movedAccepted = await client.PostAsJsonAsync($"/api/forms/definitions/{formId}/publications",
            await PublicationAttemptAsync(client, formId, afterMove), Ct);
        Assert.Equal(HttpStatusCode.Accepted, movedAccepted.StatusCode);
        using var movedCheck = await client.PostAsJsonAsync($"/api/forms/definitions/{formId}/publication-checks", CheckPayload(afterMove), Ct);
        var checkedMove = await CaptureContractAsync(movedCheck, HttpStatusCode.OK, "rows", "publication-check-moved");
        Assert.Equal("InProgress", checkedMove.GetProperty("acceptanceAvailability").GetString());
        var diff = checkedMove.GetProperty("diff");
        Assert.Equal(new[] { newQuantity }, diff.GetProperty("movedToRows").EnumerateArray().Select(value => value.GetGuid()));
        Assert.Equal(new[] { newBlock }, diff.GetProperty("added").EnumerateArray().Select(value => value.GetGuid()));
        Assert.DoesNotContain(oldQuantity, diff.GetProperty("removed").EnumerateArray().Select(value => value.GetGuid()));
        Assert.Empty(diff.GetProperty("removed").EnumerateArray());
    }

    // RG13-A: with a channel installed, a block whose minimum is over the rows WhatsApp loads blocks publication at its rows.
    // Without a channel the check already blocks with ChannelUnavailable, so the minimum is not added.
    [Fact]
    public async Task A_minimum_over_the_channel_rows_blocks_publication_at_the_rows_of_the_block()
    {
        using var writer = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        await using var host = PublicationHost();
        using var client = DeactivationClient(host, writer, "es");
        var form = Payload("Rows minimum " + Guid.NewGuid().ToString("N")[..10]);
        var fields = form["draft"]!["fields"]!.AsArray();
        fields.Add(RowsBlock(minimum: 15, maximum: 20));
        fields.Add(new JsonObject
        {
            ["localId"] = "cantidad", ["type"] = "decimal", ["repeated"] = true, ["required"] = true,
            ["label"] = new JsonObject { ["es"] = "Cantidad", ["en"] = "Quantity" },
        });
        var saved = await CreateAsync(client, form);
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var blockId = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[1].GetProperty("fieldId").GetGuid();

        using var withChannel = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        var check = await CaptureContractAsync(withChannel, HttpStatusCode.OK, "rows", "publication-check-minimum");

        Assert.False(check.GetProperty("canAccept").GetBoolean());
        var issue = Assert.Single(check.GetProperty("issues").EnumerateArray());
        Assert.Equal(FormPublicationErrors.MinRowsOverChannelLimit.Code, issue.GetProperty("code").GetString());
        Assert.Equal(blockId, issue.GetProperty("fieldId").GetGuid());
        Assert.Equal("draft.fields[1].rows", issue.GetProperty("path").GetString());
        Assert.Equal(10, issue.GetProperty("limit").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(issue.GetProperty("message").GetString()));
    }
}
