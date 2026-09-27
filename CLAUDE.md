# ArquitecturaBase: guía para agentes

Plantilla base .NET 10 + Aspire 13.5 + PostgreSQL. El front vive en `../ArquitecturaBaseFront`.
La arquitectura canónica del backend está en `docs/specs/2026-09-24-backend-mvc-architecture.md` y sus reglas inmediatas, en `AGENTS.md`. El diseño inicial `docs/specs/2026-09-18-arquitectura-base-design.md` es histórico para la estructura de capas y el pipeline HTTP; sus reglas funcionales siguen vigentes cuando no contradicen la especificación nueva. El historial y las puertas de verificación de la migración están en `docs/history/plans/2026-09-23-migracion-mvc-servicios-repositorios.md`.

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
| Application | Domain y los paquetes de la lista de `ApplicationPackagesTests` (FluentValidation y algunos Microsoft.Extensions.*) |
| Infrastructure | Application, Domain |
| Api | Application, Infrastructure (solo desde `Program.cs`, para registrar dependencias), ServiceDefaults |
| AppHost | Api (como recurso de Aspire) |

- **Domain:** reglas de negocio puras, sin paquetes.
- **Application:** interfaces de servicios y persistencia, servicios por área, modelos y validación; sin EF Core ni ASP.NET Core. `IQueryable` nunca sale de Infrastructure.
- **Infrastructure:** EF Core con Npgsql, interceptores, repositorios, lectores y adaptadores técnicos.
- **Api:** controllers MVC, contratos, adaptación HTTP y adaptadores de la petición (`Api/RequestContext`; no hay `Api/Services`, para no confundirlo con `Application/Services`). `Program.cs` compone.

## Casos de uso MVC

- Un request de negocio recorre `Api/Controllers → Application/Interfaces/Services → Application/Services/<Área> → Application/Interfaces/Persistence o Integrations → Infrastructure`. Los controllers inyectan interfaces de servicios; los servicios coordinan el caso de uso y los repositorios o lectores encapsulan EF. Las lecturas pueden usar lectores especializados y proyecciones eficientes.
- Los contratos de servicio, persistencia e integración están en `Application/Interfaces/{Services,Persistence,Integrations}`. Los modelos de aplicación, validadores y configuración funcional están en `Application/Models`, `Validation` y `Configuration`. Los contratos HTTP pertenecen a `Api/Contracts`.
- Todo body y query de entrada es un contrato de `Api/Contracts/<Área>` (`*HttpRequest`, o `*Query`), mapeado a mano al modelo de Application; las respuestas serializan los `*Response` de Application (`docs/decisions/0002-contratos-http.md`, lo verifica `ControllerInputContractTests`). Un contrato con datos personales, códigos o tokens sobrescribe `ToString()`, porque MVC registra los argumentos de la acción; los modelos de Application no lo hacen.
- FluentValidation y `Result`/`Result<T>` siguen vigentes. Cada servicio define su límite con `IUnitOfWork.ExecuteInTransactionAsync` y su `CommitPolicy`: `OnSuccess`, u `OnAnyResult` en los casos que tienen que guardar también cuando fallan (intentos, códigos o enlaces consumidos, auditoría). Es la única forma de guardar. Las consultas no abren límite. Mantener logging operativo sin registrar secretos.
- CQRS puede separar responsabilidades de lectura y escritura, sin exigir `ICommand`, `IQuery`, handlers ni repositorio genérico. Los mapeos se escriben a mano.
- No crear rutas de negocio Minimal API ni reintroducir `IEndpoint`, `Application/Features`, handlers `ICommandHandler`/`IQueryHandler` o sus decoradores. Los endpoints técnicos de OpenIddict y Aspire conservan su framework.

## Result en lugar de excepciones

- Las reglas de negocio devuelven `Result` / `Result<T>` con un `Error`. No lanzan excepciones.
- Las excepciones quedan para bugs y fallas de infraestructura. Las atrapa `GlobalExceptionHandler`, que responde un 500 genérico con `traceId`.
- Los errores se declaran en clases `<Entidad>Errors` (por ejemplo `UserErrors`).
- Los códigos son estables y siguen el formato `Area.Entidad.Motivo` (por ejemplo `Auth.LoginCode.Expired`). El código es la clave de la traducción en `Errors.resx`.
- Toda respuesta de error es ProblemDetails, con:
  - `title` y `detail` traducidos;
  - `code` y `traceId`;
  - `errors` en las validaciones (campo en camelCase → mensajes). El 400 de un cuerpo ilegible (`Request.Invalid`) no los trae a propósito: el ModelState mostraría la forma interna del modelo.
- Los errores que arma el propio framework (ruta inexistente, 405, 401/403 de la autorización) también salen como ProblemDetails (`ProblemDetailsMapper.CompleteFrameworkProblem` + `UseStatusCodePages`). Sus códigos están en `ApiErrorCodes`: `Http.*` por status, `General.Unexpected` para los 5xx y `Request.Invalid` para el resto de los 4xx. El 429 del rate limiter es distinto: `RateLimitingExtensions` arma su propio ProblemDetails con `retryAfter`; `UseStatusCodePages` solo completa la respuesta (en texto plano) cuando el cliente no acepta JSON.
- Todo middleware que pueda cortar con un error va en `Program.cs` después de `UseStatusCodePages`, o su respuesta sale vacía: `UseAuthentication`/`UseAuthorization` se declaran explícitos (no hay que dejar que `WebApplication` los agregue solo) y `UseRateLimiter` ya está después de `UseStatusCodePages`.

## Identidad

- Application usa contratos de Identity y de repositorios/lectores especializados para usuarios, roles y sesión; `UserManager`/`SignInManager` no salen de Infrastructure. `IIdentityService` conserva operaciones técnicas de Identity y delega las lecturas de negocio a lectores especializados.
- `/api` usa bearer (validación de OpenIddict, esquema por defecto). La cookie de Identity la usan solo `/account` y `/connect`.
- Las rutas piden permisos, nunca roles, con `[HasPermission(Permissions.Users.Read)]` (`Api/Authorization/HasPermissionAttribute.cs`) en el controller o la acción; nunca con `[Authorize(Policy = …)]` armado a mano (lo verifica `PermissionAuthorizationTests`). Sin sesión responde 401 y sin el permiso, 403.
- Un permiso nuevo:
  1. se declara en `Domain/Authorization/Permissions.cs` y en `Permissions.All`, y en `Permissions.resx` y `.en.resx` lleva `Permission.<código>` y `PermissionDescription.<código>` (y `Area.<área>` si el área es nueva); lo verifican `PermissionTextsTests` y `ResourceParityTests`;
  2. el seed se lo da a Admin;
  3. si cambian los permisos de un rol, hay que llamar a `IPermissionService.InvalidateRoleAsync`.
- Los claims de los tokens los arma `Api/Authentication/OpenIdPrincipalFactory.cs`. Los permisos no van en el token.
- Fuera de Development y Testing, OpenIddict firma y cifra con dos PFX propios: `Authentication:Certificates:{Signing,Encryption}` con `Base64` (o `Path`) y `Password`. Los carga `CertificateLoader`, y `Base64` gana sobre `Path` porque en un contenedor el certificado llega como secreto, no como archivo. Regenerarlos invalida todos los tokens emitidos.
- Nunca registrar códigos, tokens ni secretos. La auditoría de ingresos guarda el motivo del fallo (el código de error), nunca el código ingresado.
- Emails: plantillas embebidas en `Infrastructure/Emails/Templates` y textos en `Emails.resx`/`Emails.en.resx`, en el idioma del perfil.

## WhatsApp

Una persona puede crear su cuenta y entrar solo con su WhatsApp. El diseño está en `docs/specs/2026-09-22-ingreso-whatsapp-design.md` y el plan (histórico), en `docs/history/plans/2026-09-22-ingreso-whatsapp.md`.

- **La regla de oro: un mensaje de WhatsApp nunca abre una sesión.** El webhook no llama a `SignInAsync` ni emite tokens: la cookie se crea en la respuesta del pedido que la pide, y ese pedido lo manda Meta, así que la cookie le llegaría a Meta. Lo máximo que produce un mensaje es un **enlace de un solo uso** al mismo chat (`LoginLink`, 10 minutos, `LoginLink.Lifetime`). El enlace tampoco abre la sesión al abrirse: la abre el `POST /account/login-link/redeem` que dispara **Continuar** en `/ingresar`, así ni la vista previa de WhatsApp ni un antivirus lo gastan. El token viaja en el **fragmento** (`/ingresar#t=…`), que el navegador no le manda al servidor, y de él se guarda solo el SHA-256.
- **El webhook solo guarda.** `POST /webhooks/whatsapp` valida la firma (HMAC-SHA256 del cuerpo crudo con `AppSecret`, comparado con `CryptographicOperations.FixedTimeEquals`), guarda contactos, mensajes y estados, y responde `200`. Sin firma o con una equivocada, `401` y no guarda nada; un cuerpo de más de 5 MB, `413`. Un `phone_number_id` ajeno se ignora. Lo ilegible de un webhook **firmado** se saltea y el lote sigue: si respondiera 500, Meta lo reintentaría durante siete días.
- **Los duplicados se cierran en tres capas**, porque Meta reintenta: índice único sobre `WaMessageId`, locks por contacto y por mensaje tomados en orden, y `ExecuteInTransactionAsync` traduciendo el 23505 a `UniqueConstraintViolationException` (`UniqueViolations`) después de deshacer y soltar los locks; si igual choca, el endpoint reintenta una vez en un scope nuevo. Un estado gana por timestamp; si empatan, `sent` < `delivered` < `read` < `failed`, y `failed` es final.
- **El bot no tiene estado de conversación.** Decide con dos cosas: la cuenta del número y el id del botón que se tocó (`BotButtons`: `CREATE_ACCOUNT`, `HAVE_ACCOUNT`, `WANT_TO_ENTER`). La tabla de respuestas es la sección 8 del spec, con un test por fila en `WhatsAppInboundServiceTests`; `BotReplyTests` arma todas las respuestas en los dos idiomas y verifica que entren en los límites de Meta (ambos en `tests/ArquitecturaBase.Application.UnitTests/Services/WhatsApp/`). Los textos salen de `Application/Resources/Bot.resx` y `Bot.en.resx`, en el idioma de la cuenta o en español si no hay cuenta.
- **El procesador es quien responde, no el webhook.** `WhatsAppInboundProcessor` se despierta con una señal y además revisa la tabla cada `WhatsApp:InboundPollSeconds` (30), que es lo que encuentra lo pendiente de otra instancia o de antes de un reinicio. Toma los entrantes con `FOR NO KEY UPDATE SKIP LOCKED` por contacto, da **una sola** respuesta por contacto aunque haya varios mensajes (Meta rechaza un segundo mensaje a la misma persona dentro de 6 segundos, error 131056) y marca procesado sin responder lo que llegó hace más de 24 horas. Contesta al mensaje más nuevo dentro de esa ventana, y un botón le gana a un texto.
- **Los locks van siempre en el mismo orden: primero los contactos, después la cuenta** (sus enlaces). Al revés, el perfil y el bot se esperan mutuamente, Postgres corta a uno con un deadlock (40P01) y sale un 500. `PhoneNumberChange.LockAsync` es el orden canónico y lo usan el perfil y la administración; el contacto anterior que una cuenta suelta se toma sin esperar (`NOWAIT` en el perfil, `GetByUserIdForUnlinkAsync` en el bot), así el que pierde es el bot, que deja el mensaje para la vuelta siguiente. Quien llama lee la cuenta **después** de tomar los locks: mientras espera, el bot puede escribirla, y guardar con lo leído antes choca con el `ConcurrencyStamp` de Identity. Las claves de `pg_advisory_xact_lock` están todas en `AdvisoryLockKeys` (`login-code:`, `login-link:`, `user-invitation:`, `whatsapp-contact:user:`, `whatsapp-contact:wa:`, `whatsapp-message:` y `external-login:`): el texto es el lock, y cambiarlo deja de poner en fila a quien use el viejo. Dentro de una llamada se toman ordenadas y sin repetir (`AdvisoryLockExtensions`); entre llamadas el orden lo fija quien llama: `login-code:` del correo y después el del número, en dos llamadas; contactos antes que cuenta.
- **Vincular y soltar contactos pasa por `WhatsAppContactLinker`**, que es la única pieza que lo hace. Una cuenta tiene un solo contacto, y el bot la busca **primero por el contacto vinculado y después por el número**: un contacto que quedara apuntando a una cuenta que ya no tiene ese número le seguiría mandando sus enlaces a ese chat. Todo cambio de número (del perfil o del admin) suelta el contacto anterior e invalida los enlaces pendientes.
- **Los códigos son por destino y propósito.** `LoginCode` guarda `Destination` (`LoginCodeDestination.ForEmail` / `ForPhone`, con su `Channel`), `Purpose` (`SignIn` o `VerifyDestination`), `RequestedByUserId` y `SentAtUtc`. El hash es de destino + propósito + código, separados por un salto de línea. `LoginCodeIssuer` es el único que emite (lock del destino, límites, invalidación y tope diario de WhatsApp) y `DestinationCodeVerifier`, el único que verifica un `VerifyDestination`. Los límites por destino se comparten entre propósitos; los códigos de `VerifyDestination` se buscan e invalidan **por cuenta**, así otra cuenta que pida un código para el mismo número no invalida el de la dueña.
- **Los celulares argentinos se guardan con el 9.** `LibPhoneNumberParser` se lo agrega cuando el número sin él no es un celular válido y con él sí. El `to` que se le manda a Meta puede ir sin el 9 con `WhatsApp:SendArgentineMobilesWithoutNine`, que existe **solo** para el número de prueba: su lista de destinatarios los guarda sin el 9 y rechaza `+549…` con el error 131030. En producción va apagada. El número guardado no cambia nunca.
- **Lo que carga un administrador queda sin verificar**, el correo también. Se verifica cuando la persona lo usa: al entrar con el código del correo, al vincular Google o al confirmar el número desde el perfil. Dar de alta un correo o un número de una cuenta borrada la restaura, pero solo si todo lo que se cargó es de esa misma cuenta: puede completar lo que falte, nunca reemplazarlo. Desvincular desde la administración (`DELETE /api/users/{id}/whatsapp`) **cierra las sesiones**; hacerlo desde el propio perfil, no.
- **Enumerar cuentas no se paga con un 202.** Pedir un código responde siempre `202` con el mismo cuerpo, exista o no la cuenta, y lo mismo vale para vincular un número o agregar un correo: el `409` (`Users.Phone.AlreadyExists`, `Users.User.AlreadyExists`) sale **recién después** de un código correcto, y ese código queda gastado. Confirmar un destino no es un ingreso: un código equivocado cuenta el intento del código, pero no suma a los fallos de la cuenta ni deja `LoginAudit`.
- **El texto de los mensajes se borra a los 90 días** (`WhatsApp:MessageRetentionDays`, `WhatsAppMessageRetentionService`). La fila queda con su fecha, dirección, tipo, estado, botón y hora de procesado; los contactos no se tocan. `WhatsAppMessage` no es `IAuditable` ni `ISoftDeletable`, por eso puede usar `ExecuteUpdate`. **Ese número lo promete la política de privacidad: cambiarlo exige cambiar antes la política.** La tarea corre aunque WhatsApp esté apagado, porque la tabla puede tener mensajes de antes.
- **La plantilla de invitación (`invitacion_acceso`) quedó en Meta como Marketing**, no como Utilidad: una invitación que manda un administrador no es un mensaje que la persona pidió, y reescribir el texto no cambia eso. Lo que hay que tener presente al tocarla: cada invitación cuesta más, y Meta limita cuántos mensajes de marketing recibe cada persona, así que una invitación **puede no llegar** (el 131049 llega como estado `failed`). El respaldo es reenviarla o invitar por correo, y el backend expone `lastInvitation.deliveryStatus` para poder mostrarlo.
- **Nunca registrar un código, un token, un enlace ni un número entero.** Los números van enmascarados (`IPhoneNumberParser.Mask`, `+54 9 11 •••• 6789`). Dos consecuencias que no son obvias:
  - `Microsoft.AspNetCore` tiene que quedar en `Warning`: en `Information`, el log "Request starting" mostraría el `hub.verify_token` del GET de verificación de Meta;
  - las cadenas de conexión no llevan `Include Error Detail`: el DETAIL de un 23505 mostraría el BSUID.

  El cliente de Meta tampoco usa los logs automáticos de `HttpClient`, que en Trace guardan el header `Authorization` completo, y no reintenta los POST (`RemoveAllResilienceHandlers` más `Retry.DisableForUnsafeHttpMethods()`, en `WhatsAppRegistration`).
- **`/webhooks` está en `BackendPrefixes`** (`Api/Hosting/SpaExtensions.cs`), en `Backend_routes_keep_returning_a_problem` de `SpaHostingTests` y en el `server.proxy` de `vite.config.ts`. Los tres, como cualquier prefijo de backend.
- **Los tests capturan los envíos** con `factory.WhatsApp` (`CapturingWhatsAppOutbox`). El ciclo en segundo plano está apagado en el arnés: los tests llaman a `ProcessPendingAsync()` cuando quieren, así saben qué respondió el bot a qué.

### Configuración de WhatsApp

Todo cuelga de la sección `WhatsApp` y se lee **al arrancar**: cambiar cualquier valor, el token incluido, pide reiniciar la Api.

| Clave | Por defecto | Para qué |
|---|---|---|
| `PhoneNumberId` | — | **el interruptor.** Sin él, WhatsApp queda apagado y la app arranca igual (`IWhatsAppAvailability.IsEnabled = false`). Con él y sin `AccessToken`, la Api **no** arranca |
| `AccessToken` | — | secreto: el token del usuario del sistema |
| `AppSecret` y `VerifyToken` | — | secretos: prenden el webhook, y **solo los dos juntos**. Sin ninguno, el envío funciona y el webhook queda apagado con un Warning; con uno solo, la Api no arranca |
| `GraphApiVersion` | `v25.0` | la versión de la Graph API |
| `Templates:LoginCode` | `codigo_ingreso` | la plantilla de autenticación |
| `Templates:Invitation` | `invitacion_acceso` | la plantilla de invitación (Marketing) |
| `AllowedCountries` | `["AR"]` | a qué países se mandan códigos, ISO 3166-1 alfa-2 en mayúsculas. Vale también para vincular. **No tiene valor inicial en la clase**: el binder le suma lo configurado a lo que la lista ya tiene |
| `DailyAuthCodeLimit` | `100` | códigos por WhatsApp en una ventana móvil de 24 horas, entre todos los números. Cuenta solo lo que **salió** (`SentAtUtc`) |
| `DisplayPhoneNumber` | — | el número del bot, solo dígitos, para el enlace "Volver a WhatsApp" |
| `RetryDelaySeconds` | `6` | espera antes de reintentar. Nunca baja de 6, que es el límite de Meta por persona |
| `QueueCapacity` | `100` | la cola de envío, como `EmailQueue` |
| `InboundPollSeconds` | `30` | cada cuánto revisa el procesador los entrantes pendientes |
| `ProcessInboundInBackground` | `true` | en los tests va en `false` |
| `MessageRetentionDays` | `90` | **lo promete la política de privacidad** |
| `ApplyMessageRetentionInBackground` | `true` | en los tests va en `false` |
| `SendArgentineMobilesWithoutNine` | `false` | solo para el número de prueba de Meta |

Además:

- **Con el webhook prendido, la Api no arranca sin `Authentication:Issuer`**: es el origen público del que sale el enlace que el bot manda al chat. Fuera de Development y Testing es obligatorio siempre, porque también lo usa el botón del correo de invitación.
- El rate limiter del webhook es `whatsapp-webhook`, por IP, con `RateLimiting:WhatsAppWebhookPermitLimit` (600) y `RateLimiting:WhatsAppWebhookWindowMinutes` (1).
- **El túnel es del AppHost y viene apagado.** `DevTunnel:Enabled`, en los user-secrets del AppHost, expone el endpoint `https` de la Api para que Meta llegue al webhook. Mientras está prendido, la Api entera queda en internet: se prende para probar y se apaga al terminar. El paso a paso está en el README.
- Hay un health check `whatsapp`, solo de readiness.

## Administración (Fase 4)

- **Ajustes del sistema:** `SystemSettings` es una entidad de **una sola fila**, auditable. Se lee cacheada con `HybridCache` y el caché se invalida al guardar, así el cambio vale al instante. El seed la crea con el valor de `Registration:Mode` (por defecto `InviteOnly`); **si la fila ya existe, manda la base**: un despliegue nunca pisa lo que se configuró desde el panel.
- **El modo de registro** decide quién puede *crear* una cuenta, no quién puede entrar. `POST /account/login-code` sigue respondiendo siempre `202`, y en `InviteOnly` un correo sin cuenta **igual emite y guarda su fila de `LoginCode`**: lo único que no pasa es que se mande el email. La fila se emite a propósito y no hay que "optimizarla": los límites por dirección se apoyan en ella, y sin ella una dirección desconocida respondería `202` para siempre mientras una registrada empieza a responder `429`, que es todo lo que hace falta para enumerar cuentas. Vence sola a los 10 minutos sin que nadie la use. Con Google, en cambio, la persona ya probó ser dueña de la dirección, así que vuelve al ingreso con `Account.NotInvited`. Lo mismo pasa si alguien llega a verificar un código válido para un correo sin cuenta (por ejemplo, porque el modo cambió con el código en vuelo): el verify responde `403 Auth.Account.NotInvited` en lugar de crear la cuenta. La única excepción es el administrador inicial (`Seed:AdminEmail`), que crea su cuenta en cualquier modo, por código o con Google, porque se crea en su primer ingreso y sin esto una base nueva en `InviteOnly` no deja entrar a nadie; la regla vive solo en `AccountCreationPolicy`, y el pedido le responde igual que a cualquier otro correo (lo único distinto es que el código le llega).
- **Desactivar o eliminar tiene que cortar el acceso en el momento:** además de marcar la fila, se revocan las autorizaciones y los tokens de OpenIddict y se actualiza el `SecurityStamp` para invalidar la cookie. Sin eso, "desactivar" es una etiqueta que no impide nada durante los 15 minutos que vale el access token.
- **`ApplicationUser` es `ISoftDeletable`:** un usuario borrado desaparece de los listados y no puede entrar, pero su historial de ingresos sigue existiendo. Dar de alta el mismo correo restaura la cuenta, con los roles que diga el alta, no con los que tenía antes.
- **Las reglas que protegen al sistema viven en `Application/Services/Users/UserGuards.cs`**, con sus tests unitarios en `Application.UnitTests`. No están en Domain porque hay que contar administradores activos mediante un lector: nadie se saca a sí mismo el rol `Admin`, nadie desactiva ni elimina su propia cuenta, siempre queda al menos un usuario activo con rol `Admin`, y no se borra un rol con usuarios asignados.
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

- Los contratos de repositorios y lectores que consumen los servicios van en `Application/Interfaces/Persistence`; sus implementaciones EF, en `Infrastructure/Persistence/{Repositories,Readers}`. No hay repositorio genérico ni obligación de crear uno por cada entidad.
- Las entidades heredan de `Entity` (Id Guid v7). No hay eventos de dominio: ver `docs/decisions/0003-sin-eventos-de-dominio.md`.
- Cada entidad tiene su `IEntityTypeConfiguration<T>` en `Infrastructure/Persistence/Configurations/`.
- `IAuditable` e `ISoftDeletable` los completan los interceptores; nunca se setean a mano.
- `ExecuteUpdate`/`ExecuteDelete` saltean los interceptores: no se usan con entidades `IAuditable` o `ISoftDeletable` (se borraría físicamente y sin auditoría).
- Las filas borradas se ocultan con un filtro global. Para verlas: `IgnoreQueryFilters()`.
- Para poner en fila operaciones sobre un mismo recurso (los códigos de un destino, los enlaces de una cuenta, los contactos de un número), el repositorio toma un lock de Postgres (`pg_advisory_xact_lock` con la clave de `AdvisoryLockKeys`, o un lock de fila `FOR NO KEY UPDATE`) dentro de la transacción del caso de uso: los locks la exigen (sin ella lanzan `InvalidOperationException`, también con cero claves) y duran lo que ella. La abre solo `IUnitOfWork.ExecuteInTransactionAsync`, en READ COMMITTED; no se sube el aislamiento, porque leer la cuenta después del lock necesita ver lo que el otro acaba de confirmar. No hay `EnableRetryOnFailure`: si alguna vez se activa (por ejemplo, con `AddNpgsqlDbContext` de Aspire), `BeginTransactionAsync` lanza, y no se arregla envolviendo el trabajo en la estrategia de ejecución, porque el trabajo encola correos y mensajes y no se puede repetir.
- **Una sola forma de guardar** (la decisión es el [ADR 0001](docs/decisions/0001-transaccion-explicita-por-caso-de-uso.md); el diseño y la tabla de cada método, el [plan de la Etapa 1](docs/history/plans/2026-09-26-etapa-1-una-sola-forma-de-guardar.md)): `IUnitOfWork.ExecuteInTransactionAsync(trabajo, CommitPolicy, ct)`, una vez por cada método público de un servicio que escribe. `TransactionBoundaryTests` verifica quién puede abrir el límite: solo un punto de entrada (una clase de `Application/Services` que implementa un contrato de `Interfaces/Services`) recibe `IUnitOfWork` y llama a `ExecuteInTransactionAsync`, y solo `UnitOfWork` guarda el contexto (el seed queda afuera). No verifica que cada método que escribe abra su límite ni que abra uno solo: que las escrituras queden adentro lo aseguran las que exigen la transacción (los locks, `IUserRepository`, `IRoleRepository` e `IIdentityService`, más abajo), y que haya uno solo por método, la revisión. El ejemplo a copiar es `RoleService.UpdateAsync` con su `UpdateCoreAsync`. Los `*Operations` que exponen `Validate*Async` para que el servicio valide afuera del límite son transitorios: la Etapa 3 los reemplaza. El patrón, en cinco reglas:
  1. afuera y antes: `ThrowIfNull`, el log de inicio y el validador del pedido (un pedido inválido no abre transacción);
  2. un solo límite con la política escrita: `OnSuccess`, u `OnAnyResult` con un comentario que diga qué queda registrado cuando falla;
  3. adentro, en este orden: los locks (`login-code:` del correo y del número en dos llamadas, filas de contactos, `login-link:` o `user-invitation:`), las lecturas de lo que se va a modificar, las reglas, las escrituras y los efectos que tienen que quedar marcados en la fila (encolar y `MarkSent`);
  4. afuera, después y solo si se confirmó: invalidar caché, la cookie de Google, `Notify` y el log de resultado. El servicio que abre el límite nunca lo envuelve en un try/catch. Lo atrapan desde afuera solo quienes corren el límite de otro como una unidad: `WhatsAppWebhookService`, que ante un 23505 reintenta una vez en un scope nuevo, y los hosts en segundo plano, que atrapan por unidad para que un fallo no corte la vuelta (un contacto en `WhatsAppInboundProcessor`, un registro de envío en `WhatsAppSenderBackgroundService`);
  5. los helpers, o sea las clases de `Application/Services` que no implementan un contrato de `Interfaces/Services` (`*Operations`, `*Issuer`, `*Verifier`, `*Linker`, `*Policy`, `PhoneNumberChange`, `AccountAccessRevoker`, `UserInvitationSender`, `UserGuards`), nunca reciben `IUnitOfWork` ni guardan, y un servicio nunca llama al método de escritura de otro: anidar lanza.

  Dos excepciones, que no son el ejemplo a copiar:
  - `WhatsAppWebhookService` llama a otro servicio, `WhatsAppWebhookPersistence`, que es el que abre el límite, porque esa es la unidad que se reintenta: `WhatsAppWebhookRetry` la vuelve a correr en un scope nuevo. Su trabajo devuelve un `Result` que siempre es un éxito, solo para llevar los contadores, porque el límite pide un `Result`;
  - la cookie del ingreso por código (`LoginCodeVerifier`) y la del canje de enlace (`LoginLinkService`) se escriben adentro del límite, antes del commit; la de Google, después, como pide la regla 4. Se alinean en la Etapa 2, con `ISignInService`.
- `UserManager` y `RoleManager` siguen autoguardando (`AutoSaveChanges` no se toca), pero adentro de esa transacción y con un savepoint por guardado: un 23505 que traduce el repositorio se puede atrapar y la transacción sigue usable. Un error de SQL crudo (55P03, 40P01) nunca se atrapa adentro.
- Las escrituras de cuentas y de roles exigen la transacción igual que los locks: todas las de `IUserRepository` y `IRoleRepository`, y las de `IIdentityService` (que delegan en `IUserRepository`, más los intentos fallidos y el cierre de sesiones). Fuera de un límite lanzan `InvalidOperationException` antes de tocar nada, también cuando un test prepara datos. Los roles se escriben solo por `IRoleRepository`.
- Quedan fuera a propósito: la retención de mensajes (`ExecuteUpdate`), `ConnectService.RevokeAuthorizationAsync` (un UPDATE de OpenIddict), los seeders, las migraciones, el servidor OpenIddict y Data Protection.
- Un lector con caché que se llama adentro de un límite lee en su propio scope, como `SystemSettingsReader`: sobre el contexto de quien llama, su fábrica correría adentro de esa transacción y le ocuparía la conexión que el rollback necesita para soltar los locks. El precio es que, con el caché frío, pide una segunda conexión al pool mientras la del límite sigue tomada. Con una fábrica por clave y un minuto de caché alcanza; una fábrica por fila o por pedido puede agotar el pool. `PermissionService` lee con el contexto de quien llama, así que no se usa adentro de un límite.
- Paginado:
  - el modelo de pedido de Application usa `PagedRequest` y declara `SortableFields` cuando corresponda; el validador usa `PagedRequestValidator<T>`;
  - Infrastructure ordena con `ApplySort` (un mapa campo → expresión, con los mismos nombres, y un desempate único, normalmente el Id, para que las páginas sean estables) y pagina con `ToPagedResultAsync`.
- Filtros de un listado (desde la Fase 5):
  - **viven en `Application/Models/Identity/UserListRequest.cs`**, compartido por listado y conteos; un validador compartido y `UserReader` aplican el mismo filtro. Listado y conteos deben filtrar **exactamente igual** o los conteos dejan de describir el listado.
  - **Un valor que no existe no es un 400, es una lista vacía.** `role=NoExiste` devuelve cero resultados: contestar 400 diría qué nombres de rol existen, y eso no se cuenta por el camino de un filtro. Lo que sí es 400 es un `role=` **presente y vacío**, porque el parámetro ausente ya significa "sin filtro" y devolver todo parecería un filtro roto.
  - Los filtros se enlazan en el controller MVC y se comparan por la columna que tiene índice (para un rol, `NormalizedName`, no `Name`).
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
- Logs con `[LoggerMessage]` (source generator), nunca `logger.LogX(...)` directo (`CA1848` en `warning` rompe el build). Nunca registrar códigos, tokens ni secretos.

## Build

- `TreatWarningsAsErrors`, analizadores `latest-recommended` y estilo en el build. Las advertencias se corrigen; solo se suprimen en `.editorconfig`, con una justificación.
- Las versiones de los paquetes van solo en `Directory.Packages.props` (Central Package Management).
- Los secretos van en user-secrets (Api: `Authentication:Google:ClientSecret`, `Email:Smtp:Password`) o en variables de entorno, nunca en el repo. Excepciones de desarrollo local: la contraseña de Postgres en `src/ArquitecturaBase.AppHost/appsettings.Development.json` y la clave HMAC de los códigos en `src/ArquitecturaBase.Api/appsettings.Development.json`.

## Tests

- **Domain.UnitTests y Application.UnitTests:** xUnit v3, sin dependencias externas.
- **ArchitectureTests:** reglas de capas (NetArchTest y las referencias de cada `.csproj`) y convenciones: paquetes permitidos en Application, sin `IQueryable`/`Expression` en su API pública, sin rutas Minimal API, cada `*Service` con su interfaz, entradas de controller como contratos, `[HasPermission]`, adaptadores en `Api/RequestContext`, formato de las claves de `Errors.resx` y una configuración EF por entidad. Un paquete nuevo en Application, una clave reservada de `Errors.resx` fuera del formato o un `Map*` técnico se suman a la lista de su test (`ApplicationPackagesTests`, `ErrorCodeTests`, `MinimalApiRoutesTests`). `TransactionBoundaryTests` lee el IL con Mono.Cecil: solo un punto de entrada de un caso de uso recibe `IUnitOfWork` (o la clase concreta) y llama a `ExecuteInTransactionAsync`; la clase concreta `UnitOfWork` solo la nombra la registración en DI; solo `UnitOfWork` abre, confirma y guarda, con el guardado resuelto por herencia hasta `DbContext` o por una interfaz, y el seed afuera; el SQL y las claves de los locks viven en un solo lugar, y `ExecuteUpdate`/`ExecuteDelete` solo en la retención. Por reflexión fija además que `IUnitOfWork` tiene un solo método y los valores de `CommitPolicy`. Un prefijo de lock nuevo va en `AdvisoryLockKeys` y en `LockKeyPrefixes` del test, o la regla no lo ve. No verifica que cada método que escribe abra un límite (ver "Una sola forma de guardar").
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
- **Unidad de trabajo en los tests:** los unitarios usan `FakeUnitOfWork` (`TestDoubles`), que aplica la misma regla que producción (`CommitPolicyExtensions.Commits`) y cuenta `Transactions`, `Commits`, `Rollbacks` y `LastPolicy`; lo que hay que mirar "al confirmar" se toma en `OnCommit`, y `CommitFailure` hace fallar el commit. Los dobles de lock, y `FakeIdentityService` en sus escrituras, reciben `InTransaction = () => unitOfWork.InTransaction` y lanzan fuera del límite; lo que el test arma con las escrituras del doble antes del caso de uso va en `FakeIdentityService.ArrangeAsync`. En integración, `FailingCommitUnitOfWork.Replace(services, probe)` hace fallar el commit sobre la unidad real, y `probe.RolledBackBeforeLeaving` confirma que el rollback lo hizo producción.
- Nombres de tests en inglés, como frase: `Deleted_rows_are_hidden_from_queries_and_endpoints`.
