# Traspaso 17 · 05 · Los editores de catálogos y formularios, y los diálogos

Todo lo de este archivo está programado. Las fuentes son «Formularios y diálogos» y las secciones fechadas de
`ArquitecturaBaseFront/docs/design/visual-baseline.md` (botones y estados, leyendas y carga, una cosa una zona, el motivo de
lo apagado, avisos), `ArquitecturaBaseFront/docs/design/catalogos-mejoras.md`, `formularios-whatsapp.md` y
`acciones-del-encabezado.md`.

## 1. Cualquier editor

- **La banda:** el título; el estado, una sola vez (EP1-A: lo que dice la etiqueta no se repite debajo); a la vista, la tarea
  («Descartar» y «Guardar»; en un formulario, «Guardar borrador» y «Publicar»), con la principal última; ir a otro lado («Ver
  vista oficial») como su botón; y el ⋮ con lo demás: «Carpetas…», «Dar de baja» o «Reactivar» y, después de una línea,
  «Eliminar» en rojo. Lo que no se puede, apagado con su motivo adentro del menú («Tiene respuestas y no se puede
  eliminar.»).
- **Una cosa, una zona** (IA1-A): lo que se guarda junto va en una sola superficie, con sus campos en el orden en que se
  completan, cada botón al lado del dato que toca y lo que actúa sobre todo en la banda. Se parte en zonas sólo cuando son
  cosas distintas o pasos, nunca por tipo de control.
- **Los campos:** la etiqueta arriba; el ancho según lo que llevan; la ayuda que sirve, en la (i) junto a la etiqueta; el
  renglón de abajo, sólo para el error o un dato. Se valida al guardar y, después del primer error, en vivo mientras se
  corrige.
- **Guardar** se apaga sin cambios, sin decir nada al lado. Al salir con cambios, «¿Descartar los cambios?».
- **Lo apagado** da su motivo sobre el mismo control, si no es obvio (MA1-A), y el control no cambia de lugar ni de forma.
- **El conflicto** (otra persona guardó en el medio): una banda con «Revisar», que compara lado a lado sólo lo distinto (LR2-A),
  con «Usar la revisión actual» y «Conservar mis cambios». Conservar deja el borrador sobre la revisión nueva y no guarda solo.
- **Las esperas:** el botón con el aro y su gerundio, sin atenuarse (LC1-A); una confirmación queda abierta hasta la respuesta.
- **Los avisos** (AV1-A) siguen blancos; el tipo se ve en un filo de 3 px a la izquierda y en el ícono (información en gris,
  para no confundirla con el éxito), con la cruz adentro, a la derecha.

## 2. El editor de un catálogo

- **Las opciones** son una tabla paginada con «Agregar opción». Cada una se edita en un modal con la lista a la izquierda y la
  opción a la derecha (CO1-B), que escribe en el borrador; se guarda con el «Guardar» de la página. En la fila, sólo íconos
  (editar, restaurar, reactivar). «Texto» y «Código» van siempre en columnas separadas.
- **El orden de las opciones es un dato,** no una vista: «Ordenar por…», en el ⋮ de «Opciones» (OR1-A), abre un diálogo con la
  columna y el sentido. El orden queda pendiente hasta guardar y la página lo dice («Se ordenará por «Rubro», de la A a la Z, al
  guardar.», con «Cancelar orden»). Los empates conservan el orden anterior, lo vacío va al final y los números dentro de un
  texto se comparan por su valor (117 antes que 10030). Mientras hay movimientos sin guardar, «Ordenar por…» se apaga con su
  motivo. El encabezado de esta tabla no ordena: mostraría un orden que la tabla no tiene hasta guardar (por eso se descartó
  OR1-B).
- **Los campos** se editan en el modal «Campos de…», desde «Opciones»; cada uno dice su tipo y su papel, y «Nuevo» si no se
  guardó. Quitar uno en uso lista cada uso con «Ir a la pregunta»; cambiar su tipo muestra cuántos valores se convierten y
  cuántos no, antes de guardar.
- **Importar:** pegar una grilla o subir un archivo; «Revisar» muestra «Errores y avisos», «Altas», «Cambios» y «Bajas»; las
  bajas se confirman con su casilla («Sí, dar de baja 328 opciones»), porque importar reemplaza la lista; se puede aplicar sin
  las filas con errores. **Exportar** arma un Excel legible que se vuelve a importar sin diferencias, y la plantilla dice qué
  admite cada columna.
- Un catálogo se da de baja, no se borra, y se pone en carpetas con «Carpetas…».

**Lo que enseñó.** Las opciones se editaban en una fila vacía al pie de la tabla, con el panel debajo (CO1-B). Ordenar sólo de
la A a la Z, con 331 opciones en 17 páginas, obligaba a mover a mano (OR1-A). En la prueba integral quedaron para después: un
campo de texto que acepta 1000 caracteres en la pantalla y 300 en el servidor, sin decir el tope (O13), y una comparación de
revisión que no lista los cambios de las opciones (O15).

## 3. El editor de un formulario

- Las preguntas, en el orden del autor; los títulos de sección; las condiciones («Se muestra › Según otra respuesta»); los
  cálculos, con operaciones fijas; el catálogo con sus filtros según otra respuesta y «Elegir primero por un campo»; y «Dónde
  se completa» (en la app y por WhatsApp, o sólo en la app), al lado del prefijo.
- **Las zonas del editor de una pregunta** (ZP1-A) arrancan plegadas y la banda resume lo configurado («Sin filtros», «Como en
  el catálogo · Texto · Rubro»). Con un error, la zona se abre sola y lleva el foco al campo; con un aviso, también se abre.
- **Los mensajes con datos** se arman con «Agregar dato», que pone etiquetas (MD1-A), y «Así llega» muestra el resultado.
- **Las reglas** se leen en pasos (LR1-A): «Cuándo», «Si» y «Hacer, en este orden».
- **Publicar** abre un diálogo que comprueba antes: «Problemas», que frenan, y «Avisos», que no, aparecen sólo si hay, cada
  zona con su (i); «Ir a la pregunta» cierra el diálogo y marca la pregunta; «Qué se publica» va en dos columnas; los idiomas,
  sólo cuando falta uno.

**Lo que enseñó.** Los datos de un mensaje se escribían entre llaves, y nadie iba a saber que había que hacerlo (MD1-A). En la
prueba integral quedaron para después: un filtro guardado sin elegir la respuesta daba 400 y marcaba dos combos (O16) y la
vista previa no aplicaba las condiciones (O17).

## 4. Los diálogos

- **Breve y contextual es un diálogo; largo, seccionado o para compartir, una página.**
- Lo que el diálogo dice que va a hacer (cuántas, con qué filtros, a quién, qué archivo) va en dos columnas, rótulo y valor,
  nunca en párrafos ni viñetas (RF2).
- «Cancelar» va junto a la principal, que va última; una destructiva, separada a la izquierda. Quitar va en rojo antes de
  confirmar.
- **Lo que no se deshace o sigue solo** se confirma con el foco en «Cancelar» (AP1-A): el usuario aprobó una respuesta y, siete
  segundos después y sin querer, la siguiente.
- **Dar de baja** dice en dos columnas qué deja de pasar y qué se conserva; **eliminar**, en una frase, con «Eliminar
  formulario» en rojo.
- **Elegir varios adentro de un diálogo que guarda** es la pieza múltiple con «Guardar» ([03](03-filtros-y-elegir.md) §2).

**Lo que enseñó.** El diálogo de exportar se programó distinto de su tablero, y las reglas se aplicaban de a una: por eso la
lista de chequeo se pasa antes de mostrar el tablero y otra vez antes de programar.

## 5. Para el multitenant

- **Coincide:** sin textos que expliquen (tu `pantallas-y-ui.md`), el motivo de lo apagado en el tooltip, un botón habilitado
  o apagado sin nada al lado, sin cantidades en el encabezado de una sección, el error al tocar guardar y el banner del 409.
- **La espera de un botón:** el tuyo se ve apagado, con el aro y su mismo texto; acá, el aro y el gerundio sin atenuarse (la
  pregunta 5 del 15).
- **Las opciones de un catálogo:** tu tabla las edita en el lugar, con un ⋮ por fila y «Editar» adentro; acá, íconos en la fila
  y el modal «lista y opción» (las preguntas 21 del 15 y 70 del 16).
- **Los campos:** tu E13 los edita en una tabla en el lugar, sin diálogo; acá, en el modal «Campos de…» (la pregunta 22 del
  15). Tus propias reglas dicen que una lista se lee y que editar abre un diálogo, y prohíben una tabla adentro de un diálogo:
  las tablas editables de tu E12 y tu E13 quedan como excepción. Con los modales de acá, la excepción no hace falta.
- **El orden de las opciones:** tu E12 sube, baja u ordena de la A a la Z; «Ordenar por…» por cualquier columna está en el
  T13/01 §9.
- **Los avisos:** el verde que se cierra solo y el rojo con ✕ de tu regla contra los avisos blancos con su filo de color
  (AV1-A, en el T13/05).
