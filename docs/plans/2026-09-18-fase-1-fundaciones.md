# Fase 1 (Fundaciones) — Plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDA: usar superpowers:subagent-driven-development (recomendado) o superpowers:executing-plans para ejecutar este plan tarea por tarea. Los pasos usan checkboxes (`- [ ]`).

**Objetivo:** dejar lista la solución .NET 10 + Aspire 13.5.4 con las cuatro capas, Postgres en el AppHost y las piezas transversales (Result → ProblemDetails traducido, validación, paginado, UTC, auditoría, soft delete), verificadas por tests unitarios, de arquitectura y de integración.

**Arquitectura:** Clean Architecture (Domain → Application → Infrastructure → Api) según `docs/specs/2026-09-18-arquitectura-base-design.md` (fuente de verdad). Handlers propios sin MediatR, decorados con Scrutor (logging → validación → unit of work → handler). La Api solo compone. Los tests de integración usan `WebApplicationFactory` + Testcontainers con una entidad y endpoints que existen solo en el proyecto de tests.

**Stack (versiones verificadas en NuGet el 2026-09-18):** .NET SDK 10.0.400 · Aspire.AppHost.Sdk / Aspire.Hosting.PostgreSQL 13.5.4 · Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 · FluentValidation 12.1.1 · Scrutor 7.0.0 · Microsoft.AspNetCore.OpenApi 10.0.12 · Scalar.AspNetCore 2.17.5 · Microsoft.CodeAnalysis.BannedApiAnalyzers 5.6.0 · xunit.v3 4.0.1 (Microsoft Testing Platform v2) · Testcontainers.PostgreSql 4.15.0 · NetArchTest.Rules 1.3.2 · Microsoft.Extensions.TimeProvider.Testing / Diagnostics.Testing 10.10.0.

---

## Reglas para quien ejecute

- **Rama:** todo se commitea directo en `main`. No crear ramas. **No hacer push.**
- **Commits:** uno por tarea, en español, conventional commits, terminando con la línea `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- **Contraseña de Postgres:** por pedido del usuario, mientras se prueba en local va en `src/ArquitecturaBase.AppHost/appsettings.Development.json` (`Parameters:postgres-password` = `postgres`). Más adelante se pasa a user-secrets. No agregar ningún otro secreto al repo.
- **Docker:** los tests de integración con base de datos (Tareas 16 en adelante) y el AppHost necesitan Docker Desktop encendido. Si `docker version` falla, pedirle al usuario que lo inicie. Testcontainers falla al construir el contenedor si Docker está apagado.
- **Build:** `TreatWarningsAsErrors` está activo. Si aparece una advertencia no prevista, se corrige el código. Solo se suprime en `.editorconfig`, con un comentario que lo justifique, si la regla contradice el spec (ya pasa con CA1716 por el tipo `Error`).
- **Salida en español:** el SDK del usuario imprime en español. "Sin advertencias" = `0 Advertencia(s)` y `0 Errores`.
- **Tests:** `global.json` activa el modo Microsoft Testing Platform de `dotnet test`. Para un proyecto: `dotnet test --project <csproj>`. Para filtrar por clase: `dotnet test --project <csproj> -- --filter-class "Namespace.Clase"`.
- **Idioma:** identificadores y mensajes de excepción/log en inglés. Textos para el usuario en español (por defecto) e inglés, siempre desde `.resx`. Comentarios de código en español y solo cuando aporten.

## Hechos verificados que condicionan el diseño

1. El código de Postgres de la sección 3.1 del spec compila tal cual en Aspire 13.5.4 (`AddPostgres(name, userName, password, port)`, `WithDataVolume(name)`, `WithLifetime(ContainerLifetime.Persistent)`). La imagen por defecto es `postgres:18.3`; los tests usan la misma.
2. La plantilla `aspire-apphost` 13.5.4 agrega `<AspireUseCliBundle>true</AspireUseCliBundle>`. En 13.5 es el comportamiento por defecto de las plantillas nuevas (DCP y dashboard salen del bundle del CLI) y `dotnet run` sigue funcionando: obtiene el bundle con `dnx` y delega en `aspire run`. Se conserva. Con `false`, el SDK emite ASPIRE010. (Corregido durante la Tarea 3: la primera versión del plan lo quitaba basándose en la documentación de 13.4.)
3. La plantilla deja `aspire.config.json` junto al AppHost. Se mueve a la raíz con la ruta del AppHost, para que `aspire run` funcione desde la raíz del repo.
4. `Scrutor.Decorate` lanza `DecorationException` si no hay nada registrado (en la Fase 1 Application no tiene handlers). Se usa `TryDecorate`. El último decorador aplicado queda afuera.
5. Para que los handlers de prueba (en el proyecto de tests) pasen por el mismo pipeline sin decorar dos veces los de producción, `AddFeaturesFromAssembly(assembly)` registra y decora cada ensamblado en una colección aparte y después copia los descriptores.
6. `FluentValidation.WithMessage(Func<T,string>)` sigue reemplazando marcadores (`{MaxLength}`, `{From}`, `{To}`), así que los `.resx` pueden usarlos.
7. En .NET 10 el `Program` de top-level statements ya es público: no hace falta `public partial class Program`.
8. `AnalysisLevel=latest-recommended` activa CA1716 (el tipo `Error` choca con la palabra clave `Error` de VB): se desactiva con justificación. En tests se desactivan CA1707 (guiones bajos en nombres de tests) y CA1861.
9. `Testcontainers.PostgreSqlBuilder()` sin imagen está `[Obsolete]`: se usa `new PostgreSqlBuilder("postgres:18.3")`.
10. xUnit v3 4.0 solo soporta Microsoft Testing Platform v2; con el SDK de .NET 10 hay que activar el modo MTP en `global.json`. Los proyectos de test son `OutputType=Exe`. Un proyecto de tests sin tests hace fallar `dotnet test` (código 8), por eso cada proyecto de tests se crea en la tarea que agrega su primer test.
11. Los interceptores de EF Core se ejecutan **antes** de `DetectChanges`: el interceptor de auditoría llama a `ChangeTracker.DetectChanges()` para ver las entidades modificadas.
12. El `ExceptionHandlerMiddleware` corre dentro de `RequestLocalization` (se registra después) para que el 500 salga en el idioma pedido: los cambios de cultura de un middleware interno no vuelven al externo.
13. `RouteHandlerOptions.ThrowOnBadRequest = true` hace que los errores de binding (JSON inválido, fecha sin offset) lleguen a `GlobalExceptionHandler` y salgan como ProblemDetails 400 con código `Request.Invalid`.

## Estructura de archivos

```
ArquitecturaBase/
├─ ArquitecturaBase.slnx · global.json · aspire.config.json
├─ Directory.Build.props · Directory.Packages.props · BannedSymbols.txt · .editorconfig
├─ CLAUDE.md · README.md
├─ src/
│  ├─ ArquitecturaBase.Domain/
│  │  ├─ Common/        Entity, AggregateRoot, ValueObject, IDomainEvent, IAuditable, ISoftDeletable
│  │  └─ Results/       Result (+ Result<T>), Error, ErrorType, ValidationError
│  ├─ ArquitecturaBase.Application/
│  │  ├─ Abstractions/Messaging/    ICommand, IQuery, ICommandHandler, IQueryHandler
│  │  ├─ Abstractions/Behaviors/    ValidationDecorator, LoggingDecorator, UnitOfWorkDecorator
│  │  ├─ Abstractions/Persistence/  IUnitOfWork
│  │  ├─ Abstractions/Identity/     ICurrentUser
│  │  ├─ Common/Pagination/         PagedRequest, PagedResult<T>, SortDescriptor
│  │  ├─ Common/Validation/         ValidationRules, PagedRequestValidator<T>
│  │  ├─ Resources/                 Errors(.en).resx, Validation(.en).resx, ErrorMessages, ValidationMessages
│  │  └─ DependencyInjection.cs     AddApplication(), AddFeaturesFromAssembly()
│  ├─ ArquitecturaBase.Infrastructure/
│  │  ├─ Persistence/               ApplicationDbContext, UnitOfWork, DatabaseMigrationExtensions
│  │  ├─ Persistence/Interceptors/  AuditableEntityInterceptor, SoftDeleteInterceptor
│  │  ├─ Persistence/Extensions/    QueryableExtensions, ModelBuilderExtensions
│  │  └─ DependencyInjection.cs     AddInfrastructure(IConfiguration)
│  ├─ ArquitecturaBase.Api/
│  │  ├─ Program.cs · DependencyInjection.cs (AddPresentation)
│  │  ├─ Endpoints/                 IEndpoint, EndpointExtensions
│  │  ├─ ErrorHandling/             GlobalExceptionHandler, ProblemDetailsMapper, ResultExtensions
│  │  ├─ Json/UtcDateTimeConverter.cs
│  │  ├─ Localization/LocalizationExtensions.cs
│  │  ├─ OpenApi/OpenApiExtensions.cs
│  │  ├─ Services/CurrentUser.cs
│  │  └─ appsettings(.Development).json · Properties/launchSettings.json
│  ├─ ArquitecturaBase.AppHost/     AppHost.cs (Postgres + Api)
│  └─ ArquitecturaBase.ServiceDefaults/Extensions.cs
└─ tests/
   ├─ Directory.Build.props         Exe + xunit.v3 para todos los tests
   ├─ ArquitecturaBase.Domain.UnitTests/
   ├─ ArquitecturaBase.Application.UnitTests/
   ├─ ArquitecturaBase.Api.IntegrationTests/
   │  ├─ Support/                   ApiFactory, ApiTestGroup, TestAuthHandler, CultureScope, HttpExtensions
   │  └─ TestFeatures/              Widget, TestDbContext, comandos/queries/handlers de prueba, TestEndpoints
   └─ ArquitecturaBase.ArchitectureTests/
```

## Tareas

| # | Tarea | Test |
|---|---|---|
| 1 | Configuración de build y proyecto Domain | verificación de BannedApiAnalyzers |
| 2 | Proyectos Application, Infrastructure, ServiceDefaults y Api | build |
| 3 | AppHost con Postgres | build |
| 4 | Tests de arquitectura | ArchitectureTests |
| 5 | Result, Error y ValidationError | Domain.UnitTests |
| 6 | Entity, AggregateRoot, ValueObject e interfaces de auditoría | Domain.UnitTests |
| 7 | Abstracciones de mensajería, IUnitOfWork e ICurrentUser | build |
| 8 | Resources Errors y Validation (es/en) | Application.UnitTests |
| 9 | Paginado: PagedRequest, PagedResult, SortDescriptor | Application.UnitTests |
| 10 | Reglas de validación comunes | Application.UnitTests |
| 11 | Decoradores de validación, logging y unit of work | Application.UnitTests |
| 12 | AddApplication con Scrutor | Application.UnitTests |
| 13 | Infrastructure: DbContext, UnitOfWork, TimeProvider, migraciones | build |
| 14 | Api: composición, IEndpoint, CurrentUser, localización, OpenAPI + Scalar | build |
| 15 | Result → ProblemDetails | Api.IntegrationTests (sin Docker) |
| 16 | Arnés de integración + traducciones + diccionario de validación | Api.IntegrationTests |
| 17 | GlobalExceptionHandler (500 y 400 de request) | Api.IntegrationTests |
| 18 | UtcDateTimeConverter | Api.IntegrationTests |
| 19 | Auditoría y soft delete | Api.IntegrationTests |
| 20 | Paginado en Infrastructure (ApplySort + ToPagedResultAsync) | Api.IntegrationTests |
| 21 | CLAUDE.md y README | — |
| 22 | Verificación final: build, tests, AppHost, DBeaver | manual + comandos |

---

### Tarea 1: Configuración de build y proyecto Domain

**Archivos:**
- Crear: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `BannedSymbols.txt`, `.editorconfig`, `ArquitecturaBase.slnx`
- Crear: `src/ArquitecturaBase.Domain/ArquitecturaBase.Domain.csproj`

- [ ] **Paso 1: `global.json`**

```json
{
  "sdk": {
    "version": "10.0.400",
    "rollForward": "latestFeature"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

- [ ] **Paso 2: `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <!-- Sin el archivo de documentación, IDE0005 (usings innecesarios) no se evalúa en el build. -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" Link="BannedSymbols.txt" />
  </ItemGroup>
</Project>
```

- [ ] **Paso 3: `Directory.Packages.props`** (las tareas siguientes agregan sus `PackageVersion`)

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup Label="Analizadores para todos los proyectos">
    <GlobalPackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="5.6.0" />
  </ItemGroup>
</Project>
```

- [ ] **Paso 4: `BannedSymbols.txt`** (UTF-8)

```
P:System.DateTime.Now;Usá TimeProvider inyectado: timeProvider.GetUtcNow().UtcDateTime
P:System.DateTime.Today;Usá TimeProvider inyectado: timeProvider.GetUtcNow().UtcDateTime
P:System.DateTime.UtcNow;Usá TimeProvider inyectado para poder testear el tiempo: timeProvider.GetUtcNow().UtcDateTime
P:System.DateTimeOffset.Now;Usá TimeProvider inyectado: timeProvider.GetUtcNow()
P:System.DateTimeOffset.UtcNow;Usá TimeProvider inyectado para poder testear el tiempo: timeProvider.GetUtcNow()
```

- [ ] **Paso 5: `.editorconfig`**

```ini
root = true

[*]
charset = utf-8
indent_style = space
indent_size = 4
insert_final_newline = true
trim_trailing_whitespace = true

[*.{csproj,props,targets,slnx,xml,json,resx}]
indent_size = 2

[*.cs]
# Estilo que rompe el build
csharp_style_namespace_declarations = file_scoped:warning
dotnet_diagnostic.IDE0161.severity = warning
dotnet_diagnostic.IDE0005.severity = warning

# Estilo sugerido
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = true:suggestion
csharp_prefer_braces = true:suggestion
dotnet_style_qualification_for_field = false:suggestion
dotnet_style_qualification_for_property = false:suggestion
dotnet_style_qualification_for_method = false:suggestion

# Campos privados: _camelCase
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private
dotnet_naming_style.underscore_camel_case.capitalization = camel_case
dotnet_naming_style.underscore_camel_case.required_prefix = _
dotnet_naming_rule.private_fields_underscore.symbols = private_fields
dotnet_naming_rule.private_fields_underscore.style = underscore_camel_case
dotnet_naming_rule.private_fields_underscore.severity = suggestion

# CA1716: el spec exige un tipo llamado Error y no hay consumidores en Visual Basic.
dotnet_diagnostic.CA1716.severity = none

[tests/**.cs]
# Los nombres de tests usan guiones bajos: Metodo_condicion_resultado.
dotnet_diagnostic.CA1707.severity = none
# Arrays literales en datos de prueba y asserts.
dotnet_diagnostic.CA1861.severity = none
```

- [ ] **Paso 6: proyecto Domain y solución**

`src/ArquitecturaBase.Domain/ArquitecturaBase.Domain.csproj` (el resto sale de `Directory.Build.props`; Domain no tiene paquetes):

```xml
<Project Sdk="Microsoft.NET.Sdk">
</Project>
```

```bash
dotnet new sln -n ArquitecturaBase --format slnx
dotnet sln ArquitecturaBase.slnx add src/ArquitecturaBase.Domain/ArquitecturaBase.Domain.csproj
dotnet build ArquitecturaBase.slnx
```

Esperado: compila con `0 Advertencia(s)` y `0 Errores`.

- [ ] **Paso 7: comprobar que BannedApiAnalyzers rompe el build** (archivo temporal, no se commitea)

Crear `src/ArquitecturaBase.Domain/BannedCheck.cs`:

```csharp
namespace ArquitecturaBase.Domain;

internal static class BannedCheck
{
    public static DateTime Now() => DateTime.UtcNow;
}
```

Run: `dotnet build ArquitecturaBase.slnx`
Esperado: FALLA con `error RS0030: El símbolo "DateTime.UtcNow" está prohibido...`.
Borrar `BannedCheck.cs` y volver a compilar: `0 Errores`.

- [ ] **Paso 8: commit**

```bash
git add global.json Directory.Build.props Directory.Packages.props BannedSymbols.txt .editorconfig ArquitecturaBase.slnx src/ArquitecturaBase.Domain
git commit -m "chore: agregar solución, configuración de build y proyecto Domain" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 2: Proyectos Application, Infrastructure, ServiceDefaults y Api

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/ArquitecturaBase.Application.csproj`
- Crear: `src/ArquitecturaBase.Infrastructure/ArquitecturaBase.Infrastructure.csproj`
- Crear (plantilla + edición): `src/ArquitecturaBase.ServiceDefaults/ArquitecturaBase.ServiceDefaults.csproj`, `src/ArquitecturaBase.ServiceDefaults/Extensions.cs`
- Crear: `src/ArquitecturaBase.Api/ArquitecturaBase.Api.csproj`, `Program.cs`, `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`
- Modificar: `Directory.Packages.props`, `ArquitecturaBase.slnx`

- [ ] **Paso 1: Application e Infrastructure**

`src/ArquitecturaBase.Application/ArquitecturaBase.Application.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Los .resx sin sufijo están en español. -->
    <NeutralLanguage>es</NeutralLanguage>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\ArquitecturaBase.Domain\ArquitecturaBase.Domain.csproj" />
  </ItemGroup>
</Project>
```

`src/ArquitecturaBase.Infrastructure/ArquitecturaBase.Infrastructure.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\ArquitecturaBase.Application\ArquitecturaBase.Application.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Paso 2: ServiceDefaults desde la plantilla oficial**

```bash
dotnet new aspire-servicedefaults -n ArquitecturaBase.ServiceDefaults -o src/ArquitecturaBase.ServiceDefaults
```

Reemplazar `src/ArquitecturaBase.ServiceDefaults/ArquitecturaBase.ServiceDefaults.csproj` (sin versiones por CPM; framework y nullable vienen de `Directory.Build.props`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsAspireSharedProject>true</IsAspireSharedProject>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />

    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
  </ItemGroup>
</Project>
```

Reemplazar `src/ArquitecturaBase.ServiceDefaults/Extensions.cs` (misma lógica que la plantilla 13.5.4, sin los bloques comentados ni el using que solo ellos usaban, y con las etiquetas en un campo por CA1861):

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Servicios comunes de Aspire: service discovery, resiliencia, health checks y OpenTelemetry.
// Basado en la plantilla aspire-servicedefaults 13.5.4: https://aka.ms/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";
    private static readonly string[] LiveTags = ["live"];

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(options =>
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), LiveTags);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Solo en desarrollo: exponer health checks en producción tiene implicancias de seguridad.
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthEndpointPath);

            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("live")
            });
        }

        return app;
    }
}
```

Agregar a `Directory.Packages.props`:

```xml
  <ItemGroup Label="ServiceDefaults">
    <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.10.0" />
    <PackageVersion Include="Microsoft.Extensions.ServiceDiscovery" Version="10.10.0" />
    <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.19.0" />
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.19.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.19.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.19.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.19.0" />
  </ItemGroup>
```

- [ ] **Paso 3: Api mínima**

`src/ArquitecturaBase.Api/ArquitecturaBase.Api.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <ProjectReference Include="..\ArquitecturaBase.Application\ArquitecturaBase.Application.csproj" />
    <ProjectReference Include="..\ArquitecturaBase.Infrastructure\ArquitecturaBase.Infrastructure.csproj" />
    <ProjectReference Include="..\ArquitecturaBase.ServiceDefaults\ArquitecturaBase.ServiceDefaults.csproj" />
  </ItemGroup>
</Project>
```

`src/ArquitecturaBase.Api/Program.cs` (se completa en la Tarea 14):

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

await app.RunAsync();
```

`src/ArquitecturaBase.Api/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

`src/ArquitecturaBase.Api/appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

`src/ArquitecturaBase.Api/Properties/launchSettings.json`:

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "https": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "https://localhost:7180;http://localhost:5180",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    },
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5180",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

- [ ] **Paso 4: agregar a la solución y compilar**

```bash
dotnet sln ArquitecturaBase.slnx add src/ArquitecturaBase.Application/ArquitecturaBase.Application.csproj src/ArquitecturaBase.Infrastructure/ArquitecturaBase.Infrastructure.csproj src/ArquitecturaBase.ServiceDefaults/ArquitecturaBase.ServiceDefaults.csproj src/ArquitecturaBase.Api/ArquitecturaBase.Api.csproj
dotnet build ArquitecturaBase.slnx
```

Esperado: `0 Advertencia(s)`, `0 Errores`. Si IDE0005 marca un using de `Extensions.cs`, quitarlo; si falta uno (CS0246/CS1061), agregarlo.

- [ ] **Paso 5: commit**

```bash
git add Directory.Packages.props ArquitecturaBase.slnx src/ArquitecturaBase.Application src/ArquitecturaBase.Infrastructure src/ArquitecturaBase.ServiceDefaults src/ArquitecturaBase.Api
git commit -m "chore: agregar proyectos Application, Infrastructure, ServiceDefaults y Api" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 3: AppHost con Postgres

**Archivos:**
- Crear (plantilla + edición): `src/ArquitecturaBase.AppHost/ArquitecturaBase.AppHost.csproj`, `AppHost.cs`, `appsettings*.json`, `Properties/launchSettings.json`
- Crear: `aspire.config.json` (raíz)
- Borrar: `src/ArquitecturaBase.AppHost/aspire.config.json`
- Modificar: `Directory.Packages.props`, `ArquitecturaBase.slnx`

- [ ] **Paso 1: generar desde la plantilla**

```bash
dotnet new aspire-apphost -n ArquitecturaBase.AppHost -o src/ArquitecturaBase.AppHost
rm src/ArquitecturaBase.AppHost/aspire.config.json
```

- [ ] **Paso 2: csproj.** Conservar el `UserSecretsId` que generó la plantilla. Quitar `TargetFramework`, `ImplicitUsings` y `Nullable` (vienen de `Directory.Build.props`). Conservar `AspireUseCliBundle` (ver "Hechos verificados" 2):

```xml
<Project Sdk="Aspire.AppHost.Sdk/13.5.4">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AspireUseCliBundle>true</AspireUseCliBundle>
    <UserSecretsId>GUID-GENERADO-POR-LA-PLANTILLA</UserSecretsId>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.PostgreSQL" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ArquitecturaBase.Api\ArquitecturaBase.Api.csproj" />
  </ItemGroup>
</Project>
```

Agregar a `Directory.Packages.props`:

```xml
  <ItemGroup Label="Aspire">
    <PackageVersion Include="Aspire.Hosting.PostgreSQL" Version="13.5.4" />
  </ItemGroup>
```

- [ ] **Paso 3: `src/ArquitecturaBase.AppHost/AppHost.cs`** (idéntico a la sección 3.1 del spec)

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Contraseña fija (Parameters:postgres-password). La usa también DBeaver.
var postgresPassword = builder.AddParameter("postgres-password", secret: true);

// Puerto fijo 5433 (el 5432 lo usa el PostgreSQL local), contenedor persistente y volumen con nombre.
var postgres = builder.AddPostgres("postgres", password: postgresPassword, port: 5433)
    .WithDataVolume("arquitecturabase-pgdata")
    .WithLifetime(ContainerLifetime.Persistent);

var appDb = postgres.AddDatabase("appdb");

builder.AddProject<Projects.ArquitecturaBase_Api>("api")
    .WithReference(appDb)
    .WaitFor(appDb);

builder.Build().Run();
```

- [ ] **Paso 4: `aspire.config.json` en la raíz y contraseña local**

```json
{
  "appHost": {
    "path": "src/ArquitecturaBase.AppHost/ArquitecturaBase.AppHost.csproj"
  }
}
```

Reemplazar `src/ArquitecturaBase.AppHost/appsettings.Development.json` (temporal, por pedido del usuario: después pasa a user-secrets):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Parameters": {
    "postgres-password": "postgres"
  }
}
```

- [ ] **Paso 5: compilar**

```bash
dotnet sln ArquitecturaBase.slnx add src/ArquitecturaBase.AppHost/ArquitecturaBase.AppHost.csproj
dotnet build ArquitecturaBase.slnx
```

Esperado: `0 Advertencia(s)`, `0 Errores`. Si NuGet se queja de la referencia implícita a `Aspire.Hosting.AppHost` por CPM, agregar `<PackageVersion Include="Aspire.Hosting.AppHost" Version="13.5.4" />` e informarlo. La ejecución se verifica en la Tarea 22.

- [ ] **Paso 6: commit**

```bash
git add Directory.Packages.props ArquitecturaBase.slnx aspire.config.json src/ArquitecturaBase.AppHost
git commit -m "feat: agregar AppHost con Postgres persistente en el puerto 5433" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 4: Tests de arquitectura

**Archivos:**
- Crear: `tests/Directory.Build.props`
- Crear: `tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj`
- Crear: `tests/ArquitecturaBase.ArchitectureTests/SolutionRoot.cs`, `ProjectReferencesTests.cs`, `LayerDependencyTests.cs`
- Modificar: `Directory.Packages.props`, `ArquitecturaBase.slnx`

- [ ] **Paso 1: `tests/Directory.Build.props`** (importa el de la raíz y configura xUnit v3 para todos los tests)

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />

  <PropertyGroup>
    <!-- xUnit v3 compila cada proyecto de tests como ejecutable de Microsoft Testing Platform. -->
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

Agregar a `Directory.Packages.props`:

```xml
  <ItemGroup Label="Tests">
    <PackageVersion Include="xunit.v3" Version="4.0.1" />
    <PackageVersion Include="NetArchTest.Rules" Version="1.3.2" />
  </ItemGroup>
```

- [ ] **Paso 2: proyecto**

`tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="NetArchTest.Rules" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ArquitecturaBase.Domain\ArquitecturaBase.Domain.csproj" />
    <ProjectReference Include="..\..\src\ArquitecturaBase.Application\ArquitecturaBase.Application.csproj" />
    <ProjectReference Include="..\..\src\ArquitecturaBase.Infrastructure\ArquitecturaBase.Infrastructure.csproj" />
    <ProjectReference Include="..\..\src\ArquitecturaBase.Api\ArquitecturaBase.Api.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Paso 3: tests de referencias entre proyectos** (tabla de la sección 3.1; cubre también AppHost y ServiceDefaults)

`tests/ArquitecturaBase.ArchitectureTests/SolutionRoot.cs`:

```csharp
namespace ArquitecturaBase.ArchitectureTests;

internal static class SolutionRoot
{
    public static string FullPath { get; } = Find();

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ArquitecturaBase.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("ArquitecturaBase.slnx was not found.");
    }
}
```

`tests/ArquitecturaBase.ArchitectureTests/ProjectReferencesTests.cs`:

```csharp
using System.Xml.Linq;

namespace ArquitecturaBase.ArchitectureTests;

public sealed class ProjectReferencesTests
{
    public static TheoryData<string, string[]> AllowedReferences => new()
    {
        { "ArquitecturaBase.Domain", [] },
        { "ArquitecturaBase.Application", ["ArquitecturaBase.Domain"] },
        { "ArquitecturaBase.Infrastructure", ["ArquitecturaBase.Application", "ArquitecturaBase.Domain"] },
        { "ArquitecturaBase.Api", ["ArquitecturaBase.Application", "ArquitecturaBase.Infrastructure", "ArquitecturaBase.ServiceDefaults"] },
        { "ArquitecturaBase.AppHost", ["ArquitecturaBase.Api"] },
        { "ArquitecturaBase.ServiceDefaults", [] },
    };

    [Theory]
    [MemberData(nameof(AllowedReferences))]
    public void Project_only_references_allowed_projects(string project, string[] allowed)
    {
        var forbidden = ReadProjectReferences(project).Except(allowed, StringComparer.Ordinal);

        Assert.Empty(forbidden);
    }

    private static string[] ReadProjectReferences(string project)
    {
        var path = Path.Combine(SolutionRoot.FullPath, "src", project, project + ".csproj");

        return XDocument.Load(path)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")!.Value.Replace('\\', '/'))
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => name!)
            .ToArray();
    }
}
```

- [ ] **Paso 4: tests de dependencias entre tipos**

`tests/ArquitecturaBase.ArchitectureTests/LayerDependencyTests.cs`:

```csharp
using System.Reflection;
using NetArchTest.Rules;

namespace ArquitecturaBase.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private const string DomainNamespace = "ArquitecturaBase.Domain";
    private const string ApplicationNamespace = "ArquitecturaBase.Application";
    private const string InfrastructureNamespace = "ArquitecturaBase.Infrastructure";
    private const string ApiNamespace = "ArquitecturaBase.Api";

    private static readonly Assembly DomainAssembly = Assembly.Load(DomainNamespace);
    private static readonly Assembly ApplicationAssembly = Assembly.Load(ApplicationNamespace);
    private static readonly Assembly InfrastructureAssembly = Assembly.Load(InfrastructureNamespace);
    private static readonly Assembly ApiAssembly = Assembly.Load(ApiNamespace);

    [Fact]
    public void Domain_does_not_depend_on_other_layers_or_frameworks()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                ApplicationNamespace,
                InfrastructureNamespace,
                ApiNamespace,
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions",
                "FluentValidation",
                "Npgsql")
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Domain_only_references_the_base_class_library()
    {
        var references = DomainAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name != "netstandard");

        Assert.Empty(references);
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_api_or_persistence()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                InfrastructureNamespace,
                ApiNamespace,
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Npgsql")
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_api()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Api_uses_infrastructure_only_from_the_composition_root()
    {
        // Program.cs (namespace global) es el único que llama a AddInfrastructure y a las migraciones.
        var result = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace(ApiNamespace)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        AssertSuccessful(result);
    }

    private static void AssertSuccessful(TestResult result) =>
        Assert.True(result.IsSuccessful, "Types breaking the rule: " + string.Join(", ", result.FailingTypeNames ?? []));
}
```

- [ ] **Paso 5: correr los tests**

```bash
dotnet sln ArquitecturaBase.slnx add tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj
dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj
```

Esperado: `total: 11`, `correcto: 11` (6 casos de la teoría + 5 hechos).

- [ ] **Paso 6: comprobar que la regla detecta una violación** (cambio temporal, no se commitea)

En `AllowedReferences`, quitar `"ArquitecturaBase.ServiceDefaults"` de la fila de la Api y correr de nuevo.
Esperado: FALLA `Project_only_references_allowed_projects` para `ArquitecturaBase.Api` mostrando `ArquitecturaBase.ServiceDefaults`. Restaurar la fila y confirmar que vuelve a pasar.

- [ ] **Paso 7: commit**

```bash
git add Directory.Packages.props ArquitecturaBase.slnx tests/Directory.Build.props tests/ArquitecturaBase.ArchitectureTests
git commit -m "test: agregar tests de arquitectura con las reglas de dependencias entre capas" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 5: Result, Error y ValidationError

**Archivos:**
- Crear: `src/ArquitecturaBase.Domain/Results/ErrorType.cs`, `Error.cs`, `ValidationError.cs`, `Result.cs`
- Crear: `tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj`
- Test: `tests/ArquitecturaBase.Domain.UnitTests/Results/ResultTests.cs`, `ErrorTests.cs`
- Modificar: `ArquitecturaBase.slnx`

- [ ] **Paso 1: proyecto de tests y tests que fallan**

`tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\ArquitecturaBase.Domain\ArquitecturaBase.Domain.csproj" />
  </ItemGroup>
</Project>
```

`tests/ArquitecturaBase.Domain.UnitTests/Results/ResultTests.cs`:

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.UnitTests.Results;

public sealed class ResultTests
{
    private static readonly Error SampleError = Error.NotFound("Test.Sample.NotFound", "Sample not found.");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_carries_the_error()
    {
        var result = Result.Failure(SampleError);

        Assert.True(result.IsFailure);
        Assert.Equal(SampleError, result.Error);
    }

    [Fact]
    public void Failure_without_error_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Result.Failure(Error.None));
    }

    [Fact]
    public void Success_with_value_exposes_the_value()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Value_of_failed_result_throws()
    {
        var result = Result.Failure<int>(SampleError);

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Value_converts_implicitly_to_successful_result()
    {
        Result<string> result = "hola";

        Assert.True(result.IsSuccess);
        Assert.Equal("hola", result.Value);
    }

    [Fact]
    public void Error_converts_implicitly_to_failed_result()
    {
        Result<string> typed = SampleError;
        Result untyped = SampleError;

        Assert.Equal(SampleError, typed.Error);
        Assert.Equal(SampleError, untyped.Error);
    }
}
```

`tests/ArquitecturaBase.Domain.UnitTests/Results/ErrorTests.cs`:

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.UnitTests.Results;

public sealed class ErrorTests
{
    [Fact]
    public void Factories_set_the_error_type()
    {
        Assert.Equal(ErrorType.Failure, Error.Failure("A.B.C", "d").Type);
        Assert.Equal(ErrorType.Validation, Error.Validation("A.B.C", "d").Type);
        Assert.Equal(ErrorType.Unauthorized, Error.Unauthorized("A.B.C", "d").Type);
        Assert.Equal(ErrorType.Forbidden, Error.Forbidden("A.B.C", "d").Type);
        Assert.Equal(ErrorType.NotFound, Error.NotFound("A.B.C", "d").Type);
        Assert.Equal(ErrorType.Conflict, Error.Conflict("A.B.C", "d").Type);
        Assert.Equal(ErrorType.TooManyRequests, Error.TooManyRequests("A.B.C", "d").Type);
    }

    [Fact]
    public void Errors_with_the_same_data_are_equal()
    {
        Assert.Equal(Error.Conflict("A.B.C", "d"), Error.Conflict("A.B.C", "d"));
    }

    [Fact]
    public void Metadata_is_kept()
    {
        var metadata = new Dictionary<string, object?> { ["attemptsLeft"] = 3 };

        var error = Error.Validation("Auth.LoginCode.Invalid", "Invalid code.", metadata);

        Assert.Equal(3, error.Metadata!["attemptsLeft"]);
    }

    [Fact]
    public void Validation_error_groups_messages_by_field()
    {
        var error = new ValidationError(new Dictionary<string, string[]> { ["email"] = ["Invalid."] });

        Assert.Equal(ValidationError.ErrorCode, error.Code);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal("Invalid.", Assert.Single(error.Errors["email"]));
    }
}
```

- [ ] **Paso 2: correr y ver que falla**

```bash
dotnet sln ArquitecturaBase.slnx add tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj
dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj
```

Esperado: FALLA la compilación (`CS0246: ... 'Result' no se encontró`).

- [ ] **Paso 3: implementación**

`src/ArquitecturaBase.Domain/Results/ErrorType.cs`:

```csharp
namespace ArquitecturaBase.Domain.Results;

public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    Unauthorized = 2,
    Forbidden = 3,
    NotFound = 4,
    Conflict = 5,
    TooManyRequests = 6,
}
```

`src/ArquitecturaBase.Domain/Results/Error.cs`:

```csharp
namespace ArquitecturaBase.Domain.Results;

/// <summary>
/// Error de negocio. <see cref="Code"/> es estable y sigue el formato Area.Entidad.Motivo:
/// la API lo usa como clave para traducir la descripción desde Errors.resx.
/// </summary>
public record Error(string Code, string Description, ErrorType Type, IReadOnlyDictionary<string, object?>? Metadata = null)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Failure(string code, string description, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, description, ErrorType.Failure, metadata);

    public static Error Validation(string code, string description, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, description, ErrorType.Validation, metadata);

    public static Error Unauthorized(string code, string description, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, description, ErrorType.Unauthorized, metadata);

    public static Error Forbidden(string code, string description, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, description, ErrorType.Forbidden, metadata);

    public static Error NotFound(string code, string description, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, description, ErrorType.NotFound, metadata);

    public static Error Conflict(string code, string description, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, description, ErrorType.Conflict, metadata);

    public static Error TooManyRequests(string code, string description, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, description, ErrorType.TooManyRequests, metadata);
}
```

`src/ArquitecturaBase.Domain/Results/ValidationError.cs`:

```csharp
namespace ArquitecturaBase.Domain.Results;

/// <summary>Error de validación con los mensajes agrupados por campo (campo → mensajes).</summary>
public sealed record ValidationError : Error
{
    public const string ErrorCode = "Validation.Failed";

    public ValidationError(IReadOnlyDictionary<string, string[]> errors)
        : base(ErrorCode, "One or more validation errors occurred.", ErrorType.Validation)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
```

`src/ArquitecturaBase.Domain/Results/Result.cs`:

```csharp
namespace ArquitecturaBase.Domain.Results;

/// <summary>
/// Resultado de una operación. Las reglas de negocio fallan devolviendo un <see cref="Error"/>,
/// nunca lanzando excepciones.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("A successful result cannot carry an error.", nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException("A failed result must carry an error.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>Resultado que, si fue exitoso, trae un valor.</summary>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    protected internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The value of a failed result cannot be accessed.");

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);
}
```

- [ ] **Paso 4: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj`
Esperado: `total: 11`, `correcto: 11`.

- [ ] **Paso 5: commit**

```bash
git add ArquitecturaBase.slnx src/ArquitecturaBase.Domain/Results tests/ArquitecturaBase.Domain.UnitTests
git commit -m "feat: agregar Result, Error y ValidationError en Domain" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 6: Entity, AggregateRoot, ValueObject e interfaces de auditoría

**Archivos:**
- Crear: `src/ArquitecturaBase.Domain/Common/Entity.cs`, `AggregateRoot.cs`, `ValueObject.cs`, `IDomainEvent.cs`, `IAuditable.cs`, `ISoftDeletable.cs`
- Test: `tests/ArquitecturaBase.Domain.UnitTests/Common/EntityTests.cs`, `AggregateRootTests.cs`, `ValueObjectTests.cs`

- [ ] **Paso 1: tests que fallan**

`tests/ArquitecturaBase.Domain.UnitTests/Common/EntityTests.cs`:

```csharp
using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.UnitTests.Common;

public sealed class EntityTests
{
    private sealed class Product : Entity
    {
        public Product()
        {
        }

        public Product(Guid id)
            : base(id)
        {
        }
    }

    private sealed class Order(Guid id) : Entity(id);

    [Fact]
    public void New_entity_gets_a_version_7_guid()
    {
        var product = new Product();

        Assert.Equal(7, product.Id.Version);
    }

    [Fact]
    public void Entities_with_the_same_type_and_id_are_equal()
    {
        var id = Guid.CreateVersion7();
        var first = new Product(id);
        var second = new Product(id);

        var equalByOperator = first == second;

        Assert.Equal(first, second);
        Assert.True(equalByOperator);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Entities_of_different_types_are_not_equal_even_with_the_same_id()
    {
        var id = Guid.CreateVersion7();

        Assert.NotEqual<Entity>(new Product(id), new Order(id));
    }

    [Fact]
    public void Empty_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Product(Guid.Empty));
    }
}
```

`tests/ArquitecturaBase.Domain.UnitTests/Common/AggregateRootTests.cs`:

```csharp
using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.UnitTests.Common;

public sealed class AggregateRootTests
{
    private sealed record SomethingHappened(string What) : IDomainEvent;

    private sealed class Basket : AggregateRoot
    {
        public void DoSomething(string what) => RaiseDomainEvent(new SomethingHappened(what));
    }

    [Fact]
    public void Raised_events_are_accumulated_in_order()
    {
        var basket = new Basket();

        basket.DoSomething("first");
        basket.DoSomething("second");

        Assert.Equal(
            [new SomethingHappened("first"), new SomethingHappened("second")],
            basket.GetDomainEvents());
    }

    [Fact]
    public void Clearing_removes_the_events()
    {
        var basket = new Basket();
        basket.DoSomething("first");

        basket.ClearDomainEvents();

        Assert.Empty(basket.GetDomainEvents());
    }
}
```

`tests/ArquitecturaBase.Domain.UnitTests/Common/ValueObjectTests.cs`:

```csharp
using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.UnitTests.Common;

public sealed class ValueObjectTests
{
    private sealed class Money(decimal amount, string currency) : ValueObject
    {
        public decimal Amount { get; } = amount;

        public string Currency { get; } = currency;

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    [Fact]
    public void Value_objects_with_the_same_components_are_equal()
    {
        var first = new Money(10, "ARS");
        var second = new Money(10, "ARS");

        var equalByOperator = first == second;

        Assert.Equal(first, second);
        Assert.True(equalByOperator);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Value_objects_with_different_components_are_not_equal()
    {
        Assert.NotEqual(new Money(10, "ARS"), new Money(10, "USD"));
    }
}
```

Nota: si `Assert.Equal([..], basket.GetDomainEvents())` resulta ambiguo entre sobrecargas de xUnit, reemplazarlo por `Assert.Equal(new IDomainEvent[] { ... }, basket.GetDomainEvents())`.

- [ ] **Paso 2: correr y ver que falla**

Run: `dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj`
Esperado: FALLA la compilación (`CS0246: ... 'Entity' no se encontró`).

- [ ] **Paso 3: implementación**

`src/ArquitecturaBase.Domain/Common/IDomainEvent.cs`:

```csharp
namespace ArquitecturaBase.Domain.Common;

/// <summary>Algo relevante que pasó en el dominio. Lo acumula el agregado que lo produjo.</summary>
public interface IDomainEvent;
```

`src/ArquitecturaBase.Domain/Common/IAuditable.cs`:

```csharp
namespace ArquitecturaBase.Domain.Common;

/// <summary>
/// Entidad auditada. Los valores los completa el interceptor de Infrastructure al guardar
/// (fechas en UTC desde TimeProvider y usuario desde ICurrentUser); el dominio no los modifica.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAtUtc { get; }

    Guid? CreatedBy { get; }

    DateTime? ModifiedAtUtc { get; }

    Guid? ModifiedBy { get; }
}
```

`src/ArquitecturaBase.Domain/Common/ISoftDeletable.cs`:

```csharp
namespace ArquitecturaBase.Domain.Common;

/// <summary>
/// Entidad que se marca como borrada en lugar de eliminarse. Un filtro global de EF Core
/// la excluye de las consultas. Las propiedades deben ser públicas (no implementación explícita).
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }

    DateTime? DeletedAtUtc { get; }

    Guid? DeletedBy { get; }
}
```

`src/ArquitecturaBase.Domain/Common/Entity.cs`:

```csharp
namespace ArquitecturaBase.Domain.Common;

/// <summary>Entidad con identidad. El Id es un Guid v7, ordenable por momento de creación.</summary>
public abstract class Entity : IEquatable<Entity>
{
    protected Entity()
        : this(Guid.CreateVersion7())
    {
    }

    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The id cannot be empty.", nameof(id));
        }

        Id = id;
    }

    public Guid Id { get; private init; }

    public bool Equals(Entity? other) => other is not null && other.GetType() == GetType() && other.Id == Id;

    public override bool Equals(object? obj) => Equals(obj as Entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);
}
```

`src/ArquitecturaBase.Domain/Common/AggregateRoot.cs`:

```csharp
namespace ArquitecturaBase.Domain.Common;

/// <summary>Raíz de agregado: acumula los eventos de dominio que produce.</summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot()
    {
    }

    protected AggregateRoot(Guid id)
        : base(id)
    {
    }

    // Métodos y no propiedades: así EF Core no intenta mapear los eventos.
    public IReadOnlyCollection<IDomainEvent> GetDomainEvents() => _domainEvents.ToArray();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }
}
```

`src/ArquitecturaBase.Domain/Common/ValueObject.cs`:

```csharp
namespace ArquitecturaBase.Domain.Common;

/// <summary>Objeto de valor: sin identidad, igual a otro si todos sus componentes son iguales.</summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other) =>
        other is not null
        && other.GetType() == GetType()
        && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());

    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right) => Equals(left, right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !Equals(left, right);
}
```

- [ ] **Paso 4: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj`
Esperado: `total: 19`, `correcto: 19`.

Run: `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj`
Esperado: todos pasan (Domain sigue sin dependencias externas).

- [ ] **Paso 5: commit**

```bash
git add src/ArquitecturaBase.Domain/Common tests/ArquitecturaBase.Domain.UnitTests/Common
git commit -m "feat: agregar Entity con Guid v7, AggregateRoot, ValueObject e interfaces de auditoría" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 7: Abstracciones de mensajería, IUnitOfWork e ICurrentUser

Solo interfaces, sin lógica: se verifica con el build.

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Abstractions/Messaging/ICommand.cs`, `IQuery.cs`, `ICommandHandler.cs`, `IQueryHandler.cs`
- Crear: `src/ArquitecturaBase.Application/Abstractions/Persistence/IUnitOfWork.cs`
- Crear: `src/ArquitecturaBase.Application/Abstractions/Identity/ICurrentUser.cs`

- [ ] **Paso 1: interfaces**

`Abstractions/Messaging/ICommand.cs`:

```csharp
namespace ArquitecturaBase.Application.Abstractions.Messaging;

/// <summary>Comando que modifica estado y no devuelve valor.</summary>
public interface ICommand;

/// <summary>Comando que modifica estado y devuelve <typeparamref name="TResponse"/>.</summary>
public interface ICommand<TResponse>;
```

`Abstractions/Messaging/IQuery.cs`:

```csharp
namespace ArquitecturaBase.Application.Abstractions.Messaging;

/// <summary>Consulta sin efectos secundarios que devuelve <typeparamref name="TResponse"/>.</summary>
public interface IQuery<TResponse>;
```

`Abstractions/Messaging/ICommandHandler.cs`:

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Abstractions.Messaging;

public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken);
}
```

`Abstractions/Messaging/IQueryHandler.cs`:

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Abstractions.Messaging;

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken);
}
```

`Abstractions/Persistence/IUnitOfWork.cs`:

```csharp
namespace ArquitecturaBase.Application.Abstractions.Persistence;

/// <summary>Confirma los cambios de un caso de uso. Lo llama UnitOfWorkDecorator, no los handlers.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

`Abstractions/Identity/ICurrentUser.cs`:

```csharp
namespace ArquitecturaBase.Application.Abstractions.Identity;

/// <summary>Usuario de la petición actual. Sin sesión, <see cref="UserId"/> es null.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }
}
```

- [ ] **Paso 2: compilar**

Run: `dotnet build ArquitecturaBase.slnx`
Esperado: `0 Advertencia(s)`, `0 Errores`.

- [ ] **Paso 3: commit**

```bash
git add src/ArquitecturaBase.Application/Abstractions
git commit -m "feat: agregar abstracciones de mensajería, IUnitOfWork e ICurrentUser" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 8: Resources Errors y Validation (español por defecto e inglés)

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Resources/Errors.resx`, `Errors.en.resx`, `Validation.resx`, `Validation.en.resx`
- Crear: `src/ArquitecturaBase.Application/Resources/ErrorMessages.cs`, `ValidationMessages.cs`
- Modificar: `src/ArquitecturaBase.Application/ArquitecturaBase.Application.csproj` (InternalsVisibleTo)
- Crear: `tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`, `CultureScope.cs`
- Test: `tests/ArquitecturaBase.Application.UnitTests/Resources/ErrorMessagesTests.cs`, `ValidationMessagesTests.cs`, `ResourceParityTests.cs`
- Modificar: `ArquitecturaBase.slnx`

Las claves de `Errors.resx` son los códigos de error (`Area.Entidad.Motivo`) más los títulos por tipo (`Title.<ErrorType>`). Los textos usan voseo, como el spec.

- [ ] **Paso 1: proyecto de tests, helper y tests que fallan**

Agregar al csproj de Application:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="ArquitecturaBase.Application.UnitTests" />
  </ItemGroup>
```

`tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\ArquitecturaBase.Application\ArquitecturaBase.Application.csproj" />
  </ItemGroup>
</Project>
```

`tests/ArquitecturaBase.Application.UnitTests/CultureScope.cs`:

```csharp
using System.Globalization;

namespace ArquitecturaBase.Application.UnitTests;

/// <summary>Cambia la cultura del test actual y la restaura al salir del using.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public CultureScope(string name)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
```

`tests/ArquitecturaBase.Application.UnitTests/Resources/ErrorMessagesTests.cs`:

```csharp
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Resources;

public sealed class ErrorMessagesTests
{
    [Theory]
    [InlineData("es", "Revisá los campos marcados.")]
    [InlineData("es-AR", "Revisá los campos marcados.")]
    [InlineData("en", "Check the highlighted fields.")]
    [InlineData("en-US", "Check the highlighted fields.")]
    public void Error_code_is_translated_to_the_current_culture(string culture, string expected)
    {
        using var scope = new CultureScope(culture);

        Assert.Equal(expected, ErrorMessages.Find(ValidationError.ErrorCode));
    }

    [Fact]
    public void Unknown_code_is_not_found()
    {
        Assert.Null(ErrorMessages.Find("Test.Unknown.Code"));
        Assert.Equal("Test.Unknown.Code", ErrorMessages.Get("Test.Unknown.Code"));
    }

    [Theory]
    [InlineData("es", ErrorType.NotFound, "No encontrado")]
    [InlineData("en", ErrorType.NotFound, "Not found")]
    [InlineData("es", ErrorType.Validation, "Datos inválidos")]
    [InlineData("en", ErrorType.Failure, "Server error")]
    public void Title_depends_on_the_error_type(string culture, ErrorType type, string expected)
    {
        using var scope = new CultureScope(culture);

        Assert.Equal(expected, ErrorMessages.Title(type));
    }
}
```

`tests/ArquitecturaBase.Application.UnitTests/Resources/ValidationMessagesTests.cs`:

```csharp
using ArquitecturaBase.Application.Resources;

namespace ArquitecturaBase.Application.UnitTests.Resources;

public sealed class ValidationMessagesTests
{
    [Fact]
    public void Messages_are_in_spanish_by_default()
    {
        using var scope = new CultureScope("es");

        Assert.Equal("Este campo es obligatorio.", ValidationMessages.Required);
        Assert.Equal("Ingresá un correo válido.", ValidationMessages.EmailInvalid);
    }

    [Fact]
    public void Messages_are_translated_to_english()
    {
        using var scope = new CultureScope("en");

        Assert.Equal("This field is required.", ValidationMessages.Required);
        Assert.Equal("Enter a valid email address.", ValidationMessages.EmailInvalid);
    }
}
```

`tests/ArquitecturaBase.Application.UnitTests/Resources/ResourceParityTests.cs`:

```csharp
using System.Collections;
using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Resources;

namespace ArquitecturaBase.Application.UnitTests.Resources;

/// <summary>Cada texto en español tiene su traducción al inglés y viceversa.</summary>
public sealed class ResourceParityTests
{
    [Fact]
    public void Errors_have_the_same_keys_in_spanish_and_english()
    {
        AssertSameKeys(ErrorMessages.ResourceManager);
    }

    [Fact]
    public void Validation_messages_have_the_same_keys_in_spanish_and_english()
    {
        AssertSameKeys(ValidationMessages.ResourceManager);
    }

    private static void AssertSameKeys(ResourceManager resourceManager)
    {
        var spanish = Keys(resourceManager, CultureInfo.InvariantCulture);
        var english = Keys(resourceManager, CultureInfo.GetCultureInfo("en"));

        Assert.NotEmpty(spanish);
        Assert.Equal(spanish, english);
    }

    private static string[] Keys(ResourceManager resourceManager, CultureInfo culture)
    {
        var resourceSet = resourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"No resources for culture '{culture.Name}'.");

        return resourceSet.Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
```

- [ ] **Paso 2: correr y ver que falla**

```bash
dotnet sln ArquitecturaBase.slnx add tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj
```

Esperado: FALLA la compilación (`CS0234: ... 'Resources' no existe`).

- [ ] **Paso 3: archivos .resx**

Todos usan este encabezado; cambian solo los `<data>`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <!-- data -->
</root>
```

`Resources/Errors.resx` (español):

```xml
  <data name="Title.Failure" xml:space="preserve"><value>Error del servidor</value></data>
  <data name="Title.Validation" xml:space="preserve"><value>Datos inválidos</value></data>
  <data name="Title.Unauthorized" xml:space="preserve"><value>No autenticado</value></data>
  <data name="Title.Forbidden" xml:space="preserve"><value>Acceso denegado</value></data>
  <data name="Title.NotFound" xml:space="preserve"><value>No encontrado</value></data>
  <data name="Title.Conflict" xml:space="preserve"><value>Conflicto</value></data>
  <data name="Title.TooManyRequests" xml:space="preserve"><value>Demasiadas solicitudes</value></data>
  <data name="Validation.Failed" xml:space="preserve"><value>Revisá los campos marcados.</value></data>
  <data name="Request.Invalid" xml:space="preserve"><value>La solicitud tiene un formato inválido.</value></data>
  <data name="General.Unexpected" xml:space="preserve"><value>Ocurrió un error inesperado. Si el problema continúa, informá el código de seguimiento.</value></data>
```

`Resources/Errors.en.resx`:

```xml
  <data name="Title.Failure" xml:space="preserve"><value>Server error</value></data>
  <data name="Title.Validation" xml:space="preserve"><value>Invalid data</value></data>
  <data name="Title.Unauthorized" xml:space="preserve"><value>Unauthorized</value></data>
  <data name="Title.Forbidden" xml:space="preserve"><value>Forbidden</value></data>
  <data name="Title.NotFound" xml:space="preserve"><value>Not found</value></data>
  <data name="Title.Conflict" xml:space="preserve"><value>Conflict</value></data>
  <data name="Title.TooManyRequests" xml:space="preserve"><value>Too many requests</value></data>
  <data name="Validation.Failed" xml:space="preserve"><value>Check the highlighted fields.</value></data>
  <data name="Request.Invalid" xml:space="preserve"><value>The request has an invalid format.</value></data>
  <data name="General.Unexpected" xml:space="preserve"><value>An unexpected error occurred. If the problem persists, report the trace id.</value></data>
```

`Resources/Validation.resx` (los marcadores `{MaxLength}`, `{From}` y `{To}` los reemplaza FluentValidation):

```xml
  <data name="Required" xml:space="preserve"><value>Este campo es obligatorio.</value></data>
  <data name="MaxLength" xml:space="preserve"><value>Ingresá como máximo {MaxLength} caracteres.</value></data>
  <data name="EmailInvalid" xml:space="preserve"><value>Ingresá un correo válido.</value></data>
  <data name="PageInvalid" xml:space="preserve"><value>La página debe ser mayor o igual a 1.</value></data>
  <data name="PageSizeInvalid" xml:space="preserve"><value>La cantidad por página debe estar entre {From} y {To}.</value></data>
  <data name="SortNotAllowed" xml:space="preserve"><value>No se puede ordenar por ese campo.</value></data>
```

`Resources/Validation.en.resx`:

```xml
  <data name="Required" xml:space="preserve"><value>This field is required.</value></data>
  <data name="MaxLength" xml:space="preserve"><value>Enter at most {MaxLength} characters.</value></data>
  <data name="EmailInvalid" xml:space="preserve"><value>Enter a valid email address.</value></data>
  <data name="PageInvalid" xml:space="preserve"><value>Page must be greater than or equal to 1.</value></data>
  <data name="PageSizeInvalid" xml:space="preserve"><value>Page size must be between {From} and {To}.</value></data>
  <data name="SortNotAllowed" xml:space="preserve"><value>Sorting by that field is not allowed.</value></data>
```

- [ ] **Paso 4: accesos a los resources**

`Resources/ErrorMessages.cs`:

```csharp
using System.Globalization;
using System.Resources;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Resources;

/// <summary>
/// Textos de Errors.resx. La clave de un error es su código (Area.Entidad.Motivo).
/// El idioma sale de <see cref="CultureInfo.CurrentUICulture"/>, que en la API fija RequestLocalization.
/// </summary>
public static class ErrorMessages
{
    internal static ResourceManager ResourceManager { get; } =
        new("ArquitecturaBase.Application.Resources.Errors", typeof(ErrorMessages).Assembly);

    public static string? Find(string key) => ResourceManager.GetString(key, CultureInfo.CurrentUICulture);

    public static string Get(string key) => Find(key) ?? key;

    public static string Title(ErrorType type) => Get("Title." + Enum.GetName(type));
}
```

`Resources/ValidationMessages.cs`:

```csharp
using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Application.Resources;

/// <summary>Mensajes de Validation.resx en el idioma de la petición.</summary>
public static class ValidationMessages
{
    internal static ResourceManager ResourceManager { get; } =
        new("ArquitecturaBase.Application.Resources.Validation", typeof(ValidationMessages).Assembly);

    public static string Required => Get(nameof(Required));

    public static string MaxLength => Get(nameof(MaxLength));

    public static string EmailInvalid => Get(nameof(EmailInvalid));

    public static string PageInvalid => Get(nameof(PageInvalid));

    public static string PageSizeInvalid => Get(nameof(PageSizeInvalid));

    public static string SortNotAllowed => Get(nameof(SortNotAllowed));

    private static string Get(string key) => ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
```

- [ ] **Paso 5: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: `total: 13`, `correcto: 13`.

- [ ] **Paso 6: commit**

```bash
git add ArquitecturaBase.slnx src/ArquitecturaBase.Application tests/ArquitecturaBase.Application.UnitTests
git commit -m "feat: agregar resources de errores y validación en español e inglés" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 9: Paginado: PagedRequest, PagedResult y SortDescriptor

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Common/Pagination/PagedRequest.cs`, `PagedResult.cs`, `SortDescriptor.cs`
- Test: `tests/ArquitecturaBase.Application.UnitTests/Common/Pagination/SortDescriptorTests.cs`, `PagedResultTests.cs`

- [ ] **Paso 1: tests que fallan**

`Common/Pagination/SortDescriptorTests.cs`:

```csharp
using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.UnitTests.Common.Pagination;

public sealed class SortDescriptorTests
{
    [Theory]
    [InlineData("name", "name", false)]
    [InlineData("-createdAtUtc", "createdAtUtc", true)]
    [InlineData("  -name  ", "name", true)]
    public void Parses_field_and_direction(string sort, string field, bool descending)
    {
        var descriptor = SortDescriptor.Parse(sort);

        Assert.Equal(new SortDescriptor(field, descending), descriptor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-")]
    public void Returns_null_when_there_is_no_field(string? sort)
    {
        Assert.Null(SortDescriptor.Parse(sort));
    }
}
```

`Common/Pagination/PagedResultTests.cs`:

```csharp
using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.UnitTests.Common.Pagination;

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(1, 20, 0, 0, false, false)]
    [InlineData(1, 20, 20, 1, false, false)]
    [InlineData(1, 20, 21, 2, false, true)]
    [InlineData(2, 10, 25, 3, true, true)]
    [InlineData(3, 10, 25, 3, true, false)]
    public void Computes_pages(int page, int pageSize, int totalCount, int totalPages, bool hasPrevious, bool hasNext)
    {
        var result = new PagedResult<string>([], page, pageSize, totalCount);

        Assert.Equal(totalPages, result.TotalPages);
        Assert.Equal(hasPrevious, result.HasPrevious);
        Assert.Equal(hasNext, result.HasNext);
    }

    [Fact]
    public void Paged_request_has_the_spec_defaults()
    {
        var request = new SampleRequest();

        Assert.Equal(1, request.Page);
        Assert.Equal(20, request.PageSize);
        Assert.Null(request.Sort);
        Assert.Null(request.Search);
        Assert.Equal(100, PagedRequest.MaxPageSize);
    }

    private sealed record SampleRequest : PagedRequest;
}
```

- [ ] **Paso 2: correr y ver que falla**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: FALLA la compilación (`CS0234: ... 'Pagination' no existe`).

- [ ] **Paso 3: implementación**

`Common/Pagination/PagedRequest.cs`:

```csharp
namespace ArquitecturaBase.Application.Common.Pagination;

/// <summary>
/// Base de las consultas paginadas: <c>?page=2&amp;pageSize=20&amp;sort=-createdAtUtc&amp;search=juan</c>.
/// Cada consulta declara su lista blanca de campos ordenables y la valida con PagedRequestValidator.
/// </summary>
public abstract record PagedRequest
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = DefaultPage;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Campo por el que se ordena; con "-" adelante, descendente.</summary>
    public string? Sort { get; init; }

    public string? Search { get; init; }
}
```

`Common/Pagination/PagedResult.cs`:

```csharp
namespace ArquitecturaBase.Application.Common.Pagination;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;
}
```

`Common/Pagination/SortDescriptor.cs`:

```csharp
namespace ArquitecturaBase.Application.Common.Pagination;

public sealed record SortDescriptor(string Field, bool Descending)
{
    /// <summary>Interpreta "campo" o "-campo". Devuelve null si no hay campo.</summary>
    public static SortDescriptor? Parse(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return null;
        }

        var trimmed = sort.Trim();
        var descending = trimmed.StartsWith('-');
        var field = (descending ? trimmed[1..] : trimmed).Trim();

        return field.Length == 0 ? null : new SortDescriptor(field, descending);
    }
}
```

- [ ] **Paso 4: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: `total: 26`, `correcto: 26`.

- [ ] **Paso 5: commit**

```bash
git add src/ArquitecturaBase.Application/Common/Pagination tests/ArquitecturaBase.Application.UnitTests/Common/Pagination
git commit -m "feat: agregar PagedRequest, PagedResult y SortDescriptor" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 10: Reglas de validación comunes

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Common/Validation/ValidationRules.cs`, `PagedRequestValidator.cs`
- Modificar: `src/ArquitecturaBase.Application/ArquitecturaBase.Application.csproj`, `Directory.Packages.props`
- Test: `tests/ArquitecturaBase.Application.UnitTests/Common/Validation/ValidationRulesTests.cs`, `PagedRequestValidatorTests.cs`

- [ ] **Paso 1: paquete**

`Directory.Packages.props`:

```xml
  <ItemGroup Label="Application">
    <PackageVersion Include="FluentValidation" Version="12.1.1" />
  </ItemGroup>
```

Application csproj:

```xml
  <ItemGroup>
    <PackageReference Include="FluentValidation" />
  </ItemGroup>
```

- [ ] **Paso 2: tests que fallan**

`Common/Validation/ValidationRulesTests.cs`:

```csharp
using ArquitecturaBase.Application.Common.Validation;
using FluentValidation;

namespace ArquitecturaBase.Application.UnitTests.Common.Validation;

public sealed class ValidationRulesTests
{
    private sealed record SignUp(string? Name, string? Email);

    private sealed class SignUpValidator : AbstractValidator<SignUp>
    {
        public SignUpValidator()
        {
            RuleFor(signUp => signUp.Name).Required().MaxLength(5);
            RuleFor(signUp => signUp.Email).ValidEmail();
        }
    }

    [Fact]
    public void Required_uses_the_translated_message()
    {
        using var culture = new CultureScope("es");

        var result = new SignUpValidator().Validate(new SignUp("", "ana@example.com"));

        var failure = Assert.Single(result.Errors);
        Assert.Equal("Name", failure.PropertyName);
        Assert.Equal("Este campo es obligatorio.", failure.ErrorMessage);
    }

    [Fact]
    public void Max_length_message_includes_the_limit()
    {
        using var culture = new CultureScope("en");

        var result = new SignUpValidator().Validate(new SignUp("abcdefgh", "ana@example.com"));

        Assert.Equal("Enter at most 5 characters.", Assert.Single(result.Errors).ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Missing_email_only_reports_required(string? email)
    {
        using var culture = new CultureScope("es");

        var result = new SignUpValidator().Validate(new SignUp("Ana", email));

        Assert.Equal("Este campo es obligatorio.", Assert.Single(result.Errors).ErrorMessage);
    }

    [Theory]
    [InlineData("ana")]
    [InlineData("ana@")]
    public void Invalid_email_is_rejected(string email)
    {
        using var culture = new CultureScope("es");

        var result = new SignUpValidator().Validate(new SignUp("Ana", email));

        Assert.Equal("Ingresá un correo válido.", Assert.Single(result.Errors).ErrorMessage);
    }

    [Fact]
    public void Valid_data_passes()
    {
        Assert.True(new SignUpValidator().Validate(new SignUp("Ana", "ana@example.com")).IsValid);
    }
}
```

`Common/Validation/PagedRequestValidatorTests.cs`:

```csharp
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;

namespace ArquitecturaBase.Application.UnitTests.Common.Validation;

public sealed class PagedRequestValidatorTests
{
    private sealed record ProductsQuery : PagedRequest;

    private sealed class ProductsQueryValidator() : PagedRequestValidator<ProductsQuery>(["name", "createdAtUtc"]);

    private static readonly ProductsQueryValidator Validator = new();

    [Fact]
    public void Defaults_are_valid()
    {
        Assert.True(Validator.Validate(new ProductsQuery()).IsValid);
    }

    [Fact]
    public void Page_below_one_is_rejected()
    {
        using var culture = new CultureScope("es");

        var result = Validator.Validate(new ProductsQuery { Page = 0 });

        var failure = Assert.Single(result.Errors);
        Assert.Equal("Page", failure.PropertyName);
        Assert.Equal("La página debe ser mayor o igual a 1.", failure.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Page_size_out_of_range_is_rejected(int pageSize)
    {
        using var culture = new CultureScope("es");

        var result = Validator.Validate(new ProductsQuery { PageSize = pageSize });

        Assert.Equal("La cantidad por página debe estar entre 1 y 100.", Assert.Single(result.Errors).ErrorMessage);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("-createdAtUtc")]
    [InlineData("NAME")]
    public void Whitelisted_sort_is_accepted(string sort)
    {
        Assert.True(Validator.Validate(new ProductsQuery { Sort = sort }).IsValid);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("-")]
    public void Sort_outside_the_whitelist_is_rejected(string sort)
    {
        using var culture = new CultureScope("es");

        var result = Validator.Validate(new ProductsQuery { Sort = sort });

        var failure = Assert.Single(result.Errors);
        Assert.Equal("Sort", failure.PropertyName);
        Assert.Equal("No se puede ordenar por ese campo.", failure.ErrorMessage);
    }
}
```

- [ ] **Paso 3: correr y ver que falla**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: FALLA la compilación (`CS0234: ... 'Validation' no existe`).

- [ ] **Paso 4: implementación**

`Common/Validation/ValidationRules.cs`:

```csharp
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Common.Validation;

/// <summary>Reglas reutilizables con mensajes traducidos desde Validation.resx.</summary>
public static class ValidationRules
{
    public const int EmailMaxLength = 254;

    public static IRuleBuilderOptions<T, TProperty> Required<T, TProperty>(this IRuleBuilder<T, TProperty> ruleBuilder) =>
        ruleBuilder.NotEmpty().WithMessage(_ => ValidationMessages.Required);

    public static IRuleBuilderOptions<T, string?> MaxLength<T>(this IRuleBuilder<T, string?> ruleBuilder, int maxLength) =>
        ruleBuilder.MaximumLength(maxLength).WithMessage(_ => ValidationMessages.MaxLength);

    /// <summary>Obligatorio y con formato de correo. Va primero en la cadena: <c>RuleFor(x => x.Email).ValidEmail()</c>.</summary>
    public static IRuleBuilderOptions<T, string?> ValidEmail<T>(this IRuleBuilderInitial<T, string?> ruleBuilder) =>
        ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(_ => ValidationMessages.Required)
            .MaximumLength(EmailMaxLength).WithMessage(_ => ValidationMessages.EmailInvalid)
            .EmailAddress().WithMessage(_ => ValidationMessages.EmailInvalid);
}
```

`Common/Validation/PagedRequestValidator.cs`:

```csharp
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Common.Validation;

/// <summary>
/// Valida página, tamaño y orden. Cada consulta pasa su lista blanca de campos ordenables:
/// <c>internal sealed class GetUsersQueryValidator() : PagedRequestValidator&lt;GetUsersQuery&gt;(GetUsersQuery.SortableFields);</c>
/// </summary>
public abstract class PagedRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : PagedRequest
{
    protected PagedRequestValidator(IReadOnlyCollection<string> sortableFields)
    {
        ArgumentNullException.ThrowIfNull(sortableFields);

        RuleFor(request => request.Page)
            .GreaterThanOrEqualTo(1).WithMessage(_ => ValidationMessages.PageInvalid);

        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, PagedRequest.MaxPageSize).WithMessage(_ => ValidationMessages.PageSizeInvalid);

        RuleFor(request => request.Sort)
            .Must(sort => IsSortable(sort, sortableFields)).WithMessage(_ => ValidationMessages.SortNotAllowed);
    }

    private static bool IsSortable(string? sort, IReadOnlyCollection<string> sortableFields)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return true;
        }

        var descriptor = SortDescriptor.Parse(sort);

        return descriptor is not null && sortableFields.Contains(descriptor.Field, StringComparer.OrdinalIgnoreCase);
    }
}
```

- [ ] **Paso 5: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: `total: 42`, `correcto: 42`.

- [ ] **Paso 6: commit**

```bash
git add Directory.Packages.props src/ArquitecturaBase.Application tests/ArquitecturaBase.Application.UnitTests/Common/Validation
git commit -m "feat: agregar reglas de validación comunes y validador de paginado" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 11: Decoradores de validación, logging y unit of work

Orden del pipeline (de afuera hacia adentro): **Logging → Validation → UnitOfWork (solo comandos) → Handler**. Así se loguea todo, lo inválido no llega al handler y solo se guarda si el handler tuvo éxito.

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Abstractions/Behaviors/ValidationDecorator.cs`, `LoggingDecorator.cs`, `UnitOfWorkDecorator.cs`
- Modificar: `Directory.Packages.props`, Application csproj, Application.UnitTests csproj
- Crear: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Ping.cs`
- Test: `tests/ArquitecturaBase.Application.UnitTests/Abstractions/Behaviors/ValidationDecoratorTests.cs`, `LoggingDecoratorTests.cs`, `UnitOfWorkDecoratorTests.cs`

- [ ] **Paso 1: paquetes**

`Directory.Packages.props`: agregar en `Application`:

```xml
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.12" />
```

y en `Tests`:

```xml
    <PackageVersion Include="Microsoft.Extensions.Diagnostics.Testing" Version="10.10.0" />
```

Application csproj: `<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />` (trae el generador de `[LoggerMessage]`).
Application.UnitTests csproj:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Diagnostics.Testing" />
  </ItemGroup>
```

- [ ] **Paso 2: dobles de prueba**

`tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Ping.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Persistence;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Domain.Results;
using FluentValidation;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles;

internal sealed record PingCommand(string? Message) : ICommand<string>;

internal sealed record PingBaseCommand(string? Message) : ICommand;

internal sealed record PingQuery(string? Message) : IQuery<string>;

internal static class PingErrors
{
    public const string FailMessage = "fail";

    public static readonly Error Failed = Error.Conflict("Test.Ping.Failed", "Ping failed.");
}

internal sealed class PingCommandValidator : AbstractValidator<PingCommand>
{
    public PingCommandValidator() => RuleFor(command => command.Message).Required();
}

internal sealed class PingBaseCommandValidator : AbstractValidator<PingBaseCommand>
{
    public PingBaseCommandValidator() => RuleFor(command => command.Message).Required();
}

internal sealed class PingQueryValidator : AbstractValidator<PingQuery>
{
    public PingQueryValidator() => RuleFor(query => query.Message).Required();
}

internal sealed class PingCommandHandler : ICommandHandler<PingCommand, string>
{
    public int Calls { get; private set; }

    public Task<Result<string>> Handle(PingCommand command, CancellationToken cancellationToken)
    {
        Calls++;

        return Task.FromResult<Result<string>>(
            command.Message == PingErrors.FailMessage ? PingErrors.Failed : "pong: " + command.Message);
    }
}

internal sealed class PingBaseCommandHandler : ICommandHandler<PingBaseCommand>
{
    public int Calls { get; private set; }

    public Task<Result> Handle(PingBaseCommand command, CancellationToken cancellationToken)
    {
        Calls++;

        return Task.FromResult(
            command.Message == PingErrors.FailMessage ? Result.Failure(PingErrors.Failed) : Result.Success());
    }
}

internal sealed class PingQueryHandler : IQueryHandler<PingQuery, string>
{
    public Task<Result<string>> Handle(PingQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success("pong: " + query.Message));
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCalls++;
        return Task.FromResult(1);
    }
}
```

- [ ] **Paso 3: tests que fallan**

`Abstractions/Behaviors/ValidationDecoratorTests.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Behaviors;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Results;
using FluentValidation;

namespace ArquitecturaBase.Application.UnitTests.Abstractions.Behaviors;

public sealed class ValidationDecoratorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Invalid_command_returns_a_validation_error_without_calling_the_handler()
    {
        using var culture = new CultureScope("es");
        var handler = new PingCommandHandler();
        var decorator = new ValidationDecorator.CommandHandler<PingCommand, string>(handler, [new PingCommandValidator()]);

        var result = await decorator.Handle(new PingCommand(""), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal("Este campo es obligatorio.", Assert.Single(error.Errors["message"]));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Valid_command_reaches_the_handler()
    {
        var handler = new PingCommandHandler();
        var decorator = new ValidationDecorator.CommandHandler<PingCommand, string>(handler, [new PingCommandValidator()]);

        var result = await decorator.Handle(new PingCommand("hola"), Ct);

        Assert.Equal("pong: hola", result.Value);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Without_validators_the_handler_runs()
    {
        var handler = new PingCommandHandler();
        var decorator = new ValidationDecorator.CommandHandler<PingCommand, string>(handler, []);

        var result = await decorator.Handle(new PingCommand(""), Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Messages_from_every_validator_are_grouped_by_camel_case_field()
    {
        using var culture = new CultureScope("es");
        var tooShort = new InlineValidator<PingCommand>();
        tooShort.RuleFor(command => command.Message).Must(message => message is { Length: > 3 }).WithMessage("Muy corto.");
        var decorator = new ValidationDecorator.CommandHandler<PingCommand, string>(
            new PingCommandHandler(), [new PingCommandValidator(), tooShort]);

        var result = await decorator.Handle(new PingCommand(""), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(["message"], error.Errors.Keys);
        Assert.Equal(["Este campo es obligatorio.", "Muy corto."], error.Errors["message"]);
    }

    [Fact]
    public async Task Invalid_command_without_response_returns_a_validation_error()
    {
        var handler = new PingBaseCommandHandler();
        var decorator = new ValidationDecorator.CommandBaseHandler<PingBaseCommand>(handler, [new PingBaseCommandValidator()]);

        var result = await decorator.Handle(new PingBaseCommand(null), Ct);

        Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Invalid_query_returns_a_validation_error()
    {
        var decorator = new ValidationDecorator.QueryHandler<PingQuery, string>(new PingQueryHandler(), [new PingQueryValidator()]);

        var result = await decorator.Handle(new PingQuery(" "), Ct);

        Assert.IsType<ValidationError>(result.Error);
    }
}
```

Nota: si `Assert.Equal([..], ...)` es ambiguo, usar `new[] { ... }` como valor esperado.

`Abstractions/Behaviors/LoggingDecoratorTests.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Behaviors;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Application.UnitTests.Abstractions.Behaviors;

public sealed class LoggingDecoratorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Successful_command_logs_start_and_end()
    {
        var logger = new FakeLogger<PingCommand>();
        var decorator = new LoggingDecorator.CommandHandler<PingCommand, string>(new PingCommandHandler(), logger);

        var result = await decorator.Handle(new PingCommand("hola"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["Handling PingCommand", "Handled PingCommand"],
            logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Failed_command_logs_a_warning_with_the_error_code()
    {
        var logger = new FakeLogger<PingCommand>();
        var decorator = new LoggingDecorator.CommandHandler<PingCommand, string>(new PingCommandHandler(), logger);

        await decorator.Handle(new PingCommand(PingErrors.FailMessage), Ct);

        Assert.Contains(
            logger.Collector.GetSnapshot(),
            record => record.Level == LogLevel.Warning && record.Message == "PingCommand failed with Test.Ping.Failed");
    }

    [Fact]
    public async Task Query_is_logged()
    {
        var logger = new FakeLogger<PingQuery>();
        var decorator = new LoggingDecorator.QueryHandler<PingQuery, string>(new PingQueryHandler(), logger);

        await decorator.Handle(new PingQuery("hola"), Ct);

        Assert.Equal(2, logger.Collector.Count);
    }
}
```

`Abstractions/Behaviors/UnitOfWorkDecoratorTests.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Behaviors;
using ArquitecturaBase.Application.UnitTests.TestDoubles;

namespace ArquitecturaBase.Application.UnitTests.Abstractions.Behaviors;

public sealed class UnitOfWorkDecoratorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Successful_command_saves_changes_once()
    {
        var unitOfWork = new FakeUnitOfWork();
        var decorator = new UnitOfWorkDecorator.CommandHandler<PingCommand, string>(new PingCommandHandler(), unitOfWork);

        await decorator.Handle(new PingCommand("hola"), Ct);

        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Failed_command_does_not_save()
    {
        var unitOfWork = new FakeUnitOfWork();
        var decorator = new UnitOfWorkDecorator.CommandHandler<PingCommand, string>(new PingCommandHandler(), unitOfWork);

        var result = await decorator.Handle(new PingCommand(PingErrors.FailMessage), Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Successful_command_without_response_saves_changes_once()
    {
        var unitOfWork = new FakeUnitOfWork();
        var decorator = new UnitOfWorkDecorator.CommandBaseHandler<PingBaseCommand>(new PingBaseCommandHandler(), unitOfWork);

        await decorator.Handle(new PingBaseCommand("hola"), Ct);

        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Failed_command_without_response_does_not_save()
    {
        var unitOfWork = new FakeUnitOfWork();
        var decorator = new UnitOfWorkDecorator.CommandBaseHandler<PingBaseCommand>(new PingBaseCommandHandler(), unitOfWork);

        await decorator.Handle(new PingBaseCommand(PingErrors.FailMessage), Ct);

        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }
}
```

- [ ] **Paso 4: correr y ver que falla**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: FALLA la compilación (`CS0234: ... 'Behaviors' no existe`).

- [ ] **Paso 5: implementación**

`Abstractions/Behaviors/ValidationDecorator.cs`:

```csharp
using System.Text.Json;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using FluentValidation;
using FluentValidation.Results;

namespace ArquitecturaBase.Application.Abstractions.Behaviors;

/// <summary>Corre los validadores antes del handler. Si fallan, devuelve un <see cref="ValidationError"/>.</summary>
internal static class ValidationDecorator
{
    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var error = await ValidateAsync(command, validators, cancellationToken);

            return error is null
                ? await inner.Handle(command, cancellationToken)
                : Result.Failure<TResponse>(error);
        }
    }

    internal sealed class CommandBaseHandler<TCommand>(
        ICommandHandler<TCommand> inner,
        IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var error = await ValidateAsync(command, validators, cancellationToken);

            return error is null
                ? await inner.Handle(command, cancellationToken)
                : Result.Failure(error);
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> inner,
        IEnumerable<IValidator<TQuery>> validators)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken)
        {
            var error = await ValidateAsync(query, validators, cancellationToken);

            return error is null
                ? await inner.Handle(query, cancellationToken)
                : Result.Failure<TResponse>(error);
        }
    }

    private static async Task<ValidationError?> ValidateAsync<TRequest>(
        TRequest request,
        IEnumerable<IValidator<TRequest>> validators,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);
        var failures = new List<ValidationFailure>();

        // En serie: el mismo contexto no se comparte entre validaciones concurrentes.
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            return null;
        }

        var errors = failures
            .GroupBy(failure => ToFieldName(failure.PropertyName), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        return new ValidationError(errors);
    }

    // Los campos viajan como en el JSON: "Address.Street" → "address.street".
    private static string ToFieldName(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}
```

`Abstractions/Behaviors/LoggingDecorator.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Abstractions.Behaviors;

/// <summary>Registra el inicio y el final de cada caso de uso, con el código de error si falla.</summary>
internal static partial class LoggingDecorator
{
    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        ILogger<TCommand> logger)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var requestName = typeof(TCommand).Name;
            LogHandling(logger, requestName);

            var result = await inner.Handle(command, cancellationToken);

            LogOutcome(logger, requestName, result);
            return result;
        }
    }

    internal sealed class CommandBaseHandler<TCommand>(
        ICommandHandler<TCommand> inner,
        ILogger<TCommand> logger)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var requestName = typeof(TCommand).Name;
            LogHandling(logger, requestName);

            var result = await inner.Handle(command, cancellationToken);

            LogOutcome(logger, requestName, result);
            return result;
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> inner,
        ILogger<TQuery> logger)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken)
        {
            var requestName = typeof(TQuery).Name;
            LogHandling(logger, requestName);

            var result = await inner.Handle(query, cancellationToken);

            LogOutcome(logger, requestName, result);
            return result;
        }
    }

    private static void LogOutcome(ILogger logger, string requestName, Result result)
    {
        if (result.IsSuccess)
        {
            LogHandled(logger, requestName);
        }
        else
        {
            LogFailed(logger, requestName, result.Error.Code);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {RequestName}")]
    private static partial void LogHandling(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {RequestName}")]
    private static partial void LogHandled(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{RequestName} failed with {ErrorCode}")]
    private static partial void LogFailed(ILogger logger, string requestName, string errorCode);
}
```

`Abstractions/Behaviors/UnitOfWorkDecorator.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Persistence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Abstractions.Behaviors;

/// <summary>Guarda los cambios solo si el comando terminó bien. Las consultas no pasan por acá.</summary>
internal static class UnitOfWorkDecorator
{
    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        IUnitOfWork unitOfWork)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var result = await inner.Handle(command, cancellationToken);

            if (result.IsSuccess)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }

    internal sealed class CommandBaseHandler<TCommand>(
        ICommandHandler<TCommand> inner,
        IUnitOfWork unitOfWork)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var result = await inner.Handle(command, cancellationToken);

            if (result.IsSuccess)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }
}
```

- [ ] **Paso 6: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: `total: 55`, `correcto: 55`.

- [ ] **Paso 7: commit**

```bash
git add Directory.Packages.props src/ArquitecturaBase.Application tests/ArquitecturaBase.Application.UnitTests
git commit -m "feat: agregar decoradores de validación, logging y unit of work" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 12: AddApplication con Scrutor

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/DependencyInjection.cs`
- Modificar: `Directory.Packages.props`, Application csproj, Application.UnitTests csproj
- Test: `tests/ArquitecturaBase.Application.UnitTests/DependencyInjectionTests.cs`

- [ ] **Paso 1: paquetes**

`Directory.Packages.props`, en `Application`:

```xml
    <PackageVersion Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
    <PackageVersion Include="Scrutor" Version="7.0.0" />
```

y en `Tests`:

```xml
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Logging" Version="10.0.12" />
```

Application csproj: `<PackageReference Include="FluentValidation.DependencyInjectionExtensions" />` y `<PackageReference Include="Scrutor" />`.
Application.UnitTests csproj: `<PackageReference Include="Microsoft.Extensions.DependencyInjection" />` y `<PackageReference Include="Microsoft.Extensions.Logging" />`.

- [ ] **Paso 2: tests que fallan**

`tests/ArquitecturaBase.Application.UnitTests/DependencyInjectionTests.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Behaviors;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Persistence;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests;

public sealed class DependencyInjectionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Application_without_handlers_can_be_registered()
    {
        var services = new ServiceCollection();

        var exception = Record.Exception(() => services.AddApplication());

        Assert.Null(exception);
    }

    [Fact]
    public void Command_handler_is_wrapped_with_logging_as_the_outermost_decorator()
    {
        using var provider = BuildProvider(new FakeUnitOfWork());
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<PingCommand, string>>();

        Assert.IsType<LoggingDecorator.CommandHandler<PingCommand, string>>(handler);
    }

    [Fact]
    public async Task Invalid_command_is_rejected_before_saving()
    {
        var unitOfWork = new FakeUnitOfWork();
        using var provider = BuildProvider(unitOfWork);
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<PingCommand, string>>();

        var result = await handler.Handle(new PingCommand(""), Ct);

        Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Successful_command_is_saved_once()
    {
        var unitOfWork = new FakeUnitOfWork();
        using var provider = BuildProvider(unitOfWork);
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<PingBaseCommand>>();

        var result = await handler.Handle(new PingBaseCommand("hola"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Queries_are_validated_and_never_saved()
    {
        var unitOfWork = new FakeUnitOfWork();
        using var provider = BuildProvider(unitOfWork);
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<PingQuery, string>>();

        var invalid = await handler.Handle(new PingQuery(""), Ct);
        var valid = await handler.Handle(new PingQuery("hola"), Ct);

        Assert.IsType<ValidationError>(invalid.Error);
        Assert.Equal("pong: hola", valid.Value);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    private static ServiceProvider BuildProvider(FakeUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IUnitOfWork>(unitOfWork);

        services.AddApplication();
        services.AddFeaturesFromAssembly(typeof(DependencyInjectionTests).Assembly);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
```

El primer test comprueba que `AddApplication()` no lanza aunque Application todavía no tenga handlers (con `Decorate` en lugar de `TryDecorate` fallaría).

- [ ] **Paso 3: correr y ver que falla**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: FALLA la compilación (`CS1061: ... no contiene una definición para 'AddApplication'`).

- [ ] **Paso 4: implementación**

`src/ArquitecturaBase.Application/DependencyInjection.cs`:

```csharp
using System.Reflection;
using ArquitecturaBase.Application.Abstractions.Behaviors;
using ArquitecturaBase.Application.Abstractions.Messaging;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services) =>
        services.AddFeaturesFromAssembly(typeof(DependencyInjection).Assembly);

    /// <summary>
    /// Registra los handlers y validadores de un ensamblado y envuelve los handlers con los decoradores
    /// (de afuera hacia adentro: logging → validación → unit of work → handler).
    /// Decora en una colección aparte para no volver a decorar lo que ya estaba registrado; así también
    /// sirve para los handlers que viven en el proyecto de tests de integración.
    /// </summary>
    public static IServiceCollection AddFeaturesFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var features = new FeatureServiceCollection();

        features.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<>)).Where(IsConcrete), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)).Where(IsConcrete), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)).Where(IsConcrete), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        // El último decorador aplicado queda afuera. TryDecorate no falla si el ensamblado no tiene handlers.
        features.TryDecorate(typeof(ICommandHandler<>), typeof(UnitOfWorkDecorator.CommandBaseHandler<>));
        features.TryDecorate(typeof(ICommandHandler<,>), typeof(UnitOfWorkDecorator.CommandHandler<,>));

        features.TryDecorate(typeof(ICommandHandler<>), typeof(ValidationDecorator.CommandBaseHandler<>));
        features.TryDecorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator.CommandHandler<,>));
        features.TryDecorate(typeof(IQueryHandler<,>), typeof(ValidationDecorator.QueryHandler<,>));

        features.TryDecorate(typeof(ICommandHandler<>), typeof(LoggingDecorator.CommandBaseHandler<>));
        features.TryDecorate(typeof(ICommandHandler<,>), typeof(LoggingDecorator.CommandHandler<,>));
        features.TryDecorate(typeof(IQueryHandler<,>), typeof(LoggingDecorator.QueryHandler<,>));

        features.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        foreach (var descriptor in features)
        {
            services.Add(descriptor);
        }

        return services;
    }

    // Excluye los decoradores (genéricos abiertos) que también implementan las interfaces de handler.
    private static bool IsConcrete(Type type) => !type.IsGenericTypeDefinition;

    private sealed class FeatureServiceCollection : List<ServiceDescriptor>, IServiceCollection;
}
```

- [ ] **Paso 5: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj`
Esperado: `total: 60`, `correcto: 60`.

Run: `dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj`
Esperado: todos pasan (Application sigue sin depender de EF Core ni de ASP.NET Core).

- [ ] **Paso 6: commit**

```bash
git add Directory.Packages.props src/ArquitecturaBase.Application tests/ArquitecturaBase.Application.UnitTests
git commit -m "feat: registrar handlers y validadores con Scrutor y envolverlos con los decoradores" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 13: Infrastructure: DbContext, UnitOfWork, TimeProvider y migraciones

Sin lógica propia todavía (los interceptores y el paginado llegan con sus tests en las Tareas 19 y 20): se verifica con el build y los tests de arquitectura.

**Archivos:**
- Crear: `src/ArquitecturaBase.Infrastructure/Persistence/ApplicationDbContext.cs`, `UnitOfWork.cs`, `DatabaseMigrationExtensions.cs`
- Crear: `src/ArquitecturaBase.Infrastructure/DependencyInjection.cs`
- Modificar: `Directory.Packages.props`, Infrastructure csproj

- [ ] **Paso 1: paquetes**

`Directory.Packages.props`:

```xml
  <ItemGroup Label="Infrastructure">
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.12" />
  </ItemGroup>
```

Infrastructure csproj:

```xml
  <ItemGroup>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" />
  </ItemGroup>
```

- [ ] **Paso 2: DbContext y UnitOfWork**

`Persistence/ApplicationDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence;

/// <summary>
/// Contexto de EF Core. En la Fase 2 pasa a heredar de IdentityDbContext.
/// Toma de este ensamblado un IEntityTypeConfiguration por entidad.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Para contextos derivados, como el de los tests de integración.
    protected ApplicationDbContext(DbContextOptions options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
```

`Persistence/UnitOfWork.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Persistence;

namespace ArquitecturaBase.Infrastructure.Persistence;

internal sealed class UnitOfWork(ApplicationDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
```

`Persistence/DatabaseMigrationExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Persistence;

public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Aplica las migraciones pendientes. La Api lo llama al arrancar solo en desarrollo;
    /// en producción se usa un migration bundle desde el pipeline.
    /// </summary>
    public static async Task ApplyMigrationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
```

- [ ] **Paso 3: `DependencyInjection.cs`**

```csharp
using ArquitecturaBase.Application.Abstractions.Persistence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Nombre de la base en el AppHost: Aspire inyecta ConnectionStrings:appdb.</summary>
    public const string DatabaseConnectionName = "appdb";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) => options
            .UseNpgsql(GetConnectionString(configuration))
            .AddInterceptors(serviceProvider.GetServices<ISaveChangesInterceptor>()));

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }

    private static string GetConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(DatabaseConnectionName)
        ?? throw new InvalidOperationException(
            $"Missing connection string 'ConnectionStrings:{DatabaseConnectionName}'. Start the API from the AppHost.");
}
```

- [ ] **Paso 4: compilar y correr los tests de arquitectura**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj
```

Esperado: `0 Advertencia(s)`, `0 Errores`; tests de arquitectura en verde.

- [ ] **Paso 5: commit**

```bash
git add Directory.Packages.props src/ArquitecturaBase.Infrastructure
git commit -m "feat: agregar ApplicationDbContext, UnitOfWork, TimeProvider y migraciones al iniciar" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 14: Api: composición, IEndpoint, CurrentUser, localización y OpenAPI + Scalar

**Archivos:**
- Crear: `src/ArquitecturaBase.Api/Endpoints/IEndpoint.cs`, `EndpointExtensions.cs`
- Crear: `src/ArquitecturaBase.Api/Services/CurrentUser.cs`
- Crear: `src/ArquitecturaBase.Api/Localization/LocalizationExtensions.cs`
- Crear: `src/ArquitecturaBase.Api/OpenApi/OpenApiExtensions.cs`
- Crear: `src/ArquitecturaBase.Api/DependencyInjection.cs`
- Modificar: `src/ArquitecturaBase.Api/Program.cs`, Api csproj, `Directory.Packages.props`

- [ ] **Paso 1: paquetes**

`Directory.Packages.props`:

```xml
  <ItemGroup Label="Api">
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.12" />
    <PackageVersion Include="Scalar.AspNetCore" Version="2.17.5" />
  </ItemGroup>
```

Api csproj:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
    <PackageReference Include="Scalar.AspNetCore" />
  </ItemGroup>
```

- [ ] **Paso 2: registro automático de endpoints**

`Endpoints/IEndpoint.cs`:

```csharp
namespace ArquitecturaBase.Api.Endpoints;

/// <summary>Cada grupo de endpoints implementa esta interfaz y se registra solo (AddEndpoints + MapEndpoints).</summary>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
```

`Endpoints/EndpointExtensions.cs`:

```csharp
using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.Endpoints;

public static class EndpointExtensions
{
    /// <summary>Registra todas las clases <see cref="IEndpoint"/> de un ensamblado.</summary>
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var descriptors = assembly.DefinedTypes
            .Where(type => type is { IsAbstract: false, IsInterface: false } && type.IsAssignableTo(typeof(IEndpoint)))
            .Select(type => ServiceDescriptor.Transient(typeof(IEndpoint), type));

        services.TryAddEnumerable(descriptors);

        return services;
    }

    public static WebApplication MapEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        foreach (var endpoint in app.Services.GetServices<IEndpoint>())
        {
            endpoint.MapEndpoint(app);
        }

        return app;
    }
}
```

- [ ] **Paso 3: usuario actual**

`Services/CurrentUser.cs`:

```csharp
using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Application.Abstractions.Identity;

namespace ArquitecturaBase.Api.Services;

/// <summary>Usuario de la petición, leído de los claims: "sub" (tokens OIDC) o NameIdentifier (cookie).</summary>
internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            var value = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(value, CultureInfo.InvariantCulture, out var userId) ? userId : null;
        }
    }

    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
}
```

- [ ] **Paso 4: localización y OpenAPI**

`Localization/LocalizationExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Localization;

namespace ArquitecturaBase.Api.Localization;

internal static class LocalizationExtensions
{
    private static readonly string[] SupportedCultures = ["es", "en"];

    /// <summary>Español por defecto e inglés. El idioma sale de Accept-Language, que envía el front.</summary>
    public static IServiceCollection AddRequestLocalizationDefaults(this IServiceCollection services) =>
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture(SupportedCultures[0])
                .AddSupportedCultures(SupportedCultures)
                .AddSupportedUICultures(SupportedCultures);

            options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
            options.ApplyCurrentCultureToResponseHeaders = true;
        });
}
```

`OpenApi/OpenApiExtensions.cs`:

```csharp
using Scalar.AspNetCore;

namespace ArquitecturaBase.Api.OpenApi;

internal static class OpenApiExtensions
{
    /// <summary>Documento en /openapi/v1.json y referencia de Scalar en /scalar. Program.cs lo mapea solo en desarrollo.</summary>
    public static WebApplication MapOpenApiDocumentation(this WebApplication app)
    {
        app.MapOpenApi();
        app.MapScalarApiReference();

        return app;
    }
}
```

- [ ] **Paso 5: `DependencyInjection.cs` y `Program.cs`**

`src/ArquitecturaBase.Api/DependencyInjection.cs`:

```csharp
using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.Localization;
using ArquitecturaBase.Api.Services;
using ArquitecturaBase.Application.Abstractions.Identity;

namespace ArquitecturaBase.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        services.AddRequestLocalizationDefaults();
        services.AddOpenApi();

        services.AddEndpoints(typeof(DependencyInjection).Assembly);

        return services;
    }
}
```

`src/ArquitecturaBase.Api/Program.cs`:

```csharp
using ArquitecturaBase.Api;
using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application;
using ArquitecturaBase.Infrastructure;
using ArquitecturaBase.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddPresentation();

var app = builder.Build();

// Primero la localización: todo lo que sigue, incluidos los errores, sale en el idioma pedido.
app.UseRequestLocalization();

if (app.Environment.IsDevelopment())
{
    await app.Services.ApplyMigrationsAsync();
    app.MapOpenApiDocumentation();
}

app.MapDefaultEndpoints();
app.MapEndpoints();

await app.RunAsync();
```

- [ ] **Paso 6: compilar y correr los tests**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj
```

Esperado: `0 Advertencia(s)`, `0 Errores`; `Api_uses_infrastructure_only_from_the_composition_root` en verde (solo `Program` usa Infrastructure).

- [ ] **Paso 7: commit**

```bash
git add Directory.Packages.props src/ArquitecturaBase.Api
git commit -m "feat: componer la Api con registro automático de endpoints, localización y OpenAPI con Scalar" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 15: Result → ProblemDetails

Formato de la sección 6.1: `type`, `title` y `detail` traducidos, `status`, `code`, `traceId` y, en validaciones, `errors` (campo → mensajes). Estos tests no usan HTTP ni Docker.

**Archivos:**
- Crear: `src/ArquitecturaBase.Api/ErrorHandling/ProblemDetailsMapper.cs`, `ResultExtensions.cs`
- Modificar: `src/ArquitecturaBase.Api/DependencyInjection.cs`, Api csproj (InternalsVisibleTo), `Directory.Packages.props`, `ArquitecturaBase.slnx`
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`, `Support/CultureScope.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/ErrorHandling/ProblemDetailsMapperTests.cs`

- [ ] **Paso 1: proyecto de tests**

`Directory.Packages.props`, en `Tests`:

```xml
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
```

Api csproj:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="ArquitecturaBase.Api.IntegrationTests" />
  </ItemGroup>
```

`tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ArquitecturaBase.Api\ArquitecturaBase.Api.csproj" />
  </ItemGroup>
</Project>
```

`tests/ArquitecturaBase.Api.IntegrationTests/Support/CultureScope.cs`:

```csharp
using System.Globalization;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>Cambia la cultura del test actual y la restaura al salir del using.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public CultureScope(string name)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
```

- [ ] **Paso 2: tests que fallan**

`tests/ArquitecturaBase.Api.IntegrationTests/ErrorHandling/ProblemDetailsMapperTests.cs`:

```csharp
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.ErrorHandling;

public sealed class ProblemDetailsMapperTests
{
    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.TooManyRequests, 429)]
    [InlineData(ErrorType.Failure, 500)]
    public void Error_type_maps_to_http_status(ErrorType type, int status)
    {
        Assert.Equal(status, ProblemDetailsMapper.ToStatusCode(type));
    }

    [Fact]
    public void Validation_error_maps_to_400_with_translated_texts_and_field_errors()
    {
        using var culture = new CultureScope("es");
        var errors = new Dictionary<string, string[]> { ["email"] = ["Ingresá un correo válido."] };

        var problem = ProblemDetailsMapper.FromError(new ValidationError(errors));

        Assert.Equal(400, problem.Status);
        Assert.Equal("Datos inválidos", problem.Title);
        Assert.Equal("Revisá los campos marcados.", problem.Detail);
        Assert.Equal("Validation.Failed", problem.Extensions["code"]);
        Assert.Same(errors, problem.Extensions["errors"]);
    }

    [Fact]
    public void Known_code_is_translated_to_english()
    {
        using var culture = new CultureScope("en");

        var problem = ProblemDetailsMapper.FromError(new ValidationError(new Dictionary<string, string[]>()));

        Assert.Equal("Invalid data", problem.Title);
        Assert.Equal("Check the highlighted fields.", problem.Detail);
    }

    [Fact]
    public void Unknown_code_falls_back_to_the_error_description()
    {
        using var culture = new CultureScope("en");

        var problem = ProblemDetailsMapper.FromError(Error.NotFound("Test.Widget.NotFound", "Widget not found."));

        Assert.Equal(404, problem.Status);
        Assert.Equal("Not found", problem.Title);
        Assert.Equal("Widget not found.", problem.Detail);
        Assert.Equal("Test.Widget.NotFound", problem.Extensions["code"]);
    }

    [Fact]
    public void Metadata_is_added_as_extensions()
    {
        var metadata = new Dictionary<string, object?> { ["attemptsLeft"] = 3 };

        var problem = ProblemDetailsMapper.FromError(Error.Validation("Auth.LoginCode.Invalid", "Invalid code.", metadata));

        Assert.Equal(3, problem.Extensions["attemptsLeft"]);
    }
}
```

- [ ] **Paso 3: correr y ver que falla**

```bash
dotnet sln ArquitecturaBase.slnx add tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj
```

Esperado: FALLA la compilación (`CS0234: ... 'ErrorHandling' no existe`).

- [ ] **Paso 4: implementación**

`src/ArquitecturaBase.Api/ErrorHandling/ProblemDetailsMapper.cs`:

```csharp
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.ErrorHandling;

/// <summary>
/// Convierte errores en ProblemDetails (RFC 9457) con el formato de la sección 6.1 del spec:
/// title y detail traducidos, code, errors en validaciones y traceId (lo agrega AddProblemDetails).
/// </summary>
internal static class ProblemDetailsMapper
{
    public const string CodeExtension = "code";
    public const string ErrorsExtension = "errors";

    public static int ToStatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static ProblemDetails FromError(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        // La descripción traducida se busca por código; si no hay traducción, queda la del error.
        var problem = Create(error.Type, error.Code, ErrorMessages.Find(error.Code) ?? error.Description);

        if (error is ValidationError validationError)
        {
            problem.Extensions[ErrorsExtension] = validationError.Errors;
        }

        if (error.Metadata is not null)
        {
            foreach (var (key, value) in error.Metadata)
            {
                problem.Extensions.TryAdd(key, value);
            }
        }

        return problem;
    }

    public static ProblemDetails Create(ErrorType type, string code, string detail, int? statusCode = null) => new()
    {
        Status = statusCode ?? ToStatusCode(type),
        Title = ErrorMessages.Title(type),
        Detail = detail,
        Extensions = { [CodeExtension] = code },
    };
}
```

`src/ArquitecturaBase.Api/ErrorHandling/ResultExtensions.cs`:

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.ErrorHandling;

public static class ResultExtensions
{
    /// <summary>204 si salió bien; si no, ProblemDetails.</summary>
    public static IResult ToHttpResult(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    /// <summary>200 con el valor si salió bien; si no, ProblemDetails.</summary>
    public static IResult ToHttpResult<TValue>(this Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    public static IResult ToProblem(this Error error) =>
        TypedResults.Problem(ProblemDetailsMapper.FromError(error));
}
```

En `src/ArquitecturaBase.Api/DependencyInjection.cs`, agregar `using System.Diagnostics;` y, dentro de `AddPresentation`, antes de `AddRequestLocalizationDefaults()`:

```csharp
        // Todas las respuestas de error llevan el traceId para buscarlas en el dashboard de Aspire.
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier));
```

- [ ] **Paso 5: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`
Esperado: `total: 11`, `correcto: 11`.

- [ ] **Paso 6: commit**

```bash
git add Directory.Packages.props ArquitecturaBase.slnx src/ArquitecturaBase.Api tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "feat: convertir Result en ProblemDetails traducido con code, traceId y errors" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 16: Arnés de integración, traducciones y diccionario de validación

A partir de acá hace falta **Docker Desktop encendido** (`docker version` debe responder con el servidor).

La entidad `Widget`, su `DbContext` derivado, los comandos/consultas y los endpoints `/test/...` existen **solo** en el proyecto de tests. Pasan por el mismo pipeline que el código de producción: `AddFeaturesFromAssembly` los decora con validación, logging y unit of work.

**Archivos:**
- Modificar: `Directory.Packages.props`, `tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`
- Crear en `tests/ArquitecturaBase.Api.IntegrationTests/`:
  - `Support/ApiFactory.cs`, `Support/ApiTestGroup.cs`, `Support/HttpExtensions.cs`
  - `TestFeatures/Widget.cs`, `TestFeatures/TestDbContext.cs`, `TestFeatures/TestEndpoints.cs`
  - `TestFeatures/Widgets/WidgetErrors.cs`, `CreateWidget.cs`, `GetWidgetById.cs`
- Test: `LocalizationTests.cs`, `ValidationProblemTests.cs`

- [ ] **Paso 1: paquetes**

`Directory.Packages.props`, en `Tests`:

```xml
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.15.0" />
    <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.10.0" />
```

IntegrationTests csproj, en el `ItemGroup` de paquetes:

```xml
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
```

- [ ] **Paso 2: entidad, DbContext y features de prueba**

`TestFeatures/Widget.cs`:

```csharp
using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

/// <summary>Entidad de prueba: existe solo en este proyecto.</summary>
public sealed class Widget : AggregateRoot, IAuditable, ISoftDeletable
{
    public const int NameMaxLength = 50;

    public Widget(string name)
    {
        Name = name;
    }

    // Para EF Core.
    private Widget()
    {
        Name = string.Empty;
    }

    public string Name { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public void Rename(string name) => Name = name;
}
```

`TestFeatures/TestDbContext.cs`:

```csharp
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

/// <summary>El ApplicationDbContext de producción más la tabla de Widgets.</summary>
public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : ApplicationDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>(widget =>
        {
            widget.ToTable("widgets");
            widget.Property(w => w.Name).HasMaxLength(Widget.NameMaxLength);
        });

        // Al final: la base aplica sus convenciones (por ejemplo, el filtro de soft delete) a todas las entidades.
        base.OnModelCreating(modelBuilder);
    }
}
```

`TestFeatures/Widgets/WidgetErrors.cs`:

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

public static class WidgetErrors
{
    // Sin traducción en Errors.resx a propósito: la API usa la descripción.
    public static readonly Error NotFound = Error.NotFound("Test.Widget.NotFound", "Widget not found.");
}
```

`TestFeatures/Widgets/CreateWidget.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence;
using FluentValidation;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

public sealed record CreateWidgetCommand(string? Name) : ICommand<Guid>;

internal sealed class CreateWidgetCommandValidator : AbstractValidator<CreateWidgetCommand>
{
    public CreateWidgetCommandValidator() =>
        RuleFor(command => command.Name).Required().MaxLength(Widget.NameMaxLength);
}

internal sealed class CreateWidgetCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateWidgetCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateWidgetCommand command, CancellationToken cancellationToken)
    {
        var widget = new Widget(command.Name!);
        dbContext.Set<Widget>().Add(widget);

        // Guarda el UnitOfWorkDecorator.
        return Task.FromResult(Result.Success(widget.Id));
    }
}
```

`TestFeatures/Widgets/GetWidgetById.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

public sealed record GetWidgetByIdQuery(Guid Id) : IQuery<WidgetDetailsResponse>;

public sealed record WidgetDetailsResponse(
    Guid Id,
    string Name,
    DateTime CreatedAtUtc,
    Guid? CreatedBy,
    DateTime? ModifiedAtUtc,
    Guid? ModifiedBy);

internal sealed class GetWidgetByIdQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetWidgetByIdQuery, WidgetDetailsResponse>
{
    public async Task<Result<WidgetDetailsResponse>> Handle(GetWidgetByIdQuery query, CancellationToken cancellationToken)
    {
        var widget = await dbContext.Set<Widget>()
            .AsNoTracking()
            .Where(w => w.Id == query.Id)
            .Select(w => new WidgetDetailsResponse(w.Id, w.Name, w.CreatedAtUtc, w.CreatedBy, w.ModifiedAtUtc, w.ModifiedBy))
            .SingleOrDefaultAsync(cancellationToken);

        return widget is null ? WidgetErrors.NotFound : widget;
    }
}
```

`TestFeatures/TestEndpoints.cs` (las Tareas 17, 18 y 20 le agregan rutas):

```csharp
using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;
using ArquitecturaBase.Application.Abstractions.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

/// <summary>Endpoints que existen solo en los tests.</summary>
internal sealed class TestEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/test");

        group.MapPost("/widgets", async (
                CreateWidgetCommand command,
                ICommandHandler<CreateWidgetCommand, Guid> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult());

        group.MapGet("/widgets/{id:guid}", async (
                Guid id,
                IQueryHandler<GetWidgetByIdQuery, WidgetDetailsResponse> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetWidgetByIdQuery(id), cancellationToken)).ToHttpResult());
    }
}
```

- [ ] **Paso 3: fábrica, grupo de tests y helpers HTTP**

`Support/ApiFactory.cs`:

```csharp
using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Application;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// La Api real contra un Postgres en contenedor, con un reloj controlable y las features de prueba
/// (entidad Widget y endpoints /test) que existen solo en este proyecto.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // La misma imagen que usa Aspire 13.5.4.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        // Sin migraciones en los tests: el esquema sale del modelo de TestDbContext.
        await ExecuteDbContextAsync(dbContext => dbContext.Database.EnsureCreatedAsync());
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing": no aplica migraciones ni mapea OpenAPI, que son solo de Development.
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.RemoveAll<ApplicationDbContext>();
            services.AddDbContext<ApplicationDbContext, TestDbContext>((serviceProvider, options) => options
                .UseNpgsql(_postgres.GetConnectionString())
                .AddInterceptors(serviceProvider.GetServices<ISaveChangesInterceptor>()));

            services.AddFeaturesFromAssembly(typeof(ApiFactory).Assembly);
            services.AddEndpoints(typeof(ApiFactory).Assembly);
        });
    }
}
```

`Support/ApiTestGroup.cs`:

```csharp
namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>Un solo contenedor y una sola Api para todos los tests HTTP; corren en serie porque comparten el reloj.</summary>
[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
```

`Support/HttpExtensions.cs`:

```csharp
using System.Text.Json;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

internal static class HttpExtensions
{
    public static async Task<HttpResponseMessage> SendAsync(
        this HttpClient client,
        HttpMethod method,
        string url,
        HttpContent? content = null,
        string? language = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative)) { Content = content };

        if (language is not null)
        {
            request.Headers.AcceptLanguage.ParseAdd(language);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }
}
```

- [ ] **Paso 4: tests**

`LocalizationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class LocalizationTests(ApiFactory factory)
{
    [Fact]
    public async Task Problem_is_in_spanish_without_accept_language()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Post, "/test/widgets", JsonContent.Create(new { name = "" }));
        var problem = await response.ReadJsonAsync();

        Assert.Equal("Datos inválidos", problem.GetProperty("title").GetString());
        Assert.Equal("Revisá los campos marcados.", problem.GetProperty("detail").GetString());
        Assert.Equal("Este campo es obligatorio.", problem.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Theory]
    [InlineData("en", "Invalid data", "Check the highlighted fields.", "This field is required.")]
    [InlineData("en-US", "Invalid data", "Check the highlighted fields.", "This field is required.")]
    [InlineData("es-AR", "Datos inválidos", "Revisá los campos marcados.", "Este campo es obligatorio.")]
    public async Task Problem_follows_accept_language(string language, string title, string detail, string fieldMessage)
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Post, "/test/widgets", JsonContent.Create(new { name = "" }), language);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.Equal(detail, problem.GetProperty("detail").GetString());
        Assert.Equal(fieldMessage, problem.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task Not_found_has_translated_title_code_and_content_language()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, $"/test/widgets/{Guid.CreateVersion7()}", language: "en");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Not found", problem.GetProperty("title").GetString());
        Assert.Equal("Widget not found.", problem.GetProperty("detail").GetString());
        Assert.Equal("Test.Widget.NotFound", problem.GetProperty("code").GetString());
        Assert.Equal("en", Assert.Single(response.Content.Headers.ContentLanguage));
    }
}
```

`ValidationProblemTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class ValidationProblemTests(ApiFactory factory)
{
    [Fact]
    public async Task Invalid_command_returns_400_problem_with_errors_by_field()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Post, "/test/widgets", JsonContent.Create(new { name = "" }));
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("Validation.Failed", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal("name", Assert.Single(problem.GetProperty("errors").EnumerateObject()).Name);
    }

    [Fact]
    public async Task Too_long_name_reports_the_limit()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Post, "/test/widgets", JsonContent.Create(new { name = new string('x', 51) }), "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(
            "Ingresá como máximo 50 caracteres.",
            problem.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task Valid_command_is_saved_and_can_be_read_back()
    {
        using var client = factory.CreateClient();

        using var created = await client.SendAsync(HttpMethod.Post, "/test/widgets", JsonContent.Create(new { name = "Tornillo" }));
        var id = await created.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
        using var read = await client.SendAsync(HttpMethod.Get, $"/test/widgets/{id}");
        var widget = await read.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("Tornillo", widget.GetProperty("name").GetString());
    }
}
```

- [ ] **Paso 5: correr**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`
Esperado: `total: 19`, `correcto: 19` (11 de la Tarea 15 + 8 nuevos). La primera vez descarga la imagen `postgres:18.3`.

Este es el primer contacto con la Api real: si algo falla, depurarlo con superpowers:systematic-debugging (causas probables: Docker apagado, orden del pipeline, `ConfigureTestServices`). Para ver que los tests realmente verifican algo, comentar `app.UseRequestLocalization();` en `Program.cs`: `Problem_follows_accept_language("en", ...)` tiene que fallar. Después restaurar la línea.

- [ ] **Paso 6: commit**

```bash
git add Directory.Packages.props tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "test: agregar arnés de integración con Testcontainers y verificar errores traducidos y de validación" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 17: GlobalExceptionHandler (500 genérico y 400 de request inválida)

**Archivos:**
- Crear: `src/ArquitecturaBase.Api/ErrorHandling/GlobalExceptionHandler.cs`
- Modificar: `src/ArquitecturaBase.Api/DependencyInjection.cs`, `src/ArquitecturaBase.Api/Program.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/TestEndpoints.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/ErrorHandlingTests.cs`

- [ ] **Paso 1: endpoint de prueba que lanza**

En `TestEndpoints.MapEndpoint`, al final:

```csharp
        group.MapGet("/boom", IResult () =>
            throw new InvalidOperationException("Sensitive detail that must never reach the client."));
```

Agregar `using Microsoft.AspNetCore.Http;` si el compilador lo pide.

- [ ] **Paso 2: tests que fallan**

`ErrorHandlingTests.cs`:

```csharp
using System.Net;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class ErrorHandlingTests(ApiFactory factory)
{
    [Fact]
    public async Task Unhandled_exception_returns_a_generic_500_with_trace_id()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/test/boom", language: "es");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("General.Unexpected", problem.GetProperty("code").GetString());
        Assert.Equal("Error del servidor", problem.GetProperty("title").GetString());
        Assert.Equal(
            "Ocurrió un error inesperado. Si el problema continúa, informá el código de seguimiento.",
            problem.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("Sensitive", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unhandled_exception_message_follows_accept_language()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/test/boom", language: "en");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(
            "An unexpected error occurred. If the problem persists, report the trace id.",
            problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Malformed_json_returns_400_request_invalid()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("{", Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(HttpMethod.Post, "/test/widgets", content, "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
        Assert.Equal("La solicitud tiene un formato inválido.", problem.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
```

- [ ] **Paso 3: correr y ver que falla**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.ErrorHandlingTests"`
Esperado: FALLAN los 3 (la excepción llega al cliente de TestServer y el JSON inválido devuelve 400 sin cuerpo).

- [ ] **Paso 4: implementación**

`src/ArquitecturaBase.Api/ErrorHandling/GlobalExceptionHandler.cs`:

```csharp
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.ErrorHandling;

/// <summary>
/// Convierte cualquier excepción en ProblemDetails. Los errores de binding (JSON inválido, fecha sin offset)
/// son un 400 "Request.Invalid"; el resto, un 500 genérico con traceId y el detalle solo en el log.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public const string UnexpectedErrorCode = "General.Unexpected";
    public const string InvalidRequestCode = "Request.Invalid";

    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;

        if (exception is BadHttpRequestException badRequest)
        {
            problem = ProblemDetailsMapper.Create(
                ErrorType.Validation, InvalidRequestCode, ErrorMessages.Get(InvalidRequestCode), badRequest.StatusCode);
        }
        else
        {
            LogUnhandledException(logger, exception);
            problem = ProblemDetailsMapper.Create(
                ErrorType.Failure, UnexpectedErrorCode, ErrorMessages.Get(UnexpectedErrorCode));
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        // No se pasa la excepción al contexto: así ningún detalle interno llega a la respuesta.
        return problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception while processing the request")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception);
}
```

En `DependencyInjection.AddPresentation`, después de `AddProblemDetails(...)`:

```csharp
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Los errores de binding lanzan BadHttpRequestException y los formatea GlobalExceptionHandler.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
```

(con `using ArquitecturaBase.Api.ErrorHandling;`).

En `Program.cs`, justo después de `app.UseRequestLocalization();`:

```csharp
// Dentro de la localización: el 500 también sale en el idioma pedido.
app.UseExceptionHandler();
```

- [ ] **Paso 5: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`
Esperado: `total: 22`, `correcto: 22`.

- [ ] **Paso 6: commit**

```bash
git add src/ArquitecturaBase.Api tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "feat: agregar GlobalExceptionHandler con 500 genérico y 400 para requests inválidas" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 18: UtcDateTimeConverter

Sección 6.3: la salida es ISO 8601 con `Z`, las entradas con offset se convierten a UTC y las que no tienen offset se rechazan con 400.

**Archivos:**
- Crear: `src/ArquitecturaBase.Api/Json/UtcDateTimeConverter.cs`
- Modificar: `src/ArquitecturaBase.Api/DependencyInjection.cs`
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/DateEchoRequest.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/TestEndpoints.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Json/UtcDateTimeConverterTests.cs`, `UtcDateTimeTests.cs`

- [ ] **Paso 1: endpoint de eco**

`TestFeatures/DateEchoRequest.cs`:

```csharp
namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

public sealed record DateEchoRequest(DateTime OccurredAtUtc, DateTime? ExpiresAtUtc);
```

En `TestEndpoints.MapEndpoint`:

```csharp
        group.MapPost("/dates", (DateEchoRequest request) => TypedResults.Ok(request));
```

- [ ] **Paso 2: tests que fallan**

`Json/UtcDateTimeConverterTests.cs`:

```csharp
using System.Text.Json;
using ArquitecturaBase.Api.Json;

namespace ArquitecturaBase.Api.IntegrationTests.Json;

public sealed class UtcDateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new UtcDateTimeConverter() } };

    private static readonly DateTime ExpectedUtc = new(2026, 9, 18, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Writes_utc_with_z_suffix()
    {
        var json = JsonSerializer.Serialize(new DateTime(2026, 9, 18, 17, 32, 0, DateTimeKind.Utc), Options);

        Assert.Equal("\"2026-09-18T17:32:00Z\"", json);
    }

    [Fact]
    public void Writes_fractional_seconds_only_when_present()
    {
        var json = JsonSerializer.Serialize(new DateTime(2026, 9, 18, 17, 32, 0, 120, DateTimeKind.Utc), Options);

        Assert.Equal("\"2026-09-18T17:32:00.12Z\"", json);
    }

    [Theory]
    [InlineData("2026-09-18T10:00:00-03:00")]
    [InlineData("2026-09-18T13:00:00Z")]
    [InlineData("2026-09-18T15:00:00+02:00")]
    public void Reads_values_with_offset_as_utc(string value)
    {
        var date = JsonSerializer.Deserialize<DateTime>($"\"{value}\"", Options);

        Assert.Equal(ExpectedUtc, date);
        Assert.Equal(DateTimeKind.Utc, date.Kind);
    }

    [Theory]
    [InlineData("2026-09-18T10:00:00")]
    [InlineData("2026-09-18")]
    [InlineData("hola")]
    [InlineData("")]
    public void Rejects_values_without_offset(string value)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>($"\"{value}\"", Options));
    }

    [Fact]
    public void Nullable_dates_are_supported()
    {
        Assert.Null(JsonSerializer.Deserialize<DateTime?>("null", Options));
        Assert.Equal(ExpectedUtc, JsonSerializer.Deserialize<DateTime?>("\"2026-09-18T13:00:00Z\"", Options));
    }
}
```

`UtcDateTimeTests.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class UtcDateTimeTests(ApiFactory factory)
{
    [Fact]
    public async Task Date_with_offset_is_returned_in_utc_with_z()
    {
        using var client = factory.CreateClient();
        using var content = Json("""{ "occurredAtUtc": "2026-09-18T10:00:00-03:00", "expiresAtUtc": null }""");

        using var response = await client.SendAsync(HttpMethod.Post, "/test/dates", content);
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2026-09-18T13:00:00Z", body.GetProperty("occurredAtUtc").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("expiresAtUtc").ValueKind);
    }

    [Fact]
    public async Task Date_without_offset_is_rejected_with_400()
    {
        using var client = factory.CreateClient();
        using var content = Json("""{ "occurredAtUtc": "2026-09-18T10:00:00" }""");

        using var response = await client.SendAsync(HttpMethod.Post, "/test/dates", content);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
```

- [ ] **Paso 3: correr y ver que falla**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`
Esperado: FALLA la compilación (`CS0234: ... 'Json' no existe en 'ArquitecturaBase.Api'`).

- [ ] **Paso 4: implementación**

`src/ArquitecturaBase.Api/Json/UtcDateTimeConverter.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Json;

/// <summary>
/// Fechas en la API (sección 6.3 del spec): la salida es ISO 8601 en UTC con "Z"; las entradas con offset
/// se convierten a UTC y las que no traen offset se rechazan (400). También cubre DateTime?.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    private const string OutputFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;

        // RoundtripKind deja Kind=Unspecified cuando el texto no trae offset ni "Z".
        if (text is null
            || !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            || parsed.Kind == DateTimeKind.Unspecified)
        {
            throw new JsonException("Dates must be ISO 8601 with an explicit offset, for example 2026-09-18T17:32:00Z.");
        }

        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture).UtcDateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),

            // Todo el código trabaja en UTC: un valor sin Kind se interpreta como UTC.
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        writer.WriteStringValue(utc.ToString(OutputFormat, CultureInfo.InvariantCulture));
    }
}
```

En `DependencyInjection.AddPresentation` (con `using ArquitecturaBase.Api.Json;`):

```csharp
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new UtcDateTimeConverter()));
```

- [ ] **Paso 5: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`
Esperado: `total: 34`, `correcto: 34`.

- [ ] **Paso 6: commit**

```bash
git add src/ArquitecturaBase.Api tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "feat: agregar UtcDateTimeConverter que exige offset y responde en UTC con Z" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 19: Auditoría y soft delete

**Archivos:**
- Crear: `src/ArquitecturaBase.Infrastructure/Persistence/Interceptors/AuditableEntityInterceptor.cs`, `SoftDeleteInterceptor.cs`
- Crear: `src/ArquitecturaBase.Infrastructure/Persistence/Extensions/ModelBuilderExtensions.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/ApplicationDbContext.cs`, `src/ArquitecturaBase.Infrastructure/DependencyInjection.cs`
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/Support/TestAuthHandler.cs`
- Modificar: `tests/.../Support/ApiFactory.cs`, `tests/.../Support/HttpExtensions.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/AuditingTests.cs`, `SoftDeleteTests.cs`

- [ ] **Paso 1: usuario de prueba**

`Support/TestAuthHandler.cs`:

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>Autentica con el Id del header X-Test-UserId. Sin el header, la petición es anónima.</summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserIdHeader = "X-Test-UserId";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserIdHeader, out var userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

En `ApiFactory.ConfigureTestServices`, al final (con `using Microsoft.AspNetCore.Authentication;`):

```csharp
            // Con un esquema registrado, WebApplication agrega UseAuthentication solo.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
```

En `HttpExtensions.SendAsync`, agregar el parámetro opcional `string? userId = null` al final y, antes de enviar:

```csharp
        if (userId is not null)
        {
            request.Headers.Add(TestAuthHandler.UserIdHeader, userId);
        }
```

- [ ] **Paso 2: tests que fallan**

`AuditingTests.cs`:

```csharp
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class AuditingTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creating_sets_created_fields_from_the_clock_and_the_current_user()
    {
        using var client = factory.CreateClient();
        factory.Clock.Advance(TimeSpan.FromHours(1));
        var createdAt = factory.Clock.GetUtcNow().UtcDateTime;
        var userId = Guid.CreateVersion7();

        using var created = await client.SendAsync(
            HttpMethod.Post,
            "/test/widgets",
            JsonContent.Create(new { name = "Auditado" }),
            userId: userId.ToString("D", CultureInfo.InvariantCulture));
        var id = await created.Content.ReadFromJsonAsync<Guid>(Ct);
        using var read = await client.SendAsync(HttpMethod.Get, $"/test/widgets/{id}");
        var widget = await read.ReadJsonAsync();

        Assert.Equal(createdAt, widget.GetProperty("createdAtUtc").GetDateTime());
        Assert.Equal(userId, widget.GetProperty("createdBy").GetGuid());
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("modifiedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Anonymous_creation_leaves_created_by_empty()
    {
        using var client = factory.CreateClient();

        using var created = await client.SendAsync(HttpMethod.Post, "/test/widgets", JsonContent.Create(new { name = "Anónimo" }));
        var id = await created.Content.ReadFromJsonAsync<Guid>(Ct);
        using var read = await client.SendAsync(HttpMethod.Get, $"/test/widgets/{id}");
        var widget = await read.ReadJsonAsync();

        Assert.Equal(JsonValueKind.Null, widget.GetProperty("createdBy").ValueKind);
    }

    [Fact]
    public async Task Modifying_sets_modified_fields()
    {
        var id = await factory.ExecuteDbContextAsync(async dbContext =>
        {
            var widget = new Widget("Original");
            dbContext.Add(widget);
            await dbContext.SaveChangesAsync(Ct);
            return widget.Id;
        });
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var modifiedAt = factory.Clock.GetUtcNow().UtcDateTime;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            var widget = await dbContext.Set<Widget>().SingleAsync(w => w.Id == id, Ct);
            widget.Rename("Renombrado");
            return await dbContext.SaveChangesAsync(Ct);
        });
        var stored = await factory.ExecuteDbContextAsync(dbContext =>
            dbContext.Set<Widget>().AsNoTracking().SingleAsync(w => w.Id == id, Ct));

        Assert.Equal(modifiedAt, stored.ModifiedAtUtc);
        Assert.Null(stored.ModifiedBy);
        Assert.True(stored.CreatedAtUtc < modifiedAt);
    }
}
```

`SoftDeleteTests.cs`:

```csharp
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class SoftDeleteTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deleting_marks_the_row_instead_of_removing_it()
    {
        var id = await CreateWidgetAsync("Borrable");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var deletedAt = factory.Clock.GetUtcNow().UtcDateTime;

        await DeleteWidgetAsync(id);
        var stored = await factory.ExecuteDbContextAsync(dbContext =>
            dbContext.Set<Widget>().IgnoreQueryFilters().AsNoTracking().SingleAsync(w => w.Id == id, Ct));

        Assert.True(stored.IsDeleted);
        Assert.Equal(deletedAt, stored.DeletedAtUtc);
    }

    [Fact]
    public async Task Deleted_rows_are_hidden_from_queries_and_endpoints()
    {
        using var client = factory.CreateClient();
        var id = await CreateWidgetAsync("Oculto");

        await DeleteWidgetAsync(id);
        var visible = await factory.ExecuteDbContextAsync(dbContext =>
            dbContext.Set<Widget>().AnyAsync(w => w.Id == id, Ct));
        using var response = await client.SendAsync(HttpMethod.Get, $"/test/widgets/{id}");

        Assert.False(visible);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private Task<Guid> CreateWidgetAsync(string name) =>
        factory.ExecuteDbContextAsync(async dbContext =>
        {
            var widget = new Widget(name);
            dbContext.Add(widget);
            await dbContext.SaveChangesAsync(Ct);
            return widget.Id;
        });

    private Task<int> DeleteWidgetAsync(Guid id) =>
        factory.ExecuteDbContextAsync(async dbContext =>
        {
            var widget = await dbContext.Set<Widget>().SingleAsync(w => w.Id == id, Ct);
            dbContext.Remove(widget);
            return await dbContext.SaveChangesAsync(Ct);
        });
}
```

- [ ] **Paso 3: correr y ver que falla**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`
Esperado: FALLAN los 5 nuevos (fechas en `0001-01-01`, `createdBy` vacío y la fila borrada físicamente: `SingleAsync` lanza "Sequence contains no elements").

- [ ] **Paso 4: implementación**

`Persistence/Interceptors/AuditableEntityInterceptor.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArquitecturaBase.Infrastructure.Persistence.Interceptors;

/// <summary>Completa CreatedAtUtc/By y ModifiedAtUtc/By de las entidades IAuditable.</summary>
internal sealed class AuditableEntityInterceptor(ICurrentUser currentUser, TimeProvider timeProvider)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        UpdateAuditableEntities(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        UpdateAuditableEntities(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateAuditableEntities(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Los interceptores corren antes del DetectChanges de SaveChanges: sin esto no se ven las modificaciones.
        context.ChangeTracker.DetectChanges();

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var userId = currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(IAuditable.CreatedAtUtc)).CurrentValue = nowUtc;
                entry.Property(nameof(IAuditable.CreatedBy)).CurrentValue = userId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(IAuditable.ModifiedAtUtc)).CurrentValue = nowUtc;
                entry.Property(nameof(IAuditable.ModifiedBy)).CurrentValue = userId;
            }
        }
    }
}
```

`Persistence/Interceptors/SoftDeleteInterceptor.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArquitecturaBase.Infrastructure.Persistence.Interceptors;

/// <summary>Convierte el borrado de una entidad ISoftDeletable en una marca (IsDeleted, DeletedAtUtc, DeletedBy).</summary>
internal sealed class SoftDeleteInterceptor(ICurrentUser currentUser, TimeProvider timeProvider)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        SoftDeleteEntities(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        SoftDeleteEntities(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void SoftDeleteEntities(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var deletedEntries = context.ChangeTracker.Entries<ISoftDeletable>()
            .Where(entry => entry.State == EntityState.Deleted)
            .ToList();

        if (deletedEntries.Count == 0)
        {
            return;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var userId = currentUser.UserId;

        foreach (var entry in deletedEntries)
        {
            entry.State = EntityState.Modified;
            entry.Property(nameof(ISoftDeletable.IsDeleted)).CurrentValue = true;
            entry.Property(nameof(ISoftDeletable.DeletedAtUtc)).CurrentValue = nowUtc;
            entry.Property(nameof(ISoftDeletable.DeletedBy)).CurrentValue = userId;
        }
    }
}
```

`Persistence/Extensions/ModelBuilderExtensions.cs`:

```csharp
using System.Linq.Expressions;
using ArquitecturaBase.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Extensions;

internal static class ModelBuilderExtensions
{
    /// <summary>
    /// Filtro global: las entidades ISoftDeletable marcadas como borradas no aparecen en las consultas.
    /// Para verlas: IgnoreQueryFilters().
    /// </summary>
    public static ModelBuilder ApplySoftDeleteQueryFilter(this ModelBuilder modelBuilder)
    {
        var softDeletableRoots = modelBuilder.Model.GetEntityTypes()
            .Where(entityType => entityType.BaseType is null && typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            .ToList();

        foreach (var entityType in softDeletableRoots)
        {
            var entity = Expression.Parameter(entityType.ClrType, "entity");
            var notDeleted = Expression.Lambda(
                Expression.Not(Expression.Property(entity, nameof(ISoftDeletable.IsDeleted))),
                entity);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(notDeleted);
        }

        return modelBuilder;
    }
}
```

En `ApplicationDbContext.OnModelCreating`, después de `ApplyConfigurationsFromAssembly(...)` (con `using ArquitecturaBase.Infrastructure.Persistence.Extensions;`):

```csharp
        modelBuilder.ApplySoftDeleteQueryFilter();
```

En `DependencyInjection.AddInfrastructure`, antes de `AddDbContext` (con `using ArquitecturaBase.Infrastructure.Persistence.Interceptors;`):

```csharp
        // El orden importa: primero el soft delete convierte el borrado en modificación y después se audita.
        services.AddScoped<ISaveChangesInterceptor, SoftDeleteInterceptor>();
        services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
```

- [ ] **Paso 5: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`
Esperado: `total: 39`, `correcto: 39`.

Si solo falla `Creating_sets_created_fields_from_the_clock_and_the_current_user` porque `createdBy` llega vacío, WebApplication no agregó `UseAuthentication` por su cuenta. Solución solo en los tests: en `ApiFactory` registrar un `IStartupFilter` que ejecute `app.UseAuthentication()` antes de `next(app)`. No tocar `Program.cs`.

- [ ] **Paso 6: commit**

```bash
git add src/ArquitecturaBase.Infrastructure tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "feat: agregar interceptores de auditoría y soft delete con filtro global" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 20: Paginado en Infrastructure (ApplySort + ToPagedResultAsync)

**Archivos:**
- Crear: `src/ArquitecturaBase.Infrastructure/Persistence/Extensions/QueryableExtensions.cs`
- Crear: `tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Widgets/GetWidgets.cs`
- Modificar: `tests/.../TestFeatures/TestEndpoints.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Persistence/QueryableExtensionsTests.cs`, `PaginationTests.cs`

- [ ] **Paso 1: feature de prueba y tests que fallan**

`TestFeatures/Widgets/GetWidgets.cs`:

```csharp
using System.Linq.Expressions;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

public sealed record GetWidgetsQuery : PagedRequest, IQuery<PagedResult<WidgetResponse>>
{
    // Lista blanca: los mismos nombres que usa el handler para ordenar.
    public static readonly IReadOnlyCollection<string> SortableFields = ["name", "createdAtUtc"];
}

public sealed record WidgetResponse(Guid Id, string Name, DateTime CreatedAtUtc);

internal sealed class GetWidgetsQueryValidator() : PagedRequestValidator<GetWidgetsQuery>(GetWidgetsQuery.SortableFields);

internal sealed class GetWidgetsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetWidgetsQuery, PagedResult<WidgetResponse>>
{
    private static readonly Dictionary<string, Expression<Func<Widget, object?>>> SortMap = new()
    {
        ["name"] = widget => widget.Name,
        ["createdAtUtc"] = widget => widget.CreatedAtUtc,
    };

    private static readonly SortDescriptor DefaultSort = new("createdAtUtc", Descending: true);

    public async Task<Result<PagedResult<WidgetResponse>>> Handle(GetWidgetsQuery query, CancellationToken cancellationToken)
    {
        var widgets = dbContext.Set<Widget>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            widgets = widgets.Where(widget => EF.Functions.Like(widget.Name, query.Search + "%"));
        }

        return await widgets
            .ApplySort(SortDescriptor.Parse(query.Sort), SortMap, DefaultSort)
            .Select(widget => new WidgetResponse(widget.Id, widget.Name, widget.CreatedAtUtc))
            .ToPagedResultAsync(query, cancellationToken);
    }
}
```

En `TestEndpoints.MapEndpoint` (con `using ArquitecturaBase.Application.Common.Pagination;`):

```csharp
        group.MapGet("/widgets", async (
                int? page,
                int? pageSize,
                string? sort,
                string? search,
                IQueryHandler<GetWidgetsQuery, PagedResult<WidgetResponse>> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(
                new GetWidgetsQuery
                {
                    Page = page ?? PagedRequest.DefaultPage,
                    PageSize = pageSize ?? PagedRequest.DefaultPageSize,
                    Sort = sort,
                    Search = search,
                },
                cancellationToken)).ToHttpResult());
```

`Persistence/QueryableExtensionsTests.cs` (sin base de datos):

```csharp
using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

public sealed class QueryableExtensionsTests
{
    private sealed record Item(string Name, int Rank);

    private static readonly Dictionary<string, Expression<Func<Item, object?>>> SortableFields = new()
    {
        ["name"] = item => item.Name,
        ["rank"] = item => item.Rank,
    };

    private static readonly SortDescriptor DefaultSort = new("rank", Descending: false);

    private static readonly IQueryable<Item> Items =
        new[] { new Item("b", 2), new Item("a", 3), new Item("c", 1) }.AsQueryable();

    [Fact]
    public void Sorts_ascending_by_a_whitelisted_field()
    {
        var names = Items.ApplySort(new SortDescriptor("name", false), SortableFields, DefaultSort).Select(item => item.Name);

        Assert.Equal(["a", "b", "c"], names);
    }

    [Fact]
    public void Sorts_descending()
    {
        var names = Items.ApplySort(new SortDescriptor("name", true), SortableFields, DefaultSort).Select(item => item.Name);

        Assert.Equal(["c", "b", "a"], names);
    }

    [Fact]
    public void Field_lookup_ignores_case()
    {
        var names = Items.ApplySort(new SortDescriptor("NAME", false), SortableFields, DefaultSort).Select(item => item.Name);

        Assert.Equal(["a", "b", "c"], names);
    }

    [Fact]
    public void Uses_the_default_sort_when_none_is_given()
    {
        var names = Items.ApplySort(null, SortableFields, DefaultSort).Select(item => item.Name);

        Assert.Equal(["c", "b", "a"], names);
    }

    [Fact]
    public void Field_outside_the_whitelist_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Items.ApplySort(new SortDescriptor("secret", false), SortableFields, DefaultSort));
    }
}
```

Nota: si `Assert.Equal([..], names)` es ambiguo, usar `new[] { ... }` como valor esperado.

`PaginationTests.cs`:

```csharp
using System.Globalization;
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class PaginationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_the_requested_page_sorted_by_newest_first()
    {
        using var client = factory.CreateClient();
        var prefix = await SeedWidgetsAsync(25);

        using var response = await client.SendAsync(
            HttpMethod.Get, $"/test/widgets?search={prefix}&page=2&pageSize=10&sort=-createdAtUtc");
        var page = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Enumerable.Range(6, 10).Reverse().Select(i => Name(prefix, i)), Names(page));
        Assert.Equal(2, page.GetProperty("page").GetInt32());
        Assert.Equal(10, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(25, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, page.GetProperty("totalPages").GetInt32());
        Assert.True(page.GetProperty("hasPrevious").GetBoolean());
        Assert.True(page.GetProperty("hasNext").GetBoolean());
    }

    [Fact]
    public async Task Sorts_by_name_ascending()
    {
        using var client = factory.CreateClient();
        var prefix = await SeedWidgetsAsync(7);

        using var response = await client.SendAsync(HttpMethod.Get, $"/test/widgets?search={prefix}&pageSize=5&sort=name");
        var page = await response.ReadJsonAsync();

        Assert.Equal(Enumerable.Range(1, 5).Select(i => Name(prefix, i)), Names(page));
        Assert.False(page.GetProperty("hasPrevious").GetBoolean());
        Assert.True(page.GetProperty("hasNext").GetBoolean());
    }

    [Fact]
    public async Task Default_sort_is_newest_first()
    {
        using var client = factory.CreateClient();
        var prefix = await SeedWidgetsAsync(3);

        using var response = await client.SendAsync(HttpMethod.Get, $"/test/widgets?search={prefix}");
        var page = await response.ReadJsonAsync();

        Assert.Equal([Name(prefix, 3), Name(prefix, 2), Name(prefix, 1)], Names(page));
    }

    [Fact]
    public async Task Sort_outside_the_whitelist_returns_a_validation_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/test/widgets?sort=secret", language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No se puede ordenar por ese campo.", problem.GetProperty("errors").GetProperty("sort")[0].GetString());
    }

    [Fact]
    public async Task Page_size_above_the_limit_returns_a_validation_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/test/widgets?pageSize=101", language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "La cantidad por página debe estar entre 1 y 100.",
            problem.GetProperty("errors").GetProperty("pageSize")[0].GetString());
    }

    // Cada test usa su propio prefijo: comparten la base con otros tests.
    private async Task<string> SeedWidgetsAsync(int count)
    {
        var prefix = "pag-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];

        for (var i = 1; i <= count; i++)
        {
            // Un minuto entre cada uno para que el orden por fecha sea determinista.
            factory.Clock.Advance(TimeSpan.FromMinutes(1));
            var name = Name(prefix, i);

            await factory.ExecuteDbContextAsync(dbContext =>
            {
                dbContext.Add(new Widget(name));
                return dbContext.SaveChangesAsync(Ct);
            });
        }

        return prefix;
    }

    private static string Name(string prefix, int index) =>
        prefix + "-" + index.ToString("D2", CultureInfo.InvariantCulture);

    private static string[] Names(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!).ToArray();
}
```

- [ ] **Paso 2: correr y ver que falla**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`
Esperado: FALLA la compilación (`CS0234: ... 'Extensions' no existe en 'ArquitecturaBase.Infrastructure.Persistence'`, o `CS1061` por `ApplySort`).

- [ ] **Paso 3: implementación**

`src/ArquitecturaBase.Infrastructure/Persistence/Extensions/QueryableExtensions.cs`:

```csharp
using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Extensions;

public static class QueryableExtensions
{
    /// <summary>
    /// Ordena por un campo de la lista blanca (sin distinguir mayúsculas). Sin orden pedido, usa
    /// <paramref name="defaultSort"/>. Un campo fuera de la lista es un error de programación: el validador
    /// de la consulta ya tuvo que rechazarlo.
    /// </summary>
    public static IQueryable<T> ApplySort<T>(
        this IQueryable<T> query,
        SortDescriptor? sort,
        IReadOnlyDictionary<string, Expression<Func<T, object?>>> sortableFields,
        SortDescriptor defaultSort)
    {
        ArgumentNullException.ThrowIfNull(sortableFields);
        ArgumentNullException.ThrowIfNull(defaultSort);

        var effectiveSort = sort ?? defaultSort;

        var keySelector = sortableFields
            .FirstOrDefault(field => string.Equals(field.Key, effectiveSort.Field, StringComparison.OrdinalIgnoreCase))
            .Value
            ?? throw new InvalidOperationException($"'{effectiveSort.Field}' is not in the sort whitelist.");

        return effectiveSort.Descending ? query.OrderByDescending(keySelector) : query.OrderBy(keySelector);
    }

    /// <summary>Cuenta el total y trae solo la página pedida (Skip/Take).</summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, request.Page, request.PageSize, totalCount);
    }
}
```

- [ ] **Paso 4: correr y ver que pasa**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj`
Esperado: `total: 49`, `correcto: 49`.

- [ ] **Paso 5: commit**

```bash
git add src/ArquitecturaBase.Infrastructure tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "feat: agregar ApplySort con lista blanca y ToPagedResultAsync" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 21: CLAUDE.md y README

**Archivos:**
- Crear: `CLAUDE.md`
- Reemplazar: `README.md`

- [ ] **Paso 1: `CLAUDE.md`**

````markdown
# ArquitecturaBase: guía para agentes

Plantilla base .NET 10 + Aspire 13.5 + PostgreSQL. El front vive en `../ArquitecturaBaseFront`.
El diseño aprobado está en `docs/specs/2026-09-18-arquitectura-base-design.md` y es la fuente de verdad. Los planes por fase están en `docs/plans/`.

## Forma de trabajo

- Se trabaja directo en `main`. No crear ramas ni hacer push sin un pedido explícito.
- Commits chicos, en español, con conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`).
- TDD donde hay lógica. Antes de dar algo por terminado: `dotnet build` sin advertencias y `dotnet test` en verde. Los tests de integración necesitan Docker.

## Comandos

- Compilar: `dotnet build ArquitecturaBase.slnx`
- Todos los tests: `dotnet test` (modo Microsoft Testing Platform, configurado en `global.json`)
- Un proyecto o una clase: `dotnet test --project tests/<Proyecto>/<Proyecto>.csproj -- --filter-class "<Namespace.Clase>"`
- Levantar todo: `aspire run` desde la raíz (Postgres + Api)

## Capas y dependencias

Las verifica `tests/ArquitecturaBase.ArchitectureTests`.

| Proyecto | Puede referenciar |
|---|---|
| Domain | nada (solo la BCL) |
| Application | Domain, más Microsoft.Extensions.*, FluentValidation y Scrutor |
| Infrastructure | Application, Domain |
| Api | Application, Infrastructure (solo desde `Program.cs`, para registrar dependencias), ServiceDefaults |
| AppHost | Api (como recurso de Aspire) |

- **Domain:** reglas de negocio puras, sin paquetes.
- **Application:** casos de uso, sin EF Core ni ASP.NET Core. `IQueryable` nunca sale de Infrastructure.
- **Infrastructure:** EF Core con Npgsql, interceptores, repositorios y servicios externos.
- **Api:** solo presentación. `Program.cs` solo compone.

## Casos de uso

- Cada caso de uso es un comando o una consulta, con su handler y su validador (FluentValidation), en `Application/Features/<Area>/<CasoDeUso>/`.
- Comandos: `ICommand` / `ICommand<T>` con `ICommandHandler<...>`. Consultas: `IQuery<T>` con `IQueryHandler<...>`. Sin MediatR ni AutoMapper; los mapeos se escriben a mano.
- Los handlers se registran solos (Scrutor) y quedan envueltos en este orden: logging → validación → unit of work (solo comandos) → handler.
- Los handlers no llaman a `SaveChanges`: lo hace `UnitOfWorkDecorator` si el resultado fue exitoso.
- Endpoints: una clase `IEndpoint` por grupo en `Api/Endpoints/<Area>/`, que se registra sola. El endpoint recibe el handler por inyección y devuelve `result.ToHttpResult()`.

## Result en lugar de excepciones

- Las reglas de negocio devuelven `Result` / `Result<T>` con un `Error`. No lanzan excepciones.
- Las excepciones quedan para bugs y fallas de infraestructura. Las atrapa `GlobalExceptionHandler`, que responde un 500 genérico con `traceId`.
- Los errores se declaran en clases `<Entidad>Errors` (por ejemplo `UserErrors`).
- Los códigos son estables y siguen el formato `Area.Entidad.Motivo` (por ejemplo `Auth.LoginCode.Expired`). El código es la clave de la traducción en `Errors.resx`.
- Toda respuesta de error es ProblemDetails, con:
  - `title` y `detail` traducidos;
  - `code` y `traceId`;
  - `errors` en las validaciones (campo en camelCase → mensajes).

## Persistencia

- Un repositorio por agregado: la interfaz en Domain y la implementación en Infrastructure. No hay repositorio genérico.
- Las entidades heredan de `Entity` (Id Guid v7) o `AggregateRoot` (acumula eventos de dominio).
- Cada entidad tiene su `IEntityTypeConfiguration<T>` en `Infrastructure/Persistence/Configurations/`.
- `IAuditable` e `ISoftDeletable` los completan los interceptores; nunca se setean a mano.
- Las filas borradas se ocultan con un filtro global. Para verlas: `IgnoreQueryFilters()`.
- Paginado:
  - la consulta hereda de `PagedRequest` y declara `SortableFields`;
  - su validador hereda de `PagedRequestValidator<T>`;
  - Infrastructure ordena con `ApplySort` (un mapa campo → expresión, con los mismos nombres) y pagina con `ToPagedResultAsync`.
- Migraciones (desde la Fase 2): `dotnet ef migrations add <Nombre> --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api`. En desarrollo, la Api las aplica al iniciar.

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
- Logs con `[LoggerMessage]` (source generator), nunca `logger.LogX(...)` directo. Nunca registrar códigos, tokens ni secretos.

## Build

- `TreatWarningsAsErrors`, analizadores `latest-recommended` y estilo en el build. Las advertencias se corrigen; solo se suprimen en `.editorconfig`, con una justificación.
- Las versiones de los paquetes van solo en `Directory.Packages.props` (Central Package Management).
- Los secretos van en user-secrets o en variables de entorno, nunca en el repo. Excepción temporal: la contraseña local de Postgres está en `src/ArquitecturaBase.AppHost/appsettings.Development.json` y se va a mover a user-secrets.

## Tests

- **Domain.UnitTests y Application.UnitTests:** xUnit v3, sin dependencias externas.
- **ArchitectureTests:** reglas de capas (NetArchTest y las referencias de cada `.csproj`).
- **Api.IntegrationTests:** `ApiFactory` (WebApplicationFactory + Testcontainers `postgres:18.3`).
  - Lo que existe solo para probar (entidades, endpoints `/test`, handlers) va en `TestFeatures/` del proyecto de tests, nunca en `src/`.
- Nombres de tests en inglés, como frase: `Deleted_rows_are_hidden_from_queries_and_endpoints`.
````

- [ ] **Paso 2: `README.md`**

````markdown
# Arquitectura Base

Plantilla base para aplicaciones web con .NET 10, Aspire, React y PostgreSQL, organizada en Clean Architecture.

- Diseño: [docs/specs/2026-09-18-arquitectura-base-design.md](docs/specs/2026-09-18-arquitectura-base-design.md).
- Este repo es el backend. El front está en `../ArquitecturaBaseFront`.

## Requisitos

- .NET SDK 10.0.400 o superior (lo fija `global.json`).
- Docker Desktop encendido. Lo usan el Postgres del AppHost y los tests de integración.
- Aspire CLI 13.5.4:

  ```bash
  dotnet tool install -g Aspire.Cli --version 13.5.4
  ```

- Certificado HTTPS de desarrollo confiable:

  ```bash
  dotnet dev-certs https --trust
  ```

## Contraseña de Postgres

La contraseña del contenedor es fija y sale del parámetro `Parameters:postgres-password` del AppHost.

- **Por ahora (pruebas en local):** está en `src/ArquitecturaBase.AppHost/appsettings.Development.json` con el valor `postgres`.
- **Más adelante:** se saca del repo y se carga en los user-secrets del AppHost:

  ```bash
  dotnet user-secrets set "Parameters:postgres-password" "tu-contraseña" --project src/ArquitecturaBase.AppHost
  ```

Postgres toma la contraseña solo cuando crea el volumen. Para cambiarla:

1. Detené el AppHost.
2. Borrá el contenedor de Postgres y después el volumen: `docker volume rm arquitecturabase-pgdata`.
3. Cargá la nueva contraseña y volvé a levantar.

## Levantar el proyecto

Desde la raíz del repo:

```bash
aspire run
```

La consola muestra la URL del dashboard de Aspire. Se levantan:

- **postgres:** contenedor persistente en `localhost:5433` con la base `appdb`. Sigue vivo al cerrar el AppHost.
- **api:** espera a que la base esté lista y, en desarrollo, aplica las migraciones al iniciar. También en desarrollo:
  - OpenAPI en `/openapi/v1.json`;
  - Scalar en `/scalar`;
  - health checks en `/health` y `/alive`.

También funciona con `dotnet run --project src/ArquitecturaBase.AppHost` o con F5 sobre el AppHost en Visual Studio.

## Conectarse con DBeaver

| Campo | Valor |
|---|---|
| Host | `localhost` |
| Puerto | `5433` |
| Base de datos | `appdb` |
| Usuario | `postgres` |
| Contraseña | la de `Parameters:postgres-password` (`postgres` mientras esté en `appsettings.Development.json`) |

El puerto 5432 queda libre para el PostgreSQL local de la máquina.

## Tests

```bash
dotnet test
```

Los tests de integración levantan su propio Postgres con Testcontainers, así que necesitan Docker encendido.

## Estructura

```
src/
  ArquitecturaBase.Domain            reglas de negocio (Result, Error, Entity, ...)
  ArquitecturaBase.Application       casos de uso, validación, paginado, textos (resx)
  ArquitecturaBase.Infrastructure    EF Core + PostgreSQL, interceptores, paginado
  ArquitecturaBase.Api               endpoints, ProblemDetails, localización, OpenAPI
  ArquitecturaBase.AppHost           orquestación con Aspire
  ArquitecturaBase.ServiceDefaults   OpenTelemetry, health checks, resiliencia
tests/
  ArquitecturaBase.Domain.UnitTests
  ArquitecturaBase.Application.UnitTests
  ArquitecturaBase.Api.IntegrationTests
  ArquitecturaBase.ArchitectureTests
```

Las convenciones de código están en [CLAUDE.md](CLAUDE.md).
````

- [ ] **Paso 3: commit**

```bash
git add CLAUDE.md README.md
git commit -m "docs: agregar CLAUDE.md con las convenciones y README con la puesta en marcha" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 22: Verificación final

Se ejecuta con superpowers:verification-before-completion. Cada paso muestra la salida real.

- [ ] **Paso 1: build limpio**

```bash
dotnet build ArquitecturaBase.slnx --no-incremental
```

Esperado: `0 Advertencia(s)`, `0 Errores`.

- [ ] **Paso 2: todos los tests** (Docker encendido)

```bash
dotnet test
```

Esperado: 139 tests en verde (Architecture 11 + Domain 19 + Application 60 + Integración 49), sin fallas ni omitidos.

- [ ] **Paso 3: preparar el AppHost**

1. La contraseña ya está en `src/ArquitecturaBase.AppHost/appsettings.Development.json` (Tarea 3).
2. Revisar el certificado: `dotnet dev-certs https --check --trust`. Si no es confiable, pedirle al usuario que ejecute `dotnet dev-certs https --trust`: modifica el almacén de certificados, así que lo corre el usuario.
3. Confirmar que el 5433 está libre: `netstat -ano | grep ":5433 "` sin resultados.

- [ ] **Paso 4: levantar con Aspire** (en segundo plano)

```bash
aspire run
```

Esperar a que la salida muestre la URL del dashboard y que los recursos queden en `Running`. Luego verificar:

```bash
docker ps --format "{{.Names}}  {{.Image}}  {{.Ports}}"
docker volume ls --filter name=arquitecturabase-pgdata
docker exec <contenedor-postgres> psql -U postgres -d appdb -c "select current_database();"
curl -sk https://localhost:7180/health
```

Esperado:
- un contenedor `postgres:18.3` con `0.0.0.0:5433->5432/tcp`;
- el volumen `arquitecturabase-pgdata`;
- `current_database` = `appdb`;
- `/health` responde `Healthy`.

Si Aspire asigna otro puerto a la Api, sacarlo del dashboard o de la salida de `aspire run`. En los logs de la Api no debe haber errores; el aviso de EF Core "No migrations were found" es esperable en esta fase.

- [ ] **Paso 5: persistencia del contenedor**

Detener `aspire run` (Ctrl+C o cortar el proceso en segundo plano) y verificar con `docker ps` que el contenedor de Postgres sigue corriendo.

- [ ] **Paso 6: DBeaver**

Pedirle al usuario que se conecte con los datos del README (`localhost:5433`, base `appdb`, usuario `postgres`) y que confirme.

- [ ] **Paso 7: cierre**

Si hubo correcciones, commitearlas (`fix: ...`). Mostrarle al usuario el resumen con la salida del build y de los tests, y `git log --oneline` de la fase. Sin push.

---

## Cobertura del alcance de la Fase 1

| Pedido | Tarea |
|---|---|
| Solución `.slnx`, `Directory.Build.props` (net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors, analizadores), CPM, `.editorconfig` | 1 |
| `BannedSymbols.txt` + BannedApiAnalyzers (DateTime.Now/Today/UtcNow, DateTimeOffset.Now/UtcNow) | 1 |
| Proyectos Domain, Application, Infrastructure, Api, ServiceDefaults y referencias de la tabla 3.1 | 1, 2 |
| AppHost: Postgres en 5433, `postgres-password` secreto, persistente, volumen `arquitecturabase-pgdata`, base `appdb`, Api con `WaitFor` | 3 |
| Tests de arquitectura con las reglas de 3.1 | 4 |
| Domain Results: Result, Result<T>, Error, ErrorType | 5 |
| Domain Common: Entity (Guid v7), AggregateRoot, ValueObject, IDomainEvent, IAuditable, ISoftDeletable | 6 |
| Mensajería sin MediatR, IUnitOfWork, ICurrentUser | 7 |
| Resources Errors y Validation (es por defecto + en) | 8 |
| Pagination: PagedRequest, PagedResult<T>, SortDescriptor | 9 |
| Reglas de validación comunes | 10 |
| Decoradores de validación, logging y unit of work | 11 |
| Registro con Scrutor y `AddApplication()` | 12 |
| ApplicationDbContext (DbContext), UnitOfWork, TimeProvider, migraciones al iniciar en Development | 13 |
| Program.cs de composición, IEndpoint con registro automático, CurrentUser, RequestLocalization es/en, OpenAPI + Scalar solo en desarrollo | 14 |
| Result → ProblemDetails (code, traceId, errors) | 15 |
| Integración: errores traducidos según Accept-Language y diccionario de validación | 16 |
| GlobalExceptionHandler y 500 genérico con traceId | 17 |
| UtcDateTimeConverter: rechaza sin offset y convierte a Z | 18 |
| Interceptores de auditoría y soft delete con filtro global | 19 |
| ToPagedResultAsync y ApplySort con lista blanca; paginado con orden por lista blanca | 20 |
| CLAUDE.md con las convenciones y README (levantar, contraseña, DBeaver) | 21 |
| Terminado cuando: build sin warnings, tests en verde, AppHost levanta Postgres (5433) y Api, DBeaver conecta | 22 |

**Fuera de esta fase (no se hace):** Identity, OpenIddict, emails, rate limiting, permisos y front (Fases 2 y 3).

---

## Resultado de la ejecución (2026-09-19)

Verificación final: `dotnet build` con 0 advertencias, `dotnet test` con 151/151, y `aspire run` levanta Postgres (5433, volumen `arquitecturabase-pgdata`, base `appdb`) y la Api en estado Healthy.

### Desvíos respecto del plan

Salieron de las revisiones de cada tarea y de la revisión final:

- **AppHost:** se conserva `AspireUseCliBundle=true`, el valor por defecto de 13.5 (ver el hecho verificado 2).
- **Contraseña de Postgres:** va en `appsettings.Development.json` del AppHost por pedido del usuario.
- **Tests de arquitectura:**
  - `TestResult` se escribe calificado, porque choca con `Xunit.TestResult`.
  - El test de Domain valida contra la carpeta del framework compartido en lugar del prefijo `System`.
- **`IAuditable`:** documenta que sus propiedades deben ser públicas.
- **`AddFeaturesFromAssembly`:** un test impide que los decoradores se registren como handlers.
- **ProblemDetails:** las claves `code`, `errors` y `traceId` nunca se toman de `Metadata`.
- **`UtcDateTimeConverter`:** usa `DateTimeOffset.TryParse`, así una fecha fuera de rango da 400 en lugar de 500.
- **Paginado:**
  - `ApplySort` exige un desempate único, normalmente el Id.
  - `ToPagedResultAsync` valida `Page` y `PageSize`.
  - `PagedRequest.MaxPage` es 1.000.000: una página enorme da 400 en lugar de desbordar el OFFSET.
- **Soft delete:** el filtro global tiene nombre (`SoftDelete`) y el interceptor de auditoría ya no llama a `DetectChanges` de más.
- **CLAUDE.md:** documenta el comando de migraciones que funciona, pasando la cadena de conexión como argumento.
- **Documentación de la Api:** por pedido del usuario, Swagger UI (`Swashbuckle.AspNetCore.SwaggerUI`, que lee `/openapi/v1.json`) reemplaza a Scalar, siempre solo en desarrollo. El dashboard de Aspire muestra el link "Swagger UI" en la fila `api`. `OpenApiTests` verifica que se sirve en Development y no fuera de él.

### Pendientes para la Fase 2 (de la revisión final)

1. **Hecho (antes de la Fase 2).** El arnés reutiliza las `DbContextOptions<ApplicationDbContext>` de producción: solo reemplaza el contexto por `TestDbContext` y pasa la cadena de conexión por configuración (`DbContextRegistrationTests`). Planteo original: **Registro del DbContext en un solo lugar.** Crear un método en Infrastructure, por ejemplo `AddApplicationDbContext<TContext>()`, y usarlo desde `AddInfrastructure` y desde `ApiFactory`. Así `UseOpenIddict<Guid>()` y lo que se agregue después también llegan a los tests de integración. Hoy el arnés copia `UseNpgsql(...).AddInterceptors(...)`.
2. **Hecho (antes de la Fase 2).** `UseStatusCodePages` + `ProblemDetailsMapper.CompleteFrameworkProblem` (códigos `Http.*`), con `UseAuthentication`/`UseAuthorization` explícitos después de `UseStatusCodePages` (`FrameworkErrorsTests`). Planteo original: **Errores del framework con cuerpo ProblemDetails.** Agregar `app.UseStatusCodePages()` y completar `code` y `title` traducido en `CustomizeProblemDetails`. Así los 401, 403, 404, 405 y 429 que genera el framework también llevan cuerpo; hoy el 404 de una ruta inexistente responde vacío.
3. **Fechas en query string.** El conversor solo aplica a cuerpos JSON: un `?from=2026-09-18T10:00:00` se enlaza como `Unspecified`. Extraer el parseo de `UtcDateTimeConverter` y usarlo en un tipo enlazable o en un filtro de endpoint.
4. **`[AsParameters]` con consultas `PagedRequest`.** Las propiedades `int` quedan obligatorias. Crear un tipo de enlace reutilizable del lado de la Api, o documentar el enlace manual que usa `TestEndpoints`.
5. **Errores de binding.** Distinguir 413, 415 y 408 de "Datos inválidos" y registrarlos en el log.
6. **Hecho (Fase 2).** `PagedRequest.MaxSearchLength` (100) validado en `PagedRequestValidator`, y `IdentityService` escapa `%`, `_` y el propio carácter de escape antes de armar el patrón de `EF.Functions.ILike` (`GetUsersQueryTests`, `IdentityServiceTests`). Planteo original: **Búsqueda.** Limitar la longitud de `Search` cuando llegue la primera búsqueda real, y escapar `%` y `_` en `LIKE`.
7. **Hecho (Fase 2).** `ErrorCodeTranslationTests` verifica que cada código declarado en las clases `*Errors` existe en `Errors.resx` y en `Errors.en.resx`. Planteo original: **Traducciones de errores.** Agregar un test que verifique que cada código declarado en las clases `*Errors` existe en los dos `.resx`.
8. **Advertencias de la revisión de los puntos 1 y 2 para la Fase 2:**
   - **Hecho (Fase 2).** `UseRateLimiter` va después de `UseStatusCodePages`; `LoginCodeEndpointsTests.Rate_limiter_rejects_with_a_problem_and_retry_after` prueba un rechazo real del limitador. Planteo original: `UseRateLimiter` va después de `UseStatusCodePages`; agregar un test con un rechazo real del limitador (el test actual del 429 usa un endpoint normal).
   - **Hecho (Fase 2).** Identity se registra con `AddIdentityCore` (no `AddIdentity`), así no compite con el esquema de prueba del arnés; el arnés combina el esquema real con el de `X-Test-UserId`. Planteo original: `AddIdentity` fija sus propios esquemas por defecto y le gana al `AddAuthentication("Test")` del arnés: sobrescribirlos con `PostConfigure<AuthenticationOptions>` para que `X-Test-UserId` siga funcionando.
   - **Hecho (Fase 2).** `/api` usa la validación de OpenIddict como esquema por defecto; `OpenIddictServerTests.Anonymous_api_request_is_challenged_by_the_bearer_scheme` prueba el 401 con el esquema real. Planteo original: la cookie de Identity responde con un redirect (302) por defecto: `/api` tiene que usar la validación de OpenIddict como esquema por defecto (sección 5.5) para que devuelva 401 con ProblemDetails. Agregar un test del 401 con el esquema real.
   - **Hecho (Fase 2).** `MigrationsTests` arma un `ApplicationDbContext` a mano en lugar de usar el `TestDbContext` del arnés. Planteo original: el test de `HasPendingModelChanges` tiene que usar un `ApplicationDbContext` armado a mano, no el `TestDbContext` que entrega el arnés.
   - **Hecho (Fase 3).** `UseSpaFallback` (`Api/Hosting/SpaExtensions.cs`) es un middleware y no un `MapFallback`, así que solo atiende lo que no matcheó ningún endpoint, y además deja afuera los métodos que no son GET o HEAD, lo que parece un archivo y los prefijos de backend (`/api`, `/account`, `/connect`, `/signin-google`, `/.well-known`, `/swagger`, `/openapi`, `/health`, `/alive`). `SpaHostingTests` prueba que esas rutas siguen devolviendo ProblemDetails y que el 405 y el 415 del routing no se pierden. Planteo original: cuando la Api sirva el SPA con fallback a `index.html`, excluir `/api` del fallback para que las rutas inexistentes sigan devolviendo 404.
9. **Sugerencias:**
   - trazas de Npgsql y health check de la base en ServiceDefaults;
   - un test de `HasPendingModelChanges() == false` cuando existan migraciones.
