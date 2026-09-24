using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Features.Users;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.Validation.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

public sealed class ProfileServiceTests
{
    private readonly FakeIdentityService _identity = new();
    private readonly FakePermissionService _permissions = new();
    private readonly InMemoryLoginAuditRepository _loginAudits = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeLogger<ProfileService> _logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_the_profile_with_sorted_roles_and_permissions()
    {
        var user = _identity.AddUser("ana@example.com", culture: "en");
        _identity.SetRoles(user.Id, "User", "Admin");
        _permissions.Permissions[user.Id] = ["users.read", "roles.manage"];

        var result = await Service(user.Id).GetAsync(Ct);

        Assert.Equal(user.Id, result.Value.Id);
        Assert.Equal("ana@example.com", result.Value.Email);
        Assert.Equal("en", result.Value.Culture);
        Assert.Equal(FakeIdentityService.DefaultTimeZoneId, result.Value.TimeZoneId);
        Assert.Equal(["Admin", "User"], result.Value.Roles);
        Assert.Equal(["roles.manage", "users.read"], result.Value.Permissions);
        Assert.Null(result.Value.LastLoginAtUtc);
        Assert.Equal(["Handling GetCurrentUserQuery", "Handled GetCurrentUserQuery"],
            _logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Returns_the_phone_of_an_account_without_email()
    {
        var user = _identity.AddUser(email: null, phoneNumber: "+5493511234567");

        var result = await Service(user.Id).GetAsync(Ct);

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

        var result = await Service(user.Id).GetAsync(Ct);

        Assert.Equal("formatted +5493511234567", result.Value.FormattedPhoneNumber);
        Assert.Equal("masked 4567", result.Value.MaskedPhoneNumber);
    }

    [Fact]
    public async Task An_account_without_phone_has_no_formatted_or_masked_phone()
    {
        var user = _identity.AddUser("ana@example.com");

        var result = await Service(user.Id).GetAsync(Ct);

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

        var linked = await Service(withGoogle.Id).GetAsync(Ct);
        var notLinked = await Service(withoutGoogle.Id).GetAsync(Ct);

        Assert.True(linked.Value.HasGoogleLogin);
        Assert.True(linked.Value.EmailConfirmed);
        Assert.False(notLinked.Value.HasGoogleLogin);
    }

    [Fact]
    public async Task Unknown_user_is_not_found()
    {
        var result = await Service(Guid.CreateVersion7()).GetAsync(Ct);

        Assert.Equal(UserErrors.NotFoundCode, result.Error.Code);
        Assert.Equal(["Handling GetCurrentUserQuery", "GetCurrentUserQuery failed with Users.User.NotFound"],
            _logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Anonymous_request_is_not_found()
    {
        var result = await Service(userId: null).GetAsync(Ct);

        Assert.Equal(UserErrors.NotFoundCode, result.Error.Code);
    }

    [Fact]
    public async Task Update_changes_the_name_culture_and_time_zone_and_confirms_the_unit_of_work()
    {
        var user = _identity.AddUser("ana@example.com");

        var result = await Service(user.Id).UpdateAsync(
            new UpdateProfileRequest("Ana", "en", "America/Sao_Paulo"), Ct);

        Assert.True(result.IsSuccess);
        var updated = await _identity.FindByIdAsync(user.Id, Ct);
        Assert.Equal("Ana", updated!.DisplayName);
        Assert.Equal("en", updated.Culture);
        Assert.Equal("America/Sao_Paulo", updated.TimeZoneId);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
        Assert.Equal(["Handling UpdateProfileCommand", "Handled UpdateProfileCommand"],
            _logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Theory]
    [InlineData("fr", "America/Argentina/Buenos_Aires", "culture")]
    [InlineData("es", "Marte/Olympus", "timeZoneId")]
    [InlineData(null, "America/Argentina/Buenos_Aires", "culture")]
    [InlineData("es", null, "timeZoneId")]
    public async Task Invalid_update_is_rejected_before_the_user_is_changed(
        string? culture, string? timeZoneId, string field)
    {
        var user = _identity.AddUser("ana@example.com");

        var result = await Service(user.Id).UpdateAsync(new UpdateProfileRequest("Ana", culture, timeZoneId), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Contains(field, error.Errors.Keys);
        Assert.Null((await _identity.FindByIdAsync(user.Id, Ct))!.DisplayName);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
        Assert.Equal(["Handling UpdateProfileCommand", "UpdateProfileCommand failed with Validation.Failed"],
            _logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Display_name_longer_than_the_limit_is_rejected_before_writing()
    {
        var user = _identity.AddUser("ana@example.com");

        var result = await Service(user.Id).UpdateAsync(new UpdateProfileRequest(
            new string('A', ValidationRules.DisplayNameMaxLength + 1), "es", "America/Argentina/Buenos_Aires"), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("displayName", error.Errors.Keys);
        Assert.Null((await _identity.FindByIdAsync(user.Id, Ct))!.DisplayName);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Unknown_user_cannot_update_a_profile()
    {
        var result = await Service(Guid.CreateVersion7()).UpdateAsync(
            new UpdateProfileRequest("Ana", "es", "America/Argentina/Buenos_Aires"), Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
        Assert.Equal(["Handling UpdateProfileCommand", "UpdateProfileCommand failed with Users.User.NotFound"],
            _logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Anonymous_request_cannot_update_a_profile()
    {
        var result = await Service(userId: null).UpdateAsync(
            new UpdateProfileRequest("Ana", "es", "America/Argentina/Buenos_Aires"), Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    private ProfileService Service(Guid? userId) =>
        new(new FakeCurrentUser { UserId = userId }, _identity, _identity, _permissions, _loginAudits,
            new FakePhoneNumberParser(),
            new ServiceRequestValidator<UpdateProfileRequest>([new UpdateProfileRequestValidator()]),
            EmailOperations(userId),
            null!,
            _unitOfWork, _logger);

    private ProfileEmailOperations EmailOperations(Guid? userId)
    {
        var codes = new InMemoryLoginCodeRepository();
        var clock = new FakeTimeProvider();
        var options = Options.Create(new LoginCodeOptions());
        var hasher = new FakeLoginCodeHasher();
        return new ProfileEmailOperations(
            new FakeCurrentUser { UserId = userId }, _identity, _identity,
            new LoginCodeIssuer(codes, new FakeLoginCodeGenerator(), hasher, options,
                Options.Create(new WhatsAppLoginOptions()), clock, NullLogger<LoginCodeIssuer>.Instance),
            new DestinationCodeVerifier(codes, hasher, clock),
            new FakeEmailTemplateRenderer(), new FakeEmailQueue(), options,
            new ServiceRequestValidator<RequestEmailCodeRequest>([new RequestEmailCodeRequestValidator()]),
            new ServiceRequestValidator<ConfirmEmailRequest>([new ConfirmEmailRequestValidator(options)]),
            _unitOfWork);
    }
}
