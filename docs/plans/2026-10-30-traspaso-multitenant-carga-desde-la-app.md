# Cargar formularios desde la app: traspaso 3 al multitenant (2026-10-30)

Autor: el chat guía de ArquitecturaBase. Destino: ArquitecturaBaseMutitenant, para su etapa **E20 · Cargar una respuesta desde
la web** (`docs/plans/formularios/20-cargar-desde-la-web.md` del multitenant).

El resumen de la E20 dice que la etapa «no tiene equivalente en ArquitecturaBase». Ya lo tiene. El 2026-10-29 y el 2026-10-30
se decidió entera acá, y su primera etapa quedó programada y revisada. Esa primera etapa es la E20: un empleado completa
un formulario desde la web, a su nombre, con la misma captura que por WhatsApp. Este traspaso cuenta:
- qué decidió el usuario;
- cómo quedó;
- qué se encontró al revisarla;
- qué tiene que mirar el multitenant al adaptarla.

**Como en el [traspaso 2](2026-10-23-traspaso-multitenant/README.md), no es código para copiar.** Se toman las pantallas y las
reglas de producto, y el código sale del multitenant, según la decisión 18 de su hoja de ruta. Ese código usa sus organizaciones,
su RLS, `[FeatureGate("forms")]` y sus fichas de guardado, concurrencia e idempotencia.

## 1. En pocas palabras

- **Decidido:** el usuario tomó 11 decisiones viendo cada tablero, y además eligió el texto de la banda plegada de las fotos. Las de la carga en sí
  valen igual allá. Donde chocan con el modelo del multitenant, están en el §5.
- **Programado, la Etapa 1:**
  - entrar desde «Formularios» o desde la planilla, y cargar en una página propia o en un panel;
  - listas de catálogo con búsqueda en el servidor;
  - fotos y documentos en el mismo envío;
  - la pantalla «Enviada»;
  - el canal «App» en la planilla;
  - el formulario armado en una grilla de hasta tres por renglón, con zonas plegables.
- **Decidido sin programar, las Etapas 2 a 5:** lo propio para quien sólo carga, corregir y anular, listas dependientes y títulos de
  sección. Tocan otras etapas del multitenant (§5).
- **Revisado:** la revisión encontró cuatro problemas, ya corregidos (§4). Sirven como lista de «no repetir».
- **El código todavía no está en GitHub:** los commits son locales. Desde esta PC se leen en
  `C:\Users\ezequ\source\repos\ArquitecturaBase` y `C:\Users\ezequ\source\repos\ArquitecturaBaseFront` (§6).

## 2. Lo que decidió el usuario

| # | Decisión | Qué implica |
| --- | --- | --- |
| 1 | Elegir el formulario desde «Formularios» (CW1-B), sin entrada nueva | Cada fila suma «Cargar» para quien tiene «Completar formularios», y la planilla suma «Agregar respuesta». «Formularios» se abre también con ese permiso: quien sólo carga ve los formularios activos con «Cargar», sin conteos. |
| 2 | Página propia y panel (CW2-A y CW2-B) | «Nueva respuesta» va en `/formularios/{id}/cargar`, para la compu y el celular. Desde la planilla, en la compu, se abre un panel al costado con «Mantener las respuestas para la siguiente». |
| 3 | Lo cargado va en la planilla de siempre (CW6-A y CW6-C) | Quien ve respuestas usa la planilla con «Canal: App». Quien sólo carga ve esa misma planilla con lo suyo: estado, PDF y motivo de rechazo. No hay «Mis cargas» aparte. |
| 4 | Listas dependientes, sólo en la app (CW7) | Es un tercer filtro, «según otra respuesta». Un formulario con ese filtro no se manda por WhatsApp hasta que exista el intercambio cifrado con Meta. |
| 5 | Por ahora no se carga en nombre de otra persona | Coincide con su decisión 73. |
| 6 | Corregir (CW10-A) | La corrección reemplaza a la anterior con el mismo número y pide motivo. La anterior queda en el historial. La corrección vuelve a pasar por las reglas: una respuesta aprobada vuelve a «Por aprobar». |
| 7 | Anular con motivo (CW11-A) | Queda en «Anulada», con quién, cuándo y por qué, y se cierra la aprobación pendiente. No se borra ni se deshace. Sale de la planilla, de los conteos y del Excel, salvo que se pida. |
| 8 | Quién corrige y anula | Quien la cargó, mientras no esté aprobada (siempre, si el formulario no pide aprobación). Con un permiso nuevo, «Corregir y anular respuestas», cualquiera y en cualquier momento. |
| 9 | La unidad va en la etiqueta (CW13-A) | Por ejemplo, «Densidad (kg/ha)». No hay un dato «Unidad». |
| 10 | Hasta tres preguntas por renglón (CW16) | Grilla de tres columnas en la compu, dos en la tableta y una en el celular y en el panel. El ancho sale del tipo de pregunta y las preguntas no se reordenan. |
| 11 | Títulos de sección con zonas plegables (CW15-B y CW16) | El autor agrega títulos entre las preguntas: en la app, cada título arma una zona; en WhatsApp, es un subtítulo. Todas las zonas se pliegan y arrancan abiertas. Al enviar, la zona que tiene errores se abre sola. |
| — | Banda plegada de «Fotos y documentos» (2026-10-30) | Dice lo que se adjuntó, por ejemplo «3 fotos» o «2 fotos · 1 archivo». Sin archivos dice «0 de 1 respondidas», como las demás zonas. |

Las decisiones 6 a 8 cambian la regla de inmutabilidad. Una respuesta confirmada sigue sin modificarse, pero puede quedar
reemplazada por su corrección, o anulada.

## 3. Cómo quedó la Etapa 1

### Backend

- **Rutas,** en `api/forms/entries/{definitionId}`:
  - un `GET` que trae la versión activa para cargar;
  - `GET .../versions/{versionId}/questions/{fieldId}/options`, con las opciones de una pregunta de catálogo: 50 por página, con
    búsqueda y con los filtros de quien carga;
  - un `POST` multipart con `requestId`, `versionId`, las respuestas y los archivos.

  Las tres piden cuenta activa y `formrecords.create`, antes de leer y otra vez dentro del guardado. «Formularios»
  (`GET /api/forms/overview`) también se abre con `formrecords.create`: muestra sólo los formularios activos, sin conteos ni
  última respuesta.
- **La misma captura que WhatsApp** (`BusinessFormRecordRecorder`): validaciones, número bajo el lock común, copia de las opciones,
  archivos, reglas, aprobación y PDF.
- **Sólo la versión publicada activa.**
  - Si se publicó otra versión, responde 409 `Forms.Entry.VersionChanged`; si el formulario se dio de baja, 409
    `Forms.Entry.Unavailable`.
  - Una pregunta que no existe responde 404 `Forms.Entry.QuestionNotFound`.
  - El front ofrece pasar a la versión nueva sin perder lo cargado.
- **Reintentos por contenido.** El mismo `requestId` con las mismas respuestas devuelve el primer resultado. Esa comparación va
  **antes** de mirar la versión o la baja. Con otras respuestas, es otro envío.
- **La oferta sella sólo lo elegido.** Antes de guardar se comprueba que cada opción esté activa y pase los filtros para esa
  persona. La lista completa no se guarda, porque puede tener miles de opciones. Una opción dada de baja entre abrir y enviar
  responde `Forms.Record.CatalogItemInactive` en su pregunta.
- **Filtros por persona.**
  - Si a la persona le falta el dato que pide un filtro, la pregunta frena con `Forms.Record.RecipientDataMissing` y el nombre
    del dato.
  - Si los filtros dejan una sola opción, esa opción ya viene elegida.
- **Archivos.**
  - Viajan en el mismo `POST`, sin una subida previa aparte.
  - Tienen los límites de WhatsApp: los de cada pregunta, 10 por respuesta, 25 MiB cada uno y 100 MiB en total.
  - Se verifican por contenido. El endpoint acepta hasta 101 MiB.
- **El canal.**
  - El registro guarda `EnteredInApp` (migración `AddFormRecordEnteredInApp`).
  - La planilla lo muestra en «Canal» como «App», con su filtro también en los conteos, la navegación y el Excel.
  - El detalle dice «Recibida · … · en la app, la cargó …».
- **El aviso a quien cargó.** «Mandarle un mensaje a quien la cargó» queda en «No aplica» si esa persona no tiene un canal donde
  recibirlo. Lo decide `CanReachAsync`, del puerto del canal.
- **Idioma del contenido:** el del menú, si la versión lo publicó completo; si no, el original.

### Front

- **Entradas.** «Cargar» en «Formularios» (CW1-B). «Agregar respuesta» en la planilla abre el panel (CW2-B) sólo en la compu; en el
  celular abre la página.
- **La página «Nueva respuesta»** (CW2-A).
  - Cancelar y Enviar van en la banda; en el celular, Enviar queda fijo al pie.
  - Valida al enviar: cada error aparece en su pregunta y el foco va a la primera con error.
  - Salir con algo cargado pide confirmación.
  - Mientras sube, el botón dice «Enviando… 64 %».
- **Las listas de catálogo** (CW3) buscan en el servidor y muestran el total, la descripción de cada opción y «Mostrar 50 más». En
  el celular ocupan la pantalla entera.
- **Las fotos** (CW4) se arrastran o se eligen, con un contador. Tipo, tamaño y cantidad se revisan antes de enviar. En el celular,
  «Tomar foto» va primero.
- **«Enviada»** (CW5) muestra lo que se mandó, con el número y el estado, y ofrece «Cargar otra». Quien ve respuestas tiene además
  «Ver en la planilla». La pantalla se arma con lo enviado, porque quien sólo carga no puede leer la respuesta guardada.
- **El borrador** vive en la página o en el panel: nunca va a la URL, al storage ni a la caché.

### Cómo se arma cada pregunta (CW12, CW13-A y CW16)

El autor decide el contenido y la app decide la forma, con una tabla fija por tipo de pregunta:

| Ocupa | Tipos de pregunta |
| --- | --- |
| Una columna | Número, fecha, sí o no, texto corto de hasta 30 caracteres y lista desplegable |
| Dos columnas | Catálogo, opciones en fila (hasta cinco, de hasta 20 caracteres) y texto corto de hasta 80 |
| El renglón entero | Texto largo, texto corto de más de 80, elegir varias con opciones propias, fotos y documentos |

- **Orden.** Las preguntas no se reordenan: una que no entra baja de renglón, y hay una línea entre cada renglón visible. Por eso
  el orden del editor decide dónde quedan huecos.
- **Zonas.** Las preguntas van en «Datos» y los archivos en «Fotos y documentos». Las dos se pliegan. Plegada, la banda de «Datos»
  dice «N de M respondidas» y la de los archivos, lo que se adjuntó.
- **Controles.**
  - Número y fecha son angostos.
  - Sí o no arranca sin nada marcado.
  - Hasta cinco opciones cortas van en fila; con más, una lista desplegable.
  - Elegir varias muestra «Elegí hasta N».
  - El texto largo tiene de 2 a 6 renglones.

## 4. Lo que se encontró al revisarla (no repetir)

1. **Los renglones se arman con las columnas en uso.** Al principio siempre se armaban de a tres, y en el celular dos preguntas
   quedaban en un mismo bloque, sin línea entre ellas. Ahora el ancho de cada pregunta se recorta a las columnas disponibles
   (front `d7432893`).
2. **Las columnas salen del ancho de la zona, no del de la pantalla.** En una tableta con un panel del menú abierto entra una sola
   columna. Calculadas con el ancho de la pantalla, un campo se salía de la zona. Hoy son tres desde 46 rem, dos desde 30 rem y una
   por debajo (`entryColumns`, medido con `ResizeObserver`).
3. **En el panel, número y fecha siguen angostos**, como en el tablero. Ir «a lo ancho» con una sola columna vale sólo para la página
   (front `c95729bc`).
4. **Las migas de pan también miden su contenedor.** Si la barra superior mide menos de 40 rem, quedan sólo el formulario y la hoja.
   Cuando dependían del ancho de la pantalla, quedaban segmentos vacíos (`@container` en la barra, front `c95729bc`).

## 5. Adaptación a su E20

| Su E20 dice | En ArquitecturaBase | Qué evaluar |
| --- | --- | --- |
| «Cargar respuesta» va en la pestaña «Respuestas» de cada formulario y, en el teléfono, en su menú | «Cargar» en cada fila de «Formularios», y «Agregar respuesta» con el panel en la planilla (decisiones 1 y 2) | Con su lista única (decisión 79), «Cargar» va en la fila de «Formularios». Quien sólo tiene `formrecords.create` no abre «Respuestas»: necesita ver la lista con sus formularios para empleados (decisión 50) y «Cargar», como acá. |
| El canal «web», un valor de `FormRecords.Channel` | `EnteredInApp`, con la palabra «App» en «Canal» | Es el mismo concepto. Falta elegir la palabra que ve la persona: acá es «App», porque también cubre el celular. El filtro tiene que llegar a los conteos, la navegación y el Excel. |
| `[Idempotent]` en el `POST` | `requestId` en el multipart, comparado por contenido, antes de mirar la versión y la baja | Su `[Idempotent]` tiene que devolver el primer resultado aunque después se haya publicado otra versión o se haya dado de baja el formulario. Además, otras respuestas con la misma clave no pueden devolver el primer resultado (ficha de idempotencia). |
| La misma captura: catálogos (E16), archivos (E18) y condiciones, cálculos y líneas (E19) | Catálogos y archivos, sí. Condiciones, cálculos y líneas no existen | Las reglas de armado no cubren la E19. Falta definir qué ocupa una línea, dónde aparece una pregunta condicional (en su lugar, con los renglones recalculados) y cómo se ve un cálculo. Lo dibuja Diseño, con CW16 como base. |
| Empleado: miembro activo con teléfono verificado (hoja de ruta, «Quién es quién») | Cuenta activa con `formrecords.create`. Sin WhatsApp, el aviso a quien cargó queda en «No aplica» | Decidir si cargar desde la web exige el teléfono verificado. Acá no lo exige. |
| Al guardar se ven el número y la tarjeta, como en el chat | «Enviada» se arma con lo enviado, el número, el estado y la hora | Quien sólo carga no tiene `formrecords.read`, así que la pantalla no puede leer la respuesta guardada. |
| No lo dice | Sólo la versión activa, y un 409 que ofrece «Pasar a la versión nueva» sin perder lo cargado | Sumarlo al resumen. |
| No lo dice | Opciones paginadas en el servidor, 50 por página, con los filtros de quien carga y sin el tope de 200. La oferta sella sólo lo elegido | Sumarlo a la E16 o a la E20: la lista completa no se guarda. |
| El contenido no tiene idioma (decisión 35) | Se usa el idioma del menú, si la versión lo publicó completo | No aplica allá. |
| Botones según la decisión 43 | Cancelar y Enviar en la banda, Enviar fijo al pie en el celular, y las altas dicen «Agregar …» | Dibujarlos con su decisión 43. |
| Sin permisos nuevos | La Etapa 3 suma «Corregir y anular respuestas» | Si adoptan corregir y anular (decisiones 6 a 8), es un permiso más en su tabla de 13. |
| No lo dice | No existe | La respuesta cargada guarda las empresas activas de quien carga, como la que llega por WhatsApp (decisiones 1, 9 y 24). |

**Las decisiones que tocan otras etapas del multitenant:**
- **3, lo propio para quien sólo carga:** la planilla de la E15 y la E20.
- **4, listas dependientes:** la E16, por los filtros, y la E19, por las condiciones. Hay que evaluar si «según otra respuesta» es
  un filtro de catálogo, como acá, o una condición.
- **6 a 8, corregir y anular:** la E17 (ciclo de vida) y la E21 (reglas y aprobación). Cambian la regla de inmutabilidad.
- **11, títulos de sección:** el editor de la E14, la E20 y el subtítulo en WhatsApp.

## 6. Dónde está cada cosa en ArquitecturaBase

- **Plan,** con las decisiones, el análisis y las etapas: [cargar formularios desde la app](2026-10-29-cargar-formularios-desde-la-app.md).
- **Reglas implementadas:** [formularios](../features/formularios.md#carga-desde-la-app--etapa-1-2026-10-30).
- **Diseño elegido,** con las reglas de armado y la correspondencia con `shared/ui`: `ArquitecturaBaseFront/docs/design/carga-desde-la-app.md`.
- **Tableros:** el lienzo [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), de CW0 a CW16. Los
  elegidos llevan la banda verde y el sello «Elegida».
- **Commits:**
  - backend, de `f94f1257` a `6add270a` (11 commits);
  - front, de `cc485e09` a `c95729bc` (12 commits). Los dos últimos son las correcciones de la revisión.
- **Código de referencia:**
  - backend: `FormEntriesController`, `FormEntryService`, `FormEntryErrors`, `BusinessFormRecordRecorder.CaptureEntryAsync` y la
    migración `20261030102637_AddFormRecordEnteredInApp`;
  - front: en `src/features/business-forms/`, `entryForm.ts` (zonas, anchos, renglones, columnas, validación y envío),
    `components/FormEntryForm.tsx`, `pages/FormEntryPage.tsx`, `components/FormEntryPanel.tsx`, `components/EntryCatalogField.tsx` y
    `components/EntryFilesField.tsx`; y `api.upload` en `src/shared/api/httpClient.ts`, que muestra el avance de la subida;
  - contratos: los cuerpos exportados de `forms-entries-http` y `src/features/business-forms/api/entriesHttpContract.test.ts`.

## 7. Prompt para el chat guía del multitenant

```text
Leé el traspaso 3 de ArquitecturaBase, en
C:\Users\ezequ\source\repos\ArquitecturaBase\docs\plans\2026-10-30-traspaso-multitenant-carga-desde-la-app.md.
Cuenta la carga de formularios desde la app que allá se decidió entera y cuya primera etapa ya está programada. Esa primera etapa
es tu E20, «Cargar una respuesta desde la web» (docs/plans/formularios/20-cargar-desde-la-web.md). Los commits todavía no
están en GitHub: leelos en C:\Users\ezequ\source\repos\ArquitecturaBase y ...\ArquitecturaBaseFront.

1. Evaluá el traspaso contra tu hoja de ruta, la E20 y las etapas que toca: E14, E15, E16, E17, E19 y E21. No es código para
   copiar. Según tu decisión 18, se toman las pantallas y las reglas de producto, y el código sale de tu repo.
2. Actualizá el resumen de la E20 con lo que se adopta: entradas, página y panel, listas, fotos, «Enviada», canal, armado de
   las preguntas y la lista de «no repetir». Sacá la frase «No tiene equivalente en ArquitecturaBase».
3. Las decisiones 3, 4, 6 a 8 y 11 del traspaso tocan otras etapas. Anotalas en la etapa que corresponda o, si chocan con tu
   modelo, en «Decisiones pendientes».
4. Donde tu modelo resuelve distinto (organizaciones, RLS, empresas, clientes, idioma, teléfono verificado), proponé la
   adaptación. Lo que tenga que decidir el usuario, preguntáselo de a una pregunta, con las opciones y la recomendada marcada.
5. Diseño dibuja los tableros en tu lienzo, con tus piezas y la decisión 43. Toma como referencia los tableros elegidos del lienzo
   de ArquitecturaBase: https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M.
6. No programes nada: la E20 va después de la E19. Commits chicos en español, en main, sin push.

Al terminar, contame qué cambió en tus specs, qué decisiones quedaron pendientes y qué tableros hay que dibujar.
```

Este traspaso se retira cuando el multitenant lo haya incorporado a su E20 y a las etapas que toca.
