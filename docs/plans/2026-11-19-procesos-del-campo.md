# Procesos del campo: lo que hoy se carga a mano

Propuesta del 2026-11-19, **sin programar**. Junta los procesos que las empresas del agro cargan a mano (en papel, en
planillas, en un bot de WhatsApp, en un sistema de gestión agrícola o retipeando en el ERP) y las capacidades genéricas que le
faltan a la plataforma para que cada cliente los arme solo, a mano o con la IA. Sale del relevamiento de dos empresas del agro
que usan el mismo ERP. Desde el 2026-11-15 la lista está congelada hasta la prueba integral: esto se piensa ahora y se programa
después. Ya está decidida una parte, las [acciones del asistente](#acciones-del-asistente), en el
[ADR 0034](../decisions/0034-acciones-del-asistente.md).

## Lo que decidió el usuario

No se vuelve a preguntar (2026-11-19):

- **Nada por cliente.** No se hacen formularios ni desarrollos para una empresa: cada cliente configura su negocio.
- **Si falta algo, se mejora la dinámica de la plataforma**, para que se pueda crear lo que cada negocio necesite.
- **Todo lo que hacen los otros sistemas importa**, porque la idea es que los clientes se pasen: el sistema de gestión
  agrícola, el bot de audio y las planillas que completan al ERP.
- **Cada proceso que hoy se carga a mano se automatiza** con un flujo sencillo de creación de datos.
- **Todo se puede hacer con la IA interna o a mano.** Cada capacidad nueva trae su editor y su propuesta por chat, en Inicio y
  por WhatsApp; la IA propone y una persona aprueba ([ADR 0024](../decisions/0024-ia-arma-formularios-y-catalogos.md)).

## De dónde sale

- Una acopiadora que vende insumos, tiene semillero y siembra unas 21.000 ha propias, y un productor agrícola y ganadero de
  unas 5.000 ha.
- Su ERP no tiene API, es de escritorio y se carga en la oficina. Lo que no entra ahí vive en planillas, en un sistema de
  gestión agrícola o en un bot de WhatsApp.
- Los tamaños de sus listas están en la [hoja de ruta de catálogos](2026-10-21-catalogos-con-origenes-externos.md#dos-bases-reales-del-mismo-erp-2026-11-19).

## Qué hacen los otros sistemas

**Un sistema de gestión agrícola**, con app, web y API para terceros:

- órdenes de trabajo de aplicación, siembra, fertilización, cosecha y labores: lotes, insumos con dosis y totales, aplicador,
  máquina y depósito, con los estados pendiente, ejecutada y confirmada; se le mandan al contratista por WhatsApp;
- monitoreo de plagas, enfermedades y malezas con fotos, y estados fenológicos;
- lotes con polígono, mapas de índice verde y una app que funciona sin conexión;
- avances de siembra y cosecha, y el rinde estimado con imágenes satelitales;
- silobolsas: código, largo, ubicación, extracción, semáforo y monitoreo;
- clima: estaciones, lluvias, heladas y granizo, con alertas;
- un catálogo de más de 30.000 productos comerciales con su principio activo.

No guarda el stock de insumos ni el de granos, la hacienda ni la contabilidad.

**Un bot de campo por WhatsApp**, para ganadería:

- el peón o el encargado manda un audio o un texto y la IA lo convierte en un registro: movimientos entre lotes, nacimientos,
  muertes, tratamientos, insumos usados (rollos, raciones) y lluvias;
- el bot confirma cada mensaje y deja corregir por WhatsApp;
- muestra cómo deberían quedar los animales en cada lote y marca en rojo lo que no cierra;
- contesta consultas («¿cuánto llovió en mayo?») y manda un resumen semanal con lo cargado, quién cargó y lo que está mal;
- tiene una web con los lotes, la ficha de la hacienda, un mapa satelital y el historial, y exporta a Excel.

No tiene API. Una de las empresas también lo usa para las entradas y salidas de galpón, sin decir de qué galpón ni para qué
orden.

**Las planillas y el ERP.** El plan de siembra, el presupuesto, el control de cosecha y embolsado, los precios de varios
proveedores y los vencimientos se llevan en Excel o Google Sheets. Las órdenes, los remitos y las facturas se vuelven a tipear
en el ERP.

## Mapa de procesos

✓ se arma hoy con formularios, catálogos y reglas. Las capacidades que faltan (C1 a C17) están en la sección siguiente.
Varios de estos procesos hoy se resuelven con pantallas hechas a medida para un cliente; la plataforma tendría que dejarlos
armar por configuración.

### Planificación y labores

| Proceso | Hoy se carga en | Con la plataforma |
| --- | --- | --- |
| Plan de siembra por lote: cultivo, variedad, fecha y dosis previstas | Una planilla de muchas hojas | ✓ un catálogo de lotes de la campaña con sus campos; cargarlo en grilla y pasar de campaña: C4 |
| Presupuesto y costo por lote y campaña | Planillas | C10 y C12 |
| Arrendamientos: contratos en quintales o toneladas, cuotas y vencimientos | Planillas | ✓ un catálogo de contratos; el aviso antes de cada vencimiento: C8 |
| Orden de trabajo de aplicación, siembra, fertilización o cosecha, con su receta | Sistema agrícola, papel y retipeo en el ERP | ✓ renglones, cálculos y el PDF al aprobar; asignarla al contratista y cerrarla con lo real: C6, C9 y C14 |
| Parte diario: horas, gasoil y hectáreas | Papel | ✓ |
| Registro de aplicación: clima, caudal y producto | Papel | ✓ con ubicación y foto |
| Monitoreo de plagas, malezas y enfermedades | Sistema agrícola | ✓ catálogo de plagas, foto, ubicación y aviso por umbral; el mapa de puntos: C13; de la recorrida a la orden: C7 |
| Avance de siembra y cosecha | Sistema agrícola o planilla | ✓ el registro; el plan contra lo hecho: C10 |
| Lluvias, heladas y granizo | Bot de audio | ✓ con audio; confirmar en el chat, el resumen y consultar: C2, C8 y C11; estaciones: C15 |
| Combustible y mantenimiento de máquinas | Papel | ✓; el aviso por horas o fecha y el saldo: C8 y C10 |

### Cosecha, granos y logística

| Proceso | Hoy se carga en | Con la plataforma |
| --- | --- | --- |
| Descargas de cosecha y embolsado | Planilla de cosecha en una carpeta compartida | ✓ el destino con una condición y la bolsa con ubicación y foto; el saldo de cada bolsa: C10; la bolsa como opción: C7 |
| Monitoreo y extracción de silobolsas | Planilla o sistema agrícola | ✓; C7 y C10 |
| Producción propia sin vender y ajustes de la posición de granos | Planillas | ✓ un formulario de ajuste; lo cosechado menos lo vendido: C10 y C15 |
| Pedidos de cupo de los productores y su otorgamiento | Teléfono y la cabeza de quien otorga | ✓ el pedido; otorgar como un estado y avisarle al productor: C3, C6 y C9; los kilos por otorgar: C10 |
| Cupos recibidos de la terminal y cargas a puerto: camión, patente, kilos y estado | Correo, WhatsApp y una planilla compartida | ✓ formularios; los estados de la carga y el cupo que la origina: C6 y C7; la planilla de hoy: C4 |
| Plan de carga para el cliente | PDF del ERP mandado a mano por WhatsApp | Mandarle el documento a un contacto: C3 y C9 |
| Fletes: choferes, tarifas y liquidación | Planilla | ✓ catálogos con la tarifa; la liquidación: C12 y C14 |
| Conciliación de entregas con corredores y exportadores | Planillas | C12 y C15 |
| Stock por lote de semilla y órdenes de carga del semillero | Una herramienta casera | ✓ formularios; el saldo por lote: C10; la orden atada al pedido: C6 y C7 |
| Declaración mensual de semilla fiscalizada | Exportaciones del ERP, una planilla y la carga en un portal | Exportar con el formato del portal: C4 |

### Insumos y stock

| Proceso | Hoy se carga en | Con la plataforma |
| --- | --- | --- |
| Entrada con remito | Bot de audio y retipeo en el ERP | ✓ el remito se lee y el vencimiento por lote es una pregunta; los ítems del remito: C1 |
| Salida de galpón para una o varias órdenes | Bot de audio | ✓ con el depósito obligatorio; repartirla entre órdenes: C7; el saldo: C10 |
| Transferencias, y lo comprado que sigue en el depósito del proveedor | Bot de audio y ERP | ✓; C10 |
| Conteo e inventario | Planilla | ✓; el ajuste contra el saldo: C10 |
| Stock con semáforos, mínimos y vencimientos | Exportación del ERP retocada a mano | C8, C10 y C12 |
| Equivalencias de nombres entre sistemas | Planilla propia | ✓ catálogos con código; catálogos de referencia del rubro: C16 |
| Precios de varios proveedores | Planillas | ✓ un formulario de cotización con renglones; compararlos: C12 |

### Hacienda

| Proceso | Hoy se carga en | Con la plataforma |
| --- | --- | --- |
| Movimientos entre potreros, nacimientos, muertes, compras, ventas y cambios de categoría | Bot de audio | ✓; existencias por potrero y categoría: C10; varias categorías en un audio: C1 |
| Sanidad y su calendario | Bot de audio o papel | ✓; los recordatorios: C8 |
| Pesadas, servicio, tacto y destete | Papel | ✓ |
| Suplementación y reservas (rollos, raciones) | Bot de audio | ✓; C10 |
| Parte mensual de existencias | Planilla | C10 y C12 |

### Comercial

| Proceso | Hoy se carga en | Con la plataforma |
| --- | --- | --- |
| Visita a clientes y su plan de siembra | Nada o planilla | ✓; con el CRM, una actividad |
| Cotización a un cliente, con precio y stock | ERP y WhatsApp, a ojo | ✓ renglones, cálculos y PDF; el stock y el precio al día: C10 y C15 |
| Lista de precios y su envío a los clientes | Exportación del ERP retocada y mandada por WhatsApp o por correo | Un catálogo con precios desde el ERP: C15; mandarla sola: C8 |
| Avance de cada vendedor contra su objetivo | Nadie (lo piden por correo semanal) | C8 y C12 |
| Liquidación de comisiones | Listado del ERP retocado en Excel | C12, C14 y C15 |

### Administración y personas

| Proceso | Hoy se carga en | Con la plataforma |
| --- | --- | --- |
| Alta de cliente: CUIT, padrón y crédito | Papel y correo | ✓ con aprobación; validar la CUIT y completar desde el padrón: C14 |
| Devoluciones de cobranza de cada vendedor sobre sus cuentas vencidas | Excel por correo, que después se reimporta | Una tarea por cuenta para cada vendedor: C6; los saldos del ERP: C15 |
| Remitos y facturas de proveedores | Retipeo en el ERP | ✓ lectura de documentos; pasarlos al ERP: C15 |
| Préstamos bancarios y sus cuotas | Planilla y el cuadro de marcha en PDF | ✓ un catálogo de préstamos; las cuotas desde el PDF: C1; los vencimientos: C8 |
| Gastos de campo y comprobantes | Papel | ✓ documentos por WhatsApp |
| Vencimientos de contratos, seguros y documentos | Planillas | C8 |
| Tablero de gestión y control del cierre de mes | Planillas o nadie | C6, C8, C12 y C15 |
| Asistencia, horas, elementos de seguridad, capacitaciones y accidentes | Papel | ✓ |
| Alta de peones y contratistas que sólo usan WhatsApp | — | C3 |

### Lo que se repite

- **Doble carga:** el mismo dato va a una planilla y después al ERP, o a dos sistemas que nadie cruza.
- **El ERP llega tarde o no tiene el dato:** el galpón, el lote de un insumo, el cupo antes de la carga, el rinde.
- **Nombres que no cruzan:** texto libre en lugar de una lista (campos, destinos, variedades, productos).
- **Exportar y retocar:** un reporte que pasa de PDF a Excel y se arregla a mano cada mes.
- **Datos que circulan por Excel, WhatsApp y correo,** y planillas compartidas que pierden la historia.
- **Datos que sólo están en una planilla o en la cabeza de alguien,** como el criterio para otorgar un cupo.

La plataforma ya responde a varios: un registro por hecho, cuando pasa, con listas en vez de texto libre, la historia y las
correcciones de cada respuesta, las aprobaciones y los avisos. Lo que falta está abajo.

## Capacidades que faltan

Todas son de la plataforma, sin nada del agro en el código: el rubro queda en la configuración y en los paquetes (C16).

**Para cargar**

- **C1. Varios ítems de un solo mensaje.** «Saqué 400 l de glifosato para los lotes 1, 2, 6 y 7» llena un bloque de cuatro
  ítems, y la foto de un remito llena sus renglones. Hoy el asistente no prellena el bloque, «elegir varias», las preguntas con
  «elegir primero» ni las que tienen condición ([prellenado](../features/formularios.md#propuestas-de-prellenado--etapa-5)).
- **C2. Confirmar y corregir en el chat.** En un formulario corto, el asistente contesta con un resumen y «Guardar» o
  «Corregir», sin abrir el formulario, y entiende «fueron 25, no 22» sobre la última respuesta. Hoy toda respuesta se confirma
  en el formulario de WhatsApp.
- **C3. Personas de campo y terceros.** Dar de alta a un peón o a un contratista con su teléfono, su rol y sus datos (el campo
  que atiende), desde la app o pidiéndoselo al asistente, con la confirmación de siempre
  ([ADR 0011](../decisions/0011-separacion-acceso-y-atencion-whatsapp.md)). Y que un cliente, un productor o un transportista,
  que no es usuario, pueda pedir un cupo o recibir un documento. Se cruza con las
  [solicitudes de ingreso](2026-09-30-solicitudes-de-ingreso.md). El alta de personas, a mano y con el asistente, en Inicio y
  por WhatsApp, está decidida en [acciones del asistente](#acciones-del-asistente); lo de terceros sin cuenta sigue en esta
  propuesta. Hoy «Nuevo usuario» pide los roles, y los datos de la persona (como el campo que atiende) sólo se cargan después,
  en «Editar usuario»; con AU7-A entran en el alta.
- **C4. Carga masiva, migración y formatos de otros.** Importar respuestas desde un Excel en la app, como hoy los catálogos,
  con la IA que mapea las columnas; cargar en grilla; traer el historial de otro sistema (la exportación del bot, la API del
  sistema agrícola, el ERP), para que pasarse no empiece de cero; y exportar con el formato que pide un portal o el ERP.
- **C5. Cargar sin conexión.** Completar en la app sin señal y enviar al volver, con los catálogos guardados en el teléfono.
  WhatsApp ya tolera la mala señal: el audio sale cuando vuelve.

**Para ordenar el trabajo**

- **C6. Flujos con estados y asignación.** Cada formulario define sus estados (por ejemplo, pendiente, asignada, ejecutada y
  confirmada), quién pasa de uno a otro (un rol o la persona elegida en la respuesta), qué parte del formulario se completa en
  cada uno (lo planificado y lo real, lado a lado) y su vencimiento. Cada persona ve «Para hacer» en la app y por WhatsApp.
  Las tareas también se crean en lote desde una lista: una por cuenta vencida para cada vendedor, o una por depósito para
  contar el stock. Generaliza la aprobación de un nivel que hay hoy.
- **C7. Relaciones entre formularios y catálogos vivos.** Una pregunta que elige una respuesta de otro formulario (las órdenes
  pendientes de esa persona, las bolsas activas, los contratos vigentes) y trae sus datos; en un bloque, una salida se reparte
  entre varias órdenes, y se concilia en las dos direcciones. Una respuesta también puede crear o cambiar una opción de un
  catálogo: un alta de cliente aprobada suma el cliente.
- **C8. Avisos programados y formularios periódicos.** Reglas por tiempo: unos días antes de una fecha de la respuesta o del
  catálogo, todos los días a una hora, o cuando falta la carga del día. Mandar un formulario cada semana a un rol y un resumen
  por WhatsApp o por correo.
- **C9. Avisar fuera de las 24 horas y a terceros.** Las plantillas de Meta, que esperan la decisión W-10 en
  [WhatsApp](2026-10-01-bandeja-whatsapp.md#plantillas-de-meta), y una regla que le manda un aviso o un PDF a la persona o al
  contacto de la respuesta (el contratista de la orden, el cliente del plan de carga). Las necesitan C3, C6 y C8.

**Para saber cómo está**

- **C10. Saldos a partir de movimientos.** Varios formularios suman o restan una cantidad en un mismo saldo, según uno o dos
  datos: depósito y producto, potrero y categoría, bolsa, máquina, o lote contra el plan. El saldo se ve en el momento, marca
  los negativos y los mínimos, avisa con una regla, muestra los movimientos que lo forman y se ajusta con un conteo.
- **C11. Preguntar por los datos.** «¿Cuánto llovió en mayo en el campo 3?», «¿qué queda en el galpón?» o «¿qué órdenes tengo
  pendientes?» se contestan desde las respuestas y los saldos, con los permisos de quien pregunta. Es la etapa de agentes de
  los [casos del asistente](2026-10-21-casos-del-asistente.md).
- **C12. Tableros que cruzan formularios y otros datos.** Saldos, avances, costo por lote y campaña, comparación de precios,
  avance contra objetivos, conciliaciones y el mapa de las respuestas con ubicación, también con datos que llegan del ERP
  (C15); se pueden mandar solos (C8). Hoy hay [un tablero por formulario](../features/formularios.md#tablero-por-formulario--doc7-a),
  sin renglones ni ubicación.
- **C13. Mapas y lotes con forma.** Un campo de área en los catálogos (el polígono importado de KML, shapefile o GeoJSON, o
  dibujado), las hectáreas calculadas y el mapa del lote con sus registros; después, capas satelitales.

**Para configurar**

- **C14. Validaciones, cálculos y unidades.** Validar una CUIT, un rango o un formato en cualquier pregunta (hoy sólo en las
  de un estándar); calcular entre el bloque y el resto (dosis por hectáreas); convertir unidades (bolsas a kilos); completar
  desde un padrón al responder.
- **C15. Integraciones.** Los catálogos desde la base o la API del cliente, las lluvias de una estación, las órdenes de otro
  sistema y devolver datos al ERP. Ya están en la [hoja de ruta de catálogos](2026-10-21-catalogos-con-origenes-externos.md) y en
  el [estudio de integraciones](integraciones/estudio-2026-10-22.md).
- **C16. Paquetes por rubro.** Un paquete del campo en la [biblioteca](../features/formularios.md#biblioteca-de-estándares--bi1-a-2026-11-09):
  formularios, catálogos, reglas, flujos, saldos y tableros listos, que el cliente agrega y adapta, más catálogos de
  referencia que mantiene la plataforma (productos fitosanitarios con su principio activo, cultivos, plagas, categorías de
  hacienda). Lo propio de un cliente también se puede guardar como estándar.
- **C17. La IA arma procesos completos.** Hoy propone formularios y catálogos nuevos. Tiene que proponer también reglas,
  flujos, saldos, tableros y datos de las personas, cambiar lo que existe («sumale la humedad al de cosecha») y armar un
  proceso entero desde una descripción o desde la planilla que el cliente ya usa.

## Cómo se construye

La regla: **el código implementa conceptos, no procesos.** El proceso de un cliente, como una salida de galpón que descuenta
stock, es una combinación de definiciones guardadas como datos. El código sólo crece cuando aparece un concepto nuevo, como un
saldo o un flujo; no crece con cada cliente, cada proceso ni cada pregunta. Estas reglas están propuestas en el
[ADR 0033](../decisions/0033-motores-configurables-para-procesos.md), y el
[diseño de referencia](../specs/2026-11-19-motores-configurables-design.md) las baja a archivos, tablas, tecnología y pruebas.

- **Motores configurables.** Cada concepto es un motor determinista que interpreta una definición con un esquema fijo y
  versionado. Hoy están los de formularios, catálogos, reglas, el tablero y la biblioteca. Harían falta cinco nuevos: saldos (un
  libro de movimientos que suman y restan), flujos (estados y quién los cambia), agenda (lo que se dispara por tiempo),
  relaciones (una pregunta que elige otra respuesta, un catálogo que sale de un formulario) y consultas (una capa sobre la
  planilla para tableros y preguntas). Lo demás de C1 a C17 extiende motores que ya existen.
- **Un esquema, dos editores.** La pantalla y la IA producen la misma definición y pasan por el mismo validador. La IA propone;
  el validador le devuelve la ruta del error para que corrija; una persona aprueba; el motor ejecuta
  ([ADR 0020](../decisions/0020-motor-unico-de-ia-con-capacidades.md), [ADR 0024](../decisions/0024-ia-arma-formularios-y-catalogos.md)).
- **La IA sólo en los bordes.** Traduce lo que dice una persona a una definición o a una carga, y una pregunta a una consulta
  tipada. Nunca escribe SQL ni código que se ejecute ([ADR 0019](../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md)).
  Guardar, sumar saldos, correr reglas, avisar y armar tableros no gasta tokens.
- **Capacidades por concepto y acciones por pantalla, no por pregunta.** Cada motor le suma al motor de IA sus capacidades
  (leer un resumen y proponer su definición) por el puerto `IAssistantCapabilitySource`; hoy Formularios aporta cerca de veinte.
  Las cargas, y lo que una pantalla crea, edita o elimina, son acciones declaradas que el asistente propone con tres piezas
  comunes, así que las herramientas no crecen con las pantallas ([acciones del asistente](#acciones-del-asistente)). Lo que
  ninguna capacidad ni acción puede expresar se contesta «todavía no» y queda anotado como un faltante para la hoja, no como un
  parche.
- **Tokens con tope.** El modelo recibe resúmenes acotados (hoy, 16 KiB por lectura) y, para armar, nunca respuestas ni datos de
  personas. Cada capacidad declara su presupuesto, hay cupos y el uso queda registrado por operación. La parte fija de cada
  llamada (instrucciones y esquemas) se puede aprovechar con la caché del proveedor. El costo crece con los mensajes de la gente,
  no con los datos ni con la cantidad de procesos.
- **El código, en el lugar de siempre.** Cada motor es una rebanada del módulo dueño: la entidad y sus errores en Domain; el
  servicio, el validador, el esquema y la capacidad de IA en Application; el repositorio, el lector y el worker en
  Infrastructure; el controller y el contrato en Api; el editor y la vista en el front; y sus tests, con la calificación contra
  el modelo real. Las definiciones se versionan como los formularios y las reglas: un cambio no rompe lo ya cargado.

## Acciones del asistente

La IA prepara y la pantalla de siempre confirma, para crear, editar o eliminar cualquier cosa, a mano o con la IA, en Inicio y
por WhatsApp. Decidido el 2026-11-19 y sin programar: se programa después de la prueba integral, cuando lo decida el usuario.
La decisión está en el [ADR 0034](../decisions/0034-acciones-del-asistente.md), que enmienda los puntos 2 y 3 del
[ADR 0020](../decisions/0020-motor-unico-de-ia-con-capacidades.md); el punto 4 del
[ADR 0033](../decisions/0033-motores-configurables-para-procesos.md) se ajusta a ella.

No es un motor de procesos, porque no ejecuta definiciones: es el canal común por el que la IA propone. Cada pantalla declara
sus acciones en lugar de traer su propia capacidad de proponer, y una sola propuesta de forma cerrada, validada con el esquema
de cada acción, reemplaza el DTO tipado de cada capacidad. Así la cantidad de herramientas queda fija aunque crezcan las
pantallas. Armar una definición entera (un formulario, un catálogo o la de un motor) sigue con su capacidad y su DTO. El alta
de personas de C3 es su primer ejemplo. Las respuestas de los formularios, que el asistente ya prellena, entran como acciones
de lo armado como dato (C1 les suma varios ítems de un mensaje), y C2, guardar en el chat, es la primera excepción candidata
de la regla 6.

**Lo que decidió el usuario** el 2026-11-19, que no se vuelve a preguntar:

- juntar el alta de usuario asistida con C3 («Sí, juntalos»);
- pensarlo como regla general y motor dinámico para crear, editar o eliminar cualquier cosa, también en pantallas futuras;
- revisar el alta en «Nuevo usuario», sobre Inicio (AU1-A);
- la regla de siete puntos y el motor tal como están dibujados («Sí, quedan así»), con AU2-A, AU3-A, AU7-A y AU8-A como parte
  de la regla;
- la decisión en un ADR propio, el 0034.

**De dónde sale.** Durante la prueba integral del 2026-11-19, armando cuentas de prueba a mano, el usuario preguntó si el
asistente de Inicio podía tomar un pedido escrito o dictado y abrirle el alta de usuario con los datos cargados. Ese mismo día lo
juntó con C3 y pidió pensarlo como regla general: «también tiene que servir para todos los tipos de carga de datos, también
podría ser editar, eliminar… y tiene que ser dinámico: el día de mañana hay otra pantalla… y el asistente ya sepa».

**Qué hay hoy**, verificado en el código el 2026-11-19 (backend `67d5b944`, front `91b5556d`):

- Inicio consulta con cinco lecturas, propone formularios, catálogos, una importación y el catálogo de personas (capacidades
  `OfferedInHome` de Formularios) y cierra con `assistant_answer@2`, una propuesta por turno. La tarjeta la pone su dueño por
  `AssistantProposalContext`, que hoy recibe una sola extensión. A un pedido de alta, edición o baja de otra cosa le contesta que
  no puede.
- Por WhatsApp arma formularios y catálogos con un resumen y botones (IA3-A), y «Revisar importación» manda a la app con el
  identificador del pedido y de la propuesta. El asistente automático usa 15 de sus 16 definiciones; Inicio, hasta 13, y
  todavía no usa lecturas precargadas.
- «Nuevo usuario» ya pide los roles (con roles elegidos la cuenta queda sólo con esos; sin ninguno, con «User»); los datos de la
  persona sólo están en «Editar usuario»; N1 (nadie da lo que no tiene) lo decide el servidor al guardar.

**Tableros:** lienzo [Acciones del asistente](https://claude.ai/artifact/93Go5haPSAqikPyhtc5RZV): el mapa con la regla, el
motor (AC0), un ejemplo por regla (AC1 a AC7) y el alta de usuario en detalle (AU0 a AU8), con revisiones adversariales contra
las capturas reales, el tablero RO3-D de Roles y la lista de chequeo. El diseño elegido se registra en `docs/design` del front al
programar.

### La regla: dónde se confirma lo que prepara el asistente

Sigue a cómo lo hace hoy la app:

1. **En un diálogo que guarda:** el mismo diálogo, sobre Inicio, con lo propuesto y «Propuesta del asistente» junto al título
   (un usuario, los datos de una persona). Elegido el 2026-11-19 (AU1-A).
2. **En una página, o en un diálogo que guarda la página:** lleva a esa página con lo propuesto, sin guardar (un rol, la
   información del negocio, las opciones de un catálogo, una respuesta de un formulario). Si guardar dispara efectos fuera del
   sistema, como las reglas de un formulario, la página lo dice antes de «Guardar».
3. **Nace en borrador o inactivo:** la tarjeta lo crea y queda plegada en «Borrador creado», con el enlace a su página, como hoy
   formularios y catálogos (IA1-A). Nada queda vigente hasta que alguien lo publica o lo activa.
4. **Editar:** el mismo diálogo o la misma página, con lo guardado leído al abrir. Lo propuesto es un cambio sobre eso, y «Lo que
   cambia» lo muestra lado a lado y sólo lo distinto (LR2-A). Si otro guarda en el medio, vale el control de revisión de siempre
   (el 409 y «Revisar cambios»).
5. **Eliminar, dar de baja o desactivar:** como lo hace esa pantalla: abre su confirmación, de a una cosa. Si después se guarda
   con la página, como quitar una opción de un catálogo, la baja confirmada queda marcada hasta «Guardar» (reglas 2 y 4). La
   tarjeta se llama con un sustantivo («Eliminación de usuario») y su botón va en rojo contorneado, como todo lo que quita antes
   de confirmar (PF3).
6. **Por WhatsApp:** un resumen con «Revisar en la app», que abre lo mismo que 1, 2, 4 y 5. En el chat sólo lo que hoy ya se
   resuelve ahí: cargar una respuesta con su formulario de WhatsApp y crear un borrador (regla 3). Crear, guardar o borrar lo
   demás desde el chat es una excepción que se decide aparte. Entra cuando el asistente automático tenga lugar para las dos
   herramientas del motor: cuando suba el tope o cuando las propuestas de los módulos pasen a ser acciones.
7. **Nunca:** claves y conexiones, consentimientos o declaraciones de la persona (llegan sin tildar), efectos fuera del sistema
   sin su confirmación propia y varias cosas a la vez (una propuesta por turno, y el asistente no sigue solo). Sin pantalla donde
   confirmar, «todavía no».

Valen siempre: los permisos y N1; «Revisá» en lo dudoso (la marca de «Documentos recibidos»); lo que no se puede dar se ve
apagado con su motivo (MA1-A); de a una cosa; la IA nunca guarda; y al proveedor va lo que la persona dijo, los tipos de acción y
los nombres de las definiciones (catálogos, formularios, roles), nunca datos de otros registros.

### El motor

- **Acciones declaradas.** Cada acción tiene un identificador estable con versión (`users.create@1`) y dice qué hace (crear,
  editar, eliminar u otra), qué datos lleva y de dónde salen sus opciones, qué permiso pide, con qué validación, dónde se confirma,
  si tiene un efecto fuera del sistema, sus casos de ejemplo y su presupuesto. Se ofrece cuando sus casos pasan la calificación
  contra el modelo real y su costo está medido (ADR 0020, punto 6). Las programadas se declaran una vez (la primera tanda:
  Usuarios y Roles). Las de lo armado como dato son una por tipo de definición, no por registro (una opción de un catálogo, una
  respuesta de un formulario, los datos de una persona): el modelo nombra cuál, los campos salen de esa definición, el efecto lo
  dicen sus reglas y se califican por tipo, con definiciones ficticias.
- **El registro** es un puerto del núcleo: cada módulo aporta sus acciones por su propia fuente, como hoy las capacidades por
  `IAssistantCapabilitySource`, y el núcleo no nombra a ninguno. Se filtra por los permisos de quien pide y por los módulos
  prendidos.
- **El asistente**, con tres piezas para todas las pantallas: una lectura precargada con los tipos de acción que puede hacer esa
  persona, una lectura con los campos y las opciones acotadas de una acción, y una sola herramienta de proponer con forma cerrada:
  la acción y una lista de campo y valor. Inicio suma dos definiciones y una lectura precargada, y queda dentro de los topes (16
  definiciones y 3 precargadas); no crece con las pantallas.
- **La validación, sin guardar,** valida la lista con el esquema cerrado de esa acción y con el validador de su servicio:
  formatos, obligatorios, opciones que existen y lo que quien pide puede dar (N1). Lo rechazado vuelve al modelo con la ruta del
  error. No mira otros registros: los repetidos, a quién se refiere y N1 sobre la cuenta de destino quedan para la tarjeta y la
  pantalla.
- **Versiones selladas.** Cambiar los campos de una acción es una versión nueva (`users.create@2`), que por
  [I4](../features/inteligencia-artificial.md#motor-único-y-propuestas-por-canal--etapa-5) se lee un despliegue antes de
  escribirse. La propuesta viaja en el historial con su versión.
- **La tarjeta y la apertura son genéricas.** La tarjeta busca a quién se refiere un editar o un eliminar al dibujarse, con las
  lecturas de su pantalla y los permisos de quien pide, y muestra lo encontrado o pide elegir; nada de eso vuelve al modelo ni
  queda en la propuesta. Cada pantalla presta su diálogo, su página o su confirmación con lo propuesto.
- **Una pantalla nueva** suma a la receta de un área nueva dos pasos: declarar sus acciones, con sus casos de ejemplo, y prestar
  su pantalla con lo propuesto. No pide código de IA propio, y el asistente la toma cuando sus casos pasan. Lo armado como dato no
  pide nada.
- Las propuestas que ya existen (formularios, catálogos, la importación) siguen como están.

### El primer ejemplo: el alta de personas (C3)

Tableros AU0 a AU8. AU1-A la eligió el usuario, y las demás quedaron aprobadas con la regla el 2026-11-19:

- **Dónde se revisa (AU1-A):** «Revisar y crear» abre «Nuevo usuario» sobre Inicio con lo propuesto. Crear es el alta de
  siempre (`POST /api/users`), con N1, sus errores y su aviso, y la tarjeta se pliega en «Usuario creado», con «Ver en Usuarios»
  si se tiene «Ver usuarios».
- **Lo dudoso (AU2-A):** se carga y se marca con «Revisá» y el renglón del problema, como en «Documentos recibidos»: un número
  que no es un celular, un país al que todavía no se mandan mensajes, un correo inválido, un rol que no existe o que no se puede
  dar, y la invitación por WhatsApp sin el nombre o sin el número. El chat pregunta sólo cuando no hay alta posible (sin correo ni
  número, o sin saber de quién se habla). Si el correo o el número ya son de una cuenta, lo dice el alta al crear.
- **Los roles (AU3-A):** van en el mismo diálogo, que ya los tiene. Lo que quien pide no puede dar se ve apagado, con el motivo
  en el segundo renglón del ítem (MA1-A), en el alta a mano, en la propuesta y en «Editar usuario». Para eso `GET /api/roles`
  dice, para quien pregunta, qué roles puede dar, con la regla de `PermissionDelegationGuard`; el servidor sigue decidiendo.
- **Los datos de la persona (AU7-A):** la sección «Datos» de «Editar usuario» entra también en el alta, a mano y con la IA: un
  solo diálogo y un solo «Crear». El contrato de la sección cambia (guarda con la cuenta nueva), y el núcleo suma un puerto, con
  su versión apagada, para que el módulo valide los datos propuestos sin que el núcleo lo nombre. Si los datos fallan después de
  crear la cuenta, el diálogo queda abierto con «Guardar los datos».
- **Por WhatsApp (AU8-A):** nada se crea en el chat; el resumen ofrece «Revisar en la app», que abre «Nuevo usuario» con lo
  propuesto. Crear desde el chat (AU8-B) sería una excepción, con una sola creación por propuesta, la procedencia guardada y el
  consentimiento preguntado aparte.
- **Estados y celular (AU4, AU5):** los del diálogo de siempre. La propuesta vive con la conversación y el diálogo en el celular
  es el de hoy.

### Calificación y despliegue

- La calificación mide, primero, que el modelo elija la acción correcta en «lo que puede hacer», filtrado por permisos y módulo y
  con un tope de acciones; después, casos ficticios por tipo de acción (crear, editar, eliminar) en un set del arnés, más los del
  alta de personas (números sin característica, roles que no existen o no se pueden dar, pedidos ambiguos, instrucciones
  escondidas en un audio o un archivo). Como cambia el catálogo de Inicio, se repiten `home-existing` y las mitades de Inicio de
  `ia-authoring`. Las corridas reales, con el visto bueno del usuario.
- Por la regla de versiones (I4), las piezas del motor entran registradas y sin ofrecer en un despliegue y se ofrecen en el
  siguiente.

### Etapas de las acciones

Recomendadas; se programan después de la prueba integral y cuando lo decida el usuario.

1. **El motor, sin ofrecer:** el registro de acciones como puerto del núcleo, las tres piezas de IA, la forma cerrada de
   proponer y su validación, y la tarjeta y la apertura genéricas, registradas y sin ofrecer (I4). Tests del motor sin IA y de
   arquitectura (el núcleo no nombra a los módulos; las fuentes de acciones no reciben `IUnitOfWork` ni escritores).
2. **Usuarios y Roles:** el alta de personas (AU1-A, AU2-A, AU3-A y AU7-A, con `GET /api/roles` que dice qué roles se pueden
   dar), y la edición y la eliminación de cuentas y roles (reglas 1, 2, 4 y 5), en el backend y en el front. Se ofrece en Inicio
   con la calificación.
3. **Lo armado como dato:** las opciones de los catálogos, las respuestas de los formularios (con el aviso de efectos antes de
   «Guardar») y los datos de las personas.
4. **WhatsApp (regla 6 y AU8-A):** cuando el asistente automático tenga lugar para dos herramientas.
5. **Documentación:** `docs/features/inteligencia-artificial.md`, la receta de un área nueva (`agregar-un-area.md`) y el diseño
   en `docs/design` del front. Al retirar este plan, el enlace del ADR 0034 pasa a la regla que quede.

Fuera de alcance: varias cosas en una tarjeta, altas de muchas personas de una vez (un faltante: ninguna capacidad lo cubre),
acciones sin pantalla, crear o guardar desde el chat de WhatsApp (AU8-B y C2, que se deciden aparte) y lo de terceros sin cuenta
de C3. Una factura con el asistente espera a C1 (lleva renglones) y a la homologación de ARCA.

## Etapas

Recomendadas; el orden lo decide el usuario (PC1).

0. **Diseño, antes de programar:** el [ADR 0033](../decisions/0033-motores-configurables-para-procesos.md), en borrador, con
   las reglas de arriba, y el esquema y los tableros de cada motor nuevo. Las acciones del asistente ya tienen su ADR, el
   [0034](../decisions/0034-acciones-del-asistente.md), y sus tableros.
1. **Que se pasen:** C1, C2, C3, C4, C14 y el primer paquete del campo (C16). Reemplaza al bot de audio y a las planillas de
   carga.
2. **Saber cómo está:** C8, C9, C10, C11 y C12.
3. **Ordenar el trabajo:** C6 y C7. Con esto las órdenes de trabajo quedan atadas al stock.
4. **Lo avanzado:** C5, C13 y C15.

C17 acompaña a todas: cada capacidad sale con su versión por chat.

Las [acciones del asistente](#acciones-del-asistente) siguen [sus propias etapas](#etapas-de-las-acciones): el alta de personas
de C3 entra en la 2 y las respuestas de los formularios, a las que C1 les suma varios ítems de un mensaje, en la 3.

## Decisiones

Recomendadas primero. Sólo está tomada una parte de PC2.

- **PC1. El orden de las etapas.** El de arriba (recomendado: empieza por lo que se carga todos los días y no espera a Meta);
  las órdenes primero; o los saldos primero.
- **PC2. Personas de campo (C3).** Ya está decidido que el asistente prepara el alta y se confirma en «Nuevo usuario», como el
  alta a mano ([acciones del asistente](#acciones-del-asistente), AU1-A). Falta cómo empieza a usarlo quien sólo tiene el
  teléfono: un alta guiada por teléfono (recomendado); como hoy, con la invitación y el ingreso por la web; o formularios
  abiertos a números sin cuenta, que cambia el ADR 0011.
- **PC3. Confirmar en el chat (C2).** Sólo en los formularios que se marquen como cortos (recomendado), o siempre en el
  formulario de WhatsApp, como hoy. Es una excepción a la regla 6 de las [acciones del asistente](#acciones-del-asistente), que
  se decide acá.
- **PC4. El paquete del campo (C16).** En la biblioteca, como datos que cada cliente adapta (recomendado); que la IA los arme a
  pedido, sin paquete; o nada armado de antemano.
- **PC5. Mapas y carga sin conexión (C5 y C13).** En la etapa 4 (recomendado), antes, junto con las órdenes, o fuera de la
  hoja.

## Lo que ya se puede armar

Ejemplos de lo que un cliente configura hoy, sin programar:

| Formulario | Qué usa |
| --- | --- |
| Registro de lluvia | Campo, fecha, milímetros y foto; un aviso si pasa un umbral |
| Entrada de insumos | Depósito obligatorio, el remito como documento, renglones con producto, cantidad, lote y vencimiento, y «¿Llegó todo bien?» con un aviso si es No |
| Salida de insumos | Depósito obligatorio, renglones con producto y cantidad, y los números de orden y los lotes como texto (hasta C7) |
| Movimiento de hacienda | Tipo, categoría, cantidad y potreros de origen y de destino |
| Control de cosecha | Campo y lote, cultivo, contratista, destino y, con una condición, la bolsa con su ubicación y su foto |
| Monitoreo | Lote, plaga, severidad, estadio, foto y ubicación, con un aviso por umbral |
| Visita comercial | El cliente filtrado por vendedor, el plan de siembra y los productos de interés en renglones, y el próximo paso |

**Los catálogos**, por ahora, salen del ERP con consultas de sólo lectura, pasan a una planilla y entran con «Importar
opciones», que reemplaza la lista: campos y lotes una vez por campaña, artículos y clientes una vez por mes. Después, con C15.

## Multitenant y módulos

- Todo va por organización, como el resto de Formularios. Los paquetes y los catálogos de referencia (C16) los comparte la
  plataforma entre organizaciones.
- Con MO1-B el administrador prende y apaga cada módulo; estas capacidades se prenden con Formularios. MO1-B todavía no tiene
  documento ([Pr-4 del barrido del 18/11](../reviews/2026-11-18-barrido-documental.md#pr-4-crm-y-módulos-activables)).
- La visita comercial se cruza con el CRM que se está diseñando: si el CRM registra actividades con clientes, la visita pasa a
  ser una de ellas.
- Lo que se programe va en un traspaso al multitenant.

## Relación con otros planes

- [Catálogos: hoja de ruta](2026-10-21-catalogos-con-origenes-externos.md): el conector, la búsqueda en vivo y los tamaños de
  las dos bases. Los catálogos `External` todavía no tienen campos, y una lista de lotes los necesita para elegir primero por
  campo.
- [WhatsApp](2026-10-01-bandeja-whatsapp.md#plantillas-de-meta): las plantillas de Meta (W-10), para C9.
- [Casos del asistente](2026-10-21-casos-del-asistente.md): la etapa de agentes, para C11.
- [Integraciones](integraciones/estudio-2026-10-22.md): «Ejecutar en un sistema externo», como descontar stock en el ERP, y los
  eventos externos, para C15.
- [Solicitudes de ingreso](2026-09-30-solicitudes-de-ingreso.md): se cruza con C3.
- El [índice de planes](README.md) registra los tableros que cruzan formularios: C10 y C12 son los primeros.
