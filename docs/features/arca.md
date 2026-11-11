# ARCA: la conexión con los web services

Regla vigente desde la etapa 1 (2026-11-05). Una instalación se conecta con ARCA con el certificado de la empresa o, desde
el backend de la etapa 5 (2026-11-09), delegando los servicios de la empresa en la CUIT de la plataforma. Prueba la
conexión, trae del padrón los datos de la empresa y elige el punto de venta. Desde la etapa 3 emite la factura C y su nota de
crédito, y desde la etapa 4 la A y la B (ver [Emisión de comprobantes](#emisión-de-comprobantes-etapas-3-y-4)), con efecto
legal en producción; hasta homologar con la CUIT del usuario se prueba contra el ARCA simulado. Las decisiones están en el [ADR 0023](../decisions/0023-arca-como-modulo-opcional.md) (módulo y credenciales), el
[ADR 0025](../decisions/0025-familias-de-servicios-arca.md) (familias) y el
[ADR 0027](../decisions/0027-delegacion-guiada-arca.md) (delegación guiada); la operación, en
[configurar ARCA](../guides/configurar-arca.md), y la retirada, en [quitar ARCA](../guides/quitar-arca.md). Las etapas
que siguen están en la [hoja de ruta](../plans/2026-10-31-arca-formularios-estandar.md).

## Módulo y familias

- **Módulo opcional** en `Modules/Arca` de Domain, Application, Infrastructure y Api, con un `Arca<Capa>Registration`
  por capa que compone `Program.cs`. El núcleo no lo nombra.
- **Una carpeta por familia de servicios** debajo de cada carpeta de capa: `Connection` (conexión, certificado y ticket),
  `Registry` (padrón y datos de la empresa) e `Invoicing` (`FEDummy` y punto de venta). Una familia usa sólo
  `Connection`; `Connection` conoce a las demás sólo por `IArcaServiceDefinition` (sus servicios, su prueba y si su
  configuración está completa). `ArcaFamilyBoundaryTests` lo controla. Cada familia se quita sola.
- **Cada empresa elige qué servicios usa** entre los instalados. La constancia de inscripción es obligatoria, porque
  trae los datos de la empresa. Sólo los elegidos se prueban y muestran su zona.
- **Un ambiente por vez:** la conexión es de homologación o de producción. Pasar a producción es configurarla de nuevo.
- **Sólo certificados nuevos**, generados en la app. No se importa uno existente.
- **Tres modos:** con el certificado de la empresa, en delegación guiada o en delegación con robot (abajo). El tercero se
  ofrece sólo con el robot prendido; apagado, el alta lo rechaza con `Arca.Connection.ModeUnavailable`. Pasar de uno a otro
  es empezar de nuevo.

## Permisos y pantalla

- `arca.read` lee; cambiar algo o llamar a ARCA pide también `arca.manage` (`ArcaPermissions`, por
  `IPermissionCatalogSource`). Sin `arca.read`, «ARCA» no aparece en el menú.
- `arca.issue` decide sobre una emisión en revisión, además de los permisos de formularios.
- La pantalla es `/configuracion/arca`, en Administración → Configuración, sin estado en la URL. El alta va por zonas que
  se habilitan de a una y la conexión activa, por zonas con su acción en la banda. Los tableros y lo que se fijó al
  programar están en el [diseño del front](../../../ArquitecturaBaseFront/docs/design/arca-conexion.md).

## Estados

Sin conexión → esperando el certificado → sin verificar → activa ⇄ requiere atención.

- Subir el primer certificado lo prueba y deja la conexión sin verificar, o activa si todo salió bien.
- Una prueba en verde con el certificado vigente y las familias elegidas completas lleva a «activa».
- Una prueba fallida, un certificado vencido o una familia elegida sin su configuración llevan una conexión activa a
  «requiere atención»; en el alta, la dejan sin verificar.
- El servidor guarda el resultado general de la última prueba y su hora, no el de cada servicio. Elegir servicios sólo
  guarda la elección: la pantalla vuelve a probar.

## Contrato HTTP

Rutas MVC, todas con `arca.read`; las que escriben o llaman a ARCA, también con `arca.manage`. Las escrituras llevan la
revisión esperada y responden 409 con `Arca.Connection.RevisionConflict` si cambió.

| Verbo y ruta | Qué hace |
| --- | --- |
| `GET api/arca/connection` | Estado, modo, ambiente, CUIT, la CUIT de la plataforma (en delegación), la plataforma configurada, servicios instalados (con su familia y si se delegan) y elegidos, certificados sin secretos, última prueba y revisión |
| `POST api/arca/connection/certificate-requests` | Con CUIT, ambiente, alias, servicios y modo (por defecto, el certificado de la empresa) genera la clave y el pedido. Sin conexión la crea; con conexión pide otro certificado para ella (renovación, o un pedido nuevo en el alta), con su modo, y reemplaza el pendiente |
| `GET api/arca/connection/certificate-requests/current` | Descarga el `.csr` pendiente (`application/pkcs10`, `arca-<alias>.csr`) |
| `DELETE api/arca/connection/certificate-requests/current` | Cancela la renovación (revisión en la query) |
| `PUT api/arca/connection/services` | Elige los servicios que usa la empresa |
| `PUT api/arca/connection/certificate` | Sube el `.crt` en PEM, lo valida y prueba; responde el resultado de la prueba. En una renovación, el nuevo reemplaza al activo sólo con la prueba en verde |
| `POST api/arca/connection/test` | Prueba los servicios elegidos con la prueba de cada familia |
| `DELETE api/arca/connection` | Desconecta (revisión en la query) |
| `GET api/arca/registry/company` | Razón social, condición frente al IVA y domicilio fiscal que trajo el padrón |
| `GET api/arca/invoicing/settings` | El punto de venta elegido, ingresos brutos e inicio de actividades (cada uno null mientras no se cargó) |
| `GET api/arca/invoicing/points-of-sale` | Consulta a ARCA los puntos de venta de web service |
| `PUT api/arca/invoicing/point-of-sale` | Elige el punto de venta; la conexión se activa si la prueba salió bien y las demás familias están completas |
| `PUT api/arca/invoicing/voucher-data` | Guarda ingresos brutos (hasta 30 caracteres, sin espacios a los costados) e inicio de actividades (un día). No llama a ARCA ni cambia el estado de la conexión; responde la configuración de Facturación |

- **Límites por operador** (`Arca:RateLimiting`): lectura, escritura y `arca-remote`, que cubre lo que llama a ARCA
  (subir, probar, listar y elegir el punto de venta). Los cuerpos tienen 16 KiB como mucho. El padrón de un tercero, que se
  pide desde formularios, tiene su propio cupo por persona (`Arca:Registry`, en «Completar con ARCA»).
- **Contratos:** el `ToString` de los pedidos enmascara la CUIT y oculta el certificado.

## Certificado y credenciales

- **El pedido:** clave RSA de 2048 bits generada en el servidor y PKCS#10 con SHA-256, con `C=AR`, `O` y `CN` con el alias
  y `serialNumber` «CUIT …». No llama a ARCA. El alias va de 3 a 30 minúsculas, dígitos o guiones, y empieza con una letra.
- **Al subir el certificado** se comprueba que corresponda a la clave generada, que tenga la CUIT de la conexión, que el
  emisor sea el del ambiente elegido (`IssuerCommonName`) y que no esté vencido. Cada caso tiene su error.
- **A lo sumo un certificado activo y uno pendiente.** Renovar genera un pedido nuevo mientras el actual sigue andando.
- **Cifrado:** la clave privada y el ticket van cifrados con el anillo propio `Arca:SecretProtection` (AES-GCM, como el
  de IA), con un AAD atado a la instalación, la clave del anillo, el propósito, la conexión y el certificado. La Api no
  arranca sin el anillo. Si la clave guardada no se descifra, es una falla del anillo, no de la persona.
- **Desconectar** borra de verdad la conexión, los certificados, los tickets y los datos de cada familia: hay secretos.

## Ticket de acceso (WSAA)

- El pedido (TRA) lleva las horas con el huso explícito y un `uniqueId` al azar, y se firma en CMS (`SignedCms`).
- El ticket se guarda en PostgreSQL, uno por certificado y servicio, y se reutiliza mientras le quede margen
  (`TicketMargin`).
- **Renovar, un host por vez:** una transacción corta toma la reserva de la fila con dueño y vencimiento
  (`TicketLease`); el pedido al WSAA va fuera de la transacción, y otra transacción guarda el ticket sólo si la reserva
  sigue siendo propia. Quien encuentra la reserva de otro espera de a medio segundo y vuelve a leer.
- **«Ya posee un TA válido»:** la fila guarda hasta cuándo no se puede pedir otro (la ventana del ambiente:
  `TicketRetryWindow`) y la prueba lo informa con ese plazo. Ningún pedido HTTP espera minutos.
- Las fallas del WSAA se traducen a un motivo estable: servicio sin autorizar, reloj desfasado, bloqueado o sin respuesta.
- **En delegación,** el ticket de un servicio que la empresa delega tiene que incluirla en sus `relations` (abajo).

## La prueba

- Pide un ticket para cada servicio elegido y corre la prueba de su familia. Lo que contesta ARCA no es un error del
  pedido: es el resultado de cada servicio, con su estado y un motivo estable.
- Corre fuera de la transacción; el resultado se guarda sólo si el certificado probado sigue siendo el activo. Si cambió
  mientras corría, da `RevisionConflict`. La prueba de una renovación en rojo no guarda nada.

## Familias

### Registry: padrón y datos de la empresa

- `ws_sr_constancia_inscripcion`, `getPersona_v2` sobre la CUIT de la conexión (`idPersona`), con la CUIT de quien firma
  en `cuitRepresentada`: la de la empresa en el modo 1 y la de la plataforma en delegación. Es una consulta: no se delega.
- La prueba guarda razón social, condición frente al IVA y domicilio fiscal con `IArcaRegistryCompanyService`, en su
  transacción y con el lock de la conexión. Una CUIT sin nombre no guarda nada.
- La condición frente al IVA: el monotributo manda; si no, el impuesto 30 es responsable inscripto y el 32, exento. Una
  persona humana se nombra «nombre apellido».

### Invoicing: facturación electrónica

- `wsfe` (WSFEv1). La prueba responde a `FEDummy`: una parte distinta de «OK» la deja sin respuesta.
- Los puntos de venta salen de `FEParamGetPtosVenta`: quedan afuera los dados de baja y se marcan los bloqueados. Elegir
  uno bloqueado, dado de baja o que ARCA ya no devuelve da `Arca.Invoicing.PointOfSaleUnavailable`.
- La configuración de la familia está completa con el punto de venta elegido. Guarda además ingresos brutos e inicio de
  actividades, que el PDF fiscal muestra y el padrón no trae. Se cargan en la zona «Facturación · Datos del comprobante»
  de la conexión activa (AC4) con `PUT api/arca/invoicing/voucher-data`; no completan la familia ni frenan la activación.
  Un número vacío o de más de 30 caracteres da `Arca.Invoicing.GrossIncomeInvalid`, y una fecha vacía,
  `Arca.Invoicing.ActivityStartInvalid`.

### Emisión de comprobantes (etapas 3 y 4)

La cola del [ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md) está programada y la pide la acción de
formularios «Emitir comprobante», con el estándar «Factura» y la anulación con nota de crédito, en backend y front
(2026-11-09 a 2026-11-11). Faltan la prueba integral y la de homologación, en el
[plan](../plans/2026-11-09-arca-etapa-3-factura-c.md). La A y la B siguen el
[plan de la etapa 4](../plans/2026-11-09-arca-etapa-4-factura-a-y-b.md), programada en backend y front (2026-11-11); faltan
la prueba integral y la de homologación. Lo que se ve en la app y en WhatsApp está en el
[diseño del front](../../../ArquitecturaBaseFront/docs/design/arca-factura-a-y-b.md).

- **Las tablas como catálogos** (`ArcaStructuredCatalogFeed`): condición frente al IVA, tipos de documento, conceptos (con el
  código como valor), monedas y alícuotas de IVA (con el porcentaje como valor, más «Exento» y «No gravado», códigos `exempt` y
  `not-taxed`) llegan a formularios como catálogos `External` en la carpeta «ARCA», sólo con las filas vigentes. Se leen con el
  ticket de `wsfe`; sin conexión o sin respuesta devuelven el código del motivo y formularios conserva lo que tenía.
- **El emisor de formularios** (`ArcaStructuredRecordIssuer`): la fuente y la clave del estándar son `arca.invoice`. Lee los
  datos atados de la «Factura» (el cliente de «Clientes» con `tax_id`, `legal_name`, `vat_condition` y `address`, o consumidor
  final sin CUIT; el nombre del receptor es `legal_name` cuando viene y no está vacío y, si no, el texto de la opción, así que un
  «Clientes» creado antes del campo sigue andando; el concepto por su código de ARCA; la fecha, o la de hoy; el período, el vencimiento, los renglones, cada uno con su alícuota, que vacía vale 21 %, y el total) y
  encola con el run como clave. La vista previa del «¿La emito?» arma el mismo pedido y responde la clase que decide el
  sistema con su porqué («Lo decide el sistema: sos monotributista», o en la A y la B, la condición del cliente), el receptor,
  la fecha, los importes que discrimina (en la A, el neto gravado, el IVA de cada alícuota, lo exento y lo no gravado; en la B
  a un consumidor final, exento o no alcanzado, el IVA contenido) y el total recalculado, sin encolar; un pedido que no se
  encolaría responde su código. Está emitida cuando ARCA la autorizó y su PDF fiscal está guardado, con comprobante,
  número, fecha, CAE, vencimiento del CAE, cliente (el nombre del receptor del pedido), su documento («CUIT 30-50001091-2»,
  «DNI …» o «Sin identificar»), su condición frente al IVA y total como resultado (`voucher`, `number`, `issuedOn`, `cae`,
  `caeDueOn`, `customer`, `customerDocument`, `customerVatCondition` y `total`), también en la nota de crédito. Es la zona
  «Comprobante» del detalle y los datos `{result:arca.invoice.<clave>}` de los avisos; el documento y la condición se escriben
  con `ArcaFiscalDocumentPolicy`, como en el PDF fiscal. En revisión responde `arca.invoice.<motivo>`; seguir es otro
  número o la fecha de hoy, y decidirlo pide `arca.issue`. Anular la respuesta cancela la emisión que todavía no
  tiene número o pide la nota de crédito por el total (`EnqueueCreditNoteAsync`), las dos por el mismo puerto. Mientras espera por ARCA, el aviso es «ARCA no responde. Tu factura
  sale apenas vuelva.» (o «Tu nota de crédito»), y la respuesta trae cuándo vuelve a probar la cola (`NextCheckAtUtc`) y
  cuántas veces seguidas no salió (`Attempts`, la columna `Failures` del comprobante, que vuelve a cero al emitir o al seguir
  después de una revisión). Un rechazo trae el código estable (`wsfe.<código>`) y, en `FailureDetail`, el primer mensaje que
  guardó ARCA (los errores antes que las observaciones) como lo mandó: «10015 · El número de documento del receptor no es
  válido.», también en la nota de crédito.
- **El estándar «Factura»** (`ArcaInvoiceStandardSource`, `arca.invoice`, prefijo FAC): Cliente (de «Clientes»), CUIT del
  cliente (sólo con «Otro: escribir la CUIT»), Concepto (tabla de ARCA), Fecha (vacía, la de hoy), Período desde y hasta y
  Vencimiento del pago (opcionales y siempre a la vista: WhatsApp no muestra una pregunta por varias opciones de un catálogo,
  y la cola los exige con servicios), el bloque de ítems (Descripción, Cantidad, Precio unitario, Alícuota
  e Importe calculado), el Total calculado y «Mandar copia al cliente» (sí o no, no obligatoria), cada pregunta atada a su dato. Trae «Clientes» (`arca.customers`: CUIT, Razón social, Condición
  frente al IVA sobre la tabla de ARCA, Domicilio y Correo) con «Consumidor final» y «Otro: escribir la CUIT», y va con su copia
  a la carpeta «ARCA». El texto de una opción admite 30 caracteres y es lo que se ve en las listas y en WhatsApp; la razón
  social entera va en «Razón social» (`legal_name`, texto de hasta 200, el mismo tope que la cola pone al nombre del receptor), y
  de ahí sale el nombre del receptor. Necesita las tablas de ARCA (se crean cuando la conexión responde). Con «Otro», el emisor completa el
  cliente con el padrón antes de la vista previa y de encolar; sin la CUIT, `Arca.Invoicing.ReceiverInvalid`. Encolado (con
  «Emitir»), lo devuelve completado (`CatalogItem`: la CUIT como código, la razón social y los campos del padrón, con la razón
  social entera en `legal_name`) y formularios lo suma a «Clientes» si todavía no está: la opción lleva los primeros 30
  caracteres como texto y el campo, el nombre entero. La copia al cliente es la acción «Mandar copia por correo» de
  formularios (`SendCopy`, después de «Emitir comprobante en ARCA»): el emisor responde el correo del cliente de «Clientes»
  (`email`) sólo si se marcó «Mandar copia al cliente» y es una dirección válida (`ArcaInvoiceIssuePolicy.CopyRecipient`); con
  consumidor final, un cliente escrito por su CUIT o sin correo, la copia queda «No aplica», porque el borrador no puede ocultar
  la pregunta según un campo del cliente. La copia del estándar llega con su regla «Cuando llega»: «Emitir comprobante en
  ARCA», «Avisar a quien cargó» con el PDF fiscal («Listo: emitimos la {result:arca.invoice.voucher}
  {result:arca.invoice.number}.») y «Mandar copia por correo» («Te mandamos la … en el PDF adjunto.»). Con aprobación, se
  pasa a mano a «Cuando se aprueba» en el editor.
- **«Completar con ARCA»** (`ArcaStructuredPartyLookup`, en Facturación, con el padrón por `IArcaPartySource`): la constancia de un tercero completa los
  «Clientes» de la «Factura» (`arca.customers`) desde su CUIT, con la razón social (entera también en `legal_name`), la
  condición frente al IVA como receptor (1, 4, 5 o 6) y el domicilio fiscal. Una CUIT inválida da `Arca.Registry.CuitInvalid` sin llamar a ARCA y una que el padrón no
  tiene, `Arca.Registry.PersonNotFound`. Consulta en nombre de quien firma: en delegación, la plataforma con su CUIT, sin que la
  empresa delegue la constancia. Los cupos de formularios no cuidan a ARCA, así que el padrón cuida lo suyo: cada persona
  consulta hasta 6 CUIT distintas por minuto (`Arca:Registry:LookupsPerMinute`; repetir una no cuenta y la emisión de fondo no
  tiene cupo) y al pasarse recibe `Arca.Registry.TooManyLookups`; una constancia que respondió con datos queda diez minutos en
  Redis por conexión y CUIT, así la vista previa de «¿La emito?», que se pide con cada cambio, «Completar con ARCA» y la emisión
  no vuelven a ARCA. Una falla no se guarda.
- **Encolar** (`IArcaVoucherService`) valida el receptor (CUIT con su dígito, DNI, o consumidor final sin identificar),
  el concepto, el período y el vencimiento del pago de un servicio, la fecha que ARCA acepta hoy y los renglones, y
  recalcula cada importe con half-even: un total distinto del pedido no se encola. La clase la decide el sistema con la
  condición frente al IVA de la empresa (`IArcaCompanyProfileSource`, del padrón) y la del receptor: monotributo o exento,
  C; responsable inscripto, A al responsable inscripto y a los monotributistas (1, 6, 13 y 16) y B a los demás. Fija lo que
  va a emitir, con los datos del emisor de ese momento, y la clave del pedido la hace idempotente. La nota de crédito anula
  por el total una factura autorizada, una sola vez, de su misma clase, con los mismos renglones, la factura en
  `CbtesAsoc` y la fecha de hoy.
- **A y B (etapa 4):** cada renglón trae su IVA: una alícuota de ARCA por su código (3, 4, 5, 6, 8 y 9; el porcentaje lo
  pone el servidor), «Exento» o «No gravado». El pedido dice si los precios incluyen el IVA. El IVA se calcula por alícuota
  sobre la suma de sus renglones, con half-even; con precios con IVA, la base es la suma dividida por (1 + alícuota) y el
  IVA, lo que falta. Al pedido van `ImpNeto`, `ImpIVA`, `ImpOpEx` (exento), `ImpTotConc` (no gravado) y el array `Iva`,
  una entrada por alícuota. La A exige CUIT; un renglón sin IVA en la A o la B no se encola (`Arca.Invoicing.ItemsInvalid`).
  La C descarta el IVA que llegue.
- **Numeración:** `ArcaVoucherSequences` tiene una fila por ambiente, CUIT, punto de venta y tipo, con una reserva corta y
  el comprobante pendiente. El worker (`Arca:Invoicing:Worker`) toma una secuencia por vez y su comprobante más viejo sin
  resolver; nadie toma otro número mientras haya un pendiente. El número (último autorizado más 1) y la huella SHA-256 se
  guardan antes de `FECAESolicitar`.
- **Reconciliación:** ante un 10016, una respuesta que no llega, un 500, un 501, un 502 o una falla SOAP, el comprobante
  queda incierto y el próximo intento consulta `FECompConsultar`: si ARCA lo tiene con el mismo documento, fecha, concepto
  y total, queda autorizado con ese CAE; si no lo tiene y el número sigue siendo el siguiente, se manda otra vez con el
  mismo número. Un número tomado con otros datos o que falta va a revisión humana y frena su secuencia.
- **Espera creciente:** 30 s, 1, 2, 5, 10 y 30 minutos y después cada hora, sin tope; sin ticket o sin conexión, espera sin
  número. Una fecha que salió de la ventana (5 días con productos, 10 con servicios, en hora de la Argentina) va a revisión:
  «Emitir con la fecha de hoy» o «No emitir». Revisar exige la revisión esperada; cancelar, que no tenga número.
- **PDF fiscal:** al autorizarse, el worker lo dibuja con diseño fijo y el QR de ARCA, lo escribe una vez en el almacén
  privado (`arca-vouchers/{id}.pdf`) y confirma su SHA-256. Si el almacén falla, el comprobante sigue autorizado y el PDF
  espera. Esos PDF no se limpian mientras exista su comprobante. El nombre y el domicilio del receptor son datos fiscales y
  no se recortan: se parten por palabras en los renglones que hagan falta y el recuadro del receptor crece con ellos, con el
  período debajo.
- **PDF de la A y la B:** la A muestra precios sin IVA con su alícuota y, arriba del pie, el neto gravado, el IVA de cada
  alícuota y lo exento o no gravado; a un monotributista (6, 13 y 16) lleva la leyenda del art. 20 de la RG 5003. La B
  muestra los precios con el IVA incluido; a un exento, un no alcanzado o un consumidor final (4, 15 y 5) lleva abajo a la
  izquierda el recuadro de la RG 5614 con el IVA contenido y los otros impuestos nacionales indirectos en 0.
- **Desconectar no borra comprobantes ni secuencias:** son registros con valor legal y no tienen clave foránea a la
  conexión. El worker sólo trabaja los del ambiente y la CUIT de la conexión activa.

## Delegación guiada (etapa 5)

La empresa delega sus servicios en la CUIT de la plataforma, que firma con su certificado y opera en su nombre. Está
programada: el backend el 2026-11-09 y la pantalla el 2026-11-11, con los tableros elegidos (DG1-A y DG2-A) registrados en
el [diseño del front](../../../ArquitecturaBaseFront/docs/design/arca-delegacion.md); el [plan](../plans/2026-11-09-arca-etapa-5-delegacion-guiada.md) tiene las fuentes oficiales de ARCA.

- **La plataforma es configuración de la instalación:** `Arca:Platform:Cuit` y `Arca:Platform:Name` (el nombre de la guía),
  las dos o ninguna. Sin ellas, el alta en delegación da `Arca.Connection.PlatformNotConfigured`.
- **La conexión guarda la CUIT de la plataforma** (`PlatformCuit`) al empezar, y su certificado es el de la plataforma para
  esta instalación: el mismo ciclo, anillo, renovación y tickets que en el modo 1, con la CUIT de la plataforma en el
  pedido y en la inspección. Su alias es el computador fiscal de la plataforma; conviene uno por instalación y renovar con
  el mismo.
- **La CUIT de la conexión es la de la empresa,** y va en `Auth.Cuit`. Cada servicio declara si se delega: Facturación sí;
  la constancia de inscripción no, porque la plataforma la consulta con su CUIT.
- **Detectar es probar:** «Verificar la delegación» corre la prueba. Un servicio delegado cuyo ticket no incluye a la empresa
  queda `NotDelegated`; si el alias todavía no tiene el servicio, `NotAuthorized`. Si el ticket guardado no la incluye, se
  pide otro una vez (la delegación pudo llegar después), y vale el bloqueo de la ventana si ARCA no da otro. Una familia
  que llama a un servicio sin delegar recibe `Arca.Connection.ServiceNotDelegated`.
- **Un token que no se puede leer no frena:** queda un log de advertencia y decide ARCA.
- **La app no pide ninguna clave fiscal:** la empresa delega y da de alta el punto de venta con la suya; la plataforma
  acepta y asigna con la suya. Los pasos están en [configurar ARCA](../guides/configurar-arca.md#conectar-por-delegación).

## Delegación con robot (modo 3)

Un robot propio con Playwright y Chromium hace en la página de ARCA los pasos de la delegación guiada
([ADR 0026](../decisions/0026-robot-de-delegacion-arca.md)). La pantalla sigue los tableros elegidos (RB1-A a RB3-A),
registrados en el [diseño del front](../../../ArquitecturaBaseFront/docs/design/arca-robot.md). La operación está en
[correr y mantener el robot](../guides/robot-arca.md).

- **Apagado por defecto** (`Arca:Robot:Enabled`). Prendido, el alta acepta el modo `PlatformDelegationAutomated` con el mismo
  modelo que la delegación guiada; apagado, ese modo da `Arca.Connection.ModeUnavailable`.
- **Dos lados, uno por conexión** (`ArcaRobotRuns`): el del cliente, con su clave, y el de la plataforma, con la de la
  instalación (`Arca:Robot:Platform`). Cada lado guarda su estado, sus pasos hechos, lo que dejó cada paso (el punto de
  venta), el motivo y el paso de la falla, y su reserva. Nunca la clave ni la CUIT o CUIL de quien entró.
- **El lado del cliente corre en el host que recibió la clave,** por una cola en memoria: la clave no va a la base, a la cola
  cifrada ni a otro host. Entra a ARCA, elige la empresa si quien entra la administra, delega cada servicio delegable en la
  CUIT de la plataforma y corre los pasos de cada familia: Facturación da de alta el punto de venta de web service con el
  sistema «… Web Services» que ARCA ofrezca, o usa el que ya hay. Si el host se cae, la reserva vence y la corrida se lee
  interrumpida.
- **El lado de la plataforma es durable** y lo toma cualquier host con la reserva: crea el alias del certificado con el
  pedido de la app y lo carga por la subida de siempre (RB5-A), autoriza las consultas para el alias y, cuando el cliente
  terminó, acepta la delegación y la asigna al alias. Confirma con la prueba de la conexión (la detección de la etapa 5):
  sólo con todos los servicios en verde termina bien.
- **Si el cliente se frena, sigue con la guía (RB3-A):** la pantalla abre la guía de la etapa 5 en el paso donde se frenó.
  Hechos sus pasos a mano, «Verificar la delegación» da por terminado su lado, frenado o interrumpido, y la plataforma
  acepta, asigna y confirma enseguida. Si el lado del cliente no estaba frenado, responde `RobotNotStopped`.
- **Cada paso mira antes si ya está hecho,** y sus claves son las de la guía de la etapa 5 más `company.login`,
  `platform.login`, `platform.certificate` y `verify`, para que la pantalla abra la guía donde se frenó.
- **Motivos:** ingreso rechazado, captcha, validación extra, pantalla cambiada, ARCA no responde e interrumpida. El robot no
  resuelve captchas ni pide un segundo factor. Del lado de la plataforma, ARCA sin responder reintenta con espera creciente
  (`RetryDelay`, hasta `MaxAttempts`); lo demás frena, y un ingreso rechazado no se reintenta para no bloquear la clave.
- **El aviso (RB4-A):** una pantalla cambiada de cualquier lado, y del lado de la plataforma también un captcha, una
  validación extra o su ingreso rechazado, mandan un correo a `Arca:Robot:AlertEmail` por la cola de siempre, con el paso,
  el motivo y la hora, como mucho uno por motivo y paso por día. Siempre queda el log de error.
- **Rutas:** `GET api/arca/connection/robot-runs/current` (`arca.read`) dice si el robot está prendido y cómo va cada lado;
  `POST api/arca/connection/robot-runs` (`arca.read` + `arca.manage`, límite `arca-remote`) arranca el lado del cliente con
  el consentimiento, la CUIT o CUIL de ingreso, la clave y la revisión esperada, y responde 202;
  `POST api/arca/connection/robot-runs/current/manual-completion` (`arca.manage`) da por terminado a mano el lado del cliente
  frenado y responde 202. Errores propios: `RobotDisabled`, `RobotModeMismatch`, `RobotConsentMissing`, `RobotKeyMissing`,
  `RobotRunInProgress` (409), `RobotNotStopped` (409) y `RobotBusy` (429), con el prefijo `Arca.Connection.`.
- **La clave del cliente** es un tipo que no se imprime ni se serializa, se escribe sólo en el campo de ingreso y no queda en
  respuestas, logs, excepciones ni filas: lo verifica una prueba que la busca. Playwright corre sin trazas, HAR, video ni
  capturas, y la Api no arranca con su depuración encendida ni sin Chromium.

## Robustez y privacidad

- La prueba sólo lee y no tiene efecto legal: alcanzan la revisión, el 409 y la reserva del ticket
  ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md)).
- Los clientes SOAP son propios (`HttpClient` y `System.Xml.Linq`), sólo HTTPS, sin seguir redirecciones, con tope de
  tamaño de respuesta, sin reintentos automáticos (se saca la resiliencia común) y con los loggers del `HttpClient`
  apagados.
- Los logs no llevan token, firma, clave, pedido ni certificado; la CUIT va enmascarada.

## Pendiente

- **Homologación real** con un certificado de WSASS para una CUIT del usuario: confirmar los CN de los emisores de cada
  ambiente y la condición frente al IVA con CUIT reales. Se cierra cuando la conexión, los datos de la empresa y los
  puntos de venta respondan contra ARCA.
- **La delegación de verdad:** en homologación, con una autorización de WSASS con «CUIT representado»; en producción, con
  la clave fiscal de la plataforma y la de una empresa. Confirma el formato del token y que un ticket nuevo traiga la CUIT
  después de aceptar.
