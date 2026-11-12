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

    private static StructuredAuthoringProposalRequest Request(AssistantObservedProposal proposal) => new(Guid.NewGuid(), new CatalogAssistantFixture().Context, proposal,
        new("fictitious-profile", 3, "capabilities-v1", 1, new DateTime(2026, 11, 4, 15, 0, 0, DateTimeKind.Utc), "whatsapp-assistant"));

    [Fact]
    public void It_summarizes_its_own_authoring_proposals_and_nothing_else()
    {
        var channel = new BusinessFormAuthoringProposalChannel(new Drafts(), new Catalogs());
        Assert.Equal(StructuredAuthoringKind.Form, channel.Summarize(Guid.NewGuid(), Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, Form), "es")!.Kind);
        Assert.Equal("Contratistas", channel.Summarize(Guid.NewGuid(), Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, Manual), "es")!.Title);
        var people = channel.Summarize(Guid.NewGuid(), Observed(BusinessFormCatalogAssistantCapabilitySource.ProposeId, CatalogAssistantFixture.Proposal), "es")!;
        Assert.Equal((StructuredAuthoringKind.Catalog, CatalogAssistantFixture.Proposal.Name), (people.Kind, people.Title));
        Assert.Null(channel.Summarize(Guid.NewGuid(), Observed("forms.missing-request.propose", Form), "es"));
        Assert.Null(channel.Summarize(Guid.NewGuid(), Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, Manual), "es"));
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

    // IA, Stage 2: version 2's catalog says how many rows of data its spreadsheet brings; an import names the catalog, which column
    // fills each part of its options and the rows, and its path opens that catalog's import with the operation and the proposal.
    private static FormAuthoringSpreadsheetSummary Spreadsheet(bool headers = true, string optionLetter = "B") => new("clientes.csv", "", 86, headers,
        [new("code", null, null, "A", headers ? "CUIT" : null), new("option", null, null, optionLetter, headers ? "Razón\nsocial" : null),
            new("field", "Rubro", "rubro", "C", headers ? "Rubro" : null)], []);

    [Theory]
    [InlineData("es", "el código de la columna A (CUIT), cada opción de la columna B (Razón social) y el campo Rubro de la columna C (Rubro)")]
    [InlineData("en", "the code from column A (CUIT), each option from column B (Razón social) and the field Rubro from column C (Rubro)")]
    public void An_import_names_its_catalog_its_columns_and_rows_and_the_path_to_review_it(string culture, string mapping)
    {
        var channel = new BusinessFormAuthoringProposalChannel(new Drafts(), new Catalogs());
        var catalogId = Guid.NewGuid(); var requestId = Guid.NewGuid();
        var proposal = Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeImportId,
            new FormAuthoringImportProposal(new(catalogId, "Clientes", "text"), Spreadsheet()));
        var summary = channel.Summarize(requestId, proposal, culture)!;
        Assert.Equal((StructuredAuthoringKind.Import, "Clientes", (int?)86), (summary.Kind, summary.Title, summary.RowCount));
        Assert.Equal([mapping], summary.Lines);
        Assert.Equal($"/catalogos/{catalogId:D}?pedido={requestId:N}&propuesta={proposal.Id:N}", summary.AppPath);
    }

    [Fact]
    public void A_simple_list_shares_its_column_and_a_sheet_without_headers_names_only_the_letters()
    {
        var channel = new BusinessFormAuthoringProposalChannel(new Drafts(), new Catalogs());
        var simple = channel.Summarize(Guid.NewGuid(), Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeImportId,
            new FormAuthoringImportProposal(new(Guid.NewGuid(), "Productos", "text"), Spreadsheet(headers: false, optionLetter: "A"))), "es")!;
        Assert.Equal(["el código y cada opción de la columna A y el campo Rubro de la columna C"], simple.Lines);
    }

    [Fact]
    public void A_catalog_that_maps_a_spreadsheet_says_how_many_rows_it_takes()
    {
        var channel = new BusinessFormAuthoringProposalChannel(new Drafts(), new Catalogs());
        FormAuthoringMappedCatalogProposal Mapped(FormAuthoringSpreadsheetSummary? spreadsheet) => new(Manual.Culture, Manual.Name, Manual.Description,
            Manual.ValueType, Manual.Code, Manual.Option, Manual.Fields, spreadsheet);
        AssistantObservedProposal Version2(FormAuthoringMappedCatalogProposal mapped) =>
            Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, mapped) with { Capability = new(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, 2) };
        var first = channel.Summarize(Guid.NewGuid(), Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, Manual), "es")!;
        var mapped = channel.Summarize(Guid.NewGuid(), Version2(Mapped(Spreadsheet())), "es")!;
        Assert.Equal((first.Kind, first.Title, first.Lines[0], (int?)86, (string?)null), (mapped.Kind, mapped.Title, mapped.Lines[0], mapped.RowCount, mapped.AppPath));
        Assert.Null(channel.Summarize(Guid.NewGuid(), Version2(Mapped(null)), "es")!.RowCount);
        Assert.Null(first.RowCount);
    }

    // Stage 3: version 2 of a form, with or without a reading, has IA3-A's summary byte for byte and is created the same way.
    private static FormAuthoringDocumentFormProposal Document(FormAuthoringProposedReading? reading) => new("es", "Parte de lluvias", "LLU", "Enviar",
        [new("Milímetros", "number", true, [], null, reading is null ? null : "12,5")], reading, reading is null ? null : new("forms.rain", "Lluvias"));

    private static AssistantObservedProposal Version2(AssistantCapabilityPayload payload) =>
        new(Guid.NewGuid(), Guid.NewGuid(), new(BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, BusinessFormAuthoringAssistantCapabilitySource.DocumentFormVersion), payload);

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Version_two_of_a_form_has_version_one_s_summary(string culture)
    {
        var channel = new BusinessFormAuthoringProposalChannel(new Drafts(), new Catalogs());
        var first = System.Text.Json.JsonSerializer.Serialize(channel.Summarize(Guid.NewGuid(), Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, Form), culture),
            System.Text.Json.JsonSerializerOptions.Web);
        foreach (var reading in new FormAuthoringProposedReading?[] { null, new("Partes diarios de lluvia de cada campo.") })
            Assert.Equal(first, System.Text.Json.JsonSerializer.Serialize(channel.Summarize(Guid.NewGuid(), Version2(Document(reading)), culture),
                System.Text.Json.JsonSerializerOptions.Web));
        // Each version reads only its own payload.
        Assert.Null(channel.Summarize(Guid.NewGuid(), Version2(Form), culture));
        Assert.Null(channel.Summarize(Guid.NewGuid(), Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, Document(null)), culture));
    }

    [Fact]
    public async Task Version_two_of_a_form_is_created_the_same_way()
    {
        var drafts = new Drafts();
        var created = await new BusinessFormAuthoringProposalChannel(drafts, new Catalogs())
            .CreateAsync(Request(Version2(Document(new("Partes diarios de lluvia de cada campo.")))), Ct);
        Assert.Equal(new StructuredAuthoringCreatedResponse(StructuredAuthoringKind.Form, drafts.Id, "Parte de lluvias", $"/formularios/{drafts.Id:D}"), created.Value);
    }

    [Fact]
    public async Task Creating_passes_the_request_of_its_operation_to_the_owner()
    {
        var catalogs = new Catalogs();
        var request = Request(Observed(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, Manual));
        await new BusinessFormAuthoringProposalChannel(new Drafts(), catalogs).CreateAsync(request, Ct);
        Assert.Equal(request.RequestId, catalogs.Requests.Single().RequestId);
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
        public List<StructuredAuthoringProposalRequest> Requests { get; } = [];
        public Task<Result<FormCatalogAssistantProposalResponse>> CreateFromChannelAsync(StructuredAuthoringProposalRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult<Result<FormCatalogAssistantProposalResponse>>(Outcome is { } error ? error : new FormCatalogAssistantProposalResponse(request.Proposal.Id, Id));
        }
        public Task<Result<FormCatalogAssistantProposalResponse>> CreateAsync(CreateFromAssistantProposalRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<FormCatalogAssistantProposalResponse>> GetAsync(Guid proposalId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
