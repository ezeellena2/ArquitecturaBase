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

        Assert.Empty(receivers.Where(type => !IsUseCaseEntryPoint(type)).Select(type => type.FullName));
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

        var violations = owners.Where(owner => owner != UnitOfWorkImplementation);

        Assert.Empty(violations);
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

        var violations = sqlOwners.Where(owner => owner != AdvisoryLockExtensions)
            .Concat(keyOwners.Where(owner => owner != AdvisoryLockKeys));

        Assert.Empty(violations);
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

    [Fact]
    public void The_unit_of_work_has_a_single_way_to_save()
    {
        // Si alguien vuelve a agregar SaveChangesAsync (o cualquier otra forma de guardar), falla acá.
        var method = Assert.Single(typeof(IUnitOfWork).GetMethods());
        Assert.Equal(nameof(IUnitOfWork.ExecuteInTransactionAsync), method.Name);
        Assert.True(method.IsGenericMethodDefinition);

        var result = Assert.Single(method.GetGenericArguments());
        Assert.Equal(
            [
                typeof(Func<,>).MakeGenericType(typeof(CancellationToken), typeof(Task<>).MakeGenericType(result)),
                typeof(CommitPolicy),
                typeof(CancellationToken),
            ],
            method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(["OnSuccess", "OnAnyResult"], Enum.GetNames<CommitPolicy>());
    }

    private static bool IsUseCaseEntryPoint(Type type) =>
        type.ResidesIn(ServicesNamespace)
        && type.GetInterfaces().Any(contract => contract.ResidesIn(ServiceInterfacesNamespace));

    private static bool IsUseCaseEntryPoint(string typeName) =>
        Scanned.Select(assembly => assembly.GetType(typeName)).OfType<Type>().FirstOrDefault() is { } type
        && IsUseCaseEntryPoint(type);
}
