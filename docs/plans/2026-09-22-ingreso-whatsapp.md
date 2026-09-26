> **HISTÓRICO. No ejecutar.** Registro de cómo se construyó esta parte. La arquitectura vigente está en [`docs/specs/2026-09-24-backend-mvc-architecture.md`](../specs/2026-09-24-backend-mvc-architecture.md); donde este documento hable de handlers, `Features/` o Minimal API, prevalece la especificación.

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
| `invitacion_acceso` | **Marketing** (ver la nota) | "Hola, {{1}}. Tu cuenta de {{2}} está activa y asociada a este número de WhatsApp. Para ingresar, tocá «Quiero entrar» y te enviamos un enlace de acceso." con un botón de respuesta rápida "Quiero entrar". En inglés: "Hi, {{1}}. Your {{2}} account is active and linked to this WhatsApp number. To sign in, tap “I want to sign in” and we'll send you an access link.", con "I want to sign in". `{{1}}` es el nombre y `{{2}}` el nombre del sistema (`Email:AppName`) |

**`invitacion_acceso` quedó en revisión el 2026-09-23 como Marketing,** en `es` y `en`. Iba como Utilidad y Meta no la aceptó: Utilidad pide un mensaje que la persona haya pedido y sin intención de convencer, y una invitación que manda un administrador, con "tocá Quiero entrar", no cumple eso aunque se reescriba. Primero se probó el texto del tablero y después uno de aviso de cuenta, que es el que quedó. El usuario eligió aceptar Marketing. Consecuencias:
- cada invitación cuesta más en producción;
- Meta limita cuántos mensajes de marketing recibe cada persona, así que una invitación puede no llegar. El respaldo es reenviarla o invitar por correo. El backend expone el estado (Tarea 15), pero mostrarlo en la pantalla falta dibujarlo;
- el tablero WA-Mensajes (panel 2) tiene que pasar al texto nuevo.

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

**`/api/me`** suma `email` (ahora opcional), `emailConfirmed`, `phoneNumber`, `phoneNumberConfirmed` y `hasGoogleLogin`; desde la Tarea 14, también `formattedPhoneNumber` ("+54 9 11 2345-6789") y `maskedPhoneNumber` ("+54 9 11 •••• 6789"), null sin número.

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

- [x] Test primero (`RegistrationModeTests`): en `InviteOnly`, un código válido para un correo sin cuenta, emitido directamente con el repositorio, responde `403 Auth.Account.NotInvited`, no crea la cuenta y queda auditado.
- [x] `VerifyLoginCodeCommandHandler`: antes de `CreateAsync`, si el modo no es `Open`, `AccountErrors.NotInvited`.

**Hecha el 2026-09-23.** Suite completa 639/639 y build con 0 advertencias. La revisión adversarial (2 hallazgos, los dos del front) no confirmó ninguno. Lo que se hizo, además de lo pedido:

- **El chequeo vive en `CreateAccountAsync`**, un método privado del handler que primero mira el modo (cualquier modo que no sea `Open` cierra), después la cuenta borrada y al final crea. Es el mismo orden que Google. La Tarea 6 lo extiende con el número sin duplicar el chequeo.
- **Tests de más:** una cuenta existente sigue entrando con código en `InviteOnly` (no había un test que lo cubriera), `Open` crea la cuenta con un código emitido a mano, y una cuenta borrada responde `NotInvited` en `InviteOnly` y `Disabled` en `Open`.
- El XML doc de `AccountErrors.NotInvited` y `CLAUDE.md` ahora mencionan también el ingreso con código.

**Commit:** `fix: el ingreso por código no crea cuentas si el registro es solo por invitación`

### Tarea 5: El cliente de WhatsApp y la cola de envío

**Repo:** backend. **Depende de:** 1. **Spec:** 9 y 14.

- [x] `WhatsAppOptions`, sección `WhatsApp` (spec 14). Si la sección existe, se valida al arrancar; si no existe, WhatsApp queda apagado (`IWhatsAppAvailability.IsEnabled = false`) y la app arranca igual.
- [x] `IWhatsAppOutbox` y `WhatsAppOutboundMessage`: destinatario, tipo (texto, botón con enlace, botones de respuesta o plantilla), cuerpo, pie, botones, URL, idioma de la plantilla (el nombre lo pone Infrastructure desde la configuración), parámetros, y el **resumen seguro** que se guarda en el historial (por ejemplo, "[código]").
- [x] Tests primero (`Api.IntegrationTests/WhatsApp/WhatsAppCloudClientTests.cs`, con un `HttpMessageHandler` falso):
  - el JSON de la plantilla de autenticación lleva el código **dos veces** (cuerpo y botón `url`, según la doc de Meta);
  - el JSON del botón con enlace (`cta_url`) y el de los botones de respuesta con sus IDs;
  - el `to` va con `+`;
  - el `Authorization: Bearer`;
  - los errores 131030, 131047, 131026, 131056 y el de token inválido se traducen a un resultado tipado;
  - **ningún log contiene el código ni la URL del enlace** (capturar los logs).
- [x] `WhatsAppCloudClient`: un `HttpClient` tipado contra `https://graph.facebook.com/{version}/{phoneNumberId}/messages`. La resiliencia estándar **no reintenta los POST** de este cliente: verificar la API exacta de `Microsoft.Extensions.Http.Resilience` al implementar.
- [x] `WhatsAppOutbox` (un `Channel` acotado, como `EmailQueue`) y `WhatsAppSenderBackgroundService`: reintenta 131056, 5xx y timeouts con espera de 6 segundos o más, hasta 3 veces, y registra enmascarado.
- [x] `ApiFactory`: reemplaza el outbox por `CapturingWhatsAppOutbox` (`factory.WhatsApp`) y carga una configuración de WhatsApp de prueba.

**Hecha el 2026-09-23.** Suite completa 717/717 y build con 0 advertencias. Hubo dos pasadas de revisión adversarial más una de ajustes. Se confirmaron cuatro hallazgos menores, de tests y documentación, y quedaron corregidos. Lo que se hizo distinto del plan, o además:

- **El interruptor es `WhatsApp:PhoneNumberId`**, igual que Google con su `ClientId`. Sin él, WhatsApp queda apagado y la app arranca. Con él y sin token, la app no arranca y el error trae el comando exacto de `dotnet user-secrets`. Por eso `PhoneNumberId` todavía **no** está en `appsettings.Development.json`: se agrega en la prueba manual del Hito 1, junto con el token.
- **La plantilla la elige Infrastructure.** El mensaje es `WhatsAppLoginCodeMessage(teléfono, idioma, código)` y el nombre sale de `WhatsApp:Templates:LoginCode`. La plantilla de invitación queda para la Tarea 15.
- **`IWhatsAppOutbox.TryEnqueue` devuelve `bool`:** la Tarea 6 marca `SentAtUtc` solo si el mensaje entró en la cola.
- **Errores de Meta:**
  - no se reintentan: `InvalidToken` (0, 190, 401) y `MissingPermission` (3, 10, 200 a 299, 403). Van al log como Error y ponen la salud en Degraded; el mensaje avisa que, después de cargar un token nuevo o darle los permisos, hay que reiniciar la Api;
  - se reintentan: 131056, 130429 y los transitorios, esperando al menos 6 segundos (`RetryDelaySeconds` no baja de 6) y hasta 3 intentos;
  - tampoco se reintentan: 131047, 131026, 131030 y un 2xx sin id.
- **Health check `whatsapp`** (lo pide el spec 9), solo de readiness.
- **El cliente de Meta no reintenta los POST:** `RemoveAllResilienceHandlers` más el handler estándar con `Retry.DisableForUnsafeHttpMethods()`. `RemoveAllResilienceHandlers` es experimental en la 10.10 y su aviso se suprime solo en `WhatsAppRegistration.cs`, con justificación. Hay un test que pasa por la configuración real de ServiceDefaults: sin el ajuste, salen 4 envíos.
- **El cliente de Meta tampoco usa los logs automáticos de `HttpClient`:** en nivel Trace guardan el header `Authorization` completo en el estado estructurado. El sender registra cada resultado con el número enmascarado.
- **Para tener en cuenta más adelante:** el destinatario es siempre un `PhoneNumber`. Si algún día WhatsApp manda contactos solo con BSUID, el bot va a necesitar responder por BSUID (spec 16).

**Commit:** `feat: cliente de la API de WhatsApp y cola de envío`

### Tarea 6: El ingreso web con WhatsApp

**Repo:** backend. **Depende de:** 3, 4 y 5. **Spec:** 10 y 13.

- [x] Tests primero (`Auth/WhatsAppLoginCodeTests.cs`):
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
- [x] `RequestWhatsAppLoginCodeCommand` con su handler y su validador, y `POST /account/login-code/whatsapp` con `LoginCodePolicy`.
- [x] `VerifyLoginCodeCommand` acepta `Phone` (exactamente uno de los dos) y audita `WhatsAppCode`.
- [x] `GetLoginMethodsQuery` y `GET /account/login-methods`.
- [x] Errores nuevos en `Errors.resx` y `Errors.en.resx`: `Users.Phone.Invalid` y `Auth.WhatsApp.CountryNotSupported` (textos del spec, sección 19).

**Hecha el 2026-09-23.** Suite completa 803/803 y build con 0 advertencias. La revisión adversarial (14 hallazgos) no confirmó ninguno. Lo que se hizo distinto del plan, o además:

- **`LoginCodeIssuer`** (`Features/Auth`) junta lo que comparten el correo y WhatsApp al pedir un código: el lock, los límites por destino, la invalidación y la emisión.
- **El verify tiene un solo camino para los dos canales**, con un "identificador" privado (correo o número) que sabe su destino, cómo buscar la cuenta, la cuenta borrada, el alta y el `LoginMethod`.
- **Con WhatsApp apagado, `POST /account/login-code/whatsapp` no se mapea:** el 404 lo arma el framework (`Http.NotFound`).
- **`Users.Phone.Invalid` llega sin `errors`:** `ProblemDetailsMapper` solo arma `errors` para las validaciones. El front lo ata al campo del número (Tarea 7).
- **Opciones de Application (`WhatsAppLoginOptions`)**, sección `WhatsApp`:
  - `AllowedCountries`, que sin configurar vale `AR`. No tiene valor inicial en la clase, porque el binder suma los elementos configurados a los que ya hay;
  - `DailyAuthCodeLimit`;
  - `DisplayPhoneNumber`, opcional.
- **`IGoogleAvailability`** le dice a Application si Google está configurado, para `login-methods`.
- **El tope diario cuenta solo lo que salió** (`SentAtUtc`), en una ventana móvil de 24 horas, y responde el mismo `TooManyRequests` que el límite por número. Si se llega al tope, queda un log Warning, sin el número.
  - *Riesgo aceptado:* en `InviteOnly`, alguien que lleve el contador justo al borde podría ver si un pedido ocupó un lugar, y con eso saber si el número tiene cuenta. Es caro, se aprende un número por día y cada prueba le corta el ingreso por WhatsApp a todos. Contar también lo que no sale cerraría la pista, pero dejaría agotar el tope con 100 pedidos de números inventados, y el tope existe para controlar el costo.
  - La consulta no tiene índice propio (ver "MVP y producción").
- **Tests:** el arnés relaja el tope diario, como los demás límites. `TestPhones.Unique` genera números que libphonenumber reconoce como celulares de Córdoba: los 7 dígitos no empiezan con 1.

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

- [x] Tests primero:
  - el selector no aparece si WhatsApp está apagado;
  - enviar llama al endpoint y navega con `{ channel: "whatsapp", phone, maskedPhone, resendAfterSeconds }`;
  - el error de número inválido;
  - los textos de la pantalla del código con WhatsApp;
  - el reenvío usa el endpoint de WhatsApp;
  - verificar manda `phone`.
- [x] `loginCode.ts`: `getLoginMethods`, `requestWhatsAppLoginCode` y `verifyLoginCode` con `email` o `phone`. `PhoneField` como componente propio.
- [x] **Errores del número en `/login`.** `Users.Phone.Invalid` llega sin `errors` (Tarea 6): mostrarlo bajo el campo del número. `Auth.WhatsApp.CountryNotSupported` también va bajo el campo. En el verify, el error de validación de "correo y número a la vez" viene en `errors.phone`.
- [x] **`Auth.Account.NotInvited` en `/login/codigo`** (desde la Tarea 4, el verify lo responde en lugar de crear la cuenta). Hoy la pantalla muestra el `detail` del ProblemDetails, pero deja Verificar habilitado. Si la persona reintenta, el código ya está usado y el mensaje cambia a "ya se usó". Tratar `NotInvited` y `Auth.Account.Disabled` como errores que cortan el intento, igual que `lockedOutCodes`: deshabilitar Verificar y el reenvío, y mostrar el enlace para volver a `/login`. Test primero.
- [x] **`/login?error=<código>`.** Cuando Google falla, el backend redirige ahí (`ExternalLoginEndpoints`), pero `LoginPage` no lee el parámetro y no muestra nada. Es un hueco que viene de la Fase 4. Mostrar el mensaje con textos propios en `auth.json` (es y en) para `Auth.Account.NotInvited`, `Auth.Account.Disabled`, `Auth.Account.LockedOut` y los de `ExternalLogin`, y uno genérico para el resto. Test primero.
- [x] **Una cuenta sin correo no rompe la app.** Desde la Tarea 2, `/api/me` puede traer `email: null`, y la prueba manual del Hito 1 crea justo esa cuenta (sin correo y sin nombre). Hoy `Sidebar.tsx` y `UserMenu.tsx` hacen `initialOf(user.displayName ?? user.email)` y se caen con `null`. Test primero, y el mínimo: `profile.ts` con `email` opcional y los campos nuevos, y el menú y la barra lateral muestran el número (`displayName ?? email ?? phoneNumber`). El resto del perfil sigue en la Tarea 14.
- [x] Textos en `locales/es/auth.json` y `locales/en/auth.json`.
- [x] **Abrir el tablero y comparar** antes de cerrar.

**Hecha el 2026-09-23** (front `4d615ce`). Build y lint limpios; tests 399/400. El que falla es `RoleEditorPage.test.tsx › creates a role…`, que no es de esta tarea: pasa solo, se corta por timeout (5,4 s contra 5 s) con la suite completa, y ya fallaba antes de empezar. Revisión adversarial: 14 hallazgos y ninguno confirmado. Igual se corrigieron dos: las casillas del código se marcan en error, como en el tablero, y "Te queda 1 intento" va en singular (la clave no tenía `_one`/`_other`). Lo que se hizo distinto del plan, o además:

- **`/login?error=` llega sin `returnUrl`**, porque así redirige el backend después de Google. Sin `returnUrl`, `/login` arranca el OIDC. Por eso el código del error cruza ese redirect en `sessionStorage` (`arquitecturabase.login-error`, solo el código), se muestra una vez y se borra. *Mejora posible:* que el backend incluya el `returnUrl` en ese redirect; el front ya soporta las dos formas.
- **`LoginPage` se partió** en `EmailCodeForm` y `WhatsAppCodeForm`. El país inicial es el primero de `whatsappCountries`. `PhoneField` usa el `Select` de `shared/ui` (es su primer uso).
- **`SegmentedControl` suma `fullWidth`.** *Pendiente:* sumar esa variante a la biblioteca "ArquitecturaBase UI" del Artifact (la regla es cambiar la biblioteca y `shared/ui` a la vez).
- **`src/auth/accountName.ts`** arma el nombre (`displayName ?? email ?? phoneNumber`) y la inicial (la primera letra o cifra, o "?"). El número se muestra en E.164 hasta la Tarea 14.
- `ProfilePage` oculta la fila del correo si no hay correo.
- *Para la Tarea 18:* documentar en el `CLAUDE.md` del front `login-methods`, el error que cruza el redirect, `fullWidth`, `accountName` y el stub de `scrollIntoView` en el setup de los tests. En esta tarea no se tocó, porque tiene cambios sin commitear de otra sesión.

**Commit (front):** `feat: ingreso con WhatsApp en /login`

### Prueba manual del Hito 1 (nivel 1)

La hace el usuario, con el agente. Antes: la plantilla `codigo_ingreso` aprobada (ya lo está desde el 2026-09-23).

0. **Prender WhatsApp en desarrollo**, en este orden:
   - el usuario carga el token en su terminal: `dotnet user-secrets set "WhatsApp:AccessToken" "<token>" --project src/ArquitecturaBase.Api`;
   - el agente agrega `WhatsApp:PhoneNumberId` (`1340198875839831`), `WhatsApp:BusinessAccountId` (`1658125822339116`) y `WhatsApp:DisplayPhoneNumber` (`15551632662`, el número del bot, para "Volver a WhatsApp") a `src/ArquitecturaBase.Api/appsettings.Development.json`.
   Al revés, la Api no arranca: con `PhoneNumberId` y sin token, falla a propósito (Tarea 5).
1. `aspire run`. Entrar a `https://localhost:5173/login`, elegir WhatsApp y escribir tu número (uno de los dos de la lista).
2. Tiene que llegar el código al celular. Escribirlo y entrar.
3. **Verificar lo del 9:** el envío va a `+549…`.
   - Si Meta lo rechaza con 131030 porque la lista tiene el número sin el 9, anotarlo.
   - Se agrega entonces una adaptación **solo para el número de prueba**, activada por configuración (spec 16). Probar también mandar por BSUID.
4. `aspire stop`.

**Hecha el 2026-09-23, con éxito.** El código llegó al WhatsApp del celular y el usuario entró. La cuenta quedó sin correo, con el número verificado y guardado con el 9, el rol `User` y la auditoría `WhatsAppCode`. Lo que salió en la prueba:

- **Lo del 9 (paso 3):** Meta rechazó `+549…` con **131030**, porque la lista de destinatarios guarda el número sin el 9. Se sumó `WhatsApp:SendArgentineMobilesWithoutNine` (`1316e3c`), prendida solo en `appsettings.Development.json` (`6b50d72`): el `to` va sin el 9 y el número sigue guardado con el 9. Con eso llegó. No hizo falta probar el envío por BSUID.
- **Bug de la Fase 4 (no es de WhatsApp):** en una base nueva en `InviteOnly` nadie puede entrar, ni el admin. El seed no crea la cuenta de `Seed:AdminEmail`, al correo sin cuenta no se le manda el código, el verify responde `NotInvited` y Google también. Se esquivó pasando la fila de `SystemSettings` a `Open` en la base de desarrollo. **Arreglado el 2026-09-23** (`a956b8a`): `Seed:AdminEmail` crea su cuenta en cualquier modo, por código o con Google. La regla vive en `AccountCreationPolicy`, y lo explican el README, la guía de Azure y `CLAUDE.md`.
- **Para la Tarea 14:** con una cuenta de solo número, la barra lateral muestra el número dos veces (en el renglón del nombre y en el de abajo), y crudo, en E.164.
- El token del usuario del sistema quedó cargado en user-secrets. Una captura dejó ver parte del token en el chat; el usuario decidió no revocarlo. Para producción se genera otro de todos modos (otra cuenta de WhatsApp).

### Hito 2: Meta verifica el webhook

### Tarea 8: El webhook

**Repos:** backend y front (proxy). **Depende de:** 5. **Spec:** 6.5 y 7.

- [x] `WhatsAppContact` y `WhatsAppMessage` en Domain, con sus repositorios, configuraciones y la migración `WhatsAppMessages`. Índices únicos en `WaMessageId`, `UserIdentifier` (BSUID) y `UserId`.
- [x] Tests primero (`WhatsApp/WhatsAppWebhookTests.cs`):
  - el GET con la palabra correcta responde el `challenge`; con la palabra incorrecta o con otro `hub.mode`, `403`;
  - el POST sin firma o con una firma incorrecta responde `401` y **no guarda nada**;
  - con la firma correcta (un HMAC calculado en el test con un secreto de prueba), `200` y el contacto guardado con su BSUID y su `wa_id`;
  - **el mismo mensaje dos veces queda una sola vez**;
  - los estados actualizan el mensaje saliente solo si son más nuevos;
  - otro `phone_number_id` se ignora;
  - un cuerpo de más de 5 MB responde `413`.
- [x] `IWhatsAppWebhookReader` (Infrastructure lee el formato de Meta y devuelve un `WhatsAppWebhookBatch`) y `WhatsAppSignatureValidator`: HMAC-SHA256 del cuerpo crudo con `AppSecret`, comparado con `CryptographicOperations.FixedTimeEquals`.
- [x] `ReceiveWhatsAppWebhookCommand`: guarda los contactos, los mensajes y los estados. Un duplicado concurrente (violación de índice único) se trata como repetido.
- [x] `WhatsAppWebhookEndpoints`: lee el cuerpo crudo con límite de 5 MB y usa una política propia de límites, generosa, por IP.
- [x] Sumar `/webhooks` a `BackendPrefixes`, a `Backend_routes_keep_returning_a_problem` de `SpaHostingTests` y al proxy de `vite.config.ts` (regla de `CLAUDE.md`).

**Hecha el 2026-09-23** (backend `5e46f7a`, front `6359918`). Backend: 964/964 y 0 advertencias. Front: build, lint y 400/400. La revisión adversarial tuvo dos vueltas: se confirmaron 11 hallazgos y quedaron corregidos. Los más importantes:
- un texto con un emoji cortado o con `\0` tiraba el lote entero en 500, y Meta lo reintentaría 7 días;
- el BSUID tenía un límite de 128 caracteres, y Meta documenta hasta 256;
- el endpoint anónimo reservaba memoria según el `Content-Length` antes de validar la firma.

Lo que se hizo distinto del plan, o además:

- **El webhook se prende con `WhatsApp:AppSecret` y `WhatsApp:VerifyToken`**, además de `PhoneNumberId`:
  - sin ninguno, las rutas no se mapean (404), la Api arranca con un Warning que nombra las dos claves, y el envío sigue funcionando;
  - con uno solo, la Api no arranca y el error trae el comando de `dotnet user-secrets`.
- **Cada mensaje entrante trae su remitente** (wa_id, BSUID y nombre de perfil), en lugar de una lista aparte de contactos. Solo quien le escribe al bot pasa a ser contacto.
- **Duplicados:**
  - locks de Postgres por contacto y por mensaje de estado, tomados en orden (`AdvisoryLockExtensions`, nuevo; `LoginCodeRepository` no se tocó);
  - `UnitOfWork` deshace la transacción si falla el guardado y traduce el 23505 a `UniqueConstraintViolationException`;
  - si igual choca, el endpoint reintenta una vez en un scope nuevo.
- **Estados:** un saliente no tiene estado hasta que Meta avisa. Gana el timestamp más nuevo; si empatan, sent < delivered < read < failed, y failed es final y guarda el código de error.
- **Tipos de mensaje:** `ButtonReply` guarda en `ReplyId` el id del botón (o el payload de un botón de plantilla), para la Tarea 11. Los medios se guardan sin cuerpo. `System` guarda su texto, que tiene números, solo en la base.
- **Robustez:** lo ilegible de un webhook firmado se saltea (200 y un log de cantidades), porque si no Meta lo reintentaría durante días. Los textos se limpian (`\0` y emojis partidos).
- **La palabra de verificación** se compara por su SHA-256 en tiempo constante.
- **Rate limit:** `whatsapp-webhook`, 600 por minuto por IP, configurable en `RateLimiting`.
- **Para la Tarea 11:** falta el índice de los pendientes (`ProcessedAtUtc IS NULL`), `MarkProcessed` y vincular el contacto a la cuenta. Llevan su propia migración.
- **Para la Tarea 18:**
  - `Microsoft.AspNetCore` tiene que quedar en `Warning`: en `Information`, el log "Request starting" mostraría el `hub.verify_token` del GET de Meta;
  - las cadenas de conexión no tienen que llevar `Include Error Detail`: el DETAIL de un 23505 mostraría el BSUID;
  - documentar las claves nuevas de `RateLimiting` y los secretos del webhook.

**Commits:** backend `feat: webhook de WhatsApp con firma, guardado y sin duplicados`; front `chore: el proxy de Vite reenvía /webhooks`.

### Tarea 9: El túnel

**Repo:** backend (AppHost). **Depende de:** 8. **Spec:** 16.

- [x] Agregar `Aspire.Hosting.DevTunnels` 13.5.4 a `Directory.Packages.props` y al AppHost.
- [x] En `AppHost.cs`, **solo si `DevTunnel:Enabled` es `true`** (apagado por defecto, así `aspire run` no le exige la CLI a quien no la usa):
  - agregar un dev tunnel con un `tunnelId` fijo;
  - exponer **únicamente el endpoint `https` de la Api**, con acceso anónimo en ese puerto.
  - La forma exacta de la API está en la [documentación de la integración](https://aspire.dev/integrations/devtools/dev-tunnels/); verificarla al implementar.
- [x] README: instalar la CLI, `devtunnel user login`, activar la opción en los user-secrets del AppHost, dónde ver la URL en el dashboard de Aspire y qué cargar en Meta.

**Hecha el 2026-09-23** (`2d7a7e8`). Build con 0 advertencias y 964/964. Prueba de humo con `aspire run` y el túnel apagado: la Api arranca en *Healthy*, se aplica la migración `WhatsAppMessages`, `/webhooks/whatsapp` responde 404 con el Warning de los secretos, y no aparece ningún túnel. Revisión adversarial: se confirmó un hallazgo menor del README, que quedó corregido. Lo que se hizo distinto del plan:

- **Región fija (Brasil Sur).** Sin región, la integración elige una por ping y puede crear el túnel de nuevo en otra, con otra URL.
- **Sin `tunnelId` fijo en el código.** El id es parte de la dirección pública y es único entre todos los usuarios de Dev Tunnels: uno escrito en el repo chocaría con otra copia de la plantilla y sería fácil de adivinar. Sin `DevTunnel:TunnelId`, la integración usa uno por máquina, derivado de la ruta del AppHost, que no cambia entre arranques.
- **La URL pública** es la del recurso `tunnel-api-https` en el dashboard. El enlace "Inspect" es el inspector del túnel y no se carga en Meta.
- **README, sección "WhatsApp en local":** los tres secretos con un comando que no muestra el valor en PowerShell 5.1 ni en 7, la CLI y su login, el túnel, qué cargar en Meta y apagarlo al terminar.
- *Para confirmar en la prueba del Hito 2:* qué hace el AppHost si `devtunnel` no tiene la sesión iniciada, y la forma exacta de la URL.

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

**Hecha el 2026-09-23, con éxito.** Lo que salió en la prueba:

- **Preparación:**
  - el usuario instaló `devtunnel` e inició sesión con su cuenta de Microsoft;
  - cargó `WhatsApp:AppSecret` y `WhatsApp:VerifyToken` con el comando seguro del README, y prendió `DevTunnel:Enabled`;
  - después de `winget` hubo que refrescar el `PATH` de la terminal, y el AppHost también lo necesita.
- **El túnel:** quedó en Brasil Sur, con la URL `tunnel-api-https` del dashboard. Desde afuera, `/health` respondió `200` y el webhook, `403` con una palabra equivocada: responde y no pide login.
- **En Meta:**
  - la app **Servicios Ya** y la de prueba **Servicios Ya Rodri** (`1987276235245941`) tenían cargada la misma URL de callback, de un backend de Rodri en AWS;
  - el usuario cambió solo la de su app al túnel; la de Rodri no se tocó;
  - "Verificar y guardar" funcionó: el log dice "Meta verified the WhatsApp webhook".
- **Probar `messages`:** lo hicieron el usuario desde el panel y el agente con la herramienta de Meta. Los dos eventos llegaron con la firma válida, el lector ignoró el ejemplo (otro `phone_number_id`), el comando se procesó y la Api respondió `200`. No se guardó nada, como corresponde.
- **`aspire stop`:** la Api quedó apagada, pero la URL del túnel sigue respondiendo `200` con el cuerpo vacío. Lo contesta el servidor de Dev Tunnels, porque la dirección sigue reservada. Meta da por entregado lo que llegue mientras tanto y no lo reintenta. Quedó en el README.
- *Para el Hito 3:* revisar si las dos apps están suscriptas a la misma cuenta de WhatsApp. Si es así, los mensajes al bot les llegarían a los dos backends y podrían contestar dos bots.

### Hito 3: el chat

### Tarea 10: El enlace de ingreso

**Repo:** backend. **Depende de:** 2 y 3. **Spec:** 5, 6.4 y 11.

- [x] Tests primero:
  - dominio: vence, se consume una sola vez y emitir otro invalida el anterior;
  - integración, con el enlace emitido desde un endpoint de `TestFeatures`:
    - el preview no lo consume;
    - el redeem crea la cookie, y después `/connect/authorize` emite el código;
    - vencido, usado, inventado e invalidado responden **el mismo** `400`;
    - una cuenta deshabilitada responde `403` recién después de un enlace válido;
    - queda auditado como `WhatsAppLink`.
- [x] `LoginLink`, `LoginLinkErrors`, el repositorio, la configuración y la migración `LoginLinks`.
- [x] `ISecureTokenGenerator` (32 bytes con `RandomNumberGenerator`, en base64url) y el SHA-256 del token.
- [x] `LoginLinkIssuer` (lo usa el bot): uno por minuto por cuenta y 5 cada 15 minutos.
- [x] `PreviewLoginLinkQuery`, `RedeemLoginLinkCommand` (con `IPersistChangesOnFailure`, por la auditoría) y sus endpoints con `LoginVerifyPolicy`.

**Hecha el 2026-09-23.** Suite completa 1026/1026 y build con 0 advertencias. La revisión adversarial (dos vueltas, 12 hallazgos) confirmó uno, que era un test y quedó corregido. Además, la sesión principal sumó un arreglo que había salido en la revisión: `RevokeSessionsAsync` (desactivar, eliminar y, en la Tarea 15, desvincular) invalida los enlaces pendientes, con su test. Sin eso, un enlace mandado antes del corte volvía a servir si la cuenta se reactivaba dentro de sus 10 minutos. Lo que se hizo distinto del plan, o además:

- **El preview muestra el nombre, o el correo si la cuenta no tiene nombre**, y el número enmascarado. El número completo nunca sale.
- **Auditoría:**
  - los canjes fallidos de un enlace que existe (vencido, usado, invalidado) se auditan contra su cuenta, aunque la respuesta HTTP sea el mismo 400 que la de uno inventado;
  - uno inventado no deja fila, porque no se puede atar a nadie; queda en el log del caso de uso, solo con el código de error.
- **Un token con la forma equivocada** (no son 43 caracteres base64url) responde un 400 de validación (`errors.token`, "El enlace está incompleto…"). La regla de "el mismo 400" vale para los tokens bien formados.
- **El canje correcto** reinicia los intentos fallidos, como el código.
- **La duración está fija en el dominio** (10 minutos, `LoginLink.Lifetime`); los límites son configurables en `Authentication:LoginLink`.
- **Piezas nuevas:**
  - `IPublicOrigin` lee `Authentication:Issuer`, y el arnés lo fija en `https://localhost/`;
  - `ISecureTokenGenerator` usa SHA-256 en hex mayúscula, como los códigos;
  - el repositorio suma `FindUserIdAsync`: el canje toma el lock de la cuenta antes de releer el enlace.
- **Las rutas de los enlaces se mapean siempre.** Sin bot no hay enlaces, así que cualquier token responde 400.
- **Para la Tarea 11:**
  - el emisor no mira si la cuenta existe o está activa: lo decide el bot antes, según la tabla del spec 8;
  - su `TooManyRequests` trae `retryAfter` para el "Esperá un momento…";
  - si el webhook está prendido, conviene que la Api no arranque sin `Authentication:Issuer`: hoy el emisor falla recién al usarse.

**Commit:** `feat: enlace de ingreso de un solo uso`

### Tarea 11: El procesador y el bot

**Repo:** backend. **Depende de:** 8 y 10. **Spec:** 7 y 8. **Tablero:** "WhatsApp · Conversaciones con el bot" (los textos).

- [x] Tests primero, de la **tabla de la sección 8, un test por fila** (`Application.UnitTests/Features/WhatsApp/HandleInboundMessageTests.cs`, con dobles):
  1. cuenta activa: botón **Entrar** con enlace nuevo; el contacto queda vinculado y el número verificado;
  2. deshabilitada, bloqueada o borrada;
  3. sin cuenta con registro abierto: la pregunta con los dos botones;
  4. `CREATE_ACCOUNT` crea la cuenta y manda el enlace; repetido no duplica;
  5. `HAVE_ACCOUNT`;
  6. sin cuenta con registro solo por invitación;
  7. `WANT_TO_ENTER`;
  8. pidió un enlace hace menos de un minuto;
  9. foto o audio responde como un texto.
- [x] `HandleInboundMessageCommand` y `BotReply`, con los textos en `Bot.resx` y `Bot.en.resx`, copiados del tablero. Sumar el archivo al control de paridad si no lo toma solo.
- [x] `WhatsAppInboundProcessor` (`BackgroundService`):
  - se despierta con una señal y además revisa la tabla cada 30 segundos;
  - toma los mensajes con `FOR UPDATE SKIP LOCKED`, con un lock por contacto;
  - si hay varios mensajes pendientes del mismo contacto, les da **una sola** respuesta (Meta limita a un mensaje cada 6 segundos por persona);
  - los mensajes de más de 24 horas se marcan procesados sin responder;
  - expone `ProcessPendingAsync()` para los tests, y en los tests el ciclo en segundo plano está apagado.
- [x] El sender guarda cada mensaje saliente con su `WaMessageId` y su resumen seguro.
- [x] Tests de integración: un webhook firmado, después `ProcessPendingAsync`, después lo que capturó `factory.WhatsApp`, para las filas 1, 3, 4, 6 y 8. Además: un webhook repetido da una sola respuesta, y un mensaje de hace más de 24 horas no tiene respuesta.

**Hecha el 2026-09-23** (`bc9116a`). Suite completa 1102/1102 y build con 0 advertencias. La revisión adversarial tuvo dos vueltas: se confirmaron 3 hallazgos de 14 y quedaron corregidos.
- **Faltaba un test del lock real del contacto contra Postgres.** Ahora uno verifica que otra instancia lo saltea sin esperar, y que mientras tanto los mensajes siguen entrando.
- **El test de "primero el contacto vinculado" no probaba el orden.** Ahora hay otra cuenta con el mismo número.
- **Una cuenta borrada recibía el "deshabilitada" en español aunque fuera en inglés.** Se agregó `IIdentityService.FindDeletedByPhoneAsync`.

Lo que se hizo distinto del plan, o además:

- **El lock por contacto es `FOR NO KEY UPDATE SKIP LOCKED`, no `FOR UPDATE`.** Saltea igual lo que tiene otra instancia, pero no traba las claves foráneas: mientras el bot procesa un contacto, el webhook puede seguir guardándole mensajes y el sender sus salientes. El webhook que tiene que actualizar el contacto (su nombre, su último mensaje) sí espera.
- **La respuesta se encola antes de confirmar**, como en `RequestWhatsAppLoginCode`. Si la cola no la toma, el handler lanza una excepción y no se guarda nada: ni la cuenta, ni el enlace, ni los procesados. El procesador lo registra y lo reintenta en la vuelta siguiente. Riesgo aceptado: si la confirmación falla después de encolar, llega un enlace que no sirve, y la vuelta siguiente manda otro que sí.
- **Qué mensaje decide la respuesta:** el más nuevo dentro de las 24 horas, y un botón gana sobre un texto. No deciden los avisos de WhatsApp ni "No pedí un código": se marcan procesados sin respuesta. Una foto, un audio o un sticker responden como un texto.
- **Un contacto sin número válido** (llega solo con el BSUID) se marca procesado sin respuesta, con un Warning sin datos personales.
- **La cuenta se busca primero por el contacto vinculado y después por el número.** Si la cuenta ya tenía otro contacto (el mismo número con otro BSUID), el nuevo toma el vínculo y el viejo lo suelta.
- **Textos:**
  - el nombre del sistema sale de `Email:AppName`, con una abstracción nueva, `IAppName` (`Application/Abstractions/Branding`);
  - el saludo usa el nombre de la cuenta y, si no tiene, el del perfil de WhatsApp;
  - se sumaron variantes sin nombre que no están en el tablero;
  - los minutos salen de `LoginLink.Lifetime`;
  - el inglés es traducción propia.
- **Los ids de los botones están en `BotButtons`**, que es pública: la Tarea 15 usa `WANT_TO_ENTER`.
- **Los salientes:**
  - `WhatsAppMessageKind` suma `Interactive` y `Template`, que se guardan como texto, sin cambio de esquema;
  - el contacto se busca por el `wa_id` y, si no aparece, por el vinculado a la cuenta del número;
  - un fallo al guardar no reenvía el mensaje;
  - un estado de Meta que llega antes de que el sender guarde el saliente se ignora, y el mensaje toma el siguiente (`delivered`, `read`).
- **Configuración nueva** en `WhatsAppOptions`:
  - `InboundPollSeconds`, que vale 30 y va de 1 a 3600;
  - `ProcessInboundInBackground`, que por defecto es true y en `ApiFactory` va en false.

  Con el webhook prendido, la Api no arranca sin `Authentication:Issuer`.
- **La migración `WhatsAppInboundProcessing`** crea solo el índice filtrado de los entrantes pendientes (`ContactId`, `OccurredAtUtc`).
- **Para las Tareas 13 y 15:** al desvincular o cambiar el número de una cuenta, también hay que soltar su `WhatsAppContact` (`UnlinkUser`). Si no, el bot sigue encontrando la cuenta por el contacto vinculado y le manda enlaces a ese chat.
- **Para la Tarea 18:** documentar las dos opciones nuevas, el procesador y que `Authentication:Issuer` es obligatorio con el webhook prendido.

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

- [x] Tests primero:
  - lee el token del fragmento y lo borra de la barra (`history.replaceState`);
  - el preview muestra el pantallazo 1;
  - Continuar hace el redeem y después `signinRedirect`;
  - un token inválido muestra el 3;
  - una cuenta deshabilitada muestra el 4.
- [x] `LoginLinkPage` y la ruta `/ingresar` en `routes.tsx`, dentro de `AuthLayout` y sin `lazy`, como las otras pantallas del ingreso.
- [x] Textos en `auth.json`, en español y en inglés.
- [x] **Abrir el tablero y comparar.**

**Hecha el 2026-09-23** (front `fca504c`). Front: build, lint y 435/435 (29 de `LoginLinkPage`). Se hizo en paralelo con la Tarea 11. La revisión adversarial tuvo dos vueltas: se confirmaron 12 hallazgos y quedaron corregidos. Lo que se hizo distinto del plan, o además:

- **El token se lee en el primer render**, no en un efecto. Así sobrevive al doble montaje de `StrictMode`: el segundo montaje ya encuentra la barra limpia. Después vive solo en memoria.
- **La vista previa no gasta el enlace; Continuar sí.** El canje sale una sola vez aunque se toque dos veces, y después viene el `signinRedirect` de siempre.
- **Qué hace la pantalla con cada error** (`loginLinkFailureOf` en `errors.ts`):
  - `Auth.LoginLink.Invalid`, o un `Validation.Failed` por un token cortado, muestran "Este enlace ya no sirve";
  - `Auth.Account.Disabled` y también `Auth.Account.LockedOut` muestran "No podés entrar", cada uno con su texto (el de `login.errors`). El bloqueo no estaba en el plan, pero llega en el canje con el enlace ya gastado: esperar no alcanza, hace falta otro;
  - lo demás (el límite de pedidos, la red o un 500) se puede reintentar, y el botón respeta `retryAfter`.
- **"Volver a WhatsApp" aparece solo si hay número del bot** (`whatsappNumber` de los métodos de ingreso).
- **El foco va al título de cada estado nuevo**, así el lector de pantalla anuncia el cambio.
- **`shared/ui` sumó** `ClockIcon`, `BanIcon` y la opción `decorative` de `Spinner`.
- **Quedan pendientes:**
  - pasar esas tres piezas al Artifact de la biblioteca "ArquitecturaBase UI";
  - mirar la pantalla en ancho de celular;
  - el `Spinner` mide 20 px y en el tablero, 28;
  - documentarlo en la Tarea 18.

**Commit (front):** `feat: /ingresar, la entrada con el enlace del chat`

### Prueba manual del Hito 3 (nivel 3)

Antes: la política de privacidad publicada y la app de Meta publicada.

1. Activar el túnel y hacer `aspire run`. Abrir el chat con el número de prueba en **WhatsApp Web, en la PC**.
2. Desde tu celular (con cuenta), escribir "Hola". Llega **Entrar**; tocarlo en WhatsApp Web y pasar por `/ingresar` y Continuar hasta el inicio. Revisar de nuevo lo del 9, ahora en la respuesta del bot.
3. Con el registro abierto, desde el segundo celular (sin cuenta): **Crear cuenta**, después **Entrar**. Probar también **Ya tengo cuenta**.
4. Con el registro solo por invitación, repetir desde un número sin cuenta: llega el mensaje de acceso por invitación.
5. `aspire stop`.

**Hecha el 2026-09-23, con éxito**, con el código hasta la Tarea 13 (`efff4cd`).

Antes hubo que preparar Meta:
- **Publicar la app:** política en GitHub Pages, ícono, categoría "Negocios" y la eliminación de datos en `#eliminar-datos`.
- **Suscribir la app a la cuenta de WhatsApp** con `POST /1658125822339116/subscribed_apps`. No lo estaba: sin eso, los mensajes reales no llegan al webhook aunque la app esté en Live. Hasta ese momento solo figuraba una app interna de Meta.
- **Confirmar que la app "Servicios Ya Rodri" no está suscripta a nuestra cuenta de WhatsApp.**

Lo que salió en la prueba:
- **Con cuenta:** "Hola" desde el celular. El bot respondió en unos 8 segundos con "Hola, Eze Ellena…" y **Entrar**, y Meta confirmó la entrega. El enlace se tocó en WhatsApp Web y abrió `/ingresar`; con Continuar se entró. Quedaron el contacto vinculado, el enlace usado y la auditoría `WhatsAppLink`. Meta aceptó un enlace a `https://localhost:5173` en el botón.
- **Sin cuenta:** se borró a mano la cuenta del mismo número, con un SQL que sirve de base para el pendiente del borrado real. El bot preguntó "¿Querés crear una?". **Crear cuenta** creó la cuenta sin correo, con el número verificado y el nombre del perfil de WhatsApp ("Eze Ellena"), y mandó **Entrar**, con el que se entró.
- **No se probaron a mano** "Ya tengo cuenta" ni el aviso del registro por invitación (pasos 3 y 4). Los dos están cubiertos por los tests de la Tarea 11, y el de invitación se vuelve a ver en la prueba del Hito 4.

### Hito 4: perfil y administración

### Tarea 13: El perfil, backend

**Repo:** backend. **Depende de:** 3, 6 y 8. **Spec:** 12.

- [x] Tests primero:
  - vincular manda el código con `VerifyDestination`, y con el código correcto el número queda vinculado y verificado, y su contacto de WhatsApp apunta a la cuenta;
  - un número de otra cuenta responde `409` **solo después** de un código correcto;
  - agregar un correo funciona igual (`409 Users.User.AlreadyExists` después del código);
  - desvincular sin otro medio responde `409 Users.User.LastLoginMethod`;
  - con un correo verificado o con Google, desvincula y también desvincula el contacto.
- [x] Los comandos de la estructura de archivos y sus endpoints en `MeEndpoint`.
- [x] `UserGuards.HasOtherLoginMethod`, con su test de unidad.
- [x] **El código para vincular el número usa la misma plantilla:** tiene que respetar el tope diario. Hoy el chequeo vive en `RequestWhatsAppLoginCodeCommandHandler`: pasarlo a `LoginCodeIssuer` o a un helper común.
- [x] **Decidir si los códigos de `VerifyDestination` se buscan por cuenta.** Desde la Tarea 3, `ListActiveAsync` y `GetLatestAsync` filtran por destino y propósito. Si otra cuenta pide un código para el mismo número, invalida el de la dueña, que tiene que pedir otro. Es la misma molestia que ya permiten los límites compartidos por destino. Si se quiere evitar, sumar `RequestedByUserId` al filtro de esas dos consultas cuando el propósito es `VerifyDestination`.

**Hecha el 2026-09-23** (`efff4cd`). Suite completa 1165/1165 y build con 0 advertencias. La revisión adversarial tuvo dos vueltas: se confirmaron 7 hallazgos de 17 y todos quedaron corregidos. Los más importantes eran carreras entre el bot y el cambio de número:
- reemplazar el número no invalidaba los enlaces ya mandados al chat del número anterior;
- el bot podía leer la cuenta, esperar y, al despertar, mandar un enlace o vincular el chat de un número que la cuenta acababa de soltar;
- el perfil y el bot tomaban los locks en orden inverso, lo que en Postgres terminaba en un interbloqueo y un 500.

**Decisiones de la sesión principal** (resuelven lo que el plan dejó abierto):
- **Sin enumeración antes del código.** Pedir el código responde el mismo 202 y lo manda igual, sea el número o el correo libre, de otra cuenta o de una borrada. El 409 (`Users.Phone.AlreadyExists` o `Users.User.AlreadyExists`) sale solo después de un código correcto, y el código queda gastado.
- **Confirmar no es un ingreso.** Un código equivocado responde los mismos `Auth.LoginCode.*` y cuenta el intento del código, pero no suma a los fallos de la cuenta ni deja `LoginAudit`.
- **Se reemplaza.** Si la cuenta ya tenía otro número u otro correo, el nuevo lo reemplaza, verificado.
- **Desvincular el propio número** exige otro medio de ingreso: un correo verificado o Google. Suelta el chat de WhatsApp e invalida los enlaces pendientes, pero no cierra las sesiones: eso queda para el desvincular del admin, en la Tarea 15. Una cuenta sin número responde 204 igual.
- **Los códigos de `VerifyDestination` se buscan e invalidan por cuenta** (`RequestedByUserId`): otra cuenta que pide un código para el mismo número no invalida el de la dueña. Los límites siguen por destino y compartidos.
- **El tope diario de WhatsApp pasó a `LoginCodeIssuer`** y vale para todo código por WhatsApp. `AllowedCountries` también vale para vincular.

Lo que se hizo distinto del plan, o además:
- **Piezas compartidas nuevas:**
  - `DestinationCodeVerifier`: el lock del destino, el último código de la cuenta y `VerifyFor`;
  - `WhatsAppContactLinker`, en `Features/WhatsApp`: era el `LinkAsync` privado del bot, y ahora es la única pieza que vincula o suelta contactos;
  - `PhoneNumberChange`: toma los locks en el orden del bot (primero los contactos, después la cuenta) e invalida los enlaces;
  - `UniqueViolations`: traduce el 23505 y ahora lo usa también `UnitOfWork`.
- **Choque del índice único → 409, no 500.** `IdentityService.SetPhoneAsync` y `SetEmailAsync` traducen el 23505 a `UniqueConstraintViolationException` y devuelven la cuenta a como estaba.
- **El bot ahora revalida la cuenta con su lock.** `FindAccountAsync` la devuelve con el lock de enlaces tomado. Si llegó por el número, la vuelve a buscar. Si el número ya no es de esa cuenta, contesta como a un número sin cuenta.
- **El contacto anterior de la cuenta se toma con `NOWAIT`.** Si lo tiene el perfil, el bot falla sin guardar nada, y el mensaje queda para la vuelta siguiente.
- **Con WhatsApp apagado solo desaparece** `POST /api/me/whatsapp/code`. PUT y DELETE siguen mapeados, porque no mandan nada.
- **El correo para agregar una dirección** usa la plantilla del código con textos propios (`VerifyEmail.*` en `Emails.resx`). El asunto sigue empezando con el código.
- **`Users.User.LastLoginMethod`** tiene el texto del tablero: "Es tu único medio de ingreso: para desvincularlo, primero agregá un correo."
- **Para la Tarea 14:** en el diálogo de vincular, el error `Users.Phone.AlreadyExists` se muestra con el texto del tablero: "Este número ya está vinculado a otra cuenta. Si es tuyo, pedile a un administrador que lo libere."
- **Para la política de privacidad:** con esto, "Cuándo te escribimos" deja de ser exacto, porque vincular manda un código a cualquier número de un país habilitado. Se actualiza junto con la Tarea 14 (ver "La política de privacidad: lo que falta").
- **Riesgo aceptado, poco probable:** si el bot procesa un mensaje del mismo contacto justo cuando la cuenta confirma ese número, y ese número estaba sin verificar, Postgres puede detectar un interbloqueo. Aborta uno de los dos: el confirmar respondería 500, o el procesador reintenta en la vuelta siguiente.

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

- [x] Tests primero de cada uno de los cinco puntos.
- [x] `LoginMethodsCard`, `LinkWhatsAppDialog`, `AddEmailDialog` y `UnlinkWhatsAppDialog`; separar `ProfilePage` en las dos superficies; el aviso en `DashboardPage`; `UserMenu` y `Sidebar` con el número.
- [x] Textos en `profile.json` y `common.json`, en los dos idiomas.
- [x] **Abrir el tablero y comparar.**

**Hecha el 2026-09-24** (backend `7fd7871`, front `236334f`). Backend: 1181/1181 y 0 advertencias. Front: build, lint y 516/516. La revisión adversarial tuvo dos vueltas: se confirmaron 12 hallazgos de 25 y todos quedaron corregidos. La mayoría eran de foco y accesibilidad (el foco caía en `<body>` al cerrar los diálogos, el paso del código no se anunciaba) y de tests que no probaban lo que decían. Lo que se hizo distinto del plan, o además:
- **Backend:** `/api/me` suma `formattedPhoneNumber` y `maskedPhoneNumber`, con `IPhoneNumberParser`. El front muestra siempre el número formateado (fila de WhatsApp, menú y barra lateral) y el enmascarado en la confirmación de desvincular. No hace falta una librería de teléfonos en el front.
- **Un solo `VerifyDestinationDialog`** con dos configuraciones (`whatsapp` y `email`) en lugar de `LinkWhatsAppDialog` y `AddEmailDialog`. El código se pide en el mismo diálogo, como el paso 3 del ingreso.
- **El 409 de otra cuenta** muestra el texto del tablero. Para el correo se usa uno análogo: "Este correo ya está en otra cuenta. Si es tuyo, pedile a un administrador que lo libere."
- **Movido a `shared`** sin cambiar su comportamiento, porque una feature no importa de otra: `PhoneField` y `OtpInput` (a `shared/ui`), `countries` (a `shared/lib`), los helpers de errores de código y número (`shared/api/codeErrors.ts`, con nombres nuevos) y `getLoginMethods` (`shared/api/loginMethods.ts`). Los textos compartidos pasaron de `auth.json` a `common.json`.
- **Nuevo en `shared/ui`:**
  - `Banner` (info y danger);
  - 5 íconos: Mail, Smartphone, Check, Info y AlertCircle;
  - los tokens `--color-success-50` y `--color-success-700`, para la insignia "Verificado".

  **Falta pasarlos a la biblioteca "ArquitecturaBase UI" del Artifact**, junto con lo pendiente de la Tarea 12.
- **Si desvincular responde 409** `Users.User.LastLoginMethod`, además del aviso se vuelve a pedir `/api/me`.
- **Para decidir y dibujar** (hoy quedaron así):
  - un correo o un número sin verificar se muestra sin insignia y sin acción; ¿se ofrece "Verificar"?
  - Idioma y Zona horaria conservan su ayuda, que el tablero no dibuja;
  - el número sale con guion ("+54 9 11 2345-6789"), que es el formato de libphonenumber, y el tablero lo dibuja con espacio;
  - el pie de los diálogos no tiene la banda gris del tablero, igual que los demás diálogos de la app;
  - los avisos de éxito dicen "Vinculamos tu WhatsApp.", "Agregamos tu correo." y "Desvinculamos tu WhatsApp.";
  - el aviso del inicio tiene 8 px de radio, contra 10 en el tablero, y lleva a Mi perfil en lugar de abrir el diálogo;
  - en celular, el botón de cada fila baja debajo del valor.
- **El `CLAUDE.md` del front no se tocó,** porque tiene cambios sin commitear de otra sesión. Queda para la Tarea 18: `/perfil`, `shared/api/codeErrors`, `shared/api/loginMethods` y `Banner`.
- **La política de privacidad se actualizó** con lo de esta tarea y la 13 (ver el repo `privacidad`).

**Commit (front):** `feat: medios de ingreso en el perfil`

### Tarea 15: La administración, backend

**Repo:** backend. **Depende de:** 2, 5, 11 y 13. **Spec:** 6.6 y 12.

- [x] Tests primero:
  - alta solo con número;
  - sin correo ni número, `Users.Identity.Required`;
  - número repetido, `409`;
  - invitación por WhatsApp sin consentimiento, `400`;
  - invitación por WhatsApp sin nombre, `400 Users.Invitation.NameRequired` (ver las decisiones al final);
  - la invitación por WhatsApp encola la plantilla con el nombre y el nombre del sistema; la de correo manda el correo nuevo;
  - `DELETE /api/users/{id}/whatsapp` desvincula, **cierra las sesiones** (el access token deja de valer) y desvincula el contacto;
  - reenviar la invitación funciona;
  - tocar **Quiero entrar** en la plantilla (`WANT_TO_ENTER`) manda el enlace (fila 7 del bot).
- [x] `UserInvitation` con su repositorio, configuración y la migración `UserInvitations`.
- [x] La plantilla de correo `Invitation.html` y sus textos en `Emails.resx` y `Emails.en.resx`.
- [x] `CreateUser` y `UpdateUser` con el contrato, más `UnlinkUserPhoneCommand` y `SendInvitationCommand`.
- [x] Errores nuevos: `Users.Phone.AlreadyExists`, `Users.Identity.Required`, `Users.Invitation.ConsentRequired`, `Users.Invitation.NameRequired`, `Users.User.LastLoginMethod` y `Auth.LoginLink.Invalid`, con los textos del spec.

**Hecha el 2026-09-24** (`8f20f43`). Suite completa 1269/1269 y build con 0 advertencias. La revisión adversarial tuvo dos vueltas: se confirmaron 8 hallazgos de 24, todos corregidos. El más importante: el alta respondía 500 en vez de 409 si el bot o Google creaban la misma cuenta a la vez. Decisiones de la sesión principal y desvíos:

- **Alta:** correo, número o los dos (`Users.Identity.Required` si no hay ninguno). El número sigue las reglas del ingreso. **Lo que carga el admin queda sin verificar**, también el correo: ahora entrar con el código del correo, o vincular Google, lo verifica.
- **Restaurar una cuenta borrada:** solo si todo lo que se cargó es de esa misma cuenta. Puede completar el correo o el número que falte, nunca reemplazarlos. Si el correo y el número son de cuentas distintas, 409 del que choca.
- **Invitación en el alta y reenvío** (`POST /api/users/{id}/invitation`):
  - por WhatsApp exige número, consentimiento (`Users.Invitation.ConsentRequired`) y nombre (`Users.Invitation.NameRequired`). Esos dos códigos también llegan en `errors` por campo (`invitation.consent`, `displayName`);
  - por correo exige correo;
  - con WhatsApp apagado, la opción WhatsApp responde 400;
  - entre invitaciones a la misma cuenta hay que esperar 60 segundos (429 `Users.Invitation.TooManyRequests` con `retryAfter`);
  - una cuenta desactivada responde 400 `Users.Invitation.UserInactive`, que es un código nuevo, y una borrada responde 404.
- **`UserInvitation` guarda el consentimiento que confirmó el admin** (quién y cuándo), no un opt-in de la persona. Guarda además el `WaMessageId` del saliente, que completa el sender. El detalle suma `lastInvitation { channel, sentAtUtc, deliveryStatus }`: Pending, Sent, Delivered, Read o Failed, y null para el correo. **Failed** incluye:
  - que la cola no la tome;
  - un rechazo inmediato de Meta, como el 132001 mientras la plantilla está en revisión, que se registra con el comando nuevo `RecordUnsentWhatsAppMessage`;
  - el estado `failed` del webhook, como el 131049 del límite de marketing.
- **Si falla el envío, la cuenta igual queda creada:** la invitación queda como no enviada y se puede reenviar. Riesgo aceptado, como en el bot: la invitación se encola antes del commit.
- **Locks:** alta y edición toman primero los locks de destino (correo, después número). La invitación usa un lock propio (`user-invitation:{userId}`), así el sender no la busca antes del commit.
- **Editar:** `email` y `phone` opcionales; ausente, null o vacío es "no cambia". Si cambia el número, se suelta el contacto anterior y se anulan los enlaces, pero **no** se cierran las sesiones. Mandar de vuelta el mismo número de un país no habilitado no es un error.
- **`DELETE /api/users/{id}/whatsapp`** saca el número, suelta el contacto y **cierra las sesiones**. El admin no puede dejarse a sí mismo sin medio de ingreso (`UserGuards.EnsurePhoneCanBeUnlinkedAsync`).
- **Listado y detalle** suman `formattedPhoneNumber`.
- **El correo de invitación** (`Invitation.html`) usa los textos del panel 3 del tablero de mensajes, y su pie es el del tablero.
- **Fuera de Development y Testing, la Api no arranca sin `Authentication:Issuer`** (lo necesita el botón del correo). Está en el README.
- **`WhatsApp:Templates:Invitation`** (por defecto `invitacion_acceso`) se valida al arrancar.
- **Para la Tarea 16:** la forma del teléfono es `{ country, number }`; los errores por campo están arriba; el reenvío responde 429 con `retryAfter`; `lastInvitation.deliveryStatus` alcanza para mostrar "no llegó".
- **Para la política de privacidad** hay que sumar:
  - el caso de la invitación en "Cuándo te escribimos";
  - qué se guarda del consentimiento y de la invitación;
  - lo que carga el admin, que queda "Sin verificar";
  - desvincular desde el admin, que cierra las sesiones.

  Además, hay que sumar `UserInvitations` al script de borrado y a los plazos pendientes.
- **Para la Tarea 18:** documentar en CLAUDE.md todo lo de arriba (datos sin verificar, restaurar, locks, códigos nuevos y la opción de la plantilla).

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

- [x] Tests primero de cada punto y de cada error.
- [x] `columns.tsx`, `UserFormDialog` (alta y edición), `UnlinkUserWhatsAppDialog`, `users.ts` y `errors.ts`.
- [x] Textos en `users.json`, en los dos idiomas.
- [x] **Abrir el tablero y comparar.**

**Hecha el 2026-09-24** (front `5df7a8e`). Build, lint y 593/593. La revisión adversarial tuvo dos vueltas: se confirmaron 11 hallazgos de 18 y todos quedaron corregidos. Los más importantes:
- el foco caía en `<body>` al cerrar la edición;
- con WhatsApp apagado, el alta vacía pedía un número que no estaba en pantalla;
- la explicación de "Sin verificar" solo la veía quien usa mouse.

Lo que se hizo distinto del plan, o además:
- **Dos diálogos en lugar de uno:** `UserFormDialog` para el alta y `UserEditDialog` para la edición, que reemplaza a `UserRolesDialog`, con un `RolesField` compartido.
- **Solo lo dibujado.** El estado de la invitación (`lastInvitation`) y el reenvío **no tienen UI**: el tipo está en `users.ts`, pero falta dibujarlos en el tablero.
- **Editar:** "Único medio de ingreso" se decide por `email === null`, porque el detalle no trae Google y una cuenta con Google siempre tiene su correo. El nombre ya no muestra la ayuda "Opcional…", como en el tablero.
- **Alta:**
  - la casilla de invitación arranca sin marcar, y el canal se elige solo si hay uno posible;
  - el consentimiento usa el primer nombre ("Laura");
  - con WhatsApp apagado, el subtítulo vuelve al texto viejo.
- **Textos:** "Ya hay una cuenta con ese correo." pasó a "Ya existe una cuenta con ese correo.", como el tablero. El buscador pasó de 240 a 320 px.
- **Nuevo en `shared/ui`:** `RadioGroupField`, `VerificationBadge` (el perfil también lo usa), `Banner` con tono `warning`, `CheckboxField` con `error`, `AlertTriangleIcon` y los tokens `--color-warning-50` y `--color-warning-700`. **Falta pasarlos a la biblioteca "ArquitecturaBase UI"**, con lo de las Tareas 12 y 14.
- **Para decidir** (hoy quedaron así):
  - ¿el alta preselecciona el rol User, como dibuja el tablero? Hoy muestra "Sin roles" y el backend asigna User;
  - ¿el buscador lleva la lupa y un nombre accesible propio ("Buscar usuarios")?
  - ¿la casilla de invitación arranca marcada, como la dibuja el tablero?
  - ¿la edición recupera la ayuda del nombre?
- **El `CLAUDE.md` del front no se tocó** (tiene cambios de otra sesión). Queda para la Tarea 18.

**Commit (front):** `feat: alta con número e invitaciones en usuarios`

### Prueba manual del Hito 4

Antes: la plantilla `invitacion_acceso` aprobada.

1. Con el registro solo por invitación, dar de alta el segundo celular e invitarlo por WhatsApp. Llega la invitación; tocar **Quiero entrar**, después **Entrar**, y queda adentro.
2. Invitar por correo a una dirección tuya: llega el correo, **Ingresar** lleva a `/login` y se entra con el código.
3. Desde un perfil con correo, vincular WhatsApp. Desvincularlo. Intentar desvincular el único medio de una cuenta que solo tiene WhatsApp: no deja.
4. Desde el admin, desvincular el WhatsApp de alguien con la sesión abierta en otro navegador: la sesión se corta.
5. `aspire stop`.

**Hecha el 2026-09-24, con éxito**, con el código hasta la Tarea 16 y la plantilla `invitacion_acceso` aprobada (Marketing). Lo que salió:
- **Administrador inicial:** entrar con `Seed:AdminEmail` creó la cuenta con el rol Admin y el correo verificado. El registro se pasó a "Solo por invitación" desde Ajustes.
- **Invitación por WhatsApp:**
  - la persona se dio de alta con correo y número, los dos sin verificar;
  - Meta entregó la plantilla, y la invitación guardó su `WaMessageId`. El detalle la muestra "entregada";
  - "Quiero entrar" (`WANT_TO_ENTER`) trajo **Entrar** en 2 segundos, verificó el número y vinculó el chat;
  - con el enlace se entró desde una ventana de incógnito: `WhatsAppLink`.
- **Invitación por correo:** llegó el correo, **Ingresar** llevó a `/login`, se entró con el código y el correo quedó verificado.
- **Desde el admin:**
  - desvincular el WhatsApp de una cuenta con la sesión abierta revocó todos sus tokens;
  - sacar y volver a cargar el número dejó el número "Sin verificar", y entrar con un código por WhatsApp lo verificó.
- **Registro por invitación:** con el número sin cuenta, los códigos pedidos en `/login` no se mandaron. La respuesta fue la misma de siempre, a propósito.
- **No se probó a mano** vincular y desvincular el WhatsApp desde Mi perfil. Lo cubren los tests de las Tareas 13 y 14.
- **Comentarios del usuario:**
  - el diálogo de Editar usuario le resultó "bastante feo": hay que rediseñarlo en el tablero;
  - propuso entrar con WhatsApp sin copiar un código (ver la conversación del 2026-09-24).

### Cierre

### Tarea 17: La retención de los mensajes

**Repo:** backend. **Depende de:** 11. **Spec:** 6.5.

- [x] Test primero, con `FakeTimeProvider`: los mensajes con más de `WhatsApp:MessageRetentionDays` días (90 por defecto) quedan sin texto; los contactos no se tocan.
- [x] `WhatsAppMessageRetentionService`, que corre una vez por día. `WhatsAppMessage` **no** es `IAuditable` ni `ISoftDeletable`, así que puede usar `ExecuteUpdate` (regla de `CLAUDE.md`).

**Hecha el 2026-09-24** (`d5316df`), en paralelo con la Tarea 14. Suite completa 1181/1181 y build con 0 advertencias. La revisión adversarial tuvo dos vueltas: se confirmaron 3 hallazgos de 10, todos de tests, y quedaron corregidos. Lo que se hizo distinto del plan, o además:
- **Sin migración:** `Body` ya admitía null. Se vacía solo el texto: la fila queda con su fecha, dirección, tipo, estado, botón y hora de procesado, que es lo que promete la política. Los contactos no se tocan.
- **Corre aunque WhatsApp esté apagado,** porque la tabla puede tener mensajes de antes. Se registra fuera del `if` de WhatsApp, con sus propias opciones (`WhatsAppMessageRetentionOptions`, sección `WhatsApp`), validadas al arrancar: `MessageRetentionDays` va de 1 a 3650.
- **Cómo corre:** `ExecuteUpdate` directo sobre `ApplicationDbContext`, desde Infrastructure, como `SystemSettingsReader`. No hay una decisión de negocio más allá de la fecha de corte. Arranca al iniciar y después una vez por día, con `PeriodicTimer` y `TimeProvider`. Un error se registra y se reintenta al día siguiente.
- **Opción nueva: `WhatsApp:ApplyMessageRetentionInBackground`,** true por defecto; `ApiFactory` y `WhatsAppLogPrivacyTests` la ponen en false. `appsettings.json` muestra `MessageRetentionDays: 90`. **Cambiar ese número exige cambiar antes la política de privacidad.**
- **Pendientes:**
  - un índice parcial sobre `OccurredAtUtc` si la tabla crece;
  - los tests viejos de "la Api no arranca" (`WhatsAppRegistrationTests` y el de Google en `ExternalLoginTests`) tienen una carrera latente con `WebApplicationFactory`; conviene pasarlos a `IStartupValidator`, como el nuevo;
  - documentar las dos opciones en la Tarea 18.

**Commit:** `feat: los mensajes de WhatsApp se vacían a los 90 días`

### Tarea 18: Documentación y cierre

**Repos:** los dos.

- [x] `CLAUDE.md` del backend, una sección "WhatsApp" con las reglas que un agente tiene que saber:
  - la regla de oro (spec 5);
  - los códigos por destino;
  - los números con el 9;
  - la firma y los duplicados del webhook;
  - el bot sin estado;
  - los números enmascarados en los logs;
  - el túnel opcional;
  - los secretos;
  - `/webhooks` en `BackendPrefixes`.
- [x] `CLAUDE.md` del front: `/ingresar`, el selector del ingreso y los medios de ingreso del perfil.
- [x] README: la configuración de WhatsApp (qué va en user-secrets y qué en appsettings), el túnel y las plantillas.
- [x] `visual-baseline.md` del front: las filas nuevas del mapa Artifact → proyecto.
- [x] Este plan: la sección "Resultado de la ejecución", con el mismo formato que la Fase 5.

**Hecha el 2026-09-24.** Solo documentación: no se tocó una línea de código, así que no hay corrida de tests propia (ver "Verificación final"). Lo que se documentó, y dónde:

- **`CLAUDE.md` del backend** (`b1f701a`), una sección "WhatsApp" entre "Identidad" y "Administración": la regla de oro, el webhook y sus tres capas contra los duplicados, el bot sin estado, el procesador, el orden de los locks (primero los contactos, después la cuenta) con el porqué del 40P01, los códigos por destino y propósito, el 9 de los celulares argentinos, lo que carga un admin sin verificar, por qué el 409 sale recién después del código, la retención de 90 días atada a la política, la plantilla de invitación como Marketing, qué no se registra nunca (con las dos consecuencias no obvias: `Microsoft.AspNetCore` en `Warning` y las cadenas sin `Include Error Detail`), `/webhooks` en `BackendPrefixes`, y una tabla con las dieciséis opciones de la sección `WhatsApp`.
- **`README.md`** (`8cf7b77`): qué va en `appsettings` y qué en user-secrets, con todas las claves `WhatsApp:*` y sus valores por defecto; que `Authentication:Issuer` es obligatorio con el webhook prendido; y las dos plantillas de Meta, con el porqué de que la invitación haya quedado como Marketing. El túnel ya estaba documentado desde la Tarea 9 y no se tocó.
- **`CLAUDE.md` del front** (`e214c3d`), una sección "Ingreso con WhatsApp": `login-methods` como fuente de qué medios ofrecer, el selector de `/login`, `PhoneField` y el país, `codeErrors`, los errores que cortan el intento, el error de Google que cruza el redirect, `/ingresar` con el token en el fragmento leído en el primer render, `accountName` para las cuentas sin correo, las dos superficies de `/perfil` y el alta con invitación. Más dos subsecciones: lo que subió a `shared` y las piezas nuevas de `shared/ui` con sus tokens. Se sumaron también el bullet de `/ingresar` en "Rutas y sesión" y el stub de `scrollIntoView` del setup de los tests.
- **`visual-baseline.md`** (`ac42869`): las seis filas nuevas del mapa Artifact → proyecto, con lo que quedó distinto del tablero y lo que no se dibujó; `Banner` salió de la lista de piezas que faltaban en `shared/ui`, porque ya existe, y entró la lista al revés (lo que está en `shared/ui` y todavía no está en la biblioteca); la revisión del encabezado pasó al 2026-09-24 y se anotaron las dos cosas del lienzo que quedaron viejas.
- **Este plan:** las casillas de la Tarea 18 y la sección "Resultado de la ejecución", al final.

**Lo que quedó afuera a propósito:** el Artifact no se tocó (ni el panel 2 de WA-Mensajes ni la nota de la sección), la biblioteca ArquitecturaBase UI tampoco, y la política de privacidad vive en otro repo. Los tres están en los pendientes.

**Un arreglo de paso:** la nota de la Tarea 8 tenía dos **bytes nulos literales** adentro de sus comillas invertidas, al describir el texto que tiraba el lote. Con eso, git trataba a este archivo como binario y `git diff` no mostraba ni una línea de ningún cambio del plan. Ahora dicen `\0`.

**Commits:** backend `b1f701a`, `8cf7b77` y el de este resultado; front `e214c3d` y `ac42869`.

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
11. **La tabla `LoginCodes` no tiene retención.** Si crece, un índice parcial sobre `SentAtUtc` para `Channel = 'WhatsApp'` (lo usa el tope diario) y una tarea que borre los códigos viejos.
12. **La bandeja del admin** (la segunda entrega).

### La política de privacidad: lo que falta antes de que entre gente real

La política se escribió y se revisó el 2026-09-23, y se publica en GitHub Pages: repo `ezeellena2/privacidad`, `https://ezeellena2.github.io/privacidad/`. La sección para eliminar datos tiene el ancla `#eliminar-datos`. Datos de la política:
- servicio: "Servicios Ya";
- responsable: Ezequiel Ellena, Rosario, Santa Fe;
- edad mínima: 18 años.

Se publicó para pasar la app de Meta a Live y probar el Hito 3. Estas cosas quedaron pendientes, y **hay que resolverlas antes de que entre gente real**:

1. **Borrar de verdad a pedido.** La política promete que el resto de los datos "lo borramos a mano", y hoy no hay un procedimiento escrito. Falta un script probado que borre o anonimice:
   - la cuenta: `AspNetUsers`, `AspNetUserRoles` y `AspNetUserLogins`;
   - el ingreso: `LoginCodes`, `LoginLinks` y `LoginAudits`;
   - WhatsApp: `WhatsAppContacts` y `WhatsAppMessages`. La FK `Restrict` obliga a borrar primero los mensajes;
   - las sesiones: `OpenIddictAuthorizations` y `OpenIddictTokens`.

   Además, hay que vaciar las copias en "Enviados" de la cuenta de Gmail que manda los correos.
2. ~~**La Tarea 17:** la política dice que el texto de los mensajes se borra a los 90 días.~~ Hecha el 2026-09-24 (`d5316df`). Ojo: corre solo mientras la Api está prendida. Si el equipo está apagado, los textos vencidos quedan hasta el próximo arranque.
3. **Plazos para lo que hoy no tiene:** `LoginCodes`, `LoginLinks`, `LoginAudits` (guarda IP y user agent), los tokens de OpenIddict, `WhatsAppContacts` y las cuentas borradas. Se definen, se implementan y se actualiza la sección "Cuánto tiempo los guardamos".
4. **Con un abogado:**
   - el mecanismo de cada transferencia internacional (art. 12 de la Ley 25.326): Meta y Google en EE. UU., Dev Tunnels en Brasil y el hosting de producción;
   - si hay que inscribir la base en el Registro de la AAIP;
   - si alcanza con publicar la ciudad como domicilio;
   - los plazos que promete la política.
5. ~~**Actualizar la política cuando lleguen las Tareas 13 y 15.**~~ Hecho el 2026-09-24 en el repo `privacidad`: las Tareas 13, 14 y 17 en `bdfa5a2`, y la 15 en el commit siguiente. Esto era lo que faltaba de la 15:
   - la invitación de un administrador, con el registro del consentimiento;
   - lo que carga el admin, que queda "Sin verificar";
   - desvincular desde el admin, que cierra las sesiones.

   Lo que sigue es el texto original de este punto: Con la 13, vincular un número desde el perfil manda un código a cualquier número de un país habilitado, y "Cuándo te escribimos" hoy no lo cubre. Con la 15 entra la invitación por WhatsApp, con su consentimiento. También cambia "Tus derechos": la persona ya puede agregar su correo y vincular o desvincular WhatsApp sola.
6. **El nombre:** la web, los correos y el bot dicen "Arquitectura Base" (`Email:AppName`, `common.json`). La política lo aclara, pero WhatsApp pide que el opt-in nombre al negocio. Hay que alinearlo.
7. **Enlazar la política** desde `/login`, `/ingresar`, el pie de la web, el correo del código y el mensaje del bot antes de "Crear cuenta". Son pantallas: primero el tablero.
8. **Un canal humano en el bot:** "Contactá a un administrador" no dice cómo. Hay que sumar el correo a `Bot.resx` y `Bot.en.resx`.
9. **El webhook de la app de Meta** está suscripto a 10 campos y el sistema solo usa `messages`: dejar solo ese.
10. **El correo sale de una cuenta personal de Gmail,** sin acuerdo de encargado del tratamiento. Conviene un remitente propio.
11. **El código para vincular un número (Tarea 13)** lo puede pedir cualquier persona con cuenta, para cualquier número de un país habilitado, y llega con la misma plantilla que el de ingreso: quien lo recibe no sabe que es para vincular. Es la práctica habitual de los códigos (la persona que escribe el número es la que lo pide), pero hay que revisarla con el abogado y contra la política de opt-in de WhatsApp, y ver si conviene una plantilla propia.
12. **`LoginCodes` guarda qué cuenta pidió el código** para un número o un correo que puede ser de otra persona, y lo guarda sin plazo. Hay que sumarlo a los plazos del punto 3.
13. **No hay una lista de "no me invites"** (Tarea 15). El sistema no puede anotar ese pedido: a mano solo se puede sacar el número, y si alguien lo vuelve a cargar, se puede invitar de nuevo. Hay que evaluar una lista de bloqueo.
14. **El consentimiento de la invitación es la palabra del admin:** se guarda quién lo confirmó y cuándo, no una aceptación de la persona. Hay que revisarlo con el abogado y contra la política de opt-in de WhatsApp, que además pide nombrar al negocio (ver el punto 6).
15. **Un número o un correo mal cargados por el admin** mandan la invitación a otra persona, que puede entrar a esa cuenta con "Quiero entrar" o con un código. Es lo mismo que pasa con una invitación por correo mal escrita en cualquier sistema, y la política ya lo dice. Mitigación posible: que el alta con invitación muestre el número formateado para confirmarlo.
16. **Las invitaciones no tienen tope diario**, solo la espera de un minuto por cuenta, y cada plantilla de marketing se cobra.
17. **`UserInvitations` no tiene plazo** y se conserva aunque se borre la cuenta. Va al script de borrado (punto 1) junto con el saliente de la invitación, que queda guardado sin contacto si la persona nunca le escribió al bot.
18. **Cualquiera con `users.read` ve si la última invitación se "leyó".** Evaluar si alcanza con "entregada".

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

---

## Resultado de la ejecución

Ejecutado entre el 2026-09-22 y el 2026-09-24, tarea por tarea, con un commit por tarea en cada repo y una revisión adversarial por tarea. En los dos repos hubo otras sesiones trabajando a la vez (el rol en su propia pantalla y la biblioteca ArquitecturaBase UI): nada de lo suyo se tocó ni se agregó.

**Las 18 tareas están hechas.** De las cuatro pruebas manuales, tres salieron bien (Hitos 1, 2 y 3) y **falta la del Hito 4**, que espera que Meta apruebe la plantilla de invitación.

### Commits

| Tarea | Repo | Commit |
| --- | --- | --- |
| 1 | backend | `a8705c3` |
| 2 | backend | `8014891` |
| 3 | backend | `13f734a` |
| 4 | backend | `b8d3e83` |
| 5 | backend | `5800128` |
| 6 | backend | `73a3ae2` |
| 7 | front | `4d615ce` |
| 8 | los dos | backend `5e46f7a`; front `6359918` (el proxy de Vite) |
| 9 | backend | `2d7a7e8` |
| 10 | backend | `32b978f` |
| 11 | backend | `bc9116a` |
| 12 | front | `fca504c` |
| 13 | backend | `efff4cd` |
| 14 | los dos | backend `7fd7871`; front `236334f` |
| 15 | backend | `8f20f43` |
| 16 | front | `5df7a8e` |
| 17 | backend | `d5316df` |
| 18 | los dos | backend `b1f701a` (CLAUDE.md), `8cf7b77` (README) y el de este resultado; front `e214c3d` (CLAUDE.md) y `ac42869` (fundamento visual) |

Cada tarea sumó además su `docs: marcar la Tarea N del ingreso con WhatsApp` en el backend. Fuera de las tareas, entraron por la prueba manual del Hito 1: `1316e3c` (el envío sin el 9 para el número de prueba), `6b50d72` (WhatsApp prendido en desarrollo) y `a956b8a` (el bug de la Fase 4: el administrador inicial no podía crear su cuenta en `InviteOnly`). Y por la política de privacidad, `815217c`, `3890533` y `3b517f8`.

### Verificación final

| Repo | Comando | Resultado |
| --- | --- | --- |
| backend | `dotnet build ArquitecturaBase.slnx` | 0 advertencias, 0 errores |
| backend | `dotnet test` | **1269 / 1269** |
| front | `npm run build` | limpio |
| front | `npm run lint` | limpio |
| front | `npm run test` | **593 / 593** |

Son las de la última tarea que tocó código de cada repo: la Tarea 15 en el backend (`8f20f43`) y la Tarea 16 en el front (`5df7a8e`). **La Tarea 18 no las volvió a correr**: no tocó una sola línea de código, y al intentarlo el build falló por un `ArquitecturaBase.Api` corriendo que tenía tomados los DLL (el `aspire stop` de siempre). El suyo es el único commit de este plan que no trae su propia corrida.

La suite creció así, tarea por tarea: 578 → 602 → 631 → 639 → 717 → 803 → 964 → 1026 → 1102 → 1165 → 1181 → 1269 en el backend, y 399 → 400 → 435 → 516 → 593 en el front.

### Pruebas manuales

| Hito | Nivel | Estado |
| --- | --- | --- |
| 1: el código desde `/login` | sin túnel y sin publicar | **Hecha el 2026-09-23, con éxito** |
| 2: Meta verifica el webhook | con túnel, sin publicar | **Hecha el 2026-09-23, con éxito** |
| 3: el chat | con túnel y la app publicada | **Hecha el 2026-09-23, con éxito** |
| 4: perfil y administración | 1 y 3 | **Pendiente.** Necesita la plantilla `invitacion_acceso` aprobada, que quedó en revisión el 2026-09-23 |

Lo que cada una dejó está anotado en su sección. Lo más caro fue lo del Hito 1: Meta rechazó `+549…` con el error **131030** porque la lista de destinatarios del número de prueba guarda los celulares argentinos sin el 9. De ahí salió `WhatsApp:SendArgentineMobilesWithoutNine`, que toca solo el `to` y deja el número guardado con el 9.

### Desvíos del plan

Ninguno cambia el diseño. El detalle está en la nota de cada tarea; los que más cambian cómo se lee el código:

| Dónde | Qué dice el plan | Qué se hizo, y por qué |
| --- | --- | --- |
| Tarea 3 | `LoginCode.Email` pasa a `Destination`, un string | Un value object, **`LoginCodeDestination`**, con el canal y el valor. A propósito **no** redefine `ToString`: un destino que termine en un log no deja el número a la vista. |
| Tarea 5 | el interruptor de WhatsApp | Es **`WhatsApp:PhoneNumberId`**, como Google con su `ClientId`. Sin él, apagado y la app arranca; con él y sin token, la Api no arranca y el error trae el comando exacto de `dotnet user-secrets`. |
| Tarea 8 | "los mensajes se guardan con su lista de contactos" | **Cada mensaje trae su remitente.** Solo quien le escribe al bot pasa a ser contacto: una lista aparte guardaría gente que nunca escribió. |
| Tarea 11 | el procesador toma los pendientes con `FOR UPDATE SKIP LOCKED` | **`FOR NO KEY UPDATE SKIP LOCKED`.** Saltea igual lo que tiene otra instancia, pero no traba las claves foráneas: mientras el bot procesa un contacto, el webhook le sigue guardando mensajes. |
| Tarea 13 | "decidir si los códigos de `VerifyDestination` se buscan por cuenta" | **Sí, por cuenta.** Si no, otra cuenta que pide un código para el mismo número le invalida el suyo a la dueña. Los límites siguen por destino y compartidos, que es lo que evita el abuso. |
| Tarea 14 | `LinkWhatsAppDialog` y `AddEmailDialog` | **Un solo `VerifyDestinationDialog`** con dos configuraciones. Son el mismo recorrido: destino, código, listo. |
| Tarea 15 | "el alta puede cargar correo y número" | Además, **lo que carga un admin queda sin verificar**, el correo también, y **restaurar una cuenta borrada solo completa, nunca reemplaza**. Sin eso, un alta podía pisar la identidad de una cuenta que ya existía. |
| Tarea 16 | un `UserFormDialog` para alta y edición | **Dos diálogos.** La edición no crea identidades: muestra las que ya hay, con su estado de verificación y su desvincular. |
| Tarea 17 | la retención corre con WhatsApp prendido | **Corre siempre**, y sus opciones se validan al arrancar aunque WhatsApp esté apagado: la tabla puede tener mensajes de antes de apagarlo, y la política no distingue. |

### Lo que el plan no previó y se hizo igual

- **Las carreras entre el bot y el perfil** (Tarea 13). El bot y el cambio de número tomaban los locks en orden inverso y Postgres cortaba a uno con un 40P01, que salía como 500. De ahí salieron `PhoneNumberChange` (el orden canónico: contactos y después la cuenta), `WhatsAppContactLinker` (el único que vincula y suelta) y `DestinationCodeVerifier`. Ninguna de las tres estaba en el plan.
- **El bug del administrador inicial** (`a956b8a`), que es de la Fase 4 y lo encontró la prueba manual del Hito 1: en una base nueva en `InviteOnly` **nadie podía entrar, ni el admin**. Ahora `Seed:AdminEmail` crea su cuenta en cualquier modo, y la regla vive sola en `AccountCreationPolicy`.
- **`Users.Phone.Invalid` llega sin `errors`** (Tarea 6), porque lo arma el caso de uso y no la validación. El front lo ata al campo por el `code`, que es lo que hizo nacer `shared/api/codeErrors.ts`.
- **El error de Google que cruza el redirect** (Tarea 7). `/login?error=` llega sin `returnUrl`, así que sin el rodeo por `sessionStorage` la pantalla arrancaba el OIDC en vez de mostrar el mensaje. Es un hueco que venía de la Fase 4.
- **Que una cuenta pueda no tener correo rompía el caparazón** (Tarea 7): `initialOf(user.displayName ?? user.email)` se caía con `null` en la barra lateral y en el menú. De ahí `src/auth/accountName.ts`.
- **El formateo del número lo hace el backend** (Tarea 14): `/api/me` devuelve `formattedPhoneNumber` y `maskedPhoneNumber`. Así el front no necesita una librería de teléfonos, y el número se ve igual en los dos lados.

### Riesgos aceptados

- **Lo que se encola antes de confirmar.** El bot, el pedido de código y la invitación encolan el mensaje antes del commit. Si la confirmación falla después de encolar, sale un enlace o un código que no sirve; la vuelta siguiente manda otro que sí. Al revés (confirmar y después encolar) el caso malo es peor: la cuenta creada y el mensaje que nunca sale.
- **El tope diario cuenta solo lo que salió.** En `InviteOnly`, alguien que lleve el contador justo al borde podría deducir si un número tiene cuenta. Es caro, se aprende un número por día y cada intento le corta el ingreso por WhatsApp a todos. Contar también lo que no sale cerraría la pista, pero dejaría agotar el tope con 100 números inventados, y el tope existe para controlar el costo.
- **Un interbloqueo poco probable** (Tarea 13): si el bot procesa un mensaje del mismo contacto justo cuando la cuenta confirma ese número, y ese número estaba sin verificar, Postgres puede abortar a uno. El confirmar respondería 500, o el procesador reintenta en la vuelta siguiente.
- **La retención corre solo con la Api prendida.** Si el equipo está apagado, los textos vencidos esperan al próximo arranque.
- **Las invitaciones no tienen tope diario**, solo la espera de un minuto por cuenta, y cada plantilla de marketing se cobra.
- **La consulta del tope diario no tiene índice propio.** Con pocos códigos es despreciable; si `LoginCodes` crece, hace falta un índice parcial sobre `SentAtUtc` para `Channel = 'WhatsApp'`.

### Pendientes al cerrar

1. **La prueba manual del Hito 4.** Espera que Meta apruebe `invitacion_acceso`, en revisión desde el 2026-09-23 **como Marketing**: iba como Utilidad y Meta no la aceptó. Consecuencias que ya están asumidas: cada invitación cuesta más y puede no llegar.
2. **La biblioteca [ArquitecturaBase UI](https://claude.ai/artifact/Ew763kqorVHSYeUqE8CZ7h) quedó atrás.** `Banner`, `RadioGroupField`, `VerificationBadge`, `PhoneField`, `OtpInput`, `SegmentedControl` con `fullWidth`, `CheckboxField` con `error`, `Spinner` con `decorative`, nueve íconos y cuatro tokens están en `shared/ui` y no en la biblioteca. La regla dice que un cambio de aspecto va a los dos lados a la vez; la lista completa quedó en `visual-baseline.md`, en "La biblioteca de piezas".
3. **Dos cosas del lienzo quedaron viejas:** el panel 2 de "WhatsApp · Mensajes que manda el sistema" dibuja el texto de invitación que Meta rechazó, y la nota de la sección dice "Propuesta para aprobar: todavía no hay nada programado".
4. **Sin dibujar, y por eso sin programar:** el estado de la última invitación y su reenvío (el backend ya los expone en `lastInvitation.deliveryStatus`), y el responsive de todo, que sigue siendo el hueco más grande del fundamento visual.
5. **Decisiones del front que quedaron tomadas por omisión** y convendría mirar contra el tablero: si un correo o un número sin verificar ofrecen "Verificar"; si el alta preselecciona el rol User; si la casilla de invitación arranca marcada; el número con guion contra el espacio del tablero.
6. **La política de privacidad tiene 18 puntos abiertos** antes de que entre gente real, empezando por el script de borrado a pedido y los plazos de `LoginCodes`, `LoginLinks`, `LoginAudits`, `WhatsAppContacts`, `UserInvitations` y los tokens de OpenIddict. Están en "La política de privacidad: lo que falta".
7. **Producción queda afuera de este plan:** número propio, medio de pago, plantillas en la cuenta del número real, servidor con HTTPS propio, secretos en un almacén y monitoreo del token y del costo. Están en "MVP y producción".
8. **Dos deudas técnicas chicas:** `WhatsAppRegistrationTests` y el test de Google de `ExternalLoginTests` tienen una carrera latente con `WebApplicationFactory` y convendría pasarlos a `IStartupValidator`; y `WhatsApp:BusinessAccountId`, en `appsettings.Development.json`, hoy **no lo lee nadie** (está anotado porque es el dato que pide el panel de Meta).
9. **De la Fase 3, todavía en pie:** nada copia el `dist/` del front al `wwwroot` de la Api.
10. **Mejora posible:** que el backend incluya el `returnUrl` en el redirect de `/login?error=`, y así el rodeo por `sessionStorage` se puede sacar. El front ya soporta las dos formas.
