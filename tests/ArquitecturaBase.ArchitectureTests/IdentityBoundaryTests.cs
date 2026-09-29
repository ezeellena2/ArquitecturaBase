using System.Reflection;
using System.Security.Claims;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.ArchitectureTests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// Identity sin fachada (Etapa 2): la cuenta se carga para modificarla de una sola forma, lo técnico del ingreso vive solo
/// en SignInService, la sesión la abre solo el ingreso, las cierra solo AccountAccessRevoker, la cookie la borran solo
/// ConnectController y SignInService, y el bot solo mira el bloqueo. Como TransactionBoundaryTests, lee el IL de
/// Application, Infrastructure y Api con Mono.Cecil; de los ensamblados de tests se mira solo este, por los casos de
/// control que viven al pie del archivo. Un módulo opcional suma sus propias reglas en la parte de esta clase que vive en
/// su carpeta <c>Modules/&lt;M&gt;</c>, con acceso a los detectores de acá.
/// </summary>
public sealed partial class IdentityBoundaryTests
{
    private const string UserManager = "Microsoft.AspNetCore.Identity.UserManager`1";
    private const string UserRepository = "ArquitecturaBase.Infrastructure.Persistence.Repositories.UserRepository";
    private const string SignInManager = "Microsoft.AspNetCore.Identity.SignInManager`1";
    private const string SignInServiceImplementation = "ArquitecturaBase.Infrastructure.Identity.SignInService";
    private const string HttpContextAuthentication = "Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions";
    private const string AuthenticationService = "Microsoft.AspNetCore.Authentication.IAuthenticationService";
    private const string Connect = "ArquitecturaBase.Api.Controllers.ConnectController";
    private const string AuthServices = "ArquitecturaBase.Application.Services.Auth.";
    private const string AccessRevoker = "ArquitecturaBase.Application.Services.Users.AccountAccessRevoker";
    private const string WhatsAppServices = "ArquitecturaBase.Application.Services.WhatsApp.";

    private static readonly string SignInContract = typeof(ISignInService).FullName!;

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

    // Las llamadas de este ensamblado: ahí viven los casos de control, que no se ejecutan nunca.
    private static readonly CallSites.Call[] ControlCalls = [.. CallSites.Calls(typeof(IdentityBoundaryTests).Assembly)];

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
        Type[] accountData = [typeof(UserAccount), typeof(UserDetailRow), typeof(UserDetailResponse)];

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
        // SignInManager, el bloqueo, el security stamp, las revocaciones por sujeto de OpenIddict y HttpContext.SignInAsync
        // o IAuthenticationService.SignInAsync, que escribirían la cookie de la aplicación sin la guarda de SignInService
        // (adentro de un límite, lanza): lo técnico del ingreso vive en un solo lugar. El passthrough de OpenIddict
        // (ConnectController) emite tokens con ControllerBase.SignIn, que no cuenta; el cierre de sesión tiene su propia
        // regla, Only_the_connect_endpoints_and_the_sign_in_service_sign_out.
        var owners = SessionOwners(Calls, TypeUses);

        // Caso de control: nadie en src llama a HttpContext.SignInAsync, así que el detector se prueba con CookieWriter,
        // al pie de este archivo. Si dejara de verlo, la regla pasaría en silencio.
        Assert.Contains(typeof(CookieWriter).FullName, SessionOwners(ControlCalls, []));

        // Caso de control: IAuthenticationService.SignInAsync, a lo que llega HttpContext.SignInAsync, escribe la misma
        // cookie sin la extensión; tampoco lo llama nadie en src.
        Assert.Contains(typeof(AuthenticationServiceWriter).FullName, SessionOwners(ControlCalls, []));

        // Caso de control: SignInManager<T> se detecta por el tipo que nombra el IL, no por una llamada.
        Assert.Contains(typeof(SignInManagerUser).FullName, SessionOwners([], CallSites.TypeUses(typeof(IdentityBoundaryTests).Assembly)));

        // Caso de control: RevokeBySubjectAsync de IOpenIddictTokenManager, las revocaciones por sujeto.
        Assert.Contains(typeof(SubjectRevoker).FullName, SessionOwners(ControlCalls, []));

        // Assert.Equal y no Empty: también prueba que el detector ve a SignInService.
        Assert.Equal([SignInServiceImplementation], owners);
    }

    [Fact]
    public void Only_the_connect_endpoints_and_the_sign_in_service_sign_out()
    {
        // Borrar una cookie (HttpContext.SignOutAsync o IAuthenticationService.SignOutAsync) es cosa de ConnectController,
        // que cierra la sesión de la aplicación en authorize y logout, y de SignInService, que borra la cookie externa de
        // Google. Cerrar las sesiones de una cuenta desde un servicio pasa por AccountAccessRevoker, no por acá.
        var owners = SignOutOwners(Calls);

        // Casos de control: el detector ve las dos formas, al pie de este archivo.
        var control = SignOutOwners(ControlCalls);
        Assert.Contains(typeof(CookieWriter).FullName, control);
        Assert.Contains(typeof(AuthenticationServiceWriter).FullName, control);

        // El conjunto exacto: así también prueba que el detector ve las llamadas de src.
        Assert.Equal([Connect, SignInServiceImplementation], owners);
    }

    [Fact]
    public void Only_entry_points_open_a_session()
    {
        var owners = OwnersOf(nameof(ISignInService.SignInAsync));

        // El conjunto exacto: así también prueba que el detector ve las llamadas.
        Assert.Equal(
            [AuthServices + "ExternalLoginService", AuthServices + "LoginCodeService", AuthServices + "LoginLinkService"],
            owners);

        // Y todos son puntos de entrada: la cookie sale del método que abre el límite, después de que confirma.
        Assert.All(owners, owner => Assert.True(UseCaseEntryPoints.Contains(Scanned, owner), owner));

        // La regla de oro de WhatsApp: un mensaje nunca abre una sesión.
        Assert.DoesNotContain(owners, owner => owner.StartsWith(WhatsAppServices, StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_sign_in_code_counts_failed_attempts()
    {
        // Confirmar un destino desde el perfil no es un ingreso: no suma a los fallos de la cuenta (reglas de identidad).
        Assert.Equal([AuthServices + "LoginCodeVerifier"], OwnersOf(nameof(ISignInService.RegisterFailedAttemptAsync)));
    }

    [Fact]
    public void Only_sign_in_paths_reset_failed_attempts()
    {
        // Entrar bien pone en cero los intentos fallidos, por cualquiera de los tres ingresos (reglas de identidad), y
        // nada más los toca. El conjunto exacto: así también prueba que el detector ve las tres llamadas.
        Assert.Equal(
            [AuthServices + "ExternalLoginService", AuthServices + "LoginCodeVerifier", AuthServices + "LoginLinkVerifier"],
            OwnersOf(nameof(ISignInService.ResetFailedAttemptsAsync)));
    }

    [Fact]
    public void Only_the_access_revoker_closes_sessions()
    {
        // Cerrar las sesiones sin invalidar los enlaces pendientes dejaría servir uno que el bot mandó antes del corte:
        // AccountAccessRevoker hace las dos cosas, y desactivar, eliminar y desvincular desde la administración pasan por
        // él. El conjunto exacto: así también prueba que el detector ve la llamada, y cualquier otro dueño la rompe.
        Assert.Equal([AccessRevoker], OwnersOf(nameof(ISignInService.RevokeSessionsAsync)));
    }

    [Fact]
    public void Whatsapp_services_only_check_the_lockout()
    {
        // La regla de oro de WhatsApp por el lado del contrato: el bot recibe ISignInService solo para mirar el bloqueo. No
        // abre una sesión, no suma ni pone en cero intentos fallidos, no cierra sesiones ni toca la cookie de Google.
        var calls = SignInCallsFrom(WhatsAppServices);

        // Casos de control: el detector ve al bot mirando el bloqueo, y la misma regla sobre los servicios del ingreso
        // encuentra lo que acá estaría prohibido. Si dejara de ver cualquiera de los dos, la regla pasaría en silencio.
        Assert.Contains(calls, call => call.Method == nameof(ISignInService.IsLockedOutAsync));
        Assert.Contains(
            BeyondTheLockout(SignInCallsFrom(AuthServices)),
            call => call.Method == nameof(ISignInService.SignInAsync));

        Assert.Empty(BeyondTheLockout(calls).Select(call => call.Owner + "." + call.Method));
    }

    /// <summary>
    /// Los tipos que tocan la sesión: nombran SignInManager, llaman a los métodos de UserManager que son del ingreso, a
    /// RevokeBySubjectAsync de OpenIddict, a HttpContext.SignInAsync o a IAuthenticationService.SignInAsync. Ordenados.
    /// </summary>
    private static string[] SessionOwners(IEnumerable<CallSites.Call> calls, IEnumerable<CallSites.TypeUse> typeUses) =>
    [
        .. typeUses
            .Where(use => use.Type == SignInManager)
            .Select(use => use.Owner)
            .Concat(calls
                .Where(call => (IsOn(call, UserManager)
                        && SessionUserManagerMethods.Contains(call.Method, StringComparer.Ordinal))
                    || (OpenIddictManagers.Contains(call.DeclaringType, StringComparer.Ordinal)
                        && call.Method == "RevokeBySubjectAsync")
                    || (IsAuthentication(call) && call.Method == "SignInAsync"))
                .Select(call => call.Owner))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>Los tipos que llaman a HttpContext.SignOutAsync o a IAuthenticationService.SignOutAsync. Ordenados.</summary>
    private static string[] SignOutOwners(IEnumerable<CallSites.Call> calls) =>
    [
        .. calls
            .Where(call => IsAuthentication(call) && call.Method == "SignOutAsync")
            .Select(call => call.Owner)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>Si la llamada es a las extensiones de autenticación de HttpContext o a IAuthenticationService.</summary>
    private static bool IsAuthentication(CallSites.Call call) =>
        call.DeclaringType is HttpContextAuthentication or AuthenticationService;

    /// <summary>Si la llamada es a un método de <paramref name="genericType"/>, con sus argumentos de tipo o sin ellos.</summary>
    private static bool IsOn(CallSites.Call call, string genericType) =>
        call.DeclaringType == genericType || call.DeclaringType.StartsWith(genericType + "<", StringComparison.Ordinal);

    /// <summary>Si <paramref name="type"/> es uno de <paramref name="targets"/> o los lleva adentro (genéricos y arreglos).</summary>
    private static bool Names(Type type, Type[] targets) =>
        targets.Contains(type)
        || (type.IsGenericType && type.GetGenericArguments().Any(argument => Names(argument, targets)))
        || (type.IsArray && Names(type.GetElementType()!, targets));

    /// <summary>Las llamadas a ISignInService de los tipos cuyo nombre completo empieza con <paramref name="prefix"/>.</summary>
    private static CallSites.Call[] SignInCallsFrom(string prefix) =>
    [
        .. Calls.Where(call => call.DeclaringType == SignInContract && call.Owner.StartsWith(prefix, StringComparison.Ordinal)),
    ];

    /// <summary>Las llamadas a ISignInService que no son para mirar el bloqueo.</summary>
    private static IEnumerable<CallSites.Call> BeyondTheLockout(IEnumerable<CallSites.Call> calls) =>
        calls.Where(call => call.Method != nameof(ISignInService.IsLockedOutAsync));

    /// <summary>Los tipos de nivel superior que llaman a ese miembro de ISignInService, ordenados.</summary>
    private static string[] OwnersOf(string signInMember) =>
    [
        .. Calls
            .Where(call => call.DeclaringType == SignInContract && call.Method == signInMember)
            .Select(call => call.Owner)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];
}

// Los casos de control de Only_the_sign_in_service_touches_sessions y de
// Only_the_connect_endpoints_and_the_sign_in_service_sign_out. Van fuera de IdentityBoundaryTests, con su propio dueño, y
// no se ejecutan nunca: solo importa su IL.

/// <summary>Escribe y borra la cookie de la aplicación con las extensiones de HttpContext, sin pasar por SignInService.</summary>
file static class CookieWriter
{
    public static Task SignIn(HttpContext context, ClaimsPrincipal principal) =>
        context.SignInAsync("Identity.Application", principal);

    public static Task SignOut(HttpContext context) => context.SignOutAsync("Identity.Application");
}

/// <summary>Escribe y borra la cookie de la aplicación con IAuthenticationService, sin las extensiones de HttpContext.</summary>
file static class AuthenticationServiceWriter
{
    public static Task SignIn(IAuthenticationService authentication, HttpContext context, ClaimsPrincipal principal) =>
        authentication.SignInAsync(context, "Identity.Application", principal, properties: null);

    public static Task SignOut(IAuthenticationService authentication, HttpContext context) =>
        authentication.SignOutAsync(context, "Identity.Application", properties: null);
}

/// <summary>Usa SignInManager sin pasar por SignInService: el detector lo ve por el tipo que nombra.</summary>
file static class SignInManagerUser
{
    public static bool IsSignedIn(SignInManager<IdentityUser> manager, ClaimsPrincipal principal) =>
        manager.IsSignedIn(principal);
}

/// <summary>Revoca los tokens de un sujeto sin pasar por SignInService.</summary>
file static class SubjectRevoker
{
    public static ValueTask<long> Revoke(IOpenIddictTokenManager tokens, string subject, CancellationToken cancellationToken) =>
        tokens.RevokeBySubjectAsync(subject, cancellationToken);
}
