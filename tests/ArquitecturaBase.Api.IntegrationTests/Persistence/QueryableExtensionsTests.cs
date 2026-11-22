using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

public sealed class QueryableExtensionsTests
{
    private sealed record Item(int Id, string Name, int Rank);

    private sealed record SampleRequest : PagedRequest;

    private static readonly Dictionary<string, Expression<Func<Item, object?>>> SortableFields = new()
    {
        ["name"] = item => item.Name,
        ["rank"] = item => item.Rank,
    };

    private static readonly SortDescriptor DefaultSort = new("rank", Descending: false);

    private static readonly IQueryable<Item> Items =
        new[] { new Item(1, "b", 2), new Item(2, "a", 3), new Item(3, "c", 1) }.AsQueryable();

    [Fact]
    public void Sorts_ascending_by_a_whitelisted_field()
    {
        var names = Items.ApplySort(new SortDescriptor("name", false), SortableFields, DefaultSort, item => item.Id).Select(item => item.Name);

        Assert.Equal(["a", "b", "c"], names);
    }

    [Fact]
    public void Sorts_descending()
    {
        var names = Items.ApplySort(new SortDescriptor("name", true), SortableFields, DefaultSort, item => item.Id).Select(item => item.Name);

        Assert.Equal(["c", "b", "a"], names);
    }

    [Fact]
    public void Field_lookup_ignores_case()
    {
        var names = Items.ApplySort(new SortDescriptor("NAME", false), SortableFields, DefaultSort, item => item.Id).Select(item => item.Name);

        Assert.Equal(["a", "b", "c"], names);
    }

    [Fact]
    public void Uses_the_default_sort_when_none_is_given()
    {
        var names = Items.ApplySort(null, SortableFields, DefaultSort, item => item.Id).Select(item => item.Name);

        Assert.Equal(["c", "b", "a"], names);
    }

    [Fact]
    public void Field_outside_the_whitelist_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Items.ApplySort(new SortDescriptor("secret", false), SortableFields, DefaultSort, item => item.Id));
    }

    [Fact]
    public void Equal_keys_are_ordered_by_the_tie_breaker()
    {
        var items = new[] { new Item(3, "x", 1), new Item(1, "x", 1), new Item(2, "x", 1) }.AsQueryable();

        var ids = items.ApplySort(new SortDescriptor("name", true), SortableFields, DefaultSort, item => item.Id).Select(item => item.Id);

        Assert.Equal([1, 2, 3], ids);
    }

    private enum Stage { Received, Approved, Rejected }

    private sealed record Row(int Id, string Group, int Rank, string? Note = null, Stage Stage = Stage.Received,
        Stage? LastStage = null, string Prefix = "", long? Number = null);

    private static readonly Dictionary<string, Expression<Func<Row, object?>>> RowFields = new()
    {
        ["group"] = row => row.Group,
        ["rank"] = row => row.Rank,
        ["note"] = row => row.Note,
        ["stage"] = row => row.Stage,
        ["lastStage"] = row => row.LastStage,
        ["code"] = row => new { row.Prefix, row.Number },
    };

    private static int[] Ids(IEnumerable<Row> rows, string field, bool descending, string usualField = "rank") =>
        [.. rows.AsQueryable().ApplySort(new SortDescriptor(field, descending), RowFields, new SortDescriptor(usualField, false), row => row.Id)
            .Select(row => row.Id)];

    [Theory]
    [InlineData(false, new[] { 3, 1, 5, 2, 4 })]
    [InlineData(true, new[] { 5, 1, 3, 2, 4 })]
    public void Empty_values_go_last_in_both_directions(bool descending, int[] expected)
    {
        Row[] rows = [new(1, "x", 1, "b"), new(2, "x", 2), new(3, "x", 3, "a"), new(4, "x", 4), new(5, "x", 5, "c")];

        Assert.Equal(expected, Ids(rows, "note", descending));
    }

    [Fact]
    public void Equal_values_keep_the_usual_order_of_the_list_before_the_tie_breaker()
    {
        Row[] rows = [new(1, "x", 3), new(2, "x", 1), new(3, "x", 2)];

        Assert.Equal([2, 3, 1], Ids(rows, "group", descending: true));
    }

    [Theory]
    [InlineData(false, new[] { 2, 3, 1 })]
    [InlineData(true, new[] { 1, 3, 2 })]
    public void An_enum_sorts_in_the_order_it_was_declared_and_not_by_its_name(bool descending, int[] expected)
    {
        Row[] rows = [new(1, "x", 1, Stage: Stage.Rejected), new(2, "x", 2, Stage: Stage.Received), new(3, "x", 3, Stage: Stage.Approved)];

        Assert.Equal(expected, Ids(rows, "stage", descending));
    }

    [Fact]
    public void An_empty_enum_goes_last()
    {
        Row[] rows = [new(1, "x", 1), new(2, "x", 2, LastStage: Stage.Rejected), new(3, "x", 3, LastStage: Stage.Received)];

        Assert.Equal([3, 2, 1], Ids(rows, "lastStage", descending: false));
    }

    [Theory]
    [InlineData(false, new[] { 4, 3, 5, 1, 2 })]
    [InlineData(true, new[] { 1, 5, 3, 4, 2 })]
    public void A_field_with_several_keys_sorts_by_each_and_is_empty_when_one_is_missing(bool descending, int[] expected)
    {
        Row[] rows = [new(1, "x", 1, Prefix: "B", Number: 2), new(2, "x", 2, Prefix: "A"), new(3, "x", 3, Prefix: "A", Number: 5),
            new(4, "x", 4, Prefix: "A", Number: 1), new(5, "x", 5, Prefix: "B", Number: 1)];

        Assert.Equal(expected, Ids(rows, "code", descending));
    }

    [Fact]
    public void A_usual_order_with_several_keys_sorts_without_a_sort_and_breaks_ties()
    {
        Row[] rows = [new(1, "b", 1, "x"), new(2, "a", 1, "y"), new(3, "a", 2, "x"), new(4, "b", 2, "y")];
        SortKey<Row>[] usual = [new(row => row.Group, false), new(row => row.Rank, true)];

        var unsorted = rows.AsQueryable().ApplySortWithDefaultOrder(null, RowFields, usual, row => row.Id).Select(row => row.Id);
        var byNote = rows.AsQueryable().ApplySortWithDefaultOrder(new SortDescriptor("note", false), RowFields, usual, row => row.Id).Select(row => row.Id);

        Assert.Equal([3, 2, 4, 1], unsorted);
        Assert.Equal([3, 1, 2, 4], byNote);
    }

    [Fact]
    public void Sql_puts_empty_values_last_and_maps_an_enum_saved_as_text_to_its_declared_order()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var fields = new Dictionary<string, Expression<Func<LoginAudit, object?>>>
        {
            ["occurredAtUtc"] = audit => audit.OccurredAtUtc,
            ["method"] = audit => audit.Method,
            ["user"] = audit => audit.UserId,
        };
        string OrderBy(string field, bool descending)
        {
            var sql = db.LoginAudits.ApplySort(new SortDescriptor(field, descending), fields, new SortDescriptor("occurredAtUtc", true), audit => audit.Id)
                .ToQueryString();
            return sql[sql.IndexOf("ORDER BY", StringComparison.Ordinal)..];
        }

        var byMethod = OrderBy("method", false);
        var byUser = OrderBy("user", true);
        var byUserAscending = OrderBy("user", false);

        Assert.Contains("\"Method\" = 'Code' THEN 0", byMethod, StringComparison.Ordinal);
        Assert.Contains("\"Method\" = 'WhatsAppLink' THEN 3", byMethod, StringComparison.Ordinal);
        Assert.Contains("\"UserId\" IS NULL", byUser, StringComparison.Ordinal);
        Assert.True(byUser.IndexOf("IS NULL", StringComparison.Ordinal) < byUser.IndexOf("DESC", StringComparison.Ordinal));
        // An ASC in Postgres already leaves NULL last: the key is not repeated, which matters when it is a subquery.
        Assert.DoesNotContain("IS NULL", byUserAscending, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public async Task Page_or_page_size_below_one_throws(int page, int pageSize)
    {
        var query = new[] { 1 }.AsQueryable();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => query.ToPagedResultAsync(new SampleRequest { Page = page, PageSize = pageSize }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Page_that_would_overflow_the_offset_throws_before_touching_the_database()
    {
        var query = new[] { 1 }.AsQueryable();
        var request = new SampleRequest { Page = int.MaxValue / 10 + 1, PageSize = 10 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => query.ToPagedResultAsync(request, TestContext.Current.CancellationToken));
    }
}
