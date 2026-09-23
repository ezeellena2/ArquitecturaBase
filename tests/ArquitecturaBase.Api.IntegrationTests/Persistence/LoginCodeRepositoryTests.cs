using System.Globalization;
using System.Security.Cryptography;
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

    private const LoginCodePurpose SignIn = LoginCodePurpose.SignIn;

    private DateTime NowUtc => factory.Clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task Code_state_survives_a_round_trip()
    {
        var email = UniqueEmail();
        var code = Issue(email, NowUtc);
        code.Verify("wrong-hash", NowUtc);
        await SaveAsync(code);

        var loaded = await factory.ExecuteDbContextAsync(db => new LoginCodeRepository(db).GetLatestAsync(email, SignIn, Ct));

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

        var latest = await factory.ExecuteDbContextAsync(db => new LoginCodeRepository(db).GetLatestAsync(email, SignIn, Ct));

        Assert.Equal(newer.Id, latest!.Id);
    }

    [Fact]
    public async Task Latest_code_wins_over_an_older_one_issued_in_the_same_instant()
    {
        // Dos pedidos en el mismo instante comparten CreatedAtUtc, asi que la fecha sola no alcanza para decir
        // cual es el ultimo: sin desempate, la base devuelve cualquiera de los dos y el codigo recien emitido se
        // rechaza con "ya se uso". Pasa en los tests, donde el reloj esta congelado, y podria pasar en produccion.
        var email = UniqueEmail();
        var consumed = new List<LoginCode>();

        for (var i = 0; i < 3; i++)
        {
            var code = Issue(email, NowUtc);
            code.Verify(code.CodeHash, NowUtc);
            consumed.Add(code);
        }

        var newest = Issue(email, NowUtc);
        await SaveAsync([.. consumed, newest]);

        var latest = await factory.ExecuteDbContextAsync(db => new LoginCodeRepository(db).GetLatestAsync(email, SignIn, Ct));

        Assert.Equal(newest.Id, latest!.Id);
        Assert.Null(latest.ConsumedAtUtc);
    }

    [Fact]
    public async Task Latest_code_is_returned_even_if_it_was_already_used()
    {
        var email = UniqueEmail();
        var code = Issue(email, NowUtc);
        code.Verify(code.CodeHash, NowUtc);
        await SaveAsync(code);

        var latest = await factory.ExecuteDbContextAsync(db => new LoginCodeRepository(db).GetLatestAsync(email, SignIn, Ct));

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

        var codes = await factory.ExecuteDbContextAsync(db => new LoginCodeRepository(db).ListActiveAsync(email, SignIn, NowUtc, Ct));

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

        var saved = await factory.ExecuteDbContextAsync(db => db.LoginAudits.SingleAsync(a => a.Identifier == email.Value, Ct));
        Assert.Equal(LoginMethod.Code, saved.Method);
        Assert.Equal(LoginCodeErrors.InvalidCode, saved.FailureReason);
    }

    [Fact]
    public async Task Every_field_of_a_whatsapp_code_survives_a_round_trip()
    {
        var phone = UniquePhone();
        var owner = Guid.CreateVersion7();
        var code = LoginCode.Issue(phone, LoginCodePurpose.VerifyDestination, owner, "hash-phone", NowUtc, TimeSpan.FromMinutes(10), 5);
        code.MarkSent(NowUtc.AddSeconds(1));
        await SaveAsync(code);

        var loaded = await factory.ExecuteDbContextAsync(db =>
            new LoginCodeRepository(db).GetLatestAsync(phone, LoginCodePurpose.VerifyDestination, Ct));

        Assert.Equal(phone.Value, loaded!.Destination);
        Assert.Equal(LoginCodeChannel.WhatsApp, loaded.Channel);
        Assert.Equal(LoginCodePurpose.VerifyDestination, loaded.Purpose);
        Assert.Equal(owner, loaded.RequestedByUserId);
        Assert.Equal(NowUtc.AddSeconds(1), loaded.SentAtUtc);
        Assert.Equal(DateTimeKind.Utc, loaded.SentAtUtc!.Value.Kind);
    }

    [Fact]
    public async Task Latest_code_is_the_newest_one_of_that_purpose()
    {
        var email = UniqueEmail();
        var signIn = Issue(email, NowUtc);
        var verification = Issue(email, NowUtc.AddSeconds(30), LoginCodePurpose.VerifyDestination);
        await SaveAsync(signIn, verification);

        var (latestSignIn, latestVerification) = await factory.ExecuteDbContextAsync(async db =>
        {
            var repository = new LoginCodeRepository(db);

            return (
                await repository.GetLatestAsync(email, SignIn, Ct),
                await repository.GetLatestAsync(email, LoginCodePurpose.VerifyDestination, Ct));
        });

        Assert.Equal(signIn.Id, latestSignIn!.Id);
        Assert.Equal(verification.Id, latestVerification!.Id);
    }

    [Fact]
    public async Task Active_codes_are_only_the_ones_of_that_purpose()
    {
        var email = UniqueEmail();
        var signIn = Issue(email, NowUtc);
        await SaveAsync(signIn, Issue(email, NowUtc, LoginCodePurpose.VerifyDestination));

        var codes = await factory.ExecuteDbContextAsync(db => new LoginCodeRepository(db).ListActiveAsync(email, SignIn, NowUtc, Ct));

        Assert.Equal(signIn.Id, Assert.Single(codes).Id);
    }

    [Fact]
    public async Task Request_times_count_every_purpose_of_the_destination()
    {
        // Los límites son por destino: protegen a quien recibe los mensajes, sea cual sea el motivo.
        var email = UniqueEmail();
        await SaveAsync(
            Issue(email, NowUtc.AddMinutes(-3)),
            Issue(email, NowUtc.AddMinutes(-2), LoginCodePurpose.VerifyDestination),
            Issue(UniqueEmail(), NowUtc.AddMinutes(-1)));

        var times = await factory.ExecuteDbContextAsync(db =>
            new LoginCodeRepository(db).ListRequestTimesSinceAsync(email, NowUtc.AddMinutes(-15), Ct));

        Assert.Equal([NowUtc.AddMinutes(-3), NowUtc.AddMinutes(-2)], times);
    }

    [Theory]
    [InlineData(LoginMethod.WhatsAppCode)]
    [InlineData(LoginMethod.WhatsAppLink)]
    public async Task Whatsapp_audits_are_saved_with_the_number(LoginMethod method)
    {
        var phone = UniquePhone().Value;
        var audit = LoginAudit.Success(phone, Guid.CreateVersion7(), method, "203.0.113.10", "tests", NowUtc);

        await factory.ExecuteDbContextAsync(db =>
        {
            new LoginAuditRepository(db).Add(audit);
            return db.SaveChangesAsync(Ct);
        });

        var saved = await factory.ExecuteDbContextAsync(db => db.LoginAudits.SingleAsync(a => a.Identifier == phone, Ct));
        Assert.Equal(method, saved.Method);
    }

    private static LoginCodeDestination UniqueEmail() =>
        LoginCodeDestination.ForEmail(
            Email.Create("codes-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com").Value);

    private static LoginCodeDestination UniquePhone() =>
        LoginCodeDestination.ForPhone(PhoneNumber.Create(
            "+54911" + RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString(CultureInfo.InvariantCulture)).Value);

    private static LoginCode Issue(
        LoginCodeDestination destination,
        DateTime nowUtc,
        LoginCodePurpose purpose = LoginCodePurpose.SignIn) =>
        LoginCode.Issue(
            destination,
            purpose,
            purpose is LoginCodePurpose.VerifyDestination ? Guid.CreateVersion7() : null,
            "hash-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            nowUtc,
            TimeSpan.FromMinutes(10),
            5);

    private Task<int> SaveAsync(params LoginCode[] codes) =>
        factory.ExecuteDbContextAsync(db =>
        {
            db.LoginCodes.AddRange(codes);
            return db.SaveChangesAsync(Ct);
        });
}
