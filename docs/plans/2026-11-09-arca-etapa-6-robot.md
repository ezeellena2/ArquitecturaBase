# ARCA · Etapa 6: delegación con robot

Plan del 2026-11-09, decidido el mismo día (RB1-A a RB5-A, con la revisión legal confirmada). El backend quedó programado
el mismo día: la regla vigente está en [ARCA](../features/arca.md#delegación-con-robot-modo-3) y la operación en
[correr y mantener el robot](../guides/robot-arca.md). Faltan el front, que espera la prueba integral, y la corrida real
con las cuentas del usuario. Es la etapa 6
de la [hoja de ruta de ARCA](2026-10-31-arca-formularios-estandar.md) y el modo 3 de acceso: un robot propio, con
Playwright para .NET, hace en la página de ARCA lo que en la etapa 5 se hace a mano. El cliente entra una sola vez con su
clave fiscal; ni él ni el dueño de la plataforma tienen que entrar después a ARCA para crear el certificado, delegar,
aceptar o asignar.

Depende de la **etapa 5 (delegación guiada)**, que otro chat diseña y programa a la vez
([plan](2026-11-09-arca-etapa-5-delegacion-guiada.md)). El robot reusa de ella el certificado de la CUIT de la plataforma
dentro de la conexión, la detección de la delegación y la guía, que es la vuelta cuando el robot no puede seguir.

La decisión estructural está en el [ADR 0026](../decisions/0026-robot-de-delegacion-arca.md), que la hoja de ruta y el
[ADR 0023](../decisions/0023-arca-como-modulo-opcional.md) piden antes de programar: el robot sobre la página de ARCA y
cómo se protege la clave fiscal en memoria.

## Decisiones (2026-11-09)

1. **RB1-A · El consentimiento en la zona.** La delegación automática es una zona más del alta, como AC1-A: «Entrás con»
   (la CUIT de la empresa o la CUIL de quien administra sus relaciones), la clave fiscal y una casilla que dice qué
   autoriza. Sin la casilla, «Delegar con el robot» queda apagado con su motivo. Un ingreso rechazado va debajo de la clave,
   que queda vacía. Se descartó RB1-B (un diálogo aparte).
2. **RB2-A · El avance en pasos.** La zona lista los pasos con su estado (hecho, en curso, pendiente): a la izquierda lo
   que hace con la clave del cliente, a la derecha lo que hace con la de la plataforma. Esperando a la plataforma, «Falta
   que la plataforma acepte» con «Comprobar». Se descartó RB2-B (una barra con el paso actual).
3. **RB3-A · La guía en el paso donde se frenó.** La misma zona pasa a la guía de la etapa 5, abierta en ese paso, con lo
   hecho marcado «Lo hizo el robot» y el motivo en un aviso; «Probar otra vez con el robot» queda en la banda. Se descartó
   RB3-B (un aviso con dos salidas y la guía desde el principio).
4. **RB4-A · El aviso por correo.** Cuando el robot se rompe, un correo a una dirección de la plataforma configurada en la
   instalación, por la cola de correos de siempre: el paso, el motivo y la hora, sin datos del cliente. Como mucho uno por
   motivo y paso por día, además del log de error. Se descartó «sólo log y alerta de Azure».
5. **RB5-A · El robot también crea el certificado de la plataforma.** Con la clave de la plataforma, crea el alias en
   «Administración de Certificados Digitales» con el pedido que genera la app, descarga el certificado y lo carga. Así el
   modo 3 no tiene ningún paso a mano. Se descartó dejarlo a mano con la guía de la etapa 5.
6. **Revisión legal hecha.** El usuario confirmó el 2026-11-09 que un contador o un abogado revisó el modo 3 y que se
   puede ofrecer. Igual queda apagado por defecto en cada instalación (`Arca:Robot:Enabled`) y se prende donde se ofrezca.

Tableros RB1 a RB3, con A y B, en el lienzo provisorio
[Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), en las filas «ARCA · robot». Como en las
etapas anteriores, el diseño elegido se registra en `docs/design` del front antes de programar el front.

**Resueltas por defecto** (el plan las fija y las reglas del repo alcanzan):

- El robot es un worker dentro de la Api, no un host aparte.
- La clave fiscal de la plataforma es un secreto de la instalación.
- Sin capturas ni trazas de Playwright, ni siquiera para diagnosticar una pantalla cambiada: el aviso dice el paso.
- Cada paso mira si ya está hecho antes de actuar.

## Qué reusa de la etapa 5

Con los nombres del [plan de la etapa 5](2026-11-09-arca-etapa-5-delegacion-guiada.md); si cambian al programarse, vale
lo que commitee.

| Pieza de la etapa 5 | Para qué la usa el robot |
| --- | --- |
| `Arca:Platform:Cuit` y `Arca:Platform:Name` | La CUIT en la que el cliente delega y el nombre que muestra la casilla |
| El alta en modo delegación (`StartDelegated`) y `ArcaConnection.PlatformCuit` | El modo 3 usa el mismo modelo, con `PlatformDelegationAutomated` |
| El certificado de la plataforma dentro de la conexión, con su pedido y su carga | El robot sube el pedido a ARCA, descarga el certificado y lo carga por el mismo camino que la persona |
| La detección en el proveedor de tickets (`NotDelegated`) | Confirma que el trámite terminó: el robot no da por buena su propia lectura de la pantalla |
| La guía, con pasos `company.delegate.<servicio>`, `company.point-of-sale`, `platform.accept` y `platform.assign` | Es la vuelta cuando el robot se frena; se abre en el paso donde quedó |

## Recorrido

### Del lado del cliente

1. En «Datos», donde la etapa 5 ofrece cómo conectarse, aparece «Delegación automática» sólo si el robot está prendido en
   la instalación. Al seguir, la app genera el pedido del certificado de la plataforma (como en la etapa 5). Cuando el
   cliente pone su clave, el robot empieza los dos lados a la vez: el certificado no espera a que el cliente delegue.
2. **Consentimiento e ingreso** (RB1-A): «Entrás con», la clave fiscal y la casilla. Si quien entra no es la empresa, el
   robot elige la empresa en «Representar a». El campo de la clave se vacía al enviar.
3. **Avance** (RB2-A): los pasos con su estado, hasta que la detección de la etapa 5 confirma la delegación. Se puede
   salir y volver: el avance queda guardado, la clave no.
4. **Punto de venta:** el que creó el robot llega elegido en la zona de Facturación; la prueba y la activación siguen
   como siempre.
5. **Si el robot se frena** (RB3-A): la guía de la etapa 5 en el paso donde quedó.

### Del lado de la plataforma

Un trabajo durable, sin secretos guardados, que toma cualquier host con una reserva (dueño y vencimiento, como el ticket
de acceso). Entra con la clave fiscal de la plataforma y hace, en orden:

1. **El certificado** (RB5-A): crea el alias con el pedido, descarga el certificado y lo carga en la conexión. La carga
   valida como siempre (la clave generada, la CUIT de la plataforma, el ambiente y el vencimiento).
2. **Aceptar la delegación**, cuando el lado del cliente terminó.
3. **Asignarla al computador fiscal**, el alias del paso 1.

- La clave de la plataforma es un **secreto de la instalación** (user-secrets en desarrollo, Key Vault o variables
  protegidas al desplegar). Nunca va a la base.
- **No reintenta un ingreso rechazado:** ARCA bloquea la clave después de varios intentos, y la de la plataforma es una
  sola para todas sus instalaciones. Un rechazo frena el trabajo y avisa.
- Si ARCA no responde, reintenta con espera creciente unas pocas veces. Si la pantalla cambió o aparece un captcha, frena
  y avisa: el dueño hace ese paso a mano con la etapa 5 (las zonas del certificado o la guía de la plataforma), y la
  detección lo encuentra igual.
- Mientras tanto, el cliente ve «Falta que la plataforma acepte» con «Comprobar». No vuelve a su guía, porque esos pasos
  no los puede hacer él.

## El robot

### Dónde corre

- Es un **worker del módulo dentro de la Api**, en `Infrastructure/Modules/Arca/`, como los demás workers del repo. No hay
  un host aparte: la clave del cliente llega en el pedido HTTP y se usa en el mismo proceso, sin viajar por la red interna
  ni por una cola.
- **El lado del cliente corre en el host que recibió el pedido,** con un canal en memoria. Su avance se guarda en la base
  (sin secretos) para que cualquier host lo lea. La corrida tiene una reserva con vencimiento: si el host se reinicia, la
  reserva vence y la corrida queda interrumpida; el cliente vuelve a poner la clave o sigue con la guía.
- **Una corrida por conexión a la vez,** con el lock de la conexión, y un límite por operador (`arca-remote`).
- **El lado de la plataforma es durable** en la base, porque su clave es configuración y no depende del cliente.

### Pasos y motivos

Cada pantalla de ARCA tiene un objeto de página con sus selectores en un solo lugar. Antes de actuar, cada paso **mira si
ya está hecho** (el alias ya existe, la relación ya está, el punto de venta ya se dio de alta), así que repetir una corrida
después de una falla no duplica nada en ARCA. Los nombres de los pasos son los de la guía de la etapa 5, más los propios
del robot:

| Lado | Paso | Clave |
| --- | --- | --- |
| Cliente | Entrar con la clave fiscal y, si hace falta, elegir la empresa | `company.login` |
| Cliente | Delegar cada web service elegido en la CUIT de la plataforma | `company.delegate.<servicio>` |
| Cliente | Dar de alta el punto de venta de web service (si Facturación está elegida), con el sistema «Web Services» que ARCA ofrezca | `company.point-of-sale` |
| Plataforma | Entrar con la clave de la plataforma | `platform.login` |
| Plataforma | Crear el alias con el pedido, descargar el certificado y cargarlo | `platform.certificate` |
| Plataforma | Aceptar la delegación de esa CUIT | `platform.accept` |
| Plataforma | Asignarla al computador fiscal de la instalación | `platform.assign` |
| Los dos | Confirmar con la detección de la etapa 5 | `verify` |

El punto de venta lo aporta la familia de Facturación, como aporta su prueba con `IArcaServiceDefinition`
([ADR 0025](../decisions/0025-familias-de-servicios-arca.md)): `Connection` no la nombra.

Una falla se traduce a un motivo estable, como las fallas del WSAA en la etapa 1:

| Motivo | Qué pasa |
| --- | --- |
| Ingreso rechazado | CUIT o clave incorrecta. No se reintenta; el cliente la corrige o sigue con la guía |
| Captcha | El robot no lo resuelve: vuelta a la guía |
| Validación extra | Un segundo factor, un aviso que pide aceptar algo o un cambio de clave obligatorio: vuelta a la guía |
| Pantalla cambiada | No encontró lo que esperaba, con el paso: vuelta a la guía y aviso al dueño |
| ARCA no responde | Plazo vencido o error del servidor: el cliente reintenta o sigue con la guía |
| Interrumpida | El host se reinició en medio de la corrida |

### La clave fiscal

El detalle va en el [ADR 0026](../decisions/0026-robot-de-delegacion-arca.md); en corto:

- La clave del cliente es un **tipo propio** que no se imprime (`ToString` oculto) ni se serializa, desde el contrato
  HTTP hasta el robot. Se usa para llenar el campo de ingreso y se suelta apenas ARCA acepta el ingreso; después el robot
  sigue con la sesión del navegador.
- **Nada la registra:** sin trazas, HAR, video ni capturas de Playwright; las excepciones de Playwright se traducen a un
  motivo sin su mensaje, que puede repetir lo que se escribió; la ruta queda fuera de cualquier registro del cuerpo del
  pedido.
- **El navegador es efímero:** un contexto nuevo por corrida, sin perfil persistente, que se cierra al terminar, salga
  bien o mal, con un plazo total.
- **No va a la cola ni al trabajo de la plataforma:** ese usa la clave de la plataforma.
- .NET no garantiza borrar un `string` de la memoria. Lo que se promete es que la clave no se escribe en ningún lado y vive
  lo menos posible; un volcado de memoria del proceso podría tenerla, y los volcados no se habilitan en producción.

### El aviso cuando se rompe (RB4-A)

- Un correo a `Arca:Robot:AlertEmail`, por la cola de correos de siempre, con el paso, el motivo y la hora. Sin la CUIT
  del cliente ni la de quien entró.
- Como mucho uno por motivo y paso por día. Sin dirección configurada, sólo queda el log.
- Siempre, un log de error con el paso y el motivo, sin datos de la persona.
- Avisa una pantalla cambiada (de los dos lados), un captcha o una validación extra del lado de la plataforma y un ingreso
  rechazado de la plataforma. Un ingreso rechazado del cliente no avisa: es un error de quien escribió la clave.

### Configuración

- `Arca:Robot:Enabled`, apagado por defecto. Con el robot apagado, «Delegación automática» no aparece y el worker no
  arranca.
- Las direcciones de las páginas de ARCA (ingreso, Administrador de Relaciones, certificados y puntos de venta) son
  configuración, como los endpoints de la etapa 1: las pruebas las apuntan a páginas simuladas.
- Los plazos de cada paso y de la corrida entera, y los reintentos del lado de la plataforma.
- `Arca:Robot:Platform` con la CUIT de ingreso y la clave fiscal de la plataforma, como secretos; `Arca:Robot:AlertEmail`.
- Con el robot prendido y sin Chromium instalado, la Api no arranca: es una falla del despliegue y conviene verla al
  publicar.

### Despliegue

- La imagen de la Api se genera con el SDK de .NET, sin Dockerfile ([azure-dev](../deploy/azure-dev.md)). Para el robot
  hace falta una imagen base con Chromium y sus dependencias del sistema, de la misma versión que pide el paquete de
  Playwright. La guía del robot explica cómo generarla y cómo elegirla al publicar.
- Mientras el robot esté apagado, la imagen de siempre alcanza.

## Datos

| Tabla | Qué guarda |
| --- | --- |
| `ArcaDelegationRuns` (nombre indicativo) | Conexión, lado (cliente o plataforma), estado, paso actual, pasos hechos, motivo de la falla, intentos, reserva (dueño y vencimiento), cuándo se avisó y horas. Sin clave, sin la CUIT o CUIL de quien entró y sin capturas |

Desconectar la borra con la conexión, en cascada, como las demás tablas del módulo. El consentimiento se guarda como
hecho (quién y cuándo) en la corrida del cliente; la clave no.

## Contrato HTTP (indicativo)

Bajo `api/arca/connection`, con `arca.read` y `arca.manage`:

| Verbo y ruta | Qué hace |
| --- | --- |
| `POST …/robot-runs` | Con el consentimiento, la CUIT o CUIL de ingreso, la clave fiscal y la revisión esperada, arranca la corrida del cliente. Responde 202 con la corrida |
| `GET …/robot-runs/current` | El estado de la corrida del cliente y del trabajo de la plataforma, con los pasos y el motivo |

- El contrato de entrada sobrescribe `ToString()` y oculta la CUIT y la clave.
- Sin el robot prendido, el `POST` responde con un error propio (`Arca.Connection.RobotDisabled`).
- `GET …/robot-runs/current` dice también si el robot está prendido (`enabled`), para ofrecer el modo.

## Front

Espera a que el usuario termine la prueba integral final: mientras dure, nadie edita el front. Va en la pantalla de ARCA,
con RB1-A, RB2-A y RB3-A registrados antes en `docs/design` del front. El avance se consulta cada dos segundos mientras la
corrida está en curso.

## Pruebas

- **Unitarias:** el ciclo de la corrida, la traducción de fallas a motivos, la decisión de «ya está hecho» de cada paso,
  el límite de un aviso por día y que la clave no se imprima ni se serialice.
- **El robot contra páginas simuladas:** páginas HTML que imitan las de ARCA (ingreso, ingreso rechazado, captcha,
  validación extra, certificados, Administrador de Relaciones, nueva relación, aceptación y puntos de venta), servidas por
  un Kestrel de prueba y recorridas con Chromium sin ventana. Cubren el recorrido completo, cada motivo y la repetición
  sin duplicar. Van en IntegrationTests, que ya piden Docker; el workflow instala Chromium.
- **La clave no queda en ningún lado:** una corrida con una clave conocida y después la búsqueda de esa clave en los logs
  capturados, las excepciones, las filas de la base y la cola de correos.
- **De integración:** el contrato HTTP con un robot falso, sin Chromium; la corrida interrumpida cuando vence la reserva;
  el trabajo de la plataforma con dos hosts; el robot apagado; el aviso por correo.
- **La corrida real** la hace el usuario en producción, con sus cuentas: el Administrador de Relaciones existe sólo en
  producción. Nadie más ingresa claves ni maneja su navegador.

## Cortes

1. **Diseño:** este plan, el ADR 0026, los tableros RB1 a RB3 y las decisiones. Hecho el 2026-11-09.
2. **Backend, la corrida:** el modelo, el servicio, el contrato HTTP y el trabajo de la plataforma. Hecho el 2026-11-09,
   después del backend de la etapa 5 (aec47d2d).
3. **Backend, el robot:** Playwright contra las páginas simuladas, de los dos lados, y el aviso. Hecho el 2026-11-09.
4. **Despliegue:** Chromium en el workflow y la receta de la imagen en la guía del robot. Hecho el 2026-11-09; la receta se
   prueba la primera vez que una instalación prenda el robot.
5. **Front,** después de la prueba integral y del front de la etapa 5.
6. **Cierre:** la verificación de cierre de `AGENTS.md`, `features/arca.md` al día, la fila de la etapa 6 en la hoja de
   ruta, la auditoría que retira este plan y el traspaso al multitenant ofrecido.

## Riesgos

| Riesgo | Cómo se cubre |
| --- | --- |
| Las pantallas de ARCA cambian | Selectores en un solo lugar, motivo «Pantalla cambiada» con el paso, aviso y vuelta a la guía |
| Sin homologación | Páginas simuladas en las pruebas; la corrida real la hace el usuario |
| La clave fiscal es personal e intransferible | Revisado por un contador o un abogado (confirmado el 2026-11-09); consentimiento explícito; la clave no se guarda; apagado por defecto |
| La clave de la plataforma se bloquea | Un ingreso rechazado no se reintenta y frena el trabajo de la plataforma |
| Chromium en el despliegue | Imagen base propia, y la Api no arranca con el robot prendido y sin Chromium |
| La clave queda en un log o una excepción | Tipo propio, sin trazas de Playwright, excepciones traducidas y una prueba que la busca |

## Fuera de la etapa

- Crear con el robot el certificado de la empresa (modo 1).
- Resolver captchas o pedir el segundo factor al cliente en medio de la corrida.
- Pausar el robot para todas las instalaciones cuando se rompe: con una instalación por cliente, la delegación se hace una
  vez; vuelve a evaluarse en el multitenant.
- Revocar la delegación desde la app.
