using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogMultipleOfferTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, "es", 20)]
    [InlineData(false, "es", 21)]
    [InlineData(false, "en", 20)]
    [InlineData(false, "en", 21)]
    [InlineData(true, "es", 20)]
    [InlineData(true, "es", 21)]
    [InlineData(true, "en", 20)]
    [InlineData(true, "en", 21)]
    public async Task Multiple_selection_keeps_twenty_options_or_blocks_the_entire_real_offer(bool filtered, string culture, int count)
    {
        var f = Fixture(filtered);
        Populate(f, filtered, count);
        var source = SourceIds(f, filtered);
        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), culture, f.DefinitionId, f.Version.Id, true, Ct);
        if (count <= 20)
        {
            Assert.True(result.CanSend);
            Assert.Equal(source, Assert.Single(result.Catalogs).Options.Select(option => option.Id));
        }
        else Blocked(f, result, "Forms.Record.CatalogTooLargeForMultiple");
        Assert.Equal(source, SourceIds(f, filtered));
        Assert.Equal([f.Catalog.Id], f.Locks);
    }

    [Theory]
    [InlineData(false, 200)]
    [InlineData(false, 201)]
    [InlineData(true, 200)]
    [InlineData(true, 201)]
    public async Task Single_choice_retains_its_two_hundred_option_limit(bool filtered, int count)
    {
        var f = new FormCatalogPreparationFixture(filtered: filtered);
        Populate(f, filtered, count);
        var source = SourceIds(f, filtered);
        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "es", f.DefinitionId, f.Version.Id, false, Ct);
        if (count <= 200)
        {
            Assert.True(result.CanSend);
            Assert.Equal(source, Assert.Single(result.Catalogs).Options.Select(option => option.Id));
        }
        else Blocked(f, result, "Forms.Record.CatalogTooLarge");
        Assert.Equal(source, SourceIds(f, filtered));
        Assert.Empty(f.Locks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_result_beyond_the_existing_reader_sentinel_still_uses_the_multiple_selection_error(bool filtered)
    {
        var f = Fixture(filtered);
        Populate(f, filtered, 201);
        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "es", f.DefinitionId, f.Version.Id, true, Ct);
        Blocked(f, result, "Forms.Record.CatalogTooLargeForMultiple");
        Assert.Equal(filtered ? 0 : 201, SourceIds(f, filtered).Length);
        if (filtered) Assert.True(f.Preview.ExceedsLimit);
    }

    [Theory]
    [InlineData(false, "es", 2)]
    [InlineData(false, "es", 3)]
    [InlineData(false, "en", 2)]
    [InlineData(false, "en", 3)]
    [InlineData(true, "es", 2)]
    [InlineData(true, "es", 3)]
    [InlineData(true, "en", 2)]
    [InlineData(true, "en", 3)]
    public async Task The_real_offer_must_have_enough_options_to_reach_the_published_minimum(bool filtered, string culture, int count)
    {
        var f = Fixture(filtered, minimum: 3);
        Populate(f, filtered, count);
        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), culture, f.DefinitionId, f.Version.Id, true, Ct);
        if (count == 3)
        {
            Assert.True(result.CanSend);
            Assert.Equal(SourceIds(f, filtered), Assert.Single(result.Catalogs).Options.Select(option => option.Id));
        }
        else Blocked(f, result, "Forms.Record.CatalogInsufficientOptions");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Empty_optional_offers_and_the_required_empty_error_preserve_the_existing_policy(bool filtered, bool required)
    {
        var f = Fixture(filtered, minimum: required ? 1 : 0);
        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "es", f.DefinitionId, f.Version.Id, true, Ct);
        if (required) Blocked(f, result, "Forms.Record.CatalogEmpty");
        else
        {
            Assert.True(result.CanSend);
            Assert.Empty(Assert.Single(result.Catalogs).Options);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_offer_that_shrinks_after_preview_is_blocked_when_prepared_for_sending(bool filtered)
    {
        var f = Fixture(filtered, minimum: 3);
        Populate(f, filtered, 3);
        var preview = await f.Policy.PrepareAsync(Guid.NewGuid(), "es", f.DefinitionId, f.Version.Id, false, Ct);
        Assert.True(preview.CanSend);
        Assert.Equal(3, Assert.Single(preview.Catalogs).Options.Count);
        Assert.Empty(f.Locks);
        if (filtered) f.Preview = new(f.Preview.Items.Take(2).ToArray(), 2, false);
        else Assert.True(f.Catalog.SetManualItemActive(f.Items[^1], false).IsSuccess);
        var sent = await f.Policy.PrepareAsync(Guid.NewGuid(), "es", f.DefinitionId, f.Version.Id, true, Ct);
        Blocked(f, sent, "Forms.Record.CatalogInsufficientOptions");
        Assert.Equal([f.Catalog.Id], f.Locks);
        Assert.Equal(3, Assert.Single(preview.Catalogs).Options.Count);
    }

    [Fact]
    public async Task Inactive_manual_rows_are_not_counted_as_real_options()
    {
        var f = Fixture(false);
        Populate(f, false, 21);
        Assert.True(f.Catalog.SetManualItemActive(f.Items[^1], false).IsSuccess);
        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "es", f.DefinitionId, f.Version.Id, true, Ct);
        Assert.True(result.CanSend);
        Assert.Equal(f.Items.Take(20).Select(item => item.Id), Assert.Single(result.Catalogs).Options.Select(option => option.Id));
        Assert.Equal(21, f.Items.Count);
    }

    [Theory]
    [InlineData(BusinessFormCatalogValueType.Integer, "1500")]
    [InlineData(BusinessFormCatalogValueType.Decimal, "1500.5")]
    [InlineData(BusinessFormCatalogValueType.Date, "2026-10-22")]
    public async Task Twenty_typed_options_keep_their_sealed_value_and_visible_text(BusinessFormCatalogValueType type, string value)
    {
        var f = Fixture(false, valueType: type);
        Populate(f, false, 20, value);
        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "en", f.DefinitionId, f.Version.Id, true, Ct);
        Assert.True(result.CanSend);
        var offer = Assert.Single(result.Catalogs);
        Assert.Equal(type.ToString(), offer.ValueType);
        Assert.Equal(f.Items.Select(item => item.Id), offer.Options.Select(option => option.Id));
        Assert.Equal(f.Items.Select(item => item.TextEn), offer.Options.Select(option => option.Text));
        Assert.All(offer.Options, option => Assert.Equal(value, option.Value));
    }

    [Fact]
    public async Task The_same_source_is_not_truncated_to_satisfy_a_multiple_question_beside_a_single_question()
    {
        var f = Fixture(false);
        Populate(f, false, 200);
        var field = Assert.Single(FormDraftPolicy.Read<FormVersionSchema>(f.Version.SchemaJson).Questions);
        var singleId = Guid.CreateVersion7();
        var single = field with { FieldId = singleId, Key = "field_" + singleId.ToString("N"), Type = "choice", Selection = null };
        var schema = new FormVersionSchema(1, [single, field]);
        var result = await new FormCatalogOptionsPolicy(f, f).PrepareAsync(Guid.NewGuid(), "es", schema, "es", Ct);
        Assert.False(result.CanSend);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(field.FieldId, issue.FieldId);
        Assert.Equal("Forms.Record.CatalogTooLargeForMultiple", issue.Code);
        var offer = Assert.Single(result.Catalogs);
        Assert.Equal(singleId, offer.FieldId);
        Assert.Equal(f.Items.Select(item => item.Id), offer.Options.Select(option => option.Id));
        Assert.Equal(200, f.Items.Count);
    }

    private static FormCatalogPreparationFixture Fixture(bool filtered, int minimum = 1,
        BusinessFormCatalogValueType valueType = BusinessFormCatalogValueType.Text) =>
        new(filtered: filtered, required: minimum > 0, valueType: valueType, questionType: "multiChoice", selection: new(minimum, 20));

    private static void Populate(FormCatalogPreparationFixture f, bool filtered, int count, string? value = null)
    {
        if (filtered)
        {
            f.Preview = count > 200 ? new([], 201, true) : new(Enumerable.Range(1, count)
                .Select(index => new FormCatalogFilterOptionResponse(Guid.CreateVersion7(), "Option " + index, null)).ToArray(), count, false);
        }
        else
            f.Items.AddRange(Enumerable.Range(1, count).Select(index => BusinessFormCatalogItem.Create(f.Catalog.Id,
                "C" + index, "Opción " + index, "Option " + index, null, null, index, value)));
    }

    private static Guid[] SourceIds(FormCatalogPreparationFixture f, bool filtered) => filtered
        ? f.Preview.Items.Select(item => item.Id).ToArray() : f.Items.Where(item => item.IsActive).Select(item => item.Id).ToArray();

    private static void Blocked(FormCatalogPreparationFixture f,
        ArquitecturaBase.Application.Models.StructuredData.StructuredFormPreparationResponse response, string code)
    {
        Assert.False(response.CanSend);
        Assert.Empty(response.Catalogs);
        var issue = Assert.Single(response.Issues);
        Assert.Equal(f.FieldId, issue.FieldId);
        Assert.Equal(code, issue.Code);
    }
}
