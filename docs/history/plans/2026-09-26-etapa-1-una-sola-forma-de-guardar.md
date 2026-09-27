> **HISTÓRICO. Etapa cerrada el 2026-09-27.** No ejecutar: las casillas sin marcar no son trabajo pendiente y la sub-skill de abajo ya no aplica. Registro de cómo se diseñó y se ejecutó la Etapa 1 (commits `40d7669` a `4681d2e`). El cierre y lo que se hizo distinto están en la sección "Etapa 1" del [plan maestro](../../plans/2026-09-26-plantilla-estandar-por-etapas.md); la decisión vigente, en el [ADR 0001](../../decisions/0001-transaccion-explicita-por-caso-de-uso.md), y las reglas vigentes, en la sección "Persistencia" de `CLAUDE.md`. Donde este documento hable de `IUnitOfWork.SaveChangesAsync` o de las listas `Known*`, describe pasos intermedios que ya no existen. Después del cierre, `0513c8a` hizo que las escrituras de cuentas también exijan la transacción, lo que revierte la decisión 11, y borró el CRUD de roles de `IIdentityService`.

# Etapa 1: una sola forma de guardar — plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDA: usá `superpowers:subagent-driven-development` (recomendado) o `superpowers:executing-plans` para ejecutar este plan tarea por tarea. Los pasos usan casillas (`- [ ]`) para el seguimiento. Cada tarea termina en un commit directo en `main` (sin ramas, sin push) y con `dotnet build ArquitecturaBase.slnx` sin advertencias y `dotnet test` en verde, con Docker levantado.

**Objetivo:** que en cada caso de uso el límite transaccional se lea en el servicio y sea uno solo (`IUnitOfWork.ExecuteInTransactionAsync`), sin cambiar el comportamiento observable salvo dos mejoras explícitas: la atomicidad de `UpdateUser` sin correo ni número, y que la fábrica de caché de `SystemSettingsReader` deje de leer sobre la conexión y la transacción de quien llama.

**Arquitectura:** decisión D1 del plan maestro (`docs/plans/2026-09-26-plantilla-estandar-por-etapas.md`) y `docs/decisions/0001-transaccion-explicita-por-caso-de-uso.md`. Identity sigue autoguardando (`AutoSaveChanges` no se toca), pero adentro de una transacción que abre el punto de entrada del caso de uso. Los locks dejan de abrir transacciones propias y, al final de la etapa, la exigen. `IUnitOfWork.SaveChangesAsync` convive durante la migración y se borra al cerrarla. Un test de arquitectura que lee el IL (Mono.Cecil) funciona como trinquete: cada commit achica la lista de infractores conocidos.

**Stack:** .NET 10, ASP.NET Core MVC, EF Core 10 + Npgsql 10 (READ COMMITTED, savepoints automáticos de EF), ASP.NET Core Identity, OpenIddict 7.7.1, FluentValidation, xUnit v3 (Microsoft Testing Platform), Testcontainers `postgres:18.3`, NetArchTest 1.3.2 + Mono.Cecil 0.11.3.

**Fuentes de este plan:** la sección "Etapa 1" del plan maestro, el mapa transaccional verificado de los 75 métodos que escriben (con sus notas por área) y el diseño final aprobado. Los números de línea que aparecen como "hoy :N" son del árbol al 2026-09-26 y pueden correrse con las tareas anteriores: el texto citado es la referencia, no el número.

---

## Diseño

### 1. La API

Todo el contrato vive en `src/ArquitecturaBase.Application/Interfaces/Persistence`. Application depende solo de Domain, y `Result` está en `ArquitecturaBase.Domain.Results`.

```csharp
// CommitPolicy.cs
public enum CommitPolicy
{
    OnSuccess = 0,    // solo se confirma el éxito; un Result fallido deshace todo, autoguardados de Identity incluidos
    OnAnyResult = 1,  // se confirma con cualquier Result (intentos, códigos o enlaces gastados, auditorías)
}

// CommitPolicyExtensions.cs: la única definición de la regla, para producción y para los dobles
public static bool Commits(this CommitPolicy policy, Result result) =>
    result.IsSuccess || policy == CommitPolicy.OnAnyResult;

// IUnitOfWork.cs, forma final
Task<TResult> ExecuteInTransactionAsync<TResult>(
    Func<CancellationToken, Task<TResult>> work,
    CommitPolicy policy,
    CancellationToken cancellationToken)
    where TResult : Result;
```

Diferencias con la firma del plan maestro:

- un solo método genérico `where TResult : Result`, en lugar de dos sobrecargas: `Result<T>` hereda de `Result`, y cada doble implementa un solo método;
- `CommitPolicy` es obligatoria y no tiene valor por defecto: la decisión de guardar ante un error queda escrita en cada servicio;
- el `CancellationToken` va último (CA1068);
- `TResult` se infiere solo desde un método `*CoreAsync` tipado. Con una lambda que mezcla `return error` y `return valor`, se escribe el tipo: `ExecuteInTransactionAsync<Result<Guid>>(...)`.

`IUnitOfWork.SaveChangesAsync` queda **transitorio** desde la Tarea 3 hasta la Tarea 21: fuera de un límite conserva el comportamiento de hoy y adentro lanza `InvalidOperationException`. No se marca `[Obsolete]`: con `TreatWarningsAsErrors`, el CS0618 rompería el build en cada llamador sin migrar.

Tipos nuevos:

| Tipo | Dónde | Visibilidad | Tarea |
|---|---|---|---|
| `CommitPolicy`, `CommitPolicyExtensions` | `Application/Interfaces/Persistence` | públicos | 2 |
| `AdvisoryLockKeys` | `Infrastructure/Persistence/Extensions` | internal | 5 |
| `TransactionExtensions.RequireTransaction(this DbContext)` | `Infrastructure/Persistence/Extensions` | internal | 6 |
| `AccountAccessRevoker` | `Application/Services/Users` | internal sealed, scoped | 10 |

No se borra ningún contrato público: `IWhatsAppWebhookPersistence` e `IWhatsAppWebhookRetry` quedan como están.

### 2. Semántica de `UnitOfWork.ExecuteInTransactionAsync`

1. **Chequeos de entrada, antes de abrir nada:** `work` null → `ArgumentNullException`; una `CommitPolicy` fuera del enum → `ArgumentOutOfRangeException`; si esta instancia ya está dentro de un límite (anidar) → `InvalidOperationException`; si `Database.CurrentTransaction` no es null (una transacción que no abrió este límite) → `InvalidOperationException`, sin confirmarla, deshacerla ni adoptarla. Rige desde la Tarea 3, sin fase de adopción: cada servicio se migra junto con sus helpers.
2. **Apertura:** `BeginTransactionAsync(cancellationToken)` sin nivel explícito, o sea READ COMMITTED. No se sube el aislamiento: "leer la cuenta después del lock" (`ConcurrencyStamp`, `LoginLinkRepository.FindUserIdAsync` sin seguimiento) necesita ver lo que el otro acaba de confirmar. Se abre antes de la primera llamada a Identity o a un lock.
3. **Durante el trabajo:** `UserManager` y `RoleManager` autoguardan todo el change tracker compartido, dentro de la transacción y con el savepoint automático de EF. Un 23505 en un autoguardado vuelve solo a su savepoint; si el repositorio lo traduce (`CreateUnverifiedAsync`, `SetEmailAsync`, `SetPhoneAsync`) y el trabajo lo atrapa, la transacción sigue usable. Un error de SQL crudo (55P03 del `NOWAIT`, 40P01) aborta la transacción de Postgres: el trabajo nunca lo atrapa. Las revocaciones de OpenIddict (UPDATE masivos) y el SQL de los locks corren en el acto, adentro. Los advisory locks son reentrantes (misma conexión y misma transacción).
4. **Decisión:** `policy.Commits(result)`. Éxito → se confirma. Fallido con `OnSuccess` → se deshace sin bajar nada y se devuelve el mismo `Result`. Fallido con `OnAnyResult` → se baja todo y se confirma, y se devuelve el `Result` fallido. Un `ValidationError` nunca llega acá: el patrón valida antes del límite.
5. **Guardado final:** solo en el camino que confirma, un único `dbContext.SaveChangesAsync(cancellationToken)` justo antes del commit, con los interceptores de siempre. Sin cambios pendientes, solo va el `COMMIT`.
6. **Commit:** `CommitAsync(cancellationToken)` con el token del pedido, como hoy.
7. **Rollback:** siempre con `CancellationToken.None`. Traga `DbException` (conexión cortada) e `InvalidOperationException` (un commit fallido ya cerró la transacción) y registra un Warning con el nombre del tipo, sin mensaje ni valores. Después, siempre, `ChangeTracker.Clear()`. La transacción se descarta en un `finally`.
8. **Excepciones:** ante cualquier excepción del trabajo, del guardado final o del commit (`OperationCanceledException` y `DbUpdateConcurrencyException` incluidas), el rollback es explícito y ocurre **antes** de relanzar: el reintento del webhook corre en otra conexión del mismo pedido y no puede quedar esperando los advisory locks del primero. Esa garantía necesita que ninguna consulta de **otro** pedido corra sobre la conexión del límite. Hoy una sí puede: la fábrica de `HybridCache` de `SystemSettingsReader` lee con el contexto de quien llama, y `AccountCreationPolicy` la invoca adentro de los límites del ingreso, de Google y del bot. Con la protección contra estampidas, un pedido B se suma a la fábrica que arrancó A. Si A se cancela, su rollback encuentra la conexión ocupada (`NpgsqlOperationInProgressException`, que es un `DbException` y se traga) y los locks quedan tomados hasta que se descarta el contexto. B, a su vez, termina en un 500 cuando el scope de A se cierra debajo de la fábrica. La Tarea 7 hace que esa fábrica lea con su propio scope. `PermissionService` tiene el mismo patrón, pero se lee solo fuera de todo límite (la autorización y el `GetAsync` del perfil) y queda como está.
9. **23505:** un `DbUpdateException` con `PostgresException` 23505 que escapa del límite sale como `UniqueConstraintViolationException` (`UniqueViolations.Translate`), con la original como `InnerException`. Una ya traducida pasa tal cual. SQL crudo (55P03, 40P01, 23503, 25P02) ni se traduce ni se reintenta.
10. **Sin reintentos:** el trabajo nunca se vuelve a correr (autoguardados, colas de correo y de WhatsApp). Si algún día se activa `EnableRetryOnFailure`, `BeginTransactionAsync` lanza, y no se arregla envolviendo el trabajo en la estrategia.
11. **Logs:** un solo `[LoggerMessage]` nuevo, el Warning de rollback fallido, con `{ExceptionType}`. Los servicios registran `Handling` antes del límite y `Handled` o `Failed(código)` después: un commit fallido no deja un `Handled`.
12. **Efectos fuera de la base:** encolar el correo o el WhatsApp y `MarkSent` van adentro, antes del commit, como hoy. La cookie del ingreso por código y la del canje de enlace siguen adentro. Invalidar `HybridCache`, la cookie de Google e `IWhatsAppInboundSignal.Notify` van después y solo si se confirmó.
13. **Locks, desde la Tarea 20:** todos los `Lock*` y las lecturas con lock de fila exigen una transacción activa (`RequireTransaction`). En `AcquireAdvisoryLocksAsync` el chequeo va antes de la salida por lista vacía. El orden: ordinal y sin repetir dentro de una llamada; entre llamadas lo fija quien llama (contactos antes que cuenta; `login-code:` del correo y después el del número, en dos llamadas).
14. **Transición (Tareas 3 a 20):** `SaveChangesAsync` fuera de un límite hace lo de hoy; adentro lanza. Así un helper sin migrar no cierra el límite a mitad de camino (la trampa de `UnitOfWork.cs:31-36`, que confirma cualquier transacción actual).
15. **Fuera a propósito, documentado:** la retención de mensajes (`ExecuteUpdate` en autocommit), `ConnectService.RevokeAuthorizationAsync` (un UPDATE de OpenIddict), los seeders, las migraciones, el servidor OpenIddict y Data Protection.

### 3. El patrón canónico

1. **Afuera y antes del límite:** `ArgumentNullException.ThrowIfNull`, `LogHandling`, el validador del pedido (un pedido inválido no abre transacción) y los guards que lanzan por configuración donde ya estén hoy antes de la primera lectura. Todo lo demás va adentro y en el orden de hoy. Mientras existan los `*Operations`, exponen `Validate*Async` y el core; el servicio llama al primero afuera y al segundo adentro. Un guard de configuración que hoy vive en un `*Operations` se expone igual, como método propio que el servicio llama afuera (`ProfileWhatsAppOperations.EnsureEnabled`).
2. **Un solo límite por método público que escribe:** `unitOfWork.ExecuteInTransactionAsync(ct => XxxCoreAsync(request, ct), CommitPolicy.<política>, cancellationToken)`, con la política siempre escrita. `OnAnyResult` lleva un comentario que dice qué queda registrado.
3. **Adentro, en este orden:** los locks en el orden global (`login-code:` del correo y del número en dos llamadas; filas de contactos; `login-link:` o `user-invitation:`), las lecturas de lo que se va a modificar después de los locks, las reglas (devuelven `Error`), las escrituras, y los efectos que tienen que quedar marcados en la fila (encolar y `MarkSent`). Se puede atrapar la `UniqueConstraintViolationException` que traduce un repositorio; nunca un error de SQL crudo.
4. **Afuera, después y solo si se confirmó:** invalidar caché, la cookie de Google, `Notify`, y los logs `Handled`/`Failed` con `result.Error.Code`. Nunca un try/catch alrededor del límite; la única excepción es el webhook, que reintenta en un scope nuevo.
5. **Los helpers** (`*Operations`, `*Issuer`, `*Verifier`, `*Linker`, `PhoneNumberChange`, `UserInvitationSender`, `AccountAccessRevoker`, `UserGuards`) nunca reciben `IUnitOfWork` ni guardan. Un servicio nunca llama al método de escritura de otro: anidar lanza. Las consultas no abren límite.

Ejemplo, con el área de referencia (D4):

```csharp
public async Task<Result> UpdateAsync(UpdateRoleRequest request, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(request);
    LogHandling(logger, "UpdateRole");

    // 1. Afuera: validar el pedido. Un pedido inválido no abre transacción ni toma locks.
    if (await updateValidator.ValidateAsync(request, cancellationToken) is { } validationError)
    {
        LogFailed(logger, "UpdateRole", validationError.Code);
        return validationError;
    }

    // 2. Un solo límite, con la política escrita: un error de negocio no deja nada.
    var result = await unitOfWork.ExecuteInTransactionAsync(
        ct => UpdateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

    // 3. Afuera, después del commit y solo si se confirmó.
    if (result.IsSuccess)
    {
        await permissionService.InvalidateRoleAsync(request.RoleId, cancellationToken);
    }

    LogOutcome("UpdateRole", result);
    return result;
}
```

Variante `OnAnyResult` (verify de código):

```csharp
var result = await unitOfWork.ExecuteInTransactionAsync(
    ct => verifier.VerifyAsync(request, ct),
    // Un código equivocado cuenta el intento, un bloqueo deja su auditoría y un NotInvited o un Disabled gastan el
    // código: el error también se confirma. Una excepción igual deshace todo.
    CommitPolicy.OnAnyResult,
    cancellationToken);
```

### 4. Tabla por método

"Adentro" es el trabajo, en el orden de ejecución. "Después" es lo que corre fuera y solo si se confirmó (los logs siempre van después).

| Método | Política | Adentro | Después | Cambio de comportamiento | Tarea |
|---|---|---|---|---|---|
| `RoleService.CreateAsync` | OnSuccess | `RoleNameExistsAsync`; `repository.CreateAsync` (rol + un claim por permiso, autoguardados) | log | Ninguno visible: la transacción pasa de `RoleRepository.InTransactionAsync` al servicio. El TOCTOU del nombre sigue (500 con `UniqueConstraintViolationException` en el log) | 6 |
| `RoleService.UpdateAsync` | OnSuccess | `FindRoleAsync`; reglas de rol del sistema y de Admin; `RoleNameExistsAsync`; `repository.UpdateAsync` | `InvalidateRoleAsync`, log | Ninguno. Orden `update, commit, invalidate` | 6 |
| `RoleService.DeleteAsync` | OnSuccess | `FindRoleAsync`; `IsSystemRole`; `UserCount`; `repository.DeleteAsync` | `InvalidateRoleAsync`, log | Ninguno (el TOCTOU de `UserCount` sigue) | 6 |
| `SystemSettingsService.UpdateAsync` | OnSuccess | `GetAsync`; NotFound; `SetRegistrationMode` | `reader.InvalidateAsync`, log | Ninguno: el UPDATE pasa a una transacción explícita | 7 |
| `SystemSettingsReader.GetRegistrationModeAsync` (lectura) | no abre límite | la fábrica de `HybridCache` lee en un scope propio (otro contexto y otra conexión) | — | **Mejora explícita (§2.8):** la fábrica deja de correr sobre la conexión y la transacción de quien llama. Un cambio sin confirmar de quien lee ya no se cachea (test nuevo en rojo antes) | 7 |
| `UserService.CreateUserAsync` | OnSuccess | parseos; `IdentityRequired`; `Check` de la invitación; roles; `login-code:` correo y después número (dos llamadas); `CreateOrRestoreAsync` (con catch del 23505); `SetRolesAsync`; `invitationSender.SendAsync` (lock `user-invitation:` reentrante, `Add`, encolado) | log | Ninguno en datos; un error después de un autoguardado deshace en el acto | 8 |
| `UserService.UpdateUserAsync` | OnSuccess | parseos; `login-code:` correo y número; si hay número, `PhoneNumberChange.LockAsync`; `FindByIdAsync`; país; roles; `EnsureRolesCanChangeAsync`; `EnsureFreeAsync`; `SetDisplayNameAsync`; `SetRolesAsync`; `ChangeContactAsync` | log | **Mejora explícita:** sin correo ni número, nombre y roles pasan a ser atómicos (test nuevo en rojo antes). La carrera de `LastAdmin` entre dos administradores no se arregla | 8 |
| `UserService.SendInvitationAsync` | OnSuccess | lock `user-invitation:`; `FindByIdAsync`; NotFound; `UserInactive`; `Check`; espera mínima; `SendAsync` (lock reentrante) | log | Ninguno | 8 |
| `UserService.SetUserActiveAsync` | OnSuccess | desactivar: `login-link:`, `FindByIdAsync`, `EnsureCanBeRemovedAsync`, `SetActiveAsync`, `AccountAccessRevoker`; activar: `FindByIdAsync`, `SetActiveAsync` | log | Activar pasa de autocommit a transacción explícita (una escritura, no se nota) | 9, 10 |
| `UserService.DeleteUserAsync` | OnSuccess | `login-link:`; `FindByIdAsync`; `EnsureCanBeRemovedAsync`; `AccountAccessRevoker` (antes del borrado); `DeleteAsync` | log | Ninguno | 9, 10 |
| `UserService.UnlinkUserPhoneAsync` | OnSuccess | `PhoneNumberChange.LockAsync(userId, null)`; `FindByIdAsync`; sin número: `UnlinkUserAsync`, `VoidPendingLinksAsync`; con número: guard, `RemovePhoneAsync`, `UnlinkUserAsync`, `AccountAccessRevoker` | log | Ninguno: los dos `SaveChangesAsync` pasan a un solo commit | 9, 10 |
| `IdentityService.RevokeSessionsAsync` | la de quien llama | `RequireTransaction`; `RequireUserAsync`; `UpdateSecurityStampAsync`; revocaciones de OpenIddict | — | Ninguno en producción. Los enlaces los invalida `AccountAccessRevoker` | 10 |
| `ProfileService.UpdateAsync` | OnSuccess | `UserId`; `FindByIdAsync`; `UpdateProfileAsync` (autoguardado) | log | Ninguno visible (sigue sin lock: dos PUT simultáneos compiten por el `ConcurrencyStamp`) | 11 |
| `ProfileService.RequestEmailCodeAsync` | OnSuccess | `FindByIdAsync`; `Email.Create`; `IssueVerificationCodeAsync`; encolado; `MarkSent` | log | Ninguno en datos; un límite suelta el lock antes de responder | 11 |
| `ProfileService.ConfirmEmailAsync` | OnAnyResult | `FindByIdAsync`; `Email.Create`; `DestinationCodeVerifier` (lock `login-code:`); dueño del correo; `SetEmailAsync` con catch | log | Ninguno | 11 |
| `ProfileService.RequestPhoneLinkCodeAsync` | OnSuccess | `FindByIdAsync`; `Parse`; país; `IssueVerificationCodeAsync`; `TryEnqueue` y `MarkSent`. El guard `IsEnabled` (`ProfileWhatsAppOperations.EnsureEnabled`) va afuera, después del validador, como en `AccountService` | log | Ninguno | 11 |
| `ProfileService.ConfirmPhoneLinkAsync` | OnAnyResult | `UserId`; `PhoneNumber.Create`; verifier (`login-code:`); `PhoneNumberChange.LockAsync` (contactos y después `login-link:`); `FindByIdAsync`; dueño; `SetPhoneAsync` con catch; `VoidPendingLinksAsync`; `LinkNumberAsync` | log | Ninguno | 11 |
| `ProfileService.UnlinkOwnPhoneAsync` | OnSuccess | `UserId`; `PhoneNumberChange.LockAsync`; `FindByIdAsync`; `HasOtherLoginMethodAsync`; `RemovePhoneAsync`; `UnlinkUserAsync`; `VoidPendingLinksAsync` | log | Ninguno en datos. A propósito no revoca sesiones | 11 |
| `AccountService.RequestLoginCodeAsync` | OnSuccess | `Email.Create`; `IssueSignInCodeAsync`; `FindByEmailAsync`; `AllowsNewAccountAsync`; encolado; `MarkSent` (en InviteOnly, sin cuenta, la fila se guarda igual) | log | Ninguno | 12 |
| `AccountService.RequestWhatsAppLoginCodeAsync` | OnSuccess | `Parse`; país; `IssueSignInCodeAsync`; `FindByPhoneAsync`; `AllowsNewAccountAsync(null)`; `TryEnqueue` y `MarkSent`. El guard `IsEnabled` sigue afuera, después del validador | log | Ninguno | 12 |
| `AccountService.VerifyLoginCodeAsync` | OnAnyResult | todo `LoginCodeVerifier.VerifyAsync` (lock, cuenta después del lock, lockout, verify, intentos, alta o confirmación, `SignInAsync`, auditoría) | log | Ninguno en datos; el 23505 crudo de `CreateAsync` sale como `UniqueConstraintViolationException` | 12 |
| `LoginLinkService.RedeemAsync` | OnAnyResult | `Hash`; `FindUserIdAsync` (sin seguimiento, antes del lock); `login-link:`; `GetByTokenHashAsync`; `Redeem`; `FindByIdAsync`; lockout; `IsActive`; reset; `SignInAsync`; auditoría | log | Ninguno | 13 |
| `LoginLinkTestService.IssueAsync` (tests) | OnSuccess | `LoginLinkIssuer.IssueAsync` | respuesta | Ninguno | 13 |
| `ExternalLoginService.SignInAsync` | OnAnyResult | `GetExternalLoginAsync`; `SignOutExternalAsync`; `FindByExternalLoginAsync`; `LockExternalSignInAsync`; relectura; alta o confirmación; `AddExternalLoginAsync`; `IsActive`; lockout; auditoría | cookie de la aplicación (solo si éxito), log | Ninguno | 14 |
| `WhatsAppWebhookService.ReceiveAsync` | no abre límite | nada: firma, lectura, `PersistAsync`; catch de `UniqueConstraintViolationException` fuera de toda transacción y reintento en scope nuevo | `Notify`, log | Ninguno (el archivo no se toca) | 15 |
| `WhatsAppWebhookPersistence.PersistAsync` | OnSuccess | lote vacío vuelve antes; locks de contactos; locks de mensajes; existentes; contactos; entrantes; estados | `LogReceived` | Ninguno. Es dueña de su límite porque es la unidad que se reintenta | 15 |
| `WhatsAppWebhookRetry.RetryAsync` | la de `PersistAsync` | nada propio: scope nuevo y `PersistAsync` | — | Ninguno (no se toca) | 15 |
| `WhatsAppInboundService.ProcessContactAsync` | OnSuccess | todo `ProcessCoreAsync`, con el límite abierto antes de `GetForProcessingAsync` | log | Ante una excepción el rollback ocurre antes de que el procesador la registre | 16 |
| `WhatsAppInboundProcessor` | sin límite propio | un scope por contacto | catch por contacto y por vuelta | Ninguno (no se toca) | 18 |
| `WhatsAppDeliveryService.RecordSentAsync` | OnSuccess | `FindContactAsync`; `Add`; si es invitación, `user-invitation:`, `GetByIdAsync`, `AttachWhatsAppMessage` | log | Ninguno | 17 |
| `WhatsAppDeliveryService.RecordUnsentAsync` | OnSuccess | si es invitación, `user-invitation:`, `GetByIdAsync`, `MarkSendFailed` | log | Ninguno | 17 |
| `WhatsAppSenderBackgroundService` | sin límite propio | HTTP a Meta fuera de toda transacción; cada registro en su scope | logs de siempre | Ninguno | 18 |
| `WhatsAppMessageRetentionService` | fuera, documentado | `ExecuteUpdate` en autocommit | logs | Ninguno | 18 |
| `ConnectService.RevokeAuthorizationAsync` | fuera, documentado | un UPDATE de OpenIddict en autocommit | — | Ninguno | 18 |
| `LoginLinkIssuer.IssueAsync`, `LoginCodeIssuer`, `LoginCodeVerifier`, `DestinationCodeVerifier`, `PhoneNumberChange`, `WhatsAppContactLinker`, `UserInvitationSender` | la de quien llama | sus locks y escrituras, sin guardar | — | Desde la Tarea 20, sin transacción sus locks lanzan | — |
| `IdentityService.RegisterFailedAttemptAsync`, `ResetFailedAttemptsAsync` | la de quien llama | autoguardados de Identity | — | Ninguno | — |
| `IdentityService` CRUD de roles y delegaciones de escritura sin llamador en src | fuera | autocommit, solo para preparar datos en tests | — | Ninguno; los recorta la Etapa 2 | — |
| `RoleSeeder`, `SystemSettingsSeeder`, `OpenIddictSeeder`, `SeedExtensions` | fuera, documentado | arranque idempotente | — | Ninguno; atomicidad y lock entre réplicas van con D6 (Etapa 7) | 18 |
| `WidgetTestService.CreateAsync` (tests) | OnSuccess | `Add` del widget | Id | Ninguno | 19 |
| Servidor OpenIddict, Data Protection, `DatabaseMigrationExtensions` | fuera, del framework | sus propios guardados y transacciones | — | Ninguno | — |

### 5. Decisiones que el usuario tiene que confirmar antes de ejecutar

Las trece primeras vienen del diseño aprobado; la 14 salió de la revisión del plan. Cada una lleva su recomendación. La Tarea 0 (Paso 0) frena hasta tener la confirmación de las catorce, y si alguna se rechaza, el plan cambia en las tareas indicadas.

1. **Firma:** un solo método genérico con `CommitPolicy` obligatoria, en lugar de las dos sobrecargas del plan maestro. *Recomendación: aceptar.* (Tareas 2 y 3.)
2. **Nombres de la política:** `OnSuccess` y `OnAnyResult` (`Always` sugeriría que también confirma ante una excepción). *Aceptar.*
3. **Anidar o encontrar una transacción ajena lanza** desde la Tarea 3, en lugar de reutilizarla como decía el plan maestro. *Lanzar.* (La decisión 0001 se corrige en la Tarea 23.)
4. **Borrar `IUnitOfWork.SaveChangesAsync`** al final (Tarea 21). Mientras tanto, adentro de un límite lanza. *Borrar.*
5. **Mejora de atomicidad:** `UpdateUser` sin correo ni número pasa a ser atómico; activar una cuenta, `ProfileService.UpdateAsync`, `SystemSettings`, `RecordSent` y Google con un vínculo existente pasan de autocommit a transacción explícita, sin diferencia visible. *Aceptar.*
6. **Webhook:** `WhatsAppWebhookPersistence.PersistAsync` es dueña de su límite; `WhatsAppWebhookService` y `WhatsAppWebhookRetry` no cambian. Consecuencia: el ítem 9 de la Etapa 3 se corrige y `IWhatsAppWebhookPersistence` queda en `Interfaces/Services`. *Aceptar.*
7. **Regla de arquitectura por contrato:** recibir `IUnitOfWork` o llamar a `ExecuteInTransactionAsync` solo le está permitido a una clase de `Application.Services` que implementa una interfaz de `Interfaces/Services` (reemplaza "el nombre termina en Service"). IL con Mono.Cecil 0.11.3, trinquete desde la Tarea 4, y dos reglas nuevas: claves de lock solo en `AdvisoryLockKeys`, `ExecuteUpdate`/`ExecuteDelete` solo en la retención. *Aceptar.*
8. **Validación fuera del límite sin agrandar los servicios:** los `*Operations` exponen `Validate*Async`. *Aceptar; la Etapa 3 lo simplifica.*
9. **La cookie** del ingreso por código y del canje de enlace sigue escribiéndose antes del commit; Google, después. *No tocar en la Etapa 1; alinear en la Etapa 2 con `ISignInService`.*
10. **Tokens:** guardado final y commit con el token del pedido; rollback con `CancellationToken.None`. *Dejar como hoy.*
11. **Tarea 5 del plan maestro:** la invalidación de enlaces pasa a `AccountAccessRevoker`; `RevokeSessionsAsync` y `RoleRepository` exigen la transacción; las escrituras de `UserRepository` no (los arneses escriben en autocommit). *Aceptar.*
12. **Quedan fuera, documentadas:** retención, `ConnectService.RevokeAuthorizationAsync`, seeders, migraciones, servidor OpenIddict y Data Protection. *Aceptar.*
13. **El 23505 se traduce** también cuando escapa del trabajo, y después de cada rollback se vacía el change tracker. *Aceptar las dos cosas.*
14. **La fábrica de caché de `SystemSettingsReader` lee con su propio scope** (§2.8), en lugar del contexto de quien llama. Es la segunda mejora explícita de la etapa. Sin ella, la garantía de soltar los locks antes de relanzar no vale cuando un pedido se cancela mientras otro espera su fábrica. `PermissionService` no se toca, porque se lee fuera de todo límite. *Aceptar.* (Tarea 7.)

---

## Mapa de archivos

Rutas relativas a `C:\Users\ezequ\source\repos\ArquitecturaBase`.

**Crear**

| Archivo | Para qué | Tarea |
|---|---|---|
| `src/ArquitecturaBase.Application/Interfaces/Persistence/CommitPolicy.cs` | la política de confirmación | 2 |
| `src/ArquitecturaBase.Application/Interfaces/Persistence/CommitPolicyExtensions.cs` | la única regla `Commits` | 2 |
| `tests/ArquitecturaBase.Application.UnitTests/Common/Persistence/CommitPolicyExtensionsTests.cs` | tabla de la regla | 2 |
| `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UnitOfWorkTransactionTests.cs` | la semántica de la unidad de trabajo contra Postgres | 3, 20, 21 |
| `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/TransactionGuard.cs` | los dobles de lock lanzan fuera del límite | 3 |
| `tests/ArquitecturaBase.ArchitectureTests/Support/CallSites.cs` | llamadas y literales del IL con Mono.Cecil | 4 |
| `tests/ArquitecturaBase.ArchitectureTests/CallSitesTests.cs` | el lector del IL saltea los tipos que emite un generador | 4 |
| `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` | reglas del límite y trinquete | 4, 21 |
| `src/ArquitecturaBase.Infrastructure/Persistence/Extensions/AdvisoryLockKeys.cs` | catálogo de claves | 5 |
| `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/AdvisoryLockKeysTests.cs` | texto exacto de cada clave | 5 |
| `src/ArquitecturaBase.Infrastructure/Persistence/Extensions/TransactionExtensions.cs` | `RequireTransaction` | 6 |
| `tests/ArquitecturaBase.Api.IntegrationTests/Support/FailingCommitUnitOfWork.cs` | commit que falla sobre la unidad real | 9, 21 |
| `src/ArquitecturaBase.Application/Services/Users/AccountAccessRevoker.cs` | cortar todo acceso en dos pasos | 10 |
| `tests/ArquitecturaBase.Application.UnitTests/Services/Users/AccountAccessRevokerTests.cs` | sus reglas | 10 |

**Modificar (producción)**

| Archivo | Qué cambia | Tarea |
|---|---|---|
| `Application/Interfaces/Persistence/IUnitOfWork.cs` | método nuevo; en la 21 pierde `SaveChangesAsync` | 3, 21 |
| `Infrastructure/Persistence/UnitOfWork.cs` | `ExecuteInTransactionAsync`; en la 21 pierde lo transitorio | 3, 21 |
| `Infrastructure/Persistence/Extensions/AdvisoryLockExtensions.cs` | exige la transacción | 20 |
| `Infrastructure/Persistence/Repositories/LoginCodeRepository.cs` | usa la extensión y el catálogo | 5 |
| `Infrastructure/Persistence/Repositories/{LoginLink,UserInvitation,WhatsAppMessage}Repository.cs` | catálogo | 5 |
| `Infrastructure/Persistence/Repositories/WhatsAppContactRepository.cs` | catálogo; exige la transacción | 5, 20 |
| `Infrastructure/Persistence/Repositories/UserRepository.cs` | catálogo; comentarios | 5, 14, 20 |
| `Infrastructure/Persistence/Repositories/RoleRepository.cs` | sin transacción propia | 6 |
| `Infrastructure/Identity/IdentityService.cs` | `RevokeSessionsAsync` explícito | 10 |
| `Application/Services/Roles/RoleService.cs` | patrón canónico | 6 |
| `Application/Services/Settings/SystemSettingsService.cs` | límite | 7 |
| `Infrastructure/Persistence/Readers/SystemSettingsReader.cs` | la fábrica del caché lee con su propio scope | 7 |
| `Application/Services/Users/{UserService,UserWriteOperations,UserStatusOperations,UserPhoneOperations}.cs` | límite en el servicio | 8, 9, 10 |
| `Application/Services/Users/{ProfileService,ProfileEmailOperations,ProfileWhatsAppOperations,DestinationCodeVerifier}.cs` | límite en el servicio | 11 |
| `Application/Services/Auth/{AccountService,LoginLinkService,ExternalLoginService}.cs` | límite | 12, 13, 14 |
| `Application/Services/WhatsApp/{WhatsAppWebhookPersistence,WhatsAppInboundService,WhatsAppDeliveryService}.cs` | límite | 15, 16, 17 |
| `Application/DependencyInjection.cs` | registra `AccountAccessRevoker` | 10 |
| `Application/Common/Exceptions/UniqueConstraintViolationException.cs` | cref y texto | 21 |
| `Application/Interfaces/Integrations/IIdentityService.cs`, `Application/Interfaces/Persistence/{IUserRepository,IRoleRepository,ILoginCodeRepository,ILoginLinkRepository,IUserInvitationRepository,IWhatsAppMessageRepository,IWhatsAppContactRepository}.cs`, `Application/Interfaces/Services/{IWhatsAppWebhookPersistence,IWhatsAppInboundService,IConnectService}.cs`, `Application/Services/Users/{DestinationCodeVerifier,PhoneNumberChange}.cs` | documentación XML | 6, 10, 11, 14, 15, 16, 18, 20, 21 |
| `Infrastructure/Emails/EmailQueue.cs`, `Infrastructure/WhatsApp/{WhatsAppInboundProcessor,WhatsAppSenderBackgroundService}.cs`, `Infrastructure/Persistence/Seed/SeedExtensions.cs`, `Infrastructure/Persistence/Repositories/WhatsAppMessageRetentionRepository.cs` | documentación | 12, 18 |

**Modificar (tests y docs)**

| Archivo | Qué cambia | Tarea |
|---|---|---|
| `tests/ArquitecturaBase.Api.IntegrationTests/Support/StaleIdentityReads.cs` | gemelo sobre `IUserReader`; `StaleReadsProbe` cuenta lo que esconde | 1 |
| `tests/.../Users/{CreateUserWithPhoneTests,MeEmailEndpointsTests,MeWhatsAppEndpointsTests}.cs` | los 409 de después del chequeo afirman la sonda | 1 |
| `tests/.../Auth/{LoginLinkTests,ExternalLoginTests,LoginSecurityTests}.cs` | tests que fijan comportamiento; revocación; Google | 1, 10, 14 |
| `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/FakeUnitOfWork.cs` | doble nuevo | 3, 21 |
| `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/{Auth/AuthFakes,Users/InMemoryUserInvitationRepository,WhatsApp/WhatsAppFakes}.cs` | `InTransaction` en los dobles de lock | 3 |
| `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs` | `InTransaction` en `LockExternalSignInAsync` | 14 |
| `tests/.../Settings/SystemSettingsReaderTests.cs` | la fábrica del caché no ve lo que quien llama no confirmó | 7 |
| `docs/history/plans/2026-09-26-etapa-1-una-sola-forma-de-guardar.md` (este plan) | se versiona antes de empezar | 0 |
| los tests unitarios de cada servicio migrado | aserciones de `SaveChangesCalls` a `Commits`/`Rollbacks`/`Transactions`/`LastPolicy` | 3, 6-17 |
| `tests/.../Persistence/{UserRepositoryTransactionTests,RoleRepositoryTransactionTests}.cs` | mejora, commit fallido real, repositorio sin transacción | 3, 6, 8, 9 |
| `tests/.../WhatsApp/{WhatsAppLockOrderTests,WhatsAppBotTests}.cs` | transacción explícita antes del lock | 20 |
| `tests/.../Auth/{InitialAdminSignInTests,RegistrationModeTests,WhatsAppLoginCodeTests}.cs`, `tests/.../TestFeatures/{Widgets/WidgetTestService,LoginLinks/LoginLinkTestService}.cs` | sin `IUnitOfWork.SaveChangesAsync` | 13, 19 |
| `Directory.Packages.props`, `tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj` | Mono.Cecil explícito | 4 |
| `CLAUDE.md` | Persistencia, WhatsApp, Casos de uso, Tests | 3, 4, 5, 20, 21 |
| `docs/specs/2026-09-24-backend-mvc-architecture.md`, `AGENTS.md` | la única forma de guardar | 21 |
| `docs/plans/2026-09-26-plantilla-estandar-por-etapas.md`, `docs/decisions/0001-transaccion-explicita-por-caso-de-uso.md` | cierre | 23 |

---

## Comandos

- Compilar: `dotnet build ArquitecturaBase.slnx` (esperado: `0 Warning(s)`, `0 Error(s)`).
- Todos los tests: `dotnet test` (Docker levantado).
- Una clase: `dotnet test --project tests/<Proyecto>/<Proyecto>.csproj -- --filter-class "<Namespace.Clase>"`. Se puede repetir `--filter-class`.
- Un método: `dotnet test --project tests/<Proyecto>/<Proyecto>.csproj -- --filter-method "<Namespace.Clase.Metodo>"`. Se puede repetir `--filter-method`.
- **No mezclar `--filter-class` y `--filter-method` en el mismo comando.** En xUnit v3 con Microsoft Testing Platform, los filtros del mismo tipo se combinan con OR y los de tipos distintos, con AND. Una clase más un método de otra clase no seleccionan nada, y el comando termina con "No se ejecutaron pruebas" y código de salida 8, que parece un fallo del código. Para correr una clase y un método sueltos, usar dos comandos, o un solo comando con `--filter-method` para todos.
- Los tres proyectos de tests que usa el plan: `tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`, `tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj` y `tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj`. En las listas de archivos, `tests/.../` abrevia la carpeta del proyecto que corresponde.
- IDE0005 (using innecesario) e IDE0161 son errores de build: cuando un paso saca el último uso de un namespace, se borra su `using`; cuando agrega uno nuevo, se agrega.
- **Reglas del paso rojo:** en cada tarea de migración, el primer "verlo fallar" puede ser un error de compilación (el test ya usa la firma nueva) o una aserción; los dos cuentan. Si el test pasa antes de cambiar el código, frenar: el test no prueba lo que dice.
- **Documentación XML y comentarios:** cuando un paso trae el `<summary>` nuevo en un bloque `csharp`, se reemplaza el `<summary>` entero del miembro por ese bloque, tal cual (comillas normales, sin escapar). Cuando un paso cita en prosa una oración que hay que buscar, en el código puede estar partida en varias líneas `///` o `//`: se busca por un fragmento, se reemplaza la oración completa y se vuelve a cortar el párrafo en líneas de hasta 120 caracteres. Un `<see cref=...>` mal formado es CS1570, y con `TreatWarningsAsErrors` rompe el build.
- **Antes de cada commit:** `git status --short` muestra solo archivos de la tarea. `main` es compartido y puede haber otra sesión trabajando. Si aparece algo ajeno, no se agrega: se frena y se avisa. Los `git add` de carpetas enteras (`tests`, `src`) valen solo con esa condición.
- Commit (Git Bash), siempre con la línea de autoría:

```bash
git commit -m "$(cat <<'EOF'
<tipo>: <mensaje en español>

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Tareas

### Tarea 0: preparación (un solo commit: este plan)

**Archivos:**
- Versionar: `docs/history/plans/2026-09-26-etapa-1-una-sola-forma-de-guardar.md` (este plan)

- [ ] **Paso 0: las decisiones.** Presentarle al usuario las catorce decisiones de la sección 5 del Diseño, con su recomendación, y esperar su confirmación explícita en el chat. Si rechaza o cambia alguna, frenar: se ajustan las tareas que indica esa decisión antes de ejecutar nada.
- [ ] **Paso 1: lo que no es de esta etapa.** Correr `git status --short`. Esperado: una sola línea, `?? docs/history/plans/2026-09-26-etapa-1-una-sola-forma-de-guardar.md`. Así estaba al revisar el plan (`d63eb43`): los restos de la Etapa 0 y el trabajo de la Api de ese día ya estaban versionados, y `docs/decisions/` también, que leen el Paso 4 y la Tarea 23. Si aparece algo más, frenar y pedirle al usuario que lo versione. Puede ser trabajo de otra sesión sobre `main`. Este plan no lo commitea ni lo descarta. **Nunca descartar, mover ni borrar este plan**, aunque figure como sin versionar.
- [ ] **Paso 2: versionar el plan.** Cuando `git status --short` muestre solo la línea del plan:

```bash
git add docs/history/plans/2026-09-26-etapa-1-una-sola-forma-de-guardar.md
git commit -m "$(cat <<'EOF'
docs: plan de la Etapa 1

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

Después, `git status --short`. Esperado: sin salida. A partir de acá, el ítem de `CLAUDE.md` de la Tarea 3 y la línea de estado de la Tarea 23 enlazan un archivo que está en el repo. Si una tarea corrige este plan mientras se ejecuta, la corrección va en el commit de esa tarea.
- [ ] **Paso 3: línea de base.** Levantar Docker y correr `dotnet build ArquitecturaBase.slnx` y `dotnet test`. Esperado: `0 Warning(s)`, `0 Error(s)` y todos los tests en verde. Si algo falla antes de empezar, frenar y reportarlo: no es de esta etapa.
- [ ] **Paso 4: leer el contexto.** Leer, en este orden: este plan completo, la sección "Etapa 1" del plan maestro, `docs/decisions/0001-transaccion-explicita-por-caso-de-uso.md`, `CLAUDE.md` (Persistencia, WhatsApp, Tests) y `src/ArquitecturaBase.Infrastructure/Persistence/UnitOfWork.cs`.
- [ ] **Paso 5: `main` pudo avanzar.** Correr `git log --oneline cbff737..HEAD -- src tests CLAUDE.md AGENTS.md docs/specs`. Todo commit posterior a `cbff737` que toque un archivo del "Mapa de archivos", o uno que un paso reemplaza o edita, obliga a releer ese archivo antes de su tarea. Los reemplazos del plan se ajustan a lo que haya: el criterio es el diseño de arriba, no el texto literal. El plan se revisó contra `d63eb43`. De los commits posteriores a `cbff737` que tocan algo que el plan cita:
  - `e92d354` sacó el `ToString` de los modelos de pedido de Application y corrió líneas en `LoginLinkTests`, `WhatsAppLoginCodeTests`, `MeEmailEndpointsTests`, `MeWhatsAppEndpointsTests`, `ProfileEmailServiceTests`, `RequestWhatsAppLoginCodeServiceTests` y `ExternalLoginServiceTests`. Ningún paso depende de eso: las propiedades y los tests que cita el plan conservan sus nombres;
  - `d63eb43` corrió líneas en el spec y en `AGENTS.md` (ya ajustadas en la Tarea 21), amplió el ítem `**ArchitectureTests:**` de `CLAUDE.md` (por eso la Tarea 4 agrega en lugar de reemplazar) y sumó al plan maestro un ítem de avance en la Etapa 3, que ajusta la Tarea 23;
  - `487c3a0` y `eccbaeb` tocan controllers, OpenAPI, `README.md` y `docs/decisions/`, `docs/history/`: nada de eso lo cambia el plan, y `docs/decisions/` queda versionado.

  Lo que haya llegado después de `d63eb43` no está revisado.

---

### Tarea 1 (C0): fijar lo que hoy no tiene test

Solo tests, en verde sobre el código de hoy. Fijan tres comportamientos que la migración tiene que conservar y hacen que los 409 de "otra cuenta se quedó con el dato entre el chequeo y el guardado" pasen de verdad por el choque con el índice único y el savepoint.

**Archivos:**
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Support/StaleIdentityReads.cs` (reemplazo completo)
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginLinkTests.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/ExternalLoginTests.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginSecurityTests.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Users/{CreateUserWithPhoneTests,MeEmailEndpointsTests,MeWhatsAppEndpointsTests}.cs` (la sonda del Paso 2)

- [ ] **Paso 1: `StaleIdentityReads` tapa también `IUserReader` y cuenta lo que esconde.** Hoy solo reemplaza `IIdentityService`, pero el alta, la edición y el perfil leen por `IUserReader`: el chequeo previo ve al dueño y el 409 sale antes del guardado. Cada búsqueda escondida suma en `StaleReadsProbe`, que el test crea y afirma en el Paso 2. Reemplazar el archivo completo por:

```csharp
using System.Reflection;
using System.Runtime.ExceptionServices;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Identity;
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
/// El IdentityService y el UserReader reales, con una lectura vieja: las búsquedas de un número o de un correo dicen
/// que no es de nadie, ni de una cuenta activa ni de una borrada, aunque ya lo sea. Es lo que ve un pedido cuando otra
/// cuenta se queda con el número justo entre su búsqueda y su guardado: así un test llega al choque con el índice único
/// sin depender de cómo se crucen dos pedidos. Todo lo demás pasa tal cual al servicio real.
/// </summary>
/// <remarks>
/// Es un <see cref="DispatchProxy"/> para no repetir a mano los métodos de las dos interfaces. Es pública y no está
/// sellada porque el proxy se arma heredando de ella. Tapa las dos puertas: Application lee las cuentas por
/// <see cref="IUserReader"/> (el alta, la edición y el perfil) y por <see cref="IIdentityService"/> (el ingreso). Si
/// quedara una abierta, el 409 saldría del chequeo previo sin pasar por el choque ni por el savepoint, y el test
/// seguiría en verde: por eso cada búsqueda escondida suma en <see cref="StaleReadsProbe"/>, y el test afirma que hubo
/// alguna. Si una búsqueda se muda a otra interfaz o cambia de nombre, la sonda queda en cero y el test falla.
/// </remarks>
public class StaleIdentityReads : DispatchProxy
{
    private static readonly string[] HiddenLookups =
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
    /// Cambia el UserReader y el IdentityService de la Api por los reales con la lectura vieja de
    /// <paramref name="hidden"/>, un <see cref="PhoneNumber"/> o un <see cref="Email"/>. Cada búsqueda escondida se
    /// cuenta en <paramref name="probe"/>, que el test crea fuera de <c>ConfigureTestServices</c> para leerla después.
    /// </summary>
    public static void Replace(IServiceCollection services, object hidden, StaleReadsProbe probe)
    {
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(probe);

        services.RemoveAll<IUserReader>();
        services.AddScoped(serviceProvider =>
            Wrap<IUserReader>(ActivatorUtilities.CreateInstance<UserReader>(serviceProvider), hidden, probe));

        // El IdentityService real recibe el lector viejo de arriba: las dos puertas dicen lo mismo.
        services.RemoveAll<IIdentityService>();
        services.AddScoped(serviceProvider =>
            Wrap<IIdentityService>(ActivatorUtilities.CreateInstance<IdentityService>(serviceProvider), hidden, probe));
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (args is [{ } first, ..] && first.Equals(_hidden) && HiddenLookups.Contains(targetMethod.Name, StringComparer.Ordinal))
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

- [ ] **Paso 2: los cuatro tests que usan `StaleIdentityReads` afirman la sonda.** *Corrección de la revisión de la Tarea 1:* un 409 del chequeo previo y uno del catch del 23505 se ven iguales (mismo status, mismo código, código gastado, sin cuenta ni invitación). Así estuvo roto el proxy anterior, que solo tapaba `IIdentityService`, sin que ningún test lo notara, y así volvería a romperse si una búsqueda se muda a otra interfaz o cambia de nombre. En `CreateUserWithPhoneTests.{A_phone_that_another_account_takes_between_the_check_and_the_save_answers_409,An_email_that_another_account_takes_between_the_check_and_the_save_answers_409}`, `MeEmailEndpointsTests.A_clash_with_the_unique_index_after_the_check_answers_409_and_keeps_the_code_spent` y `MeWhatsAppEndpointsTests.A_clash_with_the_unique_index_after_the_check_answers_409_and_keeps_the_code_spent`: `var probe = new StaleReadsProbe();` antes de `WithWebHostBuilder` (fuera de la lambda de `ConfigureTestServices`), `StaleIdentityReads.Replace(services, <el número o el correo>, probe)` y, al lado de la aserción del 409, `Assert.True(probe.HiddenLookups > 0);`. Con el dueño activo, un chequeo previo a ciegas y un 409 solo pueden salir del choque. Tienen que seguir en verde, ahora pasando por el catch del 23505 y el savepoint.

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-method "ArquitecturaBase.Api.IntegrationTests.Users.CreateUserWithPhoneTests.A_phone_that_another_account_takes_between_the_check_and_the_save_answers_409" --filter-method "ArquitecturaBase.Api.IntegrationTests.Users.CreateUserWithPhoneTests.An_email_that_another_account_takes_between_the_check_and_the_save_answers_409" --filter-method "ArquitecturaBase.Api.IntegrationTests.Users.MeEmailEndpointsTests.A_clash_with_the_unique_index_after_the_check_answers_409_and_keeps_the_code_spent" --filter-method "ArquitecturaBase.Api.IntegrationTests.Users.MeWhatsAppEndpointsTests.A_clash_with_the_unique_index_after_the_check_answers_409_and_keeps_the_code_spent"`
Esperado: 4 PASS. Si alguno falla, **frenar**: es un hallazgo sobre el código de hoy (el catch o el savepoint no se comportan como se creía). Investigarlo con `superpowers:systematic-debugging` y reportarlo antes de seguir.

Verificado en rojo al hacer la corrección: con el proxy viejo (sin reemplazar `IUserReader`), los cuatro fallan en `Assert.True(probe.HiddenLookups > 0)`; con los tres `catch (UniqueConstraintViolationException)` relanzando (`UserWriteOperations.CreateOrRestoreAsync`, `ProfileEmailOperations` y `ProfileWhatsAppOperations`), los cuatro responden 500 en lugar de 409.

- [ ] **Paso 3: el canje del enlace de una cuenta borrada.** En `LoginLinkTests.cs`, agregar este test después de `Link_of_a_deleted_account_is_just_an_invalid_link`:

```csharp
    /// <summary>
    /// Un enlace todavía activo de una cuenta que se borró después de emitirlo: el canje lo gasta (queda consumido) y
    /// no deja auditoría, porque no hay a quién atribuirla. Lo fija antes de que el guardado pase a ExecuteInTransactionAsync.
    /// </summary>
    [Fact]
    public async Task Redeeming_an_active_link_of_a_deleted_account_uses_it_up_without_an_audit()
    {
        var account = await CreateAccountAsync();
        using var client = factory.CreateClient();
        var token = TokenOf(await IssueUrlAsync(client, account.Id));
        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().DeleteAsync(account.Id, Ct);

            return true;
        });

        using var redeem = await RedeemAsync(client, token);

        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        Assert.Equal(LoginLinkErrors.InvalidCode, (await redeem.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.False(HasSessionCookie(redeem));
        Assert.NotNull(await ConsumedAtAsync(token));
        Assert.False(await factory.ExecuteDbContextAsync(db => db.LoginAudits.AnyAsync(audit => audit.UserId == account.Id, Ct)));
    }
```

- [ ] **Paso 4: Google vincula una cuenta inactiva o bloqueada.** En `ExternalLoginTests.cs`, agregar los usings `using ArquitecturaBase.Application.Interfaces.Integrations;` y `using ArquitecturaBase.Domain.ValueObjects;` (en orden con los demás) y este test después de `Failed_google_commit_rolls_back_autosaved_account_role_link_and_audit`:

```csharp
    /// <summary>
    /// Una cuenta que existe por el correo pero está inactiva o bloqueada no entra con Google, y aun así queda vinculada a
    /// Google, con el correo confirmado y la auditoría del rechazo: el error se guarda igual (CommitPolicy.OnAnyResult).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Google_links_an_existing_inactive_or_locked_account_and_confirms_its_email_even_if_it_cannot_enter(bool locked)
    {
        var email = TestEmails.Unique(locked ? "google-locked" : "google-inactive");
        var providerKey = "google-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var account = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>()
            .CreateUnverifiedAsync(Email.Create(email).Value, phone: null, "Ana", "es", Ct));
        await factory.ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(user => user.Id == account.Id, Ct);
            if (locked)
            {
                user.LockoutEnd = factory.Clock.GetUtcNow().AddHours(1);
            }
            else
            {
                user.IsActive = false;
            }

            return await db.SaveChangesAsync(Ct);
        });
        using var client = factory.CreateClient();
        using var external = await client.PostJsonAsync(
            "/test/external-login", new { providerKey, email, name = "Ana Pérez", emailVerified = true });
        Assert.True(external.IsSuccessStatusCode);

        using var callback = await client.SendAsync(
            HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl));

        var expected = locked ? AccountErrors.LockedOutCode : AccountErrors.DisabledCode;
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/login?error=" + expected, callback.Headers.Location!.OriginalString);
        var persisted = await factory.ExecuteDbContextAsync(async db =>
            (EmailConfirmed: await db.Users.Where(user => user.Id == account.Id).Select(user => user.EmailConfirmed).SingleAsync(Ct),
             Linked: await db.UserLogins.AnyAsync(login => login.UserId == account.Id
                 && login.LoginProvider == "Google" && login.ProviderKey == providerKey, Ct),
             Audit: await db.LoginAudits.Where(audit => audit.UserId == account.Id).Select(audit => audit.FailureReason).SingleAsync(Ct)));
        Assert.True(persisted.EmailConfirmed);
        Assert.True(persisted.Linked);
        Assert.Equal(expected, persisted.Audit);
    }
```

- [ ] **Paso 5: el verify confirma el destino de una cuenta inactiva.** En `LoginSecurityTests.cs`, agregar los usings `using ArquitecturaBase.Application.Interfaces.Integrations;`, `using ArquitecturaBase.Domain.ValueObjects;` y `using Microsoft.Extensions.DependencyInjection;`, y este test después de `Disabled_account_is_reported_after_verifying_the_code`:

```csharp
    /// <summary>
    /// Con el código correcto, una cuenta inactiva queda con el correo confirmado y el código gastado, y responde
    /// Auth.Account.Disabled con su auditoría: el verify guarda también cuando falla (CommitPolicy.OnAnyResult).
    /// </summary>
    [Fact]
    public async Task An_inactive_account_confirms_its_email_with_a_valid_code_and_still_answers_disabled()
    {
        var email = TestEmails.Unique("inactive-confirms");
        await factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>()
            .CreateUnverifiedAsync(Email.Create(email).Value, phone: null, "Ana", "es", Ct));
        await DisableAsync(email);
        using var client = factory.CreateClient();
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync(
            "/account/login-code/verify", new { email, code, returnUrl = ReturnUrl }, language: "es");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(AccountErrors.DisabledCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        var persisted = await factory.ExecuteDbContextAsync(async db =>
            (EmailConfirmed: await db.Users.Where(user => user.Email == email).Select(user => user.EmailConfirmed).SingleAsync(Ct),
             CodeConsumed: await db.LoginCodes.Where(stored => stored.Destination == email).AnyAsync(stored => stored.ConsumedAtUtc != null, Ct),
             Audit: await db.LoginAudits.Where(audit => audit.Identifier == email).Select(audit => audit.FailureReason).SingleAsync(Ct)));
        Assert.True(persisted.EmailConfirmed);
        Assert.True(persisted.CodeConsumed);
        Assert.Equal(AccountErrors.DisabledCode, persisted.Audit);
    }
```

- [ ] **Paso 6: correr los tests nuevos.**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-method "ArquitecturaBase.Api.IntegrationTests.Auth.LoginLinkTests.Redeeming_an_active_link_of_a_deleted_account_uses_it_up_without_an_audit" --filter-method "ArquitecturaBase.Api.IntegrationTests.Auth.ExternalLoginTests.Google_links_an_existing_inactive_or_locked_account_and_confirms_its_email_even_if_it_cannot_enter" --filter-method "ArquitecturaBase.Api.IntegrationTests.Auth.LoginSecurityTests.An_inactive_account_confirms_its_email_with_a_valid_code_and_still_answers_disabled"`
Esperado: 4 PASS (la teoría cuenta dos). Son tests de caracterización: si alguno falla, el comportamiento de hoy es otro que el que describe el mapa; frenar y reportarlo, no "arreglar" el test.

- [ ] **Paso 7: el orden de locks del alta sigue en pie.**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-method "ArquitecturaBase.Application.UnitTests.Services.Users.UserServiceWriteTests.Create_locks_email_before_phone_and_rejects_unknown_roles_without_mutation"`
Esperado: PASS (correo antes que teléfono, en dos llamadas).

- [ ] **Paso 8: build y suite completa.** `dotnet build ArquitecturaBase.slnx` sin advertencias y `dotnet test` en verde.

- [ ] **Paso 9: commit.**

```bash
git add tests/ArquitecturaBase.Api.IntegrationTests/Support/StaleIdentityReads.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginLinkTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/ExternalLoginTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginSecurityTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/CreateUserWithPhoneTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/MeEmailEndpointsTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/MeWhatsAppEndpointsTests.cs
git commit -m "$(cat <<'EOF'
test: fijar comportamientos transaccionales antes de la Etapa 1

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 2 (C1a): `CommitPolicy` y su regla

Tipos nuevos y puros: no cambian nada todavía.

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Interfaces/Persistence/CommitPolicy.cs`
- Crear: `src/ArquitecturaBase.Application/Interfaces/Persistence/CommitPolicyExtensions.cs`
- Crear: `tests/ArquitecturaBase.Application.UnitTests/Common/Persistence/CommitPolicyExtensionsTests.cs`

- [ ] **Paso 1: escribir el test que falla.** Crear `tests/ArquitecturaBase.Application.UnitTests/Common/Persistence/CommitPolicyExtensionsTests.cs`:

```csharp
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Common.Persistence;

public sealed class CommitPolicyExtensionsTests
{
    private static readonly Error Failure = Error.Failure("Tests.Commit.Failed", "A business rule failed.");

    [Theory]
    [InlineData(CommitPolicy.OnSuccess, true, true)]
    [InlineData(CommitPolicy.OnSuccess, false, false)]
    [InlineData(CommitPolicy.OnAnyResult, true, true)]
    [InlineData(CommitPolicy.OnAnyResult, false, true)]
    public void Only_on_any_result_commits_a_failed_result(CommitPolicy policy, bool succeeded, bool commits)
    {
        var result = succeeded ? Result.Success() : Result.Failure(Failure);

        Assert.Equal(commits, policy.Commits(result));
    }

    [Fact]
    public void A_typed_result_follows_the_same_rule()
    {
        Assert.True(CommitPolicy.OnSuccess.Commits(Result.Success(42)));
        Assert.False(CommitPolicy.OnSuccess.Commits(Result.Failure<int>(Failure)));
        Assert.True(CommitPolicy.OnAnyResult.Commits(Result.Failure<int>(Failure)));
    }

    [Fact]
    public void A_missing_result_is_a_bug()
    {
        Assert.Throws<ArgumentNullException>(() => CommitPolicy.OnSuccess.Commits(null!));
    }

    [Fact]
    public void The_policies_are_exactly_on_success_and_on_any_result()
    {
        Assert.Equal(["OnSuccess", "OnAnyResult"], Enum.GetNames<CommitPolicy>());
    }
}
```

- [ ] **Paso 2: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Common.Persistence.CommitPolicyExtensionsTests"`
Esperado: FAIL de compilación (`CommitPolicy` no existe).

- [ ] **Paso 3: crear `src/ArquitecturaBase.Application/Interfaces/Persistence/CommitPolicy.cs`.**

```csharp
namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Qué hace <see cref="IUnitOfWork"/> con un <c>Result</c> fallido. Un éxito se confirma siempre y una excepción deshace
/// siempre: la política decide solo qué pasa con un error de negocio, y cada servicio la escribe en su llamada ("cada
/// servicio define expresamente cuándo guarda").
/// </summary>
public enum CommitPolicy
{
    /// <summary>
    /// Solo se confirma el éxito. Un Result fallido deshace todo, incluido lo que UserManager y RoleManager ya autoguardaron
    /// adentro y las revocaciones de OpenIddict, y suelta los locks en el acto. Es la política de casi todos los casos de uso.
    /// </summary>
    OnSuccess = 0,

    /// <summary>
    /// Se confirma con cualquier Result, exitoso o fallido, porque el caso de uso tiene que dejar rastro de un intento que
    /// falló: el intento de un código, un código o un enlace gastado, un bloqueo, una auditoría. Una excepción igual deshace
    /// todo. La usan solo el verify de código, el canje de enlace, Google y las dos confirmaciones del perfil.
    /// </summary>
    OnAnyResult = 1,
}
```

- [ ] **Paso 4: crear `src/ArquitecturaBase.Application/Interfaces/Persistence/CommitPolicyExtensions.cs`.**

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

public static class CommitPolicyExtensions
{
    /// <summary>
    /// Si la transacción se confirma con <paramref name="result"/>. Es la única definición de la regla: la usan UnitOfWork
    /// y los dobles de prueba, así ningún test puede afirmar otra semántica que la de producción.
    /// </summary>
    public static bool Commits(this CommitPolicy policy, Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess || policy == CommitPolicy.OnAnyResult;
    }
}
```

- [ ] **Paso 5: verlo pasar.** Mismo comando del Paso 2. Esperado: 7 PASS.

- [ ] **Paso 6: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 7: commit.**

```bash
git add src/ArquitecturaBase.Application/Interfaces/Persistence/CommitPolicy.cs src/ArquitecturaBase.Application/Interfaces/Persistence/CommitPolicyExtensions.cs tests/ArquitecturaBase.Application.UnitTests/Common/Persistence/CommitPolicyExtensionsTests.cs
git commit -m "$(cat <<'EOF'
feat: CommitPolicy para decidir qué se confirma con un Result fallido

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 3 (C1b): `ExecuteInTransactionAsync` conviviendo con `SaveChangesAsync`

Ningún servicio usa todavía la API nueva: el comportamiento es idéntico. Todos los dobles de `IUnitOfWork` tienen que implementar el método nuevo en este mismo commit o el build no compila.

**Archivos:**
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UnitOfWorkTransactionTests.cs`
- Crear: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/TransactionGuard.cs`
- Modificar: `src/ArquitecturaBase.Application/Interfaces/Persistence/IUnitOfWork.cs` (reemplazo completo)
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/UnitOfWork.cs` (reemplazo completo)
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/FakeUnitOfWork.cs` (reemplazo completo)
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/AuthFakes.cs`, `.../TestDoubles/Users/InMemoryUserInvitationRepository.cs`, `.../TestDoubles/WhatsApp/WhatsAppFakes.cs`
- Modificar (solo el stub del método nuevo): los nueve dobles privados de la tabla del Paso 8
- Modificar: `CLAUDE.md` (sección Persistencia)

- [ ] **Paso 1: escribir los tests de integración que fallan.** Crear `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UnitOfWorkTransactionTests.cs`:

```csharp
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
```

- [ ] **Paso 2: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.UnitOfWorkTransactionTests"`
Esperado: FAIL de compilación (`IUnitOfWork` no tiene `ExecuteInTransactionAsync`).

- [ ] **Paso 3: el contrato, en su forma transitoria.** Reemplazar `src/ArquitecturaBase.Application/Interfaces/Persistence/IUnitOfWork.cs` completo por:

```csharp
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// El límite transaccional de un caso de uso. Lo usa solo el punto de entrada del caso de uso, o sea una clase que
/// implementa un contrato de Interfaces/Services, una vez por cada método que escribe. Adentro van los locks, las
/// lecturas que deciden y todas las escrituras, también las que UserManager y RoleManager guardan por su cuenta: lo
/// hacen sobre el mismo contexto, dentro de esta transacción y con un savepoint cada una. La validación del pedido va
/// antes, y lo que depende del commit (invalidar un caché, la cookie de Google) va después. Helpers, repositorios y
/// lectores nunca confirman. Mientras dura la migración de la Etapa 1 convive con <see cref="SaveChangesAsync"/>.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Abre una transacción (READ COMMITTED), corre <paramref name="work"/> y después:
    /// <list type="bullet">
    /// <item>si <paramref name="policy"/> confirma el <see cref="Result"/> (<see cref="CommitPolicyExtensions.Commits"/>),
    /// baja lo que quedó seguido en un único guardado y confirma;</item>
    /// <item>si no lo confirma, deshace sin bajar nada y devuelve el mismo Result;</item>
    /// <item>si el trabajo, el guardado final o el commit lanzan una excepción (la cancelación incluida), deshace en el acto
    /// y la relanza. Si otro pedido guardó primero una fila con la misma clave única, la relanza como
    /// <see cref="UniqueConstraintViolationException"/>.</item>
    /// </list>
    /// Al deshacer suelta los locks antes de devolver o lanzar, y vacía el change tracker. No se anida ni adopta una
    /// transacción que no abrió: en los dos casos lanza <see cref="InvalidOperationException"/>. Nunca reintenta el
    /// trabajo, porque puede haber encolado un correo o un mensaje de WhatsApp.
    /// </summary>
    /// <param name="work">El caso de uso, ya validado. Casi siempre es un método privado <c>*CoreAsync</c> con el tipo
    /// de retorno escrito. Recibe el mismo token.</param>
    /// <param name="policy">Qué hacer con un Result fallido. No tiene valor por defecto, a propósito.</param>
    /// <param name="cancellationToken">El token del pedido: lo reciben el trabajo, el guardado final y el commit.</param>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work,
        CommitPolicy policy,
        CancellationToken cancellationToken)
        where TResult : Result;

    /// <summary>
    /// En retiro (Etapa 1): usá <see cref="ExecuteInTransactionAsync{TResult}"/>. Fuera de ella conserva el comportamiento
    /// de antes: guarda y confirma la transacción que haya abierto un lock, y si falla deshace y traduce el 23505 a
    /// <see cref="UniqueConstraintViolationException"/>. Adentro lanza InvalidOperationException, porque confirmar ahí
    /// cerraría a mitad de camino el límite del caso de uso.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Paso 4: la implementación, en su forma transitoria.** Reemplazar `src/ArquitecturaBase.Infrastructure/Persistence/UnitOfWork.cs` completo por:

```csharp
using System.Data.Common;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Persistence;

/// <summary>
/// La única pieza que abre, confirma y deshace transacciones, y la única que guarda (lo verifica
/// TransactionBoundaryTests). UserManager y RoleManager siguen guardando en cada operación sobre este mismo contexto
/// scoped (D1). Dentro de la transacción, EF le pone un savepoint a cada guardado: un choque con un índice único deshace
/// solo ese guardado y el caso de uso puede seguir, por ejemplo para dejar gastado el código. No usa estrategia de
/// reintentos, porque el trabajo encola correos y mensajes y no se puede volver a correr. Si alguna vez se activa
/// EnableRetryOnFailure, BeginTransactionAsync lanza, y no hay que "arreglarlo" envolviendo el trabajo en la estrategia.
/// </summary>
internal sealed partial class UnitOfWork(ApplicationDbContext dbContext, ILogger<UnitOfWork> logger) : IUnitOfWork
{
    private IDbContextTransaction? _transaction;

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work,
        CommitPolicy policy,
        CancellationToken cancellationToken)
        where TResult : Result
    {
        ArgumentNullException.ThrowIfNull(work);

        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown commit policy.");
        }

        if (_transaction is not null)
        {
            throw new InvalidOperationException(
                "ExecuteInTransactionAsync does not nest: a use case has a single transaction boundary.");
        }

        if (dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "A transaction that ExecuteInTransactionAsync did not open is still active in this scope.");
        }

        // READ COMMITTED, el nivel de Postgres. Lo que pone en fila son los locks, y lo que se lee después de un lock tiene
        // que ver lo que el otro acaba de confirmar (ConcurrencyStamp): por eso no se sube el aislamiento.
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        _transaction = transaction;

        try
        {
            var result = await work(cancellationToken);

            if (!policy.Commits(result))
            {
                await RollbackAsync(transaction);
                return result;
            }

            // Lo que Identity no guardó por su cuenta (códigos, enlaces, auditorías, contactos, mensajes) se guarda acá.
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            // Se deshace ahora y no al descartar el contexto: los locks se sueltan antes de que la excepción llegue a quien
            // llama, y quien reintenta en otro scope (el webhook de WhatsApp) no se queda esperando a este.
            await RollbackAsync(transaction);

            if (UniqueViolations.Translate(exception) is { } unique)
            {
                throw unique;
            }

            throw;
        }
        finally
        {
            _transaction = null;
            await transaction.DisposeAsync();
        }
    }

    // En retiro (Etapa 1): la usan los servicios que todavía no pasaron a ExecuteInTransactionAsync. La borra la Tarea 21.
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException(
                "SaveChangesAsync cannot run inside ExecuteInTransactionAsync: the unit of work saves and commits when the work returns.");
        }

        int changes;

        try
        {
            changes = await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Si un repositorio abrió una transacción (por ejemplo, para un lock), se deshace acá y no cuando se
            // descarte el contexto: sus locks se sueltan ya, y quien quiera reintentar en otro scope no se queda
            // esperando a este.
            await RollbackCurrentAsync();

            if (UniqueViolations.Translate(exception) is { } unique)
            {
                throw unique;
            }

            throw;
        }

        // Si un repositorio abrió una transacción (por ejemplo, para un lock), se confirma con el guardado.
        if (dbContext.Database.CurrentTransaction is { } transaction)
        {
            await transaction.CommitAsync(cancellationToken);
            await transaction.DisposeAsync();
        }

        return changes;
    }

    private async Task RollbackAsync(IDbContextTransaction transaction)
    {
        try
        {
            // Sin el token del pedido: aunque se haya cancelado, hay que soltar los locks.
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            // O se cortó la conexión, o un commit fallido ya cerró la transacción. En los dos casos Postgres la descarta
            // solo, y la excepción que importa es la del caso de uso.
            LogRollbackFailed(logger, exception.GetType().Name);
        }
        finally
        {
            // Lo que está seguido ya no coincide con la base, tampoco lo que Identity marcó como guardado. Así ningún
            // guardado posterior del mismo scope lo revive en autocommit.
            dbContext.ChangeTracker.Clear();
        }
    }

    // En retiro (Etapa 1): el rollback de SaveChangesAsync, sobre la transacción que haya abierto un lock.
    private async Task RollbackCurrentAsync()
    {
        if (dbContext.Database.CurrentTransaction is not { } transaction)
        {
            return;
        }

        try
        {
            // Sin el token del pedido: si se canceló, igual hay que soltar los locks.
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (DbException)
        {
            // La conexión ya se cortó: la base deshace la transacción sola, y la excepción que importa es la del guardado.
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    // Solo el tipo, sin mensaje ni valores: el test de privacidad del webhook corre en Trace.
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Rolling back a unit of work failed with {ExceptionType}; the database discards the transaction when the connection closes")]
    private static partial void LogRollbackFailed(ILogger logger, string exceptionType);
}
```

Si un rollback lanza algo que no es `DbException` ni `InvalidOperationException` en el camino de la política, el `catch` general lo vuelve a intentar (el segundo intento se traga) y relanza: no hay forma de confirmar dos veces.

- [ ] **Paso 5: el guard de los dobles de lock.** Crear `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/TransactionGuard.cs`:

```csharp
namespace ArquitecturaBase.Application.UnitTests.TestDoubles;

/// <summary>
/// Lo que exigen los locks de producción desde el final de la Etapa 1: una transacción abierta. Un doble de repositorio
/// que recibe <c>() =&gt; unitOfWork.InTransaction</c> lanza si se toma un lock fuera del límite; con null no controla
/// nada, así los tests de los helpers sueltos siguen igual.
/// </summary>
internal static class TransactionGuard
{
    public static void Require(Func<bool>? inTransaction)
    {
        if (inTransaction is not null && !inTransaction())
        {
            throw new InvalidOperationException("A lock was taken outside IUnitOfWork.ExecuteInTransactionAsync.");
        }
    }
}
```

- [ ] **Paso 6: los dobles de lock usan el guard.** Nadie les pasa todavía la función, así que no cambia ningún test.
  - En `TestDoubles/Auth/AuthFakes.cs`, en `InMemoryLoginCodeRepository`, agregar la propiedad y la primera línea del lock:

    ```csharp
        /// <summary>Si no es null, tomar el lock fuera de la transacción lanza (ver <see cref="TransactionGuard"/>).</summary>
        public Func<bool>? InTransaction { get; set; }

        public Task LockDestinationAsync(LoginCodeDestination destination, CancellationToken cancellationToken)
        {
            TransactionGuard.Require(InTransaction);
            LockedDestinations.Add(destination.Value);

            return Task.CompletedTask;
        }
    ```
  - En el mismo archivo, en `InMemoryLoginLinkRepository`: la misma propiedad `InTransaction` (con el mismo comentario) y `TransactionGuard.Require(InTransaction);` como primera línea de `LockAccountAsync`, antes del `if (WhileWaitingForTheLock is { } whileWaiting)`.
  - En `TestDoubles/Users/InMemoryUserInvitationRepository.cs`: la misma propiedad y `TransactionGuard.Require(InTransaction);` como primera línea de `LockAccountAsync`.
  - En `TestDoubles/WhatsApp/WhatsAppFakes.cs`, en `LockLog`: la misma propiedad y el lock con cuerpo de bloque:

    ```csharp
        public void Lock(IEnumerable<string> keys)
        {
            TransactionGuard.Require(InTransaction);
            Events.AddRange(keys);
        }
    ```

- [ ] **Paso 7: el doble compartido.** Reemplazar `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/FakeUnitOfWork.cs` completo por:

```csharp
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles;

/// <summary>
/// Corre el trabajo como UnitOfWork, con la misma regla de confirmación (<see cref="CommitPolicyExtensions.Commits"/>),
/// y cuenta qué pasó. Lo que el test quiere mirar "al confirmar" lo toma <see cref="OnCommit"/>, en el momento en que la
/// unidad real baja los cambios.
/// </summary>
internal sealed class FakeUnitOfWork(List<string>? events = null) : IUnitOfWork
{
    /// <summary>Cuántas veces se abrió el límite. En 0 significa que ni se abrió, por ejemplo con un ValidationError.</summary>
    public int Transactions { get; private set; }

    /// <summary>Cuántas veces se llegó al commit, contando la que falla por <see cref="CommitFailure"/>.</summary>
    public int Commits { get; private set; }

    /// <summary>Cuántas veces se deshizo: un Result fallido con OnSuccess, una excepción del trabajo o un commit fallido.</summary>
    public int Rollbacks { get; private set; }

    public CommitPolicy? LastPolicy { get; private set; }

    public bool InTransaction { get; private set; }

    /// <summary>Corre justo antes de confirmar: acá se sacan las fotos (lo enviado, las auditorías, el código gastado).</summary>
    public Action? OnCommit { get; init; }

    /// <summary>Si no es null, el commit falla con esta excepción después de <see cref="OnCommit"/>.</summary>
    public Exception? CommitFailure { get; set; }

    /// <summary>En retiro (Etapa 1): cuántas veces se llamó a SaveChangesAsync fuera del límite.</summary>
    public int SaveChangesCalls { get; private set; }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CommitPolicy policy, CancellationToken cancellationToken)
        where TResult : Result
    {
        ArgumentNullException.ThrowIfNull(work);

        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown commit policy.");
        }

        if (InTransaction)
        {
            throw new InvalidOperationException("ExecuteInTransactionAsync does not nest.");
        }

        Transactions++;
        LastPolicy = policy;
        InTransaction = true;

        try
        {
            TResult result;

            try
            {
                result = await work(cancellationToken);
            }
            catch
            {
                Rollbacks++;
                throw;
            }

            if (!policy.Commits(result))
            {
                Rollbacks++;
                return result;
            }

            Commits++;
            events?.Add("commit");
            OnCommit?.Invoke();

            if (CommitFailure is { } failure)
            {
                Rollbacks++;
                throw failure;
            }

            return result;
        }
        finally
        {
            InTransaction = false;
        }
    }

    // En retiro (Etapa 1): lo usan los servicios que todavía no migraron. Lo borra la Tarea 21.
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (InTransaction)
        {
            throw new InvalidOperationException("SaveChangesAsync cannot run inside ExecuteInTransactionAsync.");
        }

        SaveChangesCalls++;
        return Task.FromResult(1);
    }
}
```

- [ ] **Paso 8: los dobles privados implementan el método nuevo con un stub.** Cada uno se reemplaza cuando migra su servicio; si una migración se olvida del suyo, el test falla con `NotSupportedException`. Agregar este método a cada clase de la tabla, después de su `SaveChangesAsync`, cambiando `<Servicio>` por el indicado:

```csharp
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> work, CommitPolicy policy, CancellationToken cancellationToken)
            where TResult : Result =>
            throw new NotSupportedException("Replaced when <Servicio> moves to ExecuteInTransactionAsync.");
```

| Clase | Archivo | `<Servicio>` | Using a agregar |
|---|---|---|---|
| `RecordingUnitOfWork` | `tests/ArquitecturaBase.Application.UnitTests/Services/Auth/RequestLoginCodeServiceTests.cs` | `AccountService` | ninguno |
| `RecordingUnitOfWork` | `tests/ArquitecturaBase.Application.UnitTests/Services/Auth/RequestWhatsAppLoginCodeServiceTests.cs` | `AccountService` | ninguno |
| `RecordingUnitOfWork` | `tests/ArquitecturaBase.Application.UnitTests/Services/Auth/VerifyLoginCodeServiceTests.cs` | `AccountService` | ninguno |
| `ThrowingUnitOfWork` | `tests/ArquitecturaBase.Application.UnitTests/Services/Auth/ExternalLoginServiceTests.cs` | `ExternalLoginService` | `using ArquitecturaBase.Domain.Results;` |
| `Fixture.FakeUnitOfWork` | `tests/ArquitecturaBase.Application.UnitTests/Services/Settings/SystemSettingsServiceTests.cs` | `SystemSettingsService` | ninguno |
| `RecordingUnitOfWork` | `tests/ArquitecturaBase.Application.UnitTests/Services/Users/ProfileEmailServiceTests.cs` | `ProfileService` | ninguno |
| `NoOpUnitOfWork` | `tests/ArquitecturaBase.Application.UnitTests/Services/WhatsApp/WhatsAppWebhookPersistenceTests.cs` | `WhatsAppWebhookPersistence` | `using ArquitecturaBase.Domain.Results;` |
| `ThrowingCommitUnitOfWork` | `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UserRepositoryTransactionTests.cs` | `UserService` | `using ArquitecturaBase.Domain.Results;` |
| `ThrowingGoogleCommitUnitOfWork` | `tests/ArquitecturaBase.Api.IntegrationTests/Auth/ExternalLoginTests.cs` | `ExternalLoginService` | `using ArquitecturaBase.Domain.Results;` |

- [ ] **Paso 9: compilar.** `dotnet build ArquitecturaBase.slnx`. Esperado: `0 Warning(s)`, `0 Error(s)`. Si IDE0005 marca un using innecesario, borrarlo; si falta uno, agregarlo.

- [ ] **Paso 10: ver pasar los tests nuevos.** Mismo comando del Paso 2. Esperado: 15 PASS. Los dos de commit fallido cubren caminos distintos: en `A_commit_that_fails_before_reaching_the_server_...` el interceptor lanza antes del `COMMIT` real y el rollback funciona; en `A_commit_the_server_rejects_...` Postgres ya terminó la transacción y el rollback lanza `InvalidOperationException`. Solo el segundo pasa por el `catch` de `RollbackAsync` y su Warning: si se borra ese `catch`, tiene que fallar.

- [ ] **Paso 11: CLAUDE.md, sección Persistencia.** Agregar este ítem inmediatamente después del que empieza "Para poner en fila operaciones sobre un mismo recurso":

```markdown
- **En transición (Etapa 1):** la forma nueva de guardar es `IUnitOfWork.ExecuteInTransactionAsync(trabajo, CommitPolicy, ct)`: abre la transacción del caso de uso, corre el trabajo y confirma o deshace según la `CommitPolicy` (`OnSuccess`, u `OnAnyResult` cuando un error tiene que dejar registro). `IUnitOfWork.SaveChangesAsync` sigue hasta que migre el último servicio y se borra al cerrar la etapa; adentro de `ExecuteInTransactionAsync` lanza. Plan: `docs/history/plans/2026-09-26-etapa-1-una-sola-forma-de-guardar.md`.
```

- [ ] **Paso 12: suite completa.** `dotnet test`. Esperado: todo en verde (ningún servicio usa todavía la API nueva).

- [ ] **Paso 13: commit.**

```bash
git add src/ArquitecturaBase.Application/Interfaces/Persistence/IUnitOfWork.cs src/ArquitecturaBase.Infrastructure/Persistence/UnitOfWork.cs tests CLAUDE.md
git commit -m "$(cat <<'EOF'
feat: IUnitOfWork.ExecuteInTransactionAsync conviviendo con SaveChangesAsync

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 4 (C1c): `TransactionBoundaryTests` en modo trinquete

**Archivos:**
- Modificar: `Directory.Packages.props`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj`
- Crear: `tests/ArquitecturaBase.ArchitectureTests/Support/CallSites.cs`
- Crear: `tests/ArquitecturaBase.ArchitectureTests/CallSitesTests.cs`
- Crear: `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs`
- Modificar: `CLAUDE.md` (sección Tests)

- [ ] **Paso 1: Mono.Cecil explícito.** Hoy llega en forma transitiva (0.11.3) con NetArchTest.Rules 1.3.2. En `Directory.Packages.props`, en el `ItemGroup Label="Tests"`, después de `NetArchTest.Rules`, agregar:

```xml
    <PackageVersion Include="Mono.Cecil" Version="0.11.3" />
```

Y en `tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj`, en el `ItemGroup` de los paquetes, después de `NetArchTest.Rules`:

```xml
    <PackageReference Include="Mono.Cecil" />
```

- [ ] **Paso 2: el lector del IL.** Crear `tests/ArquitecturaBase.ArchitectureTests/Support/CallSites.cs`:

```csharp
using System.CodeDom.Compiler;
using System.Reflection;
using Mono.Cecil;

namespace ArquitecturaBase.ArchitectureTests.Support;

/// <summary>
/// Las llamadas y los literales de texto de un ensamblado, leídos del IL con Mono.Cecil y agrupados por el tipo de nivel
/// superior que los hace. NetArchTest mira dependencias de tipos, no llamadas, y un escaneo de fuentes se confunde con
/// los comentarios. El IL tampoco está libre de ellos: un generador de código fuente puede embeber la documentación XML
/// como literales (el de OpenAPI copia la de los tipos públicos de Api, Application e Infrastructure), y un <c>///</c>
/// que nombrara un lock contaría como un lock. Por eso se saltean los tipos que emite un generador.
/// </summary>
internal static class CallSites
{
    public sealed record Call(string Owner, string DeclaringType, string Method);

    public sealed record Literal(string Owner, string Value);

    public static IReadOnlyList<Call> Calls(Assembly assembly) =>
        Read(assembly, (owner, instruction) => instruction.Operand is MethodReference called
            ? new Call(owner, called.DeclaringType.FullName, called.Name)
            : null);

    public static IReadOnlyList<Literal> Literals(Assembly assembly) =>
        Read(assembly, (owner, instruction) => instruction.Operand is string value ? new Literal(owner, value) : null);

    // Se pasa todo a texto antes de soltar el módulo: Cecil lee algunas cosas recién cuando se las pide.
    private static List<T> Read<T>(Assembly assembly, Func<string, Mono.Cecil.Cil.Instruction, T?> select)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(assembly);

        using var module = ModuleDefinition.ReadModule(assembly.Location);

        return
        [
            .. module.GetTypes()
                .Where(type => !IsEmittedByGenerator(Outermost(type)))
                .SelectMany(type => type.Methods
                    .Where(method => method.HasBody)
                    .SelectMany(method => method.Body.Instructions
                        .Select(instruction => select(Outermost(type).FullName, instruction))))
                .OfType<T>(),
        ];
    }

    // El cuerpo de un método async o de una lambda vive en un tipo anidado que genera el compilador: cuenta como de su
    // dueño.
    private static TypeDefinition Outermost(TypeDefinition type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type;
    }

    // Un generador marca así los tipos propios que emite. Lo que agrega a un tipo parcial del proyecto ([LoggerMessage],
    // [GeneratedRegex]) lleva la marca en el miembro, no en el tipo, y se sigue leyendo como de ese tipo.
    private static bool IsEmittedByGenerator(TypeDefinition type) =>
        type.CustomAttributes.Any(attribute =>
            attribute.AttributeType.FullName == typeof(GeneratedCodeAttribute).FullName);
}
```

*Corrección de la revisión de la Tarea 4 (el lector del IL):* el IL tampoco está libre de comentarios. `Microsoft.AspNetCore.OpenApi` genera en la Api el tipo `Microsoft.AspNetCore.OpenApi.Generated.<OpenApiXmlCommentSupport_generated>…__XmlCommentCache`, que guarda como literales (casi mil `ldstr`) la documentación XML de los tipos públicos de Api, Application e Infrastructure: el `<summary>` de `IUnitOfWork.ExecuteInTransactionAsync` está ahí tal cual. Sin filtro, `CallSites.Literals` los leía como código, y en cuanto un `///` público nombrara `pg_advisory_xact_lock` (las interfaces de `Application/Interfaces/Persistence` ya documentan sus locks, y varias tareas de esta etapa les tocan la documentación), `Advisory_lock_sql_and_keys_live_in_one_place` fallaría con `New violations: Microsoft.AspNetCore.OpenApi.Generated.…__XmlCommentCache`, y la salida obvia, sumar ese dueño a una lista `Known*`, desaparece en la Tarea 21. Lo mismo valía para los tipos del generador de expresiones regulares (`System.Text.RegularExpressions.Generated.*`) en Infrastructure. Por eso `CallSites.Read` saltea los tipos cuyo tipo de nivel superior lleva `GeneratedCodeAttribute`: los emite un generador. Lo que un generador agrega a un tipo parcial del proyecto (`[LoggerMessage]`, `[GeneratedRegex]`) lleva la marca en el miembro, no en el tipo, y se sigue leyendo como de ese tipo. Ningún tipo `ArquitecturaBase.*` lleva la marca: las reglas ven los mismos dueños que antes, y las listas `Known*` lo confirman, porque fallan también cuando alguien deja de aparecer. `CallSitesTests` fija el filtro: falla si un tipo marcado de la Api sigue apareciendo como dueño de una llamada o de un literal, y antes afirma que la Api tiene alguno, para no pasar en silencio. Verificado en rojo al hacer la corrección: con `(<c>pg_advisory_xact_lock</c>)` agregado al `<summary>` de `ILoginCodeRepository.LockDestinationAsync`, `Advisory_lock_sql_and_keys_live_in_one_place` fallaba con `New violations: …__XmlCommentCache` y `CallSitesTests` con `Generated types are still scanned:`; con el filtro, las dos pasan. Las Tareas 5 y 21 no cambian: la 21 reemplaza `TransactionBoundaryTests.cs`, pero no `CallSites.cs` ni `CallSitesTests.cs`, así que sus reglas sin listas heredan el filtro.

Crear `tests/ArquitecturaBase.ArchitectureTests/CallSitesTests.cs`:

```csharp
using System.CodeDom.Compiler;
using System.Reflection;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// El lector del IL en el que se apoyan las reglas de TransactionBoundaryTests. Un generador de código fuente emite
/// tipos propios con textos que no son código del proyecto: el de OpenAPI copia como literales la documentación XML de
/// los tipos públicos de Api, Application e Infrastructure, y un <c>///</c> que nombrara un lock contaría como un lock.
/// </summary>
public sealed class CallSitesTests
{
    private static readonly Assembly Api = Assembly.Load("ArquitecturaBase.Api");

    [Fact]
    public void Types_emitted_by_source_generators_are_left_out()
    {
        var generated = Api.GetTypes()
            .Where(type => type.DeclaringType is null && type.IsDefined(typeof(GeneratedCodeAttribute), inherit: false))
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        // Si la Api dejara de usar generadores, este test no probaría nada.
        Assert.NotEmpty(generated);

        var scanned = CallSites.Literals(Api).Select(literal => literal.Owner)
            .Concat(CallSites.Calls(Api).Select(call => call.Owner))
            .Where(generated.Contains)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(scanned.Length == 0, "Generated types are still scanned: " + string.Join(", ", scanned));
    }
}
```

- [ ] **Paso 3: escribir las reglas con las listas vacías.** Crear `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs`:

```csharp
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
    private static readonly string[] KnownUnitOfWorkReceivers = [];

    private static readonly string[] KnownTransactionOpeners = [];

    private static readonly string[] KnownLockLiteralOwners = [];

    private static readonly string[] KnownSaveChangesCallers = [];

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

        // Todavía no lo llama nadie. Cuando migre el primer servicio (Tarea 6), acá se afirma que el detector lo ve.
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

        // Las claves todavía no tienen su lugar: AdvisoryLockKeys llega en la Tarea 5, que suma acá su Assert.Contains.
        Assert.Contains(AdvisoryLockExtensions, sqlOwners);

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
```

*Corrección de la revisión de la Tarea 4:* salvo `Only_use_case_entry_points_receive_the_unit_of_work`, que ya tenía su `Assert.NotEmpty`, las reglas descartaban a su dueño permitido sin comprobar que el detector lo viera, y el contrato era un texto escrito a mano. Si `IUnitOfWork` cambiara de nombre o de namespace (un refactor del IDE no toca los textos), o si un tipo de EF cambiara de nombre, la regla dejaría de encontrar llamadas y pasaría para siempre, sobre todo desde que la Tarea 21 borra las listas `Known*`, que hoy hacen de control. Por eso `UnitOfWorkContract` sale de `typeof(IUnitOfWork).FullName`, los métodos de `IUnitOfWork` se nombran con `nameof`, y cada regla afirma con `Assert.Contains` que ve a su dueño permitido antes de sacarlo: `UnitOfWork` en la de transacciones y en la de guardado, `AdvisoryLockExtensions` en la del SQL y `WhatsAppMessageRetentionRepository` en la de `ExecuteUpdate`. `AdvisoryLockKeys` suma su `Assert.Contains` en la Tarea 5, cuando existe, y `Only_use_case_entry_points_run_a_unit_of_work` suma `Assert.NotEmpty(callers)` en la Tarea 6, cuando migra el primer servicio. Los filtros se guardan en una variable `violations` antes de `Assert.Empty`: pasarle un `Where` directo es el error xUnit2029. Verificado en rojo al hacer la corrección: con otro nombre en las constantes de `UnitOfWork`, `AdvisoryLockExtensions` y la retención, las cuatro reglas que los miran fallan con `Assert.Contains() Failure`.

- [ ] **Paso 4: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj -- --filter-class "ArquitecturaBase.ArchitectureTests.TransactionBoundaryTests"`
Esperado: 3 PASS (`Only_use_case_entry_points_run_a_unit_of_work`, `Only_the_unit_of_work_saves_the_context`, `Bulk_updates_and_deletes_only_where_documented`) y 4 FAIL con `New violations:` que listan exactamente los tipos del paso siguiente. Si la lista que sale es distinta, frenar: el mapa no coincide con el código.

- [ ] **Paso 5: cargar las listas del trinquete.** Reemplazar las cuatro listas vacías por:

```csharp
    private static readonly string[] KnownUnitOfWorkReceivers =
    [
        "ArquitecturaBase.Application.Services.Users.ProfileEmailOperations",
        "ArquitecturaBase.Application.Services.Users.ProfileWhatsAppOperations",
        "ArquitecturaBase.Application.Services.Users.UserPhoneOperations",
        "ArquitecturaBase.Application.Services.Users.UserStatusOperations",
        "ArquitecturaBase.Application.Services.Users.UserWriteOperations",
    ];

    private static readonly string[] KnownTransactionOpeners =
    [
        "ArquitecturaBase.Infrastructure.Persistence.Extensions.AdvisoryLockExtensions",
        "ArquitecturaBase.Infrastructure.Persistence.Repositories.LoginCodeRepository",
        "ArquitecturaBase.Infrastructure.Persistence.Repositories.RoleRepository",
        "ArquitecturaBase.Infrastructure.Persistence.Repositories.WhatsAppContactRepository",
    ];

    private static readonly string[] KnownLockLiteralOwners =
    [
        "ArquitecturaBase.Infrastructure.Persistence.Repositories.LoginCodeRepository",
        "ArquitecturaBase.Infrastructure.Persistence.Repositories.LoginLinkRepository",
        "ArquitecturaBase.Infrastructure.Persistence.Repositories.UserInvitationRepository",
        "ArquitecturaBase.Infrastructure.Persistence.Repositories.UserRepository",
        "ArquitecturaBase.Infrastructure.Persistence.Repositories.WhatsAppContactRepository",
        "ArquitecturaBase.Infrastructure.Persistence.Repositories.WhatsAppMessageRepository",
    ];

    private static readonly string[] KnownSaveChangesCallers =
    [
        "ArquitecturaBase.Application.Services.Auth.AccountService",
        "ArquitecturaBase.Application.Services.Auth.ExternalLoginService",
        "ArquitecturaBase.Application.Services.Auth.LoginLinkService",
        "ArquitecturaBase.Application.Services.Settings.SystemSettingsService",
        "ArquitecturaBase.Application.Services.Users.ProfileEmailOperations",
        "ArquitecturaBase.Application.Services.Users.ProfileService",
        "ArquitecturaBase.Application.Services.Users.ProfileWhatsAppOperations",
        "ArquitecturaBase.Application.Services.Users.UserPhoneOperations",
        "ArquitecturaBase.Application.Services.Users.UserService",
        "ArquitecturaBase.Application.Services.Users.UserStatusOperations",
        "ArquitecturaBase.Application.Services.Users.UserWriteOperations",
        "ArquitecturaBase.Application.Services.WhatsApp.WhatsAppDeliveryService",
        "ArquitecturaBase.Application.Services.WhatsApp.WhatsAppInboundService",
        "ArquitecturaBase.Application.Services.WhatsApp.WhatsAppWebhookPersistence",
    ];
```

- [ ] **Paso 6: verlo pasar.** Mismo comando del Paso 4. Esperado: 7 PASS.

- [ ] **Paso 7: CLAUDE.md, sección Tests.** El ítem que empieza `**ArchitectureTests:**` ya enumera las convenciones que verifican otros tests (desde `d63eb43`): no se reemplaza. Agregar al final de ese mismo ítem, en la misma línea:

```markdown
 `TransactionBoundaryTests` lee el IL con Mono.Cecil: solo un punto de entrada de un caso de uso recibe `IUnitOfWork` y llama a `ExecuteInTransactionAsync`, solo `UnitOfWork` abre, confirma y guarda, el SQL y las claves de los locks viven en un solo lugar, y `ExecuteUpdate`/`ExecuteDelete` solo en la retención.
```

- [ ] **Paso 8: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 9: commit.**

```bash
git add Directory.Packages.props tests/ArquitecturaBase.ArchitectureTests CLAUDE.md
git commit -m "$(cat <<'EOF'
test: reglas del límite transaccional en modo trinquete

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

**De acá en adelante, cada tarea de migración empieza sacando de las listas `Known*` lo que ese commit deja de infringir.** Con el código todavía sin migrar, el test falla con `New violations:`; después de migrar, vuelve a verde. Es el paso rojo del trinquete.

---

### Tarea 5 (C2): catálogo de claves de advisory lock

Mismo SQL, mismo hash y el mismo texto de cada clave: solo cambia dónde se escribe. `LoginCodeRepository` deja de reimplementar el lock. Todavía abren la transacción si no hay una (eso cambia en la Tarea 20).

**Archivos:**
- Crear: `src/ArquitecturaBase.Infrastructure/Persistence/Extensions/AdvisoryLockKeys.cs`
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/AdvisoryLockKeysTests.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/LoginCodeRepository.cs:1-23`
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/LoginLinkRepository.cs:10-13`
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/UserInvitationRepository.cs:10-13`
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/WhatsAppMessageRepository.cs:10-13`
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/WhatsAppContactRepository.cs:10-20`
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/UserRepository.cs:25-35`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` (trinquete)
- Modificar: `CLAUDE.md` (sección WhatsApp)

- [ ] **Paso 1: escribir el test que falla.** Crear `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/AdvisoryLockKeysTests.cs` (no usa la base):

```csharp
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// El texto de cada clave de pg_advisory_xact_lock. El texto ES el lock: cambiarlo deja de poner en fila a quien use el
/// texto viejo (la versión anterior de la Api durante un despliegue) o separa casos de uso que hoy se esperan entre sí.
/// </summary>
public sealed class AdvisoryLockKeysTests
{
    private static readonly Guid AccountId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

    [Fact]
    public void The_login_code_key_is_the_normalized_destination()
    {
        Assert.Equal(
            "login-code:ana@example.com",
            AdvisoryLockKeys.LoginCode(LoginCodeDestination.ForEmail(Email.Create(" Ana@Example.com ").Value)));
        Assert.Equal(
            "login-code:+5493515550101",
            AdvisoryLockKeys.LoginCode(LoginCodeDestination.ForPhone(PhoneNumber.Create("+5493515550101").Value)));
    }

    [Fact]
    public void Account_keys_use_the_id_in_n_format()
    {
        Assert.Equal("login-link:0123456789abcdef0123456789abcdef", AdvisoryLockKeys.LoginLink(AccountId));
        Assert.Equal("user-invitation:0123456789abcdef0123456789abcdef", AdvisoryLockKeys.UserInvitation(AccountId));
    }

    [Fact]
    public void WhatsApp_keys_keep_the_text_that_meta_sends()
    {
        Assert.Equal("whatsapp-contact:user:AR.1102953142229032", AdvisoryLockKeys.WhatsAppContactByUser("AR.1102953142229032"));
        Assert.Equal("whatsapp-contact:wa:5493413654813", AdvisoryLockKeys.WhatsAppContactByWaId("5493413654813"));
        Assert.Equal("whatsapp-message:wamid.1", AdvisoryLockKeys.WhatsAppMessage("wamid.1"));
    }

    [Fact]
    public void The_external_login_key_is_the_provider_and_its_key()
    {
        Assert.Equal("external-login:Google:k", AdvisoryLockKeys.ExternalLogin("Google", "k"));
    }
}
```

- [ ] **Paso 2: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.AdvisoryLockKeysTests"`
Esperado: FAIL de compilación (`AdvisoryLockKeys` no existe).

- [ ] **Paso 3: crear `src/ArquitecturaBase.Infrastructure/Persistence/Extensions/AdvisoryLockKeys.cs`.** Son los textos exactos de hoy (`LoginCodeRepository.cs:9`, `UserRepository.cs:33`, `LoginLinkRepository.cs:10`, `UserInvitationRepository.cs:10`, `WhatsAppContactRepository.cs:11-12`, `WhatsAppMessageRepository.cs:10`):

```csharp
using System.Globalization;
using ArquitecturaBase.Domain.Authentication;

namespace ArquitecturaBase.Infrastructure.Persistence.Extensions;

/// <summary>
/// Todas las claves de pg_advisory_xact_lock. El texto ES el lock: Postgres toma hashtextextended(clave, 0). Cambiar un
/// prefijo o el formato de un id deja de poner en fila a quien use el texto viejo, por ejemplo la versión anterior de la
/// Api durante un despliegue, o separa casos de uso que hoy se esperan entre sí (el verify, Google, el alta del
/// administrador y el perfil comparten login-code:). AcquireAdvisoryLocksAsync ordena las claves de una misma llamada en
/// orden ordinal. El orden entre llamadas lo decide quien llama y no se cambia: contactos antes que cuenta, y el
/// login-code: del correo antes que el del número, en DOS llamadas. En una sola, el orden ordinal pondría '+54…' antes
/// que el correo. AdvisoryLockKeysTests fija cada texto.
/// </summary>
internal static class AdvisoryLockKeys
{
    /// <summary>"login-code:" + LoginCodeDestination.Value: el correo normalizado (Email.Value) o el E.164 con el 9.</summary>
    public static string LoginCode(LoginCodeDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return "login-code:" + destination.Value;
    }

    /// <summary>"login-link:" + el Id de la cuenta en formato N. Es el lock "de la cuenta".</summary>
    public static string LoginLink(Guid userId) => "login-link:" + userId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>"user-invitation:" + el Id de la cuenta en formato N.</summary>
    public static string UserInvitation(Guid userId) =>
        "user-invitation:" + userId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// "whatsapp-contact:user:" + el BSUID. El prefijo hace que el mismo texto como BSUID y como número no compartan lock.
    /// </summary>
    public static string WhatsAppContactByUser(string userIdentifier) => "whatsapp-contact:user:" + userIdentifier;

    /// <summary>"whatsapp-contact:wa:" + el wa_id, tal como lo manda Meta (sin '+').</summary>
    public static string WhatsAppContactByWaId(string waId) => "whatsapp-contact:wa:" + waId;

    /// <summary>"whatsapp-message:" + el wamid. Se pide después de los contactos, en otra llamada.</summary>
    public static string WhatsAppMessage(string waMessageId) => "whatsapp-message:" + waMessageId;

    /// <summary>
    /// "external-login:" + proveedor + ":" + clave del proveedor. Solo la toma Google, en la misma llamada que el
    /// login-code: del correo. Por el orden ordinal se toma primero, y como nadie más la pide, no hay ciclo.
    /// </summary>
    public static string ExternalLogin(string provider, string providerKey) =>
        "external-login:" + provider + ":" + providerKey;
}
```

`ToString("N")` da el mismo texto con `InvariantCulture` o sin ella; se escribe con `InvariantCulture` por CA1305.

- [ ] **Paso 4: verlo pasar.** Mismo comando del Paso 2. Esperado: 4 PASS.

- [ ] **Paso 5: el paso rojo del trinquete.** En `TransactionBoundaryTests.cs`: dejar `KnownLockLiteralOwners` vacía (`private static readonly string[] KnownLockLiteralOwners = [];`) y sacar la línea de `LoginCodeRepository` de `KnownTransactionOpeners`. Además, en `Advisory_lock_sql_and_keys_live_in_one_place`, reemplazar el comentario que empieza "Las claves todavía no tienen su lugar" y el `Assert.Contains(AdvisoryLockExtensions, sqlOwners);` que lo sigue por:

```csharp
        // Los dos dueños permitidos tienen que aparecer: si el detector dejara de verlos, la regla pasaría en silencio.
        Assert.Contains(AdvisoryLockExtensions, sqlOwners);
        Assert.Contains(AdvisoryLockKeys, keyOwners);
```

(*Corrección de la revisión de la Tarea 4:* sin la lista `KnownLockLiteralOwners`, esa afirmación es lo único que detecta que las claves dejaron de verse. `AdvisoryLockKeys` existe desde el Paso 3, así que pasa: el rojo de este paso lo sigue dando `New violations:`.)

Run: `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj -- --filter-class "ArquitecturaBase.ArchitectureTests.TransactionBoundaryTests"`
Esperado: FAIL en `Advisory_lock_sql_and_keys_live_in_one_place` (`New violations:` con los seis repositorios) y en `Only_the_unit_of_work_opens_commits_or_rolls_back_transactions` (`New violations: ...LoginCodeRepository`).

- [ ] **Paso 6: `LoginCodeRepository` usa la extensión y el catálogo.** Reemplazar las líneas 1-23 (desde los usings hasta el final de `LockDestinationAsync`) por:

```csharp
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class LoginCodeRepository(ApplicationDbContext dbContext) : ILoginCodeRepository
{
    // La clave es solo el destino, sin el propósito: todo lo que se pide para ese correo o ese número va en la misma fila.
    // Una clave por llamada: quien necesita dos (el alta y la edición del administrador) llama dos veces, correo primero.
    public Task LockDestinationAsync(LoginCodeDestination destination, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([AdvisoryLockKeys.LoginCode(destination)], cancellationToken);
```

(El resto del archivo, desde `GetLatestAsync`, no cambia.)

- [ ] **Paso 7: los demás repositorios usan el catálogo.**
  - `LoginLinkRepository.cs`: borrar `private const string LockKeyPrefix = "login-link:";` y dejar el lock así:

    ```csharp
        public Task LockAccountAsync(Guid userId, CancellationToken cancellationToken) =>
            dbContext.AcquireAdvisoryLocksAsync([AdvisoryLockKeys.LoginLink(userId)], cancellationToken);
    ```
  - `UserInvitationRepository.cs`: borrar `private const string LockKeyPrefix = "user-invitation:";` y:

    ```csharp
        public Task LockAccountAsync(Guid userId, CancellationToken cancellationToken) =>
            dbContext.AcquireAdvisoryLocksAsync([AdvisoryLockKeys.UserInvitation(userId)], cancellationToken);
    ```
  - `WhatsAppMessageRepository.cs`: borrar `private const string LockPrefix = "whatsapp-message:";` y:

    ```csharp
        public Task LockAsync(IReadOnlyCollection<string> waMessageIds, CancellationToken cancellationToken) =>
            dbContext.AcquireAdvisoryLocksAsync(waMessageIds.Select(AdvisoryLockKeys.WhatsAppMessage), cancellationToken);
    ```
  - `WhatsAppContactRepository.cs`: borrar las dos constantes y su comentario (líneas 10-12) y dejar:

    ```csharp
        public Task LockAsync(
            IReadOnlyCollection<string> userIdentifiers,
            IReadOnlyCollection<string> waIds,
            CancellationToken cancellationToken) =>
            dbContext.AcquireAdvisoryLocksAsync(
                userIdentifiers.Select(AdvisoryLockKeys.WhatsAppContactByUser)
                    .Concat(waIds.Select(AdvisoryLockKeys.WhatsAppContactByWaId)),
                cancellationToken);
    ```
  - `UserRepository.cs`: agregar `using ArquitecturaBase.Domain.Authentication;` y reemplazar `LockExternalSignInAsync` por:

    ```csharp
        public Task LockExternalSignInAsync(
            Email email, string provider, string providerKey, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(email);

            // En una sola llamada, que las ordena: external-login: se toma antes que login-code:. Nadie más toma
            // external-login:, así que ese orden no se cruza con otro caso de uso. login-code: es la misma clave del correo
            // que toman los códigos, el alta del administrador y el perfil.
            return dbContext.AcquireAdvisoryLocksAsync(
                [AdvisoryLockKeys.LoginCode(LoginCodeDestination.ForEmail(email)),
                 AdvisoryLockKeys.ExternalLogin(provider, providerKey)],
                cancellationToken);
        }
    ```

    (Corrige el comentario viejo, que decía que "la primera clave" era la del correo: el orden ordinal toma primero `external-login:`. `ForEmail` usa `email.Value`, así que el texto es idéntico al literal de hoy.)

- [ ] **Paso 8: verlo pasar.** Mismo comando del Paso 5. Esperado: 7 PASS. Después, los tests que miran los locks contra Postgres:

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppLockOrderTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.LoginCodeConcurrencyTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.ExternalLoginTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.UnitOfWorkTransactionTests"`
Esperado: todo PASS.

- [ ] **Paso 9: CLAUDE.md, sección WhatsApp.** En el ítem que empieza "**Los locks van siempre en el mismo orden**", reemplazar la última oración ("Las claves de `pg_advisory_xact_lock` están en los repositorios (...) y se toman ordenadas y sin repetir (`AdvisoryLockExtensions`).") por:

```markdown
Las claves de `pg_advisory_xact_lock` están todas en `AdvisoryLockKeys` (`login-code:`, `login-link:`, `user-invitation:`, `whatsapp-contact:user:`, `whatsapp-contact:wa:`, `whatsapp-message:` y `external-login:`): el texto es el lock, y cambiarlo deja de poner en fila a quien use el viejo. Dentro de una llamada se toman ordenadas y sin repetir (`AdvisoryLockExtensions`); entre llamadas el orden lo fija quien llama: `login-code:` del correo y después el del número, en dos llamadas; contactos antes que cuenta.
```

- [ ] **Paso 10: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 11: commit.**

```bash
git add src/ArquitecturaBase.Infrastructure/Persistence tests/ArquitecturaBase.Api.IntegrationTests/Persistence/AdvisoryLockKeysTests.cs tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs CLAUDE.md
git commit -m "$(cat <<'EOF'
refactor: catálogo de claves de advisory lock

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 6 (C3): `RoleService` es dueño de su transacción (área de referencia)

**Archivos:**
- Crear: `src/ArquitecturaBase.Infrastructure/Persistence/Extensions/TransactionExtensions.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/RoleRepository.cs` (reemplazo completo)
- Modificar: `src/ArquitecturaBase.Application/Interfaces/Persistence/IRoleRepository.cs:3-6`
- Modificar: `src/ArquitecturaBase.Application/Services/Roles/RoleService.cs` (reemplazo completo)
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Roles/RoleServiceWriteTests.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Roles/RoleServiceTests.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/RoleRepositoryTransactionTests.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` (trinquete)

- [ ] **Paso 1: los tests unitarios piden el límite.** En `RoleServiceWriteTests.cs`:
  - agregar `using ArquitecturaBase.Application.UnitTests.TestDoubles;`;
  - en `Fixture`, crear la unidad de trabajo con los eventos compartidos y pasarla al servicio, antes del logger:

    ```csharp
            public Fixture()
            {
                Reader = new FakeReader();
                Repository = new FakeRepository(this);
                UnitOfWork = new FakeUnitOfWork(Events);
                Service = new RoleService(
                    Reader,
                    Repository,
                    new FakePermissionService(this),
                    new ServiceRequestValidator<CreateRoleRequest>([new CreateRoleRequestValidator()]),
                    new ServiceRequestValidator<UpdateRoleRequest>([new UpdateRoleRequestValidator()]),
                    UnitOfWork,
                    Logger);
            }

            public FakeUnitOfWork UnitOfWork { get; }
    ```
  - cambiar las aserciones así (el resto de cada test no cambia):

| Test | Antes | Después |
|---|---|---|
| `Create_validates_all_fields_before_reading_or_writing` | `Assert.Empty(fixture.Events);` | `Assert.Empty(fixture.Events);` + `Assert.Equal(0, fixture.UnitOfWork.Transactions);` |
| `Create_trims_the_name_and_removes_duplicate_permissions` | `Assert.Equal(["create"], fixture.Events);` | `Assert.Equal(["create", "commit"], fixture.Events);` + `Assert.Equal(CommitPolicy.OnSuccess, fixture.UnitOfWork.LastPolicy);` |
| `Create_rejects_an_existing_normalized_name_before_writing` | `Assert.Empty(fixture.Events);` | `Assert.Empty(fixture.Events);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |
| `Update_keeps_system_names_and_admin_permissions` | segundo `Assert.Empty(fixture.Events);` | `Assert.Empty(fixture.Events);` + `Assert.Equal(2, fixture.UnitOfWork.Rollbacks);` |
| `Update_sort_permissions_and_invalidates_cache_after_the_repository_returns` → renombrar a `Update_sorts_permissions_and_invalidates_the_cache_only_after_the_commit` | `Assert.Equal(["update", "invalidate"], fixture.Events);` | `Assert.Equal(["update", "commit", "invalidate"], fixture.Events);` |
| `Failed_update_does_not_invalidate_the_cache` | `Assert.Equal(["update"], fixture.Events);` | `Assert.Equal(["update"], fixture.Events);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |
| `Delete_rejects_assigned_users_with_the_existing_count` | `Assert.Empty(fixture.Events);` | `Assert.Empty(fixture.Events);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |
| `Delete_invalidates_the_cache_after_repository_deletion` → renombrar a `Delete_invalidates_the_cache_only_after_the_commit` | `Assert.Equal(["delete", "invalidate"], fixture.Events);` | `Assert.Equal(["delete", "commit", "invalidate"], fixture.Events);` |

- [ ] **Paso 2: `RoleServiceTests` construye el servicio con la unidad de trabajo.** Agregar `using ArquitecturaBase.Application.UnitTests.TestDoubles;` y, en `NewService`, `new FakeUnitOfWork(),` como penúltimo argumento (antes de `logger`).

- [ ] **Paso 3: el repositorio exige la transacción.** En `RoleRepositoryTransactionTests.cs`, agregar `using ArquitecturaBase.Domain.Results;`. En `Failed_claim_write_rolls_back_role_and_preceding_claim_writes`, reemplazar desde el comentario "El nombre, la descripción y dos cambios de claims..." hasta el `}));` del `Assert.ThrowsAsync` por el primer bloque de abajo (la llamada queda dentro del límite; la preparación con `IIdentityService.CreateRoleAsync` y las aserciones no cambian). Después, agregar el test nuevo (segundo bloque):

```csharp
        // El nombre, la descripción y dos cambios de claims se autoguardan antes de llegar al valor nulo.
        // El valor nulo provoca un error determinista al construir el último Claim.
        await Assert.ThrowsAsync<ArgumentNullException>(() => factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
                async ct =>
                {
                    await services.GetRequiredService<IRoleRepository>().UpdateAsync(
                        roleId, newName, "After", [Permissions.Roles.Read, null!], ct);

                    return Result.Success();
                },
                CommitPolicy.OnSuccess,
                Ct)));
```

```csharp
    [Fact]
    public async Task Role_writes_outside_a_transaction_throw()
    {
        var name = UniqueName();

        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IRoleRepository>().CreateAsync(name, null, [Permissions.Users.Read], Ct)));

        Assert.False(await factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IRoleReader>().RoleNameExistsAsync(name, excludedRoleId: null, Ct)));
    }
```

- [ ] **Paso 4: el paso rojo del trinquete.** En `TransactionBoundaryTests.cs`, sacar la línea de `RoleRepository` de `KnownTransactionOpeners`. Además, en `Only_use_case_entry_points_run_a_unit_of_work`, reemplazar el comentario que empieza "Todavía no lo llama nadie" por:

```csharp
        // Si el detector no viera a nadie, la regla pasaría en silencio.
        Assert.NotEmpty(callers);

```

(*Corrección de la revisión de la Tarea 4:* `RoleService` es el primer servicio que llama a `ExecuteInTransactionAsync`. Hasta el Paso 9 esa regla falla en `Assert.NotEmpty`, y el Paso 10 la ve pasar.)

- [ ] **Paso 5: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.Roles.RoleServiceWriteTests" --filter-class "ArquitecturaBase.Application.UnitTests.Services.Roles.RoleServiceTests"`
Esperado: FAIL de compilación (`RoleService` no recibe `IUnitOfWork`).

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-method "ArquitecturaBase.Api.IntegrationTests.Persistence.RoleRepositoryTransactionTests.Role_writes_outside_a_transaction_throw"`
Esperado: FAIL (hoy el repositorio abre su propia transacción y el alta se confirma, así que no lanza). El otro test de la clase, ya envuelto en el límite, pasa antes y después del cambio.

- [ ] **Paso 6: crear `src/ArquitecturaBase.Infrastructure/Persistence/Extensions/TransactionExtensions.cs`.**

```csharp
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Extensions;

internal static class TransactionExtensions
{
    /// <summary>
    /// Lanza si no hay una transacción abierta. Un lock de Postgres, advisory o de fila, dura lo que la transacción, y una
    /// escritura con varios autoguardados de Identity solo es atómica dentro de una. Fuera de
    /// IUnitOfWork.ExecuteInTransactionAsync correrían en autocommit. Es un bug de quien llama, no una regla de negocio. La
    /// transacción la abre ExecuteInTransactionAsync, y nadie más.
    /// </summary>
    public static void RequireTransaction(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "This operation needs the transaction of the use case: call it inside IUnitOfWork.ExecuteInTransactionAsync.");
        }
    }
}
```

- [ ] **Paso 7: `RoleRepository` sin transacción propia.** Reemplazar el archivo completo por:

```csharp
using System.Security.Claims;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

/// <summary>
/// Escrituras de roles con RoleManager, que guarda el rol y cada claim por separado sobre el contexto compartido. No abre
/// transacción: exige la del caso de uso. Sin ella, un rol podría quedar con parte de sus permisos.
/// </summary>
internal sealed class RoleRepository(RoleManager<ApplicationRole> roleManager, ApplicationDbContext dbContext) : IRoleRepository
{
    public async Task<Guid> CreateAsync(
        string name,
        string? description,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        var role = new ApplicationRole(name) { Description = description };
        (await roleManager.CreateAsync(role)).EnsureSucceeded("create the role");
        await SetRolePermissionsAsync(role, permissions);

        return role.Id;
    }

    public async Task UpdateAsync(
        Guid roleId,
        string name,
        string? description,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        var role = await RequireRoleAsync(roleId, cancellationToken);
        role.Description = description;

        // SetRoleNameAsync escribe el nombre y el normalizado en el store; UpdateAsync es el que guarda.
        (await roleManager.SetRoleNameAsync(role, name)).EnsureSucceeded("rename the role");
        (await roleManager.UpdateAsync(role)).EnsureSucceeded("update the role");

        await SetRolePermissionsAsync(role, permissions);
    }

    public async Task DeleteAsync(Guid roleId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        (await roleManager.DeleteAsync(await RequireRoleAsync(roleId, cancellationToken)))
            .EnsureSucceeded("delete the role");
    }

    private async Task SetRolePermissionsAsync(ApplicationRole role, IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var current = (await roleManager.GetClaimsAsync(role))
            .Where(claim => claim.Type == Permissions.ClaimType)
            .ToList();

        foreach (var claim in current.Where(claim => !permissions.Contains(claim.Value, StringComparer.Ordinal)))
        {
            (await roleManager.RemoveClaimAsync(role, claim)).EnsureSucceeded("remove a permission");
        }

        var kept = current.Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal);

        foreach (var permission in permissions.Where(permission => !kept.Contains(permission)))
        {
            (await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission)))
                .EnsureSucceeded("add a permission");
        }
    }

    private async Task<ApplicationRole> RequireRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        await roleManager.Roles.FirstOrDefaultAsync(role => role.Id == roleId, cancellationToken)
            ?? throw new InvalidOperationException("The role does not exist.");
}
```

- [ ] **Paso 8: documentación de `IRoleRepository`.** Reemplazar el `<summary>` de la interfaz por:

```csharp
/// <summary>
/// Escrituras de roles sobre Identity. RoleManager guarda el rol y cada claim por separado. Cada operación exige la
/// transacción del caso de uso (IUnitOfWork.ExecuteInTransactionAsync): sin ella lanza InvalidOperationException, porque
/// un rol podría quedar con parte de sus permisos.
/// </summary>
```

- [ ] **Paso 9: `RoleService` con el patrón canónico.** Reemplazar `src/ArquitecturaBase.Application/Services/Roles/RoleService.cs` completo por:

```csharp
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Roles;

/// <summary>
/// Lecturas y cambios de roles, con sus reglas y la invalidación de permisos cacheados. Es el área de referencia del
/// límite transaccional: cada escritura valida afuera, corre en un solo ExecuteInTransactionAsync y, recién después del
/// commit, invalida el caché.
/// </summary>
internal sealed partial class RoleService(
    IRoleReader roles,
    IRoleRepository repository,
    IPermissionService permissionService,
    ServiceRequestValidator<CreateRoleRequest> createValidator,
    ServiceRequestValidator<UpdateRoleRequest> updateValidator,
    IUnitOfWork unitOfWork,
    ILogger<RoleService> logger) : IRoleService
{
    public async Task<Result<IReadOnlyCollection<RoleResponse>>> GetRolesAsync(CancellationToken cancellationToken)
    {
        LogHandling(logger, "GetRoles");
        var items = await roles.ListRolesAsync(cancellationToken);
        IReadOnlyCollection<RoleResponse> response =
        [
            .. items.Select(role => new RoleResponse(
                role.Id,
                role.Name,
                role.Description,
                role.IsSystemRole,
                role.UserCount,
                role.Permissions)),
        ];
        LogHandled(logger, "GetRoles");

        return Result.Success(response);
    }

    public Task<Result<IReadOnlyCollection<PermissionGroup>>> GetPermissionsAsync(CancellationToken cancellationToken)
    {
        LogHandling(logger, "GetPermissions");

        // El catálogo sale de Permissions.All: áreas y permisos quedan en el orden en que se declaran, con los textos
        // de Permissions.resx en el idioma del pedido.
        IReadOnlyCollection<PermissionGroup> groups =
        [
            .. Permissions.All
                .GroupBy(AreaOf, StringComparer.Ordinal)
                .Select(group => new PermissionGroup(
                    group.Key,
                    PermissionTexts.Area(group.Key),
                    [.. group.Select(permission => new PermissionItem(
                        permission,
                        PermissionTexts.Permission(permission),
                        PermissionTexts.Description(permission)))])),
        ];

        LogHandled(logger, "GetPermissions");
        return Task.FromResult(Result.Success(groups));
    }

    public async Task<Result<Guid>> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, "CreateRole");

        // Afuera: un pedido inválido no abre transacción.
        if (await createValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogFailed(logger, "CreateRole", validationError.Code);
            return validationError;
        }

        // Un rol nuevo no lo tiene nadie todavía: no hay caché que invalidar después del commit.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => CreateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

        LogOutcome("CreateRole", result);
        return result;
    }

    public async Task<Result> UpdateAsync(UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, "UpdateRole");

        // 1. Afuera: validar el pedido. Un pedido inválido no abre transacción ni toma locks.
        if (await updateValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogFailed(logger, "UpdateRole", validationError.Code);
            return validationError;
        }

        // 2. Un solo límite, con la política escrita: un error de negocio no deja nada.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => UpdateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

        // 3. Afuera, después del commit y solo si se confirmó. Invalidar antes dejaría que una lectura concurrente vuelva
        //    a cachear los permisos viejos durante una hora.
        if (result.IsSuccess)
        {
            await permissionService.InvalidateRoleAsync(request.RoleId, cancellationToken);
        }

        LogOutcome("UpdateRole", result);
        return result;
    }

    public async Task<Result> DeleteAsync(DeleteRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, "DeleteRole");

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => DeleteCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

        if (result.IsSuccess)
        {
            await permissionService.InvalidateRoleAsync(request.RoleId, cancellationToken);
        }

        LogOutcome("DeleteRole", result);
        return result;
    }

    // Adentro va todo lo que lee para decidir y todo lo que escribe, sin logs de éxito ni efectos que dependan del commit.
    private async Task<Result<Guid>> CreateCoreAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name!.Trim();
        if (await roles.RoleNameExistsAsync(name, excludedRoleId: null, cancellationToken))
        {
            return RoleErrors.AlreadyExists;
        }

        IReadOnlyCollection<string> permissions = [.. (request.Permissions ?? []).Distinct(StringComparer.Ordinal)];

        // RoleManager autoguarda el rol y cada claim: dentro de la transacción, o se guarda todo o no se guarda nada.
        return await repository.CreateAsync(name, request.Description, permissions, cancellationToken);
    }

    private async Task<Result> UpdateCoreAsync(UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await roles.FindRoleAsync(request.RoleId, cancellationToken);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        var name = request.Name!.Trim();
        IReadOnlyCollection<string> permissions =
            [.. (request.Permissions ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        // Admin y User no se renombran; Admin conserva siempre todos sus permisos.
        if (role.IsSystemRole && !string.Equals(role.Name, name, StringComparison.Ordinal))
        {
            return RoleErrors.SystemRoleCannotChange;
        }

        if (string.Equals(role.Name, SystemRoles.Admin, StringComparison.Ordinal)
            && !permissions.SequenceEqual(role.Permissions, StringComparer.Ordinal))
        {
            return RoleErrors.SystemRoleCannotChange;
        }

        if (await roles.RoleNameExistsAsync(name, role.Id, cancellationToken))
        {
            return RoleErrors.AlreadyExists;
        }

        // RoleManager autoguarda el rol y cada claim: dentro de la transacción, o se guarda todo o no se guarda nada.
        await repository.UpdateAsync(role.Id, name, request.Description, permissions, cancellationToken);
        return Result.Success();
    }

    private async Task<Result> DeleteCoreAsync(DeleteRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await roles.FindRoleAsync(request.RoleId, cancellationToken);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        if (role.IsSystemRole)
        {
            return RoleErrors.SystemRoleCannotChange;
        }

        if (role.UserCount > 0)
        {
            return RoleErrors.HasUsers(role.UserCount);
        }

        await repository.DeleteAsync(role.Id, cancellationToken);
        return Result.Success();
    }

    private void LogOutcome(string operation, Result result)
    {
        if (result.IsSuccess)
        {
            LogHandled(logger, operation);
        }
        else
        {
            LogFailed(logger, operation, result.Error.Code);
        }
    }

    private static string AreaOf(string permission) => permission[..permission.IndexOf('.', StringComparison.Ordinal)];

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {Operation}")]
    private static partial void LogHandling(ILogger logger, string operation);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Operation}")]
    private static partial void LogHandled(ILogger logger, string operation);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Operation} failed with {ErrorCode}")]
    private static partial void LogFailed(ILogger logger, string operation, string errorCode);
}
```

Los textos de log no cambian ("Handling UpdateRole", "UpdateRole failed with {ErrorCode}", "Handled UpdateRole"); antes se escribía un `LogFailed` por rama y ahora uno solo, con `result.Error.Code`.

- [ ] **Paso 10: verlo pasar.**

Run: los dos comandos del Paso 5, más `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.RoleRepositoryTransactionTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Roles.RoleCrudEndpointsTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Roles.RolesEndpointsTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.RoleReaderTests"` y `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj`.
Esperado: todo PASS.

- [ ] **Paso 11: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 12: commit.**

```bash
git add src/ArquitecturaBase.Infrastructure/Persistence src/ArquitecturaBase.Application/Interfaces/Persistence/IRoleRepository.cs src/ArquitecturaBase.Application/Services/Roles/RoleService.cs tests
git commit -m "$(cat <<'EOF'
refactor(roles): RoleService es dueño de su transacción

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 7 (C4): `SystemSettingsService` con `ExecuteInTransactionAsync`

Dos commits. El primero migra el servicio. El segundo es la mejora explícita del §2.8: la fábrica de caché del lector deja de leer con el contexto de quien llama.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/Settings/SystemSettingsService.cs:35-62`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Settings/SystemSettingsServiceTests.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` (trinquete)
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Readers/SystemSettingsReader.cs` (reemplazo completo; segundo commit)
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Settings/SystemSettingsReaderTests.cs` (segundo commit)

- [ ] **Paso 1: los tests usan el doble compartido.** En `SystemSettingsServiceTests.cs`:
  - agregar `using ArquitecturaBase.Application.UnitTests.TestDoubles;`;
  - borrar la clase anidada `Fixture.FakeUnitOfWork` y el campo `_unitOfWork`;
  - reemplazar el constructor de `Fixture` y la propiedad `SaveException` por:

    ```csharp
            public Fixture()
            {
                _repository = new FakeRepository(this);
                _reader = new FakeReader(this);
                UnitOfWork = new FakeUnitOfWork(Events);
                Service = new SystemSettingsService(
                    _repository,
                    _reader,
                    UnitOfWork,
                    new ServiceRequestValidator<UpdateSystemSettingsRequest>(
                        [new UpdateSystemSettingsRequestValidator()]),
                    Logger);
            }

            public FakeUnitOfWork UnitOfWork { get; }

            /// <summary>Hace fallar el commit, después de registrarlo en <see cref="Events"/>.</summary>
            public Exception? SaveException
            {
                get => UnitOfWork.CommitFailure;
                set => UnitOfWork.CommitFailure = value;
            }
    ```
  - cambiar las aserciones:

| Test | Antes | Después |
|---|---|---|
| `Update_returns_not_found_without_saving_when_the_single_row_is_missing` | `Assert.Empty(fixture.Events);` | `Assert.Empty(fixture.Events);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |
| `Update_validates_before_reading_or_saving` | `Assert.Empty(fixture.Events);` | `Assert.Empty(fixture.Events);` + `Assert.Equal(0, fixture.UnitOfWork.Transactions);` |
| `Update_saves_the_changed_mode_once_before_invalidating_the_cache` | `Assert.Equal(["save", "invalidate"], fixture.Events);` | `Assert.Equal(["commit", "invalidate"], fixture.Events);` + `Assert.Equal(CommitPolicy.OnSuccess, fixture.UnitOfWork.LastPolicy);` |
| `Update_does_not_invalidate_the_cache_when_saving_fails` | `Assert.Equal(["save"], fixture.Events);` | `Assert.Equal(["commit"], fixture.Events);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |

- [ ] **Paso 2: el paso rojo del trinquete.** Sacar `SystemSettingsService` de `KnownSaveChangesCallers`.

- [ ] **Paso 3: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.Settings.SystemSettingsServiceTests"`
Esperado: FAIL (los eventos quedan en `["invalidate"]`: el servicio todavía llama a `SaveChangesAsync`, que no registra eventos).

- [ ] **Paso 4: el límite en el servicio.** En `SystemSettingsService.cs`, reemplazar el método `UpdateAsync` completo por estos dos métodos:

```csharp
    public async Task<Result> UpdateAsync(UpdateSystemSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, UpdateOperation);

        if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogFailed(logger, UpdateOperation, validationError.Code);
            return validationError;
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => UpdateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

        // Después del commit y solo si se confirmó: invalidar antes dejaría que una lectura concurrente vuelva a cachear el
        // modo viejo, y así la fábrica de HybridCache nunca ve un valor sin confirmar.
        if (result.IsSuccess)
        {
            await reader.InvalidateAsync(cancellationToken);
        }

        LogOutcome(logger, UpdateOperation, result);
        return result;
    }

    private async Task<Result> UpdateCoreAsync(UpdateSystemSettingsRequest request, CancellationToken cancellationToken)
    {
        var settings = await repository.GetAsync(cancellationToken);
        if (settings is null)
        {
            return SettingsErrors.NotFound;
        }

        settings.SetRegistrationMode(request.RegistrationMode);
        return Result.Success();
    }
```

- [ ] **Paso 5: verlo pasar.** El comando del Paso 3, el proyecto de arquitectura y `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Settings.SettingsControllerTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Settings.SystemSettingsReaderTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.RegistrationModeTests"`. Esperado: todo PASS.

- [ ] **Paso 6: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 7: commit.**

```bash
git add src/ArquitecturaBase.Application/Services/Settings/SystemSettingsService.cs tests
git commit -m "$(cat <<'EOF'
refactor(settings): SystemSettingsService con ExecuteInTransactionAsync

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

- [ ] **Paso 8: el test del lector, en rojo.** En `tests/ArquitecturaBase.Api.IntegrationTests/Settings/SystemSettingsReaderTests.cs`, agregar `using ArquitecturaBase.Infrastructure.Persistence;` (en orden con los demás) y este test después de `The_registration_mode_is_cached_until_it_is_invalidated`:

```csharp
    /// <summary>
    /// La fábrica del caché lee en su propio scope, con otra conexión: un cambio que quien lee todavía no confirmó no se
    /// cachea. Con la protección contra estampidas, la fábrica puede seguir sirviendo a otros pedidos después de que el
    /// que la arrancó terminó o se canceló. Sobre el contexto de ese pedido correría adentro de su transacción, con sus
    /// locks, y moriría con su scope.
    /// </summary>
    [Fact]
    public async Task The_cache_factory_reads_on_its_own_connection_and_never_caches_an_uncommitted_mode()
    {
        // Al salir del scope, la fila y el caché vuelven a como estaban; SetAsync ya dejó el caché vacío.
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<ISystemSettingsReader>();

        // Quien lee tiene una transacción abierta con un cambio sin confirmar, como un límite a mitad de camino.
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        var settings = await db.SystemSettings.SingleAsync(Ct);
        settings.SetRegistrationMode(RegistrationMode.Open);
        await db.SaveChangesAsync(Ct);

        var read = await reader.GetRegistrationModeAsync(Ct);
        await transaction.RollbackAsync(Ct);

        Assert.Equal(RegistrationMode.InviteOnly, read);
        Assert.Equal(RegistrationMode.InviteOnly, await ReadModeAsync());
    }
```

- [ ] **Paso 9: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-method "ArquitecturaBase.Api.IntegrationTests.Settings.SystemSettingsReaderTests.The_cache_factory_reads_on_its_own_connection_and_never_caches_an_uncommitted_mode"`
Esperado: FAIL (`Expected: InviteOnly, Actual: Open`). Hoy la fábrica lee con el contexto de quien llama, adentro de su transacción, y cachea el valor sin confirmar.

- [ ] **Paso 10: la fábrica con su propio scope.** Reemplazar `src/ArquitecturaBase.Infrastructure/Persistence/Readers/SystemSettingsReader.cs` completo por:

```csharp
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

/// <summary>
/// Mismo patrón que PermissionService: el valor se cachea en HybridCache y se descarta explícitamente cuando
/// cambia. Sin fila, devuelve InviteOnly, que es el modo cerrado: ante la duda, el sistema no se abre solo.
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
    /// La fábrica lee en un scope propio, con su contexto y su conexión, y nunca con los de quien llama. Se lee adentro
    /// de los límites del ingreso, de Google y del bot, y con la protección contra estampidas la fábrica puede seguir
    /// sirviendo a otros pedidos después de que el que la arrancó terminó o se canceló. Sobre el contexto de ese pedido
    /// correría adentro de su transacción, vería lo que todavía no confirmó y le ocuparía la conexión que su rollback
    /// necesita para soltar los locks.
    /// </summary>
    public async Task<RegistrationMode> GetRegistrationModeAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            CacheKey,
            scopeFactory,
            static async (scopes, token) =>
            {
                await using var scope = scopes.CreateAsyncScope();

                return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SystemSettings
                    .AsNoTracking()
                    .Select(settings => settings.RegistrationMode)
                    .FirstOrDefaultAsync(token);
            },
            CacheEntryOptions,
            cancellationToken: cancellationToken);

    public async Task InvalidateAsync(CancellationToken cancellationToken) =>
        await cache.RemoveAsync(CacheKey, cancellationToken);
}
```

`IServiceScopeFactory` es singleton y el lector sigue siendo scoped: la registración (`services.AddScoped<ISystemSettingsReader, SystemSettingsReader>()`) no cambia. En los tests de integración, el scope nuevo resuelve el `TestDbContext` que registra `ApiFactory`, igual que cualquier otro scope. `WhatsAppWebhookRetry` ya usa el mismo recurso.

- [ ] **Paso 11: verlo pasar.** El comando del Paso 9, y los que leen el modo adentro de un límite:

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Settings.SystemSettingsReaderTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Settings.SettingsControllerTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.RegistrationModeTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.InitialAdminSignInTests"`
Esperado: todo PASS.

- [ ] **Paso 12: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 13: commit.**

```bash
git add src/ArquitecturaBase.Infrastructure/Persistence/Readers/SystemSettingsReader.cs tests/ArquitecturaBase.Api.IntegrationTests/Settings/SystemSettingsReaderTests.cs
git commit -m "$(cat <<'EOF'
fix(settings): el lector del modo de registro lee con su propio scope

La fábrica de HybridCache corría sobre el contexto de quien llamaba, adentro
de su transacción: cacheaba lo que no estaba confirmado y, con la protección
contra estampidas, podía ocupar la conexión que un rollback necesita para
soltar los locks.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 8 (C5a): usuarios — alta, edición e invitación con `ExecuteInTransactionAsync`

Incluye la **mejora de atomicidad** de la etapa (la otra mejora explícita, la del lector del modo de registro, está en la Tarea 7): sin correo ni número, la edición no toma locks y hoy no tiene transacción, así que el nombre puede quedar cambiado aunque fallen los roles. Adentro del límite pasa a ser todo o nada.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/Users/UserWriteOperations.cs:16-66` y las firmas de `CreateCoreAsync` y `UpdateCoreAsync`
- Modificar: `src/ArquitecturaBase.Application/Services/Users/UserService.cs:109-184` (`CreateUserAsync`, `UpdateUserAsync`, `SendInvitationAsync`)
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UserRepositoryTransactionTests.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Users/UserServiceTestHost.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Users/UserServiceWriteTests.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Users/UserInvitationServiceTests.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` (trinquete)

- [ ] **Paso 1: el test de la mejora, en rojo.** En `UserRepositoryTransactionTests.cs`, agregar `using ArquitecturaBase.Application.Models.Identity;` y `using ArquitecturaBase.Domain.ValueObjects;`, este test después de `Failed_contact_unlink_rolls_back_autosaved_name_email_and_phone`:

```csharp
    /// <summary>
    /// Sin correo ni número la edición no toma ningún lock: antes de la Etapa 1 no había transacción y el nombre quedaba
    /// guardado aunque fallaran los roles. Adentro del límite del servicio, o queda todo o no queda nada.
    /// </summary>
    [Fact]
    public async Task Failed_role_change_without_contact_rolls_back_the_autosaved_name()
    {
        var email = TestEmails.Unique("repository-roles");
        var created = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserService>()
            .CreateUserAsync(new CreateUserRequest(email, "Antes", null), Ct));
        Assert.True(created.IsSuccess);

        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUser>();
            services.AddSingleton<ICurrentUser>(new FixedCurrentUser(Guid.CreateVersion7()));
            services.RemoveAll<IUserRepository>();
            services.AddScoped<IUserRepository>(provider => new ThrowingRolesUserRepository(
                ActivatorUtilities.CreateInstance<UserRepository>(provider),
                provider.GetRequiredService<ApplicationDbContext>(),
                created.Value));
        }));

        await Assert.ThrowsAsync<ExpectedWriteFailure>(() => InScopeAsync(api.Services, services =>
            services.GetRequiredService<IUserService>().UpdateUserAsync(
                new UpdateUserRequest(created.Value, "Después", [SystemRoles.User]), Ct)));

        Assert.Equal("Antes", await factory.ExecuteDbContextAsync(db => db.Users.AsNoTracking()
            .Where(user => user.Id == created.Value)
            .Select(user => user.DisplayName)
            .SingleAsync(Ct)));
    }
```

Y esta clase anidada, al final del archivo, junto a los demás dobles:

```csharp
    /// <summary>El repositorio real, salvo SetRolesAsync: comprueba que el nombre ya se autoguardó adentro de una transacción y falla.</summary>
    private sealed class ThrowingRolesUserRepository(IUserRepository inner, ApplicationDbContext db, Guid userId) : IUserRepository
    {
        public Task LockExternalSignInAsync(Email email, string provider, string providerKey, CancellationToken cancellationToken) =>
            inner.LockExternalSignInAsync(email, provider, providerKey, cancellationToken);

        public Task<UserAccount> CreateAsync(Email? email, PhoneNumber? phone, bool phoneConfirmed, string? displayName,
            string culture, CancellationToken cancellationToken) =>
            inner.CreateAsync(email, phone, phoneConfirmed, displayName, culture, cancellationToken);

        public Task<UserAccount> CreateUnverifiedAsync(Email? email, PhoneNumber? phone, string? displayName, string culture,
            CancellationToken cancellationToken) =>
            inner.CreateUnverifiedAsync(email, phone, displayName, culture, cancellationToken);

        public Task AddExternalLoginAsync(Guid id, ExternalLogin login, CancellationToken cancellationToken) =>
            inner.AddExternalLoginAsync(id, login, cancellationToken);

        public Task RestoreAsync(Guid id, string? displayName, CancellationToken cancellationToken) =>
            inner.RestoreAsync(id, displayName, cancellationToken);

        public Task SetEmailAsync(Guid id, Email email, bool confirmed, CancellationToken cancellationToken) =>
            inner.SetEmailAsync(id, email, confirmed, cancellationToken);

        public Task SetPhoneAsync(Guid id, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken) =>
            inner.SetPhoneAsync(id, phone, confirmed, cancellationToken);

        public Task RemovePhoneAsync(Guid id, CancellationToken cancellationToken) =>
            inner.RemovePhoneAsync(id, cancellationToken);

        public async Task SetRolesAsync(Guid id, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
        {
            Assert.Equal(userId, id);
            Assert.NotNull(db.Database.CurrentTransaction);
            Assert.Equal("Después", await db.Users.AsNoTracking()
                .Where(user => user.Id == id)
                .Select(user => user.DisplayName)
                .SingleAsync(cancellationToken));

            throw new ExpectedWriteFailure();
        }

        public Task SetDisplayNameAsync(Guid id, string? displayName, CancellationToken cancellationToken) =>
            inner.SetDisplayNameAsync(id, displayName, cancellationToken);

        public Task SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken) =>
            inner.SetActiveAsync(id, isActive, cancellationToken);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            inner.DeleteAsync(id, cancellationToken);

        public Task UpdateProfileAsync(Guid id, string? displayName, string culture, string timeZoneId,
            CancellationToken cancellationToken) =>
            inner.UpdateProfileAsync(id, displayName, culture, timeZoneId, cancellationToken);
    }
```

- [ ] **Paso 2: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-method "ArquitecturaBase.Api.IntegrationTests.Persistence.UserRepositoryTransactionTests.Failed_role_change_without_contact_rolls_back_the_autosaved_name"`
Esperado: FAIL: sale `NotNullException` (no hay transacción cuando se cambian los roles) en lugar de `ExpectedWriteFailure`. Es el hueco que la mejora cierra.

- [ ] **Paso 3: el arnés unitario.** En `UserServiceTestHost.cs`:
  - reemplazar `public FakeUnitOfWork UnitOfWork { get; } = new();` por:

    ```csharp
        public FakeUnitOfWork UnitOfWork { get; }

        /// <summary>Cuántos correos había en la cola cuando se confirmó la unidad de trabajo.</summary>
        public int? QueuedAtCommit { get; private set; }
    ```
  - como primeras líneas del constructor:

    ```csharp
            UnitOfWork = new FakeUnitOfWork { OnCommit = () => QueuedAtCommit = EmailQueue.Messages.Count };
            Destinations.InTransaction = () => UnitOfWork.InTransaction;
            Invitations.InTransaction = () => UnitOfWork.InTransaction;
    ```
  - en `new UserWriteOperations(...)`, borrar el último argumento (`UnitOfWork`).

- [ ] **Paso 4: las aserciones.** En `UserServiceWriteTests.cs`:

| Test | Antes | Después |
|---|---|---|
| `Invalid_create_stops_before_locks_identity_and_commit` | `Assert.Equal(0, host.UnitOfWork.SaveChangesCalls);` | `Assert.Equal(0, host.UnitOfWork.Transactions);` |
| `Create_assigns_default_role_and_confirms_once` | `Assert.Equal(1, host.UnitOfWork.SaveChangesCalls);` | `Assert.Equal(1, host.UnitOfWork.Commits);` + `Assert.Equal(CommitPolicy.OnSuccess, host.UnitOfWork.LastPolicy);` |
| `Create_locks_email_before_phone_and_rejects_unknown_roles_without_mutation` | primer `Assert.Equal(0, ...SaveChangesCalls);` | `Assert.Equal(0, host.UnitOfWork.Commits);` + `Assert.Equal(1, host.UnitOfWork.Rollbacks);` |
| (mismo test) | segundo `Assert.Equal(1, ...SaveChangesCalls);` | `Assert.Equal(1, host.UnitOfWork.Commits);` |
| `Create_restores_a_deleted_account_only_when_both_destinations_belong_to_it` | `== 1` | `Assert.Equal(1, host.UnitOfWork.Commits);` |
| `Create_rejects_destinations_from_two_deleted_accounts_without_commit` | `== 0` | `Assert.Equal(0, host.UnitOfWork.Commits);` + `Assert.Equal(1, host.UnitOfWork.Rollbacks);` |
| `Invalid_update_stops_before_locks_and_commit` | `== 0` | `Assert.Equal(0, host.UnitOfWork.Transactions);` |
| `Update_preserves_an_unchanged_phone_from_a_disallowed_country` | `== 1` | `Assert.Equal(1, host.UnitOfWork.Commits);` |
| `Update_rejects_new_phone_from_a_disallowed_country_before_mutation` | `== 0` | `Assert.Equal(0, host.UnitOfWork.Commits);` + `Assert.Equal(1, host.UnitOfWork.Rollbacks);` |
| `Update_cannot_remove_the_last_active_admin` | `== 0` | `Assert.Equal(0, host.UnitOfWork.Commits);` + `Assert.Equal(1, host.UnitOfWork.Rollbacks);` |
| `Update_replacing_phone_invalidates_pending_link_after_unlinking_contact` | `== 1` | `Assert.Equal(1, host.UnitOfWork.Commits);` |
| `Create_with_email_invitation_enqueues_it_before_the_commit` | `== 1` | `Assert.Equal(1, host.UnitOfWork.Commits);` + `Assert.Equal(1, host.QueuedAtCommit);` |
| `Create_rejects_an_existing_email_without_committing` | `== 0` | `Assert.Equal(0, host.UnitOfWork.Commits);` + `Assert.Equal(1, host.UnitOfWork.Rollbacks);` |
| `Update_missing_user_returns_not_found_without_committing` | `== 0` | `Assert.Equal(0, host.UnitOfWork.Commits);` + `Assert.Equal(1, host.UnitOfWork.Rollbacks);` |

Agregar `using ArquitecturaBase.Application.Interfaces.Persistence;` a `UserServiceWriteTests.cs` (por `CommitPolicy`). En `UserInvitationServiceTests.cs`:

| Test | Antes | Después |
|---|---|---|
| `Missing_channel_is_validated_before_taking_a_lock` | `Assert.Equal(0, fixture.UnitOfWork.SaveChangesCalls);` | `Assert.Equal(0, fixture.UnitOfWork.Transactions);` |
| `An_inactive_account_is_not_invited_or_saved` | `== 0` | `Assert.Equal(0, fixture.UnitOfWork.Commits);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |
| `A_successful_resend_locks_before_reading_and_saves_once` | `== 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` |
| `Cooldown_rejects_a_second_resend_without_saving_or_queueing` | `== 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |

Los eventos de locks (`["lock:id", "read:GetLatestSentAsync", "lock:id"]`) y el orden correo antes que teléfono no cambian.

- [ ] **Paso 5: el paso rojo del trinquete.** Sacar `UserWriteOperations` de `KnownUnitOfWorkReceivers`, y `UserWriteOperations` y `UserService` de `KnownSaveChangesCallers`.

- [ ] **Paso 6: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.Users.UserServiceWriteTests" --filter-class "ArquitecturaBase.Application.UnitTests.Services.Users.UserInvitationServiceTests"`
Esperado: FAIL de compilación (`UserWriteOperations` todavía pide `IUnitOfWork`).

- [ ] **Paso 7: `UserWriteOperations` sin `IUnitOfWork`.** Reemplazar desde el `<summary>` de la clase (línea 16) hasta el final de `UpdateAsync` (línea 66) por:

```csharp
/// <summary>
/// El alta y la edición administrativa sobre IUserRepository. No abre ni confirma transacciones: trabaja dentro del
/// límite de UserService (ExecuteInTransactionAsync con OnSuccess), que valida afuera con Validate*Async. Los locks
/// preceden a las escrituras de Identity, que autoguardan en esa misma transacción; un error de negocio la deshace entera.
/// </summary>
internal sealed class UserWriteOperations(
    IUserRepository userRepository,
    IUserReader users,
    IRoleReader roles,
    ILoginCodeRepository destinations,
    UserContactParser contacts,
    UserInvitationSender invitationSender,
    UserGuards guards,
    PhoneNumberChange phoneChange,
    WhatsAppContactLinker contactLinker,
    ServiceRequestValidator<CreateUserRequest> createValidator,
    ServiceRequestValidator<UpdateUserRequest> updateValidator)
{
    public Task<ValidationError?> ValidateCreateAsync(CreateUserRequest request, CancellationToken cancellationToken) =>
        createValidator.ValidateAsync(request, cancellationToken);

    public Task<ValidationError?> ValidateUpdateAsync(UpdateUserRequest request, CancellationToken cancellationToken) =>
        updateValidator.ValidateAsync(request, cancellationToken);
```

Después, cambiar la firma `private async Task<Result<Guid>> CreateCoreAsync(CreateUserRequest request, CancellationToken cancellationToken)` por:

```csharp
    /// <summary>El alta, ya validada. Corre dentro del límite de UserService.CreateUserAsync.</summary>
    public async Task<Result<Guid>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
```

y `private async Task<Result> UpdateCoreAsync(UpdateUserRequest request, CancellationToken cancellationToken)` por:

```csharp
    /// <summary>La edición, ya validada. Corre dentro del límite de UserService.UpdateUserAsync.</summary>
    public async Task<Result> UpdateAsync(UpdateUserRequest request, CancellationToken cancellationToken)
```

Los cuerpos no cambian.

- [ ] **Paso 8: `UserService` abre el límite.** En `UserService.cs`, reemplazar `CreateUserAsync`, `UpdateUserAsync` y `SendInvitationAsync` completos por:

```csharp
    public async Task<Result<Guid>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string operation = "CreateUser";
        LogHandling(logger, operation);

        if (await writes.ValidateCreateAsync(request, cancellationToken) is { } validationError)
        {
            LogFailed(logger, operation, validationError.Code);
            return validationError;
        }

        // Identity autoguarda la cuenta, sus roles y la restauración adentro: un error de negocio deshace todo. La
        // invitación se encola antes del commit, así la fila queda guardada ya con su estado.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => writes.CreateAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        LogOutcome(logger, operation, result);
        return result;
    }

    public async Task<Result> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string operation = "UpdateUser";
        LogHandling(logger, operation);

        if (await writes.ValidateUpdateAsync(request, cancellationToken) is { } validationError)
        {
            LogFailed(logger, operation, validationError.Code);
            return validationError;
        }

        // El nombre, los roles, el correo y el número se autoguardan por separado: adentro del límite quedan todos o
        // ninguno, también cuando no se toca el correo ni el número y no se toma ningún lock.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => writes.UpdateAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        LogOutcome(logger, operation, result);
        return result;
    }

    public async Task<Result> SendInvitationAsync(SendUserInvitationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string operation = "SendInvitation";
        LogHandling(logger, operation);

        if (await invitationValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogFailed(logger, operation, validationError.Code);
            return validationError;
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => SendInvitationCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        LogOutcome(logger, operation, result);
        return result;
    }

    private async Task<Result> SendInvitationCoreAsync(SendUserInvitationRequest request, CancellationToken cancellationToken)
    {
        // Dos reenvíos de la misma cuenta pasan de a uno y el segundo ve el guardado del primero.
        await invitations.LockAccountAsync(request.UserId, cancellationToken);

        var user = await userReader.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (!user.IsActive)
        {
            return UserInvitationErrors.UserInactive;
        }

        var channel = request.Channel!.Value;
        var allowed = invitationSender.Check(
            channel,
            request.Consent,
            user.DisplayName,
            user.Email is not null,
            user.PhoneNumber is not null,
            InvitationFields.OfResend);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var wait = (await invitations.GetLatestSentAsync(user.Id, cancellationToken))
            ?.WaitBeforeAnother(timeProvider.GetUtcNow().UtcDateTime) ?? TimeSpan.Zero;
        if (wait > TimeSpan.Zero)
        {
            return UserInvitationErrors.TooManyRequests((int)Math.Ceiling(wait.TotalSeconds));
        }

        // Toma otra vez el lock de invitaciones de la cuenta (es reentrante) y encola antes del commit.
        await invitationSender.SendAsync(user, channel, cancellationToken);
        return Result.Success();
    }
```

- [ ] **Paso 9: verlo pasar.** El comando del Paso 6, el del Paso 2, el proyecto de arquitectura y la suite de usuarios:

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.UserRepositoryTransactionTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.CreateUserEndpointTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.CreateUserWithPhoneTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UpdateUserEndpointTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UpdateUserContactTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UserInvitationEndpointsTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UserServiceWriteIntegrationTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.AccountsWithPhoneTests"`
Esperado: todo PASS. `Failed_invitation_enqueue_rolls_back_autosaved_user_and_initial_role` y `Failed_contact_unlink_rolls_back_autosaved_name_email_and_phone` pasan ahora por el rollback explícito de producción.

- [ ] **Paso 10: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 11: commit.**

```bash
git add src/ArquitecturaBase.Application/Services/Users tests
git commit -m "$(cat <<'EOF'
refactor(users): alta, edición e invitación con ExecuteInTransactionAsync

La edición sin correo ni número pasa a ser atómica: si fallan los roles, el
nombre ya no queda guardado.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 9 (C5b): usuarios — estado, borrado y desvincular con `ExecuteInTransactionAsync`

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/Users/UserStatusOperations.cs` (reemplazo completo)
- Modificar: `src/ArquitecturaBase.Application/Services/Users/UserPhoneOperations.cs` (reemplazo completo)
- Modificar: `src/ArquitecturaBase.Application/Services/Users/UserService.cs` (`SetUserActiveAsync`, `DeleteUserAsync`, `UnlinkUserPhoneAsync`)
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Support/FailingCommitUnitOfWork.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UserRepositoryTransactionTests.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Users/UserServiceTestHost.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Users/UserServiceStatusTests.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Users/UserServicePhoneTests.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` (trinquete)

- [ ] **Paso 1: el doble de integración compartido.** Crear `tests/ArquitecturaBase.Api.IntegrationTests/Support/FailingCommitUnitOfWork.cs`:

```csharp
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>La falla que simula el commit.</summary>
internal sealed class ExpectedCommitFailure : Exception;

/// <summary>Lo que un test quiere mirar de un commit que falla.</summary>
internal sealed class CommitFailureProbe
{
    /// <summary>Corre dentro de la transacción, con todo ya guardado, justo antes de fallar.</summary>
    public Func<ApplicationDbContext, CancellationToken, Task>? BeforeFailing { get; init; }

    /// <summary>Si al salir la transacción ya estaba deshecha y el tracker vacío: el rollback lo hizo producción.</summary>
    public bool RolledBackBeforeLeaving { get; set; }
}

/// <summary>
/// La unidad de trabajo de producción, pero el commit falla justo después de guardar los cambios. Lo que se prueba es el
/// rollback explícito de UnitOfWork, no el que hace el Dispose del scope: la excepción sale del trabajo, y la unidad real
/// deshace, vacía el tracker y la relanza sin traducir.
/// </summary>
internal sealed class FailingCommitUnitOfWork(UnitOfWork inner, ApplicationDbContext db, CommitFailureProbe probe) : IUnitOfWork
{
    public static void Replace(IServiceCollection services, CommitFailureProbe probe)
    {
        services.RemoveAll<IUnitOfWork>();
        services.AddScoped<IUnitOfWork>(provider => new FailingCommitUnitOfWork(
            ActivatorUtilities.CreateInstance<UnitOfWork>(provider), provider.GetRequiredService<ApplicationDbContext>(), probe));
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CommitPolicy policy, CancellationToken cancellationToken)
        where TResult : Result
    {
        try
        {
            return await inner.ExecuteInTransactionAsync(async ct =>
            {
                var result = await work(ct);

                if (policy.Commits(result))
                {
                    Assert.NotNull(db.Database.CurrentTransaction);
                    await db.SaveChangesAsync(ct);

                    if (probe.BeforeFailing is { } check)
                    {
                        await check(db, ct);
                    }

                    throw new ExpectedCommitFailure();
                }

                return result;
            }, policy, cancellationToken);
        }
        catch (ExpectedCommitFailure)
        {
            probe.RolledBackBeforeLeaving = db.Database.CurrentTransaction is null && !db.ChangeTracker.Entries().Any();
            throw;
        }
    }

    // En retiro (Etapa 1): lo borra la Tarea 21.
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The services that use this double already run inside ExecuteInTransactionAsync.");
}
```

`ExpectedCommitFailure` es `internal` a propósito (el diseño la pedía pública): pública, CA1032 exigiría los constructores estándar y CA1710 el sufijo `Exception`; los tests están en el mismo ensamblado y `Assert.ThrowsAsync<ExpectedCommitFailure>` la ve igual.

- [ ] **Paso 2: los dos tests de commit fallido usan la unidad real.** En `UserRepositoryTransactionTests.cs`:
  - en `Failed_delete_commit_rolls_back_soft_delete`, reemplazar la registración de `ThrowingCommitUnitOfWork` y la aserción por:

    ```csharp
            var probe = new CommitFailureProbe();
            await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICurrentUser>();
                services.AddSingleton<ICurrentUser>(new FixedCurrentUser(Guid.CreateVersion7()));
                FailingCommitUnitOfWork.Replace(services, probe);
            }));

            await Assert.ThrowsAsync<ExpectedCommitFailure>(() => InScopeAsync(api.Services, services =>
                services.GetRequiredService<IUserService>().DeleteUserAsync(created.Value, Ct)));

            Assert.True(probe.RolledBackBeforeLeaving);
    ```

    (la aserción final sobre `IsDeleted` no cambia);
  - en `Failed_unlink_commit_rolls_back_autosaved_phone_removal`, lo mismo sin el `ICurrentUser`:

    ```csharp
            var probe = new CommitFailureProbe();
            await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                FailingCommitUnitOfWork.Replace(services, probe)));

            await Assert.ThrowsAsync<ExpectedCommitFailure>(() => InScopeAsync(api.Services, services =>
                services.GetRequiredService<IUserService>().UnlinkUserPhoneAsync(created.Value, Ct)));

            Assert.True(probe.RolledBackBeforeLeaving);
    ```
  - borrar la clase anidada `ThrowingCommitUnitOfWork` y el `using ArquitecturaBase.Domain.Results;` que agregó la Tarea 3.

- [ ] **Paso 3: el arnés unitario.** En `UserServiceTestHost.cs`, agregar al principio del constructor (después de las líneas de la Tarea 8):

```csharp
        Links.InTransaction = () => UnitOfWork.InTransaction;
        MessagesLog.InTransaction = () => UnitOfWork.InTransaction;
```

y construir las operaciones sin `UnitOfWork`:

```csharp
        var status = new UserStatusOperations(
            Identity, Identity, new UserGuards(CurrentUser, Identity), Links, Identity);
        var userPhone = new UserPhoneOperations(
            Identity, Identity, new UserGuards(CurrentUser, Identity), linker, phoneChange, Identity);
```

- [ ] **Paso 4: las aserciones.** En `UserServiceStatusTests.cs` agregar `using ArquitecturaBase.Application.Interfaces.Persistence;` y:

| Test | Antes | Después |
|---|---|---|
| `Deactivate_locks_account_revokes_sessions_and_commits_once` | `Assert.Equal(1, host.UnitOfWork.SaveChangesCalls);` | `Assert.Equal(1, host.UnitOfWork.Commits);` + `Assert.Equal(CommitPolicy.OnSuccess, host.UnitOfWork.LastPolicy);` |
| `Activate_does_not_revoke_sessions_or_lock_links` | `== 1` | `Assert.Equal(1, host.UnitOfWork.Commits);` |
| `Deactivate_rejects_the_last_active_admin_without_mutation` | `== 0` | `Assert.Equal(0, host.UnitOfWork.Commits);` + `Assert.Equal(1, host.UnitOfWork.Rollbacks);` |
| `Deactivate_rejects_the_current_user_without_mutation` | `== 0` | `Assert.Equal(0, host.UnitOfWork.Commits);` + `Assert.Equal(1, host.UnitOfWork.Rollbacks);` |
| `Missing_user_is_not_found_and_does_not_commit` | `== 0` | `Assert.Equal(0, host.UnitOfWork.Commits);` + `Assert.Equal(1, host.UnitOfWork.Rollbacks);` |
| `Delete_locks_revokes_then_soft_deletes_and_commits_once` | `== 1` | `Assert.Equal(1, host.UnitOfWork.Commits);` |
| `Delete_rejects_last_admin_without_revoking_or_deleting` | `== 0` | `Assert.Equal(0, host.UnitOfWork.Commits);` + `Assert.Equal(1, host.UnitOfWork.Rollbacks);` |

En `UserServicePhoneTests.cs`:

| Test | Antes | Después |
|---|---|---|
| `Unlink_locks_contact_then_account_revokes_sessions_and_commits` | `Assert.Equal(1, host.UnitOfWork.SaveChangesCalls);` | `Assert.Equal(1, host.UnitOfWork.Commits);` |
| `Already_unlinked_account_commits_without_revoking_sessions` | `== 1` | `Assert.Equal(1, host.UnitOfWork.Commits);` |
| `Own_only_login_method_cannot_be_unlinked` | `== 0` | `Assert.Equal(0, host.UnitOfWork.Commits);` + `Assert.Equal(1, host.UnitOfWork.Rollbacks);` |

- [ ] **Paso 5: el paso rojo del trinquete.** Sacar `UserStatusOperations` y `UserPhoneOperations` de `KnownUnitOfWorkReceivers` y de `KnownSaveChangesCallers`.

- [ ] **Paso 6: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.Users.UserServiceStatusTests" --filter-class "ArquitecturaBase.Application.UnitTests.Services.Users.UserServicePhoneTests"`
Esperado: FAIL de compilación (las operaciones todavía piden `IUnitOfWork`).

- [ ] **Paso 7: las operaciones sin `IUnitOfWork`.** Reemplazar `UserStatusOperations.cs` completo por:

```csharp
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Cambia el estado de la cuenta y, al desactivarla o borrarla, corta todos sus medios de acceso ya emitidos. No abre ni
/// confirma transacciones: trabaja dentro del límite de UserService, y el lock de enlaces que toma dura lo que ese límite.
/// </summary>
internal sealed class UserStatusOperations(
    IUserReader users,
    IUserRepository repository,
    UserGuards guards,
    ILoginLinkRepository loginLinks,
    IIdentityService identity)
{
    public async Task<Result> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        if (!isActive)
        {
            // Pone en fila la revocación con la emisión y el canje de enlaces de la cuenta.
            await loginLinks.LockAccountAsync(userId, cancellationToken);
        }

        if (await users.FindByIdAsync(userId, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        if (!isActive)
        {
            var allowed = await guards.EnsureCanBeRemovedAsync(userId, cancellationToken);
            if (allowed.IsFailure)
            {
                return allowed.Error;
            }
        }

        await repository.SetActiveAsync(userId, isActive, cancellationToken);

        // IsActive no invalida por sí solo el access token, el refresh token ni la cookie.
        if (!isActive)
        {
            await identity.RevokeSessionsAsync(userId, cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        await loginLinks.LockAccountAsync(userId, cancellationToken);

        if (await users.FindByIdAsync(userId, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        var allowed = await guards.EnsureCanBeRemovedAsync(userId, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        // Antes del borrado: después, el filtro global ya no encuentra la cuenta para renovarle el stamp.
        await identity.RevokeSessionsAsync(userId, cancellationToken);
        await repository.DeleteAsync(userId, cancellationToken);
        return Result.Success();
    }
}
```

Y `UserPhoneOperations.cs` completo por:

```csharp
using ArquitecturaBase.Application.Services.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Desvinculación administrativa: bloquea contacto y cuenta antes de leer y modificar la cuenta. Trabaja dentro del
/// límite de UserService.
/// </summary>
internal sealed class UserPhoneOperations(
    IUserReader users,
    IUserRepository repository,
    UserGuards guards,
    WhatsAppContactLinker contactLinker,
    PhoneNumberChange phoneChange,
    IIdentityService identity)
{
    public async Task<Result> UnlinkAsync(Guid userId, CancellationToken cancellationToken)
    {
        // El bot toma contacto y después cuenta; el orden inverso puede producir un deadlock.
        await phoneChange.LockAsync(userId, newPhone: null, cancellationToken);

        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (user.PhoneNumber is null)
        {
            await contactLinker.UnlinkUserAsync(userId, cancellationToken);
            await phoneChange.VoidPendingLinksAsync(userId, cancellationToken);
            return Result.Success();
        }

        var allowed = await guards.EnsurePhoneCanBeUnlinkedAsync(user, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        await repository.RemovePhoneAsync(userId, cancellationToken);
        await contactLinker.UnlinkUserAsync(userId, cancellationToken);
        await identity.RevokeSessionsAsync(userId, cancellationToken);
        return Result.Success();
    }
}
```

- [ ] **Paso 8: `UserService` abre el límite.** Reemplazar `SetUserActiveAsync`, `DeleteUserAsync` y `UnlinkUserPhoneAsync` completos por:

```csharp
    public async Task<Result> SetUserActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        const string operation = "SetUserActive";
        LogHandling(logger, operation);

        // Desactivar autoguarda el estado y revoca todo el acceso ya emitido (UPDATE inmediatos de OpenIddict): o pasa
        // todo o no pasa nada. Activar es una sola escritura, ahora también dentro de una transacción explícita.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => status.SetActiveAsync(userId, isActive, ct), CommitPolicy.OnSuccess, cancellationToken);
        LogOutcome(logger, operation, result);
        return result;
    }

    public async Task<Result> DeleteUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string operation = "DeleteUser";
        LogHandling(logger, operation);
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => status.DeleteAsync(userId, ct), CommitPolicy.OnSuccess, cancellationToken);
        LogOutcome(logger, operation, result);
        return result;
    }

    public async Task<Result> UnlinkUserPhoneAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string operation = "UnlinkUserPhone";
        LogHandling(logger, operation);

        // Una cuenta que ya no tiene número también es un éxito: suelta un contacto que haya quedado y sus enlaces.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => phone.UnlinkAsync(userId, ct), CommitPolicy.OnSuccess, cancellationToken);
        LogOutcome(logger, operation, result);
        return result;
    }
```

- [ ] **Paso 9: verlo pasar.** El comando del Paso 6, el proyecto de arquitectura y:

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.UserRepositoryTransactionTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.DeactivateUserEndpointTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.DeleteUserEndpointTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UnlinkUserPhoneEndpointTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UserSoftDeleteTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.LoginLinkTests"`
Esperado: todo PASS. Los seis `Failed_*` de `UserRepositoryTransactionTests` en verde, contando el que agregó la Tarea 8 (`Failed_role_change_without_contact_rolls_back_the_autosaved_name`); dos de ellos afirman además `RolledBackBeforeLeaving`.

- [ ] **Paso 10: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 11: commit.**

```bash
git add src/ArquitecturaBase.Application/Services/Users tests
git commit -m "$(cat <<'EOF'
refactor(users): estado, borrado y desvincular con ExecuteInTransactionAsync

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 10 (C5c): revocar el acceso en dos pasos explícitos (`RevokeSessionsAsync`)

Hoy `IdentityService.RevokeSessionsAsync` invalida los enlaces pendientes sin guardarlos y depende de que `UpdateSecurityStampAsync` los guarde "de paso". Pasa a dos pasos explícitos: `AccountAccessRevoker` (Application) invalida los enlaces y llama a `RevokeSessionsAsync`, que queda en stamp y OpenIddict y exige la transacción.

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Services/Users/AccountAccessRevoker.cs`
- Crear: `tests/ArquitecturaBase.Application.UnitTests/Services/Users/AccountAccessRevokerTests.cs`
- Modificar: `src/ArquitecturaBase.Application/DependencyInjection.cs:36-45`
- Modificar: `src/ArquitecturaBase.Application/Services/Users/UserStatusOperations.cs`, `UserPhoneOperations.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs:16-27` y `:117-144`
- Modificar: `src/ArquitecturaBase.Application/Interfaces/Integrations/IIdentityService.cs:115-120`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/DependencyInjectionTests.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Users/{UserServiceTestHost,UserServiceStatusTests,UserServicePhoneTests}.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginLinkTests.cs`

- [ ] **Paso 1: los tests unitarios del revocador.** Crear `tests/ArquitecturaBase.Application.UnitTests/Services/Users/AccountAccessRevokerTests.cs`:

```csharp
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authentication;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

public sealed class AccountAccessRevokerTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryLoginLinkRepository _links = new();
    private readonly FakeIdentityService _identity = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task Invalidates_every_pending_link_of_the_account_including_expired_ones_and_revokes_the_sessions()
    {
        var userId = Guid.CreateVersion7();
        var expired = LoginLink.Issue(userId, "hash-expired", Now.AddHours(-1));
        var active = LoginLink.Issue(userId, "hash-active", Now.AddMinutes(-1));
        var consumed = LoginLink.Issue(userId, "hash-consumed", Now.AddMinutes(-2));
        Assert.True(consumed.Redeem(Now.AddMinutes(-1)).IsSuccess);
        var ofAnotherAccount = LoginLink.Issue(Guid.CreateVersion7(), "hash-other", Now.AddMinutes(-1));
        _links.Links.AddRange([expired, active, consumed, ofAnotherAccount]);

        await new AccountAccessRevoker(_links, _identity, _clock).RevokeAsync(userId, Ct);

        Assert.Equal(Now, expired.InvalidatedAtUtc);
        Assert.Equal(Now, active.InvalidatedAtUtc);
        Assert.Null(consumed.InvalidatedAtUtc);
        Assert.Null(ofAnotherAccount.InvalidatedAtUtc);
        Assert.Equal([userId], _identity.RevokedUsers);
    }
}
```

- [ ] **Paso 2: el registro.** En `DependencyInjectionTests.cs`, agregar:

```csharp
    [Fact]
    public void The_account_access_revoker_is_registered_as_scoped()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        var descriptor = Assert.Single(services, registration => registration.ServiceType == typeof(AccountAccessRevoker));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }
```

- [ ] **Paso 3: los servicios de usuarios invalidan los enlaces pendientes.** `FakeIdentityService.RevokeSessionsAsync` solo registra el Id, así que estos tests fallan hasta que la invalidación viva en Application. En `UserServiceStatusTests.cs` agregar `using ArquitecturaBase.Domain.Authentication;` y:

```csharp
    [Fact]
    public async Task Deactivate_invalidates_the_pending_links_even_expired_ones()
    {
        var host = new UserServiceTestHost();
        var user = host.Identity.AddUser("links@example.com");
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var expired = LoginLink.Issue(user.Id, "hash-expired", now.AddHours(-1));
        host.Links.Links.Add(expired);

        var result = await host.Service.SetUserActiveAsync(user.Id, isActive: false, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(now, expired.InvalidatedAtUtc);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Delete_invalidates_the_pending_links_before_deleting()
    {
        var host = new UserServiceTestHost();
        var user = host.Identity.AddUser("links-delete@example.com");
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var active = LoginLink.Issue(user.Id, "hash-active", now.AddMinutes(-1));
        host.Links.Links.Add(active);

        var result = await host.Service.DeleteUserAsync(user.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(now, active.InvalidatedAtUtc);
    }
```

En `UserServicePhoneTests.cs` agregar `using ArquitecturaBase.Domain.Authentication;` y:

```csharp
    [Fact]
    public async Task Unlinking_a_number_invalidates_the_pending_links_even_expired_ones()
    {
        var host = new UserServiceTestHost();
        var user = host.Identity.AddUser("links-phone@example.com", phoneNumber: "+5491112345678");
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var expired = LoginLink.Issue(user.Id, "hash-expired", now.AddHours(-1));
        host.Links.Links.Add(expired);

        var result = await host.Service.UnlinkUserPhoneAsync(user.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(now, expired.InvalidatedAtUtc);
    }
```

- [ ] **Paso 4: el test de integración corre el revocador dentro del límite.** En `LoginLinkTests.cs`, agregar `using ArquitecturaBase.Application.Interfaces.Persistence;`, `using ArquitecturaBase.Application.Services.Users;` y `using ArquitecturaBase.Domain.Results;`. En `Revoking_sessions_invalidates_an_expired_but_pending_link`, reemplazar el bloque que llama a `RevokeSessionsAsync` por:

```csharp
        await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            async ct =>
            {
                await services.GetRequiredService<AccountAccessRevoker>().RevokeAsync(account.Id, ct);

                return Result.Success();
            },
            CommitPolicy.OnSuccess,
            Ct));
```

Y agregar después de ese test:

```csharp
    /// <summary>
    /// El stamp y las dos revocaciones de OpenIddict van juntos: las revocaciones son UPDATE inmediatos, y sin la
    /// transacción del caso de uso se confirmarían sueltas.
    /// </summary>
    [Fact]
    public async Task Revoking_sessions_outside_a_transaction_throws()
    {
        var account = await CreateAccountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().RevokeSessionsAsync(account.Id, Ct);

            return true;
        }));
    }
```

- [ ] **Paso 5: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.Users.AccountAccessRevokerTests"`
Esperado: FAIL de compilación (`AccountAccessRevoker` no existe).

- [ ] **Paso 6: crear `src/ArquitecturaBase.Application/Services/Users/AccountAccessRevoker.cs`.**

```csharp
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Corta todo acceso ya emitido de una cuenta, en dos pasos explícitos dentro de la transacción del caso de uso:
/// 1) invalida los enlaces de ingreso pendientes, también los vencidos, porque uno que el bot mandó antes del corte no
///    puede volver a servir si la cuenta se reactiva dentro de sus 10 minutos, ni después de desvincular un número cuyo
///    chat puede ya no ser de esta persona;
/// 2) renueva el security stamp y revoca autorizaciones y tokens de OpenIddict.
/// Las revocaciones son UPDATE inmediatos: un rollback posterior también las deshace. Las invalidaciones las baja el
/// guardado final del límite; no dependen del guardado del stamp. Lo usan desactivar, borrar y el desvincular del
/// administrador. PhoneNumberChange.VoidPendingLinksAsync (solo los activos) es otra semántica y no se toca.
/// </summary>
internal sealed class AccountAccessRevoker(
    ILoginLinkRepository loginLinks, IIdentityService identity, TimeProvider timeProvider)
{
    public async Task RevokeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var link in await loginLinks.ListPendingAsync(userId, cancellationToken))
        {
            link.Invalidate(nowUtc);
        }

        await identity.RevokeSessionsAsync(userId, cancellationToken);
    }
}
```

- [ ] **Paso 7: registrarlo.** En `src/ArquitecturaBase.Application/DependencyInjection.cs`, después de `services.AddScoped<PhoneNumberChange>();`, agregar:

```csharp
        services.AddScoped<AccountAccessRevoker>();
```

- [ ] **Paso 8: las operaciones usan el revocador.**
  - `UserStatusOperations.cs`: cambiar el último parámetro del constructor `IIdentityService identity` por `AccountAccessRevoker accessRevoker`; cambiar las dos llamadas `await identity.RevokeSessionsAsync(userId, cancellationToken);` por `await accessRevoker.RevokeAsync(userId, cancellationToken);`; borrar `using ArquitecturaBase.Application.Interfaces.Integrations;`; y reemplazar el `<summary>` de la clase por:

    ```csharp
    /// <summary>
    /// Cambia el estado de la cuenta y, al desactivarla o borrarla, corta todo acceso ya emitido con
    /// <see cref="AccountAccessRevoker"/>. No abre ni confirma transacciones: trabaja dentro del límite de UserService, y el
    /// lock de enlaces que toma dura lo que ese límite.
    /// </summary>
    ```

    y el comentario de `SetActiveAsync` antes de la revocación por `// IsActive no invalida por sí solo el access token, el refresh token, la cookie ni los enlaces pendientes.`.
  - `UserPhoneOperations.cs`: el mismo cambio de parámetro y de llamada, borrar `using ArquitecturaBase.Application.Interfaces.Integrations;`, y el `<summary>` por:

    ```csharp
    /// <summary>
    /// Desvinculación administrativa: bloquea contacto y cuenta antes de leer y modificar la cuenta. Trabaja dentro del
    /// límite de UserService; con número, corta además todo acceso ya emitido con <see cref="AccountAccessRevoker"/>.
    /// </summary>
    ```
  - `UserServiceTestHost.cs`: construir el revocador y pasarlo:

    ```csharp
            var revoker = new AccountAccessRevoker(Links, Identity, Clock);
            var status = new UserStatusOperations(
                Identity, Identity, new UserGuards(CurrentUser, Identity), Links, revoker);
            var userPhone = new UserPhoneOperations(
                Identity, Identity, new UserGuards(CurrentUser, Identity), linker, phoneChange, revoker);
    ```

- [ ] **Paso 9: `RevokeSessionsAsync` queda en stamp y OpenIddict.** En `IdentityService.cs`:
  - agregar `using ArquitecturaBase.Infrastructure.Persistence;` y `using ArquitecturaBase.Infrastructure.Persistence.Extensions;`;
  - reemplazar el constructor primario por:

    ```csharp
    internal sealed class IdentityService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictTokenManager tokenManager,
        IUserReader userReader,
        IRoleReader roleReader,
        IUserRepository userRepository,
        ApplicationDbContext dbContext)
        : IIdentityService
    ```
  - reemplazar `RevokeSessionsAsync` completo por:

    ```csharp
        public async Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
        {
            // El stamp y las dos revocaciones tienen que ir juntos. Las revocaciones de OpenIddict son UPDATE inmediatos, y
            // sin la transacción del caso de uso se confirmarían sueltas. Los enlaces pendientes los invalida
            // AccountAccessRevoker, que es quien llama.
            dbContext.RequireTransaction();
            var user = await RequireUserAsync(userId, cancellationToken);

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
    ```

  El SQL sale en el mismo orden que hoy: el autoguardado del stamp igual baja de paso las invalidaciones que ya están seguidas, y ahora además las baja el guardado final del límite.

- [ ] **Paso 10: documentación de `IIdentityService.RevokeSessionsAsync`.** Reemplazar su `<summary>` por:

```csharp
    /// <summary>
    /// Renueva el security stamp, con lo que la cookie de Identity deja de valer, y revoca las autorizaciones y los
    /// tokens de OpenIddict de esa persona. Exige la transacción del caso de uso (IUnitOfWork.ExecuteInTransactionAsync):
    /// las revocaciones son UPDATE inmediatos, y sin ella se confirmarían sueltas. Los enlaces de ingreso pendientes los
    /// invalida <c>AccountAccessRevoker</c>, que es quien la llama.
    /// </summary>
```

- [ ] **Paso 11: verlo pasar.**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.Users.AccountAccessRevokerTests" --filter-class "ArquitecturaBase.Application.UnitTests.DependencyInjectionTests" --filter-class "ArquitecturaBase.Application.UnitTests.Services.Users.UserServiceStatusTests" --filter-class "ArquitecturaBase.Application.UnitTests.Services.Users.UserServicePhoneTests"`
Esperado: todo PASS.

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.LoginLinkTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.UserRepositoryTransactionTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.DeactivateUserEndpointTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.DeleteUserEndpointTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UnlinkUserPhoneEndpointTests"`
Esperado: todo PASS. `Failed_session_revocation_rolls_back_autosaved_deactivation` sigue en verde: ahora `ListPendingAsync` la llama `AccountAccessRevoker`, después del autoguardado de `SetActiveAsync`, como antes.

- [ ] **Paso 12: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 13: commit.**

```bash
git add src/ArquitecturaBase.Application src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs tests
git commit -m "$(cat <<'EOF'
refactor(users): revocar el acceso en dos pasos explícitos

AccountAccessRevoker invalida los enlaces pendientes y después llama a
RevokeSessionsAsync, que queda en el stamp y OpenIddict y exige la
transacción del caso de uso.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 11 (C6): `ProfileService` es dueño de sus transacciones

Los seis métodos que escriben. `OnAnyResult` en `ConfirmEmailAsync` y `ConfirmPhoneLinkAsync` (hoy guardan siempre después de validar); `OnSuccess` en el resto. Los `*Operations` exponen `Validate*Async` y pierden `IUnitOfWork`; `ProfileWhatsAppOperations` expone además `EnsureEnabled`, el guard de WhatsApp apagado, que el servicio llama afuera del límite, como `AccountService`. Las lecturas de la cuenta siguen después de los locks (`ConcurrencyStamp`).

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/Users/ProfileEmailOperations.cs` (reemplazo completo)
- Modificar: `src/ArquitecturaBase.Application/Services/Users/ProfileWhatsAppOperations.cs` (reemplazo completo)
- Modificar: `src/ArquitecturaBase.Application/Services/Users/ProfileService.cs:75-146`
- Modificar: `src/ArquitecturaBase.Application/Services/Users/DestinationCodeVerifier.cs:8-25` (documentación)
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Users/ProfileEmailServiceTests.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Users/ProfileServiceTests.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` (trinquete)

- [ ] **Paso 1: `ProfileEmailServiceTests` usa el doble compartido.**
  - agregar `using ArquitecturaBase.Application.UnitTests.TestDoubles;`;
  - borrar la clase anidada `RecordingUnitOfWork`;
  - reemplazar, dentro de `Fixture`, la propiedad `UnitOfWork`, el constructor y la construcción de las operaciones por:

    ```csharp
            public FakeUnitOfWork UnitOfWork { get; }
            public DateTime? SentAtCommit { get; private set; }
            public int FailedAttemptsAtCommit { get; private set; }
            public bool CodeConsumedAtCommit { get; private set; }

            public Fixture()
            {
                Queue = new RecordingEmailQueue(Events);
                UnitOfWork = new FakeUnitOfWork(Events)
                {
                    // La foto que antes sacaba RecordingUnitOfWork al guardar, ahora al confirmar.
                    OnCommit = () =>
                    {
                        var code = Codes.Codes.LastOrDefault();
                        SentAtCommit = code?.SentAtUtc;
                        FailedAttemptsAtCommit = code?.FailedAttempts ?? 0;
                        CodeConsumedAtCommit = code?.ConsumedAtUtc is not null;
                    },
                };
                Codes.InTransaction = () => UnitOfWork.InTransaction;
            }

            public ProfileService Service(Guid? userId, IUserRepository? repository = null)
            {
                var currentUser = new FakeCurrentUser { UserId = userId };
                var hasher = new FakeLoginCodeHasher();
                var operations = new ProfileEmailOperations(
                    currentUser, Identity, repository ?? Identity,
                    new LoginCodeIssuer(Codes, new FakeLoginCodeGenerator(), hasher, _options,
                        Options.Create(new WhatsAppLoginOptions()), Clock, NullLogger<LoginCodeIssuer>.Instance),
                    new DestinationCodeVerifier(Codes, hasher, Clock), Renderer, Queue, _options,
                    new ServiceRequestValidator<RequestEmailCodeRequest>([new RequestEmailCodeRequestValidator()]),
                    new ServiceRequestValidator<ConfirmEmailRequest>([new ConfirmEmailRequestValidator(_options)]));

                return new ProfileService(currentUser, Identity, Identity, new FakePermissionService(),
                    new InMemoryLoginAuditRepository(), new FakePhoneNumberParser(),
                    new ServiceRequestValidator<UpdateProfileRequest>([new UpdateProfileRequestValidator()]),
                    operations, null!, UnitOfWork, Logger);
            }
    ```
  - cambiar las aserciones:

| Test | Antes | Después |
|---|---|---|
| `Request_normalizes_email_and_enqueues_in_account_language_before_saving` | `Assert.Equal(["enqueue", "save"], fixture.Events);` y `Assert.Equal(stored.SentAtUtc, fixture.UnitOfWork.SentAtSave);` | `Assert.Equal(["enqueue", "commit"], fixture.Events);` y `Assert.Equal(stored.SentAtUtc, fixture.SentAtCommit);` |
| `Request_shares_the_destination_cooldown_with_sign_in_codes` | primer `Assert.Equal(0, fixture.UnitOfWork.SaveCalls);` | `Assert.Equal(0, fixture.UnitOfWork.Commits);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |
| (mismo test) | segundo `Assert.Equal(1, fixture.UnitOfWork.SaveCalls);` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` |
| `Invalid_request_is_rejected_before_issuing_or_saving` | `Assert.Equal(0, fixture.UnitOfWork.SaveCalls);` | `Assert.Equal(0, fixture.UnitOfWork.Transactions);` |
| `Wrong_confirmation_code_saves_the_failed_attempt_without_affecting_session` | `Assert.Equal(1, fixture.UnitOfWork.SaveCalls);` y `Assert.Equal(1, fixture.UnitOfWork.FailedAttemptsAtSave);` | `Assert.Equal(1, fixture.UnitOfWork.Commits);`, `Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);` y `Assert.Equal(1, fixture.FailedAttemptsAtCommit);` |
| `Occupied_email_is_revealed_only_after_a_correct_code_and_the_code_is_saved_as_spent` | `Assert.Equal(2, fixture.UnitOfWork.SaveCalls);` y `Assert.True(fixture.UnitOfWork.CodeConsumedAtSave);` | `Assert.Equal(2, fixture.UnitOfWork.Commits);`, `Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);` y `Assert.True(fixture.CodeConsumedAtCommit);` |
| `Correct_code_sets_verified_email_without_revoking_the_current_session` | `Assert.True(fixture.UnitOfWork.CodeConsumedAtSave);` | `Assert.True(fixture.CodeConsumedAtCommit);` |
| `Unique_index_race_returns_conflict_and_still_saves_the_consumed_code` | `Assert.True(fixture.UnitOfWork.CodeConsumedAtSave);` y `Assert.Equal(1, fixture.UnitOfWork.SaveCalls);` | `Assert.True(fixture.CodeConsumedAtCommit);`, `Assert.Equal(1, fixture.UnitOfWork.Commits);` y `Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);` |
| `Invalid_confirmation_does_not_lock_or_save_but_missing_user_after_validation_does_save` | `Assert.Equal(1, fixture.UnitOfWork.SaveCalls);` | `Assert.Equal(1, fixture.UnitOfWork.Transactions);`, `Assert.Equal(1, fixture.UnitOfWork.Commits);` y `Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);` |

`RejectingEmailRepository` no cambia: sigue probando que el correo choca y el código igual queda gastado.

- [ ] **Paso 2: `ProfileServiceTests`.** En `EmailOperations(...)`, borrar el último argumento `_unitOfWork` de `new ProfileEmailOperations(...)`. Cambiar las aserciones:

| Test | Antes | Después |
|---|---|---|
| `Update_changes_the_name_culture_and_time_zone_and_confirms_the_unit_of_work` | `Assert.Equal(1, _unitOfWork.SaveChangesCalls);` | `Assert.Equal(1, _unitOfWork.Commits);` |
| `Invalid_update_is_rejected_before_the_user_is_changed` | `Assert.Equal(0, _unitOfWork.SaveChangesCalls);` | `Assert.Equal(0, _unitOfWork.Transactions);` |
| `Display_name_longer_than_the_limit_is_rejected_before_writing` | `== 0` | `Assert.Equal(0, _unitOfWork.Transactions);` |
| `Unknown_user_cannot_update_a_profile` | `== 0` | `Assert.Equal(0, _unitOfWork.Commits);` + `Assert.Equal(1, _unitOfWork.Rollbacks);` |
| `Anonymous_request_cannot_update_a_profile` | `== 0` | `Assert.Equal(0, _unitOfWork.Commits);` + `Assert.Equal(1, _unitOfWork.Rollbacks);` |

  Después, el guard de WhatsApp apagado queda fijado afuera del límite, como en `AccountService`. Hoy no tiene test en el perfil: la ruta no existe con WhatsApp apagado. Cambiar el helper `Service` para que acepte las operaciones de WhatsApp:

  ```csharp
      private ProfileService Service(Guid? userId, ProfileWhatsAppOperations? whatsAppOperations = null) =>
          new(new FakeCurrentUser { UserId = userId }, _identity, _identity, _permissions, _loginAudits,
              new FakePhoneNumberParser(),
              new ServiceRequestValidator<UpdateProfileRequest>([new UpdateProfileRequestValidator()]),
              EmailOperations(userId),
              whatsAppOperations!,
              _unitOfWork, _logger);
  ```

  y agregar este test después de `Anonymous_request_cannot_update_a_profile`:

  ```csharp
      [Fact]
      public async Task Disabled_whatsapp_is_a_programming_error_after_request_validation_without_opening_a_transaction()
      {
          var user = _identity.AddUser("ana@example.com");

          // Antes del guard solo corre el validador del pedido: el resto de las dependencias no se toca.
          var whatsAppOperations = new ProfileWhatsAppOperations(
              new FakeCurrentUser { UserId = user.Id }, _identity, _identity, null!, null!, new FakePhoneNumberParser(),
              new FakeWhatsAppAvailability(IsEnabled: false), null!, Options.Create(new WhatsAppLoginOptions()),
              Options.Create(new LoginCodeOptions()), null!, null!, null!,
              new ServiceRequestValidator<RequestPhoneLinkCodeRequest>([new RequestPhoneLinkCodeRequestValidator()]),
              null!);

          await Assert.ThrowsAsync<InvalidOperationException>(() => Service(user.Id, whatsAppOperations)
              .RequestPhoneLinkCodeAsync(new RequestPhoneLinkCodeRequest("AR", "+5493515550101"), Ct));

          Assert.Equal(0, _unitOfWork.Transactions);
      }
  ```

  Los usings que necesita ya están en el archivo: `Configuration.Auth`, `Common.Validation`, `Services.Users`, `Models.Users`, `Validation.Users`, `TestDoubles.Auth` y `Microsoft.Extensions.Options`. El constructor que usa es el de 15 parámetros del Paso 6. Con el guard adentro del límite, `Transactions` daría 1: el test fija la regla 1 del §3.

- [ ] **Paso 3: el paso rojo del trinquete.** Sacar `ProfileEmailOperations` y `ProfileWhatsAppOperations` de `KnownUnitOfWorkReceivers` (queda vacía), y `ProfileService`, `ProfileEmailOperations` y `ProfileWhatsAppOperations` de `KnownSaveChangesCallers`.

- [ ] **Paso 4: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.Users.ProfileEmailServiceTests" --filter-class "ArquitecturaBase.Application.UnitTests.Services.Users.ProfileServiceTests"`
Esperado: FAIL de compilación (`ProfileEmailOperations` todavía pide `IUnitOfWork`).

- [ ] **Paso 5: `ProfileEmailOperations` sin `IUnitOfWork`.** Reemplazar el archivo completo por:

```csharp
using ArquitecturaBase.Application.Configuration.Auth;
using System.Globalization;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Los dos casos de correo del perfil. No abren ni confirman transacciones: trabajan dentro del límite de ProfileService,
/// que valida afuera con Validate*Async y elige la política de cada uno. No cambian la sesión actual.
/// </summary>
internal sealed class ProfileEmailOperations(
    ICurrentUser currentUser,
    IUserReader users,
    IUserRepository userRepository,
    LoginCodeIssuer issuer,
    DestinationCodeVerifier verifier,
    IEmailTemplateRenderer templateRenderer,
    IEmailQueue emailQueue,
    IOptions<LoginCodeOptions> options,
    ServiceRequestValidator<RequestEmailCodeRequest> requestValidator,
    ServiceRequestValidator<ConfirmEmailRequest> confirmValidator)
{
    public Task<ValidationError?> ValidateRequestAsync(RequestEmailCodeRequest request, CancellationToken cancellationToken) =>
        requestValidator.ValidateAsync(request, cancellationToken);

    /// <summary>El pedido de código, ya validado. Corre dentro del límite de ProfileService, con OnSuccess.</summary>
    public async Task<Result<RequestEmailCodeResponse>> RequestCodeAsync(
        RequestEmailCodeRequest request, CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await users.FindByIdAsync(userId, cancellationToken)
            : null;
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;
        var issued = await issuer.IssueVerificationCodeAsync(LoginCodeDestination.ForEmail(email), user.Id, cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        // Se encola antes del commit para que el código se confirme ya marcado como enviado (con la cola llena,
        // EmailQueue descarta el correo y el código igual queda marcado).
        var settings = options.Value;
        await emailQueue.EnqueueAsync(
            templateRenderer.RenderEmailVerificationCode(
                email.Value, issued.Value.Code, settings.LifetimeMinutes, CultureInfo.GetCultureInfo(UserCultures.Of(user))),
            cancellationToken);
        issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);

        return new RequestEmailCodeResponse(settings.ResendCooldownSeconds);
    }

    public Task<ValidationError?> ValidateConfirmAsync(ConfirmEmailRequest request, CancellationToken cancellationToken) =>
        confirmValidator.ValidateAsync(request, cancellationToken);

    /// <summary>
    /// La confirmación, ya validada. Corre dentro del límite de ProfileService con OnAnyResult: la verificación puede
    /// contar un intento o gastar el código aunque la cuenta no exista, el correo esté ocupado o la escritura choque con
    /// el índice único.
    /// </summary>
    public async Task<Result> ConfirmAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await users.FindByIdAsync(userId, cancellationToken)
            : null;
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;
        var verification = await verifier.VerifyAsync(
            LoginCodeDestination.ForEmail(email), user.Id, request.Code!, cancellationToken);
        if (verification.IsFailure)
        {
            return verification.Error;
        }

        // El índice único conserva también los correos de cuentas borradas.
        var owner = await users.FindByEmailAsync(email, cancellationToken);
        if (owner is null ? await users.IsDeletedEmailAsync(email, cancellationToken) : owner.Id != user.Id)
        {
            return UserErrors.AlreadyExists;
        }

        try
        {
            await userRepository.SetEmailAsync(user.Id, email, confirmed: true, cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // El savepoint deshizo solo ese guardado: el código sigue gastado y se confirma igual.
            return UserErrors.AlreadyExists;
        }

        return Result.Success();
    }
}
```

- [ ] **Paso 6: `ProfileWhatsAppOperations` sin `IUnitOfWork`.** Reemplazar el archivo completo por:

```csharp
using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Vinculación y desvinculación del WhatsApp propio; conserva locks, cuotas y consumo de códigos. No abre ni confirma
/// transacciones: trabaja dentro del límite de ProfileService, que valida afuera con Validate*Async.
/// </summary>
internal sealed class ProfileWhatsAppOperations(
    ICurrentUser currentUser,
    IUserReader users,
    IUserRepository userRepository,
    LoginCodeIssuer issuer,
    DestinationCodeVerifier verifier,
    IPhoneNumberParser phoneNumbers,
    IWhatsAppAvailability whatsApp,
    IWhatsAppOutbox outbox,
    IOptions<WhatsAppLoginOptions> whatsAppOptions,
    IOptions<LoginCodeOptions> codeOptions,
    WhatsAppContactLinker contactLinker,
    PhoneNumberChange phoneChange,
    UserGuards guards,
    ServiceRequestValidator<RequestPhoneLinkCodeRequest> requestValidator,
    ServiceRequestValidator<ConfirmPhoneLinkRequest> confirmValidator)
{
    public Task<ValidationError?> ValidateRequestAsync(
        RequestPhoneLinkCodeRequest request, CancellationToken cancellationToken) =>
        requestValidator.ValidateAsync(request, cancellationToken);

    /// <summary>
    /// La acción no existe con WhatsApp apagado. Este guard impide emitir un código sin entrega si se invoca directamente
    /// el servicio desde otro consumidor. ProfileService lo llama después de validar y antes de abrir el límite, como
    /// AccountService: un error de configuración no abre transacción.
    /// </summary>
    public void EnsureEnabled()
    {
        if (!whatsApp.IsEnabled)
        {
            throw new InvalidOperationException("WhatsApp is disabled (no WhatsApp:PhoneNumberId): no code to link a number can be requested.");
        }
    }

    /// <summary>
    /// El pedido de código, ya validado y con WhatsApp prendido (<see cref="EnsureEnabled"/>). Corre dentro del límite de
    /// ProfileService, con OnSuccess.
    /// </summary>
    public async Task<Result<RequestPhoneLinkCodeResponse>> RequestCodeAsync(
        RequestPhoneLinkCodeRequest request, CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await users.FindByIdAsync(userId, cancellationToken)
            : null;
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var phoneResult = phoneNumbers.Parse(request.Country, request.Number);
        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var phone = phoneResult.Value;
        if (!whatsAppOptions.Value.AllowsCountry(phoneNumbers.RegionOf(phone)))
        {
            return WhatsAppErrors.CountryNotSupported;
        }

        // El emisor comparte límites por destino y cuota diaria con los códigos de ingreso.
        var issued = await issuer.IssueVerificationCodeAsync(
            LoginCodeDestination.ForPhone(phone), user.Id, cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        // Si la cola no lo toma, queda sin fecha de envío y no consume la cuota. Se marca antes del commit.
        if (outbox.TryEnqueue(new WhatsAppLoginCodeMessage(phone, UserCultures.Of(user), issued.Value.Code)))
        {
            issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
        }

        return new RequestPhoneLinkCodeResponse(
            codeOptions.Value.ResendCooldownSeconds, phone.Value, phoneNumbers.Mask(phone));
    }

    public Task<ValidationError?> ValidateConfirmAsync(
        ConfirmPhoneLinkRequest request, CancellationToken cancellationToken) =>
        confirmValidator.ValidateAsync(request, cancellationToken);

    /// <summary>
    /// La confirmación, ya validada. Corre dentro del límite de ProfileService con OnAnyResult: la verificación puede
    /// contar un intento o gastar el código aun cuando el número está ocupado o la escritura choca con el índice único.
    /// </summary>
    public async Task<Result> ConfirmAsync(ConfirmPhoneLinkRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return UserErrors.NotFound;
        }

        var phoneResult = PhoneNumber.Create(request.Phone);
        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var phone = phoneResult.Value;
        var verification = await verifier.VerifyAsync(
            LoginCodeDestination.ForPhone(phone), userId, request.Code!, cancellationToken);
        if (verification.IsFailure)
        {
            return verification.Error;
        }

        // Mismo orden que el bot: contacto, enlaces y recién entonces lectura/escritura de la cuenta.
        await phoneChange.LockAsync(userId, phone, cancellationToken);
        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var owner = await users.FindByPhoneAsync(phone, cancellationToken);
        if (owner is null ? await users.IsDeletedPhoneAsync(phone, cancellationToken) : owner.Id != user.Id)
        {
            return UserErrors.PhoneAlreadyExists;
        }

        try
        {
            await userRepository.SetPhoneAsync(user.Id, phone, confirmed: true, cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // El savepoint deshizo solo ese guardado: el código sigue gastado y se confirma igual.
            return UserErrors.PhoneAlreadyExists;
        }

        if (user.PhoneNumber is { } previous && previous != phone.Value)
        {
            await phoneChange.VoidPendingLinksAsync(user.Id, cancellationToken);
        }

        await contactLinker.LinkNumberAsync(phone, user.Id, cancellationToken);
        return Result.Success();
    }

    /// <summary>Desvincular el número propio. Corre dentro del límite de ProfileService, con OnSuccess.</summary>
    public async Task<Result> UnlinkAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return UserErrors.NotFound;
        }

        await phoneChange.LockAsync(userId, newPhone: null, cancellationToken);
        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        // La persona conserva la sesión actual; solo un administrador revoca sesiones al quitar un número.
        if (user.PhoneNumber is not null)
        {
            if (!await guards.HasOtherLoginMethodAsync(user, cancellationToken))
            {
                return UserErrors.LastLoginMethod;
            }

            await userRepository.RemovePhoneAsync(user.Id, cancellationToken);
        }

        await contactLinker.UnlinkUserAsync(user.Id, cancellationToken);
        await phoneChange.VoidPendingLinksAsync(user.Id, cancellationToken);
        return Result.Success();
    }
}
```

- [ ] **Paso 7: `ProfileService` abre los seis límites.** Reemplazar en `ProfileService.cs` los métodos `UpdateAsync`, `RequestEmailCodeAsync`, `ConfirmEmailAsync`, `RequestPhoneLinkCodeAsync`, `ConfirmPhoneLinkAsync` y `UnlinkOwnPhoneAsync` completos por:

```csharp
    public async Task<Result> UpdateAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, UpdateRequestName);

        if (await updateValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogOutcome(UpdateRequestName, validationError);
            return validationError;
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => UpdateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        LogOutcome(UpdateRequestName, result);
        return result;
    }

    public async Task<Result<RequestEmailCodeResponse>> RequestEmailCodeAsync(
        RequestEmailCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, RequestEmailCodeName);

        if (await emailOperations.ValidateRequestAsync(request, cancellationToken) is { } validationError)
        {
            LogOutcome(RequestEmailCodeName, validationError);
            return validationError;
        }

        // Un límite o un correo inválido no dejan nada; el correo se encola adentro, antes del commit.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => emailOperations.RequestCodeAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        LogOutcome(RequestEmailCodeName, result);
        return result;
    }

    public async Task<Result> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, ConfirmEmailName);

        if (await emailOperations.ValidateConfirmAsync(request, cancellationToken) is { } validationError)
        {
            LogOutcome(ConfirmEmailName, validationError);
            return validationError;
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => emailOperations.ConfirmAsync(request, ct),
            // Un código equivocado cuenta el intento, y uno correcto queda gastado aunque el correo sea de otra cuenta.
            CommitPolicy.OnAnyResult,
            cancellationToken);
        LogOutcome(ConfirmEmailName, result);
        return result;
    }

    public async Task<Result<RequestPhoneLinkCodeResponse>> RequestPhoneLinkCodeAsync(
        RequestPhoneLinkCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, RequestPhoneLinkCodeName);

        if (await whatsAppOperations.ValidateRequestAsync(request, cancellationToken) is { } validationError)
        {
            LogOutcome(RequestPhoneLinkCodeName, validationError);
            return validationError;
        }

        // Afuera y antes del límite, como en AccountService: con WhatsApp apagado es un error de programación, y un error
        // de configuración no abre transacción.
        whatsAppOperations.EnsureEnabled();

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => whatsAppOperations.RequestCodeAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        LogOutcome(RequestPhoneLinkCodeName, result);
        return result;
    }

    public async Task<Result> ConfirmPhoneLinkAsync(ConfirmPhoneLinkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, ConfirmPhoneLinkName);

        if (await whatsAppOperations.ValidateConfirmAsync(request, cancellationToken) is { } validationError)
        {
            LogOutcome(ConfirmPhoneLinkName, validationError);
            return validationError;
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => whatsAppOperations.ConfirmAsync(request, ct),
            // Un código equivocado cuenta el intento, y uno correcto queda gastado aunque el número sea de otra cuenta o
            // la cuenta ya no exista.
            CommitPolicy.OnAnyResult,
            cancellationToken);
        LogOutcome(ConfirmPhoneLinkName, result);
        return result;
    }

    public async Task<Result> UnlinkOwnPhoneAsync(CancellationToken cancellationToken)
    {
        LogHandling(logger, UnlinkOwnPhoneName);
        // Sin validador: todo va adentro. A propósito no revoca sesiones (solo un administrador las corta).
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => whatsAppOperations.UnlinkAsync(ct), CommitPolicy.OnSuccess, cancellationToken);
        LogOutcome(UnlinkOwnPhoneName, result);
        return result;
    }

    private async Task<Result> UpdateCoreAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId
            || await users.FindByIdAsync(userId, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        // UserManager autoguarda dentro de la transacción: si algo falla después, no queda nada.
        await userRepository.UpdateProfileAsync(
            userId, request.DisplayName, request.Culture!, request.TimeZoneId!, cancellationToken);
        return Result.Success();
    }
```

- [ ] **Paso 8: documentación de `DestinationCodeVerifier`.** Hoy la última oración del `<summary>` de la clase está partida entre dos líneas `///` ("Quien llama guarda también cuando falla" / "aunque la verificación falle, así los intentos quedan contados."). Reemplazar el `<summary>` de la clase completo por:

```csharp
/// <summary>
/// Verifica el código con que una cuenta prueba, desde el perfil, que un número o un correo es suyo (sección 12 del spec
/// del ingreso con WhatsApp). Lo comparten vincular el número y agregar el correo. Un código equivocado, vencido o usado
/// responde los mismos errores que el ingreso, y cada intento fallido se descuenta del código. No suma a los fallos de la
/// cuenta ni se audita: la persona ya está adentro, y esto no es un ingreso. Quien llama corre dentro de un límite con
/// CommitPolicy.OnAnyResult, así los intentos quedan contados aunque la verificación falle.
/// </summary>
```

y el `<summary>` de `VerifyAsync` completo por:

```csharp
    /// <summary>
    /// Verifica el último código que pidió <paramref name="userId"/> para <paramref name="destination"/> y, si es el
    /// correcto, lo gasta. Un código de ingreso, o uno que pidió otra cuenta, recibe la misma respuesta que la falta de
    /// código y no gasta intentos. Toma el lock del destino, que dura lo que la transacción del caso de uso: con él, dos
    /// cuentas que confirman el mismo destino a la vez pasan de a una, y la segunda ya ve lo que guardó la primera.
    /// </summary>
```

- [ ] **Paso 9: verlo pasar.**

Run: el comando del Paso 4, el proyecto de arquitectura y la suite del perfil, que incluye los tests que frenan al bot (`HeldContactRepository`, `HeldLoginLinkRepository`), el deadlock, el `NOWAIT` y el 204/409 en paralelo:

`dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.MeProfileEndpointTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.MeEmailEndpointsTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.MeWhatsAppEndpointsTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppBotTests"`
Esperado: todo PASS. Si aparece una falla intermitente en `MeWhatsAppEndpointsTests`, investigarla antes de tocar el diseño: el rollback explícito suelta los locks antes que hoy.

- [ ] **Paso 10: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 11: commit.**

```bash
git add src/ArquitecturaBase.Application/Services/Users tests
git commit -m "$(cat <<'EOF'
refactor(profile): ProfileService es dueño de sus transacciones

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 12 (C7a): `AccountService` con `ExecuteInTransactionAsync`

Pedido de código por correo y por WhatsApp con `OnSuccess`, verify con `OnAnyResult`. El encolado sigue adentro, antes del commit; la cookie del verify también.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/Auth/AccountService.cs:49-195`
- Modificar: `src/ArquitecturaBase.Infrastructure/Emails/EmailQueue.cs:8-12` (documentación)
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Auth/RequestLoginCodeServiceTests.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Auth/RequestWhatsAppLoginCodeServiceTests.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Auth/VerifyLoginCodeServiceTests.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` (trinquete)

- [ ] **Paso 1: `RequestLoginCodeServiceTests` usa el doble compartido.**
  - agregar `using ArquitecturaBase.Application.UnitTests.TestDoubles;`; borrar la clase anidada `RecordingUnitOfWork`;
  - en `Fixture`, reemplazar `UnitOfWork = new RecordingUnitOfWork(Events, Codes);` por:

    ```csharp
                UnitOfWork = new FakeUnitOfWork(Events) { OnCommit = () => SentAtCommit = Codes.Codes.LastOrDefault()?.SentAtUtc };
                Codes.InTransaction = () => UnitOfWork.InTransaction;
    ```

    y la propiedad `public RecordingUnitOfWork UnitOfWork { get; }` por:

    ```csharp
            public FakeUnitOfWork UnitOfWork { get; }

            public DateTime? SentAtCommit { get; private set; }
    ```
  - cambiar las aserciones:

| Test | Antes | Después |
|---|---|---|
| `Success_normalizes_email_enqueues_before_saving_and_marks_code_sent` | `Assert.Equal(code.SentAtUtc, fixture.UnitOfWork.SentAtSave);` y `Assert.Equal(["enqueue", "save"], fixture.Events);` | `Assert.Equal(code.SentAtUtc, fixture.SentAtCommit);`, `Assert.Equal(["enqueue", "commit"], fixture.Events);` y `Assert.Equal(CommitPolicy.OnSuccess, fixture.UnitOfWork.LastPolicy);` |
| `Invite_only_saves_an_unsent_code_for_an_unknown_email_with_the_same_response` | `Assert.Equal(2, fixture.UnitOfWork.SaveCalls);` y `Assert.Equal(["save", "enqueue", "save"], fixture.Events);` | `Assert.Equal(2, fixture.UnitOfWork.Commits);` y `Assert.Equal(["commit", "enqueue", "commit"], fixture.Events);` |
| `Invite_only_sends_the_code_to_the_initial_admin_without_an_account` | `SaveCalls == 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` |
| `Invalid_email_fails_validation_without_issuing_or_saving` | `SaveCalls == 0` | `Assert.Equal(0, fixture.UnitOfWork.Transactions);` |
| `Domain_email_rejection_returns_an_error_without_saving` | `SaveCalls == 0` | `Assert.Equal(0, fixture.UnitOfWork.Commits);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |
| `Resend_limit_returns_retry_after_without_saving_again` | `SaveCalls == 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |
| `New_sign_in_code_invalidates_previous_sign_in_code` | `SaveCalls == 2` | `Assert.Equal(2, fixture.UnitOfWork.Commits);` |
| `Sixth_request_in_window_returns_time_until_oldest_request_expires` | `SaveCalls == 5` | `Assert.Equal(5, fixture.UnitOfWork.Commits);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |
| `Recent_verification_code_holds_back_sign_in_resend_for_same_email` | `SaveCalls == 0` | `Assert.Equal(0, fixture.UnitOfWork.Commits);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |
| `Verification_codes_count_toward_sign_in_request_limit` | `SaveCalls == 0` | `Assert.Equal(0, fixture.UnitOfWork.Commits);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |
| `New_sign_in_code_keeps_verification_code_active` | `SaveCalls == 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` |
| `Queue_failure_does_not_mark_sent_or_save` | `SaveCalls == 0` | `Assert.Equal(0, fixture.UnitOfWork.Commits);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` (los eventos quedan en `["enqueue"]`) |
| `Save_failure_propagates_after_enqueuing_without_a_success_log` | `fixture.UnitOfWork.Failure = new InvalidOperationException("Save failed.");` y `Assert.Equal(["enqueue", "save"], fixture.Events);` | `fixture.UnitOfWork.CommitFailure = new InvalidOperationException("Save failed.");` y `Assert.Equal(["enqueue", "commit"], fixture.Events);` |

- [ ] **Paso 2: `RequestWhatsAppLoginCodeServiceTests`, igual.** Agregar el using, borrar `RecordingUnitOfWork`, y en `Fixture`:

```csharp
            UnitOfWork = new FakeUnitOfWork(Events) { OnCommit = () => SentAtCommit = Codes.Codes.LastOrDefault()?.SentAtUtc };
            Codes.InTransaction = () => UnitOfWork.InTransaction;
```

```csharp
        public FakeUnitOfWork UnitOfWork { get; }

        public DateTime? SentAtCommit { get; private set; }
```

| Test | Antes | Después |
|---|---|---|
| `Success_uses_normalized_destination_and_enqueues_before_saving` | `Assert.Equal(["enqueue", "save"], fixture.Events);` y `Assert.Equal(code.SentAtUtc, fixture.UnitOfWork.SentAtSave);` | `Assert.Equal(["enqueue", "commit"], fixture.Events);`, `Assert.Equal(code.SentAtUtc, fixture.SentAtCommit);` y `Assert.Equal(CommitPolicy.OnSuccess, fixture.UnitOfWork.LastPolicy);` |
| `Invalid_request_returns_localized_field_errors_before_using_parser_or_saving` | `SaveCalls == 0` | `Assert.Equal(0, fixture.UnitOfWork.Transactions);` |
| `Number_longer_than_32_characters_fails_validation_before_issuing` | `SaveCalls == 0` | `Assert.Equal(0, fixture.UnitOfWork.Transactions);` |
| `International_number_does_not_need_a_selected_country` | `SaveCalls == 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` |
| `Invalid_mobile_returns_phone_error_without_issuing_or_saving` | `SaveCalls == 0` | `Assert.Equal(0, fixture.UnitOfWork.Commits);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |
| `Country_is_checked_from_the_parsed_phone_rather_than_the_selected_country` | `SaveCalls == 0` | `Assert.Equal(0, fixture.UnitOfWork.Commits);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |
| `Configured_country_is_accepted` | `SaveCalls == 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` |
| `Disabled_whatsapp_is_a_programming_error_after_request_validation` | `SaveCalls == 0` | `Assert.Equal(0, fixture.UnitOfWork.Transactions);` (el guard sigue afuera, después del validador) |
| `Invite_only_saves_an_unsent_code_for_unknown_number_and_sends_for_existing_account` | `SaveCalls == 2` y `["save", "enqueue", "save"]` | `Assert.Equal(2, fixture.UnitOfWork.Commits);` y `Assert.Equal(["commit", "enqueue", "commit"], fixture.Events);` |
| `Invite_only_treats_deleted_account_number_as_unknown` | `SaveCalls == 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` |
| `Declined_outbox_keeps_code_unsent_but_saves_the_successful_request` | `["enqueue", "save"]` | `Assert.Equal(["enqueue", "commit"], fixture.Events);` |
| `Cooldown_is_shared_with_a_recent_verification_code_for_the_same_number` | `SaveCalls == 0` | `Assert.Equal(0, fixture.UnitOfWork.Commits);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |
| `New_sign_in_code_invalidates_the_previous_sign_in_code` | `SaveCalls == 2` | `Assert.Equal(2, fixture.UnitOfWork.Commits);` |
| `Daily_quota_is_a_moving_window_of_24_hours` | `SaveCalls == 2` | `Assert.Equal(2, fixture.UnitOfWork.Commits);` |
| `Daily_quota_ignores_unsent_whatsapp_and_sent_email_codes` | `SaveCalls == 2` | `Assert.Equal(2, fixture.UnitOfWork.Commits);` |
| `Daily_quota_counts_sent_verification_codes_too` | `SaveCalls == 0` | `Assert.Equal(0, fixture.UnitOfWork.Commits);` + `Assert.Equal(1, fixture.UnitOfWork.Rollbacks);` |
| `Save_failure_propagates_after_enqueue_without_a_success_log` | `fixture.UnitOfWork.Failure = ...` y `["enqueue", "save"]` | `fixture.UnitOfWork.CommitFailure = new InvalidOperationException("Save failed.");` y `Assert.Equal(["enqueue", "commit"], fixture.Events);` |

- [ ] **Paso 3: `VerifyLoginCodeServiceTests`, igual.** Agregar el using, borrar `RecordingUnitOfWork`, y en `Fixture` reemplazar `UnitOfWork = new RecordingUnitOfWork(this);` por:

```csharp
            UnitOfWork = new FakeUnitOfWork
            {
                OnCommit = () =>
                {
                    AuditsAtCommit = Audits.Audits.Count;
                    CodeConsumedAtCommit = Codes.Codes.Any(code => code.ConsumedAtUtc is not null);
                    SignedInAtCommit = Identity.SignedInUsers.Count;
                },
            };
            Codes.InTransaction = () => UnitOfWork.InTransaction;
```

y la propiedad `public RecordingUnitOfWork UnitOfWork { get; }` por:

```csharp
        public FakeUnitOfWork UnitOfWork { get; }

        public int AuditsAtCommit { get; private set; }

        public bool CodeConsumedAtCommit { get; private set; }

        public int SignedInAtCommit { get; private set; }
```

En todo el archivo: `fixture.UnitOfWork.AuditsAtSave` → `fixture.AuditsAtCommit`, `fixture.UnitOfWork.CodeConsumedAtSave` → `fixture.CodeConsumedAtCommit`, `fixture.UnitOfWork.SignedInAtSave` → `fixture.SignedInAtCommit`. Y además:

| Test | Antes | Después |
|---|---|---|
| `Invalid_request_neither_takes_a_lock_nor_saves` | `Assert.Equal(0, fixture.UnitOfWork.SaveCalls);` | `Assert.Equal(0, fixture.UnitOfWork.Transactions);` |
| `Valid_email_code_creates_the_account_signs_in_and_saves_the_code_and_audit` | `SaveCalls == 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` + `Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);` (y `SignedInAtCommit == 1`: la cookie sigue antes del commit) |
| `Wrong_code_counts_the_attempt_and_saves_failure_audit` | `SaveCalls == 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` + `Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);` |
| `Locked_out_account_is_audited_without_touching_the_code` | `SaveCalls == 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` + `Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);` |
| `Invite_only_consumes_a_valid_code_and_saves_the_rejection` | `SaveCalls == 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` + `Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);` |
| `Whatsapp_code_creates_a_phone_only_account_and_saves_its_audit` | `SaveCalls == 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` |
| `Valid_code_confirms_an_existing_email_and_resets_failed_attempts` | `SaveCalls == 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` |
| `Valid_whatsapp_code_confirms_a_number_loaded_by_an_administrator` | `SaveCalls == 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` |
| `Deleted_account_is_rejected_after_consuming_the_code_in_open_registration` | `SaveCalls == 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` + `Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);` |
| `Invalid_phone_format_keeps_the_legacy_save_even_without_an_audit` → renombrar a `Invalid_phone_format_commits_an_empty_unit_of_work_without_an_audit` | `SaveCalls == 1` | `Assert.Equal(1, fixture.UnitOfWork.Commits);` + `Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);` |
| `Save_failure_does_not_log_success` | `fixture.UnitOfWork.Failure = new InvalidOperationException("save failed");` y `SaveCalls == 1` | `fixture.UnitOfWork.CommitFailure = new InvalidOperationException("save failed");` y `Assert.Equal(1, fixture.UnitOfWork.Commits);` |

- [ ] **Paso 4: el paso rojo del trinquete.** Sacar `AccountService` de `KnownSaveChangesCallers`.

- [ ] **Paso 5: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.Auth.RequestLoginCodeServiceTests" --filter-class "ArquitecturaBase.Application.UnitTests.Services.Auth.RequestWhatsAppLoginCodeServiceTests" --filter-class "ArquitecturaBase.Application.UnitTests.Services.Auth.VerifyLoginCodeServiceTests"`
Esperado: FAIL (los locks lanzan fuera de la transacción y `Commits` queda en 0: el servicio todavía llama a `SaveChangesAsync`).

- [ ] **Paso 6: `AccountService` abre los tres límites.** Reemplazar `RequestLoginCodeAsync`, `RequestWhatsAppLoginCodeAsync` y `VerifyLoginCodeAsync` completos por:

```csharp
    public async Task<Result<RequestLoginCodeResponse>> RequestLoginCodeAsync(
        RequestLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogRequestLoginCodeHandling(logger);

        if (await requestLoginCodeValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogRequestLoginCodeFailed(logger, validationError.Code);
            return validationError;
        }

        // Un correo inválido o un límite no dejan nada. El correo se encola adentro, antes del commit, para que la fila se
        // confirme ya marcada como enviada; si el commit falla, el correo sale igual, con un código que no sirve.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => IssueEmailCodeAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

        if (result.IsSuccess)
        {
            LogRequestLoginCodeHandled(logger);
        }
        else
        {
            LogRequestLoginCodeFailed(logger, result.Error.Code);
        }

        return result;
    }

    public async Task<Result<RequestWhatsAppLoginCodeResponse>> RequestWhatsAppLoginCodeAsync(
        RequestWhatsAppLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogRequestWhatsAppLoginCodeHandling(logger);

        if (await requestWhatsAppLoginCodeValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogRequestWhatsAppLoginCodeFailed(logger, validationError.Code);
            return validationError;
        }

        // La ruta HTTP se omite cuando WhatsApp está apagado. Llegar hasta aquí es un error de programación.
        if (!whatsApp.IsEnabled)
        {
            throw new InvalidOperationException("WhatsApp is disabled (no WhatsApp:PhoneNumberId): no WhatsApp sign-in code can be requested.");
        }

        // Que la cola no tome el mensaje no es un fallo: el código se confirma sin fecha de envío.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => IssueWhatsAppCodeAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

        if (result.IsSuccess)
        {
            LogRequestWhatsAppLoginCodeHandled(logger);
        }
        else
        {
            LogRequestWhatsAppLoginCodeFailed(logger, result.Error.Code);
        }

        return result;
    }

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

        if (result.IsSuccess)
        {
            LogVerifyLoginCodeHandled(logger);
        }
        else
        {
            LogVerifyLoginCodeFailed(logger, result.Error.Code);
        }

        return result;
    }

    // El pedido por correo, ya validado: corre dentro del límite de RequestLoginCodeAsync.
    private async Task<Result<RequestLoginCodeResponse>> IssueEmailCodeAsync(
        RequestLoginCodeRequest request, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;
        var issued = await issuer.IssueSignInCodeAsync(LoginCodeDestination.ForEmail(email), cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        var settings = loginCodeOptions.Value;
        var user = await identityService.FindByEmailAsync(email, cancellationToken);

        // La fila también se guarda para un correo desconocido en InviteOnly: los límites no pueden revelar
        // si existe la cuenta. El administrador inicial puede recibir el email antes de crear su cuenta.
        if (user is not null || await accountCreation.AllowsNewAccountAsync(email, cancellationToken))
        {
            var culture = user is null ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(user.Culture);

            // Se encola antes del commit para que la fila se confirme ya marcada como enviada. Encolar no espera al
            // SMTP, así que no alarga el lock del destino.
            await emailQueue.EnqueueAsync(
                templateRenderer.RenderLoginCode(email.Value, issued.Value.Code, settings.LifetimeMinutes, culture),
                cancellationToken);
            issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
        }

        return new RequestLoginCodeResponse(settings.ResendCooldownSeconds);
    }

    // El pedido por WhatsApp, ya validado y con WhatsApp prendido: corre dentro del límite de RequestWhatsAppLoginCodeAsync.
    private async Task<Result<RequestWhatsAppLoginCodeResponse>> IssueWhatsAppCodeAsync(
        RequestWhatsAppLoginCodeRequest request, CancellationToken cancellationToken)
    {
        var phoneResult = phoneNumbers.Parse(request.Country, request.Number);
        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var phone = phoneResult.Value;
        if (!whatsAppOptions.Value.AllowsCountry(phoneNumbers.RegionOf(phone)))
        {
            return WhatsAppErrors.CountryNotSupported;
        }

        var issued = await issuer.IssueSignInCodeAsync(LoginCodeDestination.ForPhone(phone), cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        var user = await identityService.FindByPhoneAsync(phone, cancellationToken);

        // También se guarda la fila de un número desconocido en InviteOnly: sostiene los mismos límites por destino.
        if (user is not null || await accountCreation.AllowsNewAccountAsync(email: null, cancellationToken))
        {
            var message = new WhatsAppLoginCodeMessage(phone, UserCultures.Of(user), issued.Value.Code);

            // La cola puede rechazar el mensaje. En ese caso se guarda el código como no enviado.
            if (outbox.TryEnqueue(message))
            {
                issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
            }
        }

        return new RequestWhatsAppLoginCodeResponse(
            loginCodeOptions.Value.ResendCooldownSeconds,
            phone.Value,
            phoneNumbers.Mask(phone));
    }
```

- [ ] **Paso 7: documentación de `EmailQueue`.** Reemplazar el `<summary>` de la clase por:

```csharp
/// <summary>
/// Canal acotado entre los casos de uso y EmailBackgroundService. Encolar nunca espera: el pedido de código encola dentro
/// de la transacción del caso de uso (IUnitOfWork.ExecuteInTransactionAsync), antes del commit y con el lock del destino
/// tomado, y con el SMTP caído o lento dejaría tomadas sus conexiones. Si el commit falla, el email sale igual, con un
/// código que no sirve. Con la cola llena, el email se descarta (sección 8: si el email no llega, el usuario puede pedir
/// otro código).
/// </summary>
```

- [ ] **Paso 8: verlo pasar.** El comando del Paso 5, el proyecto de arquitectura y la suite de ingreso:

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.LoginCodeConcurrencyTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.LoginCodeEndpointsTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.LoginSecurityTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.RegistrationModeTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.WhatsAppLoginCodeTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.InitialAdminSignInTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.PhoneAccountTokensTests"`
Esperado: todo PASS. `LoginCodeConcurrencyTests` exige que el commit ocurra al final del trabajo (8 códigos errados: 5 intentos y 8 auditorías; 10 pedidos: 1 código).

- [ ] **Paso 9: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 10: commit.**

```bash
git add src/ArquitecturaBase.Application/Services/Auth/AccountService.cs src/ArquitecturaBase.Infrastructure/Emails/EmailQueue.cs tests
git commit -m "$(cat <<'EOF'
refactor(auth): AccountService con ExecuteInTransactionAsync

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 13 (C7b): canje de enlace con `ExecuteInTransactionAsync`

`LoginLinkService.RedeemAsync` con `OnAnyResult` (hoy guarda siempre después de validar). `LoginLinkTestService` (solo tests) con `OnSuccess`: tiene que migrar antes de la Tarea 20, o toda `LoginLinkTests` lanzaría cuando los locks exijan la transacción.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/Auth/LoginLinkService.cs:65-93` y el comentario del `SignInAsync` de `RedeemCoreAsync`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/LoginLinks/LoginLinkTestService.cs` (reemplazo completo)
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` (trinquete)

- [ ] **Paso 1: el paso rojo del trinquete.** Sacar `LoginLinkService` de `KnownSaveChangesCallers`.

Run: `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj -- --filter-method "ArquitecturaBase.ArchitectureTests.TransactionBoundaryTests.Unit_of_work_SaveChangesAsync_is_only_called_by_pending_services"`
Esperado: FAIL con `New violations: ArquitecturaBase.Application.Services.Auth.LoginLinkService`.

- [ ] **Paso 2: el canje abre su límite.** En `LoginLinkService.cs`, reemplazar `RedeemAsync` completo por:

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

        if (result.IsSuccess)
        {
            LogRedeemHandled(logger);
        }
        else
        {
            LogRedeemFailed(logger, result.Error.Code);
        }

        return result;
    }
```

Y en `RedeemCoreAsync`, justo antes de `await identityService.SignInAsync(user.Id, cancellationToken);`, agregar:

```csharp
        // La cookie se escribe adentro, antes del commit, como hasta ahora: si el commit falla, UseExceptionHandler limpia
        // la respuesta y el Set-Cookie no sale. Google la escribe después; se alinean en la Etapa 2.
```

- [ ] **Paso 3: el emisor de prueba abre su límite.** Reemplazar `tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/LoginLinks/LoginLinkTestService.cs` completo por:

```csharp
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.LoginLinks;

public sealed record IssueLoginLinkRequest(Guid UserId);

public sealed record IssueLoginLinkResponse(string Url, DateTime ExpiresAtUtc);

public interface ILoginLinkTestService
{
    Task<Result<IssueLoginLinkResponse>> IssueAsync(IssueLoginLinkRequest request, CancellationToken cancellationToken);
}

/// <summary>Emite el enlace con el mismo emisor del bot, en su propio límite: un TooManyRequests no deja nada.</summary>
internal sealed class LoginLinkTestService(LoginLinkIssuer issuer, IUnitOfWork unitOfWork) : ILoginLinkTestService
{
    public async Task<Result<IssueLoginLinkResponse>> IssueAsync(
        IssueLoginLinkRequest request,
        CancellationToken cancellationToken)
    {
        var issued = await unitOfWork.ExecuteInTransactionAsync(
            ct => issuer.IssueAsync(request.UserId, ct), CommitPolicy.OnSuccess, cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        return new IssueLoginLinkResponse(issued.Value.Url, issued.Value.ExpiresAtUtc);
    }
}
```

- [ ] **Paso 4: verlo pasar.** El comando del Paso 1, y:

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.LoginLinkTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppBotTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.MeWhatsAppEndpointsTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UnlinkUserPhoneEndpointTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UpdateUserContactTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Contracts.WhatsAppRouteContractsTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Contracts.AuthConnectAccountContractTests"`
Esperado: todo PASS, incluidos los 5 canjes en paralelo (1 sesión, 1 auditoría de éxito) y `Redeeming_an_active_link_of_a_deleted_account_uses_it_up_without_an_audit` (Tarea 1). Con un token inventado se confirma una transacción vacía, como antes un guardado sin cambios. Son todas las clases que canjean por `POST /account/login-link/redeem`: el perfil y la administración canjean enlaces reales del bot, y las dos de contratos canjean tokens que no existen (una de ellas, con WhatsApp apagado).

- [ ] **Paso 5: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 6: commit.**

```bash
git add src/ArquitecturaBase.Application/Services/Auth/LoginLinkService.cs tests
git commit -m "$(cat <<'EOF'
refactor(auth): canje de enlace con ExecuteInTransactionAsync

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 14 (C7c): ingreso con Google con `ExecuteInTransactionAsync`

`OnAnyResult`; la cookie de la aplicación sigue saliendo después del commit y solo con éxito. Se corrigen dos comentarios que describían mal quién toma el lock del destino.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/Auth/ExternalLoginService.cs:198-227`
- Modificar: `src/ArquitecturaBase.Application/Interfaces/Integrations/IIdentityService.cs:42-50` (documentación de `CreateUnverifiedAsync`)
- Modificar: `src/ArquitecturaBase.Application/Interfaces/Persistence/IUserRepository.cs:12-15` (documentación de `LockExternalSignInAsync`)
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/UserRepository.cs:55-56` (comentario de `CreateUnverifiedAsync`)
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/Auth/ExternalLoginServiceTests.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs` (`LockExternalSignInAsync`, hoy :102-104)
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/ExternalLoginTests.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` (trinquete)

- [ ] **Paso 1: los tests unitarios.** Primero, el lock de Google en el doble también exige el límite, como los demás dobles de lock desde la Tarea 3. Hoy `FakeIdentityService.LockExternalSignInAsync` devuelve `Task.CompletedTask` y no controla nada. En `FakeIdentityService.cs`, reemplazarlo por:

    ```csharp
        /// <summary>Si no es null, tomar el lock fuera de la transacción lanza (ver <see cref="TransactionGuard"/>).</summary>
        public Func<bool>? InTransaction { get; set; }

        public Task LockExternalSignInAsync(
            Email email, string provider, string providerKey, CancellationToken cancellationToken)
        {
            TransactionGuard.Require(InTransaction);

            return Task.CompletedTask;
        }
    ```

  `TransactionGuard` está en `ArquitecturaBase.Application.UnitTests.TestDoubles`, el namespace padre de `TestDoubles.Auth`: no hace falta `using`. Los demás tests que usan `FakeIdentityService` no cambian, porque con `InTransaction` en null no se controla nada. Después, en `ExternalLoginServiceTests.cs`:
  - borrar la clase anidada `ThrowingUnitOfWork` y el `using ArquitecturaBase.Domain.Results;` que agregó la Tarea 3 (la clase `ExpectedCommitFailure` privada se queda);
  - reemplazar el helper `Service` por este, que ata el guard a la unidad de trabajo que corre el caso de uso. Así vale también para `failing`, que no es `_unitOfWork`:

    ```csharp
        private ExternalLoginService Service(FakeUnitOfWork? unitOfWork = null)
        {
            var uow = unitOfWork ?? _unitOfWork;
            _identity.InTransaction = () => uow.InTransaction;

            return new(
                _identity,
                _identity,
                _identity,
                _audits,
                new AccountCreationPolicy(_settings, new FakeInitialAdmin()),
                new FakeRequestInfo(),
                _time,
                new ServiceRequestValidator<ExternalSignInRequest>([new ExternalSignInRequestValidator()]),
                uow,
                NullLogger<ExternalLoginService>.Instance);
        }
    ```

    (`using ArquitecturaBase.Application.Interfaces.Persistence;` sigue en uso por `CommitPolicy`.)
  - reemplazar el cuerpo de `Failed_commit_does_not_issue_the_application_cookie` por:

    ```csharp
            _identity.PendingExternalLogin = GoogleLogin();
            var failing = new FakeUnitOfWork { CommitFailure = new ExpectedCommitFailure() };

            await Assert.ThrowsAsync<ExpectedCommitFailure>(() =>
                Service(failing).SignInAsync(new ExternalSignInRequest(ReturnUrl), Ct));

            Assert.True(_identity.ExternalSignedOut);
            Assert.Empty(_identity.SignedInUsers);
            Assert.Equal(1, failing.Rollbacks);
    ```
  - cambiar las aserciones:

| Test | Antes | Después |
|---|---|---|
| `Linked_account_signs_in_and_persists_a_successful_audit` | `Assert.Equal(1, _unitOfWork.SaveChangesCalls);` | `Assert.Equal(1, _unitOfWork.Commits);` |
| `New_account_is_created_and_linked_through_the_repository` | `== 1` | `Assert.Equal(1, _unitOfWork.Commits);` |
| `Unverified_google_email_fails_and_persists_the_audit` | `== 1` | `Assert.Equal(1, _unitOfWork.Commits);` + `Assert.Equal(CommitPolicy.OnAnyResult, _unitOfWork.LastPolicy);` |
| `Missing_external_cookie_fails_and_persists_the_audit` | `== 1` | `Assert.Equal(1, _unitOfWork.Commits);` + `Assert.Equal(CommitPolicy.OnAnyResult, _unitOfWork.LastPolicy);` |
| `Invite_only_rejects_unknown_account_and_persists_the_audit` | `== 1` | `Assert.Equal(1, _unitOfWork.Commits);` + `Assert.Equal(CommitPolicy.OnAnyResult, _unitOfWork.LastPolicy);` |
| `Disabled_or_locked_account_cannot_sign_in` | `Assert.Equal(2, _unitOfWork.SaveChangesCalls);` | `Assert.Equal(2, _unitOfWork.Commits);` |
| `Invalid_return_url_does_not_read_cookie_audit_or_save` | `== 0` | `Assert.Equal(0, _unitOfWork.Transactions);` |

- [ ] **Paso 2: el test de integración del commit fallido usa la unidad real.** En `ExternalLoginTests.cs`, reemplazar `Failed_google_commit_rolls_back_autosaved_account_role_link_and_audit` completo por:

```csharp
    [Fact]
    public async Task Failed_google_commit_rolls_back_autosaved_account_role_link_and_audit()
    {
        var email = TestEmails.Unique("google-rollback");
        var providerKey = "google-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        Guid? createdUserId = null;
        var probe = new CommitFailureProbe
        {
            // Adentro de la transacción, con todo ya guardado: la cuenta, su rol, el vínculo y la auditoría existen.
            BeforeFailing = async (db, ct) =>
            {
                var user = await db.Users.AsNoTracking().SingleAsync(user => user.Email == email, ct);
                createdUserId = user.Id;
                Assert.True(await db.UserRoles.AnyAsync(role => role.UserId == user.Id, ct));
                Assert.True(await db.UserLogins.AnyAsync(login => login.LoginProvider == "Google"
                    && login.ProviderKey == providerKey, ct));
                Assert.True(await db.LoginAudits.AnyAsync(audit => audit.Identifier == email && audit.Succeeded, ct));
            },
        };
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            FailingCommitUnitOfWork.Replace(services, probe)));
        await using (var scope = api.Services.CreateAsyncScope())
        {
            Assert.Equal(factory.ConnectionString,
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString());
        }

        using var client = api.CreateClient();
        using var external = await client.PostJsonAsync(
            "/test/external-login", new { providerKey, email, name = "Ana Pérez", emailVerified = true });
        Assert.True(external.IsSuccessStatusCode);

        using var callback = await client.SendAsync(
            HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl));

        // Un assert que falle adentro también termina en un 500: RolledBackBeforeLeaving distingue los dos casos.
        Assert.Equal(HttpStatusCode.InternalServerError, callback.StatusCode);
        Assert.True(probe.RolledBackBeforeLeaving);
        var userId = Assert.IsType<Guid>(createdUserId);
        var persisted = await factory.ExecuteDbContextAsync(async db =>
            (User: await db.Users.IgnoreQueryFilters().AnyAsync(user => user.Id == userId, Ct),
             Role: await db.UserRoles.AnyAsync(role => role.UserId == userId, Ct),
             Link: await db.UserLogins.AnyAsync(login => login.LoginProvider == "Google" && login.ProviderKey == providerKey, Ct),
             Audit: await db.LoginAudits.AnyAsync(audit => audit.Identifier == email, Ct)));
        Assert.Equal((false, false, false, false), persisted);
    }
```

  Borrar las clases anidadas `FailedGoogleCommitProbe`, `ThrowingGoogleCommitUnitOfWork` y `ExpectedGoogleCommitFailure`, y los usings que quedan sin uso: `ArquitecturaBase.Application.Interfaces.Persistence`, `ArquitecturaBase.Domain.Results` (de la Tarea 3) y `Microsoft.Extensions.DependencyInjection.Extensions`.

- [ ] **Paso 3: el paso rojo del trinquete.** Sacar `ExternalLoginService` de `KnownSaveChangesCallers`.

- [ ] **Paso 4: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.Auth.ExternalLoginServiceTests"`
Esperado: FAIL. Donde se llega al lock de Google, lanza `InvalidOperationException` ("A lock was taken outside IUnitOfWork.ExecuteInTransactionAsync"). Donde no se llega, `Commits` queda en 0, porque el servicio todavía llama a `SaveChangesAsync`.

- [ ] **Paso 5: Google abre su límite.** En `ExternalLoginService.cs`, reemplazar `SignInAsync` completo por:

```csharp
    public async Task<Result<ExternalSignInResponse>> SignInAsync(
        ExternalSignInRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger);

        if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogFailed(logger, validationError.Code);
            return validationError;
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => SignInCoreAsync(ct),
            // Cada error de negocio deja su auditoría, y con una cuenta inactiva o bloqueada también el vínculo nuevo y el
            // correo confirmado: el error se confirma. Una excepción igual deshace todo.
            CommitPolicy.OnAnyResult,
            cancellationToken);

        if (result.IsSuccess)
        {
            // La cookie de la aplicación sale recién después del commit de la cuenta y el vínculo.
            await identity.SignInAsync(result.Value, cancellationToken);
            LogHandled(logger);
            return new ExternalSignInResponse(request.ReturnUrl!);
        }

        LogFailed(logger, result.Error.Code);
        return result.Error;
    }
```

- [ ] **Paso 6: los comentarios que estaban mal.**
  - `IIdentityService.cs`, `<summary>` de `CreateUnverifiedAsync` (hoy :42-50; la oración que cambia está partida en varias líneas `///`): reemplazar el `<summary>` completo por

    ```csharp
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
    ```
  - `IUserRepository.cs`, `<summary>` de `LockExternalSignInAsync`:

    ```csharp
        /// <summary>
        /// Serializa el alta o vínculo de una identidad externa por correo y clave del proveedor: toma external-login: y
        /// login-code: del correo en una sola llamada (por el orden ordinal, external-login: primero). Corre dentro de la
        /// transacción del caso de uso, que abre ExternalLoginService con ExecuteInTransactionAsync y OnAnyResult: la
        /// auditoría se confirma aunque el ingreso falle.
        /// </summary>
    ```
  - `UserRepository.cs`, en `CreateUnverifiedAsync`, reemplazar el comentario "Google y el bot pueden crear una cuenta sin el lock del destino. Si chocan con el índice único, EF revierte este guardado (savepoint en la transacción del caso de uso) y se despega la entidad para no reintentarla." por:

    ```csharp
            // El bot crea cuentas sin el lock del destino. Si ganó la carrera, el índice único choca acá: EF revierte solo
            // este guardado (savepoint en la transacción del caso de uso) y se despega la entidad para no reintentarla.
    ```

- [ ] **Paso 7: verlo pasar.** El comando del Paso 4, el proyecto de arquitectura, y:

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.ExternalLoginTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.CreateUserWithPhoneTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.RegistrationModeTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.InitialAdminSignInTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.AccountsWithPhoneTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UserSoftDeleteTests"`
Esperado: todo PASS, incluida la teoría de Google de la Tarea 1 y `Failed_google_commit_...` con `RolledBackBeforeLeaving`. Las cuatro últimas clases son las otras que pasan por `/account/external/callback`: el modo de registro, el administrador inicial, las cuentas con número y la cuenta borrada.

- [ ] **Paso 8: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 9: commit.**

```bash
git add src/ArquitecturaBase.Application src/ArquitecturaBase.Infrastructure/Persistence/Repositories/UserRepository.cs tests
git commit -m "$(cat <<'EOF'
refactor(auth): ingreso con Google con ExecuteInTransactionAsync

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 15 (C8a): el webhook guarda en su propio límite (y el reintento)

`WhatsAppWebhookPersistence.PersistAsync` es dueña de su límite, porque es la unidad que `WhatsAppWebhookRetry` vuelve a correr en un scope nuevo después de un 23505. `WhatsAppWebhookService`, `WhatsAppWebhookRetry` y la registración no cambian: el catch de `UniqueConstraintViolationException` queda fuera de toda transacción, y cuando llega, el primer scope ya hizo el rollback explícito y soltó `whatsapp-contact:*` y `whatsapp-message:*`.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/WhatsApp/WhatsAppWebhookPersistence.cs:23-44`
- Modificar: `src/ArquitecturaBase.Application/Interfaces/Services/IWhatsAppWebhookPersistence.cs:5` (documentación)
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/WhatsApp/WhatsAppWebhookPersistenceTests.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` (trinquete)

- [ ] **Paso 1: los tests unitarios.** En `WhatsAppWebhookPersistenceTests.cs`:
  - agregar `using ArquitecturaBase.Application.UnitTests.TestDoubles;`; borrar la clase `NoOpUnitOfWork` y el `using ArquitecturaBase.Domain.Results;` de la Tarea 3;
  - agregar el campo `private readonly FakeUnitOfWork _unitOfWork = new();` y, en el constructor, `_locks.InTransaction = () => _unitOfWork.InTransaction;`;
  - en `HandleAsync`, pasar `_unitOfWork` en lugar de `new NoOpUnitOfWork()`;
  - en `An_empty_webhook_takes_no_locks`, agregar al final `Assert.Equal(0, _unitOfWork.Transactions);`;
  - agregar:

    ```csharp
        [Fact]
        public async Task A_batch_is_saved_in_its_own_transaction()
        {
            await HandleAsync(Batch(Text("wamid.1", From(WaId, Bsuid, "Ana"), "Hola", Now)));

            Assert.Equal(1, _unitOfWork.Transactions);
            Assert.Equal(1, _unitOfWork.Commits);
            Assert.Equal(CommitPolicy.OnSuccess, _unitOfWork.LastPolicy);
        }
    ```

- [ ] **Paso 2: el paso rojo del trinquete.** Sacar `WhatsAppWebhookPersistence` de `KnownSaveChangesCallers`.

- [ ] **Paso 3: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.WhatsApp.WhatsAppWebhookPersistenceTests"`
Esperado: FAIL (los locks lanzan fuera de la transacción).

- [ ] **Paso 4: la persistencia abre su límite.** En `WhatsAppWebhookPersistence.cs`, agregar `using ArquitecturaBase.Domain.Results;` y reemplazar `PersistAsync` completo por:

```csharp
    public async Task PersistAsync(WhatsAppWebhookBatch batch, CancellationToken cancellationToken)
    {
        if (batch.IsEmpty)
        {
            return;
        }

        // El límite es de esta clase y no de WhatsAppWebhookService: WhatsAppWebhookRetry la vuelve a correr en un scope
        // nuevo después de un 23505, con la unidad de trabajo de ese scope. Si el guardado choca, el rollback suelta los
        // locks antes de que la excepción llegue al webhook, y el reintento no se queda esperando a este.
        var counts = await unitOfWork.ExecuteInTransactionAsync(
            ct => PersistCoreAsync(batch, ct), CommitPolicy.OnSuccess, cancellationToken);

        LogReceived(logger, counts.Value.Saved, counts.Value.Repeated, counts.Value.Applied, counts.Value.Ignored);
    }

    private async Task<Result<WebhookCounts>> PersistCoreAsync(WhatsAppWebhookBatch batch, CancellationToken cancellationToken)
    {
        // Antes de mirar nada: dos webhooks simultáneos de la misma persona (un reintento de Meta que se cruza con el
        // original) esperan acá, y el segundo ve lo que guardó el primero. Primero los contactos y después los
        // mensajes, en dos llamadas, como todo el que tome los dos.
        await contacts.LockAsync(
            Distinct(batch.Messages.Select(message => message.From.UserIdentifier)),
            Distinct(batch.Messages.Select(message => message.From.WaId)),
            cancellationToken);
        await messages.LockAsync(Distinct(batch.Statuses.Select(status => status.WaMessageId)), cancellationToken);

        var (saved, repeated) = await SaveInboundAsync(batch.Messages, cancellationToken);
        var (applied, ignored) = await ApplyStatusesAsync(batch.Statuses, cancellationToken);

        // Todo lo repetido se saltea y un estado viejo se ignora: siempre termina bien.
        return new WebhookCounts(saved, repeated, applied, ignored);
    }

    private readonly record struct WebhookCounts(int Saved, int Repeated, int Applied, int Ignored);
```

- [ ] **Paso 5: documentación de `IWhatsAppWebhookPersistence`.** Reemplazar el `<summary>` de la interfaz por:

```csharp
/// <summary>
/// Guarda un lote del webhook en su propia transacción (IUnitOfWork.ExecuteInTransactionAsync con OnSuccess). Es la
/// unidad que corre el webhook y que IWhatsAppWebhookRetry vuelve a correr, en un scope nuevo, después de un 23505.
/// </summary>
```

- [ ] **Paso 6: verlo pasar.** El comando del Paso 3, el proyecto de arquitectura, y el webhook contra Postgres (sin colisiones en paralelo, el 23505 forzado con `StaleReadsMessageRepository` y su reintento, y la privacidad en Trace):

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppWebhookTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppWebhookLogPrivacyTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppLockOrderTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppBotTests"`
Esperado: todo PASS. Si `A_message_saved_by_another_request_at_the_last_moment_counts_as_repeated` se cuelga, el rollback no está soltando los locks antes de relanzar: revisar el `catch` de `UnitOfWork.ExecuteInTransactionAsync`.

- [ ] **Paso 7: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 8: commit.**

```bash
git add src/ArquitecturaBase.Application tests
git commit -m "$(cat <<'EOF'
refactor(whatsapp): el webhook guarda en su propio límite

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 16 (C8b): el bot abre su límite antes de tomar el contacto

El límite envuelve `ProcessCoreAsync` entero y se abre antes de `GetForProcessingAsync` (`FOR NO KEY UPDATE SKIP LOCKED`): es lo que necesita `HeldContactRepository`, y si envolviera solo una parte, `CreateAsync`, `AddToRole` y `SetPhone` se confirmarían sueltos. `ProcessCoreAsync` nunca devuelve un `Result` fallido: los errores son excepciones y deshacen todo.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/WhatsApp/WhatsAppInboundService.cs:47-63`
- Modificar: `src/ArquitecturaBase.Application/Interfaces/Services/IWhatsAppInboundService.cs:5` (documentación)
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/WhatsApp/WhatsAppInboundServiceTests.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` (trinquete)

- [ ] **Paso 1: los tests unitarios.** En `WhatsAppInboundServiceTests.cs`:
  - en el constructor, después de crear los repositorios:

    ```csharp
            _locks.InTransaction = () => _unitOfWork.InTransaction;
            _loginLinks.InTransaction = () => _unitOfWork.InTransaction;
    ```
  - cambiar las aserciones:

| Test | Antes | Después |
|---|---|---|
| `An_active_account_gets_a_new_link_its_contact_linked_and_its_number_verified` | `Assert.Equal(1, _unitOfWork.SaveChangesCalls);` | `Assert.Equal(1, _unitOfWork.Commits);` |
| `A_contact_without_pending_messages_gets_no_reply` | `== 1` | `Assert.Equal(1, _unitOfWork.Commits);` |
| `When_the_queue_does_not_take_the_reply_it_fails_so_nothing_is_saved` | `Assert.Equal(0, _unitOfWork.SaveChangesCalls);` | `Assert.Equal(0, _unitOfWork.Commits);` + `Assert.Equal(1, _unitOfWork.Rollbacks);` |

- [ ] **Paso 2: el paso rojo del trinquete.** Sacar `WhatsAppInboundService` de `KnownSaveChangesCallers`.

- [ ] **Paso 3: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.WhatsApp.WhatsAppInboundServiceTests"`
Esperado: FAIL (tomar el contacto lanza fuera de la transacción).

- [ ] **Paso 4: el bot abre su límite.** En `WhatsAppInboundService.cs`, reemplazar `ProcessContactAsync` completo por:

```csharp
    public async Task<Result> ProcessContactAsync(Guid contactId, CancellationToken cancellationToken)
    {
        LogHandling(logger);

        // El límite se abre antes de tomar la fila del contacto: la fila, el lock de la cuenta, la cuenta nueva, los
        // procesados, el enlace y los vínculos van en una sola transacción. TooManyLinks, Disabled y NotInvited son
        // respuestas exitosas que confirman los procesados; si la cola no toma la respuesta, ProcessCoreAsync lanza y no
        // queda nada.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => ProcessCoreAsync(contactId, ct), CommitPolicy.OnSuccess, cancellationToken);

        if (result.IsSuccess)
        {
            LogHandled(logger);
        }
        else
        {
            LogFailed(logger, result.Error.Code);
        }

        return result;
    }
```

- [ ] **Paso 5: documentación de `IWhatsAppInboundService`.** Reemplazar el `<summary>` por:

```csharp
/// <summary>
/// Procesa los mensajes entrantes pendientes de un contacto en su propio límite (ExecuteInTransactionAsync con OnSuccess),
/// abierto antes de tomar la fila del contacto.
/// </summary>
```

- [ ] **Paso 6: verlo pasar.** El comando del Paso 3, el proyecto de arquitectura, y el bot contra Postgres junto con el perfil, que se cruza con él en el orden de locks y el `ConcurrencyStamp`:

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppBotTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppBotLogPrivacyTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppInboundSignalTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.MeWhatsAppEndpointsTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UnlinkUserPhoneEndpointTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UpdateUserContactTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UserInvitationEndpointsTests"`
Esperado: todo PASS. `A_failure_with_one_contact_does_not_stop_the_others_and_its_messages_stay_pending` sigue en verde, ahora con el rollback explícito antes del catch del procesador. 55P03 y 40P01 no se traducen ni se reintentan. Las tres últimas clases hacen correr al bot con `BotConversation` o `ProcessPendingAsync` después de desvincular, cambiar el número o invitar. Son las demás que lo manejan, además de las de `WhatsApp/`.

- [ ] **Paso 7: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 8: commit.**

```bash
git add src/ArquitecturaBase.Application tests
git commit -m "$(cat <<'EOF'
refactor(whatsapp): el bot abre su límite antes de tomar el contacto

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 17 (C8c): registro de envíos con `ExecuteInTransactionAsync`

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Services/WhatsApp/WhatsAppDeliveryService.cs` (reemplazo completo)
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Services/WhatsApp/{WhatsAppDeliveryServiceTests,RecordOutboundWhatsAppMessageTests,RecordUnsentWhatsAppMessageTests}.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` (trinquete)

- [ ] **Paso 1: los tests unitarios.**
  - `WhatsAppDeliveryServiceTests.cs`: en el constructor agregar `_invitations.InTransaction = () => _unitOfWork.InTransaction;`, y cambiar los tres `Assert.Equal(1, _unitOfWork.SaveChangesCalls);` (en `Sent_link_is_recorded_by_meta_id_without_its_secret`, `Sent_invitation_attaches_meta_id_before_commit` y `Unsent_invitation_is_marked_failed_and_saved`) por `Assert.Equal(1, _unitOfWork.Commits);`.
  - `RecordOutboundWhatsAppMessageTests.cs`: en el constructor agregar `_invitations.InTransaction = () => _unitOfWork.InTransaction;`.
  - `RecordUnsentWhatsAppMessageTests.cs`: agregar un constructor después de los campos:

    ```csharp
        public RecordUnsentWhatsAppMessageTests() => _invitations.InTransaction = () => _unitOfWork.InTransaction;
    ```

- [ ] **Paso 2: el paso rojo del trinquete.** Sacar `WhatsAppDeliveryService` de `KnownSaveChangesCallers` (queda vacía).

- [ ] **Paso 3: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Services.WhatsApp.WhatsAppDeliveryServiceTests" --filter-class "ArquitecturaBase.Application.UnitTests.Services.WhatsApp.RecordOutboundWhatsAppMessageTests" --filter-class "ArquitecturaBase.Application.UnitTests.Services.WhatsApp.RecordUnsentWhatsAppMessageTests"`
Esperado: FAIL (el lock de invitaciones lanza fuera de la transacción y `Commits` queda en 0).

- [ ] **Paso 4: el registro abre su límite.** Reemplazar `WhatsAppDeliveryService.cs` completo por:

```csharp
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.WhatsApp;

/// <summary>
/// Registra en el historial lo que la cola de WhatsApp mandó o no pudo mandar. Cada registro corre en su propio límite
/// (ExecuteInTransactionAsync con OnSuccess), en el scope que abre WhatsAppSenderBackgroundService después del HTTP a
/// Meta, que va fuera de toda transacción.
/// </summary>
internal sealed partial class WhatsAppDeliveryService(
    IWhatsAppContactRepository contacts,
    IWhatsAppMessageRepository messages,
    IUserReader users,
    IUserInvitationRepository invitations,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<WhatsAppDeliveryService> logger) : IWhatsAppDeliveryService
{
    private const string RecordSentOperation = "RecordSentWhatsAppMessage";
    private const string RecordUnsentOperation = "RecordUnsentWhatsAppMessage";

    public async Task<Result> RecordSentAsync(
        WhatsAppOutboundMessage message, string waMessageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrEmpty(waMessageId);
        LogHandling(logger, RecordSentOperation);

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => RecordSentCoreAsync(message, waMessageId, ct), CommitPolicy.OnSuccess, cancellationToken);

        LogHandled(logger, RecordSentOperation);
        return result;
    }

    public async Task<Result> RecordUnsentAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        LogHandling(logger, RecordUnsentOperation);

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => RecordUnsentCoreAsync(message, ct), CommitPolicy.OnSuccess, cancellationToken);

        LogHandled(logger, RecordUnsentOperation);
        return result;
    }

    // A propósito no toma whatsapp-message: ni la fila del contacto: los estados que llegan antes de que se confirme el
    // saliente se ignoran.
    private async Task<Result> RecordSentCoreAsync(
        WhatsAppOutboundMessage message, string waMessageId, CancellationToken cancellationToken)
    {
        var contact = await FindContactAsync(message.To, cancellationToken);
        messages.Add(WhatsAppMessage.Outbound(
            contact?.Id,
            waMessageId,
            KindOf(message),
            message.SafeSummary,
            timeProvider.GetUtcNow().UtcDateTime));

        if (message is WhatsAppInvitationMessage invitation)
        {
            // El lock espera el commit de quien encoló la invitación: la encuentra aunque Meta haya contestado antes.
            await invitations.LockAccountAsync(invitation.UserId, cancellationToken);
            (await invitations.GetByIdAsync(invitation.InvitationId, cancellationToken))?.AttachWhatsAppMessage(waMessageId);
        }

        return Result.Success();
    }

    private async Task<Result> RecordUnsentCoreAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        if (message is WhatsAppInvitationMessage invitation)
        {
            await invitations.LockAccountAsync(invitation.UserId, cancellationToken);
            (await invitations.GetByIdAsync(invitation.InvitationId, cancellationToken))?.MarkSendFailed();
        }

        return Result.Success();
    }

    private async Task<WhatsAppContact?> FindContactAsync(PhoneNumber to, CancellationToken cancellationToken)
    {
        if (await contacts.GetLatestByWaIdAsync(to.Value[1..], cancellationToken) is { } byNumber)
        {
            return byNumber;
        }

        return await users.FindByPhoneAsync(to, cancellationToken) is { } account
            ? await contacts.GetByUserIdAsync(account.Id, cancellationToken)
            : null;
    }

    private static WhatsAppMessageKind KindOf(WhatsAppOutboundMessage message) => message switch
    {
        WhatsAppTextMessage => WhatsAppMessageKind.Text,
        WhatsAppLinkButtonMessage or WhatsAppReplyButtonsMessage => WhatsAppMessageKind.Interactive,
        WhatsAppLoginCodeMessage or WhatsAppInvitationMessage => WhatsAppMessageKind.Template,
        _ => throw new ArgumentOutOfRangeException(nameof(message), message.GetType().Name, "Unknown WhatsApp message type."),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {Operation}")]
    private static partial void LogHandling(ILogger logger, string operation);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Operation}")]
    private static partial void LogHandled(ILogger logger, string operation);
}
```

- [ ] **Paso 5: verlo pasar.** El comando del Paso 3, el proyecto de arquitectura, y:

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppSenderBackgroundServiceTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppBotTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UserInvitationEndpointsTests"`
Esperado: todo PASS.

- [ ] **Paso 6: build sin advertencias y `dotnet test` completo en verde.** Desde acá ningún servicio llama a `IUnitOfWork.SaveChangesAsync`: la lista `KnownSaveChangesCallers` quedó vacía.

- [ ] **Paso 7: commit.**

```bash
git add src/ArquitecturaBase.Application/Services/WhatsApp/WhatsAppDeliveryService.cs tests
git commit -m "$(cat <<'EOF'
refactor(whatsapp): registro de envíos con ExecuteInTransactionAsync

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 18 (C8d): procesos en segundo plano y escrituras que quedan fuera del límite

Sin cambios de código: documenta dónde vive cada límite en los procesos de fondo y por qué cuatro escrituras quedan fuera de `ExecuteInTransactionAsync` a propósito. El código de `WhatsAppInboundProcessor`, `WhatsAppSenderBackgroundService`, `WhatsAppWebhookRetry`, `WhatsAppMessageRetentionService`, `EmailBackgroundService`, `ConnectService` y los seeders no cambia.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Infrastructure/WhatsApp/WhatsAppInboundProcessor.cs` (resumen de `ProcessContactAsync`)
- Modificar: `src/ArquitecturaBase.Infrastructure/WhatsApp/WhatsAppSenderBackgroundService.cs` (resúmenes de `RecordAsync` y `RecordUnsentAsync`)
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/WhatsAppMessageRetentionRepository.cs`
- Modificar: `src/ArquitecturaBase.Application/Interfaces/Services/IConnectService.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Seed/SeedExtensions.cs`

- [ ] **Paso 1: el procesador del bot.** Reemplazar el `<summary>` de `WhatsAppInboundProcessor.ProcessContactAsync` por:

```csharp
    /// <summary>
    /// Un scope por contacto: su propio contexto y su propia conexión. IWhatsAppInboundService abre ahí su límite
    /// (ExecuteInTransactionAsync) y, si algo falla, lo deshace y suelta los locks antes de que este catch registre el
    /// error. Así un contacto con un problema no deja a medio guardar nada de otro, y sus mensajes quedan pendientes para
    /// la vuelta siguiente.
    /// </summary>
```

- [ ] **Paso 2: la cola de envío.** En `WhatsAppSenderBackgroundService.cs`, reemplazar el `<summary>` de `RecordAsync` (hoy :120-125) completo por:

```csharp
    /// <summary>
    /// Guarda el mensaje recién mandado en el historial, con el id que devolvió Meta, para que los estados del webhook lo
    /// encuentren (sección 9 del spec). En su propio scope, donde IWhatsAppDeliveryService abre su límite
    /// (ExecuteInTransactionAsync); el HTTP a Meta ya salió, fuera de toda transacción. Si falla, el mensaje ya salió: se
    /// registra y no se vuelve a mandar, porque la persona lo recibiría dos veces. Lo único que se pierde es su historial
    /// y sus estados.
    /// </summary>
```

y el de `RecordUnsentAsync` (hoy :146-150) completo por:

```csharp
    /// <summary>
    /// Informa que el mensaje no salió, en su propio scope y su propio límite, como <see cref="RecordAsync"/>. Hoy solo
    /// cambia algo para una invitación, que queda como fallida. Si falla, se registra: lo único que se pierde es que el
    /// admin vea la invitación como pendiente en lugar de fallida.
    /// </summary>
```

- [ ] **Paso 3: la retención.** Agregar este `<summary>` a la clase `WhatsAppMessageRetentionRepository`:

```csharp
/// <summary>
/// Vacía el texto de los mensajes vencidos. Queda fuera de IUnitOfWork.ExecuteInTransactionAsync a propósito: es una
/// sola sentencia, idempotente, en autocommit, sobre una entidad que no es IAuditable ni ISoftDeletable. Es la única
/// clase que puede usar ExecuteUpdate (lo verifica TransactionBoundaryTests), y corre aunque WhatsApp esté apagado.
/// </summary>
```

- [ ] **Paso 4: la revocación de una autorización.** Agregar a `IConnectService.RevokeAuthorizationAsync`:

```csharp
    /// <summary>
    /// Revoca los tokens de esa autorización (el cierre de sesión). Queda fuera de IUnitOfWork.ExecuteInTransactionAsync a
    /// propósito: es un solo UPDATE de OpenIddict, en autocommit, y no devuelve Result.
    /// </summary>
```

- [ ] **Paso 5: el seed.** En `SeedExtensions.SeedDatabaseAsync`, agregar al final del `<summary>`: "No corre dentro de IUnitOfWork.ExecuteInTransactionAsync: es arranque, idempotente y aditivo, y cada seeder guarda por su cuenta. Hacerlo atómico y ponerlo en fila entre réplicas le corresponde a la decisión D6 (Etapa 7)."

- [ ] **Paso 6: los procesos de fondo contra Postgres.**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppBotTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppInboundSignalTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppSenderBackgroundServiceTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppMessageRetentionTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Settings.SystemSettingsSeedTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.ConnectFlowTests"`
Esperado: todo PASS.

- [ ] **Paso 7: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 8: commit.**

```bash
git add src
git commit -m "$(cat <<'EOF'
docs: documentar los límites de los procesos de fondo y las escrituras que quedan afuera

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 19 (C9): preparar datos sin `IUnitOfWork.SaveChangesAsync`

Los últimos que llaman a `SaveChangesAsync` de `IUnitOfWork` están en el proyecto de tests. `WidgetTestService` pasa al patrón (es el ejemplo mínimo para `AuditingTests`, `SoftDeleteTests` y `PaginationTests`); los tres setups que siembran un código pasan a `ApplicationDbContext.SaveChangesAsync`, que es preparación de datos, como `RegistrationModeScope`.

**Archivos:**
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Widgets/WidgetTestService.cs:36-48` (`CreateAsync`)
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/InitialAdminSignInTests.cs` (hoy :191)
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/RegistrationModeTests.cs` (hoy :257)
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/WhatsAppLoginCodeTests.cs` (hoy :482)

- [ ] **Paso 1: ver quién llama todavía.**

Run: `git grep -nE "(unitOfWork|IUnitOfWork>\(\))\.SaveChangesAsync" -- src tests`
Esperado: exactamente cinco líneas: `WidgetTestService.cs`, `InitialAdminSignInTests.cs`, `RegistrationModeTests.cs`, `WhatsAppLoginCodeTests.cs` y `UnitOfWorkTransactionTests.cs` (el test transitorio `SaveChangesAsync_inside_the_boundary_throws`). Si aparece otra, frenar: algo quedó sin migrar.

- [ ] **Paso 2: `WidgetTestService` con el patrón.** Reemplazar `CreateAsync` completo por:

```csharp
    public async Task<Result<Guid>> CreateAsync(CreateWidgetRequest request, CancellationToken cancellationToken)
    {
        var validationError = await createValidator.ValidateAsync(request, cancellationToken);
        if (validationError is not null)
        {
            return validationError;
        }

        // El ejemplo mínimo del patrón: validar afuera y escribir adentro de un solo límite.
        return await unitOfWork.ExecuteInTransactionAsync(
            _ =>
            {
                var widget = new Widget(request.Name!);
                dbContext.Set<Widget>().Add(widget);

                return Task.FromResult(Result.Success(widget.Id));
            },
            CommitPolicy.OnSuccess,
            cancellationToken);
    }
```

- [ ] **Paso 3: los tres setups.** En cada uno, reemplazar `await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);` por `await services.GetRequiredService<ApplicationDbContext>().SaveChangesAsync(Ct);`. `InitialAdminSignInTests.cs` ya tiene `using ArquitecturaBase.Infrastructure.Persistence;`; agregarlo en `RegistrationModeTests.cs` y en `WhatsAppLoginCodeTests.cs`. `using ArquitecturaBase.Application.Interfaces.Persistence;` sigue en uso en los tres (`ILoginCodeRepository`).

- [ ] **Paso 4: verificarlo.** El comando del Paso 1. Esperado: una sola línea, la de `UnitOfWorkTransactionTests.cs`.

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.AuditingTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.SoftDeleteTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.PaginationTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.InitialAdminSignInTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.RegistrationModeTests" --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.WhatsAppLoginCodeTests"`
Esperado: todo PASS.

- [ ] **Paso 5: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 6: commit.**

```bash
git add tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "$(cat <<'EOF'
test: preparar datos sin IUnitOfWork.SaveChangesAsync

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 20 (C10): los locks exigen la transacción del caso de uso

Recién ahora, con todo adentro de un límite. Si se adelantara, el bot lanzaría con cada contacto y sus mensajes quedarían pendientes para siempre.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Extensions/AdvisoryLockExtensions.cs` (reemplazo completo)
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/WhatsAppContactRepository.cs` (`GetForProcessingAsync`, `LockForNumberChangeAsync`, `GetByUserIdForUnlinkAsync`)
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/UserRepository.cs:14-19` (resumen de la clase)
- Modificar (documentación): `src/ArquitecturaBase.Application/Interfaces/Persistence/{ILoginCodeRepository,ILoginLinkRepository,IUserInvitationRepository,IWhatsAppMessageRepository,IWhatsAppContactRepository,IUserRepository}.cs`, `src/ArquitecturaBase.Application/Services/Users/PhoneNumberChange.cs:20-32` (el `<summary>` de `LockAsync`)
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/WhatsApp/WhatsAppLockOrderTests.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/WhatsApp/WhatsAppBotTests.cs` (hoy :359-375)
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UnitOfWorkTransactionTests.cs`
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` (trinquete)
- Modificar: `CLAUDE.md` (sección Persistencia)

- [ ] **Paso 1: confirmar que todo el que toma un lock ya está adentro de un límite.**

Run: `git grep -nE "LockDestinationAsync|LockAccountAsync|LockExternalSignInAsync|GetForProcessingAsync|LockForNumberChangeAsync|GetByUserIdForUnlinkAsync|\.LockAsync\(" -- src tests`
Esperado, fuera de las declaraciones de las interfaces, sus implementaciones y los decoradores de tests que solo delegan (`Held*`, `Throwing*`, `StaleReads*`, los dobles en memoria):

| Llamador | Límite que lo contiene |
|---|---|
| `LoginCodeIssuer` | `AccountService.Request*` (Tarea 12), `ProfileService.Request*` (Tarea 11) |
| `LoginCodeVerifier` | `AccountService.VerifyLoginCodeAsync` (Tarea 12) |
| `DestinationCodeVerifier` | `ProfileService.Confirm*` (Tarea 11) |
| `UserWriteOperations` | `UserService.CreateUserAsync` / `UpdateUserAsync` (Tarea 8) |
| `UserService.SendInvitationCoreAsync`, `UserInvitationSender` | `UserService.SendInvitationAsync` / `CreateUserAsync` (Tarea 8) |
| `UserStatusOperations`, `PhoneNumberChange`, `WhatsAppContactLinker` | `UserService` (Tareas 8 y 9), `ProfileService` (Tarea 11), bot (Tarea 16) |
| `LoginLinkIssuer` | bot (Tarea 16), `LoginLinkTestService` (Tarea 13) |
| `LoginLinkService.RedeemCoreAsync` | `LoginLinkService.RedeemAsync` (Tarea 13) |
| `ExternalLoginService.SignInCoreAsync` | `ExternalLoginService.SignInAsync` (Tarea 14) |
| `WhatsAppWebhookPersistence.PersistCoreAsync` | `WhatsAppWebhookPersistence.PersistAsync` (Tarea 15) |
| `WhatsAppInboundService.ProcessCoreAsync` | `WhatsAppInboundService.ProcessContactAsync` (Tarea 16) |
| `WhatsAppDeliveryService.Record*CoreAsync` | `WhatsAppDeliveryService.Record*Async` (Tarea 17) |
| `UnitOfWorkTransactionTests` | adentro de `ExecuteInTransactionAsync` |
| `WhatsAppLockOrderTests`, `WhatsAppBotTests` | ninguno todavía: los arregla el Paso 2 |

Si aparece un llamador que no está en la tabla, frenar y migrarlo antes.

- [ ] **Paso 2: los tests que toman locks sueltos abren su transacción.**
  - `WhatsAppLockOrderTests.cs`: en los dos tests, después de `await using var db = WithRecorder(scope.ServiceProvider, recorder);`, agregar:

    ```csharp
            // Los locks exigen la transacción del caso de uso; al descartar la del test se sueltan.
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
    ```

    y reemplazar el `<summary>` de `WithRecorder` (hoy :57-60; la oración vieja está partida en dos líneas `///`) completo por:

    ```csharp
        /// <summary>
        /// El contexto de producción (las mismas opciones que registra la Api) con un interceptor más. Los tests abren su
        /// transacción antes de los locks; al descartarla, los locks se sueltan.
        /// </summary>
    ```
  - `WhatsAppBotTests.cs` (en `A_contact_another_instance_has_taken_is_skipped_without_waiting_while_its_messages_keep_arriving`): agregar `using ArquitecturaBase.Infrastructure.Persistence;`; en el scope `first`, antes de `GetForProcessingAsync`:

    ```csharp
                // La primera instancia abre su transacción, toma el contacto y la deja abierta, como mientras decide la
                // respuesta.
                await using var firstTransaction = await first.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .Database.BeginTransactionAsync(Ct);
    ```

    (y borrar el comentario anterior "La primera instancia toma el contacto y deja la transacción abierta, como mientras decide la respuesta."); en el scope `second`, antes de su `GetForProcessingAsync`:

    ```csharp
                    await using var secondTransaction = await second.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                        .Database.BeginTransactionAsync(noWait.Token);
    ```

Dos comandos, porque `--filter-class` y `--filter-method` juntos se combinan con AND y no seleccionan nada (ver "Comandos"):

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppLockOrderTests"`

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-method "ArquitecturaBase.Api.IntegrationTests.WhatsApp.WhatsAppBotTests.A_contact_another_instance_has_taken_is_skipped_without_waiting_while_its_messages_keep_arriving"`

Esperado: los dos en PASS (valen antes y después del cambio), con 2 y 1 tests ejecutados. Si alguno dice "No se ejecutaron pruebas" (código 8), el filtro está mal escrito, no el código. El recorder no cambia: sigue viendo un NonQuery con `pg_advisory_xact_lock` y la clave en `Parameters[0]`.

- [ ] **Paso 3: el test que falla.** En `UnitOfWorkTransactionTests.cs`, agregar:

```csharp
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
        ];

        foreach (var takeLock in locks)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(takeLock);
        }

        Assert.Null(services.GetRequiredService<ApplicationDbContext>().Database.CurrentTransaction);
    }
```

- [ ] **Paso 4: el paso rojo del trinquete.** Dejar `KnownTransactionOpeners` vacía (quedaban `AdvisoryLockExtensions` y `WhatsAppContactRepository`).

- [ ] **Paso 5: verlo fallar.**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-method "ArquitecturaBase.Api.IntegrationTests.Persistence.UnitOfWorkTransactionTests.Locks_outside_the_boundary_throw_even_without_keys"`
Esperado: FAIL (hoy los locks abren su propia transacción y no lanzan).

- [ ] **Paso 6: `AdvisoryLockExtensions` exige la transacción.** Reemplazar el archivo completo por:

```csharp
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Extensions;

internal static class AdvisoryLockExtensions
{
    /// <summary>
    /// Toma un pg_advisory_xact_lock por clave (ver AdvisoryLockKeys). Dura lo que la transacción del caso de uso, que
    /// abre IUnitOfWork.ExecuteInTransactionAsync: este método no abre una propia, la exige. Las claves se toman ordenadas
    /// y sin repetir, así dos transacciones que piden las mismas claves en una llamada las piden en el mismo orden. Van
    /// como parámetro, así el log de un comando nunca muestra su valor.
    /// </summary>
    public static async Task AcquireAdvisoryLocksAsync(
        this DbContext dbContext,
        IEnumerable<string> keys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);

        // Antes de mirar las claves: un llamador sin transacción es un bug aunque esta vez no pida ninguna.
        dbContext.RequireTransaction();

        foreach (var key in keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            await dbContext.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        }
    }
}
```

- [ ] **Paso 7: los locks de fila exigen la transacción.** En `WhatsAppContactRepository.cs`, en `GetForProcessingAsync`, `LockForNumberChangeAsync` y `GetByUserIdForUnlinkAsync`, reemplazar el bloque

```csharp
        if (dbContext.Database.CurrentTransaction is null)
        {
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        }
```

por `dbContext.RequireTransaction();`, en la misma posición (antes del `FromSql`). En `GetForProcessingAsync`, además, agregar antes de esa línea el comentario `// La fila queda tomada hasta que termina la transacción del caso de uso: el bot la abre antes de llamar acá.` El SQL no cambia (`ORDER BY "Id" FOR NO KEY UPDATE`, `FOR NO KEY UPDATE NOWAIT`, `FOR NO KEY UPDATE SKIP LOCKED`). Reemplazar los tres `<summary>` completos:
  - `GetForProcessingAsync`:

    ```csharp
        /// <summary>
        /// <c>FOR NO KEY UPDATE SKIP LOCKED</c>: toma la fila sin esperar a nadie, y si otro la tiene, sigue sin ella. Es el
        /// lock de "voy a cambiar esta fila" (el bot la vincula a una cuenta), pero sin la parte que traba las claves
        /// foráneas: mientras el bot procesa un contacto, el webhook puede seguir guardándole mensajes y la cola de salida
        /// sus salientes. Sí espera, en cambio, el webhook que quiera actualizar el contacto (su nombre, su último mensaje).
        /// Dura lo que la transacción de IUnitOfWork.ExecuteInTransactionAsync, y con ella se suelta la fila; sin
        /// transacción, lanza.
        /// </summary>
    ```
  - `LockForNumberChangeAsync`:

    ```csharp
        /// <summary>
        /// <c>FOR NO KEY UPDATE</c>, el mismo lock que el bot pero sin <c>SKIP LOCKED</c>: espera a que el bot o un webhook
        /// suelten la fila. Postgres las toma en el orden en que las devuelve, y por eso van ordenadas por Id. Quedan en el
        /// contexto con lo que había después de esperar, y las lecturas que siguen devuelven estas mismas instancias. Exige
        /// la transacción de IUnitOfWork.ExecuteInTransactionAsync; sin ella, lanza.
        /// </summary>
    ```
  - `GetByUserIdForUnlinkAsync`:

    ```csharp
        /// <summary>
        /// <c>FOR NO KEY UPDATE NOWAIT</c>: el mismo lock que los demás, pero si otra transacción tiene la fila, Postgres
        /// corta con 55P03 en lugar de esperar. La transacción que ya la tiene (el cambio de número, que la tomó con
        /// <see cref="LockForNumberChangeAsync"/>) la vuelve a tomar sin problema. Exige la transacción de
        /// IUnitOfWork.ExecuteInTransactionAsync; sin ella, lanza.
        /// </summary>
    ```

- [ ] **Paso 8: la documentación de los contratos de lock.** Reemplazar cada `<summary>`:
  - `ILoginCodeRepository.LockDestinationAsync`:

    ```csharp
        /// <summary>
        /// Pone en fila los pedidos y las verificaciones de códigos de un mismo destino, con cualquier propósito. Exige la
        /// transacción de IUnitOfWork.ExecuteInTransactionAsync y dura lo que ella; sin transacción lanza
        /// InvalidOperationException. Sin esto, dos requests simultáneas leen el mismo estado y se saltean los límites de
        /// la sección 5.3.
        /// </summary>
    ```
  - `ILoginLinkRepository.LockAccountAsync`:

    ```csharp
        /// <summary>
        /// Pone en fila las emisiones y los canjes de enlaces de una misma cuenta. Exige la transacción de
        /// IUnitOfWork.ExecuteInTransactionAsync y dura lo que ella; sin transacción lanza InvalidOperationException. Sin
        /// esto, dos emisiones simultáneas se saltean los límites, y dos canjes simultáneos del mismo enlace entran los dos.
        /// </summary>
    ```
  - `ILoginLinkRepository.ListPendingAsync` (la oración vieja está partida en dos líneas `///`):

    ```csharp
        /// <summary>
        /// Los enlaces sin consumir ni invalidar de la cuenta, incluso los vencidos. Quedan seguidos: AccountAccessRevoker
        /// los invalida y los baja el guardado final del límite.
        /// </summary>
    ```
  - `IUserInvitationRepository.LockAccountAsync`:

    ```csharp
        /// <summary>
        /// Pone en fila lo que se hace con las invitaciones de una cuenta. Lo toman quien invita, antes de mirar la espera y
        /// de encolar el mensaje, y la cola de WhatsApp, antes de buscar la invitación que acaba de mandar. Exige la
        /// transacción de IUnitOfWork.ExecuteInTransactionAsync y dura lo que ella; sin transacción lanza
        /// InvalidOperationException. Sin esto, dos reenvíos simultáneos se saltean la espera, y la cola puede buscar la
        /// invitación antes de que se confirme la transacción que la guarda, y no encontrarla.
        /// </summary>
    ```
  - `IWhatsAppMessageRepository.LockAsync`:

    ```csharp
        /// <summary>
        /// Pone en fila los avisos de estado de esos mensajes: sin esto, dos avisos simultáneos del mismo mensaje leen el
        /// mismo estado y el que guarda último gana, aunque sea el más viejo. Toma los locks siempre en el mismo orden, y
        /// después de los de <see cref="IWhatsAppContactRepository.LockAsync"/>. Exige la transacción de
        /// IUnitOfWork.ExecuteInTransactionAsync y dura lo que ella; sin transacción lanza InvalidOperationException, también
        /// con la lista vacía.
        /// </summary>
    ```
  - `IWhatsAppContactRepository`, cuatro `<summary>` completos (varias de las frases viejas están partidas entre líneas `///`):
    - `LockAsync`:

      ```csharp
          /// <summary>
          /// Pone en fila, mientras dura la transacción del caso de uso, todo lo que se hace con los contactos de esos BSUID y
          /// esos números. Sin esto, dos webhooks simultáneos de la misma persona (Meta reintenta, y a veces a la vez) buscan
          /// el contacto, no lo encuentran y lo crean dos veces, o guardan dos veces el mismo mensaje. Toma los locks siempre
          /// en el mismo orden, así dos webhooks con las mismas personas no se esperan uno al otro para siempre. Quien
          /// también necesite los de <see cref="IWhatsAppMessageRepository.LockAsync"/>, toma estos primero. Exige la
          /// transacción de IUnitOfWork.ExecuteInTransactionAsync; sin ella lanza InvalidOperationException, también con las
          /// listas vacías.
          /// </summary>
      ```
    - `GetForProcessingAsync`:

      ```csharp
          /// <summary>
          /// El contacto con su fila tomada hasta que termine la transacción del caso de uso, que exige (sin ella lanza
          /// InvalidOperationException), para que el bot procese sus mensajes. Es el lock por contacto del procesador (sección
          /// 7 del spec del ingreso con WhatsApp): si otra instancia lo está procesando, o un webhook lo está guardando, no
          /// espera y devuelve null. Sus mensajes siguen pendientes y quedan para la próxima vuelta. También devuelve null si
          /// el contacto no existe.
          /// </summary>
      ```
    - `LockForNumberChangeAsync`:

      ```csharp
          /// <summary>
          /// Toma, hasta que termine la transacción del caso de uso, que exige (sin ella lanza InvalidOperationException), las
          /// filas de los contactos que toca un cambio del número de una cuenta: el vinculado a <paramref name="userId"/> y, si
          /// viene <paramref name="waId"/>, los de ese número. A diferencia de <see cref="GetForProcessingAsync"/>, espera a
          /// quien las tenga (el bot o un webhook): el cambio no se puede dejar para la próxima vuelta. Las toma ordenadas, así
          /// dos cambios que tocan los mismos contactos no se esperan uno al otro para siempre.
          /// </summary>
      ```
    - `GetByUserIdForUnlinkAsync`:

      ```csharp
          /// <summary>
          /// El contacto vinculado a esa cuenta, o null, con su fila tomada hasta que termine la transacción del caso de uso,
          /// que exige (sin ella lanza InvalidOperationException), para soltarlo porque la cuenta pasa a otro contacto. No
          /// espera: si la fila la tiene otro, falla con una excepción, y la transacción se deshace entera. Es para el bot, que
          /// ya tiene su contacto y el lock de la cuenta: un cambio de número (<see cref="LockForNumberChangeAsync"/>) toma esta
          /// fila y después espera el lock de la cuenta, y si el bot esperara la fila, cada uno esperaría al otro. Así el que se
          /// corre es el bot, que deja sus mensajes para la próxima vuelta. Quien ya tiene la fila la vuelve a tomar sin
          /// esperar.
          /// </summary>
      ```
  - `IUserRepository`: el `<summary>` de la interfaz por

    ```csharp
    /// <summary>
    /// Escrituras de cuentas sobre Identity. UserManager guarda en cada operación: adentro de ExecuteInTransactionAsync lo
    /// hace dentro de la transacción del caso de uso, con un savepoint por guardado. Fuera de la transacción, solo en la
    /// preparación de datos de los tests, se confirman en el acto. LockExternalSignInAsync exige la transacción.
    /// </summary>
    ```

    y el `<summary>` de `LockExternalSignInAsync` (el que dejó la Tarea 14) por

    ```csharp
        /// <summary>
        /// Serializa el alta o vínculo de una identidad externa por correo y clave del proveedor: toma external-login: y
        /// login-code: del correo en una sola llamada (por el orden ordinal, external-login: primero). Corre dentro de la
        /// transacción del caso de uso, que abre ExternalLoginService con ExecuteInTransactionAsync y OnAnyResult: la
        /// auditoría se confirma aunque el ingreso falle. Exige esa transacción; sin ella lanza InvalidOperationException.
        /// </summary>
    ```
  - `UserRepository` (clase): el `<summary>` por

    ```csharp
    /// <summary>
    /// Escrituras de cuentas con UserManager, que guarda en cada operación sobre el contexto compartido. Las escrituras no
    /// abren transacción: corren dentro de la del caso de uso (IUnitOfWork.ExecuteInTransactionAsync), con un savepoint por
    /// guardado. LockExternalSignInAsync la exige. Fuera de ella, solo en la preparación de datos de los tests, se
    /// confirman en el acto.
    /// </summary>
    ```
  - `PhoneNumberChange.LockAsync` (hoy :20-32; "Abre la / transacción si no hay una" está partida entre dos líneas `///`):

    ```csharp
        /// <summary>
        /// Toma los locks antes de escribir nada de la cuenta, en el mismo orden que el bot: primero las filas de los
        /// contactos que se van a tocar (el de la cuenta y, si pasa a <paramref name="newPhone"/>, los de ese número) y
        /// después el de los enlaces de la cuenta, con el que un canje en curso termina antes de que se invaliden. El bot
        /// toma el contacto y después la cuenta: al revés, cada uno espera al otro, Postgres corta a uno con un deadlock
        /// (40P01) y, si le toca al perfil, sale un 500. En el mismo orden, el que llega segundo espera al primero. Las dos
        /// excepciones las resuelve el bot: el chat que contesta puede no estar entre estas filas (otro contacto del número,
        /// sin vincular), así que con el lock de la cuenta vuelve a mirar si el número sigue siendo de ella; y el contacto de
        /// la cuenta que suelta para vincular el suyo no lo espera (<see cref="WhatsAppContactLinker.LinkAsync"/>). Corre
        /// dentro de la transacción del caso de uso, que los locks exigen, así el número, el contacto y los enlaces se
        /// guardan juntos. Quien llama lee la cuenta después, no antes: mientras espera, el bot puede escribirla (verifica el
        /// número del chat), y un guardado hecho con lo leído antes chocaría con el ConcurrencyStamp de Identity, que también
        /// es un 500.
        /// </summary>
    ```

- [ ] **Paso 9: CLAUDE.md, sección Persistencia.** Reemplazar el ítem que empieza "Para poner en fila operaciones sobre un mismo recurso" por:

```markdown
- Para poner en fila operaciones sobre un mismo recurso (los códigos de un destino, los enlaces de una cuenta, los contactos de un número), el repositorio toma un lock de Postgres (`pg_advisory_xact_lock` con la clave de `AdvisoryLockKeys`, o un lock de fila `FOR NO KEY UPDATE`) dentro de la transacción del caso de uso: los locks la exigen (sin ella lanzan `InvalidOperationException`, también con cero claves) y duran lo que ella. La abre solo `IUnitOfWork.ExecuteInTransactionAsync`, en READ COMMITTED; no se sube el aislamiento, porque leer la cuenta después del lock necesita ver lo que el otro acaba de confirmar. No hay `EnableRetryOnFailure`: si alguna vez se activa (por ejemplo, con `AddNpgsqlDbContext` de Aspire), `BeginTransactionAsync` lanza, y no se arregla envolviendo el trabajo en la estrategia de ejecución, porque el trabajo encola correos y mensajes y no se puede repetir.
```

- [ ] **Paso 10: verlo pasar.** El comando del Paso 5, el proyecto de arquitectura, y la suite de integración completa, porque cambia el contrato de todos los locks:

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`
Esperado: todo PASS. Si un test lanza `This operation needs the transaction of the use case`, hay un llamador que el Paso 1 no vio: migrarlo, no volver a abrir la transacción en el lock.

- [ ] **Paso 11: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 12: commit.**

```bash
git add src tests CLAUDE.md
git commit -m "$(cat <<'EOF'
refactor!: los locks exigen la transacción del caso de uso

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 21 (C11): `IUnitOfWork` sin `SaveChangesAsync`

El compilador pasa a garantizar que hay una sola forma de guardar.

**Archivos:**
- Modificar (reemplazo completo): `src/ArquitecturaBase.Application/Interfaces/Persistence/IUnitOfWork.cs`, `src/ArquitecturaBase.Infrastructure/Persistence/UnitOfWork.cs`, `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/FakeUnitOfWork.cs`, `tests/ArquitecturaBase.Api.IntegrationTests/Support/FailingCommitUnitOfWork.cs`, `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UnitOfWorkTransactionTests.cs` (borrar un test)
- Modificar: `src/ArquitecturaBase.Application/Common/Exceptions/UniqueConstraintViolationException.cs:5-9`
- Modificar: `src/ArquitecturaBase.Application/Interfaces/Integrations/IIdentityService.cs:63-70` (`SetPhoneAsync`)
- Modificar: `CLAUDE.md`, `docs/specs/2026-09-24-backend-mvc-architecture.md:158-161`, `AGENTS.md:19`

- [ ] **Paso 1: la regla que falla.** Reemplazar `TransactionBoundaryTests.cs` completo por su forma final (sin trinquete, sin la regla transitoria y con la regla 7). Sin las listas `Known*`, cada regla se controla sola: afirma que ve a su dueño permitido (`Assert.Contains`) o a alguien (`Assert.NotEmpty`) antes de filtrar (corrección de la revisión de la Tarea 4):

```csharp
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
```

Run: `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj -- --filter-method "ArquitecturaBase.ArchitectureTests.TransactionBoundaryTests.The_unit_of_work_has_a_single_way_to_save"`
Esperado: FAIL (`IUnitOfWork` todavía tiene dos métodos).

- [ ] **Paso 2: el contrato final.** Reemplazar `IUnitOfWork.cs` completo por:

```csharp
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// El límite transaccional de un caso de uso y la única forma de guardar. Lo usa solo el punto de entrada del caso de uso,
/// o sea una clase que implementa un contrato de Interfaces/Services, una vez por cada método que escribe. Adentro van los
/// locks, las lecturas que deciden y todas las escrituras, también las que UserManager y RoleManager guardan por su
/// cuenta: lo hacen sobre el mismo contexto, dentro de esta transacción y con un savepoint cada una. La validación del
/// pedido va antes, y lo que depende del commit (invalidar un caché, la cookie de Google) va después. Helpers,
/// repositorios y lectores nunca confirman.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Abre una transacción (READ COMMITTED), corre <paramref name="work"/> y después:
    /// <list type="bullet">
    /// <item>si <paramref name="policy"/> confirma el <see cref="Result"/> (<see cref="CommitPolicyExtensions.Commits"/>),
    /// baja lo que quedó seguido en un único guardado y confirma;</item>
    /// <item>si no lo confirma, deshace sin bajar nada y devuelve el mismo Result;</item>
    /// <item>si el trabajo, el guardado final o el commit lanzan una excepción (la cancelación incluida), deshace en el acto
    /// y la relanza. Si otro pedido guardó primero una fila con la misma clave única, la relanza como
    /// <see cref="UniqueConstraintViolationException"/>.</item>
    /// </list>
    /// Al deshacer suelta los locks antes de devolver o lanzar, y vacía el change tracker. No se anida ni adopta una
    /// transacción que no abrió: en los dos casos lanza <see cref="InvalidOperationException"/>. Nunca reintenta el
    /// trabajo, porque puede haber encolado un correo o un mensaje de WhatsApp.
    /// </summary>
    /// <param name="work">El caso de uso, ya validado. Casi siempre es un método privado <c>*CoreAsync</c> con el tipo
    /// de retorno escrito. Recibe el mismo token.</param>
    /// <param name="policy">Qué hacer con un Result fallido. No tiene valor por defecto, a propósito.</param>
    /// <param name="cancellationToken">El token del pedido: lo reciben el trabajo, el guardado final y el commit.</param>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work,
        CommitPolicy policy,
        CancellationToken cancellationToken)
        where TResult : Result;
}
```

- [ ] **Paso 3: la implementación final.** En `UnitOfWork.cs`: borrar el método `SaveChangesAsync` (con su comentario "En retiro") y el método `RollbackCurrentAsync` (con su comentario). El resto del archivo queda exactamente como en la Tarea 3 (el `using System.Data.Common;` sigue en uso por el `DbException` de `RollbackAsync`).

- [ ] **Paso 4: los dobles finales.**
  - `FakeUnitOfWork.cs`: borrar la propiedad `SaveChangesCalls` (con su comentario) y el método `SaveChangesAsync` (con su comentario).
  - `FailingCommitUnitOfWork.cs`: borrar el método `SaveChangesAsync` (con su comentario).
  - `UnitOfWorkTransactionTests.cs`: borrar el test `SaveChangesAsync_inside_the_boundary_throws` (con su comentario "Transitorio").

- [ ] **Paso 5: la excepción deja de nombrar a `SaveChangesAsync`.** (Obligatorio: el `cref` a `IUnitOfWork.SaveChangesAsync` rompe el build.) Reemplazar el `<summary>` de `UniqueConstraintViolationException` por:

```csharp
/// <summary>
/// Otro pedido guardó primero una fila con la misma clave única. La lanza
/// <see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/> después de deshacer la transacción, y en ese caso no se
/// guardó nada del caso de uso. También la lanzan los guardados de Identity que el repositorio traduce
/// (CreateUnverifiedAsync, SetEmailAsync, SetPhoneAsync): esos se pueden atrapar dentro del trabajo para devolver un
/// Result, porque el savepoint deshizo solo ese guardado. Es una falla de infraestructura, no una regla de negocio: la
/// mayoría de los casos de uso la dejan llegar al 500 genérico; el webhook de WhatsApp la trata como un aviso repetido.
/// </summary>
```

En `IIdentityService.SetPhoneAsync` (hoy :63-70; el final viejo, "y la cuenta queda como / estaba: el resto de la unidad de trabajo se puede guardar igual.", está partido entre dos líneas `///`), reemplazar el `<summary>` completo por:

```csharp
    /// <summary>
    /// Le pone el número a la cuenta, verificado o no. Solo escribe el dato: no renueva el security stamp, que le
    /// cortaría la cookie a quien vincula su propio número desde el perfil. Si hay que cerrar las sesiones, lo decide
    /// quien llama con <see cref="RevokeSessionsAsync"/>. El número tiene índice único: quien llama se fija antes con
    /// <see cref="FindByPhoneAsync"/> e <see cref="IsDeletedPhoneAsync"/>. Si igual choca, porque otra cuenta lo guardó
    /// entre esa búsqueda y este guardado, lanza <see cref="UniqueConstraintViolationException"/> y la cuenta queda como
    /// estaba: la transacción sigue usable, y con CommitPolicy.OnAnyResult se confirma lo demás.
    /// </summary>
```

- [ ] **Paso 6: compilar y verlo pasar.** `dotnet build ArquitecturaBase.slnx` (sin advertencias) y el comando del Paso 1: PASS. Después `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj` y el de `UnitOfWorkTransactionTests`: todo PASS.

- [ ] **Paso 7: CLAUDE.md.**
  - **Casos de uso MVC:** reemplazar el ítem que empieza "FluentValidation y `Result`/`Result<T>` siguen vigentes." por:

    ```markdown
    - FluentValidation y `Result`/`Result<T>` siguen vigentes. Cada servicio define su límite con `IUnitOfWork.ExecuteInTransactionAsync` y su `CommitPolicy`: `OnSuccess`, u `OnAnyResult` en los casos que tienen que guardar también cuando fallan (intentos, códigos o enlaces consumidos, auditoría). Es la única forma de guardar. Las consultas no abren límite. Mantener logging operativo sin registrar secretos.
    ```
  - **Persistencia:** borrar el ítem "**En transición (Etapa 1):** ..." de la Tarea 3 y agregar, después del ítem de los locks:

    ```markdown
    - **Una sola forma de guardar:** `IUnitOfWork.ExecuteInTransactionAsync(trabajo, CommitPolicy, ct)`, una vez por cada método público de un servicio que escribe (`TransactionBoundaryTests` lo verifica). El patrón, en cinco reglas:
      1. afuera y antes: `ThrowIfNull`, el log de inicio y el validador del pedido (un pedido inválido no abre transacción);
      2. un solo límite con la política escrita: `OnSuccess`, u `OnAnyResult` con un comentario que diga qué queda registrado cuando falla;
      3. adentro, en este orden: los locks (`login-code:` del correo y del número en dos llamadas, filas de contactos, `login-link:` o `user-invitation:`), las lecturas de lo que se va a modificar, las reglas, las escrituras y los efectos que tienen que quedar marcados en la fila (encolar y `MarkSent`);
      4. afuera, después y solo si se confirmó: invalidar caché, la cookie de Google, `Notify` y el log de resultado. Nunca un try/catch alrededor del límite (salvo el reintento del webhook, en un scope nuevo);
      5. los helpers (`*Operations`, `*Issuer`, `*Verifier`, `*Linker`, `PhoneNumberChange`, `AccountAccessRevoker`) nunca reciben `IUnitOfWork` ni guardan, y un servicio nunca llama al método de escritura de otro: anidar lanza.
    - `UserManager` y `RoleManager` siguen autoguardando (`AutoSaveChanges` no se toca), pero adentro de esa transacción y con un savepoint por guardado: un 23505 que traduce el repositorio se puede atrapar y la transacción sigue usable. Un error de SQL crudo (55P03, 40P01) nunca se atrapa adentro.
    - Quedan fuera a propósito: la retención de mensajes (`ExecuteUpdate`), `ConnectService.RevokeAuthorizationAsync` (un UPDATE de OpenIddict), los seeders, las migraciones, el servidor OpenIddict y Data Protection.
    ```
  - **WhatsApp:** en el ítem "**Los duplicados se cierran en tres capas**", reemplazar "y `UnitOfWork` traduciendo el 23505 a `UniqueConstraintViolationException` (`UniqueViolations`)" por "y `ExecuteInTransactionAsync` traduciendo el 23505 a `UniqueConstraintViolationException` (`UniqueViolations`) después de deshacer y soltar los locks".
  - **Tests:** agregar después del ítem de `Api.IntegrationTests` (al mismo nivel):

    ```markdown
    - **Unidad de trabajo en los tests:** los unitarios usan `FakeUnitOfWork` (`TestDoubles`), que aplica la misma regla que producción (`CommitPolicyExtensions.Commits`) y cuenta `Transactions`, `Commits`, `Rollbacks` y `LastPolicy`; lo que hay que mirar "al confirmar" se toma en `OnCommit`, y `CommitFailure` hace fallar el commit. Los dobles de lock reciben `InTransaction = () => unitOfWork.InTransaction` y lanzan fuera del límite. En integración, `FailingCommitUnitOfWork.Replace(services, probe)` hace fallar el commit sobre la unidad real, y `probe.RolledBackBeforeLeaving` confirma que el rollback lo hizo producción.
    ```

- [ ] **Paso 8: el spec canónico y AGENTS.md.**
  - `docs/specs/2026-09-24-backend-mvc-architecture.md`, sección "Validación, guardado y errores":
    - línea 158 (el ítem que empieza "FluentValidation valida los modelos de entrada"): reemplazar "Un pedido inválido no llega a repositorios ni a `IUnitOfWork`." por "Un pedido inválido no abre transacción ni llega a repositorios.";
    - línea 160 (el ítem que empieza "Una escritura define explícitamente cuándo confirma"), reemplazar el ítem completo por: "- Una escritura define su límite con `IUnitOfWork.ExecuteInTransactionAsync(trabajo, CommitPolicy, ct)`, la única forma de guardar; una consulta no abre límite. Hay casos que **deben guardar también al devolver error**, como intentos fallidos, códigos o enlaces consumidos y auditorías: usan `CommitPolicy.OnAnyResult`. Los demás usan `OnSuccess`, que deshace todo ante un error de negocio. No se aplica un guardado uniforme por convención.";
    - línea 161 (el ítem que empieza "`UserManager` y `RoleManager` pueden guardar internamente"), reemplazar el ítem completo por: "- `UserManager` y `RoleManager` guardan internamente, pero dentro de la transacción del caso de uso y con un savepoint por guardado. Los locks exigen esa transacción, duran lo que ella y conservan su orden: `login-code:` del correo y después del número, filas de contactos, y después la cuenta."
  - `AGENTS.md`, línea 19: reemplazar "Mantener `Result`, FluentValidation, `IUnitOfWork`, permisos, ProblemDetails y logging según las reglas funcionales vigentes." por "Mantener `Result`, FluentValidation, `IUnitOfWork.ExecuteInTransactionAsync` (la única forma de guardar, con su `CommitPolicy`), permisos, ProblemDetails y logging según las reglas funcionales vigentes.".

- [ ] **Paso 9: build sin advertencias y `dotnet test` completo en verde.**

- [ ] **Paso 10: commit.**

```bash
git add src tests CLAUDE.md AGENTS.md docs/specs/2026-09-24-backend-mvc-architecture.md
git commit -m "$(cat <<'EOF'
refactor!: IUnitOfWork sin SaveChangesAsync

ExecuteInTransactionAsync es la única forma de guardar. El test de
arquitectura deja el trinquete y fija la firma del contrato.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Tarea 22: puerta de la Etapa 1 (sin commit)

**Archivos:** ninguno.

- [ ] **Paso 1: build.** `dotnet build ArquitecturaBase.slnx`. Esperado: `0 Warning(s)`, `0 Error(s)`.
- [ ] **Paso 2: tests.** `dotnet test` con Docker. Esperado: todo en verde, incluidos `TransactionBoundaryTests` (7 PASS, sin listas), `UnitOfWorkTransactionTests` (15 PASS) y `ExplicitRouteInventoryTests` (las 41 rutas sin cambios: ningún status ni contrato HTTP cambió, así que no hace falta revisar el front).
- [ ] **Paso 3: nadie más abre, confirma ni deshace.**

Run: `git grep -nE "BeginTransaction|\.CommitAsync\(|\.RollbackAsync\(" -- src`
Esperado: solo `src/ArquitecturaBase.Infrastructure/Persistence/UnitOfWork.cs`.

- [ ] **Paso 4: nadie más guarda.**

Run: `git grep -nE "\.SaveChanges(Async)?\(" -- src`
Esperado: solo `Persistence/UnitOfWork.cs` (el guardado final) y `Persistence/Seed/SystemSettingsSeeder.cs` (arranque, D6). Ninguna línea en `src/ArquitecturaBase.Application`.

- [ ] **Paso 5: el SQL y las claves de los locks, en un solo lugar.**

Run: `git grep -nE "pg_advisory|\"(login-code|login-link|user-invitation|whatsapp-contact|whatsapp-message|external-login):" -- src`
Esperado: solo `AdvisoryLockExtensions.cs` y `AdvisoryLockKeys.cs`.

- [ ] **Paso 6: quién recibe la unidad de trabajo.**

Run: `git grep -ln "IUnitOfWork unitOfWork" -- src`
Esperado: `AccountService.cs`, `ExternalLoginService.cs`, `LoginLinkService.cs`, `RoleService.cs`, `SystemSettingsService.cs`, `ProfileService.cs`, `UserService.cs`, `WhatsAppDeliveryService.cs`, `WhatsAppInboundService.cs` y `WhatsAppWebhookPersistence.cs`. Ningún `*Operations`.

- [ ] **Paso 7: documentación.** `CLAUDE.md` (Persistencia), el spec canónico y `AGENTS.md` describen `ExecuteInTransactionAsync` como la única forma de guardar; `git grep -n "SaveChangesAsync" -- CLAUDE.md AGENTS.md docs/specs` no devuelve nada que la presente como vigente.
- [ ] **Paso 8: AppHost.** Si se levantó para probar a mano, `aspire stop`.
- [ ] **Paso 9: árbol.** `git status --short` sin salida.

---

### Tarea 23 (C12): cerrar la Etapa 1

**Archivos:**
- Modificar: `docs/plans/2026-09-26-plantilla-estandar-por-etapas.md`
- Modificar: `docs/decisions/0001-transaccion-explicita-por-caso-de-uso.md`

- [ ] **Paso 1: el plan maestro.**
  - En "Etapa 1: una sola forma de guardar", agregar debajo del título:

    ```markdown
    **Estado:** cerrada el <AAAA-MM-DD> con el plan detallado `docs/history/plans/2026-09-26-etapa-1-una-sola-forma-de-guardar.md`. Puerta cumplida: build sin advertencias, `dotnet test` en verde, `TransactionBoundaryTests` sin listas de infractores y el inventario de 41 rutas sin cambios. Diferencias con lo planeado acá: un solo método genérico con `CommitPolicy` obligatoria (`OnSuccess`/`OnAnyResult`) en lugar de dos sobrecargas; una transacción ajena o anidada lanza en lugar de reutilizarse; la regla de arquitectura es "solo un punto de entrada que implementa un contrato de `Interfaces/Services` recibe `IUnitOfWork`", y no "el nombre termina en Service"; la invalidación de enlaces pasó a `AccountAccessRevoker`; y, como mejora que salió de la revisión del plan, la fábrica de caché de `SystemSettingsReader` lee con su propio scope, para que ninguna consulta de otro pedido corra sobre la conexión de un límite.
    ```

    (con la fecha real del cierre).
  - En la Etapa 3, tarea 9, reemplazar "`IWhatsAppWebhookPersistence` pasa a `Interfaces/Persistence`." por "`IWhatsAppWebhookPersistence` queda en `Interfaces/Services`: es un punto de entrada que abre su propio límite y que `IWhatsAppWebhookRetry` vuelve a correr en un scope nuevo (Etapa 1)." En el ítem de avance de esa misma tarea (desde `d63eb43`), reemplazar "las subcarpetas de `Integrations` y la mudanza de `IWhatsAppWebhookPersistence`, que hoy está en `Interfaces/Services`." por "las subcarpetas de `Integrations`. `IWhatsAppWebhookPersistence` no se muda: la Etapa 1 la dejó en `Interfaces/Services`."
  - En la Etapa 7, tarea 4 (Producción, D6), agregar un ítem: "El seed dentro de un límite y en fila entre réplicas (un advisory lock `seed:` adentro de `ExecuteInTransactionAsync`): la Etapa 1 lo dejó afuera. Antes, verificar que los managers de OpenIddict no abran su propia transacción (`CreateTransactionAsync`), que lanzaría dentro del límite."
- [ ] **Paso 2: la decisión 0001.** En "Decisión", reemplazar la oración "El diseño concreto (la firma, cómo se reutiliza la transacción que abrió un lock y cómo un caso de uso pide que un `Result` fallido igual persista un intento) está en el [plan de la Etapa 1](...)." por "El diseño concreto (la firma con `CommitPolicy`, por qué una transacción ajena o anidada lanza en lugar de reutilizarse, y cómo un caso de uso pide con `CommitPolicy.OnAnyResult` que un `Result` fallido igual persista un intento) está en el [plan de la Etapa 1](../plans/2026-09-26-etapa-1-una-sola-forma-de-guardar.md)." En "Consecuencias", reemplazar "los repositorios dejan de abrir su propia transacción y usan la del caso de uso" por "los repositorios dejan de abrir su propia transacción y exigen la del caso de uso". Si `docs/decisions/` no está versionado todavía (Tarea 0), frenar y avisar.
- [ ] **Paso 3: commit.**

```bash
git add docs/plans/2026-09-26-plantilla-estandar-por-etapas.md docs/decisions/0001-transaccion-explicita-por-caso-de-uso.md
git commit -m "$(cat <<'EOF'
docs: cerrar la Etapa 1

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Autorrevisión

Rehecha después de la segunda revisión (lentes de cobertura, concurrencia y ejecutabilidad), contra `main` en `d63eb43`.

### Cobertura contra el mapa

Cada método del mapa transaccional, con la tarea que lo cubre:

| Método del mapa | Tarea |
|---|---|
| `ProfileService.UpdateAsync`, `RequestEmailCodeAsync`, `ConfirmEmailAsync`, `RequestPhoneLinkCodeAsync`, `ConfirmPhoneLinkAsync`, `UnlinkOwnPhoneAsync` | 11 (el guard de WhatsApp apagado, afuera del límite con `EnsureEnabled`, y su test en `ProfileServiceTests`) |
| `AccountService.RequestLoginCodeAsync`, `RequestWhatsAppLoginCodeAsync`, `VerifyLoginCodeAsync` | 12 (y 1 para el verify de una cuenta inactiva) |
| `WhatsAppWebhookService.ReceiveAsync`, `WhatsAppWebhookPersistence.PersistAsync`, `WhatsAppWebhookRetry.RetryAsync` | 15 |
| `WhatsAppInboundService.ProcessContactAsync` | 16 |
| `WhatsAppInboundProcessor.ExecuteAsync` / `ProcessPendingAsync` | 18 (sin cambio de código) |
| `WhatsAppDeliveryService.RecordSentAsync`, `RecordUnsentAsync` | 17 |
| `WhatsAppSenderBackgroundService.ExecuteAsync` | 18 (sin cambio de código) |
| `WhatsAppMessageRetentionService.ClearExpiredTextsAsync` | 18 (excepción documentada; regla 6 de la Tarea 4) |
| `UserService.CreateUserAsync`, `UpdateUserAsync`, `SendInvitationAsync` | 8 |
| `UserService.SetUserActiveAsync`, `DeleteUserAsync`, `UnlinkUserPhoneAsync` | 9 y 10 |
| `LoginLinkService.RedeemAsync` | 13 (y 1 para la cuenta borrada) |
| `ExternalLoginService.SignInAsync` | 14 (y 1 para la cuenta inactiva o bloqueada); el doble de su lock exige el límite desde la 14 |
| `ConnectService.RevokeAuthorizationAsync` (aparece dos veces en el mapa) | 18 (excepción documentada) |
| `LoginLinkIssuer.IssueAsync` | 13 y 16 (sus llamadores); 20 (su lock exige la transacción) |
| `IdentityService.RevokeSessionsAsync` | 10 |
| `IdentityService.RegisterFailedAttemptAsync`, `ResetFailedAttemptsAsync` | 12 (corren dentro del verify y del canje; sin cambio de código) |
| `IdentityService.AddExternalLoginAsync` | sin llamador en src; sin cambio (lo recorta la Etapa 2) |
| `RoleService.CreateAsync`, `UpdateAsync`, `DeleteAsync` (aparecen dos veces en el mapa) | 6 |
| `SystemSettingsService.UpdateAsync` (aparece dos veces) | 7 |
| `SystemSettingsReader.GetRegistrationModeAsync` (no escribe; lo señala la nota del grupo 1 del mapa por la fábrica de caché) | 7, segundo commit (mejora explícita, decisión 14) |
| `RoleSeeder`, `SystemSettingsSeeder`, `OpenIddictSeeder`, `SeedExtensions.SeedDatabaseAsync` | 18 (excepción documentada); regla 4 de la Tarea 4 exceptúa `Persistence.Seed` |
| Servidor OpenIddict y Data Protection | sin cambio; el `ChangeTracker.Clear()` de la Tarea 3 evita que reviva algo deshecho |
| `LoginLinkTestService.IssueAsync` (tests) | 13 |
| `WidgetTestService.CreateAsync` (tests) | 19 |
| `IdentityService` CRUD de roles y delegaciones de escritura sin llamador | sin cambio; no toman locks, así que ni la 6 ni la 20 las afectan |
| `DatabaseMigrationExtensions.ApplyMigrationsAsync` | sin cambio |
| `PermissionService` (lectura con `HybridCache`, mismo patrón que el lector de ajustes) | sin cambio a propósito: se lee solo fuera de todo límite (§2.8) |

Tareas del plan maestro: 1 (`ExecuteInTransactionAsync`) → Tareas 2 y 3; 2 (locks en la transacción del caso de uso) → Tareas 5 y 20; 3 (catálogo de claves) → Tarea 5; 4 (migrar servicios, en el orden sugerido) → Tareas 6 a 17; 5 (`RevokeSessionsAsync` explícito) → Tarea 10; 6 (blindaje) → Tareas 4 y 21. La puerta → Tarea 22. El versionado del plan y la confirmación de las decisiones → Tarea 0.

### Hallazgos de la segunda revisión

| Hallazgo | Resolución |
|---|---|
| La Tarea 0 exigía un árbol limpio, pero el plan estaba sin versionar, y la Tarea 22 (Paso 9) no se podía cumplir | Tarea 0: Paso 0 (confirmar las catorce decisiones), Paso 1 (todo lo que no sea el plan lo versiona el usuario; nunca se descarta el plan) y Paso 2 (commit `docs: plan de la Etapa 1`). "Comandos" suma la regla de mirar `git status --short` antes de cada commit, así los `git add` de la Tarea 3 (`CLAUDE.md`) y de la 21 (`AGENTS.md`, `docs/specs`) no se llevan cambios ajenos |
| Las listas de `--filter-class` de las Tareas 8, 13, 14 y 16 no incluían todos los llamadores | 13: `MeWhatsAppEndpointsTests`, `UnlinkUserPhoneEndpointTests`, `UpdateUserContactTests`, más `WhatsAppRouteContractsTests` y `AuthConnectAccountContractTests`, que también canjean. 14: `RegistrationModeTests`, `InitialAdminSignInTests`, `AccountsWithPhoneTests` y `UserSoftDeleteTests`. 16: `UnlinkUserPhoneEndpointTests`, `UpdateUserContactTests` y `UserInvitationEndpointsTests`. **No se agregaron** `UserSoftDeleteTests` a la 8 ni `WhatsAppRegistrationTests` a la 16: la primera borra con el contexto y restaura con `IIdentityService.RestoreAsync`, sin pasar por el alta (la restauración por el alta ya la cubren `CreateUserEndpointTests` y `CreateUserWithPhoneTests`, que están en la lista), y la segunda solo prueba la registración y el arranque, sin hacer correr al bot |
| El doble del lock de Google no exigía el límite | Tarea 14, Paso 1: `FakeIdentityService.InTransaction` y `TransactionGuard.Require` en `LockExternalSignInAsync`. El helper `Service(FakeUnitOfWork?)` ata el guard a la unidad que corre el caso de uso, también a `failing` |
| El guard `IsEnabled` del perfil quedaba adentro del límite | Tarea 11: `ProfileWhatsAppOperations.EnsureEnabled()`, llamado por `ProfileService` después del validador y antes de `ExecuteInTransactionAsync`, y un test en `ProfileServiceTests` que afirma `Transactions == 0`. Se actualizaron la regla 1 del §3 y la fila del §4 |
| `WidgetTestService.cs:97-109` estaba mal | `:36-48` (`CreateAsync`) |
| La fábrica de `HybridCache` de `SystemSettingsReader` corre sobre el contexto de quien llama, adentro de un límite | Decisión 14, §2.8 y la Tarea 7 (Pasos 8 a 13, un commit `fix`): la fábrica abre su propio scope. El test nuevo cachea lo no confirmado con el código de hoy y pasa con el cambio |
| `--filter-class` y `--filter-method` juntos no seleccionan nada | La Tarea 20 (Paso 2) queda en dos comandos, y "Comandos" explica la regla. Ningún otro comando del plan los mezcla |
| El Paso 4 de la Tarea 0 miraba menos de lo que el plan toca y enumeraba commits viejos | Tarea 0, Paso 5: `git log --oneline cbff737..HEAD -- src tests CLAUDE.md AGENTS.md docs/specs`, con la regla de releer todo archivo del mapa que haya cambiado |
| Textos XML en prosa con comillas escapadas, anclas partidas entre líneas `///` y `PhoneNumberChange.cs:147-159` | Bloques `csharp` completos: `IIdentityService.CreateUnverifiedAsync` (14), `WhatsAppSenderBackgroundService.RecordAsync` y `RecordUnsentAsync` (18), `WhatsAppLockOrderTests.WithRecorder`, los tres de `WhatsAppContactRepository`, `ILoginLinkRepository.ListPendingAsync`, los cuatro de `IWhatsAppContactRepository`, `IUserRepository.LockExternalSignInAsync` y `PhoneNumberChange.LockAsync` (20), `DestinationCodeVerifier` (11) e `IIdentityService.SetPhoneAsync` (21). La referencia pasa a `PhoneNumberChange.cs:20-32`. "Comandos" suma la regla general de las anclas |
| La Tarea 9 contaba cinco `Failed_*` | Seis, contando el de la Tarea 8 |
| Mientras se revisaba llegaron `487c3a0`, `eccbaeb` y `d63eb43` a `main` | Tarea 4, Paso 7: el ítem `**ArchitectureTests:**` de `CLAUDE.md` ya lista otras convenciones, así que se le agrega una oración en lugar de reemplazarlo. Tarea 21: el spec pasa a las líneas 158 a 161 y `AGENTS.md` a la 19. Tarea 23: se ajusta también el ítem de avance de la Etapa 3 que mencionaba la mudanza de `IWhatsAppWebhookPersistence`. Tarea 0: el Paso 1 y el Paso 5 describen el estado de `d63eb43` (solo falta versionar el plan; `docs/decisions/` ya está) |

### Consistencia de nombres y tipos entre tareas

- `CommitPolicy.OnSuccess` / `CommitPolicy.OnAnyResult` y `CommitPolicyExtensions.Commits(this CommitPolicy, Result)`: Tarea 2; usados igual en `UnitOfWork`, `FakeUnitOfWork`, `FailingCommitUnitOfWork` y todos los servicios.
- `IUnitOfWork.ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CommitPolicy policy, CancellationToken cancellationToken) where TResult : Result`: la misma firma en la Tarea 3 (transitoria), en la 21 (final), en los stubs de la Tarea 3, en `FakeUnitOfWork` y en `FailingCommitUnitOfWork`; la regla 7 la fija.
- `FakeUnitOfWork`: `Transactions`, `Commits`, `Rollbacks`, `LastPolicy`, `InTransaction`, `OnCommit` (init), `CommitFailure` (set) y, hasta la Tarea 21, `SaveChangesCalls`. Las fotos de los fixtures se llaman `SentAtCommit`, `FailedAttemptsAtCommit`, `CodeConsumedAtCommit`, `AuditsAtCommit`, `SignedInAtCommit` y `QueuedAtCommit`.
- `TransactionGuard.Require(Func<bool>?)` y la propiedad `Func<bool>? InTransaction` de `InMemoryLoginCodeRepository`, `InMemoryLoginLinkRepository`, `InMemoryUserInvitationRepository` y `LockLog` (Tarea 3) y de `FakeIdentityService` (Tarea 14), con el mismo comentario; se conectan en las Tareas 8, 9, 11, 12, 14, 15, 16 y 17.
- `FailingCommitUnitOfWork.Replace(IServiceCollection, CommitFailureProbe)`, `CommitFailureProbe.BeforeFailing` y `RolledBackBeforeLeaving`, `ExpectedCommitFailure`: Tarea 9; usados en la 9 y en la 14; finales en la 21.
- `TransactionExtensions.RequireTransaction(this DbContext)`: Tarea 6; usado en `RoleRepository` (6), `IdentityService` (10), `AdvisoryLockExtensions` y `WhatsAppContactRepository` (20).
- `AdvisoryLockKeys.LoginCode`, `LoginLink`, `UserInvitation`, `WhatsAppContactByUser`, `WhatsAppContactByWaId`, `WhatsAppMessage`, `ExternalLogin`: Tarea 5; los mismos nombres en `AdvisoryLockKeysTests` y en los repositorios.
- `AccountAccessRevoker.RevokeAsync(Guid, CancellationToken)`, con constructor `(ILoginLinkRepository, IIdentityService, TimeProvider)`: Tarea 10; el mismo orden en `UserServiceTestHost` y en `AccountAccessRevokerTests`.
- `UserWriteOperations.ValidateCreateAsync`/`CreateAsync`/`ValidateUpdateAsync`/`UpdateAsync` (Tarea 8), `ProfileEmailOperations.ValidateRequestAsync`/`RequestCodeAsync`/`ValidateConfirmAsync`/`ConfirmAsync` y `ProfileWhatsAppOperations.ValidateRequestAsync`/`EnsureEnabled`/`RequestCodeAsync`/`ValidateConfirmAsync`/`ConfirmAsync`/`UnlinkAsync` (Tarea 11): los mismos nombres en `UserService`, `ProfileService` y `ProfileServiceTests`. El constructor de `ProfileWhatsAppOperations` que usa el test nuevo es el de 15 parámetros del Paso 6 de la Tarea 11.
- `SystemSettingsReader(IServiceScopeFactory scopeFactory, HybridCache cache)`: Tarea 7; la registración scoped no cambia, y el test nuevo (`The_cache_factory_reads_on_its_own_connection_and_never_caches_an_uncommitted_mode`) usa `RegistrationModeScope` y `ReadModeAsync`, que ya existen en la clase.
- Listas del trinquete: cada tarea saca exactamente lo que su commit deja de infringir; `KnownUnitOfWorkReceivers` queda vacía en la 11, `KnownLockLiteralOwners` en la 5, `KnownSaveChangesCallers` en la 17 y `KnownTransactionOpeners` en la 20; la 21 borra el mecanismo. Cada regla afirma que ve a su dueño permitido antes de filtrarlo (`UnitOfWork`, `AdvisoryLockExtensions`, la retención desde la 4; `AdvisoryLockKeys` desde la 5) o, la de `ExecuteInTransactionAsync`, que ve a alguien (desde la 6), para no pasar en silencio cuando las listas ya no están. `SystemSettingsReader` no recibe `IUnitOfWork` ni abre transacciones: ninguna regla de `TransactionBoundaryTests` lo mira. El lector (`CallSites`) saltea desde la Tarea 4 los tipos que emite un generador (`GeneratedCodeAttribute` en el tipo de nivel superior), con `CallSitesTests`; la 21 no lo toca.

### Ejecutabilidad (qué se verificó y cómo)

- **Filtros de tests.** Cada `--filter-class` y cada `--filter-method` del plan se contrastó con el namespace real de su clase. Los 133 nombres de test que cita el plan existen en `tests/`, salvo los que el propio plan crea o renombra. Ningún comando mezcla `--filter-class` con `--filter-method`. Se reprodujo en una copia de `e92d354` que mezclarlos da `total: 0` y código 8, y que repetir `--filter-method` hace OR.
- **Los fragmentos nuevos de esta revisión.** En una copia de `e92d354`, fuera del árbol de trabajo:
  - el test del lector falla con el código de hoy (`Expected: InviteOnly`, `Actual: Open`);
  - con el `SystemSettingsReader` de la Tarea 7, compila sin advertencias y pasan 18 de 18 en `SystemSettingsReaderTests`, `SettingsControllerTests`, `RegistrationModeTests` e `InitialAdminSignInTests`;
  - el test del perfil compiló y pasó con el constructor de hoy, que tiene un `IUnitOfWork` más al final, y con `SaveChangesCalls` en lugar de `Transactions`: los `null!` no se tocan antes del guard.

  El guard de `FakeIdentityService` y el helper de `ExternalLoginServiceTests` no se compilaron, porque dependen del `FakeUnitOfWork` de la Tarea 3. Sus tipos (`Email`, `TransactionGuard` en el namespace padre, `CommitPolicy` en `Interfaces.Persistence`) se leyeron del árbol.
- **Documentación.** Todo `<summary>` que cambia viene en un bloque `csharp` completo, con comillas normales. Las únicas instrucciones en prosa que quedan son:
  - ediciones de Markdown (`CLAUDE.md`, `AGENTS.md`, el spec, el plan maestro y la decisión 0001), cuyas anclas se verificaron contra `d63eb43`, cada una en una sola línea;
  - agregados al final de un `<summary>` (el seed);
  - el comentario `//` de `UserRepository.CreateUnverifiedAsync` en la Tarea 14, que viene como bloque y cuya ancla ocupa dos líneas (lo cubre la regla de "Comandos").
- **Referencias de línea verificadas:** `WidgetTestService.cs:36-48`, `PhoneNumberChange.cs:20-32`, `FakeIdentityService.cs:102-104`, `IIdentityService.cs:42-50` y `:63-70`, `WhatsAppSenderBackgroundService.cs:120-125` y `:146-150`, `WhatsAppLockOrderTests.cs:57-60`, y, en `d63eb43`, las líneas 158 a 161 del spec y la 19 de `AGENTS.md`. Las demás llevan "hoy :N" y pueden correrse: manda el texto citado.
- **Git.** Con la Tarea 0, el plan queda versionado antes de la primera tarea. Los enlaces que agregan la Tarea 3 (`CLAUDE.md`) y la 23 (plan maestro) apuntan a un archivo del repo, y el Paso 9 de la Tarea 22 se puede cumplir. En `d63eb43`, `git status --short` muestra solo el plan, y `docs/decisions/` ya está versionado (lo necesitan la Tarea 0 y la 23).
- **Lo que no se verificó.** En esta revisión no se compiló ni se corrió el plan entero. La lente de ejecutabilidad de la revisión compiló el plan y corrió sus tests sobre `1153b81`. Después de `1153b81` se revisaron a mano los commits que llegaron, hasta `d63eb43`. `e92d354` no toca nada que el plan cite: sacó los `ToString` de los modelos de pedido y tres tests de `ToString`, y corrió líneas en tests de integración. `487c3a0` y `eccbaeb` no tocan archivos del plan, y lo que cambió `d63eb43` quedó ajustado en la tabla de arriba. Los fragmentos nuevos se probaron contra `e92d354`, y ninguno de los tres commits siguientes toca esos archivos. Lo que llegue a `main` después lo cubre el Paso 5 de la Tarea 0.
