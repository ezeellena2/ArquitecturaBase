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
- Levantar todo: `aspire run` desde la raíz (Postgres + Api + front)
- **Apagarlo siempre al terminar de probar: `aspire stop`.** Si queda corriendo, el arranque desde Visual Studio falla con `address already in use` y los DLL quedan bloqueados. El contenedor de Postgres sí sobrevive a propósito (`ContainerLifetime.Persistent`).

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
- Los handlers no llaman a `SaveChanges`: lo hace `UnitOfWorkDecorator` si el resultado fue exitoso, o siempre si el comando implementa `IPersistChangesOnFailure` (por ejemplo, `VerifyLoginCode`, que guarda el intento fallido y la auditoría).
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
- Los errores que arma el propio framework (ruta inexistente, 405, 401/403 de la autorización) también salen como ProblemDetails (`ProblemDetailsMapper.CompleteFrameworkProblem` + `UseStatusCodePages`). Sus códigos están en `ApiErrorCodes`: `Http.*` por status, `General.Unexpected` para los 5xx y `Request.Invalid` para el resto de los 4xx. El 429 del rate limiter es distinto: `RateLimitingExtensions` arma su propio ProblemDetails con `retryAfter`; `UseStatusCodePages` solo completa la respuesta (en texto plano) cuando el cliente no acepta JSON.
- Todo middleware que pueda cortar con un error va en `Program.cs` después de `UseStatusCodePages`, o su respuesta sale vacía: `UseAuthentication`/`UseAuthorization` se declaran explícitos (no hay que dejar que `WebApplication` los agregue solo) y `UseRateLimiter` ya está después de `UseStatusCodePages`.

## Identidad

- Application accede a usuarios, roles y sesión solo por `IIdentityService`; `UserManager`/`SignInManager` no salen de Infrastructure.
- `/api` usa bearer (validación de OpenIddict, esquema por defecto). La cookie de Identity la usan solo `/account` y `/connect`.
- Los endpoints piden permisos, nunca roles: `.RequirePermission(Permissions.Users.Read)`.
- Un permiso nuevo:
  1. se declara en `Domain/Authorization/Permissions.cs` y en `Permissions.All`, y en `Permissions.resx` y `.en.resx` lleva `Permission.<código>` y `PermissionDescription.<código>` (y `Area.<área>` si el área es nueva); lo verifican `PermissionTextsTests` y `ResourceParityTests`;
  2. el seed se lo da a Admin;
  3. si cambian los permisos de un rol, hay que llamar a `IPermissionService.InvalidateRoleAsync`.
- Los claims de los tokens los arma `Api/Endpoints/Connect/OpenIdPrincipalFactory.cs`. Los permisos no van en el token.
- Fuera de Development y Testing, OpenIddict firma y cifra con dos PFX propios: `Authentication:Certificates:{Signing,Encryption}` con `Base64` (o `Path`) y `Password`. Los carga `CertificateLoader`, y `Base64` gana sobre `Path` porque en un contenedor el certificado llega como secreto, no como archivo. Regenerarlos invalida todos los tokens emitidos.
- Nunca registrar códigos, tokens ni secretos. La auditoría de ingresos guarda el motivo del fallo (el código de error), nunca el código ingresado.
- Emails: plantillas embebidas en `Infrastructure/Emails/Templates` y textos en `Emails.resx`/`Emails.en.resx`, en el idioma del perfil.

## Administración (Fase 4)

- **Ajustes del sistema:** `SystemSettings` es una entidad de **una sola fila**, auditable. Se lee cacheada con `HybridCache` y el caché se invalida al guardar, así el cambio vale al instante. El seed la crea con el valor de `Registration:Mode` (por defecto `InviteOnly`); **si la fila ya existe, manda la base**: un despliegue nunca pisa lo que se configuró desde el panel.
- **El modo de registro** decide quién puede *crear* una cuenta, no quién puede entrar. `POST /account/login-code` sigue respondiendo siempre `202`, y en `InviteOnly` un correo sin cuenta **igual emite y guarda su fila de `LoginCode`**: lo único que no pasa es que se mande el email. La fila se emite a propósito y no hay que "optimizarla": los límites por dirección se apoyan en ella, y sin ella una dirección desconocida respondería `202` para siempre mientras una registrada empieza a responder `429`, que es todo lo que hace falta para enumerar cuentas. Vence sola a los 10 minutos sin que nadie la use. Con Google, en cambio, la persona ya probó ser dueña de la dirección, así que vuelve al ingreso con `Account.NotInvited`. Lo mismo pasa si alguien llega a verificar un código válido para un correo sin cuenta (por ejemplo, porque el modo cambió con el código en vuelo): el verify responde `403 Auth.Account.NotInvited` en lugar de crear la cuenta.
- **Desactivar o eliminar tiene que cortar el acceso en el momento:** además de marcar la fila, se revocan las autorizaciones y los tokens de OpenIddict y se actualiza el `SecurityStamp` para invalidar la cookie. Sin eso, "desactivar" es una etiqueta que no impide nada durante los 15 minutos que vale el access token.
- **`ApplicationUser` es `ISoftDeletable`:** un usuario borrado desaparece de los listados y no puede entrar, pero su historial de ingresos sigue existiendo. Dar de alta el mismo correo restaura la cuenta, con los roles que diga el alta, no con los que tenía antes.
- **Las reglas que protegen al sistema viven en `Application/Features/Users/UserGuards.cs`**, con sus tests unitarios en `Application.UnitTests`. No están en Domain porque hay que contar administradores activos, y eso vive en Identity: nadie se saca a sí mismo el rol `Admin`, nadie desactiva ni elimina su propia cuenta, siempre queda al menos un usuario activo con rol `Admin`, y no se borra un rol con usuarios asignados.
- **`Admin` y `User` son del sistema:** no se renombran ni se borran, y a `Admin` no se le editan los permisos.
- El permiso nuevo es `settings.manage`, que el seed le da a `Admin`. El catálogo queda en `users.read`, `users.manage`, `roles.read`, `roles.manage` y `settings.manage`.
- Los enums que viajan en una respuesta lo hacen **por su nombre**, no por su número (`JsonStringEnumConverter` en `Api/DependencyInjection.cs`): el número no dice nada del otro lado y reordenar el enum cambiaría en silencio lo que significa cada valor guardado.

## Front

- El SPA vive en `../ArquitecturaBaseFront` (React + Vite). El AppHost lo levanta como un recurso más.
- **Un solo origen.** El navegador habla siempre con una sola dirección: en desarrollo, `https://localhost:5173`, donde Vite sirve el SPA y reenvía `/api`, `/account`, `/connect`, `/signin-google` y `/.well-known` a la Api (`https://localhost:7180`); en producción, la Api sirve las dos cosas. No hay CORS y no se configura.
- **El issuer es el origen público, no el de la Api.** Se fija con `Authentication:Issuer` (en desarrollo, `https://localhost:5173/`). `SetIssuer` cambia solo el campo `issuer`: los demás endpoints del documento de discovery salen del `Host` del request, y por eso el proxy de Vite va con `changeOrigin: false`. Si alguna vez el front cambia de origen, hay que mover también las redirect URIs del cliente `web`.
- **El fallback del SPA no toca las rutas del backend.** `UseSpaFallback` (`Api/Hosting/SpaExtensions.cs`) es un middleware, no un `MapFallback`: solo atiende GET y HEAD que no matchearon ningún endpoint, que no parecen un archivo y que no empiezan con un prefijo de backend. Así las rutas inexistentes de la Api siguen devolviendo su ProblemDetails, y el 405 y el 415 que arma el routing no se los come un catch-all.
- `BackendPrefixes` es una **lista a mano**: un prefijo de backend nuevo (`/webhooks`, `/metrics`, lo que sea) hay que sumarlo ahí, al `Backend_routes_keep_returning_a_problem` de `SpaHostingTests` y al `server.proxy` de `vite.config.ts`. Si falta, sus rutas inexistentes devuelven el `index.html` con 200 y el cliente recibe HTML donde esperaba JSON.
- En producción la Api sirve el SPA desde `wwwroot`. **Nada copia todavía el `dist/` del front a ese `wwwroot`** (ver los pendientes del despliegue en el README). Sin `wwwroot/index.html` el middleware no se instala y la Api funciona como Api sola.
- **Una pantalla nueva se dibuja antes de programarse**, como un tablero del Artifact del sistema visual. La regla, con su enlace y con lo que el tablero tiene que mostrar, vive en `../ArquitecturaBaseFront/docs/design/visual-baseline.md`, en “Pantalla nueva: primero el tablero”.

## Persistencia

- Un repositorio por agregado: la interfaz en Domain y la implementación en Infrastructure. No hay repositorio genérico.
- Las entidades heredan de `Entity` (Id Guid v7) o `AggregateRoot` (acumula eventos de dominio).
- Cada entidad tiene su `IEntityTypeConfiguration<T>` en `Infrastructure/Persistence/Configurations/`.
- `IAuditable` e `ISoftDeletable` los completan los interceptores; nunca se setean a mano.
- `ExecuteUpdate`/`ExecuteDelete` saltean los interceptores: no se usan con entidades `IAuditable` o `ISoftDeletable` (se borraría físicamente y sin auditoría).
- Las filas borradas se ocultan con un filtro global. Para verlas: `IgnoreQueryFilters()`.
- Para poner en fila operaciones sobre un mismo recurso (por ejemplo, los códigos de un mismo destino, correo o número), el repositorio toma un lock de Postgres (`pg_advisory_xact_lock`) en una transacción, y `UnitOfWork` la confirma al guardar. Ver `LoginCodeRepository.LockDestinationAsync`. Esa transacción manual no convive con los reintentos automáticos de EF (`EnableRetryOnFailure`): si alguna vez se activan (por ejemplo, con `AddNpgsqlDbContext` de Aspire), `LockDestinationAsync` tiene que pasar a usar la estrategia de ejecución.
- Paginado:
  - la consulta hereda de `PagedRequest` y declara `SortableFields`;
  - su validador hereda de `PagedRequestValidator<T>`;
  - Infrastructure ordena con `ApplySort` (un mapa campo → expresión, con los mismos nombres, y un desempate único, normalmente el Id, para que las páginas sean estables) y pagina con `ToPagedResultAsync`.
- Filtros de un listado (desde la Fase 5):
  - **viven en un record propio de `Abstractions`, no en la consulta.** `UserListRequest` es el ejemplo: lo heredan `GetUsersQuery` y `GetUserFilterCountsQuery`, que tienen que filtrar **exactamente igual** o los conteos de cada opción dejan de describir al listado que dicen describir. Por el mismo motivo hay un `UserListRequestValidator<T>` compartido y un solo armador de la consulta en Infrastructure (`IdentityService.FilterUsers`).
  - **Un valor que no existe no es un 400, es una lista vacía.** `role=NoExiste` devuelve cero resultados: contestar 400 diría qué nombres de rol existen, y eso no se cuenta por el camino de un filtro. Lo que sí es 400 es un `role=` **presente y vacío**, porque el parámetro ausente ya significa "sin filtro" y devolver todo parecería un filtro roto.
  - Los filtros se enlazan a mano en el endpoint, como los de `PagedRequest`, y se comparan por la columna que tiene índice (para un rol, `NormalizedName`, no `Name`).
  - Las fechas relativas salen de `TimeProvider`, nunca de `DateTime.UtcNow`. En los tests de integración se mueven con `factory.Clock.Advance`, que es el mismo reloj que usa el interceptor de auditoría: no se toca `CreatedAtUtc` a mano.
- Conteos por opción de filtro (`GET /api/users/filter-counts`):
  - cada dimensión se cuenta **con los demás filtros puestos e ignorando el propio**. Es toda la gracia: con "solo activos" puesto, el número de Admin es cuántos activos quedarían al elegir Admin, y "Inactivos" sigue diciendo cuántos hay del otro lado en vez de 0.
  - el catálogo viene **completo**, con los que dan cero: la opción apagada tiene que poder verse, y para eso hay que saber que existe.
  - las opciones de un tramo (los días) viven en el backend, al lado del filtro (`UserListRequest.CreatedWithinOptions`): el que cuenta y el que dibuja las opciones tienen que estar de acuerdo.
  - es un endpoint aparte y no un campo de `PagedResult<T>`, que es genérico y lo comparten todos los listados: meterle facetas lo ataría a este caso.
  - **Cuesta cuatro consultas de agregación por pedido** (estado, roles y una por tramo). Con miles de usuarios es despreciable; con cientos de miles hay que medir antes de sumar dimensiones.
- Migraciones (desde la Fase 2): la Api necesita `Microsoft.EntityFrameworkCore.Design` (`PackageReference` con `PrivateAssets="all"`, versión en `Directory.Packages.props`). El comando pasa la cadena de conexión como argumento de la aplicación, porque la Api solo la recibe de Aspire:
  ```
  dotnet ef migrations add <Nombre> --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api --output-dir Persistence/Migrations -- --environment Development --ConnectionStrings:appdb "Host=localhost;Port=5433;Database=appdb;Username=postgres;Password=postgres"
  ```
  En desarrollo, la Api las aplica al iniciar.
  - `MigrationsTests` falla si el modelo cambia y falta la migración.
  - Las migraciones son código generado: `.editorconfig` las excluye del estilo.

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
- Los secretos van en user-secrets (Api: `Authentication:Google:ClientSecret`, `Email:Smtp:Password`) o en variables de entorno, nunca en el repo. Excepciones de desarrollo local: la contraseña de Postgres en `src/ArquitecturaBase.AppHost/appsettings.Development.json` y la clave HMAC de los códigos en `src/ArquitecturaBase.Api/appsettings.Development.json`.

## Tests

- **Domain.UnitTests y Application.UnitTests:** xUnit v3, sin dependencias externas.
- **ArchitectureTests:** reglas de capas (NetArchTest y las referencias de cada `.csproj`).
- **Api.IntegrationTests:** `ApiFactory` (WebApplicationFactory + Testcontainers `postgres:18.3`).
  - Reutiliza la registración del DbContext de producción: solo cambia el tipo de contexto (`TestDbContext`) y la cadena de conexión. No volver a registrar el DbContext en el arnés.
  - Lo que existe solo para probar (entidades, endpoints `/test`, handlers) va en `TestFeatures/` del proyecto de tests, nunca en `src/`.
  - Autenticación:
    - `AuthFlow.LoginAsync` hace el ingreso real (código → authorize con PKCE → token) y devuelve los tokens;
    - con el header `X-Test-UserId`, en cambio, se usa el usuario de prueba.
  - `factory.EmailSender` guarda los emails: el código es la primera palabra del asunto.
  - Los límites están relajados:
    - sin espera entre pedidos de código;
    - rate limiter alto.
    Para probar un límite, usar `factory.WithWebHostBuilder(...)` con el valor real.
- Nombres de tests en inglés, como frase: `Deleted_rows_are_hidden_from_queries_and_endpoints`.
