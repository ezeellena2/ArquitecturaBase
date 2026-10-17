# Mapa de instrucciones del backend

El 2026-10-02 los ámbitos existentes de `Application/Channels`, `Interfaces/Channels`, `Infrastructure/Caching` y los módulos WhatsApp amplían responsabilidades: catálogo de permisos compuesto, lease exclusiva de envío, bandeja y salida manual durable. Sus guías enlazan el [ADR 0010](../decisions/0010-catalogo-de-permisos-extensible.md) y la [fuente funcional](../features/whatsapp.md#chat-de-atención--2026-10-02). El área productiva del front está en `src/features/whatsapp`, con guía propia en su mapa.

Relevamiento del 2026-09-30, reconciliado el 2026-10-09. Hay **95 ámbitos** con `AGENTS.md` y su `CLAUDE.md`, incluido el arnés documental. Cada enlace abre la guía del ámbito; el archivo de Claude importa esa misma fuente con `@AGENTS.md`.

## Cómo entrar a un desarrollo

Leé la raíz y después las guías de las carpetas que contienen los archivos que vas a cambiar, de mayor a menor alcance. Si una operación cruza áreas, leé cada área y la guía funcional enlazada. Una guía local complementa la raíz: no redefine la arquitectura.

La arquitectura canónica está en [backend.md](backend.md); el [mapa del front](../../../ArquitecturaBaseFront/docs/mapa-de-instrucciones.md) cubre el repositorio vecino. Antes había 18 ámbitos, concentrados en áreas funcionales; se completaron capas, adaptadores, persistencia, tests y documentación.

Cada desarrollo sigue además la [rutina compartida de revisión y validación](../guides/validar-un-cambio.md): patrón existente, responsabilidades, controles y evidencia de cierre.

## Carpetas con guía propia

| Ámbito | Para qué sirve |
|---|---|
| [`./`](../../AGENTS.md) | Reglas de la plantilla, comandos, capas y dónde ubicar cada pieza. |
| [`.github/workflows/`](../../.github/workflows/AGENTS.md) | Verifica ambos repositorios, empaqueta SPA/API y bundle, y coordina el despliegue Azure dev con OIDC, activación explícita y comprobación de la revisión. |
| [`docs/`](../AGENTS.md) | Arquitectura y premisas, reglas funcionales, guías y auditoría documental, ADR, planes pendientes y procedencia recuperable desde Git; diseños y materiales de operación. |
| [`harnesses/`](../../harnesses/AGENTS.md) | Tareas repetibles de desarrollo: procedimiento compartido para Codex y Claude, adaptadores de invocación, controles y aplicación de propuestas documentales. |
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
| [`src/ArquitecturaBase.Application/Modules/WhatsApp/`](../../src/ArquitecturaBase.Application/Modules/WhatsApp/AGENTS.md) | Agrupa casos de uso, helpers y contratos; FlowReply valida mapping sellado y usa puertos neutrales de captura/lectura. |
| [`src/ArquitecturaBase.Application/Resources/`](../../src/ArquitecturaBase.Application/Resources/AGENTS.md) | Los `.resx` de errores, validación y permisos tienen español e inglés. |
| [`src/ArquitecturaBase.Application/Services/`](../../src/ArquitecturaBase.Application/Services/AGENTS.md) | Cada subcarpeta agrupa un área. |
| [`src/ArquitecturaBase.Application/Services/Auth/`](../../src/ArquitecturaBase.Application/Services/Auth/AGENTS.md) | Coordina ingreso, registro explícito, acceso externo, códigos y enlaces. |
| [`src/ArquitecturaBase.Application/Services/Roles/`](../../src/ArquitecturaBase.Application/Services/Roles/AGENTS.md) | `RoleService` coordina lectura y cambios de roles y permisos. |
| [`src/ArquitecturaBase.Application/Services/Settings/`](../../src/ArquitecturaBase.Application/Services/Settings/AGENTS.md) | `SystemSettingsService` consulta la política persistida y guarda uno o más ajustes con lock, una transacción y una revisión. |
| [`src/ArquitecturaBase.Application/Services/Users/`](../../src/ArquitecturaBase.Application/Services/Users/AGENTS.md) | Coordina listados, alta, acceso, roles, invitaciones y perfil propio. |
| [`src/ArquitecturaBase.Application/Validation/`](../../src/ArquitecturaBase.Application/Validation/AGENTS.md) | Agrupa validadores FluentValidation por área. |
| [`src/ArquitecturaBase.Domain/`](../../src/ArquitecturaBase.Domain/AGENTS.md) | Define entidades, valores y errores de negocio. |
| [`src/ArquitecturaBase.Domain/Authentication/`](../../src/ArquitecturaBase.Domain/Authentication/AGENTS.md) | Modela los hechos y las reglas puras del acceso: propósito y destino de códigos, consumos, intentos, enlaces de un solo uso y errores de cuenta. |
| [`src/ArquitecturaBase.Domain/Authorization/`](../../src/ArquitecturaBase.Domain/Authorization/AGENTS.md) | Define permisos, roles del sistema y errores de roles. |
| [`src/ArquitecturaBase.Domain/Modules/WhatsApp/`](../../src/ArquitecturaBase.Domain/Modules/WhatsApp/AGENTS.md) | Define contactos/mensajes y las transiciones de Flow; estado final por mensaje evita heredar resultados entre replays. |
| [`src/ArquitecturaBase.Domain/Settings/`](../../src/ArquitecturaBase.Domain/Settings/AGENTS.md) | Define los ajustes del sistema, modo de registro, paleta global y sus errores. |
| [`src/ArquitecturaBase.Domain/Users/`](../../src/ArquitecturaBase.Domain/Users/AGENTS.md) | Contiene reglas puras de cuenta, invitaciones y sus errores. |
| [`src/ArquitecturaBase.Infrastructure/`](../../src/ArquitecturaBase.Infrastructure/AGENTS.md) | Implementa los puertos de Application. |
| [`src/ArquitecturaBase.Infrastructure/Caching/`](../../src/ArquitecturaBase.Infrastructure/Caching/AGENTS.md) | `RedisCache` es el único adaptador de datos cacheados; `RedisCacheKey`, scripts y opciones definen claves, leases y límites. |
| [`src/ArquitecturaBase.Infrastructure/Emails/`](../../src/ArquitecturaBase.Infrastructure/Emails/AGENTS.md) | Implementa templates localizados, cola y worker de correo y adaptadores de entrega. |
| [`src/ArquitecturaBase.Infrastructure/Identity/`](../../src/ArquitecturaBase.Infrastructure/Identity/AGENTS.md) | Implementa los puertos de cuentas, roles y sesión con ASP.NET Identity. |
| [`src/ArquitecturaBase.Infrastructure/Identity/OpenIddict/`](../../src/ArquitecturaBase.Infrastructure/Identity/OpenIddict/AGENTS.md) | Configura protocolo, cliente web, credenciales y revocación. |
| [`src/ArquitecturaBase.Infrastructure/Modules/WhatsApp/`](../../src/ArquitecturaBase.Infrastructure/Modules/WhatsApp/AGENTS.md) | Implementa Meta, firma, parsing, cola de envío, workers e historial; verifica WABA/número antes de efectos y protege/resuelve respuestas de Flows. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/`](../../src/ArquitecturaBase.Infrastructure/Persistence/AGENTS.md) | `ApplicationDbContext` configura el modelo; `UnitOfWork` abre, confirma y deshace transacciones; `PersistenceRegistration` registra contexto, interceptores, repositorios, lectores, salud y seed. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/Configurations/`](../../src/ArquitecturaBase.Infrastructure/Persistence/Configurations/AGENTS.md) | Cada entidad tiene un `IEntityTypeConfiguration<T>` con columnas, límites, conversiones y restricciones. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/Extensions/`](../../src/ArquitecturaBase.Infrastructure/Persistence/Extensions/AGENTS.md) | Reúne paginado y orden, patrones de búsqueda, filtros de modelo y locks transaccionales. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/Interceptors/`](../../src/ArquitecturaBase.Infrastructure/Persistence/Interceptors/AGENTS.md) | Completa campos auditables al guardar y convierte el borrado lógico en modificación. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/Migrations/`](../../src/ArquitecturaBase.Infrastructure/Persistence/Migrations/AGENTS.md) | Contiene migraciones EF, sus metadatos y el snapshot. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/Readers/`](../../src/ArquitecturaBase.Infrastructure/Persistence/Readers/AGENTS.md) | Implementa consultas sin seguimiento, materializadas en filas o respuestas de Application. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/Repositories/`](../../src/ArquitecturaBase.Infrastructure/Persistence/Repositories/AGENTS.md) | Implementa los puertos que cargan entidades para modificar, agregan o quitan filas y toman locks. |
| [`src/ArquitecturaBase.Infrastructure/Persistence/Seed/`](../../src/ArquitecturaBase.Infrastructure/Persistence/Seed/AGENTS.md) | `DatabaseSeeder` coordina roles, ajustes y cliente OpenIddict en una sola transacción. |
| [`src/ArquitecturaBase.Infrastructure/Phones/`](../../src/ArquitecturaBase.Infrastructure/Phones/AGENTS.md) | `LibPhoneNumberParser` implementa `IPhoneNumberParser` para representación canónica, formato y máscara. |
| [`src/ArquitecturaBase.Infrastructure/Security/`](../../src/ArquitecturaBase.Infrastructure/Security/AGENTS.md) | Implementa generación segura de tokens y códigos, y su hash con opciones de configuración. |
| [`src/ArquitecturaBase.ServiceDefaults/`](../../src/ArquitecturaBase.ServiceDefaults/AGENTS.md) | `Extensions.cs` concentra los defaults compartidos de Aspire: OpenTelemetry, descubrimiento de servicios, resiliencia HTTP y endpoints de salud. |
| [`tests/`](../../tests/AGENTS.md) | Domain.UnitTests prueba reglas puras; Application.UnitTests, validación y coordinación; ArchitectureTests, fronteras y convenciones; Api.IntegrationTests, HTTP y adaptadores reales. |
| [`tests/ArquitecturaBase.Api.IntegrationTests/`](../../tests/ArquitecturaBase.Api.IntegrationTests/AGENTS.md) | `ApiFactory` hospeda la Api con PostgreSQL y Redis de Testcontainers. |
| [`tests/ArquitecturaBase.Api.IntegrationTests/Caching/`](../../tests/ArquitecturaBase.Api.IntegrationTests/Caching/AGENTS.md) | Prueba lectura, expiración, payloads, invalidación, leases, concurrencia, fallas y seed. |
| [`tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp/`](../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp/AGENTS.md) | Prueba webhook y flows con Meta simulado, WABA/PhoneNumberId, reintentos, reconciliación, acceso, cuotas y persistencia. |
| [`tests/ArquitecturaBase.Api.IntegrationTests/Support/`](../../tests/ArquitecturaBase.Api.IntegrationTests/Support/AGENTS.md) | Contiene `ApiFactory`, helpers de autenticación y scopes de prueba. |
| [`tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/`](../../tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/AGENTS.md) | Aloja entidades, contexto, controllers y dobles técnicos de escenarios de integración. |
| [`tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/WhatsApp/`](../../tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/WhatsApp/AGENTS.md) | Proveedor/cliente capturador y puertos neutrales para los tests HTTP de WhatsApp, sin red Meta ni tipos BusinessForms. |
| [`tests/ArquitecturaBase.Application.UnitTests/`](../../tests/ArquitecturaBase.Application.UnitTests/AGENTS.md) | Espeja servicios, modelos, validadores y recursos de Application. |
| [`tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp/`](../../tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp/AGENTS.md) | Prueba clasificación entrante, orientación pública, códigos, perfil y canales con dobles de los puertos del módulo. |
| [`tests/ArquitecturaBase.Application.UnitTests/TestFeatures/Modules/WhatsApp/`](../../tests/ArquitecturaBase.Application.UnitTests/TestFeatures/Modules/WhatsApp/AGENTS.md) | Dobles de contratos propios del módulo y datos ficticios, sin proveedores externos. |
| [`tests/ArquitecturaBase.ArchitectureTests/`](../../tests/ArquitecturaBase.ArchitectureTests/AGENTS.md) | Verifica capas, paquetes, ubicación, llamadas, nombres y límites de la arquitectura. |
| [`tests/ArquitecturaBase.Domain.UnitTests/`](../../tests/ArquitecturaBase.Domain.UnitTests/AGENTS.md) | Espeja las áreas de Domain y prueba invariantes, entidades, valores y resultados sin proveedores externos. |
| [`tests/ArquitecturaBase.Domain.UnitTests/Modules/WhatsApp/`](../../tests/ArquitecturaBase.Domain.UnitTests/Modules/WhatsApp/AGENTS.md) | Prueba entidades y reglas puras del módulo, sin proveedores. |

## Carpetas cubiertas por su ámbito padre

El módulo de [IA](../features/inteligencia-artificial.md) agrega estos ámbitos independientes:

| Ámbito | Responsabilidad |
| --- | --- |
| [Domain/Modules/ArtificialIntelligence](../../src/ArquitecturaBase.Domain/Modules/ArtificialIntelligence/AGENTS.md) | Conexión, negocio tipado y conversaciones/turnos privados; estados, revisiones, fencing, consumo de audio, comprobantes, permisos y errores. |
| [Application/Modules/ArtificialIntelligence](../../src/ArquitecturaBase.Application/Modules/ArtificialIntelligence/AGENTS.md) | Aceptación/lectura/ejecución/dispatch/retención del asistente y normalización de audio en el mismo motor; herramientas, evidencia y recursos; catálogo/prueba y Places independientes. |
| [Infrastructure/Modules/ArtificialIntelligence](../../src/ArquitecturaBase.Infrastructure/Modules/ArtificialIntelligence/AGENTS.md) | EF, cifrado externo, adaptadores OpenAI/Anthropic y STT con bytes privados; mismo worker con scopes separados, métricas y mantenimiento; Places con credencial independiente. |
| [Api/Modules/ArtificialIntelligence](../../src/ArquitecturaBase.Api/Modules/ArtificialIntelligence/AGENTS.md) | Contratos HTTP, controller y límites por operador. |
| [Domain.UnitTests/Modules/ArtificialIntelligence](../../tests/ArquitecturaBase.Domain.UnitTests/Modules/ArtificialIntelligence/AGENTS.md) | Invariantes puras. |
| [Application.UnitTests/Modules/ArtificialIntelligence](../../tests/ArquitecturaBase.Application.UnitTests/Modules/ArtificialIntelligence/AGENTS.md) | Coordinación con dobles y paridad de recursos. |
| [Api.IntegrationTests/Modules/ArtificialIntelligence](../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/ArtificialIntelligence/AGENTS.md) | Docker, dos hosts, cifrado y HTTP simulado, sin facturación. |
| [ArchitectureTests/Modules/ArtificialIntelligence](../../tests/ArquitecturaBase.ArchitectureTests/Modules/ArtificialIntelligence/AGENTS.md) | Aporte del lock transaccional. |
| [tests/Shared](../../tests/Shared/AGENTS.md) | Registro de módulos de Application enlazado en los arneses. |

La feature [artificial-intelligence del front](../../../ArquitecturaBaseFront/src/features/artificial-intelligence/AGENTS.md) aporta conexión/prueba, negocio/Places e Inicio asistido, compuesto desde app; no importa settings ni WhatsApp.

Domain/Common, Results y ValueObjects siguen Domain. Application/Configuration y los subgrupos de modelos y validadores siguen su capa y área; las reglas técnicas de Json, Localization, RateLimiting y OpenApi están en Api. Templates, recursos de correo y persistencia de módulos siguen su adaptador o módulo. Los detalles de docs siguen docs/AGENTS.md.

No se crean pares en `bin`, `obj`, `node_modules`, `dist`, `artifacts`, archivos individuales ni carpetas vacías. Un subámbito nuevo se justifica por una responsabilidad o riesgo que el padre no explica suficientemente.

## Mantenimiento

Inicio asistente agrega las fuentes neutrales [Application/Channels/SystemQueries](../../src/ArquitecturaBase.Application/Channels/SystemQueries/AGENTS.md), sus [modelos](../../src/ArquitecturaBase.Application/Models/SystemQueries/AGENTS.md), [errores de Domain](../../src/ArquitecturaBase.Domain/SystemQueries/AGENTS.md) y el [ejecutor técnico por scope](../../src/ArquitecturaBase.Infrastructure/Assistant/AGENTS.md), mediante el puerto `Interfaces/Integrations/Assistant/IAssistantCapabilityExecutor`. Las capacidades del motor se aportan por `Interfaces/Channels/IAssistantCapabilitySource`; los contextos, requisitos y payloads neutrales viven en [Models/Assistant](../../src/ArquitecturaBase.Application/Models/Assistant/AGENTS.md). IClientCapabilitySource conserva sólo su inventario de capacidades del cliente. El módulo IA conserva sus servicios, cifrado, persistencia y [coordinación técnica](../../src/ArquitecturaBase.Infrastructure/Modules/ArtificialIntelligence/Workers/AGENTS.md). La dependencia visual y el ciclo durable se fijan en [ADR0017](../decisions/0017-inicio-asistente-y-operaciones-durables.md); el motor neutral en [ADR0020](../decisions/0020-motor-unico-de-ia-con-capacidades.md).

Cuando agregues un área, adaptador o cambio de responsabilidad, actualizá su guía y este mapa en el mismo desarrollo. Seguí la [receta de instrucciones por carpeta](../guides/instrucciones-por-carpeta.md). La enumeración es un punto de entrada, no un inventario de cada método.

Los dobles de proveedor, probes de transacción/falla de commit y escenarios durables viven en
[TestFeatures/Modules/ArtificialIntelligence](../../tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/ArtificialIntelligence/AGENTS.md),
fuera de producción. Las pruebas E2E usan ambos adaptadores HTTP reales con handlers falsos y
lectores sobre PostgreSQL real; no sustituyen el proveedor con una llamada facturable.

## Entrega transaccional

| Carpeta | Responsabilidad |
| --- | --- |
| [src/ArquitecturaBase.Domain/Delivery](../../src/ArquitecturaBase.Domain/Delivery/AGENTS.md) | Delivery: estado puro de entrega. |
| [src/ArquitecturaBase.Application/Services/Delivery](../../src/ArquitecturaBase.Application/Services/Delivery/AGENTS.md) | Delivery: reserva y finalización. |
| [src/ArquitecturaBase.Application/Models/Delivery](../../src/ArquitecturaBase.Application/Models/Delivery/AGENTS.md) | Delivery: respuesta interna. |
| [src/ArquitecturaBase.Infrastructure/Delivery](../../src/ArquitecturaBase.Infrastructure/Delivery/AGENTS.md) | Delivery: persistencia técnica y consumo. |

El panel de Inicio del 2026-10-05 amplía la lectura del módulo IA: ReadPolicy/ResponsePolicy autorizan títulos, ConversationTitlePolicy normaliza grafemas y AiAssistantReader proyecta sólo el primer turno. [Regla funcional](../features/inteligencia-artificial.md#panel-interno-e-identificación-del-historial--2026-10-05); árbol y capacidad pendiente en el [plan](../plans/2026-10-04-panel-conversaciones-inicio.md).

## Formularios: autoría y publicación durable (actualización 2026-10-10)

| Ámbito | Responsabilidad |
| --- | --- |
| [Domain/Modules/BusinessForms](../../src/ArquitecturaBase.Domain/Modules/BusinessForms/AGENTS.md) | Identidades, snapshots, campos/versiones/operaciones, registros/valores/recibos, activación CAS y aprobación de catálogos propuestos; entidades auditables. |
| [Application/Modules/BusinessForms](../../src/ArquitecturaBase.Application/Modules/BusinessForms/AGENTS.md) | Autoría localizada, publicación, preview, catálogos Manual/Filters y captura/lectura por puertos neutrales; permisos actuales. |
| [Infrastructure/Modules/BusinessForms](../../src/ArquitecturaBase.Infrastructure/Modules/BusinessForms/AGENTS.md) | EF de formularios, catálogos Manual/Filters, registros y recibos, lectores SQL, locks/protección y workers de publicación. |
| [Api/Modules/BusinessForms](../../src/ArquitecturaBase.Api/Modules/BusinessForms/AGENTS.md) | Diez rutas MVC de autoría/chequeo/aceptación/status/preview, cinco de Catálogos Manual y cuatro de Filters; permisos, HTTP/OpenAPI, rate limits y no-store. |
| [Application.UnitTests/Modules/BusinessForms](../../tests/ArquitecturaBase.Application.UnitTests/Modules/BusinessForms/AGENTS.md) | Validación, recursos y captura/lectura con dobles neutrales. |
| [Api.IntegrationTests/Modules/BusinessForms](../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/AGENTS.md) | Persistencia/carreras/restore/publicación, catálogos Manual/Filters y captura tipada/idempotente con PostgreSQL/Redis reales. |
| [ArchitectureTests/Modules/BusinessForms](../../tests/ArquitecturaBase.ArchitectureTests/Modules/BusinessForms/AGENTS.md) | Aporte preciso de locks sin relajar detectores. |
| [ArchitectureTests/Modules/WhatsApp](../../tests/ArquitecturaBase.ArchitectureTests/Modules/WhatsApp/AGENTS.md) | Convenciones de logging, límites y composición propios del módulo. |
| [TestFeatures/Modules/BusinessForms](../../tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/BusinessForms/AGENTS.md) | Protector con fallo técnico y canal listo ficticio; composición exclusiva para rollback/aceptación sin red. |

Cada ámbito conserva CLAUDE.md que importa @AGENTS.md. Los valores puros se prueban bajo Domain.UnitTests. [Regla funcional](../features/formularios.md) y [resumen por etapa](../plans/formularios/README.md). El estado vigente del envío figura debajo; los recortes 2a/2b conservan su alcance histórico.

El incremento 2a de formularios (2026-10-10) agrega [Models/StructuredData](../../src/ArquitecturaBase.Application/Models/StructuredData/AGENTS.md), DTOs neutrales del chequeo; Interfaces/Channels aporta IStructuredFormChannel y WhatsApp su compilador/codec puro. BusinessForms coordina la lectura y la comparación de contenido guardado; no acepta ni publica. [TestFeatures/Channels](../../tests/ArquitecturaBase.Application.UnitTests/TestFeatures/Channels/AGENTS.md) contiene sólo el doble de composición. Los ámbitos WhatsApp y BusinessForms existentes describen sus responsabilidades y pruebas nuevas.
Actualización 2b mínima (2026-10-10): los ámbitos BusinessForms existentes abarcan servicios separados de chequeo/aceptación/status, registros Field/Version/PublicationOperation, protección de ticket/manifiesto y persistencia propia. Conservan sus AGENTS/CLAUDE y fronteras de módulo; el transporte externo todavía no es parte de este corte.

Actualización 2b (2026-10-10): los ámbitos BusinessForms abarcan versiones/operaciones, Dispatch/Preview y worker separado por scopes; listado/facetas SQL y diff sellado. StructuredData y Channels tienen consumidores Accept/Status/Preview reales. Los ámbitos WhatsApp abarcan operaciones/bindings propios, provider/environment/protector, Dispatch/Status y worker Flows; TestFeatures contiene handler/proveedor simulado. Los AGENTS existentes consolidan la responsabilidad, sin nuevas referencias entre módulos ni envío/captura.

Mínimo2 de publicación: los ámbitos BusinessForms existentes proyectan activa/última operación y recuperación privada en detalle, estado real del listado conservando facetas de activa y preview con query culture es/en. Contracts del módulo incluye FormPreviewQuery; los tests de integración verifican revocación, recarga, idiomas y caducidad sin URL. Sin ámbitos ni dependencias nuevos.

La presentación inmutable de versiones vive en Domain/Modules/BusinessForms y sus columnas SQL calculadas en Infrastructure/Modules/BusinessForms/Persistence/Configurations. Su backfill desde manifiestos protegidos pertenece a Persistence/Seed del módulo, registrado por su InfrastructureRegistration y coordinado por el puerto técnico IDatabaseSeedParticipant del seed del núcleo. WhatsAppContact conserva LinkRevision en Domain del módulo para detectar cambios efectivos y ABA de vínculo.

- Selección de formularios estructurados: puerto IStructuredFormSource/Models.StructuredData en Application, fallback en Channels, adaptador BusinessForms.Channels y lector propio de versión activa sellada. Consumidor WhatsAppFormService en el módulo WhatsApp; identidad/revisión del destinatario resueltas por su lector, sin dependencia directa entre módulos.

Cierre del paso 3 (2026-10-11): los ámbitos WhatsApp existentes abarcan sesión de Flow, cola tipada, revalidación del envío y presentación histórica. TestFeatures/Modules/WhatsApp contiene el proveedor simulado y el host opcional para recorrer el SPA contra API/PostgreSQL/Redis reales; las credenciales de prueba sólo viven en Temp y se eliminan al cerrar. Los contratos HTTP exportados prueban la integración con el consumidor frontend. Este arnés no acredita publicación ni recepción física con Meta.

Recepción del mínimo 3 (2026-10-11): webhook protege nfm_reply y el worker valida token/sesión/LinkRevision antes del sink. El codec usa mapping sellado y puertos StructuredRecord conectan el canal opcional con registros/valores de BusinessForms. Tests HTTP conservan puertos neutrales; un test real de BusinessForms verifica captura/recibo/FK en PostgreSQL. WABA/PhoneNumberId se comprueba antes de cada efecto. El piloto físico del 2026-10-11 comprobó publicación, entrega y recepción numerada; alcance y pendientes en la fuente funcional.

Cierre de formularios: el contexto puro de envío vive en Models de WhatsApp; [los dobles propios de BusinessForms](../../tests/ArquitecturaBase.Application.UnitTests/TestFeatures/Modules/BusinessForms/AGENTS.md) permanecen dentro de su módulo de tests. Las reglas de dependencias y fronteras no agregan excepciones.

Catálogos Manual K1–K3 (2026-10-12): los ámbitos BusinessForms existentes incluyen los cinco contratos HTTP de lista, alta, detalle, edición y opciones; Application define el delta y los movimientos, Infrastructure conserva unicidad diferida y orden persistido, y los tests de integración verifican las rutas contra PostgreSQL/Redis reales. No se agrega un ámbito, permiso ni dependencia entre módulos.

K7 amplía los ámbitos existentes de StructuredData, BusinessForms y WhatsApp: preparación neutral por destinatario, oferta protegida en sesión/cola y referencias históricas con captura atómica. Sus AGENTS.md delimitan locks, privacidad y las pruebas HTTP exportadas; no se incorpora un módulo ni caché nuevo.

El arnés de composición de catálogos utiliza `TestFeatures/IStructuredFormPublicationDriver` bajo el AGENTS de TestFeatures: un puerto neutral de tests, implementado por el módulo dueño para avanzar su worker real por operación. No crea una dependencia de producción entre módulos ni activa workers sobre lotes compartidos.

Archivos privados (Etapa 2B): [puerto de contenido](../../src/ArquitecturaBase.Application/Interfaces/Integrations/Files/AGENTS.md) y [adaptador Azure Blob](../../src/ArquitecturaBase.Infrastructure/Files/AGENTS.md). El consumidor conserva permisos y metadatos; Azurite prueba almacenamiento sin credenciales reales.

[Domain/Files](../../src/ArquitecturaBase.Domain/Files/AGENTS.md) conserva metadatos auditables y errores neutrales; [Models/Files](../../src/ArquitecturaBase.Application/Models/Files/AGENTS.md) transporta filas y resultados privados. La referencia desde cada consumidor se confirma con esos metadatos, sin retención prestada de un canal.

- `Application/Services/Files`: limpieza neutral de blobs sin metadata ni referencia durable (`src/ArquitecturaBase.Application/Services/Files/AGENTS.md`).

- `Api.IntegrationTests/TestFeatures/Files`: Azurite privado y arnés de bytes reales (AGENTS.md local).

Etapa 3: los ámbitos BusinessForms de Application/Api/Infrastructure y sus tests incluyen planilla, facetas SQL, detalle y exportación XLSX. `Interfaces/Channels` y `Models/StructuredData` definen el origen neutral por lote; WhatsApp aporta su adaptador y lector. La descarga y los tests técnicos de ClosedXML pertenecen a Infrastructure/IntegrationTests. `TestFeatures` conserva sólo el capturador HTTP neutral y el runner de exportación.

Etapa 4, G1: los ámbitos BusinessForms existentes incluyen reglas con revisiones propias y permiso formrules.manage. Domain conserva el puntero independiente del borrador; Application valida condiciones/acciones e idioma original; Infrastructure guarda revisiones y pertenencia; Api expone actual, guardado, historial y opciones con el inventario y pruebas del módulo.

Etapa 4, G5: los ámbitos BusinessForms existentes amplían reglas con el motor único ActionRuns, reserva/efecto/confirmación por scopes y metadata de documentos privados. Los puertos neutrales de avisos permanecen en Interfaces/Channels y Models/StructuredData; WhatsApp aporta el canal y BusinessForms el renderizado PDF, sin segunda cola.

G3/G4 incorpora tareas y decisión de aprobación en las guías existentes de BusinessForms de Domain, Application, Infrastructure y Api; no crea otra cola ni un área del núcleo.

G2 agrega [renderizado PDF](../../src/ArquitecturaBase.Infrastructure/Modules/BusinessForms/Pdf/AGENTS.md) y conserva copias pendientes mediante el puerto neutral de [Channels](../../src/ArquitecturaBase.Application/Interfaces/Channels/AGENTS.md), sin dependencias del núcleo hacia módulos.

G6 amplía los ámbitos WhatsApp existentes con el adaptador del canal neutral, salidas de texto/documento idempotentes por run/destinatario, transporte de medios y origen FormAction. Los tests HTTP y sus capturadores pertenecen al módulo; el exportador de BusinessForms agrupa seis cohortes con procedencia y hashes de una misma corrida, sin generar cuerpos ni declarar entrega física.

La lectura viva del historial reutiliza los ámbitos existentes de BusinessForms, WhatsApp, Channels y StructuredData. FormWorkflowReadPolicy consume observación neutral por lote; WhatsAppFormActionReader une sólo salidas y receipts locales. Las guías del dominio delimitan las dos fechas reales y las de integración la migración aditiva, sin ámbitos ni dependencias nuevos.

El puerto productivo IAssistantOperations vive en Interfaces/Channels y usa Models/Assistant; su ausencia se declara mediante los [errores neutrales de operaciones](../../src/ArquitecturaBase.Domain/Assistant/AGENTS.md). El adaptador IA reutiliza la aceptación y el worker del motor único; el dueño del canal conserva su autorización y llama fuera de transacciones ajenas.

Etapa 5, captura de procedencia: los ámbitos existentes de StructuredData, BusinessForms y sus pruebas conservan origen manual/asistente, propuesta y selecciones aceptadas, cambios y vínculos de sugerencia al confirmar la respuesta. Application valida y registra dentro del mismo límite; Domain sella el hecho una vez; Infrastructure guarda la referencia al origen y la evidencia, sin consultar luego la configuración de IA.

H4 amplía los ámbitos existentes de Channels, StructuredData y BusinessForms: capacidades readonly de metadata/propuesta Filters, aceptación neutral, evidencia original y aprobación humana. Api e IntegrationTests mantienen las dos decisiones HTTP y capturas del contrato; la creación y cada decisión usan una única UoW del dueño, sin un worker nuevo.

Etapa 5 amplía el ámbito WhatsApp de Domain con salidas Assistant sin operador y QuickReplies tipadas. La reserva y el comienzo del POST reutilizan las transiciones de la cola FormAction; las pruebas puras conservan aislamiento por origen y recuperación sin repetir un efecto incierto.
