# ArquitecturaBase: reglas para agentes

Plantilla base .NET 10 + Aspire 13.5 + PostgreSQL; el front vive en `../ArquitecturaBaseFront`. Este archivo es el índice: tiene las reglas de la plantilla, que valen para cualquier proyecto que salga de ella, y enlaza el detalle ([Más documentación](#más-documentación)). La arquitectura canónica del backend es [`docs/architecture/backend.md`](docs/architecture/backend.md): si una descripción histórica la contradice, prevalece ella. Lo propio de un área del producto (identidad, WhatsApp, administración) está en `docs/features/` y se lee antes de tocar esa área. Las tareas que se repiten tienen su guía paso a paso en `docs/guides/`.

## Forma de trabajo

- Se trabaja directo en `main`. No crear ramas ni hacer push sin un pedido explícito. Commits chicos, en español, con conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`).
- TDD donde hay lógica: el test en rojo primero. Antes de dar algo por terminado: el build sin advertencias y `dotnet test` en verde, y `aspire stop` si levantaste la app. Los tests de integración necesitan Docker: sin Docker corren el build y Domain, Application y Architecture, y el trabajo no se da por cerrado hasta correr la integración.

## Comandos

- Compilar: `dotnet build ArquitecturaBase.slnx`. Todos los tests: `dotnet test` (modo Microsoft Testing Platform, configurado en `global.json`).
- Un proyecto o una clase: `dotnet test --project tests/<Proyecto>/<Proyecto>.csproj -- --filter-class "<Namespace.Clase>"`.
- Levantar todo: `aspire run` desde la raíz (Postgres + Api + front). **Apagarlo siempre al terminar de probar: `aspire stop`.** Si queda corriendo, el arranque desde Visual Studio falla con `address already in use` y los DLL quedan bloqueados. El contenedor de Postgres sí sobrevive a propósito (`ContainerLifetime.Persistent`).
- Una migración sigue [`docs/guides/migracion.md`](docs/guides/migracion.md).

## Capas y dependencias

Las verifica `tests/ArquitecturaBase.ArchitectureTests`; qué contiene cada proyecto y las reglas de acceso, en [backend.md, "Proyectos y dependencias"](docs/architecture/backend.md#proyectos-y-dependencias) y ["Reglas de ubicación y acceso"](docs/architecture/backend.md#reglas-de-ubicación-y-acceso).

| Proyecto | Puede referenciar |
|---|---|
| Domain | nada (solo la BCL): el modelo y las reglas de negocio puras |
| Application | Domain y los paquetes de la lista de `ApplicationPackagesTests` (FluentValidation y algunos Microsoft.Extensions.*); sin EF Core ni ASP.NET Core, sin `ApplicationDbContext`, `HttpContext` ni tipos de Infrastructure o Api |
| Infrastructure | Application, Domain. Las consultas EF de negocio quedan detrás de repositorios o lectores; el contexto directo, solo en componentes técnicos (migraciones, seed, health checks, stores, interceptores, Unit of Work). `IQueryable` nunca sale de acá |
| Api | Application, Infrastructure (solo desde `Program.cs`, para registrar dependencias), ServiceDefaults. No hay `Api/Services`: los adaptadores de la petición van en `Api/RequestContext` |
| AppHost | Api (como recurso de Aspire) |

## Casos de uso MVC y borde HTTP

- Un request de negocio recorre `Api/Controllers → Application/Interfaces/Services → Application/Services/<Área> → Application/Interfaces/{Persistence,Integrations} → Infrastructure`. Los controllers inyectan interfaces de servicios (nunca EF, repositorios, lectores ni handlers); los servicios coordinan y los repositorios o lectores encapsulan EF. CQRS puede separar lectura y escritura sin `ICommand`, `IQuery`, handlers ni repositorio genérico, tampoco por simetría. Los mapeos se escriben a mano.
- Las rutas de negocio son controllers MVC en `api/<recurso>`, sin versión ([ADR 0005](docs/decisions/0005-sin-versionado-de-api-por-ahora.md): el primer cambio incompatible introduce `Asp.Versioning` con un ADR nuevo). No agregar Minimal APIs de negocio ni reintroducir `IEndpoint`, `ICommandHandler`, `IQueryHandler`, `Application/Features`, sus decoradores o Scrutor; los endpoints técnicos de OpenIddict y Aspire conservan su framework.
- La entrada de cada acción es un contrato de `Api/Contracts/<Área>` (`*HttpRequest` o `*Query`) mapeado a mano ([ADR 0002](docs/decisions/0002-contratos-http.md)); si lleva datos personales, códigos o tokens, sobrescribe `ToString()`, porque MVC registra los argumentos. El `Result` se responde con `ToActionResult`, `ToAcceptedResult` o `ToCreatedResult`. Respuestas, OpenAPI y sus tests: [backend.md, "Borde HTTP"](docs/architecture/backend.md#borde-http).
- Las rutas piden permisos, nunca roles, con `[HasPermission(Permissions.X)]` y nunca con `[Authorize(Policy = …)]` armado a mano; sin sesión responden 401 y sin el permiso, 403. Un permiso nuevo sigue [`docs/guides/permiso-nuevo.md`](docs/guides/permiso-nuevo.md): `Permissions.cs` y `Permissions.All`, sus textos en los dos `.resx`, el seed se lo da a Admin, e `IPermissionService.InvalidateRoleAsync` cuando cambian los permisos de un rol.
- Cada ruta figura en el inventario (`ExplicitRouteInventoryTests`) y tiene su test. Una ruta nueva no cambia la arquitectura: conserva verbo, autorización, rate limit, cuerpos, status, errores y transacciones, y amplía el inventario y sus pruebas. No mapear una combinación dos veces.
- Las pruebas de arquitectura exigen controllers → servicios, límites entre capas y ausencia del pipeline anterior. Cambiar esta estructura requiere un [ADR](docs/decisions/README.md) nuevo; agregar una funcionalidad o mover un archivo no cambia la regla. Un área nueva sigue [`docs/guides/agregar-un-area.md`](docs/guides/agregar-un-area.md).

## Una sola forma de guardar

- `IUnitOfWork.ExecuteInTransactionAsync(trabajo, CommitPolicy, ct)` es la única forma de guardar, una vez por cada método público de un servicio que escribe ([ADR 0001](docs/decisions/0001-transaccion-explicita-por-caso-de-uso.md)): `OnSuccess`, u `OnAnyResult` cuando también hay que guardar al fallar (intentos, códigos o enlaces consumidos, auditoría). Las consultas no abren límite. FluentValidation y `Result`/`Result<T>` siguen vigentes.
- Las cinco reglas (qué va antes, adentro y después del límite; los helpers no guardan; no se anida), el ejemplo a copiar (`RoleService.UpdateAsync`), la excepción, los locks y `TransactionBoundaryTests`: [backend.md, "Una sola forma de guardar"](docs/architecture/backend.md#una-sola-forma-de-guardar).

## Result y errores

- Las reglas de negocio devuelven `Result` / `Result<T>` con un `Error` y no lanzan. Las excepciones son para bugs y fallas de infraestructura: `GlobalExceptionHandler` responde un 500 genérico con `traceId`.
- Los errores se declaran en `<Entidad>Errors`, en `Domain/<Área>/`, aunque solo los use Application (el prefijo del código no tiene que coincidir con la carpeta). Nadie fuera de Domain arma un `Error` (ni fábricas, ni constructor, ni `with`); de `ValidationError` solo se usan su constructor de mensajes por campo (`RequestValidator`, `FieldErrors`, `ExternalLoginController`) y el de código, descripción y mensajes (solo `FieldErrors`), nunca un `with`. Lo verifica `ErrorDeclarationTests`. Los códigos son estables, con formato `Area.Entidad.Motivo` (`Auth.LoginCode.Expired`), y son la clave de la traducción en `Errors.resx`. Detalle: [backend.md, "Validación, guardado y errores"](docs/architecture/backend.md#validación-guardado-y-errores).
- Toda respuesta de error es ProblemDetails con `title` y `detail` traducidos, `code`, `traceId` y `errors` en las validaciones (campo en camelCase → mensajes). El 400 de un cuerpo ilegible (`Request.Invalid`) no trae `errors` a propósito.
- Todo middleware que pueda cortar con un error va en `Program.cs` después de `UseStatusCodePages`, o su respuesta sale vacía. Los errores del propio framework (404, 405, 401/403, 429): [backend.md, "Errores del framework, pipeline y JSON"](docs/architecture/backend.md#errores-del-framework-pipeline-y-json).

## Persistencia

- Contratos de repositorios y lectores en `Application/Interfaces/Persistence`; implementaciones EF en `Infrastructure/Persistence/{Repositories,Readers}`. No hay repositorio genérico ni uno obligatorio por entidad. Los métodos se nombran por lo que devuelven (`Get` una entidad seguida, solo en repositorios; `Find` una proyección; `List`, `Exists`, `Count`, `Lock` y los verbos de escritura), y un lector solo lee y es el único que usa `AsNoTracking`: [backend.md, "Nombres de repositorios y lectores"](docs/architecture/backend.md#nombres-de-repositorios-y-lectores) ([ADR 0008](docs/decisions/0008-nombres-de-repositorios-y-lectores.md)), verificado por `PersistenceNamingTests`.
- Las entidades heredan de `Entity` (Id Guid v7), sin eventos de dominio ([ADR 0003](docs/decisions/0003-sin-eventos-de-dominio.md)), y cada una tiene su `IEntityTypeConfiguration<T>` en `Infrastructure/Persistence/Configurations/`.
- `IAuditable` e `ISoftDeletable` los completan los interceptores; nunca se setean a mano. `ExecuteUpdate`/`ExecuteDelete` saltean los interceptores: no se usan con esas entidades (se borraría físicamente y sin auditoría). Las filas borradas se ocultan con un filtro global; para verlas, `IgnoreQueryFilters()`.
- Paginado: el pedido usa `PagedRequest` con `SortableFields`, el validador `PagedRequestValidator<T>`, e Infrastructure ordena con `ApplySort` (los mismos nombres y un desempate único, normalmente el Id) y pagina con `ToPagedResultAsync`. Filtros y conteos de un listado: [administracion.md, "Filtros de un listado"](docs/features/administracion.md#filtros-de-un-listado) y ["Conteos por opción de filtro"](docs/features/administracion.md#conteos-por-opción-de-filtro).

## Fechas: siempre en UTC

- `DateTime` en UTC de `TimeProvider` inyectado (`timeProvider.GetUtcNow().UtcDateTime`). `BannedSymbols.txt` rompe el build con `DateTime.Now`, `DateTime.Today`, `DateTime.UtcNow`, `DateTimeOffset.Now` y `DateTimeOffset.UtcNow`. Las propiedades terminan en `Utc`; sin hora, `DateOnly`. La API responde ISO 8601 con `Z` y rechaza fechas sin offset (`UtcDateTimeConverter`). En los tests, `FakeTimeProvider`.

## Idioma y textos

- Identificadores, mensajes de excepción y logs, en inglés. El idioma de la petición sale de `Accept-Language` (español por defecto, o inglés). Todo texto que ve el usuario sale de `Application/Resources/Errors.resx` y `Validation.resx` y sus `.en.resx`: cada clave en los dos idiomas (`ResourceParityTests`), en español rioplatense con voseo ("Ingresá", "Revisá").
- Logs con `[LoggerMessage]`, nunca `logger.LogX(...)` (`CA1848` rompe el build). Nunca registrar códigos, tokens, enlaces de ingreso ni secretos. `Microsoft.AspNetCore` queda en `Warning` y las cadenas de conexión no llevan `Include Error Detail`; los motivos, el enmascarado de teléfonos y los logs de `HttpClient` de Meta, en [`docs/features/whatsapp.md`](docs/features/whatsapp.md).

## Build

- `TreatWarningsAsErrors`, analizadores `latest-recommended` y estilo en el build. Las advertencias se corrigen; solo se suprimen en `.editorconfig`, con una justificación. Las versiones de los paquetes van solo en `Directory.Packages.props`.
- Los secretos van en user-secrets (Api: `Authentication:Google:ClientSecret`, `Email:Smtp:Password`) o en variables de entorno, nunca en el repo. Excepciones de desarrollo local: la contraseña de Postgres en `src/ArquitecturaBase.AppHost/appsettings.Development.json` y la clave HMAC de los códigos en `src/ArquitecturaBase.Api/appsettings.Development.json`.

## Tests

- Domain.UnitTests y Application.UnitTests: xUnit v3, sin dependencias externas. ArchitectureTests: capas y convenciones. Api.IntegrationTests: `ApiFactory` (WebApplicationFactory + Testcontainers `postgres:18.3`). Qué verifica cada uno, el arnés y los dobles: [backend.md, "Tests: arquitectura y arnés"](docs/architecture/backend.md#tests-arquitectura-y-arnés).
- Lo que existe solo para probar va en `TestFeatures/` del proyecto de tests, nunca en `src/`. Nombres en inglés, como frase: `Deleted_rows_are_hidden_from_queries_and_endpoints`.

## Front

- El SPA vive en `../ArquitecturaBaseFront` (React + Vite) y se sirve desde un solo origen, sin CORS: [backend.md, "Front y hosting del SPA"](docs/architecture/backend.md#front-y-hosting-del-spa). **Una pantalla nueva se dibuja antes de programarse**, como un tablero del Artifact del sistema visual (`../ArquitecturaBaseFront/docs/design/visual-baseline.md`, “Pantalla nueva: primero el tablero”).
- `BackendPrefixes` es una **lista a mano**: un prefijo de backend nuevo (lo que no empieza con `/api`) sigue [`docs/guides/prefijo-de-backend.md`](docs/guides/prefijo-de-backend.md). Si falta, sus rutas inexistentes devuelven el `index.html` con 200 y el cliente recibe HTML donde esperaba JSON.

## Dónde va cada cosa

| Pieza | Carpeta |
|---|---|
| Controller y contrato de entrada HTTP (`*HttpRequest`, `*Query`) | `src/ArquitecturaBase.Api/Controllers/` y `Api/Contracts/<Área>/` |
| Adaptador de `HttpContext` (`CurrentUser`, `RequestInfo`) | `Api/RequestContext/` |
| Interfaz de servicio | `Application/Interfaces/Services/` |
| Servicio y sus helpers (`*Policy`, `*Guard`, `*Issuer`, `*Verifier`, `*Linker`, `*Revoker`, `*Recorder`) | `Application/Services/<Área>/` |
| Interfaz de repositorio o lector, `IUnitOfWork` | `Application/Interfaces/Persistence/` |
| Puerto del núcleo hacia un módulo opcional | la interfaz en `Application/Interfaces/Channels/`, la versión del núcleo, si la tiene, en `Application/Channels/` (la apagada con `TryAdd`; en un puerto por canal, la suya con `TryAddEnumerable`, como el correo, o ninguna), y el adaptador del módulo en `Application/Modules/<Módulo>/Channels/` ([backend.md, "Módulos opcionales"](docs/architecture/backend.md#módulos-opcionales)) |
| Interfaz de un proveedor externo (Identity, correo, seguridad) | `Application/Interfaces/Integrations/`, en sus subcarpetas `Identity/`, `Security/`, `Emails/`, `Request/`, `Phones/` y `Caching/` (el descarte de un caché, como `ISystemSettingsCache`; el de permisos por rol, `IPermissionService.InvalidateRoleAsync`, queda en `Identity/`) |
| Modelo (`*Request`, `*Response`, `*Row`), validador y configuración funcional | `Application/Models/<Área>/`, `Application/Validation/<Área>/` y `Application/Configuration/` |
| Texto que ve el usuario | `Application/Resources/*.resx` y su `.en.resx` |
| Entidad y `<Entidad>Errors` | `Domain/<Área>/` (`Entity`, `IAuditable` e `ISoftDeletable`, en `Domain/Common/`) |
| Permiso | `Domain/Authorization/Permissions.cs` ([guía](docs/guides/permiso-nuevo.md)) |
| Repositorio o lector con EF | `Infrastructure/Persistence/Repositories/` o `Infrastructure/Persistence/Readers/` |
| Configuración EF y migraciones | `Infrastructure/Persistence/Configurations/` y `Infrastructure/Persistence/Migrations/` ([guía](docs/guides/migracion.md)) |
| Adaptador técnico o worker | `Infrastructure/<Tema>/` (`Emails/`, `Security/`, `Phones/`) |
| Registro en DI | el `DependencyInjection.cs` (o `*Registration.cs`) de la capa dueña; `Program.cs` compone, y cada módulo opcional se registra en su bloque con un `<Módulo><Capa>Registration` por capa. Los repositorios, lectores y seeders, solo en `Infrastructure/Persistence/PersistenceRegistration.cs` (los de un módulo, en su `<Módulo>InfrastructureRegistration`) |
| Módulo opcional (hoy WhatsApp) | `<Proyecto>/Modules/<Módulo>/` en cada proyecto de `src` y de `tests`, con las mismas carpetas que el núcleo adentro; el núcleo no lo nombra ([backend.md, "Módulos opcionales"](docs/architecture/backend.md#módulos-opcionales)) |
| Test unitario | `tests/ArquitecturaBase.{Domain,Application}.UnitTests/`, en la carpeta equivalente al código |
| Test de ruta o de persistencia | `tests/ArquitecturaBase.Api.IntegrationTests/<Área>/` |
| Regla de arquitectura | `tests/ArquitecturaBase.ArchitectureTests/` |
| Decisión de arquitectura | `docs/decisions/NNNN-*.md` |
| Regla de un área del producto | `docs/features/<área>.md`, con un `AGENTS.md` corto en sus carpetas de código y un `CLAUDE.md` que lo importa (`@AGENTS.md`) |
| Receta paso a paso | `docs/guides/` |

## Más documentación

- [`docs/architecture/backend.md`](docs/architecture/backend.md): la arquitectura canónica del backend, con el detalle de las reglas de arriba.
- [`docs/guides/`](docs/guides/): las recetas. [Agregar un área](docs/guides/agregar-un-area.md) (catorce pasos, con el archivo de Roles a copiar en cada uno y la lista de verificación), [permiso nuevo](docs/guides/permiso-nuevo.md), [migración](docs/guides/migracion.md), [prefijo de backend](docs/guides/prefijo-de-backend.md) y [despliegue](docs/guides/despliegue.md) (el orden bundle → imagen, qué hace la Api al arrancar en cada ambiente, qué siembra, la configuración obligatoria en Production y los pendientes).
- [`docs/features/`](docs/features/): las reglas de cada área del producto, [identidad](docs/features/identidad.md), [WhatsApp](docs/features/whatsapp.md) y [administración](docs/features/administracion.md). [`docs/specs/`](docs/specs/): sus diseños funcionales, vigentes en lo funcional; el [inicial](docs/specs/2026-09-18-arquitectura-base-design.md) es histórico para la estructura de capas y el pipeline HTTP.
- [`docs/decisions/`](docs/decisions/README.md): las decisiones de arquitectura (ADR). [`docs/plans/`](docs/plans/): el [plan maestro](docs/plans/2026-09-26-plantilla-estandar-por-etapas.md) y los planes en curso; [`docs/history/`](docs/history/): los cerrados, marcados HISTÓRICO, que no rigen código nuevo.
- [`README.md`](README.md): cómo levantar y probar el proyecto, y el mapa de la documentación. [`CLAUDE.md`](CLAUDE.md): lo propio de Claude Code; importa este archivo.
