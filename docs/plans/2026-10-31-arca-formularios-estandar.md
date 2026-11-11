# ARCA: conexión y formularios estándar

Hoja de ruta del 2026-10-31, con las decisiones del usuario de ese día. Nada está programado. El objetivo: conectar la
plataforma con los web services de ARCA y tener, de nuestro lado, los formularios de carga que generan lo que cada proceso
le pide a ARCA, empezando por la factura y siguiendo con hacienda (porcinos), granos y los demás sectores.

Es la parte 2 de los [proyectos de documentos](2026-10-22-documentos-por-whatsapp.md) y amplía el
[estudio de integraciones](integraciones/estudio-2026-10-22.md), que investigó la factura electrónica (WSAA, WSFEv1,
numeración, QR, nota de crédito y la delegación). Esta hoja cierra las decisiones 1 a 5 de su sección E.4; las 6 a 13
(webhooks, API pública, Mercado Libre, MCP y las demás) siguen pendientes allí. Cada etapa tiene su propio diseño, sus
tableros y su plan antes de programarse.

## Decisiones del usuario (2026-10-31)

1. **Primera familia: la factura.** Primero la C, después A y B, con nota de crédito.
2. **Acceso a ARCA: los tres modos, bien contemplados.**
   - Certificado de la empresa, cargado en su instalación.
   - Delegación del web service en la CUIT de la plataforma, guiada.
   - Delegación **automatizada de punta a punta**: ni el cliente ni el dueño de la plataforma entran a mano a cada cuenta
     de ARCA para darla de alta o aceptarla. Es lo que hacen Facturitas y Tramitito, que ARCA no expone por API.
3. **El robot que maneja la página de ARCA es propio**, un worker con Playwright para .NET. La clave fiscal del cliente
   nunca sale del servidor. Se descarta Afip SDK, que recibe la clave fiscal y los certificados en sus servidores.
4. **La factura lleva renglones en la app y en WhatsApp.** Hay un solo estándar «Factura» para los dos canales. El usuario
   eligió primero «WhatsApp con un solo concepto» y lo corrigió.
5. **Las etapas y el diseño de esta hoja quedaron aprobados.**
6. **Carpetas para formularios y catálogos** (2026-11-01). Hay carpetas con subcarpetas, y un mismo formulario o catálogo
   puede estar en varias, porque un catálogo se usa en formularios de categorías distintas. Van en la etapa 2, antes de la
   biblioteca. Se diseñaron y decidieron el 2026-11-03 en su [plan](2026-11-03-carpetas-de-formularios-y-catalogos.md).
7. **La etapa 1 se achica** (2026-11-01). Las tablas de ARCA como catálogos y el autocompletar de la CUIT del cliente pasan
   a la etapa 3 y llegan ya puestos en una carpeta «ARCA». La etapa 1 tiene su [diseño](2026-11-03-arca-etapa-1-conexion.md),
   con sus decisiones del 2026-11-01 al 2026-11-03.

**Resueltas por defecto**, con la recomendación del estudio y sin objeción del usuario:

- Antes de enviar, la persona confirma un resumen («¿La emito?»). La aprobación es opcional y la decide cada formulario en
  sus reglas, por ejemplo por monto, como hoy.
- Un punto de venta de web service exclusivo de la plataforma, para que nadie más consuma esa numeración.
- Si ARCA no responde, la emisión queda en cola con reintentos y la persona recibe un aviso.
- El PDF fiscal tiene un diseño fijo con QR y se genera al obtener el CAE, separado del diseño libre de los formularios.
- Cada instalación usa su propio certificado de la CUIT de la plataforma, porque ARCA no deja usar un certificado desde dos
  equipos a la vez.
- Es un módulo opcional `Arca`, no un `Integrations` genérico, hasta que exista un segundo proveedor.
- En el modo 1, la app genera la clave privada y el pedido de certificado (CSR). La clave privada no viaja.

## Qué servicios de ARCA entran

Del [catálogo de web services](https://www.arca.gob.ar/ws/documentacion/catalogo.asp) (48 servicios) y de los
[web services de factura electrónica](https://www.arca.gob.ar/ws/documentacion/ws-factura-electronica.asp) (7), consultados
el 2026-10-31:

| Familia | Servicios | Para nosotros |
| --- | --- | --- |
| Base | WSAA: ticket de acceso con certificado o delegación | La conexión; no es un formulario |
| Consultas | Constancia de inscripción, padrón A4, A10 y A13, A100 (tablas), constatación de comprobantes (WSCDC) y apócrifos | Autocompletar y validar la CUIT, catálogos con datos de ARCA y verificar facturas recibidas |
| Facturación | wsfev1 (A, B, C y M, sin ítems), wsmtxca (A y B con ítems), wsfexv1 (exportación), wsbfev1 (bienes de capital) y wsct (turismo) | Formularios de emisión |
| Granos | WSCPE (carta de porte) y WSLPG (liquidación primaria) | Formularios de emisión |
| Hacienda | WSLSP (bovinos, porcinos y aves; [manual 2.0.6 del 23/06/2026](https://www.afip.gob.ar/ws/WSLSP/manual_wslsp_2.0.6.pdf)) y WSREMCARNE ([remito cárnico](https://www.arca.gob.ar/actividadesAgropecuarias/sector-pecuario/), que se vincula con la factura) | Formularios de emisión |
| Otros sectores | Lechería (WSLUM), tabaco (WSLTV), caña (WSLCA) y remitos de harina y de azúcar | Si un cliente los pide |
| No aplica | Los 11 de Aduana y Grandes Operadores; los reservados a bancos u organismos (deuda, VEP, DNRPA y Mi Argentina); DDJJ, F931 y seguros de caución | Fuera |

**El DT-e no es de ARCA:** lo emite SENASA en [SIGSA](https://www.argentina.gob.ar/node/150794). No encontramos un web
service público documentado; se pide a SENASA y se trata como una integración aparte.

**wsfev1 no recibe los ítems**, solo los totales y el IVA por alícuota. Los renglones hacen falta para el PDF, que tiene que
describir lo vendido.

## Etapas

| Etapa | Qué incluye | Efecto legal |
| --- | --- | --- |
| 1 · Conexión ([diseño](2026-11-03-arca-etapa-1-conexion.md)) | El modelo de conexión con los tres modos, programando solo el certificado de la empresa. Suma la prueba de conexión, los datos de la empresa desde el padrón y la elección del punto de venta. **Programada el 2026-11-05** ([regla](../features/arca.md)); falta la homologación real | No |
| 2 · Motor para documentos ([plan](2026-11-09-arca-etapa-2-motor.md)) | Renglones (programados: [regla](../features/formularios.md#el-bloque-de-renglones-al-armar-y-publicar--rg1-a-rg3-a-y-rg6-a-2026-11-04)), cálculos, condiciones, resultado externo en la respuesta, carpetas para formularios y catálogos y la biblioteca de estándares. **Decidida el 2026-11-09** (CA1-A, RE1-A, BI1-A y CO1-A) y **con todo el backend programado**: cálculos, resultado externo, biblioteca con preguntas atadas, condiciones, carpetas y el perfil r13 de WhatsApp ([reglas](../features/formularios.md#cálculos--ca1-a-2026-11-09), [carpetas](../features/formularios.md#carpetas--cp1-a-a-cp5-a-2026-11-09), [ADR 0029](../decisions/0029-biblioteca-de-estandares.md)); faltan la prueba de r13 con Meta y el front, después de la prueba integral | No |
| 3 · Factura C ([diseño](2026-11-09-arca-etapa-3-factura-c.md), [ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md)) | **Decidida el 2026-11-09; la cola de emisión, la numeración con reconciliación y el PDF fiscal programados** ([regla](../features/arca.md#emisión-de-comprobantes-etapas-3-y-4-en-curso)); la acción de regla, el estándar, la anulación y los catálogos esperan la biblioteca y las carpetas de la etapa 2, y el front, la prueba integral. Emisión segura, PDF fiscal, las tablas de ARCA como catálogos que se actualizan solos y el autocompletar de la CUIT del cliente, el estándar «Factura» en la app y en WhatsApp y la nota de crédito C total, en homologación | Sí, en homologación |
| 4 · Factura A y B ([diseño](2026-11-09-arca-etapa-4-factura-a-y-b.md)) | IVA por alícuota, condición frente al IVA obligatoria (RG 5616), el apartado de la RG 5614 y la nota de crédito A y B. Diseñada el 2026-11-09 con las normas confirmadas y programada el 2026-11-11 (backend y front): extiende la etapa 3. Faltan la prueba integral y la de homologación. El piloto en producción se reemplazó por la prueba en homologación con la CUIT del usuario (decisión del 2026-11-09) | Sí, en homologación |
| 5 · Delegación guiada ([plan](2026-11-09-arca-etapa-5-delegacion-guiada.md)) | El certificado de la CUIT de la plataforma en cada instalación, la guía paso a paso para el cliente y la detección de que la delegación llegó. Decidida el 2026-11-09 (DG1-A, DG2-A y la constancia sin delegar, [ADR 0027](../decisions/0027-delegacion-guiada-arca.md)); **backend programado el 2026-11-09 y front el 2026-11-11** ([regla](../features/arca.md#delegación-guiada-etapa-5)); faltan la prueba a mano y la real, en la prueba integral | — |
| 6 · Delegación con robot ([plan](2026-11-09-arca-etapa-6-robot.md)) | El worker con Playwright: del lado del cliente delega y crea el punto de venta; del lado de la plataforma crea el certificado de la instalación, acepta y asigna. Si falla, vuelve a la guía. **Decidida; backend programado el 2026-11-09 y front el 2026-11-11** ([ADR 0026](../decisions/0026-robot-de-delegacion-arca.md), [regla](../features/arca.md#delegación-con-robot-modo-3)); falta la corrida real, en la prueba integral | — |
| Después | Hacienda (WSLSP con porcinos y el remito cárnico), granos (carta de porte y liquidación primaria), la constatación de facturas recibidas, exportación y los demás sectores, de a una familia | — |

Las etapas 1 y 2 no dependen una de otra. La 5 y la 6 solo necesitan la 1, así que se pueden adelantar si la prioridad es
ofrecerlo como servicio.

## Diseño

### Conexión (etapa 1)

Programada el 2026-11-05: la regla vigente está en [ARCA](../features/arca.md), la operación en
[configurar ARCA](../guides/configurar-arca.md) y la retirada en [quitar ARCA](../guides/quitar-arca.md). El diseño, con los
tableros elegidos, está en el [diseño de la etapa 1](2026-11-03-arca-etapa-1-conexion.md). Lo de abajo es el plan de la hoja
de ruta; donde difiere, vale la regla (por ejemplo, los estados y la pantalla por zonas).
El [ADR 0025](../decisions/0025-familias-de-servicios-arca.md) ordena el módulo con una carpeta por familia de servicios
(`Connection`, `Registry`, `Invoicing` y, más adelante, granos, hacienda y las demás). Cada empresa elige qué servicios usa,
y cada familia se puede quitar sola.

- **Módulo opcional `Arca`**, en `Modules/Arca` de cada capa, como WhatsApp o la IA. El núcleo no lo nombra. El motor de
  formularios lo usa a través de un puerto neutral en `Application/Interfaces/Channels/`, con su versión apagada.
- **Pantalla en Administración:** CUIT, ambiente (homologación o producción), modo, punto de venta, estado (sin verificar,
  activa, requiere atención o revocada) y «Probar conexión», que pide un ticket de acceso y llama a `FEDummy`. Guardar usa
  revisión esperada y 409 ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md)).
- **Modo 1, certificado de la empresa:** la app genera la clave privada y el CSR; la persona lo sube a ARCA y carga el
  certificado que le devuelve ARCA. La clave privada se guarda con el anillo externo del
  [ADR 0014](../decisions/0014-credenciales-de-ia-fuera-de-la-base.md): el backup de la base no alcanza para descifrarla.
- **Ticket de acceso:** uno por servicio y certificado, cifrado, compartido entre hosts y renovado con candado. ARCA
  rechaza otro pedido dentro de los 2 minutos en producción.
- **Modos 2 y 3 en el modelo:** la conexión guarda la CUIT representada y el certificado de la CUIT de la plataforma de esa
  instalación. La delegación se detecta cuando la CUIT aparece en `relations` del ticket de acceso; puede hacer falta pedir
  un ticket nuevo, respetando el lapso.
- **Consultas:** la constancia de inscripción trae la razón social, la condición frente al IVA y el domicilio de la empresa
  emisora. Las tablas de parámetros (`FEParamGet…`) como catálogos y el autocompletar del cliente quedan para la etapa 3.
- **Permisos nuevos:** administrar la conexión y emitir. Siguen [permiso nuevo](../guides/permiso-nuevo.md).

### Delegación (etapas 5 y 6)

**Cómo lo hacen otros.** Facturitas pide la delegación a mano desde el Administrador de Relaciones, más un punto de venta
«Facturación Electrónica - Monotributo - Webservice» ([guía](https://help.facturitas.app/es/articles/10596308-paso-a-paso-registro-facturitas-manual)).
Tramitito ofrece además una opción automática que usa la clave fiscal una sola vez y dice no guardarla
([nota](https://elmilenio.info/2026/07/29/tramitito-facturacion-en-arca-a-traves-de-whatsapp/)). La delegación no queda
activa hasta que quien la recibe la acepta ([guía de delegación](https://docs.afipsdk.com/recursos/tutoriales-pagina-de-arca/delegar-web-service)).
Afip SDK vende esos pasos como automatizaciones: «Delegar un web service», «Aceptar delegación de web service», «Crear
punto de venta» y «Crear certificado de producción» ([lista](https://afipsdk.com/docs/automations/introduction/)). Como ARCA
no publica una API para el Administrador de Relaciones, la inferencia es que todos manejan la página web con un navegador
automático.

**El robot propio:**

- **Del lado del cliente, una vez y con su clave fiscal:** «Nueva relación» que delega el web service en la CUIT de la
  plataforma y alta del punto de venta de web service.
- **Del lado de la plataforma, con su propia clave fiscal:** aceptar la delegación y asignarla al computador fiscal de esa
  instalación.
- **La clave fiscal del cliente** vive solo en memoria durante el trámite: nunca va a la base, a los logs ni a la cola. El
  cliente da un consentimiento explícito.
- **Si algo falla** (una pantalla cambiada, un captcha o una validación extra), el cliente pasa a la guía del modo 2. El robot
  no resuelve captchas.

**Riesgos que la etapa 6 tiene que cubrir:**

- **Fragilidad:** depende de las pantallas de ARCA. Hace falta un aviso cuando se rompe y la vuelta a la guía.
- **Sin homologación:** el Administrador de Relaciones existe solo en producción. Se prueba con cuentas reales y lo corre
  el usuario.
- **Legal:** ARCA define la clave fiscal como personal e intransferible. Antes de ofrecer el modo 3, lo revisa un contador
  o un abogado. El usuario confirmó esa revisión el 2026-11-09; igual el robot queda apagado por defecto en cada
  instalación.
- **Despliegue:** Playwright necesita Chromium en la imagen del worker.

### Motor para documentos (etapa 2)

- **Renglones:** diseñados y decididos el 2026-11-03 en el plan de renglones, programado y retirado ([regla](../features/formularios.md#el-bloque-de-renglones-al-armar-y-publicar--rg1-a-rg3-a-y-rg6-a-2026-11-04)),
  que adelanta esta parte de la etapa. El tipo `Group`, hoy reservado y rechazado al guardar, pasa a ser un bloque de
  preguntas que se repite, con un mínimo y un máximo de renglones; la factura lo usa para artículo, descripción, cantidad,
  precio y alícuota.
  - En la app, una tabla con «Agregar renglón» (RG2-A).
  - En WhatsApp, una pantalla por renglón, hasta 10, con «¿Agregás otro renglón?» (RG3-A): pantallas fijas repetidas, sin el
    intercambio cifrado con Meta. El total de la factura dentro del Flow probablemente necesite ese intercambio, que sigue
    pendiente en el [resumen de formularios](formularios/README.md); se decide con la factura. Con renglones, la respuesta
    tiene el tope de 64 KiB de los formularios con archivos (RG7-A, 2026-11-04): una factura sin adjuntos tiene el mismo
    lugar que una con archivos.
  - La planilla sigue con una fila por respuesta y el Excel suma una hoja por bloque (RG4-A); el detalle y el PDF muestran
    una tabla. Las reglas miran «algún renglón con…» (RG5-A).
- **Cálculos:** campos de solo lectura con un conjunto fijo de operaciones (suma de renglones, cantidad por precio e IVA
  por alícuota), sin fórmulas libres. El servidor recalcula y manda; el front muestra una vista previa.
- **Condiciones:** una pregunta se muestra según otra respuesta, por ejemplo el período solo si es un servicio.
- **Resultado externo:** la respuesta guarda lo que devolvió ARCA (CAE, vencimiento y número). Se ve en el detalle, la
  planilla y el PDF, y se usa en los avisos.
- **Biblioteca de estándares**, como la decidió el [plan de documentos](2026-10-22-documentos-por-whatsapp.md): datos de
  inicio, y «Agregar» crea una copia editable que recuerda de qué estándar viene. Las preguntas que la emisión necesita
  quedan **atadas**: se cambia su texto o su orden, pero no se borran ni cambian de tipo.
- **Carpetas:** diseñadas el 2026-11-03 en [carpetas para formularios y catálogos](2026-11-03-carpetas-de-formularios-y-catalogos.md).
  Es un solo árbol para formularios y catálogos, y un elemento puede estar en varias carpetas. Solo ordenan, sin cambiar
  permisos. El panel va a la izquierda de cada listado (CP1-A), «Carpetas…» abre un diálogo con casillas (CP2-A) y se
  administran en el mismo panel (CP3-A). Lo que trae la biblioteca y lo que llega de ARCA viene ya clasificado.

### Emisión segura (etapa 3)

- **Acción de regla «Emitir comprobante en ARCA»** sobre el motor de acciones durable de
  [formularios](../features/formularios.md): reserva con lease y fence, efecto fuera de la transacción, «Sin confirmar» y
  revisión humana.
- **Numeración:** candado por CUIT, punto de venta y tipo. El número (último autorizado más 1) se guarda con el hash de los
  datos antes de llamar a `FECAESolicitar`. Ante un timeout, un 10016, un 500 o un 502 se reconcilia con `FECompConsultar`
  antes de reintentar. Si el número existe con otros datos, va a revisión humana y no se reintenta solo. El algoritmo
  completo está en la sección «Cómo evitar emitir dos veces» del estudio.
- **Cola:** reintentos con espera creciente y aviso a la persona. Vigila la ventana de `CbteFch`: ±5 días para productos y
  ±10 para servicios.
- **Antes de emitir,** el servidor recalcula los importes con redondeo half-even (`MidpointRounding.ToEven`) y valida el
  dígito de la CUIT, las fechas y la clase compatible con la condición frente al IVA.

### Factura (etapas 3 y 4)

- **Preguntas del estándar:** cliente (del catálogo Clientes; la CUIT completa razón social y condición frente al IVA desde
  el padrón, o «Consumidor final»), concepto, renglones, período y vencimiento del pago si es un servicio, fecha (por
  defecto hoy) y «Mandar copia al cliente».
- **El tipo de comprobante lo decide el sistema,** a partir de la condición del emisor y del receptor (la tabla «Qué
  comprobante corresponde» del estudio). La persona no lo elige.
- **Catálogos de ARCA (etapa 3):** las tablas de parámetros que la factura usa (alícuotas, condición frente al IVA,
  monedas) llegan como catálogos de solo lectura que se actualizan solos, en la carpeta «ARCA». Se construyen sobre los
  catálogos con origen externo de la [hoja de ruta de catálogos](2026-10-21-catalogos-con-origenes-externos.md).
- **Reglas que trae:** al recibir, la aprobación opcional; después emitir, generar el PDF fiscal y avisar con el PDF a quien
  cargó y, si se marcó, al cliente.
- **Una respuesta con factura emitida no se corrige ni se anula como las demás.** Las reglas de
  [corregir y anular](2026-10-29-cargar-formularios-desde-la-app.md#decisiones-2026-10-29-y-2026-10-30) no le aplican:
  «Anular» emite la nota de crédito de la misma clase, que referencia la factura en `CbtesAsoc`. En la etapa 3 es por el
  total; la parcial queda para después.
- **Fuera de esta vuelta:** facturar al cobrar, cargar por audio con IA, exportación y factura de crédito MiPyME.

### Pruebas

- **Unitarias:** tipo de comprobante, importes y redondeo, QR y validaciones.
- **De integración:** un ARCA simulado (WSAA y WSFEv1) con respuestas que no llegan, 10016, «Transacción activa» y varios
  hosts a la vez, como pide el ADR 0018 para efectos fuera del sistema. [arcasim](https://github.com/federicomoroz/arcasim)
  sirve de referencia, revisado antes de usarlo.
- **Homologación real:** con un certificado de prueba de WSASS. La app genera el CSR y el usuario lo sube con su clave
  fiscal.
- **El robot:** solo en producción, con cuentas reales, corrido por el usuario.

## Antes de programar cada etapa

- **Tableros primero:** la conexión en Administración, los renglones en la app y en WhatsApp (dibujados y elegidos el
  2026-11-03, RG1 a RG5), la emisión y su estado en el detalle y la anulación con nota de crédito.
- **ADR:** el módulo `Arca` y su puerto; las credenciales de ARCA (certificados, ticket de acceso y la clave fiscal en
  memoria), que extienden el ADR 0014; el robot sobre la página de ARCA.
- **Pendientes del estudio, resueltos en la etapa 1:** la firma CMS va con SHA-256 y los endpoints son configuración
  (`Arca:Testing` y `Arca:Production`). Falta confirmar en la homologación real los emisores de cada ambiente y la
  condición frente al IVA con CUIT reales ([pendiente](../features/arca.md#pendiente)).
- **Plazo normativo:** desde el 1/12/2026 ARCA rechaza las solicitudes sin condición frente al IVA del receptor
  (RG 5616). A y B la informan desde el primer día, y la C también.
- **Multitenant:** al cerrar cada etapa se ofrece el [traspaso](2026-10-30-traspaso-multitenant-carga-desde-la-app.md) a
  ArquitecturaBaseMutitenant. Los modos de delegación encajan ahí con una sola CUIT de plataforma para todas las
  organizaciones.
