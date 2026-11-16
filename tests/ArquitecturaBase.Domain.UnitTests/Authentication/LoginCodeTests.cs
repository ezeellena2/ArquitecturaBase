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
        Assert.Equal(LoginCodeChannel.Phone, verification.Channel);
        Assert.Equal(LoginCodePurpose.VerifyDestination, verification.Purpose);
        Assert.Equal(Owner, verification.RequestedByUserId);
        Assert.Null(verification.SentAtUtc);
    }

    // N7: el tope diario por dirección cuenta los códigos de la misma dirección agrupada; sin dirección conocida, null.
    [Fact]
    public void Issued_code_keeps_the_grouped_address_that_requested_it()
    {
        var code = LoginCode.Issue(Ana, LoginCodePurpose.SignIn, null, Hash, Now, Lifetime, 5, requestedFromAddress: "2001:db8:1:2::/64");

        Assert.Equal("2001:db8:1:2::/64", code.RequestedFromAddress);
        Assert.Null(Issue().RequestedFromAddress);
        Assert.Throws<ArgumentException>(() =>
            LoginCode.Issue(Ana, LoginCodePurpose.SignIn, null, Hash, Now, Lifetime, 5, new string('1', LoginCode.MaxRequestedFromAddressLength + 1)));
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

    /// <summary>
    /// N3: «ya usado» sale sólo para el código que coincide. Con uno cualquiera, la respuesta es la de no tener código, sin
    /// intentos restantes: si no, cualquiera sabría que alguien ya entró con ese destino.
    /// </summary>
    [Fact]
    public void A_wrong_code_against_a_used_one_answers_like_no_code_and_spends_no_attempt()
    {
        var code = Issue();
        code.Verify(Hash, Now);

        var result = code.Verify("HASH-WRONG", Now.AddMinutes(1));

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Null(result.Error.Metadata);
        Assert.Equal(0, code.FailedAttempts);
    }

    /// <summary>N3: «vencido» sale sólo para el código que coincide; con otro, la respuesta es la de no tener código.</summary>
    [Fact]
    public void Expired_code_answers_expired_only_to_the_right_code_without_counting_an_attempt()
    {
        var code = Issue();

        var right = code.Verify(Hash, Now + Lifetime);
        var wrong = code.Verify("HASH-WRONG", Now + Lifetime);

        Assert.Equal(LoginCodeErrors.ExpiredCode, right.Error.Code);
        Assert.Equal(LoginCodeErrors.InvalidCode, wrong.Error.Code);
        Assert.Null(wrong.Error.Metadata);
        Assert.Equal(0, code.FailedAttempts);
        Assert.Null(code.ConsumedAtUtc);
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
        Assert.Null(code.VerifyFor(Owner, "HASH-WRONG", Now).Error.Metadata);
        Assert.Equal(LoginCodeErrors.ExpiredCode, IssueToVerifyDestination().VerifyFor(Owner, Hash, Now + Lifetime).Error.Code);
        Assert.Equal(LoginCodeErrors.InvalidCode, IssueToVerifyDestination().VerifyFor(Owner, "HASH-WRONG", Now + Lifetime).Error.Code);
    }

    [Fact]
    public void Checking_a_code_to_verify_a_destination_counts_a_wrong_one_but_does_not_spend_the_right_one()
    {
        // N11: un cambio que pide dos códigos los revisa a los dos antes de gastar alguno.
        var code = IssueToVerifyDestination();

        var wrong = code.CheckFor(Owner, "HASH-WRONG", Now);
        var right = code.CheckFor(Owner, Hash, Now);
        var stillRight = code.CheckFor(Owner, Hash, Now);

        Assert.Equal(LoginCodeErrors.InvalidCode, wrong.Error.Code);
        Assert.Equal(4, wrong.Error.Metadata![LoginCodeErrors.AttemptsLeftKey]);
        Assert.True(right.IsSuccess);
        Assert.True(stillRight.IsSuccess);
        Assert.Null(code.ConsumedAtUtc);
        Assert.Equal(1, code.FailedAttempts);

        code.Consume(Now);

        Assert.Equal(Now, code.ConsumedAtUtc);
        Assert.Equal(LoginCodeErrors.AlreadyUsedCode, code.CheckFor(Owner, Hash, Now).Error.Code);
    }

    [Fact]
    public void Checking_follows_the_same_rules_as_verifying_for_other_accounts_and_sign_in_codes()
    {
        var foreign = IssueToVerifyDestination().CheckFor(Guid.CreateVersion7(), Hash, Now);
        var signIn = Issue();

        Assert.Equal(LoginCodeErrors.InvalidCode, foreign.Error.Code);
        Assert.Null(foreign.Error.Metadata);
        Assert.Equal(LoginCodeErrors.InvalidCode, signIn.CheckFor(Owner, Hash, Now).Error.Code);
        Assert.Equal(0, signIn.FailedAttempts);
    }

    [Fact]
    public void Only_an_active_code_can_be_consumed()
    {
        var expired = IssueToVerifyDestination();
        var used = IssueToVerifyDestination();
        used.Consume(Now);

        Assert.Throws<InvalidOperationException>(() => expired.Consume(Now + Lifetime));
        Assert.Throws<InvalidOperationException>(() => used.Consume(Now));
    }

    [Fact]
    public void A_code_to_confirm_a_change_needs_the_account_that_requested_it()
    {
        Assert.Throws<ArgumentException>(() =>
            LoginCode.Issue(AnaPhone, LoginCodePurpose.ConfirmChange, requestedByUserId: null, Hash, Now, Lifetime, 5));
        Assert.Throws<ArgumentException>(() =>
            LoginCode.Issue(AnaPhone, LoginCodePurpose.ConfirmChange, Guid.Empty, Hash, Now, Lifetime, 5));
    }

    [Fact]
    public void A_code_to_confirm_a_change_is_checked_and_verified_only_for_the_account_that_requested_it()
    {
        // Cada medio confirma los cambios del otro (2026-11-16): el código que confirma es de la cuenta, como el que
        // verifica un destino, y lo que lo separa de ese es su propósito (el repositorio busca por propósito).
        var checkedCode = IssueToConfirmChange();
        var verifiedCode = IssueToConfirmChange();

        Assert.True(checkedCode.CheckFor(Owner, Hash, Now).IsSuccess);
        Assert.Null(checkedCode.ConsumedAtUtc);
        Assert.Equal(LoginCodeErrors.InvalidCode, checkedCode.CheckFor(Guid.CreateVersion7(), Hash, Now).Error.Code);
        Assert.Equal(LoginCodeErrors.InvalidCode, IssueToConfirmChange().VerifyFor(Guid.CreateVersion7(), Hash, Now).Error.Code);
        Assert.True(verifiedCode.VerifyFor(Owner, Hash, Now).IsSuccess);
        Assert.Equal(Now, verifiedCode.ConsumedAtUtc);
        Assert.Equal(LoginCodeErrors.InvalidCode, IssueToConfirmChange().Verify(Hash, Now).Error.Code);
    }

    private static LoginCode IssueToConfirmChange() =>
        LoginCode.Issue(AnaPhone, LoginCodePurpose.ConfirmChange, Owner, Hash, Now, Lifetime, maxAttempts: 5);

    [Fact]
    public void Marking_it_sent_keeps_the_first_send()
    {
        var code = Issue();

        code.MarkSent(Now.AddSeconds(1));
        code.MarkSent(Now.AddSeconds(2));

        Assert.Equal(Now.AddSeconds(1), code.SentAtUtc);
    }
}
