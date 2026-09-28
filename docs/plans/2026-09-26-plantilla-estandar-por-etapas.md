# Plantilla estándar: plan de trabajo por etapas

> **Para agentes:** este es el plan maestro. En las Etapas 0 a 2, cada etapa se ejecuta con su propio plan detallado (TDD, pasos de 2 a 5 minutos, código completo), que se escribe con `superpowers:writing-plans` **al arrancar la etapa**, porque depende de cómo quedó la anterior. Desde la Etapa 3 rige la [Forma de trabajo desde la Etapa 3](#forma-de-trabajo-desde-la-etapa-3): un solo diseño con revisor adversarial, 6 a 10 tareas, suite completa solo en la puerta. Estado al 2026-09-28: la Etapa 0 se hizo el 2026-09-26 y las Etapas 1 y 2 se cerraron el 2026-09-27; las Etapas 3 y 4 tienen todas sus tareas hechas en código y les falta la puerta (`dotnet test` completo con Docker); la 6 está postergada sin fecha, y de la 5 y la 7 hay tareas adelantadas. Los pasos usan casillas (`- [ ]`) para el seguimiento.

**Objetivo:** que ArquitecturaBase sirva como plantilla estándar para empezar cualquier proyecto. Tiene que tener una sola forma de hacer cada cosa, un área de referencia para copiar, documentación en capas que una IA pueda seguir sin adivinar y módulos de producto (WhatsApp) que se puedan quitar.

**Origen:** el code review del 2026-09-26. Hallazgos principales:
- dos formas de guardar (Identity autoguarda y `IUnitOfWork`);
- `IIdentityService` con 39 métodos, la mayoría reenvíos;
- WhatsApp entrelazado con Auth y Users;
- no hay una receta ni un área de referencia;
- boilerplate que quedó del pipeline viejo (111 llamadas de log manuales, un validador inyectado por request);
- `CLAUDE.md` mezcla la plantilla con el producto.

**Arquitectura:** no cambia la arquitectura canónica (`docs/architecture/backend.md`): controllers → servicios → repositorios/lectores. El plan termina la migración, quita duplicados y deja por escrito y con tests lo que hoy es convención implícita.

**Stack:** .NET 10, ASP.NET Core MVC, EF Core + Npgsql, Identity + OpenIddict, FluentValidation, xUnit v3, Testcontainers, NetArchTest.

---

## Reglas para todas las etapas

- Se trabaja directo en `main`. El 2026-09-26 se avanzó `main` hasta la punta de `codex/mvc-migracion` con un fast-forward y se borró esa rama. Sin ramas ni push nuevos salvo pedido explícito.
- Commits chicos, en español, con conventional commits, terminando con `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Puerta de cada etapa** (no se pasa a la siguiente sin esto):
  1. `dotnet build ArquitecturaBase.slnx` sin advertencias.
  2. `dotnet test` en verde. Los tests de integración necesitan Docker.
  3. El inventario de las 41 combinaciones verbo/ruta sigue igual, o se amplió con sus tests.
  4. La documentación de la etapa está actualizada en el mismo commit que el código que describe.
  5. Si se levantó el AppHost para probar: `aspire stop`.
- **No cambia ningún contrato HTTP que consuma el front** (`../ArquitecturaBaseFront`) sin revisar antes el cliente. Si una etapa cambia un status (por ejemplo, 200 → 201), la tarea incluye buscar en el front los usos de esa ruta.

## Decisiones

El usuario las confirmó todas el 2026-09-26 tal como están recomendadas.

| # | Decisión | Recomendación | Por qué |
|---|---|---|---|
| D1 | Cómo lograr una sola forma de guardar | **Transacción explícita por caso de uso:** `IUnitOfWork.ExecuteInTransactionAsync`, con Identity autoguardando adentro | Apagar `AutoSaveChanges` en los stores cambia el comportamiento de `SignInManager` (bloqueo, intentos fallidos), del `SecurityStamp` y del `ConcurrencyStamp`. El riesgo de regresión es alto. La transacción explícita formaliza lo que ya hacen los locks y deja el límite visible en el servicio |
| D2 | Regla de contratos HTTP | **Todo body y query de entrada tiene un contrato en `Api/Contracts/<Área>`** con mapeo manual. Las respuestas serializan los `*Response` de Application | Hoy conviven dos estilos. Así la regla es una sola, Application deja de conocer detalles de MVC (los `ToString()` que ocultan datos personales se mudan al contrato) y un test la puede verificar |
| D3 | Eventos de dominio | **Quitar** `AggregateRoot` e `IDomainEvent` | Nadie los levanta ni los despacha. Despacharlos pediría handlers, y el usuario los rechazó para la arquitectura. Si un proyecto los necesita, se agregan con su despachador y sus tests |
| D4 | Área de referencia | **Roles**, completada con paginado, obtener por id y metadatos OpenAPI | Está en todo proyecto derivado, así que la referencia nunca se borra. Es chica y ya sigue el camino canónico |
| D5 | Versionado de la API | **No versionar por ahora, pero dejarlo documentado:** rutas `api/<recurso>`; el primer cambio incompatible introduce `Asp.Versioning` | Es YAGNI, pero con la decisión escrita para que nadie invente un esquema propio |
| D6 | Migraciones y seed fuera de Development | **Seed idempotente en todos los ambientes.** Las migraciones fuera de Development se aplican con un bundle (`dotnet ef migrations bundle`) como paso del despliegue | Aplicar migraciones al arrancar con varias réplicas corre en paralelo. El seed ya es idempotente ("si la fila existe, manda la base") |
| D7 | WhatsApp | **Módulo opcional dentro del mismo repo**, con un registro propio y puertos hacia el núcleo, y una guía para quitarlo | Sacarlo a otro repo es prematuro. Lo que falta es que se pueda quitar sin tocar 40 piezas |

---

## Etapa 0: higiene y restos de la migración (bajo riesgo, medio día)

**Objetivo:** sacar todo lo que confunde a una persona o a una IA sin cambiar comportamiento.

### Tarea 0.1: sacar los secretos del árbol

**Archivos:**
- Mover fuera del repo: `docs/client_secret_830839449608-….apps.googleusercontent.com.json` e `id de cliente.txt`.

- [x] **Paso 1:** confirmar que ninguno está trackeado. Si alguno está trackeado, parar y avisar al usuario: es una rotación de secreto, no una limpieza.
  ```bash
  git ls-files docs | grep -i secret; git ls-files "id de cliente.txt"
  ```
  Se espera: salida vacía.
- [ ] **Paso 2 (pendiente del usuario, avisado el 2026-09-26):** pedirle al usuario que los mueva a una carpeta suya fuera del repo (por ejemplo `%USERPROFILE%\secrets\ArquitecturaBase\`). No borrarlos sin confirmación: son sus únicas copias.
- [x] **Paso 3:** en `README.md`, sección de Google, aclarar que el JSON de credenciales se guarda fuera del repo y que el `ClientSecret` va en user-secrets.
- [x] **Paso 4:** sumar `.playwright-mcp/` al `.gitignore`, porque hoy aparece como no trackeado.
- [x] **Paso 5:** commit `docs: aclarar dónde se guardan las credenciales de Google`.

### Tarea 0.2: borrar los restos de CQRS y del pipeline viejo

**Archivos:**
- Modificar `src/ArquitecturaBase.Application/Services/Users/UserService.cs` (9 literales `"…Command"` y `"…Query"`).
- Modificar `src/ArquitecturaBase.Application/Services/Users/ProfileService.cs` (7 literales).
- Modificar los comentarios de:
  - `Services/Auth/AccountService.cs:87`
  - `Services/Auth/LoginLinkService.cs:80`
  - `Services/Roles/RoleService.cs:45`
  - `Services/Users/ProfileEmailOperations.cs:68`
  - `Services/Users/UserWriteOperations.cs:44`
  - `Infrastructure/Persistence/Repositories/UserRepository.cs:16`

- [x] **Paso 1:** reemplazar cada literal por el nombre del método del servicio (`"CreateUserCommand"` → `"CreateUser"`, `"GetUsersQuery"` → `"GetUsers"`). Antes, confirmar que ningún test compara el texto del log:
  ```bash
  grep -rnE "(Command|Query)\"" tests --include=*.cs
  ```
- [x] **Paso 2:** reescribir cada comentario para que explique el **porqué actual** sin nombrar el pipeline viejo. Ejemplo para `LoginLinkService.cs:80`: `// Se guarda también cuando el canje falla: el intento consumido tiene que persistir.`
- [x] **Paso 3:** `dotnet build ArquitecturaBase.slnx` y `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`. Se espera todo verde.
- [x] **Paso 4:** commit `refactor: quitar nombres y comentarios del pipeline CQRS retirado`.

### Tarea 0.3: una sola visibilidad para los servicios

**Archivos:**
- Modificar a `internal`:
  - `Services/Auth/LoginLinkIssuer.cs`
  - `Services/Auth/LoginLinkService.cs`
  - `Services/Roles/RoleService.cs`
  - `Services/Settings/SystemSettingsService.cs`

- [x] **Paso 1:** cambiar `public sealed` → `internal sealed`. Si el build falla porque un test los usa, verificar que `InternalsVisibleTo` incluya ese proyecto de tests (ver el `.csproj` de Application) en lugar de volver a `public`.
- [x] **Paso 2:** build y tests unitarios de Application.
- [x] **Paso 3:** commit `refactor: servicios de Application internos como el resto`.

### Tarea 0.4: referencias viejas y una sola convención de nombres de tests

**Archivos:**
- `CLAUDE.md:79`: `HandleInboundMessageTests` → `WhatsAppInboundServiceTests` y `BotReplyTests` (`tests/ArquitecturaBase.Application.UnitTests/Services/WhatsApp/`).
- `.editorconfig:55`: el comentario dice `Metodo_condicion_resultado`; alinearlo con `CLAUDE.md` (desde la Etapa 5, `AGENTS.md`, "Tests"), que pide frases en inglés en minúscula (`Deleted_rows_are_hidden_from_queries_and_endpoints`).
- `README.md:309`: revisar la referencia al arreglo del test inestable en el plan de la Fase 3. Si está vieja o rota, apuntarla a donde está de verdad (buscar con `git log -S`) o quitarla.
- Mover `tests/ArquitecturaBase.Application.UnitTests/Services/RoleServiceTests.cs`, `RoleServiceWriteTests.cs` y `SystemSettingsServiceTests.cs` a `Services/Roles/` y `Services/Settings/`, y ajustar sus namespaces.

- [x] **Paso 1:** hacer los cambios.
- [x] **Paso 2:** build y `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`.
- [x] **Paso 3:** commit `docs: corregir referencias y unificar la convención de nombres de tests`.

### Tarea 0.5: marcar lo histórico

**Archivos:**
- Todos los planes de `docs/plans/` salvo este (desde la Etapa 5 están en `docs/history/plans/`).
- `docs/specs/2026-09-18-arquitectura-base-design.md`, `2026-09-20-fase-4-administracion-design.md` y `2026-09-22-ingreso-whatsapp-design.md`.

- [x] **Paso 1:** agregar como primera línea de cada plan viejo:
  ```markdown
  > **HISTÓRICO. No ejecutar.** Registro de cómo se construyó esta parte. La arquitectura vigente está en `docs/architecture/backend.md`; donde este documento hable de handlers, `Features/` o Minimal API, prevalece la especificación.
  ```
- [x] **Paso 2:** en los specs de Fase 4 y WhatsApp, una línea equivalente que aclare que sus **reglas funcionales** siguen vigentes y su **estructura de código** no.
- [x] **Paso 3:** commit `docs: marcar planes y specs históricos`.

**Puerta de la Etapa 0:** la puerta general, más una búsqueda que tiene que dar vacía:

```bash
git grep -nE 'Command"|Query"|handler anterior|decorator anterior|endpoint anterior|heredad|Adaptaci.n temporal' -- 'src/*.cs' ':!*Migrations*'
```

Se limita a C# porque `appsettings.Development.json` usa la categoría de log `Microsoft.EntityFrameworkCore.Database.Command`, que es de EF Core y no un resto de CQRS.

**Resultado (2026-09-26):**
- **Más literales de los previstos.** Además de `UserService` y `ProfileService`, tenían sufijo `Command`/`Query` los mensajes de log de `AccountService`, `LoginLinkService` y `WhatsAppWebhookService`. Donde el método tiene un nombre genérico, el log lleva el área: `GetAsync` → `GetProfile`, `PreviewAsync` → `PreviewLoginLink`.
- **Visibilidad.** `LoginLinkIssuer` lo usa `TestFeatures` por su tipo concreto, así que Application suma `InternalsVisibleTo` para `Api.IntegrationTests`.
- **La referencia del test inestable del README no estaba rota:** estaba vieja. El problema lo había resuelto `422a6de`, y el párrafo quedó reescrito como resuelto.
- **`docs/plans/contracts/` queda sin marcar.** Son instantáneas previas a la migración, con nombres de handlers. El inventario vivo de rutas es `tests/ArquitecturaBase.Api.IntegrationTests/Contracts/ExplicitRouteInventoryTests.cs`. Se ordenan en la Etapa 5. **Hecho el 2026-09-26:** están en `docs/history/plans/contracts/`, con una cabecera HISTÓRICO que remite a ese test.
- **Pendiente heredado.** El plan de migración deja abiertas las comprobaciones manuales de su Tarea 1, su Tarea 8 y "Puertas abiertas para el cierre": el smoke con Aspire, OIDC y WhatsApp, y el arranque con una base vacía.

---

## Etapa 1: una sola forma de guardar (riesgo alto, es la base de todo)

**Estado:** cerrada el 2026-09-27 con el plan detallado `docs/history/plans/2026-09-26-etapa-1-una-sola-forma-de-guardar.md`, que desde el cierre es histórico. Puerta cumplida: build sin advertencias, `dotnet test` en verde, `TransactionBoundaryTests` sin listas de infractores y el inventario de 41 rutas sin cambios. El objetivo, el estado anterior y las tareas de abajo son los planeados; lo que se hizo distinto:
- un solo método genérico con `CommitPolicy` obligatoria (`OnSuccess`/`OnAnyResult`) en lugar de dos sobrecargas;
- una transacción ajena o anidada lanza en lugar de reutilizarse;
- la regla de arquitectura es "solo un punto de entrada que implementa un contrato de `Interfaces/Services` recibe `IUnitOfWork`", y no "el nombre termina en Service";
- la invalidación de enlaces pasó a `AccountAccessRevoker`;
- como mejora que salió de la revisión del plan, la fábrica de caché de `SystemSettingsReader` lee con su propio scope, para que ninguna consulta de otro pedido corra sobre la conexión de un límite.

**Después del cierre (2026-09-27, revisión final):** las escrituras de cuentas también exigen la transacción. Todas las de `IUserRepository` y las de `IIdentityService` (sus delegaciones, los intentos fallidos y el cierre de sesiones) lanzan fuera de un límite, como los locks y `RoleRepository`; eso revierte la decisión 11 del plan detallado, que las dejaba en autocommit para los arneses. Los tests preparan datos con `factory.InTransactionAsync` (un límite real con `OnSuccess`) y `FakeIdentityService.ArrangeAsync`. El CRUD de roles duplicado de `IIdentityService` se borró: los roles se escriben solo por `IRoleRepository`.

**Objetivo:** que en cada caso de uso el límite transaccional se lea en el servicio y sea uno solo. Decisión D1.

**Estado anterior (antes de la etapa, 2026-09-26):**
- `UnitOfWork.SaveChangesAsync` confirma la transacción que haya abierto algún repositorio al tomar un lock (`AdvisoryLockExtensions.cs:27`, `LoginCodeRepository.cs:16`, `WhatsAppContactRepository.cs:44,64,89`).
- `RoleRepository.cs:90` abre y confirma su propia transacción.
- `UserManager` y `RoleManager` guardan en cada llamada. Sin un lock previo, un caso de uso que hace dos escrituras de Identity puede quedar a medias.

### Tareas

1. **`IUnitOfWork.ExecuteInTransactionAsync`.**
   - Archivos: `Application/Interfaces/Persistence/IUnitOfWork.cs` e `Infrastructure/Persistence/UnitOfWork.cs`.
   - Firma: `Task<Result<T>> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken)`, más la variante sin `T`.
   - Comportamiento:
     - abre la transacción si no hay una;
     - si ya hay una (porque un lock la abrió), la reutiliza;
     - ante un `Result` fallido, deshace **salvo** que el caso de uso lo pida explícitamente (algunos errores deben persistir intentos: ver `CLAUDE.md`, "Cada servicio define expresamente cuándo guarda"; desde la Etapa 5, `AGENTS.md`, "Una sola forma de guardar");
     - ante una excepción, deshace y traduce el 23505 como hoy.
   - Tests de integración nuevos en `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/UnitOfWorkTransactionTests.cs`:
     - dos escrituras de Identity con la segunda fallida → no queda ninguna;
     - lock tomado adentro → se reutiliza la transacción;
     - `Result` fallido con persistencia pedida → queda el intento.
2. **Pasar los locks a la transacción del caso de uso.** Los `Lock*Async` dejan de abrir una transacción propia y exigen una activa (lanzan `InvalidOperationException` si no la hay: es un bug, no una regla de negocio). Archivos:
   - `AdvisoryLockExtensions.cs`
   - `LoginCodeRepository.cs`, que además deja de reimplementar el lock y usa `AcquireAdvisoryLocksAsync`
   - `WhatsAppContactRepository.cs`
   - `WhatsAppMessageRepository.cs`
3. **Catálogo de claves de lock.** Crear `Infrastructure/Persistence/Extensions/AdvisoryLockKeys.cs` con todas las claves:
   - `login-code:`, `login-link:`, `user-invitation:`, `whatsapp-contact:user:` y `whatsapp-contact:wa:`;
   - `external-login:` y `whatsapp-message:`, que hoy no están documentadas.

   Reemplazar los literales repetidos (por ejemplo `UserRepository.cs:32`) y actualizar la lista de `CLAUDE.md` (desde la Etapa 5, en `docs/architecture/backend.md`, "Una sola forma de guardar").
4. **Migrar los servicios, uno por commit.** Cada método que escribe envuelve su trabajo en `ExecuteInTransactionAsync`. Los helpers (`*Operations`) dejan de llamar a `SaveChangesAsync`: guarda solo el método del servicio que expone la interfaz. Orden sugerido, de menor a mayor riesgo:
   1. `RoleService`, que además saca la transacción de `RoleRepository.cs:81-111`;
   2. `SystemSettingsService`;
   3. `UserService` con `UserWriteOperations`, `UserStatusOperations` y `UserPhoneOperations`;
   4. `ProfileService` con `ProfileEmailOperations` y `ProfileWhatsAppOperations`;
   5. los servicios de Auth;
   6. los de WhatsApp.
5. **`RevokeSessionsAsync` explícito.** Hoy depende de que `UpdateSecurityStampAsync` guarde de paso las invalidaciones de `LoginLink` (`IdentityService.cs:118-128`). Tiene que quedar como dos pasos explícitos dentro de la transacción.
6. **Blindaje.** Un test de arquitectura en `tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs` que falle si:
   - un tipo de `Application.Services` cuyo nombre no termina en `Service` depende de `IUnitOfWork`;
   - un repositorio llama a `BeginTransactionAsync` fuera de `AdvisoryLockExtensions`.

**Riesgos:**
- los tests de concurrencia de WhatsApp (deadlock 40P01, `NOWAIT`) dependen del orden de los locks, y ese orden no puede cambiar;
- los tests de `ConcurrencyStamp` (leer la cuenta **después** de tomar los locks).

Correr la suite de integración completa después de cada servicio migrado.

**Puerta:**
- la puerta general;
- el test de arquitectura nuevo en verde;
- la sección "Persistencia" de `CLAUDE.md` (desde la Etapa 5, "Una sola forma de guardar", en `AGENTS.md` y en `docs/architecture/backend.md`) y el spec canónico describen `ExecuteInTransactionAsync` como la única forma.

---

## Etapa 2: `IIdentityService` solo técnico

**Estado:** cerrada el 2026-09-27 con el plan detallado `docs/history/plans/2026-09-27-etapa-2-identity-solo-tecnico.md`, que desde el cierre es histórico. Puerta cumplida: build sin advertencias, `dotnet test` en verde, `IdentityBoundaryTests` y `PersistenceNamingTests` sin listas de conocidos, el inventario de 41 rutas sin cambios y la búsqueda de `IIdentityService` vacía en `src`, `tests` y la documentación vigente. El objetivo, el estado anterior y las tareas de abajo son los planeados; lo que se hizo distinto:
- `IIdentityService` tenía 36 miembros y no 39 (la Etapa 1 ya había borrado el CRUD de roles), y solo 16 tenían llamadores en src;
- `ISignInService` quedó con 7 miembros y no 10: `FindByExternalLogin` y `HasExternalLogin` (ahora `ExistsExternalLogin`) se quedaron en `IUserReader`, y `AddExternalLogin` en `IUserRepository`, donde ya estaban. Vive plano en `Application/Interfaces/Integrations`, no en una subcarpeta `Identity/`;
- los consumidores fueron cinco de los siete de la lista más `AccountAccessRevoker`, que no figuraba: `UserPhoneOperations` y `UserStatusOperations` ya usaban el lector y el repositorio;
- los archivos de integración que nombraban `IIdentityService` eran 23, no 20;
- la regla "ningún tipo de `Application.Services` depende de `Microsoft.AspNetCore.Identity`" ya la cubrían `LayerDependencyTests`, `ApplicationPackagesTests` y las referencias del csproj, y no se duplicó. En su lugar entró `IdentityBoundaryTests`: una sola carga por Id de la cuenta para modificarla, `ISignInService` chico y técnico, solo `SignInService` toca la sesión, solo los puntos de entrada del ingreso abren una sesión y solo el ingreso por código suma intentos fallidos;
- la puerta "`git grep IIdentityService` vacío" se acotó a `src`, `tests` y la documentación vigente, specs funcionales incluidos (el de WhatsApp nombraba `IIdentityService.SignInAsync` y se corrigió): los planes, el diseño inicial y los ADR lo nombran a propósito;
- además de lo planeado: las tres cookies de ingreso salen después del commit y `SignInAsync` lanza adentro de un límite; toda fábrica de `HybridCache` lee en su propio scope (`HybridCacheExtensions`), lo que deja sin efecto la decisión 14 de la Etapa 1 ("`PermissionService` no se toca"); el bot relee la cuenta vinculada después de su lock; y la convención de nombres quedó en el ADR 0008, con 9 renombres y las invitaciones de `IUserInvitationRepository` seguidas.

**Objetivo:** que para cada operación sobre usuarios haya un solo camino.

**Estado anterior (antes de la etapa):** 39 métodos, de los que solo usan algo los siete servicios de Application que figuran en la tabla. Los métodos `CreateRoleAsync`, `UpdateRoleAsync`, `DeleteRoleAsync` y `GetRolesAsync` no los usa ningún servicio (los tres primeros ya se borraron, ver abajo). `IdentityService.cs:72-75` documenta que `SetPhone`, `RemovePhone` y `SetEmail` solo delegan en `IUserRepository` y se recortan en esta etapa.

### Tareas

1. **Separar por responsabilidad.** Quedan tres contratos:
   - `ISignInService` (en `Application/Interfaces/Integrations/Identity/`), solo lo técnico:
     - `SignInAsync`, `SignOutExternalAsync`, `IsLockedOutAsync`;
     - `RegisterFailedAttemptAsync`, `ResetFailedAttemptsAsync`, `RevokeSessionsAsync`;
     - login externo: `GetExternalLoginAsync`, `AddExternalLoginAsync`, `FindByExternalLoginAsync`, `HasExternalLoginAsync`.
   - `IUserRepository` y `IUserReader`, para todo el acceso a datos de cuentas: `Create`, `SetEmail`, `SetPhone`, `FindBy*`, `IsDeleted*`, `Restore`, `SetActive`, `SetDisplayName`, `SetRoles`.
   - `IRoleReader` e `IRoleRepository`, para roles. **Hecho (2026-09-27, después del cierre de la Etapa 1):** se borró el CRUD de roles duplicado de `IdentityService`; quedan las lecturas de roles de `IIdentityService`, que delegan en `IRoleReader` y tampoco tienen llamadores en `src`.
2. **Migrar los consumidores, uno por commit:**
   - `AccountService`
   - `ExternalLoginService`
   - `LoginCodeVerifier`
   - `LoginLinkService`
   - `UserPhoneOperations`
   - `UserStatusOperations`
   - `WhatsAppInboundService`
3. **Tests.**
   - `FakeIdentityService` (`tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/`) se parte en `FakeSignInService` y en los dobles de repositorio que ya existan.
   - `IdentityServiceTests` pasa a `SignInServiceTests`, y lo que probaba datos se muda a `Persistence/UserRepositoryTests`.
   - Revisar los 20 archivos de integración que nombran `IIdentityService`, sobre todo `Support/IdentityServiceExtensions.cs` y `Support/StaleIdentityReads.cs`.
4. **Una sola semántica para leer una cuenta.** `IdentityService.RequireUserAsync` (con `FindByIdAsync` y un filtro manual de borrados) desaparece y se usa la consulta de `UserRepository`, que respeta el filtro global.
5. **Convención de nombres de repositorios y lectores**, escrita en el spec y aplicada:

   | Prefijo | Qué devuelve |
   |---|---|
   | `Get…` | la entidad seguida por EF, para modificarla; `null` si no existe |
   | `Find…` | una proyección de lectura |
   | `List…` | colecciones o páginas |
   | `Exists…` / `Count…` | escalares |

6. **Blindaje.** Test de arquitectura: ningún tipo de `Application.Services` depende de `Microsoft.AspNetCore.Identity`, y la interfaz `ISignInService` tiene como máximo 12 miembros. El test sirve de alarma para que no vuelva a crecer.

**Puerta:** la general, más `git grep IIdentityService` vacío.

---

## Forma de trabajo desde la Etapa 3

**Decisión del usuario (2026-09-27):** para las etapas que quedan, un modo más liviano que el de las Etapas 1 y 2: un solo diseño con un revisor adversarial (sin el plan detallado paso a paso de `superpowers:writing-plans`), de 6 a 10 tareas por etapa, una revisión por tarea (no por commit), la suite completa solo en la puerta de la etapa y modelos rápidos para lo mecánico (renombres, docs). Se sigue sin ramas ni worktrees propios. Se frena y se avisa al usuario solo si algo cambia comportamiento visible o contradice una decisión que el usuario ya tomó.

---

## Etapa 3: menos ceremonia en servicios y controllers

**Estado:** todas las tareas hechas al 2026-09-28, con el [diseño de la Etapa 3](2026-09-28-etapa-3-servicios.md) (sus 10 tareas, de `5b3c716` a `83e7732`); el 201 de `POST /api/roles`, que había quedado abierto, pasó a la Etapa 4 y se hizo ahí (`44239b3`). **No está cerrada:** la puerta queda pendiente de `dotnet test` completo con Docker. Acá corrieron el build sin advertencias y Domain, Application y Architecture en verde; los tests de integración solo compilan. La lista de riesgo de la sección 10 del diseño es la guía para revisar primero, no la puerta: `Auth/*` y `ExternalLoginTests`; `Users/*`, `Me*` y `UserInvitationEndpointsTests`; `Persistence/*`; `WhatsApp/*`; `Roles/*` y `Settings/*`; `Contracts/*`; `OpenApiTests` y `ErrorHandling`. Al cerrarla se escribe "cerrada" con los desvíos: `Emails/` en lugar de `Email/`, los sufijos `Revoker` y `Recorder`, el orden de las tareas (modelos antes de los cortes) y el corte del bot.

**Objetivo:** que un caso de uso nuevo se escriba en pocas líneas y siempre igual, y que el área de referencia (Etapa 4) ya muestre esos idiomas.

**Pendiente de la revisión final de la Etapa 2 (2026-09-27):** el detalle de usuario (`UserService.LastInvitationAsync`) lee la última invitación con `IUserInvitationRepository.GetLatestAsync`, que trae la entidad seguida por EF, solo para mostrarla. Pasa a una proyección `Find…` en un lector, como pide la convención de nombres del ADR 0008. (El otro pendiente de esa revisión, las 19 dependencias de `AccountService`, está en la tarea 3.)

- [x] **Hecho el 2026-09-28 (`fee16b8`).** `IUserInvitationReader.FindLatestAsync` devuelve `UserInvitationRow` sin seguimiento, con el estado del mensaje saliente en una subconsulta; `LastInvitation.From` lo traduce al estado de entrega (función pura, un test por rama), `UserService` deja de leer `IWhatsAppMessageRepository` y se borró `IUserInvitationRepository.GetLatestAsync`. `Persistence/UserInvitationReaderTests` corre con Docker, en la puerta.

**Pendiente (b) del [diseño de la Etapa 3](2026-09-28-etapa-3-servicios.md), tarea 10:** `IdentityBoundaryTests` no veía `IAuthenticationService.SignInAsync`, que escribe la cookie sin la extensión de `HttpContext`, ni fijaba quién borra una cookie.

- [x] **Hecho el 2026-09-28 (`45a203e`).** `SessionOwners` suma `IAuthenticationService.SignInAsync`, con su caso de control (`AuthenticationServiceWriter`, al pie del archivo), visto en rojo antes de ampliar el detector. Regla nueva, `Only_the_connect_endpoints_and_the_sign_in_service_sign_out`: `HttpContext.SignOutAsync` e `IAuthenticationService.SignOutAsync` los llaman exactamente `ConnectController` (authorize y logout) y `SignInService` (la cookie de Google), con `Assert.Equal` y un caso de control por cada forma. El código ya cumplía: no hubo que ajustar el conjunto del diseño.

### Tareas

1. **Un solo validador inyectable.**
   - `IRequestValidator` en `Application/Common/Validation/`, con `Task<ValidationError?> ValidateAsync<T>(T request, CancellationToken)`. Resuelve los `IValidator<T>` del contenedor y reutiliza la lógica de `ServiceRequestValidator<T>.ValidateAsync`.
   - Reemplaza los N `ServiceRequestValidator<T>` que hoy recibe cada constructor. Solo `AccountService` tiene tres.
   - Test unitario en `ServiceValidationTests`.
   - [x] **Hecho el 2026-09-28 (`c6a28a2`, `cd1b25c`).** `IRequestValidator` (interfaz pública) e implementación interna `RequestValidator` en `Application/Common/Validation/`, con la misma lógica de agrupado y camelCase de `ServiceRequestValidator<T>`, ahora resuelta por scope vía `IServiceProvider.GetServices<IValidator<T>>()`. Los 10 servicios que recibían un `ServiceRequestValidator<T>` por cada tipo de pedido pasan a un único parámetro `IRequestValidator`. `ServiceValidationTests` pasó a `RequestValidatorTests`, con dos casos nuevos (validadores de otro tipo no corren; un validador con dependencia scoped resuelve con `ValidateScopes`), y el doble `TestDoubles/RequestValidators.For(...)` arma el `RequestValidator` real. `DependencyInjectionTests` suma `Every_application_dependency_is_registered` (deriva las interfaces de `Interfaces.Services` y camina el constructor de cada implementación registrada) con su caso de control `Missing_application_service_dependencies_are_detected`, y reemplaza a la vieja lista fija `Application_services_are_registered_explicitly_as_scoped`.
2. **Logging de operación en un solo lugar.**
   - Hoy hay 111 llamadas `LogHandling`, `LogHandled` y `LogFailed` repartidas en 10 servicios, con mensajes distintos.
   - Crear `Application/Common/Logging/OperationLog.cs`: `[LoggerMessage]` compartidos más `static async Task<Result<T>> RunAsync<T>(ILogger, string operation, Func<Task<Result<T>>>)`, que registra el inicio, el fin y el código de error si falló.
   - Los servicios quedan con una línea por método.
   - Mantener la regla: nunca registrar el request, solo el nombre de la operación y el código de error.
   - [x] **Hecho el 2026-09-28 (`d8811a8`, `61e58fc`, `9c3e24a`).** `OperationLog.RunAsync<TResult>(ILogger, string, Func<Task<TResult>>) where TResult : Result` en `Application/Common/Logging/OperationLog.cs`, con los tres `[LoggerMessage]` compartidos; los 10 servicios pasan a una llamada por método y se borran sus `[LoggerMessage]` de operación. `WhatsAppWebhookService` (devuelve `bool`) llama directo a `OperationLog.Handling`/`Handled`. Google y el bot pasan a las operaciones `ExternalSignIn` y `ProcessWhatsAppContact`; los demás nombres de operación no cambian. `OperationLoggingTests` (ArchitectureTests) exige que ningún otro tipo declare un literal "Handling " o "Handled ".
3. **Achicar las fachadas.**
   - La meta: ningún constructor de un servicio de Application con más de 8 dependencias.
   - Hoy `AccountService` tiene 17, `ProfileWhatsAppOperations` 16, `UserService` 14 y `WhatsAppInboundService` 14.
   - Las Etapas 1 y 2 y los puntos 1 y 2 de esta etapa ya bajan varias. Lo que quede se parte por responsabilidad con **interfaz propia** en `Interfaces/Services`: por ejemplo, `IUserAdministrationService` (alta, edición, estado) e `IUserQueryService` (listado, detalle, conteos). Nada de fachadas que solo registran y delegan.
   - Test de arquitectura con el tope de 8.
   - **Pendiente de la revisión final de la Etapa 2 (2026-09-27):** con `ISignInService` ya migrado, `AccountService` quedó con 18 dependencias, no 17. Partirlo entra en esta tarea.
   - **Decisión del usuario (2026-09-27):** el ingreso con Google pone en cero los intentos fallidos al entrar bien, igual que el código y el enlace (`ISignInService.ResetFailedAttemptsAsync` en `ExternalLoginService`), con su test en rojo primero. Hoy no lo hace.
   - [x] **Google pone en cero los intentos, hecho el 2026-09-28 (`db57390`).** `ExternalLoginService` llama a `ResetFailedAttemptsAsync` adentro del límite, después de los rechazos por cuenta inactiva o bloqueada y antes de la auditoría de éxito, como el código y el enlace. Test en rojo primero: `ExternalLoginServiceTests.Google_sign_in_resets_the_failed_attempts`; el de cuenta bloqueada o inactiva fija que los intentos no cambian.
   - [x] **Tope de 8 e ingreso partido, hecho el 2026-09-28 (`90a5430`, `46df57c`).** `ServiceDependencyLimitTests` cuenta el constructor entero de toda clase no estática de `Application.Services`, con un diccionario de excepciones (cada una con su motivo; falla si una ya no hace falta) y un caso de control. `AccountService` e `IAccountService` desaparecen: `LoginMethodsService : ILoginMethodsService` (4), `LoginCodeService : ILoginCodeService` (7), `SignInCodeIssuer` (8), `LoginCodeVerifier` (8), `LoginAuditRecorder` (3), `LoginLinkService` (6) con `LoginLinkVerifier` (6) y `ExternalLoginService` (8). `IssuedLoginCode` suma `LifetimeMinutes` y `ResendCooldownSeconds`. `IdentityBoundaryTests` suma `Only_sign_in_paths_reset_failed_attempts`.
   - [x] **Las fachadas que quedaban (tareas 6 a 8 del [diseño de la Etapa 3](2026-09-28-etapa-3-servicios.md)), hechas el 2026-09-28. Con el bot, la tarea 3 queda completa:** ningún constructor de `Application.Services` pasa de 8 y el diccionario de excepciones de `ServiceDependencyLimitTests` quedó vacío.
     - [x] **Perfil (tarea 6), hecho el 2026-09-28 (`c4b00fc`, `642c3ad`).** Primero, tests de caracterización de confirmar y desvincular el número propio (`ProfileWhatsAppServiceTests`). `MeController` inyecta tres interfaces: `ProfileQueryService : IProfileQueryService` (6), `ProfileService : IProfileService` (8, editar y el correo) y `ProfileWhatsAppService : IProfileWhatsAppService` (8). `ProfileEmailOperations` y `ProfileWhatsAppOperations` desaparecen; `DestinationCodeIssuer` (7) y `DestinationCodeVerifier` quedan en `Services/Auth`, y `PhoneNumberLinker` (6) reemplaza a `PhoneNumberChange`, con la confirmación del número propio. Las tres excepciones del perfil salen de `ServiceDependencyLimitTests`. Desvío del diseño (que daba 6 y 7): `DestinationCodeIssuer` suma `IWhatsAppAvailability` por `EnsureWhatsAppEnabled`, y `PhoneNumberLinker` absorbe `IUserRepository` y `DestinationCodeVerifier` y suma `RemovePhoneAsync` y `ReleaseContactAsync`, que usa solo el perfil, para dejar a `ProfileWhatsAppService` en 8; sigue sin recibir `IUnitOfWork` ni abrir límite, y la administración lo usa solo para los locks y la anulación de enlaces, como antes.
     - [x] **Administración de usuarios (tarea 7), hecho el 2026-09-28 (`958b77f`, `ad84273`).** Primero, tests de precedencia de la edición (`NotFound` > rol inexistente > correo ocupado; `LastAdmin` y `CannotModifySelf` > correo ocupado) y del lock de invitaciones antes de encolar en el alta con WhatsApp, probados con mutaciones. `UsersController` inyecta tres interfaces con las mismas rutas: `UserQueryService : IUserQueryService` (5), `UserAdministrationService : IUserAdministrationService` (8, alta, edición e invitación) y `UserAccessService : IUserAccessService` (8, activar o desactivar, borrar y desvincular el número). `UserService`, `UserWriteOperations`, `UserStatusOperations` y `UserPhoneOperations` desaparecen; `UserContactLinker` (6) absorbe a `UserContactParser` y expone los pasos que el servicio intercala en el orden de antes, `UserInvitationIssuer` (7) reemplaza a `UserInvitationSender` con `Check`, `LockAsync`, `WaitBeforeAnotherAsync` y `SendAsync` (que conserva su lock), `WhatsAppInvitationIssuer` (4, `Services/WhatsApp`) encola la plantilla, y `UserGuard` (3) reemplaza a `UserGuards` y suma `EnsureRolesExistAsync`. Las tres excepciones de usuarios salen de `ServiceDependencyLimitTests`. Desvíos del diseño: `UserAccessService` queda en 8 y no en 7 (lector, repositorio, guarda, lock de enlaces, `PhoneNumberLinker`, revocador, unidad de trabajo y logger), `UserQueryService` en 5 y `UserInvitationIssuer` en 7, sin logger (el aviso de la cola llena pasó a `WhatsAppInvitationIssuer`); y la administración pasa a quitar el número y soltar el contacto con `PhoneNumberLinker.RemovePhoneAsync` y `ReleaseContactAsync`, que delegan igual en el repositorio y en `WhatsAppContactLinker`, en lugar de llamarlos directo.
     - [x] **El bot (tarea 8), hecho el 2026-09-28 (`5be7298`, `16eb9fb`).** Primero, tests de caracterización de dos ramas sin cuenta que no tenían test (`WhatsAppInboundServiceTests`: «Ya tengo cuenta» lleva a la web también con el registro solo por invitación, y sin origen público "Ir a la web" lanza y no se guarda nada), probados con mutaciones. `WhatsAppInboundService` baja de 16 a 8 dependencias (contactos, mensajes, `IPhoneNumberParser`, `WhatsAppReplyPolicy`, la cola, la unidad de trabajo, la hora y el logger): toma el contacto con su lock, elige el mensaje que decide, marca los procesados, encola y abre el único límite (`OnSuccess`, operación `ProcessWhatsAppContact`). `WhatsAppReplyPolicy` (7) busca la cuenta (contacto vinculado → número, lock y relectura) y decide la respuesta; `WhatsAppLinkIssuer` (5) emite el enlace, vincula el contacto, verifica el número y crea la cuenta de «Crear cuenta». Los tests del bot (34 métodos y 40 casos; 36 y 42 con los dos nuevos) cambian solo la construcción en el test host, con las mismas 167 aserciones. Desvíos del diseño (que daba 6 y 5): `WhatsAppReplyPolicy` suma `IPublicOrigin`, porque "Ir a la web" («Ya tengo cuenta» y sin invitación) es suyo, y `WhatsAppLinkIssuer` no lo necesita (la URL del enlace la arma `LoginLinkIssuer`) pero suma su logger: el log del alta desde el chat, con el mismo texto y nivel, pasa a la categoría `WhatsAppLinkIssuer`, como el aviso de la cola llena de la tarea 7.
4. **Convención de helpers.**
   - Las piezas internas de un área que no son servicios van en `Services/<Área>/`, son `internal sealed` y usan **un sufijo por rol**, documentado en el spec:

     | Sufijo | Para qué |
     |---|---|
     | `Policy` | decide |
     | `Guard` | protege una regla |
     | `Issuer` | emite |
     | `Verifier` | verifica |
     | `Linker` | vincula |

   - `Operations` y `Change` desaparecen. `DestinationCodeVerifier` se muda a `Services/Auth/`.
   - [x] **Hecho el 2026-09-28 (`92c6e80`, tarea 9 del [diseño de la Etapa 3](2026-09-28-etapa-3-servicios.md)).** `Operations` y `Change` ya habían desaparecido en las tareas 5 a 8, que partieron los servicios con su sufijo final desde el vamos, y `DestinationCodeVerifier` ya quedó en `Services/Auth` en la tarea 6 (perfil). Esta tarea deja escrita la regla, con la tabla ampliada a `Revoker` (revoca; `AccountAccessRevoker`) y `Recorder` (registra; `LoginAuditRecorder`) más allá de las cinco de arriba, en `backend.md` ("Convención de sufijos de los helpers") y su test, `ApplicationHelpersTests` (`tests/ArquitecturaBase.Application.UnitTests`): filtra los descriptores de `AddApplication().AddWhatsAppWebhookApplicationServices()` que no implementan `Interfaces.Services`, y exige `internal sealed`, `Services/<Área>` y un sufijo de la tabla, con dos casos de control (un helper conocido y un tipo en memoria con un sufijo fuera de la tabla). Los 18 helpers de hoy ya cumplían la convención; no hizo falta renombrar ninguno.
5. **Modelos sin duplicados.**
   - Borrar `Models/Users/ReadModels/UserDetail.cs` y `UserListItem.cs`, o renombrarlos a `UserDetailRow` y `UserListRow` si la proyección del lector difiere de la salida.
   - `UserListRequest` (en `Models/Identity`) y `ListUsersRequest` (en `Models/Users`) se unifican en `Models/Users`.
   - Regla escrita: la salida de un servicio termina en `Response`; la proyección de un lector, en `Row`.
   - [x] **Hecho el 2026-09-28 (`b1aeb10`).** Se renombraron las proyecciones (`UserDetailRow`, `UserListRow`, `RoleRow`, sin `ReadModels/` ni el `FormattedPhoneNumber` que el lector no llenaba) y las salidas (`UserDetailResponse`, `UserListItemResponse`, `PermissionGroupResponse`, `ConnectUserResponse`); `ListUsersRequest` absorbió `UserListRequest` y sirve también a los conteos, con un solo validador, y `UserFilterCounts` pasó a `Models/Users`. La regla está en `backend.md` ("Modelos: Response y Row") y la fija `ServiceOutputNamingTests`; el JSON no cambia, solo los nombres de esquema del OpenAPI.
6. **Contratos HTTP según D2.**
   - `MeController`, `SettingsController`, `LoginCodeController` y `LoginLinkController` pasan a recibir contratos de `Api/Contracts/<Área>` con mapeo manual.
   - Los `ToString()` que ocultan datos personales se mudan a esos contratos.
   - Test de arquitectura: los parámetros `[FromBody]` y `[FromQuery]` de un controller son tipos de `Api.Contracts`.
   - [x] **Hecho el 2026-09-26 (`e92d354`).** Contratos `*HttpRequest` nuevos en `Contracts/Auth`, `Contracts/Settings` y `Contracts/Users` (Me va con Users, como sus modelos), con los mismos campos, así que el JSON y los `errors` no cambian. Los `ToString()` se quitaron de los 10 modelos de Application y también de `ExternalSignInRequest`; sus tests pasaron a los de integración de cada ruta. `ControllerInputContractTests` cuenta también el cuerpo que `[ApiController]` infiere y admite valores simples. Único cambio visible: los esquemas del OpenAPI llevan el nombre del contrato, y el front no los usa.
7. **Helpers HTTP.**
   - Crear `[HasPermission(Permissions.Users.Read)]` en `Api/Authorization/HasPermissionAttribute.cs`, que reemplaza las 15 apariciones de `[Authorize(Policy = PermissionPolicyProvider.PolicyPrefix + …)]`.
   - Agregar `ToAcceptedResult` y `ToCreatedResult(actionName, routeValues)` en `ControllerResultExtensions.cs`.
   - El 202 se arma a mano en 4 lugares: `LoginCodeController.cs:16-19,32-35` y `MeController.cs:24-26,46-48`.
   - Pasar las altas a 201 **solo después de revisar el front**: buscar en `../ArquitecturaBaseFront/src` los `POST` a `/api/users` y `/api/roles` y confirmar que tratan cualquier 2xx como éxito.
   - Test de integración: un alta devuelve 201 con `Location`.
   - [x] **`HasPermission`, hecho el 2026-09-26 (`27c7d71`).** Eran 16 apariciones, no 15. `PermissionAuthorizationTests` rechaza `[Authorize(Policy = "permission:…")]` en cualquier controller o acción.
   - [x] **Helpers y 201 de usuarios, hecho el 2026-09-26 (`e8e889d`).** `ToAcceptedResult`, con y sin valor, y `ToCreatedResult`. El 202 a mano estaba en 5 lugares: también en `UsersController.SendInvitation`. `POST /api/users` responde 201 con `Location` a `/api/users/{id}`, también al restaurar una cuenta; el front (`src/shared/api/httpClient.ts`) trata cualquier 2xx como éxito. Test: `CreateUserEndpointTests.The_location_of_a_new_user_leads_to_its_detail`.
   - [x] **Pendiente:** `POST /api/roles` sigue en 200 porque no hay `GET /api/roles/{id}` al que apunte el `Location`. Pasa a la Etapa 4, tarea 1. **Hecho el 2026-09-28 (`44239b3`)** (tarea 2 del [diseño de la Etapa 4](2026-09-28-etapa-4-area-de-referencia.md)): responde 201 con `Location` a `/api/roles/{id}`.
8. **ProblemDetails centralizado.**
   - El `traceId` se calcula en un solo lugar (`ProblemDetailsMapper`); hoy está en `DependencyInjection.cs:32`, `ControllerResultExtensions.cs:26` y `MvcInvalidModelStateResponseFactory.cs:13`.
   - Las opciones JSON se configuran con un solo `ConfigureJson` (`DependencyInjection.cs:47-62`).
   - Documentar que el 400 de un body ilegible no trae `errors` a propósito, porque no debe revelar la forma interna.
   - [x] **Hecho el 2026-09-26 (`56c2caa`).** `ProblemDetailsMapper.AddTraceId` es el único que calcula el `traceId`. Un error de controller se arma con `FromError(error, factory, httpContext)` y la fábrica de MVC, así conserva el `type` y pasa por `CustomizeProblemDetails`. `ConfigureJson` lo usan MVC y `Http.Json`, y `JsonOptionsTests` verifica que coincidan. El porqué del 400 sin `errors` quedó en `MvcInvalidModelStateResponseFactory` y en el spec. Las líneas citadas arriba ya estaban corridas.
   - [x] **Por decidir:** el `FromError(Error)` de un solo argumento ya solo lo usa `ProblemDetailsMapperTests`. Se puede borrar si esos tests pasan a la sobrecarga de tres argumentos. **Borrado el 2026-09-28 (`64926e6`, tarea 10 del [diseño de la Etapa 3](2026-09-28-etapa-3-servicios.md)).** `ProblemDetailsMapperTests` usa la sobrecarga de tres argumentos con la fábrica de `new ServiceCollection().AddControllers()` y un `DefaultHttpContext`, y sigue corriendo sin Docker. Como esa sobrecarga siempre pone el `traceId`, `Metadata_cannot_override_reserved_extensions` pasó de exigir que no estuviera a exigir que no fuera el `"fake"` de la metadata.
9. **Carpetas.**
   - `Api/Services` → `Api/RequestContext`, como pide el spec.
   - `Application/Interfaces/Integrations` se divide en subcarpetas: `Identity/`, `Security/`, `Email/`, `WhatsApp/`, `Request/`.
   - `IWhatsAppWebhookPersistence` queda en `Interfaces/Services`: es un punto de entrada que abre su propio límite y que `IWhatsAppWebhookRetry` vuelve a correr en un scope nuevo (Etapa 1).
   - [x] **`Api/RequestContext`, hecho el 2026-09-26 (`1153b81`).** `ApiRequestContextTests` exige que no exista `Api.Services` y que todo tipo de Api que recibe `IHttpContextAccessor` viva en `Api.RequestContext`.
   - [x] **Subcarpetas de `Integrations`, hecho el 2026-09-28 (`5b3c716`).** `Identity/` (ISignInService, IPermissionService, IOpenIddictTokenRevoker, IGoogleAvailability, IInitialAdmin, IPublicOrigin), `Security/` (ILoginCodeGenerator, ILoginCodeHasher, ISecureTokenGenerator), `WhatsApp/` (los seis `IWhatsAppWebhook*`/`IWhatsApp*`), `Request/` (ICurrentUser, IRequestInfo, con sus `using` actualizados en `Api/RequestContext/` y en `Api/DependencyInjection.cs`) y una sexta carpeta no listada acá, `Phones/` (IPhoneNumberParser, que no es de WhatsApp). Desvío: la carpeta quedó en `Emails/` y no `Email/`, porque un namespace `…Integrations.Email` tapa al value object `Email` en las carpetas hermanas y rompe el build con `CS0118` (por ejemplo, `IInitialAdmin.IsInitialAdmin(Email email)`). `IWhatsAppWebhookPersistence` no se movió: sigue en `Interfaces/Services`, como dejó la Etapa 1.
10. **OpenAPI.**
    - Convención global de respuestas de error (`ProblemDetails` para 400, 401, 403, 404 y 500).
    - `ProducesResponseType` de éxito en todas las acciones.
    - Test: el documento `/openapi/v1.json` declara un esquema de respuesta para cada operación.
    - [x] **Hecho el 2026-09-26 (`487c3a0`).** `ProblemResponsesConvention` (`Api/OpenApi`, registrada en `AddOpenApiDocumentation()`) deduce los errores de la firma de cada acción, y lo que la firma no muestra se declara con `[ProducesProblem(status)]`. Un `[Authorize]` sin permiso no declara 403, porque nunca lo responde. `ConnectController`, `WhatsAppWebhookController` y `ExternalLoginController` quedan fuera (`OwnProtocolControllers`). `OpenApiTests` exige además el 500 y que todo error sea ProblemDetails; el único 2xx sin esquema aparte de los 204 es el 202 de `POST /api/users/{id}/invitation`.
    - [x] **Sin declarar todavía:** los 409 de conflicto de usuarios y roles, los 429 de las rutas con rate limit y los 403 de acciones anónimas (`Auth.Account.Disabled` y `NotInvited` en el verify y el canje del enlace). Se pueden sumar con `[ProducesProblem]` por acción o con una regla de 429 en la convención. **Declarados el 2026-09-28 (`83e7732`, tarea 10 del [diseño de la Etapa 3](2026-09-28-etapa-3-servicios.md)).** `ProblemResponsesConvention` deduce el 429 de `[EnableRateLimiting]` (gana el atributo más cercano, como en la metadata del endpoint): el pedido, el verify y el pedido por WhatsApp del código, la vista previa y el canje del enlace, y los códigos y las confirmaciones del correo y del número del perfil. `[ProducesProblem(409)]` va en toda acción cuyo servicio devuelve un `Error.Conflict`, no solo en las altas y ediciones: `POST` y `PUT /api/users…`, desactivar, borrar y desvincular el número de un usuario; alta, edición y baja de roles (nombre repetido, rol con usuarios); y en el perfil, confirmar el correo o el número y desvincular el número propio. Activar un usuario no lo declara, porque no lo responde. `[ProducesProblem(403)]` va en `POST /account/login-code/verify` y `POST /account/login-link/redeem`; la vista previa del enlace no mira la cuenta y no lo declara. Además, `POST /api/users/{id}/invitation` declara a mano su 429 (la espera entre invitaciones, sin rate limit). `OpenApiTests` suma `Conflicts_rate_limits_and_refusals_of_anonymous_actions_are_declared` y ajusta los casos de `/account/login-code` (429) y `POST /api/users` (409); corre con Docker, en la puerta.

**Puerta:** la general, más los tests de arquitectura nuevos (tope de dependencias, contratos, `HasPermission`). Contratos y `HasPermission` ya están (`ControllerInputContractTests`, `PermissionAuthorizationTests`); el tope de dependencias de la tarea 3 también (`ServiceDependencyLimitTests`, sin excepciones).

---

## Etapa 4: área de referencia y receta

**Objetivo:** que agregar un área nueva sea seguir una lista y copiar un ejemplo real. Decisión D4.

**Diseño de ejecución:** [2026-09-28-etapa-4-area-de-referencia.md](2026-09-28-etapa-4-area-de-referencia.md), en 7 tareas. **Decisión del usuario (2026-09-28):** `GET /api/roles` sigue igual, como catálogo completo para los selectores del front, y el listado paginado de referencia va en una ruta nueva, `GET /api/roles/paged`, en vez de una query opcional en la misma ruta (tarea 1 de abajo), porque una operación con dos esquemas de respuesta no se puede declarar en el OpenAPI.

**Estado:** todas las tareas hechas en código al 2026-09-28, con el [diseño de la Etapa 4](2026-09-28-etapa-4-area-de-referencia.md) (sus 7 tareas, de `692f3a3` a la corrección de la guía). **No está cerrada:**
- **La puerta general** queda pendiente de `dotnet test` completo con Docker, junto con la de la Etapa 3. Acá corrieron el build sin advertencias y Domain, Application y Architecture en verde; los tests de integración solo compilan. Lista de riesgo para esa corrida (tarea 7 del diseño): `Roles/*`, `Persistence/RoleReaderTests`, `Persistence/RoleRepositoryTransactionTests`, `Contracts/*`, `OpenApiTests`, `PaginationTests`, `Users/*` (por `LikePatterns` en `UserReader`) y todo lo que usa `/test/widgets`.
- **La puerta propia** ("la prueba de la receta sin preguntas sin respuesta"): **cumplida el 2026-09-28.** La primera prueba (tarea 4) dejó dudas y huecos, que se corrigieron en la guía (`2883d0f`). Una segunda prueba con otro subagente sin contexto, sobre la guía corregida, agregó `Tags` sin errores de la guía: compila sin advertencias, Domain, Application y Architecture en verde (758 tests) y la lista de verificación completa. Sus cuatro dudas las resolvió con lo que la guía dice. La auditoría de esa segunda área no encontró ninguno de los 7 defectos de la primera; dejó dos descuidos menores en tests y dos huecos chicos de la guía (sumar el documento del área a las listas de `AGENTS.md` y `CLAUDE.md`, y el bloque de Swagger de `OpenApiTests`), que se corrigieron en la guía. Las dos áreas de prueba se descartaron sin commitear.

**Pendientes del front** (`../ArquitecturaBaseFront`, tarea 7 del diseño):
- el comentario viejo de `features/roles/api/roles.ts:36`, que todavía describe el 200 del alta;
- `RoleEditorPage.tsx:176-189` puede usar `GET /api/roles/{id}` en lugar de buscar el rol en la lista;
- `RolesPage` puede pasar al listado paginado (`GET /api/roles/paged`) más adelante.

### Tareas

1. **Completar Roles como referencia.** `GET /api/roles` pasa a paginado con `PagedRequest`, `SortableFields`, `ApplySort` y `ToPagedResultAsync`. Se agrega `GET /api/roles/{id}` si no existe. Tiene que quedar con todas las piezas:
   - entidad (vía Identity), configuración EF;
   - `RoleErrors` y claves en `Errors.resx` / `.en.resx`;
   - permisos;
   - `IRoleRepository`, `IRoleReader` y sus implementaciones;
   - `IRoleService` y `RoleService`;
   - modelos `*Request` / `*Response` / `*Row` y validadores;
   - `RolesController` y contratos;
   - tests en los tres niveles: unitario del servicio, integración de rutas y de lector.

   Actualizar el inventario de rutas y avisar al front si cambia la forma de `GET /api/roles`. Si el front no pagina, mantener la forma y agregar el paginado como query opcional.

   Con `GET /api/roles/{id}` llega el 201 de `POST /api/roles`, con `Location` a ese GET (pendiente de la tarea 7 de la Etapa 3): la acción pasa a `ToCreatedResult` y a `[ProducesResponseType<Guid>(StatusCodes.Status201Created)]`, se ajustan `RoleCrudEndpointsTests` y `OpenApiTests`, se saca el comentario de `RolesController.Create` y, antes, se revisa el alta de roles del front.
   - [x] **`GET /api/roles/{id}`, hecho el 2026-09-28 (`692f3a3`)** (tarea 1 del [diseño](2026-09-28-etapa-4-area-de-referencia.md)). El detalle de un rol, con la misma forma que un ítem del catálogo (`RoleService.GetRoleAsync`, que comparte el mapeo `ToResponse` con `GetRolesAsync`); un id inexistente responde 404 con `Roles.Role.NotFound`. De paso, `IRoleReader.FindRoleAsync` pasó a `FindByIdAsync`, como pedía el ADR 0008, que ya no la lista entre sus excepciones. El inventario de rutas queda en 42.
   - [x] **201 de `POST /api/roles`, hecho el 2026-09-28 (`44239b3`)** (tarea 2 del diseño). La acción pasa a `ToCreatedResult` con `Location` a `GET /api/roles/{id}` y declara `[ProducesResponseType<Guid>(StatusCodes.Status201Created)]`; el cuerpo sigue siendo el id. El front ya trata cualquier 2xx como éxito (`createRole` descarta el valor); queda como pendiente del front el comentario viejo de `features/roles/api/roles.ts:36`. Test: `RoleCrudEndpointsTests.The_location_of_a_new_role_leads_to_its_detail`.
   - [x] **Listado paginado de roles, hecho el 2026-09-28 (`031399e`, `896048e`)** (tarea 3 del diseño). **Desvío, por la decisión del usuario:** `GET /api/roles` no pasa a paginado ni suma una query opcional; sigue igual, como catálogo completo para los selectores (`IRoleReader.ListAllRolesAsync`), y el listado va en `GET /api/roles/paged` (`ListRolesRequest` con `SortableFields` `name` y `createdAtUtc`, `ListRolesRequestValidator`, `IRoleReader.ListRolesAsync` con `ApplySort` y `ToPagedResultAsync`, `IRoleService.ListRolesAsync`, `RolesController.ListPaged`). Busca en el nombre y la descripción con `LikePatterns`, extraído de `UserReader`. `RoleReader` comparte una sola proyección entre catálogo, detalle y página; el SQL del catálogo y del detalle no cambió. Enmienda en el [ADR 0004](../decisions/0004-roles-como-area-de-referencia.md). El inventario de rutas queda en 43. Los tests de integración (`RoleReaderTests`, `RolesEndpointsTests`, `OpenApiTests`, los contratos y el inventario) corren con Docker, en la puerta.
2. **`docs/guides/agregar-un-area.md`.** La receta en orden, con la ruta de cada archivo y un enlace al archivo equivalente de Roles:

   | Paso | Pieza |
   |---|---|
   | 1 | entidad en Domain |
   | 2 | `IEntityTypeConfiguration` |
   | 3 | migración (comando de `docs/architecture/backend.md`, "Migraciones") |
   | 4 | `<Entidad>Errors` y claves en los dos `.resx` |
   | 5 | permiso: los tres pasos de `AGENTS.md` |
   | 6 | interfaces de repositorio y lector en `Application/Interfaces/Persistence` |
   | 7 | sus implementaciones en `Infrastructure/Persistence/{Repositories,Readers}` y su registro |
   | 8 | modelos y validadores |
   | 9 | interfaz de servicio y servicio |
   | 10 | registro en `Application/DependencyInjection.cs` |
   | 11 | contratos y controller |
   | 12 | tests |
   | 13 | inventario de rutas |
   | 14 | prefijo de backend, si la ruta no empieza con `/api` (los tres lugares de `AGENTS.md`, "Front") |

   Al final, una lista de verificación.
   - [x] **Hecho el 2026-09-28** (tarea 5 del [diseño](2026-09-28-etapa-4-area-de-referencia.md)): [`docs/guides/agregar-un-area.md`](../guides/agregar-un-area.md), con los catorce pasos, el archivo real a copiar en cada uno (`SystemSettings`, `SystemSettingsRepository` y `Widget` donde Roles no sirve), su trampa y su test; la regla del listado (un área nueva pagina en `GET /api/<recurso>`), la instalación de `dotnet-ef`, las listas fijas de un permiso nuevo, el cuarto lugar de un prefijo (el filtro de `ExplicitRouteInventoryTests`), la lista de verificación y lo que solo se ve con Docker. Enlazada desde `AGENTS.md` (`8c3d4a2`). De paso, `backend.md` ("Front y hosting del SPA") suma `/webhooks` a los prefijos que reenvía Vite. Probada con un subagente y corregida con lo que salió de la prueba (tarea 4).
3. **Rehacer `TestFeatures/Widgets` con el camino canónico.** Los archivos `CreateWidget.cs`, `GetWidgets.cs` y `GetWidgetById.cs` tienen nombres de handler y se reemplazan por `WidgetsTestController` y `WidgetTestService`, o se borra lo que ya no use ningún test.
   - [x] **Hecho el 2026-09-28 (`c352164`)** (tarea 4 del diseño).
4. **Probar la receta.** Un subagente sin contexto sigue la guía y agrega un área de prueba, por ejemplo `Tags` (nombre y descripción, CRUD completo), en una rama descartable o sin commitear. Se mide cuántas preguntas tuvo que hacer y qué archivo no encontró, se corrige la guía y se descarta el área.
   - [x] **Hecho el 2026-09-28** (tareas 6 y 7 del [diseño](2026-09-28-etapa-4-area-de-referencia.md)). Un subagente sin contexto agregó `Tags` (entidad con borrado lógico, nombre único, CRUD, permisos y migración) sin commitear; después se descartó con `git stash` y el parche quedó fuera del repo. Métricas:
     - **cero errores de compilación siguiendo la guía:** build sin advertencias, Domain (149), Application (525) y Architecture (80) en verde, `has-pending-model-changes` sin cambios; ninguna regla de arquitectura rota. La integración solo compiló;
     - **4 dudas** del subagente: si el área necesitaba `docs/features/` y los `AGENTS.md` de carpeta; cómo verificar un nombre único sin `RoleManager`; un método de entidad por propiedad o uno por edición; dónde va un área nueva en `Permissions.All`;
     - **2 piezas no encontradas:** una igualdad sin distinguir mayúsculas (agregó un `LikePatterns.Exact` con `ILike`) y una entidad de referencia con borrado lógico, nombre único y repositorio y lector propios;
     - **0 errores de la guía** según el subagente, pero la revisión a mano encontró **7 defectos** en el área, 5 por huecos o ambigüedades de la guía: el índice único sobre `Name` y la verificación con `ILike` no comparaban igual (grave: dos nombres que solo difieren en mayúsculas podían quedar y bloquear la edición); nada decía que una carrera responde 500; `OpenApiTests` sin el `post` ni el `delete`; faltaba la teoría de los cuerpos JSON; una aserción sobre un listado sin `search`; faltaba el 409 con otras mayúsculas por HTTP; y menores (el 404 del `PUT`, `CreatedAtUtc` ordenable pero ausente de la respuesta, la documentación del área).

     La guía suma la sección "Nombre único sin distinguir mayúsculas" (columna `NormalizedName`, índice único filtrado, igualdad en el lector, la carrera y el lock opcional, y sus tests), las cinco líneas de `OpenApiTests`, la teoría de los cuerpos JSON, `search` en toda aserción de un listado, el 404 de las tres rutas con `{id}`, los campos ordenables en el `Response`, cuándo un área tiene reglas propias (y el controller siempre en `Api/Controllers/AGENTS.md`), el lugar de un área nueva en `Permissions.All` y qué quiere decir "un método por cada cambio". La segunda prueba la confirmó, ver el estado de la etapa.

**Puerta:** la general, más la prueba de la receta sin preguntas sin respuesta.

---

## Etapa 5: documentación en capas para humanos e IA

**Objetivo:** que un agente cargue en cada sesión solo las reglas de la plantilla y lea lo específico de un área solo cuando la toca.

### Estructura destino

```text
AGENTS.md                      ← índice corto (~80 líneas): capas, flujo, Result, errores, UTC, i18n, build, tests, dónde va cada cosa
CLAUDE.md                      ← `@AGENTS.md` + lo específico de Claude (worktrees, el skill de los planes)
docs/architecture/backend.md   ← el spec canónico actual, sin nombres del producto
docs/guides/agregar-un-area.md
docs/guides/permiso-nuevo.md, migracion.md, prefijo-de-backend.md, quitar-whatsapp.md
docs/features/identidad.md     ← hoy CLAUDE.md "Identidad"
docs/features/whatsapp.md      ← hoy CLAUDE.md "WhatsApp" y su tabla de configuración
docs/features/administracion.md← hoy CLAUDE.md "Administración (Fase 4)" y los filtros/conteos de usuarios
docs/decisions/NNNN-*.md       ← D1 a D7 de este plan, una por archivo (ADR corto: contexto, decisión, consecuencias)
docs/specs/                    ← los diseños funcionales vigentes (inicial, Fase 4, WhatsApp); lo estructural lo rige backend.md
docs/history/                  ← los planes cerrados, marcados HISTÓRICO en la Etapa 0
src/**/<carpeta de un área>/AGENTS.md ← una línea: "Antes de tocar esto, leé docs/features/<área>.md"; su CLAUDE.md es `@AGENTS.md`
```

### Tareas

1. Mover el contenido **sin reescribir las reglas**: cada regla funcional de hoy tiene que seguir existiendo en algún archivo. Verificarlo con una lista de las reglas de `CLAUDE.md` antes y después.
2. Sacar de `AGENTS.md` el detalle de la migración ("41 combinaciones…") y reemplazarlo por la regla general: "cada ruta figura en el inventario y tiene su test".
3. `docs/architecture/backend.md` suma lo que definieron las Etapas 1 a 3: transacciones, convención de nombres, helpers, contratos, modelos, `HasPermission` y los helpers de resultado.
4. `README.md`: cómo levantar, cómo probar, mapa de docs. El resto va por enlace.

**Avance:**
- [x] **ADR** (2026-09-26, adelantado mientras corría la Etapa 1). `docs/decisions/README.md` (qué es un ADR acá, formato e índice) y `0001` a `0007`, uno por decisión D1 a D7. La `0003` cita el commit `cbff737`.
- [x] **Históricos** (2026-09-26). Los 10 planes con cabecera HISTÓRICO (del 2026-09-18 al 2026-09-23) y `contracts/` se mudaron a `docs/history/plans/`, con los enlaces ajustados. En `docs/plans/` quedan este plan y los de las etapas en curso: el plan de una etapa cerrada se muda también, con su cabecera HISTÓRICO (el de la Etapa 1, el 2026-09-27).
- [x] **Tarea 1** (2026-09-27). `AGENTS.md` es el índice de la plantilla, y `CLAUDE.md` lo importa (`@AGENTS.md`) y suma solo lo de Claude Code. Lo de cada área está en `docs/features/{identidad,whatsapp,administracion}.md`, y el spec canónico se mudó a `docs/architecture/backend.md`, donde sumó las reglas de detalle (errores del framework, guardado, migraciones, front y tests). La lista de antes y después fue un inventario de 318 reglas de `CLAUDE.md`, `AGENTS.md` y el spec, tomado en `97789f2` antes de mover nada y contrastado fila por fila con los archivos nuevos. Ninguna se perdió ni cambió de sentido. Cambian a propósito cuatro: dónde está la arquitectura canónica, que `CLAUDE.md` importe `AGENTS.md`, la regla de las 41 combinaciones (tarea 2) y las puertas de cierre de la migración, que quedan en su plan histórico. En el cierre, `aspire stop` y su motivo subieron a `AGENTS.md`, porque valen para cualquier agente. También subió la regla general de los logs. Las claves de los locks pasaron a `backend.md`; los códigos por destino y la regla de no enumerar cuentas, a `identidad.md`, y lo que carga un administrador, a `administracion.md`. Ninguna de esas reglas es solo de WhatsApp, y en `whatsapp.md` queda el enlace.
- [x] **Tarea 2.** `AGENTS.md` dice "cada ruta figura en el inventario (`ExplicitRouteInventoryTests`) y tiene su test". Las 41 combinaciones quedan en `backend.md` ("Excepciones de protocolo y conservación funcional") y en el plan histórico de la migración.
- [ ] **Tarea 3, parcial.** `backend.md` ya tiene lo de la Etapa 1: una sola forma de guardar, las cinco reglas, los helpers y la excepción. También tiene lo que se adelantó de la Etapa 3: el borde HTTP, con los contratos, `HasPermission`, los helpers de resultado y OpenAPI. La convención de nombres la sumó la Etapa 2 ("Nombres de repositorios y lectores" y el ADR 0008). Los modelos ya están: "Modelos: Response y Row" (tarea 4 del [diseño de la Etapa 3](2026-09-28-etapa-3-servicios.md), `b1aeb10`) y la tabla de sufijos de los helpers, ampliada a siete con `Revoker` y `Recorder` (tarea 9 del mismo diseño, `92c6e80`). `backend.md` todavía nombra piezas del producto (servicios, controllers y WhatsApp); sacarlas es de la Etapa 6.
- [x] **Estructura destino**, salvo `docs/guides/`. Las carpetas de código de cada área tienen un `AGENTS.md` de una línea con un `CLAUDE.md` al lado que lo importa: `Api/Controllers` tiene un índice de cuatro líneas por controller, y `Services/Users`, `Services/Auth`, `Models/Identity` y `Domain/Authentication` nombran dos documentos. Los specs quedan en `docs/specs/` como diseños funcionales vigentes (ver la tabla).
- [ ] **`docs/guides/`, parcial** (`agregar-un-area.md`, `permiso-nuevo.md`, `migracion.md`, `prefijo-de-backend.md` y `quitar-whatsapp.md`). La receta de un área sale del área de ejemplo de la Etapa 4, y `quitar-whatsapp.md`, de la Etapa 6. **Hecho el 2026-09-28:** [`agregar-un-area.md`](../guides/agregar-un-area.md) (Etapa 4, tarea 2), enlazada desde `AGENTS.md`; sus pasos 3, 5 y 14 ya cubren la migración, el permiso nuevo y el prefijo, y pueden ser la base de las otras tres. Mientras tanto, los pasos del permiso nuevo y del prefijo de backend siguen en `AGENTS.md`, y el comando de la migración, en `backend.md`. Cuando existan las guías, `AGENTS.md` los cambia por un enlace y baja hacia las ~80 líneas: hoy tiene 125.
- [x] **Fuera de este repo:** `../ArquitecturaBaseFront` apunta a `docs/history/plans/` desde su commit `47406b4`.
- [ ] **`README.md`** (tarea 4). El mapa de la documentación, que se había hecho el 2026-09-26, quedó viejo con la mudanza, y `:14` y `:333` enlazan el spec que ya no está en `docs/specs/`. **Bloqueado:** `README.md` tiene cambios del usuario sin commitear. Cuando se pueda:
  - en `:333`, el enlace pasa a `docs/architecture/backend.md`;
  - en el mapa, `AGENTS.md` pasa a ser "las reglas de la plantilla para cualquier agente y el índice del resto" y `CLAUDE.md`, "lo propio de Claude Code; importa `AGENTS.md`";
  - la fila `:14` se parte en tres: `docs/architecture/` (la arquitectura canónica), `docs/features/` (las reglas de cada área) y `docs/specs/` (los diseños funcionales);
  - en `:350`, las convenciones "en CLAUDE.md" pasan a `docs/features/`, y `CLAUDE.md` queda solo para lo de Claude Code.

  Falta además el resto de la tarea 4: cómo levantar y cómo probar, con lo demás por enlace.
- [ ] **Fuera de los documentos:** el comentario de `tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginLinkTests.cs:264` cita "(CLAUDE.md, Fase 4)", y esa regla ahora está en `docs/features/administracion.md`. Se corrige en el próximo commit que toque ese test.
- [ ] **Repetir la puerta a ciegas** (ver abajo).

**Puerta:** la general, más la comprobación de que ninguna regla se perdió en la mudanza. Un agente nuevo lee solo `AGENTS.md` y contesta bien diez preguntas del tipo "¿dónde va X?". Las preguntas se escriben antes de reorganizar.

**Preguntas de la puerta** (escritas el 2026-09-27, sobre `97789f2`, antes de mover nada). Cómo se toma:
- un agente sin contexto recibe solo `AGENTS.md` y la columna de preguntas, con la instrucción "contestá leyendo `AGENTS.md` y lo que enlaza; no busques en el código";
- no carga `CLAUDE.md` por su cuenta: si lo necesita, llega por un enlace, que es lo que ve Codex;
- se anota qué archivos abrió;
- una respuesta pasa si dice todo lo de su fila;
- si falta algo, se corrige el índice o el enlace, no la pregunta.

| # | Pregunta | Respuesta esperada | Dónde la encuentra hoy |
|---|---|---|---|
| 1 | ¿Dónde va un repositorio nuevo? | el contrato `I*Repository` o `I*Reader`, en `Application/Interfaces/Persistence`; la implementación, en `Infrastructure/Persistence/Repositories` (escrituras) o `Readers` (lecturas); no hay uno genérico ni uno por entidad; lo usa un servicio, nunca un controller | `AGENTS.md`, "Persistencia" y "Dónde va cada cosa" |
| 2 | ¿Cómo se guarda en un caso de uso? | con `ExecuteInTransactionAsync`, una vez por cada método público que escribe; `OnSuccess`, u `OnAnyResult` cuando también hay que guardar al fallar; el validador, antes y afuera; la caché y `Notify`, después y solo si se confirmó; los helpers no reciben `IUnitOfWork` y no se anida; el ejemplo es `RoleService.UpdateAsync` | `AGENTS.md`, "Una sola forma de guardar", que lleva a `backend.md` |
| 3 | ¿Cómo se declara un permiso nuevo? | en `Permissions.cs` y `Permissions.All`; `Permission.<código>` y `PermissionDescription.<código>` en los dos `.resx`; el seed se lo da a Admin; `InvalidateRoleAsync`; la ruta usa `[HasPermission]` (401 sin sesión, 403 sin el permiso) | `AGENTS.md`, "Casos de uso MVC" (después, `docs/guides/permiso-nuevo.md`) |
| 4 | ¿Dónde van los textos que ve el usuario? | en `Errors.resx` y `Validation.resx`, con sus `.en.resx`; cada clave, en los dos idiomas (`ResourceParityTests`); voseo; identificadores, excepciones y logs, en inglés | `AGENTS.md`, "Idioma y textos" |
| 5 | ¿Qué hago con una fecha? | se obtiene con `TimeProvider` y `GetUtcNow().UtcDateTime`; `BannedSymbols.txt` prohíbe cinco símbolos; sufijo `Utc`, y `DateOnly` si no tiene hora; en los tests, `FakeTimeProvider` | `AGENTS.md`, "Fechas: siempre en UTC" |
| 6 | ¿Cómo se nombra un test? | en inglés y como frase (`Deleted_rows_are_hidden_from_queries_and_endpoints`); va en uno de los cuatro proyectos de tests | `AGENTS.md`, "Tests" |
| 7 | ¿Qué no se debe loguear? | códigos, tokens, enlaces, secretos ni números enteros (`IPhoneNumberParser.Mask`); `Microsoft.AspNetCore` va en `Warning` y la conexión, sin `Include Error Detail`; los contratos sobrescriben `ToString()`; se usa `[LoggerMessage]` | `AGENTS.md`, "Idioma y textos" y "Casos de uso MVC", que llevan a `whatsapp.md` |
| 8 | ¿Qué hay que hacer antes de dar algo por terminado? | build sin advertencias y `dotnet test` en verde (los de integración, con Docker); TDD; `aspire stop` si se levantó la app | `AGENTS.md`, "Forma de trabajo" y "Comandos" |
| 9 | ¿Dónde está lo específico de WhatsApp? | en `docs/features/whatsapp.md` (reglas y configuración), el spec de diseño y el ADR 0007; regla de oro: un mensaje nunca abre una sesión | `AGENTS.md`, que lleva a `docs/features/whatsapp.md` |
| 10 | ¿Cómo se agrega un prefijo de backend? | en `BackendPrefixes`, `Backend_routes_keep_returning_a_problem` de `SpaHostingTests` y el `server.proxy` de `vite.config.ts`; si falta alguno, el `index.html` responde con 200 | `AGENTS.md`, "Front" (después, `docs/guides/prefijo-de-backend.md`) |

**Resultado del 2026-09-27: 10 de 10**, pero la corrida no fue ciega. El agente tenía la respuesta esperada al lado de cada pregunta, y el arnés le cargó un `CLAUDE.md` viejo. Para cerrar la puerta en limpio hay que repetirla con un agente que no cargue `CLAUDE.md` (por ejemplo, Codex) y solo con la columna de preguntas. La corrida marcó dos huecos, y los dos se corrigieron en el cierre: `aspire stop` estaba solo en `CLAUDE.md`, y `Api/Routing` y `WhatsAppWebhookController` no tenían puntero a su documento.

---

## Etapa 6: WhatsApp como módulo opcional (la más grande)

**POSTERGADA por decisión del usuario del 2026-09-27.** No se planifica ni se ejecuta hasta que el usuario la retome.

**Objetivo:** que un proyecto sin WhatsApp lo quite borrando carpetas y una línea de registro, con el build y los tests en verde. Decisión D7.

**Antes de planificar en detalle:** escribir su spec con `superpowers:brainstorming`, porque hay decisiones de diseño abiertas. El borrador de enfoque:

- **Puertos en el núcleo, adaptadores en el módulo:**
  - `IInvitationChannel`: correo en el núcleo, WhatsApp en el módulo.
  - `ILoginCodeChannel`: `LoginCode` deja de conocer WhatsApp y el canal es un valor que registran los módulos.
  - `IPhoneLinkObserver`: el perfil y la administración avisan del cambio de número y el módulo suelta el contacto e invalida los enlaces.
- **Carpetas del módulo:**
  - `Domain/WhatsApp`;
  - `Application/Modules/WhatsApp/{Services,Interfaces,Models}`;
  - `Infrastructure/Modules/WhatsApp/{Cloud,Webhook,Inbound,Retention,Persistence}`;
  - `Api/Modules/WhatsApp`.

  El módulo registra sus propias `IEntityTypeConfiguration` con un `ApplyConfigurationsFromAssembly` filtrado por namespace.
- **Registro:** un solo `AddWhatsAppModule()` por capa, llamado desde `Program.cs`. Infrastructure deja de registrar servicios de Application (`WhatsAppRegistration.cs:120`).
- **Lo que sigue en el núcleo:** el teléfono como dato de la cuenta y `IPhoneNumberParser`.
- **Migraciones:** las tablas de WhatsApp quedan en la migración inicial. Quitar el módulo en un proyecto nuevo incluye una migración que las borra; la guía `quitar-whatsapp.md` lo explica.
- **Prueba de fuego:** en una copia descartable, borrar el módulo siguiendo la guía; el build y los tests del núcleo tienen que quedar en verde.

**Puerta:** la general, más la prueba de fuego y un test de arquitectura (el núcleo no referencia ningún namespace `*.Modules.WhatsApp`).

---

## Etapa 7: dominio, producción y blindaje final

**Diseño de ejecución del resto:** [2026-09-28-etapa-7-dominio-produccion.md](2026-09-28-etapa-7-dominio-produccion.md), en 10 tareas, con seis decisiones del usuario del 2026-09-28: la Api no arranca en producción si la base no está migrada o no responde; el arnés evita el seed al arrancar excluyendo el ambiente `Testing`; `DatabaseSeeder` es la única excepción con nombre de `TransactionBoundaryTests` (enmienda al ADR 0001); los nombres de Google y de WhatsApp se recortan en la entrada sin partir emojis; una invitación por correo con la cola llena queda marcada como fallida; y el seed nunca crea cuentas, solo le asegura el rol Admin a la cuenta de la plataforma (`Seed:AdminEmail`).

**Decisión del usuario (2026-09-28), después de la revisión de la tarea 5:** el detalle de un usuario muestra `deliveryStatus: "Failed"` también para una invitación por correo que la cola llena rechazó (`80456d5`); una que salió sigue sin estado. **Pendiente del front:** mostrar ese `Failed` también cuando el canal es correo, y `maxLength={100}` en los tres campos de nombre (tarea 3).

### Tareas

1. **Eventos de dominio (D3).** Borrar `Domain/Common/AggregateRoot.cs` e `IDomainEvent.cs`; las cinco entidades pasan a heredar de `Entity`. Documentarlo en el ADR.
   - [x] **Hecho el 2026-09-26 (`cbff737`)**, con el ADR `docs/decisions/0003-sin-eventos-de-dominio.md`. Eran seis clases: las cinco entidades y el `Widget` de los tests. El modelo de EF no cambió (`MigrationsTests` en verde), porque los eventos se exponían con métodos y nunca se mapearon.
2. **Reglas de la cuenta en un solo lugar.**
   - Las invariantes de la cuenta ("correo o teléfono obligatorio" en `UserRepository.cs:185-188`, el largo del nombre, `Restore`) pasan a métodos de `ApplicationUser` o a una política `AccountRules` en Domain, con tests unitarios.
   - `ApplicationUser` deja los setters públicos de `IsActive`, `DisplayName` y `Culture`.
   - El nombre demasiado largo pasa de recortarse en silencio a ser un error de validación. Revisar el front antes: puede cambiar lo que ve la persona.
   - [x] **Hecho el 2026-09-28 (`8bc630a`, `28989f3`),** con un desvío de la letra por la decisión P4 del usuario: el nombre que se tipea ya era un error de validación por HTTP (400 con `errors.displayName`), y los que nadie tipea (el de Google y el del perfil de WhatsApp) no pasan a ser un error, sino que se recortan en la entrada, en Application, con `AccountRules.FitExternalDisplayName`: sin partir un emoji y sin el `\0`, con `StorableText`, que salió de `WhatsAppText` a `Domain/Common`. `AccountRules` (Domain) tiene los topes, el contacto obligatorio y la validez del nombre; `ApplicationUser` tiene `Create`, `Rename`, `SetActive`, `UpdatePreferences` y `Restore(displayName)`, con `private set` en `DisplayName`, `Culture`, `TimeZoneId` e `IsActive`. Un nombre largo que llega al repositorio es un bug y lanza. El modelo de EF no cambió (`has-pending-model-changes`). Queda para el front: `maxLength={100}` en los inputs de nombre de `ProfilePage.tsx`, `UserFormDialog.tsx` y `UserEditDialog.tsx`.
3. **Errores de Domain que usa solo Application.** `AccountErrors`, `ExternalLoginErrors`, `RoleErrors`, `SettingsErrors` y `UserInvitationErrors` se quedan en Domain, pero hay que documentar la regla: los errores viven al lado de la entidad o del área que los define. `WhatsAppErrors` sale de `Domain/Authentication/` y va con su módulo.
   - [x] **Hecho el 2026-09-28 (`20cc9ac`; la regla del IL suma el constructor y el `with` en `c99827a`):** la regla está en `AGENTS.md` y `backend.md`, `WhatsAppErrors` está en `Domain/WhatsApp/` y `ErrorDeclarationTests` la verifica.
4. **Producción (D6).**
   - El seed corre en todos los ambientes.
   - `docs/guides/despliegue.md` explica el bundle de migraciones, los certificados de OpenIddict y el pendiente de copiar el `dist/` del front al `wwwroot`, que figura como urgente desde la Fase 3.
   - Test de integración: arrancar en ambiente `Production` contra una base migrada y vacía deja los roles, los ajustes y el cliente `web`.
   - El seed dentro de un límite y en fila entre réplicas (un advisory lock `seed:` adentro de `ExecuteInTransactionAsync`): la Etapa 1 lo dejó afuera. Antes, decidir cómo entra el seed en `TransactionBoundaryTests`: hoy solo un punto de entrada de `Interfaces/Services` recibe `IUnitOfWork` y llama a `ExecuteInTransactionAsync` (`Only_use_case_entry_points_receive_the_unit_of_work` y `Only_use_case_entry_points_run_a_unit_of_work`), y el trabajo devuelve un `Result` (`where TResult : Result`), mientras que los seeders devuelven `Task`. La clave `seed:` va en `AdvisoryLockKeys` y en `LockKeyPrefixes` del test. OpenIddict no es el obstáculo: los managers que usa el seed (`FindBy*`, `CreateAsync` y `UpdateAsync`) no abren transacción propia. En sus stores de EF Core, solo `DeleteAsync` y `PruneAsync` la piden, con `CreateTransactionAsync`, que atrapa cualquier error no fatal de `BeginTransactionAsync` y devuelve `null`: adentro de un límite no lanzan, corren en la transacción de afuera. Se verificó el 2026-09-27 leyendo el IL de `OpenIddict.EntityFrameworkCore` 7.7.1; con otra versión, volver a mirarlo.
   - [x] **El seed en un límite y en fila, hecho el 2026-09-28** (tarea 8 del [diseño](2026-09-28-etapa-7-dominio-produccion.md), P3 = B): `DatabaseSeeder` (`Infrastructure/Persistence/Seed`) abre `ExecuteInTransactionAsync` con `OnSuccess`, toma `AdvisoryLockKeys.Seed` (`seed:database`) y corre los roles, los ajustes y OpenIddict; `SystemSettingsSeeder` ya no guarda por su cuenta, y `SeedExtensions.SeedDatabaseAsync` conserva su firma. En `TransactionBoundaryTests`, `DatabaseSeeder` es la única excepción con nombre de los dos puntos de entrada (con `Assert.Contains`), el seed perdió su excepción en `Only_the_unit_of_work_saves_the_context` y `seed:` entró en `LockKeyPrefixes`; `PersistenceRegistrationTests` suma `DatabaseSeeder` a sus dueños permitidos. Enmienda fechada en el ADR 0001. Tests de integración nuevos (con Docker): `DatabaseSeederTests` y el texto de la clave en `AdvisoryLockKeysTests`. Que el seed corra en todos los ambientes es la tarea 9.
5. **Versionado (D5).** Un ADR y una línea en `AGENTS.md`.
   - El ADR ya está (`docs/decisions/0005-sin-versionado-de-api-por-ahora.md`); falta la línea en `AGENTS.md`.
   - [x] **Hecho el 2026-09-28 (`f0f1f49`):** la línea está en `AGENTS.md`, en "Casos de uso MVC y borde HTTP".
6. **Colas en memoria.**
   - Renombrar `WhatsAppOutbox` a `WhatsAppSendQueue`: no es un outbox transaccional.
   - `EmailQueue` y la cola de WhatsApp con la misma forma: capacidad configurable, `TryEnqueue` que devuelve `bool` y log cuando se descarta.
   - Documentar que un reinicio pierde lo encolado.
   - [x] **Hecho el 2026-09-28 (`7da9289`, `310382f`, `80456d5`)** (tareas 4 y 5 del [diseño](2026-09-28-etapa-7-dominio-produccion.md)): la cola de WhatsApp se llama `WhatsAppSendQueue`; `IEmailQueue.TryEnqueue` devuelve `bool`, con `Email:QueueCapacity`; el código por correo se marca enviado solo si entró y, por P5, una invitación por correo con la cola llena queda `SendFailed`, con un log, el detalle la muestra con `deliveryStatus` `Failed` (`80456d5`, de la revisión) y su reenvío no da 429. Está en [backend.md, "Colas en memoria"](../architecture/backend.md#colas-en-memoria).
7. **Registro de Infrastructure en un solo lugar.** Los repositorios, lectores y seeders que hoy están en `IdentityRegistration.cs:98-100` y `OpenIddictRegistration.cs:86` se registran desde `Persistence/PersistenceRegistration.cs`. `SystemSettingsReader` deja de invalidar caché: esa escritura pasa al servicio.
   - [x] **El registro, hecho el 2026-09-28 (`ad5b4b5`)** (tarea 6 del [diseño](2026-09-28-etapa-7-dominio-produccion.md)): `Infrastructure/Persistence/PersistenceRegistration.cs` (`AddPersistence`) registra los interceptores en el mismo orden, el `DbContext`, el health check `database`, `IUnitOfWork`, los 10 repositorios, los 5 lectores (con `IPermissionReader`) y los 3 seeders con `RegistrationOptions`; el conjunto de registros de `AddInfrastructure` no cambió. `DatabaseConnectionName` se queda en `DependencyInjection`. `PersistenceRegistrationTests` verifica que en `src` solo la registración y `SeedExtensions` nombren una clase concreta de `Repositories`, `Readers` o `Seed`.
   - [x] **El caché de ajustes fuera del lector, hecho el 2026-09-28** (tarea 7 del [diseño](2026-09-28-etapa-7-dominio-produccion.md)): el descarte es de `ISystemSettingsCache` (`Application/Interfaces/Integrations/Caching`), implementado por `Infrastructure/Caching/SystemSettingsCache`, que tiene la clave; `SystemSettingsService` lo llama después del commit y solo si se confirmó, y `PersistenceNamingTests` ya no tiene la excepción de `Invalidate`.
8. **Blindaje final**, en `tests/ArquitecturaBase.ArchitectureTests/`:
   - lista de paquetes permitidos en `ArquitecturaBase.Application.csproj`;
   - ninguna firma pública de `Application` expone `IQueryable` ni `Expression<>`;
   - no hay `MapGet`, `MapPost`, `MapPut` ni `MapDelete` fuera de los endpoints técnicos permitidos;
   - cada clase `*Service` de `Application.Services` implementa una interfaz de `Interfaces.Services`;
   - las claves de `Errors.resx` cumplen el regex `^[A-Z][A-Za-z]+(\.[A-Z][A-Za-z0-9]+){2,}$`, más las excepciones de `ApiErrorCodes`;
   - cada entidad de Domain tiene su `IEntityTypeConfiguration`;
   - `ControllerServiceRepositoryTests` falla si no encuentra el namespace `Controllers` y revisa los sub-namespaces (hoy pasa en silencio, `:43-46` y `:68`);
   - `CA1848` en `warning` en `.editorconfig`, para que un `logger.LogX` directo rompa el build.
   - [x] **Hecho el 2026-09-26 (`4c393c1`).** Entraron las ocho reglas y pasan con el código de hoy: no quedó ninguna afuera. Lo que cada una deja fuera a propósito:
     - **Paquetes** (`ApplicationPackagesTests`): la lista son los cinco paquetes que usa Application hoy, no cualquier `Microsoft.Extensions.*`, y otro test prohíbe `FrameworkReference`, porque `Microsoft.AspNetCore.App` se colaría sin pasar por la lista. No cubre el analizador que `Directory.Packages.props` suma a todos los proyectos con `GlobalPackageReference`, porque no está en el `.csproj`.
     - **`Map*`** (`MinimalApiRoutesTests`): revisa `Api`, `Application` e `Infrastructure`, más de lo pedido, y la lista de excepciones está vacía. Ningún endpoint técnico usa `Map*` ahí: `/connect` es `ConnectController`, el webhook es `WhatsAppWebhookController` y la salud sale de `MapDefaultEndpoints`, en `ServiceDefaults`, que no se revisa.
     - **`*Service`** (`ApplicationServicesTests`): solo mira las clases cuyo nombre termina en `Service`. `UserGuards`, `LoginCodeIssuer` o `PhoneNumberChange` quedan fuera; sus sufijos los ordena la tarea 4 de la Etapa 3.
     - **Códigos de error** (`ErrorCodeTests`): el regex quedó en `{2,}` con dígitos, no en el `{2}` sin dígitos del borrador. Las claves reservadas son `Title.*`, `Validation.Failed` y las de `ApiErrorCodes`, y otro test exige que cada una siga existiendo en el `.resx`.
     - **Entidades** (`EntityConfigurationTests`): solo las que heredan de `Entity`. `ApplicationUser` y `ApplicationRole` viven en Infrastructure y no heredan de `Entity`; tienen su configuración igual.
     - **`ControllerServiceRepositoryTests`:** `Every_controller_injects_an_application_service_interface` sigue comparando `Interfaces.Services` de forma exacta, porque no se pidió cambiarlo.
     - **`CA1848`:** con `latest-recommended`, el SDK de .NET 10 ya lo ponía en `warning`. La línea de `.editorconfig` lo deja fijo aunque cambie `AnalysisLevel`.
   - [x] **Regla posible, no pedida:** que cada `[HasPermission]` nombre un permiso de `Permissions.All`. Hoy `[HasPermission("users.raed")]` compila y siempre responde 403.
     - **Hecho el 2026-09-28 (`f0f1f49`)** (tarea 1 del [diseño](2026-09-28-etapa-7-dominio-produccion.md)): `Every_required_permission_exists`, con un caso de control.

**Puerta:** la general, más todos los tests de arquitectura nuevos en verde.

---

## Orden y dependencias

```text
Etapa 0 ──► Etapa 1 ──► Etapa 2 ──► Etapa 3 ──► Etapa 4 ──► Etapa 5
                                        │                        │
                                        └──────► Etapa 6 ◄───────┘
                                                    │
                                                    ▼
                                                 Etapa 7
```

- 1 → 2: recortar `IIdentityService` es más seguro con el límite transaccional ya explícito.
- 3 → 4: el área de referencia tiene que mostrar los idiomas nuevos, no los viejos.
- 5 puede empezar en paralelo con 4 para la estructura, pero se cierra después, cuando la receta está probada.
- 6 necesita la 3 (interfaces por responsabilidad) y conviene después de la 5 (su doc ya tiene casa). **Postergada por decisión del usuario del 2026-09-27:** sin fecha; la 7 no depende de ella.
- La 7 puede tomar tareas sueltas antes (los tests de arquitectura del punto 8 se pueden ir sumando en cada etapa), pero se cierra al final.
- El 2026-09-26, mientras corría la Etapa 1, se adelantaron en paralelo las tareas que no tocan el guardado: de la Etapa 3, la 6, la 7 (salvo el 201 de roles), la 8, la parte de `Api` de la 9 y la 10; de la Etapa 5, los ADR y la mudanza de los históricos; de la Etapa 7, la 1 y la 8. Cada una tiene su nota "Hecho" en su etapa, con el commit cuando es de código.

## Esfuerzo estimado

| Etapa | Tamaño | Riesgo |
|---|---|---|
| 0 | medio día | bajo |
| 1 | 2 a 3 días | **alto** (concurrencia y transacciones) |
| 2 | 2 días | medio |
| 3 | 3 a 4 días | medio (toca todos los servicios) |
| 4 | 1 a 2 días | bajo |
| 5 | 1 día | bajo (el riesgo es perder una regla) |
| 6 | 4 a 6 días | **alto** (diseño y muchas piezas) |
| 7 | 2 a 3 días | medio |
