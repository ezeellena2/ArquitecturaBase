# Renglones en los formularios: traspaso 12 al multitenant (2026-11-11)

Autor: el chat de renglones de ArquitecturaBase. Destino: ArquitecturaBaseMutitenant, para sus etapas **E14 ·
Formularios**, **E15 · Respuestas**, **E19 · Condiciones, cálculos y líneas**, **E20 · Cargar desde la web** y **E21 ·
Reglas y aprobación**, y para el canal de WhatsApp de sus formularios.

Cuenta cómo quedó programado el bloque de renglones. El multitenant conoce su diseño por los traspasos
[5](2026-11-03-traspaso-multitenant-lecturas-y-filtros.md), [9](2026-11-06-traspaso-multitenant-ajustes-de-toda-la-app.md)
y [10](2026-11-07-traspaso-multitenant/README.md). Acá van:
- las cuatro etapas;
- el Flow de WhatsApp con sus tres revisiones, de r10 a r12;
- la prueba de punta a punta del 2026-11-09.

Es uno solo para todo renglones, como eligió el usuario el 2026-11-08: no hay traspasos por etapa. El plan de renglones se
retiró al escribirlo ([procedencia](../history/README.md#renglones-el-plan-y-la-etapa-4--2026-11-11)), así que este
traspaso no depende de él.

**Como los anteriores, no es código para copiar** (decisión 18 de su hoja de ruta) ni hace falta para dibujar (decisión
108). No depende del traspaso 11 ni de los de ARCA, aunque la factura de ARCA usa este bloque.

## 1. En pocas palabras

- **Qué es.** Un formulario puede tener un bloque de preguntas que se cargan una vez por ítem, como los artículos de un
  remito. En la app es una tabla; por WhatsApp, una pantalla por ítem, hasta 10.
- **Las cuatro etapas** quedaron programadas del 2026-11-04 al 2026-11-08: el editor y publicar, la carga, la lectura y
  las reglas (§3).
- **Meta aceptó el Flow con ítems.** Hicieron falta tres revisiones del perfil, por reglas que Meta no documenta (§4).
- **La prueba de punta a punta del 2026-11-09 salió entera.** Cubrió el editor, publicar, la carga desde la app y por
  WhatsApp, una respuesta de 34,6 KB, la lectura, la corrección y las reglas (§5).
- **El Excel cambió después de esa prueba.** Pasó a una sola hoja, con una fila por ítem (XL1-A). Tiene su propio
  desarrollo y acá sólo se nombra (§3).
- **No hay decisiones nuevas para el multitenant.** Las de renglones están completas en el §2, con lo que cambió desde el
  traspaso 5.

## 2. Lo que decidió el usuario

Eligió la recomendada en cada tablero, del 2026-11-03 al 2026-11-07. Los textos que ve el usuario dicen «ítem» (RG12-A);
el código y los documentos siguen diciendo renglón (`rows`).

| Decisión | Qué quedó |
| --- | --- |
| RG1-A · El editor | El bloque es un recuadro con sus preguntas adentro (5.1, 5.2…), con nombre, ayuda y el mínimo y el máximo de ítems. La (i) del máximo dice el tope de WhatsApp. Con el bloque abierto, la vista previa muestra la pantalla de un ítem en WhatsApp. |
| RG10-D · Cómo se agrega (reemplaza el tipo «Renglones» de RG1-A y RG9-A) | «Agregar bloque», al lado de «Agregar pregunta», como una pregunta más. Se suma al final, abierto, con el nombre «Bloque». Con un bloque ya armado, el botón se apaga con su motivo. «Tipo de respuesta» no ofrece el bloque. |
| RG6-A · Elegir las preguntas | La casilla «Se carga varias veces», debajo de «Obligatoria», con su (i). Las marcadas se juntan en el bloque, y desmarcar una la saca y la deja justo después. Una pregunta publicada pasa como nueva: lo cargado conserva su columna, y publicar lo dice en «Pasan al bloque». |
| RG9-A · Lo que se apaga | En una pregunta del bloque, «Foto» y «Documento» se apagan con «· No se carga varias veces», y «Permitir elegir varias» se apaga con su motivo. |
| RG2-A · La carga en la app | En la página, una tabla a todo el ancho de «Datos», con un ítem por fila y una pregunta por columna. «Agregar ítem» va al pie, y cada fila tiene ↑ ↓ y el tacho, que quita sin preguntar. Los errores van en su celda. En el panel y en el celular, un recuadro por ítem. «Mantener las respuestas» conserva los ítems, pero no los archivos. |
| RG3-A · WhatsApp | Una pantalla por ítem, hasta 10 o el máximo si es menor. Desde el mínimo, cada ítem termina con «¿Agregás otro ítem?», un sí o no obligatorio. Con un máximo mayor que 10, publicar avisa sin frenar. Es el Flow estático, sin intercambio con Meta. |
| RG8-A · Mínimo 0 en WhatsApp | Antes del primer ítem, una pantalla con el nombre del bloque y «¿Agregás un ítem?». |
| RG13-A · Un mínimo mayor que 10 | Con WhatsApp instalado, publicar frena: «En WhatsApp se cargan hasta 10 ítems; el mínimo es 15.». La app y WhatsApp siempre validan contra el mínimo y el máximo del bloque. |
| RG7-A · El tope de la respuesta | Al publicar se suma lo más largo que podría mandar cada pregunta, una vez por ítem de WhatsApp. Un formulario con bloque tiene el tope de uno con archivos: 64 KiB en lugar de 16 KiB. |
| RG4-A · La lectura | La planilla sigue con una fila por respuesta, y la columna del bloque dice la cantidad de ítems y sus primeros valores. «Filtros» busca en algún ítem. El detalle y el PDF muestran una tabla. El Excel, que en RG4-A sumaba una hoja por bloque, desde XL1-A va en una sola hoja con una fila por ítem. |
| RG5-A · Las reglas | «Algún ítem con…»: una condición sobre una pregunta del bloque se cumple si algún ítem la cumple, y varias del bloque buscan un mismo ítem. En el mensaje, el bloque es un dato, con un ítem por línea. |
| RG12-A · Los textos | «Ítem» en toda la app: «Agregar ítem», «Ítem 2», «¿Agregás otro ítem?» y «Algún ítem con…». En inglés, «item». |

**Resueltas por defecto**, sin objeción del usuario:
- Un bloque por formulario, sin bloques adentro.
- Adentro entran texto corto, texto largo, número, fecha, sí o no y elegir una, propia o de un catálogo.
- El mínimo va de 0 al máximo, y el máximo llega hasta 100. Un bloque nuevo arranca con 1 y 20.
- Las preguntas del bloque cuentan para las 40 del formulario; el bloque, no.
- Un bloque vacío se guarda, pero no se publica.
- Un ítem vacío cuenta como ítem.
- El asistente no prellena las preguntas del bloque.

**Fuera de alcance:**
- más de un bloque, o un bloque adentro de otro;
- fotos o documentos por ítem;
- ordenar la planilla por el bloque;
- condiciones sobre todos los ítems o sobre su cantidad;
- duplicar un ítem o importar ítems desde Excel;
- la lista con los nombres antes de enviar por WhatsApp.

## 3. Cómo quedó

La regla completa está en [formularios](../features/formularios.md#el-bloque-de-renglones-al-armar-y-publicar--rg1-a-rg3-a-y-rg6-a-2026-11-04),
en sus tres secciones del bloque. Las pantallas están en el
[diseño del front](../../../ArquitecturaBaseFront/docs/design/renglones-en-formularios.md).

### Etapa 1 · El bloque al armar y publicar (2026-11-04 al 2026-11-08)

- **El borrador.** El bloque es una pregunta `group` con nombre, ayuda y `rows` (el mínimo y el máximo). Sus preguntas van
  contiguas detrás, marcadas `repeated`.
- **Lo publicado no cambia.** Los campos nuevos se omiten cuando son null, así un formulario sin bloque conserva sus
  bytes, sus huellas y sus hashes.
- **La identidad.** Pasar una pregunta guardada al bloque, o sacarla, le da una identidad nueva (`replacesFieldId`), como
  cambiar el tipo. Renombrar, ordenar y traducir la conservan.
- **Publicar.**
  - Un bloque sin preguntas frena (`Forms.Publication.GroupEmpty`).
  - Un máximo mayor que el del canal avisa (`RowsOverChannelLimit`, con `limit` y `maximum`).
  - Un mínimo mayor frena (`MinRowsOverChannelLimit`).
  - «Pasan al bloque» (`diff.movedToRows`) empareja cada pregunta nueva del bloque con una quitada de afuera que tenga el
    mismo tipo, el mismo catálogo y el mismo nombre en el idioma original.

### Etapa 2 · La carga (2026-11-07 y 2026-11-08)

- **Cómo se guarda.** En `ValuesJson`, el bloque es `{ fieldId: <bloque>, value: [ [ {fieldId, value}, … ], … ] }`, con los
  ítems en orden.
  - Cada valor y cada referencia de catálogo llevan su `Row`: de 0 a 99, y 0 fuera del bloque. `Row` entra en sus índices
    únicos, así el mismo artículo puede ir en dos ítems.
  - La migración `AddFormRecordRows` es aditiva. Su Down se frena si ya hay datos con bloque.
- **La captura.** Valida la cantidad de ítems contra el mínimo y el máximo, y cada ítem como preguntas sueltas.
  - Una opción dada de baja rechaza la respuesta, y el recibo dice la pregunta y el ítem (`RejectedFieldId`,
    `RejectedRow`).
  - Sin bloque, la huella y los bytes guardados no cambian.
- **La app.**
  - El envío manda `{ fieldId, rows: [[{ fieldId, value }, …], …] }`.
  - El error de un ítem vuelve en `answers.<bloque>.<n>.<pregunta>`.
  - Corregir abre la respuesta con sus ítems en orden.
  - Una lista que depende de otra pregunta del bloque usa la respuesta de su propio ítem (LD3-A, de «elegir primero por un
    campo»).
- **WhatsApp.** Los ítems llegan hasta el primer «No». Lo que venga después no cuenta.

### Etapa 3 · La lectura (2026-11-08)

- **La lectura neutral.** Trae el bloque como valor `group`, con sus ítems.
- **La planilla.** La columna del bloque dice la cantidad en gris y el primer valor de cada ítem: «3 · GLIFOSATO 64%, …».
  No se ordena por ella.
- **«Filtros».** Varias condiciones sobre preguntas del bloque las tiene que cumplir un mismo ítem. Es un `EXISTS` que no
  multiplica respuestas, y los conteos siguen contando respuestas.
- **El detalle y el chat.** La vista rápida lista cada ítem con su primera respuesta en negro; la pantalla completa
  muestra una tabla. La tarjeta del chat pone un ítem por línea.
- **El PDF.** Una tabla con el encabezado repetido si sigue en otra página.
- **El Excel**, desde XL1-A (2026-11-09): una sola hoja con una fila por ítem. Los datos de la respuesta se repiten en
  cada ítem, y el tope es de 100.000 filas.

### Etapa 4 · Las reglas (2026-11-08)

- **Las condiciones** de una regla y de una sugerencia deciden como «Filtros»: las que tienen valor sobre preguntas del
  bloque las cumple un mismo ítem, y la presencia mira la respuesta entera.
- **Los textos.** El editor dice «Algún ítem con»; la lista, «Si algún ítem de Artículos con…».
- **En un mensaje**, el bloque es un dato.
  - Cada ítem va en su línea, numerado desde 1, con sus respuestas separadas por « · ».
  - Una pregunta del bloque no es un dato: guardarla da `Forms.Rule.PlaceholderInvalid`.

### Contratos y migración

- **Contratos.** Los DTO del borrador, la carga, la planilla y el detalle suman `rows`, `repeated` y los valores `group`.
  El OpenAPI y los ejemplos exportados al front los muestran.
- **Migración.** Es una sola, `20261107193701_AddFormRecordRows`.
- **Permisos.** No hay nuevos.

## 4. WhatsApp: lo que exige Meta (no repetir)

El detalle está en [WhatsApp](../features/whatsapp.md#selección-múltiple-y-perfil-r9) y en el
[detalle del compilador](../features/whatsapp-servicios.md).

- **El `routing_model` tiene que declarar las dos ramas del `If`** (r10, 38b22057, 2026-11-07). Meta deduce el
  `routing_model` de los `navigate` y deja afuera la rama else. Si no se declara, el «No» en el celular da «Se produjo un
  error», y la vista oficial interactiva dice «Can't perform a transition … routing_model».
- **Una pantalla `terminal` tiene que completar en todas sus ramas** (r12, e42cc2a1, 2026-11-09). Con el bloque al final,
  el «No» completaba en la pantalla del ítem, que quedaba terminal junto al `navigate` del ítem siguiente. Meta lo rechaza
  con `INVALID_PROPERTY_VALUE` («Expected 'complete'»). Ahora:
  - el «No» y el último ítem pasan a una pantalla de cierre, con el título del formulario, «Tocá «botón final» para
    mandar el formulario.» y el botón final;
  - ninguna pantalla de ítem es terminal.
- **Llegan sólo las claves de lo visitado** (`_r1`, `_r2`…): nada de los ítems que no se abrieron, ni vacíos ni con
  `${…}`. En la prueba, «Sí», «Sí» y «No» dieron 15 claves y 886 bytes.
- **Meta escapa como `\uXXXX` todo lo que no es ASCII**, es decir, seis bytes por carácter. Por eso el compilador cuenta seis
  bytes por carácter, y no es exagerado: diez ítems de 600 caracteres con eñes y tildes pesaron 34,6 KB. Sin el tope de
  64 KiB no habrían entrado.
- **La recepción tiene que aceptar `group`.** En la Etapa 1, `ProjectionIsWellFormed` no lo aceptaba y la respuesta
  quedaba «no pudimos relacionar con un envío». Se arregló en la Etapa 2.
- **Un cambio de bytes es una revisión nueva del perfil.** Lo publicado nunca se recompila, y el prellenado acepta las
  revisiones anteriores. r11, que suma elegir primero por un campo, y r13, que suma los cálculos y condiciones de ARCA,
  son de otros desarrollos: vienen en sus traspasos.
- **Un error de Meta que no cae en una pregunta** se avisa como `WhatsApp.Flow.ValidationUnlocated`: «WhatsApp no aceptó
  cómo la app armó el formulario…». Antes prometía campos marcados que no existían.
- **Para ver lo que dijo Meta sin efectos:** `GET /{flow-id}?fields=status,json_version,validation_errors` y
  `GET /{flow-id}/assets`. El `download_url` de los assets se baja sin el token.
- **Una documentación no prueba que Meta aceptó algo.** Acá se escribió que Meta había aceptado el bloque al final y no
  era cierto. Lo desmintieron esos dos GET.

## 5. Cómo se verificó

- **Pruebas automáticas.** Cada etapa tuvo sus unitarias, de integración con PostgreSQL y Redis, de contrato y visuales.
  La verificación final conjunta del 2026-11-08 dio:
  - en el backend, 7428 de 7428 sobre 9f721e5d, con el build sin advertencias;
  - en el front, lint, build y 1013 escenas sobre 98cf7f38, y vitest entero, 4384 de 4384, sobre c29c363b.
- **Con Meta.**
  - Del 2026-11-06 al 2026-11-07 publicó las variantes con preguntas después del bloque, con mínimo 0 y sin él. El
    usuario las recorrió en la vista oficial y en el celular.
  - El bloque al final pasó con r12 el 2026-11-09: la misma versión que había rechazado dos veces se publicó sin cambios.
- **La prueba de punta a punta del 2026-11-09**, con el usuario y clic por clic. Salieron los ocho puntos de renglones:
  1. El editor: «Agregar bloque», «Se carga varias veces» y los textos con «ítem».
  2. Publicar una versión con preguntas que pasan al bloque.
  3. La carga desde la app, con la tabla, los recuadros, el mínimo y el máximo.
  4. La carga por WhatsApp, con la pantalla de cierre.
  5. La respuesta de 34,6 KB, que llegó entera.
  6. La lectura: la planilla, «Filtros» por algún ítem, el detalle, el chat y el Excel.
  7. Corregir una respuesta con ítems.
  8. Las reglas: una respuesta en la que ningún ítem cumplía todas las condiciones no pidió aprobación, y otra se aprobó
     con su PDF y su mensaje.

## 6. Lo que se encontró al programarlo (no repetir)

- **Los dobles de prueba no prueban a Meta.** Las reglas del §4 salieron recién al publicar de verdad. Cada forma nueva del
  Flow se publica con Meta antes de darla por buena, mirando `validation_errors`.
- **Una prueba con el bloque al final.** Las variantes de la verificación tenían preguntas después del bloque, y el caso
  del bloque al final llegó a Meta recién en la prueba integral.
- **Las clases parciales.** Los tests de envío por WhatsApp son partes de una misma clase: filtrar por el nombre del
  archivo corre sólo una parte.
- **Las huellas.** Todo campo nuevo de un modelo sellado se omite cuando es null. Si no, cambian las huellas de lo
  publicado.

## 7. Adaptación al multitenant

- **El bloque vive en la respuesta.** No agrega nada por tenant aparte de lo que ya tienen las respuestas y sus valores.
  `Row` se suma a las claves de los valores y de las referencias, junto a lo que allá identifique al tenant.
- **El perfil del Flow** se compila por publicación, como hoy. Las revisiones r10 a r12 se cuentan por su contenido, no
  por su número: el multitenant puede numerarlas a su manera, siempre que una forma publicada no se recompile.
- **Las reglas del §4 valen igual** con un número de WhatsApp por tenant.

## 8. Lo que falta acá

Lo que quedó de la prueba integral está en
[verificación y producción](verificacion-y-produccion.md#formularios-renglones-lo-que-quedó-de-la-prueba-integral--2026-11-11):
- por WhatsApp, volver con la flecha antes del «No»;
- la lectura en inglés;
- las escenas visuales con el bloque, que son opcionales;
- los ejemplos HTTP exportados al front, que todavía dicen r11.

La verificación conjunta de esta tanda la corre el chat que la coordina.

## 9. Dónde está cada cosa en ArquitecturaBase

**Backend:**
- `src/ArquitecturaBase.Application/Modules/BusinessForms/Services/`:
  - el borrador y publicar: `FormDraftPolicy`, `FormPublicationPolicy` (`ValidateRows`, `RowIssues`),
    `BusinessFormPublicationService` y `FormPublicationSnapshotPolicy`;
  - la captura: `FormRecordAnswerPolicy`;
  - la lectura neutral: `FormRecordPresentationPolicy.Rows`;
  - el Excel: `FormExportService`;
  - el PDF: `FormPdfDesignPolicy` y `FormPdfPreviewIssuer`.
- `src/ArquitecturaBase.Infrastructure/Modules/BusinessForms/Persistence/Readers/FormResponseReader.Filters.cs` y
  `.Facets.cs`: «Filtros» y los conteos por algún ítem.
- `src/ArquitecturaBase.Application/Modules/WhatsApp/`:
  - `Services/WhatsAppFlowScreenBuilder` arma las pantallas;
  - `Services/WhatsAppFlowCompiler` cuenta el presupuesto;
  - `Services/WhatsAppFlowRowsCodec` lee la respuesta;
  - `Services/WhatsAppFlowPublicationPolicy` arma el estado y `ValidationUnlocated`;
  - `Configuration/WhatsAppFlowProfile` tiene las revisiones.
- La migración: `src/ArquitecturaBase.Infrastructure/Persistence/Migrations/20261107193701_AddFormRecordRows.cs`.
- Las pruebas:
  - en `tests/ArquitecturaBase.Application.UnitTests/Modules/BusinessForms/`, las familias `FormGroup*`, `FormRows*` y
    `FormEntryRows*`;
  - en `tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp/`, `WhatsAppFlowRows*`,
    `WhatsAppFlowPublicationStatusTests` y los fixtures `flow-static-7.3-r12-{es,en}.json`;
  - en `tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/`, las familias `FormGroup*`, `FormRows*` y
    `FormEntryRows*`, y `FormRecordRowsMigrationTests`;
  - en `.../Modules/WhatsApp/`, `WhatsAppRowsCompositionTests`.
- Las reglas: [formularios](../features/formularios.md), [WhatsApp](../features/whatsapp.md) y el detalle de
  [servicios](../features/formularios-servicios.md) y del [compilador](../features/whatsapp-servicios.md).

**Front (`../ArquitecturaBaseFront`):**
- `src/shared/ui/ItemTable.tsx`: la tabla de ítems, con su ficha en el estándar visual.
- En `src/features/business-forms/`:
  - `rowsQuestions.ts`;
  - el editor y publicar, en `QuestionEditor`, `PublicationCheckDialog` y `FormPreview`;
  - la carga, en `FormEntryForm` y `FormEntryPanel`;
  - la lectura, en `components/ResponseRows.tsx` y `ResponseFiltersMenu.tsx`;
  - las reglas, en `FormConditionsEditor.tsx` y `ConditionLines.tsx`.
- `src/features/whatsapp/components/FormReplyValues`: la tarjeta del chat.
- El diseño: `docs/design/renglones-en-formularios.md`.

## 10. Prompt para el chat guía del multitenant

```text
Leé el traspaso 12 de ArquitecturaBase, en
C:\Users\ezequ\source\repos\ArquitecturaBase\docs\plans\2026-11-11-traspaso-multitenant-renglones.md.
Cuenta cómo quedó programado allá, del 4/11 al 9/11, el bloque de renglones de los formularios: el editor y publicar,
la carga desde la app y por WhatsApp, la lectura y las reglas, con lo que exigió Meta y la prueba de punta a punta.
Conocés su diseño por los traspasos 5, 9 y 10. Parte de los commits no está en GitHub: leelos en
C:\Users\ezequ\source\repos\ArquitecturaBase y ...\ArquitecturaBaseFront.

1. Compará el §2 con las decisiones de renglones que registraste con esos traspasos y anotá las diferencias. RG10-D,
   RG12-A y RG13-A cambiaron cómo se agrega el bloque, los textos y el mínimo.
2. Pasá lo del §3 a tu diseño de E14, E15, E19, E20 y E21, y lo del §4 a las notas del canal de WhatsApp. Son reglas de
   Meta que su documentación no dice.
3. Usá el §5 como criterio de prueba y el §6 como lista de lo que no hay que repetir.
4. No copies código (tu decisión 18). Si algo del §7 choca con tu modelo por tenant, preguntale al usuario antes de
   decidir.

Al terminar, contale al usuario qué cambió en tus planes y qué quedó por decidir.
```
