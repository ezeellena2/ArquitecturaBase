# Mapa de instrucciones del backend

Relevamiento del 2026-09-30. Hay **67 ámbitos** con `AGENTS.md` y su `CLAUDE.md`. Cada enlace abre la guía del ámbito; el archivo de Claude importa esa misma fuente con `@AGENTS.md`.

## Cómo entrar a un desarrollo

Leé la raíz y después las guías de las carpetas que contienen los archivos que vas a cambiar, de mayor a menor alcance. Si una operación cruza áreas, leé cada área y la guía funcional enlazada. Una guía local complementa la raíz: no redefine la arquitectura.

La arquitectura canónica está en [backend.md](backend.md); el [mapa del front](../../../ArquitecturaBaseFront/docs/mapa-de-instrucciones.md) cubre el repositorio vecino. Antes había 18 ámbitos, concentrados en áreas funcionales; se completaron capas, adaptadores, persistencia, tests y documentación.

## Carpetas con guía propia

| Ámbito | Para qué sirve |
|---|---|
| [`./`](../../AGENTS.md) | Reglas de la plantilla, comandos, capas y dónde ubicar cada pieza. |
| [`.github/workflows/`](../../.github/workflows/AGENTS.md) | El workflow compila y prueba, genera el bundle EF y la imagen, y despliega a Azure. |
| [`docs/`](../AGENTS.md) | `architecture` fija estructura; `features`, reglas funcionales; `guides`, procedimientos; `decisions`, ADR; `plans`, trabajo en curso; `history`, trabajo cerrado; `specs`, diseños; `deploy` y `postman`, materiales de operación. |
| [`src/ArquitecturaBase.Api/`](../../src/ArquitecturaBase.Api/AGENTS.md) | `Program.cs` compone capas y ordena el pipeline. |
| [`src/ArquitecturaBase.Api/Authentication/`](../../src/ArquitecturaBase.Api/Authentication/AGENTS.md) | `OpenIdPrincipalFactory` transforma los datos de Application en el principal y destinos de claims que usa OpenIddict. |
| [`src/ArquitecturaBase.Api/Authorization/`](../../src/ArquitecturaBase.Api/Authorization/AGENTS.md) | El atributo, proveedor de políticas y handler conectan `HasPermission` con el servicio de permisos. |
| [`src/ArquitecturaBase.Api/Contracts/`](../../src/ArquitecturaBase.Api/Contracts/AGENTS.md) | Define cuerpos `*HttpRequest` y parámetros `*Query` por área. |
| [`src/ArquitecturaBase.Api/Controllers/`](../../src/ArquitecturaBase.Api/Controllers/AGENTS.md) | Recibe contratos HTTP, mapea pedidos y devuelve resultados de los servicios de Application. |
| [`src/ArquitecturaBase.Api/ErrorHandling/`](../../src/ArquitecturaBase.Api/ErrorHandling/AGENTS.md) | Mapea `Result`, validación, excepciones y errores del framework a ProblemDetails. |
| [`src/ArquitecturaBase.Api/Hosting/`](../../src/ArquitecturaBase.Api/Hosting/AGENTS.md) | Configura headers de seguridad, proxies de confianza y el fallback del SPA. |
| [`src/ArquitecturaBase.Api/Modules/WhatsApp/`](../../src/ArquitecturaBase.Api/Modules/WhatsApp/AGENTS.md) | Aloja controllers, contratos y convenciones de rutas de webhook, códigos y perfil del canal. |
| [`src/ArquitecturaBase.Api/RequestContext/`](../../src/ArquitecturaBase.Api/RequestContext/AGENTS.md) | `CurrentUser` y `RequestInfo` implementan puertos de Application sobre `HttpContext`. |
| [`src/ArquitecturaBase.AppHost/`](../../src/ArquitecturaBase.AppHost/AGENTS.md) | Orquesta PostgreSQL, Redis, Api y el front del repo vecino. |
| [`src/ArquitecturaBase.Application/`](../../src/ArquitecturaBase.Application/AGENTS.md) | Coordina operaciones de negocio sin depender de EF ni de HTTP. |
| [`src/ArquitecturaBase.Application/Channels/`](../../src/ArquitecturaBase.Application/Channels/AGENTS.md) | Implementa el canal de invitaciones por correo y la versión deshabilitada del canal de teléfono. |
| [`src/ArquitecturaBase.Application/Common/`](../../src/ArquitecturaBase.Application/Common/AGENTS.md) | `Pagination` define el pedido y resultado paginado; `Validation`, el puente a FluentValidation y errores por campo; `Logging`, operaciones; `Exceptions`, fallas técnicas que cruzan puertos. |
| [`src/ArquitecturaBase.Application/Interfaces/`](../../src/ArquitecturaBase.Application/Interfaces/AGENTS.md) | Las dependencias se definen por su función: `Services`, entrada a casos de uso; `Persistence`, acceso a datos; `Integrations`, proveedores técnicos; `Channels`, extensión por canales opcionales. |
| [`src/ArquitecturaBase.Application/Interfaces/Channels/`](../../src/ArquitecturaBase.Application/Interfaces/Channels/AGENTS.md) | Define capacidades que aportan canales opcionales: ingreso por teléfono, entrega y estado de invitaciones, y participantes de un vínculo de número. |
| [`src/ArquitecturaBase.Application/Interfaces/Integrations/`](../../src/ArquitecturaBase.Application/Interfaces/Integrations/AGENTS.md) | Agrupa contratos de Identity, seguridad, correo, petición, teléfonos y descarte de caché. |
| [`src/ArquitecturaBase.Application/Interfaces/Integrations/Identity/`](../../src/ArquitecturaBase.Application/Interfaces/Integrations/Identity/AGENTS.md) | Expresa operaciones de cuentas, roles, permisos, sesión, origen público y login externo que necesitan los casos de uso. |
| [`src/ArquitecturaBase.Application/Interfaces/Persistence/`](../../src/ArquitecturaBase.Application/Interfaces/Persistence/AGENTS.md) | Define repositorios, lectores y la unidad de trabajo. |
| [`src/ArquitecturaBase.Application/Interfaces/Services/`](../../src/ArquitecturaBase.Application/Interfaces/Services/AGENTS.md) | Define las operaciones que puede ejecutar el borde HTTP. |
| [`src/ArquitecturaBase.Application/Models/`](../../src/ArquitecturaBase.Application/Models/AGENTS.md) | Contiene pedidos, respuestas y filas de cada área. |
| [`src/ArquitecturaBase.Application/Models/Identity/`](../../src/ArquitecturaBase.Application/Models/Identity/AGENTS.md) | Describe cuenta, proveedores de ingreso externo y proyecciones de permisos sin depender de ASP.NET Identity. |
| [`src/ArquitecturaBase.Application/Models/Users/`](../../src/ArquitecturaBase.Application/Models/Users/AGENTS.md) | Define entradas funcionales, respuestas y filas de usuarios, perfil, invitaciones y filtros. |
| [`src/ArquitecturaBase.Application/Modules/WhatsApp/`](../../src/ArquitecturaBase.Application/Modules/WhatsApp/AGENTS.md) | Agrupa casos de uso, helpers, contratos, modelos, validadores, recursos y adaptadores de canales propios del módulo. |
| [`src/ArquitecturaBase.Application/Resources/`](../../src/ArquitecturaBase.Application/Resources/AGENTS.md) | Los `.resx` de errores, validación y permisos tienen español e inglés. |
| [`src/ArquitecturaBase.Application/Services/`](../../src/ArquitecturaBase.Application/Services/AGENTS.md) | Cada subcarpeta agrupa un área. |
| [`src/ArquitecturaBase.Application/Services/Auth/`](../../src/ArquitecturaBase.Application/Services/Auth/AGENTS.md) | Coordina ingreso, registro explícito, acceso externo, códigos y enlaces. |
| [`src/ArquitecturaBase.Application/Services/Roles/`](../../src/ArquitecturaBase.Application/Services/Roles/AGENTS.md) | `RoleService` coordina lectura y cambios de roles y permisos. |
| [`src/ArquitecturaBase.Application/Services/Settings/`](../../src/ArquitecturaBase.Application/Services/Settings/AGENTS.md) | `SystemSettingsService` consulta y modifica la política persistida del sistema. |
| [`src/ArquitecturaBase.Application/Services/Users/`](../../src/ArquitecturaBase.Application/Services/Users/AGENTS.md) | Coordina listados, alta, acceso, roles, invitaciones y perfil propio. |
| [`src/ArquitecturaBase.Application/Validation/`](../../src/ArquitecturaBase.Application/Validation/AGENTS.md) | Agrupa validadores FluentValidation por área. |
| [`src/ArquitecturaBase.Domain/`](../../src/ArquitecturaBase.Domain/AGENTS.md) | Define entidades, valores y errores de negocio. |
| [`src/ArquitecturaBase.Domain/Authentication/`](../../src/ArquitecturaBase.Domain/Authentication/AGENTS.md) | Modela los hechos y las reglas puras del acceso: propósito y destino de códigos, consumos, intentos, enlaces de un solo uso y errores de cuenta. |
| [`src/ArquitecturaBase.Domain/Authorization/`](../../src/ArquitecturaBase.Domain/Authorization/AGENTS.md) | Define permisos, roles del sistema y errores de roles. |
| [`src/ArquitecturaBase.Domain/Modules/WhatsApp/`](../../src/ArquitecturaBase.Domain/Modules/WhatsApp/AGENTS.md) | Define contactos, mensajes y errores del módulo. |
| [`src/ArquitecturaBase.Domain/Settings/`](../../src/ArquitecturaBase.Domain/Settings/AGENTS.md) | Define la entidad de ajustes del sistema, el modo de registro y sus errores. |
| [`src/ArquitecturaBase.Domain/Users/`](../../src/ArquitecturaBase.Domain/Users/AGENTS.md) | Contiene reglas puras de cuenta, invitaciones y sus errores. |
| [`src/ArquitecturaBase.Infrastructure/`](../../src/ArquitecturaBase.Infrastructure/AGENTS.md) | Implementa los puertos de Application. |
| [`src/ArquitecturaBase.Infrastructure/Caching/`](../../src/ArquitecturaBase.Infrastructure/Caching/AGENTS.md) | `RedisCache` es el único adaptador de datos cacheados; `RedisCacheKey`, scripts y opciones definen claves, leases y límites. |
| [`src/ArquitecturaBase.Infrastructure/Emails/`](../../src/ArquitecturaBase.Infrastructure/Emails/AGENTS.md) | Implementa templates localizados, cola y worker de correo y adaptadores de entrega. |
| [`src/ArquitecturaBase.Infrastructure/Identity/`](../../src/ArquitecturaBase.Infrastructure/Identity/AGENTS.md) | Implementa los puertos de cuentas, roles y sesión con ASP.NET Identity. |
| [`src/ArquitecturaBase.Infrastructure/Identity/OpenIddict/`](../../src/ArquitecturaBase.Infrastructure/Identity/OpenIddict/AGENTS.md) | Configura protocolo, cliente web, credenciales y revocación. |
| [`src/ArquitecturaBase.Infrastructure/Modules/WhatsApp/`](../../src/ArquitecturaBase.Infrastructure/Modules/WhatsApp/AGENTS.md) | Implementa Meta, firma, parsing del webhook, colas, workers, outbox y persistencia del módulo. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/`](../../src/ArquitecturaBase.Infrastructure/Persistence/AGENTS.md) | `ApplicationDbContext` configura el modelo; `UnitOfWork` abre, confirma y deshace transacciones; `PersistenceRegistration` registra contexto, interceptores, repositorios, lectores, salud y seed. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/Configurations/`](../../src/ArquitecturaBase.Infrastructure/Persistence/Configurations/AGENTS.md) | Cada entidad tiene un `IEntityTypeConfiguration<T>` con columnas, límites, conversiones y restricciones. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/Extensions/`](../../src/ArquitecturaBase.Infrastructure/Persistence/Extensions/AGENTS.md) | Reúne paginado y orden, patrones de búsqueda, filtros de modelo y locks transaccionales. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/Interceptors/`](../../src/ArquitecturaBase.Infrastructure/Persistence/Interceptors/AGENTS.md) | Completa campos auditables al guardar y convierte el borrado lógico en modificación. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/Migrations/`](../../src/ArquitecturaBase.Infrastructure/Persistence/Migrations/AGENTS.md) | Contiene migraciones EF, sus metadatos y el snapshot. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/Readers/`](../../src/ArquitecturaBase.Infrastructure/Persistence/Readers/AGENTS.md) | Implementa consultas sin seguimiento, materializadas en filas o respuestas de Application. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/Repositories/`](../../src/ArquitecturaBase.Infrastructure/Persistence/Repositories/AGENTS.md) | Implementa los puertos que cargan entidades para modificar, agregan o quitan filas y toman locks. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/Seed/`](../../src/ArquitecturaBase.Infrastructure/Persistence/Seed/AGENTS.md) | `DatabaseSeeder` coordina roles, ajustes y cliente OpenIddict en una sola transacción. |
| [`src/ArquitecturaBase.Infrastructure/Phones/`](../../src/ArquitecturaBase.Infrastructure/Phones/AGENTS.md) | `PhoneNumberParser` adapta entradas por país y número a la representación canónica, formateada y enmascarada. |
| [`src/ArquitecturaBase.Infrastructure/Security/`](../../src/ArquitecturaBase.Infrastructure/Security/AGENTS.md) | Implementa generación segura de tokens y códigos, y su hash con opciones de configuración. |
| [`src/ArquitecturaBase.ServiceDefaults/`](../../src/ArquitecturaBase.ServiceDefaults/AGENTS.md) | `Extensions.cs` concentra los defaults compartidos de Aspire: OpenTelemetry, descubrimiento de servicios, resiliencia HTTP y endpoints de salud. |
| [`tests/`](../../tests/AGENTS.md) | Domain.UnitTests prueba reglas puras; Application.UnitTests, validación y coordinación; ArchitectureTests, fronteras y convenciones; Api.IntegrationTests, HTTP y adaptadores reales. |
| [`tests/ArquitecturaBase.Api.IntegrationTests/`](../../tests/ArquitecturaBase.Api.IntegrationTests/AGENTS.md) | `ApiFactory` hospeda la Api con PostgreSQL y Redis de Testcontainers. |
| [`tests/ArquitecturaBase.Api.IntegrationTests/Caching/`](../../tests/ArquitecturaBase.Api.IntegrationTests/Caching/AGENTS.md) | Prueba lectura, expiración, payloads, invalidación, leases, concurrencia, fallas y seed. |
| [`tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp/`](../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp/AGENTS.md) | Prueba webhook, outbox, bot, cuota, vinculación, rutas y convivencia con el núcleo sobre PostgreSQL y Redis. |
| [`tests/ArquitecturaBase.Api.IntegrationTests/Support/`](../../tests/ArquitecturaBase.Api.IntegrationTests/Support/AGENTS.md) | Contiene `ApiFactory`, helpers de autenticación y scopes de prueba. |
| [`tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/`](../../tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/AGENTS.md) | Aloja entidades, contexto, controllers y dobles técnicos de escenarios de integración. |
| [`tests/ArquitecturaBase.Application.UnitTests/`](../../tests/ArquitecturaBase.Application.UnitTests/AGENTS.md) | Espeja servicios, modelos, validadores y recursos de Application. |
| [`tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp/`](../../tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp/AGENTS.md) | Prueba coordinación de bot, códigos, perfil y canales con dobles de los puertos del módulo. |
| [`tests/ArquitecturaBase.ArchitectureTests/`](../../tests/ArquitecturaBase.ArchitectureTests/AGENTS.md) | Verifica capas, paquetes, ubicación, llamadas, nombres y límites de la arquitectura. |
| [`tests/ArquitecturaBase.Domain.UnitTests/`](../../tests/ArquitecturaBase.Domain.UnitTests/AGENTS.md) | Espeja las áreas de Domain y prueba invariantes, entidades, valores y resultados sin proveedores externos. |
| [`tests/ArquitecturaBase.Domain.UnitTests/Modules/WhatsApp/`](../../tests/ArquitecturaBase.Domain.UnitTests/Modules/WhatsApp/AGENTS.md) | Prueba entidades y reglas puras del módulo, sin proveedores. |

## Carpetas cubiertas por su ámbito padre

Domain/Common, Results y ValueObjects siguen Domain. Application/Configuration y los subgrupos de modelos y validadores siguen su capa y área; las reglas técnicas de Json, Localization, RateLimiting y OpenApi están en Api. Templates, recursos de correo y persistencia de módulos siguen su adaptador o módulo. Los detalles de docs siguen docs/AGENTS.md.

No se crean pares en `bin`, `obj`, `node_modules`, `dist`, `artifacts`, archivos individuales ni carpetas vacías. Un subámbito nuevo se justifica por una responsabilidad o riesgo que el padre no explica suficientemente.

## Mantenimiento

Cuando agregues un área, adaptador o cambio de responsabilidad, actualizá su guía y este mapa en el mismo desarrollo. Seguí la [receta de instrucciones por carpeta](../guides/instrucciones-por-carpeta.md). La enumeración es un punto de entrada, no un inventario de cada método.
