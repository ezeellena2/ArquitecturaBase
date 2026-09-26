> **HISTÓRICO. No ejecutar.** Registro de cómo se construyó esta parte. La arquitectura vigente está en [`docs/specs/2026-09-24-backend-mvc-architecture.md`](../specs/2026-09-24-backend-mvc-architecture.md); donde este documento hable de handlers, `Features/` o Minimal API, prevalece la especificación.

# Forwarded Headers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `subagent-driven-development` (recommended) or `executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Interpretar de forma segura `X-Forwarded-For` y `X-Forwarded-Proto` antes de cualquier middleware que dependa de la IP o del esquema público.

**Architecture:** La API registrará `ForwardedHeadersOptions` con un único salto confiable y sin aceptar `X-Forwarded-Host`. Por defecto conservará las redes loopback confiables de ASP.NET Core; los despliegues detrás de un ingress aislado, como Azure Container Apps, habilitarán explícitamente `ForwardedHeaders:TrustAll`. También se admitirán proxies y redes CIDR concretas para otros hosts; en .NET 10 las CIDR se agregarán a `KnownIPNetworks`, nunca a la propiedad obsoleta `KnownNetworks`.

**Tech Stack:** ASP.NET Core 10, `ForwardedHeadersMiddleware`, xUnit v3.

---

### Task 1: Probar la política de confianza y el middleware

**Files:**
- Create: `tests/ArquitecturaBase.Api.IntegrationTests/Hosting/ForwardedHeadersTests.cs`
- Modify: `tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/TestEndpoints.cs`

- [x] **Step 1: Write failing tests**

Agregar un endpoint sólo de tests que devuelva `Request.Scheme`, `Connection.RemoteIpAddress` y `Request.Host`. Sobre el `Program` real, comprobar que un proxy confiable cambia esquema/IP sin aceptar `X-Forwarded-Host`, que un peer no confiable es ignorado y que una cadena de dos IP toma sólo el salto más cercano. Comprobar además que sólo se habilitan `X-Forwarded-For` y `X-Forwarded-Proto`, que `ForwardLimit` vale uno y que `TrustAll` limpia `KnownProxies` y `KnownIPNetworks`.

El contrato de configuración será:

```text
ForwardedHeaders:TrustAll                 booleano, false por defecto
ForwardedHeaders:KnownProxies:0           dirección IPv4 o IPv6 literal
ForwardedHeaders:KnownNetworks:0          red en formato CIDR, por ejemplo 10.0.0.0/8
```

Una IP/CIDR inválida hará fallar el arranque con una excepción descriptiva. `TrustAll=true` combinado con entradas explícitas también será rechazado para que no aparente una restricción que en realidad no existe.

- [x] **Step 2: Run the focused test and verify it fails**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Hosting.ForwardedHeadersTests"`

Expected: FAIL porque la extensión de configuración todavía no existe.

### Task 2: Registrar y ejecutar Forwarded Headers

**Files:**
- Create: `src/ArquitecturaBase.Api/Hosting/ForwardedHeadersExtensions.cs`
- Modify: `src/ArquitecturaBase.Api/Program.cs`

- [x] **Step 1: Implement the minimal configuration extension**

Registrar `XForwardedFor | XForwardedProto`, `ForwardLimit = 1`, `TrustAll` explícito y las colecciones opcionales de configuración `KnownProxies`/`KnownNetworks`; estas últimas se parsean a `KnownIPNetworks`. Rechazar configuraciones inválidas.

- [x] **Step 2: Put the middleware first in the request pipeline**

Ejecutar `UseForwardedHeaders()` inmediatamente después de `Build()`, antes de seguridad, rate limiting, autenticación, HSTS y redirección HTTPS.

- [x] **Step 3: Run the focused tests**

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Hosting.ForwardedHeadersTests"`

Expected: PASS.

### Task 3: Documentar Azure Container Apps

**Files:**
- Modify: `docs/deploy/azure-setup.md`
- Modify: `README.md`

- [x] **Step 1: Enable the explicit ACA trust mode in setup documentation**

Agregar `ForwardedHeaders__TrustAll=true` al comando que configura el Container App y explicar que sólo es seguro si Kestrel no queda accesible fuera del ingress.

- [x] **Step 2: Move Forwarded Headers out of the unresolved deployment list**

Documentar el comportamiento ya resuelto y conservar sólo los pendientes reales.

- [x] **Step 3: Verify the backend**

Run: `dotnet build ArquitecturaBase.slnx`

Run: `dotnet test`

Expected: build sin warnings y suite verde.
