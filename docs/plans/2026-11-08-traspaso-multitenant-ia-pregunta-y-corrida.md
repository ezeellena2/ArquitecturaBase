# La IA arma formularios, después del traspaso 8: traspaso 11 al multitenant (2026-11-08)

Autor: un chat de ArquitecturaBase. Destino: ArquitecturaBaseMutitenant, para las mismas etapas que el traspaso 8: **E25 ·
Documentos** («Armar desde el asistente de la web», decisiones 75 y 102), **E23 · Asistente** y su módulo `assistant` (ADR 0048).

Completa el [traspaso 8](2026-11-06-traspaso-multitenant-ia-arma-formularios.md) con lo que pasó en ArquitecturaBase después de
escribirlo, que su §6 daba como pendiente:
- la decisión que faltaba: un pedido ambiguo por WhatsApp pregunta y espera (§2);
- la corrida con el proveedor real, en verde (§3);
- dos ajustes del producto que dejó esa corrida (§4);
- lo nuevo para no repetir (§5), la adaptación (§6), lo que falta acá (§7) y dónde está cada cosa (§8).

**Como los anteriores, no es código para copiar** (decisión 18 de su hoja de ruta) ni hace falta para dibujar (decisión 108).
**Se lee después del 8** y no depende de los demás traspasos.

## 1. En pocas palabras

- **Decidido y programado el 2026-11-07:** por WhatsApp, un pedido que no dice qué armar pregunta con un texto fijo y espera. El
  mensaje siguiente, escrito o con un audio, continúa el mismo pedido (§2).
- **La corrida real, el 2026-11-08,** con OpenAI `gpt-6.1-sol`: `ia-authoring` 46/46 y cuatro audios, `home-existing` 20/20 y
  `channel-stage5` 30/30. Los dos riesgos que el plan pedía medir entraron con margen (§3).
- **Dos ajustes del producto** que encontró esa corrida (§4): un prefijo pedido que no cumple el formato se cambia por el válido
  más parecido, sin preguntar, y las aclaraciones y los «no se puede» van sin cifras.
- **Para el multitenant no hay decisiones nuevas.** Siguen las tres del §5 del traspaso 8; si el usuario adopta armar por
  WhatsApp, la pregunta va con esa respuesta. Las reglas del §4 y la lista del §5 valen con cualquier opción (§6).
- **Falta acá** el dictado por WhatsApp como caso del arnés, la prueba manual y las etapas 2 y 3 (§7).

## 2. «Pregunta y espera» por WhatsApp

El dibujo decía «pregunta y espera», pero el canal sólo manda textos fijos y no tenía cómo preguntar en un pedido de armado. El
2026-11-07 el usuario eligió que pregunte y espere, como en Inicio, y se programó ese mismo día (87ed0482):

- **Un motivo más para no proponer.** `forms.authoring.decline` suma `unclear`: la persona pide armar algo, pero no dice lo
  suficiente para proponerlo (para qué es, qué pregunta o qué guarda, o si es un formulario o un catálogo). La descripción le pide
  al modelo que no adivine una propuesta, porque la respuesta llega en el mensaje siguiente.
- **El módulo de formularios comprueba el motivo.** `unclear` no nombra nada y sólo vale para quien puede armar un formulario
  (leer y administrar formularios) o un catálogo (leer y administrar catálogos). Si no, vuelve rechazado con `permissionMissing`,
  y el modelo tiene que usar otro motivo: a quien no puede armar nada se le dice que no tiene permiso.
- **Un texto fijo, en el idioma de la persona:** «No me queda claro qué querés que arme. Contame si es un formulario o un
  catálogo, para qué es y qué datos lleva, por escrito o con un audio.»
- **El mensaje siguiente continúa el mismo pedido,** por el camino de «Cambiar algo». La operación de la pregunta queda como la
  que cambia el mensaje siguiente: el motor suma ese turno a la misma conversación, con los permisos reservados, y la IA propone
  con el pedido y la respuesta juntos. No hizo falta otro mecanismo.
- **La pregunta no es una oferta:** no gasta el cupo diario, y el audio que la contesta no lo consulta, igual que el que trae un
  cambio pedido.
- **Inicio no cambió:** ahí el asistente ya preguntaba con sus palabras (una respuesta de tipo aclaración) y el turno siguiente
  lleva la historia.

El arnés de calificación suma la pregunta y su respuesta, en español y en inglés: el canal pasó a 22 casos.

## 3. La corrida con el proveedor real

Se hizo el 2026-11-08 con la conexión que guardó el usuario, OpenAI `gpt-6.1-sol` y el mismo perfil de texto de las
calificaciones anteriores. Fueron tres corridas, cada una con su base vacía:

| Corrida | Qué cubre | Resultado |
| --- | --- | --- |
| `ia-authoring` | Lo nuevo de la etapa 1, en es y en: 24 casos de Inicio y 22 del canal automático (formulario, catálogo, personas H4, ajuste, corrección, nombre en uso, sin permiso, pedido ambiguo con su respuesta, instrucciones escondidas y dictados). | 46/46, más 4/4 audios WebM y MP4 grabados en el navegador |
| `home-existing` | Los casos de Inicio que ya estaban, con las instrucciones nuevas de Inicio. | 20/20 |
| `channel-stage5` | Los casos del asistente automático de WhatsApp que ya estaban, con seis inyecciones. | 30/30 |

Lo que midió, para dimensionar topes:
- **Volumen:** entre las tres, 306 pasos del proveedor, 206 llamadas a herramientas, 621.116 tokens de entrada y 17.190 de
  salida, y 45 segundos de audio en cuatro transcripciones.
- **Por caso:** como mucho, cuatro pasos y tres llamadas a herramientas.
- **Contexto:** el más grande fue de 19.031 bytes, en un ajuste con historia, contra los 131.072 del perfil. La continuación
  llegó a 10.821 bytes y las herramientas, a 5.256.
- **Tiempos:** la mediana por caso fue de 16,04 s en `ia-authoring`, 6,86 s en `home-existing` y 10,91 s en `channel-stage5`.
  El caso más largo sumó 33,76 s en varios pasos, y ningún paso llegó a los 30 s que tiene el proveedor.
- **El micrófono:** el perfil de transcripción de lo que graba el navegador (OpenAI `gpt-transcribe`, WebM y MP4, hasta
  1.000.000 bytes) quedó calificado, y ese mismo día se declaró en la instalación local. Cada instalación lo declara por su
  cuenta, y sin él el micrófono de Inicio no aparece.

El alcance es el de las calificaciones anteriores: comprueba los hechos de los casos ficticios, sin evaluar a fondo el estilo ni
el texto libre. Los intentos anteriores de ese día no cuentan: se cortaron por fallas del arnés, por la carga de la máquina o por
los dos ajustes del §4.

## 4. Dos ajustes del producto (b09aed3c)

La corrida se cortó en una corrección que pedía el prefijo exacto «ped-26». El modelo terminó en el primer paso, sin leer ni
proponer, y el motor descartó la respuesta (`Ai.Assistant.InvalidOutput`). El plan pedía normalizar el prefijo y proponer, y el
motor rechaza una aclaración con cifras sin que las instrucciones lo dijeran. Se arreglaron las dos cosas:

1. **Un prefijo pedido que no cumple el formato se cambia sin preguntar.** El formato es de 1 a 12 mayúsculas o cifras,
   empezando con una letra. La descripción del prefijo en `forms.authoring.propose-form` pide usar el válido más parecido, en
   mayúsculas y sin espacios ni símbolos (por ejemplo, «PED26»), en lugar de preguntar. La propuesta lo muestra y la persona
   decide.
2. **Las aclaraciones y los «no se puede» van sin cifras.** El verificador del motor los descarta si tienen cifras, porque no
   llevan evidencia y no pueden afirmar cantidades. La regla ya estaba en el verificador, pero no en lo que lee el modelo: ahora
   la descripción de la salida final (`assistant_answer`, versión 2) lo dice. La versión 1, ya calificada, no cambió.

## 5. Lo que se encontró (no repetir)

Sigue la numeración del §4 del traspaso 8. Lo del arnés de calificación de acá (un archivo que Windows retiene, un scope
compartido, un control con una regla de idioma vieja) no aplica allá.

11. **Lo que el servidor rechaza de la salida del modelo tiene que estar en lo que el modelo lee.** La regla de las cifras estaba
    en el verificador y no en las instrucciones, así que el turno se perdía entero y la persona no recibía ni la aclaración.
12. **Un dato pedido que se puede arreglar se arregla y se muestra, sin preguntar.** Preguntar por un prefijo mal escrito cortaba
    el turno. Se pregunta sólo cuando no se sabe qué armar.
13. **Un canal que sólo manda textos fijos necesita un motivo propio para preguntar.** Sin él, un pedido ambiguo no tenía salida:
    había que adivinar una propuesta o no contestar.
14. **El motivo de preguntar también lo comprueba el módulo.** A quien no puede armar nada no se le pregunta qué armar: se le
    dice que no tiene permiso.
15. **La respuesta a la pregunta continúa la misma operación, por el camino que ya existía.** Se interpreta junto con el pedido y
    con los mismos permisos, sin otro mecanismo.
16. **La pregunta y su respuesta no son ofertas:** no gastan el cupo diario, aunque la respuesta llegue en un audio.
17. **Las pruebas sin el modelo real no podían encontrar los dos problemas del §4: los encontró la corrida con el proveedor.**
    Una etapa con IA no se da por cerrada sin esa corrida, que acá lleva el visto bueno del usuario y un presupuesto controlado.

## 6. Adaptación al multitenant

Leído en el multitenant en `c339b58d` (front `c5172e65`), el mismo punto que leyó el traspaso 8; en GitHub está igual. El §5 de
ese traspaso sigue valiendo, y esto es lo que suma:

| Tema | Su hoja de ruta y sus etapas hoy | En ArquitecturaBase | Qué evaluar |
| --- | --- | --- | --- |
| **Un pedido ambiguo por WhatsApp** | No se arma nada por WhatsApp (E25). Su asistente de WhatsApp (E23) tiene «preguntar con hasta 3 botones» entre sus seis acciones y arma cada turno con hasta 10 mensajes de las últimas 24 horas. | Pregunta con un texto fijo, y el mensaje siguiente continúa el mismo pedido (§2). | Sólo si el usuario adopta armar por WhatsApp (IA3-A, pendiente desde el traspaso 8). Su historia de 24 horas ya trae el pedido al turno siguiente; falta que la respuesta siga ese pedido de armado, que la pregunta no cuente como ofrecimiento (66) y que el módulo de formularios compruebe el motivo (puntos 14 a 16). |
| **Un pedido ambiguo en el chat del Inicio** | «Armar desde el asistente de la web» (75): el pedido es la confirmación, y el chat crea al pedirlo. | El asistente pregunta con sus palabras y espera; nada se crea sin el botón. | Con cualquiera de las dos formas de crear, un pedido que no dice qué armar pregunta antes. Si el chat crea al pedirlo, más todavía: adivinar dejaría un borrador que nadie pidió. |
| **El prefijo pedido** | Hasta 12 caracteres; se propone desde el nombre, en mayúsculas, y se edita hasta la primera publicación aceptada (E14). | Uno que no cumple el formato se cambia por el válido más parecido, sin preguntar, y la propuesta lo muestra (§4). | Sumarlo a la E25. Con la tarjeta, la persona lo ve antes de crear; si el chat crea al pedirlo, lo ve en el editor del borrador. |
| **Aclaraciones sin cifras** | Las respuestas redactadas de la E23 van sin precios ni compromisos que no estén cargados, y WhatsApp las valida antes de mandarlas. Su ADR se escribe antes de planificar la etapa. | El verificador descarta una aclaración o un «no se puede» con cifras, y la salida final se lo dice al modelo (§4). | Al escribir esa ADR y las herramientas de la E25: cada regla que valida el servidor va también en la descripción que lee el modelo (punto 11). |
| **Topes y tiempos** | Anthropic con `IChatClient` (D6), con el modelo que elige cada organización (D7 y D19). 50 mensajes por persona y día y un tope mensual en dólares (D10 y ADR 0051); la E25 suma un tope de lectura (41). | Las medidas del §3, con un solo proveedor y un solo modelo. | Usarlas para dimensionar, no como calificación: medir con sus modelos un ajuste con historia y un formulario de 40 preguntas. Un turno de varios pasos puede pasar los 30 s aunque ningún paso llegue. |
| **El micrófono en el chat del Inicio** | Sin audio en el chat del Inicio; los audios de WhatsApp se transcriben por un puerto del núcleo (27). | El micrófono aparece sólo donde se declaró el perfil de transcripción del navegador (§3). | Sólo si el usuario adopta el audio (pendiente desde el traspaso 8): por organización, el micrófono aparece sólo con una transcripción que acepte WebM y MP4. |

**Lo que tiene que decidir el usuario allá:** nada nuevo. Siguen las tres preguntas del §5 del traspaso 8: crear con un botón o
que el chat cree al pedirlo, armar por WhatsApp y el audio en el chat del Inicio. Si adopta armar por WhatsApp, «pregunta y
espera» va con esa respuesta, como acá.

## 7. Lo que falta acá

- **El dictado por WhatsApp** como caso combinado del arnés, que se está programando.
- **La prueba manual,** en Inicio y por WhatsApp.
- **El perfil de transcripción del navegador** en cada instalación que no sea la local.
- **Las etapas 2 y 3** (planillas y documentos), sin programar. El usuario las dejó para después de la prueba integral final.

## 8. Dónde está cada cosa en ArquitecturaBase

- **Regla vigente:** [formularios](../features/formularios.md#la-ia-arma-formularios-y-catálogos--etapa-1-2026-11-04), con el
  prefijo y «pregunta y espera»; [WhatsApp](../features/whatsapp.md), en «Armar formularios y catálogos (IA3-A)»; e
  [IA](../features/inteligencia-artificial.md), en la salida final `assistant_answer`.
- **Evidencia:** [configurar IA](../guides/configurar-ia.md#evidencia-para-habilitar-perfiles), en la corrida del 2026-11-08.
- **Estado:** el [plan de la etapa 1](2026-11-04-ia-arma-formularios-etapa-1.md), en sus entradas del 2026-11-07 y el
  2026-11-08.
- **Diseño del front:** `ArquitecturaBaseFront/docs/design/ia-arma-formularios.md`. IA3-A e IA4 ya decían que un pedido que no
  queda claro pregunta; el front no cambió.
- **Commits del backend:** «pregunta y espera», `87ed0482`, con sus casos del arnés en `c44f932e` y el plan en `d976c442`; los
  dos ajustes, `b09aed3c`; la evidencia, `74f30093`; el perfil del navegador declarado en la instalación local, `c9b5a299`. Los
  arreglos del arnés (`88b7abdc`, `7a2cb140`, `162db253` y `98b0d50c`) no aplican allá.
- **Código de referencia:** `BusinessFormAuthoringAssistantCapabilitySource` (`unclear` y su comprobación),
  `FormAuthoringAssistantCodec` (los motivos y la descripción del prefijo), `WhatsAppAssistantAuthoringText` (el texto fijo),
  `WhatsAppAssistantAutomaticPolicy` (la espera de la respuesta), `AiAssistantEvidenceVerifier` (las cifras) y los recursos
  `Assistant.resx` (`Prompt.TerminalAnswerV2Description`).
- **GitHub:** se subió el 2026-11-08 a `ezeellena2/ArquitecturaBase`, a pedido del usuario, con lo que faltaba desde
  `ae60b950`: los ajustes, la evidencia y el perfil del navegador. Desde otra PC se lee con un pull de `main`.

## 9. Prompt para el chat guía del multitenant

```text
Leé el traspaso 11 de ArquitecturaBase: docs/plans/2026-11-08-traspaso-multitenant-ia-pregunta-y-corrida.md. En esta PC
está en C:\Users\ezequ\source\repos\ArquitecturaBase; desde otra PC, en GitHub (ezeellena2/ArquitecturaBase, rama main:
hacé pull). Completa el traspaso 8, la etapa 1 de «la IA arma formularios y catálogos», con lo que pasó después: por
WhatsApp, un pedido ambiguo pregunta y espera (decidido el 7/11); la corrida con el proveedor real quedó en verde (8/11),
y esa corrida dejó dos ajustes: el prefijo pedido que no cumple el formato se normaliza sin preguntar y las aclaraciones
van sin cifras. Toca tu E25 («Armar desde el asistente de la web», decisiones 75 y 102), tu E23 y tu módulo assistant.

1. Si todavía no incorporaste el traspaso 8, leelo antes y seguí su prompt: este lo completa y no cambia sus tres
   decisiones.
2. Evaluá el §6 contra tu E25, tu E23 y tu ADR del asistente. No es código para copiar (tu decisión 18) ni hace falta
   para dibujar (tu decisión 108).
3. No hay decisiones nuevas para el usuario: si adopta armar por WhatsApp, «pregunta y espera» va con esa respuesta. Si
   alguna adaptación sí le toca a él, preguntásela de a una, con las opciones y la recomendada primero.
4. Sumá a tu E25 las dos reglas del §4 y la pregunta ante un pedido ambiguo, y a tu lista de «no repetir», los puntos 11
   a 17 del §5. Las medidas del §3 sirven para dimensionar tus topes, no para calificar tu modelo.
5. No programes nada. Commits chicos en español, en main, sin push.

Al terminar, contame qué cambió en tus specs y qué quedó pendiente.
```

Este traspaso se retira junto con el 8, cuando el multitenant los haya incorporado a sus etapas.
