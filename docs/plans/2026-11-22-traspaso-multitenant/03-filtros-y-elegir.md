# Traspaso 17 · 03 · Los filtros y elegir de una lista

Las dos cosas se resolvieron juntas, porque un filtro es elegir de una lista. Las fuentes son «Listados y filtros» y
«Elegir de una lista» de `ArquitecturaBaseFront/docs/design/visual-baseline.md`, el plan
`ArquitecturaBaseFront/docs/plans/2026-11-19-una-sola-pieza-para-elegir.md` y el de Información del negocio del 22/11
(`ArquitecturaBaseFront/docs/plans/2026-11-22-informacion-del-negocio-reordenar.md`), donde se decidió que todo lo que se
elige va en la pieza.

## 1. Los filtros

- **La barra:** el buscador siempre a la vista; al lado, hasta tres filtros frecuentes; el resto, en «Filtros», con la
  cantidad de filtros aplicados en el botón. Cada cambio se aplica enseguida, sin un botón «Aplicar», como «Columnas»
  (PL1-A, 2/11).
- **Un filtro de opciones es la pieza única** (PL2-A y FO1-A, 3/11; DS1-A, 19/11): en la barra, con su botón («Cualquier
  rol», «Estado: Todos»); adentro de «Filtros», como un campo. Cerrado dice «Todas» o la elegida. Abierto, «Todas» primero y
  separada, una opción por renglón, sin la cantidad ni texto debajo, con la elegida tildada, y ninguna se apaga por no traer
  resultados. Se elige una. Con muchas opciones, o si vienen del servidor (las de un catálogo, las personas), se busca en el
  buscador de adentro de la lista, no escribiendo en el campo.
- **Todo en desplegable** (22/11). Los segmentados con cantidades de los listados («Activos (8) · Dados de baja (2)») también
  pasan a un filtro con su botón en la barra, sin cantidades. Se está programando en la copia de la pieza única; todavía no
  está en la app.
- **Lo aplicado** se ve como chips que se quitan de a uno, con un solo «Limpiar todo» (que no toca el orden). Sin
  coincidencias, el vacío dice cuántos filtros hay y ofrece limpiarlos.
- **Las solapas** son vistas con su propia tarea, no filtros: en la planilla, «Por aprobar (N)», «Todas» y «Por resolver
  (N)». «Por aprobar» es una cola y abre sola si hay algo para decidir.
- **Filtrar por una pregunta** (la planilla): cada tipo con sus operadores (un texto contiene o es igual; un número o una
  fecha es igual, desde o hasta; una elección o un sí o no es igual; y para todas, respondida, sin responder o no se
  preguntó). Las preguntas de un bloque van bajo su nombre, con «En algún ítem de …»: todas las condiciones las cumple un mismo
  ítem, y el chip lo dice («Estado: Dañado (en algún ítem de Artículo)»).
- **Ordenar no es filtrar:** «Ordenar por» va último en la barra, separado, y no saca filas ([02](02-listados.md) §2).

**Lo que enseñó.** La planilla tenía muchos botones de filtro, «N respuestas» debajo de cada opción y una banda arriba; el
usuario preguntó por qué tantos botones y pidió algo simple (PL1-A). El filtro de una pregunta tenía una caja «Buscar
opciones», casillas, la cantidad debajo de cada opción y un desplegable «Respuesta» que nadie entendía; pidió «un combo
desplegable simple y listo» (PL2-A), y después lo mismo en toda la app (FO1-A). Las cantidades por opción, además, costaban
una consulta por cada dimensión.

## 2. Elegir de una lista: una sola pieza

Decidida el 19/11 (DS1-A y DS2-A) y extendida el 22/11 a todo lo que se elige. La pieza está programada en una copia del
front y su extensión del 22/11 se está programando ahí; se aplica al cerrar la prueba integral y todavía no está en la app.

- **Todo lo que se elige va en la pieza** (22/11, «en toda la app»): una o varias opciones; también de dos a cuatro opciones
  fijas (el idioma, «Filas por página», el proveedor, el estado de un día del horario), el Sí o No, los filtros y la carga de
  un formulario (el Sí o No y las opciones en fila, y elegir varias, que eran casillas en columnas). Elegir varios dentro de
  un diálogo que guarda (los formularios de una carpeta) es la pieza múltiple con «Guardar»: nunca una lista de casillas ni
  un modal que sólo sirve para elegir. Una casilla queda sólo para un sí o no suelto que se marca («Incluir las fotos»).
- **Quedan afuera** la vista previa que imita WhatsApp, que muestra lo que le llega a la persona; las pestañas, que llevan a
  otra vista; y los menús de acciones.
- **Cerrada,** un campo con su flecha fina que dice lo elegido, igual para una y para varias; sin nada, lo dice en gris
  («Sin zona»). En la barra de un listado, el filtro conserva su botón y abre la misma lista.
- **La lista:** el borde, la sombra y el renglón de los menús; la descripción de cada opción en gris, debajo del nombre; una
  opción apagada, con su motivo después del nombre; «Todas» o «Sin …» primero, separada por una línea; la elegida con su
  tilde a la izquierda y, en varias, una casilla, y marcar no cierra.
- **El buscador va adentro,** arriba de la lista y con el foco puesto, cuando hay más de diez opciones o vienen del servidor,
  con la cantidad debajo. Sin buscador, una letra lleva a la primera opción que empieza así.
- **Del servidor:** «Buscando…», «Ninguna opción coincide…», «Mostrar 50 más» y el error con «Reintentar»; con varias, las
  elegidas que no están en la página van primero.
- **Con un tope,** la lista dice «Hasta 5» y, al llegar, «Elegiste 5 de 5», y las demás se apagan.
- **Varias, por su nombre** (DS2-A): separadas por coma, en el orden de la lista; si no entran, las que entran y «y N más»
  («Soja 1ª y 2 más»). El campo nunca crece, y el nombre accesible las lleva a todas.
- **En el celular** la lista ocupa la pantalla, con el nombre del campo, la ×, el buscador si son más de diez y renglones de
  56 px; en varias, se aplica con «Listo (N)».
- **Con el teclado:** las flechas recorren, Enter elige (en varias marca sin cerrar), Escape cierra y devuelve el foco al
  campo, y Tab cierra y sigue.
- **La zona horaria:** la ciudad y, en gris, la hora de su país y el desfase («Montevideo · hora de Uruguay · GMT−3»), en una
  sola lista para toda la app; se guarda el identificador de IANA.
- Lo que llega en mayúsculas de un catálogo se muestra como llega.

**Lo que enseñó.** Había seis formas de elegir: el select del navegador en 60 lugares (la zona horaria eran unas 420 zonas
de IANA en crudo, armadas tres veces), un campo que se escribe en 14, un menú con tildes, el menú del botón de un filtro, el
select de la biblioteca de componentes y una lista propia en el celular. «Formularios de «Zona Norte»» era un buscador con
una casilla por formulario. En la carga, dos preguntas parecidas salían en fila o en una lista según el largo de sus
opciones.

**Quedan por ajustar al programar:** la lectura de una pregunta de elegir en la respuesta como el formulario (VF1-A decía
la elegida rellena y las demás en gris, que era la forma de las opciones en fila) y el ancho de «elegir varias» en la carga,
que ocupaba el renglón entero por sus casillas en columnas.

## 3. No repetir

- Un control nuevo para un caso nuevo, en lugar de la pieza que ya existe.
- La cantidad debajo de cada opción de un filtro, u opciones apagadas porque hoy no traen nada.
- Un modal o una lista de casillas sólo para elegir.
- Un segmentado o radios para opciones fijas.

## 4. Para el multitenant

- **Las cantidades por opción.** Tus filtros las llevan («con la cantidad por opción», tu `pantallas-y-ui.md`, con
  `filter-counts`). Acá no las lleva ningún filtro, tampoco los segmentados. La pregunta 7 del 15 sigue, con esto de
  contexto: la recomendada es sin cantidades.
- **«Más filtros»** va en un panel al costado en tu planilla (E15); acá, «Filtros» aplica cada cambio enseguida (la
  pregunta 8 del 15).
- **Las solapas:** tus reglas las prohíben para filtrar un listado; acá, las de la planilla son vistas con su tarea (la
  pregunta 24 del 15).
- **«Alguna de»:** tu filtro de una pregunta elige varias opciones; acá, una (la pregunta 25 del 15, que ahora busca adentro
  de la lista).
- **La pieza única.** Tenés `SearchSelect`, `MultiSelect`, `CountrySelect`, `TimeZoneSelect`, `CultureSelect`, el
  segmentado de `RadioGroupField` y los desplegables de `FilterBar`. Tu `CountrySelect` ya muestra el buscador con más de
  diez opciones, que es la regla de DS1-A. Tu carga (E20) arranca el Sí o No sin nada marcado, pone hasta cinco opciones
  cortas en fila y elegir varias en el renglón entero; con la regla del 22/11, todo eso va en la pieza, y el Sí o No sigue
  arrancando vacío. Es la pregunta 74, que reemplaza a la 71 del 16.
- **Las varias elegidas al leer:** tu planilla las separa con coma, en el orden elegido, como acá. Tu detalle pone una por
  renglón; con la respuesta como el formulario ([04](04-la-respuesta.md) §2), se leen en su control.
