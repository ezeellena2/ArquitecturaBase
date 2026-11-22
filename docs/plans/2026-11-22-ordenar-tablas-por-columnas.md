# Ordenar las tablas por sus columnas

Fecha: 2026-11-22. Estado: **decidido el mismo día (OT1-C; «Por aprobar» queda como cola). El usuario pidió arrancar el
backend durante la prueba integral: B1 (la regla común) y B2 (Formularios) están en `366bedb7`, B3 y B4 (Usuarios y Roles) en
`21ffbfdb`, B5 y B6 (Catálogos y Diseñar formularios) en `525d748f`, B9 y B10 (Documentos recibidos y Formularios que faltan)
en `67924ec4`, B7 y B11 (la planilla y Datos de personas) en `d6564f3b`: el backend está completo. Las pruebas con PostgreSQL
de BusinessForms van al cierre.** El front se programa en una copia y entra después del cierre de la prueba, junto con la
[pieza única para elegir](../../../ArquitecturaBaseFront/docs/plans/2026-11-19-una-sola-pieza-para-elegir.md), cuya lista
usa «Ordenar por».

El 2026-11-22, durante la prueba integral y mirando la lista «Formularios» (`/formularios/respuestas`), el usuario pidió:
«todas las tablas sean ordenables por las columnas como se quiera ordenar tanto como por un botón que diga ordenar por y
vaya a la bbdd y ordene correctamente con un order by o directamente ordene el front desde cada columna lo que tenga ya
maquetado. Esto es importante porque cambia muchas cosas de cómo se filtra».

Los tableros están en el lienzo [Ordenar las tablas por sus columnas](https://claude.ai/artifact/HumQW2KEkQxzvw8pbcRcvs):
«Hoy» (relevamiento y regla), OT1-A, OT1-B y OT1-C, con la tabla de Formularios que se prueba en Play. Antes de escribirlo se
buscó un plan donde anexarlo (`ordenar`, `ordenable`, `sort`, `order by` en `docs/plans` y `docs/features` de los dos
repositorios): sólo hay menciones al pasar, así que va aparte.

## Decisión del usuario (2026-11-22)

**OT1-C** («yo haría OT1-C»): el encabezado ordena y, al final de la barra de filtros, «Ordenar por» dice el orden con
palabras y lo cambia. Los dos mueven el mismo orden. En el celular el botón ocupa el ancho y su lista se abre a pantalla
completa. Descartó OT1-A (sólo el encabezado) y OT1-B (sólo el botón, con el encabezado que no se toca).

La regla del tablero («Igual en las tres opciones», abajo) quedó aceptada con la elección. Si el usuario cambia un punto, se
corrige acá antes de programar.

## La regla

1. **Toda columna con un dato ordena.** No ordenan Acciones, las de un ícono (PDF) ni las de varios valores: los roles de una
   persona, lo que lee o valida un estándar, las preguntas de varias opciones, de grupo y de ubicación.
2. **Dónde:** una tabla paginada ordena en la base, con `ApplySort` y su desempate; ordenar sólo la página visible estaría mal.
   Una tabla que llega entera ordena en el navegador con la misma pieza, y se ve igual.
3. **Con los filtros:** el orden se suma a la búsqueda, las solapas, los filtros y la carpeta sin borrarlos. Cambiarlo vuelve a
   la página 1, y «Limpiar todo» lo conserva.
4. **En la dirección:** `sort=campo` o `sort=-campo`, con el nombre del backend, como la página y el tamaño. Una columna
   dinámica suma su id (`sortFieldId` en la planilla, `sortAttributeId` en Datos de personas). Atrás y un enlace compartido
   conservan el orden.
5. **Lo vacío al final** («—»), en los dos sentidos, como ya hace «Ordenar por…» de las opciones de un catálogo. Con el mismo
   valor, las filas quedan en el orden de siempre de esa lista y después por su clave única.
6. **Un estado o un tipo** ordena por el orden del circuito, que es el de su declaración (una respuesta: Recibida, Por aprobar,
   Aprobada, Rechazada, Anulada), igual en español y en inglés. Nunca por el texto guardado, que hoy está en inglés
   (`HasConversion<string>`), ni por el traducido.
7. **Sin orden elegido**, cada lista abre como hoy. Si ese orden es una columna, su flecha se ve y el botón lo dice; si es
   compuesto (Formularios: primero lo que espera aprobación, después lo último recibido y por nombre), no hay flecha y el
   botón dice «Ordenar por».
8. **El primer toque** ordena en el sentido útil del dato: el texto de la A a la Z, los números de mayor a menor, las fechas de
   la más reciente a la más antigua, un sí o no con «Sí» primero y un estado en el orden del circuito. El segundo lo invierte.
9. **No cambian** las tablas cuyo orden es un dato: las opciones de un catálogo (su «Ordenar por…» guarda el orden, OR1-A), los
   atributos de Datos de personas (su posición es el orden de las columnas de personas), los renglones de una respuesta, las
   preguntas de una propuesta, la lectura de documentos del editor, las vistas previas de una importación, las comparaciones y
   las secciones del conocimiento del asistente. Tampoco «Por aprobar», que es una cola: va de la más vieja a la más nueva y la
   revisión con ↑/↓ la recorre en ese orden (el usuario eligió «Dejarla como cola», 22/11). Las listas que no son tablas (los chats, las conversaciones y los casos del
   asistente, las filas de ARCA) quedan afuera.

## El diseño elegido, anclado al tablero OT1-C

La pantalla queda igual al tablero [OT1-C](https://claude.ai/artifact/HumQW2KEkQxzvw8pbcRcvs#artboard-OT1-C):

- **El encabezado.** Cada columna que ordena es un botón con el mismo texto y la misma letra del encabezado. La que ordena
  muestra su flecha (↑ o ↓, 12 px nominales, color de la marca) después del título; en una columna de números va antes, para
  que el título siga alineado con las cifras. Las demás que ordenan muestran ↕ y el tono `#d2decb` al pasar el mouse o con el
  foco del teclado. Una columna que no ordena no cambia al pasar. El lector de pantalla oye el sentido por `aria-sort`; no hay
  tooltip, porque el botón ya dice su nombre. Con puntero táctil el encabezado mide 44 px.
- **«Ordenar por».** Va último en la barra de filtros, separado de los filtros porque no saca filas. Es el botón de la barra
  de la pieza única para elegir (DS1-A): el ícono de ordenar, «Columna, sentido» («Recibida, más reciente primero») y la flecha.
  Abre una lista con dos zonas: «Columna» (las columnas visibles que ordenan, con la tilde a la izquierda en la elegida) y
  «Orden» (los dos sentidos, nombrados según el dato con los textos de `forms:catalogs.sort.*`, que pasan a `common`). Cada
  elección se aplica enseguida, como «Columnas», y la lista queda abierta hasta tocar afuera o Escape. Elegir otra columna la
  ordena en su sentido útil.
- **En el celular.** El botón ocupa el ancho, dentro de la superficie de los filtros, y su lista se abre a pantalla completa con
  renglones de 44 px y la × para cerrar. El encabezado también ordena.
- **Mientras llega el nuevo orden**, las filas se atenúan con la línea bajo el encabezado (LC2-A), como al paginar.

## Relevamiento (22/11)

Front en `81fa13f6`, backend en `6bbf37d6`. Lo que ya existe: `DataTable` acepta `sortable`, `sort` y `onSortChange`, pero el
encabezado ordenable no muestra flecha ni cambia al pasar (sólo `aria-sort`); `usePagination.toggleSort` alterna `campo` y
`-campo` y vuelve a la página 1; el backend valida `SortableFields` con `PagedRequestValidator<T>` y ordena con `ApplySort`
(un campo y el desempate por clave única), que deja los `NULL` primero en un `DESC`.

El nombre del campo de orden es el `id` de la columna del front (convención de `DataTable`). Donde hoy no coinciden, se
renombra el `id` del front.

| Tabla | Ruta | Pagina | Ordenan hoy | Campos que se suman |
| --- | --- | --- | --- | --- |
| Formularios | `/formularios/respuestas` | Servidor | Ninguna (orden fijo) | `name`, `formState`, `pendingApprovalCount`, `responseCount`, `lastResponseNumber`, `lastSubmittedAtUtc`, `lastResponseState` |
| Diseñar formularios | `/formularios` | Servidor | `name`, `questionCount`, `lastChangedAtUtc` | `displayState`, `completedIn`, `deprecation`, `activeVersion`, `responseCount`, `lastChangedBy` |
| Biblioteca | `/formularios/biblioteca` | Entera | Ninguna | En el navegador: `name`, `version`, `source`, `copies`, `availability` |
| Catálogos | `/catalogos` | Servidor | `name` (el back acepta también `kind`, `revision`, `optionsCount`, `usageCount`) | `state`; el front marca `kind`, `optionsCount` y `usageCount` |
| Formularios que faltan | `/formularios/faltantes` | Servidor | `lastRequestedAtUtc` | `name`, `discardedBy`, `discardedAtUtc`, `requestCount`, `personCount` |
| Documentos recibidos | `/formularios/documentos` | Servidor | `receivedAtUtc` | `document`, `sender`, `form`, `state`, `deleteAfterUtc`, `record` |
| Planilla de respuestas | `/formularios/:id/respuestas` | Servidor | `number`, `submittedAtUtc`, `actor`, `field` | `corrected`, `channel`, `state`, `rules`, los campos de un catálogo y los resultados |
| Por aprobar | dentro de la planilla | Servidor | Ninguna (la más vieja primero) | Ninguno: queda como cola (22/11) |
| Datos de personas › Personas | `/datos-de-personas` | Servidor | Ninguna en pantalla (el back acepta `displayName`) | `attribute` con `sortAttributeId` |
| Usuarios | `/usuarios` | Servidor | `email`, `displayName`, `createdAtUtc` | `whatsapp`, `verified` |
| Roles | `/roles` | Servidor | Ninguna (el back acepta `name`, `createdAtUtc`) | `description`, `userCount`, `permissions` |
| Documentos de una propuesta | `/formularios/documentos/armar/:id` | Entera | Ninguna | En el navegador: `document`, `sender`, `receivedAtUtc` |

Pantallas del front: `FormOverviewPage`, `FormsPage`, `FormLibraryPage`, `CatalogsPage` con `CatalogListTable`,
`MissingFormsPage`, `ReceivedDocumentsPage`, `FormResponsesPage` con `useResponseRouteQuery`, `FormPendingApprovals`,
`PersonDataPage`, `UsersPage` con `users/columns.tsx`, `RolesPage` y `documents/DocumentFormSources`. Lectores del backend:
`FormOverviewReader`, `FormDefinitionReader`, `FormCatalogReader`, `FormMissingRequestReader`, `FormReceivedDocumentReader`,
`FormResponseReader.Filters`, `FormApprovalReader`, `FormPersonDataReader`, `UserReader` y `RoleReader`.

## Cómo se programa

Por cortes que se integran a `main` apenas andan, para que el usuario pruebe el primero en horas. TDD: el test en rojo
primero. Cada corte corre el build y los tests
rápidos del área; lo visual, las capturas y la corrida completa van al cierre.

### Corte 1: la regla común y Formularios

- **B1 · `ApplySort` con la regla (hecho, 22/11).** En `QueryableExtensions` (Infrastructure): lo vacío al final en los dos
  sentidos (en un `ASC` ya lo hace PostgreSQL; en un `DESC`, o con varias claves, un término «es nulo» antes de la clave, que
  no se agrega en un `ASC` para no repetir una subconsulta); los empates en el orden de siempre de la lista y después la clave
  única; un campo con varias claves declarado con un tipo anónimo (`lastResponseNumber` es prefijo y número); un enum por su
  orden de declaración con un `CASE`, aunque se guarde como texto. Un orden de siempre de varias claves usa
  `ApplySortWithDefaultOrder` con `SortKey<T>`; los 17 lectores de hoy siguen con `ApplySort`. Tests en memoria y de SQL sin
  conexión (`QueryableExtensionsTests`). Cambia el empate de la planilla: con la misma fecha, primero el número más nuevo
  (`FormResponseHttpTests` ajustado). `backend.md` y la fila de `AGENTS.md` suman la regla.
- **B2 · Formularios (hecho, 22/11).** `ListFormOverviewRequest.SortableFields` con los siete campos; `FormOverviewReader`
  ordena con `ApplySortWithDefaultOrder` y conserva su orden de siempre sin `sort` y para desempatar; el último estado se lee
  como enum para ordenarlo por el circuito. Tests: unitarios de los siete campos, SQL sin conexión
  (`FormOverviewQueryTests`) y HTTP con PostgreSQL (`Overview_sorts_by_its_columns_with_forms_without_responses_last`, corre en
  el cierre conjunto). El OpenAPI de `sort` sigue siendo un texto, como en todas las listas: no hay un mecanismo que nombre los
  campos y el front no genera tipos desde él.
- **F1 · La pieza.** En `shared/ui`: `Column.sortable` pasa a obligatorio (`true` o `false`), para que toda tabla nueva decida, y
  se suma `sortKind` (`text`, `number`, `date`, `yesNo`, `state`). `DataTable` dibuja la flecha, ↕, el tono, la flecha antes del
  título en una columna `numeric` y los 44 px táctiles. `SortMenu` es el botón de la barra con la lista de la pieza única
  (DS1-A), a pantalla completa en el celular. `usePagination.toggleSort(campo, tipo)` elige el sentido útil en el primer toque.
  `useClientSort` ordena una lista entera con `Intl.Collator` (sin mayúsculas ni tildes, con números), lo vacío al final y el
  orden estable. Tests de la pieza: flecha y `aria-sort`, el sentido útil, la lista, el teclado y el celular.
- **F2 · La regla escrita.** En el front, `visual-baseline.md` (una viñeta en «Listados y filtros», la sección «Ordenar por
  columnas — 2026-11-22» y el punto 5 de la lista de chequeo) y `premisas-de-desarrollo.md` («Tablas» y «Filtros y búsqueda»),
  con los textos de abajo. El mapa de tableros suma el lienzo.
- **F3 · Formularios** usa la pieza: sus columnas con `sortable` y `sortKind`, «Ordenar por» en la barra y los `id` renombrados.

### Corte 2: administración y catálogos

Usuarios (B3: `whatsapp` por el número guardado, `verified`), Roles (B4: `description`, `userCount` y `permissions`, la
cantidad de permisos), Catálogos (B5: `state`; `kind` pasa al orden de declaración) y Diseñar formularios (B6: los seis campos
de la tabla). Cada lista, con sus tests HTTP y su pantalla (F4 a F7). En Catálogos la columna `origin` del front pasa a `kind`;
«Uso» en 0 muestra «—» y ordena como 0. En Diseñar formularios no ordenan «Deprecación» (la da el canal de WhatsApp y no se une
en SQL), «Estado» en «Dados de baja» (la misma razón) ni «Respuestas» sin «Ver respuestas».

### Corte 3: respuestas y documentos

La planilla (B7: `corrected`, `channel`, `state`, `rules`; los campos de un catálogo de una pregunta de una opción, por el valor
guardado del ítem elegido; los resultados de las reglas, por su valor guardado y, si resulta que se calculan al leer, quedan sin
orden y se anota acá), Documentos recibidos (B9: los datos leídos, una columna por pregunta, no ordenan porque viven en la
propuesta cifrada), Formularios que faltan (B10) y Datos de personas (B11: el valor de un
atributo, con `sortAttributeId`). Cada una con su pantalla (F8 a F12). B8 (Por aprobar) no se hace: queda como cola.
En la planilla, un campo del catálogo ordena con `catalogField` y `sortCatalogFieldId`, y un resultado con `result` y
`sortResultId` (`<fuente>.<clave>`, el id de su columna); los dos comparan el valor guardado como jsonb, así que un número
ordena como número. El canal pone la app primero y lo pasado desde un documento, que muestra «—», al final; si el front lo
rotula algún día, cambia esa clave.

### Corte 4: las listas enteras

La Biblioteca y los documentos de una propuesta ordenan en el navegador con `useClientSort` (F13 y F14), con el orden en la
dirección como las demás.

### Cierre

El build sin advertencias, `Application.UnitTests` y `ArchitectureTests` enteros, la integración de lo que cambió, el vitest de
lo que cambió y las escenas visuales de las tablas tocadas. La corrida completa va una sola vez, en el cierre que coordine la
prueba integral. Después, la regla pasa a las fuentes de las premisas y el plan se retira con la auditoría documental.

## Textos para la lista de chequeo (F2)

Punto 5 de la lista de chequeo de `visual-baseline.md`:

> 5. **Un dato por columna, que ordena:** ¿cada columna de una tabla o de una lista con columnas muestra un solo dato? En un
>    listado, ¿toda columna con un dato ordena desde su encabezado y desde «Ordenar por», salvo Acciones, un ícono o varios
>    valores? ([un dato por columna](#un-dato-por-columna--2026-11-09), [ordenar](#ordenar-por-columnas--2026-11-22))

Viñeta de «Listados y filtros» en `visual-baseline.md`:

> - **Toda columna con un dato ordena** ([OT1-C, 2026-11-22](#ordenar-por-columnas--2026-11-22)), desde su encabezado y desde
>   «Ordenar por», último en la barra. La flecha marca la columna; lo vacío va al final y un estado sigue el orden del circuito.
>   Una tabla paginada ordena en la base; una entera, en el navegador con la misma pieza.

`premisas-de-desarrollo.md`, «Tablas» (reemplaza la viñeta de `rowKey` y las columnas ordenables):

> - Declarar `rowKey` con la identidad estable de la fila. Toda columna declara `sortable`: con un dato, `true` y su
>   `sortKind`; Acciones, un ícono o varios valores, `false`. El `id` de una columna que ordena es el campo de orden del
>   backend. Una tabla paginada ordena con el servidor; una entera, con `useClientSort`. Conservar `aria-sort`.

`premisas-de-desarrollo.md`, «Filtros y búsqueda»:

> - «Ordenar por» (`SortMenu`) va último en la barra, separado de los filtros: no saca filas. «Limpiar todo» no lo toca, y
>   cambiar el orden vuelve a la página 1.
