using System.Reflection;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users.ReadModels;
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
    private const string SignInManager = "Microsoft.AspNetCore.Identity.SignInManager`1";
    private const string SignInServiceImplementation = "ArquitecturaBase.Infrastructure.Identity.SignInService";

    // Los métodos de UserManager que son del ingreso y no de los datos de la cuenta: el bloqueo y el security stamp.
    private static readonly string[] SessionUserManagerMethods =
        ["IsLockedOutAsync", "AccessFailedAsync", "ResetAccessFailedCountAsync", "UpdateSecurityStampAsync"];

    private static readonly string[] OpenIddictManagers =
    [
        "OpenIddict.Abstractions.IOpenIddictAuthorizationManager",
        "OpenIddict.Abstractions.IOpenIddictTokenManager",
    ];

    private static readonly Assembly[] Scanned =
    [
        Assembly.Load("ArquitecturaBase.Application"),
        Assembly.Load("ArquitecturaBase.Infrastructure"),
        Assembly.Load("ArquitecturaBase.Api"),
    ];

    private static readonly CallSites.Call[] Calls = [.. Scanned.SelectMany(assembly => CallSites.Calls(assembly))];

    private static readonly CallSites.TypeUse[] TypeUses =
        [.. Scanned.SelectMany(assembly => CallSites.TypeUses(assembly))];

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

    [Fact]
    public void Sign_in_contract_stays_small_and_technical()
    {
        var methods = typeof(ISignInService).GetMethods();
        string[] dataVerbs = ["Find", "List", "Exists", "Count", "Create", "Add", "Set", "Remove", "Restore", "Delete", "Update"];
        Type[] accountData = [typeof(UserAccount), typeof(UserDetail)];

        // La alarma del plan maestro: si vuelve a crecer, se está volviendo a armar una fachada.
        Assert.InRange(methods.Length, 1, 12);
        Assert.Empty(methods
            .Where(method => dataVerbs.Any(verb => method.Name.StartsWith(verb, StringComparison.Ordinal)))
            .Select(method => method.Name));
        Assert.Empty(methods.Where(method => Names(method.ReturnType, accountData)).Select(method => method.Name));
    }

    [Fact]
    public void Only_the_sign_in_service_touches_sessions()
    {
        // SignInManager, el bloqueo, el security stamp y las revocaciones por sujeto de OpenIddict: lo técnico del
        // ingreso vive en un solo lugar.
        var owners = TypeUses
            .Where(use => use.Type == SignInManager)
            .Select(use => use.Owner)
            .Concat(Calls
                .Where(call => (IsOn(call, UserManager)
                        && SessionUserManagerMethods.Contains(call.Method, StringComparer.Ordinal))
                    || (OpenIddictManagers.Contains(call.DeclaringType, StringComparer.Ordinal)
                        && call.Method == "RevokeBySubjectAsync"))
                .Select(call => call.Owner))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert.Equal y no Empty: también prueba que el detector ve a SignInService.
        Assert.Equal([SignInServiceImplementation], owners);
    }

    /// <summary>Si la llamada es a un método de <paramref name="genericType"/>, con sus argumentos de tipo o sin ellos.</summary>
    private static bool IsOn(CallSites.Call call, string genericType) =>
        call.DeclaringType == genericType || call.DeclaringType.StartsWith(genericType + "<", StringComparison.Ordinal);

    /// <summary>Si <paramref name="type"/> es uno de <paramref name="targets"/> o los lleva adentro (genéricos y arreglos).</summary>
    private static bool Names(Type type, Type[] targets) =>
        targets.Contains(type)
        || (type.IsGenericType && type.GetGenericArguments().Any(argument => Names(argument, targets)))
        || (type.IsArray && Names(type.GetElementType()!, targets));
}
