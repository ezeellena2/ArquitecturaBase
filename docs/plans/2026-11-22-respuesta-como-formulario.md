# La respuesta como el formulario (VF1-A)

Estado: **decidido el 2026-11-22, sin programar.** Se programa cuando el usuario lo pida, después de la prueba integral
(la lista está congelada hasta entonces). Tableros: lienzo aparte
[Respuesta como formulario](https://claude.ai/artifact/2vnLgSq6xxdoz9LjAdNmaN), versión 11. Este plan congela lo elegido;
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
7. **Lo demás no cambia**:
   - los momentos (TL1-A) y la actividad (TL3-A), con la sección «Actividad» debajo del formulario en la pantalla completa;
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

## Tareas

Cada tarea se compara con su tablero al cerrarla.

1. **La regla y el diseño en el front**, con el front libre: después de aplicar los commits de «Desplegables».
   - En `visual-baseline.md`: la regla «Nada debajo de otro dato» en «Principios» y en «Lecturas»; la pregunta 4 de la lista de chequeo; una sección fechada; y la fila del mapa.
   - En `premisas-de-desarrollo.md`, «Tablas» y lecturas.
   - Un `docs/design/respuesta-como-formulario.md` que ancle VF1-A a sus tableros, con la tabla de reemplazos de arriba y la correspondencia con `shared/ui`.
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
6. **El barrido de «nada debajo de otro dato» en toda la app (RF3).**
   - El inventario va con archivo y renglón. Conocidos:
     - `ResponseValue.tsx` y `ResponseRows.tsx`, que se van con la tarea 4;
     - la tarjeta de una respuesta en el chat (`features/whatsapp/components/FormReplyValues.tsx`, la tabla chica de TL1-A);
     - «Enviada» (`FormEntrySent.tsx`) y el PDF de una respuesta (backend), que hay que revisar.
   - Lo que cambie de forma, como la tarjeta del chat, se dibuja antes y el usuario elige.
7. **Docs.**
   - `features/formularios.md`: el detalle, la corrección en el lugar y la regla.
   - Las guías de `components` y `pages` del front.
   - `carga-desde-la-app.md` y `formularios-whatsapp.md`: lo que reemplaza.
   - El traspaso al multitenant, con los demás.
8. **Verificación.** En cada corte, el build y los tests rápidos del área. Al terminar, la integración del detalle, las escenas visuales, los contratos HTTP y la corrida completa una sola vez, al cerrar.

## Riesgos

- Una respuesta vieja cuyo formulario cambió de tipo en una pregunta: se lee con su versión, así que el control es el de entonces. Corregirla pasa a la activa, con el aviso.
- Un catálogo con muchos datos en el bloque: la tabla suma columnas y se desplaza de costado dentro de su caja (RG2-A), nunca la página.
- Quien sólo aprueba (sin leer respuestas) sigue viendo sólo la decisión, sin la respuesta.
