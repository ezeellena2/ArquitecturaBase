using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;

namespace ArquitecturaBase.Application.UnitTests.Features.Users;

public sealed class GetUsersQueryTests
{
    [Theory]
    [InlineData("email")]
    [InlineData("-displayName")]
    [InlineData("createdAtUtc")]
    public void Whitelisted_fields_can_be_sorted(string sort)
    {
        Assert.True(new GetUsersQueryValidator().Validate(new GetUsersQuery { Sort = sort }).IsValid);
    }

    [Fact]
    public void Other_fields_cannot_be_sorted()
    {
        Assert.False(new GetUsersQueryValidator().Validate(new GetUsersQuery { Sort = "passwordHash" }).IsValid);
    }

    [Fact]
    public void A_role_filter_with_no_value_is_rejected()
    {
        // El parámetro ausente es "sin filtro". Presente y vacío es un error del cliente: devolver todo
        // parecería un filtro que no anda.
        Assert.False(new GetUsersQueryValidator().Validate(new GetUsersQuery { Role = "  " }).IsValid);
    }

    [Fact]
    public void A_role_filter_longer_than_a_role_name_is_rejected()
    {
        var query = new GetUsersQuery { Role = new string('a', 200) };

        Assert.False(new GetUsersQueryValidator().Validate(query).IsValid);
    }

    [Fact]
    public void A_role_that_does_not_exist_is_not_a_validation_error()
    {
        // A propósito: el código de respuesta diría si ese nombre de rol existe. Un rol desconocido filtra
        // por un rol que no tiene a nadie, y eso es una lista vacía.
        Assert.True(new GetUsersQueryValidator().Validate(new GetUsersQuery { Role = "NoExiste" }).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4000)]
    public void Days_outside_the_range_are_rejected(int days)
    {
        var query = new GetUsersQuery { CreatedWithinDays = days };

        Assert.False(new GetUsersQueryValidator().Validate(query).IsValid);
    }

    [Fact]
    public void No_filters_at_all_is_valid()
    {
        Assert.True(new GetUsersQueryValidator().Validate(new GetUsersQuery()).IsValid);
    }

    [Fact]
    public async Task Handler_delegates_the_page_to_the_identity_service()
    {
        var identity = new FakeIdentityService();
        identity.AddUser("ana@example.com");
        var query = new GetUsersQuery { Page = 1, PageSize = 10, Search = "ana" };

        var result = await new GetUsersQueryHandler(identity, new FakePhoneNumberParser()).Handle(query, TestContext.Current.CancellationToken);

        Assert.Same(query, identity.LastListRequest);
        Assert.Equal("ana@example.com", Assert.Single(result.Value.Items).Email);
    }

    [Fact]
    public async Task Handler_adds_the_phone_formatted_for_reading_and_leaves_it_null_without_a_phone()
    {
        var identity = new FakeIdentityService();
        var withPhone = identity.AddUser(email: null, phoneNumber: "+5493515550101");
        var withoutPhone = identity.AddUser("ana@example.com");

        var result = await new GetUsersQueryHandler(identity, new FakePhoneNumberParser())
            .Handle(new GetUsersQuery { Page = 1, PageSize = 10 }, TestContext.Current.CancellationToken);

        Assert.Equal("formatted +5493515550101", result.Value.Items.Single(item => item.Id == withPhone.Id).FormattedPhoneNumber);
        Assert.Null(result.Value.Items.Single(item => item.Id == withoutPhone.Id).FormattedPhoneNumber);
        Assert.Equal(2, result.Value.TotalCount);
    }
}
