# ArquitecturaBase: reglas para agentes

Plantilla .NET 10 + Aspire 13.5 + PostgreSQL; el front vive en `../ArquitecturaBaseFront`. Este archivo es el índice de reglas; el detalle está en [`docs/architecture/backend.md`](docs/architecture/backend.md), que prevalece sobre cualquier descripción histórica. Las reglas de un área del producto están en `docs/features/` y se leen antes de tocarla. WhatsApp, BusinessForms e IA son módulos opcionales en `Modules/<Módulo>`: el núcleo no los nombra.

## Forma de trabajo

- Cada cambio sigue [revisar y validar un cambio](docs/guides/validar-un-cambio.md): buscar el patrón existente antes de crear, justificar lo nuevo, revisar el diff y separar pruebas, revisión y pendientes.
- Antes de editar, leé los `AGENTS.md` desde la raíz hasta la carpeta afectada ([mapa](docs/architecture/mapa-de-instrucciones.md), [receta](docs/guides/instrucciones-por-carpeta.md)). Un área o responsabilidad nueva actualiza su `AGENTS.md` y el mapa en el mismo desarrollo; cada `CLAUDE.md` local importa `@AGENTS.md`.
- Para formularios, leé el resumen de la etapa en curso en [`docs/plans/formularios/`](docs/plans/formularios/README.md). Es el alcance y las decisiones ya tomadas: no se vuelven a preguntar.
- Lo que el plan no fija se resuelve con la opción más simple que cumpla el plan y estas reglas, sin preguntar ni agregar mecanismos por las dudas. Un contrato todavía no publicado (creado en el desarrollo en curso) se cambia revisándolo con el front, sin capas de compatibilidad; el [ADR 0005](docs/decisions/0005-sin-versionado-de-api-por-ahora.md) aplica a consumidores existentes. No se protege contra clientes, datos o casos que no existen.
- La robustez es proporcional al efecto ([ADR 0018](docs/decisions/0018-robustez-proporcional-al-efecto.md)): recuperación durable, estado incierto y pruebas de varios hosts solo para efectos fuera del sistema (Meta, correo, archivos, LLM). La configuración interna se guarda con un límite, control de revisión y 409. El contrato con el front son los DTO, el OpenAPI y los ejemplos exportados.
- TDD donde hay lógica: el test en rojo primero. En cada corte, el build y los tests rápidos del área (unitarios y de arquitectura, sin Docker). Al terminar la tarea, una vez: la integración con PostgreSQL/Redis, `dotnet test` completo, el build sin advertencias y `aspire stop` si levantaste la app. Sin Docker no se da por cerrado.
- Se trabaja en `main`, sin ramas ni push salvo pedido explícito. Commits chicos, en español, con conventional commits.
- Al cerrar un desarrollo, sus decisiones van a las fuentes de las [premisas](docs/architecture/premisas-de-la-plantilla.md) y el plan terminado se retira con la [auditoría documental](docs/guides/auditar-documentacion.md); Git conserva el historial. Planes y revisiones se actualizan al cerrar cada paso, con un resumen corto.

## Comandos

- Compilar: `dotnet build ArquitecturaBase.slnx`. Todos los tests: `dotnet test`. Una clase: `dotnet test --project tests/<Proyecto>/<Proyecto>.csproj -- --filter-class "<Namespace.Clase>"`.
- Levantar todo: `aspire run`. **Apagarlo siempre con `aspire stop`**: si queda corriendo, Visual Studio falla con `address already in use`. El contenedor de Postgres sobrevive a propósito.
- Migraciones: [`docs/guides/migracion.md`](docs/guides/migracion.md).

## Capas y dependencias

Las verifica `tests/ArquitecturaBase.ArchitectureTests` ([detalle](docs/architecture/backend.md#proyectos-y-dependencias)).

| Proyecto | Puede referenciar |
|---|---|
| Domain | solo la BCL: modelo y reglas de negocio puras |
| Application | Domain y los paquetes de `ApplicationPackagesTests`; sin EF Core, ASP.NET Core, `ApplicationDbContext`, `HttpContext` ni tipos de Infrastructure o Api |
| Infrastructure | Application y Domain. EF de negocio detrás de repositorios o lectores; el contexto directo, solo en componentes técnicos. `IQueryable` no sale de acá |
| Api | Application, Infrastructure (solo desde `Program.cs`) y ServiceDefaults. Adaptadores de la petición en `Api/RequestContext` |
| AppHost | Api, como recurso de Aspire |

## Borde HTTP y casos de uso

- Recorrido: `Controller → interfaz de servicio → servicio → repositorio, lector o integración → Infrastructure`. Los controllers inyectan servicios, nunca EF, repositorios, lectores ni handlers. Sin `ICommand`, `IQuery`, handlers, repositorio genérico, Minimal APIs de negocio ni Scrutor. Mapeos a mano.
- Rutas MVC en `api/<recurso>`, sin versión. La entrada es un contrato de `Api/Contracts/<Área>` (`*HttpRequest` o `*Query`, [ADR 0002](docs/decisions/0002-contratos-http.md)); si lleva datos personales, códigos o tokens, sobrescribe `ToString()`. El `Result` se responde con `ToActionResult`, `ToAcceptedResult` o `ToCreatedResult` ([detalle](docs/architecture/backend.md#borde-http)).
- Permisos, nunca roles: `[HasPermission(Permissions.X)]`; 401 sin sesión, 403 sin permiso. Un permiso nuevo sigue [permiso-nuevo.md](docs/guides/permiso-nuevo.md).
- Un middleware que pueda cortar con un error va en `Program.cs` después de `UseStatusCodePages`, o su respuesta sale vacía.
- Cada ruta figura en `ExplicitRouteInventoryTests` y tiene su test. Cambiar esta estructura requiere un ADR; un área nueva sigue [agregar-un-area.md](docs/guides/agregar-un-area.md).

## Guardar, errores y persistencia

- `IUnitOfWork.ExecuteInTransactionAsync(trabajo, CommitPolicy, ct)` es la única forma de guardar: una vez por método público que escribe ([ADR 0001](docs/decisions/0001-transaccion-explicita-por-caso-de-uso.md)). `OnAnyResult` solo cuando también hay que guardar al fallar. Reglas, ejemplo (`RoleService.UpdateAsync`) y locks: [backend.md](docs/architecture/backend.md#una-sola-forma-de-guardar).
- Las reglas de negocio devuelven `Result`/`Result<T>` y no lanzan; las excepciones son bugs o fallas de infraestructura (500 con `traceId`). Los errores se declaran en `<Entidad>Errors` dentro de Domain, con códigos estables `Area.Entidad.Motivo` traducidos en `Errors.resx`; nadie fuera de Domain arma un `Error` (`ErrorDeclarationTests`). Respuestas de error: ProblemDetails con `title`, `detail`, `code`, `traceId` y `errors` en validaciones ([detalle](docs/architecture/backend.md#validación-guardado-y-errores)).
- Repositorios y lectores: contratos en `Interfaces/Persistence`, EF en `Infrastructure/.../Persistence/{Repositories,Readers}`. Nombres por lo que devuelven: `Get` (entidad seguida, solo repositorios), `Find` (proyección), `List`, `Exists`, `Count`, `Lock`. Solo los lectores usan `AsNoTracking` ([ADR 0008](docs/decisions/0008-nombres-de-repositorios-y-lectores.md)).
- Entidades: heredan de `Entity` (Guid v7), sin eventos de dominio, con su `IEntityTypeConfiguration<T>`. `IAuditable` e `ISoftDeletable` los completan los interceptores; no usar `ExecuteUpdate`/`ExecuteDelete` con ellas.
- Paginado: `PagedRequest` con `SortableFields`, `PagedRequestValidator<T>`, `ApplySort` con desempate único y `ToPagedResultAsync`.

## Otras reglas fijas

- **Caché:** Redis directo es el único caché compartido ([ADR 0009](docs/decisions/0009-redis-como-unico-cache-de-datos.md)); solo `RedisCache` usa el cliente. Agregar caché sigue [agregar-cache.md](docs/guides/agregar-cache.md).
- **Fechas:** UTC desde `TimeProvider` inyectado; `DateTime.Now`/`UtcNow` rompen el build. Propiedades terminadas en `Utc`; sin hora, `DateOnly`. Tests con `FakeTimeProvider`.
- **Idioma:** identificadores, excepciones y logs en inglés. Textos del usuario en `.resx` con su `.en.resx` (`ResourceParityTests`), en español rioplatense con voseo.
- **Logs:** `[LoggerMessage]`, nunca `logger.LogX`. Nunca códigos, tokens, enlaces de ingreso, secretos ni teléfonos enteros.
- **Build:** `TreatWarningsAsErrors`; las advertencias se corrigen y solo se suprimen en `.editorconfig` con justificación. Versiones de paquetes solo en `Directory.Packages.props`.
- **Secretos:** user-secrets o variables de entorno, nunca en el repo (excepciones de desarrollo local: contraseña de Postgres del AppHost y clave HMAC de la Api).
- **Tests:** Domain/Application con xUnit v3 sin dependencias; ArchitectureTests para capas y convenciones; IntegrationTests con `ApiFactory` y Testcontainers. Lo que existe solo para probar va en `TestFeatures/`. Nombres en inglés, como frase.
- **Front:** se sirve desde el mismo origen. **Una pantalla nueva se dibuja antes de programarse** ([visual-baseline](../ArquitecturaBaseFront/docs/design/visual-baseline.md)). Un prefijo de backend nuevo sigue [prefijo-de-backend.md](docs/guides/prefijo-de-backend.md).

## Dónde va cada cosa

Dentro de un módulo opcional, las mismas carpetas van bajo `<Proyecto>/Modules/<Módulo>/` ([módulos](docs/architecture/backend.md#módulos-opcionales)).

| Pieza | Carpeta |
|---|---|
| Controller y contrato de entrada | `Api/Controllers/` y `Api/Contracts/<Área>/` |
| Interfaz de servicio; servicio y helpers (`*Policy`, `*Guard`, `*Recorder`…) | `Application/Interfaces/Services/`; `Application/Services/<Área>/` |
| Interfaz de repositorio, lector, `IUnitOfWork` | `Application/Interfaces/Persistence/` |
| Puerto del núcleo hacia un módulo | `Application/Interfaces/Channels/` (versión apagada en `Application/Channels/`) |
| Interfaz de proveedor externo | `Application/Interfaces/Integrations/<Tema>/` |
| Modelo, validador, configuración funcional | `Application/Models/<Área>/`, `Application/Validation/<Área>/`, `Application/Configuration/` |
| Texto que ve el usuario | `Application/Resources/*.resx` y `.en.resx` |
| Entidad y `<Entidad>Errors`; permiso | `Domain/<Área>/`; `Domain/Authorization/Permissions.cs` o `<Módulo>Permissions.cs` |
| Repositorio, lector, configuración EF, migraciones | `Infrastructure/Persistence/{Repositories,Readers,Configurations,Migrations}/` |
| Adaptador técnico o worker | `Infrastructure/<Tema>/` |
| Registro en DI | el `*Registration.cs` de la capa dueña; `Program.cs` compone |
| Tests | unitarios en la carpeta equivalente; rutas y persistencia en `IntegrationTests/<Área>/`; reglas en `ArchitectureTests/` |
| Decisión, regla de área, receta | `docs/decisions/`, `docs/features/`, `docs/guides/` |

## Más documentación

- [`backend.md`](docs/architecture/backend.md): la arquitectura completa. [`docs/guides/`](docs/guides/): recetas (área nueva, permiso, migración, quitar WhatsApp o IA, despliegue).
- [`docs/features/`](docs/features/): reglas implementadas de identidad, WhatsApp, administración, [formularios](docs/features/formularios.md) e [IA](docs/features/inteligencia-artificial.md).
- [ADR](docs/decisions/README.md), [premisas](docs/architecture/premisas-de-la-plantilla.md), [planes pendientes](docs/plans/README.md) e [historial de planes retirados](docs/history/README.md).
- [Arneses de desarrollo](harnesses/README.md), incluido el auditor documental.
- [`README.md`](README.md): cómo levantar y probar. [`CLAUDE.md`](CLAUDE.md): lo propio de Claude Code.
