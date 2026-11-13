using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// DOC6 (task 14): «Crear borrador» from the proposal of a request, as the reviewer edited it: once per request, only the proposal its
// operation closed on, answered by the request's own paths, with the reading on, the proposal's provenance as evidence and its documents.
public sealed class FormDocumentDraftServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid CatalogId = Guid.NewGuid();

    [Fact]
    public async Task Creating_saves_the_edited_form_with_the_reading_on_and_the_proposal_as_it_was_as_its_evidence()
    {
        var host = new Host();

        var result = await host.Service.CreateAsync(host.Body(), Ct);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var saved = Assert.Single(host.Drafts.Created);
        Assert.Equal((host.Observed.Id, host.Fixture.Reviewer), (saved.RequestId, saved.ActorUserId!.Value));
        Assert.Equal(("es", "Tickets de combustible", "TCB"), (saved.Draft.Culture, saved.Name["es"], saved.Prefix));
        Assert.Equal("Cargar ticket", saved.Draft.SubmitLabel!["es"]);
        // The questions as the reviewer left them: the station and the total kept, the plate removed and the pump added from a catalog.
        Assert.Equal([("Estación", "shortText"), ("Importe", "decimal"), ("Surtidor", "choice")],
            saved.Draft.Fields.Select(field => (field.Label["es"], field.Type)));
        Assert.Equal(CatalogId, saved.Draft.Fields[2].Source!.Catalog);
        Assert.Equal((true, "Ticket de una estación de servicio."), (saved.Draft.DocumentReading!.Enabled, saved.Draft.DocumentReading.Recognition));
        // The evidence is the proposal as it came, version 1's shape: never the edits, its samples or its similar standard.
        var evidence = JsonSerializer.Deserialize<FormDraftProposalEvidence>(saved.Proposal!.EvidenceJson, JsonSerializerOptions.Web)!;
        Assert.Equal((host.Fixture.Reviewer, FormDraftProposalService.DocumentsRequestedIn, (bool?)true, 2, host.Provenance.OccurredAtUtc),
            (evidence.RequestedByUserId, evidence.RequestedIn, evidence.DocumentReading, evidence.CapabilityVersion, evidence.RequestedAtUtc));
        Assert.Equal(["Estación", "Patente", "Importe"], evidence.OriginalProposal.Questions.Select(question => question.Label));
        Assert.DoesNotContain("Estación Ruta 8", saved.Proposal.EvidenceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("forms.transfer-receipt", saved.Proposal.EvidenceJson, StringComparison.Ordinal);
        Assert.Equal(new FormDraftAssistantProposalResponse(host.Observed.Id, host.Drafts.DefinitionId), result.Value);
        // In the draft's own save, the request records the draft it created and the proposal it came from, under its lock.
        var definition = BusinessFormDefinition.Create();
        Assert.True((await saved.Proposal.RecordAsync!(definition, Ct)).IsSuccess);
        Assert.Equal((definition.Id, host.Observed.Id), (host.Request.DefinitionId!.Value, host.Request.ProposalId!.Value));
        Assert.Equal([("request", host.Request.Id)], host.Fixture.Locks);
    }

    [Fact]
    public async Task The_same_request_again_gets_its_draft_without_reading_the_proposal_or_saving()
    {
        var host = new Host();
        var definitionId = Guid.NewGuid();
        Assert.True(host.Request.Draft(definitionId, host.Observed.Id).IsSuccess);

        var result = await host.Service.CreateAsync(host.Body(), Ct);
        // Whatever proposal the body names, the answer is the one its draft came from.
        var other = await host.Service.CreateAsync(host.Body() with { ProposalId = Guid.NewGuid() }, Ct);

        Assert.Equal(new FormDraftAssistantProposalResponse(host.Observed.Id, definitionId), result.Value);
        Assert.Equal(new FormDraftAssistantProposalResponse(host.Observed.Id, definitionId), other.Value);
        Assert.Empty(host.Drafts.Created);
        Assert.Equal(0, host.Fixture.Engine.Reads);
    }

    [Fact]
    public async Task Another_request_of_its_own_draft_is_a_conflict_in_the_same_save()
    {
        var host = new Host();
        await host.Service.CreateAsync(host.Body(), Ct);
        var saved = Assert.Single(host.Drafts.Created);
        Assert.True(host.Request.Draft(Guid.NewGuid(), host.Observed.Id).IsSuccess);

        Assert.Equal(FormReceivedDocumentErrors.RequestConflict, (await saved.Proposal!.RecordAsync!(BusinessFormDefinition.Create(), Ct)).Error);
    }

    [Fact]
    public async Task A_request_of_someone_else_or_unknown_is_not_found()
    {
        var host = new Host();
        var stranger = host.Fixture.Request(host.Request.DocumentIds, actor: Guid.NewGuid());

        Assert.Equal(FormReceivedDocumentErrors.ProposalNotFound, (await host.Service.CreateAsync(host.Body() with { RequestId = stranger.RequestId }, Ct)).Error);
        Assert.Equal(FormReceivedDocumentErrors.ProposalNotFound, (await host.Service.CreateAsync(host.Body() with { RequestId = Guid.NewGuid() }, Ct)).Error);
        Assert.Empty(host.Drafts.Created);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("failed")]
    [InlineData("other-proposal")]
    [InlineData("refused")]
    public async Task Only_the_proposal_its_operation_closed_on_creates(string kind)
    {
        var host = new Host();
        if (kind == "pending") host.Fixture.Engine.Complete(new(host.Request.RequestId, "pending", null, null, null));
        if (kind == "failed") host.Fixture.Engine.Complete(new(host.Request.RequestId, "failed", null, host.Provenance, "Ai.Assistant.ProviderUnavailable"));
        if (kind == "refused") host.Fixture.Engine.ReadFailure = FormDefinitionErrors.Forbidden;

        var result = await host.Service.CreateAsync(kind == "other-proposal" ? host.Body() with { ProposalId = Guid.NewGuid() } : host.Body(), Ct);

        Assert.Equal(kind == "refused" ? FormDefinitionErrors.Forbidden : FormReceivedDocumentErrors.ProposalNotFound, result.Error);
        Assert.Empty(host.Drafts.Created);
    }

    [Theory]
    [InlineData("label", "questions[0]")]
    [InlineData("type", "questions[0].type")]
    [InlineData("options", "questions[1].options")]
    [InlineData("catalog", "questions[2].catalogId")]
    [InlineData("recognition", "recognition")]
    [InlineData("long-recognition", "recognition")]
    [InlineData("name", "name")]
    [InlineData("prefix", "prefix")]
    [InlineData("questions", "questions")]
    [InlineData("request", "requestId")]
    [InlineData("proposal", "proposalId")]
    public async Task The_edited_form_answers_by_the_request_s_own_paths(string change, string path)
    {
        var host = new Host();
        var body = host.Body();
        var questions = body.Questions!.ToList();
        body = change switch
        {
            "label" => body with { Questions = [questions[0]! with { Label = null }, .. questions.Skip(1)] },
            "type" => body with { Questions = [questions[0]! with { Type = "signature" }, .. questions.Skip(1)] },
            "options" => body with { Questions = [questions[0], questions[1]! with { Type = "choice" }, questions[2]] },
            "catalog" => body with { Questions = [questions[0], questions[1], questions[2]! with { CatalogId = null }] },
            "recognition" => body with { Recognition = " " },
            "long-recognition" => body with { Recognition = new string('a', FormDocumentReading.MaxRecognitionLength + 1) },
            "name" => body with { Name = "" },
            "prefix" => body with { Prefix = "1ab" },
            "questions" => body with { Questions = [] },
            "request" => body with { RequestId = Guid.Empty },
            _ => body with { ProposalId = null },
        };

        var result = await host.Service.CreateAsync(body, Ct);

        Assert.Contains(path, Assert.IsType<ValidationError>(result.Error).Errors.Keys);
        Assert.Empty(host.Drafts.Created);
    }

    [Fact]
    public async Task A_name_in_use_or_a_catalog_the_reviewer_cannot_use_creates_nothing()
    {
        var host = new Host();
        host.Authoring.NamesInUse.Add("tickets de combustible");
        var taken = await host.Service.CreateAsync(host.Body(), Ct);
        host.Authoring.NamesInUse.Clear();
        host.Fixture.Permissions.Permissions[host.Fixture.Reviewer] = [.. FormDocumentProposalFixture.Designer.Except([BusinessFormsPermissions.CatalogsRead])];
        var unreadable = await host.Service.CreateAsync(host.Body(), Ct);

        Assert.Equal(FormDefinitionErrors.NameTaken.Code, taken.Error.Code);
        Assert.Contains("name", Assert.IsType<ValidationError>(taken.Error).Errors.Keys);
        Assert.Equal(FormDefinitionErrors.CatalogUnavailable.Code, unreadable.Error.Code);
        Assert.Contains("questions[2].catalogId", Assert.IsType<ValidationError>(unreadable.Error).Errors.Keys);
        Assert.Empty(host.Drafts.Created);
    }

    [Theory]
    [InlineData(BusinessFormsPermissions.DocumentsRead)]
    [InlineData(BusinessFormsPermissions.DocumentsReview)]
    [InlineData(BusinessFormsPermissions.Read)]
    [InlineData(BusinessFormsPermissions.Manage)]
    public async Task Creating_needs_to_see_and_review_documents_and_to_design_forms(string missing)
    {
        var host = new Host();
        host.Fixture.Permissions.Permissions[host.Fixture.Reviewer] = [.. FormDocumentProposalFixture.Designer.Except([missing])];

        Assert.Equal(FormDefinitionErrors.Forbidden, (await host.Service.CreateAsync(host.Body(), Ct)).Error);
        Assert.Equal(0, host.Fixture.Engine.Reads);
    }

    [Fact]
    public async Task The_draft_save_decides_the_rest_and_its_refusal_comes_back_as_it_is()
    {
        var host = new Host();
        host.Drafts.Outcome = FormDefinitionErrors.PrefixTaken;

        Assert.Equal(FormDefinitionErrors.PrefixTaken, (await host.Service.CreateAsync(host.Body(), Ct)).Error);
    }

    private sealed class Host
    {
        public FormDocumentProposalFixture Fixture { get; } = new();
        public AuthoringAssistantFixture.AuthoringReader Authoring { get; } = new();
        public RecordingDrafts Drafts { get; } = new();
        public FormDocumentProposal Request { get; }
        public AssistantOperationProvenance Provenance { get; } = new("fictitious-profile", 3, "capabilities-v1", 1,
            new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc), "forms-documents");
        public AssistantObservedProposal Observed { get; }
        public FormDocumentDraftService Service { get; }

        public Host()
        {
            Authoring.Catalogs.Add(AuthoringAssistantFixture.Catalog(CatalogId, "Surtidores"));
            Request = Fixture.Request([Fixture.Document().Id, Fixture.Document().Id], Guid.NewGuid());
            var proposal = new FormAuthoringDocumentFormProposal("es", "Tickets de combustible", "TCB", "Cargar ticket",
                [new("Estación", "shortText", true, [], null, "Estación Ruta 8 S.R.L."), new("Patente", "shortText", false, [], null, "AB 123 CD"),
                    new("Importe", "number", true, [], null, "52.360,00")],
                new("Ticket de una estación de servicio."), new("forms.transfer-receipt", "Comprobante de transferencia"));
            Observed = new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), FormDocumentAuthoringPolicy.ProposeForm, proposal);
            var context = FormDocumentAuthoringPolicy.Context(Fixture.Reviewer, Request.Culture, Request.TimeZoneId, Request.RequestId);
            Fixture.Engine.Submitted.Add(new(Request.RequestId, context, "", Provenance.OccurredAtUtc, Provenance.OccurredAtUtc.AddDays(7)));
            Fixture.Engine.Complete(new(Request.RequestId, "completed", Observed, Provenance, null) { Proposals = [Observed] });
            Service = Fixture.Drafts(Drafts, Authoring);
        }

        // The proposal as the reviewer edited it: the plate removed and the pump added from a catalog.
        public CreateFormDocumentDraftRequest Body() => new(Request.RequestId, Observed.Id, "Tickets de combustible", "tcb", "Cargar ticket",
            "Ticket de una estación de servicio.",
            [new("Estación", "shortText", true, [], null), new("Importe", "number", true, [], null), new("Surtidor", "catalog", false, [], CatalogId)]);
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
                new([], []), new(new DateTime(2026, 12, 12, 12, 0, 0, DateTimeKind.Utc))));
        }

        public Task<Result<FormDraftCommitResponse>> UpdateAsync(Guid id, SaveFormDraftRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<FormDefinitionDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<FormRecoveredChangeResponse>> GetChangeAsync(Guid requestId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<PagedResult<FormDefinitionListResponse>>> ListAsync(ListFormDefinitionsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<FormDefinitionFacetsResponse>> GetFacetsAsync(ListFormDefinitionsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
