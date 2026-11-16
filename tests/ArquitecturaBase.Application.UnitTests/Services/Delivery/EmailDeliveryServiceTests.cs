using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Application.Services.Delivery;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Services.Delivery;

/// <summary>
/// N5 and N8: what the email worker records of an invitation it sent, could not send or may have sent, each in its own boundary and
/// under the account's invitation lock; an email that carries no invitation records nothing and opens no boundary.
/// </summary>
public sealed class EmailDeliveryServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryUserInvitationRepository _invitations = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly UserInvitation _invitation = UserInvitation.ByEmail(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

    public EmailDeliveryServiceTests()
    {
        _invitations.Add(_invitation);
        _invitations.InTransaction = () => _unitOfWork.InTransaction;
    }

    [Fact]
    public async Task A_sent_invitation_keeps_its_message_id_after_the_account_lock()
    {
        var result = await Service().RecordSentAsync(Invitation(), "<abc@example.test>", Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("<abc@example.test>", _invitation.ProviderMessageId);
        Assert.Equal(["lock:" + _invitation.UserId, "read:GetByIdAsync"], _invitations.Events);
        Assert.Equal((1, 1), (_unitOfWork.Transactions, _unitOfWork.Commits));
    }

    [Fact]
    public async Task An_invitation_that_did_not_go_out_is_marked_as_failed()
    {
        await Service().RecordUnsentAsync(Invitation(), Ct);

        Assert.Equal((true, false, null), (_invitation.SendFailed, _invitation.SendUncertain, _invitation.ProviderMessageId));
        Assert.Equal(1, _unitOfWork.Commits);
    }

    [Fact]
    public async Task An_invitation_that_may_have_arrived_is_marked_as_uncertain()
    {
        await Service().RecordUncertainAsync(Invitation(), Ct);

        Assert.Equal((false, true), (_invitation.SendFailed, _invitation.SendUncertain));
        Assert.Equal(1, _unitOfWork.Commits);
    }

    [Fact]
    public async Task An_email_without_an_invitation_records_nothing()
    {
        var code = new EmailMessage("ana@example.test", "123456", "<p>123456</p>", "123456");

        await Service().RecordSentAsync(code, "<abc@example.test>", Ct);
        await Service().RecordUnsentAsync(code, Ct);
        await Service().RecordUncertainAsync(code, Ct);

        Assert.Equal(0, _unitOfWork.Transactions);
        Assert.Empty(_invitations.Events);
    }

    private EmailMessage Invitation() => new("ana@example.test", "Invitación", "<p>Hola</p>", "Hola")
    {
        Invitation = new EmailInvitationReference(_invitation.UserId, _invitation.Id),
    };

    private EmailDeliveryService Service() => new(_invitations, _unitOfWork, NullLogger<EmailDeliveryService>.Instance);
}
