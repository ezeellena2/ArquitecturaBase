using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.UnitTests.Common.Pagination;

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(1, 20, 0, 0, false, false)]
    [InlineData(1, 20, 20, 1, false, false)]
    [InlineData(1, 20, 21, 2, false, true)]
    [InlineData(2, 10, 25, 3, true, true)]
    [InlineData(3, 10, 25, 3, true, false)]
    public void Computes_pages(int page, int pageSize, int totalCount, int totalPages, bool hasPrevious, bool hasNext)
    {
        var result = new PagedResult<string>([], page, pageSize, totalCount);

        Assert.Equal(totalPages, result.TotalPages);
        Assert.Equal(hasPrevious, result.HasPrevious);
        Assert.Equal(hasNext, result.HasNext);
    }

    [Fact]
    public void Paged_request_has_the_spec_defaults()
    {
        var request = new SampleRequest();

        Assert.Equal(1, request.Page);
        Assert.Equal(20, request.PageSize);
        Assert.Null(request.Sort);
        Assert.Null(request.Search);
        Assert.Equal(100, PagedRequest.MaxPageSize);
    }

    private sealed record SampleRequest : PagedRequest;
}
