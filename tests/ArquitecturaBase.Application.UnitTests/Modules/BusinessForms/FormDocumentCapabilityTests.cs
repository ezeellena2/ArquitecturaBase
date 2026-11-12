using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Task 7, decision 8: classifying and extracting a read document are capabilities of formularios, offered only with a document. In a
// channel the candidates read shows the forms with reading on the sender can complete; in the inbox the form read shows only the form
// the server fixed; propose-entry takes only what one of those reads showed in the turn, and a problem goes back with its path.
public sealed class FormDocumentCapabilityTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public void The_three_documents_capabilities_are_offered_only_with_a_document_and_never_in_Home()
    {
        var f = new Fixture();
        var definitions = f.Source.Definitions;
        Assert.Equal([BusinessFormDocumentAssistantCapabilitySource.CandidatesId, BusinessFormDocumentAssistantCapabilitySource.FormId,
            BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId], definitions.Select(definition => definition.Id));
        Assert.All(definitions, definition => Assert.Equal((AssistantCapabilityAttachment.Document, false, 1), (definition.Attachment, definition.OfferedInHome, definition.Version)));
        Assert.Empty(definitions[0].RequiredPermissions.Actor);
        Assert.Equal([BusinessFormsPermissions.RecordsCreate], definitions[0].RequiredPermissions.Subject);
        Assert.Equal([BusinessFormsPermissions.DocumentsReview], definitions[1].RequiredPermissions.Actor);
        Assert.Empty(definitions[1].RequiredPermissions.Subject);
        Assert.Empty(definitions[2].RequiredPermissions.Actor.Concat(definitions[2].RequiredPermissions.Subject));
        Assert.All(definitions, definition => { using var input = JsonDocument.Parse(definition.InputSchemaJson); using var output = JsonDocument.Parse(definition.OutputSchemaJson); });
    }

    [Fact]
    public async Task The_candidates_read_shows_the_forms_with_reading_on_with_their_recognition_and_the_questions_a_reading_fills()
    {
        var f = new Fixture();
        f.Documents.AddForm([FormReceivedDocumentFixture.Question("shortText", "Sin lectura")], reading: false, name: "Sin lectura");
        f.Documents.AddForm([FormReceivedDocumentFixture.Question("shortText", "En la app")], appOnly: true, name: "En la app");
        var result = await f.Source.ExecuteAsync(f.Candidates(), Ct);
        var payload = Assert.IsType<FormDocumentReadPayload>(Assert.Single([result.Value.Payload]));
        Assert.Equal(("complete", f.Sender, "es"), (payload.State, payload.UserId, payload.Culture));
        var form = Assert.Single(payload.Forms);
        Assert.Equal((f.Form.DefinitionId, f.Form.VersionId, "Comprobante", "Un comprobante de transferencia."), (form.DefinitionId, form.VersionId, form.Name, form.Recognition));
        // The document question is the file itself, never a datum to read; a catalog question is named by its catalog, without options.
        Assert.Equal(["Monto", "Fecha", "Cuenta", "Banco", "Moneda"], form.Questions.Select(question => question.Label));
        var bank = form.Questions.Single(question => question.Label == "Banco");
        Assert.Equal(("Bancos y billeteras", 0), (bank.Catalog, bank.Choices.Count));
        Assert.Equal(["Pesos", "Dólares"], form.Questions.Single(question => question.Label == "Moneda").Choices.Select(choice => choice.Text));
    }

    [Fact]
    public async Task The_candidates_read_needs_a_sender_who_can_still_load_responses_in_the_conversation_language()
    {
        var f = new Fixture();
        f.Permissions.Permissions[f.Sender] = [];
        Assert.Equal(FormRecordErrors.AuthorizationChanged, (await f.Source.ExecuteAsync(f.Candidates(), Ct)).Error);
        var other = new Fixture();
        var request = other.Candidates();
        Assert.True((await other.Source.ExecuteAsync(request with { Context = request.Context with { Culture = "en" } }, Ct)).IsFailure);
        Assert.True((await other.Source.ExecuteAsync(request with { Context = request.Context with { SubjectUserId = null } }, Ct)).IsFailure);
    }

    [Fact]
    public async Task A_read_that_does_not_fit_its_budget_is_partial_and_keeps_the_forms_that_fit()
    {
        var f = new Fixture();
        for (var index = 0; index < 60; index++)
            f.Documents.AddForm(Enumerable.Range(0, 20).Select(number => FormReceivedDocumentFixture.Question("shortText", $"Dato {number} de un formulario largo")).ToArray(),
                name: "Formulario " + index);
        var payload = Assert.IsType<FormDocumentReadPayload>((await f.Source.ExecuteAsync(f.Candidates(), Ct)).Value.Payload);
        Assert.Equal(("partial", false), (payload.State, payload.IsComplete));
        Assert.InRange(payload.Forms.Count, 1, 60);
        Assert.True(FormDocumentCapabilityCodec.ResponseBytes(payload, payload.State, false) <= FormDocumentCapabilityCodec.ResponseBudget);
    }

    [Fact]
    public async Task The_form_read_shows_only_the_form_the_server_fixed_for_a_reviewer_whatever_the_sender_may_do()
    {
        var f = new Fixture();
        var other = f.Documents.AddForm([FormReceivedDocumentFixture.Question("decimal", "Total")], reading: false, name: "Gastos");
        var payload = Assert.IsType<FormDocumentReadPayload>((await f.Source.ExecuteAsync(f.FormRead(other.DefinitionId), Ct)).Value.Payload);
        var form = Assert.Single(payload.Forms);
        Assert.Equal((other.DefinitionId, (string?)null, f.Reviewer), (form.DefinitionId, form.Recognition, payload.UserId));
        f.Permissions.Permissions[f.Reviewer] = [BusinessFormsPermissions.DocumentsRead];
        Assert.Equal(FormRecordErrors.AuthorizationChanged, (await f.Source.ExecuteAsync(f.FormRead(other.DefinitionId), Ct)).Error);
        var withSubject = f.FormRead(other.DefinitionId);
        Assert.True((await new Fixture().Source.ExecuteAsync(withSubject with { Context = withSubject.Context with { SubjectUserId = f.Sender } }, Ct)).IsFailure);
    }

    [Fact]
    public async Task A_sure_proposal_from_the_latest_read_is_normalized_with_each_value_typed_and_canonical()
    {
        var f = new Fixture();
        var read = await f.ReadAsync();
        var result = await f.Source.ExecuteAsync(f.Propose(read, "sure", [f.Candidate(("Monto", "150000.50", false), ("Fecha", "2026-11-11", true), ("Cuenta", null, false))]), Ct);
        Assert.Equal("proposed", result.Value.State);
        var proposal = Assert.IsType<FormDocumentEntryProposal>(result.Value.Payload);
        Assert.Equal(("sure", 1, "comprobante de transferencia"), (proposal.Outcome, proposal.ReadOrdinal, proposal.DocumentType));
        var candidate = Assert.Single(proposal.Candidates);
        Assert.Equal((f.Form.DefinitionId, f.Form.VersionId, 3L), (candidate.DefinitionId, candidate.VersionId, candidate.ExpectedDefinitionRevision));
        Assert.Equal([("decimal", "150000.5", false), ("date", "2026-11-11", true), ("shortText", null, false)],
            candidate.Values.Select(value => (value.Type, value.Value, value.Doubtful)));
        var parsed = f.Source.ParseOutput(new(BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId, 1), JsonSerializer.Serialize(proposal, JsonSerializerOptions.Web));
        Assert.Equal(JsonSerializer.Serialize(proposal, JsonSerializerOptions.Web), JsonSerializer.Serialize(Assert.IsType<FormDocumentEntryProposal>(parsed.Value), JsonSerializerOptions.Web));
    }

    [Theory]
    [InlineData("sure-with-two", "candidates", FormDocumentEntryIssue.Count)]
    [InlineData("unknown-with-one", "candidates", FormDocumentEntryIssue.Count)]
    [InlineData("unobserved", "candidates[0]", FormDocumentEntryIssue.NotObserved)]
    [InlineData("unknown-question", "candidates[0].values[0].fieldId", FormDocumentEntryIssue.UnknownQuestion)]
    [InlineData("catalog", "candidates[0].values[0].value", FormDocumentEntryIssue.FilledByServer)]
    [InlineData("decimal", "candidates[0].values[0].value", FormDocumentEntryIssue.Invalid)]
    [InlineData("choice", "candidates[0].values[0].value", FormDocumentEntryIssue.NotOffered)]
    [InlineData("personal", "documentType", FormDocumentEntryIssue.PersonalData)]
    [InlineData("repeated", "candidates[0].values[1].fieldId", FormDocumentEntryIssue.Repeated)]
    public async Task A_proposal_that_does_not_pass_goes_back_rejected_with_its_path_and_is_never_trimmed(string problem, string path, string code)
    {
        var f = new Fixture();
        var read = await f.ReadAsync();
        var candidate = f.Candidate(("Monto", "100", false));
        var (outcome, candidates, documentType) = problem switch
        {
            "sure-with-two" => ("sure", new[] { candidate, candidate with { DefinitionId = Guid.NewGuid() } }, "recibo"),
            "unknown-with-one" => ("unknown", [candidate], "recibo"),
            "unobserved" => ("sure", [candidate with { DefinitionId = Guid.NewGuid() }], "recibo"),
            "unknown-question" => ("sure", [candidate with { Values = [new(Guid.NewGuid(), "x", false)] }], "recibo"),
            "catalog" => ("sure", [f.Candidate(("Banco", "Galicia", false))], "recibo"),
            "decimal" => ("sure", [f.Candidate(("Monto", "150.000,50", false))], "recibo"),
            "choice" => ("sure", [f.Candidate(("Moneda", Guid.NewGuid().ToString("D"), false))], "recibo"),
            "personal" => ("sure", [candidate], "transferencia de 20123456786"),
            _ => ("sure", [f.Candidate(("Monto", "100", false), ("Monto", "200", false))], "recibo"),
        };
        var result = await f.Source.ExecuteAsync(f.Propose(read, outcome, candidates, documentType), Ct);
        Assert.Equal("rejected", result.Value.State);
        var rejection = Assert.IsType<FormDocumentEntryRejection>(result.Value.Payload);
        Assert.Contains(rejection.Issues, issue => issue.Path == path && issue.Code == code);
        var parsed = f.Source.ParseOutput(new(BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId, 1), JsonSerializer.Serialize(rejection, JsonSerializerOptions.Web));
        Assert.IsType<FormDocumentEntryRejection>(parsed.Value);
    }

    [Fact]
    public async Task An_unknown_document_names_its_type_even_without_a_read_and_a_read_of_another_person_authorizes_nothing()
    {
        var f = new Fixture();
        var unknown = await f.Source.ExecuteAsync(f.Propose(null, "unknown", [], "ticket de combustible"), Ct);
        var proposal = Assert.IsType<FormDocumentEntryProposal>(unknown.Value.Payload);
        Assert.Equal(("unknown", 0, "ticket de combustible"), (proposal.Outcome, proposal.ReadOrdinal, proposal.DocumentType));
        var read = await f.ReadAsync();
        var foreign = read with { Payload = ((FormDocumentReadPayload)read.Payload) with { UserId = Guid.NewGuid() } };
        Assert.Equal(FormRecordErrors.InvalidResponse, (await f.Source.ExecuteAsync(f.Propose(foreign, "sure", [f.Candidate(("Monto", "1", false))]), Ct)).Error);
    }

    [Fact]
    public void The_codec_rejects_arguments_with_missing_or_extra_properties_and_reads_back_only_its_own_shapes()
    {
        Assert.True(FormDocumentCapabilityCodec.ParseArguments("{\"outcome\":\"sure\",\"candidates\":[]}").IsFailure);
        Assert.True(FormDocumentCapabilityCodec.ParseArguments("{\"outcome\":\"sure\",\"candidates\":[],\"documentType\":\"x\",\"extra\":1}").IsFailure);
        Assert.True(FormDocumentCapabilityCodec.ParseArguments("not json").IsFailure);
        Assert.True(FormDocumentCapabilityCodec.ParseArguments("{\"outcome\":\"unknown\",\"candidates\":[],\"documentType\":\"x\"}").IsSuccess);
        Assert.True(FormDocumentCapabilityCodec.ParseOutput(new(BusinessFormDocumentAssistantCapabilitySource.CandidatesId, 2), "{}").IsFailure);
        Assert.True(FormDocumentCapabilityCodec.ParseOutput(new(BusinessFormDocumentAssistantCapabilitySource.CandidatesId, 1), "{\"state\":\"complete\"}").IsFailure);
    }

    private sealed class Fixture
    {
        public FormReceivedDocumentFixture Documents { get; } = new();
        public InMemoryUserAccounts Users { get; } = new();
        public TestDoubles.Auth.FakePermissionService Permissions { get; } = new();
        public BusinessFormDocumentAssistantCapabilitySource Source { get; }
        public Guid Sender { get; }
        public Guid Reviewer { get; }
        public Guid TurnId { get; } = Guid.CreateVersion7();
        public FormDocumentFormRow Form { get; }
        public Guid CatalogId { get; } = Guid.CreateVersion7();

        public Fixture()
        {
            Sender = Users.AddUser("sender@example.invalid").Id;
            Reviewer = Users.AddUser("reviewer@example.invalid").Id;
            Permissions.Permissions[Sender] = [BusinessFormsPermissions.RecordsCreate];
            Permissions.Permissions[Reviewer] = [BusinessFormsPermissions.DocumentsRead, BusinessFormsPermissions.DocumentsReview];
            Documents.Catalogs.Add(new(CatalogId, "{\"es\":\"Bancos y billeteras\",\"en\":\"Banks and wallets\"}", "es"));
            Form = Documents.AddForm([
                FormReceivedDocumentFixture.Question("decimal", "Monto", required: true),
                FormReceivedDocumentFixture.Question("date", "Fecha"),
                FormReceivedDocumentFixture.Question("shortText", "Cuenta", validation: FormQuestionValidation.BankAccount),
                FormReceivedDocumentFixture.Question("choice", "Banco", catalog: CatalogId),
                FormReceivedDocumentFixture.Question("choice", "Moneda", options: [new(Guid.CreateVersion7(), new Dictionary<string, string> { ["es"] = "Pesos" }),
                    new(Guid.CreateVersion7(), new Dictionary<string, string> { ["es"] = "Dólares" })]),
                FormReceivedDocumentFixture.Question("document", "Comprobante", files: new(1, 1))]);
            Source = new(Documents, Users, Permissions);
        }

        public AssistantCapabilityRequest Candidates() => new(TurnId, Context(Sender, Sender), new(BusinessFormDocumentAssistantCapabilitySource.CandidatesId, 1), "{}", []);

        public AssistantCapabilityRequest FormRead(Guid definitionId) => new(TurnId, Context(Reviewer, null) with { FocusDefinitionId = definitionId },
            new(BusinessFormDocumentAssistantCapabilitySource.FormId, 1), "{}", []);

        public async Task<AssistantObservedRead> ReadAsync() =>
            new(TurnId, 1, new(BusinessFormDocumentAssistantCapabilitySource.CandidatesId, 1), (await Source.ExecuteAsync(Candidates(), Ct)).Value.Payload);

        public FormDocumentEntryArgumentCandidate Candidate(params (string Label, string? Value, bool Doubtful)[] values)
        {
            var schema = JsonSerializer.Deserialize<FormVersionSchema>(Form.SchemaJson, JsonSerializerOptions.Web)!;
            return new(Form.DefinitionId, Form.VersionId, values.Select(value => new FormDocumentEntryArgumentValue(
                schema.Questions.First(question => question.Label["es"] == value.Label).FieldId, value.Value, value.Doubtful)).ToArray());
        }

        public AssistantCapabilityRequest Propose(AssistantObservedRead? read, string outcome, IReadOnlyList<FormDocumentEntryArgumentCandidate> candidates,
            string documentType = "comprobante de transferencia") =>
            new(TurnId, Context(Sender, Sender), new(BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId, 1),
                JsonSerializer.Serialize(new FormDocumentEntryArguments(outcome, candidates, documentType), JsonSerializerOptions.Web), read is null ? [] : [read]);

        private static AssistantChannelContext Context(Guid actor, Guid? subject) => new("whatsapp-assistant", actor, subject, "es", "America/Argentina/Buenos_Aires",
            new([], []), [], [new("assistant_proposal", 1)]);
    }
}
