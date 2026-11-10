# Pregunta de ubicación en los formularios

Fecha: **2026-11-10**. Estado: **propuesta. Las decisiones UB1 a UB4 esperan la elección del usuario (la recomendada va
primero en cada una) y no hay nada programado**.

El usuario preguntó si el producto puede armar un formulario que pida la ubicación. Hoy no: no existe esa pregunta, ni en
la app ni por WhatsApp. Este plan agrega una pregunta «Ubicación» a los formularios, primero en la app, y deja la parte de
WhatsApp para una etapa aparte, con su propio diseño.

## Qué se puede pedir y qué no

- **Una captura puntual:** un punto (latitud, longitud y la precisión que informa el dispositivo) de quien completa el
  formulario, en el momento de completarlo. Cada respuesta ya guarda quién, cuándo y por qué canal; con esto suma dónde.
  Sirve para visitas, entregas y check-ins: un formulario «Registro de visita» con Ubicación y Foto deja un rastro de
  puntos, que se filtra por persona y por fecha en la planilla.
- **No es seguimiento continuo.** Meta no entrega la posición en tiempo real por la API de WhatsApp (la referencia de
  mensajes `unsupported` lista `location` entre los tipos que no llegan), y un navegador corta el GPS cuando la pestaña pasa
  a segundo plano. Seguir un recorrido pide una app nativa o un servicio de rastreo: es otra decisión y no entra acá.
- **No prueba presencia.** La ubicación es lo que informa el dispositivo; quien llame a la API con una sesión válida puede
  mandar coordenadas inventadas. El producto no la audita ni la contrasta con nada.

## Antes de este cambio

- **Tipos de pregunta.** [`FormQuestionType`](../../src/ArquitecturaBase.Domain/Modules/BusinessForms/FormQuestionType.cs)
  tiene `ShortText`, `LongText`, `Number`, `Date`, `Boolean`, `Choice`, `MultiChoice`, `Group`, `Photo` y `Document`. En el
  esquema y en el contrato son los nombres `shortText`, `decimal`, `date`, `photo`… que
  [`FormDraftPolicy.TypeOf`](../../src/ArquitecturaBase.Application/Modules/BusinessForms/Services/FormDraftPolicy.cs) y
  [`SaveFormDraftRequestValidator`](../../src/ArquitecturaBase.Application/Modules/BusinessForms/Validation/SaveFormDraftRequestValidator.cs)
  conocen.
- **Cómo se guarda una respuesta.** Cada fila de `BusinessFormRecordValues` tiene un solo valor tipado (`CK_BusinessFormRecordValues_ExactlyOneValue`)
  y `Position` de 0 a 19 con índice único por respuesta, pregunta, ítem y posición. Una `multiChoice` ya guarda varias
  filas de una misma pregunta, una por opción, ordenadas por `Position`.
- **WhatsApp.** [`WhatsAppFlowCompiler`](../../src/ArquitecturaBase.Application/Modules/WhatsApp/Services/WhatsAppFlowCompiler.cs)
  traduce cada tipo a un componente de Flow y rechaza los que no conoce. Flows no tiene un componente de ubicación (no
  aparece en su [changelog](https://developers.facebook.com/documentation/business-messaging/whatsapp/flows/changelogs)).
  El webhook ya clasifica un mensaje `location` como `Other`
  ([`WhatsAppWebhookReader`](../../src/ArquitecturaBase.Infrastructure/Modules/WhatsApp/WhatsAppWebhookReader.cs)): no se
  lee ni se guarda.
- **Lo que Meta sí ofrece.** El mensaje interactivo
  [`location_request_message`](https://developers.facebook.com/documentation/business-messaging/whatsapp/messages/location-request-messages)
  muestra un botón «Enviar ubicación», y al tocarlo llega un mensaje `location` con `latitude` y `longitude` (y `name` y
  `address` sólo si la persona los comparte).
- **La app web** no usa la geolocalización del navegador.
- **Lo más cercano.** [Documentos por WhatsApp](2026-10-22-documentos-por-whatsapp.md) prevé que la ubicación que alguien
  manda al asistente complete una pregunta de dirección elegida en el editor (sin empezar). Es lo inverso: la persona
  manda y el sistema usa. Con este plan esa pregunta podría ser la de Ubicación.

## Decisiones propuestas

| Decisión | A · recomendada | Otras | Por qué A |
| --- | --- | --- | --- |
| **UB1 · Qué se guarda** | Latitud, longitud y precisión en metros (opcional). | B: sólo latitud y longitud. C: además nombre y dirección. | La precisión dice cuánto creerle a un punto: en una computadora sin GPS el navegador puede errar por kilómetros. El nombre y la dirección sólo existen en un pin de WhatsApp, no en la app. |
| **UB2 · Dónde se completa** | Sólo en la app (Etapa 1); por WhatsApp, en la Etapa 2. | B: esperar a tener también WhatsApp. | Un Flow no puede pedir la ubicación, y lo de WhatsApp necesita una sesión de varios mensajes. La app ya tiene «Sólo en la app» ([SA1-A](2026-11-09-formularios-solo-en-la-app.md)). |
| **UB3 · Cómo se pide en la app** | Botón «Usar mi ubicación»: muestra el punto con su precisión («± 15 m»), «Ver en el mapa» y «Volver a pedirla». Si el navegador no la da, dice por qué; obligatoria, no deja enviar. | B: además escribir las coordenadas a mano. C: elegir en un mapa embebido. | Sin mapas ni scripts de terceros ni costo. Escribirlas a mano invita a copiar cualquier cosa, y el mapa trae una dependencia y su cuota. |
| **UB4 · Cómo se lee** | Un punto es un solo dato: planilla, detalle, PDF y mensajes lo muestran como «-34.603722, -58.381592» con «Ver en el mapa»; el Excel lo reparte en tres columnas numéricas («· Latitud», «· Longitud», «· Precisión (m)»). | B: una sola columna de texto en el Excel. | Excel necesita números para filtrar, ordenar y armar un mapa. El enlace tiene destino fijo (Google Maps con `q=<lat>,<lng>`), sin pedir nada a un servicio. |

UB3 y UB4 son de pantalla: cada una necesita su tablero antes de programarse (ver la Etapa 1, tarea 8).

## Reglas por defecto (no se preguntan)

- **Sin migración.** Una ubicación guarda tres filas de `DecimalValue`, como una `multiChoice` guarda una por opción:
  `Position` 0 = latitud, 1 = longitud, 2 = precisión (sólo si vino). El CHECK y el índice único no cambian. La regla de
  «los escalares conservan Position 0» pasa a decir que la ubicación es la excepción.
- **El pedido.** La respuesta llega en el mismo mapa de respuestas, como el arreglo de una `multiChoice`: un JSON
  `{"latitude":…,"longitude":…,"accuracyMeters":…}` dentro de un texto. El servidor valida: números finitos, latitud de
  -90 a 90, longitud de -180 a 180, ambas redondeadas a seis decimales (como las coordenadas del negocio, en
  [inteligencia artificial](../features/inteligencia-artificial.md)), precisión de 0 a 100.000 m en metros enteros.
  Obligatoria significa latitud y longitud presentes.
- **Fuera del bloque de renglones.** Como `photo`, `document` y `multiChoice`, no se repite por ítem.
- **Sin filtros propios.** En la planilla sólo vale «con respuesta», «sin respuesta» y «no se preguntó»; no hay rango de
  coordenadas ni «cerca de».
- **Sin condiciones ni cálculos.** Ninguna regla ni condición (`ShowWhen`) se apoya en una ubicación.
- **Las máquinas no la tocan.** El asistente de IA no la propone al armar un formulario, ni la prellena, ni la mapea en
  una sugerencia, como hoy con `multiChoice`.
- **Datos personales.** Un modelo que lleve coordenadas redacta su `ToString()`, como `FormRecordAnswer`; los logs no
  llevan valores de respuestas (ya es la regla del módulo). Sumá la ubicación a la decisión pendiente de
  [retención de respuestas](README.md).
- **Sin permisos nuevos.** Se ve con `formrecords.read` y se carga con los permisos de carga de hoy.
- **Las versiones publicadas no cambian.** Un formulario existente no tiene la pregunta; cambiar el tipo de una pregunta
  a Ubicación crea otra identidad, como todo cambio de tipo.

## Plan · Etapa 1: la pregunta en la app

Cada tarea arranca con su test en rojo y cierra con el build y los tests unitarios y de arquitectura del área. Mismo orden
que los planes de ARCA y de «Sólo en la app»: el backend ahora, el front después de la prueba integral.

1. **El tipo y el borrador.** `FormQuestionType.Location`, `FormDraftPolicy.TypeOf` y `SaveFormDraftRequestValidator`
   (`"location"`, sin `repeated`, sin opciones, archivos, cálculo ni condición propios). Tests de Domain y de Application:
   guardar y publicar un borrador con la pregunta, cambio de tipo con identidad nueva, y que dentro de un bloque falla.
2. **La publicación.** `FormPublicationPolicy`: un formulario con una pregunta de ubicación que no es «Sólo en la app» da
   un problema bloqueante del chequeo, con su código estable (declarado en `FormPublicationErrors`, con su texto en
   `Errors.resx` y `Errors.en.resx`). Test de que el compilador de Flows nunca la acepta, aunque algo la dejara pasar.
3. **La captura.** `FormRecordAnswerPolicy` valida y normaliza la ubicación (la tabla de reglas de arriba);
   `FormRecordAnswer` la lleva y `Value` la pasa a `ValuesJson`; `BusinessFormRecordRecorder` y `FormEntryRecorder` escriben
   las tres filas. Tests: rango, redondeo, obligatoria vacía, precisión opcional, huella idempotente de un reintento y
   que un valor mal formado guarda el recibo rechazado sin respuesta parcial.
4. **Las lecturas.** Los lectores que hoy arman una `multiChoice` por `Position` arman también la ubicación
   (`FormResponseReader`, `FormRecordReader`); `FormRecordPresentationPolicy` la presenta como un dato. Los filtros
   «con/sin respuesta» (`FormRecordFilterPolicy`). Tests de planilla y detalle con respuestas viejas (la pregunta
   «no se preguntó» muestra «—», como cualquier pregunta posterior).
5. **Excel, PDF y mensajes.** `FormExportPolicy` y `ClosedXmlFormWorkbookWriter` (tres columnas numéricas),
   `SkiaFormPdfRenderer` y `FormPdfPreviewIssuer` (el texto del punto, con un ejemplo para la vista previa),
   `FormActionMessagePolicy` y `FormIssueDataPolicy` (el dato de un mensaje). Tests del escritor sobre el archivo
   generado y del texto del mensaje en español y en inglés.
6. **El asistente.** `FormAssistantCandidatePolicy`, `FormAssistantCapabilityCodec` y `FormAuthoringProposalPolicy` ignoran
   la pregunta (no la proponen ni la prellenan). Test de cada uno.
7. **Integración y documentos.** Pruebas con PostgreSQL por HTTP: cargar desde la app, planilla, detalle, Excel y PDF; el
   OpenAPI y los ejemplos exportados suman el tipo `location` (el contrato con el front, revisado con él porque todavía
   no está publicado). Se actualizan [formularios](../features/formularios.md) y
   [formularios-servicios](../features/formularios-servicios.md) con lo que hace el código, el `AGENTS.md` de
   `Domain/Modules/BusinessForms` (la identidad nueva) y el de `Persistence/Configurations` (la excepción de `Position`),
   y la línea de la ubicación de [Documentos por WhatsApp](2026-10-22-documentos-por-whatsapp.md).
8. **El front** (después de la prueba integral, con el front quieto). Primero el tablero: UB3 (cómo se pide, con permiso
   denegado, sin señal, precisión baja y celular) y UB4 (cómo se lee), pasados por la
   [lista de chequeo de diseño](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md#lista-de-chequeo-de-diseño),
   con el diseño registrado en `docs/design/ubicacion-en-formularios.md` del front. Después: «Ubicación» en el editor
   (sólo obligatoria u opcional), el control de la carga con `navigator.geolocation.getCurrentPosition`, la celda de la
   planilla y del detalle, los textos es/en y sus pruebas (con la geolocalización simulada).

**Cierre:** una revisión y, al terminar, una vez: la integración con PostgreSQL y Redis, `dotnet test` completo, el build
sin advertencias y `aspire stop` si levantaste la app. **A mano**, con el front: completar «Registro de visita» en un
celular (permitir y denegar el permiso), en una computadora (precisión baja), ver el punto en la planilla y en el detalle,
abrir el Excel y el PDF.

## Etapa 2: WhatsApp (sin diseño; se decide después de la Etapa 1)

Un Flow no puede pedir la ubicación, así que la pregunta saldría del Flow. Las formas posibles, todavía sin elegir:

| Forma | Cómo andaría | A favor | En contra |
| --- | --- | --- | --- |
| **W1 · Pedirla antes del Flow** | Antes de abrir el formulario se manda un `location_request_message`. Con la ubicación recibida se manda el Flow y la respuesta la lleva. | Una sola respuesta, completa. | Hay que comprobar con Meta que un Flow puede recibir un dato inicial y devolverlo en su respuesta (no lo verifiqué). Una sesión de dos mensajes que puede quedar a medias. |
| **W2 · Pedirla después del Flow** | El Flow se guarda sin la ubicación y se pide a continuación; al llegar se completa. | Reutiliza el Flow como está. | La respuesta existe un rato sin su dato, o se corrige después (otra versión de la respuesta). |
| **W3 · Sólo por el asistente** | La persona manda la ubicación al asistente y completa la pregunta del formulario que tiene abierto ([Documentos por WhatsApp](2026-10-22-documentos-por-whatsapp.md)). | Casi no agrega código de captura. | Depende de ese plan, que no empezó. |

Cualquiera es un efecto fuera del sistema (mandar y esperar un mensaje de Meta), así que le aplica el
[ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md): hay que decidir qué es una sesión a medias, cuánto dura
(la ventana de 24 horas de WhatsApp) y qué se hace al vencer. Por eso se diseña con tableros y se prueba con Meta real
antes de programarse. Entra también el parser de `location` del webhook, que hoy cae en `Other`.

## Fuera de alcance

- Seguimiento continuo, rutas y geocercas.
- Mapas embebidos, búsqueda de direcciones y autocompletar (hay una pieza parecida para el negocio, en
  [ubicación por lugar](../../../ArquitecturaBaseFront/docs/design/ubicacion-por-lugar.md)).
- Verificar que el punto es real (anti-falsificación) o que está dentro de una zona.
- La ubicación del dispositivo en pantallas que no son un formulario.

## Lo que falta

- La elección del usuario en UB1 a UB4.
- Todo lo programable de la Etapa 1; el front, después de la prueba integral.
- La decisión sobre la Etapa 2, con su diseño y su prueba con Meta.
