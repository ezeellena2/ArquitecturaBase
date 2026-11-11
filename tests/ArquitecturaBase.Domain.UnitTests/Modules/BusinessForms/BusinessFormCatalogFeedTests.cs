using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormCatalogFeedTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_external_catalog_takes_its_key_and_value_type_and_is_not_edited_by_hand()
    {
        var catalog = Catalog();

        Assert.Equal((BusinessFormCatalogKind.External, "arca.concepts", BusinessFormCatalogValueType.Integer, 1L),
            (catalog.Kind, catalog.Key, catalog.ValueType, catalog.Revision));
        Assert.True(catalog.UpdateManual(1, Names("Otro"), Names("")).IsFailure);
        Assert.True(catalog.AddManualItem("x", "X", null, null, null, 1).IsFailure);
        Assert.Throws<ArgumentException>(() => BusinessFormCatalog.CreateExternal("ARCA concepts", "es", Names("Conceptos"), Names(""),
            BusinessFormCatalogValueType.Integer));
    }

    [Fact]
    public void The_first_feed_adds_its_items_in_order_and_records_when_it_came()
    {
        var catalog = Catalog();

        var added = catalog.ApplyFeed([], [Item("1", "Productos"), Item("2", "Servicios")], Now).Value;

        Assert.Equal([("1", "Productos", "1", 1), ("2", "Servicios", "2", 2)], added.Select(item => (item.Code, item.TextEs!, item.Value!, item.Position)));
        Assert.Equal((2L, Now, (string?)null), (catalog.Revision, catalog.FeedRefreshedAtUtc, catalog.FeedFailureCode));
    }

    [Fact]
    public void A_later_feed_updates_texts_deactivates_what_stopped_coming_and_brings_back_what_returns()
    {
        var catalog = Catalog();
        var stored = catalog.ApplyFeed([], [Item("1", "Productos"), Item("2", "Servicios"), Item("3", "Ambos")], Now).Value.ToList();

        var added = catalog.ApplyFeed(stored, [Item("1", "Productos y bienes"), Item("3", "Ambos"), Item("4", "Otros")], Now.AddDays(1)).Value;

        Assert.Equal(("4", 4), (Assert.Single(added).Code, added[0].Position));
        Assert.Equal("Productos y bienes", stored[0].TextEs);
        Assert.False(stored[1].IsActive);
        Assert.Equal(3L, catalog.Revision);

        stored.AddRange(added);
        catalog.ApplyFeed(stored, [Item("1", "Productos y bienes"), Item("2", "Servicios"), Item("3", "Ambos"), Item("4", "Otros")], Now.AddDays(2));
        Assert.True(stored[1].IsActive);
    }

    [Fact]
    public void A_feed_without_changes_keeps_the_revision()
    {
        var catalog = Catalog();
        var stored = catalog.ApplyFeed([], [Item("1", "Productos")], Now).Value.ToList();

        Assert.Empty(catalog.ApplyFeed(stored, [Item("1", "Productos")], Now.AddDays(1)).Value);

        Assert.Equal((2L, Now.AddDays(1)), (catalog.Revision, catalog.FeedRefreshedAtUtc));
    }

    [Fact]
    public void One_host_at_a_time_reserves_the_refresh_and_a_failure_keeps_the_items_and_says_why()
    {
        var catalog = Catalog();

        Assert.True(catalog.TryReserveFeed(Now, TimeSpan.FromMinutes(2)));
        Assert.False(catalog.TryReserveFeed(Now.AddMinutes(1), TimeSpan.FromMinutes(2)));
        catalog.RecordFeedFailure("arca.unavailable", Now.AddMinutes(1));

        Assert.Equal(("arca.unavailable", Now.AddMinutes(1), (DateTime?)null), (catalog.FeedFailureCode, catalog.FeedFailedAtUtc, catalog.FeedLeaseUntilUtc));
        Assert.True(catalog.TryReserveFeed(Now.AddMinutes(1), TimeSpan.FromMinutes(2)));
        Assert.False(BusinessFormCatalog.CreateManual("es", Names("Manual"), Names("")).TryReserveFeed(Now, TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void Repeated_codes_or_too_many_items_are_refused_without_changes()
    {
        var catalog = Catalog();

        Assert.Equal(FormCatalogErrors.FeedInvalid, catalog.ApplyFeed([], [Item("1", "A"), Item("1", "B")], Now).Error);
        Assert.Equal(FormCatalogErrors.TooManyItems, catalog.ApplyFeed([],
            Enumerable.Range(1, BusinessFormCatalog.MaxItems + 1).Select(code => Item(code.ToString(System.Globalization.CultureInfo.InvariantCulture), "X")).ToArray(), Now).Error);
        Assert.Equal(1L, catalog.Revision);
    }

    private static BusinessFormCatalog Catalog() =>
        BusinessFormCatalog.CreateExternal("arca.concepts", "es", Names("Conceptos"), Names(""), BusinessFormCatalogValueType.Integer);

    private static BusinessFormFeedItem Item(string code, string text) => new(code, text, null, code);

    private static Dictionary<string, string> Names(string text) => new() { ["es"] = text };
}
