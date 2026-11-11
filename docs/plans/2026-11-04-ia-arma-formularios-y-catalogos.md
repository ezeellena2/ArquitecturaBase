# La IA arma formularios y catálogos a pedido

Diseño del 2026-11-03 con las decisiones del usuario del 2026-11-04. La Etapa 1 (texto y audio) quedó programada el 2026-11-05
y la 2 (planillas) el 2026-11-11, las dos calificadas con OpenAI real; la 3 (documentos) se planifica junto con la lectura de
documentos del ADR 0022 (2026-11-11). Desde el chat de Inicio o por
WhatsApp, con un texto, un archivo o un audio, se le pide a la IA un formulario o un catálogo: lo propone, lo muestra y una
persona lo pasa al editor con todo cargado, sin publicar.

**De dónde sale.** En la prueba completa del 2026-11-03, el usuario preguntó si se le puede pedir a la IA, en lenguaje
natural, con un PDF o con un audio, que arme un formulario o un catálogo, tanto por WhatsApp como en el chat de la app: que
entienda qué quiere crear, se lo sugiera, le muestre una vista previa y lo lleve al editor con todo precargado.

**Qué hay hoy**, verificado en el código el 2026-11-03:
- El chat de Inicio sólo consulta. Acepta texto, sin adjuntos ni audio, y tiene cinco herramientas de lectura
  ([IA](../features/inteligencia-artificial.md#fuentes-y-autorización)).
- El asistente de WhatsApp interpreta texto y audio (el audio se transcribe con `NormalizeAudio`), ofrece formularios que
  existen, prellenados, y anota los que faltan. Una foto o un PDF se guardan sin que la IA los lea.
- Por WhatsApp, el asistente ya arma un catálogo de personas con filtros (H4): lo crea inactivo en cuanto entiende el pedido y
  manda el enlace; en la web se aprueba o se descarta con la banda «Propuesta del asistente»
  ([formularios](../features/formularios.md#catálogos-propuestos--etapa-5-h4)).
- «Crear formulario», en Formularios que faltan, abre el editor de un formulario nuevo con el nombre escrito.

**Qué estaba decidido y este plan junta:**
- [Documentos por WhatsApp](2026-10-22-documentos-por-whatsapp.md) y el [ADR 0022](../decisions/0022-lectura-de-documentos-con-ia.md):
  la IA lee fotos, PDF, Word y Excel. Para un documento que no encaja en ningún formulario, «Armar formulario con la IA»
  (DOC6) propone el formulario entero y «Crear borrador» lo crea y abre el editor.
- La fase 4 de la [hoja de ruta de catálogos](2026-10-21-catalogos-con-origenes-externos.md#el-asistente-arma-catálogos-e-importa):
  «Creame un catálogo de proveedores con…», el Excel que se le pasa al asistente (al modelo, sólo los encabezados y unas filas
  de muestra), la importación por el recorrido de E1-A cuando la persona confirma y una entrada en Catálogos.
- El pendiente «borrador de formulario armado por la IA a partir de los pedidos de formularios que faltan» del
  [resumen de formularios](formularios/README.md#después-de-la-etapa-5).
- La IA propone, una persona aprueba y el sistema ejecuta ([ADR 0019](../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md)
  y [ADR 0020](../decisions/0020-motor-unico-de-ia-con-capacidades.md)).

**Tableros:** lienzo provisorio [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), filas «Armar con
la IA · 0» a «· 3» (IA0 a IA4), con la nota naranja de origen y una verde por elección (versión 73). Los ejemplos son
«Control de carga de camiones», desde el PDF de una planilla en papel; «Parte de lluvias», con un audio por WhatsApp, y el
catálogo «Contratistas», desde un Excel de 86 filas. El diseño de los elegidos quedó registrado en el front el 2026-11-04, en
[la IA arma formularios y catálogos](../../../ArquitecturaBaseFront/docs/design/ia-arma-formularios.md), como el de la
[carga desde la app](../../../ArquitecturaBaseFront/docs/design/carga-desde-la-app.md). El lienzo del sistema visual sigue sin
abrirse desde la cuenta de Claude Code (se volvió a probar el mismo día): los tableros pasan allí cuando se pueda y, mientras
tanto, vale el provisorio.

## Decisiones del usuario (2026-11-04)

Eligió de a una las dos filas con opciones; en las dos, la recomendada.

1. **La propuesta en el chat de Inicio: IA1-A, una tarjeta en el hilo.**
   - La respuesta trae, debajo del texto, una tarjeta con la banda «Formulario» o «Catálogo»: el nombre, el prefijo y cada
     pregunta con su tipo, si es obligatoria y sus opciones o su catálogo, en el orden del formulario. Un catálogo que usa
     dice si ya existe.
   - La de un catálogo muestra el código, la opción y los campos, cada uno con su tipo y la columna de la planilla de donde
     sale, y una tabla con tres filas de muestra («y 83 filas más»).
   - «Ver cómo queda» abre en un diálogo la vista previa del editor, la pantalla de WhatsApp. «Crear borrador» o «Crear
     catálogo» va en la banda y en el diálogo.
   - Se ajusta conversando («sacá la tara y agregá los kilos netos»). Cada cambio trae una tarjeta nueva; la anterior se
     pliega a una línea, «Reemplazada», y ya no se puede crear. Lo que cambió dice «Nueva» y lo quitado lo cuenta la
     respuesta.
   - Después de crear, la tarjeta queda plegada con «Borrador creado» y el enlace, y no se puede volver a crear.
   - Descartó IA1-B, la propuesta en un panel al costado del hilo, con las solapas «Preguntas» y «Cómo queda».
2. **Por WhatsApp: IA3-A, un resumen con botones.**
   - El asistente contesta con la propuesta en texto, que hace de vista previa: el nombre en negrita, el prefijo y hasta 10
     preguntas con su tipo («3. Milímetros · número»); con más, «y N preguntas más». Va en el idioma de la persona. La de un
     catálogo dice el código, la opción, los campos y cuántas filas trae la planilla.
   - Dos botones: «Crear borrador» o «Crear catálogo», y «Cambiar algo». «Cambiar algo» pide el cambio por escrito o con un
     audio y vuelve con otro resumen y los mismos botones.
   - Sólo vale el botón de la última propuesta de la conversación. Uno anterior contesta «Esa propuesta ya cambió. Usá los
     botones de la última.» y no crea nada.
   - Crear contesta con el enlace al borrador o al catálogo inactivo, para revisarlo y publicarlo desde la app.
   - El catálogo de personas con filtros (H4), que hoy se crea directo, pasa a contestar igual: resumen y «Crear catálogo».
   - Descartó IA3-B, crear el borrador en cuanto entiende el pedido y mandar el enlace, como hace hoy H4.

**Sin opciones, por lo ya decidido (IA2).** «Crear borrador» crea el formulario en borrador, sin publicar, y abre el editor
de siempre con las preguntas cargadas, como DOC6. «Crear catálogo» lo crea inactivo, con la banda «Propuesta del asistente»
de H4 («La pidió Ana Pérez en el chat de Inicio el 3 nov 2026.», «Descartar propuesta» y «Aprobar y activar»), y abre
«Importar opciones» con el archivo del pedido ya revisado; «Importar» aplica las filas por E1-A, con sus revisiones. La IA
nunca publica, activa ni importa.

**Resueltas por defecto** con la opción más simple, dibujadas en IA0 e IA4 y sin objeción del usuario:

- **Dónde.** En el chat de Inicio que ya existe, con el clip y el micrófono a la izquierda de la flecha; no hay otro chat.
  «Armar con la IA», junto a «Nuevo formulario» en Diseñar formularios, junto a «Nuevo catálogo» en Catálogos y en cada tema
  de Formularios que faltan, abre Inicio en una conversación nueva con el pedido empezado: «Armame un formulario para »,
  «Armame un catálogo de » o «Armame un formulario para registrar remitos de combustible». Con el permiso, las sugerencias de
  Inicio vacío suman «Armame un formulario para…» y «Armame un catálogo con una planilla».
- **Quién.** Formularios, quien puede diseñarlos (`formtemplates.manage`); catálogos, quien puede administrarlos
  (`formcatalogs.read` y `formcatalogs.manage`). Sin ninguno de los dos, Inicio queda como hoy y el clip no aparece; el
  micrófono sí, para cualquier consulta. Por WhatsApp, las cuentas vinculadas y activas con esos permisos. Sin el permiso,
  la IA lo dice y no arma nada.
- **Archivos.** Uno por mensaje, de los tipos y con los topes del ADR 0022: foto JPEG o PNG, PDF, Word `.docx` y Excel
  `.xlsx`, más CSV para un catálogo, como la importación. Lo que no se lee (formato viejo, con macros, con contraseña o más
  grande que el tope) se dice debajo del editor del chat y no se manda; por WhatsApp, como en DOC4.
- **Audio en la app.** Se graba en el navegador, con el mismo tope que los audios de WhatsApp: «Detener» lo deja listo para
  escuchar y «Descartar» lo borra. Al enviarlo se transcribe con `NormalizeAudio`, y la transcripción se ve en gris debajo
  del audio: es lo que lee la IA.
- **Un archivo por WhatsApp** va con un texto o un audio que pida armar («armame un formulario con esto»). Un archivo solo
  sigue el camino de los documentos (DOC2).
- **Lo que ya existe.** Antes de proponer, la IA lee los nombres y los campos de los formularios y catálogos que la persona
  puede ver, para no repetir un nombre y usar los catálogos que hay. Una lista que no es un catálogo queda como «Elegir una»,
  si el pedido trae las opciones, o como texto, y la respuesta ofrece armar el catálogo aparte.
- **Un pedido ambiguo**, como una planilla sin decir qué hacer, pregunta qué hacer. Importar en un catálogo que existe trae
  una tarjeta «Importación» con el mapeo, y su botón «Revisar la importación» abre la importación de ese catálogo con el
  archivo.
- **No cambia ni copia** formularios o catálogos que existen: lo dice y da el enlace al editor. Copiar sigue pendiente de
  decisión en [planes](README.md).
- **Un nombre o un prefijo en uso:** al crear, la tarjeta lo dice y no crea nada; se pide otro en el chat.
- **La procedencia** (canal, quién lo pidió y cuándo, perfil, instrucciones y capacidades, como pide el ADR 0020) queda
  guardada con el borrador y con el catálogo. El editor del formulario no suma nada; el catálogo la muestra en la banda de
  H4.
- **Desde un documento:** cuando la lectura de documentos esté programada, un formulario armado con un documento de ejemplo
  sale con «Lectura de documentos» prendida y su «Cómo reconocerlo», como en DOC6. Antes, sale sin ella.
- **Renglones:** cuando estén programados, lo que se repite, como los artículos de un remito, se propone como «Renglones»
  ([RG1-A y RG6-A](../features/formularios.md#el-bloque-de-renglones-al-armar-y-publicar--rg1-a-rg3-a-y-rg6-a-2026-11-04)).

## Cómo se arma

### Una sola propuesta, varios canales

- **Capacidades de BusinessForms** en el motor único (ADR 0020), sin otro cliente de IA:
  - una lectura con los nombres, los prefijos y los campos de los formularios y catálogos que el actor puede ver, sin
    respuestas, opciones cargadas ni personas;
  - la propuesta de un formulario entero: nombre, prefijo, texto del botón y preguntas con su tipo, si son obligatorias y sus
    opciones o un catálogo que existe, en el idioma de la conversación y con los límites de la autoría;
  - la propuesta de un catálogo Manual: nombre, tipo de valor, código, opción y campos con su tipo y, con una planilla, el
    mapeo de sus columnas; o la de importar una planilla en un catálogo que existe, con su mapeo.
- **La misma propuesta** sirve para DOC6 en Documentos recibidos, para el chat de Inicio y para WhatsApp. H4 conserva sus
  capacidades.
- **Validar.** BusinessForms valida la propuesta observada con las reglas de la autoría (tipos, largos, 40 preguntas,
  prefijo, catálogos que existen y que el actor puede usar) y la presenta normalizada. Un dato inválido rechaza la
  propuesta; no se recorta en silencio.
- **Crear.** «Crear borrador» y «Crear catálogo» llaman al servicio dueño con el Id de la propuesta: una sola UoW,
  idempotente por propuesta (dos toques no crean dos) y con los permisos actuales. Una propuesta reemplazada o vencida no se
  crea. Es la regla de H4, extendida a los formularios.

### El chat de Inicio

- **El canal Inicio** suma esas capacidades para quien tiene los permisos, y también las de H4, con la misma tarjeta (el
  nombre y los filtros). La salida terminal suma la selección de una propuesta observada, como en WhatsApp. Con las cinco
  lecturas de hoy y la respuesta son once definiciones; si la selección necesita una terminal propia, doce, justo el tope
  por turno.
- **La tarjeta** la arma BusinessForms con la propuesta validada, e Inicio la muestra por un punto de extensión del front, sin
  nombrar el módulo, como `installedFeatures`. «Crear borrador» llama a BusinessForms y navega al editor.
- **Adjuntos.** El archivo se guarda en la copia privada al enviar el mensaje y llega al motor como lo prepara la lectura de
  documentos (ADR 0022); de un Excel o un CSV, sólo los encabezados y unas filas de muestra. La importación que abre «Crear
  catálogo» lee esa copia en lugar de pedir subir el archivo, y compara su SHA-256 al aplicar, como E1-A.
- **Audio.** El navegador graba WebM u MP4, según cuál sea, y `NormalizeAudio` hoy admite Ogg y WAV: hay que sumar esos
  formatos al perfil de transcripción o convertirlos en el servidor, y calificarlo.
- **Lo que hoy dicen la [IA](../features/inteligencia-artificial.md) y el fundamento visual del front** (Inicio sólo
  consulta, sin adjuntos) se reescribe al programar.

### WhatsApp

- **El canal del asistente** ofrece las mismas capacidades a las cuentas vinculadas con los permisos. El resumen y los
  botones los arma el adaptador de WhatsApp con la propuesta validada; cada botón lleva el Id de la propuesta y el canal
  comprueba que sea la última de la conversación.
- **H4.** `WhatsAppAssistantTerminalPolicy` deja de crear el catálogo al recibir la propuesta: contesta con el resumen y
  crea con el botón.
- **Un archivo con un pedido de armar**, de una cuenta vinculada, va a la lectura del documento junto con el pedido, no a la
  clasificación de DOC2.

### Qué se manda al proveedor

El pedido, con el audio ya transcripto; el archivo preparado (de una planilla, sólo los encabezados y unas filas de
muestra), y la lectura de lo que existe: nombres y campos. Nunca respuestas, opciones cargadas ni datos de personas. Lo
leído es un dato, nunca una instrucción. Como en el ADR 0022, la cuenta del proveedor usa las opciones de no retener
contenido y se registra qué se mandó (hash y tamaño), sin el contenido.

## El ADR

El [ADR 0024](../decisions/0024-ia-arma-formularios-y-catalogos.md), «La IA arma formularios y catálogos a pedido», quedó
aceptado el 2026-11-04. Amplía el [0017](../decisions/0017-inicio-asistente-y-operaciones-durables.md)
(Inicio deja afuera las escrituras de negocio), el [0020](../decisions/0020-motor-unico-de-ia-con-capacidades.md) (capacidades
que proponen configuración en otros canales) y el [0022](../decisions/0022-lectura-de-documentos-con-ia.md) (leer documentos
también en el chat de Inicio y para armar, no sólo para cargar). Fija:

- Inicio, además de consultar, propone formularios y catálogos y acepta archivos y audios. Sigue sin escribir: crear es una
  acción de la persona, que ejecuta BusinessForms con su servicio y su UoW.
- La IA nunca publica, activa, importa ni cambia lo que existe, y tampoco crea sola: por WhatsApp, crear pasa a ser un botón,
  también en H4.
- Qué se le manda al proveedor para armar y qué no.
- Los archivos y los audios de la web: tipos, topes, la copia privada y su retención, que es la del contenido de la
  conversación.
- Los casos de evaluación antes de habilitar el perfil: pedidos escritos y dictados, PDF de planillas en papel (también
  escaneadas y fotos movidas), Excel con y sin encabezados, pedidos ambiguos, nombres que ya existen e instrucciones
  escondidas en el archivo.

## Lo que tiene que estar antes

- **La lectura de documentos del ADR 0022**, con su perfil calificado, para los PDF, las fotos y los Word. Sin ella, las dos
  primeras etapas arrancan con texto, audio y planillas, que el servidor ya lee.
- **Los renglones**, sólo para proponer bloques que se repiten.

## Plan por etapas

Cada paso queda igual a su tablero: al programarlo se abre el tablero y se compara antes de cerrarlo. El paso previo a la
Etapa 1 quedó hecho el 2026-11-04: el diseño está registrado en el front, con lo que entra en cada etapa, y el pase al lienzo
del sistema visual espera a que se pueda abrir. Cada etapa
sigue [validar un cambio](../guides/validar-un-cambio.md): TDD en las políticas, integración con PostgreSQL y Redis,
contratos exportados, y en el front tests, visuales y el recorrido es/en a 1440, 390 y 320, más una prueba física con el
proveedor de IA real y con Meta. Las piezas nuevas del chat (adjuntar, grabar y la tarjeta de la propuesta) van a
`shared/ui` con su ficha en el estándar visual.

1. **Etapa 1 · Pedir con texto o audio, proponer y crear (IA1-A, IA2 e IA3-A).**
   - Backend: la lectura de lo que existe, las propuestas de formulario y de catálogo sin planilla, la validación, la
     presentación y crear el borrador o el catálogo, idempotente. El canal Inicio con sus capacidades y la terminal que
     selecciona una propuesta. WhatsApp con el resumen y los botones, y H4 con el mismo recorrido.
   - Front: la tarjeta en el hilo, «Ver cómo queda», el micrófono con la transcripción y las entradas «Armar con la IA».
   - Los casos de evaluación y la calificación del perfil.
2. **Etapa 2 · Planillas.** El clip con Excel y CSV en el chat y por WhatsApp: los encabezados y las filas de muestra, el
   mapeo, «Crear catálogo» con la importación abierta e importar en un catálogo que existe.
3. **Etapa 3 · Documentos.** PDF, fotos y Word para armar, sobre la lectura del ADR 0022; con DOC6, la misma propuesta desde
   Documentos recibidos.

Al cerrar, las reglas van a [IA](../features/inteligencia-artificial.md), a [formularios](../features/formularios.md) y a
[WhatsApp](../features/whatsapp.md), el diseño queda en el front y se ofrece el traspaso al multitenant.

Fuera de alcance: cambiar o copiar con la IA un formulario o un catálogo que existe, varios archivos o varias propuestas en
un mensaje, armar reglas, aprobaciones o el diseño del PDF, y un chat de IA fuera de Inicio.
