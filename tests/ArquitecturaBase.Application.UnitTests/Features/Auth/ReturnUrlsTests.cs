using ArquitecturaBase.Application.Features.Auth;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

public sealed class ReturnUrlsTests
{
    [Theory]
    [InlineData("/connect/authorize")]
    [InlineData("/connect/authorize?client_id=web&redirect_uri=https://localhost:5173/auth/callback&state=x")]
    public void Local_authorize_requests_are_accepted(string returnUrl)
    {
        Assert.True(ReturnUrls.IsAuthorizeRequest(returnUrl));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/login")]
    [InlineData("https://evil.example/connect/authorize")]
    [InlineData("//evil.example/connect/authorize")]
    [InlineData("/connect/authorizeX")]
    [InlineData("/connect/authorize/../../evil")]
    [InlineData("/connect/authorize?x=1\r\nSet-Cookie: a=b")]
    public void Anything_else_is_rejected(string? returnUrl)
    {
        Assert.False(ReturnUrls.IsAuthorizeRequest(returnUrl));
    }
}
