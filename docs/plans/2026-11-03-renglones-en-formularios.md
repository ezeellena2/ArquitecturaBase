# Renglones en los formularios

Diseño del 2026-11-02 con las decisiones del usuario del 2026-11-03, que ese día dio el diseño por cerrado, y las del
2026-11-04 sobre el tope de la respuesta de WhatsApp (RG7-A), el bloque con mínimo 0 (RG8-A) y cómo se elige «Renglones» en el
editor (RG9-A), la del 2026-11-06, «Agregar bloque» (RG10-D), y las del 2026-11-07, «ítem» en lugar de «renglón» en los textos
(RG12-A) y un mínimo mayor que el tope de WhatsApp (RG13-A). La Etapa 1 se está programando ([plan de implementación](2026-11-04-renglones-etapa-1-implementacion.md)). Un formulario suma un bloque de
preguntas que se repite, como los renglones de un remito o de una factura: en la app se cargan en una tabla y por WhatsApp,
de a un renglón por pantalla.

**De dónde sale.** En la prueba completa del 2026-11-02, el usuario cargó en «Recepción de insumos» (prefijo INS) el remito
0003-00004521, que tiene tres artículos. Hoy cada artículo es una respuesta, y dijo: «si son varios artículos, debería dejar
una lista e ir agregando varios artículos en la misma carga; no tendríamos que agregar varias respuestas aunque respete los
valores anteriores».

Es la parte de renglones de la Etapa 2, «Motor para documentos», de la [hoja de ruta de ARCA](2026-10-31-arca-formularios-estandar.md),
adelantada: según su decisión 4, la factura lleva renglones en la app y en WhatsApp. Los cálculos, las condiciones, el
resultado externo y la biblioteca de estándares siguen en esa hoja. El [resumen de formularios](formularios/README.md) lo
tenía pendiente como «grupos y remitos con líneas».

**Tableros:** lienzo provisorio [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), filas
«Formularios · renglones · 0» a «· 12» (RG0 a RG12), con las notas naranjas de origen y una verde por elección (RG0 a RG6
en la versión 55; RG7 y RG8, al final del lienzo, en las versiones 79 y 82; RG12 en la versión 92).
El ejemplo es el remito de la prueba:
- el proveedor «Agroinsumos del Litoral S.A.» y el campo «SAN JULIAN»;
- en RG1 a RG5, tres artículos (GLIFOSATO 64%, 200 litros; ATRAZINA 50%, 100 litros; UREA GRANULADA, 5.000 kilos) y un
  precio unitario de ejemplo, que el formulario real no tiene;
- en RG0 y RG6, las cuatro respuestas reales, INS-000001 a INS-000004, con la observación «3 bolsas rotas en la descarga».
  RG0 compara esas cuatro respuestas con una sola de cuatro renglones.

El lienzo del sistema visual no se abre desde la cuenta de Claude Code. El diseño elegido quedó registrado en el front, en
[renglones en los formularios](../../../ArquitecturaBaseFront/docs/design/renglones-en-formularios.md), como el de la
[carga desde la app](../../../ArquitecturaBaseFront/docs/design/carga-desde-la-app.md); los tableros pasan a ese lienzo
antes de programar.

## Decisiones del usuario (2026-11-03 al 2026-11-07)

Las eligió de a una, mirando cada fila; en las ocho eligió la recomendada, la A. La sexta ajusta la primera: salió de
analizar con él la carga del remito como cuatro respuestas. La séptima y la octava salieron el 2026-11-04, al revisar el
plan antes de programar y al programar el Flow.

Desde el 2026-11-07, todo texto de la app dice «ítem» donde estas decisiones citan «renglón» («Agregar ítem», «Ítem 2»,
«¿Agregás otro ítem?», «Algún ítem con…»): es la decisión 11, y la lista completa de textos está en el
[diseño del front](../../../ArquitecturaBaseFront/docs/design/renglones-en-formularios.md#los-textos-ítem-en-lugar-de-renglón-rg12-a--2026-11-07).
Los documentos y el código siguen llamándolo renglón (`rows`).

1. **El editor: RG1-A, un tipo de pregunta «Renglones».**
   - «Agregar pregunta» suma «Renglones» al final de los tipos. En la lista, el bloque es un recuadro con sus preguntas
     adentro, numeradas 5.1, 5.2…, y su propio «Agregar pregunta» en la banda «Preguntas de cada renglón».
   - Se edita como una pregunta: el nombre («Artículos»), la ayuda y el mínimo y el máximo de renglones. La (i) del máximo
     dice el tope de WhatsApp.
   - Con el bloque abierto, la vista previa muestra la pantalla de un renglón en WhatsApp.
   - Una pregunta que ya existe se pasa al bloque con la casilla de la decisión 6.
   - Descartó RG1-B, una sección que se repite, que necesitaba antes los títulos de sección (CW15-B).
2. **La carga en la app: RG2-A, una tabla.**
   - En la página, el bloque ocupa el renglón entero de «Datos», donde lo puso el autor. Cada renglón es una fila y cada
     pregunta una columna con su etiqueta arriba; el número del renglón va a la izquierda. Si no entra, la tabla se desplaza
     de costado dentro de su caja.
   - «Agregar renglón», al pie, suma uno vacío y lleva el foco a su primera pregunta. La tabla arranca con el mínimo de
     renglones; con el máximo, el botón se apaga y dice por qué («Hasta 20 renglones.»).
   - En cada fila, ↑ ↓ y el tacho en rojo, que quita sin preguntar, como un archivo. Con el mínimo, el tacho se apaga y dice
     por qué.
   - Al enviar, la celda con un error se marca en rojo y el mensaje va debajo de su renglón, con el nombre de la pregunta
     («Cantidad: completá esta respuesta.»); el foco va a la primera. Si faltan renglones, lo dice debajo de la tabla
     («Agregá al menos 1 renglón.»).
   - En el panel junto a la planilla y en el celular, cada renglón va en su recuadro («Renglón 2»), con ↑ ↓ y «Quitar» en la
     banda y las preguntas una debajo de otra.
   - «Mantener las respuestas para la siguiente» conserva también los renglones; los archivos, no.
   - Descartó RG2-B, una tabla de lectura con un diálogo por renglón.
3. **WhatsApp: RG3-A, una pantalla por renglón, hasta 10.**
   - Al publicar, el Flow suma una pantalla por renglón, hasta 10 o el máximo del bloque si es menor. Cada pantalla tiene las
     preguntas del bloque y su número en el título («Artículos · renglón 2»).
   - Desde el mínimo, cada renglón termina con «¿Agregás otro renglón?», sí o no, obligatoria. Con «Sí» sigue el renglón
     siguiente; con «No», las preguntas de después del bloque o, si no hay, «Enviar». El último renglón no pregunta.
   - La flecha de WhatsApp vuelve al renglón anterior, para corregirlo o para decir «No».
   - Con un máximo mayor que 10, publicar avisa sin frenar: «En WhatsApp se cargan hasta 10 renglones; el máximo es 20.». En
     la app se sigue cargando hasta el máximo.
   - Sin intercambio con Meta: es el Flow estático de hoy con más pantallas. Antes de enviar no se ve la lista con los nombres
     de los artículos, porque el Flow no muestra el texto de una opción elegida en otra pantalla.
   - Resuelve lo que la hoja de ARCA dejaba para esta etapa: pantallas fijas repetidas y no el intercambio cifrado (RG3-B).
     El intercambio sigue pendiente para la búsqueda en vivo, las listas dependientes y el total de la factura.
4. **La planilla, el Excel, el detalle y el PDF: RG4-A, una fila por respuesta.**
   - La planilla sigue con una fila por respuesta. La columna del bloque dice la cantidad de renglones y los valores de su
     primera pregunta, en orden y cortados con «…»: «3 · GLIFOSATO 64%, ATRAZINA 50%, UREA GRANULADA…». No se ordena por
     ella, como «elegir varias». Una respuesta de antes del bloque dice «—».
   - En «Filtros» (PL1-A), las preguntas del bloque van debajo de su nombre y filtran por algún renglón. Desde PL2-A, una con
     opciones es un combo de una opción, sin cantidades, y el total de la planilla sigue contando respuestas.
   - La vista rápida (D2) muestra cada renglón con su número y su primer valor en negro, y los demás datos alineados debajo,
     como los de un catálogo (TL1-A). La pantalla completa (DC1) y el PDF muestran una tabla; en el PDF, la tabla sigue en la
     página siguiente si no entra.
   - El Excel deja «Respuestas» como hoy, con la cantidad de renglones en la columna del bloque, y suma una hoja con el nombre
     del bloque y una fila por renglón: N°, Renglón y sus preguntas.
   - Descartó RG4-B, una fila por renglón en la planilla y en el Excel.
5. **Las reglas: RG5-A, «algún renglón con…».**
   - Una condición sobre una pregunta del bloque se cumple si al menos un renglón la cumple. Varias del mismo bloque buscan un
     renglón que las cumpla todas.
   - La condición empieza con «Algún renglón con». La lista de reglas, en pasos desde LR1-A, dice «Si algún renglón de
     Artículos con ¿Llegó en buen estado? es No».
   - «Filtros» usa la misma regla.
   - En el mensaje (MD1-A), «Artículos» es un dato que trae todos los renglones, uno por línea, numerados y con sus valores
     separados por « · » en el orden de las preguntas. Las preguntas del bloque no se ofrecen sueltas.
   - Descartó RG5-B, que elegía «algún» o «todos» en cada condición y contaba renglones.
6. **Elegir las preguntas: RG6-A, la casilla «Se carga varias veces».** El usuario preguntó cómo se determina al crear el
   formulario que es de carga múltiple, y pidió elegir qué preguntas se cargan varias veces.
   - En el editor de cada pregunta, debajo de «Obligatoria», «Se carga varias veces», con su (i): «Se carga una vez por
     renglón, en la tabla del bloque.». Fotos y documentos no la tienen.
   - Las marcadas se juntan en el bloque; si no hay, lo crea en el lugar de la primera, llamado «Renglones» hasta que el
     autor le ponga otro nombre. El lápiz del bloque edita el nombre, el mínimo y el máximo.
   - Desmarcar una la saca del bloque y queda justo después de él. El «Agregar pregunta» de adentro del bloque sigue.
   - Una pregunta que ya existía pasa como pregunta nueva: lo cargado antes conserva su columna. Publicar lo dice en una fila
     de «Qué se publica», «Pasan a renglones», en lugar de mostrarlas como quitadas y agregadas.
   - Descartó RG6-B (como quedó en RG1-A, rehacer las preguntas adentro) y RG6-C (una zona «Carga múltiple» en el formulario,
     con la lista para marcar).
7. **El tope de la respuesta de WhatsApp: RG7-A, 64 KiB como con archivos** (2026-11-04). Al publicar, el compilador suma lo
   más largo que podría mandar cada pregunta, a 6 bytes por carácter, y frena si pasa 16 KiB, o 64 KiB si el formulario
   lleva fotos o documentos. El plan no lo tenía en cuenta: con renglones, ese peor caso se cuenta una vez por renglón de
   WhatsApp. «Recepción de insumos», con Observaciones de 600 caracteres, llega a unos 43 KB con 10 renglones y entra sólo
   porque lleva el remito adjunto; sin el adjunto entrarían 3.
   - Un formulario con bloque tiene el tope de uno con archivos. WhatsApp sigue cargando hasta 10 renglones y publicar no
     muestra nada nuevo.
   - Si igual no entra, por ejemplo con Observaciones de más de unos 950 caracteres, frena como hoy, con el problema de
     siempre en cada pregunta de texto.
   - La (i) del máximo sigue diciendo el tope de WhatsApp.
   - Descartó RG7-B (WhatsApp carga los renglones que entran y publicar avisa) y RG7-C (frenar como hoy, con 16 KiB).
8. **El bloque con mínimo 0 en WhatsApp: RG8-A, «¿Agregás un renglón?» antes del primero** (2026-11-04). Salió al programar
   el Flow: con un mínimo de 0, el «desde el mínimo» de RG3-A cae antes del primer renglón.
   - El Flow suma una pantalla con el nombre del bloque y «¿Agregás un renglón?», sí o no, obligatoria. Con «Sí» sigue el
     renglón 1; con «No», lo de después del bloque o «Enviar».
   - Desde el primero, cada renglón termina con «¿Agregás otro renglón?»; con un mínimo de 1 o más, la pantalla no aparece.
   - En la app no cambia nada: la tabla arranca vacía y «Agregar renglón» suma el primero.
   - Descartó RG8-B (la pregunta al final de la pantalla anterior) y RG8-C (en WhatsApp, al menos un renglón).
9. **«Renglones» en el tipo de respuesta: RG9-A, apagada con su motivo después de «·»** (2026-11-04). Salió al programar el
   editor: el tipo se elige en el desplegable «Tipo de respuesta» de la pregunta, no en un menú de «Agregar pregunta» como
   dibujó RG1-A, y una opción de un desplegable nativo no muestra un tooltip.
   - «Renglones» es un tipo más, al final del desplegable. Con un bloque ya armado queda apagada como «Renglones · Ya hay un
     bloque de renglones», igual que hoy «Foto · …» y «Documento · …» cuando falta el almacenamiento.
   - En una pregunta del bloque, «Foto» y «Documento» se apagan con «· No se carga varias veces», y «Permitir elegir varias»
     se apaga con su motivo al pasar el mouse.
   - Descartó RG9-B (un botón «Agregar renglones») y RG9-C («Renglones» sólo mientras no hay un bloque, sin motivo).
   - Desde el 2026-11-06, RG10-D saca «Renglones» del desplegable; lo de foto, documento y «Permitir elegir varias» sigue.
10. **Cómo se agrega el bloque: RG10-D, «Agregar bloque», como una pregunta más** (2026-11-06). Salió en la verificación
   con Meta: armando el formulario de prueba no quedó claro cómo se arma el bloque, escondido en «Tipo de respuesta». El
   usuario no eligió ninguna de las tres opciones dibujadas, propuso esta y la aprobó sobre su maqueta (lienzo, versión 86).
   - «Agregar bloque» va en la franja «Preguntas», al lado de «Agregar pregunta»; «Agregar título» no cambia.
   - El bloque se suma al final de la lista como una pregunta más, abierto: el nombre, que arranca en «Bloque» (también
     cuando lo crea «Se carga varias veces»), el mínimo y el máximo de renglones, la ayuda y su propio «Agregar pregunta».
     En la lista dice «Bloque · de 1 a 10 renglones · 3 preguntas».
   - Con un bloque ya armado, el botón se apaga con el tooltip «Ya hay un bloque de renglones.».
   - «Tipo de respuesta» deja de ofrecer «Renglones»: el bloque se agrega sólo con su botón o con «Se carga varias veces».
   - Descartó RG10-A («Agregar renglones», con el bloque como algo distinto de una pregunta), RG10-B (un solo «Agregar»
     con menú) y RG10-C (como hoy). El nombre del botón (RG11) quedó resuelto con «Agregar bloque».
11. **Los textos: RG12-A, «ítem» en lugar de «renglón», en toda la app** (2026-11-07). Salió en la verificación con Meta:
   completando el formulario de prueba en el celular, el usuario preguntó si «¿Agregás otro renglón?» no tendría que decir
   «agregar otro {nombre del ítem}» o el nombre del bloque y, mirando la lista del editor («Renglones · de 1 a 1 · 2
   preguntas», «Preguntas de cada renglón»), propuso «Items en vez de la palabra renglones». Eligió la recomendada sobre el
   tablero RG12 (lienzo, versión 92).
   - Todo texto que ve el usuario dice «ítem» donde decía «renglón», en el editor, al publicar, en WhatsApp y su vista
     previa, en la carga, en las lecturas y en las reglas; en inglés, «item». «Ítem» va siempre con «otro» y «un», así que
     se lee bien con cualquier nombre de bloque.
   - En la lista del editor, «Bloque · de 1 a 10 ítems · 3 preguntas»; con mínimo y máximo iguales, la cantidad («Bloque ·
     1 ítem · 2 preguntas»), y con mínimo 0, «hasta 10 ítems». El recuadro dice «Preguntas de cada ítem» y el editor del
     bloque titula «Ítems» el mínimo y el máximo.
   - WhatsApp: «Artículos · ítem 2» y «¿Agregás otro ítem?», 19 de los 30 caracteres que admite Meta en la pregunta de un
     sí o no (`RadioButtonsGroup`, según la referencia de componentes consultada el 2026-11-07); con mínimo 0, «¿Agregás
     un ítem?».
   - Al publicar, «Pasan al bloque» y «En WhatsApp se cargan hasta 10 ítems; el máximo es 20.». En la carga, «Agregar
     ítem», «Ítem 2», «Agregá al menos 1 ítem.» y «Hasta 20 ítems.»; en las lecturas, «En algún ítem de Artículos» y la
     columna «Ítem» del Excel; en las reglas, «Algún ítem con…».
   - Lo que dice «renglón» en Importar opciones («Cada fila copiada ocupa un renglón…») es la línea de lo pegado y queda.
   - Descartó RG12-B (en WhatsApp, el nombre del bloque: «¿Agregás más artículos?», con el nombre en plural y de hasta 16
     caracteres) y RG12-C (el editor del bloque suma «Cada ítem es [un/una] [artículo]»: «¿Agregás otro artículo?»).
12. **Un mínimo mayor que el tope de WhatsApp: RG13-A, publicar frena** (2026-11-07). Salió al planear la Etapa 2, de la
   revisión del backend de la Etapa 1: WhatsApp carga 10 ítems como mucho, y un mínimo de 15 no se podía completar. Lo
   eligió con la pregunta del chat, sin tablero.
   - Con WhatsApp instalado, publicar frena con un problema en el bloque, con «Ir a la pregunta»: «En WhatsApp se cargan
     hasta 10 ítems; el mínimo es 15.».
   - Al guardar una respuesta, la app y WhatsApp validan siempre contra el mínimo y el máximo del bloque: ninguna respuesta
     queda con menos ítems que el mínimo.
   - Descartó RG13-B (publicar avisa y WhatsApp pide los 10 sin preguntar, con respuestas de menos ítems que el mínimo) y
     RG13-C (el editor no deja un mínimo mayor que 10 en ningún formulario).

**Resueltas por defecto** con la opción más simple, dibujadas en los tableros y sin objeción del usuario:

- Un bloque por formulario, sin bloques adentro. Con uno, «Agregar bloque» se apaga y dice por qué («Ya hay un bloque de
  renglones.»). Más de uno, si aparece el caso.
- Adentro de un bloque entran texto corto, texto largo, número, fecha, sí o no y elegir una, propia o de un catálogo. El
  texto largo, como las observaciones de cada artículo, es una celda que crece con lo escrito. Quedan afuera elegir varias,
  las fotos y los documentos.
- El mínimo va de 0 al máximo, y el máximo llega hasta 100. Un bloque nuevo arranca con 1 y 20; con mínimo 0 es opcional.
- Las preguntas del bloque cuentan para el máximo de 40 del formulario.
- El borrador se guarda con un bloque sin preguntas, pero no se publica: en «Problemas», «Agregá al menos una pregunta al
  bloque.».
- Cambiar el tipo de una pregunta del bloque crea otra, como hoy; renombrar, ordenar y traducir conservan su identidad.
- Los textos dicen «ítem» («Agregar ítem», «Ítem 2», «¿Agregás otro ítem?», RG12-A): el bloque no guarda cómo se llama uno
  en singular.
- Un renglón vacío cuenta como renglón: pide sus obligatorias y se quita con el tacho.
- El asistente no prellena las preguntas del bloque, como no prellena «elegir varias».

## Cómo se arma

### El modelo

- **El tipo.** `FormQuestionType.Group`, hoy reservado y rechazado al guardar, pasa a ser el bloque: una pregunta `group`
  con nombre, ayuda, mínimo y máximo de renglones y sus preguntas adentro. Cada pregunta del bloque tiene su FieldId y su
  lugar en el registro de identidades retiradas.
- **Lo publicado no cambia.** Los esquemas `format=1` sin bloque siguen iguales. Los campos nuevos de los modelos sellados
  llevan `JsonIgnore(WhenWritingNull)`, así no cambian las huellas anteriores. La regla del tipo reservado, en
  [persistencia y responsabilidad](../features/formularios.md#persistencia-y-responsabilidad), se reescribe al programar.
- **Pasar a renglones.** Marcar «Se carga varias veces» en una pregunta guardada la pasa al bloque con una identidad nueva,
  como cambiar el tipo, y desmarcarla también. La versión publicada conserva la pregunta anterior y sus respuestas.
- **Los valores.** El valor de un bloque es una lista de renglones: `ValuesJson` lleva un array con un objeto por renglón.
  - `BusinessFormRecordValue` suma el renglón (de 0 a 99) al lado de `Position`.
  - `BusinessFormRecordReferences` lo suma a su clave.
  - La migración es aditiva y su Down se frena si hay bloques publicados o capturados, como la de «elegir varias».
- **La captura.** Valida la cantidad de renglones contra el mínimo y el máximo y, por WhatsApp, contra el tope. Cada renglón
  se valida como preguntas sueltas: obligatorias, tipos, largos y opciones ofrecidas. La huella incluye los renglones y el
  replay los conserva; un rechazo no consume número, como hoy.

### WhatsApp

- **Un perfil nuevo del compilador**, la revisión siguiente a `flow-static-7.3-r9`, sólo para publicaciones nuevas. La
  primera pantalla tiene lo anterior al bloque; después va una pantalla por renglón y, al final, lo de después del bloque. Las
  versiones publicadas conservan sus bytes.
- **Flow JSON 7.3 alcanza sin intercambio**, según la [referencia de componentes](https://developers.facebook.com/docs/whatsapp/flows/reference/components)
  y la de [Flow JSON](https://developers.facebook.com/docs/whatsapp/flows/reference/flowjson), consultadas el 2026-11-02:
  - el `If` (desde 4.0) admite un `Footer` en cada rama: «Siguiente» con «Sí» y la pantalla que sigue con «No»;
  - una pantalla lee lo cargado en otra con `${screen.<pantalla>.form.<campo>}` (desde 4.0), así que el envío final junta
    todos los renglones;
  - no hay un tope de pantallas documentado: el JSON llega hasta 10 MB, cada pantalla hasta 50 componentes y un Flow puede
    tener varias pantallas terminales.
- **A confirmar con Meta al programar**, con un Flow de prueba publicado:
  - el límite de «10 ramas» del modelo de rutas, que la documentación no define y que, sin intercambio, se arma solo. Si no
    alcanza para 10 renglones, el tope baja;
  - cómo llega en el envío final lo de las pantallas que no se visitaron, y que la cantidad de renglones viaje con él;
  - que las opciones de un catálogo, que viajan en los datos del mensaje, sirvan en cada pantalla de renglón;
  - que una respuesta de unos 40 KB llegue entera (RG7-A). Si no llega, un formulario con bloque se queda con 16 KiB y frena
    como hoy, como ya preveía la opción elegida.
- **El tope de la respuesta (RG7-A):** el peor caso cuenta las preguntas del bloque una vez por renglón de WhatsApp. Con un
  bloque, el tope es `MaxMediaResponseJsonBytes` (64 KiB), el de los formularios con archivos, en el compilador y en el
  webhook, que hoy elige ese tope cuando la respuesta trae archivos y lo va a elegir también cuando trae un bloque. Si no
  entra, `WhatsApp.Flow.ResponseTooLarge`, como hoy.
- **Archivos:** el presupuesto no cambia, porque un bloque no lleva fotos ni documentos.

### Lecturas y reglas

- **Planilla, facetas y conteos.** Un filtro sobre una pregunta del bloque es un `EXISTS` sobre sus valores, sin multiplicar
  la respuesta por sus renglones, como «elegir varias». La columna del bloque no se ordena.
- **Excel.** Una hoja más, con una fila por renglón y el número de respuesta y de renglón; los tipos y la precisión, como hoy.
- **PDF.** La tabla del bloque, con su encabezado repetido si pasa de página. El diseño del PDF elige el bloque entero.
- **Reglas.** Las opciones de condiciones suman las preguntas del bloque debajo de él. La evaluación agrupa las condiciones
  de un mismo bloque y busca un renglón que las cumpla todas. El dato del mensaje guarda la identidad del bloque, como MD1-A
  guarda la de una pregunta.
- **El chat de WhatsApp**, que muestra los valores de una respuesta, muestra el bloque con un renglón por línea.

## Lo que tiene que estar antes

Los cinco quedaron programados el 2026-11-03, comprobado el 2026-11-04 en los dos repositorios y en el diseño del front;
falta su prueba manual. Cada etapa de este plan se apoya en los que toca:

- **AR1-A** (dos anchos) y **AR2-A** (mantener lo cargado en la página): el bloque ocupa el renglón entero y «Mantener»
  conserva los renglones.
- **PL1-A** («Filtros»): las preguntas del bloque se suman a ese desplegable.
- **TL1-A y TL3-A** (el detalle): los renglones se leen con esa forma.
- **MD1-A** («Agregar dato»): el bloque es un dato más del mensaje.

## Plan por etapas

Cada paso queda igual a su tablero: al programarlo se abre el tablero y se compara antes de cerrarlo. Antes de la Etapa 1,
los tableros elegidos pasan al lienzo del sistema visual; su diseño ya está registrado en el front. Cada etapa sigue
[validar un cambio](../guides/validar-un-cambio.md): TDD en las políticas, integración con PostgreSQL y Redis, contratos
exportados, y en el front tests, visuales y el recorrido es/en a 1440, 390 y 320. Las piezas nuevas (la tabla de renglones y
su versión en recuadros; la casilla es el `CheckboxField` de siempre) van a `shared/ui` con su ficha en el estándar visual,
para que se usen igual en toda la app.

1. **Etapa 1 · El bloque en el editor y al publicar (RG1-A, RG6-A y RG3-A).** Su
   [plan de implementación](2026-11-04-renglones-etapa-1-implementacion.md) tiene diez tareas.
   - Backend: el tipo `group` en el borrador, con validación, identidades, mínimo y máximo, tipos permitidos, un solo bloque
     y las 40 preguntas, y pasar una pregunta guardada al bloque o sacarla. El chequeo de publicación (el bloque vacío frena,
     el máximo mayor que el tope avisa y «Pasan a renglones») y la aceptación.
   - El compilador con el perfil nuevo, una pantalla por renglón con «¿Agregás otro renglón?» y el tope de 64 KiB con bloque
     (RG7-A), también en el webhook, y la verificación con Meta de los puntos de arriba, con un Flow de prueba publicado y su
     vista previa.
   - Front: «Renglones» en el editor, la casilla «Se carga varias veces», el recuadro con sus preguntas, el lápiz con el
     nombre, el mínimo y el máximo, y la vista previa de un renglón.
2. **Etapa 2 · Cargar renglones (RG2-A y RG3-A).** Su [plan de implementación](2026-11-07-renglones-etapa-2-implementacion.md)
   tiene diez tareas y suma RG13-A y LD3-A.
   - Backend: los valores con su renglón y su migración, y la captura de WhatsApp y de la app con los renglones, la huella y
     el replay.
   - Front: la tabla en la página, los recuadros en el panel y en el celular, agregar, quitar y mover, los errores por renglón
     y «Mantener».
   - Prueba física: «Recepción de insumos» con los tres renglones del remito, desde la app y por WhatsApp.
3. **Etapa 3 · Ver los renglones (RG4-A).** La columna de la planilla, «Filtros» por algún renglón con sus conteos, la vista
   rápida, la pantalla completa, el chat de WhatsApp, la hoja del Excel y la tabla del PDF.
4. **Etapa 4 · Las reglas (RG5-A).** Las condiciones «algún renglón con…» en el editor y en la evaluación, la lista de reglas
   y el bloque como dato del mensaje, con «Así llega». Con eso, la regla de «Recepción de insumos» avisa al Agrónomo si algún
   artículo llegó en mal estado.

Al cerrar, las reglas van a [formularios](../features/formularios.md) y a [WhatsApp](../features/whatsapp.md), el diseño
queda en el front y se ofrece el traspaso al multitenant. La factura de ARCA usa este bloque en su Etapa 3; los cálculos
(cantidad por precio y suma de renglones) siguen en la Etapa 2 de esa hoja.

Fuera de alcance: más de un bloque, bloques adentro de un bloque, fotos o documentos por renglón, ordenar la planilla por el
bloque, condiciones sobre todos los renglones o su cantidad, duplicar un renglón, importar renglones desde Excel y la lista
con los nombres antes de enviar por WhatsApp.
