using System.Data.Common;
using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// La unidad de trabajo contra Postgres de verdad (Etapa 1): un límite por caso de uso, Identity autoguardando adentro
/// con un savepoint por guardado, la política decide qué pasa con un Result fallido, y todo rollback suelta los locks
/// antes de devolver o de lanzar. Los locks se miran desde otra conexión con pg_try_advisory_xact_lock.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class UnitOfWorkTransactionTests(ApiFactory factory)
{
    private static readonly Error BusinessFailure = Error.Failure("Tests.UnitOfWork.Failed", "A business rule failed.");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_successful_result_commits_identity_autosaves_and_the_final_flush()
    {
        var account = await CreateAccountAsync();
        var widgetName = WidgetName();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await users.SetDisplayNameAsync(account.Id, "Después", ct);
            db.Set<Widget>().Add(new Widget(widgetName));

            return Result.Success();
        }, CommitPolicy.OnSuccess, Ct);

        Assert.True(result.IsSuccess);
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal("Después", await DisplayNameAsync(account.Id));
        Assert.True(await WidgetExistsAsync(widgetName));
    }

    [Fact]
    public async Task Two_identity_writes_with_the_second_failing_leave_nothing()
    {
        var account = await CreateAccountAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        // SetRolesAsync saca "User" (autoguardado) y después AddToRoles lanza porque el rol no existe.
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await users.SetDisplayNameAsync(account.Id, "Después", ct);
            await users.SetRolesAsync(account.Id, ["NoExiste"], ct);

            return Result.Success();
        }, CommitPolicy.OnSuccess, Ct));

        Assert.Equal("Antes", await DisplayNameAsync(account.Id));
        Assert.True(await factory.ExecuteDbContextAsync(db => db.UserRoles.AnyAsync(role => role.UserId == account.Id, Ct)));
    }

    [Fact]
    public async Task A_lock_taken_inside_uses_the_boundary_transaction()
    {
        var account = await CreateAccountAsync();
        var key = LoginLinkKey(account.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var links = scope.ServiceProvider.GetRequiredService<ILoginLinkRepository>();
        var freeDuringTheWork = true;

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var boundary = db.Database.CurrentTransaction;
            Assert.NotNull(boundary);

            await links.LockAccountAsync(account.Id, ct);

            Assert.Same(boundary, db.Database.CurrentTransaction);
            freeDuringTheWork = await TryLockElsewhereAsync(key);

            return Result.Success();
        }, CommitPolicy.OnSuccess, Ct);

        Assert.False(freeDuringTheWork);
        Assert.True(await TryLockElsewhereAsync(key));
    }

    [Fact]
    public async Task A_failed_result_with_on_success_rolls_back_autosaves_and_releases_the_locks()
    {
        var account = await CreateAccountAsync();
        var key = LoginLinkKey(account.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var links = scope.ServiceProvider.GetRequiredService<ILoginLinkRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await links.LockAccountAsync(account.Id, ct);
            await users.SetDisplayNameAsync(account.Id, "Después", ct);

            return Result.Failure(BusinessFailure);
        }, CommitPolicy.OnSuccess, Ct);

        Assert.Equal(BusinessFailure, result.Error);
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.True(await TryLockElsewhereAsync(key));
        Assert.Equal("Antes", await DisplayNameAsync(account.Id));
    }

    [Fact]
    public async Task A_failed_result_with_on_any_result_keeps_the_attempt()
    {
        var destination = LoginCodeDestination.ForEmail(Email.Create(TestEmails.Unique("uow-attempt")).Value);
        var codeId = await factory.ExecuteScopeAsync(async services =>
        {
            var code = LoginCode.Issue(destination, LoginCodePurpose.SignIn, requestedByUserId: null, "hash-right",
                factory.Clock.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(10), maxAttempts: 5);
            services.GetRequiredService<ILoginCodeRepository>().Add(code);
            await services.GetRequiredService<ApplicationDbContext>().SaveChangesAsync(Ct);

            return code.Id;
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var codes = scope.ServiceProvider.GetRequiredService<ILoginCodeRepository>();

        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await codes.LockDestinationAsync(destination, ct);
            var code = await codes.GetLatestAsync(destination, LoginCodePurpose.SignIn, requestedByUserId: null, ct);

            return code!.Verify("hash-wrong", factory.Clock.GetUtcNow().UtcDateTime);
        }, CommitPolicy.OnAnyResult, Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.LoginCodes
            .Where(code => code.Id == codeId)
            .Select(code => code.FailedAttempts)
            .SingleAsync(Ct)));
    }

    [Fact]
    public async Task An_exception_rolls_back_and_releases_the_locks_before_propagating()
    {
        var account = await CreateAccountAsync();
        var key = LoginLinkKey(account.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var links = scope.ServiceProvider.GetRequiredService<ILoginLinkRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        await Assert.ThrowsAsync<WorkFailure>(() => unitOfWork.ExecuteInTransactionAsync<Result>(async ct =>
        {
            await links.LockAccountAsync(account.Id, ct);
            await users.SetDisplayNameAsync(account.Id, "Después", ct);

            throw new WorkFailure();
        }, CommitPolicy.OnSuccess, Ct));

        // El scope (y su conexión) sigue vivo: lo que necesita el reintento del webhook es que otra conexión tome el
        // lock en el acto, sin esperar a que se descarte este contexto.
        Assert.Null(db.Database.CurrentTransaction);
        Assert.True(await TryLockElsewhereAsync(key));
        Assert.Equal("Antes", await DisplayNameAsync(account.Id));
    }

    [Fact]
    public async Task A_unique_violation_in_the_final_flush_is_translated_after_rolling_back()
    {
        var waMessageId = "wamid.uow-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        await factory.ExecuteScopeAsync(async services =>
        {
            services.GetRequiredService<IWhatsAppMessageRepository>().Add(Outbound(waMessageId));

            return await services.GetRequiredService<ApplicationDbContext>().SaveChangesAsync(Ct);
        });
        var widgetName = WidgetName();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var messages = scope.ServiceProvider.GetRequiredService<IWhatsAppMessageRepository>();

        var exception = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() =>
            unitOfWork.ExecuteInTransactionAsync(_ =>
            {
                messages.Add(Outbound(waMessageId));

                return Task.FromResult(Result.Success());
            }, CommitPolicy.OnSuccess, Ct));

        Assert.IsAssignableFrom<DbUpdateException>(exception.InnerException);
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Empty(db.ChangeTracker.Entries());

        // El mismo scope puede correr otro límite: nada de lo deshecho vuelve a bajar.
        await unitOfWork.ExecuteInTransactionAsync(_ =>
        {
            db.Set<Widget>().Add(new Widget(widgetName));

            return Task.FromResult(Result.Success());
        }, CommitPolicy.OnSuccess, Ct);
        Assert.True(await WidgetExistsAsync(widgetName));
    }

    [Fact]
    public async Task A_caught_unique_violation_inside_keeps_the_transaction_usable()
    {
        var ownerEmail = TestEmails.Unique("uow-owner");
        await CreateAccountAsync(ownerEmail);
        var other = await CreateAccountAsync();
        var widgetName = WidgetName();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            db.Set<Widget>().Add(new Widget(widgetName));

            try
            {
                await users.SetEmailAsync(other.Id, Email.Create(ownerEmail).Value, confirmed: true, ct);
            }
            catch (UniqueConstraintViolationException)
            {
                // El savepoint deshizo solo ese guardado: la transacción sigue usable y el widget sigue pendiente.
                return Result.Failure(BusinessFailure);
            }

            return Result.Success();
        }, CommitPolicy.OnAnyResult, Ct);

        Assert.Equal(BusinessFailure, result.Error);
        Assert.True(await WidgetExistsAsync(widgetName));
        Assert.Equal(other.Email, await EmailAsync(other.Id));
    }

    [Fact]
    public async Task A_failed_commit_rolls_back_and_releases_the_locks()
    {
        var key = "uow-commit:" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var widgetName = WidgetName();
        await using var scope = factory.Services.CreateAsyncScope();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>(
                scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>())
            .AddInterceptors(new RefusingCommitInterceptor())
            .Options;
        await using var db = new TestDbContext(options);
        var unitOfWork = new UnitOfWork(db, NullLogger<UnitOfWork>.Instance);

        // Sale la excepción del commit, no una InvalidOperationException del rollback.
        await Assert.ThrowsAsync<CommitRefused>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await db.AcquireAdvisoryLocksAsync([key], ct);
            db.Set<Widget>().Add(new Widget(widgetName));

            return Result.Success();
        }, CommitPolicy.OnSuccess, Ct));

        Assert.Null(db.Database.CurrentTransaction);
        Assert.False(await WidgetExistsAsync(widgetName));
        Assert.True(await TryLockElsewhereAsync(key));
    }

    [Fact]
    public async Task Nested_calls_throw_and_the_outer_boundary_rolls_back()
    {
        var account = await CreateAccountAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await users.SetDisplayNameAsync(account.Id, "Después", ct);

            return await unitOfWork.ExecuteInTransactionAsync(
                _ => Task.FromResult(Result.Success()), CommitPolicy.OnSuccess, ct);
        }, CommitPolicy.OnSuccess, Ct));

        Assert.Equal("Antes", await DisplayNameAsync(account.Id));
    }

    [Fact]
    public async Task A_transaction_opened_outside_the_boundary_throws_without_touching_it()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await using var foreign = await db.Database.BeginTransactionAsync(Ct);
        var ran = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(_ =>
        {
            ran = true;

            return Task.FromResult(Result.Success());
        }, CommitPolicy.OnSuccess, Ct));

        Assert.False(ran);
        Assert.Same(foreign, db.Database.CurrentTransaction);
        await foreign.RollbackAsync(Ct);
    }

    [Fact]
    public async Task An_unknown_policy_is_rejected_before_opening_anything()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => unitOfWork.ExecuteInTransactionAsync(
            _ => Task.FromResult(Result.Success()), (CommitPolicy)7, Ct));

        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Cancellation_during_the_work_rolls_back_and_releases_the_locks()
    {
        var account = await CreateAccountAsync();
        var key = LoginLinkKey(account.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var links = scope.ServiceProvider.GetRequiredService<ILoginLinkRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await links.LockAccountAsync(account.Id, ct);
            await users.SetDisplayNameAsync(account.Id, "Después", ct);
            await cancellation.CancelAsync();
            ct.ThrowIfCancellationRequested();

            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellation.Token));

        Assert.Null(db.Database.CurrentTransaction);
        Assert.True(await TryLockElsewhereAsync(key));
        Assert.Equal("Antes", await DisplayNameAsync(account.Id));
    }

    // Transitorio: lo borra la Tarea 21, junto con IUnitOfWork.SaveChangesAsync.
    [Fact]
    public async Task SaveChangesAsync_inside_the_boundary_throws()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await unitOfWork.SaveChangesAsync(ct);

            return Result.Success();
        }, CommitPolicy.OnSuccess, Ct));

        Assert.Null(db.Database.CurrentTransaction);
    }

    private static string LoginLinkKey(Guid userId) => "login-link:" + userId.ToString("N", CultureInfo.InvariantCulture);

    private static string WidgetName() => "uow-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12];

    private WhatsAppMessage Outbound(string waMessageId) =>
        WhatsAppMessage.Outbound(contactId: null, waMessageId, WhatsAppMessageKind.Text, "Listo.", factory.Clock.GetUtcNow().UtcDateTime);

    private Task<UserAccount> CreateAccountAsync(string? email = null) =>
        factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>().CreateAsync(
            Email.Create(email ?? TestEmails.Unique("uow")).Value, phone: null, phoneConfirmed: false, "Antes", "es", Ct));

    private Task<string?> DisplayNameAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.DisplayName)
            .SingleAsync(Ct));

    private Task<string?> EmailAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.Email)
            .SingleAsync(Ct));

    private Task<bool> WidgetExistsAsync(string name) =>
        factory.ExecuteDbContextAsync(db => db.Set<Widget>().AnyAsync(widget => widget.Name == name, Ct));

    /// <summary>
    /// Si otra conexión puede tomar el lock en este instante. pg_try_advisory_xact_lock no espera, y en autocommit lo
    /// suelta al terminar la sentencia.
    /// </summary>
    private async Task<bool> TryLockElsewhereAsync(string key)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(hashtextextended(@key, 0))", connection);
        command.Parameters.AddWithValue("key", key);

        return (bool)(await command.ExecuteScalarAsync(Ct))!;
    }

    private sealed class RefusingCommitInterceptor : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            throw new CommitRefused();
    }

    private sealed class CommitRefused : Exception;

    private sealed class WorkFailure : Exception;
}
