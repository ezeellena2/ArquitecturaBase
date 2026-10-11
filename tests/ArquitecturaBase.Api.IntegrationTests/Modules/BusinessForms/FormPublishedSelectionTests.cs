using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Accepted_session_can_resolve_its_published_version_after_a_newer_version_becomes_active()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        using var employee = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        await using var host = PublicationHost();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));

        var saved = await CreateAsync(client, Payload("Historical accepted form"));
        var definitionId = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var firstAttempt = await PublicationAttemptAsync(client, definitionId, saved);
        using var firstAccepted = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", firstAttempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, firstAccepted.StatusCode);
        var firstOperation = (await firstAccepted.ReadJsonAsync()).GetProperty("operationId").GetGuid();
        var firstVersionId = await CompletePublicationAsync(host, firstOperation, definitionId);

        var edit = EditPayload(saved);
        edit["name"]!["es"] = "Formulario histórico editado";
        edit["name"]!["en"] = "Edited historical form";
        using var changed = await client.PutAsJsonAsync($"/api/forms/definitions/{definitionId}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var edited = await changed.ReadJsonAsync();
        var secondAttempt = await PublicationAttemptAsync(client, definitionId, edited);
        using var secondAccepted = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", secondAttempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, secondAccepted.StatusCode);
        var secondOperation = (await secondAccepted.ReadJsonAsync()).GetProperty("operationId").GetGuid();
        var secondVersionId = await CompletePublicationAsync(host, secondOperation, definitionId);

        using var scope = host.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IStructuredFormSource>();
        Assert.Null(await source.FindAsync(employeeId, "es", definitionId, firstVersionId, Ct));
        var active = await source.FindAsync(employeeId, "es", definitionId, secondVersionId, Ct);
        Assert.NotNull(active);
        var accepted = await source.FindAcceptedSessionAsync(employeeId, "es", definitionId, firstVersionId, Ct);
        Assert.NotNull(accepted);
        Assert.Equal("Historical accepted form", accepted.Name["es"]);
        Assert.Null(await source.FindAcceptedSessionAsync(Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single()),
            "es", definitionId, firstVersionId, Ct));
    }

    private static async Task<Guid> CompletePublicationAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host,
        Guid operationId, Guid definitionId)
    {
        using var scope = host.Services.CreateScope();
        var dispatch = scope.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>();
        var work = (await dispatch.ReserveAsync(operationId, Ct)).Value!;
        var published = new StructuredFormPublicationResponse(work.OperationId, definitionId, work.VersionId,
            work.Manifest.PublicationFingerprint, "Published", "Verify", [], null, false);
        Assert.True((await dispatch.CompleteAsync(work, [new("test", published)], Ct)).IsSuccess);
        return work.VersionId;
    }

    [Fact]
    public async Task Published_choices_search_sort_and_page_the_sealed_recipient_language_and_require_employee_permission()
    {
        var search = "SEL" + Guid.NewGuid().ToString("N")[..10];
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        using var employee = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        await using var host = PublicationHost(); using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        Guid firstDefinition = Guid.Empty; Guid firstVersion = Guid.Empty;
        foreach (var (spanish, english) in new[] { ("Zeta", "Alpha"), ("Alfa", "Zulu") })
        {
            var payload = Payload(search + spanish); payload["name"]!["en"] = search + english;
            var saved = await CreateAsync(client, payload); var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
            var attempt = await PublicationAttemptAsync(client, id, saved);
            using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode); var receipt = await accepted.ReadJsonAsync();
            using var scope = host.Services.CreateScope(); var dispatch = scope.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>();
            var work = (await dispatch.ReserveAsync(receipt.GetProperty("operationId").GetGuid(), Ct)).Value!;
            var published = new StructuredFormPublicationResponse(work.OperationId, id, work.VersionId, work.Manifest.PublicationFingerprint, "Published", "Verify", [], null, false);
            Assert.True((await dispatch.CompleteAsync(work, [new("test", published)], Ct)).IsSuccess);
            if (spanish == "Zeta") { firstDefinition = id; firstVersion = work.VersionId; }
            var edit = EditPayload(saved); edit["name"]!["es"] = "Changed draft"; edit["name"]!["en"] = "Changed draft";
            using var changed = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct);
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        }
        await CreateAsync(client, Payload(search + "Unpublished"));
        using var selectionScope = host.Services.CreateScope();
        var source = selectionScope.ServiceProvider.GetRequiredService<IStructuredFormSource>();
        var request = new ListStructuredFormsRequest { Search = search, PageSize = 1 };
        var englishFirst = await source.ListAsync(employeeId, "en", request, Ct);
        Assert.Equal(2, englishFirst.TotalCount); var englishRow = Assert.Single(englishFirst.Items);
        Assert.Equal(firstDefinition, englishRow.DefinitionId); Assert.Equal(firstVersion, englishRow.VersionId);
        Assert.Equal(search + "Alpha", englishRow.Name["en"]); Assert.Equal("Submit", englishRow.SubmitLabel["en"]);
        Assert.Equal(1, englishRow.VersionNumber);
        var englishSecond = await source.ListAsync(employeeId, "en", request with { Page = 2 }, Ct);
        Assert.Equal(2, englishSecond.TotalCount); Assert.Equal(search + "Zulu", Assert.Single(englishSecond.Items).Name["en"]);
        var spanishPage = await source.ListAsync(employeeId, "es", request, Ct);
        Assert.Equal(2, spanishPage.TotalCount); Assert.Equal(search + "Alfa", Assert.Single(spanishPage.Items).Name["es"]);
        Assert.Empty((await source.ListAsync(authorId, "en", request, Ct)).Items);
        Assert.Empty((await source.ListAsync(employeeId, "en", request with { Search = "Changed draft" }, Ct)).Items);

    }
}
