# La IA arma formularios y catálogos, etapa 1: traspaso 8 al multitenant (2026-11-06)

Autor: el chat guía de ArquitecturaBase. Destino: ArquitecturaBaseMutitenant, para sus etapas **E25 · Documentos** («Armar
desde el asistente de la web», decisiones 75 y 102), **E23 · Asistente** y **E12 · Catálogos**, y para su módulo
`assistant` (ADR 0048).

En la prueba completa del 2026-11-03, el usuario preguntó si podía pedirle a la IA, en lenguaje natural, con un PDF o con un
audio, por WhatsApp o en el chat de la app, que armara un formulario o un catálogo y lo llevara al editor con todo cargado.
El 2026-11-04 eligió el diseño y aceptó el [ADR 0024](../decisions/0024-ia-arma-formularios-y-catalogos.md), y la etapa 1
(texto y audio) se programó entera del 2026-11-04 al 2026-11-05. Este traspaso cuenta:
- qué decidió el usuario;
- cómo quedó;
- qué se encontró al programarla;
- qué tiene que decidir el multitenant al adaptarla.

**Como los anteriores, no es código para copiar** (decisión 18 de su hoja de ruta) ni hace falta para dibujar (decisión
108): los tableros de acá son un ejemplo para adaptar con sus piezas y su decisión 43.

**No depende de los traspasos 4 a 7.** Toca otras etapas: se puede incorporar aparte.

## 1. En pocas palabras

- **Decidido y programado:** en el chat de Inicio y por WhatsApp, con un texto o un audio, la IA propone un formulario o un
  catálogo Manual sin opciones. Una persona lo crea con un botón: el formulario queda en borrador y el catálogo, inactivo
  hasta que alguien lo aprueba (§2 y §3).
- **La regla de fondo** (ADR 0024): la IA propone y nunca crea, publica, activa, importa ni cambia lo que existe. Crear es
  siempre una acción de la persona, idempotente por propuesta.
- **Lo que choca con el multitenant** (§5): en su E25, dos herramientas del chat crean el borrador y el catálogo al pedirlo
  (el pedido es la confirmación); además, no arma nada por WhatsApp y no tiene audio en el chat del Inicio. Las tres cosas
  las decide el usuario.
- **Falta acá** (§6): lo pendiente quedó al día en el [traspaso 11](2026-11-08-traspaso-multitenant-ia-pregunta-y-corrida.md),
  que completa este. Las etapas 2 (planillas) y 3 (documentos) siguen sin programar.
- **Parte de los commits no está en GitHub.** Desde esta PC se leen en `C:\Users\ezequ\source\repos\ArquitecturaBase` y
  `C:\Users\ezequ\source\repos\ArquitecturaBaseFront` (§7).

## 2. Lo que decidió el usuario

El 2026-11-04 eligió de a una las dos filas con opciones, en las dos la recomendada:

1. **En el chat de Inicio, una tarjeta en el hilo (IA1-A).** Debajo de la respuesta va la propuesta: la banda «Formulario» o
   «Catálogo», el nombre, el prefijo y cada pregunta con su tipo, si es obligatoria y sus opciones o su catálogo. «Ver cómo
   queda» abre la vista previa del editor en un diálogo, y «Crear borrador» o «Crear catálogo» va en la banda y en el
   diálogo. Se ajusta conversando: cada cambio trae otra tarjeta, y la anterior se pliega a una línea «Reemplazada» que ya no
   se puede crear. Después de crear, la tarjeta queda plegada con «Borrador creado» y el enlace. Descartó IA1-B, la propuesta
   en un panel al costado del hilo.
2. **Por WhatsApp, un resumen con botones (IA3-A).** El asistente contesta con la propuesta en texto: el nombre, el prefijo y
   hasta 10 preguntas con su tipo. Ofrece «Crear borrador» o «Crear catálogo», y «Cambiar algo», que pide el cambio por
   escrito o con un audio y vuelve con otro resumen. Sólo vale el botón de la última propuesta. Crear contesta con el enlace
   para revisarla y publicarla desde la app. El catálogo de personas con filtros (H4), que antes se creaba en cuanto el
   asistente entendía el pedido, pasa a contestar igual. Descartó IA3-B, crear el borrador al entender el pedido.

Sin opciones, por lo ya decidido (IA2): «Crear borrador» deja el formulario en borrador, sin publicar, y abre el editor de
siempre. «Crear catálogo» lo deja inactivo con la banda «Propuesta del asistente» de H4, que dice quién lo pidió y dónde, con
«Descartar propuesta» y «Aprobar y activar».

Resuelto por defecto, sin objeción del usuario:
- «Armar con la IA» va junto a «Nuevo formulario», junto a «Nuevo catálogo» y en cada tema de «Formularios que faltan», y abre
  Inicio con el pedido empezado. Inicio vacío suma la sugerencia «Armame un formulario para…».
- Arma formularios quien puede diseñarlos, y catálogos quien puede administrarlos. Sin esos permisos, Inicio queda como
  antes; el micrófono aparece igual, para cualquier consulta.
- Antes de proponer, la IA lee los nombres y los campos de lo que la persona puede ver, para no repetir un nombre y usar los
  catálogos que existen.
- No cambia ni copia lo que existe: lo dice y da el enlace. Un nombre o un prefijo en uso lo dice la tarjeta, sin crear nada.
- Un pedido ambiguo pregunta qué hacer.
- La procedencia (canal, quién lo pidió y cuándo, perfil, instrucciones y capacidades) queda guardada con lo creado.

## 3. Cómo quedó

### Backend

- **Cuatro capacidades del módulo de formularios en el motor único de IA**, sin otro cliente:
  - dos lecturas de lo que existe, cada una con su permiso de leer: los formularios (nombre, prefijo, estado y preguntas) y
    los catálogos (nombre, tipo de valor y campos), sin respuestas, opciones ni personas;
  - dos propuestas: un formulario entero y un catálogo Manual sin opciones.
- **Validar sin recortar.** El módulo valida la propuesta con las reglas de la autoría: tipos, largos, 40 preguntas, prefijo y
  catálogos activos que la persona puede leer. Un dato inválido vuelve al modelo como rechazado, con la ruta de cada
  argumento, para que lo corrija en el mismo turno. Nunca se elige una propuesta rechazada ni se recorta un dato.
- **Inicio.** Suma sólo las capacidades que su dueño ofrece en Inicio, filtradas por permisos. Cierra con una única salida
  terminal (versión 2), que puede nombrar una propuesta observada del turno, y usa instrucciones genéricas: el motor no
  nombra formularios. La historia lleva la última propuesta, para ajustarla. El tope de definiciones por pedido pasó de 12 a
  16, y la salida, de 1500 a 4096 tokens.
- **Crear lo pide la persona.** `POST /api/forms/definitions/assistant-proposals` y `POST /api/forms/catalogs/assistant-proposals`
  reciben el pedido del turno y la propuesta. El servidor la relee por un puerto del motor (sólo un turno completo de Inicio
  del mismo actor, vigente y con sus permisos) y rechaza una que otra posterior de la misma capacidad ya reemplazó: un
  catálogo pedido después de un formulario no lo reemplaza. Crea una sola vez: repetir devuelve lo creado, aunque la
  propuesta haya vencido. `GET …/assistant-proposals/{proposalId}` dice si ya se creó, para la tarjeta plegada.
- **El catálogo Manual propuesto** nace inactivo con la propuesta pendiente, igual que H4. «Aprobar y activar» sólo lo activa,
  y la edición común no activa una propuesta pendiente.
- **WhatsApp.** Un puerto neutral nuevo, que implementa el módulo de formularios, resume y crea una propuesta desde la
  conversación propia de la persona, sin sesión web. Cada botón lleva la referencia de su propuesta, y cada toque se
  contesta con una referencia propia. «Cambiar algo» deja la conversación esperando el cambio: el mensaje siguiente continúa
  la operación anterior del motor en la misma conversación, con sus permisos reservados. Una capacidad «no se puede», sin
  permisos y sólo para el canal, dice por qué no se arma nada (falta un permiso o lo pedido ya existe), y el módulo
  comprueba el motivo. El resumen no es una oferta: no gasta el cupo diario.
- **Audio en Inicio.** El navegador sube la grabación (WebM o MP4, hasta 1.000.000 bytes, como una nota de WhatsApp) como
  copia privada. La normalización del motor la transcribe con un perfil nuevo, que reconoce el formato por sus bytes. El
  turno toma la transcripción del servidor, nunca la del cliente, y el archivo se conserva mientras dure el contenido del
  turno.
- **Qué se manda al proveedor:** el pedido, con el audio ya transcripto, y la lectura de lo que existe (nombres y campos).
  Nunca respuestas, opciones cargadas ni datos de personas. Lo leído es un dato, nunca una instrucción.

### Front

- La tarjeta de la propuesta es una pieza nueva del chat (`shared/ui/assistant`). El contenido lo aporta el módulo de
  formularios por un punto de extensión, sin que Inicio nombre el módulo. Tiene sus estados: abierta, «Reemplazada» y
  «Borrador creado». «Ver cómo queda» usa la vista previa del editor.
- El micrófono va a la izquierda de la flecha. Graba con «Detener» y «Descartar», se escucha antes de mandarlo, y la
  transcripción queda en gris debajo del audio. Aparece cuando la instalación puede transcribir lo que graba el navegador y
  tiene almacenamiento privado.
- «Armar con la IA» lleva el pedido empezado en el estado de la navegación, nunca en la dirección.
- Un catálogo Manual propuesto muestra la banda «Propuesta del asistente», que nombra el canal («en el chat de Inicio»).

### Contratos, permisos y migraciones

- **Rutas nuevas:** `POST` y `GET /api/forms/definitions/assistant-proposals[/{proposalId}]`, `POST` y
  `GET /api/forms/catalogs/assistant-proposals[/{proposalId}]`, y `POST /api/ai/assistant/audio`,
  `GET /api/ai/assistant/audio/{requestId}` y `GET …/audio/{requestId}/file`. El turno de Inicio suma la propuesta y la
  referencia del audio, y el arranque del asistente dice si hay audio.
- **Permisos:** ninguno nuevo. Las capacidades usan los de diseñar formularios y administrar catálogos, y el chat, el de usar
  el asistente.
- **Migraciones:** `AddFormAssistantProposals` (la propuesta y su procedencia en el borrador y en el catálogo),
  `AddWhatsAppAssistantContinuation` (la operación que continúa «Cambiar algo») y `AddAssistantHomeAudio` (el audio de Inicio
  y su retención).
- **Ejemplos HTTP:** la cohorte `forms-assistant-proposals-http` (14 cuerpos) y la del asistente regenerada, exportadas al
  front.
- **Casos de evaluación:** el arnés de calificación suma 24 casos de Inicio y 18 del canal automático, en es y en: formulario,
  catálogo, personas H4, ajuste, corrección, nombre en uso, sin permiso, pedido ambiguo, instrucciones escondidas y cuatro
  dictados.

## 4. Lo que se encontró al programarla (no repetir)

1. **El nombre que lee la IA es el que compara crear.** Las lecturas daban el nombre en el idioma original, y crear comparaba
   el visible en el idioma de la propuesta: en inglés, el modelo no veía el nombre traducido. Ahora leen en el idioma de la
   conversación, con respaldo al original.
2. **Una propuesta inválida vuelve al modelo, no se arregla en silencio.** Rechazada con la ruta de cada argumento, el modelo
   la corrige en el mismo turno.
3. **Crear busca primero lo ya creado por esa propuesta.** Así un segundo toque devuelve lo mismo, aunque la propuesta ya haya
   vencido.
4. **El reemplazo es por capacidad.** Un catálogo pedido después de un formulario no invalida el formulario.
5. **Los topes del pedido al modelo cambian con capacidades nuevas.** Con todas, el asistente de WhatsApp llegaba a 14
   definiciones (el tope era 12), y agruparlas mezclaba permisos. Con 1500 tokens de salida, un formulario mediano se cortaba.
6. **En WhatsApp cada botón lleva su propuesta**, y sólo vale el del último resumen. Un botón viejo contesta y no crea nada.
7. **El audio que trae un cambio pedido no es una oferta**, así que no consulta el cupo diario de ofertas.
8. **El navegador graba WebM o MP4**, y la normalización sólo admitía Ogg y WAV. Hubo que sumar los dos formatos con su perfil,
   reconocidos por sus bytes y no por lo que dice el cliente.
9. **Un turno sin propuesta conserva la forma que lee el front**, y aprobar un Manual propuesto devolvía mapas nulos en lugar de
   vacíos: los dos se arreglaron con sus tests.
10. **Un CHECK de SQL escrito en varias líneas mete `\r`** con un checkout de fin de línea CRLF: va en una sola línea.

## 5. Adaptación al multitenant

Leído en el multitenant en `c339b58d` (front `c5172e65`), con la hoja de ruta hasta la decisión 108, la E23 y la E25.

| Tema | Su hoja de ruta y sus etapas hoy | En ArquitecturaBase | Qué evaluar |
| --- | --- | --- | --- |
| **Dónde se pide** | E25 «Armar desde el asistente de la web» (75 y 102): en el chat del Inicio, con un PDF, una planilla, una foto o un texto. Por WhatsApp sólo llegan documentos, y «Armar formulario con la IA» va en la bandeja web. | En Inicio y por WhatsApp, con texto o audio, y «Armar con la IA» en tres pantallas. Las planillas y los documentos son las etapas 2 y 3, sin programar. | **Armar por WhatsApp (IA3-A) lo decide el usuario.** «Armar con la IA» desde las pantallas no choca con nada. |
| **Quién crea** | En la bandeja, la persona toca «Crear borrador». En el chat, `create_form_draft` y `create_catalog_proposal` son herramientas que escriben, que estrena la E25 (75): «el pedido de la persona es la confirmación». La E23 las deja para la E25, y su ADR del asistente (D8) dejaba las acciones con confirmación para una versión siguiente. | Siempre una persona, con un botón; la IA nunca crea (ADR 0024), y crear es idempotente por propuesta. | Choca con el ADR 0024 de acá, que pide un botón de la persona para crear. **Lo decide el usuario:** la tarjeta con «Crear borrador» (IA1-A) o que el chat cree al pedirlo. |
| **Estado de lo creado** | Formulario en borrador; catálogos «Por aprobar» (`Proposed`), que aprueba quien tiene `formcatalogs.manage`. Uno sin aprobar frena publicar (`Forms.Draft.CatalogUnavailable`). La 102 suma los catálogos escritos a mano. | Borrador; catálogo Manual inactivo, pendiente hasta «Aprobar y activar». Un formulario propuesto sólo usa catálogos activos que la persona puede leer. | Coincide en lo esencial. Para sumar: que un formulario propuesto use sólo catálogos activos y legibles al crearlo, y que la edición común no active una propuesta pendiente. |
| **Audio en el chat** | Sin audio en el chat del Inicio; las transcripciones son de los chats de WhatsApp (27). | Micrófono en Inicio, con la transcripción del servidor y el audio retenido con el turno. | **Lo decide el usuario.** Si lo adopta, la copia privada y su retención van por organización. |
| **Módulo, proveedor y cupos** | Módulo `assistant` quitable y apagado, que se prende por organización (D3), con `[FeatureGate("assistant")]`. Cupo de 50 mensajes por persona y día y un tope mensual en USD por organización (D10, D23 y su ADR 0051); la E25 suma un tope de lectura (41). Proveedor Anthropic con `IChatClient` (D6); cada organización elige el modelo y la clave (D2). | Motor único (ADR 0020) con un perfil calificado por instalación; las capacidades las aporta el módulo de formularios. | Las capacidades de armar entran en su módulo y su cupo. La propuesta y la tarjeta van por un puerto neutral, así formularios y asistente se siguen quitando por separado. Los topes de definiciones y de salida dependen de su proveedor y su modelo: medirlos con un formulario de 40 preguntas. |
| **Lo que lee la IA** | La E25 lee el documento o la planilla que se pasa. | Lee nombres y campos de lo que la persona puede ver, en el idioma de la conversación. | Por organización: la lectura sólo ve lo de su organización y, con empresas, lo que la persona puede ver. El contenido allá no tiene idioma (35): se lee el único nombre, y el problema del punto 1 del §4 no aparece. |
| **Permisos** | `forms.read`, `forms.manage`, `formcatalogs.read` y `formcatalogs.manage`. | Los equivalentes de diseñar formularios y administrar catálogos. | Mapear a sus nombres, con el alcance de empresa si un formulario es de una empresa. |
| **WhatsApp** | No se arma nada por WhatsApp. | El resumen con sus botones, en la conversación propia del asistente de una cuenta vinculada. | Si se adopta, la conversación es con el número de la organización y la cuenta tiene que estar vinculada en esa organización. |
| **Datos** | — | La propuesta y su procedencia, guardadas con el borrador y con el catálogo. | En tablas del esquema `tenant`, con su test de aislamiento. |

**Lo que tiene que decidir el usuario allá:**
- crear con un botón de la persona (IA1-A, como pide el ADR 0024 de acá) o que el chat cree al pedirlo, como dice hoy su E25;
- armar también por WhatsApp (IA3-A);
- el audio en el chat del Inicio.

## 6. Lo que falta acá

Está al día en el [traspaso 11](2026-11-08-traspaso-multitenant-ia-pregunta-y-corrida.md) (2026-11-08), que completa este. El
usuario decidió que un pedido ambiguo por WhatsApp pregunte y espere (2026-11-07), y la corrida con el proveedor real quedó en
verde, con dos ajustes del producto; la del dictado por WhatsApp, el 2026-11-09. Lo que sigue pendiente está en el §7 de ese
traspaso: la prueba manual, el perfil de transcripción del navegador en las demás instalaciones y las etapas 2 y 3.

## 7. Dónde está cada cosa en ArquitecturaBase

- **Regla vigente:** [formularios](../features/formularios.md), en «La IA arma formularios y catálogos — Etapa 1»;
  [IA](../features/inteligencia-artificial.md), en el canal Inicio, `assistant_answer@2`, las definiciones y el audio; y
  [WhatsApp](../features/whatsapp.md), en «Armar formularios y catálogos (IA3-A)».
- **Decisión:** [ADR 0024](../decisions/0024-ia-arma-formularios-y-catalogos.md), que amplía los ADR 0017, 0020 y 0022.
- **Planes:** el [plan general](2026-11-04-ia-arma-formularios-y-catalogos.md), con las tres etapas, y el
  [plan de la etapa 1](2026-11-04-ia-arma-formularios-etapa-1.md), con las decisiones técnicas y el estado de cada tarea.
- **Diseño del front:** `ArquitecturaBaseFront/docs/design/ia-arma-formularios.md`.
- **Tableros:** el lienzo [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), filas «Armar con la
  IA · 0» a «· 3»: IA0 (el recorrido), IA1-A e IA3-A (elegidas), IA1-B e IA3-B (descartadas), IA2 e IA4 (las piezas).
- **Commits:**
  - backend: plan `d3c7c675`, ADR `cc358c5f` y `3646a6f7`, plan de la etapa `79b8f1d7`; tareas `6b113b2e`, `bd75ea73`,
    `cb172643`, `da624007`, `50d6f354`, `72196b2d`, `a39d5bf2`, `36923c7b`, `b0b9dacc`, `e14ba38d`, `fc47be1a`, `11882200`,
    `87024d2b` y `65fcfa6c`; cierre `662e72b4`, `a6c699a0` y `a57e66ad`; el nombre en el idioma de la conversación, `e6281663`;
  - front: diseño `e223d004`; `5ad868b0`, `b2fc0422`, `b0eaa847`, `b66026ed`, `261c470b`, `9973da3f` y `fd36e494`.
- **Código de referencia:**
  - backend: las capacidades `forms.authoring.forms`, `forms.authoring.catalogs`, `forms.authoring.propose-form`,
    `forms.authoring.propose-catalog` y `forms.authoring.decline`; `FormAuthoringProposalPolicy`, `IAssistantProposalLookup`,
    `IStructuredAuthoringProposalChannel`, `FormDefinitionAssistantProposalsController` y
    `FormCatalogAssistantProposalsController`; el audio en `AiAssistantController`;
  - front: `AssistantProposalCard`, `AssistantRecorder`, `useAudioRecorder`, `useAssistantDictation` y `AssistantTurnAudio`, en
    `shared/ui/assistant` y la feature del asistente, y la tarjeta de formulario y de catálogo en `business-forms`.

## 8. Prompt para el chat guía del multitenant

```text
Leé el traspaso 8 de ArquitecturaBase, en
C:\Users\ezequ\source\repos\ArquitecturaBase\docs\plans\2026-11-06-traspaso-multitenant-ia-arma-formularios.md.
Cuenta la etapa 1 de «la IA arma formularios y catálogos», que allá se decidió el 4/11 y se programó hasta el 5/11:
en el chat de Inicio y por WhatsApp, con un texto o un audio, la IA propone un formulario o un catálogo, y una persona
lo crea con un botón, como borrador o catálogo inactivo. Toca tu E25 («Armar desde el asistente de la web», decisiones
75 y 102), tu E23 y tu módulo assistant. No depende de los traspasos 4 a 7. Parte de los commits no está en GitHub:
leelos en C:\Users\ezequ\source\repos\ArquitecturaBase y ...\ArquitecturaBaseFront.

1. Evaluá el traspaso contra tu E25, tu E23, tu E12 y tu ADR del asistente. No es código para copiar (tu decisión 18)
   ni hace falta para dibujar (tu decisión 108).
2. El §5 dice qué evaluar tema por tema. Los tres puntos del final (crear con un botón o que el chat cree al pedirlo,
   armar por WhatsApp y el audio en el chat del Inicio) los decide el usuario: preguntáselos de a una pregunta, con las
   opciones y la recomendada marcada. Lo mismo para cualquier otra adaptación que tenga que decidir él.
3. Actualizá tu E25 y lo que toque de la E23 y la E12 con lo que se adopte, y sumá la lista de «no repetir» del §4.
4. Decile al usuario qué tableros habría que dibujar o ajustar. Como ejemplo para adaptar, tiene IA0 a IA4 en el lienzo de
   ArquitecturaBase (https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), filas «Armar con la IA».
5. No programes nada. Commits chicos en español, en main, sin push.

Al terminar, contame qué cambió en tus specs, qué decisiones quedaron pendientes y qué tableros hay que dibujar.
```

Este traspaso se retira cuando el multitenant lo haya incorporado a sus etapas.
