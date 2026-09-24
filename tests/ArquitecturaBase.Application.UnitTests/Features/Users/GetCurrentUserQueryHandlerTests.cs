using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Features.Users.GetCurrentUser;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Features.Users;

public sealed class GetCurrentUserQueryHandlerTests
{
    private readonly FakeIdentityService _identity = new();
    private readonly FakePermissionService _permissions = new();
    private readonly InMemoryLoginAuditRepository _loginAudits = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_the_profile_with_sorted_roles_and_permissions()
    {
        var user = _identity.AddUser("ana@example.com", culture: "en");
        _identity.SetRoles(user.Id, "User", "Admin");
        _permissions.Permissions[user.Id] = ["users.read", "roles.manage"];

        var result = await Handler(user.Id).Handle(new GetCurrentUserQuery(), Ct);

        Assert.Equal(user.Id, result.Value.Id);
        Assert.Equal("ana@example.com", result.Value.Email);
        Assert.Equal("en", result.Value.Culture);
        Assert.Equal(FakeIdentityService.DefaultTimeZoneId, result.Value.TimeZoneId);
        Assert.Equal(["Admin", "User"], result.Value.Roles);
        Assert.Equal(["roles.manage", "users.read"], result.Value.Permissions);
        Assert.Null(result.Value.LastLoginAtUtc);
    }

    [Fact]
    public async Task Returns_the_phone_of_an_account_without_email()
    {
        var user = _identity.AddUser(email: null, phoneNumber: "+5493511234567");

        var result = await Handler(user.Id).Handle(new GetCurrentUserQuery(), Ct);

        Assert.Null(result.Value.Email);
        Assert.False(result.Value.EmailConfirmed);
        Assert.Equal("+5493511234567", result.Value.PhoneNumber);
        Assert.True(result.Value.PhoneNumberConfirmed);
        Assert.False(result.Value.HasGoogleLogin);
    }

    [Fact]
    public async Task Returns_the_phone_formatted_for_reading_and_masked()
    {
        // El front nunca muestra el E.164 crudo: el formato y la máscara los arma el parser, que es quien sabe
        // agrupar cada país (FakePhoneNumberParser marca cuál usó).
        var user = _identity.AddUser(email: null, phoneNumber: "+5493511234567");

        var result = await Handler(user.Id).Handle(new GetCurrentUserQuery(), Ct);

        Assert.Equal("formatted +5493511234567", result.Value.FormattedPhoneNumber);
        Assert.Equal("masked 4567", result.Value.MaskedPhoneNumber);
    }

    [Fact]
    public async Task An_account_without_phone_has_no_formatted_or_masked_phone()
    {
        var user = _identity.AddUser("ana@example.com");

        var result = await Handler(user.Id).Handle(new GetCurrentUserQuery(), Ct);

        Assert.Null(result.Value.PhoneNumber);
        Assert.Null(result.Value.FormattedPhoneNumber);
        Assert.Null(result.Value.MaskedPhoneNumber);
    }

    [Fact]
    public async Task Says_whether_the_account_signs_in_with_google()
    {
        var withGoogle = _identity.AddUser("ana@example.com");
        _identity.LinkExternalLogin(withGoogle.Id, ExternalLoginProviders.Google, "google-123");
        var withoutGoogle = _identity.AddUser("beto@example.com");

        var linked = await Handler(withGoogle.Id).Handle(new GetCurrentUserQuery(), Ct);
        var notLinked = await Handler(withoutGoogle.Id).Handle(new GetCurrentUserQuery(), Ct);

        Assert.True(linked.Value.HasGoogleLogin);
        Assert.True(linked.Value.EmailConfirmed);
        Assert.False(notLinked.Value.HasGoogleLogin);
    }

    [Fact]
    public async Task Unknown_user_is_not_found()
    {
        var result = await Handler(Guid.CreateVersion7()).Handle(new GetCurrentUserQuery(), Ct);

        Assert.Equal(UserErrors.NotFoundCode, result.Error.Code);
    }

    [Fact]
    public async Task Anonymous_request_is_not_found()
    {
        var result = await Handler(userId: null).Handle(new GetCurrentUserQuery(), Ct);

        Assert.Equal(UserErrors.NotFoundCode, result.Error.Code);
    }

    private GetCurrentUserQueryHandler Handler(Guid? userId) =>
        new(new FakeCurrentUser { UserId = userId }, _identity, _permissions, _loginAudits, new FakePhoneNumberParser());
}
