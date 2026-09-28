using System.Data.Common;
using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
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

    /// <summary>
    /// Una FK diferida que no se cumple: el INSERT pasa y el COMMIT falla con 23503. Las tablas son temporales y nacen en
    /// la misma transacción, así que el rechazo no deja nada.
    /// </summary>
    private const string DeferredForeignKeyViolation = """
        CREATE TEMP TABLE uow_parent (id integer PRIMARY KEY) ON COMMIT DROP;
        CREATE TEMP TABLE uow_child (parent_id integer REFERENCES uow_parent DEFERRABLE INITIALLY DEFERRED) ON COMMIT DROP;
        INSERT INTO uow_child VALUES (1);
        """;

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
    public async Task Locks_outside_the_boundary_throw_even_without_keys()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var userId = Guid.CreateVersion7();
        var email = Email.Create(TestEmails.Unique("uow-outside")).Value;
        var contacts = services.GetRequiredService<IWhatsAppContactRepository>();

        Func<Task>[] locks =
        [
            () => services.GetRequiredService<ILoginLinkRepository>().LockAccountAsync(userId, Ct),
            () => services.GetRequiredService<IUserInvitationRepository>().LockAccountAsync(userId, Ct),
            () => services.GetRequiredService<ILoginCodeRepository>().LockDestinationAsync(LoginCodeDestination.ForEmail(email), Ct),
            () => services.GetRequiredService<IWhatsAppMessageRepository>().LockAsync([], Ct),
            () => contacts.LockAsync([], [], Ct),
            () => contacts.GetForProcessingAsync(Guid.CreateVersion7(), Ct),
            () => contacts.LockForNumberChangeAsync(userId, waId: null, Ct),
            () => contacts.GetByUserIdForUnlinkAsync(userId, Ct),
            () => services.GetRequiredService<IUserRepository>().LockExternalSignInAsync(email, "Google", "k", Ct),
            () => services.GetRequiredService<IUserRepository>().LockAdminsAsync(Ct),
        ];

        foreach (var takeLock in locks)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(takeLock);
        }

        Assert.Null(services.GetRequiredService<ApplicationDbContext>().Database.CurrentTransaction);
    }

    /// <summary>
    /// Una sola forma de guardar vale también para las cuentas: cada escritura de IUserRepository exige la transacción
    /// del caso de uso y, sin ella, lanza antes de tocar nada. La lista cubre el contrato entero: una escritura nueva que
    /// no se sume acá hace fallar el test.
    /// </summary>
    [Fact]
    public async Task Account_writes_outside_the_boundary_throw_and_change_nothing()
    {
        var account = await CreateAccountAsync();
        var createdEmail = Email.Create(TestEmails.Unique("uow-outside-create")).Value;
        var unverifiedEmail = Email.Create(TestEmails.Unique("uow-outside-unverified")).Value;
        var changedEmail = Email.Create(TestEmails.Unique("uow-outside-email")).Value;
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var writes = new Dictionary<string, Func<Task>>(StringComparer.Ordinal)
        {
            [nameof(IUserRepository.CreateAsync)] = () =>
                users.CreateAsync(createdEmail, phone: null, phoneConfirmed: false, "Nueva", "es", Ct),
            [nameof(IUserRepository.CreateUnverifiedAsync)] = () =>
                users.CreateUnverifiedAsync(unverifiedEmail, phone: null, "Nueva", "es", Ct),
            [nameof(IUserRepository.AddExternalLoginAsync)] = () =>
                users.AddExternalLoginAsync(account.Id, GoogleLogin(account), Ct),
            [nameof(IUserRepository.RestoreAsync)] = () => users.RestoreAsync(account.Id, "Después", Ct),
            [nameof(IUserRepository.SetEmailAsync)] = () => users.SetEmailAsync(account.Id, changedEmail, confirmed: true, Ct),
            [nameof(IUserRepository.SetPhoneAsync)] = () =>
                users.SetPhoneAsync(account.Id, TestPhones.Unique(), confirmed: true, Ct),
            [nameof(IUserRepository.RemovePhoneAsync)] = () => users.RemovePhoneAsync(account.Id, Ct),
            [nameof(IUserRepository.SetRolesAsync)] = () => users.SetRolesAsync(account.Id, [SystemRoles.Admin], Ct),
            [nameof(IUserRepository.SetDisplayNameAsync)] = () => users.SetDisplayNameAsync(account.Id, "Después", Ct),
            [nameof(IUserRepository.SetActiveAsync)] = () => users.SetActiveAsync(account.Id, isActive: false, Ct),
            [nameof(IUserRepository.UpdateProfileAsync)] = () =>
                users.UpdateProfileAsync(account.Id, "Después", "en", "UTC", Ct),
            [nameof(IUserRepository.DeleteAsync)] = () => users.DeleteAsync(account.Id, Ct),
        };

        // LockExternalSignInAsync y LockAdminsAsync son locks, no escrituras: los cubre
        // Locks_outside_the_boundary_throw_even_without_keys.
        Assert.Equal(
            typeof(IUserRepository).GetMethods()
                .Select(method => method.Name)
                .Where(name => name != nameof(IUserRepository.LockExternalSignInAsync)
                    && name != nameof(IUserRepository.LockAdminsAsync))
                .Order(StringComparer.Ordinal),
            writes.Keys.Order(StringComparer.Ordinal));

        Assert.Empty(await UnguardedAsync(writes));
        Assert.Null(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.CurrentTransaction);
        await AssertUntouchedAsync(account);
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.IgnoreQueryFilters()
            .AnyAsync(user => user.Email == createdEmail.Value || user.Email == unverifiedEmail.Value, Ct)));
    }

    /// <summary>
    /// ISignInService declara la regla de cada miembro: los que escriben exigen la transacción del caso de uso y, sin ella,
    /// lanzan antes de tocar nada, el stamp incluido; los demás leen o tocan solo cookies de la petición. La clasificación
    /// cubre el contrato entero: un miembro nuevo sin clasificar hace fallar el test.
    /// </summary>
    [Fact]
    public async Task Sign_in_service_follows_its_transaction_rules()
    {
        var account = await CreateAccountAsync();
        var stampBefore = await SecurityStampAsync(account.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var signIn = scope.ServiceProvider.GetRequiredService<ISignInService>();

        var writes = new Dictionary<string, Func<Task>>(StringComparer.Ordinal)
        {
            [nameof(ISignInService.RegisterFailedAttemptAsync)] = () => signIn.RegisterFailedAttemptAsync(account.Id, Ct),
            [nameof(ISignInService.ResetFailedAttemptsAsync)] = () => signIn.ResetFailedAttemptsAsync(account.Id, Ct),
            [nameof(ISignInService.RevokeSessionsAsync)] = () => signIn.RevokeSessionsAsync(account.Id, Ct),
        };

        // Después del commit: adentro de un límite lanzan (lo prueba SignInServiceTests).
        string[] afterCommit = [nameof(ISignInService.SignInAsync)];

        // Leen el bloqueo o tocan solo la cookie externa de la petición.
        string[] anywhere =
        [
            nameof(ISignInService.IsLockedOutAsync),
            nameof(ISignInService.GetExternalLoginAsync),
            nameof(ISignInService.SignOutExternalAsync),
        ];

        Assert.Equal(
            typeof(ISignInService).GetMethods().Select(method => method.Name).Order(StringComparer.Ordinal),
            writes.Keys.Concat(afterCommit).Concat(anywhere).Order(StringComparer.Ordinal));
        Assert.Empty(await UnguardedAsync(writes));
        Assert.Null(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.CurrentTransaction);
        await AssertUntouchedAsync(account);
        Assert.Equal(stampBefore, await SecurityStampAsync(account.Id));
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
    public async Task An_exception_with_on_any_result_rolls_back_and_releases_the_locks()
    {
        var account = await CreateAccountAsync();
        var key = LoginLinkKey(account.Id);
        var widgetName = WidgetName();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var links = scope.ServiceProvider.GetRequiredService<ILoginLinkRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        // OnAnyResult confirma cualquier Result, también uno fallido, pero una excepción no es un Result: no se confirma
        // ni el autoguardado de Identity ni lo que esperaba el guardado final.
        await Assert.ThrowsAsync<WorkFailure>(() => unitOfWork.ExecuteInTransactionAsync<Result>(async ct =>
        {
            await links.LockAccountAsync(account.Id, ct);
            await users.SetDisplayNameAsync(account.Id, "Después", ct);
            db.Set<Widget>().Add(new Widget(widgetName));

            throw new WorkFailure();
        }, CommitPolicy.OnAnyResult, Ct));

        Assert.Null(db.Database.CurrentTransaction);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.True(await TryLockElsewhereAsync(key));
        Assert.Equal("Antes", await DisplayNameAsync(account.Id));
        Assert.False(await WidgetExistsAsync(widgetName));
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
    public async Task A_unique_violation_from_an_identity_autosave_escaping_the_work_is_translated()
    {
        var email = Email.Create(TestEmails.Unique("uow-taken")).Value;
        await CreateAccountAsync(email.Value);
        var destination = LoginCodeDestination.ForEmail(email);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var codes = scope.ServiceProvider.GetRequiredService<ILoginCodeRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        // Como el verify o Google: el lock del correo y el alta. UserRepository.CreateAsync no atrapa el 23505 (solo lo
        // hace CreateUnverifiedAsync), así que el guardado de UserManager sale del trabajo como DbUpdateException y es la
        // unidad de trabajo la que lo traduce, después de deshacer.
        var exception = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() =>
            unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await codes.LockDestinationAsync(destination, ct);
                await users.CreateAsync(email, phone: null, phoneConfirmed: false, "Otra", "es", ct);

                return Result.Success();
            }, CommitPolicy.OnAnyResult, Ct));

        Assert.IsAssignableFrom<DbUpdateException>(exception.InnerException);
        Assert.Equal("EmailIndex", exception.ConstraintName);
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.True(await TryLockElsewhereAsync(AdvisoryLockKeys.LoginCode(destination)));
        Assert.Equal(1, await factory.ExecuteDbContextAsync(context => context.Users.IgnoreQueryFilters()
            .CountAsync(user => user.Email == email.Value, Ct)));
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
    public async Task A_commit_that_fails_before_reaching_the_server_rolls_back_and_releases_the_locks()
    {
        var key = "uow-commit:" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var widgetName = WidgetName();
        await using var scope = factory.Services.CreateAsyncScope();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>(
                scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>())
            .AddInterceptors(new RefusingCommitInterceptor())
            .Options;
        await using var db = new TestDbContext(options);
        var logger = new FakeLogger<UnitOfWork>();
        var unitOfWork = new UnitOfWork(db, logger);

        // El interceptor lanza antes de que salga el COMMIT: la transacción sigue viva y el rollback de la unidad de
        // trabajo la deshace sin problemas, así que no hay nada que avisar. El COMMIT que rechaza el servidor, que sí
        // deja un rollback fallido, es el test siguiente.
        await Assert.ThrowsAsync<CommitRefused>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await db.AcquireAdvisoryLocksAsync([key], ct);
            db.Set<Widget>().Add(new Widget(widgetName));

            return Result.Success();
        }, CommitPolicy.OnSuccess, Ct));

        Assert.Null(db.Database.CurrentTransaction);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(logger.Collector.GetSnapshot());
        Assert.False(await WidgetExistsAsync(widgetName));
        Assert.True(await TryLockElsewhereAsync(key));
    }

    [Fact]
    public async Task A_commit_the_server_rejects_propagates_and_the_failed_rollback_only_logs_its_type()
    {
        var key = "uow-commit:" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var widgetName = WidgetName();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = new FakeLogger<UnitOfWork>();
        var unitOfWork = new UnitOfWork(db, logger);

        // Postgres revisa la FK diferida en el COMMIT, lo rechaza y termina la transacción: el rollback de la unidad de
        // trabajo encuentra la NpgsqlTransaction ya completada y lanza. Tiene que salir el error del commit, no el del
        // rollback, y del rollback fallido tiene que quedar solo un Warning con el tipo de la excepción.
        var exception = await Assert.ThrowsAsync<PostgresException>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await db.AcquireAdvisoryLocksAsync([key], ct);
            await db.Database.ExecuteSqlRawAsync(DeferredForeignKeyViolation, ct);
            db.Set<Widget>().Add(new Widget(widgetName));

            return Result.Success();
        }, CommitPolicy.OnSuccess, Ct));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Empty(db.ChangeTracker.Entries());
        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Null(record.Exception);
        var value = Assert.Single(record.StructuredState!, pair => pair.Key != "{OriginalFormat}");
        Assert.Equal(new KeyValuePair<string, string?>("ExceptionType", nameof(InvalidOperationException)), value);
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
    public async Task A_null_work_is_rejected_before_opening_anything()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            unitOfWork.ExecuteInTransactionAsync<Result>(null!, CommitPolicy.OnSuccess, Ct));

        Assert.Equal("work", exception.ParamName);
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

    private static string LoginLinkKey(Guid userId) => "login-link:" + userId.ToString("N", CultureInfo.InvariantCulture);

    private static string WidgetName() => "uow-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12];

    private WhatsAppMessage Outbound(string waMessageId) =>
        WhatsAppMessage.Outbound(contactId: null, waMessageId, WhatsAppMessageKind.Text, "Listo.", factory.Clock.GetUtcNow().UtcDateTime);

    private Task<UserAccount> CreateAccountAsync(string? email = null) =>
        factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().CreateAsync(
            Email.Create(email ?? TestEmails.Unique("uow")).Value, phone: null, phoneConfirmed: false, "Antes", "es", Ct));

    private static ExternalLogin GoogleLogin(UserAccount account) =>
        new(ExternalLoginProviders.Google, "google-" + account.Id.ToString("N", CultureInfo.InvariantCulture), account.Email,
            EmailVerified: true, DisplayName: null);

    /// <summary>
    /// Corre cada escritura fuera de un límite y devuelve las que no frenó la guarda de la transacción: las que no
    /// lanzaron y las que lanzaron otra cosa. Las junta todas para que el rojo diga cuáles faltan.
    /// </summary>
    private static async Task<IReadOnlyList<string>> UnguardedAsync(IReadOnlyDictionary<string, Func<Task>> writes)
    {
        var unguarded = new List<string>();

        foreach (var (name, write) in writes)
        {
            var exception = await Record.ExceptionAsync(write);

            if (exception is not InvalidOperationException
                || !exception.Message.Contains(nameof(IUnitOfWork.ExecuteInTransactionAsync), StringComparison.Ordinal))
            {
                unguarded.Add($"{name}: {exception?.GetType().Name ?? "no exception"}");
            }
        }

        return unguarded;
    }

    /// <summary>La cuenta quedó como la dejó <see cref="CreateAccountAsync"/>: sin nada de lo que intentaron las escrituras.</summary>
    private async Task AssertUntouchedAsync(UserAccount account)
    {
        var stored = await factory.ExecuteDbContextAsync(db => db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(user => user.Id == account.Id)
            .Select(user => new
            {
                user.Email,
                user.PhoneNumber,
                user.DisplayName,
                user.Culture,
                user.IsActive,
                user.IsDeleted,
                user.AccessFailedCount,
                Logins = db.UserLogins.Count(login => login.UserId == user.Id),
                Roles = db.UserRoles.Count(role => role.UserId == user.Id),
            })
            .SingleAsync(Ct));

        Assert.Equal(account.Email, stored.Email);
        Assert.Null(stored.PhoneNumber);
        Assert.Equal("Antes", stored.DisplayName);
        Assert.Equal("es", stored.Culture);
        Assert.True(stored.IsActive);
        Assert.False(stored.IsDeleted);
        Assert.Equal(0, stored.AccessFailedCount);
        Assert.Equal(0, stored.Logins);
        Assert.Equal(1, stored.Roles);
    }

    private Task<string?> DisplayNameAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.DisplayName)
            .SingleAsync(Ct));

    private Task<string?> SecurityStampAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.SecurityStamp)
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

    /// <summary>EF lo llama antes de mandar el COMMIT, con la transacción de Npgsql todavía activa.</summary>
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
