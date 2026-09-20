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
  - Swagger UI en `/swagger` (en el dashboard de Aspire, el link "Swagger UI" de la fila `api`);
  - health checks en `/health` y `/alive`.
- **front:** el SPA del repo `../ArquitecturaBaseFront`, servido por Vite. Espera a la Api.

**La dirección que se abre en el navegador es `https://localhost:5173`**, la del front. Ver [El front y el origen único](#el-front-y-el-origen-único).

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

Si DBeaver responde `FATAL: invalid value for parameter "TimeZone": "America/Buenos_Aires"`, es porque Java manda el nombre viejo de la zona horaria y la imagen de Postgres 18 ya no lo trae. Cerrá DBeaver, agregá esta línea al final de `dbeaver.ini` (debajo de `-vmargs`; en Windows está en `C:\Program Files\DBeaver\dbeaver.ini` y se edita como administrador) y volvé a abrirlo:

```
-Duser.timezone=UTC
```

Con UTC ves las fechas tal como están guardadas. Para verlas en hora local, usá `-Duser.timezone=America/Argentina/Buenos_Aires`.

## Identidad (Fase 2)

El ingreso es sin contraseña: con un código de 6 dígitos que llega por email, o con Google. La Api es a la vez el servidor OpenIddict (`/connect/*`) y la Api de negocio (`/api/*`, con bearer).

### El front y el origen único

El navegador habla con **un solo origen**, así que no hay CORS ni cookies de terceros. Cómo se arma ese origen cambia según el entorno:

| | Desarrollo (`aspire run`) | Producción |
|---|---|---|
| Origen del navegador | `https://localhost:5173` | el del despliegue |
| Quién sirve el SPA | Vite (repo `../ArquitecturaBaseFront`) | la Api, desde `wwwroot` |
| Quién atiende `/api`, `/account`, `/connect`, `/signin-google`, `/.well-known` | la Api, `https://localhost:7180`, a la que Vite reenvía con proxy | la Api, el mismo proceso |

Dos consecuencias:

- **El issuer de OpenIddict es el origen público, no el de la Api.** En desarrollo se fija con `Authentication:Issuer` en `https://localhost:5173/`, porque es lo que ve el navegador. El proxy de Vite va con `changeOrigin: false` para que la Api reciba `Host: localhost:5173` y arme bien el resto de los endpoints del documento de discovery y el `redirect_uri` de Google.
- **El puerto 7180 casi no se usa a mano.** Sirve para Swagger UI y para pegarle a la Api sin pasar por el front (Postman). El ingreso con Google funciona por los dos, porque el cliente OAuth tiene registradas las dos URIs de redireccionamiento.

Las rutas del SPA las resuelve su propio router. Del lado de la Api eso es `UseSpaFallback` (`src/ArquitecturaBase.Api/Hosting/SpaExtensions.cs`): sirve el `index.html` en las rutas que nadie atendió, sin tocar las del backend, que siguen devolviendo su 404 o 405 con ProblemDetails. Si no hay `wwwroot/index.html` —el caso de desarrollo, donde el SPA lo sirve Vite— el middleware no se instala.

### Configuración de desarrollo

`src/ArquitecturaBase.Api/appsettings.Development.json` ya trae lo necesario para trabajar local:
- la clave HMAC de los códigos;
- las redirect URIs del cliente `web`;
- `Seed:AdminEmail`, el email que recibe el rol Admin al crear su cuenta;
- los emails salen por Gmail, igual que en producción (ver **Emails**, abajo).

### Secretos (user-secrets de la Api)

| Clave | Para qué | Cómo |
|---|---|---|
| `Authentication:Google:ClientSecret` | ingreso con Google (obligatorio si hay `ClientId`) | `dotnet user-secrets set "Authentication:Google:ClientSecret" "<secreto>" --project src/ArquitecturaBase.Api` |
| `Email:Smtp:Password` | enviar emails reales por Gmail | contraseña de aplicación: https://myaccount.google.com/apppasswords |

En Google Cloud Console, el cliente OAuth tiene que tener como URIs de redireccionamiento autorizados `https://localhost:7180/signin-google` (Api directa) y `https://localhost:5173/signin-google` (a través de Vite, Fase 3).

### Emails

En desarrollo los emails **se envían de verdad**, por Gmail, igual que en producción: así el código de ingreso llega a la casilla y el flujo se prueba entero. `appsettings.Development.json` trae `Email:Delivery` en `Smtp` y la cuenta que envía; la contraseña va en user-secrets:

```
dotnet user-secrets set "Email:Smtp:Password" "<contraseña de aplicación>" --project src/ArquitecturaBase.Api
```

Es una **contraseña de aplicación** de Gmail (https://myaccount.google.com/apppasswords), no la contraseña de la cuenta ni el ClientSecret de OAuth: son tres cosas distintas. Sin ella la Api **no arranca**, porque `SmtpOptions` se valida al iniciar.

Gmail reescribe el remitente a la cuenta que autentica, así que los correos salen desde `Email:Smtp:UserName` aunque `FromName` diga otra cosa. El límite es de unos 500 envíos por día.

Para volver a no enviar nada y escribir archivos `.eml` en `src/ArquitecturaBase.Api/.emails/` (carpeta ignorada por git), alcanza con poner `Email:Delivery` en `PickupDirectory`. Sirve cuando no hay internet o no se quiere gastar la cuota.

### Probar el flujo

Con Postman: [docs/postman/README.md](docs/postman/README.md).

### Producción

Fuera de Development y Testing, esta configuración es obligatoria: la Api la valida al iniciar y no arranca si falta.

| Clave | Requisito |
|---|---|
| `Authentication:LoginCode:HashKey` | al menos 32 bytes aleatorios en base64 |
| `Authentication:Clients:Web:RedirectUris` | al menos una URI |
| `Authentication:Clients:Web:PostLogoutRedirectUris` | al menos una URI |
| `Authentication:Certificates:Encryption:Path` / `:Password` | certificado PFX de cifrado de OpenIddict |
| `Authentication:Certificates:Signing:Path` / `:Password` | certificado PFX de firma de OpenIddict |
| `Authentication:Google:ClientSecret` | obligatorio si hay `Authentication:Google:ClientId` |
| `Email:Smtp:UserName`, `Email:Smtp:Password`, `Email:Smtp:FromAddress` | obligatorios si `Email:Delivery = Smtp` |

Además, `Authentication:Issuer` y las redirect URIs del cliente `web` tienen que apuntar al origen público del despliegue, no a `localhost`.

#### Pendientes del despliegue

Lo que sigue **no está resuelto** y lo tiene que cubrir quien arme el pipeline. Está acá para que no se descubra en el primer despliegue.

1. **Nadie copia el `dist/` del front a `wwwroot/`.** La Api sabe servir el SPA, pero el paso que lo pone en su lugar no existe: el `.csproj` de la Api no tiene ningún `Target`, no hay Dockerfile, y `.github/workflows/deploy.yml` publica la Api sin mencionar al front. Sin ese paso la Api arranca igual y `UseSpaFallback` no se instala: **el sitio responde 404 en `/`** y solo anda la Api. Falta correr `npm ci && npm run build` en `../ArquitecturaBaseFront` y copiar el resultado a `src/ArquitecturaBase.Api/wwwroot/` antes del `dotnet publish`. Dos detalles: el front vive en otro repo, así que el checkout tiene que traer los dos; y el `dist/` incluye `silent-renew.html`, que hace falta para la renovación silenciosa de la sesión.
2. **`UseHttpsRedirection()` y `UseHsts()` sin `ForwardedHeaders`.** Detrás de un proxy o balanceador que termina TLS (Azure Container Apps, App Service, nginx, un ingress de Kubernetes), la Api recibe el pedido por http y responde un 307 a https; el proxy vuelve a entrar por http y **se arma un bucle de redirecciones**. Hay que agregar `UseForwardedHeaders` con `ForwardedHeaders.XForwardedProto | XForwardedFor`, antes de `UseHttpsRedirection`, y configurar `KnownProxies`/`KnownNetworks` (o limpiarlos si el proxy es de confianza y no manda la IP real). Lo mismo hace falta para que el rate limiter y la auditoría de ingresos vean la IP del cliente y no la del proxy.
3. **Migraciones y seed.** En producción no corren solos: ni las migraciones ni el seed de roles, permisos y el cliente `web`. Quedan para cuando haya pipeline.
4. **Certificados de OpenIddict.** Los de firma y cifrado salen de los PFX de la tabla de arriba. Con varias instancias tienen que ser los mismos en todas.
5. **Data Protection.** Las claves quedan sin cifrar en Postgres. En producción: `ProtectKeysWithCertificate`.

## Tests

```bash
dotnet test
```

Los tests de integración levantan su propio Postgres con Testcontainers, así que necesitan Docker encendido.

**Hay un test inestable conocido.** En la corrida completa, `UsersEndpointsTests.Admin_gets_every_permission` y `Sorting_by_a_field_outside_the_whitelist_is_rejected` fallan aproximadamente una de cada cinco veces, adentro de `AuthFlow.LoginAsync`. No es un problema del código de producción: es una carrera del arnés, que el propio `AuthFlow.RequestCodeAsync` documenta en un comentario (puede leer el email de un código viejo de `admin@arquitecturabase.test` en vez del recién pedido). La agrava el `FakeTimeProvider` compartido, que algunos tests adelantan hasta una hora y con eso vencen códigos de otros que estaban en vuelo. Si te pasa, volvé a correr; el arreglo está anotado como pendiente en el [plan de la Fase 3](docs/plans/2026-09-19-fase-3-front-base.md).

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
