# ARCA: conexión y formularios estándar

Hoja de ruta del 2026-10-31, con las decisiones del usuario de ese día. Las seis etapas tienen backend y front programados
(2026-11-04 a 2026-11-11), y los [arreglos de la revisión](#arreglos-de-la-revisión-de-arquitectura) también (14 a 16/11);
faltan la prueba integral, las pruebas en homologación y la corrida real del robot. El objetivo: conectar la
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
   a la etapa 3 y llegan ya puestos en una carpeta «ARCA». La etapa 1 tiene su diseño ([regla](../features/arca.md)),
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
| 1 · Conexión ([regla](../features/arca.md)) | El modelo de conexión con los tres modos, programando solo el certificado de la empresa. Suma la prueba de conexión, los datos de la empresa desde el padrón y la elección del punto de venta. **Programada el 2026-11-05** ([regla](../features/arca.md)), con ingresos brutos e inicio de actividades el 2026-11-11; falta la homologación real | No |
| 2 · Motor para documentos ([regla](../features/formularios.md#cálculos--ca1-a-2026-11-09)) | Renglones (programados: [regla](../features/formularios.md#el-bloque-de-renglones-al-armar-y-publicar--rg1-a-rg3-a-y-rg6-a-2026-11-04)), cálculos, condiciones, resultado externo en la respuesta, carpetas para formularios y catálogos y la biblioteca de estándares. **Decidida el 2026-11-09** (CA1-A, RE1-A, BI1-A y CO1-A) y **programada en backend y front** (2026-11-09 a 2026-11-11): cálculos, resultado externo, biblioteca con preguntas atadas, condiciones, carpetas y el perfil r13 de WhatsApp, probado con Meta ([reglas](../features/formularios.md#cálculos--ca1-a-2026-11-09), [carpetas](../features/formularios.md#carpetas--cp1-a-a-cp5-a-2026-11-09), [ADR 0029](../decisions/0029-biblioteca-de-estandares.md)); faltan la prueba integral y el cierre | No |
| 3 · Factura C ([diseño](2026-11-09-arca-etapa-3-factura-c.md), [ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md)) | **Decidida el 2026-11-09 y programada en backend y front del 2026-11-09 al 2026-11-11** ([regla](../features/arca.md#emisión-de-comprobantes-etapas-3-y-4)): la cola con la numeración y la reconciliación, el PDF fiscal, la acción de regla, el estándar, la anulación y los catálogos; el front lo programó la etapa 4. Faltan la prueba integral y la de homologación. Emisión segura, PDF fiscal, las tablas de ARCA como catálogos que se actualizan solos y el autocompletar de la CUIT del cliente, el estándar «Factura» en la app y en WhatsApp y la nota de crédito C total, en homologación | Sí, en homologación |
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
tableros elegidos, está en el [diseño del front](../../../ArquitecturaBaseFront/docs/design/arca-conexion.md). Lo de abajo es el plan de la hoja
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

## Arreglos de la revisión de arquitectura

Decididos el 2026-11-14 y programados el 14 y el 15/11; H11 lo hizo F2 de formularios el 16/11. La corrida completa del
cierre de formularios del 17/11 (`02c60e91`, backend 11.656/11.656) ya incluye todo el código de ARCA, que no cambió desde
`c6890b3f`. Falta la medición de H22, en la corrida real del robot. Salen de la [revisión de arquitectura de ARCA](../reviews/2026-11-14-arquitectura-arca.md),
que tiene el escenario, la evidencia (archivo y línea en `23797fff`) y el esfuerzo de cada hallazgo, y de las
[decisiones del usuario](../reviews/2026-11-14-arquitectura-arca.md#decisiones-del-2026-11-14), que no se vuelven a preguntar.

- **Alcance:** los hallazgos de «Hoy» (B1 a B5 y H1 a H31), la documentación de H32, el índice de H34 y la pausa de H33. De las
  observaciones menores entran O10, por la decisión 4, y la espera de O9 en el mismo paso del robot.
- **Fuera:** el resto de gran escala (más puntos de venta o lotes en H33, y H35 a H37), cuando haya volumen real que medir, y las
  demás observaciones, que quedan anotadas en la revisión.
- **Cuándo:** la etapa 1 (B1 y H1) va antes de la prueba integral y de homologar; las demás, en orden y sin fecha.
- **Al día:** el 2026-11-14 ningún commit había tocado el código de ARCA desde `23797fff`. Cada tarea vuelve a leer sus archivos
  antes de empezar.
- **Revisado:** el mismo día, un revisor contrastó el plan con la revisión y con el código, y sus correcciones ya están
  incorporadas. Las tareas 4, 5, 11, 12, 17 a 22, 27 a 29 y 31 a 33 las contrastó sólo con la revisión.
- **Sin datos que convertir:** no hubo emisiones reales, así que un cambio de formato de la cola no lee el de hoy y los
  comprobantes de prueba de desarrollo no se convierten.

**Cómo se trabaja.** Cada tarea sigue [validar un cambio](../guides/validar-un-cambio.md): el test en rojo primero y las suites
rápidas del área. La revisión adversarial va completa en lo que toca la emisión, los permisos, los datos personales o el robot, y
no en documentación, textos ni arreglos de una línea. Cada etapa cierra con el build sin advertencias, los unitarios y
`ArchitectureTests` enteros y la integración de lo que cambió; la corrida completa va una sola vez, al cerrar el desarrollo, y la
etapa 1 entra en la de la tanda, antes de la prueba integral. Además:

- una regla escrita que cambia se corrige con su arreglo, no antes, y cada tarea nombra las suyas. Las de ARCA están en
  [ARCA](../features/arca.md), sus guías ([configurar](../guides/configurar-arca.md), [quitar](../guides/quitar-arca.md) y
  [el robot](../guides/robot-arca.md)) y los `AGENTS.md` de sus carpetas, que repiten algunas. Lo que cambia de un ADR aceptado
  va en uno nuevo ([índice de ADR](../decisions/README.md));
- el ARCA simulado aprende cada caso nuevo antes del arreglo (el 10016 por fecha, un corte a mitad del cuerpo, un último
  autorizado que la base no conoce, una constancia con errores), como pide el
  [ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md) para un efecto fuera del sistema;
- un error nuevo se declara en Domain, con su código estable y sus textos en `Errors.resx` y `.en.resx`; los textos de un motivo
  de revisión nuevo van en los `.resx` del emisor, porque el front no tiene textos de un emisor;
- un cambio de un contrato publicado es aditivo, un campo o una ruta nuevos, y se revisa con el front
  ([ADR 0005](../decisions/0005-sin-versionado-de-api-por-ahora.md));
- los logs van con `[LoggerMessage]`, sin claves, secretos ni datos personales del receptor.

Las etapas van en orden, y dentro de cada etapa las tareas también, salvo en la etapa 2, donde son independientes.

### Etapa 1: lo que traba la emisión

1. **B1. La factura A o B con precios sin IVA se emite.** El total esperado se compara con la suma de los importes de los
   renglones, que es lo que calcula el formulario en cualquier clase, y no con neto más IVA (`ArcaVoucherOrderPolicy`, que usan
   la vista previa y el encolado). Cambian las dos pruebas que fijan el total con IVA y se suma una del emisor con una empresa
   responsable inscripta y el estándar, para una A y una B, con «El precio incluye IVA» marcado y sin marcar. La regla de
   [ARCA](../features/arca.md) dice que el «Total» del formulario es la suma de los importes y que, sin IVA incluido, el de la
   factura es mayor y sale en el resultado. Con eso vuelve a pasar el paso 4 de la prueba a mano de la
   [etapa 4](2026-11-09-arca-etapa-4-factura-a-y-b.md). Chico.
2. **H1 y B2. Un 10016 que ARCA no emitió va a revisión** (decisión 2).
   - Si ARCA no tiene el número y el último autorizado es menor, antes de reenviarlo se consulta la fecha de ese último. Si la
     nuestra es anterior o salió de la ventana, el comprobante va a revisión, libera el número y lleva su motivo («anterior a
     la del último comprobante» o «fuera de la ventana»), con sus textos en el emisor. Un 10016 solo no alcanza para soltar el
     número: puede ser nuestro y estar autorizado tarde.
   - La revisión ofrece «Emitir con la fecha de hoy», con la mayor entre hoy y la última fecha autorizada que la base conoce de
     la secuencia, o «No emitir»; hoy esa opción existe sólo para la fecha fuera de la ventana. Al elegirla, la revisión
     reescribe en el pedido guardado la fecha y un vencimiento del pago ya pasado, que corre a esa fecha como en la nota de
     crédito, y el texto de la opción lo dice (B2).
   - Al encolar y en «¿La emito?» se avisa si la fecha es anterior a esa misma fecha de la base.
   - El ARCA simulado da el 10016 por una fecha anterior a la del último comprobante, no sólo por el número. Una fecha fuera de
     la ventana ya no llega a ARCA.
   - Cambian la reconciliación y la revisión por fecha en [ARCA](../features/arca.md). Lo que cambia de la decisión 5 del
     [ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md) va en un ADR nuevo, que registra también el control de la
     tarea 13, decidido el mismo día (decisión 4).

   Tests: una venta atrasada va a revisión sin frenar la secuencia, y «Emitir con la fecha de hoy» sale con el vencimiento
   corrido. Mediano.

**Hecha el 2026-11-14:** B1 en 829ed447, y H1 con B2 en 0385b59b, con el
[ADR 0031](../decisions/0031-fecha-rechazada-y-numero-adelantado-arca.md).
- **La revisión adversarial** no encontró fallas serias y cambió dos cosas:
  - El número se suelta por la fecha del último comprobante, consultada a ARCA, y no por el 10016. Un 10016 puede venir de un
    número nuestro autorizado tarde, y soltarlo así facturaría dos veces.
  - Un comprobante que vuelve a la cola después de una revisión deja de decir que espera a ARCA.
- **Pruebas:** el build sin advertencias; Domain (1158), Application (6016) y ArchitectureTests (226) enteros; la integración de
  la cola, el emisor y el cliente de facturación (48/48). La corrida completa va en el cierre conjunto.
- **Límites conocidos:**
  - después de un corte largo, la espera tras un 10016 sigue la espera acumulada, hasta una hora, con la secuencia frenada;
  - un servicio con fecha futura en la misma secuencia que productos puede dejar una venta sin fecha posible hasta que pase el
    tiempo.
- **Etapas 2 a 8:** van al pie de cada etapa.

### Etapa 2: lo trivial y la documentación

3. **H10. El rechazo del padrón al tocar «Emitir» se puede reintentar.** Sale con `CanRetry`, porque no se encoló nada, como
   dice [formularios](../features/formularios.md); el rechazo de un pedido mal armado sigue sin reintento, porque repetirlo da
   lo mismo. Test: con el padrón caído, la acción queda fallida con «Reintentar». Una línea.
4. **H12. Un corte a mitad de la respuesta es «ARCA no respondió».** `SoapEnvelope` atrapa `IOException` como `Unavailable`,
   con un modo del ARCA simulado que corta a mitad del cuerpo. Una línea.
5. **H16. Anular mientras la cola espera no la frena.** En el paso de esperar, si la emisión ya no está en cola, incierta ni
   enviándose, sólo se suelta la reserva de la secuencia. Test: anular mientras ARCA no contesta. Una línea.
6. **H33. La cola no espera si emitió algo.** La pausa de 2 s se saltea cuando la vuelta pidió un CAE o guardó un PDF; una
   secuencia frenada, una espera o una falla no cuentan. Tests: varios comprobantes de una secuencia salen sin la pausa entre uno
   y otro, y con una revisión abierta la cola sigue pausando. Chico.
7. **H23 y H32. La configuración se valida y se documenta.**
   - Al arrancar se validan las reservas contra los plazos, de modo que pasen los valores de fábrica: la del ticket, mayor que el
     plazo de un pedido (`Arca:RequestTimeout`) con margen; la de la cola, que cubra una vuelta que concilia (hasta cuatro pedidos
     a wsfe y la espera del ticket); y la del robot, al menos cuatro `StepTimeout` (los de fábrica, 3 minutos y 45 s, quedan
     justo), sólo con el robot prendido. Si un valor de fábrica no cubre su cuenta, se sube.
   - [Configurar ARCA](../guides/configurar-arca.md) escribe esas cuentas; el paso a paso de la rotación del anillo, con la
     consulta del id de clave, como el de IA; y que los límites de pedidos y el cupo del padrón son de cada copia, con cómo
     dimensionarlos (H32). La [guía de despliegue](../guides/despliegue.md) suma la fila de `Arca:SecretProtection`.

   Chico.
8. **H3 y H24. Los PDF fiscales no se borran solos.**
   - La retención guarda todo `arca-vouchers/` sin mirar la base (una línea). Cambia la frase de la retención en
     [ARCA](../features/arca.md) y en el `AGENTS.md` de Application.
   - [Desplegar a Azure](../deploy/azure-setup.md) suma un apartado de ARCA en la restauración: levantar la copia con la cola
     (`Arca:Invoicing:Worker:ProcessInBackground`) y las acciones de formularios
     (`BusinessForms:ActionWorker:ProcessInBackground`) apagadas; los PDF fiscales ya no se borran, pero la limpieza sigue y borra
     a las 24 horas los demás archivos sin fila;
     comparar el último autorizado de cada punto de venta con la base y recuperar del servidor original los comprobantes que
     falten antes de prenderlas.
   - [Quitar ARCA](../guides/quitar-arca.md), en la sección del módulo y en la de la facturación, copia `arca-vouchers/` fuera
     del alcance de la limpieza antes de desplegar, con el aviso de que se borran una hora después de arrancar; así dice lo que
     el ADR 0028 cree que ya dice. Corrige también «siete tablas», que son ocho.

   Chico.

**Hecha el 2026-11-15:** las seis tareas en ba9dba84.
- **Cambios:** la reserva de la cola pasa a 3 minutos para cubrir una vuelta que concilia. La revisión adversarial no encontró
  fallas serias; corrigió que una CUIT vacía o inválida quedara con «Reintentar» y lo que decían las guías.
- **Pruebas:** el build sin advertencias; Domain, Application y ArchitectureTests enteros; la integración de las opciones, la
  cola, el emisor, el cliente y el PDF fiscal (63 tests).
- **Queda para H20:** la reserva del robot cubre cuatro acciones, no un paso largo; eso lo resuelve el latido.

### Etapa 3: quién pone a emitir y qué se emite

9. **H6. Poner a emitir pide `formrules.manage` y `arca.issue`** (decisión 3). Agregar de la biblioteca un estándar con reglas
   que emiten pide los dos, y todo guardado de reglas que deje una acción «Emitir» suma el permiso de su emisor al
   `formrules.manage` que ya pide: en una copia de «Factura», cambiar cualquier regla pide `arca.issue`. Formularios no nombra a
   ARCA: el permiso sale del emisor (`ReviewPermission` del puerto), y si falta se responde como en la anulación
   (`FormIssueReversalRecorder`). Cargar la «Factura» sigue sin pedir `arca.issue`. Cambian las reglas de
   [formularios](../features/formularios.md) (quién guarda reglas y quién agrega de la biblioteca) y los permisos de
   [ARCA](../features/arca.md). Test: un rol con «Administrar formularios» y «Publicar formularios», sin permisos de ARCA, no
   puede agregar «Factura». Chico.
10. **H2. La cola manda lo que se fijó al encolar.** Al encolar se fija el pedido ya armado (importes, alícuotas, fechas y
    códigos) con una versión, y el worker sólo pone el número; la nota de crédito fija el suyo desde el de la factura. La
    reconciliación y el PDF usan lo fijado, y el worker comprueba la huella (`DataHash`, que hoy nadie lee) antes de mandar: si
    no coincide, va a revisión con su motivo, no se pospone. Con una versión que no entiende, el worker la pospone, la respuesta
    se muestra sin el receptor y la revisión y la anulación responden un conflicto. «Emitir con la fecha de hoy» (tarea 2)
    cambia la fecha y el vencimiento de lo fijado, y la huella nueva sale al dar el número. Cambian «Fija lo que va a emitir»
    en [ARCA](../features/arca.md) y los datos fijados en el `AGENTS.md` de Application. Tests: una fila con una versión que el
    worker no conoce queda esperando, y una cuya huella no coincide va a revisión. Mediano.
11. **H9. Los importes tienen tope antes de encolar.** Un tope para el total y cada renglón (9.999.999.999.999,99), y para la
    cantidad y el precio, antes de calcular el IVA, con un código propio, en la vista previa y al encolar. Test: Cantidad
    1.000.000 por Precio 10.000.000.000 responde el código y no queda «sin confirmar». Chico.
12. **H27 y H34. Dos índices en la cola.** Una migración con un único parcial sobre `CancelsVoucherId` para las notas de crédito
    que no están rechazadas ni canceladas (H27) y un índice parcial sobre los autorizados sin PDF, que conviene crear ahora,
    con la tabla vacía (H34). Test de persistencia: dos notas activas para la misma factura no se guardan. Chico.

**Hecha el 2026-11-15:** H9, H27 y H34 en 960d1c48; H6 en 78dc2114 (front 018caf37), y H2 en e6974cf3.
- **Cambios:** el permiso del emisor que falta da 403 con un código propio (`Forms.Rule.IssuerPermissionRequired`) y no el de la
  anulación: la revisión adversarial mostró que el editor de reglas toma todo 403 como el permiso de reglas perdido y se cierra
  sin lo editado. Un emisor que no está da `Forms.Action.IssuerUnavailable`, como en la anulación, y sacar la regla que emite no
  pide el permiso. En H2 la huella se compara antes de reenviar, después de consultar a ARCA, que concilia por el total fijado; si
  no coincide, va a revisión con su motivo (`DataChanged`) y conserva el número, porque ARCA puede tenerlo.
- **Pruebas:** el build sin advertencias; Domain, Application y ArchitectureTests enteros; la integración de la biblioteca y las
  reglas de formularios (21 tests) y la de facturación de ARCA entera (92); en el front, la sección de reglas (49), tipos y oxlint.
- **Revisión:** la de H6, con un agente; la de H2, propia, porque el cupo ya pasaba el 85 %.
- **Queda para H7:** lo que espera por una versión que no conoce también dice «ARCA no responde»; H7 separa ese motivo.
- **Bases de desarrollo:** lo encolado antes de H2 no tiene versión y espera como si fuera de otra; sin número, se puede cancelar.

### Etapa 4: restauraciones, esperas y PDF

13. **H3. Lo que ARCA tiene y la base no conoce va a revisión** (decisión 4). Al numerar, si ARCA tiene un último autorizado mayor
    que el último número que la base conoce de la secuencia (de sus comprobantes con número, autorizados o pendientes), el
    comprobante va a revisión con un motivo propio, salvo cuando la base no conoce ningún número de la secuencia. La revisión
    ofrece emitir con el número siguiente o no emitir, con un texto que pide mirar en ARCA si esa venta ya se facturó. Guarda
    el último autorizado que vio, y emitir vale para ése: toma el siguiente sin volver a revisión mientras ARCA siga en ese
    número, y si ARCA avanzó otra vez, vuelve a revisión. Lo mismo vale para «Emitir con el número siguiente» de un número
    tomado o que falta, que si no caerían en esta revisión para siempre. Cambian la numeración en [ARCA](../features/arca.md),
    el apartado de restauración de la tarea 8 y [configurar ARCA](../guides/configurar-arca.md): en los modos 1 y 2 el punto de
    venta lo elige la persona, y si lo comparte con otro sistema, cada factura pide una decisión. Tests: con el ARCA simulado
    adelantado, una venta encolada no se emite dos veces, y emitir desde la revisión sale una sola vez, sin pedir otra
    decisión. Mediano. El punto de venta propio del robot es la tarea 30.
14. **H7. La espera dice de quién es.** El motivo de la espera se separa (ARCA, el almacén o la conexión), cada uno con su aviso,
    y el código del almacén queda declarado; con la facturación instalada, la Api no arranca sin almacén. Una factura autorizada
    sin su PDF se informa emitida, con su resultado y sin el documento (un dato nuevo del puerto, aditivo), así que formularios
    muestra el número y el CAE y la puede anular. Cambian el aviso «ARCA no responde. Tu factura sale apenas vuelva.» de
    [ARCA](../features/arca.md), NC1-A en [formularios](../features/formularios.md) y el almacén obligatorio en configurar ARCA
    y en la [guía de despliegue](../guides/despliegue.md). Test: con el almacén caído después de autorizar, el aviso no culpa a
    ARCA y muestra el CAE. Mediano.
15. **H17. Un PDF que no se puede armar no frena a los demás.** El armado va dentro del `try`, con un `try` por comprobante, y el
    que falla se pospone con su propio código, de los que declara la tarea 14. Test: un comprobante con una alícuota
    desconocida no deja sin PDF al siguiente. Chico.

**Hecha el 2026-11-15:** H3 en 6ee20368, H7 en 27469add y H17 en 8ac6588b.
- **Cambios:** en H3, un número tomado no consulta el último autorizado, así que como «último que vio» la revisión guarda el
  número tomado; la columna nueva es `ReviewedLastAuthorized`, con su migración, y el ADR 0031 lo dice. En H7 la espera se
  separa por el código con que espera (ARCA, la conexión u otra versión de la app), sin columna nueva; la factura emitida antes
  de su PDF deja el run de formularios en curso y guarda ya su resultado, una sola vez, y el chequeo de arranque se saca en el
  módulo de prueba de ARCA, como el de Chromium, porque la factory base no tiene almacén.
- **Pruebas:** el build sin advertencias; Domain, Application y ArchitectureTests enteros; la integración de facturación de ARCA
  entera (96) y la emisión de formularios (21).
- **Revisión:** propia, porque el cupo ya pasaba el 85 %.

### Etapa 5: la conexión y el ticket

16. **B4. El estado sigue al certificado y a los servicios** (decisión 5). Primero se corrige `CertificateExpired()`, que lleva
    «sin verificar» a «requiere atención» contra la regla del alta; después el vencimiento se calcula al leer y la conexión
    corta con `CertificateExpired` antes de llamar a ARCA. Sumar un servicio a una conexión activa la pasa a «requiere
    atención» hasta que una prueba dé verde, sin probar sola: cambian la frase «la pantalla vuelve a probar» de
    [ARCA](../features/arca.md) y el diseño del front. Chico.
17. **H4. Las tablas de ARCA no piden un ticket que no sirve.** El feed de ARCA devuelve un código sin llamar si wsfe no está
    elegido o la conexión no está activa; con la tarea 16, sumar un servicio frena la lectura de las tablas hasta otra prueba en
    verde. Test: sin Facturación elegida, una vuelta del worker de catálogos no llama al WSAA. Chico.
18. **H8. Cortar el pedido de un ticket no lo pierde.** El pedido al WSAA usa `Arca:RequestTimeout`, sin el token de quien llama
    ni el del apagado; guardar no se cancela; la reserva se suelta en un `finally`; y el ticket guardado se entrega mientras no venza,
    también al bloquear. Test: cancelar la vista previa mientras se pide el ticket. Chico.
19. **H15. Los guardados confirman la conexión que se leyó.** Las escrituras comparan el id de la conexión además de la
    revisión; la prueba y el punto de venta dan 409 si cambió la conexión o sus servicios y calculan la completitud bajo el
    lock; y el estado se recalcula en la misma transacción que el guardado. Chico a mediano.
20. **H30. Las pantallas de ARCA dicen por qué fallaron.** La zona de puntos de venta muestra el detalle de un código `Arca.*`.
    Cambiar CUIT, ambiente o modo pasa a ser un solo pedido que reemplaza la conexión en una transacción del servidor, con una
    ruta nueva y su test, y la pantalla vuelve a leer la conexión ante `NotConnected` o un 409. Mediano.

**Hecha el 2026-11-15:** B4 en adaf6d6b (front d518f018), H4 en 0b42d899, H8 en 945513cf, H15 en 5537d08b y H30 en e9a2e280 (front 80281db9).
- **Cambios:** en H15, una pantalla vieja no escribe sobre la conexión que se empezó después porque una conexión nueva
  empieza su revisión en el instante del alta (milisegundos): la revisión dice también qué conexión se leyó, sin sumar el id a
  seis contratos y al front. La prueba y el punto de venta confirman bajo el lock la conexión y los servicios con que llamaron a
  ARCA, y el punto de venta recalcula el estado en el mismo guardado; `ReevaluateAsync` quedó sin uso y se fue. En H4 el
  escenario de prueba deja activa la conexión con su certificado, como la deja el alta. En H8 el ticket guardado se sigue
  entregando mientras no venza también si la renovación no respondió, no sólo si ARCA la bloqueó.
- **Pruebas:** el build sin advertencias; Domain, Application y ArchitectureTests enteros; la integración de ARCA entera
  (conexión, padrón y facturación, 206); en el front, los tests de ARCA, tipos y oxlint.
- **Revisión:** propia, porque el cupo ya pasaba el 85 %.

### Etapa 6: el padrón y los clientes

21. **B3. Una constancia con errores no decide la clase.** Con errores en el grupo que decide la condición frente al IVA, no se
    infiere: responde un código propio y pide revisar, y esa condición no llega a «Clientes». Cambia lo que dice
    [ARCA](../features/arca.md) de una constancia con errores. Chico.
22. **H13 y H14. El padrón dice por qué falló.** El «no existe» se reconoce por el código o el texto de la falla, y las demás
    pasan a «ARCA rechazó el pedido» o «ARCA no respondió» (H14); el adaptador de la caché del padrón traduce el corte de la
    espera a «ARCA no respondió» (H13). Cambia lo que dice ARCA de una CUIT que el padrón no tiene. Chico.
23. **H25 y H31. Los datos personales y «Clientes»** (decisión 6).
    - [ARCA](../features/arca.md) dice que los comprobantes, su pedido y su PDF se conservan durante el plazo fiscal, que fija
      el negocio con su contador, y que un pedido de borrado no los alcanza dentro de ese plazo. ARCA se suma al procedimiento
      de borrado pendiente en [verificación y producción](verificacion-y-produccion.md); la purga al vencer queda para después.
    - El cliente escrito por su CUIT se suma a «Clientes» recién cuando la factura queda emitida, sin el domicilio si es una
      persona humana, que se sabe por el tipo de persona del padrón, guardado al encolar. La consulta devuelve el cliente con el
      resultado emitido, para toda CUIT (formularios ya descarta el que está), así que también se suma si la acción se retoma
      después de un corte (H31). Una factura siguiente a esa persona sale sin domicilio, como hoy la de un cliente de
      «Clientes» que no lo tiene. Cambia la frase del cliente que vuelve al encolar en ARCA, en
      [formularios](../features/formularios.md) y en el `AGENTS.md` de Application.

    Tests: una factura que no llega a emitirse no suma el cliente, y un corte entre encolar y cerrar el run lo suma igual al
    emitirse. Chico.
24. **B5. «Factura» por WhatsApp con más de 200 clientes.** La pregunta Cliente elige primero por «Inicial»
    ([LD1-A](../features/formularios.md#elegir-primero-por-un-campo--ld1-a-2026-11-07), que WhatsApp manda desde r11), un campo
    de texto nuevo de «Clientes» con la primera letra del nombre: el emisor lo completa al sumar un cliente, se corrige a mano, y
    «Consumidor final» y «Otro» llevan la suya. «Agregar» traduce el campo por su clave, como ya traduce el catálogo, y frena si
    el «Clientes» que reusa no lo tiene, como con una opción que falta: es un cambio de la biblioteca de formularios. Va con la
    versión 2 del estándar y cuesta el prellenado del cliente por WhatsApp, que LD1-A no hace. La regla de ARCA escribe el tope
    de 200 por inicial. La otra salida de la revisión, escribir la CUIT por WhatsApp, pediría un mecanismo nuevo en los Flows.
    Mediano.

**Hecha el 2026-11-15:** B3 en e283a29f, H13 y H14 en 66ee7c63, H25 y H31 en fc24b0d3, y B5 en 936b3b0c.
- **Cambios:** en H14 el «no existe» se reconoce por el texto de la falla, que es lo que el padrón manda. En B5 también
  «Completar con ARCA» escribe la inicial, no sólo el emisor, y la inicial va sin tilde salvo la Ñ, una letra propia: así
  «Álvarez» queda con la A. Un «Clientes» creado por la versión 1 no tiene «Inicial» y frena «Agregar», como se decidió; el
  emisor y el front ignoran la inicial en un catálogo sin ese campo, así que una copia vieja sigue andando.
- **Pruebas:** el build sin advertencias; Domain (1164), Application (6042) y ArchitectureTests (226) enteros; la integración
  del padrón (19), del emisor y «Completar con ARCA» (16) y de la biblioteca de formularios (6).
- **Revisión:** propia, porque el cupo ya pasaba el 85 %.

### Etapa 7: el robot

25. **H5. La clave rechazada de la plataforma no se vuelve a probar sola.** Un lado de la plataforma frenado por ingreso
    rechazado no se rearma al tocar «Delegar con el robot», ni al terminar a mano, ni al arrancar: lo rearma sólo una acción
    explícita de quien tiene `arca.manage`, después de actualizar la clave, con una ruta nueva, su test y su botón en la zona del
    robot. Así se cumple la decisión 3 del [ADR 0026](../decisions/0026-robot-de-delegacion-arca.md) («un ingreso rechazado no se
    reintenta»): la clave de la plataforma es una sola para todas sus instalaciones, y un reintento automático en cada una
    podría hacerla bloquear. Cambia el test que hoy fija el rearme, y la [guía del robot](../guides/robot-arca.md) y el correo
    dicen cómo rearmarlo. Chico.
26. **H18 y H19. El robot dice por qué se frena y avisa cuando se rinde.**
    - El plazo vencido de una navegación y un 5xx en cualquier página son «ARCA no responde»; «pantalla cambiada» queda para una
      página cargada a la que le falta lo esperado (H18).
    - `TryReserve` respeta `MaxAttempts`, así que una pasada interrumpida también se frena. Cuando el lado de la plataforma queda
      frenado para siempre, por cualquier motivo, también «ARCA no respondió» o una pasada interrumpida, sale el correo de RB4-A
      (decisión 8), y la pantalla de la empresa promete el aviso sólo cuando sale (H19).
    - Cambian RB4-A en [ARCA](../features/arca.md) y lo que dice la guía del robot de cuándo llega el correo.

    Chico.
27. **H20. La reserva acompaña al paso.** Un latido renueva la reserva mientras corre un paso, y la del cliente se renueva al
    sacarlo de la cola; la validación contra `StepTimeout` al arrancar es de la tarea 7. Test: un paso más largo que la reserva
    no deja que otro host la tome. Mediano.
28. **H21. La clave fiscal vive lo menos posible.** Las credenciales entregan la clave una sola vez y quedan vacías, sin que el
    bucle ni el canal la retengan, y la Api no arranca con un `DEBUG` que tenga `*` o `pw`. Chico.
29. **H22. Chromium medido y un driver que vuelve.** La [guía del robot](../guides/robot-arca.md) documenta la memoria y la CPU de
    una pasada en la imagen publicada; si no entran en el despliegue, corre un lado por vez por host. El driver se recrea cuando
    se cae, y el chequeo de arranque abre y cierra Chromium. Mediano; la medición se confirma en la corrida real.
30. **O10. El robot da de alta un punto de venta propio** (decisión 4). En lugar de usar cualquier punto de venta de web service
    vigente, da de alta uno: antes de confirmarlo guarda en la corrida el número que va a dar, y al reintentar toma ese número
    si ya está en la tabla, así repetir la corrida no da de alta otro (ADR 0026, decisión 5). En el mismo paso espera la tabla
    después del clic (O9). La regla de ARCA deja de decir «o usa el que ya hay». Chico.

**Hecha el 2026-11-15:** H5 en 237ea6df (front ba0761e4), H18 en 3c0b4d3a, H19 en f4e5a372 (front 341cea32), H20 en 8749f436,
H21 en 51bfc96a, H22 en e322d9b5 y O10 en 02029be2.
- **Cambios:** H5 rearma con `POST …/robot-runs/current/platform-rearm` y un error propio (`RobotPlatformNotRejected`), y el
  correo del aviso dice cómo. En H18 la sesión vigila las navegaciones de cada página y, al vencer una espera, lee el vigía
  antes de mirar la página, porque mirarla esperaría a la navegación que ARCA no contesta; un 5xx se reconoce por el estado
  HTTP del documento. En H19 también avisa el certificado que falta, y cada lado dice en `alerted` si el aviso salió. En H20
  el latido es cada `StepTimeout` (la reserva cubre cuatro) y de mejor esfuerzo. En O10 lo que dejan los pasos sobrevive a
  reiniciar la corrida, y la espera de O9 es el «Agregar» de la tabla y la fila nueva. De H22 quedan para la corrida real la
  medición y, si no entra, correr un lado por vez por host.
- **Pruebas:** el build sin advertencias; Domain (1168), Application (6046) y ArchitectureTests (226) enteros; la integración
  del robot (31, con Chromium y por HTTP); en el front, los tests de la conexión de ARCA, tipos y oxlint.
- **Revisión:** propia, porque el cupo ya pasaba el 85 %.

### Etapa 8: lo que se ve y lo que se sigue

31. **H11. Por WhatsApp, quien cargó se entera si la emisión falla.** Cuando la emisión termina fallida, se avisa a quien cargó,
    con el motivo en su idioma, como cuando falla la vista previa. Es de formularios: antes de programarla, mirar si la revisión
    de arquitectura de formularios, que corre en paralelo, la trata. Chico.
32. **H26. Una emisión se sigue de punta a punta.**
    - Los logs del worker llevan el id del comprobante, su clave de pedido y el código de la falla, también en los caminos que
      hoy posponen callados, y el mensaje de las excepciones propias que no llevan datos personales.
    - El certificado y la configuración pasan a `IAuditable`, con su migración, y desconectar deja un log con el usuario.
    - Una métrica de la cola con su `AddMeter`, como WhatsApp y la IA: los pendientes, los que esperan una revisión y la
      antigüedad del más viejo.

    Chico la trazabilidad y la auditoría; mediano la métrica.
33. **H28. Los nombres fiscales, en español en todos lados** (decisión 7). La vista previa usa el español para el comprobante, el
    documento y la condición, como el resultado y el PDF, y la regla de [ARCA](../features/arca.md) lo dice también para el
    comprobante. Una línea más el ajuste.
34. **H29. Los importes con dos decimales fijos.** En el front y en el dato `{result:arca.invoice.total}` del resultado, como ya
    salen en el PDF fiscal y en el resumen por WhatsApp: el total pasa a un tipo «importe» del puerto (aditivo), que formularios
    y el front escriben con dos decimales fijos y la región de cada idioma. Chico.

**Hecha el 2026-11-15, salvo H11:** H28 en 74d86581, H29 en e3e582c4 (front 57021f72) y H26 en 5a9ffb1f (logs), 3ac32991
(auditoría, con su migración), 3cd27c01 (desconectar) y c6890b3f (métrica).
- **H11 no se programó acá:** la revisión de formularios la trata en F2 («avisar a quien cargó cuando su regla se frena, que
  es lo que pide ARCA H11»), que vale para cualquier acción. F2 lo programó el 2026-11-16 (`63edb23e` y `f4041e4e`): un aviso a quien
  cargó, una vez por acción frenada, sin el motivo de ARCA, que se ve en la zona «Comprobante».
- **Cambios:** en H28 la explicación en inglés de la vista previa nombra la condición con el texto de ARCA. En H29 el tipo
  `amount` es aditivo: los resultados guardados con `number` se siguen leyendo igual, y el PDF y el Excel de la respuesta
  también lo conocen. En H26 desconectar pasó a `IArcaDisconnectionService`, porque el servicio de la conexión ya tenía las
  ocho dependencias de la regla; el mensaje de una excepción va al log sólo si es propia; la métrica se lee como mucho una vez
  por `PollInterval`.
- **Pruebas:** el build sin advertencias; Domain (1168), Application (6049) y ArchitectureTests (226) enteros; la
  integración de ARCA de lo que cambió (facturación y raíz 110, conexión por HTTP 26, persistencia 7) y del resultado en
  formularios; en el front, los tests de formularios que tocan importes, tipos y oxlint.
- **Revisión:** propia, porque el cupo ya pasaba el 85 %.

**Cierre.** Cada regla que cambió queda en [ARCA](../features/arca.md) y sus guías, lo de H1 y H3 también en su ADR nuevo, y la
revisión anota qué commit arregló cada hallazgo. Las decisiones van a las fuentes de las
[premisas](../architecture/premisas-de-la-plantilla.md) y la sección se retira con la
[auditoría documental](../guides/auditar-documentacion.md). Lo programado entra en el próximo traspaso al multitenant.
