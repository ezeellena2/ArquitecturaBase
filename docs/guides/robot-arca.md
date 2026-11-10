# Correr y mantener el robot de ARCA

El robot de delegación es el modo 3 de acceso a ARCA ([ADR 0026](../decisions/0026-robot-de-delegacion-arca.md)): con
Playwright y Chromium hace en la página de ARCA lo que la [delegación guiada](configurar-arca.md) hace a mano. La regla
vigente está en [ARCA](../features/arca.md#delegación-con-robot-modo-3). Esta guía cuenta cómo prenderlo, cómo probarlo con
cuentas reales y qué tocar cuando ARCA cambia una pantalla.

## Antes de prenderlo

- **La delegación guiada configurada:** la CUIT y el nombre de la plataforma (`Arca:Platform:Cuit` y `Arca:Platform:Name`).
  El robot delega en esa CUIT y, si se frena, el cliente sigue con esa guía.
- **La revisión legal:** el usuario la confirmó el 2026-11-09. Cada instalación lo prende sólo donde se ofrece.
- **Chromium** en la máquina o en la imagen (ver «Despliegue»).

## Configuración

Todo va en la sección `Arca:Robot`. Las claves son secretos de la instalación: user-secrets en desarrollo, Key Vault o
variables protegidas al desplegar. Nunca en `appsettings` ni en el repo.

| Clave | Para qué | Por defecto |
| --- | --- | --- |
| `Enabled` | Ofrece «Delegación automática» y arranca los workers | `false` |
| `Platform:LoginCuit` | La CUIT o CUIL con la que el robot entra del lado de la plataforma (secreto) | — |
| `Platform:FiscalKey` | La clave fiscal de esa cuenta (secreto) | — |
| `AlertEmail` | A quién le llega el aviso cuando el robot se rompe; vacío, sólo queda el log | — |
| `LoginUrl` | La página de ingreso con clave fiscal | la de ARCA |
| `ServiceLabels:<código WSN>` | El nombre del servicio en el Administrador de Relaciones, si ARCA lo cambia | `wsfe`: «Facturación Electrónica»; constancia: «Consulta de Constancia de Inscripción» |
| `StepTimeout`, `RunTimeout` | Lo que espera cada acción y una pasada entera | 45 s y 10 min |
| `Lease`, `RetryDelay`, `MaxAttempts` | La reserva de un lado y los reintentos del lado de la plataforma | 3 min, 2 min y 5 |
| `PollInterval`, `QueueCapacity` | Cada cuánto mira el worker de la plataforma y cuántas corridas del cliente espera un host | 30 s y 4 |

En desarrollo:

```bash
dotnet user-secrets --project src/ArquitecturaBase.Api set "Arca:Robot:Enabled" "true"
```

```bash
dotnet user-secrets --project src/ArquitecturaBase.Api set "Arca:Robot:Platform:LoginCuit" "<CUIT de la plataforma>"
```

```bash
dotnet user-secrets --project src/ArquitecturaBase.Api set "Arca:Robot:Platform:FiscalKey" "<clave fiscal de la plataforma>"
```

Con el robot prendido, la Api no arranca sin Chromium, sin la clave de la plataforma o con la depuración de Playwright
encendida (`DEBUG=pw:*` o `PWDEBUG`), que escribiría en los logs lo que se teclea.

## Chromium

La versión de Chromium la fija el paquete `Microsoft.Playwright` de [`Directory.Packages.props`](../../Directory.Packages.props).
Después de compilar, el instalador queda junto a los binarios:

```bash
pwsh src/ArquitecturaBase.Api/bin/Debug/net10.0/playwright.ps1 install chromium
```

En Linux, sumá `--with-deps` para las bibliotecas del sistema. Actualizar el paquete es instalar el Chromium nuevo en cada
máquina y en la imagen. El workflow lo instala antes de las pruebas de .NET.

## Despliegue

La imagen de la Api se genera con el SDK de .NET, sin Dockerfile ([azure-dev](../deploy/azure-dev.md)). Donde el robot
está apagado, alcanza la imagen de siempre. Donde se prende, la imagen necesita una base con Chromium y sus dependencias:

1. Generá la base una vez por versión de Playwright, a partir de la imagen de ASP.NET y con el instalador del publicado
   para Linux (`dotnet publish --runtime linux-x64` deja `playwright.ps1` y `.playwright/` en la salida):

   ```dockerfile
   FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
   ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
   COPY .playwright /tmp/playwright
   RUN /tmp/playwright/node/linux-x64/node /tmp/playwright/package/cli.js install --with-deps chromium \
       && chmod -R a+rX /ms-playwright && rm -rf /tmp/playwright
   ```

2. Subila al registro de la instalación y publicá la Api sobre ella con `-p:ContainerBaseImage=<registro>/<base>:<versión>`
   y la variable `PLAYWRIGHT_BROWSERS_PATH=/ms-playwright` en la Container App.
3. Prendé `Arca:Robot:Enabled` y cargá los secretos. Si la Api no arranca, el log dice qué falta.

Esta receta no se corrió todavía contra Azure: se prueba la primera vez que una instalación prenda el robot.

## La corrida real

El Administrador de Relaciones existe sólo en producción, así que las pruebas automáticas van contra páginas simuladas
(`FakeArcaWeb`). La corrida real la hace el usuario, con sus cuentas, en una instalación con el robot prendido:

1. En ARCA, una conexión nueva con «Delegación automática», la CUIT de una empresa de prueba y Facturación electrónica.
2. En la zona del robot, la CUIT o CUIL con la que entra a ARCA, su clave y la casilla; «Delegar con el robot».
3. Mirar el avance: los pasos del cliente, después los de la plataforma, y la prueba en verde al final.
4. Si se frena, anotar el paso y el motivo que muestra la pantalla (o el aviso por correo) y seguir con la guía.

Lo que falle en esa corrida es una pantalla distinta de la simulada: se corrige como dice la próxima sección.

## Cuando ARCA cambia una pantalla

El aviso dice el paso (por ejemplo `company.point-of-sale`) y el motivo. Para corregirlo:

1. Reproducilo con una cuenta propia: el robot no guarda capturas ni trazas, a propósito.
2. Corregí lo que espera esa pantalla en [`ArcaRobotScreens`](../../src/ArquitecturaBase.Infrastructure/Modules/Arca/Connection/Robot/ArcaRobotScreens.cs):
   textos, ids y selectores están ahí, y se comparan sin mayúsculas ni tildes. Un nombre de servicio se corrige sin tocar
   código con `ServiceLabels`.
3. Cambiá igual la página simulada en
   [`FakeArcaWeb`](../../tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/Arca/FakeArcaWeb.cs) y corré
   `ArcaDelegationRobotTests`.
4. Repetí la corrida real.

| Motivo | Qué revisar |
| --- | --- |
| Pantalla cambiada | El paso del aviso: un texto, un botón o una tabla que ya no son los de `ArcaRobotScreens` |
| Captcha | ARCA lo pide a veces; el cliente sigue con la guía. Si es siempre, el robot no sirve para esa cuenta |
| Validación extra | Un cambio de clave obligatorio, un segundo factor o condiciones por aceptar: lo hace la persona en ARCA |
| Ingreso rechazado | Del cliente, su CUIT o su clave. De la plataforma, la clave de `Platform:FiscalKey`: el robot frena para no bloquearla |
| ARCA no responde | El lado de la plataforma reintenta solo; el del cliente vuelve a probar o sigue con la guía |
| Interrumpida | El host se reinició en medio de la corrida: el cliente la arranca de nuevo |

## La clave fiscal

- La del cliente vive sólo en memoria, en el host que recibió el pedido: no va a la base, a los logs, a la cola ni a otro
  host. Una prueba la busca en las respuestas, los logs y las filas del módulo.
- No habilites volcados de memoria del proceso en producción: podrían tener la clave.
- No agregues trazas, HAR, video ni capturas de Playwright, ni registres el mensaje de sus excepciones: pueden repetir lo
  escrito en la página.

## Quitar el robot

Está en [quitar ARCA](quitar-arca.md#quitar-el-robot-de-delegación).
