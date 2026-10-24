using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// Los candidatos del asistente con un formulario que filtra por un dato de la persona, leídos de PostgreSQL.
[Collection(ApiTestGroup.Name)]
public sealed class FormAssistantRecipientDataIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Assistant_candidates_respect_the_recipient_filter_and_omit_the_form_for_a_person_without_the_data()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        await scenario.SaveDraftAsync(payload => payload["offerWhen"] = "Cuando pide un repuesto", Ct);
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);

        // Juan es de la zona Centro: el formulario está y la pregunta ofrece sólo el proveedor de su zona y del tipo fijo.
        var first = await ReadAsync(scenario, scenario.FirstId);
        var form = Assert.Single(first.Forms, value => value.DefinitionId == scenario.DefinitionId);
        Assert.Equal(versionId, form.VersionId);
        var question = Assert.Single(form.Questions);
        Assert.Equal((scenario.QuestionId, (Guid?)scenario.CatalogId), (question.FieldId, question.CatalogId));
        Assert.Equal(scenario.FirstItemId, Assert.Single(question.Choices).Id);

        // Pablo no tiene la zona: la lectura sale bien y el formulario no está.
        var missing = await ReadAsync(scenario, scenario.MissingId);
        Assert.DoesNotContain(missing.Forms, value => value.DefinitionId == scenario.DefinitionId);

        Assert.Equal(0, await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>()
            .Set<BusinessFormRecord>().CountAsync(record => record.DefinitionId == scenario.DefinitionId, Ct)));
    }

    private static Task<FormAssistantCandidatesPayload> ReadAsync(FormRecipientScenario scenario, Guid subject) => scenario.ScopeAsync(async services =>
    {
        var source = Assert.Single(services.GetServices<IAssistantCapabilitySource>(),
            item => item.Definitions.Any(definition => definition.Id == "forms.assistant-candidates"));
        var context = new AssistantChannelContext("whatsapp-assistant", scenario.ManagerId, subject, "es", "America/Argentina/Buenos_Aires", new([], []),
            [new("forms.assistant-candidates", 1), new("forms.propose-registration", 1)], [new("assistant_proposal", 1)]);
        var read = await source.ExecuteAsync(new(Guid.CreateVersion7(), context, new("forms.assistant-candidates", 1), "{}", []), Ct);
        Assert.True(read.IsSuccess);
        return Assert.IsType<FormAssistantCandidatesPayload>(read.Value.Payload);
    });
}
