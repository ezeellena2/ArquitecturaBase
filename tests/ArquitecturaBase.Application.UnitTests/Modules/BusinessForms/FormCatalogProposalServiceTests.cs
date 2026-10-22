using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogProposalServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Accepting_creates_one_inactive_catalog_in_the_original_language_with_real_evidence(string culture)
    {
        var host = new FormCatalogProposalFixture();
        var request = host.Request(culture);
        var result = await host.Service.AcceptAsync(request, Ct);
        Assert.True(result.IsSuccess);
        var catalog = Assert.Single(host.Catalogs);
        Assert.False(catalog.IsActive);
        Assert.Equal(1, catalog.Revision);
        Assert.Equal(BusinessFormCatalogProposalState.Pending, catalog.ProposalState);
        Assert.Equal(request.Proposal.Id, Assert.Single(host.Locks));
        Assert.Equal(catalog.Id, result.Value.CatalogId);
        Assert.Equal(1, result.Value.PreviewTotalCount);
        using var names = JsonDocument.Parse(catalog.NameJson);
        Assert.Equal(culture, Assert.Single(names.RootElement.EnumerateObject()).Name);
        var evidence = JsonSerializer.Deserialize<FormCatalogProposalEvidence>(catalog.ProposalEvidenceJson!, JsonSerializerOptions.Web)!;
        Assert.Equal(request.Context.ActorUserId, evidence.RequestedByUserId);
        Assert.Equal(request.Provenance, evidence.Provenance);
        Assert.Equal(request.Provenance.OccurredAtUtc, evidence.RequestedAtUtc);
        Assert.Equal(1, host.Uow.Commits);
    }

    [Fact]
    public async Task A_replayed_proposal_returns_its_existing_catalog_without_reopening_a_decision_or_repeating_preview()
    {
        var host = new FormCatalogProposalFixture();
        var request = host.Request();
        Assert.True((await host.Service.AcceptAsync(request, Ct)).IsSuccess);
        var catalog = Assert.Single(host.Catalogs);
        Assert.True(catalog.DiscardProposal(1, host.Assistant.Actor, host.Time.GetUtcNow().UtcDateTime).IsSuccess);
        host.Roles = [];
        var replay = await host.Service.AcceptAsync(request, Ct);
        Assert.True(replay.IsSuccess);
        Assert.Equal(catalog.Id, replay.Value.CatalogId);
        Assert.Equal("Discarded", replay.Value.State);
        Assert.Equal(2, catalog.Revision);
        Assert.Single(host.Catalogs);
        Assert.Equal(1, host.PreviewReads);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("limit")]
    [InlineData("text")]
    public async Task Invalid_current_metadata_or_an_unsendable_preview_never_creates_a_ready_proposal(string invalid)
    {
        var host = new FormCatalogProposalFixture();
        if (invalid == "role") host.Roles = [];
        if (invalid == "limit") host.Preview = new([], 201, true);
        if (invalid == "text") host.Preview = new([new(Guid.NewGuid(), new string('x', BusinessFormCatalogItem.MaxTextLength + 1), null)], 1, false);
        var result = await host.Service.AcceptAsync(host.Request(), Ct);
        Assert.Equal(FormCatalogErrors.ContentInvalid.Code, result.Error.Code);
        Assert.Empty(host.Catalogs);
    }

    [Fact]
    public async Task Acceptance_rechecks_real_permissions_after_waiting_for_its_lock()
    {
        var host = new FormCatalogProposalFixture();
        host.WhileLocking = () => host.Assistant.Permissions.Permissions[host.Assistant.Actor] = [];
        var result = await host.Service.AcceptAsync(host.Request(), Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Single(host.Locks);
        Assert.Empty(host.Catalogs);
    }

    [Theory]
    [InlineData("channel")]
    [InlineData("actor")]
    [InlineData("turn")]
    [InlineData("capability")]
    public async Task The_owner_rejects_a_proposal_outside_its_captured_channel_identity_or_observed_read(string invalid)
    {
        var host = new FormCatalogProposalFixture();
        var request = host.Request();
        request = invalid switch
        {
            "channel" => request with { Context = request.Context with { ChannelId = "whatsapp-manual" } },
            "actor" => request with { Context = request.Context with { SubjectUserId = Guid.NewGuid() } },
            "turn" => request with { Proposal = request.Proposal with { TurnId = Guid.NewGuid() } },
            _ => request with { Proposal = request.Proposal with { Capability = new("forms.propose-registration", 1) } },
        };
        var result = await host.Service.AcceptAsync(request, Ct);
        Assert.True(result.IsFailure);
        Assert.Empty(host.Catalogs);
        Assert.Equal(0, host.PreviewReads);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Approval_saves_edits_and_activation_together_with_the_actual_decider_and_original_evidence(string culture)
    {
        var host = new FormCatalogProposalFixture();
        var catalog = host.Pending(culture);
        var evidence = catalog.ProposalEvidenceJson;
        var decider = host.Assistant.Users.AddUser("catalog-reviewer@example.test").Id;
        host.Assistant.Permissions.Permissions[decider] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        host.Current.UserId = decider;
        var result = await host.Service.ApproveAsync(catalog.Id, host.Approval(catalog, culture), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, catalog.Revision);
        Assert.True(catalog.IsActive);
        Assert.Equal(BusinessFormCatalogProposalState.Approved, catalog.ProposalState);
        Assert.Equal(decider, catalog.ProposalDecidedByUserId);
        Assert.Equal(host.Time.GetUtcNow().UtcDateTime, catalog.ProposalDecidedAtUtc);
        Assert.Equal(evidence, catalog.ProposalEvidenceJson);
        Assert.Equal("Reviewed supervisors", result.Value.Catalog.Name[culture]);
        Assert.Equal("Approved", result.Value.Catalog.Proposal!.State);
        Assert.Equal(1, host.Uow.Transactions);
        Assert.Equal(1, host.Uow.Commits);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("missing")]
    [InlineData("role")]
    [InlineData("sample")]
    [InlineData("original")]
    public async Task A_failed_approval_keeps_the_pending_catalog_inactive_and_unchanged(string invalid)
    {
        var host = new FormCatalogProposalFixture();
        var catalog = host.Pending("en");
        var request = host.Approval(catalog, "en");
        if (invalid == "revision") request = request with { ExpectedRevision = 2 };
        if (invalid == "role") host.Roles = [];
        if (invalid == "sample") host.SampleExists = false;
        if (invalid == "original") request = request with { Name = new Dictionary<string,string?> { ["es"] = "Sólo español" } };
        var result = await host.Service.ApproveAsync(invalid == "missing" ? Guid.NewGuid() : catalog.Id, request, Ct);
        var expected = invalid switch { "revision" => FormCatalogErrors.RevisionConflict, "missing" or "sample" => FormCatalogErrors.NotFound, _ => FormCatalogErrors.ContentInvalid };
        Assert.Equal(expected.Code, result.Error.Code);
        Assert.False(catalog.IsActive);
        Assert.Equal(1, catalog.Revision);
        Assert.Equal(BusinessFormCatalogProposalState.Pending, catalog.ProposalState);
        Assert.Null(catalog.ProposalDecidedByUserId);
    }

    [Fact]
    public async Task Discard_records_one_inactive_decision_and_a_stale_second_decision_is_a_conflict()
    {
        var host = new FormCatalogProposalFixture();
        var catalog = host.Pending();
        var result = await host.Service.DiscardAsync(catalog.Id, new(1), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("Discarded", result.Value.Proposal!.State);
        Assert.False(catalog.IsActive);
        Assert.Equal(host.Current.UserId, catalog.ProposalDecidedByUserId);
        Assert.Equal(2, catalog.Revision);
        Assert.Equal(FormCatalogErrors.RevisionConflict, (await host.Service.DiscardAsync(catalog.Id, new(1), Ct)).Error);
    }

    [Fact]
    public async Task The_web_decision_requires_its_actual_session_without_using_the_requester_as_a_fallback()
    {
        var host = new FormCatalogProposalFixture();
        var catalog = host.Pending();
        host.Current.UserId = null;
        Assert.Equal(FormDefinitionErrors.Unauthorized, (await host.Service.ApproveAsync(catalog.Id, host.Approval(catalog), Ct)).Error);
        Assert.Null(catalog.ProposalDecidedByUserId);
        Assert.Equal(0, host.Uow.Transactions);
    }
    [Fact]
    public async Task Reading_the_catalog_keeps_its_original_proposal_and_actual_requester()
    {
        var host = new FormCatalogProposalFixture();
        var catalog = host.Pending("en");
        var reader = new FormCatalogService(host, host, host.Uow, RequestValidators.For(new UpdateManualCatalogRequestValidator()),
            new(host.Current, host.Assistant.Permissions), host.Assistant.Users, new(host));
        var result = await reader.GetAsync(catalog.Id, Ct);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Proposal);
        Assert.Equal("en", result.Value.Proposal.OriginalCulture);
        Assert.Equal(host.Assistant.Actor, result.Value.Proposal.RequestedBy.UserId);
        Assert.Equal("Pending", result.Value.Proposal.State);
        Assert.False(result.Value.IsActive);
    }

    [Fact]
    public async Task Ordinary_edits_of_a_proposed_catalog_accept_its_original_language_without_changing_its_proposal()
    {
        var host = new FormCatalogProposalFixture();
        var catalog = host.Pending("en");
        var evidence = catalog.ProposalEvidenceJson;
        var editor = new FormCatalogFiltersService(host, host, host.Uow, RequestValidators.For(new UpdateFiltersCatalogRequestValidator()),
            new(host.Current, host.Assistant.Permissions), host.Assistant.Users, host);
        var request = host.Approval(catalog, "en");
        var result = await editor.UpdateAsync(catalog.Id, new(catalog.Revision, request.Name, request.Description, request.Filters, false), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("Reviewed supervisors", result.Value.Catalog.Name["en"]);
        Assert.Equal("Pending", result.Value.Catalog.Proposal!.State);
        Assert.False(catalog.IsActive);
        Assert.Equal(evidence, catalog.ProposalEvidenceJson);
    }

    [Fact]
    public async Task Normal_filters_catalog_edits_still_require_both_languages()
    {
        var host = new FormCatalogProposalFixture();
        var catalog = BusinessFormCatalog.CreateFilters(new Dictionary<string,string> { ["es"] = "Normal", ["en"] = "Normal" },
            new Dictionary<string,string> { ["es"] = "Normal", ["en"] = "Normal" }, "{}");
        host.Catalogs.Add(catalog);
        var editor = new FormCatalogFiltersService(host, host, host.Uow, RequestValidators.For(new UpdateFiltersCatalogRequestValidator()),
            new(host.Current, host.Assistant.Permissions), host.Assistant.Users, host);
        var request = host.Approval(catalog, "en");
        var result = await editor.UpdateAsync(catalog.Id, new(catalog.Revision, request.Name, request.Description, request.Filters, false), Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(1, catalog.Revision);
        Assert.True(catalog.IsActive);
    }

}
