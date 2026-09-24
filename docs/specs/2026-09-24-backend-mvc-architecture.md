# Arquitectura canónica del backend: MVC, servicios y persistencia especializada

**Estado:** decisión aprobada el 2026-09-24.

**Alcance:** `ArquitecturaBase` backend. `ArquitecturaBaseFront` mantiene su proyecto separado; `ArquitecturaBaseMultitenant` queda fuera de este alcance.

**Ejecución:** [plan de migración](../plans/2026-09-23-migracion-mvc-servicios-repositorios.md).

Este documento fija la **arquitectura vigente del backend**. Ante una contradicción con el diseño inicial de 2026-09-18 o con una guía histórica que describa handlers, prevalece esta decisión. Los contratos funcionales y de seguridad implementados siguen vigentes. El [plan de migración](../plans/2026-09-23-migracion-mvc-servicios-repositorios.md) conserva el historial y las puertas de verificación antes de integrar el cambio a `main`.

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
| `ArquitecturaBase.Domain` | Entidades, value objects, reglas, eventos y errores del modelo | BCL |
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
│  │  ├─ Contracts/<Área>/                 # modelos propios del transporte HTTP
│  │  ├─ Authentication/                   # adaptación HTTP/OpenIddict
│  │  ├─ Authorization/
│  │  ├─ ErrorHandling/                    # Result → ProblemDetails/IActionResult
│  │  ├─ RequestContext/                   # adaptadores de HttpContext
│  │  ├─ Hosting/
│  │  ├─ Json/
│  │  ├─ Localization/
│  │  ├─ OpenApi/
│  │  └─ RateLimiting/
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
│  │  ├─ Common/
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

## Validación, guardado y errores

- FluentValidation valida los modelos de entrada de Application **antes** de ejecutar cambios. El registro DI debe resolver todos los validadores aplicables. Un pedido inválido no llega a repositorios ni a `IUnitOfWork`.
- Los servicios devuelven `Result` o `Result<T>` para errores de negocio. El borde HTTP conserva los códigos y mensajes traducidos de `ProblemDetails`, incluidos `code`, `traceId` y los errores por campo.
- Una escritura define explícitamente cuándo confirma con `IUnitOfWork`; una consulta no guarda. Hay casos que **deben guardar también al devolver error**, como intentos fallidos o códigos/enlaces consumidos. La migración de un handler a servicio conserva exactamente esas reglas; no aplica un `SaveChanges` uniforme por convención.
- `UserManager` y `RoleManager` pueden guardar internamente. Cuando un flujo combina Identity con códigos, enlaces, auditoría, permisos o invitaciones, se verifica la transacción compartida y el rollback o guardado en error esperado. Los locks y su orden existente se conservan.
- El logging operativo conserva inicio, resultado y código de error sin escribir códigos de ingreso, tokens ni secretos.

## Excepciones de protocolo y conservación funcional

`/connect/revoke`, `/connect/introspect` y discovery `/.well-known/*` son endpoints técnicos administrados por OpenIddict. Los endpoints técnicos de Aspire también permanecen con su framework. Las acciones propias de `/connect` usan controllers MVC y conservan passthrough, PKCE, cookies, formularios y redirecciones. Esto no habilita Minimal APIs para rutas de negocio nuevas.

El cambio de estructura **no cambia el producto**: se conservan las 41 combinaciones explícitas de verbo/ruta inventariadas en el plan, respuestas HTTP, autorización, rate limits, contratos JSON, OpenIddict, frontend y comportamiento de WhatsApp. Las cuatro combinaciones condicionales de WhatsApp deben seguir apareciendo o desapareciendo en el enrutamiento según configuración; responder 404 dentro de una ruta siempre registrada no equivale a omitirla. Los workers de entrada, envío y retención siguen operativos.

La base es de desarrollo y puede recrearse. No se exige migrar datos ni preservar su historial; sí se exige que una base vacía arranque con esquema, seed y flujos principales funcionales.

## Cómo mantener fija esta decisión

1. **Antes de desarrollar una función nueva**, ubicar cada pieza en el recorrido Controller → interfaz de servicio → servicio → interfaz de persistencia/integración → implementación. Si una pieza no encaja, aclarar primero su responsabilidad; no añadir automáticamente un handler o un repositorio genérico.
2. **Para cada cambio**, conservar contratos HTTP y reglas de negocio con pruebas de servicio, persistencia y rutas. No reintroducir `Application/Features`, `Api/Endpoints`, `IEndpoint`, handlers `ICommandHandler`/`IQueryHandler`, sus decoradores ni Scrutor para casos de uso.
3. **Proteger la arquitectura con tests:** dependencias permitidas entre proyectos; prohibición de EF/Npgsql/ASP.NET Core en `Application`; prohibición de EF en `Api`; controllers que dependen de servicios y no de repositorios; contratos de persistencia en `Application`; ausencia del pipeline de handlers/Minimal APIs de negocio. Los tests HTTP verifican el contrato observable de cada ruta.
4. **Puerta de cierre por área:** pruebas unitarias del servicio, integración HTTP, comportamiento de persistencia/transacción, inventario de rutas sin duplicados ni pérdidas, y build/test completos al terminar la migración. El plan enlazado contiene el orden de ejecución y los casos de regresión de cada área.

Esta decisión solo se modifica mediante una nueva decisión de arquitectura explícita. Mover un archivo o agregar una funcionalidad no cambia por sí solo los límites de las capas.
