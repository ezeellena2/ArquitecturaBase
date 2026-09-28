> **HISTÓRICO. Etapa cerrada el 2026-09-27.** No ejecutar: las casillas sin marcar no son trabajo pendiente y la sub-skill de abajo ya no aplica. Registro de cómo se diseñó y se ejecutó la Etapa 2, desde el commit `docs: plan detallado de la Etapa 2 (IIdentityService solo técnico)` hasta `fix: la cookie del canje del enlace sale después del commit`. El cierre y lo que se hizo distinto están en la sección "Etapa 2" del plan maestro; las reglas vigentes, en las reglas de identidad y en el spec de arquitectura, y la convención de nombres, en el ADR 0008. Donde este documento hable de `IIdentityService`, `IdentityService` o `FakeIdentityService`, describe pasos intermedios que ya no existen.

# Etapa 2: `IIdentityService` solo técnico — plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDA: usá `superpowers:subagent-driven-development` (recomendado) o `superpowers:executing-plans` para ejecutar este plan tarea por tarea. Los pasos usan casillas (`- [ ]`) para el seguimiento. Cada tarea termina en un commit directo en `main` (sin ramas, sin worktrees y sin push), con `dotnet build ArquitecturaBase.slnx` sin advertencias y todos los tests en verde, corridos en tandas (ver "Comandos") con Docker levantado.

**Objetivo:** que para cada operación sobre cuentas haya un solo camino. Los datos de cuentas se leen con `IUserReader` y se escriben con `IUserRepository`; lo técnico del ingreso (el bloqueo, la cookie de la aplicación, la cookie externa y el cierre de sesiones) vive en un `ISignInService` de 7 miembros; `IIdentityService` desaparece. Con eso se cierran los pendientes que dejó la Etapa 1: las tres cookies de ingreso salen después del commit, la fábrica de caché de `PermissionService` lee en su propio scope, las lecturas de roles sin llamadores se van, los tests dejan de armar datos con `UserManager`/`RoleManager` en autocommit, los arneses dejan de pedir un `return true;` y la cobertura de las escrituras fuera del límite deja de ser una lista a mano. Además: la cuenta se carga para modificarla con una sola consulta, el bot relee la cuenta vinculada después de su lock y los contratos de persistencia siguen una convención de nombres que verifica un test.

**Arquitectura:** no cambia la arquitectura canónica (el spec de arquitectura, hoy `docs/architecture/backend.md`): controllers → servicios → repositorios, lectores e integraciones. `ISignInService` es un contrato de integración (`Application/Interfaces/Integrations`) implementado en `Infrastructure/Identity`, como `IPermissionService`. La migración se hace en fases que dejan todo en verde en cada commit: primero la preparación (arneses, caché), después una sola carga de la cuenta, nace `ISignInService` con `IdentityService` delegando en él, los consumidores migran uno por commit, los tests dejan `IIdentityService`, se borra la fachada, se parte el doble, se aplica la convención de nombres y, al final, los dos únicos cambios de comportamiento (la cookie del código y la del enlace), cada uno en su commit para poder revertirlo solo. Las reglas nuevas se verifican leyendo el IL con Mono.Cecil (`IdentityBoundaryTests`, `TransactionBoundaryTests`) y por reflexión (`PersistenceNamingTests`).

**Stack:** .NET 10, ASP.NET Core MVC, EF Core 10 + Npgsql 10 (READ COMMITTED), ASP.NET Core Identity, OpenIddict, `Microsoft.Extensions.Caching.Hybrid`, FluentValidation, xUnit v3 (Microsoft Testing Platform), Testcontainers `postgres:18.3`, NetArchTest + Mono.Cecil.

**Fuentes de este plan:** la sección "Etapa 2" del plan maestro (`docs/plans/2026-09-26-plantilla-estandar-por-etapas.md`) y su tabla "Decisiones"; el plan histórico de la Etapa 1 (`docs/history/plans/2026-09-26-etapa-1-una-sola-forma-de-guardar.md`, su "Diseño" explica `ExecuteInTransactionAsync` y `CommitPolicy`); el mapa verificado de los 36 miembros de `IIdentityService` (contrato, consumidores, tests y dobles, persistencia y caché) y el diseño final aprobado. Todo se verificó contra `main` en `42a25c1`. Los números de línea que aparecen como "hoy :N" son de ese árbol y se corren con las tareas anteriores: manda el texto citado, no el número.

---

## Diseño

### 1. Los contratos

#### 1.1 `ISignInService`

Nuevo, en `src/ArquitecturaBase.Application/Interfaces/Integrations/ISignInService.cs`, plano como los otros 22 contratos de esa carpeta (la subcarpeta `Integrations/Identity/` que nombra el plan maestro queda para cuando la Etapa 3 ordene `Integrations` entera). Tiene 7 miembros, no los 10 del plan maestro: `FindByExternalLoginAsync` y `HasExternalLoginAsync` ya viven en `IUserReader`, y `AddExternalLoginAsync` en `IUserRepository`, y `ExternalLoginService` ya los usa por ahí.

Cada miembro sigue una de tres reglas:

| Regla | Miembros | Qué hace fuera o dentro de un límite |
|---|---|---|
| **Escribe** | `RegisterFailedAttemptAsync`, `ResetFailedAttemptsAsync`, `RevokeSessionsAsync` | exige la transacción del caso de uso; sin ella lanza `InvalidOperationException` antes de tocar nada (`RequireTransaction`) |
| **Después del commit** | `SignInAsync` | adentro de un límite lanza `InvalidOperationException` (`RequireNoTransaction`, desde la Tarea 27) |
| **En cualquier lado** | `IsLockedOutAsync`, `GetExternalLoginAsync`, `SignOutExternalAsync` | leen, o tocan solo la cookie externa de la petición |

Hasta la Tarea 27, `SignInAsync` está en "En cualquier lado" (el código y el enlace todavía escriben la cookie adentro). La forma final del contrato, la que deja la Tarea 27:

```csharp
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;

namespace ArquitecturaBase.Application.Interfaces.Integrations;

/// <summary>
/// Lo técnico del ingreso sobre ASP.NET Core Identity y OpenIddict: el bloqueo por intentos fallidos, la cookie de la
/// aplicación, la cookie del proveedor externo y el cierre de todas las sesiones de una cuenta. No tiene datos de
/// cuentas: se leen con <see cref="IUserReader"/> y se escriben con <see cref="IUserRepository"/>, incluido el vínculo con
/// el proveedor externo. Lo implementa Infrastructure: UserManager y SignInManager no salen de ahí.
/// </summary>
/// <remarks>
/// <para>Cada miembro sigue una de tres reglas, y UnitOfWorkTransactionTests exige que todo miembro nuevo declare la
/// suya:</para>
/// <list type="bullet">
/// <item><b>Escribe</b> (<see cref="RegisterFailedAttemptAsync"/>, <see cref="ResetFailedAttemptsAsync"/>,
/// <see cref="RevokeSessionsAsync"/>): exige la transacción del caso de uso
/// (<see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/>) y, sin ella, lanza
/// <see cref="InvalidOperationException"/> antes de tocar nada.</item>
/// <item><b>Después del commit</b> (<see cref="SignInAsync"/>): adentro de un límite lanza
/// <see cref="InvalidOperationException"/>.</item>
/// <item><b>En cualquier lado</b> (<see cref="IsLockedOutAsync"/>, <see cref="GetExternalLoginAsync"/>,
/// <see cref="SignOutExternalAsync"/>): leen, o tocan solo la cookie externa de la petición.</item>
/// </list>
/// <para>Los miembros que reciben un userId cargan la cuenta no borrada con ese Id, siempre en la base. Si no existe,
/// lanzan <see cref="InvalidOperationException"/>, porque quien llama ya la buscó. Si la cuenta ya está seguida en el
/// scope, usan esa misma instancia sin refrescarla: quien escribe después de un lock tiene que haberla leído después del
/// lock.</para>
/// <para>El bot de WhatsApp usa solo <see cref="IsLockedOutAsync"/>: un mensaje nunca abre una sesión. Lo fijan
/// IdentityBoundaryTests y la guarda de <see cref="SignInAsync"/>.</para>
/// </remarks>
public interface ISignInService
{
    /// <summary>Si Identity tiene bloqueada la cuenta por intentos fallidos, según su propio reloj. No escribe.</summary>
    Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Suma un intento fallido; al llegar al máximo (Authentication:LoginCode:LockoutMaxFailedAttempts), Identity bloquea
    /// la cuenta un tiempo. Solo lo suma el ingreso por código: confirmar un destino desde el perfil no es un ingreso.
    /// Exige la transacción, que el ingreso confirma con <see cref="CommitPolicy.OnAnyResult"/> aunque falle.
    /// </summary>
    Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Vuelve a cero los intentos fallidos. Exige la transacción: va adentro, antes del commit.</summary>
    Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Renueva el security stamp, con lo que la cookie de Identity deja de valer en la próxima petición (ValidationInterval
    /// en cero), y revoca primero las autorizaciones y después los tokens de OpenIddict de la cuenta. Exige la transacción:
    /// las revocaciones son UPDATE inmediatos y, sin ella, se confirmarían sueltas. Los enlaces pendientes los invalida
    /// <c>AccountAccessRevoker</c>, que es su único llamador.
    /// </summary>
    Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Escribe en la respuesta la cookie persistente de la aplicación. La llaman solo los puntos de entrada del ingreso
    /// (código, enlace y Google), después de <see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/> y solo con un
    /// Result exitoso. Adentro de un límite lanza <see cref="InvalidOperationException"/>. No escribe en la base.
    /// </summary>
    Task SignInAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// El resultado del proveedor externo, leído de la cookie externa, o null si no hay un ingreso externo en curso.
    /// </summary>
    Task<ExternalLogin?> GetExternalLoginAsync(CancellationToken cancellationToken);

    /// <summary>Borra la cookie externa, que sirve para un solo callback.</summary>
    Task SignOutExternalAsync(CancellationToken cancellationToken);
}
```

#### 1.2 `SignInService`

`src/ArquitecturaBase.Infrastructure/Identity/SignInService.cs`, `internal sealed`, scoped. Depende solo de `UserManager<ApplicationUser>`, `SignInManager<ApplicationUser>`, `IOpenIddictAuthorizationManager`, `IOpenIddictTokenManager` y `ApplicationDbContext` (para `RequireTransaction` y `RequireNoTransaction`). Ya no depende de `IUserReader`, `IRoleReader` ni `IUserRepository`. Los 7 cuerpos se mudan tal cual desde `IdentityService.cs` (hoy :123-191), con la cuenta cargada por `UserManagerExtensions.RequireUserAsync` (sección 4). `RevokeSessionsAsync` conserva el orden stamp → autorizaciones → tokens, con el subject `userId.ToString("D")`, el mismo `sub` que pone `OpenIdPrincipalFactory`.

#### 1.3 Los contratos de datos

Se quedan en `Application/Interfaces/Persistence`, con sus implementaciones en `Infrastructure/Persistence/{Readers,Repositories}`. No se agrega ningún método: todo lo de datos ya existe ahí. Cambia lo siguiente:

- **Tarea 16:** `IUserReader` e `IUserRepository` reciben los XML que hoy viven solo en `IIdentityService` (con los `cref` apuntando al contrato nuevo) y sus resúmenes dicen qué no hacen: el lector nunca devuelve la entidad de Identity y no exige transacción; el repositorio solo escribe datos, y cortar el acceso lo decide el caso de uso con `AccountAccessRevoker`.
- **Tareas 19 a 24:** nueve renombres, por la convención de nombres (sección 6). `IUserInvitationRepository.GetLatestAsync` y `GetLatestSentAsync` conservan el nombre y pasan a devolver la entidad seguida.
- `IOpenIddictTokenRevoker` (`ConnectService.RevokeAuthorizationAsync`) no se toca: corre fuera de todo límite a propósito (Etapa 1, §2.15), y fundirlo con `ISignInService` mezclaría dos semánticas de transacción en un contrato.

#### 1.4 Quién usa qué al final

| Consumidor | Recibe | Tarea |
|---|---|---|
| `AccountService` | `IUserReader` e `ISignInService` (la cookie, después del commit) | 9 y 26 |
| `LoginCodeVerifier` (con `EmailIdentifier` y `PhoneIdentifier`) | `IUserReader`, `IUserRepository` e `ISignInService` (bloqueo, intentos fallidos y reset) | 10 |
| `LoginLinkService` | `IUserReader` e `ISignInService` | 8 y 27 |
| `ExternalLoginService` | `ISignInService`, `IUserReader` e `IUserRepository` (solo cambia el tipo) | 7 |
| `AccountAccessRevoker` | `ISignInService` | 6 |
| `WhatsAppInboundService` | `IUserReader`, `IUserRepository` e `ISignInService`, este último solo por `IsLockedOutAsync` | 11 y 12 |

`UserPhoneOperations`, `UserStatusOperations` y `ConnectService` no cambian: los dos primeros ya usan `IUserReader`/`IUserRepository` y llegan al cierre de sesiones solo por `AccountAccessRevoker`, que el plan maestro no listaba. Los controllers no pueden inyectar `ISignInService`: `ControllerServiceRepositoryTests` ya les prohíbe `Interfaces.Integrations`.

### 2. Qué pasa con cada miembro de `IIdentityService`

Los 36 miembros, en el orden del contrato de hoy, más el `RequireUserAsync` privado. "Llamadores en src" es el estado de `42a25c1`; los de los tests se migran en las Tareas 13 a 15.

| # | Miembro | Destino | Llamadores en src | Tarea |
|---|---|---|---|---|
| 1 | `FindByIdAsync` | `IUserReader.FindByIdAsync` (reenvío idéntico) | `LoginLinkService` (:47, :112), bot (:195) | 8, 11, 12 |
| 2 | `FindByEmailAsync` | `IUserReader.FindByEmailAsync` | `AccountService` (:165), `EmailIdentifier` (:183) | 9, 10 |
| 3 | `FindByExternalLoginAsync` | `IUserReader.FindByExternalLoginAsync`; no va a `ISignInService` | ninguno (`ExternalLoginService` ya usa el lector) | 16 |
| 4 | `FindByPhoneAsync` | `IUserReader.FindByPhoneAsync` | `AccountService` (:206), `PhoneIdentifier` (:208), bot (:204 y la relectura de :216) | 9, 10, 11 |
| 5 | `CreateAsync` | `IUserRepository.CreateAsync`, con el XML de :33-41 | `EmailIdentifier` (:189), `PhoneIdentifier` (:214), bot (:260) | 10, 11, 16 |
| 6 | `CreateUnverifiedAsync` | `IUserRepository.CreateUnverifiedAsync`, con el XML de :50-59 | ninguno | 16 |
| 7 | `AddExternalLoginAsync` | `IUserRepository.AddExternalLoginAsync`; no va a `ISignInService` | ninguno (`ExternalLoginService` :108 ya usa el repositorio) | 16 |
| 8 | `HasExternalLoginAsync` | `IUserReader`, renombrado `ExistsExternalLoginAsync` | ninguno (`ProfileService` :63 y `UserGuards` :73 ya usan el lector) | 16, 19 |
| 9 | `SetPhoneAsync` | `IUserRepository.SetPhoneAsync`, con el XML de :72-80 | `PhoneIdentifier` (:219), bot (:244) | 10, 11, 16 |
| 10 | `RemovePhoneAsync` | `IUserRepository.RemovePhoneAsync`, con el XML de :83-86 | ninguno | 16 |
| 11 | `SetEmailAsync` | `IUserRepository.SetEmailAsync`, con el XML de :89-94 | `EmailIdentifier` (:194) | 10, 16 |
| 12 | `GetRolesAsync` | se borra sin reemplazo en src | ninguno | 16 |
| 13 | `FindDeletedByEmailAsync` | `IUserReader.FindDeletedByEmailAsync` | ninguno | 16 |
| 14 | `FindDeletedByPhoneAsync` | `IUserReader.FindDeletedByPhoneAsync`, con el XML de :102-106 | bot (:166) | 11, 16 |
| 15 | `RestoreAsync` | `IUserRepository.RestoreAsync`, con el XML de :109 | ninguno | 16 |
| 16 | `SetRolesAsync` | `IUserRepository.SetRolesAsync`, con el XML de :112 | ninguno | 16 |
| 17 | `ListRoleNamesAsync` | `IRoleReader.ListRoleNamesAsync` (pendiente c) | ninguno | 16 |
| 18 | `FindDetailAsync` | `IUserReader.FindDetailAsync` | ninguno | 16 |
| 19 | `SetDisplayNameAsync` | `IUserRepository.SetDisplayNameAsync` | ninguno | 16 |
| 20 | `SetActiveAsync` | `IUserRepository.SetActiveAsync` | ninguno | 16 |
| 21 | `RevokeSessionsAsync` | `ISignInService` (escribe; conserva `RequireTransaction`) | `AccountAccessRevoker` (:36) | 5, 6 |
| 22 | `DeleteAsync` | `IUserRepository.DeleteAsync`, con el XML de :134 | ninguno | 16 |
| 23 | `ListRolesAsync` | `IRoleReader.ListRolesAsync` (pendiente c) | ninguno | 16 |
| 24 | `FindRoleAsync` | `IRoleReader.FindRoleAsync` (pendiente c); el renombre a `FindByIdAsync` es de la Etapa 4 | ninguno | 16 |
| 25 | `RoleNameExistsAsync` | `IRoleReader`, renombrado `ExistsByNameAsync` | ninguno (`RoleService` :137 y :172 ya usan el lector) | 16, 20 |
| 26 | `IsLockedOutAsync` | `ISignInService` (en cualquier lado) | `LoginCodeVerifier` (:41), `LoginLinkService` (:123), `ExternalLoginService` (:118), bot (:159) | 5, 7, 8, 10, 11 |
| 27 | `RegisterFailedAttemptAsync` | `ISignInService` (escribe) | `LoginCodeVerifier` (:58) | 5, 10 |
| 28 | `ResetFailedAttemptsAsync` | `ISignInService` (escribe) | `LoginCodeVerifier` (:86), `LoginLinkService` (:133) | 5, 8, 10 |
| 29 | `SignInAsync` | `ISignInService` (después del commit desde la Tarea 27) | `ExternalLoginService` (:49, después), `LoginCodeVerifier` (:87, adentro), `LoginLinkService` (:137, adentro) | 5, 7, 8, 10, 26, 27 |
| 30 | `GetExternalLoginAsync` | `ISignInService` (en cualquier lado) | `ExternalLoginService` (:60) | 5, 7 |
| 31 | `SignOutExternalAsync` | `ISignInService` (en cualquier lado) | `ExternalLoginService` (:67) | 5, 7 |
| 32 | `ListUsersAsync` | `IUserReader.ListUsersAsync` | ninguno | 16 |
| 33 | `GetUserFilterCountsAsync` | `IUserReader`, renombrado `CountByFilterOptionAsync` (el de `IUserService` no cambia) | ninguno | 16, 19 |
| 34 | `CountActiveAdminsAsync` | `IUserReader.CountActiveAdminsAsync` | ninguno | 16 |
| 35 | `IsDeletedEmailAsync` | `IUserReader`, renombrado `ExistsDeletedByEmailAsync`, con el XML de :177-181 | `EmailIdentifier` (:186) | 10, 16, 19 |
| 36 | `IsDeletedPhoneAsync` | `IUserReader`, renombrado `ExistsDeletedByPhoneAsync`, con el XML de :184-188 | `PhoneIdentifier` (:211) | 10, 16, 19 |
| — | `RequireUserAsync` (privado de `IdentityService` y de `UserRepository`) | `Infrastructure/Identity/UserManagerExtensions.RequireUserAsync`, la única carga por Id de una cuenta no borrada para modificarla | `IdentityService` (6 sitios), `UserRepository` (9 sitios) | 4 |

De los 36, 16 tienen llamadores en src y 20 son reenvíos que solo usan los tests. Las cuatro lecturas de roles (17, 23, 24 y 25) no tienen llamadores en src ni en integración: en los unitarios las usa solo `UserServiceTestHost.FakeRoleReader`, que pasa a tener datos propios (Tarea 16).

### 3. La cookie después del commit (pendiente a)

**Hoy (verificado):** Google escribe la cookie después del commit y solo con éxito (`ExternalLoginService.cs:46-49`). El código (`LoginCodeVerifier.cs:87`, adentro del límite `OnAnyResult` de `AccountService.cs:128-133`) y el enlace (`LoginLinkService.cs:137`, adentro del límite de :75-80) la escriben antes del commit; si el commit falla, `UseExceptionHandler` limpia el `Set-Cookie` (lo fija `LoginLinkTests.A_failed_commit_answers_500_without_the_cookie_and_keeps_the_link`). Está documentado como excepción en la regla del patrón transaccional, en la regla 4 ("la cookie de Google") y en el resumen de `IUnitOfWork`.

**Decisión:** las tres cookies salen después del commit, desde el punto de entrada y solo con éxito, y `SignInAsync` lanza si hay una transacción abierta. Son dos commits `fix` al final de la etapa, después de todos los refactors sin cambio de comportamiento, así cada uno se revierte solo:

- **Tarea 26, el código:** `LoginCodeVerifier.VerifyAsync` pasa a `Task<Result<Guid>>`, devuelve `user.Id` y deja de llamar a `SignInAsync`; `ResetFailedAttemptsAsync` y la auditoría de éxito siguen adentro. `AccountService` recibe `ISignInService` y escribe la cookie entre el límite y el log `Handled`. `IAccountService`, `VerifyLoginCodeResponse`, el controller y el contrato HTTP no cambian: el verifier ya copiaba `request.ReturnUrl` sin tocarlo.
- **Tarea 27, el enlace y la guarda:** `RedeemCoreAsync` pasa a `Task<Result<Guid>>`; `RedeemAsync` escribe la cookie después del límite y sigue devolviendo `Task<Result>`, así `ILoginLinkService` y el controller no se tocan. `SignInService.SignInAsync` suma `dbContext.RequireNoTransaction()` antes de tocar `SignInManager`. De paso protege la regla de oro de WhatsApp en tiempo de ejecución: el bot corre siempre adentro de un límite.

**Comportamiento observable:**

1. Camino feliz: la misma respuesta, el mismo `Set-Cookie` (sale del mismo `HttpContext` antes de que responda el controller), el mismo status y la misma auditoría. El lock se suelta antes, porque la lectura de roles y del stamp del claims factory queda afuera.
2. Error de negocio: 4xx sin cookie. Se confirma lo mismo que hoy: código o enlace gastado, intento contado y auditoría.
3. Commit fallido: 500 sin cookie, con el código o el enlace sin gastar y sin auditoría. Ya no depende de que `UseExceptionHandler` limpie el encabezado.
4. Lo único nuevo es la ventana que Google ya tiene: si `SignInAsync` lanza después del commit (la base se cae al leer el stamp o los roles, o se borra la cuenta en ese instante), el código o el enlace quedan gastados, los intentos en cero, la `LoginAudit` dice Success y la respuesta es un 500 sin cookie. La persona pide otro.
5. Una revocación que entra entre el commit y la cookie hace que la cookie salga con el stamp viejo, y el validador la rechaza en el pedido siguiente (`ValidationInterval = 0`, `IdentityRegistration.cs:82`). `ConnectService` vuelve a mirar `IsActive` antes de emitir tokens. Es igual que con Google.
6. Después del commit, `RequireUserAsync` va a la base y recibe la instancia que siguió el alta o el reset: `UnitOfWork` vacía el tracker solo al deshacer.

### 4. Una sola carga de la cuenta (tarea 4 del plan maestro)

Hoy hay dos: `IdentityService.RequireUserAsync` (`UserManager.FindByIdAsync`, que devuelve lo que ya sigue el contexto sin ir a la base y no recibe el token, más un filtro manual de `IsDeleted`) y `UserRepository.RequireUserAsync` (`userManager.Users` con el filtro global y el token). Queda una sola, en `Infrastructure/Identity/UserManagerExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Identity;

internal static class UserManagerExtensions
{
    /// <summary>
    /// La única forma de cargar por Id una cuenta no borrada para modificarla con UserManager, seguida por EF. Va
    /// siempre a la base con el filtro global de borrados y el token de quien llama. Si EF ya sigue la fila en este
    /// scope, devuelve esa misma instancia sin refrescarla (identity resolution), con su ConcurrencyStamp: por eso se
    /// lee después de tomar los locks. El chequeo de IsDeleted en memoria cubre una instancia seguida que se marcó
    /// borrada y no llegó a guardarse. Sin cuenta, lanza: quien llama ya la buscó.
    /// </summary>
    /// <remarks>
    /// Dos cargas quedan afuera a propósito, porque buscan otra cosa: UserRepository.RestoreAsync carga la cuenta
    /// borrada (IgnoreQueryFilters sobre el filtro de borrados) y RoleSeeder busca al administrador inicial por su
    /// correo (FindByEmailAsync) para darle el rol Admin.
    /// </remarks>
    public static async Task<ApplicationUser> RequireUserAsync(
        this UserManager<ApplicationUser> userManager, Guid userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userManager);

        return await userManager.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken)
            is { IsDeleted: false } user
            ? user
            : throw new InvalidOperationException("The user does not exist.");
    }
}
```

La usan `UserRepository` y `SignInService` (y `IdentityService` mientras existe). Cuesta un viaje a la base por llamada: dos o tres por ingreso (`IsLockedOut`, `Reset` y `SignIn`). Se mantiene la regla de leer la cuenta después de tomar los locks: la instancia seguida se reutiliza en las escrituras y trae el `ConcurrencyStamp`. `IdentityBoundaryTests.Accounts_are_loaded_with_one_query` prohíbe volver a llamar a `UserManager.FindByIdAsync` en src. "La única" vale para la carga por Id de una cuenta no borrada: `UserRepository.RestoreAsync` sigue cargando la borrada con su propia consulta (`IgnoreQueryFilters`, porque `RequireUserAsync` la excluye) y `RoleSeeder` busca al administrador inicial con `FindByEmailAsync`. Las dos quedan nombradas en el `<remarks>` y ninguna llama a `FindByIdAsync`.

Al lado de `RequireTransaction`, en `Infrastructure/Persistence/Extensions/TransactionExtensions.cs` (Tarea 27):

```csharp
    /// <summary>
    /// Lanza si hay una transacción abierta. Lo que corre después del commit del caso de uso (la cookie de la aplicación)
    /// no puede correr adentro de IUnitOfWork.ExecuteInTransactionAsync: si el commit fallara, quedaría hecho algo que
    /// depende de datos que no se guardaron. Es un bug de quien llama, no una regla de negocio.
    /// </summary>
    public static void RequireNoTransaction(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        if (dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "This operation runs after the use case commits: call it outside IUnitOfWork.ExecuteInTransactionAsync.");
        }
    }
```

`UnitOfWork` confirma y descarta la transacción en su `finally`, así que `CurrentTransaction` vuelve a null después del límite. Leer `CurrentTransaction` no está en `TransactionApiMethods` de `TransactionBoundaryTests`: `RequireTransaction` ya lo lee.

### 5. La caché de permisos (pendiente b)

`PermissionService.cs:49-55` le pasa a la fábrica de `HybridCache` el `IPermissionReader` scoped de quien llama. Su remark (:8-19) lo admite y deja escrita la restricción: nunca se llama adentro de un límite. Hoy se cumple (la autorización y la consulta del perfil), así que es una trampa latente y no un bug activo. `SystemSettingsReader` ya lee en su propio scope desde la Etapa 1.

**Decisión:** una sola forma de llenar el caché, con un helper y su regla (la Etapa 7, tarea 7, unifica los registros, no las fábricas). Nuevo `src/ArquitecturaBase.Infrastructure/Caching/HybridCacheExtensions.cs` con `GetOrCreateInOwnScopeAsync` (código completo en la Tarea 3): la fábrica abre un scope, resuelve el lector y lee. `PermissionService` pasa a `(IPermissionReader reader, IServiceScopeFactory scopes, HybridCache cache)`: los roles de la cuenta siguen con el lector de quien llama (no se cachean), los permisos de cada rol van por el helper. `SystemSettingsReader` pasa al mismo helper, con `ApplicationDbContext` como lector. `TransactionBoundaryTests.Cache_factories_read_in_their_own_scope` exige que el único dueño de las llamadas a `HybridCache.GetOrCreateAsync` sea `HybridCacheExtensions`. Deja sin efecto la decisión 14 de la Etapa 1 ("`PermissionService` no se toca"). Costo: con el caché frío, una conexión más del pool por rol, en secuencia, con una hora de TTL.

**Test primero, en rojo con el código de hoy** (`PermissionServiceTests.The_cache_factory_reads_on_its_own_connection_and_never_caches_uncommitted_permissions`, espejo del de `SystemSettingsReaderTests`): adentro de un límite se le suma un permiso a un rol con el caché frío, se leen los permisos de un usuario de ese rol y se deshace. Hoy la lectura ve el permiso sin confirmar y lo deja una hora en el caché. No hay deadlock posible: la fábrica lee con otra conexión en READ COMMITTED, y en Postgres los locks de fila del UPDATE no bloquean lecturas.

**Pendiente d, antes y sobre el mismo archivo (Tarea 2):** `PermissionServiceTests` deja de armar datos con `UserManager`/`RoleManager` en autocommit y usa `IUserRepository`/`IRoleRepository` adentro de `factory.InTransactionAsync`. El claim `"unrelated"` no se puede escribir por `IRoleRepository`: va con `ApplicationDbContext.RoleClaims.Add` adentro del límite. Así también se corrige el `UserName = email` de su `CreateUserAsync`. Después de eso, ningún test resuelve `UserManager`, `RoleManager` ni `SignInManager`.

### 6. La convención de nombres de repositorios y lectores

Cubre todo método de las interfaces de `Application/Interfaces/Persistence` salvo `IUnitOfWork`, y se decide por el tipo de retorno, que es lo que un test puede ver. Va en el spec de arquitectura (sección nueva "Nombres de repositorios y lectores") y en el ADR 0008 (Tarea 18).

| Prefijo | Devuelve | Dónde |
|---|---|---|
| `Get…` | `Task<TEntidad?>`: una entidad de Domain (hereda de `Domain.Common.Entity`), siempre seguida por EF; a veces con lock de fila, y entonces el XML lo dice; `null` si no existe | solo en `*Repository` |
| `Find…` | `Task<T?>`: una proyección, un registro o un escalar, nunca una entidad; `null` si no existe, o un valor por defecto documentado (`FindRegistrationModeAsync`) | lectores y repositorios |
| `List…` | una colección (no `string`) o `PagedResult<T>` | los dos |
| `Exists…` | `Task<bool>` | los dos |
| `Count…` | `Task<int>`, o un registro cuyo nombre termina en `Counts` | los dos |
| `Lock…` | `Task` no genérico: toma un lock de Postgres y exige la transacción | solo repositorios |
| `Add` | `void`: alta en el contexto, que baja el commit | solo repositorios |
| `Create…`, `Update…`, `Delete…`, `Set…`, `Remove…`, `Restore…`, `Clear…`, `Add…Async` | escrituras; exigen la transacción | solo repositorios |
| `Invalidate…` | descarta un caché: excepción temporal, solo en `ISystemSettingsReader`, hasta la tarea 7 de la Etapa 7 | lectores con caché |

Corolarios: un lector (`I*Reader`) solo tiene `Find`, `List`, `Exists` y `Count` (más la excepción de `Invalidate`), y es el único que llama a `AsNoTracking`; una entidad que devuelve un repositorio está siempre seguida. Quedan afuera `IUnitOfWork` y todo `Interfaces/Integrations` (`ISignInService.GetExternalLoginAsync` e `IPermissionService.GetPermissionsAsync` son operaciones técnicas). `IWhatsAppWebhookReader` es un parser del webhook y no un lector de base. Excepción conocida: `IUserReader.CountActiveAdminsAsync` trae y deja seguidos a todos los Admin (`GetUsersInRoleAsync`) y la regla del IL no lo ve (decisión 9).

**Renombres (Tareas 19 a 23), mecánicos, sin cambio de HTTP:**

| Contrato | Antes | Después | Tarea |
|---|---|---|---|
| `IUserReader` | `IsDeletedEmailAsync` | `ExistsDeletedByEmailAsync` | 19 |
| `IUserReader` | `IsDeletedPhoneAsync` | `ExistsDeletedByPhoneAsync` | 19 |
| `IUserReader` | `HasExternalLoginAsync` | `ExistsExternalLoginAsync` | 19 |
| `IUserReader` | `GetUserFilterCountsAsync` | `CountByFilterOptionAsync` | 19 |
| `IRoleReader` | `RoleNameExistsAsync` | `ExistsByNameAsync` | 20 |
| `IPermissionReader` | `GetUserRoleIdsAsync` | `ListRoleIdsForUserAsync` | 21 |
| `IPermissionReader` | `GetRolePermissionsAsync` | `ListPermissionsForRoleAsync` | 21 |
| `ILoginAuditRepository` | `GetLastSuccessAtUtcAsync` | `FindLastSuccessAtUtcAsync` (y sale su `AsNoTracking`, que sobre un escalar no hacía nada) | 22 |
| `ISystemSettingsReader` | `GetRegistrationModeAsync` | `FindRegistrationModeAsync` (nunca devuelve null: sin fila, `InviteOnly`) | 23 |

**Ajustes sin renombre (Tarea 24):** sale el `AsNoTracking` de `LoginLinkRepository.FindUserIdAsync` (proyecta un `Guid`: no seguía nada) y el de `UserInvitationRepository.GetLatestAsync` y `GetLatestSentAsync`, que devuelven la entidad `UserInvitation` (su regla `WaitBeforeAnother` es de dominio): se quedan con `Get` y pasan a seguidas. Nadie modifica esas instancias y `SaveChanges` no encuentra cambios; `WhatsAppDeliveryService` carga las invitaciones por `GetByIdAsync` en su propio scope. Fuera de esta etapa: `FindRoleAsync` → `FindByIdAsync` (Etapa 4) y `CountActiveAdminsAsync` en SQL (decisión 9).

**Reglas** (`tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs`, Tarea 18): `Persistence_methods_start_with_a_known_verb`, `Reads_return_what_their_prefix_promises`, `Readers_only_read` (por reflexión) y `Only_readers_skip_tracking` (con `CallSites`: las llamadas a `EntityFrameworkQueryableExtensions.AsNoTracking` y `AsNoTrackingWithIdentityResolution` salen solo de `Infrastructure.Persistence.Readers`). Nacen como trinquete, con `KnownViolations` (los 9 nombres) y `KnownUntrackedOwners` (tres repositorios), afirmados en los dos sentidos; cada tarea de renombre saca sus entradas y la Tarea 24 borra las listas.

### 7. Tests y dobles

**Dobles unitarios, en dos fases** (así migrar los consumidores no toca ninguna aserción):

- **Transición (Tarea 5):** `FakeIdentityService` suma `ISignInService` a sus interfaces. No lleva código nuevo: ya tiene los 7 métodos con la misma firma. En cada commit de consumidor los tests siguen pasando el mismo objeto; solo crece la cantidad de argumentos de algunos constructores.
- **Final (Tarea 17, después de borrar `IIdentityService`):**
  - `TestDoubles/Auth/FakeSignInService.cs : ISignInService`, con `SignedInUsers`, `LockedOutUsers`, `FailedAttempts`, `RevokedUsers`, `PendingExternalLogin` y `ExternalSignedOut`, sacados tal cual de `FakeIdentityService` (su estado va por userId, así que se separa limpio). Tiene su propio `Func<bool>? InTransaction`: `RegisterFailedAttempt`, `ResetFailedAttempts` y `RevokeSessions` llaman a `TransactionGuard.Require`. El constructor opcional `(List<string>? events)` anota `"sign-in"` en la misma lista en la que `FakeUnitOfWork(events)` anota `"commit"`. Desde la Tarea 26, `SignInAsync` llama a `TransactionGuard.RequireNone` (nuevo).
  - `TestDoubles/Users/InMemoryUserAccounts.cs : IUserReader, IUserRepository`: es `FakeIdentityService` sin la sesión, sin `IIdentityService`, sin `GetRolesAsync` y sin las lecturas de roles. Conserva `AddUser`, `SetRoles`, `LinkExternalLogin`, `ArrangeAsync` (que apaga solo su guarda), `DeletedUsers`, `DeletedEmails`, `LastListRequest` y `DefaultTimeZoneId`.
  - Los fixtures que reciben un `FakeSignInService` conectan las dos guardas (`ExternalLoginServiceTests`, `VerifyLoginCodeServiceTests`, `UserServiceTestHost` y `WhatsAppInboundServiceTests`); `ProfileEmailServiceTests` y `ProfileServiceTests` conectan solo la de cuentas, porque `ProfileService` no recibe `ISignInService`. Los nombres de los campos y propiedades (`_identity`, `Identity`, `_users`) no se cambian: el tipo dice lo que es, y renombrarlos tocaría unas 140 líneas sin cambiar nada.
  - `ProfileEmailServiceTests.cs` (hoy :116-117, :152 y :171-172) pierde cinco aserciones que no podían fallar: afirman `Empty(RevokedUsers)` y `Empty(SignedInUsers)` sobre un doble que `ProfileService` no recibe como `ISignInService`. Las reemplaza la regla de IL `Only_the_sign_in_code_counts_failed_attempts`; "desvincular desde el perfil no cierra sesiones" sigue cubierto en `MeWhatsAppEndpointsTests`.

**`IdentityServiceTests` se reparte y desaparece:**

| Test de hoy | Va a | Tarea |
|---|---|---|
| `Tenth_failed_attempt_locks_the_account` | `Identity/SignInServiceTests` | 5 |
| `Revoking_sessions_outside_a_transaction_throws_before_touching_the_stamp` | `Identity/SignInServiceTests` | 5 |
| (nuevo) `Resetting_failed_attempts_brings_the_count_back_to_zero` | `Identity/SignInServiceTests` | 5 |
| (nuevo) `Operations_reject_a_deleted_or_missing_account` | `Identity/SignInServiceTests` | 5 |
| (nuevo) `Signing_in_inside_a_boundary_throws_before_touching_the_response` | `Identity/SignInServiceTests` | 27 |
| `New_users_are_confirmed_and_get_the_user_role`, `Admin_email_gets_the_admin_role`, `Phone_only_accounts_can_be_created`, `The_user_name_is_the_account_id_and_not_the_email_or_the_phone`, `A_phone_loaded_without_verifying_it_stays_unconfirmed`, `An_account_needs_an_email_or_a_phone`, `Two_accounts_cannot_share_a_phone_number`, `A_deleted_account_keeps_its_phone_number_reserved`, `Linking_a_phone_or_an_email_writes_them_without_closing_the_session` | `Persistence/UserRepositoryTests` (roles por `ListRoleNamesForUserAsync`) | 15 |
| `Users_are_found_by_email_and_by_external_login`, `Search_treats_like_wildcards_as_literals`, `Users_are_sorted_by_the_requested_field`, `A_deleted_account_is_found_by_its_phone_with_its_culture`, `Users_are_found_by_phone`, `Deleted_email_lookup_uses_identity_normalization_and_preserves_the_account`, `User_detail_sorts_roles_and_excludes_deleted_accounts`, `Admin_count_includes_only_active_and_not_deleted_accounts`, `External_logins_are_reported_by_provider` | `Persistence/UserReaderTests` | 15 |
| `Role_names_are_sorted_and_deleted_users_are_rejected` | se borra: el orden lo cubre `User_detail_sorts_roles_and_excludes_deleted_accounts`, y el rechazo, `SignInServiceTests.Operations_reject_a_deleted_or_missing_account` | 15 |

**Helpers de integración:** `Support/UserRepositoryExtensions.cs` reemplaza a `IdentityServiceExtensions`, con la misma forma corta (`CreateAsync(this IUserRepository, Email, string?, string, CancellationToken)`; con 4 argumentos solo aplica la extensión). `AdminUsersApi` pasa a `IUserReader` e `IUserRepository`. `StaleIdentityReads` pierde el envoltorio de `IIdentityService` (hoy :69-73) y el `using ArquitecturaBase.Infrastructure.Identity`; el de `IUserReader` y `HiddenLookupNames` quedan tal cual (sostienen los 409 de `CreateUserWithPhoneTests`, `MeEmailEndpointsTests` y `MeWhatsAppEndpointsTests`). `ApiFactory` (Tarea 1) implementa directo el `InTransactionAsync` sin valor y suma sobrecargas sin valor de `ExecuteScopeAsync` y `ExecuteDbContextAsync`.

**Los `return` sobrantes (pendiente e, Tarea 1):** 26 lambdas de `factory.InTransactionAsync` cuyo resultado se descarta (13 `return true;` y 13 `return 0;`): `LoginLinkTests` 3, `Support/AdminUsersApi` 1, `MeEmailEndpointsTests` 2, `MeWhatsAppEndpointsTests` 7, `UnlinkUserPhoneEndpointTests` 2, `UpdateUserContactTests` 1, `UsersEndpointsTests` 9 y `UserSoftDeleteTests` 1. Además, los de `ExecuteScopeAsync`/`ExecuteDbContextAsync` de `RoleRepositoryTransactionTests` (1) y `SystemSettingsReaderTests` (2) pasan a las sobrecargas nuevas, y los tres de `PermissionServiceTests` desaparecen con su reescritura (Tarea 2). Puerta: `git grep -nE "return (true|0);" tests/ArquitecturaBase.Api.IntegrationTests` solo puede mostrar `CapturingWhatsAppOutbox.cs` (un valor con sentido) y, hasta la Tarea 15, `IdentityServiceTests.cs`.

**Escrituras fuera del límite con cobertura automática (pendiente f, Tarea 5):** `UnitOfWorkTransactionTests.Sign_in_service_follows_its_transaction_rules` clasifica cada miembro de `ISignInService` en `writes`, `afterCommit` o `anywhere` y afirma, por reflexión, que la unión es el contrato entero: un miembro nuevo sin clasificar rompe el test. Las escrituras tienen que lanzar fuera de un límite sin tocar la cuenta, el stamp incluido. `Identity_service_writes_outside_the_boundary_throw_and_change_nothing` se borra junto con `IIdentityService` (Tarea 16): sus 11 reenvíos ya los cubre `Account_writes_outside_the_boundary_throw_and_change_nothing`, que tiene completitud por reflexión, y sus 3 escrituras técnicas, el test nuevo.

### 8. Reglas de arquitectura

1. **`IdentityBoundaryTests`** (nuevo, `tests/ArquitecturaBase.ArchitectureTests/`), con `CallSites` y `TypeUses` sobre Application, Infrastructure y Api, igual que `TransactionBoundaryTests`. El dueño de una llamada es el tipo de nivel superior, así que las clases anidadas (`EmailIdentifier`) cuentan como `LoginCodeVerifier`. Sus reglas entran en tres momentos:
   - Tarea 4: `Accounts_are_loaded_with_one_query`: ninguna llamada a `UserManager<T>.FindByIdAsync` en src. No se extiende a `FindByEmailAsync`, porque `RoleSeeder` la usa en el seed.
   - Tarea 5: `Sign_in_contract_stays_small_and_technical` (como máximo 12 miembros, la alarma del plan maestro; ninguno empieza con `Find`, `List`, `Exists`, `Count`, `Create`, `Add`, `Set`, `Remove`, `Restore`, `Delete` ni `Update`, y ninguno devuelve `UserAccount` ni `UserDetail`) y `Only_the_sign_in_service_touches_sessions` (el único dueño de los usos de `SignInManager<T>` y de las llamadas a `UserManager<T>.{IsLockedOutAsync, AccessFailedAsync, ResetAccessFailedCountAsync, UpdateSecurityStampAsync}` y a `RevokeBySubjectAsync` de los managers de OpenIddict es `SignInService`).
   - Tarea 16 (antes no se puede: mientras `IdentityService` reenvía `SignInAsync` y `RegisterFailedAttemptAsync`, él mismo es un llamador en Infrastructure): `Only_entry_points_open_a_session` (los dueños de las llamadas a `ISignInService.SignInAsync` son exactamente `ExternalLoginService`, `LoginCodeVerifier` y `LoginLinkService`, y ninguno vive en `Application.Services.WhatsApp`) y `Only_the_sign_in_code_counts_failed_attempts` (el único dueño de `RegisterFailedAttemptAsync` es `LoginCodeVerifier`). En la Tarea 26 el conjunto pasa a `AccountService`, `ExternalLoginService` y `LoginLinkService`; en la 27 se suma que todos sean puntos de entrada. Que la llamada vaya después del commit lo fijan `RequireNoTransaction` en tiempo de ejecución y los unitarios por eventos, porque el IL no distingue una lambda del método que la contiene.
2. **`PersistenceNamingTests`** (nuevo, Tarea 18): sección 6.
3. **`TransactionBoundaryTests`** suma `Cache_factories_read_in_their_own_scope` (Tarea 3). Sus reglas actuales no cambian: `SignInService` no recibe `IUnitOfWork` y `RequireNoTransaction` solo lee `CurrentTransaction`. En la Tarea 27, `IsUseCaseEntryPoint` se muda a `Support/UseCaseEntryPoints.cs` para compartirlo con `IdentityBoundaryTests`.
4. **Lo que el plan maestro pedía y ya se cumple, y no se duplica:** "ningún tipo de `Application.Services` depende de `Microsoft.AspNetCore.Identity`" ya lo cubren `LayerDependencyTests.Application_does_not_depend_on_infrastructure_api_or_persistence` (sin `Microsoft.AspNetCore`), `ApplicationPackagesTests` y las referencias del csproj; que un controller no inyecte `ISignInService` ya lo cubre `ControllerServiceRepositoryTests.Controllers_do_not_access_persistence_integrations_or_handlers_directly`. El plan maestro se corrige en el cierre para decirlo.
5. No se agrega ninguna regla sobre los ensamblados de tests, que ArchitectureTests no escanea a propósito. Alcanza la puerta `git grep -nE "UserManager<|RoleManager<|SignInManager<" -- tests`, vacía desde la Tarea 2.
6. **Puerta de la etapa:** la general, las reglas nuevas en verde y la búsqueda de `IIdentityService`, `IdentityService`, `FakeIdentityService`, `IdentityServiceTests` e `IdentityServiceExtensions` vacía en `src`, `tests`, `AGENTS.md`, `CLAUDE.md`, `README.md`, `docs/architecture`, `docs/features` y los specs funcionales de `docs/specs`, que `AGENTS.md` declara vigentes en lo funcional (el de WhatsApp nombra a quien crea la cookie: se corrige en la Tarea 16). Quedan afuera a propósito `docs/history`, el diseño inicial (`docs/specs/2026-09-18-arquitectura-base-design.md`, histórico para la estructura de capas, que es justo donde nombra `IIdentityService`), `docs/plans` (el plan maestro lo narra) y `docs/decisions` (un ADR aceptado no se reescribe, y el "Origen" del 0001 describe la Etapa 1 con fecha). Tal como está escrita en el plan maestro, "`git grep IIdentityService` vacío" no se puede cumplir.

### 9. Los documentos, por su rol

La Etapa 5 reorganizó la documentación mientras se diseñaba esta etapa, y puede seguir moviéndola. Este plan nombra cada documento por su rol; la ruta de la tabla es la vigente en `42a25c1`. **Antes de editar un documento:** `git ls-files <ruta>` tiene que listarlo y `git status --short -- <ruta>` tiene que salir vacío. Si la ruta ya no existe, se busca el documento de ese rol (`git grep -l "<título>"`) y se edita ahí. Si tiene cambios ajenos sin commitear, no se suma al commit: la edición se anota en la lista "Ediciones de documentos pendientes" del final de este plan y se aplica cuando la Etapa 5 lo haya commiteado; la puerta de la Tarea 28 no cierra con ediciones pendientes.

| Rol | Ruta en `42a25c1` | Qué cambia y en qué tarea |
|---|---|---|
| Índice de reglas para agentes | `AGENTS.md` | la convención de nombres en "Persistencia" (18); "las dos excepciones" → "la excepción" (27) |
| Spec de arquitectura | `docs/architecture/backend.md` | la regla de lectores con caché (3); lo que verifica ArchitectureTests (3, 4, 5, 16, 18, 26, 27); la capa Infrastructure y las escrituras que exigen la transacción (16); la guía de dobles (17, 26); la sección nueva de nombres (18, 24); la excepción de la cookie y la regla 4 (26, 27) |
| Reglas de identidad | `docs/features/identidad.md` | la regla de los contratos de Identity (16); la cookie después del commit (26, 27) |
| Reglas de WhatsApp | `docs/features/whatsapp.md` | la regla de oro, con la regla de IL (16) y la guarda en tiempo de ejecución (27) |
| Spec funcional del ingreso por WhatsApp | `docs/specs/2026-09-22-ingreso-whatsapp-design.md` | quién crea la cookie de sesión, en la sección 5 ("La regla de oro") (16) |
| Índice de ADR | `docs/decisions/README.md` | la fila del 0008 (18) |
| ADR de la convención de nombres | `docs/decisions/0008-nombres-de-repositorios-y-lectores.md` | nuevo (18); el enlace al plan, en el cierre (29) |
| Plan maestro | `docs/plans/2026-09-26-plantilla-estandar-por-etapas.md` | el cierre de la Etapa 2 (29) |

### 10. Decisiones que el usuario tiene que confirmar antes de ejecutar

La Tarea 0 (Paso 0) frena hasta tener la confirmación explícita de las doce. Cada una lleva su recomendación; si se rechaza o cambia alguna, se ajustan las tareas indicadas antes de ejecutar nada.

1. **La cookie del ingreso por código y la del canje del enlace salen después del commit, como la de Google,** y `SignInService.SignInAsync` lanza adentro de un límite. Cambia lo observable en un solo caso: si la cookie falla después de confirmar, el código o el enlace quedan gastados, con auditoría de éxito, y la respuesta es un 500. *Recomendación: sí, en dos commits al final (Tareas 26 y 27), con los tests en rojo primero. Queda una sola regla para los tres ingresos, la verifica toda la suite y protege la regla de oro de WhatsApp también en tiempo de ejecución.*
2. **`ISignInService` queda con 7 miembros**, no con los 10 del plan maestro: `FindByExternalLogin` y `HasExternalLogin` (que pasa a `ExistsExternalLogin`) se quedan en `IUserReader`, y `AddExternalLogin` en `IUserRepository`. *Sí, y corregir el plan maestro, que además tiene vieja la lista de consumidores: le falta `AccountAccessRevoker` y le sobran `UserPhoneOperations` y `UserStatusOperations`.* (Tareas 5 y 29.)
3. **El bot recibe `ISignInService` entero**, aunque usa solo `IsLockedOutAsync`. Lo protegen la regla de IL, `RequireNoTransaction` y `WhatsAppInboundServiceTests`. La alternativa es un contrato aparte, solo de lectura, para el bloqueo. *Un solo contrato: no es una regresión (hoy lo tiene por `IIdentityService`) y es lo que menos toca.* (Tareas 11, 16 y 27.)
4. **Ubicación:** `Application/Interfaces/Integrations/ISignInService.cs`, plano como los otros 22 contratos, en lugar de la subcarpeta `Integrations/Identity/` del plan maestro. *Plano; se muda con la tarea 9 de la Etapa 3.* (Tarea 5.)
5. **Una sola consulta de la cuenta seguida** (`userManager.Users` con el filtro global, más el chequeo de `IsDeleted` en memoria) para `UserRepository` y `SignInService`, a cambio de dos o tres consultas más por ingreso, y una regla que prohíbe `UserManager.FindByIdAsync` en src. *Sí, en su propio commit antes de crear `ISignInService`.* (Tarea 4.)
6. **Hallazgo del bot:** `WhatsAppInboundService.cs:194-201` lee la cuenta vinculada antes de tomar su lock y no la vuelve a leer. Si un administrador la desactiva mientras el bot espera (`UserStatusOperations.SetActiveAsync(false)` toma `login-link:`), el bot emite un enlace después del corte, y ese enlace sirve si la reactivan dentro de los 10 minutos. Si la borra, no hay fuga: `IsLockedOutAsync` lanza con la cuenta borrada, la unidad del bot se deshace y la vuelta siguiente contesta Disabled; la relectura solo lo resuelve en el acto (robustez, no seguridad). *Arreglarlo en esta etapa, con el unitario en rojo primero: leer, tomar el lock y releer; si la relectura da null, seguir por el camino del número. Si el test no lo muestra, no se toca.* (Tarea 12.)
7. **Invitaciones:** `GetLatestAsync` y `GetLatestSentAsync` conservan `Get` y pasan a devolver la entidad seguida (sale su `AsNoTracking`). La alternativa es `FindLatest…` devolviendo una entidad sin seguimiento: se lee bien, pero "`AsNoTracking` solo en lectores" deja de poder verificarse. *Conservar `Get`, seguidas.* (Tarea 24.)
8. **La convención de nombres**, en el spec de arquitectura y en un ADR 0008 corto (el `README` de `docs/decisions` pide un ADR para toda decisión que cambia cómo se escribe el código): la tabla completa con `Lock…`, `Add`, las escrituras y la excepción temporal de `Invalidate`; `Count` admite un registro `*Counts`; `FindRegistrationModeAsync` con su valor por defecto documentado; 9 renombres después de borrar `IIdentityService`; `PersistenceNamingTests` con trinquete; `FindRoleAsync` → `FindByIdAsync` queda para la Etapa 4. *Aceptar todo.* (Tareas 18 a 24.)
9. **`CountActiveAdminsAsync`** trae a memoria y deja seguidos a todos los Admin desde un lector. Opciones: (A) un commit extra que lo pasa a un `COUNT` en SQL, con `Admin_count_includes_only_active_and_not_deleted_accounts` como red; (B) dejarlo anotado como excepción en el spec. *B en esta etapa, para no agrandarla, y A en la Etapa 7.* (Tarea 18.)
10. **Una sola forma de llenar `HybridCache`** (`HybridCacheExtensions.GetOrCreateInOwnScopeAsync`, con su regla). `PermissionService` y `SystemSettingsReader` pasan a usarla y queda sin efecto la decisión 14 de la Etapa 1. Cuesta una conexión más por rol con el caché frío. La alternativa es un cambio local en `PermissionService`, sin helper ni regla. *El helper: la plantilla tiene que mostrar una sola manera.* (Tarea 3.)
11. **`IOpenIddictTokenRevoker`** queda separado de `ISignInService`: corre fuera de todo límite, a propósito. *No tocarlo.*
12. **La puerta de la etapa** busca `IIdentityService`, `IdentityService`, `FakeIdentityService`, `IdentityServiceTests` e `IdentityServiceExtensions` solo en `src`, `tests`, `AGENTS.md`, `CLAUDE.md`, `README.md`, `docs/architecture`, `docs/features` y los specs funcionales (todo `docs/specs` salvo el diseño inicial, histórico para la estructura de capas), y el plan maestro se corrige: la regla "`Application.Services` no depende de `Microsoft.AspNetCore.Identity`" ya la cubren `LayerDependencyTests` y las referencias del csproj. Para que la puerta pueda mirar los specs funcionales, la Tarea 16 corrige la única mención que tienen (el spec de WhatsApp nombra `IIdentityService.SignInAsync`). *Aceptar.* (Tareas 16, 28 y 29.)

---

## Mapa de archivos

Rutas relativas a `C:\Users\ezequ\source\repos\ArquitecturaBase`. En las listas de tests, `tests/.../` abrevia la carpeta del proyecto que corresponde.

**Crear**

| Archivo | Para qué | Tarea |
|---|---|---|
| `src/ArquitecturaBase.Infrastructure/Caching/HybridCacheExtensions.cs` | la única forma de llenar `HybridCache`, en un scope propio | 3 |
| `src/ArquitecturaBase.Infrastructure/Identity/UserManagerExtensions.cs` | la única carga por Id de una cuenta no borrada para modificarla | 4 |
| `tests/ArquitecturaBase.ArchitectureTests/IdentityBoundaryTests.cs` | las reglas de Identity sobre el IL | 4, 5, 16, 26, 27 |
| `src/ArquitecturaBase.Application/Interfaces/Integrations/ISignInService.cs` | el contrato técnico del ingreso | 5, 26, 27 |
| `src/ArquitecturaBase.Infrastructure/Identity/SignInService.cs` | su implementación | 5, 27 |
| `tests/ArquitecturaBase.Api.IntegrationTests/Identity/SignInServiceTests.cs` | el contrato contra Postgres | 5, 27 |
| `tests/ArquitecturaBase.Api.IntegrationTests/Support/UserRepositoryExtensions.cs` | la forma corta del alta, sobre `IUserRepository` | 13 |
| `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UserRepositoryTests.cs` | las escrituras de cuentas | 15 |
| `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UserReaderTests.cs` | las lecturas de cuentas | 15 |
| `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeSignInService.cs` | el doble de `ISignInService` | 17, 26 |
| `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Users/InMemoryUserAccounts.cs` | el doble de `IUserReader` e `IUserRepository` | 17 |
| `docs/decisions/0008-nombres-de-repositorios-y-lectores.md` | el ADR de la convención | 18, 29 |
| `tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs` | la convención, verificada | 18 a 24 |
| `tests/ArquitecturaBase.Application.UnitTests/Services/Auth/LoginLinkServiceTests.cs` | el canje del enlace y su cookie | 27 |
| `tests/ArquitecturaBase.ArchitectureTests/Support/UseCaseEntryPoints.cs` | qué es un punto de entrada, compartido | 27 |

**Borrar**

| Archivo | Tarea |
|---|---|
| `src/ArquitecturaBase.Application/Interfaces/Integrations/IIdentityService.cs` | 16 |
| `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs` | 16 |
| `tests/ArquitecturaBase.Api.IntegrationTests/Support/IdentityServiceExtensions.cs` | 16 |
| `tests/ArquitecturaBase.Api.IntegrationTests/Identity/IdentityServiceTests.cs` | 15 |
| `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs` | 17 |

**Modificar (producción)**

| Archivo | Qué cambia | Tarea |
|---|---|---|
| `src/ArquitecturaBase.Infrastructure/Identity/PermissionService.cs` | la fábrica en su propio scope; nombres del lector | 3, 21 |
| `src/ArquitecturaBase.Infrastructure/Persistence/Readers/SystemSettingsReader.cs` | la fábrica por el helper; nombre | 3, 23 |
| `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/UserRepository.cs` | la carga única | 4 |
| `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs` | la carga única; delega en `ISignInService`; se borra | 4, 5, 16 |
| `src/ArquitecturaBase.Infrastructure/Identity/IdentityRegistration.cs` | registra `SignInService`; saca `IdentityService`; comentario del `UserName` | 5, 16 |
| `src/ArquitecturaBase.Application/Services/Users/AccountAccessRevoker.cs` | `ISignInService` | 6 |
| `src/ArquitecturaBase.Application/Services/Auth/ExternalLoginService.cs` | `ISignInService`; nombre del lector | 7, 19 |
| `src/ArquitecturaBase.Application/Services/Auth/LoginLinkService.cs` | `IUserReader` e `ISignInService`; la cookie después del commit | 8, 27 |
| `src/ArquitecturaBase.Application/Services/Auth/AccountService.cs` | `IUserReader`; `ISignInService` y la cookie después del commit | 9, 26 |
| `src/ArquitecturaBase.Application/Services/Auth/LoginCodeVerifier.cs` | los tres contratos; nombres; devuelve el Id | 10, 19, 26 |
| `src/ArquitecturaBase.Application/Services/WhatsApp/WhatsAppInboundService.cs` | los tres contratos; relee la cuenta vinculada | 11, 12 |
| `src/ArquitecturaBase.Application/Interfaces/Persistence/IUserReader.cs`, `IUserRepository.cs` | los XML que traen de `IIdentityService`; nombres | 16, 19 |
| `src/ArquitecturaBase.Infrastructure/Persistence/Readers/UserReader.cs` | nombres | 19 |
| `src/ArquitecturaBase.Application/Services/Users/{ProfileService,ProfileEmailOperations,ProfileWhatsAppOperations,UserGuards,UserService,UserWriteOperations}.cs` | nombres de los lectores | 19, 22 |
| `src/ArquitecturaBase.Application/Interfaces/Persistence/IRoleReader.cs`, `src/ArquitecturaBase.Infrastructure/Persistence/Readers/RoleReader.cs`, `src/ArquitecturaBase.Application/Services/Roles/RoleService.cs` | `ExistsByNameAsync` | 20 |
| `src/ArquitecturaBase.Application/Interfaces/Persistence/IPermissionReader.cs`, `src/ArquitecturaBase.Infrastructure/Persistence/Readers/PermissionReader.cs` | nombres | 21 |
| `src/ArquitecturaBase.Application/Interfaces/Persistence/ILoginAuditRepository.cs`, `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/LoginAuditRepository.cs` | nombre y sin `AsNoTracking` | 22 |
| `src/ArquitecturaBase.Application/Interfaces/Persistence/ISystemSettingsReader.cs`, `src/ArquitecturaBase.Application/Services/Auth/AccountCreationPolicy.cs` | `FindRegistrationModeAsync` | 23 |
| `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/{LoginLinkRepository,UserInvitationRepository}.cs`, `src/ArquitecturaBase.Application/Interfaces/Persistence/IUserInvitationRepository.cs` | sin `AsNoTracking` | 24 |
| `src/ArquitecturaBase.Infrastructure/Persistence/Extensions/TransactionExtensions.cs` | `RequireNoTransaction` | 27 |
| `src/ArquitecturaBase.Application/Interfaces/Persistence/IUnitOfWork.cs` | resumen: la cookie de la aplicación | 27 |

**Modificar (tests y docs)**

| Archivo | Qué cambia | Tarea |
|---|---|---|
| `tests/.../Support/ApiFactory.cs` | sobrecargas sin valor | 1 |
| `tests/.../Auth/LoginLinkTests.cs` | sin `return`; arma por los repositorios; resumen del commit fallido | 1, 14, 27 |
| `tests/.../Support/AdminUsersApi.cs`, `tests/.../Users/{MeEmailEndpointsTests,MeWhatsAppEndpointsTests,UnlinkUserPhoneEndpointTests,UpdateUserContactTests,UserSoftDeleteTests,UsersEndpointsTests,AccountsWithPhoneTests}.cs` | sin `return`; arman por los repositorios | 1, 13 |
| `tests/.../Persistence/RoleRepositoryTransactionTests.cs`, `tests/.../Settings/SystemSettingsReaderTests.cs` | sobrecargas sin valor; nombres | 1, 20, 23 |
| `tests/.../Identity/PermissionServiceTests.cs` | arma con los repositorios; test de la fábrica; nombre | 2, 3, 21 |
| `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` | la regla del caché; `UseCaseEntryPoints` | 3, 27 |
| `tests/.../Identity/IdentityServiceTests.cs` | pierde los dos tests que se mudan; se borra | 5, 15 |
| `tests/.../Persistence/UnitOfWorkTransactionTests.cs` | la cobertura de `ISignInService`; arma por el repositorio; sin el test de `IIdentityService` | 5, 14, 16, 27 |
| `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs` | declara `ISignInService`; pierde `IIdentityService` y los roles | 5, 16 |
| `tests/ArquitecturaBase.Application.UnitTests/Services/Auth/{AccountServiceTests,LoginCodeVerifierParityTests,RequestLoginCodeServiceTests,RequestWhatsAppLoginCodeServiceTests,VerifyLoginCodeServiceTests}.cs` | constructores; la cookie después del commit | 10, 17, 26 |
| `tests/ArquitecturaBase.Application.UnitTests/Services/WhatsApp/WhatsAppInboundServiceTests.cs` | constructor; la relectura | 11, 12, 17 |
| `tests/.../Auth/{ExternalLoginTests,LoginSecurityTests,PhoneAccountTokensTests,RegistrationModeTests,WhatsAppLoginCodeTests,InitialAdminSignInTests}.cs`, `tests/.../Persistence/{LoginLinkRepositoryTests,RoleReaderTests}.cs`, `tests/.../WhatsApp/{WhatsAppBotTests,WhatsAppBotLogPrivacyTests}.cs` | arman por los repositorios | 14 |
| `tests/.../Support/StaleIdentityReads.cs` | sin el envoltorio de `IIdentityService`; nombres | 16, 19 |
| `tests/ArquitecturaBase.Application.UnitTests/Services/Users/{UserServiceTestHost,UserServiceWriteTests}.cs` | `FakeRoleReader` propio; roles por el lector | 16, 17 |
| el resto de los unitarios que usan `FakeIdentityService` | `InMemoryUserAccounts` y `FakeSignInService` | 17 |
| `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/TransactionGuard.cs` | `RequireNone` | 26 |
| `tests/.../Auth/LoginCodeEndpointsTests.cs` | el verify con commit fallido | 25 |
| los unitarios y de integración que nombran un método renombrado | nombres | 19 a 23 |
| los documentos de la sección 9 del Diseño | ver esa tabla | 3 a 29 |

---

## Comandos

- **Una llamada, un comando completo.** Las variables y funciones de la shell no sobreviven entre llamadas de la herramienta: cada comando de abajo va entero. `$TEMP` sí existe siempre (es del entorno de Windows), y ahí van los logs: nunca adentro del repo.
- **La salida completa va a un archivo** y en la pantalla se mira el código de salida y la cola. Si algo falla, se abre el archivo entero (`grep -nE "error|Error|falló|failed|Assert" "$TEMP/etapa2-<nombre>.log"`) en lugar de volver a correr. Códigos de salida de Microsoft Testing Platform: `0` todo en verde; `2` algún test falló; `8` no se ejecutó ningún test (casi siempre, un filtro mal escrito).
- **Leer una falla.** Microsoft Testing Platform encabeza cada test que falla con `con errores <Clase.Método>` (en inglés, `failed <Clase.Método>`): los pasos rojos lo buscan con `^(con errores|failed) `. En el mensaje, xUnit muestra hasta 5 elementos de una colección (después, `···`) y corta cada texto a los 50 caracteres (`"ArquitecturaBase.Infrastructure.Identity.IdentityS"···`): por eso un paso rojo busca un fragmento que entre en ese corte, o la línea `Collection:` entera, y no el nombre completo de un tipo largo (verificado con xUnit v3 4.0.1).
- Compilar:

  ```bash
  dotnet build ArquitecturaBase.slnx > "$TEMP/etapa2-build.log" 2>&1; echo "exit=$?"; tail -n 5 "$TEMP/etapa2-build.log"
  ```

  Esperado: `exit=0` y `0 Advertencia(s)`, `0 Errores` (o `0 Warning(s)`, `0 Error(s)`, según el idioma de la máquina). IDE0005 (using innecesario) e IDE0161 son errores de build: cuando un paso saca el último uso de un namespace, se borra su `using`; cuando agrega uno nuevo, se agrega.
- **Una clase:** `dotnet test --project <proyecto> -- --filter-class "<Namespace.Clase>" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 30 "$TEMP/etapa2-focus.log"`. Se puede repetir `--filter-class` (OR).
- **Un método:** lo mismo con `--filter-method "<Namespace.Clase.Metodo>"`, repetible (OR).
- **No mezclar `--filter-class` y `--filter-method` en el mismo comando.** Los filtros del mismo tipo se combinan con OR y los de tipos distintos con AND: una clase más un método de otra clase no seleccionan nada y el comando termina con código 8, que parece un fallo del código. Para correr una clase y un método sueltos, dos comandos.
- Los proyectos: `tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj`, `tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`, `tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj` y `tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`. Un `dotnet test --project` sin `--no-build` compila ese proyecto y sus dependencias: así un paso rojo por compilación se ve en el mismo comando.
- **Verificación completa (V1 a V10).** La herramienta corta a los 10 minutos, así que la integración va en seis tandas por namespace, cada una en su propia llamada, después de un build (V1) y con `--no-build`. Las seis tandas cubren los 955 tests de integración de `42a25c1` sin repetir ninguno (verificado con `--list-tests`: 119, 190, 198, 127, 114 y 207). Un namespace nuevo cae solo en V10. Si una tanda pasa de 9 minutos, se parte por clase con `--filter-class`.

  | Paso | Comando |
  |---|---|
  | V1 | `dotnet build ArquitecturaBase.slnx > "$TEMP/etapa2-build.log" 2>&1; echo "exit=$?"; tail -n 5 "$TEMP/etapa2-build.log"` |
  | V2 | `dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj --no-build > "$TEMP/etapa2-domain.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-domain.log"` |
  | V3 | `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj --no-build > "$TEMP/etapa2-unit.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-unit.log"` |
  | V4 | `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj --no-build > "$TEMP/etapa2-arch.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-arch.log"` |
  | V5 | `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-namespace "ArquitecturaBase.Api.IntegrationTests.Auth" > "$TEMP/etapa2-it-auth.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-it-auth.log"` |
  | V6 | `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-namespace "ArquitecturaBase.Api.IntegrationTests.Users" > "$TEMP/etapa2-it-users.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-it-users.log"` |
  | V7 | `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-namespace "ArquitecturaBase.Api.IntegrationTests.WhatsApp" > "$TEMP/etapa2-it-whatsapp.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-it-whatsapp.log"` |
  | V8 | `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-namespace "ArquitecturaBase.Api.IntegrationTests.Persistence" --filter-namespace "ArquitecturaBase.Api.IntegrationTests.Identity" --filter-namespace "ArquitecturaBase.Api.IntegrationTests.Settings" --filter-namespace "ArquitecturaBase.Api.IntegrationTests.Roles" > "$TEMP/etapa2-it-persistence.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-it-persistence.log"` |
  | V9 | `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-namespace "ArquitecturaBase.Api.IntegrationTests.Contracts" > "$TEMP/etapa2-it-contracts.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-it-contracts.log"` |
  | V10 | `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-not-namespace "ArquitecturaBase.Api.IntegrationTests.Auth" --filter-not-namespace "ArquitecturaBase.Api.IntegrationTests.Users" --filter-not-namespace "ArquitecturaBase.Api.IntegrationTests.WhatsApp" --filter-not-namespace "ArquitecturaBase.Api.IntegrationTests.Persistence" --filter-not-namespace "ArquitecturaBase.Api.IntegrationTests.Identity" --filter-not-namespace "ArquitecturaBase.Api.IntegrationTests.Settings" --filter-not-namespace "ArquitecturaBase.Api.IntegrationTests.Roles" --filter-not-namespace "ArquitecturaBase.Api.IntegrationTests.Contracts" > "$TEMP/etapa2-it-rest.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-it-rest.log"` |

  Esperado en cada paso: `exit=0`. `--filter-namespace` elige el namespace exacto, sin los hijos: por eso V10 excluye uno por uno los de las otras tandas y así levanta la raíz (`ArquitecturaBase.Api.IntegrationTests`) y los namespaces chicos.
- **Tiempos de las clases de concurrencia.** Las tareas que tocan src de ingreso o del bot corren además, en un comando aparte y sin `--no-build`, `WhatsAppLockOrderTests`, `WhatsAppBotTests`, `MeWhatsAppEndpointsTests` y `LoginLinkTests`, y anotan la duración que informa el resumen: un salto de más del doble frente a la Tarea 0 es para frenar y mirar (un lock que ahora espera un timeout).

  ```bash
  dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppLockOrderTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppBotTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.MeWhatsAppEndpointsTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.LoginLinkTests" > "$TEMP/etapa2-concurrency.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-concurrency.log"
  ```
- **Reglas del paso rojo:** el primer "verlo fallar" puede ser un error de compilación (el test ya usa la firma nueva) o una aserción; los dos cuentan. Si un test que tiene que fallar pasa antes de cambiar el código, frenar: el test no prueba lo que dice. Las tareas de refactor sin cambio de comportamiento no tienen paso rojo: su red son los tests que ya existen, que tienen que seguir en verde sin tocarlos (o tocando solo lo que el paso indica).
- **Documentación XML y comentarios:** cuando un paso trae un `<summary>` o un comentario en un bloque `csharp`, se reemplaza el bloque entero por ese. Cuando cita en prosa una oración que hay que buscar, en el código puede estar partida en varias líneas `///` o `//`: se busca por un fragmento, se reemplaza la oración completa y se vuelve a cortar en líneas de hasta 120 caracteres. Un `<see cref=...>` roto es CS1574 y rompe el build.
- **Reemplazos mecánicos:** los pasos que tocan muchos sitios dan un comando de `perl` (viene con Git para Windows). `perl -0777 -pi -e '...'` lee el archivo entero y respeta los finales de línea CRLF que tienen los `.cs`. Después de cada uno, el paso dice qué `git grep` tiene que salir vacío y el build confirma el resto.
- **Antes de cada commit:** `git status --short` muestra solo archivos de la tarea, más los ajenos que ya estaban (` M README.md` y `?? .codex/`, del usuario, que no se tocan ni se agregan). `main` es compartido y la Etapa 5 puede estar trabajando en paralelo: si aparece algo más, no se agrega; se frena y se avisa. Se agrega siempre con rutas explícitas (`git add <rutas>`), nunca con una carpeta entera, `git add -A`, `git add .` ni `git commit -a`. Las tareas de renombre, que tocan muchos archivos, arman la lista con `$(git diff --name-only -- <carpetas> | grep -E '\.cs$')`, después de mirar `git status --short -- <carpetas>`: la Etapa 5 mantiene `AGENTS.md` y `CLAUDE.md` adentro de `src`.
- Commit (Git Bash), siempre con la línea de autoría:

  ```bash
  git commit -m "$(cat <<'EOF'
  <tipo>: <mensaje en español>

  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  EOF
  )"
  ```
- Si se levanta el AppHost para probar a mano (`aspire run`), al terminar: `aspire stop`.

---

## Tareas

### Tarea 0: preparación (un solo commit: este plan)

**Archivos:**
- Versionar: `docs/plans/2026-09-27-etapa-2-identity-solo-tecnico.md` (este plan)

- [ ] **Paso 0: las decisiones.** Presentarle al usuario las doce decisiones de la sección 10 del Diseño, con su recomendación, y esperar su confirmación explícita en el chat. Si rechaza o cambia alguna, frenar: se ajustan las tareas que indica esa decisión antes de ejecutar nada.
- [ ] **Paso 1: lo que no es de esta etapa.** Correr `git status --short`. Esperado, en `42a25c1`: ` M README.md` y `?? .codex/` (trabajo del usuario, que este plan no toca ni agrega) y `?? docs/plans/2026-09-27-etapa-2-identity-solo-tecnico.md`. Si aparece algo más, frenar y preguntarle al usuario: puede ser la Etapa 5 trabajando sobre `main`. **Nunca descartar, mover ni borrar este plan**, aunque figure como sin versionar.
- [ ] **Paso 2: versionar el plan.**

```bash
git add docs/plans/2026-09-27-etapa-2-identity-solo-tecnico.md
git commit -m "$(cat <<'EOF'
docs: plan de la Etapa 2

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

Después, `git status --short`. Esperado: solo ` M README.md` y `?? .codex/`.
- [ ] **Paso 3: línea de base.** Con Docker levantado, correr V1 a V10 ("Comandos"). Esperado: `exit=0` en los diez, y el build sin advertencias. Correr también el comando de "Tiempos de las clases de concurrencia" y reportarle al usuario la duración que informa el resumen, sin escribirla en ningún archivo: es la línea de base contra la que se comparan las tareas siguientes. Si algo falla antes de empezar, frenar y reportarlo: no es de esta etapa.
- [ ] **Paso 4: leer el contexto.** En este orden: este plan completo, la sección "Etapa 2" del plan maestro y su tabla "Decisiones", el "Diseño" del plan histórico de la Etapa 1 (secciones 1 a 3), las reglas de identidad y de WhatsApp (sección 9 del Diseño) y la sección "Una sola forma de guardar" del spec de arquitectura.
- [ ] **Paso 5: `main` pudo avanzar.** Correr `git log --oneline 42a25c1..HEAD -- src tests AGENTS.md CLAUDE.md docs/architecture docs/features docs/decisions docs/specs`. Todo commit posterior a `42a25c1` que toque un archivo del "Mapa de archivos" o un documento de la sección 9 obliga a releer ese archivo antes de su tarea. Los reemplazos del plan se ajustan a lo que haya: el criterio es el Diseño, no el texto literal. En particular, si la Etapa 5 movió un documento, se sigue la regla de la sección 9.

---

### Tarea 1: los arneses sin valor no piden un `return` (pendiente e)

Solo tests. `ApiFactory` implementa directo el `InTransactionAsync` sin valor y suma sobrecargas sin valor de `ExecuteScopeAsync` y `ExecuteDbContextAsync`; las lambdas que devolvían un valor de mentira dejan de hacerlo, y el compilador elige la sobrecarga sin valor.

**Archivos:**
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/RoleRepositoryTransactionTests.cs`, `tests/ArquitecturaBase.Api.IntegrationTests/Settings/SystemSettingsReaderTests.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginLinkTests.cs`, `Support/AdminUsersApi.cs`, `Users/MeEmailEndpointsTests.cs`, `Users/MeWhatsAppEndpointsTests.cs`, `Users/UnlinkUserPhoneEndpointTests.cs`, `Users/UpdateUserContactTests.cs`, `Users/UserSoftDeleteTests.cs`, `Users/UsersEndpointsTests.cs`

- [ ] **Paso 1: el paso rojo.** Sacar el valor de mentira de los arneses que todavía no tienen sobrecarga sin valor.
  - En `RoleRepositoryTransactionTests.cs`:

    ```bash
    perl -0777 -pi -e 's/(\r?\n)(?:[ \t]*\r?\n)?[ \t]*return true;\r?\n/$1/g' tests/ArquitecturaBase.Api.IntegrationTests/Persistence/RoleRepositoryTransactionTests.cs
    ```
  - En `SystemSettingsReaderTests.cs`, reemplazar los dos helpers del final (`UpdateRowAsync` e `InvalidateAsync`) por:

    ```csharp
        private Task UpdateRowAsync(RegistrationMode mode) =>
            factory.ExecuteDbContextAsync(async dbContext =>
            {
                var settings = await dbContext.SystemSettings.SingleAsync(Ct);
                settings.SetRegistrationMode(mode);
                await dbContext.SaveChangesAsync(Ct);
            });

        private Task InvalidateAsync() =>
            factory.ExecuteScopeAsync(services => services.GetRequiredService<ISystemSettingsReader>().InvalidateAsync(Ct));
    ```

  Run: `dotnet build tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj > "$TEMP/etapa2-build.log" 2>&1; echo "exit=$?"; grep -E "error CS" "$TEMP/etapa2-build.log" | head -5`
  Esperado: `exit=1`, con errores de conversión de la lambda asíncrona en `RoleRepositoryTransactionTests.cs` y `SystemSettingsReaderTests.cs` (no hay una sobrecarga que acepte una lambda que no devuelve nada).

- [ ] **Paso 2: las sobrecargas de `ApiFactory`.** En `ApiFactory.cs`:
  - Debajo de `ExecuteDbContextAsync<T>`, agregar:

    ```csharp
        /// <summary>Como la sobrecarga genérica, para una acción que no devuelve nada.</summary>
        public async Task ExecuteDbContextAsync(Func<ApplicationDbContext, Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            await using var scope = Services.CreateAsyncScope();

            await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }
    ```
  - Debajo de `ExecuteScopeAsync<T>`, agregar:

    ```csharp
        /// <summary>Como la sobrecarga genérica, para una acción que no devuelve nada.</summary>
        public async Task ExecuteScopeAsync(Func<IServiceProvider, Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            await using var scope = Services.CreateAsyncScope();

            await action(scope.ServiceProvider);
        }
    ```
  - Reemplazar el `InTransactionAsync(Func<IServiceProvider, Task> action)` completo (hoy :157-168, el que termina en `return true;`) por:

    ```csharp
        /// <inheritdoc cref="InTransactionAsync{T}(Func{IServiceProvider, Task{T}})"/>
        public async Task InTransactionAsync(Func<IServiceProvider, Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            await using var scope = Services.CreateAsyncScope();
            var services = scope.ServiceProvider;

            await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
                async _ =>
                {
                    await action(services);

                    return Result.Success();
                },
                CommitPolicy.OnSuccess,
                TestContext.Current.CancellationToken);
        }
    ```

  Una lambda con `return` de un valor sigue eligiendo la sobrecarga genérica (la conversión a `Func<…, Task<T>>` es mejor), y una sin valor elige la nueva.

- [ ] **Paso 3: las 26 lambdas de `InTransactionAsync`.** Cada una termina en `return true;` o `return 0;` (con o sin una línea en blanco antes) y su resultado se descarta. En todos estos archivos, todo `return true;`/`return 0;` es uno de esos (verificado en `42a25c1`: 3, 1, 2, 7, 2, 1, 1 y 9):

  ```bash
  perl -0777 -pi -e 's/(\r?\n)(?:[ \t]*\r?\n)?[ \t]*return (?:true|0);\r?\n/$1/g' tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginLinkTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Support/AdminUsersApi.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/MeEmailEndpointsTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/MeWhatsAppEndpointsTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UnlinkUserPhoneEndpointTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UpdateUserContactTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UserSoftDeleteTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UsersEndpointsTests.cs
  ```

  Revisar con `git diff --stat`: solo líneas borradas (6, 2, 4, 14, 3, 2, 2 y 9 en ese orden, contando las en blanco).

- [ ] **Paso 4: la puerta del paso.**

  Run: `git grep -nE "return (true|0);" -- tests/ArquitecturaBase.Api.IntegrationTests`
  Esperado: solo `Identity/IdentityServiceTests.cs` (13 líneas, se van en la Tarea 15), `Identity/PermissionServiceTests.cs` (3, Tarea 2) y `Support/CapturingWhatsAppOutbox.cs:29` (un valor con sentido).

- [ ] **Paso 5: verlo pasar.** V1 (build sin advertencias) y:

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.RoleRepositoryTransactionTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Settings.SystemSettingsReaderTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.LoginLinkTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`.

- [ ] **Paso 6: verificación completa.** V2 a V10 en verde.
- [ ] **Paso 7: commit.**

```bash
git add tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs tests/ArquitecturaBase.Api.IntegrationTests/Support/AdminUsersApi.cs tests/ArquitecturaBase.Api.IntegrationTests/Persistence/RoleRepositoryTransactionTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Settings/SystemSettingsReaderTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginLinkTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/MeEmailEndpointsTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/MeWhatsAppEndpointsTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UnlinkUserPhoneEndpointTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UpdateUserContactTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UserSoftDeleteTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UsersEndpointsTests.cs
git commit -m "$(cat <<'EOF'
test: los arneses sin valor no piden un return

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 2: `PermissionServiceTests` arma usuarios y roles con los repositorios dentro de un límite (pendiente d)

Solo tests, en verde antes y después. Es el único archivo de tests que resuelve `UserManager`, `RoleManager` o `SignInManager`, y lo hace en autocommit; además su `CreateUserAsync` pone `UserName = email`, cuando producción usa el Id.

**Archivos:**
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Identity/PermissionServiceTests.cs` (reemplazo completo)

- [ ] **Paso 1: reescribir el archivo.** Reemplazar `PermissionServiceTests.cs` completo por:

```csharp
using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Identity;

/// <summary>
/// Permisos efectivos: la suma de los permisos de los roles de la cuenta, con los de cada rol cacheados. Los datos se
/// arman como los arma la aplicación: con IUserRepository e IRoleRepository, adentro de un límite
/// (factory.InTransactionAsync).
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class PermissionServiceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Permissions_are_the_union_of_the_user_roles()
    {
        var userId = await CreateUserAsync(SystemRoles.Admin, SystemRoles.User);

        var permissions = await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct));

        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), permissions);
    }

    [Fact]
    public async Task User_role_has_no_permissions_yet()
    {
        var userId = await CreateUserAsync(SystemRoles.User);

        Assert.Empty(await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct)));
        Assert.False(await WithPermissionsAsync(service => service.HasPermissionAsync(userId, Permissions.Users.Read, Ct)));
    }

    [Fact]
    public async Task Role_permissions_are_cached_until_the_role_is_invalidated()
    {
        var role = await CreateRoleAsync(Permissions.Users.Read);
        var userId = await CreateUserAsync(role.Name);
        Assert.True(await HasUsersReadAsync(userId));

        // Se le saca el permiso sin pasar por RoleService, que invalidaría el caché después del commit.
        await factory.InTransactionAsync(services =>
            services.GetRequiredService<IRoleRepository>().UpdateAsync(role.Id, role.Name, null, [], Ct));

        Assert.True(await HasUsersReadAsync(userId));

        await factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IPermissionService>().InvalidateRoleAsync(role.Id, Ct));

        Assert.False(await HasUsersReadAsync(userId));
    }

    [Fact]
    public async Task Role_reassignment_is_visible_without_invalidating_cached_role_permissions()
    {
        var originalRole = await CreateRoleAsync(Permissions.Users.Read);
        var replacementRole = await CreateRoleAsync(Permissions.Roles.Read);
        var userId = await CreateUserAsync(originalRole.Name);

        Assert.Equal([Permissions.Users.Read], await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct)));

        await factory.InTransactionAsync(services =>
            services.GetRequiredService<IUserRepository>().SetRolesAsync(userId, [replacementRole.Name], Ct));

        Assert.Equal([Permissions.Roles.Read], await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct)));
        Assert.False(await HasUsersReadAsync(userId));
    }

    [Fact]
    public async Task Reader_returns_only_permission_claims_for_a_role()
    {
        var role = await CreateRoleAsync(Permissions.Roles.Read);

        // IRoleRepository escribe solo claims de permisos. Uno de otro tipo se agrega directo en el contexto, adentro del
        // límite, y lo baja su guardado final.
        await factory.InTransactionAsync(services =>
        {
            services.GetRequiredService<ApplicationDbContext>().RoleClaims.Add(new IdentityRoleClaim<Guid>
            {
                RoleId = role.Id,
                ClaimType = "unrelated",
                ClaimValue = Permissions.Users.Read,
            });

            return Task.CompletedTask;
        });

        var claims = await factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IPermissionReader>().GetRolePermissionsAsync(role.Id, Ct));

        Assert.Equal([Permissions.Roles.Read], claims);
    }

    private Task<bool> HasUsersReadAsync(Guid userId) =>
        WithPermissionsAsync(service => service.HasPermissionAsync(userId, Permissions.Users.Read, Ct));

    /// <summary>Una cuenta nueva con exactamente esos roles, como la dejan el alta y la edición de sus roles.</summary>
    private Task<Guid> CreateUserAsync(params string[] roles) =>
        factory.InTransactionAsync(async services =>
        {
            var users = services.GetRequiredService<IUserRepository>();
            var email = Email.Create("perm-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com").Value;
            var user = await users.CreateAsync(email, phone: null, phoneConfirmed: false, displayName: null, "es", Ct);
            await users.SetRolesAsync(user.Id, roles, Ct);

            return user.Id;
        });

    private Task<(Guid Id, string Name)> CreateRoleAsync(string permission) =>
        factory.InTransactionAsync(async services =>
        {
            var name = "permission-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            var id = await services.GetRequiredService<IRoleRepository>().CreateAsync(name, null, [permission], Ct);

            return (id, name);
        });

    private Task<T> WithPermissionsAsync<T>(Func<IPermissionService, Task<T>> action) =>
        factory.ExecuteScopeAsync(services => action(services.GetRequiredService<IPermissionService>()));
}
```

- [ ] **Paso 2: verlo pasar.**

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Identity.PermissionServiceTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`, 5 tests.

- [ ] **Paso 3: la puerta del pendiente d.**

  Run: `git grep -nE "UserManager<|RoleManager<|SignInManager<" -- tests; git grep -nE "return (true|0);" -- tests/ArquitecturaBase.Api.IntegrationTests/Identity/PermissionServiceTests.cs`
  Esperado: sin salida.

- [ ] **Paso 4: verificación completa.** V1 a V10.
- [ ] **Paso 5: commit.**

```bash
git add tests/ArquitecturaBase.Api.IntegrationTests/Identity/PermissionServiceTests.cs
git commit -m "$(cat <<'EOF'
test: PermissionServiceTests arma usuarios y roles con los repositorios dentro de un límite

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 3: las fábricas de caché leen en su propio scope (pendiente b)

Un `fix`: hoy la fábrica de `PermissionService` corre sobre el contexto de quien llama. Primero el test en rojo; después el helper, los dos lectores con caché sobre él y la regla.

**Archivos:**
- Crear: `src/ArquitecturaBase.Infrastructure/Caching/HybridCacheExtensions.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/PermissionService.cs` (reemplazo completo)
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Readers/SystemSettingsReader.cs` (reemplazo completo)
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Identity/PermissionServiceTests.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs`
- Modificar: el spec de arquitectura (rol; hoy `docs/architecture/backend.md`)

- [ ] **Paso 1: el test de la fábrica, en rojo.** En `PermissionServiceTests.cs`, debajo de `Reader_returns_only_permission_claims_for_a_role`, agregar:

```csharp
    /// <summary>
    /// La fábrica del caché lee en su propio scope, con otra conexión: un permiso que quien pregunta todavía no confirmó no
    /// se cachea. Sobre el contexto de quien llama, adentro de un límite, la fábrica vería el cambio sin confirmar y lo
    /// dejaría una hora en el caché aunque el límite se deshiciera.
    /// </summary>
    [Fact]
    public async Task The_cache_factory_reads_on_its_own_connection_and_never_caches_uncommitted_permissions()
    {
        // Un rol nuevo: su clave del caché arranca fría.
        var role = await CreateRoleAsync(Permissions.Users.Read);
        var userId = await CreateUserAsync(role.Name);
        IReadOnlyCollection<string> captured = [];

        await Assert.ThrowsAsync<RolledBackOnPurpose>(() => factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IRoleRepository>().UpdateAsync(
                role.Id, role.Name, null, [Permissions.Users.Read, Permissions.Roles.Read], Ct);
            captured = await services.GetRequiredService<IPermissionService>().GetPermissionsAsync(userId, Ct);

            throw new RolledBackOnPurpose();
        }));

        Assert.Equal([Permissions.Users.Read], captured);
        Assert.Equal([Permissions.Users.Read], await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct)));
    }
```

  y, al final de la clase, antes de la llave que la cierra:

```csharp
    private sealed class RolledBackOnPurpose : Exception;
```

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-method "ArquitecturaBase.Api.IntegrationTests.Identity.PermissionServiceTests.The_cache_factory_reads_on_its_own_connection_and_never_caches_uncommitted_permissions" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; grep -nE "Expected|Actual|Esperado|Real" "$TEMP/etapa2-focus.log" | head -6`
  Esperado: `exit=2`, con la colección esperada `["users.read"]` y la real `["roles.read", "users.read"]`: la fábrica vio el permiso sin confirmar.

- [ ] **Paso 2: la regla, en rojo.** En `TransactionBoundaryTests.cs`, debajo de la constante `MessageRetentionRepository`, agregar:

```csharp
    private const string CacheExtensions = "ArquitecturaBase.Infrastructure.Caching.HybridCacheExtensions";
```

  y, debajo de `Bulk_updates_and_deletes_only_where_documented`, agregar:

```csharp
    [Fact]
    public void Cache_factories_read_in_their_own_scope()
    {
        // Una fábrica de HybridCache que leyera con el contexto de quien llama correría adentro de su límite: vería lo que
        // todavía no se confirmó, lo cachearía y le ocuparía la conexión que el rollback necesita para soltar los locks.
        // HybridCacheExtensions.GetOrCreateInOwnScopeAsync abre un scope propio y es la única que llena el caché.
        var owners = Calls
            .Where(call => call.DeclaringType == "Microsoft.Extensions.Caching.Hybrid.HybridCache"
                && call.Method == "GetOrCreateAsync")
            .Select(call => call.Owner)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Assert.Equal y no Empty: también prueba que el detector ve al dueño permitido.
        Assert.Equal([CacheExtensions], owners);
    }
```

  Run: `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj -- --filter-method "ArquitecturaBase.ArchitectureTests.TransactionBoundaryTests.Cache_factories_read_in_their_own_scope" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 20 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=2`: los dueños de hoy son `PermissionService` y `SystemSettingsReader`. En la línea `Actual:` salen cortados a los 50 caracteres ("Leer una falla", en "Comandos"): `"ArquitecturaBase.Infrastructure.Identity.Permissio"···` y `"ArquitecturaBase.Infrastructure.Persistence.Reader"···`, en el orden en que aparecen en el ensamblado.

- [ ] **Paso 3: el helper.** Crear `src/ArquitecturaBase.Infrastructure/Caching/HybridCacheExtensions.cs`:

```csharp
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Caching;

/// <summary>
/// La única forma de llenar HybridCache (lo verifica TransactionBoundaryTests): la fábrica lee en un scope propio, con su
/// contexto y su conexión, nunca con los de quien llama.
/// </summary>
internal static class HybridCacheExtensions
{
    /// <summary>
    /// Lo cacheado en <paramref name="key"/>, o lo que lee <paramref name="read"/> con un <typeparamref name="TReader"/> de
    /// un scope nuevo. Sobre el contexto de quien llama, la fábrica correría adentro de su límite: vería lo que todavía no
    /// se confirmó y lo cachearía. Con la protección contra estampidas, además, la fábrica sigue sirviendo a otros pedidos
    /// después de que el que la arrancó terminó o se canceló: le ocuparía la conexión que su rollback necesita para soltar
    /// los locks y moriría con su scope. El precio es que, con el caché frío, la fábrica pide una segunda conexión al pool
    /// mientras quien llama tiene la suya tomada por su límite. Si el pool se agotara con límites que esperan justo esta
    /// fábrica, ella esperaría el timeout de conexión y todos esos pedidos terminarían en un 500. Con una fábrica por clave
    /// y un caché de un minuto (los ajustes) o de una hora (los permisos de cada rol) es improbable; no sirve para una
    /// fábrica que corra por fila o por pedido.
    /// </summary>
    public static ValueTask<TValue> GetOrCreateInOwnScopeAsync<TReader, TState, TValue>(
        this HybridCache cache,
        string key,
        IServiceScopeFactory scopes,
        TState state,
        Func<TReader, TState, CancellationToken, Task<TValue>> read,
        HybridCacheEntryOptions options,
        CancellationToken cancellationToken)
        where TReader : notnull
    {
        ArgumentNullException.ThrowIfNull(cache);

        return cache.GetOrCreateAsync(
            key,
            (scopes, state, read),
            static async (factory, token) =>
            {
                await using var scope = factory.scopes.CreateAsyncScope();

                return await factory.read(scope.ServiceProvider.GetRequiredService<TReader>(), factory.state, token);
            },
            options,
            cancellationToken: cancellationToken);
    }

    /// <summary>Como la sobrecarga con estado, para una lectura que no necesita ninguno.</summary>
    public static ValueTask<TValue> GetOrCreateInOwnScopeAsync<TReader, TValue>(
        this HybridCache cache,
        string key,
        IServiceScopeFactory scopes,
        Func<TReader, CancellationToken, Task<TValue>> read,
        HybridCacheEntryOptions options,
        CancellationToken cancellationToken)
        where TReader : notnull =>
        cache.GetOrCreateInOwnScopeAsync<TReader, Func<TReader, CancellationToken, Task<TValue>>, TValue>(
            key, scopes, read, static (reader, readWith, token) => readWith(reader, token), options, cancellationToken);
}
```

- [ ] **Paso 4: `PermissionService` sobre el helper.** Reemplazar `PermissionService.cs` completo por:

```csharp
using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Permisos efectivos: la suma de los permisos de los roles del usuario (sección 5.6). Los roles del usuario se leen
/// siempre de la base, con el lector de quien llama; los permisos de cada rol se cachean y se descartan con
/// <see cref="InvalidateRoleAsync"/>.
/// <para>
/// La fábrica del caché lee en su propio scope (<see cref="HybridCacheExtensions"/>), así que se puede llamar adentro de
/// un límite: no ve lo que quien llama todavía no confirmó ni le ocupa la conexión.
/// </para>
/// </summary>
internal sealed class PermissionService(IPermissionReader reader, IServiceScopeFactory scopes, HybridCache cache)
    : IPermissionService
{
    private static readonly HybridCacheEntryOptions CacheEntryOptions = new()
    {
        Expiration = TimeSpan.FromHours(1),
        LocalCacheExpiration = TimeSpan.FromHours(1),
    };

    public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roleIds = await reader.GetUserRoleIdsAsync(userId, cancellationToken);

        var permissions = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var roleId in roleIds)
        {
            permissions.UnionWith(await GetRolePermissionsAsync(roleId, cancellationToken));
        }

        return permissions;
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
        (await GetPermissionsAsync(userId, cancellationToken)).Contains(permission);

    public async Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        await cache.RemoveAsync(CacheKey(roleId), cancellationToken);

    private async Task<string[]> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateInOwnScopeAsync<IPermissionReader, Guid, string[]>(
            CacheKey(roleId),
            scopes,
            roleId,
            static (permissionReader, id, token) => permissionReader.GetRolePermissionsAsync(id, token),
            CacheEntryOptions,
            cancellationToken);

    private static string CacheKey(Guid roleId) =>
        string.Create(CultureInfo.InvariantCulture, $"permissions:role:{roleId:N}");
}
```

  El registro no cambia (`IdentityRegistration.cs`, hoy :99): `IServiceScopeFactory` ya está en el contenedor.

- [ ] **Paso 5: `SystemSettingsReader` sobre el helper.** Reemplazar `SystemSettingsReader.cs` completo por:

```csharp
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

/// <summary>
/// Como PermissionService, cachea en HybridCache y descarta el valor explícitamente cuando cambia, y como él, la fábrica
/// lee en su propio scope (<see cref="HybridCacheExtensions"/>). Sin fila, devuelve InviteOnly, que es el modo cerrado:
/// ante la duda, el sistema no se abre solo.
/// </summary>
internal sealed class SystemSettingsReader(IServiceScopeFactory scopeFactory, HybridCache cache) : ISystemSettingsReader
{
    public const string CacheKey = "settings:system";

    /// <summary>
    /// Un minuto, y no una hora como los permisos por rol, a propósito: <b>no subirlo</b>. El servicio descarta
    /// el caché después de guardar los ajustes. El TTL acota cuánto puede durar un valor viejo si se cambia la fila
    /// por fuera del servicio o una lectura concurrente se cruza con la invalidación. Un ajuste que se lee una vez
    /// por ingreso no gana nada con una hora de caché.
    /// </summary>
    private static readonly HybridCacheEntryOptions CacheEntryOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(60),
    };

    /// <summary>
    /// Se lee adentro de los límites del ingreso, de Google y del bot: por eso la fábrica corre en un scope propio, con su
    /// contexto y su conexión. El costo en conexiones lo explica <see cref="HybridCacheExtensions"/>.
    /// </summary>
    public async Task<RegistrationMode> GetRegistrationModeAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateInOwnScopeAsync<ApplicationDbContext, RegistrationMode>(
            CacheKey,
            scopeFactory,
            static (db, token) => db.SystemSettings
                .AsNoTracking()
                .Select(settings => settings.RegistrationMode)
                .FirstOrDefaultAsync(token),
            CacheEntryOptions,
            cancellationToken);

    public async Task InvalidateAsync(CancellationToken cancellationToken) =>
        await cache.RemoveAsync(CacheKey, cancellationToken);
}
```

- [ ] **Paso 6: verlo pasar.** V1 (sin advertencias), el comando del Paso 1 (esperado `exit=0`), el del Paso 2 (esperado `exit=0`) y:

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Identity.PermissionServiceTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Settings.SystemSettingsReaderTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.RegistrationModeTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.InitialAdminSignInTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Roles.RoleCrudEndpointsTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`. Incluye `SystemSettingsReaderTests.The_cache_factory_reads_on_its_own_connection_and_never_caches_an_uncommitted_mode`, que ahora pasa por el helper.

- [ ] **Paso 7: el spec de arquitectura.** Verificar la ruta (sección 9 del Diseño). En "Una sola forma de guardar", reemplazar el ítem que empieza con "Un lector con caché que se llama adentro de un límite lee en su propio scope" por:

  ```markdown
  - Toda fábrica de `HybridCache` lee en su propio scope, con `HybridCacheExtensions.GetOrCreateInOwnScopeAsync` (lo verifica `TransactionBoundaryTests`): sobre el contexto de quien llama, adentro de un límite, vería lo que todavía no se confirmó, lo cachearía y le ocuparía la conexión que el rollback necesita para soltar los locks. Por eso `PermissionService` y `SystemSettingsReader` se pueden llamar adentro de un límite. El precio es que, con el caché frío, la fábrica pide una segunda conexión al pool mientras la del límite sigue tomada. Con una fábrica por clave (los ajustes, un minuto; los permisos de cada rol, una hora) alcanza; una fábrica por fila o por pedido puede agotar el pool.
  ```

  En "Tests: arquitectura y arnés", en el ítem de **ArchitectureTests**, reemplazar "el SQL y las claves de los locks viven en un solo lugar, y `ExecuteUpdate`/`ExecuteDelete` solo en la retención." por "el SQL y las claves de los locks viven en un solo lugar, `ExecuteUpdate`/`ExecuteDelete` solo en la retención, y `HybridCache` se llena solo con `HybridCacheExtensions`, que lee en un scope propio."

- [ ] **Paso 8: verificación completa.** V1 a V10.
- [ ] **Paso 9: commit.**

```bash
git add src/ArquitecturaBase.Infrastructure/Caching/HybridCacheExtensions.cs src/ArquitecturaBase.Infrastructure/Identity/PermissionService.cs src/ArquitecturaBase.Infrastructure/Persistence/Readers/SystemSettingsReader.cs tests/ArquitecturaBase.Api.IntegrationTests/Identity/PermissionServiceTests.cs tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs docs/architecture/backend.md
git commit -m "$(cat <<'EOF'
fix: las fábricas de caché leen en su propio scope

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 4: una sola consulta carga la cuenta para modificarla (tarea 4 del plan maestro)

Refactor sin cambio visible: la suite completa confirma el `ConcurrencyStamp`, el bloqueo y los 40P01. Nace `IdentityBoundaryTests` con su primera regla.

**Archivos:**
- Crear: `src/ArquitecturaBase.Infrastructure/Identity/UserManagerExtensions.cs`
- Crear: `tests/ArquitecturaBase.ArchitectureTests/IdentityBoundaryTests.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/UserRepository.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`
- Modificar: el spec de arquitectura (rol; hoy `docs/architecture/backend.md`)

- [ ] **Paso 1: la regla, en rojo.** Crear `tests/ArquitecturaBase.ArchitectureTests/IdentityBoundaryTests.cs`:

```csharp
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
```

  Run: `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj -- --filter-class "ArquitecturaBase.ArchitectureTests.IdentityBoundaryTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; grep -n "Identity\.IdentityS" "$TEMP/etapa2-focus.log" | head -3`
  Esperado: `exit=2`, con `ArquitecturaBase.Infrastructure.Identity.IdentityService` como dueño (su `RequireUserAsync` privado). xUnit corta cada texto del mensaje a los 50 caracteres, así que se ve como `"ArquitecturaBase.Infrastructure.Identity.IdentityS"···`: por eso se busca `Identity\.IdentityS` y no el nombre entero (es el único tipo de ese namespace que empieza así). Verificado sobre una copia de `42a25c1`.

- [ ] **Paso 2: la carga única.** Crear `src/ArquitecturaBase.Infrastructure/Identity/UserManagerExtensions.cs` con el código de la sección 4 del Diseño, tal cual.
- [ ] **Paso 3: `UserRepository` la usa.**

  ```bash
  perl -0777 -pi -e 's/await RequireUserAsync\(/await userManager.RequireUserAsync(/g' src/ArquitecturaBase.Infrastructure/Persistence/Repositories/UserRepository.cs
  ```

  Son 9 sitios. Después, borrar el método privado del final (y la línea en blanco que lo precede):

  ```csharp
      private async Task<ApplicationUser> RequireUserAsync(Guid userId, CancellationToken cancellationToken) =>
          await userManager.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken)
              ?? throw new InvalidOperationException("The user does not exist.");
  ```

- [ ] **Paso 4: `IdentityService` la usa.**

  ```bash
  perl -0777 -pi -e 's/await RequireUserAsync\(/await userManager.RequireUserAsync(/g' src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs
  ```

  Son 6 sitios (`GetRolesAsync`, `RevokeSessionsAsync`, `IsLockedOutAsync`, `RegisterFailedAttemptAsync`, `ResetFailedAttemptsAsync` y `SignInAsync`). Después, borrar el método privado del final (y la línea en blanco que lo precede):

  ```csharp
      private async Task<ApplicationUser> RequireUserAsync(Guid userId, CancellationToken cancellationToken)
      {
          cancellationToken.ThrowIfCancellationRequested();
          var user = await userManager.FindByIdAsync(userId.ToString("D", CultureInfo.InvariantCulture));
          cancellationToken.ThrowIfCancellationRequested();

          // FindByIdAsync puede devolver una entidad borrada que ya está seguida por EF en este scope.
          return user is { IsDeleted: false }
              ? user
              : throw new InvalidOperationException("The user does not exist.");
      }
  ```

  `using System.Globalization;` se queda: lo usa el subject de `RevokeSessionsAsync`.

  Run: `git grep --untracked -n "RequireUserAsync" -- src`
  Esperado: la definición en `UserManagerExtensions.cs` y las 15 llamadas `userManager.RequireUserAsync(` de `UserRepository.cs` e `IdentityService.cs`. (`--untracked` porque `UserManagerExtensions.cs` todavía no está versionado: sin él, `git grep` no lo mira.)

- [ ] **Paso 5: verlo pasar.** V1 (sin advertencias), el comando del Paso 1 (esperado `exit=0`), y:

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Identity.IdentityServiceTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.UserRepositoryTransactionTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.UnitOfWorkTransactionTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.LoginSecurityTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`. `Role_names_are_sorted_and_deleted_users_are_rejected` sigue viendo el `InvalidOperationException` de una cuenta borrada en el mismo scope: ahora lo da el filtro global de la consulta.

  Correr también el comando de "Tiempos de las clases de concurrencia" y comparar con la Tarea 0.

- [ ] **Paso 6: el spec de arquitectura.** Verificar la ruta. En "Tests: arquitectura y arnés", al final del ítem de **ArchitectureTests** (después de "No verifica que cada método que escribe abra un límite (ver [Una sola forma de guardar](#una-sola-forma-de-guardar))."), agregar:

  ```markdown
   `IdentityBoundaryTests`, con la misma lectura del IL, fija lo de Identity: la única carga por Id de una cuenta no borrada para modificarla es `UserManagerExtensions.RequireUserAsync` (nadie llama a `UserManager.FindByIdAsync`; `UserRepository.RestoreAsync`, que carga la borrada, y el seed, que busca al administrador por correo, cargan con su propia consulta).
  ```

- [ ] **Paso 7: verificación completa.** V2 a V10.
- [ ] **Paso 8: commit.**

```bash
git add src/ArquitecturaBase.Infrastructure/Identity/UserManagerExtensions.cs src/ArquitecturaBase.Infrastructure/Persistence/Repositories/UserRepository.cs src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs tests/ArquitecturaBase.ArchitectureTests/IdentityBoundaryTests.cs docs/architecture/backend.md
git commit -m "$(cat <<'EOF'
refactor: una sola consulta carga la cuenta para modificarla

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 5: nace `ISignInService`, con la sesión y el bloqueo de Identity

`SignInService` recibe los 7 cuerpos técnicos; `IdentityService` delega en él y deja de recibir `SignInManager` y los managers de OpenIddict, así hay una sola implementación durante la transición. `FakeIdentityService` declara `ISignInService` sin código nuevo. Nacen `SignInServiceTests`, la cobertura automática de sus reglas de transacción (pendiente f) y dos reglas de IL.

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Interfaces/Integrations/ISignInService.cs`
- Crear: `src/ArquitecturaBase.Infrastructure/Identity/SignInService.cs`
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Identity/SignInServiceTests.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityRegistration.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs` (reemplazo completo)
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Identity/IdentityServiceTests.cs` (salen dos tests)
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UnitOfWorkTransactionTests.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/IdentityBoundaryTests.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`
- Modificar: el spec de arquitectura (rol; hoy `docs/architecture/backend.md`)

- [ ] **Paso 1: los tests del contrato, en rojo.** Crear `tests/ArquitecturaBase.Api.IntegrationTests/Identity/SignInServiceTests.cs`:

```csharp
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Identity;

/// <summary>
/// Lo técnico del ingreso sobre Identity y OpenIddict (ISignInService), contra Postgres. La cookie de la aplicación y la
/// cookie externa necesitan un HttpContext: las cubren por HTTP AuthFlow, LoginLinkTests y ExternalLoginTests. Que cada
/// miembro declare su regla de transacción lo fija UnitOfWorkTransactionTests.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class SignInServiceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tenth_failed_attempt_locks_the_account()
    {
        var user = await CreateAccountAsync("lock");

        var lockedAfterNine = await InTransactionWithSignInAsync(async signIn =>
        {
            for (var i = 0; i < 9; i++)
            {
                await signIn.RegisterFailedAttemptAsync(user.Id, Ct);
            }

            return await signIn.IsLockedOutAsync(user.Id, Ct);
        });

        var lockedAfterTen = await InTransactionWithSignInAsync(async signIn =>
        {
            await signIn.RegisterFailedAttemptAsync(user.Id, Ct);
            return await signIn.IsLockedOutAsync(user.Id, Ct);
        });

        Assert.False(lockedAfterNine);
        Assert.True(lockedAfterTen);
    }

    [Fact]
    public async Task Resetting_failed_attempts_brings_the_count_back_to_zero()
    {
        var user = await CreateAccountAsync("reset");
        await factory.InTransactionAsync(async services =>
        {
            var signIn = services.GetRequiredService<ISignInService>();

            for (var i = 0; i < 9; i++)
            {
                await signIn.RegisterFailedAttemptAsync(user.Id, Ct);
            }
        });
        Assert.Equal(9, await AccessFailedCountAsync(user.Id));

        var locked = await InTransactionWithSignInAsync(async signIn =>
        {
            await signIn.ResetFailedAttemptsAsync(user.Id, Ct);
            await signIn.RegisterFailedAttemptAsync(user.Id, Ct);

            return await signIn.IsLockedOutAsync(user.Id, Ct);
        });

        Assert.False(locked);
        Assert.Equal(1, await AccessFailedCountAsync(user.Id));
    }

    /// <summary>
    /// Las operaciones cargan la cuenta con UserManagerExtensions.RequireUserAsync, la única carga por Id de una cuenta
    /// no borrada para modificarla: una borrada o una que no existe lanzan, también cuando la borrada sigue en el
    /// contexto del mismo scope.
    /// </summary>
    [Fact]
    public async Task Operations_reject_a_deleted_or_missing_account()
    {
        var user = await CreateAccountAsync("deleted");

        await factory.InTransactionAsync(async services =>
        {
            var signIn = services.GetRequiredService<ISignInService>();
            Assert.False(await signIn.IsLockedOutAsync(user.Id, Ct));

            await services.GetRequiredService<IUserRepository>().DeleteAsync(user.Id, Ct);

            await Assert.ThrowsAsync<InvalidOperationException>(() => signIn.IsLockedOutAsync(user.Id, Ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => signIn.IsLockedOutAsync(Guid.CreateVersion7(), Ct));
        });
    }

    /// <summary>
    /// El stamp y las dos revocaciones de OpenIddict van juntos: las revocaciones son UPDATE inmediatos, y sin la
    /// transacción del caso de uso se confirmarían sueltas. Por eso lanza antes de tocar nada, también el stamp.
    /// </summary>
    [Fact]
    public async Task Revoking_sessions_outside_a_transaction_throws_before_touching_the_stamp()
    {
        var user = await CreateAccountAsync("revoke-outside");
        var stampBefore = await SecurityStampOfAsync(user.Id);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<ISignInService>().RevokeSessionsAsync(user.Id, Ct)));

        Assert.Contains(nameof(IUnitOfWork.ExecuteInTransactionAsync), error.Message, StringComparison.Ordinal);
        Assert.Equal(stampBefore, await SecurityStampOfAsync(user.Id));
    }

    private Task<UserAccount> CreateAccountAsync(string prefix) =>
        factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().CreateAsync(
            Email.Create(TestEmails.Unique(prefix)).Value, phone: null, phoneConfirmed: false, displayName: null, "es", Ct));

    private Task<int> AccessFailedCountAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.AccessFailedCount)
            .SingleAsync(Ct));

    private Task<string?> SecurityStampOfAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.SecurityStamp)
            .SingleAsync(Ct));

    private Task<T> InTransactionWithSignInAsync<T>(Func<ISignInService, Task<T>> action) =>
        factory.InTransactionAsync(services => action(services.GetRequiredService<ISignInService>()));
}
```

  En `IdentityServiceTests.cs`, borrar `Tenth_failed_attempt_locks_the_account` y `Revoking_sessions_outside_a_transaction_throws_before_touching_the_stamp` (con su `<summary>`): se mudaron. Borrar también `using ArquitecturaBase.Application.Interfaces.Persistence;`: su único uso era el `nameof(IUnitOfWork.ExecuteInTransactionAsync)` del test que se muda, y quedaría IDE0005.

- [ ] **Paso 2: la cobertura de sus reglas, en rojo.** En `UnitOfWorkTransactionTests.cs`, debajo de `Identity_service_writes_outside_the_boundary_throw_and_change_nothing`, agregar:

```csharp
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

        // Después del commit: adentro de un límite lanzan (lo prueba SignInServiceTests). Vacío hasta la tarea 27 de la
        // Etapa 2.
        string[] afterCommit = [];

        // Leen el bloqueo o tocan solo las cookies de la petición. SignInAsync está acá hasta la tarea 27 de la Etapa 2.
        string[] anywhere =
        [
            nameof(ISignInService.IsLockedOutAsync),
            nameof(ISignInService.SignInAsync),
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
```

  y, debajo de `DisplayNameAsync` (entre los helpers del final), agregar:

```csharp
    private Task<string?> SecurityStampAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.SecurityStamp)
            .SingleAsync(Ct));
```

- [ ] **Paso 3: las reglas de IL, en rojo.** En `IdentityBoundaryTests.cs`:
  - agregar los `using`:

    ```csharp
    using ArquitecturaBase.Application.Interfaces.Integrations;
    using ArquitecturaBase.Application.Models.Identity;
    using ArquitecturaBase.Application.Models.Users.ReadModels;
    ```
  - debajo de la constante `UserRepository`, agregar:

    ```csharp
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
    ```
  - debajo del campo `Calls`, agregar:

    ```csharp
        private static readonly CallSites.TypeUse[] TypeUses =
            [.. Scanned.SelectMany(assembly => CallSites.TypeUses(assembly))];
    ```
  - debajo de `Accounts_are_loaded_with_one_query`, agregar:

    ```csharp
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
    ```
  - al final de la clase, debajo de `IsOn`, agregar:

    ```csharp
        /// <summary>Si <paramref name="type"/> es uno de <paramref name="targets"/> o los lleva adentro (genéricos y arreglos).</summary>
        private static bool Names(Type type, Type[] targets) =>
            targets.Contains(type)
            || (type.IsGenericType && type.GetGenericArguments().Any(argument => Names(argument, targets)))
            || (type.IsArray && Names(type.GetElementType()!, targets));
    ```

  Run: `dotnet build ArquitecturaBase.slnx > "$TEMP/etapa2-build.log" 2>&1; echo "exit=$?"; grep -E "error CS" "$TEMP/etapa2-build.log" | head -5`
  Esperado: `exit=1`, con `ISignInService` sin encontrar (CS0246) en los tests.

- [ ] **Paso 4: el contrato.** Crear `src/ArquitecturaBase.Application/Interfaces/Integrations/ISignInService.cs` con el código de la sección 1.1 del Diseño, con dos cambios, porque hasta la Tarea 27 el código y el enlace todavía escriben la cookie adentro del límite:
  - en el `<remarks>`, reemplazar el primer `<para>` ("Cada miembro sigue una de tres reglas…") y la lista entera por:

    ```csharp
    /// <para>Cada miembro sigue una de dos reglas, y UnitOfWorkTransactionTests exige que todo miembro nuevo declare la
    /// suya:</para>
    /// <list type="bullet">
    /// <item><b>Escribe</b> (<see cref="RegisterFailedAttemptAsync"/>, <see cref="ResetFailedAttemptsAsync"/>,
    /// <see cref="RevokeSessionsAsync"/>): exige la transacción del caso de uso
    /// (<see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/>) y, sin ella, lanza
    /// <see cref="InvalidOperationException"/> antes de tocar nada.</item>
    /// <item><b>En cualquier lado</b> (<see cref="IsLockedOutAsync"/>, <see cref="SignInAsync"/>,
    /// <see cref="GetExternalLoginAsync"/>, <see cref="SignOutExternalAsync"/>): leen, o tocan solo las cookies de la
    /// petición.</item>
    /// </list>
    ```
  - el último `<para>` queda en "El bot de WhatsApp usa solo <see cref="IsLockedOutAsync"/>: un mensaje nunca abre una sesión." (sin la oración de IdentityBoundaryTests);
  - el `<summary>` de `SignInAsync` queda en:

    ```csharp
        /// <summary>
        /// Escribe en la respuesta la cookie persistente de la aplicación. No escribe en la base. La llaman solo los
        /// ingresos (código, enlace y Google); el de Google, después del commit.
        /// </summary>
    ```

- [ ] **Paso 5: la implementación.** Crear `src/ArquitecturaBase.Infrastructure/Identity/SignInService.cs`:

```csharp
using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// ISignInService sobre UserManager, SignInManager y los managers de OpenIddict. Las escrituras (los intentos fallidos y
/// el cierre de sesiones) exigen la transacción del caso de uso con su propio chequeo. La cuenta se carga siempre con
/// <see cref="UserManagerExtensions.RequireUserAsync"/>.
/// </summary>
internal sealed class SignInService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictTokenManager tokenManager,
    ApplicationDbContext dbContext) : ISignInService
{
    public async Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken) =>
        await userManager.IsLockedOutAsync(await userManager.RequireUserAsync(userId, cancellationToken));

    public async Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        (await userManager.AccessFailedAsync(await userManager.RequireUserAsync(userId, cancellationToken)))
            .EnsureSucceeded("register the failed attempt");
    }

    public async Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        (await userManager.ResetAccessFailedCountAsync(await userManager.RequireUserAsync(userId, cancellationToken)))
            .EnsureSucceeded("reset the failed attempts");
    }

    public async Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        // El stamp y las dos revocaciones tienen que ir juntos. Las revocaciones de OpenIddict son UPDATE inmediatos, y
        // sin la transacción del caso de uso se confirmarían sueltas. Los enlaces pendientes los invalida
        // AccountAccessRevoker, que es quien llama.
        dbContext.RequireTransaction();
        var user = await userManager.RequireUserAsync(userId, cancellationToken);

        // La cookie de Identity deja de valer en la próxima petición: el validador del security stamp la rechaza
        // (ValidationInterval está en cero, ver IdentityRegistration).
        (await userManager.UpdateSecurityStampAsync(user)).EnsureSucceeded("renew the security stamp");

        // El subject es el mismo que pone OpenIdPrincipalFactory en el claim "sub".
        var subject = userId.ToString("D", CultureInfo.InvariantCulture);

        // Primero las autorizaciones y después los tokens: si entre las dos llamadas se emitiera un token a partir
        // de una autorización que ya está revocada, la segunda llamada igual lo alcanza. Con
        // EnableTokenEntryValidation, un token revocado deja de valer en el acto, sin esperar a que venza.
        await authorizationManager.RevokeBySubjectAsync(subject, cancellationToken);
        await tokenManager.RevokeBySubjectAsync(subject, cancellationToken);
    }

    public async Task SignInAsync(Guid userId, CancellationToken cancellationToken) =>
        await signInManager.SignInAsync(await userManager.RequireUserAsync(userId, cancellationToken), isPersistent: true);

    public async Task<ExternalLogin?> GetExternalLoginAsync(CancellationToken cancellationToken)
    {
        var info = await signInManager.GetExternalLoginInfoAsync();

        if (info is null)
        {
            return null;
        }

        return new ExternalLogin(
            info.LoginProvider,
            info.ProviderKey,
            info.Principal.FindFirstValue(ClaimTypes.Email),
            string.Equals(info.Principal.FindFirstValue(ExternalClaimTypes.EmailVerified), "true", StringComparison.OrdinalIgnoreCase),
            info.Principal.FindFirstValue(ClaimTypes.Name));
    }

    public Task SignOutExternalAsync(CancellationToken cancellationToken) =>
        signInManager.Context.SignOutAsync(IdentityConstants.ExternalScheme);
}
```

- [ ] **Paso 6: el registro.** En `IdentityRegistration.cs`, debajo de `services.AddScoped<IIdentityService, IdentityService>();`, agregar:

  ```csharp
          services.AddScoped<ISignInService, SignInService>();
  ```

- [ ] **Paso 7: `IdentityService` delega.** Reemplazar `IdentityService.cs` completo por:

```csharp
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Roles.ReadModels;
using ArquitecturaBase.Application.Models.Users.ReadModels;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.AspNetCore.Identity;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Transitorio (Etapa 2): reenvía los datos de cuentas a los lectores y a IUserRepository, y lo técnico del ingreso a
/// ISignInService, mientras los servicios y los tests dejan de pedirlo. Se borra en la tarea 16 del plan de la Etapa 2.
/// </summary>
internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    ISignInService signIn,
    IUserReader userReader,
    IRoleReader roleReader,
    IUserRepository userRepository)
    : IIdentityService
{
    public Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        userReader.FindByIdAsync(userId, cancellationToken);

    public Task<UserAccount?> FindByEmailAsync(Email email, CancellationToken cancellationToken) =>
        userReader.FindByEmailAsync(email, cancellationToken);

    public Task<bool> IsDeletedEmailAsync(Email email, CancellationToken cancellationToken) =>
        userReader.IsDeletedEmailAsync(email, cancellationToken);

    public Task<UserAccount?> FindByExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken) =>
        userReader.FindByExternalLoginAsync(provider, providerKey, cancellationToken);

    public Task<UserAccount?> FindByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        userReader.FindByPhoneAsync(phone, cancellationToken);

    public Task<bool> IsDeletedPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        userReader.IsDeletedPhoneAsync(phone, cancellationToken);

    public Task<UserAccount> CreateAsync(
        Email? email,
        PhoneNumber? phone,
        bool phoneConfirmed,
        string? displayName,
        string culture,
        CancellationToken cancellationToken) =>
        userRepository.CreateAsync(email, phone, phoneConfirmed, displayName, culture, cancellationToken);

    public Task<UserAccount> CreateUnverifiedAsync(
        Email? email,
        PhoneNumber? phone,
        string? displayName,
        string culture,
        CancellationToken cancellationToken) =>
        userRepository.CreateUnverifiedAsync(email, phone, displayName, culture, cancellationToken);

    public Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken) =>
        userRepository.AddExternalLoginAsync(userId, login, cancellationToken);

    public Task<bool> HasExternalLoginAsync(Guid userId, string provider, CancellationToken cancellationToken) =>
        userReader.HasExternalLoginAsync(userId, provider, cancellationToken);

    public Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken) =>
        userRepository.SetPhoneAsync(userId, phone, confirmed, cancellationToken);

    public Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken) =>
        userRepository.RemovePhoneAsync(userId, cancellationToken);

    public Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken) =>
        userRepository.SetEmailAsync(userId, email, confirmed, cancellationToken);

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await userManager.RequireUserAsync(userId, cancellationToken);
        var roles = await userReader.ListRoleNamesForUserAsync(userId, cancellationToken);

        return [.. roles.Order(StringComparer.Ordinal)];
    }

    public Task<UserAccount?> FindDeletedByEmailAsync(Email email, CancellationToken cancellationToken) =>
        userReader.FindDeletedByEmailAsync(email, cancellationToken);

    public Task<UserAccount?> FindDeletedByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        userReader.FindDeletedByPhoneAsync(phone, cancellationToken);

    public Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken) =>
        userRepository.RestoreAsync(userId, displayName, cancellationToken);

    public Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken) =>
        userRepository.SetRolesAsync(userId, roles, cancellationToken);

    public Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
        roleReader.ListRoleNamesAsync(cancellationToken);

    public Task<UserDetail?> FindDetailAsync(Guid userId, CancellationToken cancellationToken) =>
        userReader.FindDetailAsync(userId, cancellationToken);

    public Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken) =>
        userRepository.SetDisplayNameAsync(userId, displayName, cancellationToken);

    public Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken) =>
        userRepository.SetActiveAsync(userId, isActive, cancellationToken);

    public Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken) =>
        signIn.RevokeSessionsAsync(userId, cancellationToken);

    public Task DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
        userRepository.DeleteAsync(userId, cancellationToken);

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        userReader.CountActiveAdminsAsync(cancellationToken);

    public Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken) =>
        signIn.IsLockedOutAsync(userId, cancellationToken);

    public Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken) =>
        signIn.RegisterFailedAttemptAsync(userId, cancellationToken);

    public Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken) =>
        signIn.ResetFailedAttemptsAsync(userId, cancellationToken);

    public Task SignInAsync(Guid userId, CancellationToken cancellationToken) =>
        signIn.SignInAsync(userId, cancellationToken);

    public Task<ExternalLogin?> GetExternalLoginAsync(CancellationToken cancellationToken) =>
        signIn.GetExternalLoginAsync(cancellationToken);

    public Task SignOutExternalAsync(CancellationToken cancellationToken) =>
        signIn.SignOutExternalAsync(cancellationToken);

    public Task<PagedResult<UserListItem>> ListUsersAsync(UserListRequest request, CancellationToken cancellationToken) =>
        userReader.ListUsersAsync(request, cancellationToken);

    public Task<UserFilterCounts> GetUserFilterCountsAsync(UserListRequest request, CancellationToken cancellationToken) =>
        userReader.GetUserFilterCountsAsync(request, cancellationToken);

    public Task<IReadOnlyCollection<RoleListItem>> ListRolesAsync(CancellationToken cancellationToken) =>
        roleReader.ListRolesAsync(cancellationToken);

    public Task<RoleListItem?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        roleReader.FindRoleAsync(roleId, cancellationToken);

    public Task<bool> RoleNameExistsAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken) =>
        roleReader.RoleNameExistsAsync(name, excludedRoleId, cancellationToken);
}
```

  `StaleIdentityReads` arma este `IdentityService` con `ActivatorUtilities`, que resuelve el constructor nuevo solo.

- [ ] **Paso 8: el doble declara el contrato.** En `FakeIdentityService.cs`, la declaración pasa a:

  ```csharp
  internal sealed class FakeIdentityService : IIdentityService, IUserReader, IUserRepository, ISignInService
  ```

  No lleva código nuevo: ya tiene los 7 métodos con la misma firma.

- [ ] **Paso 9: verlo pasar.** V1 (sin advertencias) y:

  Run: `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj --no-build > "$TEMP/etapa2-arch.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-arch.log"`
  Esperado: `exit=0`, con las tres reglas de `IdentityBoundaryTests`.

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Identity.SignInServiceTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Identity.IdentityServiceTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.UnitOfWorkTransactionTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`, con los 4 de `SignInServiceTests` y `Sign_in_service_follows_its_transaction_rules`.

- [ ] **Paso 10: el spec de arquitectura.** Verificar la ruta. En "Tests: arquitectura y arnés", reemplazar la oración que agregó la Tarea 4 (" `IdentityBoundaryTests`, con la misma lectura del IL, fija lo de Identity: …") por:

  ```markdown
   `IdentityBoundaryTests`, con la misma lectura del IL, fija lo de Identity: la única carga por Id de una cuenta no borrada para modificarla es `UserManagerExtensions.RequireUserAsync` (nadie llama a `UserManager.FindByIdAsync`; `UserRepository.RestoreAsync`, que carga la borrada, y el seed, que busca al administrador por correo, cargan con su propia consulta); `ISignInService` tiene como máximo 12 miembros, ninguno de datos de cuentas, y solo `SignInService` toca `SignInManager`, el bloqueo, el security stamp y las revocaciones por sujeto de OpenIddict.
  ```

- [ ] **Paso 11: verificación completa.** V2 a V10.
- [ ] **Paso 12: commit.**

```bash
git add src/ArquitecturaBase.Application/Interfaces/Integrations/ISignInService.cs src/ArquitecturaBase.Infrastructure/Identity/SignInService.cs src/ArquitecturaBase.Infrastructure/Identity/IdentityRegistration.cs src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs tests/ArquitecturaBase.Api.IntegrationTests/Identity/SignInServiceTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Identity/IdentityServiceTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UnitOfWorkTransactionTests.cs tests/ArquitecturaBase.ArchitectureTests/IdentityBoundaryTests.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs docs/architecture/backend.md
git commit -m "$(cat <<'EOF'
feat: ISignInService con la sesión y el bloqueo de Identity

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 6: `AccountAccessRevoker` cierra sesiones por `ISignInService`

Refactor mecánico, sin paso rojo: los tests siguen pasando el mismo doble, que desde la Tarea 5 también es `ISignInService`.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/Users/AccountAccessRevoker.cs`

- [ ] **Paso 1: el constructor y la llamada.** En `AccountAccessRevoker.cs`:
  - `    ILoginLinkRepository loginLinks, IIdentityService identity, TimeProvider timeProvider)` pasa a `    ILoginLinkRepository loginLinks, ISignInService signIn, TimeProvider timeProvider)`;
  - `        await identity.RevokeSessionsAsync(userId, cancellationToken);` pasa a `        await signIn.RevokeSessionsAsync(userId, cancellationToken);`.

  El `using ArquitecturaBase.Application.Interfaces.Integrations;` se queda (`ISignInService` vive ahí).

- [ ] **Paso 2: verlo pasar.** V1 (sin advertencias) y:

  Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj --no-build -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.Users.AccountAccessRevokerTests" --filter-class "ArquitecturaBase.Application.UnitTests.Services.Users.UserServiceStatusTests" --filter-class "ArquitecturaBase.Application.UnitTests.Services.Users.UserServicePhoneTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`.

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UsersEndpointsTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UnlinkUserPhoneEndpointTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.LoginLinkTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0` (incluye `Cutting_off_the_access_invalidates_the_pending_links`).

- [ ] **Paso 3: verificación completa.** V2 a V10.
- [ ] **Paso 4: commit.**

```bash
git add src/ArquitecturaBase.Application/Services/Users/AccountAccessRevoker.cs
git commit -m "$(cat <<'EOF'
refactor: AccountAccessRevoker cierra sesiones por ISignInService

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 7: `ExternalLoginService` usa `ISignInService`

Solo cambia el tipo de su primer parámetro: los datos ya los lee y escribe por `IUserReader` e `IUserRepository`. `GetExternalLoginAsync` y `SignOutExternalAsync` siguen adentro del límite (tocan solo la cookie externa; si el commit falla, `UseExceptionHandler` descarta la respuesta), y la cookie de la aplicación, después.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/Auth/ExternalLoginService.cs`

- [ ] **Paso 1: el reemplazo.**

  ```bash
  perl -0777 -pi -e 's/IIdentityService identity,/ISignInService signIn,/; s/\bidentity\./signIn./g' src/ArquitecturaBase.Application/Services/Auth/ExternalLoginService.cs
  ```

  Run: `git grep -nE "identity\b|signIn" -- src/ArquitecturaBase.Application/Services/Auth/ExternalLoginService.cs`
  Esperado: el parámetro `ISignInService signIn,` y cuatro llamadas: `signIn.SignInAsync` (después del límite), `signIn.GetExternalLoginAsync`, `signIn.SignOutExternalAsync` y `signIn.IsLockedOutAsync`.

- [ ] **Paso 2: verlo pasar.** V1 y:

  Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj --no-build -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.Auth.ExternalLoginServiceTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`.

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.ExternalLoginTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`.

- [ ] **Paso 3: verificación completa.** V2 a V10.
- [ ] **Paso 4: commit.**

```bash
git add src/ArquitecturaBase.Application/Services/Auth/ExternalLoginService.cs
git commit -m "$(cat <<'EOF'
refactor: ExternalLoginService usa ISignInService

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 8: `LoginLinkService` lee por `IUserReader` y usa `ISignInService`

Mecánico: el orden del canje (hash, `FindUserIdAsync` sin seguimiento, lock `login-link:`, relectura del enlace, cuenta después del lock) no cambia, y la cookie sigue adentro hasta la Tarea 27. `LoginLinkService` no tiene tests unitarios; lo resuelve la DI.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/Auth/LoginLinkService.cs`

- [ ] **Paso 1: el reemplazo.**

  ```bash
  perl -0777 -pi -e 's/    IIdentityService identityService,(\r?\n)/    IUserReader users,$1    ISignInService signIn,$1/; s/identityService\.FindByIdAsync/users.FindByIdAsync/g; s/identityService\./signIn./g' src/ArquitecturaBase.Application/Services/Auth/LoginLinkService.cs
  ```

  El `(\r?\n)` captura el final de línea que tenga el archivo (en el árbol de trabajo son CRLF, en el índice LF) y lo repite en las líneas nuevas.

  Run: `git grep -nE "identityService|users\.|signIn\." -- src/ArquitecturaBase.Application/Services/Auth/LoginLinkService.cs`
  Esperado: sin `identityService`; `users.FindByIdAsync` dos veces (vista previa y canje); `signIn.IsLockedOutAsync`, `signIn.ResetFailedAttemptsAsync` y `signIn.SignInAsync` una vez cada uno.

- [ ] **Paso 2: verlo pasar.** V1 y:

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.LoginLinkTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Contracts.WhatsAppRouteContractsTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Contracts.AuthConnectAccountContractTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.MeWhatsAppEndpointsTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`. Son las clases que canjean por `POST /account/login-link/redeem`.

  Correr también el comando de "Tiempos de las clases de concurrencia".

- [ ] **Paso 3: verificación completa.** V2 a V10.
- [ ] **Paso 4: commit.**

```bash
git add src/ArquitecturaBase.Application/Services/Auth/LoginLinkService.cs
git commit -m "$(cat <<'EOF'
refactor: LoginLinkService lee por IUserReader y usa ISignInService

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 9: `AccountService` busca cuentas por `IUserReader`

Mecánico: las dos búsquedas siguen después del lock `login-code:` que toma `LoginCodeIssuer`. `StaleIdentityReads` ya esconde esas búsquedas por `nameof(IUserReader.*)` y envuelve también `IUserReader`, así que la sonda sigue sirviendo.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/Auth/AccountService.cs`

- [ ] **Paso 1: el reemplazo.**

  ```bash
  perl -0777 -pi -e 's/IIdentityService identityService,/IUserReader users,/; s/identityService\.FindBy/users.FindBy/g' src/ArquitecturaBase.Application/Services/Auth/AccountService.cs
  ```

  Run: `git grep -nE "identityService|users\." -- src/ArquitecturaBase.Application/Services/Auth/AccountService.cs`
  Esperado: sin `identityService`; `users.FindByEmailAsync` y `users.FindByPhoneAsync`.

- [ ] **Paso 2: verlo pasar.** V1 (los unitarios compilan sin tocarlos: pasan `FakeIdentityService`, que es `IUserReader`) y:

  Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj --no-build -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.Auth.AccountServiceTests" --filter-class "ArquitecturaBase.Application.UnitTests.Services.Auth.RequestLoginCodeServiceTests" --filter-class "ArquitecturaBase.Application.UnitTests.Services.Auth.RequestWhatsAppLoginCodeServiceTests" --filter-class "ArquitecturaBase.Application.UnitTests.Services.Auth.VerifyLoginCodeServiceTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`.

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-namespace "ArquitecturaBase.Api.IntegrationTests.Auth" > "$TEMP/etapa2-it-auth.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-it-auth.log"`
  Esperado: `exit=0`.

- [ ] **Paso 3: verificación completa.** V2, V3, V4 y V6 a V10 (V5 ya corrió).
- [ ] **Paso 4: commit.**

```bash
git add src/ArquitecturaBase.Application/Services/Auth/AccountService.cs
git commit -m "$(cat <<'EOF'
refactor: AccountService busca cuentas por IUserReader

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 10: `LoginCodeVerifier` y sus identificadores usan `IUserReader`, `IUserRepository` e `ISignInService`

Mecánico: el orden del verify (identificador, lock del destino, cuenta después del lock, bloqueo, código, intento fallido, alta o confirmación, reset, cookie, auditoría) no cambia. El constructor pasa de 7 a 9 parámetros, y los 5 tests que lo arman pasan el mismo doble tres veces.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/Auth/LoginCodeVerifier.cs` (reemplazo completo)
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Auth/{VerifyLoginCodeServiceTests,LoginCodeVerifierParityTests,AccountServiceTests,RequestLoginCodeServiceTests,RequestWhatsAppLoginCodeServiceTests}.cs`

- [ ] **Paso 1: el verifier.** Reemplazar `LoginCodeVerifier.cs` completo por:

```csharp
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Services.Auth;

/// <summary>
/// Verifica el código que llegó por correo o por WhatsApp e inicia la sesión. Los dos canales recorren el mismo camino
/// (sección 10 del spec del ingreso con WhatsApp); lo que cambia entre uno y otro lo sabe <see cref="SignInIdentifier"/>.
/// </summary>
internal sealed class LoginCodeVerifier(
    ILoginCodeRepository loginCodes,
    ILoginAuditRepository loginAudits,
    IUserReader users,
    IUserRepository userRepository,
    ISignInService signIn,
    ILoginCodeHasher codeHasher,
    AccountCreationPolicy accountCreation,
    IRequestInfo requestInfo,
    TimeProvider timeProvider)
{
    public async Task<Result<VerifyLoginCodeResponse>> VerifyAsync(VerifyLoginCodeRequest request, CancellationToken cancellationToken)
    {
        var identifierResult = SignInIdentifier.From(request, users, userRepository);

        if (identifierResult.IsFailure)
        {
            return identifierResult.Error;
        }

        var identifier = identifierResult.Value;

        // Los límites de la sección 5.3 se aplican de a un request por destino.
        await loginCodes.LockDestinationAsync(identifier.Destination, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var user = await identifier.FindAccountAsync(cancellationToken);

        if (user is not null && await signIn.IsLockedOutAsync(user.Id, cancellationToken))
        {
            return Fail(identifier, user, AccountErrors.LockedOut, nowUtc);
        }

        // Sin un código de ingreso para ese destino, el error es el mismo que el de un código incorrecto. Uno pedido
        // desde el perfil para vincular el correo o el número no sirve para entrar.
        var loginCode = await loginCodes.GetLatestAsync(
            identifier.Destination, LoginCodePurpose.SignIn, requestedByUserId: null, cancellationToken);
        var verification = loginCode?.Verify(
                codeHasher.Hash(identifier.Destination, LoginCodePurpose.SignIn, request.Code!), nowUtc)
            ?? Result.Failure(LoginCodeErrors.Invalid(attemptsLeft: null));

        if (verification.IsFailure)
        {
            if (user is not null)
            {
                await signIn.RegisterFailedAttemptAsync(user.Id, cancellationToken);
            }

            return Fail(identifier, user, verification.Error, nowUtc);
        }

        if (user is null)
        {
            var created = await CreateAccountAsync(identifier, cancellationToken);

            if (created.IsFailure)
            {
                return Fail(identifier, user: null, created.Error, nowUtc);
            }

            user = created.Value;
        }
        else
        {
            await identifier.ConfirmAsync(user, cancellationToken);
        }

        // Se informa recién ahora: el usuario ya probó que el correo o el número es suyo.
        if (!user.IsActive)
        {
            return Fail(identifier, user, AccountErrors.Disabled, nowUtc);
        }

        await signIn.ResetFailedAttemptsAsync(user.Id, cancellationToken);
        await signIn.SignInAsync(user.Id, cancellationToken);

        loginAudits.Add(LoginAudit.Success(
            identifier.Destination.Value, user.Id, identifier.Method, requestInfo.IpAddress, requestInfo.UserAgent, nowUtc));

        return new VerifyLoginCodeResponse(request.ReturnUrl!);
    }

    /// <summary>
    /// La cuenta de quien acaba de probar con el código que el correo o el número es suyo y todavía no tiene una. Como
    /// el código ya se verificó, los rechazos se pueden decir con todas las letras, igual que con Google y en el mismo
    /// orden: primero si el registro permite crearla y después la cuenta borrada.
    /// </summary>
    private async Task<Result<UserAccount>> CreateAccountAsync(SignInIdentifier identifier, CancellationToken cancellationToken)
    {
        // En Open, cualquiera; en InviteOnly, solo el administrador inicial (AccountCreationPolicy). Se mira acá y no
        // solo en el pedido: InviteOnly se sostenía solo porque el pedido no le manda el código a un destino sin
        // cuenta, y con dos canales esa defensa no alcanza (hallazgo 1 de la etapa 1, sección 10 del spec del ingreso
        // con WhatsApp).
        if (!await accountCreation.AllowsNewAccountAsync(identifier.Email, cancellationToken))
        {
            return AccountErrors.NotInvited;
        }

        // Una cuenta borrada no aparece en ninguna búsqueda, así que sin esto se intentaría crear otra con el mismo
        // correo o número y el índice único la rechazaría con un 500. Se informa como cuenta deshabilitada, que es lo
        // que es.
        if (await identifier.BelongsToDeletedAccountAsync(cancellationToken))
        {
            return AccountErrors.Disabled;
        }

        return await identifier.CreateAccountAsync(UserCultures.FromCurrentRequest(), cancellationToken);
    }

    private Error Fail(SignInIdentifier identifier, UserAccount? user, Error error, DateTime nowUtc)
    {
        loginAudits.Add(LoginAudit.Failure(
            identifier.Destination.Value, user?.Id, identifier.Method, error.Code, requestInfo.IpAddress, requestInfo.UserAgent, nowUtc));

        return error;
    }

    /// <summary>
    /// Con qué se presenta la persona: el correo o el número. Cada uno sabe su destino (el del lock, el código y la
    /// auditoría), su método de ingreso, cómo se busca su cuenta, cómo se reconoce una cuenta borrada y cómo se crea
    /// una. El resto del ingreso es el mismo para los dos.
    /// </summary>
    private abstract class SignInIdentifier(LoginCodeDestination destination, LoginMethod method)
    {
        /// <summary>El correo normalizado o el número en formato internacional: lo que queda en la auditoría.</summary>
        public LoginCodeDestination Destination { get; } = destination;

        public LoginMethod Method { get; } = method;

        /// <summary>
        /// El correo con el que se crearía la cuenta, o null si la persona se presenta con el número. Es lo que mira
        /// <see cref="AccountCreationPolicy"/> para reconocer al administrador inicial.
        /// </summary>
        public abstract Email? Email { get; }

        /// <summary>Con el número si vino (el validador ya controló que venga uno solo); si no, con el correo.</summary>
        public static Result<SignInIdentifier> From(
            VerifyLoginCodeRequest request, IUserReader reader, IUserRepository repository)
        {
            if (request.IsByPhone)
            {
                var phone = PhoneNumber.Create(request.Phone);

                return phone.IsSuccess ? new PhoneIdentifier(phone.Value, reader, repository) : phone.Error;
            }

            var email = Email.Create(request.Email);

            return email.IsSuccess ? new EmailIdentifier(email.Value, reader, repository) : email.Error;
        }

        public abstract Task<UserAccount?> FindAccountAsync(CancellationToken cancellationToken);

        public abstract Task<bool> BelongsToDeletedAccountAsync(CancellationToken cancellationToken);

        public abstract Task<UserAccount> CreateAccountAsync(string culture, CancellationToken cancellationToken);

        /// <summary>Lo que cambia en una cuenta que ya existía, ahora que la persona probó que el destino es suyo.</summary>
        public virtual Task ConfirmAsync(UserAccount user, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// El correo: el alta lo deja verificado, y el que cargó un administrador queda verificado cuando la persona entra con
    /// él (sección 6.1 del spec del ingreso con WhatsApp), como el número.
    /// </summary>
    private sealed class EmailIdentifier(Email email, IUserReader reader, IUserRepository repository)
        : SignInIdentifier(LoginCodeDestination.ForEmail(email), LoginMethod.Code)
    {
        public override Email? Email => email;

        public override Task<UserAccount?> FindAccountAsync(CancellationToken cancellationToken) =>
            reader.FindByEmailAsync(email, cancellationToken);

        public override Task<bool> BelongsToDeletedAccountAsync(CancellationToken cancellationToken) =>
            reader.IsDeletedEmailAsync(email, cancellationToken);

        public override Task<UserAccount> CreateAccountAsync(string culture, CancellationToken cancellationToken) =>
            repository.CreateAsync(email, phone: null, phoneConfirmed: false, displayName: null, culture, cancellationToken);

        public override Task ConfirmAsync(UserAccount user, CancellationToken cancellationToken) =>
            user.EmailConfirmed
                ? Task.CompletedTask
                : repository.SetEmailAsync(user.Id, email, confirmed: true, cancellationToken);
    }

    /// <summary>
    /// El número de WhatsApp: el alta crea una cuenta sin correo, con el número verificado, y el número que cargó un
    /// administrador queda verificado cuando la persona entra con él (sección 6.1 del spec del ingreso con WhatsApp).
    /// </summary>
    private sealed class PhoneIdentifier(PhoneNumber phone, IUserReader reader, IUserRepository repository)
        : SignInIdentifier(LoginCodeDestination.ForPhone(phone), LoginMethod.WhatsAppCode)
    {
        // Sin correo: el número nunca es el del administrador inicial, así que solo Open le crea la cuenta.
        public override Email? Email => null;

        public override Task<UserAccount?> FindAccountAsync(CancellationToken cancellationToken) =>
            reader.FindByPhoneAsync(phone, cancellationToken);

        public override Task<bool> BelongsToDeletedAccountAsync(CancellationToken cancellationToken) =>
            reader.IsDeletedPhoneAsync(phone, cancellationToken);

        public override Task<UserAccount> CreateAccountAsync(string culture, CancellationToken cancellationToken) =>
            repository.CreateAsync(email: null, phone, phoneConfirmed: true, displayName: null, culture, cancellationToken);

        public override Task ConfirmAsync(UserAccount user, CancellationToken cancellationToken) =>
            user.PhoneNumberConfirmed
                ? Task.CompletedTask
                : repository.SetPhoneAsync(user.Id, phone, confirmed: true, cancellationToken);
    }
}
```

- [ ] **Paso 2: los tests que lo arman.** En cada archivo, la construcción del verifier pasa el mismo doble en los tres lugares (lector, repositorio y sesión):
  - `VerifyLoginCodeServiceTests.cs`: reemplazar

    ```csharp
                    new LoginCodeVerifier(
                        Codes, Audits, Identity, new FakeLoginCodeHasher(), accountCreation, new FakeRequestInfo(), Clock),
    ```

    por

    ```csharp
                    new LoginCodeVerifier(
                        Codes,
                        Audits,
                        Identity,
                        Identity,
                        Identity,
                        new FakeLoginCodeHasher(),
                        accountCreation,
                        new FakeRequestInfo(),
                        Clock),
    ```
  - `LoginCodeVerifierParityTests.cs`: en el constructor, debajo de la línea `            _identity,` que sigue a `            _audits,`, agregar dos líneas más `            _identity,`.
  - `AccountServiceTests.cs`: en `new LoginCodeVerifier(`, debajo de `            identity,`, agregar dos líneas más `            identity,`. El `identity,` que va después, como argumento de `new AccountService(`, no se toca.
  - `RequestLoginCodeServiceTests.cs` y `RequestWhatsAppLoginCodeServiceTests.cs`: en `new LoginCodeVerifier(`, debajo de `                    Identity,`, agregar dos líneas más `                    Identity,`. El `Identity,` argumento de `new AccountService(` no se toca.

- [ ] **Paso 3: verlo pasar.** V1 (sin advertencias) y:

  Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj --no-build > "$TEMP/etapa2-unit.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-unit.log"`
  Esperado: `exit=0`.

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-namespace "ArquitecturaBase.Api.IntegrationTests.Auth" > "$TEMP/etapa2-it-auth.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-it-auth.log"`
  Esperado: `exit=0`.

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.CreateUserWithPhoneTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.MeEmailEndpointsTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`: los 409 de `StaleIdentityReads` siguen afirmando que la sonda escondió al menos una búsqueda.

- [ ] **Paso 4: verificación completa.** V2, V4 y V6 a V10.
- [ ] **Paso 5: commit.**

```bash
git add src/ArquitecturaBase.Application/Services/Auth/LoginCodeVerifier.cs tests/ArquitecturaBase.Application.UnitTests/Services/Auth/VerifyLoginCodeServiceTests.cs tests/ArquitecturaBase.Application.UnitTests/Services/Auth/LoginCodeVerifierParityTests.cs tests/ArquitecturaBase.Application.UnitTests/Services/Auth/AccountServiceTests.cs tests/ArquitecturaBase.Application.UnitTests/Services/Auth/RequestLoginCodeServiceTests.cs tests/ArquitecturaBase.Application.UnitTests/Services/Auth/RequestWhatsAppLoginCodeServiceTests.cs
git commit -m "$(cat <<'EOF'
refactor: LoginCodeVerifier usa IUserReader, IUserRepository e ISignInService

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 11: el bot usa `IUserReader`, `IUserRepository` e `ISignInService`

Mecánico: no reordena lecturas ni locks. `ISignInService` lo recibe solo por `IsLockedOutAsync`; desde la Tarea 16 una regla de IL prohíbe que el bot llame a `SignInAsync`, y desde la 27, también la guarda en tiempo de ejecución. Su método privado `SignInAsync`, que solo emite un enlace, pasa a llamarse `SendLoginLinkAsync`: con el nombre viejo, una búsqueda de `SignInAsync` o una regla de IL lo confundirían con la sesión.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/WhatsApp/WhatsAppInboundService.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/WhatsApp/WhatsAppInboundServiceTests.cs`

- [ ] **Paso 1: el reemplazo en el bot.**

  ```bash
  perl -0777 -pi -e 's/    IIdentityService identityService,(\r?\n)/    IUserReader users,$1    IUserRepository userRepository,$1    ISignInService signIn,$1/; s/identityService\.IsLockedOutAsync/signIn.IsLockedOutAsync/g; s/identityService\.(SetPhoneAsync|CreateAsync)/userRepository.$1/g; s/identityService\./users./g; s/\? await SignInAsync\(contact, phone, account, cancellationToken\)/? await SendLoginLinkAsync(contact, phone, account, cancellationToken)/; s/private async Task<WhatsAppOutboundMessage> SignInAsync\(/private async Task<WhatsAppOutboundMessage> SendLoginLinkAsync(/' src/ArquitecturaBase.Application/Services/WhatsApp/WhatsAppInboundService.cs
  ```

  Run: `git grep -nE "identityService|users\.|userRepository\.|signIn\.|SignInAsync|SendLoginLinkAsync" -- src/ArquitecturaBase.Application/Services/WhatsApp/WhatsAppInboundService.cs`
  Esperado: sin `identityService` ni `SignInAsync`; `signIn.IsLockedOutAsync` (1); `users.FindDeletedByPhoneAsync` (1), `users.FindByIdAsync` (1) y `users.FindByPhoneAsync` (2); `userRepository.SetPhoneAsync` (1) y `userRepository.CreateAsync` (1); `SendLoginLinkAsync` en la llamada y en la definición.

- [ ] **Paso 2: el test que lo arma.** En `WhatsAppInboundServiceTests.cs`, en `Service()`, la línea `            _identity,` (el cuarto argumento, entre `_messages,` y `new FakePhoneNumberParser(),`) pasa a tres líneas `            _identity,`.

- [ ] **Paso 3: verlo pasar.** V1 y:

  Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj --no-build -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.WhatsApp.WhatsAppInboundServiceTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`, con las dos aserciones de la regla de oro (`Assert.Empty(_identity.SignedInUsers)`).

  V7 (WhatsApp) y el comando de "Tiempos de las clases de concurrencia".

- [ ] **Paso 4: verificación completa.** V2 a V6 y V8 a V10.
- [ ] **Paso 5: commit.**

```bash
git add src/ArquitecturaBase.Application/Services/WhatsApp/WhatsAppInboundService.cs tests/ArquitecturaBase.Application.UnitTests/Services/WhatsApp/WhatsAppInboundServiceTests.cs
git commit -m "$(cat <<'EOF'
refactor: el bot usa IUserReader, IUserRepository e ISignInService

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 12: el bot relee la cuenta vinculada después de tomar su lock (decisión 6)

Un `fix`. `FindAccountAsync` lee la cuenta del contacto vinculado antes de `LockAccountAsync` y no la vuelve a leer. El comentario lo justifica para el perfil, que toma primero la fila del contacto, pero la administración no: `UserStatusOperations.SetActiveAsync(false)` y `DeleteAsync` toman solo `login-link:`. Si un administrador desactiva la cuenta mientras el bot espera ese lock, el bot decide con la lectura vieja y emite un enlace después del corte, y ese enlace sirve si la reactivan dentro de sus 10 minutos, justo lo que `AccountAccessRevoker` dice evitar. Si la borra, en cambio, no hay fuga: con la lectura vieja, `IsLockedOutAsync` carga la cuenta con `RequireUserAsync`, que lanza porque ya no existe; la unidad del bot se deshace sin enlace y el mensaje espera a la vuelta siguiente, que ya contesta Disabled. Ese caso es de robustez, no de seguridad. El arreglo: leer, tomar el lock y releer; si la relectura da null (la borraron mientras esperaba), seguir por el camino del número, que la reconoce como borrada y contesta Disabled en el acto. No hay deadlock posible: ningún otro flujo toma dos locks `login-link:` de cuentas, y una cuenta borrada no aparece por número.

**Archivos:**
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/WhatsApp/WhatsAppInboundServiceTests.cs`
- Modificar: `src/ArquitecturaBase.Application/Services/WhatsApp/WhatsAppInboundService.cs`

- [ ] **Paso 1: el test, en rojo.** En `WhatsAppInboundServiceTests.cs`, debajo de `An_account_that_lets_go_of_the_number_while_the_bot_waits_for_it_gets_neither_a_link_nor_the_chat`, agregar:

```csharp
    /// <summary>
    /// El bot lee la cuenta del contacto vinculado antes de tener su lock, y mientras lo espera un administrador la
    /// puede desactivar o borrar (UserStatusOperations toma el mismo lock login-link:, pero no el contacto).
    /// Desactivada: con la lectura de antes, el bot mandaría un enlace después del corte, y ese enlace serviría si la
    /// reactivan dentro de sus 10 minutos. Borrada: con la lectura de antes, IsLockedOutAsync lanza porque la cuenta ya
    /// no existe, la unidad se deshace y el mensaje espera a la vuelta siguiente. Con la relectura, las dos contestan
    /// Disabled en el acto; la borrada, por el camino del número, que la reconoce como borrada. El IsLockedOutAsync del
    /// doble no mira las cuentas y no lanza: por eso, antes del arreglo, el caso "deleted" también sale en rojo con un
    /// enlace.
    /// </summary>
    [Theory]
    [InlineData("disabled")]
    [InlineData("deleted")]
    public async Task A_linked_account_cut_off_while_the_bot_waits_for_its_lock_gets_no_link(string state)
    {
        var ana = await AccountWithPhoneAsync("Ana", confirmed: true);
        var contact = Contact("Ana");
        contact.LinkUser(ana.Id);
        var hola = Text(contact, "Hola");
        _loginLinks.WhileWaitingForTheLock = userId => PutInStateAsync(userId, state);

        await HandleAsync(contact);

        Assert.Equal(Disabled, Assert.IsType<WhatsAppTextMessage>(Assert.Single(_outbox.Messages)).Body);
        Assert.Empty(_loginLinks.Links);
        Assert.Equal(ana.Id, contact.UserId);
        Assert.Equal(Now, hola.ProcessedAtUtc);
    }
```

  Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-method "ArquitecturaBase.Application.UnitTests.Services.WhatsApp.WhatsAppInboundServiceTests.A_linked_account_cut_off_while_the_bot_waits_for_its_lock_gets_no_link" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; grep -nE "IsType|WhatsAppLinkButtonMessage" "$TEMP/etapa2-focus.log" | head -4`
  Esperado: `exit=2`, los dos casos: la respuesta es un `WhatsAppLinkButtonMessage` (el enlace) y no el texto de cuenta deshabilitada. El caso `deleted` sale en rojo por el doble, cuyo `IsLockedOutAsync` no mira las cuentas y no lanza con una borrada; en producción `IsLockedOutAsync` lanzaría (sección 1.1) y no saldría el enlace. El que prueba el hallazgo es `disabled`: si ese pasa, frenar: el hallazgo no se reproduce y, por la decisión 6, no se toca el bot.

- [ ] **Paso 2: releer después del lock.** En `WhatsAppInboundService.cs`, reemplazar el primer bloque de `FindAccountAsync`:

```csharp
        if (contact.UserId is { } linkedUserId
            && await users.FindByIdAsync(linkedUserId, cancellationToken) is { } linked)
        {
            // Para cambiarle el número a la cuenta, el perfil toma antes la fila de su contacto, que es este y lo tiene el
            // bot: espera a que el bot termine, así que la cuenta sigue siendo la de este chat.
            await accountLocks.LockAccountAsync(linked.Id, cancellationToken);

            return linked;
        }
```

  por:

```csharp
        if (contact.UserId is { } linkedUserId
            && await users.FindByIdAsync(linkedUserId, cancellationToken) is { } linked)
        {
            // Para cambiarle el número a la cuenta, el perfil toma antes la fila de su contacto, que es este y lo tiene el
            // bot: espera a que el bot termine, así que la cuenta sigue siendo la de este chat.
            await accountLocks.LockAccountAsync(linked.Id, cancellationToken);

            // La administración, en cambio, no toma el contacto: desactivar o borrar la cuenta toma solo este lock, así
            // que mientras el bot lo esperaba pudo cortarle el acceso. Se vuelve a leer (la consulta va a la base y ve
            // lo que la administración ya confirmó): con la lectura de antes, a una cuenta desactivada le mandaría un
            // enlace después del corte. Si la borraron, sigue por el número, que la reconoce como borrada.
            if (await users.FindByIdAsync(linked.Id, cancellationToken) is { } current)
            {
                return current;
            }
        }
```

- [ ] **Paso 3: verlo pasar.** El comando del Paso 1: esperado `exit=0` en los dos casos. Después:

  Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.WhatsApp.WhatsAppInboundServiceTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`, incluido `The_account_of_a_linked_contact_is_found_before_looking_at_the_number`.

  V1, V7 (WhatsApp), V6 (Users, por la administración) y el comando de "Tiempos de las clases de concurrencia" (las suites de 40P01 y `NOWAIT`).

- [ ] **Paso 4: verificación completa.** V2 a V5 y V8 a V10.
- [ ] **Paso 5: commit.**

```bash
git add src/ArquitecturaBase.Application/Services/WhatsApp/WhatsAppInboundService.cs tests/ArquitecturaBase.Application.UnitTests/Services/WhatsApp/WhatsAppInboundServiceTests.cs
git commit -m "$(cat <<'EOF'
fix: el bot relee la cuenta vinculada después de tomar su lock

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 13: los tests de usuarios arman por `IUserRepository` y leen por `IUserReader`

Solo tests: `IIdentityService` todavía existe, y estos archivos dejan de pedirlo. Nace la forma corta del alta sobre `IUserRepository`.

**Regla de reemplazo** (vale también para la Tarea 14): cada `GetRequiredService<IIdentityService>()` pasa a `GetRequiredService<IUserReader>()` si lo que se llama sobre él (en la misma línea o en la siguiente) es una búsqueda `Find…`, y a `GetRequiredService<IUserRepository>()` en todos los demás casos (`CreateAsync`, `CreateUnverifiedAsync`, `SetEmailAsync`, `SetPhoneAsync`, `RemovePhoneAsync`, `AddExternalLoginAsync`, `SetActiveAsync`, `SetRolesAsync`, `RestoreAsync` y `DeleteAsync`). En estos archivos no hay otras lecturas por `IIdentityService` (verificado en `42a25c1`).

**Archivos:**
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Support/UserRepositoryExtensions.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Support/AdminUsersApi.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Users/{AccountsWithPhoneTests,MeEmailEndpointsTests,MeWhatsAppEndpointsTests,UnlinkUserPhoneEndpointTests,UpdateUserContactTests,UserSoftDeleteTests,UsersEndpointsTests}.cs`

- [ ] **Paso 1: la forma corta.** Crear `tests/ArquitecturaBase.Api.IntegrationTests/Support/UserRepositoryExtensions.cs`:

```csharp
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

internal static class UserRepositoryExtensions
{
    /// <summary>
    /// El alta con solo correo, como la hacían las fases anteriores. Casi todos los tests arman sus usuarios así y no
    /// tienen nada que decir del número: les alcanza con esta forma corta. Va adentro de factory.InTransactionAsync, como
    /// toda escritura de cuentas.
    /// </summary>
    public static Task<UserAccount> CreateAsync(
        this IUserRepository users, Email email, string? displayName, string culture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(users);

        return users.CreateAsync(email, phone: null, phoneConfirmed: false, displayName, culture, cancellationToken);
    }
}
```

  Con 4 argumentos, el método de 6 parámetros de `IUserRepository` no aplica, así que la llamada corta resuelve a la extensión.

- [ ] **Paso 2: los tipos.**

  ```bash
  perl -0777 -pi -e 's/GetRequiredService<IIdentityService>\(\)(\s*\.Find)/GetRequiredService<IUserReader>()$1/g; s/GetRequiredService<IIdentityService>\(\)/GetRequiredService<IUserRepository>()/g; s/var identity = services\.GetRequiredService<IUserRepository>\(\);/var users = services.GetRequiredService<IUserRepository>();/g; s/\bidentity\./users./g; s/using ArquitecturaBase\.Application\.Interfaces\.Integrations;/using ArquitecturaBase.Application.Interfaces.Persistence;/' tests/ArquitecturaBase.Api.IntegrationTests/Support/AdminUsersApi.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/AccountsWithPhoneTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/MeEmailEndpointsTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/MeWhatsAppEndpointsTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UnlinkUserPhoneEndpointTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UpdateUserContactTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UserSoftDeleteTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UsersEndpointsTests.cs
  ```

  Qué hace, en orden: las búsquedas `Find…` van al lector; todo lo demás, al repositorio; las variables `identity` de `UsersEndpointsTests` (10) y `UnlinkUserPhoneEndpointTests` (1) pasan a llamarse `users` (en esos archivos no hay otro `identity.`); y como ninguno de los ocho usa otro tipo de `Interfaces.Integrations` ni tenía el `using` de `Interfaces.Persistence`, uno reemplaza al otro. `UsersEndpointsTests` y `UserSoftDeleteTests` pasan de la forma corta sobre `IIdentityService` a la del Paso 1, sin cambiar la llamada.

  Run: `git grep -nE "IIdentityService|\bidentity\." -- tests/ArquitecturaBase.Api.IntegrationTests/Users tests/ArquitecturaBase.Api.IntegrationTests/Support/AdminUsersApi.cs`
  Esperado: sin salida.

  Run: `git grep -c "GetRequiredService<IUserReader>" -- tests/ArquitecturaBase.Api.IntegrationTests/Users tests/ArquitecturaBase.Api.IntegrationTests/Support/AdminUsersApi.cs`
  Esperado: `AdminUsersApi.cs:1`, `MeEmailEndpointsTests.cs:1`, `MeWhatsAppEndpointsTests.cs:1` y `UnlinkUserPhoneEndpointTests.cs:1` (los `AccountAsync` que buscan por Id), y ningún otro de esta tarea.

- [ ] **Paso 3: verlo pasar.** V1 (sin advertencias; IDE0005 marcaría un `using` que haya quedado de más) y V6 (Users).
- [ ] **Paso 4: verificación completa.** V2 a V5 y V7 a V10.
- [ ] **Paso 5: commit.**

```bash
git add tests/ArquitecturaBase.Api.IntegrationTests/Support/UserRepositoryExtensions.cs tests/ArquitecturaBase.Api.IntegrationTests/Support/AdminUsersApi.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/AccountsWithPhoneTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/MeEmailEndpointsTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/MeWhatsAppEndpointsTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UnlinkUserPhoneEndpointTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UpdateUserContactTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UserSoftDeleteTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UsersEndpointsTests.cs
git commit -m "$(cat <<'EOF'
test: los tests de usuarios arman por IUserRepository y leen por IUserReader

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 14: los tests de ingreso, persistencia y WhatsApp arman por `IUserRepository`

Solo tests, con la misma regla de reemplazo de la Tarea 13. Tres archivos llevan un paso a mano: `InitialAdminSignInTests` (su lectura de roles pasa a `ListRoleNamesForUserAsync`: tiene un solo rol, así que el orden da igual), `RoleReaderTests` (una variable) y `UnitOfWorkTransactionTests` (solo su `CreateAccountAsync`: el test de las escrituras de `IIdentityService` se queda hasta la Tarea 16).

**Archivos:**
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/{ExternalLoginTests,LoginSecurityTests,PhoneAccountTokensTests,RegistrationModeTests,WhatsAppLoginCodeTests,LoginLinkTests,InitialAdminSignInTests}.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/{LoginLinkRepositoryTests,RoleReaderTests,UnitOfWorkTransactionTests}.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/WhatsApp/{WhatsAppBotTests,WhatsAppBotLogPrivacyTests}.cs`

- [ ] **Paso 1: los que no usan otro tipo de `Interfaces.Integrations`** (el `using` se reemplaza):

  ```bash
  perl -0777 -pi -e 's/GetRequiredService<IIdentityService>\(\)(\s*\.Find)/GetRequiredService<IUserReader>()$1/g; s/GetRequiredService<IIdentityService>\(\)/GetRequiredService<IUserRepository>()/g; s/using ArquitecturaBase\.Application\.Interfaces\.Integrations;/using ArquitecturaBase.Application.Interfaces.Persistence;/' tests/ArquitecturaBase.Api.IntegrationTests/Auth/ExternalLoginTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginSecurityTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/PhoneAccountTokensTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Persistence/LoginLinkRepositoryTests.cs tests/ArquitecturaBase.Api.IntegrationTests/WhatsApp/WhatsAppBotLogPrivacyTests.cs
  ```

- [ ] **Paso 2: los que siguen usando otro tipo de `Interfaces.Integrations`** (`ILoginCodeHasher`, `ISecureTokenGenerator` o `IWhatsAppOutbox`) y ya tienen el `using` de `Interfaces.Persistence` (el suyo se queda):

  ```bash
  perl -0777 -pi -e 's/GetRequiredService<IIdentityService>\(\)(\s*\.Find)/GetRequiredService<IUserReader>()$1/g; s/GetRequiredService<IIdentityService>\(\)/GetRequiredService<IUserRepository>()/g' tests/ArquitecturaBase.Api.IntegrationTests/Auth/RegistrationModeTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/WhatsAppLoginCodeTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginLinkTests.cs tests/ArquitecturaBase.Api.IntegrationTests/WhatsApp/WhatsAppBotTests.cs
  ```

- [ ] **Paso 3: `RoleReaderTests`.**

  ```bash
  perl -0777 -pi -e 's/GetRequiredService<IIdentityService>\(\)/GetRequiredService<IUserRepository>()/g; s/var identity = services\.GetRequiredService<IUserRepository>\(\);/var users = services.GetRequiredService<IUserRepository>();/; s/\bidentity\./users./g; s/using ArquitecturaBase\.Application\.Interfaces\.Integrations;\r?\n//' tests/ArquitecturaBase.Api.IntegrationTests/Persistence/RoleReaderTests.cs
  ```

  (Ya tiene el `using` de `Interfaces.Persistence`, así que el de `Integrations` se borra.)

- [ ] **Paso 4: `InitialAdminSignInTests`.** Reemplazar:

  ```csharp
              var identity = services.GetRequiredService<IIdentityService>();
              var admin = await identity.FindByEmailAsync(Email.Create(adminEmail).Value, Ct);

              return admin is null ? [] : await identity.GetRolesAsync(admin.Id, Ct);
  ```

  por:

  ```csharp
              var users = services.GetRequiredService<IUserReader>();
              var admin = await users.FindByEmailAsync(Email.Create(adminEmail).Value, Ct);

              return admin is null ? [] : await users.ListRoleNamesForUserAsync(admin.Id, Ct);
  ```

  (El `using` de `Interfaces.Integrations` se queda por `ILoginCodeHasher`.)

- [ ] **Paso 5: `UnitOfWorkTransactionTests.CreateAccountAsync`.** Reemplazar:

  ```csharp
      private Task<UserAccount> CreateAccountAsync(string? email = null) =>
          factory.InTransactionAsync(services => services.GetRequiredService<IIdentityService>().CreateAsync(
  ```

  por:

  ```csharp
      private Task<UserAccount> CreateAccountAsync(string? email = null) =>
          factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().CreateAsync(
  ```

- [ ] **Paso 6: la puerta del paso.**

  Run: `git grep -ln "IIdentityService" -- tests/ArquitecturaBase.Api.IntegrationTests`
  Esperado: solo `Identity/IdentityServiceTests.cs`, `Persistence/UnitOfWorkTransactionTests.cs` (el test de sus escrituras), `Support/IdentityServiceExtensions.cs` y `Support/StaleIdentityReads.cs`.

- [ ] **Paso 7: verlo pasar.** V1 (sin advertencias), V5 (Auth), V7 (WhatsApp) y V8 (Persistence, Identity, Settings y Roles).
- [ ] **Paso 8: verificación completa.** V2 a V4, V6, V9 y V10.
- [ ] **Paso 9: commit.**

```bash
git add tests/ArquitecturaBase.Api.IntegrationTests/Auth/ExternalLoginTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginSecurityTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/PhoneAccountTokensTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/RegistrationModeTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/WhatsAppLoginCodeTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginLinkTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/InitialAdminSignInTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Persistence/LoginLinkRepositoryTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Persistence/RoleReaderTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UnitOfWorkTransactionTests.cs tests/ArquitecturaBase.Api.IntegrationTests/WhatsApp/WhatsAppBotTests.cs tests/ArquitecturaBase.Api.IntegrationTests/WhatsApp/WhatsAppBotLogPrivacyTests.cs
git commit -m "$(cat <<'EOF'
test: los tests de ingreso, persistencia y WhatsApp arman por IUserRepository

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 15: `IdentityServiceTests` se reparte entre `UserRepositoryTests` y `UserReaderTests`

Solo tests. Cada test conserva su nombre y lo que afirma; lo que cambia es el contrato por el que llega. Las listas de roles salen de `IUserReader.ListRoleNamesForUserAsync` (un solo rol en cada caso, o comparación con `Contains`). `Role_names_are_sorted_and_deleted_users_are_rejected` se borra: el orden lo cubre `User_detail_sorts_roles_and_excludes_deleted_accounts`, y el rechazo de una cuenta borrada, `SignInServiceTests.Operations_reject_a_deleted_or_missing_account` (Tarea 5). El comentario XML huérfano de `IdentityServiceTests` (hoy :437-443, pegado a `WithIdentityAsync`) vuelve a los tests de búsqueda, corregido: hoy la búsqueda compara contra el número solo un texto que parece un número, así que lo que importa del prefijo es que no se repita entre corridas.

**Archivos:**
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UserRepositoryTests.cs`
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UserReaderTests.cs`
- Borrar: `tests/ArquitecturaBase.Api.IntegrationTests/Identity/IdentityServiceTests.cs`

- [ ] **Paso 1: las escrituras.** Crear `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UserRepositoryTests.cs`:

```csharp
using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// Las escrituras de cuentas (IUserRepository) contra Postgres: el alta y sus roles iniciales, el número único y lo que
/// escriben sin tocar la sesión. Que exijan la transacción lo fija UnitOfWorkTransactionTests, y qué pasa con un 23505
/// adentro de un límite, UserRepositoryTransactionTests.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class UserRepositoryTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task New_users_are_confirmed_and_get_the_user_role()
    {
        var email = UniqueEmail("new");

        var (user, roles) = await InTransactionWithAccountsAsync(async (users, reader) =>
        {
            var created = await users.CreateAsync(email, "Ana", "en", Ct);
            return (created, await reader.ListRoleNamesForUserAsync(created.Id, Ct));
        });

        Assert.Equal(email.Value, user.Email);
        Assert.Equal("Ana", user.DisplayName);
        Assert.Equal("en", user.Culture);
        Assert.True(user.IsActive);
        Assert.Equal(["User"], roles);
        Assert.True(await factory.ExecuteDbContextAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.EmailConfirmed).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Admin_email_gets_the_admin_role()
    {
        var adminEmail = Email.Create(ApiFactory.AdminEmail).Value;

        var roles = await InTransactionWithAccountsAsync(async (users, reader) =>
        {
            var admin = await reader.FindByEmailAsync(adminEmail, Ct) ?? await users.CreateAsync(adminEmail, null, "es", Ct);
            return await reader.ListRoleNamesForUserAsync(admin.Id, Ct);
        });

        Assert.Contains("Admin", roles);
    }

    [Fact]
    public async Task Phone_only_accounts_can_be_created()
    {
        var phone = TestPhones.Unique();

        var (user, roles) = await InTransactionWithAccountsAsync(async (users, reader) =>
        {
            var created = await users.CreateAsync(email: null, phone, phoneConfirmed: true, "Laura", "es", Ct);
            return (created, await reader.ListRoleNamesForUserAsync(created.Id, Ct));
        });
        var stored = await factory.ExecuteDbContextAsync(db => db.Users.SingleAsync(u => u.Id == user.Id, Ct));

        Assert.Null(user.Email);
        Assert.False(user.EmailConfirmed);
        Assert.Equal(phone.Value, user.PhoneNumber);
        Assert.True(user.PhoneNumberConfirmed);
        Assert.Equal(["User"], roles);
        Assert.Null(stored.Email);
        Assert.Null(stored.NormalizedEmail);
        Assert.Equal(phone.Value, stored.PhoneNumber);
    }

    [Fact]
    public async Task The_user_name_is_the_account_id_and_not_the_email_or_the_phone()
    {
        var withEmail = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(UniqueEmail("username"), null, "es", Ct));
        var withPhone = await InTransactionWithAccountsAsync((users, _) =>
            users.CreateAsync(email: null, TestPhones.Unique(), phoneConfirmed: false, null, "es", Ct));

        var userNames = await factory.ExecuteDbContextAsync(db => db.Users
            .Where(u => u.Id == withEmail.Id || u.Id == withPhone.Id)
            .ToDictionaryAsync(u => u.Id, u => u.UserName, Ct));

        Assert.Equal(withEmail.Id.ToString("D", CultureInfo.InvariantCulture), userNames[withEmail.Id]);
        Assert.Equal(withPhone.Id.ToString("D", CultureInfo.InvariantCulture), userNames[withPhone.Id]);
    }

    [Fact]
    public async Task A_phone_loaded_without_verifying_it_stays_unconfirmed()
    {
        var user = await InTransactionWithAccountsAsync((users, _) =>
            users.CreateAsync(UniqueEmail("unverified"), TestPhones.Unique(), phoneConfirmed: false, null, "es", Ct));

        Assert.True(user.EmailConfirmed);
        Assert.NotNull(user.PhoneNumber);
        Assert.False(user.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task An_account_needs_an_email_or_a_phone()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => InTransactionWithAccountsAsync((users, _) =>
            users.CreateAsync(email: null, phone: null, phoneConfirmed: false, "Nadie", "es", Ct)));
    }

    [Fact]
    public async Task Two_accounts_cannot_share_a_phone_number()
    {
        var phone = TestPhones.Unique();
        await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(email: null, phone, phoneConfirmed: true, null, "es", Ct));

        // La regla la da el índice único: Application se fija antes, así que chocar con él es un error de programación.
        // El 23505 que escapa del límite sale traducido, con el original adentro.
        var exception = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => InTransactionWithAccountsAsync(
            (users, _) => users.CreateAsync(UniqueEmail("samephone"), phone, phoneConfirmed: false, null, "es", Ct)));
        Assert.IsAssignableFrom<DbUpdateException>(exception.InnerException);
    }

    [Fact]
    public async Task A_deleted_account_keeps_its_phone_number_reserved()
    {
        var phone = TestPhones.Unique();
        var user = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(email: null, phone, phoneConfirmed: true, null, "es", Ct));
        await WriteAsync(users => users.DeleteAsync(user.Id, Ct));

        var found = await WithReaderAsync(reader => reader.FindByPhoneAsync(phone, Ct));
        var deleted = await WithReaderAsync(reader => reader.IsDeletedPhoneAsync(phone, Ct));

        Assert.Null(found);
        Assert.True(deleted);
        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => InTransactionWithAccountsAsync((users, _) =>
            users.CreateAsync(email: null, phone, phoneConfirmed: true, null, "es", Ct)));
    }

    [Fact]
    public async Task Linking_a_phone_or_an_email_writes_them_without_closing_the_session()
    {
        // Solo escriben los datos: renovar el security stamp le cortaría la cookie a quien vincula su propio número
        // desde el perfil. Cortar las sesiones lo decide quien llama.
        var phone = TestPhones.Unique();
        var email = UniqueEmail("linked");
        var user = await InTransactionWithAccountsAsync((users, _) =>
            users.CreateAsync(email: null, TestPhones.Unique(), phoneConfirmed: true, null, "es", Ct));
        var stampBefore = await SecurityStampOfAsync(user.Id);

        var afterLinking = await InTransactionWithAccountsAsync(async (users, reader) =>
        {
            await users.SetPhoneAsync(user.Id, phone, confirmed: false, Ct);
            await users.SetEmailAsync(user.Id, email, confirmed: true, Ct);
            return await reader.FindByIdAsync(user.Id, Ct);
        });
        var byEmail = await WithReaderAsync(reader => reader.FindByEmailAsync(email, Ct));

        var afterRemoving = await InTransactionWithAccountsAsync(async (users, reader) =>
        {
            await users.RemovePhoneAsync(user.Id, Ct);
            return await reader.FindByIdAsync(user.Id, Ct);
        });

        Assert.Equal(phone.Value, afterLinking!.PhoneNumber);
        Assert.False(afterLinking.PhoneNumberConfirmed);
        Assert.Equal(email.Value, afterLinking.Email);
        Assert.True(afterLinking.EmailConfirmed);
        Assert.Equal(user.Id, byEmail?.Id);
        Assert.Null(afterRemoving!.PhoneNumber);
        Assert.False(afterRemoving.PhoneNumberConfirmed);
        Assert.Equal(stampBefore, await SecurityStampOfAsync(user.Id));
    }

    private Task<string?> SecurityStampOfAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.SecurityStamp).SingleAsync(Ct));

    private static Email UniqueEmail(string prefix) =>
        Email.Create(prefix + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com").Value;

    private Task<T> WithReaderAsync<T>(Func<IUserReader, Task<T>> action) =>
        factory.ExecuteScopeAsync(services => action(services.GetRequiredService<IUserReader>()));

    private Task WriteAsync(Func<IUserRepository, Task> write) =>
        factory.InTransactionAsync(services => write(services.GetRequiredService<IUserRepository>()));

    /// <summary>
    /// Adentro de un límite, con el repositorio y el lector del mismo scope: las escrituras de cuentas lo exigen, y el
    /// lector ve lo que escribió el repositorio antes del commit.
    /// </summary>
    private Task<T> InTransactionWithAccountsAsync<T>(Func<IUserRepository, IUserReader, Task<T>> action) =>
        factory.InTransactionAsync(services => action(
            services.GetRequiredService<IUserRepository>(), services.GetRequiredService<IUserReader>()));
}
```

- [ ] **Paso 2: las lecturas.** Crear `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UserReaderTests.cs`:

```csharp
using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// Las lecturas de cuentas (IUserReader) contra Postgres: las búsquedas, las cuentas borradas, el detalle, el listado, el
/// conteo de administradores y los vínculos externos. Los datos se arman con IUserRepository adentro de un límite, como
/// los arma la aplicación.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class UserReaderTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Users_are_found_by_email_and_by_external_login()
    {
        var email = UniqueEmail("find");
        var created = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(email, null, "es", Ct));
        var login = new ExternalLogin("Google", "google-" + created.Id.ToString("N", CultureInfo.InvariantCulture), email.Value, true, null);

        await WriteAsync(users => users.AddExternalLoginAsync(created.Id, login, Ct));

        Assert.Equal(created.Id, (await WithReaderAsync(reader => reader.FindByEmailAsync(email, Ct)))!.Id);
        Assert.Equal(created.Id, (await WithReaderAsync(reader => reader.FindByExternalLoginAsync("Google", login.ProviderKey, Ct)))!.Id);
        Assert.Equal(created.Id, (await WithReaderAsync(reader => reader.FindByIdAsync(created.Id, Ct)))!.Id);
    }

    [Fact]
    public async Task Search_treats_like_wildcards_as_literals()
    {
        var prefix = SearchPrefix("srch");
        var withUnderscore = Email.Create(prefix + "-a_b@example.com").Value;
        var withoutUnderscore = Email.Create(prefix + "-axb@example.com").Value;
        await WriteAsync(async users =>
        {
            await users.CreateAsync(withUnderscore, null, "es", Ct);
            await users.CreateAsync(withoutUnderscore, null, "es", Ct);
        });

        var page = await WithReaderAsync(reader => reader.ListUsersAsync(new ListUsersRequest { Search = prefix + "-a_b" }, Ct));

        Assert.Equal([withUnderscore.Value], page.Items.Select(item => item.Email));
    }

    [Fact]
    public async Task Users_are_sorted_by_the_requested_field()
    {
        var prefix = SearchPrefix("sort");
        await WriteAsync(async users =>
        {
            foreach (var name in new[] { "b", "c", "a" })
            {
                await users.CreateAsync(Email.Create($"{prefix}-{name}@example.com").Value, null, "es", Ct);
            }
        });

        var page = await WithReaderAsync(reader =>
            reader.ListUsersAsync(new ListUsersRequest { Search = prefix, Sort = "-email", PageSize = 2 }, Ct));

        Assert.Equal([$"{prefix}-c@example.com", $"{prefix}-b@example.com"], page.Items.Select(item => item.Email));
        Assert.Equal(3, page.TotalCount);
    }

    /// <summary>
    /// El bot le contesta a una cuenta borrada como a una deshabilitada, en su idioma: necesita la cuenta, no solo saber
    /// que existe. Una cuenta que no está borrada no aparece.
    /// </summary>
    [Fact]
    public async Task A_deleted_account_is_found_by_its_phone_with_its_culture()
    {
        var phone = TestPhones.Unique();
        var activePhone = TestPhones.Unique();
        var user = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(email: null, phone, phoneConfirmed: true, null, "en", Ct));
        await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(email: null, activePhone, phoneConfirmed: true, null, "en", Ct));
        await WriteAsync(users => users.DeleteAsync(user.Id, Ct));

        var deleted = await WithReaderAsync(reader => reader.FindDeletedByPhoneAsync(phone, Ct));
        var active = await WithReaderAsync(reader => reader.FindDeletedByPhoneAsync(activePhone, Ct));
        var unknown = await WithReaderAsync(reader => reader.FindDeletedByPhoneAsync(TestPhones.Unique(), Ct));

        Assert.Equal(user.Id, deleted?.Id);
        Assert.Equal("en", deleted?.Culture);
        Assert.Null(active);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task Users_are_found_by_phone()
    {
        var phone = TestPhones.Unique();
        var created = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(email: null, phone, phoneConfirmed: true, null, "es", Ct));

        var found = await WithReaderAsync(reader => reader.FindByPhoneAsync(phone, Ct));
        var other = await WithReaderAsync(reader => reader.FindByPhoneAsync(TestPhones.Unique(), Ct));
        var deleted = await WithReaderAsync(reader => reader.IsDeletedPhoneAsync(phone, Ct));

        Assert.Equal(created.Id, found?.Id);
        Assert.Null(other);
        Assert.False(deleted);
    }

    [Fact]
    public async Task Deleted_email_lookup_uses_identity_normalization_and_preserves_the_account()
    {
        var email = UniqueEmail("deleted-email");
        var user = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(email, "Lucía", "en", Ct));
        var uppercaseEmail = Email.Create(email.Value.ToUpperInvariant()).Value;

        Assert.False(await WithReaderAsync(reader => reader.IsDeletedEmailAsync(uppercaseEmail, Ct)));
        Assert.Null(await WithReaderAsync(reader => reader.FindDeletedByEmailAsync(uppercaseEmail, Ct)));

        await WriteAsync(users => users.DeleteAsync(user.Id, Ct));

        Assert.True(await WithReaderAsync(reader => reader.IsDeletedEmailAsync(uppercaseEmail, Ct)));
        Assert.Null(await WithReaderAsync(reader => reader.FindByIdAsync(user.Id, Ct)));
        var deleted = await WithReaderAsync(reader => reader.FindDeletedByEmailAsync(uppercaseEmail, Ct));
        Assert.Equal(user.Id, deleted?.Id);
        Assert.Equal("en", deleted?.Culture);
    }

    [Fact]
    public async Task User_detail_sorts_roles_and_excludes_deleted_accounts()
    {
        var user = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(UniqueEmail("detail"), "Ana", "es", Ct));
        await WriteAsync(users => users.SetRolesAsync(user.Id, [SystemRoles.User, SystemRoles.Admin], Ct));

        var detail = await WithReaderAsync(reader => reader.FindDetailAsync(user.Id, Ct));
        Assert.Equal(user.Id, detail?.Id);
        Assert.Equal("Ana", detail?.DisplayName);
        Assert.Equal([SystemRoles.Admin, SystemRoles.User], detail?.Roles);

        await WriteAsync(users => users.DeleteAsync(user.Id, Ct));

        Assert.Null(await WithReaderAsync(reader => reader.FindDetailAsync(user.Id, Ct)));
    }

    [Fact]
    public async Task Admin_count_includes_only_active_and_not_deleted_accounts()
    {
        var before = await WithReaderAsync(reader => reader.CountActiveAdminsAsync(Ct));
        var user = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(UniqueEmail("admin-count"), null, "es", Ct));
        await WriteAsync(users => users.SetRolesAsync(user.Id, [SystemRoles.Admin], Ct));

        Assert.Equal(before + 1, await WithReaderAsync(reader => reader.CountActiveAdminsAsync(Ct)));
        await WriteAsync(users => users.SetActiveAsync(user.Id, false, Ct));
        Assert.Equal(before, await WithReaderAsync(reader => reader.CountActiveAdminsAsync(Ct)));

        await WriteAsync(async users =>
        {
            await users.SetActiveAsync(user.Id, true, Ct);
            await users.DeleteAsync(user.Id, Ct);
        });
        Assert.Equal(before, await WithReaderAsync(reader => reader.CountActiveAdminsAsync(Ct)));
    }

    [Fact]
    public async Task External_logins_are_reported_by_provider()
    {
        var user = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(UniqueEmail("provider"), null, "es", Ct));
        var login = new ExternalLogin(
            ExternalLoginProviders.Google, "google-" + user.Id.ToString("N", CultureInfo.InvariantCulture), user.Email, true, null);

        var before = await WithReaderAsync(reader => reader.HasExternalLoginAsync(user.Id, ExternalLoginProviders.Google, Ct));
        var after = await InTransactionWithAccountsAsync(async (users, reader) =>
        {
            await users.AddExternalLoginAsync(user.Id, login, Ct);
            return await reader.HasExternalLoginAsync(user.Id, ExternalLoginProviders.Google, Ct);
        });
        var otherProvider = await WithReaderAsync(reader => reader.HasExternalLoginAsync(user.Id, "Microsoft", Ct));

        Assert.False(before);
        Assert.True(after);
        Assert.False(otherProvider);
    }

    private static Email UniqueEmail(string prefix) =>
        Email.Create(prefix + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com").Value;

    /// <summary>
    /// Un texto de búsqueda que solo matchea con las cuentas del test. Lleva el Guid entero y no un pedazo: todos los
    /// tests comparten la base, y ocho caracteres pueden repetirse entre corridas. Empieza con letras, así que la búsqueda
    /// no lo compara contra los números de teléfono: eso pasa solo con un texto que parece un número (dígitos, espacios,
    /// "+", guiones, puntos y paréntesis).
    /// </summary>
    private static string SearchPrefix(string name) => name + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    private Task<T> WithReaderAsync<T>(Func<IUserReader, Task<T>> action) =>
        factory.ExecuteScopeAsync(services => action(services.GetRequiredService<IUserReader>()));

    private Task WriteAsync(Func<IUserRepository, Task> write) =>
        factory.InTransactionAsync(services => write(services.GetRequiredService<IUserRepository>()));

    /// <summary>Adentro de un límite, con el repositorio y el lector del mismo scope.</summary>
    private Task<T> InTransactionWithAccountsAsync<T>(Func<IUserRepository, IUserReader, Task<T>> action) =>
        factory.InTransactionAsync(services => action(
            services.GetRequiredService<IUserRepository>(), services.GetRequiredService<IUserReader>()));
}
```

- [ ] **Paso 3: borrar el archivo viejo.**

  ```bash
  git rm tests/ArquitecturaBase.Api.IntegrationTests/Identity/IdentityServiceTests.cs
  ```

- [ ] **Paso 4: verlo pasar.** V1 (sin advertencias) y:

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.UserRepositoryTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.UserReaderTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`, 18 tests (9 y 9).

  Run: `git grep -nE "return (true|0);" -- tests/ArquitecturaBase.Api.IntegrationTests`
  Esperado: solo `Support/CapturingWhatsAppOutbox.cs:29`. Es la puerta del pendiente e.

- [ ] **Paso 5: verificación completa.** V2 a V10.
- [ ] **Paso 6: commit.**

```bash
git add tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UserRepositoryTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UserReaderTests.cs
git commit -m "$(cat <<'EOF'
test: IdentityServiceTests se reparte entre UserRepositoryTests y UserReaderTests

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

  (El borrado ya quedó en el índice con `git rm`.)

---

### Tarea 16: se borra `IIdentityService` (pendiente c)

Un `refactor!`: desaparecen el contrato, su implementación, su registro, la forma corta de los tests, el envoltorio de `StaleIdentityReads` y el test viejo de escrituras. `IUserReader` e `IUserRepository` reciben sus XML. El doble pierde `IIdentityService` y las lecturas de roles; `FakeRoleReader` pasa a tener datos propios. Entran las dos reglas de IL sobre quién llama a `ISignInService`, que antes no se podían escribir porque `IdentityService` era un llamador más.

**Archivos:**
- Borrar: `src/ArquitecturaBase.Application/Interfaces/Integrations/IIdentityService.cs`, `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`, `tests/ArquitecturaBase.Api.IntegrationTests/Support/IdentityServiceExtensions.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityRegistration.cs`
- Modificar: `src/ArquitecturaBase.Application/Interfaces/Persistence/IUserReader.cs` e `IUserRepository.cs` (reemplazos completos)
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Support/StaleIdentityReads.cs` (reemplazo completo)
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UnitOfWorkTransactionTests.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Users/{UserServiceTestHost,UserServiceWriteTests}.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/IdentityBoundaryTests.cs`
- Modificar: las reglas de identidad, las de WhatsApp, el spec funcional del ingreso por WhatsApp y el spec de arquitectura (roles; hoy `docs/features/identidad.md`, `docs/features/whatsapp.md`, `docs/specs/2026-09-22-ingreso-whatsapp-design.md` y `docs/architecture/backend.md`)

- [ ] **Paso 1: las reglas de quién llama, en rojo.** En `IdentityBoundaryTests.cs`, debajo de `SignInServiceImplementation`, agregar:

```csharp
    private const string AuthServices = "ArquitecturaBase.Application.Services.Auth.";
    private const string WhatsAppServices = "ArquitecturaBase.Application.Services.WhatsApp.";

    private static readonly string SignInContract = typeof(ISignInService).FullName!;
```

  y, debajo de `Only_the_sign_in_service_touches_sessions`, agregar:

```csharp
    [Fact]
    public void Only_entry_points_open_a_session()
    {
        var owners = OwnersOf(nameof(ISignInService.SignInAsync));

        // El conjunto exacto: así también prueba que el detector ve las llamadas.
        Assert.Equal(
            [AuthServices + "ExternalLoginService", AuthServices + "LoginCodeVerifier", AuthServices + "LoginLinkService"],
            owners);

        // La regla de oro de WhatsApp: un mensaje nunca abre una sesión.
        Assert.DoesNotContain(owners, owner => owner.StartsWith(WhatsAppServices, StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_sign_in_code_counts_failed_attempts()
    {
        // Confirmar un destino desde el perfil no es un ingreso: no suma a los fallos de la cuenta (reglas de identidad).
        Assert.Equal([AuthServices + "LoginCodeVerifier"], OwnersOf(nameof(ISignInService.RegisterFailedAttemptAsync)));
    }
```

  y, al final de la clase, debajo de `Names`, agregar:

```csharp
    /// <summary>Los tipos de nivel superior que llaman a ese miembro de ISignInService, ordenados.</summary>
    private static string[] OwnersOf(string signInMember) =>
    [
        .. Calls
            .Where(call => call.DeclaringType == SignInContract && call.Method == signInMember)
            .Select(call => call.Owner)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];
```

  Run: `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj -- --filter-class "ArquitecturaBase.ArchitectureTests.IdentityBoundaryTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; grep -n "Identity\.IdentityS" "$TEMP/etapa2-focus.log" | head -4`
  Esperado: `exit=2`: `ArquitecturaBase.Infrastructure.Identity.IdentityService` aparece como dueño en las dos reglas nuevas, porque todavía reenvía. Sale en las dos líneas `Actual:`, cortado a los 50 caracteres (`"ArquitecturaBase.Infrastructure.Identity.IdentityS"···`), igual que en la Tarea 4, Paso 1.

- [ ] **Paso 2: borrar la fachada.**

  ```bash
  git rm src/ArquitecturaBase.Application/Interfaces/Integrations/IIdentityService.cs src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs tests/ArquitecturaBase.Api.IntegrationTests/Support/IdentityServiceExtensions.cs
  ```

  En `IdentityRegistration.cs`:
  - borrar la línea `        services.AddScoped<IIdentityService, IdentityService>();`;
  - reemplazar el comentario

    ```csharp
                    // El UserName es el Id de la cuenta, que arma IdentityService: nadie lo escribe, así que no hace
                    // falta restringir sus caracteres.
    ```

    por

    ```csharp
                    // El UserName es el Id de la cuenta, que arma UserRepository.NewUser: nadie lo escribe, así que no
                    // hace falta restringir sus caracteres.
    ```

- [ ] **Paso 3: el lector recibe sus XML.** Reemplazar `IUserReader.cs` completo por:

```csharp
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Users.ReadModels;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Lecturas de cuentas: proyecciones sin seguimiento, existencia y conteos. Respetan el filtro global de borrados, salvo
/// las que dicen Deleted. Nunca devuelven la entidad de Identity y no exigen transacción. Van siempre a la base: una
/// relectura después de tomar un lock ve lo que otro ya confirmó. Para escribir, <see cref="IUserRepository"/>; para el
/// bloqueo y la sesión, <see cref="Integrations.ISignInService"/>.
/// </summary>
public interface IUserReader
{
    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserAccount?> FindByEmailAsync(Email email, CancellationToken cancellationToken);

    Task<UserAccount?> FindByExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken);

    /// <summary>La cuenta no borrada con ese número exacto, o null.</summary>
    Task<UserAccount?> FindByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken);

    /// <summary>
    /// Si ese correo es de una cuenta borrada lógicamente. El filtro global las oculta de todas las demás búsquedas, así
    /// que sin esto un ingreso intentaría crear una cuenta nueva y chocaría con el índice único del correo.
    /// </summary>
    Task<bool> IsDeletedEmailAsync(Email email, CancellationToken cancellationToken);

    /// <summary>
    /// Si ese número es de una cuenta borrada lógicamente. Como con el correo, la cuenta borrada conserva su número y el
    /// índice único lo sigue reservando.
    /// </summary>
    Task<bool> IsDeletedPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken);

    /// <summary>La cuenta borrada lógicamente con ese correo, o null. La usa el alta para restaurarla.</summary>
    Task<UserAccount?> FindDeletedByEmailAsync(Email email, CancellationToken cancellationToken);

    /// <summary>
    /// La cuenta borrada lógicamente con ese número, o null. La usa el bot de WhatsApp, que le contesta a una cuenta
    /// borrada como a una deshabilitada y en su idioma: por eso necesita la cuenta y no le alcanza con
    /// <see cref="IsDeletedPhoneAsync"/>.
    /// </summary>
    Task<UserAccount?> FindDeletedByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken);

    /// <summary>Si la cuenta tiene vinculado ese proveedor externo (ver <see cref="ExternalLoginProviders"/>).</summary>
    Task<bool> HasExternalLoginAsync(Guid userId, string provider, CancellationToken cancellationToken);

    /// <summary>
    /// Los roles asignados a una cuenta, sin imponer un orden de presentación: <see cref="FindDetailAsync"/> y
    /// ConnectService los ordenan.
    /// </summary>
    Task<IReadOnlyCollection<string>> ListRoleNamesForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>El detalle de la cuenta, con los roles en orden ordinal, o null si no existe o está borrada.</summary>
    Task<UserDetail?> FindDetailAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Cuántas cuentas activas tienen el rol Admin. Lo usa <c>UserGuards</c> para no dejar al sistema sin
    /// administradores.
    /// </summary>
    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken);

    Task<PagedResult<UserListItem>> ListUsersAsync(UserListRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Cuántas cuentas traería cada opción de filtro, con los mismos filtros que <see cref="ListUsersAsync"/> salvo el
    /// propio.
    /// </summary>
    Task<UserFilterCounts> GetUserFilterCountsAsync(UserListRequest request, CancellationToken cancellationToken);
}
```

- [ ] **Paso 4: el repositorio recibe sus XML.** Reemplazar `IUserRepository.cs` completo por:

```csharp
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Escrituras de cuentas sobre Identity. Solo escribe datos: para leer, <see cref="IUserReader"/>; para el bloqueo, la
/// cookie y el cierre de sesiones, <see cref="Integrations.ISignInService"/>. Las escrituras de datos no renuevan el
/// security stamp (el alta pone el primero): cortar el acceso lo decide el caso de uso con <c>AccountAccessRevoker</c>.
/// Todas, igual que <see cref="LockExternalSignInAsync"/>, exigen la transacción del caso de uso
/// (<see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/>): sin ella lanzan <see cref="InvalidOperationException"/>
/// antes de tocar nada, también cuando un test prepara datos. UserManager guarda en cada operación, dentro de esa
/// transacción y con un savepoint por guardado: un Result fallido con <see cref="CommitPolicy.OnSuccess"/> o una
/// excepción deshacen todo, incluidos esos guardados.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Serializa el alta o vínculo de una identidad externa por correo y clave del proveedor: toma external-login: y
    /// login-code: del correo en una sola llamada (por el orden ordinal, external-login: primero). Corre dentro de la
    /// transacción del caso de uso, que el ingreso con Google abre con <see cref="CommitPolicy.OnAnyResult"/>: la
    /// auditoría se confirma aunque el ingreso falle. Exige esa transacción; sin ella lanza
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    Task LockExternalSignInAsync(
        Email email, string provider, string providerKey, CancellationToken cancellationToken);

    /// <summary>
    /// Crea la cuenta con un correo, un número o los dos; sin ninguno lanza una <see cref="ArgumentException"/>, porque
    /// que haya al menos uno lo valida Application antes. El UserName es el Id de la cuenta, así cambiar el correo o el
    /// número no cambia nada más. El correo queda confirmado, porque ambos ingresos lo verifican; el número, según
    /// <paramref name="phoneConfirmed"/>: uno que carga un administrador queda sin verificar hasta que la persona entra con
    /// él. Le asigna el rol Admin si el correo es el configurado en Seed:AdminEmail, y User si no. Si Identity o la base lo
    /// rechazan lanza una excepción: el caso de uso buscó antes la cuenta por su correo y su número, así que un rechazo es
    /// un error de programación.
    /// </summary>
    Task<UserAccount> CreateAsync(
        Email? email,
        PhoneNumber? phone,
        bool phoneConfirmed,
        string? displayName,
        string culture,
        CancellationToken cancellationToken);

    /// <summary>
    /// El alta de un administrador (sección 12 del spec del ingreso con WhatsApp): como <see cref="CreateAsync"/>, pero el
    /// correo y el número quedan sin verificar hasta que la persona entra con ellos, porque nadie probó todavía que sean
    /// suyos. El ingreso con el código, Google y el bot los verifican. El alta toma el lock del destino, igual que el
    /// ingreso con código y Google (login-code: del correo); el que crea cuentas sin él es el bot, que solo tiene la fila
    /// del contacto. Si otra cuenta se quedó con el correo o el número entre la búsqueda del alta y este guardado, lanza
    /// <see cref="UniqueConstraintViolationException"/> y no queda nada de la cuenta, ni en la base ni para guardar
    /// después: el savepoint deshizo solo ese guardado y la transacción del caso de uso sigue usable. Cualquier otro
    /// rechazo sigue siendo un error de programación, como en <see cref="CreateAsync"/>.
    /// </summary>
    Task<UserAccount> CreateUnverifiedAsync(
        Email? email,
        PhoneNumber? phone,
        string? displayName,
        string culture,
        CancellationToken cancellationToken);

    /// <summary>Vincula el proveedor externo a la cuenta.</summary>
    Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken);

    /// <summary>Deshace el borrado lógico, deja la cuenta activa y le pone el nombre del alta.</summary>
    Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken);

    /// <summary>
    /// Le pone el correo a la cuenta, verificado o no, y recalcula el normalizado que usa la búsqueda por correo. Como
    /// <see cref="SetPhoneAsync"/>, solo escribe el dato y no renueva el security stamp. El correo tiene índice único:
    /// quien llama se fija antes con <see cref="IUserReader.FindByEmailAsync"/> e
    /// <see cref="IUserReader.IsDeletedEmailAsync"/>, y un choque posterior se trata igual que con el número.
    /// </summary>
    Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken);

    /// <summary>
    /// Le pone el número a la cuenta, verificado o no. Solo escribe el dato: no renueva el security stamp, que le cortaría
    /// la cookie a quien vincula su propio número desde el perfil. Si hay que cortar el acceso, lo decide quien llama con
    /// <c>AccountAccessRevoker</c>, que además de cerrar las sesiones invalida los enlaces pendientes. El número tiene
    /// índice único: quien llama se fija antes con <see cref="IUserReader.FindByPhoneAsync"/> e
    /// <see cref="IUserReader.IsDeletedPhoneAsync"/>. Si igual choca, porque otra cuenta lo guardó entre esa búsqueda y
    /// este guardado, lanza <see cref="UniqueConstraintViolationException"/> y la cuenta queda como estaba: la
    /// transacción sigue usable, y con <see cref="CommitPolicy.OnAnyResult"/> se confirma lo demás.
    /// </summary>
    Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken);

    /// <summary>
    /// Le saca el número a la cuenta y lo deja sin verificar. Como <see cref="SetPhoneAsync"/>, solo escribe el dato:
    /// cuando lo desvincula un administrador, el caso de uso corta el acceso con <c>AccountAccessRevoker</c>.
    /// </summary>
    Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Deja la cuenta exactamente con esos roles: agrega los que faltan y saca los que sobran.</summary>
    Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken);

    Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken);

    Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken);

    /// <summary>Borrado lógico: la fila queda y el filtro global la esconde, así el historial sigue existiendo.</summary>
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken);

    Task UpdateProfileAsync(
        Guid userId, string? displayName, string culture, string timeZoneId, CancellationToken cancellationToken);
}
```

- [ ] **Paso 5: la sonda de lecturas viejas.** Reemplazar `tests/ArquitecturaBase.Api.IntegrationTests/Support/StaleIdentityReads.cs` completo por:

```csharp
using System.Reflection;
using System.Runtime.ExceptionServices;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence.Readers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// Cuántas búsquedas escondió <see cref="StaleIdentityReads"/>. El test la crea, se la pasa y, junto al 409, afirma que
/// hubo al menos una: así sabe que el chequeo previo leyó por la lectura vieja y que el 409 salió del choque.
/// </summary>
public sealed class StaleReadsProbe
{
    private int _hiddenLookups;

    public int HiddenLookups => Volatile.Read(ref _hiddenLookups);

    internal void Hit() => Interlocked.Increment(ref _hiddenLookups);
}

/// <summary>
/// El UserReader real, con una lectura vieja: las búsquedas de un número o de un correo dicen que no es de nadie, ni de
/// una cuenta activa ni de una borrada, aunque ya lo sea. Es lo que ve un pedido cuando otra cuenta se queda con el número
/// justo entre su búsqueda y su guardado: así un test llega al choque con el índice único sin depender de cómo se crucen
/// dos pedidos. Todo lo demás pasa tal cual al lector real.
/// </summary>
/// <remarks>
/// Es un <see cref="DispatchProxy"/> para no repetir a mano los métodos de la interfaz. Es pública y no está sellada
/// porque el proxy se arma heredando de ella. Tapa la única puerta: Application lee las cuentas solo por
/// <see cref="IUserReader"/> (el alta, la edición, el perfil, el ingreso y el bot). Si alguna vez leyera por otra, el 409
/// saldría del chequeo previo sin pasar por el choque ni por el savepoint, y el test seguiría en verde: por eso cada
/// búsqueda escondida suma en <see cref="StaleReadsProbe"/>, y el test afirma que hubo alguna. Si una búsqueda se muda a
/// otra interfaz o cambia de nombre, la sonda queda en cero y el test falla.
/// </remarks>
public class StaleIdentityReads : DispatchProxy
{
    private static readonly string[] HiddenLookupNames =
    [
        nameof(IUserReader.FindByPhoneAsync),
        nameof(IUserReader.IsDeletedPhoneAsync),
        nameof(IUserReader.FindByEmailAsync),
        nameof(IUserReader.IsDeletedEmailAsync),
    ];

    private object _inner = null!;
    private object _hidden = null!;
    private StaleReadsProbe _probe = null!;

    /// <summary>
    /// Cambia el UserReader de la Api por el real con la lectura vieja de <paramref name="hidden"/>, un
    /// <see cref="PhoneNumber"/> o un <see cref="Email"/>. Cada búsqueda escondida se cuenta en <paramref name="probe"/>,
    /// que el test crea fuera de <c>ConfigureTestServices</c> para leerla después.
    /// </summary>
    public static void Replace(IServiceCollection services, object hidden, StaleReadsProbe probe)
    {
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(probe);

        services.RemoveAll<IUserReader>();
        services.AddScoped(serviceProvider =>
            Wrap<IUserReader>(ActivatorUtilities.CreateInstance<UserReader>(serviceProvider), hidden, probe));
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (args is [{ } first, ..]
            && first.Equals(_hidden)
            && HiddenLookupNames.Contains(targetMethod.Name, StringComparer.Ordinal))
        {
            _probe.Hit();

            return targetMethod.ReturnType == typeof(Task<bool>) ? Task.FromResult(false) : Task.FromResult<UserAccount?>(null);
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(exception.InnerException);

            throw;
        }
    }

    private static TService Wrap<TService>(TService inner, object hidden, StaleReadsProbe probe)
        where TService : class
    {
        var proxy = Create<TService, StaleIdentityReads>();
        var reads = (StaleIdentityReads)(object)proxy;
        reads._inner = inner;
        reads._hidden = hidden;
        reads._probe = probe;

        return proxy;
    }
}
```

- [ ] **Paso 6: el test viejo de escrituras.** En `UnitOfWorkTransactionTests.cs`, borrar `Identity_service_writes_outside_the_boundary_throw_and_change_nothing` completo, con su `<summary>` ("Lo mismo por IIdentityService: …"). Sus 11 reenvíos los cubre `Account_writes_outside_the_boundary_throw_and_change_nothing` y sus 3 escrituras técnicas, `Sign_in_service_follows_its_transaction_rules`. El `using` de `Interfaces.Integrations` se queda por `ISignInService`.

- [ ] **Paso 7: el doble.** En `FakeIdentityService.cs`:
  - la declaración y su resumen pasan a:

    ```csharp
    /// <summary>
    /// Cuentas (IUserReader e IUserRepository) y sesión (ISignInService) en memoria: registra lo que hicieron los casos de
    /// uso para poder verificarlo. La tarea 17 de la Etapa 2 lo parte en InMemoryUserAccounts y FakeSignInService.
    /// </summary>
    internal sealed class FakeIdentityService : IUserReader, IUserRepository, ISignInService
    ```
  - `GetRolesAsync` se borra y `ListRoleNamesForUserAsync` queda así:

    ```csharp
        public Task<IReadOnlyCollection<string>> ListRoleNamesForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>(_roles.GetValueOrDefault(userId) ?? []);
    ```
  - se borran la propiedad `RoleNames` (con su `<summary>`) y los métodos `ListRoleNamesAsync`, `ListRolesAsync`, `FindRoleAsync` y `RoleNameExistsAsync`;
  - se borra `using ArquitecturaBase.Application.Models.Roles.ReadModels;`.

- [ ] **Paso 8: `FakeRoleReader` con datos propios.** En `UserServiceTestHost.cs`, `RoleReader = new FakeRoleReader(Identity);` pasa a `RoleReader = new FakeRoleReader();`, y la clase anidada `FakeRoleReader` completa pasa a:

```csharp
    /// <summary>
    /// Los roles que existen, para que el alta y la edición validen los nombres. UserService solo valida nombres: las demás
    /// lecturas de roles no se usan acá.
    /// </summary>
    internal sealed class FakeRoleReader : IRoleReader
    {
        public List<string> RoleNames { get; } = ["Admin", "User"];

        public Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>(RoleNames);

        public Task<IReadOnlyCollection<RoleListItem>> ListRolesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("UserService only validates role names.");

        public Task<RoleListItem?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("UserService only validates role names.");

        public Task<bool> RoleNameExistsAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("UserService only validates role names.");
    }
```

  En `UserServiceWriteTests.cs` (tres sitios, todos con un solo rol):

  ```bash
  perl -0777 -pi -e 's/host\.Identity\.GetRolesAsync\(/host.Identity.ListRoleNamesForUserAsync(/g' tests/ArquitecturaBase.Application.UnitTests/Services/Users/UserServiceWriteTests.cs
  ```

- [ ] **Paso 9: verlo pasar.** V1 (sin advertencias), el comando del Paso 1 (esperado `exit=0`, con las cinco reglas de `IdentityBoundaryTests`), y:

  Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj --no-build > "$TEMP/etapa2-unit.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-unit.log"`
  Esperado: `exit=0`.

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj --no-build -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.CreateUserWithPhoneTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.MeEmailEndpointsTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.MeWhatsAppEndpointsTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.UnitOfWorkTransactionTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`: la sonda de `StaleIdentityReads` sigue viendo búsquedas escondidas con una sola puerta.

- [ ] **Paso 10: la puerta de la fachada.**

  Run: `git grep -nE "IIdentityService|\bIdentityService\b|IdentityServiceTests|IdentityServiceExtensions" -- src tests`
  Esperado: sin salida. (`FakeIdentityService` todavía existe: se parte en la Tarea 17.)

- [ ] **Paso 11: los documentos.** Verificar cada ruta (sección 9 del Diseño).
  - Reglas de identidad, sección "Reglas": reemplazar el ítem que empieza con "Application usa contratos de Identity y de repositorios/lectores especializados" por:

    ```markdown
    - Application usa contratos de Identity y de repositorios/lectores especializados para usuarios, roles y sesión; `UserManager`/`SignInManager` no salen de Infrastructure. Los datos de cuentas se leen con `IUserReader` y se escriben con `IUserRepository`, incluido el vínculo con Google. `ISignInService` tiene solo lo técnico del ingreso: el bloqueo por intentos fallidos, la cookie de la aplicación, la cookie de Google y el cierre de todas las sesiones de una cuenta. `IdentityBoundaryTests` le pone un tope de 12 miembros, ninguno de datos, y fija que solo el ingreso (código, enlace y Google) abre una sesión y que solo el ingreso por código suma intentos fallidos.
    ```
  - Reglas de WhatsApp, al final del ítem "**La regla de oro: un mensaje de WhatsApp nunca abre una sesión.**" (después de "…y de él se guarda solo el SHA-256."), agregar: " `IdentityBoundaryTests` fija que ningún tipo de `Application/Services/WhatsApp` llame a `ISignInService.SignInAsync`: el bot recibe `ISignInService` solo para mirar el bloqueo."
  - Spec funcional del ingreso por WhatsApp, sección 5 ("La regla de oro: un mensaje de WhatsApp nunca abre una sesión"), primer ítem: reemplazar "La cookie de sesión la crea `IIdentityService.SignInAsync`," por "La cookie de sesión la crea `ISignInService.SignInAsync`, solo desde el ingreso (código, enlace o Google),". El resto del ítem no cambia. Es la única mención de la fachada en los specs funcionales: sin esto, un documento vigente nombraría un contrato que ya no existe y la puerta de la Tarea 28 no saldría vacía.
  - Spec de arquitectura, "Reglas de ubicación y acceso", regla 4: reemplazar "`IdentityService` y `PermissionService` delegan sus consultas de negocio a esos componentes; mantienen operaciones técnicas de Identity." por "`SignInService` (lo técnico del ingreso: el bloqueo, la cookie y el cierre de sesiones) y `PermissionService` usan Identity y OpenIddict sin consultas de negocio: los datos de cuentas van por `IUserReader` e `IUserRepository`."
  - Spec de arquitectura, "Una sola forma de guardar": en el primer ítem, reemplazar "(los locks, `IUserRepository`, `IRoleRepository` e `IIdentityService`, más abajo)" por "(los locks, `IUserRepository`, `IRoleRepository` y las escrituras de `ISignInService`, más abajo)"; y reemplazar el ítem que empieza con "Las escrituras de cuentas y de roles exigen la transacción igual que los locks" por:

    ```markdown
    - Las escrituras de cuentas y de roles exigen la transacción igual que los locks: todas las de `IUserRepository` y `IRoleRepository`, y las de `ISignInService` (los intentos fallidos y el cierre de sesiones). Fuera de un límite lanzan `InvalidOperationException` antes de tocar nada, también cuando un test prepara datos. Los roles se escriben solo por `IRoleRepository`.
    ```
  - Spec de arquitectura, "Tests: arquitectura y arnés": en la oración de `IdentityBoundaryTests` (Tarea 5), reemplazar "y solo `SignInService` toca `SignInManager`, el bloqueo, el security stamp y las revocaciones por sujeto de OpenIddict." por "solo `SignInService` toca `SignInManager`, el bloqueo, el security stamp y las revocaciones por sujeto de OpenIddict; solo el ingreso (código, enlace y Google) abre una sesión, nunca el bot, y solo `LoginCodeVerifier` suma intentos fallidos."

- [ ] **Paso 12: verificación completa.** V2 a V10.
- [ ] **Paso 13: commit.**

```bash
git add src/ArquitecturaBase.Infrastructure/Identity/IdentityRegistration.cs src/ArquitecturaBase.Application/Interfaces/Persistence/IUserReader.cs src/ArquitecturaBase.Application/Interfaces/Persistence/IUserRepository.cs tests/ArquitecturaBase.Api.IntegrationTests/Support/StaleIdentityReads.cs tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UnitOfWorkTransactionTests.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs tests/ArquitecturaBase.Application.UnitTests/Services/Users/UserServiceTestHost.cs tests/ArquitecturaBase.Application.UnitTests/Services/Users/UserServiceWriteTests.cs tests/ArquitecturaBase.ArchitectureTests/IdentityBoundaryTests.cs docs/features/identidad.md docs/features/whatsapp.md docs/specs/2026-09-22-ingreso-whatsapp-design.md docs/architecture/backend.md
git commit -m "$(cat <<'EOF'
refactor!: se borra IIdentityService

Los datos de cuentas van solo por IUserReader e IUserRepository, y lo
técnico del ingreso por ISignInService.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

  (Los tres borrados ya quedaron en el índice con `git rm`.)

---

### Tarea 17: `FakeIdentityService` se parte en `FakeSignInService` e `InMemoryUserAccounts`

Solo tests. El estado de la sesión va por userId y no depende de las cuentas, así que se separa limpio. Los nombres de los campos y propiedades de los tests (`_identity`, `Identity`, `_users`) no cambian: cambia su tipo. Lo que sí cambia es a qué doble van las aserciones de sesión.

**Archivos:**
- Crear: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeSignInService.cs`
- Crear: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Users/InMemoryUserAccounts.cs`
- Borrar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Auth/{AccountServiceTests,ConnectServiceTests,ExternalLoginServiceTests,LoginCodeVerifierParityTests,RequestLoginCodeServiceTests,RequestWhatsAppLoginCodeServiceTests,VerifyLoginCodeServiceTests}.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Users/{AccountAccessRevokerTests,ProfileEmailServiceTests,ProfileServiceTests,UserGuardsTests,UserServiceTestHost,UserServiceStatusTests,UserServicePhoneTests}.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/WhatsApp/{RecordOutboundWhatsAppMessageTests,RecordUnsentWhatsAppMessageTests,WhatsAppDeliveryServiceTests,WhatsAppInboundServiceTests}.cs`
- Modificar: el spec de arquitectura (rol; hoy `docs/architecture/backend.md`)

- [ ] **Paso 1: el doble de la sesión.** Crear `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeSignInService.cs`:

```csharp
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.Identity;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;

/// <summary>
/// ISignInService en memoria: registra lo que hicieron los casos de uso con la sesión y el bloqueo. Su estado va por
/// userId y no depende de las cuentas (InMemoryUserAccounts). Con una lista de eventos, anota "sign-in" en la misma lista
/// en la que FakeUnitOfWork anota "commit": así un test fija en qué orden pasaron.
/// </summary>
/// <remarks>
/// A diferencia de producción, no lanza con una cuenta borrada o que no existe, porque no conoce las cuentas:
/// IsLockedOutAsync devuelve false. Un test que dependa de ese InvalidOperationException va en integración
/// (SignInServiceTests.Operations_reject_a_deleted_or_missing_account).
/// </remarks>
internal sealed class FakeSignInService(List<string>? events = null) : ISignInService
{
    public Dictionary<Guid, int> FailedAttempts { get; } = [];

    public HashSet<Guid> LockedOutUsers { get; } = [];

    public List<Guid> SignedInUsers { get; } = [];

    /// <summary>A quiénes se les cortó el acceso ya emitido.</summary>
    public List<Guid> RevokedUsers { get; } = [];

    public ExternalLogin? PendingExternalLogin { get; set; }

    public bool ExternalSignedOut { get; private set; }

    /// <summary>
    /// Si no es null, las escrituras (los intentos fallidos y el cierre de sesiones) lanzan fuera de la transacción, como
    /// en producción (ver <see cref="TransactionGuard"/>).
    /// </summary>
    public Func<bool>? InTransaction { get; set; }

    public Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(LockedOutUsers.Contains(userId));

    public Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        FailedAttempts[userId] = FailedAttempts.GetValueOrDefault(userId) + 1;

        return Task.CompletedTask;
    }

    public Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        FailedAttempts[userId] = 0;

        return Task.CompletedTask;
    }

    public Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        RevokedUsers.Add(userId);

        return Task.CompletedTask;
    }

    public Task SignInAsync(Guid userId, CancellationToken cancellationToken)
    {
        SignedInUsers.Add(userId);
        events?.Add("sign-in");

        return Task.CompletedTask;
    }

    public Task<ExternalLogin?> GetExternalLoginAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PendingExternalLogin);

    public Task SignOutExternalAsync(CancellationToken cancellationToken)
    {
        ExternalSignedOut = true;

        return Task.CompletedTask;
    }
}
```

- [ ] **Paso 2: el doble de las cuentas.** Crear `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Users/InMemoryUserAccounts.cs`:

```csharp
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users.ReadModels;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Users;

/// <summary>
/// Las cuentas en memoria: IUserReader e IUserRepository sobre la misma lista, como en producción los dos van sobre la
/// misma base. Registra lo que hicieron los casos de uso para poder verificarlo. El bloqueo, la cookie y el cierre de
/// sesiones están en FakeSignInService.
/// </summary>
internal sealed class InMemoryUserAccounts : IUserReader, IUserRepository
{
    public const string DefaultTimeZoneId = "America/Argentina/Buenos_Aires";

    private readonly List<UserAccount> _users = [];
    private readonly Dictionary<(string Provider, string Key), Guid> _externalLogins = [];
    private readonly Dictionary<Guid, string[]> _roles = [];

    public IReadOnlyList<UserAccount> Users => _users;

    public UserListRequest? LastListRequest { get; private set; }

    /// <summary>Cuentas borradas lógicamente: las ve el alta, que las restaura. Acompaña a <see cref="DeletedEmails"/>.</summary>
    public List<UserAccount> DeletedUsers { get; } = [];

    /// <summary>Correos con una cuenta borrada lógicamente: el doble no las guarda en <see cref="Users"/>.</summary>
    public HashSet<string> DeletedEmails { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Si no es null, tomar el lock o escribir fuera de la transacción lanza, como en producción (ver
    /// <see cref="TransactionGuard"/>). Lo que el test arma con <see cref="AddUser"/>, <see cref="SetRoles"/> y
    /// <see cref="LinkExternalLogin"/> no pasa por la guarda.
    /// </summary>
    public Func<bool>? InTransaction { get; set; }

    /// <summary>Una cuenta con el correo verificado, o solo con el número (también verificado) si no hay correo.</summary>
    public UserAccount AddUser(string? email, bool isActive = true, string culture = "es", string? phoneNumber = null)
    {
        var user = new UserAccount(
            Guid.CreateVersion7(),
            email,
            EmailConfirmed: email is not null,
            phoneNumber,
            PhoneNumberConfirmed: phoneNumber is not null,
            DisplayName: null,
            culture,
            DefaultTimeZoneId,
            isActive);
        _users.Add(user);
        _roles[user.Id] = [];

        return user;
    }

    public void SetRoles(Guid userId, params string[] roles) => _roles[userId] = roles;

    public void LinkExternalLogin(Guid userId, string provider, string providerKey) =>
        _externalLogins[(provider, providerKey)] = userId;

    /// <summary>
    /// Arma datos con las escrituras del doble sin pasar por la guarda de <see cref="InTransaction"/>, como
    /// ApiFactory.InTransactionAsync en integración: lo que se prepara antes del caso de uso no es parte de su límite.
    /// </summary>
    public async Task<T> ArrangeAsync<T>(Func<InMemoryUserAccounts, Task<T>> arrange)
    {
        ArgumentNullException.ThrowIfNull(arrange);

        var guard = InTransaction;
        InTransaction = null;

        try
        {
            return await arrange(this);
        }
        finally
        {
            InTransaction = guard;
        }
    }

    /// <inheritdoc cref="ArrangeAsync{T}(Func{InMemoryUserAccounts, Task{T}})"/>
    public Task ArrangeAsync(Func<InMemoryUserAccounts, Task> arrange)
    {
        ArgumentNullException.ThrowIfNull(arrange);

        return ArrangeAsync(async accounts =>
        {
            await arrange(accounts);

            return true;
        });
    }

    public Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_users.SingleOrDefault(user => user.Id == userId));

    public Task<UserAccount?> FindByEmailAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(_users.SingleOrDefault(user => user.Email == email.Value));

    public Task<UserAccount?> FindByExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken) =>
        Task.FromResult(_externalLogins.TryGetValue((provider, providerKey), out var userId)
            ? _users.Single(user => user.Id == userId)
            : null);

    public Task<UserAccount?> FindByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        Task.FromResult(_users.SingleOrDefault(user => user.PhoneNumber == phone.Value));

    public Task<bool> IsDeletedEmailAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(DeletedEmails.Contains(email.Value));

    public Task<bool> IsDeletedPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        Task.FromResult(DeletedUsers.Any(user => user.PhoneNumber == phone.Value));

    public Task<UserAccount?> FindDeletedByEmailAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(DeletedUsers.SingleOrDefault(user => user.Email == email.Value));

    public Task<UserAccount?> FindDeletedByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken) =>
        Task.FromResult(DeletedUsers.SingleOrDefault(user => user.PhoneNumber == phone.Value));

    public Task<bool> HasExternalLoginAsync(Guid userId, string provider, CancellationToken cancellationToken) =>
        Task.FromResult(_externalLogins.Any(login => login.Key.Provider == provider && login.Value == userId));

    public Task<IReadOnlyCollection<string>> ListRoleNamesForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>(_roles.GetValueOrDefault(userId) ?? []);

    public Task<UserDetail?> FindDetailAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = _users.SingleOrDefault(user => user.Id == userId);

        return Task.FromResult(user is null
            ? null
            : new UserDetail(
                user.Id,
                user.Email,
                user.EmailConfirmed,
                user.PhoneNumber,
                user.PhoneNumberConfirmed,
                user.DisplayName,
                user.IsActive,
                default,
                [.. (_roles.GetValueOrDefault(user.Id) ?? []).Order(StringComparer.Ordinal)]));
    }

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_users.Count(user =>
            user.IsActive && (_roles.GetValueOrDefault(user.Id) ?? []).Contains(SystemRoles.Admin, StringComparer.Ordinal)));

    public Task<PagedResult<UserListItem>> ListUsersAsync(UserListRequest request, CancellationToken cancellationToken)
    {
        LastListRequest = request;
        var items = _users.Select(user => new UserListItem(
            user.Id,
            user.Email,
            user.PhoneNumber,
            user.PhoneNumberConfirmed,
            user.DisplayName,
            user.IsActive,
            default,
            _roles.GetValueOrDefault(user.Id) ?? [])).ToList();

        return Task.FromResult(new PagedResult<UserListItem>(items, request.Page, request.PageSize, items.Count));
    }

    /// <summary>Los conteos de verdad se prueban contra la base, en integración: acá solo tiene que existir.</summary>
    public Task<UserFilterCounts> GetUserFilterCountsAsync(UserListRequest request, CancellationToken cancellationToken)
    {
        LastListRequest = request;
        var active = _users.Count(user => user.IsActive);

        return Task.FromResult(new UserFilterCounts(
            new UserStatusCounts(_users.Count, active, _users.Count - active),
            [],
            []));
    }

    public Task LockExternalSignInAsync(
        Email email, string provider, string providerKey, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);

        return Task.CompletedTask;
    }

    public Task<UserAccount> CreateAsync(
        Email? email,
        PhoneNumber? phone,
        bool phoneConfirmed,
        string? displayName,
        string culture,
        CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);

        if (email is null && phone is null)
        {
            throw new ArgumentException("An account needs an email or a phone number.", nameof(email));
        }

        var user = new UserAccount(
            Guid.CreateVersion7(),
            email?.Value,
            EmailConfirmed: email is not null,
            phone?.Value,
            PhoneNumberConfirmed: phone is not null && phoneConfirmed,
            displayName,
            culture,
            DefaultTimeZoneId,
            IsActive: true);
        _users.Add(user);
        _roles[user.Id] = ["User"];

        return Task.FromResult(user);
    }

    /// <summary>El alta de un administrador: el correo y el número quedan sin verificar.</summary>
    public Task<UserAccount> CreateUnverifiedAsync(
        Email? email,
        PhoneNumber? phone,
        string? displayName,
        string culture,
        CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);

        if (email is null && phone is null)
        {
            throw new ArgumentException("An account needs an email or a phone number.", nameof(email));
        }

        var user = new UserAccount(
            Guid.CreateVersion7(),
            email?.Value,
            EmailConfirmed: false,
            phone?.Value,
            PhoneNumberConfirmed: false,
            displayName,
            culture,
            DefaultTimeZoneId,
            IsActive: true);
        _users.Add(user);
        _roles[user.Id] = ["User"];

        return Task.FromResult(user);
    }

    public Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        LinkExternalLogin(userId, login.Provider, login.ProviderKey);

        return Task.CompletedTask;
    }

    public Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        var user = DeletedUsers.Single(user => user.Id == userId);
        DeletedUsers.Remove(user);

        // IsDeletedEmailAsync lee DeletedEmails: los dos tienen que decir lo mismo.
        if (user.Email is not null)
        {
            DeletedEmails.Remove(user.Email);
        }

        _users.Add(user with { DisplayName = displayName, IsActive = true });
        _roles[user.Id] = [];

        return Task.CompletedTask;
    }

    public Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken) =>
        Update(userId, user => user with { Email = email.Value, EmailConfirmed = confirmed });

    public Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken) =>
        Update(userId, user => user with { PhoneNumber = phone.Value, PhoneNumberConfirmed = confirmed });

    public Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken) =>
        Update(userId, user => user with { PhoneNumber = null, PhoneNumberConfirmed = false });

    public Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        _roles[userId] = [.. roles];

        return Task.CompletedTask;
    }

    public Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken) =>
        Update(userId, user => user with { DisplayName = displayName });

    public Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken) =>
        Update(userId, user => user with { IsActive = isActive });

    public Task DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        var user = _users.Single(user => user.Id == userId);
        _users.Remove(user);
        _roles.Remove(userId);
        DeletedUsers.Add(user);

        // IsDeletedEmailAsync lee DeletedEmails: los dos tienen que decir lo mismo.
        if (user.Email is not null)
        {
            DeletedEmails.Add(user.Email);
        }

        return Task.CompletedTask;
    }

    public Task UpdateProfileAsync(
        Guid userId, string? displayName, string culture, string timeZoneId, CancellationToken cancellationToken) =>
        Update(userId, user => user with { DisplayName = displayName, Culture = culture, TimeZoneId = timeZoneId });

    private Task Update(Guid userId, Func<UserAccount, UserAccount> change)
    {
        TransactionGuard.Require(InTransaction);
        var index = _users.FindIndex(user => user.Id == userId);
        _users[index] = change(_users[index]);

        return Task.CompletedTask;
    }
}
```

  `SetDisplayNameAsync` y `SetActiveAsync` pasan por `Update`, que hace lo mismo que el código de `FakeIdentityService` (guarda y reemplazo del registro).

- [ ] **Paso 3: el tipo, en todos los tests.**

  ```bash
  git rm tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs
  perl -pi -e 's/\bFakeIdentityService\b/InMemoryUserAccounts/g' $(git grep -l "FakeIdentityService" -- tests/ArquitecturaBase.Application.UnitTests/Services)
  ```

- [ ] **Paso 4: la sesión va al doble nuevo, archivo por archivo.**
  - `Services/Auth/ExternalLoginServiceTests.cs`:
    - debajo de `private readonly InMemoryUserAccounts _identity = new();`, agregar `    private readonly FakeSignInService _signIn = new();`;
    - en `Service(...)`, el comentario y la guarda pasan a:

      ```csharp
              // Las guardas de los dobles quedan atadas a la unidad del último servicio armado: cada test arma uno solo.
              _identity.InTransaction = () => uow.InTransaction;
              _signIn.InTransaction = () => uow.InTransaction;
      ```
    - el primero de los tres `            _identity,` de `return new(` pasa a `            _signIn,`;
    - `perl -0777 -pi -e 's/_identity\.(PendingExternalLogin|SignedInUsers|ExternalSignedOut|LockedOutUsers)\b/_signIn.$1/g' tests/ArquitecturaBase.Application.UnitTests/Services/Auth/ExternalLoginServiceTests.cs`
  - `Services/Auth/VerifyLoginCodeServiceTests.cs`:
    - debajo de `public InMemoryUserAccounts Identity { get; } = new();`, agregar `        public FakeSignInService SignIn { get; } = new();` (con una línea en blanco antes, como las otras propiedades);
    - en el constructor del fixture, debajo de `Identity.InTransaction = () => UnitOfWork.InTransaction;`, agregar `            SignIn.InTransaction = () => UnitOfWork.InTransaction;`;
    - en `OnCommit`, `SignedInAtCommit = Identity.SignedInUsers.Count;` pasa a `SignedInAtCommit = SignIn.SignedInUsers.Count;`;
    - en `new LoginCodeVerifier(` (el quinto argumento), el tercero de los tres `                    Identity,` pasa a `                    SignIn,` (20 espacios, como los dejó la Tarea 10);
    - `perl -0777 -pi -e 's/fixture\.Identity\.(SignedInUsers|LockedOutUsers|FailedAttempts)\b/fixture.SignIn.$1/g' tests/ArquitecturaBase.Application.UnitTests/Services/Auth/VerifyLoginCodeServiceTests.cs`
  - `Services/Auth/LoginCodeVerifierParityTests.cs`:
    - debajo de `private readonly InMemoryUserAccounts _identity = new();`, agregar `    private readonly FakeSignInService _signIn = new();`;
    - en el constructor, el tercero de los tres `            _identity,` pasa a `            _signIn,`;
    - `perl -0777 -pi -e 's/_identity\.(SignedInUsers|LockedOutUsers|FailedAttempts)\b/_signIn.$1/g' tests/ArquitecturaBase.Application.UnitTests/Services/Auth/LoginCodeVerifierParityTests.cs`
  - `Services/Auth/AccountServiceTests.cs`: debajo de `var identity = new InMemoryUserAccounts();`, agregar `        var signIn = new FakeSignInService();`, y en `new LoginCodeVerifier(` el tercero de los tres `            identity,` pasa a `            signIn,`.
  - `Services/Auth/RequestLoginCodeServiceTests.cs` y `RequestWhatsAppLoginCodeServiceTests.cs`: en `new LoginCodeVerifier(`, el tercero de los tres `                    Identity,` pasa a `                    new FakeSignInService(),`.
  - `Services/Users/AccountAccessRevokerTests.cs`: `private readonly InMemoryUserAccounts _identity = new();` pasa a `private readonly FakeSignInService _signIn = new();`, `new AccountAccessRevoker(_links, _identity, _clock)` pasa a `new AccountAccessRevoker(_links, _signIn, _clock)`, y `Assert.Equal([userId], _identity.RevokedUsers);` pasa a `Assert.Equal([userId], _signIn.RevokedUsers);`.
  - `Services/Users/UserServiceTestHost.cs`: debajo de `public InMemoryUserAccounts Identity { get; } = new();`, agregar `    public FakeSignInService SignIn { get; } = new();`; en el constructor, debajo de `Identity.InTransaction = () => UnitOfWork.InTransaction;`, agregar `        SignIn.InTransaction = () => UnitOfWork.InTransaction;`; y `new AccountAccessRevoker(Links, Identity, Clock)` pasa a `new AccountAccessRevoker(Links, SignIn, Clock)`.
  - `Services/Users/UserServiceStatusTests.cs` y `UserServicePhoneTests.cs`:

    ```bash
    perl -0777 -pi -e 's/host\.Identity\.RevokedUsers/host.SignIn.RevokedUsers/g' tests/ArquitecturaBase.Application.UnitTests/Services/Users/UserServiceStatusTests.cs tests/ArquitecturaBase.Application.UnitTests/Services/Users/UserServicePhoneTests.cs
    ```
  - `Services/Users/ProfileEmailServiceTests.cs`: borrar las cinco aserciones que no podían fallar (`ProfileService` no recibe `ISignInService`): las dos de `Wrong_confirmation_code_saves_the_failed_attempt_without_affecting_session` (`Assert.Empty(fixture.Identity.RevokedUsers);` y `Assert.Empty(fixture.Identity.SignedInUsers);`), la de la prueba del 409 del correo de otra cuenta (`Assert.Empty(fixture.Identity.RevokedUsers);`) y las dos de `Correct_code_sets_verified_email_without_revoking_the_current_session`. Las reemplaza la regla `Only_the_sign_in_code_counts_failed_attempts`; que desvincular desde el perfil no cierra sesiones sigue en `MeWhatsAppEndpointsTests`.

    ```bash
    perl -0777 -pi -e 's/[ \t]*Assert\.Empty\(fixture\.Identity\.(RevokedUsers|SignedInUsers)\);\r?\n//g' tests/ArquitecturaBase.Application.UnitTests/Services/Users/ProfileEmailServiceTests.cs
    ```
  - `Services/WhatsApp/WhatsAppInboundServiceTests.cs`:
    - debajo de `private readonly InMemoryUserAccounts _identity = new();`, agregar `    private readonly FakeSignInService _signIn = new();`;
    - en el constructor, debajo de `_identity.InTransaction = () => _unitOfWork.InTransaction;`, agregar `        _signIn.InTransaction = () => _unitOfWork.InTransaction;`;
    - en `Service()`, el tercero de los tres `            _identity,` pasa a `            _signIn,`;
    - `perl -0777 -pi -e 's/_identity\.(SignedInUsers|LockedOutUsers)\b/_signIn.$1/g' tests/ArquitecturaBase.Application.UnitTests/Services/WhatsApp/WhatsAppInboundServiceTests.cs`

- [ ] **Paso 5: los `using`.** `InMemoryUserAccounts` vive en `TestDoubles.Users`.
  - Agregar `using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;` en `AccountServiceTests`, `ExternalLoginServiceTests`, `LoginCodeVerifierParityTests`, `RequestLoginCodeServiceTests`, `RequestWhatsAppLoginCodeServiceTests`, `VerifyLoginCodeServiceTests`, `ProfileEmailServiceTests`, `ProfileServiceTests`, `UserGuardsTests` y `WhatsAppInboundServiceTests`.
  - En `ConnectServiceTests`, reemplazar `using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;` por `using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;` (no usa ningún otro doble de `Auth`).
  - En `RecordOutboundWhatsAppMessageTests`, `RecordUnsentWhatsAppMessageTests` y `WhatsAppDeliveryServiceTests`, borrar `using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;` (ya tienen el de `Users` y no usan ningún otro doble de `Auth`).
  - `AccountAccessRevokerTests` no necesita el de `Users`: ya no usa las cuentas.

  El build confirma la lista: IDE0005 marca un `using` de más, y CS0246, uno que falta.

- [ ] **Paso 6: verlo pasar.** V1 (sin advertencias) y V3 (unitarios): esperado `exit=0`.

  Run: `git grep -nE "FakeIdentityService" -- tests src; git grep -nE "InTransaction = \(\) =>" -- tests/ArquitecturaBase.Application.UnitTests/Services`
  Esperado: sin `FakeIdentityService`. En la segunda lista, cada fixture que recibe un `FakeSignInService` conecta las dos guardas: `ExternalLoginServiceTests` (`_identity` y `_signIn`), `VerifyLoginCodeServiceTests` (`Identity` y `SignIn`), `UserServiceTestHost` (`Identity` y `SignIn`) y `WhatsAppInboundServiceTests` (`_identity` y `_signIn`); `ProfileEmailServiceTests` y `ProfileServiceTests` conectan solo la de las cuentas, porque no reciben el de la sesión. Una guarda sin conectar deja pasar en silencio.

- [ ] **Paso 7: el spec de arquitectura.** Verificar la ruta. En "Tests: arquitectura y arnés", reemplazar el ítem que empieza con "**Unidad de trabajo en los tests:**" por:

  ```markdown
  - **Unidad de trabajo en los tests:** los unitarios usan `FakeUnitOfWork` (`TestDoubles`), que aplica la misma regla que producción (`CommitPolicyExtensions.Commits`) y cuenta `Transactions`, `Commits`, `Rollbacks` y `LastPolicy`; lo que hay que mirar "al confirmar" se toma en `OnCommit`, y `CommitFailure` hace fallar el commit. Los dobles de lock, `InMemoryUserAccounts` (las cuentas: `IUserReader` e `IUserRepository`) en sus escrituras y `FakeSignInService` en los intentos fallidos y el cierre de sesiones reciben `InTransaction = () => unitOfWork.InTransaction` y lanzan fuera del límite; lo que el test arma con las escrituras de cuentas antes del caso de uso va en `InMemoryUserAccounts.ArrangeAsync`. Con una lista de eventos compartida, `FakeUnitOfWork` anota `"commit"` y `FakeSignInService` anota `"sign-in"`: así un test fija en qué orden pasaron. En integración, `FailingCommitUnitOfWork.Replace(services, probe)` hace fallar el commit sobre la unidad real, y `probe.RolledBackBeforeLeaving` confirma que el rollback lo hizo producción.
  ```

- [ ] **Paso 8: verificación completa.** V2, V4 y V5 a V10.
- [ ] **Paso 9: commit.**

  Antes de agregar, `git status --short -- tests/ArquitecturaBase.Application.UnitTests/Services`. Esperado: solo ` M` de los 18 `.cs` de la lista "Archivos". Si aparece otra cosa, frenar y avisar: no se agrega.

```bash
git add -- tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeSignInService.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Users/InMemoryUserAccounts.cs docs/architecture/backend.md $(git diff --name-only -- tests/ArquitecturaBase.Application.UnitTests/Services | grep -E '\.cs$')
git status --short
```

  Esperado: en el índice, los dos dobles nuevos, el borrado de `FakeIdentityService.cs` (su `git rm` del Paso 3), los `.cs` de `Services` de esta tarea y el spec de arquitectura; afuera, lo ajeno que ya estaba. Si entró un archivo ajeno, sacarlo con `git restore --staged <ruta>`. Después:

```bash
git commit -m "$(cat <<'EOF'
test: FakeIdentityService se parte en FakeSignInService e InMemoryUserAccounts

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 18: la convención de nombres de repositorios y lectores, con su ADR y su test

Documentación y un test de arquitectura nuevo, sin tocar producción. El test nace como trinquete: lista los 9 nombres y los 3 repositorios que todavía no cumplen, y las Tareas 19 a 24 los arreglan y lo vacían. Va después del borrado de `IIdentityService` para no renombrar código que se iba a borrar.

**Archivos:**
- Crear: `tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs`
- Crear: el ADR de la convención (rol; `docs/decisions/0008-nombres-de-repositorios-y-lectores.md`)
- Modificar: el índice de ADR, el spec de arquitectura y el índice de reglas para agentes (roles; hoy `docs/decisions/README.md`, `docs/architecture/backend.md` y `AGENTS.md`)

- [ ] **Paso 1: el test, con las listas vacías.** Crear `tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs`:

```csharp
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

    // Invalidate es una excepción temporal: el caché de los ajustes lo descarta su lector hasta que la tarea 7 de la
    // Etapa 7 lo pase al servicio.
    private const string CacheInvalidationOwner = nameof(ISystemSettingsReader);

    private static readonly string[] Verbs =
    [
        "Get", "Find", "List", "Exists", "Count", "Lock", "Add", "Create", "Update", "Delete", "Set", "Remove", "Restore",
        "Clear", "Invalidate",
    ];

    private static readonly string[] ReaderVerbs = ["Find", "List", "Exists", "Count"];

    // Trinquete de la Etapa 2: los nombres que todavía no siguen la convención. Cada tarea de la 19 a la 23 saca los suyos
    // y la 24 borra la lista. Un nombre nuevo que no la siga no entra acá: se nombra bien.
    private static readonly string[] KnownViolations = [];

    // Trinquete de la Etapa 2: los repositorios que todavía leen sin seguimiento. La 22 y la 24 los sacan.
    private static readonly string[] KnownUntrackedOwners = [];

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

        Assert.Empty(UnknownVerbs().Except(KnownViolations, StringComparer.Ordinal));
    }

    [Fact]
    public void Reads_return_what_their_prefix_promises()
    {
        Assert.Empty(WrongShapes().Except(KnownViolations, StringComparer.Ordinal));
    }

    [Fact]
    public void Readers_only_read()
    {
        Assert.Contains(Contracts, contract => contract.Name.EndsWith("Reader", StringComparison.Ordinal));

        Assert.Empty(ReaderWrites().Except(KnownViolations, StringComparer.Ordinal));
    }

    [Fact]
    public void Known_violations_are_still_violations()
    {
        // Un nombre que ya se arregló sale de la lista en el mismo commit: si no, la lista taparía una violación nueva
        // con el mismo nombre.
        var all = UnknownVerbs().Concat(WrongShapes()).Concat(ReaderWrites()).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(KnownViolations, name => !all.Contains(name));
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

        Assert.Empty(violations.Except(KnownUntrackedOwners, StringComparer.Ordinal));
        Assert.Empty(KnownUntrackedOwners.Except(violations, StringComparer.Ordinal));
    }

    private static IEnumerable<string> UnknownVerbs() =>
        Methods
            .Where(method => VerbOf(method.Name) is not { } verb
                || (verb == "Invalidate" && method.DeclaringType!.Name != CacheInvalidationOwner))
            .Select(NameOf);

    private static IEnumerable<string> WrongShapes() =>
        Methods.Where(method => !HasTheShapeOfItsVerb(method)).Select(NameOf);

    private static IEnumerable<string> ReaderWrites() =>
        Methods
            .Where(method => method.DeclaringType!.Name.EndsWith("Reader", StringComparison.Ordinal))
            .Where(method => VerbOf(method.Name) is not { } verb
                || !(ReaderVerbs.Contains(verb, StringComparer.Ordinal)
                    || (verb == "Invalidate" && method.DeclaringType!.Name == CacheInvalidationOwner)))
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
```

  Run: `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj -- --filter-class "ArquitecturaBase.ArchitectureTests.PersistenceNamingTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; grep -nE "^(con errores|failed) |Collection:" "$TEMP/etapa2-focus.log" | head -30`
  Esperado: `exit=2`, con fallas en `Persistence_methods_start_with_a_known_verb` (`IRoleReader.RoleNameExistsAsync`, `IUserReader.IsDeletedEmailAsync`, `IUserReader.IsDeletedPhoneAsync` e `IUserReader.HasExternalLoginAsync`), `Reads_return_what_their_prefix_promises` (`ILoginAuditRepository.GetLastSuccessAtUtcAsync`, `IPermissionReader.GetUserRoleIdsAsync`, `IPermissionReader.GetRolePermissionsAsync`, `ISystemSettingsReader.GetRegistrationModeAsync` e `IUserReader.GetUserFilterCountsAsync`), `Readers_only_read` (los 8 de los lectores: el mensaje muestra los 5 primeros, de `IPermissionReader.GetUserRoleIdsAsync` a `IUserReader.IsDeletedEmailAsync`, y `···`) y `Only_readers_skip_tracking` (`Persistence.Repositories.LoginAuditRepository`, `Persistence.Repositories.LoginLinkRepository` y `Persistence.Repositories.UserInvitationRepository`). Es el paso rojo que prueba que el detector los ve. xUnit muestra en cada falla hasta 5 elementos y corta cada texto a los 50 caracteres; por eso el test les saca a los dueños el prefijo `ArquitecturaBase.Infrastructure.`. El conjunto exacto lo confirma el Paso 2: con las dos listas llenas, un nombre de más hace fallar su regla y uno de menos, `Known_violations_are_still_violations` o `Only_readers_skip_tracking`. Si aparece un nombre que no está en estas listas, frenar: el Diseño no lo contempla. (Verificado sobre una copia de `42a25c1`: salida exacta en el Paso 1 y `exit=0` con 5 tests en el Paso 2.)

- [ ] **Paso 2: el trinquete.** En el mismo archivo, llenar las dos listas:

```csharp
    private static readonly string[] KnownViolations =
    [
        "ILoginAuditRepository.GetLastSuccessAtUtcAsync",
        "IPermissionReader.GetRolePermissionsAsync",
        "IPermissionReader.GetUserRoleIdsAsync",
        "IRoleReader.RoleNameExistsAsync",
        "ISystemSettingsReader.GetRegistrationModeAsync",
        "IUserReader.GetUserFilterCountsAsync",
        "IUserReader.HasExternalLoginAsync",
        "IUserReader.IsDeletedEmailAsync",
        "IUserReader.IsDeletedPhoneAsync",
    ];
```

```csharp
    private static readonly string[] KnownUntrackedOwners =
    [
        "Persistence.Repositories.LoginAuditRepository",
        "Persistence.Repositories.LoginLinkRepository",
        "Persistence.Repositories.UserInvitationRepository",
    ];
```

  El comando del Paso 1: esperado `exit=0`, 5 tests.

- [ ] **Paso 3: el ADR.** Verificar que `docs/decisions/` exista y que `0008` esté libre (`ls docs/decisions`). Crear `docs/decisions/0008-nombres-de-repositorios-y-lectores.md`:

```markdown
# 0008. Nombres de repositorios y lectores

**Estado:** Aceptada, 2026-09-27.

**Origen:** tarea 5 de la Etapa 2 del [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md). Se aplica en el [plan de la Etapa 2](../plans/2026-09-27-etapa-2-identity-solo-tecnico.md).

## Contexto

Los contratos de `Application/Interfaces/Persistence` usaban `Get` para cosas distintas: una entidad seguida para modificarla (`ILoginCodeRepository.GetLatestAsync`), una colección (`IPermissionReader.GetUserRoleIdsAsync`), un escalar sin seguimiento (`ILoginAuditRepository.GetLastSuccessAtUtcAsync`) y un valor cacheado (`ISystemSettingsReader.GetRegistrationModeAsync`). Los booleanos empezaban con `Is` o `Has`, o terminaban en `Exists`. Por el nombre no se podía saber si lo que volvía estaba seguido por EF, que es lo que decide si se puede modificar y dejar que lo baje el guardado final del límite.

## Decisión

El prefijo de un método de un repositorio o de un lector dice qué devuelve: `Get` una entidad de Domain seguida, solo en repositorios; `Find` una proyección, un registro o un escalar, nunca una entidad; `List` una colección o una página; `Exists` un `bool`; `Count` un número o un registro de conteos; `Lock` un lock de Postgres; `Add` un alta en el contexto; y los verbos de escritura (`Create`, `Update`, `Delete`, `Set`, `Remove`, `Restore`, `Clear`). Un lector solo lee y es el único que llama a `AsNoTracking`. La tabla completa está en [backend.md, "Nombres de repositorios y lectores"](../architecture/backend.md#nombres-de-repositorios-y-lectores).

## Consecuencias

- Se renombran nueve métodos en la Etapa 2, sin cambiar nada de HTTP.
- `IUserInvitationRepository.GetLatestAsync` y `GetLatestSentAsync` conservan el nombre y pasan a devolver la entidad seguida, como toda entidad que devuelve un repositorio.
- `PersistenceNamingTests` lo verifica por reflexión (el prefijo y el tipo de retorno) y por el IL (`AsNoTracking` solo en `Infrastructure/Persistence/Readers`): un método nuevo con otro prefijo rompe los tests.
- Excepciones conocidas: `ISystemSettingsReader.InvalidateAsync`, hasta que la Etapa 7 pase la invalidación al servicio, e `IUserReader.CountActiveAdminsAsync`, que sigue entidades desde un lector y pasa a un `COUNT` en SQL en la Etapa 7. `IRoleReader.FindRoleAsync` pasa a `FindByIdAsync` en la Etapa 4.
- Quedan afuera `IUnitOfWork` y los contratos de `Interfaces/Integrations`, que no son de persistencia.

## Alternativas descartadas

- **`FindLatest…` devolviendo una entidad sin seguimiento para las invitaciones.** Se lee bien, pero "`AsNoTracking` solo en lectores" deja de poder verificarse y pasa a depender de la revisión.
- **Escribir la convención sin un test.** Es la regla que más se degrada con el tiempo: cada método nuevo es una oportunidad de romperla sin que nadie lo note.
- **Una tabla sin los verbos de escritura ni `Lock`.** Dejaría afuera la mitad de los métodos, y el test no podría decir cuándo un nombre es desconocido.
```

  En el índice de ADR (`docs/decisions/README.md`), debajo de la fila del `0007`, agregar:

```markdown
| [0008](0008-nombres-de-repositorios-y-lectores.md) | Los métodos de repositorios y lectores se nombran por lo que devuelven | Aceptada, 2026-09-27 |
```

  Si la confirmación de la Tarea 0 fue otro día, esa es la fecha, en el ADR y en su fila.

- [ ] **Paso 4: el spec de arquitectura.** Verificar la ruta. Entre la sección "Una sola forma de guardar" y "## Migraciones", agregar:

```markdown
## Nombres de repositorios y lectores

La decisión es el [ADR 0008](../decisions/0008-nombres-de-repositorios-y-lectores.md). Rige para todo método de un contrato de `Application/Interfaces/Persistence`, salvo `IUnitOfWork`, y se decide por el tipo de retorno, que es lo que un test puede ver:

| Prefijo | Devuelve | Dónde |
|---|---|---|
| `Get…` | una entidad de Domain (hereda de `Entity`), siempre seguida por EF, para modificarla; a veces con lock de fila, y entonces su XML lo dice; `null` si no existe | solo en `*Repository` |
| `Find…` | una proyección, un registro o un escalar, nunca una entidad; `null` si no existe, o un valor por defecto documentado (`FindRegistrationModeAsync` devuelve `InviteOnly` si no hay fila) | lectores y repositorios |
| `List…` | una colección (no `string`) o `PagedResult<T>` | los dos |
| `Exists…` | `Task<bool>` | los dos |
| `Count…` | `Task<int>`, o un registro cuyo nombre termina en `Counts` (`UserFilterCounts`) | los dos |
| `Lock…` | `Task`: toma un lock de Postgres y exige la transacción | solo repositorios |
| `Add` | `void`: da de alta en el contexto, y lo baja el guardado final del límite | solo repositorios |
| `Create…`, `Update…`, `Delete…`, `Set…`, `Remove…`, `Restore…`, `Clear…`, `Add…Async` | escrituras; exigen la transacción | solo repositorios |
| `Invalidate…` | descarta un caché: excepción temporal, solo en `ISystemSettingsReader`, hasta la tarea 7 de la Etapa 7 | lectores con caché |

- Un lector (`I*Reader`) solo tiene `Find`, `List`, `Exists` y `Count`, y es el único que llama a `AsNoTracking`. Una entidad que devuelve un repositorio está siempre seguida: se puede modificar, y la baja el guardado final del límite.
- Quedan afuera `IUnitOfWork` (su único método lo fija `TransactionBoundaryTests`) y los contratos de `Interfaces/Integrations`, que no son de persistencia: `ISignInService.GetExternalLoginAsync` e `IPermissionService.GetPermissionsAsync` son operaciones técnicas. `IWhatsAppWebhookReader` es un parser del cuerpo del webhook, no un lector de base.
- Excepción conocida: `IUserReader.CountActiveAdminsAsync` trae a memoria, y deja seguidos, a todos los administradores (`UserManager.GetUsersInRoleAsync`), y la regla del IL no lo ve. Pasa a un `COUNT` en SQL en la Etapa 7.
- Lo verifica `PersistenceNamingTests` ([Tests](#tests-arquitectura-y-arnés)). Mientras dura la Etapa 2, el test lista como conocidos los nombres que todavía no cumplen la convención; las tareas 19 a 24 de su plan los renombran y borran la lista.
```

  En "Tests: arquitectura y arnés", al final del ítem de **ArchitectureTests**, agregar: " `PersistenceNamingTests` fija la [convención de nombres](#nombres-de-repositorios-y-lectores) de los contratos de `Interfaces/Persistence` por reflexión, y por el IL que solo los lectores llaman a `AsNoTracking`."

- [ ] **Paso 5: el índice de reglas.** Verificar la ruta. En `AGENTS.md`, sección "Persistencia", debajo del primer ítem ("Los contratos de repositorios y lectores van en…"), agregar:

```markdown
- Los métodos de repositorios y lectores se nombran por lo que devuelven: `Get` una entidad seguida (solo en repositorios), `Find` una proyección, `List`, `Exists`, `Count`, `Lock` y los verbos de escritura; un lector solo lee y es el único que usa `AsNoTracking`. La tabla está en [backend.md, "Nombres de repositorios y lectores"](docs/architecture/backend.md#nombres-de-repositorios-y-lectores) ([ADR 0008](docs/decisions/0008-nombres-de-repositorios-y-lectores.md)), y lo verifica `PersistenceNamingTests`.
```

- [ ] **Paso 6: verificación completa.** V1 a V10.
- [ ] **Paso 7: commit.**

```bash
git add tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs docs/decisions/0008-nombres-de-repositorios-y-lectores.md docs/decisions/README.md docs/architecture/backend.md AGENTS.md
git commit -m "$(cat <<'EOF'
docs: la convención de nombres de repositorios y lectores (ADR 0008) y su test

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 19: `IUserReader` sigue la convención de nombres

Cuatro renombres mecánicos: el compilador los sigue, no cambia nada de HTTP y el front no se entera. `StaleIdentityReads.HiddenLookupNames` usa `nameof`, así que sigue solo. El método de `IUserService` que se llama `GetUserFilterCountsAsync` es un servicio y no se toca, ni su literal de log.

**Archivos:**
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs`
- Modificar: los archivos que nombran `IsDeletedEmailAsync`, `IsDeletedPhoneAsync` o `HasExternalLoginAsync` (en `42a25c1`, después de las tareas anteriores: `IUserReader.cs`, `UserReader.cs`, `IUserRepository.cs` (sus `cref`), `LoginCodeVerifier.cs`, `ExternalLoginService.cs`, `ProfileEmailOperations.cs`, `ProfileWhatsAppOperations.cs`, `ProfileService.cs`, `UserGuards.cs`, `UserWriteOperations.cs`, `InMemoryUserAccounts.cs`, `StaleIdentityReads.cs`, `UserRepositoryTests.cs` y `UserReaderTests.cs`)
- Modificar: `IUserReader.cs`, `UserReader.cs`, `UserService.cs` e `InMemoryUserAccounts.cs` (el de los conteos)

- [ ] **Paso 1: el paso rojo.** En `PersistenceNamingTests.cs`, sacar de `KnownViolations` las cuatro entradas de `IUserReader`.

  Run: `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj -- --filter-class "ArquitecturaBase.ArchitectureTests.PersistenceNamingTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; grep -nE "^(con errores|failed) |Collection:" "$TEMP/etapa2-focus.log" | head -20`
  Esperado: `exit=2`, con los cuatro nombres. La búsqueda muestra cada regla que falla y su colección, sea cual sea el contrato: las Tareas 20 a 24 usan este mismo comando.

- [ ] **Paso 2: buscar literales.** Un nombre escrito como texto no lo sigue el compilador.

  Run: `git grep -nE "\"[^\"]*(IsDeletedEmail|IsDeletedPhone|HasExternalLogin)" -- src tests | grep -v 'cref="'`
  Esperado: sin salida. El `grep -v` saca los `<see cref="…"/>` de la documentación XML, que el compilador sí sigue (un `cref` roto es CS1574) y que el perl del Paso 3 renombra: a esta altura son tres, el de `IsDeletedPhoneAsync` en `IUserReader.cs` y los de `IUserReader.IsDeletedEmailAsync` e `IUserReader.IsDeletedPhoneAsync` en `IUserRepository.cs` (Tarea 16). Si aparece otro literal, se renombra a mano en el Paso 3.

- [ ] **Paso 3: los renombres.**

  ```bash
  perl -0777 -pi -e 's/\bIsDeletedEmailAsync\b/ExistsDeletedByEmailAsync/g; s/\bIsDeletedPhoneAsync\b/ExistsDeletedByPhoneAsync/g; s/\bHasExternalLoginAsync\b/ExistsExternalLoginAsync/g' $(git grep -lE "IsDeletedEmailAsync|IsDeletedPhoneAsync|HasExternalLoginAsync" -- src tests)
  perl -0777 -pi -e 's/Task<UserFilterCounts> GetUserFilterCountsAsync\(UserListRequest/Task<UserFilterCounts> CountByFilterOptionAsync(UserListRequest/' src/ArquitecturaBase.Application/Interfaces/Persistence/IUserReader.cs src/ArquitecturaBase.Infrastructure/Persistence/Readers/UserReader.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Users/InMemoryUserAccounts.cs
  perl -0777 -pi -e 's/userReader\.GetUserFilterCountsAsync\(/userReader.CountByFilterOptionAsync(/' src/ArquitecturaBase.Application/Services/Users/UserService.cs
  ```

  Run: `git grep -nE "IsDeletedEmailAsync|IsDeletedPhoneAsync|HasExternalLoginAsync" -- src tests; git grep -n "GetUserFilterCountsAsync" -- src tests`
  Esperado: la primera, sin salida; la segunda, solo el servicio: `IUserService.cs`, `UserService.cs` (su método público), `UsersController.cs` y `UserServiceTests.cs`.

- [ ] **Paso 4: verlo pasar.** V1 (sin advertencias: un `cref` que no se renombró es CS1574), el comando del Paso 1 (esperado `exit=0`) y V3 (unitarios).
- [ ] **Paso 5: verificación completa.** V2, V4 y V5 a V10.
- [ ] **Paso 6: commit.** Los renombres tocan muchos archivos, así que se agregan por la lista de `git diff` y no a mano, pero nunca por carpeta: la Etapa 5 mantiene `AGENTS.md` y `CLAUDE.md` adentro de `src` y puede tener cambios sin commitear ahí.

  Antes de agregar, `git status --short -- src tests`. Esperado: solo ` M` de archivos `.cs` de la lista "Archivos" de esta tarea. Si aparece otra cosa, frenar y avisar: no se agrega.

```bash
git add -- $(git diff --name-only -- src tests | grep -E '\.cs$')
git status --short
```

  Esperado: en el índice, solo los `.cs` de esta tarea; afuera, lo ajeno que ya estaba (` M README.md` y `?? .codex/`). Si entró un archivo ajeno, sacarlo con `git restore --staged <ruta>` antes de seguir. Después:

```bash
git commit -m "$(cat <<'EOF'
refactor: IUserReader sigue la convención de nombres

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 20: `IRoleReader.ExistsByNameAsync`

**Archivos:**
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs`
- Modificar: los que nombran `RoleNameExistsAsync` (`IRoleReader.cs`, `RoleReader.cs`, `RoleService.cs`, `RoleReaderTests.cs`, `RoleRepositoryTransactionTests.cs`, `RoleServiceTests.cs`, `RoleServiceWriteTests.cs` y `UserServiceTestHost.cs`)

- [ ] **Paso 1: el paso rojo.** Sacar `"IRoleReader.RoleNameExistsAsync"` de `KnownViolations` y correr el comando de la Tarea 19, Paso 1. Esperado: `exit=2`, con ese nombre.
- [ ] **Paso 2: literales.** `git grep -nE "\"[^\"]*RoleNameExists" -- src tests`: esperado sin salida.
- [ ] **Paso 3: el renombre.**

  ```bash
  perl -0777 -pi -e 's/\bRoleNameExistsAsync\b/ExistsByNameAsync/g' $(git grep -l "RoleNameExistsAsync" -- src tests)
  ```

  Run: `git grep -n "RoleNameExistsAsync" -- src tests`
  Esperado: sin salida.

- [ ] **Paso 4: verlo pasar.** V1, el comando del Paso 1 (esperado `exit=0`), V3 y V8.
- [ ] **Paso 5: verificación completa.** V2, V4 a V7, V9 y V10.
- [ ] **Paso 6: commit.** Igual que en la Tarea 19, Paso 6: antes de agregar, `git status --short -- src tests` tiene que mostrar solo ` M` de archivos `.cs` de la lista "Archivos" de esta tarea (si aparece otra cosa, frenar y avisar).

```bash
git add -- $(git diff --name-only -- src tests | grep -E '\.cs$')
git status --short
```

  Esperado: en el índice, solo los `.cs` de esta tarea. Si entró un archivo ajeno, sacarlo con `git restore --staged <ruta>`. Después:

```bash
git commit -m "$(cat <<'EOF'
refactor: IRoleReader.ExistsByNameAsync

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 21: `IPermissionReader` sigue la convención de nombres

Dos renombres, y el privado homónimo de `PermissionService` pasa a `ListRolePermissionsAsync`. `string[]` se queda: es lo que `PermissionService` cachea por rol.

**Archivos:**
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs`
- Modificar: `src/ArquitecturaBase.Application/Interfaces/Persistence/IPermissionReader.cs` (reemplazo completo)
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Readers/PermissionReader.cs`, `src/ArquitecturaBase.Infrastructure/Identity/PermissionService.cs`, `tests/ArquitecturaBase.Api.IntegrationTests/Identity/PermissionServiceTests.cs`

- [ ] **Paso 1: el paso rojo.** Sacar `"IPermissionReader.GetRolePermissionsAsync"` y `"IPermissionReader.GetUserRoleIdsAsync"` de `KnownViolations` y correr el comando de la Tarea 19, Paso 1. Esperado: `exit=2`, con los dos.
- [ ] **Paso 2: el contrato.** Reemplazar `IPermissionReader.cs` completo por:

```csharp
namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Lecturas especializadas para resolver permisos efectivos. Los roles de una cuenta se consultan en cada petición;
/// los claims de cada rol se pueden cachear e invalidar por separado.
/// </summary>
public interface IPermissionReader
{
    /// <summary>Los Id de los roles de la cuenta. Se leen en cada petición: no se cachean.</summary>
    Task<IReadOnlyList<Guid>> ListRoleIdsForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Los permisos del rol: sus claims de permiso, sin los de otro tipo. PermissionService los cachea por rol.
    /// </summary>
    Task<string[]> ListPermissionsForRoleAsync(Guid roleId, CancellationToken cancellationToken);
}
```

- [ ] **Paso 3: los usos.**

  ```bash
  perl -0777 -pi -e 's/\bGetUserRoleIdsAsync\b/ListRoleIdsForUserAsync/g; s/\bGetRolePermissionsAsync\b/ListPermissionsForRoleAsync/g' src/ArquitecturaBase.Infrastructure/Persistence/Readers/PermissionReader.cs tests/ArquitecturaBase.Api.IntegrationTests/Identity/PermissionServiceTests.cs
  perl -0777 -pi -e 's/reader\.GetUserRoleIdsAsync\(/reader.ListRoleIdsForUserAsync(/; s/await GetRolePermissionsAsync\(roleId/await ListRolePermissionsAsync(roleId/; s/private async Task<string\[\]> GetRolePermissionsAsync\(/private async Task<string[]> ListRolePermissionsAsync(/; s/permissionReader\.GetRolePermissionsAsync\(/permissionReader.ListPermissionsForRoleAsync(/' src/ArquitecturaBase.Infrastructure/Identity/PermissionService.cs
  ```

  Run: `git grep -nE "GetUserRoleIdsAsync|GetRolePermissionsAsync" -- src tests`
  Esperado: sin salida.

- [ ] **Paso 4: verlo pasar.** V1, el comando del Paso 1 (esperado `exit=0`) y V8 (incluye `PermissionServiceTests`).
- [ ] **Paso 5: verificación completa.** V2 a V7, V9 y V10.
- [ ] **Paso 6: commit.**

```bash
git add src/ArquitecturaBase.Application/Interfaces/Persistence/IPermissionReader.cs src/ArquitecturaBase.Infrastructure/Persistence/Readers/PermissionReader.cs src/ArquitecturaBase.Infrastructure/Identity/PermissionService.cs tests/ArquitecturaBase.Api.IntegrationTests/Identity/PermissionServiceTests.cs tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs
git commit -m "$(cat <<'EOF'
refactor: IPermissionReader sigue la convención de nombres

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 22: `ILoginAuditRepository.FindLastSuccessAtUtcAsync`

El renombre y, de paso, sale su `AsNoTracking`: proyecta un escalar, así que no seguía nada.

**Archivos:**
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs`
- Modificar: `ILoginAuditRepository.cs`, `LoginAuditRepository.cs`, `ProfileService.cs` y `AuthFakes.cs`

- [ ] **Paso 1: el paso rojo.** Sacar `"ILoginAuditRepository.GetLastSuccessAtUtcAsync"` de `KnownViolations` y `"Persistence.Repositories.LoginAuditRepository"` de `KnownUntrackedOwners`, y correr el comando de la Tarea 19, Paso 1. Esperado: `exit=2`, con los dos.
- [ ] **Paso 2: el renombre y el seguimiento.**

  ```bash
  perl -0777 -pi -e 's/\bGetLastSuccessAtUtcAsync\b/FindLastSuccessAtUtcAsync/g' $(git grep -l "GetLastSuccessAtUtcAsync" -- src tests)
  perl -0777 -pi -e 's/dbContext\.LoginAudits(\r?\n)[ \t]*\.AsNoTracking\(\)\r?\n/dbContext.LoginAudits$1/' src/ArquitecturaBase.Infrastructure/Persistence/Repositories/LoginAuditRepository.cs
  ```

  Run: `git grep -n "GetLastSuccessAtUtcAsync" -- src tests; git grep -n "AsNoTracking" -- src/ArquitecturaBase.Infrastructure/Persistence/Repositories/LoginAuditRepository.cs`
  Esperado: sin salida en los dos.

- [ ] **Paso 3: verlo pasar.** V1, el comando del Paso 1 (esperado `exit=0`), V3 y V6 (el perfil muestra el último ingreso).
- [ ] **Paso 4: verificación completa.** V2, V4, V5 y V7 a V10.
- [ ] **Paso 5: commit.**

```bash
git add src/ArquitecturaBase.Application/Interfaces/Persistence/ILoginAuditRepository.cs src/ArquitecturaBase.Infrastructure/Persistence/Repositories/LoginAuditRepository.cs src/ArquitecturaBase.Application/Services/Users/ProfileService.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/AuthFakes.cs tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs
git commit -m "$(cat <<'EOF'
refactor: ILoginAuditRepository.FindLastSuccessAtUtcAsync

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 23: `ISystemSettingsReader.FindRegistrationModeAsync`

`Find` con un valor por defecto documentado: sin fila no devuelve null ni lanza, devuelve `InviteOnly`.

**Archivos:**
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs`
- Modificar: `src/ArquitecturaBase.Application/Interfaces/Persistence/ISystemSettingsReader.cs` (reemplazo completo)
- Modificar: los que nombran `GetRegistrationModeAsync` (`SystemSettingsReader.cs`, `AccountCreationPolicy.cs`, `SystemSettingsReaderTests.cs`, `AuthFakes.cs` y `SystemSettingsServiceTests.cs`)

- [ ] **Paso 1: el paso rojo.** Sacar `"ISystemSettingsReader.GetRegistrationModeAsync"` de `KnownViolations` (queda vacía: `KnownViolations = [];`) y correr el comando de la Tarea 19, Paso 1. Esperado: `exit=2`, con ese nombre.
- [ ] **Paso 2: el contrato.** Reemplazar `ISystemSettingsReader.cs` completo por:

```csharp
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Lectura cacheada de los ajustes, para el camino del ingreso: se consulta en cada pedido de código y en cada
/// ingreso con Google, así que no puede pegarle a la base todas las veces. Lo implementa Infrastructure sobre
/// HybridCache, igual que los permisos por rol.
/// </summary>
public interface ISystemSettingsReader
{
    /// <summary>
    /// El modo de registro, cacheado. Sin la fila de ajustes no devuelve null ni lanza: devuelve InviteOnly, que es el
    /// modo cerrado.
    /// </summary>
    Task<RegistrationMode> FindRegistrationModeAsync(CancellationToken cancellationToken);

    /// <summary>Descarta lo cacheado: lo que se cambia desde el panel vale al instante, sin reiniciar nada.</summary>
    Task InvalidateAsync(CancellationToken cancellationToken);
}
```

- [ ] **Paso 3: los usos.**

  ```bash
  perl -0777 -pi -e 's/\bGetRegistrationModeAsync\b/FindRegistrationModeAsync/g' $(git grep -l "GetRegistrationModeAsync" -- src tests)
  ```

  Run: `git grep -n "GetRegistrationModeAsync" -- src tests`
  Esperado: sin salida.

- [ ] **Paso 4: verlo pasar.** V1, el comando del Paso 1 (esperado `exit=0`), V3 y V8 (incluye `SystemSettingsReaderTests`) y V5 (`RegistrationModeTests`).
- [ ] **Paso 5: verificación completa.** V2, V4, V6, V7, V9 y V10.
- [ ] **Paso 6: commit.** Igual que en la Tarea 19, Paso 6: antes de agregar, `git status --short -- src tests` tiene que mostrar solo ` M` de archivos `.cs` de la lista "Archivos" de esta tarea (si aparece otra cosa, frenar y avisar).

```bash
git add -- $(git diff --name-only -- src tests | grep -E '\.cs$')
git status --short
```

  Esperado: en el índice, solo los `.cs` de esta tarea. Si entró un archivo ajeno, sacarlo con `git restore --staged <ruta>`. Después:

```bash
git commit -m "$(cat <<'EOF'
refactor: ISystemSettingsReader.FindRegistrationModeAsync

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 24: solo los lectores leen sin seguimiento

Sale el `AsNoTracking` de `LoginLinkRepository.FindUserIdAsync` (proyecta un `Guid`: no seguía nada) y el de `UserInvitationRepository.GetLatestAsync` y `GetLatestSentAsync`, que se quedan con `Get` y pasan a devolver la entidad seguida (decisión 7). No hay cambio observable: nadie modifica esas instancias, el guardado final no encuentra cambios, y `WhatsAppDeliveryService` carga las invitaciones por `GetByIdAsync` en su propio scope. Se borra el trinquete.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/LoginLinkRepository.cs`, `UserInvitationRepository.cs`
- Modificar: `src/ArquitecturaBase.Application/Interfaces/Persistence/IUserInvitationRepository.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs` (reemplazo de las reglas)
- Modificar: el spec de arquitectura (rol; hoy `docs/architecture/backend.md`)

- [ ] **Paso 1: el paso rojo.** En `PersistenceNamingTests.cs`, dejar `KnownUntrackedOwners = [];` y correr el comando de la Tarea 19, Paso 1. Esperado: `exit=2`, con `Persistence.Repositories.LoginLinkRepository` y `Persistence.Repositories.UserInvitationRepository`.
- [ ] **Paso 2: verificar que nadie espera otra instancia.**

  Run: `git grep -nE "invitations\.(GetLatest|GetByIdAsync)" -- src`
  Esperado: `GetLatestSentAsync` y `GetLatestAsync` solo en `UserService.cs`, y `GetByIdAsync` solo en `WhatsAppDeliveryService.cs`, que corre en el scope de la cola de envío. Si aparece un flujo que lea la última invitación y la misma por Id en un solo scope, frenar y preguntarle al usuario.

- [ ] **Paso 3: `LoginLinkRepository`.** Reemplazar:

```csharp
    // Sin seguimiento a propósito: si la fila quedara en el contexto, la lectura de después del lock devolvería esta
    // misma instancia, con lo que había antes de que otro canje la consumiera.
    public Task<Guid?> FindUserIdAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.LoginLinks
            .AsNoTracking()
            .Where(link => link.TokenHash == tokenHash)
```

  por:

```csharp
    // Proyecta solo el Id, así el enlace no queda seguido en el contexto: si quedara, la lectura de después del lock
    // (GetByTokenHashAsync) devolvería esta misma instancia, con lo que había antes de que otro canje la consumiera.
    public Task<Guid?> FindUserIdAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.LoginLinks
            .Where(link => link.TokenHash == tokenHash)
```

- [ ] **Paso 4: `UserInvitationRepository`.** Reemplazar el comentario y los dos métodos:

```csharp
    // Sin seguimiento: las dos se leen para mostrar o para decidir, no para cambiarlas. Van por el índice de la cuenta y
    // la fecha. Desempatan por el Id, que es un Guid v7 y crece con el tiempo.
    public Task<UserInvitation?> GetLatestAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserInvitations
            .AsNoTracking()
            .Where(invitation => invitation.UserId == userId)
```

  por:

```csharp
    // Seguidas, como toda entidad que devuelve un repositorio (Get…): se leen para mostrar o para decidir y nadie las
    // cambia, así que el guardado final del límite no encuentra nada que bajar. Van por el índice de la cuenta y la
    // fecha. Desempatan por el Id, que es un Guid v7 y crece con el tiempo.
    public Task<UserInvitation?> GetLatestAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserInvitations
            .Where(invitation => invitation.UserId == userId)
```

  y en `GetLatestSentAsync` borrar la línea `            .AsNoTracking()`.

  En `IUserInvitationRepository.cs`, los dos resúmenes pasan a:

```csharp
    /// <summary>
    /// La invitación más nueva de la cuenta, se haya podido mandar o no, seguida; null si nunca se la invitó.
    /// </summary>
    Task<UserInvitation?> GetLatestAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// La invitación más nueva de la cuenta que no falló, seguida: la que cuenta para la espera hasta la próxima.
    /// </summary>
    Task<UserInvitation?> GetLatestSentAsync(Guid userId, CancellationToken cancellationToken);
```

  Run: `git grep -n "AsNoTracking" -- src/ArquitecturaBase.Infrastructure/Persistence/Repositories`
  Esperado: sin salida.

- [ ] **Paso 5: sin trinquete.** En `PersistenceNamingTests.cs`:
  - borrar los campos `KnownViolations` y `KnownUntrackedOwners` con sus comentarios, y el test `Known_violations_are_still_violations`;
  - en `Persistence_methods_start_with_a_known_verb`, `Assert.Empty(UnknownVerbs().Except(KnownViolations, StringComparer.Ordinal));` pasa a `Assert.Empty(UnknownVerbs());`;
  - en `Reads_return_what_their_prefix_promises`, `Assert.Empty(WrongShapes().Except(KnownViolations, StringComparer.Ordinal));` pasa a `Assert.Empty(WrongShapes());`;
  - en `Readers_only_read`, `Assert.Empty(ReaderWrites().Except(KnownViolations, StringComparer.Ordinal));` pasa a `Assert.Empty(ReaderWrites());`;
  - en `Only_readers_skip_tracking`, las dos últimas líneas (`Assert.Empty(violations.Except(…));` y `Assert.Empty(KnownUntrackedOwners.Except(…));`) pasan a `Assert.Empty(violations);`.

  Run: el comando de la Tarea 19, Paso 1. Esperado: `exit=0`, 4 tests.

- [ ] **Paso 6: el spec de arquitectura.** Verificar la ruta. En "Nombres de repositorios y lectores", borrar la oración "Mientras dura la Etapa 2, el test lista como conocidos los nombres que todavía no cumplen la convención; las tareas 19 a 24 de su plan los renombran y borran la lista."
- [ ] **Paso 7: verlo pasar.** V1, V5 (canje de enlaces) y V6 (invitaciones: `UserInvitationEndpointsTests`), y:

  Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj --no-build -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.Users.UserInvitationServiceTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0` (su literal `"read:GetLatestSentAsync"` no cambia, porque el nombre tampoco).

- [ ] **Paso 8: verificación completa.** V2 a V4 y V7 a V10.
- [ ] **Paso 9: commit.**

```bash
git add src/ArquitecturaBase.Infrastructure/Persistence/Repositories/LoginLinkRepository.cs src/ArquitecturaBase.Infrastructure/Persistence/Repositories/UserInvitationRepository.cs src/ArquitecturaBase.Application/Interfaces/Persistence/IUserInvitationRepository.cs tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs docs/architecture/backend.md
git commit -m "$(cat <<'EOF'
refactor: solo los lectores leen sin seguimiento

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 25: un verify con commit fallido responde 500 sin cookie y conserva el código

Solo un test de integración, en verde antes y después de la Tarea 26: es la red de regresión del cambio de la cookie del código. Hoy no hay ningún test de `/account/login-code/verify` con `FailingCommitUnitOfWork`.

**Archivos:**
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginCodeEndpointsTests.cs`

- [ ] **Paso 1: el test.** En `LoginCodeEndpointsTests.cs`:
  - agregar `using Microsoft.AspNetCore.TestHost;`;
  - debajo de `Wrong_code_returns_the_attempts_left_in_the_requested_language`, agregar:

```csharp
    /// <summary>
    /// Si el commit del verify falla, la respuesta es un 500 sin la cookie de la aplicación, sin cuenta nueva ni auditoría,
    /// y el mismo código sigue sirviendo. Vale con la cookie escrita adentro del límite (UseExceptionHandler limpia el
    /// Set-Cookie) y con la cookie escrita después del commit (ni se llega a escribir).
    /// </summary>
    [Fact]
    public async Task A_failed_commit_on_verify_answers_500_without_the_cookie_and_keeps_the_code()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("verify-commit");
        var code = await client.RequestCodeAsync(factory, email);
        var probe = new CommitFailureProbe();
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            FailingCommitUnitOfWork.Replace(services, probe)));
        using var failing = api.CreateClient();

        using var failed = await failing.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = ReturnUrl });

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.True(probe.RolledBackBeforeLeaving);
        Assert.False(HasSessionCookie(failed));
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(user => user.Email == email, Ct)));
        Assert.False(await factory.ExecuteDbContextAsync(db => db.LoginAudits.AnyAsync(audit => audit.Identifier == email, Ct)));

        using var retried = await client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = ReturnUrl });

        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.True(HasSessionCookie(retried));
    }
```

  - al final de la clase, debajo de `Login_code_requests_hide_destination_and_code_in_action_logs`, agregar:

```csharp
    private static bool HasSessionCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
        && cookies.Any(cookie => cookie.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
```

- [ ] **Paso 2: verlo pasar.**

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-method "ArquitecturaBase.Api.IntegrationTests.Auth.LoginCodeEndpointsTests.A_failed_commit_on_verify_answers_500_without_the_cookie_and_keeps_the_code" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 8 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=0`, ya con el código de hoy (la cookie adentro del límite). Si falla, frenar: la red no describe el comportamiento actual.

- [ ] **Paso 3: verificación completa.** V1 a V10.
- [ ] **Paso 4: commit.**

```bash
git add tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginCodeEndpointsTests.cs
git commit -m "$(cat <<'EOF'
test: un verify con commit fallido responde 500 sin cookie y conserva el código

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 26: la cookie del ingreso por código sale después del commit (decisión 1, primera mitad)

Un `fix` y el primero de los dos cambios de comportamiento de la etapa (sección 3 del Diseño). `LoginCodeVerifier.VerifyAsync` devuelve el Id de la cuenta y deja de escribir la cookie; `AccountService` la escribe después del límite y solo con éxito. En los unitarios, `FakeSignInService.SignInAsync` pasa a lanzar adentro de un límite y anota `"sign-in"` en la lista de eventos: con el código de hoy, los tests de éxito del verify quedan en rojo.

**Archivos:**
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/TransactionGuard.cs`, `TestDoubles/Auth/FakeSignInService.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Auth/{VerifyLoginCodeServiceTests,LoginCodeVerifierParityTests,AccountServiceTests,RequestLoginCodeServiceTests,RequestWhatsAppLoginCodeServiceTests}.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/IdentityBoundaryTests.cs`
- Modificar: `src/ArquitecturaBase.Application/Services/Auth/LoginCodeVerifier.cs`, `AccountService.cs`
- Modificar: `src/ArquitecturaBase.Application/Interfaces/Integrations/ISignInService.cs`
- Modificar: el spec de arquitectura y las reglas de identidad (roles; hoy `docs/architecture/backend.md` y `docs/features/identidad.md`)

- [ ] **Paso 1: la guarda al revés.** En `TransactionGuard.cs`, debajo de `Require`, agregar:

```csharp
    /// <summary>
    /// Lo contrario de <see cref="Require"/>: lo que corre después del commit (la cookie de la aplicación) lanza adentro
    /// de un límite, como ISignInService.SignInAsync en producción. Con null no controla nada.
    /// </summary>
    public static void RequireNone(Func<bool>? inTransaction)
    {
        if (inTransaction is not null && inTransaction())
        {
            throw new InvalidOperationException(
                "This operation runs after the use case commits: call it outside IUnitOfWork.ExecuteInTransactionAsync.");
        }
    }
```

  En `FakeSignInService.cs`, `SignInAsync` pasa a:

```csharp
    public Task SignInAsync(Guid userId, CancellationToken cancellationToken)
    {
        TransactionGuard.RequireNone(InTransaction);
        SignedInUsers.Add(userId);
        events?.Add("sign-in");

        return Task.CompletedTask;
    }
```

  y el `<summary>` de `InTransaction` pasa a:

```csharp
    /// <summary>
    /// Si no es null, las escrituras (los intentos fallidos y el cierre de sesiones) lanzan fuera de la transacción, y
    /// <see cref="SignInAsync"/> lanza adentro, como en producción (ver <see cref="TransactionGuard"/>).
    /// </summary>
```

- [ ] **Paso 2: el fixture del verify cuenta los eventos.** En `VerifyLoginCodeServiceTests.cs`, reemplazar la clase `Fixture` completa por:

```csharp
    private sealed class Fixture
    {
        public Fixture()
        {
            var codeOptions = Options.Create(new LoginCodeOptions());
            var whatsAppOptions = Options.Create(new WhatsAppLoginOptions());
            var accountCreation = new AccountCreationPolicy(Settings, new FakeInitialAdmin());
            UnitOfWork = new FakeUnitOfWork(Events)
            {
                OnCommit = () =>
                {
                    AuditsAtCommit = Audits.Audits.Count;
                    CodeConsumedAtCommit = Codes.Codes.Any(code => code.ConsumedAtUtc is not null);
                },
            };
            SignIn = new FakeSignInService(Events);
            Codes.InTransaction = () => UnitOfWork.InTransaction;
            Identity.InTransaction = () => UnitOfWork.InTransaction;
            SignIn.InTransaction = () => UnitOfWork.InTransaction;
            Service = new AccountService(
                new FakeGoogleAvailability(false),
                new FakeWhatsAppAvailability(false),
                whatsAppOptions,
                new LoginCodeIssuer(
                    Codes,
                    new FakeLoginCodeGenerator(),
                    new FakeLoginCodeHasher(),
                    codeOptions,
                    whatsAppOptions,
                    Clock,
                    NullLogger<LoginCodeIssuer>.Instance),
                new LoginCodeVerifier(
                    Codes,
                    Audits,
                    Identity,
                    Identity,
                    SignIn,
                    new FakeLoginCodeHasher(),
                    accountCreation,
                    new FakeRequestInfo(),
                    Clock),
                Identity,
                new FakePhoneNumberParser(),
                new FakeWhatsAppOutbox(),
                new FakeEmailTemplateRenderer(),
                new FakeEmailQueue(),
                accountCreation,
                codeOptions,
                new ServiceRequestValidator<RequestLoginCodeRequest>([new RequestLoginCodeRequestValidator()]),
                new ServiceRequestValidator<RequestWhatsAppLoginCodeRequest>([new RequestWhatsAppLoginCodeRequestValidator()]),
                new ServiceRequestValidator<VerifyLoginCodeRequest>([new VerifyLoginCodeRequestValidator(codeOptions)]),
                UnitOfWork,
                Logger);
        }

        /// <summary>"commit" (FakeUnitOfWork) y "sign-in" (FakeSignInService), en el orden en que pasaron.</summary>
        public List<string> Events { get; } = [];

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));

        public InMemoryLoginCodeRepository Codes { get; } = new();

        public InMemoryLoginAuditRepository Audits { get; } = new();

        public InMemoryUserAccounts Identity { get; } = new();

        public FakeSignInService SignIn { get; }

        public FakeSystemSettingsReader Settings { get; } = new();

        public FakeLogger<AccountService> Logger { get; } = new();

        public FakeUnitOfWork UnitOfWork { get; }

        public int AuditsAtCommit { get; private set; }

        public bool CodeConsumedAtCommit { get; private set; }

        public AccountService Service { get; }

        public void IssueEmailCode() => IssueCode(LoginCodeDestination.ForEmail(Email.Create(UserEmail).Value));

        public void IssuePhoneCode() => IssueCode(LoginCodeDestination.ForPhone(PhoneNumber.Create(UserPhone).Value));

        private void IssueCode(LoginCodeDestination destination) => Codes.Add(LoginCode.Issue(
            destination,
            LoginCodePurpose.SignIn,
            requestedByUserId: null,
            FakeLoginCodeHasher.HashOf(destination.Value, LoginCodePurpose.SignIn, RightCode),
            Clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(10),
            maxAttempts: 5));
    }
```

  Sale `SignedInAtCommit`, que fijaba la cookie antes del commit. `AccountService` todavía no recibe `SignIn`: se lo pasa el Paso 7, cuando su constructor lo pida. En los tests:
  - en `Valid_email_code_creates_the_account_signs_in_and_saves_the_code_and_audit`, `Assert.Equal(1, fixture.SignedInAtCommit);` pasa a `Assert.Equal(["commit", "sign-in"], fixture.Events);`;
  - en `Wrong_code_counts_the_attempt_and_saves_failure_audit`, `Assert.Equal(0, fixture.SignedInAtCommit);` pasa a `Assert.Equal(["commit"], fixture.Events);`;
  - en `Commit_failure_does_not_log_success`, debajo de `Assert.Equal(1, fixture.UnitOfWork.Commits);`, agregar `        Assert.Empty(fixture.SignIn.SignedInUsers);`;
  - debajo de `Commit_failure_does_not_log_success`, agregar:

```csharp
    /// <summary>
    /// La cookie de la aplicación sale recién después del commit de la cuenta, el código gastado y la auditoría, como la
    /// de Google: si el commit falla, no hay cookie de una sesión que no quedó guardada.
    /// </summary>
    [Fact]
    public async Task The_cookie_is_issued_only_after_the_commit()
    {
        var fixture = new Fixture();
        var user = fixture.Identity.AddUser(UserEmail);
        fixture.IssueEmailCode();

        var result = await fixture.Service.VerifyLoginCodeAsync(EmailRequest(), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["commit", "sign-in"], fixture.Events);
        Assert.Equal([user.Id], fixture.SignIn.SignedInUsers);
    }
```

- [ ] **Paso 3: la regla de IL.** En `IdentityBoundaryTests.Only_entry_points_open_a_session`, el conjunto pasa a:

```csharp
        Assert.Equal(
            [AuthServices + "AccountService", AuthServices + "ExternalLoginService", AuthServices + "LoginLinkService"],
            owners);
```

- [ ] **Paso 4: verlo fallar.**

  Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.Auth.VerifyLoginCodeServiceTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; grep -nE "This operation runs after" "$TEMP/etapa2-focus.log" | head -5`
  Esperado: `exit=2`. Fallan los que terminan en un ingreso (`Valid_email_code_creates_the_account_signs_in_and_saves_the_code_and_audit`, `Whatsapp_code_creates_a_phone_only_account_and_saves_its_audit`, `Valid_code_confirms_an_existing_email_and_resets_failed_attempts`, `Valid_whatsapp_code_confirms_a_number_loaded_by_an_administrator` y `The_cookie_is_issued_only_after_the_commit`) con "This operation runs after the use case commits": el verifier de hoy escribe la cookie adentro del límite y la guarda del doble lanza. También `Commit_failure_does_not_log_success`, que recibe esa excepción en lugar de "commit failed". Los de error pasan: ahí no hay cookie. Los demás proyectos compilan igual: todavía no cambió ninguna firma.

  Run: `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj -- --filter-method "ArquitecturaBase.ArchitectureTests.IdentityBoundaryTests.Only_entry_points_open_a_session" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 15 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=2`: hoy el dueño es `LoginCodeVerifier` y no `AccountService`. En el mensaje los nombres salen cortados a los 50 caracteres ("Leer una falla", en "Comandos"): `Expected:` tiene `"ArquitecturaBase.Application.Services.Auth.Account"···` y `Actual:`, en su lugar, `"ArquitecturaBase.Application.Services.Auth.LoginCo"···`.

- [ ] **Paso 5: el verifier devuelve la cuenta.** En `LoginCodeVerifier.cs`:
  - el `<summary>` de la clase pasa a:

    ```csharp
    /// <summary>
    /// Verifica el código que llegó por correo o por WhatsApp y devuelve el Id de la cuenta que entra: la cookie la
    /// escribe AccountService, después del commit. Los dos canales recorren el mismo camino (sección 10 del spec del
    /// ingreso con WhatsApp); lo que cambia entre uno y otro lo sabe <see cref="SignInIdentifier"/>.
    /// </summary>
    ```
  - `    public async Task<Result<VerifyLoginCodeResponse>> VerifyAsync(VerifyLoginCodeRequest request, CancellationToken cancellationToken)` pasa a `    public async Task<Result<Guid>> VerifyAsync(VerifyLoginCodeRequest request, CancellationToken cancellationToken)`;
  - se borra la línea `        await signIn.SignInAsync(user.Id, cancellationToken);`;
  - `        return new VerifyLoginCodeResponse(request.ReturnUrl!);` pasa a `        return user.Id;`.

- [ ] **Paso 6: `AccountService` escribe la cookie después del commit.** En `AccountService.cs`:
  - en el constructor, debajo de `    IUserReader users,`, agregar `    ISignInService signIn,`;
  - reemplazar `VerifyLoginCodeAsync` completo por:

```csharp
    public async Task<Result<VerifyLoginCodeResponse>> VerifyLoginCodeAsync(
        VerifyLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogVerifyLoginCodeHandling(logger);

        if (await verifyLoginCodeValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogVerifyLoginCodeFailed(logger, validationError.Code);
            return validationError;
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => verifier.VerifyAsync(request, ct),
            // Un código equivocado cuenta el intento, un bloqueo deja su auditoría y un NotInvited o un Disabled gastan el
            // código: el error también se confirma. Una excepción igual deshace todo.
            CommitPolicy.OnAnyResult,
            cancellationToken);

        if (result.IsFailure)
        {
            LogVerifyLoginCodeFailed(logger, result.Error.Code);
            return result.Error;
        }

        // La cookie de la aplicación sale recién después del commit de la cuenta, el código gastado y la auditoría, como
        // la de Google: SignInAsync lanza adentro de un límite.
        await signIn.SignInAsync(result.Value, cancellationToken);
        LogVerifyLoginCodeHandled(logger);

        return new VerifyLoginCodeResponse(request.ReturnUrl!);
    }
```

  `IAccountService`, `VerifyLoginCodeResponse`, el controller y el contrato HTTP no cambian.

- [ ] **Paso 7: los tests que arman `AccountService`.** El constructor suma `ISignInService` después de `IUserReader`:
  - `VerifyLoginCodeServiceTests.cs`: en `new AccountService(`, reemplazar

    ```csharp
                    Identity,
                    new FakePhoneNumberParser(),
    ```

    por

    ```csharp
                    Identity,
                    SignIn,
                    new FakePhoneNumberParser(),
    ```
  - `AccountServiceTests.cs`: en `new AccountService(`, reemplazar

    ```csharp
                identity,
                new FakePhoneNumberParser(),
    ```

    por

    ```csharp
                identity,
                signIn,
                new FakePhoneNumberParser(),
    ```
  - `RequestLoginCodeServiceTests.cs`: en `new AccountService(`, reemplazar

    ```csharp
                    Identity,
                    new FakePhoneNumberParser(),
    ```

    por

    ```csharp
                    Identity,
                    new FakeSignInService(),
                    new FakePhoneNumberParser(),
    ```
  - `RequestWhatsAppLoginCodeServiceTests.cs`: en `new AccountService(`, reemplazar

    ```csharp
                    Identity,
                    Parser,
    ```

    por

    ```csharp
                    Identity,
                    new FakeSignInService(),
                    Parser,
    ```

  En los cuatro, el patrón es único: en `new LoginCodeVerifier(`, lo que sigue al doble de las cuentas es el de la sesión (Tarea 17), no `new FakePhoneNumberParser()` ni `Parser`.

- [ ] **Paso 8: el verifier ya no abre la sesión, en sus tests.**

  ```bash
  perl -0777 -pi -e 's/[ \t]*Assert\.Equal\(ReturnUrl, result\.Value\.ReturnUrl\);\r?\n//; s/Assert\.Equal\(\[(\w+)\.Id\], _signIn\.SignedInUsers\);/Assert.Equal($1.Id, result.Value);/g; s/[ \t]*Assert\.Empty\(_signIn\.SignedInUsers\);\r?\n//g; s/\bRight_code_for_a_new_email_creates_the_account_and_signs_in\b/Right_code_for_a_new_email_creates_the_account_and_returns_its_id/' tests/ArquitecturaBase.Application.UnitTests/Services/Auth/LoginCodeVerifierParityTests.cs
  ```

  Son 8 `Assert.Equal([…Id], _signIn.SignedInUsers)` que pasan a afirmar el Id devuelto, 7 `Assert.Empty(_signIn.SignedInUsers)` que se borran (el verifier ya no puede abrir una sesión; quién puede lo fija la regla de IL) y el único `result.Value.ReturnUrl`. `_signIn` sigue en uso: el bloqueo y los intentos fallidos.

  Run: `git grep -nE "SignedInUsers|result\.Value\.ReturnUrl" -- tests/ArquitecturaBase.Application.UnitTests/Services/Auth/LoginCodeVerifierParityTests.cs`
  Esperado: sin salida.

- [ ] **Paso 9: el contrato lo dice.** En `ISignInService.cs`, el `<summary>` de `SignInAsync` pasa a:

```csharp
    /// <summary>
    /// Escribe en la respuesta la cookie persistente de la aplicación. No escribe en la base. La llaman solo los puntos de
    /// entrada del ingreso (código, enlace y Google); el código y Google, después del commit y solo con un Result exitoso.
    /// </summary>
```

- [ ] **Paso 10: verlo pasar.** V1 (sin advertencias), V3 (unitarios; esperado `exit=0`), los dos comandos del Paso 4 (esperado `exit=0`) y V5 (Auth; incluye `A_failed_commit_on_verify_answers_500_without_the_cookie_and_keeps_the_code` de la Tarea 25 y `Right_code_starts_a_persistent_secure_session`). Correr también el comando de "Tiempos de las clases de concurrencia".
- [ ] **Paso 11: los documentos.** Verificar cada ruta.
  - Spec de arquitectura, "Una sola forma de guardar": reemplazar el ítem de la cookie de las "Dos excepciones" (el que empieza con "la cookie del ingreso por código (`LoginCodeVerifier`) y la del canje de enlace") por:

    ```markdown
      - la cookie del canje de enlace (`LoginLinkService`) se escribe adentro del límite, antes del commit; la del ingreso por código (`AccountService`) y la de Google, después, como pide la regla 4. Se alinea en la tarea 27 de la Etapa 2, con la guarda de `ISignInService.SignInAsync`.
    ```
  - Spec de arquitectura, "Tests: arquitectura y arnés", en el ítem de **Unidad de trabajo en los tests**: después de "así un test fija en qué orden pasaron.", agregar " `FakeSignInService.SignInAsync`, al revés que las escrituras, lanza adentro del límite: la cookie sale después del commit."
  - Reglas de identidad, al final del primer párrafo: reemplazar ": la cookie del ingreso por código (`LoginCodeVerifier`) y la del canje de enlace (`LoginLinkService`) se escriben adentro del límite, y la de Google, después." por ": la cookie del canje de enlace (`LoginLinkService`) se escribe adentro del límite, y la del ingreso por código (`AccountService`) y la de Google, después."
- [ ] **Paso 12: verificación completa.** V2, V4 y V6 a V10.
- [ ] **Paso 13: commit.**

```bash
git add tests/ArquitecturaBase.Application.UnitTests/TestDoubles/TransactionGuard.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeSignInService.cs tests/ArquitecturaBase.Application.UnitTests/Services/Auth/VerifyLoginCodeServiceTests.cs tests/ArquitecturaBase.Application.UnitTests/Services/Auth/LoginCodeVerifierParityTests.cs tests/ArquitecturaBase.Application.UnitTests/Services/Auth/AccountServiceTests.cs tests/ArquitecturaBase.Application.UnitTests/Services/Auth/RequestLoginCodeServiceTests.cs tests/ArquitecturaBase.Application.UnitTests/Services/Auth/RequestWhatsAppLoginCodeServiceTests.cs tests/ArquitecturaBase.ArchitectureTests/IdentityBoundaryTests.cs src/ArquitecturaBase.Application/Services/Auth/LoginCodeVerifier.cs src/ArquitecturaBase.Application/Services/Auth/AccountService.cs src/ArquitecturaBase.Application/Interfaces/Integrations/ISignInService.cs docs/architecture/backend.md docs/features/identidad.md
git commit -m "$(cat <<'EOF'
fix: la cookie del ingreso por código sale después del commit

Si la cookie falla después de confirmar, el código queda gastado, con su
auditoría de éxito, y la respuesta es un 500: la misma ventana que ya tenía
el ingreso con Google.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 27: la cookie del canje del enlace sale después del commit, y ninguna cookie se escribe adentro de un límite (decisión 1, segunda mitad)

El segundo cambio de comportamiento. `LoginLinkService.RedeemCoreAsync` devuelve el Id y `RedeemAsync` escribe la cookie después del límite; `SignInService.SignInAsync` lanza si hay una transacción abierta. `LoginLinkService` no tiene tests unitarios, y el orden de su cookie no se ve por HTTP (el 500 sin cookie vale antes y después): esta tarea no entra sin `LoginLinkServiceTests`.

**Archivos:**
- Crear: `tests/ArquitecturaBase.Application.UnitTests/Services/Auth/LoginLinkServiceTests.cs`
- Crear: `tests/ArquitecturaBase.ArchitectureTests/Support/UseCaseEntryPoints.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Identity/SignInServiceTests.cs`, `Persistence/UnitOfWorkTransactionTests.cs`, `Auth/LoginLinkTests.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/{IdentityBoundaryTests,TransactionBoundaryTests}.cs`
- Modificar: `src/ArquitecturaBase.Application/Services/Auth/LoginLinkService.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Extensions/TransactionExtensions.cs`, `src/ArquitecturaBase.Infrastructure/Identity/SignInService.cs`
- Modificar: `src/ArquitecturaBase.Application/Interfaces/Integrations/ISignInService.cs` (reemplazo completo), `src/ArquitecturaBase.Application/Interfaces/Persistence/IUnitOfWork.cs`
- Modificar: el spec de arquitectura, el índice de reglas para agentes y las reglas de identidad y de WhatsApp (roles; hoy `docs/architecture/backend.md`, `AGENTS.md`, `docs/features/identidad.md` y `docs/features/whatsapp.md`)

- [ ] **Paso 1: los unitarios del canje, en rojo.** Crear `tests/ArquitecturaBase.Application.UnitTests/Services/Auth/LoginLinkServiceTests.cs`:

```csharp
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.Validation.Auth;
using ArquitecturaBase.Domain.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

/// <summary>
/// El canje del enlace de ingreso (LoginLinkService.RedeemAsync): un límite con OnAnyResult, y la cookie de la aplicación
/// recién después del commit, como en los otros dos ingresos. Lo que se ve por HTTP lo fija LoginLinkTests.
/// </summary>
public sealed class LoginLinkServiceTests
{
    /// <summary>Un token con la forma de los de verdad: 43 caracteres de base64url.</summary>
    private const string Token = "AnaLoginLinkToken0123456789abcdefghijklmnop";

    private const string Phone = "+5493511234567";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Redeeming_signs_in_after_the_commit()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, phoneNumber: Phone);
        fixture.IssueLink(user.Id);

        var result = await fixture.Service.RedeemAsync(new RedeemLoginLinkRequest(Token), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["commit", "sign-in"], fixture.Events);
        Assert.Equal([user.Id], fixture.SignIn.SignedInUsers);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);
        Assert.NotNull(Assert.Single(fixture.Links.Links).ConsumedAtUtc);
        Assert.True(Assert.Single(fixture.Audits.Audits).Succeeded);
    }

    [Fact]
    public async Task Failed_commit_does_not_issue_the_application_cookie()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, phoneNumber: Phone);
        fixture.IssueLink(user.Id);
        fixture.UnitOfWork.CommitFailure = new InvalidOperationException("commit failed");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.RedeemAsync(new RedeemLoginLinkRequest(Token), Ct));

        Assert.Equal("commit failed", exception.Message);
        Assert.Equal(["commit"], fixture.Events);
        Assert.Empty(fixture.SignIn.SignedInUsers);
    }

    /// <summary>El enlace se gasta y queda la auditoría, pero sin cookie: el error también se confirma.</summary>
    [Theory]
    [InlineData("locked out")]
    [InlineData("disabled")]
    public async Task Locked_or_disabled_accounts_spend_the_link_without_the_cookie(string state)
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, isActive: state != "disabled", phoneNumber: Phone);
        if (state == "locked out")
        {
            fixture.SignIn.LockedOutUsers.Add(user.Id);
        }

        fixture.IssueLink(user.Id);

        var result = await fixture.Service.RedeemAsync(new RedeemLoginLinkRequest(Token), Ct);

        Assert.Equal(state == "disabled" ? AccountErrors.DisabledCode : AccountErrors.LockedOutCode, result.Error.Code);
        Assert.Equal(["commit"], fixture.Events);
        Assert.Empty(fixture.SignIn.SignedInUsers);
        Assert.NotNull(Assert.Single(fixture.Links.Links).ConsumedAtUtc);
        Assert.False(Assert.Single(fixture.Audits.Audits).Succeeded);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            UnitOfWork = new FakeUnitOfWork(Events);
            SignIn = new FakeSignInService(Events);
            Links.InTransaction = () => UnitOfWork.InTransaction;
            Accounts.InTransaction = () => UnitOfWork.InTransaction;
            SignIn.InTransaction = () => UnitOfWork.InTransaction;
            Service = new LoginLinkService(
                Links,
                Audits,
                new FakeSecureTokenGenerator(),
                Accounts,
                SignIn,
                new FakePhoneNumberParser(),
                new FakeRequestInfo(),
                Clock,
                new ServiceRequestValidator<PreviewLoginLinkRequest>([new PreviewLoginLinkRequestValidator()]),
                new ServiceRequestValidator<RedeemLoginLinkRequest>([new RedeemLoginLinkRequestValidator()]),
                UnitOfWork,
                NullLogger<LoginLinkService>.Instance);
        }

        /// <summary>"commit" (FakeUnitOfWork) y "sign-in" (FakeSignInService), en el orden en que pasaron.</summary>
        public List<string> Events { get; } = [];

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));

        public InMemoryLoginLinkRepository Links { get; } = new();

        public InMemoryLoginAuditRepository Audits { get; } = new();

        public InMemoryUserAccounts Accounts { get; } = new();

        public FakeSignInService SignIn { get; }

        public FakeUnitOfWork UnitOfWork { get; }

        public LoginLinkService Service { get; }

        public void IssueLink(Guid userId) =>
            Links.Add(LoginLink.Issue(userId, FakeSecureTokenGenerator.HashOf(Token), Clock.GetUtcNow().UtcDateTime));
    }
}
```

  Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.Auth.LoginLinkServiceTests" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; grep -nE "This operation runs after|^(con errores|failed) " "$TEMP/etapa2-focus.log" | head -6`
  Esperado: `exit=2`. `Redeeming_signs_in_after_the_commit` y `Failed_commit_does_not_issue_the_application_cookie` fallan: el canje de hoy llama a `SignInAsync` adentro del límite y la guarda del doble lanza. `Locked_or_disabled_accounts_spend_the_link_without_the_cookie` pasa: fija lo que ya es así.

- [ ] **Paso 2: la guarda de producción, en rojo.** En `SignInServiceTests.cs`, debajo de `Revoking_sessions_outside_a_transaction_throws_before_touching_the_stamp`, agregar:

```csharp
    /// <summary>
    /// La cookie de la aplicación sale después del commit: adentro de un límite, SignInAsync lanza antes de tocar la
    /// respuesta, así que no necesita un HttpContext para fallar. Protege también la regla de oro de WhatsApp: el bot
    /// corre siempre adentro de un límite.
    /// </summary>
    [Fact]
    public async Task Signing_in_inside_a_boundary_throws_before_touching_the_response()
    {
        var user = await CreateAccountAsync("inside");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.InTransactionAsync(services =>
            services.GetRequiredService<ISignInService>().SignInAsync(user.Id, Ct)));

        Assert.Contains("outside IUnitOfWork.ExecuteInTransactionAsync", error.Message, StringComparison.Ordinal);
    }
```

  Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-method "ArquitecturaBase.Api.IntegrationTests.Identity.SignInServiceTests.Signing_in_inside_a_boundary_throws_before_touching_the_response" > "$TEMP/etapa2-focus.log" 2>&1; echo "exit=$?"; tail -n 20 "$TEMP/etapa2-focus.log"`
  Esperado: `exit=2`: hoy lanza otra `InvalidOperationException`, la de `SignInManager` sin `HttpContext`, y el mensaje no coincide.

- [ ] **Paso 3: la clasificación del contrato.** En `UnitOfWorkTransactionTests.Sign_in_service_follows_its_transaction_rules`:
  - el comentario y el arreglo `afterCommit` pasan a:

    ```csharp
            // Después del commit: adentro de un límite lanzan (lo prueba SignInServiceTests).
            string[] afterCommit = [nameof(ISignInService.SignInAsync)];
    ```
  - el comentario de `anywhere` pasa a `        // Leen el bloqueo o tocan solo la cookie externa de la petición.`, y `nameof(ISignInService.SignInAsync),` sale de `anywhere`.

- [ ] **Paso 4: los puntos de entrada, compartidos.** Crear `tests/ArquitecturaBase.ArchitectureTests/Support/UseCaseEntryPoints.cs`:

```csharp
using System.Reflection;

namespace ArquitecturaBase.ArchitectureTests.Support;

/// <summary>
/// Los puntos de entrada de un caso de uso: las clases de Application/Services que implementan un contrato de
/// Interfaces/Services. Solo ellos abren el límite transaccional (TransactionBoundaryTests) y escriben la cookie de la
/// aplicación, después de confirmarlo (IdentityBoundaryTests).
/// </summary>
internal static class UseCaseEntryPoints
{
    private const string ServicesNamespace = "ArquitecturaBase.Application.Services";
    private const string ServiceInterfacesNamespace = "ArquitecturaBase.Application.Interfaces.Services";

    public static bool Contains(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type.ResidesIn(ServicesNamespace)
            && type.GetInterfaces().Any(contract => contract.ResidesIn(ServiceInterfacesNamespace));
    }

    /// <summary>Por nombre, como lo da el IL: el tipo se busca en <paramref name="assemblies"/>.</summary>
    public static bool Contains(IEnumerable<Assembly> assemblies, string typeName) =>
        assemblies.Select(assembly => assembly.GetType(typeName)).OfType<Type>().FirstOrDefault() is { } type
        && Contains(type);
}
```

  En `TransactionBoundaryTests.cs`:
  - borrar las constantes `ServicesNamespace` y `ServiceInterfacesNamespace` y los dos métodos privados `IsUseCaseEntryPoint`;
  - `receivers.Where(type => !IsUseCaseEntryPoint(type))` pasa a `receivers.Where(type => !UseCaseEntryPoints.Contains(type))`;
  - `callers.Where(owner => !IsUseCaseEntryPoint(owner))` pasa a `callers.Where(owner => !UseCaseEntryPoints.Contains(Scanned, owner))`.

  En `IdentityBoundaryTests.Only_entry_points_open_a_session`, debajo del `Assert.Equal` del conjunto, agregar:

```csharp
        // Y todos son puntos de entrada: la cookie sale del método que abre el límite, después de que confirma.
        Assert.All(owners, owner => Assert.True(UseCaseEntryPoints.Contains(Scanned, owner), owner));
```

- [ ] **Paso 5: el canje escribe la cookie después del commit.** En `LoginLinkService.cs`, reemplazar `RedeemAsync` completo por:

```csharp
    public async Task<Result> RedeemAsync(RedeemLoginLinkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogRedeemHandling(logger);

        if (await redeemValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogRedeemFailed(logger, validationError.Code);
            return validationError;
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => RedeemCoreAsync(request, ct),
            // El enlace queda consumido aunque la cuenta esté bloqueada, deshabilitada o borrada, y todo intento sobre una
            // cuenta existente deja su auditoría: el error también se confirma. Una excepción igual deshace todo.
            CommitPolicy.OnAnyResult,
            cancellationToken);

        if (result.IsFailure)
        {
            LogRedeemFailed(logger, result.Error.Code);
            return result.Error;
        }

        // La cookie de la aplicación sale recién después del commit del enlace gastado y la auditoría, como en los otros
        // dos ingresos: SignInAsync lanza adentro de un límite.
        await signIn.SignInAsync(result.Value, cancellationToken);
        LogRedeemHandled(logger);

        return Result.Success();
    }
```

  y en `RedeemCoreAsync`:
  - `    private async Task<Result> RedeemCoreAsync(RedeemLoginLinkRequest request, CancellationToken cancellationToken)` pasa a `    private async Task<Result<Guid>> RedeemCoreAsync(RedeemLoginLinkRequest request, CancellationToken cancellationToken)`;
  - se borran el comentario de dos líneas ("La cookie se escribe adentro, antes del commit: …") y la línea `        await signIn.SignInAsync(user.Id, cancellationToken);` que lo sigue;
  - el `return Result.Success();` final pasa a `return user.Id;`.

  `ILoginLinkService` sigue devolviendo `Task<Result>` y el controller no se toca.

- [ ] **Paso 6: la guarda de producción.** En `TransactionExtensions.cs`, debajo de `RequireTransaction`, agregar el método `RequireNoTransaction` de la sección 4 del Diseño, tal cual. En `SignInService.cs`, `SignInAsync` pasa a:

```csharp
    public async Task SignInAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Después del commit: con el límite abierto, un commit fallido dejaría una sesión de una cuenta, un código o un
        // enlace que no quedaron guardados. Va antes de tocar el HttpContext.
        dbContext.RequireNoTransaction();

        await signInManager.SignInAsync(await userManager.RequireUserAsync(userId, cancellationToken), isPersistent: true);
    }
```

  y su `<summary>` de clase pasa a:

```csharp
/// <summary>
/// ISignInService sobre UserManager, SignInManager y los managers de OpenIddict. Las escrituras (los intentos fallidos y
/// el cierre de sesiones) exigen la transacción del caso de uso con su propio chequeo, y la cookie de la aplicación exige
/// lo contrario: no se escribe adentro de un límite. La cuenta se carga siempre con
/// <see cref="UserManagerExtensions.RequireUserAsync"/>.
/// </summary>
```

- [ ] **Paso 7: el contrato en su forma final.** Reemplazar `ISignInService.cs` completo por el código de la sección 1.1 del Diseño, tal cual.

- [ ] **Paso 8: verlo pasar.** V1 (sin advertencias), los comandos de los Pasos 1 y 2 (esperado `exit=0`), V3, V4 (incluye `IdentityBoundaryTests` y `TransactionBoundaryTests` con `UseCaseEntryPoints`), V5 (Auth: `LoginLinkTests`, `LoginCodeEndpointsTests` y `ExternalLoginTests`, con los tres ingresos después del commit) y V8 (`SignInServiceTests` y `UnitOfWorkTransactionTests`). Correr también el comando de "Tiempos de las clases de concurrencia".
- [ ] **Paso 9: el resumen del test de integración.** En `LoginLinkTests.cs`, el `<summary>` de `A_failed_commit_answers_500_without_the_cookie_and_keeps_the_link` pasa a:

```csharp
    /// <summary>
    /// La cookie del canje sale recién después del commit: si el commit falla, la respuesta es un 500 sin Set-Cookie, y el
    /// enlace queda sin gastar y sin auditoría, así sirve para volver a probar.
    /// </summary>
```

  En `IUnitOfWork.cs`, el `<summary>` de la interfaz pasa a:

```csharp
/// <summary>
/// El límite transaccional de un caso de uso y la única forma de guardar. Lo usa solo el punto de entrada del caso de uso,
/// o sea una clase que implementa un contrato de Interfaces/Services, una vez por cada método que escribe. Adentro van los
/// locks, las lecturas que deciden y todas las escrituras, también las que UserManager y RoleManager guardan por su
/// cuenta: lo hacen sobre el mismo contexto, dentro de esta transacción y con un savepoint cada una. La validación del
/// pedido va antes, y lo que depende del commit (invalidar un caché, la cookie de la aplicación) va después. Helpers,
/// repositorios y lectores nunca confirman.
/// </summary>
```

- [ ] **Paso 10: los documentos.** Verificar cada ruta.
  - Spec de arquitectura, "Una sola forma de guardar", regla 4: "invalidar caché, la cookie de Google, `Notify` y el log de resultado." pasa a "invalidar caché, la cookie de la aplicación (`ISignInService.SignInAsync`, que lanza adentro de un límite), `Notify` y el log de resultado."
  - Spec de arquitectura, mismo ítem: reemplazar el bloque

    ```markdown
      Dos excepciones, que no son el ejemplo a copiar:
      - `WhatsAppWebhookService` llama a otro servicio, `WhatsAppWebhookPersistence`, que es el que abre el límite, porque esa es la unidad que se reintenta: `WhatsAppWebhookRetry` la vuelve a correr en un scope nuevo. Su trabajo devuelve un `Result` que siempre es un éxito, solo para llevar los contadores, porque el límite pide un `Result`;
      - la cookie del canje de enlace (`LoginLinkService`) se escribe adentro del límite, antes del commit; la del ingreso por código (`AccountService`) y la de Google, después, como pide la regla 4. Se alinea en la tarea 27 de la Etapa 2, con la guarda de `ISignInService.SignInAsync`.
    ```

    por

    ```markdown
      Una excepción, que no es el ejemplo a copiar: `WhatsAppWebhookService` llama a otro servicio, `WhatsAppWebhookPersistence`, que es el que abre el límite, porque esa es la unidad que se reintenta: `WhatsAppWebhookRetry` la vuelve a correr en un scope nuevo. Su trabajo devuelve un `Result` que siempre es un éxito, solo para llevar los contadores, porque el límite pide un `Result`.
    ```
  - Spec de arquitectura, "Tests: arquitectura y arnés", en la oración de `IdentityBoundaryTests`: "solo el ingreso (código, enlace y Google) abre una sesión, nunca el bot," pasa a "solo los puntos de entrada del ingreso (`AccountService`, `LoginLinkService` y `ExternalLoginService`) abren una sesión, nunca el bot,".
  - Índice de reglas para agentes (`AGENTS.md`, "Una sola forma de guardar"): "las dos excepciones" pasa a "la excepción".
  - Reglas de identidad: al final del primer párrafo, reemplazar ": la cookie del canje de enlace (`LoginLinkService`) se escribe adentro del límite, y la del ingreso por código (`AccountService`) y la de Google, después." por ": la cookie de la aplicación la escribe solo el punto de entrada de cada ingreso, después del commit." Y en "Reglas", debajo del ítem de los contratos de Identity, agregar:

    ```markdown
    - **La cookie de la aplicación sale después del commit.** La escribe solo el punto de entrada de cada ingreso (`AccountService` para el código, `LoginLinkService` para el enlace y `ExternalLoginService` para Google), después de `ExecuteInTransactionAsync` y solo con un `Result` exitoso: `ISignInService.SignInAsync` lanza adentro de un límite, e `IdentityBoundaryTests` fija quién la llama. Si la cookie falla después del commit, el código o el enlace quedan gastados, con su auditoría de éxito, y la respuesta es un 500: la persona pide otro.
    ```
  - Reglas de WhatsApp: al final de la oración que agregó la Tarea 16 ("…el bot recibe `ISignInService` solo para mirar el bloqueo."), agregar " Además, `SignInAsync` lanza adentro de un límite, y el bot corre siempre en uno."

  Run: `git grep -nE "[Dd]os excepciones|cookie de Google\)|cookie de Google, |se escriben? adentro del límite|adentro del límite, antes del commit" -- AGENTS.md CLAUDE.md docs/architecture docs/features src tests`
  Esperado: sin salida. ("La cookie de Google" a secas sigue en las reglas de identidad, donde la Tarea 16 enumera lo que tiene `ISignInService`: está bien.)

- [ ] **Paso 11: verificación completa.** V2, V6, V7, V9 y V10.
- [ ] **Paso 12: commit.**

```bash
git add tests/ArquitecturaBase.Application.UnitTests/Services/Auth/LoginLinkServiceTests.cs tests/ArquitecturaBase.ArchitectureTests/Support/UseCaseEntryPoints.cs tests/ArquitecturaBase.ArchitectureTests/IdentityBoundaryTests.cs tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Identity/SignInServiceTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UnitOfWorkTransactionTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginLinkTests.cs src/ArquitecturaBase.Application/Services/Auth/LoginLinkService.cs src/ArquitecturaBase.Infrastructure/Persistence/Extensions/TransactionExtensions.cs src/ArquitecturaBase.Infrastructure/Identity/SignInService.cs src/ArquitecturaBase.Application/Interfaces/Integrations/ISignInService.cs src/ArquitecturaBase.Application/Interfaces/Persistence/IUnitOfWork.cs docs/architecture/backend.md AGENTS.md docs/features/identidad.md docs/features/whatsapp.md
git commit -m "$(cat <<'EOF'
fix: la cookie del canje del enlace sale después del commit

Las tres cookies de ingreso salen desde el punto de entrada, después del
commit y solo con éxito, y ISignInService.SignInAsync lanza adentro de un
límite.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 28: puerta de la Etapa 2 (sin commit)

**Archivos:** ninguno.

- [ ] **Paso 1: build y tests.** V1 a V10, con Docker. Esperado: `exit=0` en los diez y el build sin advertencias. En V4, `IdentityBoundaryTests` (5 reglas), `PersistenceNamingTests` (4, sin listas de conocidos) y `TransactionBoundaryTests` (con `Cache_factories_read_in_their_own_scope`). En V9, `ExplicitRouteInventoryTests` con las 41 rutas sin cambios: ningún status ni contrato HTTP cambió, así que no hace falta revisar el front.
- [ ] **Paso 2: la fachada ya no está.**

  Run: `git grep -nE "IIdentityService|\bIdentityService\b|FakeIdentityService|IdentityServiceTests|IdentityServiceExtensions" -- src tests AGENTS.md CLAUDE.md README.md docs/architecture docs/features docs/specs ':!docs/specs/2026-09-18-arquitectura-base-design.md'`
  Esperado: sin salida. (Quedan afuera a propósito `docs/history`, el diseño inicial de `docs/specs`, `docs/plans` y `docs/decisions`: decisión 12. Antes de correrla, verificar las rutas de la sección 9: si la Etapa 5 movió los specs o el diseño inicial, se ajustan la ruta y la exclusión.)

- [ ] **Paso 3: los tests no arman datos con los managers de Identity ni devuelven valores de mentira.**

  Run: `git grep -nE "UserManager<|RoleManager<|SignInManager<" -- tests; git grep -nE "return (true|0);" -- tests/ArquitecturaBase.Api.IntegrationTests`
  Esperado: la primera, sin salida; la segunda, solo `Support/CapturingWhatsAppOutbox.cs`.

- [ ] **Paso 4: una sola forma de cada cosa.**

  Run: `git grep -n "AsNoTracking" -- src/ArquitecturaBase.Infrastructure | grep -v "Persistence/Readers/"; git grep -n "GetOrCreateAsync" -- src; git grep -n "FindByIdAsync(userId.ToString" -- src; git grep -nE "\bSignInAsync\(" -- src/ArquitecturaBase.Application`
  Esperado: la primera, sin salida (y ninguna en `Migrations`); la segunda, solo `Infrastructure/Caching/HybridCacheExtensions.cs`; la tercera, sin salida; la cuarta, las tres llamadas a `signIn.SignInAsync` de `AccountService`, `LoginLinkService` y `ExternalLoginService` (después de su límite), el `SignInAsync` propio de `ExternalLoginService` e `IExternalLoginService`, y la declaración de `ISignInService`. El `\b` deja afuera `IUserRepository.LockExternalSignInAsync` y su llamada en `ExternalLoginService`, que no abren una sesión.

- [ ] **Paso 5: documentación.** La lista "Ediciones de documentos pendientes" del final de este plan está vacía (o todo lo que tenía ya se aplicó y se commiteó). Los documentos de la sección 9 del Diseño describen `ISignInService`, la cookie después del commit, la caché en su propio scope y la convención de nombres.
- [ ] **Paso 6: AppHost.** Si se levantó para probar a mano, `aspire stop`.
- [ ] **Paso 7: árbol.** `git status --short`: solo ` M README.md` y `?? .codex/`, del usuario.

---

### Tarea 29: cerrar la Etapa 2

**Archivos:**
- Modificar: el plan maestro (rol; `docs/plans/2026-09-26-plantilla-estandar-por-etapas.md`)
- Mover: este plan, de `docs/plans/` a `docs/history/plans/`
- Modificar: el ADR 0008 (su enlace al plan)

- [ ] **Paso 1: el plan maestro.** Verificar la ruta y que no tenga cambios ajenos.
  - En "Etapa 2: `IIdentityService` solo técnico", debajo del título, agregar un párrafo que empiece con `**Estado:** cerrada el ` seguido de la fecha del día (`date +%F`) y siga con este texto:

    ```markdown
    con el plan detallado `docs/history/plans/2026-09-27-etapa-2-identity-solo-tecnico.md`, que desde el cierre es histórico. Puerta cumplida: build sin advertencias, `dotnet test` en verde, `IdentityBoundaryTests` y `PersistenceNamingTests` sin listas de conocidos, el inventario de 41 rutas sin cambios y la búsqueda de `IIdentityService` vacía en `src`, `tests` y la documentación vigente. El objetivo, el estado anterior y las tareas de abajo son los planeados; lo que se hizo distinto:
    - `IIdentityService` tenía 36 miembros y no 39 (la Etapa 1 ya había borrado el CRUD de roles), y solo 16 tenían llamadores en src;
    - `ISignInService` quedó con 7 miembros y no 10: `FindByExternalLogin` y `HasExternalLogin` (ahora `ExistsExternalLogin`) se quedaron en `IUserReader`, y `AddExternalLogin` en `IUserRepository`, donde ya estaban. Vive plano en `Application/Interfaces/Integrations`, no en una subcarpeta `Identity/`;
    - los consumidores fueron cinco de los siete de la lista más `AccountAccessRevoker`, que no figuraba: `UserPhoneOperations` y `UserStatusOperations` ya usaban el lector y el repositorio;
    - los archivos de integración que nombraban `IIdentityService` eran 23, no 20;
    - la regla "ningún tipo de `Application.Services` depende de `Microsoft.AspNetCore.Identity`" ya la cubrían `LayerDependencyTests`, `ApplicationPackagesTests` y las referencias del csproj, y no se duplicó. En su lugar entró `IdentityBoundaryTests`: una sola carga por Id de la cuenta para modificarla, `ISignInService` chico y técnico, solo `SignInService` toca la sesión, solo los puntos de entrada del ingreso abren una sesión y solo el ingreso por código suma intentos fallidos;
    - la puerta "`git grep IIdentityService` vacío" se acotó a `src`, `tests` y la documentación vigente, specs funcionales incluidos (el de WhatsApp nombraba `IIdentityService.SignInAsync` y se corrigió): los planes, el diseño inicial y los ADR lo nombran a propósito;
    - además de lo planeado: las tres cookies de ingreso salen después del commit y `SignInAsync` lanza adentro de un límite; toda fábrica de `HybridCache` lee en su propio scope (`HybridCacheExtensions`), lo que deja sin efecto la decisión 14 de la Etapa 1 ("`PermissionService` no se toca"); el bot relee la cuenta vinculada después de su lock; y la convención de nombres quedó en el ADR 0008, con 9 renombres y las invitaciones de `IUserInvitationRepository` seguidas.
    ```
  - En "Etapa 5", en el ítem "**Tarea 3, parcial.**", reemplazar "las cinco reglas, los helpers y las dos excepciones." por "las cinco reglas, los helpers y la excepción.", y "Faltan la convención de nombres y los modelos, que se suman cuando cierre la Etapa 3." por "La convención de nombres la sumó la Etapa 2 (\"Nombres de repositorios y lectores\" y el ADR 0008). Faltan los modelos, que se suman cuando cierre la Etapa 3." Si la Etapa 5 ya cambió ese ítem, se aplica el sentido de los dos reemplazos sobre el texto que haya.
- [ ] **Paso 2: este plan pasa a histórico.**

  ```bash
  git mv docs/plans/2026-09-27-etapa-2-identity-solo-tecnico.md docs/history/plans/2026-09-27-etapa-2-identity-solo-tecnico.md
  ```

  Arriba de todo, antes del título, agregar un párrafo que empiece con `> **HISTÓRICO. Etapa cerrada el ` seguido de la fecha del día y de `.**`, y siga con: " No ejecutar: las casillas sin marcar no son trabajo pendiente y la sub-skill de abajo ya no aplica. Registro de cómo se diseñó y se ejecutó la Etapa 2, desde el commit `docs: plan de la Etapa 2` hasta `fix: la cookie del canje del enlace sale después del commit`. El cierre y lo que se hizo distinto están en la sección \"Etapa 2\" del plan maestro; las reglas vigentes, en las reglas de identidad y en el spec de arquitectura, y la convención de nombres, en el ADR 0008. Donde este documento hable de `IIdentityService`, `IdentityService` o `FakeIdentityService`, describe pasos intermedios que ya no existen." (Una línea en blanco entre ese párrafo y el título.)

- [ ] **Paso 3: los enlaces al plan.**

  Run: `git grep -n "plans/2026-09-27-etapa-2-identity-solo-tecnico" -- AGENTS.md CLAUDE.md docs ':!docs/history/plans/2026-09-27-etapa-2-identity-solo-tecnico.md'`
  Esperado: el ADR 0008 (`../plans/2026-09-27-etapa-2-identity-solo-tecnico.md`), que pasa a `../history/plans/2026-09-27-etapa-2-identity-solo-tecnico.md`, y el plan maestro, que ya lo nombra en `docs/history/plans/`. Cualquier otro enlace al lugar viejo se corrige igual. La búsqueda excluye este mismo plan, ya movido: sus comandos nombran `docs/plans/2026-09-27-…` porque así se ejecutaron, y un plan histórico no se reescribe.

- [ ] **Paso 4: commit.**

```bash
git add docs/plans/2026-09-26-plantilla-estandar-por-etapas.md docs/history/plans/2026-09-27-etapa-2-identity-solo-tecnico.md docs/decisions/0008-nombres-de-repositorios-y-lectores.md
git status --short
git commit -m "$(cat <<'EOF'
docs: cerrar la Etapa 2

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

  Antes del commit, `git status --short` muestra el `git mv` (renombrado) y los dos archivos editados, más los ajenos del usuario sin agregar.

---

## Ediciones de documentos pendientes

Si un documento de la sección 9 del Diseño tiene cambios ajenos sin commitear cuando una tarea lo tiene que editar, la edición no entra en ese commit: se anota acá (tarea, rol del documento y texto exacto a aplicar) y se aplica en un commit `docs:` propio cuando ese documento quede limpio. La Tarea 28 no cierra con esta lista llena.

- (vacía)

---

## Autorrevisión

### Cobertura contra el mapa

Cada miembro de `IIdentityService` y cada pendiente, con la tarea que lo cubre:

| Del mapa | Tarea |
|---|---|
| Los 7 miembros técnicos (`IsLockedOut`, `RegisterFailedAttempt`, `ResetFailedAttempts`, `RevokeSessions`, `SignIn`, `GetExternalLogin`, `SignOutExternal`) | 5 (a `ISignInService` y `SignInService`); consumidores en 6, 7, 8, 10 y 11 |
| Las 4 búsquedas con llamadores (`FindById`, `FindByEmail`, `FindByPhone`, `FindDeletedByPhone`) y las dos de borrados (`IsDeletedEmail`, `IsDeletedPhone`) | 8, 9, 10 y 11 (a `IUserReader`); renombres de las dos últimas en 19 |
| Las 3 escrituras con llamadores (`Create`, `SetPhone`, `SetEmail`) | 10 y 11 (a `IUserRepository`) |
| Los 20 reenvíos sin llamadores en src (`FindByExternalLogin`, `CreateUnverified`, `AddExternalLogin`, `HasExternalLogin`, `RemovePhone`, `GetRoles`, `FindDeletedByEmail`, `Restore`, `SetRoles`, `ListRoleNames`, `FindDetail`, `SetDisplayName`, `SetActive`, `Delete`, `ListRoles`, `FindRole`, `RoleNameExists`, `ListUsers`, `GetUserFilterCounts`, `CountActiveAdmins`) | 13 a 15 (los tests dejan de usarlos) y 16 (se borran); renombres en 19 y 20 |
| `RequireUserAsync` privado (dos copias) | 4 (`UserManagerExtensions.RequireUserAsync` y la regla contra `UserManager.FindByIdAsync`) |
| Pendiente a (la cookie) | 25 (red), 26 (código) y 27 (enlace y guarda) |
| Pendiente b (la fábrica de `PermissionService`) | 3 |
| Pendiente c (las lecturas de roles) | 16 (se borran del contrato y del doble; `FakeRoleReader` propio) |
| Pendiente d (`PermissionServiceTests` en autocommit) | 2 |
| Pendiente e (los `return` sobrantes) | 1, y los de `IdentityServiceTests`/`PermissionServiceTests` en 15 y 2 |
| Pendiente f (la cobertura de las escrituras fuera del límite) | 5 (`Sign_in_service_follows_its_transaction_rules`) y 16 (se borra el test a mano) |
| Hallazgo del bot (decisión 6) | 12 (desactivada: fuga real; borrada: robustez, porque `IsLockedOutAsync` ya lanza) |
| El spec funcional de WhatsApp nombra `IIdentityService.SignInAsync` (vigente en lo funcional según `AGENTS.md`) | 16 (se corrige) y 28 (la puerta mira los specs funcionales) |
| Tareas del plan maestro: 1 (separar) → 5 y 16; 2 (consumidores) → 6 a 11; 3 (tests) → 13 a 17; 4 (una sola lectura) → 4; 5 (convención) → 18 a 24; 6 (blindaje) → 4, 5, 16, 18, 26 y 27 | — |
| La puerta y el cierre | 28 y 29 |

### Diferencias con el diseño aprobado, encontradas al escribir el plan

| Hallazgo | Resolución |
|---|---|
| El diseño decía que la Etapa 5 tenía `docs/architecture` y `docs/features` sin trackear | En `42a25c1` están commiteados (`9b50e97`, `0f2ac10`, `d4ced97`, `42a25c1`): la regla de la sección 9 se simplificó a "verificar la ruta y que no tenga cambios ajenos", con la lista de ediciones pendientes como red |
| El diseño decía que "los 6 fixtures que hoy conectan la guarda conectan las dos" | `ProfileEmailServiceTests` y `ProfileServiceTests` no reciben `ISignInService` (`ProfileService` no lo usa): conectan solo la de las cuentas. Los que conectan las dos son 4 (Tarea 17, Paso 6) |
| El pendiente del plan maestro sobre el comentario de `LoginLinkTests.cs:264` ("CLAUDE.md, Fase 4") | Ya lo corrigió `42a25c1`: no se toca |
| `Add` exige `void`, pero `IUserRepository.AddExternalLoginAsync` devuelve `Task` | La regla de forma aplica a `Add` solo cuando el método se llama exactamente `Add`; `Add…Async` es una escritura, como `Create…` (sección 6 y `PersistenceNamingTests`) |
| El token del enlace tiene que tener la forma de los de verdad para pasar el validador del canje | `LoginLinkServiceTests` usa un token de 43 caracteres de base64url (verificado con `wc -c`) |
| Los `.cs` tienen CRLF en el árbol de trabajo y LF en el índice | Los reemplazos con `perl` capturan el final de línea que tenga el archivo (`(\r?\n)` y `$1`) en lugar de escribir uno fijo |
| El test de `SystemSettingsReaderTests` y los de `RoleRepositoryTransactionTests` usan `ExecuteScopeAsync`/`ExecuteDbContextAsync` con un valor de mentira | Pasan a las sobrecargas sin valor en la Tarea 1, junto con las 26 de `InTransactionAsync` |

### Correcciones de la revisión

La revisión simuló el plan sobre una copia del árbol. Lo que encontró, verificado contra el código, y lo que se encontró al rehacer esta autorrevisión:

| Problema | Corrección | Tareas |
|---|---|---|
| `Assert.Empty(KnownViolations.Where(…))` es xUnit2029, un error con `TreatWarningsAsErrors`: el trinquete no compilaba | `Assert.DoesNotContain(KnownViolations, name => !all.Contains(name))` | 18 |
| Al mudar el test de revocación, `using …Interfaces.Persistence;` quedaba sin uso en `IdentityServiceTests` (IDE0005) | el Paso 1 lo borra | 5 |
| `git grep -n "SignInAsync("` también encontraba `LockExternalSignInAsync(` | `git grep -nE "\bSignInAsync\("` | 28 |
| La búsqueda de literales de la Tarea 19 encontraba los `cref` de la Tarea 16 (y decía que en `42a25c1` no había, cuando `IIdentityService.cs` tenía tres) | filtra `cref="`, que el compilador sigue y el perl renombra | 19 |
| `git grep` no mira `UserManagerExtensions.cs` antes del `git add` | `git grep --untracked` | 4 |
| El spec funcional de WhatsApp, vigente según `AGENTS.md`, nombra `IIdentityService.SignInAsync`, y la puerta lo excluía como "histórico" | se corrige en la Tarea 16 (nueva fila en la sección 9); la puerta mira `docs/specs` salvo el diseño inicial; decisión 12, sección 8 y el cierre, alineados | 16, 28, 29 |
| `git add src tests` contradecía la regla de rutas explícitas, con `AGENTS.md`/`CLAUDE.md` de la Etapa 5 adentro de `src` | `git status` antes, `git add` de los `.cs` de `git diff --name-only`, `git restore --staged` si entra algo ajeno; lo mismo para la carpeta `Services` de la Tarea 17; "Comandos" lo admite | 17, 19, 20, 23 |
| El test y la decisión 6 decían que una cuenta borrada mientras el bot espera recibía un enlace: en producción `IsLockedOutAsync` lanza y la unidad se deshace | el resumen del test, la intro, el paso rojo y la decisión 6 separan desactivada (fuga) de borrada (robustez); `FakeSignInService` documenta que no lanza con una cuenta que no conoce | 12, 17 |
| "`RequireUserAsync`, la única carga de una cuenta para modificarla" era falso: `UserRepository.RestoreAsync` carga la borrada y `RoleSeeder` busca por correo | "la única carga por Id de una cuenta no borrada", con las dos excepciones nombradas en el `<remarks>`, la sección 4 y el spec | 4, 5, 29 |
| El comentario `UserManager<T>` de `IdentityBoundaryTests` hacía fallar la puerta de tests sin managers | ``UserManager`1`` | 4, 28 |
| xUnit corta cada texto a los 50 caracteres: `grep "IdentityService"` no encontraba al dueño | `grep "Identity\.IdentityS"` | 4, 16 |
| Después del `git mv`, la búsqueda de enlaces al plan listaba el propio plan | excluye el plan movido | 29 |
| `Identity,` citado con 24 espacios donde la Tarea 10 deja 20 | 20 espacios y la posición del argumento | 17 |
| (autorrevisión) El comando de la Tarea 19, Paso 1, que reusan las 20 a 24, buscaba solo `IUserReader\.` | busca `^(con errores\|failed) \|Collection:`, que muestra cada regla que falla con su colección | 19 a 24 |
| (autorrevisión) xUnit muestra hasta 5 elementos y corta a 50 caracteres: `Only_readers_skip_tracking` mostraba tres veces `"…Persistence.Reposi"···` | el test les saca a los dueños el prefijo `ArquitecturaBase.Infrastructure.` (`KnownUntrackedOwners` pasa a `Persistence.Repositories.*`); el paso rojo de la 18 dice qué se ve y que el conjunto exacto lo confirma su Paso 2 | 18, 22, 24 |
| (autorrevisión) Los pasos rojos de la regla del caché y de `Only_entry_points_open_a_session` nombraban dueños que el mensaje muestra cortados | el esperado dice qué fragmento se ve; "Comandos" suma "Leer una falla" | 3, 26 |
| (autorrevisión) El paso rojo de la Tarea 27 buscaba `[FAIL]`, que Microsoft Testing Platform no escribe | `^(con errores\|failed) ` | 27 |
| (autorrevisión) El `git log` de la Tarea 0 no miraba `docs/specs`, que ahora tiene un documento de la sección 9 | suma `docs/specs` | 0 |

No se aplicó el caso de control opcional para `RestoreAsync` en `Accounts_are_loaded_with_one_query`: la regla prohíbe `FindByIdAsync` y `RestoreAsync` no lo llama, así que un caso que la nombre no probaría nada que el `<remarks>` no diga.

### Consistencia de nombres y tipos entre tareas

- `ISignInService` (7 miembros, las firmas de la sección 1.1) se crea en la Tarea 5 y solo cambian sus XML en la 26 y la 27. `SignInService(UserManager<ApplicationUser>, SignInManager<ApplicationUser>, IOpenIddictAuthorizationManager, IOpenIddictTokenManager, ApplicationDbContext)`: Tarea 5; la 27 le suma la guarda.
- `UserManagerExtensions.RequireUserAsync(this UserManager<ApplicationUser>, Guid, CancellationToken)`: Tarea 4; la usan `UserRepository`, `IdentityService` (hasta la 16) y `SignInService`.
- `TransactionExtensions.RequireNoTransaction(this DbContext)`: Tarea 27, con el mensaje "This operation runs after the use case commits: call it outside IUnitOfWork.ExecuteInTransactionAsync.", el mismo de `TransactionGuard.RequireNone` (Tarea 26); `SignInServiceTests` busca "outside IUnitOfWork.ExecuteInTransactionAsync".
- `HybridCacheExtensions.GetOrCreateInOwnScopeAsync<TReader, TState, TValue>` y `<TReader, TValue>`: Tarea 3; la regla nombra `ArquitecturaBase.Infrastructure.Caching.HybridCacheExtensions`.
- Los parámetros nuevos: `LoginLinkService(…, ISecureTokenGenerator tokens, IUserReader users, ISignInService signIn, IPhoneNumberParser phoneNumbers, …)` (Tarea 8), `AccountService(…, LoginCodeVerifier verifier, IUserReader users, ISignInService signIn, IPhoneNumberParser phoneNumbers, …)` (Tareas 9 y 26), `LoginCodeVerifier(ILoginCodeRepository, ILoginAuditRepository, IUserReader users, IUserRepository userRepository, ISignInService signIn, ILoginCodeHasher, …)` (Tarea 10), `WhatsAppInboundService(…, IWhatsAppMessageRepository messages, IUserReader users, IUserRepository userRepository, ISignInService signIn, IPhoneNumberParser phoneNumbers, …)` (Tarea 11), `ExternalLoginService(ISignInService signIn, IUserReader users, IUserRepository userRepository, …)` (Tarea 7) y `AccountAccessRevoker(ILoginLinkRepository loginLinks, ISignInService signIn, TimeProvider timeProvider)` (Tarea 6). Los tests de la Tarea 17 y de la 26 pasan los dobles en ese orden.
- Los dobles: `FakeSignInService(List<string>? events = null)` con `SignedInUsers`, `LockedOutUsers`, `FailedAttempts`, `RevokedUsers`, `PendingExternalLogin`, `ExternalSignedOut` e `InTransaction` (Tarea 17; `RequireNone` en la 26); `InMemoryUserAccounts` con `Users`, `AddUser`, `SetRoles`, `LinkExternalLogin`, `ArrangeAsync`, `DeletedUsers`, `DeletedEmails`, `LastListRequest`, `DefaultTimeZoneId` e `InTransaction` (Tarea 17). Los eventos: `"commit"` y `"sign-in"`.
- Los tests nuevos y sus nombres: `SignInServiceTests.{Tenth_failed_attempt_locks_the_account, Resetting_failed_attempts_brings_the_count_back_to_zero, Operations_reject_a_deleted_or_missing_account, Revoking_sessions_outside_a_transaction_throws_before_touching_the_stamp}` (5) y `Signing_in_inside_a_boundary_throws_before_touching_the_response` (27); `UnitOfWorkTransactionTests.Sign_in_service_follows_its_transaction_rules` (5, 27); `PermissionServiceTests.The_cache_factory_reads_on_its_own_connection_and_never_caches_uncommitted_permissions` (3); `WhatsAppInboundServiceTests.A_linked_account_cut_off_while_the_bot_waits_for_its_lock_gets_no_link` (12); `LoginCodeEndpointsTests.A_failed_commit_on_verify_answers_500_without_the_cookie_and_keeps_the_code` (25); `VerifyLoginCodeServiceTests.The_cookie_is_issued_only_after_the_commit` (26); `LoginLinkServiceTests.{Redeeming_signs_in_after_the_commit, Failed_commit_does_not_issue_the_application_cookie, Locked_or_disabled_accounts_spend_the_link_without_the_cookie}` (27); `IdentityBoundaryTests.{Accounts_are_loaded_with_one_query (4), Sign_in_contract_stays_small_and_technical (5), Only_the_sign_in_service_touches_sessions (5), Only_entry_points_open_a_session (16, 26, 27), Only_the_sign_in_code_counts_failed_attempts (16)}`; `PersistenceNamingTests.{Persistence_methods_start_with_a_known_verb, Reads_return_what_their_prefix_promises, Readers_only_read, Only_readers_skip_tracking}` y, hasta la 24, `Known_violations_are_still_violations`; `TransactionBoundaryTests.Cache_factories_read_in_their_own_scope` (3).
- Los renombres de la sección 6 son los mismos en la tabla, en `KnownViolations` (Tarea 18) y en las Tareas 19 a 23, y cada tarea saca exactamente sus entradas. `KnownUntrackedOwners` usa los nombres sin el prefijo del ensamblado (`Persistence.Repositories.LoginAuditRepository`, `…LoginLinkRepository` y `…UserInvitationRepository`), igual que `Only_readers_skip_tracking` y los pasos rojos de las Tareas 18, 22 y 24.
- El comando rojo de `PersistenceNamingTests` se define una vez (Tarea 19, Paso 1; el de la 18 usa la misma búsqueda) y las Tareas 20 a 24 lo reusan: `grep -nE "^(con errores|failed) |Collection:"`.
- "La única carga por Id de una cuenta no borrada para modificarla" dice lo mismo en la sección 4 (XML y prosa), el mapa, el comentario de `Accounts_are_loaded_with_one_query`, el resumen de `Operations_reject_a_deleted_or_missing_account`, la oración del spec de las Tareas 4 y 5 y el cierre de la 29; las dos excepciones (`UserRepository.RestoreAsync` y `RoleSeeder`) se nombran igual en todos.
- `FakeSignInService` no lanza con una cuenta borrada o que no existe (su `<remarks>`, Tarea 17), y eso es lo que el paso rojo de la Tarea 12 y el resumen de su test explican para el caso `deleted`.

### Ejecutabilidad (qué se verificó y cómo)

- **Filtros y tandas.** Las seis tandas de V5 a V10 se contaron con `dotnet test --list-tests` sobre `42a25c1` (119, 190, 198, 127, 114 y 207, que suman los 955 tests de integración) y `--filter-namespace` resultó exacto, sin los namespaces hijos. Ningún comando mezcla `--filter-class` con `--filter-method`.
- **Reemplazos mecánicos.** Se probaron sobre copias fuera del árbol: el de los `return` de la Tarea 1 (borra exactamente 6, 2, 4, 14, 3, 2, 2 y 9 líneas, y conserva CRLF), el de los tipos de las Tareas 13 y 14 (6 lecturas pasan a `IUserReader` y todo lo demás a `IUserRepository`) y los de los consumidores de las Tareas 7, 8, 9 y 11 (cada `git grep` de control da lo que el paso dice).
- **Lo que se compiló y se corrió en la autorrevisión.** Sobre una copia de `42a25c1` en el directorio temporal de la sesión (`git archive`, fuera del árbol y sin worktree): `PersistenceNamingTests` tal como queda en la Tarea 18 compila sin advertencias (con el `Assert.Empty(….Where(…))` de antes, xUnit2029), su Paso 1 da `exit=2` con exactamente los nombres del esperado y su Paso 2, `exit=0` con 5 tests; sacar las entradas de las Tareas 20 y 22 da `exit=2` con esos nombres y `Persistence.Repositories.LoginAuditRepository`. El paso rojo de la Tarea 4 da `exit=2` y `grep "Identity\.IdentityS"` encuentra al dueño, que `grep "IdentityService"` no encontraba. El formato de los mensajes se midió con xUnit v3 4.0.1 bajo Microsoft Testing Platform: hasta 5 elementos por colección, textos cortados a 50 caracteres, nombres de tipo (`Assert.IsType`) enteros y el encabezado `con errores <test>`. La puerta de la Tarea 28 con `docs/specs ':!…'` y `\bSignInAsync\(` se corrieron sobre el árbol de hoy.
- **Lo que no se compiló.** El plan no se ejecutó entero: los bloques de código nuevo se escribieron contra el árbol de `42a25c1` leyendo cada tipo que usan (namespaces de `UserAccount`, `UserDetail`, `UserListItem`, `ExternalLogin`, `ExternalLoginProviders`, `PagedResult<T>`, `Entity`, `AccountErrors`, `LoginLinkErrors`; firmas de `IRoleRepository`, `LoginLink.Issue`, `ISecureTokenGenerator.HasTokenFormat`, `FakeUnitOfWork` y `CallSites`). Los pasos rojos dicen qué tiene que fallar y cómo; si algo no falla o falla distinto, se frena. Lo que llegue a `main` después de `42a25c1` lo cubre el Paso 5 de la Tarea 0.
