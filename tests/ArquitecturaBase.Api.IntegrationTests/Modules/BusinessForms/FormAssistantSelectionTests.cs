using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Assistant_discovery_uses_current_instructions_and_the_published_schema_without_capturing_a_response()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        using var employee = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        await using var host = PublicationHost();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var input = Payload("Published assistant form"); input["offerWhen"] = "Cuando cuenta una visita";
        var saved = await CreateAsync(client, input);
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", await PublicationAttemptAsync(client, id, saved), Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var version = await CompletePublicationAsync(host, (await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid(), id);
        var edit = EditPayload(saved);
        edit["offerWhen"] = "Cuando describe una visita realizada";
        edit["name"]!["es"] = "Draft name";
        edit["draft"]!["fields"]![0]!["label"]!["es"] = "Draft question";
        using var updated = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var current = await updated.ReadJsonAsync();
        var context = new AssistantChannelContext("whatsapp-assistant", authorId, employeeId, "es", "America/Argentina/Buenos_Aires", new([], []),
            [new("forms.assistant-candidates", 1), new("forms.propose-registration", 1)], [new("assistant_proposal", 1)]);
        using var scope = host.Services.CreateScope();
        var source = Assert.Single(scope.ServiceProvider.GetServices<IAssistantCapabilitySource>(), item => item.Definitions.Any(definition => definition.Id == "forms.assistant-candidates"));
        var turn = Guid.CreateVersion7();
        var read = await source.ExecuteAsync(new(turn, context, new("forms.assistant-candidates", 1), "{}", []), Ct);
        Assert.True(read.IsSuccess);
        var observed = Assert.IsType<FormAssistantCandidatesPayload>(read.Value.Payload);
        var form = Assert.Single(observed.Forms, form => form.DefinitionId == id);
        Assert.Equal(version, form.VersionId);
        Assert.Equal("Published assistant form", form.Name);
        Assert.Equal("Cuando describe una visita realizada", form.OfferWhen);
        Assert.Equal("Nombre", Assert.Single(form.Questions).Label);
        Assert.Equal(current.GetProperty("definition").GetProperty("revision").GetInt64(), form.DefinitionRevision);
        var arguments = JsonSerializer.Serialize(new { definitionId = id, versionId = version,
            values = new[] { new { fieldId = form.Questions[0].FieldId, type = "shortText", value = "Parque" } }, reason = "Describió una visita." });
        var observedRead = new AssistantObservedRead(turn, 1, new("forms.assistant-candidates", 1), observed);
        var proposed = await source.ExecuteAsync(new(turn, context, new("forms.propose-registration", 1), arguments, [observedRead]), Ct);
        Assert.True(proposed.IsSuccess);
        var proposal = new AssistantObservedProposal(turn, Guid.CreateVersion7(), new("forms.propose-registration", 1), proposed.Value.Payload) { ObservedReads = [observedRead] };
        var prepared = await scope.ServiceProvider.GetRequiredService<IStructuredFormSource>().ValidatePrefillAsync(new(employeeId, "es", proposal), Ct);
        Assert.True(prepared.IsSuccess);
        Assert.Equal("Parque", Assert.Single(prepared.Value.Fields).Value);
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().CountAsync(record => record.DefinitionId == id, Ct)));
        var disabled = EditPayload(current); disabled["offerWhen"] = null;
        using var cleared = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", disabled, Ct);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var after = await source.ExecuteAsync(new(turn, context, new("forms.assistant-candidates", 1), "{}", []), Ct);
        Assert.True(after.IsSuccess);
        Assert.DoesNotContain(Assert.IsType<FormAssistantCandidatesPayload>(after.Value.Payload).Forms, form => form.DefinitionId == id);
        Assert.True((await scope.ServiceProvider.GetRequiredService<IStructuredFormSource>().ValidatePrefillAsync(new(employeeId, "es", proposal), Ct)).IsFailure);
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().CountAsync(item => item.DefinitionId == id, Ct)));
    }
}
