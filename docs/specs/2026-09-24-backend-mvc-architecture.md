# Arquitectura canónica del backend: MVC, servicios y persistencia especializada

**Estado:** decisión aprobada el 2026-09-24. El 2026-09-26 sumó el [borde HTTP](#borde-http) y los tests de arquitectura nuevos, que aplican las decisiones del [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md) sin cambiar los límites de las capas.

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
│  │  │  ├─ ConnectController.cs
│  │  │  └─ WhatsAppWebhookController.cs
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
│  │  └─ Routing/                          # rutas condicionales de WhatsApp
│  │
│  ├─ ArquitecturaBase.Application/
│  │  ├─ DependencyInjection.cs
│  │  ├─ Interfaces/
│  │  │  ├─ Services/                      # IUserService, IProfileService, etc.
│  │  │  ├─ Persistence/                   # I*Repository, I*Reader, IUnitOfWork
│  │  │  └─ Integrations/                  # Identity, correo, seguridad, WhatsApp
│  │  ├─ Services/
│  │  │  ├─ Settings/                      # SystemSettingsService
│  │  │  ├─ Users/                         # UserService, ProfileService y helpers
│  │  │  ├─ Roles/                         # RoleService
│  │  │  ├─ Auth/                          # AccountService, LoginLinkService
│  │  │  └─ WhatsApp/                      # Webhook, entrada y entrega
│  │  ├─ Models/<Área>/                    # pedidos, respuestas y DTO de aplicación
│  │  ├─ Validation/<Área>/                # validadores FluentValidation
│  │  ├─ Configuration/
│  │  ├─ Common/
│  │  └─ Resources/
│  │
│  ├─ ArquitecturaBase.Domain/
│  │  ├─ Authentication/
│  │  ├─ Authorization/
│  │  ├─ Common/                           # Entity (Id Guid v7), ValueObject, IAuditable, ISoftDeletable
│  │  ├─ Results/
│  │  ├─ Settings/
│  │  ├─ Users/
│  │  ├─ ValueObjects/
│  │  └─ WhatsApp/
│  │
│  ├─ ArquitecturaBase.Infrastructure/
│  │  ├─ DependencyInjection.cs
│  │  ├─ Persistence/
│  │  │  ├─ ApplicationDbContext.cs
│  │  │  ├─ UnitOfWork.cs
│  │  │  ├─ Repositories/                 # escrituras y agregados
│  │  │  ├─ Readers/                      # lecturas especializadas
│  │  │  ├─ Configurations/
│  │  │  ├─ Extensions/
│  │  │  ├─ Interceptors/
│  │  │  ├─ Seed/
│  │  │  └─ Migrations/
│  │  ├─ Identity/                        # adaptadores Identity y OpenIddict
│  │  ├─ Emails/
│  │  ├─ WhatsApp/                        # cliente Meta y workers técnicos
│  │  ├─ Phones/
│  │  ├─ Security/
│  │  └─ Settings/                        # opciones técnicas de registro
│  │
│  ├─ ArquitecturaBase.AppHost/
│  └─ ArquitecturaBase.ServiceDefaults/
└─ tests/
   ├─ ArquitecturaBase.Api.IntegrationTests/
   ├─ ArquitecturaBase.Application.UnitTests/
   ├─ ArquitecturaBase.ArchitectureTests/
   └─ ArquitecturaBase.Domain.UnitTests/
```

`Application/Interfaces/Services` contiene `ISystemSettingsService`, `IUserService`, `IProfileService`, `IRoleService`, `IAccountService`, `ILoginLinkService`, `IWhatsAppWebhookService`, `IWhatsAppInboundService` e `IWhatsAppDeliveryService` para las responsabilidades actuales. Las implementaciones se ubican por área en `Application/Services`. Los contratos concretos de persistencia y proveedores van en las otras dos carpetas de `Interfaces`, con implementaciones en `Infrastructure`.

## Reglas de ubicación y acceso

1. **Api:** cada ruta HTTP de negocio se implementa como acción de un controller MVC. El controller recibe servicios por sus interfaces; no inyecta `ApplicationDbContext`, `UserManager`, `RoleManager`, `IQueryable`, repositorios ni lectores. El binding, la autorización HTTP, `Challenge`, cookies, redirecciones, cuerpo crudo del webhook y conversión de `Result` a HTTP pertenecen a `Api`.
2. **Application:** los servicios coordinan reglas, validadores, transacciones, repositorios/lectores y puertos externos. No referencian EF Core, Npgsql, `HttpContext`, `IActionResult` ni entidades de Identity de Infrastructure. Sus contratos no exponen `IQueryable`, `ApplicationUser` ni `ApplicationRole`; retornan modelos o resultados propios de Application/Domain.
3. **Domain:** alberga el modelo puro, incluidas interfaces que son parte del modelo como `IAuditable` e `ISoftDeletable`. Los contratos de persistencia que consumen servicios viven en `Application/Interfaces/Persistence`, no junto a entidades de `Domain`.
4. **Infrastructure:** las consultas y escrituras EF de negocio viven detrás de repositorios o lectores especializados. `IdentityService` y `PermissionService` delegan sus consultas de negocio a esos componentes; mantienen operaciones técnicas de Identity. `UnitOfWork`, migraciones, seeders, health checks, interceptores y stores de Identity/OpenIddict/DataProtection pueden usar directamente `ApplicationDbContext` como infraestructura técnica. Un contrato que solo usa Infrastructure, como el cliente interno de Meta, puede permanecer privado allí.
5. **DI:** `Api` registra MVC y componentes HTTP; `Application` registra las interfaces y servicios de casos de uso y validación; `Infrastructure` registra `ApplicationDbContext`, interfaces de persistencia, adaptadores y workers. Las dependencias se resuelven por DI explícita y con lifetimes compatibles con `DbContext` (normalmente scoped). `Program.cs` compone las capas. Evitar service locator en lógica de negocio.
6. **Servicios de fondo:** un worker de Infrastructure puede manejar programación, scopes, señalización y proveedores externos; invoca un servicio de Application para el caso de uso. La retención de mensajes conserva el scheduler técnico en Infrastructure, pero encapsula la operación EF de negocio en un repositorio especializado. El reintento del webhook que requiere un scope nuevo se implementa mediante un adaptador de Infrastructure.

## Borde HTTP

Cómo escribe una acción de controller su entrada, su autorización, su respuesta y su documentación. Rige para las rutas de negocio; `ConnectController`, `ExternalLoginController` y `WhatsAppWebhookController` hablan su propio protocolo (OpenIddict, navegación del navegador y Meta) y quedan fuera de la convención de OpenAPI.

1. **Entrada ([ADR 0002](../decisions/0002-contratos-http.md)):** todo parámetro de body o de query, incluido el cuerpo que `[ApiController]` infiere para un tipo complejo sin atributo, es un contrato de `Api/Contracts/<Área>` (`*HttpRequest`, o `*Query` para una query) o un valor simple (primitivo, enum, `string`, `Guid`, fecha u hora, o su versión nulable); una colección en la query necesita un contrato. Lo verifica `ControllerInputContractTests`. El controller mapea el contrato a mano al modelo de Application. Las respuestas no llevan contrato propio: serializan los `*Response` de Application, así que renombrar una propiedad de un `*Response` cambia el contrato HTTP. Un contrato con correo, número, código, token o datos del perfil sobrescribe `ToString()`, porque MVC registra los argumentos de la acción; los modelos de Application no lo hacen. Los parámetros de ruta, header y servicios quedan fuera de la regla.
2. **Autorización:** una ruta pide un permiso con `[HasPermission(Permissions.<Área>.<Acción>)]` (`Api/Authorization/HasPermissionAttribute.cs`) en el controller o en la acción, nunca con roles ni con `[Authorize(Policy = …)]` armado a mano; lo verifica `PermissionAuthorizationTests`. Sin sesión responde 401 y sin el permiso, 403. Un `[Authorize]` sin permiso solo pide sesión.
3. **Resultado:** el controller convierte el `Result` con las extensiones de `ControllerResultExtensions`:
   - `ToActionResult`: 200 con el valor, o 204 para un `Result` sin valor;
   - `ToAcceptedResult`: 202, con el valor en el cuerpo si lo hay y nunca con `Location`;
   - `ToCreatedResult(this, nameof(Get), id => new { id })`: 201 con el valor y el `Location` de la acción que devuelve el recurso (`CreatedAtAction`). Esa acción tiene que existir en el mismo controller: si la ruta no se puede armar, MVC lanza una excepción en lugar de responder un 201 sin `Location`.

   No se escribe a mano `Accepted(...)`, `StatusCode(202)` ni `IsSuccess ? … : …`. Por dentro, las tres arman el error con `ProblemDetailsMapper.FromError(error, factory, httpContext)` y el `ProblemDetailsFactory` de MVC, así lleva el mismo `type` y pasa por el mismo `CustomizeProblemDetails` que los errores del framework.
4. **ProblemDetails y JSON:** el `traceId` se calcula en un solo lugar, `ProblemDetailsMapper.AddTraceId` (la actividad actual o, sin ella, el `TraceIdentifier` del pedido). Las opciones JSON de MVC y de `Http.Json` salen de un solo `ConfigureJson` (`Api/DependencyInjection.cs`); un conversor nuevo va ahí, y `JsonOptionsTests` verifica que las dos sigan escribiendo y leyendo igual. El 400 de un cuerpo ilegible o de un parámetro que no se puede convertir (`Request.Invalid`) no trae `errors` a propósito: el ModelState nombraría rutas JSON, propiedades y tipos .NET, que son la forma interna del modelo. Los `errors` por campo salen solo de los validadores de Application.
5. **OpenAPI:** cada acción declara su éxito con `[ProducesResponseType<T>(status)]`, o `[ProducesResponseType(StatusCodes.Status204NoContent)]`. Una acción sin esa declaración aparece en el documento solo con sus errores, porque ApiExplorer deja de suponer el 200 en cuanto hay alguna respuesta declarada. Los errores, todos como ProblemDetails, los declara `ProblemResponsesConvention` (`Api/OpenApi`) a partir de la firma: 500 siempre; 400 si recibe body o query; 401 si pide sesión y no es `[AllowAnonymous]`; 403 solo si además pide un permiso; 404 si la ruta tiene un parámetro como `{id}`. Lo que la firma no muestra se declara con `[ProducesProblem(status)]` (por ejemplo, el 404 de `/api/me`). Un controller nuevo con protocolo propio se suma a `OwnProtocolControllers` y declara a mano sus respuestas. `OpenApiTests` falla si una operación de `/api` no declara un 2xx con esquema (salvo un 204 o un 202 sin cuerpo que figure en su lista), no declara el 500 o declara un error que no es ProblemDetails.

## Validación, guardado y errores

- FluentValidation valida los modelos de entrada de Application **antes** de ejecutar cambios. El registro DI debe resolver todos los validadores aplicables. Un pedido inválido no abre transacción ni llega a repositorios.
- Los servicios devuelven `Result` o `Result<T>` para errores de negocio. El borde HTTP conserva los códigos y mensajes traducidos de `ProblemDetails`, incluidos `code`, `traceId` y los errores por campo.
- Una escritura define su límite con `IUnitOfWork.ExecuteInTransactionAsync(trabajo, CommitPolicy, ct)`, la única forma de guardar; una consulta no abre límite. Hay casos que **deben guardar también al devolver error**, como intentos fallidos, códigos o enlaces consumidos y auditorías: usan `CommitPolicy.OnAnyResult`. Los demás usan `OnSuccess`, que deshace todo ante un error de negocio. No se aplica un guardado uniforme por convención.
- `UserManager` y `RoleManager` guardan internamente, pero dentro de la transacción del caso de uso y con un savepoint por guardado. Los locks exigen esa transacción, duran lo que ella y conservan su orden: `login-code:` del correo y después del número, filas de contactos, y después la cuenta.
- El logging operativo conserva inicio, resultado y código de error sin escribir códigos de ingreso, tokens ni secretos.

## Excepciones de protocolo y conservación funcional

`/connect/revoke`, `/connect/introspect` y discovery `/.well-known/*` son endpoints técnicos administrados por OpenIddict. Los endpoints técnicos de Aspire también permanecen con su framework. Las acciones propias de `/connect` usan controllers MVC y conservan passthrough, PKCE, cookies, formularios y redirecciones. Esto no habilita Minimal APIs para rutas de negocio nuevas.

El cambio de estructura **no cambia el producto**: se conservan las 41 combinaciones explícitas de verbo/ruta inventariadas en el plan, respuestas HTTP, autorización, rate limits, contratos JSON, OpenIddict, frontend y comportamiento de WhatsApp. Las cuatro combinaciones condicionales de WhatsApp deben seguir apareciendo o desapareciendo en el enrutamiento según configuración; responder 404 dentro de una ruta siempre registrada no equivale a omitirla. Los workers de entrada, envío y retención siguen operativos.

Cambios deliberados posteriores a la migración (2026-09-26, [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md), Etapa 3): `POST /api/users` responde `201` con `Location` a `/api/users/{id}` en lugar de `200`, con el mismo cuerpo, y el front trata cualquier 2xx como éxito; `POST /api/roles` sigue en `200` hasta que exista `GET /api/roles/{id}` (Etapa 4). En el documento OpenAPI, los esquemas de los cuerpos llevan el nombre de su contrato (`UpdateProfileHttpRequest` en lugar de `UpdateProfileRequest`); el JSON no cambió.

La base es de desarrollo y puede recrearse. No se exige migrar datos ni preservar su historial; sí se exige que una base vacía arranque con esquema, seed y flujos principales funcionales.

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
   - `ControllerServiceRepositoryTests` falla si no encuentra los controllers y revisa también los sub-namespaces.

   `.editorconfig` deja `CA1848` en `warning`, así un `logger.LogX` directo rompe el build. Un paquete nuevo en `Application`, una clave reservada nueva o un `Map*` técnico se agregan a la lista de su test, con el motivo.
4. **Puerta de cierre por área:** pruebas unitarias del servicio, integración HTTP, comportamiento de persistencia/transacción, inventario de rutas sin duplicados ni pérdidas, y build/test completos al terminar la migración. El plan enlazado contiene el orden de ejecución y los casos de regresión de cada área.

Esta decisión solo se modifica mediante una nueva decisión de arquitectura explícita. Mover un archivo o agregar una funcionalidad no cambia por sí solo los límites de las capas.
