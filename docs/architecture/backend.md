# Arquitectura canónica del backend: MVC, servicios y persistencia especializada

**Estado:** decisión aprobada el 2026-09-24. El 2026-09-26 sumó el [borde HTTP](#borde-http) y los tests de arquitectura nuevos, que aplican las decisiones del [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md) sin cambiar los límites de las capas.

**Ubicación:** hasta el 2026-09-27 vivía en `docs/specs/2026-09-24-backend-mvc-architecture.md`. Ese día (Etapa 5) se mudó acá y sumó, sin reescribirlas, las reglas de detalle que estaban en `CLAUDE.md`: [errores del framework](#errores-del-framework-pipeline-y-json), [una sola forma de guardar](#una-sola-forma-de-guardar), [migraciones](#migraciones), [front](#front-y-hosting-del-spa) y [tests](#tests-arquitectura-y-arnés). El índice de las reglas de la plantilla es [`AGENTS.md`](../../AGENTS.md).

**Alcance:** `ArquitecturaBase` backend. `ArquitecturaBaseFront` mantiene su proyecto separado; `ArquitecturaBaseMultitenant` queda fuera de este alcance.

**Ejecución:** [plan de migración](../history/plans/2026-09-23-migracion-mvc-servicios-repositorios.md).

Este documento fija la **arquitectura vigente del backend**. Ante una contradicción con el diseño inicial de 2026-09-18 o con una guía histórica que describa handlers, prevalece esta decisión. Los contratos funcionales y de seguridad implementados siguen vigentes. El [plan de migración](../history/plans/2026-09-23-migracion-mvc-servicios-repositorios.md) conserva el historial y las puertas de verificación antes de integrar el cambio a `main`.

## Recorrido obligatorio de un caso de uso HTTP

```text
Cliente
  → Controller MVC (Api)
  → interfaz de servicio (Application/Interfaces/Services)
  → servicio de aplicación (Application/Services/<Área>)
  → interfaz de repositorio, lector o integración (Application/Interfaces)
  → implementación (Infrastructure)
  → ApplicationDbContext, Identity, Meta, SMTP u otro proveedor
```

El controller traduce HTTP a un pedido del servicio y su resultado a HTTP. El servicio coordina el caso de uso. El repositorio escribe o recupera un agregado; un lector especializado resuelve consultas de lectura, incluidas proyecciones, filtros y paginado. `ApplicationDbContext` permanece dentro de `Infrastructure`.

Se pueden separar operaciones de escritura y lectura como en CQRS **sin exigir** comandos, consultas, handlers, bus ni `IRepository<T>` genérico. Los nombres y carpetas deben hacer visible el recorrido anterior. Un servicio agrupa operaciones de una responsabilidad coherente; no se crea un servicio por endpoint ni un repositorio artificial por cada clase.

## Proyectos y dependencias

| Proyecto | Contiene | Puede depender de |
|---|---|---|
| `ArquitecturaBase.Domain` | Entidades, value objects, reglas y errores del modelo. Sin eventos de dominio ([ADR 0003](../decisions/0003-sin-eventos-de-dominio.md)) | BCL |
| `ArquitecturaBase.Application` | Interfaces consumidas por los casos de uso, servicios, modelos, validación y configuración funcional | `Domain`, abstracciones de Microsoft y FluentValidation; sin EF ni ASP.NET Core |
| `ArquitecturaBase.Infrastructure` | EF Core, `ApplicationDbContext`, repositorios, lectores, Identity, OpenIddict, correo, Meta y workers técnicos | `Application`, `Domain` |
| `ArquitecturaBase.Api` | Controllers, contratos HTTP, autorización, cookies, redirecciones, errores, configuración del host | `Application`; `Infrastructure` solo desde la raíz de composición |
| `ArquitecturaBase.AppHost` | Recursos y orquestación Aspire | `Api` |
| `ArquitecturaBase.ServiceDefaults` | Configuración técnica compartida de Aspire | Sin referencias a las capas de negocio |

No se agregan proyectos `Interfaces` ni `Repositories`. Son carpetas en los proyectos existentes. `Domain` no conoce a las otras capas. `Application` no conoce tipos de `Infrastructure` ni de `Api`; `Infrastructure` no conoce `Api`.

## Árbol de destino

El árbol indica la **ubicación de responsabilidades**, no obliga a crear archivos vacíos. Los nombres de clases indicados son los acordados para el alcance actual; una funcionalidad futura usa la misma regla de ubicación.

```text
ArquitecturaBase/
├─ src/
│  ├─ ArquitecturaBase.Api/
│  │  ├─ Program.cs                         # composición y pipeline HTTP
│  │  ├─ DependencyInjection.cs
│  │  ├─ Controllers/
│  │  │  ├─ SettingsController.cs
│  │  │  ├─ UsersController.cs
│  │  │  ├─ MeController.cs
│  │  │  ├─ RolesController.cs
│  │  │  ├─ PermissionsController.cs
│  │  │  ├─ LoginMethodsController.cs
│  │  │  ├─ LoginCodeController.cs
│  │  │  ├─ LoginLinkController.cs
│  │  │  ├─ ExternalLoginController.cs
│  │  │  └─ ConnectController.cs
│  │  ├─ Contracts/<Área>/                 # entradas HTTP (*HttpRequest, *Query)
│  │  ├─ Authentication/                   # adaptación HTTP/OpenIddict
│  │  ├─ Authorization/                    # [HasPermission] y políticas de permisos
│  │  ├─ ErrorHandling/                    # Result → ProblemDetails/IActionResult
│  │  ├─ RequestContext/                   # adaptadores de HttpContext (CurrentUser, RequestInfo)
│  │  ├─ Hosting/
│  │  ├─ Json/
│  │  ├─ Localization/
│  │  ├─ OpenApi/                          # convención de errores y [ProducesProblem]
│  │  ├─ RateLimiting/
│  │  └─ Modules/WhatsApp/                 # WhatsAppApiRegistration; Controllers/ (el webhook, el código de ingreso y el número
│  │                                       # del perfil), Contracts/ y Routing/ (rutas condicionales)
│  │
│  ├─ ArquitecturaBase.Application/
│  │  ├─ DependencyInjection.cs
│  │  ├─ Interfaces/
│  │  │  ├─ Services/                      # IUserQueryService, IUserAdministrationService, IUserAccessService, IProfileService, etc.
│  │  │  ├─ Persistence/                   # I*Repository, I*Reader, IUnitOfWork
│  │  │  ├─ Integrations/                  # Identity/, Security/, Emails/, Request/, Phones/, Caching/
│  │  │  └─ Channels/                      # puertos hacia un módulo opcional (IPhoneChannel, IInvitationChannel,
│  │  │                                    # IInvitationDeliveryStatusSource, IPhoneLinkParticipant)
│  │  ├─ Channels/                         # la versión del núcleo, sin módulo (DisabledPhoneChannel, EmailInvitationChannel)
│  │  ├─ Services/
│  │  │  ├─ Settings/                      # SystemSettingsService
│  │  │  ├─ Users/                         # UserQueryService, UserAdministrationService, UserAccessService, Profile*Service y helpers
│  │  │  ├─ Roles/                         # RoleService
│  │  │  └─ Auth/                          # LoginMethodsService, LoginCodeService, LoginLinkService, ExternalLoginService y helpers
│  │  ├─ Models/<Área>/                    # *Request, *Response y *Row (ver "Modelos: Response y Row")
│  │  ├─ Validation/<Área>/                # validadores FluentValidation
│  │  ├─ Configuration/
│  │  ├─ Common/
│  │  ├─ Resources/
│  │  └─ Modules/WhatsApp/                 # WhatsAppApplicationRegistration; Channels/ (los adaptadores de los puertos:
│  │                                       # WhatsAppPhoneChannel, WhatsAppInvitationChannel,
│  │                                       # WhatsAppInvitationDeliveryStatusSource y WhatsAppPhoneLinkParticipant),
│  │                                       # Services/ (el bot, el webhook y
│  │                                       # la entrega), Interfaces/, Models/, Validation/, Configuration/ y
│  │                                       # Resources/ (Bot.resx)
│  │
│  ├─ ArquitecturaBase.Domain/
│  │  ├─ Authentication/
│  │  ├─ Authorization/
│  │  ├─ Common/                           # Entity (Id Guid v7), ValueObject, IAuditable, ISoftDeletable
│  │  ├─ Results/
│  │  ├─ Settings/
│  │  ├─ Users/
│  │  ├─ ValueObjects/
│  │  └─ Modules/WhatsApp/                 # entidades y errores del módulo
│  │
│  ├─ ArquitecturaBase.Infrastructure/
│  │  ├─ DependencyInjection.cs
│  │  ├─ Persistence/
│  │  │  ├─ PersistenceRegistration.cs    # el contexto, los repositorios, los lectores y los seeders
│  │  │  ├─ ApplicationDbContext.cs
│  │  │  ├─ UnitOfWork.cs
│  │  │  ├─ Repositories/                 # escrituras y agregados
│  │  │  ├─ Readers/                      # lecturas especializadas
│  │  │  ├─ Configurations/
│  │  │  ├─ Extensions/
│  │  │  ├─ Interceptors/
│  │  │  ├─ Seed/
│  │  │  └─ Migrations/
│  │  ├─ Caching/                         # HybridCacheExtensions y SystemSettingsCache
│  │  ├─ Identity/                        # adaptadores Identity y OpenIddict
│  │  ├─ Emails/
│  │  ├─ Phones/
│  │  ├─ Security/
│  │  ├─ Settings/                        # opciones técnicas de registro
│  │  └─ Modules/WhatsApp/                # cliente Meta, workers técnicos, su registro y su persistencia (configuraciones,
│  │                                      # repositorios, lectores y WhatsAppLockKeys)
│  │
│  ├─ ArquitecturaBase.AppHost/
│  └─ ArquitecturaBase.ServiceDefaults/
└─ tests/                                  # cada proyecto, con los tests del módulo en su Modules/WhatsApp
   ├─ ArquitecturaBase.Api.IntegrationTests/
   ├─ ArquitecturaBase.Application.UnitTests/
   ├─ ArquitecturaBase.ArchitectureTests/
   └─ ArquitecturaBase.Domain.UnitTests/
```

`Application/Interfaces/Services` contiene `ISystemSettingsService`, `IUserQueryService`, `IUserAdministrationService`, `IUserAccessService`, `IProfileQueryService`, `IProfileService`, `IRoleService`, `ILoginMethodsService`, `ILoginCodeService`, `ILoginLinkService`, `IExternalLoginService` e `IConnectService` (el usuario activo y el cierre de sesión del endpoint de OpenIddict): los doce contratos de la carpeta. Los seis del módulo WhatsApp, `IWhatsAppLoginCodeService` (el pedido de un código para entrar por WhatsApp), `IProfileWhatsAppService`, `IWhatsAppWebhookService`, `IWhatsAppWebhookPersistence` (guarda un lote del webhook en su propia transacción), `IWhatsAppInboundService` e `IWhatsAppDeliveryService`, están en `Application/Modules/WhatsApp/Interfaces/Services`. Las implementaciones se ubican por área en `Application/Services` (las del módulo, en `Application/Modules/WhatsApp/Services`). Los contratos concretos de persistencia y proveedores van en las otras dos carpetas de `Interfaces`, con implementaciones en `Infrastructure`.

## Reglas de ubicación y acceso

1. **Api:** cada ruta HTTP de negocio se implementa como acción de un controller MVC. El controller recibe servicios por sus interfaces; no inyecta `ApplicationDbContext`, `UserManager`, `RoleManager`, `IQueryable`, repositorios ni lectores. El binding, la autorización HTTP, `Challenge`, cookies, redirecciones, cuerpo crudo del webhook y conversión de `Result` a HTTP pertenecen a `Api`.
2. **Application:** los servicios coordinan reglas, validadores, transacciones, repositorios/lectores y puertos externos. No referencian EF Core, Npgsql, `HttpContext`, `IActionResult` ni entidades de Identity de Infrastructure. Sus contratos no exponen `IQueryable`, `ApplicationUser` ni `ApplicationRole`; retornan modelos o resultados propios de Application/Domain.
3. **Domain:** alberga el modelo puro, incluidas interfaces que son parte del modelo como `IAuditable` e `ISoftDeletable`. Los contratos de persistencia que consumen servicios viven en `Application/Interfaces/Persistence`, no junto a entidades de `Domain`.
4. **Infrastructure:** las consultas y escrituras EF de negocio viven detrás de repositorios o lectores especializados. `SignInService` (lo técnico del ingreso: el bloqueo, la cookie y el cierre de sesiones) usa Identity y OpenIddict sin consultas de negocio: los datos de cuentas van por `IUserReader` e `IUserRepository`. `PermissionService` no usa Identity ni OpenIddict: lee por `IPermissionReader` y cachea con `HybridCacheExtensions`. `UnitOfWork`, migraciones, seeders, health checks, interceptores y stores de Identity/OpenIddict/DataProtection pueden usar directamente `ApplicationDbContext` como infraestructura técnica. Un contrato que solo usa Infrastructure, como el cliente interno de Meta, puede permanecer privado allí.
5. **DI:** `Api` registra MVC y componentes HTTP; `Application` registra las interfaces y servicios de casos de uso y validación; `Infrastructure` registra `ApplicationDbContext`, interfaces de persistencia, adaptadores y workers. Las dependencias se resuelven por DI explícita y con lifetimes compatibles con `DbContext` (normalmente scoped). `Program.cs` compone las capas y, en un bloque aparte, cada [módulo opcional](#módulos-opcionales), que registra lo suyo con un registro por capa (el núcleo no lo registra ni lo nombra). Evitar service locator en lógica de negocio.
6. **Servicios de fondo:** un worker de Infrastructure puede manejar programación, scopes, señalización y proveedores externos; invoca un servicio de Application para el caso de uso. La retención de mensajes conserva el scheduler técnico en Infrastructure, pero encapsula la operación EF de negocio en un repositorio especializado. El reintento del webhook que requiere un scope nuevo se implementa mediante un adaptador de Infrastructure.

## Módulos opcionales

Un módulo de producto que un proyecto derivado puede quitar ([ADR 0007](../decisions/0007-whatsapp-como-modulo-opcional.md); WhatsApp se muda a esta forma en la Etapa 6 del [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md)) vive en una carpeta `Modules/<Módulo>` dentro de los mismos proyectos, con el namespace de la carpeta: `ArquitecturaBase.<Capa>.Modules.<Módulo>.…`. No hay proyectos aparte: la regla de capas es la misma, y los tests de arquitectura comparan el namespace canónico, sin el segmento del módulo (`ModuleNamespaces.Canonical`), así un controller, un servicio, un helper, un repositorio o un lector del módulo cumple las mismas reglas que uno del núcleo.

- **La frontera:** el núcleo no nombra un módulo, ni en el IL ni en el fuente (`using`, `cref`, `nameof`, constantes, un `global using` del `.csproj`), y un módulo no nombra a otro; la única excepción es `Program.cs`, que los compone. `ApplicationDbContext` no expone entidades de un módulo: el módulo usa `dbContext.Set<T>()`. Lo verifica `ModuleBoundaryTests`, que lee el fuente, porque una constante, un `cref` o un `nameof` no dejan rastro en el IL y rompen el build al borrar el módulo. No lee lo que genera EF en las migraciones (el snapshot y los `.Designer.cs`): ahí cada entidad se nombra con una cadena, que compila sin el módulo.
- **La composición:** un módulo tiene un registro por cada capa en la que tiene tipos, salvo Domain: una clase `public static` `<M><Capa>Registration` en la raíz de su carpeta, con una extensión `Add<M><Capa>(this IServiceCollection)` que devuelve la colección, y `Program.cs` las llama en un bloque propio, después de las del núcleo (hoy, `AddWhatsAppApplication()`, `AddWhatsAppInfrastructure(builder.Configuration)` y `AddWhatsAppApi()`, con sus tres `using` marcados). Cada uno registra todo lo suyo, también lo que antes vivía en el registro del núcleo: opciones, servicios, validadores (`AddApplication` deja afuera los de `Application.Modules.*`), políticas de rate limit y convenciones de MVC. Los repositorios y lectores del módulo los registra su `<M>InfrastructureRegistration`, no `PersistenceRegistration`. Las opciones y las convenciones se componen con `Configure`, así que el orden entre el núcleo y el módulo no cambia el resultado. También lo verifica `ModuleBoundaryTests`.
- **Los puertos:** donde el núcleo necesita algo que un módulo puede dar, define un puerto en `Application/Interfaces/Channels` y, si tiene una, trae su versión en `Application/Channels`. Hay tres formas. Si hay uno solo (el canal telefónico, `IPhoneChannel`), el núcleo trae el apagado (`DisabledPhoneChannel`) con `TryAdd` y el módulo lo reemplaza con `Replace`. Si hay uno por canal (`IInvitationChannel`, por dónde sale una invitación, e `IInvitationDeliveryStatusSource`, quién sigue su entrega), cada uno se suma con `TryAddEnumerable`: el núcleo trae lo suyo si lo tiene (el correo, `EmailInvitationChannel`; fuente de estado no trae ninguna, porque el correo no sigue la entrega) y el módulo, lo suyo; quien los usa elige por el canal, y dos del mismo canal son un error de registro. Si el núcleo avisa a todos los que haya (los participantes del número, `IPhoneLinkParticipant`: lo que un módulo ata al número de una cuenta), cada módulo suma el suyo con `TryAddEnumerable` y el núcleo no trae ninguno; `PhoneNumberLinker` los llama a todos, en el orden en que se registraron, y toma sus locks antes que el de la cuenta. En los tres casos registrar dos veces no duplica nada, y en los dos primeros el orden de los registros no cambia el resultado. El adaptador del módulo vive en `Modules/<M>/Channels`, fuera de `Services`. Sin el módulo, el núcleo funciona con lo suyo: sin canal telefónico no se ofrece el ingreso por teléfono y no hay regla de país para un número nuevo; sin canal de WhatsApp, invitar por WhatsApp responde que no está disponible, y el detalle no tiene fuente que siga la entrega; sin participantes, el número es solo un dato de la cuenta y un cambio de número toma solo el lock de la cuenta.
- **Dónde va cada cosa:** lo mismo que en el núcleo, adentro de `Modules/<M>`. Los errores del módulo van en la raíz de `Domain/Modules/<M>/` (el módulo es el área) y sus helpers en `Application/Modules/<M>/Services`.
- **Los tests:** los del núcleo no nombran un módulo, y sus casos de control usan módulos inventados (`Control`, `Sms`). Donde un test del núcleo necesita algo del módulo (la configuración del arnés, las rutas del inventario, los registros, las claves de lock), la clase es `partial` y declara un gancho `partial void`; el módulo lo implementa en otra parte de la misma clase, en `tests/<Proyecto>/Modules/<M>/`, con el namespace de la clase del núcleo (una excepción a "namespace = carpeta", como los archivos de casos de control, que declaran tipos en namespaces inventados: `IDE0130` no está activo). Sin la carpeta del módulo, el compilador borra las llamadas. Tienen ganchos `ApiFactory`, `ExplicitRouteInventoryTests`, `DependencyInjectionTests`, `ApplicationHelpersTests` y `TransactionBoundaryTests`; `IdentityBoundaryTests` es `partial` para que el módulo sume sus propias reglas.

## Borde HTTP

Cómo escribe una acción de controller su entrada, su autorización, su respuesta y su documentación. Rige para las rutas de negocio; `ConnectController`, `ExternalLoginController` y `WhatsAppWebhookController` hablan su propio protocolo (OpenIddict, navegación del navegador y Meta), llevan `[OwnProtocol]` y quedan fuera de la convención de OpenAPI.

1. **Entrada ([ADR 0002](../decisions/0002-contratos-http.md)):** todo parámetro de body o de query, incluido el cuerpo que `[ApiController]` infiere para un tipo complejo sin atributo, es un contrato de `Api/Contracts/<Área>` (`*HttpRequest`, o `*Query` para una query) o un valor simple (primitivo, enum, `string`, `Guid`, fecha u hora, o su versión nulable); una colección en la query necesita un contrato. Lo verifica `ControllerInputContractTests`. El controller mapea el contrato a mano al modelo de Application. Las respuestas no llevan contrato propio: serializan los `*Response` de Application, así que renombrar una propiedad de un `*Response` cambia el contrato HTTP. Un contrato con correo, número, código, token o datos del perfil sobrescribe `ToString()`, porque MVC registra los argumentos de la acción; los modelos de Application no lo hacen. Los parámetros de ruta, header y servicios quedan fuera de la regla.
2. **Autorización:** una ruta pide un permiso con `[HasPermission(Permissions.<Área>.<Acción>)]` (`Api/Authorization/HasPermissionAttribute.cs`) en el controller o en la acción, nunca con roles (`[Authorize(Roles = …)]`) ni con `[Authorize(Policy = …)]` armado a mano; lo verifica `PermissionAuthorizationTests`, con un caso de control para cada detector. Sin sesión responde 401 y sin el permiso, 403. Un `[Authorize]` sin permiso solo pide sesión.
3. **Resultado:** el controller convierte el `Result` con las extensiones de `ControllerResultExtensions`:
   - `ToActionResult`: 200 con el valor, o 204 para un `Result` sin valor;
   - `ToAcceptedResult`: 202, con el valor en el cuerpo si lo hay y nunca con `Location`;
   - `ToCreatedResult(this, nameof(Get), id => new { id })`: 201 con el valor y el `Location` de la acción que devuelve el recurso (`CreatedAtAction`). Esa acción tiene que existir en el mismo controller: si la ruta no se puede armar, MVC lanza una excepción en lugar de responder un 201 sin `Location`.

   No se escribe a mano `Accepted(...)`, `StatusCode(202)` ni `IsSuccess ? … : …`. Por dentro, las tres arman el error con `ProblemDetailsMapper.FromError(error, factory, httpContext)` y el `ProblemDetailsFactory` de MVC, así lleva el mismo `type` y pasa por el mismo `CustomizeProblemDetails` que los errores del framework. Es la única forma de convertir un `Error`: no hay una sobrecarga sin la fábrica, y `ProblemDetailsMapperTests` la prueba con la fábrica de `AddControllers()` y un `DefaultHttpContext`, sin Docker.
4. **ProblemDetails y JSON:** el `traceId` se calcula en un solo lugar, `ProblemDetailsMapper.AddTraceId` (la actividad actual o, sin ella, el `TraceIdentifier` del pedido). Las opciones JSON de MVC y de `Http.Json` salen de un solo `ConfigureJson` (`Api/DependencyInjection.cs`); un conversor nuevo va ahí, y `JsonOptionsTests` verifica que las dos sigan escribiendo y leyendo igual. El 400 de un cuerpo ilegible o de un parámetro que no se puede convertir (`Request.Invalid`) no trae `errors` a propósito: el ModelState nombraría rutas JSON, propiedades y tipos .NET, que son la forma interna del modelo. Los `errors` por campo salen solo de los validadores de Application.
5. **OpenAPI:** cada acción declara su éxito con `[ProducesResponseType<T>(status)]`, o `[ProducesResponseType(StatusCodes.Status204NoContent)]`. Una acción sin esa declaración aparece en el documento solo con sus errores, porque ApiExplorer deja de suponer el 200 en cuanto hay alguna respuesta declarada. Los errores, todos como ProblemDetails, los declara `ProblemResponsesConvention` (`Api/OpenApi`) a partir de la firma: 500 siempre; 400 si recibe body o query; 401 si pide sesión y no es `[AllowAnonymous]`; 403 solo si además pide un permiso; 404 si la ruta tiene un parámetro como `{id}`; 429 si tiene `[EnableRateLimiting]` en el controller o en la acción (sin un `[DisableRateLimiting]` más cercano). Lo que la firma no muestra se declara con `[ProducesProblem(status)]`, solo si el servicio de verdad lo responde (el status sale de `ErrorType` en `ProblemDetailsMapper.ToStatusCode`): el 404 de `/api/me`; el 409 de un `Error.Conflict` (el correo o el número de otra cuenta, la propia cuenta o el último administrador, el único medio de ingreso, un rol con el mismo nombre o con usuarios); el 403 de una acción anónima que rechaza la cuenta (`Auth.Account.Disabled` y `NotInvited` en el verify del código y en el canje del enlace); y el 429 de un servicio sin rate limit (la espera entre invitaciones). Un controller nuevo con protocolo propio lleva `[OwnProtocol]` (`Api/OpenApi/OwnProtocolAttribute.cs`) y declara a mano sus respuestas; es un atributo y no una lista, así la convención no nombra los controllers de un módulo. `OpenApiTests` falla si una operación de `/api` no declara un 2xx con esquema (salvo un 204 o un 202 sin cuerpo que figure en su lista), no declara el 500 o declara un error que no es ProblemDetails.

## Errores del framework, pipeline y JSON

- Los errores que arma el propio framework (ruta inexistente, 405, 401/403 de la autorización) también salen como ProblemDetails (`ProblemDetailsMapper.CompleteFrameworkProblem` + `UseStatusCodePages`). Sus códigos están en `ApiErrorCodes`: `Http.*` por status, `General.Unexpected` para los 5xx y `Request.Invalid` para el resto de los 4xx. El 429 del rate limiter es distinto: `RateLimitingExtensions` arma su propio ProblemDetails con `retryAfter`; `UseStatusCodePages` solo completa la respuesta (en texto plano) cuando el cliente no acepta JSON.
- Todo middleware que pueda cortar con un error va en `Program.cs` después de `UseStatusCodePages`, o su respuesta sale vacía: `UseAuthentication`/`UseAuthorization` se declaran explícitos (no hay que dejar que `WebApplication` los agregue solo) y `UseRateLimiter` ya está después de `UseStatusCodePages`.
- Los enums que viajan en una respuesta lo hacen **por su nombre**, no por su número (`JsonStringEnumConverter` en `Api/DependencyInjection.cs`): el número no dice nada del otro lado y reordenar el enum cambiaría en silencio lo que significa cada valor guardado.

## Validación, guardado y errores

- FluentValidation valida los modelos de entrada de Application **antes** de ejecutar cambios. El registro DI debe resolver todos los validadores aplicables. Un servicio o helper recibe un solo `IRequestValidator` (`Application/Common/Validation`, scoped), que al validar resuelve del scope los `IValidator<T>` del tipo **estático** del pedido: un pedido pasado como un tipo base o como `object` no correría ningún validador. Un pedido inválido no abre transacción ni llega a repositorios.
- Los errores se declaran en clases `public static` `<Entidad>Errors` que viven en `Domain/<Área>/` (no en `Common`, `Results` ni `ValueObjects`), al lado de la entidad o del área que define los errores, aunque solo las use Application: `WhatsAppErrors` está en `Domain/Modules/WhatsApp/` y `RoleErrors`, con códigos `Roles.Role.*`, en `Authorization`. El prefijo del código no tiene que coincidir con la carpeta, y el código no cambia si la clase se muda (el front usa `Auth.WhatsApp.CountryNotSupported`). Nadie fuera de Domain llama a las fábricas `Error.Failure`, `Validation`, `Unauthorized`, `Forbidden`, `NotFound`, `Conflict` ni `TooManyRequests`, ni al constructor de `Error` (es público, porque es un record) ni a una copia con `with`. De `ValidationError` se permite solo lo que se usa: el constructor de un argumento (los mensajes por campo) en `RequestValidator`, `FieldErrors` y `ExternalLoginController`, y el de tres (código, descripción y mensajes, una regla de negocio atada a un campo) únicamente en `FieldErrors`; un `with` sobre un `ValidationError` no. `ErrorDeclarationTests` verifica las dos cosas. En un [módulo opcional](#módulos-opcionales), los errores van en la raíz de `Domain/Modules/<M>/`: el módulo es el área.
- Los servicios devuelven `Result` o `Result<T>` para errores de negocio. El borde HTTP conserva los códigos y mensajes traducidos de `ProblemDetails`, incluidos `code`, `traceId` y los errores por campo.
- Una escritura define su límite con `IUnitOfWork.ExecuteInTransactionAsync(trabajo, CommitPolicy, ct)`, la única forma de guardar; una consulta no abre límite. Cuándo se usa `OnSuccess` y cuándo `OnAnyResult` (los casos que **deben guardar también al devolver error**), los locks y los guardados de `UserManager` y `RoleManager` están en [Una sola forma de guardar](#una-sola-forma-de-guardar). No se aplica un guardado uniforme por convención.
- El logging operativo conserva inicio, resultado y código de error sin escribir códigos de ingreso, tokens ni secretos. Un método público de un servicio que devuelve `Result` o `Result<T>` lo registra con `OperationLog.RunAsync(logger, "Operación", () => …)` (`Application/Common/Logging/OperationLog.cs`), que envuelve el cuerpo entero (validar, abrir el límite, la cookie o el caché): registra "Handling {Operation}" antes, "Handled {Operation}" si el resultado fue exitoso o "{Operation} failed with {ErrorCode}" (`Warning`) si no, y nunca el pedido. No atrapa excepciones: si el trabajo lanza, el log queda solo con "Handling". `WhatsAppWebhookService`, que devuelve `bool`, llama directo a `OperationLog.Handling`/`Handled`. Ningún otro tipo declara su propio `[LoggerMessage]` con esos textos; lo verifica `OperationLoggingTests`.

## Una sola forma de guardar

- `IUnitOfWork.ExecuteInTransactionAsync(trabajo, CommitPolicy, ct)` es la única forma de guardar, una vez por cada método público de un servicio que escribe (la decisión es el [ADR 0001](../decisions/0001-transaccion-explicita-por-caso-de-uso.md); el diseño y la tabla de cada método, el [plan de la Etapa 1](../history/plans/2026-09-26-etapa-1-una-sola-forma-de-guardar.md)). `TransactionBoundaryTests` verifica quién puede abrir el límite: solo un punto de entrada (una clase de `Application/Services` que implementa un contrato de `Interfaces/Services`) recibe `IUnitOfWork` y llama a `ExecuteInTransactionAsync`, con una sola excepción con nombre, el seed de arranque (más abajo), y solo `UnitOfWork` guarda el contexto. No verifica que cada método que escribe abra su límite ni que abra uno solo: que las escrituras queden adentro lo aseguran las que exigen la transacción (los locks, `IUserRepository`, `IRoleRepository` y las escrituras de `ISignInService`, más abajo), y que haya uno solo por método, la revisión. El ejemplo a copiar es `RoleService.UpdateAsync` con su `UpdateCoreAsync`. El patrón, en cinco reglas:
  1. afuera y antes: `ThrowIfNull`, el log de inicio y el validador del pedido (un pedido inválido no abre transacción);
  2. un solo límite con la política escrita: `OnSuccess`, u `OnAnyResult` con un comentario que diga qué queda registrado cuando falla;
  3. adentro, en este orden: los locks (`login-code:` del correo y del número en dos llamadas, los de los participantes del número (`IPhoneLinkParticipant`; con WhatsApp, las filas de los contactos), `login-link:` o `user-invitation:`, y al final, solo cuando se va un administrador activo, `users:admins`), las lecturas de lo que se va a modificar, las reglas, las escrituras y los efectos que tienen que quedar marcados en la fila (encolar y `MarkSent`);
  4. afuera, después y solo si se confirmó: invalidar caché, la cookie de la aplicación (`ISignInService.SignInAsync`, que lanza adentro de un límite), `Notify` y el log de resultado. El servicio que abre el límite nunca lo envuelve en un try/catch. Lo atrapan desde afuera solo quienes corren el límite de otro como una unidad: `WhatsAppWebhookService`, que ante un 23505 reintenta una vez en un scope nuevo, y los hosts en segundo plano, que atrapan por unidad para que un fallo no corte la vuelta (un contacto en `WhatsAppInboundProcessor`, un registro de envío en `WhatsAppSenderBackgroundService`);
  5. los helpers ([Convención de sufijos de los helpers](#convención-de-sufijos-de-los-helpers)) nunca reciben `IUnitOfWork` ni guardan, y un servicio nunca llama al método de escritura de otro: anidar lanza.

  Una excepción, que no es el ejemplo a copiar: `WhatsAppWebhookService` llama a otro servicio, `WhatsAppWebhookPersistence`, que es el que abre el límite, porque esa es la unidad que se reintenta: `WhatsAppWebhookRetry` la vuelve a correr en un scope nuevo. Su trabajo devuelve un `Result` que siempre es un éxito, solo para llevar los contadores, porque el límite pide un `Result`.
- Para poner en fila operaciones sobre un mismo recurso (los códigos de un destino, los enlaces de una cuenta, los contactos de un número), el repositorio toma un lock de Postgres (`pg_advisory_xact_lock` con la clave de `AdvisoryLockKeys`, o un lock de fila `FOR NO KEY UPDATE`) dentro de la transacción del caso de uso: los locks la exigen (sin ella lanzan `InvalidOperationException`, también con cero claves) y duran lo que ella. La abre solo `IUnitOfWork.ExecuteInTransactionAsync`, en READ COMMITTED; no se sube el aislamiento, porque leer la cuenta después del lock necesita ver lo que el otro acaba de confirmar. No hay `EnableRetryOnFailure`: si alguna vez se activa (por ejemplo, con `AddNpgsqlDbContext` de Aspire), `BeginTransactionAsync` lanza, y no se arregla envolviendo el trabajo en la estrategia de ejecución, porque el trabajo encola correos y mensajes y no se puede repetir.
- Las claves de `pg_advisory_xact_lock` del núcleo están todas en `AdvisoryLockKeys` (`login-code:`, `login-link:`, `user-invitation:`, `external-login:`, `users:admins` y `seed:database`), y las de un módulo, en el suyo (las de WhatsApp, en [`whatsapp.md`](../features/whatsapp.md)): el texto es el lock, y cambiarlo deja de poner en fila a quien use el viejo. Dentro de una llamada se toman ordenadas y sin repetir (`AdvisoryLockExtensions`); entre llamadas el orden lo fija quien llama: `login-code:` del correo y después el del número, en dos llamadas; los de los participantes del número antes que la cuenta (`PhoneNumberLinker.LockAsync`); `users:admins`, último. Es el único lock global de los casos de uso: lo toma `UserGuard` (`IUserRepository.LockAdminsAsync`) justo antes de contar los administradores activos, solo cuando se desactiva, se elimina o se le saca el rol `Admin` a uno activo, y después de los locks de los participantes y de cuenta que ya tomó el caso de uso; nadie toma otro lock después, así que no arma ciclos. Pone en fila ese conteo: sin él, dos administradores que se desactivan entre sí a la vez cuentan dos cada uno y el sistema queda sin ninguno ([`administracion.md`](../features/administracion.md)). Un prefijo nuevo va también en `LockKeyPrefixes` de `TransactionBoundaryTests` (el de un módulo, en el gancho `AddModuleLockKeyPrefixes` de su parte de la clase; ver [Tests](#tests-arquitectura-y-arnés)). Por qué los participantes van antes que la cuenta (con WhatsApp, los contactos del bot), y por qué el que pierde cuando chocan es el bot, está en [`docs/features/whatsapp.md`](../features/whatsapp.md).
- `UserManager` y `RoleManager` siguen autoguardando (`AutoSaveChanges` no se toca), pero adentro de la transacción del caso de uso y con un savepoint por guardado: un 23505 que traduce el repositorio se puede atrapar y la transacción sigue usable. Un error de SQL crudo (55P03, 40P01) nunca se atrapa adentro.
- Las escrituras de cuentas y de roles exigen la transacción igual que los locks: todas las de `IUserRepository` y `IRoleRepository`, y las de `ISignInService` (los intentos fallidos y el cierre de sesiones). Fuera de un límite lanzan `InvalidOperationException` antes de tocar nada, también cuando un test prepara datos. Los roles se escriben solo por `IRoleRepository`.
- Quedan fuera a propósito: la retención de mensajes (`ExecuteUpdate`), `ConnectService.RevokeAuthorizationAsync` (un UPDATE de OpenIddict), las migraciones, el servidor OpenIddict y Data Protection.
- **El seed de arranque** (`SeedExtensions.SeedDatabaseAsync`) corre en un scope propio con `DatabaseSeeder` (`Infrastructure/Persistence/Seed`), la única excepción con nombre a "solo un punto de entrada abre el límite" ([ADR 0001](../decisions/0001-transaccion-explicita-por-caso-de-uso.md), enmienda del 2026-09-28). Abre un solo límite con `OnSuccess`, toma adentro el lock `seed:database` y corre los roles, los ajustes y OpenIddict, en ese orden; el trabajo devuelve `Result.Success()` porque el límite pide un `Result`, y los errores de los seeders siguen siendo excepciones: `UnitOfWork` deshace, vacía el tracker y relanza, y no queda nada a medias. Así dos réplicas que arrancan juntas siembran una después de la otra, y la segunda ve lo que confirmó la primera. Adentro, `RoleManager` y el `UserManager` del administrador autoguardan con un savepoint por guardado, los managers de OpenIddict guardan sobre el mismo contexto sin abrir transacción propia (verificado en 7.7.1: ver la tarea 4 de la Etapa 7 del [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md)) y `SystemSettingsSeeder` solo agrega la fila, que baja con el guardado siguiente o con el final del límite. Nada del seed llama a `SaveChanges` por su cuenta. Lo prueban `DatabaseSeederTests` (con Docker): el seed espera el lock tomado desde otra conexión, dos seeds en paralelo dejan una fila de cada cosa y un seed que falla no deja nada.
- Toda fábrica de `HybridCache` lee en su propio scope, con `HybridCacheExtensions.GetOrCreateInOwnScopeAsync`, que es la única forma de llenar el caché (lo verifica `TransactionBoundaryTests`, que ve `GetOrCreateAsync` y `SetAsync`): sobre el contexto de quien llama, adentro de un límite, vería lo que todavía no se confirmó, lo cachearía y le ocuparía la conexión que el rollback necesita para soltar los locks. `HybridCache.SetAsync` no se usa: guardaría un valor que calculó quien llama, quizás adentro de su límite, con el mismo riesgo. Para que un cambio valga al instante se descarta la clave (`RemoveAsync`) y la próxima lectura la vuelve a llenar. Por eso `PermissionService` y `SystemSettingsReader` se pueden llamar adentro de un límite. El precio es que, con el caché frío, la fábrica pide una segunda conexión al pool mientras la del límite sigue tomada. Con una fábrica por clave (los ajustes, un minuto; los permisos de cada rol, una hora) alcanza; una fábrica por fila o por pedido puede agotar el pool.

## Colas en memoria

El correo y WhatsApp salen en segundo plano, por dos colas con la misma forma: `IEmailQueue` (`EmailQueue`, que vacía `EmailBackgroundService`) e `IWhatsAppSendQueue` (`WhatsAppSendQueue`, que vacía `WhatsAppSenderBackgroundService`). Son un `Channel` acotado en memoria, no un outbox transaccional:

- **La cola nunca espera.** `TryEnqueue` devuelve `false` si el mensaje no entró (la cola está llena o, en WhatsApp, está apagado) y deja un Warning sin destinatario ni contenido. Se encola adentro del límite, con los locks tomados: esperar al SMTP o a Meta los dejaría tomados.
- **Un reinicio pierde lo encolado.** Lo que estaba en la cola y no salió no se recupera: la persona pide otro código, o el admin reenvía la invitación.
- **Un commit fallido deja salir el mensaje.** Se encola antes del commit para que la fila se confirme ya marcada; si el commit falla, el mensaje sale igual, con un código que no sirve.
- **La capacidad** es `Email:QueueCapacity` y `WhatsApp:QueueCapacity`, entre 1 y 10.000 y 100 por defecto.

Qué hace cada llamador con un `false`:

| Llamador | Con la cola llena |
|---|---|
| Código por correo (`SignInCodeIssuer`, `DestinationCodeIssuer`) | el código queda sin `MarkSent` y el pedido responde igual (202) |
| Código por WhatsApp (`WhatsAppCodeIssuer`) | igual: sin `MarkSent`, así no consume la cuota diaria |
| Invitación por correo (`EmailInvitationChannel`) o por WhatsApp (`WhatsAppInvitationChannel`), los canales de `UserInvitationIssuer` | el canal la marca con `MarkSendFailed` y deja un log; el alta no se deshace, el detalle la muestra con `lastInvitation.deliveryStatus` en `Failed` (también por correo) y el reenvío no espera, porque la espera se cuenta desde la última invitación que salió |
| Respuesta del bot (`WhatsAppInboundService`) | lanza: el límite se deshace y el procesador reintenta los mensajes en la próxima vuelta |

## Convención de sufijos de los helpers

Un helper es una pieza interna de un área (`Application/Services/<Área>`) que no implementa ningún contrato de `Interfaces/Services`: se registra por su tipo concreto, nunca por interfaz (`services.AddScoped<UserGuard>()`, por ejemplo). Es siempre `internal sealed` y su nombre termina con uno de estos sufijos, según su rol:

| Sufijo | Para qué |
|---|---|
| `Policy` | decide |
| `Guard` | protege una regla |
| `Issuer` | emite |
| `Verifier` | verifica |
| `Linker` | vincula |
| `Revoker` | revoca |
| `Recorder` | registra |

`Operations` y `Change` no se usan: un helper nace ya con su sufijo. Los helpers de hoy: `AccountCreationPolicy` y `WhatsAppReplyPolicy` (Policy); `UserGuard` y `WhatsAppCodeQuotaGuard` (Guard); `DestinationCodeIssuer`, `LoginCodeIssuer`, `SignInCodeIssuer`, `LoginLinkIssuer`, `UserInvitationIssuer`, `WhatsAppCodeIssuer` y `WhatsAppLinkIssuer` (Issuer); `DestinationCodeVerifier`, `LoginCodeVerifier` y `LoginLinkVerifier` (Verifier); `PhoneNumberLinker`, `UserContactLinker` y `WhatsAppContactLinker` (Linker); `AccountAccessRevoker` (Revoker); `LoginAuditRecorder` (Recorder). Quedan afuera por construcción, porque no se registran ni como servicio ni como helper: los adaptadores de los puertos, que viven en `Channels` y se registran por su interfaz, los validadores de FluentValidation, `RequestValidator` (vive en `Common/Validation`), las opciones y los tipos que se crean con `new` o son estáticos (`BotReply`, `IssuedLoginCode`, `IssuedLoginLink`, `InvitationFields`, `UserCultures`). Lo verifica `ApplicationHelpersTests` (`tests/ArquitecturaBase.Application.UnitTests`), sobre los descriptores de `AddApplication()` más los del registro de cada módulo (el gancho `AddModules`, que en WhatsApp llama a `AddWhatsAppApplication()`), con dos casos de control: el filtro ve al menos un helper conocido (`AccountAccessRevoker`), y un tipo armado en memoria con un sufijo fuera de la tabla falla.

## Nombres de repositorios y lectores

La decisión es el [ADR 0008](../decisions/0008-nombres-de-repositorios-y-lectores.md). Rige para todo método de un contrato de `Application/Interfaces/Persistence`, salvo `IUnitOfWork`, y se decide por el tipo de retorno, que es lo que un test puede ver:

| Prefijo | Devuelve | Dónde |
|---|---|---|
| `Get…` | una entidad de Domain (hereda de `Entity`), siempre seguida por EF, para modificarla; a veces con lock de fila, y entonces su XML lo dice; `null` si no existe | solo en `*Repository` |
| `Find…` | una proyección, un registro o un escalar, nunca una entidad; `null` si no existe, o un valor por defecto documentado (`FindRegistrationModeAsync` devuelve `InviteOnly` si no hay fila) | lectores y repositorios |
| `List…` | una colección (no `string`) o `PagedResult<T>` | los dos |
| `Exists…` | `Task<bool>` | los dos |
| `Count…` | `Task<int>`, o un registro cuyo nombre termina en `Counts` (`UserFilterCounts`) | los dos |
| `Lock…` | `Task`: toma un lock de Postgres y exige la transacción | solo repositorios |
| `Add` | `void`: da de alta en el contexto, y lo baja el guardado final del límite | solo repositorios |
| `Create…`, `Update…`, `Delete…`, `Set…`, `Remove…`, `Restore…`, `Clear…`, `Add…Async` | escrituras; exigen la transacción | solo repositorios |

- Un lector (`I*Reader`) solo tiene `Find`, `List`, `Exists` y `Count`, y es el único que llama a `AsNoTracking`. Una entidad que devuelve un repositorio está siempre seguida: se puede modificar, y la baja el guardado final del límite.
- Quedan afuera `IUnitOfWork` (su único método lo fija `TransactionBoundaryTests`) y los contratos de `Interfaces/Integrations`, que no son de persistencia: `ISignInService.GetExternalLoginAsync` e `IPermissionService.GetPermissionsAsync` son operaciones técnicas.
- Un lector no descarta su caché: eso es de un contrato de `Interfaces/Integrations/Caching`, como `ISystemSettingsCache.InvalidateAsync`, que llama `SystemSettingsService` después del commit y solo si se confirmó; la clave (`SystemSettingsCache.CacheKey`) vive en la implementación del caché y el lector la lee de ahí. `IPermissionService.InvalidateRoleAsync` es el caché de permisos por rol y se queda en `Integrations/Identity`, con el resto del servicio de permisos. `IWhatsAppWebhookReader` es un parser del cuerpo del webhook, no un lector de base.
- Un `Count…` cuenta en SQL, sin traer entidades. `IUserReader.CountActiveAdminsAsync` es un `CountAsync` sobre `userManager.Users` con la semántica de `UserManager.GetUsersInRoleAsync` más `IsActive`: el filtro global de borrados, la unión `AspNetUserRoles` → `AspNetRoles` y el rol por `NormalizedName` normalizado con el `UserManager`, no por claim. Hasta la Etapa 7 traía a memoria, y dejaba seguidos, a todos los administradores; lo caracteriza `UserReaderTests.Admin_count_includes_only_active_and_not_deleted_accounts`.
- Lo verifica `PersistenceNamingTests` ([Tests](#tests-arquitectura-y-arnés)).

## Modelos: Response y Row

- **La salida de un servicio termina en `Response`.** Es el tipo de nivel superior que devuelve un método de `Interfaces/Services`, sin `Task`, `Result<T>`, `PagedResult<T>` ni la colección: `UserDetailResponse`, `UserListItemResponse`, `PermissionGroupResponse`, `ConnectUserResponse`. Los tipos anidados (`LastInvitation`, `PermissionItem`) y los escalares (`Guid`, `bool`) quedan afuera. La única excepción es `UserFilterCounts`, que devuelven igual el lector y el servicio y sigue el sufijo `*Counts` de los conteos ([Nombres de repositorios y lectores](#nombres-de-repositorios-y-lectores)). Lo verifica `ServiceOutputNamingTests`, con su caso de control.
- **Una fila de listado o de detalle que proyecta un lector termina en `Row`** (`UserListRow`, `UserDetailRow`, `RoleRow`, `UserInvitationRow`) y vive en `Models/<Área>`, sin subcarpeta. El servicio la traduce a su `Response`, que es lo que sale por HTTP (la última invitación, con la función pura `LastInvitation.From`): así la forma de la consulta puede cambiar sin tocar el contrato, y lo que el servicio suma (el número para mostrar, la última invitación) no queda como una propiedad que el lector nunca llena. Queda afuera `UserAccount` (`Models/Identity`): no es una fila de listado ni de detalle, sino la vista de la cuenta que devuelven los `Find*` de `IUserReader` y que usan casi todos los casos de uso para decidir. Esta regla no tiene test propio: `PersistenceNamingTests` mira el prefijo y el tipo de retorno, no el nombre del modelo.

## Migraciones

Desde la Fase 2, la Api necesita `Microsoft.EntityFrameworkCore.Design` (`PackageReference` con `PrivateAssets="all"`, versión en `Directory.Packages.props`). El comando `dotnet ef migrations add` pasa la cadena de conexión como argumento de la aplicación, porque la Api solo la recibe de Aspire; no se conecta a la base. El comando completo, cómo instalar `dotnet ef`, cómo comprobar sin Docker que no falta ninguna (`has-pending-model-changes`) y las trampas están en la [guía de la migración](../guides/migracion.md).

En desarrollo, la Api las aplica al iniciar.

- `MigrationsTests` falla si el modelo cambia y falta la migración.
- Las migraciones son código generado: `.editorconfig` las excluye del estilo.
- Cómo se aplican fuera de Development lo fija el [ADR 0006](../decisions/0006-migraciones-y-seed-fuera-de-development.md).

## Excepciones de protocolo y conservación funcional

`/connect/revoke`, `/connect/introspect` y discovery `/.well-known/*` son endpoints técnicos administrados por OpenIddict. Los endpoints técnicos de Aspire también permanecen con su framework. Las acciones propias de `/connect` usan controllers MVC y conservan passthrough, PKCE, cookies, formularios y redirecciones. Esto no habilita Minimal APIs para rutas de negocio nuevas.

El cambio de estructura **no cambia el producto**: se conservan las 41 combinaciones explícitas de verbo/ruta inventariadas en el plan, respuestas HTTP, autorización, rate limits, contratos JSON, OpenIddict, frontend y comportamiento de WhatsApp. Las cuatro combinaciones condicionales de WhatsApp deben seguir apareciendo o desapareciendo en el enrutamiento según configuración; responder 404 dentro de una ruta siempre registrada no equivale a omitirla. Los workers de entrada, envío y retención siguen operativos.

Cambios deliberados posteriores a la migración (2026-09-26, [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md), Etapa 3): `POST /api/users` responde `201` con `Location` a `/api/users/{id}` en lugar de `200`, con el mismo cuerpo, y el front trata cualquier 2xx como éxito; `POST /api/roles` también responde `201`, con `Location` a `GET /api/roles/{id}` (2026-09-28, Etapa 4). Después de la migración se agregaron `GET /api/roles/{id}` y `GET /api/roles/paged`, el listado paginado de roles (el catálogo completo sigue en `GET /api/roles`, ver la enmienda del [ADR 0004](../decisions/0004-roles-como-area-de-referencia.md)): las 41 de arriba son las de la migración, y el inventario de `ExplicitRouteInventoryTests` suma las nuevas. En el documento OpenAPI, los esquemas de los cuerpos llevan el nombre de su contrato (`UpdateProfileHttpRequest` en lugar de `UpdateProfileRequest`); el JSON no cambió.

La base es de desarrollo y puede recrearse. No se exige migrar datos ni preservar su historial; sí se exige que una base vacía arranque con esquema, seed y flujos principales funcionales.

## Front y hosting del SPA

El SPA vive en `../ArquitecturaBaseFront` (React + Vite). El AppHost lo levanta como un recurso más. La regla de la pantalla nueva está en [`AGENTS.md`](../../AGENTS.md), "Front"; la lista de prefijos de backend (`BackendPrefixes`) y los cuatro lugares que toca un prefijo nuevo, en la [guía del prefijo de backend](../guides/prefijo-de-backend.md).

- **Un solo origen.** El navegador habla siempre con una sola dirección: en desarrollo, `https://localhost:5173`, donde Vite sirve el SPA y reenvía `/api`, `/account`, `/connect`, `/signin-google`, `/.well-known` y `/webhooks` a la Api (`https://localhost:7180`); en producción, la Api sirve las dos cosas. No hay CORS y no se configura.
- **El issuer es el origen público, no el de la Api.** Se fija con `Authentication:Issuer` (en desarrollo, `https://localhost:5173/`). `SetIssuer` cambia solo el campo `issuer`: los demás endpoints del documento de discovery salen del `Host` del request, y por eso el proxy de Vite va con `changeOrigin: false`. Si alguna vez el front cambia de origen, hay que mover también las redirect URIs del cliente `web`.
- **El fallback del SPA no toca las rutas del backend.** `UseSpaFallback` (`Api/Hosting/SpaExtensions.cs`) es un middleware, no un `MapFallback`: solo atiende GET y HEAD que no matchearon ningún endpoint, que no parecen un archivo y que no empiezan con un prefijo de backend. Así las rutas inexistentes de la Api siguen devolviendo su ProblemDetails, y el 405 y el 415 que arma el routing no se los come un catch-all.
- En producción la Api sirve el SPA desde `wwwroot`. **Nada copia todavía el `dist/` del front a ese `wwwroot`** (ver la [guía de despliegue](../guides/despliegue.md#antes-que-nada-el-front-no-llega-a-la-imagen)). Sin `wwwroot/index.html` el middleware no se instala y la Api funciona como Api sola.

## Tests: arquitectura y arnés

- **ArchitectureTests:** reglas de capas (NetArchTest y las referencias de cada `.csproj`) y convenciones: paquetes permitidos en Application, sin `IQueryable`/`Expression` en su API pública, sin rutas Minimal API, cada `*Service` con su interfaz, entradas de controller como contratos, `[HasPermission]` (sin `[Authorize(Roles = …)]` ni la política de un permiso armada a mano), adaptadores en `Api/RequestContext`, formato de las claves de `Errors.resx` y una configuración EF por entidad. Un paquete nuevo en Application, una clave reservada de `Errors.resx` fuera del formato o un `Map*` técnico se suman a la lista de su test (`ApplicationPackagesTests`, `ErrorCodeTests`, `MinimalApiRoutesTests`). `TransactionBoundaryTests` lee el IL con Mono.Cecil: solo un punto de entrada de un caso de uso recibe `IUnitOfWork` (o la clase concreta) y llama a `ExecuteInTransactionAsync`; con una sola excepción con nombre, `DatabaseSeeder` (el seed de arranque), que las dos reglas afirman ver para que no quede vieja; la clase concreta `UnitOfWork` solo la nombra la registración en DI (`PersistenceRegistration`); solo `UnitOfWork` abre, confirma y guarda, con el guardado resuelto por herencia hasta `DbContext` o por una interfaz, sin excepciones (tampoco el seed); el SQL y las claves de los locks viven en un solo lugar, `ExecuteUpdate`/`ExecuteDelete` solo en la retención, y `HybridCache` se llena solo con `HybridCacheExtensions`, que lee en un scope propio (ni `GetOrCreateAsync` ni `SetAsync` fuera de ella; `SetAsync` se prueba con un caso de control, porque nadie lo llama). Por reflexión fija además que `IUnitOfWork` tiene un solo método y los valores de `CommitPolicy`. Un prefijo de lock nuevo va en `AdvisoryLockKeys` y en `LockKeyPrefixes` del test (el de un módulo, en las claves del módulo y en `AddModuleLockKeyPrefixes`), o la regla no lo ve. No verifica que cada método que escribe abra un límite (ver [Una sola forma de guardar](#una-sola-forma-de-guardar)). `IdentityBoundaryTests`, con la misma lectura del IL, fija lo de Identity: la única carga por Id de una cuenta no borrada para modificarla es `UserManagerExtensions.RequireUserAsync` (nadie llama a `UserManager.FindByIdAsync`; `UserRepository.RestoreAsync`, que carga la borrada, y el seed, que busca al administrador por correo, cargan con su propia consulta); `ISignInService` tiene como máximo 12 miembros, ninguno de datos de cuentas, solo `SignInService` toca `SignInManager`, el bloqueo, el security stamp, las revocaciones por sujeto de OpenIddict y `HttpContext.SignInAsync` o `IAuthenticationService.SignInAsync` (el passthrough de OpenIddict emite con `ControllerBase.SignIn` y no cuenta); solo `ConnectController` (authorize y logout) y `SignInService` (la cookie de Google) borran una cookie con `HttpContext.SignOutAsync` o `IAuthenticationService.SignOutAsync`; solo los puntos de entrada del ingreso (`LoginCodeService`, `LoginLinkService` y `ExternalLoginService`) abren una sesión, solo `LoginCodeVerifier` suma intentos fallidos, solo el ingreso los pone en cero (`LoginCodeVerifier`, `LoginLinkVerifier` y `ExternalLoginService`), solo `AccountAccessRevoker` cierra las sesiones (`RevokeSessionsAsync`) y, en la parte del módulo, los tipos de `Application/Modules/WhatsApp/Services` llaman a `ISignInService` solo para `IsLockedOutAsync`. Las reglas que no tienen una llamada real que mostrar prueban su detector con un caso de control al pie del archivo, como `TransactionBoundaryTests`. `PersistenceNamingTests` fija la [convención de nombres](#nombres-de-repositorios-y-lectores) de los contratos de `Interfaces/Persistence` por reflexión, y por el IL que solo los lectores llaman a `AsNoTracking`. `PersistenceRegistrationTests`, con la misma lectura del IL, fija que en `src` nadie nombre una clase concreta de `Persistence.Repositories`, `.Readers` o `.Seed` salvo `PersistenceRegistration`, que las registra, el `<M>InfrastructureRegistration` de un módulo, solo para las de su módulo, `SeedExtensions`, que resuelve `DatabaseSeeder`, y `DatabaseSeeder`, que recibe los tres seeders; filtra por namespace y no por sufijo (`WhatsAppWebhookReader` es un parser), y los tests de integración que construyen repositorios concretos quedan fuera. `ServiceOutputNamingTests` fija que la salida de un servicio termina en `Response` ([Modelos](#modelos-response-y-row)). `ServiceDependencyLimitTests` pone un tope de 8 dependencias al constructor de toda clase no estática de `Application.Services`, puntos de entrada y helpers, contando `ILogger`, `IOptions` y `TimeProvider`. Su diccionario de excepciones (cada una con su motivo; el test falla si una ya no hace falta) quedó vacío al partir el bot en la Etapa 3: una clase que se pase del tope se parte por responsabilidad. Vacío no pasa en silencio, porque el escaneo tiene que ver clases y el detector tiene su caso de control. Las reglas que ubican un tipo por su namespace comparan el namespace canónico, así ven también a los [módulos opcionales](#módulos-opcionales); `ModuleBoundaryTests` fija su frontera, sin excepciones: la lista de archivos del núcleo que todavía nombraban a WhatsApp (`KnownViolations`) existió solo mientras se mudaba, y se borró al vaciarse (Etapa 6, tanda 7). En `TransactionBoundaryTests`, los prefijos de lock del núcleo viven solo en `AdvisoryLockKeys` y los de un módulo, solo en el tipo que guarda sus claves.
- **Api.IntegrationTests:** `ApiFactory` (WebApplicationFactory + Testcontainers `postgres:18.3`).
  - Reutiliza la registración del DbContext de producción: solo cambia el tipo de contexto (`TestDbContext`) y la cadena de conexión. No volver a registrar el DbContext en el arnés.
  - Lo que existe solo para probar (entidades, adaptadores y controllers de las rutas `/test`) va en `TestFeatures/` del proyecto de tests, nunca en `src/`. `ApiFactory` registra únicamente esos controllers de prueba mediante un `ApplicationPart` selectivo.
  - Autenticación:
    - `AuthFlow.LoginAsync` hace el ingreso real (código → authorize con PKCE → token) y devuelve los tokens;
    - con el header `X-Test-UserId`, en cambio, se usa el usuario de prueba.
  - `factory.EmailSender` guarda los emails: el código es la primera palabra del asunto.
  - Los datos que se arman con escrituras de cuentas o de roles van dentro de `factory.InTransactionAsync(services => ...)`, un límite real con `OnSuccess` en un scope nuevo; fuera de uno, esas escrituras lanzan. Un 23505 que escapa sale como `UniqueConstraintViolationException`, igual que en producción.
  - Los límites están relajados:
    - sin espera entre pedidos de código;
    - rate limiter alto.
    Para probar un límite, usar `factory.WithWebHostBuilder(...)` con el valor real.
- **Unidad de trabajo en los tests:** los unitarios usan `FakeUnitOfWork` (`TestDoubles`), que aplica la misma regla que producción (`CommitPolicyExtensions.Commits`) y cuenta `Transactions`, `Commits`, `Rollbacks` y `LastPolicy`; lo que hay que mirar "al confirmar" se toma en `OnCommit`, y `CommitFailure` hace fallar el commit. Los dobles de lock, `InMemoryUserAccounts` (las cuentas: `IUserReader` e `IUserRepository`) en sus escrituras y `FakeSignInService` en los intentos fallidos y el cierre de sesiones reciben `InTransaction = () => unitOfWork.InTransaction` y lanzan fuera del límite; lo que el test arma con las escrituras de cuentas antes del caso de uso va en `InMemoryUserAccounts.ArrangeAsync`. Con una lista de eventos compartida, `FakeUnitOfWork` anota `"commit"` y `FakeSignInService` anota `"sign-in"`: así un test fija en qué orden pasaron. `FakeSignInService.SignInAsync`, al revés que las escrituras, lanza adentro del límite: la cookie sale después del commit. Con `SignInFailure`, falla como una cookie que no se pudo escribir (anota igual `"sign-in"`): así los tests de los tres ingresos fijan que el código o el enlace quedan gastados y la auditoría de éxito se queda. En integración, `FailingCommitUnitOfWork.Replace(services, probe)` hace fallar el commit sobre la unidad real, y `probe.RolledBackBeforeLeaving` confirma que el rollback lo hizo producción.

## Cómo mantener fija esta decisión

1. **Antes de desarrollar una función nueva**, ubicar cada pieza en el recorrido Controller → interfaz de servicio → servicio → interfaz de persistencia/integración → implementación. Si una pieza no encaja, aclarar primero su responsabilidad; no añadir automáticamente un handler o un repositorio genérico.
2. **Para cada cambio**, conservar contratos HTTP y reglas de negocio con pruebas de servicio, persistencia y rutas. No reintroducir `Application/Features`, `Api/Endpoints`, `IEndpoint`, handlers `ICommandHandler`/`IQueryHandler`, sus decoradores ni Scrutor para casos de uso.
3. **Proteger la arquitectura con tests:** dependencias permitidas entre proyectos; prohibición de EF/Npgsql/ASP.NET Core en `Application`; prohibición de EF en `Api`; controllers que dependen de servicios y no de repositorios; contratos de persistencia en `Application`; ausencia del pipeline de handlers/Minimal APIs de negocio. Los tests HTTP verifican el contrato observable de cada ruta. Además, `tests/ArquitecturaBase.ArchitectureTests` verifica:
   - `ApplicationPackagesTests`: `Application` solo referencia los paquetes de una lista explícita y ningún framework compartido (`FrameworkReference`);
   - `ApplicationPublicApiTests`: ninguna firma pública de `Application` expone `IQueryable` ni `Expression`;
   - `MinimalApiRoutesTests`: no hay `MapGet`, `MapPost`, `MapPut`, `MapDelete`, `MapPatch` ni `MapMethods` en `Api`, `Application` ni `Infrastructure` (los endpoints técnicos de Aspire se mapean en `ServiceDefaults`);
   - `ApplicationServicesTests`: cada clase `*Service` de `Application.Services` implementa una interfaz de `Interfaces.Services`; las piezas internas de un área (guards, issuers, verifiers) quedan fuera;
   - `ControllerInputContractTests` y `PermissionAuthorizationTests`: las reglas 1 y 2 del [borde HTTP](#borde-http);
   - `ApiRequestContextTests`: los adaptadores de `HttpContext` viven en `Api/RequestContext` y no existe `Api.Services`;
   - `ErrorCodeTests`: las claves de `Errors.resx` siguen `Area.Entidad.Motivo`, salvo las reservadas del framework (`Title.*`, `Validation.Failed` y las de `ApiErrorCodes`), y cada reservada sigue existiendo;
   - `EntityConfigurationTests`: cada entidad de Domain tiene su `IEntityTypeConfiguration<T>` en `Infrastructure/Persistence/Configurations`;
   - `ControllerServiceRepositoryTests` falla si no encuentra los controllers y revisa también los sub-namespaces y los controllers de un módulo;
   - `ModuleBoundaryTests`: la frontera de los [módulos opcionales](#módulos-opcionales).

   `.editorconfig` deja `CA1848` en `warning`, así un `logger.LogX` directo rompe el build. Un paquete nuevo en `Application`, una clave reservada nueva o un `Map*` técnico se agregan a la lista de su test, con el motivo.
4. **Puerta de cierre por área:** pruebas unitarias del servicio, integración HTTP, comportamiento de persistencia/transacción, inventario de rutas sin duplicados ni pérdidas, y build/test completos al terminar la migración. El plan enlazado contiene el orden de ejecución y los casos de regresión de cada área.

Esta decisión solo se modifica mediante una nueva decisión de arquitectura explícita. Mover un archivo o agregar una funcionalidad no cambia por sí solo los límites de las capas.
