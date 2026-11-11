# ARCA · Etapa 3: factura C

Diseño del 2026-11-09, **decidido por el usuario el mismo día** (abajo) y programado en backend y front del
2026-11-09 al 2026-11-11 ([tareas](#tareas)); faltan la prueba integral y la homologación. Es la etapa 3 de la
[hoja de ruta de ARCA](2026-10-31-arca-formularios-estandar.md): emitir la factura C y su nota de crédito en homologación,
desde un formulario estándar «Factura» que se carga en la app y por WhatsApp. Se apoya en la [etapa 1](../features/arca.md)
(conexión, ticket, padrón y punto de venta) y en las piezas de la etapa 2, que se diseñan en otro chat: los cálculos, el
resultado externo en la respuesta, las carpetas y la biblioteca de estándares con preguntas atadas. El
[ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md) fija la cola, la numeración y la reconciliación.

**Tableros:** EM1, EM2, NC1 y CA1 en el lienzo provisorio
[Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), en las filas «ARCA · factura C». El diseño
elegido se registra en el front (`docs/design`) antes de programar el front, como en la etapa 1. El plan y el ADR entraron en
`main` dentro del commit 5bbad70c de la etapa 2, que se llevó lo que estaba preparado en el índice compartido.

## Lo que ya está decidido

De la hoja de ruta (2026-10-31) y sin volver a preguntar:

- La factura C primero, en homologación; A y B son la etapa 4, y la delegación, las etapas 5 y 6.
- **El tipo de comprobante lo decide el sistema** con la condición frente al IVA del emisor y del receptor (tabla «Qué
  comprobante corresponde» del [estudio](integraciones/estudio-2026-10-22.md#qué-comprobante-corresponde)).
- **Antes de emitir, la persona confirma un resumen** («¿La emito?»). La aprobación es opcional y la decide cada formulario
  en sus reglas.
- Un punto de venta de web service exclusivo de la plataforma.
- **Si ARCA no responde,** la emisión queda en cola con reintentos y la persona recibe un aviso.
- **El PDF fiscal** tiene un diseño fijo con QR, se genera al obtener el CAE y va separado del diseño libre de los
  formularios.
- **Anular** emite la nota de crédito de la misma clase por el total, con la factura en `CbtesAsoc`. La parcial queda para
  después. Una respuesta con factura emitida no se corrige ni se anula como las demás.
- **Las tablas de ARCA** que usa la factura llegan como catálogos de sólo lectura que se actualizan solos, en la carpeta
  «ARCA».
- **Las preguntas del estándar:** cliente (del catálogo «Clientes»; la CUIT completa la razón social y la condición frente al
  IVA desde el padrón, o «Consumidor final»), concepto, renglones, período y vencimiento del pago si es un servicio, fecha
  (por defecto hoy) y «Mandar copia al cliente».
- **La condición frente al IVA del receptor** va siempre, también en la C: desde el 1/12/2026 ARCA rechaza el pedido sin
  ella (RG 5616).
- **Fuera de esta vuelta:** facturar al cobrar, cargar por audio con IA, exportación, factura de crédito MiPyME, CAEA y
  otras monedas que no sean pesos.

## Lo que hay hoy

- **ARCA:** la conexión con el certificado de la empresa, el ticket compartido entre hosts, el padrón de la empresa
  (`getPersona_v2`) y el punto de venta (`FEParamGetPtosVenta`). `IArcaInvoicingClient` sólo tiene `FEDummy` y los puntos de
  venta; no hay emisión.
- **El motor de acciones de formularios** (`BusinessFormActionRun`, `FormActionBackgroundService`): reserva con lease de
  2 minutos y fence, efecto fuera de la transacción, confirmación bajo el fence, `Uncertain` para lo que no se confirmó y
  revisión humana por persona. Los tipos de acción son textos fijos (`RequestApproval`, `GeneratePdf`, `NotifyAuthor`,
  `NotifyRole`) en `FormRulePolicy`, `FormActionRunRecorder`, `FormActionEffectService` y el front. **No hay espera
  creciente:** un `Uncertain` nunca vuelve solo y un `Running` se vuelve a mirar cada 5 s.
- **El canal neutral de acciones** (`IStructuredRecordActionChannel`) es por persona y mensaje, deduplica por run y persona
  y adjunta un documento por su clave del almacén privado (`StructuredRecordActionDocument`). No lleva datos de una emisión.
- **No existen** en el código el resultado externo, la biblioteca de estándares, las preguntas atadas, las carpetas ni los
  catálogos de sólo lectura: son de la etapa 2 o de esta.
- **El manual de WSFEv1** es el 4.7 (1/9/2026). El campo `Reproceso` de la respuesta es «no operativo para esta versión»:
  reenviar un comprobante ya autorizado no devuelve su CAE, da un error de consecutividad. Por eso todo reintento pasa antes
  por `FECompConsultar`.

## Diseño

### La emisión es una cola de la familia `Invoicing`

El [ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md) da el porqué. En resumen: la numeración es por secuencia y
tiene que sobrevivir a cada acción, a los reinicios y a los hosts; la acción de formularios no puede ser la dueña.

- **`ArcaVoucher`** (comprobante), una fila por emisión pedida:
  - la clave del pedido (`RequestKey`, el run de la acción de formularios), única: pedir dos veces la misma emisión
    devuelve la misma fila;
  - clase y tipo (`Class` C y `Kind` factura o nota de crédito, que dan el código 11 o 13), CUIT emisora, punto de venta;
  - los datos fijados al pedir, en jsonb: receptor (tipo y número de documento, nombre, condición frente al IVA y
    domicilio), concepto, fecha pedida, período y vencimiento del pago, renglones, importes y, en la nota de crédito, el
    comprobante asociado;
  - `Number` (vacío hasta asignarlo) y el `DataHash` (SHA-256 de lo que se manda a ARCA, con el número);
  - estado: `Queued` → `Sending` → `Authorized`, o `Rejected`, `Uncertain`, `NeedsReview` o `Cancelled`;
  - CAE, su vencimiento, las observaciones de ARCA y la fecha con la que se emitió (`IssuedOn`);
  - el motivo estable de la última falla, los intentos, `NextAttemptAtUtc` y el estado del PDF fiscal.
- **`ArcaVoucherSequence`**, una fila por CUIT, punto de venta y tipo: la reserva (dueño y vencimiento), el fence y el
  comprobante pendiente (`PendingVoucherId`), el que tiene número y todavía no se sabe si ARCA lo autorizó.
- **El worker** (`ArcaVoucherBackgroundService`, en Infrastructure, con `Arca:Invoicing:Worker`) toma de a una secuencia
  con trabajo vencido y procesa su comprobante más viejo sin resolver. Así cada secuencia avanza en orden y nunca hay dos
  emisiones de la misma secuencia a la vez, aunque corran varios hosts.

### Numeración y reconciliación

El algoritmo de «Cómo evitar emitir dos veces» del estudio, con el candado en la secuencia:

1. Una transacción corta toma la reserva de la secuencia (dueño, vencimiento y fence+1). Si la tiene otro y no venció, se
   saltea.
2. Si la secuencia tiene un comprobante pendiente, se trabaja ese, aunque no sea el que se buscaba.
3. **Si el comprobante ya tiene número,** se reconcilia (paso 6).
4. **Si no tiene:** se pide el ticket de `wsfe`, se valida la fecha (ventana de abajo) y se llama a
   `FECompUltimoAutorizado`. Con N, una transacción corta guarda número N+1, el hash, el estado `Sending` y el pendiente de la
   secuencia, **sólo si el fence sigue siendo el propio**. Recién después se llama.
5. **`FECAESolicitar`** con `CbteDesde = CbteHasta = número`, fuera de toda transacción:
   - aprobado, con o sin observaciones: `Authorized` con CAE, vencimiento y observaciones; se libera el pendiente;
   - rechazado por validación (sin 10016): `Rejected` con los códigos de ARCA; el número queda libre y se libera el pendiente;
   - 10016, sin respuesta, corte, 500, 501 o 502 («Transacción activa»): `Uncertain`, con el pendiente puesto y un
     próximo intento con espera.
6. **Reconciliar** con `FECompConsultar(tipo, punto de venta, número)`:
   - existe y coincide (documento del receptor, importe total, fecha y concepto): `Authorized`, con el CAE consultado;
   - existe con otros datos: alguien usó ese número. `NeedsReview` (conflicto) y no se reintenta solo;
   - no existe y el último autorizado es menor que el número: no se emitió. Se vuelve a llamar con **el mismo número**;
   - no existe y el último autorizado es igual o mayor: `NeedsReview`;
   - sin respuesta: sigue `Uncertain` con espera.
7. Cada resultado se guarda en una transacción corta que exige el fence propio y libera la reserva.

Ningún otro comprobante de la secuencia toma número mientras haya un pendiente: si ARCA no lo emitió, el número N+1 sigue
siendo suyo. Un pendiente en `NeedsReview` frena la secuencia hasta que una persona lo resuelve. El número funciona como
clave de idempotencia natural.

### Cola, espera creciente y la ventana de la fecha

- **Espera creciente** entre intentos: 30 s, 1, 2, 5, 10 y 30 minutos, y después cada hora (configurable). Sin tope de
  intentos: el límite es la ventana de la fecha.
- **Sin ticket** (bloqueado, servicio sin autorizar, reloj desfasado) o con la conexión fuera de «activa», el comprobante
  sigue en cola con espera y el motivo a la vista. No toma número.
- **La ventana de `CbteFch`** (manual 4.7): con concepto 1 (productos), hasta 5 días antes o después de la fecha de
  generación y sin salirse del mes; con concepto 2 o 3, hasta 10 días. Las fechas son de la Argentina (UTC−3, sin horario de
  verano). Antes de tomar número se controla que la fecha pedida entre en la ventana de hoy. Si ya no entra, el comprobante
  va a `NeedsReview` con el motivo «La fecha quedó fuera de lo que acepta ARCA» y una persona elige «Emitir con la fecha de
  hoy» o «No emitir» (decisión 5).
- **El aviso a la persona:** la primera vez que un comprobante queda esperando por ARCA, la acción de formularios le manda a
  quien cargó un aviso fijo por el canal («ARCA no responde. Tu factura sale apenas vuelva.»). Uno solo por emisión.

### Importes y validaciones antes de emitir

- **El servidor recalcula** con la regla de importes de la etapa 4: importe de cada renglón = cantidad × precio
  unitario, redondeado a 2 decimales con half-even (`MidpointRounding.ToEven`, el redondeo de WSFEv1); el total es la suma.
  Si no coincide con el total que calculó el formulario (cálculos de la etapa 2), no se encola: es un error del sistema, no
  de la persona.
- **En la C:** `ImpNeto` = `ImpTotal` = la suma; `ImpTotConc`, `ImpOpEx`, `ImpIVA` e `ImpTrib` en 0, sin `Iva`. Moneda
  `PES` con cotización 1. Importes con 2 decimales y hasta 13 enteros.
- **Se valida:** el dígito de la CUIT del receptor (`CuitNumber`), la fecha, el período (el fin no antes del inicio) y el
  vencimiento del pago (no antes de la fecha) cuando el concepto es 2 o 3, que la clase corresponda a la condición del
  emisor y que la condición del receptor sea una de las de la clase (tabla de ARCA). Sin CUIT, consumidor final: `DocTipo`
  99 y `DocNro` 0, condición 5. Lo que ARCA rechace igual (por ejemplo, el tope de consumidor final sin identificar) vuelve
  como `Rejected` con su código.

### Tipo de comprobante

- **El reparto con la etapa 4** (su plan del 2026-11-09): esta etapa programa en `Domain/Modules/Arca/Invoicing/` la clase
  según el emisor y el receptor (`ArcaVoucherClassPolicy`: C para monotributo o exento; A o B para responsable inscripto), el
  código de cada clase y tipo (`ArcaVoucherTypes`; C: 11 factura y 13 nota de crédito; A: 1 y 3; B: 6 y 8), las condiciones
  del receptor con sus códigos y cuáles admite cada clase (`ArcaReceiverVatConditions`) y el importe de cada renglón
  (`ArcaInvoiceLine`). La etapa 4 programó el desglose del IVA (`ArcaVatBreakdown`) y suma las filas A y B.
- **En esta etapa** la cola emite sólo la clase C: si la regla da A o B, el pedido no se encola
  (`Arca.Invoicing.ClassNotSupported`) hasta que la etapa 4 lo habilite.
- La condición del emisor sale de los datos de la empresa que guarda el padrón (`ArcaRegistryCompany`). Si cambia (por
  ejemplo, deja el monotributo), la próxima emisión lo toma de la última prueba de la conexión.

### El puerto con formularios

Formularios no nombra a ARCA ([ADR 0023](../decisions/0023-arca-como-modulo-opcional.md)). El núcleo suma
`IStructuredRecordIssuer` en `Application/Interfaces/Channels/`, una colección opcional como el canal de acciones: sin
emisor instalado, la acción no se ofrece. No hay versión apagada aparte: la colección vacía es la versión apagada.

- **`IssuerId`** y su nombre en los dos idiomas («Emitir comprobante en ARCA»), que el editor de reglas muestra sin
  conocerlo.
- **`EnqueueAsync`** con el run, la respuesta y los datos neutrales fijados por la acción: identificación y nombre del
  receptor, su condición (el código de la tabla), concepto, fecha, período, vencimiento, renglones, total y, para anular, el
  run de la emisión original. Deduplica por run.
- **`GetAsync`** (y su versión por lote para las lecturas): estado, motivo estable, si espera por ARCA, cuándo vuelve a
  mirar, y el resultado al autorizarse: número formateado («0004-00000012»), código de autorización, vencimiento, el nombre
  del comprobante («Factura C») y la referencia del PDF fiscal en el almacén privado.
- **`ReviewAsync`** con la decisión de una persona sobre un `NeedsReview` y la revisión esperada.
- **`CancelAsync`**: sólo mientras el comprobante no tiene número; después, no.
- **`PreviewAsync`**: el tipo que corresponde y el total recalculado, para el «¿La emito?» de la app, sin guardar nada.

**La acción de regla «Emitir comprobante»** (`IssueDocument`, con el `IssuerId`) se suma a los cuatro tipos de formularios.
Sólo en «Cuando llega» o «Cuando se aprueba», una por regla y antes de los avisos. Su run fija los datos al reservar,
encola, y queda `Running` mirando el emisor (con la espera que el emisor sugiere, no cada 5 s). Termina `Completed` con el
comprobante autorizado y guarda el resultado en la respuesta (resultado externo, etapa 2); `Failed` con un rechazo; y
`Uncertain` sólo si el emisor está en `NeedsReview`. Antes de encolar, si la respuesta se anuló o se corrigió, queda
«No aplica». «Avisar a quien cargó» puede adjuntar el PDF fiscal de una emisión anterior de la misma regla, como hoy el PDF
de «Armar el PDF».

### PDF fiscal con QR

- **Lo arma el módulo ARCA** apenas tiene el CAE, en el mismo worker, con SkiaSharp y una copia propia de Public Sans (los
  módulos no comparten código). Se guarda una vez en el almacén privado (`arca-vouchers/{id:N}.pdf`, `WriteOnceAsync`, se
  relee y se confirma el SHA-256) y tiene su propia fuente de retención. Si el almacén falla, el comprobante queda autorizado
  con el PDF pendiente y se reintenta con espera; nunca se vuelve a emitir por eso.
- **El QR** sigue la especificación de ARCA: `https://www.arca.gob.ar/fe/qr/?p=` + el JSON versión 1 en Base64 (`ver`,
  `fecha`, `cuit`, `ptoVta`, `tipoCmp`, `nroCmp`, `importe`, `moneda`, `ctz`, `tipoDocRec`, `nroDocRec`, `tipoCodAut` «E» y
  `codAut`). La matriz la arma QRCoder (MIT, paquete nuevo) y se dibuja con Skia, sin `System.Drawing`.
- **Lo que lleva:** «ORIGINAL», la letra C con «COD. 11», punto de venta y número, fecha; el emisor (razón social,
  domicilio, CUIT, condición frente al IVA, ingresos brutos e inicio de actividades); el receptor (nombre, documento,
  condición y domicilio); el período y el vencimiento del pago si es un servicio; los renglones (descripción, cantidad,
  precio unitario e importe); el total; CAE y su vencimiento, y el QR. La nota de crédito dice además a qué factura anula.
- **Ingresos brutos e inicio de actividades** no salen del padrón: son dos datos nuevos de la configuración de Facturación,
  en su zona de la pantalla de ARCA (tablero CA1). Sin ellos la configuración de la familia no está completa.

### Catálogos de ARCA

- **Fuente:** el núcleo suma `IStructuredCatalogFeed` en `Application/Interfaces/Channels/`, una colección opcional. Cada
  fuente declara sus catálogos con una clave estable («arca.vat-conditions»), nombre en los dos idiomas, la carpeta del
  sistema («ARCA») y sus opciones (código, texto y si está vigente). ARCA aporta: condición frente al IVA del receptor
  (`FEParamGetCondicionIvaReceptor`, con la clase de cada una), tipos de documento, conceptos, monedas y alícuotas de IVA
  (estas dos, para la etapa 4).
- **Formularios guarda una copia local** de sólo lectura, como dice la
  [hoja de catálogos](2026-10-21-catalogos-con-origenes-externos.md#la-idea-central-el-origen-alimenta-una-copia-local): un
  tipo de catálogo nuevo (`External`) que no se edita, no se importa y no se borra desde la app; lo que deja de venir queda
  inactivo. Se usa en preguntas como cualquier catálogo.
- **Se actualiza solo:** una vez por día y al activarse la conexión, con una reserva para que corra un host por vez. Leer no
  tiene efecto fuera del sistema: alcanza la reserva y aplicar las diferencias con la revisión del catálogo
  ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md)). La pantalla del catálogo dice «Actualizado por ARCA hace
  2 h» o la última falla.
- **La carpeta «ARCA»** es la carpeta del sistema que diseñó el [plan de carpetas](2026-11-03-carpetas-de-formularios-y-catalogos.md)
  (etapa 2), recordada por su clave.

### Autocompletar del cliente

- **El padrón de un tercero:** `getPersona_v2` ya acepta cualquier CUIT. Una política nueva traduce la persona a nombre,
  domicilio y condición frente al IVA del receptor con el código de ARCA (responsable inscripto 1, exento 4, consumidor
  final 5, monotributo 6, no categorizado 7).
- **El puerto:** el núcleo suma `IStructuredPartyLookup` (buscar a alguien por su identificación fiscal), con adaptador en
  ARCA. Formularios lo usa al agregar o editar una opción del catálogo «Clientes»: «Completar con ARCA» junto a la CUIT llena
  la razón social, la condición y el domicilio (tablero CA1). El catálogo «Clientes» llega con el estándar y su campo CUIT
  queda atado a esa búsqueda.
- **Un cliente nuevo, también al facturar** (decisión 4): la pregunta «Cliente» ofrece los del catálogo, «Consumidor final»
  y «Otro: escribir la CUIT», que muestra la pregunta «CUIT del cliente» (condición de la etapa 2). Al recibir, el servidor
  la completa con el padrón y el «¿La emito?» ya dice el nombre que trajo ARCA; con «Emitir», el cliente se suma a «Clientes».
  Vale en la app y en WhatsApp. Si ARCA no tiene esa CUIT o no responde, el resumen lo dice y ofrece «Volver a probar» y «No
  emitir»: sin nombre no se emite.

### El estándar «Factura»

- **Preguntas atadas:** Cliente (catálogo «Clientes», con «Consumidor final» y «Otro: escribir la CUIT»), CUIT del cliente
  (se ve con «Otro»), Concepto (catálogo de ARCA), el bloque de
  renglones (Descripción, Cantidad, Precio unitario e Importe calculado), Total (cálculo), Período desde y hasta y
  Vencimiento del pago (se ven si el concepto no es «Productos»), Fecha (hoy por defecto) y Mandar copia al cliente.
- **Reglas que trae:** «Cuando llega»: emitir (o pedir la aprobación, si el formulario la usa, y emitir «Cuando se aprueba»);
  después avisar a quien cargó con el PDF fiscal y, si se marcó, mandar la copia al cliente.
- **El «¿La emito?»** (EM2-A): en la app, al tocar «Enviar», un resumen con el tipo que decidió el sistema, el cliente y el
  total (`PreviewAsync`), con «Emitir factura» y «Volver». En WhatsApp, al terminar el Flow llega el mismo resumen como mensaje,
  con «Emitir» y «No emitir», sin el intercambio cifrado con Meta. Mientras no contesta, la emisión espera y el detalle le
  muestra a quien cargó «Esperando que lo confirmes» con los mismos dos botones. «No emitir» anula la respuesta con el motivo
  «No se emitió» y no va nada a ARCA.
- **La copia al cliente** va por correo, con el PDF, a la dirección del catálogo «Clientes»; sin dirección, la opción no se
  ofrece. Usa el correo que ya manda la app.

### Anular con nota de crédito

- «Anular» una respuesta con factura autorizada pide el motivo, como hoy, y encola la nota de crédito C por el total: los
  mismos renglones e importes, el mismo concepto, período y receptor, la fecha de hoy y la factura en `CbtesAsoc` (tipo,
  punto de venta, número, CUIT y fecha). Sigue la misma cola, numeración y reconciliación.
- **Cuándo queda anulada la respuesta** (NC1-A): cuando ARCA autoriza la nota de crédito. Mientras tanto el título dice
  «Anulándose», sigue en la planilla y la zona «Comprobante» suma la nota con su estado. Si ARCA la rechaza, la respuesta sigue
  vigente con el motivo y se puede volver a anular. Anulada, sale de la planilla como cualquier anulada, con la factura y la
  nota (y sus dos PDF) en el detalle.
- **Corregir** una respuesta con una emisión encolada no se puede (`Forms.Record.AmendUnavailable`): se anula y se carga
  otra. Mientras la emisión no tiene número, anular la cancela y la respuesta queda anulada sin factura. Con número y sin
  resolver, «Anular» espera a que se resuelva.

### Permisos

- **`arca.issue`** («Emitir comprobantes en ARCA», el permiso de emitir que pedía la hoja de ruta). Hace falta, además de los
  permisos de formularios, para resolver la revisión de una emisión y para anular una respuesta con factura (efectos con
  valor legal). Cargar el formulario «Factura» no lo pide: lo controla quién puede completar ese formulario.
- Configurar la acción en las reglas sigue con `formrules.manage`.

### Pruebas

- **Unitarias:** la ventana de la fecha con los bordes
  de mes; el hash; el QR; la reconciliación (cada rama del paso 6); la espera creciente; el padrón de un tercero.
- **De integración con `FakeArca`** (WSAA y WSFEv1 simulados, ampliados con `FECompUltimoAutorizado`, `FECAESolicitar`,
  `FECompConsultar` y las tablas): respuestas que no llegan después de autorizar, 10016, 502 «Transacción activa», 500,
  rechazos con códigos y observaciones; dos workers a la vez sobre la misma secuencia (dos hosts) y un worker que muere con el
  número guardado. Ninguna prueba emite dos veces ni deja un número salteado.
- **Homologación real:** la corre el usuario con su clave fiscal, siguiendo [configurar ARCA](../guides/configurar-arca.md):
  certificado de WSASS con el pedido que genera la app, `wsfe` y la constancia asociados, un punto de venta de homologación y
  una factura C de punta a punta con su nota de crédito. Riesgo de la etapa 1 que sigue abierto: que homologación no devuelva
  puntos de venta.

## Decisiones del usuario (2026-11-09)

1. **EM1-A:** el detalle de una respuesta con factura tiene una zona «Comprobante», la primera, con número, CAE, vencimiento,
   cliente y total; su banda dice el estado de la emisión (Emitiendo, En cola, Emitida, Rechazada o Para revisar) y «PDF
   fiscal» va con los íconos del título. La actividad sigue contando el paso. El número es una columna para elegir en la
   planilla. Descartó EM1-B (todo dentro de la actividad).
2. **NC1-A:** la respuesta queda anulada cuando ARCA autoriza la nota de crédito; mientras tanto, «Anulándose». Descartó NC1-B
   (anulada al confirmar).
3. **EM2-A:** en WhatsApp, el «¿La emito?» es un mensaje después del Flow con «Emitir» y «No emitir». Descartó EM2-B (el total
   dentro del Flow con el intercambio cifrado).
4. **Un cliente nuevo también se puede escribir al facturar,** en la app y en WhatsApp: «Otro: escribir la CUIT» y el padrón la
   completa antes del «¿La emito?». Eligió esto en lugar de la recomendada (sólo desde la app).
5. **La fecha fuera de la ventana frena la emisión** y la persona elige «Emitir con la fecha de hoy» o «No emitir». Descartó
   cambiarla sola.

CA1 no tenía opciones: las tablas de ARCA de sólo lectura en la carpeta «ARCA», «Completar con ARCA» en «Clientes» y los datos
del comprobante en la zona «Facturación».

## Tareas

El backend va en orden. Lo de ARCA no depende de la etapa 2 y va primero; lo que toca formularios espera a que la etapa 2
haya commiteado lo suyo. El front lo programó la etapa 4 antes de la prueba integral, como pidió el usuario el 2026-11-11.

1. **Plan, ADR 0028, tableros y preguntas.**
2. **Hecho (8a81ab4b). Clientes de WSFEv1:** `FECompUltimoAutorizado`, `FECAESolicitar`, `FECompConsultar` y las tablas (`FEParamGetTiposDoc`,
   `…TiposConcepto`, `…TiposMonedas`, `…TiposIva` y `FEParamGetCondicionIvaReceptor`), con `FakeArca`.
3. **Hecho (b6fdae26). Dominio:** comprobante, secuencia, clase y códigos, condiciones del receptor, importe de cada
   renglón, ventana de la fecha y espera creciente. La huella quedó en Application (`ArcaVoucherRequestPolicy`) y el QR va
   con el PDF fiscal.
4. **Hecho (c9bf65a9 y ed22a602). Persistencia y migración** `AddArcaVouchers`, con ingresos brutos e inicio de
   actividades en Facturación y la referencia del PDF fiscal en el comprobante. Sin clave foránea a la conexión.
5. **Hecho (1b9ece7a). La cola:** encolar, el paso del worker (numeración, reconciliación y espera), la revisión y la cancelación, con las
   pruebas de varios hosts.
6. **Hecho (61c466ca). PDF fiscal,** almacén y retención. Ingresos brutos e inicio de actividades quedaron en la entidad
   (tarea 4); su ruta y el cambio de completitud de Facturación van con el front, para no dejar «requiere atención» una
   conexión que todavía no puede cargarlos.
7. **Hecho. Padrón de un tercero** y el adaptador de `IStructuredPartyLookup`, con las rutas `lookup` del catálogo
   («Completar con ARCA»).
8. **Hecho. Tablas de ARCA:** el adaptador de `IStructuredCatalogFeed`, con «Exento» y «No gravado» en las alícuotas.
**Lo que espera (2026-11-09):** la acción ya arma el pedido con el mapa «dato → pregunta» de la biblioteca (BI1-A); la
anulación con nota de crédito y el «¿La emito?» siguen a la acción; los catálogos de ARCA dependen de las carpetas, y el
autocompletar, del catálogo «Clientes» del estándar. Las tareas 7 y 8 van
con su consumidor (no se agrega un puerto sin quien lo use). El resultado externo y los cálculos de la etapa 2 ya están.

9. **Hecho (bbda01fa y el aviso de espera).** El puerto `IStructuredRecordIssuer`, la acción `IssueDocument` en formularios
   (en las reglas sólo con el emisor de su estándar, el run que encola una vez y sigue la espera del emisor, el resultado en
   la respuesta, la revisión con `arca.issue` y un solo aviso a quien cargó mientras espera por ARCA) y el emisor de ARCA.
10. **Hecho.** Los catálogos de sólo lectura (`External`) y su actualización en formularios, en la carpeta «ARCA»: se crean recién cuando el origen responde.
11. **Hecho.** El estándar «Factura» y el catálogo «Clientes» con «Completar con ARCA», y «Otro: escribir la CUIT»
    completado por el padrón en el emisor y, con «Emitir», sumado a «Clientes» una sola vez (el emisor lo devuelve al encolar y
    formularios lo agrega en la transacción del run). `pricesIncludeVat` como casilla de «Precio unitario» y la Alícuota que
    empieza en 21 % los programó la etapa 4 (8fb5a9a0 y f089f494); el emisor toma 21 % para un ítem sin alícuota (bcdeaf9e).
    **Hechas (2026-11-11) las reglas que
    trae el estándar:** «Agregar» deja la regla «Cuando llega» de la «Factura» (emitir, avisar con el PDF fiscal y mandar la
    copia) como la revisión activa de la copia, en el mismo guardado, con la clave de la revisión activa diferida
    (`DeferActiveRuleSetKey`, decisión B del usuario: sin excepción al ADR 0001). **Hecha (2026-11-11) la copia al cliente por correo:** «Mandar copia
    al cliente» atada y la acción `SendCopy`, que manda el PDF fiscal al correo del cliente por la cola durable de la app (sin
    correo, «No aplica»). **Arreglada (2026-11-11) la razón social recortada:** «Clientes» suma «Razón social»
    (`legal_name`, texto de hasta 200, el tope de la cola) y el emisor la prefiere al texto de la opción (30); «Completar con ARCA» y el cliente nuevo la
    traen entera, y el PDF fiscal parte el nombre y el domicilio del receptor en renglones en lugar de recortarlos.
12. **Hecho. Anular con nota de crédito:** la reversión es un run del motor; corregir una respuesta con emisión queda frenado.
13. **Hecho. El «¿La emito?» en WhatsApp** (EM2-A) y el cliente nuevo por su CUIT: la vista previa
    del emisor, el run que espera la confirmación de quien cargó por un canal, la ruta `confirmation` del flujo y «No emitir»
    que anula con «No se emitió», y la vista previa antes de «Enviar» en la app (`issue-preview`). En WhatsApp, los botones
    «Emitir» y «No emitir» del mensaje (ebb2f8df) y la respuesta «No la emitimos. FAC-… quedó anulada.» (f23759fa). El cliente
    nuevo se suma a «Clientes» al emitir (tarea 11).
14. **Hecho (front 0815da57, 18e31307, 9da76575 y e7b7476c). Front:** lo programó la etapa 4 junto con el suyo, con el diseño registrado en `docs/design/arca-factura-c.md`
    (b82c002a) y los contratos de este plan.
15. **Cierre:** la verificación de `AGENTS.md`, `docs/features/arca.md`, la fila de la etapa 3 en la hoja de ruta, la guía de
    homologación, el retiro de este plan con la auditoría y el traspaso al multitenant ofrecido.
