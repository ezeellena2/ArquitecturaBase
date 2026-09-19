using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.UnitTests.Common.Pagination;

public sealed class SortDescriptorTests
{
    [Theory]
    [InlineData("name", "name", false)]
    [InlineData("-createdAtUtc", "createdAtUtc", true)]
    [InlineData("  -name  ", "name", true)]
    public void Parses_field_and_direction(string sort, string field, bool descending)
    {
        var descriptor = SortDescriptor.Parse(sort);

        Assert.Equal(new SortDescriptor(field, descending), descriptor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-")]
    public void Returns_null_when_there_is_no_field(string? sort)
    {
        Assert.Null(SortDescriptor.Parse(sort));
    }
}
