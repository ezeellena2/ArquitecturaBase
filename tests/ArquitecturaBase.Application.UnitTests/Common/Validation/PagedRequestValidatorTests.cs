using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;

namespace ArquitecturaBase.Application.UnitTests.Common.Validation;

public sealed class PagedRequestValidatorTests
{
    private sealed record ProductsQuery : PagedRequest;

    private sealed class ProductsQueryValidator() : PagedRequestValidator<ProductsQuery>(["name", "createdAtUtc"]);

    // A query that reads fewer rows at a time, like the options of a question (50).
    private sealed class ShortPagesQueryValidator() : PagedRequestValidator<ProductsQuery>(["name"], 50);

    private sealed class WidePagesQueryValidator() : PagedRequestValidator<ProductsQuery>(["name"], PagedRequest.MaxPageSize + 1);

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
        Assert.Equal("La página debe estar entre 1 y 1.000.000.", failure.ErrorMessage);
    }

    [Fact]
    public void Page_above_the_maximum_is_rejected()
    {
        using var culture = new CultureScope("es");

        var result = Validator.Validate(new ProductsQuery { Page = PagedRequest.MaxPage + 1 });

        var failure = Assert.Single(result.Errors);
        Assert.Equal("Page", failure.PropertyName);
        Assert.Equal("La página debe estar entre 1 y 1.000.000.", failure.ErrorMessage);
    }

    [Fact]
    public void Page_at_the_maximum_is_valid()
    {
        Assert.True(Validator.Validate(new ProductsQuery { Page = PagedRequest.MaxPage }).IsValid);
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
    [InlineData("es", 0, "La cantidad por página debe estar entre 1 y 50.")]
    [InlineData("es", 51, "La cantidad por página debe estar entre 1 y 50.")]
    [InlineData("es", 101, "La cantidad por página debe estar entre 1 y 50.")]
    [InlineData("en", 51, "Page size must be between 1 and 50.")]
    public void Page_size_outside_a_smaller_cap_is_rejected_once_with_that_cap(string language, int pageSize, string message)
    {
        using var culture = new CultureScope(language);

        var result = new ShortPagesQueryValidator().Validate(new ProductsQuery { PageSize = pageSize });

        var failure = Assert.Single(result.Errors);
        Assert.Equal("PageSize", failure.PropertyName);
        Assert.Equal(message, failure.ErrorMessage);
    }

    [Fact]
    public void Page_size_at_a_smaller_cap_is_valid()
    {
        Assert.True(new ShortPagesQueryValidator().Validate(new ProductsQuery { PageSize = 50 }).IsValid);
    }

    // PagedRequest.MaxPage was set with the common cap (100): a query may only lower it.
    [Fact]
    public void A_cap_above_the_common_maximum_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WidePagesQueryValidator());
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

    [Fact]
    public void Search_longer_than_the_limit_is_rejected()
    {
        using var culture = new CultureScope("es");

        var result = Validator.Validate(new ProductsQuery { Search = new string('a', PagedRequest.MaxSearchLength + 1) });

        var failure = Assert.Single(result.Errors);
        Assert.Equal("Search", failure.PropertyName);
        Assert.Equal("Ingresá como máximo 100 caracteres.", failure.ErrorMessage);
    }
}
