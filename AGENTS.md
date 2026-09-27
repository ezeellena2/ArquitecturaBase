# ArquitecturaBase: reglas para agentes

Plantilla base .NET 10 + Aspire 13.5 + PostgreSQL. El front vive en `../ArquitecturaBaseFront`. Este archivo es el índice: tiene las reglas de la plantilla, que valen para cualquier proyecto que salga de ella, y enlaza el resto ([Más documentación](#más-documentación)). La arquitectura canónica del backend está en [`docs/architecture/backend.md`](docs/architecture/backend.md); si una descripción histórica de carpetas o del pipeline de handlers la contradice, prevalece ella. Lo propio de un área del producto (identidad, WhatsApp, administración) está en `docs/features/` y se lee antes de tocar esa área.

## Forma de trabajo

- Se trabaja directo en `main`. No crear ramas ni hacer push sin un pedido explícito.
- Commits chicos, en español, con conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`).
- TDD donde hay lógica. Antes de dar algo por terminado: `dotnet build` sin advertencias y `dotnet test` en verde. Los tests de integración necesitan Docker.

## Comandos

- Compilar: `dotnet build ArquitecturaBase.slnx`
- Todos los tests: `dotnet test` (modo Microsoft Testing Platform, configurado en `global.json`)
- Un proyecto o una clase: `dotnet test --project tests/<Proyecto>/<Proyecto>.csproj -- --filter-class "<Namespace.Clase>"`
- Levantar todo: `aspire run` desde la raíz (Postgres + Api + front). **Apagarlo siempre al terminar de probar: `aspire stop`.** Si queda corriendo, el arranque desde Visual Studio falla con `address already in use` y los DLL quedan bloqueados. El contenedor de Postgres sí sobrevive a propósito (`ContainerLifetime.Persistent`).
- Agregar una migración: el comando está en [backend.md, "Migraciones"](docs/architecture/backend.md#migraciones).

## Capas y dependencias

Las verifica `tests/ArquitecturaBase.ArchitectureTests`.

| Proyecto | Puede referenciar |
|---|---|
| Domain | nada (solo la BCL) |
| Application | Domain y los paquetes de la lista de `ApplicationPackagesTests` (FluentValidation y algunos Microsoft.Extensions.*) |
| Infrastructure | Application, Domain |
| Api | Application, Infrastructure (solo desde `Program.cs`, para registrar dependencias), ServiceDefaults |
| AppHost | Api (como recurso de Aspire) |

- **Domain:** el modelo y las reglas de negocio puras, sin paquetes. Los contratos de repositorios y lectores que consumen los servicios van en `Application/Interfaces/Persistence`, no en `Domain`.
- **Application:** servicios de casos de uso y sus interfaces, modelos, validadores y contratos de persistencia e integración; sin EF Core ni ASP.NET Core. No usa `ApplicationDbContext`, `HttpContext` ni tipos de `Infrastructure` o `Api`. `IQueryable` nunca sale de Infrastructure.
- **Infrastructure:** EF Core con Npgsql, `ApplicationDbContext`, interceptores, repositorios y lectores especializados, Identity, OpenIddict, correo, WhatsApp, adaptadores y workers técnicos. Las consultas EF de negocio quedan detrás de repositorios o lectores; el acceso directo al contexto se permite solo a componentes técnicos, como migraciones, seed, health checks, stores, interceptores y Unit of Work.
- **Api:** controllers MVC, contratos HTTP, adaptación de protocolo y adaptadores de la petición (`Api/RequestContext`; no hay `Api/Services`, para no confundirlo con `Application/Services`). Cada capa registra sus dependencias y `Program.cs` las compone.

## Casos de uso MVC y borde HTTP

- Un request de negocio recorre `Api/Controllers → Application/Interfaces/Services → Application/Services/<Área> → Application/Interfaces/{Persistence,Integrations} → Infrastructure`. Los controllers inyectan interfaces de servicios (nunca EF, repositorios, lectores ni handlers); los servicios coordinan el caso de uso y los repositorios o lectores encapsulan EF. Las lecturas pueden usar lectores especializados y proyecciones eficientes.
- CQRS puede separar lectura y escritura, sin exigir `ICommand`, `IQuery`, handlers ni repositorio genérico; tampoco se crea un repositorio genérico por simetría. Los mapeos se escriben a mano.
- Las rutas HTTP de negocio se implementan con controllers MVC. No agregar Minimal APIs de negocio ni reintroducir `IEndpoint`, `ICommandHandler`, `IQueryHandler`, `Application/Features`, sus decoradores o Scrutor para casos de uso. Los endpoints técnicos de OpenIddict y Aspire conservan su framework.
- La entrada de cada acción (body o query) es un contrato de `Api/Contracts/<Área>` (`*HttpRequest` o `*Query`) mapeado a mano al modelo de Application ([ADR 0002](docs/decisions/0002-contratos-http.md)); si lleva datos personales, códigos o tokens, sobrescribe `ToString()`, porque MVC registra los argumentos de la acción. El `Result` se responde con `ToActionResult`, `ToAcceptedResult` o `ToCreatedResult`. Las respuestas, OpenAPI y los tests que lo verifican están en [backend.md, "Borde HTTP"](docs/architecture/backend.md#borde-http).
- Las rutas piden permisos, nunca roles, con `[HasPermission(Permissions.X)]` y nunca con `[Authorize(Policy = …)]` armado a mano; sin sesión responden 401 y sin el permiso, 403. Un permiso nuevo:
  1. se declara en `Domain/Authorization/Permissions.cs` y en `Permissions.All`, y en `Permissions.resx` y `.en.resx` lleva `Permission.<código>` y `PermissionDescription.<código>` (y `Area.<área>` si el área es nueva); lo verifican `PermissionTextsTests` y `ResourceParityTests`;
  2. el seed se lo da a Admin;
  3. si cambian los permisos de un rol, hay que llamar a `IPermissionService.InvalidateRoleAsync`.
- Cada ruta figura en el inventario (`ExplicitRouteInventoryTests`) y tiene su test. Una ruta nueva no cambia la arquitectura: conservar verbo, autorización, rate limit, cuerpos, status, errores y transacciones que correspondan, y ampliar el inventario y sus pruebas. No mapear una combinación dos veces.
- Las pruebas de arquitectura deben exigir controllers → servicios, límites entre capas y ausencia del pipeline anterior. Cambiar esta estructura requiere una nueva decisión de arquitectura explícita y documentada ([ADR](docs/decisions/README.md)); agregar una funcionalidad o mover un archivo no cambia la regla.

## Una sola forma de guardar

- `IUnitOfWork.ExecuteInTransactionAsync(trabajo, CommitPolicy, ct)` es la única forma de guardar, una vez por cada método público de un servicio que escribe ([ADR 0001](docs/decisions/0001-transaccion-explicita-por-caso-de-uso.md)). La política es `OnSuccess`, u `OnAnyResult` en los casos que tienen que guardar también cuando fallan (intentos, códigos o enlaces consumidos, auditoría). Las consultas no abren límite. FluentValidation y `Result`/`Result<T>` siguen vigentes, y el logging operativo se mantiene sin registrar secretos.
- El patrón en cinco reglas (qué va antes, adentro y después del límite; los helpers no guardan; no se anida), el ejemplo a copiar (`RoleService.UpdateAsync`), las dos excepciones, los locks y lo que verifica `TransactionBoundaryTests` están en [backend.md, "Una sola forma de guardar"](docs/architecture/backend.md#una-sola-forma-de-guardar).

## Result y errores

- Las reglas de negocio devuelven `Result` / `Result<T>` con un `Error`. No lanzan excepciones. Las excepciones quedan para bugs y fallas de infraestructura: las atrapa `GlobalExceptionHandler`, que responde un 500 genérico con `traceId`.
- Los errores se declaran en clases `<Entidad>Errors` (por ejemplo `UserErrors`). Los códigos son estables y siguen el formato `Area.Entidad.Motivo` (por ejemplo `Auth.LoginCode.Expired`). El código es la clave de la traducción en `Errors.resx`.
- Toda respuesta de error es ProblemDetails, con `title` y `detail` traducidos, `code` y `traceId`, y `errors` en las validaciones (campo en camelCase → mensajes). El 400 de un cuerpo ilegible (`Request.Invalid`) no trae `errors` a propósito: el ModelState mostraría la forma interna del modelo.
- Todo middleware que pueda cortar con un error va en `Program.cs` después de `UseStatusCodePages`, o su respuesta sale vacía. Los errores que arma el propio framework (404, 405, 401/403, 429) están en [backend.md, "Errores del framework, pipeline y JSON"](docs/architecture/backend.md#errores-del-framework-pipeline-y-json).

## Persistencia

- Los contratos de repositorios y lectores van en `Application/Interfaces/Persistence`; sus implementaciones EF, en `Infrastructure/Persistence/{Repositories,Readers}`. No hay repositorio genérico ni obligación de crear uno por cada entidad.
- Los métodos de repositorios y lectores se nombran por lo que devuelven: `Get` una entidad seguida (solo en repositorios), `Find` una proyección, `List`, `Exists`, `Count`, `Lock` y los verbos de escritura; un lector solo lee y es el único que usa `AsNoTracking`. La tabla está en [backend.md, "Nombres de repositorios y lectores"](docs/architecture/backend.md#nombres-de-repositorios-y-lectores) ([ADR 0008](docs/decisions/0008-nombres-de-repositorios-y-lectores.md)), y lo verifica `PersistenceNamingTests`.
- Las entidades heredan de `Entity` (Id Guid v7). No hay eventos de dominio: ver [ADR 0003](docs/decisions/0003-sin-eventos-de-dominio.md). Cada entidad tiene su `IEntityTypeConfiguration<T>` en `Infrastructure/Persistence/Configurations/`.
- `IAuditable` e `ISoftDeletable` los completan los interceptores; nunca se setean a mano. `ExecuteUpdate`/`ExecuteDelete` saltean los interceptores: no se usan con entidades `IAuditable` o `ISoftDeletable` (se borraría físicamente y sin auditoría). Las filas borradas se ocultan con un filtro global. Para verlas: `IgnoreQueryFilters()`.
- Paginado: el modelo de pedido de Application usa `PagedRequest` y declara `SortableFields` cuando corresponda; el validador usa `PagedRequestValidator<T>`. Infrastructure ordena con `ApplySort` (un mapa campo → expresión, con los mismos nombres, y un desempate único, normalmente el Id, para que las páginas sean estables) y pagina con `ToPagedResultAsync`.
- Filtros y conteos de un listado (un valor inexistente es una lista vacía, listado y conteos filtran igual, los conteos van aparte): el patrón está en [administracion.md, "Filtros de un listado"](docs/features/administracion.md#filtros-de-un-listado) y ["Conteos por opción de filtro"](docs/features/administracion.md#conteos-por-opción-de-filtro).

## Fechas: siempre en UTC

- Se usa `DateTime` en UTC, obtenido de `TimeProvider` inyectado: `timeProvider.GetUtcNow().UtcDateTime`. Están prohibidos `DateTime.Now`, `DateTime.Today`, `DateTime.UtcNow`, `DateTimeOffset.Now` y `DateTimeOffset.UtcNow`: `BannedSymbols.txt` rompe el build.
- Las propiedades terminan en `Utc` (`CreatedAtUtc`, `ExpiresAtUtc`). Las fechas sin hora usan `DateOnly`. La API responde ISO 8601 con `Z` y rechaza las fechas sin offset (`UtcDateTimeConverter`). En los tests se usa `FakeTimeProvider`.

## Idioma y textos

- Identificadores, mensajes de excepción y logs, en inglés. El idioma de la petición sale de `Accept-Language` (español por defecto, o inglés).
- Todo texto que ve el usuario sale de resources: `Application/Resources/Errors.resx` y `Validation.resx` (español, por defecto) y sus `.en.resx`. Cada clave nueva va en los dos idiomas; `ResourceParityTests` lo verifica. Español rioplatense con voseo ("Ingresá", "Revisá").
- Logs con `[LoggerMessage]` (source generator), nunca `logger.LogX(...)` directo (`CA1848` en `warning` rompe el build). Nunca registrar códigos, tokens, enlaces de ingreso ni secretos. `Microsoft.AspNetCore` queda en `Warning` y las cadenas de conexión no llevan `Include Error Detail`; los motivos, el enmascarado de los números de teléfono y los logs de `HttpClient` del cliente de Meta están en [`docs/features/whatsapp.md`](docs/features/whatsapp.md).

## Build

- `TreatWarningsAsErrors`, analizadores `latest-recommended` y estilo en el build. Las advertencias se corrigen; solo se suprimen en `.editorconfig`, con una justificación. Las versiones de los paquetes van solo en `Directory.Packages.props` (Central Package Management).
- Los secretos van en user-secrets (Api: `Authentication:Google:ClientSecret`, `Email:Smtp:Password`) o en variables de entorno, nunca en el repo. Excepciones de desarrollo local: la contraseña de Postgres en `src/ArquitecturaBase.AppHost/appsettings.Development.json` y la clave HMAC de los códigos en `src/ArquitecturaBase.Api/appsettings.Development.json`.

## Tests

- **Domain.UnitTests y Application.UnitTests:** xUnit v3, sin dependencias externas. **ArchitectureTests:** capas y convenciones. **Api.IntegrationTests:** `ApiFactory` (WebApplicationFactory + Testcontainers `postgres:18.3`).
- Lo que existe solo para probar va en `TestFeatures/` del proyecto de tests, nunca en `src/`. Lo que verifica cada test de arquitectura, el arnés de integración y los dobles de la unidad de trabajo están en [backend.md, "Tests: arquitectura y arnés"](docs/architecture/backend.md#tests-arquitectura-y-arnés).
- Nombres de tests en inglés, como frase: `Deleted_rows_are_hidden_from_queries_and_endpoints`.

## Front

- El SPA vive en `../ArquitecturaBaseFront` (React + Vite) y se sirve desde un solo origen, sin CORS: el detalle está en [backend.md, "Front y hosting del SPA"](docs/architecture/backend.md#front-y-hosting-del-spa). **Una pantalla nueva se dibuja antes de programarse**, como un tablero del Artifact del sistema visual; la regla vive en `../ArquitecturaBaseFront/docs/design/visual-baseline.md`, en “Pantalla nueva: primero el tablero”.
- `BackendPrefixes` es una **lista a mano**: un prefijo de backend nuevo (`/webhooks`, `/metrics`, lo que sea) hay que sumarlo ahí (`Api/Hosting/SpaExtensions.cs`), al `Backend_routes_keep_returning_a_problem` de `SpaHostingTests` y al `server.proxy` de `vite.config.ts`. Si falta, sus rutas inexistentes devuelven el `index.html` con 200 y el cliente recibe HTML donde esperaba JSON.

## Dónde va cada cosa

| Pieza | Carpeta |
|---|---|
| Controller y contrato de entrada HTTP (`*HttpRequest`, `*Query`) | `src/ArquitecturaBase.Api/Controllers/` y `Api/Contracts/<Área>/` |
| Adaptador de `HttpContext` (`CurrentUser`, `RequestInfo`) | `Api/RequestContext/` |
| Interfaz de servicio | `Application/Interfaces/Services/` |
| Servicio y sus helpers (`*Issuer`, `*Verifier`, `*Guards`, `*Policy`) | `Application/Services/<Área>/` |
| Interfaz de repositorio o lector, `IUnitOfWork` | `Application/Interfaces/Persistence/` |
| Interfaz de un proveedor externo (Identity, correo, seguridad, WhatsApp) | `Application/Interfaces/Integrations/` |
| Modelo (`*Request`, `*Response`, `*Row`), validador y configuración funcional | `Application/Models/<Área>/`, `Application/Validation/<Área>/` y `Application/Configuration/` |
| Texto que ve el usuario | `Application/Resources/*.resx` y su `.en.resx` |
| Entidad y `<Entidad>Errors` | `Domain/<Área>/` (`Entity`, `IAuditable` e `ISoftDeletable`, en `Domain/Common/`) |
| Permiso | `Domain/Authorization/Permissions.cs` |
| Repositorio o lector con EF | `Infrastructure/Persistence/Repositories/` o `Infrastructure/Persistence/Readers/` |
| Configuración EF y migraciones | `Infrastructure/Persistence/Configurations/` y `Infrastructure/Persistence/Migrations/` |
| Adaptador técnico o worker | `Infrastructure/<Tema>/` (`Emails/`, `Security/`, `Phones/`) |
| Registro en DI | el `DependencyInjection.cs` (o `*Registration.cs`) de la capa dueña; `Program.cs` compone |
| Test unitario | `tests/ArquitecturaBase.{Domain,Application}.UnitTests/`, en la carpeta equivalente al código |
| Test de ruta o de persistencia | `tests/ArquitecturaBase.Api.IntegrationTests/<Área>/` |
| Regla de arquitectura | `tests/ArquitecturaBase.ArchitectureTests/` |
| Decisión de arquitectura | `docs/decisions/NNNN-*.md` |
| Regla de un área del producto | `docs/features/<área>.md`, con un `AGENTS.md` de una línea en sus carpetas de código y un `CLAUDE.md` que lo importa (`@AGENTS.md`) |

## Más documentación

- [`docs/architecture/backend.md`](docs/architecture/backend.md): la arquitectura canónica del backend, con el detalle de las reglas de arriba.
- [`docs/features/`](docs/features/): las reglas de cada área del producto, [identidad](docs/features/identidad.md), [WhatsApp](docs/features/whatsapp.md) y [administración](docs/features/administracion.md). [`docs/specs/`](docs/specs/): sus diseños funcionales, vigentes en lo funcional; el [inicial](docs/specs/2026-09-18-arquitectura-base-design.md) es histórico para la estructura de capas y el pipeline HTTP.
- [`docs/decisions/`](docs/decisions/README.md): las decisiones de arquitectura (ADR). [`docs/plans/`](docs/plans/): el [plan maestro](docs/plans/2026-09-26-plantilla-estandar-por-etapas.md) y los planes en curso; [`docs/history/`](docs/history/): los cerrados, marcados HISTÓRICO, que no rigen código nuevo.
- [`README.md`](README.md): cómo levantar y probar el proyecto, el túnel para WhatsApp y el despliegue ([Azure](docs/deploy/azure-setup.md), [Postman](docs/postman/README.md)). [`CLAUDE.md`](CLAUDE.md): lo propio de Claude Code; importa este archivo.
