# Revisión de arquitectura: ARCA — 2026-11-14

Segunda corrida de [revisar la arquitectura de un desarrollo](../guides/revisar-arquitectura.md), después de la de
[WhatsApp](2026-11-12-arquitectura-whatsapp.md). Es de solo lectura, sobre el backend en `23797fff` y el front en `7b95095b`;
no incluye los cambios sin commitear de otras sesiones (ese día seguían en curso los arreglos de WhatsApp y la lectura de
documentos). ARCA va antes que IA y Formularios porque emite comprobantes que no se pueden deshacer, y conviene revisarlo antes
de homologar.

- **Ficha:** confirmada por el usuario el 2026-11-14, sin cambios.
- **Revisión:** seis revisores contestaron los 30 criterios, por bloque: conexión y ticket, padrón y configuración, cola de
  emisión, emisor y cálculo, robot, y uno transversal.
- **Verificación:** nueve verificadores, en cinco tandas, intentaron refutar cada «No cumple» y «Con reservas» con el escenario
  y el código, sin la conclusión del revisor (uno se cortó sin escribir y se repitió). Se refutó un caso: los valores nuevos de
  una enumeración no mandan nada equivocado, y quedó como nota en H2. Dos pasaron a costos que acepta la documentación (O1 y la
  caída de Redis en H13), y la mayoría quedó con ajustes de alcance, horizonte o arreglo, ya incorporados. Los verificadores
  sumaron casos que los revisores no habían visto: el 10016 también cubre fechas, según el manual de WSFEv1 (H1); una pantalla
  vieja que borra una conexión nueva (H15); el silencio por WhatsApp cuando la emisión falla (H11), y el tope de 200 clientes
  por WhatsApp (B5). Estos tres últimos se confirmaron además leyendo el código.
- **Fuentes externas:** el manual del desarrollador de WSFEv1 (v4.7) y el de la constancia de inscripción (v3.4), de ARCA, y el
  código de EF Core 10 para la conversión de enumeraciones.
- **Sin ejecución:** no se corrió nada. Los tiempos y los techos salen del código, y las pruebas que los confirmarían están en
  [controles y pruebas](#controles-y-pruebas-que-conviene-sumar).

## Resumen

- **Lo fuerte.** La cola resuelve bien lo difícil y lo prueba con un ARCA simulado y dos workers: una sola emisión por pedido,
  el número y la huella guardados antes de pedir, la reserva con fence entre copias y la consulta a ARCA antes de repetir lo
  incierto. El ticket se comparte entre hosts sin pedir dos, los secretos van cifrados con un AAD atado a su dueño y la clave
  fiscal del robot no deja rastro en respuestas, logs ni filas. Dos criterios se cumplen en los tres horizontes, estado por
  pedido y paginado; filtros no aplica, y en el resto hay huecos, la mayoría con arreglos de una línea o chicos.
- **Bugs funcionales, fuera de los criterios:**
  - la factura A o B con precios sin IVA, que es como viene el estándar, no se emite nunca: la prueba de la etapa 4 falla en el
    paso 4 (B1);
  - «Emitir con la fecha de hoy» no corre el vencimiento del pago, y ARCA la rechaza (B2);
  - una constancia con errores puede llevar a una B en vez de una A, y queda guardada en «Clientes» (B3);
  - el estado de la conexión no sigue al certificado vencido ni a un servicio nuevo (B4);
  - con más de 200 clientes, la «Factura» deja de poder cargarse por WhatsApp (B5).
- **Hoy, con una sola copia**, lo que más pesa:
  - una venta con fecha anterior a la última factura traba esa secuencia para siempre, sin revisión ni salida desde la app (H1);
  - una versión puede mandarle a ARCA otro comprobante que el confirmado, en cada despliegue que cambie el cálculo (H2);
  - restaurar la base puede borrar PDF fiscales y facturar dos veces una venta (H3);
  - las tablas de ARCA piden un ticket cada cinco minutos para un servicio que no se eligió (H4);
  - el robot vuelve a probar la clave rechazada de la plataforma y puede hacerla bloquear (H5);
  - poner a emitir un formulario no pide ningún permiso de ARCA (H6);
  - una espera que no es de ARCA avisa «ARCA no responde», también con el CAE ya dado (H7).
- **Con varias copias**, los límites de pedidos y el cupo del padrón se multiplican (H32), y empeoran la convivencia de
  versiones (H2) y la reserva del robot (H20).
- **A gran escala**, el techo está en la cola de a uno por secuencia (H33, que con picos ya se nota hoy), en las búsquedas de los
  PDF y la limpieza que recorren todo (H34 y H35), en el lock del ticket en cada llamada (H36) y en «Clientes» cargado entero
  (H37).
- **Lo que decidiste el 2026-11-14**, en las ocho la opción recomendada ([detalle](#decisiones-del-2026-11-14)):
  - el horizonte es «Hoy», con B1 y H1 antes de la prueba integral, más la documentación de H32, el índice de H34 y la pausa de
    H33;
  - un 10016 que ARCA no emitió va a revisión (H1);
  - poner a emitir un formulario pide `formrules.manage` y `arca.issue` (H6);
  - lo que ARCA tiene y la base no conoce va a revisión, y el robot da de alta un punto de venta propio (H3);
  - sumar un servicio pasa la conexión a «requiere atención» (B4);
  - el plazo fiscal se escribe, y «Clientes» suma al cliente recién al emitir (H25);
  - los nombres fiscales van en español en todos lados (H28);
  - la plataforma avisa cuando se rinde (H19).

## Ficha

Commits revisados: backend `23797fff` (copia extraída con `git archive`; quedan afuera los cambios sin commitear de otras
sesiones: WhatsApp, lectura de documentos y una edición del plan de la etapa 1 de ARCA) y front `7b95095b`.

**Entradas**

- **Rutas propias** (`src/ArquitecturaBase.Api/Modules/Arca/Controllers`, cuatro controllers, 17 rutas):
  - `api/arca/connection`: estado; pedido de certificado (crear, descargar el `.csr`, cancelar); servicios; subir el
    certificado, que prueba; probar; desconectar.
  - `api/arca/connection/robot-runs`: estado, arrancar el lado del cliente con la clave fiscal (202) y darlo por terminado
    a mano (202).
  - `api/arca/registry/company`: los datos de la empresa que trajo el padrón.
  - `api/arca/invoicing`: configuración; puntos de venta (consulta a ARCA); elegir el punto de venta; datos del comprobante.
- **Rutas de formularios que llegan a ARCA** (`Api/Modules/BusinessForms/Controllers`): `api/forms/entries/{id}/issue-preview`
  (vista previa de «¿La emito?»), `api/forms/catalogs/{id}/lookup` («Completar con ARCA»),
  `api/forms/records/{id}/actions/{runId}/review` (decidir una emisión en revisión, con `arca.issue`) y
  `.../confirmation` («Emitir» o «No emitir», también desde WhatsApp); anular una respuesta cancela la emisión o pide la nota
  de crédito por el mismo puerto.
- **Workers:** `ArcaVoucherBackgroundService` (la cola de emisión), `ArcaCompanyRobotBackgroundService` y
  `ArcaPlatformRobotBackgroundService` (el robot, apagado por defecto) y `ArcaRobotBrowserCheck` (al arrancar). La acción
  «Emitir comprobante» corre en `FormActionBackgroundService` y las tablas de ARCA llegan por `FormCatalogFeedBackgroundService`,
  los dos de BusinessForms.
- **Pantallas:** `../ArquitecturaBaseFront/src/features/arca` (conexión, delegación guiada, robot, punto de venta, datos del
  comprobante y empresa) y, en formularios, la emisión: «¿La emito?», la zona «Comprobante» y «Completar con ARCA».

**Datos**

- **Tablas:** `ArcaConnections`, `ArcaCertificates`, `ArcaAccessTickets`, `ArcaRobotRuns`, `ArcaRegistryCompanies`,
  `ArcaInvoicingSettings`, `ArcaVouchers` y `ArcaVoucherSequences` (cinco migraciones, del 2026-11-04 al 2026-11-11).
- **Secretos en la base:** la clave privada y los tickets, cifrados con el anillo propio `Arca:SecretProtection`.
- **En memoria, a propósito:** la cola del robot (la clave fiscal no sale del host) y el cupo del padrón por persona.
- **Redis:** la constancia de un tercero, diez minutos, por conexión y SHA-256 de la CUIT.
- **Archivos:** los PDF fiscales en el almacén privado (`arca-vouchers/{id}.pdf`), retenidos mientras exista su comprobante.

**Terceros**

- **ARCA:** WSAA (tickets), `ws_sr_constancia_inscripcion` (padrón) y `wsfe` (WSFEv1: prueba, puntos de venta, tablas,
  último autorizado, `FECAESolicitar` y `FECompConsultar`), con clientes SOAP propios.
- **La web de ARCA**, con Playwright y Chromium (el robot).
- **Núcleo:** `CuitNumber` (6c1aff65), el catálogo de permisos, el almacén privado y su limpieza, la cola de correo (avisos
  del robot) y `RedisCache`.
- **BusinessForms, por puertos:** `IStructuredRecordIssuer` (emitir, vista previa, revisar, cancelar y nota de crédito),
  `IStructuredCatalogFeed`, `IStructuredPartyLookup` e `IStructuredFormStandardSource` (el estándar «Factura» y «Clientes»).

**Reglas:** [ARCA](../features/arca.md); los ADR [0023](../decisions/0023-arca-como-modulo-opcional.md),
[0025](../decisions/0025-familias-de-servicios-arca.md), [0026](../decisions/0026-robot-de-delegacion-arca.md),
[0027](../decisions/0027-delegacion-guiada-arca.md), [0028](../decisions/0028-emision-de-comprobantes-arca.md) y
[0029](../decisions/0029-biblioteca-de-estandares.md), con los generales 0001, 0009, 0012, 0015, 0016 y 0018; las guías
[configurar ARCA](../guides/configurar-arca.md), [quitar ARCA](../guides/quitar-arca.md) y [el robot](../guides/robot-arca.md);
la [hoja de ruta](../plans/2026-10-31-arca-formularios-estandar.md) y los planes de las etapas 1 a 6; los diseños
`arca-*.md` del front, y los `AGENTS.md` del módulo en cada capa.

**Pruebas:** integración en `tests/ArquitecturaBase.Api.IntegrationTests/Modules/Arca` (28 archivos, más 9 de
`TestFeatures`), unitarias de Application (16, más 3) y de Domain (15), arquitectura (2) y seis de BusinessForms que
nombran a ARCA; en el front, los tests de `features/arca` y `src/test/visual/arca.spec.ts`.

**Tamaño:** 226 archivos `.cs` sin migraciones (Api 12, Application 126, Domain 39 e Infrastructure 49), unas 9.200
líneas, más 6 `.resx`; del lado de formularios, las 19 piezas de emisión (unas 850 líneas).

## Matriz

| # | Criterio | Hoy | Varias copias | Gran escala | Hallazgos |
| --- | --- | --- | --- | --- | --- |
| 1 | Estado por pedido | Cumple | Cumple | Cumple | — |
| 2 | Ediciones simultáneas | Con reservas | Con reservas | Con reservas | H15, H16 |
| 3 | Repeticiones | Con reservas | Con reservas | Con reservas | H2, H3, H20 |
| 4 | Orden | No cumple | No cumple | No cumple | H1 |
| 5 | Todo o nada | Con reservas | Con reservas | Con reservas | H8, H15, H31 |
| 6 | Cada uno lo suyo | Con reservas | Con reservas | Con reservas | H6 |
| 7 | Sin estado propio | Cumple | Con reservas | Con reservas | H32 y un [costo aceptado](#costos-aceptados-por-decisiones-documentadas) (la cola del robot) |
| 8 | Trabajos de fondo | Con reservas | Con reservas | No cumple | H4, H19, H20, H35 |
| 9 | Arranque, apagado y configuración | Con reservas | Con reservas | Con reservas | H7, H8, H21, H22, H23 |
| 10 | Despliegue sin corte | Con reservas | No cumple | No cumple | H2 |
| 11 | Consultas | Con reservas | Con reservas | No cumple | H34, H35, H36, H37 |
| 12 | Paginado y exportaciones | Cumple | Cumple | Cumple | — |
| 13 | Filtros y búsqueda | No aplica | No aplica | No aplica | Se consulta por clave o por CUIT exacta |
| 14 | Caché | Con reservas | Con reservas | Con reservas | H13, O1 |
| 15 | Trabajo pesado fuera del pedido | Con reservas | Con reservas | Con reservas | H8 |
| 16 | Filas únicas | Con reservas | Con reservas | No cumple | H33, H36 |
| 17 | Plazos y reintentos | No cumple | No cumple | No cumple | H4, H5, H10, H12, H18 |
| 18 | Estado incierto | No cumple | No cumple | No cumple | H1, H2, H3, H8, H9 |
| 19 | Degradación | Con reservas | Con reservas | Con reservas | H1, H7, H11, H13, H14, H17, H18, H19, H22, H30 |
| 20 | Límites y costo | No cumple | No cumple | No cumple | H4, H5, H32 |
| 21 | Acceso | Con reservas | Con reservas | Con reservas | H6 |
| 22 | Entradas hostiles | Con reservas | Con reservas | Con reservas | H9 |
| 23 | Datos sensibles | Con reservas | Con reservas | Con reservas | H21 |
| 24 | Integridad en la base | Con reservas | Con reservas | Con reservas | H27 |
| 25 | Privacidad y respaldo | No cumple | No cumple | No cumple | H3, H24, H25 |
| 26 | Idioma | Con reservas | Con reservas | Con reservas | H28 |
| 27 | Fechas, zonas horarias y números | Con reservas | Con reservas | Con reservas | H29 |
| 28 | Errores | Con reservas | Con reservas | Con reservas | H7, H12, H14, H19, H30 |
| 29 | Observabilidad | Con reservas | Con reservas | Con reservas | H26 |
| 30 | Separabilidad | Con reservas | Con reservas | Con reservas | H24 y el [inventario](#separar-el-módulo-inventario-no-defecto) |

## Hallazgos

Cada hallazgo dice dónde empieza a fallar, el escenario, la evidencia (archivo y línea en `23797fff`; en el front, en
`7b95095b`) y el arreglo con su esfuerzo. Cuando el comportamiento viene de una decisión documentada, se cita la fuente:
cambiarla lo decidís vos.

### Fuera de los criterios

#### B1. La factura A o B con precios sin IVA no se emite nunca

- **Dónde falla:** hoy, siempre: le pasa a toda empresa responsable inscripta que use «Factura» como viene y cargue un ítem
  gravado. Lo encontró el revisor del emisor y lo confirmó su verificador.
- **Escenario:** la empresa agrega «Factura» y deja «El precio incluye IVA» sin marcar, que es como viene. Carga una factura
  a un responsable inscripto con un ítem de $ 1.000 al 21 %. El «Total» del formulario es $ 1.000, porque suma cantidad por
  precio, pero la cola espera $ 1.210 y responde «El total no coincide con el de los ítems. Volvé a calcularlo.». En la app,
  «¿Emitimos la factura?» sólo ofrece «Volver a probar», que falla igual: la respuesta no se puede enviar. Por WhatsApp,
  que no muestra la alícuota y usa 21 %, falla toda A o B con la casilla sin marcar, y sólo queda «No emitir». La prueba a
  mano de la etapa 4 falla en el paso 4.
- **Evidencia:** el estándar calcula «Importe» como cantidad por precio y «Total» como la suma de los importes, con la casilla
  sin marcar (`ArcaInvoiceStandardSource.cs:47-56,76`), y el servidor calcula así, sin mirar el IVA
  (`FormRecordAnswerPolicy.cs:168-191`). El emisor pasa ese total como esperado (`ArcaInvoiceOrderPolicy.cs:56-57`) y la cola
  lo compara con neto más IVA (`ArcaVoucherOrderPolicy.cs:51-52`, `ArcaVatBreakdown.cs:21,73`), ya en la vista previa
  (`ArcaVoucherService.cs:46-49`). Los tests no lo ven: los del emisor usan una empresa monotributista
  (`ArcaVoucherScenario.cs:24-25`) y los de la A pasan a mano un total con IVA (`ArcaVoucherQueueTests.cs:366`,
  `ArcaVoucherOrderPolicyTests.cs:121-128`). Las reglas esperan lo contrario: el ejemplo de [formularios](../features/formularios.md)
  («$ 1.210,00 (Importe neto gravado: $ 1.000,00; IVA 21%: $ 210,00)»), la decisión 7 del
  [ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md) y la prueba a mano del
  [plan de la etapa 4](../plans/2026-11-09-arca-etapa-4-factura-a-y-b.md).
- **Arreglo:** comparar el total esperado con la suma de los importes de los renglones, que es lo que calcula el formulario en
  cualquier clase; corregir las dos pruebas que fijan el total con IVA y sumar una del emisor con una empresa responsable
  inscripta y el estándar, para una A y una B. Chico.

#### B2. «Emitir con la fecha de hoy» no corre el vencimiento del pago

- **Dónde falla:** hoy, raro: un servicio tiene que quedar más de 10 días en la cola (ARCA caído, el certificado vencido o la
  secuencia frenada).
- **Escenario:** un servicio del 01/11 con vencimiento del pago el 05/11 sale de la ventana y va a revisión. El 14/11 alguien
  elige «Emitir con la fecha de hoy»: la factura sale con fecha 14/11 y vencimiento 05/11, y ARCA la rechaza con el 10036.
  No frena la secuencia, pero no se emite y hay que corregir la respuesta a mano.
- **Evidencia:** la decisión sólo cambia la fecha (`ArcaVoucher.cs:173-176`) y el pedido sale con el vencimiento guardado
  (`ArcaVoucherRequestPolicy.cs:34,38`); al encolar sí se exige que no sea anterior a la fecha (`ArcaVoucherOrderPolicy.cs:71`),
  y la nota de crédito lo corre a hoy (`ArcaVoucherService.cs:89-92`). El período no importa: ARCA no lo ata a la fecha.
- **Arreglo:** con «Emitir con la fecha de hoy», correr a hoy un vencimiento ya pasado, como la nota de crédito, y decirlo en el
  texto de la opción. Chico.

#### B3. Una constancia con errores puede llevar a una B en vez de una A, y queda guardada

- **Dónde falla:** hoy, si ARCA devuelve una constancia con errores; cuántas veces pasa depende de ARCA y se confirma en la
  homologación.
- **Escenario:** la empresa, responsable inscripta, le factura a otro responsable inscripto escribiendo su CUIT. Si la
  constancia llega con datos generales pero con el error del régimen general («No cumple con las condiciones para enviar
  datos del régimen general», según el manual de la constancia), el tercero queda como consumidor final y la cola le emite una
  B en vez de una A, con efecto legal. Esa condición se guarda en «Clientes», así que la B se repite en las facturas
  siguientes. La propia empresa no emite mal: queda bloqueada, pero sin el motivo que da ARCA.
- **Evidencia:** el cliente lee los errores (`ArcaRegistryClient.cs:22,53`, `ArcaRegistryPerson.cs:7-8`), pero la condición sale
  sólo de los impuestos y el monotributo (`ArcaCompanyDataPolicy.cs:15,20-36`), y sin ellos queda consumidor final
  (`ArcaRegistryPartySource.cs:35`, `ArcaVoucherClassPolicy.cs:22-24`); el cliente vuelve a «Clientes» con esa condición
  (`ArcaStructuredRecordIssuer.cs:44-45,53`).
- **Arreglo:** con errores en el grupo que decide la condición frente al IVA, no inferir: responder un código propio y pedir
  que se revise. Chico.

#### B4. El estado de la conexión no sigue al certificado vencido ni a un servicio nuevo

- **Dónde falla:** hoy, una vez por certificado y cada vez que se suma un servicio.
- **Escenario:** el certificado vence el sábado. El lunes la insignia sigue «Activa» en verde, aunque el front muestra el aviso
  rojo de vencido, y las facturas esperan en la cola con «ARCA no respondió». Al prender Facturación sin punto de venta, la
  conexión sigue «Activa» hasta la próxima prueba. El estado sólo se muestra: no habilita ni frena ninguna factura.
- **Evidencia:** `CertificateExpired()` (`ArcaConnection.cs:102-106`) sólo la llama un test; nadie mira el vencimiento al
  entregar un ticket o al leer (`ArcaServiceSessionIssuer.cs:30-38`, `ArcaConnectionReader.cs:11-40`); elegir servicios no
  recalcula (`ArcaConnection.cs:66-75`) y el front no vuelve a probar (`ActiveSections.tsx:61-71` en el front). Las fuentes se
  contradicen: [ARCA](../features/arca.md) dice que al elegir servicios «la pantalla vuelve a probar», y el diseño del front
  fijó que no (`docs/design/arca-conexion.md:36-39` del front).
- **Arreglo:** calcular el vencimiento al leer y cortar con `CertificateExpired` antes de llamar a ARCA; antes, corregir
  `CertificateExpired()`, que lleva «sin verificar» a «requiere atención» contra la regla del alta. Para los servicios, ver la
  [decisión 5](#decisiones-del-2026-11-14). Chico.

#### B5. Con más de 200 clientes, la «Factura» deja de poder cargarse por WhatsApp

- **Dónde falla:** hoy, en un negocio que factura por WhatsApp y llega a 200 clientes; lo encontró un verificador y se
  confirmó leyendo el código.
- **Escenario:** cada CUIT nueva que se factura con «Otro» se suma sola a «Clientes» (H25). Un Flow de WhatsApp muestra hasta
  200 opciones para elegir una: con el cliente 201, la «Factura» deja de poder mandarse por WhatsApp con
  `Forms.Record.CatalogTooLarge`. Ningún documento de ARCA lo menciona.
- **Evidencia:** el tope de 200 (`FormCatalogOfferPolicy.cs:26`, [formularios](../features/formularios.md), «El tope es 200
  opciones para elegir una»); el alta automática en «Clientes» (`FormIssuedCatalogItemRecorder.cs:22-57`).
- **Arreglo:** que el estándar elija primero por un campo de «Clientes» (por ejemplo, la inicial), como ya permite formularios,
  o que por WhatsApp se escriba la CUIT; y escribir el límite en [ARCA](../features/arca.md). Chico.

### Hoy, con una sola copia

#### H1. Una fecha anterior a la del último comprobante traba la secuencia para siempre

- **Dónde falla:** hoy, cada vez que alguien carga una venta con una fecha anterior a la de la última factura del mismo tipo y
  punto de venta; es común al ponerse al día con las ventas del fin de semana.
- **Escenario:** el lunes 10 sale la factura B 0001-00000100 con fecha 10/11. A la tarde alguien carga una venta del sábado con
  fecha 08/11, que la ventana de 5 días deja pasar. La cola le da el 101 y ARCA contesta 10016: según el manual de WSFEv1, ese
  código cubre también una fecha anterior a la del último comprobante y una fuera de la ventana, no sólo un número equivocado.
  La cola lo toma como incierto, consulta, ve que ARCA no tiene el 101 y que el último sigue siendo el 100, y lo manda otra
  vez igual. Así para siempre, cada vez más espaciado hasta una vez por hora, sin pasar nunca a revisión. Nadie puede
  cancelarlo ni anularlo desde la app, y desde ese momento no sale ninguna factura B: quien cargó la 101 recibe «ARCA no
  responde. Tu factura sale apenas vuelva.», que es falso, y los demás ven «Emitiendo» sin plazo. Destrabarlo pide tocar la
  base.
- **Evidencia:** el 10016 queda incierto (`ArcaInvoicingClient.cs:164-165,178-181`) con su número y su pendiente
  (`ArcaVoucherStore.cs:110,164-166`); la reconciliación decide «no se emitió» y lo reenvía (`ArcaReconciliationPolicy.cs:14`,
  `ArcaVoucherStore.cs:120-122`); la ventana se mira sólo sin número (`ArcaVoucherStore.cs:87`) y el número se da sin mirar
  fechas (`:108-110`); cancelar exige no tener número (`ArcaVoucher.cs:183-185`) y revisar, estar en revisión (`:161`). El doble
  de ARCA da 10016 sólo por el número (`FakeArcaVouchers.cs:129-144`): ningún test lo cubre. El mismo camino reenvía para
  siempre un incierto cuya fecha salió de la ventana, por ejemplo si después se vence el certificado y se renueva a los seis
  días. El [ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md) trata el 10016 sólo como un número equivocado (§5) y
  manda a revisión lo que sale de la ventana (§6), que con número no se cumple.
- **Arreglo:** cuando el reenvío vuelve con 10016 y la consulta dice que ARCA no lo tiene, ir a revisión liberando el número,
  con un motivo y un texto propios («anterior a la del último comprobante»); avisar antes, al encolar y en «¿La emito?»,
  comparando con la última fecha autorizada de la secuencia; que «Emitir con la fecha de hoy» use la mayor entre hoy y la del
  último y corra el vencimiento del pago (B2), y que el doble aprenda las dos reglas de fecha. Mediano. Ver la
  [decisión 2](#decisiones-del-2026-11-14).

#### H2. Una versión puede mandarle a ARCA otro comprobante que el confirmado

- **Dónde falla:** con una sola copia, en cada despliegue que cambie el cálculo o el pedido, durante el rato en que conviven las
  dos versiones, y al volver a la imagen anterior; con varias copias, durante todo un despliegue gradual.
- **Escenario:** una versión nueva agrega al pedido un dato que mueve los importes (un descuento, percepciones) o corrige un
  redondeo. Mientras conviven, la copia vieja toma una factura que encoló la nueva, arma el pedido de nuevo con su código,
  ignora lo que no conoce y le pide a ARCA un comprobante distinto del que se confirmó en «¿La emito?»: sólo se corrige con
  una nota de crédito, y el PDF y el QR no coinciden con ARCA. Peor: si la respuesta de la primera versión se perdió y la otra
  calcula distinto, la reconciliación toma la factura que ARCA sí emitió como «número tomado con otros datos», y la revisión
  ofrece primero «Emitir con el número siguiente»: dos facturas. Un valor nuevo de una enumeración, en cambio, no manda nada
  equivocado: EF lanza al leerlo y esa secuencia queda trabada en la copia vieja mientras conviven; la verificación lo refutó
  como camino de una emisión distinta.
- **Evidencia:** lo fijado al encolar no tiene los importes ni una versión (`ArcaVoucherPayload.cs:8-16`,
  `ArcaVoucherService.cs:64-73`), aunque el plan de la etapa 3 pedía fijarlos; cada intento rearma el pedido
  (`ArcaVoucherStore.cs:93`) con un JSON que ignora lo desconocido (`ArcaVoucherRequestPolicy.cs:20,24-25`) y recalcula los
  importes (`:35,74`); la huella `DataHash` se escribe y nunca se lee (`ArcaVoucher.cs:108`); la reconciliación compara con el
  total rearmado (`ArcaReconciliationPolicy.cs:17-24`) y el PDF recalcula los subtotales (`ArcaFiscalDocumentPolicy.cs:61`).
  El ADR 0028 pide «datos fijados» y la huella justamente para esto. Formularios ya lee sus codecs de forma estricta
  (`FormDocumentRowCodec.cs:28`).
- **Arreglo:** fijar al encolar el pedido ya armado (importes, alícuotas, fechas y códigos) y que el worker sólo ponga el
  número; una versión en el JSON, para que el worker posponga lo que no entiende; que la reconciliación y el PDF usen lo
  fijado; y, como red inmediata, no mandar si el total armado difiere del guardado. Mediano.

#### H3. Restaurar la base deja a ARCA adelante: se borran PDF fiscales y una venta puede facturarse dos veces

- **Dónde falla:** hoy, en una restauración de la base a un momento dado; es raro, pero el efecto es legal.
- **Escenario:** el martes se restaura la base al lunes 12:00. Entre el lunes 12:00 y el martes, ARCA autorizó diez facturas con
  su PDF, que la base restaurada no tiene. Entre 24 y 25 horas después de escrito cada PDF, la limpieza lo borra, porque ningún
  comprobante lo reclama: quedan sólo en ARCA y en el servidor viejo, si no se borró. Además, si a las 12:00 una de esas
  emisiones estaba en la cola sin número, o la acción de formularios todavía no la había encolado, al prender los workers la
  cola le da el número siguiente al último de ARCA: la misma venta queda facturada dos veces. Lo que ya tenía número no se
  duplica: se concilia con ARCA.
- **Evidencia:** la retención reclama un PDF sólo si existe su fila (`ArcaPrivateFileRetentionSource.cs:20-27`); la limpieza corre
  cada hora y borra lo no reclamado con más de un día (`PrivateFileCleanupBackgroundService.cs:11-18`,
  `PrivateFileCleanupService.cs:45-47,59-63`); la cola numera con el último de ARCA más 1 sin compararlo con lo que conoce la
  base (`ArcaVoucherProcessor.cs:124-133`, `ArcaVoucherStore.cs:98-111`), y la acción vuelve a encolar con la misma clave, que la
  base ya no tiene (`FormIssueActionService.cs:24-36`, `ArcaStructuredRecordIssuer.cs:69-71`). La guía de restauración sólo nombra
  el interruptor de la cola (`docs/deploy/azure-setup.md:97-116`), y el ADR 0028 no contempla la restauración.
- **Arreglo:** que la retención guarde todo `arca-vouchers/` sin mirar la base (una línea); que, al numerar, si ARCA tiene un
  último autorizado mayor que el último que la base conoce para esa secuencia, el comprobante vaya a revisión con un motivo
  propio, salvo cuando la base no conoce ningún número de la secuencia (mediano); y un apartado de ARCA en la restauración:
  levantar la copia con la cola y las acciones de formularios apagadas, avisar que la limpieza sigue corriendo y borra a las 24
  horas todo objeto sin fila, comparar el último autorizado de cada punto de venta con la base y recuperar del servidor original
  lo que falte antes de prenderlas (chico). Ver la [decisión 4](#decisiones-del-2026-11-14).

#### H4. Las tablas de ARCA piden un ticket de wsfe cada cinco minutos para un servicio que no se eligió

- **Dónde falla:** hoy, en una conexión que no eligió Facturación o que todavía no autorizó wsfe en ARCA, algo que puede durar
  días.
- **Escenario:** las cinco tablas de ARCA nunca se leyeron, así que no existen como catálogos. Cada cinco minutos el worker de
  formularios las da por vencidas y, por cada una, pide al WSAA un ticket de wsfe, que lo rechaza. Nada queda anotado y a los
  cinco minutos repite: unos 1.440 pedidos firmados por día y por copia, para un servicio que la empresa no eligió; con el WSAA
  caído, 20 s cada uno.
- **Evidencia:** un catálogo que no existe siempre está vencido y su falla no se anota (`FormCatalogFeedService.cs:35,77-79`),
  cada 5 minutos (`FormCatalogFeedWorkerOptions.cs:7`); el feed pide el ticket sin mirar los servicios elegidos
  (`ArcaStructuredCatalogFeed.cs:25`, `ArcaServiceSessionIssuer.cs:32-36`); un «sin autorizar» no deja nada guardado
  (`ArcaAccessTicketProvider.cs:53-59`). Va contra el [ADR 0025](../decisions/0025-familias-de-servicios-arca.md) (decisión 4:
  sólo se autorizan los servicios elegidos).
- **Arreglo:** que el feed de ARCA devuelva un código sin llamar si wsfe no está elegido o la conexión no está activa. Chico.

#### H5. El robot vuelve a probar la clave rechazada de la plataforma

- **Dónde falla:** hoy, con el robot prendido.
- **Escenario:** el dueño de la plataforma cambió su clave fiscal en ARCA y no actualizó la configuración. Una empresa arranca
  el robot: ARCA rechaza el ingreso de la plataforma, ese lado se frena y sale un correo. La persona de la empresa vuelve a
  tocar «Delegar con el robot», o termina a mano: cada vez, el robot vuelve a entrar con la clave vieja. Unos pocos rechazos
  alcanzan para que ARCA bloquee esa clave fiscal, que según el plan de la etapa 6 es una sola para todas las instalaciones y
  es la de una persona para todos sus trámites. El dueño recibe un solo correo por día.
- **Evidencia:** arrancar el cliente y terminar a mano llaman a `Reschedule` (`ArcaRobotRunStore.cs:62,118`), que rearma
  también un lado frenado por ingreso rechazado (`ArcaRobotRun.cs:106-115`), y un test fija ese rearme
  (`ArcaRobotRunTests.cs:208-229`). Dicen lo contrario [ARCA](../features/arca.md) («un ingreso rechazado no se reintenta para
  no bloquear la clave»), el [ADR 0026](../decisions/0026-robot-de-delegacion-arca.md) y la [guía del robot](../guides/robot-arca.md).
- **Arreglo:** no rearmar un lado de la plataforma frenado por ingreso rechazado, salvo una vez después de reiniciar con la
  clave nueva o con una acción explícita. Chico.

#### H6. Poner a emitir un formulario no pide ningún permiso de ARCA

- **Dónde falla:** hoy, con un rol que tenga «Administrar formularios» y «Publicar formularios» (o dos personas que se los
  repartan) y ningún permiso de ARCA.
- **Escenario:** alguien agrega «Factura» desde la biblioteca y la publica. La copia trae activa la regla «Cuando llega: Emitir
  comprobante en ARCA», y desde ese momento cualquiera con «Completar formularios» emite comprobantes con valor legal, en la
  app o por WhatsApp. Para deshacer uno, la nota de crédito, hace falta `arca.issue`; para poner a emitir el formulario, ni
  siquiera «Administrar reglas y PDF».
- **Evidencia:** agregar pide sólo ver y administrar formularios (`FormLibraryController.cs:17,28`, `FormLibraryService.cs:66`)
  y deja activas las reglas del estándar (`FormLibraryService.cs:104-109`, `FormStandardRuleRecorder.cs:39-46`,
  `ArcaInvoiceStandardSource.cs:105-121`); cargar pide sólo «Completar formularios» (`FormEntriesController.cs:17`); anular sí
  pide el permiso del emisor (`FormIssueReversalRecorder.cs:28`). El [plan de la etapa 3](../plans/2026-11-09-arca-etapa-3-factura-c.md)
  acepta que cargar la «Factura» no pida `arca.issue`, pero exige `formrules.manage` para configurar la acción, y la biblioteca
  la configura sin él.
- **Arreglo:** pedir `formrules.manage` y el permiso del emisor (`arca.issue`) a quien agrega un estándar con reglas que emiten o
  guarda una regla así, o dejar esas reglas sin activar al copiar. Chico. Ver la [decisión 3](#decisiones-del-2026-11-14).

#### H7. Una espera que no es de ARCA avisa «ARCA no responde», también con el CAE ya dado

- **Dónde falla:** hoy, con el almacén de archivos caído o sin configurar después de autorizar, o con la conexión que deja de
  servir (certificado vencido, delegación revocada, otro ambiente) antes de emitir.
- **Escenario:** vencen las credenciales del almacén. ARCA autoriza la 0001-00000123 con su CAE, pero el PDF no se puede
  guardar. Formularios recibe «pendiente», sin número ni CAE; a quien cargó le llega «ARCA no responde. Tu factura sale apenas
  vuelva.» y la pantalla dice «Todavía no se pudo emitir», con el código crudo `Files.Unavailable` en la actividad. El
  vendedor podría hacerla a mano en ARCA y quedarían dos facturas legales. Mientras falte el PDF, tampoco se puede anular: la
  anulación queda en curso hasta que el PDF exista, y sin almacén configurado, para siempre.
- **Evidencia:** posponer el PDF marca la espera como de ARCA (`ArcaVoucher.cs:197-211`) y guarda `Files.Unavailable`, un
  código sin declarar (`ArcaVoucherProcessor.cs:88-92`); el emisor responde pendiente (`ArcaInvoiceIssuePolicy.cs:36-52`) y
  formularios manda el aviso (`FormIssueActionService.cs:42-43`); sin almacén nada lo ataja al arrancar
  (`ArcaVoucherProcessor.cs:75`); anular espera el PDF (`FormIssueActionService.cs:52-60`). Que la emisión cuente como hecha
  recién con el PDF guardado es una decisión (ADR 0028, decisión 8); el aviso que culpa a ARCA, no.
- **Arreglo:** separar el motivo de la espera (ARCA, almacén o conexión) con su aviso; mostrar número y CAE mientras falta el
  PDF; declarar el código del almacén y exigir el almacén al arrancar con la facturación instalada; dejar anular una factura
  autorizada sin su PDF. Chico.

#### H8. Cortar el pedido de un ticket lo pierde y deja la reserva tomada

- **Dónde falla:** hoy, poco frecuente: el ticket de cada servicio se renueva una vez cada 12 horas, y basta con que el pedido se
  corte en ese segundo.
- **Escenario:** el ticket de Facturación vence. Mientras la app pide el nuevo al WSAA, la persona cierra la pestaña, el front
  cancela la vista previa de «¿La emito?» porque cambió una respuesta, o un despliegue reinicia la Api mientras la cola pide el
  suyo. ARCA pudo haber dado el ticket, pero la app no lo guarda y la reserva queda tomada 60 s: quien prueba o emite en ese
  rato espera hasta unos 80 s y probablemente recibe «ya posee un TA válido», que bloquea el servicio 2 minutos en producción
  o 10 en homologación y borra el ticket guardado que todavía servía. Si el WSAA se cae en los últimos 10 minutos de un ticket,
  todo falla aunque el ticket guardado sirva hasta vencer.
- **Evidencia:** el pedido al WSAA y el guardado usan el token de quien llama (`ArcaAccessTicketProvider.cs:53-60`), sin
  `try/finally` (`:25-63`); la prueba usa el del pedido HTTP (`ArcaConnectionController.cs:83`) y la cola, el del apagado; el
  front cancela la vista previa con cada cambio (`useEntryEditor.ts:69-72` en el front); el ticket guardado no se entrega
  dentro del margen (`ArcaAccessTicket.cs:32`) y bloquear lo borra (`:51`). El [ADR 0023](../decisions/0023-arca-como-modulo-opcional.md)
  (decisión 4) acepta perder un ticket de vez en cuando, no por una cancelación común.
- **Arreglo:** pedir al WSAA con un plazo propio (no el de quien llama ni el del apagado), guardar sin cancelación, soltar la
  reserva en un `finally` y entregar el ticket guardado mientras no venza, también al bloquear. Chico.

#### H9. Los importes no tienen tope antes de encolar

- **Dónde falla:** hoy, muy raro: un error de tipeo, como pegar una CUIT en «Cantidad».
- **Escenario:** alguien carga Cantidad 1.000.000 y Precio 10.000.000.000. La vista previa muestra un total de diez mil billones
  y deja emitir. Al encolar, la base rechaza el importe (la columna admite 13 enteros, como ARCA) y la acción queda «sin
  confirmar»: «Consultar» repite la falla, la respuesta no se puede corregir y hay que anularla y cargarla otra vez. En la A o
  la B, con importes cerca del máximo de `decimal`, la vista previa responde un error inesperado.
- **Evidencia:** sin tope en `ArcaVoucherOrderPolicy.cs:32,36,73-75` ni en la captura (`FormRecordAnswerPolicy.cs:219-223`);
  `numeric(15,2)` (`ArcaVoucherConfiguration.cs:28`); la excepción queda sin confirmar (`FormActionEffectService.cs:27-30`); el
  desborde, en `ArcaVatBreakdown.cs:21` y `ArcaInvoiceLine.cs:14`. El plan de la etapa 3 pedía «hasta 13 enteros».
- **Arreglo:** un tope para el total y cada renglón (9.999.999.999.999,99) y para cantidad y precio, antes de calcular el IVA,
  con un código propio. Chico.

#### H10. Una falla pasajera del padrón al tocar «Emitir» no se puede reintentar

- **Dónde falla:** hoy, con un cliente escrito por su CUIT («Otro»), si el padrón no responde o el ticket está bloqueado justo al
  tocar «Emitir».
- **Escenario:** por WhatsApp, alguien carga una factura con «Otro: escribir la CUIT»; el resumen sale y la persona toca
  «Emitir» media hora después, cuando la constancia ya no está en la caché (dura diez minutos). Si el padrón no responde, la
  acción termina fallida sin «Reintentar», aunque no se encoló nada y el texto dice «Probá de nuevo en unos minutos». Hay que
  corregir la respuesta, o anularla y cargarla otra vez.
- **Evidencia:** el rechazo sale sin `CanRetry` (`ArcaStructuredRecordIssuer.cs:39-40`), que por defecto es falso
  (`StructuredRecordIssueModels.cs:37`), y reintentar lo exige (`BusinessFormActionRun.cs:77`). Contradice
  [formularios](../features/formularios.md) («reintentable si no se encoló nada»).
- **Arreglo:** marcar reintentable ese rechazo. Una línea.

#### H11. Por WhatsApp, nadie se entera si la emisión falla después de «Emitir»

- **Dónde falla:** hoy, cada vez que una emisión pedida por WhatsApp termina fallida: un rechazo de ARCA (como el 10036 de B2) o
  del emisor (H10). Lo encontró un verificador y se confirmó leyendo el código.
- **Escenario:** después de «Emitir» no se contesta nada en el acto, porque el aviso con el PDF lo manda «Avisar a quien
  cargó». Si la emisión falla, ese aviso espera para siempre: quien cargó por WhatsApp nunca se entera, y en la app sólo lo ve
  quien abre la respuesta.
- **Evidencia:** una acción que depende de otra sólo corre si la anterior terminó completada o «no aplica»
  (`FormActionDispatchService.cs:24-28`); [formularios](../features/formularios.md) («después de «Emitir» no se contesta nada en
  el acto»).
- **Arreglo:** cuando la emisión termina fallida, avisar a quien cargó con el motivo en su idioma, como ya se hace cuando falla
  la vista previa. Chico. Es de formularios; conviene contrastarlo en su revisión.

#### H12. Un corte a mitad de la respuesta de ARCA sale como un error inesperado

- **Dónde falla:** hoy, raro: ARCA, o un proxy en el medio, corta después de mandar los encabezados.
- **Escenario:** en vez de «ARCA no respondió», probar la conexión, elegir el punto de venta o «Completar con ARCA» responden
  «Ocurrió un error inesperado». Si pasa al pedir el ticket, además queda la reserva (H8). En `FECAESolicitar` el comprobante
  no se pierde ni se repite: se concilia a los 2 minutos, cuando vence la reserva de la secuencia, sin anotar el motivo.
- **Evidencia:** `SoapEnvelope.cs:44,47,93` lee el cuerpo como stream y sólo atrapa su propio plazo y `HttpRequestException`
  (`:53,57`); un corte leyendo el cuerpo es una `IOException`, que sube y rompe el contrato de que los clientes de ARCA nunca
  lanzan (el comentario de la clase promete lo contrario, `:21-25`).
- **Arreglo:** atrapar `IOException` como `Unavailable`, con un modo del ARCA simulado que corte a mitad del cuerpo. Una línea.

#### H13. Dos pedidos de la misma CUIT con el padrón colgado pueden terminar en un error inesperado

- **Dónde falla:** hoy, con el padrón colgado; alcanza una sola persona que toca «Enviar» con el total todavía cargando.
- **Escenario:** dos pedidos de la misma CUIT llegan con menos de 5 s de diferencia mientras el padrón no contesta. La reserva de
  la caché (15 s) vence antes que el plazo de ARCA (20 s), el segundo vuelve a llamar y, cuando corta la espera de la caché
  (30 s), recibe un error inesperado en vez de «ARCA no respondió». Con Redis caído, «Completar con ARCA» y la vista previa con
  «Otro» fallan siempre: eso es el costo aceptado del [ADR 0009](../decisions/0009-redis-como-unico-cache-de-datos.md).
- **Evidencia:** `RedisCache.cs:87-89,176-178` corta con `TimeoutException`, que `ArcaRegistryCache.cs:20-24` no traduce;
  `RedisCacheOptions.cs:15,18` y `ArcaOptions.cs:9`. Que el segundo vuelva a llamar lo acepta
  [agregar caché](../guides/agregar-cache.md); el error inesperado, no.
- **Arreglo:** que el adaptador de la caché del padrón traduzca el corte a «ARCA no respondió». Chico.

#### H14. Toda falla del padrón se informa como «ARCA no tiene datos de esa CUIT»

- **Dónde falla:** hoy.
- **Escenario:** el padrón contesta con una falla por otro motivo: un ticket rechazado, la CUIT representada sin autorización, un
  error interno. Quien toca «Completar con ARCA» lee «ARCA no tiene datos de esa CUIT en el padrón.» y revisa una CUIT que está
  bien, y la vista previa con «Otro» queda esperando con ese motivo.
- **Evidencia:** toda falla pasa a `registry.fault` (`ArcaRegistryClient.cs:38`) y de ahí a `PersonNotFound`
  (`ArcaRegistryPartySource.cs:32`), aunque `SoapEnvelope.cs:80-84` ya separa el texto; el test sólo cubre la CUIT inexistente.
- **Arreglo:** reconocer el «no existe» por el código o el texto de la falla y traducir las demás a «ARCA rechazó el pedido» o
  «ARCA no respondió». Chico.

#### H15. Tres guardados no confirman que la conexión siga siendo la que se leyó

- **Dónde falla:** hoy, raro: hacen falta dos administradores a la vez, o una pantalla vieja.
- **Escenario:**
  - **La prueba.** Ana prueba con sólo la constancia. Mientras corre, Beto prende Facturación sin punto de venta y prueba: la
    conexión queda «requiere atención». La prueba de Ana termina después y guarda «activa», con Facturación sin configurar ni
    probar. Al revés, una prueba vieja de un servicio que ya se quitó deja «requiere atención» sin un motivo visible.
  - **El punto de venta.** En el alta, un administrador elige el punto 3 y el servidor lo consulta en ARCA con la conexión de
    homologación; en esos segundos otro desconecta y empieza una de producción. El guardado toma la conexión nueva, le crea la
    configuración y le guarda el punto 3, que nadie verificó en producción. Además, guardar y recalcular el estado van en dos
    transacciones.
  - **Una pantalla vieja.** Toda conexión nueva nace con revisión 1 y las escrituras comparan sólo la revisión. Si alguien tiene
    la pantalla de hace un rato y, mientras tanto, otra persona desconectó y empezó otra conexión que llegó al mismo paso,
    «Desconectar» o «Cambiar» en el alta borran la conexión nueva con su clave privada, y «Generar el pedido» le reemplaza el
    pedido pendiente. Lo encontró un verificador y se confirmó leyendo el código.
- **Evidencia:** la prueba compara sólo la conexión y el certificado al guardar (`ArcaConnectionTestService.cs:28,42,60-66,75`);
  el punto de venta se guarda en la conexión que haya (`ArcaInvoicingSetupService.cs:53-60,86`, `ArcaInvoicingSettingsGuard.cs:17-23`),
  a diferencia de los datos de la empresa (`ArcaRegistryCompanyService.cs:32-34`); `Revision = 1` en cada alta
  (`ArcaConnection.cs:145`) y las escrituras comparan sólo la revisión (`ArcaConnectionService.cs:83,121-127`). Las dos
  transacciones están en el plan de la etapa 1, contra el [ADR 0001](../decisions/0001-transaccion-explicita-por-caso-de-uso.md).
- **Arreglo:** que las escrituras comparen también el id de la conexión; que la prueba y el punto de venta den 409 si cambió la
  conexión o sus servicios y calculen la completitud bajo el lock; y recalcular el estado en la misma transacción. Chico a
  mediano.

#### H16. Anular mientras la cola espera a ARCA frena la secuencia dos minutos

- **Dónde falla:** hoy, raro: la anulación tiene que caer en los 20 a 40 s en que ARCA o el ticket no responden.
- **Escenario:** la cola tomó una factura sin número y espera el último autorizado. La persona anula la respuesta y la emisión
  queda cancelada. ARCA no contesta: posponer una emisión cancelada lanza un error, la transacción se deshace y la reserva queda
  a nombre de un dueño que ya no existe. Las facturas de ese tipo salen unos 2 minutos más tarde; no se emite de más ni se
  pierde nada.
- **Evidencia:** cancelar no mira la reserva de la secuencia (`ArcaVoucherReviewService.cs:54-60`); posponer exige una emisión en
  cola o incierta (`ArcaVoucherStore.cs:37-39,171-175`, `ArcaVoucher.cs:144-148`); el caso con respuesta ya está previsto
  (`ArcaVoucherStore.cs:101-106`).
- **Arreglo:** en el paso de esperar, si la emisión ya no está en cola, incierta ni enviándose, sólo soltar la reserva, con su
  test. Una línea.

#### H17. Un PDF que no se puede armar deja sin PDF a los que siguen

- **Dónde falla:** hoy, con un error al armar el PDF que tengan todas las copias o al volver a la imagen anterior; en un
  despliegue normal, unos segundos.
- **Escenario:** la versión nueva admite una alícuota que la vieja no conoce, o hay un error de programación al armar el
  documento. La copia que lo toma falla antes del `try`: el comprobante no se pospone, queda primero en la lista y corta la
  vuelta de los PDF cada 2 s. Todo lo que se autoriza después queda sin PDF, sin emitirse para formularios. Si lo que falla es
  el dibujo, sí se pospone, pero con el motivo falso del almacén (H7).
- **Evidencia:** sin `try` por comprobante (`ArcaVoucherProcessor.cs:62`) y el armado fuera del `try` de las líneas 73 a 92
  (`:71`); se lanza con una alícuota desconocida (`ArcaFiscalDocumentPolicy.cs:127-128`, `ArcaVoucherRequestPolicy.cs:75`); el que
  falla queda primero (`ArcaVoucherReader.cs:27`).
- **Arreglo:** armar dentro del `try`, un `try` por comprobante y posponer con su propio código. Chico.

#### H18. Robot: una ARCA lenta se lee como «pantalla cambiada»

- **Dónde falla:** hoy, con el robot prendido, un día en que ARCA tarde más de 45 s en una acción.
- **Escenario:** ARCA tarda más que el plazo por acción en abrir la página de ingreso o en contestar «Siguiente». El robot no lo
  toma como «ARCA no responde», que la plataforma reintenta con espera creciente, sino como «pantalla cambiada»: frena al primer
  intento, el dueño recibe un correo que le pide revisar una pantalla que no cambió y la empresa ve la guía con «una pantalla de
  ARCA no era la esperada».
- **Evidencia:** el plazo vencido pasa a `ScreenChanged` (`ArcaRobotSession.cs:144-148,174-178`); sólo un 5xx del ingreso, un
  error de red o el plazo total dan `Unavailable` (`:37,179-182`, `PlaywrightArcaDelegationRobot.cs:149-153`); reintentan sólo
  `Unavailable` e `Interrupted` (`ArcaRobotRunStore.cs:165-169`). La regla y el plan de la etapa 6 separan «ARCA no responde» de
  «pantalla cambiada».
- **Arreglo:** que el plazo vencido de una navegación y un 5xx en cualquier página sean «ARCA no responde», y «pantalla
  cambiada» quede para una página cargada a la que le falta lo esperado. Chico.

#### H19. Robot: la plataforma se rinde sin avisar, o reintenta sin fin

- **Dónde falla:** hoy, con el robot prendido.
- **Escenario:** la empresa terminó su lado. Del lado de la plataforma, ARCA no responde media hora: el robot reintenta cinco veces
  y queda frenado por «ARCA no respondió», sin correo, como fija RB4-A, mientras la pantalla de la empresa dice «La plataforma
  tiene que terminar este paso a mano; ya le avisamos», también sin `AlertEmail` configurado. Nadie se entera de que falta
  aceptar y asignar. Si en cambio la pasada se interrumpe (un host que se cae, una reserva que vence), nunca se frena: se
  reintenta sin límite.
- **Evidencia:** el aviso no sale por «ARCA no respondió» ni «interrumpida», ni sin dirección (`ArcaRobotAlertPolicy.cs:30-32,41`);
  el texto no mira el motivo (`RobotZone.tsx:102,108` y `arcaRobot.json:48` en el front); `TryReserve` suma intentos sin tope
  (`ArcaRobotRun.cs:133-142`; el tope sólo vale en `ArcaRobotRunStore.cs:165`) y `Fail` no se aplica con la reserva vencida.
- **Arreglo:** que la pantalla prometa el aviso sólo cuando sale, que `TryReserve` respete `MaxAttempts` y avisar cuando la
  plataforma queda frenada para siempre, también por «ARCA no respondió». Chico. Ver la [decisión 8](#decisiones-del-2026-11-14).

#### H20. Robot: la reserva no acompaña a un paso largo ni a una corrida que espera

- **Dónde falla:** hoy, con ARCA lenta; con varias copias, además, dos sesiones a la vez con la clave de la plataforma.
- **Escenario:** con ARCA lenta pero dentro de los 45 s por acción, «Aceptar la delegación» dura más que los 3 minutos de la
  reserva, que sólo se renueva entre pasos. El paso se hace en ARCA pero no se guarda (se pierde el punto de venta que dejó) y
  la pantalla dice «el servidor se reinició» sin que nada se haya reiniciado. Con dos copias, la otra toma el lado vencido y
  entra con la misma clave mientras la primera sigue. Del lado del cliente, una corrida que espera en la cola deja correr su
  reserva y puede arrancar vencida.
- **Evidencia:** la reserva tiene que durar más que el paso más largo (`ArcaRobotOptions.cs:18-19`), pero sólo se exigen 30 s
  (`:30-32`) y no se compara con `StepTimeout` (`ArcaRobotBrowserOptions.cs:43-45`); se renueva sólo entre pasos
  (`PlaywrightArcaDelegationRobot.cs:283-290`, `ArcaRobotRun.cs:144-160`); otro host toma la vencida (`ArcaRobotRunReader.cs:23`,
  `ArcaRobotRun.cs:133-142`); la cola no la renueva (`ArcaCompanyRobotQueue.cs:21`).
- **Arreglo:** un latido que renueve la reserva mientras corre un paso, validar la reserva contra `StepTimeout` al arrancar (H23)
  y renovar la del cliente al sacarlo de la cola. Mediano.

#### H21. Robot: la clave fiscal vive toda la pasada, y `DEBUG=*` pasa el chequeo

- **Dónde falla:** hoy, con el robot prendido.
- **Escenario:** (a) una pasada del cliente dura varios minutos y la clave sigue alcanzable en memoria hasta el final, y antes, en
  la cola; no se suelta cuando ARCA acepta el ingreso, como promete el ADR 0026, y un volcado del proceso la tendría. (b)
  Alguien deja `DEBUG=*` en el contenedor, un valor común para depurar herramientas de Node: el chequeo sólo busca «pw», la Api
  arranca y el driver de Playwright podría escribir en los logs lo que se teclea, también las dos claves.
- **Evidencia:** (a) el campo es `readonly` y nada lo suelta (`ArcaFiscalKey.cs:9,14`); la retienen la clausura
  (`PlaywrightArcaDelegationRobot.cs:26-45`), el bucle (`ArcaRobotBackgroundServices.cs:14-19`) y el canal
  (`ArcaCompanyRobotQueue.cs:14-21`). (b) `ArcaRobotBrowserCheck.cs:7-11,17-19`. El ADR 0026 acepta que .NET no borra un
  `string`, pero promete que la clave viva lo menos posible.
- **Arreglo:** credenciales que entregan la clave una sola vez y quedan vacías, sin que el bucle ni el canal la retengan; rechazar
  al arrancar cualquier `DEBUG` con `*` o `pw`. Chico.

#### H22. Robot: dos Chromium por host sin medir, y un driver caído que no vuelve

- **Dónde falla:** hoy, con el robot prendido.
- **Escenario:** al arrancar el robot corren a la vez la pasada del cliente y la de la plataforma, cada una con su Chromium, en el
  mismo contenedor que atiende la Api; nada dice cuánta memoria y CPU hace falta, y el despliegue de desarrollo da 0,25 vCPU y
  0,5 GiB. Si el sistema mata al driver de Playwright, la instancia guardada no se recrea: todo da «ARCA no responde», sin
  correo, hasta reiniciar la Api. Un Chromium sin sus bibliotecas pasa el chequeo de arranque, que sólo mira que el archivo
  exista.
- **Evidencia:** los dos lados arrancan juntos (`ArcaRobotRunStore.cs:117-119`), un Chromium por pasada
  (`PlaywrightArcaDelegationRobot.cs:128`), el driver se crea una vez y no se rehace (`:162-174`), `ArcaRobotBrowserCheck.cs:21-23`
  y [la guía del robot](../guides/robot-arca.md).
- **Arreglo:** medir y documentar memoria y CPU de una corrida en la imagen publicada; si no entra, un lado por vez por host;
  recrear el driver cuando se cae; que el chequeo abra y cierre Chromium. Mediano.

#### H23. Las opciones que dependen unas de otras no se validan juntas

- **Dónde falla:** hoy, si alguien cambia los valores de fábrica, que andan; en el primer despliegue a Production; y en cada
  rotación del anillo.
- **Escenario:**
  - **Reservas más cortas que los pedidos.** Alguien baja `Arca:TicketLease` a 15 s con `RequestTimeout` en 20 s: un día el WSAA
    tarda 18 s, otro pedido toma la reserva vencida, el ticket del primero se usa sin guardarse y el segundo puede dejar el
    servicio bloqueado. Si en cambio sube `RequestTimeout` a 90 s porque ARCA anda lenta, la reserva de la cola (2 minutos) ya
    no cubre una vuelta: no emite dos veces, porque cada paso exige su reserva, pero el CAE recibido no se guarda (se recupera
    conciliando), la espera creciente se pierde y la cola golpea a ARCA cada 2 s sin avanzar. Lo mismo pasa con la reserva del
    robot frente a su paso más largo (H20).
  - **El anillo en la guía de despliegue.** Quien sigue la configuración obligatoria de Production no encuentra
    `Arca:SecretProtection`, y la Api no arranca sin él.
  - **La rotación a ciegas.** La clave privada queda cifrada con la clave del anillo que estaba activa al pedir el certificado,
    hasta el próximo certificado, que puede tardar años. Nada dice cómo ver qué clave usa: si se retira antes de tiempo, la
    emisión se frena hasta subir otro certificado y repetir el alta en ARCA.
- **Evidencia:** cada opción se valida por su lado (`ArcaOptions.cs:9,14-15`, `ArcaVoucherWorkerOptions.cs:4-5,11,14`,
  `ArcaInfrastructureRegistration.cs:44-47,88-89`); la regla de «dos pedidos seguidos» sólo está escrita
  ([configurar ARCA](../guides/configurar-arca.md)); [despliegue](../guides/despliegue.md) pide el anillo de IA y no el de ARCA,
  que `Program.cs:49-51` registra siempre; cada valor cifrado guarda el id de su clave en claro
  (`ArcaCredentialService.cs:130,141-143`), pero sólo la [guía de IA](../guides/configurar-ia.md) explica cómo consultarlo. El
  ADR 0023 acepta que rotar no recifre.
- **Arreglo:** validar al arrancar las reservas contra los plazos (la del ticket mayor que un plazo con margen, la de la cola
  mayor que dos y la del robot mayor que cuatro `StepTimeout`); la fila de `Arca:SecretProtection` en la guía de despliegue; y en
  [configurar ARCA](../guides/configurar-arca.md) el paso a paso de la rotación, con la consulta del id de clave, como el de IA.
  Chico.

#### H24. Quitar ARCA, o su facturación, deja que la limpieza borre los PDF fiscales

- **Dónde falla:** hoy, una vez, en un derivado que quita el módulo o la familia después de haber emitido.
- **Escenario:** alguien sigue [quitar ARCA](../guides/quitar-arca.md), respalda la base y despliega. Una hora después de
  arrancar, la limpieza borra todos los `arca-vouchers/*.pdf`, que ya tienen más de un día: sin el módulo nadie los retiene, y
  tampoco hay quién vuelva a dibujarlos. Si quita sólo la familia `Invoicing`, la guía ni los nombra.
- **Evidencia:** la única que retiene esos PDF es `ArcaPrivateFileRetentionSource`, que se va con el módulo o con la familia; la
  limpieza es del núcleo (`FileStorageRegistration.cs:15`) y borra lo que nadie reclama (`PrivateFileCleanupService.cs:45,59-63`).
  La guía dice que los PDF siguen en el almacén y que la limpieza «deja de retenerlos», sin un paso para copiarlos
  (`quitar-arca.md:8-9`); la sección de la familia no los nombra (`:162-213`), y el ADR 0028 cree que quedan («la guía avisa que
  los PDF fiscales quedan en el almacén»).
- **Arreglo:** un paso en las dos secciones de la guía, antes de desplegar, para copiar `arca-vouchers/` fuera del alcance de la
  limpieza, con el aviso de que se borran una hora después de arrancar; corregir la frase del ADR 0028 y «siete tablas», que son
  ocho. Chico.

#### H25. Los datos personales de receptores y clientes no tienen plazo ni camino de borrado

- **Dónde falla:** hoy, en cuanto se facture a consumidores identificados o a personas humanas.
- **Escenario:** un consumidor identificado con su DNI pide que borren sus datos. Su DNI, nombre y domicilio están en
  `ArcaVouchers` (en columnas y en el pedido guardado, en claro), en el PDF fiscal y en el resultado de la acción de formularios,
  y no hay regla que diga cuánto se guardan, que la obligación fiscal prevalece sobre un pedido de supresión ni cómo se purgan al
  vencer: hoy sólo se podría contestar con SQL a mano. Además, cuando se le factura a alguien escribiendo su CUIT en «Otro», su
  nombre, CUIT, condición y domicilio fiscal (en una persona humana, muchas veces el de su casa) se suman a «Clientes» al
  encolar, antes de que ARCA autorice, y sin plazo: ni anular, ni cancelar, ni un rechazo de ARCA los sacan. En las listas se ve
  sólo el nombre; la CUIT y el domicilio, con «Ver catálogos» y en el detalle de las respuestas.
- **Evidencia:** `ArcaVoucher.cs:26-28` y `ArcaVoucherParty.cs:7` dentro de `PayloadJson` (`ArcaVoucherConfiguration.cs:39`); el
  resultado de la acción (`ArcaInvoiceIssuePolicy.cs:22,113`); sin borrado ni anonimización (`ArcaVoucherRepository.cs:12-38`); el
  cliente se suma al encolar (`ArcaStructuredRecordIssuer.cs:43-46,51-62`, `FormIssuedCatalogItemRecorder.cs:22-57`). Conservar
  los comprobantes al desconectar y sumar el cliente nuevo a «Clientes» son decisiones ([ARCA](../features/arca.md)); el plazo y
  la supresión, no. La retención de las respuestas de formularios también está pendiente ([planes](../plans/README.md)).
- **Arreglo:** escribir en [ARCA](../features/arca.md) que los comprobantes, su pedido y su PDF se conservan durante el plazo
  fiscal y que ese plazo prevalece sobre un pedido de supresión, qué se anonimiza al vencer y en qué orden, y sumar ARCA al
  procedimiento de borrado pendiente en [verificación y producción](../plans/verificacion-y-produccion.md) (chico); la purga al
  vencer, si se decide, es mediana. En «Clientes», sumar el cliente recién cuando la factura queda emitida (chico). Ver la
  [decisión 6](#decisiones-del-2026-11-14).

#### H26. Una emisión no se puede seguir de punta a punta, y no queda quién desconectó

- **Dónde falla:** hoy, cada vez que la facturación se frena.
- **Escenario:** un lunes las facturas no salen. El log repite cada 2 s que la secuencia del tipo 11 en el punto de venta 4 falló
  con `InvalidOperationException`, sin decir que el anillo no descifra la clave, ni qué comprobantes esperan, ni de qué
  formulario vienen; y si la causa es el ticket, el certificado o una fecha fuera de la ventana, no queda ningún log. El motivo
  está en cada fila y se ve abriendo una respuesta del formulario, pero no hay una vista de la cola, ni una métrica, ni un
  chequeo de salud. Otro día la conexión aparece borrada y no hay registro de quién desconectó; tampoco de quién subió un
  certificado o cambió el punto de venta.
- **Evidencia:** los logs nombran tipo, número y punto de venta, sin el id del comprobante ni su clave de pedido
  (`ArcaVoucherProcessor.cs:187-194`), y de las fallas sólo el tipo de excepción (`:47,90,196-200`,
  `ArcaVoucherBackgroundService.cs:21-31`); los caminos que posponen sin respuesta no registran nada (`ArcaVoucherProcessor.cs:100-105,126-131,141-146`,
  `ArcaVoucherStore.cs:80-91`); no hay `Meter` ni chequeo propio (`ServiceDefaults/Extensions.cs:46-51,82-85`), que sí tienen
  WhatsApp, IA y el caché; sólo `ArcaConnection` es `IAuditable` (`ArcaConnection.cs:13`) y desconectar borra la fila
  (`ArcaConnectionService.cs:71-75`); `OperationLog` no registra el usuario (`OperationLog.cs:18-44`).
- **Arreglo:** el id del comprobante, su clave de pedido y el código de la falla en los logs del worker, también en los caminos
  que hoy posponen callados, y el mensaje de las excepciones propias que no llevan datos personales (chico); `IAuditable` en el
  certificado y la configuración y un log con el usuario al desconectar (chico); una métrica de la cola con los pendientes, los
  que esperan una revisión y la antigüedad del más viejo, o un chequeo degradado (mediano).

#### H27. Una sola nota de crédito por factura depende sólo del lock

- **Dónde falla:** hoy no falla, ni con varias copias: el lock es de PostgreSQL. Fallaría con un camino nuevo que cree notas sin
  el lock, si se parte el lock global para escalar (H33), o con SQL a mano.
- **Escenario:** anular dos veces a la vez da una sola nota de crédito porque toda escritura toma el lock de la conexión y después
  mira si ya hay otra. Sin ese lock, nada en la base impide dos notas de crédito activas para la misma factura, y cada una se
  emitiría en ARCA, sin vuelta atrás. Las otras dos reglas de la cola, la clave del pedido y el número en su secuencia, tienen
  además su único en la base; ésta no.
- **Evidencia:** `ArcaVoucherService.cs:80-98` y `ArcaVoucherRepository.cs:32-34`; el índice de `CancelsVoucherId` no es único
  (`ArcaVoucherConfiguration.cs:20`). El test pide la segunda nota en secuencia, no a la vez (`ArcaVoucherQueueTests.cs:319-341`).
- **Arreglo:** un único parcial sobre `CancelsVoucherId` para las notas que no están rechazadas ni canceladas, con su migración y
  un test de persistencia. Chico.

#### H28. Los datos de la emisión llegan en español a quien usa la app en inglés

- **Dónde falla:** hoy, con cada emisión que vea alguien con la app en inglés; es de presentación.
- **Escenario:** un operador con la app en inglés ve «Invoice C» en «¿La emito?» y, ya emitida, «Voucher: Factura C»,
  «Document: Sin identificar» y «VAT condition: IVA Responsable Inscripto» en la zona «Comprobante» y en los avisos
  («Done: we issued the Factura C 00004-00000012»).
- **Evidencia:** el resultado guarda un solo valor por dato, con la etiqueta en los dos idiomas
  (`StructuredRecordResultModels.cs:11`); el documento y la condición salen en español de `ArcaFiscalDocumentPolicy.cs:27-32,44-54`,
  como decide [ARCA](../features/arca.md) («como en el PDF fiscal»); el nombre del comprobante, de `name["es"]`
  (`ArcaInvoiceIssuePolicy.cs:103,107`) sin una regla que lo diga, mientras la vista previa usa los dos idiomas (`:69`).
- **Arreglo:** decir en la regla que los nombres fiscales van en español, como en el PDF, y usar el español también en la vista
  previa, para que no cambie entre antes y después (una línea más el ajuste); traducirlos pediría que el resultado guarde los
  dos idiomas, en el núcleo, formularios y el front (mediano). Ver la [decisión 7](#decisiones-del-2026-11-14).

#### H29. En la app, los importes de la emisión salen sin dos decimales fijos

- **Dónde falla:** hoy; efecto chico.
- **Escenario:** una factura de $ 1.300,50 se ve en la app como «$ 1.300,5», en el diálogo y en el total mientras se carga, y una
  de $ 1.210 como «$ 1.210»; por WhatsApp el mismo resumen dice «$ 1.300,50» y el PDF fiscal «1.300,50». Un
  `{result:arca.invoice.total}` en un aviso o en la copia por correo sale «1.300,5». La región se escribe bien en todos lados;
  difieren los decimales, y los tableros elegidos muestran «$ 605.500,00».
- **Evidencia:** el servidor manda el valor canónico (`ArcaInvoiceIssuePolicy.cs:69,99`); el front lo escribe tal cual llega
  (`issuePreview.ts:7-9` y `shared/lib/decimal.ts:5-15` en el front), aunque su comentario promete «$ 1.210,00»; por canal,
  `FormIssueConfirmationPolicy.cs:43-45` usa dos decimales; el dato del resultado, `FormActionResultTokenPolicy.cs:64`.
- **Arreglo:** importes con dos decimales fijos en el front y en ese dato del resultado. Chico.

#### H30. Dos pantallas de ARCA no dicen por qué fallaron, o quedan viejas

- **Dónde falla:** hoy, en el front.
- **Escenario:**
  - **Puntos de venta.** ARCA no autorizó wsfe, el ticket está bloqueado o la persona pasó el límite: la zona dice siempre «No
    pudimos traer los puntos de venta.» y ofrece «Buscar», que no lo arregla. Un aviso general muestra el motivo de un 400 o un
    429, pero con ARCA sin responder dice «Algo salió mal».
  - **El alta.** Cambiar CUIT, ambiente o modo son tres pedidos sueltos (desconectar, pedir el certificado y elegir servicios).
    Si falla el segundo, o si otra persona ya desconectó, la pantalla sigue con la conexión vieja y cada reintento dice
    «Todavía no hay una conexión con ARCA.» sin volver a leer, hasta salir y volver.
- **Evidencia:** `PointOfSaleZone.tsx:25` descarta el error, mientras `useArcaAction.ts:26` muestra el detalle de un código
  `Arca.*`; `SetupZones.tsx:194-203` y `useArcaAction.ts:22`, que no trata `Arca.Connection.NotConnected` como un conflicto (todo
  en el front); cada pedido es su propia transacción (`ArcaConnectionService.cs:21-37,71-76`).
- **Arreglo:** mostrar el detalle de un código `Arca.*` en la zona; ante `NotConnected` o cualquier error después del borrado,
  volver a leer la conexión. Una línea cada uno; lo robusto para el alta es que reemplace la conexión en una sola transacción
  del servidor (mediano).

#### H31. Un cliente escrito por su CUIT puede no sumarse a «Clientes» si se corta el proceso

- **Dónde falla:** hoy; raro y de efecto chico.
- **Escenario:** el worker de acciones encola la factura de un cliente escrito por su CUIT y la copia se cae, o vence la reserva
  del run, antes de cerrarlo. Al retomar, el emisor ya tiene la emisión y responde sin el cliente: la factura sale, pero el
  cliente nunca se suma a «Clientes».
- **Evidencia:** el cliente vuelve sólo al encolar (`ArcaStructuredRecordIssuer.cs:45`) y la consulta no lo trae (`:69-71`); al
  retomar, la acción consulta (`FormIssueActionService.cs:24-36`).
- **Arreglo:** al retomar sin resultado, volver a pedir el encolado, que no duplica la factura y devuelve el cliente. Chico.

### Con varias copias

#### H32. Los límites de pedidos y el cupo del padrón son de cada copia

- **Escenario:** con tres copias detrás del balanceador, un operador con `arca.manage` puede probar o subir certificados 18 veces
  por minuto en vez de 6, y quien escribe CUIT en «Otro» consultar 18 CUIT distintas por minuto en vez de 6; tras cada
  despliegue, el cupo del padrón vuelve a cero, y no hay techo por conexión. Son lecturas, sin efecto legal ni costo. Durante un
  despliegue con una sola copia, el tope llega a 12 por minuto un rato.
- **Evidencia:** ventanas fijas en memoria por operador (`ArcaApiRegistration.cs:18-33`, `RateLimitingExtensions.cs:24-58`); el
  cupo del padrón vive en memoria por host (`ArcaRegistryQuota.cs:14-27`), como dice su interfaz (`IArcaRegistryQuota.cs:7`).
  [Configurar ARCA](../guides/configurar-arca.md) no dice que son por copia; la [guía de IA](../guides/configurar-ia.md) sí.
- **Arreglo:** documentarlo y dimensionar por copia, como en IA y en H26 de la [revisión de WhatsApp](2026-11-12-arquitectura-whatsapp.md),
  incluido el cupo del padrón. Un limitador compartido sobra: no protege dinero ni un efecto legal. Una línea.

Con varias copias empeoran además H2 (la convivencia de versiones dura todo el despliegue), H20 (dos sesiones con la clave de la
plataforma) y H4 (cada copia pide sus tickets).

### A gran escala

#### H33. La cola emite de a uno por secuencia, con 2 s de pausa aunque haya cola

- **Dónde falla:** a gran escala; con picos, ya se nota hoy.
- **Escenario:** un comercio con un solo punto de venta junta 3.000 ventas B en la hora pico. Cada vuelta emite una por secuencia
  y espera 2 s fijos: unas 13 a 18 por minuto, así que la última sale dos o tres horas después. Hoy, 300 facturas juntas tardan
  de 15 a 20 minutos. Sumar copias casi no ayuda con una sola secuencia, y la app admite un solo punto de venta por instalación.
  Detrás, del lado de formularios, cada paso de un run toma el candado global de formularios y el worker de acciones atiende de a
  uno por copia.
- **Evidencia:** `ArcaVoucherProcessor.cs:95-121`, `ArcaVoucherBackgroundService.cs:26`, `ArcaVoucherWorkerOptions.cs:10`; un
  punto de venta por instalación (`ArcaVoucherService.cs:59`); un solo candado para toda la instalación en cada encolado y cada
  paso (`ArcaLockKeys.cs:11`, `ArcaConnectionRepository.cs:11-12`), que ordena en fila las transacciones cortas y no los pedidos
  a ARCA; en formularios, `FormActionDispatchService.cs:23` y `FormActionBackgroundService.cs:16-41`. El
  [ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md) acepta una secuencia de a uno y descarta los lotes; la pausa con
  cola y el candado de toda la instalación no están decididos, y [formularios](../features/formularios.md) deja su candado global
  hasta medir.
- **Arreglo:** no esperar si la vuelta hizo trabajo (chico); para gran escala, más puntos de venta o lotes, que pedirían revisar el
  ADR 0028 (grande).

#### H34. Buscar los PDF pendientes recorre todos los autorizados

- **Escenario:** con un millón de comprobantes autorizados, todos con su PDF, cada 2 s y en cada copia la base recorre el millón
  para concluir que no falta ninguno.
- **Evidencia:** `ArcaVoucherReader.cs:24-30`; el único índice útil es `(State, NextAttemptAtUtc)`
  (`ArcaVoucherConfiguration.cs:19`); `NextAttemptAtUtc` queda en el pasado al autorizar (`ArcaVoucher.cs:124`) y adjuntar el PDF
  no lo mueve (`:190-195`).
- **Arreglo:** un índice parcial sobre los autorizados sin PDF; conviene hacerlo ahora, con la tabla vacía. Chico.

#### H35. La limpieza del almacén recorre todos los archivos cada hora, en cada copia

- **Dónde falla:** a gran escala; con varias copias, el trabajo se repite.
- **Escenario:** los PDF fiscales no se borran nunca. Con millones de archivos, cada hora cada copia lista el contenedor entero y
  pregunta a la base de a 128 claves, para no borrar casi nada.
- **Evidencia:** `PrivateFileCleanupService.cs:16-25,28-35`, `AzureBlobFileStore.cs:80-84` (lista todo, sin prefijo),
  `PrivateFileCleanupBackgroundService.cs:11-18` (sin reserva entre copias) y `ArcaPrivateFileRetentionSource.cs:15-28`.
- **Arreglo:** que la fuente de retención del módulo declare su prefijo y el almacén liste salteándolo (el núcleo no puede nombrar
  `arca-vouchers/`), y un lock de sesión para que corra una sola copia. Chico. Es del núcleo, así que alcanza también a los otros
  módulos.

#### H36. Cada llamada a ARCA toma el lock del ticket, aunque esté vigente

- **Escenario:** en un pico de cientos de «Completar con ARCA» y emisiones por segundo sobre el mismo servicio, cada llamada abre
  una transacción, toma el mismo lock del ticket y lee la fila antes de mirar la caché o llamar a ARCA: se forma fila y se ocupan
  conexiones de la base.
- **Evidencia:** `ArcaAccessTicketProvider.cs:34-35` y `ArcaAccessTicketStore.cs:16-18,48`; `ArcaRegistryPartySource.cs:28` abre
  la sesión antes de la caché. El lock dura dos viajes a la base, sin nada remoto adentro.
- **Arreglo:** leer sin lock (`FindTicketAsync` ya existe) cuando el ticket guardado está vigente con margen; la reserva, sólo
  para renovar. Chico.

#### H37. Sumar un cliente nuevo carga todo «Clientes» bajo su lock

- **Escenario:** con «Clientes» cerca de su tope de 10.000 opciones, contando las dadas de baja, cada factura a un cliente nuevo
  bloquea el catálogo dentro del candado global de formularios, carga todas las opciones con sus campos para ver si el cliente
  ya está y, si lo suma, vuelve a validarlas todas. En el tope no suma nada, como está escrito, pero paga igual todo el costo.
- **Evidencia:** `FormIssuedCatalogItemRecorder.cs:31-41,63-70`.
- **Arreglo:** buscar el código o la CUIT con una consulta y sumar en una transacción propia. Mediano.

### Observaciones menores

- **O1. Una CUIT que el padrón no tiene vuelve a consultarse en cada vista previa.** El «no existe» llega como falla, que la caché
  no guarda, y repetir la misma CUIT no gasta cupo (`ArcaRegistryClient.cs:38`, `ArcaRegistryCache.cs:24`,
  `ArcaRegistryQuota.cs:24`). Lo acepta [ARCA](../features/arca.md) («una falla no se guarda»), y la pausa de 1,2 s del front lo
  frena; guardar unos minutos el «no existe» sería una mejora chica.
- **O2. Veinte secuencias frenadas dejan sin atender a las demás.** El worker toma las 20 secuencias con el intento más viejo
  (`ArcaVoucherReader.cs:13-20`, `ArcaVoucherWorkerOptions.cs:12`); una frenada conserva la fecha más vieja. Con una CUIT son
  pocas; con varias CUIT representadas o con el multitenant puede pasar.
- **O3. El alta de la fila de un ticket no toma el lock de la conexión,** contra el comentario de `ArcaLockKeys.cs:5-8`: si se
  desconecta, se promueve o se cancela una renovación mientras tanto, la clave foránea sale como error inesperado
  (`ArcaAccessTicketStore.cs:48-54`). Raro, sin datos mal guardados.
- **O4. La prueba de una renovación en rojo sí guarda los datos de la empresa y los tickets del certificado nuevo,** aunque
  [ARCA](../features/arca.md) dice que «no guarda nada» (`ArcaRegistryServiceDefinition.cs:29-35`). Es inocuo: hay que corregir
  la frase.
- **O5. La CUIT se valida distinto en el front:** quita sólo el guion y el espacio comunes y deja pasar once ceros
  (`catalogPartyLookup.ts:17` en el front), y `features/arca/connection/model.ts` tiene otra regla; el servidor acepta cualquier
  espacio o guion y rechaza once ceros (`CuitNumber.cs:15-25`).
- **O6. Una fila rara de ARCA congela un catálogo entero** (`ArcaCatalogFeedPolicy.cs:61-63`, `BusinessFormCatalog.Feed.cs:39-41`);
  saltear la fila alcanza.
- **O7. Una revisión vieja consulta a ARCA antes del 409** al elegir el punto de venta (`ArcaInvoicingSetupService.cs:53,86`), y
  gasta una de las seis llamadas por minuto.
- **O8. «Empresa» sin leer dice «No pudimos leer la conexión.»** (`CompanySection.tsx:17` en el front).
- **O9. Robot, menores:** reintentar enseguida puede dar un 409 falso y obliga a escribir la clave otra vez (`robot.ts:32-36`,
  `RobotZone.tsx:162-164` en el front); sin certificado la plataforma se frena sin log ni aviso (`ArcaRobotRunStore.cs:135-141`);
  la cuenta del aviso guarda sólo el último motivo y paso (`ArcaRobotRun.cs:193-200`); con el robot apagado la pantalla relee
  cada 2 s sin fin, y con la plataforma interrumpida deja de releer aunque otro host la retome; el patrón de «validación extra»
  es muy amplio (`ArcaRobotScreens.cs:62`); terminar a mano sin haber delegado termina como «pantalla cambiada» en la asignación,
  con correo, y deja «aceptar» como hecho; «aceptar» queda hecho aunque no haya pendientes; después de un clic, el robot lee la
  tabla sin esperar (`ArcaPointOfSaleRobotStep.cs:26-35`), lo que con ARCA lenta podría dar de alta un punto de venta de más; y
  `manual-completion` usa el límite de lectura en vez del de escritura (`ArcaRobotController.cs:22,47-54`). Varias se confirman
  en la corrida real.
- **O10. El robot reutiliza cualquier punto de venta de web service vigente,** aunque lo use otro sistema de la empresa
  (`ArcaPointOfSaleRobotStep.cs:28-33`); está en la regla («o usa el que ya hay») y se cruza con la
  [decisión 4](#decisiones-del-2026-11-14).
- **O11. El consentimiento del robot queda sólo en la última corrida:** cada reintento lo pisa y desconectar lo borra
  (`ArcaRobotRun.cs:207-208`). Si la revisión legal pide guardar cada autorización, es una tabla chica.
- **O12. Lo confirmado no se fija:** con la fecha vacía, la factura sale con la fecha del día en que se toca «Emitir», no con la
  que mostró «¿La emito?», y la clase se vuelve a decidir con el padrón de ese momento (`ArcaInvoiceOrderPolicy.cs:56`,
  `ArcaVoucherService.cs:61-62`). La regla dice «la fecha, o la de hoy»; si cambia la clase o el total, convendría volver a
  preguntar.
- **O13. Dos guías desactualizadas:** [quitar ARCA](../guides/quitar-arca.md) dice «siete tablas» y enumera ocho; y el `Down`
  de `20261109210502_AddArcaVouchers` borra la cola de comprobantes (`:126-136`). Ningún procedimiento lo corre, porque el
  despliegue no revierte el esquema ([despliegue](../guides/despliegue.md)), pero conviene avisarlo ahí.

## Separar el módulo: inventario, no defecto

Si ARCA pasara a ser un servicio con su propia base, habría que cambiar poco:

- los puertos de formularios (`IStructuredRecordIssuer`, `IStructuredCatalogFeed`, `IStructuredPartyLookup` e
  `IStructuredFormStandardSource`), que hoy son llamadas en proceso; cada uno abre su propia transacción, así que no hay
  guardados compartidos con formularios;
- el aviso del robot, que encola el correo del núcleo dentro de la transacción del paso (`ArcaRobotAlertPolicy.cs:42`);
- el almacén privado y su limpieza, `RedisCache`, `CuitNumber` y el catálogo de permisos, que se usan por sus puertos.

El núcleo no nombra a ARCA y ARCA no nombra a otro módulo (`ModuleBoundaryTests`), y una familia sólo usa `Connection`
(`ArcaFamilyBoundaryTests`). El [ADR 0023](../decisions/0023-arca-como-modulo-opcional.md) lo deja como módulo opcional
dentro de la Api; separarlo pediría un ADR nuevo. Lo que sí falla hoy al quitar el módulo es la retención de los PDF
fiscales (H24).

## Costos aceptados por decisiones documentadas

No son defectos: los acepta una decisión escrita, y cambiarlos lo decidís vos.

- **La cola del robot vive en memoria** del host que recibió la clave fiscal, para que la clave no vaya a la base ni a otro
  host ([ADR 0026](../decisions/0026-robot-de-delegacion-arca.md), decisión 2). Si ese host se reinicia, la persona ve
  «interrumpida» cuando vence la reserva, hasta 3 minutos después, y tiene que volver a escribir la clave.
- **El cupo del padrón vive en memoria y por host** (`IArcaRegistryQuota.cs:7`). Con varias copias se multiplica (H32).
- **Una secuencia con un comprobante en revisión frena las demás emisiones de esa secuencia**
  ([ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md), «Consecuencias»): es el precio de no saltear ni duplicar
  números. H1 muestra un caso en el que la secuencia se frena sin pasar a revisión, que el ADR no cubre.
- **Con Redis caído, «Completar con ARCA» y la vista previa con «Otro» fallan** ([ADR 0009](../decisions/0009-redis-como-unico-cache-de-datos.md),
  [agregar caché](../guides/agregar-cache.md)). Lo que no cubre ninguna decisión está en H13.
- **El PDF fiscal y el aviso del robot van en español**, con diseño fijo y legal.
- **Perder un ticket de vez en cuando** hace que ARCA rechace otro pedido unos minutos ([ADR 0023](../decisions/0023-arca-como-modulo-opcional.md),
  decisión 4). Lo que no cubre es perderlo por una cancelación común (H8).
- **Los comprobantes y su pedido guardado no se borran al desconectar**: son registros con valor legal
  ([ARCA](../features/arca.md), «Desconectar no borra comprobantes ni secuencias»). Falta el plazo (H25).

## Lo que está bien

- **Una sola emisión por pedido.** La clave del pedido es única en la base y se busca bajo el lock antes de crear
  (`ArcaVoucherConfiguration.cs:17`, `ArcaVoucherService.cs:56-57`): dos encolados del mismo run dan un solo
  `FECAESolicitar` (`ArcaStructuredRecordIssuerTests.cs:30-73`). Una respuesta con una emisión viva no se puede corregir, y
  la nota de crédito sale una vez por factura (`ArcaVoucherQueueTests.cs:319-350`).
- **Numeración.** El número y la huella se guardan antes de pedir (`ArcaVoucherStore.cs:108-110`); un pendiente frena su
  secuencia; la base no deja dos comprobantes con el mismo número en una secuencia (`ArcaVoucherConfiguration.cs:18`,
  `Two_vouchers_of_a_sequence_never_hold_the_same_number`), y un rechazo libera el número.
- **Varias copias en la cola.** Cada paso exige la reserva propia, con fence (`ArcaVoucherStore.cs:183-189`). Si una copia
  muere a mitad de `FECAESolicitar`, otra concilia cuando vence la reserva (`ArcaVoucherQueueTests.cs:253-316`,
  `ArcaVoucherSequenceTests.cs:13-60`).
- **Reconciliación.** Lo incierto se consulta con `FECompConsultar` antes de repetir (`ArcaReconciliationPolicy.cs:11-24`),
  probado de punta a punta con pedidos perdidos, 500 y 502 (`ArcaVoucherQueueTests.cs:60-125,179-217`).
- **El PDF fiscal** se escribe una sola vez (`IfNoneMatch = *`, `AzureBlobFileStore.cs:43-61`), con el SHA-256 de lo que
  quedó guardado (`ArcaVoucherProcessor.cs:81-86`); una falla del almacén nunca provoca otra emisión.
- **El ticket entre hosts.** La reserva tiene dueño, vencimiento y cerco en cada paso (`ArcaAccessTicket.cs:30-62`), y el
  pedido al WSAA va fuera de toda transacción (`ArcaAccessTicketProvider.cs:34-61`): dos hosts que lo piden a la vez hacen un
  solo `loginCms` (`ArcaAccessTicketProviderTests.cs:49-74`).
- **Ediciones simultáneas.** La conexión, la configuración de Facturación y la revisión de un comprobante comparan la
  revisión bajo el lock y responden 409 (`ArcaConnectionService.cs:81-83`, `ArcaInvoicingSettingsGuard.cs:17-25`,
  `ArcaVoucher.cs:161-162`), y el front abre «Revisar» con lo actual.
- **Permisos bajo el lock.** Revisar una emisión exige `arca.issue`; confirmar, también desde el botón de WhatsApp, revalida
  al autor y sus permisos; anular pide el permiso del emisor (`FormWorkflowService.cs:33-49`,
  `FormIssueDecisionRecorder.cs:25-30`, `FormIssueReversalRecorder.cs:28`; `FormIssueActionHttpTests.cs:127-170`).
- **Secretos.** La clave privada y los tickets van con AES-GCM y un AAD atado a la instalación, la clave del anillo, el
  propósito, la conexión y el certificado (`ArcaCredentialService.cs:119-163`), y la Api no arranca sin el anillo. Los 14
  `[LoggerMessage]` del módulo no llevan CUIT, token, clave ni cuerpos, y los `ToString` de contratos y modelos los ocultan.
  La clave fiscal del robot no se imprime ni se serializa, y una prueba de punta a punta la busca en respuestas, logs y filas
  (`ArcaRobotEndpointsTests.cs:68-106`).
- **Clientes SOAP propios:** plazo de 20 s, tope de bytes, XML sin DTD, sin redirecciones, sin reintentos y sin loggers
  (`SoapEnvelope.cs:40-99`, `ArcaInfrastructureRegistration.cs:103-106`).
- **Cálculo.** Half-even por renglón y por alícuota, con el porcentaje que pone el servidor y la clase que decide una tabla
  (`ArcaVatBreakdown.cs:23-79`, `ArcaVoucherClassPolicy.cs:14-27`); cantidades y precios negativos se rechazan
  (`ArcaVoucherOrderPolicyTests.cs:72-97`).
- **Fechas.** «Hoy» y la ventana de emisión se calculan en hora de la Argentina (`ArcaIssueDateWindow.cs:3-20`, test
  `Today_is_the_date_in_argentina`), y los instantes se guardan en UTC con `TimeProvider`.
- **Errores e idioma.** Los 58 códigos `Arca.*` están en `Errors.resx` y `Errors.en.resx`, y los alcanzan `ErrorCodeTests`,
  `ErrorDeclarationTests` y `ResourceParityTests`, que también recorre los `.resx` del módulo.
- **Fronteras.** El núcleo no nombra a ARCA, ARCA no nombra a otros módulos y cada familia sólo usa `Connection`
  (`ModuleBoundaryTests`, `ArcaFamilyBoundaryTests`); formularios usa la cola por un puerto que no transporta tipos de ARCA
  (`IStructuredRecordIssuer.cs:11-57`).

## Controles y pruebas que conviene sumar

- **Controles automáticos, para que no vuelva:**
  - las opciones cruzadas al arrancar: la reserva de la cola cubre dos pedidos a ARCA, la del ticket uno con margen y la del
    robot su paso más largo (H23);
  - todo código que la cola guarda como motivo está declarado en un `*Errors` y traducido (H7);
  - el pedido guardado de un efecto fuera del sistema tiene versión y se lee de forma estricta en el worker (H2);
  - la retención de `arca-vouchers/` no depende de la fila del comprobante (H3 y H24);
  - un único parcial para la nota de crédito activa (H27);
  - ninguna lambda de `ExecuteInTransactionAsync` llama a un cliente de ARCA ni al proveedor de tickets;
  - los clientes con nombre de ARCA no tienen handlers de resiliencia ni de logging ni siguen redirecciones, mirando la
    configuración real (hoy los tests reemplazan el handler).
- **Pruebas que faltan:**
  - cada estándar con emisor, copiado y cargado con datos válidos, emite en todas las clases que puede decidir: la A y la B
    con y sin «El precio incluye IVA» (B1);
  - un ARCA simulado que rechace con 10016 una fecha anterior a la del último comprobante y una fuera de la ventana (H1);
  - un comprobante encolado por una versión que la otra no entiende (H2);
  - restaurar la base con un comprobante autorizado que ya no está (H3);
  - el almacén caído después de autorizar (H7) y un PDF que no se puede armar con otro detrás (H17);
  - cancelar el pedido al WSAA a mitad de camino y cortar el cuerpo de una respuesta (H8 y H12);
  - el padrón que deja de responder entre la vista previa y «Emitir» (H10);
  - dos personas en el alta de la conexión con la pantalla vieja (H15);
  - el robot con la clave de la plataforma rechazada y la empresa que reintenta (H5).
- **Pruebas reales:**
  - la homologación con la CUIT del usuario: el 10016 por fecha, los errores de la constancia y la ventana de «ya posee un
    TA válido»;
  - carga de una secuencia con mil comprobantes y del almacén con cientos de miles de PDF, midiendo con `EXPLAIN ANALYZE`;
  - el robot en la imagen de Linux publicada, midiendo memoria y CPU;
  - varias copias levantadas.

## Límites de esta revisión

- **Alcance.** Es de solo lectura sobre `23797fff` y `7b95095b`, y no incluye los cambios sin commitear de ese día. Mientras se
  revisaba entraron cuatro commits de otras sesiones; ninguno toca el código de ARCA (sólo suman un traspaso al multitenant en
  `docs/plans`).
- **Sin ejecución.** No se corrió nada: los tiempos, los techos y las frecuencias salen del código y se confirman con las
  pruebas de la sección anterior.
- **ARCA de verdad.** La homologación está pendiente: el comportamiento real de ARCA (el 10016 por fecha, los errores de la
  constancia, la ventana del WSAA) sale del código, del manual oficial de WSFEv1 y de los dobles de prueba.
- **Despliegue.** Hoy corre una sola réplica (`--max-replicas 1`, [desplegar a Azure](../deploy/azure-setup.md)): lo de
  varias copias es diseño, no algo observado.
- **Front.** Se leyó en `7b95095b`, sólo donde hacía falta para un criterio.

## Decisiones del 2026-11-14

En las ocho preguntas elegiste la opción recomendada. Las reglas escritas que cambian se actualizan con su arreglo, no antes; lo
que cambia del [ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md) va en un ADR nuevo, porque un ADR aceptado no se
reescribe ([índice de ADR](../decisions/README.md)).

1. **Horizonte: «Hoy» entero, con B1 y H1 primero.** El plan lleva B1 a B5 y H1 a H31; B1 y H1 van antes de la prueba integral y
   de homologar, porque traban la emisión. El resto de gran escala (más puntos de venta o lotes en H33, y H35 a H37) espera a que
   haya volumen real que medir, y para pasar de una réplica no queda otro arreglo: H2, H4 y H20, que empeoran con más copias, ya
   van en «Hoy». El plan suma además tres arreglos chicos:
   - la documentación de H32: los límites de pedidos y el cupo del padrón son de cada copia;
   - el índice de H34, que conviene crear ahora, con la tabla vacía;
   - la pausa de H33: la cola no espera si la vuelta emitió algo.
2. **H1: un 10016 que ARCA no emitió va a revisión.** Si el reenvío vuelve con 10016 y la consulta dice que ARCA no tiene ese
   número, el comprobante va a revisión, libera el número y lleva un motivo y un texto propios («anterior a la del último
   comprobante»). La revisión ofrece «Emitir con la fecha de hoy», con la mayor entre hoy y la fecha del último comprobante y el
   vencimiento del pago corrido (B2), o «No emitir». Al encolar y en «¿La emito?» se avisa si la fecha es anterior a la última
   autorizada de la secuencia. El mismo camino manda a revisión un incierto cuya fecha salió de la ventana, como ya pedía el
   ADR (§6). Reemplaza la decisión 5 del ADR 0028, que reenvía con el mismo número. Se descartó corregir la fecha sola, porque
   cambia un dato legal sin que lo vea una persona.
3. **H6: poner a emitir pide `formrules.manage` y `arca.issue`.** Agregar de la biblioteca un estándar con reglas que emiten pide
   los dos, y guardar una regla así suma `arca.issue` al `formrules.manage` que ya pide. Cargar la «Factura» sigue sin pedir
   `arca.issue`, como acepta el [plan de la etapa 3](../plans/2026-11-09-arca-etapa-3-factura-c.md), así que la carga por
   WhatsApp no cambia. Se descartó dejar esas reglas apagadas al copiar, pedir `arca.issue` a cada persona que carga, que
   frenaría la carga por WhatsApp, y dejarlo como está.
4. **H3: lo que ARCA tiene y la base no conoce va a revisión.** Al numerar, si ARCA tiene un último autorizado mayor que el último
   que la base conoce para esa secuencia, el comprobante va a revisión con un motivo propio, salvo cuando la base no conoce
   ningún número de la secuencia. Con el control van la retención de todos los PDF fiscales, sin mirar la base, y un apartado de
   ARCA en la guía de restauración. Cubre una restauración y otro sistema que factura en el mismo punto de venta. El costo
   aceptado: si la empresa usa otro sistema en ese punto de venta, cada factura pide una decisión. Por eso el robot da de alta un
   punto de venta propio en lugar de usar el que haya (O10), y la regla deja de decir «o usa el que ya hay». Se descartó
   quedarse con la guía y la retención, sin el control.
5. **B4: sumar un servicio pasa la conexión a «requiere atención».** Una conexión activa a la que se le suma un servicio queda en
   «requiere atención» hasta que una prueba dé verde, como pide la regla para una familia elegida sin su configuración. No se
   prueba sola, porque antes hay que autorizar el servicio en ARCA: la frase «la pantalla vuelve a probar» de
   [ARCA](../features/arca.md) y el diseño del front se ajustan con el arreglo. Se descartó corregir la regla y el
   [ADR 0025](../decisions/0025-familias-de-servicios-arca.md) para que digan lo que fijó el front.
6. **H25: el plazo fiscal se escribe, y «Clientes» suma al cliente recién al emitir.** [ARCA](../features/arca.md) va a decir que
   los comprobantes, su pedido y su PDF se conservan durante el plazo fiscal, que fija el negocio con su contador, y que un
   pedido de borrado no los alcanza dentro de ese plazo. ARCA se suma al procedimiento de borrado pendiente en
   [verificación y producción](../plans/verificacion-y-produccion.md), y la purga al vencer queda para después. El cliente
   escrito por su CUIT se suma a «Clientes» recién cuando la factura queda emitida, sin el domicilio si es una persona humana.
   Se descartó escribir sólo la regla, sin cambiar «Clientes».
7. **H28: los nombres fiscales van en español en todos lados.** El nombre del comprobante, el documento y la condición del
   cliente salen en español también en la vista previa, como en el PDF, y la regla de [ARCA](../features/arca.md), que ya lo
   decide para el documento y la condición, lo dice también para el comprobante. Se descartó traducirlos, que pide guardar los
   dos idiomas en el resultado, en el núcleo, formularios y el front.
8. **H19: la plataforma avisa cuando se rinde.** Cuando el lado de la plataforma queda frenado para siempre, también por «ARCA no
   respondió», sale el correo de RB4-A, porque es el fin de los reintentos y no un corte pasajero; y la pantalla de la empresa
   promete el aviso sólo cuando sale. Cambia RB4-A en [ARCA](../features/arca.md). Se descartó corregir sólo el texto de la
   pantalla.

## Cómo seguir

- **Antes de la prueba integral:** B1, que hace fallar el paso 4 de la prueba de la etapa 4, y H1, que traba una secuencia con una
  venta atrasada; van también antes de homologar.
- **Tus decisiones:** tomadas el 2026-11-14 ([decisiones](#decisiones-del-2026-11-14)).
- **El plan de «Hoy»:** todavía no está armado. Lleva B1 a B5, H1 a H31, la documentación de H32, el índice de H34 y la pausa de
  H33, con esas decisiones, y se anexa a un plan de ARCA en `docs/plans`, como el de WhatsApp. El resto de gran escala espera a
  que haya volumen real que medir.
- **Repetir:** después de homologar con la CUIT del usuario, que confirma o corrige lo que hoy sale del manual (el 10016 por
  fecha, los errores de la constancia y la ventana del WSAA).
