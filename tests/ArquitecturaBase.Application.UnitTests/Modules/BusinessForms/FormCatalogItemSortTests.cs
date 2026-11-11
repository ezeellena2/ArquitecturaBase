using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogItemSortTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Sorting_by_text_orders_the_complete_catalog_by_its_original_text_and_preserves_values(string culture)
    {
        using var scope = new CultureScope(culture);
        var catalog = Catalog();
        var zulu = catalog.AddManualItem("Z", "Zulú", "A", null, null, 1, "0.5").Value;
        var alamo = catalog.AddManualItem("A", "Álamo", "Z", null, null, 501, "1.5").Value;
        var inactive = catalog.AddManualItem("B", "Bajo", "Y", null, null, 1000, "2.5").Value;
        Assert.True(catalog.SetManualItemActive(inactive, false).IsSuccess);
        var repository = new FormCatalogCapacityRepository(catalog, [zulu, alamo, inactive]);
        var work = new FakeUnitOfWork();
        var result = await Service(repository, work).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [new("new", null, "C", Texts("Campo"), null, true, "3.5")], Sort: new("text")), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "A", "B", "C", "Z" }, Codes(repository));
        Assert.Equal(new[] { 1, 2, 3, 4 }, repository.Items.OrderBy(item => item.Position).Select(item => item.Position));
        Assert.Equal("0.5", zulu.Value);
        Assert.Equal("1.5", alamo.Value);
        Assert.Equal("2.5", inactive.Value);
        Assert.False(inactive.IsActive);
        Assert.Equal("3.5", repository.Items.Single(item => item.Code == "C").Value);
        Assert.Equal(2, catalog.Revision);
        Assert.Equal(1, work.Commits);
    }

    [Fact]
    public async Task Equal_original_texts_keep_the_existing_position_and_id_tiebreaker()
    {
        var catalog = Catalog();
        var later = catalog.AddManualItem("B", "Igual", null, null, null, 8, "1").Value;
        var earlier = catalog.AddManualItem("A", "Igual", null, null, null, 3, "2").Value;
        var repository = new FormCatalogCapacityRepository(catalog, [later, earlier]);
        var work = new FakeUnitOfWork();
        var result = await Service(repository, work).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [], Moves: [], Sort: new("text")), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, earlier.Position);
        Assert.Equal(2, later.Position);
    }

    [Fact]
    public async Task Sorting_an_English_original_catalog_by_text_uses_its_English_texts()
    {
        var catalog = BusinessFormCatalog.CreateManual("en", new Dictionary<string, string> { ["en"] = "Own" },
            new Dictionary<string, string>(), BusinessFormCatalogValueType.Decimal);
        var celery = catalog.AddManualItem("A", "Apio", "Celery", null, null, 1, "1").Value;
        var carrot = catalog.AddManualItem("B", "Zanahoria", "Carrot", null, null, 2, "2").Value;
        var repository = new FormCatalogCapacityRepository(catalog, [celery, carrot]);
        var work = new FakeUnitOfWork();

        var result = await Service(repository, work).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [], Moves: [], Sort: new("text")), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, carrot.Position);
        Assert.Equal(2, celery.Position);
    }

    [Theory]
    [InlineData(false, "117 245 10030 13007 HER-2 HER-10")]
    [InlineData(true, "HER-10 HER-2 13007 10030 245 117")]
    public async Task Sorting_by_code_reads_the_numbers_inside_a_code_by_their_value(bool descending, string expected)
    {
        var catalog = Catalog();
        var items = new[] { "10030", "HER-10", "117", "13007", "HER-2", "245" }
            .Select((code, index) => catalog.AddManualItem(code, "Opción " + code, null, null, null, index + 1, "1").Value).ToList();
        var repository = new FormCatalogCapacityRepository(catalog, items);

        var result = await Service(repository, new FakeUnitOfWork()).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [], Sort: new("code", descending)), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected.Split(' '), Codes(repository));
    }

    [Theory]
    [InlineData(false, "C1 C4 C6 C2 C5 C3")]
    [InlineData(true, "C2 C5 C6 C1 C4 C3")]
    public async Task Sorting_by_a_field_keeps_the_current_order_among_ties_and_leaves_empty_values_last(bool descending, string expected)
    {
        var (catalog, field, items) = WithField("text", "Electricidad", "Pinturas", null, "Electricidad", "Pinturas", "Ferretería");
        var repository = new FormCatalogCapacityRepository(catalog, items);

        var result = await Service(repository, new FakeUnitOfWork()).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [], Sort: new(field.Id.ToString("D"), descending)), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected.Split(' '), Codes(repository));
    }

    [Theory]
    [InlineData("integer", "9|10|-1||10", false, "C3 C1 C2 C5 C4")]
    [InlineData("integer", "9|10|-1||10", true, "C2 C5 C1 C3 C4")]
    [InlineData("decimal", "9.5|10|-0.25||10", false, "C3 C1 C2 C5 C4")]
    [InlineData("date", "2026-03-01|2026-11-10|2025-12-31||2026-11-10", false, "C3 C1 C2 C5 C4")]
    [InlineData("date", "2026-03-01|2026-11-10|2025-12-31||2026-11-10", true, "C2 C5 C1 C3 C4")]
    [InlineData("boolean", "false|true|false||true", false, "C1 C3 C2 C5 C4")]
    [InlineData("boolean", "false|true|false||true", true, "C2 C5 C1 C3 C4")]
    public async Task Sorting_by_a_typed_field_compares_its_values_in_their_type(string type, string values, bool descending, string expected)
    {
        var (catalog, field, items) = WithField(type, values.Split('|').Select(value => value.Length == 0 ? null : value).ToArray());
        var repository = new FormCatalogCapacityRepository(catalog, items);

        var result = await Service(repository, new FakeUnitOfWork()).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [], Sort: new(field.Id.ToString("D"), descending)), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected.Split(' '), Codes(repository));
    }

    [Fact]
    public async Task Sorting_by_a_field_of_another_catalog_follows_the_text_of_its_option()
    {
        var source = Catalog();
        var zeta = source.AddManualItem("Z", "Zeta", null, null, null, 1, "1").Value;
        var alfa = source.AddManualItem("A", "Alfa", null, null, null, 2, "1").Value;
        var media = source.AddManualItem("M", "Media", null, null, null, 3, "1").Value;
        var (catalog, field, items) = WithField("catalog", source.Id, zeta.Id.ToString("D"), alfa.Id.ToString("D"), media.Id.ToString("D"), null);
        var reader = new FormCatalogFieldReader();
        reader.Catalogs[source.Id] = Row(source);
        reader.Items[source.Id] = [.. new[] { zeta, alfa, media }.Select(item => new FormCatalogStoredItemRow(item.Id, source.Id, item.Code, item.TextEs, null, null, null, item.Position, true))];
        var repository = new FormCatalogCapacityRepository(catalog, items);

        var result = await Service(repository, new FakeUnitOfWork(), reader).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [], Sort: new(field.Id.ToString("D"))), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "C2", "C3", "C1", "C4" }, Codes(repository));
    }

    [Fact]
    public async Task Sorting_by_a_field_uses_the_values_written_in_the_same_save()
    {
        var (catalog, field, items) = WithField("text", "Beta", "Gamma", "Delta");
        var repository = new FormCatalogCapacityRepository(catalog, items);
        var edit = new FormCatalogManualItemSaveRequest("c3", items[2].Id, "C3", Texts("Opción 3"), null, true, "1",
            new Dictionary<string, string?> { [field.Id.ToString("D")] = "Alfa" });

        var result = await Service(repository, new FakeUnitOfWork()).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [edit], Sort: new(field.Id.ToString("D"))), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "C3", "C1", "C2" }, Codes(repository));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sorting_by_a_field_that_is_not_an_active_field_of_the_catalog_saves_nothing(bool known)
    {
        var (catalog, field, items) = WithField("text", "B", "A");
        Assert.True(catalog.SetFields([field with { IsActive = false }], null).IsSuccess);
        var repository = new FormCatalogCapacityRepository(catalog, items);
        var work = new FakeUnitOfWork();
        var column = known ? field.Id.ToString("D") : Guid.CreateVersion7().ToString("D");

        var result = await Service(repository, work).UpdateManualAsync(catalog.Id, new(1, Texts(), Texts(), [], Sort: new(column)), Ct);

        Assert.Equal(["sort.column"], Assert.IsType<ValidationError>(result.Error).Errors.Keys);
        Assert.Equal(0, work.Commits);
        Assert.Equal(new[] { "C1", "C2" }, Codes(repository));
        Assert.Equal(1, catalog.Revision);
    }

    [Fact]
    public async Task Sorting_cannot_be_combined_with_explicit_moves()
    {
        var catalog = Catalog();
        var first = catalog.AddManualItem("B", "B", null, null, null, 1, "1").Value;
        var second = catalog.AddManualItem("A", "A", null, null, null, 2, "2").Value;
        var repository = new FormCatalogCapacityRepository(catalog, [first, second]);
        var work = new FakeUnitOfWork();
        var result = await Service(repository, work).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [], [new(first.Id, "down")], Sort: new("text")), Ct);
        Assert.Contains("moves", Assert.IsType<ValidationError>(result.Error).Errors.Keys);
        Assert.Equal(0, work.Transactions);
        Assert.Empty(repository.Locks);
        Assert.Equal(1, catalog.Revision);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("TEXT")]
    [InlineData("6F9619FF-8B86-D011-B42D-00CF4FC964FF")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void The_column_is_the_text_the_code_or_the_id_of_a_field(string? column)
    {
        var result = new UpdateManualCatalogRequestValidator().Validate(new UpdateManualCatalogRequest(1, Texts(), Texts(), [], Sort: new(column)));

        Assert.Equal(["sort.column"], result.Errors.Select(error => error.PropertyName));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("code")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    public void A_valid_column_passes_the_shape_check(string column)
    {
        Assert.True(new UpdateManualCatalogRequestValidator().Validate(new UpdateManualCatalogRequest(1, Texts(), Texts(), [], Sort: new(column, true))).IsValid);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static Dictionary<string, string?> Texts(string es = "Propio") => new() { ["es"] = es, ["en"] = "Own" };
    private static IEnumerable<string> Codes(FormCatalogCapacityRepository repository) => repository.Items.OrderBy(item => item.Position).Select(item => item.Code);

    private static BusinessFormCatalog Catalog() => BusinessFormCatalog.CreateManual("es",
        new Dictionary<string,string> { ["es"] = "Propio", ["en"] = "Own" },
        new Dictionary<string,string> { ["es"] = "Propio", ["en"] = "Own" }, BusinessFormCatalogValueType.Decimal);

    private static (BusinessFormCatalog Catalog, BusinessFormCatalogField Field, List<BusinessFormCatalogItem> Items) WithField(string type, params string?[] values) =>
        WithField(type, null, values);

    // One field and an option per value, coded C1, C2… in that order; a null value leaves the option without it.
    private static (BusinessFormCatalog Catalog, BusinessFormCatalogField Field, List<BusinessFormCatalogItem> Items) WithField(string type, Guid? sourceCatalogId,
        params string?[] values)
    {
        var catalog = Catalog();
        var field = new BusinessFormCatalogField(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["es"] = "Campo" }, type, sourceCatalogId,
            false, false, null, null, type == "text" ? 300 : null, "data", 0, true);
        Assert.True(catalog.SetFields([field], null).IsSuccess);
        var items = values.Select((value, index) =>
        {
            var item = catalog.AddManualItem("C" + (index + 1), "Opción " + (index + 1), null, null, null, index + 1, "1").Value;
            if (value is not null)
                Assert.True(catalog.SetManualItemFields(item, JsonSerializer.Serialize(new Dictionary<string, string> { [field.Id.ToString("D")] = value },
                    JsonSerializerOptions.Web)).IsSuccess);
            return item;
        }).ToList();
        return (catalog, field, items);
    }

    private static FormCatalogStoredRow Row(BusinessFormCatalog catalog) => new(catalog.Id, catalog.Key, catalog.NameJson, catalog.DescriptionJson,
        catalog.OriginalCulture, "Manual", 1, true, catalog.CreatedAtUtc, null, null, null, 1, FieldsJson: catalog.FieldsJson,
        PresentationJson: catalog.PresentationJson);

    private static FormCatalogService Service(FormCatalogCapacityRepository repository, FakeUnitOfWork work, FormCatalogFieldReader? reader = null)
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        reader ??= new FormCatalogFieldReader();
        return new(repository, reader, work, RequestValidators.For(new UpdateManualCatalogRequestValidator()),
            new(new FakeCurrentUser { UserId = actor }, permissions), null!, new(repository), new(reader));
    }
}
