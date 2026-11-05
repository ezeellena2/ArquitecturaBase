using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormAuthoringAssistantReadTests
{
    private const string Forms = BusinessFormAuthoringAssistantCapabilitySource.FormsId;
    private const string Catalogs = BusinessFormAuthoringAssistantCapabilitySource.CatalogsId;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);

    [Fact]
    public void Each_read_declares_its_own_permission_and_a_closed_contract()
    {
        var definitions = new AuthoringAssistantFixture().Source.Definitions;
        var forms = definitions.Single(definition => definition.Id == Forms);
        var catalogs = definitions.Single(definition => definition.Id == Catalogs);
        Assert.Equal(AssistantCapabilityKind.Read, forms.Kind);
        Assert.Equal([BusinessFormsPermissions.Read], forms.RequiredPermissions.Actor);
        Assert.Equal(AssistantCapabilityKind.Read, catalogs.Kind);
        Assert.Equal([BusinessFormsPermissions.CatalogsRead], catalogs.RequiredPermissions.Actor);
        Assert.All([forms, catalogs], definition =>
        {
            Assert.Equal(1, definition.Version);
            Assert.Empty(definition.RequiredPermissions.Subject);
            using var input = JsonDocument.Parse(definition.InputSchemaJson);
            using var output = JsonDocument.Parse(definition.OutputSchemaJson);
            Assert.False(input.RootElement.GetProperty("additionalProperties").GetBoolean());
            Assert.False(output.RootElement.GetProperty("additionalProperties").GetBoolean());
        });
    }

    [Fact]
    public async Task Reading_forms_returns_names_prefixes_states_and_question_types()
    {
        var host = new AuthoringAssistantFixture();
        host.Reader.Definitions.Add(AuthoringAssistantFixture.Definition("Control de carga", "CCC", published: true, inactive: false,
            ("Patente", "shortText"), ("Kilos", "decimal")));
        host.Reader.Definitions.Add(AuthoringAssistantFixture.Definition("Parte de lluvias", "LLU", published: true, inactive: true));
        host.Reader.Definitions.Add(AuthoringAssistantFixture.Definition("Recepción", "REC"));
        var result = await host.Source.ExecuteAsync(host.Request(Forms), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("complete", result.Value.State);
        var payload = Assert.IsType<FormAuthoringFormsPayload>(result.Value.Payload);
        Assert.True(payload.IsComplete);
        Assert.Equal(["published", "inactive", "draft"], payload.Forms.Select(form => form.State));
        var first = payload.Forms[0];
        Assert.Equal(("Control de carga", "CCC"), (first.Name, first.Prefix));
        Assert.Equal([new FormAuthoringQuestionRow("Patente", "shortText"), new FormAuthoringQuestionRow("Kilos", "decimal")], first.Questions);
        Assert.Equal(3, result.Value.ItemCount);
        Assert.Empty(result.Value.Evidences);
    }

    [Theory]
    [InlineData("en", "Register maintenance", "Equipment", "Colors")]
    [InlineData("es", "Registrar mantenimiento", "Equipo", "Colores")]
    public async Task Each_name_reads_in_the_conversation_language_as_creating_compares_it_or_in_its_original_without_a_translation(
        string culture, string form, string question, string catalog)
    {
        var host = new AuthoringAssistantFixture();
        host.Reader.Definitions.Add(new(FormDraftPolicy.Serialize(new Dictionary<string, string> { ["es"] = "Registrar mantenimiento", ["en"] = "Register maintenance" }),
            "es", "MAN", true, false, FormDraftPolicy.Serialize(new FormQuestionSchema(1, [new CanonicalFormField(Guid.NewGuid(), "q0", "shortText",
                new Dictionary<string, string> { ["es"] = "Equipo", ["en"] = "Equipment" }, null, false, null, null)]))));
        host.Reader.Definitions.Add(AuthoringAssistantFixture.Definition("Parte de lluvias", "LLU", questions: [("Milímetros", "decimal")]));
        host.Reader.Catalogs.Add(AuthoringAssistantFixture.Catalog(Guid.NewGuid(), "Colores") with
            { NameJson = FormDraftPolicy.Serialize(new Dictionary<string, string> { ["es"] = "Colores", ["en"] = "Colors" }) });
        host.Reader.Catalogs.Add(AuthoringAssistantFixture.Catalog(Guid.NewGuid(), "Zonas"));
        var context = host.Context with { Culture = culture };

        var forms = Assert.IsType<FormAuthoringFormsPayload>((await host.Source.ExecuteAsync(host.Request(Forms) with { Context = context }, Ct)).Value.Payload);
        var catalogs = Assert.IsType<FormAuthoringCatalogsPayload>((await host.Source.ExecuteAsync(host.Request(Catalogs) with { Context = context }, Ct)).Value.Payload);

        Assert.Equal([form, "Parte de lluvias"], forms.Forms.Select(row => row.Name));
        Assert.Equal([question, "Milímetros"], forms.Forms.Select(row => row.Questions[0].Label));
        Assert.Equal([catalog, "Zonas"], catalogs.Catalogs.Select(row => row.Name));
    }

    [Fact]
    public async Task A_block_of_rows_is_not_read_as_one_of_the_forty_questions()
    {
        var host = new AuthoringAssistantFixture();
        host.Reader.Definitions.Add(AuthoringAssistantFixture.Definition("Recepción", "REC", false, false,
            [("Artículos", "group"), .. Enumerable.Range(1, 40).Select(index => ($"Dato {index}", "shortText"))]));

        var result = await host.Source.ExecuteAsync(host.Request(Forms), Ct);

        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<FormAuthoringFormsPayload>(result.Value.Payload);
        Assert.Equal(40, payload.Forms[0].Questions.Count);
        Assert.DoesNotContain(payload.Forms[0].Questions, question => question.Type == "group");
        Assert.True(host.Source.ParseOutput(new(Forms, 1), Json(payload)).IsSuccess);
    }

    [Fact]
    public async Task Reading_catalogs_returns_ids_kinds_and_active_fields_without_options()
    {
        var host = new AuthoringAssistantFixture();
        var id = Guid.NewGuid();
        host.Reader.Catalogs.Add(AuthoringAssistantFixture.Catalog(id, "Campos", fields:
            [AuthoringAssistantFixture.Field("zona", "Zona", position: 1), AuthoringAssistantFixture.Field("viejo", "Viejo", active: false),
             AuthoringAssistantFixture.Field("superficie", "Superficie", "decimal", position: 0)]));
        host.Reader.Catalogs.Add(AuthoringAssistantFixture.Catalog(Guid.NewGuid(), "Supervisores", BusinessFormCatalogKind.Filters, active: false));
        var result = await host.Source.ExecuteAsync(host.Request(Catalogs), Ct);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<FormAuthoringCatalogsPayload>(result.Value.Payload);
        var campos = payload.Catalogs[0];
        Assert.Equal((id, "Campos", "manual", "text", true), (campos.Id, campos.Name, campos.Kind, campos.ValueType, campos.IsActive));
        Assert.Equal([new FormAuthoringCatalogFieldRow("Superficie", "decimal"), new FormAuthoringCatalogFieldRow("Zona", "text")], campos.Fields);
        Assert.Equal(("filters", false), (payload.Catalogs[1].Kind, payload.Catalogs[1].IsActive));
        Assert.DoesNotContain("items", Json(payload), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(Forms, BusinessFormsPermissions.CatalogsRead)]
    [InlineData(Catalogs, BusinessFormsPermissions.Read)]
    public async Task A_read_without_its_own_permission_is_forbidden_before_reading(string capability, string otherPermission)
    {
        var host = new AuthoringAssistantFixture([otherPermission]);
        var result = await host.Source.ExecuteAsync(host.Request(capability), Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(0, host.Reader.Reads);
    }

    [Fact]
    public async Task An_inactive_account_cannot_read()
    {
        var host = new AuthoringAssistantFixture(active: false);
        var result = await host.Source.ExecuteAsync(host.Request(Forms), Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(0, host.Reader.Reads);
    }

    [Fact]
    public async Task More_than_a_hundred_forms_mark_the_read_partial()
    {
        var host = new AuthoringAssistantFixture();
        for (var index = 0; index < 101; index++) host.Reader.Definitions.Add(AuthoringAssistantFixture.Definition("Formulario " + index, "F" + index));
        var result = await host.Source.ExecuteAsync(host.Request(Forms), Ct);
        var payload = Assert.IsType<FormAuthoringFormsPayload>(result.Value.Payload);
        Assert.Equal(101, host.Reader.LastTake);
        Assert.Equal("partial", result.Value.State);
        Assert.False(payload.IsComplete);
        Assert.Equal(100, payload.Forms.Count);
    }

    [Fact]
    public async Task A_read_that_exceeds_its_budget_keeps_what_fits_and_stays_partial()
    {
        var host = new AuthoringAssistantFixture();
        var questions = Enumerable.Range(0, 40).Select(index => (new string('x', 150) + index, "longText")).ToArray();
        for (var index = 0; index < 10; index++) host.Reader.Definitions.Add(AuthoringAssistantFixture.Definition("Largo " + index, "L" + index, questions: questions));
        var result = await host.Source.ExecuteAsync(host.Request(Forms), Ct);
        var payload = Assert.IsType<FormAuthoringFormsPayload>(result.Value.Payload);
        Assert.False(payload.IsComplete);
        Assert.InRange(payload.Forms.Count, 1, 9);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(Json(new { state = "partial", payload })) <= 16384);
    }

    [Fact]
    public async Task Reads_take_no_arguments()
    {
        var host = new AuthoringAssistantFixture();
        var result = await host.Source.ExecuteAsync(host.Request(Forms, """{"search":"carga"}"""), Ct);
        Assert.Equal(FormDefinitionErrors.ContentInvalid, result.Error);
        Assert.Equal(0, host.Reader.Reads);
    }

    [Fact]
    public async Task The_owner_codec_reads_back_its_own_output_and_rejects_extra_members()
    {
        var host = new AuthoringAssistantFixture();
        host.Reader.Catalogs.Add(AuthoringAssistantFixture.Catalog(Guid.NewGuid(), "Campos"));
        var result = await host.Source.ExecuteAsync(host.Request(Catalogs), Ct);
        var json = Json(result.Value.Payload as FormAuthoringCatalogsPayload);
        Assert.True(host.Source.ParseOutput(new(Catalogs, 1), json).IsSuccess);
        Assert.True(host.Source.ParseOutput(new(Catalogs, 1), json.Replace("\"isComplete\"", "\"extra\":1,\"isComplete\"", StringComparison.Ordinal)).IsFailure);
        Assert.True(host.Source.ParseOutput(new(Catalogs, 2), json).IsFailure);
        Assert.True(host.Source.ParseOutput(new(Forms, 1), json).IsFailure);
    }
}
