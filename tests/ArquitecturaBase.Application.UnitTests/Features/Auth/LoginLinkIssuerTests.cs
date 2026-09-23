using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

/// <summary>El emisor de los enlaces que manda el bot (secciones 5, 6.4 y 13 del spec del ingreso con WhatsApp).</summary>
public sealed class LoginLinkIssuerTests
{
    private static readonly Uri Origin = new("https://app.test/");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryLoginLinkRepository _loginLinks = new();
    private readonly Guid _ana = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task Issues_a_link_that_stores_only_the_hash_of_its_token()
    {
        var result = await Issuer().IssueAsync(_ana, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([_ana], _loginLinks.LockedAccounts);

        var link = Assert.Single(_loginLinks.Links);
        Assert.Equal(_ana, link.UserId);
        Assert.Equal(FakeSecureTokenGenerator.HashOf("token-1"), link.TokenHash);
        Assert.Equal(Now, link.CreatedAtUtc);
        Assert.Equal(Now.AddMinutes(10), link.ExpiresAtUtc);
        Assert.Equal(link.ExpiresAtUtc, result.Value.ExpiresAtUtc);
    }

    [Fact]
    public async Task Url_is_the_login_link_page_of_the_public_origin_with_the_token_in_the_fragment()
    {
        var result = await Issuer().IssueAsync(_ana, Ct);

        // En el fragmento: el navegador no se lo manda al servidor (sección 5 del spec del ingreso con WhatsApp).
        Assert.Equal("https://app.test/ingresar#t=token-1", result.Value.Url);
    }

    [Theory]
    [InlineData("https://app.test/base/", "https://app.test/base/ingresar#t=token-1")]
    [InlineData("https://app.test:5173/", "https://app.test:5173/ingresar#t=token-1")]
    public async Task Url_keeps_the_port_and_the_path_of_the_public_origin(string origin, string expected)
    {
        var result = await Issuer(origin: new Uri(origin)).IssueAsync(_ana, Ct);

        Assert.Equal(expected, result.Value.Url);
    }

    [Fact]
    public async Task Without_a_public_origin_it_fails_before_issuing_anything()
    {
        var issuer = Issuer(origin: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(_ana, Ct));
        Assert.Empty(_loginLinks.Links);
    }

    [Fact]
    public async Task Issuing_another_link_invalidates_the_active_ones_of_the_account()
    {
        var bruno = Guid.CreateVersion7();
        var issuer = Issuer();
        await issuer.IssueAsync(_ana, Ct);
        await issuer.IssueAsync(bruno, Ct);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var result = await issuer.IssueAsync(_ana, Ct);

        Assert.True(result.IsSuccess);
        var anaLinks = _loginLinks.Links.Where(link => link.UserId == _ana).ToList();
        Assert.Equal(2, anaLinks.Count);
        Assert.Equal(Now, anaLinks[0].InvalidatedAtUtc);
        Assert.True(anaLinks[1].IsActive(Now));

        // El de otra cuenta no se toca.
        Assert.True(_loginLinks.Links.Single(link => link.UserId == bruno).IsActive(Now));
    }

    [Fact]
    public async Task A_second_link_within_a_minute_waits_until_the_minute_is_over()
    {
        var issuer = Issuer();
        await issuer.IssueAsync(_ana, Ct);
        _clock.Advance(TimeSpan.FromSeconds(20));

        var result = await issuer.IssueAsync(_ana, Ct);

        Assert.Equal(LoginLinkErrors.TooManyRequestsCode, result.Error.Code);
        Assert.Equal(40, result.Error.Metadata![LoginLinkErrors.RetryAfterKey]);

        // Nada cambia: el primero sigue sirviendo.
        Assert.True(Assert.Single(_loginLinks.Links).IsActive(Now));
    }

    [Fact]
    public async Task Sixth_link_in_fifteen_minutes_waits_until_the_oldest_one_leaves_the_window()
    {
        var issuer = Issuer();

        for (var i = 0; i < 5; i++)
        {
            Assert.True((await issuer.IssueAsync(_ana, Ct)).IsSuccess);
            _clock.Advance(TimeSpan.FromMinutes(2));
        }

        var result = await issuer.IssueAsync(_ana, Ct);

        // El primero se emitió hace 10 minutos: sale de la ventana de 15 dentro de 5.
        Assert.Equal(LoginLinkErrors.TooManyRequestsCode, result.Error.Code);
        Assert.Equal(300, result.Error.Metadata![LoginLinkErrors.RetryAfterKey]);
        Assert.Equal(5, _loginLinks.Links.Count);

        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.True((await issuer.IssueAsync(_ana, Ct)).IsSuccess);
    }

    [Fact]
    public async Task Limits_are_per_account()
    {
        var issuer = Issuer();
        await issuer.IssueAsync(_ana, Ct);

        var other = await issuer.IssueAsync(Guid.CreateVersion7(), Ct);

        Assert.True(other.IsSuccess);
    }

    [Fact]
    public async Task Limits_come_from_the_options()
    {
        var issuer = Issuer(new LoginLinkOptions { ResendCooldownSeconds = 0, MaxRequestsPerWindow = 2, RequestWindowMinutes = 60 });

        Assert.True((await issuer.IssueAsync(_ana, Ct)).IsSuccess);
        Assert.True((await issuer.IssueAsync(_ana, Ct)).IsSuccess);
        _clock.Advance(TimeSpan.FromMinutes(30));

        var third = await issuer.IssueAsync(_ana, Ct);

        Assert.Equal(LoginLinkErrors.TooManyRequestsCode, third.Error.Code);
        Assert.Equal(30 * 60, third.Error.Metadata![LoginLinkErrors.RetryAfterKey]);
    }

    [Fact]
    public async Task The_issued_link_does_not_print_its_url()
    {
        var result = await Issuer().IssueAsync(_ana, Ct);

        // Un record imprimiría sus propiedades: la URL lleva el token y no tiene que terminar en un log.
        Assert.DoesNotContain("token-1", result.Value.ToString(), StringComparison.Ordinal);
    }

    private LoginLinkIssuer Issuer(LoginLinkOptions? options = null) => Issuer(Origin, options);

    private LoginLinkIssuer Issuer(Uri? origin, LoginLinkOptions? options = null) =>
        new(
            _loginLinks,
            new FakeSecureTokenGenerator(),
            new FakePublicOrigin(origin),
            Options.Create(options ?? new LoginLinkOptions()),
            _clock);
}
