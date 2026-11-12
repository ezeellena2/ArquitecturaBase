# BusinessForms: garantías reales

Pruebas del módulo Formularios con HTTP, PostgreSQL, Redis, archivos, Excel y PDF reales, más pruebas técnicas sin base (contratos, SQL sin conexión, renderer y libro); la validación y la coordinación van en las [unitarias](../../../ArquitecturaBase.Application.UnitTests/Modules/BusinessForms/AGENTS.md) y lo que depende de Meta, en las de [WhatsApp](../WhatsApp/AGENTS.md). Leer [formularios](../../../../docs/features/formularios.md) y, antes de tocar una prueba, la sección de su tema en el [detalle](../../../../docs/features/formularios-pruebas-de-integracion.md): esta guía es el índice.

## Al modificar

- Usar ApiFactory, PostgreSQL/Redis y scopes propios. Actores propios por escenario, sin subir cuotas ni quitar el rate limit para lograr verde.
- La base de `ApiFactory` sale de `EnsureCreated`, que no instala lo que una migración agrega a mano en SQL: eso y los Up/Down/Up de las migraciones se prueban en una base propia migrada.
- Las pruebas con DB y sus exportaciones se ejecutan sólo en el cierre conjunto; por corte se compilan y corren contratos puros.
- Código sólo para pruebas en TestFeatures, sin proveedores externos.
- Lo que verifica una prueba nueva se escribe en la sección de su tema del detalle; acá sólo se ajusta el renglón del tema, o se agrega uno para un tema nuevo.

## Qué garantiza cada tema

- [Borradores y chequeo](../../../../docs/features/formularios-pruebas-de-integracion.md#borradores-y-chequeo): las seis rutas de autoría en una transacción, carreras entre hosts con recibos, el resultado protegido y su expiración, el costo medido y los nombres por idioma; el chequeo de publicación no escribe.
- [Publicación y vista previa](../../../../docs/features/formularios-pruebas-de-integracion.md#publicación-y-vista-previa): aceptar y activar con replay, rollback y otro host, con el canal de prueba y los workers reales; la recarga, la revocación, la vista previa por idioma y las culturas de cada versión.
- [Listado, baja, eliminación y retiro](../../../../docs/features/formularios-pruebas-de-integracion.md#listado-baja-eliminación-y-retiro): baja, reactivación y eliminación con CAS e historia intacta, lo que las bloquea, los estados del canal y la proyección del listado, sin revelar conteos.
- [Catálogos](../../../../docs/features/formularios-pruebas-de-integracion.md#catálogos): opciones Manual (posición, unicidad, 409 al borrar una en uso, 10.000 filas), valores tipados con su Excel y PDF, importar y exportar sin diferencias, catálogos de personas, elegir varias e idioma original, con sus migraciones.
- [Datos de las personas y destinatarios](../../../../docs/features/formularios-pruebas-de-integracion.md#datos-de-las-personas-y-destinatarios): las rutas de datos con CAS, la confirmación al borrar valores y los faltantes; filtros y máximo por destinatario; 409 al quitar o cambiar un campo o dato en uso.
- [Captura de respuestas](../../../../docs/features/formularios-pruebas-de-integracion.md#captura-de-respuestas): valores tipados, recibo idempotente, conflicto de replay y pertenencia del campo; las etiquetas ofrecidas de un catálogo se leen igual después de editarlo.
- [Carga desde la app, corregir y anular](../../../../docs/features/formularios-pruebas-de-integracion.md#carga-desde-la-app-corregir-y-anular): cargar con fotos y replay sobre PostgreSQL y Azurite, lo propio de quien sólo completa, y corregir o anular con el mismo número y su historia.
- [Bloques de ítems](../../../../docs/features/formularios-pruebas-de-integracion.md#bloques-de-ítems): el bloque de renglones en la migración, la captura, la carga y la corrección, el mínimo al publicar, la lectura en planilla, Filtros, Excel y PDF, y las reglas con el bloque.
- [Listas dependientes, elegir primero y títulos de sección](../../../../docs/features/formularios-pruebas-de-integracion.md#listas-dependientes-elegir-primero-y-títulos-de-sección): del borrador y el chequeo a la carga y la corrección; las opciones que filtra otra respuesta o el valor elegido primero, y los títulos de la versión sellada.
- [Planilla, detalle y navegación](../../../../docs/features/formularios-pruebas-de-integracion.md#planilla-detalle-y-navegación): filtros, orden, conteos y paginado en PostgreSQL con preguntas históricas, el Excel con su tope, el Estado, la presencia del PDF y la navegación sobre el conjunto filtrado; el tablero (DOC7-A) por período en la zona del perfil de quien lee.
- [Reglas, acciones y aprobaciones](../../../../docs/features/formularios-pruebas-de-integracion.md#reglas-acciones-y-aprobaciones): el bucle de acciones que no frena el lote, la paridad entre condiciones y SQL (también en un mismo ítem del bloque), la fecha de solicitud, el recorrido con aprobación y PDF, y la continuación entre aprobaciones.
- [PDF](../../../../docs/features/formularios-pruebas-de-integracion.md#pdf): la vista previa con Skia real y los emojis, en pruebas técnicas que exportan el PDF para revisarlo con Poppler.
- [Asistente y sugerencias](../../../../docs/features/formularios-pruebas-de-integracion.md#asistente-y-sugerencias): cuándo ofrecer, la selección, el prellenado y la aclaración sobre lo publicado, las rutas y la captura de sugerencias, y crear desde una propuesta de la IA e importar la copia de su planilla.
- [Motor para documentos y carpetas](../../../../docs/features/formularios-pruebas-de-integracion.md#motor-para-documentos-y-carpetas): cálculos y condiciones de punta a punta, el resultado en el detalle y la planilla, la biblioteca con sus catálogos y su carpeta, y las carpetas con su árbol, sus carreras, sus 409, el menú y el filtro de los listados.
- [Lectura de documentos y validaciones fijas](../../../../docs/features/formularios-pruebas-de-integracion.md#lectura-de-documentos-y-validaciones-fijas): la copia del comprobante con su catálogo, su lectura y sus validaciones hasta la versión, el rechazo de una cuenta o una CUIT que no dan, «Cómo viene leyendo» en SQL y la migración; la bandeja «Documentos recibidos» (DOC5-A) del puerto de los canales a pasar al formulario y la purga.
- [Ubicación](../../../../docs/features/formularios-pruebas-de-integracion.md#ubicación): del chequeo que la frena con WhatsApp a la carga desde la app, la planilla, el detalle, el Excel y la corrección con PostgreSQL, y el XLSX y el PDF con su enlace en pruebas técnicas.

## Referencias

- [Formularios](../../../../docs/features/formularios.md) y el [detalle de estas pruebas](../../../../docs/features/formularios-pruebas-de-integracion.md).
- [Escenarios, capturas y exportador de cohortes](../../TestFeatures/Modules/BusinessForms/AGENTS.md); por ejemplo, [`FormRecipientScenario`](../../TestFeatures/Modules/BusinessForms/FormRecipientScenario.cs).
- [`FormsTestModule`](FormsTestModule.cs): las rutas del módulo para el inventario y los workers apagados.
