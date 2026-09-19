# Fase 2 (Identidad) — Plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDA: usar superpowers:subagent-driven-development (recomendado) o superpowers:executing-plans para ejecutar este plan tarea por tarea. Los pasos usan checkboxes (`- [ ]`).

**Objetivo:** ingreso sin contraseña por código de 6 dígitos enviado por email y con Google, sobre ASP.NET Core Identity + OpenIddict 7.7.1 (authorization code + PKCE y refresh token), con roles y permisos iniciales, rate limiting y auditoría de ingresos. Terminado cuando el flujo completo funciona en los tests de integración y a mano con Postman.

**Arquitectura:** sigue el spec `docs/specs/2026-09-18-arquitectura-base-design.md` (secciones 4, 5, 6.8, 8 y 9). La Api es a la vez servidor OpenIddict y API de negocio. Domain modela `LoginCode` y `LoginAudit`. Application tiene los casos de uso (pedir código, verificar, Google, perfil, listado) y habla con Identity solo a través de `IIdentityService`. Infrastructure implementa Identity, OpenIddict, emails, seguridad y la primera migración. La Api expone `/account`, `/connect` y `/api`.

**Stack (verificado el 2026-09-19 en NuGet y en el código fuente de cada paquete):**
- OpenIddict 7.7.1 (`OpenIddict.EntityFrameworkCore`, `OpenIddict.Server.AspNetCore`, `OpenIddict.Validation.AspNetCore`, `OpenIddict.Validation.ServerIntegration`);
- Microsoft.AspNetCore.Identity.EntityFrameworkCore, Authentication.Google y DataProtection.EntityFrameworkCore 10.0.12;
- Microsoft.EntityFrameworkCore.Design 10.0.12 (sube EF Core a 10.0.12, dentro del rango de Npgsql 10.0.3);
- Microsoft.Extensions.Caching.Hybrid 10.10.0;
- MailKit 4.18.0;
- Microsoft.Extensions.Options.ConfigurationExtensions y DataAnnotations 10.0.12;
- Microsoft.Extensions.Configuration 10.0.12 (solo en Application.UnitTests).

---

## Reglas para quien ejecute

Son las mismas de la Fase 1. Las repito porque cada subagente lee solo su tarea y este archivo.

- **Rama:** todo va directo a `main`. No crear ramas. **No hacer push.**
- **Commits:** uno por tarea, en español, conventional commits. La última línea de cada mensaje es exactamente `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- **Secretos:** nunca inventar ni escribir credenciales reales. Hay dos excepciones, ambas de desarrollo local:
  - la clave HMAC de desarrollo de los códigos (`Authentication:LoginCode:HashKey`) va en `appsettings.Development.json` de la Api, generada al azar en la Tarea 13;
  - la contraseña del Postgres local sigue en el AppHost.
- **Google:**
  - el ClientId es público y va en `appsettings.json`;
  - el ClientSecret lo carga **el usuario** con `dotnet user-secrets` (Tarea 22). Nunca escribirlo en archivos;
  - `docs/client_secret_*.json` está ignorado por git y no se toca.
- **Docker:** los tests de integración y el AppHost necesitan Docker Desktop encendido.
- **AppHost:** no dejar corriendo un AppHost al terminar una tarea. Bloquea los DLL y rompe el build del usuario (`aspire stop`).
- **Build:** `TreatWarningsAsErrors` sigue activo. Las advertencias se corrigen en el código. Solo se suprimen en `.editorconfig`, con justificación, si contradicen el spec.
- **Salida en español:** el SDK imprime "0 Advertencia(s)", "0 Errores", "total:" y "correcto:".
- **Tests:**
  - un proyecto: `dotnet test --project <csproj>`;
  - una clase: `dotnet test --project <csproj> -- --filter-class "Namespace.Clase"`;
  - los tests de xUnit pasan `TestContext.Current.CancellationToken` a todo método que acepte un token (analizador xUnit1051).
- **Idioma:** identificadores, logs y mensajes de excepción en inglés. Textos para el usuario en `.resx`, en español rioplatense con voseo y en inglés. Comentarios en español, solo si aportan.
- **Nunca registrar** códigos de ingreso, tokens ni secretos en logs (spec 6.7).
- **Convenciones del repo:** leer `CLAUDE.md`. Las tareas no las repiten.
- **Desvíos:** si algo del plan no compila o una API no existe tal cual, hacer el cambio mínimo e informarlo. Si el cambio altera el diseño, frenar y pedir contexto.

## Hechos verificados que condicionan el diseño

1. **Identity se registra con `AddIdentityCore` + `AddIdentityCookies`, no con `AddIdentity`.** `AddIdentity` fija `DefaultAuthenticateScheme` y `DefaultChallengeScheme` a la cookie, y esos valores le ganan a `DefaultScheme`. Queremos que el esquema por defecto de `/api` sea la validación de OpenIddict (bearer). `AddRoles<ApplicationRole>()` va **antes** de `AddEntityFrameworkStores`.
2. **Eventos de la cookie:** se asignan de a un delegado (`options.Events.OnRedirectToLogin = ...`). Reemplazar `options.Events` borra el `SecurityStampValidator`.
3. **Duración de la cookie:** para que dure 30 días, `SignInAsync` tiene que usar `isPersistent: true`.
4. **Lockout:**
   - lo manejamos a mano con `UserManager` (`IsLockedOutAsync`, `AccessFailedAsync`, `ResetAccessFailedCountAsync`);
   - usa el reloj real (`DateTimeOffset.UtcNow`), no `TimeProvider`: los tests no pueden adelantarlo;
   - defaults de Identity: 5 intentos y 5 minutos. El spec pide 10 intentos y 15 minutos.
5. **Google:**
   - si se registra con ClientId vacío, rompe **todos** los requests. Se registra solo cuando hay ClientId;
   - no mapea `email_verified` por defecto: hay que agregar `ClaimActions.MapJsonKey("email_verified", "email_verified")`. El valor llega como `"True"`, así que se compara sin distinguir mayúsculas;
   - `SignInScheme` tiene que ser `IdentityConstants.ExternalScheme`, explícito;
   - hay que usar `SignInManager.ConfigureExternalAuthenticationProperties`, o `GetExternalLoginInfoAsync` devuelve null;
   - en el camino de crear o vincular usuario, la cookie externa se cierra a mano.
6. **OpenIddict 7.7.1** (nombres vigentes desde la 6.0):
   - `SetEndSessionEndpointUris`, `SetUserInfoEndpointUris`, `EnableEndSessionEndpointPassthrough`, `EnableUserInfoEndpointPassthrough`;
   - `Permissions.Endpoints.EndSession`; no existe `Permissions.Endpoints.UserInfo`;
   - `PromptValues.None` / `HasPromptValue`.
   `GetOpenIddictServerRequest()` está en el namespace `Microsoft.AspNetCore`.
7. **Defaults de OpenIddict que contradicen el spec** y se cambian:
   - el refresh token reusado se acepta durante 30 s: `SetRefreshTokenReuseLeeway(null)`;
   - access token de 1 h: `SetAccessTokenLifetime(15 min)`;
   - refresh token de 14 días: `SetRefreshTokenLifetime(30 días)`;
   - revocar no invalida los access tokens ya emitidos: `EnableTokenEntryValidation()` en la validación, **después** de `UseLocalServer()`.
   Con esos cambios, reusar un refresh token revoca todos los tokens de la autorización (`RevokeByAuthorizationIdAsync`).
8. **Transporte:** OpenIddict exige HTTPS. En los tests el cliente usa `BaseAddress = https://localhost` y `AllowAutoRedirect = false`. La cookie de Identity es `Secure`, así que por http no vuelve.
9. **Issuer:** si no se configura, OpenIddict lo deduce del request. En la Fase 2 no se configura, porque todavía no hay proxy de Vite; `Authentication:Issuer` se agrega en la Fase 3.
10. **Reloj:** OpenIddict, la cookie y HybridCache toman el `TimeProvider` de DI. El `FakeTimeProvider` de los tests maneja la vida de tokens y códigos.
11. **Credenciales de firma y cifrado:** son obligatorias, una de cada.
   - Development: `AddDevelopmentEncryptionCertificate` / `AddDevelopmentSigningCertificate`;
   - Testing: `AddEphemeralEncryptionKey` / `AddEphemeralSigningKey`;
   - Producción: archivos PFX cargados con `X509CertificateLoader.LoadPkcs12FromFile`. Los constructores de `X509Certificate2` generan SYSLIB0057, que rompe el build.
12. **.NET 10 y los endpoints con `TypedResults`:** la cookie devuelve 401 en lugar de redirigir. El endpoint de authorize devuelve `Results.*` (`IResult`) y hace la redirección a `/login` él mismo.
13. **Rate limiting:**
   - el rechazo por defecto es 503: se fija `RejectionStatusCode = 429`;
   - `Retry-After` es la ventana completa;
   - bajo TestServer, `RemoteIpAddress` es null: todos los tests caen en la partición "unknown". Los límites son configurables; los tests usan límites altos, y el test de 429 usa una Api aparte con límite bajo.
14. **Migraciones:**
   - la primera migración generada rompe el build por IDE0005/IDE0161: se marca `[**/Migrations/*.cs] generated_code = true` en `.editorconfig`;
   - `dotnet ef` necesita la cadena de conexión como argumento (ver CLAUDE.md);
   - el `dotnet-ef` instalado es 10.0.9 y avisa que es más viejo que el runtime, pero funciona;
   - EF 10 lanza `PendingModelChangesWarning` en `MigrateAsync` si falta una migración.
15. **CA1711:** rechaza el sufijo `Queue` en `IEmailQueue`, un nombre que exige el spec. Se permite en `.editorconfig` con `dotnet_code_quality.CA1711.allowed_suffixes = Queue`.
16. **Data Protection:** con `PersistKeysToDbContext` las claves se leen al arrancar el host, antes de que los tests creen el esquema. En los tests se usa `UseEphemeralDataProtectionProvider()`, así no aparece el error "Key ring failed to load".
17. **Configuración en los tests:** `WebApplicationFactory` pasa a `Program` como argumentos de línea de comandos lo que el arnés configura con `UseSetting` y `UseEnvironment`. Igual, casi todo se lee con el Options pattern, en tiempo de ejecución. Al registrar servicios solo se leen dos cosas:
   - el entorno, para elegir las credenciales de OpenIddict;
   - el `ClientId` de Google, que además está en `appsettings.json`.
18. **Nombres:** un namespace `Email` taparía al value object `Email` en todo `Application.Abstractions` e `Infrastructure` (CS0118). Por eso las carpetas de emails se llaman `Emails/`.
19. **`ValidateOnBuild` en Development:** al construirse, el host verifica que se puedan crear todos los servicios registrados, y `dotnet ef` también construye el host. Cada dependencia de un handler se registra en la misma tarea que el handler o antes. Por eso `RequestInfo` (Api) se agrega en la Tarea 16, antes de la migración.
   Estado conocido: `OpenApiTests.Swagger_ui_and_openapi_document_are_served_in_development` levanta la Api en Development. Falla desde la Tarea 8 hasta la 15, porque `IIdentityService`, `IPermissionService` e `IRequestInfo` recién se registran en la 16. Es esperado; desde la Tarea 16 tiene que pasar. En la Tarea 18 ese test pasa a usar una base propia.
20. **Estilo de los tests:** es el mismo de la Fase 1:
   - `Guid.ToString("N", CultureInfo.InvariantCulture)` (CA1305);
   - las peticiones pasan por las extensiones de `HttpExtensions`, que arman una `Uri` relativa;
   - todo método que acepta un `CancellationToken` recibe `TestContext.Current.CancellationToken`.

## Decisiones del usuario (2026-09-19)

- **Admin inicial:** `Seed:AdminEmail = ezequielellena0003@gmail.com`.
- **Emails en desarrollo:** se guardan como archivos `.eml` en `src/ArquitecturaBase.Api/.emails/`, ignorada por git. El envío por Gmail con MailKit se implementa igual y se activa con `Email:Delivery = Smtp`.
- **Google:** con el cliente real. `ClientId = 830839449608-nkaial9hn903rhe38fukv12gmsn4bvhs.apps.googleusercontent.com`; el secreto lo carga el usuario.
- **Prueba manual:** con una colección de Postman versionada en `docs/postman/`.

## Desvíos respecto del spec, y por qué

- **`AddInfrastructure(IConfiguration, IHostEnvironment)`:** suma el entorno para elegir las credenciales de OpenIddict.
- **La construcción de los claims del token vive en la Api** (`Endpoints/Connect/OpenIdPrincipalFactory.cs`) y no en `Infrastructure/Identity/OpenIddict/ClaimsDestinations.cs`. La regla de arquitectura prohíbe que la Api use tipos de Infrastructure fuera de `Program.cs`; los endpoints de `/connect` obtienen los datos del usuario por `IIdentityService`.
- **Marcador `IPersistChangesOnFailure`:** los comandos que lo implementan también guardan cuando fallan. Así los intentos fallidos de un código y la auditoría quedan registrados aunque el caso de uso devuelva error.
- **Issuer:** no se configura en la Fase 2 (hecho verificado 9).
- **Seed de datos:** corre al iniciar en Development y en los tests. Producción queda para cuando haya pipeline, igual que las migraciones.
  - Está en `Persistence/Seed/`, en dos clases: `RoleSeeder` y `OpenIddictSeeder`.
  - Si la cuenta de `Seed:AdminEmail` ya existe cuando corre el seed, recibe el rol Admin ("admin inicial" del spec).
- **Nombres en Infrastructure:**
  - `Identity/OpenIddict/OpenIddictRegistration.cs` y `AuthServerDefaults.cs`, en lugar de `OpenIddictConfiguration.cs`. `ClaimsDestinations.cs` pasa a la Api (primer desvío).
  - Las carpetas de emails se llaman `Emails/` (hecho verificado 18).
- **`SignInWithExternalProvider` tiene validador** (el spec lista solo Command + Handler): valida el `returnUrl` con la misma regla que la verificación del código.
- **Errores del ingreso con Google:** el spec no dice qué hace el callback cuando falla. Como es una navegación del navegador, vuelve a `/login?error=<código>`. El SPA de la Fase 3 muestra el texto traducido por código.
- **Rate limiting:** las claves son planas (`RateLimiting:LoginCodePermitLimit`...). El límite de verificaciones por IP (30 cada 15 minutos) lo fija el plan, porque el spec no da un número.
- **Certificados de producción:** PFX con ruta y contraseña en `Authentication:Certificates:{Encryption|Signing}:{Path|Password}`.
- **Fuera de la Fase 2:** los encabezados de seguridad, HSTS y CSP (sección 6.9) van con el SPA en la Fase 3, cuando la Api sirva el front.

## Estructura de archivos nuevos

```
src/ArquitecturaBase.Domain/
  ValueObjects/Email.cs
  Users/UserErrors.cs
  Authorization/Permissions.cs · SystemRoles.cs
  Authentication/LoginCode.cs · LoginCodeErrors.cs · ILoginCodeRepository.cs
  Authentication/LoginAudit.cs · LoginMethod.cs · ILoginAuditRepository.cs
  Authentication/AccountErrors.cs · ExternalLoginErrors.cs
src/ArquitecturaBase.Application/
  Abstractions/Messaging/IPersistChangesOnFailure.cs
  Abstractions/Identity/IIdentityService.cs · UserAccount.cs · ExternalLogin.cs · IPermissionService.cs · IRequestInfo.cs
  Abstractions/Emails/IEmailSender.cs · IEmailQueue.cs · IEmailTemplateRenderer.cs · EmailMessage.cs
  Abstractions/Security/ILoginCodeGenerator.cs · ILoginCodeHasher.cs
  Features/Auth/LoginCodeOptions.cs · ReturnUrls.cs · UserCultures.cs
  Features/Auth/RequestLoginCode/            Command · Validator · Handler · Response
  Features/Auth/VerifyLoginCode/             Command · Validator · Handler · Response
  Features/Auth/SignInWithExternalProvider/  Command · Validator · Handler · Response
  Features/Users/GetCurrentUser/             Query · Handler · CurrentUserResponse
  Features/Users/GetUsers/                   Query · Validator · Handler · UserListItem
src/ArquitecturaBase.Infrastructure/
  Identity/ApplicationUser.cs · ApplicationRole.cs · IdentityRegistration.cs · IdentityService.cs · PermissionService.cs
  Identity/SeedOptions.cs · ExternalClaimTypes.cs · IdentityResultExtensions.cs
  Identity/OpenIddict/OpenIddictRegistration.cs · AuthServerDefaults.cs · WebClientOptions.cs
  Persistence/Configurations/ApplicationUserConfiguration.cs · ApplicationRoleConfiguration.cs · LoginCodeConfiguration.cs · LoginAuditConfiguration.cs
  Persistence/Repositories/LoginCodeRepository.cs · LoginAuditRepository.cs
  Persistence/Seed/RoleSeeder.cs · OpenIddictSeeder.cs · SeedExtensions.cs
  Persistence/Migrations/  (generadas)
  Security/LoginCodeGenerator.cs · LoginCodeHasher.cs · LoginCodeHashOptions.cs
  Emails/EmailOptions.cs · EmailDelivery.cs · SmtpOptions.cs · SmtpOptionsValidator.cs · EmailRegistration.cs
  Emails/MimeMessageFactory.cs · SmtpEmailSender.cs · PickupDirectoryEmailSender.cs · EmailQueue.cs · EmailBackgroundService.cs
  Emails/EmailTemplateRenderer.cs · Templates/_Layout.html · Templates/LoginCode.html   (recursos embebidos)
  Emails/Resources/Emails.resx · Emails.en.resx · EmailTexts.cs
src/ArquitecturaBase.Api/
  Services/RequestInfo.cs
  RateLimiting/RateLimitingOptions.cs · RateLimitingExtensions.cs
  Authorization/PermissionRequirement.cs · PermissionAuthorizationHandler.cs · PermissionPolicyProvider.cs · EndpointExtensions.cs
  Endpoints/Account/LoginCodeEndpoints.cs · ExternalLoginEndpoints.cs
  Endpoints/Connect/AuthorizeEndpoint.cs · TokenEndpoint.cs · LogoutEndpoint.cs · UserInfoEndpoint.cs
  Endpoints/Connect/OpenIdPrincipalFactory.cs · OpenIddictResults.cs
  Endpoints/Users/MeEndpoint.cs · UsersEndpoints.cs
tests/ArquitecturaBase.Application.UnitTests/
  TestDoubles/Auth/FakeIdentityService.cs · AuthFakes.cs
  Features/Auth/* · Features/Users/* · Resources/ErrorCodeTranslationTests.cs
tests/ArquitecturaBase.Api.IntegrationTests/
  Support/CapturingEmailSender.cs · TestEmails.cs · AuthFlow.cs · Pkce.cs
  TestFeatures/ExternalLoginTestEndpoints.cs
  Auth/* · Identity/* · Emails/* · Security/* · Users/* · Persistence/LoginCodeRepositoryTests.cs · IdentityModelTests.cs · MigrationsTests.cs
docs/postman/ArquitecturaBase.postman_collection.json · README.md
```

## Tareas

| # | Tarea | Tests |
|---|---|---|
| 1 | Value object `Email` y `UserErrors` | Domain |
| 2 | Catálogo de permisos y roles del sistema | Domain |
| 3 | Agregado `LoginCode` | Domain |
| 4 | `LoginAudit`, errores de cuenta y de ingreso externo | Domain |
| 5 | Comandos que guardan también al fallar | Application |
| 6 | Abstracciones de identidad, email y seguridad, y opciones del código | Application |
| 7 | Textos de los errores nuevos y test de traducciones | Application |
| 8 | Caso de uso: pedir código | Application |
| 9 | Caso de uso: verificar código | Application |
| 10 | Caso de uso: ingreso con proveedor externo | Application |
| 11 | Casos de uso: usuario actual y listado paginado | Application |
| 12 | Identity en el modelo: `ApplicationUser`, `ApplicationDbContext` y repositorios | Integración |
| 13 | Generador y hash HMAC de códigos | Integración (sin Api) |
| 14 | Plantillas de email y textos | Integración (sin Api) |
| 15 | Envío de emails: MailKit, archivos .eml, cola y reintentos | Integración (sin Api) |
| 16 | Identity: registro, `IdentityService`, `PermissionService` y seed de roles | Integración |
| 17 | OpenIddict: servidor, validación, credenciales y seed del cliente | Integración |
| 18 | Primera migración | Integración |
| 19 | Endpoints `/account/login-code` y rate limiting | Integración |
| 20 | Endpoints `/connect/*` y flujo completo con PKCE | Integración |
| 21 | Permisos, `/api/me` y `/api/users` | Integración |
| 22 | Ingreso con Google | Integración + manual |
| 23 | Auditoría, bloqueo y cuenta deshabilitada de punta a punta | Integración |
| 24 | Colección de Postman, README y CLAUDE.md | — |
| 25 | Verificación final | comandos + manual |

---

### Tarea 1: Value object `Email` y `UserErrors`

**Archivos:**
- Crear: `src/ArquitecturaBase.Domain/ValueObjects/Email.cs`, `src/ArquitecturaBase.Domain/Users/UserErrors.cs`
- Test: `tests/ArquitecturaBase.Domain.UnitTests/ValueObjects/EmailTests.cs`

- [ ] **Paso 1: tests que fallan**

`tests/ArquitecturaBase.Domain.UnitTests/ValueObjects/EmailTests.cs`:

```csharp
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Domain.UnitTests.ValueObjects;

public sealed class EmailTests
{
    [Fact]
    public void Email_is_trimmed_and_lowercased()
    {
        var result = Email.Create("  Ana.Perez@Example.COM ");

        Assert.True(result.IsSuccess);
        Assert.Equal("ana.perez@example.com", result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ana")]
    [InlineData("@example.com")]
    [InlineData("ana@")]
    [InlineData("ana@@example.com")]
    [InlineData("ana@example")]
    [InlineData("ana maria@example.com")]
    [InlineData("ana@example.com.")]
    public void Invalid_emails_are_rejected(string? value)
    {
        var result = Email.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.EmailInvalidCode, result.Error.Code);
    }

    [Fact]
    public void Emails_longer_than_the_limit_are_rejected()
    {
        var value = new string('a', Email.MaxLength - "@example.com".Length + 1) + "@example.com";

        Assert.True(Email.Create(value).IsFailure);
    }

    [Fact]
    public void Emails_with_the_same_normalized_value_are_equal()
    {
        Assert.Equal(Email.Create("ANA@example.com").Value, Email.Create("ana@example.com").Value);
    }
}
```

- [ ] **Paso 2: correr y ver que falla**

Run: `dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj`
Esperado: FALLA la compilación (`CS0234: ... 'ValueObjects' no existe`).

- [ ] **Paso 3: implementación**

`src/ArquitecturaBase.Domain/Users/UserErrors.cs`:

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Users;

public static class UserErrors
{
    public const string EmailInvalidCode = "Users.Email.Invalid";
    public const string NotFoundCode = "Users.User.NotFound";

    public static readonly Error EmailInvalid = Error.Validation(EmailInvalidCode, "The email address is not valid.");

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The user was not found.");
}
```

`src/ArquitecturaBase.Domain/ValueObjects/Email.cs`:

```csharp
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Domain.ValueObjects;

/// <summary>Email normalizado (sin espacios alrededor y en minúsculas) con un formato básico validado.</summary>
public sealed class Email : ValueObject
{
    public const int MaxLength = 254;

    private Email(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<Email> Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxLength || !HasValidFormat(normalized))
        {
            return UserErrors.EmailInvalid;
        }

        return new Email(normalized);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    private static bool HasValidFormat(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);

        return at > 0
            && at == value.LastIndexOf('@')
            && at < value.Length - 1
            && !value.Any(char.IsWhiteSpace)
            && value[(at + 1)..].Contains('.', StringComparison.Ordinal)
            && !value.EndsWith('.');
    }
}
```

- [ ] **Paso 4: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj`
Esperado: todos en verde (13 tests nuevos). `dotnet build ArquitecturaBase.slnx` con 0 advertencias. Los tests de arquitectura siguen en verde: Domain solo usa la BCL.

- [ ] **Paso 5: commit**

```bash
git add src/ArquitecturaBase.Domain tests/ArquitecturaBase.Domain.UnitTests
git commit -m "feat: agregar el value object Email y los errores de usuario" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 2: Catálogo de permisos y roles del sistema

**Archivos:**
- Crear: `src/ArquitecturaBase.Domain/Authorization/Permissions.cs`, `SystemRoles.cs`
- Test: `tests/ArquitecturaBase.Domain.UnitTests/Authorization/PermissionsTests.cs`

- [ ] **Paso 1: tests que fallan**

`tests/ArquitecturaBase.Domain.UnitTests/Authorization/PermissionsTests.cs`:

```csharp
using System.Text.RegularExpressions;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Domain.UnitTests.Authorization;

public sealed partial class PermissionsTests
{
    [Fact]
    public void All_lists_every_permission_once()
    {
        Assert.Equal(
            [Permissions.Users.Read, Permissions.Users.Manage, Permissions.Roles.Read, Permissions.Roles.Manage],
            Permissions.All);
        Assert.Equal(Permissions.All.Count, Permissions.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Permissions_follow_the_area_action_format()
    {
        Assert.All(Permissions.All, permission => Assert.Matches(PermissionFormat(), permission));
    }

    [Fact]
    public void System_roles_are_admin_and_user()
    {
        Assert.Equal([SystemRoles.Admin, SystemRoles.User], SystemRoles.All);
    }

    [GeneratedRegex("^[a-z]+\\.[a-z]+$")]
    private static partial Regex PermissionFormat();
}
```

Nota: si `Assert.Equal([..], ...)` resulta ambiguo, usar `new[] { ... }`.

- [ ] **Paso 2: correr y ver que falla**

Run: `dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj`
Esperado: FALLA la compilación (`'Authorization' no existe`).

- [ ] **Paso 3: implementación**

`src/ArquitecturaBase.Domain/Authorization/Permissions.cs`:

```csharp
namespace ArquitecturaBase.Domain.Authorization;

/// <summary>
/// Catálogo de permisos. Cada permiso se guarda como role claim de tipo <see cref="ClaimType"/> y un usuario suma
/// los de todos sus roles. Los endpoints piden permisos, nunca roles.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    public static class Users
    {
        public const string Read = "users.read";
        public const string Manage = "users.manage";
    }

    public static class Roles
    {
        public const string Read = "roles.read";
        public const string Manage = "roles.manage";
    }

    public static IReadOnlyCollection<string> All { get; } = [Users.Read, Users.Manage, Roles.Read, Roles.Manage];
}
```

`src/ArquitecturaBase.Domain/Authorization/SystemRoles.cs`:

```csharp
namespace ArquitecturaBase.Domain.Authorization;

/// <summary>Roles que existen siempre. Admin recibe todos los permisos; User, ninguno por ahora.</summary>
public static class SystemRoles
{
    public const string Admin = "Admin";
    public const string User = "User";

    public static IReadOnlyCollection<string> All { get; } = [Admin, User];
}
```

- [ ] **Paso 4: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj`
Esperado: todos en verde (3 nuevos).

- [ ] **Paso 5: commit**

```bash
git add src/ArquitecturaBase.Domain/Authorization tests/ArquitecturaBase.Domain.UnitTests/Authorization
git commit -m "feat: agregar el catálogo de permisos y los roles del sistema" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 3: Agregado `LoginCode`

Reglas de la sección 5.3 del spec:
- solo se guarda el hash;
- vence en 10 minutos;
- admite 5 intentos: al 5.º fallo queda bloqueado;
- es de un solo uso;
- pedir uno nuevo invalida los anteriores.

`Verify(hash, now)` devuelve `Auth.LoginCode.Invalid` (con `attemptsLeft`), `Expired`, `AlreadyUsed` o `TooManyAttempts`.

**Archivos:**
- Crear: `src/ArquitecturaBase.Domain/Authentication/LoginCode.cs`, `LoginCodeErrors.cs`, `ILoginCodeRepository.cs`
- Test: `tests/ArquitecturaBase.Domain.UnitTests/Authentication/LoginCodeTests.cs`

- [ ] **Paso 1: tests que fallan**

`tests/ArquitecturaBase.Domain.UnitTests/Authentication/LoginCodeTests.cs`:

```csharp
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Domain.UnitTests.Authentication;

public sealed class LoginCodeTests
{
    private const string Hash = "HASH-OK";
    private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private static LoginCode Issue() =>
        LoginCode.Issue(Email.Create("ana@example.com").Value, Hash, Now, Lifetime, maxAttempts: 5);

    [Fact]
    public void Issued_code_is_active_until_it_expires()
    {
        var code = Issue();

        Assert.Equal("ana@example.com", code.Email);
        Assert.Equal(Now + Lifetime, code.ExpiresAtUtc);
        Assert.True(code.IsActive(Now));
        Assert.False(code.IsActive(Now + Lifetime));
    }

    [Fact]
    public void Issue_rejects_invalid_arguments()
    {
        var email = Email.Create("ana@example.com").Value;

        Assert.Throws<ArgumentOutOfRangeException>(() => LoginCode.Issue(email, Hash, Now, TimeSpan.Zero, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => LoginCode.Issue(email, Hash, Now, Lifetime, 0));
        Assert.Throws<ArgumentException>(() => LoginCode.Issue(email, " ", Now, Lifetime, 5));
    }

    [Fact]
    public void Correct_code_is_consumed()
    {
        var code = Issue();

        var result = code.Verify(Hash, Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddMinutes(1), code.ConsumedAtUtc);
        Assert.False(code.IsActive(Now.AddMinutes(1)));
    }

    [Fact]
    public void Wrong_code_counts_the_attempt_and_reports_the_attempts_left()
    {
        var code = Issue();

        var result = code.Verify("HASH-WRONG", Now);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Equal(4, result.Error.Metadata![LoginCodeErrors.AttemptsLeftKey]);
        Assert.Equal(1, code.FailedAttempts);
    }

    [Fact]
    public void Fifth_failure_blocks_the_code_even_for_the_right_hash()
    {
        var code = Issue();

        for (var i = 0; i < 4; i++)
        {
            code.Verify("HASH-WRONG", Now);
        }

        var fifth = code.Verify("HASH-WRONG", Now);
        var afterwards = code.Verify(Hash, Now);

        Assert.Equal(LoginCodeErrors.TooManyAttemptsCode, fifth.Error.Code);
        Assert.Equal(LoginCodeErrors.TooManyAttemptsCode, afterwards.Error.Code);
        Assert.Null(code.ConsumedAtUtc);
    }

    [Fact]
    public void Used_code_cannot_be_used_again()
    {
        var code = Issue();
        code.Verify(Hash, Now);

        var result = code.Verify(Hash, Now);

        Assert.Equal(LoginCodeErrors.AlreadyUsedCode, result.Error.Code);
    }

    [Fact]
    public void Expired_code_is_rejected_without_counting_an_attempt()
    {
        var code = Issue();

        var result = code.Verify("HASH-WRONG", Now + Lifetime);

        Assert.Equal(LoginCodeErrors.ExpiredCode, result.Error.Code);
        Assert.Equal(0, code.FailedAttempts);
    }

    [Fact]
    public void Invalidated_code_behaves_like_a_wrong_code()
    {
        var code = Issue();
        code.Invalidate(Now.AddMinutes(1));
        code.Invalidate(Now.AddMinutes(2));

        var result = code.Verify(Hash, Now.AddMinutes(3));

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Null(result.Error.Metadata);
        Assert.Equal(Now.AddMinutes(1), code.InvalidatedAtUtc);
        Assert.False(code.IsActive(Now.AddMinutes(3)));
    }
}
```

- [ ] **Paso 2: correr y ver que falla**

Run: `dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj`
Esperado: FALLA la compilación (`'Authentication' no existe`).

- [ ] **Paso 3: implementación**

`src/ArquitecturaBase.Domain/Authentication/LoginCodeErrors.cs`:

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authentication;

public static class LoginCodeErrors
{
    public const string InvalidCode = "Auth.LoginCode.Invalid";
    public const string ExpiredCode = "Auth.LoginCode.Expired";
    public const string AlreadyUsedCode = "Auth.LoginCode.AlreadyUsed";
    public const string TooManyAttemptsCode = "Auth.LoginCode.TooManyAttempts";
    public const string ResendTooSoonCode = "Auth.LoginCode.ResendTooSoon";
    public const string TooManyRequestsCode = "Auth.LoginCode.TooManyRequests";

    public const string AttemptsLeftKey = "attemptsLeft";
    public const string RetryAfterKey = "retryAfter";

    public static readonly Error Expired = Error.Validation(ExpiredCode, "The code has expired.");

    public static readonly Error AlreadyUsed = Error.Validation(AlreadyUsedCode, "The code has already been used.");

    public static readonly Error TooManyAttempts = Error.Validation(TooManyAttemptsCode, "Too many failed attempts for this code.");

    /// <summary>Código incorrecto o inexistente. Sin <paramref name="attemptsLeft"/> cuando no hay un código activo.</summary>
    public static Error Invalid(int? attemptsLeft) =>
        Error.Validation(
            InvalidCode,
            "The code is not valid.",
            attemptsLeft is null ? null : new Dictionary<string, object?> { [AttemptsLeftKey] = attemptsLeft });

    public static Error ResendTooSoon(int retryAfterSeconds) =>
        Error.TooManyRequests(
            ResendTooSoonCode,
            "A new code can't be requested yet.",
            new Dictionary<string, object?> { [RetryAfterKey] = retryAfterSeconds });

    public static Error TooManyRequests(int retryAfterSeconds) =>
        Error.TooManyRequests(
            TooManyRequestsCode,
            "Too many codes were requested.",
            new Dictionary<string, object?> { [RetryAfterKey] = retryAfterSeconds });
}
```

`src/ArquitecturaBase.Domain/Authentication/LoginCode.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Domain.Authentication;

/// <summary>
/// Código de ingreso de un solo uso enviado por email (sección 5.3 del spec). Solo se guarda su hash.
/// Vence, admite una cantidad máxima de intentos y queda invalidado cuando se pide otro.
/// </summary>
public sealed class LoginCode : AggregateRoot
{
    // Para EF Core.
    private LoginCode()
    {
        Email = string.Empty;
        CodeHash = string.Empty;
    }

    private LoginCode(string email, string codeHash, DateTime createdAtUtc, DateTime expiresAtUtc, int maxAttempts)
    {
        Email = email;
        CodeHash = codeHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        MaxAttempts = maxAttempts;
    }

    public string Email { get; private set; }

    public string CodeHash { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public DateTime? InvalidatedAtUtc { get; private set; }

    public int FailedAttempts { get; private set; }

    public int MaxAttempts { get; private set; }

    public int AttemptsLeft => Math.Max(0, MaxAttempts - FailedAttempts);

    public static LoginCode Issue(Email email, string codeHash, DateTime nowUtc, TimeSpan lifetime, int maxAttempts)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        return new LoginCode(email.Value, codeHash, nowUtc, nowUtc + lifetime, maxAttempts);
    }

    public bool IsActive(DateTime nowUtc) =>
        ConsumedAtUtc is null && InvalidatedAtUtc is null && nowUtc < ExpiresAtUtc && FailedAttempts < MaxAttempts;

    /// <summary>Lo invalida porque se pidió un código nuevo. Conserva el primer momento de invalidación.</summary>
    public void Invalidate(DateTime nowUtc)
    {
        InvalidatedAtUtc ??= nowUtc;
    }

    public Result Verify(string codeHash, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(codeHash);

        if (ConsumedAtUtc is not null)
        {
            return LoginCodeErrors.AlreadyUsed;
        }

        if (InvalidatedAtUtc is not null)
        {
            return LoginCodeErrors.Invalid(attemptsLeft: null);
        }

        if (nowUtc >= ExpiresAtUtc)
        {
            return LoginCodeErrors.Expired;
        }

        if (FailedAttempts >= MaxAttempts)
        {
            return LoginCodeErrors.TooManyAttempts;
        }

        // Comparación en tiempo constante: no revela cuántos caracteres coinciden.
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(CodeHash), Encoding.UTF8.GetBytes(codeHash)))
        {
            FailedAttempts++;

            return FailedAttempts >= MaxAttempts ? LoginCodeErrors.TooManyAttempts : LoginCodeErrors.Invalid(AttemptsLeft);
        }

        ConsumedAtUtc = nowUtc;

        return Result.Success();
    }
}
```

`src/ArquitecturaBase.Domain/Authentication/ILoginCodeRepository.cs`:

```csharp
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Domain.Authentication;

public interface ILoginCodeRepository
{
    /// <summary>El último código de ese email que no fue reemplazado por uno nuevo (puede estar vencido o usado).</summary>
    Task<LoginCode?> GetLatestAsync(Email email, CancellationToken cancellationToken);

    /// <summary>Los códigos todavía activos de ese email, para invalidarlos cuando se pide uno nuevo.</summary>
    Task<IReadOnlyList<LoginCode>> ListActiveAsync(Email email, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Cuándo se pidió cada código de ese email desde <paramref name="sinceUtc"/>, del más viejo al más nuevo.</summary>
    Task<IReadOnlyList<DateTime>> ListRequestTimesSinceAsync(Email email, DateTime sinceUtc, CancellationToken cancellationToken);

    void Add(LoginCode loginCode);
}
```

- [ ] **Paso 4: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj`
Esperado: todos en verde (8 nuevos). Tests de arquitectura en verde: `System.Security.Cryptography` es parte del framework compartido.

- [ ] **Paso 5: commit**

```bash
git add src/ArquitecturaBase.Domain/Authentication tests/ArquitecturaBase.Domain.UnitTests/Authentication
git commit -m "feat: agregar el agregado LoginCode con sus reglas de vencimiento, intentos y uso único" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 4: `LoginAudit`, errores de cuenta y de ingreso externo

Cada intento de ingreso, exitoso o fallido, se registra con email, usuario, método, resultado, motivo, IP, user agent y `OccurredAtUtc`. Nunca se registra el código.

**Archivos:**
- Crear: `src/ArquitecturaBase.Domain/Authentication/LoginAudit.cs`, `LoginMethod.cs`, `ILoginAuditRepository.cs`, `AccountErrors.cs`, `ExternalLoginErrors.cs`
- Test: `tests/ArquitecturaBase.Domain.UnitTests/Authentication/LoginAuditTests.cs`

- [ ] **Paso 1: tests que fallan**

`tests/ArquitecturaBase.Domain.UnitTests/Authentication/LoginAuditTests.cs`:

```csharp
using ArquitecturaBase.Domain.Authentication;

namespace ArquitecturaBase.Domain.UnitTests.Authentication;

public sealed class LoginAuditTests
{
    private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Success_records_the_user_and_no_reason()
    {
        var userId = Guid.CreateVersion7();

        var audit = LoginAudit.Success("ana@example.com", userId, LoginMethod.Code, "10.0.0.1", "Firefox", Now);

        Assert.True(audit.Succeeded);
        Assert.Equal(userId, audit.UserId);
        Assert.Equal(LoginMethod.Code, audit.Method);
        Assert.Null(audit.FailureReason);
        Assert.Equal("10.0.0.1", audit.IpAddress);
        Assert.Equal(Now, audit.OccurredAtUtc);
    }

    [Fact]
    public void Failure_records_the_reason()
    {
        var audit = LoginAudit.Failure("ana@example.com", null, LoginMethod.Google, "Auth.LoginCode.Invalid", null, null, Now);

        Assert.False(audit.Succeeded);
        Assert.Null(audit.UserId);
        Assert.Equal("Auth.LoginCode.Invalid", audit.FailureReason);
    }

    [Fact]
    public void Failure_requires_a_reason()
    {
        Assert.Throws<ArgumentException>(() =>
            LoginAudit.Failure("ana@example.com", null, LoginMethod.Code, " ", null, null, Now));
    }

    [Fact]
    public void Long_user_agents_are_truncated()
    {
        var audit = LoginAudit.Success(
            "ana@example.com", Guid.CreateVersion7(), LoginMethod.Code, null, new string('x', 2000), Now);

        Assert.Equal(LoginAudit.MaxUserAgentLength, audit.UserAgent!.Length);
    }
}
```

- [ ] **Paso 2: correr y ver que falla**

Run: `dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj`
Esperado: FALLA la compilación (`'LoginAudit' no se encontró`).

- [ ] **Paso 3: implementación**

`src/ArquitecturaBase.Domain/Authentication/LoginMethod.cs`:

```csharp
namespace ArquitecturaBase.Domain.Authentication;

public enum LoginMethod
{
    Code = 1,
    Google = 2,
}
```

`src/ArquitecturaBase.Domain/Authentication/LoginAudit.cs`:

```csharp
using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Authentication;

/// <summary>Registro de un intento de ingreso, exitoso o fallido. Nunca guarda el código.</summary>
public sealed class LoginAudit : Entity
{
    public const int MaxUserAgentLength = 512;

    // Para EF Core.
    private LoginAudit()
    {
        Email = string.Empty;
    }

    private LoginAudit(
        string email,
        Guid? userId,
        LoginMethod method,
        bool succeeded,
        string? failureReason,
        string? ipAddress,
        string? userAgent,
        DateTime occurredAtUtc)
    {
        Email = email;
        UserId = userId;
        Method = method;
        Succeeded = succeeded;
        FailureReason = failureReason;
        IpAddress = ipAddress;
        UserAgent = userAgent is { Length: > MaxUserAgentLength } ? userAgent[..MaxUserAgentLength] : userAgent;
        OccurredAtUtc = occurredAtUtc;
    }

    public string Email { get; private set; }

    public Guid? UserId { get; private set; }

    public LoginMethod Method { get; private set; }

    public bool Succeeded { get; private set; }

    public string? FailureReason { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public static LoginAudit Success(
        string email, Guid userId, LoginMethod method, string? ipAddress, string? userAgent, DateTime occurredAtUtc) =>
        new(email, userId, method, succeeded: true, failureReason: null, ipAddress, userAgent, occurredAtUtc);

    public static LoginAudit Failure(
        string email,
        Guid? userId,
        LoginMethod method,
        string failureReason,
        string? ipAddress,
        string? userAgent,
        DateTime occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        return new(email, userId, method, succeeded: false, failureReason, ipAddress, userAgent, occurredAtUtc);
    }
}
```

`src/ArquitecturaBase.Domain/Authentication/ILoginAuditRepository.cs`:

```csharp
namespace ArquitecturaBase.Domain.Authentication;

public interface ILoginAuditRepository
{
    void Add(LoginAudit audit);
}
```

`src/ArquitecturaBase.Domain/Authentication/AccountErrors.cs`:

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authentication;

public static class AccountErrors
{
    public const string DisabledCode = "Auth.Account.Disabled";
    public const string LockedOutCode = "Auth.Account.LockedOut";

    /// <summary>Cuenta deshabilitada. Se informa después de verificar el código: el usuario ya probó que el email es suyo.</summary>
    public static readonly Error Disabled = Error.Forbidden(DisabledCode, "The account is disabled.");

    /// <summary>Bloqueo de Identity por verificaciones fallidas seguidas.</summary>
    public static readonly Error LockedOut = Error.TooManyRequests(LockedOutCode, "The account is temporarily locked.");
}
```

`src/ArquitecturaBase.Domain/Authentication/ExternalLoginErrors.cs`:

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authentication;

public static class ExternalLoginErrors
{
    public const string FailedCode = "Auth.ExternalLogin.Failed";
    public const string EmailNotVerifiedCode = "Auth.ExternalLogin.EmailNotVerified";

    public static readonly Error Failed = Error.Unauthorized(FailedCode, "The external sign-in could not be completed.");

    public static readonly Error EmailNotVerified = Error.Forbidden(
        EmailNotVerifiedCode, "The external provider did not verify the email address.");
}
```

- [ ] **Paso 4: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj`
Esperado: todos en verde (4 nuevos).

- [ ] **Paso 5: commit**

```bash
git add src/ArquitecturaBase.Domain/Authentication tests/ArquitecturaBase.Domain.UnitTests/Authentication
git commit -m "feat: agregar la auditoría de ingresos y los errores de cuenta e ingreso externo" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 5: Comandos que guardan también al fallar

Un código incorrecto tiene que quedar registrado: suma un intento fallido y un registro de auditoría. El caso de uso devuelve error, y hoy el `UnitOfWorkDecorator` solo guarda si hubo éxito. La marca `IPersistChangesOnFailure` hace que esos comandos guarden igual.

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Abstractions/Messaging/IPersistChangesOnFailure.cs`
- Modificar: `src/ArquitecturaBase.Application/Abstractions/Behaviors/UnitOfWorkDecorator.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Ping.cs`
- Test: `tests/ArquitecturaBase.Application.UnitTests/Abstractions/Behaviors/UnitOfWorkDecoratorTests.cs`

- [ ] **Paso 1: dobles de prueba y tests que fallan**

Agregar al final de `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Ping.cs`:

```csharp
internal sealed record PingPersistentCommand(string? Message) : ICommand<string>, IPersistChangesOnFailure;

internal sealed record PingPersistentBaseCommand(string? Message) : ICommand, IPersistChangesOnFailure;

internal sealed class PingPersistentCommandHandler : ICommandHandler<PingPersistentCommand, string>
{
    public Task<Result<string>> Handle(PingPersistentCommand command, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>(
            command.Message == PingErrors.FailMessage ? PingErrors.Failed : "pong: " + command.Message);
}

internal sealed class PingPersistentBaseCommandHandler : ICommandHandler<PingPersistentBaseCommand>
{
    public Task<Result> Handle(PingPersistentBaseCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(command.Message == PingErrors.FailMessage ? Result.Failure(PingErrors.Failed) : Result.Success());
}
```

Agregar a `UnitOfWorkDecoratorTests`:

```csharp
    [Fact]
    public async Task Failed_command_that_persists_changes_on_failure_still_saves()
    {
        var unitOfWork = new FakeUnitOfWork();
        var decorator = new UnitOfWorkDecorator.CommandHandler<PingPersistentCommand, string>(
            new PingPersistentCommandHandler(), unitOfWork);

        var result = await decorator.Handle(new PingPersistentCommand(PingErrors.FailMessage), Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Failed_command_without_response_that_persists_changes_on_failure_still_saves()
    {
        var unitOfWork = new FakeUnitOfWork();
        var decorator = new UnitOfWorkDecorator.CommandBaseHandler<PingPersistentBaseCommand>(
            new PingPersistentBaseCommandHandler(), unitOfWork);

        await decorator.Handle(new PingPersistentBaseCommand(PingErrors.FailMessage), Ct);

        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }
```

- [ ] **Paso 2: correr y ver que falla**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: FALLA la compilación (`'IPersistChangesOnFailure' no se encontró`).

- [ ] **Paso 3: implementación**

`src/ArquitecturaBase.Application/Abstractions/Messaging/IPersistChangesOnFailure.cs`:

```csharp
namespace ArquitecturaBase.Application.Abstractions.Messaging;

/// <summary>
/// Marca un comando cuyos cambios se guardan aunque el resultado sea un error. Por ejemplo, los intentos fallidos
/// de un código de ingreso y su auditoría tienen que quedar registrados.
/// </summary>
public interface IPersistChangesOnFailure;
```

En `UnitOfWorkDecorator.cs`:
- En las dos clases, reemplazar `if (result.IsSuccess)` por `if (result.IsSuccess || command is IPersistChangesOnFailure)`.
- Actualizar el resumen de la clase:

```csharp
/// <summary>
/// Guarda los cambios si el comando terminó bien, o siempre si el comando implementa
/// <see cref="IPersistChangesOnFailure"/>. Las consultas no pasan por acá.
/// </summary>
```

- [ ] **Paso 4: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: todos en verde (2 nuevos). `Decorators_are_not_registered_as_handlers` sigue en verde.

- [ ] **Paso 5: commit**

```bash
git add src/ArquitecturaBase.Application tests/ArquitecturaBase.Application.UnitTests
git commit -m "feat: permitir que un comando guarde sus cambios aunque falle" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 6: Abstracciones de identidad, email y seguridad, y opciones del código

Application habla con Identity, con el email y con la criptografía solo mediante interfaces. Esta tarea agrega esas interfaces, los DTOs, las opciones del código y la regla de `returnUrl`. La regla tiene lógica, así que va con test.

**Archivos:**
- Crear en `src/ArquitecturaBase.Application/`:
  - `Abstractions/Identity/UserAccount.cs`, `ExternalLogin.cs`, `IIdentityService.cs`, `IPermissionService.cs`, `IRequestInfo.cs`
  - `Abstractions/Emails/EmailMessage.cs`, `IEmailSender.cs`, `IEmailQueue.cs`, `IEmailTemplateRenderer.cs`
  - `Abstractions/Security/ILoginCodeGenerator.cs`, `ILoginCodeHasher.cs`
  - `Features/Auth/LoginCodeOptions.cs`, `Features/Auth/ReturnUrls.cs`
  - `Features/Users/GetUsers/UserListItem.cs`
- Modificar: `src/ArquitecturaBase.Application/DependencyInjection.cs`, Application csproj, `Directory.Packages.props`, `.editorconfig`
- Test: `tests/ArquitecturaBase.Application.UnitTests/Features/Auth/ReturnUrlsTests.cs`

- [ ] **Paso 1: paquetes y regla de CA1711**

`Directory.Packages.props`, en el grupo `Application`:

```xml
    <PackageVersion Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Options.DataAnnotations" Version="10.0.12" />
```

Application csproj: `<PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />` y `<PackageReference Include="Microsoft.Extensions.Options.DataAnnotations" />`.

`.editorconfig`, en la sección `[*.cs]`, debajo de CA1716:

```ini
# CA1711: el spec exige IEmailQueue; el sufijo Queue describe exactamente lo que es.
dotnet_code_quality.CA1711.allowed_suffixes = Queue
```

- [ ] **Paso 2: test que falla**

`tests/ArquitecturaBase.Application.UnitTests/Features/Auth/ReturnUrlsTests.cs`:

```csharp
using ArquitecturaBase.Application.Features.Auth;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

public sealed class ReturnUrlsTests
{
    [Theory]
    [InlineData("/connect/authorize")]
    [InlineData("/connect/authorize?client_id=web&redirect_uri=https://localhost:5173/auth/callback&state=x")]
    public void Local_authorize_requests_are_accepted(string returnUrl)
    {
        Assert.True(ReturnUrls.IsAuthorizeRequest(returnUrl));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/login")]
    [InlineData("https://evil.example/connect/authorize")]
    [InlineData("//evil.example/connect/authorize")]
    [InlineData("/connect/authorizeX")]
    [InlineData("/connect/authorize/../../evil")]
    [InlineData("/connect/authorize?x=1\r\nSet-Cookie: a=b")]
    public void Anything_else_is_rejected(string? returnUrl)
    {
        Assert.False(ReturnUrls.IsAuthorizeRequest(returnUrl));
    }
}
```

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: FALLA la compilación (`'Features' no existe`).

- [ ] **Paso 3: abstracciones de identidad**

`Abstractions/Identity/UserAccount.cs`:

```csharp
namespace ArquitecturaBase.Application.Abstractions.Identity;

/// <summary>Los datos del usuario que necesitan los casos de uso, sin exponer el modelo de Identity.</summary>
public sealed record UserAccount(
    Guid Id,
    string Email,
    string? DisplayName,
    string Culture,
    string TimeZoneId,
    bool IsActive);
```

`Abstractions/Identity/ExternalLogin.cs`:

```csharp
namespace ArquitecturaBase.Application.Abstractions.Identity;

/// <summary>Lo que devolvió el proveedor externo (por ejemplo, Google) al volver del challenge.</summary>
public sealed record ExternalLogin(
    string Provider,
    string ProviderKey,
    string? Email,
    bool EmailVerified,
    string? DisplayName);
```

`Abstractions/Identity/IIdentityService.cs`:

```csharp
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Abstractions.Identity;

/// <summary>
/// Acceso a usuarios, roles y sesión. Lo implementa Infrastructure sobre ASP.NET Core Identity:
/// Domain y Application no dependen del framework.
/// </summary>
public interface IIdentityService
{
    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserAccount?> FindByEmailAsync(Email email, CancellationToken cancellationToken);

    Task<UserAccount?> FindByExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken);

    /// <summary>
    /// Crea el usuario con el email confirmado, porque ambos ingresos lo verifican. Le asigna el rol Admin si es el
    /// email configurado en Seed:AdminEmail, y User si no. Si Identity lo rechaza lanza una excepción: el email ya
    /// está validado y el caso de uso buscó antes al usuario, así que un rechazo es un error de programación.
    /// </summary>
    Task<UserAccount> CreateAsync(Email email, string? displayName, string culture, CancellationToken cancellationToken);

    Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Suma una verificación fallida; al llegar al máximo, Identity bloquea la cuenta un tiempo.</summary>
    Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken);

    Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Inicia la sesión del servidor: la cookie persistente de Identity.</summary>
    Task SignInAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Lee el resultado del proveedor externo; null si no hay un ingreso externo en curso.</summary>
    Task<ExternalLogin?> GetExternalLoginAsync(CancellationToken cancellationToken);

    Task SignOutExternalAsync(CancellationToken cancellationToken);

    Task<PagedResult<UserListItem>> ListUsersAsync(PagedRequest request, CancellationToken cancellationToken);
}
```

`Abstractions/Identity/IPermissionService.cs`:

```csharp
namespace ArquitecturaBase.Application.Abstractions.Identity;

/// <summary>Permisos efectivos de un usuario: la suma de los permisos de sus roles.</summary>
public interface IPermissionService
{
    Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken);

    /// <summary>Descarta los permisos en caché de los usuarios de ese rol. Se llama cuando cambia el rol.</summary>
    Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken);
}
```

`Abstractions/Identity/IRequestInfo.cs`:

```csharp
namespace ArquitecturaBase.Application.Abstractions.Identity;

/// <summary>Datos del cliente de la petición actual, para la auditoría de ingresos.</summary>
public interface IRequestInfo
{
    string? IpAddress { get; }

    string? UserAgent { get; }
}
```

`Features/Users/GetUsers/UserListItem.cs`:

```csharp
namespace ArquitecturaBase.Application.Features.Users.GetUsers;

public sealed record UserListItem(Guid Id, string Email, string? DisplayName, bool IsActive, DateTime CreatedAtUtc);
```

- [ ] **Paso 4: abstracciones de email y seguridad**

`Abstractions/Emails/EmailMessage.cs`:

```csharp
namespace ArquitecturaBase.Application.Abstractions.Emails;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);
```

`Abstractions/Emails/IEmailSender.cs`:

```csharp
namespace ArquitecturaBase.Application.Abstractions.Emails;

/// <summary>Envía un email. Cambiar de proveedor es escribir otra implementación.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
```

`Abstractions/Emails/IEmailQueue.cs`:

```csharp
namespace ArquitecturaBase.Application.Abstractions.Emails;

/// <summary>Encola un email para enviarlo en segundo plano: el ingreso no espera al servidor de correo.</summary>
public interface IEmailQueue
{
    ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken);
}
```

`Abstractions/Emails/IEmailTemplateRenderer.cs`:

```csharp
using System.Globalization;

namespace ArquitecturaBase.Application.Abstractions.Emails;

public interface IEmailTemplateRenderer
{
    /// <summary>Email con el código de ingreso, con los textos en <paramref name="culture"/>.</summary>
    EmailMessage RenderLoginCode(string to, string code, int lifetimeMinutes, CultureInfo culture);
}
```

`Abstractions/Security/ILoginCodeGenerator.cs`:

```csharp
namespace ArquitecturaBase.Application.Abstractions.Security;

public interface ILoginCodeGenerator
{
    /// <summary>Un código numérico aleatorio con la cantidad de dígitos configurada.</summary>
    string Generate();
}
```

`Abstractions/Security/ILoginCodeHasher.cs`:

```csharp
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Abstractions.Security;

public interface ILoginCodeHasher
{
    /// <summary>Hash del código atado al email: el mismo código para otro email da otro hash.</summary>
    string Hash(Email email, string code);
}
```

- [ ] **Paso 5: opciones y regla de `returnUrl`**

`Features/Auth/LoginCodeOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Application.Features.Auth;

/// <summary>Reglas del código de ingreso (sección 5.3 del spec), en Authentication:LoginCode.</summary>
public sealed class LoginCodeOptions
{
    public const string SectionName = "Authentication:LoginCode";

    [Range(4, 10)]
    public int Length { get; init; } = 6;

    [Range(1, 60)]
    public int LifetimeMinutes { get; init; } = 10;

    [Range(1, 20)]
    public int MaxAttempts { get; init; } = 5;

    [Range(0, 3600)]
    public int ResendCooldownSeconds { get; init; } = 60;

    [Range(1, 100)]
    public int MaxRequestsPerWindow { get; init; } = 5;

    [Range(1, 1440)]
    public int RequestWindowMinutes { get; init; } = 15;
}
```

`Features/Auth/ReturnUrls.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace ArquitecturaBase.Application.Features.Auth;

/// <summary>
/// El returnUrl de un ingreso tiene que ser una ruta local al endpoint de autorización, para evitar redirecciones
/// abiertas (sección 5.2 del spec).
/// </summary>
public static class ReturnUrls
{
    public const string AuthorizePath = "/connect/authorize";

    /// <summary>Página de login del SPA: el backend manda ahí cuando falta la sesión o falla un ingreso externo.</summary>
    public const string LoginPath = "/login";

    public static bool IsAuthorizeRequest([NotNullWhen(true)] string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl.StartsWith(AuthorizePath, StringComparison.Ordinal)
        && (returnUrl.Length == AuthorizePath.Length || returnUrl[AuthorizePath.Length] == '?')
        && !returnUrl.Any(char.IsControl);
}
```

En `src/ArquitecturaBase.Application/DependencyInjection.cs`, `AddApplication` pasa a registrar las opciones (con `using ArquitecturaBase.Application.Features.Auth;`):

```csharp
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<LoginCodeOptions>()
            .BindConfiguration(LoginCodeOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services.AddFeaturesFromAssembly(typeof(DependencyInjection).Assembly);
    }
```

- [ ] **Paso 6: correr y ver que pasa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

Esperado: 0 advertencias. Todo en verde (10 nuevos en `ReturnUrlsTests`). La regla de arquitectura de Application sigue en verde: nada de `Microsoft.AspNetCore`.

- [ ] **Paso 7: commit**

```bash
git add Directory.Packages.props .editorconfig src/ArquitecturaBase.Application tests/ArquitecturaBase.Application.UnitTests
git commit -m "feat: agregar las abstracciones de identidad, email y seguridad para el ingreso" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 7: Textos de los errores nuevos y test de traducciones

Resuelve el pendiente 7 de la Fase 1: un test verifica que cada código de error declarado en Domain y en Application tenga texto en los dos idiomas.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Resources/Errors.resx`, `Errors.en.resx`, `Validation.resx`, `Validation.en.resx`, `ValidationMessages.cs`
- Test: `tests/ArquitecturaBase.Application.UnitTests/Resources/ErrorCodeTranslationTests.cs`

- [ ] **Paso 1: test que falla**

`tests/ArquitecturaBase.Application.UnitTests/Resources/ErrorCodeTranslationTests.cs`:

```csharp
using System.Globalization;
using System.Reflection;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Resources;

/// <summary>
/// Cada código declarado en una clase *Errors (constantes públicas terminadas en "Code") tiene su texto en español
/// y en inglés. Sin esto, el usuario vería la descripción técnica en inglés.
/// </summary>
public sealed class ErrorCodeTranslationTests
{
    public static TheoryData<string> DeclaredCodes()
    {
        var data = new TheoryData<string>();
        Assembly[] assemblies = [typeof(Error).Assembly, typeof(ErrorMessages).Assembly];

        var codes = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Name.EndsWith("Errors", StringComparison.Ordinal))
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string) && field.Name.EndsWith("Code", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);

        foreach (var code in codes)
        {
            data.Add(code);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DeclaredCodes))]
    public void Error_code_has_spanish_and_english_texts(string code)
    {
        var spanish = ErrorMessages.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!.GetString(code);
        var english = ErrorMessages.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)!.GetString(code);

        Assert.False(string.IsNullOrWhiteSpace(spanish), $"Missing Spanish text for {code}");
        Assert.False(string.IsNullOrWhiteSpace(english), $"Missing English text for {code}");
    }
}
```

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Resources.ErrorCodeTranslationTests"`
Esperado: FALLAN los códigos nuevos (`Auth.*`, `Users.*`), porque todavía no tienen texto.

- [ ] **Paso 2: textos**

Agregar a `Errors.resx`, antes de `</root>`:

```xml
  <data name="Auth.LoginCode.Invalid" xml:space="preserve"><value>El código no es válido.</value></data>
  <data name="Auth.LoginCode.Expired" xml:space="preserve"><value>El código venció. Pedí uno nuevo.</value></data>
  <data name="Auth.LoginCode.AlreadyUsed" xml:space="preserve"><value>El código ya se usó. Pedí uno nuevo.</value></data>
  <data name="Auth.LoginCode.TooManyAttempts" xml:space="preserve"><value>Superaste los intentos para este código. Pedí uno nuevo.</value></data>
  <data name="Auth.LoginCode.ResendTooSoon" xml:space="preserve"><value>Esperá un momento antes de pedir otro código.</value></data>
  <data name="Auth.LoginCode.TooManyRequests" xml:space="preserve"><value>Pediste demasiados códigos. Probá de nuevo más tarde.</value></data>
  <data name="Auth.Account.Disabled" xml:space="preserve"><value>Tu cuenta está deshabilitada. Contactá a un administrador.</value></data>
  <data name="Auth.Account.LockedOut" xml:space="preserve"><value>Tu cuenta está bloqueada por unos minutos por demasiados intentos fallidos.</value></data>
  <data name="Auth.ExternalLogin.Failed" xml:space="preserve"><value>No pudimos completar el ingreso con Google.</value></data>
  <data name="Auth.ExternalLogin.EmailNotVerified" xml:space="preserve"><value>Tu cuenta de Google no tiene el email verificado.</value></data>
  <data name="Users.Email.Invalid" xml:space="preserve"><value>El email no es válido.</value></data>
  <data name="Users.User.NotFound" xml:space="preserve"><value>No encontramos el usuario.</value></data>
```

Agregar a `Errors.en.resx`:

```xml
  <data name="Auth.LoginCode.Invalid" xml:space="preserve"><value>The code is not valid.</value></data>
  <data name="Auth.LoginCode.Expired" xml:space="preserve"><value>The code has expired. Request a new one.</value></data>
  <data name="Auth.LoginCode.AlreadyUsed" xml:space="preserve"><value>The code has already been used. Request a new one.</value></data>
  <data name="Auth.LoginCode.TooManyAttempts" xml:space="preserve"><value>Too many attempts for this code. Request a new one.</value></data>
  <data name="Auth.LoginCode.ResendTooSoon" xml:space="preserve"><value>Wait a moment before requesting another code.</value></data>
  <data name="Auth.LoginCode.TooManyRequests" xml:space="preserve"><value>You requested too many codes. Try again later.</value></data>
  <data name="Auth.Account.Disabled" xml:space="preserve"><value>Your account is disabled. Contact an administrator.</value></data>
  <data name="Auth.Account.LockedOut" xml:space="preserve"><value>Your account is locked for a few minutes after too many failed attempts.</value></data>
  <data name="Auth.ExternalLogin.Failed" xml:space="preserve"><value>We couldn't complete the sign-in with Google.</value></data>
  <data name="Auth.ExternalLogin.EmailNotVerified" xml:space="preserve"><value>Your Google account doesn't have a verified email.</value></data>
  <data name="Users.Email.Invalid" xml:space="preserve"><value>The email address is not valid.</value></data>
  <data name="Users.User.NotFound" xml:space="preserve"><value>We couldn't find the user.</value></data>
```

Agregar a `Validation.resx` / `Validation.en.resx`:

```xml
  <data name="ReturnUrlInvalid" xml:space="preserve"><value>La dirección de retorno no es válida.</value></data>
  <data name="LoginCodeFormat" xml:space="preserve"><value>Ingresá el código que te enviamos por email.</value></data>
```

```xml
  <data name="ReturnUrlInvalid" xml:space="preserve"><value>The return address is not valid.</value></data>
  <data name="LoginCodeFormat" xml:space="preserve"><value>Enter the code we sent you by email.</value></data>
```

Y en `ValidationMessages.cs`:

```csharp
    public static string ReturnUrlInvalid => Get(nameof(ReturnUrlInvalid));

    public static string LoginCodeFormat => Get(nameof(LoginCodeFormat));
```

- [ ] **Paso 3: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: todo en verde, incluidos `ResourceParityTests` y los 12 casos de `ErrorCodeTranslationTests`.

- [ ] **Paso 4: commit**

```bash
git add src/ArquitecturaBase.Application/Resources tests/ArquitecturaBase.Application.UnitTests/Resources
git commit -m "feat: traducir los errores de ingreso y verificar que todo código de error tenga texto" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 8: Caso de uso: pedir código

`RequestLoginCode` aplica el reenvío y el límite por email, invalida los códigos anteriores, emite uno nuevo (se guarda solo el hash) y encola el email. Esta tarea también crea los dobles de prueba de identidad que usan las Tareas 9 a 11.

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Features/Auth/RequestLoginCode/RequestLoginCodeCommand.cs`, `RequestLoginCodeResponse.cs`, `RequestLoginCodeCommandValidator.cs`, `RequestLoginCodeCommandHandler.cs`
- Crear: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`, `AuthFakes.cs`
- Test: `tests/ArquitecturaBase.Application.UnitTests/Features/Auth/RequestLoginCodeCommandHandlerTests.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/DependencyInjectionTests.cs`, el csproj de Application.UnitTests, `Directory.Packages.props`

- [ ] **Paso 1: paquetes de test**

`Directory.Packages.props`, en el grupo `Tests`:

```xml
    <PackageVersion Include="Microsoft.Extensions.Configuration" Version="10.0.12" />
```

En `tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`, agregar:

```xml
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
```

- [ ] **Paso 2: dobles de prueba**

`tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;

/// <summary>IIdentityService en memoria. Registra lo que hicieron los casos de uso para poder verificarlo.</summary>
internal sealed class FakeIdentityService : IIdentityService
{
    public const string DefaultTimeZoneId = "America/Argentina/Buenos_Aires";

    private readonly List<UserAccount> _users = [];
    private readonly Dictionary<(string Provider, string Key), Guid> _externalLogins = [];
    private readonly Dictionary<Guid, string[]> _roles = [];

    public IReadOnlyList<UserAccount> Users => _users;

    public Dictionary<Guid, int> FailedAttempts { get; } = [];

    public HashSet<Guid> LockedOutUsers { get; } = [];

    public List<Guid> SignedInUsers { get; } = [];

    public ExternalLogin? PendingExternalLogin { get; set; }

    public bool ExternalSignedOut { get; private set; }

    public PagedRequest? LastListRequest { get; private set; }

    public UserAccount AddUser(string email, bool isActive = true, string culture = "es")
    {
        var user = new UserAccount(Guid.CreateVersion7(), email, DisplayName: null, culture, DefaultTimeZoneId, isActive);
        _users.Add(user);
        _roles[user.Id] = [];

        return user;
    }

    public void SetRoles(Guid userId, params string[] roles) => _roles[userId] = roles;

    public void LinkExternalLogin(Guid userId, string provider, string providerKey) =>
        _externalLogins[(provider, providerKey)] = userId;

    public Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_users.SingleOrDefault(user => user.Id == userId));

    public Task<UserAccount?> FindByEmailAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(_users.SingleOrDefault(user => user.Email == email.Value));

    public Task<UserAccount?> FindByExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken) =>
        Task.FromResult(_externalLogins.TryGetValue((provider, providerKey), out var userId)
            ? _users.Single(user => user.Id == userId)
            : null);

    public Task<UserAccount> CreateAsync(Email email, string? displayName, string culture, CancellationToken cancellationToken)
    {
        var user = new UserAccount(Guid.CreateVersion7(), email.Value, displayName, culture, DefaultTimeZoneId, IsActive: true);
        _users.Add(user);
        _roles[user.Id] = ["User"];

        return Task.FromResult(user);
    }

    public Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken)
    {
        LinkExternalLogin(userId, login.Provider, login.ProviderKey);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>(_roles.GetValueOrDefault(userId) ?? []);

    public Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(LockedOutUsers.Contains(userId));

    public Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken)
    {
        FailedAttempts[userId] = FailedAttempts.GetValueOrDefault(userId) + 1;

        return Task.CompletedTask;
    }

    public Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken)
    {
        FailedAttempts[userId] = 0;

        return Task.CompletedTask;
    }

    public Task SignInAsync(Guid userId, CancellationToken cancellationToken)
    {
        SignedInUsers.Add(userId);

        return Task.CompletedTask;
    }

    public Task<ExternalLogin?> GetExternalLoginAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PendingExternalLogin);

    public Task SignOutExternalAsync(CancellationToken cancellationToken)
    {
        ExternalSignedOut = true;

        return Task.CompletedTask;
    }

    public Task<PagedResult<UserListItem>> ListUsersAsync(PagedRequest request, CancellationToken cancellationToken)
    {
        LastListRequest = request;
        var items = _users.Select(user => new UserListItem(user.Id, user.Email, user.DisplayName, user.IsActive, default)).ToList();

        return Task.FromResult(new PagedResult<UserListItem>(items, request.Page, request.PageSize, items.Count));
    }
}
```

`tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/AuthFakes.cs`:

```csharp
using System.Globalization;
using ArquitecturaBase.Application.Abstractions.Emails;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;

internal sealed class InMemoryLoginCodeRepository : ILoginCodeRepository
{
    public List<LoginCode> Codes { get; } = [];

    public Task<LoginCode?> GetLatestAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(Codes
            .Where(code => code.Email == email.Value && code.InvalidatedAtUtc is null)
            .MaxBy(code => code.CreatedAtUtc));

    public Task<IReadOnlyList<LoginCode>> ListActiveAsync(Email email, DateTime nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LoginCode>>(Codes.Where(code => code.Email == email.Value && code.IsActive(nowUtc)).ToList());

    public Task<IReadOnlyList<DateTime>> ListRequestTimesSinceAsync(Email email, DateTime sinceUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DateTime>>(Codes
            .Where(code => code.Email == email.Value && code.CreatedAtUtc > sinceUtc)
            .Select(code => code.CreatedAtUtc)
            .Order()
            .ToList());

    public void Add(LoginCode loginCode) => Codes.Add(loginCode);
}

internal sealed class InMemoryLoginAuditRepository : ILoginAuditRepository
{
    public List<LoginAudit> Audits { get; } = [];

    public void Add(LoginAudit audit) => Audits.Add(audit);
}

internal sealed class FakeLoginCodeGenerator : ILoginCodeGenerator
{
    public const string Code = "123456";

    public string Generate() => Code;
}

internal sealed class FakeLoginCodeHasher : ILoginCodeHasher
{
    public static string HashOf(string email, string code) => $"hash:{email}:{code}";

    public string Hash(Email email, string code) => HashOf(email.Value, code);
}

internal sealed class FakeEmailTemplateRenderer : IEmailTemplateRenderer
{
    public string? LastCode { get; private set; }

    public CultureInfo? LastCulture { get; private set; }

    public EmailMessage RenderLoginCode(string to, string code, int lifetimeMinutes, CultureInfo culture)
    {
        LastCode = code;
        LastCulture = culture;

        return new EmailMessage(to, "subject", "<p>html</p>", "text");
    }
}

internal sealed class FakeEmailQueue : IEmailQueue
{
    public List<EmailMessage> Messages { get; } = [];

    public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Messages.Add(message);

        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeRequestInfo : IRequestInfo
{
    public string? IpAddress { get; init; } = "203.0.113.10";

    public string? UserAgent { get; init; } = "unit-tests";
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; init; }

    public bool IsAuthenticated => UserId is not null;
}

internal sealed class FakePermissionService : IPermissionService
{
    public Dictionary<Guid, string[]> Permissions { get; } = [];

    public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>(Permissions.GetValueOrDefault(userId) ?? []);

    public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
        Task.FromResult(Permissions.GetValueOrDefault(userId)?.Contains(permission) ?? false);

    public Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

- [ ] **Paso 3: tests que fallan**

`tests/ArquitecturaBase.Application.UnitTests/Features/Auth/RequestLoginCodeCommandHandlerTests.cs`:

```csharp
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Features.Auth.RequestLoginCode;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

public sealed class RequestLoginCodeCommandHandlerTests
{
    private const string UserEmail = "ana@example.com";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryLoginCodeRepository _loginCodes = new();
    private readonly FakeIdentityService _identity = new();
    private readonly FakeEmailTemplateRenderer _renderer = new();
    private readonly FakeEmailQueue _emailQueue = new();
    private readonly RequestLoginCodeCommandHandler _handler;

    public RequestLoginCodeCommandHandlerTests()
    {
        _handler = new RequestLoginCodeCommandHandler(
            _loginCodes,
            _identity,
            new FakeLoginCodeGenerator(),
            new FakeLoginCodeHasher(),
            _renderer,
            _emailQueue,
            Options.Create(new LoginCodeOptions()),
            _clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Issues_a_hashed_code_and_queues_the_email()
    {
        var result = await _handler.Handle(new RequestLoginCodeCommand(" Ana@Example.com "), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.ResendAfterSeconds);

        var code = Assert.Single(_loginCodes.Codes);
        Assert.Equal(UserEmail, code.Email);
        Assert.Equal(FakeLoginCodeHasher.HashOf(UserEmail, FakeLoginCodeGenerator.Code), code.CodeHash);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime.AddMinutes(10), code.ExpiresAtUtc);

        var message = Assert.Single(_emailQueue.Messages);
        Assert.Equal(UserEmail, message.To);
        Assert.Equal(FakeLoginCodeGenerator.Code, _renderer.LastCode);
    }

    [Fact]
    public async Task Email_uses_the_culture_of_the_existing_user()
    {
        _identity.AddUser(UserEmail, culture: "en");
        using var culture = new CultureScope("es");

        await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        Assert.Equal("en", _renderer.LastCulture!.Name);
    }

    [Fact]
    public async Task Email_for_a_new_account_uses_the_culture_of_the_request()
    {
        using var culture = new CultureScope("en");

        await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        Assert.Equal("en", _renderer.LastCulture!.Name);
    }

    [Fact]
    public async Task Asking_again_before_the_cooldown_returns_the_seconds_left()
    {
        await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);
        _clock.Advance(TimeSpan.FromSeconds(20));

        var result = await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        Assert.Equal(LoginCodeErrors.ResendTooSoonCode, result.Error.Code);
        Assert.Equal(40, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Single(_loginCodes.Codes);
        Assert.Single(_emailQueue.Messages);
    }

    [Fact]
    public async Task New_code_invalidates_the_previous_ones()
    {
        await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);
        _clock.Advance(TimeSpan.FromSeconds(60));

        var result = await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _loginCodes.Codes.Count);
        Assert.NotNull(_loginCodes.Codes[0].InvalidatedAtUtc);
        Assert.Null(_loginCodes.Codes[1].InvalidatedAtUtc);
    }

    [Fact]
    public async Task Sixth_request_in_the_window_waits_until_the_oldest_one_leaves_it()
    {
        for (var i = 0; i < 5; i++)
        {
            Assert.True((await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct)).IsSuccess);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        var result = await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        // El primero se pidió hace 5 minutos: sale de la ventana de 15 dentro de 10.
        Assert.Equal(LoginCodeErrors.TooManyRequestsCode, result.Error.Code);
        Assert.Equal(600, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Equal(5, _loginCodes.Codes.Count);
    }

    [Fact]
    public async Task Invalid_email_issues_nothing()
    {
        var result = await _handler.Handle(new RequestLoginCodeCommand("not-an-email"), Ct);

        Assert.Equal(UserErrors.EmailInvalidCode, result.Error.Code);
        Assert.Empty(_loginCodes.Codes);
        Assert.Empty(_emailQueue.Messages);
    }

    [Fact]
    public void Validator_requires_a_valid_email()
    {
        using var culture = new CultureScope("es");
        var validator = new RequestLoginCodeCommandValidator();

        var failure = Assert.Single(validator.Validate(new RequestLoginCodeCommand("ana@")).Errors);

        Assert.Equal("Ingresá un correo válido.", failure.ErrorMessage);
    }
}
```

En `DependencyInjectionTests`:
- Renombrar `Application_without_handlers_can_be_registered` a `Application_can_be_registered`.
- En `BuildProvider`, borrar la línea `services.AddApplication();`. Desde esta tarea, los handlers reales de Application necesitan servicios de Infrastructure, y `ValidateOnBuild` fallaría. Los tests de ese archivo usan solo los dobles `Ping*`.
- Agregar estos dos tests, con los usings `ArquitecturaBase.Application.Features.Auth`, `Microsoft.Extensions.Configuration` y `Microsoft.Extensions.Options`:

```csharp
    [Fact]
    public void Login_code_options_are_read_from_configuration()
    {
        using var provider = BuildProviderWithConfiguration(new() { ["Authentication:LoginCode:Length"] = "8" });

        Assert.Equal(8, provider.GetRequiredService<IOptions<LoginCodeOptions>>().Value.Length);
    }

    [Fact]
    public void Invalid_login_code_options_are_rejected()
    {
        using var provider = BuildProviderWithConfiguration(new() { ["Authentication:LoginCode:MaxAttempts"] = "0" });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<LoginCodeOptions>>().Value);

        Assert.Contains(nameof(LoginCodeOptions.MaxAttempts), exception.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProviderWithConfiguration(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddApplication();

        return services.BuildServiceProvider();
    }
```

- [ ] **Paso 4: correr y ver que falla**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: FALLA la compilación (`'RequestLoginCode' no existe`).

- [ ] **Paso 5: implementación**

`RequestLoginCodeCommand.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Auth.RequestLoginCode;

public sealed record RequestLoginCodeCommand(string? Email) : ICommand<RequestLoginCodeResponse>;
```

`RequestLoginCodeResponse.cs`:

```csharp
namespace ArquitecturaBase.Application.Features.Auth.RequestLoginCode;

/// <summary>Siempre la misma respuesta, exista o no la cuenta: no revela nada (sección 5.3).</summary>
public sealed record RequestLoginCodeResponse(int ResendAfterSeconds);
```

`RequestLoginCodeCommandValidator.cs`:

```csharp
using ArquitecturaBase.Application.Common.Validation;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Auth.RequestLoginCode;

internal sealed class RequestLoginCodeCommandValidator : AbstractValidator<RequestLoginCodeCommand>
{
    public RequestLoginCodeCommandValidator() => RuleFor(command => command.Email).ValidEmail();
}
```

`RequestLoginCodeCommandHandler.cs`:

```csharp
using System.Globalization;
using ArquitecturaBase.Application.Abstractions.Emails;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Auth.RequestLoginCode;

/// <summary>
/// Emite un código nuevo y encola el email. Aplica el reenvío y el límite por email (sección 5.3); el límite por IP
/// lo aplica el rate limiter de la Api. El email se encola antes de guardar: si el guardado fallara, el usuario
/// recibiría un código que no sirve y pediría otro.
/// </summary>
internal sealed class RequestLoginCodeCommandHandler(
    ILoginCodeRepository loginCodes,
    IIdentityService identityService,
    ILoginCodeGenerator codeGenerator,
    ILoginCodeHasher codeHasher,
    IEmailTemplateRenderer templateRenderer,
    IEmailQueue emailQueue,
    IOptions<LoginCodeOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<RequestLoginCodeCommand, RequestLoginCodeResponse>
{
    public async Task<Result<RequestLoginCodeResponse>> Handle(RequestLoginCodeCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);

        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;
        var settings = options.Value;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var limitError = await CheckLimitsAsync(email, settings, nowUtc, cancellationToken);

        if (limitError is not null)
        {
            return limitError;
        }

        foreach (var activeCode in await loginCodes.ListActiveAsync(email, nowUtc, cancellationToken))
        {
            activeCode.Invalidate(nowUtc);
        }

        var code = codeGenerator.Generate();

        loginCodes.Add(LoginCode.Issue(
            email,
            codeHasher.Hash(email, code),
            nowUtc,
            TimeSpan.FromMinutes(settings.LifetimeMinutes),
            settings.MaxAttempts));

        // El email sale en el idioma del perfil; si la cuenta todavía no existe, en el de la petición.
        var user = await identityService.FindByEmailAsync(email, cancellationToken);
        var culture = user is null ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(user.Culture);

        await emailQueue.EnqueueAsync(
            templateRenderer.RenderLoginCode(email.Value, code, settings.LifetimeMinutes, culture),
            cancellationToken);

        return new RequestLoginCodeResponse(settings.ResendCooldownSeconds);
    }

    private async Task<Error?> CheckLimitsAsync(Email email, LoginCodeOptions settings, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var window = TimeSpan.FromMinutes(settings.RequestWindowMinutes);
        var requestTimes = await loginCodes.ListRequestTimesSinceAsync(email, nowUtc - window, cancellationToken);

        if (requestTimes.Count >= settings.MaxRequestsPerWindow)
        {
            // Se libera un lugar cuando el pedido más viejo de la ventana sale de ella.
            return LoginCodeErrors.TooManyRequests(SecondsUntil(requestTimes[0] + window, nowUtc));
        }

        var latest = await loginCodes.GetLatestAsync(email, cancellationToken);
        var resendAllowedAtUtc = latest?.CreatedAtUtc + TimeSpan.FromSeconds(settings.ResendCooldownSeconds);

        return resendAllowedAtUtc > nowUtc
            ? LoginCodeErrors.ResendTooSoon(SecondsUntil(resendAllowedAtUtc.Value, nowUtc))
            : null;
    }

    private static int SecondsUntil(DateTime momentUtc, DateTime nowUtc) =>
        Math.Max(1, (int)Math.Ceiling((momentUtc - nowUtc).TotalSeconds));
}
```

- [ ] **Paso 6: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: todo en verde (8 tests nuevos en el handler y 2 en `DependencyInjectionTests`).

- [ ] **Paso 7: commit**

```bash
git add Directory.Packages.props src/ArquitecturaBase.Application tests/ArquitecturaBase.Application.UnitTests
git commit -m "feat: agregar el caso de uso para pedir un código de ingreso" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 9: Caso de uso: verificar código

`VerifyLoginCode` es el corazón del ingreso. Hace esto, en orden:
1. Chequea el bloqueo de Identity.
2. Verifica el código.
3. Si el código es correcto, crea la cuenta si no existía.
4. Informa si la cuenta está deshabilitada (recién acá, después de verificar el código).
5. Inicia la sesión del servidor y audita el intento.

Implementa `IPersistChangesOnFailure`, porque el intento fallido y la auditoría se guardan aunque el resultado sea un error.

**Archivos:**
- Crear en `src/ArquitecturaBase.Application/Features/Auth/`:
  - `UserCultures.cs`
  - `VerifyLoginCode/VerifyLoginCodeCommand.cs`, `VerifyLoginCodeResponse.cs`, `VerifyLoginCodeCommandValidator.cs`, `VerifyLoginCodeCommandHandler.cs`
- Test: `tests/ArquitecturaBase.Application.UnitTests/Features/Auth/VerifyLoginCodeCommandHandlerTests.cs`, `VerifyLoginCodeCommandValidatorTests.cs`

- [ ] **Paso 1: tests que fallan**

`VerifyLoginCodeCommandHandlerTests.cs`:

```csharp
using ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

public sealed class VerifyLoginCodeCommandHandlerTests
{
    private const string UserEmail = "ana@example.com";
    private const string RightCode = "123456";
    private const string ReturnUrl = "/connect/authorize?client_id=web";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryLoginCodeRepository _loginCodes = new();
    private readonly InMemoryLoginAuditRepository _audits = new();
    private readonly FakeIdentityService _identity = new();
    private readonly VerifyLoginCodeCommandHandler _handler;

    public VerifyLoginCodeCommandHandlerTests()
    {
        _handler = new VerifyLoginCodeCommandHandler(
            _loginCodes, _audits, _identity, new FakeLoginCodeHasher(), new FakeRequestInfo(), _clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Right_code_for_a_new_email_creates_the_account_and_signs_in()
    {
        IssueCode();
        using var culture = new CultureScope("en");

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReturnUrl, result.Value.ReturnUrl);
        var user = Assert.Single(_identity.Users);
        Assert.Equal("en", user.Culture);
        Assert.Equal([user.Id], _identity.SignedInUsers);
        Assert.NotNull(_loginCodes.Codes[0].ConsumedAtUtc);

        var audit = Assert.Single(_audits.Audits);
        Assert.True(audit.Succeeded);
        Assert.Equal(user.Id, audit.UserId);
        Assert.Equal(LoginMethod.Code, audit.Method);
        Assert.Equal("203.0.113.10", audit.IpAddress);
    }

    [Fact]
    public async Task Right_code_for_an_existing_user_resets_the_failed_attempts()
    {
        var user = _identity.AddUser(UserEmail);
        _identity.FailedAttempts[user.Id] = 3;
        IssueCode();

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.True(result.IsSuccess);
        Assert.Single(_identity.Users);
        Assert.Equal(0, _identity.FailedAttempts[user.Id]);
        Assert.Equal([user.Id], _identity.SignedInUsers);
    }

    [Fact]
    public async Task Wrong_code_counts_a_failed_attempt_and_is_audited()
    {
        var user = _identity.AddUser(UserEmail);
        IssueCode();

        var result = await _handler.Handle(Command("000000"), Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Equal(4, result.Error.Metadata![LoginCodeErrors.AttemptsLeftKey]);
        Assert.Equal(1, _identity.FailedAttempts[user.Id]);
        Assert.Empty(_identity.SignedInUsers);

        var audit = Assert.Single(_audits.Audits);
        Assert.False(audit.Succeeded);
        Assert.Equal(LoginCodeErrors.InvalidCode, audit.FailureReason);
        Assert.Equal(user.Id, audit.UserId);
    }

    [Fact]
    public async Task Without_a_code_the_error_is_the_same_as_for_a_wrong_code()
    {
        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Null(result.Error.Metadata);
        Assert.Empty(_identity.Users);
        Assert.Single(_audits.Audits);
    }

    [Fact]
    public async Task Locked_out_user_is_rejected_before_checking_the_code()
    {
        var user = _identity.AddUser(UserEmail);
        _identity.LockedOutUsers.Add(user.Id);
        IssueCode();

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.Equal(AccountErrors.LockedOutCode, result.Error.Code);
        Assert.Null(_loginCodes.Codes[0].ConsumedAtUtc);
        Assert.Equal(0, _loginCodes.Codes[0].FailedAttempts);
        Assert.Equal(AccountErrors.LockedOutCode, Assert.Single(_audits.Audits).FailureReason);
    }

    [Fact]
    public async Task Disabled_account_is_reported_after_the_code_is_verified()
    {
        _identity.AddUser(UserEmail, isActive: false);
        IssueCode();

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.Equal(AccountErrors.DisabledCode, result.Error.Code);
        Assert.NotNull(_loginCodes.Codes[0].ConsumedAtUtc);
        Assert.Empty(_identity.SignedInUsers);
        Assert.Equal(AccountErrors.DisabledCode, Assert.Single(_audits.Audits).FailureReason);
    }

    [Fact]
    public async Task Expired_code_is_rejected()
    {
        IssueCode();
        _clock.Advance(TimeSpan.FromMinutes(10));

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.Equal(LoginCodeErrors.ExpiredCode, result.Error.Code);
        Assert.Empty(_identity.Users);
    }

    private static VerifyLoginCodeCommand Command(string code) => new(UserEmail, code, ReturnUrl);

    private void IssueCode() =>
        _loginCodes.Add(LoginCode.Issue(
            Email.Create(UserEmail).Value,
            FakeLoginCodeHasher.HashOf(UserEmail, RightCode),
            _clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(10),
            maxAttempts: 5));
}
```

`VerifyLoginCodeCommandValidatorTests.cs`:

```csharp
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

public sealed class VerifyLoginCodeCommandValidatorTests
{
    private static readonly VerifyLoginCodeCommandValidator Validator = new(Options.Create(new LoginCodeOptions()));

    [Fact]
    public void Valid_command_passes()
    {
        Assert.True(Validator.Validate(new VerifyLoginCodeCommand("ana@example.com", "123456", "/connect/authorize?x=1")).IsValid);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public void Code_must_have_the_configured_digits(string code)
    {
        using var culture = new CultureScope("es");

        var failure = Assert.Single(Validator.Validate(new VerifyLoginCodeCommand("ana@example.com", code, "/connect/authorize")).Errors);

        Assert.Equal(nameof(VerifyLoginCodeCommand.Code), failure.PropertyName);
        Assert.Equal("Ingresá el código que te enviamos por email.", failure.ErrorMessage);
    }

    [Theory]
    [InlineData("https://evil.example/connect/authorize")]
    [InlineData("/login")]
    public void Return_url_must_be_the_local_authorize_endpoint(string returnUrl)
    {
        using var culture = new CultureScope("es");

        var failure = Assert.Single(Validator.Validate(new VerifyLoginCodeCommand("ana@example.com", "123456", returnUrl)).Errors);

        Assert.Equal(nameof(VerifyLoginCodeCommand.ReturnUrl), failure.PropertyName);
        Assert.Equal("La dirección de retorno no es válida.", failure.ErrorMessage);
    }
}
```

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: FALLA la compilación (`'VerifyLoginCode' no existe`).

- [ ] **Paso 2: implementación**

`Features/Auth/UserCultures.cs`:

```csharp
using System.Globalization;

namespace ArquitecturaBase.Application.Features.Auth;

/// <summary>Idioma de una cuenta nueva: el de la petición si está soportado; si no, español.</summary>
internal static class UserCultures
{
    public const string Default = "es";

    private static readonly string[] Supported = [Default, "en"];

    public static string FromCurrentRequest()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        return Supported.Contains(language) ? language : Default;
    }
}
```

`VerifyLoginCode/VerifyLoginCodeCommand.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;

public sealed record VerifyLoginCodeCommand(string? Email, string? Code, string? ReturnUrl)
    : ICommand<VerifyLoginCodeResponse>, IPersistChangesOnFailure;
```

`VerifyLoginCode/VerifyLoginCodeResponse.cs`:

```csharp
namespace ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;

/// <summary>A dónde navega el SPA: el authorize original, que ahora encuentra la sesión (sección 5.2).</summary>
public sealed record VerifyLoginCodeResponse(string ReturnUrl);
```

`VerifyLoginCode/VerifyLoginCodeCommandValidator.cs`:

```csharp
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Resources;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;

internal sealed class VerifyLoginCodeCommandValidator : AbstractValidator<VerifyLoginCodeCommand>
{
    public VerifyLoginCodeCommandValidator(IOptions<LoginCodeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var length = options.Value.Length;

        RuleFor(command => command.Email).ValidEmail();

        RuleFor(command => command.Code)
            .Cascade(CascadeMode.Stop)
            .Required()
            .Must(code => code!.Length == length && code.All(char.IsAsciiDigit))
            .WithMessage(_ => ValidationMessages.LoginCodeFormat);

        RuleFor(command => command.ReturnUrl)
            .Cascade(CascadeMode.Stop)
            .Required()
            .Must(ReturnUrls.IsAuthorizeRequest)
            .WithMessage(_ => ValidationMessages.ReturnUrlInvalid);
    }
}
```

`VerifyLoginCode/VerifyLoginCodeCommandHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;

internal sealed class VerifyLoginCodeCommandHandler(
    ILoginCodeRepository loginCodes,
    ILoginAuditRepository loginAudits,
    IIdentityService identityService,
    ILoginCodeHasher codeHasher,
    IRequestInfo requestInfo,
    TimeProvider timeProvider)
    : ICommandHandler<VerifyLoginCodeCommand, VerifyLoginCodeResponse>
{
    public async Task<Result<VerifyLoginCodeResponse>> Handle(VerifyLoginCodeCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);

        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var user = await identityService.FindByEmailAsync(email, cancellationToken);

        if (user is not null && await identityService.IsLockedOutAsync(user.Id, cancellationToken))
        {
            return Fail(email, user, AccountErrors.LockedOut, nowUtc);
        }

        // Sin un código para ese email, el error es el mismo que el de un código incorrecto.
        var loginCode = await loginCodes.GetLatestAsync(email, cancellationToken);
        var verification = loginCode?.Verify(codeHasher.Hash(email, command.Code!), nowUtc)
            ?? Result.Failure(LoginCodeErrors.Invalid(attemptsLeft: null));

        if (verification.IsFailure)
        {
            if (user is not null)
            {
                await identityService.RegisterFailedAttemptAsync(user.Id, cancellationToken);
            }

            return Fail(email, user, verification.Error, nowUtc);
        }

        user ??= await identityService.CreateAsync(email, displayName: null, UserCultures.FromCurrentRequest(), cancellationToken);

        // Se informa recién ahora: el usuario ya probó que el email es suyo.
        if (!user.IsActive)
        {
            return Fail(email, user, AccountErrors.Disabled, nowUtc);
        }

        await identityService.ResetFailedAttemptsAsync(user.Id, cancellationToken);
        await identityService.SignInAsync(user.Id, cancellationToken);

        loginAudits.Add(LoginAudit.Success(
            email.Value, user.Id, LoginMethod.Code, requestInfo.IpAddress, requestInfo.UserAgent, nowUtc));

        return new VerifyLoginCodeResponse(command.ReturnUrl!);
    }

    private Error Fail(Email email, UserAccount? user, Error error, DateTime nowUtc)
    {
        loginAudits.Add(LoginAudit.Failure(
            email.Value, user?.Id, LoginMethod.Code, error.Code, requestInfo.IpAddress, requestInfo.UserAgent, nowUtc));

        return error;
    }
}
```

- [ ] **Paso 3: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: todo en verde (7 tests nuevos en el handler y 6 en el validador).

- [ ] **Paso 4: commit**

```bash
git add src/ArquitecturaBase.Application tests/ArquitecturaBase.Application.UnitTests
git commit -m "feat: agregar el caso de uso para verificar el código e iniciar la sesión" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 10: Caso de uso: ingreso con proveedor externo

`SignInWithExternalProvider` sigue la sección 5.4 del spec:
1. Busca la cuenta por login externo.
2. Si no la encuentra y el proveedor verificó el email, busca por email y vincula el login, o crea la cuenta.
3. Sin email verificado, no vincula ni crea nada.

La cookie externa se cierra siempre, apenas se lee.

**Archivos:**
- Crear en `src/ArquitecturaBase.Application/Features/Auth/SignInWithExternalProvider/`: `SignInWithExternalProviderCommand.cs`, `SignInWithExternalProviderResponse.cs`, `SignInWithExternalProviderCommandValidator.cs`, `SignInWithExternalProviderCommandHandler.cs`
- Test: `tests/ArquitecturaBase.Application.UnitTests/Features/Auth/SignInWithExternalProviderCommandHandlerTests.cs`

- [ ] **Paso 1: tests que fallan**

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Features.Auth.SignInWithExternalProvider;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authentication;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

public sealed class SignInWithExternalProviderCommandHandlerTests
{
    private const string UserEmail = "ana@example.com";
    private const string ReturnUrl = "/connect/authorize?client_id=web";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryLoginAuditRepository _audits = new();
    private readonly FakeIdentityService _identity = new();
    private readonly SignInWithExternalProviderCommandHandler _handler;

    public SignInWithExternalProviderCommandHandlerTests()
    {
        _handler = new SignInWithExternalProviderCommandHandler(_identity, _audits, new FakeRequestInfo(), _clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Linked_account_signs_in()
    {
        var user = _identity.AddUser(UserEmail);
        _identity.LinkExternalLogin(user.Id, "Google", "google-123");
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.Equal(ReturnUrl, result.Value.ReturnUrl);
        Assert.Equal([user.Id], _identity.SignedInUsers);
        Assert.True(_identity.ExternalSignedOut);

        var audit = Assert.Single(_audits.Audits);
        Assert.True(audit.Succeeded);
        Assert.Equal(LoginMethod.Google, audit.Method);
    }

    [Fact]
    public async Task Verified_email_without_an_account_creates_it_and_links_the_login()
    {
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(_identity.Users);
        Assert.Equal("Ana Pérez", user.DisplayName);
        Assert.Same(user, await _identity.FindByExternalLoginAsync("Google", "google-123", Ct));
    }

    [Fact]
    public async Task Verified_email_of_an_existing_account_links_the_login_without_duplicating_it()
    {
        var user = _identity.AddUser(UserEmail);
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true);

        await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.Single(_identity.Users);
        Assert.Same(user, await _identity.FindByExternalLoginAsync("Google", "google-123", Ct));
        Assert.Equal([user.Id], _identity.SignedInUsers);
    }

    [Fact]
    public async Task Unverified_email_is_rejected_without_creating_or_linking()
    {
        _identity.AddUser(UserEmail);
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: false);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.Equal(ExternalLoginErrors.EmailNotVerifiedCode, result.Error.Code);
        Assert.Single(_identity.Users);
        Assert.Null(await _identity.FindByExternalLoginAsync("Google", "google-123", Ct));
        Assert.Empty(_identity.SignedInUsers);
        Assert.True(_identity.ExternalSignedOut);
        Assert.False(Assert.Single(_audits.Audits).Succeeded);
    }

    [Fact]
    public async Task Missing_external_login_fails()
    {
        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.Equal(ExternalLoginErrors.FailedCode, result.Error.Code);
        Assert.Single(_audits.Audits);
    }

    [Fact]
    public async Task Disabled_account_cannot_sign_in()
    {
        var user = _identity.AddUser(UserEmail, isActive: false);
        _identity.LinkExternalLogin(user.Id, "Google", "google-123");
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.Equal(AccountErrors.DisabledCode, result.Error.Code);
        Assert.Empty(_identity.SignedInUsers);
    }

    private static ExternalLogin GoogleLogin(bool emailVerified) =>
        new("Google", "google-123", "Ana@Example.com", emailVerified, "Ana Pérez");
}
```

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: FALLA la compilación (`'SignInWithExternalProvider' no existe`).

- [ ] **Paso 2: implementación**

`SignInWithExternalProviderCommand.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Auth.SignInWithExternalProvider;

public sealed record SignInWithExternalProviderCommand(string? ReturnUrl)
    : ICommand<SignInWithExternalProviderResponse>, IPersistChangesOnFailure;
```

`SignInWithExternalProviderResponse.cs`:

```csharp
namespace ArquitecturaBase.Application.Features.Auth.SignInWithExternalProvider;

public sealed record SignInWithExternalProviderResponse(string ReturnUrl);
```

`SignInWithExternalProviderCommandValidator.cs`:

```csharp
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Auth.SignInWithExternalProvider;

internal sealed class SignInWithExternalProviderCommandValidator : AbstractValidator<SignInWithExternalProviderCommand>
{
    public SignInWithExternalProviderCommandValidator() =>
        RuleFor(command => command.ReturnUrl)
            .Cascade(CascadeMode.Stop)
            .Required()
            .Must(ReturnUrls.IsAuthorizeRequest)
            .WithMessage(_ => ValidationMessages.ReturnUrlInvalid);
}
```

`SignInWithExternalProviderCommandHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Auth.SignInWithExternalProvider;

internal sealed class SignInWithExternalProviderCommandHandler(
    IIdentityService identityService,
    ILoginAuditRepository loginAudits,
    IRequestInfo requestInfo,
    TimeProvider timeProvider)
    : ICommandHandler<SignInWithExternalProviderCommand, SignInWithExternalProviderResponse>
{
    public async Task<Result<SignInWithExternalProviderResponse>> Handle(
        SignInWithExternalProviderCommand command,
        CancellationToken cancellationToken)
    {
        var login = await identityService.GetExternalLoginAsync(cancellationToken);

        if (login is null)
        {
            return Fail(email: string.Empty, user: null, ExternalLoginErrors.Failed);
        }

        // La cookie externa solo sirve para este paso: se cierra pase lo que pase.
        await identityService.SignOutExternalAsync(cancellationToken);

        var user = await identityService.FindByExternalLoginAsync(login.Provider, login.ProviderKey, cancellationToken);

        if (user is null)
        {
            var email = Email.Create(login.Email);

            // Sin email verificado no se vincula ni se crea: alguien podría presentarse con un email ajeno.
            if (!login.EmailVerified || email.IsFailure)
            {
                return Fail(email.IsSuccess ? email.Value.Value : string.Empty, user: null, ExternalLoginErrors.EmailNotVerified);
            }

            user = await identityService.FindByEmailAsync(email.Value, cancellationToken)
                ?? await identityService.CreateAsync(email.Value, login.DisplayName, UserCultures.FromCurrentRequest(), cancellationToken);

            await identityService.AddExternalLoginAsync(user.Id, login, cancellationToken);
        }

        if (!user.IsActive)
        {
            return Fail(user.Email, user, AccountErrors.Disabled);
        }

        // Igual que el ingreso con código: una cuenta bloqueada no entra por ningún medio.
        if (await identityService.IsLockedOutAsync(user.Id, cancellationToken))
        {
            return Fail(user.Email, user, AccountErrors.LockedOut);
        }

        await identityService.SignInAsync(user.Id, cancellationToken);

        loginAudits.Add(LoginAudit.Success(
            user.Email, user.Id, LoginMethod.Google, requestInfo.IpAddress, requestInfo.UserAgent, UtcNow()));

        return new SignInWithExternalProviderResponse(command.ReturnUrl!);
    }

    private Error Fail(string email, UserAccount? user, Error error)
    {
        loginAudits.Add(LoginAudit.Failure(
            email, user?.Id, LoginMethod.Google, error.Code, requestInfo.IpAddress, requestInfo.UserAgent, UtcNow()));

        return error;
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
```

- [ ] **Paso 3: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: todo en verde (6 tests nuevos).

- [ ] **Paso 4: commit**

```bash
git add src/ArquitecturaBase.Application tests/ArquitecturaBase.Application.UnitTests
git commit -m "feat: agregar el caso de uso de ingreso con un proveedor externo" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 11: Casos de uso: usuario actual y listado paginado

`GetCurrentUser` devuelve el perfil con roles, permisos, idioma y zona horaria para `/api/me`. `GetUsers` es el ejemplo del patrón de paginado para `/api/users`. También resuelve el pendiente 6 de la Fase 1: `Search` tiene un largo máximo.

**Archivos:**
- Crear en `src/ArquitecturaBase.Application/Features/Users/`:
  - `GetCurrentUser/GetCurrentUserQuery.cs`, `CurrentUserResponse.cs`, `GetCurrentUserQueryHandler.cs`
  - `GetUsers/GetUsersQuery.cs`, `GetUsersQueryValidator.cs`, `GetUsersQueryHandler.cs` (`UserListItem.cs` ya existe desde la Tarea 6)
- Modificar: `src/ArquitecturaBase.Application/Common/Pagination/PagedRequest.cs`, `Common/Validation/PagedRequestValidator.cs`
- Test: `tests/ArquitecturaBase.Application.UnitTests/Features/Users/GetCurrentUserQueryHandlerTests.cs`, `GetUsersQueryTests.cs`, y un caso nuevo en `Common/Validation/PagedRequestValidatorTests.cs`

- [ ] **Paso 1: tests que fallan**

`Features/Users/GetCurrentUserQueryHandlerTests.cs`:

```csharp
using ArquitecturaBase.Application.Features.Users.GetCurrentUser;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Features.Users;

public sealed class GetCurrentUserQueryHandlerTests
{
    private readonly FakeIdentityService _identity = new();
    private readonly FakePermissionService _permissions = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_the_profile_with_sorted_roles_and_permissions()
    {
        var user = _identity.AddUser("ana@example.com", culture: "en");
        _identity.SetRoles(user.Id, "User", "Admin");
        _permissions.Permissions[user.Id] = ["users.read", "roles.manage"];

        var result = await Handler(user.Id).Handle(new GetCurrentUserQuery(), Ct);

        Assert.Equal(user.Id, result.Value.Id);
        Assert.Equal("ana@example.com", result.Value.Email);
        Assert.Equal("en", result.Value.Culture);
        Assert.Equal(FakeIdentityService.DefaultTimeZoneId, result.Value.TimeZoneId);
        Assert.Equal(["Admin", "User"], result.Value.Roles);
        Assert.Equal(["roles.manage", "users.read"], result.Value.Permissions);
    }

    [Fact]
    public async Task Unknown_user_is_not_found()
    {
        var result = await Handler(Guid.CreateVersion7()).Handle(new GetCurrentUserQuery(), Ct);

        Assert.Equal(UserErrors.NotFoundCode, result.Error.Code);
    }

    [Fact]
    public async Task Anonymous_request_is_not_found()
    {
        var result = await Handler(userId: null).Handle(new GetCurrentUserQuery(), Ct);

        Assert.Equal(UserErrors.NotFoundCode, result.Error.Code);
    }

    private GetCurrentUserQueryHandler Handler(Guid? userId) =>
        new(new FakeCurrentUser { UserId = userId }, _identity, _permissions);
}
```

`Features/Users/GetUsersQueryTests.cs`:

```csharp
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;

namespace ArquitecturaBase.Application.UnitTests.Features.Users;

public sealed class GetUsersQueryTests
{
    [Theory]
    [InlineData("email")]
    [InlineData("-displayName")]
    [InlineData("createdAtUtc")]
    public void Whitelisted_fields_can_be_sorted(string sort)
    {
        Assert.True(new GetUsersQueryValidator().Validate(new GetUsersQuery { Sort = sort }).IsValid);
    }

    [Fact]
    public void Other_fields_cannot_be_sorted()
    {
        Assert.False(new GetUsersQueryValidator().Validate(new GetUsersQuery { Sort = "passwordHash" }).IsValid);
    }

    [Fact]
    public async Task Handler_delegates_the_page_to_the_identity_service()
    {
        var identity = new FakeIdentityService();
        identity.AddUser("ana@example.com");
        var query = new GetUsersQuery { Page = 1, PageSize = 10, Search = "ana" };

        var result = await new GetUsersQueryHandler(identity).Handle(query, TestContext.Current.CancellationToken);

        Assert.Same(query, identity.LastListRequest);
        Assert.Equal("ana@example.com", Assert.Single(result.Value.Items).Email);
    }
}
```

Agregar a `PagedRequestValidatorTests`:

```csharp
    [Fact]
    public void Search_longer_than_the_limit_is_rejected()
    {
        using var culture = new CultureScope("es");

        var result = Validator.Validate(new ProductsQuery { Search = new string('a', PagedRequest.MaxSearchLength + 1) });

        var failure = Assert.Single(result.Errors);
        Assert.Equal("Search", failure.PropertyName);
        Assert.Equal("Ingresá como máximo 100 caracteres.", failure.ErrorMessage);
    }
```

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: FALLA la compilación (`'GetCurrentUser' no existe`).

- [ ] **Paso 2: implementación**

En `PagedRequest`, debajo de `MaxPage`:

```csharp
    public const int MaxSearchLength = 100;
```

En el constructor de `PagedRequestValidator<TRequest>`, después de la regla de `Sort`:

```csharp
        RuleFor(request => request.Search).MaxLength(PagedRequest.MaxSearchLength);
```

`GetCurrentUser/GetCurrentUserQuery.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.GetCurrentUser;

public sealed record GetCurrentUserQuery : IQuery<CurrentUserResponse>;
```

`GetCurrentUser/CurrentUserResponse.cs`:

```csharp
namespace ArquitecturaBase.Application.Features.Users.GetCurrentUser;

/// <summary>Perfil, roles, permisos e idioma/zona horaria: lo que el front necesita al iniciar (sección 5.6).</summary>
public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string? DisplayName,
    string Culture,
    string TimeZoneId,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);
```

`GetCurrentUser/GetCurrentUserQueryHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.GetCurrentUser;

internal sealed class GetCurrentUserQueryHandler(
    ICurrentUser currentUser,
    IIdentityService identityService,
    IPermissionService permissionService)
    : IQueryHandler<GetCurrentUserQuery, CurrentUserResponse>
{
    public async Task<Result<CurrentUserResponse>> Handle(GetCurrentUserQuery query, CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await identityService.FindByIdAsync(userId, cancellationToken)
            : null;

        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var roles = await identityService.GetRolesAsync(user.Id, cancellationToken);
        var permissions = await permissionService.GetPermissionsAsync(user.Id, cancellationToken);

        return new CurrentUserResponse(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Culture,
            user.TimeZoneId,
            [.. roles.Order(StringComparer.Ordinal)],
            [.. permissions.Order(StringComparer.Ordinal)]);
    }
}
```

`GetUsers/GetUsersQuery.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.Features.Users.GetUsers;

public sealed record GetUsersQuery : PagedRequest, IQuery<PagedResult<UserListItem>>
{
    // Lista blanca: los mismos nombres que usa IdentityService para ordenar.
    public static readonly IReadOnlyCollection<string> SortableFields = ["email", "displayName", "createdAtUtc"];
}
```

`GetUsers/GetUsersQueryValidator.cs`:

```csharp
using ArquitecturaBase.Application.Common.Validation;

namespace ArquitecturaBase.Application.Features.Users.GetUsers;

internal sealed class GetUsersQueryValidator() : PagedRequestValidator<GetUsersQuery>(GetUsersQuery.SortableFields);
```

`GetUsers/GetUsersQueryHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.Users.GetUsers;

internal sealed class GetUsersQueryHandler(IIdentityService identityService)
    : IQueryHandler<GetUsersQuery, PagedResult<UserListItem>>
{
    public async Task<Result<PagedResult<UserListItem>>> Handle(GetUsersQuery query, CancellationToken cancellationToken) =>
        await identityService.ListUsersAsync(query, cancellationToken);
}
```

- [ ] **Paso 3: correr y ver que pasa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj
```

Esperado: 0 advertencias. Todo en verde: 3 tests nuevos del perfil, 5 del listado y 1 del validador.

- [ ] **Paso 4: commit**

```bash
git add src/ArquitecturaBase.Application tests/ArquitecturaBase.Application.UnitTests
git commit -m "feat: agregar las consultas del usuario actual y del listado paginado de usuarios" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 12: Identity en el modelo: `ApplicationUser`, `ApplicationDbContext` y repositorios

`ApplicationDbContext` pasa a heredar de `IdentityDbContext` y suma las tablas propias (`LoginCodes`, `LoginAudits`) y las claves de Data Protection. Los repositorios implementan las interfaces de Domain.

**Archivos:**
- Crear en `src/ArquitecturaBase.Infrastructure/`:
  - `Identity/ApplicationUser.cs`, `Identity/ApplicationRole.cs`
  - `Persistence/Configurations/ApplicationUserConfiguration.cs`, `ApplicationRoleConfiguration.cs`, `LoginCodeConfiguration.cs`, `LoginAuditConfiguration.cs`
  - `Persistence/Repositories/LoginCodeRepository.cs`, `LoginAuditRepository.cs`
- Modificar: `Persistence/ApplicationDbContext.cs`, `DependencyInjection.cs`, el csproj de Infrastructure, `Directory.Packages.props`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/LoginCodeRepositoryTests.cs`, `IdentityModelTests.cs`

- [ ] **Paso 1: paquetes**

`Directory.Packages.props`, grupo `Infrastructure`:
- Agregar:

```xml
    <PackageVersion Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.12" />
    <PackageVersion Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" Version="10.0.12" />
```

- Borrar `Microsoft.Extensions.Configuration.Abstractions`. Con la referencia al framework de ASP.NET Core ya viene incluido, y el SDK 10 avisaría (NU1510) que la referencia sobra.

`src/ArquitecturaBase.Infrastructure/ArquitecturaBase.Infrastructure.csproj` queda así:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\ArquitecturaBase.Application\ArquitecturaBase.Application.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- Identity (UserManager, SignInManager), Data Protection y autenticación son parte de ASP.NET Core. -->
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="ArquitecturaBase.Api.IntegrationTests" />
  </ItemGroup>
</Project>
```

- [ ] **Paso 2: tests que fallan**

`tests/ArquitecturaBase.Api.IntegrationTests/Persistence/LoginCodeRepositoryTests.cs`:

```csharp
using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

[Collection(ApiTestGroup.Name)]
public sealed class LoginCodeRepositoryTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime NowUtc => factory.Clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task Code_state_survives_a_round_trip()
    {
        var email = UniqueEmail();
        var code = Issue(email, NowUtc);
        code.Verify("wrong-hash", NowUtc);
        await SaveAsync(code);

        var loaded = await factory.ExecuteDbContextAsync(db => new LoginCodeRepository(db).GetLatestAsync(email, Ct));

        Assert.Equal(code.Id, loaded!.Id);
        Assert.Equal(1, loaded.FailedAttempts);
        Assert.Equal(code.ExpiresAtUtc, loaded.ExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, loaded.ExpiresAtUtc.Kind);
    }

    [Fact]
    public async Task Latest_code_is_the_newest_one_of_that_email()
    {
        var email = UniqueEmail();
        var older = Issue(email, NowUtc);
        var newer = Issue(email, NowUtc.AddSeconds(30));
        older.Invalidate(NowUtc.AddSeconds(30));
        await SaveAsync(older, newer, Issue(UniqueEmail(), NowUtc.AddSeconds(60)));

        var latest = await factory.ExecuteDbContextAsync(db => new LoginCodeRepository(db).GetLatestAsync(email, Ct));

        Assert.Equal(newer.Id, latest!.Id);
    }

    [Fact]
    public async Task Active_codes_exclude_consumed_invalidated_and_expired_ones()
    {
        var email = UniqueEmail();
        var active = Issue(email, NowUtc);
        var consumed = Issue(email, NowUtc);
        consumed.Verify(consumed.CodeHash, NowUtc);
        var invalidated = Issue(email, NowUtc);
        invalidated.Invalidate(NowUtc);
        var expired = Issue(email, NowUtc.AddMinutes(-11));
        await SaveAsync(active, consumed, invalidated, expired);

        var codes = await factory.ExecuteDbContextAsync(db => new LoginCodeRepository(db).ListActiveAsync(email, NowUtc, Ct));

        Assert.Equal(active.Id, Assert.Single(codes).Id);
    }

    [Fact]
    public async Task Request_times_inside_the_window_are_listed_from_oldest_to_newest()
    {
        var email = UniqueEmail();
        await SaveAsync(
            Issue(email, NowUtc.AddMinutes(-20)),
            Issue(email, NowUtc.AddMinutes(-2)),
            Issue(email, NowUtc.AddMinutes(-10)));

        var times = await factory.ExecuteDbContextAsync(db =>
            new LoginCodeRepository(db).ListRequestTimesSinceAsync(email, NowUtc.AddMinutes(-15), Ct));

        Assert.Equal([NowUtc.AddMinutes(-10), NowUtc.AddMinutes(-2)], times);
    }

    [Fact]
    public async Task Audits_are_saved()
    {
        var email = UniqueEmail();
        var audit = LoginAudit.Failure(email.Value, null, LoginMethod.Code, LoginCodeErrors.InvalidCode, "203.0.113.10", "tests", NowUtc);

        await factory.ExecuteDbContextAsync(db =>
        {
            new LoginAuditRepository(db).Add(audit);
            return db.SaveChangesAsync(Ct);
        });

        var saved = await factory.ExecuteDbContextAsync(db => db.LoginAudits.SingleAsync(a => a.Email == email.Value, Ct));
        Assert.Equal(LoginMethod.Code, saved.Method);
        Assert.Equal(LoginCodeErrors.InvalidCode, saved.FailureReason);
    }

    private static Email UniqueEmail() =>
        Email.Create("codes-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com").Value;

    private static LoginCode Issue(Email email, DateTime nowUtc) =>
        LoginCode.Issue(email, "hash-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture), nowUtc, TimeSpan.FromMinutes(10), 5);

    private Task SaveAsync(params LoginCode[] codes) =>
        factory.ExecuteDbContextAsync(db =>
        {
            db.LoginCodes.AddRange(codes);
            return db.SaveChangesAsync(Ct);
        });
}
```

`tests/ArquitecturaBase.Api.IntegrationTests/Persistence/IdentityModelTests.cs`:

```csharp
using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

[Collection(ApiTestGroup.Name)]
public sealed class IdentityModelTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task New_users_get_a_version_7_id_the_default_profile_and_audit_dates()
    {
        var user = NewUser();

        await factory.ExecuteDbContextAsync(db =>
        {
            db.Users.Add(user);
            return db.SaveChangesAsync(Ct);
        });

        var saved = await factory.ExecuteDbContextAsync(db => db.Users.SingleAsync(u => u.Id == user.Id, Ct));
        Assert.Equal(7, saved.Id.Version);
        Assert.Equal("es", saved.Culture);
        Assert.Equal("America/Argentina/Buenos_Aires", saved.TimeZoneId);
        Assert.True(saved.IsActive);
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime, saved.CreatedAtUtc);
    }

    [Fact]
    public async Task Normalized_email_is_unique_in_the_database()
    {
        var first = NewUser();
        var second = NewUser();
        second.NormalizedEmail = first.NormalizedEmail;
        await factory.ExecuteDbContextAsync(db =>
        {
            db.Users.Add(first);
            return db.SaveChangesAsync(Ct);
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => factory.ExecuteDbContextAsync(db =>
        {
            db.Users.Add(second);
            return db.SaveChangesAsync(Ct);
        }));
    }

    private static ApplicationUser NewUser()
    {
        var email = "model-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com";

        return new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
        };
    }
}
```

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.LoginCodeRepositoryTests"`
Esperado: FALLA la compilación (`'LoginCodeRepository' no existe`).

- [ ] **Paso 3: usuario y rol**

`Identity/ApplicationUser.cs`:

```csharp
using ArquitecturaBase.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>Usuario de Identity con el perfil de la sección 4.3. El UserName es el email.</summary>
public sealed class ApplicationUser : IdentityUser<Guid>, IAuditable
{
    public const int DisplayNameMaxLength = 100;
    public const int CultureMaxLength = 10;
    public const int TimeZoneIdMaxLength = 64;
    public const string DefaultCulture = "es";
    public const string DefaultTimeZoneId = "America/Argentina/Buenos_Aires";

    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
    }

    public string? DisplayName { get; set; }

    public string Culture { get; set; } = DefaultCulture;

    /// <summary>Zona horaria IANA con la que el front muestra las fechas.</summary>
    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public Guid? ModifiedBy { get; private set; }
}
```

`Identity/ApplicationRole.cs`:

```csharp
using Microsoft.AspNetCore.Identity;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>Rol de Identity. Sus permisos son role claims de tipo "permission" (sección 5.6).</summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
    public const int DescriptionMaxLength = 256;

    public ApplicationRole()
    {
        Id = Guid.CreateVersion7();
    }

    public ApplicationRole(string name)
        : this()
    {
        Name = name;
    }

    public string? Description { get; set; }
}
```

- [ ] **Paso 4: contexto y configuraciones**

`Persistence/ApplicationDbContext.cs`:

```csharp
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence;

/// <summary>
/// Contexto de EF Core: Identity (usuarios y roles con Id Guid), las tablas propias y las claves de Data Protection.
/// Las entidades de OpenIddict llegan por las opciones que arma AddInfrastructure. Toma de este ensamblado un
/// IEntityTypeConfiguration por entidad.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IDataProtectionKeyContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<LoginCode> LoginCodes => Set<LoginCode>();

    public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        modelBuilder.ApplySoftDeleteQueryFilter();
    }
}
```

`Persistence/Configurations/ApplicationUserConfiguration.cs`:

```csharp
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.DisplayName).HasMaxLength(ApplicationUser.DisplayNameMaxLength);
        builder.Property(user => user.Culture).HasMaxLength(ApplicationUser.CultureMaxLength);
        builder.Property(user => user.TimeZoneId).HasMaxLength(ApplicationUser.TimeZoneIdMaxLength);

        // Identity valida que el email no se repita, pero la base no lo garantizaba: su índice EmailIndex pasa a único.
        builder.HasIndex(user => user.NormalizedEmail).IsUnique();
    }
}
```

`Persistence/Configurations/ApplicationRoleConfiguration.cs`:

```csharp
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> builder) =>
        builder.Property(role => role.Description).HasMaxLength(ApplicationRole.DescriptionMaxLength);
}
```

`Persistence/Configurations/LoginCodeConfiguration.cs`:

```csharp
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

internal sealed class LoginCodeConfiguration : IEntityTypeConfiguration<LoginCode>
{
    public const int CodeHashMaxLength = 128;

    public void Configure(EntityTypeBuilder<LoginCode> builder)
    {
        builder.Property(code => code.Email).HasMaxLength(Email.MaxLength);
        builder.Property(code => code.CodeHash).HasMaxLength(CodeHashMaxLength);
        builder.Ignore(code => code.AttemptsLeft);

        // Todas las búsquedas son por email, del código más nuevo al más viejo.
        builder.HasIndex(code => new { code.Email, code.CreatedAtUtc });
    }
}
```

`Persistence/Configurations/LoginAuditConfiguration.cs`:

```csharp
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

internal sealed class LoginAuditConfiguration : IEntityTypeConfiguration<LoginAudit>
{
    public const int MethodMaxLength = 20;
    public const int FailureReasonMaxLength = 100;
    public const int IpAddressMaxLength = 45;

    public void Configure(EntityTypeBuilder<LoginAudit> builder)
    {
        builder.Property(audit => audit.Email).HasMaxLength(Email.MaxLength);
        builder.Property(audit => audit.Method).HasConversion<string>().HasMaxLength(MethodMaxLength);
        builder.Property(audit => audit.FailureReason).HasMaxLength(FailureReasonMaxLength);
        builder.Property(audit => audit.IpAddress).HasMaxLength(IpAddressMaxLength);
        builder.Property(audit => audit.UserAgent).HasMaxLength(LoginAudit.MaxUserAgentLength);

        builder.HasIndex(audit => audit.OccurredAtUtc);
        builder.HasIndex(audit => audit.UserId);
    }
}
```

- [ ] **Paso 5: repositorios y registro**

`Persistence/Repositories/LoginCodeRepository.cs`:

```csharp
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class LoginCodeRepository(ApplicationDbContext dbContext) : ILoginCodeRepository
{
    public Task<LoginCode?> GetLatestAsync(Email email, CancellationToken cancellationToken) =>
        dbContext.LoginCodes
            .Where(code => code.Email == email.Value && code.InvalidatedAtUtc == null)
            .OrderByDescending(code => code.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LoginCode>> ListActiveAsync(Email email, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var candidates = await dbContext.LoginCodes
            .Where(code => code.Email == email.Value && code.ConsumedAtUtc == null && code.InvalidatedAtUtc == null)
            .ToListAsync(cancellationToken);

        // La regla de "activo" vive en el dominio; acá solo se acota la consulta.
        return candidates.Where(code => code.IsActive(nowUtc)).ToList();
    }

    public async Task<IReadOnlyList<DateTime>> ListRequestTimesSinceAsync(
        Email email,
        DateTime sinceUtc,
        CancellationToken cancellationToken) =>
        await dbContext.LoginCodes
            .Where(code => code.Email == email.Value && code.CreatedAtUtc > sinceUtc)
            .OrderBy(code => code.CreatedAtUtc)
            .Select(code => code.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public void Add(LoginCode loginCode) => dbContext.LoginCodes.Add(loginCode);
}
```

`Persistence/Repositories/LoginAuditRepository.cs`:

```csharp
using ArquitecturaBase.Domain.Authentication;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class LoginAuditRepository(ApplicationDbContext dbContext) : ILoginAuditRepository
{
    public void Add(LoginAudit audit) => dbContext.LoginAudits.Add(audit);
}
```

En `DependencyInjection.AddInfrastructure`, después de `IUnitOfWork` (con los usings `ArquitecturaBase.Domain.Authentication` y `ArquitecturaBase.Infrastructure.Persistence.Repositories`):

```csharp
        services.AddScoped<ILoginCodeRepository, LoginCodeRepository>();
        services.AddScoped<ILoginAuditRepository, LoginAuditRepository>();
```

- [ ] **Paso 6: correr y ver que pasa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj
dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj
```

Esperado:
- 0 advertencias.
- Integración en verde: los 7 tests nuevos y todos los de la Fase 1. `EnsureCreated` ahora crea también las tablas de Identity.
- Arquitectura en verde.

- [ ] **Paso 7: commit**

```bash
git add Directory.Packages.props src/ArquitecturaBase.Infrastructure tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "feat: agregar Identity al modelo con los códigos de ingreso y su auditoría" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 13: Generador y hash HMAC de códigos

**Archivos:**
- Crear: `src/ArquitecturaBase.Infrastructure/Security/LoginCodeGenerator.cs`, `LoginCodeHasher.cs`, `LoginCodeHashOptions.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/DependencyInjection.cs`, `src/ArquitecturaBase.Api/appsettings.Development.json`, `tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Security/LoginCodeSecurityTests.cs`

- [ ] **Paso 1: test que falla**

```csharp
using System.ComponentModel.DataAnnotations;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Security;

public sealed class LoginCodeSecurityTests
{
    private static readonly Email Ana = Email.Create("ana@example.com").Value;
    private static readonly Email Beto = Email.Create("beto@example.com").Value;

    [Fact]
    public void Codes_have_the_configured_number_of_digits_and_vary()
    {
        var generator = new LoginCodeGenerator(Options.Create(new LoginCodeOptions { Length = 8 }));

        var codes = Enumerable.Range(0, 1000).Select(_ => generator.Generate()).ToList();

        Assert.All(codes, code => Assert.Matches("^[0-9]{8}$", code));
        Assert.True(codes.Distinct().Count() > 990);
    }

    [Fact]
    public void Hash_is_stable_hexadecimal_and_does_not_contain_the_code()
    {
        var hasher = Hasher(ApiFactory.TestHashKey);

        var hash = hasher.Hash(Ana, "123456");

        Assert.Equal(hash, hasher.Hash(Ana, "123456"));
        Assert.Matches("^[0-9A-F]{64}$", hash);
        Assert.DoesNotContain("123456", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void Hash_depends_on_the_email_the_code_and_the_key()
    {
        var hash = Hasher(ApiFactory.TestHashKey).Hash(Ana, "123456");

        Assert.NotEqual(hash, Hasher(ApiFactory.TestHashKey).Hash(Beto, "123456"));
        Assert.NotEqual(hash, Hasher(ApiFactory.TestHashKey).Hash(Ana, "123457"));
        Assert.NotEqual(hash, Hasher(Convert.ToBase64String(new byte[32])).Hash(Ana, "123456"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==")]
    public void Missing_invalid_or_short_keys_are_rejected(string key)
    {
        var options = new LoginCodeHashOptions { HashKey = key };

        Assert.False(Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true));
    }

    private static LoginCodeHasher Hasher(string key) => new(Options.Create(new LoginCodeHashOptions { HashKey = key }));
}
```

En `ApiFactory`, agregar la constante y la configuración:

```csharp
    /// <summary>Clave HMAC de los tests: los bytes 0 a 31 en base64. Nunca se usa fuera de los tests.</summary>
    public const string TestHashKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
```

```csharp
        builder.UseSetting("Authentication:LoginCode:HashKey", TestHashKey);
```

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Security.LoginCodeSecurityTests"`
Esperado: FALLA la compilación (`'LoginCodeGenerator' no existe`).

- [ ] **Paso 2: implementación**

`Security/LoginCodeHashOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Infrastructure.Security;

/// <summary>
/// Clave secreta del HMAC de los códigos (Authentication:LoginCode:HashKey): al menos 32 bytes aleatorios en base64.
/// En desarrollo está en appsettings.Development.json; en producción, en variables de entorno o un almacén de secretos.
/// </summary>
internal sealed class LoginCodeHashOptions : IValidatableObject
{
    public const string SectionName = "Authentication:LoginCode";
    public const int MinKeyBytes = 32;

    [Required]
    public string HashKey { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var buffer = new byte[HashKey.Length];

        if (!Convert.TryFromBase64String(HashKey, buffer, out var length) || length < MinKeyBytes)
        {
            yield return new ValidationResult(
                "HashKey must be at least 32 random bytes encoded in base64.", [nameof(HashKey)]);
        }
    }
}
```

`Security/LoginCodeHasher.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Security;

/// <summary>HMAC-SHA256 del código con una clave secreta. La comparación en tiempo constante la hace LoginCode.</summary>
internal sealed class LoginCodeHasher(IOptions<LoginCodeHashOptions> options) : ILoginCodeHasher
{
    private readonly byte[] _key = Convert.FromBase64String(options.Value.HashKey);

    public string Hash(Email email, string code)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(code);

        // El email entra en el mensaje: el mismo código para otro email da otro hash.
        var message = Encoding.UTF8.GetBytes(email.Value + ":" + code);

        return Convert.ToHexString(HMACSHA256.HashData(_key, message));
    }
}
```

`Security/LoginCodeGenerator.cs`:

```csharp
using System.Security.Cryptography;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Application.Features.Auth;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Security;

internal sealed class LoginCodeGenerator(IOptions<LoginCodeOptions> options) : ILoginCodeGenerator
{
    private const string Digits = "0123456789";

    public string Generate() => RandomNumberGenerator.GetString(Digits, options.Value.Length);
}
```

En `DependencyInjection.AddInfrastructure`, después de los repositorios (con los usings `ArquitecturaBase.Application.Abstractions.Security` y `ArquitecturaBase.Infrastructure.Security`):

```csharp
        services.AddOptions<LoginCodeHashOptions>()
            .BindConfiguration(LoginCodeHashOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ILoginCodeGenerator, LoginCodeGenerator>();
        services.AddSingleton<ILoginCodeHasher, LoginCodeHasher>();
```

- [ ] **Paso 3: clave de desarrollo**

Generar una clave al azar con PowerShell:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

`src/ArquitecturaBase.Api/appsettings.Development.json` queda así, con la clave recién generada:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  },
  "Authentication": {
    "LoginCode": {
      "HashKey": "<la clave generada arriba>"
    }
  }
}
```

La clave solo sirve para desarrollo local. No es la de los tests (`ApiFactory.TestHashKey`), y producción usa otra, cargada como secreto.

- [ ] **Paso 4: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`
Esperado: todo en verde (7 tests nuevos). Sin `HashKey`, la Api no arrancaría (`ValidateOnStart`).

- [ ] **Paso 5: commit**

```bash
git add src/ArquitecturaBase.Infrastructure src/ArquitecturaBase.Api/appsettings.Development.json tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "feat: generar los códigos de ingreso y guardarlos con HMAC-SHA256" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 14: Plantillas de email y textos

Esta tarea sigue la sección 8 del spec:
- `_Layout.html` + `LoginCode.html` son recursos embebidos, con tablas y estilos inline.
- `{{Marcadores}}` se reemplazan escapando el HTML.
- Los textos salen de `Emails.resx`, en el idioma del usuario.
- El asunto empieza con el código.

**Archivos:**
- Crear en `src/ArquitecturaBase.Infrastructure/Emails/`:
  - `EmailOptions.cs`, `EmailTemplateRenderer.cs`, `EmailRegistration.cs`
  - `Templates/_Layout.html`, `Templates/LoginCode.html`
  - `Resources/Emails.resx`, `Resources/Emails.en.resx`, `Resources/EmailTexts.cs`
- Modificar: el csproj de Infrastructure y `DependencyInjection.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Emails/EmailTemplateRendererTests.cs`

- [ ] **Paso 1: test que falla**

```csharp
using System.Collections;
using System.Globalization;
using ArquitecturaBase.Infrastructure.Emails;
using ArquitecturaBase.Infrastructure.Emails.Resources;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

public sealed class EmailTemplateRendererTests
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Spanish_email_starts_the_subject_with_the_code()
    {
        var message = Renderer().RenderLoginCode("ana@example.com", "482913", 10, Spanish);

        Assert.Equal("ana@example.com", message.To);
        Assert.Equal("482913 es tu código de acceso a Arquitectura Base", message.Subject);
        Assert.Contains("482913", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("lang=\"es\"", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Vence en 10 minutos.", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void English_email_uses_the_english_texts()
    {
        var message = Renderer().RenderLoginCode("ana@example.com", "482913", 10, English);

        Assert.Equal("482913 is your Arquitectura Base access code", message.Subject);
        Assert.Contains("It expires in 10 minutes.", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("lang=\"en\"", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Values_are_html_encoded()
    {
        var message = Renderer(appName: "A&B <Test>").RenderLoginCode("ana@example.com", "482913", 10, English);

        Assert.Contains("A&amp;B &lt;Test&gt;", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<Test>", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Logo_is_shown_only_when_configured()
    {
        var withLogo = Renderer(logoUrl: "https://cdn.example.com/logo.png").RenderLoginCode("ana@example.com", "482913", 10, English);
        var withoutLogo = Renderer().RenderLoginCode("ana@example.com", "482913", 10, English);

        Assert.Contains("<img src=\"https://cdn.example.com/logo.png\"", withLogo.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", withoutLogo.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_texts_have_the_same_keys_in_spanish_and_english()
    {
        Assert.Equal(Keys(CultureInfo.InvariantCulture), Keys(English));
    }

    private static EmailTemplateRenderer Renderer(string appName = "Arquitectura Base", string? logoUrl = null) =>
        new(Options.Create(new EmailOptions { AppName = appName, LogoUrl = logoUrl }));

    private static string[] Keys(CultureInfo culture) =>
        EmailTexts.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
```

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Emails.EmailTemplateRendererTests"`
Esperado: FALLA la compilación (`'Emails' no existe`).

- [ ] **Paso 2: plantillas y recursos embebidos**

En el csproj de Infrastructure agregar:

```xml
  <PropertyGroup>
    <!-- Los .resx sin sufijo están en español. -->
    <NeutralLanguage>es</NeutralLanguage>
  </PropertyGroup>

  <ItemGroup>
    <!-- Nombre fijo: "EmailTemplates.<archivo>", sin depender de cómo MSBuild arma el nombre desde la carpeta. -->
    <EmbeddedResource Include="Emails\Templates\*.html" LogicalName="EmailTemplates.%(Filename)%(Extension)" />
  </ItemGroup>
```

`Emails/Templates/_Layout.html`:

```html
<!DOCTYPE html>
<html lang="{{Lang}}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{Title}}</title>
</head>
<body style="margin:0;padding:0;background-color:#f4f5f7;">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:#f4f5f7;">
  <tr>
    <td align="center" style="padding:24px 12px;">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;background-color:#ffffff;border-radius:8px;">
        <tr>
          <td style="padding:24px 32px;border-bottom:1px solid #e5e7eb;font-family:Arial,Helvetica,sans-serif;font-size:18px;font-weight:bold;color:#111827;">{{Header}}</td>
        </tr>
        <tr>
          <td style="padding:32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:22px;color:#374151;">{{Content}}</td>
        </tr>
        <tr>
          <td style="padding:16px 32px;border-top:1px solid #e5e7eb;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#6b7280;">{{Footer}}</td>
        </tr>
      </table>
    </td>
  </tr>
</table>
</body>
</html>
```

`Emails/Templates/LoginCode.html`:

```html
<h1 style="margin:0 0 16px;font-size:20px;line-height:28px;color:#111827;">{{Title}}</h1>
<p style="margin:0 0 16px;">{{Intro}}</p>
<p style="margin:0 0 16px;font-family:'Courier New',Courier,monospace;font-size:32px;font-weight:bold;letter-spacing:8px;color:#111827;">{{Code}}</p>
<p style="margin:0;">{{Expiry}}</p>
```

`Emails/Resources/Emails.resx`: la misma cabecera `<resheader>` que `src/ArquitecturaBase.Application/Resources/Errors.resx` (sus 14 primeras líneas, sin cambios), y después:

```xml
  <data name="LoginCode.Subject" xml:space="preserve"><value>{0} es tu código de acceso a {1}</value></data>
  <data name="LoginCode.Title" xml:space="preserve"><value>Tu código de acceso</value></data>
  <data name="LoginCode.Intro" xml:space="preserve"><value>Usá este código para ingresar a {0}:</value></data>
  <data name="LoginCode.Expiry" xml:space="preserve"><value>Vence en {0} minutos. Si no lo pediste, ignorá este email: nadie puede entrar a tu cuenta sin el código.</value></data>
  <data name="Layout.Footer" xml:space="preserve"><value>Este email lo envió {0} automáticamente. No lo respondas.</value></data>
</root>
```

`Emails/Resources/Emails.en.resx`, con la misma cabecera:

```xml
  <data name="LoginCode.Subject" xml:space="preserve"><value>{0} is your {1} access code</value></data>
  <data name="LoginCode.Title" xml:space="preserve"><value>Your access code</value></data>
  <data name="LoginCode.Intro" xml:space="preserve"><value>Use this code to sign in to {0}:</value></data>
  <data name="LoginCode.Expiry" xml:space="preserve"><value>It expires in {0} minutes. If you didn't request it, ignore this email: nobody can access your account without the code.</value></data>
  <data name="Layout.Footer" xml:space="preserve"><value>This email was sent automatically by {0}. Please don't reply.</value></data>
</root>
```

`Emails/Resources/EmailTexts.cs`:

```csharp
using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Infrastructure.Emails.Resources;

/// <summary>Textos de Emails.resx en el idioma del destinatario (el Culture de su perfil).</summary>
internal static class EmailTexts
{
    internal static ResourceManager ResourceManager { get; } =
        new("ArquitecturaBase.Infrastructure.Emails.Resources.Emails", typeof(EmailTexts).Assembly);

    public static string Get(string key, CultureInfo culture) =>
        ResourceManager.GetString(key, culture) ?? throw new InvalidOperationException($"Missing email text '{key}'.");

    public static string Format(string key, CultureInfo culture, params object[] arguments) =>
        string.Format(culture, Get(key, culture), arguments);
}
```

- [ ] **Paso 3: opciones y renderer**

`Emails/EmailOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Infrastructure.Emails;

internal sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Nombre del producto en los emails. Provisorio hasta definir el definitivo (sección 11 del spec).</summary>
    [Required]
    public string AppName { get; init; } = "Arquitectura Base";

    /// <summary>PNG con URL absoluta: Gmail no muestra SVG (sección 8). Sin logo, el encabezado muestra el nombre.</summary>
    [Url]
    public string? LogoUrl { get; init; }
}
```

`Emails/EmailTemplateRenderer.cs`:

```csharp
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Abstractions.Emails;
using ArquitecturaBase.Infrastructure.Emails.Resources;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// Arma los emails con las plantillas embebidas: cada {{Marcador}} se reemplaza por un valor ya escapado.
/// Un marcador sin valor es un error de programación y lanza.
/// </summary>
internal sealed partial class EmailTemplateRenderer(IOptions<EmailOptions> options) : IEmailTemplateRenderer
{
    private const string LayoutTemplate = "_Layout.html";
    private const string LoginCodeTemplate = "LoginCode.html";

    private static readonly ConcurrentDictionary<string, string> Templates = new(StringComparer.Ordinal);

    public EmailMessage RenderLoginCode(string to, string code, int lifetimeMinutes, CultureInfo culture)
    {
        var appName = options.Value.AppName;
        var title = EmailTexts.Get("LoginCode.Title", culture);
        var intro = EmailTexts.Format("LoginCode.Intro", culture, appName);
        var expiry = EmailTexts.Format("LoginCode.Expiry", culture, lifetimeMinutes);
        var footer = EmailTexts.Format("Layout.Footer", culture, appName);

        var content = Fill(LoginCodeTemplate, new Dictionary<string, string>
        {
            ["Title"] = Encode(title),
            ["Intro"] = Encode(intro),
            ["Code"] = Encode(code),
            ["Expiry"] = Encode(expiry),
        });

        var html = Fill(LayoutTemplate, new Dictionary<string, string>
        {
            ["Lang"] = Encode(culture.TwoLetterISOLanguageName),
            ["Title"] = Encode(title),
            ["Header"] = HeaderHtml(),
            ["Content"] = content,
            ["Footer"] = Encode(footer),
        });

        var paragraphBreak = Environment.NewLine + Environment.NewLine;
        var text = string.Join(paragraphBreak, title, intro, code, expiry, footer);

        return new EmailMessage(to, EmailTexts.Format("LoginCode.Subject", culture, code, appName), html, text);
    }

    private string HeaderHtml()
    {
        var settings = options.Value;

        return string.IsNullOrWhiteSpace(settings.LogoUrl)
            ? Encode(settings.AppName)
            : $"<img src=\"{Encode(settings.LogoUrl)}\" alt=\"{Encode(settings.AppName)}\" height=\"32\" style=\"display:block;border:0;height:32px;\">";
    }

    private static string Fill(string templateName, IReadOnlyDictionary<string, string> values) =>
        PlaceholderPattern().Replace(Load(templateName), match =>
            values.TryGetValue(match.Groups["name"].Value, out var value)
                ? value
                : throw new InvalidOperationException($"Template '{templateName}' has no value for '{match.Value}'."));

    private static string Load(string templateName) =>
        Templates.GetOrAdd(templateName, static name =>
        {
            using var stream = typeof(EmailTemplateRenderer).Assembly.GetManifestResourceStream("EmailTemplates." + name)
                ?? throw new InvalidOperationException($"Missing email template '{name}'.");
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        });

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    [GeneratedRegex(@"\{\{(?<name>[A-Za-z]+)\}\}")]
    private static partial Regex PlaceholderPattern();
}
```

`Emails/EmailRegistration.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Emails;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Emails;

internal static class EmailRegistration
{
    public static IServiceCollection AddEmails(this IServiceCollection services)
    {
        services.AddOptions<EmailOptions>()
            .BindConfiguration(EmailOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

        return services;
    }
}
```

En `DependencyInjection.AddInfrastructure`, antes del `return` (con `using ArquitecturaBase.Infrastructure.Emails;`):

```csharp
        services.AddEmails();
```

- [ ] **Paso 4: correr y ver que pasa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj
```

Esperado: 0 advertencias. Todo en verde (5 tests nuevos).

- [ ] **Paso 5: commit**

```bash
git add src/ArquitecturaBase.Infrastructure tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "feat: agregar la plantilla del email con el código en español e inglés" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 15: Envío de emails: MailKit, archivos .eml, cola y reintentos

Cómo se envía cada email:
- **Envío real:** por SMTP con MailKit (Gmail).
- **En desarrollo:** se guarda como archivo `.eml` en `src/ArquitecturaBase.Api/.emails/`. Se elige con `Email:Delivery`.
- **Cola:** los casos de uso encolan en un canal acotado. `EmailBackgroundService` envía cada email con hasta 3 intentos y espera exponencial entre ellos (sección 4.3). Si fallan los 3, registra el error y sigue con el próximo.

**Archivos:**
- Crear en `src/ArquitecturaBase.Infrastructure/Emails/`:
  - `EmailDelivery.cs`, `SmtpOptions.cs`, `SmtpOptionsValidator.cs`
  - `MimeMessageFactory.cs`, `SmtpEmailSender.cs`, `PickupDirectoryEmailSender.cs`
  - `EmailQueue.cs`, `EmailBackgroundService.cs`
- Modificar:
  - `Emails/EmailOptions.cs`, `Emails/EmailRegistration.cs`, el csproj de Infrastructure y `Directory.Packages.props`
  - `src/ArquitecturaBase.Api/appsettings.json`, `appsettings.Development.json`
  - `.gitignore`, `tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Emails/EmailDeliveryTests.cs`, `EmailBackgroundServiceTests.cs`

- [ ] **Paso 1: paquete**

`Directory.Packages.props`, grupo `Infrastructure`: `<PackageVersion Include="MailKit" Version="4.18.0" />`. En el csproj de Infrastructure: `<PackageReference Include="MailKit" />`.

- [ ] **Paso 2: tests que fallan**

`tests/ArquitecturaBase.Api.IntegrationTests/Emails/EmailDeliveryTests.cs`:

```csharp
using System.Globalization;
using ArquitecturaBase.Application.Abstractions.Emails;
using ArquitecturaBase.Infrastructure.Emails;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

public sealed class EmailDeliveryTests
{
    private static readonly SmtpOptions Sender = new() { FromName = "Arquitectura Base", FromAddress = "no-reply@example.com" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Message_has_the_sender_the_recipient_and_both_bodies()
    {
        using var mime = MimeMessageFactory.Create(new EmailMessage("ana@example.com", "Asunto", "<p>Hola</p>", "Hola"), Sender);

        var from = Assert.IsType<MailboxAddress>(Assert.Single(mime.From));
        Assert.Equal("no-reply@example.com", from.Address);
        Assert.Equal("Arquitectura Base", from.Name);
        Assert.Equal("ana@example.com", Assert.IsType<MailboxAddress>(Assert.Single(mime.To)).Address);
        Assert.Equal("Asunto", mime.Subject);
        Assert.Equal("<p>Hola</p>", mime.HtmlBody);
        Assert.Equal("Hola", mime.TextBody);
    }

    [Fact]
    public async Task Pickup_directory_sender_saves_an_eml_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "arquitecturabase-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        try
        {
            var sender = new PickupDirectoryEmailSender(
                Options.Create(new EmailOptions { PickupDirectory = directory }),
                Options.Create(Sender),
                new TestHostEnvironment(),
                TimeProvider.System,
                NullLogger<PickupDirectoryEmailSender>.Instance);

            await sender.SendAsync(new EmailMessage("ana@example.com", "123456 es tu código", "<p>123456</p>", "123456"), Ct);

            var file = Assert.Single(Directory.GetFiles(directory, "*.eml"));
            using var saved = await MimeMessage.LoadAsync(file, Ct);
            Assert.Equal("123456 es tu código", saved.Subject);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Smtp_settings_are_required_only_when_sending_by_smtp()
    {
        var empty = new SmtpOptions();

        Assert.True(Validator(EmailDelivery.PickupDirectory).Validate(null, empty).Skipped);

        var result = Validator(EmailDelivery.Smtp).Validate(null, empty);
        Assert.True(result.Failed);
        Assert.Contains(nameof(SmtpOptions.Host), result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains(nameof(SmtpOptions.Password), result.FailureMessage, StringComparison.Ordinal);
    }

    private static SmtpOptionsValidator Validator(EmailDelivery delivery) =>
        new(Options.Create(new EmailOptions { Delivery = delivery }));

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
```

`tests/ArquitecturaBase.Api.IntegrationTests/Emails/EmailBackgroundServiceTests.cs`:

```csharp
using System.Collections.Concurrent;
using ArquitecturaBase.Application.Abstractions.Emails;
using ArquitecturaBase.Infrastructure.Emails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

public sealed class EmailBackgroundServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Failed_sends_are_retried_until_they_succeed()
    {
        var sender = new FlakyEmailSender(failuresPerMessage: 2);

        await RunAsync(sender, async queue =>
        {
            await queue.EnqueueAsync(Message("ana@example.com"), Ct);
            await WaitUntilAsync(() => sender.Sent.Count == 1);
        });

        Assert.Equal(3, sender.AttemptsFor("ana@example.com"));
    }

    [Fact]
    public async Task After_three_failed_attempts_the_email_is_dropped_and_the_next_one_is_sent()
    {
        var sender = new FlakyEmailSender(failuresPerMessage: 0, alwaysFailFor: "down@example.com");

        await RunAsync(sender, async queue =>
        {
            await queue.EnqueueAsync(Message("down@example.com"), Ct);
            await queue.EnqueueAsync(Message("ana@example.com"), Ct);
            await WaitUntilAsync(() => sender.Sent.Count == 1);
        });

        Assert.Equal(EmailBackgroundService.MaxAttempts, sender.AttemptsFor("down@example.com"));
        Assert.Equal("ana@example.com", Assert.Single(sender.Sent).To);
    }

    private static async Task RunAsync(IEmailSender sender, Func<EmailQueue, Task> act)
    {
        var services = new ServiceCollection();
        services.AddSingleton(sender);
        await using var provider = services.BuildServiceProvider();

        var queue = new EmailQueue();
        using var service = new EmailBackgroundService(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new EmailOptions { RetryDelaySeconds = 0 }),
            TimeProvider.System,
            NullLogger<EmailBackgroundService>.Instance);

        await service.StartAsync(Ct);

        try
        {
            await act(queue);
        }
        finally
        {
            await service.StopAsync(Ct);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private static EmailMessage Message(string to) => new(to, "Asunto", "<p>Hola</p>", "Hola");

    private sealed class FlakyEmailSender(int failuresPerMessage, string? alwaysFailFor = null) : IEmailSender
    {
        private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<EmailMessage> _sent = new();

        public IReadOnlyCollection<EmailMessage> Sent => _sent;

        public int AttemptsFor(string to) => _attempts.GetValueOrDefault(to);

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            var attempt = _attempts.AddOrUpdate(message.To, 1, (_, previous) => previous + 1);

            if (message.To == alwaysFailFor || attempt <= failuresPerMessage)
            {
                throw new InvalidOperationException("The SMTP server is down.");
            }

            _sent.Enqueue(message);

            return Task.CompletedTask;
        }
    }
}
```

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Emails.EmailDeliveryTests"`
Esperado: FALLA la compilación (`'SmtpOptions' no existe`).

- [ ] **Paso 3: opciones**

`Emails/EmailDelivery.cs`:

```csharp
namespace ArquitecturaBase.Infrastructure.Emails;

internal enum EmailDelivery
{
    /// <summary>Envío real por SMTP (Gmail con contraseña de aplicación).</summary>
    Smtp,

    /// <summary>Archivos .eml en una carpeta local, para desarrollo.</summary>
    PickupDirectory,
}
```

`Emails/EmailOptions.cs` queda así:

```csharp
using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Infrastructure.Emails;

internal sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailDelivery Delivery { get; init; } = EmailDelivery.Smtp;

    /// <summary>Nombre del producto en los emails. Provisorio hasta definir el definitivo (sección 11 del spec).</summary>
    [Required]
    public string AppName { get; init; } = "Arquitectura Base";

    /// <summary>PNG con URL absoluta: Gmail no muestra SVG (sección 8). Sin logo, el encabezado muestra el nombre.</summary>
    [Url]
    public string? LogoUrl { get; init; }

    /// <summary>Carpeta de los .eml con <see cref="EmailDelivery.PickupDirectory"/>, relativa a la raíz de la Api.</summary>
    [Required]
    public string PickupDirectory { get; init; } = ".emails";

    /// <summary>Espera antes del primer reintento; se duplica en cada intento.</summary>
    [Range(0, 300)]
    public int RetryDelaySeconds { get; init; } = 2;
}
```

`Emails/SmtpOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using MailKit.Security;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// Cuenta que envía los emails (Email:Smtp, sección 6.8). La contraseña es una contraseña de aplicación de Gmail y
/// va en user-secrets o en variables de entorno. Remitente: FromName y FromAddress, también para los .eml.
/// </summary>
internal sealed class SmtpOptions
{
    public const string SectionName = "Email:Smtp";

    [Required]
    public string Host { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; init; } = 587;

    public SecureSocketOptions Security { get; init; } = SecureSocketOptions.StartTls;

    [Required]
    public string UserName { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    [Required]
    public string FromName { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    public string FromAddress { get; init; } = string.Empty;
}
```

`Emails/SmtpOptionsValidator.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>Los datos de SMTP son obligatorios solo si los emails salen por SMTP.</summary>
internal sealed class SmtpOptionsValidator(IOptions<EmailOptions> emailOptions) : IValidateOptions<SmtpOptions>
{
    public ValidateOptionsResult Validate(string? name, SmtpOptions options)
    {
        if (emailOptions.Value.Delivery != EmailDelivery.Smtp)
        {
            return ValidateOptionsResult.Skip;
        }

        var results = new List<ValidationResult>();

        return Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(results.Select(result => $"{SmtpOptions.SectionName}: {result.ErrorMessage}"));
    }
}
```

- [ ] **Paso 4: envío**

`Emails/MimeMessageFactory.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Emails;
using MimeKit;

namespace ArquitecturaBase.Infrastructure.Emails;

internal static class MimeMessageFactory
{
    public static MimeMessage Create(EmailMessage message, SmtpOptions sender)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(sender.FromName, sender.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        return mime;
    }
}
```

`Emails/SmtpEmailSender.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Emails;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var mime = MimeMessageFactory.Create(message, settings);
        using var client = new SmtpClient();

        await client.ConnectAsync(settings.Host, settings.Port, settings.Security, cancellationToken);
        await client.AuthenticateAsync(settings.UserName, settings.Password, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
```

`Emails/PickupDirectoryEmailSender.cs`:

```csharp
using System.Globalization;
using ArquitecturaBase.Application.Abstractions.Emails;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>Guarda cada email como .eml para abrirlo con cualquier cliente de correo. Solo para desarrollo.</summary>
internal sealed partial class PickupDirectoryEmailSender(
    IOptions<EmailOptions> emailOptions,
    IOptions<SmtpOptions> smtpOptions,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<PickupDirectoryEmailSender> logger)
    : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(environment.ContentRootPath, emailOptions.Value.PickupDirectory);
        Directory.CreateDirectory(directory);

        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"{timeProvider.GetUtcNow():yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.eml");
        var path = Path.Combine(directory, fileName);

        using var mime = MimeMessageFactory.Create(message, smtpOptions.Value);
        await mime.WriteToAsync(path, cancellationToken);

        LogEmailSaved(logger, path);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email saved to {Path}")]
    private static partial void LogEmailSaved(ILogger logger, string path);
}
```

`Emails/EmailQueue.cs`:

```csharp
using System.Threading.Channels;
using ArquitecturaBase.Application.Abstractions.Emails;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>Canal acotado entre los casos de uso y EmailBackgroundService. Si se llena, el que encola espera.</summary>
internal sealed class EmailQueue : IEmailQueue
{
    public const int Capacity = 100;

    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(message, cancellationToken);

    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
```

`Emails/EmailBackgroundService.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Emails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// Envía los emails encolados de a uno, con hasta 3 intentos y espera exponencial. Si fallan todos, registra el
/// error y sigue: el usuario puede pedir otro código. El log nunca incluye el destinatario ni el contenido.
/// </summary>
internal sealed partial class EmailBackgroundService(
    EmailQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<EmailOptions> options,
    TimeProvider timeProvider,
    ILogger<EmailBackgroundService> logger)
    : BackgroundService
{
    public const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.ReadAllAsync(stoppingToken))
        {
            await SendWithRetriesAsync(message, stoppingToken);
        }
    }

    private async Task SendWithRetriesAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(message, cancellationToken);

                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (attempt == MaxAttempts)
                {
                    LogEmailDropped(logger, MaxAttempts, exception);

                    return;
                }

                LogEmailRetry(logger, attempt, MaxAttempts, exception);

                var delay = TimeSpan.FromSeconds(options.Value.RetryDelaySeconds * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending an email failed (attempt {Attempt} of {MaxAttempts}); retrying")]
    private static partial void LogEmailRetry(ILogger logger, int attempt, int maxAttempts, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending an email failed after {MaxAttempts} attempts; the email was dropped")]
    private static partial void LogEmailDropped(ILogger logger, int maxAttempts, Exception exception);
}
```

`Emails/EmailRegistration.cs` queda así:

```csharp
using ArquitecturaBase.Application.Abstractions.Emails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

internal static class EmailRegistration
{
    public static IServiceCollection AddEmails(this IServiceCollection services)
    {
        services.AddOptions<EmailOptions>()
            .BindConfiguration(EmailOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SmtpOptions>()
            .BindConfiguration(SmtpOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SmtpOptions>, SmtpOptionsValidator>();

        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

        services.AddSingleton<EmailQueue>();
        services.AddSingleton<IEmailQueue>(serviceProvider => serviceProvider.GetRequiredService<EmailQueue>());
        services.AddHostedService<EmailBackgroundService>();

        // El envío real se elige por configuración: SMTP (Gmail) o archivos .eml en desarrollo.
        services.AddScoped<IEmailSender>(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<EmailOptions>>().Value.Delivery == EmailDelivery.Smtp
                ? ActivatorUtilities.CreateInstance<SmtpEmailSender>(serviceProvider)
                : ActivatorUtilities.CreateInstance<PickupDirectoryEmailSender>(serviceProvider));

        return services;
    }
}
```

- [ ] **Paso 5: configuración**

`src/ArquitecturaBase.Api/appsettings.json` queda así (sin secretos, sección 6.8):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Authentication": {
    "LoginCode": {
      "Length": 6,
      "LifetimeMinutes": 10,
      "MaxAttempts": 5,
      "ResendCooldownSeconds": 60,
      "MaxRequestsPerWindow": 5,
      "RequestWindowMinutes": 15
    }
  },
  "Email": {
    "Delivery": "Smtp",
    "AppName": "Arquitectura Base",
    "Smtp": {
      "Host": "smtp.gmail.com",
      "Port": 587,
      "Security": "StartTls",
      "UserName": "",
      "FromName": "Arquitectura Base",
      "FromAddress": ""
    }
  }
}
```

En `appsettings.Development.json`, agregar al mismo nivel que `Authentication`:

```json
  "Email": {
    "Delivery": "PickupDirectory",
    "Smtp": {
      "FromAddress": "no-reply@arquitecturabase.local"
    }
  }
```

Para probar el envío real con Gmail:
1. Cambiar `Delivery` a `Smtp`.
2. Cargar `UserName`, `FromAddress` y la contraseña de aplicación como user-secrets. Eso lo hace el usuario (ver README, Tarea 24).

`.gitignore`, al final:

```gitignore
# Emails de desarrollo guardados como .eml (Email:Delivery = PickupDirectory)
.emails/
```

En `ApiFactory.ConfigureWebHost`, junto a las otras llamadas a `UseSetting`:

```csharp
        // Los tests no envían emails de verdad; la Tarea 19 reemplaza IEmailSender por uno que los guarda en memoria.
        builder.UseSetting("Email:Delivery", "PickupDirectory");
```

- [ ] **Paso 6: correr y ver que pasa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj
```

Esperado: 0 advertencias. Todo en verde (5 tests nuevos).

- [ ] **Paso 7: commit**

```bash
git add .gitignore Directory.Packages.props src/ArquitecturaBase.Infrastructure src/ArquitecturaBase.Api/appsettings.json src/ArquitecturaBase.Api/appsettings.Development.json tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "feat: enviar los emails en segundo plano por SMTP o como archivos .eml" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 16: Identity: registro, `IdentityService`, `PermissionService` y seed de roles

Esta tarea registra Identity según los hechos verificados 1 a 4:
- `AddIdentityCore` + `AddIdentityCookies`;
- la cookie responde 401/403 en lugar de redirigir;
- el bloqueo es de 10 intentos y 15 minutos.

Además:
- Data Protection guarda sus claves en Postgres.
- `PermissionService` cachea los permisos de cada rol con HybridCache.
- El seed crea los roles con sus permisos y le da `Admin` al email de `Seed:AdminEmail`.

**Archivos:**
- Crear en `src/ArquitecturaBase.Infrastructure/`:
  - `Identity/IdentityRegistration.cs`, `IdentityService.cs`, `PermissionService.cs`, `SeedOptions.cs`, `ExternalClaimTypes.cs`, `IdentityResultExtensions.cs`
  - `Persistence/Seed/RoleSeeder.cs`, `SeedExtensions.cs`
- Crear: `src/ArquitecturaBase.Api/Services/RequestInfo.cs`
- Modificar:
  - `src/ArquitecturaBase.Infrastructure/DependencyInjection.cs`, el csproj de Infrastructure, `Directory.Packages.props`
  - `src/ArquitecturaBase.Api/DependencyInjection.cs`, `Program.cs`, `appsettings.json`, `appsettings.Development.json`
  - `tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Identity/IdentityServiceTests.cs`, `PermissionServiceTests.cs`, `SeedTests.cs`

- [ ] **Paso 1: paquete**

`Directory.Packages.props`, grupo `Infrastructure`: `<PackageVersion Include="Microsoft.Extensions.Caching.Hybrid" Version="10.10.0" />`. En el csproj de Infrastructure: `<PackageReference Include="Microsoft.Extensions.Caching.Hybrid" />`.

- [ ] **Paso 2: arnés**

En `ApiFactory`:
- Agregar la constante del administrador:

```csharp
    /// <summary>Recibe el rol Admin al crearse (Seed:AdminEmail).</summary>
    public const string AdminEmail = "admin@arquitecturabase.test";
```

- Agregar un helper para usar servicios dentro de un scope:

```csharp
    public async Task<T> ExecuteScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = Services.CreateAsyncScope();

        return await action(scope.ServiceProvider);
    }
```

- En `InitializeAsync`, después de `EnsureCreatedAsync` (con `using ArquitecturaBase.Infrastructure.Persistence.Seed;`):

```csharp
        // Los mismos datos base que en desarrollo: roles, permisos y, desde la Tarea 17, el cliente "web".
        await Services.SeedDatabaseAsync();
```

- En `ConfigureWebHost`, junto a los otros `UseSetting`:

```csharp
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
```

- En `ConfigureTestServices` (con `using Microsoft.AspNetCore.DataProtection;`):

```csharp
            // Claves en memoria: las de Postgres se leen al arrancar el host, antes de que exista el esquema.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
```

- [ ] **Paso 3: tests que fallan**

`tests/ArquitecturaBase.Api.IntegrationTests/Identity/IdentityServiceTests.cs`:

```csharp
using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Identity;

[Collection(ApiTestGroup.Name)]
public sealed class IdentityServiceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task New_users_are_confirmed_and_get_the_user_role()
    {
        var email = UniqueEmail("new");

        var (user, roles) = await WithIdentityAsync(async identity =>
        {
            var created = await identity.CreateAsync(email, "Ana", "en", Ct);
            return (created, await identity.GetRolesAsync(created.Id, Ct));
        });

        Assert.Equal(email.Value, user.Email);
        Assert.Equal("Ana", user.DisplayName);
        Assert.Equal("en", user.Culture);
        Assert.True(user.IsActive);
        Assert.Equal(["User"], roles);
        Assert.True(await factory.ExecuteDbContextAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.EmailConfirmed).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Admin_email_gets_the_admin_role()
    {
        var adminEmail = Email.Create(ApiFactory.AdminEmail).Value;

        var roles = await WithIdentityAsync(async identity =>
        {
            var admin = await identity.FindByEmailAsync(adminEmail, Ct) ?? await identity.CreateAsync(adminEmail, null, "es", Ct);
            return await identity.GetRolesAsync(admin.Id, Ct);
        });

        Assert.Contains("Admin", roles);
    }

    [Fact]
    public async Task Users_are_found_by_email_and_by_external_login()
    {
        var email = UniqueEmail("find");
        var created = await WithIdentityAsync(identity => identity.CreateAsync(email, null, "es", Ct));
        var login = new ExternalLogin("Google", "google-" + created.Id.ToString("N", CultureInfo.InvariantCulture), email.Value, true, null);

        await WithIdentityAsync(async identity =>
        {
            await identity.AddExternalLoginAsync(created.Id, login, Ct);
            return true;
        });

        Assert.Equal(created.Id, (await WithIdentityAsync(identity => identity.FindByEmailAsync(email, Ct)))!.Id);
        Assert.Equal(created.Id, (await WithIdentityAsync(identity => identity.FindByExternalLoginAsync("Google", login.ProviderKey, Ct)))!.Id);
        Assert.Equal(created.Id, (await WithIdentityAsync(identity => identity.FindByIdAsync(created.Id, Ct)))!.Id);
    }

    [Fact]
    public async Task Tenth_failed_attempt_locks_the_account()
    {
        var user = await WithIdentityAsync(identity => identity.CreateAsync(UniqueEmail("lock"), null, "es", Ct));

        var lockedAfterNine = await WithIdentityAsync(async identity =>
        {
            for (var i = 0; i < 9; i++)
            {
                await identity.RegisterFailedAttemptAsync(user.Id, Ct);
            }

            return await identity.IsLockedOutAsync(user.Id, Ct);
        });

        var lockedAfterTen = await WithIdentityAsync(async identity =>
        {
            await identity.RegisterFailedAttemptAsync(user.Id, Ct);
            return await identity.IsLockedOutAsync(user.Id, Ct);
        });

        Assert.False(lockedAfterNine);
        Assert.True(lockedAfterTen);
    }

    [Fact]
    public async Task Search_treats_like_wildcards_as_literals()
    {
        var prefix = "srch" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
        var withUnderscore = Email.Create(prefix + "-a_b@example.com").Value;
        var withoutUnderscore = Email.Create(prefix + "-axb@example.com").Value;
        await WithIdentityAsync(async identity =>
        {
            await identity.CreateAsync(withUnderscore, null, "es", Ct);
            await identity.CreateAsync(withoutUnderscore, null, "es", Ct);
            return true;
        });

        var page = await WithIdentityAsync(identity => identity.ListUsersAsync(new GetUsersQuery { Search = prefix + "-a_b" }, Ct));

        Assert.Equal([withUnderscore.Value], page.Items.Select(item => item.Email));
    }

    [Fact]
    public async Task Users_are_sorted_by_the_requested_field()
    {
        var prefix = "sort" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
        await WithIdentityAsync(async identity =>
        {
            foreach (var name in new[] { "b", "c", "a" })
            {
                await identity.CreateAsync(Email.Create($"{prefix}-{name}@example.com").Value, null, "es", Ct);
            }

            return true;
        });

        var page = await WithIdentityAsync(identity =>
            identity.ListUsersAsync(new GetUsersQuery { Search = prefix, Sort = "-email", PageSize = 2 }, Ct));

        Assert.Equal([$"{prefix}-c@example.com", $"{prefix}-b@example.com"], page.Items.Select(item => item.Email));
        Assert.Equal(3, page.TotalCount);
    }

    private static Email UniqueEmail(string prefix) =>
        Email.Create(prefix + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com").Value;

    private Task<T> WithIdentityAsync<T>(Func<IIdentityService, Task<T>> action) =>
        factory.ExecuteScopeAsync(services => action(services.GetRequiredService<IIdentityService>()));
}
```

`tests/ArquitecturaBase.Api.IntegrationTests/Identity/PermissionServiceTests.cs`:

```csharp
using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Identity;

[Collection(ApiTestGroup.Name)]
public sealed class PermissionServiceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Permissions_are_the_union_of_the_user_roles()
    {
        var userId = await CreateUserAsync(SystemRoles.Admin, SystemRoles.User);

        var permissions = await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct));

        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), permissions);
    }

    [Fact]
    public async Task User_role_has_no_permissions_yet()
    {
        var userId = await CreateUserAsync(SystemRoles.User);

        Assert.Empty(await WithPermissionsAsync(service => service.GetPermissionsAsync(userId, Ct)));
        Assert.False(await WithPermissionsAsync(service => service.HasPermissionAsync(userId, Permissions.Users.Read, Ct)));
    }

    [Fact]
    public async Task Role_permissions_are_cached_until_the_role_is_invalidated()
    {
        var roleName = "cache-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var roleId = await factory.ExecuteScopeAsync(async services =>
        {
            var roles = services.GetRequiredService<RoleManager<ApplicationRole>>();
            var role = new ApplicationRole(roleName);
            await roles.CreateAsync(role);
            await roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, Permissions.Users.Read));
            return role.Id;
        });
        var userId = await CreateUserAsync(roleName);
        Assert.True(await HasUsersReadAsync(userId));

        await factory.ExecuteScopeAsync(async services =>
        {
            var roles = services.GetRequiredService<RoleManager<ApplicationRole>>();
            var role = (await roles.FindByIdAsync(roleId.ToString("D", CultureInfo.InvariantCulture)))!;
            return await roles.RemoveClaimAsync(role, new Claim(Permissions.ClaimType, Permissions.Users.Read));
        });

        Assert.True(await HasUsersReadAsync(userId));

        await WithPermissionsAsync(async service =>
        {
            await service.InvalidateRoleAsync(roleId, Ct);
            return true;
        });

        Assert.False(await HasUsersReadAsync(userId));
    }

    private Task<bool> HasUsersReadAsync(Guid userId) =>
        WithPermissionsAsync(service => service.HasPermissionAsync(userId, Permissions.Users.Read, Ct));

    private Task<Guid> CreateUserAsync(params string[] roles) =>
        factory.ExecuteScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var email = "perm-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com";
            var user = new ApplicationUser { UserName = email, Email = email };
            await users.CreateAsync(user);
            await users.AddToRolesAsync(user, roles);
            return user.Id;
        });

    private Task<T> WithPermissionsAsync<T>(Func<IPermissionService, Task<T>> action) =>
        factory.ExecuteScopeAsync(services => action(services.GetRequiredService<IPermissionService>()));
}
```

`tests/ArquitecturaBase.Api.IntegrationTests/Identity/SeedTests.cs`:

```csharp
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Identity;

[Collection(ApiTestGroup.Name)]
public sealed class SeedTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seeding_again_leaves_the_same_roles_and_permissions()
    {
        await factory.Services.SeedDatabaseAsync(Ct);

        var (roles, adminPermissions) = await factory.ExecuteDbContextAsync(async db =>
        {
            var roleNames = await db.Roles
                .Where(role => role.Name == SystemRoles.Admin || role.Name == SystemRoles.User)
                .Select(role => role.Name!)
                .ToListAsync(Ct);
            var permissions = await db.RoleClaims
                .Where(claim => claim.ClaimType == Permissions.ClaimType && db.Roles.Any(role => role.Id == claim.RoleId && role.Name == SystemRoles.Admin))
                .Select(claim => claim.ClaimValue!)
                .ToListAsync(Ct);
            return (roleNames, permissions);
        });

        Assert.Equal([SystemRoles.Admin, SystemRoles.User], roles.Order(StringComparer.Ordinal));
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), adminPermissions.Order(StringComparer.Ordinal));
    }
}
```

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`
Esperado: FALLA la compilación (`'SeedExtensions' no existe`).

- [ ] **Paso 4: implementación de Identity**

`Identity/ExternalClaimTypes.cs`:

```csharp
namespace ArquitecturaBase.Infrastructure.Identity;

internal static class ExternalClaimTypes
{
    /// <summary>Google lo manda como booleano; al mapearlo queda "True"/"False".</summary>
    public const string EmailVerified = "email_verified";
}
```

`Identity/IdentityResultExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Identity;

namespace ArquitecturaBase.Infrastructure.Identity;

internal static class IdentityResultExtensions
{
    /// <summary>
    /// Los casos de uso validan antes de llamar a Identity: si igual rechaza, es un error de programación. El mensaje
    /// lleva solo los códigos de error de Identity, nunca datos del usuario.
    /// </summary>
    public static void EnsureSucceeded(this IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not {action}: {string.Join(", ", result.Errors.Select(error => error.Code))}.");
        }
    }
}
```

`Identity/SeedOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Infrastructure.Identity;

internal sealed class SeedOptions : IValidatableObject
{
    public const string SectionName = "Seed";

    /// <summary>Recibe el rol Admin al crearse la cuenta, o al correr el seed si ya existía. Vacío: nadie.</summary>
    public string? AdminEmail { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(AdminEmail) && Email.Create(AdminEmail).IsFailure)
        {
            yield return new ValidationResult("Seed:AdminEmail is not a valid email address.", [nameof(AdminEmail)]);
        }
    }
}
```

`Identity/IdentityService.cs`:

```csharp
using System.Linq.Expressions;
using System.Security.Claims;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Identity;

internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IOptions<SeedOptions> seedOptions)
    : IIdentityService
{
    private const string LikeEscapeCharacter = "\\";

    // Lista blanca: los mismos nombres que GetUsersQuery.SortableFields.
    private static readonly Dictionary<string, Expression<Func<ApplicationUser, object?>>> SortMap = new()
    {
        ["email"] = user => user.Email,
        ["displayName"] = user => user.DisplayName,
        ["createdAtUtc"] = user => user.CreatedAtUtc,
    };

    private static readonly SortDescriptor DefaultSort = new("createdAtUtc", Descending: true);

    public async Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        ToAccountOrNull(await FindUserAsync(userId, cancellationToken));

    public async Task<UserAccount?> FindByEmailAsync(Email email, CancellationToken cancellationToken) =>
        ToAccountOrNull(await userManager.FindByEmailAsync(email.Value));

    public async Task<UserAccount?> FindByExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken) =>
        ToAccountOrNull(await userManager.FindByLoginAsync(provider, providerKey));

    public async Task<UserAccount> CreateAsync(Email email, string? displayName, string culture, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            UserName = email.Value,
            Email = email.Value,
            EmailConfirmed = true,
            DisplayName = displayName is { Length: > ApplicationUser.DisplayNameMaxLength }
                ? displayName[..ApplicationUser.DisplayNameMaxLength]
                : displayName,
            Culture = culture,
        };

        (await userManager.CreateAsync(user)).EnsureSucceeded("create the user");
        (await userManager.AddToRoleAsync(user, IsAdminEmail(email) ? SystemRoles.Admin : SystemRoles.User))
            .EnsureSucceeded("assign the initial role");

        return ToAccount(user);
    }

    public async Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);

        (await userManager.AddLoginAsync(user, new UserLoginInfo(login.Provider, login.ProviderKey, login.Provider)))
            .EnsureSucceeded("link the external login");
    }

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(await RequireUserAsync(userId, cancellationToken));

        return [.. roles.Order(StringComparer.Ordinal)];
    }

    public async Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken) =>
        await userManager.IsLockedOutAsync(await RequireUserAsync(userId, cancellationToken));

    public async Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken) =>
        (await userManager.AccessFailedAsync(await RequireUserAsync(userId, cancellationToken)))
            .EnsureSucceeded("register the failed attempt");

    public async Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await userManager.ResetAccessFailedCountAsync(await RequireUserAsync(userId, cancellationToken)))
            .EnsureSucceeded("reset the failed attempts");

    public async Task SignInAsync(Guid userId, CancellationToken cancellationToken) =>
        await signInManager.SignInAsync(await RequireUserAsync(userId, cancellationToken), isPersistent: true);

    public async Task<ExternalLogin?> GetExternalLoginAsync(CancellationToken cancellationToken)
    {
        var info = await signInManager.GetExternalLoginInfoAsync();

        if (info is null)
        {
            return null;
        }

        return new ExternalLogin(
            info.LoginProvider,
            info.ProviderKey,
            info.Principal.FindFirstValue(ClaimTypes.Email),
            string.Equals(info.Principal.FindFirstValue(ExternalClaimTypes.EmailVerified), "true", StringComparison.OrdinalIgnoreCase),
            info.Principal.FindFirstValue(ClaimTypes.Name));
    }

    public Task SignOutExternalAsync(CancellationToken cancellationToken) =>
        signInManager.Context.SignOutAsync(IdentityConstants.ExternalScheme);

    public Task<PagedResult<UserListItem>> ListUsersAsync(PagedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var users = userManager.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // "%" y "_" del texto buscado son literales, no comodines.
            var pattern = "%" + EscapeLike(request.Search.Trim()) + "%";
            users = users.Where(user =>
                EF.Functions.ILike(user.Email!, pattern, LikeEscapeCharacter)
                || (user.DisplayName != null && EF.Functions.ILike(user.DisplayName, pattern, LikeEscapeCharacter)));
        }

        return users
            .ApplySort(SortDescriptor.Parse(request.Sort), SortMap, DefaultSort, user => user.Id)
            .Select(user => new UserListItem(user.Id, user.Email!, user.DisplayName, user.IsActive, user.CreatedAtUtc))
            .ToPagedResultAsync(request, cancellationToken);
    }

    private bool IsAdminEmail(Email email)
    {
        var adminEmail = Email.Create(seedOptions.Value.AdminEmail);

        return adminEmail.IsSuccess && adminEmail.Value.Equals(email);
    }

    private Task<ApplicationUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        userManager.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    private async Task<ApplicationUser> RequireUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await FindUserAsync(userId, cancellationToken) ?? throw new InvalidOperationException("The user does not exist.");

    private static string EscapeLike(string value) =>
        value
            .Replace(LikeEscapeCharacter, LikeEscapeCharacter + LikeEscapeCharacter, StringComparison.Ordinal)
            .Replace("%", LikeEscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscapeCharacter + "_", StringComparison.Ordinal);

    private static UserAccount? ToAccountOrNull(ApplicationUser? user) => user is null ? null : ToAccount(user);

    private static UserAccount ToAccount(ApplicationUser user) =>
        new(user.Id, user.Email!, user.DisplayName, user.Culture, user.TimeZoneId, user.IsActive);
}
```

`Identity/PermissionService.cs`:

```csharp
using System.Globalization;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Permisos efectivos: la suma de los permisos de los roles del usuario (sección 5.6). Los roles del usuario se leen
/// siempre de la base; los permisos de cada rol se cachean y se descartan con <see cref="InvalidateRoleAsync"/>.
/// </summary>
internal sealed class PermissionService(ApplicationDbContext dbContext, HybridCache cache) : IPermissionService
{
    private static readonly HybridCacheEntryOptions CacheEntryOptions = new()
    {
        Expiration = TimeSpan.FromHours(1),
        LocalCacheExpiration = TimeSpan.FromHours(1),
    };

    public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roleIds = await dbContext.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.RoleId)
            .ToListAsync(cancellationToken);

        var permissions = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var roleId in roleIds)
        {
            permissions.UnionWith(await GetRolePermissionsAsync(roleId, cancellationToken));
        }

        return permissions;
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
        (await GetPermissionsAsync(userId, cancellationToken)).Contains(permission);

    public async Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        await cache.RemoveAsync(CacheKey(roleId), cancellationToken);

    private async Task<string[]> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            CacheKey(roleId),
            (dbContext, roleId),
            static async (state, token) => await state.dbContext.RoleClaims
                .Where(claim => claim.RoleId == state.roleId && claim.ClaimType == Permissions.ClaimType)
                .Select(claim => claim.ClaimValue!)
                .ToArrayAsync(token),
            CacheEntryOptions,
            cancellationToken: cancellationToken);

    private static string CacheKey(Guid roleId) =>
        string.Create(CultureInfo.InvariantCulture, $"permissions:role:{roleId:N}");
}
```

`Identity/IdentityRegistration.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Identity;

internal static class IdentityRegistration
{
    public static IServiceCollection AddIdentityServices(this IServiceCollection services)
    {
        // AddIdentityCore y no AddIdentity: AddIdentity fija la cookie como esquema por defecto para autenticar y
        // desafiar, y /api tiene que usar la validación de OpenIddict (Tarea 17).
        services.AddAuthentication().AddIdentityCookies();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;

            // Sin redirecciones a una página de login: 401/403 y UseStatusCodePages arma el ProblemDetails.
            // Se asignan de a uno: reemplazar options.Events borraría la validación del security stamp.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // El UserName es el email, ya validado por el value object Email.
                options.User.AllowedUserNameCharacters = string.Empty;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 10;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager();

        // Claves en Postgres: la cookie y los tokens siguen valiendo con varias instancias o tras reiniciar.
        services.AddDataProtection()
            .SetApplicationName("ArquitecturaBase")
            .PersistKeysToDbContext<ApplicationDbContext>();

        services.AddHybridCache();

        services.AddOptions<SeedOptions>()
            .BindConfiguration(SeedOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<RoleSeeder>();

        return services;
    }
}
```

- [ ] **Paso 5: seed**

`Persistence/Seed/RoleSeeder.cs`:

```csharp
using System.Security.Claims;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

/// <summary>Roles del sistema con sus permisos (Admin: todos; User: ninguno por ahora) y el rol del administrador.</summary>
internal sealed class RoleSeeder(
    RoleManager<ApplicationRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IOptions<SeedOptions> seedOptions)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureRoleAsync(SystemRoles.Admin, Permissions.All);
        await EnsureRoleAsync(SystemRoles.User, []);
        await EnsureAdminRoleAsync();
    }

    private async Task EnsureRoleAsync(string name, IReadOnlyCollection<string> permissions)
    {
        var role = await roleManager.FindByNameAsync(name);

        if (role is null)
        {
            role = new ApplicationRole(name);
            (await roleManager.CreateAsync(role)).EnsureSucceeded("create the role");
        }

        var current = (await roleManager.GetClaimsAsync(role))
            .Where(claim => claim.Type == Permissions.ClaimType)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var permission in permissions.Where(permission => !current.Contains(permission)))
        {
            (await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission))).EnsureSucceeded("add a permission");
        }
    }

    // Si la cuenta ya existía cuando se configuró Seed:AdminEmail, recibe el rol acá.
    private async Task EnsureAdminRoleAsync()
    {
        var adminEmail = Email.Create(seedOptions.Value.AdminEmail);

        if (adminEmail.IsFailure)
        {
            return;
        }

        var admin = await userManager.FindByEmailAsync(adminEmail.Value.Value);

        if (admin is not null && !await userManager.IsInRoleAsync(admin, SystemRoles.Admin))
        {
            (await userManager.AddToRoleAsync(admin, SystemRoles.Admin)).EnsureSucceeded("assign the Admin role");
        }
    }
}
```

`Persistence/Seed/SeedExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

public static class SeedExtensions
{
    /// <summary>
    /// Crea o actualiza los datos base. Se puede correr las veces que haga falta. La Api lo llama al arrancar en
    /// desarrollo, después de las migraciones; los tests, al crear la base.
    /// </summary>
    public static async Task SeedDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<RoleSeeder>().SeedAsync(cancellationToken);
    }
}
```

- [ ] **Paso 6: registro, Program y configuración**

En `DependencyInjection.AddInfrastructure`, antes de `services.AddEmails();` (con `using ArquitecturaBase.Infrastructure.Identity;`):

```csharp
        services.AddIdentityServices();
```

`src/ArquitecturaBase.Api/Services/RequestInfo.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;

namespace ArquitecturaBase.Api.Services;

/// <summary>IP y user agent de la petición, para la auditoría de ingresos. LoginAudit recorta el user agent.</summary>
internal sealed class RequestInfo(IHttpContextAccessor httpContextAccessor) : IRequestInfo
{
    public string? IpAddress => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var userAgent = httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();

            return string.IsNullOrEmpty(userAgent) ? null : userAgent;
        }
    }
}
```

Tiene que existir desde esta tarea. En Development, `ValidateOnBuild` verifica que todos los servicios registrados se puedan construir, y los handlers de las Tareas 9 y 10 dependen de `IRequestInfo`. Sin él fallarían el AppHost y el `dotnet ef` de la Tarea 18. Su comportamiento lo cubre el test de auditoría de la Tarea 23.

En `src/ArquitecturaBase.Api/DependencyInjection.cs`:
- Junto a `ICurrentUser`:

```csharp
        services.AddScoped<IRequestInfo, RequestInfo>();
```

- Reemplazar el bloque de autenticación por:

```csharp
        // Los esquemas (cookie de Identity y, desde la Tarea 17, OpenIddict) los registra Infrastructure.
        services.AddAuthorization();
```

En `Program.cs`, dentro del `if (app.Environment.IsDevelopment())` (con `using ArquitecturaBase.Infrastructure.Persistence.Seed;`):

```csharp
    await app.Services.ApplyMigrationsAsync();
    await app.Services.SeedDatabaseAsync();
    app.MapOpenApiDocumentation();
```

`appsettings.json`: agregar al final, al mismo nivel que `Email`:

```json
  "Seed": {
    "AdminEmail": ""
  }
```

`appsettings.Development.json`: agregar:

```json
  "Seed": {
    "AdminEmail": "ezequielellena0003@gmail.com"
  }
```

- [ ] **Paso 7: correr y ver que pasa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

Esperado:
- 0 advertencias.
- Todo en verde: 6 tests de `IdentityServiceTests`, 3 de `PermissionServiceTests` y 1 de `SeedTests`, además de los anteriores.
- `FrameworkErrorsTests` sigue en verde, porque el esquema de prueba sigue siendo el por defecto.

- [ ] **Paso 8: commit**

```bash
git add Directory.Packages.props src tests
git commit -m "feat: registrar Identity con roles, permisos cacheados y seed de roles" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 17: OpenIddict: servidor, validación, credenciales y seed del cliente

**Archivos:**
- Crear en `src/ArquitecturaBase.Infrastructure/`:
  - `Identity/OpenIddict/AuthServerDefaults.cs`, `WebClientOptions.cs`, `OpenIddictRegistration.cs`
  - `Persistence/Seed/OpenIddictSeeder.cs`
- Modificar:
  - `src/ArquitecturaBase.Infrastructure/DependencyInjection.cs`, `Identity/IdentityRegistration.cs`, `Persistence/Seed/SeedExtensions.cs`, el csproj y `Directory.Packages.props`
  - `src/ArquitecturaBase.Api/Program.cs`, `appsettings.Development.json`
  - `tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs`, `TestAuthHandler.cs`, `Identity/SeedTests.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/OpenIddictServerTests.cs`

- [ ] **Paso 1: paquetes**

`Directory.Packages.props`, grupo `Infrastructure`:

```xml
    <PackageVersion Include="OpenIddict.EntityFrameworkCore" Version="7.7.1" />
    <PackageVersion Include="OpenIddict.Server.AspNetCore" Version="7.7.1" />
    <PackageVersion Include="OpenIddict.Validation.AspNetCore" Version="7.7.1" />
    <PackageVersion Include="OpenIddict.Validation.ServerIntegration" Version="7.7.1" />
```

En el csproj de Infrastructure, los cuatro `PackageReference`.

- [ ] **Paso 2: arnés**

`TestAuthHandler`: agregar la constante del esquema combinado:

```csharp
    /// <summary>Esquema por defecto de los tests: el de prueba si viene X-Test-UserId; si no, los tokens reales.</summary>
    public const string PolicySchemeName = "TestOrBearer";
```

`ApiFactory`:
- Agregar constantes y un constructor:

```csharp
    public const string WebRedirectUri = "https://localhost/auth/callback";
    public const string PostLogoutRedirectUri = "https://localhost/login";

    public ApiFactory()
    {
        // OpenIddict exige HTTPS y la cookie de Identity es Secure. Las redirecciones se leen, no se siguen.
        ClientOptions.BaseAddress = new Uri("https://localhost");
        ClientOptions.AllowAutoRedirect = false;
    }

    public string ConnectionString => _postgres.GetConnectionString();
```

- En `ConfigureWebHost`, junto a los otros `UseSetting`:

```csharp
        builder.UseSetting("Authentication:Clients:Web:RedirectUris:0", WebRedirectUri);
        builder.UseSetting("Authentication:Clients:Web:PostLogoutRedirectUris:0", PostLogoutRedirectUri);
```

- Reemplazar el registro del esquema de prueba (con `using OpenIddict.Validation.AspNetCore;`):

```csharp
            // Con el header X-Test-UserId, el usuario de prueba; sin él, la validación real de OpenIddict.
            services.AddAuthentication(TestAuthHandler.PolicySchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { })
                .AddPolicyScheme(TestAuthHandler.PolicySchemeName, TestAuthHandler.PolicySchemeName, options =>
                    options.ForwardDefaultSelector = context =>
                        context.Request.Headers.ContainsKey(TestAuthHandler.UserIdHeader)
                            ? TestAuthHandler.SchemeName
                            : OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
```

- [ ] **Paso 3: tests que fallan**

`tests/ArquitecturaBase.Api.IntegrationTests/Auth/OpenIddictServerTests.cs`:

```csharp
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

[Collection(ApiTestGroup.Name)]
public sealed class OpenIddictServerTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Discovery_document_publishes_the_endpoints_flows_and_scopes()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/.well-known/openid-configuration");
        var document = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://localhost/connect/authorize", document.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("https://localhost/connect/token", document.GetProperty("token_endpoint").GetString());
        Assert.Equal("https://localhost/connect/logout", document.GetProperty("end_session_endpoint").GetString());
        Assert.Equal("https://localhost/connect/userinfo", document.GetProperty("userinfo_endpoint").GetString());
        Assert.Equal("https://localhost/connect/revoke", document.GetProperty("revocation_endpoint").GetString());
        Assert.Equal("https://localhost/connect/introspect", document.GetProperty("introspection_endpoint").GetString());

        var grantTypes = Strings(document, "grant_types_supported");
        Assert.Contains(GrantTypes.AuthorizationCode, grantTypes);
        Assert.Contains(GrantTypes.RefreshToken, grantTypes);
        Assert.DoesNotContain(GrantTypes.Password, grantTypes);
        Assert.DoesNotContain(GrantTypes.ClientCredentials, grantTypes);
        Assert.Contains(CodeChallengeMethods.Sha256, Strings(document, "code_challenge_methods_supported"));
        Assert.Contains("api", Strings(document, "scopes_supported"));
    }

    [Fact]
    public async Task Web_client_is_public_requires_pkce_and_uses_the_configured_uris()
    {
        var (clientType, requirements, redirectUris, postLogoutRedirectUris) = await factory.ExecuteScopeAsync(async services =>
        {
            var manager = services.GetRequiredService<IOpenIddictApplicationManager>();
            var client = await manager.FindByClientIdAsync("web", Ct) ?? throw new InvalidOperationException("Missing client.");

            return (
                await manager.GetClientTypeAsync(client, Ct),
                await manager.GetRequirementsAsync(client, Ct),
                await manager.GetRedirectUrisAsync(client, Ct),
                await manager.GetPostLogoutRedirectUrisAsync(client, Ct));
        });

        Assert.Equal(ClientTypes.Public, clientType);
        Assert.Contains(Requirements.Features.ProofKeyForCodeExchange, requirements);
        Assert.Equal(ApiFactory.WebRedirectUri, Assert.Single(redirectUris));
        Assert.Equal(ApiFactory.PostLogoutRedirectUri, Assert.Single(postLogoutRedirectUris));
    }

    [Fact]
    public async Task Anonymous_api_request_is_challenged_by_the_bearer_scheme()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/test/protected", language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Equal("Http.Unauthorized", problem.GetProperty("code").GetString());
    }

    private static string[] Strings(System.Text.Json.JsonElement document, string property) =>
        document.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();
}
```

Agregar a `SeedTests`:

```csharp
    [Fact]
    public async Task Seeding_again_keeps_a_single_web_client()
    {
        await factory.Services.SeedDatabaseAsync(Ct);

        var clients = await factory.ExecuteDbContextAsync(db =>
            db.Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreApplication<Guid>>()
                .CountAsync(application => application.ClientId == "web", Ct));

        Assert.Equal(1, clients);
    }
```

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.OpenIddictServerTests"`
Esperado: FALLA la compilación (`'OpenIddict' no existe`).

- [ ] **Paso 4: implementación**

`Identity/OpenIddict/AuthServerDefaults.cs`:

```csharp
namespace ArquitecturaBase.Infrastructure.Identity.OpenIddict;

/// <summary>Cliente, scope y rutas del servidor de autorización (sección 5.5).</summary>
internal static class AuthServerDefaults
{
    public const string WebClientId = "web";
    public const string ApiScope = "api";
    public const string ApiResource = "arquitecturabase-api";

    public const string AuthorizationEndpoint = "connect/authorize";
    public const string TokenEndpoint = "connect/token";
    public const string EndSessionEndpoint = "connect/logout";
    public const string UserInfoEndpoint = "connect/userinfo";
    public const string RevocationEndpoint = "connect/revoke";
    public const string IntrospectionEndpoint = "connect/introspect";
}
```

`Identity/OpenIddict/WebClientOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Infrastructure.Identity.OpenIddict;

/// <summary>URIs del cliente público "web" (el SPA), en Authentication:Clients:Web.</summary>
internal sealed class WebClientOptions
{
    public const string SectionName = "Authentication:Clients:Web";

    [MinLength(1)]
    public IList<Uri> RedirectUris { get; init; } = [];

    public IList<Uri> PostLogoutRedirectUris { get; init; } = [];
}
```

`Identity/OpenIddict/OpenIddictRegistration.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Infrastructure.Identity.OpenIddict;

internal static class OpenIddictRegistration
{
    public const string TestingEnvironment = "Testing";
    public const string CertificatesSection = "Authentication:Certificates";

    public static IServiceCollection AddOpenIddictServer(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<WebClientOptions>()
            .BindConfiguration(WebClientOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOpenIddict()
            .AddCore(options => options
                .UseEntityFrameworkCore()
                .UseDbContext<ApplicationDbContext>()
                .ReplaceDefaultEntities<Guid>())
            .AddServer(options =>
            {
                options
                    .SetAuthorizationEndpointUris(AuthServerDefaults.AuthorizationEndpoint)
                    .SetTokenEndpointUris(AuthServerDefaults.TokenEndpoint)
                    .SetEndSessionEndpointUris(AuthServerDefaults.EndSessionEndpoint)
                    .SetUserInfoEndpointUris(AuthServerDefaults.UserInfoEndpoint)
                    .SetRevocationEndpointUris(AuthServerDefaults.RevocationEndpoint)
                    .SetIntrospectionEndpointUris(AuthServerDefaults.IntrospectionEndpoint);

                // Authorization code con PKCE obligatorio y refresh token. Client credentials queda para más adelante.
                options
                    .AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange()
                    .AllowRefreshTokenFlow();

                options.RegisterScopes(
                    Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Roles, Scopes.OfflineAccess, AuthServerDefaults.ApiScope);

                // Los defaults de OpenIddict contradicen el spec: access token de 1 h, refresh token de 14 días y 30 s
                // en los que un refresh token ya usado se acepta de nuevo. Sin ese margen, reusarlo revoca toda la cadena.
                options
                    .SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(5))
                    .SetAccessTokenLifetime(TimeSpan.FromMinutes(15))
                    .SetRefreshTokenLifetime(TimeSpan.FromDays(30))
                    .SetRefreshTokenReuseLeeway(null);

                AddCredentials(options, configuration, environment);

                // Passthrough: los endpoints de /connect de la Api deciden quién es el usuario y qué claims lleva.
                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();

                // Va después de UseLocalServer: así un token revocado (logout o reuso de un refresh token) deja de
                // valer en el momento, sin esperar a que venza.
                options.EnableTokenEntryValidation();
                options.UseAspNetCore();
            });

        services.AddScoped<OpenIddictSeeder>();

        return services;
    }

    private static void AddCredentials(OpenIddictServerBuilder options, IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            options.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
        }
        else if (environment.IsEnvironment(TestingEnvironment))
        {
            options.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
        }
        else
        {
            options
                .AddEncryptionCertificate(LoadCertificate(configuration, "Encryption"))
                .AddSigningCertificate(LoadCertificate(configuration, "Signing"));
        }
    }

    // Producción: un PFX por uso, con ruta y contraseña en Authentication:Certificates:{Encryption|Signing}.
    // Los constructores de X509Certificate2 están obsoletos (SYSLIB0057).
    private static X509Certificate2 LoadCertificate(IConfiguration configuration, string purpose)
    {
        var section = configuration.GetSection(CertificatesSection).GetSection(purpose);
        var path = section["Path"];

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                $"Missing '{CertificatesSection}:{purpose}:Path': OpenIddict needs PFX certificates outside Development and Testing.");
        }

        return X509CertificateLoader.LoadPkcs12FromFile(path, section["Password"]);
    }
}
```

`Persistence/Seed/OpenIddictSeeder.cs`:

```csharp
using ArquitecturaBase.Infrastructure.Identity.OpenIddict;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

/// <summary>El scope "api" y el cliente público "web" con PKCE. Si ya existen, los actualiza con la configuración actual.</summary>
internal sealed class OpenIddictSeeder(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictScopeManager scopeManager,
    IOptions<WebClientOptions> webClientOptions)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await SeedApiScopeAsync(cancellationToken);
        await SeedWebClientAsync(cancellationToken);
    }

    private async Task SeedApiScopeAsync(CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = AuthServerDefaults.ApiScope,
            DisplayName = "ArquitecturaBase API",
        };
        descriptor.Resources.Add(AuthServerDefaults.ApiResource);

        var scope = await scopeManager.FindByNameAsync(AuthServerDefaults.ApiScope, cancellationToken);

        if (scope is null)
        {
            await scopeManager.CreateAsync(descriptor, cancellationToken);
        }
        else
        {
            await scopeManager.UpdateAsync(scope, descriptor, cancellationToken);
        }
    }

    private async Task SeedWebClientAsync(CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = AuthServerDefaults.WebClientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "ArquitecturaBase Web",
        };

        descriptor.Permissions.UnionWith(
        [
            Permissions.Endpoints.Authorization,
            Permissions.Endpoints.Token,
            Permissions.Endpoints.EndSession,
            Permissions.Endpoints.Revocation,
            Permissions.GrantTypes.AuthorizationCode,
            Permissions.GrantTypes.RefreshToken,
            Permissions.ResponseTypes.Code,
            Permissions.Scopes.Email,
            Permissions.Scopes.Profile,
            Permissions.Scopes.Roles,
            Permissions.Prefixes.Scope + AuthServerDefaults.ApiScope,
        ]);
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        descriptor.RedirectUris.UnionWith(webClientOptions.Value.RedirectUris);
        descriptor.PostLogoutRedirectUris.UnionWith(webClientOptions.Value.PostLogoutRedirectUris);

        var client = await applicationManager.FindByClientIdAsync(AuthServerDefaults.WebClientId, cancellationToken);

        if (client is null)
        {
            await applicationManager.CreateAsync(descriptor, cancellationToken);
        }
        else
        {
            await applicationManager.UpdateAsync(client, descriptor, cancellationToken);
        }
    }
}
```

`SeedExtensions.SeedDatabaseAsync`: después de `RoleSeeder`, agregar:

```csharp
        await scope.ServiceProvider.GetRequiredService<OpenIddictSeeder>().SeedAsync(cancellationToken);
```

`IdentityRegistration`: el esquema por defecto pasa a ser la validación de OpenIddict (con `using OpenIddict.Validation.AspNetCore;`). Hay que reemplazar la línea y su comentario:

```csharp
        // AddIdentityCore y no AddIdentity: AddIdentity fija la cookie como esquema por defecto para autenticar y
        // desafiar. /api usa la validación de OpenIddict (bearer); la cookie solo la usan /account y /connect.
        services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme).AddIdentityCookies();
```

`DependencyInjection.AddInfrastructure`:
- La firma pasa a ser `AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)`, con `ArgumentNullException.ThrowIfNull(environment);`.
- El DbContext suma las entidades de OpenIddict:

```csharp
        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) => options
            .UseNpgsql(GetConnectionString(configuration))
            .UseOpenIddict<Guid>()
            .AddInterceptors(serviceProvider.GetServices<ISaveChangesInterceptor>()));
```

- Después de `services.AddIdentityServices();` (con los usings `ArquitecturaBase.Infrastructure.Identity.OpenIddict` y `Microsoft.Extensions.Hosting`):

```csharp
        services.AddOpenIddictServer(configuration, environment);
```

`Program.cs`:

```csharp
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration, builder.Environment)
    .AddPresentation();
```

`appsettings.Development.json`, dentro de `Authentication`:

```json
    "Clients": {
      "Web": {
        "RedirectUris": [ "https://localhost:5173/auth/callback", "https://oauth.pstmn.io/v1/callback" ],
        "PostLogoutRedirectUris": [ "https://localhost:5173/login" ]
      }
    }
```

La segunda redirect URI es la de Postman (Tarea 24).

- [ ] **Paso 5: correr y ver que pasa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

Esperado:
- 0 advertencias.
- Todo en verde: 3 tests nuevos en `OpenIddictServerTests` y 1 en `SeedTests`.
- `FrameworkErrorsTests.Anonymous_request_to_a_protected_endpoint_returns_a_401_problem` pasa con el esquema real (pendiente 8 de la Fase 1). El desafío de OpenIddict responde 401 sin cuerpo y `UseStatusCodePages` arma el ProblemDetails.

- [ ] **Paso 6: commit**

```bash
git add Directory.Packages.props src tests
git commit -m "feat: configurar OpenIddict con code + PKCE, refresh rotativo y el cliente web" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 18: Primera migración

**Archivos:**
- Crear: `src/ArquitecturaBase.Infrastructure/Persistence/Migrations/*` (generado)
- Modificar: `.editorconfig`, el csproj de la Api, `Directory.Packages.props`, `CLAUDE.md`, `tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs`, `tests/ArquitecturaBase.Api.IntegrationTests/OpenApiTests.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/MigrationsTests.cs`

- [ ] **Paso 1: test que falla**

En `ApiFactory`, debajo de `ConnectionString` (con los usings `System.Globalization` y `Npgsql`):

```csharp
    /// <summary>Cadena de conexión a una base nueva y vacía en el mismo contenedor. EF la crea al migrar.</summary>
    public string NewDatabaseConnectionString(string prefix) =>
        new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = prefix + "_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8],
        }.ConnectionString;
```

`tests/ArquitecturaBase.Api.IntegrationTests/Persistence/MigrationsTests.cs`:

```csharp
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// Usan el ApplicationDbContext de producción armado con las opciones registradas. El TestDbContext del arnés suma
/// la tabla de Widgets, que no es parte de las migraciones.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class MigrationsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Model_has_no_pending_changes()
    {
        var pending = await factory.ExecuteScopeAsync(services =>
        {
            using var dbContext = new ApplicationDbContext(services.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
            return Task.FromResult(dbContext.Database.HasPendingModelChanges());
        });

        Assert.False(pending);
    }

    [Fact]
    public async Task Migrations_create_the_schema_on_an_empty_database()
    {
        await using var emptyDatabase = factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("migrations")));
        await using var scope = emptyDatabase.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());

        try
        {
            await dbContext.Database.MigrateAsync(Ct);

            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync(Ct));
            Assert.False(await dbContext.LoginCodes.AnyAsync(Ct));
            Assert.False(await dbContext.Users.AnyAsync(Ct));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }
}
```

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.MigrationsTests"`
Esperado: FALLAN los dos. `Model_has_no_pending_changes` falla porque todavía no hay snapshot. `MigrateAsync` lanza `PendingModelChangesWarning`.

- [ ] **Paso 2: herramientas y estilo**

`Directory.Packages.props`, grupo `Api`: `<PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12" />`.

Csproj de la Api:

```xml
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
```

`.editorconfig`, al final:

```ini
[**/Migrations/*.cs]
# Código que genera dotnet ef: no se le exige el estilo del repo (IDE0005, IDE0161 y los analizadores).
generated_code = true
```

- [ ] **Paso 3: generar la migración**

Docker encendido. El comando no necesita que la base exista: solo arma el modelo.

```bash
dotnet ef migrations add InitialIdentity --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api --output-dir Persistence/Migrations -- --environment Development --ConnectionStrings:appdb "Host=localhost;Port=5433;Database=appdb;Username=postgres;Password=postgres"
```

Esperado:
- `Done.` y tres archivos en `src/ArquitecturaBase.Infrastructure/Persistence/Migrations/`.
- El aviso de que `dotnet-ef` 10.0.9 es más viejo que el runtime es normal.
- En la migración aparecen estas tablas:
  - las de Identity (`AspNetUsers`, `AspNetRoles`...);
  - las de OpenIddict (`OpenIddictApplications`, `OpenIddictAuthorizations`, `OpenIddictScopes`, `OpenIddictTokens`);
  - `LoginCodes`, `LoginAudits` y `DataProtectionKeys`.
- `EmailIndex` es único.

- [ ] **Paso 4: la Api en Development dentro de los tests**

En Development, la Api aplica las migraciones y el seed al arrancar. `OpenApiTests.Swagger_ui_and_openapi_document_are_served_in_development` levanta la Api en Development contra la base compartida de los tests, y esa base tiene dos problemas para migrar:
- se creó con `EnsureCreated`, así que la migración choca con tablas que ya existen;
- usa `TestDbContext` (con Widgets), cuyo modelo no es el de las migraciones, y EF 10 lanza `PendingModelChangesWarning`.

Por eso ese test pasa a usar una base vacía propia y el `ApplicationDbContext` de producción. De paso, prueba el arranque real de desarrollo: migraciones más seed.

En `OpenApiTests.cs`:
- Reemplazar el test así:

```csharp
    [Fact]
    public async Task Swagger_ui_and_openapi_document_are_served_in_development()
    {
        // En Development la Api aplica las migraciones y el seed al arrancar: se le da una base vacía propia y el
        // ApplicationDbContext de producción (el TestDbContext del arnés suma Widgets, que no están en las migraciones).
        await using var development = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .UseSetting($"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("development"))
            .ConfigureTestServices(services => services.Replace(ServiceDescriptor.Scoped<ApplicationDbContext>(serviceProvider =>
                new ApplicationDbContext(serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>())))));
        using var client = development.CreateClient();

        using var swagger = await client.SendAsync(HttpMethod.Get, "/swagger/index.html");
        using var document = await client.SendAsync(HttpMethod.Get, "/openapi/v1.json");
        var html = await swagger.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        Assert.Contains("swagger-ui", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
    }
```

- Agregar los usings `ArquitecturaBase.Infrastructure.Persistence`, `Microsoft.AspNetCore.TestHost`, `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.DependencyInjection` y `Microsoft.Extensions.DependencyInjection.Extensions`, y el alias `using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;`.

- [ ] **Paso 5: correr y ver que pasa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

Esperado: 0 advertencias. Todo en verde, incluidos los 2 tests de migraciones y `OpenApiTests`.

- [ ] **Paso 6: CLAUDE.md**

En la sección "Persistencia", reemplazar el comando de migraciones por:

```
dotnet ef migrations add <Nombre> --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api --output-dir Persistence/Migrations -- --environment Development --ConnectionStrings:appdb "Host=localhost;Port=5433;Database=appdb;Username=postgres;Password=postgres"
```

Agregar debajo del bloque:
- `MigrationsTests` falla si el modelo cambia y falta la migración.
- Las migraciones son código generado: `.editorconfig` las excluye del estilo.

- [ ] **Paso 7: commit**

```bash
git add .editorconfig Directory.Packages.props CLAUDE.md src tests
git commit -m "feat: agregar la primera migración con Identity, OpenIddict y los códigos de ingreso" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 19: Endpoints `/account/login-code` y rate limiting

Esta tarea expone pedir y verificar el código, con estas reglas:
- el límite por IP es un `RateLimiter` que responde 429 con ProblemDetails y `retryAfter`;
- los endpoints de `/account` solo aceptan JSON (sección 5.7);
- en el arnés, `IEmailSender` pasa a guardar los emails en memoria, para leer el código.

**Archivos:**
- Crear:
  - `src/ArquitecturaBase.Api/RateLimiting/RateLimitingOptions.cs`, `RateLimitingExtensions.cs`
  - `src/ArquitecturaBase.Api/Endpoints/Account/LoginCodeEndpoints.cs`
  - `tests/ArquitecturaBase.Api.IntegrationTests/Support/CapturingEmailSender.cs`, `TestEmails.cs`, `AuthFlow.cs`
- Modificar:
  - `src/ArquitecturaBase.Api/DependencyInjection.cs`, `Program.cs`, `appsettings.json`
  - `tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs`, `HttpExtensions.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginCodeEndpointsTests.cs`

- [ ] **Paso 1: arnés**

`Support/CapturingEmailSender.cs`:

```csharp
using System.Collections.Concurrent;
using ArquitecturaBase.Application.Abstractions.Emails;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>IEmailSender de los tests: guarda los emails en memoria para leer el código enviado (sección 9).</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    /// <summary>El código es la primera palabra del asunto: "123456 es tu código de acceso a ...".</summary>
    public static string CodeOf(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.Subject.Split(' ')[0];
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _messages.Enqueue(message);

        return Task.CompletedTask;
    }

    public int CountFor(string to) => _messages.Count(message => message.To == to);

    /// <summary>Espera el email número <paramref name="number"/> (desde 1) enviado a <paramref name="to"/>.</summary>
    public async Task<EmailMessage> WaitForAsync(string to, int number = 1)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (true)
        {
            var message = _messages.Where(candidate => candidate.To == to).Skip(number - 1).FirstOrDefault();

            if (message is not null)
            {
                return message;
            }

            await Task.Delay(20, timeout.Token);
        }
    }
}
```

`Support/TestEmails.cs`:

```csharp
using System.Globalization;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

internal static class TestEmails
{
    /// <summary>Un email distinto por test, ya normalizado: todos los tests comparten la base.</summary>
    public static string Unique(string prefix) =>
        prefix + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com";
}
```

En `HttpExtensions`, agregar (con `using System.Net.Http.Json;`):

```csharp
    public static Task<HttpResponseMessage> PostJsonAsync(
        this HttpClient client,
        string url,
        object body,
        string? language = null) =>
        client.SendAsync(HttpMethod.Post, url, JsonContent.Create(body), language);
```

`Support/AuthFlow.cs`:

```csharp
using System.Net;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>Los pasos del ingreso, contra la Api real, para reutilizar en los tests.</summary>
internal static class AuthFlow
{
    public const string AuthorizeReturnUrl = "/connect/authorize";

    /// <summary>Pide un código para <paramref name="email"/> y devuelve el que llegó por email.</summary>
    public static async Task<string> RequestCodeAsync(this HttpClient client, ApiFactory factory, string email)
    {
        var previous = factory.EmailSender.CountFor(email);

        using var response = await client.PostJsonAsync("/account/login-code", new { email });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return CapturingEmailSender.CodeOf(await factory.EmailSender.WaitForAsync(email, previous + 1));
    }

    /// <summary>Pide y verifica un código: el cliente queda con la cookie de sesión del servidor.</summary>
    public static async Task SignInWithCodeAsync(this HttpClient client, ApiFactory factory, string email)
    {
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = AuthorizeReturnUrl });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

`ApiFactory` queda así (reúne lo que sumaron las Tareas 13 a 17 y agrega el sender en memoria y los límites de los tests):

```csharp
using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Application;
using ArquitecturaBase.Application.Abstractions.Emails;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using OpenIddict.Validation.AspNetCore;
using Testcontainers.PostgreSql;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// La Api real contra un Postgres en contenedor, con un reloj controlable, los emails en memoria y las features de
/// prueba (entidad Widget y endpoints /test) que existen solo en este proyecto.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Recibe el rol Admin al crearse (Seed:AdminEmail).</summary>
    public const string AdminEmail = "admin@arquitecturabase.test";

    public const string WebRedirectUri = "https://localhost/auth/callback";
    public const string PostLogoutRedirectUri = "https://localhost/login";

    /// <summary>Clave HMAC de los tests: los bytes 0 a 31 en base64. Nunca se usa fuera de los tests.</summary>
    public const string TestHashKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    // La misma imagen que usa Aspire 13.5.4.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    public ApiFactory()
    {
        // OpenIddict exige HTTPS y la cookie de Identity es Secure. Las redirecciones se leen, no se siguen.
        ClientOptions.BaseAddress = new Uri("https://localhost");
        ClientOptions.AllowAutoRedirect = false;
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));

    public CapturingEmailSender EmailSender { get; } = new();

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        // Sin migraciones en los tests: el esquema sale del modelo de TestDbContext.
        await ExecuteDbContextAsync(dbContext => dbContext.Database.EnsureCreatedAsync());

        // Los mismos datos base que en desarrollo: roles, permisos y el cliente "web".
        await Services.SeedDatabaseAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public async Task<T> ExecuteDbContextAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = Services.CreateAsyncScope();

        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public async Task<T> ExecuteScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = Services.CreateAsyncScope();

        return await action(scope.ServiceProvider);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing": no aplica migraciones ni mapea OpenAPI, que son solo de Development.
        builder.UseEnvironment("Testing");

        // La registración del DbContext de producción lee la cadena de conexión de acá.
        builder.UseSetting($"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", _postgres.GetConnectionString());

        builder.UseSetting("Authentication:LoginCode:HashKey", TestHashKey);

        // Sin espera entre pedidos ni límite por email: muchos tests piden códigos seguidos para el mismo email.
        // LoginCodeEndpointsTests prueba esos límites con una Api aparte (WithWebHostBuilder).
        builder.UseSetting("Authentication:LoginCode:ResendCooldownSeconds", "0");
        builder.UseSetting("Authentication:LoginCode:MaxRequestsPerWindow", "100");

        builder.UseSetting("Authentication:Clients:Web:RedirectUris:0", WebRedirectUri);
        builder.UseSetting("Authentication:Clients:Web:PostLogoutRedirectUris:0", PostLogoutRedirectUri);

        // Bajo TestServer no hay IP remota: todos los tests caen en la misma partición del rate limiter.
        builder.UseSetting("RateLimiting:LoginCodePermitLimit", "100000");
        builder.UseSetting("RateLimiting:LoginVerifyPermitLimit", "100000");

        // Sin validación de SMTP: los emails quedan en memoria (EmailSender).
        builder.UseSetting("Email:Delivery", "PickupDirectory");

        builder.UseSetting("Seed:AdminEmail", AdminEmail);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);

            // Claves en memoria: las de Postgres se leen al arrancar el host, antes de que exista el esquema.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            // Mismas opciones que producción (Npgsql, interceptores, OpenIddict); solo cambia el tipo de contexto,
            // que suma la tabla de Widgets.
            services.Replace(ServiceDescriptor.Scoped<ApplicationDbContext>(serviceProvider =>
                new TestDbContext(serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>())));

            services.AddFeaturesFromAssembly(typeof(ApiFactory).Assembly);
            services.AddEndpoints(typeof(ApiFactory).Assembly);

            // Con el header X-Test-UserId, el usuario de prueba; sin él, la validación real de OpenIddict.
            services.AddAuthentication(TestAuthHandler.PolicySchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { })
                .AddPolicyScheme(TestAuthHandler.PolicySchemeName, TestAuthHandler.PolicySchemeName, options =>
                    options.ForwardDefaultSelector = context =>
                        context.Request.Headers.ContainsKey(TestAuthHandler.UserIdHeader)
                            ? TestAuthHandler.SchemeName
                            : OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        });
    }
}
```

- [ ] **Paso 2: tests que fallan**

`tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginCodeEndpointsTests.cs`:

```csharp
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

[Collection(ApiTestGroup.Name)]
public sealed class LoginCodeEndpointsTests(ApiFactory factory)
{
    private const string ReturnUrl = "/connect/authorize?client_id=web";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Requesting_a_code_returns_202_and_emails_the_code()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("request");

        using var response = await client.PostJsonAsync("/account/login-code", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var code = CapturingEmailSender.CodeOf(await factory.EmailSender.WaitForAsync(email));
        Assert.Matches("^[0-9]{6}$", code);

        var stored = await factory.ExecuteDbContextAsync(db => db.LoginCodes.SingleAsync(loginCode => loginCode.Email == email, Ct));
        Assert.DoesNotContain(code, stored.CodeHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Right_code_starts_a_persistent_secure_session()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("verify");
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = ReturnUrl });
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ReturnUrl, body.GetProperty("returnUrl").GetString());

        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wrong_code_returns_the_attempts_left_in_the_requested_language()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("wrong");
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync(
            "/account/login-code/verify",
            new { email, code = code == "000000" ? "111111" : "000000", returnUrl = ReturnUrl },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Auth.LoginCode.Invalid", problem.GetProperty("code").GetString());
        Assert.Equal("El código no es válido.", problem.GetProperty("detail").GetString());
        Assert.Equal(4, problem.GetProperty("attemptsLeft").GetInt32());
    }

    [Fact]
    public async Task Return_url_outside_the_authorize_endpoint_is_rejected()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("returnurl");
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync(
            "/account/login-code/verify",
            new { email, code, returnUrl = "https://evil.example/connect/authorize" },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("La dirección de retorno no es válida.", problem.GetProperty("errors").GetProperty("returnUrl")[0].GetString());
    }

    [Fact]
    public async Task Account_endpoints_only_accept_json()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Post,
            "/account/login-code",
            new FormUrlEncodedContent([new("email", TestEmails.Unique("form"))]));
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Asking_again_before_the_cooldown_returns_429_with_the_seconds_left()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Authentication:LoginCode:ResendCooldownSeconds", "60")
            .UseSetting("Authentication:LoginCode:MaxRequestsPerWindow", "5"));
        using var client = api.CreateClient();
        var email = TestEmails.Unique("cooldown");

        using var first = await client.PostJsonAsync("/account/login-code", new { email });
        factory.Clock.Advance(TimeSpan.FromSeconds(15));
        using var second = await client.PostJsonAsync("/account/login-code", new { email }, language: "es");
        var problem = await second.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(60, (await first.ReadJsonAsync()).GetProperty("resendAfterSeconds").GetInt32());
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("Auth.LoginCode.ResendTooSoon", problem.GetProperty("code").GetString());
        Assert.Equal(45, problem.GetProperty("retryAfter").GetInt32());
        Assert.Equal("Esperá un momento antes de pedir otro código.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Rate_limiter_rejects_with_a_problem_and_retry_after()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:LoginCodePermitLimit", "1"));
        using var client = api.CreateClient();

        using var first = await client.PostJsonAsync("/account/login-code", new { email = TestEmails.Unique("limit") });
        using var second = await client.PostJsonAsync("/account/login-code", new { email = TestEmails.Unique("limit") }, language: "es");
        var problem = await second.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("Http.TooManyRequests", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("retryAfter").GetInt32() > 0);
        Assert.True(second.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
```

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.LoginCodeEndpointsTests"`
Esperado: FALLAN con 404 (los endpoints no existen).

- [ ] **Paso 3: rate limiting**

`RateLimiting/RateLimitingOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Api.RateLimiting;

/// <summary>Límites por IP de /account (sección 5.3), en la sección RateLimiting.</summary>
internal sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Pedidos de código: 20 cada 15 minutos.</summary>
    [Range(1, 100_000)]
    public int LoginCodePermitLimit { get; init; } = 20;

    [Range(1, 1440)]
    public int LoginCodeWindowMinutes { get; init; } = 15;

    /// <summary>Verificaciones: el spec no fija el número; 30 cada 15 minutos alcanza para varios intentos por código.</summary>
    [Range(1, 100_000)]
    public int LoginVerifyPermitLimit { get; init; } = 30;

    [Range(1, 1440)]
    public int LoginVerifyWindowMinutes { get; init; } = 15;
}
```

`RateLimiting/RateLimitingExtensions.cs`:

```csharp
using System.Globalization;
using System.Threading.RateLimiting;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.RateLimiting;

internal static class RateLimitingExtensions
{
    public const string LoginCodePolicy = "login-code";
    public const string LoginVerifyPolicy = "login-verify";

    public static IServiceCollection AddRateLimitingPolicies(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            // El default es 503; el spec pide 429 con retryAfter.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;

            options.AddPolicy(LoginCodePolicy, context =>
            {
                var settings = SettingsOf(context);
                return FixedWindowByIp(context, settings.LoginCodePermitLimit, settings.LoginCodeWindowMinutes);
            });

            options.AddPolicy(LoginVerifyPolicy, context =>
            {
                var settings = SettingsOf(context);
                return FixedWindowByIp(context, settings.LoginVerifyPermitLimit, settings.LoginVerifyWindowMinutes);
            });
        });

        return services;
    }

    private static RateLimitingOptions SettingsOf(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    private static RateLimitPartition<string> FixedWindowByIp(HttpContext context, int permitLimit, int windowMinutes) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(windowMinutes),
                QueueLimit = 0,
            });

    // Mismo formato que los demás errores, con retryAfter en segundos (encabezado y cuerpo).
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        var problem = ProblemDetailsMapper.Create(
            ErrorType.TooManyRequests, ApiErrorCodes.TooManyRequests, ErrorMessages.Get(ApiErrorCodes.TooManyRequests));

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            problem.Extensions[LoginCodeErrors.RetryAfterKey] = seconds;
        }

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });
    }
}
```

- [ ] **Paso 4: endpoints**

`Endpoints/Account/LoginCodeEndpoints.cs`:

```csharp
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Auth.RequestLoginCode;
using ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;

namespace ArquitecturaBase.Api.Endpoints.Account;

/// <summary>
/// Ingreso con código (sección 5.2). Solo aceptan JSON y no hay CORS: un formulario de otro sitio no puede iniciar
/// una sesión (CSRF de login).
/// </summary>
internal sealed class LoginCodeEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account/login-code").AllowAnonymous().WithTags("Account");

        // 202 exista o no la cuenta: la respuesta no revela nada.
        group.MapPost("", async (
                RequestLoginCodeCommand command,
                ICommandHandler<RequestLoginCodeCommand, RequestLoginCodeResponse> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(command, cancellationToken);

                return result.IsSuccess ? TypedResults.Accepted((string?)null, result.Value) : result.Error.ToProblem();
            })
            .RequireRateLimiting(RateLimitingExtensions.LoginCodePolicy);

        group.MapPost("/verify", async (
                VerifyLoginCodeCommand command,
                ICommandHandler<VerifyLoginCodeCommand, VerifyLoginCodeResponse> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult())
            .RequireRateLimiting(RateLimitingExtensions.LoginVerifyPolicy);
    }
}
```

En `AddPresentation`, después de `AddRequestLocalizationDefaults` (con `using ArquitecturaBase.Api.RateLimiting;`):

```csharp
        services.AddRateLimitingPolicies();
```

En `Program.cs`, reemplazar el bloque que va de `UseStatusCodePages` a `UseAuthorization` por:

```csharp
// Las respuestas de error sin cuerpo (ruta inexistente, 405, 401/403 de la autorización) salen como ProblemDetails.
app.UseStatusCodePages();

// Como todo middleware que pueda cortar con un error, va después de UseStatusCodePages. El rechazo arma su propio
// ProblemDetails con retryAfter.
app.UseRateLimiter();

// Explícitos, y no los que WebApplication agrega solo al principio del pipeline: así quedan dentro de la
// localización y de UseStatusCodePages, y el 401/403 también sale como ProblemDetails traducido.
app.UseAuthentication();
app.UseAuthorization();
```

`appsettings.json`: agregar al mismo nivel que `Email`:

```json
  "RateLimiting": {
    "LoginCodePermitLimit": 20,
    "LoginCodeWindowMinutes": 15,
    "LoginVerifyPermitLimit": 30,
    "LoginVerifyWindowMinutes": 15
  }
```

- [ ] **Paso 5: correr y ver que pasa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

Esperado:
- 0 advertencias.
- Todo en verde: 7 tests nuevos. `Rate_limiter_rejects_with_a_problem_and_retry_after` prueba un rechazo real del limitador (pendiente 8 de la Fase 1).

- [ ] **Paso 6: commit**

```bash
git add src/ArquitecturaBase.Api tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "feat: exponer el pedido y la verificación del código con rate limiting por IP" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 20: Endpoints `/connect/*` y flujo completo con PKCE

Los endpoints que usa OpenIddict en modo passthrough:
- **authorize:** emite el code si hay cookie; si no, redirige a `/login` o responde `login_required` con `prompt=none`;
- **token:** vuelve a mirar al usuario antes de emitir tokens;
- **logout:** revoca los tokens de la autorización y cierra la cookie;
- **userinfo.**

`OpenIdPrincipalFactory` arma los claims (ver "Desvíos").

**Archivos:**
- Crear en `src/ArquitecturaBase.Api/Endpoints/Connect/`:
  - `OpenIdPrincipalFactory.cs`, `OpenIddictResults.cs`
  - `AuthorizeEndpoint.cs`, `TokenEndpoint.cs`, `LogoutEndpoint.cs`, `UserInfoEndpoint.cs`
- Modificar:
  - `src/ArquitecturaBase.Api/DependencyInjection.cs`, el csproj de la Api
  - `tests/ArquitecturaBase.Api.IntegrationTests/Support/AuthFlow.cs`
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Support/Pkce.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/ConnectFlowTests.cs`

- [ ] **Paso 1: arnés**

`Support/Pkce.cs`:

```csharp
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>PKCE con S256, como lo hace oidc-client-ts.</summary>
internal static class Pkce
{
    public static string CreateVerifier() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string ChallengeOf(string verifier) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}
```

`Support/AuthFlow.cs` queda así:

```csharp
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.WebUtilities;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

internal sealed record TokenResponse(string AccessToken, string RefreshToken, string? IdToken)
{
    public static async Task<TokenResponse> ReadAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.ReadJsonAsync();

        return new TokenResponse(
            json.GetProperty("access_token").GetString()!,
            json.GetProperty("refresh_token").GetString()!,
            json.TryGetProperty("id_token", out var idToken) ? idToken.GetString() : null);
    }
}

/// <summary>Los pasos del ingreso, contra la Api real, en el mismo orden que el SPA (sección 5.2).</summary>
internal static class AuthFlow
{
    public const string AuthorizeReturnUrl = "/connect/authorize";
    public const string Scopes = "openid profile email roles offline_access api";

    /// <summary>Pide un código para <paramref name="email"/> y devuelve el que llegó por email.</summary>
    public static async Task<string> RequestCodeAsync(this HttpClient client, ApiFactory factory, string email)
    {
        var previous = factory.EmailSender.CountFor(email);

        using var response = await client.PostJsonAsync("/account/login-code", new { email });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return CapturingEmailSender.CodeOf(await factory.EmailSender.WaitForAsync(email, previous + 1));
    }

    /// <summary>Pide y verifica un código: el cliente queda con la cookie de sesión del servidor.</summary>
    public static async Task SignInWithCodeAsync(this HttpClient client, ApiFactory factory, string email)
    {
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = AuthorizeReturnUrl });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public static Task<HttpResponseMessage> AuthorizeAsync(this HttpClient client, string codeChallenge, string? prompt = null) =>
        client.SendAsync(HttpMethod.Get, QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = "web",
            ["redirect_uri"] = ApiFactory.WebRedirectUri,
            ["scope"] = Scopes,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = "test-state",
            ["prompt"] = prompt,
        }));

    /// <summary>El authorization code de la redirección a la redirect URI del cliente.</summary>
    public static string CodeFromRedirect(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location!;
        Assert.Equal(ApiFactory.WebRedirectUri, location.GetLeftPart(UriPartial.Path));

        return QueryHelpers.ParseQuery(location.Query)["code"].ToString();
    }

    public static async Task<TokenResponse> ExchangeCodeAsync(this HttpClient client, string code, string verifier)
    {
        using var response = await client.SendAsync(HttpMethod.Post, "/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "web",
            ["code"] = code,
            ["redirect_uri"] = ApiFactory.WebRedirectUri,
            ["code_verifier"] = verifier,
        }));

        return await TokenResponse.ReadAsync(response);
    }

    public static Task<HttpResponseMessage> RefreshAsync(this HttpClient client, string refreshToken) =>
        client.SendAsync(HttpMethod.Post, "/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = "web",
            ["refresh_token"] = refreshToken,
        }));

    /// <summary>El flujo completo: código por email, authorize con PKCE y canje en /connect/token.</summary>
    public static async Task<TokenResponse> LoginAsync(this HttpClient client, ApiFactory factory, string email)
    {
        await client.SignInWithCodeAsync(factory, email);

        var verifier = Pkce.CreateVerifier();
        using var authorize = await client.AuthorizeAsync(Pkce.ChallengeOf(verifier));

        return await client.ExchangeCodeAsync(CodeFromRedirect(authorize), verifier);
    }

    public static async Task<HttpResponseMessage> GetWithTokenAsync(
        this HttpClient client,
        string url,
        string accessToken,
        string? language = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (language is not null)
        {
            request.Headers.AcceptLanguage.ParseAdd(language);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
```

- [ ] **Paso 2: tests que fallan**

`tests/ArquitecturaBase.Api.IntegrationTests/Auth/ConnectFlowTests.cs`:

```csharp
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Features.Auth;
using Microsoft.AspNetCore.WebUtilities;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

[Collection(ApiTestGroup.Name)]
public sealed class ConnectFlowTests(ApiFactory factory)
{
    [Fact]
    public async Task Code_flow_issues_tokens_that_the_api_accepts()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("flow");

        var tokens = await client.LoginAsync(factory, email);

        using var protectedResponse = await client.GetWithTokenAsync("/test/protected", tokens.AccessToken);
        using var userInfo = await client.GetWithTokenAsync("/connect/userinfo", tokens.AccessToken);
        var claims = await userInfo.ReadJsonAsync();

        Assert.NotNull(tokens.IdToken);
        Assert.Equal(HttpStatusCode.NoContent, protectedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, userInfo.StatusCode);
        Assert.Equal(email, claims.GetProperty("email").GetString());
        Assert.Equal("es", claims.GetProperty("locale").GetString());
        Assert.Equal(["User"], claims.GetProperty("role").EnumerateArray().Select(role => role.GetString()));
    }

    [Fact]
    public async Task Authorize_without_a_session_sends_the_user_to_login_with_the_original_request()
    {
        using var client = factory.CreateClient();

        using var response = await client.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/login?returnUrl=", location, StringComparison.Ordinal);

        var returnUrl = QueryHelpers.ParseQuery(location[location.IndexOf('?', StringComparison.Ordinal)..])["returnUrl"].ToString();
        Assert.True(ReturnUrls.IsAuthorizeRequest(returnUrl));
        Assert.Contains("code_challenge=", returnUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Silent_renewal_without_a_session_returns_login_required()
    {
        using var client = factory.CreateClient();

        using var response = await client.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()), prompt: "none");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ApiFactory.WebRedirectUri, response.Headers.Location!.GetLeftPart(UriPartial.Path));
        Assert.Equal("login_required", QueryHelpers.ParseQuery(response.Headers.Location.Query)["error"].ToString());
    }

    [Fact]
    public async Task Refresh_token_rotates_and_reusing_an_old_one_revokes_the_whole_chain()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("rotate"));

        using var firstRefresh = await client.RefreshAsync(tokens.RefreshToken);
        var rotated = await TokenResponse.ReadAsync(firstRefresh);
        using var reuse = await client.RefreshAsync(tokens.RefreshToken);
        using var afterReuse = await client.RefreshAsync(rotated.RefreshToken);
        using var api = await client.GetWithTokenAsync("/test/protected", rotated.AccessToken);

        Assert.NotEqual(tokens.RefreshToken, rotated.RefreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Equal("invalid_grant", (await reuse.ReadJsonAsync()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, afterReuse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
    }

    [Fact]
    public async Task Access_token_expires_after_15_minutes()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("expiry"));

        factory.Clock.Advance(TimeSpan.FromMinutes(16));
        using var response = await client.GetWithTokenAsync("/test/protected", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Revoked_refresh_token_can_no_longer_be_used()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("revoke"));

        using var revoke = await client.SendAsync(HttpMethod.Post, "/connect/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = tokens.RefreshToken,
            ["token_type_hint"] = "refresh_token",
            ["client_id"] = "web",
        }));
        using var refresh = await client.RefreshAsync(tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_tokens_and_closes_the_session()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("logout"));

        using var logout = await client.SendAsync(HttpMethod.Get, QueryHelpers.AddQueryString("/connect/logout", new Dictionary<string, string?>
        {
            ["id_token_hint"] = tokens.IdToken,
            ["post_logout_redirect_uri"] = ApiFactory.PostLogoutRedirectUri,
        }));
        using var api = await client.GetWithTokenAsync("/test/protected", tokens.AccessToken);
        using var refresh = await client.RefreshAsync(tokens.RefreshToken);
        using var authorize = await client.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()));

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(ApiFactory.PostLogoutRedirectUri, logout.Headers.Location!.GetLeftPart(UriPartial.Path));
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
        Assert.StartsWith("/login?", authorize.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }
}
```

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.ConnectFlowTests"`
Esperado: FALLAN. Con passthrough y sin endpoints, `/connect/authorize` y `/connect/token` terminan en 404.

- [ ] **Paso 3: implementación**

Csproj de la Api: la Api usa directamente los tipos del servidor de OpenIddict.

```xml
    <PackageReference Include="OpenIddict.Server.AspNetCore" />
    <PackageReference Include="OpenIddict.Validation.AspNetCore" />
```

`Endpoints/Connect/OpenIddictResults.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Server.AspNetCore;

namespace ArquitecturaBase.Api.Endpoints.Connect;

/// <summary>
/// Respuestas que arma OpenIddict: redirección con el code o los tokens, o un error OAuth. Son Results y no
/// TypedResults: con TypedResults, .NET 10 convierte las redirecciones de autenticación en 401.
/// </summary>
internal static class OpenIddictResults
{
    public static IResult SignIn(ClaimsPrincipal principal) =>
        Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    public static IResult Forbid(string error, string description) =>
        Results.Forbid(ErrorProperties(error, description), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    public static IResult Challenge(string error, string description) =>
        Results.Challenge(ErrorProperties(error, description), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    private static AuthenticationProperties ErrorProperties(string error, string description) =>
        new(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        });
}
```

`Endpoints/Connect/OpenIdPrincipalFactory.cs`:

```csharp
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Application.Abstractions.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Api.Endpoints.Connect;

/// <summary>
/// Arma la identidad que OpenIddict convierte en tokens: sub, email, name y role (sección 5.6). Los permisos no van
/// en el token. Vive en la Api, y no en Infrastructure como dice el spec, porque la Api no puede usar tipos de
/// Infrastructure fuera de Program.cs.
/// </summary>
internal sealed class OpenIdPrincipalFactory(IIdentityService identityService, IOpenIddictScopeManager scopeManager)
{
    /// <summary>Para /connect/authorize. Null si la cuenta ya no puede ingresar.</summary>
    public async Task<ClaimsPrincipal?> CreateAsync(Guid userId, ImmutableArray<string> scopes, CancellationToken cancellationToken)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);

        if (!await SetUserClaimsAsync(identity, userId, cancellationToken))
        {
            return null;
        }

        var resources = new List<string>();

        await foreach (var resource in scopeManager.ListResourcesAsync(scopes, cancellationToken))
        {
            resources.Add(resource);
        }

        identity.SetScopes(scopes);
        identity.SetResources(resources);
        identity.SetDestinations(GetDestinations);

        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Para /connect/token. Parte de lo guardado en el code o el refresh token: los scopes y la autorización, que
    /// OpenIddict necesita para revocar toda la cadena. Actualiza los datos del usuario. Null si ya no puede ingresar.
    /// </summary>
    public async Task<ClaimsPrincipal?> RefreshAsync(ClaimsPrincipal stored, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(stored.GetClaim(Claims.Subject), CultureInfo.InvariantCulture, out var userId))
        {
            return null;
        }

        var identity = new ClaimsIdentity(stored.Claims, TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);

        if (!await SetUserClaimsAsync(identity, userId, cancellationToken))
        {
            return null;
        }

        identity.SetDestinations(GetDestinations);

        return new ClaimsPrincipal(identity);
    }

    private async Task<bool> SetUserClaimsAsync(ClaimsIdentity identity, Guid userId, CancellationToken cancellationToken)
    {
        var user = await identityService.FindByIdAsync(userId, cancellationToken);

        if (user is not { IsActive: true })
        {
            return false;
        }

        var roles = await identityService.GetRolesAsync(user.Id, cancellationToken);

        identity
            .SetClaim(Claims.Subject, user.Id.ToString("D", CultureInfo.InvariantCulture))
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, user.DisplayName ?? user.Email)
            .SetClaims(Claims.Role, [.. roles]);

        return true;
    }

    // El access token lleva siempre sub, email, name y role (los usa la Api); el id token, según los scopes pedidos.
    private static IEnumerable<string> GetDestinations(Claim claim) => claim.Type switch
    {
        Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Email when claim.Subject?.HasScope(Scopes.Email) == true => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Name when claim.Subject?.HasScope(Scopes.Profile) == true => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Role when claim.Subject?.HasScope(Scopes.Roles) == true => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Email or Claims.Name or Claims.Role => [Destinations.AccessToken],
        _ => [],
    };
}
```

`Endpoints/Connect/AuthorizeEndpoint.cs`:

```csharp
using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Application.Features.Auth;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Api.Endpoints.Connect;

/// <summary>
/// Emite el authorization code si hay sesión (cookie de Identity). Si no, manda al SPA a /login con el authorize
/// original como returnUrl (sección 5.2); si el SPA renueva en silencio (prompt=none), responde login_required.
/// </summary>
internal sealed class AuthorizeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapMethods(ReturnUrls.AuthorizePath, [HttpMethods.Get, HttpMethods.Post], HandleAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        OpenIdPrincipalFactory principalFactory,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var session = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var principal = session.Succeeded && TryGetUserId(session.Principal, out var userId)
            ? await principalFactory.CreateAsync(userId, request.GetScopes(), cancellationToken)
            : null;

        if (principal is not null)
        {
            return OpenIddictResults.SignIn(principal);
        }

        // Hay cookie pero la cuenta ya no puede ingresar (deshabilitada o borrada): se cierra la sesión.
        if (session.Succeeded)
        {
            await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }

        if (request.HasPromptValue(PromptValues.None))
        {
            return OpenIddictResults.Forbid(Errors.LoginRequired, "The user is not signed in.");
        }

        return Results.Redirect(ReturnUrls.LoginPath + QueryString.Create("returnUrl", await BuildReturnUrlAsync(httpContext.Request, cancellationToken)));
    }

    // El mismo pedido, siempre como GET: el SPA navega a esta URL después de verificar el código.
    private static async Task<string> BuildReturnUrlAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var parameters = request.HasFormContentType
            ? (await request.ReadFormAsync(cancellationToken)).ToList()
            : request.Query.ToList();

        return ReturnUrls.AuthorizePath + QueryString.Create(parameters);
    }

    private static bool TryGetUserId(ClaimsPrincipal? principal, out Guid userId) =>
        Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), CultureInfo.InvariantCulture, out userId);
}
```

`Endpoints/Connect/TokenEndpoint.cs`:

```csharp
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Api.Endpoints.Connect;

/// <summary>
/// Canje del code y del refresh token. OpenIddict ya validó el cliente, el PKCE y el token; acá se vuelve a mirar al
/// usuario: si lo deshabilitaron o lo borraron, no recibe tokens nuevos.
/// </summary>
internal sealed class TokenEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/connect/token", HandleAsync).AllowAnonymous().ExcludeFromDescription();

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        OpenIdPrincipalFactory principalFactory,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // OpenIddict rechaza antes cualquier otro grant: solo están habilitados estos dos.
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            throw new InvalidOperationException("The grant type is not supported.");
        }

        var stored = await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = stored.Principal is null ? null : await principalFactory.RefreshAsync(stored.Principal, cancellationToken);

        return principal is null
            ? OpenIddictResults.Forbid(Errors.InvalidGrant, "The user can no longer sign in.")
            : OpenIddictResults.SignIn(principal);
    }
}
```

`Endpoints/Connect/LogoutEndpoint.cs`:

```csharp
using ArquitecturaBase.Application.Features.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace ArquitecturaBase.Api.Endpoints.Connect;

/// <summary>Cierra la cookie, revoca los tokens de la autorización del SPA y vuelve a /login (sección 5.5).</summary>
internal sealed class LogoutEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapMethods("/connect/logout", [HttpMethods.Get, HttpMethods.Post], HandleAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IOpenIddictTokenManager tokenManager,
        CancellationToken cancellationToken)
    {
        // El id_token_hint identifica la autorización: se revocan su refresh token y sus access tokens.
        var hint = await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var authorizationId = hint.Principal?.GetAuthorizationId();

        if (!string.IsNullOrEmpty(authorizationId))
        {
            await tokenManager.RevokeByAuthorizationIdAsync(authorizationId, cancellationToken);
        }

        await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);

        // OpenIddict redirige al post_logout_redirect_uri del cliente; si no vino ninguno, a /login.
        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = ReturnUrls.LoginPath },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }
}
```

`Endpoints/Connect/UserInfoEndpoint.cs`:

```csharp
using System.Globalization;
using ArquitecturaBase.Application.Abstractions.Identity;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Api.Endpoints.Connect;

/// <summary>Claims del usuario según los scopes del access token, que OpenIddict ya validó.</summary>
internal sealed class UserInfoEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapMethods("/connect/userinfo", [HttpMethods.Get, HttpMethods.Post], HandleAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IIdentityService identityService,
        CancellationToken cancellationToken)
    {
        var principal = (await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
        var user = Guid.TryParse(principal?.GetClaim(Claims.Subject), CultureInfo.InvariantCulture, out var userId)
            ? await identityService.FindByIdAsync(userId, cancellationToken)
            : null;

        if (principal is null || user is null)
        {
            return OpenIddictResults.Challenge(Errors.InvalidToken, "The user no longer exists.");
        }

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [Claims.Subject] = user.Id.ToString("D", CultureInfo.InvariantCulture),
        };

        if (principal.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = user.Email;
            claims[Claims.EmailVerified] = true;
        }

        if (principal.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = user.DisplayName ?? user.Email;
            claims[Claims.Locale] = user.Culture;
            claims[Claims.Zoneinfo] = user.TimeZoneId;
        }

        if (principal.HasScope(Scopes.Roles))
        {
            claims[Claims.Role] = await identityService.GetRolesAsync(user.Id, cancellationToken);
        }

        return Results.Ok(claims);
    }
}
```

En `AddPresentation`, junto a `ICurrentUser` (con `using ArquitecturaBase.Api.Endpoints.Connect;`):

```csharp
        services.AddScoped<OpenIdPrincipalFactory>();
```

- [ ] **Paso 4: correr y ver que pasa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

Esperado:
- 0 advertencias.
- Todo en verde: 7 tests nuevos. El flujo completo funciona: código → authorize → token → Api.

`Access_token_expires_after_15_minutes` confirma que la validación usa el `TimeProvider` de DI (hecho 10). Si fallara, avisar antes de tocar código: sería un desvío del diseño.

- [ ] **Paso 5: commit**

```bash
git add src/ArquitecturaBase.Api tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "feat: agregar los endpoints de OpenIddict con el flujo code + PKCE, refresh rotativo y logout" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 21: Permisos, `/api/me` y `/api/users`

Los endpoints piden permisos, nunca roles (sección 5.6). `.RequirePermission(Permissions.Users.Read)` arma una política `permission:users.read`, que resuelve `PermissionPolicyProvider`. `PermissionAuthorizationHandler` la verifica con `IPermissionService`.

**Archivos:**
- Crear:
  - `src/ArquitecturaBase.Api/Authorization/PermissionRequirement.cs`, `PermissionAuthorizationHandler.cs`, `PermissionPolicyProvider.cs`, `EndpointExtensions.cs`
  - `src/ArquitecturaBase.Api/Endpoints/Users/MeEndpoint.cs`, `UsersEndpoints.cs`
- Modificar: `src/ArquitecturaBase.Api/DependencyInjection.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Users/UsersEndpointsTests.cs`

- [ ] **Paso 1: tests que fallan**

```csharp
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

[Collection(ApiTestGroup.Name)]
public sealed class UsersEndpointsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Me_returns_the_profile_roles_and_permissions_of_the_token_owner()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("me");
        var tokens = await client.LoginAsync(factory, email);

        using var response = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var me = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(email, me.GetProperty("email").GetString());
        Assert.Equal("es", me.GetProperty("culture").GetString());
        Assert.Equal("America/Argentina/Buenos_Aires", me.GetProperty("timeZoneId").GetString());
        Assert.Equal(["User"], Strings(me, "roles"));
        Assert.Empty(Strings(me, "permissions"));
    }

    [Fact]
    public async Task Admin_gets_every_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var me = await response.ReadJsonAsync();

        Assert.Contains("Admin", Strings(me, "roles"));
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), Strings(me, "permissions"));
    }

    [Fact]
    public async Task Me_without_a_token_returns_a_401_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/api/me");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Http.Unauthorized", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Users_list_requires_the_users_read_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("nolist"));

        using var response = await client.GetWithTokenAsync("/api/users", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Admin_lists_users_with_search_sort_and_paging()
    {
        var prefix = TestEmails.Unique("list").Split('@')[0];
        await factory.ExecuteScopeAsync(async services =>
        {
            var identity = services.GetRequiredService<IIdentityService>();
            await identity.CreateAsync(Email.Create(prefix + "-a@example.com").Value, "Ana", "es", Ct);
            await identity.CreateAsync(Email.Create(prefix + "-b@example.com").Value, "Beto", "es", Ct);
            return true;
        });
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync($"/api/users?search={prefix}&sort=-email&pageSize=1", tokens.AccessToken);
        var page = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(prefix + "-b@example.com", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("email").GetString());
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.True(page.GetProperty("hasNext").GetBoolean());
    }

    [Fact]
    public async Task Sorting_by_a_field_outside_the_whitelist_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/users?sort=passwordHash", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No se puede ordenar por ese campo.", problem.GetProperty("errors").GetProperty("sort")[0].GetString());
    }

    private static string[] Strings(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();
}
```

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UsersEndpointsTests"`
Esperado: FALLAN con 404.

- [ ] **Paso 2: autorización por permisos**

`Authorization/PermissionRequirement.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace ArquitecturaBase.Api.Authorization;

internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
```

`Authorization/PermissionAuthorizationHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using Microsoft.AspNetCore.Authorization;

namespace ArquitecturaBase.Api.Authorization;

/// <summary>El usuario cumple si alguno de sus roles tiene el permiso. Los permisos no viajan en el token.</summary>
internal sealed class PermissionAuthorizationHandler(ICurrentUser currentUser, IPermissionService permissionService)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var cancellationToken = context.Resource is HttpContext httpContext ? httpContext.RequestAborted : CancellationToken.None;

        if (currentUser.UserId is { } userId
            && await permissionService.HasPermissionAsync(userId, requirement.Permission, cancellationToken))
        {
            context.Succeed(requirement);
        }
    }
}
```

`Authorization/PermissionPolicyProvider.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.Authorization;

/// <summary>Arma al vuelo las políticas "permission:&lt;permiso&gt;"; el resto las resuelve el proveedor por defecto.</summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string PolicyPrefix = "permission:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[PolicyPrefix.Length..]))
            .Build();
    }
}
```

`Authorization/EndpointExtensions.cs`:

```csharp
namespace ArquitecturaBase.Api.Authorization;

internal static class EndpointExtensions
{
    /// <summary>Exige un permiso del catálogo (<c>Domain/Authorization/Permissions</c>). Los endpoints nunca piden roles.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + permission);
}
```

En `AddPresentation`, reemplazar `services.AddAuthorization();` y su comentario por (con los usings `ArquitecturaBase.Api.Authorization` y `Microsoft.AspNetCore.Authorization`):

```csharp
        // Los esquemas (cookie de Identity y validación de OpenIddict) los registra Infrastructure.
        // Las políticas "permission:*" se arman al vuelo: .RequirePermission(Permissions.Users.Read).
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
```

- [ ] **Paso 3: endpoints**

`Endpoints/Users/MeEndpoint.cs`:

```csharp
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Users.GetCurrentUser;

namespace ArquitecturaBase.Api.Endpoints.Users;

/// <summary>Perfil, roles, permisos e idioma/zona horaria del usuario del token (sección 5.6).</summary>
internal sealed class MeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/me", async (
                IQueryHandler<GetCurrentUserQuery, CurrentUserResponse> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetCurrentUserQuery(), cancellationToken)).ToHttpResult())
            .RequireAuthorization()
            .WithTags("Users");
}
```

`Endpoints/Users/UsersEndpoints.cs`:

```csharp
using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Api.Endpoints.Users;

/// <summary>
/// Listado paginado: el ejemplo del patrón de paginado (sección 6.2). Los parámetros se enlazan a mano porque
/// [AsParameters] haría obligatorios los int de PagedRequest.
/// </summary>
internal sealed class UsersEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/users", async (
                int? page,
                int? pageSize,
                string? sort,
                string? search,
                IQueryHandler<GetUsersQuery, PagedResult<UserListItem>> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(
                new GetUsersQuery
                {
                    Page = page ?? PagedRequest.DefaultPage,
                    PageSize = pageSize ?? PagedRequest.DefaultPageSize,
                    Sort = sort,
                    Search = search,
                },
                cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Read)
            .WithTags("Users");
}
```

- [ ] **Paso 4: correr y ver que pasa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

Esperado: 0 advertencias. Todo en verde (6 tests nuevos). Los tests de arquitectura siguen en verde: la Api usa Application, Domain y OpenIddict, nunca Infrastructure.

- [ ] **Paso 5: commit**

```bash
git add src/ArquitecturaBase.Api tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "feat: autorizar por permisos y exponer /api/me y el listado de usuarios" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 22: Ingreso con Google

Sigue la sección 5.4 del spec y el hecho verificado 5:
- Google se registra solo si hay `ClientId`.
- El secreto se lee al crear las opciones y se valida al arrancar.
- `email_verified` se mapea a un claim.
- Si Google falla o el usuario cancela, el navegador vuelve a `/login?error=<código>`.

El endpoint del challenge arma las mismas `AuthenticationProperties` que `SignInManager.ConfigureExternalAuthenticationProperties`. Ese método no se puede llamar desde la Api, porque es genérico en `ApplicationUser` (Infrastructure). El test lee el `state` que viaja a Google y verifica esas propiedades.

**Archivos:**
- Crear:
  - `src/ArquitecturaBase.Api/Endpoints/Account/ExternalLoginEndpoints.cs`
  - `tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/ExternalLoginTestEndpoints.cs`
- Modificar:
  - `src/ArquitecturaBase.Infrastructure/Identity/IdentityRegistration.cs`, `DependencyInjection.cs`, el csproj de Infrastructure, `Directory.Packages.props`
  - el csproj de la Api (`UserSecretsId`), `appsettings.json`
  - `tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/ExternalLoginTests.cs`

- [ ] **Paso 1: paquete, configuración y arnés**

`Directory.Packages.props`, grupo `Infrastructure`: `<PackageVersion Include="Microsoft.AspNetCore.Authentication.Google" Version="10.0.12" />`. En el csproj de Infrastructure: `<PackageReference Include="Microsoft.AspNetCore.Authentication.Google" />`.

`appsettings.json`: dentro de `Authentication`, agregar el ClientId. Es público; el secreto no va acá.

```json
    "Google": {
      "ClientId": "830839449608-nkaial9hn903rhe38fukv12gmsn4bvhs.apps.googleusercontent.com"
    }
```

En el csproj de la Api, dentro del primer `<PropertyGroup>` (o en uno nuevo si no hay):

```xml
    <UserSecretsId>4f8d2c1a-9b3e-4d7f-a6c5-2e1b8f9d0a47</UserSecretsId>
```

En `ApiFactory.ConfigureWebHost`, junto a los otros `UseSetting`:

```csharp
        // El ClientId sale de appsettings.json; el secreto real nunca llega a los tests.
        builder.UseSetting("Authentication:Google:ClientSecret", "test-google-client-secret");
```

`tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/ExternalLoginTestEndpoints.cs`:

```csharp
using System.Security.Claims;
using ArquitecturaBase.Api.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

public sealed record FakeExternalLogin(string ProviderKey, string Email, string Name, bool EmailVerified);

/// <summary>
/// Simula la vuelta de Google: deja la misma cookie externa que dejaría el middleware de Google después de
/// /signin-google, con los claims que mapea (email_verified llega como "True"/"False") y el proveedor en
/// "LoginProvider".
/// </summary>
internal sealed class ExternalLoginTestEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/test/external-login", (FakeExternalLogin login) =>
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, login.ProviderKey),
                    new Claim(ClaimTypes.Email, login.Email),
                    new Claim(ClaimTypes.Name, login.Name),
                    new Claim("email_verified", login.EmailVerified ? "True" : "False"),
                ],
                GoogleDefaults.AuthenticationScheme);

            var properties = new AuthenticationProperties();
            properties.Items["LoginProvider"] = GoogleDefaults.AuthenticationScheme;

            return Results.SignIn(new ClaimsPrincipal(identity), properties, IdentityConstants.ExternalScheme);
        });
}
```

- [ ] **Paso 2: tests que fallan**

`tests/ArquitecturaBase.Api.IntegrationTests/Auth/ExternalLoginTests.cs`:

```csharp
using System.Globalization;
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

[Collection(ApiTestGroup.Name)]
public sealed class ExternalLoginTests(ApiFactory factory)
{
    private const string ReturnUrl = "/connect/authorize?client_id=web";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Google_challenge_goes_to_google_with_the_callback_and_the_provider()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/account/external/google?returnUrl=" + Uri.EscapeDataString(ReturnUrl));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", location.GetLeftPart(UriPartial.Path));

        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal(factory.Services.GetRequiredService<IConfiguration>()["Authentication:Google:ClientId"], query["client_id"].ToString());
        Assert.Equal("https://localhost/signin-google", query["redirect_uri"].ToString());

        // Lo que Google devuelve en el state es lo que después lee GetExternalLoginInfoAsync.
        var properties = factory.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>()
            .Get(GoogleDefaults.AuthenticationScheme)
            .StateDataFormat.Unprotect(query["state"].ToString());
        Assert.Equal(GoogleDefaults.AuthenticationScheme, properties!.Items["LoginProvider"]);
        Assert.Equal("/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl), properties.RedirectUri);
    }

    [Fact]
    public async Task Google_challenge_rejects_a_return_url_outside_the_authorize_endpoint()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Get, "/account/external/google?returnUrl=" + Uri.EscapeDataString("https://evil.example/"), language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("La dirección de retorno no es válida.", problem.GetProperty("errors").GetProperty("returnUrl")[0].GetString());
    }

    [Fact]
    public async Task Verified_google_account_is_created_linked_signed_in_and_audited()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("google");
        var providerKey = "google-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        using var external = await client.PostJsonAsync("/test/external-login", new { providerKey, email, name = "Ana Pérez", emailVerified = true });

        using var callback = await client.SendAsync(HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl));
        using var authorize = await client.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()));

        Assert.True(external.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(ReturnUrl, callback.Headers.Location!.OriginalString);
        Assert.False(string.IsNullOrEmpty(AuthFlow.CodeFromRedirect(authorize)));

        var (displayName, loginProvider, audit) = await factory.ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email, Ct);
            var login = await db.UserLogins.SingleAsync(l => l.UserId == user.Id, Ct);
            var success = await db.LoginAudits.SingleAsync(a => a.Email == email && a.Succeeded, Ct);
            return (user.DisplayName, login.LoginProvider, success);
        });
        Assert.Equal("Ana Pérez", displayName);
        Assert.Equal(GoogleDefaults.AuthenticationScheme, loginProvider);
        Assert.Equal(LoginMethod.Google, audit.Method);
    }

    [Fact]
    public async Task Unverified_google_email_goes_back_to_login_with_the_error()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("unverified");
        using var external = await client.PostJsonAsync(
            "/test/external-login", new { providerKey = "google-" + email, email, name = "Ana", emailVerified = false });

        using var callback = await client.SendAsync(HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl));

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/login?error=" + ExternalLoginErrors.EmailNotVerifiedCode, callback.Headers.Location!.OriginalString);
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(u => u.Email == email, Ct)));
    }

    [Fact]
    public async Task Callback_without_a_google_sign_in_goes_back_to_login()
    {
        using var client = factory.CreateClient();

        using var callback = await client.SendAsync(HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl));

        Assert.Equal("/login?error=" + ExternalLoginErrors.FailedCode, callback.Headers.Location!.OriginalString);
    }
}
```

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.ExternalLoginTests"`
Esperado: FALLAN (404 en `/account/external/*` y `GoogleOptions` sin registrar).

- [ ] **Paso 3: registro de Google**

`IdentityRegistration`:
- La firma pasa a ser `AddIdentityServices(this IServiceCollection services, IConfiguration configuration)`.
- La primera línea de autenticación queda así:

```csharp
        // AddIdentityCore y no AddIdentity: AddIdentity fija la cookie como esquema por defecto para autenticar y
        // desafiar. /api usa la validación de OpenIddict (bearer); la cookie solo la usan /account y /connect.
        var authentication = services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        authentication.AddIdentityCookies();
        AddGoogle(services, authentication, configuration);
```

- Agregar la constante y el método. Usings: `ArquitecturaBase.Application.Features.Auth`, `ArquitecturaBase.Domain.Authentication`, `Microsoft.AspNetCore.Authentication`, `Microsoft.AspNetCore.Authentication.Google` y `Microsoft.Extensions.Configuration`.

```csharp
    public const string GoogleSection = "Authentication:Google";

    // Solo si hay ClientId: registrado con un ClientId vacío, Google rompe todas las peticiones al validar sus opciones.
    private static void AddGoogle(IServiceCollection services, AuthenticationBuilder authentication, IConfiguration configuration)
    {
        var clientId = configuration[GoogleSection + ":ClientId"];

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        authentication.AddGoogle(options =>
        {
            options.ClientId = clientId;

            // En desarrollo viene de user-secrets; en producción, de variables de entorno o un almacén de secretos.
            options.ClientSecret = configuration[GoogleSection + ":ClientSecret"] ?? string.Empty;
            options.SignInScheme = IdentityConstants.ExternalScheme;

            // Google no lo mapea por defecto; sin él no se puede vincular por email (sección 5.4).
            options.ClaimActions.MapJsonKey(ExternalClaimTypes.EmailVerified, "email_verified");

            // Si el usuario cancela en Google o algo falla, vuelve al login del SPA con el código del error.
            options.Events.OnRemoteFailure = context =>
            {
                context.Response.Redirect(
                    ReturnUrls.LoginPath + "?error=" + Uri.EscapeDataString(ExternalLoginErrors.FailedCode));
                context.HandleResponse();

                return Task.CompletedTask;
            };
        });

        services.AddOptions<GoogleOptions>(GoogleDefaults.AuthenticationScheme)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ClientSecret),
                "Missing Authentication:Google:ClientSecret. In development, load it with dotnet user-secrets (see the README).")
            .ValidateOnStart();
    }
```

En `DependencyInjection.AddInfrastructure`: `services.AddIdentityServices(configuration);`.

- [ ] **Paso 4: endpoints**

`src/ArquitecturaBase.Api/Endpoints/Account/ExternalLoginEndpoints.cs`:

```csharp
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Features.Auth.SignInWithExternalProvider;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;

namespace ArquitecturaBase.Api.Endpoints.Account;

/// <summary>Ingreso con Google (sección 5.4). Son navegaciones del navegador: los errores vuelven al login del SPA.</summary>
internal sealed class ExternalLoginEndpoints : IEndpoint
{
    public const string CallbackPath = "/account/external/callback";

    // La clave que usa SignInManager.ConfigureExternalAuthenticationProperties. Ese método no se puede llamar desde la
    // Api (es genérico en ApplicationUser, de Infrastructure); GetExternalLoginInfoAsync lee de acá el proveedor.
    public const string LoginProviderKey = "LoginProvider";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account/external").AllowAnonymous().WithTags("Account");

        group.MapGet("/google", async (string? returnUrl, IAuthenticationSchemeProvider schemes) =>
        {
            // Sin ClientId configurado, Google no se registra.
            if (await schemes.GetSchemeAsync(GoogleDefaults.AuthenticationScheme) is null)
            {
                return Results.NotFound();
            }

            if (!ReturnUrls.IsAuthorizeRequest(returnUrl))
            {
                return new ValidationError(new Dictionary<string, string[]>
                {
                    ["returnUrl"] = [ValidationMessages.ReturnUrlInvalid],
                }).ToProblem();
            }

            var properties = new AuthenticationProperties { RedirectUri = CallbackPath + QueryString.Create("returnUrl", returnUrl) };
            properties.Items[LoginProviderKey] = GoogleDefaults.AuthenticationScheme;

            return Results.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
        });

        group.MapGet("/callback", async (
            string? returnUrl,
            ICommandHandler<SignInWithExternalProviderCommand, SignInWithExternalProviderResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.Handle(new SignInWithExternalProviderCommand(returnUrl), cancellationToken);

            return result.IsSuccess
                ? Results.LocalRedirect(result.Value.ReturnUrl)
                : Results.Redirect(ReturnUrls.LoginPath + QueryString.Create("error", result.Error.Code));
        });
    }
}
```

- [ ] **Paso 5: correr y ver que pasa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

Esperado: 0 advertencias. Todo en verde (5 tests nuevos).

- [ ] **Paso 6: commit**

```bash
git add Directory.Packages.props src tests
git commit -m "feat: agregar el ingreso con Google con vinculación por email verificado" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Paso 7: secreto y URIs (los carga el usuario)**

Frenar y pedirle al usuario estos dos pasos. Desde acá, el AppHost no arranca sin el secreto (`ValidateOnStart`).
1. Cargar el secreto del cliente de Google en los user-secrets de la Api:

```bash
dotnet user-secrets set "Authentication:Google:ClientSecret" "<secreto del cliente>" --project src/ArquitecturaBase.Api
```

2. En Google Cloud Console → Credenciales → el cliente OAuth → "URIs de redireccionamiento autorizados", agregar:
   - `https://localhost:7180/signin-google` (Fase 2: la Api directo);
   - `https://localhost:5173/signin-google` (Fase 3: a través de Vite).

Se recomienda regenerar el secreto antes de cargarlo, porque se compartió por chat.

---

### Tarea 23: Auditoría, bloqueo y cuenta deshabilitada de punta a punta

Los tests de Application ya cubren estas reglas. Esta tarea las prueba con la Api real:
- que la auditoría guarde IP y user agent;
- que el bloqueo de Identity actúe tras 10 fallos;
- que una cuenta deshabilitada no reciba tokens nuevos ni reutilice su sesión.

No agrega código de producción: si algún test falla, es un bug de las tareas anteriores y se corrige ahí.

**Archivos:**
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/LoginSecurityTests.cs`

- [ ] **Paso 1: tests**

```csharp
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authentication;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

[Collection(ApiTestGroup.Name)]
public sealed class LoginSecurityTests(ApiFactory factory)
{
    private const string ReturnUrl = "/connect/authorize";
    private const string UserAgent = "ArquitecturaBase.Tests/1.0";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_attempt_is_audited_with_the_client_and_without_the_code()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        var email = TestEmails.Unique("audit");
        var code = await client.RequestCodeAsync(factory, email);

        using var wrong = await client.PostJsonAsync("/account/login-code/verify", new { email, code = WrongCode(code), returnUrl = ReturnUrl });
        using var right = await client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = ReturnUrl });

        var audits = await factory.ExecuteDbContextAsync(db => db.LoginAudits
            .Where(audit => audit.Email == email)
            .OrderBy(audit => audit.Succeeded)
            .ToListAsync(Ct));

        Assert.Collection(
            audits,
            failure =>
            {
                Assert.False(failure.Succeeded);
                Assert.Equal(LoginCodeErrors.InvalidCode, failure.FailureReason);
                Assert.Null(failure.UserId);
            },
            success =>
            {
                Assert.True(success.Succeeded);
                Assert.NotNull(success.UserId);
                Assert.Equal(LoginMethod.Code, success.Method);
            });
        Assert.All(audits, audit =>
        {
            Assert.Equal(UserAgent, audit.UserAgent);
            Assert.DoesNotContain(code, audit.FailureReason ?? string.Empty, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Ten_failed_verifications_in_a_row_lock_the_account()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("lockout");
        await client.SignInWithCodeAsync(factory, email);

        // Cada código admite 5 intentos: hacen falta dos códigos para llegar a 10 fallos seguidos.
        for (var round = 0; round < 2; round++)
        {
            var code = await client.RequestCodeAsync(factory, email);

            for (var attempt = 0; attempt < 5; attempt++)
            {
                using var failed = await client.PostJsonAsync(
                    "/account/login-code/verify", new { email, code = WrongCode(code), returnUrl = ReturnUrl });
                Assert.NotEqual(HttpStatusCode.OK, failed.StatusCode);
            }
        }

        var lastCode = await client.RequestCodeAsync(factory, email);
        using var locked = await client.PostJsonAsync("/account/login-code/verify", new { email, code = lastCode, returnUrl = ReturnUrl }, language: "es");
        var problem = await locked.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal(AccountErrors.LockedOutCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Disabled_account_is_reported_after_verifying_the_code()
    {
        var email = TestEmails.Unique("disabled");
        using (var first = factory.CreateClient())
        {
            await first.SignInWithCodeAsync(factory, email);
        }

        await DisableAsync(email);
        using var client = factory.CreateClient();
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = ReturnUrl }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(AccountErrors.DisabledCode, problem.GetProperty("code").GetString());
        Assert.Equal("Tu cuenta está deshabilitada. Contactá a un administrador.", problem.GetProperty("detail").GetString());
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Disabled_user_cannot_refresh_tokens_or_reuse_the_session()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("disabledtokens");
        var tokens = await client.LoginAsync(factory, email);

        await DisableAsync(email);
        using var refresh = await client.RefreshAsync(tokens.RefreshToken);
        using var authorize = await client.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()));

        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
        Assert.Equal("invalid_grant", (await refresh.ReadJsonAsync()).GetProperty("error").GetString());
        Assert.StartsWith("/login?", authorize.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";

    // Con el change tracker (no ExecuteUpdate): así pasa por el interceptor de auditoría, como en producción.
    private Task<int> DisableAsync(string email) =>
        factory.ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email, Ct);
            user.IsActive = false;
            return await db.SaveChangesAsync(Ct);
        });
}
```

- [ ] **Paso 2: correr**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.LoginSecurityTests"`
Esperado: los 4 en verde. Si alguno falla, corregir la tarea que corresponda (sin tocar el test) e informarlo.

- [ ] **Paso 3: commit**

```bash
git add tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "test: probar de punta a punta la auditoría, el bloqueo y las cuentas deshabilitadas" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 24: Colección de Postman, README y CLAUDE.md

**Archivos:**
- Crear: `docs/postman/ArquitecturaBase.postman_collection.json`, `docs/postman/README.md`
- Modificar: `README.md`, `CLAUDE.md`, `docs/plans/2026-09-18-fase-1-fundaciones.md` (pendientes resueltos)

- [ ] **Paso 1: colección**

`docs/postman/ArquitecturaBase.postman_collection.json`:

```json
{
  "info": {
    "name": "ArquitecturaBase - Identidad",
    "description": "Ingreso con código, OpenIddict (code + PKCE) y la Api, contra la Api local. Instrucciones en docs/postman/README.md.",
    "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
  },
  "variable": [
    { "key": "baseUrl", "value": "https://localhost:7180" },
    { "key": "email", "value": "tu-email@ejemplo.com" },
    { "key": "code", "value": "" },
    { "key": "clientId", "value": "web" },
    { "key": "redirectUri", "value": "https://oauth.pstmn.io/v1/callback" },
    { "key": "postLogoutRedirectUri", "value": "https://localhost:5173/login" },
    { "key": "scopes", "value": "openid profile email roles offline_access api" },
    { "key": "codeVerifier", "value": "" },
    { "key": "codeChallenge", "value": "" },
    { "key": "authorizationCode", "value": "" },
    { "key": "accessToken", "value": "" },
    { "key": "refreshToken", "value": "" },
    { "key": "idToken", "value": "" },
    { "key": "googleLoginUrl", "value": "" }
  ],
  "item": [
    {
      "name": "1. Account",
      "item": [
        {
          "name": "Pedir código",
          "request": {
            "method": "POST",
            "header": [
              { "key": "Content-Type", "value": "application/json" },
              { "key": "Accept-Language", "value": "es" }
            ],
            "body": { "mode": "raw", "raw": "{\n  \"email\": \"{{email}}\"\n}" },
            "url": "{{baseUrl}}/account/login-code"
          },
          "event": [
            {
              "listen": "test",
              "script": {
                "exec": [
                  "pm.test('202 Accepted', () => pm.response.to.have.status(202));",
                  "console.log('El código está en el asunto del último .eml de src/ArquitecturaBase.Api/.emails/. Copialo en la variable code.');"
                ]
              }
            }
          ]
        },
        {
          "name": "Verificar código",
          "request": {
            "method": "POST",
            "header": [
              { "key": "Content-Type", "value": "application/json" },
              { "key": "Accept-Language", "value": "es" }
            ],
            "body": { "mode": "raw", "raw": "{\n  \"email\": \"{{email}}\",\n  \"code\": \"{{code}}\",\n  \"returnUrl\": \"/connect/authorize\"\n}" },
            "url": "{{baseUrl}}/account/login-code/verify"
          },
          "event": [
            {
              "listen": "test",
              "script": {
                "exec": [
                  "pm.test('200 OK: Postman guardó la cookie de sesión', () => pm.response.to.have.status(200));"
                ]
              }
            }
          ]
        }
      ]
    },
    {
      "name": "2. Connect",
      "item": [
        {
          "name": "Authorize (code + PKCE)",
          "protocolProfileBehavior": { "followRedirects": false },
          "request": {
            "method": "GET",
            "header": [],
            "url": "{{baseUrl}}/connect/authorize?response_type=code&client_id={{clientId}}&redirect_uri={{redirectUri}}&scope={{scopes}}&code_challenge={{codeChallenge}}&code_challenge_method=S256&state=postman"
          },
          "event": [
            {
              "listen": "prerequest",
              "script": {
                "exec": [
                  "// PKCE S256, como oidc-client-ts: el verifier queda guardado para el canje.",
                  "const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~';",
                  "let verifier = '';",
                  "for (let i = 0; i < 64; i++) { verifier += alphabet.charAt(Math.floor(Math.random() * alphabet.length)); }",
                  "const challenge = CryptoJS.SHA256(verifier).toString(CryptoJS.enc.Base64).replace(/\\+/g, '-').replace(/\\//g, '_').replace(/=+$/, '');",
                  "pm.collectionVariables.set('codeVerifier', verifier);",
                  "pm.collectionVariables.set('codeChallenge', challenge);"
                ]
              }
            },
            {
              "listen": "test",
              "script": {
                "exec": [
                  "const location = pm.response.headers.get('Location') || '';",
                  "const match = /[?&]code=([^&]+)/.exec(location);",
                  "pm.test('Redirige al cliente con el authorization code (si va a /login, falta verificar el código)', () => pm.expect(match).to.not.be.null);",
                  "if (match) { pm.collectionVariables.set('authorizationCode', decodeURIComponent(match[1])); }"
                ]
              }
            }
          ]
        },
        {
          "name": "Token (canjear el code)",
          "request": {
            "method": "POST",
            "header": [],
            "body": {
              "mode": "urlencoded",
              "urlencoded": [
                { "key": "grant_type", "value": "authorization_code" },
                { "key": "client_id", "value": "{{clientId}}" },
                { "key": "code", "value": "{{authorizationCode}}" },
                { "key": "redirect_uri", "value": "{{redirectUri}}" },
                { "key": "code_verifier", "value": "{{codeVerifier}}" }
              ]
            },
            "url": "{{baseUrl}}/connect/token"
          },
          "event": [
            {
              "listen": "test",
              "script": {
                "exec": [
                  "pm.test('200 OK', () => pm.response.to.have.status(200));",
                  "const tokens = pm.response.json();",
                  "pm.collectionVariables.set('accessToken', tokens.access_token);",
                  "pm.collectionVariables.set('refreshToken', tokens.refresh_token);",
                  "pm.collectionVariables.set('idToken', tokens.id_token);"
                ]
              }
            }
          ]
        },
        {
          "name": "Token (refresh)",
          "request": {
            "method": "POST",
            "header": [],
            "body": {
              "mode": "urlencoded",
              "urlencoded": [
                { "key": "grant_type", "value": "refresh_token" },
                { "key": "client_id", "value": "{{clientId}}" },
                { "key": "refresh_token", "value": "{{refreshToken}}" }
              ]
            },
            "url": "{{baseUrl}}/connect/token"
          },
          "event": [
            {
              "listen": "test",
              "script": {
                "exec": [
                  "pm.test('200 OK: el refresh token rota (reusar el anterior revoca toda la cadena)', () => pm.response.to.have.status(200));",
                  "const tokens = pm.response.json();",
                  "pm.collectionVariables.set('accessToken', tokens.access_token);",
                  "pm.collectionVariables.set('refreshToken', tokens.refresh_token);",
                  "if (tokens.id_token) { pm.collectionVariables.set('idToken', tokens.id_token); }"
                ]
              }
            }
          ]
        },
        {
          "name": "UserInfo",
          "request": {
            "method": "GET",
            "header": [{ "key": "Authorization", "value": "Bearer {{accessToken}}" }],
            "url": "{{baseUrl}}/connect/userinfo"
          }
        },
        {
          "name": "Revocar el refresh token",
          "request": {
            "method": "POST",
            "header": [],
            "body": {
              "mode": "urlencoded",
              "urlencoded": [
                { "key": "token", "value": "{{refreshToken}}" },
                { "key": "token_type_hint", "value": "refresh_token" },
                { "key": "client_id", "value": "{{clientId}}" }
              ]
            },
            "url": "{{baseUrl}}/connect/revoke"
          }
        },
        {
          "name": "Logout",
          "protocolProfileBehavior": { "followRedirects": false },
          "request": {
            "method": "GET",
            "header": [],
            "url": "{{baseUrl}}/connect/logout?id_token_hint={{idToken}}&post_logout_redirect_uri={{postLogoutRedirectUri}}"
          },
          "event": [
            {
              "listen": "test",
              "script": {
                "exec": [
                  "pm.test('302 al post_logout_redirect_uri', () => pm.response.to.have.status(302));"
                ]
              }
            }
          ]
        }
      ]
    },
    {
      "name": "3. Api",
      "item": [
        {
          "name": "Mi perfil (/api/me)",
          "request": {
            "method": "GET",
            "header": [
              { "key": "Authorization", "value": "Bearer {{accessToken}}" },
              { "key": "Accept-Language", "value": "es" }
            ],
            "url": "{{baseUrl}}/api/me"
          }
        },
        {
          "name": "Usuarios (/api/users, requiere users.read)",
          "request": {
            "method": "GET",
            "header": [
              { "key": "Authorization", "value": "Bearer {{accessToken}}" },
              { "key": "Accept-Language", "value": "es" }
            ],
            "url": "{{baseUrl}}/api/users?page=1&pageSize=20&sort=email"
          }
        }
      ]
    },
    {
      "name": "4. Google (en el navegador)",
      "item": [
        {
          "name": "Armar la URL de ingreso con Google",
          "protocolProfileBehavior": { "followRedirects": false },
          "request": {
            "method": "GET",
            "header": [],
            "url": "{{googleLoginUrl}}"
          },
          "event": [
            {
              "listen": "prerequest",
              "script": {
                "exec": [
                  "const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~';",
                  "let verifier = '';",
                  "for (let i = 0; i < 64; i++) { verifier += alphabet.charAt(Math.floor(Math.random() * alphabet.length)); }",
                  "const challenge = CryptoJS.SHA256(verifier).toString(CryptoJS.enc.Base64).replace(/\\+/g, '-').replace(/\\//g, '_').replace(/=+$/, '');",
                  "pm.collectionVariables.set('codeVerifier', verifier);",
                  "pm.collectionVariables.set('codeChallenge', challenge);",
                  "const authorize = '/connect/authorize?response_type=code&client_id=' + pm.collectionVariables.get('clientId')",
                  "  + '&redirect_uri=' + encodeURIComponent(pm.collectionVariables.get('redirectUri'))",
                  "  + '&scope=' + encodeURIComponent(pm.collectionVariables.get('scopes'))",
                  "  + '&code_challenge=' + challenge + '&code_challenge_method=S256&state=google';",
                  "pm.collectionVariables.set('googleLoginUrl', pm.collectionVariables.get('baseUrl') + '/account/external/google?returnUrl=' + encodeURIComponent(authorize));"
                ]
              }
            },
            {
              "listen": "test",
              "script": {
                "exec": [
                  "pm.test('302 a Google', () => pm.response.to.have.status(302));",
                  "console.log('Abrí esta URL en el navegador:', pm.collectionVariables.get('googleLoginUrl'));",
                  "console.log('Al final, copiá el code de la URL de oauth.pstmn.io en la variable authorizationCode y ejecutá \"Token (canjear el code)\".');"
                ]
              }
            }
          ]
        }
      ]
    }
  ]
}
```

- [ ] **Paso 2: README de la colección**

`docs/postman/README.md`:

````markdown
# Probar la identidad con Postman

La colección `ArquitecturaBase.postman_collection.json` recorre el ingreso completo contra la Api local, como lo va a hacer el SPA en la Fase 3.

## Antes de empezar

1. Levantá todo con `aspire run` desde la raíz del repo. La Api queda en `https://localhost:7180`.
2. Importá la colección en Postman (Import → archivo).
3. Postman tiene que aceptar el certificado de desarrollo. Hay dos opciones:
   - confiar en él con `dotnet dev-certs https --trust`;
   - desactivar "SSL certificate verification" en Settings → General.
4. En las variables de la colección, cambiá `email` por tu email. Con el de `Seed:AdminEmail` entrás como Admin y podés ver `/api/users`.

## Ingreso con código

| Paso | Request | Qué pasa |
|---|---|---|
| 1 | 1. Account → Pedir código | 202. En desarrollo, el email se guarda como `.eml` en `src/ArquitecturaBase.Api/.emails/`. El código es la primera palabra del asunto. |
| 2 | Copiá el código en la variable `code` | |
| 3 | 1. Account → Verificar código | 200. Postman guarda la cookie de sesión del servidor. |
| 4 | 2. Connect → Authorize | Genera el PKCE y guarda el `authorizationCode` de la redirección. |
| 5 | 2. Connect → Token (canjear el code) | Guarda el access token, el refresh token y el id token. |
| 6 | 3. Api → Mi perfil / Usuarios | Con el access token. `/api/users` responde 403 si tu usuario no tiene `users.read`. |
| 7 | 2. Connect → Token (refresh) | Rota el refresh token. Si reusás el anterior, se revoca toda la cadena. |
| 8 | 2. Connect → Logout | Cierra la sesión y revoca los tokens. |

Límites que conviene conocer:
- Entre dos pedidos de código hay que esperar 60 segundos.
- Se aceptan 5 pedidos por email cada 15 minutos, y 20 por IP.
- Cada código admite 5 intentos. Con 10 fallos seguidos, la cuenta se bloquea 15 minutos.

## Ingreso con Google

Requiere el secreto del cliente en user-secrets (ver el README principal).

1. Ejecutá "4. Google → Armar la URL de ingreso con Google". La consola de Postman muestra la URL.
2. Abrila en el navegador y entrá con Google. Al final, el navegador queda en `https://oauth.pstmn.io/v1/callback?code=...`.
3. Copiá el valor de `code` en la variable `authorizationCode`.
4. Ejecutá "2. Connect → Token (canjear el code)". Usa el `codeVerifier` que generó el paso 1.

## Emails reales con Gmail

Por defecto, en desarrollo los emails se guardan como archivos. Para enviarlos con Gmail, ver "Emails" en el README principal.
````

- [ ] **Paso 3: README principal**

Agregar una sección `## Identidad (Fase 2)` con este contenido:

````markdown
## Identidad (Fase 2)

El ingreso es sin contraseña: con un código de 6 dígitos que llega por email, o con Google. La Api es a la vez el servidor OpenIddict (`/connect/*`) y la Api de negocio (`/api/*`, con bearer).

### Configuración de desarrollo

`src/ArquitecturaBase.Api/appsettings.Development.json` ya trae lo necesario para trabajar local:
- la clave HMAC de los códigos;
- las redirect URIs del cliente `web`;
- `Seed:AdminEmail`, el email que recibe el rol Admin al crear su cuenta;
- emails guardados como `.eml` en `src/ArquitecturaBase.Api/.emails/`, ignorada por git.

### Secretos (user-secrets de la Api)

| Clave | Para qué | Cómo |
|---|---|---|
| `Authentication:Google:ClientSecret` | ingreso con Google (obligatorio si hay `ClientId`) | `dotnet user-secrets set "Authentication:Google:ClientSecret" "<secreto>" --project src/ArquitecturaBase.Api` |
| `Email:Smtp:Password` | enviar emails reales por Gmail | contraseña de aplicación: https://myaccount.google.com/apppasswords |

En Google Cloud Console, el cliente OAuth tiene que tener como URIs de redireccionamiento autorizados `https://localhost:7180/signin-google` (Api directa) y `https://localhost:5173/signin-google` (a través de Vite, Fase 3).

### Emails

Para enviar por Gmail en lugar de guardar archivos:
1. En `appsettings.Development.json`, cambiar `Email:Delivery` a `Smtp`.
2. Cargar como user-secrets `Email:Smtp:UserName` y `Email:Smtp:FromAddress` (la cuenta de Gmail) y `Email:Smtp:Password`.

### Probar el flujo

Con Postman: [docs/postman/README.md](docs/postman/README.md).
````

- [ ] **Paso 4: CLAUDE.md**

- En "Casos de uso", reemplazar la viñeta de `SaveChanges` por:

```markdown
- Los handlers no llaman a `SaveChanges`: lo hace `UnitOfWorkDecorator` si el resultado fue exitoso, o siempre si el comando implementa `IPersistChangesOnFailure` (por ejemplo, `VerifyLoginCode`, que guarda el intento fallido y la auditoría).
```

- Agregar una sección `## Identidad` después de "Result en lugar de excepciones":

```markdown
## Identidad

- Application accede a usuarios, roles y sesión solo por `IIdentityService`; `UserManager`/`SignInManager` no salen de Infrastructure.
- `/api` usa bearer (validación de OpenIddict, esquema por defecto). La cookie de Identity la usan solo `/account` y `/connect`.
- Los endpoints piden permisos, nunca roles: `.RequirePermission(Permissions.Users.Read)`.
- Un permiso nuevo:
  1. se declara en `Domain/Authorization/Permissions.cs` y en `Permissions.All`;
  2. el seed se lo da a Admin;
  3. si cambian los permisos de un rol, hay que llamar a `IPermissionService.InvalidateRoleAsync`.
- Los claims de los tokens los arma `Api/Endpoints/Connect/OpenIdPrincipalFactory.cs`. Los permisos no van en el token.
- Nunca registrar códigos, tokens ni secretos. La auditoría de ingresos guarda el motivo del fallo (el código de error), nunca el código ingresado.
- Emails: plantillas embebidas en `Infrastructure/Emails/Templates` y textos en `Emails.resx`/`Emails.en.resx`, en el idioma del perfil.
```

- En "Tests", agregar dentro de la viñeta de Api.IntegrationTests:

```markdown
  - Autenticación:
    - `AuthFlow.LoginAsync` hace el ingreso real (código → authorize con PKCE → token) y devuelve los tokens;
    - con el header `X-Test-UserId`, en cambio, se usa el usuario de prueba.
  - `factory.EmailSender` guarda los emails: el código es la primera palabra del asunto.
  - Los límites están relajados:
    - sin espera entre pedidos de código;
    - rate limiter alto.
    Para probar un límite, usar `factory.WithWebHostBuilder(...)` con el valor real.
```

- En "Build", cambiar la viñeta de secretos por:

```markdown
- Los secretos van en user-secrets (Api: `Authentication:Google:ClientSecret`, `Email:Smtp:Password`) o en variables de entorno, nunca en el repo. Excepciones de desarrollo local: la contraseña de Postgres en `src/ArquitecturaBase.AppHost/appsettings.Development.json` y la clave HMAC de los códigos en `src/ArquitecturaBase.Api/appsettings.Development.json`.
```

- [ ] **Paso 5: pendientes de la Fase 1**

En `docs/plans/2026-09-18-fase-1-fundaciones.md`, sección "Pendientes para la Fase 2", marcar como hechos:
- el 6: largo máximo de `Search` y `%`/`_` escapados en el listado de usuarios;
- el 7: `ErrorCodeTranslationTests`;
- los cuatro primeros subpuntos del 8. `LoginCodeEndpointsTests.Rate_limiter_rejects_with_a_problem_and_retry_after`, `AddIdentityCore` + esquema combinado del arnés, `OpenIddictServerTests.Anonymous_api_request_is_challenged_by_the_bearer_scheme` y `MigrationsTests`.

El formato es el mismo que el de los puntos 1 y 2: "**Hecho (Fase 2).** …".

El quinto subpunto (fallback del SPA) y los puntos 3, 4, 5 y 9 siguen pendientes.

- [ ] **Paso 6: commit**

```bash
git add docs README.md CLAUDE.md
git commit -m "docs: documentar la identidad y agregar la colección de Postman" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 25: Verificación final

- [ ] **Paso 1: build y tests**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

Esperado: `0 Advertencia(s)`, `0 Errores`; todos los proyectos de test en verde. Mostrar el total por proyecto.

- [ ] **Paso 2: AppHost**

El usuario ya cargó el secreto de Google (Tarea 22) y Docker está encendido.

```bash
aspire run --detach
aspire describe
```

Esperado: `postgres`, `appdb` y `api` en estado `Running`/`Healthy`. En `aspire logs api` se ve que se aplicó la migración `InitialIdentity` y que no hay errores al iniciar.

- [ ] **Paso 3: humo por HTTP**

```bash
curl -sk https://localhost:7180/.well-known/openid-configuration
curl -sk -o /dev/null -w "%{http_code}\n" -X POST https://localhost:7180/account/login-code -H "Content-Type: application/json" -d "{\"email\":\"humo@example.com\"}"
ls src/ArquitecturaBase.Api/.emails
```

Esperado:
- el documento de discovery con los seis endpoints;
- `202`;
- un `.eml` nuevo. No mostrar su contenido: tiene el código.

- [ ] **Paso 4: parar el AppHost**

```bash
aspire stop
```

No dejarlo corriendo: bloquea los DLL.

- [ ] **Paso 5: pruebas manuales (las hace el usuario)**

Pedirle al usuario que siga `docs/postman/README.md`:
- ingreso con código hasta `/api/users` con su email de admin;
- refresh;
- logout;
- ingreso con Google en el navegador.

Si quiere probar Gmail, que siga "Emails" en el README principal.

- [ ] **Paso 6: revisión final y cierre**

- Revisión de código de toda la fase con un subagente revisor (diff desde el commit del plan hasta `HEAD`).
- Agregar a este plan la sección "Resultado de la ejecución", con la misma estructura que la de la Fase 1: tests por proyecto, desvíos y pendientes para la Fase 3.
- Commit:

```bash
git add docs/plans/2026-09-19-fase-2-identidad.md
git commit -m "docs: registrar el resultado de la Fase 2" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Resultado de la ejecución (2026-09-19)

**Estado:**
- Implementadas las Tareas 1 a 24, más una tarea extra (26) con las correcciones de la revisión final.
- La Tarea 25 está a medias. Falta el humo por HTTP con el AppHost y las pruebas manuales con Postman y Google, que esperan a que el usuario cargue el secreto de Google en los user-secrets de la Api.

**Proceso:**
- Cada tarea, o grupo de tareas chicas, la hizo un implementador.
- Antes de seguir, cada una pasó una revisión en dos etapas: cumplimiento del plan y calidad.
- Al final, un revisor independiente miró toda la fase.

**Tests:** `dotnet test` da 348/348 y `dotnet build` da 0 advertencias.

| Proyecto | Tests |
|---|---|
| Domain.UnitTests | 47 |
| Application.UnitTests | 125 |
| ArchitectureTests | 11 |
| Api.IntegrationTests | 165 |

**AppHost:**
- Postgres levanta y la Api aplica la migración `InitialIdentity` sobre la base real.
- Sin el secreto de Google, la Api no arranca y el log explica cómo cargarlo.
- Pendiente:
  - humo por HTTP: discovery, pedido de código y el `.eml` generado;
  - la prueba manual con Postman y Google (`docs/postman/README.md`).

### Desvíos respecto del plan

- **Tarea 6:** `DependencyInjectionTests.Decorators_are_not_registered_as_handlers` quedó acotado a tipos de `ArquitecturaBase`, porque `AddOptions` registra genéricos abiertos del framework.
- **Tarea 12:**
  - CA1725: el parámetro de `OnModelCreating` se llama `builder`, como en `IdentityDbContext`.
  - CA1859: un helper de test devuelve `Task<int>`.
- **Tareas 14 y 15:** también por CA1859, `EmailTemplateRenderer.Fill` recibe `Dictionary` y un doble de prueba expone `ConcurrentQueue`.
- **Tarea 18** (plan ajustado en `cbc1f3a`): `OpenApiTests` levanta la Api en Development con una base propia y el `ApplicationDbContext` de producción. Así prueba migraciones y seed de punta a punta.
- **Rojo esperado entre las Tareas 8 y 15:** `OpenApiTests` (Development, con `ValidateOnBuild`) estuvo en rojo hasta que la 16 registró los últimos servicios.
- **Tareas 5 a 7:** se reescribieron sus commits para corregir el trailer `Co-Authored-By`.

### Correcciones que salieron de las revisiones

- `e6e4bab`: test de que `GetLatestAsync` devuelve códigos ya usados, que es la base del error `AlreadyUsed`.
- `c679b24`: test del esquema por defecto de producción (bearer) y de la validación de tokens revocados.
- `45068f0`: error del plan. El 429 del rate limiter daba 500 si el cliente no aceptaba JSON; ahora usa `TryWriteAsync`. Se sumaron tests del límite de `/verify` y de JSON enviado como `text/plain`.
- `f17ff39`: `/connect/userinfo` rechaza cuentas deshabilitadas, y los tests del vencimiento y de la cadena de refresh tienen control positivo.
- `756ce28`: error del plan. Sin el secreto de Google, el framework cortaba con un error genérico; ahora el mensaje explica cómo cargarlo. Se sumaron tests del ingreso externo.
- `68d6dca`: `PostLogoutRedirectUris` pasa a ser obligatoria, como documenta el README.
- **Tarea 26**, de la revisión final:
  - `1e690c1`: el reloj de los tests arranca en la hora real. Con la fecha fija, el cliente de test iba a descartar la cookie de sesión por vencida desde el 2026-10-18 aprox.
  - `377d4c9`: pedidos y verificaciones de códigos de un mismo email se ponen en fila, con `pg_advisory_xact_lock` en una transacción que confirma `UnitOfWork`. Antes, con requests en paralelo se salteaban:
    - el límite de intentos;
    - el bloqueo;
    - la auditoría;
    - el reenvío;
    - el límite por email.
  - `7ad8c1b`: el bloqueo de Identity se configura en `Authentication:LoginCode` (sección 5.3).
  - `25410a2`: encolar un email nunca espera. Con la cola llena, el email se descarta y se registra. Antes, junto con el lock, una cola llena podía agotar el pool de conexiones.

### Riesgos aceptados

- **Verificar sin código activo:** devuelve `Auth.LoginCode.Invalid` sin `attemptsLeft`. Se puede inferir si hay un código pendiente, pero no si existe la cuenta.
- **Bloqueo:** después de 10 fallos, `Auth.Account.LockedOut` delata que la cuenta existe. Es una regla del spec.
- **`Email.HasValidFormat`:** es básico; por ejemplo, acepta `ana@.com`.
- **Refresh token:** vence de forma deslizante (30 días desde el último uso), igual que la cookie. Para un tope absoluto: `DisableSlidingRefreshTokenExpiration()`.
- **Logout por GET sin `id_token_hint`:** cierra la sesión, así que otro sitio puede desloguear al usuario. El impacto es bajo.

### Pendientes para la Fase 3 y producción

**Seguridad y operación**
1. **HTTPS:** falta `UseHttpsRedirection`, HSTS, CSP y los encabezados de la sección 6.9. La Api también escucha por http.
2. **Rate limiter:** agrupar IPv6 por /64, aplicar `MapToIPv4` y usar `UseForwardedHeaders` detrás de un proxy.
3. **`/account/external/callback`:** no tiene rate limiter y audita cada request. Las fallas de Google (cancelación, correlación) no se auditan ni se registran.
4. **`LockedOut`:** responde 429 sin `retryAfter`.
5. **Emails:**
   - MailKit no tiene timeout propio: 2 minutos por intento;
   - si la cola está llena, el email se descarta, el pedido igual responde 202 y el usuario tiene que esperar 60 s para pedir otro;
   - `EmailBackgroundService` registra la excepción completa, y algunos servidores SMTP incluyen el destinatario.
6. **Lock por email:** cada request que espera el lock ocupa una conexión. Conviene acotar la espera (`pg_try_advisory_xact_lock`) cuando se resuelva el punto 2. Además, el lock no convive con `EnableRetryOnFailure` (ver CLAUDE.md).
7. **Cuentas deshabilitadas:** deshabilitar una cuenta no revoca sus tokens. Se agrega cuando exista esa función (Fase 4).
8. **Varias instancias:** HybridCache no tiene L2, así que la invalidación de permisos y el rate limiter son locales a cada instancia.
9. **Limpieza periódica:** falta para `LoginCodes`, `LoginAudits` y las autorizaciones y tokens de OpenIddict.
10. **Data Protection:** las claves quedan sin cifrar en Postgres. En producción: `ProtectKeysWithCertificate`.
11. **Migraciones y seed en producción:** quedan para cuando haya pipeline.

**Funcionales**

12. `authorize` ignora `prompt=login` y `max_age`.
13. **Errores del ingreso con Google:**
    - vuelven a `/login?error=` sin el `returnUrl`;
    - si el callback falla la validación, la cookie externa queda abierta hasta que vence (5 minutos).
14. **Ingreso con Google y el contador de fallos:** el ingreso con Google no reinicia el contador, y vincula la cuenta antes de mirar si está deshabilitada o bloqueada.
15. **Carrera poco probable:** si el mismo usuario verifica un código e ingresa con Google al mismo tiempo, Identity puede devolver `ConcurrencyFailure`.
16. **HybridCache:** el factory usa el `DbContext` de la primera petición.

**Código y tests**

17. **Duplicaciones:**
    - la regla del `returnUrl`, en 3 lugares;
    - el armado de `/login?error=`, en 2;
    - la clave `"LoginProvider"`, en 2;
    - las rutas de `/connect`, en la Api y en Infrastructure;
    - dos clases `EndpointExtensions`.
18. **Tests que faltan:**
    - la promoción a Admin por el seed;
    - el escape de `%` y `\`;
    - la búsqueda por DisplayName;
    - las opciones de la cookie;
    - la duración del bloqueo;
    - el backoff de los emails;
    - la cancelación real en Google.
    Los sondeos de concurrencia de la revisión final se pueden convertir en tests.
19. **Log del key ring:** al arrancar la `ApiFactory` sale "An error occurred while reading the key ring". No rompe nada, pero el hecho verificado 16 lo daba por resuelto.
20. **Detalles:**
    - el recorte del DisplayName puede partir un par sustituto;
    - en la auditoría, una IP IPv4 queda guardada como dirección mapeada a IPv6.
