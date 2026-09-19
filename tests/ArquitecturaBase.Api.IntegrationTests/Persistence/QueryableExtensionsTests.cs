using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;

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
