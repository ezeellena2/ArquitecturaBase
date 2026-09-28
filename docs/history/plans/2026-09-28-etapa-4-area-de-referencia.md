> **HISTÓRICO. Etapa cerrada el 2026-09-28.** No ejecutar: las casillas sin marcar y las instrucciones a agentes no son trabajo pendiente. Registro de cómo se diseñó y se ejecutó la Etapa 4 (Diseño de la Etapa 4 (Roles como área de referencia y la receta)), con los commits `692f3a3` a la corrección de la guía (`2883d0f`). El cierre, la puerta cumplida y lo que se hizo distinto están en la sección "Etapa 4" del [plan maestro](../../plans/2026-09-26-plantilla-estandar-por-etapas.md); las reglas vigentes, en `docs/architecture/backend.md` y en `docs/features/`.

> **Diseño de la Etapa 4** (2026-09-28). Lo escribió un agente, lo revisó un revisor adversarial y quedó corregido. Rige la [Forma de trabajo desde la Etapa 3](../../plans/2026-09-26-plantilla-estandar-por-etapas.md#forma-de-trabajo-desde-la-etapa-3) del plan maestro.

# Diseño de la Etapa 4: Roles como área de referencia y la receta, en 7 tareas (corregido)

Rige la "Forma de trabajo desde la Etapa 3" (plan :263-265). Las citas son de `58ce76b`. Alias: `App` = `src/ArquitecturaBase.Application`, `Infra` = `src/ArquitecturaBase.Infrastructure`, `IT` = `tests/ArquitecturaBase.Api.IntegrationTests`.

**P1 la decidió el usuario el 2026-09-28: opción B.** `GET /api/roles` queda igual, como catálogo completo para los selectores. El listado paginado va en `GET /api/roles/paged`. Como esto se aparta de la letra del ADR 0004 (:28, "query opcional") y del plan (:380), se suma una enmienda fechada al ADR y el desvío se anota en el plan.

**No hace falta preguntar** por el 201 de `POST /api/roles`, porque ya está decidido (plan :340 y :382). El front lo tolera:
- `createRole` descarta el valor (`RoleEditorPage.tsx:242-245`).
- `httpClient.ts:98-112` acepta cualquier 2xx.
- Queda viejo el comentario de `features/roles/api/roles.ts:36`. Va como pendiente del front.

**Antes de la tarea 1:** este diseño se guarda como `docs/history/plans/2026-09-28-etapa-4-area-de-referencia.md` y se enlaza desde la Etapa 4 del plan maestro, igual que el de la Etapa 3. Commit `docs:`.

**Limitación del entorno (verificada):** no hay Docker (`docker ps` no encuentra el socket). Los tests de integración de esta etapa solo compilan acá. Corren en la puerta, con Docker, junto con la puerta pendiente de la Etapa 3.

---

### 1. `GET /api/roles/{id}` y el renombre del ADR 0008
- **Objetivo:**
  - el detalle de un rol, con la misma forma que un ítem del listado;
  - es el destino del `Location` de la tarea 2;
  - de paso se cumple el pendiente del ADR 0008 (:21): "`IRoleReader.FindRoleAsync` pasa a `FindByIdAsync` en la Etapa 4".
- **Archivos:**
  - `App/Interfaces/Persistence/IRoleReader.cs:12`: `FindRoleAsync` pasa a `FindByIdAsync`. Hay que actualizar:
    - `RoleReader.cs:63`;
    - `RoleService.cs:146,180`;
    - los tres dobles (`RoleServiceTests.cs:67-87`, `RoleServiceWriteTests.cs:194-217` y `UserServiceTestHost.cs:111-118`);
    - `IT/Persistence/RoleReaderTests.cs:37,47`;
    - `IT/Persistence/RoleRepositoryTransactionTests.cs:42,88,113,124`;
    - `docs/decisions/0008-…md:21`: se borra la excepción, que ya no existe.
  - `App/Interfaces/Services/IRoleService.cs`:
    - se suma `Task<Result<RoleResponse>> GetRoleAsync(Guid roleId, CancellationToken)`;
    - a `GetRolesAsync` se le agrega un XML doc: "catálogo completo, para selectores".
  - `App/Services/Roles/RoleService.cs`:
    - la implementación usa `OperationLog.RunAsync(logger, "GetRole", …)`, `roles.FindByIdAsync` y `RoleErrors.NotFound`;
    - no valida nada ni abre límite, porque es una consulta;
    - el mapeo `RoleRow → RoleResponse` de :31-40 pasa a un `private static RoleResponse ToResponse(RoleRow)`, que también usa `GetRolesAsync`.
  - `Api/Controllers/RolesController.cs`: acción `Get` con:
    - `[HttpGet("{id:guid}")]`;
    - `[HasPermission(Permissions.Roles.Read)]`;
    - `[ProducesResponseType<RoleResponse>(StatusCodes.Status200OK)]`;
    - **`[FromRoute] Guid id`**, como `UsersController.Get` (:61-65). `ProblemResponsesConvention.cs:93` deduce el 404 de `BindingSource.Path`, así que el atributo explícito es el camino probado.
- **Tests:**
  - Unitarios en `RoleServiceTests`, rojo primero:
    - `A_missing_role_is_not_found`;
    - `The_detail_is_the_row_with_its_permissions`.
  - Integración:
    - En `RoleCrudEndpointsTests`:
      - `The_detail_of_a_role_shows_its_permissions_and_user_count`;
      - el 404 con `Roles.Role.NotFound`;
      - el helper `FindAsync` (:254-259) pasa a usar el GET por id;
      - `FindByNameAsync` (:261) sigue usando el catálogo.
    - En `RolesEndpointsTests`, el 403 sin `roles.read`, como :71-83.
    - En `ApiAdministrationHttpContractsTests`:
      - se suma `GET /api/roles/00000000-…-0001` a la teoría del 401 (:32-36);
      - también a la del 403 (:54-56).
    - En `OpenApiTests`:
      - `roleById` (:120-124) verifica también el `get` y su tag;
      - `AssertErrors(paths, "get", "/api/roles/{id}", "401", "403", "404", "500")`.
  - Inventario:
    - `ExplicitRouteInventoryTests` suma `"GET /api/roles/{id:guid}"`;
    - el test pasa a llamarse `The_explicit_business_routes_have_no_missing_or_duplicate_method_path_pairs`;
    - el número queda fijo en el `Assert.Equal` (:74): 42 en esta tarea y 43 después de la tarea 3;
    - se corrige el comentario "41-route inventory" de `IT/Support/TestControllerApplicationPart.cs:8-9` por uno sin número.
- **Riesgos:**
  - Con `{id:guid}` y `"paged"` no hay conflicto de rutas. La restricción `guid` no matchea `paged` y además gana el literal. Ya hay precedente: `GET /api/users/filter-counts` convive con `/api/users/{id:guid}`.
  - `FindByIdAsync` es `AsNoTracking`, como corresponde a un lector.

### 2. `POST /api/roles` responde 201 con `Location`
- **Archivos:**
  - `RolesController.Create`:
    - se borra el comentario de :23-24;
    - :27 pasa a `[ProducesResponseType<Guid>(StatusCodes.Status201Created)]`;
    - :33 pasa a `.ToCreatedResult(this, nameof(Get), id => new { id })`, igual que en `UsersController.cs:81`. `ToCreatedResult` lanza una excepción si no puede armar la ruta, así que la tarea 1 tiene que estar antes.
  - `docs/architecture/backend.md:250`: el alta de roles ya responde 201. Se suma, además, que después de la migración se agregaron `GET /api/roles/{id}` y `GET /api/roles/paged`. Las 41 de :248 se conservan: son las de la migración.
  - Plan maestro: `[x]` en :340.
- **Tests (todos los que hoy fijan el 200):**
  - `RoleCrudEndpointsTests.Admin_creates_a_role_with_its_permissions` (:27) pasa a `Created`;
  - **el helper `CreateAsync` de `RoleCrudEndpointsTests.cs:249`** pasa a `Created`;
  - **`ApiAdministrationHttpContractsTests.cs:274`** (`Omitting_permissions_from_role_update_…`) pasa a `Created`;
  - test nuevo, `The_location_of_a_new_role_leads_to_its_detail`, copiado de `CreateUserEndpointTests.cs:33`, que sigue el `Location` con un GET;
  - `OpenApiTests`: `post /api/roles` declara un `201` con esquema.
- **Riesgo:** en el back, ninguno. Del front solo queda el comentario viejo, que va como pendiente.
- **Orden:** va después de la tarea 1, en un commit propio para poder revertirlo.

### 3. El listado paginado de Roles (P1 = B)
- **Objetivo:** que Roles muestre `PagedRequest`, `SortableFields`, `ApplySort` y `ToPagedResultAsync` de punta a punta.
- **Archivos:**
  - `App/Models/Roles/ListRolesRequest.cs`:
    - `public sealed record ListRolesRequest : PagedRequest`, con `SortableFields = ["name", "createdAtUtc"]`;
    - verificado: `ApplicationRole` es `IAuditable` y tiene `CreatedAtUtc` (`ApplicationRole.cs:10,27`), que llena `AuditableEntityInterceptor`, incluido el seed vía `RoleManager.CreateAsync`;
    - los roles creados antes de la migración `RoleAuditing` tienen `0001-01-01` (default de la columna). No molesta: ordenan primero y el desempate por Id los estabiliza.
  - `App/Validation/Roles/ListRolesRequestValidator.cs`:
    - `internal sealed class ListRolesRequestValidator() : PagedRequestValidator<ListRolesRequest>(ListRolesRequest.SortableFields);`;
    - se registra solo, por `AddApplicationValidatorsFromAssembly` con `includeInternalTypes` (`App/DependencyInjection.cs:67,88`). Verificado.
  - `IRoleReader`:
    - el catálogo actual (:10) se renombra `ListAllRolesAsync`, para no tener dos `ListRolesAsync` con significados distintos. Hay que actualizar `RoleService.cs:30`, los tres dobles y `RoleReaderTests.cs:48`;
    - se suma `Task<PagedResult<RoleRow>> ListRolesAsync(ListRolesRequest, CancellationToken)`;
    - `PersistenceNamingTests` acepta las dos, porque `List` admite una colección o `PagedResult<T>` (`PersistenceNamingTests.cs:118,133-135`). No hay sobrecarga que lo complique.
  - `Infra/Persistence/Readers/RoleReader.cs`:
    - `SortMap` (`Dictionary<string, Expression<Func<ApplicationRole, object?>>>`) con `name → role.Name` y `createdAtUtc → role.CreatedAtUtc`;
    - `DefaultSort = new("name", false)`;
    - desempate `role => role.Id`, aplicado con `ApplySort` **antes** del `Select`, como en `UserReader.cs:189`.
    - Búsqueda:
      - `var pattern = LikePatterns.Contains(request.Search.Trim())`;
      - el filtro es `(role.Name != null && EF.Functions.ILike(role.Name, pattern, LikePatterns.EscapeCharacter)) || (role.Description != null && EF.Functions.ILike(role.Description, pattern, LikePatterns.EscapeCharacter))`;
      - los `!= null` no son adorno: `Name` y `Description` son `string?`, y sin ellos el build se rompe por nulabilidad (`TreatWarningsAsErrors`).
    - Por qué se busca también en la descripción:
      - el front no busca roles hoy (`RolesPage.tsx` no tiene buscador), así que no lo pide el producto;
      - se mantiene porque la mayoría de las áreas que se copien tienen nombre y descripción, y porque la descripción opcional es donde está la trampa del `null`, que la referencia tiene que mostrar;
      - si el usuario prefiere lo mínimo, se deja solo el nombre: la guía lo dice.
    - Una sola proyección:
      - `LoadRolesAsync` (:26-61) usa hoy un tipo anónimo, que no se puede compartir entre métodos;
      - pasa a un `private sealed record RoleData(Guid Id, string Name, string? Description, int UserCount, List<string> Permissions)`, con un método de instancia `private IQueryable<RoleData> Project(IQueryable<ApplicationRole>)`. Tiene que ser de instancia porque la expresión usa `dbContext.Users`, `UserRoles` y `RoleClaims`;
      - también un `private static RoleRow ToRow(RoleData)`, que hace `SystemRoles.All.Contains` y el orden ordinal de los permisos en memoria. Se conserva el comentario "la misma forma" (:22-25);
      - el catálogo (`OrderBy(Name)` y `ToListAsync`), el detalle (`Where(Id)`) y la página (`ApplySort` → `Project` → `ToPagedResultAsync`, y después `new PagedResult<RoleRow>([.. page.Items.Select(ToRow)], page.Page, page.PageSize, page.TotalCount)`) usan `Project`.
  - `Infra/Persistence/Extensions/LikePatterns.cs` (`internal static`; el proyecto de integración ve lo interno, `Infra.csproj:29`):
    - `public const string EscapeCharacter = "\\";`
    - `public static string Contains(string value)`, que devuelve `"%" + escapado + "%"`, con la lógica de `UserReader.cs:322-326` tal cual (primero la barra, después `%` y `_`).
    - El escapado actual se puede reutilizar sin cambios. `EF.Functions.ILike(string, string, string)` de Npgsql ya se usa con el carácter de escape en `UserReader.cs:274-275`.
    - `UserReader` pasa a usarlo: se borran :22 y :322-326. `PhoneSearchPatternOf` queda donde está, porque no escapa nada.
    - Quien use `LikePatterns.Contains` **siempre** pasa `EscapeCharacter` al `Like`/`ILike`. Va en el XML doc.
  - `IRoleService.ListRolesAsync(ListRolesRequest, CancellationToken)`:
    - devuelve `Task<Result<PagedResult<RoleResponse>>>`, con la operación de log `"ListRoles"`;
    - valida afuera y no abre límite, igual que `UserQueryService.ListUsersAsync` (:26-43).
  - `RolesController.ListPaged`:
    - `[HttpGet("paged")]`, `[HasPermission(Permissions.Roles.Read)]` y `[ProducesResponseType<PagedResult<RoleResponse>>(StatusCodes.Status200OK)]`;
    - recibe `[FromQuery] int? page, int? pageSize, string? sort, string? search`, como `UsersController.List` (:24-42). Son valores simples, así que `ControllerInputContractTests` no pide contrato;
    - la convención deduce el 400 de la query.
- **Tests:**
  - Unitarios en `RoleServiceTests`, rojo primero, con `RequestValidators.For(new ListRolesRequestValidator())`:
    - `A_sort_outside_the_list_is_a_validation_error_and_does_not_reach_the_reader` (`errors.sort`);
    - `The_page_is_translated_to_responses_keeping_its_totals`.
  - Dobles: los tres suman `ListRolesAsync(ListRolesRequest, …)` (con `throw new NotSupportedException()` donde no se use) y el renombre a `ListAllRolesAsync`.
  - Integración, `RoleReaderTests`:
    - cada test crea sus roles con un prefijo único y busca por ese prefijo, porque la base es compartida;
    - para ordenar por `createdAtUtc`, adelanta `factory.Clock` entre alta y alta, porque el reloj falso no avanza solo;
    - casos: la página y los totales; `-name`; el desempate estable con fechas iguales; `search` con `%` y `_` literales (el nombre no restringe caracteres: `CreateRoleRequestValidator`); y un rol sin descripción que no rompe la búsqueda.
  - Integración, endpoints (`RolesEndpointsTests`):
    - la página con sus totales;
    - el 400 de `sort=secret`, con `errors.sort`;
    - el 403.
  - En `ApiAdministrationHttpContractsTests`, la teoría del 401 suma `GET /api/roles/paged`.
  - En `OpenApiTests`:
    - `AssertErrors(paths, "get", "/api/roles/paged", "400", "401", "403", "500")`;
    - `GET /api/roles` sigue con `"401", "403", "500"`.
  - El inventario suma `"GET /api/roles/paged"`, con total 43.
- **Documentación:**
  - `docs/features/administracion.md`: el catálogo (`GET /api/roles`) frente al listado (`/paged`).
  - ADR 0004: enmienda fechada 2026-09-28 en "Consecuencias", que dice por qué es una ruta aparte y no una query opcional: dos esquemas en una operación no se pueden declarar y `OpenApiTests` exige un 2xx con esquema.
  - Plan :380: el desvío.
- **Riesgo:** `ToPagedResultAsync` hace dos consultas (`COUNT` y la página). EF saca la proyección con subconsultas del `COUNT`, así que no pesa.

### 4. Rehacer `IT/TestFeatures/Widgets`
- **Qué usa hoy cada pieza:**
  - las tres rutas las usan `PaginationTests` (6), `AuditingTests` (4), `ValidationProblemTests` (4), `LocalizationTests` (3), `SoftDeleteTests`, `ErrorHandlingTests` y `FrameworkErrorsTests`;
  - no se borra ninguna, y ningún test nombra los tipos: solo usan las URLs.
- **Cambios:**
  - `CreateWidget.cs` (ya contiene `CreateWidgetRequest` y su validador) se parte en `CreateWidgetRequest.cs` y `CreateWidgetRequestValidator.cs`.
  - `GetWidgets.cs` se parte en `ListWidgetsRequest.cs` (`GetWidgetsRequest` pasa a `ListWidgetsRequest`), `ListWidgetsRequestValidator.cs` y `WidgetListItemResponse.cs` (`WidgetResponse` pasa a `WidgetListItemResponse`).
  - `GetWidgetById.cs` pasa a `WidgetDetailResponse.cs`: **el tipo actual `WidgetDetailsResponse` se renombra `WidgetDetailResponse`**, en singular como `UserDetailResponse`.
  - `IWidgetTestService`:
    - `GetByIdAsync` pasa a `GetWidgetAsync`, y `ListAsync` a `ListWidgetsAsync`;
    - los nombres quedan como los de `IUserQueryService`.
  - Controllers:
    - las tres acciones salen de `TestController` (:15-42) a `WidgetsTestController`, con `[Route("test/widgets")]` y los mismos verbos y plantillas (`""`, `"{id:guid}"`);
    - `TestController` pierde su dependencia y se queda con `boom`, `dates`, `protected`, `admin`, `status` y `request-context`;
    - **se suma `typeof(WidgetsTestController)` a `IT/Support/TestControllerApplicationPart.cs:17`**. Si falta, las rutas `/test/widgets` desaparecen y fallan siete clases de test. `/test` no entra en el inventario (`ExplicitRouteInventoryTests.cs:67-70`).
  - `WidgetTestService`:
    - sigue con `ApplicationDbContext`, con un comentario que explica que es solo de prueba y no se copia;
    - la búsqueda pasa de `Like(Name, search + "%")` (prefijo, sensible a mayúsculas y sin escapar) a `ILike(Name, LikePatterns.Contains(search.Trim()), LikePatterns.EscapeCharacter)`, y se borra el descargo de :82. El cambio a "contiene" no rompe `PaginationTests`, porque los prefijos son únicos (`"pag-" + 8 hex`).
  - `Widget.cs` queda donde está: la usan `TestDbContext` y los tests de persistencia.
- **Qué no se cambia:** el `POST` sigue en 200 (`ValidationProblemTests.cs:50`). Son tests del arnés, no del contrato.
- **Test:** compila acá, y en la puerta corre con Docker.

### 5. `docs/guides/agregar-un-area.md`
- **Contenido:** la tabla de 14 pasos del plan (:385-400). Cada fila lleva la ruta destino y el enlace al archivo real de Roles. Donde Roles no sirve de ejemplo, se dice explícitamente:
  - **Paso 1 (entidad):**
    - Roles viene de Identity;
    - el ejemplo de entidad propia es `Domain/Settings/SystemSettings.cs` (`Entity` + `IAuditable`);
    - para `ISoftDeletable` no hay entidad de Domain en `src`, así que se enlaza `IT/TestFeatures/Widget.cs`;
    - `EntityConfigurationTests` exige su configuración.
  - **Paso 2:**
    - el ejemplo es `SystemSettingsConfiguration.cs`, con `ApplicationRoleConfiguration.cs` al lado;
    - `ApplyConfigurationsFromAssembly` (`ApplicationDbContext.cs:45`) la levanta sola, y el `DbSet` es opcional (los repositorios pueden usar `Set<T>()`);
    - con `ISoftDeletable` y un nombre único, el índice único lleva `HasFilter("\"IsDeleted\" = false")`, o un borrado bloquea el nombre para siempre.
  - **Paso 3 (migración):**
    - el comando de `backend.md` "Migraciones" (:235);
    - `dotnet ef` no viene con el repo (no hay `.config/dotnet-tools.json`). Se instala con `dotnet tool install --global dotnet-ef --version 10.0.12`, la versión de `Microsoft.EntityFrameworkCore.Design` en `Directory.Packages.props:66`, y `~/.dotnet/tools` tiene que estar en el `PATH`;
    - `migrations add` **no necesita Postgres**. Verificado en este entorno: `dotnet ef migrations has-pending-model-changes` con ese comando responde "No changes" sin base;
    - `MigrationsTests` sí necesita Docker.
  - **Paso 5 (permiso):**
    - los tres pasos de `AGENTS.md`. Alcanza con sumarlo a `Permissions.All`, porque `RoleSeeder.cs:20` se lo da a Admin en cada arranque;
    - las claves van en `Permissions.resx` y `.en.resx` (`Permission.*`, `PermissionDescription.*` y `Area.<área>`);
    - **listas fijas que hay que tocar con un área o un permiso nuevo:**
      - `Domain.UnitTests/Authorization/PermissionsTests.cs:11-19`;
      - `RoleServiceTests.cs:57`, con las áreas del catálogo;
      - `IT/Roles/RolesEndpointsTests.cs:42`, con las áreas;
      - `docs/features/administracion.md:14`, el catálogo.
  - **Pasos 6 y 7 (repositorio y lector):**
    - el lector de referencia es `RoleReader` (catálogo, detalle y página);
    - **`RoleRepository` no sirve de ejemplo para una entidad propia**: envuelve `RoleManager` con escrituras escalares;
    - para `Get` (entidad seguida) + `Add` se enlaza `SystemSettingsRepository`;
    - para el borrado de una `ISoftDeletable` se explica `dbContext.Remove(entidad)`, que el `SoftDeleteInterceptor` convierte en marca, y se prohíbe `ExecuteDelete`;
    - `PersistenceNamingTests` fija los prefijos;
    - el registro va en `Infra/DependencyInjection.cs:69`.
  - **Pasos 8 a 11:**
    - cada uno con su archivo de Roles;
    - el registro del servicio es a mano (`App/DependencyInjection.cs:57`), y los validadores se registran solos;
    - nota de B: **un área nueva pone el listado paginado directamente en `GET /api/<recurso>`**. `/paged` existe en Roles solo porque su `GET` ya era el catálogo de los selectores.
  - **Paso 12 (tests):**
    - `RoleServiceTests` y `RoleServiceWriteTests` (con `FakeUnitOfWork`), `RoleCrudEndpointsTests`, `RolesEndpointsTests`, `RoleReaderTests`, `OpenApiTests` (`AssertErrors`), las teorías de 401 y 403 de `ApiAdministrationHttpContractsTests`, y los de arquitectura, que lo cubren solos;
    - se aclara que la integración necesita Docker.
  - **Paso 13 (inventario):** la lista y el número en `ExplicitRouteInventoryTests`. Es un test de integración: sin Docker no avisa.
  - **Paso 14 (prefijo):**
    - los tres lugares de `AGENTS.md` ("Front"): `BackendPrefixes`, `SpaHostingTests` y `vite.config.ts`, este último **en el repo del front**;
    - **y un cuarto:** el filtro `.Where` de `ExplicitRouteInventoryTests.cs:67-70`. Sin él, las rutas del prefijo nuevo no entran en el inventario y el test pasa sin verlas.
    - de paso se corrige `backend.md:258`, que no lista `/webhooks` entre los prefijos que reenvía Vite (`vite.config.ts:39` sí lo tiene).
- **Lista de verificación al final:**
  - build sin advertencias;
  - Domain, Application y Architecture en verde;
  - inventario (lista y número), `OpenApiTests` y las teorías de 401 y 403;
  - las listas fijas del paso 5;
  - resx en los dos idiomas;
  - un `AGENTS.md` y un `CLAUDE.md` de carpeta si el área tiene reglas propias.
- **Enlaces que se suman:**
  - `AGENTS.md` ("Más documentación");
  - plan maestro, Etapa 5 (`docs/guides/`, parcial).
- **Riesgo:** los enlaces relativos. Se verifican con un `grep` de las rutas de la guía contra `git ls-files`.

### 6. Probar la receta con un subagente, sin ramas
- **Preparación:**
  - la guía ya está commiteada (tarea 5);
  - `git status --porcelain` sale vacío;
  - se anota `git rev-parse HEAD`;
  - `git ls-files -v global.json` muestra `S`;
  - se hace una copia `cp global.json <scratchpad>/global.json.antes`. El `global.json` local difiere de `HEAD` (10.0.100 contra 10.0.400) y `skip-worktree` lo oculta de `git status`/`diff`, así que la única forma de verificarlo es comparar el archivo;
  - `git stash list` se anota, para no tocar un stash ajeno.
  - Mientras el subagente trabaja, la sesión principal no toca el árbol.
- **Pedido al subagente, sin contexto:**
  - "Leé `AGENTS.md` y `docs/guides/agregar-un-area.md`. Agregá el área `Tags`, con:
    - entidad `Tag` de Domain, `Entity` + `IAuditable` + `ISoftDeletable`;
    - nombre obligatorio, de hasta 50 caracteres y único entre los no borrados;
    - descripción opcional, de hasta 256;
    - CRUD con `GET /api/tags` paginado y `GET /api/tags/{id}`;
    - permisos `tags.read` y `tags.manage`;
    - la migración."
  - Prohibido:
    - `git commit`, `stash`, `checkout`, `reset`, `restore`, `clean`, `update-index` y crear ramas;
    - tocar `global.json`;
    - `aspire run` y `dotnet ef database update`;
    - tocar el repo del front.
  - Como no puede preguntar, al final entrega dos listas:
    - las dudas: qué supuso y dónde lo buscó;
    - los archivos o las piezas que no encontró.
  - Verifica con `dotnet build` y con los tests de Domain, Application y Architecture. La integración solo compila.
- **Migración:**
  - `dotnet-ef` 10.0.12 **quedó instalado** como herramienta global (`/root/.dotnet/tools`, fuera del repo) durante esta revisión. El subagente tiene que sumarlo al `PATH`, y la guía dice cómo;
  - el paso 3 se puede verificar acá, porque `migrations add` no necesita base;
  - `MigrationsTests` no corre.
- **Revisión a mano** (sin Docker, la suite no lo ve):
  - inventario y número;
  - `OpenApiTests`;
  - las teorías de 401 y 403;
  - `RolesEndpointsTests.cs:42` y `RoleServiceTests.cs:57`;
  - el índice único filtrado.
- **Descarte:**
  - `git stash push --include-untracked -m etapa4-tags`, **sin pathspec**. Un pathspec `-- src tests docs` deja afuera cambios en `AGENTS.md`, `Directory.Packages.props`, `.editorconfig` o la raíz, y la comprobación siguiente fallaría;
  - verificado con git 2.43 en un repo de prueba, con y sin pathspec:
    - el stash respeta el `global.json` con `skip-worktree` (no lo guarda ni lo revierte);
    - `--include-untracked` con pathspec sí incluye los archivos nuevos;
    - ninguno toca los `bin/` y `obj/` ignorados;
  - `git stash show -p --include-untracked stash@{0} > <scratchpad>/etapa4-tags.patch` guarda la evidencia afuera del repo;
  - comprobar:
    - `git status --porcelain` vacío;
    - el mismo `HEAD`;
    - `global.json` en `S`;
    - `cmp global.json <scratchpad>/global.json.antes`;
    - que `stash@{0}` sea `etapa4-tags`;
  - `git stash drop stash@{0}`;
  - `dotnet build`: los `obj/` viejos se recompilan sin los fuentes borrados.
- **Métricas:**
  - cantidad de dudas;
  - archivos no encontrados;
  - reglas de arquitectura rotas;
  - piezas de integración que faltaron en la revisión a mano.

### 7. Corregir la guía y cerrar
- **Objetivo:** cada hueco de la tarea 6 se corrige en la guía, sin tocar el código de Tags, que ya se descartó.
- **Repetición:** si hubo dudas de fondo, se repite la tarea 6 con otro subagente hasta que no quede ninguna sin respuesta. Es la puerta del plan (:406).
- **Documentación:**
  - Etapa 4 del plan:
    - `[x]` con commits y desvíos (`/paged` y el catálogo; el renombre de `FindByIdAsync`; `LikePatterns`);
    - se actualiza la línea de estado (:3).
  - Pendientes del front:
    - el comentario de `features/roles/api/roles.ts:36`;
    - `RoleEditorPage.tsx:176-189` puede usar `GET /api/roles/{id}` en lugar de buscar en la lista;
    - `RolesPage` puede pasar a `/paged` más adelante.
- **Puerta:**
  - build y los tres proyectos sin Docker;
  - la integración, con Docker, queda pendiente junto con la de la Etapa 3;
  - lista de riesgo para esa corrida: `Roles/*`, `Persistence/RoleReaderTests`, `Persistence/RoleRepositoryTransactionTests`, `Contracts/*`, `OpenApiTests`, `PaginationTests`, `Users/*` (por `LikePatterns` en `UserReader`) y todo lo que usa `/test/widgets`.

**Orden:** diseño en `docs/history/plans` → 1 → 2 → 3 → 4 (usa `LikePatterns`) → 5 → 6 → 7.

### Critical Files for Implementation
- src/ArquitecturaBase.Api/Controllers/RolesController.cs
- src/ArquitecturaBase.Application/Services/Roles/RoleService.cs
- src/ArquitecturaBase.Infrastructure/Persistence/Readers/RoleReader.cs
- src/ArquitecturaBase.Infrastructure/Persistence/Readers/UserReader.cs
- tests/ArquitecturaBase.Api.IntegrationTests/Roles/RoleCrudEndpointsTests.cs
- tests/ArquitecturaBase.Api.IntegrationTests/Contracts/ApiAdministrationHttpContractsTests.cs
- tests/ArquitecturaBase.Api.IntegrationTests/Contracts/ExplicitRouteInventoryTests.cs
- tests/ArquitecturaBase.Api.IntegrationTests/Support/TestControllerApplicationPart.cs
- tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Widgets/WidgetTestService.cs

---

## Hallazgos aceptados
- ADR 0008:21 manda renombrar `FindRoleAsync` a `FindByIdAsync` en la Etapa 4, y el diseño lo ignoraba. Se suma a la tarea 1, con sus 9 llamadas.
- `ApiAdministrationHttpContractsTests.cs:274` y el helper `CreateAsync` de `RoleCrudEndpointsTests.cs:249` fijan el 200 del alta y no estaban en la tarea 2.
- `WidgetsTestController` tiene que sumarse a `TestControllerApplicationPart.cs:17`: si no, desaparecen las rutas `/test/widgets` y fallan 7 clases de test.
- El comentario "41-route inventory" de `TestControllerApplicationPart.cs:8-9` queda viejo con el inventario nuevo.
- Faltaban las teorías de 401 y 403 de `ApiAdministrationHttpContractsTests` (:32-36 y :54-56) para las dos rutas nuevas.
- `OpenApiTests` necesita un `AssertErrors` explícito para `/{id}` (401/403/404/500) y para `/paged` (400/401/403/500).
- La acción `Get` tiene que usar `[FromRoute]`, como `UsersController.Get`, para que la convención deduzca el 404 (`ProblemResponsesConvention.cs:93`).
- La proyección intermedia no puede ser anónima si la comparten tres métodos: pasa a un record privado con un `Project` de instancia, porque usa `dbContext`.
- La búsqueda en `Name` y `Description` (`string?`) necesita la guarda `!= null`, o el build se rompe por nulabilidad.
- La búsqueda en la descripción no la pide el producto (el front no busca roles). Se mantiene con una justificación explícita, y la alternativa queda en la guía.
- `RoleReaderTests` tiene que usar prefijos únicos y adelantar `factory.Clock`, porque la base es compartida y el reloj falso no avanza solo.
- El renombre a `ListAllRolesAsync` también toca `RoleReaderTests.cs:48`, que el diseño no nombraba.
- En la tarea 4, el diseño decía al revés el renombre de `WidgetDetailsResponse`: el tipo actual se llama así y pasa a `WidgetDetailResponse`.
- La búsqueda de Widgets pasa de "empieza con" a "contiene": no rompe `PaginationTests`, porque los prefijos son únicos.
- `git stash` con pathspec `src tests docs` deja afuera los cambios en la raíz (`AGENTS.md`, `Directory.Packages.props`), y la comprobación de árbol vacío fallaría. Va sin pathspec: está verificado que respeta el `skip-worktree`.
- El `global.json` local difiere de `HEAD` y `skip-worktree` lo esconde. Se verifica con una copia y `cmp`, no con `git status`.
- `dotnet-ef` 10.0.12 se instala como herramienta global. Quedó instalado en `/root/.dotnet/tools`, y `has-pending-model-changes` corrió sin Postgres, así que el paso 3 se puede verificar.
- La guía no decía cómo instalar `dotnet-ef` ni su versión (no hay manifiesto local).
- Paso 14: el filtro de prefijos de `ExplicitRouteInventoryTests.cs:67-70` es un cuarto lugar que `AGENTS.md` no nombra.
- `backend.md:258` no lista `/webhooks` entre los prefijos que reenvía Vite (`vite.config.ts:39` sí).
- Paso 5: un área o un permiso nuevo rompe listas fijas (`PermissionsTests.cs:11-19`, `RoleServiceTests.cs:57`, `RolesEndpointsTests.cs:42`, `administracion.md:14`) que la guía tiene que nombrar.
- `RoleRepository` no sirve de ejemplo de repositorio para una entidad propia: la guía enlaza `SystemSettingsRepository` y explica `Remove` con borrado lógico.
- Con `ISoftDeletable` y un nombre único hace falta un índice único filtrado (`HasFilter`), y la guía no lo decía.
- El pedido de Tags era ambiguo (¿borrado lógico?, ¿detalle?): se precisa para que las dudas midan la guía y no el enunciado.
- Sin Docker, la suite no ve el inventario, `OpenApiTests` ni las teorías de 401 y 403 del área de prueba: se suma una revisión a mano como métrica.
- El diseño tiene que quedar en `docs/history/plans/` y enlazado desde el plan maestro, como el de la Etapa 3.
- El registro del validador por escaneo está confirmado en `App/DependencyInjection.cs:67,88`; ya no queda "a confirmar".

## Descartados
- Conflicto de rutas entre `{id:guid}` y `paged`: no existe, por la restricción `guid` y la precedencia del literal, y `users/filter-counts` ya lo muestra.
- "`ApplicationRole` no tiene `CreatedAtUtc`": sí lo tiene (`ApplicationRole.cs:27`), y lo llena el interceptor, también en el seed.
- "`PersistenceNamingTests` exige el renombre del catálogo": acepta tanto sobrecargas como el nombre nuevo. El renombre se mantiene solo por claridad.
- "`EF.Functions.ILike` de Npgsql no admite escape": admite, y ya se usa así en `UserReader.cs:274-275`.
- "`git stash -u` se lleva o pisa `global.json` con `skip-worktree`": verificado que no, con y sin pathspec.
- "El stash toca los `bin/` y `obj/` ignorados": verificado que no.
- "`ToCreatedResult` con `nameof(Get)` y `id => new { id }` no arma el `Location`": es el mismo uso que `UsersController.cs:81` y `MvcResultContractTests`.
- "Los tests de Widgets dependen de los nombres de los tipos": solo usan las URLs, que no cambian.
- "Hay que actualizar las 41 de `backend.md:248` y del README": se refieren a la migración y siguen siendo ciertas. Solo se aclara en :250 que después se sumaron rutas.
- "La colección de Postman necesita las rutas nuevas": no tiene rutas de roles.