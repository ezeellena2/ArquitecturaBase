using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

/// <summary>
/// LD2-A (2026-11-07): a catalog question that chooses first by a field is offered to a channel with the value of each option and
/// the values in order, as the app lists them, so the channel can show the values first and then only the options of the one
/// chosen. The limit of 200 is for each value and for the list of values, not for the total.
/// </summary>
public sealed class FormCatalogGroupedOfferTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("es", "Rubro", "Sin rubro")]
    [InlineData("en", "Category", "No category")]
    public async Task Each_option_carries_its_value_and_the_values_come_in_order_with_those_without_one_last(string culture, string label, string none)
    {
        var f = new FormCatalogPreparationFixture(groupByType: "text");
        var glyphosate = Add(f, "Glifosato 66,2 %", 1, "Herbicidas");
        var cypermethrin = Add(f, "Cipermetrina 25 %", 2, "Insecticidas");
        var unclassified = Add(f, "Sin clasificar", 3, null);
        var dicamba = Add(f, "Dicamba 57,7 %", 4, "Herbicidas");

        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), culture, f.DefinitionId, f.Version.Id, true, Ct);

        var offer = Assert.Single(result.Catalogs);
        Assert.Equal(label, offer.Grouping!.Label);
        Assert.Equal([new StructuredFormCatalogGroup("Herbicidas", "Herbicidas"), new("Insecticidas", "Insecticidas"), new(null, none)], offer.Grouping.Groups);
        Assert.Equal([(glyphosate.Id, "Herbicidas"), (cypermethrin.Id, "Insecticidas"), (unclassified.Id, null), (dicamba.Id, "Herbicidas")],
            offer.Options.Select(option => (option.Id, option.Group)));
    }

    [Fact]
    public async Task A_question_that_does_not_choose_first_keeps_its_offer_as_it_was()
    {
        var f = new FormCatalogPreparationFixture();
        f.Items.Add(BusinessFormCatalogItem.Create(f.Catalog.Id, "L7", "Lote 7", null, null, null, 1));

        var offer = Assert.Single((await f.Policy.PrepareAsync(Guid.NewGuid(), "es", f.DefinitionId, f.Version.Id, true, Ct)).Catalogs);

        Assert.Null(offer.Grouping);
        Assert.Null(Assert.Single(offer.Options).Group);
        Assert.DoesNotContain("group", FormDraftPolicy.Serialize(offer), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(150, 150, 1, null)]
    [InlineData(201, 1, 1, "Forms.Record.CatalogTooLarge")]
    [InlineData(1, 1, 199, "Forms.Record.CatalogTooLarge")]
    public async Task The_limit_of_two_hundred_is_for_each_value_and_for_the_list_of_values(int herbicides, int insecticides, int others, string? issue)
    {
        var f = new FormCatalogPreparationFixture(groupByType: "text");
        var position = 0;
        for (var index = 0; index < herbicides; index++) Add(f, "Herbicida " + index, ++position, "Herbicidas");
        for (var index = 0; index < insecticides; index++) Add(f, "Insecticida " + index, ++position, "Insecticidas");
        // Each of the others is a value of its own: with 199 of them the list holds 201 values.
        for (var index = 0; index < others; index++) Add(f, "Otro " + index, ++position, "Otro " + index);

        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "es", f.DefinitionId, f.Version.Id, true, Ct);

        Assert.Equal(issue, result.Issues.SingleOrDefault()?.Code);
        if (issue is null) Assert.Equal(herbicides + insecticides + others, Assert.Single(result.Catalogs).Options.Count);
    }

    [Fact]
    public async Task Without_its_field_the_question_cannot_be_prepared()
    {
        var f = new FormCatalogPreparationFixture(groupByType: "text");
        Add(f, "Glifosato 66,2 %", 1, "Herbicidas");
        Assert.True(f.Catalog.SetFields([new(f.GroupFieldId, "rubro", FormCatalogPreparationFixture.GroupNames, "text", null, false, false, null, null, 300, "data", 0, false)],
            null).IsSuccess);

        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "es", f.DefinitionId, f.Version.Id, true, Ct);

        Assert.Equal(FormRecordErrors.CatalogFilterUnavailable.Code, Assert.Single(result.Issues).Code);
    }

    // VO1-A: the official preview takes the first values, each with its first options, as it takes the first options of a list.
    [Fact]
    public async Task The_official_preview_offers_the_first_values_each_with_its_first_options()
    {
        var f = new FormCatalogPreparationFixture(groupByType: "text");
        var position = 0;
        for (var group = 0; group < 22; group++)
            for (var index = 0; index < 22; index++) Add(f, $"Opción {group}-{index}", ++position, "Valor " + group.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        var schema = FormDraftPolicy.Read<ArquitecturaBase.Application.Modules.BusinessForms.Models.FormVersionSchema>(f.Version.SchemaJson!);

        var offer = Assert.Single(await new FormCatalogOptionsPolicy(f, f, new FormCatalogPersonReader()).PreviewAsync(Guid.NewGuid(), "es", schema, "es", Ct));

        Assert.Equal(FormCatalogOptionsPolicy.PreviewOptionLimit, offer.Grouping!.Groups.Count);
        Assert.Equal("Valor 00", offer.Grouping.Groups[0].Value);
        Assert.All(offer.Grouping.Groups, group => Assert.Equal(FormCatalogOptionsPolicy.PreviewOptionLimit, offer.Options.Count(option => option.Group == group.Value)));
        Assert.Equal(400, offer.Options.Count);
    }

    [Fact]
    public void A_grouped_offer_is_valid_with_more_than_two_hundred_options_in_total()
    {
        var options = Enumerable.Range(0, 300).Select(index => new StructuredFormCatalogOption(Guid.NewGuid(), "Opción " + index, null, Group: index < 150 ? "A" : "B")).ToArray();
        var grouping = new StructuredFormCatalogGrouping("Rubro", [new("A", "A"), new("B", "B")]);

        Assert.Null(FormCatalogOfferPolicy.Validate(true, options, grouping: grouping));
        Assert.Equal(FormRecordErrors.CatalogTooLarge.Code, FormCatalogOfferPolicy.Validate(true, options));
        // An option whose value is not among the values does not belong to the offer.
        Assert.Equal(FormRecordErrors.CatalogOptionInvalid.Code, FormCatalogOfferPolicy.Validate(true, options, grouping: grouping with { Groups = [new("A", "A")] }));
    }

    // LD1-A: an option whose field to choose first holds the given text, or nothing.
    private static BusinessFormCatalogItem Add(FormCatalogPreparationFixture f, string text, int position, string? group)
    {
        var item = BusinessFormCatalogItem.Create(f.Catalog.Id, "G" + position, text, null, null, null, position);
        var values = group is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [f.GroupFieldId.ToString("D")] = group };
        Assert.True(f.Catalog.SetManualItemFields(item, FormDraftPolicy.Serialize(values)).IsSuccess);
        f.Items.Add(item);
        return item;
    }
}
