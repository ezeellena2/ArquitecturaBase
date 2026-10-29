# Cargar formularios desde la app

Propuesta del 2026-10-29, sin programar, con las decisiones del usuario tomadas el mismo día. La persona elige un
formulario publicado en «Formularios», lo carga en la app (desde la compu o el celular) y lo cargado se trabaja en la
planilla. Las reglas de hoy están en [formularios](../features/formularios.md); este plan suma la carga desde la web, que el
[resumen de formularios](formularios/README.md) dejaba pendiente con tableros propios.

**Tableros:** lienzo [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), tableros CW0 a CW9 con los catálogos y
formularios de OCHA (Lluvia, Siembra y Aplicación) y valores de ejemplo. Los elegidos llevan la banda verde y el sello «Elegida».
Es **provisorio**: el lienzo [Sistema visual](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ) no se pudo abrir desde la cuenta de esta
sesión. Los tableros elegidos se pasan allí antes de programar; el formato es el mismo. Están armados con los tokens y las
medidas de `index.css` y `shared/ui`, con el escritorio al 75 % y el celular a 16 px con áreas táctiles de 44.

## Decisiones (2026-10-29)

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
  - la lectura de lo propio y el filtro por respuesta.
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
  - la planilla recortada a lo propio y el filtro nuevo del editor.

## Plan por etapas

Cada paso queda igual a su tablero elegido: al programarlo se abre el tablero y se compara antes de cerrarlo. Antes de la
Etapa 1, los tableros elegidos pasan al lienzo del sistema visual y su diseño se registra en el front.

1. **Etapa 1 · Cargar desde la app.**
   - Backend: el servicio, las rutas, la lectura de opciones y la captura con origen «App».
   - Front, cada parte igual a su tablero:
     - «Formularios» con «Cargar» por fila, abierto a quien completa (CW1-B);
     - la página de carga (CW2-A) y el panel desde la planilla (CW2-B);
     - las listas (CW3), las fotos (CW4) y lo que pasa después de enviar (CW5);
     - la planilla con «Canal: App» (CW6-A).
   - Cierre con la rutina de [validar un cambio](../guides/validar-un-cambio.md): integración con PostgreSQL, Redis y Azurite;
     contratos exportados; front con build, lint, tests y visuales; recorrido es/en a 1440, 390 y 320; y la cámara de un
     celular real por el túnel.
2. **Etapa 2 · Lo propio en «Formularios» (CW6-C).**
   - La lectura de lo propio con «Completar formularios», y la planilla y el detalle recortados, con estado, PDF y motivo.
   - En «Formularios», sus cargas y la última para quien sólo carga.
   - El panel de carga, también en esa planilla.
3. **Etapa 3 · Listas dependientes (CW7):** el filtro, el editor, la comprobación de publicación, el bloqueo del envío por
   WhatsApp y la validación al guardar. La regla nueva va a [formularios](../features/formularios.md).
4. **Después:** las listas dependientes en WhatsApp con el intercambio con Meta. La carga en nombre de otra persona, si aparece
   el caso, con su tablero.

Fuera de alcance: corregir una respuesta, importar respuestas desde Excel, borradores guardados y carga sin conexión, la carga
en filas (CW2-C) y grupos con líneas. Aplicación usa pares fijos de producto y dosis; una lista de líneas necesita «grupos y
remitos con líneas», que tiene su propio pendiente.
