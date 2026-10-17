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
    public async Task Clarification_uses_two_real_published_choices_and_invalidates_the_whole_question_after_disabling_one()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        using var employee = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        await using var host = PublicationHost(); using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var choices = new List<(Guid DefinitionId, Guid VersionId, JsonElement Saved)>();
        foreach (var name in new[] { "Visita para aclaración", "Entrega para aclaración" })
        {
            var input = Payload(name); input["offerWhen"] = "Cuando describe una visita o entrega";
            var saved = await CreateAsync(client, input);
            var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
            using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", await PublicationAttemptAsync(client, id, saved), Ct);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            var version = await CompletePublicationAsync(host, (await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid(), id);
            choices.Add((id, version, saved));
        }
        using var scope = host.Services.CreateScope();
        var capability = Assert.Single(scope.ServiceProvider.GetServices<IAssistantCapabilitySource>(), item => item.Definitions.Any(definition => definition.Id == "forms.clarify-registration"));
        var context = new AssistantChannelContext("whatsapp-assistant", authorId, employeeId, "es", "America/Argentina/Buenos_Aires", new([], []),
            [new("forms.assistant-candidates", 1), new("forms.clarify-registration", 1)], [new("assistant_proposal", 1)]);
        var turn = Guid.CreateVersion7();
        var read = await capability.ExecuteAsync(new(turn, context, new("forms.assistant-candidates", 1), "{}", []), Ct);
        Assert.True(read.IsSuccess);
        var observed = new AssistantObservedRead(turn, 1, new("forms.assistant-candidates", 1), read.Value.Payload);
        var arguments = JsonSerializer.Serialize(new { forms = choices.Select(choice => new { definitionId = choice.DefinitionId, versionId = choice.VersionId }).ToArray() });
        var result = await capability.ExecuteAsync(new(turn, context, new("forms.clarify-registration", 1), arguments, [observed]), Ct);
        Assert.True(result.IsSuccess);
        Assert.IsType<FormAssistantClarificationPayload>(result.Value.Payload);
        var proposal = new AssistantObservedProposal(turn, Guid.CreateVersion7(), new("forms.clarify-registration", 1), result.Value.Payload) { ObservedReads = [observed] };
        var source = scope.ServiceProvider.GetRequiredService<IStructuredFormSource>();
        var clarification = await source.ValidateClarificationAsync(new(employeeId, "es", proposal), Ct);
        Assert.True(clarification.IsSuccess);
        Assert.Equal("¿Qué formulario querés completar?", clarification.Value.Question);
        Assert.Equal(new[] { "Visita para aclaración", "Entrega para aclaración" }, clarification.Value.Choices.Select(choice => choice.Label));
        Assert.Equal(choices.Select(choice => choice.DefinitionId), clarification.Value.Choices.Select(choice => choice.DefinitionId));
        var ids = choices.Select(choice => choice.DefinitionId).ToArray();
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().CountAsync(record => ids.Contains(record.DefinitionId), Ct)));
        var disabled = EditPayload(choices[1].Saved); disabled["offerWhen"] = null;
        using var changed = await client.PutAsJsonAsync($"/api/forms/definitions/{choices[1].DefinitionId}/draft", disabled, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.True((await source.ValidateClarificationAsync(new(employeeId, "es", proposal), Ct)).IsFailure);
    }
}
