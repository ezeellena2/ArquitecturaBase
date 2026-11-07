using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

/// <summary>
/// LD1-A (2026-11-07) in the app: a catalog question that chooses first by a field names it, lists the values its options have
/// after the filters and offers only the options of the value chosen. What is sent and saved is still just the option.
/// </summary>
public sealed class FormEntryGroupByTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("es", "Rubro")]
    [InlineData("en", "Category")]
    public async Task A_question_that_chooses_first_names_the_field_in_the_content_language(string culture, string name)
    {
        using var fixture = new FormEntryFixture(groupBy: "text");
        fixture.AddGrouped("2,4-D 100 %", 1, "Herbicidas");
        fixture.AddGrouped("Cipermetrina 25 %", 2, "Insecticidas");
        using var scope = new CultureScope(culture);

        var catalog = Assert.Single((await fixture.Service.GetAsync(fixture.Catalogs.DefinitionId, Ct)).Value.Questions).Catalog!;

        Assert.Equal(new FormEntryGroupByResponse(fixture.Catalogs.GroupFieldId, name), catalog.GroupBy);
        Assert.Null(catalog.OnlyOption);
        Assert.Null(catalog.Issue);
    }

    [Fact]
    public async Task A_question_without_a_field_to_choose_first_keeps_its_description()
    {
        using var fixture = new FormEntryFixture();
        fixture.AddItem("Lote 7", 1);

        var catalog = Assert.Single((await fixture.Service.GetAsync(fixture.Catalogs.DefinitionId, Ct)).Value.Questions).Catalog!;

        Assert.Null(catalog.GroupBy);
        Assert.DoesNotContain("groupBy", FormDraftPolicy.Serialize(catalog), StringComparison.Ordinal);
        var groups = await fixture.Service.ListGroupsAsync(fixture.Catalogs.DefinitionId, fixture.Catalogs.Version.Id, fixture.FieldId, new(), Ct);
        Assert.Equal(FormEntryErrors.QuestionNotFound, groups.Error);
    }

    [Theory]
    [InlineData("es", "Sin rubro")]
    [InlineData("en", "No category")]
    public async Task Text_values_come_in_alphabetical_order_with_the_options_without_one_last(string culture, string none)
    {
        using var fixture = new FormEntryFixture(groupBy: "text");
        fixture.AddGrouped("Glifosato 66,2 %", 1, "Herbicidas");
        fixture.AddGrouped("Carbendazim 50 %", 2, "Fungicidas");
        fixture.AddGrouped("Sin clasificar", 3, null);
        fixture.AddGrouped("2,4-D 100 %", 4, "Herbicidas");
        fixture.AddGrouped("Cipermetrina 25 %", 5, "insecticidas");
        using var scope = new CultureScope(culture);

        var groups = await fixture.Service.ListGroupsAsync(fixture.Catalogs.DefinitionId, fixture.Catalogs.Version.Id, fixture.FieldId, new(), Ct);

        Assert.Equal([new FormEntryGroupResponse("Fungicidas", "Fungicidas"), new("Herbicidas", "Herbicidas"),
            new("insecticidas", "insecticidas"), new(null, none)], groups.Value.Groups);
        Assert.Equal(fixture.Catalogs.GroupFieldId, Assert.Single(fixture.GroupQueries).FieldId);
    }

    [Theory]
    [InlineData("es", "Herbicidas", "Insecticidas")]
    [InlineData("en", "Herbicides", "Insecticides")]
    public async Task Catalog_values_follow_the_order_and_texts_of_their_source(string culture, string first, string second)
    {
        using var fixture = new FormEntryFixture(groupBy: "catalog");
        var insecticides = fixture.AddSource("Insecticidas", "Insecticides", 2);
        var herbicides = fixture.AddSource("Herbicidas", "Herbicides", 1);
        fixture.AddSource("Fungicidas", "Fungicides", 3);
        fixture.AddGrouped("Cipermetrina 25 %", 1, insecticides.Id.ToString("D"));
        fixture.AddGrouped("2,4-D 100 %", 2, herbicides.Id.ToString("D"));
        using var scope = new CultureScope(culture);

        var groups = await fixture.Service.ListGroupsAsync(fixture.Catalogs.DefinitionId, fixture.Catalogs.Version.Id, fixture.FieldId, new(), Ct);

        Assert.Equal([new FormEntryGroupResponse(herbicides.Id.ToString("D"), first), new(insecticides.Id.ToString("D"), second)], groups.Value.Groups);
    }

    [Fact]
    public async Task Options_are_those_of_the_chosen_value_or_of_none_and_the_search_stays_inside_it()
    {
        using var fixture = new FormEntryFixture(groupBy: "text");
        fixture.AddGrouped("2,4-D 100 %", 1, "Herbicidas");
        fixture.AddGrouped("Cipermetrina 25 %", 2, "Insecticidas");
        fixture.AddGrouped("Glifosato 66,2 %", 3, "Herbicidas");
        fixture.AddGrouped("Sin clasificar", 4, null);
        var path = (Definition: fixture.Catalogs.DefinitionId, Version: fixture.Catalogs.Version.Id, Field: fixture.FieldId);

        var herbicides = await fixture.Service.ListOptionsAsync(path.Definition, path.Version, path.Field, new() { PageSize = 50, Group = "Herbicidas" }, Ct);
        var searched = await fixture.Service.ListOptionsAsync(path.Definition, path.Version, path.Field,
            new() { PageSize = 50, Group = "Herbicidas", Search = "glifo" }, Ct);
        var none = await fixture.Service.ListOptionsAsync(path.Definition, path.Version, path.Field, new() { PageSize = 50, WithoutGroup = true }, Ct);
        var all = await fixture.Service.ListOptionsAsync(path.Definition, path.Version, path.Field, new() { PageSize = 50 }, Ct);

        Assert.Equal(["2,4-D 100 %", "Glifosato 66,2 %"], herbicides.Value.Items.Select(option => option.Title));
        Assert.Equal(["Glifosato 66,2 %"], searched.Value.Items.Select(option => option.Title));
        Assert.Equal(["Sin clasificar"], none.Value.Items.Select(option => option.Title));
        Assert.Equal(4, all.Value.TotalCount);
        Assert.Equal(fixture.Catalogs.GroupFieldId.ToString("D"), fixture.ItemQueries[2].WithoutField);
    }

    [Fact]
    public async Task A_send_takes_any_option_of_the_question_whatever_value_was_chosen_first()
    {
        using var fixture = new FormEntryFixture(groupBy: "text");
        var herbicide = fixture.AddGrouped("2,4-D 100 %", 1, "Herbicidas");

        var sent = await fixture.Service.SubmitAsync(fixture.Catalogs.DefinitionId,
            fixture.Send(new FormEntryAnswer(fixture.FieldId, herbicide.Id.ToString("D"), null)), Ct);

        Assert.True(sent.IsSuccess);
        Assert.Equal(herbicide.Id, Assert.Single(Assert.Single(Assert.Single(fixture.Captures).Catalogs!).Options).Id);
    }

    [Theory]
    [InlineData("both")]
    [InlineData("long")]
    public async Task A_value_and_none_at_once_or_a_value_too_long_is_rejected(string problem)
    {
        using var fixture = new FormEntryFixture(groupBy: "text");
        var request = problem == "both" ? new ListFormEntryOptionsRequest { Group = "Herbicidas", WithoutGroup = true }
            : new ListFormEntryOptionsRequest { Group = new string('x', 301) };

        var result = await fixture.Service.ListOptionsAsync(fixture.Catalogs.DefinitionId, fixture.Catalogs.Version.Id, fixture.FieldId, request, Ct);

        Assert.IsType<ValidationError>(result.Error);
        Assert.Empty(fixture.ItemQueries);
    }

    // A correction (CW10-A) starts the first list with the value the saved option has now, or with the options without one.
    [Theory]
    [InlineData("Herbicidas", "Herbicidas")]
    [InlineData(null, "Sin rubro")]
    public async Task A_correction_starts_with_the_value_of_the_saved_option(string? group, string text)
    {
        using var fixture = new FormEntryFixture(groupBy: "text");
        var saved = fixture.AddGrouped("2,4-D 100 %", 1, group);
        var recordId = Saved(fixture, saved);

        var answer = Assert.Single((await fixture.Service.GetCorrectionAsync(fixture.Catalogs.DefinitionId, recordId, Ct)).Value.Answers);

        Assert.Equal(new FormEntryGroupResponse(group, text), answer.Group);
        Assert.Equal(saved.Id, Assert.Single(answer.Options!).Id);
    }

    [Fact]
    public async Task A_correction_of_a_question_without_a_field_to_choose_first_says_no_value()
    {
        using var fixture = new FormEntryFixture();
        var recordId = Saved(fixture, fixture.AddItem("Lote 7", 1));

        var answer = Assert.Single((await fixture.Service.GetCorrectionAsync(fixture.Catalogs.DefinitionId, recordId, Ct)).Value.Answers);

        Assert.Null(answer.Group);
        Assert.DoesNotContain("group", FormDraftPolicy.Serialize(answer), StringComparison.Ordinal);
    }

    // A response of whoever loads with the given option in the question of the version, as a correction opens it.
    private static Guid Saved(FormEntryFixture fixture, BusinessFormCatalogItem option)
    {
        var recordId = Guid.NewGuid();
        var values = JsonSerializer.Serialize(new[] { new { fieldId = fixture.FieldId, value = option.Id } }, JsonSerializerOptions.Web);
        fixture.Responses.ExportRows = [new(new(Guid.NewGuid(), recordId, 7, fixture.Catalogs.DefinitionId, "SIE", fixture.Catalogs.Version.Id, 1,
            """{"es":"Pedido"}""", fixture.Catalogs.Version.SchemaJson!, values, [new(recordId, fixture.FieldId, fixture.Catalogs.Catalog.Id, 1, option.Id, option.TextEs!, null, "Artículos")]),
            new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc), new(fixture.Actor, "Autor"))];
        return recordId;
    }
}
