using System.Collections;
using System.Reflection;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.ArchitectureTests.Support;
using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// La convención de nombres de repositorios y lectores (ADR 0008): el prefijo de un método de Interfaces/Persistence dice
/// qué devuelve. Se verifica por el tipo de retorno, que la reflexión ve, y por el IL: solo los lectores llaman a
/// AsNoTracking, así "Get" siempre devuelve una entidad seguida.
/// </summary>
public sealed class PersistenceNamingTests
{
    private const string PersistenceNamespace = "ArquitecturaBase.Application.Interfaces.Persistence";
    private const string InfrastructureNamespace = "ArquitecturaBase.Infrastructure.";
    private const string ReadersNamespace = "ArquitecturaBase.Infrastructure.Persistence.Readers";
    private const string QueryableExtensions = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";

    private static readonly string[] Verbs =
    [
        "Get", "Find", "List", "Exists", "Count", "Lock", "Add", "Create", "Update", "Delete", "Set", "Remove", "Restore",
        "Clear",
    ];

    private static readonly string[] ReaderVerbs = ["Find", "List", "Exists", "Count"];

    private static readonly Type[] Contracts =
    [
        .. typeof(IUnitOfWork).Assembly.GetTypes()
            .Where(type => type.IsInterface && type.Namespace == PersistenceNamespace && type != typeof(IUnitOfWork))
            .OrderBy(type => type.Name, StringComparer.Ordinal),
    ];

    private static readonly MethodInfo[] Methods =
        [.. Contracts.SelectMany(contract => contract.GetMethods().Where(method => !method.IsSpecialName))];

    [Fact]
    public void Persistence_methods_start_with_a_known_verb()
    {
        // Si el escaneo no encontrara contratos, las reglas pasarían en silencio.
        Assert.NotEmpty(Methods);

        Assert.Empty(UnknownVerbs());
    }

    [Fact]
    public void Reads_return_what_their_prefix_promises()
    {
        Assert.Empty(WrongShapes());
    }

    [Fact]
    public void Readers_only_read()
    {
        Assert.Contains(Contracts, contract => contract.Name.EndsWith("Reader", StringComparison.Ordinal));

        Assert.Empty(ReaderWrites());
    }

    [Fact]
    public void Only_readers_skip_tracking()
    {
        var owners = CallSites.Calls(Assembly.Load("ArquitecturaBase.Infrastructure"))
            .Where(call => call.DeclaringType == QueryableExtensions
                && call.Method is "AsNoTracking" or "AsNoTrackingWithIdentityResolution")
            .Select(call => call.Owner)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Caso de control: el detector ve a los lectores.
        Assert.Contains(ReadersNamespace + ".UserReader", owners);

        // Sin el prefijo del ensamblado: xUnit corta cada texto del mensaje a los 50 caracteres, y así se ve el tipo.
        var violations = owners
            .Where(owner => !owner.StartsWith(ReadersNamespace + ".", StringComparison.Ordinal))
            .Select(owner => owner.StartsWith(InfrastructureNamespace, StringComparison.Ordinal)
                ? owner[InfrastructureNamespace.Length..]
                : owner)
            .ToArray();

        Assert.Empty(violations);
    }

    private static IEnumerable<string> UnknownVerbs() =>
        Methods
            .Where(method => VerbOf(method.Name) is null)
            .Select(NameOf);

    private static IEnumerable<string> WrongShapes() =>
        Methods.Where(method => !HasTheShapeOfItsVerb(method)).Select(NameOf);

    private static IEnumerable<string> ReaderWrites() =>
        Methods
            .Where(method => method.DeclaringType!.Name.EndsWith("Reader", StringComparison.Ordinal))
            .Where(method => VerbOf(method.Name) is not { } verb || !ReaderVerbs.Contains(verb, StringComparer.Ordinal))
            .Select(NameOf);

    private static bool HasTheShapeOfItsVerb(MethodInfo method)
    {
        var result = ResultOf(method.ReturnType);

        return VerbOf(method.Name) switch
        {
            "Get" => method.DeclaringType!.Name.EndsWith("Repository", StringComparison.Ordinal)
                && result is not null
                && typeof(Entity).IsAssignableFrom(result),
            "Find" => result is not null && !typeof(Entity).IsAssignableFrom(result),
            "List" => result is not null && IsListOrPage(result),
            "Exists" => result == typeof(bool),
            "Count" => result == typeof(int) || result?.Name.EndsWith("Counts", StringComparison.Ordinal) == true,
            "Lock" => method.ReturnType == typeof(Task),
            "Add" when method.Name == "Add" => method.ReturnType == typeof(void),
            _ => true,
        };
    }

    /// <summary>El T de un Task&lt;T&gt;, sin el Nullable de un valor; null si no es un Task&lt;T&gt;.</summary>
    private static Type? ResultOf(Type returnType) =>
        returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>)
            ? Nullable.GetUnderlyingType(returnType.GetGenericArguments()[0]) ?? returnType.GetGenericArguments()[0]
            : null;

    private static bool IsListOrPage(Type type) =>
        (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
        || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PagedResult<>));

    private static string? VerbOf(string name) =>
        Verbs.FirstOrDefault(verb => name == verb
            || (name.Length > verb.Length
                && name.StartsWith(verb, StringComparison.Ordinal)
                && char.IsUpper(name[verb.Length])));

    private static string NameOf(MethodInfo method) => method.DeclaringType!.Name + "." + method.Name;
}
