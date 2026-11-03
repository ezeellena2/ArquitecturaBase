# Lecturas, filtros y renglones: traspaso 5 al multitenant (2026-11-03)

Autor: el chat guía de ArquitecturaBase. Destino: ArquitecturaBaseMutitenant, para sus etapas **E12 · Catálogos**,
**E14 · Formularios**, **E15 · Respuestas**, **E16 · Datos de las personas**, **E18 · Fotos y documentos**, **E19 ·
Condiciones, cálculos y líneas**, **E20 · Cargar desde la web**, **E21 · Reglas y aprobación** y **E23 · Asistente**, para
el texto de diseño de todas (`docs/plans/formularios/` del multitenant) y para las reglas de pantallas de su front.

El 2026-11-02 y el 2026-11-03 el usuario siguió probando la aplicación de punta a punta, después de lo que contó el
[traspaso 4](2026-11-02-traspaso-multitenant-ajustes-de-la-prueba.md):
- miró con más detalle la planilla, el detalle y las reglas de «Registro de aplicación»;
- armó «Recepción de insumos» a partir de un remito en PDF con tres artículos y lo cargó desde la app;
- probó sus filtros, sus reglas y el aviso a un rol.

Cada cosa que no le cerró se dibujó en tableros, eligió una y se programó, salvo los renglones, que quedaron diseñados.
Además fijó una forma de trabajar: un cambio de diseño pedido sobre una pantalla vale para toda la app. Este traspaso
cuenta:
- qué decidió el usuario;
- cómo quedó;
- qué se encontró en la prueba;
- qué tiene que evaluar el multitenant.

**Como los anteriores, no es código para copiar** (decisión 18 de su hoja de ruta). Tampoco hace falta para dibujar
(decisión 108): los tableros de acá son un ejemplo para adaptar con sus piezas y su decisión 43.

## 1. En pocas palabras

- **Una forma de trabajar** (§2): un cambio de diseño pedido sobre una pantalla se busca y se aplica en toda la app, y se
  escribe como regla general en los textos de diseño.
- **Cuatro reglas para todas las pantallas:**
  - toda tabla lleva una línea entre columnas y los números a la derecha (§3);
  - una lectura no deja rótulos sueltos: lo que pasa en orden va en pasos, los datos de un registro en dos columnas y dos
    versiones, lado a lado (§4);
  - lo elegido se distingue con algo más que un tinte (§5);
  - un filtro de opciones es un desplegable simple, de una opción y sin cantidades (§6).
- **Respuestas** (§6 y §7): la planilla con dos solapas y un solo «Filtros»; el detalle con los datos alineados, la
  actividad en cuatro columnas fijas y las acciones del encabezado junto a la navegación.
- **Reglas** (§8): se leen en pasos, el historial se abre desde su sección y el mensaje se arma con «Agregar dato», que
  guarda la identidad de la pregunta.
- **Editor y carga** (§9 y §10): las zonas de una pregunta de catálogo arrancan plegadas; el armado tiene dos anchos y la
  página de carga también mantiene lo cargado para la siguiente.
- **Decidido y sin programar** (§11): los renglones, un bloque de preguntas que se repite, como los artículos de un remito.
  También se diseñó la conexión con ARCA.
- **La prueba** (§12): lo que se encontró sirve como lista de «no repetir». Lo más importante: el aviso a un rol sale sólo
  por WhatsApp y falló sin que el editor lo anticipara.
- **El código todavía no está en GitHub:** los commits son locales. Desde esta PC se leen en
  `C:\Users\ezequ\source\repos\ArquitecturaBase` y `C:\Users\ezequ\source\repos\ArquitecturaBaseFront` (§14).

## 2. Cada cambio de diseño vale para toda la app (2026-11-03)

Probando «Recepción de insumos», el usuario pidió tres cambios sobre capturas: el filtro de una pregunta con opciones, la
lectura de las reglas y el «Sí o no» de la carga. Agregó que cada uno tenía que cumplirse en toda la aplicación. Desde
entonces, un cambio de diseño:
- se busca en todos los lugares que usan la misma pieza o el mismo patrón;
- se dibuja con casos de varias áreas;
- se escribe como regla general en los textos de diseño del front (`visual-baseline.md` y `premisas-de-desarrollo.md`),
  para que lo cumplan las pantallas nuevas.

Por eso varias decisiones de este traspaso nacieron en una pantalla de formularios y terminaron en Usuarios, en el chat de
WhatsApp, en Configuración o en Información del negocio.

## 3. Tablas: líneas entre columnas y números a la derecha (2026-11-02)

Mirando la planilla de respuestas, el usuario dijo que las columnas no se distinguían: todo se veía como una sola línea
uniforme. De tres bocetos con sus datos eligió la **A**. Descartó la B, un encabezado de dos renglones que agrupaba las
subcolumnas de catálogo bajo su pregunta, y la C, sin líneas y con más aire. Al verla en las tablas anchas pidió lo mismo
**en todas las tablas**, listas incluidas.

| Regla | Cómo quedó |
| --- | --- |
| **Una línea fina entre columnas** | En el encabezado y en el cuerpo, del tono de las líneas entre filas. La pone la biblioteca en toda tabla, sin una prop ni clases por pantalla. Es una sombra interior de la celda y no un borde, porque con `border-collapse` el borde de una celda fija no se desplaza con ella. |
| **Los números, a la derecha** | Cantidades, medidas, importes y el número de fila, encabezado incluido. Un texto con un número («En 3 formularios»), una fecha o un código («VIS-000024») no es una columna de números. |
| **La tabla cierra abajo** | La última fila conserva su línea, sobre la barra de desplazamiento o el paginado. Si la tabla es lo último de su superficie, el borde de la superficie ya la cierra. |
| **Un código no se parte** | «30-71458236-9» no se corta en sus guiones; el texto sigue cortándose en sus espacios. Lo usan las subcolumnas de catálogo, la elección múltiple de la planilla y los cambios de una importación. |
| **Una zona horaria se corta en sus barras** | A 320 px, «America/Argentina/Buenos Aires» se partía en cualquier letra. Ahora se corta sólo después de una barra y en los espacios. Una fecha no se corta. |

La banda del encabezado, las filas alternadas, el hover, la selección y el foco no cambiaron.

## 4. Lecturas: pasos, dos columnas y comparaciones lado a lado (2026-11-03)

Una lectura es lo que se muestra de algo guardado sin editarlo: una regla plegada, una sugerencia, los datos de un
registro, lo que propuso el asistente. Mirando una regla, el usuario dijo que el mensaje con etiquetas (§8) le gustaba,
pero no los rótulos chicos en gris («Cuándo», «Si», «Hacer») con frases sueltas al lado, dos numeraciones juntas y el rol
pegado con un punto. Lo pidió para toda la app y eligió tres tableros:
- **LR1-A**, en pasos. Descartó una tabla con «Cuándo», «Si» y «Hacer» como columnas, y un recuadro por regla con las
  condiciones como los filtros de la planilla. Para los demás datos sueltos eligió las dos columnas del detalle de una
  respuesta.
- **LR2-A**, para las comparaciones: el dato una vez y las dos versiones en su columna. Descartó cada versión entera en
  dos columnas, y sólo lo que cambió, en un renglón con una flecha.
- **LR3-A**, para lo elegido en otra página: su tarjeta primero en la lista. Descartó un renglón con la tilde debajo del
  buscador.

La regla general:

| Regla | Cómo quedó |
| --- | --- |
| **Ningún rótulo suelto con texto al lado** | Cada dato va en un lugar fijo, lo principal destacado (negrita o el color del texto) y lo secundario en gris. No va un rótulo chico pegado a una frase («Cuándo  Se recibe una respuesta») ni «Rótulo: valor» corrido en un renglón («Cliente: Pérez Hnos.», «Zona: Norte · Rubro: Frío»). |
| **Lo que pasa en orden, en pasos** | Una regla o una sugerencia se lee de arriba abajo, un paso por parte, con su ícono en una columna fija y una línea que lo une al siguiente. Las palabras que unen («Si», «y», «es», «desde») van en gris y los datos, en negrita. Un dato aparte de una acción (el rol, «con el PDF») va en una etiqueta, y un texto del autor (el mensaje), entero en su recuadro. |
| **Los datos de un registro, en dos columnas** | El dato en gris y del mismo tamaño que el valor; el valor en el color del texto; una línea entre renglones. Los datos que dependen de un valor, como los campos de la opción elegida, van alineados debajo de él (§7). |
| **Dos versiones, lado a lado** | Qué cambió, el dato una sola vez, en gris, y las dos versiones en sus columnas, frente a frente; sólo entra lo distinto. Lo que una versión no tiene lo dice en gris («Quitada», «Sin traducir», «—»), y un orden se lee numerado, con lo que se movió en negrita. En el celular, el dato ocupa su renglón y las dos versiones van debajo, sin desplazar de costado. |
| **Lo elegido en otra página, primero** | En una lista paginada para elegir, la tarjeta elegida que no está en la página va arriba de las demás, marcada; nunca un renglón «Elegido: …». |

Siguen como estaban la línea secundaria en gris debajo de un título (fecha · quién) y el resumen de una zona plegada (§9).

Dónde se aplicó:

| Pantalla | Cómo se lee ahora |
| --- | --- |
| Reglas guardadas y su historial | En pasos (§8). |
| Sugerencias de «Qué sugerir después» y su historial | En pasos: el formulario y cómo se ofrece, cuándo (con «La IA decide»), si y los datos que pasan, como etiquetas. |
| Chat de WhatsApp: «Por qué lo mandó» y lo prellenado de una sugerencia | En dos columnas. |
| Tarjeta de una respuesta en el chat | El detalle de cada opción, alineado con su opción. |
| Inicio: los valores de cada fuente de una respuesta del asistente | En dos columnas. |
| Pregunta de catálogo: lo que tiene la persona probada en «Como si lo recibiera» | En dos columnas. |
| Revisión del borrador y «Cambios de la otra revisión para revisar» | Qué cambió, Dato, Guardado actualmente y Mis cambios, con los nombres del editor («Obligatoria», «Máximo», «De dónde sale la foto»). Una pregunta que una versión no tiene muestra sus datos y «Quitada» una vez. Una opción se nombra «Unidad · la opción «Kilos»». |
| Importar opciones, «Cambios» | Fila, Código, Columna, Antes y Después. |
| Información del negocio y Configuración, al revisar un conflicto (409) | Sólo lo distinto, con sus dos valores. En el negocio, el horario muestra sólo los días y fechas distintos, la ubicación sus partes y las preguntas frecuentes cada respuesta con su pregunta como dato. |
| Enviar un formulario desde el chat | El elegido de otra página va primero en la lista, con su tarjeta elegida; otra página lleva el foco a su primer formulario. |

## 5. Lo elegido se distingue sin dudas (OE1-A, 2026-11-03)

Cargando «¿Llegó en buen estado?», el usuario dijo que costaba distinguir cuál estaba elegida entre «Sí» y «No». La
elegida era un verde casi blanco sobre blanco (contraste de 1,1:1) y, con el mouse encima, la otra tomaba el mismo tono.
Eligió la **A**, la elegida rellena con el color de los botones. Descartó la B: el grupo en gris claro y la elegida en
blanco, con un borde y una tilde.

La regla, para toda pantalla: **lo elegido se distingue por algo más que un tinte** (un relleno, una tilde, un punto, un
subrayado o una barra), con un contraste de 3:1 contra lo no elegido y su texto a 4,5:1, en cualquier paleta de
Apariencia. No se arman botones sueltos que se rellenan para hacer de segmentado.

| Dónde | Cómo se ve la elegida |
| --- | --- |
| Botones agrupados (segmentados): el «Sí o no» de la carga y los filtros de estado, origen o vista | Rellena con el tono de los enlaces y su texto en blanco o negro, el de más contraste; las otras, blancas. El foco es un anillo por fuera. En el celular, cada opción mide 44 × 44 px. |
| Tarjetas de opción: catálogo nuevo, presentación de una pregunta, fuente de las fotos, enviar un formulario, sugerencias | Un borde de 2 px, sin moverse, y el tinte de la marca, además del punto. |
| Desplegable de un filtro y el idioma del menú de la cuenta | Una tilde a la izquierda, como en «Columnas». |
| La fila abierta de una tabla: la respuesta de la vista rápida, lo que espera una decisión | El tinte de la marca y una barra de 3 px al inicio. |
| La conversación abierta en el chat de WhatsApp | El tinte de la marca y una barra de 3 px al inicio. |

Ya cumplían y no cambiaron: los radios (el punto), las casillas, las solapas (subrayado y negrita), el menú lateral
(barra), las listas con detalle y la foto que se ve en grande (anillo). «Todos» y «Sin leer» del chat eran dos botones
sueltos y pasaron al segmentado común.

## 6. Filtros

### La planilla: dos solapas y un solo «Filtros» (PL1-A, 2026-11-02)

Mirando la planilla, el usuario preguntó por qué había tantos botones de filtro («Lote», «Recibida», «Más filtros»). Pidió
los filtros uno al lado del otro, un botón como el desplegable de «Columnas», sin «5 respuestas» y, en vez de la banda de
las por aprobar arriba, dos solapas. Eligió la **A**, con lo aplicado en chips; descartó la B, con lo aplicado adentro de
«Filtros».
- **Dos solapas.** «Por aprobar (N)» tiene la tabla de lo que espera (N°, Cargó, Recibida, Rol y «Revisar»), de la más
  vieja a la más nueva y sin filtros; «Todas» es la planilla con sus filtros. Se abre en «Por aprobar» si la persona tiene
  algo para decidir y, si no, en «Todas». La solapa va en la dirección. Quien sólo ve respuestas tiene la planilla, y quien
  sólo aprueba, lo que espera su decisión, sin solapas.
- **Tres filtros a la vista**, sin botón para aplicar: la búsqueda (si el formulario tiene textos largos), el canal y el
  estado (si pide aprobación), cada uno con su ancho.
- **Un solo «Filtros»**, con la cantidad aplicada, que se despliega como «Columnas»: a la izquierda Recibida, cada pregunta
  en orden, Cargó y Versión; a la derecha, lo que se elige en la marcada. Cada cambio se aplica enseguida: se fueron «Ver N
  respuestas», Cancelar y «Contando…». En el celular ocupa la pantalla.
- **Lo aplicado, en chips** con su × y «Limpiar todo», como en Usuarios.
- **Sin «N respuestas»:** la cantidad ya está al pie de la tabla.

### Una pregunta con opciones: un combo de una opción (PL2-A, 2026-11-03)

Adentro de «Filtros», el usuario encontró una caja «Buscar opciones», una casilla por opción con «1 respuesta» debajo de
cada una y un desplegable «Respuesta». Pidió un combo simple y preguntó para qué era «Respuesta». Eligió la **A**, que
elige una; descartó la B, que marcaba varias.
- **Un combo como cualquier desplegable.** Cerrado dice «Todas» o la elegida; abierto, «Todas», una línea y las opciones.
  Escribiendo se busca entre todas, también las que no se ven. Muestra 20 y, al pie, «Mostrar N más».
- **Cada opción, un renglón,** con su nombre y sin la cantidad ni otro texto debajo. La elegida lleva la tilde a la
  izquierda.
- **Se elige una:** elegir cierra la lista y aplica, y el chip dice «Artículo: Glifosato 48%». Sin cantidades, una opción
  con cero ya no se apaga.
- **Igual en toda pregunta con opciones** (de catálogo, de opciones propias y sí o no), en Cargó y en Versión.
- **Sin «Respuesta»:** servía para ver las respuestas que dejaron vacía la pregunta o las de una versión sin ella. El
  usuario eligió sacarlo, en vez de dejar «Sin respuesta» como una opción más del combo. Foto y Documento, que sólo tenían
  eso, salieron de la lista de «Filtros».

El contrato no cambió: las facetas siguen trayendo sus cantidades, y `answered`, `unanswered` y `notAsked` siguen para
reglas y sugerencias. La columna de una pregunta de catálogo no trae sus opciones, así que el chip decía «Valor no
disponible»: ahora el nombre de la elegida viaja con el cambio y la planilla lo recuerda.

### En toda la app (FO1-A, 2026-11-03)

El usuario pidió que la regla valga en todos lados. De dos tableros eligió la **A**: los desplegables, sin cantidades, y
los segmentados, como estaban; la B también les sacaba el número a los segmentados. La regla reemplaza «una opción con cero
coincidencias queda deshabilitada»:
- Un filtro de opciones es un desplegable simple. Cerrado dice «Todas» (o «Cualquier …») o la elegida; abierto, una opción
  por renglón, sin la cantidad ni texto debajo, con la elegida tildada. Ninguna se apaga: si no hay coincidencias, el vacío
  lo dice y ofrece limpiar.
- Con muchas opciones o que vienen del servidor (las de un catálogo, las personas), es el combo con búsqueda; con pocas y
  fijas, el desplegable del filtro, como Rol en Usuarios. Se elige una.
- Un segmentado de pocas opciones fijas conserva la cantidad de cada conjunto («Activos (8) · Dados de baja (2)»), que sale
  del servidor con los demás filtros aplicados.
- En Usuarios, Rol y «Más filtros › Creado» perdieron la cantidad y las opciones apagadas. El endpoint de conteos sigue
  igual: el front lo usa para ofrecer los roles y los tramos.

No entran en la regla «Columnas», las condiciones de una regla o una sugerencia, que eligen varias opciones, ni «Elegidos ·
N» del selector de permisos de un rol.

## 7. El detalle de una respuesta (TL1-A y TL3-A, 2026-11-02)

Mirando el detalle, el usuario dijo que las pantallas con mucho texto cuestan de leer. Más tarde dijo que la actividad
tenía «tanta información así desparramada sin sentido», con «Hecho» en una punta del renglón y etiquetas sueltas, y que los
botones de la vista rápida, en un renglón propio, «están raros ahí». Eligió **TL1-A**, alineado (descartó la versión en
una línea y la de celdas), y **TL3-A**, por momento (descartó una sola lista). No cambió lo que se muestra, sino cómo se lee: tres
tamaños de texto y lo secundario en gris.
- **El valor, primero.** La pregunta a la izquierda, en gris; el valor, en negro. En pantalla completa la pregunta tiene una
  columna fija, así el valor queda cerca.
- **Los datos del catálogo, alineados debajo del valor**, en una tabla chica con una línea a la izquierda que los ata a él:
  el dato en gris y su valor en negro. Se fue el nombre del catálogo («Lotes»), que repetía la pregunta.
- **Los momentos, un renglón cada uno,** arriba de la vista rápida: el momento con su fecha a la derecha y, debajo en gris,
  quién o qué («En la app · Ana Pérez», «Agrónomo · Marcos Gil»).
- **La actividad, en cuatro columnas fijas:** Fecha, Qué pasó, Estado y Acciones, iguales en todos los renglones. Quién y el
  detalle van debajo de qué pasó, en gris. «Hecho», «Aprobada» o «No se pudo» van siempre en Estado, y «Ver PDF»,
  «Reintentar», «Consultar» o «Resolver envío», siempre en Acciones. En la vista rápida las acciones van debajo del texto;
  en el celular, también el estado.
- **Por momento:** bandas finas, «Al recibirla», «Aprobación», «Al aprobarla» o «Al rechazarla», con los renglones en el
  orden en que pasaron.
- **Nada se repite:** quién la cargó va una vez; el estado, una vez, en su columna; la decisión es un renglón y el motivo de
  un rechazo va en su recuadro; la fecha va una vez por día, y la hora en todos los renglones.
- **El PDF, en su renglón,** debajo de «Armar el PDF», sin la vista previa chica.
- **Los avisos:** cada persona de un aviso a un rol va en su renglón, aunque sea una sola, con el estado de su envío y, si
  hace falta, «Resolver envío». El aviso puede estar «Hecho» aunque una persona no lo haya recibido. Los acuses de WhatsApp
  («Entregado», «Leído») van con su hora debajo de la persona.
- **Las acciones del encabezado, junto a la navegación:** en la vista rápida, «Abrir en pantalla completa», «Descargar ▾» y
  «Abrir conversación» son íconos con su tooltip en el renglón del título, antes de ↑ ↓ ×.

**Un arreglo de aprobaciones.** Quien tenía el permiso de aprobar pero no el rol de la tarea (por ejemplo, una cuenta
administradora) veía en el detalle «No encontramos la tarea de aprobación.» y «No pudimos cargar los datos»: el panel pedía
la tarea y la API la responde como inexistente fuera del rol. Ahora el detalle la toma como no disponible, sin error. La
vista rápida muestra la decisión de sólo lectura (rol, quién decidió y cuándo) y la pantalla completa no reserva la barra
de decisión.

## 8. Reglas: pasos, historial y el mensaje con datos

### Cómo se lee una regla (TL2-A y LR1-A)

El 2026-11-02 la lista de reglas pasó a rótulos fijos («Cuándo», «Si», «Hacer») con las acciones numeradas (TL2-A). Al día
siguiente LR1-A la reemplazó por pasos (§4), y así quedó:
- **Un paso por parte, en el orden en que pasa:** el cuándo, «Si» con sus condiciones y cada acción. Cada paso lleva su
  ícono en una columna fija: una flecha cuando se recibe, una tilde cuando se aprueba y un círculo tachado cuando se
  rechaza; el de «Filtros» para las condiciones; un escudo para pedir aprobación, una hoja para el PDF, un globo para el
  mensaje y personas para el aviso a un rol.
- **Las condiciones, una por renglón:** «Si ¿Llegó en buen estado? es No», «y Cantidad recibida desde 500», con la pregunta
  y el valor en negrita.
- **Cada acción, un paso,** con su dato aparte en una etiqueta: el rol o «con el PDF», que antes la lista no mostraba. Se ve
  que el PDF se arma antes del mensaje que lo adjunta. El mensaje va entero debajo, en su recuadro.

### El historial detrás de un botón (HI1-A, 2026-11-02)

Las reglas y las sugerencias tenían cada una su historial en una columna fija a la derecha, siempre a la vista, aunque
estuviera vacío. De tres tableros (A, un botón por sección; B, solapas; C, un solo historial del formulario) el usuario
eligió la **A**:
- «Historial», con su reloj, va primero entre las acciones del encabezado de cada sección.
- Sin revisiones queda apagado, con «Todavía no hay revisiones.» en su tooltip.
- Abre un panel al costado con las revisiones, de la más nueva a la más vieja: el número, «Vigente», la fecha, quién y qué
  cambió. Tocar una muestra sus reglas en pasos, sin editar. En el celular ocupa la pantalla.
- Las secciones ganaron todo el ancho.

### El mensaje con datos (MD1-A, 2026-11-03)

Armando un formulario, el usuario vio el mensaje «Llegó con problemas el remito {N° de remito} de {Proveedor}. Revisá la
recepción {número}.» y dijo que nadie va a saber que hay que poner los datos entre llaves. De tres tableros (A, «Agregar dato»
pone etiquetas; B, escribir «@» abre la lista; C, el botón escribe las llaves) eligió la **A**:
- **«Agregar dato»**, al final del renglón de «Mensaje», abre una lista con buscador: «Número» y, al rechazar, «Motivo del
  rechazo»; y las preguntas en su orden, sin fotos ni documentos, porque llegaría sólo el nombre del archivo. El dato entra
  donde está el cursor.
- **El cuadro** muestra cada dato como una etiqueta con el nombre actual de su pregunta, que se borra entera. Las llaves
  escritas o pegadas no entran. Una pregunta que ya no está se ve «Pregunta no disponible», en rojo, y no deja guardar.
- **«Así llega»**, debajo, muestra el mensaje con los datos de ejemplo de la vista previa del PDF.
- **Qué se guarda:** la identidad de la pregunta, `{FieldId}`, y no su nombre. La etiqueta sigue a la pregunta renombrada y
  el mismo dato sirve en los dos idiomas. Antes, renombrar una pregunta no rompía lo guardado (la revisión tiene su copia de
  las preguntas), pero el siguiente «Guardar reglas» fallaba sin decir qué nombre había cambiado.
- **Compatibilidad:** el servidor sigue aceptando el nombre entre llaves. Al leer las reglas, una revisión o el historial, lo
  convierte en la referencia con el esquema fijado de esa revisión, así el editor y la comparación del historial ven una
  sola forma. El back y el front se despliegan juntos: con el back nuevo y el front anterior, el editor muestra los GUID.

## 9. El editor de una pregunta de catálogo: zonas plegadas (ZP1-A, 2026-11-02)

Armando «Recepción de insumos», el usuario pidió poder plegar las zonas del editor de una pregunta «Elegir de un catálogo».
Eligió la **A**, plegadas con su resumen; descartó la B, expandidas como en la carga.
- «Qué opciones ve cada persona» y «En WhatsApp» **arrancan plegadas** cada vez que se abre la pregunta, con la flecha en
  la banda.
- **La banda dice lo configurado**, en una línea: los filtros en orden («Rubro: Agroquímicos», «Zona según quien lo
  recibe», «sin valor» si falta, «Sin filtros» si no hay) y el modo de «En WhatsApp» («Como en el catálogo · Texto ·
  Detalle»).
- **Expandida,** «Agregar filtro…» vuelve a la banda, en el lugar del resumen; la (i) va junto al título.
- **La que tiene un error o un aviso se abre sola:** un error al guardar, un problema de la publicación, datos de las
  personas que no se pudieron leer, una prueba con una persona que falla, un máximo que deja a alguien sin opciones o el
  chequeo del largo de WhatsApp. Se puede volver a plegar, y sólo un problema nuevo la abre otra vez.

Las demás preguntas no tienen zonas: sus ajustes son campos del editor.

## 10. Armado y carga desde la web (AR1-A y AR2-A, 2026-11-02)

### Dos anchos (AR1-A)

Mirando «Registro de aplicación», el usuario preguntó si había que estandarizar los formularios: como son dinámicos, le
daba la sensación de que no iban a quedar todos bien. Ya estaban estandarizados (el autor decide el contenido y el orden, y
la app la forma según el tipo); lo que se ajustó es esa regla. Un tablero dibujó tres formularios con la regla anterior y
marcó dónde quedaban mal: renglones a medias, casillas apiladas en una columna alta, preguntas parecidas que caían en fila o
en lista según el largo de sus opciones, una sola zona larga y el panel sin zonas. Eligió la **A**; la B dejaba los tres
anchos con las piezas arregladas y la C volvía a una columna.
- **Dos anchos.** Lo que tiene un solo valor ocupa una columna: número, fecha, sí o no, texto corto de hasta 80, elegir una
  y elegir de un catálogo, de una o de varias. Lo que necesita espacio ocupa el renglón entero: texto largo, texto corto sin
  largo máximo o de más de 80, elegir varias con opciones propias, fotos y documentos. Se fueron las «dos columnas». La
  grilla sigue siendo de tres en la compu, dos en la tableta y una en el celular y en el panel, sin reordenar.
- **Casillas en columnas.** Elegir varias reparte sus casillas de arriba abajo en tres columnas en la compu, dos en la
  tableta y en el panel y una en el celular, con «hasta N» en la etiqueta.
- **Opciones en fila, siempre.** Elegir una con hasta cinco opciones las muestra en fila aunque sean largas; con más, una
  lista desplegable. Dejó de contar el largo de cada opción.
- **El panel con sus zonas.** Junto a la planilla, «Datos» y «Fotos y documentos» se pliegan como en la página, con los
  archivos al final.

### Mantener lo cargado en la página (AR2-A)

Cargando el remito desde «Cargar», el usuario no encontró «Mantener las respuestas para la siguiente» en la página «Nueva
respuesta»: sólo estaba en el panel junto a la planilla. Eligió la **A**, la misma casilla; descartó la B, «Cargar otra con
lo mismo» en «Enviada».
- La casilla va en la banda, antes de Cancelar y Enviar; en el celular, en el pie fijo, arriba de Enviar.
- Marcada, al enviar el aviso dice el número y el formulario queda con lo cargado, sin pasar por «Enviada», con el foco en
  la primera pregunta. Sin marcar, se ve «Enviada», como antes.
- Los archivos no se mantienen, y la casilla no se recuerda al volver a entrar.
- **Cada envío es otra respuesta.** Al programarla apareció que, con las mismas respuestas mantenidas y sin archivos, el
  envío siguiente repetía la clave de idempotencia del anterior y recibía su número. Ahora, después de un envío que llegó,
  el front arma otro `requestId`; sólo un reintento después de un corte repite el anterior.

## 11. Decidido y sin programar

### Renglones en los formularios (RG1-A a RG6-A, 2026-11-03)

Cargando el remito de tres artículos, el usuario tuvo que mandar una respuesta por artículo, aunque «Mantener las
respuestas» le ahorrara volver a escribir el resto. Dijo que debería poder ir agregando varios artículos en la misma
carga. El diseño quedó cerrado el 2026-11-03, sin programar, en el [plan de renglones](2026-11-03-renglones-en-formularios.md).
Adelanta la parte de renglones de la Etapa 2 de la [hoja de ruta de ARCA](2026-10-31-arca-formularios-estandar.md). En las
seis filas eligió la recomendada:

| Tablero | Qué decidió |
| --- | --- |
| RG1-A · El editor | Un tipo de pregunta «Renglones»: un recuadro con sus preguntas adentro (5.1, 5.2…), con nombre, ayuda y mínimo y máximo de renglones. La (i) del máximo dice el tope de WhatsApp. |
| RG6-A · Elegir las preguntas | En el editor de cada pregunta, la casilla «Se carga varias veces», debajo de «Obligatoria». Las marcadas se juntan en el bloque y desmarcar una la saca. Una pregunta que ya existía pasa como pregunta nueva, y publicar lo dice en «Pasan a renglones». |
| RG2-A · La carga en la app | Una tabla a todo el ancho de «Datos»: un renglón por fila y una pregunta por columna, «Agregar renglón» al pie, ↑ ↓ y el tacho en cada fila, y los errores en su celda. En el panel y en el celular, un recuadro por renglón. «Mantener las respuestas» conserva también los renglones. |
| RG3-A · WhatsApp | Una pantalla por renglón, hasta 10, con «¿Agregás otro renglón?» desde el mínimo. Sin intercambio con Meta: es el Flow estático con más pantallas. Con un máximo mayor que 10, publicar avisa sin frenar. |
| RG4-A · Planilla, Excel, detalle y PDF | Una fila por respuesta; la columna del bloque dice la cantidad de renglones y los primeros valores. La vista rápida muestra cada renglón como los datos de un catálogo (§7); la pantalla completa y el PDF, una tabla. El Excel suma una hoja por bloque, con una fila por renglón. |
| RG5-A · Reglas | «Algún renglón con…»: una condición se cumple si al menos un renglón la cumple, y «Filtros» usa la misma regla. En el mensaje, el bloque es un dato con todos los renglones, uno por línea. |

Resueltas por defecto, sin objeción del usuario:
- un bloque por formulario, sin bloques adentro;
- adentro entran texto corto y largo, número, fecha, sí o no y elegir una, propia o de catálogo; quedan afuera elegir
  varias, las fotos y los documentos;
- el mínimo va de 0 al máximo y el máximo llega hasta 100; un bloque nuevo arranca con 1 y 20;
- las preguntas del bloque cuentan para el máximo de 40 del formulario.

En el modelo, el tipo reservado `Group` pasa a ser el bloque y su valor es una lista de renglones; lo publicado no cambia.
El plan tiene las cuatro etapas (editor y publicación, carga, lecturas y reglas) y lo que falta antes de programar.

### La conexión con ARCA (2026-11-03)

También quedó diseñada, sin programar, la [etapa 1 de ARCA](2026-11-03-arca-etapa-1-conexion.md): conectar la
instalación con el certificado de la empresa, probar la conexión, traer los datos de la empresa del padrón y elegir el
punto de venta, sin emitir. No es parte de este traspaso: se nombra para que el multitenant la tenga en cuenta cuando la
sume a su hoja de ruta, donde habrá que decidir de quién es la conexión.

## 12. Lo que se encontró en la prueba (no repetir)

1. **El aviso a un rol sale sólo por WhatsApp, y el editor no lo anticipa.** En «Recepción de insumos», la regla «si
   ¿Llegó en buen estado? es No, avisar al rol Agrónomo» se disparó con INS-000004. El aviso falló con «El destinatario no
   está disponible para recibir el aviso.»: la persona del rol tenía un contacto de WhatsApp con su número, pero no
   vinculado a su cuenta, y su ventana de 24 h estaba vencida. Para recibirlo hacen falta el contacto vinculado, una cuenta
   activa con el teléfono confirmado e igual al del contacto, y la ventana abierta. El editor de reglas no lo avisa y el fallo se ve sólo en la
   actividad de la respuesta. Conviene evaluar avisarlo al elegir el rol, decir cuántas personas del rol pueden recibirlo o
   sumar otro canal.
2. **Un comprobante con varios ítems no entra en una respuesta.** El remito de tres artículos fue tres respuestas iguales
   salvo el artículo. De ahí los renglones (§11).
3. **Las cantidades por opción y el filtro de presencia confunden.** «1 respuesta» debajo de cada opción y «Con respuesta /
   Sin respuesta / La pregunta no existía» eran ruido para el usuario (§6).
4. **Un tinte no alcanza para marcar lo elegido.** El «Sí o no» tenía un contraste de 1,1:1 (§5).
5. **Un rótulo chico al lado de una frase no ordena nada.** «Cuándo  Se recibe una respuesta» se leía como texto suelto
   (§4).
6. **Lo que tiene el panel tiene que estar en la página.** «Mantener las respuestas» estaba sólo en el panel (§10).
7. **Una clave de idempotencia no se reusa entre envíos distintos.** Con las mismas respuestas mantenidas, el segundo envío
   repetía el pedido del primero y recibía su número (§10).
8. **El detalle no puede depender de lo que la persona no tiene.** Con el permiso de aprobar y sin el rol de la tarea, el
   detalle fallaba (§7).
9. **Un dato guardado por nombre se rompe al renombrar.** El mensaje guarda la identidad de la pregunta (§8).
10. **Nada vacío a la vista.** Un historial sin revisiones ocupaba una columna fija (§8).
11. **Un chip sin el nombre de lo elegido dice «Valor no disponible».** La columna de una pregunta de catálogo no trae sus
    opciones; el nombre viaja con el cambio (§6).

## 13. Adaptación a sus etapas

Leído en el multitenant en `c339b58d` (front `c5172e65`, lienzo en la versión 147), con la hoja de ruta hasta la decisión
108. Es el mismo punto del traspaso 4, que todavía no está incorporado: conviene incorporar los dos juntos, porque el 5
sigue al 4.

Sus reglas de pantallas están en `docs/rules/` del front, en `docs/architecture/tema.md` y en los tableros «Criterio» de su
lienzo. Según `pantallas-y-ui.md`, esos tableros mandan junto con el tablero validado de cada pantalla. Por eso la mayoría
de los choques son con esas reglas y no con decisiones numeradas.

| Tema | Su etapa dice hoy | En ArquitecturaBase | Qué evaluar |
| --- | --- | --- | --- |
| Tablas (front `tema.md` §Forma, `formatos.md` y `Criterio-Tablas`) | Encabezado arena, filas alternadas y un dato por celda en un renglón. Sólo llevan línea las columnas fijas de una tabla que se desplaza (`12-catalogos.md` §10). Los números van a la derecha. Un texto largo se trunca con elipsis y tooltip. No dice nada de una línea que cierre la tabla. | Línea fina entre columnas en toda tabla, números a la derecha, la tabla cierra abajo y un código no se parte (§3). | Los números coinciden. La línea entre columnas y el cierre son nuevos y no chocan con una regla escrita. Con la elipsis el texto no baja de renglón, así que lo del código aplica sólo si una celda pasa a cortarse en renglones. |
| La planilla (E15, `15-respuestas.md` «Filtros» y F4; front `pantallas-y-ui.md`; `Criterio-Decisiones`; tablero `FRM-Respuestas-Filtros`) | El buscador y hasta tres filtros en una fila. «Más filtros» es un panel al costado, con borrador hasta aplicar. Lo elegido va en el filtro, sin fila de chips, con la cantidad en cada opción. La pestaña «Respuestas» lleva su cantidad, y el tablero, «24 respuestas» y «Ver 24 respuestas». | Un solo «Filtros», desplegable como «Columnas», que aplica cada cambio enseguida; chips con «Limpiar todo»; sin contador (PL1-A). | Los tres filtros a la vista coinciden. Chocan los chips, aplicar al momento y sacar el contador. |
| «Por aprobar» (E21, «Por aprobar en Formularios» y «La planilla»; `21-texto-diseno.md`; decisión 79) | Sin cartel arriba de la planilla: es una opción del filtro «Estado», y la cola se abre desde la campana y «Para resolver». El lienzo todavía dibuja «Por aprobar · 3» arriba de la planilla. Su front prohíbe pestañas para filtrar un listado y dos filas de pestañas, y las del formulario son Diseño, Respuestas y Tablero (79). | Dos solapas, «Por aprobar (N)» y «Todas», que abre en la primera si hay algo para decidir (PL1-A). | Las solapas chocan con su 79 y con esa prohibición. Allá ya sacaron la banda de otra forma, con el filtro y la campana. Falta alinear el lienzo, que todavía la dibuja. |
| El filtro de una pregunta con opciones (E15 «Filtros», «Conteos» y el contrato `facets`; `15-texto-diseno.md`; E18; E19; hoja de ruta §3) | Elegir una y sí o no usan `in` con varios valores; elegir varias, «alguna de»; persona y versión son listas. Cada opción trae su cantidad, con búsqueda y páginas. En todas las preguntas, «con respuesta», «sin respuesta» y «no existía»; la E18 lo suma para fotos y documentos y la E19 suma «No se mostró». | Un combo de una opción, con «Todas» y búsqueda, sin cantidades ni opciones apagadas; sin el filtro de presencia, y Foto y Documento salen de «Filtros» (PL2-A). El contrato conserva las cantidades y los valores de presencia, para reglas y sugerencias. | Choca en todo con su E15 y con la §3 de su hoja de ruta («alguna de» y el criterio de «sin respuesta»). El contrato puede quedar igual: cambia lo que muestra el front. Sin el filtro de presencia, también cambian la E18 y la E19. |
| Filtros de otros listados (front `pantallas-y-ui.md`, `paginado-y-listados.md` y `responsive.md`; `Criterio-Menus`; `docs/features/organizaciones.md`) | La regla general pide la cantidad por opción (`filter-counts`), también en la hoja del teléfono. Usuarios filtra por Estado y Empresa, con conteos. Sólo la auditoría va sin cantidades (R8). | Un desplegable simple, sin cantidades ni opciones apagadas, en toda la app; los segmentados conservan su número (FO1-A). | Choca con su regla general del front. Si la adoptan, la excepción de la auditoría pasa a ser la regla. |
| El detalle de una respuesta (E15 «El detalle»; front `pantallas-y-ui.md`; `Criterio-Decisiones`) | Los valores en `DetailGrid`: el rótulo a la izquierda, sobre arena clara, y el valor a la derecha. Decidir va en el momento «Por aprobar»; Actividad es una pestaña con lo que hizo cada regla, su estado y Reintentar. «Anular» va al final del ⋮. No dice nada de columnas fijas, bandas ni la fecha a la derecha. | El valor en negro y los datos del catálogo alineados debajo; los momentos con la fecha a la derecha; la actividad en cuatro columnas fijas, por momento y con cada persona de un aviso en su renglón; las acciones del encabezado como íconos junto a ↑ ↓ × (TL1-A y TL3-A). | Las dos columnas coinciden con su `DetailGrid`; cambia el rótulo (acá en gris, sin fondo). Lo nuevo son los datos del catálogo debajo del valor, la actividad en columnas fijas y los avisos por persona. «Anular» puede seguir en su ⋮. |
| Los historiales (`21-texto-diseno.md`; `Criterio-Decisiones`; tablero `FRM-Editor-Reglas`) | El historial de las reglas va a la derecha, a la vista; la versión anterior de una respuesta, en Actividad. | Detrás de un botón «Historial», apagado si no hay nada, en un panel al costado (HI1-A). | Adoptar el botón; la sección gana el ancho. |
| Cómo se lee una regla (E21; `Criterio-Decisiones`; tablero `FRM-Editor-Reglas`; front `pantallas-y-ui.md`) | Cada regla en palabras (Cuando, Si, Hacer), en un renglón, con el mensaje entre comillas. Su front ya prohíbe rótulos y valores sueltos, y pide la grilla. | En pasos con su ícono, las condiciones una por renglón, el dato de cada acción en una etiqueta y el mensaje entero en su recuadro (LR1-A); los demás datos sueltos, en dos columnas. | No choca: las tres partes coinciden y cambia la forma. Su prohibición de rótulos sueltos es la misma regla del §4. |
| El mensaje con datos (E21 «Reglas»; tablero `FRM-Editor-Reglas`; E22) | Los textos usan `{número}`, `{motivo}` y `{pregunta}`, escritos a mano. No dice si se guarda la identidad de la pregunta; las condiciones sí van por id. La E22 marca una pregunta borrada en el texto que la nombra. | «Agregar dato» con etiquetas, «Así llega» y `{FieldId}` guardado, con el nombre todavía aceptado (MD1-A). | Choca con escribir `{pregunta}` a mano. Guardar la identidad, como ya hacen con las condiciones, evita que renombrar una pregunta rompa el mensaje. |
| Comparaciones (E14 §9 «Estados»; E12 §10; front `formularios.md` y `concurrencia.md`) | Un 409 muestra `ConcurrencyBanner`, con «Ver lo nuevo» y «Seguir editando», sin comparar. El editor de formularios tiene «Qué cambió», «Guardado actualmente» y «Mis cambios» como tres vistas. Importar muestra errores, altas, cambios y bajas por fila y columna. | Lado a lado (LR2-A): la revisión del borrador, los cambios de una importación y los conflictos de Configuración y de Información del negocio. | No choca con una decisión. Lo nuevo es la forma: las dos versiones en columnas, sólo lo distinto y apiladas en el celular. |
| Lo elegido en otra página (E14 §9, `WA-Chats-Formulario`; front `paginado-y-listados.md` y `formularios.md`) | Una tarjeta por formulario, con buscador y paginado. Lo elegido se ve aunque no esté en la página, y la búsqueda no lo oculta. | La tarjeta elegida va primero en la lista (LR3-A). | Coincide; falta decir que va primero. |
| Lo elegido (front `tema.md` §Forma; `Criterio-Menus`; `Criterio-Decisiones`; tableros `FRM-Catalogo-Nuevo`, `FRM-Respuesta` y `WA-Chats`) | En el segmentado, la elegida va blanca con sombra (`--lado-activo`, `--sombra-segmento`). Un filtro elegido se pinta de petróleo, y en una lista desplegable la elegida va en 500; la tilde, sólo en las hojas del teléfono y en los perfiles. En los tableros, la tarjeta lleva borde y tinte; la fila abierta, tinte y una barra de 3 px; la conversación, sólo el tinte. | Nunca sólo un tinte: el segmentado rellena la elegida, la tarjeta suma un borde de 2 px, el desplegable lleva su tilde, y la fila abierta y la conversación, tinte y barra (OE1-A). | Choca su segmentado blanco con sombra, que es lo que el usuario no distinguía. La conversación con sólo el tinte tampoco cumple. La fila abierta coincide. |
| El editor de «Elegir de un catálogo» (E14 §9; E16 «Qué opciones ve cada persona»; front `formularios.md` §Prohibido y `Criterio-Dialogos`) | Las preguntas se abren en el lugar, sin zonas plegadas ni resumen. Su front prohíbe paneles o filas expandibles salvo en permisos, con excepciones escritas: «Avanzado», las zonas de la E20 y «Respuesta» y «Actividad» de la E15. | «Qué opciones ve cada persona» y «En WhatsApp» arrancan plegadas, con lo configurado en la banda, y se abren solas con un error o un aviso (ZP1-A). | Choca con esa regla del front; sería una excepción más, como las que ya tiene. |
| Armado y carga (E20 «Cómo se arma cada pregunta (86 y 87)» y «El panel»; `20-texto-diseno.md`; decisión 84) | Tres anchos: el catálogo, elegir una en fila y el texto corto de hasta 80 ocupan dos columnas. Elegir varias va en el renglón entero, con «Elegí hasta N», sin casillas en columnas. El panel va en una columna, sin zonas. «Mantener las respuestas» va sólo en el panel (84), y la página termina en «Enviada» con «Cargar otra». | Dos anchos, casillas en columnas, opciones en fila sin mirar el largo y el panel con sus zonas (AR1-A). La casilla también en la página, con otro `requestId` después de cada envío (AR2-A). | Los anchos salen de la tabla de su E20 y no de la 87, que sólo dice que el ancho sale del tipo. «Mantener» en la página choca con su 84. La clave de idempotencia aplica igual (§12, punto 7). |
| Renglones (E19 «Líneas» y decisión 54; E21; E25; decisión 76) | Un «Grupo de líneas» con hasta 6 columnas y un tope de 1 a 10, probado con Meta. En WhatsApp, los renglones se completan uno debajo del otro. El detalle los muestra en una tabla y el Excel suma una hoja por grupo. Quedan afuera las líneas dentro de líneas y filtrar por una columna de líneas, y las reglas no tienen condiciones sobre renglones. ARCA aparece sólo en la 76. | Decidido y sin programar: el tipo «Renglones» con mínimo y máximo (hasta 100 en la app), la casilla «Se carga varias veces», una tabla en la app, una pantalla por renglón en WhatsApp hasta 10 y «algún renglón con…» en reglas y filtros (§11). | Coinciden la tabla del detalle y la hoja del Excel. Chocan con su 54 la forma en WhatsApp, el mínimo, el tope y las reglas y los filtros sobre renglones. |
| El aviso a un rol (E21; decisiones 61 y 62; `docs/features/whatsapp.md`; `Campana-21`) | Sale por WhatsApp desde el número de la organización; fuera de las 24 h, con una plantilla aprobada que elige la regla (61), y sin plantilla, «No se pudo». Aparte, «Mandar por correo» a un rol (62). Las fallas son `NoConversation`, `WindowClosed`, `TemplateUnavailable`, `NotMember` y `RoleMissing`, y se ven por destinatario en Actividad, con «Reintentar», y en la campana de quien administra las reglas. | Sólo por WhatsApp, sin plantilla ni correo, con los requisitos del §12 (punto 1); el fallo se ve sólo en la actividad. | Allá está más resuelto: plantilla, correo y campana. Lo que suma la prueba es avisar en el editor, al elegir el rol, cuántas personas pueden recibirlo. |
| El detalle por aprobar sin el rol de la tarea (E21 «Aprobación de un nivel»; decisión 100; prueba B5) | La 100 cubre a quien decide sin ver respuestas. No está escrito el caso de quien ve respuestas pero no tiene el rol. `GET …/workflow` pide `formrecords.read` y devuelve `approval?`; `GET /{id}/approvals/{taskId}` pide `formapprovals.decide` y responde `Forms.Approval.NotFound` o `RoleRequired`. B5 prueba «sin el rol», pero no qué muestra el detalle. | El detalle toma la tarea como no disponible y muestra la decisión de sólo lectura (§7). | Escribirlo en su E21 antes de programar: con su contrato, el detalle puede pedir la tarea y recibir el 403 o el NotFound. |

**Lo que choca con decisiones que ya tomó allá y le toca decidir al usuario:**
- las solapas de «Por aprobar» (PL1-A), contra su 79 y la prohibición de pestañas para filtrar; allá la banda ya se fue de otra forma;
- «Mantener las respuestas» también en la página (AR2-A), contra su 84;
- los renglones (§11), contra su 54: una pantalla por renglón, el mínimo, el tope y las reglas sobre renglones;
- el filtro de una pregunta con opciones (PL2-A), contra la §3 de su hoja de ruta: «alguna de» y el criterio de «sin
  respuesta».

**Lo que choca con reglas de su front y también decide el usuario,** porque son reglas para todas las pantallas:
- la cantidad por opción en los filtros (PL2-A y FO1-A);
- los chips y aplicar cada cambio al momento (PL1-A);
- el segmentado con la elegida blanca y con sombra (OE1-A);
- las zonas plegables sólo en permisos (ZP1-A);
- `{pregunta}` escrita a mano en los mensajes (MD1-A).

## 14. Dónde está cada cosa en ArquitecturaBase

- **Reglas implementadas:** [formularios](../features/formularios.md), en sus partes de reglas (`{FieldId}` en el
  mensaje), «Por aprobar» (las dos solapas), la planilla («Filtros») y la carga desde la app (dos anchos y mantener lo
  cargado); [administración](../features/administracion.md), en los conteos de Usuarios.
- **Diseño elegido,** en `ArquitecturaBaseFront/docs/design/`:
  - `visual-baseline.md`: «Listados y filtros», «Lecturas», «Líneas entre columnas», «Opción elegida», «Lecturas en pasos
    y en dos columnas», «Los filtros de opciones, en toda la app» y «Comparaciones lado a lado»;
  - `formularios-whatsapp.md`: PL1-A, PL2-A, TL1-A y TL2-A, TL3-A, HI1-A, ZP1-A, MD1-A, LR1-A y LR2-A;
  - `carga-desde-la-app.md`: AR1-A y AR2-A.
- **Las reglas y sus piezas:** `ArquitecturaBaseFront/docs/guides/premisas-de-desarrollo.md` y `estandar-visual.md`.
- **Planes sin programar:** [renglones en los formularios](2026-11-03-renglones-en-formularios.md) y
  [ARCA · etapa 1](2026-11-03-arca-etapa-1-conexion.md).
- **Tableros:** el lienzo [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), en estas filas:
  - «Formularios · respuestas · dos solapas y un solo «Filtros»» y «el filtro de una pregunta con opciones: un combo
    simple»;
  - FO1, LR1, LR2 y «Toda la app · la opción elegida se distingue sin dudas»;
  - «Formularios · textos largos», del 1 al 3, con los historiales en la fila de la lista de reglas;
  - «Formularios · editor de una pregunta · zonas que se pliegan»;
  - «Formularios · reglas · el mensaje con datos»;
  - «Formularios · armado» y «La carga en la página · mantener lo cargado para la siguiente»;
  - «Formularios · renglones · 0» a «· 6».

  La elegida de cada fila es la que nombra este traspaso.
- **Commits:**
  - backend, de `37dea9ef` a `ad0ed362`: MD1-A (`37dea9ef` y `18c725a7`) y documentación;
  - front, de `23bfaa20` a `94b11e5f`.
- **Código de referencia:**
  - piezas comunes del front, en `src/shared/ui`: `numeric` de `TableHead`, `TableCell` y `DataTable`, `CellText`,
    `FilterPanel`, `Autocomplete` con `dropdown`, `SegmentedControl`, `RadioGroupField variant="cards"`,
    `DropdownMenuRadioItem`, `DataTable selectedRowKey`, `StepList`, `DataList`, `ComparisonTable`, `CollapsibleSection` y
    `FormField` con `action`;
  - formularios, en `src/features/business-forms/`: `ResponseFiltersMenu`, `ResponseFacetControl`,
    `ResponseDetailContent`, `ApprovalDecisionPanel`, `FormRulesSection`, `ConditionLines`, `DataTag`, `RuleMessageField`,
    `ruleMessage.ts`, `RevisionHistoryButton`, `RevisionHistoryPanel`, `draftReviewLines` y `CatalogSourceValues`;
  - otras áreas del front: `UsersFilterBar` (Usuarios), `FormComposer` y `WhatsAppChatPage` (chat) y `businessReviewGroups`
    (Información del negocio);
  - backend: `FormRulePolicy`, `FormActionMessagePolicy`, `FormRuleService` y `FormRuleReader` (el mensaje con
    `{FieldId}`), y `WhatsAppFormActionPolicy` (lo que exige el aviso por WhatsApp).

## 15. Prompt para el chat guía del multitenant

```text
Leé el traspaso 5 de ArquitecturaBase, en
C:\Users\ezequ\source\repos\ArquitecturaBase\docs\plans\2026-11-03-traspaso-multitenant-lecturas-y-filtros.md.
Cuenta lo que allá se decidió y se programó el 2/11 y el 3/11, siguiendo la prueba de punta a punta: una forma de
trabajar (cada cambio de diseño vale para toda la app), las tablas, cómo se lee lo guardado (pasos, dos columnas y
comparaciones lado a lado), cómo se marca lo elegido, los filtros (la planilla con dos solapas y un solo «Filtros», y un
filtro de opciones como desplegable simple), el detalle de una respuesta, las reglas (en pasos, el historial y el mensaje
con «Agregar dato»), el editor de una pregunta de catálogo y el armado y la carga desde la web. También trae dos diseños
sin programar: los renglones y la conexión con ARCA. Los commits todavía no están en GitHub: leelos en
C:\Users\ezequ\source\repos\ArquitecturaBase y ...\ArquitecturaBaseFront.

1. Si todavía no incorporaste el traspaso 4 (2026-11-02-traspaso-multitenant-ajustes-de-la-prueba.md, en la misma
   carpeta), hacelo junto con este: el 5 lo continúa. Evaluá los dos contra tu hoja de ruta, las etapas que nombra el §13
   y el texto de diseño de cada una. No son código para copiar (tu decisión 18) ni hacen falta para dibujar (tu decisión
   108).
2. La forma de trabajar del §2 y las reglas del §3 al §6 (tablas, lecturas, lo elegido y los filtros de opciones) valen
   para todas las pantallas. Proponé dónde fijarlas (tu decisión 43, docs/rules del front, docs/architecture/tema.md y
   los tableros «Criterio») y revisá los textos de diseño que hoy piden otra cosa.
3. Actualizá cada etapa y su texto de diseño con lo que se adopta del §7 al §10, y sumá la lista de «no repetir» del §12.
   El §13 dice qué dice hoy cada etapa y qué evaluar.
4. Los renglones del §11 están decididos y sin programar: decí en qué etapa o plan tuyo entrarían y qué cambia con tu
   modelo, sin programarlos. La conexión con ARCA sólo se nombra: evaluala cuando la sumes a tu hoja de ruta.
5. El §13 termina con los puntos que chocan con decisiones que ya tomaste y con reglas de tu front. No los cambies por tu
   cuenta: preguntale al usuario de a una pregunta, con las opciones y la recomendada marcada. Lo mismo vale para cualquier otra adaptación que
   tenga que decidir él.
6. Decile al usuario qué maquetas cambian. Como ejemplo para adaptar, tiene los tableros elegidos del lienzo de
   ArquitecturaBase (https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), en las filas que nombra el §14.
7. No programes nada. Commits chicos en español, en main, sin push.

Al terminar, contame qué cambió en tus specs, qué decisiones quedaron pendientes y qué maquetas hay que ajustar.
```

Este traspaso se retira cuando el multitenant lo haya incorporado a sus etapas.
