# ARCA · Etapa 1: plan de implementación

> **Para quien lo ejecute:** se trabaja tarea por tarea, con TDD (el test en rojo primero) y en `main`, sin worktrees
> ([AGENTS.md](../../AGENTS.md)). Es el modo liviano de las etapas: nueve tareas, una revisión por tarea y la suite completa
> solo al cerrar. Las casillas (`- [ ]`) se marcan al terminar cada paso. Lo que sigue un patrón existente lo nombra con su
> archivo de referencia; el código nuevo de verdad (CUIT, CSR, firma CMS, sobres SOAP, reserva del ticket) va completo.

**Objetivo:** conectar una instalación con ARCA usando un certificado nuevo de la empresa, probar la conexión, traer los
datos de la empresa desde el padrón y elegir el punto de venta, sin emitir comprobantes.

**Arquitectura:** es el módulo opcional `Arca` en `Modules/Arca` de cada capa ([ADR 0023](../decisions/0023-arca-como-modulo-opcional.md)).
Copia la forma del módulo de IA en el anillo propio, la conexión única con revisión y 409, la prueba manual y el módulo de
tests. Los clientes SOAP son propios, y el ticket de acceso se guarda cifrado en PostgreSQL y se renueva con una reserva.

**Tecnología:** .NET 10, EF Core con PostgreSQL, `System.Security.Cryptography` (`CertificateRequest`, `SignedCms` de
`System.Security.Cryptography.Pkcs`), `HttpClient` con `System.Xml.Linq`, xUnit v3 y Testcontainers. En el front, React,
TanStack Query, Vitest con MSW y Playwright.

**Fuentes:** [diseño de la etapa](2026-11-03-arca-etapa-1-conexion.md), tableros AC1-A, AC2-A y AC3 del lienzo
[Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), y la sección A.2 del
[estudio de integraciones](integraciones/estudio-2026-10-22.md) (WSAA, WSFEv1 y errores).

---

## Archivos

| Capa | Archivos nuevos |
| --- | --- |
| Domain `Modules/Arca/` | `ArcaPermissions`, `Cuit`, `ArcaAccessMode`, `ArcaEnvironment`, `ArcaConnectionStatus`, `ArcaService`, `ArcaVatCondition`, `ArcaCompanyData`, `ArcaConnection`, `ArcaCertificate`, `ArcaCertificateRole`, `ArcaAccessTicket`, `ArcaTicketLease`, `ArcaConnectionErrors` |
| Application `Modules/Arca/` | `ArcaApplicationRegistration`; `Channels/ArcaPermissionCatalogSource`; `Resources/Permissions(.en).resx`; `Interfaces/Services/IArcaConnectionService`, `IArcaConnectionTestService`; `Interfaces/Persistence/IArcaConnectionRepository`, `IArcaConnectionReader`; `Interfaces/Integrations/IArcaCredentialService`, `IArcaAuthClient`, `IArcaInvoicingClient`, `IArcaRegistryClient`; `Models/*`; `Services/ArcaConnectionService`, `ArcaConnectionTestService`, `ArcaAccessTicketProvider`, `ArcaCompanyDataPolicy` |
| Infrastructure `Modules/Arca/` | `ArcaInfrastructureRegistration`, `ArcaOptions`; `Security/ArcaKeyRing`, `ArcaSecretProtectionOptions`, `ArcaCredentialService`, `ArcaLoginTicketRequest`, `ArcaCmsSigner`; `Soap/ArcaAuthClient`, `ArcaInvoicingClient`, `ArcaRegistryClient`, `SoapEnvelope`; `Persistence/ArcaLockKeys`, `Configurations/*`, `Repositories/ArcaConnectionRepository`, `Readers/ArcaConnectionReader` |
| Api `Modules/Arca/` | `ArcaApiRegistration`, `ArcaRateLimitOptions`; `Controllers/ArcaConnectionController`; `Contracts/StartArcaConnectionHttpRequest`, `UploadArcaCertificateHttpRequest`, `ChooseArcaPointOfSaleHttpRequest`, `ArcaRevisionHttpRequest` |
| Comunes | `Program.cs` (bloque del módulo), `Application/Resources/Errors(.en).resx`, `Directory.Packages.props`, migración `AddArcaConnection` |
| Tests | `Domain.UnitTests/Modules/Arca/*`, `Application.UnitTests/Modules/Arca/*` y `TestFeatures/Modules/Arca/*`, `ArchitectureTests/Modules/Arca/ArcaTransactionContribution`, `Api.IntegrationTests/Modules/Arca/*` (con `ArcaTestModule`) y `Api.IntegrationTests/TestFeatures/Modules/Arca/*` (`FakeArca` y `FakeArcaCertificateAuthority`) |
| Front `src/features/arca/` | `api/arcaConnection.ts`, `pages/ArcaSettingsPage.tsx`, `components/*`, `model.ts`, `locales/{es,en}/arca.json`, la ruta y la entrada del menú |
| Docs | `docs/features/arca.md`, `docs/guides/configurar-arca.md`, `docs/guides/quitar-arca.md`, `AGENTS.md` y `CLAUDE.md` de cada carpeta nueva, el mapa de instrucciones |

## Desacople

ARCA se tiene que poder quitar entero, como WhatsApp y la IA ([ADR 0007](../decisions/0007-whatsapp-como-modulo-opcional.md),
[módulos opcionales](../architecture/backend.md#módulos-opcionales)). Todo el código vive en las carpetas `Modules/Arca`
(cuatro en `src` y cuatro en `tests`, más `TestFeatures/Modules/Arca` en Application.UnitTests y Api.IntegrationTests) y en
`src/features/arca` del front. El núcleo no las nombra, y `ModuleBoundaryTests` lo verifica en cada corrida.

Fuera de esas carpetas, el módulo toca solo estos lugares, y la guía `quitar-arca.md` los enumera:

| Lugar | Qué hay | Al quitar el módulo |
| --- | --- | --- |
| `src/ArquitecturaBase.Api/Program.cs` | Tres `using` marcados `// módulo ARCA` y el bloque del módulo | Se borran |
| `src/ArquitecturaBase.Api/appsettings.json` | La sección `Arca` (direcciones, emisores, plazos) | Se borra |
| User-secrets o variables del host | `Arca:SecretProtection:*` | Se guarda una copia segura para los backups y se borra |
| `Application/Resources/Errors.resx` y `.en.resx` | Las claves `Arca.*` | Se borran; si no, `ErrorCodeTests` falla y las nombra |
| `Directory.Packages.props` e `Infrastructure.csproj` | `System.Security.Cryptography.Pkcs` | Se quita del `.csproj`, y de `Directory.Packages.props` si nadie más lo usa |
| La cadena de migraciones | `AddArcaConnection`, que nombra sus entidades con cadenas | Se conserva y se genera `RemoveArca`, que borra solo las tres tablas |
| Front: `src/app/routes.tsx`, `src/layouts/navigation.ts` y `common.json` | La ruta `/configuracion/arca`, la entrada del menú y su texto | Se borran |
| Front: `src/test/visual` | La escena y las capturas de ARCA | Se borran |
| Docs | `features/arca.md`, `guides/configurar-arca.md` y los enlaces a ellos | Se borran; el ADR 0023 y el historial quedan |

**Para las etapas que siguen**, la misma regla:
- El motor de formularios (BusinessForms) es otro módulo y no puede nombrar a ARCA. La emisión (etapa 3) entra por un
  puerto neutral del núcleo en `Application/Interfaces/Channels`, con su versión apagada, como el canal de avisos que
  implementa WhatsApp.
- El estándar «Factura» y los catálogos de ARCA los aporta el módulo por puertos del núcleo, así que se van con él.
- Lo genérico del motor (renglones, cálculos, condiciones, resultado externo y carpetas) se queda: no nombra a ARCA.

---

### Tarea 1: esqueleto del módulo, permisos y anillo

**Archivos:**
- Crear: `src/ArquitecturaBase.Domain/Modules/Arca/ArcaPermissions.cs`
- Crear: `src/ArquitecturaBase.Application/Modules/Arca/ArcaApplicationRegistration.cs`, `Channels/ArcaPermissionCatalogSource.cs`, `Resources/Permissions.resx`, `Resources/Permissions.en.resx`
- Crear: `src/ArquitecturaBase.Infrastructure/Modules/Arca/ArcaInfrastructureRegistration.cs`, `ArcaOptions.cs`, `Security/ArcaSecretProtectionOptions.cs`, `Security/ArcaKeyRing.cs`
- Crear: `src/ArquitecturaBase.Api/Modules/Arca/ArcaApiRegistration.cs`
- Modificar: `src/ArquitecturaBase.Api/Program.cs` (bloque después del de IA)
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca/ArcaTestModule.cs`, `ArcaOptionsTests.cs`, `ArcaPermissionsTests.cs`

Referencias: `ArtificialIntelligencePermissions`, `AiPermissionCatalogSource`, `AiSecretProtectionOptions`, `AiKeyRing`,
`ArtificialIntelligenceInfrastructureRegistration` (líneas 24 a 27) y `AiTestModule`.

- [ ] **Paso 1: tests en rojo.** En `ArcaPermissionsTests`, `GET /api/roles/permissions` (o el catálogo compuesto) incluye
  `arca.read` y `arca.manage` con su área y sus textos en español y en inglés. En `ArcaOptionsTests`, la app no arranca
  sin `Arca:SecretProtection` válido y arranca con el del `ArcaTestModule`.
- [ ] **Paso 2: correr y ver que fallan.**
  `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Modules.Arca.ArcaPermissionsTests"`
- [ ] **Paso 3: permisos.**

```csharp
namespace ArquitecturaBase.Domain.Modules.Arca;

public static class ArcaPermissions
{
    public const string Read = "arca.read";
    public const string Manage = "arca.manage";
    public static IReadOnlyCollection<string> All { get; } = [Read, Manage];
}
```

  `ArcaPermissionCatalogSource` es igual a `AiPermissionCatalogSource`, con el `ResourceManager` de
  `ArquitecturaBase.Application.Modules.Arca.Resources.Permissions`. Los recursos llevan `Area.Arca` («ARCA»),
  `Permission.arca.read` («Ver la conexión con ARCA»), `Permission.arca.manage` («Administrar la conexión con ARCA») y sus
  descripciones, en `.resx` y `.en.resx`.

- [ ] **Paso 4: anillo y opciones.** `ArcaSecretProtectionOptions` copia `AiSecretProtectionOptions` (instalación, clave
  activa, claves de 256 bits y `IsValid()`), con su `ToString` redactado. `ArcaKeyRing` copia `AiKeyRing` sin las
  derivaciones de huellas.

```csharp
namespace ArquitecturaBase.Infrastructure.Modules.Arca;

public sealed record ArcaOptions
{
    public ArcaEndpoints Testing { get; init; } = new();
    public ArcaEndpoints Production { get; init; } = new();
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public int MaxResponseBytes { get; init; } = 512 * 1024;
    public TimeSpan TicketMargin { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan TicketLease { get; init; } = TimeSpan.FromSeconds(60);

    public bool IsValid() => Testing.IsValid() && Production.IsValid() && RequestTimeout > TimeSpan.Zero && MaxResponseBytes > 0;
}

public sealed record ArcaEndpoints
{
    public Uri? Wsaa { get; init; }
    public Uri? Wsfe { get; init; }
    public Uri? Registry { get; init; }
    /// El CN del emisor de los certificados de este ambiente; sirve para saber si un .crt es de homologación o de producción.
    public string IssuerCommonName { get; init; } = string.Empty;
    /// Lo que ARCA tarda en aceptar otro pedido de ticket para el mismo servicio (10 min en homologación, 2 en producción).
    public TimeSpan TicketRetryWindow { get; init; }

    public bool IsValid() => new[] { Wsaa, Wsfe, Registry }.All(u => u is { Scheme: "https" })
        && !string.IsNullOrWhiteSpace(IssuerCommonName) && TicketRetryWindow > TimeSpan.Zero;
}
```

  Los valores por defecto van en `src/ArquitecturaBase.Api/appsettings.json`, en la sección `Arca`. Las direcciones son
  las del estudio: WSAA `https://wsaahomo.afip.gov.ar/ws/services/LoginCms` y `https://wsaa.afip.gov.ar/ws/services/LoginCms`;
  WSFEv1 `https://wswhomo.afip.gov.ar/wsfev1/service.asmx` y `https://servicios1.afip.gov.ar/wsfev1/service.asmx`; padrón
  `https://awshomo.afip.gov.ar/sr-padron/webservices/personaServiceA5` y `https://aws.afip.gov.ar/sr-padron/webservices/personaServiceA5`.
  Los emisores y la dirección de homologación del padrón se confirman en la Tarea 6. El anillo nunca va en appsettings:
  va en user-secrets o en variables de entorno.

- [ ] **Paso 5: registros.** `AddArcaApplication()`, `AddArcaInfrastructure(IConfiguration)` y `AddArcaApi()`, con la
  forma de los de IA:
  - las opciones con `Bind`, `Validate` y `ValidateOnStart`;
  - el `IPermissionCatalogSource` con `TryAddEnumerable`;
  - tres `HttpClient` con nombre (`arca-wsaa`, `arca-wsfe`, `arca-registry`), sin redirecciones (`AllowAutoRedirect = false`),
    con `Timeout` de las opciones y los loggers del `HttpClient` apagados como en IA.

  En `Program.cs` van los tres `using` marcados `// módulo ARCA`, como los de WhatsApp, y el bloque debajo del de IA:

```csharp
// Módulo ARCA (ADR 0023). Para quitarlo: este bloque, los tres using marcados y docs/guides/quitar-arca.md.
builder.Services
    .AddArcaApplication()
    .AddArcaInfrastructure(builder.Configuration)
    .AddArcaApi();
```

- [ ] **Paso 6: `ArcaTestModule`.** Pone `Arca:SecretProtection` con `ApiFactory.TestHashKey`, rate limits altos, y deja los
  tres `HttpClient` apuntando a `FakeArca`, que en esta tarea responde siempre 503. `Routes` arranca vacío.
- [ ] **Paso 7: correr los tests de la tarea y los de arquitectura,** que tienen que pasar sin cambios: el módulo no se nombra
  desde el núcleo.
  `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj`
- [ ] **Paso 8: commit.** `feat(arca): módulo opcional con permisos y anillo propio`

---

### Tarea 2: dominio

**Archivos:**
- Crear en `src/ArquitecturaBase.Domain/Modules/Arca/`: `Cuit.cs`, los enums, `ArcaCompanyData.cs`, `ArcaConnection.cs`, `ArcaCertificate.cs`, `ArcaAccessTicket.cs`, `ArcaTicketLease.cs`, `ArcaConnectionErrors.cs`
- Modificar: `src/ArquitecturaBase.Application/Resources/Errors.resx` y `Errors.en.resx`
- Crear: `tests/ArquitecturaBase.Domain.UnitTests/Modules/Arca/CuitTests.cs`, `ArcaConnectionTests.cs`, `ArcaAccessTicketTests.cs`

- [ ] **Paso 1: tests en rojo.**

```csharp
public sealed class CuitTests
{
    [Theory]
    [InlineData("30-71000123-1", "30710001231")]
    [InlineData("30710001231", "30710001231")]
    [InlineData(" 30 71000123 1 ", "30710001231")]
    public void A_valid_cuit_is_normalized_to_eleven_digits(string input, string expected) =>
        Assert.Equal(expected, Cuit.Normalize(input));

    [Theory]
    [InlineData("30-71000123-2")]
    [InlineData("3071000123")]
    [InlineData("30-7100012A-1")]
    [InlineData("")]
    [InlineData(null)]
    public void An_invalid_cuit_is_rejected(string? input) => Assert.Null(Cuit.Normalize(input));

    [Fact]
    public void A_masked_cuit_keeps_only_the_prefix_and_the_last_digits() =>
        Assert.Equal("30-*****123-1", Cuit.Mask("30710001231"));
}
```

  En `ArcaConnectionTests`, con `FakeTimeProvider`:
  - empezar con una CUIT inválida da `ArcaConnectionErrors.CuitInvalid`;
  - empezar con una válida deja `AwaitingCertificate`, modo `CompanyCertificate` y revisión 1;
  - cargar el certificado pasa a `Unverified`;
  - una prueba en verde sin punto de venta deja `Unverified`;
  - elegir un punto de venta con la última prueba en verde pasa a `Active`, y con la última prueba fallida da
    `ArcaConnectionErrors.NotTested`;
  - una prueba fallida con la conexión activa pasa a `NeedsAttention`, y una en verde la vuelve a `Active`;
  - cada cambio sube la revisión;
  - `ToString` no muestra la CUIT completa.

  En `ArcaAccessTicketTests`:
  - un ticket vigente con margen se reutiliza (`AlreadyValid`);
  - sin ticket ni reserva, la reserva se toma (`Acquired`);
  - con la reserva de otro sin vencer da `Busy`, y vencida se toma;
  - con `BlockedUntilUtc` en el futuro da `Blocked`;
  - guardar con otro dueño de reserva no cambia nada y devuelve `false`.

- [ ] **Paso 2: correr y ver que fallan.**
  `dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj -- --filter-class "ArquitecturaBase.Domain.UnitTests.Modules.Arca.CuitTests"`
- [ ] **Paso 3: CUIT.** Un dígito verificador 10 no es válido: ARCA nunca lo asigna.

```csharp
namespace ArquitecturaBase.Domain.Modules.Arca;

public static class Cuit
{
    private static readonly int[] Weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    public static string? Normalize(string? value)
    {
        if (value is null) return null;
        var digits = new string(value.Where(c => c is not ('-' or ' ')).ToArray());
        if (digits.Length != 11 || !digits.All(char.IsAsciiDigit)) return null;
        var sum = 0;
        for (var i = 0; i < 10; i++) sum += (digits[i] - '0') * Weights[i];
        var check = 11 - sum % 11;
        if (check == 11) check = 0;
        return check != 10 && digits[10] - '0' == check ? digits : null;
    }

    public static string Mask(string cuit) => cuit.Length == 11 ? $"{cuit[..2]}-*****{cuit[7..10]}-{cuit[10]}" : "***";
}
```

- [ ] **Paso 4: enums y datos.**
  - `ArcaAccessMode { CompanyCertificate, PlatformDelegation, PlatformDelegationAutomated }`;
  - `ArcaEnvironment { Testing, Production }`;
  - `ArcaConnectionStatus { AwaitingCertificate, Unverified, Active, NeedsAttention }`;
  - `ArcaService { Invoicing, Registry }`, con los nombres de WSAA `wsfe` y `ws_sr_constancia_inscripcion` en un
    `ArcaServiceNames.Of(service)`;
  - `ArcaVatCondition { RegisteredTaxpayer, Monotax, Exempt, NotRegistered }`;
  - `ArcaCertificateRole { Active, Pending }`;
  - `ArcaCompanyData(string LegalName, ArcaVatCondition VatCondition, string FiscalAddress)`, un value object guardado como
    owned.
- [ ] **Paso 5: entidades.** `ArcaConnection : Entity, IAuditable`, con `Scope = 1` como `AiConnection`, y estos métodos:
  - `static Result<ArcaConnection> Start(string cuit, ArcaEnvironment environment)`;
  - `void CertificateLoaded()`;
  - `void RecordTest(bool passed, ArcaCompanyData? company, DateTime testedAtUtc)`;
  - `Result ChoosePointOfSale(int number)`, con `number` entre 1 y 99998 y `LastTestPassed` exigido;
  - `void CertificateExpired()`.

  `ArcaCertificate : Entity` lleva `ConnectionId`, `Role`, `Alias`, `RequestPem`, `ProtectedPrivateKey`,
  `CertificatePem?`, `NotBeforeUtc?`, `NotAfterUtc?` y `Thumbprint?`, con `Load(...)` y `Promote()` para la renovación.

  `ArcaAccessTicket : Entity` lleva `CertificateId`, `Service`, `ProtectedCredentials?`, `ExpiresAtUtc?`,
  `LeaseOwner?`, `LeaseUntilUtc?` y `BlockedUntilUtc?`:

```csharp
public ArcaTicketLease TryLease(Guid owner, DateTime nowUtc, TimeSpan margin, TimeSpan leaseDuration)
{
    if (ExpiresAtUtc is { } expires && expires - nowUtc > margin) return ArcaTicketLease.AlreadyValid;
    if (BlockedUntilUtc is { } blocked && blocked > nowUtc) return ArcaTicketLease.Blocked;
    if (LeaseOwner is not null && LeaseOwner != owner && LeaseUntilUtc > nowUtc) return ArcaTicketLease.Busy;
    LeaseOwner = owner;
    LeaseUntilUtc = nowUtc + leaseDuration;
    return ArcaTicketLease.Acquired;
}

public bool Store(Guid owner, string protectedCredentials, DateTime expiresAtUtc)
{
    if (LeaseOwner != owner) return false;
    (ProtectedCredentials, ExpiresAtUtc, LeaseOwner, LeaseUntilUtc, BlockedUntilUtc) = (protectedCredentials, expiresAtUtc, null, null, null);
    return true;
}

public bool Block(Guid owner, DateTime untilUtc)
{
    if (LeaseOwner != owner) return false;
    (ProtectedCredentials, ExpiresAtUtc, LeaseOwner, LeaseUntilUtc, BlockedUntilUtc) = (null, null, null, null, untilUtc);
    return true;
}
```

- [ ] **Paso 6: errores.** En `ArcaConnectionErrors`:
  - `CuitInvalid` y `AliasInvalid` (validación);
  - `CertificateUnreadable`, `CertificateKeyMismatch`, `CertificateCuitMismatch`, `CertificateEnvironmentMismatch`,
    `CertificateExpired` y `NoPendingRequest` (validación);
  - `NotTested` y `PointOfSaleUnavailable` (validación);
  - `RevisionConflict` (conflicto) y `NotConnected` (no encontrado).

  Los códigos son `Arca.Connection.<Motivo>`, con su texto en `Errors.resx` (voseo) y en `Errors.en.resx`.
- [ ] **Paso 7: correr** los tres tests de la tarea, `ErrorDeclarationTests` y `ResourceParityTests`.
- [ ] **Paso 8: commit.** `feat(arca): conexión, certificado y ticket de acceso en el dominio`

---

### Tarea 3: persistencia

**Archivos:**
- Crear: `src/ArquitecturaBase.Infrastructure/Modules/Arca/Persistence/ArcaLockKeys.cs`, `Configurations/ArcaConnectionConfiguration.cs`, `ArcaCertificateConfiguration.cs`, `ArcaAccessTicketConfiguration.cs`, `Repositories/ArcaConnectionRepository.cs`, `Readers/ArcaConnectionReader.cs`
- Crear: `src/ArquitecturaBase.Application/Modules/Arca/Interfaces/Persistence/IArcaConnectionRepository.cs`, `IArcaConnectionReader.cs`, `Models/ArcaConnectionSnapshot.cs`
- Crear: migración `AddArcaConnection` (según [migración](../guides/migracion.md))
- Crear: `tests/ArquitecturaBase.ArchitectureTests/Modules/Arca/ArcaTransactionContribution.cs`, `tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca/ArcaPersistenceTests.cs`

Referencias: `IAiConnectionRepository`, `IAiConnectionReader`, sus implementaciones y configuraciones, `AiAdvisoryLockKeys`,
`AiTransactionContribution`, y [nombres de repositorios](../decisions/0008-nombres-de-repositorios-y-lectores.md).

- [ ] **Paso 1: tests en rojo** en `ArcaPersistenceTests`, con PostgreSQL:
  - un segundo `ArcaConnection` con `Scope = 1` choca con el índice único;
  - un certificado activo y uno pendiente conviven, y un segundo pendiente choca (índice único por conexión y papel);
  - un ticket por certificado y servicio;
  - borrar la conexión borra certificados y tickets en cascada;
  - el lector devuelve la foto sin seguimiento y sin la clave ni las credenciales del ticket.
- [ ] **Paso 2: contratos.**

```csharp
public interface IArcaConnectionRepository
{
    Task LockConnectionAsync(CancellationToken cancellationToken);
    Task LockTicketAsync(Guid certificateId, ArcaService service, CancellationToken cancellationToken);
    Task<ArcaConnection?> GetConnectionAsync(CancellationToken cancellationToken);
    Task<ArcaCertificate?> GetCertificateAsync(Guid connectionId, ArcaCertificateRole role, CancellationToken cancellationToken);
    Task<ArcaAccessTicket?> GetTicketAsync(Guid certificateId, ArcaService service, CancellationToken cancellationToken);
    void Add(ArcaConnection connection);
    void Add(ArcaCertificate certificate);
    void Add(ArcaAccessTicket ticket);
    void Remove(ArcaConnection connection);
    void Remove(ArcaCertificate certificate);
}

public interface IArcaConnectionReader
{
    Task<ArcaConnectionSnapshot?> FindAsync(CancellationToken cancellationToken);
    Task<ArcaTicketSnapshot?> FindTicketAsync(Guid certificateId, ArcaService service, CancellationToken cancellationToken);
}
```

  `ArcaConnectionSnapshot` lleva la conexión, los dos certificados sin `ProtectedPrivateKey` y la revisión.
  `ArcaTicketSnapshot` lleva el vencimiento, la reserva, el bloqueo y las credenciales protegidas; solo lo usa el proveedor
  de tickets.
- [ ] **Paso 3: locks.** En `ArcaLockKeys`: `arca-connection:installation` y `arca-ticket:{certificateId}:{service}`.
  `ArcaTransactionContribution` declara `LockOwners`, que apunta a `ArcaLockKeys`, y `LockPrefixes` `["arca-connection:", "arca-ticket:"]`.
- [ ] **Paso 4: configuraciones EF.**
  - tablas `ArcaConnections`, `ArcaCertificates` y `ArcaAccessTickets` con `ToTable` explícito, como en los módulos;
  - `ArcaCompanyData` como owned;
  - enums como texto;
  - `Revision` como token de concurrencia, como en IA;
  - cascada desde la conexión.
- [ ] **Paso 5: migración** con la cadena de mentira del pipeline:
  `dotnet ef migrations add AddArcaConnection --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api`
  (los parámetros exactos están en [migración](../guides/migracion.md)).
- [ ] **Paso 6: correr** `ArcaPersistenceTests`, `MigrationsTests` y `TransactionBoundaryTests`.
- [ ] **Paso 7: commit.** `feat(arca): persistencia de la conexión, los certificados y los tickets`

---

### Tarea 4: credenciales (clave, pedido y certificado)

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Modules/Arca/Interfaces/Integrations/IArcaCredentialService.cs`, `Models/ArcaCertificateRequest.cs`, `Models/InspectedCertificate.cs`
- Crear: `src/ArquitecturaBase.Infrastructure/Modules/Arca/Security/ArcaCredentialService.cs`
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca/ArcaCredentialServiceTests.cs` y `tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/Arca/FakeArcaCertificateAuthority.cs`

- [ ] **Paso 1: la autoridad de prueba.** `FakeArcaCertificateAuthority` crea una CA autofirmada por ambiente, con el CN
  configurado en `ArcaTestModule` (`Computadores Test` y `Computadores`). `Issue(string requestPem, ArcaEnvironment environment, DateTime notAfterUtc)`
  carga el pedido con `CertificateRequest.LoadSigningRequestPem(requestPem, HashAlgorithmName.SHA256)` y firma con la CA
  de ese ambiente.
- [ ] **Paso 2: tests en rojo** en `ArcaCredentialServiceTests`:
  - el pedido tiene `C=AR`, `CN=<alias>` y `serialNumber=CUIT <cuit>`, y su firma verifica;
  - la clave protegida no contiene la clave en claro y no se descifra con otro AAD (otra conexión u otro certificado);
  - un certificado emitido para ese pedido se acepta y devuelve fechas y huella;
  - uno emitido para otro pedido da `CertificateKeyMismatch`;
  - uno con otra CUIT da `CertificateCuitMismatch`;
  - uno de la CA del otro ambiente da `CertificateEnvironmentMismatch`;
  - uno vencido da `CertificateExpired`;
  - un texto que no es PEM da `CertificateUnreadable`.
- [ ] **Paso 3: contrato.**

```csharp
public interface IArcaCredentialService
{
    ArcaCertificateRequest CreateRequest(Guid connectionId, Guid certificateId, string cuit, string alias);
    Result<InspectedCertificate> Inspect(Guid connectionId, Guid certificateId, string protectedPrivateKey, string certificatePem,
        string cuit, ArcaEnvironment environment, DateTime nowUtc);
    string SignLoginRequest(Guid connectionId, Guid certificateId, string protectedPrivateKey, string certificatePem, byte[] loginTicketRequest);
    string ProtectTicket(Guid certificateId, ArcaService service, string token, string sign);
    (string Token, string Sign)? UnprotectTicket(Guid certificateId, ArcaService service, string protectedCredentials);
}

public sealed record ArcaCertificateRequest(string RequestPem, string ProtectedPrivateKey)
{
    public override string ToString() => "ArcaCertificateRequest { privateKey: [REDACTED] }";
}
```

- [ ] **Paso 4: implementación.** El sobre cifrado es el de `AesGcmAiSecretProtector`, con este AAD:
  `ArquitecturaBase.Arca|1|{installation}|{keyId}|{purpose}|{connectionId}|{certificateId}`. Los propósitos son
  `certificate-key` y `access-ticket:{service}`. El pedido:

```csharp
using var rsa = RSA.Create(2048);
var subject = new X500DistinguishedNameBuilder();
subject.AddCountryOrRegion("AR");
subject.AddOrganizationName(alias);
subject.AddCommonName(alias);
subject.Add("2.5.4.5", $"CUIT {cuit}", UniversalTagNumber.PrintableString); // serialNumber
var request = new CertificateRequest(subject.Build(), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
var requestPem = request.CreateSigningRequestPem();
var pkcs8 = rsa.ExportPkcs8PrivateKey();
try { return new ArcaCertificateRequest(requestPem, Protect("certificate-key", connectionId, certificateId, pkcs8)); }
finally { CryptographicOperations.ZeroMemory(pkcs8); }
```

  Para inspeccionar, `X509Certificate2.CreateFromPem(certificatePem)`, comparando `ExportSubjectPublicKeyInfo()` del
  certificado con el de la clave descifrada. El `serialNumber` del sujeto se lee con
  `X500DistinguishedName.EnumerateRelativeDistinguishedNames()` buscando el OID `2.5.4.5`, y el emisor se compara por su
  CN con `ArcaOptions.<ambiente>.IssuerCommonName`. Para firmar, la clave se importa con `ImportPkcs8PrivateKey` y se ata
  con `certificate.CopyWithPrivateKey(rsa)`; los bytes en claro se borran con `CryptographicOperations.ZeroMemory`.
- [ ] **Paso 5: alias.** Entre 3 y 30 caracteres, solo minúsculas, dígitos y guiones, empezando por letra
  (`^[a-z][a-z0-9-]{2,29}$`). Se valida en el servicio con `AliasInvalid`. Si ARCA admite otros caracteres, se amplía
  con la homologación.
- [ ] **Paso 6: correr** `ArcaCredentialServiceTests`.
- [ ] **Paso 7: commit.** `feat(arca): clave, pedido y certificado con el anillo del módulo`

---

### Tarea 5: WSAA y el ticket compartido

**Archivos:**
- Crear: `src/ArquitecturaBase.Infrastructure/Modules/Arca/Security/ArcaLoginTicketRequest.cs`, `ArcaCmsSigner.cs` (los usa `ArcaCredentialService.SignLoginRequest`)
- Crear: `src/ArquitecturaBase.Infrastructure/Modules/Arca/Soap/SoapEnvelope.cs`, `ArcaAuthClient.cs`
- Crear: `src/ArquitecturaBase.Application/Modules/Arca/Interfaces/Integrations/IArcaAuthClient.cs`, `Models/ArcaLoginResult.cs`, `Models/ArcaCertificateRef.cs`, `Services/ArcaAccessTicketProvider.cs`, `Models/ArcaTicketOutcome.cs`
- Modificar: `Directory.Packages.props` y `src/ArquitecturaBase.Infrastructure/ArquitecturaBase.Infrastructure.csproj` (`System.Security.Cryptography.Pkcs`)
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/Arca/FakeArca.cs`, y en `tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca/`, `ArcaLoginTicketRequestTests.cs` y `ArcaAccessTicketProviderTests.cs`

- [ ] **Paso 1: tests en rojo.**
  - **`ArcaLoginTicketRequestTests`:** con `FakeTimeProvider` en `2026-11-03T13:00:00Z`, el pedido tiene `version="1.0"`,
    `generationTime` `2026-11-03T09:50:00-03:00`, `expirationTime` `2026-11-03T10:10:00-03:00`, `service` `wsfe` y un
    `uniqueId` distinto en dos pedidos seguidos. La firma CMS verifica con `SignedCms.Decode` y `CheckSignature(true)`, y
    su contenido es el pedido.
  - **`ArcaAccessTicketProviderTests`, con PostgreSQL y `FakeArca`:**
    - el primer pedido llama una vez a `LoginCms` y guarda el ticket cifrado;
    - el segundo, con margen, no llama;
    - **dos hosts:** dos scopes en paralelo, cada uno con su `DbContext` y su transacción como dos hosts sobre la misma
      base, piden a la vez, llaman a `LoginCms` una sola vez y usan el mismo token;
    - `coe.alreadyAuthenticated` deja `BlockedUntilUtc` en ahora más la ventana del ambiente y devuelve `Blocked` con ese
      plazo;
    - `coe.notAuthorized` devuelve `NotAuthorized`;
    - un 503 o un timeout devuelve `Unavailable` y suelta la reserva.
- [ ] **Paso 2: el pedido de ticket.**

```csharp
internal static class ArcaLoginTicketRequest
{
    private static readonly TimeSpan Argentina = TimeSpan.FromHours(-3);

    public static byte[] Build(string service, DateTimeOffset nowUtc)
    {
        static string Format(DateTimeOffset value) => value.ToOffset(Argentina).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        var document = new XElement("loginTicketRequest", new XAttribute("version", "1.0"),
            new XElement("header",
                new XElement("uniqueId", (uint)(nowUtc.ToUnixTimeMilliseconds() % uint.MaxValue)),
                new XElement("generationTime", Format(nowUtc.AddMinutes(-10))),
                new XElement("expirationTime", Format(nowUtc.AddMinutes(10)))),
            new XElement("service", service));
        return Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + document.ToString(SaveOptions.DisableFormatting));
    }
}

internal static class ArcaCmsSigner
{
    public static string Sign(byte[] content, X509Certificate2 certificateWithKey)
    {
        var cms = new SignedCms(new ContentInfo(content), detached: false);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificateWithKey)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), // SHA-256
            IncludeOption = X509IncludeOption.EndCertOnly,
        };
        cms.ComputeSignature(signer);
        return Convert.ToBase64String(cms.Encode());
    }
}
```

- [ ] **Paso 3: `LoginCms`.** Es un POST con `Content-Type: text/xml; charset=utf-8` y `SOAPAction: ""`:

```xml
<soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:wsaa="http://wsaa.view.sua.dvadac.desein.afip.gov">
  <soapenv:Header/>
  <soapenv:Body><wsaa:loginCms><wsaa:in0>{cms en base64}</wsaa:in0></wsaa:loginCms></soapenv:Body>
</soapenv:Envelope>
```

  La respuesta trae `loginCmsReturn` con el XML `loginTicketResponse` escapado: `credentials/token`, `credentials/sign` y
  `header/expirationTime`. Un `soapenv:Fault` trae `faultcode`; los conocidos se mapean así:
  - `coe.alreadyAuthenticated` → `Blocked`;
  - `coe.notAuthorized` y `cms.cert.untrusted` → `NotAuthorized`;
  - `cms.bad` y lo que hable del reloj (`GenerationTime`, `expiración`) → `ClockSkew`;
  - lo demás → `Unavailable`.

  `SoapEnvelope` lee la respuesta con el tope de `MaxResponseBytes`, la parsea con `XmlReaderSettings { DtdProcessing = Prohibit, XmlResolver = null }`
  y nunca deja la respuesta en un log.
- [ ] **Paso 4: `ArcaAccessTicketProvider`.** No usa HTTP dentro de una transacción ([locks](../architecture/backend.md#locks)):

```csharp
public async Task<ArcaTicketOutcome> GetAsync(ArcaCertificateRef certificate, ArcaService service, CancellationToken cancellationToken)
{
    var owner = Guid.NewGuid();
    for (var attempt = 0; attempt < 20; attempt++)
    {
        var lease = ArcaTicketLease.Busy;
        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await repository.LockTicketAsync(certificate.Id, service, token);
            var ticket = await repository.GetTicketAsync(certificate.Id, service, token)
                ?? throw new InvalidOperationException("Every certificate has one ticket row per service.");
            lease = ticket.TryLease(owner, time.GetUtcNow().UtcDateTime, options.TicketMargin, options.TicketLease);
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);

        switch (lease)
        {
            case ArcaTicketLease.AlreadyValid: return await ReadStoredAsync(certificate, service, cancellationToken);
            case ArcaTicketLease.Blocked: return await ReadBlockedAsync(certificate, service, cancellationToken);
            case ArcaTicketLease.Busy: await Task.Delay(TimeSpan.FromMilliseconds(500), time, cancellationToken); continue;
        }

        var login = await auth.LoginAsync(certificate, service, cancellationToken); // fuera de la transacción
        return await SaveAsync(certificate, service, owner, login, cancellationToken); // Store, Block o suelta la reserva bajo el lock, con el fence del dueño
    }
    return ArcaTicketOutcome.Unavailable();
}
```

  `ArcaTicketOutcome` es un record cerrado (`Ready(token, sign)`, `Blocked(untilUtc)`, `NotAuthorized`, `ClockSkew` y
  `Unavailable`). No es un `Error`: es el resultado que la prueba muestra por servicio. `ArcaCertificateRef`, en `Models`,
  es lo que el proveedor y los clientes necesitan del certificado activo:

```csharp
public sealed record ArcaCertificateRef(Guid ConnectionId, Guid Id, string Cuit, ArcaEnvironment Environment,
    string ProtectedPrivateKey, string CertificatePem)
{
    public override string ToString() => $"ArcaCertificateRef {{ id: {Id}, privateKey: [REDACTED] }}";
}
```

  `IArcaAuthClient.LoginAsync(ArcaCertificateRef certificate, ArcaService service, CancellationToken cancellationToken)`
  arma el pedido, lo firma con `IArcaCredentialService.SignLoginRequest` y devuelve `ArcaLoginResult` (token, firma y
  vencimiento, o el motivo de la falla).
- [ ] **Paso 5: `FakeArca`.** Es un `HttpMessageHandler` que enruta por la URL y el `SOAPAction`, responde los sobres de
  esta tarea y de la siguiente, cuenta las llamadas por operación y deja programar fallas por servicio: `alreadyAuthenticated`,
  `notAuthorized`, 503 y demora.
- [ ] **Paso 6: correr** los dos tests de la tarea.
- [ ] **Paso 7: commit.** `feat(arca): ticket de acceso compartido entre hosts con reserva`

---

### Tarea 6: WSFEv1 y padrón

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Modules/Arca/Interfaces/Integrations/IArcaInvoicingClient.cs`, `IArcaRegistryClient.cs`, `Models/ArcaPointOfSale.cs`, `Models/ArcaRegistryPerson.cs`, `Services/ArcaCompanyDataPolicy.cs`
- Crear: `src/ArquitecturaBase.Infrastructure/Modules/Arca/Soap/ArcaInvoicingClient.cs`, `ArcaRegistryClient.cs`
- Crear: `tests/ArquitecturaBase.Application.UnitTests/Modules/Arca/ArcaCompanyDataPolicyTests.cs`, `tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca/ArcaSoapClientsTests.cs`

- [ ] **Paso 0: confirmar en los manuales,** antes del código:
  - en el [manual de WSFEv1](https://www.arca.gob.ar/ws/documentacion/manuales/manual-desarrollador-ARCA-COMPG.pdf), los
    campos de `FEParamGetPtosVenta`;
  - en el manual 3.4 de la constancia de inscripción, la dirección de homologación, los nombres de los elementos de
    `getPersona_v2` y cómo se deduce la condición frente al IVA;
  - los CN de los emisores de cada ambiente.

  Lo que difiera de este plan se corrige acá y en el [diseño](2026-11-03-arca-etapa-1-conexion.md#a-confirmar-al-programar).
- [ ] **Paso 1: tests en rojo.**
  - **`ArcaSoapClientsTests`, contra `FakeArca`:**
    - `FEDummy` con `AppServer`, `DbServer` y `AuthServer` en `OK` es `true`, y con alguno distinto es `false`;
    - `FEParamGetPtosVenta` devuelve número, tipo de emisión, bloqueado (`S`/`N`) y baja (`NULL` o fecha);
    - el error 602 (sin datos) es una lista vacía;
    - `getPersona_v2` devuelve razón social o nombre y apellido, domicilio fiscal, impuestos y si es monotributo;
    - un `errorConstancia` se devuelve como resultado, sin excepción.
  - **`ArcaCompanyDataPolicyTests`:**
    - con datos de monotributo → `Monotax`;
    - con el impuesto 30 (IVA) → `RegisteredTaxpayer`;
    - con el 32 (IVA exento) → `Exempt`;
    - sin ninguno → `NotRegistered`;
    - el domicilio se arma como «dirección, localidad, provincia».
- [ ] **Paso 2: WSFEv1.** `FEDummy` va sin `Auth`. `FEParamGetPtosVenta` lleva `Auth` con `Token`, `Sign` y `Cuit`:

```xml
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:ar="http://ar.gov.afip.dif.FEV1/">
  <soap:Body>
    <ar:FEParamGetPtosVenta>
      <ar:Auth><ar:Token>{token}</ar:Token><ar:Sign>{sign}</ar:Sign><ar:Cuit>{cuit}</ar:Cuit></ar:Auth>
    </ar:FEParamGetPtosVenta>
  </soap:Body>
</soap:Envelope>
```

  El `SOAPAction` es `http://ar.gov.afip.dif.FEV1/<Operación>`. Los errores 600 y 601 se devuelven como `NotAuthorized`.
- [ ] **Paso 3: padrón.** Es `getPersona_v2` en el namespace `http://a5.soap.ws.server.puc.sr/`, con `token`, `sign`,
  `cuitRepresentada` (la CUIT de la conexión) e `idPersona` (la misma CUIT). Va sin `SOAPAction` y los elementos de
  entrada van sin calificar.
- [ ] **Paso 4: correr** los dos tests.
- [ ] **Paso 5: commit.** `feat(arca): clientes de WSFEv1 y del padrón`

---

### Tarea 7: servicios de aplicación

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Modules/Arca/Interfaces/Services/IArcaConnectionService.cs`, `IArcaConnectionTestService.cs`, `Services/ArcaConnectionService.cs`, `Services/ArcaConnectionTestService.cs`, `Models/ArcaConnectionResponse.cs`, `Models/ArcaTestResponse.cs`, `Models/ArcaServiceCheck.cs`, `Models/ArcaPointOfSaleRow.cs`
- Crear: `tests/ArquitecturaBase.Application.UnitTests/Modules/Arca/ArcaConnectionServiceTests.cs`, `TestFeatures/Modules/Arca/*` (fakes de repositorio, credenciales y clientes)

Referencias: `AiConnectionService`, `AiConnectionTestService` y `RoleService.UpdateAsync`, para la forma de guardar con
`ExecuteInTransactionAsync(…, CommitPolicy.OnSuccess, …)`. El límite de dependencias por servicio es 8
(`ServiceDependencyLimitTests`): por eso la prueba va en un servicio aparte.

- [ ] **Paso 1: tests en rojo** en `ArcaConnectionServiceTests`:
  - **empezar** con CUIT y alias válidos crea la conexión, el certificado pendiente y sus dos tickets vacíos;
  - **empezar con la conexión ya activa** crea solo el pendiente de la renovación;
  - **la revisión vieja** da `RevisionConflict`;
  - **subir un certificado inválido** devuelve el error del inspector y no cambia nada;
  - **subir uno válido en el alta** lo deja activo y la conexión `Unverified`;
  - **subir uno válido en la renovación** lo deja guardado como pendiente cargado, sin reemplazar al activo;
  - **la prueba de una renovación** en verde promueve el nuevo y borra el viejo con sus tickets; en rojo, el activo sigue;
  - **cancelar la renovación** borra el pendiente;
  - **la prueba en verde** guarda los datos de la empresa y pasa a `Active` si había punto de venta;
  - **la prueba en rojo** de una conexión activa pasa a `NeedsAttention`;
  - **la prueba no pisa:** si el certificado probado dejó de ser el activo mientras corría, el resultado no se guarda;
  - **elegir un punto de venta** que ARCA ya no devuelve da `PointOfSaleUnavailable`;
  - **desconectar** borra todo.
- [ ] **Paso 2: contratos.**

```csharp
public interface IArcaConnectionService
{
    Task<Result<ArcaConnectionResponse>> GetAsync(CancellationToken cancellationToken);
    Task<Result<ArcaConnectionResponse>> StartCertificateRequestAsync(string cuit, ArcaEnvironment environment, string alias, long expectedRevision, CancellationToken cancellationToken);
    Task<Result<string>> GetPendingRequestPemAsync(CancellationToken cancellationToken);
    Task<Result<ArcaConnectionResponse>> CancelRenewalAsync(long expectedRevision, CancellationToken cancellationToken);
    Task<Result<ArcaConnectionResponse>> UploadCertificateAsync(string certificatePem, long expectedRevision, CancellationToken cancellationToken);
    Task<Result<ArcaConnectionResponse>> ChoosePointOfSaleAsync(int number, long expectedRevision, CancellationToken cancellationToken);
    Task<Result> DisconnectAsync(long expectedRevision, CancellationToken cancellationToken);
}

public interface IArcaConnectionTestService
{
    Task<Result<ArcaTestResponse>> TestAsync(CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<ArcaPointOfSaleRow>>> ListPointsOfSaleAsync(CancellationToken cancellationToken);
}
```

  `ArcaTestResponse` lleva un `ArcaServiceCheck` por servicio (`Service`, `Outcome`, `TicketExpiresAtUtc?` y
  `RetryAfterUtc?`), los datos de la empresa y la conexión actualizada. Subir el certificado en el alta encadena la prueba
  desde el controller, como dice el diseño.
- [ ] **Paso 3: implementación.** Cada escritura es un solo `ExecuteInTransactionAsync` con `LockConnectionAsync` adentro.
  La prueba hace sus llamadas a ARCA fuera de la transacción, y después guarda en otra transacción corta, comparando el
  certificado activo. Los logs van con `[LoggerMessage]` y la CUIT con `Cuit.Mask`.
- [ ] **Paso 4: correr** `ArcaConnectionServiceTests`, `ApplicationServicesTests`, `ServiceDependencyLimitTests` y
  `TransactionBoundaryTests`.
- [ ] **Paso 5: commit.** `feat(arca): alta, prueba, renovación y punto de venta`

---

### Tarea 8: contrato HTTP

**Archivos:**
- Crear: `src/ArquitecturaBase.Api/Modules/Arca/Controllers/ArcaConnectionController.cs`, `Contracts/*.cs`, `ArcaRateLimitOptions.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca/ArcaTestModule.cs` (`Routes`)
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca/ArcaConnectionEndpointsTests.cs`

Referencias: `AiConnectionController`, `UpdateAiConnectionHttpRequest` (con su `ToString`) y `AiRateLimitOptions`.

- [ ] **Paso 1: tests en rojo** en `ArcaConnectionEndpointsTests`, con `FakeArca` y `FakeArcaCertificateAuthority`:
  - **permisos:** 401 sin sesión, y 403 sin `arca.read` o, en las escrituras, sin `arca.manage`;
  - **sin conexión,** `GET` devuelve `status: null` y revisión 0;
  - **el alta completa:** empezar, descargar el `.csr` (`application/pkcs10`, con el nombre `arca-<alias>.csr`), emitirlo con
    la CA de prueba, subirlo (prueba sola), listar los puntos de venta, elegir el 5 y ver `Active`;
  - **una revisión vieja** da 409 con ProblemDetails y `code` `Arca.Connection.RevisionConflict`;
  - **un servicio sin autorizar** devuelve 200 con ese servicio en `NotAuthorized` y la conexión en `NeedsAttention`;
  - **la renovación sin corte:** mientras hay un pendiente, la prueba sigue usando el activo;
  - **desconectar** deja `GET` como sin conexión;
  - **ninguna respuesta** contiene `PRIVATE KEY`, el token ni la firma.
- [ ] **Paso 2: rutas** (MVC, `[Route("api/arca/connection")]`):

| Verbo y ruta | Permisos | Servicio |
| --- | --- | --- |
| `GET` | `arca.read` | `GetAsync` |
| `POST certificate-requests` | `arca.read` y `arca.manage` | `StartCertificateRequestAsync` |
| `GET certificate-requests/current` | `arca.read` y `arca.manage` | `GetPendingRequestPemAsync`, como archivo |
| `DELETE certificate-requests/current` | `arca.read` y `arca.manage` | `CancelRenewalAsync` (revisión en la query) |
| `PUT certificate` | `arca.read` y `arca.manage` | `UploadCertificateAsync`, y en el alta la prueba |
| `POST test` | `arca.read` y `arca.manage` | `TestAsync` |
| `GET points-of-sale` | `arca.read` y `arca.manage` | `ListPointsOfSaleAsync` |
| `PUT point-of-sale` | `arca.read` y `arca.manage` | `ChoosePointOfSaleAsync` |
| `DELETE` | `arca.read` y `arca.manage` | `DisconnectAsync` (revisión en la query) |

  - **Contratos:** el certificado viaja como PEM en JSON, con un tope de 16 KiB. `UploadArcaCertificateHttpRequest.ToString`
    redacta el certificado y `StartArcaConnectionHttpRequest.ToString` enmascara la CUIT.
  - **Rate limit:** la prueba y la lista de puntos de venta tienen un límite por operador.
  - **Inventario:** las nueve rutas van en `ArcaTestModule.Routes`.
- [ ] **Paso 3: correr** `ArcaConnectionEndpointsTests`, `ExplicitRouteInventoryTests`, `ControllerInputContractTests` y
  `PermissionAuthorizationTests`.
- [ ] **Paso 4: commit.** `feat(arca): rutas de la conexión`

---

### Tarea 9: front (AC1-A, AC2-A y AC3)

Repo `../ArquitecturaBaseFront`. Antes, leer su `AGENTS.md`, el de `src/features` y el de
`src/features/artificial-intelligence`, que es la pantalla más parecida (`AiSettingsPage`).

**Archivos:**
- Crear: `src/features/arca/AGENTS.md` y `CLAUDE.md`, `api/arcaConnection.ts`, `model.ts`, `pages/ArcaSettingsPage.tsx`, `components/ArcaSetupZones.tsx` (las cinco zonas del alta), `components/ArcaActiveSections.tsx` (empresa, servicios, certificado y punto de venta), `components/ArcaDisconnectDialog.tsx`
- Crear: `src/locales/es/arca.json` y `src/locales/en/arca.json`
- Modificar: `src/app/routes.tsx` (`/configuracion/arca` con `ProtectedRoute permission="arca.read"`), `src/layouts/navigation.ts` (entrada `navigation.arca` en «Configuración» con `permission: "arca.read"`), `src/locales/{es,en}/common.json`
- Crear: `pages/ArcaSettingsPage.test.tsx`, `model.test.ts` y las escenas visuales de `src/test/visual`

- [ ] **Paso 1: tests en rojo** (Vitest con MSW): cada estado de AC3 tiene su test.
  - **Sin conexión:** con `arca.manage` se ve «Conectar con ARCA», que abre el paso 1; sin él, «Solo lectura».
  - **Paso 1:** la CUIT con el dígito mal da el error junto al campo, sin pedido al servidor.
  - **«Generar el pedido»** pliega «Datos» con su resumen y ofrece la descarga.
  - **Subir un certificado que no corresponde** muestra el texto del `detail` junto a la carga.
  - **La prueba con un servicio sin autorizar** dice cuál y deja «Probar de nuevo».
  - **«Activar la conexión»** se habilita solo con un punto de venta elegido y la prueba en verde.
  - **La conexión activa** muestra las cuatro zonas, y «Renovar» muestra el pendiente dentro de «Certificado».
  - **Avisos de vencimiento:** treinta días antes, amarillo; vencido, rojo.
  - **Un 409** abre «Revisar» con «Leer lo actual».
  - **«Desconectar»** pide confirmación.
  - **«Probando…»:** el botón ocupado.
- [ ] **Paso 2: implementación.** Usa las piezas de `shared/ui`: `Surface` con banda, `Button`, la (i) con tooltip y
  `ConfirmDialog`. Todas las medidas van en rem ([escala del 75 %](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md)),
  sin leyendas fijas (la ayuda va en la (i)) y con los textos en voseo. Las zonas apagadas no son interactivas, y la zona
  en curso recibe el foco al abrirse.
- [ ] **Paso 3: correr** `npm run lint`, `npm run test -- src/features/arca` y `npm run test:visual` para las escenas
  nuevas.
- [ ] **Paso 4: commit en el front.** `feat(arca): pantalla de la conexión con ARCA`

---

### Cierre

- [ ] **Docs:**
  - `docs/features/arca.md` con la regla vigente;
  - `docs/guides/configurar-arca.md`, con el anillo en user-secrets, las direcciones, el alta en WSASS y en producción, y la
    renovación;
  - `docs/guides/quitar-arca.md`, como `quitar-ia.md`;
  - el `AGENTS.md` y el `CLAUDE.md` de cada carpeta nueva y el [mapa de instrucciones](../architecture/mapa-de-instrucciones.md);
  - en este plan y en la [hoja de ruta](2026-10-31-arca-formularios-estandar.md), la etapa cerrada.
- [ ] **Verificación,** según [validar un cambio](../guides/validar-un-cambio.md#verificación-y-cierre):
  - la integración con PostgreSQL;
  - `dotnet test` completo (pedir que la PC no se suspenda: tarda unos 51 minutos);
  - `dotnet build ArquitecturaBase.slnx` sin advertencias;
  - en el front: build, lint, tests y la suite visual;
  - `aspire stop` si se levantó la app.
- [ ] **Prueba de fuego de la retirada:** en una copia descartable de los dos repos, sin tocar las carpetas de trabajo,
  un agente sigue solo `quitar-arca.md` y comprueba:
  - el build sin advertencias, `RemoveArca` con solo los tres `DropTable` y `has-pending-model-changes` sin cambios;
  - `ModuleBoundaryTests`, `ErrorCodeTests` y los tests del núcleo en verde;
  - en el front, build, lint y tests;
  - sin rastros: `git grep -nE '\bArca|\barca\b|\bARCA\b' -- src tests` en el backend y `-- src` en el front no
    devuelven nada (con `-i` aparecería «marca»).

  Lo que encuentre se corrige en la guía, como se hizo con WhatsApp.
- [ ] **Homologación real con el usuario:**
  - el usuario crea en WSASS el certificado con el `.csr` que da la app y autoriza `wsfe` y `ws_sr_constancia_inscripcion`;
  - después se prueban la conexión, los datos de la empresa y los puntos de venta contra ARCA;
  - el resultado va al diseño de la etapa.
- [ ] **Traspaso:** se ofrece el traspaso al multitenant.

## Autorrevisión

- **Cobertura del diseño:**
  - recorrido AC1-A → tareas 7, 8 y 9; conexión activa AC2-A y estados AC3 → tarea 9;
  - módulo y permisos → tarea 1; datos → tareas 2 y 3; ticket → tarea 5; clientes → tareas 5 y 6;
  - contrato HTTP → tarea 8; errores → tarea 2; robustez y privacidad → tareas 4, 5 y 8; pruebas → cada tarea y el
    cierre; «A confirmar al programar» → paso 0 de la tarea 6.
- **Fuera de este plan,** como fija el diseño: emitir, las tablas de ARCA como catálogos, el autocompletar del cliente y
  los modos de delegación.
