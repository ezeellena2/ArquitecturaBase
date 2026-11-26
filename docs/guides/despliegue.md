# Despliegue

Para quien despliega la Api fuera de Development: en qué orden sale cada cosa, qué hace la Api al arrancar, qué configuración tiene que estar sí o sí y qué queda pendiente. La infraestructura de Azure, paso a paso y una sola vez, está en [`docs/deploy/azure-setup.md`](../deploy/azure-setup.md); la decisión de fondo, en el [ADR 0006](../decisions/0006-migraciones-y-seed-fuera-de-development.md). La puesta en marcha de dev y GitHub está en [azure-dev.md](../deploy/azure-dev.md). Reconciliada con el código y el workflow local el 2026-10-02; los valores por defecto, los límites, el cupo del correo, la retención y el apagado se volvieron a contrastar con el código el **2026-11-18**. Describe su configuración, no acredita un despliegue ejecutado en Azure.

Índice:

1. [Front dentro de la imagen](#front-dentro-de-la-imagen)
2. [El orden: bundle, después imagen](#el-orden-bundle-después-imagen)
3. [Qué hace la Api al arrancar, por ambiente](#qué-hace-la-api-al-arrancar-por-ambiente)
4. [Qué hace el seed en cada arranque](#qué-hace-el-seed-en-cada-arranque)
5. [Configuración obligatoria en Production](#configuración-obligatoria-en-production)
6. [Redis](#redis)
7. [Proxy y encabezados reenviados](#proxy-y-encabezados-reenviados)
8. [Límites de pedidos](#límites-de-pedidos)
9. [Cupo del correo](#cupo-del-correo)
10. [Retención de los datos del ingreso](#retención-de-los-datos-del-ingreso)
11. [Certificados de OpenIddict](#certificados-de-openiddict)
12. [Data Protection](#data-protection)
13. [Colas transaccionales](#colas-transaccionales)
14. [Logs, trazas y alertas](#logs-trazas-y-alertas)
15. [Lista antes del primer despliegue](#lista-antes-del-primer-despliegue)

El [registro de preparación](../plans/verificacion-y-produccion.md) conserva los recorridos
manuales, las dependencias externas y los pendientes de privacidad rescatados de planes cerrados.
No se consideran completados por pasar las suites automáticas.

<a id="antes-que-nada-el-front-no-llega-a-la-imagen"></a>

## Front dentro de la imagen

El workflow descarga ambos repositorios, verifica y compila React y copia **todo** `dist/` a `src/ArquitecturaBase.Api/wwwroot/` antes de publicar .NET. Incluye `silent-renew.html` y compara por SHA-256 todos los archivos del front con los del publicado. `wwwroot/` es generado e ignorado por Git: la fuente se conserva en el repo del front. La Api sirve el SPA desde ese directorio con un solo origen. Sin `wwwroot/index.html`, `UseSpaFallback` no se instala y `/` devuelve 404; empaquetar el front es parte obligatoria de esta pipeline. La Api comprime con Brotli o gzip y fija el caché: `/assets` (nombres con hash) un año e `immutable`; `index.html` y el resto de los archivos, `no-cache`. Un proxy o CDN delante no necesita repetirlo, pero tiene que respetar `Cache-Control` y `Vary: Accept-Encoding`.

## El orden: bundle, después imagen

El pipeline es [`.github/workflows/deploy.yml`](../../.github/workflows/deploy.yml), con estos jobs:

1. **`browser`, `front` y `backend`:** prueban en paralelo, partidos con `--shard` o por módulo para entrar en el tope de cada job: Playwright en 4 partes sobre Windows, vitest en 6 y .NET en 3 (la integración de WhatsApp, la de Formularios y el resto, con Docker). Cada parte del front guarda el commit que probó y, si falla, sus diagnósticos con el número de la parte.
2. **`build`:** exige que todas las partes del front hayan probado el commit que empaqueta, revisa el lint y compila React; produce una imagen Linux en archivo, el bundle EF y un manifest con los dos commits. No se autentica en Azure. La etiqueta es única por ejecución/intento, sin `latest`.
3. **`deploy`:** solo en `main` y con la variable de repositorio `DEV_DEPLOY_ENABLED=true`, usa el environment `dev`. Omite el paquete si cualquiera de sus commits fue superado por `main`. Se autentica con OIDC, corta si la Container App no tiene sus [probes](../deploy/azure-setup.md#probes), sube la imagen, abre acceso temporal para el bundle, aplica migraciones y cierra el acceso incluso ante fallas. **Recién después** actualiza la Container App en modo Single. Espera la revisión concreta lista y comprueba `/health`, `/`, `/silent-renew.html` y el discovery de OpenIddict.

Los despliegues se serializan; GitHub conserva solo el último pendiente. Un push del front solicita esta pipeline del backend con un token limitado. Variables, secrets e identidades: [azure-dev.md](../deploy/azure-dev.md). No se resetea la base ni se crean recursos desde el workflow. Una falla posterior a migrar no revierte el esquema; una falla de la comprobación pública tampoco hace rollback automático.

Si una migración falla, el job corta antes de desplegar y producción sigue con la imagen vieja: nunca queda código nuevo contra un esquema sin migrar. Al revés sí pasa un rato: entre el bundle y la revisión nueva, la imagen vieja corre contra el esquema nuevo. Por eso una migración tiene que ser compatible con la versión anterior (primero se agrega, en otro despliegue se borra); lo mismo vale si volvés a una imagen vieja. Con los datos pasa igual: un campo o un valor nuevo (un tipo de mensaje o de salida, un formato sellado, un perfil, un proveedor de IA) se escribe recién un despliegue después del que ya sabe leerlo, y mientras tanto, donde ya se cuida (la cola durable, el tipo de un mensaje o de una salida de WhatsApp, la proyección de un Flow, el sello de un turno de IA), la versión que no lo conoce lo deja para otra copia, con un tope (en la IA, el vencimiento del turno, y su consulta responde el estado en claro una vez terminado; un proveedor de IA que no conoce deja la conexión pausada, sin 500); lo demás todavía se rompe ([WhatsApp](../features/whatsapp.md), [IA](../features/inteligencia-artificial.md), H25, N27 e I4). Al revés también: un valor deja de leerse recién cuando venció la retención de lo que lo escribió.

Dos migraciones de la plantilla no cumplen esa regla, a propósito, porque no había ningún proyecto con WhatsApp en producción: `UserInvitationProviderMessageId` renombra la columna `WaMessageId` y `LoginCodePhoneChannel` cambia el canal guardado de los códigos por teléfono de `'WhatsApp'` a `'Phone'`. Entre el bundle y la imagen nueva, la imagen anterior no lee esa columna ni ese valor, y sigue escribiendo los viejos: no puede guardar el id de Meta de una invitación que mande en ese rato (la columna ya no existe), y un código que emita queda con `'WhatsApp'`. La imagen nueva no lee ese código (EF lanza al convertir un nombre que el enum no tiene), y como nada lo invalida sin leerlo, ese número responde 500 al pedir o verificar un código hasta que se corrija la fila. Por eso, **después** de que la imagen nueva queda arriba, se vuelve a correr el `UPDATE` de la migración, que no cambia nada si no quedó ninguno:

```sql
UPDATE "LoginCodes" SET "Channel" = 'Phone' WHERE "Channel" = 'WhatsApp';
```

Tampoco cumplen la regla las migraciones que cifran el chat de WhatsApp (H16, [ADR 0030](../decisions/0030-contenido-del-chat-cifrado.md)): cambian el tipo de esas columnas y vacían lo que había, así que entre el bundle y la imagen nueva la imagen anterior falla al leer o guardar ese contenido. Se decidió así porque todavía no hay chats reales; una instalación con chats tiene que convertirlos antes.

Un proyecto derivado que ya esté en producción con WhatsApp las tiene que partir en dos despliegues, en lugar de estas dos migraciones:

1. **Primero:** una migración que agrega `ProviderMessageId` y copia `WaMessageId`, y una versión que lee y escribe solo `ProviderMessageId` (su modelo ya no mapea `WaMessageId`) y escribe `'Phone'`, pero también lee `'WhatsApp'` (un conversor que traduzca el nombre viejo).
2. **Después:** una migración que vuelve a copiar lo que la imagen anterior escribió en `WaMessageId` después de la primera copia, pasa a `'Phone'` los códigos que hayan quedado con `'WhatsApp'` y recién entonces borra la columna, y una versión que ya no lee `'WhatsApp'`:

```sql
UPDATE "UserInvitations" SET "ProviderMessageId" = "WaMessageId" WHERE "ProviderMessageId" IS NULL AND "WaMessageId" IS NOT NULL;
UPDATE "LoginCodes" SET "Channel" = 'Phone' WHERE "Channel" = 'WhatsApp';
```

Ni el bundle ni `dotnet ef` corren el código de arranque de la sección siguiente: cortan el programa en `Build()`, antes de él.

Tanto la generación como la ejecución del bundle declaran `ConnectionStrings:appdb` y `ConnectionStrings:cache` para construir el host y el contexto. El pipeline usa valores de mentira para esa construcción; al aplicar, `--connection` reemplaza la conexión de PostgreSQL con el secreto real. El cliente Redis no se resuelve y el seed no corre, así que aplicar migraciones no necesita Redis. Declarar esas conexiones no sustituye la conexión real de Redis que necesita la Api desplegada.

La base requiere **PostgreSQL 16 o superior**: las migraciones de cuotas y reserva activa usan `IS JSON OBJECT`, incorporado en [PostgreSQL 16](https://www.postgresql.org/docs/16/release-16.html). El arnés de integración usa PostgreSQL 18.3; eso no acredita una corrida sobre la versión mínima. Comprobá la versión del servidor antes de aplicar el bundle.

## Qué hace la Api al arrancar, por ambiente

`Program.cs` llama a `InitializeDatabaseAsync` (`src/ArquitecturaBase.Infrastructure/Persistence/DatabaseInitialization.cs`) antes de atender pedidos. El ambiente sale de `ASPNETCORE_ENVIRONMENT`; Staging o cualquier otro nombre se comporta como Production.

| Ambiente | Qué hace, en orden |
|---|---|
| Development | valida las opciones, aplica las migraciones y siembra |
| Testing (solo el arnés de tests) | nada: el arnés crea el esquema y siembra después |
| Production y cualquier otro | valida las opciones; si la base no responde o falta aplicar alguna migración, **no arranca**; siembra |

- **Primero valida las opciones**, lo mismo que `ValidateOnStart` pero antes de tocar la base (`cd974cc`): una configuración mal escrita se ve como tal, y no detrás de un error de la base ni después de un seed que ya corrió. Los certificados de OpenIddict se leen antes todavía, al registrar los servicios.
- **No migra.** Si la base tiene migraciones sin aplicar, lanza `InvalidOperationException` con la lista y un mensaje que dice que se apliquen con el bundle. El chequeo es explícito porque una base vieja no haría fallar al seed, que solo toca roles, ajustes y OpenIddict.
- **Base caída o sin migrar: la Api no arranca.** Antes del chequeo de migraciones prueba la conexión (abre una y corre `SELECT 1`): si el servidor no responde, lanza `InvalidOperationException` con un mensaje que dice eso, que la base no responde, y no que falten migraciones; la causa (conexión rechazada, timeout, contraseña mal escrita) va como `InnerException`. Sin esa prueba, un reset de conexión puede hacer que EF crea que la base no existe y liste todas las migraciones como pendientes, y el error mandaría a correr el bundle. No usa `Database.CanConnectAsync`, que también da `false` cuando la base no existe: una base que nadie creó (un despliegue sin el bundle) sí responde, y sigue cayendo en el chequeo de migraciones, que manda al bundle. El proceso termina con error y el orquestador lo reinicia hasta que la base responde (Container Apps reinicia el contenedor que se cae). El workflow no configura los probes, pero no despliega si faltan ([Probes](../deploy/azure-setup.md#probes)): con `/health` como readiness, la revisión nueva no recibe tráfico hasta que PostgreSQL responde (Redis caído la deja en Degraded, que recibe tráfico), y el Startup en `/alive` cubre la espera del arranque, porque la Api no escucha hasta terminar este chequeo y el seed.
- **El seed espera su lock como mucho el timeout de comando de Npgsql, 30 s por defecto.** Dos réplicas que arrancan juntas siembran en fila (ver la sección siguiente); si una esperara más que eso, el comando vence, la réplica no arranca y el orquestador la reinicia. El seed dura mucho menos, así que no debería pasar; si pasa, se sube con `Command Timeout=<segundos>` en la cadena de conexión.
- Después, fuera de Development: HSTS, sin OpenAPI.

## Qué hace el seed en cada arranque

Corre en cada arranque de cada réplica, fuera de Testing. Es idempotente: crea lo que falta y no duplica nada. Lo arma `DatabaseSeeder` (`src/ArquitecturaBase.Infrastructure/Persistence/Seed/`): todo en **una transacción** (si algo lanza, no queda nada a medias) y **en fila entre réplicas**, con el advisory lock `seed:database` que toma adentro. En orden:

1. **Roles** (`RoleSeeder`). Crea `Admin` y `User` si no existen, y a `Admin` le suma los permisos de `IPermissionCatalog.All` que le falten: incluye el núcleo y las fuentes de módulos presentes. Un permiso nuevo del código le llega a Admin en el próximo despliegue. No elimina claims históricos; los de una fuente retirada dejan de ser permisos efectivos. A `User` no le da ninguno. A cada rol, también a `User` y a los creados desde el panel, le suma lo que incluyen sus permisos y le falta: uno guardado antes con «Administrar usuarios» y sin «Ver usuarios» queda con los dos, con su revisión avanzada y su historia sin actor ([un permiso incluye a otro](../features/administracion.md)).
2. **La cuenta de `Seed:AdminEmail`** (en el mismo `RoleSeeder`). Es el administrador inicial de esta instalación, que atiende un solo negocio. **El seed nunca crea cuentas**: si esa cuenta existe, se asegura de que tenga el rol Admin, y se lo devuelve en cada arranque si se lo sacaron por fuera de la aplicación. Desde la pantalla nadie le quita Admin ni le cambia el correo, ni la administración ni su titular desde «Mi perfil»: responde `409 Users.User.InitialAdmin` y pide cambiar antes esta clave ([administración](../features/administracion.md)). Para pasarle la administración a otra persona, se cambia `Seed:AdminEmail` y se despliega; recién entonces se le puede sacar Admin a la cuenta anterior. Si no existe, no hace nada. La cuenta la crea su titular en su primer ingreso (con código o con Google), que se le permite aunque el registro sea solo por invitación (`AccountCreationPolicy`), y nace con el rol Admin. Las demás cuentas y roles se administran desde el panel. Otra empresa usa otra instalación con datos, recursos, secretos y administrador inicial propios; no hay administración central de empresas.
3. **La fila de ajustes** (`SystemSettingsSeeder`). Si no existe, la crea con `Registration:Mode`: desde el 2026-11-22 (RC6-B) el `appsettings.json` de la plantilla configura `InviteOnly`, así una instalación nueva arranca sólo por invitación, y `RegistrationOptions` respalda con el mismo valor si falta la clave. Un valor fuera del enum, como `Registration__Mode=5`, frena el arranque antes de tocar la base. `SystemSettings.Create` agrega cultura `es`, zona `America/Argentina/Buenos_Aires`, tamaño de página 20 y revisión 1. Si la fila ya existe, manda la base: el seed no pisa ninguno de sus ajustes.
4. **OpenIddict** (`OpenIddictSeeder`). El scope `api` y el cliente público `web` (PKCE), creados o **realineados con la configuración**: los permisos, los requisitos y las URIs del cliente `web` quedan exactamente como dicen el código y `Authentication:Clients:Web`. **Lo que se haya cargado a mano en la base sobre ese cliente o ese scope se borra en el próximo arranque**; para cambiar una URI, se cambia la configuración. Otros clientes no se tocan.

Después del commit, el seed descarta en Redis los ajustes. Redis puede seguir caliente después de reiniciar una Api. Los permisos no se cachean: lo que el seed le suma a Admin vale desde el pedido siguiente. Si Redis no responde en ese descarte, el seed ya se confirmó y el arranque sigue, con un Warning: lo que Redis conserve vence con su TTL de 60 segundos (N9).

## Configuración obligatoria en Production

Con el módulo IA presente, el startup exige `ArtificialIntelligence:SecretProtection`:
InstallationId, ActiveKeyId y claves externas de 32 bytes base64. Todas las réplicas comparten
el anillo y el ID; negocios independientes no los comparten. No se requiere clave de proveedor
para arrancar y no se prueba el proveedor en readiness. Provisioná antes de publicar, fuera
de la base/imagen, y conservá claves históricas con backups. La [guía de IA](configurar-ia.md)
fija variables, rotación y restauración. La migración AiConnection es aditiva; el workflow
conserva secretos del host y no provisiona este anillo. Azure real sigue sin verificar.

Con el módulo ARCA presente, el startup exige además `Arca:SecretProtection`, con el mismo diseño: InstallationId,
ActiveKeyId y claves externas de 32 bytes base64 (`Arca__SecretProtection__InstallationId`,
`Arca__SecretProtection__ActiveKeyId` y `Arca__SecretProtection__Keys__<keyId>`). Lo exige aunque todavía no haya una
conexión. [Configurar ARCA](configurar-arca.md) fija las variables y la rotación; si cambian el ID o la clave, la clave
privada guardada queda ilegible y hay que repetir el alta en ARCA. Con la facturación de ARCA, tampoco arranca sin el almacén
privado (`FileStorage`), donde va el PDF fiscal de cada comprobante.

Fuera de Aspire la Api no recibe nada sola. Con variables de entorno, el `:` se escribe `__` (`ConnectionStrings__appdb`). Lo que es secreto va como secreto del orquestador (en Container Apps, con `secretref:`), nunca en el repo. Cómo cargarlas en Azure: [azure-setup.md, paso 6](../deploy/azure-setup.md#6-configuración-de-la-aplicación). Rutas desde `src/`:

| Clave | Qué pide | Dónde lo exige el código | Si falta |
|---|---|---|---|
| `ConnectionStrings:appdb` | la cadena de Postgres, con `Maximum Pool Size` por debajo de lo que admite el servidor ([Azure](../deploy/azure-setup.md#6-configuración-de-la-aplicación)) | `ArquitecturaBase.Infrastructure/Persistence/PersistenceRegistration.cs` (el nombre, en `DependencyInjection.cs`) | no arranca: lanza al crear el contexto, en el chequeo de migraciones |
| `ConnectionStrings:cache` | conexión de Redis, como secreto; TLS y acceso privado en producción | `ArquitecturaBase.Infrastructure/Caching/CachingRegistration.cs` | no arranca sin conexión configurada; con Redis inaccesible arranca y queda en Degraded (N9) |
| `Caching:KeyPrefix` | el prefijo de las claves de Redis: uno por instalación y ambiente, igual en todas las réplicas (por ejemplo `acme:production`) | `ArquitecturaBase.Infrastructure/Caching/CachingRegistration.cs` | no arranca (validación de opciones). Sólo Development y Testing lo derivan de aplicación, ambiente y base |
| `Authentication:LoginCode:HashKey` | al menos 32 bytes aleatorios en base64 (`openssl rand -base64 48`) | `ArquitecturaBase.Infrastructure/Security/LoginCodeHashOptions.cs`, registrado en `DependencyInjection.cs` | no arranca (validación de opciones) |
| `Authentication:Certificates:Signing:Base64` o `:Path`, y `:Password` | el PFX de firma de OpenIddict; `Base64` gana sobre `Path` | `ArquitecturaBase.Infrastructure/Identity/OpenIddict/CertificateLoader.cs`, llamado en `OpenIddictRegistration.cs` | no arranca: se lee al registrar los servicios |
| `Authentication:Certificates:Encryption:Base64` o `:Path`, y `:Password` | el PFX de cifrado, igual que el anterior | los mismos | igual |
| `Authentication:Certificates:DataProtection:Base64` o `:Path`, y `:Password` | PFX con clave privada compartido entre réplicas | `Identity/DataProtectionRegistration.cs` | no arranca sin cifrado de claves |
| `Authentication:Issuer` | el origen público de la web, el que ve el navegador (no el de la Api detrás del proxy). De él salen el botón de las invitaciones por correo y los botones públicos de orientación de acceso, y OpenIddict lo usa como issuer | `ArquitecturaBase.Infrastructure/Emails/EmailRegistration.cs` (fuera de Development y Testing, siempre); `ArquitecturaBase.Infrastructure/Modules/WhatsApp/WhatsAppInfrastructureRegistration.cs` (con el webhook, en cualquier ambiente); `ArquitecturaBase.Infrastructure/Identity/OpenIddict/OpenIddictRegistration.cs` | no arranca (validación de opciones) |
| `Authentication:Clients:Web:RedirectUris` | al menos una URI, del origen público (`…/auth/callback` y `…/silent-renew.html`) | `ArquitecturaBase.Infrastructure/Identity/OpenIddict/WebClientOptions.cs`, registrado en `OpenIddictRegistration.cs` | no arranca. Alimenta el cliente `web` del seed |
| `Authentication:Clients:Web:PostLogoutRedirectUris` | al menos una URI (`…/login`) | `WebClientOptions.cs` | igual |
| `Authentication:Google:ClientId` y `Authentication:Google:ClientSecret` | solo si se prende Google: el `ClientId` del cliente OAuth del ambiente es el interruptor, y el `ClientSecret` va como secreto. La plantilla trae el `ClientId` vacío, con Google apagado | `ArquitecturaBase.Infrastructure/Identity/IdentityRegistration.cs` | sin `ClientId`, Google queda apagado y la Api arranca; con él y sin `ClientSecret`, no arranca |
| `Email:Smtp:UserName`, `Email:Smtp:Password` y `Email:Smtp:FromAddress` | la cuenta que envía; la contraseña es una contraseña de aplicación, como secreto. `Email:Delivery` es `Smtp` por defecto y `appsettings.json` trae los otros datos de Gmail; `PickupDirectory` (archivos `.eml`) sólo vale en Development y Testing | `ArquitecturaBase.Infrastructure/Emails/SmtpOptions.cs`, `SmtpOptionsValidator.cs`, el `Smtp` por defecto en `EmailOptions.cs` y `EmailRegistration.cs` | no arranca (validación de opciones), tampoco con `PickupDirectory` |
| `Seed:AdminEmail` | correo del administrador inicial. Opcional para arrancar; permite crear esa cuenta en cualquier modo y el seed le asegura Admin. Con `InviteOnly` y sin cuentas previas es la única excepción al registro cerrado | `ArquitecturaBase.Infrastructure/Identity/SeedOptions.cs` y `ArquitecturaBase.Application/Services/Auth/AccountCreationPolicy.cs` | arranca; con `Open` permite registro de cuentas User, pero no crea un administrador inicial. Con `InviteOnly` y sin cuentas previas no se puede crear ninguna cuenta. Un correo inválido impide arrancar |
| `ForwardedHeaders:TrustAll`, o `:KnownProxies` / `:KnownNetworks` | detrás de un proxy: a quién se le creen `X-Forwarded-For` y `X-Forwarded-Proto`. En Container Apps, `TrustAll=true`, solo porque Kestrel no se alcanza por fuera del ingress | `ArquitecturaBase.Api/Hosting/ForwardedHeadersExtensions.cs` | no arranca (validación de opciones), fuera de Development y Testing; tampoco con una entrada que no sea una IP o una red CIDR. Sin ellos, la IP y el esquema serían los del proxy: OpenIddict rechazaría el ingreso por HTTP y el límite por IP juntaría a todos |
| `WhatsApp:PhoneNumberId`, `WhatsApp:AccessToken` y, para el webhook, `WhatsApp:AppSecret` con `WhatsApp:VerifyToken` | solo si se prende WhatsApp: `PhoneNumberId` es el interruptor, y el webhook necesita los dos secretos juntos | `ArquitecturaBase.Infrastructure/Modules/WhatsApp/WhatsAppInfrastructureRegistration.cs`, `WhatsAppOptionsValidator.cs` | sin `PhoneNumberId`, WhatsApp queda apagado y la Api arranca; con él y sin token, o con un solo secreto del webhook, no arranca |

`AllowedHosts` queda en `*`: el que filtra por host es el ingress o el proxy, y el porqué está en [Proxy y encabezados reenviados](#proxy-y-encabezados-reenviados), junto con lo de `X-Forwarded-Host`. El resto de las claves (`Authentication:LoginCode:*`, `RateLimiting:*`, `RequestLimits:*`, `WhatsApp:*` sin los de arriba, `Email:QueueCapacity` y los topes `Email:Daily*` del [cupo del correo](#cupo-del-correo)) trae valores por defecto válidos en `appsettings.json` o en sus clases de opciones.

Desde el 2026-11-22 (RC3-A) el tiempo para contestar un formulario, los países que reciben códigos y la retención de WhatsApp ya no salen de `WhatsApp:*`: la migración `AddWhatsAppSettings` crea la tabla vacía, mientras nadie guarda valen los de la plantilla (24 horas, Argentina y 90 días) y el administrador los cambia en Configuración › WhatsApp ([WhatsApp](../features/whatsapp.md#configuración--whatsapp-rc3-a-2026-11-22)). Un ambiente que los tenía en variables (`WhatsApp__FlowSessionLifetimeHours`, `WhatsApp__AllowedCountries__0`, `WhatsApp__MessageRetentionDays`) los vuelve a cargar ahí después de desplegar, y las variables se borran: la Api ya no las lee.

## Redis

El despliegue actual fuera de Aspire debe provisionar Redis por separado. El AppHost levanta `redis:8.6` solo para desarrollo; ni el pipeline de imagen ni las instrucciones previas de Azure crean un servidor Redis. Antes de desplegar esta versión, configurá `ConnectionStrings__cache` como secreto del orquestador y acceso privado con TLS, y `Caching__KeyPrefix` con un valor compartido entre las réplicas y distinto por instalación/ambiente. Sin prefijo la Api no arranca: sólo Development y Testing lo derivan de aplicación, ambiente y nombre de la base, que no distingue instalaciones que comparten Redis con bases del mismo nombre.

Redis guarda valores reconstruibles: no requiere persistencia para recuperar el producto. Dimensioná memoria y una política de expulsión para TTLs, y monitoreá las métricas del caché; no guarda tokens, códigos ni claves de Data Protection. Sus datos usan formato de clave `v1`; una revisión del formato en un despliegue debe permitir que las versiones de Api que conviven lean sus propias claves.

`/health` incluye Redis y `/alive` lo excluye; `/health` guarda su resultado 2 s, así que una ráfaga no le pide un comando por pedido. Sin Redis la app sigue (N9, [ADR 0032](../decisions/0032-sin-redis-la-app-sigue.md)): su chequeo queda en Degraded, que responde 200 y deja la copia en el tráfico; el arranque, el ingreso, los permisos (que no se cachean) y la configuración (que se lee de la base) andan. Falla lo que necesita Redis para su efecto: el envío de WhatsApp, que coordina las copias con una lease por destinatario, y la consulta cacheada de ARCA. Una invalidación fallida después del commit devuelve 500 genérico con `traceId`, aunque PostgreSQL ya guardó. No asumir rollback ni repetir toda la escritura. Los ajustes conservados pueden atrasarse hasta su TTL de 60 segundos. La receta, las opciones y la coordinación entre réplicas están en [agregar-cache.md](agregar-cache.md).

## Proxy y encabezados reenviados

La Api procesa `X-Forwarded-For` y `X-Forwarded-Proto` antes del rate limiter, la autenticación y la redirección HTTPS. Por defecto solo confía en los proxies loopback de ASP.NET Core. Un despliegue puede declarar `ForwardedHeaders:KnownProxies` (direcciones IP) o `ForwardedHeaders:KnownNetworks` (CIDR). Azure Container Apps, cuyas IP internas pueden cambiar, usa `ForwardedHeaders:TrustAll=true`; esto solo es seguro cuando Kestrel no es accesible por fuera del ingress confiable. Fuera de Development y Testing la Api no arranca sin una de las tres, ni con una entrada que no se interpreta: lo valida al arrancar, antes de comprobar las migraciones y sembrar.

`X-Forwarded-Host` no se acepta a propósito: OpenIddict usa `Request.Host` para construir URLs públicas, y confiar ese encabezado sin una lista explícita permitiría que un cliente las manipule. El reverse proxy tiene que conservar el host público en el encabezado HTTP `Host` (en nginx, por ejemplo, `proxy_set_header Host $host`). `AllowedHosts` queda en `*`, porque el filtro por host lo hace el ingress: el de Container Apps sólo le pasa a la app los pedidos dirigidos a su FQDN y a sus dominios propios, y con nginx lo hace un `server_name` con los hosts públicos y un servidor por defecto que corta el resto. Restringirlo en la Api haría fallar los chequeos de Container Apps, que mandan como `Host` la IP de la réplica: recibirían 400 y la revisión nueva nunca quedaría lista.

La Api se toma hasta 25 segundos para apagarse (`HostOptions.ShutdownTimeout`, en `Program.cs`): terminar los pedidos en curso y frenar los workers, todos a la vez (`ServicesStopConcurrently`). El correo que ya empezó a salir termina en ese plazo: 12 s de envío y 10 para anotarlo y completarlo (N8). El orquestador tiene que darle más antes de matarla; en Container Apps, `terminationGracePeriodSeconds: 60` ([Azure](../deploy/azure-setup.md#probes)), porque con los 30 por defecto quedaría casi sin margen. El asistente de IA usa ese plazo, menos el margen para cerrar el turno, para dejar terminar la consulta que ya llegó al proveedor; la que todavía no llegó vuelve a pendiente para otra copia ([IA](../features/inteligencia-artificial.md), I12).

## Límites de pedidos

Además de las políticas de cada ruta (los códigos de ingreso, los módulos), cada réplica pone tres límites a todos los
pedidos (`ArquitecturaBase.Api/RateLimiting/GlobalRequestLimits.cs`), con sus valores en `RequestLimits`:

| Clave | Por defecto | Qué limita |
|---|---|---|
| `AnonymousPermitLimit` y `AnonymousWindowSeconds` | 300 cada 60 s | los pedidos por IP (una IPv6, por su /64) a las rutas que no piden sesión y no tienen política propia: `/connect`, Google, los métodos de ingreso, la presentación y `/health`. No cuentan los archivos del SPA |
| `ConcurrentRequests` y `QueuedRequests` | 100 y 100 | los pedidos a la vez en la réplica; los que no entran esperan en la cola, y sin lugar en ella, 429. `/health` y `/alive` no cuentan, para que el orquestador no saque ni reinicie una réplica cargada |
| `MaxRequestBodyBytes` | 1 MiB | el cuerpo que acepta Kestrel; más grande, 413. Una ruta que sube archivos declara el suyo con `[RequestSizeLimit]` y `[RequestFormLimits]`, que manda sobre este |

Corren antes de la autenticación, que ya consulta la base (OpenIddict valida el cliente de `/connect` y la entrada de
cada token), y responden el 429 de siempre, con `Retry-After`. Cada réplica cuenta los suyos. La IP es la que dejan los
[encabezados reenviados](#proxy-y-encabezados-reenviados): sin ellos, todos compartirían la del proxy. Una oficina
detrás de una sola IP comparte la ventana de lo anónimo; si no alcanza, se sube `AnonymousPermitLimit`.

## Cupo del correo

Producción manda con Gmail (decisión del 2026-11-14, N7). Gmail le da a una cuenta gratuita unos 500 correos por día (a
una de Google Workspace, unos 2.000); pasado el cupo rechaza el remitente hasta por 24 horas, y no sale ningún correo: ni
códigos, ni invitaciones, ni los avisos de los módulos. Antes de desplegar, anotá acá el cupo de la cuenta que se use y
fijá `Email:DailyLimit` por debajo, con lugar para los demás correos del sistema (los avisos de Formularios y de ARCA no
cuentan en los topes). Los topes cuentan en 24 horas móviles, en la base, así que valen para todas las réplicas y
sobreviven a un reinicio; los mira `EmailQuotaGuard` ([identidad](../features/identidad.md),
[administración](../features/administracion.md)):

| Clave | Por defecto | Qué limita |
|---|---|---|
| `Email:DailyLimit` | 400 | los correos con un código o una invitación que salen, entre todos. Los códigos por correo frenan antes, en este tope menos `Email:DailyInvitationReserve` (350 por defecto), y las invitaciones llegan hasta acá; cada uno responde 429 hasta que se libere un lugar. Desde el 80 % del tope (320 por defecto) queda un Warning de `EmailQuotaGuard` en el log, una vez por día en cada réplica, y al llegar al tope, otro |
| `Email:DailyInvitationReserve` | 50 | los lugares de `Email:DailyLimit` que quedan sólo para las invitaciones, así un abuso anónimo de los códigos no deja a los administradores sin invitar. Va por debajo de `Email:DailyLimit`; 0 la apaga. Con la reserva por debajo del 20 % del tope, el aviso del 80 % sale antes de que frenen los códigos |
| `Email:DailyCodeLimitPerAddress` | 50 | los códigos por correo que se piden desde una misma IP (una IPv6, por su /64), salgan o no. Una oficina detrás de una sola IP lo comparte: si no alcanza, se sube |
| `Email:DailyProfileCodeLimitPerAccount` | 10 | los códigos por correo que una cuenta pide desde su perfil |
| `Email:DailyInvitationLimitPerAdministrator` | 100 | las invitaciones por correo de un administrador. Para una carga inicial grande se sube, sin pasar `Email:DailyLimit` |

Un tope menor que 1, una reserva negativa o una que no quede por debajo de `Email:DailyLimit` no arranca (validación de opciones). Si el cupo se agota igual (otro sistema que usa la misma
cuenta, un tope mal fijado), `/health` marca `email` en Degraded cuando Gmail rechaza el remitente
([colas](#colas-transaccionales)). Un abuso desde muchas direcciones puede agotar los lugares de los códigos y dejar a todos sin
códigos por correo hasta que se libere (las invitaciones siguen con su reserva): es el costo aceptado de seguir con Gmail, y
un proveedor transaccional con su cupo medido lo saca. En `InviteOnly`, el mismo abuso, con un único lugar libre, deja ver
si un correo tiene cuenta, también un costo aceptado ([identidad](../features/identidad.md)).

## Retención de los datos del ingreso

La Api borra o limpia sola los datos del ingreso, al arrancar y cada `IdentityRetention:IntervalHours`, en todas las
réplicas: la que encuentra tomado el lock de la retención termina su corrida sin esperar, porque otra ya está en eso ([identidad](../features/identidad.md#retención-de-los-datos-del-ingreso-n16-2026-11-16)).
Desde el 2026-11-22 (RC1-A) los plazos de los códigos, de los registros de ingreso y de las invitaciones los elige el
administrador en Configuración general › «Privacidad y retención» (12 meses y 30 días al empezar), y la retención los lee de
la base al empezar cada corrida; ya no son claves de `appsettings`. La política de privacidad es un sitio aparte: antes de
cambiar un plazo en la pantalla, cambiá la política que lo promete, como avisa la (i) de la zona. Lo que sigue en
`appsettings` es técnico; un valor fuera de rango no arranca (validación de opciones).

| Clave | Por defecto | Qué fija |
|---|---|---|
| `IdentityRetention:SessionDaysAfterExpiry` | 14 | cuántos días después de vencer se podan las autorizaciones y los tokens de OpenIddict |
| `IdentityRetention:BatchSize` | 500 | cuántas filas de cada tabla borra o limpia un lote, en su transacción. Como mucho 1000: los enlaces toman un lock por cuenta |
| `IdentityRetention:IntervalHours` | 24 | cada cuántas horas corre |
| `IdentityRetention:RunInBackground` | `true` | si corre sola. Sólo la apagan los tests de integración |

## Certificados de OpenIddict

Fuera de Development y Testing, OpenIddict firma y cifra los tokens con dos PFX propios: no son certificados TLS, pueden ser autofirmados y de larga duración, y se pasan en base64 como secretos, sin archivos en el contenedor. Con varias réplicas tienen que ser los mismos en todas. Cómo generarlos y cargarlos: [azure-setup.md, "Certificados de OpenIddict"](../deploy/azure-setup.md#certificados-de-openiddict). Si se pierden y se regeneran, todos los tokens emitidos dejan de valer y las sesiones abiertas se caen.

## Data Protection

Las claves se guardan cifradas en `DataProtectionKeys`. Todas las réplicas usan `ApplicationName=ArquitecturaBase` y el mismo PFX externo con clave privada: `Authentication:Certificates:DataProtection:Base64` (prioritario) o `Path`, y `Password`. Se provisionan como secretos del ambiente; no van en Git, imagen, salida de comandos ni copia de la base. Fuera de Development Windows y Testing, su ausencia impide el arranque. EF puede construir el modelo y el bundle sin ese secreto; el host en ejecución lo exige.

En Development Windows sin PFX se usa DPAPI del usuario: una copia de la base no se restaura bajo otro usuario o equipo sin ese material. Para un ambiente portable, configurar el PFX desde el principio. Testing usa material efímero; el arnés comparte el proveedor entre sus clones y las pruebas de restauración usan certificados explícitos.

El seed cifra las claves antiguas marcadas `requiresEncryption` en su única transacción, conservando ID, fechas y friendly name. Las cookies previas siguen funcionando. **Las copias antiguas con claves en texto plano siguen expuestas:** sustituirlas según la política de respaldo; si se sospecha compromiso, revocar las sesiones y claves afectadas. Cifrar una copia nueva no recupera el secreto filtrado.

Para restaurar, recuperar juntos base y PFX/contraseña desde respaldos separados con acceso controlado; comprobar cookie previa y descifrado de pendientes antes de habilitar tráfico. Para rotar, configurar el PFX nuevo y conservar los anteriores en `Authentication:Certificates:DataProtection:Previous:0:{Base64|Path,Password}` (y siguientes índices). Todas las réplicas necesitan ese conjunto. Las claves ya cifradas no se reenvuelven automáticamente: no retirar el PFX anterior mientras haya claves o respaldos que lo requieran.

## Colas transaccionales

El correo y los códigos, invitaciones y orientación de acceso se persisten cifrados junto al caso de uso. Un reinicio conserva pendientes y un rollback no permite enviarlos. El bundle agrega `QueuedDeliveries` antes de la imagen nueva; requiere el certificado de Data Protection disponible en todas las réplicas. La capacidad es compartida por canal (100 por defecto), y una fila reservada no se repite después de una caída: puede quedar un resultado incierto. El detalle y las políticas de cola llena están en [backend](../architecture/backend.md#colas-transaccionales) y en cada área.

## Logs, trazas y alertas

Un error inesperado le muestra a la persona un código («informá el código 00-…-01»): es el `traceId` del pedido, y su
segundo tramo es el `TraceId` que llevan sus logs. Para encontrarlo:

- **La consola.** Fuera de Development y Testing escribe una línea JSON por log (`AddJsonConsole`, en
  `ArquitecturaBase.ServiceDefaults/Extensions.cs`) con los scopes del pedido: `TraceId`, `SpanId` y `RequestPath`, que
  pone ASP.NET Core, y con sesión `UserId`, sólo el id de la cuenta, nunca su correo ni su número
  (`Api/RequestContext/UserLogScope.cs`). El log del 500 los lleva todos. En Container Apps la consola llega a Log
  Analytics (`ContainerAppConsoleLogs_CL | where Log_s contains "<TraceId>"`) sin configurar nada más.
- **El exportador.** Con `OTEL_EXPORTER_OTLP_ENDPOINT`, la Api manda por OTLP trazas, métricas y logs, también los
  spans de cada comando SQL; sin ella no exporta nada (`AddOpenTelemetryExporters`). En desarrollo la pone el AppHost,
  hacia el dashboard de Aspire. En Container Apps la define el agente de OpenTelemetry del ambiente cuando se le da un
  destino, por ejemplo Application Insights
  ([agentes de OpenTelemetry](https://learn.microsoft.com/azure/container-apps/opentelemetry-agents)):

  ```bash
  az containerapp env telemetry app-insights set --resource-group $RG --name $ACA_ENV --connection-string "<cadena de Application Insights>" --enable-open-telemetry-traces true --enable-open-telemetry-logs true
  ```

  Con un colector propio, se declara a mano: `OTEL_EXPORTER_OTLP_ENDPOINT` y, si pide una clave,
  `OTEL_EXPORTER_OTLP_HEADERS` como secreto.
- **La alerta de 5xx.** Una alerta de métrica sobre `Requests` de la Container App, filtrada por `statusCodeCategory`,
  avisa cuando la Api, o el ingress con un 502 o un 504, responde errores:

  ```bash
  az monitor action-group create --resource-group $RG --name api-alertas --short-name api --action email responsable <correo>
  az monitor metrics alert create --resource-group $RG --name api-5xx --scopes $(az containerapp show --resource-group $RG --name $APP --query id --output tsv) --condition "total Requests > 5 where statusCodeCategory includes 5xx" --window-size 5m --evaluation-frequency 1m --action api-alertas --description "La Api respondió errores 5xx"
  ```

  El umbral (más de 5 en 5 minutos) es un punto de partida: con poco tráfico, un solo 500 ya dice algo.

Nada de esto se probó todavía contra Azure real.

## Lista antes del primer despliegue

- [ ] La configuración de [azure-dev.md](../deploy/azure-dev.md) completada, con infraestructura, base `appdb`, GitHub environment `dev`, identidades y secrets; ambos workflows en GitHub.
- [ ] Los probes Startup y Liveness en `/alive` y Readiness en `/health` configurados en la Container App ([Probes](../deploy/azure-setup.md#probes)); sin ellos el workflow no despliega.
- [ ] Redis provisionado, accesible por red privada/TLS, `ConnectionStrings__cache` como secreto y `Caching__KeyPrefix` por instalación y ambiente ([Redis](#redis)).
- [ ] `Maximum Pool Size` en `ConnectionStrings__appdb`, con las réplicas y sus workers sumados por debajo del límite de conexiones del servidor ([Azure](../deploy/azure-setup.md#6-configuración-de-la-aplicación), N6).
- [ ] El `dist/` del front llega a `wwwroot/` ([arriba](#front-dentro-de-la-imagen)).
- [ ] Cada clave de la [tabla](#configuración-obligatoria-en-production), con `Authentication:Issuer` y las URIs del cliente `web` apuntando al origen público, no a `localhost`.
- [ ] La URL de producción entre los redirect URIs autorizados en la consola de Google.
- [ ] `ForwardedHeaders` declarado, `AllowedHosts` en `*` con el ingress o el proxy filtrando por host y conservando el `Host` público, y el plazo de apagado de la Container App en 60 s ([Proxy y encabezados reenviados](#proxy-y-encabezados-reenviados)).
- [ ] Los PFX de firma, cifrado de tokens y Data Protection respaldados aparte de PostgreSQL, junto a sus contraseñas.
- [ ] El destino de OpenTelemetry del ambiente y la alerta de 5xx ([Logs, trazas y alertas](#logs-trazas-y-alertas)).
- [ ] El cupo diario de la cuenta de correo anotado y `Email:DailyLimit` por debajo ([Cupo del correo](#cupo-del-correo)).
- [ ] Después del primer arranque: el dueño entra con el correo de `Seed:AdminEmail` y queda como Admin.
