using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// Section titles over HTTP (CW15-B): saved apart from the questions, checked (a title with no question below blocks) and
// sealed with the published version.
public sealed partial class FormDraftEndpointsTests
{
    private static JsonObject SectionQuestion(string localId, string spanish, string english) => new()
    {
        ["localId"] = localId, ["type"] = "shortText", ["label"] = new JsonObject { ["es"] = spanish, ["en"] = english },
    };

    private static JsonObject SectionTitle(int start, string spanish, string english) => new()
    {
        ["start"] = start, ["title"] = new JsonObject { ["es"] = spanish, ["en"] = english },
    };

    [Fact]
    public async Task Section_titles_are_saved_checked_and_sealed_with_the_published_version()
    {
        using var writer = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        await using var host = PublicationHost();
        using var client = DeactivationClient(host, writer, "es");
        var form = Payload("Sections " + Guid.NewGuid().ToString("N")[..10]);
        var fields = form["draft"]!["fields"]!.AsArray();
        fields.Add(SectionQuestion("lote", "Lote", "Lot"));
        fields.Add(SectionQuestion("producto", "Producto", "Product"));
        form["draft"]!["sections"] = new JsonArray(SectionTitle(1, "Lote", "Lot"), SectionTitle(3, "Vacío", "Empty"));
        using var created = await client.PostAsJsonAsync("/api/forms/definitions", form, Ct);
        var saved = await CaptureContractAsync(created, HttpStatusCode.Created, "sections", "draft");
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var sections = saved.GetProperty("definition").GetProperty("draft").GetProperty("sections");
        Assert.Equal([1, 3], sections.EnumerateArray().Select(section => section.GetProperty("start").GetInt32()));
        Assert.Equal("Lot", sections[0].GetProperty("title").GetProperty("en").GetString());
        Assert.Equal(3, saved.GetProperty("definition").GetProperty("draft").GetProperty("fields").GetArrayLength());

        using var checkedDraft = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        var check = await CaptureContractAsync(checkedDraft, HttpStatusCode.OK, "sections", "publication-check");
        Assert.False(check.GetProperty("canAccept").GetBoolean());
        var issue = Assert.Single(check.GetProperty("issues").EnumerateArray());
        Assert.Equal(FormPublicationErrors.SectionEmpty.Code, issue.GetProperty("code").GetString());
        Assert.Equal("draft.sections[1]", issue.GetProperty("path").GetString());
        Assert.Equal(JsonValueKind.Null, issue.GetProperty("fieldId").ValueKind);

        var edit = EditPayload(saved);
        edit["draft"]!["sections"] = new JsonArray(SectionTitle(1, "Lote", "Lot"), SectionTitle(2, "Productos", "Products"));
        using var fixedDraft = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, fixedDraft.StatusCode);
        var publishable = await fixedDraft.ReadJsonAsync();
        var attempt = await PublicationAttemptAsync(client, id, publishable);
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var versionId = await CompletePublicationAsync(host, (await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid(), id);

        using var scope = host.Services.CreateScope();
        var version = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<BusinessFormVersion>().AsNoTracking()
            .SingleAsync(value => value.Id == versionId, Ct);
        using var schema = JsonDocument.Parse(version.SchemaJson);
        var sealedSections = schema.RootElement.GetProperty("sections");
        Assert.Equal([1, 2], sealedSections.EnumerateArray().Select(section => section.GetProperty("start").GetInt32()));
        Assert.Equal("Products", sealedSections[1].GetProperty("title").GetProperty("en").GetString());
    }

    [Fact]
    public async Task Invalid_section_titles_return_field_errors()
    {
        using var client = await AdminAsync();
        var draft = Payload("Invalid sections " + Guid.NewGuid().ToString("N"));
        draft["draft"]!["fields"]!.AsArray().Add(SectionQuestion("lote", "Lote", "Lot"));
        draft["draft"]!["sections"] = new JsonArray(SectionTitle(3, "Lejos", "Far"), new JsonObject { ["start"] = 1, ["title"] = new JsonObject { ["en"] = "Lot" } });

        using var response = await client.PostAsJsonAsync("/api/forms/definitions", draft, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("draft.sections[0].start", out _));
        Assert.True(errors.TryGetProperty("draft.sections[1].title.es", out _));
    }
}
