# Arquitectura Base — Documento de diseño

- **Fecha:** 2026-09-18
- **Estado:** borrador para revisión
- **Repos:** `ArquitecturaBase` (backend) y `ArquitecturaBaseFront` (frontend), hermanos en la misma carpeta
- **Maqueta navegable:** https://claude.ai/artifact/Lhjip75FcWeKRn8zZNMs5J

## 1. Objetivo y alcance

Una plantilla base reutilizable para aplicaciones web: .NET 10 + Aspire + React + PostgreSQL, con Clean Architecture y patrones explícitos.

El primer entregable es:

1. Un login robusto sin contraseña (código por email + Google) sobre OpenIddict, preparado para roles y permisos.
2. El tablero base: menú lateral colapsable, barra superior con el usuario y área de contenido.
3. Las piezas transversales que usa cualquier módulo: result pattern, errores, paginado, traducciones, validación, auditoría, fechas en UTC y componentes estándar del front.

**Fuera de alcance por ahora:** multi-tenant, pantallas de administración de usuarios/roles (fase 4), passkeys, segundo factor adicional y outbox persistente para emails.

## 2. Decisiones principales

| Decisión | Motivo |
|---|---|
| Login sin contraseña: código de 6 dígitos por email + Google | No se guardan contraseñas, no hay "olvidé mi contraseña" y cada ingreso verifica el email. El código funciona entre dispositivos y los antivirus de correo no lo consumen. |
| OpenIddict + ASP.NET Core Identity | Servidor OIDC/OAuth2 estándar y gratuito (Apache 2.0). Identity aporta usuarios, roles, claims, logins externos y bloqueo. |
| Un solo host `Api`: servidor de identidad + API de negocio | Menos piezas. Se puede separar después sin tocar Domain ni Application. |
| Clean Architecture en 4 capas | Domain → Application → Infrastructure → Api, con reglas verificadas por tests. |
| Handlers propios, sin MediatR ni AutoMapper | Ambos pasaron a licencia comercial en 2025. Un dispatcher mínimo y mapeos manuales alcanzan. |
| Repositorio específico por agregado + Unit of Work | Repository pattern explícito sin filtrar `IQueryable` fuera de Infrastructure. |
| PostgreSQL + EF Core (Npgsql) | Pedido del proyecto. |
| Email por SMTP de Gmail con MailKit | Pedido del proyecto. Cambiar de proveedor es una nueva implementación de `IEmailSender`. |
| **Todas las fechas se guardan y viajan en UTC** | Evita errores de zona horaria. Solo se convierte a hora local para mostrar. |
| Mismo origen para el navegador | Sin CORS y con cookies first-party (ver 5.1). |

## 3. Soluciones y proyectos

### 3.1 Backend

```
ArquitecturaBase/
├─ ArquitecturaBase.slnx
├─ Directory.Build.props         net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors, analizadores
├─ Directory.Packages.props      Central Package Management
├─ BannedSymbols.txt             APIs prohibidas (DateTime.Now, etc.)
├─ .editorconfig
├─ docs/specs/                   este documento
├─ src/
│  ├─ ArquitecturaBase.Domain/
│  ├─ ArquitecturaBase.Application/
│  ├─ ArquitecturaBase.Infrastructure/
│  ├─ ArquitecturaBase.Api/
│  ├─ ArquitecturaBase.AppHost/
│  └─ ArquitecturaBase.ServiceDefaults/
└─ tests/
   ├─ ArquitecturaBase.Domain.UnitTests/
   ├─ ArquitecturaBase.Application.UnitTests/
   ├─ ArquitecturaBase.Api.IntegrationTests/
   └─ ArquitecturaBase.ArchitectureTests/
```

**Referencias permitidas** (las verifica `ArchitectureTests`):

| Proyecto | Puede referenciar |
|---|---|
| Domain | nada |
| Application | Domain |
| Infrastructure | Application, Domain |
| Api | Application, Infrastructure (solo para registrar dependencias), ServiceDefaults |
| AppHost | Api (como recurso de Aspire) |

**AppHost (Aspire)** orquesta:

- `postgres`: contenedor PostgreSQL con la base `appdb`, configurado para que siempre se pueda conectar desde un cliente externo (DBeaver):
  - **Puerto fijo 5433** en el host. El 5432 lo usa el PostgreSQL local de la máquina.
  - **Usuario `postgres` y contraseña fija**, tomada del parámetro `postgres-password` en los user-secrets del AppHost. Aspire no la regenera.
  - **Contenedor persistente** (`ContainerLifetime.Persistent`): sigue vivo entre ejecuciones del AppHost, así que DBeaver se conecta aunque la Api esté apagada.
  - **Volumen con nombre** `arquitecturabase-pgdata`: los datos sobreviven si se borra el contenedor. Postgres toma la contraseña solo al crear el volumen; para cambiarla hay que borrar el volumen.
  - Sin pgAdmin, porque se usa DBeaver.

  ```csharp
  var postgresPassword = builder.AddParameter("postgres-password", secret: true);

  var postgres = builder.AddPostgres("postgres", password: postgresPassword, port: 5433)
      .WithDataVolume("arquitecturabase-pgdata")
      .WithLifetime(ContainerLifetime.Persistent);

  var appDb = postgres.AddDatabase("appdb");
  ```

  La Api recibe la cadena de conexión que arma Aspire (`ConnectionStrings:appdb`), así que no se duplica en el `appsettings.json` de la Api.
- `api`: `ArquitecturaBase.Api`, que referencia `appdb` y espera a que esté lista.
- `web`: el front con Vite (`../../../ArquitecturaBaseFront`), que referencia `api` para configurar su proxy. Vite corre con HTTPS usando el certificado de desarrollo de .NET, porque la cookie de sesión es `Secure`.

En desarrollo, la Api aplica migraciones y datos iniciales al arrancar. En producción, las migraciones se aplican desde el pipeline con un migration bundle.

### 3.2 Frontend

```
ArquitecturaBaseFront/
├─ index.html · vite.config.ts · tsconfig.json · .env.development
└─ src/
   ├─ app/              main.tsx, App.tsx, providers (Query, Auth, i18n, Theme), router.tsx
   ├─ layouts/
   │  ├─ AppLayout/     Sidebar + Topbar + <Outlet />
   │  └─ AuthLayout/    fondo de marca + tarjeta centrada
   ├─ features/         por módulo: pages/, components/, api/, hooks/, types.ts
   │  ├─ auth/          LoginPage, VerifyCodePage, CallbackPage, OtpInput, GoogleButton
   │  ├─ dashboard/     HomePage
   │  └─ users/         UsersPage (ejemplo de listado paginado)
   ├─ shared/
   │  ├─ ui/            componentes estándar (ver 7.4)
   │  ├─ components/    ErrorBoundary, ProtectedRoute, Can, ForbiddenPage, NotFoundPage
   │  ├─ api/           httpClient, ApiError, queryKeys
   │  ├─ auth/          configuración de oidc-client-ts, useAuth, usePermissions
   │  ├─ types/         PagedRequest, PagedResult<T>, ProblemDetails
   │  ├─ hooks/         usePagination, useDebounce, useMediaQuery, useLocalStorage
   │  ├─ lib/           formateo de fechas/números (Intl), cn()
   │  ├─ i18n/          configuración de i18next
   │  └─ config/        env.ts (validado con zod), navigation.ts
   ├─ locales/          es/{common,auth,users}.json · en/...
   └─ styles/           tokens de marca (CSS variables), globals.css
```

## 4. Contenido de cada capa del backend

### 4.1 Domain

No depende de ningún paquete. Contiene reglas de negocio puras.

```
Common/
  Entity.cs               Id (Guid v7) + igualdad por identidad
  AggregateRoot.cs        acumula eventos de dominio
  ValueObject.cs
  IDomainEvent.cs
  IAuditable.cs           CreatedAtUtc, CreatedBy, ModifiedAtUtc, ModifiedBy
  ISoftDeletable.cs       IsDeleted, DeletedAtUtc, DeletedBy
Results/
  Result.cs               Result y Result<T>
  Error.cs                Code, Description, Type, Metadata
  ErrorType.cs
Authentication/
  LoginCode.cs            agregado (ver 5.3)
  LoginCodeErrors.cs
  ILoginCodeRepository.cs
  LoginAudit.cs           registro de cada intento de ingreso
  ILoginAuditRepository.cs
Authorization/
  Permissions.cs          catálogo de permisos (constantes)
  SystemRoles.cs          Admin, User
Users/
  UserErrors.cs
ValueObjects/
  Email.cs                normaliza (trim + minúsculas) y valida
```

El usuario no es una entidad de Domain: lo gestiona ASP.NET Core Identity en Infrastructure. Así Domain no depende de ningún framework. Application accede a usuarios mediante `IIdentityService`.

### 4.2 Application

Contiene los casos de uso. Depende solo de Domain y de abstracciones de Microsoft (`Microsoft.Extensions.*`), más FluentValidation.

```
Abstractions/
  Messaging/      ICommand, ICommand<T>, IQuery<T>, ICommandHandler<,>, IQueryHandler<,>
  Behaviors/      ValidationDecorator, LoggingDecorator, UnitOfWorkDecorator (solo comandos)
  Persistence/    IUnitOfWork
  Identity/       IIdentityService, ICurrentUser, IPermissionService
  Email/          IEmailSender, IEmailQueue, IEmailTemplateRenderer, EmailMessage
  Security/       ILoginCodeGenerator, ILoginCodeHasher
Common/
  Pagination/     PagedRequest, PagedResult<T>, SortDescriptor
  Validation/     reglas reutilizables (email, paginado)
Features/
  Auth/
    RequestLoginCode/             Command + Handler + Validator
    VerifyLoginCode/              Command + Handler + Validator + VerifyLoginCodeResponse
    SignInWithExternalProvider/   Command + Handler
  Users/
    GetCurrentUser/               Query + Handler + CurrentUserResponse (perfil + permisos)
    GetUsers/                     Query : PagedRequest + Handler + UserListItem
Resources/
  Errors.resx · Errors.en.resx
  Validation.resx · Validation.en.resx
DependencyInjection.cs            AddApplication()
```

Los handlers se registran por escaneo del ensamblado y se envuelven con los decoradores (Scrutor `Decorate`). Cada handler devuelve `Result` o `Result<T>`; no lanza excepciones por reglas de negocio.

### 4.3 Infrastructure

```
Persistence/
  ApplicationDbContext.cs       IdentityDbContext<ApplicationUser, ApplicationRole, Guid> + OpenIddict + tablas propias
  Configurations/               un IEntityTypeConfiguration<T> por entidad
  Interceptors/
    AuditableEntityInterceptor.cs
    SoftDeleteInterceptor.cs
  Repositories/                 LoginCodeRepository, LoginAuditRepository
  UnitOfWork.cs
  Extensions/QueryableExtensions.cs   ToPagedResultAsync(), ApplySort() con lista blanca
  Migrations/
  Seed/                         roles, permisos por rol, cliente OpenIddict "web", admin inicial
Identity/
  ApplicationUser.cs            IdentityUser<Guid> + DisplayName, Culture, TimeZoneId, IsActive, auditoría
  ApplicationRole.cs            IdentityRole<Guid> + Description
  IdentityService.cs            IIdentityService sobre UserManager/RoleManager
  PermissionService.cs          permisos efectivos por usuario, con HybridCache
  OpenIddict/
    OpenIddictConfiguration.cs  endpoints, flujos, scopes, duración de tokens, certificados
    ClaimsDestinations.cs       qué claims van a cada token
Security/
  LoginCodeGenerator.cs         RandomNumberGenerator
  LoginCodeHasher.cs            HMAC-SHA256 con clave secreta + FixedTimeEquals
Email/
  SmtpOptions.cs                validadas al arrancar
  SmtpEmailSender.cs            MailKit
  EmailQueue.cs                 Channel<EmailMessage>
  EmailBackgroundService.cs     envía y reintenta (3 intentos, backoff exponencial)
  EmailTemplateRenderer.cs      plantilla + textos de Emails.resx según el idioma
  Templates/_Layout.html · LoginCode.html   (recursos embebidos)
  Resources/Emails.resx · Emails.en.resx
DependencyInjection.cs          AddInfrastructure(IConfiguration)
```

### 4.4 Api (presentación)

```
Program.cs                      solo composición
Endpoints/
  IEndpoint.cs                  cada grupo de endpoints se registra solo
  Account/                      LoginCodeEndpoints, ExternalLoginEndpoints
  Connect/                      AuthorizeEndpoint, TokenEndpoint, LogoutEndpoint, UserInfoEndpoint
  Users/                        MeEndpoint, UsersEndpoints
ErrorHandling/
  GlobalExceptionHandler.cs     IExceptionHandler → 500 ProblemDetails
  ResultExtensions.cs           Result → IResult (ProblemDetails)
Authorization/
  PermissionRequirement.cs · PermissionAuthorizationHandler.cs · PermissionPolicyProvider.cs
  EndpointExtensions.cs         .RequirePermission(Permissions.Users.Read)
Services/
  CurrentUser.cs                ICurrentUser desde los claims
Localization/                   RequestLocalization (es por defecto, en)
RateLimiting/                   políticas por IP "login-code" y "login-verify"
Json/
  UtcDateTimeConverter.cs       ver 6.3
OpenApi/                        OpenAPI + Scalar (solo en desarrollo)
DependencyInjection.cs          AddPresentation()
appsettings.json · appsettings.Development.json
```

## 5. Autenticación y autorización

### 5.1 Topología: el navegador ve un solo origen

- **Desarrollo:** el navegador abre `https://localhost:5173` (Vite). Vite hace de proxy de `/api`, `/account`, `/connect`, `/signin-google` y `/.well-known` hacia la Api. El destino lo obtiene de las variables que inyecta Aspire.
- **Producción:** la Api sirve el build del front desde `wwwroot`, con fallback a `index.html` para las rutas del SPA. Otra opción es un reverse proxy con las mismas rutas.
- **Resultado:** no hace falta CORS, la cookie de sesión es first-party y el issuer de OpenIddict es el origen público (configurable en `Authentication:Issuer`).

### 5.2 Flujo de ingreso con código

1. El SPA detecta que no hay sesión y llama a `signinRedirect()` (oidc-client-ts). El navegador va a `/connect/authorize` con code + PKCE y el cliente `web`.
2. `/connect/authorize` no encuentra la cookie de Identity y redirige a `/login?returnUrl=<authorize original>`. Esa página la sirve el SPA.
3. El usuario escribe su email y el SPA llama a `POST /account/login-code`. La respuesta es siempre `202 Accepted` con el mismo cuerpo.
4. El usuario ingresa el código y el SPA llama a `POST /account/login-code/verify` con `{ email, code, returnUrl }`.
5. Si el código es válido, la Api crea el usuario si no existía, inicia la cookie de Identity y responde `200 { returnUrl }`. `returnUrl` debe ser una ruta local que empiece con `/connect/authorize`; si no, se rechaza para evitar redirecciones abiertas.
6. El SPA navega a `returnUrl`. Ahora `/connect/authorize` encuentra la cookie, emite el authorization code y redirige a `/auth/callback`.
7. oidc-client-ts canjea el code en `/connect/token` y recibe el access token, el refresh token y el id token.

Si alguien abre `/login` sin `returnUrl`, el SPA arranca primero el flujo OIDC (paso 1).

### 5.3 Reglas del código (`LoginCode`)

| Regla | Valor (configurable en `Authentication:LoginCode`) |
|---|---|
| Formato | 6 dígitos generados con `RandomNumberGenerator` |
| Almacenamiento | solo el hash HMAC-SHA256 (clave secreta en configuración) |
| Vencimiento | 10 minutos (`ExpiresAtUtc`) |
| Intentos | máximo 5 por código; al 5.º fallo queda bloqueado |
| Uso | una sola vez (`ConsumedAtUtc`) |
| Reenvío | cada 60 s; pedir uno nuevo invalida los anteriores del mismo email |
| Límite por IP | 20 pedidos cada 15 minutos (middleware `RateLimiter`) |
| Límite por email | 5 pedidos cada 15 minutos (lo controla el handler contando los códigos recientes, porque el email viene en el body) |
| Respuesta de pedido | 202, exista o no la cuenta: el primer ingreso la crea, así que no se revela nada. 429 con `retryAfter` si se pide antes del reenvío o se supera un límite |
| Respuesta de verificación sin código activo | el mismo error que un código incorrecto |
| Cuenta deshabilitada | se informa después de verificar el código (el usuario ya probó que el email es suyo) |
| Bloqueo de Identity | 10 verificaciones fallidas seguidas → 15 min de bloqueo |

`LoginCode.Verify(hash, now)` devuelve `Result` con uno de estos errores: `Auth.LoginCode.Invalid` (con `attemptsLeft` en Metadata), `Auth.LoginCode.Expired`, `Auth.LoginCode.AlreadyUsed` o `Auth.LoginCode.TooManyAttempts`.

Cada intento (éxito o fallo) se registra en `LoginAudit` con email, usuario, método, resultado, motivo, IP, user agent y `OccurredAtUtc`. Nunca se registra el código.

### 5.4 Ingreso con Google

1. El botón navega a `GET /account/external/google?returnUrl=...` y la Api hace el challenge.
2. Google vuelve a `/signin-google` y el middleware redirige a `GET /account/external/callback`.
3. `SignInWithExternalProvider` hace lo siguiente:
   - busca un usuario por login externo (proveedor + id);
   - si no lo encuentra, busca por email, pero solo si Google indica `email_verified`, y vincula la cuenta;
   - si tampoco existe, crea el usuario con `EmailConfirmed = true`.
4. Se inicia la cookie y se redirige a `returnUrl` (desde el paso 6 de 5.2).

La URI de redirección registrada en Google es `https://localhost:5173/signin-google` en desarrollo y `https://<dominio>/signin-google` en producción.

### 5.5 OpenIddict

- **Endpoints:** authorize, token, end session (`/connect/logout`), userinfo, revocation e introspection (esta última para futuros servidores de recursos).
- **Flujos:** authorization code con PKCE obligatorio y refresh token. Client credentials queda documentado para más adelante.
- **Cliente inicial:** `web` (público), con redirect URIs tomadas de configuración.
- **Scopes:** `openid`, `profile`, `email`, `roles`, `offline_access`, `api`.
- **Duraciones:**
  - authorization code: 5 min;
  - access token: 15 min;
  - refresh token: 30 días, rotativo (reusar uno ya rotado revoca toda la cadena);
  - cookie de Identity: 30 días, deslizante.
- **Certificados:** en desarrollo, los de desarrollo; en producción, X.509 desde configuración o un almacén de secretos.
- **Data Protection:** claves persistidas en Postgres para soportar varias instancias.
- **Validación:** `UseLocalServer()` + `UseAspNetCore()`. El esquema por defecto de `/api` es la validación de OpenIddict (bearer).
- **Tokens en el SPA:** se guardan en memoria, nunca en `localStorage`. Al recargar la página se renuevan en silencio (`prompt=none`) usando la cookie del servidor.
- **Logout:** el SPA va a `/connect/logout`. La Api cierra la cookie, revoca los tokens de esa autorización y vuelve a `/login`.

### 5.6 Roles y permisos

- **Catálogo:** los permisos viven en `Domain/Authorization/Permissions.cs` como constantes (`users.read`, `users.manage`, `roles.read`, `roles.manage`). `Permissions.All` los lista para el seed.
- **Almacenamiento:** cada permiso es un role claim de tipo `permission`. Un usuario puede tener varios roles y sus permisos se suman.
- **Seed:**
  - `Admin` recibe todos los permisos y `User` ninguno por ahora.
  - El email de `Seed:AdminEmail` recibe `Admin` al crearse.
  - El resto de los usuarios nuevos recibe `User`.
- **Tokens:** el access token lleva `sub`, `email`, `name` y `role`. Los permisos no van en el token, para que no crezca ni quede desactualizado.
- **Chequeo en el backend:** `PermissionService` resuelve los permisos por rol con HybridCache y los invalida cuando cambia un rol. Los endpoints piden permisos con `.RequirePermission(Permissions.Users.Read)`, nunca roles.
- **Front:** `GET /api/me` devuelve perfil, permisos e idioma/zona horaria. El front los usa para `usePermissions`, `<Can>` y para filtrar `navigation.ts`. Los chequeos del front son solo de experiencia de uso: quien decide es el backend.

### 5.7 Endpoints iniciales

| Método | Ruta | Acceso | Qué hace |
|---|---|---|---|
| POST | `/account/login-code` | anónimo + rate limit | pide un código |
| POST | `/account/login-code/verify` | anónimo + rate limit | verifica e inicia la sesión del servidor |
| GET | `/account/external/google` | anónimo | inicia el ingreso con Google |
| GET | `/account/external/callback` | anónimo | vuelve de Google |
| GET/POST | `/connect/authorize` | cookie | emite el authorization code |
| POST | `/connect/token` | cliente | canjea el code o el refresh token |
| GET/POST | `/connect/logout` | cookie | cierra la sesión |
| GET | `/connect/userinfo` | bearer | claims del usuario |
| POST | `/connect/revoke` | cliente | revoca tokens |
| GET | `/api/me` | bearer | perfil, permisos y preferencias |
| GET | `/api/users` | bearer + `users.read` | listado paginado (ejemplo del patrón) |

Los endpoints de `/account` solo aceptan `application/json` del mismo origen, sin CORS. Así se evita el CSRF de login.

## 6. Temas transversales

### 6.1 Result pattern y manejo de errores

- `Error(string Code, string Description, ErrorType Type, IReadOnlyDictionary<string, object?>? Metadata)`.
- Los códigos son estables y siguen el formato `Area.Entidad.Motivo`, por ejemplo `Auth.LoginCode.Expired`. La `Description` se traduce desde `Errors.resx` usando el código como clave.
- Los errores de validación llevan un diccionario `campo → mensajes`.

| ErrorType | HTTP |
|---|---|
| Validation | 400 |
| Unauthorized | 401 |
| Forbidden | 403 |
| NotFound | 404 |
| Conflict | 409 |
| TooManyRequests | 429 |
| Failure | 500 |

Todas las respuestas de error usan ProblemDetails (RFC 9457):

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Datos inválidos",
  "status": 400,
  "detail": "Revisá los campos marcados.",
  "code": "Validation.Failed",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errors": { "email": ["Ingresá un correo válido."] }
}
```

- **Excepciones no controladas:** `GlobalExceptionHandler` devuelve un 500 con un mensaje genérico y el `traceId`, y registra el detalle en el log. Nunca se exponen stack traces.
- **Front:** `httpClient` convierte ProblemDetails en `ApiError` y lo maneja así:
  - `errors` → `setError` en react-hook-form;
  - 401 → renovar la sesión y, si falla, ir al login;
  - 403 → `ForbiddenPage`;
  - resto → toast con `detail`;
  - error de red → toast con opción de reintentar.

### 6.2 Paginado

- **Pedido:** `?page=2&pageSize=20&sort=-createdAtUtc&search=juan`.
- **`PagedRequest`:** `Page` (≥ 1, por defecto 1), `PageSize` (1–100, por defecto 20), `Sort` (`campo` o `-campo`) y `Search`.
- **Ordenamiento:** cada consulta declara su lista blanca de campos ordenables. Un campo fuera de la lista devuelve un error de validación.
- **`PagedResult<T>`:** `Items`, `Page`, `PageSize`, `TotalCount`, `TotalPages`, `HasPrevious`, `HasNext`.
- **Infrastructure:** `ToPagedResultAsync()` aplica `Skip`/`Take` y el conteo.
- **Front:** `usePagination` sincroniza página, orden y búsqueda con la URL (para compartir y volver atrás). `DataTable` + `Pagination` consumen `PagedResult<T>`.

### 6.3 Fechas y horas: todo en UTC

- **Base de datos:** las columnas de fecha-hora son `timestamp with time zone`. Npgsql rechaza escribir `DateTime` que no esté en UTC, así que funciona como red de seguridad.
- **Código:** se usa `DateTime` en UTC, obtenido de `TimeProvider` inyectado (`timeProvider.GetUtcNow().UtcDateTime`). `DateTime.Now`, `DateTime.Today`, `DateTimeOffset.Now` y `DateTime.UtcNow` están prohibidos con `BannedApiAnalyzers` y rompen el build; esta última porque impide testear el tiempo.
- **Nombres:** las propiedades terminan en `Utc` (`CreatedAtUtc`, `ExpiresAtUtc`, `OccurredAtUtc`).
- **API:**
  - la salida es ISO 8601 con `Z` (`2026-09-18T17:32:00Z`);
  - las entradas con offset se convierten a UTC;
  - las entradas sin offset se rechazan con 400.
  - Todo lo hace `UtcDateTimeConverter`.
- **Fechas de calendario sin hora** (vencimientos, cumpleaños): `DateOnly` → columna `date`.
- **Mostrar:** el front convierte a la zona del usuario con `Intl.DateTimeFormat`. La zona sale de `TimeZoneId` del perfil (IANA, por defecto `America/Argentina/Buenos_Aires`). Los filtros por día se envían como rango UTC.
- **Emails:** las fechas se formatean en el servidor con la zona del usuario (`TimeZoneInfo.FindSystemTimeZoneById`).
- **Infraestructura:** el contenedor de Postgres corre en UTC, los logs van en UTC y los tests usan `FakeTimeProvider`.

### 6.4 Traducciones y resources

- **Idiomas:** español (por defecto) e inglés.
- **Backend:**
  - archivos `.resx` por área: `Errors`, `Validation` y `Emails`;
  - el idioma de la petición sale de `Accept-Language`, que el front envía según su idioma actual;
  - los emails usan el `Culture` del perfil del usuario.
- **Front:**
  - i18next con un namespace por módulo (`locales/{idioma}/{módulo}.json`), cargados bajo demanda;
  - el idioma se cambia desde el menú del usuario y se guarda en el perfil;
  - fechas, números y moneda se formatean con `Intl` en el idioma actual.

### 6.5 Validación

- **Backend:** FluentValidation en Application. `ValidationDecorator` corre los validadores antes del handler y devuelve un `Result` de validación.
- **Front:** los esquemas zod repiten los mismos límites para dar feedback inmediato. El backend siempre vuelve a validar.

### 6.6 Auditoría y soft delete

- `AuditableEntityInterceptor` completa `CreatedAtUtc/By` y `ModifiedAtUtc/By` con `ICurrentUser` y `TimeProvider`.
- Las entidades `ISoftDeletable` se marcan como borradas en lugar de eliminarse, y un filtro global las excluye de las consultas.

### 6.7 Observabilidad

- ServiceDefaults configura OpenTelemetry (trazas, métricas y logs) hacia el dashboard de Aspire.
- El `traceId` de cada ProblemDetails permite encontrar el error en el dashboard.
- Nunca se registran códigos, tokens ni secretos.

### 6.8 Configuración y secretos

Las opciones usan el Options pattern con `ValidateDataAnnotations().ValidateOnStart()`: si falta algo obligatorio, la app no arranca y dice qué falta.

`appsettings.json` de la Api (sin secretos):

```json
{
  "Authentication": {
    "Issuer": "https://localhost:5173/",
    "LoginCode": { "Length": 6, "LifetimeMinutes": 10, "MaxAttempts": 5, "ResendCooldownSeconds": 60 },
    "Google": { "ClientId": "" }
  },
  "Email": {
    "Smtp": {
      "Host": "smtp.gmail.com",
      "Port": 587,
      "Security": "StartTls",
      "UserName": "<cuenta de Gmail que envía>",
      "FromName": "Arquitectura Base",
      "FromAddress": "<cuenta de Gmail que envía>"
    }
  },
  "Seed": { "AdminEmail": "" }
}
```

Los secretos van en user-secrets en desarrollo y en variables de entorno o un almacén de secretos en producción:

| Clave | Qué es |
|---|---|
| `Email:Smtp:Password` | contraseña de aplicación de Gmail (https://myaccount.google.com/apppasswords) |
| `Authentication:Google:ClientSecret` | secreto del cliente OAuth de Google |
| `Authentication:LoginCode:HashKey` | clave del HMAC de los códigos (32 bytes en base64) |

En el AppHost (user-secrets del proyecto `ArquitecturaBase.AppHost`):

| Clave | Qué es |
|---|---|
| `Parameters:postgres-password` | contraseña fija del contenedor de Postgres (la usa también DBeaver) |

La cadena de conexión de Postgres la inyecta Aspire.

### 6.9 Seguridad general

- HTTPS y HSTS.
- Encabezados de seguridad: CSP para el SPA, `X-Content-Type-Options`, `Referrer-Policy` y `frame-ancestors 'self'`.
- Rate limiting en `/account`.
- Respuestas genéricas donde podrían revelar si existe una cuenta.
- Bloqueo de Identity ante fallos repetidos.
- La cookie de sesión es `HttpOnly`, `Secure` y `SameSite=Lax`.
- Ningún secreto en el repo.

## 7. Frontend

### 7.1 Stack

- Vite + React + TypeScript (strict).
- React Router: rutas y `<Outlet />`.
- TanStack Query: estado del servidor, caché y reintentos.
- react-hook-form + zod: formularios.
- oidc-client-ts: OIDC con code + PKCE.
- i18next + react-i18next: traducciones.
- Tailwind CSS + shadcn/ui (sobre Radix, accesibles, copiados en el repo).
- Vitest + Testing Library + MSW: tests.

### 7.2 Layouts

- **AuthLayout:** fondo del color de marca y tarjeta centrada. Lo usan `/login`, `/login/codigo` y `/auth/callback`.
- **AppLayout:**
  - **Sidebar:**
    - expandido (ícono + texto) o contraído (solo íconos, con tooltip);
    - el estado se recuerda en `localStorage`;
    - los grupos se despliegan (por ejemplo, Administración → Usuarios, Roles y permisos);
    - en pantallas de menos de 768 px es un panel deslizable con fondo oscurecido.
  - **Topbar:** botón ☰ (contrae el menú en escritorio y lo abre en móvil), migas y título de la página, y menú del usuario (perfil, dispositivos y sesiones, idioma, cerrar sesión).
  - **Contenido:** `<Outlet />`. Cada módulo aporta solo su página.
- **Menú:** `navigation.ts` define ítems, íconos, rutas y el permiso que requiere cada uno. El menú se filtra con los permisos del usuario.

### 7.3 Rutas

- `ProtectedRoute` exige una sesión válida y, opcionalmente, un permiso.
- Sin sesión, arranca el flujo OIDC y después vuelve a la ruta original.
- Sin permiso, muestra `ForbiddenPage`.

### 7.4 Componentes estándar (`shared/ui`)

- **Controles:** Button, IconButton, Input, Textarea, Select, Checkbox, Switch, FormField (label + control + error).
- **Superposiciones:** Dialog, ConfirmDialog, DropdownMenu, Tooltip.
- **Estados:** Badge, Toast, Skeleton, Spinner, EmptyState.
- **Página:** PageHeader (título + acciones), SearchInput.
- **Listados:** DataTable (columnas, orden, estados de carga/vacío/error) y Pagination.

Todos toman colores y radios de los tokens de marca, así cambiar la marca es cambiar variables.

## 8. Emails

- **Plantillas:** `_Layout.html` (encabezado de marca y pie) + `LoginCode.html`, como recursos embebidos.
  - HTML con tablas y estilos inline, compatible con Gmail y Outlook.
  - El logo es un PNG con URL absoluta (`Email:LogoUrl`), porque Gmail no muestra SVG.
- **Render:** `EmailTemplateRenderer` reemplaza `{{Marcadores}}` escapando el HTML, con los textos de `Emails.resx` en el idioma del usuario.
- **Asunto:** `{código} es tu código de acceso a {AppName}`, para que se vea en la notificación del celular.
- **Envío:** en segundo plano (`IEmailQueue` → `EmailBackgroundService`) con 3 reintentos, así el login no espera al SMTP. Si fallan todos, se registra el error y el usuario puede pedir otro código.
- **Límite de Gmail:** unos 500 destinatarios por día en cuentas personales. Si no alcanza, se cambia la implementación de `IEmailSender`.

## 9. Tests

| Proyecto | Qué cubre |
|---|---|
| Domain.UnitTests | reglas de `LoginCode`, `Email`, `Result` |
| Application.UnitTests | handlers con dobles de prueba y `FakeTimeProvider` |
| Api.IntegrationTests | API real (`WebApplicationFactory`) contra Postgres en contenedor (Testcontainers): flujo completo de código → authorize → token → `/api/me`, errores en ProblemDetails, rate limiting, permisos |
| ArchitectureTests | reglas de dependencias entre capas (NetArchTest.Rules) |
| Front (Vitest) | OtpInput, DataTable, Pagination, httpClient/ApiError, Sidebar |

En los tests de integración, `IEmailSender` se reemplaza por uno que guarda los mensajes, para leer el código enviado.

## 10. Fases

Cada fase tiene su propio plan de implementación y un punto de revisión al terminar.

| Fase | Entregable | Terminada cuando |
|---|---|---|
| 1. Fundaciones | solución y capas, AppHost con Postgres, ServiceDefaults, Result/errores/ProblemDetails, paginado, validación, traducciones, UTC, auditoría, tests de arquitectura | `aspire run` levanta Postgres y la Api, y los tests pasan. Esos tests verifican `Result` → ProblemDetails traducido, paginado y el conversor UTC con endpoints de prueba que existen solo en el proyecto de tests |
| 2. Identidad | Identity + OpenIddict, código por email, Gmail + plantilla, Google, roles y permisos iniciales, rate limiting, auditoría de ingresos | el flujo completo funciona en los tests de integración y con Postman/Scalar |
| 3. Front base | proyecto Vite, componentes estándar, i18n, httpClient, AppLayout con sidebar, login/código/callback, listado paginado de Usuarios | se ingresa desde el navegador con código y con Google, y se navega el tablero |
| 4. Administración (después) | ABM de usuarios, roles y permisos; dispositivos y sesiones | se define en su propio spec |

Además, cada repo tiene un `CLAUDE.md` con las convenciones de este documento.

## 11. Pendientes

- Cuenta de Gmail que envía los correos (va en `UserName` y `FromAddress`).
- Email del administrador inicial (`Seed:AdminEmail`).
- Nombre definitivo del producto. Mientras tanto: "Arquitectura Base".
