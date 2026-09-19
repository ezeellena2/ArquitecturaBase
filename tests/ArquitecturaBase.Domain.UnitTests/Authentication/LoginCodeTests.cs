using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Domain.UnitTests.Authentication;

public sealed class LoginCodeTests
{
    private const string Hash = "HASH-OK";
    private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private static LoginCode Issue() =>
        LoginCode.Issue(Email.Create("ana@example.com").Value, Hash, Now, Lifetime, maxAttempts: 5);

    [Fact]
    public void Issued_code_is_active_until_it_expires()
    {
        var code = Issue();

        Assert.Equal("ana@example.com", code.Email);
        Assert.Equal(Now + Lifetime, code.ExpiresAtUtc);
        Assert.True(code.IsActive(Now));
        Assert.False(code.IsActive(Now + Lifetime));
    }

    [Fact]
    public void Issue_rejects_invalid_arguments()
    {
        var email = Email.Create("ana@example.com").Value;

        Assert.Throws<ArgumentOutOfRangeException>(() => LoginCode.Issue(email, Hash, Now, TimeSpan.Zero, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => LoginCode.Issue(email, Hash, Now, Lifetime, 0));
        Assert.Throws<ArgumentException>(() => LoginCode.Issue(email, " ", Now, Lifetime, 5));
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
}
