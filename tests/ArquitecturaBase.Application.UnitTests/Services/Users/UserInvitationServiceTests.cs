using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

public sealed class UserInvitationServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Missing_channel_is_validated_before_taking_a_lock()
    {
        var fixture = new UserServiceTestHost();

        var result = await fixture.Service.SendInvitationAsync(
            new SendUserInvitationRequest(Guid.CreateVersion7(), null, false), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal("Este campo es obligatorio.", error.Errors["channel"].Single());
        Assert.Empty(fixture.Invitations.Events);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task An_inactive_account_is_not_invited_or_saved()
    {
        var fixture = new UserServiceTestHost();
        var user = fixture.Identity.AddUser("inactive@example.test", isActive: false);

        var result = await fixture.Service.SendInvitationAsync(
            new SendUserInvitationRequest(user.Id, UserInvitationChannel.Email, false), Ct);

        Assert.Equal(UserInvitationErrors.UserInactive, result.Error);
        Assert.Equal(["lock:" + user.Id], fixture.Invitations.Events);
        Assert.Empty(fixture.EmailQueue.Messages);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task A_successful_resend_locks_before_reading_and_saves_once()
    {
        var fixture = new UserServiceTestHost();
        var user = fixture.Identity.AddUser("invited@example.test", culture: "en");

        var result = await fixture.Service.SendInvitationAsync(
            new SendUserInvitationRequest(user.Id, UserInvitationChannel.Email, false), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["lock:" + user.Id, "read:GetLatestSentAsync", "lock:" + user.Id], fixture.Invitations.Events);
        Assert.Single(fixture.EmailQueue.Messages);
        Assert.Single(fixture.Invitations.Invitations);
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Cooldown_rejects_a_second_resend_without_saving_or_queueing()
    {
        var fixture = new UserServiceTestHost();
        var user = fixture.Identity.AddUser("invited@example.test");
        var request = new SendUserInvitationRequest(user.Id, UserInvitationChannel.Email, false);
        Assert.True((await fixture.Service.SendInvitationAsync(request, Ct)).IsSuccess);

        var result = await fixture.Service.SendInvitationAsync(request, Ct);

        Assert.Equal(UserInvitationErrors.TooManyRequestsCode, result.Error.Code);
        Assert.Single(fixture.EmailQueue.Messages);
        Assert.Single(fixture.Invitations.Invitations);
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCalls);
    }
}
