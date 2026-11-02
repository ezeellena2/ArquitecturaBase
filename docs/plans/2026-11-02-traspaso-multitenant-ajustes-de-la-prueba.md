# Ajustes de la prueba completa: traspaso 4 al multitenant (2026-11-02)

Autor: el chat guía de ArquitecturaBase. Destino: ArquitecturaBaseMutitenant, para sus etapas **E12 · Catálogos**,
**E13 · Campos**, **E14 · Formularios**, **E15 · Respuestas**, **E16 · Datos de las personas** y **E21 · Reglas y
aprobación**, y para el texto de diseño de todas (`docs/plans/formularios/` del multitenant).

Entre el 2026-10-31 y el 2026-11-01 el usuario probó la aplicación de punta a punta con un caso real:
- catálogos con campos;
- un formulario con filtros por persona, reglas, aprobación y PDF;
- la carga desde la app;
- la aprobación con otra cuenta;
- la planilla y el Excel.

Cada cosa que no le cerró se dibujó en tableros, eligió una y se programó. Además fijó dos reglas de diseño para todas las
pantallas. Este traspaso cuenta:
- qué decidió el usuario;
- cómo quedó;
- qué se encontró en la prueba;
- qué tiene que evaluar el multitenant.

**Como los anteriores, no es código para copiar** (decisión 18 de su hoja de ruta). Tampoco hace falta para dibujar
(decisión 108): los tableros de acá son un ejemplo para adaptar con sus piezas y su decisión 43.

## 1. En pocas palabras

- **Dos reglas de diseño para todas las pantallas** (§2):
  - sin leyendas explicativas: la explicación que sirve va en la (i) con su tooltip;
  - toda acción que tarda muestra que trabaja.

  Se aplicaron en unas 50 pantallas y diálogos.
- **Catálogos** (§3):
  - las opciones se agregan y editan en un modal, como los campos;
  - el catálogo de un campo se elige en un combo y la clave sale sola del nombre;
  - importar muestra lo pegado en una grilla con las columnas del catálogo;
  - el Excel exportado se lee como en pantalla y vuelve sin cambios.
- **Datos de las personas** (§3): el mismo combo y la misma clave automática.
- **Formularios** (§4): el catálogo de una pregunta usa el mismo combo, y el diálogo de publicar en WhatsApp quedó por zonas
  y dice el límite concreto de un texto largo.
- **Reglas y aprobación** (§5):
  - «Adjuntar el PDF» ya no se desmarca solo y dice cómo arreglarse;
  - cambiar el tipo de una acción conserva lo escrito;
  - «Aprobar» pide confirmar con el número.
- **La prueba** (§6): el circuito completo anduvo. Lo que se encontró sirve como lista de «no repetir».
- **El código todavía no está en GitHub:** los commits son locales. Desde esta PC se leen en
  `C:\Users\ezequ\source\repos\ArquitecturaBase` y `C:\Users\ezequ\source\repos\ArquitecturaBaseFront` (§8).

## 2. Dos reglas de diseño para todas las pantallas (2026-10-31)

El usuario las fijó probando los catálogos, con sus palabras: nada de leyendas que expliquen cosas por todos lados, porque
para eso está la (i). El 2026-11-01 aprobó, pantalla por pantalla, qué hacer con cada leyenda y cada espera: unas 240 de cada
una, en unas 50 pantallas y diálogos. Lo aprobado reemplaza decisiones anteriores; por ejemplo, de la confirmación de publicar
se fueron «Qué queda fijo» y «Qué va a pasar».

| Regla | Cómo quedó |
| --- | --- |
| **Sin leyendas explicativas** | La información es etiqueta y valor o control. Una explicación que sirve va en la (i) con su tooltip, junto a la etiqueta o el título, y es parte de la descripción accesible del control. El renglón de abajo es sólo para el error o para un dato (el texto original en el otro idioma, un valor). Lo que no ayuda a decidir se saca, sin pasarlo a la (i). |
| **Toda acción que tarda muestra que trabaja** (tableros LC1-A y LC2-A) | El botón pone un aro y su gerundio («Guardando…»), sin atenuarse ni aceptar otro clic. Una confirmación queda abierta hasta la respuesta. Una tabla que vuelve a leer atenúa sus filas y corre una línea arriba. Nunca alcanza con apagar el botón. |
| **Lo apagado dice por qué** | El motivo de un botón, una casilla o la × de un diálogo apagados va en su tooltip. Un segmentado se apaga mientras su dueño trabaja. |

Hubo una excepción pedida por el usuario: «Listo» del modal de campos avisa que hay que guardar el catálogo (§3).

## 3. Catálogos y datos de las personas (su E12, E13 y E16)

### Las opciones en un modal (CO1-B, 2026-10-31)

«Agregar opción» ya no suma una fila vacía al pie de la tabla con un panel debajo. Abre un modal igual al de «Campos de…»:
- la lista de opciones a la izquierda, con número o «—», texto, «Nueva» y código o «Sin código»;
- la opción elegida a la derecha, con su posición («Opción 2 de 5» o «Nueva opción») y «Quitar opción» en rojo en la banda;
- las filas de la opción: Texto con su contador, Código con «Generar desde el texto», los campos del catálogo, Valor, Detalle
  y, sólo en una guardada, Estado «Activa | De baja».

Cómo se comporta:
- **Guardar.** El modal escribe en el borrador y se guarda con «Guardar» de la página.
- **Cerrar.** «Cancelar» o Esc devuelven todo como estaba al abrirlo. «Listo» conserva todo, menos una opción nueva sin nada
  escrito.
- **Errores.** Si «Guardar» encuentra un error, el modal se abre en la primera opción con problemas y la lista las marca con
  un triángulo.
- **Quitar.** Quitar una opción nueva no pregunta. Una guardada pide confirmación y queda con «Se elimina al guardar» y
  «Restaurar».
- **Celular.** El modal ocupa la pantalla: primero la lista y, al tocar una opción, la opción con «Volver a la lista».
- **Escritorio.** La tabla sigue editable en el lugar.

### Los campos de un catálogo (2026-10-31)

- **El catálogo de un campo se elige en un combo.** Al tocarlo muestra los primeros catálogos activos («Proveedores · 6
  opciones») y al escribir los filtra.
- **La clave se completa sola desde el nombre.** «Proveedor desde» pasa a `proveedor_desde`. Las reglas:
  - snake_case en minúscula, sin tildes, con `_` en lugar de lo que no es letra ni número, sin repetirlo ni dejarlo en las
    puntas;
  - empieza con una letra y tiene hasta 40 caracteres;
  - si ya existe en el catálogo, suma `_2`, `_3`…;
  - deja de seguir al nombre cuando se edita a mano, y una clave guardada no cambia.

  Antes, la clave viajaba como se escribía y el servidor rechazaba lo que no cumplía su patrón.
- **Las columnas nuevas siguen apagadas hasta guardar,** con el motivo en el tooltip de cada celda. Mientras haya campos
  nuevos, «Listo» avisa: «Guardá el catálogo para cargar los valores de los campos nuevos.».
- **Sin leyendas.** «Papel» («Filtro | Dato») ya no repite debajo lo que dice su (i), y la tabla perdió «hay más columnas a la
  derecha»: la barra de desplazamiento alcanza.
- **La barra de opciones deja sólo «Importar».** «Pegar desde Excel» pasó a ser un origen del diálogo de importación.

### Importar opciones (IM1-A, IM2-A e IM3-A, 2026-10-31)

- **IM1-A · Las dos zonas a la vista.** Es un solo diálogo, de arriba abajo:
  - «Origen» tiene «Desde» («Subir un archivo | Pegar desde Excel») y, para un archivo, su nombre con «Elegir archivo» o
    «Cambiar»; la plantilla va en la banda;
  - después de «Revisar», «Revisión» queda debajo, con el archivo, la hoja, las filas leídas y las que no cambian, y una
    pestaña por tipo de cambio con su cantidad;
  - «Antes de aplicar» aparece sólo si hay algo para confirmar.

  Se fueron la descripción, los pasos numerados y los siete chips de cantidades.
- **IM2-A · La espera a la vista.** «Revisar» pasa a «Revisando…» y la revisión aparece enseguida como un esqueleto. Al
  aplicar, «Aplicando…» deja la revisión atenuada, y Cancelar y la × quedan apagados con «Esperá a que termine de aplicar.».
- **IM3-A · Una grilla con las columnas del catálogo.** «Pegar desde Excel» muestra una grilla con los encabezados del
  catálogo en el orden de la plantilla.
  - **Columnas.** Sin encabezados, las filas van en ese orden. Si lo copiado los trae (nombre o clave), cada columna va a su
    lugar y esa fila no se importa.
  - **Columna ajena.** Una columna que no es del catálogo queda al final, en rojo, con su motivo, y no se manda.
  - **Revisión.** Lo pegado se revisa como un archivo, pero sólo con las columnas pegadas: una columna opcional que no se pegó
    conserva sus valores.

### Exportar opciones legibles (2026-10-31)

El Excel exportado se lee como en pantalla y se reimporta sin diferencias:
- la columna del valor dice «Valor (tipo)», como la plantilla;
- entero y decimal van como números de Excel, con separador de miles y tantos decimales como tenga el valor;
- la fecha va como fecha de Excel, con el formato del idioma del menú.

Una celda numérica guarda 15 cifras significativas. Un valor más largo, o una fecha anterior a 1900, va como su texto
canónico, que se reimporta sin ambigüedad. La garantía de volver sin cambios es para subir el archivo: pegar celdas
copiadas lee el texto que muestra Excel, en la cultura del menú.

### Datos de las personas (2026-10-31)

«Agregar dato» y «Editar dato» cambiaron con el mismo criterio:
- el catálogo de un dato se elige en el mismo combo que el de un campo, sin buscador, lista paginada ni descripción del
  catálogo elegido;
- la clave de un dato nuevo sale sola del nombre, con las mismas reglas, sin repetir la de otro dato;
- las leyendas del diálogo pasaron a su (i) o se sacaron;
- guardar dice «Guardando…» y la relectura, «Volviendo a cargar…».

## 4. Formularios: el catálogo de una pregunta y publicar en WhatsApp (su E14)

### El catálogo de una pregunta (2026-10-31)

«Catálogo», en una pregunta «Elegir de un catálogo», es el mismo combo:
- ofrece los catálogos de los dos tipos;
- muestra apagados, con su motivo, los dados de baja y los que no se pueden usar;
- se fueron el desplegable con «Lotes · Texto · Escritas a mano · 193 opciones.», el buscador con su paginado y «Ver el
  catálogo».

«Las opciones se preparan al mandar el formulario.» pasó a la (i) de «Catálogo», y la leyenda de «Qué opciones ve cada
persona», a la (i) de su banda. Los filtros fijos y los filtros según quien lo recibe no cambiaron.

### Publicar en WhatsApp (PU1-A, 2026-11-01)

El usuario dijo que la confirmación tenía demasiada explicación y texto suelto. De tres tableros (A por zonas, B una lista
con íconos, C pestañas) eligió **A**:
- **Zonas.** El diálogo son zonas con líneas entre filas, sin descripción ni párrafos.
  - «Problemas» (lo que frena) y «Avisos» (lo que conviene revisar) aparecen sólo si hay alguno, cada fila con su ícono, qué
    es, el detalle y «Ir a la pregunta».
  - «Qué se publica» va siempre, en filas: borrador guardado, preguntas agregadas, cambiadas y quitadas respecto de la versión
    publicada, idiomas y, mientras está libre, el prefijo.
- **El límite, en concreto.** Un texto que pasa su límite lo dice: «Hasta 20 caracteres; tiene 26.». Si no es el título,
  agrega «La ayuda: …» o «La opción «…»: …». El límite viene del compilador del canal, y el largo sale del borrador guardado.
- **La ayuda, en la (i).** Lo que el usuario aprobó sacar no va ni ahí.
- **Publicar apagado** dice su motivo en el tooltip: el problema, la comprobación vencida, una publicación en curso o la
  publicación no disponible.
- **Las esperas.** «Comprobando…» arma «Qué se publica» con un esqueleto, y «Consultar publicación» dice «Consultando…».

### Los límites de WhatsApp Flow

Son los del compilador de acá (`WhatsAppFlowCompiler`). En la prueba frenaron la primera publicación (§6):

| Texto | Hasta |
| --- | --- |
| Título de una pregunta de número, texto o lista | 20 caracteres |
| Título de sí o no, o de elegir varias | 30 |
| Título de fecha | 40 |
| Título de foto o documento | 80 |
| Ayuda | 80; 300 en archivos, sí o no y elegir varias |
| Nombre del formulario | 80 |
| Botón final | 35 |
| Cada opción | 30 |
| Opciones de una pregunta | 20 en elegir varias; 50 en las demás |
| Preguntas por formulario | 40 |
| Componentes por pantalla | 50 |

Una pregunta de lista con ayuda no se puede representar.

## 5. Reglas y aprobación (su E21)

### «Adjuntar el PDF» y cambiar el tipo (RP1-A, 2026-11-01)

Armando la regla «Se aprueba una respuesta», el usuario dejó el mensaje que adjuntaba el PDF arriba de «Armar el PDF». No
entendió «Agregá Armar el PDF antes de adjuntarlo.», porque la acción estaba, sólo que más abajo. Además, la casilla se le
desmarcó sola al cambiar el tipo de una acción. De tres tableros (A avisa al lado, B marcar trae el PDF, C no deja romperlo)
eligió **A**:
- «Adjuntar el PDF» nunca se desmarca solo: ni al mover, quitar o cambiar una acción, ni al cambiar el evento.
- Si «Armar el PDF» falta o está después del mensaje, debajo de la casilla aparece en el momento «Para adjuntarlo, «Armar el
  PDF» tiene que ir antes de este mensaje.».
- Debajo, un botón lo arregla:
  - «Subir "Armar el PDF"» pone el primero que está después justo antes del mensaje;
  - si no hay ninguno, «Agregar "Armar el PDF" antes» lo agrega ahí.

  El foco vuelve a la casilla.
- Con otro evento, la casilla sin marcar se apaga, con el motivo en su tooltip: «El PDF se adjunta sólo cuando se aprueba una
  respuesta.».
- **Cambiar el tipo de una acción conserva lo escrito:** el mensaje, el rol y el adjunto vuelven si se elige otra vez el tipo
  anterior. Al guardar, cada dato va sólo con el tipo que lo usa.

### Confirmar antes de aprobar (AP1-A, 2026-11-01)

El usuario aprobó una respuesta y, siete segundos después, también la siguiente, sin querer. La pantalla no pasa sola a la
siguiente. Pero después de decidir, el foco queda en el panel, y ↓ (que recorre las respuestas por aprobar) abre la
siguiente con «Aprobar» en el mismo lugar. De tres tableros (A confirmar, B volver a la lista, C unos segundos para
deshacer) eligió **A**:
- «Aprobar» abre un diálogo como el de Rechazar: «¿Aprobar APL-000003?», con quién la cargó y cuándo;
- el foco empieza en «Cancelar», así que Enter no aprueba;
- mientras manda, el diálogo espera abierto con «Aprobando…».

### El circuito, probado

El formulario de la prueba tenía tres reglas:

| Evento | Condición | Acciones |
| --- | --- | --- |
| Se recibe una respuesta | Hectáreas aplicadas, 50 o más | Pedir aprobación al rol «Agrónomo» |
| Se aprueba una respuesta | — | Armar el PDF; mandarle un mensaje a quien la cargó, con el PDF |
| Se rechaza una respuesta | — | Mandarle un mensaje a quien la cargó |

Resultado:
- **Sin aprobación.** La respuesta de 30 ha quedó «Recibida».
- **Por aprobar.** Las de 53, 62 y 120 ha quedaron «Por aprobar», con una tarea para el rol.
- **Quién decide.** Decidió otra cuenta, porque quien carga no decide lo suyo: «No podés aprobar ni rechazar tu propia
  respuesta.».
- **Al aprobar.** Se armó el PDF, con las preguntas, las fotos y el bloque de la aprobación.
- **El mensaje.** Quedó «No aplica», porque quien cargó no tenía teléfono confirmado.
- **Al rechazar.** Quedaron el motivo y quién decidió, y no se armó PDF.
- **Lo demás.** Anduvieron el historial, la planilla con las columnas de los catálogos, los filtros y el Excel.

## 6. Lo que se encontró en la prueba (no repetir)

1. **Lo que va en la versión y lo que se guarda aparte.** Las preguntas y sus filtros van en una versión nueva. Las reglas y
   el diseño del PDF tienen guardado y revisión propios, y valen sin publicar. En la prueba, la versión 1 salió sin los
   filtros de «Lote», y además las reglas y el PDF no se habían guardado. El editor tiene que dejar claro qué se publica y
   qué se guarda aparte.
2. **La unidad en la etiqueta choca con el límite de WhatsApp.** «Hectáreas aplicadas (ha)» tiene 24 caracteres, y el título
   de una pregunta de número admite 20. La decisión 9 del traspaso 3 pone la unidad en la etiqueta. Acá se ve recién al
   publicar, ahora con el límite concreto; conviene evaluar avisarlo mientras se escribe.
3. **Un error tiene que decir qué hacer en cada caso.** «Agregá Armar el PDF…» se leía igual si faltaba la acción o si
   estaba más abajo (§5).
4. **Nada se desmarca ni se borra sin avisar.** Ni al cambiar el tipo, ni al mover o quitar una acción (§5).
5. **Una decisión que no se deshace pide confirmar.** Más todavía cuando ↑ ↓ cambian de respuesta con el foco en el panel
   (§5).
6. **Un mismo selector de catálogo en todos lados.** Antes, campos, datos de las personas y preguntas tenían tres selectores
   distintos (§3 y §4).
7. **Un select toma el ancho de su opción más larga.** Los del horario de «Información del negocio» tenían ancho fijo y
   cortaban «Sin definir» y «Elegir».

## 7. Adaptación a sus etapas

Leído en el multitenant en `c339b58d`, con la hoja de ruta hasta la decisión 108. El traspaso 3 ya está incorporado a su
E20 (decisiones 83 a 98).

| Tema | Su etapa dice hoy | En ArquitecturaBase | Qué evaluar |
| --- | --- | --- | --- |
| Opciones de un catálogo (E12) | Tabla editable con «+ Agregar opción» en la banda y un ⋮ por fila con «Editar»; en el celular, el editor por opción (`12-catalogos.md`, §10). Su ficha del front dice «editar/agregar abre diálogo». | Modal «lista y opción», igual al de «Campos de…» (CO1-B). En escritorio la tabla sigue editable en el lugar. | El modal coincide con su ficha del front. Si lo adoptan, deja de hacer falta el estado «opción sin completar» de la fila vacía. |
| Campos de un catálogo (E13) | La clave sale sola del nombre, fija y oculta. El catálogo viene dentro del tipo «De un catálogo: …». «Campos» es una tabla editable sin diálogo, y la columna nueva queda grisada con el motivo en el tooltip. | Modal «Campos de…». La clave se ve y sigue al nombre hasta que se edita. El catálogo se elige en un combo con búsqueda, y «Listo» avisa que hay que guardar. | La clave coincide. Sin modal, el aviso de «Listo» no aplica, y su decisión 43 prohíbe «Cambios sin guardar»: alcanza con el tooltip. Conviene evaluar el combo con búsqueda para elegir el catálogo cuando hay muchos. |
| Importar y pegar (E12) | «Pegar desde Excel» e «Importar» van por separado en el ⋮. Pegar agrega e importar reemplaza. `FRM-Catalogo-Importar` revisa (Errores, Altas, Cambios y Bajas) y aplica, con «Aplicar sin las N filas con errores». | Sólo «Importar» en la barra. Un diálogo con «Origen» (archivo o pegar) y la revisión debajo (IM1-A), con las esperas a la vista (IM2-A) y lo pegado en una grilla con las columnas del catálogo (IM3-A). Pegar sigue agregando y el archivo, reemplazando. | Lo nuevo es la grilla (IM3-A): columnas por nombre o clave, la ajena en rojo y fuera del envío, y la columna opcional que no se pegó conserva sus valores. Evaluar juntar los dos orígenes en un diálogo. |
| Exportar opciones (E12) | C10 escribe el valor canónico como texto y se reimporta sin diferencias. | El Excel se lee como en pantalla, con números y fechas de Excel con su formato, y se reimporta sin diferencias. Guarda 15 cifras significativas; lo que no entra va como texto canónico. | Cambian el formato y su test, no la garantía. Fue un pedido del usuario al probar el export. |
| Datos de las personas (E16) | Un campo de nombre, el tipo y el catálogo, sin búsqueda. No hay clave: los valores van por la identidad del dato. | El catálogo se elige en el mismo combo, la clave sale del nombre, no hay leyendas y guardar dice «Guardando…». | La clave no aplica. Adoptar el mismo combo de catálogo que en campos y preguntas. |
| Catálogo de una pregunta (E14) | El selector usa el listado de la E12 (activos, paginado y con búsqueda) y pide `formcatalogs.read`. Los filtros coinciden. | El mismo combo que en campos y datos. Los dados de baja y los que no se pueden usar se ven apagados con su motivo, y las leyendas pasaron a la (i). | Un solo combo en los tres lugares (§6, punto 6). |
| Publicar en WhatsApp (E14) | «Publicar la versión N en WhatsApp», con qué cambia y qué queda fijo. Los límites son los mismos y se informan al comprobar, con su `FieldId`; no hay contador en el editor. | PU1-A: zonas «Problemas», «Avisos» y «Qué se publica», con el límite en concreto («Hasta 20 caracteres; tiene 26.»). Se fueron «Qué queda fijo» y «Qué va a pasar»; queda sólo el prefijo que se fija. | Adoptar las zonas y el texto del límite. Evaluar un aviso mientras se escribe el título, por la unidad en la etiqueta (§6, punto 2): acá tampoco existe. |
| «Adjuntar el PDF» (E21) | Adjuntar exige una acción anterior que lo arme en la misma regla. Con aprobación, el PDF se arma al aprobar y, sin ella, al recibir (su decisión 106). Hasta 10 acciones en orden. No dice cómo se reordenan ni qué pasa al cambiar el tipo. | RP1-A: el motivo aparece junto a la casilla, con un botón que sube o agrega «Armar el PDF». Nada se desmarca solo, y cambiar el tipo conserva lo escrito. Acá el adjunto vale sólo al aprobar. | Adoptar el motivo con su arreglo, y que nada se borre ni se desmarque solo. Con su 106, la casilla también vale al recibir sin aprobación, así que cambia cuándo se apaga y qué motivo dice. |
| Aprobaciones (E21) | Quien cargó no decide, rechazar pide motivo y ↑ ↓ recorren la cola en la vista rápida. No dice si aprobar pide confirmación. | AP1-A: «Aprobar» pide confirmar con el número, quién la cargó y cuándo, con el foco en «Cancelar». | Tienen la misma navegación con ↑ ↓, así que corren el mismo riesgo de aprobar la siguiente sin querer. |
| Reglas de diseño (el texto de diseño de cada etapa y su decisión 43) | «Sin rótulos que expliquen», y la ⓘ abre un aviso (`ImportantNotice`). Según su 43, el botón que trabaja queda grisado con su texto y un aro, nunca aparece «Cambios sin guardar», y una opción de menú grisada lleva el motivo en el tooltip. En tablas y confirmaciones, «cargando» no dice cómo se ve. | Sin leyendas: la ayuda va en la (i), con un tooltip. LC1-A: el botón muestra el aro y su gerundio («Guardando…») sin atenuarse. La confirmación queda abierta hasta la respuesta. Una tabla que relee atenúa sus filas, con una línea que corre. Lo apagado dice por qué en su tooltip. | Lo de las leyendas coincide; cambia sólo tooltip o aviso. La espera del botón choca con su 43. Falta definir cómo se ve la espera en tablas y confirmaciones. |

**Lo que choca con decisiones que ya tomó allá y le toca decidir al usuario:**
- la espera de un botón: grisado con su texto (su 43) o el aro con el gerundio sin atenuarse (LC1-A);
- la ⓘ: un aviso o un tooltip;
- las opciones de un catálogo: la tabla con ⋮ (su E12) o el modal (CO1-B), que coincide con su ficha del front;
- los campos de un catálogo: la tabla en el lugar (su E13) o el modal «Campos de…»;
- el export: el texto canónico (su C10) o el Excel legible.

## 8. Dónde está cada cosa en ArquitecturaBase

- **Reglas implementadas:** [formularios](../features/formularios.md), en sus partes de catálogos Manual (pegar, importar y
  exportar), datos de las personas, publicación (`WhatsApp.Flow.TextTooLong`), reglas y «Por aprobar».
- **Diseño elegido,** en `ArquitecturaBaseFront/docs/design/`:
  - `catalogos-mejoras.md`: las opciones en un modal, los ajustes de «Campos de…» y el diálogo de importación;
  - `catalogos-campos-extra.md`;
  - `formularios-whatsapp.md`: PU1-A, RP1-A y AP1-A;
  - `visual-baseline.md`: «Leyendas y carga».
- **Las dos reglas y sus piezas:** `ArquitecturaBaseFront/docs/guides/premisas-de-desarrollo.md` y `estandar-visual.md`.
- **Tableros:** el lienzo [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), en estas filas:
  - «Catálogos · agregar una opción en un modal»;
  - «Catálogos · importar opciones»;
  - «Leyendas y carga»;
  - «Formularios · publicar en WhatsApp»;
  - «Formularios · reglas · "Adjuntar el PDF"»;
  - «Formularios · Por aprobar».

  Los elegidos llevan el sello «Elegida».
- **Commits:**
  - backend, de `0cb73d4e` a `cfbb4c13`: documentación y el export legible (`ed5b964c` y `064bc821`);
  - front, de `8239f0c7` a `29c49be5`.
- **Código de referencia:**
  - piezas comunes del front, en `src/shared/ui`: `InfoTip`, `help` de `FormField`, `pending` de `Button`, `IconButton` y
    `ConfirmDialog`, `refreshing` de `Table` y `DataTable`, `disabledReason` de `CheckboxField`, `PasteGrid` y `Autocomplete`
    con `dropdown`;
  - formularios, en `src/features/business-forms/`: `CatalogItemsDialog`, `CatalogRow`, `CatalogFieldsDialog`,
    `CatalogImportDialog`, `PublicationCheckDialog`, `FormRulesSection`, `rules.ts` y `ApprovalDecisionPanel`;
  - backend: `FormCatalogExportPolicy`, `FormCatalogExportService`, `FormCatalogExportSheet`, `ClosedXmlFormWorkbookWriter` y
    `WhatsAppFlowCompiler`.

## 9. Prompt para el chat guía del multitenant

```text
Leé el traspaso 4 de ArquitecturaBase, en
C:\Users\ezequ\source\repos\ArquitecturaBase\docs\plans\2026-11-02-traspaso-multitenant-ajustes-de-la-prueba.md.
Cuenta lo que allá se decidió y se programó el 31/10 y el 1/11, probando la app de punta a punta: dos reglas de diseño
para todas las pantallas, catálogos (opciones en un modal, campos, importar y exportar), datos de las personas, publicar
en WhatsApp, «Adjuntar el PDF» en las reglas y confirmar antes de aprobar. Los commits todavía no están en GitHub: leelos
en C:\Users\ezequ\source\repos\ArquitecturaBase y ...\ArquitecturaBaseFront.

El usuario está dibujando tus maquetas de estas pantallas: lo que adoptes tiene que quedar en tus specs antes de que
siga dibujando y antes de programar.

1. Evaluá el traspaso contra tu hoja de ruta y las etapas que toca: E12, E13, E14, E15, E16 y E21, y el texto de diseño
   de cada una. No es código para copiar (tu decisión 18) ni hace falta para dibujar (tu decisión 108).
2. Las dos reglas de diseño del §2 (sin leyendas explicativas, con la ayuda en la (i); toda acción que tarda muestra que
   trabaja; lo apagado dice por qué) valen para todas las pantallas. Proponé dónde fijarlas (tu decisión 43 y docs/rules
   del front) y revisá los textos de diseño que hoy piden leyendas debajo de los campos o esperas sin señal.
3. Actualizá cada etapa y su texto de diseño con lo que se adopta del §3 al §5, y sumá la lista de «no repetir» del §6.
   El §7 dice qué dice hoy cada etapa y qué evaluar.
4. El §7 termina con cinco puntos que chocan con decisiones que ya tomaste. No los cambies por tu cuenta: preguntale al
   usuario de a una pregunta, con las opciones y la recomendada marcada. Lo mismo vale para cualquier otra adaptación que
   tenga que decidir él.
5. Decile al usuario qué maquetas cambian. Como ejemplo para adaptar, tiene los tableros elegidos del lienzo de
   ArquitecturaBase (https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), en las filas que nombra el §8.
6. No programes nada. Commits chicos en español, en main, sin push.

Al terminar, contame qué cambió en tus specs, qué decisiones quedaron pendientes y qué maquetas hay que ajustar.
```

Este traspaso se retira cuando el multitenant lo haya incorporado a sus etapas.
