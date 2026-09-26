# Plantilla estándar: plan de trabajo por etapas

> **Para agentes:** este es el plan maestro. Cada etapa se ejecuta con su propio plan detallado (TDD, pasos de 2 a 5 minutos, código completo), que se escribe con `superpowers:writing-plans` **al arrancar la etapa**, porque depende de cómo quedó la anterior. La Etapa 0 ya está al nivel de detalle ejecutable. Los pasos usan casillas (`- [ ]`) para el seguimiento.

**Objetivo:** que ArquitecturaBase sirva como plantilla estándar para empezar cualquier proyecto. Tiene que tener una sola forma de hacer cada cosa, un área de referencia para copiar, documentación en capas que una IA pueda seguir sin adivinar y módulos de producto (WhatsApp) que se puedan quitar.

**Origen:** el code review del 2026-09-26. Hallazgos principales:
- dos formas de guardar (Identity autoguarda y `IUnitOfWork`);
- `IIdentityService` con 39 métodos, la mayoría reenvíos;
- WhatsApp entrelazado con Auth y Users;
- no hay una receta ni un área de referencia;
- boilerplate que quedó del pipeline viejo (111 llamadas de log manuales, un validador inyectado por request);
- `CLAUDE.md` mezcla la plantilla con el producto.

**Arquitectura:** no cambia la arquitectura canónica (`docs/specs/2026-09-24-backend-mvc-architecture.md`): controllers → servicios → repositorios/lectores. El plan termina la migración, quita duplicados y deja por escrito y con tests lo que hoy es convención implícita.

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
- `.editorconfig:55`: el comentario dice `Metodo_condicion_resultado`; alinearlo con `CLAUDE.md`, que pide frases en inglés en minúscula (`Deleted_rows_are_hidden_from_queries_and_endpoints`).
- `README.md:309`: revisar la referencia al arreglo del test inestable en el plan de la Fase 3. Si está vieja o rota, apuntarla a donde está de verdad (buscar con `git log -S`) o quitarla.
- Mover `tests/ArquitecturaBase.Application.UnitTests/Services/RoleServiceTests.cs`, `RoleServiceWriteTests.cs` y `SystemSettingsServiceTests.cs` a `Services/Roles/` y `Services/Settings/`, y ajustar sus namespaces.

- [x] **Paso 1:** hacer los cambios.
- [x] **Paso 2:** build y `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`.
- [x] **Paso 3:** commit `docs: corregir referencias y unificar la convención de nombres de tests`.

### Tarea 0.5: marcar lo histórico

**Archivos:**
- Todos los planes de `docs/plans/` salvo este.
- `docs/specs/2026-09-18-arquitectura-base-design.md`, `2026-09-20-fase-4-administracion-design.md` y `2026-09-22-ingreso-whatsapp-design.md`.

- [x] **Paso 1:** agregar como primera línea de cada plan viejo:
  ```markdown
  > **HISTÓRICO. No ejecutar.** Registro de cómo se construyó esta parte. La arquitectura vigente está en `docs/specs/2026-09-24-backend-mvc-architecture.md`; donde este documento hable de handlers, `Features/` o Minimal API, prevalece la especificación.
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
- **`docs/plans/contracts/` queda sin marcar.** Son instantáneas previas a la migración, con nombres de handlers. El inventario vivo de rutas es `tests/ArquitecturaBase.Api.IntegrationTests/Contracts/ExplicitRouteInventoryTests.cs`. Se ordenan en la Etapa 5.
- **Pendiente heredado.** El plan de migración deja abiertas las comprobaciones manuales de su Tarea 1, su Tarea 8 y "Puertas abiertas para el cierre": el smoke con Aspire, OIDC y WhatsApp, y el arranque con una base vacía.

---

## Etapa 1: una sola forma de guardar (riesgo alto, es la base de todo)

**Objetivo:** que en cada caso de uso el límite transaccional se lea en el servicio y sea uno solo. Decisión D1.

**Estado actual:**
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
     - ante un `Result` fallido, deshace **salvo** que el caso de uso lo pida explícitamente (algunos errores deben persistir intentos: ver `CLAUDE.md`, "Cada servicio define expresamente cuándo guarda");
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

   Reemplazar los literales repetidos (por ejemplo `UserRepository.cs:32`) y actualizar la lista de `CLAUDE.md`.
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
- la sección "Persistencia" de `CLAUDE.md` y el spec canónico describen `ExecuteInTransactionAsync` como la única forma.

---

## Etapa 2: `IIdentityService` solo técnico

**Objetivo:** que para cada operación sobre usuarios haya un solo camino.

**Estado actual:** 39 métodos, de los que solo usan algo los siete servicios de Application que figuran en la tabla. Los métodos `CreateRoleAsync`, `UpdateRoleAsync`, `DeleteRoleAsync` y `GetRolesAsync` no los usa ningún servicio. `IdentityService.cs:72-75` documenta que `SetPhone`, `RemovePhone` y `SetEmail` solo delegan en `IUserRepository` y se recortan en esta etapa.

### Tareas

1. **Separar por responsabilidad.** Quedan tres contratos:
   - `ISignInService` (en `Application/Interfaces/Integrations/Identity/`), solo lo técnico:
     - `SignInAsync`, `SignOutExternalAsync`, `IsLockedOutAsync`;
     - `RegisterFailedAttemptAsync`, `ResetFailedAttemptsAsync`, `RevokeSessionsAsync`;
     - login externo: `GetExternalLoginAsync`, `AddExternalLoginAsync`, `FindByExternalLoginAsync`, `HasExternalLoginAsync`.
   - `IUserRepository` y `IUserReader`, para todo el acceso a datos de cuentas: `Create`, `SetEmail`, `SetPhone`, `FindBy*`, `IsDeleted*`, `Restore`, `SetActive`, `SetDisplayName`, `SetRoles`.
   - `IRoleReader` e `IRoleRepository`, para roles. Se borra el CRUD de roles duplicado de `IdentityService.cs:198-245`.
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

## Etapa 3: menos ceremonia en servicios y controllers

**Objetivo:** que un caso de uso nuevo se escriba en pocas líneas y siempre igual, y que el área de referencia (Etapa 4) ya muestre esos idiomas.

### Tareas

1. **Un solo validador inyectable.**
   - `IRequestValidator` en `Application/Common/Validation/`, con `Task<ValidationError?> ValidateAsync<T>(T request, CancellationToken)`. Resuelve los `IValidator<T>` del contenedor y reutiliza la lógica de `ServiceRequestValidator<T>.ValidateAsync`.
   - Reemplaza los N `ServiceRequestValidator<T>` que hoy recibe cada constructor. Solo `AccountService` tiene tres.
   - Test unitario en `ServiceValidationTests`.
2. **Logging de operación en un solo lugar.**
   - Hoy hay 111 llamadas `LogHandling`, `LogHandled` y `LogFailed` repartidas en 10 servicios, con mensajes distintos.
   - Crear `Application/Common/Logging/OperationLog.cs`: `[LoggerMessage]` compartidos más `static async Task<Result<T>> RunAsync<T>(ILogger, string operation, Func<Task<Result<T>>>)`, que registra el inicio, el fin y el código de error si falló.
   - Los servicios quedan con una línea por método.
   - Mantener la regla: nunca registrar el request, solo el nombre de la operación y el código de error.
3. **Achicar las fachadas.**
   - La meta: ningún constructor de un servicio de Application con más de 8 dependencias.
   - Hoy `AccountService` tiene 17, `ProfileWhatsAppOperations` 16, `UserService` 14 y `WhatsAppInboundService` 14.
   - Las Etapas 1 y 2 y los puntos 1 y 2 de esta etapa ya bajan varias. Lo que quede se parte por responsabilidad con **interfaz propia** en `Interfaces/Services`: por ejemplo, `IUserAdministrationService` (alta, edición, estado) e `IUserQueryService` (listado, detalle, conteos). Nada de fachadas que solo registran y delegan.
   - Test de arquitectura con el tope de 8.
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
5. **Modelos sin duplicados.**
   - Borrar `Models/Users/ReadModels/UserDetail.cs` y `UserListItem.cs`, o renombrarlos a `UserDetailRow` y `UserListRow` si la proyección del lector difiere de la salida.
   - `UserListRequest` (en `Models/Identity`) y `ListUsersRequest` (en `Models/Users`) se unifican en `Models/Users`.
   - Regla escrita: la salida de un servicio termina en `Response`; la proyección de un lector, en `Row`.
6. **Contratos HTTP según D2.**
   - `MeController`, `SettingsController`, `LoginCodeController` y `LoginLinkController` pasan a recibir contratos de `Api/Contracts/<Área>` con mapeo manual.
   - Los `ToString()` que ocultan datos personales se mudan a esos contratos.
   - Test de arquitectura: los parámetros `[FromBody]` y `[FromQuery]` de un controller son tipos de `Api.Contracts`.
7. **Helpers HTTP.**
   - Crear `[HasPermission(Permissions.Users.Read)]` en `Api/Authorization/HasPermissionAttribute.cs`, que reemplaza las 15 apariciones de `[Authorize(Policy = PermissionPolicyProvider.PolicyPrefix + …)]`.
   - Agregar `ToAcceptedResult` y `ToCreatedResult(actionName, routeValues)` en `ControllerResultExtensions.cs`.
   - El 202 se arma a mano en 4 lugares: `LoginCodeController.cs:16-19,32-35` y `MeController.cs:24-26,46-48`.
   - Pasar las altas a 201 **solo después de revisar el front**: buscar en `../ArquitecturaBaseFront/src` los `POST` a `/api/users` y `/api/roles` y confirmar que tratan cualquier 2xx como éxito.
   - Test de integración: un alta devuelve 201 con `Location`.
8. **ProblemDetails centralizado.**
   - El `traceId` se calcula en un solo lugar (`ProblemDetailsMapper`); hoy está en `DependencyInjection.cs:32`, `ControllerResultExtensions.cs:26` y `MvcInvalidModelStateResponseFactory.cs:13`.
   - Las opciones JSON se configuran con un solo `ConfigureJson` (`DependencyInjection.cs:47-62`).
   - Documentar que el 400 de un body ilegible no trae `errors` a propósito, porque no debe revelar la forma interna.
9. **Carpetas.**
   - `Api/Services` → `Api/RequestContext`, como pide el spec.
   - `Application/Interfaces/Integrations` se divide en subcarpetas: `Identity/`, `Security/`, `Email/`, `WhatsApp/`, `Request/`.
   - `IWhatsAppWebhookPersistence` pasa a `Interfaces/Persistence`.
10. **OpenAPI.**
    - Convención global de respuestas de error (`ProblemDetails` para 400, 401, 403, 404 y 500).
    - `ProducesResponseType` de éxito en todas las acciones.
    - Test: el documento `/openapi/v1.json` declara un esquema de respuesta para cada operación.

**Puerta:** la general, más los tests de arquitectura nuevos (tope de dependencias, contratos, `HasPermission`).

---

## Etapa 4: área de referencia y receta

**Objetivo:** que agregar un área nueva sea seguir una lista y copiar un ejemplo real. Decisión D4.

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
2. **`docs/guides/agregar-un-area.md`.** La receta en orden, con la ruta de cada archivo y un enlace al archivo equivalente de Roles:

   | Paso | Pieza |
   |---|---|
   | 1 | entidad en Domain |
   | 2 | `IEntityTypeConfiguration` |
   | 3 | migración (comando de `CLAUDE.md`) |
   | 4 | `<Entidad>Errors` y claves en los dos `.resx` |
   | 5 | permiso: los tres pasos de `CLAUDE.md` |
   | 6 | interfaces de repositorio y lector en `Application/Interfaces/Persistence` |
   | 7 | sus implementaciones en `Infrastructure/Persistence/{Repositories,Readers}` y su registro |
   | 8 | modelos y validadores |
   | 9 | interfaz de servicio y servicio |
   | 10 | registro en `Application/DependencyInjection.cs` |
   | 11 | contratos y controller |
   | 12 | tests |
   | 13 | inventario de rutas |
   | 14 | prefijo de backend, si la ruta no empieza con `/api` (los tres lugares de `CLAUDE.md`) |

   Al final, una lista de verificación.
3. **Rehacer `TestFeatures/Widgets` con el camino canónico.** Los archivos `CreateWidget.cs`, `GetWidgets.cs` y `GetWidgetById.cs` tienen nombres de handler y se reemplazan por `WidgetsTestController` y `WidgetTestService`, o se borra lo que ya no use ningún test.
4. **Probar la receta.** Un subagente sin contexto sigue la guía y agrega un área de prueba, por ejemplo `Tags` (nombre y descripción, CRUD completo), en una rama descartable o sin commitear. Se mide cuántas preguntas tuvo que hacer y qué archivo no encontró, se corrige la guía y se descarta el área.

**Puerta:** la general, más la prueba de la receta sin preguntas sin respuesta.

---

## Etapa 5: documentación en capas para humanos e IA

**Objetivo:** que un agente cargue en cada sesión solo las reglas de la plantilla y lea lo específico de un área solo cuando la toca.

### Estructura destino

```text
AGENTS.md                      ← índice corto (~80 líneas): capas, flujo, Result, errores, UTC, i18n, build, tests, dónde va cada cosa
CLAUDE.md                      ← `@AGENTS.md` + lo específico de Claude (aspire stop, forma de trabajo)
docs/architecture/backend.md   ← el spec canónico actual, sin nombres del producto
docs/guides/agregar-un-area.md
docs/guides/permiso-nuevo.md, migracion.md, prefijo-de-backend.md, quitar-whatsapp.md
docs/features/identidad.md     ← hoy CLAUDE.md "Identidad"
docs/features/whatsapp.md      ← hoy CLAUDE.md "WhatsApp" y su tabla de configuración
docs/features/administracion.md← hoy CLAUDE.md "Administración (Fase 4)" y los filtros/conteos de usuarios
docs/decisions/NNNN-*.md       ← D1 a D7 de este plan, una por archivo (ADR corto: contexto, decisión, consecuencias)
docs/history/                  ← planes y specs de fases, ya marcados en la Etapa 0
src/**/WhatsApp/CLAUDE.md      ← una línea: "Antes de tocar esto, leé docs/features/whatsapp.md"
```

### Tareas

1. Mover el contenido **sin reescribir las reglas**: cada regla funcional de hoy tiene que seguir existiendo en algún archivo. Verificarlo con una lista de las reglas de `CLAUDE.md` antes y después.
2. Sacar de `AGENTS.md` el detalle de la migración ("41 combinaciones…") y reemplazarlo por la regla general: "cada ruta figura en el inventario y tiene su test".
3. `docs/architecture/backend.md` suma lo que definieron las Etapas 1 a 3: transacciones, convención de nombres, helpers, contratos, modelos, `HasPermission` y los helpers de resultado.
4. `README.md`: cómo levantar, cómo probar, mapa de docs. El resto va por enlace.

**Puerta:** la general, más la comprobación de que ninguna regla se perdió en la mudanza. Un agente nuevo lee solo `AGENTS.md` y contesta bien diez preguntas del tipo "¿dónde va X?". Las preguntas se escriben antes de reorganizar.

---

## Etapa 6: WhatsApp como módulo opcional (la más grande)

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

### Tareas

1. **Eventos de dominio (D3).** Borrar `Domain/Common/AggregateRoot.cs` e `IDomainEvent.cs`; las cinco entidades pasan a heredar de `Entity`. Documentarlo en el ADR.
2. **Reglas de la cuenta en un solo lugar.**
   - Las invariantes de la cuenta ("correo o teléfono obligatorio" en `UserRepository.cs:185-188`, el largo del nombre, `Restore`) pasan a métodos de `ApplicationUser` o a una política `AccountRules` en Domain, con tests unitarios.
   - `ApplicationUser` deja los setters públicos de `IsActive`, `DisplayName` y `Culture`.
   - El nombre demasiado largo pasa de recortarse en silencio a ser un error de validación. Revisar el front antes: puede cambiar lo que ve la persona.
3. **Errores de Domain que usa solo Application.** `AccountErrors`, `ExternalLoginErrors`, `RoleErrors`, `SettingsErrors` y `UserInvitationErrors` se quedan en Domain, pero hay que documentar la regla: los errores viven al lado de la entidad o del área que los define. `WhatsAppErrors` sale de `Domain/Authentication/` y va con su módulo.
4. **Producción (D6).**
   - El seed corre en todos los ambientes.
   - `docs/guides/despliegue.md` explica el bundle de migraciones, los certificados de OpenIddict y el pendiente de copiar el `dist/` del front al `wwwroot`, que figura como urgente desde la Fase 3.
   - Test de integración: arrancar en ambiente `Production` contra una base migrada y vacía deja los roles, los ajustes y el cliente `web`.
5. **Versionado (D5).** Un ADR y una línea en `AGENTS.md`.
6. **Colas en memoria.**
   - Renombrar `WhatsAppOutbox` a `WhatsAppSendQueue`: no es un outbox transaccional.
   - `EmailQueue` y la cola de WhatsApp con la misma forma: capacidad configurable, `TryEnqueue` que devuelve `bool` y log cuando se descarta.
   - Documentar que un reinicio pierde lo encolado.
7. **Registro de Infrastructure en un solo lugar.** Los repositorios, lectores y seeders que hoy están en `IdentityRegistration.cs:98-100` y `OpenIddictRegistration.cs:86` se registran desde `Persistence/PersistenceRegistration.cs`. `SystemSettingsReader` deja de invalidar caché: esa escritura pasa al servicio.
8. **Blindaje final**, en `tests/ArquitecturaBase.ArchitectureTests/`:
   - lista de paquetes permitidos en `ArquitecturaBase.Application.csproj`;
   - ninguna firma pública de `Application` expone `IQueryable` ni `Expression<>`;
   - no hay `MapGet`, `MapPost`, `MapPut` ni `MapDelete` fuera de los endpoints técnicos permitidos;
   - cada clase `*Service` de `Application.Services` implementa una interfaz de `Interfaces.Services`;
   - las claves de `Errors.resx` cumplen el regex `^[A-Z][A-Za-z]+(\.[A-Z][A-Za-z]+){2}$`, más las excepciones de `ApiErrorCodes`;
   - cada entidad de Domain tiene su `IEntityTypeConfiguration`;
   - `ControllerServiceRepositoryTests` falla si no encuentra el namespace `Controllers` y revisa los sub-namespaces (hoy pasa en silencio, `:43-46` y `:68`);
   - `CA1848` en `warning` en `.editorconfig`, para que un `logger.LogX` directo rompa el build.

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
- 6 necesita la 3 (interfaces por responsabilidad) y conviene después de la 5 (su doc ya tiene casa).
- La 7 puede tomar tareas sueltas antes (los tests de arquitectura del punto 8 se pueden ir sumando en cada etapa), pero se cierra al final.

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
