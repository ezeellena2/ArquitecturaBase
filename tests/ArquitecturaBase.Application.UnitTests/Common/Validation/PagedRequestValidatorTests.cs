using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;

namespace ArquitecturaBase.Application.UnitTests.Common.Validation;

public sealed class PagedRequestValidatorTests
{
    private sealed record ProductsQuery : PagedRequest;

    private sealed class ProductsQueryValidator() : PagedRequestValidator<ProductsQuery>(["name", "createdAtUtc"]);

    private static readonly ProductsQueryValidator Validator = new();

    [Fact]
    public void Defaults_are_valid()
    {
        Assert.True(Validator.Validate(new ProductsQuery()).IsValid);
    }

    [Fact]
    public void Page_below_one_is_rejected()
    {
        using var culture = new CultureScope("es");

        var result = Validator.Validate(new ProductsQuery { Page = 0 });

        var failure = Assert.Single(result.Errors);
        Assert.Equal("Page", failure.PropertyName);
        Assert.Equal("La página debe ser mayor o igual a 1.", failure.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Page_size_out_of_range_is_rejected(int pageSize)
    {
        using var culture = new CultureScope("es");

        var result = Validator.Validate(new ProductsQuery { PageSize = pageSize });

        Assert.Equal("La cantidad por página debe estar entre 1 y 100.", Assert.Single(result.Errors).ErrorMessage);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("-createdAtUtc")]
    [InlineData("NAME")]
    public void Whitelisted_sort_is_accepted(string sort)
    {
        Assert.True(Validator.Validate(new ProductsQuery { Sort = sort }).IsValid);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("-")]
    public void Sort_outside_the_whitelist_is_rejected(string sort)
    {
        using var culture = new CultureScope("es");

        var result = Validator.Validate(new ProductsQuery { Sort = sort });

        var failure = Assert.Single(result.Errors);
        Assert.Equal("Sort", failure.PropertyName);
        Assert.Equal("No se puede ordenar por ese campo.", failure.ErrorMessage);
    }
}
