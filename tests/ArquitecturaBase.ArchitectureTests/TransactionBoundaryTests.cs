using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.ArchitectureTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// Una sola forma de guardar (Etapa 1, decisión 0001): el límite transaccional lo abre el punto de entrada de un caso de
/// uso con IUnitOfWork.ExecuteInTransactionAsync, y solo UnitOfWork abre, confirma, deshace y guarda. Las reglas leen el
/// IL de Application, Infrastructure y Api (llamadas, tipos nombrados y literales), no el texto de las fuentes. Los
/// ensamblados de tests no se miran: el arnés puede abrir transacciones para sostener una fila. La única excepción con
/// nombre es DatabaseSeeder, el seed de arranque (ADR 0001, enmienda del 2026-09-28). Un módulo opcional suma sus claves
/// de lock y sus excepciones con los ganchos de abajo, en la parte de esta clase que vive en su carpeta
/// <c>Modules/&lt;M&gt;</c>: sin el módulo, el compilador borra las llamadas.
/// </summary>
public sealed partial class TransactionBoundaryTests
{
    // Los tipos de Infrastructure son internos y van por nombre: cada regla afirma que el detector ve al dueño
    // permitido, así un nombre que quedó viejo hace fallar la regla en lugar de dejarla pasando en silencio.
    private const string UnitOfWorkImplementation = "ArquitecturaBase.Infrastructure.Persistence.UnitOfWork";
    private const string UnitOfWorkRegistration = "ArquitecturaBase.Infrastructure.Persistence.PersistenceRegistration";

    // El seed de arranque abre su propio límite: no es un caso de uso ni tiene contrato en Interfaces/Services, pero tiene
    // que ser atómico y ponerse en fila entre réplicas (ADR 0001, enmienda del 2026-09-28). Es la única excepción con nombre
    // a los puntos de entrada, y las dos reglas afirman que el detector lo ve, así no queda vieja si cambia de nombre.
    private const string SeedRunner = "ArquitecturaBase.Infrastructure.Persistence.Seed.DatabaseSeeder";
    private const string AdvisoryLockExtensions = "ArquitecturaBase.Infrastructure.Persistence.Extensions.AdvisoryLockExtensions";
    private const string AdvisoryLockKeys = "ArquitecturaBase.Infrastructure.Persistence.Extensions.AdvisoryLockKeys";
    private const string MessageRetentionRepository =
        "ArquitecturaBase.Infrastructure.Persistence.Repositories.WhatsAppMessageRetentionRepository";
    private const string CacheExtensions = "ArquitecturaBase.Infrastructure.Caching.HybridCacheExtensions";

    // Del tipo, no de un texto: si IUnitOfWork cambia de nombre o de namespace, las reglas lo siguen buscando bien.
    private static readonly string UnitOfWorkContract = typeof(IUnitOfWork).FullName!;

    private static readonly Assembly Infrastructure = Assembly.Load("ArquitecturaBase.Infrastructure");

    private static readonly Assembly[] Scanned =
    [
        Assembly.Load("ArquitecturaBase.Application"),
        Infrastructure,
        Assembly.Load("ArquitecturaBase.Api"),
    ];

    // La clase concreta, que es interna, por nombre: si cambia, GetType lanza y fallan las reglas que la miran.
    private static readonly Type UnitOfWorkClass = Infrastructure.GetType(UnitOfWorkImplementation, throwOnError: true)!;

    private static readonly CallSites.Call[] Calls = [.. Scanned.SelectMany(assembly => CallSites.Calls(assembly))];

    private static readonly CallSites.Literal[] Literals = [.. Scanned.SelectMany(assembly => CallSites.Literals(assembly))];

    private static readonly CallSites.TypeUse[] TypeUses = [.. Scanned.SelectMany(assembly => CallSites.TypeUses(assembly))];

    private static readonly string[] SaveMethods = ["SaveChanges", "SaveChangesAsync"];

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
        "whatsapp-message:", "external-login:", "seed:", "users:",
    ];

    private static readonly string[] BulkMethods = ["ExecuteUpdate", "ExecuteUpdateAsync", "ExecuteDelete", "ExecuteDeleteAsync"];

    private static readonly string[] CacheFillMethods = ["GetOrCreateAsync", "SetAsync"];

    [Fact]
    public void Only_use_case_entry_points_receive_the_unit_of_work()
    {
        var receivers = Receivers(Scanned.SelectMany(assembly => assembly.GetTypes()));

        // Si la regla no encontrara a nadie, pasaría en silencio.
        Assert.NotEmpty(receivers);

        // Caso de control de la clase concreta: en src no hay (ni puede haber) un receptor legítimo, porque los puntos de
        // entrada viven en Application y no la ven. Se arma uno en memoria y el detector tiene que verlo.
        var concreteReceiver = TypeReceiving(UnitOfWorkClass);
        Assert.Equal([concreteReceiver], Receivers([concreteReceiver]));

        // La excepción del seed se afirma: si DatabaseSeeder dejara de recibir la unidad de trabajo o cambiara de nombre,
        // falla acá en lugar de quedar permitida en silencio.
        Assert.Contains(SeedRunner, receivers.Select(type => type.FullName));

        Assert.Empty(receivers
            .Where(type => !UseCaseEntryPoints.Contains(type) && type.FullName != SeedRunner)
            .Select(type => type.FullName));
    }

    [Fact]
    public void Only_use_case_entry_points_run_a_unit_of_work()
    {
        // También caza un service locator (GetRequiredService<IUnitOfWork>()) en Infrastructure o en Api. Llamar por la
        // clase concreta lo caza Only_the_registration_names_the_concrete_unit_of_work.
        var callers = Calls
            .Where(call => call.DeclaringType == UnitOfWorkContract
                && call.Method == nameof(IUnitOfWork.ExecuteInTransactionAsync))
            .Select(call => call.Owner)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Si el detector no viera a nadie, la regla pasaría en silencio.
        Assert.NotEmpty(callers);

        // La excepción del seed se afirma, como en la regla anterior.
        Assert.Contains(SeedRunner, callers);

        var violations = callers.Where(owner => !UseCaseEntryPoints.Contains(Scanned, owner) && owner != SeedRunner);

        Assert.Empty(violations);
    }

    [Fact]
    public void Only_the_registration_names_the_concrete_unit_of_work()
    {
        // Las dos reglas anteriores miran el contrato. Quien construya, resuelva o llame a la clase concreta se las
        // saltearía, así que nadie más la nombra en su IL: ni como el tipo que declara lo que se llama, ni como argumento
        // genérico (GetRequiredService<UnitOfWork>()), ni en un typeof o un cast. Solo la registración en DI y la propia
        // clase.
        var owners = TypeUses
            .Where(use => use.Type == UnitOfWorkImplementation)
            .Select(use => use.Owner)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Casos de control: la clase se nombra a sí misma cuando llama a sus miembros (el tipo que declara el método), y
        // la registración la nombra solo como argumento genérico de AddScoped. Si el detector dejara de ver cualquiera de
        // las dos formas, falla acá en lugar de pasar en silencio.
        Assert.Contains(UnitOfWorkImplementation, owners);
        Assert.Contains(UnitOfWorkRegistration, owners);

        var violations = owners.Where(owner => owner != UnitOfWorkImplementation && owner != UnitOfWorkRegistration);

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
        // Cecil resuelve cada llamada a SaveChanges o SaveChangesAsync al método que de verdad se llama y mira si lo declara
        // DbContext, un tipo que hereda de él o una interfaz: el nombre del contexto no cuenta. Los autoguardados de
        // Identity, OpenIddict y Data Protection y el Migrator viven en otros ensamblados. El seed tampoco guarda por su
        // cuenta: corre en el límite de DatabaseSeeder, y lo suyo baja con esos autoguardados o con el guardado final.
        var owners = SaveOwners(Scanned);

        Assert.Contains(UnitOfWorkImplementation, owners);

        // Casos de control, al pie de este archivo y cada uno con su propio dueño (el detector agrupa por el tipo de nivel
        // superior, así que dos controles anidados en un mismo tipo se taparían entre sí). Ledger no se llama "DbContext"
        // y esconde SaveChanges: un filtro por el nombre del tipo no lo vería. Bookkeeper guarda por una interfaz que
        // Journal implementa con el SaveChangesAsync heredado: si las interfaces no contaran, tampoco.
        var controlOwners = SaveOwners([typeof(TransactionBoundaryTests).Assembly]);
        Assert.Contains(typeof(Ledger).FullName, controlOwners);
        Assert.Contains(typeof(Bookkeeper).FullName, controlOwners);

        var violations = owners.Where(owner => owner != UnitOfWorkImplementation);

        Assert.Empty(violations);
    }

    [Fact]
    public void Advisory_lock_sql_and_keys_live_in_one_place()
    {
        // El texto de la clave ES el lock: un prefijo escrito dos veces se puede desalinear y dejar de poner en fila. Las
        // claves del núcleo viven en AdvisoryLockKeys; un módulo suma sus prefijos y el tipo que guarda sus claves, y cada
        // prefijo vive solo en su dueño.
        List<string> modulePrefixes = [];
        AddModuleLockKeyPrefixes(modulePrefixes);
        List<string> moduleOwners = [];
        AddModuleLockKeyOwners(moduleOwners);

        var sqlOwners = Literals
            .Where(literal => literal.Value.Contains("pg_advisory", StringComparison.Ordinal))
            .Select(literal => literal.Owner)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var keyOwners = Literals
            .Where(literal => LockKeyPrefixes.Concat(modulePrefixes)
                .Any(prefix => literal.Value.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(literal => literal.Owner)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Los dueños permitidos tienen que aparecer: si el detector dejara de verlos, la regla pasaría en silencio.
        Assert.Contains(AdvisoryLockExtensions, sqlOwners);
        Assert.Contains(AdvisoryLockKeys, keyOwners);
        Assert.All(moduleOwners, owner => Assert.Contains(owner, keyOwners));

        var violations = sqlOwners.Where(owner => owner != AdvisoryLockExtensions)
            .Concat(LockKeyViolations(Literals, modulePrefixes, moduleOwners));

        Assert.Empty(violations);
    }

    [Fact]
    public void Each_lock_key_prefix_lives_only_in_its_owner()
    {
        // Casos de control con literales armados a mano y un módulo inventado: un prefijo del núcleo fuera de
        // AdvisoryLockKeys, aunque sea en el dueño de las claves del módulo, falla; uno del módulo en AdvisoryLockKeys,
        // también.
        const string ControlKeys = "ArquitecturaBase.Infrastructure.Modules.Control.Persistence.ControlLockKeys";
        string[] modulePrefixes = ["control:"];
        string[] moduleOwners = [ControlKeys];

        Assert.Empty(LockKeyViolations(
            [new CallSites.Literal(AdvisoryLockKeys, "users:admins"), new CallSites.Literal(ControlKeys, "control:x")],
            modulePrefixes,
            moduleOwners));
        Assert.NotEmpty(LockKeyViolations([new CallSites.Literal(ControlKeys, "users:admins")], modulePrefixes, moduleOwners));
        Assert.NotEmpty(LockKeyViolations([new CallSites.Literal(AdvisoryLockKeys, "control:x")], modulePrefixes, moduleOwners));
        Assert.NotEmpty(LockKeyViolations(
            [new CallSites.Literal("ArquitecturaBase.Infrastructure.Persistence.Repositories.UserRepository", "control:x")],
            modulePrefixes,
            moduleOwners));
    }

    [Fact]
    public void Bulk_updates_and_deletes_only_where_documented()
    {
        // ExecuteUpdate y ExecuteDelete saltean los interceptores: solo la retención, sobre WhatsAppMessage, que no es
        // IAuditable ni ISoftDeletable. Un módulo suma sus excepciones.
        List<string> allowed = [MessageRetentionRepository];
        AddModuleBulkUpdateOwners(allowed);

        var owners = BulkUpdaters(Calls);

        // Caso de control, al pie de este archivo: Purger llama a ExecuteDeleteAsync. Así la regla prueba su detector
        // aunque el núcleo no tenga ninguna excepción.
        Assert.Contains(typeof(Purger).FullName, BulkUpdaters(CallSites.Calls(typeof(TransactionBoundaryTests).Assembly)));

        // Cada excepción tiene que aparecer: si quedara vieja, la regla la seguiría permitiendo en silencio.
        Assert.All(allowed, owner => Assert.Contains(owner, owners));

        var violations = owners.Where(owner => !allowed.Contains(owner, StringComparer.Ordinal));

        Assert.Empty(violations);
    }

    [Fact]
    public void Only_the_cache_extensions_fill_hybrid_cache()
    {
        // Una fábrica de HybridCache que leyera con el contexto de quien llama correría adentro de su límite: vería lo que
        // todavía no se confirmó, lo cachearía y le ocuparía la conexión que el rollback necesita para soltar los locks.
        // HybridCacheExtensions.GetOrCreateInOwnScopeAsync abre un scope propio y es la única que llena el caché. SetAsync
        // también cuenta: el valor lo calculó quien llama, quizás adentro de su límite, con el mismo riesgo.
        var owners = CacheFillers(Calls);

        // Caso de control, al pie de este archivo: nadie en src llama a SetAsync, así que CacheWriter prueba que el
        // detector lo ve. Si dejara de verlo, la regla pasaría en silencio.
        var controlOwners = CacheFillers(CallSites.Calls(typeof(TransactionBoundaryTests).Assembly));
        Assert.Contains(typeof(CacheWriter).FullName, controlOwners);

        // Assert.Equal y no Empty: también prueba que el detector ve al dueño permitido.
        Assert.Equal([CacheExtensions], owners);
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

    /// <summary>Los tipos de un módulo que pueden tener literales con un prefijo de lock: los que guardan sus claves.</summary>
    static partial void AddModuleLockKeyOwners(List<string> owners);

    /// <summary>Los prefijos de las claves de lock de un módulo, como <see cref="LockKeyPrefixes"/>.</summary>
    static partial void AddModuleLockKeyPrefixes(List<string> prefixes);

    /// <summary>Los tipos de un módulo que pueden llamar a ExecuteUpdate o ExecuteDelete, cada uno con su motivo.</summary>
    static partial void AddModuleBulkUpdateOwners(List<string> owners);

    /// <summary>
    /// Los literales con un prefijo de lock que no están en su dueño: los del núcleo (<see cref="LockKeyPrefixes"/>) van
    /// solo en AdvisoryLockKeys, y los de un módulo, solo en el tipo que guarda las claves del módulo.
    /// </summary>
    private static string[] LockKeyViolations(
        IEnumerable<CallSites.Literal> literals,
        IReadOnlyCollection<string> modulePrefixes,
        IReadOnlyCollection<string> moduleOwners) =>
    [
        .. literals
            .Where(literal => LockKeyPrefixes.Any(prefix => literal.Value.StartsWith(prefix, StringComparison.Ordinal))
                ? literal.Owner != AdvisoryLockKeys
                : modulePrefixes.Any(prefix => literal.Value.StartsWith(prefix, StringComparison.Ordinal))
                    && !moduleOwners.Contains(literal.Owner, StringComparer.Ordinal))
            .Select(literal => $"{literal.Owner}: {literal.Value}")
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>Quienes llaman a ExecuteUpdate o a ExecuteDelete, que saltean los interceptores.</summary>
    private static string[] BulkUpdaters(IEnumerable<CallSites.Call> calls) =>
    [
        .. calls
            .Where(call => BulkMethods.Contains(call.Method, StringComparer.Ordinal))
            .Select(call => call.Owner)
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>Quienes llenan HybridCache: llaman a GetOrCreateAsync o a SetAsync.</summary>
    private static string[] CacheFillers(IEnumerable<CallSites.Call> calls) =>
    [
        .. calls
            .Where(call => call.DeclaringType == typeof(HybridCache).FullName
                && CacheFillMethods.Contains(call.Method, StringComparer.Ordinal))
            .Select(call => call.Owner)
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>Los tipos que reciben por constructor el contrato o la clase concreta de la unidad de trabajo.</summary>
    private static Type[] Receivers(IEnumerable<Type> types) =>
    [
        .. types
            .Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Where(type => type
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(constructor => constructor.GetParameters().Any(parameter =>
                    parameter.ParameterType == typeof(IUnitOfWork) || parameter.ParameterType == UnitOfWorkClass))),
    ];

    /// <summary>
    /// Quienes llaman a SaveChanges o SaveChangesAsync de un contexto o de una interfaz, por el tipo que de verdad declara
    /// el método y no por el nombre que figura en la llamada.
    /// </summary>
    private static string[] SaveOwners(IEnumerable<Assembly> assemblies) =>
    [
        .. assemblies
            .SelectMany(assembly => CallSites.CallsThatCanReach(assembly, typeof(DbContext), SaveMethods))
            .Select(call => call.Owner)
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>
    /// Un tipo armado en memoria cuyo constructor recibe <paramref name="dependency"/>. No se instancia nunca: alcanza con
    /// que la reflexión vea el parámetro, aunque su tipo sea interno de otro ensamblado.
    /// </summary>
    private static Type TypeReceiving(Type dependency)
    {
        var name = new AssemblyName("TransactionBoundaryControl");
        var type = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect)
            .DefineDynamicModule(name.Name!)
            .DefineType("TransactionBoundaryControl.Receiver", TypeAttributes.Public | TypeAttributes.Sealed);
        var il = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [dependency]).GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        return type.CreateType();
    }
}

// Los casos de control de Only_the_unit_of_work_saves_the_context, de Only_the_cache_extensions_fill_hybrid_cache y de
// Bulk_updates_and_deletes_only_where_documented. Van fuera de TransactionBoundaryTests y cada uno es su propio dueño. No
// se ejecutan nunca: solo importa su IL.

/// <summary>
/// Un contexto que no se llama "DbContext" y esconde SaveChanges con <c>new</c>, así quien lo llama referencia
/// Ledger::SaveChanges y no la declaración de DbContext.
/// </summary>
file sealed class Ledger : DbContext
{
    public new int SaveChanges() => throw new NotSupportedException();

    public static int SaveOnce()
    {
        using var ledger = new Ledger();

        return ledger.SaveChanges();
    }
}

/// <summary>
/// La forma de sacar el guardado de EF más común en una plantilla por capas: una interfaz sin referencias a EF que el
/// contexto implementa con el método que hereda de DbContext.
/// </summary>
file interface IJournal
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Implementa IJournal sin escribir nada: el SaveChangesAsync heredado de DbContext alcanza.</summary>
file abstract class Journal : DbContext, IJournal;

/// <summary>
/// Guarda por IJournal: el IL referencia IJournal::SaveChangesAsync, un método que declara la interfaz, no DbContext.
/// </summary>
file static class Bookkeeper
{
    public static Task<int> Save(IJournal journal) => journal.SaveChangesAsync();
}

/// <summary>
/// Llena HybridCache con SetAsync y un valor que calculó quien llama, quizás adentro de su límite: lo mismo que una fábrica
/// que lee con el contexto de quien llama.
/// </summary>
file static class CacheWriter
{
    public static ValueTask Write(HybridCache cache) => cache.SetAsync("control", 0);
}

/// <summary>Borra en bloque con ExecuteDeleteAsync, que saltea los interceptores de auditoría y de borrado lógico.</summary>
file static class Purger
{
    public static Task<int> Purge(IQueryable<Ledger> rows, CancellationToken cancellationToken) =>
        rows.ExecuteDeleteAsync(cancellationToken);
}
