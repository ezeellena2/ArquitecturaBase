using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Validation.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

public sealed class UserServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("email")]
    [InlineData("-displayName")]
    [InlineData("createdAtUtc")]
    public void Whitelisted_fields_can_be_sorted(string sort) =>
        Assert.True(new ListUsersRequestValidator().Validate(new ListUsersRequest { Sort = sort }).IsValid);

    [Fact]
    public void Other_fields_cannot_be_sorted() =>
        Assert.False(new ListUsersRequestValidator().Validate(new ListUsersRequest { Sort = "passwordHash" }).IsValid);

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public void A_present_role_filter_without_a_value_is_rejected(string role) =>
        Assert.False(new ListUsersRequestValidator().Validate(new ListUsersRequest { Role = role }).IsValid);

    [Fact]
    public void A_role_that_does_not_exist_is_not_a_validation_error() =>
        Assert.True(new ListUsersRequestValidator().Validate(new ListUsersRequest { Role = "NoExiste" }).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4000)]
    public void Days_outside_the_range_are_rejected(int days) =>
        Assert.False(new ListUsersRequestValidator().Validate(new ListUsersRequest { CreatedWithinDays = days }).IsValid);

    [Fact]
    public async Task Invalid_list_request_fails_before_accessing_identity()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ListUsersAsync(new ListUsersRequest { Sort = "passwordHash" }, Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.True(error.Errors.ContainsKey("sort"));
        Assert.Null(fixture.Identity.LastListRequest);
        Assert.Equal(
            ["Handling GetUsersQuery", "GetUsersQuery failed with Validation.Failed"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
        Assert.Equal(LogLevel.Warning, fixture.Logger.Collector.GetSnapshot()[1].Level);
    }

    [Fact]
    public async Task List_preserves_the_request_and_formats_only_valid_phone_numbers()
    {
        var fixture = new Fixture();
        var withPhone = fixture.Identity.AddUser(email: null, phoneNumber: "+5493515550101");
        var withoutPhone = fixture.Identity.AddUser("ana@example.com");
        var request = new ListUsersRequest { Page = 1, PageSize = 10, Search = "ana" };

        var result = await fixture.Service.ListUsersAsync(request, Ct);

        Assert.True(result.IsSuccess);
        Assert.Same(request, fixture.Identity.LastListRequest);
        Assert.Equal("formatted +5493515550101", result.Value.Items.Single(item => item.Id == withPhone.Id).FormattedPhoneNumber);
        Assert.Null(result.Value.Items.Single(item => item.Id == withoutPhone.Id).FormattedPhoneNumber);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(["Handling GetUsersQuery", "Handled GetUsersQuery"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Counts_validate_filters_before_accessing_identity()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.GetUserFilterCountsAsync(new UserFilterCountsRequest { Role = " " }, Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.True(error.Errors.ContainsKey("role"));
        Assert.Null(fixture.Identity.LastListRequest);
    }

    [Fact]
    public async Task Counts_pass_the_filters_to_the_identity_adapter()
    {
        var fixture = new Fixture();
        fixture.Identity.AddUser("ana@example.com");
        var request = new UserFilterCountsRequest { Search = "ana", IsActive = true };

        var result = await fixture.Service.GetUserFilterCountsAsync(request, Ct);

        Assert.True(result.IsSuccess);
        Assert.Same(request, fixture.Identity.LastListRequest);
        Assert.Equal(1, result.Value.Status.Active);
        Assert.Equal(["Handling GetUserFilterCountsQuery", "Handled GetUserFilterCountsQuery"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Missing_user_returns_not_found_and_logs_the_error_code()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.GetUserAsync(Guid.CreateVersion7(), Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.Empty(fixture.MessagesLog.Events);
        Assert.Equal(["Handling GetUserQuery", "GetUserQuery failed with Users.User.NotFound"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Detail_formats_the_phone_and_shows_email_invitation_without_a_delivery_status()
    {
        var fixture = new Fixture();
        var user = fixture.Identity.AddUser(email: null, phoneNumber: "+5493515550101");
        var sentAt = DateTime.UnixEpoch;
        fixture.Invitations.Invitations.Add(UserInvitation.ByEmail(user.Id, Guid.CreateVersion7(), sentAt));

        var result = await fixture.Service.GetUserAsync(user.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("formatted +5493515550101", result.Value.FormattedPhoneNumber);
        Assert.Equal(UserInvitationChannel.Email, result.Value.LastInvitation?.Channel);
        Assert.Equal(sentAt, result.Value.LastInvitation?.SentAtUtc);
        Assert.Null(result.Value.LastInvitation?.DeliveryStatus);
        Assert.Empty(fixture.MessagesLog.Events);
    }

    [Theory]
    [InlineData(WhatsAppMessageStatus.Read, InvitationDeliveryStatus.Read)]
    [InlineData(WhatsAppMessageStatus.Failed, InvitationDeliveryStatus.Failed)]
    public async Task Detail_projects_the_whatsapp_invitation_delivery_status(
        WhatsAppMessageStatus status,
        InvitationDeliveryStatus expected)
    {
        var fixture = new Fixture();
        var user = fixture.Identity.AddUser(email: null, phoneNumber: "+5493515550101");
        var invitation = UserInvitation.ByWhatsApp(user.Id, Guid.CreateVersion7(), DateTime.UnixEpoch);
        invitation.AttachWhatsAppMessage("wamid.invitation");
        fixture.Invitations.Invitations.Add(invitation);
        var message = WhatsAppMessage.Outbound(null, "wamid.invitation", WhatsAppMessageKind.Template, "[invitación]", DateTime.UnixEpoch);
        message.ApplyStatus(status, DateTime.UnixEpoch.AddMinutes(1), status is WhatsAppMessageStatus.Failed ? 131026 : null);
        fixture.Messages.Messages.Add(message);

        var result = await fixture.Service.GetUserAsync(user.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.LastInvitation?.DeliveryStatus);
        Assert.Contains("read:ListOutboundAsync", fixture.MessagesLog.Events);
    }

    private sealed class Fixture : UserServiceTestHost;
}