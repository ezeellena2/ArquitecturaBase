# ARCA · Etapa 5: delegación guiada

Plan del 2026-11-09, decidido el mismo día (DG1-A, DG2-A y la constancia sin delegar); el backend se programa ya y el front,
después de la prueba integral. Es la etapa 5 de la [hoja de ruta de ARCA](2026-10-31-arca-formularios-estandar.md)
y el modo 2 de acceso: la empresa no genera un certificado propio, sino que delega sus web services en la CUIT de la
plataforma, que firma con su certificado y opera en nombre de la empresa. Sólo depende de la etapa 1
([regla](../features/arca.md)), cuyo modelo de conexión ya contempla los tres modos y sólo programó el certificado de la
empresa. La etapa 6 ([robot](2026-11-09-arca-etapa-6-robot.md)) se apoya en esta: reusa el certificado de la plataforma,
la guía y la detección, y vuelve a la guía cuando se frena.

## Cómo funciona la delegación en ARCA

Confirmado el 2026-11-09 con los documentos oficiales: [delegar un web service](https://www.arca.gob.ar/ws/WSAA/ADMINREL.DelegarWS.pdf),
la [guía de delegar y aceptar](https://www.afip.gob.ar/guias/paso-a-paso/Como-delego-servicios-para-que-utilicen-en-mi-nombre-como-se-acepta-delegacion.pdf)
(2026), el [manual del WSAA](https://www.afip.gob.ar/ws/WSAA/WSAAmanualDev.pdf), el
[manual de WSFEv1](https://www.afip.gob.ar/ws/documentacion/manuales/manual-desarrollador-ARCA-COMPG.pdf), el
[de la constancia de inscripción](https://www.arca.gob.ar/ws/WSCI/manual_ws_sr_ws_constancia_inscripcion.pdf) y el
[de WSASS](https://www.arca.gob.ar/ws/WSASS/WSASS_manual.pdf).

1. **La empresa delega,** con clave fiscal nivel 3: «Administrador de Relaciones de Clave Fiscal» › «Nueva Relación» ›
   ARCA › WebServices › «Facturacion Electronica», y en «Representante» la CUIT de la plataforma («CUIT/CUIL/CDI Usuario»;
   «Computador Fiscal» es sólo para los propios). Al confirmar, ARCA emite sola la constancia F.3283/E.
2. **La plataforma acepta,** con su clave fiscal: el servicio «Aceptación de Designación» › «Aceptar».
3. **La plataforma asigna** el servicio delegado a su computador fiscal: «Nueva Relación» con la empresa como
   representada, el servicio y el alias del certificado como representante. No puede volver a delegarlo en otra CUIT.
4. **El ticket lo dice:** según el manual del WSAA (6.3), las `relations` del token son «las CUITs autorizadas a actuar
   como representadas». WSFEv1 rechaza una CUIT que no está (600 o 601, «CUIT representada no incluida en token»). Que
   haga falta un ticket nuevo después de aceptar no está escrito, pero se deduce: la lista va adentro del token. El WSAA
   puede rechazar un ticket nuevo durante 2 minutos en producción y 10 en homologación (FAQ 10.6).
5. **El punto de venta lo da de alta la empresa:** «Administración de puntos de ventas y domicilios» › «A/B/M de puntos
   de ventas» › «Agregar». WSFEv1 sólo pide que sea de tipo RECE (error 10005). Los nombres exactos del «Sistema» no están
   en una fuente oficial; los de terceros son «RECE para aplicativo y Web Service» (responsable inscripto) y «Factura
   Electronica - Monotributo - Web Service».
6. **La constancia de inscripción no necesita delegación:** `cuitRepresentada` tiene que estar en las relaciones del token
   e `idPersona` es cualquier CUIT. La plataforma consulta a la empresa con su propia CUIT si autorizó el servicio para su
   alias.
7. **En homologación se puede simular:** en WSASS, «Crear Autorización a Servicio» acepta como «CUIT representado» cualquier
   persona, y la crea el dueño del certificado solo, sin aceptación.
8. **Revocar:** la empresa, en «Consultar» › «Representantes del Usuario» › «Revocar». No está escrito qué pasa con un
   ticket vigente; lo esperable es que el próximo ya no traiga la CUIT.
9. **Sin límites escritos:** un computador fiscal sirve para todos los web services, y no se documenta un tope de
   representados ni que la relación venza.

## Lo que fijan la hoja de ruta y el ADR 0023

- **La conexión guarda la CUIT representada y el certificado de la CUIT de la plataforma de esa instalación** (hoja de
  ruta, «Modos 2 y 3 en el modelo»). La delegación se detecta cuando la CUIT aparece en `relations` del ticket de acceso;
  puede hacer falta pedir un ticket nuevo, respetando el lapso.
- **Cada instalación usa su propio certificado de la CUIT de la plataforma,** porque ARCA no deja usar un certificado
  desde dos equipos a la vez (resuelta por defecto en la hoja de ruta).
- **Ese certificado sigue las reglas del de la empresa** ([ADR 0023](../decisions/0023-arca-como-modulo-opcional.md),
  punto 6): la clave se genera en el servidor, se cifra con el anillo `Arca:SecretProtection` y nunca sale del módulo.
- **En el multitenant** la conexión pasa a ser por organización, y el anillo y la CUIT de la plataforma siguen siendo por
  instalación (ADR 0023, consecuencias).

## Alcance

1. **El certificado de la CUIT de la plataforma** en la instalación: se genera, se guarda y se renueva como el de la
   empresa, con la CUIT de la plataforma en el pedido.
2. **La conexión en modo delegación:** la CUIT de la empresa es la representada, el certificado es el de la plataforma y
   el ticket de acceso va a nombre de la plataforma.
3. **La guía paso a paso,** con lo que hace la empresa en ARCA (delegar cada servicio elegido en la CUIT de la plataforma
   y dar de alta el punto de venta de web service) y lo que hace la plataforma (aceptar y asignar al alias de la
   instalación).
4. **La detección** de que la delegación llegó: la CUIT de la empresa en las relaciones del ticket.

Fuera: el robot (etapa 6), la emisión (etapas 3 y 4) y revocar la delegación desde la app.

## Diseño

### El certificado de la plataforma, dentro de la conexión

Como dice la hoja de ruta, la conexión en modo delegación guarda el certificado de la plataforma. No hay una cuenta de
plataforma aparte: en esta plantilla hay una conexión por instalación, así que un certificado por conexión es un
certificado de la plataforma por instalación.

- **El mismo `ArcaCertificate`,** con su pedido, su clave cifrada, su certificado, su renovación y sus tickets. Lo único
  que cambia es la CUIT del pedido y de la inspección: la de la plataforma en vez de la de la empresa.
- **La CUIT de la plataforma** es configuración de la instalación: `Arca:Platform:Cuit` y `Arca:Platform:Name` (el nombre
  que muestra la guía). Sin ella, el modo delegación no se ofrece. Se guarda en la conexión al empezar, porque el
  certificado queda atado a esa CUIT.
- **El alias** es el computador fiscal de la plataforma para esa instalación. La guía pide uno distinto por instalación:
  las relaciones de ARCA son por alias, y dos instalaciones con el mismo verían a las empresas de la otra.
- **Renovar** conviene con el mismo alias: en ARCA se agrega el certificado nuevo a ese alias y las delegaciones que ya
  tenía siguen asignadas.
- **Desconectar** borra la clave, como en el modo 1. El alias sigue en ARCA; para volver se genera otro pedido y se agrega
  el certificado nuevo a ese alias.

### La conexión en modo delegación

- `ArcaConnection` suma `PlatformCuit` (sólo en los modos de delegación) y el alta en modo delegación
  (`StartDelegated`). La CUIT de la conexión sigue siendo la de la empresa: es la que va en `Auth.Cuit` de WSFEv1, así que
  Facturación no cambia.
- **Cada servicio dice en nombre de quién actúa** (`ArcaInstalledService`): Facturación, en nombre de la empresa, y hay que
  delegarlo; la constancia de inscripción, como una consulta de quien firma, y no se delega. El certificado que reciben
  las familias trae las dos CUIT (la de la empresa y la de quien firma), y el padrón manda la de quien firma en
  `cuitRepresentada`. En el modo 1 las dos son la misma (decisión DG4).
- **Los estados no cambian:** esperando el certificado (de la plataforma) → sin verificar → activa ⇄ requiere atención.
  Mientras la delegación no llega, la conexión queda sin verificar y la prueba dice qué servicio falta.
- **Pasar de un modo al otro** es empezar de nuevo, como cambiar de ambiente.

### El ticket a nombre de la plataforma y la detección

- **El ticket es de la plataforma:** el certificado firma el pedido al WSAA y ARCA devuelve un ticket cuyo token trae, en
  `relations`, las CUIT que esa plataforma representa para el servicio.
- **La detección vive en `Connection`:** con una conexión en modo delegación, el ticket de un servicio que actúa en nombre
  de la empresa no se entrega si no la incluye. El proveedor de tickets lee las relaciones del token (Infrastructure
  conoce su formato) y `Connection` le dice qué CUIT tiene que estar, según el servicio. Así lo ven igual la prueba, las
  familias y, más adelante, la emisión.
- **Si el ticket guardado no incluye la CUIT,** se pide uno nuevo una vez (la delegación pudo llegar después). Si ARCA
  todavía no da otro, vale el bloqueo de la etapa 1: «ARCA no acepta otro pedido hasta las …».
- **Resultado nuevo de cada servicio: «sin delegar»** (`NotDelegated`), al lado de «sin autorizar». Con el modo
  delegación, «sin autorizar» quiere decir que el alias todavía no tiene el servicio asignado. Los dos los muestra la
  prueba con lo que falta hacer.
- **La prueba es la detección:** «Verificar la delegación» corre la prueba de siempre. No hay un proceso que mire solo.
- **Un token que no se puede leer** no frena: se deja que ARCA decida y queda un log de advertencia.

### La guía

Es texto del front con datos del servidor: la CUIT y el nombre de la plataforma, el alias, los servicios elegidos (con
en nombre de quién actúa cada uno) y el ambiente. Cada paso tiene un identificador estable para que el robot la abra
donde se frenó. En producción:

| Paso | Quién | Qué se hace en ARCA |
| --- | --- | --- |
| `platform.authorize.<servicio>` | La plataforma, una vez por alias, en la zona del certificado | En «Administrador de Relaciones de Clave Fiscal», a nombre de la plataforma, «Nueva Relación» con cada servicio de consulta (la constancia) y el alias como representante |
| `company.delegate.<servicio>` | La empresa, con su clave fiscal nivel 3 | «Administrador de Relaciones de Clave Fiscal» › «Nueva Relación» › ARCA › WebServices › el servicio, con la CUIT de la plataforma en «Representante» |
| `company.point-of-sale` | La empresa | «Administración de puntos de ventas y domicilios» › «A/B/M de puntos de ventas» › «Agregar», uno de web service según su condición frente al IVA |
| `platform.accept` | La plataforma, con su clave fiscal | «Aceptación de Designación» › «Aceptar» la delegación de la empresa |
| `platform.assign.<servicio>` | La plataforma | «Administrador de Relaciones», representando a la empresa, «Nueva Relación» con el servicio y el alias como representante |

En homologación hay un solo paso, de la plataforma: en WSASS, «Crear Autorización a Servicio» con el alias, la CUIT de la
empresa como «CUIT representado» y el servicio. La empresa no hace nada.

### Contrato HTTP

- `POST api/arca/connection/certificate-requests` suma `mode` (por defecto, el certificado de la empresa). En modo
  delegación pide la CUIT de la empresa, el ambiente, el alias y los servicios, y genera el pedido con la CUIT de la
  plataforma. Sin la plataforma configurada responde `Arca.Connection.PlatformNotConfigured`.
- `GET api/arca/connection` suma la plataforma configurada (CUIT y nombre, o nada) y, con una conexión en modo
  delegación, la CUIT de la plataforma que guardó.
- La prueba suma el estado `notDelegated` por servicio.

### Errores

- `Arca.Connection.PlatformNotConfigured`: el modo delegación sin la plataforma configurada.
- `Arca.Connection.ServiceNotDelegated`: una familia que llama a un servicio que la empresa no delegó (fuera de la prueba).

### Robustez y privacidad

- La detección sólo lee y no tiene efecto legal: alcanzan la revisión y la reserva del ticket de la etapa 1
  ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md)).
- La app nunca pide ni recibe la clave fiscal de la empresa ni la de la plataforma: los pasos en ARCA los hace cada uno.
- Los logs no llevan el token ni las CUIT de las relaciones; la CUIT va enmascarada.

## Decisiones del usuario (2026-11-09)

1. **DG1-A · El modo se elige en «Datos»,** con «Cómo se conecta», y las zonas siguen como en AC1-A: «Certificado de la
   plataforma» y una zona «Delegación» en lugar de «Prueba». Descartó DG1-B, dos tarjetas antes de las zonas.
2. **DG2-A · La guía en dos listas,** «Lo hace la empresa» y «Lo hace la plataforma», con «Verificar la delegación» en la
   banda y cada servicio debajo. Descartó DG2-B, un renglón por servicio.
3. **DG4 · La constancia de inscripción la consulta la plataforma con su CUIT,** sin delegarla. La empresa delega sólo los
   servicios que actúan en su nombre (hoy, Facturación). Descartó que la empresa delegue también la constancia.

**Resueltas por defecto** (la hoja de ruta, el ADR 0023 y la regla de la opción más simple alcanzan):

- El certificado de la plataforma lo guarda la conexión; no hay una cuenta de plataforma aparte.
- Lo administra quien tiene `arca.manage`, en la misma pantalla. Los pasos en ARCA con la clave fiscal de la plataforma los
  hace quien la tiene; la app no la pide. No hay un permiso aparte.
- La CUIT y el nombre de la plataforma son configuración de la instalación.
- La detección es la prueba de siempre, con un botón; no hay un proceso que mire solo.
- Un alias por instalación, y renovar con el mismo alias.

## Tableros

En el lienzo provisorio [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), filas «ARCA ·
delegación», publicados el 2026-11-09:

| Tablero | Qué muestra |
| --- | --- |
| DG1-A · recomendada | «Cómo se conecta» en «Datos»; el certificado de la plataforma en las zonas 2 y 3; «Delegación» en lugar de «Prueba»; el punto de venta igual |
| DG1-B | Sin conexión, dos tarjetas para elegir el modo; elegido, las mismas zonas con «Cambiar el modo» arriba |
| DG2-A · recomendada | La zona «Delegación» con «Lo hace la empresa» y «Lo hace la plataforma», «Verificar la delegación» en la banda y cada servicio debajo; plegada con su resumen cuando llegó |
| DG2-B | Un renglón por servicio con su estado y lo que falta, que se achica a medida que llega |
| DG3 · sin opciones | La plataforma sin configurar (la opción apagada con su motivo, MA1-A), un ticket viejo con ARCA que no da otro, homologación con WSASS y la conexión activa con la delegación revocada |

## Pruebas

- **Unitarias:** el alta en modo delegación, la CUIT del pedido y de la inspección, la lectura de las relaciones del token
  y la renovación forzada del ticket.
- **De integración:** el ARCA simulado de la etapa 1 devuelve tokens con relaciones. Cubren el alta completa en modo
  delegación (pedido con la CUIT de la plataforma, certificado, prueba «sin delegar», la delegación que llega, la prueba en
  verde y el punto de venta), la plataforma sin configurar, la revocación y el contrato HTTP.
- **La prueba real** la hace el usuario en producción, con su clave fiscal y la de una empresa: el Administrador de
  Relaciones existe sólo en producción.

## Cortes

1. **Diseño:** este plan, el ADR 0027, los tableros y las decisiones.
2. **Backend:** el modelo, el alta en modo delegación, la detección en el proveedor de tickets y el contrato, con TDD.
3. **Front,** cuando el usuario termine la prueba integral: el diseño elegido en `docs/design` del front y la pantalla.
4. **Cierre:** la verificación de `AGENTS.md`, `features/arca.md` y `configurar-arca.md` al día, la fila de la etapa 5 en
   la hoja de ruta, la auditoría que retira este plan y el traspaso al multitenant ofrecido.
