using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// IA3-A with PostgreSQL: a channel's worker has no web session; the owner creates with the channel's person, once per proposal.
[Collection(ApiTestGroup.Name)]
public sealed class FormAssistantProposalChannelTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AssistantChannelContext Context(Guid person) => new("whatsapp-assistant", person, person, "es", "America/Argentina/Buenos_Aires",
        new([], []), [new(BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, 1)], [new("assistant_proposal", 1)])
        { InteractionMode = AssistantInteractionMode.Assistant };

    [Fact]
    public async Task A_channel_creates_the_draft_and_the_catalog_once_for_its_own_person_without_a_web_session()
    {
        await using var scenario = await FormAssistantProposalScenario.CreateAsync(factory, Ct);
        var provenance = new AssistantOperationProvenance("fictitious-profile", 1, "capabilities-v1", 1, factory.Clock.GetUtcNow().UtcDateTime, "whatsapp-assistant");
        var form = new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), new(BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, 1),
            new FormAuthoringFormProposal("es", "Canal " + scenario.Suffix, scenario.Prefix, "Enviar", [new("Fecha", "date", true, [], null), new("Milímetros", "number", true, [], null)]));
        var created = await CreateAsync(new(Guid.NewGuid(), Context(scenario.AuthorId), form, provenance));
        Assert.True(created.IsSuccess);
        Assert.Equal((StructuredAuthoringKind.Form, "Canal " + scenario.Suffix, $"/formularios/{created.Value.Id:D}"), (created.Value.Kind, created.Value.Name, created.Value.AppPath));
        var stored = await scenario.DefinitionAsync(created.Value.Id, Ct);
        Assert.Equal(form.Id, stored.ProposalId);
        using (var evidence = JsonDocument.Parse(stored.ProposalEvidenceJson!))
        {
            Assert.Equal(scenario.AuthorId, evidence.RootElement.GetProperty("requestedByUserId").GetGuid());
            Assert.False(evidence.RootElement.TryGetProperty("requestedIn", out _));
        }
        Assert.Equal(created.Value.Id, (await CreateAsync(new(Guid.NewGuid(), Context(scenario.AuthorId), form, provenance))).Value.Id);
        Assert.Equal(1, await scenario.CountDefinitionsAsync(form.Id, Ct));

        var denied = form with { Id = Guid.NewGuid() };
        Assert.Equal(FormDefinitionErrors.Forbidden, (await CreateAsync(new(Guid.NewGuid(), Context(scenario.ReaderId), denied, provenance))).Error);
        Assert.Equal(0, await scenario.CountDefinitionsAsync(denied.Id, Ct));

        var manual = new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), new(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, 1),
            new FormAuthoringCatalogProposal("es", "Contratistas " + scenario.Suffix, "Empresas ficticias", "text", "CUIT", "Razón social", [new("Rubro", "text")]));
        var catalog = await CreateAsync(new(Guid.NewGuid(), Context(scenario.AuthorId), manual, provenance));
        Assert.True(catalog.IsSuccess);
        Assert.Equal($"/catalogos/{catalog.Value.Id:D}", catalog.Value.AppPath);
        var row = await factory.ExecuteScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormCatalog>().AsNoTracking()
            .SingleAsync(item => item.Id == catalog.Value.Id, Ct));
        Assert.Equal((manual.Id, false, BusinessFormCatalogProposalState.Pending), (row.ProposalId, row.IsActive, row.ProposalState));
    }

    // The base host's scope, like a channel worker's, has no current user.
    private Task<ArquitecturaBase.Domain.Results.Result<StructuredAuthoringCreatedResponse>> CreateAsync(StructuredAuthoringProposalRequest request) =>
        factory.ExecuteScopeAsync(services => services.GetRequiredService<IStructuredAuthoringProposalChannel>().CreateAsync(request, Ct));
}
