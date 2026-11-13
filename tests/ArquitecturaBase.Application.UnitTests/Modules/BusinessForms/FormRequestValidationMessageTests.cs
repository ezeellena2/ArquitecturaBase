using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using FluentValidation;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Each rule of a Forms request answers with a text of the project, the error of its area or a common one, never with the one
// FluentValidation brings (in Spanish from Spain and with the name of the property in English). The errors are the ones the
// person gets: one entry per field, without repeating a text.
public sealed class FormRequestValidationMessageTests
{
    private const string Required = "Este campo es obligatorio.";
    private const string Approval = "Revisá la decisión de aprobación.";
    private const string Filters = "Revisá los filtros: el campo, el operador y el valor deben corresponder a ese formulario.";
    private const string Response = "La respuesta no coincide con el formulario publicado.";
    private const string Pdf = "Revisá el diseño del PDF.";
    private const string Rules = "Revisá las condiciones y las acciones de la regla.";
    private const string Suggestions = "Revisá las condiciones y los datos de la sugerencia.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public Task The_approvals_of_an_empty_form_say_it_is_required() =>
        AssertErrors(new ListFormApprovalsRequest { DefinitionId = Guid.Empty }, new ListFormApprovalsRequestValidator(),
            new() { ["definitionId"] = [Required] });

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(9007199254740991L)]
    public Task A_decision_without_a_valid_revision_says_to_check_the_decision(long? revision) =>
        AssertErrors(new DecideFormApprovalRequest(revision, "Approved"), new DecideFormApprovalRequestValidator(),
            new() { ["expectedRevision"] = [Approval] });

    [Fact]
    public Task The_next_approval_without_its_form_or_with_a_page_size_out_of_range_says_so() =>
        AssertErrors(new FormApprovalContinuationRequest(Guid.Empty, "Next", 0), new FormApprovalContinuationRequestValidator(),
            new() { ["definitionId"] = [Required], ["pageSize"] = ["La cantidad por página debe estar entre 1 y 100."] });

    [Fact]
    public Task A_dashboard_without_its_filter_says_to_check_the_filters() =>
        AssertErrors(new FormDashboardRequest { Filter = null! }, new FormDashboardRequestValidator(), new() { ["filter"] = [Filters] });

    [Fact]
    public Task A_send_without_its_files_says_the_response_does_not_match() =>
        AssertErrors(new SubmitFormEntryRequest(Guid.NewGuid(), Guid.NewGuid(), [], null), new SubmitFormEntryRequestValidator(),
            new() { ["files"] = [Response] });

    [Fact]
    public Task A_correction_without_its_files_says_the_response_does_not_match() =>
        AssertErrors(new CorrectFormEntryRequest(Guid.NewGuid(), Guid.NewGuid(), [], null, "Typo", null), new CorrectFormEntryRequestValidator(),
            new() { ["files"] = [Response], ["keptFileIds"] = [Response] });

    [Fact]
    public Task An_export_without_its_filter_or_with_wrong_columns_says_to_check_the_filters() =>
        AssertErrors(new FormExportRequest(null!, [Guid.Empty], [null], [" "]), new FormExportRequestValidator(),
            new() { ["filter"] = [Filters], ["columns"] = [Filters], ["catalogFields"] = [Filters], ["resultColumns"] = [Filters] });

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    [InlineData(9007199254740991L)]
    public Task A_pdf_design_without_a_valid_revision_or_its_design_says_to_check_the_design(long? revision) =>
        AssertErrors(new SaveFormPdfDesignRequest(revision, null), new SaveFormPdfDesignRequestValidator(),
            new() { ["expectedRevision"] = [Pdf], ["design"] = [Pdf] });

    [Fact]
    public Task A_pdf_preview_without_its_design_or_in_another_language_says_so() =>
        AssertErrors(new PreviewFormPdfDesignRequest(null, "fr"), new PreviewFormPdfDesignRequestValidator(),
            new() { ["design"] = [Pdf], ["culture"] = ["Elegí es o en para la vista previa."] });

    [Fact]
    public Task A_response_list_without_its_filters_says_to_check_them() =>
        AssertErrors(new ListFormRecordsRequest { Conditions = null!, VersionIds = null!, ActorIds = null! }, new ListFormRecordsRequestValidator(),
            new() { ["conditions"] = [Filters], ["versionIds"] = [Filters], ["actorIds"] = [Filters] });

    [Fact]
    public Task A_response_list_with_more_than_100_versions_says_to_check_the_filters() =>
        AssertErrors(new ListFormRecordsRequest { VersionIds = [.. Enumerable.Range(0, 101).Select(_ => Guid.NewGuid())] },
            new ListFormRecordsRequestValidator(), new() { ["versionIds"] = [Filters] });

    [Fact]
    public Task A_facet_without_its_filter_says_to_check_the_filters() =>
        AssertErrors(new FormRecordFacetRequest { Filter = null! }, new FormRecordFacetRequestValidator(), new() { ["filter"] = [Filters] });

    [Theory]
    [InlineData(null, false)]
    [InlineData(-1L, true)]
    public Task Rules_without_a_valid_revision_or_list_say_to_check_the_rules(long? revision, bool tooMany) =>
        AssertErrors(new SaveFormRulesRequest(revision, tooMany ? new FormRuleInput?[21] : null), new SaveFormRulesRequestValidator(),
            new() { ["expectedRevision"] = [Rules], ["rules"] = [Rules] });

    [Theory]
    [InlineData(null, false)]
    [InlineData(-1L, true)]
    public Task Suggestions_without_a_valid_revision_or_list_say_to_check_the_suggestions(long? revision, bool tooMany) =>
        AssertErrors(new SaveFormSuggestionsRequest(revision, tooMany ? new FormSuggestionInput?[21] : null), new SaveFormSuggestionsRequestValidator(),
            new() { ["expectedRevision"] = [Suggestions], ["suggestions"] = [Suggestions] });

    [Fact]
    public Task The_error_of_an_area_comes_in_the_language_of_the_request() =>
        AssertErrors(new SaveFormRulesRequest(null, null), new SaveFormRulesRequestValidator(),
            new() { ["expectedRevision"] = ["Check the rule conditions and actions."], ["rules"] = ["Check the rule conditions and actions."] }, "en");

    private static async Task AssertErrors<TRequest>(
        TRequest request, IValidator<TRequest> validator, Dictionary<string, string[]> expected, string language = "es")
    {
        using var culture = new CultureScope(language);

        var error = await RequestValidator.ValidateAsync(request, [validator], Ct);

        Assert.Equal(expected, error?.Errors);
    }
}
