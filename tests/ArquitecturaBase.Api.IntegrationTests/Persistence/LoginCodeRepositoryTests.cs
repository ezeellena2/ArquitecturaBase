using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

[Collection(ApiTestGroup.Name)]
public sealed class LoginCodeRepositoryTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime NowUtc => factory.Clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task Code_state_survives_a_round_trip()
    {
        var email = UniqueEmail();
        var code = Issue(email, NowUtc);
        code.Verify("wrong-hash", NowUtc);
        await SaveAsync(code);

        var loaded = await factory.ExecuteDbContextAsync(db => new LoginCodeRepository(db).GetLatestAsync(email, Ct));

        Assert.Equal(code.Id, loaded!.Id);
        Assert.Equal(1, loaded.FailedAttempts);
        Assert.Equal(code.ExpiresAtUtc, loaded.ExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, loaded.ExpiresAtUtc.Kind);
    }

    [Fact]
    public async Task Latest_code_is_the_newest_one_of_that_email()
    {
        var email = UniqueEmail();
        var older = Issue(email, NowUtc);
        var newer = Issue(email, NowUtc.AddSeconds(30));
        older.Invalidate(NowUtc.AddSeconds(30));
        await SaveAsync(older, newer, Issue(UniqueEmail(), NowUtc.AddSeconds(60)));

        var latest = await factory.ExecuteDbContextAsync(db => new LoginCodeRepository(db).GetLatestAsync(email, Ct));

        Assert.Equal(newer.Id, latest!.Id);
    }

    [Fact]
    public async Task Latest_code_is_returned_even_if_it_was_already_used()
    {
        var email = UniqueEmail();
        var code = Issue(email, NowUtc);
        code.Verify(code.CodeHash, NowUtc);
        await SaveAsync(code);

        var latest = await factory.ExecuteDbContextAsync(db => new LoginCodeRepository(db).GetLatestAsync(email, Ct));

        Assert.Equal(code.Id, latest!.Id);
        Assert.NotNull(latest.ConsumedAtUtc);
    }

    [Fact]
    public async Task Active_codes_exclude_consumed_invalidated_and_expired_ones()
    {
        var email = UniqueEmail();
        var active = Issue(email, NowUtc);
        var consumed = Issue(email, NowUtc);
        consumed.Verify(consumed.CodeHash, NowUtc);
        var invalidated = Issue(email, NowUtc);
        invalidated.Invalidate(NowUtc);
        var expired = Issue(email, NowUtc.AddMinutes(-11));
        await SaveAsync(active, consumed, invalidated, expired);

        var codes = await factory.ExecuteDbContextAsync(db => new LoginCodeRepository(db).ListActiveAsync(email, NowUtc, Ct));

        Assert.Equal(active.Id, Assert.Single(codes).Id);
    }

    [Fact]
    public async Task Request_times_inside_the_window_are_listed_from_oldest_to_newest()
    {
        var email = UniqueEmail();
        await SaveAsync(
            Issue(email, NowUtc.AddMinutes(-20)),
            Issue(email, NowUtc.AddMinutes(-2)),
            Issue(email, NowUtc.AddMinutes(-10)));

        var times = await factory.ExecuteDbContextAsync(db =>
            new LoginCodeRepository(db).ListRequestTimesSinceAsync(email, NowUtc.AddMinutes(-15), Ct));

        Assert.Equal([NowUtc.AddMinutes(-10), NowUtc.AddMinutes(-2)], times);
    }

    [Fact]
    public async Task Audits_are_saved()
    {
        var email = UniqueEmail();
        var audit = LoginAudit.Failure(email.Value, null, LoginMethod.Code, LoginCodeErrors.InvalidCode, "203.0.113.10", "tests", NowUtc);

        await factory.ExecuteDbContextAsync(db =>
        {
            new LoginAuditRepository(db).Add(audit);
            return db.SaveChangesAsync(Ct);
        });

        var saved = await factory.ExecuteDbContextAsync(db => db.LoginAudits.SingleAsync(a => a.Email == email.Value, Ct));
        Assert.Equal(LoginMethod.Code, saved.Method);
        Assert.Equal(LoginCodeErrors.InvalidCode, saved.FailureReason);
    }

    private static Email UniqueEmail() =>
        Email.Create("codes-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com").Value;

    private static LoginCode Issue(Email email, DateTime nowUtc) =>
        LoginCode.Issue(email, "hash-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture), nowUtc, TimeSpan.FromMinutes(10), 5);

    private Task<int> SaveAsync(params LoginCode[] codes) =>
        factory.ExecuteDbContextAsync(db =>
        {
            db.LoginCodes.AddRange(codes);
            return db.SaveChangesAsync(Ct);
        });
}
