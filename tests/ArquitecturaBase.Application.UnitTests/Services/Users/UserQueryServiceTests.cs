using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Validation.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

public sealed class UserQueryServiceTests
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

        var result = await fixture.Queries.ListUsersAsync(new ListUsersRequest { Sort = "passwordHash" }, Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.True(error.Errors.ContainsKey("sort"));
        Assert.Null(fixture.Accounts.LastListRequest);
        Assert.Equal(
            ["Handling ListUsers", "ListUsers failed with Validation.Failed"],
            fixture.QueryLogger.Collector.GetSnapshot().Select(record => record.Message));
        Assert.Equal(LogLevel.Warning, fixture.QueryLogger.Collector.GetSnapshot()[1].Level);
    }

    [Fact]
    public async Task List_preserves_the_request_and_formats_only_valid_phone_numbers()
    {
        var fixture = new Fixture();
        var withPhone = fixture.Accounts.AddUser(email: null, phoneNumber: "+5493515550101");
        var withoutPhone = fixture.Accounts.AddUser("ana@example.com");
        var request = new ListUsersRequest { Page = 1, PageSize = 10, Search = "ana" };

        var result = await fixture.Queries.ListUsersAsync(request, Ct);

        Assert.True(result.IsSuccess);
        Assert.Same(request, fixture.Accounts.LastListRequest);
        Assert.Equal("formatted +5493515550101", result.Value.Items.Single(item => item.Id == withPhone.Id).FormattedPhoneNumber);
        Assert.Null(result.Value.Items.Single(item => item.Id == withoutPhone.Id).FormattedPhoneNumber);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(["Handling ListUsers", "Handled ListUsers"],
            fixture.QueryLogger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Counts_validate_filters_before_accessing_identity()
    {
        var fixture = new Fixture();

        var result = await fixture.Queries.GetUserFilterCountsAsync(new ListUsersRequest { Role = " " }, Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.True(error.Errors.ContainsKey("role"));
        Assert.Null(fixture.Accounts.LastListRequest);
    }

    [Fact]
    public async Task Counts_pass_the_filters_to_the_identity_adapter()
    {
        var fixture = new Fixture();
        fixture.Accounts.AddUser("ana@example.com");
        var request = new ListUsersRequest { Search = "ana", IsActive = true };

        var result = await fixture.Queries.GetUserFilterCountsAsync(request, Ct);

        Assert.True(result.IsSuccess);
        Assert.Same(request, fixture.Accounts.LastListRequest);
        Assert.Equal(1, result.Value.Status.Active);
        Assert.Equal(["Handling GetUserFilterCounts", "Handled GetUserFilterCounts"],
            fixture.QueryLogger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Missing_user_returns_not_found_and_logs_the_error_code()
    {
        var fixture = new Fixture();

        var result = await fixture.Queries.GetUserAsync(Guid.CreateVersion7(), Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.Empty(fixture.MessagesLog.Events);
        Assert.Equal(["Handling GetUser", "GetUser failed with Users.User.NotFound"],
            fixture.QueryLogger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Detail_formats_the_phone_and_shows_email_invitation_without_a_delivery_status()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, phoneNumber: "+5493515550101");
        var sentAt = DateTime.UnixEpoch;
        fixture.InvitationReader.Latest[user.Id] = new UserInvitationRow(
            UserInvitationChannel.Email, sentAt, SendFailed: false, ProviderMessageId: null);

        var result = await fixture.Queries.GetUserAsync(user.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("formatted +5493515550101", result.Value.FormattedPhoneNumber);
        Assert.Equal(UserInvitationChannel.Email, result.Value.LastInvitation?.Channel);
        Assert.Equal(sentAt, result.Value.LastInvitation?.SentAtUtc);
        Assert.Null(result.Value.LastInvitation?.DeliveryStatus);
        Assert.Empty(fixture.MessagesLog.Events);
    }

    [Fact]
    public async Task The_detail_asks_the_status_source_of_the_channel_and_not_for_a_failed_invitation()
    {
        var fixture = new Fixture();
        fixture.DeliveryStatuses.Status = InvitationDeliveryStatus.Read;
        var sent = fixture.Accounts.AddUser(email: null, phoneNumber: "+5493515550101");
        var failed = fixture.Accounts.AddUser(email: null, phoneNumber: "+5493515550102");
        fixture.InvitationReader.Latest[sent.Id] = new UserInvitationRow(
            UserInvitationChannel.WhatsApp, DateTime.UnixEpoch, SendFailed: false, "wamid.sent");
        fixture.InvitationReader.Latest[failed.Id] = new UserInvitationRow(
            UserInvitationChannel.WhatsApp, DateTime.UnixEpoch, SendFailed: true, ProviderMessageId: null);

        var sentDetail = await fixture.Queries.GetUserAsync(sent.Id, Ct);
        var failedDetail = await fixture.Queries.GetUserAsync(failed.Id, Ct);

        // La que salió tiene el estado que da la fuente de WhatsApp, pedido por su id; la fallida no le pregunta a nadie.
        Assert.Equal(InvitationDeliveryStatus.Read, sentDetail.Value.LastInvitation?.DeliveryStatus);
        Assert.Equal(InvitationDeliveryStatus.Failed, failedDetail.Value.LastInvitation?.DeliveryStatus);
        Assert.Equal(["wamid.sent"], fixture.DeliveryStatuses.Reads);
        Assert.Equal([sent.Id, failed.Id], fixture.InvitationReader.Reads);
    }

    [Fact]
    public async Task Without_a_status_source_for_its_channel_the_invitation_has_no_delivery_status()
    {
        // El correo no tiene fuente: el detalle no le pide el estado a la de WhatsApp.
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: "ana@example.com", phoneNumber: null);
        fixture.InvitationReader.Latest[user.Id] = new UserInvitationRow(
            UserInvitationChannel.Email, DateTime.UnixEpoch, SendFailed: false, ProviderMessageId: null);

        var result = await fixture.Queries.GetUserAsync(user.Id, Ct);

        Assert.Null(result.Value.LastInvitation?.DeliveryStatus);
        Assert.Empty(fixture.DeliveryStatuses.Reads);
    }

    [Fact]
    public async Task Detail_of_a_user_never_invited_has_no_last_invitation()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: "ana@example.com", phoneNumber: null);

        var result = await fixture.Queries.GetUserAsync(user.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.LastInvitation);
        Assert.Equal([user.Id], fixture.InvitationReader.Reads);
    }

    private sealed class Fixture : UserServiceTestHost;
}