# Arquitectura Base

Plantilla base para aplicaciones web con .NET 10, Aspire, React y PostgreSQL, organizada en Clean Architecture. Este repo es el backend; el front está en `../ArquitecturaBaseFront`.

Este README dice cómo levantar el proyecto, cómo probarlo y dónde está el resto de la documentación. La arquitectura vigente es [`docs/architecture/backend.md`](docs/architecture/backend.md), y las reglas para trabajar en el código, [`AGENTS.md`](AGENTS.md). El [diseño inicial](docs/specs/2026-09-18-arquitectura-base-design.md) sigue valiendo en lo funcional, pero es histórico para la estructura de capas y el pipeline HTTP.

## Mapa de la documentación

| Dónde | Qué hay |
|---|---|
| [`AGENTS.md`](AGENTS.md) | el índice de las reglas de la plantilla para cualquier agente: forma de trabajo, comandos, capas, casos de uso, persistencia, errores, textos, build, tests y dónde va cada cosa |
| [`CLAUDE.md`](CLAUDE.md) | importa `AGENTS.md` y suma solo lo propio de Claude Code |
| [`docs/architecture/backend.md`](docs/architecture/backend.md) | la arquitectura canónica del backend: capas, recorrido de un caso de uso, borde HTTP, una sola forma de guardar, migraciones, front y tests |
| [`docs/guides/`](docs/guides/) | las guías paso a paso: [agregar un área](docs/guides/agregar-un-area.md), [permiso nuevo](docs/guides/permiso-nuevo.md), [migración](docs/guides/migracion.md), [prefijo de backend](docs/guides/prefijo-de-backend.md), [WhatsApp en local](docs/guides/whatsapp-en-local.md) (la configuración, los secretos, el túnel y las plantillas de Meta), [quitar WhatsApp](docs/guides/quitar-whatsapp.md) (para un proyecto que no lo usa) y [despliegue](docs/guides/despliegue.md) (el orden, la configuración obligatoria en producción, el proxy y los pendientes) |
| [`docs/features/`](docs/features/) | las reglas de cada área del producto: [identidad](docs/features/identidad.md), [WhatsApp](docs/features/whatsapp.md) y [administración](docs/features/administracion.md) |
| [`docs/specs/`](docs/specs/) | los diseños funcionales vigentes. Son históricos para la estructura del código, que fija la arquitectura canónica, pero sus reglas funcionales siguen valiendo |
| [`docs/decisions/`](docs/decisions/README.md) | las decisiones de arquitectura (ADR), una por archivo |
| [`docs/plans/`](docs/plans/) | el [plan maestro](docs/plans/2026-09-26-plantilla-estandar-por-etapas.md) y los planes en curso |
| [`docs/history/`](docs/history/) | los planes terminados y los inventarios previos a la migración a MVC. No se ejecutan |
| [`docs/deploy/`](docs/deploy/azure-setup.md) y [`docs/postman/`](docs/postman/README.md) | la infraestructura de Azure y la colección de Postman para probar la identidad |

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

El JSON de credenciales que descarga Google Cloud (`client_secret_*.json`) se guarda **fuera del repo**, en una carpeta tuya (por ejemplo `%USERPROFILE%\secrets\ArquitecturaBase\`); el `.gitignore` igual lo ignora, por las dudas. La Api no lee ese archivo: lo único que necesita de él es el `ClientSecret`, que va en user-secrets como indica la tabla (el `ClientId` no es secreto y está en `appsettings.json`).

### Emails

En desarrollo los emails **se envían de verdad**, por Gmail, igual que en producción: así el código de ingreso llega a la casilla y el flujo se prueba entero. `appsettings.Development.json` trae `Email:Delivery` en `Smtp` y la cuenta que envía; la contraseña va en user-secrets:

```
dotnet user-secrets set "Email:Smtp:Password" "<contraseña de aplicación>" --project src/ArquitecturaBase.Api
```

Es una **contraseña de aplicación** de Gmail (https://myaccount.google.com/apppasswords), no la contraseña de la cuenta ni el ClientSecret de OAuth: son tres cosas distintas. Sin ella la Api **no arranca**, porque `SmtpOptions` se valida al iniciar.

Gmail reescribe el remitente a la cuenta que autentica, así que los correos salen desde `Email:Smtp:UserName` aunque `FromName` diga otra cosa. El límite es de unos 500 envíos por día.

Para volver a no enviar nada y escribir archivos `.eml` en `src/ArquitecturaBase.Api/.emails/` (carpeta ignorada por git), alcanza con poner `Email:Delivery` en `PickupDirectory`. Sirve cuando no hay internet o no se quiere gastar la cuota.

## El primer ingreso

El ingreso es sin contraseña: con un código de 6 dígitos que llega por email, o con Google. La Api es a la vez el servidor OpenIddict (`/connect/*`) y la Api de negocio (`/api/*`, con bearer).

El modo de registro (`/configuracion` en el front, `PUT /api/settings` en la Api) decide quién puede crear una cuenta:

| Modo | Qué pasa con un correo que no tiene cuenta |
|---|---|
| `InviteOnly` | No entra. La cuenta la tiene que crear un administrador. |
| `Open` | Se crea la cuenta sola, con el rol `User`. |

El valor inicial, al crear la base, sale de `Registration:Mode` y por defecto es **`InviteOnly`**: una instalación nueva arranca cerrada y se abre a propósito. Después manda lo que diga la base: el seed no pisa la fila si ya existe. La única excepción es el administrador inicial (`Seed:AdminEmail`), que crea su cuenta en su primer ingreso, por código o con Google, en cualquier modo. Por eso, en una instalación nueva en `InviteOnly`, **sin `Seed:AdminEmail` no entra nadie**. Las reglas del modo de registro, de las cuentas y de los roles están en [`docs/features/administracion.md`](docs/features/administracion.md).

WhatsApp viene apagado salvo que se configure: cómo prenderlo y probarlo está en [WhatsApp en local](docs/guides/whatsapp-en-local.md). Es un módulo opcional: un proyecto que no lo usa lo quita con [quitar WhatsApp](docs/guides/quitar-whatsapp.md).

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

Los tests de integración levantan su propio Postgres con Testcontainers, así que necesitan Docker encendido. Para un proyecto o una clase: `dotnet test --project tests/<Proyecto>/<Proyecto>.csproj -- --filter-class "<Namespace.Clase>"`. Qué prueba cada proyecto, en [backend.md, "Tests: arquitectura y arnés"](docs/architecture/backend.md#tests-arquitectura-y-arnés).

El flujo de ingreso a mano, con Postman: [docs/postman/README.md](docs/postman/README.md).

**Test inestable (resuelto).** `UsersEndpointsTests.Admin_gets_every_permission` y `Sorting_by_a_field_outside_the_whitelist_is_rejected` fallaban de vez en cuando adentro de `AuthFlow.LoginAsync`. La causa estaba en el código de producción: con el reloj congelado, dos códigos pedidos en el mismo instante dejaban en manos de la base cuál era el último, y a veces devolvía uno ya consumido (`Auth.LoginCode.AlreadyUsed`). Lo arregló el commit `422a6de` con el desempate de `LoginCodeRepository.GetLatestAsync`. Si vuelve a fallar un ingreso en los tests, `AuthFlow` muestra el ProblemDetails del verify que no respondió 200.

## Desplegar

Todo lo de producción (el orden bundle → imagen, qué hace la Api al arrancar, la configuración obligatoria, el proxy y los encabezados reenviados, y lo que todavía no está resuelto, como el `dist/` del front) está en la [guía de despliegue](docs/guides/despliegue.md); la infraestructura de Azure, en [`docs/deploy/azure-setup.md`](docs/deploy/azure-setup.md).
