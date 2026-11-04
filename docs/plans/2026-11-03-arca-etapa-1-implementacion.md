# ARCA · Etapa 1: plan de implementación

> **Para quien lo ejecute:** se trabaja tarea por tarea, con TDD (el test en rojo primero) y en `main`, sin worktrees
> ([AGENTS.md](../../AGENTS.md)). Es el modo liviano de las etapas: nueve tareas, una revisión por tarea y la suite completa
> solo al cerrar. Las casillas (`- [ ]`) se marcan al terminar cada paso. Lo que sigue un patrón existente lo nombra con su
> archivo de referencia; el código nuevo de verdad (CUIT, CSR, firma CMS, sobres SOAP, reserva del ticket y la interfaz de
> las familias) va completo.

**Objetivo:** conectar una instalación con ARCA usando un certificado nuevo de la empresa, elegir los servicios que usa,
probarlos, traer los datos de la empresa desde el padrón y elegir el punto de venta, sin emitir comprobantes.

**Arquitectura:** es el módulo opcional `Arca` ([ADR 0023](../decisions/0023-arca-como-modulo-opcional.md)), ordenado por
familias de servicios ([ADR 0025](../decisions/0025-familias-de-servicios-arca.md)). La etapa crea tres familias:
- `Connection`: conexión, certificados, tickets, anillo y el sobre SOAP;
- `Registry`: el padrón y los datos de la empresa;
- `Invoicing`: `FEDummy` y el punto de venta.

Cada capa tiene una carpeta por familia. Una familia usa solo `Connection`, y `Connection` conoce a las demás por
`IArcaServiceDefinition`. Copia la forma del módulo de IA en el anillo propio, la conexión única con revisión y 409, la
prueba manual y el módulo de tests.

**Tecnología:** .NET 10, EF Core con PostgreSQL, `System.Security.Cryptography` (`CertificateRequest`, `SignedCms` de
`System.Security.Cryptography.Pkcs`), `HttpClient` con `System.Xml.Linq`, xUnit v3 y Testcontainers. En el front, React,
TanStack Query, Vitest con MSW y Playwright.

**Fuentes:** [diseño de la etapa](2026-11-03-arca-etapa-1-conexion.md), tableros AC1-A, AC2-A y AC3 del lienzo
[Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), y la sección A.2 del
[estudio de integraciones](integraciones/estudio-2026-10-22.md) (WSAA, WSFEv1 y errores).

---

## Árbol

Lo marcado con ✎ es lo único que se toca fuera de las carpetas del módulo.

```
src/ArquitecturaBase.Domain/Modules/Arca/
├─ AGENTS.md · CLAUDE.md
├─ ArcaPermissions.cs
├─ ArcaConnectionErrors.cs · ArcaInvoicingErrors.cs        los errores van en la raíz (regla del repo)
├─ Connection/   Cuit · ArcaServiceCode · ArcaAccessMode · ArcaEnvironment · ArcaConnectionStatus
│                ArcaConnection · ArcaCertificate · ArcaCertificateRole · ArcaAccessTicket · ArcaTicketLease
├─ Registry/     ArcaRegistryCompany · ArcaVatCondition
└─ Invoicing/    ArcaInvoicingSettings

src/ArquitecturaBase.Application/Modules/Arca/
├─ AGENTS.md · CLAUDE.md
├─ ArcaApplicationRegistration.cs                          una línea por familia
├─ Channels/            ArcaPermissionCatalogSource
│  ├─ Registry/         ArcaRegistryServiceDefinition
│  └─ Invoicing/        ArcaInvoicingServiceDefinition
├─ Resources/Permissions.resx · Permissions.en.resx
├─ Interfaces/
│  ├─ Channels/Connection/     IArcaServiceDefinition
│  ├─ Services/Connection/     IArcaConnectionService · IArcaConnectionTestService · IArcaAccessTicketStore
│  ├─ Services/Registry/       IArcaRegistryCompanyService
│  ├─ Services/Invoicing/      IArcaInvoicingSetupService
│  ├─ Persistence/Connection/  IArcaConnectionRepository · IArcaConnectionReader
│  ├─ Persistence/Registry/    IArcaRegistryRepository · IArcaRegistryReader
│  ├─ Persistence/Invoicing/   IArcaInvoicingRepository · IArcaInvoicingReader
│  ├─ Integrations/Connection/ IArcaCredentialService · IArcaAccessTicketProvider
│  ├─ Integrations/Registry/   IArcaRegistryClient
│  └─ Integrations/Invoicing/  IArcaInvoicingClient
├─ Models/Connection/   ArcaConnectionResponse · ArcaConnectionRow · ArcaTicketRow · ArcaTestResponse
│                       ArcaServiceCheckResponse · ArcaServiceContext · ArcaCertificateRequest · ArcaInspectedCertificate
│                       ArcaCertificateRef · ArcaTicketOutcome · ArcaTicketLeaseResponse · ArcaInstalledService
├─ Models/Registry/     ArcaRegistryPerson · ArcaRegistryCompanyRow · ArcaCompanyResponse
├─ Models/Invoicing/    ArcaPointOfSale · ArcaPointOfSaleRow · ArcaInvoicingSettingsRow · ArcaInvoicingSettingsResponse
└─ Services/
   ├─ Connection/  ArcaConnectionService · ArcaConnectionTestService · ArcaAccessTicketStore
   ├─ Registry/    ArcaRegistryCompanyService · ArcaCompanyDataPolicy
   └─ Invoicing/   ArcaInvoicingSetupService

src/ArquitecturaBase.Infrastructure/Modules/Arca/
├─ AGENTS.md · CLAUDE.md
├─ ArcaInfrastructureRegistration.cs · ArcaOptions.cs
├─ Connection/   Security: ArcaSecretProtectionOptions · ArcaKeyRing · ArcaCredentialService
│                          ArcaLoginTicketRequest · ArcaCmsSigner
│                Soap: SoapEnvelope · ArcaAuthClient
│                ArcaAccessTicketProvider
├─ Registry/     ArcaRegistryClient
├─ Invoicing/    ArcaInvoicingClient
└─ Persistence/  ArcaLockKeys.cs
   ├─ Configurations/{Connection,Registry,Invoicing}/
   ├─ Repositories/{Connection,Registry,Invoicing}/
   └─ Readers/{Connection,Registry,Invoicing}/

src/ArquitecturaBase.Api/Modules/Arca/
├─ AGENTS.md · CLAUDE.md
├─ ArcaApiRegistration.cs · ArcaRateLimitOptions.cs
├─ Controllers/  ArcaConnectionController · ArcaRegistryController · ArcaInvoicingController
└─ Contracts/{Connection,Invoicing}/

✎ src/ArquitecturaBase.Api/Program.cs                 3 using marcados «// módulo ARCA» y el bloque
✎ src/ArquitecturaBase.Api/appsettings.json           sección "Arca"
✎ src/ArquitecturaBase.Application/Resources/Errors(.en).resx   claves Arca.*
✎ src/ArquitecturaBase.Infrastructure/Persistence/Migrations/   AddArcaConnection
✎ Directory.Packages.props · Infrastructure.csproj   System.Security.Cryptography.Pkcs

tests/
├─ ArquitecturaBase.Domain.UnitTests/Modules/Arca/{Connection,Registry,Invoicing}/
├─ ArquitecturaBase.Application.UnitTests/Modules/Arca/{Connection,Registry,Invoicing}/
├─ ArquitecturaBase.Application.UnitTests/TestFeatures/Modules/Arca/
├─ ArquitecturaBase.ArchitectureTests/Modules/Arca/   ArcaTransactionContribution · ArcaFamilyBoundaryTests
├─ ArquitecturaBase.Api.IntegrationTests/Modules/Arca/{Connection,Registry,Invoicing}/ · ArcaTestModule.cs
└─ ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/Arca/   ArcaPersistenceScenario · FakeArca · FakeArcaCertificateAuthority

ArquitecturaBaseFront/src/
├─ features/arca/
│  ├─ AGENTS.md · CLAUDE.md
│  ├─ pages/ArcaSettingsPage.tsx                     arma las zonas de las familias instaladas
│  ├─ connection/   api · model · ConnectionZones (datos, pedido, certificado, prueba, servicios, certificado activo)
│  ├─ registry/     api · CompanySection
│  └─ invoicing/    api · PointOfSaleZone
├─ locales/{es,en}/arca.json
✎ app/routes.tsx · layouts/navigation.ts · locales/{es,en}/common.json · test/visual/…
```

## Desacople

ARCA se tiene que poder quitar entero, como WhatsApp y la IA ([ADR 0007](../decisions/0007-whatsapp-como-modulo-opcional.md),
[módulos opcionales](../architecture/backend.md#módulos-opcionales)), y también una familia sola
([ADR 0025](../decisions/0025-familias-de-servicios-arca.md)). El núcleo no nombra las carpetas del módulo
(`ModuleBoundaryTests`), y una familia no nombra a otra (`ArcaFamilyBoundaryTests`).

**Quitar el módulo entero.** Se borran sus carpetas y se deshacen los lugares marcados con ✎:

| Lugar | Qué hay | Al quitar el módulo |
| --- | --- | --- |
| `Program.cs` | Tres `using` marcados `// módulo ARCA` y el bloque | Se borran |
| `appsettings.json` | La sección `Arca` | Se borra |
| User-secrets o variables del host | `Arca:SecretProtection:*` | Se guarda una copia segura para los backups y se borra |
| `Errors.resx` y `.en.resx` | Las claves `Arca.*` | Se borran; si no, `ErrorCodeTests` falla y las nombra |
| `Directory.Packages.props` e `Infrastructure.csproj` | `System.Security.Cryptography.Pkcs` | Se quita del `.csproj`, y de `Directory.Packages.props` si nadie más lo usa |
| Migraciones | `AddArcaConnection`, con las entidades nombradas por cadenas | Se conserva y se genera `RemoveArca`, que borra las tablas del módulo |
| Front | La ruta, la entrada del menú, su texto en `common.json` y las escenas visuales | Se borran |
| Docs | `features/arca.md`, `guides/configurar-arca.md` y sus enlaces | Se borran; los ADR y el historial quedan |

**Quitar una familia** (por ejemplo, `Invoicing`):
- se borran sus carpetas `Invoicing` en cada capa, en los tests y en `features/arca/invoicing`;
- se borran su clase `ArcaInvoicingErrors` y sus claves `Arca.Invoicing.*`;
- se saca su línea de cada registro y su ruta del inventario;
- se genera la migración que borra sus tablas (`ArcaInvoicingSettings`);
- las conexiones que la tenían elegida la pierden sola: un servicio que ya no está instalado no se prueba.

**Para las etapas que siguen,** la misma regla:
- El motor de formularios (BusinessForms) es otro módulo y no puede nombrar a ARCA. La emisión (etapa 3) entra por un
  puerto neutral del núcleo en `Application/Interfaces/Channels`, con su versión apagada, como el canal de avisos que
  implementa WhatsApp.
- El estándar «Factura» y los catálogos de ARCA los aporta la familia `Invoicing` por puertos del núcleo, así que se van
  con ella.
- Una familia nueva (granos, hacienda) crea sus carpetas, implementa `IArcaServiceDefinition` y suma su línea a cada
  registro, sin tocar `Connection`.
- Lo genérico del motor (renglones, cálculos, condiciones, resultado externo y carpetas) se queda: no nombra a ARCA.

---

### Tarea 1: esqueleto del módulo, permisos, anillo y frontera de familias

**Hecha el 2026-11-04.** Diferencias con lo escrito:
- `ArcaApiRegistration` llega con la tarea 8: el módulo todavía no tiene tipos en Api, y `ModuleBoundaryTests` pide
  un registro solo en las capas con tipos. Por eso `Program.cs` tiene, por ahora, dos `using` marcados.
- Los permisos se prueban en `Application.UnitTests/Modules/Arca/ArcaPermissionCatalogSourceTests`, con su paridad
  de recursos. Las opciones y la composición, en `Api.IntegrationTests/Modules/Arca/ArcaOptionsTests` y
  `ArcaCompositionTests`.
- `ArcaTestModule` deja los clientes sin red (un handler que lanza), como el de IA; `FakeArca` llega con la tarea 5.
- La guía [configurar ARCA](../guides/configurar-arca.md) empieza ahora con el anillo, porque la Api ya no arranca
  sin él. El resto de la guía llega al cierre.
- `EXTEXP0001` se suprime en `.editorconfig` solo para `ArcaInfrastructureRegistration.cs`, como en IA y WhatsApp.

**Archivos:**
- Crear: `src/ArquitecturaBase.Domain/Modules/Arca/ArcaPermissions.cs`
- Crear: `src/ArquitecturaBase.Application/Modules/Arca/ArcaApplicationRegistration.cs`, `Channels/ArcaPermissionCatalogSource.cs`, `Resources/Permissions.resx` y `Permissions.en.resx`
- Crear: `src/ArquitecturaBase.Infrastructure/Modules/Arca/ArcaInfrastructureRegistration.cs`, `ArcaOptions.cs`, `Connection/Security/ArcaSecretProtectionOptions.cs`, `Connection/Security/ArcaKeyRing.cs`
- Crear: `src/ArquitecturaBase.Api/Modules/Arca/ArcaApiRegistration.cs`
- Modificar: `src/ArquitecturaBase.Api/Program.cs` y `src/ArquitecturaBase.Api/appsettings.json`
- Crear: `tests/ArquitecturaBase.ArchitectureTests/Modules/Arca/ArcaFamilyBoundaryTests.cs`
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca/ArcaTestModule.cs`, `Connection/ArcaOptionsTests.cs`, `Connection/ArcaPermissionsTests.cs`

Referencias: `ArtificialIntelligencePermissions`, `AiPermissionCatalogSource`, `AiSecretProtectionOptions`, `AiKeyRing`,
`ArtificialIntelligenceInfrastructureRegistration` (líneas 24 a 27), `AiTestModule` y `ModuleBoundaryTests`.

- [x] **Paso 1: tests en rojo.**
  - **`ArcaPermissionsTests`:** el catálogo compuesto de permisos incluye `arca.read` y `arca.manage` con su área y sus
    textos en español y en inglés.
  - **`ArcaOptionsTests`:** la app no arranca sin `Arca:SecretProtection` válido, y arranca con el del `ArcaTestModule`.
  - **`ArcaFamilyBoundaryTests`:** lee los tipos del módulo en los cuatro ensamblados y el fuente:
    - un tipo de `…Modules.Arca.<capa>.<Familia>` no referencia tipos de otra familia;
    - `Connection` no referencia ninguna familia;
    - la raíz del módulo (registros, permisos, errores, opciones) puede nombrar a todas.

    La familia es el segmento que sigue a la carpeta de capa (`Services.Invoicing`, `Persistence.Repositories.Registry`,
    `Contracts.Connection`) o, en Domain e Infrastructure, el primero después de `Modules.Arca`. Un caso de control con un
    tipo inventado que cruza familias tiene que fallar.
- [x] **Paso 2: correr y ver que fallan.**
  `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Connection.ArcaPermissionsTests"`
- [x] **Paso 3: permisos.**

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

- [x] **Paso 4: anillo y opciones.** `ArcaSecretProtectionOptions` copia `AiSecretProtectionOptions` (instalación, clave
  activa, claves de 256 bits, `IsValid()` y el `ToString` redactado). `ArcaKeyRing` copia `AiKeyRing` sin las derivaciones
  de huellas. Las direcciones van por familia y por ambiente:

```csharp
namespace ArquitecturaBase.Infrastructure.Modules.Arca;

public sealed record ArcaOptions
{
    public ArcaEnvironmentOptions Testing { get; init; } = new();
    public ArcaEnvironmentOptions Production { get; init; } = new();
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public int MaxResponseBytes { get; init; } = 512 * 1024;
    public TimeSpan TicketMargin { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan TicketLease { get; init; } = TimeSpan.FromSeconds(60);

    public bool IsValid() => Testing.IsValid() && Production.IsValid() && RequestTimeout > TimeSpan.Zero && MaxResponseBytes > 0;
}

public sealed record ArcaEnvironmentOptions
{
    public Uri? Wsaa { get; init; }
    /// La dirección de cada servicio por su código WSN: "wsfe", "ws_sr_constancia_inscripcion", y los de cada familia nueva.
    public Dictionary<string, Uri> Services { get; init; } = new(StringComparer.Ordinal);
    /// El CN del emisor de los certificados de este ambiente; sirve para saber si un .crt es de homologación o de producción.
    public string IssuerCommonName { get; init; } = string.Empty;
    /// Lo que ARCA tarda en aceptar otro pedido de ticket para el mismo servicio (10 min en homologación, 2 en producción).
    public TimeSpan TicketRetryWindow { get; init; }

    public bool IsValid() => Wsaa is { Scheme: "https" } && Services.Values.All(u => u.Scheme == "https")
        && !string.IsNullOrWhiteSpace(IssuerCommonName) && TicketRetryWindow > TimeSpan.Zero;
}
```

  Los valores por defecto van en la sección `Arca` de `appsettings.json`. Las direcciones son las del estudio:
  - WSAA: `https://wsaahomo.afip.gov.ar/ws/services/LoginCms` y `https://wsaa.afip.gov.ar/ws/services/LoginCms`;
  - `wsfe`: `https://wswhomo.afip.gov.ar/wsfev1/service.asmx` y `https://servicios1.afip.gov.ar/wsfev1/service.asmx`;
  - `ws_sr_constancia_inscripcion`: `https://awshomo.afip.gov.ar/sr-padron/webservices/personaServiceA5` y
    `https://aws.afip.gov.ar/sr-padron/webservices/personaServiceA5`.

  Los emisores y la dirección de homologación del padrón se confirman en la Tarea 6. El anillo nunca va en appsettings:
  va en user-secrets o en variables de entorno.

- [x] **Paso 5: registros.** `AddArcaApplication()`, `AddArcaInfrastructure(IConfiguration)` y `AddArcaApi()` tienen la
  forma de los de IA, con un método privado por familia, así quitar una es borrar una línea:

```csharp
public static IServiceCollection AddArcaInfrastructure(this IServiceCollection services, IConfiguration configuration)
{
    services.AddArcaConnectionInfrastructure(configuration); // anillo, opciones, WSAA, tickets, persistencia base
    services.AddArcaRegistryInfrastructure();                 // cliente del padrón y sus tablas
    services.AddArcaInvoicingInfrastructure();                // cliente de WSFEv1 y sus tablas
    return services;
}
```

  - Las opciones se registran con `Bind`, `Validate` y `ValidateOnStart`, y el `IPermissionCatalogSource` con
    `TryAddEnumerable`.
  - Hay un `HttpClient` con nombre para el WSAA y otro por familia (`arca-wsaa`, `arca-registry`, `arca-invoicing`), sin
    redirecciones (`AllowAutoRedirect = false`), con el `Timeout` de las opciones y los loggers del `HttpClient` apagados,
    como en IA.

  En `Program.cs` van los tres `using` marcados `// módulo ARCA`, como los de WhatsApp, y el bloque debajo del de IA:

```csharp
// Módulo ARCA (ADR 0023). Para quitarlo: este bloque, los tres using marcados y docs/guides/quitar-arca.md.
builder.Services
    .AddArcaApplication()
    .AddArcaInfrastructure(builder.Configuration)
    .AddArcaApi();
```

- [x] **Paso 6: `ArcaTestModule`.** Pone `Arca:SecretProtection` con `ApiFactory.TestHashKey` y rate limits altos, y deja
  los `HttpClient` del módulo apuntando a `FakeArca`, que en esta tarea responde siempre 503. `Routes` arranca vacío.
- [x] **Paso 7: correr** los tests de la tarea y todo `ArchitectureTests`, que tiene que pasar sin cambios en el núcleo.
  `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj`
- [x] **Paso 8: commit.** `feat(arca): módulo opcional con permisos, anillo propio y frontera de familias`

---

### Tarea 2: dominio

**Hecha el 2026-11-04, con la tarea 3 en el mismo commit:** `EntityConfigurationTests` pide la configuración de EF de
cada entidad en el mismo cambio. Diferencias con lo escrito:
- `Cuit` se llama `CuitNumber`, porque `ArcaConnection` ya tiene una propiedad `Cuit`.
- No hay `NotTested`: ningún caso de la etapa lo devuelve.
- `ArcaConnection` suma `ReevaluateFamilies`, para cuando una familia completa o pierde su configuración, y
  `EnabledServices` es un `string[]` con setter privado, como `PublishedCultures` de formularios, para mapearlo a `text[]`.
- `ArcaAccessTicket` suma `Release`, para soltar la reserva sin guardar nada cuando ARCA no responde.

**Archivos:**
- Crear en `src/ArquitecturaBase.Domain/Modules/Arca/`:
  - `Connection/`: `Cuit.cs`, `ArcaServiceCode.cs`, los enums, `ArcaConnection.cs`, `ArcaCertificate.cs`, `ArcaAccessTicket.cs` y `ArcaTicketLease.cs`;
  - `Registry/`: `ArcaRegistryCompany.cs` y `ArcaVatCondition.cs`;
  - `Invoicing/`: `ArcaInvoicingSettings.cs`;
  - en la raíz: `ArcaConnectionErrors.cs` y `ArcaInvoicingErrors.cs`.
- Modificar: `src/ArquitecturaBase.Application/Resources/Errors.resx` y `Errors.en.resx`
- Crear: `tests/ArquitecturaBase.Domain.UnitTests/Modules/Arca/Connection/CuitTests.cs`, `ArcaServiceCodeTests.cs`, `ArcaConnectionTests.cs`, `ArcaAccessTicketTests.cs`, y `Invoicing/ArcaInvoicingSettingsTests.cs`

- [x] **Paso 1: tests en rojo.**

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

  - **`ArcaServiceCodeTests`:** `wsfe`, `ws_sr_constancia_inscripcion` y `wscpe` son válidos; vacío, con mayúsculas, con
    espacios o de más de 40 caracteres, no.
  - **`ArcaConnectionTests`**, con `FakeTimeProvider`:
    - empezar con una CUIT inválida da `ArcaConnectionErrors.CuitInvalid`;
    - empezar con una válida y los servicios elegidos deja `AwaitingCertificate`, modo `CompanyCertificate` y revisión 1;
    - elegir un servicio que no está entre los instalados da `ServiceNotInstalled`, y uno obligatorio siempre queda elegido;
    - cargar el certificado pasa a `Unverified`;
    - `RecordTest(passed: true, familiesComplete: false)` deja `Unverified`;
    - `RecordTest(passed: true, familiesComplete: true)` pasa a `Active`;
    - con la conexión activa, una prueba fallida o una familia incompleta pasan a `NeedsAttention`, y una prueba en verde
      con todo completo la vuelve a `Active`;
    - cada cambio sube la revisión, y `ToString` no muestra la CUIT completa.
  - **`ArcaAccessTicketTests`:**
    - un ticket vigente con margen se reutiliza (`AlreadyValid`);
    - sin ticket ni reserva, la reserva se toma (`Acquired`);
    - con la reserva de otro sin vencer da `Busy`, y vencida se toma;
    - con `BlockedUntilUtc` en el futuro da `Blocked`;
    - guardar o bloquear con otro dueño de reserva no cambia nada y devuelve `false`.
  - **`ArcaInvoicingSettingsTests`:** el punto de venta va de 1 a 99998, y la configuración está completa solo con un punto
    de venta elegido.
- [x] **Paso 2: correr y ver que fallan.**
  `dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj -- --filter-class "ArquitecturaBase.Domain.UnitTests.Modules.Arca.Connection.CuitTests"`
- [x] **Paso 3: CUIT y código de servicio.** Un dígito verificador 10 no es válido: ARCA nunca lo asigna.

```csharp
namespace ArquitecturaBase.Domain.Modules.Arca.Connection;

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

/// El código de un servicio de ARCA en el WSAA (WSN): "wsfe", "ws_sr_constancia_inscripcion", "wscpe"…
public readonly record struct ArcaServiceCode
{
    private ArcaServiceCode(string value) => Value = value;
    public string Value { get; }

    public static ArcaServiceCode? Create(string? value) =>
        value is { Length: >= 2 and <= 40 } && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')
            ? new ArcaServiceCode(value) : null;

    public override string ToString() => Value;
}
```

- [x] **Paso 4: enums y entidades.**
  - **Enums de `Connection/`:**
    - `ArcaAccessMode { CompanyCertificate, PlatformDelegation, PlatformDelegationAutomated }`;
    - `ArcaEnvironment { Testing, Production }`;
    - `ArcaConnectionStatus { AwaitingCertificate, Unverified, Active, NeedsAttention }`;
    - `ArcaCertificateRole { Active, Pending }`.
  - **`ArcaConnection : Entity, IAuditable`**, con `Scope = 1` como `AiConnection` y `EnabledServices` (lista de códigos
    guardada como arreglo de texto). Sus métodos:
    - `static Result<ArcaConnection> Start(string cuit, ArcaEnvironment environment, IReadOnlyCollection<ArcaServiceCode> chosen, IReadOnlyCollection<ArcaServiceCode> installed, IReadOnlyCollection<ArcaServiceCode> required)`;
    - `Result ChooseServices(...)`, con la misma validación;
    - `void CertificateLoaded()`;
    - `void RecordTest(bool passed, bool familiesComplete, DateTime testedAtUtc)`;
    - `void CertificateExpired()`.
  - **`ArcaCertificate : Entity`** lleva `ConnectionId`, `Role`, `Alias`, `RequestPem`, `ProtectedPrivateKey`,
    `CertificatePem?`, `NotBeforeUtc?`, `NotAfterUtc?` y `Thumbprint?`, con `Load(...)` y `Promote()` para la renovación.
  - **`ArcaAccessTicket : Entity`** lleva `CertificateId`, `ServiceCode`, `ProtectedCredentials?`, `ExpiresAtUtc?`,
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

  - **`Registry/`:** `ArcaRegistryCompany : Entity` (conexión, razón social, condición frente al IVA y domicilio fiscal,
    con `Update(...)`) y `ArcaVatCondition { RegisteredTaxpayer, Monotax, Exempt, NotRegistered }`.
  - **`Invoicing/`:** `ArcaInvoicingSettings : Entity` (conexión y punto de venta), con `Result ChoosePointOfSale(int number)`
    e `IsComplete`.
- [x] **Paso 5: errores.** Los códigos son `Arca.Connection.<Motivo>` y `Arca.Invoicing.<Motivo>`, con su texto en
  `Errors.resx` (voseo) y en `Errors.en.resx`.
  - **`ArcaConnectionErrors`:**
    - de validación: `CuitInvalid`, `AliasInvalid`, `ServiceNotInstalled`, `CertificateUnreadable`,
      `CertificateKeyMismatch`, `CertificateCuitMismatch`, `CertificateEnvironmentMismatch`, `CertificateExpired`,
      `NoPendingRequest` y `NotTested`;
    - `RevisionConflict` (conflicto) y `NotConnected` (no encontrado).
  - **`ArcaInvoicingErrors`:** `PointOfSaleInvalid` y `PointOfSaleUnavailable` (validación).
- [x] **Paso 6: correr** los tests de la tarea, `ErrorDeclarationTests`, `ResourceParityTests` y `ArcaFamilyBoundaryTests`.
- [x] **Paso 7: commit.** `feat(arca): dominio de la conexión, el padrón y la facturación`

---

### Tarea 3: persistencia

**Hecha el 2026-11-04.** Diferencias con lo escrito:
- Las fotos de los lectores terminan en `Row`, como pide `ReaderOutputNamingTests`: `ArcaConnectionRow` (con
  `ArcaCertificateRow`, sin clave ni pedido), `ArcaTicketRow`, `ArcaRegistryCompanyRow` y `ArcaInvoicingSettingsRow`.
- `ArcaTicketRow` lleva las credenciales cifradas, como `AiConnectionRow` lleva la clave cifrada: el proveedor de tickets
  de la tarea 5 las necesita. Su `ToString` las oculta. La clave privada del certificado no sale por ningún lector: la
  tarea 5 arma `ArcaCertificateRef` desde el repositorio, o con un método de lectura propio.
- Los repositorios de `Registry` e `Invoicing` no tienen lock propio: quien escribe toma el de la conexión con
  `IArcaConnectionRepository.LockConnectionAsync`, porque una familia puede usar `Connection`.
- `PersistenceNamingTests` y `PersistenceRegistrationTests` comparaban el namespace exacto y no veían las subcarpetas
  por familia. Ahora revisan también las subcarpetas, con casos de control. `ControllerServiceRepositoryTests` tiene la
  misma comparación para `Interfaces/Services`, pero falla a la vista: se amplía en la tarea 8.
- La migración se corrigió a mano en un punto: `CK_BusinessFormCatalogs_Proposal` es un texto de varias líneas, y con
  `core.autocrlf` EF lo volcó con `
` en el snapshot. Se volvió a `
`, como en `HEAD`, para
  `MigrationSqlPortabilityTests`.

**Archivos:**
- Crear en `src/ArquitecturaBase.Infrastructure/Modules/Arca/Persistence/`: `ArcaLockKeys.cs`, y en `Configurations/`, `Repositories/` y `Readers/` una carpeta `Connection`, `Registry` e `Invoicing` con lo suyo
- Crear en `src/ArquitecturaBase.Application/Modules/Arca/Interfaces/Persistence/{Connection,Registry,Invoicing}/` los contratos, y en `Models/<Familia>/` sus filas
- Crear: la migración `AddArcaConnection` (según [migración](../guides/migracion.md))
- Crear: `tests/ArquitecturaBase.ArchitectureTests/Modules/Arca/ArcaTransactionContribution.cs`, y en `tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca/`, `Connection/ArcaConnectionPersistenceTests.cs`, `Registry/ArcaRegistryPersistenceTests.cs` e `Invoicing/ArcaInvoicingPersistenceTests.cs`

Referencias: `IAiConnectionRepository`, `IAiConnectionReader`, sus implementaciones y configuraciones, `AiAdvisoryLockKeys`,
`AiTransactionContribution`, y [nombres de repositorios](../decisions/0008-nombres-de-repositorios-y-lectores.md).

- [x] **Paso 1: tests en rojo,** con PostgreSQL:
  - un segundo `ArcaConnection` con `Scope = 1` choca con el índice único;
  - un certificado activo y uno pendiente conviven, y un segundo pendiente choca (índice único por conexión y papel);
  - hay un ticket por certificado y código de servicio;
  - borrar la conexión borra en cascada certificados, tickets, `ArcaRegistryCompanies` y `ArcaInvoicingSettings`;
  - los lectores devuelven fotos sin seguimiento, sin la clave ni las credenciales del ticket.
- [x] **Paso 2: contratos de `Connection`.**

```csharp
public interface IArcaConnectionRepository
{
    Task LockConnectionAsync(CancellationToken cancellationToken);
    Task LockTicketAsync(Guid certificateId, ArcaServiceCode service, CancellationToken cancellationToken);
    Task<ArcaConnection?> GetConnectionAsync(CancellationToken cancellationToken);
    Task<ArcaCertificate?> GetCertificateAsync(Guid connectionId, ArcaCertificateRole role, CancellationToken cancellationToken);
    Task<ArcaAccessTicket?> GetTicketAsync(Guid certificateId, ArcaServiceCode service, CancellationToken cancellationToken);
    void Add(ArcaConnection connection);
    void Add(ArcaCertificate certificate);
    void Add(ArcaAccessTicket ticket);
    void Remove(ArcaConnection connection);
    void Remove(ArcaCertificate certificate);
}

public interface IArcaConnectionReader
{
    Task<ArcaConnectionRow?> FindAsync(CancellationToken cancellationToken);
    Task<ArcaTicketRow?> FindTicketAsync(Guid certificateId, ArcaServiceCode service, CancellationToken cancellationToken);
}
```

  `Registry` e `Invoicing` tienen cada una su repositorio (`Get…Async(Guid connectionId)` y `Add`) y su lector
  (`Find…Async`). No nombran tipos de `Connection` más allá del `Guid` de la conexión.
- [x] **Paso 3: locks.** En `ArcaLockKeys` van `arca-connection:installation` y `arca-ticket:{certificateId}:{service}`. Las
  escrituras de `Registry` e `Invoicing` toman el de la conexión. `ArcaTransactionContribution` declara `LockOwners`, que
  apunta a `ArcaLockKeys`, y `LockPrefixes` `["arca-connection:", "arca-ticket:"]`.
- [x] **Paso 4: configuraciones EF.**
  - tablas `ArcaConnections`, `ArcaCertificates`, `ArcaAccessTickets`, `ArcaRegistryCompanies` y `ArcaInvoicingSettings`,
    con `ToTable` explícito como en los módulos;
  - enums como texto y `EnabledServices` como `text[]`;
  - `Revision` como token de concurrencia, como en IA;
  - claves foráneas a `ArcaConnections` con cascada.
- [x] **Paso 5: migración.** Se genera con la cadena de mentira del pipeline, con los parámetros exactos de
  [migración](../guides/migracion.md):
  `dotnet ef migrations add AddArcaConnection --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api`
- [x] **Paso 6: correr** los tests de la tarea, `MigrationsTests`, `TransactionBoundaryTests` y `ArcaFamilyBoundaryTests`.
- [x] **Paso 7: commit.** `feat(arca): persistencia de la conexión, el padrón y la facturación`

---

### Tarea 4: credenciales (clave, pedido y certificado)

**Hecha el 2026-11-04.** Diferencias con lo escrito:
- `SignLoginRequest` llega con la tarea 5, junto con `ArcaCmsSigner` y el paquete `System.Security.Cryptography.Pkcs`
  que necesita: sin eso quedaba un método de la interfaz sin implementar.
- `ProtectTicket` y `UnprotectTicket` reciben también la conexión, así el AAD tiene la misma forma para la clave y para
  el ticket.
- El modelo se llama `ArcaInspectedCertificate`, con el prefijo de los demás modelos del módulo.
- El alias ya lo valida `ArcaCertificate.IsValidAlias` desde la tarea 2. El servicio de la tarea 7 lo revisa antes de
  pedir la clave, y `CreateRequest` lo recibe validado.
- Si la clave guardada no se descifra con el anillo, `Inspect` lanza: no es un error de la persona sino del anillo.
- Los tests no usan `ApiFactory` ni Docker, como `AiSecretProtectionTests`: arman el servicio con un anillo inventado.

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Modules/Arca/Interfaces/Integrations/Connection/IArcaCredentialService.cs`, `Models/Connection/ArcaCertificateRequest.cs` e `InspectedCertificate.cs`
- Crear: `src/ArquitecturaBase.Infrastructure/Modules/Arca/Connection/Security/ArcaCredentialService.cs`
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca/Connection/ArcaCredentialServiceTests.cs` y `tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/Arca/FakeArcaCertificateAuthority.cs`

- [x] **Paso 1: la autoridad de prueba.** `FakeArcaCertificateAuthority` crea una CA autofirmada por ambiente, con el CN
  configurado en `ArcaTestModule` (`Computadores Test` y `Computadores`). `Issue(string requestPem, ArcaEnvironment environment, DateTime notAfterUtc)`
  carga el pedido con `CertificateRequest.LoadSigningRequestPem(requestPem, HashAlgorithmName.SHA256)` y firma con la CA
  de ese ambiente.
- [x] **Paso 2: tests en rojo:**
  - el pedido tiene `C=AR`, `CN=<alias>` y `serialNumber=CUIT <cuit>`, y su firma verifica;
  - la clave protegida no contiene la clave en claro y no se descifra con otro AAD (otra conexión u otro certificado);
  - un certificado emitido para ese pedido se acepta y devuelve fechas y huella;
  - uno emitido para otro pedido da `CertificateKeyMismatch`;
  - uno con otra CUIT da `CertificateCuitMismatch`;
  - uno de la CA del otro ambiente da `CertificateEnvironmentMismatch`;
  - uno vencido da `CertificateExpired`;
  - un texto que no es PEM da `CertificateUnreadable`.
- [x] **Paso 3: contrato.**

```csharp
public interface IArcaCredentialService
{
    ArcaCertificateRequest CreateRequest(Guid connectionId, Guid certificateId, string cuit, string alias);
    Result<InspectedCertificate> Inspect(Guid connectionId, Guid certificateId, string protectedPrivateKey, string certificatePem,
        string cuit, ArcaEnvironment environment, DateTime nowUtc);
    string SignLoginRequest(Guid connectionId, Guid certificateId, string protectedPrivateKey, string certificatePem, byte[] loginTicketRequest);
    string ProtectTicket(Guid certificateId, ArcaServiceCode service, string token, string sign);
    (string Token, string Sign)? UnprotectTicket(Guid certificateId, ArcaServiceCode service, string protectedCredentials);
}

public sealed record ArcaCertificateRequest(string RequestPem, string ProtectedPrivateKey)
{
    public override string ToString() => "ArcaCertificateRequest { privateKey: [REDACTED] }";
}
```

- [x] **Paso 4: implementación.** El sobre cifrado es el de `AesGcmAiSecretProtector`, con este AAD:
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

  - **Inspeccionar:** `X509Certificate2.CreateFromPem(certificatePem)`, comparando `ExportSubjectPublicKeyInfo()` del
    certificado con el de la clave descifrada. El `serialNumber` del sujeto se lee con
    `X500DistinguishedName.EnumerateRelativeDistinguishedNames()` buscando el OID `2.5.4.5`, y el emisor se compara por su
    CN con `IssuerCommonName` del ambiente.
  - **Firmar:** la clave se importa con `ImportPkcs8PrivateKey` y se ata con `certificate.CopyWithPrivateKey(rsa)`.
  - **Bytes en claro:** se borran con `CryptographicOperations.ZeroMemory`.
- [x] **Paso 5: alias.** Entre 3 y 30 caracteres, solo minúsculas, dígitos y guiones, empezando por letra
  (`^[a-z][a-z0-9-]{2,29}$`). Se valida en el servicio con `AliasInvalid`. Si ARCA admite otros caracteres, se amplía
  con la homologación.
- [x] **Paso 6: correr** `ArcaCredentialServiceTests`.
- [x] **Paso 7: commit.** `feat(arca): clave, pedido y certificado con el anillo del módulo`

---

### Tarea 5: WSAA, el ticket compartido y la interfaz de las familias

**Hecha el 2026-11-04.** Diferencias con lo escrito:
- **El proveedor de tickets está en Infrastructure** (`Connection/ArcaAccessTicketProvider`), como el coordinador del
  asistente de IA. `TransactionBoundaryTests` permite un límite por método público, y el ticket necesita dos (reservar y
  guardar) con el pedido al WSAA en el medio. Cada paso es un método de `IArcaAccessTicketStore` (un servicio de
  `Connection` con una transacción por método) y corre en un scope propio. Así tampoco se lee un ticket seguido viejo:
  la unidad de trabajo no vacía el seguimiento después de confirmar.
- **No hay `IArcaAuthClient` ni `ArcaLoginResult`.** `ArcaAuthClient` es interno de Infrastructure y devuelve
  `ArcaTicketOutcome`; calcula el bloqueo con la ventana del ambiente, que está en `ArcaOptions`.
- **`IArcaServiceDefinition` va en `Interfaces/Channels/Connection`,** como puerto «todos avisados», y los adaptadores
  de cada familia en `Channels/<Familia>`. `DependencyInjectionTests` pide un único registro scoped por interfaz de
  `Interfaces/Services`, y aquí hay uno por familia. Un adaptador no abre transacciones: lo que guarde una familia lo guarda su
  servicio (tarea 6). El resultado se llama `ArcaServiceCheckResponse` y lleva el código del servicio como texto.
- El `uniqueId` del pedido es al azar: con la hora, dos pedidos en el mismo milisegundo chocaban.
- `cms.bad` no se toma como reloj corrido: da `Unavailable`. El reloj se reconoce por `generationTime` o
  `expirationTime` en el código o en el texto de la falla.
- Quien encuentra la reserva tomada espera de a medio segundo con el `TimeProvider`, como mucho lo que dura una reserva.
- El paquete `System.Security.Cryptography.Pkcs` ya estaba en Infrastructure. La prueba de la firma está en
  `ArcaCredentialServiceTests`.

**Archivos:**
- Crear: `src/ArquitecturaBase.Infrastructure/Modules/Arca/Connection/Security/ArcaLoginTicketRequest.cs` y `ArcaCmsSigner.cs` (los usa `ArcaCredentialService.SignLoginRequest`)
- Crear: `src/ArquitecturaBase.Infrastructure/Modules/Arca/Connection/Soap/SoapEnvelope.cs` y `ArcaAuthClient.cs`
- Crear en `src/ArquitecturaBase.Application/Modules/Arca/`:
  - `Interfaces/Integrations/Connection/`: `IArcaAuthClient.cs` e `IArcaAccessTicketProvider.cs`;
  - `Interfaces/Services/Connection/IArcaServiceDefinition.cs`;
  - `Models/Connection/`: `ArcaLoginResult.cs`, `ArcaCertificateRef.cs`, `ArcaTicketOutcome.cs`, `ArcaServiceContext.cs` y `ArcaServiceCheck.cs`;
  - `Services/Connection/ArcaAccessTicketProvider.cs`.
- Modificar: `Directory.Packages.props` y `src/ArquitecturaBase.Infrastructure/ArquitecturaBase.Infrastructure.csproj` (`System.Security.Cryptography.Pkcs`)
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/Arca/FakeArca.cs`, y en `tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca/Connection/`, `ArcaLoginTicketRequestTests.cs` y `ArcaAccessTicketProviderTests.cs`

- [x] **Paso 1: tests en rojo.**
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
- [x] **Paso 2: el pedido de ticket.**

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

- [x] **Paso 3: `LoginCms`.** Es un POST con `Content-Type: text/xml; charset=utf-8` y `SOAPAction: ""`:

```xml
<soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:wsaa="http://wsaa.view.sua.dvadac.desein.afip.gov">
  <soapenv:Header/>
  <soapenv:Body><wsaa:loginCms><wsaa:in0>{cms en base64}</wsaa:in0></wsaa:loginCms></soapenv:Body>
</soapenv:Envelope>
```

  La respuesta trae `loginCmsReturn` con el XML `loginTicketResponse` escapado: `credentials/token`, `credentials/sign` y
  `header/expirationTime`. Un `soapenv:Fault` trae `faultcode`, y los conocidos se mapean así:
  - `coe.alreadyAuthenticated` → `Blocked`;
  - `coe.notAuthorized` y `cms.cert.untrusted` → `NotAuthorized`;
  - `cms.bad` y lo que hable del reloj (`GenerationTime`, `expiración`) → `ClockSkew`;
  - lo demás → `Unavailable`.

  `SoapEnvelope` es la pieza común que usan las familias para sus servicios. Arma el sobre, manda el POST, lee la
  respuesta con el tope de `MaxResponseBytes`, la parsea con `XmlReaderSettings { DtdProcessing = Prohibit, XmlResolver = null }`
  y nunca la deja en un log.
- [x] **Paso 4: el proveedor de tickets.** No usa HTTP dentro de una transacción ([locks](../architecture/backend.md#locks)):

```csharp
public async Task<ArcaTicketOutcome> GetAsync(ArcaCertificateRef certificate, ArcaServiceCode service, CancellationToken cancellationToken)
{
    var owner = Guid.NewGuid();
    for (var attempt = 0; attempt < 20; attempt++)
    {
        var lease = ArcaTicketLease.Busy;
        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await repository.LockTicketAsync(certificate.Id, service, token);
            var ticket = await repository.GetTicketAsync(certificate.Id, service, token);
            if (ticket is null) { ticket = ArcaAccessTicket.Create(certificate.Id, service); repository.Add(ticket); }
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
  `Unavailable`). No es un `Error`: es el resultado que la prueba muestra por servicio. `ArcaCertificateRef` es lo que el
  proveedor y las familias necesitan del certificado activo:

```csharp
public sealed record ArcaCertificateRef(Guid ConnectionId, Guid Id, string Cuit, ArcaEnvironment Environment,
    string ProtectedPrivateKey, string CertificatePem)
{
    public override string ToString() => $"ArcaCertificateRef {{ id: {Id}, privateKey: [REDACTED] }}";
}
```

  `IArcaAuthClient.LoginAsync(ArcaCertificateRef certificate, ArcaServiceCode service, CancellationToken cancellationToken)`
  arma el pedido, lo firma con `IArcaCredentialService.SignLoginRequest` y devuelve `ArcaLoginResult` (token, firma y
  vencimiento, o el motivo de la falla).
- [x] **Paso 5: la interfaz de las familias.** Vive en `Connection` y la implementa cada familia:

```csharp
namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;

/// Una familia de servicios de ARCA (padrón, facturación, granos…) presentada a la conexión, que no la nombra.
public interface IArcaServiceDefinition
{
    /// Los servicios de ARCA de la familia, con su código WSN y la clave de su nombre en los recursos del front.
    IReadOnlyCollection<ArcaInstalledService> Services { get; }

    /// Prueba un servicio elegido con el ticket que le da el contexto, y guarda lo que la familia necesite (el padrón,
    /// los datos de la empresa). Nunca lanza por una respuesta de ARCA: la devuelve en el resultado.
    Task<ArcaServiceCheck> CheckAsync(ArcaServiceCode service, ArcaServiceContext context, CancellationToken cancellationToken);

    /// Si la configuración propia de la familia está completa (Facturación: un punto de venta elegido).
    Task<bool> IsCompleteAsync(Guid connectionId, CancellationToken cancellationToken);
}

public sealed record ArcaInstalledService(ArcaServiceCode Code, string Family, bool Required);

public sealed record ArcaServiceContext(ArcaCertificateRef Certificate, IArcaAccessTicketProvider Tickets);
```

  `ArcaServiceCheck` lleva el código, el resultado (`Ready`, `NotAuthorized`, `Blocked`, `ClockSkew`, `Unavailable` o
  `Failed` con un motivo estable), `TicketExpiresAtUtc?` y `RetryAfterUtc?`. Cada familia lo registra con
  `services.TryAddEnumerable(ServiceDescriptor.Scoped<IArcaServiceDefinition, …>())`.
- [x] **Paso 6: `FakeArca`.** Es un `HttpMessageHandler` que enruta por la URL y el `SOAPAction`, responde los sobres de
  esta tarea y de la siguiente, cuenta las llamadas por operación y deja programar fallas por servicio: `alreadyAuthenticated`,
  `notAuthorized`, 503 y demora.
- [x] **Paso 7: correr** los dos tests de la tarea y `ArcaFamilyBoundaryTests`.
- [x] **Paso 8: commit.** `feat(arca): ticket de acceso compartido e interfaz de las familias`

---

### Tarea 6: las familias `Registry` e `Invoicing`

**Hecha el 2026-11-04.** Diferencias con lo escrito:
- El paso 0 se confirmó con los WSDL de homologación. WSFEv1 tiene `PtoVenta` con `Nro`, `EmisionTipo`, `Bloqueado` y
  `FchBaja`, los errores en `Errors/Err/Code`, y el `SOAPAction` con el nombre de la operación. El padrón es
  `getPersona_v2` en `http://a5.soap.ws.server.puc.sr/`, con entrada y respuesta sin calificar, el `SOAPAction` vacío,
  y `errorConstancia`, `errorRegimenGeneral` y `errorMonotributo` separados. La fecha de `FchBaja` se lee como
  `aaaammdd`, como las demás fechas de WSFEv1. Los CN de los emisores siguen a confirmar en la homologación.
- Los clientes devuelven `ArcaServiceReply<T>`: el valor, o un estado de `ArcaServiceCheckStatus` y un motivo estable
  (`wsfe.<código>`, `wsfe.fault` o `registry.fault`), nunca una excepción. `ArcaServiceCheckResponse` arma el resultado
  de la prueba desde el ticket o desde esa respuesta.
- Las definiciones van en `Channels/<Familia>` (tarea 5). El padrón guarda con `IArcaRegistryCompanyService`, en su
  transacción y con el lock de la conexión. Una CUIT sin nombre da `registry.withoutData` y no guarda nada.
- Un `FEDummy` con alguna parte distinta de «OK» deja Facturación en `Unavailable`.
- El nombre de una persona humana es «nombre apellido». Se juntan los errores de los tres grupos del padrón.
- Además de los clientes y la política, se prueban las dos definiciones con PostgreSQL y FakeArca.

**Archivos:**
- **`Registry`:**
  - `src/ArquitecturaBase.Application/Modules/Arca/Interfaces/Integrations/Registry/IArcaRegistryClient.cs`;
  - `Models/Registry/ArcaRegistryPerson.cs`;
  - `Channels/Registry/ArcaRegistryServiceDefinition.cs`;
  - `Interfaces/Services/Registry/IArcaRegistryCompanyService.cs`, y en `Services/Registry/`,
    `ArcaRegistryCompanyService.cs` y `ArcaCompanyDataPolicy.cs`;
  - `src/ArquitecturaBase.Infrastructure/Modules/Arca/Registry/ArcaRegistryClient.cs`.
- **`Invoicing`:**
  - `src/ArquitecturaBase.Application/Modules/Arca/Interfaces/Integrations/Invoicing/IArcaInvoicingClient.cs`;
  - `Models/Invoicing/ArcaPointOfSale.cs`;
  - `Channels/Invoicing/ArcaInvoicingServiceDefinition.cs`;
  - `src/ArquitecturaBase.Infrastructure/Modules/Arca/Invoicing/ArcaInvoicingClient.cs`.
- **Tests:** `tests/ArquitecturaBase.Application.UnitTests/Modules/Arca/Registry/ArcaCompanyDataPolicyTests.cs`, y en
  `tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca/`, `Registry/ArcaRegistryClientTests.cs` e
  `Invoicing/ArcaInvoicingClientTests.cs`.

- [x] **Paso 0: confirmar en los manuales,** antes del código:
  - en el [manual de WSFEv1](https://www.arca.gob.ar/ws/documentacion/manuales/manual-desarrollador-ARCA-COMPG.pdf), los
    campos de `FEParamGetPtosVenta`;
  - en el manual 3.4 de la constancia de inscripción, la dirección de homologación, los nombres de los elementos de
    `getPersona_v2` y cómo se deduce la condición frente al IVA;
  - los CN de los emisores de cada ambiente.

  Lo que difiera de este plan se corrige acá y en el [diseño](2026-11-03-arca-etapa-1-conexion.md#a-confirmar-al-programar).
- [x] **Paso 1: tests en rojo.**
  - **`ArcaInvoicingClientTests`, contra `FakeArca`:**
    - `FEDummy` con `AppServer`, `DbServer` y `AuthServer` en `OK` es `true`, y con alguno distinto es `false`;
    - `FEParamGetPtosVenta` devuelve número, tipo de emisión, bloqueado (`S`/`N`) y baja (`NULL` o fecha);
    - el error 602 (sin datos) es una lista vacía.
  - **`ArcaRegistryClientTests`, contra `FakeArca`:**
    - `getPersona_v2` devuelve razón social o nombre y apellido, domicilio fiscal, impuestos y si es monotributo;
    - un `errorConstancia` se devuelve como resultado, sin excepción.
  - **`ArcaCompanyDataPolicyTests`:**
    - con datos de monotributo → `Monotax`;
    - con el impuesto 30 (IVA) → `RegisteredTaxpayer`;
    - con el 32 (IVA exento) → `Exempt`;
    - sin ninguno → `NotRegistered`;
    - el domicilio se arma como «dirección, localidad, provincia».
- [x] **Paso 2: WSFEv1.** `FEDummy` va sin `Auth`. `FEParamGetPtosVenta` lleva `Auth` con `Token`, `Sign` y `Cuit`:

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
- [x] **Paso 3: padrón.** Es `getPersona_v2` en el namespace `http://a5.soap.ws.server.puc.sr/`, con `token`, `sign`,
  `cuitRepresentada` (la CUIT de la conexión) e `idPersona` (la misma CUIT). Va sin `SOAPAction` y los elementos de
  entrada van sin calificar.
- [x] **Paso 4: las definiciones.**
  - **`ArcaRegistryServiceDefinition`** declara `ws_sr_constancia_inscripcion` como obligatorio. Su prueba pide el ticket,
    consulta la CUIT de la conexión y guarda los datos con `IArcaRegistryCompanyService`, que arma `ArcaRegistryCompany`
    con `ArcaCompanyDataPolicy` en su propia transacción con el lock de la conexión: un adaptador de `Channels` no abre
    transacciones. `IsCompleteAsync` es siempre `true`.
  - **`ArcaInvoicingServiceDefinition`** declara `wsfe`, no obligatorio. Su prueba pide el ticket y llama a `FEDummy`.
    `IsCompleteAsync` mira si `ArcaInvoicingSettings` tiene un punto de venta.
- [x] **Paso 5: correr** los tests de la tarea y `ArcaFamilyBoundaryTests`.
- [x] **Paso 6: commit.** `feat(arca): familias del padrón y de la facturación`

---

### Tarea 7: servicios de aplicación

**Hecha el 2026-11-04.** Diferencias con lo escrito:
- **Nombres de salida:** las salidas de un servicio terminan en `Response` (`ServiceOutputNamingTests`). Por eso
  `ArcaPointOfSaleRow` es `ArcaPointOfSaleResponse`. `GetPendingRequestPemAsync` pasó a ser `GetPendingRequestAsync`
  y devuelve `ArcaPendingRequestResponse`, con el alias para el nombre del archivo.
- **Puntos de venta:** la lista deja afuera los dados de baja y marca los bloqueados, que el diseño (AC2-A) muestra
  apagados. Elegir uno bloqueado, dado de baja o que ARCA ya no devuelve da `PointOfSaleUnavailable`.
- **Empezar con una conexión que ya existe** pide otro certificado para ella, una renovación o un pedido nuevo en el
  alta, y reemplaza el pedido pendiente. La CUIT, el ambiente y los servicios son los de la conexión.
- **Errores nuevos de `Arca.Connection`:** `CertificateNotLoaded`, `ServiceNotAuthorized`, `ServiceBlocked`, `ClockSkew`,
  `Unavailable` y `ServiceRejected`, con sus textos. `ServiceBlocked` lleva `retryAfter` en segundos.
- **Lecturas nuevas:** `FindPendingRequestAsync` y `FindSigningCertificateAsync`. La segunda devuelve la clave cifrada,
  como `AiConnectionRow`, para la prueba y las familias.
- **Ayudantes en `Services/Connection`:** `ArcaServiceCatalogPolicy` (servicios instalados, obligatorios, quién prueba cada uno
  y la completitud de las familias) y `ArcaServiceSessionIssuer` (certificado activo, ticket y error de cada respuesta).
- **Dominio:** `ArcaConnection` suma `CertificateRequested` y `RequestCancelled`, que avanzan la revisión.
- **Prueba de una renovación:** en rojo no guarda nada. Si el certificado probado cambió mientras corría, da
  `RevisionConflict`.
- **Elegir servicios** sólo los guarda: la pantalla vuelve a probar.
- **Elegir el punto de venta** guarda en su transacción y después llama a `ReevaluateAsync`, que abre la suya.
- **Datos de la empresa:** los lee `IArcaRegistryCompanyService.GetAsync`.
- **Tests:** la prueba tiene su archivo, `ArcaConnectionTestServiceTests`. Los dobles y el host están en
  `TestFeatures/Modules/Arca`.

**Archivos:**
- **`Connection`:**
  - `src/ArquitecturaBase.Application/Modules/Arca/Interfaces/Services/Connection/IArcaConnectionService.cs` e `IArcaConnectionTestService.cs`;
  - `Services/Connection/ArcaConnectionService.cs` y `ArcaConnectionTestService.cs`;
  - `Models/Connection/ArcaConnectionResponse.cs` y `ArcaTestResponse.cs`.
- **`Invoicing`:** `Interfaces/Services/Invoicing/IArcaInvoicingSetupService.cs`, `Services/Invoicing/ArcaInvoicingSetupService.cs`, `Models/Invoicing/ArcaPointOfSaleRow.cs` y `ArcaInvoicingSettingsResponse.cs`
- **`Registry`:** `Models/Registry/ArcaCompanyResponse.cs`, servido por un método de lectura de `IArcaRegistryCompanyService`
- **Tests:** `tests/ArquitecturaBase.Application.UnitTests/Modules/Arca/Connection/ArcaConnectionServiceTests.cs`, `Invoicing/ArcaInvoicingSetupServiceTests.cs` y los dobles en `TestFeatures/Modules/Arca/`

Referencias: `AiConnectionService`, `AiConnectionTestService` y `RoleService.UpdateAsync`, para la forma de guardar con
`ExecuteInTransactionAsync(…, CommitPolicy.OnSuccess, …)`. El límite de dependencias por servicio es 8
(`ServiceDependencyLimitTests`).

- [x] **Paso 1: tests en rojo.**
  - **`ArcaConnectionServiceTests`:**
    - **empezar** con CUIT, alias y servicios válidos crea la conexión, con el certificado pendiente;
    - **empezar con un servicio no instalado** da `ServiceNotInstalled`;
    - **empezar con la conexión ya activa** crea solo el pendiente de la renovación;
    - **elegir servicios** guarda los nuevos; quitar uno obligatorio no se puede;
    - **la revisión vieja** da `RevisionConflict`;
    - **subir un certificado inválido** devuelve el error del inspector y no cambia nada;
    - **subir uno válido en el alta** lo deja activo y la conexión `Unverified`;
    - **subir uno válido en la renovación** lo guarda como pendiente cargado, sin reemplazar al activo;
    - **cancelar la renovación** borra el pendiente;
    - **desconectar** borra todo.
  - **`ArcaConnectionTestService`**, con dos definiciones de prueba:
    - corre `CheckAsync` solo de los servicios elegidos que siguen instalados;
    - con todo en verde y las familias completas, deja `Active`;
    - con todo en verde y una familia incompleta, deja `Unverified`; si ya estaba activa, `NeedsAttention`;
    - un servicio en rojo deja `NeedsAttention` si estaba activa;
    - la prueba de una renovación en verde promueve el nuevo certificado y borra el viejo con sus tickets; en rojo, el
      activo sigue;
    - si el certificado probado dejó de ser el activo mientras corría, el resultado no se guarda.
  - **`ArcaInvoicingSetupServiceTests`:**
    - lista los puntos de venta de web service, sin bloqueados ni dados de baja;
    - elegir uno que ARCA ya no devuelve da `PointOfSaleUnavailable`;
    - elegir uno válido lo guarda y vuelve a evaluar la conexión con `IsCompleteAsync` de todas las familias elegidas,
      sin nombrar a ninguna.
- [x] **Paso 2: contratos.**

```csharp
public interface IArcaConnectionService
{
    Task<Result<ArcaConnectionResponse>> GetAsync(CancellationToken cancellationToken);
    Task<Result<ArcaConnectionResponse>> StartCertificateRequestAsync(string cuit, ArcaEnvironment environment, string alias,
        IReadOnlyCollection<string> services, long expectedRevision, CancellationToken cancellationToken);
    Task<Result<ArcaConnectionResponse>> ChooseServicesAsync(IReadOnlyCollection<string> services, long expectedRevision, CancellationToken cancellationToken);
    Task<Result<string>> GetPendingRequestPemAsync(CancellationToken cancellationToken);
    Task<Result<ArcaConnectionResponse>> CancelRenewalAsync(long expectedRevision, CancellationToken cancellationToken);
    Task<Result<ArcaConnectionResponse>> UploadCertificateAsync(string certificatePem, long expectedRevision, CancellationToken cancellationToken);
    Task<Result> DisconnectAsync(long expectedRevision, CancellationToken cancellationToken);
}

public interface IArcaConnectionTestService
{
    Task<Result<ArcaTestResponse>> TestAsync(CancellationToken cancellationToken);
    /// Vuelve a calcular el estado con IsCompleteAsync de las familias elegidas; lo llama una familia al guardar lo suyo.
    Task<Result<ArcaConnectionResponse>> ReevaluateAsync(CancellationToken cancellationToken);
}

public interface IArcaInvoicingSetupService
{
    Task<Result<ArcaInvoicingSettingsResponse>> GetAsync(CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<ArcaPointOfSaleRow>>> ListPointsOfSaleAsync(CancellationToken cancellationToken);
    Task<Result<ArcaInvoicingSettingsResponse>> ChoosePointOfSaleAsync(int number, CancellationToken cancellationToken);
}
```

  `ArcaConnectionResponse` lleva los servicios instalados (código, familia y si es obligatorio), los elegidos, el estado,
  el ambiente, la CUIT, los certificados sin secretos, la última prueba y la revisión. `ArcaTestResponse` lleva un
  `ArcaServiceCheck` por servicio y la conexión actualizada. `Invoicing` depende de `Connection`
  (`IArcaConnectionTestService.ReevaluateAsync`, el ticket y el certificado activo); nunca al revés.
- [x] **Paso 3: implementación.**
  - Cada escritura es un solo `ExecuteInTransactionAsync` con `LockConnectionAsync` adentro.
  - La prueba hace sus llamadas a ARCA fuera de la transacción, y después guarda en otra transacción corta, comparando el
    certificado activo.
  - Los logs van con `[LoggerMessage]` y la CUIT con `Cuit.Mask`.
- [x] **Paso 4: correr** los tests de la tarea, `ApplicationServicesTests`, `ServiceDependencyLimitTests`,
  `TransactionBoundaryTests` y `ArcaFamilyBoundaryTests`.
- [x] **Paso 5: commit.** `feat(arca): alta, servicios, prueba, renovación y punto de venta`

---

### Tarea 8: contrato HTTP

**Archivos:**
- Crear en `src/ArquitecturaBase.Api/Modules/Arca/`:
  - `Controllers/`: `ArcaConnectionController.cs`, `ArcaRegistryController.cs` y `ArcaInvoicingController.cs`;
  - `Contracts/Connection/`: `StartArcaConnectionHttpRequest.cs`, `ChooseArcaServicesHttpRequest.cs`, `UploadArcaCertificateHttpRequest.cs` y `ArcaRevisionHttpRequest.cs`;
  - `Contracts/Invoicing/ChooseArcaPointOfSaleHttpRequest.cs`;
  - `ArcaRateLimitOptions.cs`.
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca/ArcaTestModule.cs` (`Routes`)
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca/Connection/ArcaConnectionEndpointsTests.cs`, `Registry/ArcaRegistryEndpointsTests.cs` e `Invoicing/ArcaInvoicingEndpointsTests.cs`

Referencias: `AiConnectionController`, `UpdateAiConnectionHttpRequest` (con su `ToString`) y `AiRateLimitOptions`.

- [ ] **Paso 1: tests en rojo,** con `FakeArca` y `FakeArcaCertificateAuthority`:
  - **permisos:** 401 sin sesión, y 403 sin `arca.read` o, en las escrituras, sin `arca.manage`;
  - **sin conexión,** `GET api/arca/connection` devuelve `status: null`, los servicios instalados y revisión 0;
  - **el alta completa:**
    - empezar con `wsfe` y el padrón;
    - descargar el `.csr` (`application/pkcs10`, con el nombre `arca-<alias>.csr`) y emitirlo con la CA de prueba;
    - subirlo (prueba sola) y leer los datos de la empresa en `GET api/arca/registry/company`;
    - listar los puntos de venta, elegir el 5 y ver `Active`;
  - **sin elegir `wsfe`,** la conexión queda activa sin punto de venta;
  - **una revisión vieja** da 409 con ProblemDetails y `code` `Arca.Connection.RevisionConflict`;
  - **un servicio sin autorizar** devuelve 200 con ese servicio en `NotAuthorized` y la conexión en `NeedsAttention`;
  - **la renovación sin corte:** mientras hay un pendiente, la prueba sigue usando el activo;
  - **desconectar** deja `GET` como sin conexión;
  - **ninguna respuesta** contiene `PRIVATE KEY`, el token ni la firma.
- [ ] **Paso 2: rutas** (MVC). Las escrituras y las llamadas a ARCA exigen `arca.read` y `arca.manage`; las lecturas,
  `arca.read`.

| Controlador | Verbo y ruta | Servicio |
| --- | --- | --- |
| `ArcaConnectionController` | `GET api/arca/connection` | `GetAsync` |
| | `POST api/arca/connection/certificate-requests` | `StartCertificateRequestAsync` |
| | `GET api/arca/connection/certificate-requests/current` | `GetPendingRequestAsync`, como archivo |
| | `DELETE api/arca/connection/certificate-requests/current` | `CancelRenewalAsync` (revisión en la query) |
| | `PUT api/arca/connection/services` | `ChooseServicesAsync` |
| | `PUT api/arca/connection/certificate` | `UploadCertificateAsync`, y en el alta la prueba |
| | `POST api/arca/connection/test` | `TestAsync` |
| | `DELETE api/arca/connection` | `DisconnectAsync` (revisión en la query) |
| `ArcaRegistryController` | `GET api/arca/registry/company` | `IArcaRegistryCompanyService.GetAsync` |
| `ArcaInvoicingController` | `GET api/arca/invoicing/settings` | `GetAsync` |
| | `GET api/arca/invoicing/points-of-sale` | `ListPointsOfSaleAsync` |
| | `PUT api/arca/invoicing/point-of-sale` | `ChoosePointOfSaleAsync` |

  - **Contratos:** el certificado viaja como PEM en JSON, con un tope de 16 KiB. `UploadArcaCertificateHttpRequest.ToString`
    redacta el certificado y `StartArcaConnectionHttpRequest.ToString` enmascara la CUIT.
  - **Rate limit:** la prueba y la lista de puntos de venta tienen un límite por operador.
  - **Inventario:** las doce rutas van en `ArcaTestModule.Routes`.
- [ ] **Paso 3: correr** los tests de la tarea, `ExplicitRouteInventoryTests`, `ControllerInputContractTests`,
  `PermissionAuthorizationTests` y `ArcaFamilyBoundaryTests`.
- [ ] **Paso 4: commit.** `feat(arca): rutas de la conexión, el padrón y la facturación`

---

### Tarea 9: front (AC1-A, AC2-A y AC3)

Repo `../ArquitecturaBaseFront`. Antes, leer su `AGENTS.md`, el de `src/features` y el de
`src/features/artificial-intelligence`, que es la pantalla más parecida (`AiSettingsPage`).

**Archivos:**
- Crear en `src/features/arca/`:
  - `AGENTS.md` y `CLAUDE.md`;
  - `pages/ArcaSettingsPage.tsx`, que arma las zonas de las familias instaladas según `GET api/arca/connection`;
  - `connection/`: `api.ts`, `model.ts`, `SetupZones.tsx` (datos con «Servicios que usa», pedido, certificado y prueba), `ActiveSections.tsx` (servicios con «Lo usa esta empresa» y certificado) y `DisconnectDialog.tsx`;
  - `registry/`: `api.ts` y `CompanySection.tsx`;
  - `invoicing/`: `api.ts` y `PointOfSaleZone.tsx`.
- Crear: `src/locales/es/arca.json` y `src/locales/en/arca.json`, con una sección por familia
- Modificar: `src/app/routes.tsx` (`/configuracion/arca` con `ProtectedRoute permission="arca.read"`), `src/layouts/navigation.ts` (entrada `navigation.arca` en «Configuración», con `permission: "arca.read"`) y `src/locales/{es,en}/common.json`
- Crear: `pages/ArcaSettingsPage.test.tsx`, los tests de cada familia y las escenas visuales de `src/test/visual`

Una familia del front no importa a otra. La página conoce las zonas por un registro chico (familia → componente), así
quitar una familia es borrar su carpeta y su línea.

- [ ] **Paso 1: tests en rojo** (Vitest con MSW), uno por estado de AC3 y por zona:
  - **Sin conexión:** con `arca.manage` se ve «Conectar con ARCA», que abre el paso 1; sin él, «Solo lectura».
  - **Paso 1:**
    - la CUIT con el dígito mal da el error junto al campo, sin pedido al servidor;
    - «Servicios que usa» muestra los instalados, con el padrón marcado y fijo.
  - **«Generar el pedido»** pliega «Datos» con su resumen y ofrece la descarga.
  - **Paso 3:** la lista de lo que se hace en ARCA nombra los servicios elegidos, y un certificado que no corresponde
    muestra el texto del `detail` junto a la carga.
  - **La prueba con un servicio sin autorizar** dice cuál y deja «Probar de nuevo».
  - **La zona «Punto de venta»** aparece solo con Facturación elegida, y «Activar la conexión» se habilita cuando la prueba
    sale en verde y las zonas están completas.
  - **La conexión activa** muestra Empresa, Servicios (con «Lo usa esta empresa»), Certificado y Punto de venta; «Renovar»
    muestra el pendiente dentro de «Certificado».
  - **Avisos de vencimiento:** treinta días antes, amarillo; vencido, rojo.
  - **Un 409** abre «Revisar» con «Leer lo actual».
  - **«Desconectar»** pide confirmación.
  - **«Probando…»:** el botón ocupado.
- [ ] **Paso 2: implementación.**
  - Usa las piezas de `shared/ui`: `Surface` con banda, `Button`, la (i) con tooltip y `ConfirmDialog`.
  - Todas las medidas van en rem ([escala del 75 %](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md)).
  - Sin leyendas fijas: la ayuda va en la (i). Los textos, en voseo.
  - Las zonas apagadas no son interactivas, y la zona en curso recibe el foco al abrirse.
- [ ] **Paso 3: correr** `npm run lint`, `npm run test -- src/features/arca` y `npm run test:visual` para las escenas
  nuevas.
- [ ] **Paso 4: commit en el front.** `feat(arca): pantalla de la conexión con ARCA`

---

### Cierre

- [ ] **Docs:**
  - `docs/features/arca.md`, con la regla vigente y una sección por familia;
  - `docs/guides/configurar-arca.md`, con el anillo en user-secrets, las direcciones, el alta en WSASS y en producción, y la
    renovación;
  - `docs/guides/quitar-arca.md`, con el módulo entero (como `quitar-ia.md`) y una familia sola;
  - el `AGENTS.md` y el `CLAUDE.md` de cada carpeta nueva y el [mapa de instrucciones](../architecture/mapa-de-instrucciones.md);
  - en este plan y en la [hoja de ruta](2026-10-31-arca-formularios-estandar.md), la etapa cerrada.
- [ ] **Verificación,** según [validar un cambio](../guides/validar-un-cambio.md#verificación-y-cierre):
  - la integración con PostgreSQL;
  - `dotnet test` completo (pedir que la PC no se suspenda: tarda unos 51 minutos);
  - `dotnet build ArquitecturaBase.slnx` sin advertencias;
  - en el front: build, lint, tests y la suite visual;
  - `aspire stop` si se levantó la app.
- [ ] **Prueba de fuego de la retirada:** en copias descartables de los dos repos, sin tocar las carpetas de trabajo, un
  agente sigue solo `quitar-arca.md` dos veces.
  - **Quitando la familia `Invoicing`:** el build sin advertencias, la migración que borra solo `ArcaInvoicingSettings`,
    los tests del módulo que quedan en verde y la conexión activa sin punto de venta.
  - **Quitando el módulo entero:**
    - el build sin advertencias, `RemoveArca` con solo los `DropTable` del módulo y `has-pending-model-changes` sin cambios;
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
  - recorrido AC1-A, conexión activa AC2-A y estados AC3 → tareas 7, 8 y 9;
  - módulo, permisos y familias → tareas 1 y 5; datos → tareas 2 y 3; ticket → tarea 5; clientes → tareas 5 y 6;
  - contrato HTTP → tarea 8; errores → tarea 2; robustez y privacidad → tareas 4, 5 y 8; pruebas → cada tarea y el
    cierre; «A confirmar al programar» → paso 0 de la tarea 6.
- **Fuera de este plan,** como fija el diseño: emitir, las tablas de ARCA como catálogos, el autocompletar del cliente y
  los modos de delegación.
