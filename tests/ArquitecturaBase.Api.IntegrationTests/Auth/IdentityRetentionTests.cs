using System.Data.Common;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

/// <summary>
/// N16 y N31: la retención de los datos del ingreso contra PostgreSQL. La base es compartida: cada test mira sus propias
/// filas, y la corre entera, con la corrida del worker (<see cref="IdentityRetentionBackgroundService.RunAsync"/>), hasta
/// que no queda ningún lote ni tramo, así lo viejo de otros tests no le cambia el resultado.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class IdentityRetentionTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime Now => factory.Clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task Codes_and_links_go_after_thirty_days_and_younger_ones_stay()
    {
        var userId = Guid.CreateVersion7();
        var old = Code(Now.AddDays(-31), requestedFromAddress: "203.0.113.7");
        var young = Code(Now.AddDays(-29), requestedFromAddress: "203.0.113.7");
        var oldLink = LoginLink.Issue(userId, Guid.NewGuid().ToString("N"), Now.AddDays(-31));
        var youngLink = LoginLink.Issue(userId, Guid.NewGuid().ToString("N"), Now.AddDays(-29));
        await factory.ExecuteDbContextAsync(db =>
        {
            db.LoginCodes.AddRange(old, young);
            db.LoginLinks.AddRange(oldLink, youngLink);
            return db.SaveChangesAsync(Ct);
        });

        await RunAsync();

        var (codes, links) = await factory.ExecuteDbContextAsync(async db => (
            await db.LoginCodes.AsNoTracking().Where(code => code.Id == old.Id || code.Id == young.Id).Select(code => code.Id).ToListAsync(Ct),
            await db.LoginLinks.AsNoTracking().Where(link => link.UserId == userId).Select(link => link.Id).ToListAsync(Ct)));
        Assert.Equal([young.Id], codes);
        Assert.Equal([youngLink.Id], links);
    }

    [Fact]
    public async Task Sign_ins_lose_the_ip_and_the_browser_and_failures_what_was_written_after_twelve_months()
    {
        var onlyOld = Guid.CreateVersion7();
        var withRecent = Guid.CreateVersion7();
        var lastOfOnlyOld = LoginAudit.Success("ana@example.com", onlyOld, LoginMethod.Code, "10.0.0.1", "Firefox", Now.AddMonths(-13));
        var oldFailure = LoginAudit.Failure("ana@example.com", onlyOld, LoginMethod.Code, "Auth.LoginCode.Invalid", "10.0.0.2", "Firefox", Now.AddMonths(-13).AddDays(-1));
        var recent = LoginAudit.Success("bea@example.com", withRecent, LoginMethod.Google, "10.0.0.3", "Edge", Now.AddMonths(-11));
        await factory.ExecuteDbContextAsync(db =>
        {
            db.LoginAudits.AddRange(lastOfOnlyOld, oldFailure, recent);
            return db.SaveChangesAsync(Ct);
        });

        await RunAsync();

        var audits = await factory.ExecuteDbContextAsync(db => db.LoginAudits.AsNoTracking()
            .Where(audit => audit.UserId == onlyOld || audit.UserId == withRecent)
            .ToDictionaryAsync(audit => audit.Id, Ct));
        Assert.Equal(3, audits.Count);
        Assert.All([audits[lastOfOnlyOld.Id], audits[oldFailure.Id]], audit => Assert.Equal((null, null), (audit.IpAddress, audit.UserAgent)));
        Assert.Equal(("10.0.0.3", "Edge"), (audits[recent.Id].IpAddress, audits[recent.Id].UserAgent));

        // Lo que se escribió en un intento fallido puede ser el correo de otro: se olvida. El de un ingreso exitoso queda.
        Assert.Equal(("ana@example.com", true), (audits[lastOfOnlyOld.Id].Identifier, audits[lastOfOnlyOld.Id].Succeeded));
        Assert.Equal((null, false, "Auth.LoginCode.Invalid"), (audits[oldFailure.Id].Identifier, audits[oldFailure.Id].Succeeded, audits[oldFailure.Id].FailureReason));
        Assert.Equal("bea@example.com", audits[recent.Id].Identifier);

        var lastSignIn = await factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<ILoginAuditRepository>().FindLastSuccessAtUtcAsync(onlyOld, Ct));
        Assert.Equal(lastOfOnlyOld.OccurredAtUtc, lastSignIn);
    }

    [Fact]
    public async Task Invitations_go_after_thirty_days_except_the_last_of_each_account()
    {
        var (invited, alsoInvited, admin) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var first = UserInvitation.ByEmail(invited, admin, Now.AddDays(-60));
        var second = UserInvitation.ByEmail(invited, admin, Now.AddDays(-40));
        var last = UserInvitation.ByEmail(invited, admin, Now.AddDays(-35));
        var failed = UserInvitation.ByEmail(alsoInvited, admin, Now.AddDays(-31));
        failed.MarkSendFailed();
        var recent = UserInvitation.ByEmail(alsoInvited, admin, Now.AddDays(-29));
        var recentButNotLast = UserInvitation.ByEmail(alsoInvited, admin, Now.AddDays(-30).AddMinutes(1));
        await factory.ExecuteDbContextAsync(db =>
        {
            db.UserInvitations.AddRange(first, second, last, failed, recentButNotLast, recent);
            return db.SaveChangesAsync(Ct);
        });

        await RunAsync();

        var kept = await factory.ExecuteDbContextAsync(db => db.UserInvitations.AsNoTracking()
            .Where(invitation => invitation.SentBy == admin).Select(invitation => invitation.Id).ToListAsync(Ct));
        Assert.Equal(new[] { last.Id, recent.Id, recentButNotLast.Id }.Order(), kept.Order());

        // «Editar usuario» sigue mostrando la última, aunque tenga más de 30 días.
        var shown = await factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IUserInvitationReader>().FindLatestAsync(invited, Ct));
        Assert.Equal(last.SentAtUtc, shown?.SentAtUtc);
    }

    [Fact]
    public async Task OpenIddict_prunes_what_expired_more_than_fourteen_days_ago()
    {
        var subject = Guid.CreateVersion7().ToString();
        var (oldAuthorization, keptAuthorization, revokedAuthorization, oldToken, recentToken, tokenOfRevoked) = await factory.ExecuteScopeAsync(async services =>
        {
            var authorizations = services.GetRequiredService<IOpenIddictAuthorizationManager>();
            var tokens = services.GetRequiredService<IOpenIddictTokenManager>();
            var oldAuthorization = await authorizations.GetIdAsync(
                await authorizations.CreateAsync(Authorization(subject, Now.AddDays(-60)), Ct), Ct);
            var keptAuthorization = await authorizations.GetIdAsync(
                await authorizations.CreateAsync(Authorization(subject, Now.AddDays(-60)), Ct), Ct);

            // Una sesión vieja que se renovó hace 5 días y se cerró: la autorización revocada conserva su último token.
            var revokedDescriptor = Authorization(subject, Now.AddDays(-60));
            revokedDescriptor.Status = Statuses.Revoked;
            var revokedAuthorization = await authorizations.GetIdAsync(await authorizations.CreateAsync(revokedDescriptor, Ct), Ct);
            var tokenOfRevoked = await tokens.CreateAsync(RefreshToken(subject, revokedAuthorization, Now.AddDays(-5)), Ct);

            // Un refresh token dura 30 días: el viejo venció hace 30 y el reciente hace 10, los dos canjeados.
            var oldToken = await tokens.CreateAsync(RefreshToken(subject, oldAuthorization, Now.AddDays(-60)), Ct);
            var recentToken = await tokens.CreateAsync(RefreshToken(subject, keptAuthorization, Now.AddDays(-40)), Ct);

            return (oldAuthorization, keptAuthorization, revokedAuthorization, await tokens.GetIdAsync(oldToken, Ct),
                await tokens.GetIdAsync(recentToken, Ct), await tokens.GetIdAsync(tokenOfRevoked, Ct));
        });

        await RunAsync();

        var found = await factory.ExecuteScopeAsync(async services =>
        {
            var authorizations = services.GetRequiredService<IOpenIddictAuthorizationManager>();
            var tokens = services.GetRequiredService<IOpenIddictTokenManager>();
            return (await authorizations.FindByIdAsync(oldAuthorization!, Ct) is not null,
                await authorizations.FindByIdAsync(keptAuthorization!, Ct) is not null,
                await authorizations.FindByIdAsync(revokedAuthorization!, Ct) is not null,
                await tokens.FindByIdAsync(oldToken!, Ct) is not null,
                await tokens.FindByIdAsync(recentToken!, Ct) is not null,
                await tokens.FindByIdAsync(tokenOfRevoked!, Ct) is not null);
        });

        // La revocada queda mientras le quede un token, que todavía no venció hace 14 días: la poda no choca con la clave
        // foránea de los tokens (si chocara, RunAsync lanzaría).
        Assert.Equal((false, true, true, false, true, true), found);
    }

    [Fact]
    public async Task A_failure_that_already_lost_its_ip_and_browser_still_loses_what_was_written()
    {
        // Un fallido que ya quedó sin IP ni navegador (sin ellos desde el principio, o limpio por la versión de antes) se
        // vuelve a visitar mientras guarde lo escrito.
        var failure = LoginAudit.Failure("carla@example.com", null, LoginMethod.Code, "Auth.LoginCode.Invalid", null, null, Now.AddMonths(-13));
        await factory.ExecuteDbContextAsync(db =>
        {
            db.LoginAudits.Add(failure);
            return db.SaveChangesAsync(Ct);
        });

        await RunAsync();

        var identifier = await factory.ExecuteDbContextAsync(db => db.LoginAudits.AsNoTracking()
            .Where(audit => audit.Id == failure.Id).Select(audit => audit.Identifier).SingleAsync(Ct));
        Assert.Null(identifier);
    }

    [Fact]
    public async Task OpenIddict_prunes_one_day_of_tokens_per_tranche_each_one_saved_on_its_own()
    {
        await RunAsync();
        var subject = Guid.CreateVersion7().ToString();
        var (older, newer) = await factory.ExecuteScopeAsync(async services =>
        {
            var tokens = services.GetRequiredService<IOpenIddictTokenManager>();
            var older = await tokens.CreateAsync(RefreshToken(subject, null, Now.AddDays(-70)), Ct);
            var newer = await tokens.CreateAsync(RefreshToken(subject, null, Now.AddDays(-50)), Ct);
            return (await tokens.GetIdAsync(older, Ct), await tokens.GetIdAsync(newer, Ct));
        });

        // Cada tramo es una llamada, con su transacción, y llega a un día después del token más viejo que le queda por
        // mirar: hay uno en que el de hace 70 días ya se fue y el de hace 50 todavía no. Los tokens de otros tests de la
        // base compartida pueden sumar tramos antes, pero no cambian eso.
        var seen = new List<(bool Older, bool Newer)>();
        DateTime? from = null;
        do
        {
            from = await TrancheAsync(from);
            seen.Add(await ExistingTokensAsync(older!, newer!));
        }
        while (from is not null);

        Assert.Contains((false, true), seen);
        Assert.Equal((false, false), seen[^1]);
    }

    [Fact]
    public async Task A_full_batch_stops_and_the_next_one_goes_on()
    {
        await RunAsync();
        var codes = Enumerable.Range(0, 3).Select(index => Code(Now.AddDays(-40).AddMinutes(index))).ToArray();
        await factory.ExecuteDbContextAsync(db =>
        {
            db.LoginCodes.AddRange(codes);
            return db.SaveChangesAsync(Ct);
        });
        var ids = codes.Select(code => code.Id).ToArray();

        var first = await PurgeWithBatchOfTwoAsync();
        var leftAfterFirst = await CountAsync(ids);
        var second = await PurgeWithBatchOfTwoAsync();

        Assert.True(first);
        Assert.Equal(1, leftAfterFirst);
        Assert.False(second);
        Assert.Equal(0, await CountAsync(ids));
    }

    [Fact]
    public async Task A_copy_that_finds_another_one_running_ends_its_run_without_waiting()
    {
        var old = Code(Now.AddDays(-31));
        await factory.ExecuteDbContextAsync(db =>
        {
            db.LoginCodes.Add(old);
            return db.SaveChangesAsync(Ct);
        });

        // La otra copia está en medio de un lote: tiene el lock de la retención. Esta corre la suya entera sin esperarla y
        // sin tocar nada: si esperara, el WaitAsync lanzaría.
        await factory.InTransactionAsync(async services =>
        {
            Assert.False(await services.GetRequiredService<IIdentityRetentionRepository>().ExistsOtherRunAsync(Ct));

            await Task.Run(() => Worker.RunAsync(Ct), Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
            Assert.Equal(1, await CountAsync([old.Id]));
        });

        // Con el lock libre, la corrida siguiente lo borra.
        await RunAsync();
        Assert.Equal(0, await CountAsync([old.Id]));
    }

    [Fact]
    public async Task The_daily_limits_and_the_retention_read_through_their_indexes()
    {
        var plans = await ExplainAsync(async db =>
        {
            var codes = new LoginCodeRepository(db);
            await codes.ListLatestSentTimesAsync(LoginCodeChannel.Email, Now.AddDays(-1), 400, Ct);
            await codes.ListLatestSentTimesByRequesterAsync(LoginCodeChannel.Email, Guid.CreateVersion7(), Now.AddDays(-1), 10, Ct);
            await new IdentityRetentionRepository(db).ListLoginAuditsWithRequestInfoAsync(Now.AddMonths(-12), 500, Ct);
        });

        Assert.Equal(3, plans.Count);
        Assert.Contains("IX_LoginCodes_Channel_SentAtUtc", plans[0], StringComparison.Ordinal);
        Assert.Contains("IX_LoginCodes_RequestedByUserId_Channel_SentAtUtc", plans[1], StringComparison.Ordinal);
        Assert.Contains("IX_LoginAudits_OccurredAtUtc_RequestInfo", plans[2], StringComparison.Ordinal);
    }

    // El worker registrado en la Api: la corrida de los tests es la misma que la del ciclo.
    private IdentityRetentionBackgroundService Worker =>
        factory.Services.GetServices<IHostedService>().OfType<IdentityRetentionBackgroundService>().Single();

    private Task RunAsync() => Worker.RunAsync(Ct);

    private Task<DateTime?> TrancheAsync(DateTime? from) => factory.ExecuteScopeAsync(async services =>
        (await services.GetRequiredService<IIdentityRetentionService>().PruneSessionsTrancheAsync(from, Ct)).Value);

    private Task<(bool Older, bool Newer)> ExistingTokensAsync(string older, string newer) => factory.ExecuteScopeAsync(async services =>
    {
        var tokens = services.GetRequiredService<IOpenIddictTokenManager>();
        return (await tokens.FindByIdAsync(older, Ct) is not null, await tokens.FindByIdAsync(newer, Ct) is not null);
    });

    private Task<bool> PurgeWithBatchOfTwoAsync() => factory.ExecuteScopeAsync(async services =>
    {
        var service = ActivatorUtilities.CreateInstance<IdentityRetentionService>(
            services, Options.Create(new IdentityRetentionOptions { BatchSize = 2 }));

        return (await service.PurgeBatchAsync(Ct)).Value;
    });

    private Task<int> CountAsync(Guid[] codeIds) =>
        factory.ExecuteDbContextAsync(db => db.LoginCodes.AsNoTracking().CountAsync(code => codeIds.Contains(code.Id), Ct));

    // EXPLAIN de las consultas que de verdad arma el repositorio, con sus parámetros, sin recorridos secuenciales: así el
    // plan dice qué índice puede usar, aunque la tabla de los tests sea chica.
    private Task<List<string>> ExplainAsync(Func<ApplicationDbContext, Task> queries) => factory.ExecuteScopeAsync(async services =>
    {
        var capture = new PlanCapture();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>(services.GetRequiredService<DbContextOptions<ApplicationDbContext>>())
            .AddInterceptors(capture)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        await db.Database.ExecuteSqlRawAsync("ANALYZE \"LoginCodes\"", Ct);
        await db.Database.ExecuteSqlRawAsync("ANALYZE \"LoginAudits\"", Ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off", Ct);
        capture.Enabled = true;

        await queries(db);

        capture.Enabled = false;
        await transaction.RollbackAsync(Ct);
        return capture.Plans;
    });

    private static LoginCode Code(DateTime createdAtUtc, string? requestedFromAddress = null) => LoginCode.Issue(
        LoginCodeDestination.ForEmail(Email.Create(TestEmails.Unique("retention")).Value),
        LoginCodePurpose.SignIn,
        requestedByUserId: null,
        codeHash: Guid.NewGuid().ToString("N"),
        createdAtUtc,
        TimeSpan.FromMinutes(10),
        maxAttempts: 5,
        requestedFromAddress);

    private static OpenIddictAuthorizationDescriptor Authorization(string subject, DateTime createdAtUtc) => new()
    {
        Subject = subject,
        Status = Statuses.Valid,
        Type = AuthorizationTypes.AdHoc,
        CreationDate = new DateTimeOffset(createdAtUtc, TimeSpan.Zero),
    };

    private static OpenIddictTokenDescriptor RefreshToken(string subject, string? authorizationId, DateTime createdAtUtc) => new()
    {
        Subject = subject,
        AuthorizationId = authorizationId,
        Status = Statuses.Redeemed,
        Type = TokenTypeHints.RefreshToken,
        CreationDate = new DateTimeOffset(createdAtUtc, TimeSpan.Zero),
        ExpirationDate = new DateTimeOffset(createdAtUtc.AddDays(30), TimeSpan.Zero),
    };
}

/// <summary>Antes de cada consulta, su EXPLAIN con los mismos parámetros, en la misma transacción.</summary>
file sealed class PlanCapture : DbCommandInterceptor
{
    public bool Enabled { get; set; }

    public List<string> Plans { get; } = [];

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return result;

        await using var explain = command.Connection!.CreateCommand();
        explain.Transaction = command.Transaction;
        explain.CommandText = "EXPLAIN " + command.CommandText;
        foreach (NpgsqlParameter parameter in command.Parameters) explain.Parameters.Add(parameter.Clone());

        var lines = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) lines.Add(reader.GetString(0));
        }

        Plans.Add(string.Join('\n', lines));

        return result;
    }
}
