# Renglones · Etapa 4: plan de implementación («Las reglas»)

> **Para quien lo ejecute:** se trabaja tarea por tarea, con TDD (el test en rojo primero) y en `main`, sin worktrees
> ([AGENTS.md](../../AGENTS.md)). Es el modo liviano: siete tareas y el cierre, una revisión al terminar el backend y la
> suite completa sólo al cerrar la etapa. Las casillas (`- [ ]`) se marcan al terminar cada paso. Lo que sigue un patrón
> existente nombra su archivo de referencia; las reglas nuevas van escritas acá.

**Objetivo:** que las reglas lean el bloque de renglones (RG5-A). Una condición sobre una pregunta del bloque se cumple si
algún ítem la cumple, y varias del bloque buscan un mismo ítem, igual que «Filtros». En un mensaje, el bloque es un dato
más, con un ítem por línea. Con eso, la regla de «Recepción de insumos» avisa al Agrónomo si algún artículo llegó en mal
estado.

**Fuentes:**

- el [plan de renglones](2026-11-03-renglones-en-formularios.md): RG5-A, RG12-A, «Lecturas y reglas» y la Etapa 4;
- el [diseño registrado en el front](../../../ArquitecturaBaseFront/docs/design/renglones-en-formularios.md), que manda en
  pantallas y textos;
- el tablero RG5-A del lienzo provisorio [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M),
  versión 55;
- el plan de la [Etapa 2](2026-11-07-renglones-etapa-2-implementacion.md), que dice cómo se guardan los ítems, y el de la
  [Etapa 3](2026-11-07-renglones-etapa-3-implementacion.md), con la lectura neutral y «Filtros» por algún ítem;
- en [formularios](../features/formularios.md), las reglas (G1), el motor de acciones, el mensaje con datos (MD1-A) y las
  reglas en pasos (LR1-A).

Los textos dicen «ítem» (RG12-A): «Algún ítem con…» y «Si algún ítem de Artículos con…». El código y los documentos
siguen diciendo renglón (`rows`).

**Tecnología:** .NET 10, xUnit v3, Testcontainers con PostgreSQL; en el front, React, Zod, Vitest con MSW y Playwright.

**Depende de las etapas 2 y 3.** Las reglas leen lo que guarda la 2 (los ítems en `ValuesJson`) y deciden como el
«Filtros» de la 3 (`ItemConditions` y `SameItem`). Se programa cuando cierren las dos, con la exportación conjunta de sus
capturas.

**Visto bueno:** pendiente del usuario.

---

## Lo que hay hoy

Ya anda, sin cambios:

- Guardar las reglas valida las condiciones con `FormRecordFilterPolicy`, que desde la Etapa 3 acepta las preguntas del
  bloque y rechaza el bloque mismo.
- `GET .../rules/options` trae el bloque con `rows`, sus preguntas `repeated` y las opciones de las que son de un
  catálogo. El contrato del front (`canonicalFormFieldSchema`) ya los acepta.
- La lectura neutral trae el bloque como valor `group`, con sus ítems (`StructuredRecordReadRow`).

Lo que falla:

- El editor ofrece las preguntas del bloque como sueltas: `FormRulesSection` deja afuera el bloque, pero no sus preguntas.
  No dice «algún ítem» y ofrece «Tiene respuesta», «Sin respuesta» y «No se preguntó».
- `FormRecordConditionEvaluator` no lee los ítems: una condición con valor sobre una pregunta del bloque nunca se cumple, y
  «Sin respuesta» se cumple siempre.
- El mensaje deja el bloque vacío (`FormActionMessagePolicy.Value`, desde la Etapa 3). «Agregar dato» ofrece el bloque y
  sus preguntas sueltas, y «Así llega» muestra el bloque como «Ejemplo de respuesta».
- Las sugerencias («Qué sugerir después») usan el mismo editor, la misma lista y la misma evaluación
  (`FormSuggestionCandidatePolicy` y `FormRecordCaptureOriginPolicy`), con los mismos problemas.

## Cómo deciden las reglas

### Las condiciones

En `FormRecordConditionEvaluator.Matches`, con la misma regla que el SQL de «Filtros»:

- Los ítems salen de la entrada del bloque de la versión de la respuesta en `ValuesJson`
  (`[[{fieldId, value}, …], …]`). Sin entrada o con `[]`, no hay ítems.
- Las condiciones con valor sobre preguntas `repeated` las tiene que cumplir un mismo ítem. Cada una usa la comparación de
  hoy para su tipo: texto que contiene, sin distinguir mayúsculas, o igual; número y fecha; sí o no, y opción.
- La presencia sobre una pregunta del bloque mira la respuesta entera, como en «Filtros»: con «answered», algún ítem la
  respondió; con «unanswered», ninguno, y con «notAsked», la versión no la tiene. El editor no la ofrece (ver «Lo que el
  tablero no fija»), pero el evaluador la resuelve como el SQL para que los dos sigan diciendo lo mismo.
- Una respuesta de una versión sin la pregunta no cumple ninguna condición con valor sobre ella, como hoy. Una de antes del
  bloque no cumple ninguna sobre sus preguntas.
- Las demás condiciones no cambian.

Las sugerencias usan la misma evaluación, así que también buscan un mismo ítem.

### El mensaje

En `FormActionMessagePolicy.Render`, el dato del bloque escribe sus ítems:

```text
Llegó mercadería en mal estado en el remito 0003-00004521 de Agroinsumos del Litoral S.A.:
1. GLIFOSATO 64% · 200 · Litros · Sí
2. ATRAZINA 50% · 100 · Litros · Sí
3. UREA GRANULADA · 5000 · Kilos · No
```

- Cada ítem va en su línea, numerado desde 1. Sus valores van separados por « · », en el orden de las preguntas del bloque
  de su versión y sin los que quedaron sin respuesta. Cada valor lleva el formato que tiene hoy en un mensaje (`Value`).
- El primer ítem también empieza en una línea nueva, salvo al comienzo del mensaje o después de un salto de línea.
- Sin ítems, el dato queda vacío, como una pregunta sin respuesta. Un ítem vacío deja sólo su número («2.»). Una respuesta
  de una versión sin el bloque también lo deja vacío.
- Un mensaje de más de 4096 caracteres, o de 1024 con el PDF, falla como hoy (`MessageTooLong`), sin recortar.

Los avisos salen como texto libre por WhatsApp, sin plantillas de Meta, así que los saltos de línea llegan.

### Guardar las reglas

- Las condiciones no cambian: `FormRulePolicy` las valida con `FormRecordFilterPolicy`.
- Un dato del mensaje puede ser el bloque, pero no una de sus preguntas. `ValidPlaceholders` da
  `Forms.Rule.PlaceholderInvalid` en `rules[i].actions[j].text.<idioma>` si el dato es una pregunta `repeated`, por su
  identidad o por su nombre entre llaves. Hasta esta etapa, «Agregar dato» las ofrecía y en el mensaje quedaban vacías.

### Las opciones

`GET .../rules/options` no cambia. Trae las preguntas del borrador y después las de versiones anteriores que ya no están.
Cada pregunta `repeated` va detrás de su bloque, o al final si se retiró; el front la pone con el último bloque anterior a
ella.

## El editor y la lista

### Las columnas de las condiciones

`conditionColumns`, en `rules.ts`, arma las columnas desde las opciones, para reglas y sugerencias. Cada pregunta suelta
va como hoy, y el bloque va como una columna `group` con sus `questions`, igual que en la planilla de la Etapa 3. Así
`responseQuestionColumns` encuentra las preguntas del bloque y cada condición sabe de qué bloque es. Reemplaza las dos
copias que hoy arman las columnas en `FormRulesSection` y `FormSuggestionsSection`.

### Las condiciones en el editor

En `FormConditionsEditor`, como el tablero RG5-A:

- «Pregunta» ofrece las preguntas del bloque en su lugar, como «Artículos › ¿Llegó en buen estado?». El bloque mismo no se
  elige.
- Con una pregunta del bloque, la condición empieza con «Algún ítem con», en gris, a la izquierda de «Pregunta» y a la
  altura de los controles.
  - Si alguna condición es del bloque, todas las filas reservan esa columna, así las demás quedan alineadas.
  - En una columna, como en el celular, va arriba de «Pregunta».
- Una pregunta del bloque ofrece sólo los operadores con valor de su tipo, como «Filtros»: «Es una de», «Contiene», «Es
  igual a», «Es mayor o igual a» y «Es menor o igual a».
- Al elegir una pregunta del bloque, la condición arranca con su primer operador. «Agregar condición» arranca con la
  primera pregunta que se puede elegir.

### La lista de reglas en pasos

`ruleConditionSummaries` junta las condiciones del bloque en una línea, en el lugar de la primera de ellas. `ConditionLines`
la escribe con «Si» o «y» y las palabras que unen en gris, y el bloque, cada pregunta y cada valor en negrita:

- con una: «Si algún ítem de **Artículos** con **¿Llegó en buen estado?** es **No**»;
- con dos: «Si algún ítem de **Artículos** con **Artículo** es **GLIFOSATO 64%** y **Cantidad** desde **100**».

Así la regla dice que las busca en un mismo ítem. Las demás condiciones siguen una por línea. Vale para la lista, el
historial y las sugerencias.

### El bloque como dato del mensaje

- «Agregar dato» ofrece el bloque con su nombre, en su lugar en el formulario, y no sus preguntas (`ruleMessageData`).
- «Así llega» pone el bloque con un ítem de ejemplo en su línea y los ejemplos de hoy de cada pregunta: «1. GLIFOSATO 64% ·
  12.500,5 · Litros · Sí» (`ruleMessagePreview`). Sigue la misma regla del salto de línea que el aviso.
- El recuadro del mensaje en la lista muestra el dato «Artículos», como cualquier otro.

### Textos

En `forms.json`, es y en:

- `rules.someItem`: «Algún ítem con» / «Any item with»;
- `rules.blockQuestion`: «{{block}} › {{question}}», igual en los dos;
- `rules.conditionWords.someItemOf`: «algún ítem de» / «any item of»;
- `rules.conditionWords.with`: «con» / «with».

## Lo que el tablero no fija

Se resuelve con la opción más simple que cumple el plan y las reglas vigentes:

- **Los operadores de una pregunta del bloque.** Sólo los que llevan valor, como «Filtros» (PL2-A): sin «Tiene
  respuesta», «Sin respuesta» ni «No se preguntó». Con «Algún ítem con» se leerían mal: «algún ítem con Observaciones sin
  respuesta» querría decir que ningún ítem la tiene. Además, RG5-B, que miraba todos los ítems o los contaba, quedó
  descartada.
- **Dos condiciones del bloque en el editor.** Cada una empieza con «Algún ítem con», como dibuja el tablero y como
  «Filtros» dice «En algún ítem de Artículos» en cada pregunta. Que es un mismo ítem lo dice la lista, en una sola línea.
- **«Así llega»** lleva un solo ítem de ejemplo, como el tablero, con los valores de ejemplo de hoy. Un número es
  «12.500,5», aunque el tablero dibujó «1».
- **Los números del aviso** van con el formato de hoy en los mensajes, sin separador de miles («5000»). El tablero dibujó
  «5.000».
- **Las sugerencias** comparten el editor, la lista y la evaluación: también dicen «Algún ítem con» y buscan un mismo ítem.
- **Una pregunta del bloque suelta en un mensaje guardado antes** se ve con su nombre. Al guardar, el servidor la rechaza
  con el error de siempre debajo del mensaje (`PlaceholderInvalid`): se quita y se agrega «Artículos».
- **Una regla que apuntaba a una pregunta que pasó al bloque** sigue apuntando a la vieja, como cuando cambia el tipo,
  porque pasar al bloque le da otra identidad (RG6-A). Se cambia a mano; la prueba física lo hace.

## Coordinación

- **La Etapa 2** (`local_9b2ea60b-52c8-4faa-86fc-aba5b6bd4d04`) tiene pendientes su Tarea 9 (publicar y el editor, que
  espera RG10-D y RG12-A) y el cierre con la exportación conjunta. Esta etapa suma claves `rules.*` a `forms.json`, como
  su Tarea 9 suma `publication.*`, así que se programa después.
- **La Etapa 3** (`local_062834b7-ee5b-49f2-8555-bf9146cd7334`) cierra con la exportación, las escenas visuales y la
  verificación. Esta etapa toca `responses.ts` (`ruleConditionSummaries`) y `FormRowsReadHttpTests.cs` (saca la captura de
  su semilla a un helper): va después de su cierre.
- **RG10-D y RG12-A** (el chat de la Etapa 1, `local_81a26003-86cd-4de5-bda0-9bec94d80664`) cambian textos de
  `forms.json` y del editor del formulario, no los de las reglas.
- **Elegir primero por un campo** (LD1-A, la sesión «IMPORTANTE Filtro») programa la carga del front y no comparte archivos
  con esta etapa.
- **El exportador** (`export-responses-http.ps1`) lo tocan todas las etapas: antes de sumar la procedencia de la cohorte de
  reglas se mira `git status`.
- **El usuario prueba con la app levantada:** el `src` del front se edita con su permiso, porque Vite le recarga la página,
  y se avisa cuando haya que reiniciar la Api.

## Compilar y probar

Con la salida propia, para no chocar con otras sesiones ni con la app abierta:

```bash
dotnet build ArquitecturaBase.slnx --artifacts-path C:/Users/ezequ/source/repos/ArquitecturaBase/.artifacts/renglones-e4
```

```bash
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj --artifacts-path C:/Users/ezequ/source/repos/ArquitecturaBase/.artifacts/renglones-e4 -- --filter-class "<Namespace.Clase>"
```

En cada corte, el build y los tests rápidos del área: `Application.UnitTests` y `ArchitectureTests` enteros antes de
commitear. La integración nueva se escribe en rojo y corre con PostgreSQL al cierre. Otras sesiones trabajan en el mismo
checkout: se preparan sólo los archivos y los fragmentos propios.

## Tareas

### Tarea 1 · Las condiciones buscan un mismo ítem

- [ ] Esperar el cierre de las etapas 2 y 3, revisar `git status` y ajustar «Lo que hay hoy» a lo que quedó.
- [ ] Tests en rojo en `FormRecordConditionEvaluatorTests`, con una respuesta de tres ítems (Semilla, 200,5 y «Sin
  daños»; Semilla y 100; Fertilizante y 7) y otra de una versión sin el bloque:
  - una condición que cumple cualquier ítem;
  - dos en el mismo ítem contra dos en ítems distintos: «Semilla y desde 200» se cumple, y «Fertilizante y desde 200», no;
  - una condición de afuera del bloque junto a una del bloque, que se cumple aparte;
  - la presencia por respuesta: algún ítem, ninguno y la versión sin la pregunta;
  - un bloque sin ítems (`[]`) y una respuesta de antes del bloque, que no cumplen ninguna condición con valor.
- [ ] Tests que fijan lo que ya anda:
  - en `FormRulePolicyTests`, una condición sobre una pregunta del bloque se guarda, y una sobre el bloque da
    `ContentInvalid` en `rules[0].conditions`;
  - en `FormRuleOptionsPolicyTests`, el bloque con `rows` y sus preguntas `repeated` detrás, con las opciones del catálogo
    de una pregunta del bloque.
- [ ] `FormRecordConditionEvaluator`.
- [ ] Integración en `FormActionConditionParityTests`, con la semilla de la Etapa 3 (`SeedRowsReadAsync`). El evaluador y
  el SQL de la planilla dan los números esperados en cada caso: una condición en cualquier ítem, dos en el mismo ítem y en
  ítems distintos, una de afuera junto a una del bloque, «contiene» en las notas de los ítems, la presencia y un bloque
  sin ítems. Se compara contra conjuntos fijos, no sólo entre sí, y corre con PostgreSQL al cierre.
- [ ] Commit: `feat(formularios): las condiciones de una regla buscan un mismo ítem del bloque`.

### Tarea 2 · El bloque en el mensaje

- [ ] Tests en rojo:
  - en `FormRecordRowsPresentationTests`, el mensaje con los ítems reemplaza a
    `A_rule_message_that_names_the_block_leaves_it_empty_until_the_rules_read_its_items`. Cubre un ítem por línea,
    numerado, con los valores en orden, separados por « · » y sin los vacíos, en es y en (el decimal con coma, el sí o no
    traducido y la opción en su idioma). También el salto antes del primero, sin salto al comienzo del mensaje ni después
    de otro salto; el bloque sin ítems, vacío; el ítem vacío, «2.», y la versión sin el bloque, vacío;
  - en `FormRulePolicyTests`, `PlaceholderInvalid` con una pregunta del bloque como dato, por su identidad y por su nombre
    entre llaves, y el bloque como dato, que se guarda.
- [ ] `FormActionMessagePolicy.Render` y `FormRulePolicy.ValidPlaceholders`.
- [ ] Commit: `feat(formularios): el mensaje de una regla trae el bloque con un ítem por línea`.

### Tarea 3 · Las reglas con el bloque por HTTP

- [ ] Sacar la captura de `SeedRowsReadAsync` a `CaptureRowsAsync`, en `FormRowsReadHttpTests.cs` y sin cambiar sus tests,
  para poder guardar las reglas antes de capturar.
- [ ] Integración en rojo en `FormRowsRuleHttpTests`, parcial de `FormDraftEndpointsTests` como `FormRowsReadHttpTests`, con
  un operador que administra reglas y lee respuestas:
  - las opciones traen el bloque y sus preguntas, en es y en, con las capturas `rows-options-{es,en}`;
  - se guarda una regla «Algún ítem con Artículo es Semilla y Cantidad desde 200» que le avisa a quien cargó «Llegó:
    {bloque}», con la captura `rows-saved-es`;
  - una condición sobre el bloque y una pregunta del bloque como dato dan 400 en su ruta;
  - una respuesta con Semilla y 200,5 en el mismo ítem crea el aviso con el cuerpo de tres líneas, y otra con Semilla y
    200,5 en ítems distintos lo deja «No aplica». Los runs se leen de la base.
- [ ] La cohorte `forms-rules-http` suma `FormRowsRuleHttpTests.cs` y `FormRowsReadHttpTests.cs` a su procedencia en
  `export-responses-http.ps1`. No fija inventario.
- [ ] Commit: `test(formularios): las reglas con el bloque por HTTP, con sus capturas`.

### Tarea 4 · Revisión del backend

- [ ] Un revisor sobre las tareas 1 a 3 y sus correcciones, sin cambios de bytes en lo que ya existe. Mira la paridad con
  «Filtros», que un formulario sin bloque siga igual que hoy, el mensaje y los datos. Unitarios de formularios y
  ArchitectureTests enteros antes de cada commit.

### Tarea 5 · El front: las condiciones sobre el bloque

- [ ] Esperar el cierre de las etapas 2 y 3 en el front, pedirle permiso al usuario para editar el `src` y mirar
  `git status`.
- [ ] Abrir el tablero RG5-A del lienzo provisorio y anotar lo que falte.
- [ ] Tests en rojo:
  - `rules.test.ts`: `conditionColumns` con el bloque y sus preguntas, también una retirada al final;
  - `FormConditionsEditor.test.tsx`: «Artículos › ¿Llegó en buen estado?», «Algún ítem con», los operadores con valor, el
    operador y el valor al elegir una pregunta del bloque, «Agregar condición» con el bloque primero y las filas
    alineadas;
  - `FormSuggestionsSection.test.tsx`: la misma condición en una sugerencia.
- [ ] `rules.ts`, `FormConditionsEditor`, `FormRulesSection`, `FormSuggestionsSection` y los textos es y en de
  `forms.json`.
- [ ] Comparar con el tablero.
- [ ] Commit en el front: `feat(formularios): las condiciones sobre el bloque dicen «Algún ítem con»`.

### Tarea 6 · El front: la lista en pasos

- [ ] Tests en rojo:
  - de `ruleConditionSummaries`, donde está hoy: una y dos condiciones del bloque, también separadas por una de afuera;
  - `FormRulesSection.test.tsx`: la regla plegada, la lista y el historial.
- [ ] `responses.ts` y `ConditionLines`.
- [ ] Comparar con el tablero (la lista, con LR1-A).
- [ ] Commit en el front: `feat(formularios): la lista lee juntas las condiciones del bloque`.

### Tarea 7 · El front: el bloque en el mensaje

- [ ] Tests en rojo:
  - `ruleMessage.test.ts`: el bloque en «Agregar dato» sin sus preguntas, y «Así llega» con un ítem en su línea, sin
    salto al comienzo y con el ejemplo de cada tipo;
  - `RuleMessageField.test.tsx`: el menú y el ejemplo.
- [ ] `ruleMessage.ts` (`repeated` en `RuleMessageQuestion`, `ruleMessageData` y `ruleMessagePreview`). En
  `prepareRules`, `hasMissingRuleData` busca en todas las preguntas (`responseQuestionColumns`).
- [ ] Escena visual en `formRules.spec.ts`, con un escenario de reglas con el bloque en `formRuleFixtures.ts`: el editor
  con dos condiciones del bloque y el mensaje con «Así llega», y la lista, en es y en a 1440, 390 y 320. Va con la corrida
  de `test:visual` del cierre.
- [ ] Comparar con el tablero.
- [ ] Commit en el front: `feat(formularios): el bloque es un dato del mensaje, con un ítem por línea`.

### Tarea 8 · Cierre

- [ ] Exportar la cohorte `forms-rules-http` desde una copia, como en las etapas 2 y 3, porque el exportador corre la suite
  entera y escribe en el front. En el front, `rulesHttpContract.test.ts` suma `rows-options-{es,en}` y `rows-saved-es`, y
  se actualizan las listas de los tests de contrato que cuentan capturas.
- [ ] Verificación:
  - la integración con PostgreSQL y Redis, `dotnet test` completo en tandas y el build sin advertencias;
  - en el front, `build`, `lint`, `test` y `test:visual` en tandas;
  - el recorrido es y en a 1440, 390 y 320;
  - `aspire stop` si se levantó la app.
- [ ] Docs:
  - [formularios](../features/formularios.md), en las reglas (G1 y el mensaje con datos) y en «El bloque de renglones en
    la lectura», que hoy dice que el mensaje deja el bloque vacío;
  - los `AGENTS.md` de Services y de sus tests (unitarios e integración); en el front, el de `components`, «Lo que cambia
    de lo que hay» del diseño de renglones, y las reglas en pasos y el mensaje con datos de `formularios-whatsapp.md`;
  - este plan y el de renglones.
- [ ] Prueba física, guiando al usuario clic por clic y sin cargar datos por él. Va dentro de la prueba integral final de
  renglones, como eligió para la Etapa 2:
  - en «Recepción de insumos», ya con el bloque «Artículos», cambiar la regla que avisa al Agrónomo. La condición pasa a
    «Algún ítem con Artículos › ¿Llegó en buen estado? es No» y el mensaje suma «Artículos». Revisar «Así llega» y la
    lista;
  - cargar el remito de tres artículos, con uno en mal estado, desde la app y por WhatsApp: al Agrónomo le llega con un
    artículo por línea;
  - cargar uno con los tres en buen estado: la acción queda «No aplica»;
  - con dos condiciones del bloque que se cumplen en ítems distintos, no avisa, y «Filtros» con las mismas condiciones
    tampoco encuentra la respuesta.
- [ ] Si las cuatro etapas quedaron cerradas y probadas, consolidar y retirar los planes de renglones con la
  [auditoría documental](../guides/auditar-documentacion.md), y ofrecer el traspaso al multitenant.
