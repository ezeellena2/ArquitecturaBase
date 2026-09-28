using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.Validation.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

public sealed class ProfileServiceTests
{
    private readonly InMemoryUserAccounts _accounts = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeLogger<ProfileService> _logger = new();

    public ProfileServiceTests() => _accounts.InTransaction = () => _unitOfWork.InTransaction;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Update_changes_the_name_culture_and_time_zone_and_confirms_the_unit_of_work()
    {
        var user = _accounts.AddUser("ana@example.com");

        var result = await Service(user.Id).UpdateAsync(
            new UpdateProfileRequest("Ana", "en", "America/Sao_Paulo"), Ct);

        Assert.True(result.IsSuccess);
        var updated = await _accounts.FindByIdAsync(user.Id, Ct);
        Assert.Equal("Ana", updated!.DisplayName);
        Assert.Equal("en", updated.Culture);
        Assert.Equal("America/Sao_Paulo", updated.TimeZoneId);
        Assert.Equal(1, _unitOfWork.Commits);
        Assert.Equal(["Handling UpdateProfile", "Handled UpdateProfile"],
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
        var user = _accounts.AddUser("ana@example.com");

        var result = await Service(user.Id).UpdateAsync(new UpdateProfileRequest("Ana", culture, timeZoneId), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Contains(field, error.Errors.Keys);
        Assert.Null((await _accounts.FindByIdAsync(user.Id, Ct))!.DisplayName);
        Assert.Equal(0, _unitOfWork.Transactions);
        Assert.Equal(["Handling UpdateProfile", "UpdateProfile failed with Validation.Failed"],
            _logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Display_name_longer_than_the_limit_is_rejected_before_writing()
    {
        var user = _accounts.AddUser("ana@example.com");

        var result = await Service(user.Id).UpdateAsync(new UpdateProfileRequest(
            new string('A', ValidationRules.DisplayNameMaxLength + 1), "es", "America/Argentina/Buenos_Aires"), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("displayName", error.Errors.Keys);
        Assert.Null((await _accounts.FindByIdAsync(user.Id, Ct))!.DisplayName);
        Assert.Equal(0, _unitOfWork.Transactions);
    }

    [Fact]
    public async Task Unknown_user_cannot_update_a_profile()
    {
        var result = await Service(Guid.CreateVersion7()).UpdateAsync(
            new UpdateProfileRequest("Ana", "es", "America/Argentina/Buenos_Aires"), Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.Equal(0, _unitOfWork.Commits);
        Assert.Equal(1, _unitOfWork.Rollbacks);
        Assert.Equal(["Handling UpdateProfile", "UpdateProfile failed with Users.User.NotFound"],
            _logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Anonymous_request_cannot_update_a_profile()
    {
        var result = await Service(userId: null).UpdateAsync(
            new UpdateProfileRequest("Ana", "es", "America/Argentina/Buenos_Aires"), Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.Equal(0, _unitOfWork.Commits);
        Assert.Equal(1, _unitOfWork.Rollbacks);
    }

    private ProfileService Service(Guid? userId) =>
        new(new FakeCurrentUser { UserId = userId }, _accounts, _accounts, null!, null!,
            RequestValidators.For(new UpdateProfileRequestValidator()),
            _unitOfWork, _logger);
}
