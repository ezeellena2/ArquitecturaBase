using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Domain.UnitTests.Authentication;

public sealed class LoginCodeTests
{
    private const string Hash = "HASH-OK";
    private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly LoginCodeDestination Ana = LoginCodeDestination.ForEmail(Email.Create("ana@example.com").Value);
    private static readonly LoginCodeDestination AnaPhone = LoginCodeDestination.ForPhone(PhoneNumber.Create("+5491123456789").Value);
    private static readonly Guid Owner = Guid.CreateVersion7();

    private static LoginCode Issue() =>
        LoginCode.Issue(Ana, LoginCodePurpose.SignIn, requestedByUserId: null, Hash, Now, Lifetime, maxAttempts: 5);

    private static LoginCode IssueToVerifyDestination() =>
        LoginCode.Issue(AnaPhone, LoginCodePurpose.VerifyDestination, Owner, Hash, Now, Lifetime, maxAttempts: 5);

    [Fact]
    public void Issued_code_is_active_until_it_expires()
    {
        var code = Issue();

        Assert.Equal("ana@example.com", code.Destination);
        Assert.Equal(Now + Lifetime, code.ExpiresAtUtc);
        Assert.True(code.IsActive(Now));
        Assert.False(code.IsActive(Now + Lifetime));
    }

    [Fact]
    public void Issued_code_records_its_destination_and_purpose_and_starts_unsent()
    {
        var signIn = Issue();
        var verification = IssueToVerifyDestination();

        Assert.Equal(LoginCodeChannel.Email, signIn.Channel);
        Assert.Equal(LoginCodePurpose.SignIn, signIn.Purpose);
        Assert.Null(signIn.RequestedByUserId);
        Assert.Null(signIn.SentAtUtc);

        Assert.Equal("+5491123456789", verification.Destination);
        Assert.Equal(LoginCodeChannel.WhatsApp, verification.Channel);
        Assert.Equal(LoginCodePurpose.VerifyDestination, verification.Purpose);
        Assert.Equal(Owner, verification.RequestedByUserId);
        Assert.Null(verification.SentAtUtc);
    }

    [Fact]
    public void Issue_rejects_invalid_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => LoginCode.Issue(null!, LoginCodePurpose.SignIn, null, Hash, Now, Lifetime, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => LoginCode.Issue(Ana, LoginCodePurpose.SignIn, null, Hash, Now, TimeSpan.Zero, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => LoginCode.Issue(Ana, LoginCodePurpose.SignIn, null, Hash, Now, Lifetime, 0));
        Assert.Throws<ArgumentException>(() => LoginCode.Issue(Ana, LoginCodePurpose.SignIn, null, " ", Now, Lifetime, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => LoginCode.Issue(Ana, (LoginCodePurpose)99, null, Hash, Now, Lifetime, 5));
    }

    [Fact]
    public void A_sign_in_code_does_not_belong_to_an_account()
    {
        Assert.Throws<ArgumentException>(() =>
            LoginCode.Issue(Ana, LoginCodePurpose.SignIn, Owner, Hash, Now, Lifetime, 5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_code_to_verify_a_destination_needs_the_account_that_requested_it(bool emptyId)
    {
        Guid? requestedBy = emptyId ? Guid.Empty : null;

        Assert.Throws<ArgumentException>(() =>
            LoginCode.Issue(AnaPhone, LoginCodePurpose.VerifyDestination, requestedBy, Hash, Now, Lifetime, 5));
    }

    [Fact]
    public void Correct_code_is_consumed()
    {
        var code = Issue();

        var result = code.Verify(Hash, Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddMinutes(1), code.ConsumedAtUtc);
        Assert.False(code.IsActive(Now.AddMinutes(1)));
    }

    [Fact]
    public void Wrong_code_counts_the_attempt_and_reports_the_attempts_left()
    {
        var code = Issue();

        var result = code.Verify("HASH-WRONG", Now);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Equal(4, result.Error.Metadata![LoginCodeErrors.AttemptsLeftKey]);
        Assert.Equal(1, code.FailedAttempts);
    }

    [Fact]
    public void Fifth_failure_blocks_the_code_even_for_the_right_hash()
    {
        var code = Issue();

        for (var i = 0; i < 4; i++)
        {
            code.Verify("HASH-WRONG", Now);
        }

        var fifth = code.Verify("HASH-WRONG", Now);
        var afterwards = code.Verify(Hash, Now);

        Assert.Equal(LoginCodeErrors.TooManyAttemptsCode, fifth.Error.Code);
        Assert.Equal(LoginCodeErrors.TooManyAttemptsCode, afterwards.Error.Code);
        Assert.Null(code.ConsumedAtUtc);
    }

    [Fact]
    public void Used_code_cannot_be_used_again()
    {
        var code = Issue();
        code.Verify(Hash, Now);

        var result = code.Verify(Hash, Now);

        Assert.Equal(LoginCodeErrors.AlreadyUsedCode, result.Error.Code);
    }

    [Fact]
    public void Expired_code_is_rejected_without_counting_an_attempt()
    {
        var code = Issue();

        var result = code.Verify("HASH-WRONG", Now + Lifetime);

        Assert.Equal(LoginCodeErrors.ExpiredCode, result.Error.Code);
        Assert.Equal(0, code.FailedAttempts);
    }

    [Fact]
    public void Invalidated_code_behaves_like_a_wrong_code()
    {
        var code = Issue();
        code.Invalidate(Now.AddMinutes(1));
        code.Invalidate(Now.AddMinutes(2));

        var result = code.Verify(Hash, Now.AddMinutes(3));

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Null(result.Error.Metadata);
        Assert.Equal(Now.AddMinutes(1), code.InvalidatedAtUtc);
        Assert.False(code.IsActive(Now.AddMinutes(3)));
    }

    [Fact]
    public void A_sign_in_code_does_not_verify_a_destination_and_spends_no_attempt()
    {
        var code = Issue();

        var right = code.VerifyFor(Owner, Hash, Now);
        var wrong = code.VerifyFor(Owner, "HASH-WRONG", Now);

        // La misma respuesta que cuando no hay código: sin intentos restantes y sin tocar los contadores.
        Assert.Equal(LoginCodeErrors.InvalidCode, right.Error.Code);
        Assert.Null(right.Error.Metadata);
        Assert.Equal(LoginCodeErrors.InvalidCode, wrong.Error.Code);
        Assert.Null(wrong.Error.Metadata);
        Assert.Equal(0, code.FailedAttempts);
        Assert.Null(code.ConsumedAtUtc);
        Assert.True(code.Verify(Hash, Now).IsSuccess);
    }

    [Fact]
    public void A_code_to_verify_a_destination_does_not_sign_in_and_spends_no_attempt()
    {
        var code = IssueToVerifyDestination();

        var right = code.Verify(Hash, Now);
        var wrong = code.Verify("HASH-WRONG", Now);

        Assert.Equal(LoginCodeErrors.InvalidCode, right.Error.Code);
        Assert.Null(right.Error.Metadata);
        Assert.Equal(LoginCodeErrors.InvalidCode, wrong.Error.Code);
        Assert.Null(wrong.Error.Metadata);
        Assert.Equal(0, code.FailedAttempts);
        Assert.Null(code.ConsumedAtUtc);
        Assert.True(code.VerifyFor(Owner, Hash, Now).IsSuccess);
    }

    [Fact]
    public void A_code_to_verify_a_destination_only_works_for_the_account_that_requested_it()
    {
        var code = IssueToVerifyDestination();
        var otherAccount = Guid.CreateVersion7();

        var right = code.VerifyFor(otherAccount, Hash, Now);
        var wrong = code.VerifyFor(otherAccount, "HASH-WRONG", Now);

        // A la otra cuenta se le responde como si no hubiera código: no se entera de que alguien está vinculando
        // ese destino, ni le gasta los intentos a quien lo pidió.
        Assert.Equal(LoginCodeErrors.InvalidCode, right.Error.Code);
        Assert.Null(right.Error.Metadata);
        Assert.Equal(LoginCodeErrors.InvalidCode, wrong.Error.Code);
        Assert.Null(wrong.Error.Metadata);
        Assert.Equal(0, code.FailedAttempts);
        Assert.True(code.IsActive(Now));

        Assert.True(code.VerifyFor(Owner, Hash, Now.AddMinutes(1)).IsSuccess);
        Assert.Equal(Now.AddMinutes(1), code.ConsumedAtUtc);
    }

    [Fact]
    public void A_code_to_verify_a_destination_follows_the_usual_rules_for_its_owner()
    {
        var code = IssueToVerifyDestination();

        var wrong = code.VerifyFor(Owner, "HASH-WRONG", Now);
        var right = code.VerifyFor(Owner, Hash, Now);
        var again = code.VerifyFor(Owner, Hash, Now);

        Assert.Equal(LoginCodeErrors.InvalidCode, wrong.Error.Code);
        Assert.Equal(4, wrong.Error.Metadata![LoginCodeErrors.AttemptsLeftKey]);
        Assert.True(right.IsSuccess);
        Assert.Equal(LoginCodeErrors.AlreadyUsedCode, again.Error.Code);
        Assert.Equal(LoginCodeErrors.ExpiredCode, IssueToVerifyDestination().VerifyFor(Owner, Hash, Now + Lifetime).Error.Code);
    }

    [Fact]
    public void Marking_it_sent_keeps_the_first_send()
    {
        var code = Issue();

        code.MarkSent(Now.AddSeconds(1));
        code.MarkSent(Now.AddSeconds(2));

        Assert.Equal(Now.AddSeconds(1), code.SentAtUtc);
    }
}
