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

La Api procesa `X-Forwarded-For` y `X-Forwarded-Proto` antes del rate limiter, la autenticación y la redirección
HTTPS. Por defecto sólo confía en los proxies loopback de ASP.NET Core. Un despliegue puede declarar
`ForwardedHeaders:KnownProxies` (direcciones IP) o `ForwardedHeaders:KnownNetworks` (CIDR). Azure Container Apps,
cuyas IP internas pueden cambiar, usa `ForwardedHeaders:TrustAll=true`; esto sólo es seguro cuando Kestrel no es
accesible por fuera del ingress confiable.

`X-Forwarded-Host` no se acepta a propósito: OpenIddict usa `Request.Host` para construir URLs públicas y confiar
ese encabezado sin una lista explícita permitiría que un cliente las manipule. El reverse proxy tiene que conservar
el host público en el encabezado HTTP `Host` (en nginx, por ejemplo, `proxy_set_header Host $host`) y producción
debe reemplazar `AllowedHosts: "*"` por los hosts públicos permitidos, separados por `;` si hay más de uno.

#### Pendientes del despliegue

Lo que sigue **no está resuelto** y lo tiene que cubrir quien arme el pipeline. Está acá para que no se descubra en el primer despliegue.

1. **Nadie copia el `dist/` del front a `wwwroot/`.** La Api sabe servir el SPA, pero el paso que lo pone en su lugar no existe: el `.csproj` de la Api no tiene ningún `Target`, no hay Dockerfile, y `.github/workflows/deploy.yml` publica la Api sin mencionar al front. Sin ese paso la Api arranca igual y `UseSpaFallback` no se instala: **el sitio responde 404 en `/`** y solo anda la Api. Falta correr `npm ci && npm run build` en `../ArquitecturaBaseFront` y copiar el resultado a `src/ArquitecturaBase.Api/wwwroot/` antes del `dotnet publish`. Dos detalles: el front vive en otro repo, así que el checkout tiene que traer los dos; y el `dist/` incluye `silent-renew.html`, que hace falta para la renovación silenciosa de la sesión.
2. **Migraciones y seed.** En producción no corren solos: ni las migraciones ni el seed de roles, permisos y el cliente `web`. Quedan para cuando haya pipeline.
3. **Certificados de OpenIddict.** Los de firma y cifrado salen de los PFX de la tabla de arriba. Con varias instancias tienen que ser los mismos en todas.
4. **Data Protection.** Las claves quedan sin cifrar en Postgres. En producción: `ProtectKeysWithCertificate`.

## Administración (Fase 4)

Todo se maneja desde el panel, sin tocar la base ni la configuración del servidor: usuarios (alta, roles, activar, desactivar y eliminar), roles con sus permisos, y quién puede entrar al sistema.

**El modo de registro** (`/configuracion` en el front, `PUT /api/settings` en la Api) tiene dos valores:

| Modo | Qué pasa con un correo que no tiene cuenta |
|---|---|
| `InviteOnly` | No entra. La cuenta la tiene que crear un administrador. |
| `Open` | Se crea la cuenta sola, con el rol `User`. |

El valor inicial, al crear la base, sale de `Registration:Mode` y por defecto es **`InviteOnly`**: una instalación nueva arranca cerrada y se abre a propósito. Después, manda lo que diga la base: el seed no pisa la fila si ya existe.

La única excepción es el administrador inicial (`Seed:AdminEmail`): crea su cuenta en su primer ingreso, por código o con Google, en cualquier modo. Por eso, en una instalación nueva en `InviteOnly`, **sin `Seed:AdminEmail` no entra nadie**.

Desactivar o eliminar una cuenta le corta el acceso en el acto (se revocan sus tokens y se invalida su cookie), no solo en el próximo ingreso.

## WhatsApp en local

El ingreso con WhatsApp usa la app de Meta `4601782356805744` y su número de prueba. `src/ArquitecturaBase.Api/appsettings.Development.json` ya trae lo que no es secreto: `WhatsApp:PhoneNumberId` (el que prende el envío), el id de la cuenta y el número del bot. Lo secreto va en los user-secrets de la Api, y nunca en el chat ni en un archivo del repo.

### Secretos

| Clave | Qué es | De dónde sale |
|---|---|---|
| `WhatsApp:AccessToken` | el token del usuario del sistema, para mandar mensajes | [Configuración del negocio](https://business.facebook.com/latest/settings) › Usuarios del sistema, con `whatsapp_business_messaging` y `whatsapp_business_management` |
| `WhatsApp:AppSecret` | el secreto de la app, con el que Meta firma cada webhook | [Configuración › Básica](https://developers.facebook.com/apps/4601782356805744/settings/basic/) de la app |
| `WhatsApp:VerifyToken` | la palabra de verificación del webhook | la inventás vos, larga y al azar (solo letras y números, por ejemplo de un generador de contraseñas), y cargás la misma en Meta |

**El webhook se prende solo con `AppSecret` y `VerifyToken` juntos.** Sin ninguno, queda apagado: la Api arranca con un Warning que nombra las dos claves y el envío funciona igual. Con uno solo, la Api no arranca. Se leen al iniciar: después de cargarlos, reiniciá la Api.

Se cargan desde la raíz del repo, en PowerShell (sirve igual en Windows PowerShell 5.1 y en PowerShell 7). El valor se escribe sin que se vea. Con el SDK de .NET 10, `dotnet user-secrets set` solo nombra la clave al guardar, pero versiones viejas repetían también el valor, así que su salida va a `Out-Null` por las dudas. Eso se come la línea que confirma el guardado (los errores se siguen viendo): para confirmarlo está el comando que lista las claves, más abajo. No uses `-MaskInput`: en 5.1 no existe y el valor queda a la vista.

```powershell
$s = Read-Host "WhatsApp:AccessToken" -AsSecureString
dotnet user-secrets set "WhatsApp:AccessToken" (New-Object System.Net.NetworkCredential('', $s)).Password --project src/ArquitecturaBase.Api | Out-Null
Remove-Variable s
```

```powershell
$s = Read-Host "WhatsApp:AppSecret" -AsSecureString
dotnet user-secrets set "WhatsApp:AppSecret" (New-Object System.Net.NetworkCredential('', $s)).Password --project src/ArquitecturaBase.Api | Out-Null
Remove-Variable s
```

```powershell
$s = Read-Host "WhatsApp:VerifyToken" -AsSecureString
dotnet user-secrets set "WhatsApp:VerifyToken" (New-Object System.Net.NetworkCredential('', $s)).Password --project src/ArquitecturaBase.Api | Out-Null
Remove-Variable s
```

Para confirmar qué claves quedaron cargadas, sin mostrar los valores:

```powershell
(dotnet user-secrets list --project src/ArquitecturaBase.Api) -replace ' = .*', ''
```

### El túnel, para recibir los webhooks

Meta le pega al webhook desde internet y exige HTTPS con un certificado válido: el de desarrollo de `localhost` no le sirve. Por eso el AppHost puede levantar un [dev tunnel](https://aspire.dev/integrations/devtools/dev-tunnels/) de Microsoft. **Viene apagado**, así `aspire run` no le pide la CLI a quien no la usa.

1. **Una sola vez:** instalá la CLI con `winget install Microsoft.devtunnel`, abrí una terminal nueva (para que tome el `PATH`) e iniciá sesión con `devtunnel user login`. La sesión dura unos días: si el túnel no arranca, `devtunnel user show` dice si venció, y se renueva con el mismo `devtunnel user login`.
2. **Prendé el túnel** en los user-secrets del AppHost. Este valor no es secreto: va ahí para que cada uno lo prenda en su máquina sin tocar el repo.

   ```powershell
   dotnet user-secrets set "DevTunnel:Enabled" "true" --project src/ArquitecturaBase.AppHost
   ```

3. **`aspire run`.** En el dashboard aparece el recurso `tunnel` y, debajo, `tunnel-api-https`. La URL de este último es la dirección pública de la Api, del estilo `https://tunnel-xxxxxxxx-7180.brs.devtunnels.ms`. El enlace "Inspect" es el inspector del túnel y no se carga en Meta. En esta máquina la URL es siempre la misma, porque la región es fija y el id sale de la ruta del AppHost, y queda reservada 30 días aunque no se use. El id no está escrito en el repo porque forma parte de la dirección pública: es único entre todos los usuarios de Dev Tunnels y uno fijo sería fácil de adivinar. Si hace falta uno a mano (de 3 a 60 caracteres, minúsculas, números y guiones), va en `DevTunnel:TunnelId`, en los user-secrets del AppHost.
4. **En Meta**, en [Paso 2. Configuración de producción](https://developers.facebook.com/apps/4601782356805744/use_cases/customize/wa-configurations-v2/?use_case_enum=WHATSAPP_BUSINESS_MESSAGING) › Configurar webhooks:
   - la URL de devolución de llamada es `https://<la-url-del-túnel>/webhooks/whatsapp`;
   - la palabra de verificación es la misma de `WhatsApp:VerifyToken`;
   - tocá **Verificar y guardar** (Meta hace un GET y la Api le responde el `challenge`);
   - suscribí el campo `messages`.

   Como la URL no cambia, esto se hace una sola vez.
5. **Al terminar, `aspire stop`.** El túnel expone solo el endpoint `https` de la Api (ni el front, ni Postgres), con acceso anónimo en ese puerto porque Meta no inicia sesión. Pero mientras está prendido **la Api entera queda en internet**, no solo el webhook: se prende para probar y se apaga al terminar.

Para que `aspire run` deje de levantar el túnel: `dotnet user-secrets remove "DevTunnel:Enabled" --project src/ArquitecturaBase.AppHost`.

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
