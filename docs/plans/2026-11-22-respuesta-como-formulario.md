# La respuesta como el formulario (VF1-A) y la actividad fila por fila (VF2-A)

Estado: **programado entre el 2026-11-22 y el 2026-11-24 y verificado**, a pedido del usuario («Sí, arrancá» y «Sí, con R2-A»).
- Hechas las tareas 1 a 8:
  - la regla y el diseño (front `6908c39b`), el detalle con la forma de su versión (`b857e1d6`) y la lectura (`559759a6`);
  - las dos pantallas y corregir en el lugar (front `d3eab5d7` a `3df8d92d`);
  - la actividad (front `3b977974`, `d092a78f` y `427c4250`; backend `06df1a4b`);
  - el barrido VF3 con R2-A (front `6c760450` y `d4a83756`; backend `e8f503e9`) y las docs (`208d1ad0`, front `01082c5d`);
  - las escenas visuales (front `caa7caf9` y `13467d38`), y el texto largo respondido y el visor de cada pregunta (`563f716f`).
- Tarea 9, la verificación (2026-11-24):
  - el exportador, con la suite completa del backend en verde sobre `2366fccc` (antes, `ded021a7` y `2366fccc` arreglaron dos
    tests que fallaban sólo con todos juntos), y las 25 cohortes de esa corrida en el front (`0546084e`), con `entry`
    obligatorio (`f569d3e2` y `c0cc3f3f`);
  - las referencias visuales no cambian: el bloque usa `Input answered` y `563f716f` queda dentro de la tolerancia;
  - la corrida completa del front en una copia de `97117768`, con cuatro tests ajenos ajustados (`b434c463`, `54a2e0ef` y
    `97117768`, los plazos de borrar una opción del catálogo, del editor en el idioma del menú y de abrir un formulario
    desde «Formularios que faltan», y `a1e3499b`, el correo del editor de usuarios después de O2): en verde, con build,
    lint, 6.432 tests de vitest en 10 tandas y 1.395 visuales.
- De la prueba integral, en el front: O52, el 409 al guardar vuelve a la lectura con «Ver la actual», que abre la versión
  actual (`8d9bc33d` y `2c15cdb1`), y O56, lo que se hizo al recibir la respuesta va después de ella en la actividad
  (`c7923674` y `5c086c02`).
- Falta terminar la prueba a mano, que va dentro de la prueba integral. Después se retira el plan.

Tableros: lienzo aparte
[Respuesta como formulario](https://claude.ai/artifact/2vnLgSq6xxdoz9LjAdNmaN), versión 15. Este plan congela lo elegido;
el lienzo es privado y puede cambiar fuera de Git.

## Qué pidió el usuario

Mirando PED-000010 («Pedido de prueba») en pantalla completa, el 2026-11-22:

> la respuesta de los formularios, no se pueden ver ordenados tipo inputs? es decir así como un formulario común y
> corriente tenemos para cargar datos, esto sería lo mismo, tipo visualización y que ahí mismo si queremos editamos o
> hacemos algo. Es básicamente un ABM el formulario. no sé por qué le metemos estos diseños raros.

Entre tres propuestas eligió la A («El punto A es el más adecuado», «haría el VF1-A»):

- **A, elegida:** los controles de la carga con su valor.
- **B, descartada:** el valor escrito en la grilla, sin recuadros.
- **C, descartada:** A con los momentos y la actividad al costado.

Al elegir pidió una regla para toda la app, que ya había dicho otras veces:

> NO COLOCAR NADA ABAJO DE OTRO DATO, es decir, muchos datos juntos uno debajo del otro como tipo rubro todo junto, o se
> agregan columnas nuevas a la tabla o no se colocan. o se agregan inputs con los desplegables correspondientes xq es
> horrible sino.

El mismo día, mirando la actividad de RVI-000003:

> para indicarle también la zona de actividad, por qué complicarla de esa manera con un diseño bastante difícil de
> entender. Una tabla con fila por fila que fue pasando con todas las columnas correspondientes y no poner todo junto una
> cosa debajo de la otra, ¿no sería más fácil de leer y entender?

Eligió el tablero VF2-A («Sí, la actividad así, sumala al plan»).

## Decisiones

No se vuelven a preguntar.

1. **La respuesta se lee con la forma de su formulario de carga** (tableros VF1-A · 1 de 2 y 2 de 2).
   - Va en la vista rápida (D2) y en la pantalla completa (DC1), con las mismas zonas: «Datos», una por título de sección y «Fotos y documentos».
   - Mismo orden, la grilla de hasta tres por renglón y el ancho de cada tipo (AR1-A).
   - Cada pregunta es su control, con lo respondido adentro y sin poder tocarlo:
     - el recuadro gris claro y el valor en negro;
     - la opción elegida, rellena (OE1-A), y las demás a la vista, en gris;
     - los cálculos, en gris con su fórmula en la (i);
     - la ubicación, en un renglón, con su precisión al lado y «Ver en el mapa».
2. **Nada debajo de otro dato**, en toda la app. Cada dato de una opción de catálogo es su propio campo:
   - en la grilla va en el mismo renglón que su pregunta, con el nombre «Pregunta · Dato» (como la columna «Artículo · Rubro» de la planilla) y en gris, porque sale del catálogo;
   - en el bloque de ítems es una columna más, y si no aporta, no va;
   - no se repite el dato que ya es otra respuesta, como el campo que filtra una lista dependiente (CW7: «Tipo» del artículo es «Tipo de artículo»);
   - tampoco se repite el que se elige primero (LD1-A), que ya tiene su control o su columna («Rubro»).

   Siguen debajo, porque no son datos de la respuesta, «Antes: …» al corregir (CW10-A), el error de una pregunta y la línea gris de quién y cuándo en los momentos y la actividad.
3. **Lo que su condición oculta no aparece**, como al cargar. Hoy «Período desde» y «¿Lleva envío?» dicen «Sin respuesta».
4. **«Corregir» va a la vista** porque, en un ABM, modificar es la tarea. Esto cambia AC1-A en esta pantalla:
   - en la banda de la pantalla completa es un botón con texto;
   - en la vista rápida es el lápiz, primero entre los íconos del título.

   «Anular» sigue en ⋮, en rojo (CW11-A). Quién los ve lo decide el servidor (`canAmend`, decisión 8), y sin permiso no se dibujan apagados.
5. **Corregir en el lugar.** «Corregir» destraba los mismos controles en la misma pantalla:
   - el título pasa a «Corregir PED-000010» y la banda queda con «Cancelar» y «Guardar corrección»;
   - se van ⋮, ↑ ↓ y la actividad;
   - «Antes: …» va debajo de lo que cambió, salvo adentro del bloque (RG2-A);
   - el motivo va al final y es obligatorio.

   `/corregir` deja de ser una página y lleva a la pantalla completa, leyendo. `FormEntryCorrectionPanel` se va: la vista rápida corrige en el mismo panel, con el mismo componente.
6. **La versión.** La respuesta se lee con su versión («Versión 1» en la banda) y se corrige con la activa, como hoy. Si cambia algo, un aviso arriba lo dice al empezar, por ejemplo «Se corrige con la versión 2 del formulario, la activa: «Receta agronómica» arranca vacía.».
7. **La actividad, fila por fila** (tablero VF2-A). Va un renglón por cada cosa que pasó, con un dato por columna:
   - las columnas son Fecha, Hora, Qué pasó, Quién, Canal, Detalle, Estado y Acciones;
   - la fecha y la hora van en cada renglón, en sus columnas, sin la hora debajo de la fecha;
   - lo que no aplica dice «—»;
   - las bandas por momento («Al recibirla», «Aprobación», «Al aprobarla» o «Al rechazarla») quedan como renglones que separan, no como datos;
   - cada PDF es su renglón («Se armó el PDF», «Se armó el PDF en inglés»), con el archivo en «Detalle»;
   - cada persona de un aviso es su renglón, con su estado («Entregado», «Leído», «No se pudo»);
   - el motivo de un rechazo, de una corrección o de una anulación va entero en «Detalle», la única celda que puede ocupar varias líneas;
   - en «Acciones» van sólo íconos con su nombre en el tooltip: Ver PDF, Descargar, Reintentar (RT1-A), Consultar y Resolver envío;
   - a 390 px y en la vista rápida, donde no entran ocho columnas, cada renglón es un recuadro, con «Qué pasó» arriba y los demás datos con su nombre, de a dos, como los ítems del bloque.

   No cambian los textos de «Qué pasó», la revisión humana de un envío ni RT1-A. Los acuses de WhatsApp pasan a ser el estado del renglón de cada persona.
8. **Lo demás no cambia**:
   - los momentos (TL1-A) y la sección «Actividad» debajo del formulario en la pantalla completa, ahora con la tabla de la decisión 7;
   - el PDF y «Abrir conversación»;
   - «Comprobante», primero, y «Cómo empezó», después del formulario;
   - en el celular, un recuadro por ítem (ItemCards), y «Rechazar»/«Aprobar» o «Guardar corrección» fijos abajo.

Los estados están en el tablero «VF · A completa»: por aprobar, corregida, anulada y sin permiso; cargando (ES3), error con «Reintentar», 409 («Esta respuesta ya no se puede corregir.» con «Ver la actual»), la espera de un 429 como motivo de «Guardar corrección» y salir con cambios («¿Descartar la corrección?»).

## Qué reemplaza

| Decisión | Queda |
| --- | --- |
| TL1-A en el detalle | La lectura en dos columnas y los datos del catálogo debajo del valor se van del detalle. `DataList` sigue para las demás lecturas |
| DC1 (sección «Respuesta») | Las zonas del formulario |
| RG4-A en el detalle | La tabla o los recuadros de la carga, sin acciones |
| CW10-A | Corregir deja de ser otra página u otro panel; «Antes» y el motivo siguen |
| AC1-A en esta pantalla | «Corregir» a la vista; «Anular» en ⋮ |
| TL3-A (la actividad) | Las cuatro columnas con quién y el detalle debajo, la fecha una vez por día y, en la vista rápida, las acciones debajo del texto se van. Queda la tabla de la decisión 7, con las bandas por momento |

## Tareas

Cada tarea se compara con su tablero al cerrarla.

1. **La regla y el diseño en el front**, con el front libre: después de aplicar los commits de «Desplegables».
   - En `visual-baseline.md`: la regla «Nada debajo de otro dato» en «Principios» y en «Lecturas»; la pregunta 4 de la lista de chequeo; una sección fechada; y la fila del mapa.
   - En `premisas-de-desarrollo.md`, «Tablas» y lecturas.
   - Un `docs/design/respuesta-como-formulario.md` que ancle VF1-A y VF2-A a sus tableros, con la tabla de reemplazos de arriba y la correspondencia con `shared/ui`.
   - Al tocar el front, se avisa a las demás sesiones.
2. **El contrato del detalle (backend).** El detalle hoy trae pregunta y valor; tiene que traer también la forma de la versión de la respuesta:
   - las preguntas en orden, con su tipo, los títulos de sección, las condiciones y los cálculos;
   - en cada pregunta de catálogo, sus datos visibles con su nombre por idioma, y cuál filtra (CW7) o agrupa (LD1-A).

   Los valores quedan como hoy. Se suma al cuerpo del detalle (una sola lectura), con los mismos permisos (lectura, o lo propio con «Completar formularios»). Lleva su test de integración, el contrato en el OpenAPI y la exportación de ejemplos HTTP (`forms-http`) con su lector en el front.
3. **El modo lectura de `FormEntryForm` (front)**, con TDD en la lógica que arma las zonas y los campos del catálogo:
   - los controles con su valor y sin tocar;
   - los datos de catálogo como campos o columnas, con la regla de no repetir;
   - el bloque con `ItemTable`/`ItemCards` sin acciones;
   - las condiciones, y archivos con Ver y Descargar.
4. **El detalle con la forma de la carga (front).** D2 y DC1 usan el modo lectura, con la banda de la decisión 4. Se van del detalle `ResponseAnswer`, `ResponseValue` y `ResponseRows`; antes se mira si tienen otros consumidores. Escenas visuales de la vista rápida y de la pantalla completa en es/en a 1440, 390 y 320.
5. **Corregir en el lugar (front)**, con la decisión 5, el aviso de versión, los estados del tablero «A completa» y la guarda de salida. `/corregir` redirige a la pantalla completa y `FormEntryCorrectionPanel` se va. Tests de la corrección y de las rutas.
6. **La actividad fila por fila (front)**, en `ResponseWorkflowHistory` y con la decisión 7.
   - La lógica que arma los renglones va con TDD: una fila por evento, por PDF y por persona de un aviso, y cada dato en su columna.
   - La tabla en la pantalla completa; los recuadros en la vista rápida y en el celular.
   - Las acciones de cada renglón, como íconos.
   - Los datos ya llegan en la historia y en las acciones del workflow. Si una columna necesita un dato que hoy viene armado en un texto, se suma al contrato del workflow con su ejemplo HTTP.
   - Escenas visuales de la actividad en es/en, a 1440, 390 y 320.
7. **El barrido de «nada debajo de otro dato» en toda la app (RF3).**
   - El inventario va con archivo y renglón. Conocidos:
     - `ResponseValue.tsx` y `ResponseRows.tsx`, que se van con la tarea 4;
     - la tarjeta de una respuesta en el chat (`features/whatsapp/components/FormReplyValues.tsx`, la tabla chica de TL1-A);
     - «Enviada» (`FormEntrySent.tsx`) y el PDF de una respuesta (backend), que hay que revisar.
   - Lo que cambie de forma, como la tarjeta del chat, se dibuja antes y el usuario elige.
   - **Elegido el 2026-11-22: el tablero VF3 con R2-A** («Sí, con R2-A»). R3 y R4, como los dibuja; R1 no cambia la forma.
   - **Inventario del 2026-11-22**, sobre front `6908c39b` y backend `33faf2e2`:
     - **R1, las propuestas del asistente** en el detalle (`ResponseAssistance` → `ResponseValue` en modo detalle,
       `ResponseValue.tsx` renglones 13-14 y 96-125): los datos de la opción (`CapturedFields`) y su detalle van debajo
       del valor. Se arregla con un renglón propio por dato, «Pregunta · Dato», dentro de la misma lista; no cambia la
       forma.
     - **R2, la celda de la planilla** (`ResponseValue.tsx` renglones 120-123): el detalle de una opción va debajo de su
       nombre. Es la excepción A de «Un dato por columna», que la regla nueva deja sin efecto para los datos de una
       opción. Cambia la forma de la planilla: se dibuja y el usuario elige.
     - **R3, la tarjeta de una respuesta en el chat** (`features/whatsapp/components/FormReplyValues.tsx` renglones 37 a
       42): debajo del valor van el nombre del catálogo, el detalle de la opción, «Ver en el mapa» y, en una de varias,
       los catálogos y los detalles alineados. Se dibuja y el usuario elige.
     - **R4, el PDF de una respuesta** (`SkiaFormPdfRenderer.CatalogValue`, backend): los datos de la opción van en un
       segundo renglón debajo del valor («Rubro Herbicidas · Tipo Agroquímicos»). Se dibuja y el usuario elige.
     - «Enviada» (`FormEntrySent.tsx`) no apila datos: queda como está.
8. **Docs.**
   - `features/formularios.md`: el detalle, la corrección en el lugar, la actividad fila por fila y la regla.
   - Las guías de `components` y `pages` del front.
   - `carga-desde-la-app.md` y `formularios-whatsapp.md`: lo que reemplaza.
   - El traspaso al multitenant, con los demás.
9. **Verificación.** En cada corte, el build y los tests rápidos del área. Al terminar, la integración del detalle, las escenas visuales, los contratos HTTP y la corrida completa una sola vez, al cerrar.

## Riesgos

- Una respuesta vieja cuyo formulario cambió de tipo en una pregunta: se lee con su versión, así que el control es el de entonces. Corregirla pasa a la activa, con el aviso.
- Un catálogo con muchos datos en el bloque: la tabla suma columnas y se desplaza de costado dentro de su caja (RG2-A), nunca la página.
- Quien sólo aprueba (sin leer respuestas) sigue viendo sólo la decisión, sin la respuesta.
