> **HISTÓRICO. Etapa cerrada el 2026-09-28.** No ejecutar: las casillas sin marcar y las instrucciones a agentes no son trabajo pendiente. Registro de cómo se diseñó y se ejecutó la Etapa 3 (Diseño de la Etapa 3 (menos ceremonia en servicios y controllers)), con los commits `5b3c716` a `83e7732`, más `fee16b8` y `45a203e`. El cierre, la puerta cumplida y lo que se hizo distinto están en la sección "Etapa 3" del [plan maestro](../../plans/2026-09-26-plantilla-estandar-por-etapas.md); las reglas vigentes, en `docs/architecture/backend.md` y en `docs/features/`.

> **Diseño de la Etapa 3, parte servicios** (2026-09-28). Lo escribió un agente, lo revisaron tres revisores adversariales y quedó corregido. Rige la [Forma de trabajo desde la Etapa 3](../../plans/2026-09-26-plantilla-estandar-por-etapas.md#forma-de-trabajo-desde-la-etapa-3) del plan maestro. Las citas `archivo:línea` son del commit 1a5e3e2.

# Diseño de la Etapa 3: los servicios en 10 tareas

Las rutas cortas usan dos alias: `App` = `src/ArquitecturaBase.Application` y `Tests` = `tests`.

**Por qué este orden.**
- La 1 va primero para que todo lo nuevo nazca con los namespaces finales.
- La 2 y la 3 van antes de partir, así lo que se mueve ya está simplificado. La 2 trae además el test de registros completos, que cubre desde el primer corte.
- La 4 (modelos y lecturas) va antes de los cortes para que `IUserQueryService` y los demás nazcan con sus tipos finales y con una dependencia menos. El diseño anterior la ponía después y había que rehacer el corte.
- La 9 cierra reglas que las tareas 5 a 8 ya van cumpliendo.
- La 10 junta los pendientes del borde HTTP y la puerta.

**Documentación en cada tarea.** Cada tarea actualiza, en el mismo commit, los docs que nombran lo que toca:
- `identidad.md` líneas 7-8 y 14;
- `whatsapp.md` líneas 14-15;
- `administracion.md` líneas 12, 20 y 31;
- `backend.md` líneas 79, 84, 86, 132, 173, 178 y 244;
- `AGENTS.md` líneas 104 y 106;
- los `AGENTS.md` de carpeta que se citan en cada tarea.

Cada tarea también marca en el plan maestro `[x] Hecho (commit)` en su tarea del plan, como hicieron las anteriores (plan :313, :320, :327, :339). Al cierre queda escrito "Estado: cerrada", con lo que se hizo distinto: `Emails/`, los sufijos nuevos, el orden de las tareas y el corte del bot.

### 1. Subcarpetas de Integrations (pendiente de la tarea 9). Es mecánica.
- **Con cambio de namespace**, porque todo el repo mantiene namespace = carpeta.
- Regla: cada interfaz va en la subcarpeta de quien la implementa.
  - `Identity/`: ISignInService, IPermissionService, IOpenIddictTokenRevoker, IGoogleAvailability, IInitialAdmin, IPublicOrigin.
  - `Security/`: ILoginCodeGenerator, ILoginCodeHasher, ISecureTokenGenerator.
  - **`Emails/` (no `Email/`)**: IEmailQueue, IEmailSender, IEmailTemplateRenderer, IAppName.
    - Un namespace `…Integrations.Email` tapa al value object `Email` en las carpetas hermanas, y el build se rompe con `CS0118`. Pasa, por ejemplo, con `IInitialAdmin.IsInitialAdmin(Email email)` en `Identity/` (`App/Interfaces/Integrations/IInitialAdmin.cs:13`).
    - `Emails/` es además el nombre de la carpeta de quien lo implementa (`Infrastructure/Emails`).
    - Se anota en la tarea 9 del plan como desvío forzado del nombre.
  - `WhatsApp/`: los seis `IWhatsApp*`.
  - `Request/`: ICurrentUser, IRequestInfo. Se actualizan los `using` de `Api/RequestContext/` y de `Api/DependencyInjection.cs`, como pide el plan.
  - `Phones/`: IPhoneNumberParser.
    - No es de WhatsApp: el plan (:485) lo deja en el núcleo.
    - Su implementación vive en `Infrastructure/Phones/LibPhoneNumberParser.cs`.
    - `AGENTS.md:109` ya lista `Phones/` como carpeta de tema.
    - Se decide acá, sin preguntar.
- **Alcance:** son 113 archivos con `using`. Los `cref` relativos rompen el build con CS1574 (`TreatWarningsAsErrors` y `GenerateDocumentationFile` en `Directory.Build.props`). Por ejemplo, `Integrations.ISignInService` en `IUserReader`.
- `ControllerServiceRepositoryTests.cs:53` compara namespaces por prefijo (`…Interfaces.Integrations`). Hay que confirmar que sigue viendo las subcarpetas.
- **Documentación de área:** crear un `AGENTS.md` de una línea y su `CLAUDE.md` (`@AGENTS.md`) en `Interfaces/Integrations/WhatsApp/`, que remite a `whatsapp.md`, y en `Interfaces/Integrations/Identity/`, que remite a `identidad.md`. En `AGENTS.md:106`, mencionar las subcarpetas.

### 2. Un solo validador inyectable (tarea 1 del plan)
- **Validador:**
  - Crear `App/Common/Validation/IRequestValidator.cs` con `ValidateAsync<T>`.
  - Crear `RequestValidator`, `internal sealed` y scoped. Resuelve `GetServices<IValidator<T>>()` del scope y reutiliza tal cual la lógica de `ServiceRequestValidator<T>.ValidateAsync` (`ServiceRequestValidator.cs:13-44`).
  - Borrar `ServiceRequestValidator<T>` y su registro abierto (`App/DependencyInjection.cs:63`).
- **Test en rojo primero:** `ServiceValidationTests` pasa a llamarse `RequestValidatorTests`.
  - Conserva sus casos. `Valid_request_returns_no_error` ya cubre "sin validadores da null" (`ServiceValidationTests.cs:49-58`), así que ese caso no se repite.
  - Suma dos casos. El primero: los validadores registrados para otro tipo no corren. El segundo: un validador con una dependencia scoped funciona con `ValidateScopes`.
- **Doble para los tests unitarios:** `TestDoubles/RequestValidators.For(params IValidator[])` arma el `RequestValidator` **real** sobre un `ServiceCollection` con esos validadores registrados como `IValidator<T>`. No reimplementa ni el agrupado ni el camelCase, porque unos 20 tests de servicio verifican las claves de `errors` a través de esa lógica.
- **`DependencyInjectionTests`:**
  - Se adaptan los dos tests que hoy resuelven `ServiceRequestValidator<>` (`DependencyInjectionTests.cs:60` y `:69`).
  - **Test nuevo que corre sin Docker, `Every_application_dependency_is_registered`:**
    - Arma `new ServiceCollection().AddApplication().AddWhatsAppWebhookApplicationServices()`. Las dos registraciones van juntas: `IWhatsAppInboundService` y compañía solo las registra la segunda (`App/DependencyInjection.cs:68-75`).
    - Toda dependencia de constructor que sea de `Application.Services`, `IRequestValidator` o `Interfaces.Services` tiene que estar registrada.
    - Toda interfaz de `Interfaces.Services` tiene que estar registrada como Scoped.
    - Caso de control: una clase armada en memoria que depende de un tipo de `Application.Services` sin registrar tiene que ser detectada.
    - Así, cada clase nueva de las tareas 5 a 8 queda cubierta desde el día uno. Hoy, un registro faltante solo aparece en integración, como un 500.
  - `Application_services_are_registered_explicitly_as_scoped` (`:77-118`), con su lista fija de parejas, se reemplaza por el test nuevo, que deriva la lista de `Interfaces.Services`. Se conserva la aserción de que `AddApplication()` sola no registra el webhook (`:102`).
- **Integración:** también cambia `WidgetTestService`. Acá solo se comprueba que compila.

### 3. `OperationLog` (tarea 2 del plan)
- **Qué es:** `App/Common/Logging/OperationLog.cs`, `static partial`. Contiene tres `[LoggerMessage]` con la plantilla que ya usa la mayoría: `"Handling {Operation}"`, `"Handled {Operation}"` y `"{Operation} failed with {ErrorCode}"` (`RoleService.cs:218`, `UserService.cs:308`, `SystemSettingsService.cs:84`, `WhatsAppDeliveryService.cs:116`). También contiene `RunAsync<TResult>(ILogger, string operation, Func<Task<TResult>>) where TResult : Result`.
- **Por qué alcanza un solo método:** `Result<T>` hereda de `Result`, así que sirve para los dos. No atrapa excepciones: si el trabajo lanza, queda solo el "Handling", como ya fija `RoleServiceWriteTests`.
- **Por qué `RunAsync` y no un helper más simple:** los métodos con varias salidas hoy repiten `LogFailed` antes de cada `return`, y ahí es donde se olvida. `PreviewAsync` tiene tres salidas de error; `VerifyLoginCodeAsync` y `GetUserAsync`, varias.
- **Qué va adentro del lambda:** el cuerpo entero (validar, abrir el límite, la cookie o el caché).
  - `ThrowIfNull` y `KindOf` quedan afuera, como hoy.
  - Si el lambda devuelve a veces un `Error` y a veces un `Result`, se escribe `RunAsync<Result<T>>` explícito.
- **El request nunca entra al log:** la firma solo recibe el nombre de la operación.
- **Qué cambia en los logs.** Nada es HTTP. La tarea 2 del plan (:282-285) pide unificar, así que se decide sin preguntar.
  - La **categoría** pasa a ser la del servicio que registra. En los servicios que se parten (tareas 5 a 7), la categoría cambia de `AccountService`, `ProfileService` y `UserService` al servicio nuevo.
    - Ningún `appsettings` filtra por esas categorías.
    - Los `FakeLogger<AccountService>` (3), `<ProfileService>` (2) y `<UserService>` (1) de los tests se actualizan en la tarea del corte.
  - Los servicios que usaban la clave estructurada `{RequestName}` (`ProfileService.cs:211-218`) o un texto literal pasan a la clave `Operation` y cambian de `EventName`. Los de texto literal son `AccountService.cs:229-260`, `LoginLinkService.cs:156-172`, `ExternalLoginService.cs:147-155`, `WhatsAppInboundService.cs:310-317` y `WhatsAppWebhookService.cs:52-56`.
  - Los textos de Google y del bot pasan a `ExternalSignIn` y `ProcessWhatsAppContact`. Ningún test compara esos textos.
  - Los demás nombres de operación se conservan: `LoginLinkTests.cs:570` busca "Handling RedeemLoginLink", y siguen igual los mensajes que miran `VerifyLoginCodeServiceTests` y `RequestLoginCodeServiceTests`.
- **Webhook:** `WhatsAppWebhookService` devuelve `bool`, así que llama directo a `OperationLog.Handling` y `Handled`.
- **Tests:**
  - `Common/Logging/OperationLogTests`, en rojo primero: éxito, fallo con código y nivel, excepción, `Result` y `Result<T>`.
  - Test de arquitectura: ningún literal "Handling " ni "Handled " fuera de `OperationLog`, con `CallSites.Literals` y un caso de control.

### 4. Modelos y lecturas (tarea 5 del plan y pendiente a). Va antes de los cortes.
**4a. Renombres y unificación. Commit propio, sin Docker salvo la puerta.**
- **Proyecciones de lector (terminan en `Row`):**
  - `Models/Users/ReadModels/UserDetail` pasa a `UserDetailRow`, `UserListItem` a `UserListRow` y `Models/Roles/ReadModels/RoleListItem` a `RoleRow`.
  - Se borran las carpetas `ReadModels/` y el `FormattedPhoneNumber` de las filas, que el lector nunca llena.
  - En Roles es **solo un renombre**, sin tocar formas ni rutas: la Etapa 4 rehace esos modelos al paginar `GET /api/roles` y arranca de `RoleRow`.
- **Salidas de servicio (terminan en `Response`):** `UserDetailResponse`, `UserListItemResponse`, `PermissionGroupResponse` y `ConnectUserResponse`. `ConnectUser` hoy lo devuelve `IConnectService.GetActiveUserAsync` (`IConnectService.cs:7`). Solo lo consume `ConnectController`, que queda fuera del OpenAPI, así que el renombre no es visible.
- **Unificar los pedidos de listado:**
  - `ListUsersRequest` absorbe los filtros y las constantes del `abstract record UserListRequest` (`Models/Identity/UserListRequest.cs:10`), del que hoy hereda.
  - Se borran `UserListRequest`, `UserFilterCountsRequest`, `UserListRequestValidator<T>` y el validador de conteos.
  - Los conteos reciben `ListUsersRequest`, con las mismas reglas.
- **`ServiceOutputNamingTests`:**
  - **Qué mira:** el tipo de retorno de cada método de `Interfaces.Services`. Desenvuelve `Task`, `Result<T>`, `PagedResult<T>` y las colecciones, y solo mira el tipo de nivel superior; los tipos anidados (`LastInvitation`, `PermissionItem`) no entran.
  - **Qué exige:** si ese tipo es de `Application.Models`, termina en `Response`.
  - **Qué queda afuera:** los escalares (`Guid`, `bool`). La única excepción es `UserFilterCounts`, que lo devuelven el lector y el servicio y sigue el sufijo `*Counts` del ADR 0008; va con su motivo.
  - **Caso de control:** el detector ve un `Result<PagedResult<X>>` e `IReadOnlyCollection<X>`.
- **Regla del `Row`:** va escrita en `backend.md` y acotada. Las filas de listado y de detalle que proyecta un lector terminan en `Row`. `UserAccount` (`Models/Identity`), la vista de cuenta que devuelven los `Find*` de `IUserReader` (`IUserReader.cs:16-45`), queda afuera con su motivo. No se suma a `PersistenceNamingTests`.
- **Archivos que cambian además:**
  - `IdentityBoundaryTests.cs:5` y `:79`: `accountData = [typeof(UserAccount), typeof(UserDetailRow), typeof(UserDetailResponse)]`.
  - `TestDoubles/Users/InMemoryUserAccounts.cs`, `RoleServiceTests.cs`, `RoleServiceWriteTests.cs` y `UserServiceTestHost.cs`.
  - En integración (solo se compila): `Persistence/UserReaderTests.cs:48,66,135` y `RoleReaderTests.cs:48`.
- **Docs:**
  - `administracion.md:20,31` pasa a nombrar `Models/Users/ListUsersRequest.cs` y `ListUsersRequest.CreatedWithinOptions`.
  - El `AGENTS.md` de `Models/Identity` (hoy nombra `UserListRequest`) se reemplaza por uno en `Models/Users`, con su `CLAUDE.md`. `Models/Identity` queda sin puntero si ya no tiene nada del listado.

**4b. La última invitación en el detalle (pendiente a). Commit propio: toca Infrastructure.**
- **El lector:** crear `IUserInvitationReader.FindLatestAsync`, que devuelve `UserInvitationRow(Channel, SentAtUtc, SendFailed, HasWaMessageId, OutboundStatus?)`, con `AsNoTracking`, el mismo orden de hoy y una subconsulta del mensaje saliente.
- **Registro:** `AddScoped<IUserInvitationReader, UserInvitationReader>()` en `Infrastructure/DependencyInjection.cs:67-69`, en este mismo commit.
- **Traducción a estado de entrega:** queda en Application como función pura, con un test en rojo primero por cada rama.
- **Test de integración nuevo:** `Persistence/UserInvitationReaderTests.cs`, con estos casos: sin invitación, la última gana, correo contra WhatsApp, con y sin mensaje saliente, y el estado del mensaje. Se corre en la puerta.
- **Lo que se borra:** `GetLatestAsync` del repositorio de invitaciones. El servicio de consulta suelta `IUserInvitationRepository` (para leer) e `IWhatsAppMessageRepository` (`UserService.cs:274` y `:296`).
- **ADR 0008 (`:18` dice que `GetLatestAsync` "conserva el nombre"):** se suma una nota de consecuencia posterior, sin reescribir la decisión.

### 5. El tope de 8 y el ingreso (tarea 3 del plan)
**5a. Google pone en cero los intentos. Primer commit de la tarea, solo, para poder revertirlo, como en la Etapa 2.**
- **Test en rojo primero, antes de mover nada:** `Google_sign_in_resets_the_failed_attempts` en `ExternalLoginServiceTests`.
  - Arranca con 3 intentos, termina con 0, y los eventos son `["commit","sign-in"]`.
  - `FakeSignInService` lanza si se lo llama fuera de un límite, así que el test también fija que la llamada va adentro.
- **Dónde va la llamada:** igual que en el código (`LoginCodeVerifier.cs:89`) y el enlace (`LoginLinkService.cs:136`). En `ExternalLoginService`, como dice el plan (:292): adentro del límite (`OnAnyResult`), después de los rechazos por cuenta inactiva o bloqueada, y justo antes de la auditoría de éxito.
- Al test de cuenta bloqueada o inactiva se le suma la aserción de que los intentos no cambian.

**5b. `Tests/ArquitecturaBase.ArchitectureTests/ServiceDependencyLimitTests.cs`.**
- **Qué cuenta:** mira toda clase no estática de `Application.Services`, puntos de entrada y helpers, y cuenta el constructor entero, incluidos `ILogger`, `IOptions` y `TimeProvider`.
- **Excepciones:** un diccionario, cada una con su motivo, que exige que cada excepción siga pasándose del tope. Arranca con las clases que hoy lo superan, y las tareas 5c a 8 las van sacando.
- **Al cierre de la tarea 8 el diccionario queda vacío.** No quedan excepciones permanentes, tampoco `WhatsAppInboundService` (ver la tarea 8).
- **Pieza compartida:** `IssuedLoginCode` suma `LifetimeMinutes` y `ResendCooldownSeconds`. Con eso, quienes llaman ya no necesitan `IOptions<LoginCodeOptions>`.

**5c. Partir el ingreso.**

| Queda | Deps |
|---|---|
| `LoginMethodsService : ILoginMethodsService` (GetLoginMethods) | 4 |
| `LoginCodeService : ILoginCodeService` (los dos pedidos y el verify; reemplaza a IAccountService) | 7 |
| `SignInCodeIssuer` (los dos `Request*CoreAsync`, sin cambios) | 8 |
| `LoginCodeVerifier` con `LoginAuditRecorder` | ≤8 |
| `LoginAuditRecorder` (ILoginAuditRepository + IRequestInfo + TimeProvider; `Succeeded`/`Failed` toman la hora al escribir) | 3 |
| `LoginLinkService` / `LoginLinkVerifier` (vista previa y `RedeemCoreAsync`) | 6 / 6 |
| `ExternalLoginService` (signIn, users, userRepository, recorder, accountCreation, IRequestValidator, unitOfWork, logger) | 8 |

- **Sin `ExternalLoginVerifier`.** Hoy `ExternalLoginService` tiene 10 dependencias (`ExternalLoginService.cs:3-13`). El recorder absorbe `ILoginAuditRepository`, `IRequestInfo` y `TimeProvider`, y así el servicio queda en 8 sin un verificador que casi solo delegaría.
- **La hora de la auditoría** la toma el recorder al escribir, igual que hoy, después del lock `external-login:` (`:81-83`, `:145`).
- **Controllers que cambian:** `LoginMethodsController` pasa a inyectar `ILoginMethodsService`, y `LoginCodeController`, `ILoginCodeService`.
- **`IdentityBoundaryTests`:**
  - Quienes llaman a `SignInAsync` pasan a ser exactamente {ExternalLoginService, LoginCodeService, LoginLinkService}.
  - Regla nueva `Only_sign_in_paths_reset_failed_attempts`: `ResetFailedAttemptsAsync` lo llaman exactamente {LoginCodeVerifier, LoginLinkVerifier, ExternalLoginService}, con `Assert.Equal`.
- **Build:** hay que arreglar los `cref` a `AccountService`.

### 6. Perfil (MeController inyecta tres interfaces)
- **Primero, tests de caracterización en rojo o verdes, antes de mover nada.** Hoy `ConfirmPhoneLinkAsync` y `UnlinkOwnPhoneAsync` no tienen ningún test unitario: `ProfileServiceTests` solo llama a `RequestPhoneLinkCodeAsync`. Se escribe `ProfileWhatsAppServiceTests` con los dobles que ya existen. Casos de confirmar (`ProfileWhatsAppOperations.cs:109-161`):
  1. Orden de los locks: código (`login-code:`) → contacto → enlaces de la cuenta → lectura.
  2. Código correcto con número ocupado, o carrera del índice (23505): da `PhoneAlreadyExists` y confirma el código ya gastado (commit con `OnAnyResult`, `ProfileService.cs:165-170`).
  3. Código equivocado: cuenta el intento sin sumar a `RegisterFailedAttempt`.
  4. `VoidPendingLinks` corre solo si el número cambió.

  Casos de desvincular (`:164-192`):
  1. Sin otro medio de ingreso, da `LastLoginMethod`.
  2. No revoca las sesiones.
  3. Siempre suelta el contacto e invalida los enlaces pendientes.

| Queda | Deps |
|---|---|
| `ProfileQueryService : IProfileQueryService` (GetAsync) | 6 |
| `ProfileService : IProfileService` (Update, RequestEmailCode, ConfirmEmail) | 8 |
| `ProfileWhatsAppService : IProfileWhatsAppService` (pedir código, confirmar y desvincular el número propio) | 8 |
| `DestinationCodeIssuer` (Services/Auth: emite y manda el código de verificación) | 6 |
| `PhoneNumberLinker` (reemplaza a PhoneNumberChange: `LockAsync` contacto → enlaces, `VoidPendingLinksAsync`, confirmar el número propio) | 7 |

- **Desvincular el número propio queda en `ProfileWhatsAppService`, separado del desvincular del administrador.** Son dos flujos distintos a propósito:
  - el propio usa `HasOtherLoginMethodAsync`, no revoca las sesiones y siempre invalida los enlaces;
  - el del administrador usa `EnsurePhoneCanBeUnlinkedAsync` y revoca (`UserPhoneOperations.cs:19-50`).

  `PhoneNumberLinker` aporta solo los pasos compartidos (lock y anulación de enlaces).
- `DestinationCodeVerifier` se muda a `Services/Auth`. Se actualiza `App/Services/Users/AGENTS.md:2`, que lo nombra.
- Get queda aparte porque Get y Update juntos suman 9. La alternativa (mover `FindLastSuccessAtUtcAsync` a `IUserReader`) toca Infrastructure.
- **`cref` a `PhoneNumberChange` que rompen el build:** `WhatsAppInboundService.cs:191`, `WhatsAppContactLinker.cs:71` y `AccountAccessRevoker.cs:15` y `:20`.
- **Docs:**
  - `whatsapp.md:15`: aclarar que `PhoneNumberLinker` delega en `WhatsAppContactLinker`, que sigue siendo la única pieza que vincula y suelta.
  - `identidad.md:14`: `DestinationCodeIssuer` y `SignInCodeIssuer` delegan en `LoginCodeIssuer`, que sigue siendo el único que emite.

### 7. Administración de usuarios (UsersController inyecta tres interfaces)
- **Primero, tests de precedencia de la edición, antes de mover nada.** Hoy solo el alta tiene fijado que los roles se miran antes de los locks (`UserServiceWriteTests.cs:53-70`). Se suman:
  1. `NotFound` le gana a `RoleNotFound`.
  2. `RoleNotFound` le gana a `AlreadyExists`.
  3. `LastAdmin` y `CannotModifySelf` le ganan a `AlreadyExists`.
  4. El alta con invitación por WhatsApp toma `lock:` antes de encolar.

| Queda | Deps |
|---|---|
| `UserQueryService : IUserQueryService` (listado, conteos, detalle; usa `IUserInvitationReader` de la tarea 4b) | ≤6 |
| `UserAdministrationService : IUserAdministrationService` (alta, edición, invitación) | 8 |
| `UserAccessService : IUserAccessService` (activar/desactivar, borrar, desvincular número: las tres cortan acceso) | 7 |
| `UserContactLinker` (UserContactParser + pasos separados: `LockAsync`, `EnsureFreeAsync`, `CreateOrRestoreAsync`, `ChangeAsync`) | 6 |
| `UserInvitationIssuer` (antes UserInvitationSender: `Check`, `LockAsync`, `WaitBeforeAnotherAsync`, `SendAsync`) | 8 |
| `WhatsAppInvitationIssuer` (Services/WhatsApp) | 4 |
| `UserGuard` (antes UserGuards, + `EnsureRolesExistAsync`) | 3 |

- **El orden de cada método no cambia.** `UserContactLinker` expone pasos separados para que el servicio los intercale igual que hoy:
  - **Alta** (`UserWriteOperations.cs:43-96`): correo → teléfono → `IdentityRequired` → `Check` de la invitación → roles existentes → locks → alta o restauración → roles → envío.
  - **Edición** (`:102-156`): parseo → locks (destinos, después contacto y enlaces) → `NotFound` → país (solo si el número es nuevo) → roles existentes → `EnsureRolesCanChangeAsync` → `EnsureFreeAsync` → escrituras → cambio de contacto.
  - **Reenvío de la invitación:** lock → NotFound → inactiva → Check → espera → envío.
- **`UserInvitationIssuer.SendAsync` sigue tomando el lock de invitaciones** antes de agregar y encolar (`UserInvitationSender.cs:84`).
  - El alta solo llama a `SendAsync` (`UserWriteOperations.cs:93-96`), y `WhatsAppDeliveryService.cs:75-77` depende de ese lock para encontrar la invitación después del commit.
  - `LockAsync` es solo el primer lock del reenvío. El segundo, reentrante, queda adentro de `SendAsync`, como fija `UserInvitationServiceTests.cs:54`.
- **Locks de acceso:**
  - `UserAccessService.UnlinkUserPhoneAsync` toma `PhoneNumberLinker.LockAsync` (contacto → enlaces) y nunca un lock del revocador.
  - `SetActiveAsync` toma el lock de enlaces solo si `!isActive`, y antes de `FindById`. `DeleteAsync` lo toma siempre, antes de `FindById` (`UserStatusOperations.cs:21-25` y `:55`).
  - `AccountAccessRevoker` **no suma `LockAsync`**: su `<remarks>` (`AccountAccessRevoker.cs:19-21`) deja el orden a quien llama, y se mantiene así.
  - `UserServicePhoneTests` y `UserServiceStatusTests` siguen sin cambios en sus aserciones.
- **Tests de integración que solo se compilan acá:** `UserServiceWriteIntegrationTests` y `Persistence/UserRepositoryTransactionTests.cs:41-190`, que hoy resuelven `IUserService`.
- **Build y docs:** hay que arreglar los `cref` a `UserGuards` y `UserInvitationSender`. Se actualiza el comentario de `ApplicationServicesTests.cs:17-18`, que nombra `UserGuards` y `PhoneNumberChange`.

### 8. El bot de WhatsApp (sale de las excepciones del tope)
- **Por qué no queda como excepción:** el plan (:287-288) fija el tope de 8 y nombra a `WhatsAppInboundService`, que hoy tiene 16 dependencias (`WhatsAppInboundService.cs:21-37`). La Etapa 6, que lo rearmaría, está postergada sin fecha. Sus invariantes sí están fijadas por tests unitarios que corren acá: los 34 de `WhatsAppInboundServiceTests`, entre ellos el lock antes de leer (`:563`), la relectura después del lock (`:627`, `:659`, `:685`) y una sola transacción (`:705`).
- **Corte por responsabilidad, con esos 34 tests como red.** Solo cambia la construcción en el test host; las aserciones no se tocan.

| Queda | Deps |
|---|---|
| `WhatsAppInboundService` (contacts, messages, phoneNumbers, `WhatsAppReplyPolicy`, outbox, unitOfWork, timeProvider, logger) | 8 |
| `WhatsAppReplyPolicy` (`DecideAsync` + `FindAccountAsync`: users, signIn, accountCreation, accountLocks, `WhatsAppLinkIssuer`, appName) | 6 |
| `WhatsAppLinkIssuer` (`SendLoginLinkAsync` + `CreateAccountAsync`: loginLinks, contactLinker, userRepository, publicOrigin, appName) | 5 |

- **Lo que sigue igual:** el orden contacto → cuenta, la relectura después del lock y la única transacción quedan en el mismo flujo; el límite lo sigue abriendo el servicio. La regla de `IdentityBoundaryTests` (los tipos de `Services/WhatsApp` llaman a `ISignInService` solo para `IsLockedOutAsync`) sigue valiendo.
- **Al cerrar esta tarea,** el diccionario de excepciones de `ServiceDependencyLimitTests` queda vacío.

### 9. Convención de helpers (tarea 4 del plan)
- Los `*Operations` y el `Change` ya desaparecieron en las tareas 5 a 8, porque los helpers nacen con su sufijo. Esta tarea es la regla escrita más su test.
- **Tabla de sufijos:** los cinco del plan más `Revoker` y `Recorder`.
  - `AccountAccessRevoker` ya existe y `backend.md:178` e `IdentityBoundaryTests.cs:26` lo tratan como helper; `Recorder` es nuevo.
  - Se amplía la tabla del spec, se corrige `AGENTS.md:104` (`*Guards` pasa a `*Guard`, y se suman `*Linker`, `*Revoker` y `*Recorder`) y se actualiza la lista de helpers de `backend.md:178`.
- **`ApplicationHelpersTests`:**
  - **Qué mira:** los descriptores de `AddApplication().AddWhatsAppWebhookApplicationServices()` cuyo `ImplementationType` es del ensamblado de Application y vive en `ArquitecturaBase.Application.Services`, y que no implementan `Interfaces.Services`.
  - **Qué exige:** cada uno es `internal sealed`, vive en `Services/<Área>` y termina con un sufijo de la tabla.
  - **Por qué ese filtro:** deja afuera por construcción a los validadores FluentValidation, al `RequestValidator` (`Common/Validation`), a las opciones y a los tipos que se crean con `new` o son estáticos (`BotReply`, `IssuedLoginCode`, `IssuedLoginLink`, `InvitationFields`, `UserCultures`). No hace falta listarlos.
  - **Casos de control:** el filtro ve al menos un helper conocido (`AccountAccessRevoker`), y un tipo armado en memoria con un sufijo fuera de la tabla falla, como `TransactionBoundaryTests.TypeReceiving`.
- **`backend.md`:** se suman los modelos (`Response` y `Row` acotado), como pide la Etapa 5, tarea 3 (plan :425). Se marca ese avance allá.

### 10. Pendientes del borde HTTP (b, c y OpenAPI) y la puerta
- **(b) `IdentityBoundaryTests`:**
  - Hoy no ve `IAuthenticationService.SignInAsync`. Se suma a `SessionOwners`, con un caso de control al pie del archivo, en rojo primero.
  - Regla nueva: `HttpContext.SignOutAsync` e `IAuthenticationService.SignOutAsync` los llaman exactamente {ConnectController, SignInService}, con `Assert.Equal`.
- **(c) `FromError(Error)`: se borra.** El plan lo dejó "por decidir" (:328), y es interno.
  - `ProblemDetailsMapperTests` pasa a la sobrecarga de tres argumentos, con la fábrica de `new ServiceCollection().AddControllers()` y un `DefaultHttpContext`.
  - Esa sobrecarga siempre agrega el `traceId` (`ProblemDetailsMapper.cs:47-63`). Por eso `Metadata_cannot_override_reserved_extensions` (`ProblemDetailsMapperTests.cs:100-105`) cambia `Assert.False(ContainsKey("traceId"))` por `Assert.NotEqual("fake", problem.Extensions["traceId"])`, que es lo que protege de verdad: que la metadata no pise la clave reservada.
  - Hay que arreglar los `cref` de `ProblemDetailsMapper.cs:43` y `:80`.
  - Corre sin Docker con `--filter-class`, porque no usa `ApiTestGroup`.
- **(d) OpenAPI, lo que falta de la tarea 10 del plan (:340):**
  - `[ProducesProblem(409)]` en las altas y ediciones de usuarios y roles.
  - `[ProducesProblem(403)]` en el verify del código y en el canje del enlace (`Auth.Account.Disabled` y `NotInvited`).
  - Una regla en `ProblemResponsesConvention` que deduce el 429 de `[EnableRateLimiting]`.
  - Un caso nuevo en `OpenApiTests`.
  - El cambio es aditivo en el documento, y el front no usa los esquemas (precedente del plan en :313).
- **Esquemas del OpenAPI:** cambian de nombre (`UserDetail` pasa a `UserDetailResponse`, etc.) y el JSON no cambia. El plan ya fijó ese precedente (:313), así que se decide sin preguntar.
- **Puerta:** la Etapa 3 **no se cierra sin `dotnet test` completo con Docker**, como exigen el plan (:25-27, :265) y `AGENTS.md`. Acá corren el build sin advertencias y Domain, Application y Architecture en verde.
  - Esta lista **no es la puerta**, es una guía de riesgo para revisar primero:
    - `Auth/*` y `ExternalLoginTests` (con el reinicio de intentos con Google);
    - `Users/*`, `Me*` y `UserInvitationEndpointsTests`;
    - `Persistence/*` (con `UserInvitationReaderTests`, `UserReaderTests`, `RoleReaderTests`, `UserRepositoryTransactionTests` y `UnitOfWorkTransactionTests`);
    - `WhatsApp/*` (el bot se parte en la tarea 8);
    - `Roles/*` y `Settings/*`;
    - `Contracts/*` (`ExplicitRouteInventoryTests` y los contratos HTTP de administración y de WhatsApp);
    - `OpenApiTests` y `ErrorHandling`.
  - Al cierre, "Estado: cerrada" en el plan, con los desvíos anotados.

### Archivos críticos para implementar
- `src/ArquitecturaBase.Application/Services/Auth/AccountService.cs`
- `src/ArquitecturaBase.Application/Services/Auth/ExternalLoginService.cs`
- `src/ArquitecturaBase.Application/Services/Users/UserService.cs`
- `src/ArquitecturaBase.Application/Services/Users/UserWriteOperations.cs`
- `src/ArquitecturaBase.Application/Services/Users/UserInvitationSender.cs`
- `src/ArquitecturaBase.Application/Services/Users/ProfileWhatsAppOperations.cs`
- `src/ArquitecturaBase.Application/Services/WhatsApp/WhatsAppInboundService.cs`
- `src/ArquitecturaBase.Application/DependencyInjection.cs`
- `src/ArquitecturaBase.Infrastructure/DependencyInjection.cs`
- `tests/ArquitecturaBase.ArchitectureTests/IdentityBoundaryTests.cs`
- `tests/ArquitecturaBase.Application.UnitTests/DependencyInjectionTests.cs`

---

### Hallazgos aceptados
- 1, alta: la carpeta `Email/` pasa a `Emails/`. `…Integrations.Email` tapa al value object en `IInitialAdmin.cs:13` (CS0118). Queda anotado como desvío del plan.
- 1, baja: se crean un `AGENTS.md` y su `CLAUDE.md` en `Integrations/WhatsApp` e `Integrations/Identity`.
- 2, media: el test de registros completos se adelanta a la tarea 2 y arma las dos registraciones. Reemplaza la lista fija de `DependencyInjectionTests.cs:77-118`, que nombraba `IAccountService`, `IUserService` e `IProfileService`, y suma un caso de control.
- 2, baja: se quita el caso duplicado "sin validadores da null". El doble usa el `RequestValidator` real.
- 3, baja: se corrige que "la categoría no cambia". Se decide la clave `Operation` (la plantilla mayoritaria) y se enumeran los `EventName` y las categorías que cambian.
- Orden, media: modelos y lecturas pasan de la tarea 8 a la 4, antes de los cortes.
- 4, alta: `IUserInvitationReader` suma su test de integración y su registro en Infrastructure en la misma tarea (4b).
- 4, media: `ServiceOutputNamingTests` mira solo el tipo de nivel superior desenvuelto, excluye los escalares y renombra `ConnectUser` a `ConnectUserResponse`.
- 4, media: `IdentityBoundaryTests.cs:5,79` y los dobles de Roles y Users quedan en la lista de archivos.
- 4, media: se actualizan `administracion.md:20,31`, el `AGENTS.md` de `Models/Identity` y una nota en el ADR 0008:18.
- 4, baja: la regla del `Row` queda acotada, con `UserAccount` afuera. Roles es solo un renombre, para la Etapa 4.
- 5, media: el reinicio de intentos con Google es el primer commit de la tarea, solo, en `ExternalLoginService`, como dice el plan.
- 5, baja: `LoginAuditRecorder` toma su propio `TimeProvider`. Se elimina `ExternalLoginVerifier` (queda en 8), y la hora de la auditoría queda después del lock.
- 6, media: tests de caracterización de confirmar y desvincular el número propio antes de partir.
- 5/6, media: el desvincular propio y el del administrador quedan separados. `AccountAccessRevoker` no suma `LockAsync`, y se fijan los locks de `SetActive` y `Delete`.
- 5/6, baja: se listan los `cref` a `PhoneNumberChange`, `UserGuards`, `UserInvitationSender` y `AccountService`.
- 7, media: `UserInvitationIssuer.SendAsync` conserva su lock. Hay un test del alta con WhatsApp que exige el lock antes de encolar.
- 7, media: se escribe el orden del alta y de la edición. `UserContactLinker` expone pasos separados, y se suman tests de precedencia de la edición.
- 8 (tope), alta: `WhatsAppInboundService` se parte en la tarea 8 con sus 34 tests unitarios como red. El motivo anterior era falso, y no quedan excepciones permanentes.
- 9, media: `ApplicationHelpersTests` filtra por `ImplementationType` en `Application.Services`, con dos casos de control. Se corrige la lista de tipos que quedan afuera.
- 10, alta: se suman los 409, 429 y 403 del OpenAPI, lo que faltaba de la tarea 10 del plan.
- 10, media: la aserción del `traceId` en `ProblemDetailsMapperTests` pasa a `NotEqual("fake", …)`.
- Puerta, alta: la puerta es `dotnet test` completo con Docker. La lista de suites queda como guía, ampliada con Persistence, WhatsApp, Roles, Settings, Contracts y OpenApiTests.
- Docs, media: se suman `AGENTS.md:104,106`, `backend.md:79,84,86`, `whatsapp.md:15`, `identidad.md:14`, `Services/Users/AGENTS.md:2`, el comentario de `ApplicationServicesTests.cs:17-18`, las marcas del plan maestro y los modelos en `backend.md` (plan :425).

### Hallazgos descartados
- Docs, corregir el comentario de `LoginLinkTests.cs:264` ("CLAUDE.md, Fase 4"): ya está corregido. `grep` no lo encuentra en `tests/`.
- Docs, actualizar `docs/specs/2026-09-20-fase-4-administracion-design.md:3`: el documento tiene la cabecera "histórico para la estructura del código" y no rige código nuevo.
- Partir la tarea 8 original en dos: se acepta en espíritu como 4a/4b dentro de una sola tarea, para no pasar de las 10 que fija el plan.
- Preguntas P2, P3, P5 y P6 como decisiones del usuario: se resuelven con precedentes del plan (:485, :313, :282-285) o ya estaban decididas.

### Para preguntarle al usuario
Nada frena la etapa: ninguna corrección cambia comportamiento visible ni contradice una decisión escrita. Las dos que se apartan del texto del plan se toman sin preguntar y quedan anotadas en él:
- `Emails/` en lugar de `Email/`, forzado por el compilador.
- Los sufijos `Revoker` y `Recorder`, que amplían la tabla sin cambiar las cinco entradas que ya tiene.

El único cambio funcional sigue siendo el reinicio de intentos con Google, que el usuario ya decidió.