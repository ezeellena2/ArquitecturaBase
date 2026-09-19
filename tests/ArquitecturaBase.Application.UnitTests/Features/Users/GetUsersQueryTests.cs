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
    public async Task Handler_delegates_the_page_to_the_identity_service()
    {
        var identity = new FakeIdentityService();
        identity.AddUser("ana@example.com");
        var query = new GetUsersQuery { Page = 1, PageSize = 10, Search = "ana" };

        var result = await new GetUsersQueryHandler(identity).Handle(query, TestContext.Current.CancellationToken);

        Assert.Same(query, identity.LastListRequest);
        Assert.Equal("ana@example.com", Assert.Single(result.Value.Items).Email);
    }
}
