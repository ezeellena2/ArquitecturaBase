using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormEntryServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("anonymous")]
    [InlineData("inactive")]
    [InlineData("noPermission")]
    public async Task Only_an_active_account_that_completes_forms_loads_one(string reason)
    {
        using var fixture = new FormEntryFixture();
        if (reason == "anonymous") fixture.Current.UserId = null;
        else if (reason == "inactive") await fixture.Users.SetActiveAsync(fixture.Actor, false, Ct);
        else fixture.Permissions.Permissions[fixture.Actor] = [BusinessFormsPermissions.RecordsRead];

        var load = await fixture.Service.GetAsync(fixture.Catalogs.DefinitionId, Ct);
        var send = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId, fixture.Send(), Ct);

        var expected = reason == "anonymous" ? FormDefinitionErrors.Unauthorized : FormDefinitionErrors.Forbidden;
        Assert.Equal(expected, load.Error);
        Assert.Equal(expected, send.Error);
        Assert.Empty(fixture.Captures);
    }

    [Theory]
    [InlineData("es", "Personas")]
    [InlineData("en", "People")]
    public async Task The_active_version_is_loaded_in_the_menu_language_it_was_published_in(string culture, string label)
    {
        using var fixture = new FormEntryFixture();
        using var scope = new CultureScope(culture);

        var result = await fixture.Service.GetAsync(fixture.Catalogs.DefinitionId, Ct);

        Assert.Equal(culture, result.Value.Culture);
        Assert.Equal(fixture.Catalogs.Version.Id, result.Value.VersionId);
        Assert.Equal("SIE", result.Value.Prefix);
        Assert.Equal(label, Assert.Single(result.Value.Questions).Label);
    }

    [Theory]
    [InlineData("es", "Datos del lote")]
    [InlineData("en", "Lot data")]
    public async Task The_question_where_a_section_starts_carries_its_title_in_the_content_language(string culture, string title)
    {
        using var fixture = new FormEntryFixture();
        var schema = FormDraftPolicy.Read<FormVersionSchema>(fixture.Catalogs.Version.SchemaJson);
        var second = schema.Questions[0] with { FieldId = Guid.CreateVersion7() };
        fixture.SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, [schema.Questions[0], second],
            [new FormSection(1, new Dictionary<string, string> { ["es"] = "Datos del lote", ["en"] = "Lot data" })]));
        using var scope = new CultureScope(culture);

        var questions = (await fixture.Service.GetAsync(fixture.Catalogs.DefinitionId, Ct)).Value.Questions;

        Assert.Equal([null, title], questions.Select(question => question.Section));
        Assert.DoesNotContain("section", FormDraftPolicy.Serialize(questions[0]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deactivated_form_says_so_and_a_missing_one_is_not_found()
    {
        using var fixture = new FormEntryFixture { Deactivated = true };
        Assert.Equal(FormEntryErrors.Unavailable, (await fixture.Service.GetAsync(fixture.Catalogs.DefinitionId, Ct)).Error);
        fixture.Missing = true;
        Assert.Equal(FormDefinitionErrors.NotFound, (await fixture.Service.GetAsync(fixture.Catalogs.DefinitionId, Ct)).Error);
    }

    [Fact]
    public async Task The_only_option_the_filters_leave_comes_chosen_and_an_empty_required_list_blocks_the_question()
    {
        using var fixture = new FormEntryFixture();
        var empty = Assert.Single((await fixture.Service.GetAsync(fixture.Catalogs.DefinitionId, Ct)).Value.Questions).Catalog!;
        var only = fixture.AddItem("Lote 7", 1, "Soja 1ª");
        var single = Assert.Single((await fixture.Service.GetAsync(fixture.Catalogs.DefinitionId, Ct)).Value.Questions).Catalog!;
        fixture.AddItem("Lote 17", 2);
        var several = Assert.Single((await fixture.Service.GetAsync(fixture.Catalogs.DefinitionId, Ct)).Value.Questions).Catalog!;

        Assert.Equal(FormRecordErrors.CatalogEmpty.Code, empty.Issue);
        Assert.Equal(new FormEntryOptionResponse(only.Id, "Lote 7", "Soja 1ª"), single.OnlyOption);
        Assert.Null(single.Issue);
        Assert.Null(several.OnlyOption);
    }

    [Fact]
    public async Task Options_are_read_page_by_page_with_the_search_and_in_order()
    {
        using var fixture = new FormEntryFixture();
        for (var position = 1; position <= 60; position++) fixture.AddItem("Lote " + position, position);

        var first = await fixture.Service.ListOptionsAsync(fixture.Catalogs.DefinitionId, fixture.Catalogs.Version.Id, fixture.FieldId,
            new() { PageSize = 50 }, Ct);
        var search = await fixture.Service.ListOptionsAsync(fixture.Catalogs.DefinitionId, fixture.Catalogs.Version.Id, fixture.FieldId,
            new() { Search = "  lote 5 ", PageSize = 50 }, Ct);
        var tooLarge = await fixture.Service.ListOptionsAsync(fixture.Catalogs.DefinitionId, fixture.Catalogs.Version.Id, fixture.FieldId,
            new() { PageSize = 51 }, Ct);

        Assert.Equal(60, first.Value.TotalCount);
        Assert.Equal(50, first.Value.Items.Count);
        Assert.Equal("Lote 1", first.Value.Items[0].Title);
        Assert.Equal(["Lote 5", "Lote 50", "Lote 51", "Lote 52", "Lote 53", "Lote 54", "Lote 55", "Lote 56", "Lote 57", "Lote 58", "Lote 59"],
            search.Value.Items.Select(option => option.Title));
        Assert.Equal("lote 5", fixture.ItemQueries[^1].Search);
        Assert.IsType<ValidationError>(tooLarge.Error);
    }

    [Fact]
    public async Task A_list_that_depends_on_another_answer_says_which_one_and_has_no_option_chosen_beforehand()
    {
        using var fixture = new FormEntryFixture(dependsOnAnswer: true);
        var field = fixture.AddItem("El Ombú", 1);
        fixture.AddItem("Lote 2", 2, answer: field);

        var questions = (await fixture.Service.GetAsync(fixture.Catalogs.DefinitionId, Ct)).Value.Questions;

        Assert.Equal([fixture.Catalogs.AnswerFieldId, fixture.FieldId], questions.Select(question => question.FieldId));
        Assert.Null(questions[0].Catalog!.DependsOn);
        var dependent = questions[1].Catalog!;
        Assert.Equal([fixture.Catalogs.AnswerFieldId], dependent.DependsOn);
        Assert.Null(dependent.OnlyOption);
        Assert.Null(dependent.Issue);
    }

    [Fact]
    public async Task The_options_of_a_dependent_list_are_those_of_the_chosen_answer_and_wait_for_it()
    {
        using var fixture = new FormEntryFixture(dependsOnAnswer: true);
        var ombu = fixture.AddItem("El Ombú", 1);
        var esperanza = fixture.AddItem("La Esperanza", 2);
        fixture.AddItem("Lote 2", 3, answer: ombu);
        fixture.AddItem("Lote 5", 4, answer: esperanza);
        fixture.AddItem("Lote 3", 5, answer: ombu);

        var options = await fixture.Service.ListOptionsAsync(fixture.Catalogs.DefinitionId, fixture.Catalogs.Version.Id, fixture.FieldId,
            new() { PageSize = 50, Answers = new Dictionary<Guid, Guid> { [fixture.Catalogs.AnswerFieldId] = ombu.Id } }, Ct);
        var waiting = await fixture.Service.ListOptionsAsync(fixture.Catalogs.DefinitionId, fixture.Catalogs.Version.Id, fixture.FieldId,
            new() { PageSize = 50 }, Ct);
        var invalid = await fixture.Service.ListOptionsAsync(fixture.Catalogs.DefinitionId, fixture.Catalogs.Version.Id, fixture.FieldId,
            new() { PageSize = 50, Answers = new Dictionary<Guid, Guid> { [fixture.Catalogs.AnswerFieldId] = Guid.Empty } }, Ct);

        Assert.Equal(["Lote 2", "Lote 3"], options.Value.Items.Select(option => option.Title));
        Assert.Equal(FormRecordErrors.CatalogDependsOnAnswer, waiting.Error);
        Assert.IsType<ValidationError>(invalid.Error);
    }

    [Fact]
    public async Task A_send_checks_that_the_chosen_option_belongs_to_the_answer_it_depends_on()
    {
        using var fixture = new FormEntryFixture(dependsOnAnswer: true);
        var ombu = fixture.AddItem("El Ombú", 1);
        var esperanza = fixture.AddItem("La Esperanza", 2);
        var lot = fixture.AddItem("Lote 2", 3, answer: ombu);
        var answer = fixture.Catalogs.AnswerFieldId;

        var otherField = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId, fixture.Send(
            new FormEntryAnswer(answer, esperanza.Id.ToString("D"), null), new FormEntryAnswer(fixture.FieldId, lot.Id.ToString("D"), null)), Ct);
        var withoutField = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId, fixture.Send(
            new FormEntryAnswer(fixture.FieldId, lot.Id.ToString("D"), null)), Ct);
        Assert.Empty(fixture.Captures);
        var sent = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId, fixture.Send(
            new FormEntryAnswer(answer, ombu.Id.ToString("D"), null), new FormEntryAnswer(fixture.FieldId, lot.Id.ToString("D"), null)), Ct);

        Assert.All(new[] { otherField, withoutField }, rejected =>
        {
            var error = Assert.IsType<ValidationError>(rejected.Error);
            Assert.Equal(FormRecordErrors.CatalogItemInactive.Code, error.Code);
            Assert.Equal(["answers." + fixture.FieldId.ToString("D")], error.Errors.Keys);
        });
        Assert.True(sent.IsSuccess);
        var offer = Assert.Single(Assert.Single(fixture.Captures).Catalogs!, offer => offer.FieldId == fixture.FieldId);
        Assert.Equal(lot.Id, Assert.Single(offer.Options).Id);
    }

    [Fact]
    public async Task Options_of_a_superseded_version_are_a_conflict()
    {
        using var fixture = new FormEntryFixture { ActiveVersionId = Guid.NewGuid() };

        var result = await fixture.Service.ListOptionsAsync(fixture.Catalogs.DefinitionId, fixture.Catalogs.Version.Id, fixture.FieldId, new(), Ct);

        Assert.Equal(FormEntryErrors.VersionChanged, result.Error);
    }

    [Fact]
    public async Task A_send_seals_an_offer_with_only_the_chosen_option_in_the_content_language()
    {
        using var fixture = new FormEntryFixture();
        using var scope = new CultureScope("en");
        fixture.AddItem("Lote 7", 1);
        var chosen = fixture.AddItem("Lote 17", 2);

        var result = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId,
            fixture.Send(new FormEntryAnswer(fixture.FieldId, chosen.Id.ToString("D"), null)), Ct);

        var capture = Assert.Single(fixture.Captures);
        var offer = Assert.Single(capture.Catalogs!);
        Assert.Equal(chosen.Id, Assert.Single(offer.Options).Id);
        Assert.Equal("People", offer.Label);
        Assert.Equal("en", capture.PresentationCulture);
        Assert.Equal(fixture.Actor, capture.ActorUserId);
        Assert.Equal(chosen.Id.ToString("D"), capture.Answers["field_" + fixture.FieldId.ToString("N")]);
        Assert.Equal(new FormEntrySubmitResponse(fixture.Captured.RecordId!.Value, "SIE-000042", "Received", fixture.Outcome.SubmittedAtUtc), result.Value);
    }

    [Fact]
    public async Task An_option_that_is_no_longer_offered_to_the_person_is_marked_on_its_question_without_a_capture()
    {
        using var fixture = new FormEntryFixture();
        fixture.AddItem("Lote 7", 1);

        var result = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId,
            fixture.Send(new FormEntryAnswer(fixture.FieldId, Guid.NewGuid().ToString("D"), null)), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(FormRecordErrors.CatalogItemInactive.Code, error.Code);
        Assert.Equal(["answers." + fixture.FieldId.ToString("D")], error.Errors.Keys);
        Assert.Empty(fixture.Captures);
    }

    [Fact]
    public async Task A_repeated_send_returns_its_first_result_even_after_a_new_version_was_published()
    {
        using var fixture = new FormEntryFixture { ActiveVersionId = Guid.NewGuid() };
        var item = fixture.AddItem("Lote 7", 1);
        fixture.Existing = new("Completed", Guid.NewGuid(), 7, null);

        var result = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId,
            fixture.Send(new FormEntryAnswer(fixture.FieldId, item.Id.ToString("D"), null)), Ct);

        Assert.Equal("SIE-000007", result.Value.Number);
        Assert.Empty(fixture.Captures);
    }

    [Fact]
    public async Task A_new_send_of_a_superseded_version_or_a_deactivated_form_is_a_conflict()
    {
        using var fixture = new FormEntryFixture { ActiveVersionId = Guid.NewGuid() };
        var item = fixture.AddItem("Lote 7", 1);
        var request = fixture.Send(new FormEntryAnswer(fixture.FieldId, item.Id.ToString("D"), null));

        var superseded = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId, request, Ct);
        fixture.ActiveVersionId = fixture.Catalogs.Version.Id;
        fixture.Deactivated = true;
        var deactivated = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId, request, Ct);

        Assert.Equal(FormEntryErrors.VersionChanged, superseded.Error);
        Assert.Equal(FormEntryErrors.Unavailable, deactivated.Error);
        Assert.Empty(fixture.Captures);
    }

    [Fact]
    public async Task A_response_whose_rules_ask_for_approval_is_already_pending_approval()
    {
        using var fixture = new FormEntryFixture { Outcome = new(new DateTime(2026, 10, 30, 13, 58, 0, DateTimeKind.Utc), "Received", true) };
        var item = fixture.AddItem("Lote 7", 1);

        var result = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId,
            fixture.Send(new FormEntryAnswer(fixture.FieldId, item.Id.ToString("D"), null)), Ct);

        Assert.Equal("PendingApproval", result.Value.State);
    }

    [Fact]
    public async Task A_rejected_file_is_reported_on_its_question()
    {
        using var fixture = new FormEntryFixture();
        var item = fixture.AddItem("Lote 7", 1);
        var photo = Guid.NewGuid();
        fixture.Captured = new StructuredRecordCaptureResponse("Rejected", null, null, FileErrors.TooLarge.Code, photo);

        var result = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId,
            fixture.Send(new FormEntryAnswer(fixture.FieldId, item.Id.ToString("D"), null)), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(FileErrors.TooLarge.Code, error.Code);
        Assert.Equal(["answers." + photo.ToString("D")], error.Errors.Keys);
    }

    [Fact]
    public async Task Answers_to_unknown_questions_or_with_the_wrong_shape_are_rejected_without_a_capture()
    {
        using var fixture = new FormEntryFixture();
        var item = fixture.AddItem("Lote 7", 1);

        var unknown = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId,
            fixture.Send(new FormEntryAnswer(Guid.NewGuid(), "x", null)), Ct);
        var wrongShape = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId,
            fixture.Send(new FormEntryAnswer(fixture.FieldId, null, [item.Id])), Ct);
        var unreadable = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId,
            new SubmitFormEntryRequest(Guid.NewGuid(), fixture.Catalogs.Version.Id, null, []), Ct);

        Assert.Equal(FormRecordErrors.InvalidResponse, unknown.Error);
        Assert.Equal(FormRecordErrors.InvalidResponse, wrongShape.Error);
        Assert.IsType<ValidationError>(unreadable.Error);
        Assert.Empty(fixture.Captures);
    }
}
