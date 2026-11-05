using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Channels;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormDraftProposalServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid CatalogId = Guid.NewGuid();

    [Fact]
    public async Task Creating_saves_the_draft_of_the_current_proposal_once_with_where_it_came_from()
    {
        var host = new Host();
        var result = await host.Service.CreateAsync(host.Request, Ct);
        Assert.True(result.IsSuccess);
        var saved = Assert.Single(host.Drafts.Created);
        Assert.Equal(host.ProposalId, saved.RequestId);
        Assert.Equal(("es", "Control de carga", "CCC"), (saved.Draft.Culture, saved.Name["es"], saved.Prefix));
        Assert.Equal(["shortText", "choice"], saved.Draft.Fields.Select(field => field.Type));
        Assert.Equal(CatalogId, saved.Draft.Fields[1].Source!.Catalog);
        Assert.Equal(host.ProposalId, saved.Proposal!.ProposalId);
        var evidence = JsonSerializer.Deserialize<FormDraftProposalEvidence>(saved.Proposal.EvidenceJson, JsonSerializerOptions.Web)!;
        Assert.Equal((host.Authoring.Actor, "app", host.Lookup.Response!.Provenance.OccurredAtUtc), (evidence.RequestedByUserId, evidence.RequestedIn, evidence.RequestedAtUtc));
        Assert.Equal(JsonSerializer.Serialize(host.Proposal, JsonSerializerOptions.Web), JsonSerializer.Serialize(evidence.OriginalProposal, JsonSerializerOptions.Web));
        Assert.Equal(new FormDraftAssistantProposalResponse(host.ProposalId, host.Drafts.DefinitionId), result.Value);
    }

    [Fact]
    public async Task What_the_proposal_already_created_comes_back_without_reading_the_proposal_again()
    {
        var host = new Host();
        var definitionId = Guid.NewGuid();
        host.Authoring.Reader.CreatedDefinitions[host.ProposalId] = new(definitionId, host.Evidence(host.Authoring.Actor));
        var created = await host.Service.CreateAsync(host.Request, Ct);
        var status = await host.Service.GetAsync(host.ProposalId, Ct);
        Assert.Equal(definitionId, created.Value.DefinitionId);
        Assert.Equal(definitionId, status.Value.DefinitionId);
        Assert.Equal(0, host.Lookup.Reads);
        Assert.Empty(host.Drafts.Created);
    }

    [Fact]
    public async Task A_draft_another_person_created_does_not_count_as_this_persons()
    {
        var host = new Host();
        host.Authoring.Reader.CreatedDefinitions[host.ProposalId] = new(Guid.NewGuid(), host.Evidence(Guid.NewGuid()));
        var status = await host.Service.GetAsync(host.ProposalId, Ct);
        Assert.Equal(new FormDraftAssistantProposalResponse(host.ProposalId, null), status.Value);
    }

    [Fact]
    public async Task A_replaced_proposal_creates_nothing()
    {
        var host = new Host();
        host.Lookup.Response = host.Lookup.Response! with { IsCurrent = false };
        var result = await host.Service.CreateAsync(host.Request, Ct);
        Assert.Equal(FormDefinitionErrors.ProposalReplaced, result.Error);
        Assert.Empty(host.Drafts.Created);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("catalog")]
    public async Task A_proposal_that_cannot_be_read_or_is_not_a_form_is_unavailable(string kind)
    {
        var host = new Host();
        if (kind == "missing") host.Lookup.Response = null;
        else host.Lookup.Response = host.Lookup.Response! with { Proposal = host.Lookup.Response.Proposal with
            { Capability = new(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, 1) } };
        var result = await host.Service.CreateAsync(host.Request, Ct);
        Assert.Equal(FormDefinitionErrors.ProposalUnavailable, result.Error);
        Assert.Empty(host.Drafts.Created);
    }

    [Fact]
    public async Task A_name_in_use_creates_nothing()
    {
        var host = new Host();
        host.Authoring.Reader.NamesInUse.Add("control de carga");
        var result = await host.Service.CreateAsync(host.Request, Ct);
        Assert.Equal(FormDefinitionErrors.NameTaken.Code, result.Error.Code);
        Assert.Contains("name", Assert.IsType<ValidationError>(result.Error).Errors.Keys);
        Assert.Empty(host.Drafts.Created);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("unreadable")]
    public async Task A_catalog_that_is_no_longer_usable_creates_nothing(string kind)
    {
        var host = new Host();
        if (kind == "inactive") host.Authoring.Reader.Catalogs[0] = AuthoringAssistantFixture.Catalog(CatalogId, "Campos", active: false);
        else host.Authoring.Permissions.Permissions[host.Authoring.Actor] = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage];
        var result = await host.Service.CreateAsync(host.Request, Ct);
        Assert.Equal(FormDefinitionErrors.CatalogUnavailable.Code, result.Error.Code);
        Assert.Contains("questions[1].catalogId", Assert.IsType<ValidationError>(result.Error).Errors.Keys);
        Assert.Empty(host.Drafts.Created);
    }

    [Fact]
    public async Task Creating_needs_to_manage_forms_and_the_status_to_read_them()
    {
        var host = new Host();
        host.Authoring.Permissions.Permissions[host.Authoring.Actor] = [BusinessFormsPermissions.Read];
        Assert.Equal(FormDefinitionErrors.Forbidden, (await host.Service.CreateAsync(host.Request, Ct)).Error);
        Assert.True((await host.Service.GetAsync(host.ProposalId, Ct)).IsSuccess);
        host.Authoring.Permissions.Permissions[host.Authoring.Actor] = [];
        Assert.Equal(FormDefinitionErrors.Forbidden, (await host.Service.GetAsync(host.ProposalId, Ct)).Error);
        Assert.Equal(0, host.Lookup.Reads);
    }

    [Fact]
    public async Task The_draft_save_decides_the_rest_and_its_refusal_comes_back_as_it_is()
    {
        var host = new Host();
        host.Drafts.Outcome = FormDefinitionErrors.PrefixTaken;
        var result = await host.Service.CreateAsync(host.Request, Ct);
        Assert.Equal(FormDefinitionErrors.PrefixTaken, result.Error);
    }

    [Fact]
    public async Task A_channel_creates_the_draft_of_its_proposal_for_its_own_person_without_a_web_session()
    {
        var host = new Host();
        host.Current.UserId = null;
        var result = await host.Service.CreateFromChannelAsync(host.ChannelRequest(), Ct);
        Assert.True(result.IsSuccess);
        var saved = Assert.Single(host.Drafts.Created);
        Assert.Equal((host.ProposalId, host.Authoring.Actor), (saved.RequestId, saved.ActorUserId));
        var evidence = JsonSerializer.Deserialize<FormDraftProposalEvidence>(saved.Proposal!.EvidenceJson, JsonSerializerOptions.Web)!;
        Assert.Equal((host.Authoring.Actor, (string?)null, "whatsapp-assistant"), (evidence.RequestedByUserId, evidence.RequestedIn, evidence.Provenance.ChannelId));
        Assert.Equal(new FormDraftAssistantProposalResponse(host.ProposalId, host.Drafts.DefinitionId), result.Value);
        Assert.Equal(0, host.Lookup.Reads);
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("another-subject")]
    [InlineData("permission")]
    [InlineData("other-capability")]
    public async Task A_channel_creates_only_from_its_own_conversation_with_the_permission_to_manage_forms(string reason)
    {
        var host = new Host();
        var request = host.ChannelRequest();
        request = reason switch
        {
            "manual" => request with { Context = request.Context with { InteractionMode = AssistantInteractionMode.Manual } },
            "another-subject" => request with { Context = request.Context with { SubjectUserId = Guid.NewGuid() } },
            "other-capability" => request with { Proposal = request.Proposal with { Capability = new(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, 1) } },
            _ => request,
        };
        if (reason == "permission") host.Authoring.Permissions.Permissions[host.Authoring.Actor] = [BusinessFormsPermissions.Read];
        var result = await host.Service.CreateFromChannelAsync(request, Ct);
        Assert.Equal(reason == "other-capability" ? FormDefinitionErrors.ProposalUnavailable : FormDefinitionErrors.Forbidden, result.Error);
        Assert.Empty(host.Drafts.Created);
    }

    [Fact]
    public async Task A_channel_tap_after_the_draft_exists_gets_the_same_draft()
    {
        var host = new Host();
        var definitionId = Guid.NewGuid();
        host.Authoring.Reader.CreatedDefinitions[host.ProposalId] = new(definitionId, host.Evidence(host.Authoring.Actor));
        var result = await host.Service.CreateFromChannelAsync(host.ChannelRequest(), Ct);
        Assert.Equal(definitionId, result.Value.DefinitionId);
        Assert.Empty(host.Drafts.Created);
    }

    private sealed class Host
    {
        public AuthoringAssistantFixture Authoring { get; } = new();
        public StubAssistantProposalLookup Lookup { get; } = new();
        public RecordingDrafts Drafts { get; } = new();
        public FakeCurrentUser Current { get; } = new();
        public Guid ProposalId { get; } = Guid.NewGuid();
        public FormAuthoringFormProposal Proposal { get; } = new("es", "Control de carga", "CCC", "Enviar",
            [new("Patente", "shortText", true, [], null), new("Campo", "catalog", true, [], new(CatalogId, "Campos"))]);
        public FormDraftProposalService Service { get; }

        public Host()
        {
            Current.UserId = Authoring.Actor;
            Authoring.Reader.Catalogs.Add(AuthoringAssistantFixture.Catalog(CatalogId, "Campos"));
            Lookup.OwnerUserId = Authoring.Actor;
            Lookup.Response = new AssistantProposalLookupResponse(Authoring.Context,
                new(Authoring.TurnId, ProposalId, new(BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, 1), Proposal),
                new("fictitious-profile", 3, "home-system", 2, new DateTime(2026, 11, 4, 15, 0, 0, DateTimeKind.Utc), "home"), true);
            Service = new(Lookup, Authoring.Reader, Drafts, new FormDraftAccessGuard(Current, Authoring.Permissions));
        }

        public CreateFromAssistantProposalRequest Request => new(Lookup.RequestId, ProposalId);

        // WhatsApp's own conversation: the person is both Actor and Subject, in the assistant mode.
        public StructuredAuthoringProposalRequest ChannelRequest() => new(
            Authoring.Context with { ChannelId = "whatsapp-assistant", SubjectUserId = Authoring.Actor, InteractionMode = AssistantInteractionMode.Assistant },
            Lookup.Response!.Proposal, Lookup.Response.Provenance with { ChannelId = "whatsapp-assistant" });

        public string Evidence(Guid requestedBy) => JsonSerializer.Serialize(new FormDraftProposalEvidence("es", requestedBy,
            Lookup.Response!.Provenance.OccurredAtUtc, Authoring.TurnId, BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, 1,
            Lookup.Response!.Provenance, Proposal, "app"), JsonSerializerOptions.Web);
    }

    private sealed class RecordingDrafts : IFormDraftService
    {
        public List<SaveFormDraftRequest> Created { get; } = [];
        public Guid DefinitionId { get; } = Guid.NewGuid();
        public Error? Outcome { get; set; }

        public Task<Result<FormDraftCommitResponse>> CreateAsync(SaveFormDraftRequest request, CancellationToken cancellationToken)
        {
            if (Outcome is { } error) return Task.FromResult<Result<FormDraftCommitResponse>>(error);
            Created.Add(request);
            return Task.FromResult<Result<FormDraftCommitResponse>>(new FormDraftCommitResponse(request.RequestId,
                new(DefinitionId, request.Name, request.Prefix, false, 1, new(1, request.Draft.Culture, new Dictionary<string, string>(), []), null),
                new([], []), new(new DateTime(2026, 12, 4, 15, 0, 0, DateTimeKind.Utc))));
        }

        public Task<Result<FormDraftCommitResponse>> UpdateAsync(Guid id, SaveFormDraftRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<FormDefinitionDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<FormRecoveredChangeResponse>> GetChangeAsync(Guid requestId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<PagedResult<FormDefinitionListResponse>>> ListAsync(ListFormDefinitionsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<FormDefinitionFacetsResponse>> GetFacetsAsync(ListFormDefinitionsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
