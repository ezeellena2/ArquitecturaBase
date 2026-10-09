# Despliegue

Para quien despliega la Api fuera de Development: en qué orden sale cada cosa, qué hace la Api al arrancar, qué configuración tiene que estar sí o sí y qué queda pendiente. La infraestructura de Azure, paso a paso y una sola vez, está en [`docs/deploy/azure-setup.md`](../deploy/azure-setup.md); la decisión de fondo, en el [ADR 0006](../decisions/0006-migraciones-y-seed-fuera-de-development.md). La puesta en marcha de dev y GitHub está en [azure-dev.md](../deploy/azure-dev.md). Reconciliada con el código y el workflow local el **2026-10-02**. Describe su configuración, no acredita un despliegue ejecutado en Azure.

Índice:

1. [Front dentro de la imagen](#antes-que-nada-el-front-no-llega-a-la-imagen)
2. [El orden: bundle, después imagen](#el-orden-bundle-después-imagen)
3. [Qué hace la Api al arrancar, por ambiente](#qué-hace-la-api-al-arrancar-por-ambiente)
4. [Qué hace el seed en cada arranque](#qué-hace-el-seed-en-cada-arranque)
5. [Configuración obligatoria en Production](#configuración-obligatoria-en-production)
6. [Proxy y encabezados reenviados](#proxy-y-encabezados-reenviados)
7. [Certificados de OpenIddict](#certificados-de-openiddict)
8. [Data Protection](#data-protection)
9. [Colas transaccionales](#colas-transaccionales)
10. [Lista antes del primer despliegue](#lista-antes-del-primer-despliegue)

El [registro de preparación](../plans/verificacion-y-produccion.md) conserva los recorridos
manuales, las dependencias externas y los pendientes de privacidad rescatados de planes cerrados.
No se consideran completados por pasar las suites automáticas.

<a id="antes-que-nada-el-front-no-llega-a-la-imagen"></a>

## Front dentro de la imagen

El workflow descarga ambos repositorios, verifica y compila React y copia **todo** `dist/` a `src/ArquitecturaBase.Api/wwwroot/` antes de publicar .NET. Incluye `silent-renew.html` y compara por SHA-256 todos los archivos del front con los del publicado. `wwwroot/` es generado e ignorado por Git: la fuente se conserva en el repo del front. La Api sirve el SPA desde ese directorio con un solo origen. Sin `wwwroot/index.html`, `UseSpaFallback` no se instala y `/` devuelve 404; empaquetar el front es parte obligatoria de esta pipeline.

## El orden: bundle, después imagen

El pipeline es [`.github/workflows/deploy.yml`](../../.github/workflows/deploy.yml), con dos jobs:

1. **`build`:** verifica React y .NET, incluyendo integración con Docker; produce una imagen Linux en archivo, el bundle EF y un manifest con los dos commits. No se autentica en Azure. La etiqueta es única por ejecución/intento, sin `latest`.
2. **`deploy`:** solo en `main` y con la variable de repositorio `DEV_DEPLOY_ENABLED=true`, usa el environment `dev`. Omite el paquete si cualquiera de sus commits fue superado por `main`. Se autentica con OIDC, sube la imagen, abre acceso temporal para el bundle, aplica migraciones y cierra el acceso incluso ante fallas. **Recién después** actualiza la Container App en modo Single. Espera la revisión concreta lista y comprueba `/health`, `/`, `/silent-renew.html` y el discovery de OpenIddict.

Los despliegues se serializan; GitHub conserva solo el último pendiente. Un push del front solicita esta pipeline del backend con un token limitado. Variables, secrets e identidades: [azure-dev.md](../deploy/azure-dev.md). No se resetea la base ni se crean recursos desde el workflow. Una falla posterior a migrar no revierte el esquema; una falla de la comprobación pública tampoco hace rollback automático.

Si una migración falla, el job corta antes de desplegar y producción sigue con la imagen vieja: nunca queda código nuevo contra un esquema sin migrar. Al revés sí pasa un rato: entre el bundle y la revisión nueva, la imagen vieja corre contra el esquema nuevo. Por eso una migración tiene que ser compatible con la versión anterior (primero se agrega, en otro despliegue se borra); lo mismo vale si volvés a una imagen vieja.

Dos migraciones de la plantilla no cumplen esa regla, a propósito, porque no había ningún proyecto con WhatsApp en producción: `UserInvitationProviderMessageId` renombra la columna `WaMessageId` y `LoginCodePhoneChannel` cambia el canal guardado de los códigos por teléfono de `'WhatsApp'` a `'Phone'`. Entre el bundle y la imagen nueva, la imagen anterior no lee esa columna ni ese valor, y sigue escribiendo los viejos: no puede guardar el id de Meta de una invitación que mande en ese rato (la columna ya no existe), y un código que emita queda con `'WhatsApp'`. La imagen nueva no lee ese código (EF lanza al convertir un nombre que el enum no tiene), y como nada lo invalida sin leerlo, ese número responde 500 al pedir o verificar un código hasta que se corrija la fila. Por eso, **después** de que la imagen nueva queda arriba, se vuelve a correr el `UPDATE` de la migración, que no cambia nada si no quedó ninguno:

```sql
UPDATE "LoginCodes" SET "Channel" = 'Phone' WHERE "Channel" = 'WhatsApp';
```

Un proyecto derivado que ya esté en producción con WhatsApp las tiene que partir en dos despliegues, en lugar de estas dos migraciones:

1. **Primero:** una migración que agrega `ProviderMessageId` y copia `WaMessageId`, y una versión que lee y escribe solo `ProviderMessageId` (su modelo ya no mapea `WaMessageId`) y escribe `'Phone'`, pero también lee `'WhatsApp'` (un conversor que traduzca el nombre viejo).
2. **Después:** una migración que vuelve a copiar lo que la imagen anterior escribió en `WaMessageId` después de la primera copia, pasa a `'Phone'` los códigos que hayan quedado con `'WhatsApp'` y recién entonces borra la columna, y una versión que ya no lee `'WhatsApp'`:

```sql
UPDATE "UserInvitations" SET "ProviderMessageId" = "WaMessageId" WHERE "ProviderMessageId" IS NULL AND "WaMessageId" IS NOT NULL;
UPDATE "LoginCodes" SET "Channel" = 'Phone' WHERE "Channel" = 'WhatsApp';
```

Ni el bundle ni `dotnet ef` corren el código de arranque de la sección siguiente: cortan el programa en `Build()`, antes de él.

Tanto la generación como la ejecución del bundle declaran `ConnectionStrings:appdb` y `ConnectionStrings:cache` para construir el host y el contexto. El pipeline usa valores de mentira para esa construcción; al aplicar, `--connection` reemplaza la conexión de PostgreSQL con el secreto real. El cliente Redis no se resuelve y el seed no corre, así que aplicar migraciones no necesita Redis. Declarar esas conexiones no sustituye la conexión real de Redis que necesita la Api desplegada.

## Qué hace la Api al arrancar, por ambiente

`Program.cs` llama a `InitializeDatabaseAsync` (`src/ArquitecturaBase.Infrastructure/Persistence/DatabaseInitialization.cs`) antes de atender pedidos. El ambiente sale de `ASPNETCORE_ENVIRONMENT`; Staging o cualquier otro nombre se comporta como Production.

| Ambiente | Qué hace, en orden |
|---|---|
| Development | valida las opciones, aplica las migraciones y siembra |
| Testing (solo el arnés de tests) | nada: el arnés crea el esquema y siembra después |
| Production y cualquier otro | valida las opciones; si la base no responde o falta aplicar alguna migración, **no arranca**; siembra |

- **Primero valida las opciones**, lo mismo que `ValidateOnStart` pero antes de tocar la base (`cd974cc`): una configuración mal escrita se ve como tal, y no detrás de un error de la base ni después de un seed que ya corrió. Los certificados de OpenIddict se leen antes todavía, al registrar los servicios.
- **No migra.** Si la base tiene migraciones sin aplicar, lanza `InvalidOperationException` con la lista y un mensaje que dice que se apliquen con el bundle. El chequeo es explícito porque una base vieja no haría fallar al seed, que solo toca roles, ajustes y OpenIddict.
- **Base caída o sin migrar: la Api no arranca.** Antes del chequeo de migraciones prueba la conexión (abre una y corre `SELECT 1`): si el servidor no responde, lanza `InvalidOperationException` con un mensaje que dice eso, que la base no responde, y no que falten migraciones; la causa (conexión rechazada, timeout, contraseña mal escrita) va como `InnerException`. Sin esa prueba, un reset de conexión puede hacer que EF crea que la base no existe y liste todas las migraciones como pendientes, y el error mandaría a correr el bundle. No usa `Database.CanConnectAsync`, que también da `false` cuando la base no existe: una base que nadie creó (un despliegue sin el bundle) sí responde, y sigue cayendo en el chequeo de migraciones, que manda al bundle. El proceso termina con error y el orquestador lo reinicia hasta que la base responde (Container Apps reinicia el contenedor que se cae). El workflow espera la revisión y comprueba `/health` públicamente, pero no configura los probes. Verificar PostgreSQL y Redis **antes de recibir tráfico** requiere configurar `/health` como readiness en Container Apps (ver [Probes](../deploy/azure-setup.md#probes-opcional)).
- **El seed espera su lock como mucho el timeout de comando de Npgsql, 30 s por defecto.** Dos réplicas que arrancan juntas siembran en fila (ver la sección siguiente); si una esperara más que eso, el comando vence, la réplica no arranca y el orquestador la reinicia. El seed dura mucho menos, así que no debería pasar; si pasa, se sube con `Command Timeout=<segundos>` en la cadena de conexión.
- Después, fuera de Development: HSTS, sin OpenAPI.

## Qué hace el seed en cada arranque

Corre en cada arranque de cada réplica, fuera de Testing. Es idempotente: crea lo que falta y no duplica nada. Lo arma `DatabaseSeeder` (`src/ArquitecturaBase.Infrastructure/Persistence/Seed/`): todo en **una transacción** (si algo lanza, no queda nada a medias) y **en fila entre réplicas**, con el advisory lock `seed:database` que toma adentro. En orden:

1. **Roles** (`RoleSeeder`). Crea `Admin` y `User` si no existen, y a `Admin` le suma los permisos de `IPermissionCatalog.All` que le falten: incluye el núcleo y las fuentes de módulos presentes. Un permiso nuevo del código le llega a Admin en el próximo despliegue. No elimina claims históricos; los de una fuente retirada dejan de ser permisos efectivos. A `User` no le da ninguno y no toca los roles creados desde el panel.
2. **La cuenta de `Seed:AdminEmail`** (en el mismo `RoleSeeder`). Es el administrador inicial de esta instalación, que atiende un solo negocio. **El seed nunca crea cuentas**: si esa cuenta existe, se asegura de que tenga el rol Admin, y se lo devuelve en cada arranque si alguien se lo sacó. Si no existe, no hace nada. La cuenta la crea su titular en su primer ingreso (con código o con Google), que se le permite aunque el registro sea solo por invitación (`AccountCreationPolicy`), y nace con el rol Admin. Las demás cuentas y roles se administran desde el panel. Otra empresa usa otra instalación con datos, recursos, secretos y administrador inicial propios; no hay administración central de empresas.
3. **La fila de ajustes** (`SystemSettingsSeeder`). Si no existe, la crea con `Registration:Mode`: el `appsettings.json` de la plantilla configura `Open`; `RegistrationOptions` respalda con `InviteOnly` si falta la clave. Un valor fuera del enum, como `Registration__Mode=5`, frena el arranque antes de tocar la base. `SystemSettings.Create` agrega cultura `es`, zona `America/Argentina/Buenos_Aires`, tamaño de página 20 y revisión 1. Si la fila ya existe, manda la base: el seed no pisa ninguno de sus ajustes.
4. **OpenIddict** (`OpenIddictSeeder`). El scope `api` y el cliente público `web` (PKCE), creados o **realineados con la configuración**: los permisos, los requisitos y las URIs del cliente `web` quedan exactamente como dicen el código y `Authentication:Clients:Web`. **Lo que se haya cargado a mano en la base sobre ese cliente o ese scope se borra en el próximo arranque**; para cambiar una URI, se cambia la configuración. Otros clientes no se tocan.

Después del commit, el seed descarta en Redis los permisos de los roles del sistema y los ajustes. Redis puede seguir caliente después de reiniciar una Api. Identity avanza el `ConcurrencyStamp` cuando suma claims; las próximas lecturas seleccionan esa nueva revisión. Si Redis falla en ese descarte, el seed ya se confirmó, pero el arranque falla y el orquestador puede reintentarlo: el seed es idempotente.

## Configuración obligatoria en Production

Con el módulo IA presente, el startup exige `ArtificialIntelligence:SecretProtection`:
InstallationId, ActiveKeyId y claves externas de 32 bytes base64. Todas las réplicas comparten
el anillo y el ID; negocios independientes no los comparten. No se requiere clave de proveedor
para arrancar y no se prueba el proveedor en readiness. Provisioná antes de publicar, fuera
de la base/imagen, y conservá claves históricas con backups. La [guía de IA](configurar-ia.md)
fija variables, rotación y restauración. La migración AiConnection es aditiva; el workflow
conserva secretos del host y no provisiona este anillo. Azure real sigue sin verificar.

Fuera de Aspire la Api no recibe nada sola. Con variables de entorno, el `:` se escribe `__` (`ConnectionStrings__appdb`). Lo que es secreto va como secreto del orquestador (en Container Apps, con `secretref:`), nunca en el repo. Cómo cargarlas en Azure: [azure-setup.md, paso 6](../deploy/azure-setup.md#6-configuración-de-la-aplicación). Rutas desde `src/`:

| Clave | Qué pide | Dónde lo exige el código | Si falta |
|---|---|---|---|
| `ConnectionStrings:appdb` | la cadena de Postgres | `ArquitecturaBase.Infrastructure/Persistence/PersistenceRegistration.cs` (el nombre, en `DependencyInjection.cs`) | no arranca: lanza al crear el contexto, en el chequeo de migraciones |
| `ConnectionStrings:cache` | conexión de Redis, como secreto; TLS y acceso privado en producción | `ArquitecturaBase.Infrastructure/Caching/CachingRegistration.cs` | no arranca sin conexión configurada; Redis inaccesible falla el descarte del seed |
| `Authentication:LoginCode:HashKey` | al menos 32 bytes aleatorios en base64 (`openssl rand -base64 48`) | `ArquitecturaBase.Infrastructure/Security/LoginCodeHashOptions.cs`, registrado en `DependencyInjection.cs` | no arranca (validación de opciones) |
| `Authentication:Certificates:Signing:Base64` o `:Path`, y `:Password` | el PFX de firma de OpenIddict; `Base64` gana sobre `Path` | `ArquitecturaBase.Infrastructure/Identity/OpenIddict/CertificateLoader.cs`, llamado en `OpenIddictRegistration.cs` | no arranca: se lee al registrar los servicios |
| `Authentication:Certificates:Encryption:Base64` o `:Path`, y `:Password` | el PFX de cifrado, igual que el anterior | los mismos | igual |
| `Authentication:Certificates:DataProtection:Base64` o `:Path`, y `:Password` | PFX con clave privada compartido entre réplicas | `Identity/DataProtectionRegistration.cs` | no arranca sin cifrado de claves |
| `Authentication:Issuer` | el origen público de la web, el que ve el navegador (no el de la Api detrás del proxy). De él salen el botón de las invitaciones por correo y los botones públicos de orientación de acceso, y OpenIddict lo usa como issuer | `ArquitecturaBase.Infrastructure/Emails/EmailRegistration.cs` (fuera de Development y Testing, siempre); `ArquitecturaBase.Infrastructure/Modules/WhatsApp/WhatsAppInfrastructureRegistration.cs` (con el webhook, en cualquier ambiente); `ArquitecturaBase.Infrastructure/Identity/OpenIddict/OpenIddictRegistration.cs` | no arranca (validación de opciones) |
| `Authentication:Clients:Web:RedirectUris` | al menos una URI, del origen público (`…/auth/callback` y `…/silent-renew.html`) | `ArquitecturaBase.Infrastructure/Identity/OpenIddict/WebClientOptions.cs`, registrado en `OpenIddictRegistration.cs` | no arranca. Alimenta el cliente `web` del seed |
| `Authentication:Clients:Web:PostLogoutRedirectUris` | al menos una URI (`…/login`) | `WebClientOptions.cs` | igual |
| `Authentication:Google:ClientSecret` | el secreto de OAuth de Google. El `ClientId` ya viene en `appsettings.json`, así que Google está prendido | `ArquitecturaBase.Infrastructure/Identity/IdentityRegistration.cs`  | no arranca. Para apagar Google, dejá `Authentication:Google:ClientId` vacío |
| `Email:Smtp:UserName`, `Email:Smtp:Password` y `Email:Smtp:FromAddress` | la cuenta que envía; la contraseña es una contraseña de aplicación, como secreto. `Email:Delivery` es `Smtp` por defecto y `appsettings.json` trae los otros datos de Gmail | `ArquitecturaBase.Infrastructure/Emails/SmtpOptions.cs`, `SmtpOptionsValidator.cs`, y el `Smtp` por defecto en `EmailOptions.cs` | no arranca (validación de opciones) |
| `Seed:AdminEmail` | correo del administrador inicial. Opcional para arrancar; permite crear esa cuenta en cualquier modo y el seed le asegura Admin. Con `InviteOnly` y sin cuentas previas es la única excepción al registro cerrado | `ArquitecturaBase.Infrastructure/Identity/SeedOptions.cs` y `ArquitecturaBase.Application/Services/Auth/AccountCreationPolicy.cs` | arranca; con `Open` permite registro de cuentas User, pero no crea un administrador inicial. Con `InviteOnly` y sin cuentas previas no se puede crear ninguna cuenta. Un correo inválido impide arrancar |
| `ForwardedHeaders:TrustAll`, o `:KnownProxies` / `:KnownNetworks` | detrás de un proxy: a quién se le creen `X-Forwarded-For` y `X-Forwarded-Proto`. En Container Apps, `TrustAll=true`, solo porque Kestrel no se alcanza por fuera del ingress | `ArquitecturaBase.Api/Hosting/ForwardedHeadersExtensions.cs` | arranca, pero la IP y el esquema son los del proxy: el rate limit y la redirección HTTPS se equivocan |
| `WhatsApp:PhoneNumberId`, `WhatsApp:AccessToken` y, para el webhook, `WhatsApp:AppSecret` con `WhatsApp:VerifyToken` | solo si se prende WhatsApp: `PhoneNumberId` es el interruptor, y el webhook necesita los dos secretos juntos | `ArquitecturaBase.Infrastructure/Modules/WhatsApp/WhatsAppInfrastructureRegistration.cs`, `WhatsAppOptionsValidator.cs` | sin `PhoneNumberId`, WhatsApp queda apagado y la Api arranca; con él y sin token, o con un solo secreto del webhook, no arranca |

Además, hay que reemplazar `AllowedHosts: "*"` por los hosts públicos (la Api arranca igual con `*`): el porqué está en [Proxy y encabezados reenviados](#proxy-y-encabezados-reenviados), junto con lo de `X-Forwarded-Host`. El resto de las claves (`Authentication:LoginCode:*`, `RateLimiting:*`, `WhatsApp:*` sin los de arriba, `Email:QueueCapacity`) trae valores por defecto válidos en `appsettings.json` o en sus clases de opciones.

## Redis

El despliegue actual fuera de Aspire debe provisionar Redis por separado. El AppHost levanta `redis:8.6` solo para desarrollo; ni el pipeline de imagen ni las instrucciones previas de Azure crean un servidor Redis. Antes de desplegar esta versión, configurá `ConnectionStrings__cache` como secreto del orquestador y acceso privado con TLS, y `Caching__KeyPrefix` con un valor compartido entre las réplicas y distinto por instalación/ambiente. Sin prefijo explícito se usa aplicación, ambiente y nombre de la base, que no distingue instalaciones con nombres de base iguales.

Redis guarda valores reconstruibles: no requiere persistencia para recuperar el producto. Dimensioná memoria y una política de expulsión para TTLs, y monitoreá las métricas del caché; no guarda tokens, códigos ni claves de Data Protection. Sus datos usan formato de clave `v1`; una revisión del formato en un despliegue debe permitir que las versiones de Api que conviven lean sus propias claves.

`/health` incluye Redis y `/alive` lo excluye. La caída de Redis falla las lecturas que lo usan, con comandos de hasta 3 segundos y sin fallback. Una invalidación fallida después del commit devuelve 500 genérico con `traceId`, aunque PostgreSQL ya guardó. No asumir rollback ni repetir toda la escritura. Los ajustes conservados pueden atrasarse hasta su TTL de 60 segundos; los permisos usan la revisión confirmada del rol y no seleccionan la entrada de una revisión anterior. La receta, las opciones y la coordinación entre réplicas están en [agregar-cache.md](agregar-cache.md).

## Proxy y encabezados reenviados

La Api procesa `X-Forwarded-For` y `X-Forwarded-Proto` antes del rate limiter, la autenticación y la redirección HTTPS. Por defecto solo confía en los proxies loopback de ASP.NET Core. Un despliegue puede declarar `ForwardedHeaders:KnownProxies` (direcciones IP) o `ForwardedHeaders:KnownNetworks` (CIDR). Azure Container Apps, cuyas IP internas pueden cambiar, usa `ForwardedHeaders:TrustAll=true`; esto solo es seguro cuando Kestrel no es accesible por fuera del ingress confiable.

`X-Forwarded-Host` no se acepta a propósito: OpenIddict usa `Request.Host` para construir URLs públicas, y confiar ese encabezado sin una lista explícita permitiría que un cliente las manipule. El reverse proxy tiene que conservar el host público en el encabezado HTTP `Host` (en nginx, por ejemplo, `proxy_set_header Host $host`), y producción debe reemplazar `AllowedHosts: "*"` por los hosts públicos permitidos, separados por `;` si hay más de uno.

## Certificados de OpenIddict

Fuera de Development y Testing, OpenIddict firma y cifra los tokens con dos PFX propios: no son certificados TLS, pueden ser autofirmados y de larga duración, y se pasan en base64 como secretos, sin archivos en el contenedor. Con varias réplicas tienen que ser los mismos en todas. Cómo generarlos y cargarlos: [azure-setup.md, "Certificados de OpenIddict"](../deploy/azure-setup.md#certificados-de-openiddict). Si se pierden y se regeneran, todos los tokens emitidos dejan de valer y las sesiones abiertas se caen.

## Data Protection

Las claves se guardan cifradas en `DataProtectionKeys`. Todas las réplicas usan `ApplicationName=ArquitecturaBase` y el mismo PFX externo con clave privada: `Authentication:Certificates:DataProtection:Base64` (prioritario) o `Path`, y `Password`. Se provisionan como secretos del ambiente; no van en Git, imagen, salida de comandos ni copia de la base. Fuera de Development Windows y Testing, su ausencia impide el arranque. EF puede construir el modelo y el bundle sin ese secreto; el host en ejecución lo exige.

En Development Windows sin PFX se usa DPAPI del usuario: una copia de la base no se restaura bajo otro usuario o equipo sin ese material. Para un ambiente portable, configurar el PFX desde el principio. Testing usa material efímero; el arnés comparte el proveedor entre sus clones y las pruebas de restauración usan certificados explícitos.

El seed cifra las claves antiguas marcadas `requiresEncryption` en su única transacción, conservando ID, fechas y friendly name. Las cookies previas siguen funcionando. **Las copias antiguas con claves en texto plano siguen expuestas:** sustituirlas según la política de respaldo; si se sospecha compromiso, revocar las sesiones y claves afectadas. Cifrar una copia nueva no recupera el secreto filtrado.

Para restaurar, recuperar juntos base y PFX/contraseña desde respaldos separados con acceso controlado; comprobar cookie previa y descifrado de pendientes antes de habilitar tráfico. Para rotar, configurar el PFX nuevo y conservar los anteriores en `Authentication:Certificates:DataProtection:Previous:0:{Base64|Path,Password}` (y siguientes índices). Todas las réplicas necesitan ese conjunto. Las claves ya cifradas no se reenvuelven automáticamente: no retirar el PFX anterior mientras haya claves o respaldos que lo requieran.

## Colas transaccionales

El correo y los códigos, invitaciones y orientación de acceso se persisten cifrados junto al caso de uso. Un reinicio conserva pendientes y un rollback no permite enviarlos. El bundle agrega `QueuedDeliveries` antes de la imagen nueva; requiere el certificado de Data Protection disponible en todas las réplicas. La capacidad es compartida por canal (100 por defecto), y una fila reservada no se repite después de una caída: puede quedar un resultado incierto. El detalle y las políticas de cola llena están en [backend](../architecture/backend.md#colas-transaccionales) y en cada área.

## Lista antes del primer despliegue

- [ ] La configuración de [azure-dev.md](../deploy/azure-dev.md) completada, con infraestructura, base `appdb`, GitHub environment `dev`, identidades y secrets; ambos workflows en GitHub.
- [ ] Redis provisionado, accesible por red privada/TLS, `ConnectionStrings__cache` como secreto y `Caching__KeyPrefix` por instalación y ambiente ([Redis](#redis)).
- [ ] El `dist/` del front llega a `wwwroot/` ([arriba](#antes-que-nada-el-front-no-llega-a-la-imagen)).
- [ ] Cada clave de la [tabla](#configuración-obligatoria-en-production), con `Authentication:Issuer` y las URIs del cliente `web` apuntando al origen público, no a `localhost`.
- [ ] La URL de producción entre los redirect URIs autorizados en la consola de Google.
- [ ] `AllowedHosts` con los hosts públicos, y el proxy conservando el `Host` público ([Proxy y encabezados reenviados](#proxy-y-encabezados-reenviados)).
- [ ] Los PFX de firma, cifrado de tokens y Data Protection respaldados aparte de PostgreSQL, junto a sus contraseñas.
- [ ] Después del primer arranque: el dueño entra con el correo de `Seed:AdminEmail` y queda como Admin.
