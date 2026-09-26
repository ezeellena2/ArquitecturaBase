using System.Reflection;
using System.Runtime.CompilerServices;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// Una sola forma de guardar (Etapa 1, decisión 0001): el límite transaccional lo abre el punto de entrada de un caso de
/// uso con IUnitOfWork.ExecuteInTransactionAsync, y solo UnitOfWork abre, confirma, deshace y guarda. Las reglas leen el
/// IL de Application, Infrastructure y Api (llamadas y literales), no el texto de las fuentes. Los ensamblados de tests
/// no se miran: el arnés puede abrir transacciones para sostener una fila.
/// </summary>
public sealed class TransactionBoundaryTests
{
    private const string ServicesNamespace = "ArquitecturaBase.Application.Services";
    private const string ServiceInterfacesNamespace = "ArquitecturaBase.Application.Interfaces.Services";

    // Los tipos de Infrastructure son internos y van por nombre: cada regla afirma que el detector ve al dueño
    // permitido, así un nombre que quedó viejo hace fallar la regla en lugar de dejarla pasando en silencio.
    private const string UnitOfWorkImplementation = "ArquitecturaBase.Infrastructure.Persistence.UnitOfWork";
    private const string SeedNamespace = "ArquitecturaBase.Infrastructure.Persistence.Seed";
    private const string AdvisoryLockExtensions = "ArquitecturaBase.Infrastructure.Persistence.Extensions.AdvisoryLockExtensions";
    private const string AdvisoryLockKeys = "ArquitecturaBase.Infrastructure.Persistence.Extensions.AdvisoryLockKeys";
    private const string MessageRetentionRepository =
        "ArquitecturaBase.Infrastructure.Persistence.Repositories.WhatsAppMessageRetentionRepository";

    // Del tipo, no de un texto: si IUnitOfWork cambia de nombre o de namespace, las reglas lo siguen buscando bien.
    private static readonly string UnitOfWorkContract = typeof(IUnitOfWork).FullName!;

    private static readonly Assembly[] Scanned =
    [
        Assembly.Load("ArquitecturaBase.Application"),
        Assembly.Load("ArquitecturaBase.Infrastructure"),
        Assembly.Load("ArquitecturaBase.Api"),
    ];

    private static readonly CallSites.Call[] Calls = [.. Scanned.SelectMany(assembly => CallSites.Calls(assembly))];

    private static readonly CallSites.Literal[] Literals = [.. Scanned.SelectMany(assembly => CallSites.Literals(assembly))];

    private static readonly string[] TransactionApiTypes =
    [
        "Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade",
        "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions",
        "Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction",
        "System.Data.Common.DbTransaction",
    ];

    private static readonly string[] TransactionApiMethods =
    [
        "BeginTransaction", "BeginTransactionAsync", "UseTransaction", "UseTransactionAsync",
        "CommitTransaction", "CommitTransactionAsync", "RollbackTransaction", "RollbackTransactionAsync",
        "Commit", "CommitAsync", "Rollback", "RollbackAsync",
        "CreateSavepoint", "CreateSavepointAsync", "RollbackToSavepoint", "RollbackToSavepointAsync",
        "ReleaseSavepoint", "ReleaseSavepointAsync",
    ];

    private static readonly string[] LockKeyPrefixes =
    [
        "login-code:", "login-link:", "user-invitation:", "whatsapp-contact:user:", "whatsapp-contact:wa:",
        "whatsapp-message:", "external-login:",
    ];

    private static readonly string[] BulkMethods = ["ExecuteUpdate", "ExecuteUpdateAsync", "ExecuteDelete", "ExecuteDeleteAsync"];

    // Trinquete de la migración (Tareas 4 a 21): quién infringe todavía cada regla. El test falla si aparece alguien
    // nuevo y también si alguien de la lista dejó de infringir, así se lo saca. Cada commit de migración achica una lista
    // y la Tarea 21 las borra.
    private static readonly string[] KnownUnitOfWorkReceivers =
    [
        "ArquitecturaBase.Application.Services.Users.ProfileEmailOperations",
        "ArquitecturaBase.Application.Services.Users.ProfileWhatsAppOperations",
        "ArquitecturaBase.Application.Services.Users.UserPhoneOperations",
        "ArquitecturaBase.Application.Services.Users.UserStatusOperations",
    ];

    private static readonly string[] KnownTransactionOpeners =
    [
        "ArquitecturaBase.Infrastructure.Persistence.Extensions.AdvisoryLockExtensions",
        "ArquitecturaBase.Infrastructure.Persistence.Repositories.WhatsAppContactRepository",
    ];

    private static readonly string[] KnownLockLiteralOwners = [];

    private static readonly string[] KnownSaveChangesCallers =
    [
        "ArquitecturaBase.Application.Services.Auth.AccountService",
        "ArquitecturaBase.Application.Services.Auth.ExternalLoginService",
        "ArquitecturaBase.Application.Services.Auth.LoginLinkService",
        "ArquitecturaBase.Application.Services.Users.ProfileEmailOperations",
        "ArquitecturaBase.Application.Services.Users.ProfileService",
        "ArquitecturaBase.Application.Services.Users.ProfileWhatsAppOperations",
        "ArquitecturaBase.Application.Services.Users.UserPhoneOperations",
        "ArquitecturaBase.Application.Services.Users.UserStatusOperations",
        "ArquitecturaBase.Application.Services.WhatsApp.WhatsAppDeliveryService",
        "ArquitecturaBase.Application.Services.WhatsApp.WhatsAppInboundService",
        "ArquitecturaBase.Application.Services.WhatsApp.WhatsAppWebhookPersistence",
    ];

    [Fact]
    public void Only_use_case_entry_points_receive_the_unit_of_work()
    {
        var receivers = Scanned
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Where(type => type
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(IUnitOfWork))))
            .ToArray();

        // Si la regla no encontrara a nadie, pasaría en silencio.
        Assert.NotEmpty(receivers);

        AssertOnlyKnown(
            receivers.Where(type => !IsUseCaseEntryPoint(type)).Select(type => type.FullName!),
            KnownUnitOfWorkReceivers);
    }

    [Fact]
    public void Only_use_case_entry_points_run_a_unit_of_work()
    {
        // También caza un service locator (GetRequiredService<IUnitOfWork>()) en Infrastructure o en Api.
        var callers = Calls
            .Where(call => call.DeclaringType == UnitOfWorkContract
                && call.Method == nameof(IUnitOfWork.ExecuteInTransactionAsync))
            .Select(call => call.Owner)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Si el detector no viera a nadie, la regla pasaría en silencio.
        Assert.NotEmpty(callers);

        var violations = callers.Where(owner => !IsUseCaseEntryPoint(owner));

        Assert.Empty(violations);
    }

    [Fact]
    public void Only_the_unit_of_work_opens_commits_or_rolls_back_transactions()
    {
        var owners = Calls
            .Where(call => TransactionApiTypes.Contains(call.DeclaringType, StringComparer.Ordinal)
                && TransactionApiMethods.Contains(call.Method, StringComparer.Ordinal))
            .Select(call => call.Owner)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains(UnitOfWorkImplementation, owners);

        AssertOnlyKnown(owners.Where(owner => owner != UnitOfWorkImplementation), KnownTransactionOpeners);
    }

    [Fact]
    public void Only_the_unit_of_work_saves_the_context()
    {
        // El compilador referencia la declaración virtual original (DbContext o IdentityDbContext): por eso se compara
        // por "DbContext" en el nombre del tipo. Los autoguardados de Identity, OpenIddict y Data Protection y el Migrator
        // viven en otros ensamblados. El seed es arranque idempotente y guarda por su cuenta (decisión D6, Etapa 7).
        var owners = Calls
            .Where(call => call.Method is "SaveChanges" or "SaveChangesAsync"
                && call.DeclaringType.Contains("DbContext", StringComparison.Ordinal))
            .Select(call => call.Owner)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains(UnitOfWorkImplementation, owners);

        var violations = owners.Where(owner => owner != UnitOfWorkImplementation
            && !owner.StartsWith(SeedNamespace + ".", StringComparison.Ordinal));

        Assert.Empty(violations);
    }

    [Fact]
    public void Advisory_lock_sql_and_keys_live_in_one_place()
    {
        // El texto de la clave ES el lock: un prefijo escrito dos veces se puede desalinear y dejar de poner en fila.
        var sqlOwners = Literals
            .Where(literal => literal.Value.Contains("pg_advisory", StringComparison.Ordinal))
            .Select(literal => literal.Owner)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var keyOwners = Literals
            .Where(literal => LockKeyPrefixes.Any(prefix => literal.Value.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(literal => literal.Owner)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Los dos dueños permitidos tienen que aparecer: si el detector dejara de verlos, la regla pasaría en silencio.
        Assert.Contains(AdvisoryLockExtensions, sqlOwners);
        Assert.Contains(AdvisoryLockKeys, keyOwners);

        AssertOnlyKnown(
            sqlOwners.Where(owner => owner != AdvisoryLockExtensions)
                .Concat(keyOwners.Where(owner => owner != AdvisoryLockKeys)),
            KnownLockLiteralOwners);
    }

    [Fact]
    public void Bulk_updates_and_deletes_only_where_documented()
    {
        // ExecuteUpdate y ExecuteDelete saltean los interceptores: solo la retención, sobre WhatsAppMessage, que no es
        // IAuditable ni ISoftDeletable.
        var owners = Calls
            .Where(call => BulkMethods.Contains(call.Method, StringComparer.Ordinal))
            .Select(call => call.Owner)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains(MessageRetentionRepository, owners);

        var violations = owners.Where(owner => owner != MessageRetentionRepository);

        Assert.Empty(violations);
    }

    // Transitoria: la borra la Tarea 21, cuando SaveChangesAsync sale de IUnitOfWork y el compilador ya lo impide.
    [Fact]
    public void Unit_of_work_SaveChangesAsync_is_only_called_by_pending_services()
    {
        var owners = Calls
            .Where(call => call.DeclaringType == UnitOfWorkContract
                && call.Method == nameof(IUnitOfWork.SaveChangesAsync))
            .Select(call => call.Owner);

        AssertOnlyKnown(owners, KnownSaveChangesCallers);
    }

    private static bool IsUseCaseEntryPoint(Type type) =>
        type.ResidesIn(ServicesNamespace)
        && type.GetInterfaces().Any(contract => contract.ResidesIn(ServiceInterfacesNamespace));

    private static bool IsUseCaseEntryPoint(string typeName) =>
        Scanned.Select(assembly => assembly.GetType(typeName)).OfType<Type>().FirstOrDefault() is { } type
        && IsUseCaseEntryPoint(type);

    private static void AssertOnlyKnown(IEnumerable<string> found, string[] known)
    {
        var actual = found.ToHashSet(StringComparer.Ordinal);
        var unexpected = actual.Except(known, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var cleared = known.Except(actual, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Assert.True(unexpected.Length == 0, "New violations: " + string.Join(", ", unexpected));
        Assert.True(cleared.Length == 0, "No longer violating, remove them from the known list: " + string.Join(", ", cleared));
    }
}
