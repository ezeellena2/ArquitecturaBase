# Cargar formularios desde la app

Propuesta del 2026-10-29, con las decisiones del usuario del 2026-10-29 y el 2026-10-30. La Etapa 1 está implementada desde el
2026-10-30 (su resumen está en el plan por etapas); las etapas 2 a 5 siguen sin programar. La persona elige
un formulario publicado en «Formularios», lo carga en la app (desde la compu o el celular) y lo cargado se trabaja en la
planilla, donde además se corrige o se anula. Las reglas de hoy están en [formularios](../features/formularios.md); este plan suma la carga desde la web, que el
[resumen de formularios](formularios/README.md) dejaba pendiente con tableros propios.

**Tableros:** lienzo [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), tableros CW0 a CW9 con los catálogos y
formularios de OCHA (Lluvia, Siembra y Aplicación) y valores de ejemplo. Los elegidos llevan la banda verde y el sello «Elegida».
Es **provisorio**: el lienzo [Sistema visual](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ) no se pudo abrir desde la cuenta de esta
sesión. Los tableros elegidos se pasan allí antes de programar; el formato es el mismo. El diseño de la Etapa 1 quedó registrado en el
front, en [carga desde la app](../../../ArquitecturaBaseFront/docs/design/carga-desde-la-app.md); pasarlo al lienzo del sistema visual sigue
pendiente, porque esa cuenta no lo abre. Están armados con los tokens y las
medidas de `index.css` y `shared/ui`, con el escritorio al 75 % y el celular a 16 px con áreas táctiles de 44.

## Decisiones (2026-10-29 y 2026-10-30)

1. **Elegir el formulario: CW1-B, desde «Formularios».** No hay entrada nueva.
   - La tabla MF1 suma «Cargar» en cada fila para quien puede completar, y la planilla suma «Agregar respuesta».
   - «Formularios» se abre también con «Completar formularios». Quien sólo carga ve sus formularios y «Cargar».
2. **Cargar una respuesta: CW2-A y CW2-B.** Es el mismo par que D2 y DC1 para el detalle.
   - La página propia («Nueva respuesta», con el formulario en las migas) sirve en el escritorio y en el celular.
   - Desde la planilla, en el escritorio, la carga se abre en un panel al costado.
3. **Lo cargado: CW6-A y CW6-C.**
   - Quien ve respuestas usa la planilla de siempre, con «Canal: App».
   - Quien sólo carga entra a cada formulario y ve esa misma planilla sólo con lo suyo: estado, PDF y motivo de rechazo.
   - No hay «Mis cargas» aparte (CW6-B).
4. **Listas dependientes: sí, sólo en la app, en una etapa propia (CW7).**
5. **Cargar en nombre de otra persona: no por ahora.** Cada uno carga lo suyo; si aparece el caso, se dibuja su tablero.
6. **Editar: corregir (CW10-A).**
   - «Corregir», en el detalle, abre lo cargado en la versión activa, con lo de antes a la vista y el motivo obligatorio.
   - La corrección reemplaza a la anterior con el mismo número, y la anterior queda en el historial tal como se avisó, aprobó o imprimió.
   - Vuelve a pasar por las reglas. Si el formulario pide aprobación, vuelve a «Por aprobar», aunque ya estuviera aprobada.
7. **Eliminar: anular con motivo (CW11-A).**
   - Queda «Anulada» con su número, quién, cuándo y por qué, y cierra la aprobación pendiente.
   - La planilla la oculta salvo con el filtro «Anulada», no cuenta en «Formularios» y no sale en el Excel salvo que se pida.
   - No se borra ni se deshace.
8. **Quién corrige y anula:**
   - quien la cargó, mientras no esté aprobada (si el formulario no pide aprobación, siempre);
   - con un permiso nuevo, «Corregir y anular respuestas», cualquiera y siempre;
   - quien sólo ve respuestas, no.
9. **Unidades: en la etiqueta (CW13-A).** Como hoy, «Densidad (kg/ha)», sin un dato «Unidad» nuevo.
10. **Hasta tres por renglón (CW16).** Reemplaza la «una columna» elegida primero.
    - En escritorio, cada zona es una grilla de tres columnas, en el orden del editor; en tableta, dos; en el celular y en el panel, una.
    - El ancho sale del tipo de pregunta; el autor no lo elige.
11. **Títulos de sección, con zonas plegables (CW15-B y CW16).**
    - El autor agrega títulos entre las preguntas. La app arma una zona por título y WhatsApp lo muestra como subtítulo.
    - Todas las zonas, también «Datos» y «Fotos y documentos», se pliegan y arrancan abiertas.
    - Al enviar, una zona plegada con errores se abre sola.

Las decisiones 6 a 8 cambian la regla de [inmutabilidad](../features/formularios.md#persistencia-y-responsabilidad). Una respuesta
confirmada sigue sin modificarse, pero puede quedar reemplazada por su corrección o anulada; al implementarlo, la regla se
reescribe allí.

## Análisis

### Quién carga y con qué permiso

- Alcanza con **«Completar formularios»** (`formrecords.create`), el mismo permiso que pide WhatsApp; su descripción ya dice
  «cuando esté disponible». No hay permiso nuevo. Ver respuestas y aprobar siguen aparte (CW9).
- La respuesta queda a nombre de quien la carga, y los filtros «según quien lo recibe» usan sus datos. Sin el dato, la
  pregunta frena igual que el envío por WhatsApp (`RecipientDataMissing`), con el nombre del dato (CW3).
  Quien carga desde la compu sin ese dato queda cubierto por las listas dependientes.
- **Ver lo propio sin «Ver respuestas»** (decisión 3) es una lectura nueva, acotada a las respuestas propias: planilla, detalle,
  archivos y PDF. El servidor filtra por quien cargó, también en una dirección directa. Tiene un antecedente: el asistente ya
  lee la propia captura.

### Qué versión se carga y qué se guarda

- Sólo la **versión publicada activa** de un formulario activo, con el filtro de disponibilidad del selector y del asistente.
  A diferencia de una sesión de WhatsApp, que conserva su versión, la web exige la activa al enviar. Si se publicó otra o se
  dio de baja, el envío devuelve 409 y la página lo dice; al pasar a la versión nueva se conservan las respuestas de las
  preguntas que siguen (CW5).
- **Las mismas validaciones y copias que WhatsApp**, porque la captura pasa por `BusinessFormRecordRecorder`:
  - un recibo idempotente por pedido de la web, sin replay ni número nuevo al repetir;
  - el número común bajo el lock de definiciones;
  - la opción activa al guardar, y la copia de texto, detalle, valor, presentación y campos;
  - archivos, reglas, aprobación (nunca de lo propio) y PDF.
- **La lista ofrecida:** en la app no hay una sesión con 200 opciones selladas. El servidor arma la oferta sólo con lo elegido,
  después de comprobar que esté activo y pase los filtros de la pregunta para esa persona, y la sella como hoy. La lista
  completa no se guarda, porque puede tener miles. El replay compara lo que mandó la persona, no la oferta que arma el servidor.
- **Origen «App»:** BusinessForms registra quién cargó y cuándo, y lo aporta por `IStructuredRecordOriginSource`, como lo hace
  WhatsApp con sus sesiones. La planilla, el detalle y el Excel dicen «App» (CW6-A).
- **Idioma del contenido:** el del menú si la versión lo publicó completo; si no, el original. Se guarda en
  `PresentationCulture`, así que los avisos y el PDF lo siguen.
- **Avisos de reglas:** «Mandarle un mensaje a quien la cargó» sale por WhatsApp si la persona tiene el número vinculado. Hoy
  falla con `RecipientUnavailable` si no lo tiene. Para una carga desde la app queda «No aplica», en vez de una incidencia.
- **Sugerencias de qué cargar después:** hoy las ofrece el chat de WhatsApp después de la captura, así que una carga desde la
  app no las dispara. Ofrecerlas en la app queda fuera de esta propuesta.

### Listas de catálogo

- Se agrega una lectura paginada con búsqueda para quien carga:
  - busca por texto en los dos idiomas, detalle y código, y trae páginas de 50;
  - filtra en SQL antes de paginar con los filtros fijos y por persona, con el mismo `FormCatalogMatchPolicy`;
  - cada opción trae título y descripción según la presentación, en el idioma del contenido.
  No tiene el tope de 200, y si los filtros dejan una sola opción, ya viene elegida (CW3).
- En «elegir varias», el máximo sigue siendo el publicado, hasta 20. Los catálogos de Filtros (personas y roles) usan la misma lectura.
- **Listas dependientes:** un tercer filtro, «según otra respuesta», junto a «fijo» y «según quien lo recibe» (CW7).
  - La pregunta de la que depende va antes, es de elegir una y elige del mismo catálogo que el campo de papel «filtro».
  - En la app, la lista espera esa respuesta y se limpia si cambia. Al guardar se comprueba que la opción corresponda.
  - Un Flow estático no puede filtrar por otra respuesta. Hasta el intercambio cifrado con Meta (en la
    [hoja de ruta de catálogos](2026-10-21-catalogos-con-origenes-externos.md)), un formulario con este filtro no se manda por
    WhatsApp: es un problema de la preparación, como `CatalogTooLarge`, y la comprobación de publicación lo avisa sin frenar.
  - Las preguntas ya publicadas no cambian.

### Cómo se arma un formulario

- **Reglas fijas, iguales para todos los formularios (CW12 y CW16).**
  - El autor decide el contenido: orden, textos, ayuda, obligatoria, opciones, largo máximo, rango de «elegir varias» y límites de fotos.
  - La app decide la forma con una tabla por tipo de pregunta.
- **Cuánto ocupa cada pregunta en la grilla de tres (dos anchos desde AR1-A, 2026-11-02):**
  - una columna: número, fecha, sí o no, texto corto de hasta 80, elegir una (hasta cinco opciones en fila, aunque sean
    largas; más, lista desplegable) y elegir de un catálogo, de una o de varias;
  - el renglón entero: texto largo, texto corto de más de 80 o sin largo máximo, elegir varias con opciones propias
    (sus casillas en columnas), fotos y documentos.
  Las preguntas no se reordenan: una que no entra baja de renglón. El panel junto a la planilla tiene las mismas zonas,
  en una columna.
- **Las zonas** son el `CollapsibleSection` de `shared/ui`. Plegada, la banda dice cuántas preguntas se respondieron.
- **Los títulos de sección** son un elemento nuevo del borrador, que no es una pregunta. El tipo `Group` sigue reservado para las líneas.
  - Entran en la huella de publicación como contenido opcional, así no cambian las versiones ya selladas.
  - En WhatsApp salen como subtítulo de la pantalla. La planilla y el Excel no cambian.

### Corregir y anular

- **Las reglas siguen siendo de cada formulario.** «Cuando llega una respuesta» decide si se pide aprobación, a qué rol, qué
  avisos salen y si hay PDF. Sin una regla de aprobación, la respuesta queda «Recibida» y listo. Un cambio de reglas vale
  para las respuestas que llegan después.
- **Una corrección es una carga más:**
  - pasa por la misma captura, con sus validaciones, copias y numeración;
  - las fotos que no se tocan se conservan;
  - corre las reglas de «al recibirla», con los valores nuevos.
  El número sigue siendo único entre las respuestas vigentes; la reemplazada queda enlazada a su corrección.
- **Anular es un estado nuevo, con motivo y autor.**
  - Cierra la tarea de aprobación abierta.
  - Planilla, facetas, navegación, Excel y conteos de «Formularios» excluyen las anuladas por defecto. Hoy «Respuestas» sale de
    `LastRecordNumber`, así que pasa a contarlas.
  - El chat de WhatsApp muestra la marca.
- **El permiso nuevo** sigue la [receta de un permiso](../guides/permiso-nuevo.md). Las acciones van en la vista rápida, en la
  pantalla completa y en la planilla de lo propio, para quien la cargó.

### Fotos y documentos

- **Los mismos límites, sellados en la versión:** mínimo y máximo por pregunta, hasta 10 por respuesta,
  `BusinessForms:MaxFileSizeMiB` por archivo (25) y 100 MiB en total.
- **Tipos:** las fotos son JPG o PNG; con `accept` en esos tipos, el iPhone convierte las suyas al elegirlas. Los documentos
  son PDF, Word, Excel o imagen (CW4).
- **El envío:** los archivos viajan en el mismo POST multipart, sin staging ni borradores. `BusinessFormFileRecorder` los
  verifica y sube antes del límite, como hoy, y repetir el envío no duplica ni consume número. El endpoint necesita un límite
  de cuerpo acorde.
- **En el celular:** «Tomar foto» abre la cámara; una pregunta «solo cámara» no ofrece la galería. En la compu se elige el archivo.

### Pantallas

| Tablero | Define |
| --- | --- |
| CW0 · El recorrido | Los seis pasos y qué es igual y qué cambia respecto de WhatsApp. |
| **CW1-B · Desde «Formularios»** (elegida) | «Cargar» en cada fila de MF1, «Agregar respuesta» en la planilla y la vista de quien sólo carga. CW1-A y CW1-C quedan como alternativas. |
| **CW2-A y CW2-B** (elegidas) | La página «Nueva respuesta» (`/formularios/{id}/cargar`), con datos y archivos en dos zonas y Enviar en la banda (fijo al pie en el celular); y el panel junto a la planilla, con «Mantener las respuestas para la siguiente». CW2-C queda como alternativa. |
| CW3 · Elegir de un catálogo | La lista con búsqueda, título y descripción, filtros por persona, una sola opción, sin el dato, estados de la lista, elegir varias y celular. |
| CW4 · Fotos y documentos | Arrastrar o elegir, límites y errores antes de subir, avance al enviar, corte de conexión, cámara y galería. |
| CW5 · Después de enviar | Número y estado en el encabezado, aprobación, rechazo por una opción dada de baja, sin conexión, versión nueva, baja y sin permiso. |
| **CW6-A y CW6-C** (elegidas) | La planilla con «Canal: App», quién cargó y el origen en D2; y la misma planilla con lo propio para quien sólo carga. CW6-B queda como alternativa. |
| **CW7 · Listas dependientes** (elegida) | El filtro nuevo en el editor, el comportamiento en la app y lo que avisa WhatsApp. |
| CW8 · En el celular | Una lluvia de punta a punta, desde «Formularios». |
| CW9 · Quién ve qué | Cuatro personas con sus permisos y la interfaz en inglés. |
| **CW10-A · Corregir** (elegida) | La corrección en el panel con lo de antes y el motivo, «Corregida» en la fila y el detalle, la versión anterior, una aprobada que vuelve a «Por aprobar», quien la cargó y el cuadro de quién puede. CW10-B queda como alternativa. |
| CW12 · Cómo se arma un formulario | Las reglas fijas: un control por tipo de pregunta, las dos zonas y un formulario largo entero. |
| **CW13-A · Unidades en la etiqueta** (elegida) | La unidad va en la etiqueta, igual en todos lados. |
| CW14 · Preguntas lado a lado | Una columna, pares automáticos o elegidos por el autor; la reemplazó CW16. |
| **CW15-B · Títulos de sección** (elegida) | Una zona por título, «Agregar título» en el editor y el subtítulo en WhatsApp. |
| **CW16 · Hasta tres por renglón y zonas plegables** (elegida) | La grilla de tres con el ancho por tipo, zonas plegadas, el error que abre su zona, la regla de anchos y el celular. Manda sobre el dibujo de las preguntas en CW2. |
| **CW11-A · Anular con motivo** (elegida) | «Anular» al final de «⋯», el motivo, la fila que sale de la vista, el filtro «Anulada» y el Excel con «Incluir las anuladas». CW11-B queda como alternativa. |

### Qué se reutiliza y qué es nuevo

- **Backend que se reutiliza:**
  - la captura (`BusinessFormRecordRecorder`, `BusinessFormFileRecorder`, `FormCatalogFieldCapturePolicy` y `FormActionRunRecorder`);
  - los filtros y la presentación (`FormCatalogOptionsPolicy`, `FormCatalogMatchPolicy` y `FormCatalogPresentationPolicy`);
  - el idioma (`FormPresentationCulturePolicy`) y la disponibilidad de versiones (`IFormSelectionReader`);
  - el origen por `IStructuredRecordOriginSource`;
  - la planilla, la navegación y el Excel, que no cambian de contrato.
- **Backend nuevo:**
  - `IFormEntryService` con `FormEntriesController` en `api/forms/entries`, con cuatro rutas: formularios que puedo completar,
    la versión para cargar, las opciones de una pregunta y enviar (multipart);
  - la lectura paginada de la oferta, el origen «App» y el aviso «No aplica»;
  - «Formularios» con «Completar formularios»;
  - la lectura de lo propio y el filtro por respuesta;
  - la corrección, la anulación y su permiso.
  - Cada ruta va al inventario y tiene su test.
- **Front que se reutiliza:**
  - las piezas de formulario: `Page`, `Surface`, `FormField`, `Input`, `Textarea`, `RadioGroupField`, `SegmentedControl` (Sí/No sin valor inicial) y `Autocomplete`;
  - las de estado y listado: `Banner`, `EmptyState`, `DataTable`, `FilterBar`, `ConfirmDialog`, `DisabledReason` y el Toaster;
  - las de fotos: `PhotoThumbnail` y `PhotoViewer`;
  - MF1 y la planilla con su panel D2.
- **Front nuevo:**
  - «Cargar» y «Agregar respuesta», la página y el panel de carga;
  - la lista de catálogo con búsqueda en el servidor, que extiende `Autocomplete` con descripción, páginas y estados;
  - la pregunta de archivos con cámara, y el estado «Enviada»;
  - la planilla recortada a lo propio y el filtro nuevo del editor;
  - «Corregir» y «Anular» en el detalle, y el filtro «Anulada».

## Plan por etapas

Cada paso queda igual a su tablero elegido: al programarlo se abre el tablero y se compara antes de cerrarlo. Antes de la
Etapa 1, los tableros elegidos pasan al lienzo del sistema visual y su diseño se registra en el front.

1. **Etapa 1 · Cargar desde la app.**
   - Backend: el servicio, las rutas, la lectura de opciones y la captura con origen «App».
   - Front, cada parte igual a su tablero:
     - «Formularios» con «Cargar» por fila, abierto a quien completa (CW1-B);
     - la página de carga (CW2-A) y el panel desde la planilla (CW2-B), con las preguntas armadas según CW12 y CW16:
       grilla de tres, zonas plegables y la unidad en la etiqueta;
     - las listas (CW3), las fotos (CW4) y lo que pasa después de enviar (CW5);
     - la planilla con «Canal: App» (CW6-A).
   - Cierre con la rutina de [validar un cambio](../guides/validar-un-cambio.md): integración con PostgreSQL, Redis y Azurite;
     contratos exportados; front con build, lint, tests y visuales; recorrido es/en a 1440, 390 y 320; y la cámara de un
     celular real por el túnel.
   - **Hecha el 2026-10-30.** Backend: `api/forms/entries` (versión activa, opciones paginadas con los filtros de quien carga y
     envío multipart por la captura común), `EnteredInApp` con su migración, el origen «App», el aviso a quien cargó que no
     aplica sin canal, «Formularios» con «Completar formularios» y el filtro por canal. Front: «Cargar» en «Formularios», la
     página y el panel con las preguntas armadas según CW12, CW13-A y CW16, las listas, las fotos, «Enviada» y la planilla con
     «Canal». Las reglas quedaron en [formularios](../features/formularios.md#carga-desde-la-app--etapa-1-2026-10-30).
     Verificación final: backend con build sin advertencias y 6.548/6.548 tests con PostgreSQL, Redis y Azurite, y las 24
     cohortes HTTP exportadas juntas; front con build y lint limpios, 3.560/3.560 tests y 775/775 visuales; recorrido en el
     navegador integrado en es/en a 1440, 390 y 320, sobre el catálogo visual y con la API real.
   - Pendiente: la cámara de un celular real por el túnel y pasar los tableros elegidos al lienzo del sistema visual.
2. **Etapa 2 · Lo propio en «Formularios» (CW6-C).**
   - La lectura de lo propio con «Completar formularios», y la planilla y el detalle recortados, con estado, PDF y motivo.
   - En «Formularios», sus cargas y la última para quien sólo carga.
   - El panel de carga, también en esa planilla.
3. **Etapa 3 · Corregir y anular (CW10-A y CW11-A).**
   - El permiso nuevo, la corrección como carga que reemplaza y la anulación con motivo.
   - Las lecturas y conteos sin las anuladas, y las acciones en la vista rápida, la pantalla completa y la planilla de lo propio.
   - La regla de inmutabilidad se reescribe en [formularios](../features/formularios.md). Vale también para lo que llegó por WhatsApp.
4. **Etapa 4 · Listas dependientes (CW7):** el filtro, el editor, la comprobación de publicación, el bloqueo del envío por
   WhatsApp y la validación al guardar. La regla nueva va a [formularios](../features/formularios.md).
5. **Etapa 5 · Títulos de sección (CW15-B):** el elemento nuevo en el editor y en la vista previa, la publicación, el subtítulo
   en WhatsApp y una zona por título en la app.
6. **Después:** las listas dependientes en WhatsApp con el intercambio con Meta. La carga en nombre de otra persona, si aparece
   el caso, con su tablero.

El orden de las etapas 3 a 5 es una propuesta: se puede cambiar. Fuera de alcance: importar respuestas desde Excel,
borradores guardados y carga sin conexión, la carga en filas (CW2-C) y grupos con líneas. Aplicación usa pares fijos de producto y dosis; una lista de líneas necesita «grupos y
remitos con líneas», que tiene su propio plan: [renglones en los formularios](2026-11-03-renglones-en-formularios.md).
