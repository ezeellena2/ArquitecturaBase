# Cargar formularios desde la app

Propuesta del 2026-10-29, sin programar. La persona elige un formulario publicado, lo carga en la app (desde la compu o
el celular) y lo cargado se trabaja en la planilla. Las reglas de hoy están en [formularios](../features/formularios.md); este plan suma la
carga desde la web, que el [resumen de formularios](formularios/README.md) dejaba pendiente con tableros propios.

**Tableros:** lienzo [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), tableros CW0 a CW9 con los catálogos y
formularios de OCHA (Lluvia, Siembra y Aplicación) y valores de ejemplo. Es **provisorio**: el lienzo
[Sistema visual](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ) no se pudo abrir desde la cuenta de esta sesión. Los tableros elegidos
se pasan allí antes de programar; el formato es el mismo. Están armados con los tokens y las medidas de `index.css` y
`shared/ui`, con el escritorio al 75 % y el celular a 16 px con áreas táctiles de 44.

## Análisis

### Quién carga y con qué permiso

- Alcanza con **«Completar formularios»** (`formrecords.create`), el mismo permiso que pide WhatsApp; su descripción ya dice
  «cuando esté disponible». No hay permiso nuevo para cargar lo propio. Ver respuestas y aprobar siguen aparte (CW9).
- La respuesta queda a nombre de quien la carga, y los filtros «según quien lo recibe» usan sus datos. Sin el dato, la
  pregunta frena igual que el envío por WhatsApp (`RecipientDataMissing`), con el nombre del dato (CW3).
- **Consecuencia:** quien carga desde la compu sin ese dato (por ejemplo, administración) no puede completar un formulario
  con filtros por persona. Eso lo resuelven las listas dependientes (D2) o la carga en nombre de otra persona (D3).
- **En nombre de otra persona:** exige elegir la persona y un permiso propio, y cambia a quién se le aplican los filtros,
  quién figura en la planilla, a quién avisan las reglas y quién no puede aprobarla. Se recomienda dejarlo para después (D3).

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
  falla con `RecipientUnavailable` si no lo tiene. Para una carga desde la app se propone que quede «No aplica», en vez de
  una incidencia.
- **Sugerencias de qué cargar después:** hoy las ofrece el chat de WhatsApp después de la captura, así que una carga desde la
  app no las dispara. Ofrecerlas en la app queda fuera de esta propuesta.

### Listas de catálogo

- Se agrega una lectura paginada con búsqueda para quien carga:
  - busca por texto en los dos idiomas, detalle y código, y trae páginas de 50;
  - filtra en SQL antes de paginar con los filtros fijos y por persona, con el mismo `FormCatalogMatchPolicy`;
  - cada opción trae título y descripción según la presentación, en el idioma del contenido.
  No tiene el tope de 200, y si los filtros dejan una sola opción, ya viene elegida (CW3).
- En «elegir varias», el máximo sigue siendo el publicado, hasta 20. Los catálogos de Filtros (personas y roles) usan la misma lectura.
- **Listas dependientes (D2):** un tercer filtro, «según otra respuesta», junto a «fijo» y «según quien lo recibe» (CW7).
  - La pregunta de la que depende va antes, es de elegir una y elige del mismo catálogo que el campo de papel «filtro».
  - Al guardar se comprueba que la opción elegida corresponda a esa respuesta.
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
| CW1 · Elegir el formulario (A, B, C) | A: entrada «Cargar» con su tabla. B: «Cargar» en cada fila de «Formularios» y «Agregar respuesta» en la planilla. C: diálogo «Nueva carga» desde la barra superior. |
| CW2 · Cargar una respuesta (A, B, C) | A: página propia (`/formularios/{id}/cargar`), datos y archivos en dos zonas, Enviar en la banda y fijo al pie en el celular. B: panel junto a la planilla, como D2. C: varias respuestas en filas. |
| CW3 · Elegir de un catálogo | La lista con búsqueda, título y descripción, filtros por persona, una sola opción, sin el dato, estados de la lista, elegir varias y celular. |
| CW4 · Fotos y documentos | Arrastrar o elegir, límites y errores antes de subir, avance al enviar, corte de conexión, cámara y galería. |
| CW5 · Después de enviar | Número y estado, aprobación, rechazo por una opción dada de baja, sin conexión, versión nueva, baja y sin permiso. |
| CW6 · Lo cargado (A, B) | A: la planilla con «Canal: App», quién cargó y el origen en D2. B: «Mis cargas» para quien sólo carga. |
| CW7 · Listas dependientes | El filtro nuevo en el editor, el comportamiento en la app y lo que avisa WhatsApp. |
| CW8 · En el celular | Una lluvia de punta a punta. |
| CW9 · Quién ve qué | Cuatro personas con sus permisos y la interfaz en inglés. |

### Qué se reutiliza y qué es nuevo

- **Backend que se reutiliza:**
  - la captura (`BusinessFormRecordRecorder`, `BusinessFormFileRecorder`, `FormCatalogFieldCapturePolicy` y `FormActionRunRecorder`);
  - los filtros y la presentación (`FormCatalogOptionsPolicy`, `FormCatalogMatchPolicy` y `FormCatalogPresentationPolicy`);
  - el idioma (`FormPresentationCulturePolicy`) y la disponibilidad de versiones (`IFormSelectionReader`);
  - el origen por `IStructuredRecordOriginSource`, y la planilla y el Excel sin cambios de contrato.
- **Backend nuevo:**
  - `IFormEntryService` con `FormEntriesController` en `api/forms/entries`, con cuatro rutas: formularios que puedo completar,
    la versión para cargar, las opciones de una pregunta y enviar (multipart);
  - la lectura paginada de la oferta, el origen «App» y el aviso «No aplica»;
  - según D2 y D4, el filtro por respuesta y la lectura de lo propio.
  - Cada ruta va al inventario y tiene su test.
- **Front que se reutiliza:**
  - las piezas de formulario: `Page`, `Surface`, `FormField`, `Input`, `Textarea`, `RadioGroupField`, `SegmentedControl` (Sí/No sin valor inicial) y `Autocomplete`;
  - las de estado y listado: `Banner`, `EmptyState`, `DataTable`, `FilterBar`, `ConfirmDialog`, `DisabledReason` y el Toaster;
  - las de fotos: `PhotoThumbnail`, `PhotoViewer` y la planilla con su panel D2.
- **Front nuevo:**
  - la pantalla elegida de CW1 y la página de carga;
  - la lista de catálogo con búsqueda en el servidor, que extiende `Autocomplete` con descripción, páginas y estados;
  - la pregunta de archivos con cámara, y el estado «Enviada»;
  - según la elección, el panel de CW2-B y «Mis cargas».

## Decisiones para tomar

1. **D1 · Qué tableros.**
   - CW1: A, B o C.
   - CW2: A, B, C, o A con B como D2 y DC1 (vista rápida en escritorio y página propia, también en el celular).
   - CW6: A, y si se suma B.
   - Recomendación: CW1-A, CW2-A con CW2-B y CW6-A.
2. **D2 · Listas dependientes.**
   - A (recomendada): sumarlas en una etapa propia, sólo en la app; por WhatsApp ese formulario no se manda hasta el intercambio con Meta.
   - B: esperar al intercambio y hacerlas en los dos canales a la vez.
3. **D3 · Cargar en nombre de otra persona.**
   - A (recomendada): no por ahora; cada uno carga lo suyo.
   - B: sí, con un permiso propio y un tablero aparte.
4. **D4 · «Mis cargas».**
   - A (recomendada): quien sólo carga ve lo propio (número, estado, valores y PDF) sin «Ver respuestas».
   - B: no; sólo la confirmación al enviar.

## Plan por etapas

Cada paso queda igual a su tablero elegido: al programarlo se abre el tablero y se compara antes de cerrarlo.

1. **Etapa 1 · Cargar desde la app.**
   - Backend: el servicio, las rutas, la lectura de opciones y la captura con origen «App».
   - Front: la pantalla elegida de CW1, CW2-A, CW3, CW4 y CW5, y la planilla con «Canal: App» (CW6-A).
   - Cierre con la rutina de [validar un cambio](../guides/validar-un-cambio.md): integración con PostgreSQL, Redis y Azurite;
     contratos exportados; front con build, lint, tests y visuales; recorrido es/en a 1440, 390 y 320; y la cámara de un
     celular real por el túnel.
2. **Etapa 2 · Escritorio y lo propio.** El panel junto a la planilla (si se elige CW2-B) y «Mis cargas» (si D4 es A).
3. **Etapa 3 · Listas dependientes** (si D2 es A): el filtro, el editor, la comprobación de publicación, el bloqueo del envío
   por WhatsApp y la validación al guardar. La regla nueva va a [formularios](../features/formularios.md).
4. **Después:** en nombre de otra persona (si D3 es B), la carga en filas (si se elige CW2-C) y las listas en WhatsApp con el
   intercambio con Meta.

Fuera de alcance: corregir una respuesta, importar respuestas desde Excel, borradores guardados y carga sin conexión, y
grupos con líneas. Aplicación usa pares fijos de producto y dosis; una lista de líneas necesita «grupos y remitos con
líneas», que tiene su propio pendiente.
