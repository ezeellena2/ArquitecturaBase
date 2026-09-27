using System.Reflection;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// Identity sin fachada (Etapa 2): la cuenta se carga para modificarla de una sola forma, lo técnico del ingreso vive solo
/// en SignInService y la sesión la abre solo el ingreso. Como TransactionBoundaryTests, lee el IL de Application,
/// Infrastructure y Api con Mono.Cecil; los ensamblados de tests no se miran.
/// </summary>
public sealed class IdentityBoundaryTests
{
    private const string UserManager = "Microsoft.AspNetCore.Identity.UserManager`1";
    private const string UserRepository = "ArquitecturaBase.Infrastructure.Persistence.Repositories.UserRepository";

    private static readonly Assembly[] Scanned =
    [
        Assembly.Load("ArquitecturaBase.Application"),
        Assembly.Load("ArquitecturaBase.Infrastructure"),
        Assembly.Load("ArquitecturaBase.Api"),
    ];

    private static readonly CallSites.Call[] Calls = [.. Scanned.SelectMany(assembly => CallSites.Calls(assembly))];

    [Fact]
    public void Accounts_are_loaded_with_one_query()
    {
        // UserManager.FindByIdAsync devuelve lo que ya sigue el contexto sin ir a la base, aunque esté marcado borrado,
        // y no recibe el token de quien llama. La única carga por Id de una cuenta no borrada para modificarla es
        // UserManagerExtensions.RequireUserAsync (RestoreAsync, que carga la borrada, y el seed, que busca por correo,
        // no usan FindByIdAsync).
        var owners = Calls
            .Where(call => IsOn(call, UserManager) && call.Method == "FindByIdAsync")
            .Select(call => call.Owner)
            .Distinct(StringComparer.Ordinal);

        // Caso de control: el detector ve las llamadas a UserManager`1 (el alta de UserRepository).
        Assert.Contains(Calls, call => IsOn(call, UserManager) && call.Method == "CreateAsync" && call.Owner == UserRepository);

        Assert.Empty(owners);
    }

    /// <summary>Si la llamada es a un método de <paramref name="genericType"/>, con sus argumentos de tipo o sin ellos.</summary>
    private static bool IsOn(CallSites.Call call, string genericType) =>
        call.DeclaringType == genericType || call.DeclaringType.StartsWith(genericType + "<", StringComparison.Ordinal);
}
