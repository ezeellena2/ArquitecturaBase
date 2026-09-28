# Despliegue

Para quien despliega la Api fuera de Development: en qué orden sale cada cosa, qué hace la Api al arrancar, qué configuración tiene que estar sí o sí y qué queda pendiente. La infraestructura de Azure, paso a paso y una sola vez, está en [`docs/deploy/azure-setup.md`](../deploy/azure-setup.md); la decisión de fondo, en el [ADR 0006](../decisions/0006-migraciones-y-seed-fuera-de-development.md). Todo lo que sigue sale del código a la fecha de la guía (2026-09-28): si cambia una clave o un archivo, se corrige acá en el mismo commit.

Índice:

1. [Antes que nada: el front no llega a la imagen](#antes-que-nada-el-front-no-llega-a-la-imagen)
2. [El orden: bundle, después imagen](#el-orden-bundle-después-imagen)
3. [Qué hace la Api al arrancar, por ambiente](#qué-hace-la-api-al-arrancar-por-ambiente)
4. [Qué hace el seed en cada arranque](#qué-hace-el-seed-en-cada-arranque)
5. [Configuración obligatoria en Production](#configuración-obligatoria-en-production)
6. [Proxy y encabezados reenviados](#proxy-y-encabezados-reenviados)
7. [Certificados de OpenIddict](#certificados-de-openiddict)
8. [Data Protection](#data-protection)
9. [Colas en memoria](#colas-en-memoria)
10. [Lista antes del primer despliegue](#lista-antes-del-primer-despliegue)

## Antes que nada: el front no llega a la imagen

**Urgente, y no lo resuelve nadie todavía.** La Api sabe servir el SPA desde `wwwroot/`, pero ningún paso copia ahí el `dist/` del front: el `.csproj` no tiene un `Target`, no hay Dockerfile y [`deploy.yml`](../../.github/workflows/deploy.yml) publica la Api sin mencionar al front. Sin ese paso la Api arranca igual, pero **el sitio responde 404 en `/`** y solo anda la Api (sin `wwwroot/index.html`, `UseSpaFallback` no se instala). Falta correr `npm ci && npm run build` en `../ArquitecturaBaseFront` y copiar el resultado a `src/ArquitecturaBase.Api/wwwroot/` antes del `dotnet publish`. Dos detalles: el front vive en otro repo, así que el checkout tiene que traer los dos; y el `dist/` incluye `silent-renew.html`, que hace falta para la renovación silenciosa de la sesión.

## El orden: bundle, después imagen

El pipeline es [`.github/workflows/deploy.yml`](../../.github/workflows/deploy.yml), con dos jobs:

1. **`build`:** compila, corre `dotnet test` (con Docker, como en tu máquina), publica la imagen en el registry con la etiqueta del SHA (`:57-64`) y genera el bundle de migraciones (`:72-81`), un ejecutable autónomo que no toca ninguna base al generarse. La cadena de conexión de ese paso es de mentira.
2. **`deploy`:** abre el firewall de Postgres para el runner, **aplica el bundle** contra la base real (`:123-126`), cierra el firewall (`:128-135`) y **recién después** apunta la Container App a la imagen nueva (`:139-144`).

Si una migración falla, el job corta antes de desplegar y producción sigue con la imagen vieja: nunca queda código nuevo contra un esquema sin migrar. Al revés sí pasa un rato: entre el bundle y la revisión nueva, la imagen vieja corre contra el esquema nuevo. Por eso una migración tiene que ser compatible con la versión anterior (primero se agrega, en otro despliegue se borra); lo mismo vale si volvés a una imagen vieja.

Ni el bundle ni `dotnet ef` corren el código de arranque de la sección siguiente: cortan el programa en `Build()`, antes de él.

## Qué hace la Api al arrancar, por ambiente

`Program.cs` llama a `InitializeDatabaseAsync` (`src/ArquitecturaBase.Infrastructure/Persistence/DatabaseInitialization.cs`) antes de atender pedidos. El ambiente sale de `ASPNETCORE_ENVIRONMENT`; Staging o cualquier otro nombre se comporta como Production.

| Ambiente | Qué hace, en orden |
|---|---|
| Development | valida las opciones, aplica las migraciones y siembra |
| Testing (solo el arnés de tests) | nada: el arnés crea el esquema y siembra después |
| Production y cualquier otro | valida las opciones; si falta aplicar alguna migración, **no arranca**; siembra |

- **Primero valida las opciones**, lo mismo que `ValidateOnStart` pero antes de tocar la base (`cd974cc`): una configuración mal escrita se ve como tal, y no detrás de un error de la base ni después de un seed que ya corrió. Los certificados de OpenIddict se leen antes todavía, al registrar los servicios.
- **No migra.** Si la base tiene migraciones sin aplicar, lanza `InvalidOperationException` con la lista y un mensaje que dice que se apliquen con el bundle. El chequeo es explícito porque una base vieja no haría fallar al seed, que solo toca roles, ajustes y OpenIddict.
- **Base caída o sin migrar: la Api no arranca.** El chequeo de migraciones ya necesita la base, así que con la base caída también lanza. El proceso termina con error y el orquestador lo reinicia hasta que la base responde (Container Apps reinicia el contenedor que se cae). Según `deploy.yml`, Container Apps le pasa el tráfico a la revisión nueva recién cuando está sana, así que mientras tanto sigue atendiendo la anterior.
- **El seed espera su lock como mucho el timeout de comando de Npgsql, 30 s por defecto.** Dos réplicas que arrancan juntas siembran en fila (ver la sección siguiente); si una esperara más que eso, el comando vence, la réplica no arranca y el orquestador la reinicia. El seed dura mucho menos, así que no debería pasar; si pasa, se sube con `Command Timeout=<segundos>` en la cadena de conexión.
- Después, fuera de Development: HSTS, sin OpenAPI.

## Qué hace el seed en cada arranque

Corre en cada arranque de cada réplica, fuera de Testing. Es idempotente: crea lo que falta y no duplica nada. Lo arma `DatabaseSeeder` (`src/ArquitecturaBase.Infrastructure/Persistence/Seed/`): todo en **una transacción** (si algo lanza, no queda nada a medias) y **en fila entre réplicas**, con el advisory lock `seed:database` que toma adentro. En orden:

1. **Roles** (`RoleSeeder`). Crea `Admin` y `User` si no existen, y a `Admin` le suma los permisos de `Permissions.All` que le falten: un permiso nuevo del código le llega a Admin en el próximo despliegue. No le saca ninguno a nadie, a `User` no le da ninguno y no toca los roles creados desde el panel.
2. **La cuenta de `Seed:AdminEmail`** (en el mismo `RoleSeeder`). Es la cuenta del dueño de la plataforma. **El seed nunca crea cuentas**, ni de empresas ni de nadie: si esa cuenta existe, se asegura de que tenga el rol Admin, y se lo devuelve en cada arranque si alguien se lo sacó. Si no existe, no hace nada. La cuenta la crea el propio dueño en su primer ingreso (con código o con Google), que se le permite aunque el registro sea solo por invitación (`AccountCreationPolicy`), y nace con el rol Admin. Los administradores de las empresas los agrega el dueño desde el panel, y el seed no los toca.
3. **La fila de ajustes** (`SystemSettingsSeeder`). Si no existe, la crea con el modo de registro de `Registration:Mode` (`InviteOnly` si no se configura; un valor que no es `InviteOnly` ni `Open`, como `Registration__Mode=5`, frena el arranque en la validación de opciones, antes de tocar la base). Si ya existe, manda la base: un despliegue nunca pisa lo que se cambió desde el panel.
4. **OpenIddict** (`OpenIddictSeeder`). El scope `api` y el cliente público `web` (PKCE), creados o **realineados con la configuración**: los permisos, los requisitos y las URIs del cliente `web` quedan exactamente como dicen el código y `Authentication:Clients:Web`. **Lo que se haya cargado a mano en la base sobre ese cliente o ese scope se borra en el próximo arranque**; para cambiar una URI, se cambia la configuración. Otros clientes no se tocan.

El caché de permisos no molesta: es local a cada proceso y arranca vacío, así que ve lo que el seed acaba de sumar.

## Configuración obligatoria en Production

Fuera de Aspire la Api no recibe nada sola. Con variables de entorno, el `:` se escribe `__` (`ConnectionStrings__appdb`). Lo que es secreto va como secreto del orquestador (en Container Apps, con `secretref:`), nunca en el repo. Cómo cargarlas en Azure: [azure-setup.md, paso 6](../deploy/azure-setup.md#6-configuración-de-la-aplicación). Rutas desde `src/`:

| Clave | Qué pide | Dónde lo exige el código | Si falta |
|---|---|---|---|
| `ConnectionStrings:appdb` | la cadena de Postgres | `ArquitecturaBase.Infrastructure/Persistence/PersistenceRegistration.cs:71-74` (el nombre, en `DependencyInjection.cs:21`) | no arranca: lanza al crear el contexto, en el chequeo de migraciones |
| `Authentication:LoginCode:HashKey` | al menos 32 bytes aleatorios en base64 (`openssl rand -base64 48`) | `ArquitecturaBase.Infrastructure/Security/LoginCodeHashOptions.cs:14-26`, registrado en `DependencyInjection.cs:38-41` | no arranca (validación de opciones) |
| `Authentication:Certificates:Signing:Base64` o `:Path`, y `:Password` | el PFX de firma de OpenIddict; `Base64` gana sobre `Path` | `ArquitecturaBase.Infrastructure/Identity/OpenIddict/CertificateLoader.cs:15-35`, llamado en `OpenIddictRegistration.cs:98-103` | no arranca: se lee al registrar los servicios |
| `Authentication:Certificates:Encryption:Base64` o `:Path`, y `:Password` | el PFX de cifrado, igual que el anterior | los mismos | igual |
| `Authentication:Issuer` | el origen público de la web, el que ve el navegador (no el de la Api detrás del proxy). De él salen el botón de las invitaciones por correo y los enlaces del bot, y OpenIddict lo usa como issuer | `ArquitecturaBase.Infrastructure/Emails/EmailRegistration.cs:32-36` (fuera de Development y Testing, siempre); `WhatsApp/WhatsAppRegistration.cs:125-126` (con el webhook, en cualquier ambiente); `Identity/OpenIddict/OpenIddictRegistration.cs:59-64` | no arranca (validación de opciones) |
| `Authentication:Clients:Web:RedirectUris` | al menos una URI, del origen público (`…/auth/callback` y `…/silent-renew.html`) | `ArquitecturaBase.Infrastructure/Identity/OpenIddict/WebClientOptions.cs:10-11`, registrado en `OpenIddictRegistration.cs:20-23` | no arranca. Alimenta el cliente `web` del seed |
| `Authentication:Clients:Web:PostLogoutRedirectUris` | al menos una URI (`…/login`) | `WebClientOptions.cs:13-14` | igual |
| `Authentication:Google:ClientSecret` | el secreto de OAuth de Google. El `ClientId` ya viene en `appsettings.json`, así que Google está prendido | `ArquitecturaBase.Infrastructure/Identity/IdentityRegistration.cs:107-128` y `:148` | no arranca. Para apagar Google, dejá `Authentication:Google:ClientId` vacío |
| `Email:Smtp:UserName`, `Email:Smtp:Password` y `Email:Smtp:FromAddress` | la cuenta que envía; la contraseña es una contraseña de aplicación, como secreto. `Email:Delivery` es `Smtp` por defecto y `appsettings.json` trae los otros datos de Gmail | `ArquitecturaBase.Infrastructure/Emails/SmtpOptions.cs:22-33`, `SmtpOptionsValidator.cs`, y el `Smtp` por defecto en `EmailOptions.cs:9` | no arranca (validación de opciones) |
| `Seed:AdminEmail` | el correo del dueño de la plataforma. Opcional para arrancar, pero con `InviteOnly` (el modo por defecto) es la única cuenta que se puede crear en una base nueva: sin él no entra nadie | `ArquitecturaBase.Infrastructure/Identity/SeedOptions.cs:14-22`; `ArquitecturaBase.Application/Services/Auth/AccountCreationPolicy.cs:20-22` | arranca, pero nadie puede entrar. Con un correo inválido, no arranca |
| `ForwardedHeaders:TrustAll`, o `:KnownProxies` / `:KnownNetworks` | detrás de un proxy: a quién se le creen `X-Forwarded-For` y `X-Forwarded-Proto`. En Container Apps, `TrustAll=true`, solo porque Kestrel no se alcanza por fuera del ingress | `ArquitecturaBase.Api/Hosting/ForwardedHeadersExtensions.cs:24-47` | arranca, pero la IP y el esquema son los del proxy: el rate limit y la redirección HTTPS se equivocan |
| `WhatsApp:PhoneNumberId`, `WhatsApp:AccessToken` y, para el webhook, `WhatsApp:AppSecret` con `WhatsApp:VerifyToken` | solo si se prende WhatsApp: `PhoneNumberId` es el interruptor, y el webhook necesita los dos secretos juntos | `ArquitecturaBase.Infrastructure/WhatsApp/WhatsAppRegistration.cs:32-37`, `WhatsAppOptionsValidator.cs:19-45` | sin `PhoneNumberId`, WhatsApp queda apagado y la Api arranca; con él y sin token, o con un solo secreto del webhook, no arranca |

Además, hay que reemplazar `AllowedHosts: "*"` por los hosts públicos (la Api arranca igual con `*`): el porqué está en [Proxy y encabezados reenviados](#proxy-y-encabezados-reenviados), junto con lo de `X-Forwarded-Host`. El resto de las claves (`Authentication:LoginCode:*`, `RateLimiting:*`, `WhatsApp:*` sin los de arriba, `Email:QueueCapacity`) trae valores por defecto válidos en `appsettings.json` o en sus clases de opciones.

## Proxy y encabezados reenviados

La Api procesa `X-Forwarded-For` y `X-Forwarded-Proto` antes del rate limiter, la autenticación y la redirección HTTPS. Por defecto solo confía en los proxies loopback de ASP.NET Core. Un despliegue puede declarar `ForwardedHeaders:KnownProxies` (direcciones IP) o `ForwardedHeaders:KnownNetworks` (CIDR). Azure Container Apps, cuyas IP internas pueden cambiar, usa `ForwardedHeaders:TrustAll=true`; esto solo es seguro cuando Kestrel no es accesible por fuera del ingress confiable.

`X-Forwarded-Host` no se acepta a propósito: OpenIddict usa `Request.Host` para construir URLs públicas, y confiar ese encabezado sin una lista explícita permitiría que un cliente las manipule. El reverse proxy tiene que conservar el host público en el encabezado HTTP `Host` (en nginx, por ejemplo, `proxy_set_header Host $host`), y producción debe reemplazar `AllowedHosts: "*"` por los hosts públicos permitidos, separados por `;` si hay más de uno.

## Certificados de OpenIddict

Fuera de Development y Testing, OpenIddict firma y cifra los tokens con dos PFX propios: no son certificados TLS, pueden ser autofirmados y de larga duración, y se pasan en base64 como secretos, sin archivos en el contenedor. Con varias réplicas tienen que ser los mismos en todas. Cómo generarlos y cargarlos: [azure-setup.md, "Certificados de OpenIddict"](../deploy/azure-setup.md#certificados-de-openiddict). Si se pierden y se regeneran, todos los tokens emitidos dejan de valer y las sesiones abiertas se caen.

## Data Protection

Las claves de Data Protection (la cookie de `/account` y `/connect`, y lo que Identity protege) se guardan en Postgres, en la tabla `DataProtectionKeys`, con `PersistKeysToDbContext` (`src/ArquitecturaBase.Infrastructure/Identity/IdentityRegistration.cs:84-86`): así valen entre réplicas y después de un reinicio. **No hay `ProtectKeysWith…`, así que quedan sin cifrar en la base**: quien lea esa tabla puede falsificar cookies. Hasta que se cifren (por ejemplo, con `ProtectKeysWithCertificate`), el acceso a la base es el que las protege.

## Colas en memoria

El correo y los mensajes de WhatsApp salen en segundo plano por colas en memoria, no por un outbox transaccional. **Un reinicio o un despliegue pierde lo que estaba encolado y no salió**: la persona pide otro código, o el admin reenvía la invitación. Con la cola llena el mensaje se descarta con un log. La capacidad es `Email:QueueCapacity` y `WhatsApp:QueueCapacity` (100 por defecto). El detalle, y qué hace cada llamador con la cola llena, está en [backend.md, "Colas en memoria"](../architecture/backend.md#colas-en-memoria).

## Lista antes del primer despliegue

- [ ] La infraestructura de [azure-setup.md](../deploy/azure-setup.md) creada, con la base `appdb` y los secretos de GitHub.
- [ ] El `dist/` del front llega a `wwwroot/` ([arriba](#antes-que-nada-el-front-no-llega-a-la-imagen)).
- [ ] Cada clave de la [tabla](#configuración-obligatoria-en-production), con `Authentication:Issuer` y las URIs del cliente `web` apuntando al origen público, no a `localhost`.
- [ ] La URL de producción entre los redirect URIs autorizados en la consola de Google.
- [ ] `AllowedHosts` con los hosts públicos, y el proxy conservando el `Host` público ([Proxy y encabezados reenviados](#proxy-y-encabezados-reenviados)).
- [ ] Los dos PFX guardados en un lugar seguro.
- [ ] Después del primer arranque: el dueño entra con el correo de `Seed:AdminEmail` y queda como Admin.
