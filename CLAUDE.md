# ArquitecturaBase: guía para agentes

Plantilla base .NET 10 + Aspire 13.5 + PostgreSQL. El front vive en `../ArquitecturaBaseFront`.
El diseño aprobado está en `docs/specs/2026-09-18-arquitectura-base-design.md` y es la fuente de verdad. Los planes por fase están en `docs/plans/`.

## Forma de trabajo

- Se trabaja directo en `main`. No crear ramas ni hacer push sin un pedido explícito.
- Commits chicos, en español, con conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`).
- TDD donde hay lógica. Antes de dar algo por terminado: `dotnet build` sin advertencias y `dotnet test` en verde. Los tests de integración necesitan Docker.

## Comandos

- Compilar: `dotnet build ArquitecturaBase.slnx`
- Todos los tests: `dotnet test` (modo Microsoft Testing Platform, configurado en `global.json`)
- Un proyecto o una clase: `dotnet test --project tests/<Proyecto>/<Proyecto>.csproj -- --filter-class "<Namespace.Clase>"`
- Levantar todo: `aspire run` desde la raíz (Postgres + Api)

## Capas y dependencias

Las verifica `tests/ArquitecturaBase.ArchitectureTests`.

| Proyecto | Puede referenciar |
|---|---|
| Domain | nada (solo la BCL) |
| Application | Domain, más Microsoft.Extensions.*, FluentValidation y Scrutor |
| Infrastructure | Application, Domain |
| Api | Application, Infrastructure (solo desde `Program.cs`, para registrar dependencias), ServiceDefaults |
| AppHost | Api (como recurso de Aspire) |

- **Domain:** reglas de negocio puras, sin paquetes.
- **Application:** casos de uso, sin EF Core ni ASP.NET Core. `IQueryable` nunca sale de Infrastructure.
- **Infrastructure:** EF Core con Npgsql, interceptores, repositorios y servicios externos.
- **Api:** solo presentación. `Program.cs` solo compone.

## Casos de uso

- Cada caso de uso es un comando o una consulta, con su handler y su validador (FluentValidation), en `Application/Features/<Area>/<CasoDeUso>/`.
- Comandos: `ICommand` / `ICommand<T>` con `ICommandHandler<...>`. Consultas: `IQuery<T>` con `IQueryHandler<...>`. Sin MediatR ni AutoMapper; los mapeos se escriben a mano.
- Los handlers se registran solos (Scrutor) y quedan envueltos en este orden: logging → validación → unit of work (solo comandos) → handler.
- Los handlers no llaman a `SaveChanges`: lo hace `UnitOfWorkDecorator` si el resultado fue exitoso.
- Endpoints: una clase `IEndpoint` por grupo en `Api/Endpoints/<Area>/`, que se registra sola. El endpoint recibe el handler por inyección y devuelve `result.ToHttpResult()`.

## Result en lugar de excepciones

- Las reglas de negocio devuelven `Result` / `Result<T>` con un `Error`. No lanzan excepciones.
- Las excepciones quedan para bugs y fallas de infraestructura. Las atrapa `GlobalExceptionHandler`, que responde un 500 genérico con `traceId`.
- Los errores se declaran en clases `<Entidad>Errors` (por ejemplo `UserErrors`).
- Los códigos son estables y siguen el formato `Area.Entidad.Motivo` (por ejemplo `Auth.LoginCode.Expired`). El código es la clave de la traducción en `Errors.resx`.
- Toda respuesta de error es ProblemDetails, con:
  - `title` y `detail` traducidos;
  - `code` y `traceId`;
  - `errors` en las validaciones (campo en camelCase → mensajes).
- Los errores que arma el propio framework (ruta inexistente, 405, 401/403 de la autorización, 429) también salen como ProblemDetails (`ProblemDetailsMapper.CompleteFrameworkProblem` + `UseStatusCodePages`). Sus códigos están en `ApiErrorCodes`: `Http.*` por status, `General.Unexpected` para los 5xx y `Request.Invalid` para el resto de los 4xx.
- Todo middleware que pueda cortar con un error va en `Program.cs` después de `UseStatusCodePages`, o su respuesta sale vacía: `UseAuthentication`/`UseAuthorization` se declaran explícitos (no hay que dejar que `WebApplication` los agregue solo) y en la Fase 2 `UseRateLimiter` va en el mismo lugar.

## Persistencia

- Un repositorio por agregado: la interfaz en Domain y la implementación en Infrastructure. No hay repositorio genérico.
- Las entidades heredan de `Entity` (Id Guid v7) o `AggregateRoot` (acumula eventos de dominio).
- Cada entidad tiene su `IEntityTypeConfiguration<T>` en `Infrastructure/Persistence/Configurations/`.
- `IAuditable` e `ISoftDeletable` los completan los interceptores; nunca se setean a mano.
- `ExecuteUpdate`/`ExecuteDelete` saltean los interceptores: no se usan con entidades `IAuditable` o `ISoftDeletable` (se borraría físicamente y sin auditoría).
- Las filas borradas se ocultan con un filtro global. Para verlas: `IgnoreQueryFilters()`.
- Paginado:
  - la consulta hereda de `PagedRequest` y declara `SortableFields`;
  - su validador hereda de `PagedRequestValidator<T>`;
  - Infrastructure ordena con `ApplySort` (un mapa campo → expresión, con los mismos nombres, y un desempate único, normalmente el Id, para que las páginas sean estables) y pagina con `ToPagedResultAsync`.
- Migraciones (desde la Fase 2): la Api necesita `Microsoft.EntityFrameworkCore.Design` (`PackageReference` con `PrivateAssets="all"`, versión en `Directory.Packages.props`). El comando pasa la cadena de conexión como argumento de la aplicación, porque la Api solo la recibe de Aspire:
  ```
  dotnet ef migrations add <Nombre> --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api -- --ConnectionStrings:appdb "Host=localhost;Port=5433;Database=appdb;Username=postgres;Password=postgres"
  ```
  En desarrollo, la Api las aplica al iniciar.

## Fechas: siempre en UTC

- Se usa `DateTime` en UTC, obtenido de `TimeProvider` inyectado: `timeProvider.GetUtcNow().UtcDateTime`.
- Están prohibidos `DateTime.Now`, `DateTime.Today`, `DateTime.UtcNow`, `DateTimeOffset.Now` y `DateTimeOffset.UtcNow`: `BannedSymbols.txt` rompe el build.
- Las propiedades terminan en `Utc` (`CreatedAtUtc`, `ExpiresAtUtc`). Las fechas sin hora usan `DateOnly`.
- La API responde ISO 8601 con `Z` y rechaza las fechas sin offset (`UtcDateTimeConverter`).
- En los tests se usa `FakeTimeProvider`.

## Idioma y textos

- Identificadores, mensajes de excepción y logs, en inglés.
- Todo texto que ve el usuario sale de resources: `Application/Resources/Errors.resx` y `Validation.resx` (español, por defecto) y sus `.en.resx`.
- Cada clave nueva va en los dos idiomas; `ResourceParityTests` lo verifica.
- Español rioplatense con voseo ("Ingresá", "Revisá").
- El idioma de la petición sale de `Accept-Language` (español por defecto, o inglés).
- Logs con `[LoggerMessage]` (source generator), nunca `logger.LogX(...)` directo. Nunca registrar códigos, tokens ni secretos.

## Build

- `TreatWarningsAsErrors`, analizadores `latest-recommended` y estilo en el build. Las advertencias se corrigen; solo se suprimen en `.editorconfig`, con una justificación.
- Las versiones de los paquetes van solo en `Directory.Packages.props` (Central Package Management).
- Los secretos van en user-secrets o en variables de entorno, nunca en el repo. Excepción temporal: la contraseña local de Postgres está en `src/ArquitecturaBase.AppHost/appsettings.Development.json` y se va a mover a user-secrets.

## Tests

- **Domain.UnitTests y Application.UnitTests:** xUnit v3, sin dependencias externas.
- **ArchitectureTests:** reglas de capas (NetArchTest y las referencias de cada `.csproj`).
- **Api.IntegrationTests:** `ApiFactory` (WebApplicationFactory + Testcontainers `postgres:18.3`).
  - Reutiliza la registración del DbContext de producción: solo cambia el tipo de contexto (`TestDbContext`) y la cadena de conexión. No volver a registrar el DbContext en el arnés.
  - Lo que existe solo para probar (entidades, endpoints `/test`, handlers) va en `TestFeatures/` del proyecto de tests, nunca en `src/`.
- Nombres de tests en inglés, como frase: `Deleted_rows_are_hidden_from_queries_and_endpoints`.
