# Traspaso 17 · 02 · Los listados: la tabla, el orden y las acciones

Vale para todo listado de un ABM: catálogos, formularios, la planilla de respuestas, documentos, personas, usuarios y roles.
Las fuentes son «Listados y filtros», «Íconos y acciones» y «Un dato por columna» de
`ArquitecturaBaseFront/docs/design/visual-baseline.md`, «Tablas» de `ArquitecturaBaseFront/docs/guides/premisas-de-desarrollo.md`
y el plan `docs/plans/2026-11-22-ordenar-tablas-por-columnas.md`. Los filtros están en el [03](03-filtros-y-elegir.md).

## 1. La tabla

- **Una colección es una tabla** (`DataTable` con `Pagination`) en su superficie, con la barra de filtros arriba y aparte.
- **Un dato por columna y nada debajo de otro dato** (UD, 9/11; la regla del 22/11). Cada columna muestra un solo dato y su
  encabezado lo nombra.
  - Cuentan como un dato la fecha y la hora de un mismo momento, un valor con su unidad, varios valores de un mismo dato (los
    roles de una persona, las opciones elegidas) y un dato con su enlace.
  - No van juntos un número con su fecha y su estado, «fecha · persona», un estado con su versión («Publicado · versión 3»),
    una etiqueta junto al dato («Corregida» y «Sin verificar» tienen su columna) ni el resumen de otra tabla: el bloque de
    ítems dice «3 ítems» y los documentos, «2 documentos».
  - Un encabezado nombra una sola cosa: «(desde v3)» salió del encabezado de la planilla y quedó en «Columnas».
  - Una columna que sólo tiene sentido en una vista aparece sólo ahí: «Estado» en la planilla si alguna regla pide
    aprobación, y «Reglas» cuando alguna fila está «Por resolver».
  - Siguen el punto de estado delante del correo en Usuarios y las listas que no son tablas, que siguen la regla de las
    lecturas. La excepción del texto secundario en gris debajo del dato (A) queda en revisión con el barrido de «nada
    debajo de otro dato» (tarea 7 del plan de la respuesta como el formulario).
- **Líneas entre columnas y números a la derecha** (A, 2/11). Una línea fina entre columnas, también en el encabezado. Las
  columnas de números (cantidades, medidas, importes, el número de fila) van a la derecha, con su encabezado. La tabla cierra
  con la línea de su última fila. Un código con guiones («30-71458236-9») no se parte en dos renglones.
- **Filas alternadas** y el estado con su palabra, nunca sólo con un color.
- **Lo vacío:** «—» cuando no hay dato. En la planilla, «—» dice que la pregunta no existía en esa versión y «Sin respuesta»,
  que se dejó vacía; el cero y el «No» se muestran.
- **Ancha:** se desplaza de costado dentro de su caja, nunca la página. En el celular, menos columnas o una tarjeta por fila,
  con cada dato nombrado.
- **Fechas y números** con las piezas comunes: fechas sólo con números (`03/11/2026`; con hora, después de una coma) y
  números con la región del idioma (es-AR `1.234,5`, en-US `1,234.5`). Un código, una CUIT o un teléfono van como texto.

## 2. El orden (OT1-C, 22/11)

Lo pidió el usuario mirando la lista «Formularios»: que todas las tablas se ordenen por sus columnas, desde la columna o
desde un botón «Ordenar por», y en la base. El backend de la regla común, de Formularios, de Usuarios y de Roles está
programado (`366bedb7` y `21ffbfdb`); las demás listas y el front van después del cierre de la prueba y de la pieza única.

1. **Toda columna con un dato ordena.** No ordenan Acciones, las de un ícono ni las de varios valores (los roles de una
   persona, las preguntas de varias opciones, de un bloque o de ubicación).
2. **Desde dos lugares que mueven el mismo orden.** El encabezado: cada columna que ordena es un botón con su texto; la que
   ordena muestra su flecha (↑ o ↓, en el color de la marca) después del título, o antes en una columna de números; las
   demás muestran ↕ al pasar el mouse o con el foco; 44 px para tocar. Y «Ordenar por», último en la barra y separado de los
   filtros porque no saca filas: dice el orden con palabras («Recibida, más reciente primero») y abre la lista de la pieza
   única con «Columna» y «Orden». En el celular el botón ocupa el ancho y su lista, la pantalla.
3. **Dónde:** una tabla paginada ordena en la base, con su desempate; ordenar sólo la página visible estaría mal. Una tabla
   que llega entera ordena en el navegador con la misma pieza, y se ve igual.
4. **Con los filtros:** el orden se suma a la búsqueda, las solapas, los filtros y la carpeta. Cambiarlo vuelve a la página
   1 y «Limpiar todo» lo conserva. Va en la dirección (`sort=campo` o `sort=-campo`); una columna dinámica suma su id.
5. **Lo vacío al final**, en los dos sentidos. Con el mismo valor, las filas siguen el orden de siempre de esa lista y
   después su clave única.
6. **Un estado o un tipo ordena por el orden del circuito** (Recibida, Por aprobar, Aprobada, Rechazada, Anulada), igual en
   los dos idiomas; nunca por el texto guardado ni por el traducido.
7. **El primer toque ordena en el sentido útil:** el texto de la A a la Z, los números de mayor a menor, las fechas de la más
   reciente a la más vieja, un sí o no con el «Sí» primero y un estado por el circuito. El segundo lo invierte.
8. **Sin orden elegido** cada lista abre como siempre. Si ese orden es una columna, su flecha se ve; si es compuesto, no hay
   flecha y el botón dice «Ordenar por».
9. **No cambian las tablas cuyo orden es un dato:** las opciones de un catálogo (su «Ordenar por…» guarda el orden, [05](05-editores-y-dialogos.md)),
   los renglones de una respuesta, las comparaciones y las vistas previas de una importación. Tampoco «Por aprobar», que es
   una cola: va de la más vieja a la más nueva.

Lo que enseñó: el encabezado ordenable no mostraba ninguna flecha, así que nadie sabía qué columnas ordenaban; y en
PostgreSQL un orden descendente deja los vacíos primero.

## 3. El paginado y la búsqueda

- La búsqueda de texto libre está siempre a la vista. Buscar, filtrar, ordenar y paginar lo hace el servidor: el front nunca
  simula resultados sobre una página parcial.
- Página, tamaño, orden, búsqueda y filtros viajan en la dirección, con los nombres del backend; Atrás y un enlace compartido
  restauran la vista. Lo privado de la planilla (textos, personas, condiciones) va en el cuerpo del pedido y dura en memoria.
- Cambiar la búsqueda o un filtro vuelve a la página 1. Con cero resultados el paginado dice «0–0 de 0», sin «página 1 de 0».
- **Dos vacíos distintos:** «todavía no hay», que ofrece crear si hay permiso, y «ninguno coincide», que dice cuántos filtros
  hay y ofrece limpiarlos.

## 4. Las acciones

- **En el encabezado de la pantalla** (AC1-A): a la vista, con texto, la tarea (crear, agregar, exportar la planilla,
  aprobar o rechazar lo que espera una decisión), con la principal última. Ir a otro lado y bajar, como íconos. Lo demás que
  se hace con el objeto, en ⋮, con lo destructivo al final, en rojo y después de una línea.
- **En una fila, sólo íconos** (20/11): lo que se hace con la fila (cargar, revisar, editar, restaurar, reactivar, descartar)
  es un ícono con su nombre en el tooltip y el dato de la fila en el nombre accesible («Eliminar a Ana Pérez»), en la columna
  «Acciones», al borde derecho. Ir a otro lado es un enlace. Una acción que tarda cambia su dibujo por el aro. Lo que es un
  dato de la fila (el nombre que la abre, el número de la última respuesta) sigue siendo texto. En el celular, la tarjeta
  puede llevar su botón con texto a todo el ancho.
- Las inocuas primero y la destructiva al final, con el rojo al interactuar. Sin permiso, la acción no se dibuja.

## 5. Las esperas y los estados

- La primera carga muestra el aro dentro de la tabla, con el alto de ocho renglones; volver a leer atenúa las filas con una
  línea que corre bajo el encabezado (ES1-A y LC2-A). Nada salta al llegar los datos.
- Un error dice qué pasó y ofrece «Reintentar»: nunca se muestra como una lista vacía. Sin permiso, el aviso de siempre. La
  espera de un 429 queda como motivo del botón que la provocó.

## 6. No repetir

- Juntar dos datos en una celda porque entran, o poner uno debajo del otro.
- Una etiqueta pegada al dato («Corregida») en lugar de su columna.
- Un encabezado que ordena sin ninguna señal, o un orden armado sobre la página visible.
- Botones con texto en cada fila.
- Contar o filtrar sobre lo que se ve en vez de pedírselo al servidor.

## 7. Para el multitenant

- **Coincide.** Tu «cada celda lleva un solo dato, en un renglón» (`docs/rules/pantallas-y-ui.md`, 25/10) es esta regla, y
  más estricta: no tiene la excepción del texto en gris debajo, que acá queda en revisión. Con «nada debajo de otro dato»,
  tu regla es la que vale (la pregunta 13 del 15 se inclina hacia ella). También coinciden los números a la derecha, la raya
  para lo vacío, la búsqueda y el orden en el servidor y la dirección compartible, con la misma excepción de lo privado de
  la planilla (tu E15).
- **Las acciones de fila** van en tu ⋮; acá, íconos a la vista. Es la pregunta 70 del 16, que sigue.
- **El orden.** Tus columnas ordenan con `sortable` y su flecha, pero tus reglas no dicen lo vacío al final, el desempate,
  el estado por el circuito, el sentido útil del primer toque ni «Ordenar por». Tu planilla ordena por N°, Recibida y cada
  pregunta salvo «elegir varias»; tus catálogos, por nombre o fecha. Es la pregunta 76.
- **El celular.** Tu `mobileRow` pone una línea de datos debajo del nombre, como «N respuestas · estado» en la lista de tu
  E15: son dos datos unidos con «·» y debajo de otro. Acá, una tarjeta con cada dato nombrado, como los ítems del bloque.
- **La espera de un botón** (apagado con su texto, sin «Guardando…», en tu regla) sigue siendo la pregunta 5 del 15.
