# Arquitectura canónica del backend: MVC, servicios y persistencia especializada

**Estado:** vigente. **Alcance:** el backend de `ArquitecturaBase` (el front es un proyecto aparte; `ArquitecturaBaseMultitenant` queda fuera). Prevalece sobre descripciones anteriores con handlers o Minimal APIs de negocio. Reglas funcionales en [`docs/features/`](../features/), resumen en [AGENTS.md](../../AGENTS.md), motivos en los [ADR](../decisions/README.md) e índice compartido con el front en las [premisas](premisas-de-la-plantilla.md). Lo [pendiente](../plans/README.md) no se da por cumplido por estar escrito acá.

## Revisión y validación de cada cambio

Todo desarrollo sigue la [rutina compartida](../guides/validar-un-cambio.md): parte de un patrón existente, justifica lo nuevo, revisa responsabilidades y dependencias y valida el comportamiento. La entrega separa garantías automáticas, revisión y pendientes. Nunca se debilita una regla para que pase la implementación.

## Recorrido obligatorio de un caso de uso HTTP

```text
Cliente
  → Controller MVC (Api)
  → interfaz de servicio (Application/Interfaces/Services)
  → servicio de aplicación (Application/Services/<Área>)
  → interfaz de repositorio, lector o integración (Application/Interfaces)
  → implementación (Infrastructure)
  → ApplicationDbContext, Identity, SMTP u otro proveedor
```

El controller traduce HTTP al pedido del servicio, y el resultado a HTTP. El servicio coordina. El repositorio escribe o recupera un agregado; el lector resuelve consultas (proyecciones, filtros, paginado). Se puede separar lectura y escritura **sin** comandos, handlers, bus ni `IRepository<T>` genérico. Un servicio agrupa una responsabilidad coherente: no hay un servicio por endpoint ni un repositorio artificial por clase.

## Proyectos y dependencias

| Proyecto | Contiene | Puede depender de |
|---|---|---|
| `Domain` | entidades, value objects, reglas y errores; sin eventos de dominio ([ADR 0003](../decisions/0003-sin-eventos-de-dominio.md)) | BCL |
| `Application` | interfaces de los casos de uso, servicios, modelos, validación y configuración funcional | `Domain`, abstracciones de Microsoft y FluentValidation |
| `Infrastructure` | EF Core, `ApplicationDbContext`, repositorios, lectores, Identity, OpenIddict, correo, clientes externos y workers | `Application`, `Domain` |
| `Api` | controllers, contratos HTTP, autorización, cookies, redirecciones, errores y host | `Application`; `Infrastructure` solo desde la raíz de composición |
| `AppHost` | orquestación de Aspire | `Api` |
| `ServiceDefaults` | configuración técnica compartida de Aspire | ninguna capa de negocio |

No hay proyectos `Interfaces` ni `Repositories`: son carpetas. `Domain` no conoce otras capas, `Application` no conoce `Infrastructure` ni `Api`, e `Infrastructure` no conoce `Api`. Dónde va cada pieza: la tabla de [AGENTS.md](../../AGENTS.md#dónde-va-cada-cosa) y el [mapa de instrucciones](mapa-de-instrucciones.md), con Roles como área de referencia ([ADR 0004](../decisions/0004-roles-como-area-de-referencia.md)). Hay un contrato de servicio por punto de entrada, y no hace falta crear carpetas vacías.

## Reglas de ubicación y acceso

1. **Api:** cada ruta de negocio es una acción MVC que recibe servicios por interfaz; nunca `ApplicationDbContext`, `UserManager`, `RoleManager`, `IQueryable`, repositorios ni lectores. Son de Api el binding, la autorización HTTP, `Challenge`, cookies, redirecciones, el cuerpo crudo de un webhook y la conversión de `Result` a HTTP.
2. **Application:** coordina reglas, validadores, transacciones, persistencia y puertos, sin EF Core, Npgsql, `HttpContext`, `IActionResult` ni entidades de Identity. Sus contratos no exponen `IQueryable`, `ApplicationUser` ni `ApplicationRole`.
3. **Domain:** el modelo puro, con `IAuditable` e `ISoftDeletable`. Los contratos de persistencia van en Application.
4. **Infrastructure:** el EF de negocio va detrás de repositorios o lectores. `SignInService` (bloqueo, cookie, cierre de sesiones) usa Identity y OpenIddict sin consultas de negocio; las cuentas van por `IUserReader` e `IUserRepository`. `PermissionService` lee por `IPermissionReader`, sin Identity ni OpenIddict. Solo usan el contexto directo `UnitOfWork`, migraciones, seeders, health checks, interceptores y los stores de Identity, OpenIddict y Data Protection. Un contrato que solo usa Infrastructure puede quedar privado ahí.
5. **DI:** cada capa registra lo suyo y `Program.cs` compone, con un bloque por [módulo](#módulos-opcionales). Lifetimes compatibles con `DbContext` (normalmente scoped), sin service locator en la lógica de negocio.
6. **Servicios de fondo:** un worker de Infrastructure maneja programación, scopes y proveedores, e invoca un servicio de Application. Una operación EF de negocio sin caso de uso (una retención) va en un repositorio especializado, con el scheduler en Infrastructure. Lo que un servicio necesita correr en un scope nuevo lo pide por un puerto que implementa Infrastructure.

## Módulos opcionales

Un módulo que un proyecto derivado puede quitar ([ADR 0007](../decisions/0007-whatsapp-como-modulo-opcional.md); quitar [WhatsApp](../guides/quitar-whatsapp.md) o [IA](../guides/quitar-ia.md)) vive en `Modules/<Módulo>` de los mismos proyectos, con namespace `ArquitecturaBase.<Capa>.Modules.<Módulo>.…`, y cumple las mismas reglas que el núcleo: los tests comparan el namespace canónico (`ModuleNamespaces.Canonical`).

- **Frontera:** el núcleo no nombra un módulo, ni un módulo a otro, ni en el IL ni en el fuente (`using`, `cref`, `nameof`, constantes, `global using`); solo `Program.cs` los compone. `ApplicationDbContext` no expone entidades de módulos: usan `dbContext.Set<T>()`. Lo verifica `ModuleBoundaryTests`, sin excepciones, sobre el fuente (no sobre lo que EF genera en las migraciones).
- **Composición:** una clase `public static` `<M><Capa>Registration` por capa con tipos (salvo Domain), en la raíz del módulo, con `Add<M><Capa>(this IServiceCollection)` que devuelve la colección. `Program.cs` las llama en un bloque propio, después del núcleo. Cada una registra todo lo del módulo, incluidos sus validadores (`AddApplication` excluye `Application.Modules.*`), rate limits, convenciones de MVC, repositorios y lectores (no `PersistenceRegistration`). Opciones y convenciones usan `Configure`, así el orden no cambia el resultado (`ModuleBoundaryTests`).
- **Migraciones:** van en la cadena única, porque hay un solo `ApplicationDbContext`. Nombran las entidades con cadenas: compilan sin el módulo y no se borran al quitarlo. Quitar un módulo genera su propia migración, que borra sus tablas; sin ella falla `MigrationsTests.Model_has_no_pending_changes` y en Development la Api no arranca.
- **Puertos:** interfaz en `Application/Interfaces/Channels`, versión del núcleo (si hay) en `Application/Channels`, adaptador en `Modules/<M>/Channels`, fuera de `Services`. Tres formas:
  - **uno solo** (`IPhoneChannel`): el núcleo registra el apagado (`DisabledPhoneChannel`) con `TryAdd` y el módulo lo reemplaza con `Replace`;
  - **uno por canal** (`IInvitationChannel`, `IInvitationDeliveryStatusSource`): todos con `TryAddEnumerable`; el núcleo trae el correo (`EmailInvitationChannel`) y ninguna fuente de estado. Se elige por canal; dos del mismo canal son un error de registro;
  - **todos avisados** (`IPhoneLinkParticipant`): cada módulo suma el suyo con `TryAddEnumerable` y el núcleo no trae ninguno; `PhoneNumberLinker` los llama en orden de registro y toma sus locks antes que el de la cuenta.

  Registrar dos veces no duplica; en las dos primeras formas, el orden no cambia el resultado. Sin el módulo, el núcleo funciona con lo suyo ([identidad.md](../features/identidad.md), [administracion.md](../features/administracion.md)).
- **Otros puertos:** `IClientCapabilitySource` publica las capacidades instaladas fuera del valor cacheado de ajustes; una colección vacía es válida y un identificador vacío o repetido es un error de composición. `ISystemQuerySource` (`Interfaces/Channels`) aporta consultas tipadas, versionadas y autorizadas, y `ISystemQueryExecutor` (`Interfaces/Integrations/SystemQueries`) crea un scope por consulta, sin descubrir SQL ni exponer el DbContext ([IA](../features/inteligencia-artificial.md)).
- **Permisos:** `IPermissionCatalog` compone `Permissions.All` con las `IPermissionCatalogSource` de los módulos; roles, `RoleSeeder` y `PermissionService` usan ese catálogo, así un módulo quitado no conserva permisos efectivos. Un código duplicado falla al componer, y los textos salen en la cultura del pedido ([ADR 0010](../decisions/0010-catalogo-de-permisos-extensible.md), [receta](../guides/permiso-nuevo.md)).
- **Ubicación:** errores en la raíz de `Domain/Modules/<M>/` (el módulo es el área); helpers en `Application/Modules/<M>/Services`.
- **Tests** ([ADR 0013](../decisions/0013-composicion-de-modulos-en-tests.md)): los aportes viven en `tests/<Proyecto>/Modules/<M>` y se componen con `IApiTestModule`, `IModuleArchitectureContribution` y `tests/Shared/ModuleApplicationRegistration.cs`. El arnés del núcleo no nombra módulos ni usa `partial void`. Un test del núcleo pasa con y sin módulos. La reflexión se limita a los tests.

## Borde HTTP

Rige para las rutas de negocio. Un controller con protocolo propio (OpenIddict, redirección a un proveedor externo, webhook de un módulo) lleva `[OwnProtocol]`: queda fuera de la convención de OpenAPI y declara a mano sus respuestas.

1. **Entrada ([ADR 0002](../decisions/0002-contratos-http.md)):** cada parámetro de body o query, también el body que infiere `[ApiController]`, es un contrato de `Api/Contracts/<Área>` (`*HttpRequest` o `*Query`) o un valor simple (primitivo, enum, `string`, `Guid`, fecha u hora, o su nulable); una colección en la query necesita contrato; ruta, header y servicios quedan fuera (`ControllerInputContractTests`). El mapeo es a mano. Las respuestas serializan los `*Response` de Application: renombrar una propiedad cambia el contrato. Un contrato con correo, número, código, token o datos del perfil sobrescribe `ToString()`; los modelos de Application no.
2. **Autorización:** `[HasPermission(Permissions.<Área>.<Acción>)]`; nunca `[Authorize(Roles = …)]` ni `[Authorize(Policy = …)]` a mano (`PermissionAuthorizationTests`). Sin sesión, 401; sin permiso, 403. Un `[Authorize]` sin permiso solo pide sesión.
3. **Resultado** (`ControllerResultExtensions`): `ToActionResult` (200, o 204 sin valor), `ToAcceptedResult` (202, nunca con `Location`) y `ToCreatedResult(this, nameof(Get), id => new { id })` (201 con `Location`; la acción nombrada tiene que existir en el controller). Nada de `Accepted(...)`, `StatusCode(202)` ni `IsSuccess ? … : …` a mano. El error sale siempre de `ProblemDetailsMapper.FromError(error, factory, httpContext)` con el `ProblemDetailsFactory` de MVC (`ProblemDetailsMapperTests`), que deja el `title` traducido del `ErrorType` y el `code`. `ErrorType.Gone` da el 410 de un resultado durable vencido (`[ProducesProblem(410)]`, `Title.Gone`).
4. **ProblemDetails y JSON:** el `traceId` sale solo de `ProblemDetailsMapper.AddTraceId`. Las opciones JSON de MVC y de `Http.Json` salen de un único `ConfigureJson`, donde va todo conversor nuevo (`JsonOptionsTests`). El 400 `Request.Invalid` (cuerpo ilegible o parámetro inconvertible) no trae `errors`, para no mostrar la forma interna del modelo; los `errors` por campo salen solo de los validadores.
5. **OpenAPI:** cada acción declara su éxito con `[ProducesResponseType<T>(status)]` o `[ProducesResponseType(StatusCodes.Status204NoContent)]`. `ProblemResponsesConvention` declara los errores según la firma: 500 siempre; 400 con body o query; 401 si pide sesión sin `[AllowAnonymous]`; 403 si además pide permiso; 404 con un parámetro como `{id}`; 429 con `[EnableRateLimiting]` sin un `[DisableRateLimiting]` más cercano. Lo demás va con `[ProducesProblem(status)]`, solo si el servicio de verdad lo responde. `OpenApiTests` falla si una operación de `/api` no declara un 2xx con esquema (salvo un 204 o un 202 de su lista) o el 500, o si declara un error que no es ProblemDetails.

## Errores del framework, pipeline y JSON

- Los errores del framework (ruta inexistente, 405, 401/403) salen como ProblemDetails (`ProblemDetailsMapper.CompleteFrameworkProblem` y `UseStatusCodePages`), con códigos de `ApiErrorCodes`: `Http.*`, `General.Unexpected` para los 5xx y `Request.Invalid` para los demás 4xx. El 429 lo arma `RateLimitingExtensions`, con `retryAfter`.
- Todo middleware que pueda cortar con un error va después de `UseStatusCodePages`, o su respuesta sale vacía. `UseAuthentication` y `UseAuthorization` se declaran explícitos, y `UseRateLimiter` va entre los dos, para particionar por operador autenticado; las políticas por IP cubren lo anónimo.
- Los enums viajan por nombre (`JsonStringEnumConverter`), nunca por número.

## Validación, guardado y errores

- **Validación:** FluentValidation valida **antes** de cualquier cambio; un pedido inválido no abre transacción. Servicios y helpers reciben un solo `IRequestValidator` (scoped), que resuelve los `IValidator<T>` del tipo **estático** del pedido: pasado como tipo base u `object`, no valida nada.
- **Declaración:** los errores van en clases `public static` `<Entidad>Errors` en `Domain/<Área>/` (no en `Common`, `Results` ni `ValueObjects`), aunque solo los use Application. El prefijo del código no tiene que coincidir con la carpeta y no cambia si la clase se muda.
- **Quién arma un `Error`:** fuera de Domain, nadie usa las fábricas (`Failure`, `Validation`, `Unauthorized`, `Forbidden`, `NotFound`, `Conflict`, `TooManyRequests`), el constructor ni un `with`. De `ValidationError`, el constructor de mensajes por campo se usa solo en `RequestValidator`, `FieldErrors` y `ExternalLoginController`, y el de código, descripción y mensajes, solo en `FieldErrors`; nunca un `with` (`ErrorDeclarationTests`).
- **Log de la operación:** un método público de servicio que devuelve `Result` envuelve todo su cuerpo en `OperationLog.RunAsync(logger, "Operación", () => …)`: "Handling" antes, "Handled" o "failed with {ErrorCode}" (`Warning`) después, nunca el pedido. No atrapa excepciones. Un punto de entrada sin `Result` (el webhook de WhatsApp) usa `OperationLog.Handling` y `Handled`. Nadie más declara esos textos (`OperationLoggingTests`).

## Una sola forma de guardar

`IUnitOfWork.ExecuteInTransactionAsync(trabajo, CommitPolicy, ct)` es la única forma de guardar: una vez por método público de un servicio que escribe ([ADR 0001](../decisions/0001-transaccion-explicita-por-caso-de-uso.md)). El ejemplo a copiar es `RoleService.UpdateAsync` con su `UpdateCoreAsync`.

1. **Antes:** `ThrowIfNull`, el log de inicio y el validador.
2. **Un solo límite:** `OnSuccess`, u `OnAnyResult` si también hay que guardar al devolver error, con un comentario que diga qué queda registrado.
3. **Adentro, en orden:** locks, lecturas de lo que se modifica, reglas, escrituras y los efectos marcados en la fila (encolar, `MarkSent`). Los locks: `login-code:` del correo y del número (dos llamadas) → participantes del número → `login-link:` o `user-invitation:` → `users:admins`, solo si se va un administrador activo.
4. **Después y solo si se confirmó:** invalidar caché, la cookie (`ISignInService.SignInAsync` lanza dentro de un límite), `Notify` y el log de resultado. Quien abre el límite no lo envuelve en try/catch. Solo lo atrapan quienes corren el límite de otro como unidad: el reintento único ante un 23505, en un scope nuevo, y los hosts en segundo plano.
5. **Helpers** ([sufijos](#convención-de-sufijos-de-los-helpers)): nunca reciben `IUnitOfWork` ni guardan. Un servicio no llama al método de escritura de otro: anidar lanza.

La única excepción, que no se copia, es un servicio sin límite que llama al punto de entrada que lo abre, porque esa es la unidad que se reintenta: el webhook de WhatsApp ([whatsapp.md](../features/whatsapp.md)).

`TransactionBoundaryTests` (IL) fija que solo un punto de entrada (clase de `Application/Services` con contrato en `Interfaces/Services`) recibe `IUnitOfWork` y abre el límite, salvo `DatabaseSeeder`; que solo `PersistenceRegistration` nombra `UnitOfWork` y solo `UnitOfWork` guarda; que el SQL y las claves de los locks viven en un solo lugar; que `IUnitOfWork` tiene un solo método; y que el núcleo no usa `ExecuteUpdate`/`ExecuteDelete` (un módulo declara los suyos en `IModuleArchitectureContribution.BulkUpdateOwners`). No verifica que cada escritura abra un límite, ni uno solo: eso lo cubren las escrituras que exigen la transacción y la revisión.

### Locks

- Un repositorio pone en fila las operaciones sobre un recurso con `pg_advisory_xact_lock` (clave de `AdvisoryLockKeys`) o `FOR NO KEY UPDATE`, dentro de la transacción; sin ella lanza `InvalidOperationException`, también con cero claves.
- Solo `ExecuteInTransactionAsync` abre la transacción, en READ COMMITTED y sin subir el aislamiento. No hay `EnableRetryOnFailure` (si se activa, por ejemplo con `AddNpgsqlDbContext` de Aspire, `BeginTransactionAsync` lanza) ni estrategia de ejecución, porque el trabajo encola mensajes y no se puede repetir.
- Las claves del núcleo están solo en `AdvisoryLockKeys` (`login-code:`, `login-link:`, `user-invitation:`, `external-login:`, `users:admins`, `seed:database`); las de un módulo, en las suyas. El texto es el lock: cambiarlo deja de poner en fila a quien use el viejo. Un prefijo nuevo va también en `LockKeyPrefixes` de `TransactionBoundaryTests` (el de un módulo, en `IModuleArchitectureContribution.LockPrefixes`).
- En una llamada, las claves se toman ordenadas y sin repetir (`AdvisoryLockExtensions`); entre llamadas rige el orden de la regla 3: `users:admins` último ([administracion.md](../features/administracion.md)) y los participantes antes que la cuenta ([whatsapp.md](../features/whatsapp.md)). Cada módulo documenta sus locks.

### Identity y escrituras que exigen la transacción

- `UserManager` y `RoleManager` autoguardan (no se toca `AutoSaveChanges`) dentro de la transacción, con un savepoint por guardado: un 23505 traducido se puede atrapar; un error de SQL crudo (55P03, 40P01), nunca.
- Las escrituras de `IUserRepository`, `IRoleRepository` e `ISignInService` lanzan `InvalidOperationException` fuera de un límite, también en un test. Los roles se escriben solo por `IRoleRepository`.
- Quedan fuera a propósito la retención de WhatsApp, `ConnectService.RevokeAuthorizationAsync`, las migraciones, el servidor OpenIddict y Data Protection.

### El seed de arranque

`SeedExtensions.SeedDatabaseAsync` usa `DatabaseSeeder` en un scope propio: un límite `OnSuccess` con el lock `seed:database` que corre roles, ajustes y OpenIddict, y después los `IDatabaseSeedParticipant` de los módulos (con `TryAddEnumerable`; no abren límite, no guardan ni llaman a proveedores). Los errores son excepciones: `UnitOfWork` deshace, vacía el tracker y relanza. Dos réplicas siembran una después de otra. Nada del seed llama a `SaveChanges`, y los managers de OpenIddict no abren transacción propia (`DatabaseSeederTests`).

### Robustez proporcional al efecto

La regla está en AGENTS.md ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md)): la configuración interna (borradores, catálogos, reglas, diseños, ajustes) usa este patrón con revisión y 409; lo durable se reserva para efectos externos, mensajes a personas y efectos que dispara una regla. Si un plan pide más para algo interno, prevalece esta regla salvo que el cambio justifique el riesgo.

## Caché compartido en Redis

Redis directo es el único caché de datos ([ADR 0009](../decisions/0009-redis-como-unico-cache-de-datos.md)). `Program.cs` llama a `AddRedisCaching` (un `IConnectionMultiplexer` por proceso) y `AddInfrastructure` registra el adaptador. Application y Domain no conocen el cliente. Una lectura nueva sigue [agregar-cache.md](../guides/agregar-cache.md).

- **Fábricas:** `RedisCache.GetOrCreateInOwnScopeAsync` es la única forma de llenar Redis; no hay `Set` público. La fábrica lee en su propio scope, para no cachear algo sin confirmar (por eso `PermissionService` y `SystemSettingsReader` se pueden llamar dentro de un límite). Su estado lleva identificadores o filtros, nunca servicios, contextos ni entidades seguidas, y respeta el token. Una fábrica por fila o por pedido puede agotar el pool: definí cachés por lectura útil, con TTL.
- **Permisos** ([identidad.md](../features/identidad.md)): clave `permissions:role:<id>:<revisión>`, con el `ConcurrencyStamp` confirmado (TTL de una hora); si el rol cambió, la fábrica reintenta hasta cuatro veces. Los claims cambian solo por Identity.
- **Ajustes:** claves y TTL en [administracion.md](../features/administracion.md). El seed descarta permisos y ajustes después de confirmar. Redis puede estar caliente al reiniciar la Api.
- **Concurrencia:** un miss toma una lease con token y vencimiento y vuelve a leer antes de la fábrica; solo publica o libera quien tiene el token. El descarte borra dato y lease, así un llenado anterior no queda cacheado. Como la lease vence, la fábrica puede repetirse y solo lee. Dato y lease comparten hash tag (Redis Cluster). Las leases no reemplazan los locks de PostgreSQL.
- **Límites:** espera y fábrica de 30 s, lease de 15 s, reintento cada 25 ms, comandos y conexión de 3 s. Un resultado de más de 1 MiB no se guarda. Las opciones se ajustan en `Caching`.
- **Disponibilidad:** Redis es obligatorio (`/health` lo incluye, `/alive` no), sin fallback. Si el descarte falla después del commit, la escritura quedó y la respuesta es 500: no se repite la transacción.
- **Despliegue:** fuera de Aspire, `ConnectionStrings__cache` es obligatorio. `Caching__KeyPrefix` separa instalación y ambiente y coincide entre réplicas; el predeterminado no distingue dos servidores con bases del mismo nombre. Producción: red privada y TLS ([despliegue](../guides/despliegue.md)).
- **Pruebas:** `CacheBoundaryTests` prohíbe un cliente o una escritura fuera del adaptador, y también `AddMemoryCache`, `AddDistributedMemoryCache` y `AddHybridCache`. Las métricas no registran claves ni datos personales. La memoización de recursos embebidos, las colas y el rate limiter tienen reglas propias.

## Colas transaccionales

El correo y las colas de acceso de los módulos encolan contenido cifrado en `QueuedDeliveries`, en la transacción del caso de uso ([ADR 0015](../decisions/0015-colas-transaccionales-cifradas.md)). `TryEnqueue` exige el límite y no guarda ni llama al proveedor. La fila se ve después del commit: un rollback deshace el envío y un reinicio conserva los pendientes.

- **Capacidad:** `Email:QueueCapacity` o `<M>:QueueCapacity`, de 1 a 10.000 (100 por defecto), contada bajo el lock `delivery:<canal>`. `false` significa sin capacidad, contenido vencido o módulo apagado; encolar no confirma la entrega. Insertar nunca espera a SMTP ni a Meta.
- **Cifrado y vencimiento:** Data Protection con un propósito por canal y tipo; nada legible en la tabla, en logs ni en `ToString()`. Un código conserva su vencimiento; el resto vence a un día. Al reservar se borran los vencidos bajo `delivery-retention:expired`, antes del lock del canal; el consumidor de correo también limpia lo de un módulo apagado. Al completar, se borra el contenido.
- **Réplicas y caídas:** `IDeliveryQueueService` confirma la reserva antes de llamar al proveedor, sin transacción abierta. Una fila reservada no se reintenta: queda incierta y se borra al vencer. No hay entrega exactamente una vez.
- **Sobrecarga:** si el mensaje acompaña al caso de uso, se confirma sin enviar (sin `MarkSent` o con `MarkSendFailed`). Si el caso de uso es solo la orientación de acceso, se lanza y se revierte.
- **Piezas:** `DurableMessageStore` inserta; Delivery reserva y completa con `IUnitOfWork`. Los codecs de un módulo quedan en su carpeta. Las colas con `Channel` solo existen en pruebas de transporte.

Las claves de Data Protection se cifran antes de guardarse ([ADR 0016](../decisions/0016-proteccion-de-claves-data-protection.md)): con DPAPI en Development sobre Windows y, fuera de eso y de Testing, con un PFX externo obligatorio ([despliegue](../guides/despliegue.md#data-protection)).

## Convención de sufijos de los helpers

Un helper es una pieza interna de un área (`Application/Services/<Área>` o `Application/Modules/<M>/Services`) que no implementa un contrato de `Interfaces/Services`. Se registra por su tipo concreto (`services.AddScoped<UserGuard>()`), es `internal sealed` y termina en uno de estos sufijos:

| Sufijo | Para qué |
|---|---|
| `Policy` | decide |
| `Guard` | protege una regla |
| `Issuer` | emite |
| `Verifier` | verifica |
| `Linker` | vincula |
| `Revoker` | revoca |
| `Recorder` | registra |

No se usan `Operations` ni `Change`, ni helpers en la raíz de `Services` o del módulo. No son helpers los adaptadores de puertos, los validadores, `RequestValidator`, las opciones ni los tipos estáticos o creados con `new` (`ApplicationHelpersTests`).

## Nombres de repositorios y lectores

Rige para los métodos de `Application/Interfaces/Persistence`, salvo `IUnitOfWork`, según el tipo de retorno ([ADR 0008](../decisions/0008-nombres-de-repositorios-y-lectores.md)):

| Prefijo | Devuelve | Dónde |
|---|---|---|
| `Get…` | una entidad seguida por EF, para modificarla (si toma lock de fila, su XML lo dice); `null` si no existe | solo `*Repository` |
| `Find…` | una proyección, un registro o un escalar, nunca una entidad; `null` o un valor por defecto documentado | lectores y repositorios |
| `List…` | una colección (no `string`) o `PagedResult<T>` | los dos |
| `Exists…` | `Task<bool>` | los dos |
| `Count…` | `Task<int>` o un registro terminado en `Counts`; cuenta en SQL | los dos |
| `Lock…` | `Task`: toma un lock de Postgres y exige la transacción | solo repositorios |
| `Add` | `void`: da de alta en el contexto; lo guarda el límite | solo repositorios |
| `Create…`, `Update…`, `Delete…`, `Set…`, `Remove…`, `Restore…`, `Clear…`, `Add…Async` | escrituras; exigen la transacción | solo repositorios |

- Un lector (`I*Reader`) solo tiene `Find`, `List`, `Exists` y `Count`, y es el único que usa `AsNoTracking`.
- No siguen la tabla los contratos de `Interfaces/Integrations` ni un tipo terminado en `Reader` fuera de `Interfaces/Persistence`.
- Un lector no descarta su caché: lo hace un contrato de `Interfaces/Integrations/Caching` (`ISystemSettingsCache.InvalidateAsync`, después del commit), y la clave vive en la implementación del caché. `IPermissionService.InvalidateRoleAsync` queda en `Integrations/Identity`.
- Lo verifica `PersistenceNamingTests`.

## Modelos: Response y Row

- **La salida de un servicio termina en `Response`:** el tipo de nivel superior que devuelve un método de `Interfaces/Services`, sin contar `Task`, `Result<T>`, `PagedResult<T>` ni la colección. Quedan afuera los tipos anidados, los escalares y `UserFilterCounts` (`ServiceOutputNamingTests`).
- **Una fila que proyecta un lector termina en `Row`** y vive en `Models/<Área>`, sin subcarpeta. El servicio la traduce a su `Response`, así la consulta cambia sin tocar el contrato HTTP. Queda afuera `UserAccount`, la vista de la cuenta de `IUserReader`. Esta regla no tiene test propio.

## Migraciones

- `dotnet ef migrations add` recibe las conexiones de PostgreSQL y Redis como argumentos, sin conectarse. El bundle usa `--connection` para la base real y no corre el seed. Comando y trampas: [guía de la migración](../guides/migracion.md).
- En Development, la Api las aplica al iniciar; fuera de Development rige el [ADR 0006](../decisions/0006-migraciones-y-seed-fuera-de-development.md). `MigrationsTests` falla si falta una migración.
- **Una migración de datos se escribe a mano dentro de la generada.** Si cambia un valor guardado y no el modelo (un enum guardado como texto), el `UPDATE` va en `Up` y el inverso en `Down`, con un comentario y un test que migre filas y vuelva atrás (`StoredValuesMigrationTests`).
- Un renombre de columna tiene que salir como `RenameColumn`, nunca `DropColumn` y `AddColumn`. Ninguno de estos dos casos convive con la imagen anterior en un despliegue ([despliegue](../guides/despliegue.md#el-orden-bundle-después-imagen)).

## Excepciones de protocolo y conservación funcional

- `/connect/revoke`, `/connect/introspect` y `/.well-known/*` son de OpenIddict, y los endpoints de Aspire conservan su framework. Las acciones propias de `/connect` son controllers MVC (passthrough, PKCE, cookies, formularios, redirecciones). Nada de esto habilita Minimal APIs de negocio.
- Un cambio de estructura conserva contratos HTTP, autorización, rate limits, OpenIddict, front, workers y módulos. El inventario verbo/ruta es `ExplicitRouteInventoryTests`. Las rutas condicionales de un módulo aparecen o desaparecen del enrutamiento según su configuración: un 404 dentro de una ruta registrada no equivale a omitirla.
- `POST /api/users` y `POST /api/roles` responden 201 con `Location` al detalle. En OpenAPI, los esquemas de los cuerpos llevan el nombre de su contrato.
- Una base vacía arranca con esquema, seed y flujos principales; una desplegada conserva su cadena de migraciones y sus datos.

## Front y hosting del SPA

El SPA vive en `../ArquitecturaBaseFront` (React + Vite) y AppHost lo levanta. Un prefijo de backend nuevo (`BackendPrefixes`) sigue su [guía](../guides/prefijo-de-backend.md).

- **Un solo origen, sin CORS.** En desarrollo, Vite (`https://localhost:5173`) sirve el SPA y reenvía a la Api los prefijos de su `server.proxy`: `/api`, `/account`, `/connect`, `/signin-google`, `/.well-known` y `/webhooks`. En producción la Api sirve las dos cosas. `/webhooks` es del núcleo y queda aunque se quite el módulo, para que una ruta inexistente bajo él responda ProblemDetails y no el `index.html`.
- **El issuer es el origen público** (`Authentication:Issuer`). `SetIssuer` cambia solo el campo `issuer`; el resto del discovery sale del `Host` del pedido, por eso el proxy usa `changeOrigin: false`. Si el front cambia de origen, se mueven también las redirect URIs del cliente `web`.
- **El fallback no toca el backend:** `UseSpaFallback` es un middleware, no un `MapFallback`. Atiende solo GET y HEAD sin endpoint, que no parecen un archivo ni empiezan con un prefijo de backend; así la Api conserva sus ProblemDetails, su 405 y su 415.
- **Encabezados y cookie del navegador.** `UseSecurityHeaders` (`Api/Hosting/SecurityHeadersExtensions.cs`) agrega a toda respuesta una CSP limitada al propio origen (estilos en línea, imágenes `https:`/`data:`, `frame-ancestors 'self'` y `form-action` hacia Google), `X-Content-Type-Options: nosniff` y `Referrer-Policy: no-referrer`; fuera de Development se suma HSTS. Una fuente externa nueva (script, conexión, iframe o destino de formulario) cambia esa política y `SpaHostingTests`. La cookie de Identity es `HttpOnly`, `Secure` y `SameSite=Lax`, de 30 días deslizantes. `/account` solo acepta JSON: un formulario o un `text/plain` recibe 415 (`LoginCodeEndpointsTests`), lo que evita el CSRF de ingreso sin CORS.
- Fuera de Vite, la Api sirve el SPA desde `wwwroot` (generado, sin versionar); sin `wwwroot/index.html`, el middleware no se instala. El despliegue copia todo `dist/` antes de publicar .NET ([despliegue](../guides/despliegue.md#front-dentro-de-la-imagen)).

## Tests: arquitectura y arnés

**ArchitectureTests.** Cada regla de este documento cita su test. Además:

- reglas de capas (NetArchTest y referencias de cada `.csproj`) y `ControllerServiceRepositoryTests` (controllers → servicios, también en módulos);
- `ApplicationPackagesTests` (paquetes de una lista, sin `FrameworkReference`), `ApplicationPublicApiTests` (sin `IQueryable` ni `Expression` públicos), `MinimalApiRoutesTests` (sin `Map*` fuera de ServiceDefaults), `ApplicationServicesTests` (cada `*Service` con su interfaz), `ApiRequestContextTests` (adaptadores en `Api/RequestContext`) y `EntityConfigurationTests` (una configuración por entidad);
- `ErrorCodeTests`: claves `Area.Entidad.Motivo`, salvo las reservadas (`Title.*`, `Validation.Failed`, `ApiErrorCodes`), que tienen que seguir existiendo;
- `IdentityBoundaryTests` (IL): los límites de Identity, listados en [identidad.md](../features/identidad.md);
- `PersistenceRegistrationTests` (IL): en `src`, solo `PersistenceRegistration`, el `<M>InfrastructureRegistration` (lo suyo), `SeedExtensions` y `DatabaseSeeder` nombran repositorios, lectores o seeders concretos;
- `ServiceDependencyLimitTests`: hasta 8 dependencias por constructor en `Application.Services`, contando `ILogger`, `IOptions` y `TimeProvider`, sin excepciones: la clase que se pasa se parte por responsabilidad.

Una regla sin llamada real prueba su detector con un caso de control. Un paquete, una clave reservada o un `Map*` técnico nuevo se suma a la lista de su test, con el motivo.

**Api.IntegrationTests:** `ApiFactory` usa Testcontainers `postgres:18.3` y `redis:8.6`; el prefijo del caché sale de la base, así dos réplicas con la misma base comparten claves. Reutiliza el registro del DbContext de producción (solo cambia a `TestDbContext`); no se registra otra vez. `AuthFlow.LoginAsync` hace el ingreso real; `X-Test-UserId` usa el usuario de prueba. `factory.EmailSender` guarda los correos (el código es la primera palabra del asunto). Un test del núcleo no usa lo de un módulo (`IApiTestModule`); a lo sumo lo apaga nombrando una clave como texto (`UseSetting("WhatsApp:PhoneNumberId", "")`). Los datos que se arman con escrituras de cuentas o roles van en `factory.InTransactionAsync(...)`. Los límites están relajados; para probar uno, `factory.WithWebHostBuilder(...)`.

**Unidad de trabajo en los tests:** `FakeUnitOfWork` aplica la regla de producción (`CommitPolicyExtensions.Commits`) y cuenta transacciones, commits y rollbacks; `CommitFailure` hace fallar el commit. Los dobles de lock, `InMemoryUserAccounts` y `FakeSignInService` lanzan fuera del límite; los datos previos van en `InMemoryUserAccounts.ArrangeAsync`. `FakeSignInService.SignInAsync` lanza dentro del límite. En integración, `FailingCommitUnitOfWork.Replace(services, probe)` hace fallar el commit real.

## Cómo mantener fija esta decisión

Antes de una función nueva, ubicá cada pieza en el recorrido; si no encaja, aclará su responsabilidad en vez de sumar un handler o un repositorio genérico. No se reintroducen `Application/Features`, `Api/Endpoints`, `IEndpoint`, `ICommandHandler`/`IQueryHandler`, sus decoradores ni Scrutor. El cierre de un área pide unitarias, integración HTTP, persistencia y transacción, inventario de rutas y la suite completa. Esta decisión solo cambia con un ADR nuevo; mover un archivo o agregar una funcionalidad no cambia los límites de las capas.

## Presentación general y precedencia (2026-09-30)

El contrato está en [administracion.md](../features/administracion.md#configuración-general-2026-09-30).

- **Idioma del pedido:** manda `Accept-Language`. Si no trae un idioma admitido, se usa el idioma general de `ISystemSettingsService`, solo en `/api`, `/account` y `/connect`, sin EF ni HTTP a la propia API. Las demás rutas usan `es`.
- `UseExceptionHandler` envuelve la localización para cubrir una falla de Redis, y `GlobalExceptionHandler` conserva `IRequestCultureFeature` al traducir el error.
- **Tamaño de página:** si el listado paginado de usuarios o de roles lo omite, el controller lo resuelve con el servicio de configuración; uno explícito se valida entre 1 y 100.

## Formularios estructurados (módulo BusinessForms)

Reglas en [formularios.md](../features/formularios.md); lo del canal, en [whatsapp.md](../features/whatsapp.md#publicación-durable-de-flows--2b-2026-10-10).

- Puertos neutrales en `Application/Interfaces/Channels`, con modelos en `Models/StructuredData`: `IStructuredFormChannel`, `IStructuredFormSource`, `IStructuredRecordSink` e `IStructuredRecordSource`.
- Entre BusinessForms y el canal: commits separados, IDs cruzados opacos, sin FK ni joins. Nunca se llama al escritor de otro módulo dentro de una transacción.
