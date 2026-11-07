# Elegir primero por un campo: un combo que filtra el siguiente

Decidido el 2026-11-07, sin programar. Una pregunta de catálogo puede pedir primero el valor de un campo del mismo catálogo,
como «Rubro», y después listar sólo las opciones con ese valor. Lo pidió el usuario en el editor de «Prueba renglones 2»: la
pregunta «Artículos», adentro del bloque, tenía «Tipo» fijo en Agroquímicos y 125 opciones, y quería elegir primero
«Herbicidas» y que la lista trajera sólo esos artículos, «en vez de mostrar el listado completo y filtrarlo». Las reglas de
hoy están en [formularios](../features/formularios.md#listas-dependientes--etapa-4-2026-11-04) y en el
[plan de la carga desde la app](2026-10-29-cargar-formularios-desde-la-app.md).

**Tableros:** LD1 a LD3, al final del lienzo provisorio
[Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M) (versión 93; la 94 marca las elegidas con su
nota verde). Como en renglones, carpetas y la IA, el diseño se registra en el front antes de programar:
[carga desde la app](../../../ArquitecturaBaseFront/docs/design/carga-desde-la-app.md#elegir-primero-por-un-campo-ld1-a-a-ld3-a--2026-11-07).

## Lo que había

- **Listas dependientes (CW7), sólo en la app.** «Agregar filtro según otra respuesta» filtra una pregunta por la respuesta de
  otra anterior. Pide un campo de papel «filtro» y de tipo opción de un catálogo, y una pregunta anterior que elija de ese
  catálogo. En el ejemplo, Artículos tiene «Tipo» (opción de «Tipos de producto», papel «Filtro») y «Rubro» (texto, papel
  «Dato»): con «Rubro» no se puede sin armar un catálogo «Rubros».
- **WhatsApp.** El Flow es estático: las opciones de cada lista viajan en el mensaje que abre el formulario y no hay
  intercambio con el servidor mientras se completa. Un formulario con CW7 no se manda.
- **Los datos del ejemplo** (base local, 2026-11-07): Artículos tiene 331 opciones activas. Con «Tipo» fijo en Agroquímicos
  quedan 125, en tres rubros: Herbicidas 75, Insecticidas 34 y Fungicidas 16. Sin filtros suman Semillas varias 188 y
  Fertilizantes 18. Como JSON del mensaje son unos 33 KB con las 331 y 9 KB con las 125.

## Decisiones (2026-11-07)

El usuario eligió las recomendadas, de a una, viendo los tableros:

1. **Elegir primero por un campo (LD1-A).** Es un ajuste de la pregunta de catálogo, no un filtro ni otra pregunta: no hace
   falta otro catálogo y sirve con un campo de texto como «Rubro». Se guarda sólo la opción. Descartó LD1-B: dos preguntas
   con CW7, con un catálogo «Rubros», el campo pasado a opción de ese catálogo con papel «Filtro» y una pregunta «Rubro» más
   en cada formulario.
2. **En WhatsApp, las dos listas en la misma pantalla (LD2-A).** Cada valor viaja en el mensaje con sus opciones y, al
   elegirlo, la acción `update_data` de Meta (Flow JSON 6.0 o más) llena la segunda lista en el teléfono, sin intercambio
   con el servidor. Antes de programar, una prueba con Meta (abajo). Descartó LD2-B, la lista entera por WhatsApp como hoy
   si entra en 200, que queda como salida si la prueba no anda, y LD2-C, no mandarlo por WhatsApp hasta el intercambio
   cifrado.
3. **Listas dependientes en el bloque de renglones: cada renglón con su respuesta (LD3-A).** Para CW7: si las dos preguntas
   están en el bloque, la lista usa la respuesta del mismo renglón; una pregunta de afuera vale para todos los renglones; y
   una pregunta de afuera no puede depender de una del bloque. Descartó LD3-B, no permitir CW7 con preguntas del bloque.
   Resuelve el «Por decidir» del
   [plan de la Etapa 1 de renglones](2026-11-04-renglones-etapa-1-implementacion.md#por-decidir-la-revisión-del-backend-2026-11-04).
   Con LD1-A, el rubro y el artículo son la misma pregunta y van juntos en cada renglón: no necesita esta regla.

**Resueltas por defecto**, con la opción más simple que cumple las decisiones:

- Sirven los campos activos del catálogo de tipo texto u opción de un catálogo, con papel «Dato» o «Filtro». Uno por
  pregunta. Un catálogo sin esos campos, como el de personas, no ofrece la casilla.
- No se combina con «Permitir elegir varias»: la casilla queda apagada con el motivo, y viceversa.
- Los valores salen de las opciones que dejan los demás filtros (fijo, por persona y según otra respuesta). Van en el orden
  de su catálogo si el campo es una opción de un catálogo, y por orden alfabético si es texto. Las opciones sin valor van al
  final, en «Sin …» con el nombre del campo («Sin rubro»).
- Si queda un solo valor, viene elegido, como la única opción de CW3.
- La búsqueda de la segunda lista busca dentro del valor elegido.
- Al corregir (CW10-A), el primer combo arranca con el valor de la opción guardada.

## Cómo queda

### En el editor

- **«Elegir primero por un campo»**, una casilla con su (i) debajo de «Permitir elegir varias». Marcada, muestra «Campo» con
  los campos que sirven. La línea de la pregunta dice «Elegir de un catálogo, primero por Rubro».
- **El resumen de «Qué opciones ve cada persona»** cuenta por valor: «Todos ven las mismas 125, en 3 rubros. El más grande
  tiene 75: entra en el desplegable (200).» El tope de 200 de WhatsApp vale para cada valor y para la lista de valores, no
  para el total.
- **El borrador** guarda el campo en la fuente de la pregunta, con un nombre en inglés como `source.groupBy` (el `fieldId`).
  Se omite cuando es null, así las preguntas publicadas y sus huellas no cambian. Publicar frena un campo que no existe, no
  está activo o no es de un tipo que sirve, y la pregunta de «elegir varias» con el ajuste.

### En la app

- **Dos combos**, el del campo, con su nombre de etiqueta («Rubro»), y el de la pregunta. El de la pregunta espera, apagado
  con «Elegí primero «Rubro»»; con el valor elegido trae sólo sus opciones, y la cuenta lo nombra: «75 opciones de
  Herbicidas». Si el valor cambia, la opción elegida se limpia, como en CW7.
- **El servidor** lista los valores con los mismos filtros, en SQL, y las opciones reciben el valor elegido como un filtro
  más. El envío no cambia: la opción tiene que ser de la oferta de la pregunta, sin mirar el valor.
- **En la grilla**, el par ocupa dos columnas; en el bloque de renglones (RG2-A), dos columnas de la tabla, «Rubro» y la
  pregunta, y cada renglón elige el suyo.

### En WhatsApp (LD2-A)

- **El Flow** suma, delante del desplegable de la pregunta, uno con los valores (`${data.<clave>_groups}`), que no va en lo
  que se manda al completar. El de la pregunta arranca oculto y vacío (`visible` y `data-source` en datos de la pantalla).
  Cada valor lleva en su `on-select-action` un `update_data` con sus opciones y la visibilidad. Es un perfil nuevo, r11:
  un formulario sin el ajuste compila igual que con r10, como pasó con los renglones.
- **El mensaje** que abre el formulario lleva los valores con sus opciones, armados con la oferta de cada persona. El tope de
  200 se revisa por valor y para la lista de valores (`CatalogTooLarge` en la pregunta). La vista oficial interactiva
  (VO1-A) lleva los primeros valores con sus primeras opciones dentro de sus 6000 caracteres.
- **En los renglones**, cada pantalla de renglón declara sus propios datos para la lista de la pregunta y toma los valores
  de la primera pantalla, como hoy las opciones (`${screen.FORM.data…}`).
- **La respuesta** no cambia: llega sólo la opción. El webhook y la captura siguen igual.
- Suma un componente por pregunta a su pantalla (tope de 50).

### Listas dependientes con renglones (LD3-A)

- **En el editor,** «Respuesta de» dice de dónde sale: en una pregunta del bloque, «Elige de Rubros · del mismo renglón» o,
  para una de afuera, «Elige de Campos · para todos los renglones»; en una pregunta de afuera, las del bloque salen apagadas
  con «Está en el bloque».
- **Publicar** frena con `Forms.Publication.MatchQuestionInvalid` si una pregunta de afuera depende de una del bloque.
- **En la carga** (Etapa 2 de renglones), cada renglón pasa la respuesta de su mismo renglón, o la de afuera. Por WhatsApp,
  CW7 sigue sin mandarse.

## Antes de programar: la prueba con Meta

Con el visto bueno del usuario, porque crea un Flow en su cuenta de Meta. Un Flow borrador armado a mano, con las dos listas,
abierto con la vista oficial interactiva y su `flow_action_payload` (como VO1-A) y, si hace falta, en el celular. A
confirmar:

1. que las opciones que llegan en el mensaje (no en `__example__`) traigan su `on-select-action` y que `update_data` llene
   la segunda lista;
2. qué pasa con la opción elegida si cambia el valor: si se limpia sola o queda una de otro valor (con LD1-A no rompe el
   envío, porque la opción sigue siendo de la oferta, pero confunde);
3. el desplegable oculto y obligatorio, y el botón «Continuar» antes de elegir;
4. lo mismo en una pantalla de renglón, con los valores tomados de la primera;
5. que salga un mensaje de unos 33 KB: Meta no publica un tope para `flow_action_payload`.

Si 1 no anda, queda LD2-B: por WhatsApp, la lista entera si entra en 200. Lo que dé la prueba se anota acá.

## Cortes

1. **La prueba con Meta**, sin código de la app.
2. **El backend:** el borrador y su validación, publicar, la descripción de la carga (los valores) y las opciones con el
   valor, corregir, los textos es/en y sus tests (TDD).
3. **El front:** el editor, la carga y la tabla del bloque, con la app apagada (Vite recarga la página de quien prueba). La
   tabla del bloque va con la Etapa 2 de renglones.
4. **WhatsApp:** r11, el mensaje, la vista oficial y el chequeo de los 200 por valor.

LD3-A se programa con la Etapa 2 de renglones.

## Coordinación

- El compilador de Flows y el bloque los está tocando la sesión de renglones: r11 se programa después de su cierre o
  coordinado con ella.
- Es un contrato todavía no publicado: el front y el backend se cambian juntos, sin capas de compatibilidad.

## Para después

- Con el mismo `update_data` se podrían mandar por WhatsApp las listas dependientes de CW7 cuando las dos preguntas están
  en la misma pantalla. Ahí sí importa el punto 2 de la prueba: una respuesta de otro valor haría rechazar el envío.
