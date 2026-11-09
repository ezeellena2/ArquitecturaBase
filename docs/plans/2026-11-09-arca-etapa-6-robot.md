# ARCA · Etapa 6: delegación con robot

Plan del 2026-11-09, sin decidir ni programar. Es la etapa 6 de la
[hoja de ruta de ARCA](2026-10-31-arca-formularios-estandar.md) y el modo 3 de acceso: un robot propio, con Playwright
para .NET, hace en la página de ARCA lo que en la etapa 5 la persona hace a mano. El cliente entra una sola vez con su
clave fiscal; ni él ni el dueño de la plataforma tienen que entrar después a ARCA para delegar, aceptar o asignar.

Depende de la **etapa 5 (delegación guiada)**, que otro chat diseña y programa a la vez. El robot reusa tres cosas de
ella: el certificado de la CUIT de la plataforma en cada instalación, la detección de que la delegación llegó y la guía,
que es la vuelta cuando el robot no puede seguir. Este plan nombra esas piezas por lo que hacen; los nombres de clases,
rutas y pantallas se toman de la etapa 5 cuando esté commiteada.

La decisión estructural está en el [ADR 0026](../decisions/0026-robot-de-delegacion-arca.md), propuesto el 2026-11-09,
que la hoja de ruta y el [ADR 0023](../decisions/0023-arca-como-modulo-opcional.md) piden antes de programar: el robot
sobre la página de ARCA y cómo se protege la clave fiscal en memoria.

## Alcance

- **Del lado del cliente, una sola vez y con su clave fiscal:** el cliente da un consentimiento explícito. El robot entra a
  ARCA, hace «Nueva relación» en el Administrador de Relaciones para delegar cada web service elegido en la CUIT de la
  plataforma y da de alta el punto de venta de web service. La clave vive sólo en memoria y nunca va a la base, a los logs
  ni a la cola.
- **Del lado de la plataforma, con su clave fiscal:** el robot acepta la delegación y la asigna al computador fiscal de la
  instalación, el alias del certificado de plataforma de la etapa 5.
- **Si algo falla** (una pantalla cambiada, un captcha o una validación extra), el cliente vuelve a la guía de la etapa 5
  en el paso donde se frenó el robot. El robot no resuelve captchas.
- **Un aviso** al dueño de la plataforma cuando el robot se rompe.
- **Chromium en la imagen** que corre el robot.
- **Apagado por configuración** hasta que un contador o un abogado confirme que se puede ofrecer (ver «Revisión legal»).

## Qué reusa de la etapa 5

| Pieza de la etapa 5 | Para qué la usa el robot |
| --- | --- |
| La conexión en modo delegación, con la CUIT representada | El robot la crea o la completa en el modo 3, con el mismo modelo |
| El certificado de la CUIT de la plataforma y su alias | El alias es el computador fiscal al que el robot asigna la delegación |
| La detección de la delegación (la CUIT en `relations` del ticket de acceso) | Confirma que el trámite terminó de verdad; el robot no da por buena su propia lectura de la pantalla |
| La guía paso a paso | Es la vuelta cuando el robot se frena; se abre en el paso donde quedó |

Si la etapa 5 cambia algo de esto, el plan se ajusta a lo que commitee; no se arma una versión paralela.

## Recorrido

### Del lado del cliente

1. En la pantalla de ARCA, donde la etapa 5 ofrece cómo conectarse, aparece «Delegación automática» sólo si el robot está
   prendido en la instalación (`Arca:Robot:Enabled`).
2. **Consentimiento** (tablero RB1): qué va a hacer el robot, en pasos; qué pasa con la clave (se usa una vez, no se
   guarda); y una casilla explícita. Sin la casilla, el botón queda apagado con su motivo.
3. **Ingreso:** la CUIT o CUIL con la que se entra a ARCA y la clave fiscal. Si quien entra no es la empresa (una persona
   que administra sus relaciones), el robot elige la empresa en «Representar a». El campo de la clave se vacía al enviar.
4. **Avance** (tablero RB2): los pasos con su estado, hasta que la detección de la etapa 5 confirma la delegación.
5. **Punto de venta:** el que creó el robot llega elegido en la zona de Facturación de la etapa 1; la prueba y la
   activación siguen como siempre.
6. **Si el robot se frena** (tablero RB3): la guía de la etapa 5, abierta en el paso donde quedó, con lo que ya hizo
   marcado como hecho y el motivo en una línea.

### Del lado de la plataforma

- Cuando el lado del cliente termina, queda una **aceptación pendiente** guardada, sin secretos. Un worker la toma con una
  reserva (dueño y vencimiento, como el ticket de acceso), entra con la clave fiscal de la plataforma, acepta la delegación
  de esa CUIT y la asigna al computador fiscal de la instalación.
- La clave de la plataforma es un **secreto de la instalación** (user-secrets en desarrollo, Key Vault o variables
  protegidas al desplegar), como los demás secretos del repo. Nunca va a la base.
- **No reintenta un ingreso rechazado:** ARCA bloquea la clave después de varios intentos fallidos, y la clave de la
  plataforma es una sola para todas sus instalaciones. Un rechazo frena las aceptaciones y avisa.
- Si ARCA no responde, reintenta con espera creciente unas pocas veces. Si la pantalla cambió o aparece un captcha, frena
  y avisa: el dueño acepta a mano con la guía de la plataforma de la etapa 5 y la detección lo encuentra igual.
- Mientras tanto, el cliente ve «Falta que la plataforma acepte» con la comprobación de la etapa 5. No vuelve a su guía,
  porque ese paso no lo puede hacer él.

## El robot

### Dónde corre

- Es un **worker del módulo dentro de la Api**, en `Infrastructure/Modules/Arca/`, como los demás workers del repo. No hay
  un host aparte: la clave del cliente llega en el pedido HTTP y se usa en el mismo proceso, sin viajar por la red interna
  ni por una cola.
- **El lado del cliente corre en el host que recibió el pedido,** con un canal en memoria. Su avance se guarda en la base
  (sin secretos) para que cualquier host lo lea. La corrida tiene una reserva con vencimiento: si el host se reinicia, la
  reserva vence y la corrida queda «interrumpida»; el cliente vuelve a poner la clave o sigue con la guía.
- **Una corrida por conexión a la vez,** con el lock de la conexión, y un límite por operador (`arca-remote`).
- **El lado de la plataforma es una aceptación durable** en la base, porque su clave es configuración y no depende del
  cliente: sobrevive a un reinicio y la toma cualquier host con la reserva.

### Pasos y motivos

Cada pantalla de ARCA tiene un objeto de página con sus selectores en un solo lugar. Antes de actuar, cada paso **mira si
ya está hecho** (la relación ya existe, el punto de venta ya está dado de alta), así que repetir una corrida después de una
falla no duplica nada en ARCA.

| Lado | Paso |
| --- | --- |
| Cliente | Entrar con la clave fiscal y, si hace falta, elegir la empresa |
| Cliente | Delegar cada web service elegido en la CUIT de la plataforma |
| Cliente | Dar de alta el punto de venta de web service (si Facturación está elegida), con el sistema que corresponde a su condición frente al IVA |
| Plataforma | Entrar con la clave de la plataforma |
| Plataforma | Aceptar la delegación de esa CUIT |
| Plataforma | Asignarla al computador fiscal de la instalación |
| Los dos | Confirmar con la detección de la etapa 5 |

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
- **No va a la cola ni a la aceptación pendiente:** el lado de la plataforma usa la clave de la plataforma.
- .NET no garantiza borrar un `string` de la memoria. Lo que se promete es que la clave no se escribe en ningún lado y vive
  lo menos posible; un volcado de memoria del proceso podría tenerla, y los volcados no se habilitan en producción.

### El aviso cuando se rompe

Va al dueño de la plataforma, no al cliente: el cliente ya tiene la guía. Por dónde y cada cuánto lo decide el usuario
(RB4). En todos los casos queda un log de error con el paso y el motivo, sin datos de la persona.

### Configuración

- `Arca:Robot:Enabled`, apagado por defecto. Con el robot apagado, «Delegación automática» no aparece y el worker no
  arranca.
- Las direcciones de las páginas de ARCA (ingreso, Administrador de Relaciones, puntos de venta) son configuración, como
  los endpoints de la etapa 1: las pruebas las apuntan a páginas simuladas.
- Los plazos de cada paso y de la corrida entera, y los reintentos del lado de la plataforma.
- La CUIT y la clave fiscal de la plataforma, como secretos.
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
| `ArcaDelegationRuns` (nombre indicativo) | Conexión, lado (cliente o plataforma), estado, paso actual, pasos hechos, motivo de la falla, intentos, reserva (dueño y vencimiento) y horas. Sin clave, sin la CUIT o CUIL de quien entró y sin capturas |

Desconectar la borra con la conexión, en cascada, como las demás tablas del módulo. El consentimiento se guarda como
hecho (quién y cuándo) en la corrida del cliente; la clave no.

## Contrato HTTP (indicativo)

Bajo la ruta de delegación que fije la etapa 5, con `arca.read` y `arca.manage`:

| Verbo y ruta | Qué hace |
| --- | --- |
| `POST …/delegation/robot-runs` | Con el consentimiento, la CUIT o CUIL de ingreso, la clave fiscal y la revisión esperada, arranca la corrida del cliente. Responde 202 con la corrida |
| `GET …/delegation/robot-runs/current` | El estado de la corrida actual y de la aceptación pendiente, con los pasos y el motivo |

- El contrato de entrada sobrescribe `ToString()` y oculta la CUIT y la clave.
- Sin el robot prendido, el `POST` responde con un error propio (`Arca.Connection.RobotDisabled`).

## Front

Espera a que el usuario termine la prueba integral final: mientras dure, nadie edita el front. Va en la pantalla de ARCA,
dentro de la zona de delegación de la etapa 5, con el diseño elegido en los tableros y registrado en `docs/design` del
front. El avance se consulta cada dos segundos mientras la corrida está en curso.

## Pruebas

- **Unitarias:** el ciclo de la corrida, la traducción de fallas a motivos, la decisión de «ya está hecho» de cada paso y
  que la clave no se imprima ni se serialice.
- **El robot contra páginas simuladas:** páginas HTML que imitan las de ARCA (ingreso, ingreso rechazado, captcha,
  validación extra, Administrador de Relaciones, nueva relación, aceptación y puntos de venta), servidas por un Kestrel de
  prueba y recorridas con Chromium sin ventana. Cubren el recorrido completo, cada motivo y la repetición sin duplicar.
  Van en IntegrationTests, que ya piden Docker; el workflow instala Chromium.
- **La clave no queda en ningún lado:** una corrida con una clave conocida y después la búsqueda de esa clave en los logs
  capturados, las excepciones, las filas de la base y la cola de correos.
- **De integración:** el contrato HTTP con un robot falso, sin Chromium; la corrida interrumpida cuando vence la reserva;
  la aceptación pendiente con dos hosts; el robot apagado.
- **La corrida real** la hace el usuario en producción, con sus cuentas: el Administrador de Relaciones existe sólo en
  producción. Nadie más ingresa claves ni maneja su navegador.

## Revisión legal

ARCA define la clave fiscal como personal e intransferible, y la hoja de ruta pide que un contador o un abogado revise el
modo 3 antes de ofrecerlo. Se programa igual, con `Arca:Robot:Enabled` apagado hasta que el usuario confirme esa revisión.
El consentimiento explícito y que la clave no se guarde no reemplazan la revisión.

## Decisiones para el usuario

De a una, con los tableros en el lienzo y la recomendada marcada:

- **RB1 · El consentimiento:** dónde y cómo se pide, junto con la clave.
- **RB2 · El avance del trámite:** cómo se ve mientras el robot trabaja y cuando espera a la plataforma.
- **RB3 · La vuelta a la guía:** cómo se pasa de la falla del robot a la guía de la etapa 5.
- **RB4 · El aviso cuando se rompe:** por dónde le llega al dueño de la plataforma y cada cuánto.
- **Revisión legal:** si ya consultó a un contador o un abogado.

**Resueltas por defecto** (el plan las fija y las reglas del repo alcanzan):

- El robot es un worker dentro de la Api, no un host aparte.
- La clave de la plataforma es un secreto de la instalación.
- Sin capturas ni trazas de Playwright, ni siquiera para diagnosticar una pantalla cambiada: el aviso dice el paso.
- Cada paso mira si ya está hecho antes de actuar.

## Cortes

1. **Diseño:** este plan, el ADR, los tableros RB1 a RB3 y las decisiones. Ahora.
2. **Backend, la corrida:** el modelo, el servicio, el contrato HTTP y la aceptación pendiente, con un robot falso. Arranca
   cuando la etapa 5 haya commiteado su modelo de delegación.
3. **Backend, el robot:** Playwright contra las páginas simuladas, de los dos lados, y el aviso.
4. **Despliegue:** la imagen con Chromium y la guía para correr y mantener el robot.
5. **Front,** después de la prueba integral.
6. **Cierre:** la verificación de cierre de `AGENTS.md`, `features/arca.md` al día, la fila de la etapa 6 en la hoja de
   ruta, la auditoría que retira este plan y el traspaso al multitenant ofrecido.

## Riesgos

| Riesgo | Cómo se cubre |
| --- | --- |
| Las pantallas de ARCA cambian | Selectores en un solo lugar, motivo «Pantalla cambiada» con el paso, aviso y vuelta a la guía |
| Sin homologación | Páginas simuladas en las pruebas; la corrida real la hace el usuario |
| La clave fiscal es personal e intransferible | Apagado hasta la revisión legal; consentimiento explícito; la clave no se guarda |
| La clave de la plataforma se bloquea | Un ingreso rechazado no se reintenta y frena las aceptaciones |
| Chromium en el despliegue | Imagen base propia, y la Api no arranca con el robot prendido y sin Chromium |
| La clave queda en un log o una excepción | Tipo propio, sin trazas de Playwright, excepciones traducidas y una prueba que la busca |

## Fuera de la etapa

- Crear con el robot el certificado de la plataforma (lo hace la etapa 5) o el de la empresa (modo 1).
- Resolver captchas o pedir el segundo factor al cliente en medio de la corrida.
- Pausar el robot para todas las instalaciones cuando se rompe: con una instalación por cliente, la delegación se hace una
  vez; vuelve a evaluarse en el multitenant.
- Revocar la delegación desde la app.
