# Traspaso 17 · 01 · Los principios y lo que aprendimos

Las pantallas de catálogos y formularios de ArquitecturaBase se corrigieron muchas veces entre el 23/10 y el 22/11. Casi
todas las correcciones salieron de lo mismo: el usuario miraba una pantalla real, con datos reales, y no se entendía. Este
archivo junta lo que se repitió en principios, cada uno con el error que lo enseñó, y cierra con cómo se trabaja un diseño
para no repetirlos. El detalle de cada pantalla está del 02 al 05.

## 1. Los principios

### 1.1. El contenido es del autor; la forma, de la plataforma

Quien arma un formulario o un catálogo decide qué preguntas o campos tiene y en qué orden. Cómo se ven lo decide la app,
por el tipo de cada dato: el ancho y el control al cargar, la columna en la planilla, cómo se lee, cómo va al Excel y al
PDF. El autor nunca elige anchos, estilos ni controles.

- **Lo que enseñó (AR1-A, 2/11).** Mirando un formulario largo, el usuario preguntó si había que estandarizar los
  formularios, porque como son dinámicos sentía que no iban a quedar todos bien. Quedaban renglones a medias, casillas
  apiladas en una columna alta y dos preguntas parecidas que salían en fila o en una lista según el largo de sus opciones.
  La respuesta fue ajustar la regla de la forma, no darle opciones al autor.

### 1.2. Una cosa se hace de una sola manera, en toda la app

Lo mismo se ve y se maneja igual en todas las pantallas, con la misma pieza. Un cambio que el usuario pide mirando una
pantalla vale para todas las que tienen lo mismo (RF3).

- **Lo que enseñó.** Había seis formas de elegir de una lista, cada una puesta cuando apareció su caso; el usuario lo vio en
  «Editar usuario», donde Roles y «Campo asignado» se manejaban distinto (DS1-A, 19/11). Había unas 120 formas de explicar un
  control apagado (MA1-A, 9/11) y unas 240 leyendas distintas (31/10). Cada regla nueva se aplicaba a la pantalla donde se
  pidió y las demás seguían igual, hasta que el usuario preguntó por qué las premisas no se aplicaban solas.

### 1.3. Un dato, un lugar: un dato por columna y nada debajo de otro dato

Cada columna muestra un solo dato y su encabezado lo nombra. Ningún dato va debajo de otro: si importa, tiene su columna o
su campo; si no, no va.

- **Lo que enseñó.** «Última respuesta» juntaba en una celda el número, la fecha y el estado; había 16 columnas así en ocho
  pantallas (UD, 9/11). El bloque de ítems mostraba «3 · GLIFOSATO 64%, …» (se cambió por «3 ítems»). En el detalle de una
  respuesta, los datos de la opción de un catálogo (tipo, rubro) iban todos juntos debajo del valor, y la actividad ponía
  quién, cuándo y el detalle uno debajo del otro. El 22/11 el usuario lo pidió en mayúsculas, para toda la app: nada abajo de
  otro dato; o se agregan columnas, o campos con su desplegable, o no se ponen.

### 1.4. Una respuesta se lee como se carga

El detalle de una respuesta es el mismo formulario de carga, con lo respondido adentro y sin poder tocarlo. Corregir
destraba esos mismos controles en el lugar.

- **Lo que enseñó (VF1-A, 22/11).** El detalle era una lectura especial en dos columnas, con tablas chicas debajo de cada
  valor. El usuario preguntó por qué no se ve como un formulario común, si es básicamente un ABM, y por qué se le meten
  diseños raros.

### 1.5. Cada cosa en su forma: tabla, pasos o lado a lado

- Lo que pasó en el tiempo (la actividad de una respuesta) es una tabla, un renglón por cosa que pasó y un dato por columna
  (VF2-A, 22/11).
- Lo que pasa en orden (una regla, una sugerencia) se lee en pasos (LR1-A).
- Dos versiones de lo mismo (un conflicto de revisión, los cambios de una importación) van lado a lado, con sólo lo
  distinto (LR2-A).
- Los datos sueltos de algo, en dos columnas, rótulo y valor (`DataList`), nunca «Rótulo: valor» corrido en un renglón.
- **Lo que enseñó.** La actividad estaba «desparramada», con etiquetas pegadas a cualquier cosa (TL3-A, 2/11), y después en
  cuatro columnas con el detalle debajo, que tampoco se entendía (VF2-A). Las reglas se leían con rótulos chicos pegados a
  frases (LR1-A, 3/11), y las comparaciones como «Zona: Norte → Centro» (LR2-A).

### 1.6. Sin leyendas; lo apagado dice su motivo sobre el control

Debajo o al costado de un control, de una sección o de un título no va un texto que explique cómo funciona: la ayuda que
sirve va en la (i), con su tooltip. Un control apagado no lleva nada al lado; si el motivo no es obvio, aparece sobre el
mismo control, al pasar el mouse, con el foco, con un clic o con un toque. Los botones no cambian de lugar ni de forma.

- **Lo que enseñó.** Las 240 leyendas de unas 50 pantallas (31/10); «No hay cambios para guardar» y botones que se movían
  (23/10); las 120 formas de explicar lo apagado, algunas que no se podían leer con el dedo ni con el teclado (MA1-A).

### 1.7. Lo elegido sin dudas; toda espera a la vista; lo que carga no mueve nada

- Lo elegido se distingue por algo más que un tinte: un relleno, una tilde, un punto, una barra (OE1-A). Al usuario le
  costaba saber si estaba elegido «Sí» o «No».
- Toda acción que tarda muestra el aro y su gerundio en el botón («Guardando…»), sin atenuarlo; un listado que vuelve a leer
  atenúa sus filas con una línea que corre bajo el encabezado (LC1-A y LC2-A).
- Lo que carga ocupa el lugar y la medida de lo que va a llegar (ES1-A a ES3): nada salta al llegar los datos.

### 1.8. A la vista, la tarea; lo demás, en ⋮; en una fila, sólo íconos

En el encabezado de una pantalla va a la vista, con texto, su tarea (guardar, publicar, crear, aprobar) y lo que lleva a
otro lado; lo demás que se hace con el objeto va en ⋮, con lo destructivo al final y en rojo (AC1-A). En una fila de tabla,
nunca un botón con texto: íconos con su nombre en el tooltip (20/11).

- **Lo que enseñó.** «Corregir» con texto y borde quedaba suelto en la vista rápida («para eso están los tres puntitos»,
  AC1-A); «Cargar» y «Revisar» eran botones con texto en cada fila. Y al revés: en la respuesta, que es un ABM, «Corregir» es
  la tarea y vuelve a la vista (VF1-A).

### 1.9. Lo que se busca, donde se busca

Las cosas van donde la persona las espera, aunque el diseño alternativo parezca más ordenado.

- **Lo que enseñó.** El usuario creó una carpeta y la buscó en el menú lateral, no en un panel al costado del listado: las
  carpetas pasaron a ser menús (CM1, 20/11). No reconoció la matriz de permisos de un rol y esperaba áreas plegables (RO3-D,
  19/11). En el encabezado de una tabla no se veía qué columnas ordenaban, porque no tenían flecha (OT1-C, 22/11).

### 1.10. Un estado, un texto: una vez y ciertos

Lo que dice la etiqueta del encabezado no se repite debajo (EP1-A). Cada texto sigue siendo cierto sin datos, con filtros,
sin permiso y después de un error. Un texto nombra su idioma sólo cuando no es el del menú (NI1-A). Las fechas van sólo con
números y los números, con la región de su idioma.

## 2. Cómo se trabaja un diseño para no repetirlo

- **Primero el tablero, con la pantalla entera.** Toda pantalla, diálogo o cambio se dibuja entero, con sus estados (carga,
  vacíos, error, sin permiso, conflicto, 320 px, inglés) y con las piezas de la biblioteca, antes de programar. Sumar un
  control a una pantalla que ya existe pide redibujar la pantalla entera (RF1).
- **La lista de chequeo dos veces:** antes de mostrar el tablero y antes de programarlo (§3). Al programar, la pantalla se
  compara con su tablero elemento por elemento.
- **Desde las capturas reales.** Los tableros copian las medidas y las piezas de la app de verdad, no un dibujo a mano que
  después no coincide.
- **Con datos de verdad:** 331 opciones en un catálogo, textos largos, nombres en inglés, celdas vacías, varias versiones de
  un formulario, un bloque con muchos ítems. Lo que se ve bien con tres filas de ejemplo suele romperse con datos reales.
- **Un cambio pedido sobre una captura vale para toda la app** (RF3): se busca dónde más pasa lo mismo antes de cerrarlo.
- **La prueba con el usuario encuentra lo que el tablero esconde.** Casi todas estas reglas salieron de mirar pantallas
  reales con el usuario: la prueba completa del 31/10, las revisiones de noviembre y la integral del 19 al 22/11.

## 3. La lista de chequeo

Es la de `ArquitecturaBaseFront/docs/design/visual-baseline.md` con las reglas del 22/11 adentro. Cada tablero y cada
pantalla de un ABM la pasa entera; lo que no pasa se corrige antes de mostrarlo, o se muestra como una pregunta.

1. **Entero y armado:** ¿muestra la pantalla entera, con sus estados y su recorrido, armada con las piezas de la biblioteca?
   ¿Es un diálogo sólo si es breve y contextual?
2. **Una tarea, zonas que se entienden solas:** ¿cada zona es una cosa, en el orden en que se usa, sin partirla por tipo de
   control?
3. **El control según el dato:** ¿una colección va en una tabla; todo lo que se elige (una o varias opciones, también de dos
   a cuatro fijas, un sí o no, un filtro) va en la pieza única; y una casilla, sólo para un sí o no suelto que se marca?
4. **Lo que se lee, en su lugar:** ¿nada va debajo de otro dato? ¿Una respuesta se lee con la forma de su formulario, lo que
   pasó en el tiempo en una tabla fila por fila, lo que pasa en orden en pasos y dos versiones lado a lado?
5. **Un dato por columna, que ordena:** ¿cada columna muestra un solo dato? ¿Toda columna con un dato ordena desde su
   encabezado y desde «Ordenar por», salvo Acciones, un ícono o varios valores?
6. **Sin leyendas:** ¿ningún texto explica cómo funciona algo debajo o al costado? ¿La ayuda está en la (i)?
7. **Textos ciertos, una sola vez:** ¿cada texto es cierto sin datos, con filtros, sin permiso y después de un error, y nada
   se dice dos veces?
8. **Botones y lo apagado:** ¿cada botón nombra su acción, la principal va última y lo destructivo, separado? ¿Lo apagado da
   su motivo sobre el control? ¿A la vista va la tarea y lo demás en ⋮? ¿Las filas, sólo con íconos?
9. **Lo elegido y la espera:** ¿lo elegido se distingue por algo más que un tinte? ¿Toda espera muestra el aro y su gerundio,
   y lo que carga ocupa su lugar sin mover nada?
10. **Estados y mensajes:** ¿están la carga, los dos vacíos («todavía no hay» y «ninguno coincide»), el error con reintento,
    la falta de permiso, la espera de un 429 y el conflicto? ¿El resultado va en el aviso, la validación junto al campo y la
    decisión en una confirmación?
11. **Lo mismo, igual en toda la app:** ¿lo que ya tiene forma en otra pantalla usa la misma pieza? ¿Fechas sólo con
    números y números con la región del idioma?
12. **Celular, teclado e idiomas:** ¿funciona desde 320 px sin desplazar la página de costado, con 44 px para tocar, con
    teclado y lector de pantalla, en español y en inglés? ¿Una colección guarda su estado (búsqueda, filtros, orden, página)
    en la dirección?

## 4. Para el multitenant

- **Los principios valen tal cual,** como reglas de plataforma (la tanda 1 del 15). Tus tableros siguen siendo tuyos (tu
  decisión 108): lo que se toma son las reglas, no los dibujos.
- **Ya tenés varios:** «cada celda lleva un solo dato, en un renglón» (§1.3, y más estricta que la de acá), sin textos que
  expliquen y el motivo de lo apagado en su tooltip (§1.6), el estado con su palabra y las fechas con números (§1.10; la coma
  antes de la hora es la pregunta 12 del 15).
- **No tenés escritos:** que la forma sale del tipo de cada dato en la lectura, la planilla y el PDF, y no sólo en la carga de
  tu E20 (§1.1); una sola pieza por cosa, porque hoy tenés varias para elegir (§1.2); la respuesta que se lee como el
  formulario (§1.4); y lo que se busca, donde se busca (§1.9).
- **La lista de chequeo del §3** conviene sumarla como paso de tu Diseño, antes de mostrar un tablero y antes de programarlo.
  Tus tableros de la E15 están «A revisar», los de la E20 sin dibujar y varios de la E21, «Por dibujar»: es el momento de
  pasarla, antes de dibujarlos.
