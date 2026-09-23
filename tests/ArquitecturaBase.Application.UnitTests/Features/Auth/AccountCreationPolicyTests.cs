using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

public sealed class AccountCreationPolicyTests
{
    private static readonly Email InitialAdmin = Email.Create(FakeInitialAdmin.DefaultEmail).Value;
    private static readonly Email Other = Email.Create("ana@example.com").Value;

    private readonly FakeSystemSettingsReader _settings = new();
    private readonly FakeInitialAdmin _initialAdmin = new();
    private readonly AccountCreationPolicy _policy;

    public AccountCreationPolicyTests()
    {
        _policy = new AccountCreationPolicy(_settings, _initialAdmin);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Open_registration_lets_anyone_create_an_account()
    {
        _settings.Mode = RegistrationMode.Open;

        Assert.True(await _policy.AllowsNewAccountAsync(Other, Ct));
        Assert.True(await _policy.AllowsNewAccountAsync(InitialAdmin, Ct));
        Assert.True(await _policy.AllowsNewAccountAsync(email: null, Ct));
    }

    [Fact]
    public async Task Invite_only_lets_only_the_initial_admin_create_an_account()
    {
        _settings.Mode = RegistrationMode.InviteOnly;

        Assert.True(await _policy.AllowsNewAccountAsync(InitialAdmin, Ct));
        Assert.False(await _policy.AllowsNewAccountAsync(Other, Ct));
    }

    [Fact]
    public async Task Invite_only_never_lets_a_number_create_an_account()
    {
        // El administrador inicial se reconoce por el correo: una cuenta de solo número nunca es la suya.
        _settings.Mode = RegistrationMode.InviteOnly;

        Assert.False(await _policy.AllowsNewAccountAsync(email: null, Ct));
    }

    [Fact]
    public async Task Without_an_initial_admin_invite_only_lets_nobody_create_an_account()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        _initialAdmin.AdminEmail = null;

        Assert.False(await _policy.AllowsNewAccountAsync(InitialAdmin, Ct));
    }
}
