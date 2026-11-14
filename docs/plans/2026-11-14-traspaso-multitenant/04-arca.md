# 04 · ARCA: etapas 3 a 6 y lo que sumó la etapa 1

La etapa 1 (conexión con el certificado de la empresa, padrón y punto de venta) está en el traspaso 6. La etapa 2, el
motor para documentos, vive en Formularios y la cuenta el 01. Las seis etapas tienen backend y front programados (del
2026-11-09 al 2026-11-11); ninguna está cerrada. Faltan la prueba integral, las homologaciones con la clave fiscal del
usuario, la corrida real del robot y el retiro de los planes.

Fuentes: la regla en `docs/features/arca.md`; la hoja de ruta `docs/plans/2026-10-31-arca-formularios-estandar.md` y un
plan por etapa (`docs/plans/2026-11-09-arca-etapa-3-factura-c.md` a `…-etapa-6-robot.md`); las guías
`docs/guides/configurar-arca.md`, `robot-arca.md` y `quitar-arca.md`; los ADR 0025 a 0028; y el diseño en
`ArquitecturaBaseFront/docs/design/arca-*.md`.

## 1. El módulo

ARCA es un módulo opcional (`Modules/Arca`) ordenado por familias de servicios, cada una quitable sola: `Connection`,
`Registry` (padrón) e `Invoicing` (facturación) (ADR 0025). Cada empresa elige qué servicios usa. El núcleo no nombra a
ARCA: Formularios se conecta por puertos neutrales (emitir, catálogos que mantiene un módulo, completar con un padrón, la
biblioteca y las acciones). Permisos: `arca.read`, `arca.manage` y `arca.issue` («Emitir comprobantes en ARCA»), que se
pide para resolver la revisión de una emisión y para anular una respuesta con factura; cargar la «Factura» no lo pide.

## 2. Etapa 3: la factura C (2026-11-09; ADR 0028)

- **Decisiones del usuario.**
  - EM1-A: una zona «Comprobante» arriba de todo en el detalle de la respuesta.
  - NC1-A: la respuesta queda anulada cuando ARCA autoriza la nota de crédito, no al confirmar.
  - EM2-A: por WhatsApp, el «¿La emito?» es un mensaje después del Flow; se descartó poner el total dentro del Flow.
  - Un cliente nuevo también se puede escribir al facturar, en la app y por WhatsApp («Otro: escribir la CUIT»), y el
    padrón lo completa antes del «¿La emito?». El usuario eligió esto en vez de la recomendada (sólo desde la app).
  - La fecha fuera de la ventana de ARCA frena la emisión, y la persona elige «Emitir con la fecha de hoy» o «No emitir».

  De la hoja de ruta, sin volver a preguntar: el resumen antes de enviar, la aprobación opcional por formulario, un punto
  de venta de web service exclusivo, la cola con reintentos y aviso, el PDF fiscal de diseño fijo con QR, un solo
  estándar «Factura» para la app y WhatsApp, la condición del receptor siempre, el tipo de comprobante lo decide el
  sistema y anular es una nota de crédito total.
- **La cola.** Una cola propia de `Invoicing`, con un candado por secuencia (ambiente, CUIT, punto de venta y tipo): nadie
  toma otro número mientras haya uno pendiente. El número y el hash se guardan **antes** de llamar a `FECAESolicitar`, y
  ningún pedido a ARCA va dentro de una transacción. Ante un corte, un error de ARCA o «Transacción activa», se reconcilia
  con `FECompConsultar`: si existe y coincide, queda autorizado; si no existe y el último es menor, se reintenta con el
  mismo número; si no coincide, queda «Para revisar» y frena esa secuencia. Espera creciente (30 s, 1, 2, 5, 10 y 30
  minutos, después cada hora), sin tope. Estados: en cola, enviando, autorizado, rechazado, incierto, para revisar y
  cancelado.
- **Importes.** El servidor recalcula con half-even; un total distinto del que mandó el formulario no se encola. Sin CUIT,
  consumidor final. La clase sale de una tabla: monotributista o exento emite C; responsable inscripto, A o B (§3).
- **El «¿La emito?».** En la app, «Enviar» abre «¿Emitimos la factura?» con «Emitir factura» y «Volver», y avisa «Una vez
  emitida, sólo se anula con una nota de crédito.». Por WhatsApp, después del Flow llega «Factura C para … por $ …, con
  fecha ….» con «Emitir» y «No emitir». «No emitir» anula la respuesta sin ir a ARCA. Decide sólo quien cargó. Si la vista
  previa falla, el canal manda «Todavía no puedo emitirla: <motivo>» con «Volver a probar».
- **La zona «Comprobante».** Emitiendo; en cola («Todavía no se pudo emitir · Volvemos a probar a las 10:45»); emitida;
  rechazada, con el código y el primer mensaje de ARCA; y para revisar, que resuelve una persona con «Emitir con el número
  siguiente» o «Emitir con la fecha de hoy», o «No emitir». Si ARCA no responde, a quien cargó le llega una sola vez «ARCA
  no responde. Tu factura sale apenas vuelva.».
- **El PDF fiscal.** De diseño fijo, con QR según la especificación de ARCA, dibujado por el worker y guardado una vez en el
  almacén privado. Si el almacén falla, el comprobante queda autorizado con el PDF pendiente y nunca se reemite por eso.
- **El estándar «Factura»** (prefijo FAC, en la biblioteca, 01 §2): Cliente, Concepto, Fecha, el período y el vencimiento
  si es servicio, el bloque de ítems (descripción, cantidad, precio, alícuota e importe calculado), el total calculado y
  «Mandar copia al cliente». Trae el catálogo «Clientes» y la regla que emite, avisa a quien cargó con el PDF fiscal y
  manda la copia por correo; con aprobación, se pasa a «Cuando se aprueba». Un cliente escrito por su CUIT se suma a
  «Clientes» al emitir.
- **Anular.** Una respuesta con emisión viva no se corrige: se anula y se carga otra. Lo emitido se revierte con una nota de
  crédito por el total; la respuesta dice «Anulándose» hasta que ARCA la autoriza. La nota de crédito parcial queda para
  después.
- **Plazo normativo:** desde el 2026-12-01 ARCA rechaza lo que no informe la condición frente al IVA del receptor (RG
  5616); el estándar la informa siempre.
- **Commits:** backend `8a81ab4b`, `b6fdae26`, `1b9ece7a`, `61c466ca`, `bbda01fa`, `2315d5ba`, `054e419d`, `81ccfaae`,
  `a61cfaab` y `1af1678d`; front `0815da57`, `18e31307`, `9da76575` y `c34630f6`. Migración `AddArcaVouchers`.

## 3. Etapa 4: la factura A y B (2026-11-09)

- **Decisiones del usuario.** Cada formulario elige si sus precios incluyen IVA; exento y no gravado son opciones de la
  alícuota de cada ítem; y todas las pruebas van en homologación, con su CUIT responsable inscripta (el piloto en
  producción salió de la etapa). IV1-A: la alícuota en cada ítem, con el IVA discriminado una vez en el total. IV2-A: la
  casilla «El precio incluye IVA» en «Precio unitario».
- **Cómo quedó.** Con emisor responsable inscripto, A si el receptor es responsable inscripto o monotributista y B si no;
  la A exige CUIT. Un ítem sin alícuota vale 21 %. El IVA se calcula por alícuota, con half-even, y va en el array `Iva`.
  El «¿La emito?» dice «Factura A» o «Factura B» y discrimina los importes; por WhatsApp, entre paréntesis después del
  total. El PDF de la A discrimina el IVA y, a un monotributista, suma la leyenda de la RG 5003; el de la B, a consumidor
  final, suma el recuadro de la RG 5614 (Ley 27.743). Las notas de crédito A y B anulan igual que la C.
- **Fuera de la etapa:** A con leyenda, MiPyME, tributos, otras monedas, exportación, notas de débito y nota de crédito
  parcial. El plan tiene las normas confirmadas con fuentes oficiales y nueve pasos de prueba a mano.
- **Commits:** backend `33b631f3`, `38ebe5dc` y `890f04bf`; front `ba5e2fb0`, `43397c5d` y `9fb8a70f`.

## 4. Etapa 5: la delegación guiada (2026-11-09; ADR 0027)

- **Qué es.** El modo 2 de acceso: la empresa delega sus web services en la CUIT de la plataforma, que firma con su propio
  certificado. La app nunca pide ni recibe una clave fiscal en este modo.
- **Decisiones del usuario.** DG1-A: «Cómo se conecta» va en la zona «Datos». DG2-A: la guía en dos listas, «Lo hace la
  empresa» y «Lo hace la plataforma». La constancia de inscripción la consulta la plataforma con su CUIT, sin delegar.
- **Cómo quedó.** La CUIT y el nombre de la plataforma son configuración de la instalación (`Arca:Platform:Cuit` y `Name`);
  sin ellas, el modo aparece apagado con su motivo. El certificado de la plataforma lo guarda la conexión. Detectar es
  probar: un servicio cuyo ticket no incluye a la empresa queda «Sin delegar», y uno sin asignar, «Sin asignar al alias».
  La guía de producción tiene los pasos de cada lado («Nueva Relación» con la CUIT de la plataforma, el punto de venta,
  «Aceptación de Designación» y asignarlo al alias); en homologación alcanza un paso de la plataforma. Revocar lo hace la
  empresa en ARCA; la próxima prueba deja la conexión en «Requiere atención».
- **Commits:** backend `aec47d2d`, `6f49a04d` y `ad6a9685`; front `5b07d49d`. Migración `ArcaPlatformDelegation`.

## 5. Etapa 6: la delegación con robot (2026-11-09; ADR 0026)

- **Qué es.** El modo 3: un robot propio con Playwright para .NET, dentro de la Api, hace por el cliente los pasos de la
  delegación y por la plataforma los suyos. Se descartaron los servicios de terceros que reciben la clave fiscal.
- **Decisiones del usuario.** RB1-A: el consentimiento y el ingreso son una zona más del alta. RB2-A: el avance en pasos,
  en dos columnas («Con tu clave» y «Con la clave de {plataforma}»). RB3-A: si se frena, la guía de la etapa 5 abre en ese
  paso. RB4-A: un aviso por correo al dueño de la instalación, uno por motivo y paso por día. RB5-A: el robot también crea
  el certificado de la plataforma, así que el modo 3 no tiene pasos a mano. La revisión legal la confirmó el usuario; igual
  queda apagado por defecto en cada instalación.
- **El lado del cliente** es efímero: corre en el host que recibió la clave, que sólo vive en memoria y nunca viaja por la
  red interna, la cola ni la base. Entra a ARCA, delega cada servicio y da de alta el punto de venta. El consentimiento
  dice «Autorizo a usar mi clave fiscal una sola vez para delegar «Facturación electrónica» en {plataforma} (CUIT …) y dar
  de alta un punto de venta de web service.».
- **El lado de la plataforma** es durable, con la clave fiscal de la plataforma como secreto de la instalación: crea el
  certificado, acepta la delegación y la asigna. Un ingreso rechazado de la plataforma no se reintenta, porque esa clave es
  una sola: frena y avisa.
- **Lo que no hace:** no resuelve captchas ni pide un segundo factor; en esos casos el cliente sigue con la guía en el paso
  donde quedó. Sólo se ofrece en producción, porque el administrador de relaciones existe sólo ahí. Donde se prende, la
  imagen de la Api necesita Chromium.
- **Commits:** backend `94839a2e`, `4e644f01` y `f7e9fd79`; front `5afcd50e`, `cde6d0dd` y `dcf13351`. Migración
  `ArcaRobotRuns`.

## 6. Lo que sumó la etapa 1

- **Datos del comprobante (AC4, 2026-11-11).** Una zona «Facturación · Datos del comprobante» con ingresos brutos e inicio
  de actividades, que van en el PDF y el padrón no trae (backend `b79c4f35`, front `2dab5542`).
- **La configuración de Facturación** se guarda con revisión y 409, y valida antes de abrir la transacción y de consultar a
  ARCA (`9aa0ba96`).
- **La CUIT** se valida con la pieza común del núcleo, también usada por la lectura de documentos: once dígitos con
  verificador y nunca once ceros (`6c1aff65`).
- **El padrón de un tercero** («Completar con ARCA») tiene un cupo de 6 CUIT distintas por minuto y por persona y una caché
  de 10 minutos (`af67a8d2`).
- **Pantalla:** «Desconectar» pasó al ⋮, en rojo (AC1-A), y lo apagado dice por qué (MA1-A).

## 7. Para el multitenant

- **Qué es de la plataforma:** la CUIT y el nombre de la plataforma, su certificado, el ticket de acceso de cada servicio,
  la clave fiscal de la plataforma para el robot, el interruptor del robot y su correo de aviso, y los catálogos y
  estándares que aporta el módulo. El ADR 0027 dice que en el multitenant el certificado de la plataforma no puede vivir en
  cada conexión: hace falta **una cuenta de plataforma** con un ticket que sirva a todas las organizaciones, con su
  pantalla y su permiso de plataforma.
- **Qué es de cada empresa:** la conexión (CUIT, ambiente, modo, servicios, punto de venta, ingresos brutos, inicio de
  actividades), la delegación que hace su titular, la corrida del robot con su clave, y los comprobantes, las secuencias y
  la numeración por CUIT emisora, punto de venta y tipo. Comprobantes y secuencias tienen valor legal: no se borran al
  desconectar ni al quitar el módulo.
- **Encaja mejor allá que acá.** La delegación (modos 2 y 3) supone una plataforma que atiende a muchas empresas: es tu
  modelo. Acá «la plataforma» es la instalación.
- **Lo que tiene que decidir el usuario allá:**
  - la conexión por organización o por empresa (del traspaso 6; tus empresas tienen CUIT);
  - ARCA como módulo propio o dentro de tu `Integrations` (decisión 76);
  - las zonas plegables del alta (del traspaso 6);
  - la cuenta de plataforma única (ADR 0027);
  - qué hacer si se bloquea la clave de la plataforma, que frena a todas las organizaciones (pausar el robot, ADR 0026);
  - el robot en un host aparte o dentro de la Api (acá se descartó porque la clave viajaría por la red interna);
  - revocar desde la app, que acá quedó afuera y se vuelve a evaluar allá.
- **Robustez completa** en la emisión y el robot (valor legal y claves fiscales, ADR 0018); leer tablas del padrón o
  catálogos sólo necesita reserva y revisión.
