# ARCA · Etapa 2: el motor para documentos

Plan del 2026-11-09, decidido el mismo día y sin programar. Es la etapa 2 de la [hoja de ruta de ARCA](2026-10-31-arca-formularios-estandar.md):
lo que el motor de formularios necesita para armar documentos como la factura, sin emitir nada. Todo va en el módulo de
formularios (BusinessForms) y no nombra a ARCA: la etapa 3 lo usa para la factura C. Las reglas de hoy están en
[formularios](../features/formularios.md).

**Tableros:** CA1, RE1, BI1 y CO1 en el lienzo aparte
[ARCA · motor para documentos](https://claude.ai/artifact/JPwMtoM9TqM8fyqGCr9QVP), una fila por pregunta con la
recomendada primero. El lienzo provisorio «Formularios desde la app» lo comparten a la vez las sesiones de las etapas 3 a 6,
y el Sistema visual es de otra cuenta, así que, como en renglones, carpetas, la IA y «Formularios sólo en la app», el
diseño elegido está registrado en el front:
[motor para documentos](../../../ArquitecturaBaseFront/docs/design/motor-para-documentos.md).

**Orden:** primero lo que necesita la etapa 3 (cálculos, resultado externo y la biblioteca con preguntas atadas), después
las carpetas, que la biblioteca usa para ubicar lo que agrega, y al final las condiciones. El backend va ahora; el front,
cuando el usuario termine la prueba integral del 2026-11-09 (mientras dura, nadie edita el front). Cada pieza se le avisa
al usuario cuando queda commiteada.

## Inventario

| Pieza | Qué hay | Qué falta |
| --- | --- | --- |
| Renglones | Las cuatro etapas programadas y verificadas ([regla](../features/formularios.md#el-bloque-de-renglones-al-armar-y-publicar--rg1-a-rg3-a-y-rg6-a-2026-11-04)): el bloque con texto, decimal, fecha, sí o no y elegir una (propia o de catálogo), en la app hasta 100 ítems y en WhatsApp hasta 10, leído entero y con reglas por ítem | Para la factura, sólo el subtotal del ítem, que es un cálculo (abajo). Artículo, descripción, cantidad, precio y alícuota ya entran en un bloque |
| Cálculos | Nada | Todo |
| Condiciones | Dos parecidas que no ocultan preguntas: las [listas dependientes](../features/formularios.md#listas-dependientes--etapa-4-2026-11-04) (sólo en la app) y [elegir primero por un campo](../features/formularios.md#elegir-primero-por-un-campo--ld1-a-2026-11-07). El Flow de WhatsApp ya usa `If` sobre una respuesta (`${form.<bloque>} == 'true'`, r12) | Mostrar una pregunta según otra respuesta |
| Resultado externo | El motor de acciones durable (lease, fence, «Sin confirmar» y revisión humana) con cuatro acciones fijas; el PDF es el único resultado que deja una fila (`BusinessFormDocument`). El texto de un aviso se arma al recibir o decidir | Dónde se guarda, cómo se lee y cómo lo usan los avisos; cómo otro módulo aporta la acción que lo trae |
| Carpetas | Diseñadas y elegidas ([CP1-A a CP5-A](2026-11-03-carpetas-de-formularios-y-catalogos.md)), con su backend fijado | Todo el código |
| Biblioteca | La pantalla «Biblioteca» y «Agregar» elegidas en DOC1 ([diseño](../../../ArquitecturaBaseFront/docs/design/documentos.md)); crear un borrador desde código ya existe para la IA (`FormDraftProposalService`, `ProposalId`) | La fuente de estándares, la copia que recuerda su origen, las preguntas atadas y la pantalla |

**Lo que se revisó de renglones:** el decimal no tiene mínimo ni escala (acepta negativos y hasta 28 decimales). Una
cantidad o un precio negativos los rechaza la emisión (etapa 3, «antes de emitir, el servidor recalcula y valida»), no el
formulario.

## Decisiones del usuario (2026-11-09)

Eligió las cuatro recomendadas. No se vuelven a preguntar.

1. **CA1-A: el cálculo es una pregunta más,** de tipo «Cálculo», con multiplicar, sumar o porcentaje entre preguntas de
   número anteriores. Al cargar se ve en gris y no se escribe. Se descartó CA1-B (columnas calculadas y totales al pie).
2. **RE1-A: el resultado externo es una zona en la respuesta,** arriba de «Datos» en el detalle y en la copia PDF, con una
   columna por dato en la planilla y el Excel. Los avisos de la misma regla que van después de la acción usan sus datos.
   Se descartó RE1-B (en la actividad).
3. **BI1-A: un candado en cada pregunta atada,** en la lista del editor, y los controles que no se cambian apagados con su
   motivo. Se descartó BI1-B (una lista en «El formulario»).
4. **CO1-A: la condición va en cada pregunta,** «Se muestra: Siempre / Según otra respuesta», en la app y por WhatsApp. Se
   descartó CO1-B (en el título de sección).

**Resueltas por defecto** (lo más simple que cumple la hoja de ruta):

- Un cálculo se redondea a 2 decimales con half-even (`MidpointRounding.ToEven`), cada uno por su lado: el total suma los
  subtotales ya redondeados, como una factura.
- Los operandos van antes que el cálculo, así no hay ciclos. Un operando vacío deja vacío un producto o un porcentaje; la
  suma ignora los vacíos y queda vacía si no hay ninguno.
- En WhatsApp un cálculo no se ve mientras se completa: el Flow estático no puede calcular y el intercambio cifrado sigue
  pendiente. La respuesta lo trae calculado por el servidor. Si el total tiene que verse dentro del Flow se decide con la
  factura (etapa 3), como dice la hoja de ruta.
- Una pregunta atada conserva lo que la emisión necesita: no se borra, no cambia de tipo, no entra ni sale del bloque y
  conserva sus opciones, su catálogo, su cálculo y si es obligatoria. Se cambian su texto, su ayuda y su orden.
- Las carpetas siguen su plan sin cambios.
- La biblioteca no trae un estándar propio en esta etapa: el primero es la factura de la etapa 3. Sin estándares
  instalados, «Biblioteca» no aparece.
- Las condiciones valen también por WhatsApp (con un `If` sobre la respuesta, como el bloque de r12), porque la factura es
  un solo estándar para los dos canales (decisión 4 de la hoja de ruta).

## Diseño

### Cálculos

- **Es una pregunta decimal con `calculation`**, de sólo lectura: `{ operation, operands }`. Así la planilla, «Filtros»,
  el Excel, el PDF, las reglas y los avisos la leen como cualquier número, sin tocar sus lecturas. Prender o cambiar el
  cálculo conserva la identidad de la pregunta. No puede ser obligatoria.
- **Tres operaciones fijas, sin fórmulas libres:**
  - `multiply`: dos operandos, como cantidad × precio. Si el cálculo está en el bloque, los dos son del mismo ítem; si no,
    los dos son de afuera.
  - `sum`: uno o más operandos. Uno de afuera suma su valor; uno del bloque suma ese valor en todos los ítems. El cálculo
    va afuera del bloque.
  - `percent`: base × alícuota / 100, con la misma regla de alcance que `multiply`. La alícuota puede ser un decimal o una
    elección de un catálogo con valor numérico (entero o decimal), cuyo valor sellado se usa.
- **Operandos:** decimales, otros cálculos o, para la alícuota, la elección de catálogo. Se nombran como en las listas
  dependientes: por `fieldId` o, si son nuevas, por su `localId`, que el guardado sella.
- **Guardar** valida la forma (operación, cantidad de operandos, que no se nombre a sí misma, sólo en un decimal no
  obligatorio). **Publicar** frena `Forms.Publication.CalculationInvalid` en `draft.fields[i].calculation` si un operando no
  existe, no va antes, no es de un tipo que sirve o no respeta el alcance.
- **La captura** (app y WhatsApp, en `BusinessFormRecordRecorder` después de normalizar) calcula y guarda los valores como
  respuestas decimales. Una respuesta enviada para un cálculo es inválida (`answers.<pregunta>`). Corregir recalcula.
- **La carga en la app** recibe `calculation` en cada pregunta para mostrar la vista previa con el mismo redondeo; manda el
  servidor.
- **WhatsApp:** el perfil siguiente (r13) no pone los cálculos en el Flow; sin cálculos compila los mismos bytes. El
  asistente no los prellena, las sugerencias no los mapean como destino y la IA no los propone.

### Resultado externo

- **Lo trae la acción «Emitir comprobante»** (`IssueDocument`) de la etapa 3, sobre su puerto `IStructuredRecordIssuer`
  ([diseño de la etapa 3](2026-11-09-arca-etapa-3-factura-c.md), [ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md)). Esta etapa no hace ningún puerto de
  acciones: pone dónde se guarda, cómo se lee y cómo lo usan los avisos.
- **DTO neutrales** en `Models/StructuredData`: `StructuredRecordResult` (nombre de la fuente por idioma y sus datos, cada
  uno con clave, nombre por idioma, tipo —texto, fecha o número— y valor) y `StructuredRecordResultField`, el mismo dato sin
  valor, para declararlo de antemano.
- **Se guarda en la respuesta:** una fila por run (`BusinessFormRecordResults`) con la fuente, su nombre y los datos con
  sus nombres, sellados en ese momento, así se siguen leyendo aunque se quite el módulo. La guarda un helper de
  BusinessForms que la acción llama en la transacción que confirma el run bajo el fence, sin abrir otra.
- **Se lee** en el detalle, la planilla (columnas para elegir en «Columnas»), el Excel y la copia PDF, según RE1.
- **Los avisos** usan los datos del resultado. Como el texto se arma al recibir, un dato del resultado se resuelve al
  ejecutar el aviso, y sólo vale en un aviso que va después, en la misma regla, de una acción que lo declara (la acción
  siguiente espera a que la anterior termine). Guardarlo en otro lugar da `Forms.Rule.PlaceholderInvalid`. La acción dice
  qué datos declara por un punto que esta etapa deja y la etapa 3 completa con su descriptor.
- **El contrato con la etapa 3** queda en las notas compartidas de las sesiones de ARCA (`etapa-2-contratos.md`). Esta
  etapa lo prueba guardando resultados desde `TestFeatures`.

### Biblioteca

- **Cada módulo aporta sus estándares** por un puerto neutral (`TryAddEnumerable`), como datos (un JSON embebido por
  estándar), no como código. El estándar tiene clave, versión, nombre, descripción, prefijo sugerido, carpeta, idioma, el
  formulario (preguntas, bloque, cálculos, condiciones y títulos) y, por pregunta, la clave del dato que la emisión necesita
  si está atada. Nombra los catálogos por su clave (`Key`) y puede traer uno propio, que se crea al agregar si no existe
  (la factura trae «Clientes»); una condición nombra las opciones de un catálogo por su valor.
- **«Agregar»** (`formtemplates.manage`) pide el nombre, el prefijo y la carpeta (por defecto, la del estándar) y crea un
  borrador propio por el mismo camino que la IA. La definición sella una vez el estándar, su versión y qué pregunta
  corresponde a cada dato. Se puede agregar más de una vez.
- **Las preguntas atadas** las controla el guardado del borrador (`Forms.Draft.BoundQuestionChanged` en
  `draft.fields[i]`); el borrador las marca para que el editor muestre el candado.
- **La pantalla** sigue DOC1 sin la lectura de documentos, que es del [plan de documentos](2026-10-22-documentos-por-whatsapp.md).

### Carpetas

Como su [plan](2026-11-03-carpetas-de-formularios-y-catalogos.md#backend): `FormFolders` y `FormFolderMemberships`, el
árbol podado por listado, el filtro con subcarpetas en los tres listados, el menú y la carpeta del sistema que vuelve
aunque la renombren (la usan ARCA y la biblioteca).

### Condiciones

- **Una pregunta de afuera del bloque se muestra según una pregunta anterior de afuera del bloque**, de elegir una (propia
  o de catálogo) o sí o no: `showWhen` con la pregunta y los valores que la muestran. El bloque y sus preguntas no llevan
  condición en esta etapa.
- **Oculta, no es obligatoria** y no puede tener respuesta (`answers.<pregunta>`). Un cálculo la toma como vacía y las
  reglas, como sin responder.
- **Publicar** frena `Forms.Publication.ConditionInvalid` en `draft.fields[i].showWhen` si la pregunta no va antes, no es de
  un tipo que sirve o un valor ya no existe.
- **WhatsApp:** el Flow la muestra con un `If` sobre la respuesta, como el bloque de r12; se prueba con Meta.

## ADR

El puerto de emisión es del [ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md) de la etapa 3. Esta etapa suma uno, antes de programar la biblioteca: **los estándares
de la biblioteca los aportan los módulos como datos**, la copia recuerda su origen y las preguntas atadas protegen lo que
el módulo necesita. El número se elige al escribirlo, porque las etapas 3, 5 y 6 también escriben los suyos.

## Tareas

Cada tarea con su test en rojo primero, el build y los tests rápidos del área, y su commit.

**Cálculos**

1. ~~CA1: tablero y decisión.~~ Hecho: CA1-A.
2. ~~El borrador: `calculation` en `FormFieldInput` y `CanonicalFormField` (omitido si es null), su validación y el sellado
   de los operandos.~~ Hecho (3c15292e).
3. ~~Publicar: `CalculationInvalid` con el alcance de cada operación; la versión sellada lo lleva.~~ Hecho (3c15292e); el
   pedido al canal lo suma la tarea 6.
4. ~~La captura: calcular (también en los ítems y al corregir) y rechazar una respuesta a un cálculo.~~ Hecho en
   `FormRecordAnswerPolicy` (3c15292e).
5. ~~La carga: `calculation` en `GET /api/forms/entries/{definitionId}` y en la corrección.~~ Hecho (3c15292e).
6. WhatsApp sin los cálculos, más el prellenado, las sugerencias y la IA. Va junto con la tarea 28 en un solo perfil r13, para cambiar fixtures y probar con Meta una vez.
7. La alícuota desde un catálogo con valor numérico.
8. Front (después de la prueba integral): el editor, la vista previa en la página, el panel y la tabla del bloque.

**Resultado externo**

9. ~~RE1: tablero y decisión; el contrato con la etapa 3 en las notas.~~ Hecho: RE1-A.
10. ~~Los DTO, `BusinessFormRecordResults` con su migración y el helper que lo guarda.~~ Hecho (dea532ec).
11. ~~Las lecturas: detalle, planilla, Excel y PDF.~~ Hecho: detalle (dea532ec), planilla y Excel (4a391b3d), PDF (f204664b).
12. ~~Los avisos con datos del resultado, resueltos al ejecutar, y el punto donde una acción declara sus datos.~~ Hecho (c1f7f4ff); la etapa 3 pasa `declared` con los datos de su emisor.
13. Front: el editor de reglas, el detalle y la planilla.
14. *(sin tarea propia)* La etapa 3 conecta `IssueDocument`.

**Biblioteca**

15. ~~BI1: tablero y decisión.~~ Hecho: BI1-A.
16. ~~El puerto de estándares y `GET api/forms/library`.~~ Hecho (e2cb8606).
17. ~~«Agregar»: la copia con su origen sellado y su migración.~~ Hecho (e2cb8606); un catálogo que trae el propio estándar, cuando la etapa 3 lo necesite.
18. ~~Las preguntas atadas en el guardado y en la lectura del borrador.~~ Hecho (e2cb8606).
19. Front: la pantalla «Biblioteca», «Agregar» y el candado en el editor.

**Carpetas** (los cortes de su plan)

20. Backend: modelo, rutas y errores.
21. El filtro en los tres listados y el árbol podado.
22. El menú y el lugar de cada carpeta.
23. La carpeta del sistema, y la del estándar al agregar.
24. Front: ver y filtrar, ordenar y menú.

**Condiciones**

25. ~~CO1: tablero y decisión.~~ Hecho: CO1-A.
26. El borrador y publicar.
27. La captura y la carga.
28. WhatsApp con un `If`, junto con la tarea 6 en el perfil r13, con prueba con Meta.
29. Front: el editor y la carga.

**Cierre**

30. Las reglas en [formularios](../features/formularios.md), la fila de la etapa 2 en la hoja de ruta, la verificación de
    cierre de `AGENTS.md` con Docker y el front, la auditoría documental que retira este plan y la oferta del traspaso al
    multitenant.

## Pendiente

- La confirmación de la etapa 3 del corte del resultado externo (se le mandó el 2026-11-09).
- [Formularios sólo en la app](2026-11-09-formularios-solo-en-la-app.md), decidido el 2026-11-09 y sin programar: con
  «Sólo en la app» no corre nada de WhatsApp (SA2-A), así que los chequeos de WhatsApp de los cálculos y las condiciones
  sólo valen para los formularios que van por WhatsApp. Lo que se programe primero se adapta al otro.
