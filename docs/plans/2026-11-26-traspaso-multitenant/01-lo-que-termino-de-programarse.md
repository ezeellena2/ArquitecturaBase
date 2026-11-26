# Traspaso 19 · 01 · Lo que terminó de programarse del diseño

El [18](../2026-11-23-traspaso-multitenant/02-lo-programado-del-diseno.md) dejó el diseño del 17 a medias: el orden de las
tablas sin front, la pieza única sin su pasada final, la respuesta como formulario sin cerrar, y Formularios en una sola
entrada e Información del negocio sin programar. Del 23 al 26/11 se terminó todo eso y se sumaron dos decisiones nuevas: la
falta de permiso dentro de la app (SP1-B) y las tarjetas con datos del chat (TC1-B a TC5-A). Las reglas del 17 siguen
valiendo; acá va lo que cambió al programarlas.

## 1. Ordenar las tablas (OT1-C): el front completo

El backend estaba completo desde el 22/11. El front entró en cortes, de F1 a F14 (front `05da1d16` a `7db3ec78`, y las capturas
en `a314e255`):

- **La pieza** (`05da1d16`). Cada columna de la tabla declara si ordena y, si ordena, su tipo (texto, número, fecha, sí o no,
  estado). Una columna que ordena sin tipo no compila. La que ordena muestra la flecha de la marca después del título, y las
  demás que ordenan, el doble sentido. El primer toque ordena en el sentido útil (números y fechas de mayor a menor, «Sí»
  primero, texto y estado ascendentes) y el segundo lo invierte. «Ordenar por» va al final de la barra, dice el orden con
  palabras, aplica cada elección enseguida y en el celular se abre a pantalla completa.
- **Las listas que llegan enteras** (la Biblioteca y los documentos de una propuesta) ordenan en el navegador con la misma regla
  que el servidor: lo vacío al final en los dos sentidos y el orden estable.
- **Cada lista**: Formularios, Usuarios, Roles, Catálogos, Diseñar formularios, la planilla, Documentos recibidos,
  Formularios que faltan, Datos de personas, la Biblioteca y los documentos de una propuesta. La planilla ordena también por
  cada pregunta según su tipo, por el campo del catálogo de una pregunta y por cada resultado, y lleva ese orden a la
  navegación anterior y siguiente, a la pantalla completa y al Excel (`4d43a122`; en el backend, `e798f906`).
- **Lo que no ordena, y por qué:** varios valores (los roles, las preguntas de varias opciones, los bloques, la ubicación),
  lo que da el canal de WhatsApp (la deprecación, el estado de los dados de baja), lo cifrado (los datos leídos de un documento)
  y «Por aprobar» cuando es una cola. Un orden que el servidor no conoce queda afuera del pedido.
- **Tres arreglos que vale la pena no repetir:** el primer toque parte del orden que se ve, no del que dice la dirección
  (`3034e6b4`); el encabezado que ordena mide lo mismo que los demás y se toca en toda la celda (`7ecea6ee`); y pasar una tabla
  a la que ordena no puede perder la región con nombre que se recorre con el teclado (`b6430490`).
- **«Por aprobar» de FU2-A** ordena en el servidor, con lo que más espera primero, sólo para quien decide (`638dfb6d`, back
  `3639fad4`).

La regla quedó en el fundamento visual del front y en el backend («Orden de un listado»); la procedencia del plan, en el
[historial](../../history/README.md#cierre-del-2026-11-26).

## 2. La pieza única para elegir: la pasada final

- **La regla del foco (IM3-A).** Al cerrar la lista, el foco vuelve al botón de la pieza, salvo que la elección lo haya llevado
  a otro lado (el campo que mostró, una confirmación que abrió). Lo que contiene al botón, como el diálogo que cambia con la
  elección, no cuenta como otro lado (front `0e189191` y `8db195cd`).
- **Se retiraron las variantes sin uso** del segmentado y de los radios (`ecfab53e`), y las guías de Configuración, IA,
  carpetas y casos nombran la pieza (`27f4713e`).
- **«Ingresar con» es una excepción:** el 24/11 el usuario pidió volver a la elección con subrayado de antes en Iniciar sesión
  y Crear cuenta (`cf60c593`, con la excepción escrita en `3ec57019`). Lo contó el 18 en su 02 §2. Se suma a las de antes: el
  select del navegador en la vista previa que imita WhatsApp y el autocompletar del buscador de lugares.
- **El idioma del pie** de la portada, el ingreso y el registro pasó de dos botones a la pieza (O50, `57528c38`).
- **Detalles:** «Mandar formulario» pide de a 20, como las demás listas que buscan en la pieza (`e5044cbb`); una subcarpeta del
  filtro «Carpeta» se nombra «Norte, en Repartos» para el lector de pantalla, con la sangría en pantalla (O57, `b14784b3`).

## 3. La respuesta como el formulario (VF1-A y VF2-A): cerrada

- **El detalle trae siempre la forma de su versión:** `entry` dejó de ser opcional en el contrato, con los ejemplos de una
  misma corrida (front `f569d3e2` y `0546084e`).
- **Lo respondido se lee en el color del texto** también en un texto largo, y el visor de una pregunta de fotos recorre sólo
  las suyas (`563f716f`).
- **La corrección que encuentra la respuesta cambiada** (O52): si mientras tanto otra persona la corrigió, la decidieron o la
  anularon, la pantalla vuelve a la lectura con «Esta respuesta ya no se puede corregir.» arriba y «Ver la actual», que abre
  la versión que la reemplazó (`8d9bc33d` y `2c15cdb1`). Un 403 o un 404 al guardar hacen lo mismo.
- **El orden de la actividad** (O56): lo que se hizo al recibir la respuesta nunca va antes de ella, aunque la acción tome la
  hora en que empezó el guardado y la respuesta la del final (`c7923674`, renombrado en `5c086c02`).

La regla vive en [formularios](../../features/formularios.md#la-respuesta-como-el-formulario--vf1-a-y-vf2-a-2026-11-22).

## 4. Formularios en una sola entrada (FU1-B y FU2-A): programada

Back `3639fad4` y front `f6f22fc6`, con sus escenas en `c0ccf749`, como lo eligió el usuario el 23/11 (el 18, 02 §5):

- El panel de «Gestión de formularios» deja una sola entrada «Formularios», y una línea separa lo de todos los días
  (Formularios y Documentos recibidos) de Catálogos, Biblioteca y Formularios que faltan.
- Quien diseña ve la lista de diseño con «Cargar» primero en cada fila publicada, la columna «Por aprobar» y el número de
  respuestas que abre la planilla. Quien no diseña ve la lista de Formularios de siempre.
- «Con algo por aprobar» pasó al desplegable de estado, y `/formularios/respuestas` lleva a `/formularios` con ese filtro.
- En el backend, cada fila de la lista de diseño suma `pendingDecisionCount` (sin el permiso de decidir no viene), con el mismo
  predicado que Formularios en un solo lugar (`FormPendingDecisionQuery`). El filtro `Pending` sin ese permiso da 403.

## 5. Información del negocio (IN1-C, IN2 e IN3): programada y probada a mano

Front `17bca40a`, `1f3c6db7` y `3c984eca`, con lo que decidió el usuario el 22/11 (el 18, 02 §4): la semana en una tabla (Día ·
Horario · lápiz) y las fechas especiales en otra, un día se edita en el mismo diálogo que una fecha, con la atención en la pieza,
y desde la compu ancha Ubicación, Servicios y Preguntas a la izquierda y el horario a la derecha. A 320 px, la franja de un
renglón baja entre sus dos horas. Se probó a mano el 24/11, también sin permiso y en sólo lectura.

## 6. La falta de permiso, dentro de la app (SP1-B): nueva

Nació de O58 (`/sin-permiso` mostraba «No tenés permiso» sin el menú) y el usuario la eligió el 24/11 en el lienzo «Sin
permiso». Front `a3bcdcda`, probada a mano el mismo día:

- La pantalla sin permiso se muestra **en su propia dirección y dentro de la app**, con el título de la pantalla (el nombre de
  su entrada del menú, y su «volver» si es hija) y el vacío estándar con «Volver al inicio». En Inicio, sin botón. La ruta
  `/sin-permiso` se fue.
- **Un 403 al leer** muestra esa pieza en su lugar; **un 403 al guardar o probar algo** hace que la pantalla vuelva a leer lo
  que está a la vista: queda en sólo lectura o sin permiso.
- La 404 también entra al caparazón. Los vacíos embebidos (el filtro de opciones de Respuestas, la vista rápida y «Editar
  usuario») contestan con la misma pieza.
- Una regla de arquitectura deja la pieza sólo en pantallas y en la ruta protegida.

## 7. Las tarjetas con datos del chat (TC1-B a TC5-A): nuevas

Propuestas el 24/11 en el lienzo «Tarjetas del chat» y elegidas las recomendadas el 25/11 (front `b3d3826c`, con la revisión
de las escenas en `7394d803` y `98a23bd2`):

- **TC5-A, en toda la app:** los datos de etiqueta y valor (`DataList`, 28 lugares) van en su recuadro, como una tabla de dos
  columnas con el dato en una columna teñida. Cuando la lista es el cuerpo de un marco (una tarjeta, una zona con banda o una
  celda), va de borde a borde, sin otro recuadro adentro (`1b3f90f1`, escenas en `3b177936`).
- **Lo que enseñó la revisión de las escenas:** adentro de algo que se ajusta a su contenido, las columnas en porcentaje se
  encogen y parten el dato; van en fracciones y comparten las columnas (subgrid), y la columna del dato nunca mide menos que su
  palabra más larga.
- **TC2-B:** la tarjeta de una propuesta pierde el resumen gris y suma un pie del tono de la banda. **TC1-B:** en un catálogo o
  una importación, la muestra nombra cada columna con el dato del catálogo y, debajo, la columna de la planilla, y el pie dice
  cuántas filas muestra. **TC3-A:** las preguntas de un formulario, en una tabla, que en el celular se desplaza adentro de la
  tarjeta. **TC4-A:** cada fuente de una respuesta, en la misma tarjeta.

## 8. Para el multitenant

Leído en tu `main` del 26/11 (`b2334284`). Tu módulo de formularios todavía no tiene código, así que esto va a tus specs, tus
reglas y tus tableros.

- **El orden de las tablas:** con el front cerrado, la pregunta 76 del 17 queda completa. Si el usuario la elige, tu ficha de
  paginado y tus tablas suman lo de §1: el tipo obligatorio de cada columna que ordena, el primer toque en el sentido útil,
  «Ordenar por» al final de la barra y las listas enteras ordenadas en el navegador con la misma regla. No choca con tu
  decisión 224, que es otra cosa: «Ordenar por…» de las opciones de un catálogo se aplica al guardar y no se toca en el
  encabezado.
- **La pieza única:** la pregunta 74 del 17 suma la regla del foco y sus excepciones (§2). Ver el [05](05-preguntas.md).
- **La respuesta:** la pregunta 77 del 17 suma O52 y O56 (§3), que valen para tu E15 y tu E21 aunque corrijas en otra pantalla.
- **Formularios en una sola entrada:** tu decisión 79 ya tiene una sola lista. De FU2-A te sirven la columna «Por aprobar» con
  su predicado único y su orden sólo para quien decide, y «Cargar» como primer ícono de cada fila.
- **La falta de permiso:** tus contratos todavía tienen la ruta `/sin-permiso` (`docs/contracts/frontend-routes.json`). Es la
  pregunta 85.
- **Las tarjetas y `DataList`:** tu asistente de la web y tu E23 dibujan tarjetas en el chat. Es la pregunta 86.
- **Información del negocio** es de esta app. Si dibujás un horario por día, la tabla con un diálogo por día es lo que eligió
  el usuario acá.
