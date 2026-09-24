# Migración a MVC, servicios y repositorios — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `subagent-driven-development` or `executing-plans` to implement this plan task by task. Mark each completed step with `- [x]`.

**Goal:** Convertir `ArquitecturaBase` a `Controller MVC → servicio de Application con interfaz → repositorio/lector con interfaz → ApplicationDbContext`, conservando las funcionalidades existentes y la integración con el frontend.

**Architecture:** Mantener los proyectos `Domain`, `Application`, `Infrastructure`, `Api`, `AppHost` y `ServiceDefaults`. Organizar contratos en `Application/Interfaces`, casos de uso en `Application/Services`, entrada HTTP en `Api/Controllers` y acceso a datos en repositorios/lectores de `Infrastructure`. Migrar una superficie funcional por vez. Los adaptadores técnicos de Identity, OpenIddict, SMTP y WhatsApp siguen siendo infraestructura. La [especificación canónica](../specs/2026-09-24-backend-mvc-architecture.md) y `AGENTS.md` ya rigen el código nuevo.

**Tech Stack:** .NET 10, ASP.NET Core MVC, FluentValidation, ASP.NET Core Identity, OpenIddict, EF Core/Npgsql, PostgreSQL, Aspire, xUnit/Testcontainers.

---

## Ejecución aislada y paralela

Por autorización del usuario para esta migración, `main` conserva el proyecto estable que ejecuta Visual Studio. La integración se hace en `codex/mvc-migracion`, en un worktree separado. Cada tarea paralela escribe en su propio worktree y entrega commits para integrar; ninguna tarea hace push ni cambia el checkout principal. El integrador incorpora una familia de rutas por vez y ejecuta su puerta de pruebas antes de aceptar la siguiente.

| Ola | Trabajo que puede avanzar en paralelo | Integración obligatoriamente secuencial |
|---|---|---|
| 0 — Baseline | Inventarios y pruebas de contrato de `Account/Connect`, API de administración y WhatsApp en archivos distintos. | Consolidar las 41 combinaciones verbo/ruta, registrar build/tests de backend y verificar cobertura. |
| 1 — Base MVC | Investigar y probar passthrough OpenIddict y rutas condicionales WhatsApp. | Un solo dueño para MVC, JSON, errores, validación, DI y piloto de Ajustes. |
| 2 — Persistencia | Preparar pruebas y extracción por agregado o lector sin alterar firmas a la vez. | Integrar los contratos y consultas de Identity/roles antes de migrar servicios que dependen de ellos. |
| 3 — Áreas | Servicios, controllers y tests de Usuarios/Perfil/Roles, Account/Connect y WhatsApp en worktrees separados, según dependencias ya fijadas. | Cambiar el mapeo de cada familia y retirar su endpoint anterior en un único paso verificado. |
| 4 — Cierre | Revisiones de especificación, código y regresión por área. | Retirar el pipeline viejo solo sin consumidores y correr build, suites y flujos manuales completos. |

`Api/Program.cs`, los tres `DependencyInjection.cs` de Api/Application/Infrastructure y `ApiFactory.cs` tienen un integrador único. Los agentes de área que necesiten modificarlos entregan el cambio requerido para su integración. Las reglas compartidas de códigos, contactos, locks y sesión tienen un único dueño por ola. Los worktrees aíslan archivos, pero no puertos ni servicios externos: no levantar otra instancia de Aspire mientras Visual Studio ejecuta el proyecto principal.

**Arranque de esta ejecución (2026-09-24):** base backend `d2f6583`; worktree integrador `codex/mvc-migracion`. La Tarea 0 se repartió en `codex/task0-auth-connect-contracts`, `codex/api-administration-contracts` y `codex/whatsapp-contracts-t0`, con matrices y pruebas nuevas por área. Ninguna reemplaza rutas de producción ni modifica los archivos de composición compartidos. El usuario confirmó que la solución completa funciona desde Visual Studio en el checkout principal.

**Baseline automatizado en el worktree integrador:** `dotnet build ArquitecturaBase.slnx --nologo` pasó con 0 advertencias y 0 errores. La primera corrida de `dotnet test` tuvo 1267/1269: dos casos de `WhatsAppRegistrationTests` recibieron `ObjectDisposedException` del arnés al forzar un fallo de arranque por plantillas vacías. Se ajustó solo esa prueba para invocar la validación de inicio registrada por `AddWhatsApp` y comprobar la clave inválida en `OptionsValidationException.Failures` (commit `ffba42f`). El rerun focalizado pasó 18/18 y la suite completa pasó **1269/1269**, sin cambios en producción. El frontend conserva cambios ajenos sin commit en su checkout activo; verificarlo en un estado aislado antes de atribuirle resultados a esta migración.

**Puerta HTTP de la Tarea 0:** las matrices están en [`contracts/auth-connect.md`](contracts/auth-connect.md), [`contracts/api-administration.md`](contracts/api-administration.md) y [`contracts/whatsapp.md`](contracts/whatsapp.md); los cinco workers y accesos EF de Infrastructure están en [`contracts/infrastructure-inventory.md`](contracts/infrastructure-inventory.md). `ExplicitRouteInventoryTests` compara el registro de endpoints con las **41** combinaciones verbo/ruta, incluidos los cuatro registros condicionales de WhatsApp, y detecta pérdidas o duplicados. Las pruebas de contrato de Account/Connect, administración y WhatsApp fijan las respuestas observables de sus bordes. Con ellas incorporadas, `dotnet test` pasó **1350/1350** en el worktree integrador; no se cambió código de producción en esta ola.

**Avance de la Tarea 1:** se movieron los siete contratos de repositorio y todos los contratos retenidos de `Application/Abstractions`; allí solo quedan `Messaging` y `Behaviors` durante la transición. `AddControllers` y `MapControllers` conviven con las rutas aún no migradas, sin duplicarlas. El adaptador MVC de `Result` conserva 200/204 y `ProblemDetails` en tres pruebas HTTP; las pruebas de arquitectura fijan los límites nuevos. La solución compiló con 0 advertencias y 0 errores, y la suite backend completa pasó **1356/1356** antes del primer cambio de ruta.

**Puerta del primer piloto MVC (Ajustes):** `db0497b` agregó servicio, modelos, validador y pruebas; `076b06b` cambió en un único commit `GET/PUT /api/settings` del endpoint Minimal al controller, registró el servicio y retiró los handlers sin consumidores; `76702e2` agregó logging del servicio. `9f7d0c6` y `4a8d287` resolvieron antes las diferencias de binding y cuerpo de `ProblemDetails`. El inventario sigue en **41** verbo/ruta, OpenAPI conserva operaciones, tag y cuerpo JSON de Ajustes, y `dotnet test ArquitecturaBase.slnx --no-restore --verbosity quiet` pasó **1384/1384** tras el logging. `main`, el frontend y la instancia de Aspire de Visual Studio no se modificaron.

## Estado y decisión sobre crear otro proyecto

**Estado al redactar el plan, antes de iniciar la rama de migración:** las 18 tareas de WhatsApp y los cuatro hitos manuales terminaron; el Hito 4 quedó registrado en `docs/plans/2026-09-22-ingreso-whatsapp.md` (la tabla de resumen al final de ese documento no se actualizó). Backend `67c20ab` y frontend `ac42869` eran los commits base al revisar el plan. Había 13 clases `IEndpoint`, 34 handlers y 41 combinaciones explícitas de verbo/ruta posibles. Las últimas pruebas automáticas documentadas entonces fueron 1269/1269 en backend y 593/593 en frontend. Todo está en desarrollo, sin datos productivos; la base local puede borrarse y recrearse. La arquitectura ya estaba fijada en `AGENTS.md` y la especificación nueva. El avance ejecutado y los resultados actuales figuran arriba, en «Ejecución aislada y paralela».

**Comprobación previa a esta ejecución:** `npm run build`, `npm run lint` y `npm run test` del frontend pasaron (593/593). En aquella revisión, `dotnet build ArquitecturaBase.slnx` no pudo restaurar paquetes por acceso bloqueado a `api.nuget.org` (`NU1301`) y falta local de `Aspire.AppHost.Sdk`. El baseline backend se pudo verificar después en el worktree integrador, como se detalla arriba.

### Puertas de avance

- **Inicio:** partir de los commits backend `67c20ab` y frontend `ac42869`, o anotar los nuevos commits base si aparece otro cambio antes de implementar. Registrar `dotnet build ArquitecturaBase.slnx` y `dotnet test` verdes en backend, y `npm run build`, `npm run lint`, `npm run test` verdes en `ArquitecturaBaseFront`, además de la matriz de contrato con cobertura verificable para cada verbo/ruta. Si el baseline falla, resolverlo antes de atribuir una falla al refactor.
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

### Qué existe hoy y qué quedará

El recorrido actual es `Api/Endpoints → Application/Features/<CasoDeUso>/*Handler → interfaces de Domain o Application/Abstractions → Infrastructure`. Los handlers cumplen la función de servicios de aplicación, pero su nombre y dispersión por caso de uso no muestran el recorrido convencional. **`Api` y `Application` ya están libres de `ApplicationDbContext`, EF, `IQueryable` y `UserManager`**; el acceso directo a EF de negocio que hay que encapsular está en `Infrastructure/Identity/IdentityService` y `PermissionService`, además de la operación de retención de WhatsApp. La reorganización busca hacer explícitas las responsabilidades sin reescribir las reglas de negocio que funcionan.

| Proyecto | Responsabilidad final | Carpetas principales |
|---|---|---|
| `Api` | HTTP: rutas, binding, autorización, cookies/redirecciones y traducción de `Result` a respuesta. | `Controllers/`, `ErrorHandling/`, `Authorization/` |
| `Application` | Servicios de casos de uso, validación, orquestación y contratos que consumen. Sin EF ni `HttpContext`. | `Interfaces/Services/`, `Interfaces/Persistence/`, `Interfaces/Integrations/`, `Services/<Area>/`, `Models/<Area>/`, `Validation/<Area>/`, `Configuration/` |
| `Domain` | Entidades, value objects, reglas y errores de dominio. | `Authentication/`, `Users/`, `WhatsApp/`, `Settings/`, `Authorization/` |
| `Infrastructure` | Implementaciones EF, Identity, OpenIddict, correo, Meta y workers técnicos. `ApplicationDbContext` permanece aquí. | `Persistence/Repositories/`, `Persistence/Readers/`, `Identity/`, `WhatsApp/` |

`Interfaces` es una **carpeta de contratos dentro de Application**, no un proyecto adicional. Los siete contratos `I*Repository` que hoy están junto a agregados de `Domain` y los contratos que hoy están en `Application/Abstractions` se trasladan allí en un cambio mecánico separado: las interfaces de servicio a `Interfaces/Services`, las de repositorio/lector a `Interfaces/Persistence`, y los puertos externos a `Interfaces/Integrations`; los records/DTO se ubican en `Models`. Las implementaciones van en `Services` o `Infrastructure`. No cambiar métodos ni reglas durante ese traslado y ejecutar la suite después; `IAuditable`, `ISoftDeletable` y otras interfaces propias del modelo permanecen en `Domain`. No crear un `IRepository<T>` genérico ni una interfaz por cada clase técnica.

### Inventario HTTP que se protege

Las 41 combinaciones explícitas son: `/account` 8, `/connect` 7, `/api` 24 y `/webhooks` 2. La Tarea 0 vincula **cada verbo/ruta** con su prueba exacta; las suites siguientes son el punto de partida, no una afirmación de cobertura individual completa.

| Área | Verbos y rutas existentes | Suites principales |
|---|---|---|
| Cuenta | `GET /account/login-methods`; `POST /account/login-code`, `/verify`, `/whatsapp`; `POST /account/login-link/preview`, `/redeem`; `GET /account/external/google`, `/callback` | `Auth/{LoginMethodsEndpointTests,LoginCodeEndpointsTests,WhatsAppLoginCodeTests,LoginLinkTests,ExternalLoginTests}.cs` |
| OIDC | `GET+POST /connect/authorize`, `/logout`, `/userinfo`; `POST /connect/token` | `Auth/{ConnectFlowTests,OpenIddictServerTests,PhoneAccountTokensTests}.cs` |
| Usuarios | `GET+POST /api/users`; `GET /api/users/filter-counts`; `GET+PUT+DELETE /api/users/{id:guid}`; `POST /{id:guid}/invitation`, `/{id:guid}/activate`, `/{id:guid}/deactivate`; `DELETE /{id:guid}/whatsapp` | `Users/{UsersEndpointsTests,CreateUserEndpointTests,UpdateUserEndpointTests,UserInvitationEndpointsTests,UnlinkUserPhoneEndpointTests,DeactivateUserEndpointTests,DeleteUserEndpointTests}.cs` |
| Perfil | `GET+PUT /api/me`; `POST /api/me/whatsapp/code`; `PUT+DELETE /api/me/whatsapp`; `POST /api/me/email/code`; `PUT /api/me/email` | `Users/{MeProfileEndpointTests,MeWhatsAppEndpointsTests,MeEmailEndpointsTests}.cs` |
| Roles, permisos, ajustes | `GET+POST /api/roles`; `PUT+DELETE /api/roles/{id:guid}`; `GET /api/permissions`; `GET+PUT /api/settings` | `Roles/{RolesEndpointsTests,RoleCrudEndpointsTests}.cs`, `Settings/SettingsEndpointsTests.cs` |
| Webhook | `GET+POST /webhooks/whatsapp` | `WhatsApp/{WhatsAppWebhookTests,WhatsAppRegistrationTests}.cs` |

Cuatro combinaciones son condicionales al arrancar: `POST /account/login-code/whatsapp` y `POST /api/me/whatsapp/code` dependen de `IWhatsAppAvailability.IsEnabled`; `GET+POST /webhooks/whatsapp` dependen de `IsWebhookEnabled`. `PUT+DELETE /api/me/whatsapp` y las dos rutas de login link **siguen mapeadas** con WhatsApp apagado. OpenIddict registra además `/connect/revoke`, `/connect/introspect` y discovery `/.well-known/*` fuera de esas 41; conservan su registro técnico, sin inventar controllers para endpoints que administra el framework.

## Reglas que deberá cumplir el resultado

1. Cada ruta HTTP de negocio vive en un controller MVC. El controller inyecta una interfaz de servicio, no `ApplicationDbContext`, `UserManager`, `IQueryable` ni un repositorio. La adaptación de protocolo de OpenIddict y la verificación de la firma sobre bytes crudos del webhook pueden usar helpers de presentación sin lógica de persistencia.
2. Los servicios y sus interfaces viven en `Application`; no referencian EF Core ni ASP.NET Core. Devuelven `Result`/`Result<T>` y orquestan los contratos de persistencia y de servicios externos. Cada método conserva el comportamiento de validación, auditoría y guardado del caso de uso que reemplaza.
3. Los contratos consumidos por servicios viven en `Application/Interfaces`; las implementaciones de repositorios y lectores viven en `Infrastructure`. Los siete contratos de repositorio hoy ubicados en `Domain` se mueven mecánicamente a `Application/Interfaces/Persistence`, sin cambiar firmas. `Domain` conserva el modelo y sus propias interfaces internas. No se expone `IQueryable`, `ApplicationUser` ni `ApplicationRole` fuera de Infrastructure.
4. Las consultas y escrituras de negocio a EF pasan por repositorios o lectores especializados. `SystemSettingsReader` ya es uno de esos lectores. `UnitOfWork`, migraciones, seeders, health checks y los stores de Identity/OpenIddict/DataProtection conservan acceso técnico a `ApplicationDbContext` dentro de Infrastructure.
5. DI registra explícitamente controllers, servicios, repositorios y lectores. Al terminar, no quedan `IEndpoint`, handlers `ICommandHandler`/`IQueryHandler` ni decoradores Scrutor en la aplicación. Los workers de WhatsApp consumen servicios de Application, no handlers; los endpoints técnicos de OpenIddict y Aspire siguen a cargo de sus frameworks.
6. Se conservan método y ruta HTTP, status, JSON, `ProblemDetails` traducido, permisos, rate limit, cookies, redirecciones y los contratos de los clientes. No mezclar cambios de producto o de API con esta migración. Si MVC obliga a una diferencia observable, detener esa área, documentar la decisión y actualizar cliente y pruebas antes de quitar la ruta anterior. No hay que preservar datos, sesiones ni historial de migraciones de desarrollo; sí debe poder crearse una base vacía y arrancar con esquema, seed y flujos de ingreso funcionales.

## Mapa de archivos objetivo

| Área | Entrada MVC en `Api/Controllers/` | `Application/Interfaces/Services/` y `Application/Services/<Area>/` | Persistencia o proveedor |
|---|---|---|---|
| Ajustes | `SettingsController.cs` | `ISystemSettingsService.cs`, `Settings/SystemSettingsService.cs` | `ISystemSettingsRepository`, `ISystemSettingsReader` existentes |
| Usuarios | `UsersController.cs` | `IUserService`, `UserService` | Repositorio/lector de usuarios, más adaptador de operaciones Identity |
| Perfil | `MeController.cs` | `IProfileService`, `ProfileService` | Repositorios/lectores de usuarios, códigos e historial existentes |
| Roles y permisos | `RolesController.cs`, `PermissionsController.cs` | `IRoleService`, `RoleService` | Repositorio/lector de roles y permisos |
| Códigos y métodos de ingreso | `LoginCodeController.cs`, `LoginMethodsController.cs` | `IAccountService`, `AccountService` | Repositorios de códigos y auditoría existentes; proveedor de identidad |
| Enlaces de ingreso | `LoginLinkController.cs` | `ILoginLinkService`, `LoginLinkService` | `ILoginLinkRepository` existente |
| Google y OIDC | `ExternalLoginController.cs`, `ConnectController.cs` | Servicio de autenticación y adaptadores de protocolo | Identity/OpenIddict en Infrastructure, sin tipos HTTP en Application |
| WhatsApp | `WhatsAppWebhookController.cs` | `IWhatsAppWebhookService`, `IWhatsAppInboundService`, `IWhatsAppDeliveryService` y sus implementaciones | Repositorios de contactos/mensajes existentes; operación de retención en repositorio; adaptador de scope para reintento del webhook; cliente Meta y workers en Infrastructure |

El mapa parte del código posterior a la Tarea 18: incluye perfil con correo/WhatsApp, invitaciones, registro del resultado de envíos y retención. No se crean servicios de historial o de retención solo por simetría: la retención sigue programada por su worker técnico y usa un repositorio para EF; el registro de envíos pertenece al servicio de entrega. Los métodos concretos se cierran contra el inventario de la Tarea 0.

## Tarea 0: Fijar el baseline posterior a WhatsApp y una salida segura

**Archivos:**
- Revisar: `docs/plans/2026-09-22-ingreso-whatsapp.md`, `CLAUDE.md`, `src/ArquitecturaBase.Api/Endpoints/`, `src/ArquitecturaBase.Application/Features/`.
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Contracts/HttpContractTests.cs`.
- Ampliar: `tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs` solo si los tests de contrato necesitan datos comunes.

- [x] Confirmar que WhatsApp 13–18 y los cuatro hitos manuales terminaron, y que backend `67c20ab` y frontend `ac42869` tienen árboles limpios al revisar el plan. Si hay commits nuevos antes de implementar, anotar el nuevo par base. No incluir cambios de otro trabajo en los commits de esta migración.
- [x] Completar para las **41 combinaciones de verbo y ruta** del inventario anterior una fila con método del handler, autorización, rate limit, content type, status, cuerpo, headers y condición de registro. Incluir además `/connect/revoke`, `/connect/introspect`, discovery `/.well-known/*` y los endpoints técnicos de Aspire como superficie que no se convierte a controllers. Inventariar todos los hosted services y los accesos de negocio directos a `ApplicationDbContext` en Infrastructure, incluida la retención ya implementada.
- [x] Vincular **cada verbo/ruta** con una prueba HTTP existente o nueva; crear pruebas donde falten. Agregar expresamente las variantes `POST` de authorize/userinfo/logout y la disponibilidad de introspection si el inventario confirma el hueco actual. Probar las cuatro rutas condicionales con WhatsApp apagado y prendido. Cubrir JSON inválido/ausente, query y GUID inválidos, 401/403/404/405/429, formato de error, `traceId`, localización, enum como texto y fechas UTC. Guardar el listado de endpoints antes del refactor y compararlo después de cada familia: mismos verbos/rutas y respuestas observables.
- [ ] Capturar baseline: `dotnet build ArquitecturaBase.slnx` y `dotnet test` en backend; `npm run build`, `npm run lint` y `npm run test` en `../ArquitecturaBaseFront`. Los tests de integración requieren Docker. Registrar resultados y fallas previas, si las hubiera.
- [x] Registrar cómo recrear una base de desarrollo vacía y verificar que arranca con el esquema y el seed necesarios. En Development, `Program.cs` ejecuta `ApplyMigrationsAsync` y `SeedDatabaseAsync` al iniciar con una base vacía; `MigrationsTests` y `SystemSettingsSeedTests` lo verifican en bases temporales independientes. No se borra la base que usa Visual Studio. No planificar respaldo, migración de datos ni preservación de migraciones históricas; si el modelo cambia, se puede simplificar o rehacer la migración inicial y recrear la base.
- [ ] Mantener un commit por área que pase su suite. Si una etapa falla, corregirla o revertir ese commit antes de migrar otra área. No dejar rutas duplicadas entre endpoint y controller.

## Tarea 1: Fijar límites de arquitectura y composición MVC

**Archivos:**
- Mover los siete `I*Repository.cs` de `src/ArquitecturaBase.Domain/{Authentication,Settings,Users,WhatsApp}/` a `src/ArquitecturaBase.Application/Interfaces/Persistence/` y los contratos retenidos de `Application/Abstractions/` a `Application/Interfaces/`; mover sus records/DTO a `Application/Models/`. Dejar `Abstractions/Messaging/` y `Abstractions/Behaviors/` hasta retirar el pipeline.
- Guías ya actualizadas: `AGENTS.md`, `CLAUDE.md`, `README.md`, `docs/specs/2026-09-18-arquitectura-base-design.md` y `docs/specs/2026-09-24-backend-mvc-architecture.md`.
- Modificar: `tests/ArquitecturaBase.ArchitectureTests/LayerDependencyTests.cs`.
- Crear: `tests/ArquitecturaBase.ArchitectureTests/ControllerServiceRepositoryTests.cs`.
- Modificar: `src/ArquitecturaBase.Api/DependencyInjection.cs`, `src/ArquitecturaBase.Api/Program.cs`.
- Crear: `src/ArquitecturaBase.Api/ErrorHandling/ControllerResultExtensions.cs`.
- Crear: `src/ArquitecturaBase.Application/Common/Validation/ServiceRequestValidator.cs` y `tests/ArquitecturaBase.Application.UnitTests/Services/ServiceValidationTests.cs`.
- Probar: `tests/ArquitecturaBase.Api.IntegrationTests/FrameworkErrorsTests.cs`, `ErrorHandlingTests.cs`, `ValidationProblemTests.cs`, `LocalizationTests.cs`, `OpenApiTests.cs`.

- [x] Antes de mover archivos, actualizar las guías con la decisión MVC y el estado transitorio: durante la migración conviven endpoints/handlers viejos con controllers/servicios nuevos, y los contratos nuevos van a `Application/Interfaces`. La guía ya no ordena crear handlers o repositorios en `Domain` para trabajo nuevo.
- [x] En un commit **solo mecánico**, mover los siete repositorios de `Domain` a `Application/Interfaces/Persistence`, actualizar namespaces/usings/DI/tests y compilar/probar. Mantener exactamente firmas y comportamiento; no crear controllers, servicios ni consultas nuevas en ese commit. Integrado en `9de1d6e`: build sin advertencias ni errores y suite completa 1350/1350.
- [x] En commits mecánicos separados por grupo (`Identity`, `Settings`, `WhatsApp`, proveedores), mover los contratos retenidos de `Application/Abstractions` a `Application/Interfaces` y sus tipos de datos a `Application/Models` o `Common`, actualizando namespaces/usings/DI/tests y ejecutando la suite pertinente tras cada grupo. Integrados en `32dc5a5`, `019553d`, `94b2905`, `3eeb727`, `023d8a6` y `38cd2b0`; las interfaces del pipeline viejo esperan su retiro en la Tarea 8.
- [x] Escribir pruebas de arquitectura para prohibir EF y `ApplicationDbContext` en `Api`/`Application`, repositorios directos en controllers y tipos de ASP.NET Core en servicios de Application. Verificar además que los contratos de persistencia de negocio no queden en `Domain`. Las reglas existentes de capas se complementaron con `ControllerServiceRepositoryTests` en `6b52fdf`; 14/14 pruebas de arquitectura verdes.
- [x] Probar por HTTP la equivalencia de `Result`/`Result<T>` para 200, 204 y cada familia de `ProblemDetails`; las pruebas revelaron diferencias de string, null y `type` que se corrigieron antes de migrar la primera ruta (`4aa87c1`).
- [x] Probar temprano, en controllers de prueba del arnés y sin sustituir rutas existentes, que MVC conserva el passthrough de `/connect` (sign-in, challenge, forbid, redirect y POST de formulario) y que una convención de startup omite de verdad las **cuatro** combinaciones WhatsApp condicionales según `IsEnabled` o `IsWebhookEnabled`. Los spikes quedaron en `a953c71` (7/7) y `bdee4b4` (3/3); no se duplicaron rutas productivas.
- [x] Implementar el adaptador MVC usando `ProblemDetailsMapper`, conservando status, `code`, `traceId`, errores de validación e idioma. `ControllerResultExtensions` quedó en `4aa87c1` con 3/3 pruebas HTTP verdes; `ResultExtensions` sigue vigente para las Minimal APIs.
- [x] Separar el registro de FluentValidation del escaneo de handlers en `Application/DependencyInjection.cs`. Implementar validación reusable en servicios que invoque **todos** los `IValidator<TRequest>` registrados, agrupe errores, traduzca mensajes, conserve nombres de campo camelCase y corte antes de mutar/guardar. `7f27d6b` prueba múltiples validadores, idioma y campos anidados; `SystemSettingsServiceTests` prueba cero lecturas/guardados/invalidation con entrada inválida.
- [ ] Definir logging en el límite de cada método de servicio con `ILogger<TService>` o un helper de Application: inicio, resultado y código de error, sin datos sensibles. `SystemSettingsService` y sus pruebas fijan el patrón en `76702e2`; migrar los tests útiles de `LoggingDecoratorTests` a servicios de cada área antes de retirar el decorador.
- [x] Registrar `AddControllers().AddJsonOptions(...)` con los mismos `UtcDateTimeConverter` y `JsonStringEnumConverter` que hoy se aplican con `ConfigureHttpJsonOptions`; los contratos HTTP de MVC comprueban enum y fecha.
- [x] Configurar las respuestas de binding/model state para conservar `Request.Invalid`; el filtro de cuerpo ausente (`9f7d0c6`) y la fábrica de `ProblemDetails` genérico (`4a8d287`) conservan 400/415, idioma y cuerpo sin mensajes del parser en 6/6 contratos HTTP.
- [x] Mapear controllers en `Program.cs`, manteniendo `MapEndpoints()` para las áreas aún no migradas. `ExplicitRouteInventoryTests` confirma que siguen las 41 combinaciones sin rutas nuevas ni duplicadas.
- [ ] Correr los tests de error, localización, OpenAPI y arquitectura. Confirmar que `AddControllers` no cambia `UseSpaFallback` ni el orden del middleware de `Program.cs`, y que 404/405/415 y los endpoints de Aspire/OpenIddict siguen igual. Build 0/0 y suite backend 1384/1384 verdes; `MvcConnectPassthroughTests`, `MvcBindingContractTests` y OpenAPI cubren los bordes HTTP. Falta una comprobación manual de Aspire/SPA desde el worktree de migración antes de cerrar toda la migración; no se altera la instancia que corre desde Visual Studio.

## Tarea 2: Piloto completo con ajustes del sistema

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Interfaces/Services/ISystemSettingsService.cs`, `src/ArquitecturaBase.Application/Services/Settings/SystemSettingsService.cs`, `src/ArquitecturaBase.Api/Controllers/SettingsController.cs`.
- Modificar: `src/ArquitecturaBase.Application/DependencyInjection.cs`, `src/ArquitecturaBase.Api/DependencyInjection.cs`.
- Eliminar al final de la tarea: `src/ArquitecturaBase.Api/Endpoints/Settings/SettingsEndpoints.cs`, los dos handlers de `Application/Features/Settings/` y sus comandos/consultas solo si dejaron de usarse.
- Probar: `tests/ArquitecturaBase.Api.IntegrationTests/Settings/SettingsControllerTests.cs`, `SystemSettingsReaderTests.cs`, `SystemSettingsSeedTests.cs`; crear `tests/ArquitecturaBase.Application.UnitTests/Services/SystemSettingsServiceTests.cs`.

- [x] Escribir pruebas del servicio para `Get` y `Update`: `SettingsErrors.NotFound`, `RegistrationMode`, validación, guardado una sola vez en éxito e invalidación del caché después del commit. El test de guardado fallido detectó que el handler anterior invalidaba antes del commit; el servicio nuevo corrigió el orden en `db0497b`, separado del cambio de ruta `076b06b` y sin alterar el handler mientras seguía activo.
- [x] Mover la lógica de los dos handlers al servicio con interfaz. Para escritura, validar antes de mutar, usar `IUnitOfWork.SaveChangesAsync` explícitamente y llamar `ISystemSettingsReader.InvalidateAsync` solo tras un guardado exitoso.
- [x] Registrar `ISystemSettingsService → SystemSettingsService` con lifetime scoped. Registrar el controller con `GET/PUT /api/settings` y política `settings.manage`; el controller inyecta solo la interfaz del servicio.
- [x] Retirar el mapeo Minimal API de ajustes en el mismo cambio que habilita el controller. `ExplicitRouteInventoryTests`, los contratos de Ajustes, permisos, caché y OpenAPI pasaron; también se retiraron los handlers y contratos viejos sin consumidores.
- [x] Resolver en la configuración común de MVC las diferencias de binding y `ProblemDetails` antes del piloto; `MvcBindingContractTests` verifica 400/404/415, cuerpo e idioma.

## Tarea 3: Encapsular persistencia de usuarios, roles y permisos

**Archivos:**
- Crear primero `IUserReader.cs`, `IRoleReader.cs` e `IPermissionReader.cs` en `src/ArquitecturaBase.Application/Interfaces/Persistence/`, con implementaciones en `Infrastructure/Persistence/Readers/`. Crear `IUserRepository.cs`/`IRoleRepository.cs` y sus implementaciones solo al extraer escrituras concretas de los stores de Identity; no introducir interfaces vacías.
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`, `PermissionService.cs`, `src/ArquitecturaBase.Infrastructure/DependencyInjection.cs`.
- Probar: `tests/ArquitecturaBase.Api.IntegrationTests/Identity/IdentityServiceTests.cs`, `PermissionServiceTests.cs`, `tests/ArquitecturaBase.Api.IntegrationTests/Users/`, `Roles/`.

**Orden de integración basado en el inventario de EF:** (1) `ILoginLinkRepository.ListPendingAsync` con prueba de enlace vencido pendiente al revocar; (2) `IPermissionReader` manteniendo lectura fresca de `UserRoles` y caché solo de `RoleClaims`; (3) `IUserReader` con un único filtro para listado/conteos, soft delete, `ILIKE`, paginación y orden secundario por Id; (4) `IRoleReader`, normalización y conteos sin usuarios borrados; (5) escrituras y transacciones de Identity con pruebas de fallo intermedio. Esta secuencia evita duplicar `UserManager`/`RoleManager` antes de definir el límite transaccional. Además de la suite existente, agregar pruebas de roles frescos tras reasignación, empates entre páginas, normalización del rol y enlaces vencidos.

- [ ] Para cada consulta directa de `IdentityService` y `PermissionService`, escribir un test que capture filtros, soft delete, orden estable, paginado, normalización, proyección y caché actuales.
- [ ] Extraer las consultas EF de negocio de `IdentityService` y `PermissionService` a lectores especializados; conservar filtros de cuentas borradas, conteos y caché. Las interfaces no retornan `IQueryable`, `ApplicationUser` ni `ApplicationRole`.
- [ ] En `IdentityService.RevokeSessionsAsync`, trasladar la consulta directa a `dbContext.LoginLinks` al `ILoginLinkRepository`. Agregar una operación para buscar enlaces **sin consumir y sin invalidar aunque ya hayan expirado**; `ListActiveAsync` no sirve porque filtra vencidos. Mantener la invalidación antes de `UpdateSecurityStampAsync` y en el mismo contexto/transacción. Dejar en el adaptador Identity solo el security stamp y las revocaciones técnicas de OpenIddict; probar la revocación de enlaces tras desactivar/desvincular.
- [ ] Encapsular escrituras de usuarios/roles en repositorios o adaptadores de Identity, con una responsabilidad clara por operación; `IIdentityService` no debe convertirse en un envoltorio redundante de `IUserRepository`. Mantener separados los actos técnicos de sesión, cookie, seguridad y revocación de OpenIddict. No crear un repositorio genérico `IRepository<T>` ni repositorios para SMTP o HttpClient.
- [ ] Mantener `ISystemSettingsReader` como lector especializado. Dejar `UnitOfWork`, migraciones, seeders, health checks y stores del framework como infraestructura técnica; no crear repositorios ficticios para ellos.
- [ ] Verificar transacciones antes de mover la responsabilidad del commit: `UserManager`/`RoleManager` autoguardan. Para cada flujo que combine Identity con códigos, links, auditoría, permisos o invitaciones, escribir un test de fallo intermedio, definir el límite de transacción compartida sobre el mismo `ApplicationDbContext` y comprobar rollback o el guardado en error que corresponda. No asumir que el `SaveChanges` final vuelve atómicas las operaciones de Identity.
- [ ] Registrar cada contrato e implementación scoped en `Infrastructure/DependencyInjection.cs`. Ejecutar pruebas de Identity, permisos, usuarios y roles antes de migrar controllers de esas áreas.

## Tarea 4: Usuarios, perfil, roles y permisos

**Archivos:**
- Crear: `Api/Controllers/UsersController.cs`, `MeController.cs`, `RolesController.cs`, `PermissionsController.cs` bajo `src/ArquitecturaBase.Api/`.
- Crear interfaces en `src/ArquitecturaBase.Application/Interfaces/Services/`: `IUserService.cs`, `IProfileService.cs`, `IRoleService.cs`.
- Crear implementaciones en `src/ArquitecturaBase.Application/Services/{Users,Roles}/`: `UserService.cs`, `ProfileService.cs`, `RoleService.cs`.
- Modificar: `src/ArquitecturaBase.Application/DependencyInjection.cs`.
- Retirar, tras paridad: `Api/Endpoints/Users/UsersEndpoints.cs`, `MeEndpoint.cs`, `Api/Endpoints/Roles/RolesEndpoints.cs` y los handlers de `Application/Features/Users/` y `Roles/` reemplazados.
- Probar: `tests/ArquitecturaBase.Api.IntegrationTests/Users/`, `Roles/`, `PaginationTests.cs`, `Identity/PermissionServiceTests.cs` y tests unitarios nuevos en `Application.UnitTests/Services/`.

- [ ] Pasar al mapa de métodos los casos de uso **ya implementados**: crear/editar/restaurar/desactivar/borrar usuario, filtros/conteos, invitaciones, desvinculación administrativa, perfil con correo y WhatsApp, y roles/permisos. Conservar `lastInvitation.deliveryStatus`, `WaMessageId`, números formateados/enmascarados y estados de verificación en las respuestas. Evitar un servicio gigante: separar usuarios, perfil y roles por responsabilidad.
- [ ] Escribir tests de servicio primero para reglas de usuario/rol: cuenta borrada, último Admin, permisos, filtros y conteos, concurrencia e invalidación de permisos. Para perfil, probar expresamente que `ConfirmEmail` y `ConfirmPhoneLink` consumen/intentan guardar los cambios exigidos aun cuando la verificación o la unicidad devuelve error; ambos usan hoy `IPersistChangesOnFailure`.
- [ ] Conservar `PhoneNumberChange`, `WhatsAppContactLinker` y `DestinationCodeVerifier`: locks en orden contactos → cuenta, códigos `VerifyDestination` por cuenta, revocación de sesiones y enlaces al soltar un número. Usar `MeWhatsAppEndpointsTests`, `MeEmailEndpointsTests`, `UserInvitationEndpointsTests`, `UnlinkUserPhoneEndpointTests` y `WhatsAppLockOrderTests` como regresión explícita.
- [ ] Mover lógica de handlers a servicios, conservando los helpers de negocio útiles (`UserGuards`, etc.) o integrándolos sin duplicación. El servicio coordina validadores y `IUnitOfWork`; las consultas no guardan y cada escritura conserva su condición de guardado, incluidos los dos errores de perfil anteriores.
- [ ] Crear controllers con rutas y permisos idénticos. Mantener `/{id:guid}`, defaults de paginado, filtros y cuerpos JSON. Cambiar una familia de rutas por commit: primero usuarios, luego perfil, luego roles/permisos.
- [ ] Después de cada familia, ejecutar tests de esa área y el contrato HTTP. Eliminar los handlers/endpoint viejos de esa familia solo cuando los tests nuevos y existentes pasen.

## Tarea 5: Ingreso por código, enlaces y proveedor externo

**Archivos:**
- Crear controllers `LoginCodeController.cs`, `LoginMethodsController.cs`, `LoginLinkController.cs`, `ExternalLoginController.cs` en `src/ArquitecturaBase.Api/Controllers/`.
- Crear `IAccountService.cs`, `ILoginLinkService.cs` en `Application/Interfaces/Services/` y sus implementaciones en `Application/Services/Auth/`.
- Modificar: `src/ArquitecturaBase.Application/DependencyInjection.cs`, los adaptadores de Identity necesarios y `src/ArquitecturaBase.Api/DependencyInjection.cs`.
- Retirar tras paridad: `Api/Endpoints/Account/` y handlers sustituidos en `Application/Features/Auth/`.
- Probar: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/` (código, link, Google, rate limits, concurrencia, registro e ingreso), `Identity/IdentityServiceTests.cs`.

- [ ] Escribir tests para los flujos donde un error **sí** debe persistir intento, auditoría o consumo: verificación de código, canje de enlace e ingreso externo. Esos casos hoy implementan `IPersistChangesOnFailure` y no pueden convertirse en un `return` sin guardar.
- [ ] Migrar código y enlaces a servicios con validación explícita antes de la operación; conservar locks, cuotas, expiración, idioma, emisión y respuesta HTTP. Mantener `202` en pedidos de códigos/invitaciones, `204` y cookie al canjear enlace, `415` para cuerpos no JSON donde protege de CSRF y la partición compartida de rate limits `login-code`/`login-verify` con perfil.
- [ ] Mantener la lógica de `Challenge`, callback y redirección Google en el controller/adaptador de presentación; el servicio de Application decide reglas de cuenta sin depender de `HttpContext`. Probar ingreso de cuentas sin correo y la propagación del error y `returnUrl` de Google al SPA.
- [ ] Cambiar endpoints por controllers en familias separadas, preservando rutas `/account/*`, status, JSON, cookies y las políticas `login-code`/`login-verify`.
- [ ] Ejecutar tests de autenticación completos y el contrato HTTP después de cada familia.

## Tarea 6: OpenIddict y `/connect`

**Archivos:**
- Crear: `src/ArquitecturaBase.Api/Controllers/ConnectController.cs` y helpers de protocolo necesarios dentro de `Api`.
- Conservar o adaptar: `src/ArquitecturaBase.Api/Endpoints/Connect/OpenIdPrincipalFactory.cs`, `OpenIddictResults.cs` según la prueba de paridad; mover los helpers retenidos a `Api/Authentication/` al retirar `Endpoints/`.
- Retirar tras paridad: `Api/Endpoints/Connect/{Authorize,Token,Logout,UserInfo}Endpoint.cs`.
- Probar: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/{ConnectFlowTests,OpenIddictServerTests,AuthenticationRegistrationTests}.cs`, `OpenApiTests.cs`, tests de contratos HTTP.

- [ ] Antes de sustituir rutas, escribir una prueba pequeña de passthrough OpenIddict con un controller MVC: GET/POST authorize, POST token con formulario, GET/POST logout/userinfo, challenge, sign-in, forbid y redirecciones. No asumir que un `IResult` de Minimal API conserva su comportamiento al devolverlo desde MVC.
- [ ] Conservar issuer usado por el SPA, PKCE, scopes, cookies, revocación de tokens, carga de principal y ausencia de permisos en el token. Verificar `/connect/revoke`, `/connect/introspect` y discovery `/.well-known/*`, que administra OpenIddict y no los controllers; conservar la exclusión de `/connect` del OpenAPI público. Si MVC necesita un `IActionResult` adaptador, probarlo antes de migrar la primera ruta.
- [ ] Cambiar las cuatro familias de `/connect` (siete combinaciones verbo/ruta) juntas solo cuando la prueba de protocolo y la suite de integración pasen; evitar dos handlers para el mismo path/verbo.

## Tarea 7: Webhook y workers de WhatsApp

**Archivos:**
- Crear: `src/ArquitecturaBase.Api/Controllers/WhatsAppWebhookController.cs`.
- Crear interfaces `IWhatsAppWebhookService.cs`, `IWhatsAppInboundService.cs`, `IWhatsAppDeliveryService.cs` en `Application/Interfaces/Services/` y sus implementaciones en `Application/Services/WhatsApp/`.
- Crear `Application/Interfaces/Integrations/IWhatsAppWebhookAttemptRunner.cs` y `Infrastructure/WhatsApp/WhatsAppWebhookAttemptRunner.cs` para ejecutar un intento en un scope nuevo cuando haya un conflicto único.
- Ampliar `Application/Interfaces/Persistence/IWhatsAppMessageRepository.cs` (o un lector/repositorio especializado si su responsabilidad queda más clara) y su implementación EF en `Infrastructure/Persistence/Repositories/` con la operación que borra solo `Body` mediante `ExecuteUpdate`.
- Modificar: `src/ArquitecturaBase.Infrastructure/WhatsApp/WhatsAppInboundProcessor.cs`, `WhatsAppSenderBackgroundService.cs`, `WhatsAppMessageRetentionService.cs`, `WhatsAppRegistration.cs`, `src/ArquitecturaBase.Application/DependencyInjection.cs`.
- Retirar tras paridad: `Api/Endpoints/Webhooks/WhatsAppWebhookEndpoints.cs` y los **cuatro** handlers de `Application/Features/WhatsApp/`: `ReceiveWhatsAppWebhook`, `HandleInboundMessage`, `RecordOutboundWhatsAppMessage` y `RecordUnsentWhatsAppMessage`.
- Probar: `tests/ArquitecturaBase.Api.IntegrationTests/WhatsApp/` (`WhatsAppWebhookTests`, `WhatsAppRegistrationTests`, `WhatsAppBotTests`, `WhatsAppSenderBackgroundServiceTests`, `WhatsAppMessageRetentionTests`, `WhatsAppLockOrderTests`, `WhatsAppWebhookLogPrivacyTests`) y tests de contrato.

- [ ] Conservar la ausencia de `GET+POST /webhooks/whatsapp` si `IsWebhookEnabled` es falso, y la ausencia de los dos `POST` condicionales de cuenta/perfil si `IsEnabled` es falso. Probar un mecanismo de registro MVC condicional en startup, no solo un 404 producido dentro de una ruta siempre registrada. `PUT+DELETE /api/me/whatsapp` y login link siguen mapeados aun con WhatsApp apagado.
- [ ] Leer el cuerpo crudo en el controller antes de deserializar; respetar límite de 5 MB aun sin `Content-Length`, firma, challenge `text/plain`, 401/413 y 200 vacío para eventos firmados.
- [ ] Pasar el lote validado al servicio. `WhatsAppWebhookController` solo lee, verifica y parsea HTTP; `WhatsAppWebhookService` coordina la idempotencia y decide reintentar tras `UniqueConstraintViolationException`. Un adaptador de Infrastructure crea el segundo scope y resuelve allí el procesador del lote con un `ApplicationDbContext` limpio. Mantener orden de locks, un solo reintento, señal al worker solo después del commit y estados de mensajes; probar el conflicto concurrente desde HTTP.
- [ ] Hacer que los workers de entrada y envío invoquen servicios de Application en vez de handlers; conservar señal **y** polling, scope y transacción por contacto, fallos aislados, cola acotada, cancelación y logs sin secretos. El sender conserva tres intentos con demora mínima, sin retry HTTP automático de `POST`; registra saliente o fallo (`WaMessageId`, estado de invitación), y **no reenvía** un mensaje si falló solamente el guardado del historial. Conservar la decisión actual de encolar antes del commit.
- [ ] Mantener `WhatsAppMessageRetentionService` como scheduler técnico en Infrastructure: corre al arrancar y diariamente **aunque WhatsApp esté apagado**, valida sus opciones al iniciar y calcula el umbral con `TimeProvider`. Pasar solamente la operación EF que borra `Body` a un repositorio especializado; las opciones `WhatsAppMessageRetentionOptions` permanecen en Infrastructure. No crear un servicio de Application nominal si no hay un caso de uso adicional.
- [ ] Correr la suite WhatsApp completa y pruebas con feature apagada/prendida antes de retirar los endpoints y handlers antiguos.

## Tarea 8: Retirar infraestructura anterior y cerrar la migración

**Archivos:**
- Modificar: `src/ArquitecturaBase.Api/Program.cs`, `DependencyInjection.cs`, `src/ArquitecturaBase.Application/DependencyInjection.cs`; reconciliar `AGENTS.md`, `CLAUDE.md`, `README.md` y la especificación canónica con el estado final.
- Eliminar después de confirmar cero usos: `Api/Endpoints/IEndpoint.cs`, `EndpointExtensions.cs`, endpoints y helpers ya reemplazados; `Application/Abstractions/Messaging/`, decoradores de handlers y registro Scrutor de la aplicación.
- Modificar pruebas: `tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs`, `TestFeatures/TestEndpoints.cs`, `tests/ArquitecturaBase.Application.UnitTests/DependencyInjectionTests.cs`, `tests/ArquitecturaBase.ArchitectureTests/`.

- [ ] Sustituir los endpoints de prueba de `ApiFactory` por controllers de prueba o por un arnés equivalente; mantener pruebas de errores y binding. Reemplazar las pruebas de `ValidationDecorator` y `LoggingDecorator` por pruebas de validación/logging de servicios, y las de `UnitOfWorkDecorator` por pruebas de commit y rollback de cada categoría de servicio.
- [ ] Antes de retirar `AddFeaturesFromAssembly`, resolver todos los validadores desde DI en un test de composición y demostrar que los requests inválidos no llegan a repositorios ni a `IUnitOfWork`. Confirmar también que cada método migrado conserva los logs operativos sin registrar códigos de acceso, tokens ni secretos.
- [ ] Buscar referencias a `IEndpoint`, `ICommandHandler`, `IQueryHandler`, `AddFeaturesFromAssembly` y `MapEndpoints` en `src` y `tests`; retirar el pipeline viejo solo cuando no quede ningún consumidor de aplicación o test.
- [ ] Inventariar todo lo que quede en `Application/Features/` después de retirar handlers: mover reglas y helpers usados (`UserGuards`, `PhoneNumberChange`, `DestinationCodeVerifier`, `WhatsAppContactLinker`, emisores de códigos/enlaces) a `Services/<Area>/` o a `Domain` si son reglas puras; mover requests/responses/DTO y opciones a `Models/<Area>/` o `Configuration/`, y validadores a `Validation/<Area>/`. Hacer los traslados por área y en commits mecánicos separados de cambios de comportamiento. Verificar que ya no queden directorios `Features/` ni `Abstractions/` de la arquitectura vieja.
- [ ] Mover helpers de OpenIddict retenidos fuera de `Api/Endpoints/`. Quitar Scrutor de los proyectos y del catálogo de paquetes si ya no queda ningún uso; confirmar que la composición DI no depende del escaneo de handlers.
- [ ] Al cerrar, quitar de las guías las notas de transición y comprobar que la estructura real coincide con `AGENTS.md` y la especificación canónica. La decisión MVC, las carpetas y el recorrido de un request ya están documentados desde el inicio.
- [ ] Ejecutar `dotnet build ArquitecturaBase.slnx` sin warnings y `dotnet test` completo con Docker. Ejecutar además build/lint/tests del frontend y comprobaciones manuales de login por correo y WhatsApp, invitaciones, vincular/desvincular desde Mi perfil, administración, `/connect` y webhook con la API real antes de cerrar. El Hito 4 de WhatsApp ya probó invitación y desvinculación administrativa; vincular/desvincular desde Mi perfil tenía cobertura automática, así que se comprueba a mano en este cambio.
- [ ] Revisar el diff final para confirmar que no se introdujeron rutas duplicadas, cambios HTTP observables ni referencias de EF en Api/Application. Probar el arranque con una base nueva y los flujos principales con el frontend; documentar cualquier diferencia de esquema causada por recrear la base de desarrollo.

## Criterios de aceptación

- Las 41 combinaciones explícitas de verbo/ruta tienen trazabilidad a pruebas, y las cuatro condicionales aparecen o desaparecen igual que hoy. Las rutas de negocio usan controllers MVC; las técnicas de OpenIddict/Aspire siguen operativas. Los contratos observables de frontend, OIDC y Meta se mantienen.
- Los controllers dependen de interfaces de servicios; `Application/Interfaces` contiene contratos y `Application/Services` sus implementaciones. Los servicios no dependen de EF/ASP.NET Core y usan repositorios, lectores o puertos externos para persistencia/integraciones.
- Identity y permisos no contienen consultas de negocio directas a `ApplicationDbContext` fuera de repositorios/lectores; los componentes técnicos de Infrastructure conservan el contexto donde corresponde.
- El pipeline de validación, auditoría, guardado en error, locks y transacciones conserva su comportamiento demostrado por tests.
- `dotnet build ArquitecturaBase.slnx` y `dotnet test` pasan completos, sin warnings ni tests omitidos inesperados. El front compila y sus pruebas pasan con el contrato HTTP acordado.
- Una base de desarrollo vacía se puede crear y poner en marcha con esquema, datos iniciales e ingreso de administrador, sin depender de datos o migraciones anteriores.
