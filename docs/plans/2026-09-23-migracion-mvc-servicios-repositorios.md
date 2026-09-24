# Migración a MVC, servicios y repositorios — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `subagent-driven-development` or `executing-plans` to implement this plan task by task. Mark each completed step with `- [x]`.

**Goal:** Convertir `ArquitecturaBase` a `Controller MVC → servicio de Application con interfaz → repositorio/lector con interfaz → ApplicationDbContext`, conservando las funcionalidades existentes y la integración con el frontend.

**Architecture:** Mantener los proyectos `Domain`, `Application`, `Infrastructure`, `Api`, `AppHost` y `ServiceDefaults`. Migrar una superficie funcional por vez dentro de esos proyectos. Los controllers solo adaptan HTTP; los servicios coordinan reglas, validación y unidad de trabajo; los repositorios y lectores de Infrastructure son la frontera de persistencia. Los adaptadores técnicos de Identity, OpenIddict, SMTP y WhatsApp siguen siendo infraestructura.

**Tech Stack:** .NET 10, ASP.NET Core MVC, FluentValidation, ASP.NET Core Identity, OpenIddict, EF Core/Npgsql, PostgreSQL, Aspire, xUnit/Testcontainers.

---

## Estado y decisión sobre crear otro proyecto

**Estado:** plan de diseño para revisión; todavía no se modifica el código de la aplicación. Todo está en desarrollo: no hay publicación ni datos productivos. La base local y sus datos se pueden borrar y recrear. El plan de ingreso por WhatsApp tiene trabajo de las tareas 13–18 en curso. Al terminarlo, actualizar el inventario de rutas, handlers, consumidores y tests antes de ejecutar la primera tarea de esta migración. El árbol de trabajo contiene cambios de esa tarea; este documento no los altera.

### Puertas de avance

- **Inicio:** tareas 13–18 cerradas en backend **y** frontend, ambos working trees limpios y sus commits base compatibles anotados. Registrar `dotnet build ArquitecturaBase.slnx` y `dotnet test` verdes en backend, y `npm run build`, `npm run lint`, `npm run test` verdes en `ArquitecturaBaseFront`, además de la matriz de contrato con cobertura verificable para cada verbo/ruta. Si el baseline ya falla, resolverlo antes de atribuir una falla al refactor.
- **Viabilidad MVC:** antes de migrar áreas grandes, probar en el arnés de integración el passthrough de OpenIddict y la ausencia de rutas WhatsApp al deshabilitar la función. Si alguna de estas dos pruebas no se puede reproducir con MVC, detener la ejecución y revisar el diseño; no convertir el resto esperando resolverlo al final.
- **Cada área:** servicio y controller nuevos pasan sus tests, el diff de rutas no muestra duplicados ni pérdidas, y la suite de esa área está verde antes de retirar la implementación vieja. Si falla, no avanzar a otra área; corregir o revertir el commit de esa área.
- **Retiro del pipeline:** antes de eliminar `AddFeaturesFromAssembly`, comprobar que todos los `IValidator<T>` se siguen resolviendo por DI, que los servicios agregan/traducen errores igual que `ValidationDecorator` y que los logs de inicio, resultado y código de error de `LoggingDecorator` tienen reemplazo explícito. Un request inválido no debe ejecutar lógica ni guardar cambios.
- **Cierre:** suite completa de backend y frontend, pruebas manuales de ingreso/OIDC/webhook y revisión final de rutas, DI, dependencias y contratos. La ausencia de errores en compilación por sí sola no cierra la migración.

Se recomienda **usar la solución y los proyectos actuales**, reescribiendo `Api` y `Application` por áreas con la arquitectura acordada. La base descartable elimina cualquier tarea de traslado de datos o compatibilidad con el historial de migraciones. Lo que lleva tiempo en un proyecto nuevo es volver a conectar el frontend, Identity, OpenIddict, el webhook, los workers, AppHost y las pruebas: `Api` ya reúne esas piezas. La reescritura interna permite probar cada funcionalidad antes de retirar su endpoint y handler anteriores. No se agrega otro repositorio ni otro `.csproj` por este cambio de arquitectura.

| Opción | Ventaja | Costo y riesgo |
|---|---|---|
| Misma solución y proyectos, reescritura por áreas (**recomendada**) | Reutiliza Domain, persistencia, host y pruebas; permite comprobar cada caso de uso al cambiarlo. | Durante la transición conviven controllers y endpoints de áreas distintas; ninguna ruta puede mapearse dos veces. |
| Proyectos `Api`/`Application` MVC nuevos en la misma solución | Permite escribir las capas nuevas en archivos separados y cambiar de host al final. | Duplica temporalmente DI, Identity, OpenIddict y pruebas; después hay que reconectar AppHost y frontend y hacer el corte completo. |
| Solución o repositorio nuevos desde cero | Permite replantear toda la estructura sin código previo. | Obliga a reconstruir y volver a verificar autenticación, OIDC, SPA, webhook, workers, DI y pruebas antes de recuperar todas las funcionalidades. |

Esta recomendación se basa en el trabajo de integración y de negocio que ya funciona, no en conservar la base de datos. Si una implementación vieja estorba, se puede reemplazar dentro del proyecto actual sin copiar su diseño.

## Reglas que deberá cumplir el resultado

1. Cada ruta HTTP de negocio vive en un controller MVC. El controller inyecta una interfaz de servicio, no `ApplicationDbContext`, `UserManager`, `IQueryable` ni un repositorio. La adaptación de protocolo de OpenIddict y la verificación de la firma sobre bytes crudos del webhook pueden usar helpers de presentación sin lógica de persistencia.
2. Los servicios y sus interfaces viven en `Application`; no referencian EF Core ni ASP.NET Core. Devuelven `Result`/`Result<T>` y orquestan los contratos de persistencia y de servicios externos. Cada método conserva el comportamiento de validación, auditoría y guardado del caso de uso que reemplaza.
3. Los contratos de repositorio/lector viven en `Domain` cuando operan sobre agregados de dominio, o en `Application/Abstractions/Persistence` cuando devuelven proyecciones de Application o representan Identity. Las implementaciones viven en `Infrastructure`. No se expone `IQueryable`, `ApplicationUser` ni `ApplicationRole` fuera de Infrastructure.
4. Las consultas y escrituras de negocio a EF pasan por repositorios o lectores especializados. `SystemSettingsReader` ya es uno de esos lectores. `UnitOfWork`, migraciones, seeders, health checks y los stores de Identity/OpenIddict/DataProtection conservan acceso técnico a `ApplicationDbContext` dentro de Infrastructure.
5. DI registra explícitamente controllers, servicios, repositorios y lectores. Al terminar, no quedan `IEndpoint`, handlers `ICommandHandler`/`IQueryHandler` ni decoradores Scrutor en la aplicación. Los workers de WhatsApp consumen servicios de Application, no handlers.
6. Por defecto se conservan método y ruta HTTP, status, JSON, `ProblemDetails` traducido, permisos, rate limit, cookies, redirecciones y contrato del frontend. Una diferencia deliberada se implementa junto con el cambio correspondiente en el frontend y sus pruebas. No hay que preservar datos, sesiones ni historial de migraciones de desarrollo; sí debe poder crearse una base vacía y arrancar con esquema, seed y flujos de ingreso funcionales.

## Mapa de archivos objetivo

| Área | Entrada MVC en `Api/Controllers/` | Servicio en `Application` | Persistencia o proveedor |
|---|---|---|---|
| Ajustes | `SettingsController.cs` | `Abstractions/Services/ISystemSettingsService.cs`, `Services/SystemSettingsService.cs` | `ISystemSettingsRepository`, `ISystemSettingsReader` existentes |
| Usuarios | `UsersController.cs` | `IUserService`, `UserService` | Repositorio/lector de usuarios, más adaptador de operaciones Identity |
| Perfil | `MeController.cs` | `IProfileService`, `ProfileService` | Repositorios/lectores de usuarios, códigos e historial existentes |
| Roles y permisos | `RolesController.cs`, `PermissionsController.cs` | `IRoleService`, `RoleService` | Repositorio/lector de roles y permisos |
| Códigos y métodos de ingreso | `LoginCodeController.cs`, `LoginMethodsController.cs` | `IAccountService`, `AccountService` | Repositorios de códigos y auditoría existentes; proveedor de identidad |
| Enlaces de ingreso | `LoginLinkController.cs` | `ILoginLinkService`, `LoginLinkService` | `ILoginLinkRepository` existente |
| Google y OIDC | `ExternalLoginController.cs`, `ConnectController.cs` | Servicio de autenticación y adaptadores de protocolo | Identity/OpenIddict en Infrastructure, sin tipos HTTP en Application |
| WhatsApp | `WhatsAppWebhookController.cs` | `IWhatsAppWebhookService`, `IWhatsAppInboundService`, `IWhatsAppMessageHistoryService`, `IWhatsAppRetentionService` y sus implementaciones | Repositorios de contactos/mensajes existentes; repositorio de retención; adaptador de scope para reintento del webhook; cliente Meta y workers en Infrastructure |

Los nombres y métodos concretos se congelan después de la Tarea 18 de WhatsApp: la Tarea 13 está agregando operaciones de perfil y la 15 agregará invitaciones. Este mapa es la estructura objetivo, no un conteo cerrado de acciones.

## Tarea 0: Congelar comportamiento y crear una salida segura

**Archivos:**
- Revisar: `docs/plans/2026-09-22-ingreso-whatsapp.md`, `CLAUDE.md`, `src/ArquitecturaBase.Api/Endpoints/`, `src/ArquitecturaBase.Application/Features/`.
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Contracts/HttpContractTests.cs`.
- Ampliar: `tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs` solo si los tests de contrato necesitan datos comunes.

- [ ] Terminar las tareas 13–18 de WhatsApp y confirmar que los árboles de backend y frontend están limpios y corresponden al mismo estado funcional. Registrar ambos commits base. No incluir cambios de otro trabajo en los commits de esta migración.
- [ ] Enumerar **cada combinación de verbo y ruta** de `Api/Endpoints` y los métodos que llama; registrar autorización, rate limit, content type, status, cuerpo, headers y condiciones de registro. Incluir `/connect`, `/account`, `/api`, `/webhooks` y los endpoints técnicos generados por OpenIddict/Aspire. Inventariar también todos los hosted services y cada acceso de negocio directo a `ApplicationDbContext`, incluido el servicio de retención que agregará la Tarea 17.
- [ ] Construir una matriz verificable que vincule **cada verbo/ruta** con una prueba HTTP existente o nueva; crear pruebas donde falten. Agregar casos para JSON inválido/ausente, query y GUID inválidos, 401/403/404/405/429, formato de error, `traceId`, localización, enum como texto y fechas UTC. Guardar el listado de endpoints antes del refactor y compararlo después de cada familia: mismos verbos/rutas, salvo diferencias deliberadas documentadas.
- [ ] Capturar baseline: `dotnet build ArquitecturaBase.slnx` y `dotnet test` en backend; `npm run build`, `npm run lint` y `npm run test` en `../ArquitecturaBaseFront`. Los tests de integración requieren Docker. Registrar resultados y fallas previas, si las hubiera.
- [ ] Registrar cómo recrear una base de desarrollo vacía y verificar que arranca con el esquema y el seed necesarios. No planificar respaldo, migración de datos ni preservación de migraciones históricas; si el modelo cambia, se puede simplificar o rehacer la migración inicial y recrear la base.
- [ ] Mantener un commit por área que pase su suite. Si una etapa falla, corregirla o revertir ese commit antes de migrar otra área. No dejar rutas duplicadas entre endpoint y controller.

## Tarea 1: Fijar límites de arquitectura y composición MVC

**Archivos:**
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/LayerDependencyTests.cs`.
- Crear: `tests/ArquitecturaBase.ArchitectureTests/ControllerServiceRepositoryTests.cs`.
- Modificar: `src/ArquitecturaBase.Api/DependencyInjection.cs`, `src/ArquitecturaBase.Api/Program.cs`.
- Crear: `src/ArquitecturaBase.Api/ErrorHandling/ControllerResultExtensions.cs`.
- Crear: `src/ArquitecturaBase.Application/Common/Validation/ServiceRequestValidator.cs` y `tests/ArquitecturaBase.Application.UnitTests/Services/ServiceValidationTests.cs`.
- Probar: `tests/ArquitecturaBase.Api.IntegrationTests/FrameworkErrorsTests.cs`, `ErrorHandlingTests.cs`, `ValidationProblemTests.cs`, `LocalizationTests.cs`, `OpenApiTests.cs`.

- [ ] Escribir pruebas de arquitectura para prohibir EF y `ApplicationDbContext` en `Api`/`Application`, repositorios directos en controllers y tipos de ASP.NET Core en servicios de Application. Mantener las reglas de dependencias existentes.
- [ ] Crear primero una prueba HTTP de equivalencia de `Result`/`Result<T>` para 200, 204 y cada familia de `ProblemDetails`; hacerla fallar con el adaptador MVC aún ausente.
- [ ] Probar temprano, en controllers de prueba del arnés y sin sustituir rutas existentes, que MVC puede conservar el passthrough de `/connect` (sign-in, challenge, forbid, redirect y POST de formulario) y que una convención de startup puede omitir de verdad las acciones WhatsApp cuando la función está apagada. Si falla un spike, detener el plan y rediseñar ese borde antes del piloto de Ajustes.
- [ ] Implementar el adaptador MVC usando `ProblemDetailsMapper`, conservando status, `code`, `traceId`, errores de validación e idioma. No cambiar `ResultExtensions` mientras existan Minimal APIs.
- [ ] Separar el registro de FluentValidation del escaneo de handlers en `Application/DependencyInjection.cs`. Implementar validación reusable en servicios que invoque **todos** los `IValidator<TRequest>` registrados, agrupe errores, traduzca mensajes, conserve nombres de campo camelCase y corte antes de mutar/guardar. Probar múltiples validadores, mensaje traducido, campo anidado y cero llamadas a repositorio/UoW ante entrada inválida.
- [ ] Definir logging en el límite de cada método de servicio con `ILogger<TService>` o un helper de Application: inicio, resultado y código de error, sin datos sensibles. Migrar los tests útiles de `LoggingDecoratorTests` a este comportamiento antes de retirar el decorador.
- [ ] Registrar `AddControllers().AddJsonOptions(...)` con los mismos `UtcDateTimeConverter` y `JsonStringEnumConverter` que hoy se aplican con `ConfigureHttpJsonOptions`. Configurar las respuestas de binding/model state para conservar `Request.Invalid`; `RouteHandlerOptions.ThrowOnBadRequest` no configura MVC.
- [ ] Mapear controllers en `Program.cs`, manteniendo `MapEndpoints()` para las áreas aún no migradas. Verificar que no aparece ninguna ruta nueva o duplicada.
- [ ] Correr los tests de error, localización, OpenAPI y arquitectura. Confirmar manualmente que `AddControllers` no cambia el fallback del SPA ni los endpoints de Aspire/OpenIddict.

## Tarea 2: Piloto completo con ajustes del sistema

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Abstractions/Services/ISystemSettingsService.cs`, `src/ArquitecturaBase.Application/Services/SystemSettingsService.cs`, `src/ArquitecturaBase.Api/Controllers/SettingsController.cs`.
- Modificar: `src/ArquitecturaBase.Application/DependencyInjection.cs`, `src/ArquitecturaBase.Api/DependencyInjection.cs`.
- Eliminar al final de la tarea: `src/ArquitecturaBase.Api/Endpoints/Settings/SettingsEndpoints.cs`, los dos handlers de `Application/Features/Settings/` y sus comandos/consultas solo si dejaron de usarse.
- Probar: `tests/ArquitecturaBase.Api.IntegrationTests/Settings/SettingsEndpointsTests.cs`, `SystemSettingsReaderTests.cs`, `SystemSettingsSeedTests.cs`; crear `tests/ArquitecturaBase.Application.UnitTests/Services/SystemSettingsServiceTests.cs`.

- [ ] Escribir pruebas del servicio para `Get` y `Update`: `SettingsErrors.NotFound`, `RegistrationMode`, validación, guardado una sola vez en éxito e invalidación del caché después del commit. Agregar un test donde falla el guardado y el valor cacheado sigue disponible; esto corrige el riesgo de invalidación prematura actual.
- [ ] Mover la lógica de los dos handlers al servicio con interfaz. Para escritura, validar antes de mutar, usar `IUnitOfWork.SaveChangesAsync` explícitamente y llamar `ISystemSettingsReader.InvalidateAsync` solo tras un guardado exitoso.
- [ ] Registrar `ISystemSettingsService → SystemSettingsService` con lifetime scoped. Registrar el controller con `GET/PUT /api/settings` y política `settings.manage`; no inyectar repositorios en el controller.
- [ ] Retirar el mapeo Minimal API de ajustes en el mismo cambio que habilita el controller. Comprobar que solo existe una acción por método/ruta y que los tests de ajustes, permisos, caché y contrato HTTP siguen verdes.
- [ ] Si el piloto revela una diferencia entre MVC y Minimal API, resolverla en la configuración común antes de seguir con otras áreas.

## Tarea 3: Encapsular persistencia de usuarios, roles y permisos

**Archivos:**
- Crear contratos en `src/ArquitecturaBase.Application/Abstractions/Persistence/`: `IUserRepository.cs`, `IUserReader.cs`, `IRoleRepository.cs`, `IRoleReader.cs`, `IPermissionReader.cs` (ajustar responsabilidades exactas al inventario posterior a WhatsApp).
- Crear implementaciones en `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/` y `Readers/` con los mismos nombres sin `I`.
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`, `PermissionService.cs`, `src/ArquitecturaBase.Infrastructure/DependencyInjection.cs`.
- Probar: `tests/ArquitecturaBase.Api.IntegrationTests/Identity/IdentityServiceTests.cs`, `PermissionServiceTests.cs`, `tests/ArquitecturaBase.Api.IntegrationTests/Users/`, `Roles/`.

- [ ] Para cada consulta directa de `IdentityService` y `PermissionService`, escribir un test que capture filtros, soft delete, orden estable, paginado, normalización, proyección y caché actuales.
- [ ] Extraer consultas EF de usuarios, roles, logins y permisos a lectores especializados. Las interfaces no retornan `IQueryable`, `ApplicationUser` ni `ApplicationRole`.
- [ ] Encapsular escrituras de usuarios/roles en repositorios o adaptadores de Identity. Mantener separados los actos de sesión, cookie, seguridad y revocación de OpenIddict. No crear un repositorio genérico `IRepository<T>`.
- [ ] Mantener `ISystemSettingsReader` como lector especializado. Dejar `UnitOfWork`, migraciones, seeders, health checks y stores del framework como infraestructura técnica; no crear repositorios ficticios para ellos.
- [ ] Verificar transacciones antes de mover la responsabilidad del commit: `UserManager`/`RoleManager` autoguardan. Para cada flujo que combine Identity con códigos, links, auditoría, permisos o invitaciones, escribir un test de fallo intermedio, definir el límite de transacción compartida sobre el mismo `ApplicationDbContext` y comprobar rollback o el guardado en error que corresponda. No asumir que el `SaveChanges` final vuelve atómicas las operaciones de Identity.
- [ ] Registrar cada contrato e implementación scoped en `Infrastructure/DependencyInjection.cs`. Ejecutar pruebas de Identity, permisos, usuarios y roles antes de migrar controllers de esas áreas.

## Tarea 4: Usuarios, perfil, roles y permisos

**Archivos:**
- Crear: `Api/Controllers/UsersController.cs`, `MeController.cs`, `RolesController.cs`, `PermissionsController.cs` bajo `src/ArquitecturaBase.Api/`.
- Crear interfaces en `src/ArquitecturaBase.Application/Abstractions/Services/`: `IUserService.cs`, `IProfileService.cs`, `IRoleService.cs`.
- Crear implementaciones en `src/ArquitecturaBase.Application/Services/`: `UserService.cs`, `ProfileService.cs`, `RoleService.cs`.
- Modificar: `src/ArquitecturaBase.Application/DependencyInjection.cs`.
- Retirar, tras paridad: `Api/Endpoints/Users/UsersEndpoints.cs`, `MeEndpoint.cs`, `Api/Endpoints/Roles/RolesEndpoints.cs` y los handlers de `Application/Features/Users/` y `Roles/` reemplazados.
- Probar: `tests/ArquitecturaBase.Api.IntegrationTests/Users/`, `Roles/`, `PaginationTests.cs`, `Identity/PermissionServiceTests.cs` y tests unitarios nuevos en `Application.UnitTests/Services/`.

- [ ] Inventariar los casos de uso definitivos después de las tareas 13 y 15 de WhatsApp; añadir todos los métodos a `IUserService`/`IProfileService`/`IRoleService`, sin olvidar invitaciones o vinculación de teléfono/correo.
- [ ] Escribir tests de servicio primero para reglas de usuario/rol: cuenta borrada, último Admin, permisos, filtros y conteos, concurrencia e invalidación de permisos. Para perfil, probar expresamente que `ConfirmEmail` y `ConfirmPhoneLink` consumen/intentan guardar los cambios exigidos aun cuando la verificación o la unicidad devuelve error; ambos usan `IPersistChangesOnFailure` en la Tarea 13.
- [ ] Mover lógica de handlers a servicios, conservando los helpers de negocio útiles (`UserGuards`, etc.) o integrándolos sin duplicación. El servicio coordina validadores y `IUnitOfWork`; las consultas no guardan y cada escritura conserva su condición de guardado, incluidos los dos errores de perfil anteriores.
- [ ] Crear controllers con rutas y permisos idénticos. Mantener `/{id:guid}`, defaults de paginado, filtros y cuerpos JSON. Cambiar una familia de rutas por commit: primero usuarios, luego perfil, luego roles/permisos.
- [ ] Después de cada familia, ejecutar tests de esa área y el contrato HTTP. Eliminar los handlers/endpoint viejos de esa familia solo cuando los tests nuevos y existentes pasen.

## Tarea 5: Ingreso por código, enlaces y proveedor externo

**Archivos:**
- Crear controllers `LoginCodeController.cs`, `LoginMethodsController.cs`, `LoginLinkController.cs`, `ExternalLoginController.cs` en `src/ArquitecturaBase.Api/Controllers/`.
- Crear `IAccountService.cs`, `ILoginLinkService.cs` en `Application/Abstractions/Services/` y sus implementaciones en `Application/Services/`.
- Modificar: `src/ArquitecturaBase.Application/DependencyInjection.cs`, los adaptadores de Identity necesarios y `src/ArquitecturaBase.Api/DependencyInjection.cs`.
- Retirar tras paridad: `Api/Endpoints/Account/` y handlers sustituidos en `Application/Features/Auth/`.
- Probar: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/` (código, link, Google, rate limits, concurrencia, registro e ingreso), `Identity/IdentityServiceTests.cs`.

- [ ] Escribir tests para los flujos donde un error **sí** debe persistir intento, auditoría o consumo: verificación de código, canje de enlace e ingreso externo. Esos casos hoy implementan `IPersistChangesOnFailure` y no pueden convertirse en un `return` sin guardar.
- [ ] Migrar código y enlaces a servicios con validación explícita antes de la operación; conservar locks, cuotas, expiración, idioma, emisión y respuesta HTTP.
- [ ] Mantener la lógica de `Challenge`, callback y redirección Google en el controller/adaptador de presentación; el servicio de Application decide reglas de cuenta sin depender de `HttpContext`.
- [ ] Cambiar endpoints por controllers en familias separadas, preservando rutas `/account/*`, status, JSON, cookies y las políticas `login-code`/`login-verify`.
- [ ] Ejecutar tests de autenticación completos y el contrato HTTP después de cada familia.

## Tarea 6: OpenIddict y `/connect`

**Archivos:**
- Crear: `src/ArquitecturaBase.Api/Controllers/ConnectController.cs` y helpers de protocolo necesarios dentro de `Api`.
- Conservar o adaptar: `src/ArquitecturaBase.Api/Endpoints/Connect/OpenIdPrincipalFactory.cs`, `OpenIddictResults.cs` según la prueba de paridad.
- Retirar tras paridad: `Api/Endpoints/Connect/{Authorize,Token,Logout,UserInfo}Endpoint.cs`.
- Probar: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/{ConnectFlowTests,OpenIddictServerTests,AuthenticationRegistrationTests}.cs`, `OpenApiTests.cs`, tests de contratos HTTP.

- [ ] Antes de sustituir rutas, escribir una prueba pequeña de passthrough OpenIddict con un controller MVC: GET/POST authorize, POST token con formulario, GET/POST logout/userinfo, challenge, sign-in, forbid y redirecciones. No asumir que un `IResult` de Minimal API conserva su comportamiento al devolverlo desde MVC.
- [ ] Conservar issuer, PKCE, scopes, cookies, revocación de tokens, carga de principal y ausencia de permisos en el token. Si MVC necesita un `IActionResult` adaptador, probarlo antes de migrar la primera ruta.
- [ ] Cambiar las cuatro rutas de `/connect` juntas solo cuando la prueba de protocolo y la suite de integración pasen; evitar dos handlers para el mismo path/verbo.

## Tarea 7: Webhook y workers de WhatsApp

**Archivos:**
- Crear: `src/ArquitecturaBase.Api/Controllers/WhatsAppWebhookController.cs`.
- Crear interfaces `IWhatsAppWebhookService.cs`, `IWhatsAppInboundService.cs`, `IWhatsAppMessageHistoryService.cs`, `IWhatsAppRetentionService.cs` en `Application/Abstractions/Services/` y sus implementaciones en `Application/Services/WhatsApp/`.
- Crear `Application/Abstractions/WhatsApp/IWhatsAppWebhookAttemptRunner.cs` y `Infrastructure/WhatsApp/WhatsAppWebhookAttemptRunner.cs` para ejecutar un intento en un scope nuevo cuando haya un conflicto único.
- Crear o ampliar un contrato de repositorio de retención de mensajes y su implementación EF en `Infrastructure/Persistence/Repositories/`; el método encapsula `ExecuteUpdate`.
- Modificar: `src/ArquitecturaBase.Infrastructure/WhatsApp/WhatsAppInboundProcessor.cs`, `WhatsAppSenderBackgroundService.cs`, `WhatsAppMessageRetentionService.cs` (nombre previsto por Tarea 17), `src/ArquitecturaBase.Application/DependencyInjection.cs`.
- Retirar tras paridad: `Api/Endpoints/Webhooks/WhatsAppWebhookEndpoints.cs` y los tres handlers de `Application/Features/WhatsApp/`.
- Probar: `tests/ArquitecturaBase.Api.IntegrationTests/WhatsApp/` (webhook, registro, bot, sender, locks, reintentos) y tests de contrato.

- [ ] Conservar la ausencia de las rutas cuando WhatsApp/webhook está deshabilitado. Probar un mecanismo de registro MVC condicional en startup, no solo un 404 producido dentro de una ruta siempre registrada.
- [ ] Leer el cuerpo crudo en el controller antes de deserializar; respetar límite de 5 MB aun sin `Content-Length`, firma, challenge `text/plain`, 401/413 y 200 vacío para eventos firmados.
- [ ] Pasar el lote validado al servicio. `WhatsAppWebhookController` solo lee, verifica y parsea HTTP; `WhatsAppWebhookService` coordina la idempotencia y decide reintentar tras `UniqueConstraintViolationException`. Un adaptador de Infrastructure crea el segundo scope y resuelve allí el procesador del lote con un `ApplicationDbContext` limpio. Mantener orden de locks, un solo reintento y estados de mensajes; probar el conflicto concurrente desde HTTP.
- [ ] Migrar los workers de entrada y envío a servicios de Application; conservar scopes por iteración, cancelación, retry, logs sin secretos y persistencia del historial. Migrar también la retención que agregará la Tarea 17: el hosted service coordina la ejecución diaria, el servicio de Application fija la política y el repositorio ejecuta el update en EF.
- [ ] Correr la suite WhatsApp completa y pruebas con feature apagada/prendida antes de retirar los endpoints y handlers antiguos.

## Tarea 8: Retirar infraestructura anterior y cerrar la migración

**Archivos:**
- Modificar: `src/ArquitecturaBase.Api/Program.cs`, `DependencyInjection.cs`, `src/ArquitecturaBase.Application/DependencyInjection.cs`, `CLAUDE.md`, `docs/specs/2026-09-18-arquitectura-base-design.md`, `README.md`.
- Eliminar después de confirmar cero usos: `Api/Endpoints/IEndpoint.cs`, `EndpointExtensions.cs`, endpoints y helpers ya reemplazados; `Application/Abstractions/Messaging/`, decoradores de handlers y registro Scrutor de la aplicación.
- Modificar pruebas: `tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs`, `TestFeatures/TestEndpoints.cs`, `tests/ArquitecturaBase.Application.UnitTests/DependencyInjectionTests.cs`, `tests/ArquitecturaBase.ArchitectureTests/`.

- [ ] Sustituir los endpoints de prueba de `ApiFactory` por controllers de prueba o por un arnés equivalente; mantener pruebas de errores y binding. Reemplazar las pruebas de `ValidationDecorator` y `LoggingDecorator` por pruebas de validación/logging de servicios, y las de `UnitOfWorkDecorator` por pruebas de commit y rollback de cada categoría de servicio.
- [ ] Antes de retirar `AddFeaturesFromAssembly`, resolver todos los validadores desde DI en un test de composición y demostrar que los requests inválidos no llegan a repositorios ni a `IUnitOfWork`. Confirmar también que cada método migrado conserva los logs operativos sin registrar códigos de acceso, tokens ni secretos.
- [ ] Buscar referencias a `IEndpoint`, `ICommandHandler`, `IQueryHandler`, `AddFeaturesFromAssembly` y `MapEndpoints` en `src` y `tests`; retirar el pipeline viejo solo cuando no quede ningún consumidor de aplicación o test.
- [ ] Actualizar la guía y el diseño para que describan MVC, servicios, repositorios/lectores y DI explícita. Quitar la instrucción anterior de crear endpoints/handlers nuevos.
- [ ] Ejecutar `dotnet build ArquitecturaBase.slnx` sin warnings y `dotnet test` completo con Docker. Ejecutar además las pruebas del frontend y una comprobación manual de login, administración, `/connect` y webhook con la API real antes de cerrar.
- [ ] Revisar el diff final para confirmar que no se introdujeron rutas duplicadas ni referencias de EF en Api/Application. Probar el arranque con una base nueva y los flujos principales con el frontend; documentar toda diferencia deliberada de rutas, esquema o JSON y actualizar sus pruebas.

## Criterios de aceptación

- Todas las rutas de negocio actuales y las agregadas hasta la Tarea 18 tienen controller MVC y son consumidas por el frontend actualizado. Los contratos observables se mantienen por defecto; cualquier cambio deliberado se refleja en el cliente y sus pruebas.
- Los controllers dependen de interfaces de servicios; los servicios no dependen de EF/ASP.NET Core y usan repositorios, lectores o puertos externos para persistencia/integraciones.
- Identity y permisos no contienen consultas de negocio directas a `ApplicationDbContext` fuera de repositorios/lectores; los componentes técnicos de Infrastructure conservan el contexto donde corresponde.
- El pipeline de validación, auditoría, guardado en error, locks y transacciones conserva su comportamiento demostrado por tests.
- `dotnet build ArquitecturaBase.slnx` y `dotnet test` pasan completos, sin warnings ni tests omitidos inesperados. El front compila y sus pruebas pasan con el contrato HTTP acordado.
- Una base de desarrollo vacía se puede crear y poner en marcha con esquema, datos iniciales e ingreso de administrador, sin depender de datos o migraciones anteriores.
