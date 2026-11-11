# La IA arma formularios y catálogos · Etapa 2: planillas

Plan de implementación del 2026-11-09 para la Etapa 2 del [plan](2026-11-04-ia-arma-formularios-y-catalogos.md), con el
[ADR 0024](../decisions/0024-ia-arma-formularios-y-catalogos.md) y el diseño congelado en el front
([la IA arma formularios y catálogos](../../../ArquitecturaBaseFront/docs/design/ia-arma-formularios.md), fila «2 · Planillas»).
Se trabaja en modo liviano, como la Etapa 1: este diseño con un revisor, diez tareas, una revisión por tarea y la suite completa
al cerrar.

Alcance: una planilla, Excel `.xlsx` o CSV, una por mensaje, en Inicio con el clip y por WhatsApp como documento. Con ella la IA
propone un catálogo Manual con el mapeo de sus columnas, o importarla en un catálogo Manual que existe. En Inicio la propuesta es
la tarjeta de IA1-A con las columnas y tres filas de muestra, o la tarjeta «Importación» con «Revisar la importación»; por
WhatsApp, el resumen de IA3-A con «Son N filas». «Crear catálogo» lo deja inactivo con «Importar opciones» abierto y la planilla
ya revisada (IA2): importar sigue siendo de la persona. Inicio vacío suma «Armame un catálogo con una planilla». Un archivo que no
se lee lo dice y no llega a la IA. Quedan afuera los PDF, las fotos y los Word (Etapa 3), más de una hoja, varias planillas en un
mensaje y cambiar los campos de un catálogo que existe.

## Lo que hay

Verificado en el código el 2026-11-09:

- **La importación de opciones (E1-A)** recibe el archivo sólo por multipart: la vista previa no lo guarda y aplicar lo vuelve a
  mandar y compara el SHA-256. Los encabezados son fijos («Código», «Texto», el valor y los campos) y van siempre en la fila 1, así
  que una planilla con «CUIT» y «Razón social» da `MissingColumn`; el único mapeo vive en el front, al pegar. El lector
  (`IFormSpreadsheetReader`: ClosedXML y un lector CSV propio) acepta hasta 10 MiB, 64 columnas y 10.000 filas y decide el tipo
  por la extensión; un `.xls`, un `.xlsm` o uno con contraseña dan el mismo `FileUnreadable`.
- **`forms.authoring.propose-catalog@1`** propone un catálogo «sin opciones»: el código y la opción son descripciones libres y los
  campos no dicen de qué columna salen. Crear guarda la propuesta entera en la evidencia del catálogo, que ve cualquiera que pueda
  leerlo.
- **Inicio** acepta texto y audio. El audio es el patrón de esta etapa: copia privada `WriteOnce` antes de la UoW, huella con el
  descriptor, el servidor toma lo que necesita de su lado y nunca del navegador, y una fuente de retención conserva el archivo
  mientras el turno tenga contenido. La historia entra en 24.000 caracteres, del turno más viejo al más nuevo, y saltea lo que no
  entra; en un canal sólo viaja la última propuesta.
- **Las capacidades** ven sólo los argumentos del modelo y el contexto que fija el servidor; `AiAssistantCapabilityBoundaryTests`
  no les deja abrir archivos privados ni el lector de planillas. El reemplazo de una propuesta es por Id de capacidad.
- **Por WhatsApp**, un documento entrante queda como `Media`: no se baja, pierde su `caption` y, si es el último mensaje, corta la
  interpretación. La fuente es siempre el último mensaje, sin buscar hacia atrás, y una interpretación sin texto no se acepta. Los
  botones tienen hasta 20 caracteres.
- **Definiciones por turno:** Inicio usa 12 de 16, y el asistente automático 15 de 16 para una cuenta con todos los permisos.
- **El front de hoy** valida estrictos el bootstrap de Inicio y la propuesta del catálogo: un campo nuevo los rompe.

## Decisiones técnicas

Lo que el plan no fijaba, resuelto con la opción más simple que cumple el plan y las reglas:

1. **Leer la planilla es de BusinessForms, por un puerto neutral.** `IStructuredAttachmentReader`, en
   `Application/Interfaces/Channels` y con su versión apagada, dice quién puede adjuntar planillas (en esta etapa, quien administra
   catálogos) y recibe el contenido y el nombre: devuelve la planilla preparada o el motivo del rechazo, sin `Error` entre
   módulos. BusinessForms lo implementa con el lector de la importación. El tipo sale de los bytes (un ZIP con el manifiesto de una
   hoja de cálculo, o texto UTF-8), nunca de la extensión. Los rechazos son explícitos: Excel viejo (OLE2 con `Workbook`), con
   contraseña (OLE2 con `EncryptedPackage`; los dos se distinguen con OpenMcdf, que ya usa el proyecto), con macros (por el
   manifiesto), más grande que los topes de la importación, ilegible o sin filas. Lo preparado es la primera hoja: su nombre,
   cuántas filas con contenido y cuántas columnas tiene, y sus seis primeras filas, cada una con un mapa de la letra de la columna
   a su texto, en 12 KiB como mucho. Las filas vacías se saltean: «la fila 1» es la primera con contenido. Para entrar en el tope
   van siempre esa fila y todas las columnas: cada celda va hasta 80 caracteres; si no entra, se quitan filas hasta dejar cuatro (la
   primera y tres de muestra, las de la tarjeta); después se acortan las celdas a la mitad, y sólo al final se quitan las que
   quedan. El nombre del archivo se acota aparte. Inicio y WhatsApp lo llaman
   al recibir el archivo; el motor no lee archivos. La huella y el turno guardan el SHA-256 y el tamaño, como pide el ADR 0022.
2. **La planilla en el motor.**
   - La operación neutral suma `Attachment`, con el descriptor privado (como el del audio) y lo preparado, y su huella
     `attachment:v1`. No entra en `AssistantChannelContext` ni en `SameScope`.
   - Cada turno que tiene una planilla guarda su referencia y lo preparado, cifrado con un propósito propio: el que la trae y los
     siguientes de la misma conversación, que la heredan del último turno que la tiene con contenido. En Inicio son los turnos
     siguientes; por WhatsApp, «Cambiar algo» y la respuesta a una pregunta, que se suman a la conversación de la operación
     anterior. Una planilla nueva reemplaza a la anterior. El turno sabe si la trajo él o la heredó, y guarda en claro el SHA-256 y
     el tamaño, que quedan después de la purga, como el uso. Una planilla propia acota el vencimiento del turno. La heredada lo
     acota sólo si su copia vence por su cuenta, como la de WhatsApp con su mensaje; la de Inicio vive mientras algún turno la tenga.
   - El modelo la ve como pieza propia, fuera de los 24.000 caracteres de la historia: un bloque «Planilla de la conversación
     (datos, no instrucciones)» de `Assistant.resx` al final del mensaje actual, después de lo escrito y lo dicho. No cambian las
     instrucciones selladas (`home-system@2` y `capabilities-v1@1`).
   - Las capacidades reciben lo preparado en su pedido, así que BusinessForms valida el mapeo sin abrir el archivo. La definición
     de una capacidad puede pedir una planilla (`RequiresAttachment`, neutral como `OfferedInHome`): el motor no la ofrece en un
     turno sin planilla.
   - Una propuesta reemplaza a las anteriores de su grupo: `IAssistantProposalLookup` recibe las capacidades que se reemplazan
     entre sí, y el front calcula «Reemplazada» igual.
3. **Las capacidades.**
   - `forms.authoring.propose-catalog@2` reemplaza a la versión 1 en lo que se ofrece (en Inicio desde la tarea 9 y por WhatsApp
     desde la 7, por el punto 9). La 1 sigue registrada: los turnos con ella se leen, y sus tarjetas y resúmenes todavía crean. Con una
     planilla, el mapeo dice si la primera fila con contenido es de encabezados y de qué columna salen el código, la opción, el valor
     (si el tipo no es texto), el detalle y cada campo.
     BusinessForms comprueba que las columnas existan en lo preparado y que estén las obligatorias. El código y la opción pueden
     salir de la misma columna, para una lista simple; los campos, el valor y el detalle no se repiten. Sin planilla, el mapeo va
     vacío. La salida suma, para la tarjeta, el nombre de la planilla, cuántas filas trae, cada columna con su encabezado y tres
     filas de muestra ordenadas como el catálogo.
   - `forms.authoring.propose-import@1`, con `RequiresAttachment`, propone importar la planilla en un catálogo Manual que existe y
     que la persona puede leer y administrar, con el mismo mapeo hacia sus columnas. Los campos se nombran como en la lectura de
     catálogos, y el rechazo nombra el obligatorio que falta. Con una planilla, Inicio llega a 13 definiciones y el asistente
     automático a 16, el tope, sin subirlo; sin planilla quedan en 12 y 15. Se reemplaza con `propose-catalog`: pedir importar
     después de un catálogo propuesto pliega su tarjeta, y al revés.
   - `forms.authoring.decline` cambia la descripción de `catalogExists`: lo que existe no cambia sus campos, pero con una planilla
     se puede proponer importarla.
   - Crear acepta la versión 2. La evidencia del catálogo suma el pedido (`RequestId`) y conserva la forma de la versión 1, sin
     mapeo ni filas: el mapeo y la muestra se releen de la propuesta mientras dure, y la planilla no queda guardada en el catálogo.
     Crear desde la web vale sólo para un turno de Inicio; lo pedido por WhatsApp se crea con su botón.
   - El lookup suma las propuestas de la conversación propia por un canal (modo Asistente, con Actor igual a Subject, el criterio
     neutral que ya usa crear) y el descriptor de la planilla del turno. Una propuesta está vigente si ninguna posterior de su grupo
     la reemplazó en su conversación del motor.
4. **Importar desde la copia.** `POST api/forms/catalogs/{id}/assistant-import-previews` y
   `POST api/forms/catalogs/{id}/assistant-imports` reciben en JSON el `requestId` y el `proposalId` en lugar del archivo. El
   servidor relee la propuesta con el lookup, abre la copia, compara su SHA-256 y arma en memoria la hoja con los encabezados de la
   importación según el mapeo, conservando el origen canónico de cada celda: la vista previa, las confirmaciones, los locks, la UoW
   y `MarkImported` son los de siempre. Vale para el catálogo creado desde esa propuesta, sólo para quien la pidió, y para una
   propuesta de importar que siga vigente. **Modo:** reemplazar, como subir un Excel hoy (decisión del usuario del 2026-11-09).
   Las opciones que no vienen quedan inactivas, con la revisión y la confirmación de siempre; en un catálogo recién creado, sin
   opciones, da lo mismo. Una copia vencida da su código y el diálogo vuelve a pedir el archivo. La importación pendiente de un
   catálogo creado desde una propuesta con planilla se consulta con su propio `GET api/forms/catalogs/{id}/assistant-import`,
   sólo para quien la pidió: el detalle del catálogo no cambia de forma.
5. **Inicio.** El turno con una planilla sube en un solo pedido multipart (`POST api/ai/assistant/file-turns`, los campos del turno
   más el archivo, hasta 10 MiB): sin tabla nueva ni otro identificador, porque leer una planilla no espera a ningún proveedor. El
   servidor la prepara por el puerto, deja la copia `ai/assistant/attachments/{requestId}.xlsx` o `.csv` con `WriteOnce` y
   relectura del SHA-256 antes de la UoW, y acepta el turno con su huella; repetir el pedido recupera el mismo turno y otro
   contenido con el mismo Id da 409. Un archivo que no se lee da 400 en `file` con su código, sin turno ni copia; quien no puede
   adjuntar, 403. Lo escrito pasa a ser opcional y el turno puede traer además un audio (`audioRequestId`); sin texto, el título es
   el nombre del archivo. El turno responde `attachment` para la burbuja (nombre, tipo, filas y tamaño). La fuente de retención
   conserva la copia mientras algún turno que la tenga conserve su contenido. El bootstrap suma `attachments`, por persona: lo
   dice el puerto, así que el front no repite la regla de permisos.
6. **WhatsApp.**
   - El webhook lee el documento (id, tipo, SHA-256, nombre y `caption`) en un tipo entrante nuevo, con su descriptor, su copia,
     su estado y sus fallas como las del audio, y su migración; `Document` es saliente y no se reusa. Se baja sólo una planilla,
     por el tipo o el nombre que manda Meta y después por los bytes, hasta 10 MiB, de una cuenta vinculada y activa que puede
     adjuntarla, en su conversación del Asistente automático. Lo demás sigue como hoy. Al bajarla, fuera de la UoW, se guarda lo
     preparado con el mensaje, que vence con su cuerpo, y la copia `whatsapp/documents/{id}` tiene su fuente de retención.
   - El pedido es el `caption`. Sin `caption`, la planilla se junta con el texto o el audio contiguo, antes o después, si no hay
     una respuesta del asistente en el medio y llegaron a menos de diez minutos. Esto cambia la regla del lector de «no buscar
     hacia atrás»: se actualizan su `AGENTS.md` y `WhatsAppAssistantSourceQueryTests`. Una planilla sola no se interpreta, porque
     el plan general la manda al camino de los documentos (DOC2), que no está programado: queda en el chat, bajada, como hoy.
   - Una planilla también puede responder a «Cambiar algo» o a la pregunta, y pasa a ser la de la conversación. Si llega sin texto,
     el motor la acepta sólo como continuación (con `PreviousRequestId`); fuera de una continuación, una interpretación sigue
     necesitando texto.
   - Una planilla que no se lee contesta un texto fijo con el motivo y no llega a la IA. El texto pide reenviarla junto con el
     pedido, porque una planilla sola no se interpreta.
   - El resumen de un catálogo con planilla suma «Son N filas.». El de importar dice el catálogo, el mapeo y las filas, con
     «Revisar importación» (el dibujo dice «Revisar la importación», que no entra en los 20 caracteres de un botón) y «Cambiar
     algo». El toque contesta con el enlace a la importación de ese catálogo en la app (`/catalogos/{id}?pedido=…&propuesta=…`):
     el puerto del canal suma el tipo `Import` y BusinessForms arma el enlace con el pedido y la propuesta que le pasa el canal.
   - Si la IA pregunta con una planilla, la pregunta tiene su propio texto («¿Qué querés que haga con esta planilla? …»).
   - En el chat del operador, la planilla se ve con su nombre en lugar de «Otro».
7. **El clip aparece cuando el bootstrap dice `attachments`.** En esta etapa eso es para quien administra catálogos
   (`formcatalogs.read` y `formcatalogs.manage`), porque una planilla sólo sirve para un catálogo. Con los documentos de la Etapa 3
   el puerto suma a quien diseña formularios, como dice IA4.
8. **El front:**
   - el clip y el archivo elegido, como pieza nueva de `shared/ui/assistant`;
   - el envío con su espera y su progreso, y el motivo de lo que no se lee debajo del editor;
   - el archivo en la burbuja y la sugerencia de Inicio vacío, que escribe el pedido y abre el selector de archivos;
   - las tarjetas con sus columnas y la muestra, y la tarjeta «Importación», con el reemplazo por grupo;
   - `CatalogImportDialog` con la planilla del asistente. Se abre solo en el catálogo creado desde la propuesta (pendiente, sin
     opciones, para quien la pidió y con la propuesta disponible), desde «Revisar la importación» con el estado de la navegación y
     desde el enlace de WhatsApp;
   - la planilla en el chat del operador.
9. **Contratos y su front.** Nada que el front rechace, ni nada que le saque algo que hoy muestra, llega a `main` antes que su
   front. El `attachments` del bootstrap se publica con la tarea 8; las rutas nuevas (el turno con
   archivo, importar desde la copia y la importación pendiente) no las llama el front de hoy. El front de hoy dibuja sólo la versión 1 de `propose-catalog`, así que Inicio la sigue
   ofreciendo hasta la tarea 9: la versión 2 y `propose-import` quedan registradas sin `OfferedInHome` y la 9 da vuelta las
   marcas. WhatsApp, que no depende del front, pasa a la 2 y suma `propose-import` en la tarea 7. Lo demás no rompe nada: el front
   de hoy no puede mandar un turno con archivo y no recibe `Manual` con otra forma.

## Tareas

Cada tarea sigue [validar un cambio](../guides/validar-un-cambio.md): TDD en las políticas, build y tests rápidos del área en cada
corte. Las del backend escriben y prueban sus capturas. Los ejemplos HTTP los exporta una sola vez, al final de la tanda y con
todas las cohortes, el chat que la coordina; esa corrida vale además como verificación completa. Los tests del front que leen
capturas nuevas entran después de esa exportación. Desde el 2026-11-11 el front no espera a la prueba integral: a pedido del usuario, cada desarrollo se termina
entero y la prueba integral se hace una sola vez al final.

1. **Leer planillas para el asistente.** El puerto, su versión apagada y la implementación en BusinessForms, con quién puede
   adjuntar y los rechazos por contenido. Unitarias de lo preparado (filas, columnas y el orden del recorte) y pruebas del lector
   con un archivo real de cada caso: `.xls`, cifrado, `.xlsm`, CSV en UTF-8 y en ANSI, una columna, vacío y pasado de tope.
2. **La planilla en el motor.** `Attachment` en la operación neutral y su huella, la referencia y lo preparado en el turno (propio
   o heredado) con su migración y la purga, el bloque del mensaje actual, lo preparado en el pedido de la capacidad,
   `RequiresAttachment` y el grupo de reemplazo del lookup. Unitarias de contexto, herencia, huella y catálogo, y la prueba de
   arquitectura.
3. **El turno con planilla en Inicio.** El turno multipart, la copia y su retención, el título, `attachment` en el turno y los
   errores en `AiAssistantErrors` con sus textos; el bootstrap queda listo para la tarea 8. Integración con PostgreSQL (dueño,
   replay, rechazos, audio y planilla, retención), el inventario de rutas y la cohorte `ai-assistant-http`.
4. **Propuestas con planilla.** `propose-catalog@2` y `propose-import@1` registradas sin `OfferedInHome` (Inicio sigue con la 1),
   `decline`, la presentación con la muestra, crear con las dos versiones y la evidencia con el pedido, y el lookup para la
   conversación propia de un canal con el descriptor. Unitarias del mapeo (válidos y rechazados, una columna), del reemplazo por
   grupo, del lookup y de crear.
5. **Importar desde la copia.** Los dos endpoints, la hoja armada con el mapeo, reemplazar, quién puede, la copia vencida y el
   `GET` de la importación pendiente. Unitarias de la hoja mapeada y del servicio, integración con PostgreSQL (crear y abrir la
   importación, importar en uno que existe y en un propuesto pendiente, ajeno, vencido) y el inventario de rutas. Las capturas de
   las cohortes `catalog-imports` y `forms-assistant-proposals-http` van con la tarea 9.
6. **WhatsApp: el documento entrante.** El webhook (`MetaWebhook.Document`), el tipo nuevo, sus columnas y la migración, la
   descarga y su verificación, lo preparado con el mensaje, la copia y su retención, el texto de lo que no se lee y la vista
   previa del chat. Unitarias e integración con PostgreSQL.
7. **WhatsApp: el asistente con la planilla.** La fuente con el emparejado en los dos órdenes, el reemplazo de una interpretación
   en curso y la guía del lector; la operación con la planilla, la planilla como respuesta, la pregunta con su texto, el pase a
   `propose-catalog@2` y `propose-import@1` del asistente automático, los resúmenes con filas y el de importar con su enlace. Unitarias de la fuente y de los resúmenes, e integración con PostgreSQL del recorrido.
8. **Front: el clip y la planilla en el hilo.** El bootstrap publicado (`AiAssistantReadPolicy` está en el tope de dependencias), el
   compositor, el envío multipart con su progreso, la burbuja, el motivo, la sugerencia y el chat del operador. La captura del turno
   con planilla entra a la cohorte `ai-assistant-http` acá, con el inventario del exportador, su autotest y el test del front, que
   fijan seis cuerpos. Tests y visuales es/en a 1440, 390 y 320.
9. **Front: las tarjetas y la importación.** Inicio pasa a ofrecer `propose-catalog@2` y `propose-import@1` (se dan vuelta las
   marcas `OfferedInHome` en el backend). Las tarjetas con columnas y muestra, la tarjeta «Importación», el reemplazo por grupo,
   `CatalogImportDialog` con la planilla del asistente y sus tres aperturas. Si la planilla ya no está (`ProposalUnavailable` o
   `SheetUnavailable`), el diálogo vuelve a pedir el archivo. Las capturas de `catalog-imports` y `forms-assistant-proposals-http`
   entran acá, con el inventario del exportador y el test del front. Tests, visuales y contratos.
10. **Casos de evaluación y cierre.**
    - Planillas ficticias: Excel con y sin encabezados, CSV, una lista de una columna, una para importar en un catálogo del
      fixture y otra con una instrucción escondida en una celda. Casos es/en en Inicio y por WhatsApp: armar un catálogo, importar,
      un ajuste, la instrucción escondida, una planilla con un pedido dictado (en Inicio, el audio en el mismo turno; por WhatsApp,
      la nota de voz contigua) y, en Inicio, una planilla sin texto que pregunta. Los rechazos van como controles offline.
    - El inventario del arnés compara los Ids de las capacidades y corta con las dos versiones de `propose-catalog` registradas:
      pasa a comparar Id y versión del contexto.
    - Vuelven a correr los sets cuyo catálogo cambia: `ia-authoring` (`propose-catalog`), `channel-stage5` y `channel-dictation`
      (`decline` se ofrece en todo turno automático) y `home-existing` si sus cuentas ven `propose-catalog`.
    - La corrida con el proveedor real espera el visto bueno del usuario. La prueba física con Meta manda un `.xlsx` y un `.csv`
      reales desde el celular, y muestra con qué tipo los manda Meta.
    - Reglas a `docs/features`, los `AGENTS.md` que cambian (el del asistente en `shared/ui`, el del lector de WhatsApp y los de los
      módulos), las guías para quitar Formularios, IA y WhatsApp, las diferencias con el dibujo en el front, suites completas y el
      traspaso al multitenant.

## Riesgos

- **Historia.** La última propuesta viaja entera en la historia, y con planilla lleva sus encabezados y tres filas de muestra, que
  repiten lo que ya va en el bloque de la planilla. Como la historia se llena de la más vieja a la más nueva, puede quedar afuera la
  propuesta a ajustar: se mide en la calificación y, si hace falta, la muestra sale de la historia.
- **Contexto.** La planilla preparada, hasta 12 KiB, viaja en cada paso del turno, además de la historia y las lecturas: se mide en
  la calificación, con un ajuste con historia, contra los 131.072 bytes.
- **El texto de una opción admite 30 caracteres**, el límite de WhatsApp. Muchas razones sociales lo pasan, y la vista previa las
  marca con error: la IA puede elegir otra columna, pero no recorta.
- **CSV sólo en UTF-8.** Un CSV de Excel en un Windows en español suele salir en ANSI y no se lee: el motivo pide guardarlo como
  «CSV UTF-8».
- **WhatsApp.** El emparejado depende del orden de llegada y de la descarga; Meta puede mandar un CSV como `text/plain` o como
  `application/vnd.ms-excel`. Las pruebas cubren los dos órdenes, el reemplazo de una interpretación en curso y esos tipos.
- **`forms-export`** admite una operación por persona: abrir la importación mientras corre otra da 429.
- **Catálogos de ARCA.** ARCA 3 sumó el tipo `External` (08c83c19), las tablas de ARCA que mantiene el módulo y no se editan. La
  lectura de autoría los muestra como `manual`, para que sirvan de fuente de preguntas. Importarles una planilla da `notManual`. Si
  en la calificación el modelo intenta importar ahí, la lectura suma el tipo `external`.

## Estado

2026-11-09: plan escrito y revisado (17 hallazgos, incorporados). El usuario eligió que importar en un catálogo que existe
reemplace la lista, como subir un Excel hoy, y aprobó el plan el mismo día.

2026-11-09: tarea 1 hecha (3577b62b). `IStructuredAttachmentReader` recibe los bytes y el nombre, y `CanAttachAsync` resuelve
quién puede adjuntar sin usuario actual. BusinessForms detecta el formato con una operación nueva del lector y prepara la
planilla con `FormAttachmentPreparationPolicy`. Cualquier parte de Excel habilitada para macros cuenta como «con macros»,
también un `.xlsb`. El commit se llevó por error un arreglo del snapshot que otra sesión tenía en el índice compartido; HEAD quedó
correcto. La revisión (9 hallazgos, ninguno bloqueante) quedó corregida en 28b4268e. Cada fila de la muestra es un mapa de letra a
texto. El recorte deja siempre cuatro filas, y la memoria queda acotada aunque las celdas sean gigantes. `CompoundDocument`,
extraído de `FileContentProcessor`, separa el Excel viejo del protegido con contraseña, y otro OLE2 es ilegible. Los motivos de
rechazo quedan en siete: no disponible, Excel viejo, con contraseña, con macros, pasado de tope, ilegible y vacío. Unitarias
4070/4070, arquitectura 207/207 y las técnicas del lector en verde, sobre una copia de HEAD, porque el árbol compartido no compila
por trabajo ajeno sin commitear.

2026-11-09: tarea 2 hecha (515397a4): `AssistantAttachmentInput`, las huellas `attachment:v1` y `home-attachment:v1`, el turno con
`AttachmentKey` y `ProtectedAttachment` (migración `AddAssistantTurnAttachment`), la herencia, el bloque `Prompt.Attachment`, la
planilla en el pedido de la capacidad, `RequiresAttachment` y el grupo del lookup. Su revisión encontró un punto que importa: la
huella de Inicio llevaba el vencimiento, y un reintento daba 409. También encontró siete menores, que se corrigen después de la
tarea 3.

2026-11-09: tarea 3 hecha (c77c741f): `POST api/ai/assistant/file-turns`. El reintento compara la planilla con el descriptor
guardado, el validador corre después de preparar, hay ocho códigos de error, y el turno propio se distingue del heredado por la
clave de la copia, sin columna nueva. También suman el título y la historia de un turno sin texto, la fuente de retención y la
regla del bootstrap sin publicar. La captura de la cohorte `ai-assistant-http` va con la tarea 8, junto con el inventario del
exportador y el test del front, porque los dos fijan seis cuerpos. Para publicar `attachments`, `AiAssistantReadPolicy` ya tiene
ocho dependencias, el tope de `ServiceDependencyLimitTests`.

2026-11-09: las revisiones de las tareas 2 y 3 (ninguna bloqueante) quedaron corregidas juntas en 731b3198:
- la huella de Inicio no lleva el vencimiento, y un reintento superpuesto recupera el turno;
- una copia de Inicio heredada toma el vencimiento del turno nuevo;
- el turno guarda en claro el SHA-256 y el tamaño (migración `AddAssistantTurnAttachmentHashAndSize`);
- la planilla cifrada se lee sólo donde se usa (la burbuja, la historia y el título de los turnos propios);
- el reintento compara el SHA-256 y el tamaño antes de descifrar, y da 404 o `ContentUnavailable` como el turno JSON;
- una continuación de un canal acepta una planilla sin texto;
- sin almacenamiento privado da `AttachmentUnavailable`, y el 403 queda para el permiso, con su remedio.

Unitarias 4325/4325, dominio 821/821, arquitectura 207/207 e integración de las nueve clases tocadas (52/52), sobre una copia de
HEAD.

2026-11-10: tarea 4 hecha, en dos commits:
- 70b01b9c, en BusinessForms: `propose-catalog@2`, con la salida nueva `FormAuthoringMappedCatalogProposal` (los campos de la 1
  más `spreadsheet`), su mapeo (`hasHeaders`, código, opción, valor, detalle y una letra por campo) y el resumen para la tarjeta;
  `propose-import@1`; cinco códigos de rechazo nuevos; `decline`; y crear con las dos versiones. La evidencia suma `RequestId`
  sólo desde Inicio: el puerto del canal lo suma en la tarea 7.
- ecdc74d0, en el módulo IA: el lookup encuentra la conversación propia de un canal y devuelve el descriptor de la planilla del
  turno, sin vencimiento.

Inicio sigue ofreciendo la versión 1. Unitarias 4366/4366, arquitectura 207/207, dominio 821/821 e integración 11/11. Queda por
confirmar en la corrida real que OpenAI acepte un objeto que puede ser null en el modo estricto. La revisión no encontró nada
bloqueante, y su corrección entró en f84711cc. Crear desde la web vale sólo para un turno de Inicio, también en H4. El encabezado
es la primera fila con contenido. En la importación, dos campos con el mismo nombre visible dan `ambiguous`, y el modelo elige por
el nombre original. Unitarias 4374/4374 e integración 11/11.

2026-11-10: tarea 5 hecha (9354af77). Las dos rutas de importar desde la copia responden la misma vista previa y el mismo recibo
que subir un Excel, en modo reemplazar, y lo que corre dentro de la UoW pasó a `FormCatalogImportRecorder`, compartido con la
subida. `FormCatalogAssistantImportPolicy` decide qué propuesta vale. Una copia que falta o no coincide da
`Forms.CatalogImport.SheetUnavailable` (409). El `GET assistant-import` devuelve la importación pendiente o `pending: null`. La
hoja mapeada usa los encabezados de la plantilla y conserva el origen de cada celda. Unitarias 4417/4417, arquitectura 207/207,
dominio 821/821 e integración 73/73 en las clases tocadas. Las capturas de las cohortes de importación y de propuestas van con la
tarea 9. La revisión no encontró nada bloqueante, y su corrección entró en f56af5c6:
- el `GET` de la importación pendiente usa el cupo de lectura y no abre la copia;
- una columna de un campo que ya no está activo se avisa como `UnknownColumn`;
- sin administrar catálogos no hay importación pendiente;
- la regla de «fila con contenido» queda en un solo lugar, `FormSpreadsheetContentPolicy`.

Unitarias 4421/4421 e integración 116/116.

2026-11-10: tarea 6 hecha (a1026298). Una planilla que llega por WhatsApp entra como `WhatsAppMessageKind.Spreadsheet`, por el
nombre o el tipo (`text/plain` sólo con `.csv`), con su `caption` como cuerpo. Se baja sólo para una cuenta vinculada y activa, en
modo Asistente automático y con permiso, con un tope de 10 MiB por lo declarado y por los bytes y con el SHA-256 de Meta. La copia
`whatsapp/documents/{id}` y lo preparado vencen con el cuerpo del mensaje (migración `AddWhatsAppInboundSpreadsheets`). Un rechazo
se contesta una vez con su motivo. El chat del operador y la bandeja la siguen viendo como un archivo sin cuerpo, hasta la tarea 8.
Unitarias 4449/4449, dominio 843/843, arquitectura 207/207 e integración 220/220 en las clases nuevas y tocadas. Queda para la
prueba física con Meta confirmar que el `sha256` y el `mime_type` de un documento coinciden con los del webhook. La revisión no
encontró nada bloqueante, y su corrección entró en 7aa0b24f:
- el rechazo de una planilla ya no es un turno de la conversación ni una oferta sin resolver;
- la planilla se baja antes de interpretar, y una pendiente hace sondear cada 2 s;
- una excepción cuenta como falla;
- el nombre del archivo se normaliza una sola vez, con uno por defecto;
- un cambio de modo o de vínculo durante la descarga la deja no disponible.

2026-11-10: tarea 7 hecha (ebef6d31). `WhatsAppAssistantSourcePolicy` decide la fuente y el emparejado. La referencia guarda qué
planilla lleva (`AttachmentMessageId`, migración `AddWhatsAppAssistantAttachmentMessage`). El automático ofrece
`propose-catalog@2` y `propose-import@1`: 16 definiciones con planilla y 15 sin ella. El resumen de un catálogo dice «Son N
filas.», y el de importar trae «Revisar importación» y «Cambiar algo». El toque `review` contesta con el enlace
`/catalogos/{id}?pedido=…&propuesta=…`, que arma BusinessForms. La pregunta con planilla tiene su texto, y crear desde un canal
guarda el pedido en la evidencia. Unitarias 4505/4505, dominio 848/848, arquitectura 207/207 e integración de 17 clases (123/123).
La revisión no encontró nada bloqueante. Dos puntos importaban, y quedaron corregidos en 74372d8c (el motor) y 5913cade (WhatsApp):
- Reemplazar una interpretación en curso que continuaba un «Cambiar algo» o una pregunta perdía la conversación, y el turno
  cancelado trababa las continuaciones siguientes (`RevisionConflict`, sin respuesta). La referencia que reemplaza continúa lo
  mismo que la reemplazada. En el motor, una continuación de un canal se suma al final de la conversación aunque después de la
  operación pedida haya otros turnos, siempre que no haya una activa. La vigencia de la propuesta la controla el canal.
- Reenviar sola una planilla rechazada no obtenía respuesta. Los textos de rechazo piden reenviarla junto con el pedido.

Al reemplazarla, la interpretación reemplazada deja como cambio pendiente lo que continuaba, y la que la reemplaza lo sigue. Una
que termina por una falla no deja nada que continuar. En el motor, mientras haya una operación activa en la conversación,
continuar da `ActiveTurn`, que se reintenta. Además:
- `SetCapturedSuggestion` rechaza una referencia con planilla;
- quedan documentados el orden con una planilla no disponible y que «Revisar importación» necesita el front de la tarea 9.

Unitarias 4548/4548, dominio 855/855, arquitectura 207/207 e integración de 13 clases (128/128), con seis casos nuevos del
emparejado contra PostgreSQL.

Quedan dos casos raros, aceptados:
- Si la persona contesta a una pregunta con el «Cambiar algo» del resumen anterior, la historia lleva la pregunta como última
  propuesta, no el resumen: en un canal sólo viaja la última.
- Si una interpretación reemplazada terminó justo antes de cancelarse, su propuesta queda en la historia aunque WhatsApp no la
  mostró.

2026-11-11: a pedido del usuario, cada desarrollo se termina entero, front incluido, y la prueba integral se hace una sola vez al
final; la verificación completa la corre una sola vez el chat que coordina la tanda. El contrato de Inicio en el front suma
`attach` en una sugerencia (abre el selector de archivos) y `replacementGroup` (front d363ee7f). Las tareas 8 y 9 están en curso.
`CatalogImportDialog` y `CatalogEditorPage` los toca primero «Diálogo de exportar», y la tarea 9 suma lo suyo sobre su versión.

2026-11-11: los casos del arnés con planillas quedaron escritos (64f91d70), sin correr nada contra el proveedor:
- `home-spreadsheets`: 18 casos y dos dictados WebM;
- `channel-spreadsheets`: 12 casos y dos notas Ogg;
- `ia-spreadsheets`: los 30 juntos.

Las planillas ficticias están commiteadas con su SHA-256, y el catálogo «Proveedores» es el de importar. El inventario del arnés
compara Id y versión, y quedan 206 controles offline en verde. `channel-spreadsheets` se puede correr ya. Inicio necesita que la
tarea 9 dé vuelta lo que ofrece; después, `ia-authoring`, `channel-stage5` y `channel-dictation` se alinean con lo que ofrece la
app (`propose-catalog@2` y la importación) y vuelven a correr. `home-existing` no, porque sus cuentas no ven `propose-catalog`.

2026-11-11: tarea 9, lo que no espera, hecho.
- **Backend:** Inicio ofrece `propose-catalog@2` y `propose-import@1`, y deja de ofrecer la 1 (6e27fa0b): 12 definiciones sin
  planilla y 13 con ella.
- **Capturas** (bd0beb97): la cohorte de importaciones suma 6, `assistant-pending`, `-preview`, `-applied`, `-none`,
  `-proposal-unavailable` y `-sheet-unavailable`, y queda en 39. La de propuestas suma 2, `catalog-mapped-created` y
  `catalog-mapped-detail-pending`, y queda en 16.
- **Front:** la API de importar desde la copia (95364038), y las tarjetas del catálogo con planilla y de «Importación», el
  reemplazo por grupo y la sugerencia con `attach` (add86775), con 12 escenas nuevas (`assistantSpreadsheets.spec.ts`).

Pruebas: unitarias 4546/4546, arquitectura 207/207, integración 12/12, vitest 56/56 de lo tocado y 99/99 de lo vecino, lint y
`tsc -b` sin errores. Faltan el diálogo y la página, que esperan a «Diálogo de exportar», y los contratos del front, que esperan a
la exportación conjunta.

2026-11-11: el arnés califica lo que ofrece la app (54ebca97), con 206 controles offline:
- `ia-authoring` pasa a la versión 2 en Inicio y en el canal; los catálogos sin planilla esperan `spreadsheet` en null;
- los contextos automáticos de `channel-stage5` y `channel-dictation` son los de `WhatsAppAssistantAccessPolicy`;
- `home-spreadsheets` e `ia-spreadsheets` ya arrancan.

La corrida con el proveedor real, con el visto bueno del usuario, es de tres sets, cada uno con su RunId y su base vacía:
- `ia-authoring`: 48 casos, 4 dictados WebM/MP4 y 2 notas Ogg, con los dos de `channel-dictation`;
- `channel-stage5`: 30 casos;
- `ia-spreadsheets`: 30 casos, 2 WebM y 2 Ogg.

A mirar en esa corrida:
- que OpenAI acepte en modo estricto el mapeo en null;
- el contexto con la planilla;
- que una planilla sin texto pregunte en Inicio;
- que un pedido que no dice qué hacer con la planilla dé `unclear`.

2026-11-11: el usuario dio el visto bueno («Sí, corréla»). La corrida real corre desde una copia limpia de be8048ce (autotest
206/206), con la conexión guardada (OpenAI `gpt-6.1-sol`, habilitada y con clave), en tres runs seguidos:
`ia2_spreadsheets_20261111_01`, `ia2_authoring_20261111_01` e `ia2_channel_stage5_20261111_01`. Cada uno espera a que no haya
suites del backend corriendo. El primer intento se cortó al sembrar el catálogo «Proveedores», antes de llamar al proveedor: un
campo nuevo no puede nacer obligatorio. El arnés lo crea con sus campos opcionales y marca «Rubro» en un segundo guardado, y una
siembra fallida imprime su código de error y las rutas de los campos (51db5a59). La corrida se relanzó desde 51db5a59, con
`ia2_spreadsheets_20261111_02`.

2026-11-11: la corrida real quedó en verde entre las 12:36 y las 13:06:
- `ia-spreadsheets`: 30/30 casos, 4/4 normalizaciones y 892/892 checks;
- `ia-authoring`: 48/48, 6/6 y 1038/1038;
- `channel-stage5`: 30/30 y 434/434.

Entre las tres: 942.240 tokens de entrada y 20.953 de salida. El contexto máximo fue de 23.709 bytes, con la planilla, y el caso
más largo, de 19,66 s. Pasaron los cuatro puntos a mirar. La evidencia está en
[configurar IA](../guides/configurar-ia.md#evidencia-para-habilitar-perfiles). Las bases de las tres corridas verdes quedan hasta
cerrar la etapa.

2026-11-11: tarea 9 hecha (front 4ec7b7d1), sobre el diálogo y la página que rehízo «Diálogo de exportar» (f8a1c98a y
3e36fc87).
- **El diálogo:** «Desde» suma «La planilla del chat: {nombre} ({N} filas)», que abre seleccionada y se revisa sola desde la copia
  del turno, en modo reemplazar y con las confirmaciones de siempre. Con `ProposalUnavailable`, `SheetUnavailable` o
  `ProposalReplaced`, el diálogo dice el motivo y pasa a «Subir un archivo».
- **La página:** abre el diálogo sola una vez por carga, sólo con administrar catálogos. Lo hace desde el estado de la tarjeta,
  desde el enlace de WhatsApp o desde el `GET` de la importación pendiente, y después saca el estado y los parámetros sin crear una
  entrada.

Pruebas: vitest de lo tocado 221/221, lint y `tsc -b` sin errores, y 12 escenas nuevas (`catalogAssistantImport.spec.ts`). Las de
catálogos siguen sin diferencias. A 320 px, el «Desde» con dos opciones se cortaba en cualquier teléfono. Ahora baja de renglón en
el celular, con el `wrap` del control (front 22598fcd).

Falta, después de la exportación conjunta que hace el chat coordinador:
- los tests del front que leen las capturas nuevas (`ai-assistant-http`, `whatsapp-audio-http`, `catalog-imports` y
  `forms-assistant-proposals-http`);
- volver requerido `attachments` en el zod del bootstrap.

Después de la prueba integral se retira el plan y se arma el traspaso al multitenant.

2026-11-11: tarea 8 hecha.
- **Backend:** el inventario de cohortes pasa a `formularios-pruebas-de-integracion.md`, y la guía de pruebas de BusinessForms
  vuelve a entrar en el tope (007e4489). El bootstrap dice `attachments` por persona, con `AiAssistantBootstrapPolicy` (d99a9b63).
  Las capturas `file-submitted` y `turn-attachment` llevan `ai-assistant-http` a 8 cuerpos (07c300a4). El chat del operador
  muestra la planilla con su nombre y su `caption`, y la bandeja dice «Planilla» (45e81319).
- **Front:** `formatFileSize` en un solo lugar (65dd22e5). El clip, el envío con su progreso, el motivo de lo que no se lee y la
  planilla en el mensaje (d42603dc). La planilla en el chat de WhatsApp (faf9b7c9). Son 24 escenas visuales nuevas.

Unitarias 4625/4625 e integración de las clases tocadas 45/45. Hasta la exportación conjunta, `attachments` va opcional en el zod
del front, para que el test de contrato siga en verde con la captura de hoy (front 8bf31247). En ese mismo commit, el micrófono, el
clip y la X habilitados pasan a verse grises, como la flecha, y se regeneraron sus 24 referencias. Después de la exportación se
vuelve requerido `attachments` y entran los tests de las capturas nuevas.
