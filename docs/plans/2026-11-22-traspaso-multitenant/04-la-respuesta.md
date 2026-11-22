# Traspaso 17 · 04 · Cómo se carga, se lee y se exporta una respuesta

Es donde más se notaba que un formulario dinámico «queda horrible» si cada pantalla lo dibuja a su manera. La regla de fondo
es la del [01](01-principios-y-lo-aprendido.md) §1.1: el autor decide las preguntas y su orden, y la app decide la forma por
el tipo de cada una, igual al cargar, al leer, en la planilla, en el Excel y en el PDF. Las fuentes son
`ArquitecturaBaseFront/docs/design/carga-desde-la-app.md`, el plan `docs/plans/2026-11-22-respuesta-como-formulario.md` y
«Planilla, detalle y Excel» y «El bloque de renglones en la lectura» de `docs/features/formularios.md`.

## 1. La carga

Programada (CW, AR1-A y AR2-A, del 30/10 al 5/11).

- **Las zonas:** «Datos», con lo que va antes del primer título; una por cada título de sección; y «Fotos y documentos», al
  final. Se pliegan, arrancan abiertas y, plegadas, dicen «N de M respondidas». Al enviar con errores, la zona que los tiene se
  abre sola y el foco va a la primera pregunta con error.
- **La grilla:** hasta tres preguntas por renglón según el ancho de la zona (tres desde 46 rem, dos desde 30, una debajo), en
  el orden del autor y sin reordenar; la que no entra baja de renglón, con una línea entre renglones.
- **Dos anchos** (AR1-A): ocupa una columna lo que tiene un solo valor (número, fecha, sí o no, texto corto de hasta 80,
  elegir una y elegir de un catálogo, de una o de varias); ocupa el renglón entero lo que necesita espacio (texto largo, texto
  corto sin largo máximo o de más de 80, elegir varias con opciones propias, foto, documento y el bloque de ítems).
- **Por tipo:** el número y la fecha, angostos; el texto largo, de 2 a 6 renglones según su largo máximo; la unidad, en la
  etiqueta («Densidad (kg/ha)»), sin sufijo adentro; el catálogo, con búsqueda en el servidor y «Mostrar 50 más», a pantalla
  completa en el celular; elegir primero por un campo y las listas que dependen de otra respuesta; la ubicación, que pide el
  permiso recién al tocar «Usar mi ubicación»; y el cálculo, en gris, de sólo lectura, con su fórmula en la (i).
- **Elegir, en la pieza única** (22/11): el Sí o No (sin nada marcado al empezar), las opciones en fila y elegir varias pasan
  a la pieza ([03](03-filtros-y-elegir.md) §2). Se está programando en la copia de la pieza; todavía no está en la app.
- **El bloque de ítems:** una tabla, un ítem por fila, donde la zona tiene columnas; un recuadro por ítem en el panel y en el
  celular.
- **Página o panel:** «Nueva respuesta» es una página; desde la planilla, en la compu, un panel al costado. Los dos tienen
  «Mantener las respuestas para la siguiente» (AR2-A).
- **Las condiciones:** lo que una condición oculta no se ve ni se manda.
- **Los números** se leen con la región del idioma: en español, «1.500» se puede leer de dos maneras y se marca antes de
  mandar. El cálculo en vivo tiene que leerlos igual (O19, para después de la prueba).

## 2. La lectura: la respuesta como el formulario (VF1-A)

Decidida el 22/11, sin programar. Reemplaza al detalle en dos columnas con tablas chicas debajo de cada valor (TL1-A).

- **La misma forma que la carga,** en la vista rápida y en la pantalla completa: las mismas zonas, el mismo orden, la grilla
  de hasta tres por renglón y el ancho de cada tipo.
- **Cada pregunta es su control,** con lo respondido adentro y sin poder tocarlo: el recuadro gris claro con el valor en
  negro; los cálculos, en gris con su fórmula en la (i); la ubicación, en un renglón con su precisión y «Ver en el mapa».
- **Nada debajo de otro dato:** cada dato de la opción elegida de un catálogo es su propio campo, en el mismo renglón, con el
  nombre «Pregunta · Dato» («Artículo · Rubro») y en gris, porque sale del catálogo. En el bloque es una columna más, y si no
  aporta, no va. No se repite lo que ya es otra respuesta: el dato que filtra una lista dependiente ni el que se eligió
  primero.
- **Siguen debajo**, porque no son datos de la respuesta: «Antes: …» al corregir, el error de una pregunta y la línea gris de
  quién y cuándo.
- **Lo que una condición ocultó no aparece**, como al cargar (hoy dice «Sin respuesta»).
- **«Corregir» va a la vista,** porque en un ABM modificar es la tarea: en la banda de la pantalla completa, con texto; en la
  vista rápida, el lápiz primero. «Anular» sigue en ⋮, en rojo. Quién los ve lo decide el servidor, y sin permiso no se
  dibujan.
- **Corregir en el lugar:** «Corregir» destraba los mismos controles en la misma pantalla. El título pasa a «Corregir
  PED-000010» y la banda queda con «Cancelar» y «Guardar corrección»; se van el ⋮, las flechas y la actividad; «Antes: …» va
  debajo de lo que cambió (salvo adentro del bloque) y el motivo, al final, es obligatorio. Ya no hay otra página ni otro
  panel para corregir.
- **La versión:** la respuesta se lee con su versión y se corrige con la activa; si algo cambia, un aviso arriba lo dice al
  empezar.
- **El backend** suma al detalle la forma de la versión de la respuesta: las preguntas en orden con su tipo, los títulos, las
  condiciones, los cálculos y, en cada pregunta de catálogo, sus datos visibles con su nombre por idioma.

## 3. La actividad, fila por fila (VF2-A)

Decidida el 22/11, sin programar. Reemplaza a TL3-A.

- Un renglón por cada cosa que pasó, con las columnas Fecha, Hora, Qué pasó, Quién, Canal, Detalle, Estado y Acciones. La
  fecha y la hora van en cada renglón, en sus columnas; lo que no aplica dice «—».
- Las bandas por momento («Al recibirla», «Aprobación», «Al aprobarla») quedan como renglones que separan.
- Cada PDF es su renglón, y cada persona de un aviso también, con su estado («Entregado», «Leído», «No se pudo»).
- El motivo de un rechazo, de una corrección o de una anulación va entero en «Detalle», la única celda que puede ocupar varias
  líneas.
- En «Acciones», sólo íconos: ver el PDF, descargar, reintentar, consultar y resolver un envío.
- A 390 px y en la vista rápida, donde no entran ocho columnas, cada renglón es un recuadro, con «Qué pasó» arriba y los demás
  datos con su nombre, de a dos.

## 4. La planilla

Programada; el orden de sus columnas nuevas va en el corte 3 de OT1-C.

- **Una fila por respuesta.** Siempre: N°, Recibida, Cargó y Canal («WhatsApp» o «App»). «Estado», si alguna regla pide
  aprobación; «Reglas», si alguna fila está «Por resolver»; «Corregida», en su columna.
- **Una columna por pregunta,** unida por su identidad entre las versiones: primero el orden de la activa, después las
  retiradas. El encabezado es el nombre de la pregunta y nada más.
- **«—» y «Sin respuesta»:** la pregunta no existía en esa versión, o se dejó vacía. El cero y el «No» se muestran.
- **Un catálogo:** el valor elegido y, en sus propias columnas, sus datos («Artículo · Rubro»), que se prenden y apagan en
  «Columnas».
- **Varias opciones:** una celda con los nombres separados por coma (varios valores de un mismo dato).
- **El bloque** dice «3 ítems» y abre el detalle; los documentos, «2 documentos»; las fotos, la primera miniatura y la cantidad,
  que abren el visor; la ubicación, el punto como enlace al mapa (no ordena ni filtra).
- **Ancha:** se desplaza de costado dentro de su caja.
- **Las solapas:** «Por aprobar (N)» (una cola), «Todas» y «Por resolver (N)».
- **El Excel:** una sola hoja, «Respuestas», con una fila por ítem del bloque y los datos de la respuesta repetidos en cada
  uno; N°, Recibida, Cargó, Canal y Estado siempre; los encabezados en el idioma de quien exporta; las fechas sólo con números
  y la hora después de una coma; números, fechas y sí o no como tipos de Excel; la ubicación en tres columnas numéricas; los
  datos de un catálogo en sus columnas; los archivos, por cantidad y nombre; hasta 100.000 filas. El diálogo dice qué exporta
  en una lectura de dos columnas: cuántas respuestas, con qué filtros, qué columnas y una fila por qué.
- **El PDF:** el bloque en una tabla, con el número del ítem en una columna angosta y una columna por pregunta, líneas entre
  columnas, números a la derecha y el encabezado repetido si sigue en otra página; las fechas sólo con números, con la zona de
  quien cargó.
- **La tarjeta del chat y «Enviada»** se revisan con el barrido de «nada debajo de otro dato» (tarea 7 de VF1-A); lo que
  cambie de forma se dibuja antes.

## 5. No repetir

- Una lectura con un diseño propio, distinta del formulario que se cargó.
- Los datos de una opción de catálogo todos juntos debajo del valor.
- Los primeros valores de un bloque metidos en una celda.
- «Sin respuesta» para lo que una condición ocultó.
- La hora debajo de la fecha, o la fecha una sola vez por día en una actividad.
- Un Excel con una hoja por bloque: acá se pasó a una sola hoja con una fila por ítem (XL1-A), que se filtra y se suma.

## 6. Para el multitenant

- **Tu planilla (E15) coincide en mucho:** la unión de preguntas por su identidad, «—» y «Sin respuesta», las subcolumnas
  «pregunta · campo» que se prenden en «Columnas», la cantidad de líneas en la celda del grupo, la coma entre varias opciones
  y el desplazamiento dentro de su caja. Tu tercer estado, «No se mostró» (E19), va en la misma línea que no mostrar lo que una
  condición ocultó.
- **El Excel:** tu E19 suma una hoja por grupo; acá, una sola con una fila por ítem (la pregunta 27 del 15).
- **El detalle:** tu E15 lee la respuesta en tu `DetailGrid` (rótulo y valor) y la actividad como una zona plegable. Acá, la
  respuesta con la forma del formulario (pregunta 77) y la actividad fila por fila (pregunta 78). Tus tableros de la E15
  están «A revisar» y los de corregir (E21), «Por dibujar»: es el momento de dibujarlos así.
- **Corregir:** tu E21 abre lo cargado con el formulario de la carga, con lo de antes a la vista y el motivo. Es casi VF1-A;
  falta que sea en la misma pantalla, sin otra página.
- **La carga (E20):** tus tres anchos (una columna, dos y el renglón entero) son los de antes de AR1-A; acá quedaron dos (T5
  §10). Tu cálculo no se ve al cargar y aparece en «Enviada»; acá, en la app, se ve en vivo, en gris, con su fórmula en la
  (i). Por WhatsApp se calcula al llegar la respuesta, como en tu decisión 54.
- **El celular:** sin vista rápida, tocar la fila abre el detalle; coincide con lo de acá.
- **La ubicación:** tu planilla no la tiene (la pregunta 28 del 15).
