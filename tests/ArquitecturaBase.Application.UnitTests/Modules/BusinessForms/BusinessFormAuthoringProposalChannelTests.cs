using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// IA3-A: the channel never reads the owner's payload; the owner summarizes it and creates it with the channel's person.
public sealed class BusinessFormAuthoringProposalChannelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly FormAuthoringFormProposal Form = new("es", "Parte de lluvias", "LLU", "Enviar", [new("Milímetros", "number", true, [], null)]);
    private static readonly FormAuthoringCatalogProposal Manual = new("es", "Contratistas", "Empresas.", "text", "el CUIT", "la razón social", []);

    private static AssistantObservedProposal Observed(string capability, AssistantCapabilityPayload payload) =>
        new(Guid.NewGuid(), Guid.NewGuid(), new(capability, 1), payload);

    private static StructuredAuthoringProposalRequest Request(AssistantObservedProposal proposal) => new(new CatalogAssistantFixture().Context, proposal,
        new("fictitious-profile", 3, "capabilities-v1", 1, new DateTime(2026, 11, 4, 15, 0, 0, DateTimeKind.Utc), "whatsapp-assistant"));

    [Fact]
    public void It_summarizes_its_own_authoring_proposals_and_nothing_else()
    {
        var channel = new BusinessFormAuthoringProposalChannel(new Drafts(), new Catalogs());
        Assert.Equal(StructuredAuthoringKind.Form, channel.Summarize(Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, Form), "es")!.Kind);
        Assert.Equal("Contratistas", channel.Summarize(Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, Manual), "es")!.Title);
        var people = channel.Summarize(Observed(BusinessFormCatalogAssistantCapabilitySource.ProposeId, CatalogAssistantFixture.Proposal), "es")!;
        Assert.Equal((StructuredAuthoringKind.Catalog, CatalogAssistantFixture.Proposal.Name), (people.Kind, people.Title));
        Assert.Null(channel.Summarize(Observed("forms.missing-request.propose", Form), "es"));
        Assert.Null(channel.Summarize(Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, Manual), "es"));
    }

    [Fact]
    public async Task Creating_returns_what_was_created_and_the_app_path_to_review_it()
    {
        var drafts = new Drafts();
        var catalogs = new Catalogs();
        var channel = new BusinessFormAuthoringProposalChannel(drafts, catalogs);
        var form = await channel.CreateAsync(Request(Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, Form)), Ct);
        Assert.Equal(new StructuredAuthoringCreatedResponse(StructuredAuthoringKind.Form, drafts.Id, "Parte de lluvias", $"/formularios/{drafts.Id:D}"), form.Value);
        var catalog = await channel.CreateAsync(Request(Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, Manual)), Ct);
        Assert.Equal(new StructuredAuthoringCreatedResponse(StructuredAuthoringKind.Catalog, catalogs.Id, "Contratistas", $"/catalogos/{catalogs.Id:D}"), catalog.Value);
        catalogs.Outcome = FormCatalogErrors.NameTaken;
        Assert.Equal(FormCatalogErrors.NameTaken, (await channel.CreateAsync(Request(Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, Manual)), Ct)).Error);
    }

    [Fact]
    public async Task A_catalog_that_maps_a_spreadsheet_is_created_the_same_way()
    {
        var catalogs = new Catalogs();
        var channel = new BusinessFormAuthoringProposalChannel(new Drafts(), catalogs);
        var mapped = new FormAuthoringMappedCatalogProposal(Manual.Culture, Manual.Name, Manual.Description, Manual.ValueType, Manual.Code, Manual.Option,
            Manual.Fields, null);
        var proposal = Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, mapped) with
            { Capability = new(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, 2) };
        var created = await channel.CreateAsync(Request(proposal), Ct);
        Assert.Equal(new StructuredAuthoringCreatedResponse(StructuredAuthoringKind.Catalog, catalogs.Id, "Contratistas", $"/catalogos/{catalogs.Id:D}"), created.Value);
    }

    private sealed class Drafts : IFormDraftProposalService
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Task<Result<FormDraftAssistantProposalResponse>> CreateFromChannelAsync(StructuredAuthoringProposalRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<Result<FormDraftAssistantProposalResponse>>(new FormDraftAssistantProposalResponse(request.Proposal.Id, Id));
        public Task<Result<FormDraftAssistantProposalResponse>> CreateAsync(CreateFromAssistantProposalRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<FormDraftAssistantProposalResponse>> GetAsync(Guid proposalId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Catalogs : IFormCatalogAssistantProposalService
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Error? Outcome { get; set; }
        public Task<Result<FormCatalogAssistantProposalResponse>> CreateFromChannelAsync(StructuredAuthoringProposalRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<Result<FormCatalogAssistantProposalResponse>>(Outcome is { } error ? error : new FormCatalogAssistantProposalResponse(request.Proposal.Id, Id));
        public Task<Result<FormCatalogAssistantProposalResponse>> CreateAsync(CreateFromAssistantProposalRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<FormCatalogAssistantProposalResponse>> GetAsync(Guid proposalId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
