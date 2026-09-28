> **HISTÓRICO. Etapa cerrada el 2026-09-28.** No ejecutar: las casillas sin marcar y las instrucciones a agentes no son trabajo pendiente. Registro de cómo se diseñó y se ejecutó la Etapa 7 (Diseño del resto de la Etapa 7 (dominio, producción y blindaje final)), con los commits `f0f1f49` a la guía de despliegue. El cierre, la puerta cumplida y lo que se hizo distinto están en la sección "Etapa 7" del [plan maestro](../../plans/2026-09-26-plantilla-estandar-por-etapas.md); las reglas vigentes, en `docs/architecture/backend.md` y en `docs/features/`.

> **Diseño del resto de la Etapa 7** (2026-09-28). Lo escribió un agente, lo revisó un revisor adversarial y quedó corregido, con las decisiones del usuario. Rige la [Forma de trabajo desde la Etapa 3](../../plans/2026-09-26-plantilla-estandar-por-etapas.md#forma-de-trabajo-desde-la-etapa-3) del plan maestro.

# Diseño de la Etapa 7 (resto): dominio, producción y blindaje final, en 10 tareas

Sigue la "Forma de trabajo desde la Etapa 3". Alias: `App` = `src/ArquitecturaBase.Application`, `Infra` = `src/ArquitecturaBase.Infrastructure`, `IT` = `tests/ArquitecturaBase.Api.IntegrationTests`, `Arch` = `tests/ArquitecturaBase.ArchitectureTests`. No hay Docker: acá corren el build, Domain, Application y Architecture, y `dotnet ef migrations has-pending-model-changes`.

**Antes de la tarea 1:** este diseño se guarda como `docs/history/plans/2026-09-28-etapa-7-dominio-produccion.md` y se enlaza desde la Etapa 7 del plan maestro. Commit `docs:`.

**Hallazgos que cambian el enunciado:**
- **El nombre largo ya es un error de validación por HTTP.** Lo responden `CreateUserRequestValidator:15`, `UpdateUserRequestValidator:13` y `UpdateProfileRequestValidator:13` con 400 y `errors.displayName`. El recorte en silencio (`ApplicationUserMapper.TrimDisplayName`, `ApplicationUserMapper.cs:8`) solo lo alcanzan dos nombres que nadie tipea:
  - Google: `ClaimTypes.Name`, en `SignInService.cs:90` → `ExternalLoginService.cs:99`;
  - WhatsApp: `contact.ProfileName`, que ya llega limpio y con hasta 256 caracteres (`WhatsAppContact.cs:23,173`), en `WhatsAppLinkIssuer.cs:68`.
- **Bug posible en ese recorte.** `displayName[..100]` puede partir un par sustituto, y un nombre de Google puede traer `\0`. Npgsql lanza y el ingreso con Google responde 500. El recorte seguro ya existe en `Domain/WhatsApp/WhatsAppText.cs:44-90` (`Sanitize`, `Truncate`, `Clean`), pero es `internal` y es de WhatsApp.
- **El seed al arrancar rompería el arnés si corriera en `Testing`.** `ApiFactory` arranca el host al primer `Services` (`ApiFactory.cs:98`), antes de `EnsureCreatedAsync` y de `SeedDatabaseAsync` (`:98-102`). Con P2 = A no corre. Revisé todos los caminos del arnés:
  - `WithWebHostBuilder` hereda `Testing` (`ApiFactory.cs:200`);
  - solo `OpenApiTests.DevelopmentApi` (`OpenApiTests.cs:233-237`) cambia de ambiente, a `Development`, que ya hoy migra y siembra sobre su propia base vacía;
  - `MigrationsTests`, `SystemSettingsSeedTests` e `InitialAdminSignInTests.NewInstallation` quedan en `Testing` y siembran a mano.
- **`OpenIddictRegistration.TestingEnvironment` es `internal`** (`OpenIddictRegistration.cs:9-12`). La Api no tiene `InternalsVisibleTo` sobre Infrastructure, así que `Program.cs` no lo puede usar.
- **`PersistenceRegistration.cs` no existe.** Hoy los registros están repartidos:
  - en `Infra/DependencyInjection.cs:42-79`: los interceptores, el `DbContext`, el health check, `IUnitOfWork`, 10 repositorios, 4 lectores, `RegistrationOptions` y `SystemSettingsSeeder`;
  - en `IdentityRegistration.cs:98,100`: `IPermissionReader` y `RoleSeeder`;
  - en `OpenIddictRegistration.cs:86`: `OpenIddictSeeder`.
- **La cola de WhatsApp ya tiene la forma pedida.** `TryEnqueue` devuelve `bool`, la capacidad sale de `WhatsApp:QueueCapacity` y descartar deja un log. Solo el correo cambia de forma.
- **Los tests de integración nombran repositorios concretos:** `StaleReadsMessageRepository.cs:33`, `HeldContactRepository.cs:45`, `HeldLoginLinkRepository.cs:31`, `StaleUserReads.cs:65`, `WhatsAppLockOrderTests.cs:32,56` y `LoginCodeRepositoryTests.cs:28`. La regla nueva de la tarea 6 solo lee `src`.

---

### 1. Versionado en `AGENTS.md` y la regla de `HasPermission`
- **Archivos:**
  - `AGENTS.md`, en "Casos de uso MVC y borde HTTP": una línea que dice que las rutas son `api/<recurso>`, sin versión, y que el primer cambio incompatible introduce `Asp.Versioning` con un ADR nuevo ([ADR 0005]).
  - `Arch/PermissionAuthorizationTests.cs`, regla nueva `Every_required_permission_exists`. Lee `HasPermissionAttribute.Permission` en clases y en métodos; cada uno tiene que estar en `Permissions.All`.
- **Tests:** un caso de control, un controller `file`-local con `[HasPermission("users.raed")]`, visto en rojo.
- **Plan maestro:** se tilda la "Regla posible, no pedida" de la tarea 8 de la Etapa 7.
- **Riesgo:** ninguno.

### 2. Dónde viven los errores
- **Archivos:**
  - `Domain/Authentication/WhatsAppErrors.cs` pasa a `Domain/WhatsApp/WhatsAppErrors.cs`, con namespace `Domain.WhatsApp`. **El código `Auth.WhatsApp.CountryNotSupported` no cambia**, porque el front lo usa en `features/users/errors.ts`.
  - Se ajustan los `using` de `DestinationCodeIssuer`, `SignInCodeIssuer` y `UserContactLinker`, y de 5 tests: `MeWhatsAppEndpointsTests`, `UpdateUserContactTests`, `CreateUserWithPhoneTests`, `RequestWhatsAppLoginCodeServiceTests` y `UserAdministrationServiceTests`.
- **La regla**, en `AGENTS.md` "Result y errores" y en `backend.md` "Validación, guardado y errores":
  - `<Entidad>Errors` vive en `Domain/<Área>/`, al lado de la entidad o del área que define los errores, aunque solo lo use Application;
  - el prefijo del código no tiene que coincidir con la carpeta: `Roles.Role` está en `Authorization`.
- **Test nuevo, `Arch/ErrorDeclarationTests`:**
  - Todo tipo con un miembro estático de tipo `Error` es `public static`, se llama `*Errors` y está en `ArquitecturaBase.Domain.<Área>`, fuera de `Common`, `Results` y `ValueObjects`.
  - **Salvo `Error` mismo:** `Error.None` (`Results/Error.cs:9`) es un miembro estático de tipo `Error`. Se excluye por tipo, con `Assert.Contains` para que la exclusión no quede vieja.
  - Por el IL, nadie fuera de Domain llama a las fábricas `Error.Failure`, `Validation`, `Unauthorized`, `Forbidden`, `NotFound`, `Conflict` ni `TooManyRequests`. Hoy ya cumple (verificado con grep).
  - **Los constructores de `ValidationError` quedan permitidos** y se documenta: son errores por campo que arman `RequestValidator.cs:46`, `FieldErrors.cs:20` y `ExternalLoginController.cs:44`. `FieldErrors` tampoco cae en la primera regla, porque no declara ningún `Error`.
- **Riesgo:** bajo. La Etapa 6 (postergada) llevaría `Domain/WhatsApp` al módulo, y este archivo iría con él.

### 3. Reglas de la cuenta
- **`Domain/Common/StorableText.cs` (nuevo, `internal static`):** sale de `WhatsAppText` con `IsStorable`, `Sanitize`, `Truncate` y `Clean`. `WhatsAppText` delega o se reemplaza por él. Así `Domain/Users` no depende de `Domain/WhatsApp`, que el ADR 0007 quiere poder quitar. `WhatsAppContactTests` y `WhatsAppMessageTests` no cambian.
- **`Domain/Users/AccountRules.cs` (nuevo, `public static`):**
  - `DisplayNameMaxLength = 100`, `CultureMaxLength` y `TimeZoneIdMaxLength`;
  - `EnsureHasContact(bool hasEmail, bool hasPhone)`, que lanza `ArgumentException` porque es un bug (hoy está en `UserRepository.cs:216-219`);
  - `IsValidDisplayName(string?)`;
  - `FitExternalDisplayName(string?)`: limpia, saca los espacios de los bordes y recorta sin partir un par sustituto; `null` o vacío devuelven `null`. Es lo mismo que `WhatsAppContact.NormalizeProfileName`, con 100 de tope.
- **`Infra/Identity/ApplicationUser.cs`:**
  - `DisplayName`, `Culture`, `TimeZoneId` e `IsActive` pasan a `private set`. EF los materializa por el backing field, y `ApplicationUserConfiguration` no fija el modo de acceso;
  - **el constructor público sin parámetros se queda**, porque `IT/Persistence/IdentityModelTests.cs:55` arma usuarios con inicializador de objeto (solo propiedades de `IdentityUser`);
  - métodos nuevos:
    - `static Create(...)`, con el contacto obligatorio y `UserName = Id`;
    - `Rename(string?)`, que lanza si pasa del máximo;
    - `SetActive(bool)`;
    - `UpdatePreferences(culture, timeZoneId)`;
    - `Restore(displayName)`, que reemplaza al `Restore()` de hoy (`:51`) y absorbe `IsActive`, el nombre, `AccessFailedCount` y `LockoutEnd` de `UserRepository.RestoreAsync:99-103`;
  - las constantes toman el valor de `AccountRules`, así que el modelo EF no cambia. Se verifica con `has-pending-model-changes`.
- **`UserRepository`** usa esos métodos en `:99-103,171,181,201-203,221-231`. Se borra `ApplicationUserMapper.TrimDisplayName`, y el comentario de `IdentityRegistration.cs:62` pasa a nombrar `ApplicationUser.Create`.
- **`App/Common/Validation/ValidationRules.cs:14`:** `DisplayNameMaxLength = AccountRules.DisplayNameMaxLength`. Se borra el comentario "tiene que coincidir".
- **Dónde se recorta:** en la entrada del dato ajeno, en Application (P4):
  - `ExternalLoginService.cs:99` pasa `AccountRules.FitExternalDisplayName(login.DisplayName)`;
  - `WhatsAppLinkIssuer.cs:68`, lo mismo con `contact.ProfileName`.
- **Dónde se valida:** en los tres validadores de HTTP, que no cambian. Los otros llamadores ya pasan nombres validados (`UserContactLinker.cs:142,260` desde el alta, `UserAdministrationService.cs:198`, `ProfileService.cs:97`) o `null` (`LoginCodeVerifier.cs:186,211`). El repositorio lanza si le llega un nombre largo, porque es un bug.
- **Tests, rojo primero:**
  - `Domain.UnitTests/Users/AccountRulesTests`: nulo, vacío, espacios, corto, exacto, largo, un emoji en el corte y un `\0`;
  - `ExternalLoginServiceTests.A_long_Google_name_is_cut_to_the_limit`;
  - `WhatsAppInboundServiceTests`: «Crear cuenta» con un perfil de 150 caracteres.
- **Tests que cambian en `IT`:**
  - `MeProfileEndpointTests.Repository_profile_update_truncates…` (`:198`) pasa a `…rejects_a_name_over_the_limit`: lanza y no queda nada guardado;
  - se borra `CreateUserEndpointTests.The_display_name_limit_matches_the_column` (`:168-172`), que ahora sería una tautología;
  - `user.IsActive = false` pasa a `user.SetActive(false)` en 4 tests: `ConnectFlowTests:116`, `MvcConnectPassthroughTests:101`, `ExternalLoginTests:254` y `LoginSecurityTests:159`. Verifiqué con grep que no hay otras asignaciones a esas cuatro propiedades en `src` ni en `tests`.
- **Plan maestro:** anotar el desvío de la letra (`:543` decía "pasa a ser error de validación"). P4 decidió recortar en la entrada.
- **Pendiente del front** (no se toca acá): `maxLength={100}` en los inputs de nombre de `ProfilePage.tsx:163`, `UserFormDialog.tsx:231-236` y `UserEditDialog.tsx:346-350`.

### 4. Renombre de la cola de WhatsApp (mecánico)
- **Renombres:**
  - `IWhatsAppOutbox` → `IWhatsAppSendQueue`;
  - `WhatsAppOutbox` → `WhatsAppSendQueue`;
  - `DisabledWhatsAppOutbox` → `DisabledWhatsAppSendQueue`;
  - los dobles: `CapturingWhatsAppOutbox`, la clase de `AuthFakes.cs:278`, `RecordingOutbox`, `RejectingOutbox` y `FullOutbox`, y los de `WhatsAppBotTests:500` y `UserInvitationEndpointsTests:557`;
  - `WhatsAppOutboxTests` → `WhatsAppSendQueueTests`.
- **Referencias:** `WhatsAppRegistration.cs:62`, `WhatsAppSenderBackgroundService`, `IWhatsAppCloudClient.cs:5`, `ApiFactory.cs:84,261-262` y `docs/features/whatsapp.md:27,45`.
- **Sin cambio de comportamiento.** La categoría del log pasa a `WhatsAppSendQueue`. Ningún test filtra por esa categoría (verificado).
- **Riesgo:** ninguno. Es un commit `refactor:` solo.

### 5. La cola de correo con `TryEnqueue` (P5 = B)
- **Cambios:**
  - `IEmailQueue.EnqueueAsync` pasa a `bool TryEnqueue(EmailMessage)`;
  - `EmailOptions.QueueCapacity` nace con `[Range(1, 10_000)]` y 100 por defecto;
  - `EmailQueue` recibe `IOptions<EmailOptions>`, y se borra `EmailQueue.Capacity`;
  - el log de cola llena se mantiene.
- **Los llamadores con la cola llena:**

  | Llamador | Hoy | Con el cambio |
  |---|---|---|
  | `SignInCodeIssuer:58`, correo | descarta, marca enviado; 202 | `MarkSent` solo si entró. Invisible: `SentAtUtc` solo lo cuenta el tope de WhatsApp (`LoginCodeRepository.cs:74`) |
  | `DestinationCodeIssuer:53`, correo del perfil | igual; 202 | igual que el anterior |
  | `UserInvitationIssuer:107`, invitación por correo | queda como enviada; 202/201 | **P5 = B**: `MarkSendFailed()` y log. `GetLatestSentAsync` ya filtra `!SendFailed` (`UserInvitationRepository.cs:21`), así que el reenvío no da 429. `LastInvitation` no muestra estado para el correo (`LastInvitation.cs:23`) |
  | Códigos por WhatsApp (`SignInCodeIssuer:98`, `DestinationCodeIssuer:103`) | no marca enviado; 202 | sin cambio |
  | `WhatsAppInvitationIssuer:45` | `MarkSendFailed` y log | sin cambio |
  | Bot (`WhatsAppInboundService:96`) | lanza, deshace, reintenta | sin cambio |

- **`UserInvitationIssuer`** suma `ILogger` con un `[LoggerMessage]` sin datos personales: queda en 8 dependencias, el tope de `ServiceDependencyLimitTests`. Se actualiza su XML doc (`:85-91`).
- **Documentar:**
  - una sección "Colas en memoria" en `backend.md`: un reinicio pierde lo encolado, un commit fallido deja salir el mensaje, la cola nunca espera;
  - los XML docs de las dos interfaces y de `EmailQueue.cs:8-13`;
  - `WhatsAppOptions.cs:62`;
  - una fila `Email:QueueCapacity` en `README.md:237`.
- **Tests:**
  - rojo primero en `RequestLoginCodeServiceTests` y `ProfileEmailServiceTests`: con la cola llena, el código queda sin enviar;
  - rojo primero en `UserAdministrationServiceTests` (invitación): con la cola llena, la invitación queda `SendFailed`;
  - dobles que cambian: `FakeEmailQueue` (`AuthFakes.cs:209`) y los dos `RecordingEmailQueue`;
  - **`ThrowingEmailQueue` (`UserRepositoryTransactionTests.cs:215-224`) hoy es asíncrono y consulta la base.** Con `TryEnqueue` sincrónico pasa a consultas sincrónicas (`Single`/`Any`) sobre el mismo contexto. El test sigue probando que el alta autoguardada se deshace;
  - `EmailQueueTests` lee la capacidad de las opciones, y `EmailBackgroundServiceTests` usa `TryEnqueue`;
  - `WhatsAppOutboxTests:33` (ya renombrado) compara con `new EmailOptions().QueueCapacity` y no con `EmailQueue.Capacity`.
- **Riesgo:** bajo. Ningún status HTTP ni texto cambia, salvo el 429 que P5 saca.

### 6. `PersistenceRegistration`
- **Archivo nuevo:** `Infra/Persistence/PersistenceRegistration.cs`, con `AddPersistence(configuration)`. Recibe:
  - los dos interceptores, **en el mismo orden** (primero el borrado lógico, después la auditoría). Es el único orden que importa: `AddDbContext` los resuelve con `GetServices` al armar las opciones;
  - `AddDbContext`, con la misma lambda; no hay pooling;
  - `AddDbContextCheck<ApplicationDbContext>("database")`, sin el tag `live`;
  - `IUnitOfWork`;
  - los 10 repositorios y 4 lectores de `DependencyInjection.cs:59-72`, más `IPermissionReader` (`IdentityRegistration.cs:98`);
  - los tres seeders (`IdentityRegistration.cs:100`, `OpenIddictRegistration.cs:86` y `DependencyInjection.cs:79`), con `RegistrationOptions` (`:74-77`), que solo usa `SystemSettingsSeeder`.
- **Se quedan donde están:**
  - `DependencyInjection.DatabaseConnectionName`, que es público y lo usan muchos tests como `InfrastructureSetup.DatabaseConnectionName`. `AddPersistence` lo lee de ahí;
  - Data Protection (`IdentityRegistration.cs:85-87`), `AddHybridCache` y `IOpenIddictTokenRevoker`, que no son persistencia.
- **`Arch/TransactionBoundaryTests`:** `UnitOfWorkRegistration` (`:22`) pasa a `…Persistence.PersistenceRegistration`.
- **Test nuevo en `Arch`:** ningún tipo de `src` (Application, Infrastructure y Api, como `TransactionBoundaryTests`) nombra en su IL una clase concreta de `Infrastructure.Persistence.Repositories`, `.Readers` o `.Seed`.
  - Se filtra por namespace y no por sufijo: `WhatsAppWebhookReader` es un parser, no un lector.
  - Excepciones: la propia clase, `PersistenceRegistration`, `SeedExtensions` y `DatabaseSeeder` (tarea 8), que llama a los tres seeders.
  - Usa el mismo detector que `Only_the_registration_names_the_concrete_unit_of_work`, con `Assert.Contains` de `PersistenceRegistration` como caso de control.
  - Los tests de integración que construyen repositorios concretos quedan fuera a propósito, y el test lo dice.
- **Docs:**
  - `docs/guides/agregar-un-area.md:137,298`: el registro pasa a `PersistenceRegistration.cs`;
  - el árbol de `backend.md` (`:105-115`);
  - la fila "Registro en DI" de `AGENTS.md`.
- **Tests a revisar con Docker:** `DbContextRegistrationTests` y `HealthCheckTests`.

### 7. El caché de ajustes fuera del lector, y `CountActiveAdminsAsync` en SQL (dos commits)
- **Commit A, el caché:**
  - `ISystemSettingsReader.InvalidateAsync` sale del lector;
  - nuevo `App/Interfaces/Integrations/Caching/ISystemSettingsCache` (`InvalidateAsync`), implementado por `Infra/Caching/SystemSettingsCache`, que usa `SystemSettingsReader.CacheKey`. Mejor todavía: la clave se muda al caché y el lector la lee de ahí. Se registra junto a `AddHybridCache`;
  - lo llama `SystemSettingsService.cs:49`, en el mismo lugar que hoy: después del commit y solo si salió bien;
  - **carpeta nueva `Integrations/Caching/`:** se suma a la lista de subcarpetas de `AGENTS.md` ("Dónde va cada cosa") y de `backend.md:81`. `IPermissionService.InvalidateRoleAsync` se queda en `Integrations/Identity`, y queda anotado que es un caché de permisos;
  - se borran las excepciones: `Invalidate` en `Arch/PersistenceNamingTests.cs:22-29,94-105`, `backend.md:218` y el ADR 0008 (`:21`), con una nota fechada;
  - dobles: `SystemSettingsServiceTests:185` pasa al doble nuevo, y se borra `InvalidateAsync` del lector falso de `AuthFakes.cs:304`;
  - `IT/Support/RegistrationModeScope.cs:30` y `SystemSettingsReaderTests:72` pasan a `ISystemSettingsCache`.
- **Commit B, el conteo:** `UserReader.CountActiveAdminsAsync` (`:179-180`) pasa a un `CountAsync` sobre `userManager.Users` sin cargar entidades. Conserva la semántica de `GetUsersInRoleAsync`:
  - el filtro global de borrados;
  - `IsActive`;
  - el rol por `NormalizedName` normalizado con `userManager`, como hace Identity. No es un claim;
  - `backend.md:222` y el ADR 0008 se actualizan.
  - La caracterización ya existe: `UserReaderTests.Admin_count_includes_only_active_and_not_deleted_accounts` (`:146`), con Docker.

### 8. El seed en un límite y en fila entre réplicas (P3 = B)
- **`Infra/Persistence/Seed/DatabaseSeeder`** recibe `IUnitOfWork`, `ApplicationDbContext` y los tres seeders. Corre:
  ```
  ExecuteInTransactionAsync(async ct => {
      await dbContext.AcquireAdvisoryLocksAsync([AdvisoryLockKeys.Seed], ct);
      roles; ajustes; OpenIddict;
      return Result.Success();
  }, CommitPolicy.OnSuccess, ct)
  ```
  - La firma real es `AcquireAdvisoryLocksAsync(this DbContext, IEnumerable<string>, CancellationToken)` (`AdvisoryLockExtensions.cs:13`), con el token.
  - `AdvisoryLockKeys.Seed` es `"seed:database"`, una propiedad o constante documentada como las demás.
  - Los errores de los seeders siguen siendo excepciones: el `UnitOfWork` deshace, limpia el tracker (`UnitOfWork.cs:89-104`) y relanza.
- **Adentro de la transacción, cada seeder guarda así:**
  - `RoleManager` y el `UserManager` del administrador autoguardan sobre el mismo contexto scoped, con un savepoint por guardado, como en la Etapa 1;
  - los managers de OpenIddict corren en la transacción de afuera (verificado para 7.7.1, plan `:549`);
  - `SystemSettingsSeeder` deja de llamar a `SaveChangesAsync` y pierde la dependencia de `ApplicationDbContext`: la guarda el `SaveChangesAsync` final del límite.
- **`SeedExtensions.SeedDatabaseAsync`** resuelve solo `DatabaseSeeder`. Su firma pública no cambia; la usan `ApiFactory` y los tests.
- **`Arch/TransactionBoundaryTests`:**
  - `DatabaseSeeder` es la única excepción con nombre en `Only_use_case_entry_points_receive_…` y `…_run_a_unit_of_work`, con `Assert.Contains` para que no pase en silencio;
  - sale la excepción de todo el namespace `Seed` en `Only_the_unit_of_work_saves_the_context` (`:177`);
  - `"seed:"` entra en `LockKeyPrefixes` (`:66-70`).
- **Tests con Docker:**
  - `IT/Persistence/AdvisoryLockKeysTests` fija `"seed:database"`;
  - `The_seed_waits_for_the_seed_lock`, **determinista**: otra conexión toma `pg_advisory_xact_lock(hashtextextended('seed:database', 0))` en una transacción. El seed no termina mientras el lock siga tomado, y termina cuando se suelta. Es el rojo seguro sin el lock;
  - `Two_seeds_in_parallel_leave_one_row_of_each`: dos `SeedDatabaseAsync` con `Task.WhenAll` sobre una base migrada. Sin el lock el rojo es probabilístico (índice único de roles), por eso va el test anterior;
  - `A_failing_seed_leaves_nothing`: `WithWebHostBuilder` con un `IOpenIddictApplicationManager` que lanza, sobre una base migrada y vacía. No quedan roles ni fila de ajustes. No sirve fallar con opciones inválidas: `ValidateOnStart` corta el arranque antes del seed.
- **Docs:** `SeedExtensions.cs:7-13`, `UnitOfWork.cs:10-12`, `IUnitOfWork.cs:116`, `backend.md:173,185,265`, y una enmienda fechada al ADR 0001: `DatabaseSeeder` es la única excepción con nombre, y el motivo.

### 9. El seed al arrancar en todo ambiente salvo `Testing` (P1 = A, P2 = A)
- **`Infra/Persistence/DatabaseInitialization.cs` (nuevo, público):** `InitializeDatabaseAsync(this IServiceProvider, IHostEnvironment, CancellationToken)`. Encapsula la decisión porque `TestingEnvironment` es `internal`. `Program.cs:48-53` lo llama y deja en el `if` solo `MapOpenApiDocumentation`.
  - **Development:** `ApplyMigrationsAsync` y después el seed, como hoy.
  - **Testing:** nada. El arnés siembra después de `EnsureCreatedAsync`.
  - **Cualquier otro ambiente:** si `GetPendingMigrationsAsync` devuelve algo, lanza `InvalidOperationException` con un mensaje que dice que se corra el bundle. Después, el seed.
  - **Por qué el chequeo explícito (P1):** una base migrada a una versión vieja no hace fallar al seed, que solo toca roles, ajustes y OpenIddict. Con la base caída, el chequeo ya lanza.
- **Diseño y bundle:** el seed y el chequeo no corren en `dotnet ef` ni en el bundle. `HostFactoryResolver` corta el programa en `Build()`, antes de este código.
- **Comentario de `ApiFactory.cs:199`:** "Testing: no aplica migraciones, no siembra al arrancar ni mapea OpenAPI".
- **Test nuevo, `IT/Hosting/ProductionStartupTests`:**
  - migra una base nueva con `new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs).UseOpenIddict<Guid>().Options)`, **antes** de arrancar el host;
  - arranca con `WithWebHostBuilder`, `UseEnvironment("Production")`, la cadena nueva y el `ApplicationDbContext` de producción reemplazando al `TestDbContext` heredado, como `OpenApiTests.cs:233-237`;
  - usa certificados autofirmados en base64, generados en el test, con `X509KeyUsageExtension`: `DigitalSignature` para firmar y `KeyEncipherment` para cifrar. Las fechas salen de `TimeProvider.System.GetUtcNow()`, porque `BannedSymbols.txt` también rige en los tests;
  - comprueba Admin con `Permissions.All`, User, una fila de ajustes, el cliente `web` y el scope `api`;
  - un segundo arranque sobre la misma base no duplica nada;
  - **nuevo, por P1:** `Starting_in_production_against_an_unmigrated_database_fails`, con una base que no existe: el host no arranca y el mensaje nombra el bundle;
  - `finally` con `EnsureDeletedAsync`, como `SystemSettingsSeedTests`.
- **Lo que este test NO prueba:** hereda de `ApiFactory` `Email:Delivery=PickupDirectory`, la clave HMAC, el `Issuer`, el secreto de Google, `Clients:Web`, WhatsApp, Data Protection efímero y el reloj falso. Prueba las ramas por ambiente (certificados, validación del origen público, sin migraciones ni OpenAPI, HSTS, seed y chequeo de migraciones), no la lista de configuración obligatoria. Esa lista sale del código (tarea 10).
- **Sin Docker:** solo compila.

### 10. `docs/guides/despliegue.md` y cierre
- **Contenido de la guía:**
  - el orden bundle → imagen (`deploy.yml:72-84,126-130`);
  - el seed al arrancar: idempotente, con lock y en una transacción. Suma los permisos nuevos a Admin, realinea el cliente `web` con `Authentication:Clients:Web` (borra lo que se haya cargado a mano) y le da el rol Admin a `Seed:AdminEmail` en cada arranque (ver la pregunta de abajo);
  - qué pasa con la base sin migrar o caída (P1): el proceso no arranca, y el orquestador lo reinicia hasta que la base responde;
  - los certificados (enlace a `azure-setup.md` §6);
  - **urgente:** copiar el `dist/` del front a `wwwroot/` (desde `README.md:186`);
  - Data Protection: las claves se guardan en Postgres (`IdentityRegistration.cs:85-87`), sin `ProtectKeysWith`, así que quedan sin cifrar en la base;
  - la configuración obligatoria en `Production`, sacada del código:
    - `ConnectionStrings:appdb` (`DependencyInjection.cs:107-110`);
    - `Authentication:LoginCode:HashKey`, de al menos 32 bytes en base64;
    - `Authentication:Certificates:{Signing,Encryption}:{Base64|Path}` y `:Password`, que se leen al registrar;
    - `Authentication:Issuer` (`EmailRegistration.cs:32-36`, y `WhatsAppRegistration.cs:126` con el webhook);
    - `Authentication:Clients:Web:RedirectUris` y `PostLogoutRedirectUris`, al menos una cada una;
    - `Authentication:Google:ClientSecret`, porque `appsettings.json` trae el `ClientId` (`IdentityRegistration.cs:124-128`);
    - `Email:Smtp:UserName`, `FromAddress` y `Password`, porque `Delivery` es `Smtp` por defecto;
    - `Seed:AdminEmail`, opcional pero necesario con `InviteOnly`;
    - `ForwardedHeaders:*` detrás de un proxy;
    - las claves de `WhatsApp:*`, solo si se prende.
- **Otros documentos:**
  - `README.md:186-187`;
  - `azure-setup.md:238-250`: la lista de "Pendientes" suma `Authentication:Issuer` y `Authentication:Clients:Web:*`, que faltan y ahora además alimentan el seed;
  - el ADR 0006 ("implementado", con fecha y commits) y el ADR 0001 (ya enmendado en la 8);
  - `AGENTS.md` enlaza la guía en "Más documentación". **`CLAUDE.md` no se toca**: es lo propio de Claude Code e importa `AGENTS.md`;
  - el estado de la Etapa 7 en el plan maestro.

**Orden:** diseño → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10.
- La 4 va antes de la 5, porque la 5 toca los tests ya renombrados.
- La 6 va antes de la 8, porque `DatabaseSeeder` se registra ahí.
- La 8 va antes de la 9.

**Puerta, tests de integración a revisar primero:**
- `Hosting/ProductionStartupTests`, `Identity/SeedTests`, `Settings/SystemSettingsSeedTests` y `Persistence/{MigrationsTests,AdvisoryLockKeysTests}`, más los tres tests nuevos del seed;
- `Auth/InitialAdminSignInTests` y `OpenApiTests` (arranque en Development);
- `Users/MeProfileEndpointTests`, `Auth/ExternalLoginTests`, `ConnectFlowTests`, `LoginSecurityTests` y `Contracts/MvcConnectPassthroughTests`;
- `WhatsApp/*` (renombres y privacidad de logs), `Emails/*` y `Users/UserInvitationEndpointsTests`;
- `Settings/*`, `Support/RegistrationModeScope` y `Persistence/UserReaderTests`;
- `DbContextRegistrationTests`, `HealthCheckTests`, `Persistence/UserRepositoryTransactionTests` e `IdentityModelTests`.

---

## Decisiones del usuario (2026-09-28)

1. **P1 = A.** Arrancar en producción falla si la base no está migrada o no responde. El seed realinea el cliente `web` con la configuración y le suma a Admin los permisos nuevos. Lo implementan la tarea 9 (chequeo explícito de migraciones pendientes más el seed) y la guía de la 10.
2. **P2 = A.** El arnés evita el seed al arrancar excluyendo el ambiente `Testing`. No es visible. Va en `InitializeDatabaseAsync`, dentro de Infrastructure, porque la constante es interna.
3. **P3 = B.** `DatabaseSeeder` es la única excepción con nombre en `TransactionBoundaryTests`, con una enmienda al ADR 0001 (tarea 8).
4. **P4.** Los nombres de Google y de WhatsApp se recortan en la entrada sin partir pares sustitutos (tarea 3). También se limpia el `\0`, con el mismo helper que ya usa WhatsApp.
5. **P5 = B.** Una invitación por correo con la cola llena queda `MarkSendFailed` con un log, y el reenvío no da 429 (tarea 5).

## Decisión 6 del usuario (2026-09-28): la cuenta de `Seed:AdminEmail`

`Seed:AdminEmail` es la cuenta del dueño de la plataforma (en desarrollo, la del usuario). El seed **nunca crea cuentas**, ni de empresas ni de nadie: solo garantiza que esa cuenta, si existe, tenga el rol Admin, y se lo devuelve en cada arranque si alguien se lo sacó. Los administradores de las empresas los agrega el dueño desde el panel, y el seed no los toca. Es lo que hace hoy `RoleSeeder.EnsureAdminRoleAsync`; no cambia código. La guía de despliegue (tarea 10) lo escribe así.

## Hallazgos aceptados
- `OpenIddictRegistration.TestingEnvironment` es `internal`: la decisión por ambiente pasa a `InitializeDatabaseAsync` en Infrastructure.
- P1 pide fallar con la base sin migrar, y una base vieja no hace fallar al seed: se suma el chequeo explícito de `GetPendingMigrationsAsync` y su test.
- `ProductionStartupTests` hereda la configuración de `ApiFactory`: no prueba la lista de configuración obligatoria, que sale del código hacia la guía.
- Los certificados del test usan `TimeProvider.System`, porque `BannedSymbols` también rige en los tests.
- El recorte seguro ya existe en `WhatsAppText`: se extrae a `Domain/Common/StorableText` para no atar `Users` a WhatsApp.
- Google puede mandar `\0`: `FitExternalDisplayName` limpia además de recortar.
- `IdentityModelTests.cs:55` usa inicializador de objeto: el constructor público de `ApplicationUser` se queda.
- `ApplicationUser.Restore()` ya existe: `Restore(displayName)` lo reemplaza.
- `Error.None` rompería `ErrorDeclarationTests`: se excluye `Error` por tipo.
- `ValidationError` se construye fuera de Domain (tres lugares): la regla del IL cubre solo las fábricas, y se documenta.
- `ThrowingEmailQueue` es asíncrono y consulta la base: pasa a consultas sincrónicas.
- `WhatsAppOutboxTests:33` usa `EmailQueue.Capacity`, que se borra: compara con `EmailOptions`.
- Falta la fila `Email:QueueCapacity` en `README.md:237`.
- `UserInvitationIssuer` con logger llega justo al tope de 8 dependencias.
- El lector falso de `AuthFakes.cs:304` también implementa `InvalidateAsync` y cambia.
- `Integrations/Caching/` es carpeta nueva: se suma a `AGENTS.md` y a `backend.md:81`.
- `DatabaseConnectionName` se queda en `DependencyInjection`, porque los tests lo usan.
- La receta `agregar-un-area.md:137,298` nombra el registro en `DependencyInjection.cs`: se actualiza.
- La regla de "nadie nombra la clase concreta" filtra por namespace (`WhatsAppWebhookReader`) y solo lee `src` (los tests construyen repositorios concretos).
- `AcquireAdvisoryLocksAsync` pide el `CancellationToken`: se corrige la firma en el diseño.
- `SystemSettingsSeeder` pierde la dependencia de `ApplicationDbContext`.
- El test de dos seeds en paralelo tiene un rojo probabilístico: se suma uno determinista con el lock tomado desde otra conexión.
- `A_failing_seed_leaves_nothing` no puede fallar con opciones inválidas, por `ValidateOnStart`: falla con un manager de OpenIddict que lanza.
- `CountActiveAdminsAsync` ya tiene su test de semántica (`UserReaderTests:146`): se nombra ese en lugar de uno de `UserGuard`.
- La tarea 4 original mezclaba un renombre mecánico con un cambio de comportamiento: se parte en 4 y 5 y quedan 10 tareas, el tope de la forma de trabajo.
- La tarea 6 original mezclaba el caché y el conteo: quedan como dos commits de la 7.
- `azure-setup.md` no lista `Authentication:Issuer` ni `Clients:Web`, que ahora además alimentan el seed.
- `CLAUDE.md` no enlaza la guía: basta `AGENTS.md`.

## Descartados
- `OpenApiTests.DevelopmentApi` no se rompe: `Development` ya migra y siembra sobre su propia base vacía, y sigue igual.
- `MigrationsTests`, `SystemSettingsSeedTests` e `InitialAdminSignInTests` no se rompen: quedan en `Testing` y siembran a mano.
- EF materializa `private set` por el backing field. No hay configuración de acceso que lo impida, y el modelo no cambia.
- `RoleManager` y el `UserManager` del seed adentro de la transacción autoguardan con savepoints, como `UserRepository` desde la Etapa 1.
- `ExecuteInTransactionAsync` exige un `Result`: el seed devuelve `Result.Success()` y los errores siguen siendo excepciones.
- El caché de permisos después de que el seed suma permisos no es un problema: `HybridCache` es local al proceso y arranca vacío.
- El seed no corre dentro de `dotnet ef` ni del bundle: `HostFactoryResolver` corta en `Build()`.
- El orden de registros no importa salvo los interceptores, que se conservan. No hay pooling, y Data Protection se queda en Identity.
- Renombrar la categoría del log de WhatsApp no afecta ningún test de privacidad: ninguno filtra por ella.
- `CountActiveAdminsAsync` en SQL no cambia la semántica: filtro de borrados, `IsActive` y rol por nombre normalizado, no por claim.
- `SentAtUtc` de los códigos por correo no lo lee nadie más que el tope de WhatsApp: marcarlo solo si entró es invisible.