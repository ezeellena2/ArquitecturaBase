# Arquitectura Base

Plantilla base para aplicaciones web con .NET 10, Aspire, React, PostgreSQL y Redis, organizada en Clean Architecture. Este repo es el backend; el front está en `../ArquitecturaBaseFront`.

Este README dice cómo levantar el proyecto, cómo probarlo y dónde está el resto de la documentación. La arquitectura vigente es [`docs/architecture/backend.md`](docs/architecture/backend.md), y las reglas para trabajar en el código, [`AGENTS.md`](AGENTS.md). Las [features](docs/features/) describen los contratos funcionales actuales; el [diseño inicial](docs/specs/2026-09-18-arquitectura-base-design.md) conserva la procedencia histórica.

## Módulos opcionales

WhatsApp, Formularios, IA y ARCA viven en `Modules/<Módulo>` de cada proyecto y se pueden quitar: el núcleo no los nombra ([módulos](docs/architecture/backend.md#módulos-opcionales)).

| Módulo | Reglas | Configurar | Quitar |
|---|---|---|---|
| WhatsApp | [whatsapp.md](docs/features/whatsapp.md) | [WhatsApp en local](docs/guides/whatsapp-en-local.md) | [quitar WhatsApp](docs/guides/quitar-whatsapp.md) |
| Formularios | [formularios.md](docs/features/formularios.md) | [archivos en Azure Blob](docs/guides/azure-blob-en-local.md) | [quitar Formularios](docs/guides/quitar-formularios.md) |
| IA | [inteligencia-artificial.md](docs/features/inteligencia-artificial.md) | [configurar IA](docs/guides/configurar-ia.md) (el anillo externo, aun sin clave de proveedor, y el [asistente de Inicio](docs/guides/configurar-ia.md#habilitar-el-asistente)) y [Google Places](docs/guides/configurar-google-places.md) | [quitar IA](docs/guides/quitar-ia.md) |
| ARCA | [arca.md](docs/features/arca.md) | [configurar ARCA](docs/guides/configurar-arca.md) y el [robot](docs/guides/robot-arca.md) | [quitar ARCA](docs/guides/quitar-arca.md) |

## Mapa de la documentación

| Dónde | Qué hay |
|---|---|
| [`AGENTS.md`](AGENTS.md) | el índice de las reglas de la plantilla para cualquier agente: forma de trabajo, comandos, capas, casos de uso, persistencia, errores, textos, build, tests y dónde va cada cosa |
| [`CLAUDE.md`](CLAUDE.md) | importa `AGENTS.md` y suma solo lo propio de Claude Code |
| [Mapa de instrucciones por carpeta](docs/architecture/mapa-de-instrucciones.md) | qué hace cada capa, área, adaptador y arnés; enlaza sus `AGENTS.md` y el mapa del front |
| [Instrucciones por carpeta](docs/guides/instrucciones-por-carpeta.md) | cuándo crear un par `AGENTS.md`/`CLAUDE.md`, qué documentar y cómo mantenerlo con cada desarrollo |
| [Premisas de la plantilla](docs/architecture/premisas-de-la-plantilla.md) y [auditor documental](docs/guides/auditar-documentacion.md) | decisiones implementadas con su fuente y evidencia; cómo consolidar reglas, conservar pendientes y retirar planes cumplidos |
| [Arneses de desarrollo](harnesses/README.md) y [auditor ejecutable](harnesses/documentation/README.md) | quién los inicia, estructura y scripts; revisión o actualización documental con Codex o Claude |
| [`docs/architecture/backend.md`](docs/architecture/backend.md) | la arquitectura canónica del backend: capas, recorrido de un caso de uso, borde HTTP, una sola forma de guardar, migraciones, front y tests |
| [`docs/guides/`](docs/guides/) | las guías paso a paso: [validar un cambio](docs/guides/validar-un-cambio.md), [agregar un área](docs/guides/agregar-un-area.md), [permiso nuevo](docs/guides/permiso-nuevo.md), [migración](docs/guides/migracion.md), [agregar caché](docs/guides/agregar-cache.md), [prefijo de backend](docs/guides/prefijo-de-backend.md), [revisar la arquitectura](docs/guides/revisar-arquitectura.md), [probar en el celular](docs/guides/probar-en-el-celular.md) y [despliegue](docs/guides/despliegue.md) (el orden, la configuración obligatoria en producción, el proxy, los límites, el cupo del correo y la retención); las de cada módulo están en la tabla de arriba |
| [`docs/features/`](docs/features/) | las reglas de cada área del producto: [identidad](docs/features/identidad.md) y [administración](docs/features/administracion.md) en el núcleo, y las de cada módulo (tabla de arriba) |
| [`docs/specs/`](docs/specs/) | diseños de referencia con fecha y secciones reemplazadas identificadas; las reglas implementadas actuales están en `features` y las arquitecturas de ambos repositorios |
| [`docs/decisions/`](docs/decisions/README.md) | las decisiones de arquitectura (ADR), una por archivo |
| [Planes pendientes](docs/plans/README.md) | propuestas, adopción parcial y comprobaciones abiertas; incluye el plan del front dueño de la unificación de mensajes |
| [Procedencia](docs/history/README.md) | dónde quedaron las reglas de planes retirados y cómo recuperar los originales desde Git |
| [`docs/reviews/`](docs/reviews/) | evidencia fechada de revisiones y validaciones; no sustituye contratos vigentes ni acredita proveedores externos por una suite simulada |
| [`docs/deploy/`](docs/deploy/azure-dev.md) y [`docs/postman/`](docs/postman/README.md) | conceptos y puesta en marcha de Azure dev, infraestructura y colección de Postman para probar la identidad |

## Revisar y actualizar documentación

Vos iniciás el arnés desde la raíz de este repositorio. PowerShell llama al runner de Node,
que inicia la CLI elegida y verifica su propuesta. Ambos motores usan el mismo procedimiento.

```powershell
# Ver el recorrido y los archivos de entrada sin llamar a un agente.
.\harnesses\documentation\run.ps1 -DryRun

# Actualizar con Codex; podés cambiar codex por claude.
.\harnesses\documentation\run.ps1 -Engine codex -Mode update
```

Requiere Node, la CLI elegida instalada y autenticada, y las herramientas del build/test
de ambos repositorios. [La guía del arnés](harnesses/documentation/README.md) explica `audit`,
`update`, los controles y dónde encontrar el informe. No se ejecuta automáticamente al abrir
un chat ni al guardar código.

## Requisitos

- .NET SDK 10.0.400 o superior (lo fija `global.json`).
- Docker Desktop encendido. Lo usan PostgreSQL y Redis del AppHost y los tests de integración.
- Aspire CLI 13.6.5:

  ```bash
  dotnet tool install -g Aspire.Cli --version 13.6.5
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
- **cache:** Redis 8.6, en un puerto asignado por Aspire. Contenedor efímero: sus datos son reconstruibles. Es el único caché de datos compartidos.
- **api:** espera a PostgreSQL y Redis y, en desarrollo, aplica las migraciones al iniciar. También en desarrollo:
  - OpenAPI en `/openapi/v1.json`;
  - Swagger UI en `/swagger` (en el dashboard de Aspire, el link "Swagger UI" de la fila `api`);
  - health checks en `/health` y `/alive`.
- **front:** el SPA del repo `../ArquitecturaBaseFront`, servido por Vite. Espera a la Api.

**La dirección que se abre en el navegador es `https://localhost:5173`**, la del front. Ver [El front y el origen único](#el-front-y-el-origen-único).

También funciona con `dotnet run --project src/ArquitecturaBase.AppHost` o con F5 sobre el AppHost en Visual Studio.

Con F5, la ventana Salida muestra solo los avisos y errores de la Api (`Logging:Debug` en `appsettings.Development.json`): con el depurador enganchado, escribir ahí cada línea frena toda la Api. El log completo, con cada consulta SQL, está en el dashboard de Aspire.

Si el arranque avisa que Redis no respondió después del seed, el cliente no pudo conectarse a Redis
al descartar los ajustes: la Api arranca igual y `/health` da `Degraded` (N9). Un contenedor `Running`
no demuestra esa conexión: revisá los logs de `cache` y `api`, levantá desde el AppHost y conservá el
puerto dinámico que Aspire inyecta. No copies a user-secrets el puerto de un arranque anterior.
[Diagnóstico y comprobación local](docs/reviews/2026-10-03-roles-y-arranque-redis.md).

**Al terminar, `aspire stop`.** Si queda corriendo, el arranque desde Visual Studio falla con `address already in use` y los DLL quedan bloqueados. El contenedor de Postgres sigue vivo a propósito.

### El front y el origen único

El navegador habla con **un solo origen**, así que no hay CORS ni cookies de terceros. Cómo se arma ese origen cambia según el entorno:

| | Desarrollo (`aspire run`) | Producción |
|---|---|---|
| Origen del navegador | `https://localhost:5173` | el del despliegue |
| Quién sirve el SPA | Vite (repo `../ArquitecturaBaseFront`) | la Api, desde `wwwroot` |
| Quién atiende `/api`, `/account`, `/connect`, `/signin-google`, `/.well-known`, `/webhooks` | la Api, `https://localhost:7180`, a la que Vite reenvía con proxy | la Api, el mismo proceso |

Dos consecuencias:

- **El issuer de OpenIddict es el origen público, no el de la Api.** En desarrollo se fija con `Authentication:Issuer` en `https://localhost:5173/`, porque es lo que ve el navegador. El proxy de Vite va con `changeOrigin: false` para que la Api reciba `Host: localhost:5173` y arme bien el resto de los endpoints del documento de discovery y el `redirect_uri` de Google.
- **El puerto 7180 casi no se usa a mano.** Sirve para Swagger UI y para pegarle a la Api sin pasar por el front (Postman). El ingreso con Google funciona por los dos, porque el cliente OAuth tiene registradas las dos URIs de redireccionamiento.

Las rutas del SPA las resuelve su propio router. Del lado de la Api eso es `UseSpaFallback` (`src/ArquitecturaBase.Api/Hosting/SpaExtensions.cs`): sirve el `index.html` en las rutas que nadie atendió, sin tocar las del backend, que siguen devolviendo su 404 o 405 con ProblemDetails. Si no hay `wwwroot/index.html` —el caso de desarrollo, donde el SPA lo sirve Vite— el middleware no se instala.

El detalle (el issuer, el proxy y el fallback del SPA) está en [backend.md, "Front y hosting del SPA"](docs/architecture/backend.md#front-y-hosting-del-spa).

## Configuración de desarrollo

El repo trae la configuración de la plantilla, no la de una instalación: en Development, la Api arranca con WhatsApp y Google apagados y los correos escritos como archivos. Los únicos secretos que pide para arrancar son los anillos de claves de IA y de ARCA, mientras esos módulos estén: se generan una vez por instalación, como explican [configurar IA](docs/guides/configurar-ia.md) y [configurar ARCA](docs/guides/configurar-arca.md). `src/ArquitecturaBase.Api/appsettings.Development.json` trae lo necesario para trabajar local:
- la clave HMAC de los códigos;
- las redirect URIs del cliente `web`;
- `Email:Delivery` en `PickupDirectory`: los correos quedan como archivos `.eml` (ver **Emails**, abajo).

Lo de cada instalación va en los user-secrets de la Api, aunque no todo sea secreto: quién es el administrador inicial, desde qué cuenta salen los correos, el cliente de Google y el número de WhatsApp. Así no viaja con la plantilla cuando se copia para otro negocio.

### User-secrets de la Api

Cada grupo de claves prende algo; sin él, la Api arranca igual, con eso apagado.

| Clave | Para qué | Secreto |
|---|---|---|
| `Seed:AdminEmail` | el correo que recibe el rol Admin al crear su cuenta, en su primer ingreso | — |
| `Email:Delivery` en `Smtp`, `Email:Smtp:UserName`, `Email:Smtp:FromAddress` y `Email:Smtp:Password` | enviar los correos de verdad, por Gmail (ver **Emails**) | `Email:Smtp:Password` |
| `Authentication:Google:ClientId` y `Authentication:Google:ClientSecret` | el ingreso con Google. Sin `ClientId` queda apagado; con él y sin `ClientSecret`, la Api no arranca | `Authentication:Google:ClientSecret` |
| `WhatsApp:PhoneNumberId`, `WhatsApp:AccessToken` y `WhatsApp:WabaId` | el ingreso y la atención por WhatsApp. `PhoneNumberId`, el id del número en Meta, es el interruptor: con él y sin `AccessToken`, la Api no arranca. `WabaId`, el de la cuenta, sirve para publicar Flows. El webhook suma `WhatsApp:AppSecret` y `WhatsApp:VerifyToken` ([WhatsApp en local](docs/guides/whatsapp-en-local.md)) | `WhatsApp:AccessToken`, `WhatsApp:AppSecret` y `WhatsApp:VerifyToken` |

Se cargan desde la raíz del repo, una clave por comando:

```
dotnet user-secrets set "Seed:AdminEmail" "<tu correo>" --project src/ArquitecturaBase.Api
dotnet user-secrets set "Authentication:Google:ClientId" "<id>.apps.googleusercontent.com" --project src/ArquitecturaBase.Api
dotnet user-secrets set "Authentication:Google:ClientSecret" "<secreto>" --project src/ArquitecturaBase.Api
dotnet user-secrets set "WhatsApp:PhoneNumberId" "<id del número>" --project src/ArquitecturaBase.Api
dotnet user-secrets set "WhatsApp:WabaId" "<id de la cuenta>" --project src/ArquitecturaBase.Api
dotnet user-secrets set "WhatsApp:AccessToken" "<token>" --project src/ArquitecturaBase.Api
```

Un secreto conviene cargarlo sin que quede a la vista en la consola ni en su historial: cómo, en [WhatsApp en local](docs/guides/whatsapp-en-local.md#los-tres-secretos-y-de-dónde-salen). Para ver qué claves quedaron cargadas, sin los valores, en PowerShell: `(dotnet user-secrets list --project src/ArquitecturaBase.Api) -replace ' = .*', ''`. Se leen al arrancar: después de cambiar una, reiniciá la Api.

En Google Cloud Console, el cliente OAuth tiene que tener como URIs de redireccionamiento autorizados `https://localhost:7180/signin-google` (Api directa) y `https://localhost:5173/signin-google` (a través de Vite, Fase 3).

El JSON de credenciales que descarga Google Cloud (`client_secret_*.json`) se guarda **fuera del repo**, en una carpeta tuya (por ejemplo `%USERPROFILE%\secrets\ArquitecturaBase\`); el `.gitignore` igual lo ignora, por las dudas. La Api no lee ese archivo: lo único que necesita de él son el `ClientId` y el `ClientSecret`, que van en user-secrets como indica la tabla.

### Emails

En desarrollo, la plantilla escribe cada correo como un archivo `.eml` en `src/ArquitecturaBase.Api/.emails/` (carpeta ignorada por git): `appsettings.Development.json` trae `Email:Delivery` en `PickupDirectory`. El código de ingreso es la primera palabra del asunto. Sólo vale en Development y en los tests: en otro ambiente la Api no arranca así, porque nadie recibiría los correos.

Para que lleguen de verdad a la casilla, por Gmail como en producción, y probar el flujo entero, la instalación carga en sus user-secrets la cuenta que envía:

```
dotnet user-secrets set "Email:Delivery" "Smtp" --project src/ArquitecturaBase.Api
dotnet user-secrets set "Email:Smtp:UserName" "<cuenta>@gmail.com" --project src/ArquitecturaBase.Api
dotnet user-secrets set "Email:Smtp:FromAddress" "<cuenta>@gmail.com" --project src/ArquitecturaBase.Api
dotnet user-secrets set "Email:Smtp:Password" "<contraseña de aplicación>" --project src/ArquitecturaBase.Api
```

La contraseña es una **contraseña de aplicación** de Gmail (https://myaccount.google.com/apppasswords), no la contraseña de la cuenta ni el ClientSecret de OAuth: son tres cosas distintas. Con `Email:Delivery` en `Smtp`, sin la cuenta o sin la contraseña la Api **no arranca**, porque `SmtpOptions` se valida al iniciar.

Gmail reescribe la dirección del remitente a la cuenta que autentica, así que los correos salen desde `Email:Smtp:UserName` aunque `FromAddress` diga otra cosa; el nombre que se ve es el del remitente de Configuración › Apariencia. El límite es de unos 500 envíos por día. Para volver a los `.eml`, sin internet o para no gastar la cuota, alcanza con poner `Email:Delivery` en `PickupDirectory`.

## Caché compartido

El modo de registro y las preferencias generales de presentación usan Redis directo, sin HybridCache ni nivel local; los permisos se leen de la base en cada pedido. Sin Redis la app sigue, con `/health` en `Degraded` ([ADR 0032](docs/decisions/0032-sin-redis-la-app-sigue.md)). AppHost entrega `ConnectionStrings:cache`; fuera de Aspire hay que provisionar Redis y configurar `ConnectionStrings__cache` como secreto. Las réplicas comparten `Caching__KeyPrefix`; cada instalación y ambiente usa uno distinto, y fuera de Development y Testing es obligatorio. Redis participa en `/health` y queda fuera de `/alive`.

Una funcionalidad nueva sigue [agregar-cache.md](docs/guides/agregar-cache.md): fábrica en scope propio, TTL, invalidación después del commit y pruebas entre instancias. Las garantías de permisos, coordinación, fallas y métricas están en [backend.md](docs/architecture/backend.md#caché-compartido-en-redis); la conexión de producción, en [despliegue.md](docs/guides/despliegue.md#redis).

## El primer ingreso

El ingreso es sin contraseña: con un código de 6 dígitos que llega por email, o con Google. La Api es a la vez el servidor OpenIddict (`/connect/*`) y la Api de negocio (`/api/*`, con bearer).

El modo de registro (`/configuracion` en el front; `PATCH /api/settings` con `expectedRevision` en la Api, desde que el PUT sin revisión se retiró el 2026-11-16) decide quién puede crear una cuenta:

| Modo | Qué pasa con un correo que no tiene cuenta |
|---|---|
| `InviteOnly` | No entra. La cuenta la tiene que crear un administrador. |
| `Open` | Permite crear la cuenta desde Registro, con el rol `User`. El ingreso explícito no registra una cuenta nueva. |

El valor inicial, al crear la base, sale de `Registration:Mode`: **el `appsettings.json` actual configura `Open`**. `RegistrationOptions` usa `InviteOnly` únicamente si esa clave no está configurada. Después manda lo que diga la base: el seed no pisa la fila si ya existe. La única excepción es el administrador inicial (`Seed:AdminEmail`), que crea su cuenta en su primer ingreso, por código o con Google, en cualquier modo. En una base nueva configurada en `InviteOnly`, sin cuentas previas ni `Seed:AdminEmail`, nadie puede crear su cuenta. Las reglas del modo de registro, de las cuentas y de los roles están en [`docs/features/administracion.md`](docs/features/administracion.md); la intención explícita de ingreso o registro y la compatibilidad de pedidos antiguos, en [`identidad.md`](docs/features/identidad.md#ingreso-y-registro-explícitos-2026-09-30).

WhatsApp viene apagado: se prende con su número y su token en los [user-secrets de la Api](#user-secrets-de-la-api), y cómo probarlo, con el webhook y las plantillas, está en [WhatsApp en local](docs/guides/whatsapp-en-local.md). Es un módulo opcional: un proyecto que no lo usa lo quita con [quitar WhatsApp](docs/guides/quitar-whatsapp.md).

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

## Probar

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

Los tests de integración levantan PostgreSQL y Redis propios con Testcontainers, así que necesitan Docker encendido. Para un proyecto o una clase: `dotnet test --project tests/<Proyecto>/<Proyecto>.csproj -- --filter-class "<Namespace.Clase>"`. Qué prueba cada proyecto, en [backend.md, "Tests: arquitectura y arnés"](docs/architecture/backend.md#tests-arquitectura-y-arnés).

El flujo de ingreso a mano, con Postman: [docs/postman/README.md](docs/postman/README.md).

**Test inestable (resuelto).** `UsersEndpointsTests.Admin_gets_every_permission` y `Sorting_by_a_field_outside_the_whitelist_is_rejected` fallaban de vez en cuando adentro de `AuthFlow.LoginAsync`. La causa estaba en el código de producción: con el reloj congelado, dos códigos pedidos en el mismo instante dejaban en manos de la base cuál era el último, y a veces devolvía uno ya consumido (`Auth.LoginCode.AlreadyUsed`). Lo arregló el commit `422a6de` con el desempate de `LoginCodeRepository.GetLatestAsync`. Si vuelve a fallar un ingreso en los tests, `AuthFlow` muestra el ProblemDetails del verify que no respondió 200.

## Desplegar

El workflow obtiene ambos repositorios y publica React con la Api en una imagen. Los conceptos y la puesta en marcha de dev están en [Azure dev](docs/deploy/azure-dev.md). El orden bundle → imagen, el arranque, la configuración obligatoria y el proxy están en la [guía de despliegue](docs/guides/despliegue.md); la receta técnica de recursos, en [azure-setup.md](docs/deploy/azure-setup.md). La preparación local no acredita todavía un despliegue en Azure.
