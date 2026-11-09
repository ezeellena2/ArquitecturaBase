using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Channels;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogAssistantProposalServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_manual_proposal_creates_one_inactive_pending_catalog_with_its_fields()
    {
        var host = new Host();
        var result = await host.Service.CreateAsync(host.Request, Ct);
        Assert.True(result.IsSuccess);
        var catalog = Assert.Single(host.Catalogs.Catalogs);
        Assert.Equal((BusinessFormCatalogKind.Manual, false, BusinessFormCatalogProposalState.Pending, (Guid?)host.ProposalId),
            (catalog.Kind, catalog.IsActive, catalog.ProposalState, catalog.ProposalId));
        Assert.Equal(("es", BusinessFormCatalogValueType.Integer, 1L), (catalog.OriginalCulture, catalog.ValueType, catalog.Revision));
        var fields = JsonSerializer.Deserialize<BusinessFormCatalogField[]>(catalog.FieldsJson!, JsonSerializerOptions.Web)!;
        Assert.Equal(["razon_social", "telefono", "razon_social_2"], fields.Select(field => field.Key));
        Assert.All(fields, field => Assert.Equal((false, "data", true), (field.Required, field.Role, field.IsActive)));
        var evidence = FormCatalogProposalPresentation.Evidence(catalog.ProposalEvidenceJson!);
        Assert.Equal((host.Catalogs.Assistant.Actor, "app", (FormCatalogProposalPayload?)null), (evidence.RequestedByUserId, evidence.RequestedIn, evidence.OriginalProposal));
        Assert.Equal(JsonSerializer.Serialize(host.Manual, JsonSerializerOptions.Web), JsonSerializer.Serialize(evidence.Manual, JsonSerializerOptions.Web));
        Assert.Equal(host.ProposalId, Assert.Single(host.Catalogs.Locks));
        Assert.Equal(1, host.Catalogs.Uow.Commits);
        Assert.Equal(new FormCatalogAssistantProposalResponse(host.ProposalId, catalog.Id), result.Value);
        Assert.Equal([BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId], host.Lookup.LastCapabilityGroup);
    }

    [Fact]
    public async Task Two_taps_at_once_get_the_same_catalog()
    {
        var host = new Host();
        var first = await host.Service.CreateAsync(host.Request, Ct);
        var second = await host.Service.CreateAsync(host.Request, Ct);
        Assert.Equal(first.Value, second.Value);
        Assert.Single(host.Catalogs.Catalogs);
    }

    [Fact]
    public async Task What_the_proposal_already_created_comes_back_without_reading_the_proposal_again()
    {
        var host = new Host();
        Assert.True((await host.Service.CreateAsync(host.Request, Ct)).IsSuccess);
        var catalog = Assert.Single(host.Catalogs.Catalogs);
        host.Reader.CreatedCatalogs[host.ProposalId] = new(catalog.Id, catalog.ProposalEvidenceJson!);
        host.Lookup.Response = null;
        Assert.Equal(catalog.Id, (await host.Service.CreateAsync(host.Request, Ct)).Value.CatalogId);
        Assert.Equal(catalog.Id, (await host.Service.GetAsync(host.ProposalId, Ct)).Value.CatalogId);
        Assert.Equal(1, host.Lookup.Reads);
    }

    [Fact]
    public async Task A_people_proposal_from_Home_goes_through_its_own_acceptance()
    {
        var host = new Host();
        var assistant = host.Catalogs.Assistant;
        host.Lookup.Response = host.Lookup.Response! with
        {
            Context = assistant.Context with { ChannelId = "home", SubjectUserId = null, InteractionMode = AssistantInteractionMode.Unspecified },
            Proposal = new(assistant.TurnId, host.ProposalId, new(BusinessFormCatalogAssistantCapabilitySource.ProposeId, 1), CatalogAssistantFixture.Proposal)
                { ObservedReads = [assistant.Observed()] },
        };
        var result = await host.Service.CreateAsync(host.Request, Ct);
        Assert.True(result.IsSuccess);
        var catalog = Assert.Single(host.Catalogs.Catalogs);
        Assert.Equal((BusinessFormCatalogKind.Filters, BusinessFormCatalogProposalState.Pending), (catalog.Kind, catalog.ProposalState));
        Assert.Equal("app", FormCatalogProposalPresentation.Evidence(catalog.ProposalEvidenceJson!).RequestedIn);
        Assert.Equal(catalog.Id, result.Value.CatalogId);
    }

    [Theory]
    [InlineData("replaced")]
    [InlineData("name")]
    [InlineData("missing")]
    [InlineData("form")]
    [InlineData("permission")]
    public async Task A_replaced_unreadable_or_unusable_proposal_creates_nothing(string kind)
    {
        var host = new Host();
        Error expected = kind switch
        {
            "replaced" => FormCatalogErrors.ProposalReplaced,
            "name" => FormCatalogErrors.NameTaken,
            "permission" => FormDefinitionErrors.Forbidden,
            _ => FormCatalogErrors.ProposalUnavailable,
        };
        if (kind == "replaced") host.Lookup.Response = host.Lookup.Response! with { IsCurrent = false };
        if (kind == "name") host.Reader.NamesInUse.Add("TRANSPORTISTAS");
        if (kind == "missing") host.Lookup.Response = null;
        if (kind == "form") host.Lookup.Response = host.Lookup.Response! with { Proposal = host.Lookup.Response.Proposal with
            { Capability = new(BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, 1) } };
        if (kind == "permission") host.Catalogs.Assistant.Permissions.Permissions[host.Catalogs.Assistant.Actor] = [BusinessFormsPermissions.CatalogsRead];
        var result = await host.Service.CreateAsync(host.Request, Ct);
        Assert.Equal(expected.Code, result.Error.Code);
        Assert.Empty(host.Catalogs.Catalogs);
        Assert.Equal(0, host.Catalogs.Uow.Commits);
    }

    [Fact]
    public async Task Approving_a_manual_proposal_only_activates_it_and_keeps_the_decision()
    {
        var host = new Host();
        Assert.True((await host.Service.CreateAsync(host.Request, Ct)).IsSuccess);
        var catalog = Assert.Single(host.Catalogs.Catalogs);
        var approved = await host.Catalogs.Service.ApproveAsync(catalog.Id, new(catalog.Revision, null, null, null, null), Ct);
        Assert.True(approved.IsSuccess);
        Assert.Equal((true, BusinessFormCatalogProposalState.Approved, (Guid?)host.Catalogs.Assistant.Actor, 2L),
            (catalog.IsActive, catalog.ProposalState, catalog.ProposalDecidedByUserId, catalog.Revision));
        Assert.Equal(("Integer", 3), (approved.Value.Catalog.ValueType, approved.Value.Catalog.Fields!.Count));
        Assert.Equal((0, 0), (approved.Value.ItemIdentityMap.Count, approved.Value.FieldIdentityMap!.Count));
        Assert.Equal("Transportistas", approved.Value.Catalog.Proposal!.Manual!.Name);
        Assert.Equal(0, host.Catalogs.PreviewReads);
    }

    [Fact]
    public async Task A_channel_creates_its_manual_proposal_for_its_own_person_without_a_web_session()
    {
        var host = new Host();
        host.Catalogs.Current.UserId = null;
        var result = await host.Service.CreateFromChannelAsync(host.ChannelRequest(), Ct);
        Assert.True(result.IsSuccess);
        var catalog = Assert.Single(host.Catalogs.Catalogs);
        Assert.Equal((BusinessFormCatalogKind.Manual, false, BusinessFormCatalogProposalState.Pending), (catalog.Kind, catalog.IsActive, catalog.ProposalState));
        var evidence = FormCatalogProposalPresentation.Evidence(catalog.ProposalEvidenceJson!);
        Assert.Equal((host.Catalogs.Assistant.Actor, (string?)null, "whatsapp-assistant"), (evidence.RequestedByUserId, evidence.RequestedIn, evidence.Provenance.ChannelId));
        Assert.Equal(new FormCatalogAssistantProposalResponse(host.ProposalId, catalog.Id), result.Value);
        Assert.Equal(0, host.Lookup.Reads);
    }

    [Fact]
    public async Task A_channel_people_proposal_goes_through_its_own_acceptance_on_the_tap()
    {
        var host = new Host();
        var assistant = host.Catalogs.Assistant;
        host.Catalogs.Current.UserId = null;
        var request = host.ChannelRequest() with
        {
            Proposal = new(assistant.TurnId, host.ProposalId, new(BusinessFormCatalogAssistantCapabilitySource.ProposeId, 1), CatalogAssistantFixture.Proposal)
                { ObservedReads = [assistant.Observed()] },
        };
        var result = await host.Service.CreateFromChannelAsync(request, Ct);
        Assert.True(result.IsSuccess);
        var catalog = Assert.Single(host.Catalogs.Catalogs);
        Assert.Equal((BusinessFormCatalogKind.Filters, BusinessFormCatalogProposalState.Pending), (catalog.Kind, catalog.ProposalState));
        Assert.Null(FormCatalogProposalPresentation.Evidence(catalog.ProposalEvidenceJson!).RequestedIn);
        Assert.Equal(catalog.Id, result.Value.CatalogId);
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("another-subject")]
    [InlineData("permission")]
    public async Task A_channel_creates_only_from_its_own_conversation_with_the_permission_to_manage_catalogs(string reason)
    {
        var host = new Host();
        var request = host.ChannelRequest();
        if (reason == "manual") request = request with { Context = request.Context with { InteractionMode = AssistantInteractionMode.Manual } };
        if (reason == "another-subject") request = request with { Context = request.Context with { SubjectUserId = Guid.NewGuid() } };
        if (reason == "permission") host.Catalogs.Assistant.Permissions.Permissions[host.Catalogs.Assistant.Actor] = [BusinessFormsPermissions.CatalogsRead];
        Assert.Equal(FormDefinitionErrors.Forbidden, (await host.Service.CreateFromChannelAsync(request, Ct)).Error);
        Assert.Empty(host.Catalogs.Catalogs);
    }

    private sealed class Host
    {
        public FormCatalogProposalFixture Catalogs { get; } = new();
        public AuthoringAssistantFixture.AuthoringReader Reader { get; } = new();
        public StubAssistantProposalLookup Lookup { get; } = new();
        public Guid ProposalId { get; } = Guid.NewGuid();
        public FormAuthoringCatalogProposal Manual { get; } = new("es", "Transportistas", "Empresas que traen la carga", "integer", "CUIT",
            "Razón social", [new("Razón social", "text"), new("Teléfono", "text"), new("Razón-social", "text")]);
        public FormCatalogAssistantProposalService Service { get; }

        public Host()
        {
            var assistant = Catalogs.Assistant;
            Lookup.OwnerUserId = assistant.Actor;
            Lookup.Response = new AssistantProposalLookupResponse(assistant.Context with { ChannelId = "home", SubjectUserId = null,
                    InteractionMode = AssistantInteractionMode.Unspecified },
                new(assistant.TurnId, ProposalId, new(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, 1), Manual),
                new("fictitious-profile", 3, "home-system", 2, Catalogs.Time.GetUtcNow().UtcDateTime.AddMinutes(-2), "home"), true);
            Service = new(Lookup, Reader, Catalogs, Catalogs.Service, new FormCatalogAccessGuard(Catalogs.Current, assistant.Permissions), Catalogs.Uow);
        }

        public CreateFromAssistantProposalRequest Request => new(Lookup.RequestId, ProposalId);

        // WhatsApp's own conversation, where the person is both Actor and Subject, in the assistant mode.
        public StructuredAuthoringProposalRequest ChannelRequest() => new(Catalogs.Assistant.Context, Lookup.Response!.Proposal,
            Lookup.Response.Provenance with { ChannelId = "whatsapp-assistant" });
    }
}
