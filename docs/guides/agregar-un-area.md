# Agregar un área

La receta para sumar un área de negocio nueva (una entidad con su CRUD, sus permisos y sus rutas) sin preguntar nada: catorce pasos en orden, y en cada uno qué crear, dónde, qué archivo real copiar, la trampa típica y el test que lo verifica. Al final hay una [lista de verificación](#lista-de-verificación) y lo que [solo se ve con Docker](#qué-solo-se-ve-con-docker).

El área de referencia es **Roles** ([ADR 0004](../decisions/0004-roles-como-area-de-referencia.md)). Roles viene de Identity, así que para lo que Identity le resuelve (la entidad, el repositorio) la guía enlaza otro ejemplo: `SystemSettings` para una entidad y un repositorio propios, y `Widget` (solo de tests) para el borrado lógico. Las reglas que esta guía aplica están en [`AGENTS.md`](../../AGENTS.md) y en [`docs/architecture/backend.md`](../architecture/backend.md); ante una diferencia, mandan ellas.

En los nombres, `<Entidad>` es el singular en PascalCase (`Product`), `<Área>` el plural (`Products`), `<recurso>` la ruta en minúsculas (`products`) y `<área>` el prefijo del permiso (`products`). Las rutas de archivo son desde la raíz del repo.

## Antes de empezar

- Leé [`AGENTS.md`](../../AGENTS.md) entero. De [`backend.md`](../architecture/backend.md), por lo menos: [Borde HTTP](../architecture/backend.md#borde-http), [Una sola forma de guardar](../architecture/backend.md#una-sola-forma-de-guardar), [Nombres de repositorios y lectores](../architecture/backend.md#nombres-de-repositorios-y-lectores) y [Modelos: Response y Row](../architecture/backend.md#modelos-response-y-row).
- Se trabaja en `main`, sin ramas. TDD donde hay lógica: el test del servicio va en rojo antes que el servicio.
- **El listado de un área nueva pagina directamente en `GET /api/<recurso>`.** Roles tiene su listado paginado en `GET /api/roles/paged` solo porque su `GET /api/roles` ya era el catálogo completo que usan los selectores del front y no se podía cambiar ([ADR 0004](../decisions/0004-roles-como-area-de-referencia.md), enmienda). No copies `/paged`.
- Cómo verificar mientras avanzás, sin Docker:

  ```
  dotnet build ArquitecturaBase.slnx
  dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj
  dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj
  dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj
  ```

  Para una sola clase: `dotnet test --project <proyecto>.csproj -- --filter-class "<Namespace.Clase>"`. Si la solución no compila solo por el AppHost (un entorno sin Aspire), compilá `src/ArquitecturaBase.Api` y los cuatro proyectos de `tests/`: juntos cubren el resto. Los tests de integración (`tests/ArquitecturaBase.Api.IntegrationTests`) necesitan Docker; sin él, que por lo menos compilen.

## Los catorce pasos

| Paso | Pieza | Dónde | Ejemplo a copiar |
|---|---|---|---|
| [1](#1-entidad) | entidad | `src/ArquitecturaBase.Domain/<Área>/` | `SystemSettings`, `Widget` |
| [2](#2-configuración-ef) | `IEntityTypeConfiguration<T>` | `Infrastructure/Persistence/Configurations/` | `SystemSettingsConfiguration` |
| [3](#3-migración) | migración | `Infrastructure/Persistence/Migrations/` | el comando de `backend.md` |
| [4](#4-errores-y-sus-textos) | `<Entidad>Errors` y claves en los dos `.resx` | `Domain/<Área>/`, `Application/Resources/` | `RoleErrors` |
| [5](#5-permisos) | permisos | `Domain/Authorization/Permissions.cs` | `Permissions.Roles` |
| [6](#6-contratos-de-repositorio-y-lector) | interfaces de repositorio y lector | `Application/Interfaces/Persistence/` | `IRoleReader`, `ISystemSettingsRepository` |
| [7](#7-repositorio-y-lector-con-ef) | sus implementaciones y su registro | `Infrastructure/Persistence/{Repositories,Readers}/` | `RoleReader`, `SystemSettingsRepository` |
| [8](#8-modelos-y-validadores) | modelos y validadores | `Application/Models/<Área>/`, `Application/Validation/<Área>/` | `Models/Roles`, `Validation/Roles` |
| [9](#9-interfaz-de-servicio-y-servicio) | interfaz de servicio y servicio | `Application/Interfaces/Services/`, `Application/Services/<Área>/` | `IRoleService`, `RoleService` |
| [10](#10-registro-del-servicio) | registro del servicio | `Application/DependencyInjection.cs` | la línea de `IRoleService` |
| [11](#11-contratos-http-y-controller) | contratos y controller | `Api/Contracts/<Área>/`, `Api/Controllers/` | `RolesController` |
| [12](#12-tests) | tests | los cuatro proyectos de `tests/` | los de Roles |
| [13](#13-inventario-de-rutas) | inventario de rutas | `ExplicitRouteInventoryTests` | — |
| [14](#14-prefijo-de-backend-solo-si-la-ruta-no-empieza-con-api) | prefijo de backend, solo si la ruta no empieza con `/api` | cuatro lugares | — |

### 1. Entidad

- **Qué:** `public sealed class <Entidad> : Entity, IAuditable`, y además `ISoftDeletable` si un borrado tiene que dejar la fila. Va en `src/ArquitecturaBase.Domain/<Área>/<Entidad>.cs`.
- **Copiá:**
  - [`SystemSettings.cs`](../../src/ArquitecturaBase.Domain/Settings/SystemSettings.cs): `Entity` + `IAuditable`, constructor privado para EF, un `Create` estático y un método por cada cambio (`SetRegistrationMode`). Su constructor pasa un Id fijo (`base(SingletonId)`) porque es una tabla de una sola fila: una entidad normal usa el constructor sin argumentos de [`Entity`](../../src/ArquitecturaBase.Domain/Common/Entity.cs), que genera un Guid v7.
  - Para `ISoftDeletable` no hay entidad de Domain en `src`: copiá las propiedades de [`Widget.cs`](../../tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Widget.cs) (entidad de prueba, `Entity` + `IAuditable` + `ISoftDeletable`), que también muestra el largo máximo como constante de la entidad (`NameMaxLength`).
  - Roles no sirve acá: [`ApplicationRole`](../../src/ArquitecturaBase.Infrastructure/Identity/ApplicationRole.cs) es un rol de Identity y vive en Infrastructure.
- **Trampas:**
  - Las propiedades de [`IAuditable`](../../src/ArquitecturaBase.Domain/Common/IAuditable.cs) e [`ISoftDeletable`](../../src/ArquitecturaBase.Domain/Common/ISoftDeletable.cs) son públicas con `private set` (los interceptores las buscan por nombre) y **nunca se asignan a mano**: las completan `AuditableEntityInterceptor` y `SoftDeleteInterceptor` al guardar.
  - Las fechas son `DateTime` en UTC y terminan en `Utc`. No hay eventos de dominio ([ADR 0003](../decisions/0003-sin-eventos-de-dominio.md)).
  - Los largos máximos van como constantes de la entidad (`public const int NameMaxLength = 50;`): Application ve Domain, así que el validador del paso 8 y la configuración del paso 2 usan la misma. Roles los repite en `ValidationRules` solo porque Application no ve `ApplicationRole`.
- **Lo verifica:** [`EntityConfigurationTests`](../../tests/ArquitecturaBase.ArchitectureTests/EntityConfigurationTests.cs) falla hasta que exista la configuración del paso 2. Si la entidad tiene reglas propias, sus tests van en `tests/ArquitecturaBase.Domain.UnitTests/<Área>/`, como [`SystemSettingsTests`](../../tests/ArquitecturaBase.Domain.UnitTests/Settings/SystemSettingsTests.cs).

### 2. Configuración EF

- **Qué:** `internal sealed class <Entidad>Configuration : IEntityTypeConfiguration<<Entidad>>` en `src/ArquitecturaBase.Infrastructure/Persistence/Configurations/`, con los largos (`HasMaxLength`), los índices y las conversiones.
- **Copiá:** [`SystemSettingsConfiguration.cs`](../../src/ArquitecturaBase.Infrastructure/Persistence/Configurations/SystemSettingsConfiguration.cs); al lado, [`ApplicationRoleConfiguration.cs`](../../src/ArquitecturaBase.Infrastructure/Persistence/Configurations/ApplicationRoleConfiguration.cs) muestra lo mínimo (un `HasMaxLength` con la constante de la entidad).
- **No hace falta registrarla:** `ApplicationDbContext.OnModelCreating` llama a `ApplyConfigurationsFromAssembly` ([`ApplicationDbContext.cs`](../../src/ArquitecturaBase.Infrastructure/Persistence/ApplicationDbContext.cs)) y la levanta sola. El filtro global que oculta las filas borradas también se aplica solo a toda entidad `ISoftDeletable` (`ApplySoftDeleteQueryFilter`). El `DbSet` en el contexto es opcional: el repositorio y el lector pueden usar `dbContext.Set<<Entidad>>()`.
- **Trampa:** con `ISoftDeletable` y un nombre único, el índice único tiene que ser **filtrado**, o un registro borrado bloquea su nombre para siempre (la fila sigue en la tabla):

  ```csharp
  builder.HasIndex(entity => entity.Name).IsUnique().HasFilter("\"IsDeleted\" = false");
  ```

  Las columnas van entre comillas dobles, como en el `HasFilter` de [`WhatsAppMessageConfiguration.cs`](../../src/ArquitecturaBase.Infrastructure/Persistence/Configurations/WhatsAppMessageConfiguration.cs). Sin borrado lógico, el índice va sin filtro.
- **Lo verifica:** `EntityConfigurationTests` (arquitectura, sin Docker).

### 3. Migración

- **Instalar `dotnet ef`:** no viene con el repo (no hay manifiesto `.config/dotnet-tools.json`). Se instala como herramienta global con la misma versión que `Microsoft.EntityFrameworkCore.Design` en [`Directory.Packages.props`](../../Directory.Packages.props) (hoy, 10.0.12):

  ```
  dotnet tool install --global dotnet-ef --version 10.0.12
  export PATH="$PATH:$HOME/.dotnet/tools"
  dotnet ef --version
  ```

  En Linux y macOS, `~/.dotnet/tools` tiene que estar en el `PATH` (el `export` vale para esa terminal); en Windows el instalador ya lo suma. Si la versión del paquete cambia, actualizá la herramienta con `dotnet tool update --global dotnet-ef --version <la nueva>`.
- **Qué:** el comando de [backend.md, "Migraciones"](../architecture/backend.md#migraciones), desde la raíz del repo, con un nombre que diga qué cambia (`Add<Área>`):

  ```
  dotnet ef migrations add <Nombre> --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api --output-dir Persistence/Migrations -- --environment Development --ConnectionStrings:appdb "Host=localhost;Port=5433;Database=appdb;Username=postgres;Password=postgres"
  ```

  `migrations add` **no necesita Postgres**: la cadena de conexión solo tiene que existir para que la Api arranque su configuración. Después, `dotnet ef migrations has-pending-model-changes` con los mismos argumentos tiene que responder "No changes have been made to the model since the last migration.", también sin base.
- **Trampas:**
  - Los argumentos después de `--` son de la aplicación, no de `dotnet ef`: sin ellos la Api no encuentra `ConnectionStrings:appdb` y el comando falla.
  - Revisá la migración generada: el índice único tiene que tener su `filter` si la entidad es `ISoftDeletable`.
  - No corras `dotnet ef database update`: en Development la Api aplica las migraciones al arrancar. Las migraciones son código generado y `.editorconfig` las excluye del estilo; no las edites a mano salvo para corregir lo generado.
- **Lo verifica:** [`MigrationsTests`](../../tests/ArquitecturaBase.Api.IntegrationTests/Persistence/MigrationsTests.cs) (`Model_has_no_pending_changes` y `Migrations_create_the_schema_on_an_empty_database`), **solo con Docker**. Sin Docker, `has-pending-model-changes` es la verificación.

### 4. Errores y sus textos

- **Qué:** `public static class <Entidad>Errors` en `src/ArquitecturaBase.Domain/<Área>/`. Cada error es un par: una constante `public const string <Motivo>Code = "<Área>.<Entidad>.<Motivo>";` y un `public static readonly Error <Motivo> = Error.NotFound(<Motivo>Code, "<descripción técnica en inglés>");`. El código es la clave del texto en [`Errors.resx`](../../src/ArquitecturaBase.Application/Resources/Errors.resx) (español rioplatense, con voseo) y en [`Errors.en.resx`](../../src/ArquitecturaBase.Application/Resources/Errors.en.resx).
- **Copiá:** [`RoleErrors.cs`](../../src/ArquitecturaBase.Domain/Authorization/RoleErrors.cs) y sus claves `Roles.Role.*` en los dos `.resx`.
- **Trampas:**
  - Sin la constante terminada en `Code`, el test de traducciones no ve el error y el usuario recibe la descripción técnica en inglés.
  - El tipo del `Error` decide el status HTTP (`ProblemDetailsMapper.ToStatusCode`): `NotFound` 404, `Conflict` 409, `Forbidden` 403, `Validation` 400. Un `Conflict` pide además `[ProducesProblem(StatusCodes.Status409Conflict)]` en la acción (paso 11).
  - Un mensaje de validación nuevo (no de negocio) va en `Validation.resx` y `Validation.en.resx`; los que ya existen (`Required`, `MaxLength`) se reutilizan.
- **Lo verifica:** [`ErrorCodeTranslationTests`](../../tests/ArquitecturaBase.Application.UnitTests/Resources/ErrorCodeTranslationTests.cs) (cada `*Code` con texto en los dos idiomas) y [`ResourceParityTests`](../../tests/ArquitecturaBase.Application.UnitTests/Resources/ResourceParityTests.cs) en Application; [`ErrorCodeTests`](../../tests/ArquitecturaBase.ArchitectureTests/ErrorCodeTests.cs) (el formato `Area.Entidad.Motivo`) en arquitectura.

### 5. Permisos

- **Qué:** los tres pasos de [`AGENTS.md`](../../AGENTS.md#casos-de-uso-mvc-y-borde-http):
  1. en [`Permissions.cs`](../../src/ArquitecturaBase.Domain/Authorization/Permissions.cs), una clase `public static class <Área>` con `Read = "<área>.read"` y `Manage = "<área>.manage"` (copiá `Permissions.Roles`), y los dos en `Permissions.All`. El orden de `All` es el del catálogo que ve el front;
  2. en [`Permissions.resx`](../../src/ArquitecturaBase.Application/Resources/Permissions.resx) y `Permissions.en.resx`: `Area.<área>`, `Permission.<área>.read`, `Permission.<área>.manage`, `PermissionDescription.<área>.read` y `PermissionDescription.<área>.manage`;
  3. el seed: no hay que tocarlo. [`RoleSeeder`](../../src/ArquitecturaBase.Infrastructure/Persistence/Seed/RoleSeeder.cs) le da `Permissions.All` a Admin en cada arranque y suma los que falten. `IPermissionService.InvalidateRoleAsync` se llama solo desde un caso de uso que cambia los permisos de un rol (como `RoleService.UpdateAsync`); sumar un permiso no lo pide.
- **Trampas:**
  - El código es `^[a-z]+\.[a-z]+$`: nada de mayúsculas, guiones ni números (`product-lines.read` no pasa).
  - **Listas fijas que hay que tocar** con un área o un permiso nuevo, porque fijan el catálogo a mano:
    - [`PermissionsTests.All_lists_every_permission_once`](../../tests/ArquitecturaBase.Domain.UnitTests/Authorization/PermissionsTests.cs) (Domain): la lista de todos los permisos, en orden;
    - [`RoleServiceTests.Get_permissions_keeps_catalog_order_and_request_culture`](../../tests/ArquitecturaBase.Application.UnitTests/Services/Roles/RoleServiceTests.cs) (Application): las áreas, en orden;
    - [`RolesEndpointsTests`](../../tests/ArquitecturaBase.Api.IntegrationTests/Roles/RolesEndpointsTests.cs) (integración): `The_permission_catalog_is_grouped_by_area_and_translated` (las áreas y sus nombres en español) y `The_permission_catalog_is_also_in_english` (los nombres en inglés);
    - [`docs/features/administracion.md`](../features/administracion.md), "Reglas": la frase "El catálogo queda en …".
- **Lo verifica:** `PermissionsTests` (Domain), [`PermissionTextsTests`](../../tests/ArquitecturaBase.Application.UnitTests/Resources/PermissionTextsTests.cs) y `ResourceParityTests` (Application). Los de `RolesEndpointsTests`, solo con Docker.

### 6. Contratos de repositorio y lector

- **Qué:** en `src/ArquitecturaBase.Application/Interfaces/Persistence/`, un `I<Entidad>Reader` para las consultas y un `I<Entidad>Repository` para las escrituras. Los métodos se nombran por lo que devuelven ([tabla de backend.md](../architecture/backend.md#nombres-de-repositorios-y-lectores), [ADR 0008](../decisions/0008-nombres-de-repositorios-y-lectores.md)).
- **Copiá:**
  - el lector, de [`IRoleReader.cs`](../../src/ArquitecturaBase.Application/Interfaces/Persistence/IRoleReader.cs): `Task<PagedResult<<Entidad>Row>> List<Área>Async(List<Área>Request, CancellationToken)`, `Task<<Entidad>Row?> FindByIdAsync(Guid, CancellationToken)` y, si el nombre es único, `Task<bool> ExistsByNameAsync(string name, Guid? excludedId, CancellationToken)` (el id a excluir es el propio, al editar). `ListAllRolesAsync` es el catálogo de Roles: un área nueva no lo necesita salvo que tenga selectores;
  - el repositorio, de [`ISystemSettingsRepository.cs`](../../src/ArquitecturaBase.Application/Interfaces/Persistence/ISystemSettingsRepository.cs): `Task<<Entidad>?> GetByIdAsync(Guid, CancellationToken)` (la entidad **seguida**, para modificarla), `void Add(<Entidad>)` y, para borrar, `void Remove(<Entidad>)`.
  - **[`IRoleRepository`](../../src/ArquitecturaBase.Application/Interfaces/Persistence/IRoleRepository.cs) no sirve de ejemplo** para una entidad propia: envuelve `RoleManager` de Identity con escrituras escalares (`CreateAsync(name, description, …)`).
- **Trampas:** `Get…` solo en un repositorio y devolviendo una entidad; `Find…` nunca devuelve una entidad; un lector solo tiene `Find`, `List`, `Exists` y `Count`. Ninguna firma expone `IQueryable` ni `Expression`.
- **Lo verifica:** [`PersistenceNamingTests`](../../tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs) y `ApplicationPublicApiTests` (arquitectura).

### 7. Repositorio y lector con EF

- **Qué:** `internal sealed class <Entidad>Reader(ApplicationDbContext dbContext) : I<Entidad>Reader` en `src/ArquitecturaBase.Infrastructure/Persistence/Readers/`, y `internal sealed class <Entidad>Repository(ApplicationDbContext dbContext) : I<Entidad>Repository` en `.../Repositories/`. Se registran a mano en [`Infrastructure/DependencyInjection.cs`](../../src/ArquitecturaBase.Infrastructure/DependencyInjection.cs), junto a `services.AddScoped<IRoleReader, RoleReader>();` y `services.AddScoped<ISystemSettingsRepository, SystemSettingsRepository>();`.
- **El lector, copiado de [`RoleReader.cs`](../../src/ArquitecturaBase.Infrastructure/Persistence/Readers/RoleReader.cs)** (catálogo, detalle y página con una sola proyección):
  - un `SortMap` (`Dictionary<string, Expression<Func<<Entidad>, object?>>>`) con **los mismos nombres** que `SortableFields` del pedido (paso 8), y un `DefaultSort`;
  - `ApplySort(SortDescriptor.Parse(request.Sort), SortMap, DefaultSort, entity => entity.Id)` **antes** del `Select`: el último argumento es el desempate único, sin él las páginas repiten o pierden filas. Después, `ToPagedResultAsync(request, ct)`. Los dos están en [`QueryableExtensions.cs`](../../src/ArquitecturaBase.Infrastructure/Persistence/Extensions/QueryableExtensions.cs);
  - la búsqueda, con [`LikePatterns`](../../src/ArquitecturaBase.Infrastructure/Persistence/Extensions/LikePatterns.cs): `var pattern = LikePatterns.Contains(request.Search.Trim());` y `EF.Functions.ILike(columna, pattern, LikePatterns.EscapeCharacter)`. **Siempre** con `EscapeCharacter`, o `%` y `_` del texto vuelven a ser comodines. Una columna `string?` necesita su guarda (`entity.Description != null && …`), o el build se rompe por nulabilidad. Roles busca en el nombre y la descripción; si tu área no lo necesita, alcanza con el nombre;
  - `AsNoTracking()` en las consultas: solo un lector lo usa;
  - el detalle (`FindByIdAsync`) y la página devuelven el mismo `<Entidad>Row`.
- **El repositorio, copiado de [`SystemSettingsRepository.cs`](../../src/ArquitecturaBase.Infrastructure/Persistence/Repositories/SystemSettingsRepository.cs)** (Roles no sirve: `RoleRepository` usa `RoleManager`):
  - `GetByIdAsync`: `dbContext.Set<<Entidad>>().FirstOrDefaultAsync(entity => entity.Id == id, ct)`, **sin** `AsNoTracking`, para que el guardado final del límite baje los cambios;
  - `Add`: `dbContext.Set<<Entidad>>().Add(entity)`;
  - `Remove`: `dbContext.Set<<Entidad>>().Remove(entity)`. Con una `ISoftDeletable`, [`SoftDeleteInterceptor`](../../src/ArquitecturaBase.Infrastructure/Persistence/Interceptors/SoftDeleteInterceptor.cs) lo convierte en la marca (`IsDeleted`, `DeletedAtUtc`, `DeletedBy`) al guardar, y el filtro global la oculta desde ahí.
- **Trampas:**
  - **Prohibido `ExecuteDelete` y `ExecuteUpdate`:** saltean los interceptores, así que borrarían físicamente y sin auditoría.
  - Ni el repositorio ni el lector llaman a `SaveChanges`: guarda solo `UnitOfWork`, al final del límite del servicio.
  - Si falta el registro, nada lo avisa sin Docker: la Api falla recién al resolver el servicio.
- **Lo verifica:** `PersistenceNamingTests.Only_readers_skip_tracking` y [`TransactionBoundaryTests`](../../tests/ArquitecturaBase.ArchitectureTests/TransactionBoundaryTests.cs) (`Bulk_updates_and_deletes_only_where_documented`, `Only_the_unit_of_work_saves_the_context`), sin Docker. El comportamiento, con un test de integración como [`RoleReaderTests`](../../tests/ArquitecturaBase.Api.IntegrationTests/Persistence/RoleReaderTests.cs) (paso 12).

### 8. Modelos y validadores

- **Qué**, en `src/ArquitecturaBase.Application/Models/<Área>/` (records `sealed`, sin subcarpetas):
  - `List<Área>Request : PagedRequest`, con `public static readonly IReadOnlyCollection<string> SortableFields = [...]` (en camelCase, los mismos nombres del `SortMap`): copiá [`ListRolesRequest.cs`](../../src/ArquitecturaBase.Application/Models/Roles/ListRolesRequest.cs);
  - `Create<Entidad>Request` y `Update<Entidad>Request` (el id va como propiedad, llega por la ruta), con los textos como `string?` para que un campo faltante sea un 400 con su error y no un fallo de binding: copiá [`CreateRoleRequest.cs`](../../src/ArquitecturaBase.Application/Models/Roles/CreateRoleRequest.cs) y [`UpdateRoleRequest.cs`](../../src/ArquitecturaBase.Application/Models/Roles/UpdateRoleRequest.cs);
  - `<Entidad>Row`, lo que proyecta el lector, y `<Entidad>Response`, lo que devuelve el servicio y sale por HTTP: copiá [`RoleRow.cs`](../../src/ArquitecturaBase.Application/Models/Roles/RoleRow.cs) y [`RoleResponse.cs`](../../src/ArquitecturaBase.Application/Models/Roles/RoleResponse.cs).
- **Validadores**, en `src/ArquitecturaBase.Application/Validation/<Área>/`, `internal sealed`:
  - el del listado, en una línea: `internal sealed class List<Área>RequestValidator() : PagedRequestValidator<List<Área>Request>(List<Área>Request.SortableFields);`, como [`ListRolesRequestValidator.cs`](../../src/ArquitecturaBase.Application/Validation/Roles/ListRolesRequestValidator.cs);
  - los del alta y la edición, como [`CreateRoleRequestValidator.cs`](../../src/ArquitecturaBase.Application/Validation/Roles/CreateRoleRequestValidator.cs), con `.Required()` y `.MaxLength(<Entidad>.NameMaxLength)` de [`ValidationRules`](../../src/ArquitecturaBase.Application/Common/Validation/ValidationRules.cs).
  - Se registran solos (`AddApplicationValidatorsFromAssembly`, con los tipos internos).
- **Trampas:** el validador se resuelve por el tipo **estático** del pedido: pasado como un tipo base u `object`, no corre ninguno. El nombre único **no** se valida acá: es una regla que lee la base y va adentro del límite (paso 9).
- **Lo verifica:** `DependencyInjectionTests.Every_application_validator_is_registered_and_resolves_in_a_scope` (Application), y los tests del servicio del paso 12.

### 9. Interfaz de servicio y servicio

- **Qué:** `I<Entidad>Service` en `src/ArquitecturaBase.Application/Interfaces/Services/` y `internal sealed class <Entidad>Service(...) : I<Entidad>Service` en `src/ArquitecturaBase.Application/Services/<Área>/`. Métodos: `List<Área>Async`, `Get<Entidad>Async`, `CreateAsync` (devuelve `Result<Guid>`), `UpdateAsync` y `DeleteAsync` (devuelven `Result`).
- **Copiá:** [`IRoleService.cs`](../../src/ArquitecturaBase.Application/Interfaces/Services/IRoleService.cs) y [`RoleService.cs`](../../src/ArquitecturaBase.Application/Services/Roles/RoleService.cs):
  - cada método público envuelve su cuerpo entero en `OperationLog.RunAsync(logger, "<Operación>", …)` ([`OperationLog.cs`](../../src/ArquitecturaBase.Application/Common/Logging/OperationLog.cs));
  - las consultas (`ListRolesAsync`, `GetRoleAsync`) validan afuera y **no abren límite**; un id inexistente devuelve `<Entidad>Errors.NotFound`;
  - las escrituras siguen el patrón de `UpdateAsync` con su `UpdateCoreAsync` ([las cinco reglas](../architecture/backend.md#una-sola-forma-de-guardar)): `ArgumentNullException.ThrowIfNull` y el validador afuera; **un solo** `unitOfWork.ExecuteInTransactionAsync(ct => …CoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken)`; adentro, las lecturas, las reglas (el `ExistsByNameAsync` que da el 409) y las escrituras;
  - con una entidad propia, las escrituras del `…CoreAsync` son de entidad y no de `RoleManager`: el alta hace `repository.Add(<Entidad>.Create(…))` y devuelve el `Id`; la edición trae la entidad seguida con `repository.GetByIdAsync` y llama a su método, como `SystemSettingsService.UpdateCoreAsync` ([`SystemSettingsService.cs`](../../src/ArquitecturaBase.Application/Services/Settings/SystemSettingsService.cs)); el borrado hace `GetByIdAsync` y `repository.Remove`. El guardado final del límite baja todo;
  - un `private static <Entidad>Response ToResponse(<Entidad>Row row)` compartido por el detalle y la página, como en `RoleService`.
- **Trampas:** nunca `SaveChanges` ni un segundo límite en el mismo método; nunca un try/catch alrededor del límite; un servicio no llama a la escritura de otro. El constructor tiene como máximo 8 dependencias, contando `ILogger`. La salida de cada método termina en `Response` (salvo escalares como el `Guid` del alta).
- **Lo verifica** (arquitectura, sin Docker): [`ApplicationServicesTests`](../../tests/ArquitecturaBase.ArchitectureTests/ApplicationServicesTests.cs), [`OperationLoggingTests`](../../tests/ArquitecturaBase.ArchitectureTests/OperationLoggingTests.cs), `TransactionBoundaryTests`, [`ServiceDependencyLimitTests`](../../tests/ArquitecturaBase.ArchitectureTests/ServiceDependencyLimitTests.cs) y [`ServiceOutputNamingTests`](../../tests/ArquitecturaBase.ArchitectureTests/ServiceOutputNamingTests.cs).

### 10. Registro del servicio

- **Qué:** `services.AddScoped<I<Entidad>Service, <Entidad>Service>();` en [`Application/DependencyInjection.cs`](../../src/ArquitecturaBase.Application/DependencyInjection.cs), junto a `services.AddScoped<IRoleService, RoleService>();`. Es a mano, para que la composición quede visible. Los validadores no se registran: se escanean solos.
- **Trampa:** un helper del área (`*Policy`, `*Guard`, …) se registra por su tipo concreto (`services.AddScoped<UserGuard>()`), nunca por interfaz, y lleva uno de los [sufijos de la tabla](../architecture/backend.md#convención-de-sufijos-de-los-helpers). Un CRUD simple no necesita ninguno.
- **Lo verifica:** [`DependencyInjectionTests.Every_application_dependency_is_registered`](../../tests/ArquitecturaBase.Application.UnitTests/DependencyInjectionTests.cs) (Application, sin Docker).

### 11. Contratos HTTP y controller

- **Qué:**
  - en `src/ArquitecturaBase.Api/Contracts/<Área>/`, `Create<Entidad>HttpRequest` y `Update<Entidad>HttpRequest` (el cuerpo; el id de la edición llega por la ruta). Copiá [`CreateRoleHttpRequest.cs`](../../src/ArquitecturaBase.Api/Contracts/Roles/CreateRoleHttpRequest.cs) y [`UpdateRoleHttpRequest.cs`](../../src/ArquitecturaBase.Api/Contracts/Roles/UpdateRoleHttpRequest.cs) ([ADR 0002](../decisions/0002-contratos-http.md));
  - en `src/ArquitecturaBase.Api/Controllers/`, `public sealed class <Área>Controller(I<Entidad>Service service) : ControllerBase`, con `[ApiController]`, `[Route("api/<recurso>")]` y `[Tags("<Área>")]`. Copiá [`RolesController.cs`](../../src/ArquitecturaBase.Api/Controllers/RolesController.cs).
- **Las acciones:**
  - **`List`**: `[HttpGet]` (en la raíz, no en `/paged`), con `[FromQuery] int? page, int? pageSize, string? sort, string? search`, el cuerpo de `RolesController.ListPaged` y `[ProducesResponseType<PagedResult<<Entidad>Response>>(StatusCodes.Status200OK)]`. Un listado paginado en la raíz, ya hecho: `UsersController.List` ([`UsersController.cs`](../../src/ArquitecturaBase.Api/Controllers/UsersController.cs));
  - **`Get`**: `[HttpGet("{id:guid}")]` con **`[FromRoute] Guid id`** y `[ProducesResponseType<<Entidad>Response>(StatusCodes.Status200OK)]`;
  - **`Create`**: `[HttpPost]`, `[ProducesResponseType<Guid>(StatusCodes.Status201Created)]` y `.ToCreatedResult(this, nameof(Get), id => new { id })`, que responde 201 con el `Location` del detalle;
  - **`Update`** y **`Delete`**: `[HttpPut("{id:guid}")]` y `[HttpDelete("{id:guid}")]`, con `[FromRoute] Guid id`, `[ProducesResponseType(StatusCodes.Status204NoContent)]` y `.ToActionResult(this)`;
  - cada acción, con `[HasPermission(Permissions.<Área>.Read)]` o `[HasPermission(Permissions.<Área>.Manage)]` ([`HasPermissionAttribute.cs`](../../src/ArquitecturaBase.Api/Authorization/HasPermissionAttribute.cs)), y `[ProducesProblem(StatusCodes.Status409Conflict)]` ([`ProducesProblemAttribute.cs`](../../src/ArquitecturaBase.Api/OpenApi/ProducesProblemAttribute.cs)) en las que pueden devolver un `Conflict`, con un comentario que diga cuál.
- **Trampas:**
  - `ToCreatedResult` lanza si la acción `Get` no existe en el mismo controller: el detalle va antes que el 201.
  - Sin `[FromRoute]` explícito, la convención de OpenAPI no deduce el 404 del `{id}`.
  - El controller mapea a mano el contrato al modelo de Application y responde solo con `ToActionResult`, `ToCreatedResult` o `ToAcceptedResult` ([`ControllerResultExtensions.cs`](../../src/ArquitecturaBase.Api/ErrorHandling/ControllerResultExtensions.cs)); nada de `Ok(...)` ni `IsSuccess ? … : …`. Solo inyecta interfaces de servicios.
  - Un contrato con datos personales, códigos o tokens sobrescribe `ToString()`. Una colección en la query necesita un contrato `*Query`; los valores simples no.
  - Nunca `[Authorize(Policy = …)]` ni roles: siempre `[HasPermission]`.
  - Si el área tiene reglas propias, creá `docs/features/<área>.md`, un `AGENTS.md` de una línea y un `CLAUDE.md` con `@AGENTS.md` en sus carpetas de código (como [`Services/Roles/AGENTS.md`](../../src/ArquitecturaBase.Application/Services/Roles/AGENTS.md)), y sumá el controller a [`Api/Controllers/AGENTS.md`](../../src/ArquitecturaBase.Api/Controllers/AGENTS.md).
- **Lo verifica** (arquitectura, sin Docker): [`ControllerInputContractTests`](../../tests/ArquitecturaBase.ArchitectureTests/ControllerInputContractTests.cs), [`PermissionAuthorizationTests`](../../tests/ArquitecturaBase.ArchitectureTests/PermissionAuthorizationTests.cs) y [`ControllerServiceRepositoryTests`](../../tests/ArquitecturaBase.ArchitectureTests/ControllerServiceRepositoryTests.cs). Los status y el documento OpenAPI, con los tests del paso 12.

### 12. Tests

Nombres en inglés, como frase (`A_repeated_name_is_rejected`). Lo que existe solo para probar va en el proyecto de tests, nunca en `src/`.

- **Unitarios del servicio**, en `tests/ArquitecturaBase.Application.UnitTests/Services/<Área>/`, en rojo antes del servicio:
  - las consultas, como [`RoleServiceTests`](../../tests/ArquitecturaBase.Application.UnitTests/Services/Roles/RoleServiceTests.cs) (`A_missing_role_is_not_found`, `A_sort_outside_the_list_is_a_validation_error_and_does_not_reach_the_reader`, `The_page_is_translated_to_responses_keeping_its_totals`), con un lector falso anidado en la clase de test;
  - las escrituras, como [`RoleServiceWriteTests`](../../tests/ArquitecturaBase.Application.UnitTests/Services/Roles/RoleServiceWriteTests.cs), con [`FakeUnitOfWork`](../../tests/ArquitecturaBase.Application.UnitTests/TestDoubles/FakeUnitOfWork.cs) (cuenta `Transactions` y `Commits`: un pedido inválido no abre transacción) y un validador real armado con [`RequestValidators.For(new …Validator())`](../../tests/ArquitecturaBase.Application.UnitTests/TestDoubles/RequestValidators.cs).
- **Integración de rutas**, en `tests/ArquitecturaBase.Api.IntegrationTests/<Área>/`:
  - como [`RoleCrudEndpointsTests`](../../tests/ArquitecturaBase.Api.IntegrationTests/Roles/RoleCrudEndpointsTests.cs): el alta con 201 y `Location` (`The_location_of_a_new_role_leads_to_its_detail` sigue el `Location` con un GET), el nombre repetido (409), la edición, el borrado y el 404;
  - como [`RolesEndpointsTests`](../../tests/ArquitecturaBase.Api.IntegrationTests/Roles/RolesEndpointsTests.cs): la página con sus totales, el 400 de un `sort` fuera de la lista (con `errors.sort`) y el 403 sin el permiso;
  - el administrador entra con `client.LoginAsync(factory, ApiFactory.AdminEmail)`, y una cuenta sin permisos con `client.LoginAsync(factory, TestEmails.Unique("…"))`.
- **Integración del lector**, como [`RoleReaderTests`](../../tests/ArquitecturaBase.Api.IntegrationTests/Persistence/RoleReaderTests.cs): la base es compartida, así que cada test crea sus filas con un prefijo único y busca por él; para ordenar por `createdAtUtc`, adelantá `factory.Clock` entre alta y alta (el reloj falso no avanza solo). Casos: la página y los totales, el orden descendente, el desempate estable, `%` y `_` literales en la búsqueda y, si hay borrado lógico, que la fila borrada no aparezca y que su nombre se pueda volver a usar.
- **Contratos comunes**, que ya existen y se amplían con una línea por ruta:
  - [`OpenApiTests`](../../tests/ArquitecturaBase.Api.IntegrationTests/OpenApiTests.cs): en `Each_operation_declares_only_the_errors_it_can_answer`, `AssertErrors(paths, "get", "/api/<recurso>", "400", "401", "403", "500")` y `AssertErrors(paths, "get", "/api/<recurso>/{id}", "401", "403", "404", "500")`; las que declaran un 409, en `Conflicts_rate_limits_and_refusals_of_anonymous_actions_are_declared` (como `put /api/roles/{id}`);
  - [`ApiAdministrationHttpContractsTests`](../../tests/ArquitecturaBase.Api.IntegrationTests/Contracts/ApiAdministrationHttpContractsTests.cs): cada ruta nueva en la teoría del 401 (`Every_route_requires_a_bearer_and_returns_a_translated_problem`) y las de escritura y el detalle en la del 403 (`Remaining_management_routes_reject_a_bearer_without_permission`), con un id como `00000000-0000-0000-0000-000000000001`.
- **Arquitectura:** no hay que escribir nada; los tests de los pasos anteriores cubren el área nueva solos.
- **Docker:** todo lo de integración corre solo con Docker. Sin él, que compile, y revisalo a mano con la [lista de verificación](#lista-de-verificación).

### 13. Inventario de rutas

- **Qué:** cada combinación nueva en `ExpectedRoutes` de [`ExplicitRouteInventoryTests`](../../tests/ArquitecturaBase.Api.IntegrationTests/Contracts/ExplicitRouteInventoryTests.cs), con el verbo en mayúsculas, la ruta en minúsculas y la restricción tal como está en el atributo (`"GET /api/<recurso>/{id:guid}"`), y el total del `Assert.Equal(<número>, ExpectedRoutes.Length)` sumado: un CRUD completo son cinco (`GET` listado, `GET` detalle, `POST`, `PUT`, `DELETE`).
- **Trampa:** es un test de integración: sin Docker no avisa. Revisá a mano que la lista y el número coincidan con las acciones del controller.
- **Lo verifica:** `The_explicit_business_routes_have_no_missing_or_duplicate_method_path_pairs`, con Docker.

### 14. Prefijo de backend (solo si la ruta no empieza con `/api`)

Una ruta bajo `/api` no toca nada de esto. Un prefijo nuevo (`/metrics`, por ejemplo) va en **cuatro** lugares:

1. `BackendPrefixes` en [`Api/Hosting/SpaExtensions.cs`](../../src/ArquitecturaBase.Api/Hosting/SpaExtensions.cs), para que el fallback del SPA no le conteste con el `index.html`;
2. un `[InlineData("/<prefijo>/no-existe")]` en `Backend_routes_keep_returning_a_problem` de [`SpaHostingTests`](../../tests/ArquitecturaBase.Api.IntegrationTests/Hosting/SpaHostingTests.cs);
3. el `server.proxy` de `vite.config.ts`, **en el repo del front** (`../ArquitecturaBaseFront`), para que en desarrollo Vite lo reenvíe a la Api;
4. el filtro `.Where(route => route.Contains(" /account/", …) || …)` de [`ExplicitRouteInventoryTests`](../../tests/ArquitecturaBase.Api.IntegrationTests/Contracts/ExplicitRouteInventoryTests.cs). Sin él, las rutas del prefijo nuevo no entran en el inventario y el test pasa sin verlas.

Los tres primeros son los de [`AGENTS.md`, "Front"](../../AGENTS.md#front); el cuarto es propio del inventario. Sin el primero (en producción) o el tercero (en desarrollo), una ruta inexistente del prefijo responde el `index.html` con 200 y el cliente recibe HTML donde esperaba JSON.

## Lista de verificación

Sin Docker:

- [ ] `dotnet build` sin advertencias ni errores (`TreatWarningsAsErrors`).
- [ ] Domain, Application y Architecture en verde.
- [ ] `dotnet ef migrations has-pending-model-changes` responde "No changes", y la migración tiene el índice único filtrado si hay borrado lógico.
- [ ] Las listas fijas del [paso 5](#5-permisos): `PermissionsTests`, `RoleServiceTests`, `RolesEndpointsTests` y `administracion.md`.
- [ ] Los errores, los permisos y los mensajes nuevos, en los dos `.resx` (español con voseo e inglés).
- [ ] El repositorio y el lector registrados en `Infrastructure/DependencyInjection.cs` (nada lo verifica sin Docker).
- [ ] El inventario: la lista y el número de `ExplicitRouteInventoryTests`, contra las acciones del controller.
- [ ] `OpenApiTests` y las teorías de 401 y 403 de `ApiAdministrationHttpContractsTests`, con una línea por ruta nueva.
- [ ] El prefijo, en los cuatro lugares del [paso 14](#14-prefijo-de-backend-solo-si-la-ruta-no-empieza-con-api), si la ruta no empieza con `/api`.
- [ ] Si el área tiene reglas propias: `docs/features/<área>.md` y el `AGENTS.md` y el `CLAUDE.md` de sus carpetas.
- [ ] Una pantalla nueva del front se dibuja antes de programarse (regla del front, en [`AGENTS.md`, "Front"](../../AGENTS.md#front)).

Con Docker: `dotnet test` completo en verde.

## Qué solo se ve con Docker

Los tests de `tests/ArquitecturaBase.Api.IntegrationTests` levantan Postgres con Testcontainers. Sin Docker compilan pero no corren, así que estas fallas pasan en silencio hasta la puerta:

- la migración que falta o sobra (`MigrationsTests`; la cubre en parte `has-pending-model-changes`);
- el inventario de rutas (`ExplicitRouteInventoryTests`), la lista y el número;
- el documento OpenAPI (`OpenApiTests`): un 2xx sin esquema, un error de más o de menos;
- las teorías de 401 y 403 (`ApiAdministrationHttpContractsTests`);
- el catálogo de permisos por HTTP (`RolesEndpointsTests`);
- el prefijo en `SpaHostingTests`;
- todo lo que depende de la base: el lector (orden, desempate, búsqueda), el índice único filtrado, el borrado lógico, el 201 con su `Location`, el 409 y el 404 de las rutas, y un repositorio o lector sin registrar.
