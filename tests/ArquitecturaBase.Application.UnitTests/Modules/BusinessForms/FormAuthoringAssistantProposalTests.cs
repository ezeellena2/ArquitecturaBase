using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormAuthoringAssistantProposalTests
{
    private const string ProposeForm = BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId;
    private const string ProposeCatalog = BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId;
    private static readonly Guid Campos = new("0193f2c4-7a1e-7c10-9d4e-2b1f3a4c5d6e");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);

    private static object Question(string label, string type, bool required = false, string[]? options = null, Guid? catalogId = null) =>
        new { label, type, required, options = options ?? [], catalogId };

    private static string Form(string name = "Control de carga de camiones", string prefix = "ccc ", string submitLabel = "", params object[] questions) =>
        Json(new { name, prefix, submitLabel, questions = questions.Length == 0
            ? new[] { Question("Fecha de carga", "date", true), Question("Campo", "catalog", true, catalogId: Campos),
                Question("Destino", "choice", true, ["Puerto", "Acopio", "Planta"]), Question("Kilos brutos", "number", true),
                Question("Foto del ticket", "photo"), Question("Observaciones", "longText") }
            : questions });

    private static string Catalog(string name = "Contratistas", string code = "CUIT", string option = "Razón social", params object[] fields) =>
        Json(new { name, description = "", valueType = "text", code, option,
            fields = fields.Length == 0 ? new object[] { new { name = "Rubro", type = "text" }, new { name = "Zona", type = "text" } } : fields });

    private static AuthoringAssistantFixture WithCampos(string[]? permissions = null, bool active = true)
    {
        var host = new AuthoringAssistantFixture(permissions);
        host.Reader.Catalogs.Add(AuthoringAssistantFixture.Catalog(Campos, "Campos", active: active));
        return host;
    }

    [Fact]
    public void Proposals_declare_the_authoring_permissions_and_closed_contracts()
    {
        var definitions = new AuthoringAssistantFixture().Source.Definitions;
        var form = definitions.Single(definition => definition.Id == ProposeForm);
        var catalog = definitions.Single(definition => definition.Id == ProposeCatalog);
        Assert.Equal(AssistantCapabilityKind.Proposal, form.Kind);
        Assert.Equal([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage], form.RequiredPermissions.Actor);
        Assert.Equal(AssistantCapabilityKind.Proposal, catalog.Kind);
        Assert.Equal([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage], catalog.RequiredPermissions.Actor);
        Assert.Equal(4, definitions.Count);
    }

    [Fact]
    public async Task A_form_proposal_is_normalized_with_the_catalog_it_uses_and_the_default_button()
    {
        var host = WithCampos();
        var result = await host.Source.ExecuteAsync(host.Request(ProposeForm, Form()), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("proposed", result.Value.State);
        var proposal = Assert.IsType<FormAuthoringFormProposal>(result.Value.Payload);
        Assert.Equal(("es", "Control de carga de camiones", "CCC", "Enviar"), (proposal.Culture, proposal.Name, proposal.Prefix, proposal.SubmitLabel));
        Assert.Equal(new FormAuthoringProposedCatalog(Campos, "Campos"), proposal.Questions[1].Catalog);
        Assert.Equal(["Puerto", "Acopio", "Planta"], proposal.Questions[2].Options);
        Assert.Equal(6, proposal.Questions.Count);
    }

    [Fact]
    public async Task The_proposal_becomes_the_same_draft_the_editor_would_save()
    {
        var host = WithCampos();
        var result = await host.Source.ExecuteAsync(host.Request(ProposeForm, Form()), Ct);
        var draft = FormAuthoringProposalPolicy.Draft(Guid.NewGuid(), Assert.IsType<FormAuthoringFormProposal>(result.Value.Payload));
        Assert.Equal(["date", "choice", "choice", "decimal", "photo", "longText"], draft.Draft.Fields.Select(field => field.Type));
        Assert.Equal(Campos, draft.Draft.Fields[1].Source!.Catalog);
        Assert.Null(draft.Draft.Fields[1].Options);
        Assert.Equal(new FormFileSettings(0, 1, "cameraOrGallery"), draft.Draft.Fields[4].Files);
        Assert.Equal("Enviar", draft.Draft.SubmitLabel!["es"]);
    }

    [Fact]
    public async Task A_required_multiple_choice_asks_for_at_least_one_of_its_options()
    {
        var host = WithCampos();
        var result = await host.Source.ExecuteAsync(host.Request(ProposeForm, Form(questions: Question("Granos", "multiChoice", true, ["Soja", "Maíz"]))), Ct);
        var draft = FormAuthoringProposalPolicy.Draft(Guid.NewGuid(), Assert.IsType<FormAuthoringFormProposal>(result.Value.Payload));
        Assert.Equal(new FormSelectionSettings(1, 2), draft.Draft.Fields[0].Selection);
    }

    [Theory]
    [InlineData("1CC")]
    [InlineData("PREFIJODEMASIADOLARGO")]
    public async Task A_proposal_that_breaks_an_authoring_rule_is_rejected_and_not_trimmed(string prefix)
    {
        var host = WithCampos();
        var result = await host.Source.ExecuteAsync(host.Request(ProposeForm, Form(prefix: prefix)), Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task More_than_forty_questions_are_rejected()
    {
        var host = WithCampos();
        var questions = Enumerable.Range(1, 41).Select(index => Question("Pregunta " + index, "shortText")).ToArray();
        var result = await host.Source.ExecuteAsync(host.Request(ProposeForm, Form(questions: questions)), Ct);
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_catalog_question_needs_an_existing_active_catalog(bool exists)
    {
        var host = exists ? WithCampos(active: false) : new AuthoringAssistantFixture();
        var result = await host.Source.ExecuteAsync(host.Request(ProposeForm, Form()), Ct);
        Assert.Equal(FormDefinitionErrors.ContentInvalid, result.Error);
    }

    [Fact]
    public async Task Using_a_catalog_requires_reading_catalogs()
    {
        var host = WithCampos([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var result = await host.Source.ExecuteAsync(host.Request(ProposeForm, Form()), Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
    }

    [Theory]
    [InlineData("choice", false)]
    [InlineData("shortText", true)]
    public async Task Options_belong_only_to_choices(string type, bool withOptions)
    {
        var host = WithCampos();
        var result = await host.Source.ExecuteAsync(host.Request(ProposeForm, Form(questions: Question("Destino", type, options: withOptions ? ["Puerto"] : []))), Ct);
        Assert.Equal(FormDefinitionErrors.ContentInvalid, result.Error);
    }

    [Theory]
    [InlineData(ProposeForm, BusinessFormsPermissions.Read)]
    [InlineData(ProposeCatalog, BusinessFormsPermissions.CatalogsRead)]
    public async Task Proposing_requires_the_manage_permission_of_its_area(string capability, string readOnly)
    {
        var host = WithCampos([readOnly]);
        var result = await host.Source.ExecuteAsync(host.Request(capability, capability == ProposeForm ? Form() : Catalog()), Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(0, host.Reader.Reads);
    }

    [Fact]
    public async Task Unknown_members_are_rejected()
    {
        var host = WithCampos();
        var json = Form().Replace("\"name\"", "\"publish\":true,\"name\"", StringComparison.Ordinal);
        var result = await host.Source.ExecuteAsync(host.Request(ProposeForm, json), Ct);
        Assert.Equal(FormDefinitionErrors.ContentInvalid, result.Error);
    }

    [Fact]
    public async Task A_catalog_proposal_keeps_what_the_code_and_the_option_are_and_its_fields()
    {
        var host = new AuthoringAssistantFixture();
        var result = await host.Source.ExecuteAsync(host.Request(ProposeCatalog, Catalog(" Contratistas ")), Ct);
        Assert.True(result.IsSuccess);
        var proposal = Assert.IsType<FormAuthoringCatalogProposal>(result.Value.Payload);
        Assert.Equal(("es", "Contratistas", "CUIT", "Razón social", "text"), (proposal.Culture, proposal.Name, proposal.Code, proposal.Option, proposal.ValueType));
        Assert.Equal([new FormAuthoringProposedField("Rubro", "text"), new FormAuthoringProposedField("Zona", "text")], proposal.Fields);
    }

    [Theory]
    [InlineData("", "CUIT")]
    [InlineData("Contratistas", "")]
    public async Task A_catalog_proposal_needs_a_name_and_what_the_code_is(string name, string code)
    {
        var host = new AuthoringAssistantFixture();
        var result = await host.Source.ExecuteAsync(host.Request(ProposeCatalog, Catalog(name, code)), Ct);
        Assert.Equal(FormCatalogErrors.ContentInvalid, result.Error);
    }

    [Fact]
    public async Task Repeated_or_unknown_field_types_are_rejected()
    {
        var host = new AuthoringAssistantFixture();
        var repeated = await host.Source.ExecuteAsync(host.Request(ProposeCatalog, Catalog(fields: [new { name = "Zona", type = "text" }, new { name = "zona", type = "text" }])), Ct);
        var catalogField = await host.Source.ExecuteAsync(host.Request(ProposeCatalog, Catalog(fields: [new { name = "Lote", type = "catalog" }])), Ct);
        Assert.Equal(FormCatalogErrors.ContentInvalid, repeated.Error);
        Assert.Equal(FormCatalogErrors.ContentInvalid, catalogField.Error);
    }

    [Fact]
    public async Task The_owner_codec_reads_back_both_proposals()
    {
        var host = WithCampos();
        var form = await host.Source.ExecuteAsync(host.Request(ProposeForm, Form()), Ct);
        var catalog = await host.Source.ExecuteAsync(host.Request(ProposeCatalog, Catalog()), Ct);
        var formJson = Json(form.Value.Payload as FormAuthoringFormProposal);
        var catalogJson = Json(catalog.Value.Payload as FormAuthoringCatalogProposal);
        Assert.IsType<FormAuthoringFormProposal>(host.Source.ParseOutput(new(ProposeForm, 1), formJson).Value);
        Assert.IsType<FormAuthoringCatalogProposal>(host.Source.ParseOutput(new(ProposeCatalog, 1), catalogJson).Value);
        Assert.True(host.Source.ParseOutput(new(ProposeCatalog, 1), catalogJson.Replace("\"Contratistas\"", "\" Contratistas \"", StringComparison.Ordinal)).IsFailure);
        Assert.True(host.Source.ParseOutput(new(ProposeForm, 1), formJson.Replace("\"catalog\":null", "\"catalog\":null,\"x\":1", StringComparison.Ordinal)).IsFailure);
    }
}
