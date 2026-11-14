# 05 · Diseño: reglas para toda la app y Roles

Las reglas viven en `ArquitecturaBaseFront/docs/design/visual-baseline.md`; acá van resumidas, con la sección donde leer
cada una. Valen también para los tableros: un tablero dibuja lo que va a mostrar la app, con las mismas fechas, números y
piezas. Esqueletos de carga (ES1-A a ES3) y el estado una vez (EP1-A) ya los contó el traspaso 10.

## 1. La lista de chequeo de diseño (premisa, 2026-11-09)

- **Qué es.** Doce preguntas que todo tablero, pantalla o diálogo nuevo pasa antes de mostrarse para elegir y otra vez
  antes de programarse. No suma reglas: junta las que ya existían. Cada diferencia aceptada se anota en la nota del
  tablero, con su motivo. Vive en `visual-baseline.md`, «Lista de chequeo de diseño», y es una fila de las premisas del
  backend (`docs/architecture/premisas-de-la-plantilla.md`).
- **Las doce, en corto:**
  1. entera y armada con piezas de la biblioteca; un diálogo sólo si es breve y contextual;
  2. una tarea por pantalla, cada zona es una cosa y van en el orden en que se usan;
  3. el control según el dato: tabla para una colección, selector para un catálogo, radios o segmentado para pocas
     opciones, casillas para varias, desplegable simple para filtros;
  4. lo que se lee, en su pieza: `DataList` para datos, `StepList` para pasos, `ComparisonTable` para dos versiones;
     nunca «Rótulo: valor» corrido;
  5. un dato por columna;
  6. sin leyendas: lo que ayuda va en la (i);
  7. textos ciertos y dichos una vez;
  8. botones y lo apagado: Cancelar junto a la principal, que va última; la destructiva, separada a la izquierda; qué va
     a la vista y qué en ⋮;
  9. lo elegido se distingue con algo más que un tinte; toda espera muestra el aro y su gerundio;
  10. estados completos: carga, los dos vacíos, error con reintento, sin permiso, la espera de un 429 y el conflicto;
  11. lo mismo, igual en toda la app: chips de filtros, tokens, cinco niveles de letra, espacios en `rem`, fechas y
      números por región;
  12. celular, teclado e idiomas: desde 320 px, 44 px para tocar, lector de pantalla, es/en y el estado de una
      colección en la URL.
- **RF1 a RF3 (2026-11-11).** RF1: sumar un control, un dato o un estado a algo que ya existe obliga a dibujar la pantalla
  entera otra vez y pasar la lista. RF2: lo que un diálogo dice que va a hacer (cuántas, con qué filtros, a quién, a qué
  archivo) va en `DataList`. RF3: un cambio pedido sobre una captura vale para toda la app; se buscan también las
  pantallas parecidas, no sólo las que usan la misma pieza.
- **Por qué nació.** El usuario marcó el diálogo «Exportar respuestas a Excel» como una lista suelta y preguntó por qué
  las premisas no se aplicaban solas. Se había programado distinto del tablero y nadie comparó, cada regla se aplicaba de
  a una y las reglas estaban repartidas (`docs/design/dialogos-con-la-lista-de-chequeo.md`, «Por qué pasa»). No se agregó
  un control automático: no se pueden acotar sus falsos positivos.

## 2. Reglas nuevas para todas las pantallas

| Regla | Qué dice | Dónde leer |
| --- | --- | --- |
| **Un dato por columna (UD, 2026-11-09, premisa)** | Cada columna muestra un dato y su encabezado lo nombra. Cuentan como uno la fecha y la hora de un mismo momento, un valor con su unidad y varios valores del mismo dato. No van juntos un número, su fecha y su estado; «fecha · persona»; «Publicado · versión 3»; una etiqueta como «Corregida» al lado del dato; ni el resumen de otra tabla. Excepciones elegidas: **A**, un texto secundario en gris debajo del dato, que no se ordena ni se filtra; **C**, el punto de estado delante del correo en Usuarios; **F**, las listas que no son tablas siguen la regla de lecturas. Se descartaron B, D y E. Otra excepción pide una elección del usuario. Se separaron 16 columnas de 8 pantallas (por ejemplo, «Última respuesta» pasó a número, «Recibida» y «Estado»). | `visual-baseline.md`, «Un dato por columna — 2026-11-09» |
| **El motivo de lo apagado (MA1-A, 2026-11-09)** | El motivo va en un tooltip sobre el mismo control apagado, sin íconos, candados ni texto al lado. Se abre con el mouse, el foco, un clic o un toque, también en el celular; el clic muestra el motivo y no hace la acción. Responde el control entero (la casilla con su texto, el renglón de un radio). El control no cambia de forma al apagarse, se alcanza con Tab (`aria-disabled`) y el lector dice el motivo. Lo obvio, como «Guardar» sin cambios, no lleva motivo. Se descartaron MA1-B (una (i) al lado) y MA1-C (un candado). | `visual-baseline.md`, «El motivo de lo apagado — 2026-11-09» |
| **Avisos según el tipo (AV1-A, 2026-11-08)** | Todo aviso es blanco; el tipo se ve en un filo de 3 px a la izquierda y en el ícono, con el mismo color: éxito verde con tilde, información gris con (i), atención con triángulo, error con octógono. La cruz va adentro, a la derecha. Sin fondos tintados. | `visual-baseline.md`, «Avisos según el tipo — 2026-11-08» |
| **Una cosa, una zona (IA1-A, 2026-11-08)** | Lo que se configura y se guarda junto (una conexión, un registro) va en una sola zona: los campos en el orden en que se completan, cada botón al lado del dato que toca y lo que actúa sobre todo en la banda. Se parte en zonas sólo si son cosas distintas o pasos, nunca por tipo de control. Caso: «Inteligencia artificial» pasó de tres zonas a «Conexión con el proveedor». | `visual-baseline.md`, «Una cosa, una zona — 2026-11-08» |
| **Qué va a la vista y qué en ⋮ (AC1-A, 2026-11-11)** | A la vista, con texto, la tarea de la pantalla (guardar, publicar, crear, exportar, aprobar o rechazar lo que espera); la principal, última. Ir a otro lado o bajar, como íconos con nombre en un panel o texto en la banda de una pantalla. En ⋮: corregir, anular, dar de baja, reactivar, eliminar, descargar la plantilla, ordenar, «Carpetas…»; lo destructivo al final, en rojo y después de una línea. Orden: la tarea, ir y bajar, ⋮ y al final ↑ ↓ ×. Reemplaza a CW10-A («Corregir» suelto). | `visual-baseline.md`, «Íconos y acciones», y `docs/design/acciones-del-encabezado.md` |
| **Fechas sólo con números (2026-11-10, premisa)** | `dd/MM/yyyy` en español y `MM/dd/yyyy` en inglés; con hora, después de una coma (`03/11/2026, 21:00`; en inglés con AM o PM). Nunca el mes en letras ni un formato propio de una pantalla. Vale también para el PDF, los avisos, el Excel y el asistente. | `visual-baseline.md`, «Fechas, sólo con números — 2026-11-10» |
| **Números con la región de cada idioma (2026-11-12, premisa)** | es-AR en español (`1.210`, `1.234,5`) y en-US en inglés (`1,210`, `1,234.5`): el `es` sin región no agrupa los números de cuatro cifras y salía «$ 1210». El porcentaje va pegado en las dos («21%», «10,5%»). Un identificador (código, CUIT, número de comprobante, teléfono) va como texto. Una sola pieza por lado y un test de arquitectura en cada repo impide un formateador suelto. | `visual-baseline.md`, «Números con la región de cada idioma — 2026-11-12», y `docs/architecture/backend.md`, «Presentación general y precedencia» |
| **Confirmaciones en una frase (2026-11-11)** | Una confirmación dice en una frase lo que se afecta, sin instrucciones. Se ajustaron cinco (pasar a modo Asistente, la revisión de un envío, quitar la clave, descartar un pedido y descartar cambios al cambiar de idioma). | `docs/design/dialogos-con-la-lista-de-chequeo.md` |
| **Detalles de piezas** | El título de un grupo de opciones lleva su (i) (`GroupLabel`); un chip de sólo lectura va sin cruz; el tooltip respeta los saltos de renglón; las teclas que mueven la pantalla mueven el contenido aunque el foco esté en el menú; un desplegable mide 44 px en el celular. | `visual-baseline.md` y `docs/architecture.md` del front |

Dos decisiones viejas quedaron cerradas como estaban (2026-11-11, «Queda así»): una sola forma de diálogo, la pieza común,
y el aviso de error dura 10 s, o hasta cerrarlo si trae una acción.

## 3. Pantallas que cambiaron

- **Roles (RO1-A, 2026-11-11).** Cierra el rediseño pendiente desde septiembre. Los permisos de un rol son una matriz con
  una fila por área y las columnas Área, Ver (`<área>.read`), Administrar (`<área>.manage`) y Otros permisos (el resto, con
  su nombre). La casilla del área actúa sobre toda la fila. Qué permite cada permiso va en la (i) del área. Se fueron el
  buscador, «Todos · Elegidos», «Expandir todo» y las áreas plegables. Desde 768 px se ve la matriz; más angosto, una banda
  por área con sus permisos debajo. Admin se lee con tildes y sin casillas; User tiene el nombre de sólo lectura, con el
  porqué en la (i). Se descartaron RO1-B (segmentado Ninguno · Ver · Administrar) y RO1-C (áreas a la izquierda). Front
  `9f9a9d84` y `954dec3b`; backend `e39d05fc` (premisas). Diseño en `docs/design/roles-y-permisos.md`, con su prueba
  manual.
- **Los diálogos con la lista de chequeo (DL, 2026-11-11).** Se rehicieron 19, con piezas que ya existían. Algunos: dar de
  baja un formulario en dos columnas, «Deja de pasar» y «Se conserva»; eliminar con una frase y «Eliminar formulario»;
  la revisión de un catálogo lado a lado; enviar un formulario con un `DataList` de lo que se manda; «¿Aprobar N?» con
  quién la cargó y cuándo; la foto con su nombre y una línea gris con el tamaño y la fecha; verificar un número o un correo
  con el código en su campo. La tabla completa está en `dialogos-con-la-lista-de-chequeo.md`, «Los demás diálogos». El
  de exportar (EX1-A y EX2-A) lo cuenta el 01, §6.
- **Las acciones del encabezado (AC1-A).** Se revisaron 22 pantallas. Cambiaron la vista rápida y la pantalla completa de
  una respuesta («Corregir» y «Anular» al ⋮; «Aprobar» y «Rechazar» a la vista y primeros mientras espera), los editores
  de catálogo («Dar de baja» y «Reactivar» al ⋮), los editores con «Carpetas…» y la conexión de ARCA («Desconectar» al ⋮,
  en rojo). La tabla está en `docs/design/acciones-del-encabezado.md`.

## 4. Estado

Todo está programado en el front. Falta la prueba a mano, que va en la prueba integral (07), y pasar los tableros al
lienzo del Sistema visual, que esta cuenta no puede abrir. Los tableros de UD, MA1-A, IA1-A, los diálogos y Roles están en
lienzos aparte de esta cuenta; lo elegido quedó registrado en los documentos de diseño del front, que son la fuente segura.

## 5. Para el multitenant

- **Un dato por columna contra tu «un dato por celda, en un renglón»** (tu decisión del 2026-10-25, en
  `docs/rules/paginado-y-listados.md` del front). Coinciden en lo principal, pero tu regla sacó la segunda línea gris y
  acá quedó como excepción A (un texto secundario que no se ordena ni se filtra). Es una pregunta para el usuario.
- **Fechas contra tu `docs/rules/formatos.md`.** Allá la fecha con hora va «sin coma» y existe una fecha larga con el mes en
  letras. Acá la hora va después de una coma y el mes nunca va en letras, tampoco en el PDF ni en los avisos. También para
  el usuario.
- **Números.** Tu lista de la puerta pide «la cultura del pedido»; acá la cultura se fija en es-AR o en-US según el
  idioma, y el porcentaje va pegado. Revisá también tu nota de «Lo que aprendimos» sobre el punto decimal: WhatsApp ahora
  pide coma en español (03, H18).
- **Lo apagado, los avisos y el ⋮.** Todo tablero con una (i) o un texto al lado de un botón apagado, avisos con fondo de
  color o botones como «Corregir», «Dar de baja» o «Desconectar» sueltos en un encabezado se redibuja con MA1-A, AV1-A y
  AC1-A. Tu versión 145 ya lleva «su acción y un ⋮» en el encabezado de una sección: AC1-A fija qué entra en el ⋮.
- **Roles.** Tu editor de roles se redibuja como matriz. Con permisos de organización y de plataforma, la matriz se arma
  igual, por área, en cada acceso.
- **La lista de chequeo y RF1 a RF3** sirven como paso de tu Diseño antes de mostrar un tablero, junto con
  `alinear-etapas`.
