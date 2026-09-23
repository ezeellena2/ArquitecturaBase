# Ingreso con WhatsApp — Plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDA: usar superpowers:subagent-driven-development (recomendado) o superpowers:executing-plans para ejecutar este plan tarea por tarea. Los pasos usan checkboxes (`- [ ]`).

**Objetivo:** que una persona pueda crear su cuenta y entrar a la web usando solo su WhatsApp, sin que un mensaje de WhatsApp alcance por sí solo para abrir una sesión. Terminado cuando se cumplen, **en local y con el número de prueba**, los cinco puntos de la sección 1 del spec.

**Diseño (fuente de verdad):** `docs/specs/2026-09-22-ingreso-whatsapp-design.md`, aprobado el 2026-09-22. Este plan **no repite** las reglas del spec: las cita por sección. Si una tarea y el spec se contradicen, gana el spec y se corrige el plan.

**Pantallas y mensajes:** los tableros de la sección "Ingreso con WhatsApp" del Artifact [Sistema visual — ArquitecturaBase](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ). Cada tarea de pantalla nombra su tablero y enumera lo que tiene que quedar igual. **Antes de cerrar una tarea de pantalla hay que abrir el tablero y comparar**, no solo tachar los pasos.

**Tecnología nueva:** `libphonenumber-csharp` 9.0.39 (números), `Aspire.Hosting.DevTunnels` 13.5.4 (túnel) y la Graph API de WhatsApp v25.0 a través de un `HttpClient`. Nada más.

**Stack:** el que ya está. Backend .NET 10 + Aspire 13.5 + PostgreSQL + Identity + OpenIddict. Front Vite + React 19 + TypeScript + Tailwind 4 + shadcn/ui.

---

## Cómo está organizado

Los hitos coinciden con los tres niveles de prueba en local de la sección 16 del spec. **Cada hito termina con una prueba manual que hace el usuario**, y el siguiente no arranca sin ella.

| Hito | Tareas | Qué queda funcionando | Nivel de prueba |
|---|---|---|---|
| 1 | 1 a 7 | el código por WhatsApp desde `/login` | 1: sin túnel y sin publicar |
| 2 | 8 y 9 | Meta verifica el webhook y los eventos se guardan | 2: con túnel, sin publicar |
| 3 | 10 a 12 | el chat: entrar con el enlace y crear la cuenta | 3: con túnel y la app publicada |
| 4 | 13 a 16 | el perfil y la administración: vincular, agregar correo, invitar | 1 y 3 |
| Cierre | 17 y 18 | la retención de mensajes y la documentación | — |

## Lo que hace el usuario en Meta (el agente guía, no toca)

Cada paso se hace cuando lo pide su hito, no antes. El agente da el enlace exacto y explica cada campo. **Los secretos los escribe el usuario en su terminal con `dotnet user-secrets`: nunca pasan por el chat ni por un archivo del repo.**

| Para | Qué | Dónde |
|---|---|---|
| Hito 1 | Crear un usuario del sistema, asignarle la app y la cuenta de WhatsApp, y generar un token con `whatsapp_business_messaging` y `whatsapp_business_management`. Cargarlo con `dotnet user-secrets set "WhatsApp:AccessToken" "<token>"` en `src/ArquitecturaBase.Api` | [Configuración del negocio](https://business.facebook.com/latest/settings) › Usuarios del sistema |
| Hito 1 | Crear la plantilla de autenticación (tabla de abajo) y esperar que la aprueben | [Administrador de WhatsApp](https://business.facebook.com/wa/manage/home/) › Plantillas |
| Hito 2 | Copiar el secreto de la app y cargarlo en `WhatsApp:AppSecret` | [Configuración › Básica](https://developers.facebook.com/apps/4601782356805744/settings/basic/) |
| Hito 2 | Inventar una palabra de verificación larga y al azar, y cargarla en `WhatsApp:VerifyToken` | — |
| Hito 2 | Instalar la CLI `devtunnel` (`winget install Microsoft.devtunnel`) y hacer `devtunnel user login` una sola vez | terminal |
| Hito 2 | Cargar la URL del túnel y la palabra, tocar "Verificar y guardar" y suscribir el campo `messages` | [Paso 2. Configuración de producción](https://developers.facebook.com/apps/4601782356805744/use_cases/customize/wa-configurations-v2/?use_case_enum=WHATSAPP_BUSINESS_MESSAGING) › Configurar webhooks |
| Hito 3 | Publicar la política de privacidad en una página pública y cargar su URL; completar el ícono, la categoría y el propósito; publicar la app | Configuración › Básica, y el aviso "Publica tu aplicación" del Paso 2 |
| Hito 4 | Crear la plantilla de invitación (tabla de abajo) y esperar que la aprueben | Administrador de WhatsApp › Plantillas |

**Las plantillas** (una por idioma, con los códigos `es` y `en`, que el panel muestra como "Spanish" y "English"; coinciden con la cultura del perfil):

| Nombre | Categoría | Contenido |
|---|---|---|
| `codigo_ingreso` | Autenticación | el texto fijo de Meta, con el aviso de seguridad, "Este código caduca en 10 minutos" y el botón "Copiar código" |
| `invitacion_acceso` | Utilidad | "Hola, {{1}}. Te dieron acceso a {{2}}. Tocá «Quiero entrar» y te mandamos el enlace para ingresar." con un botón de respuesta rápida "Quiero entrar". `{{1}}` es el nombre y `{{2}}` el nombre del sistema (`Email:AppName`). En inglés, el mismo mensaje traducido |

**`codigo_ingreso` quedó creada el 2026-09-23** en la cuenta de prueba (`1658125822339116`), en `es` y `en`, con "Copiar código", el aviso de seguridad, el vencimiento y el período de validez del mensaje en 10 minutos (el mismo tiempo que dura el código). La cuenta de prueba no necesita medio de pago para mandar plantillas.

## Reglas para quien ejecute

Las mismas de las fases anteriores, más las de WhatsApp.

- **Dos repos.** Backend en `C:\Users\ezequ\source\repos\ArquitecturaBase`, front en `C:\Users\ezequ\source\repos\ArquitecturaBaseFront`. Cada tarea dice cuál toca.
- **Rama:** todo va directo a `main`, en los dos repos. No crear ramas. **No hacer push.**
- **Commits:** uno por tarea (dos si la tarea toca los dos repos), en español, con conventional commits. Cierran con la línea de atribución que indique la sesión que ejecuta (hoy, `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`). Si el mensaje lleva tildes, se escribe con heredoc, no con `-m` en Git Bash.
- **Verificación antes de cada commit:**
  - backend: `dotnet build ArquitecturaBase.slnx` con **0 advertencias** y `dotnet test` en verde;
  - front: `npm run build`, `npm run lint` y `npm run test`, los tres limpios.
  - Pegar la salida real, no decir que pasó.
- **Docker** encendido para los tests de integración.
- **AppHost:** apagarlo siempre al terminar de probar, con `aspire stop` (desde PowerShell, `C:\Users\ezequ\.dotnet\tools\aspire.cmd`). Si queda corriendo, Visual Studio falla con `address already in use` y el túnel sigue expuesto.
- **Puede haber otra sesión trabajando.** Nunca `git add .` ni `git add -A`: agregar solo los archivos propios.
- **TDD** donde hay lógica: el test primero, se verifica que falla por la razón correcta, después el código.
- **Idioma:** identificadores, logs y mensajes de excepción en inglés. Todo texto que ve el usuario sale de resources (backend) o de i18next (front), en español rioplatense con voseo y en inglés, siempre los dos.
- **Secretos:** nunca pedirle al usuario un token, un secreto ni un código en el chat. Nunca escribirlos en un archivo del repo ni en un log.
- **Números en los logs:** siempre enmascarados (`+54 9 11 •••• 6789`). Los códigos, los enlaces y los tokens no se registran nunca.
- **Datos viejos:** no se migran (sección 6.1 del spec). La Tarea 2 borra la base de desarrollo, y desde ahí las migraciones solo cambian el esquema.
- **Desvíos:** si algo no compila o una API cambió, hacer el cambio mínimo e informarlo. Si el cambio altera el diseño, frenar y preguntar.

## Hechos verificados del código actual

Comprobados el 2026-09-22 leyendo los repos y la cuenta de Meta. No hace falta volver a verificarlos, pero sí leer cada archivo antes de tocarlo.

**Backend:**

- `LoginCode` está atado al correo: `Email`, lock con `pg_advisory_xact_lock` por correo (`LoginCodeRepository.LockEmailAsync`), hash HMAC de `email:código` con `Authentication:LoginCode:HashKey`, índice `(Email, CreatedAtUtc)`.
- `RequestLoginCodeCommandHandler`:
  - invalida los códigos activos del mismo correo;
  - en `InviteOnly` emite y guarda la fila de un correo sin cuenta, pero no manda el correo;
  - encola el correo antes de guardar.
- `VerifyLoginCodeCommandHandler` crea la cuenta con cualquier código válido **sin mirar el modo de registro** (línea 63). `SignInWithExternalProviderCommandHandler` sí lo mira (línea 51).
- `ApplicationUser`:
  - el `UserName` es el correo y `RequireUniqueEmail = true`;
  - el índice único sobre `NormalizedEmail` está en `ApplicationUserConfiguration`;
  - `PhoneNumber` y `PhoneNumberConfirmed` existen pero no los usa nadie.
- `LoginAudit.Email` es obligatorio (`Email.MaxLength`) y `Method` se guarda como texto (`HasConversion<string>`): sumar valores al enum no rompe nada.
- `OpenIdPrincipalFactory` arma `sub`, `email`, `name` (`DisplayName ?? Email`) y `role`.
- `EmailQueue` es un `Channel` acotado a 100 con `TryWrite`, y `EmailBackgroundService` reintenta 3 veces con espera. Es el patrón para la cola de WhatsApp.
- `ServiceDefaults` le pone `AddStandardResilienceHandler()` **a todos** los `HttpClient` (`Extensions.cs`, línea 31).
- `BackendPrefixes` (`Api/Hosting/SpaExtensions.cs`, línea 15) no tiene `/webhooks`.
- Las políticas de límites son `LoginCodePolicy` y `LoginVerifyPolicy`, por IP (`Api/RateLimiting`).
- El AppHost levanta Postgres con el volumen persistente `arquitecturabase-pgdata`, la Api y el front en `https://localhost:5173`.
- Tests:
  - `ApiFactory` con `CapturingEmailSender` (`factory.EmailSender`);
  - `Application.UnitTests/Features/Auth/RequestLoginCodeCommandHandlerTests.cs`;
  - en `Api.IntegrationTests/Auth/`: `LoginCodeEndpointsTests`, `RegistrationModeTests`, `LoginSecurityTests`, `ConnectFlowTests`;
  - `Hosting/SpaHostingTests.cs` y `Persistence/MigrationsTests.cs`.

**Front:**

- `LoginPage`: Google, el separador "o" y el formulario de correo. Llama a `requestLoginCode` y navega a `/login/codigo` con `{ email, resendAfterSeconds }` en el estado de la ruta. No tiene test propio: lo cubre `loginReturnUrl.test.tsx`.
- `LoginCodePage` exige `state.email` (si falta, vuelve a `/login`), usa `OtpInput` de 6 casillas y reenvía con `requestLoginCode(email)`.
- `routes.tsx`: `/login`, `/login/codigo` y `/auth/callback` cuelgan de `AuthLayout` y se cargan sin `lazy`, a propósito.
- `ProfilePage` es un solo formulario, con el correo como identidad de solo lectura.
- Usuarios: `columns.tsx`, `UserFormDialog.tsx`, `UserRolesDialog.tsx`, `UsersFilterBar.tsx` y `errors.ts`.
- El proxy de Vite (`vite.config.ts`) reenvía `/api`, `/account`, `/connect`, `/signin-google` y `/.well-known`.

**Meta** (verificado con capturas y con la herramienta de Meta en modo lectura):

- App `4601782356805744` ("Servicios Ya"), **en desarrollo**, sin webhook y sin política de privacidad.
- La verificación de empresa está aprobada.
- Número de prueba +1 555 163-2662, `phone_number_id` `1340198875839831`, cuenta de WhatsApp `1658125822339116`.
- Hay dos destinatarios de prueba, que el panel muestra **sin el 9**: +54 341 602 0069 y +54 341 365 4813.
- Un webhook real trae `wa_id: "5493413654813"` (**con el 9**) y `user_id: "AR.1102953142229032"` (el BSUID).
- Mientras la app no esté publicada, **solo llegan los webhooks de prueba del panel**, ni siquiera los de los administradores.

## Contratos

Los endpoints de la sección 18 del spec. Todos los errores son ProblemDetails, como siempre.

**`GET /account/login-methods`** (anónimo)
```json
{ "google": true, "whatsapp": true, "whatsappCountries": ["AR"], "whatsappNumber": "15551632662" }
```
`whatsappNumber` es el del bot, para el enlace "Volver a WhatsApp" (`https://wa.me/<número>`).

**`POST /account/login-code/whatsapp`** (anónimo, `LoginCodePolicy`)
```json
{ "country": "AR", "number": "11 2345-6789" }
```
→ `202 { "resendAfterSeconds": 60, "phone": "+5491123456789", "maskedPhone": "+54 9 11 •••• 6789" }`, siempre igual exista o no la cuenta.
Errores: `400 Users.Phone.Invalid`, `400 Auth.WhatsApp.CountryNotSupported` y `429` como el pedido por correo.

**`POST /account/login-code/verify`** (anónimo, `LoginVerifyPolicy`)
```json
{ "phone": "+5491123456789", "code": "482913", "returnUrl": "/connect/authorize?..." }
```
Lleva `email` **o** `phone`, exactamente uno. Responde `200 { "returnUrl": "..." }` como hoy.

**`POST /account/login-link/preview`** y **`POST /account/login-link/redeem`** (anónimos, `LoginVerifyPolicy`)
```json
{ "token": "<base64url>" }
```
- preview → `200 { "displayName": "Ana Pérez", "maskedPhone": "+54 9 11 •••• 6789" }`
- redeem → `204` y crea la cookie
- los dos: `400 Auth.LoginLink.Invalid`; redeem además `403 Auth.Account.Disabled` y `429 Auth.Account.LockedOut`.

**`GET /webhooks/whatsapp`** → `200` con `hub.challenge` en texto plano, o `403`. **`POST /webhooks/whatsapp`** → `200` vacío, `401` si la firma no coincide, `413` si el cuerpo supera 5 MB.

**`/api/me`** suma `email` (ahora opcional), `emailConfirmed`, `phoneNumber`, `phoneNumberConfirmed` y `hasGoogleLogin`.

| Método | Ruta | Cuerpo | Respuesta |
|---|---|---|---|
| POST | `/api/me/whatsapp/code` | `{ "country", "number" }` | `202 { "resendAfterSeconds", "phone", "maskedPhone" }` |
| PUT | `/api/me/whatsapp` | `{ "phone", "code" }` | `204`, o `409 Users.Phone.AlreadyExists` después de un código correcto |
| DELETE | `/api/me/whatsapp` | — | `204`, o `409 Users.User.LastLoginMethod` |
| POST | `/api/me/email/code` | `{ "email" }` | `202 { "resendAfterSeconds" }` |
| PUT | `/api/me/email` | `{ "email", "code" }` | `204`, o `409 Users.User.AlreadyExists` después de un código correcto |

**Administración** (`users.manage`):

`POST /api/users`:
```json
{
  "email": null,
  "phone": { "country": "AR", "number": "351 555-1234" },
  "displayName": "Laura Ríos",
  "roles": ["User"],
  "invitation": { "channel": "WhatsApp", "consent": true }
}
```
- Pide al menos uno de `email` y `phone`.
- Errores: `400 Users.Identity.Required`, `Users.Phone.Invalid`, `Users.Invitation.ConsentRequired` y `Users.Invitation.NameRequired`; `409 Users.Phone.AlreadyExists` y `Users.User.AlreadyExists`.

Además:
- `PUT /api/users/{id}` suma `email` y `phone` opcionales; lo que cambia queda sin verificar.
- `DELETE /api/users/{id}/whatsapp` responde `204` y cierra las sesiones.
- `POST /api/users/{id}/invitation` con `{ "channel", "consent" }` responde `202`.
- `GET /api/users`: `email` pasa a ser opcional, se suman `phoneNumber` y `phoneNumberConfirmed`, y la búsqueda también encuentra por número.

## Estructura de archivos

**Backend, nuevos:**

```
src/ArquitecturaBase.Domain/
  ValueObjects/PhoneNumber.cs
  Authentication/LoginLink.cs, LoginLinkErrors.cs, ILoginLinkRepository.cs
  Authentication/LoginCodeChannel.cs, LoginCodePurpose.cs
  WhatsApp/WhatsAppContact.cs, WhatsAppMessage.cs (+ enums), IWhatsAppContactRepository.cs, IWhatsAppMessageRepository.cs
  Users/UserInvitation.cs, IUserInvitationRepository.cs
src/ArquitecturaBase.Application/
  Abstractions/Phones/IPhoneNumberParser.cs
  Abstractions/WhatsApp/IWhatsAppOutbox.cs, WhatsAppOutboundMessage.cs, IWhatsAppWebhookReader.cs, WhatsAppWebhookBatch.cs
  Abstractions/Security/ISecureTokenGenerator.cs
  Features/Auth/RequestWhatsAppLoginCode/, GetLoginMethods/, PreviewLoginLink/, RedeemLoginLink/, LoginLinkIssuer.cs
  Features/WhatsApp/ReceiveWebhook/, HandleInboundMessage/ (+ BotReply.cs)
  Features/Users/RequestPhoneLinkCode/, ConfirmPhoneLink/, UnlinkOwnPhone/, RequestEmailCode/, ConfirmEmail/, UnlinkUserPhone/, SendInvitation/
  Resources/Bot.resx, Bot.en.resx
src/ArquitecturaBase.Infrastructure/
  Phones/LibPhoneNumberParser.cs
  WhatsApp/WhatsAppOptions.cs, WhatsAppCloudClient.cs, WhatsAppOutbox.cs, WhatsAppSenderBackgroundService.cs,
          WhatsAppWebhookReader.cs, WhatsAppSignatureValidator.cs, WhatsAppInboundProcessor.cs,
          WhatsAppMessageRetentionService.cs, WhatsAppRegistration.cs
  Security/SecureTokenGenerator.cs
  Persistence/Configurations/LoginLinkConfiguration.cs, WhatsAppContactConfiguration.cs,
          WhatsAppMessageConfiguration.cs, UserInvitationConfiguration.cs
  Persistence/Repositories/LoginLinkRepository.cs, WhatsAppContactRepository.cs,
          WhatsAppMessageRepository.cs, UserInvitationRepository.cs
  Emails/Templates/Invitation.html
src/ArquitecturaBase.Api/
  Endpoints/Webhooks/WhatsAppWebhookEndpoints.cs
  Endpoints/Account/LoginLinkEndpoints.cs, LoginMethodsEndpoint.cs
tests/ (en el proyecto de cada capa)
  Domain.UnitTests: PhoneNumberTests, LoginLinkTests, LoginCodeTests (se amplía)
  Application.UnitTests: HandleInboundMessageTests (la tabla del bot), UserGuardsTests (se amplía)
  Api.IntegrationTests: Phones/LibPhoneNumberParserTests, WhatsApp/*, Auth/WhatsAppLoginCodeTests,
          Auth/LoginLinkTests, Users/*; Support/CapturingWhatsAppOutbox.cs
```

**Backend, se modifican:** `ApplicationUser`, `ApplicationUserConfiguration`, `IdentityRegistration`, `IdentityService`, `IIdentityService`, `UserAccount`, `LoginCode` y su configuración, repositorio y hasher, `LoginAudit` y su configuración, `LoginMethod`, `RequestLoginCode*`, `VerifyLoginCode*`, `OpenIdPrincipalFactory`, `GetCurrentUser*`, `GetUsers*`, `GetUser*`, `CreateUser*`, `UpdateUser*`, `UserGuards`, `MeEndpoint`, `UsersEndpoints`, `LoginCodeEndpoints`, `SpaExtensions`, `RateLimitingExtensions`, `Errors.resx` y `Errors.en.resx`, `Emails.resx` y `Emails.en.resx`, `Directory.Packages.props`, `AppHost.cs`, `ApiFactory`, `SpaHostingTests`, `README.md` y `CLAUDE.md`.

**Front, nuevos:** `features/auth/pages/LoginLinkPage.tsx`, `features/auth/components/PhoneField.tsx`, `features/profile/components/LoginMethodsCard.tsx`, `LinkWhatsAppDialog.tsx`, `AddEmailDialog.tsx` y `UnlinkWhatsAppDialog.tsx`, `features/users/components/UnlinkUserWhatsAppDialog.tsx`, más sus tests.

**Front, se modifican:** `features/auth/api/loginCode.ts`, `LoginPage.tsx`, `LoginCodePage.tsx`, `app/routes.tsx`, `features/profile/pages/ProfilePage.tsx`, `features/home/pages/DashboardPage.tsx`, `layouts/components/UserMenu.tsx` y `Sidebar.tsx`, `shared/api/profile.ts`, `features/users/*`, `locales/{es,en}/{auth,profile,users,common}.json`, `vite.config.ts` y `CLAUDE.md`.

---

## Tareas

### Hito 1: el código por WhatsApp desde `/login`

### Tarea 1: `PhoneNumber` y el parser

**Repo:** backend. **Depende de:** nada. **Spec:** 6.2.

- [x] Agregar `libphonenumber-csharp` 9.0.39 a `Directory.Packages.props` y a `ArquitecturaBase.Infrastructure.csproj`.
- [x] Test primero, en `Domain.UnitTests/ValueObjects/PhoneNumberTests.cs`: `PhoneNumber.Create` acepta `+` y de 8 a 15 dígitos, y rechaza todo lo demás con `Users.Phone.Invalid` (sumarlo a `UserErrors`).
- [x] Implementar `PhoneNumber` (value object, solo BCL).
- [x] `IPhoneNumberParser` en `Application/Abstractions/Phones` con cuatro métodos: `Parse(country, number)`, `FromWhatsAppId(waId)`, `Mask(phone)` y `FormatInternational(phone)`.
- [x] Tests primero, en `Api.IntegrationTests/Phones/LibPhoneNumberParserTests.cs` (son de unidad, sin base). Estos casos, todos con país `AR` salvo el último:

  | Entrada | Resultado |
  |---|---|
  | `11 2345-6789` | `+5491123456789` |
  | `011 15 2345-6789` | `+5491123456789` |
  | `+54 9 11 2345 6789` | `+5491123456789` |
  | `3416020069` | `+5493416020069` |
  | `341 15 602 0069` | `+5493416020069` |
  | `FromWhatsAppId("5493413654813")` | `+5493413654813` |
  | `099 123 456` con país `UY` | `+59899123456` |
  | `123` | error |
  | `Mask(+5491123456789)` | `+54 9 11 •••• 6789` |

- [x] Implementar `LibPhoneNumberParser`. Si un número argentino no es un celular válido sin el 9 y con el 9 sí lo es, se le agrega (spec 6.2). Registrarlo en la DI de Infrastructure.

**Aceptación:** los casos de arriba en verde, build con 0 advertencias y `ArchitectureTests` en verde. Domain sigue sin paquetes y Application no referencia `libphonenumber`.

**Hecha el 2026-09-22** (`a8705c3`). Suite completa 578/578, build con 0 advertencias. La revisión adversarial confirmó 4 hallazgos de 25 y los cuatro quedaron corregidos. Lo que se hizo distinto del plan:

- **Los números con letras se rechazan.** libphonenumber lee las letras como el dígito de su tecla, así que una O tipeada en lugar de un cero daba el celular válido de otra persona. Por lo mismo, un "ext 12" al final ya no se acepta.
- **El país se pasa a mayúsculas antes de interpretar el número**, porque la librería rechaza "ar".
- **`Mask` nunca deja un número a la vista entero.** Con un código de país que la librería no conoce devuelve "•••• 5678", y si el código de área más los últimos 4 dígitos cubren todo el número, tapa también el código de área. `FormatInternational` devuelve el valor guardado si la librería no conoce el número.
- **Tests que se sumaron a la tabla:** la regla del 9 es solo para Argentina (un fijo de Brasil no se convierte en celular argentino), un número argentino válido que no es celular (0800, 0810) sigue rechazado, y un fijo de Uruguay también.
- **Para tener en cuenta más adelante:** un fijo argentino con el 9 adelante pasa a ser celular ("0341 424-0000" queda +5493414240000), como pide el spec. El formato viejo de México con el 1 (+521…) la librería no lo toma como válido: no afecta mientras solo se admita Argentina.

**Commit:** `feat: número de WhatsApp en formato internacional, con el 9 de los celulares argentinos`

### Tarea 2: La cuenta con correo opcional y número único

**Repo:** backend. **Depende de:** 1. **Spec:** 6.1.

- [x] **Borrar la base de desarrollo** `appdb`, sin tocar el contenedor ni su volumen. La Api la recrea al arrancar, con el seed. Avisarle al usuario antes.
- [x] Tests primero (integración):
  - se puede crear una cuenta solo con número;
  - dos cuentas no pueden tener el mismo número;
  - el token de una cuenta sin correo no lleva `email` y su `name` es el número;
  - `/api/me` devuelve el número;
  - el listado muestra el número cuando no hay correo;
  - la búsqueda encuentra por número (con o sin espacios).
- [x] `IdentityRegistration`: `RequireUniqueEmail = false`.
- [x] `ApplicationUserConfiguration`: `PhoneNumber` hasta 16 caracteres, con índice único.
- [x] `IIdentityService` e `IdentityService`:
  - `CreateAsync(Email? email, PhoneNumber? phone, bool phoneConfirmed, string? displayName, string culture)`, que exige al menos uno y pone `UserName = Id`;
  - `FindByPhoneAsync`, `IsDeletedPhoneAsync`, `SetPhoneAsync(userId, phone, confirmed)`, `RemovePhoneAsync` y `SetEmailAsync(userId, email, confirmed)`.
- [x] `UserAccount`: `Email` pasa a ser opcional y se suman `PhoneNumber`, `PhoneNumberConfirmed` y `EmailConfirmed`. Corregir todos los usos hasta que compile.
- [x] `OpenIdPrincipalFactory`: `email` solo si hay correo, y `name` pasa a ser `DisplayName ?? Email ?? número`.
- [x] `GetCurrentUser`, `GetUsers` y `GetUser` suman los campos del contrato. La búsqueda: si el texto tiene 4 dígitos o más, también compara contra `PhoneNumber` solo con sus dígitos.
- [x] Migración `AccountsWithPhone`, que cambia solo el esquema.

**Aceptación:** los tests nuevos y todos los anteriores en verde, incluido `MigrationsTests`. El seed crea al admin como antes.

**Hecha el 2026-09-23** (`8014891`). La base `appdb` se borró antes de empezar. Suite completa 602/602 y build con 0 advertencias. La revisión adversarial (dos vueltas, 25 hallazgos) confirmó uno: dos tests de búsqueda que ya existían quedaban intermitentes con la búsqueda por número. Lo que se hizo distinto del plan, o además:

- **La búsqueda por número solo se activa si el texto parece un número**: dígitos, espacios, "+", guiones, puntos y paréntesis, con 4 dígitos o más. Con letras o una arroba es un nombre o un correo, y sus dígitos ("juan2024@…") traerían a quien los tiene en el número por casualidad. Así los tests intermitentes quedaron como estaban.
- **`SetPhoneAsync`, `RemovePhoneAsync` y `SetEmailAsync` solo escriben el dato** y no renuevan el security stamp: si lo renovaran, a quien vincula su propio número desde el perfil se le invalidaría la cookie. Cortar las sesiones lo decide quien llama (el desvinculado por un admin, Tarea 15, llama a `RevokeSessionsAsync`).
- **`HasExternalLoginAsync(userId, provider)` y `ExternalLoginProviders.Google`** en Application, para el `hasGoogleLogin` de `/api/me`. Un test con el ingreso real de Google verifica que el nombre coincide con el que guarda Identity.
- **`/connect/userinfo`:** `email_verified` sale de `EmailConfirmed` en lugar de un `true` fijo, y el `name` sale de la misma regla que los tokens (`OpenIdPrincipalFactory.NameOf`).
- **La auditoría de Google** usa el correo de la cuenta y, si no tiene, el que mandó Google, porque `LoginAudit.Email` sigue siendo obligatorio hasta la Tarea 3.
- **Tests:** los que ya existían siguen creando usuarios con solo correo mediante una extensión de prueba (`Support/IdentityServiceExtensions.cs`), sin cambiar lo que verifican.
- **Para tener en cuenta más adelante:**
  - Ordenar por `email` deja al final (o al principio, en descendente) las cuentas sin correo. Si la tabla de la Tarea 16 las mezcla mal, ordenar por "identidad mostrada".
  - El alta del admin sigue creando el correo como verificado, como antes. `emailConfirmed` ahora se ve: la Tarea 15 decide si un correo cargado por el admin queda sin verificar.
  - `UserListItem` no trae `emailConfirmed` (solo `/api/me` y el detalle): sumarlo si la tabla de la Tarea 16 marca correos sin verificar.
  - La búsqueda no encuentra un número escrito con el 0 o el 15 ("0351 15 123-4567"); sí sin ellos o en formato internacional.

**Commit:** `feat: cuentas con correo opcional y número de WhatsApp único`

### Tarea 3: `LoginCode` por destino y propósito

**Repo:** backend. **Depende de:** 2. **Spec:** 6.3 y 6.7.

- [x] Tests primero (dominio y aplicación):
  - un código emitido para `SignIn` no verifica con `VerifyDestination`, ni al revés;
  - los límites por destino se comparten entre propósitos;
  - el hash cambia con el propósito;
  - un `VerifyDestination` solo vale para el `RequestedByUserId` que lo pidió.
- [x] `LoginCode`: `Email` pasa a ser `Destination`, y se suman `Channel` (`LoginCodeChannel`), `Purpose` (`LoginCodePurpose`), `RequestedByUserId` y `SentAtUtc`. Este último es null si no se mandó nada; la Tarea 6 lo usa para el tope diario.
- [x] Repositorio: `LockDestinationAsync`, `GetLatestAsync(destination, purpose)`, `ListActiveAsync(destination, purpose, now)` y `ListRequestTimesSinceAsync(destination)`.
- [x] `ILoginCodeHasher.Hash(destination, purpose, code)`.
- [x] Los handlers del correo pasan a usar `Destination = email`, `Channel = Email` y `Purpose = SignIn`. **El comportamiento del correo no cambia**: los tests que ya existen tienen que pasar sin tocar lo que verifican.
- [x] `LoginAudit.Email` pasa a ser `Identifier`, y `LoginMethod` suma `WhatsAppCode` y `WhatsAppLink`.
- [x] Migración `LoginCodeDestination`.

**Aceptación:** los tests anteriores del ingreso en verde, sin cambiar lo que verifican, más los nuevos.

**Hecha el 2026-09-23.** Suite completa 631/631 y build con 0 advertencias. La revisión adversarial (7 hallazgos) no confirmó ninguno. Lo que se hizo distinto del plan, o además:

- **Value object `LoginCodeDestination`** (`ForEmail` y `ForPhone`), con el canal y el valor, para no pasar strings sueltos. A propósito no redefine `ToString`: un destino que termine en un log no deja el número a la vista.
- **`LoginCode.Verify` es el de `SignIn` y `VerifyFor(userId, …)` el de `VerifyDestination`.** Un código de otro propósito, o que pidió otra cuenta, recibe la misma respuesta que la falta de código y no gasta intentos. `MarkSent` conserva el primer envío, y se marca al encolar el mensaje.
- **El hash separa destino, propósito y código con un salto de línea**, no con ":", porque un correo válido puede tener ":" ("a:b@example.com").
- **El reenvío mira el último pedido del destino**, sin importar el propósito (antes usaba `GetLatestAsync`). Para el correo da lo mismo que antes.
- **La migración `LoginCodeDestination` renombra** las columnas (`LoginCodes.Email` → `Destination`, `LoginAudits.Email` → `Identifier`) y el índice. `Channel` y `Purpose` llevan como default "Email" y "SignIn", que es lo que eran todas las filas.
- `CLAUDE.md` apunta ahora a `LoginCodeRepository.LockDestinationAsync`.

**Commit:** `refactor: los códigos de ingreso pasan a ser por destino y propósito`

### Tarea 4: El verify mira el modo de registro

**Repo:** backend. **Depende de:** 3. **Spec:** 10 (corrige el hallazgo 1 de la etapa 1).

- [ ] Test primero (`RegistrationModeTests`): en `InviteOnly`, un código válido para un correo sin cuenta, emitido directamente con el repositorio, responde `403 Auth.Account.NotInvited`, no crea la cuenta y queda auditado.
- [ ] `VerifyLoginCodeCommandHandler`: antes de `CreateAsync`, si el modo no es `Open`, `AccountErrors.NotInvited`.

**Commit:** `fix: el ingreso por código no crea cuentas si el registro es solo por invitación`

### Tarea 5: El cliente de WhatsApp y la cola de envío

**Repo:** backend. **Depende de:** 1. **Spec:** 9 y 14.

- [ ] `WhatsAppOptions`, sección `WhatsApp` (spec 14). Si la sección existe, se valida al arrancar; si no existe, WhatsApp queda apagado (`IWhatsAppAvailability.IsEnabled = false`) y la app arranca igual.
- [ ] `IWhatsAppOutbox` y `WhatsAppOutboundMessage`: destinatario, tipo (texto, botón con enlace, botones de respuesta o plantilla), cuerpo, pie, botones, URL, nombre e idioma de la plantilla, parámetros, y el **resumen seguro** que se guarda en el historial (por ejemplo, "[código]").
- [ ] Tests primero (`Api.IntegrationTests/WhatsApp/WhatsAppCloudClientTests.cs`, con un `HttpMessageHandler` falso):
  - el JSON de la plantilla de autenticación lleva el código **dos veces** (cuerpo y botón `url`, según la doc de Meta);
  - el JSON del botón con enlace (`cta_url`) y el de los botones de respuesta con sus IDs;
  - el `to` va con `+`;
  - el `Authorization: Bearer`;
  - los errores 131030, 131047, 131026, 131056 y el de token inválido se traducen a un resultado tipado;
  - **ningún log contiene el código ni la URL del enlace** (capturar los logs).
- [ ] `WhatsAppCloudClient`: un `HttpClient` tipado contra `https://graph.facebook.com/{version}/{phoneNumberId}/messages`. La resiliencia estándar **no reintenta los POST** de este cliente: verificar la API exacta de `Microsoft.Extensions.Http.Resilience` al implementar.
- [ ] `WhatsAppOutbox` (un `Channel` acotado, como `EmailQueue`) y `WhatsAppSenderBackgroundService`: reintenta 131056, 5xx y timeouts con espera de 6 segundos o más, hasta 3 veces, y registra enmascarado.
- [ ] `ApiFactory`: reemplaza el outbox por `CapturingWhatsAppOutbox` (`factory.WhatsApp`) y carga una configuración de WhatsApp de prueba.

**Commit:** `feat: cliente de la API de WhatsApp y cola de envío`

### Tarea 6: El ingreso web con WhatsApp

**Repo:** backend. **Depende de:** 3, 4 y 5. **Spec:** 10 y 13.

- [ ] Tests primero (`Auth/WhatsAppLoginCodeTests.cs`):
  - `202` con el mismo cuerpo para un número con cuenta y uno sin cuenta;
  - en `InviteOnly`, un número sin cuenta **no encola nada**;
  - en `Open`, sí encola;
  - verificar el código de un número nuevo en `Open` crea la cuenta sin correo y con el número verificado;
  - verificar el código de un número que cargó un admin lo verifica;
  - un país no permitido responde `400 Auth.WhatsApp.CountryNotSupported`;
  - al llegar al tope diario responde `429` y no encola;
  - los límites por número y el reenvío de 60 segundos funcionan (con `factory.WithWebHostBuilder` y los valores reales);
  - el ingreso por correo sigue igual;
  - con WhatsApp apagado, `login-methods` dice `false` y el endpoint responde `404`.
- [ ] `RequestWhatsAppLoginCodeCommand` con su handler y su validador, y `POST /account/login-code/whatsapp` con `LoginCodePolicy`.
- [ ] `VerifyLoginCodeCommand` acepta `Phone` (exactamente uno de los dos) y audita `WhatsAppCode`.
- [ ] `GetLoginMethodsQuery` y `GET /account/login-methods`.
- [ ] Errores nuevos en `Errors.resx` y `Errors.en.resx`: `Users.Phone.Invalid` y `Auth.WhatsApp.CountryNotSupported` (textos del spec, sección 19).

**Commit:** `feat: ingreso con código por WhatsApp desde la web`

### Tarea 7: Front, el ingreso con WhatsApp

**Repo:** front. **Depende de:** 6. **Tablero:** "WhatsApp · Ingreso en la web".

Tiene que quedar igual al tablero:
1. **`/login`:** Google arriba, el separador "o" y, debajo, el selector **Correo | WhatsApp**. Arranca en Correo, y el selector solo aparece si `login-methods` dice que WhatsApp está activo. Con WhatsApp:
   - el campo "Número de WhatsApp", con el país a la izquierda (`AR +54`, de `whatsappCountries`);
   - la ayuda "Te mandamos un código por WhatsApp.";
   - el botón "Enviar código".
2. **Número inválido:** "Ingresá un número de celular válido." en el lugar de la ayuda. Se valida al enviar.
3. **`/login/codigo` con WhatsApp:**
   - el enlace "Usar otro número";
   - el título "Revisá tu WhatsApp";
   - "Te mandamos un código al **+54 9 11 •••• 6789**. Vence en 10 minutos.";
   - las 6 casillas, "Verificar" y "Reenviar en N s";
   - el aviso "Llega al WhatsApp de tu celular. En WhatsApp Web no se muestra."
4. **Código incorrecto:** el error y "Te quedan N intentos" arriba de Verificar, y "Reenviar código" habilitado.

Además, los estados de la lista del tablero con sus textos: vencido, ya usado, sin intentos, cuenta bloqueada, cuenta deshabilitada y demasiados pedidos.

- [ ] Tests primero:
  - el selector no aparece si WhatsApp está apagado;
  - enviar llama al endpoint y navega con `{ channel: "whatsapp", phone, maskedPhone, resendAfterSeconds }`;
  - el error de número inválido;
  - los textos de la pantalla del código con WhatsApp;
  - el reenvío usa el endpoint de WhatsApp;
  - verificar manda `phone`.
- [ ] `loginCode.ts`: `getLoginMethods`, `requestWhatsAppLoginCode` y `verifyLoginCode` con `email` o `phone`. `PhoneField` como componente propio.
- [ ] **Una cuenta sin correo no rompe la app.** Desde la Tarea 2, `/api/me` puede traer `email: null`, y la prueba manual del Hito 1 crea justo esa cuenta (sin correo y sin nombre). Hoy `Sidebar.tsx` y `UserMenu.tsx` hacen `initialOf(user.displayName ?? user.email)` y se caen con `null`. Test primero, y el mínimo: `profile.ts` con `email` opcional y los campos nuevos, y el menú y la barra lateral muestran el número (`displayName ?? email ?? phoneNumber`). El resto del perfil sigue en la Tarea 14.
- [ ] Textos en `locales/es/auth.json` y `locales/en/auth.json`.
- [ ] **Abrir el tablero y comparar** antes de cerrar.

**Commit (front):** `feat: ingreso con WhatsApp en /login`

### Prueba manual del Hito 1 (nivel 1)

La hace el usuario, con el agente. Antes: el token cargado y la plantilla `codigo_ingreso` aprobada.

1. `aspire run`. Entrar a `https://localhost:5173/login`, elegir WhatsApp y escribir tu número (uno de los dos de la lista).
2. Tiene que llegar el código al celular. Escribirlo y entrar.
3. **Verificar lo del 9:** el envío va a `+549…`.
   - Si Meta lo rechaza con 131030 porque la lista tiene el número sin el 9, anotarlo.
   - Se agrega entonces una adaptación **solo para el número de prueba**, activada por configuración (spec 16). Probar también mandar por BSUID.
4. `aspire stop`.

### Hito 2: Meta verifica el webhook

### Tarea 8: El webhook

**Repos:** backend y front (proxy). **Depende de:** 5. **Spec:** 6.5 y 7.

- [ ] `WhatsAppContact` y `WhatsAppMessage` en Domain, con sus repositorios, configuraciones y la migración `WhatsAppMessages`. Índices únicos en `WaMessageId`, `UserIdentifier` (BSUID) y `UserId`.
- [ ] Tests primero (`WhatsApp/WhatsAppWebhookTests.cs`):
  - el GET con la palabra correcta responde el `challenge`; con la palabra incorrecta o con otro `hub.mode`, `403`;
  - el POST sin firma o con una firma incorrecta responde `401` y **no guarda nada**;
  - con la firma correcta (un HMAC calculado en el test con un secreto de prueba), `200` y el contacto guardado con su BSUID y su `wa_id`;
  - **el mismo mensaje dos veces queda una sola vez**;
  - los estados actualizan el mensaje saliente solo si son más nuevos;
  - otro `phone_number_id` se ignora;
  - un cuerpo de más de 5 MB responde `413`.
- [ ] `IWhatsAppWebhookReader` (Infrastructure lee el formato de Meta y devuelve un `WhatsAppWebhookBatch`) y `WhatsAppSignatureValidator`: HMAC-SHA256 del cuerpo crudo con `AppSecret`, comparado con `CryptographicOperations.FixedTimeEquals`.
- [ ] `ReceiveWhatsAppWebhookCommand`: guarda los contactos, los mensajes y los estados. Un duplicado concurrente (violación de índice único) se trata como repetido.
- [ ] `WhatsAppWebhookEndpoints`: lee el cuerpo crudo con límite de 5 MB y usa una política propia de límites, generosa, por IP.
- [ ] Sumar `/webhooks` a `BackendPrefixes`, a `Backend_routes_keep_returning_a_problem` de `SpaHostingTests` y al proxy de `vite.config.ts` (regla de `CLAUDE.md`).

**Commits:** backend `feat: webhook de WhatsApp con firma, guardado y sin duplicados`; front `chore: el proxy de Vite reenvía /webhooks`.

### Tarea 9: El túnel

**Repo:** backend (AppHost). **Depende de:** 8. **Spec:** 16.

- [ ] Agregar `Aspire.Hosting.DevTunnels` 13.5.4 a `Directory.Packages.props` y al AppHost.
- [ ] En `AppHost.cs`, **solo si `DevTunnel:Enabled` es `true`** (apagado por defecto, así `aspire run` no le exige la CLI a quien no la usa):
  - agregar un dev tunnel con un `tunnelId` fijo;
  - exponer **únicamente el endpoint `https` de la Api**, con acceso anónimo en ese puerto.
  - La forma exacta de la API está en la [documentación de la integración](https://aspire.dev/integrations/devtools/dev-tunnels/); verificarla al implementar.
- [ ] README: instalar la CLI, `devtunnel user login`, activar la opción en los user-secrets del AppHost, dónde ver la URL en el dashboard de Aspire y qué cargar en Meta.

**Commit:** `chore: túnel opcional para recibir los webhooks de Meta en local`

### Prueba manual del Hito 2 (nivel 2)

1. Activar el túnel y hacer `aspire run`. La URL del túnel está en el dashboard.
2. En el Paso 2 de Meta:
   - URL de devolución: `https://<túnel>/webhooks/whatsapp`, con la palabra de verificación;
   - tocar **Verificar y guardar**;
   - suscribir `messages`.
3. Tocar **Probar** en el campo `messages`: la Api tiene que responder `200`.
   - Si el ejemplo de Meta trae otro `phone_number_id`, el log dice "ignorado". Alcanza igual: prueba la firma y la ruta de punta a punta.
4. `aspire stop`, y comprobar que el túnel dejó de responder.

### Hito 3: el chat

### Tarea 10: El enlace de ingreso

**Repo:** backend. **Depende de:** 2 y 3. **Spec:** 5, 6.4 y 11.

- [ ] Tests primero:
  - dominio: vence, se consume una sola vez y emitir otro invalida el anterior;
  - integración, con el enlace emitido desde un endpoint de `TestFeatures`:
    - el preview no lo consume;
    - el redeem crea la cookie, y después `/connect/authorize` emite el código;
    - vencido, usado, inventado e invalidado responden **el mismo** `400`;
    - una cuenta deshabilitada responde `403` recién después de un enlace válido;
    - queda auditado como `WhatsAppLink`.
- [ ] `LoginLink`, `LoginLinkErrors`, el repositorio, la configuración y la migración `LoginLinks`.
- [ ] `ISecureTokenGenerator` (32 bytes con `RandomNumberGenerator`, en base64url) y el SHA-256 del token.
- [ ] `LoginLinkIssuer` (lo usa el bot): uno por minuto por cuenta y 5 cada 15 minutos.
- [ ] `PreviewLoginLinkQuery`, `RedeemLoginLinkCommand` (con `IPersistChangesOnFailure`, por la auditoría) y sus endpoints con `LoginVerifyPolicy`.

**Commit:** `feat: enlace de ingreso de un solo uso`

### Tarea 11: El procesador y el bot

**Repo:** backend. **Depende de:** 8 y 10. **Spec:** 7 y 8. **Tablero:** "WhatsApp · Conversaciones con el bot" (los textos).

- [ ] Tests primero, de la **tabla de la sección 8, un test por fila** (`Application.UnitTests/Features/WhatsApp/HandleInboundMessageTests.cs`, con dobles):
  1. cuenta activa: botón **Entrar** con enlace nuevo; el contacto queda vinculado y el número verificado;
  2. deshabilitada, bloqueada o borrada;
  3. sin cuenta con registro abierto: la pregunta con los dos botones;
  4. `CREATE_ACCOUNT` crea la cuenta y manda el enlace; repetido no duplica;
  5. `HAVE_ACCOUNT`;
  6. sin cuenta con registro solo por invitación;
  7. `WANT_TO_ENTER`;
  8. pidió un enlace hace menos de un minuto;
  9. foto o audio responde como un texto.
- [ ] `HandleInboundMessageCommand` y `BotReply`, con los textos en `Bot.resx` y `Bot.en.resx`, copiados del tablero. Sumar el archivo al control de paridad si no lo toma solo.
- [ ] `WhatsAppInboundProcessor` (`BackgroundService`):
  - se despierta con una señal y además revisa la tabla cada 30 segundos;
  - toma los mensajes con `FOR UPDATE SKIP LOCKED`, con un lock por contacto;
  - si hay varios mensajes pendientes del mismo contacto, les da **una sola** respuesta (Meta limita a un mensaje cada 6 segundos por persona);
  - los mensajes de más de 24 horas se marcan procesados sin responder;
  - expone `ProcessPendingAsync()` para los tests, y en los tests el ciclo en segundo plano está apagado.
- [ ] El sender guarda cada mensaje saliente con su `WaMessageId` y su resumen seguro.
- [ ] Tests de integración: un webhook firmado, después `ProcessPendingAsync`, después lo que capturó `factory.WhatsApp`, para las filas 1, 3, 4, 6 y 8. Además: un webhook repetido da una sola respuesta, y un mensaje de hace más de 24 horas no tiene respuesta.

**Commit:** `feat: el bot de WhatsApp responde para entrar y crear la cuenta`

### Tarea 12: Front, `/ingresar`

**Repo:** front. **Depende de:** 10. **Tablero:** "WhatsApp · El enlace del chat".

Tiene que quedar igual al tablero, en el marco de `AuthLayout`:
1. **"Entrar a Arquitectura Base"**:
   - "Vas a entrar como", con la tarjeta de la inicial, el nombre y el número enmascarado;
   - el botón **Continuar**;
   - "¿No pediste entrar? Cerrá esta página: si no tocás Continuar, no pasa nada."
2. **"Iniciando sesión…"**, con "Esto puede tardar unos segundos.".
3. **"Este enlace ya no sirve"**, con su explicación, el botón **Volver a WhatsApp** (`https://wa.me/<whatsappNumber>`) e **Ir al ingreso**.
4. **"No podés entrar"**, con el texto de cuenta deshabilitada e **Ir al ingreso**.

- [ ] Tests primero:
  - lee el token del fragmento y lo borra de la barra (`history.replaceState`);
  - el preview muestra el pantallazo 1;
  - Continuar hace el redeem y después `signinRedirect`;
  - un token inválido muestra el 3;
  - una cuenta deshabilitada muestra el 4.
- [ ] `LoginLinkPage` y la ruta `/ingresar` en `routes.tsx`, dentro de `AuthLayout` y sin `lazy`, como las otras pantallas del ingreso.
- [ ] Textos en `auth.json`, en español y en inglés.
- [ ] **Abrir el tablero y comparar.**

**Commit (front):** `feat: /ingresar, la entrada con el enlace del chat`

### Prueba manual del Hito 3 (nivel 3)

Antes: la política de privacidad publicada y la app de Meta publicada.

1. Activar el túnel y hacer `aspire run`. Abrir el chat con el número de prueba en **WhatsApp Web, en la PC**.
2. Desde tu celular (con cuenta), escribir "Hola". Llega **Entrar**; tocarlo en WhatsApp Web y pasar por `/ingresar` y Continuar hasta el inicio. Revisar de nuevo lo del 9, ahora en la respuesta del bot.
3. Con el registro abierto, desde el segundo celular (sin cuenta): **Crear cuenta**, después **Entrar**. Probar también **Ya tengo cuenta**.
4. Con el registro solo por invitación, repetir desde un número sin cuenta: llega el mensaje de acceso por invitación.
5. `aspire stop`.

### Hito 4: perfil y administración

### Tarea 13: El perfil, backend

**Repo:** backend. **Depende de:** 3, 6 y 8. **Spec:** 12.

- [ ] Tests primero:
  - vincular manda el código con `VerifyDestination`, y con el código correcto el número queda vinculado y verificado, y su contacto de WhatsApp apunta a la cuenta;
  - un número de otra cuenta responde `409` **solo después** de un código correcto;
  - agregar un correo funciona igual (`409 Users.User.AlreadyExists` después del código);
  - desvincular sin otro medio responde `409 Users.User.LastLoginMethod`;
  - con un correo verificado o con Google, desvincula y también desvincula el contacto.
- [ ] Los comandos de la estructura de archivos y sus endpoints en `MeEndpoint`.
- [ ] `UserGuards.HasOtherLoginMethod`, con su test de unidad.
- [ ] **Decidir si los códigos de `VerifyDestination` se buscan por cuenta.** Desde la Tarea 3, `ListActiveAsync` y `GetLatestAsync` filtran por destino y propósito. Si otra cuenta pide un código para el mismo número, invalida el de la dueña, que tiene que pedir otro. Es la misma molestia que ya permiten los límites compartidos por destino. Si se quiere evitar, sumar `RequestedByUserId` al filtro de esas dos consultas cuando el propósito es `VerifyDestination`.

**Commit:** `feat: vincular WhatsApp y agregar el correo desde el perfil`

### Tarea 14: Front, el perfil

**Repo:** front. **Depende de:** 13. **Tablero:** "WhatsApp · Perfil: correo y WhatsApp".

Tiene que quedar igual al tablero:
1. **Mi perfil** con **dos superficies**, cada una con su banda de encabezado:
   - **"Medios de ingreso"**:
     - fila del correo: ícono, "Correo electrónico", "Todavía no agregaste uno.", **Agregar correo** y la ayuda de recuperar la cuenta;
     - fila de WhatsApp: ícono, el número, la insignia **Verificado** y, si es el único medio, la ayuda en lugar de **Desvincular**.
   - **"Datos del perfil"**: último ingreso, nombre, idioma, zona horaria y Guardar.
   - En el menú del usuario, el número donde iría el correo.
2. **El aviso del inicio:** "Agregá un correo para no perder el acceso si cambiás de número." con **Agregar correo**. Solo aparece mientras no haya correo.
3. **Cuenta con correo:** la fila de WhatsApp "Sin vincular" con **Vincular**, y el diálogo **Vincular WhatsApp** (número; después el código, en el mismo diálogo). **Agregar correo** usa el mismo diálogo, con el correo.
4. **La confirmación** "¿Desvincular tu WhatsApp?", con su texto y el botón peligroso.
5. **El error** "Este número ya está vinculado a otra cuenta…".

- [ ] Tests primero de cada uno de los cinco puntos.
- [ ] `LoginMethodsCard`, `LinkWhatsAppDialog`, `AddEmailDialog` y `UnlinkWhatsAppDialog`; separar `ProfilePage` en las dos superficies; el aviso en `DashboardPage`; `UserMenu` y `Sidebar` con el número.
- [ ] Textos en `profile.json` y `common.json`, en los dos idiomas.
- [ ] **Abrir el tablero y comparar.**

**Commit (front):** `feat: medios de ingreso en el perfil`

### Tarea 15: La administración, backend

**Repo:** backend. **Depende de:** 2, 5, 11 y 13. **Spec:** 6.6 y 12.

- [ ] Tests primero:
  - alta solo con número;
  - sin correo ni número, `Users.Identity.Required`;
  - número repetido, `409`;
  - invitación por WhatsApp sin consentimiento, `400`;
  - invitación por WhatsApp sin nombre, `400 Users.Invitation.NameRequired` (ver las decisiones al final);
  - la invitación por WhatsApp encola la plantilla con el nombre y el nombre del sistema; la de correo manda el correo nuevo;
  - `DELETE /api/users/{id}/whatsapp` desvincula, **cierra las sesiones** (el access token deja de valer) y desvincula el contacto;
  - reenviar la invitación funciona;
  - tocar **Quiero entrar** en la plantilla (`WANT_TO_ENTER`) manda el enlace (fila 7 del bot).
- [ ] `UserInvitation` con su repositorio, configuración y la migración `UserInvitations`.
- [ ] La plantilla de correo `Invitation.html` y sus textos en `Emails.resx` y `Emails.en.resx`.
- [ ] `CreateUser` y `UpdateUser` con el contrato, más `UnlinkUserPhoneCommand` y `SendInvitationCommand`.
- [ ] Errores nuevos: `Users.Phone.AlreadyExists`, `Users.Identity.Required`, `Users.Invitation.ConsentRequired`, `Users.Invitation.NameRequired`, `Users.User.LastLoginMethod` y `Auth.LoginLink.Invalid`, con los textos del spec.

**Commit:** `feat: alta con número e invitaciones por correo y WhatsApp`

### Tarea 16: Front, los usuarios

**Repo:** front. **Depende de:** 15. **Tablero:** "WhatsApp · Usuarios: alta con teléfono".

Tiene que quedar igual al tablero:
1. **El listado:**
   - el buscador dice "Buscar por correo, nombre o número";
   - la primera columna se llama **Usuario**: el punto de estado, y después el correo, o el ícono del teléfono y el número si no hay correo;
   - el ícono del teléfono al lado del correo cuando también entra con WhatsApp;
   - la insignia **Sin verificar** para un número que cargó un admin;
   - las columnas siguen igual: Nombre, Roles, Creado y Acciones;
   - la primera acción es **Editar** (el lápiz).
2. **Nuevo usuario:**
   - correo, número de WhatsApp (con el país), nombre y roles;
   - la casilla **Mandarle una invitación** y el canal **Por correo** / **Por WhatsApp**. Por correo queda deshabilitado, con su ayuda, si no hay correo;
   - la casilla de **consentimiento**, con su ayuda;
   - el botón **Crear e invitar**.
3. **Editar usuario:** nombre, roles y la sección **Medios de ingreso**, con el correo (o **Agregar**) y el WhatsApp con **Verificado** y **Desvincular**. Si es el único medio, la advertencia, y la ayuda de "Sin verificar".
4. **La confirmación** "¿Desvincular el WhatsApp de …?", con su texto según tenga correo o no.

Además, los errores del alta con sus textos (la lista del tablero).

- [ ] Tests primero de cada punto y de cada error.
- [ ] `columns.tsx`, `UserFormDialog` (alta y edición), `UnlinkUserWhatsAppDialog`, `users.ts` y `errors.ts`.
- [ ] Textos en `users.json`, en los dos idiomas.
- [ ] **Abrir el tablero y comparar.**

**Commit (front):** `feat: alta con número e invitaciones en usuarios`

### Prueba manual del Hito 4

Antes: la plantilla `invitacion_acceso` aprobada.

1. Con el registro solo por invitación, dar de alta el segundo celular e invitarlo por WhatsApp. Llega la invitación; tocar **Quiero entrar**, después **Entrar**, y queda adentro.
2. Invitar por correo a una dirección tuya: llega el correo, **Ingresar** lleva a `/login` y se entra con el código.
3. Desde un perfil con correo, vincular WhatsApp. Desvincularlo. Intentar desvincular el único medio de una cuenta que solo tiene WhatsApp: no deja.
4. Desde el admin, desvincular el WhatsApp de alguien con la sesión abierta en otro navegador: la sesión se corta.
5. `aspire stop`.

### Cierre

### Tarea 17: La retención de los mensajes

**Repo:** backend. **Depende de:** 11. **Spec:** 6.5.

- [ ] Test primero, con `FakeTimeProvider`: los mensajes con más de `WhatsApp:MessageRetentionDays` días (90 por defecto) quedan sin texto; los contactos no se tocan.
- [ ] `WhatsAppMessageRetentionService`, que corre una vez por día. `WhatsAppMessage` **no** es `IAuditable` ni `ISoftDeletable`, así que puede usar `ExecuteUpdate` (regla de `CLAUDE.md`).

**Commit:** `feat: los mensajes de WhatsApp se vacían a los 90 días`

### Tarea 18: Documentación y cierre

**Repos:** los dos.

- [ ] `CLAUDE.md` del backend, una sección "WhatsApp" con las reglas que un agente tiene que saber:
  - la regla de oro (spec 5);
  - los códigos por destino;
  - los números con el 9;
  - la firma y los duplicados del webhook;
  - el bot sin estado;
  - los números enmascarados en los logs;
  - el túnel opcional;
  - los secretos;
  - `/webhooks` en `BackendPrefixes`.
- [ ] `CLAUDE.md` del front: `/ingresar`, el selector del ingreso y los medios de ingreso del perfil.
- [ ] README: la configuración de WhatsApp (qué va en user-secrets y qué en appsettings), el túnel y las plantillas.
- [ ] `visual-baseline.md` del front: las filas nuevas del mapa Artifact → proyecto.
- [ ] Este plan: la sección "Resultado de la ejecución", con el mismo formato que la Fase 5.

**Commits:** backend `docs: cerrar el ingreso con WhatsApp`; front `docs: el ingreso con WhatsApp en el CLAUDE.md del front`.

---

## MVP y producción

**El MVP es este plan:** todo funcionando en local, con el número de prueba y los dos celulares de la lista.

**Producción queda afuera de este plan** y se planifica aparte, con este mismo diseño:

1. **Número propio**: registrarlo en el Paso 2, con nombre visible y PIN de dos pasos, y que no esté en uso en WhatsApp.
2. **Medio de pago** en la cuenta de WhatsApp.
3. **Las plantillas aprobadas en la cuenta del número real.** Agregar un número real desde el panel de configuración de la API crea una cuenta de WhatsApp nueva (la de prueba, "Test WhatsApp Business Account", la creó Meta sola junto con el número de prueba), así que las plantillas se crean de nuevo ahí.
4. **Un servidor con HTTPS propio** y su URL de webhook. El número de prueba se desvía al túnel con la URL alternativa por número.
5. **Los secretos** en variables de entorno o en un almacén.
6. **"Requerir secreto de la app"** en Meta y el control de cambio de identidad del número.
7. **El cambio de número** de una persona (el mensaje de sistema de WhatsApp).
8. **La política de privacidad definitiva** en el dominio propio.
9. **El pendiente de la Fase 3:** copiar el `dist/` del front al `wwwroot` de la Api.
10. **Monitoreo:** un aviso si el token deja de valer y el seguimiento del costo de las plantillas.
11. **La bandeja del admin** (la segunda entrega).

## Decisiones del plan (aprobadas el 2026-09-22)

1. **Para invitar por WhatsApp hace falta el nombre.** La plantilla saluda por nombre ("Hola, Laura", tablero de mensajes, 2) y Meta no deja un parámetro vacío.
2. **El túnel se activa con una opción**, apagada por defecto. Así `aspire run` sigue funcionando para quien no tiene la CLI.
3. **El tope diario de códigos por WhatsApp arranca en 100.** Es configurable.

## Lo que necesito de vos, y cuándo

| Cuándo | Qué |
|---|---|
| Hito 1 | El token y la plantilla de autenticación (te guío en el momento) |
| Hito 2 | El secreto de la app, la palabra de verificación y la CLI del túnel |
| Hito 3 | Los tres datos de la política de privacidad (responsable, correo de contacto y dónde publicarla) para que te la redacte, y publicar la app |
| Hito 4 | La plantilla de invitación |
