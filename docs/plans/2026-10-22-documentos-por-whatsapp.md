# Documentos por WhatsApp: biblioteca de formularios estándar, lectura con IA y tableros

**Estado:** decidido el 2026-10-22, con el [ADR 0022](../decisions/0022-lectura-de-documentos-con-ia.md) aceptado y los tableros de diseño elegidos (DOC5-A y DOC7-A). Sin empezar. Es la etapa «fotos y documentos» que el usuario marcó como prioridad ([casos del asistente](2026-10-21-casos-del-asistente.md#etapa-siguiente-fotos-y-documentos)). Va después de [Catálogos E2](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23), porque el cruce con catálogos usa sus campos extra. Su orden frente a «Casos del asistente» lo define el usuario. El 2026-11-10 se le anexó la [pregunta «Ubicación»](#la-pregunta-ubicación), una propuesta con decisiones sin elegir que no depende del resto.

## Por qué

Hoy, cuando alguien manda por WhatsApp una foto o un PDF, se guarda como «archivo» sin contenido y la IA no lo ve. El producto tiene que poder recibir **cualquier tipo de documento**:
- comprobantes de transferencia, facturas, recibos y remitos;
- liquidaciones, DTE y constancias.

Con ese documento tiene que reconocer qué es, sacar sus datos y proponer el formulario que corresponde, ya completo, para que la persona lo valide. **Tiene que ser genérico:** ningún tipo de documento queda escrito en el código.

## Conceptos

### Biblioteca de formularios estándar

- **El producto trae formularios estándar** para los documentos comunes. Vienen como **datos de inicio** de cada versión, no como código. En una instalación de una empresa aparecen en la pantalla **«Biblioteca»**; en el multitenant, la misma biblioteca la comparten todas las organizaciones.
- **Cada estándar trae:**
  - sus preguntas, con sus tipos;
  - **«Cómo reconocerlo»**: la descripción para la IA;
  - sus **validaciones fijas**, que no dependen de la IA;
  - una pregunta de archivo para el documento original.
- **«Agregar desde la biblioteca» crea un formulario propio** de la empresa, que se puede editar como cualquier otro: nombre, preguntas, reglas y aprobaciones. Recuerda de qué estándar viene, con qué versión, y qué pregunta corresponde a cada dato del estándar.
- **Una empresa también puede activar la lectura de documentos en un formulario propio,** escribiendo su «Cómo reconocerlo». Se usa el mismo mecanismo.
- **Primer estándar: Comprobante de transferencia.** Sus preguntas:
  - banco o billetera de origen y de destino;
  - CBU, CVU o alias de origen y de destino;
  - titulares y CUIT/CUIL;
  - monto y moneda;
  - fecha y hora;
  - número de operación;
  - concepto;
  - el comprobante, como archivo.

  Sus validaciones fijas:
  - **el CBU y el CVU tienen dígitos verificadores, y sus primeros dígitos dicen el banco o la billetera,** según la tabla de entidades del BCRA, que se carga como datos;
  - el CUIT y el CUIL tienen dígito verificador;
  - montos y fechas en formato argentino.
- **Los siguientes estándares,** en etapas posteriores: factura (con el QR de AFIP), nota de crédito o débito, recibo, remito, ticket de gasto, orden de compra, presupuesto, cheque o eCheq, constancias, liquidaciones, carta de porte y DTE.

### Qué puede llegar

Se leen **solo los mensajes de personas con cuenta vinculada y activa,** como hoy en el asistente. Lo que manda un número desconocido queda en el chat para un operador, sin leerse.

| Lo que llega | Cómo se lee | Qué completa |
| --- | --- | --- |
| Texto | Como hoy: el asistente lo interpreta | Propone el formulario prellenado |
| Audio | Como hoy: se transcribe y se trata como texto | Igual |
| Foto o captura (JPEG, PNG) | La IA la lee | Tipo de documento y datos; propone el formulario con la foto adjunta |
| PDF | La IA lee todas sus páginas, con un tope | Igual que la foto |
| Word (`.docx`) | El servidor extrae el texto y las tablas, y la IA los lee | Igual, con el archivo adjunto |
| Excel (`.xlsx`) | El servidor lee las hojas, con los topes de la importación de catálogos, y la IA las lee | Igual. Una planilla con varias filas del mismo tipo puede proponer varias cargas, hasta el tope de acciones; las filas que pasan el tope quedan propuestas en la bandeja |
| Ubicación | Sin IA: llega con latitud, longitud y, a veces, nombre y dirección | La pregunta de ubicación o dirección del formulario que se esté completando; cuál es se elige en el editor. Para la ubicación hace falta la pregunta [«Ubicación»](#la-pregunta-ubicación), que todavía no existe |
| Contacto (tarjeta) | Sin IA: llega con nombre y teléfonos | Las preguntas de nombre y teléfono, elegidas en el editor |
| Video o sticker | No se lee | — |

- **Los formatos viejos** (`.doc`, `.xls`), los archivos con macros y los protegidos con contraseña no se leen: se pide un PDF o una versión sin contraseña.
- **El tipo se verifica por el contenido,** nunca por la extensión, como ya hacen los archivos de la [etapa 2B](../features/formularios.md#fotos-y-documentos--implementación-local-2b-2026-10-15).
- **Se aplican topes de tamaño, páginas, hojas y celdas,** incluidas las celdas vacías con formato que encontró el [review de catálogos E1](../reviews/2026-10-22-code-review-catalogos-e1.md).
- **Una foto con un audio que la explica** («es el pago de Pérez») se lee junto solo con la espera de la ráfaga de [casos del asistente](2026-10-21-casos-del-asistente.md). Sin eso, cada mensaje se trata por separado.

### El circuito

1. **Recibir:**
   - la imagen o el PDF se descarga y se guarda como **copia privada**, como los audios;
   - el texto que venga junto al archivo también se usa;
   - un PDF de varias páginas se lee entero.
2. **Clasificar,** entre los formularios con lectura de documentos que **esa persona puede completar.** No hay permisos por formulario: son todos los publicados, si su cuenta tiene el permiso de cargar respuestas, como en el envío desde el chat.
   - **seguro:** propone ese formulario;
   - **duda:** pregunta «¿Qué es este documento?», con botones para 2 o 3 formularios posibles. Un botón de WhatsApp admite hasta 20 caracteres: la spec fija cómo se muestra un nombre más largo;
   - **no lo reconoce o no hay formulario:** ofrece la lista que esa persona puede completar, más «Otro». «Otro» lo deja en la bandeja y lo anota en «Formularios que faltan».
3. **Extraer** los datos según las **preguntas del formulario elegido**, con su nivel de seguridad por dato.
4. **Validar sin IA:**
   - las validaciones fijas del estándar;
   - el cruce con catálogos: el CUIT en Proveedores o Clientes, como [campo extra de Catálogos E2](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23);
   - **los duplicados:** el mismo archivo, o la misma operación, monto y fecha.
5. **Proponer:** el formulario prellenado por WhatsApp, **con el documento adjunto** en su pregunta de archivo. Un dato que no se pudo leer queda vacío, y uno dudoso, marcado.
6. **Confirmar o corregir:** lo hace la persona. **En esta etapa, la IA nunca carga sola.**
7. **Organizar:** queda como una respuesta más, con sus reglas, aprobación y PDF.

### Casos a contemplar

| Situación | Qué hace |
| --- | --- |
| Foto movida, oscura o cortada | Pide reenviarla, diciendo qué falta |
| PDF con contraseña | Pide uno sin contraseña |
| Documento repetido | Avisa que ya se cargó, con cuándo y en qué respuesta |
| Datos que no están | Quedan vacíos y los completa la persona |
| Escrito a mano | Lo intenta y marca los datos dudosos |
| Otra moneda u otro idioma | Lo extrae igual |
| Documento sin texto | Funciona igual: el documento es el mensaje |
| Un contacto sin cuenta vinculada | No se lee ([ADR 0022](../decisions/0022-lectura-de-documentos-con-ia.md)): queda en el chat para un operador, como hoy |
| Una cuenta sin permiso para cargar respuestas | No se lee, porque no tiene formularios posibles: lo dice y deriva |
| El formulario reconocido no se le puede mandar, por ejemplo porque le falta el dato con el que se filtra un catálogo ([E2](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23)) | Lo dice, deriva y queda propuesto en la bandeja; nunca va la lista entera |
| Varias fotos de un mismo documento, o varios documentos en un mensaje | Después de esta etapa: necesita la ráfaga y las varias acciones de [casos del asistente](2026-10-21-casos-del-asistente.md) |

### La bandeja «Documentos recibidos»

Forma elegida, **DOC5-A:** una planilla como la de respuestas, con sus filtros. El documento se abre en un panel al costado, junto a sus datos, como la vista rápida de una respuesta.

- **Filtros:** tipo, estado (propuesto, confirmado, sin clasificar, descartado), persona y fecha.
- **El documento al lado de los datos extraídos,** para revisar, corregir y pasar al formulario.
- **Propuestos para revisar acá:** los que la persona no confirmó antes de que venciera el envío, los que no se le pudieron mandar y las filas de una planilla que pasaron el tope. Alguien con permiso los pasa al formulario, y la respuesta dice de qué documento salió y quién la revisó.
- **Cuándo se borra:** cada descartado o sin clasificar muestra la fecha, a los 90 días.
- **Búsqueda por los datos:** monto, CUIT o número de operación.
- **Permisos propios** para ver y revisar documentos.

### Cómo mejora con el uso

- **Cada corrección queda registrada:** el dato propuesto, el confirmado y quién lo corrigió.
- **Las correcciones se suman como ejemplos** a «Cómo reconocerlo». No se entrena ningún modelo.
- **Se mide la precisión por formulario y por campo.** Es la base para la carga automática.

### Formulario nuevo armado por la IA

Para un documento que no encaja en ningún formulario:
1. **Queda en la bandeja como «Sin clasificar».**
2. **Alguien con permiso toca «Armar formulario con la IA».** La IA propone:
   - el nombre;
   - «Cómo reconocerlo»;
   - las preguntas con sus tipos, a partir de los datos del documento;
   - y, si alguno se parece, sugiere un estándar de la biblioteca.
3. **La persona revisa, ajusta y publica** como cualquier formulario. La IA propone la configuración y nunca la publica ni la aprueba, como ya dice el [ADR 0019](../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md).
4. **El documento que lo originó se carga en el formulario nuevo,** y desde ahí los documentos parecidos se reconocen solos.

La bandeja **agrupa los documentos sin clasificar que se parecen** y avisa: «Llegaron 5 documentos parecidos. ¿Armamos un formulario?».

Retoma el pendiente «borrador de formulario armado por la IA a partir de los formularios que faltan» ([formularios](formularios/README.md#después-de-la-etapa-5)).

El chat de Inicio y el asistente de WhatsApp usan la misma propuesta y el mismo «Crear borrador», también con un pedido escrito
o un audio ([la IA arma formularios y catálogos](2026-11-04-ia-arma-formularios-y-catalogos.md), decidido el 2026-11-04).

### Tableros por formulario

Cada formulario tiene una pestaña **«Tablero»**, armada sola a partir de sus preguntas y ajustable:

| Si el formulario tiene… | El tablero muestra… |
| --- | --- |
| Cualquier pregunta | Cuántas respuestas llegaron por día, semana o mes |
| Un número o un monto | Total, promedio, mínimo y máximo por período |
| Elegir una opción o un catálogo | El reparto por opción, por ejemplo transferencias por banco |
| Persona y estado | Quién cargó cuánto; aprobadas, rechazadas y pendientes |
| Fecha | La evolución en el tiempo |

Forma elegida, **DOC7-A:** arriba, los números contra el período anterior; abajo, una tarjeta por gráfico.

- **Se puede ajustar:** sumar o quitar gráficos, y filtrar con la misma gramática de la planilla, por período, persona o estado.
- **No hay tableros en el código:** salen de los tipos de las preguntas, así que un formulario nuevo, también el armado por la IA, ya tiene el suyo.
- **Los permisos de la planilla:** quien no puede leer respuestas no ve el tablero.
- **Usa los valores con tipo** que ya guardan las respuestas (número, fecha, opción), para sumar y agrupar bien.
- **Más adelante:** tableros que cruzan formularios y que suman datos de sistemas externos, cuando exista la conexión con bases de clientes.

### La carga automática (etapa posterior)

Un modo por formulario, **«Cargar solo si es seguro»**, que carga sin preguntar cuando se cumplen las tres señales:
1. el tipo es claro;
2. las validaciones fijas de los campos elegidos dan bien y no es un duplicado;
3. el formulario tiene un historial suficiente de cargas sin correcciones en esos campos.

La persona recibe «Cargué tu transferencia de $150.000 (REC-000123). Si algo está mal, avisá», y en la bandeja queda marcada «Cargada automáticamente». **No se usa la seguridad que declara el modelo:** no es confiable por sí sola. Se usan las validaciones fijas y el historial medido.

### La pregunta «Ubicación»

Anexada el 2026-11-10. El usuario preguntó si un formulario puede pedir la ubicación. Hoy no: no existe esa pregunta, ni en la app ni por WhatsApp, y la fila «Ubicación» de [Qué puede llegar](#qué-puede-llegar) supone una pregunta que la reciba. Esta sección agrega esa pregunta a los formularios, primero en la app, y deja la parte de WhatsApp para una etapa aparte, con su propio diseño. **Estado: propuesta. Las decisiones UB1 a UB4 esperan la elección del usuario (la recomendada va primero en cada una) y no hay nada programado.** La Etapa 1 no depende del resto de este plan: puede programarse antes.

#### Qué se puede pedir y qué no

- **Una captura puntual:** un punto (latitud, longitud y la precisión que informa el dispositivo) de quien completa el formulario, en el momento de completarlo. Cada respuesta ya guarda quién, cuándo y por qué canal; con esto suma dónde. Sirve para visitas, entregas y check-ins: un formulario «Registro de visita» con Ubicación y Foto deja un rastro de puntos, que se filtra por persona y por fecha en la planilla.
- **No es seguimiento continuo.** Meta no entrega la posición en tiempo real por la API de WhatsApp (la referencia de mensajes `unsupported` lista `location` entre los tipos que no llegan), y un navegador corta el GPS cuando la pestaña pasa a segundo plano. Seguir un recorrido pide una app nativa o un servicio de rastreo: es otra decisión y no entra acá.
- **No prueba presencia.** La ubicación es lo que informa el dispositivo; quien llame a la API con una sesión válida puede mandar coordenadas inventadas. El producto no la audita ni la contrasta con nada.

#### Antes de este cambio

- **Tipos de pregunta.** [`FormQuestionType`](../../src/ArquitecturaBase.Domain/Modules/BusinessForms/FormQuestionType.cs) tiene `ShortText`, `LongText`, `Number`, `Date`, `Boolean`, `Choice`, `MultiChoice`, `Group`, `Photo` y `Document`. En el esquema y en el contrato son los nombres `shortText`, `decimal`, `date`, `photo`… que [`FormDraftPolicy.TypeOf`](../../src/ArquitecturaBase.Application/Modules/BusinessForms/Services/FormDraftPolicy.cs) y [`SaveFormDraftRequestValidator`](../../src/ArquitecturaBase.Application/Modules/BusinessForms/Validation/SaveFormDraftRequestValidator.cs) conocen.
- **Cómo se guarda una respuesta.** Cada fila de `BusinessFormRecordValues` tiene un solo valor tipado (`CK_BusinessFormRecordValues_ExactlyOneValue`) y `Position` de 0 a 19 con índice único por respuesta, pregunta, ítem y posición. Una `multiChoice` ya guarda varias filas de una misma pregunta, una por opción, ordenadas por `Position`.
- **WhatsApp.** [`WhatsAppFlowCompiler`](../../src/ArquitecturaBase.Application/Modules/WhatsApp/Services/WhatsAppFlowCompiler.cs) traduce cada tipo a un componente de Flow y rechaza los que no conoce. Flows no tiene un componente de ubicación (no aparece en su [changelog](https://developers.facebook.com/documentation/business-messaging/whatsapp/flows/changelogs)). El webhook ya clasifica un mensaje `location` como `Other` ([`WhatsAppWebhookReader`](../../src/ArquitecturaBase.Infrastructure/Modules/WhatsApp/WhatsAppWebhookReader.cs)): no se lee ni se guarda.
- **Lo que Meta sí ofrece.** El mensaje interactivo [`location_request_message`](https://developers.facebook.com/documentation/business-messaging/whatsapp/messages/location-request-messages) muestra un botón «Enviar ubicación», y al tocarlo llega un mensaje `location` con `latitude` y `longitude` (y `name` y `address` sólo si la persona los comparte).
- **La app web** no usa la geolocalización del navegador.
- **Lo más cercano** es la fila «Ubicación» de [Qué puede llegar](#qué-puede-llegar): la ubicación que alguien manda al asistente completa una pregunta de dirección elegida en el editor. Es lo inverso: la persona manda y el sistema usa. Con esta pregunta, ese destino pasa a ser la de Ubicación.

#### Decisiones propuestas

| Decisión | A · recomendada | Otras | Por qué A |
| --- | --- | --- | --- |
| **UB1 · Qué se guarda** | Latitud, longitud y precisión en metros (opcional). | B: sólo latitud y longitud. C: además nombre y dirección. | La precisión dice cuánto creerle a un punto: en una computadora sin GPS el navegador puede errar por kilómetros. El nombre y la dirección sólo existen en un pin de WhatsApp, no en la app. |
| **UB2 · Dónde se completa** | Sólo en la app (Etapa 1); por WhatsApp, en la Etapa 2. | B: esperar a tener también WhatsApp. | Un Flow no puede pedir la ubicación, y lo de WhatsApp necesita una sesión de varios mensajes. La app ya tiene «Sólo en la app» ([SA1-A](2026-11-09-formularios-solo-en-la-app.md)). |
| **UB3 · Cómo se pide en la app** | Botón «Usar mi ubicación»: muestra el punto con su precisión («± 15 m»), «Ver en el mapa» y «Volver a pedirla». Si el navegador no la da, dice por qué; obligatoria, no deja enviar. | B: además escribir las coordenadas a mano. C: elegir en un mapa embebido. | Sin mapas ni scripts de terceros ni costo. Escribirlas a mano invita a copiar cualquier cosa, y el mapa trae una dependencia y su cuota. |
| **UB4 · Cómo se lee** | Un punto es un solo dato: planilla, detalle, PDF y mensajes lo muestran como «-34.603722, -58.381592» con «Ver en el mapa»; el Excel lo reparte en tres columnas numéricas («· Latitud», «· Longitud», «· Precisión (m)»). | B: una sola columna de texto en el Excel. | Excel necesita números para filtrar, ordenar y armar un mapa. El enlace tiene destino fijo (Google Maps con `q=<lat>,<lng>`), sin pedir nada a un servicio. |

UB3 y UB4 son de pantalla: cada una necesita su tablero antes de programarse (ver la tarea 8 de la Etapa 1).

#### Reglas por defecto (no se preguntan)

- **Sin migración.** Una ubicación guarda tres filas de `DecimalValue`, como una `multiChoice` guarda una por opción: `Position` 0 = latitud, 1 = longitud, 2 = precisión (sólo si vino). El CHECK y el índice único no cambian. La regla de «los escalares conservan Position 0» pasa a decir que la ubicación es la excepción.
- **El pedido.** La respuesta llega en el mismo mapa de respuestas, como el arreglo de una `multiChoice`: un JSON `{"latitude":…,"longitude":…,"accuracyMeters":…}` dentro de un texto. El servidor valida: números finitos, latitud de -90 a 90, longitud de -180 a 180, ambas redondeadas a seis decimales (como las coordenadas del negocio, en [inteligencia artificial](../features/inteligencia-artificial.md)), precisión de 0 a 100.000 m en metros enteros. Obligatoria significa latitud y longitud presentes.
- **Fuera del bloque de renglones.** Como `photo`, `document` y `multiChoice`, no se repite por ítem.
- **Sin filtros propios.** En la planilla sólo vale «con respuesta», «sin respuesta» y «no se preguntó»; no hay rango de coordenadas ni «cerca de».
- **Sin condiciones ni cálculos.** Ninguna regla ni condición (`ShowWhen`) se apoya en una ubicación.
- **Las máquinas no la tocan al armar.** El asistente de IA no la propone al armar un formulario, ni la mapea en una sugerencia, como hoy con `multiChoice`. Lo que sí hace, en la Etapa 2, es completarla con la ubicación que manda la persona (W3).
- **Datos personales.** Un modelo que lleve coordenadas redacta su `ToString()`, como `FormRecordAnswer`; los logs no llevan valores de respuestas (ya es la regla del módulo). Sumá la ubicación a la decisión pendiente de [retención de respuestas](README.md).
- **Sin permisos nuevos.** Se ve con `formrecords.read` y se carga con los permisos de carga de hoy.
- **Las versiones publicadas no cambian.** Un formulario existente no tiene la pregunta; cambiar el tipo de una pregunta a Ubicación crea otra identidad, como todo cambio de tipo.

#### Etapa 1: la pregunta en la app

Cada tarea arranca con su test en rojo y cierra con el build y los tests unitarios y de arquitectura del área. Mismo orden que los planes de ARCA y de «Sólo en la app»: el backend ahora, el front después de la prueba integral.

1. **El tipo y el borrador.** `FormQuestionType.Location`, `FormDraftPolicy.TypeOf` y `SaveFormDraftRequestValidator` (`"location"`, sin `repeated`, sin opciones, archivos, cálculo ni condición propios). Tests de Domain y de Application: guardar y publicar un borrador con la pregunta, cambio de tipo con identidad nueva, y que dentro de un bloque falla.
2. **La publicación.** `FormPublicationPolicy`: un formulario con una pregunta de ubicación que no es «Sólo en la app» da un problema bloqueante del chequeo, con su código estable (declarado en `FormPublicationErrors`, con su texto en `Errors.resx` y `Errors.en.resx`). Test de que el compilador de Flows nunca la acepta, aunque algo la dejara pasar.
3. **La captura.** `FormRecordAnswerPolicy` valida y normaliza la ubicación (las reglas de arriba); `FormRecordAnswer` la lleva y `Value` la pasa a `ValuesJson`; `BusinessFormRecordRecorder` y `FormEntryRecorder` escriben las tres filas. Tests: rango, redondeo, obligatoria vacía, precisión opcional, huella idempotente de un reintento y que un valor mal formado guarda el recibo rechazado sin respuesta parcial.
4. **Las lecturas.** Los lectores que hoy arman una `multiChoice` por `Position` arman también la ubicación (`FormResponseReader`, `FormRecordReader`); `FormRecordPresentationPolicy` la presenta como un dato. Los filtros «con/sin respuesta» (`FormRecordFilterPolicy`). Tests de planilla y detalle con respuestas viejas (la pregunta «no se preguntó» muestra «—», como cualquier pregunta posterior).
5. **Excel, PDF y mensajes.** `FormExportPolicy` y `ClosedXmlFormWorkbookWriter` (tres columnas numéricas), `SkiaFormPdfRenderer` y `FormPdfPreviewIssuer` (el texto del punto, con un ejemplo para la vista previa), `FormActionMessagePolicy` y `FormIssueDataPolicy` (el dato de un mensaje). Tests del escritor sobre el archivo generado y del texto del mensaje en español y en inglés.
6. **El asistente.** `FormAssistantCandidatePolicy`, `FormAssistantCapabilityCodec` y `FormAuthoringProposalPolicy` ignoran la pregunta (no la proponen ni la mapean). Test de cada uno.
7. **Integración y documentos.** Pruebas con PostgreSQL por HTTP: cargar desde la app, planilla, detalle, Excel y PDF; el OpenAPI y los ejemplos exportados suman el tipo `location` (el contrato con el front, revisado con él porque todavía no está publicado). Se actualizan [formularios](../features/formularios.md) y [formularios-servicios](../features/formularios-servicios.md) con lo que hace el código, el `AGENTS.md` de `Domain/Modules/BusinessForms` (la identidad nueva) y el de `Persistence/Configurations` (la excepción de `Position`), y la fila «Ubicación» de [Qué puede llegar](#qué-puede-llegar).
8. **El front** (después de la prueba integral, con el front quieto). Primero el tablero: UB3 (cómo se pide, con permiso denegado, sin señal, precisión baja y celular) y UB4 (cómo se lee), pasados por la [lista de chequeo de diseño](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md#lista-de-chequeo-de-diseño), con el diseño registrado en `docs/design/ubicacion-en-formularios.md` del front. Después: «Ubicación» en el editor (sólo obligatoria u opcional), el control de la carga con `navigator.geolocation.getCurrentPosition`, la celda de la planilla y del detalle, los textos es/en y sus pruebas (con la geolocalización simulada).

**Cierre:** una revisión y, al terminar, una vez: la integración con PostgreSQL y Redis, `dotnet test` completo, el build sin advertencias y `aspire stop` si levantaste la app. **A mano**, con el front: completar «Registro de visita» en un celular (permitir y denegar el permiso), en una computadora (precisión baja), ver el punto en la planilla y en el detalle, abrir el Excel y el PDF.

#### Etapa 2: la ubicación por WhatsApp (sin diseño; se decide después de la Etapa 1)

Un Flow no puede pedir la ubicación, así que la pregunta saldría del Flow. Las formas posibles, todavía sin elegir:

| Forma | Cómo andaría | A favor | En contra |
| --- | --- | --- | --- |
| **W1 · Pedirla antes del Flow** | Antes de abrir el formulario se manda un `location_request_message`. Con la ubicación recibida se manda el Flow y la respuesta la lleva. | Una sola respuesta, completa. | Hay que comprobar con Meta que un Flow puede recibir un dato inicial y devolverlo en su respuesta (no lo verifiqué). Una sesión de dos mensajes que puede quedar a medias. |
| **W2 · Pedirla después del Flow** | El Flow se guarda sin la ubicación y se pide a continuación; al llegar se completa. | Reutiliza el Flow como está. | La respuesta existe un rato sin su dato, o se corrige después (otra versión de la respuesta). |
| **W3 · Sólo por el asistente** | La persona manda la ubicación al asistente y completa la pregunta de Ubicación del formulario que tiene abierto: la fila «Ubicación» de [Qué puede llegar](#qué-puede-llegar). | Casi no agrega código de captura: el circuito de arriba ya recibe, propone y pide confirmación. | Depende de que este plan empiece. |

Cualquiera es un efecto fuera del sistema (mandar y esperar un mensaje de Meta), así que le aplica el [ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md): hay que decidir qué es una sesión a medias, cuánto dura (la ventana de 24 horas de WhatsApp) y qué se hace al vencer. Por eso se diseña con tableros y se prueba con Meta real antes de programarse. Entra también el parser de `location` del webhook, que hoy cae en `Other`.

#### Fuera de alcance

- Seguimiento continuo, rutas y geocercas.
- Mapas embebidos, búsqueda de direcciones y autocompletar (hay una pieza parecida para el negocio, en [ubicación por lugar](../../../ArquitecturaBaseFront/docs/design/ubicacion-por-lugar.md)).
- Verificar que el punto es real (anti-falsificación) o que está dentro de una zona.
- La ubicación del dispositivo en pantallas que no son un formulario.

## Decisiones tomadas

No se vuelven a preguntar.

- **Biblioteca:** formularios estándar como datos de inicio del producto. «Agregar» crea una copia propia y editable, que recuerda de qué estándar viene. Sirve para una instalación de una empresa y para el multitenant.
- **Primer estándar:** Comprobante de transferencia.
- **La bandeja «Documentos recibidos»** entra en la primera etapa.
- **La carga automática viene después, con datos:** la primera etapa siempre propone y una persona confirma.
- **Genérico:** ningún tipo de documento en el código. Un estándar nuevo es configuración.
- **Formulario nuevo armado por la IA:** nace en la bandeja, sobre un documento sin clasificar, con el aviso de documentos parecidos agrupados. La IA propone y una persona publica.
- **Tableros:** uno por formulario, automático a partir de sus preguntas y ajustable. Los que cruzan formularios o suman datos externos vienen después.
- **Quién manda:** se leen solo los mensajes de personas con cuenta vinculada y activa.
- **Retención:** un documento que termina en una respuesta se queda con ella. Uno descartado o sin clasificar se borra a los 90 días, con su lectura.
- **Tipos en la primera etapa:** foto, PDF, Word (`.docx`), Excel (`.xlsx`), ubicación y contacto.
- **Tableros elegidos (2026-10-22):** DOC5-A, la bandeja como planilla con panel, y DOC7-A, el tablero con tarjetas. DOC1–DOC4 y DOC6 tienen un solo concepto. Lo fijado al dibujar y las correcciones al elegir están en el [diseño del front](../../../ArquitecturaBaseFront/docs/design/documentos.md#los-tableros).

**Esta es la parte 1 de tres proyectos.** La parte 2, formularios conectados a sistemas externos (ARCA, Mercado Libre y cualquier API), y la parte 3, que otros sistemas reciban nuestros datos (webhooks, API pública y MCP), tienen su propio plan después de la investigación del 2026-10-22.

## Cómo encaja

- **El [motor de IA](../decisions/0020-motor-unico-de-ia-con-capacidades.md):**
  - el documento se vuelve legible para el modelo con un propósito propio, como hoy la transcripción del audio;
  - clasificar y proponer los datos son **capacidades** de BusinessForms, de lectura y propuesta;
  - ejecuta el módulo dueño.
- **[Casos del asistente](2026-10-21-casos-del-asistente.md):** «llega un documento» es un tipo de entrada de los casos, que ya quedó previsto. Sin casos del asistente, el circuito arranca igual, desde la recepción del documento.
- **Archivos privados** ([etapa 2B](../features/formularios.md#fotos-y-documentos--implementación-local-2b-2026-10-15)): la copia, la verificación por contenido, los topes y la retención.
- **El prellenado tiene que poder proponer un archivo:** hoy no lo hace ([formularios](../features/formularios.md)).
- **La ubicación que llega** completa la pregunta [«Ubicación»](#la-pregunta-ubicación) del formulario, que hay que crear antes: hoy ninguna pregunta guarda un punto.

## ADR

Las reglas de lectura están en el [ADR 0022](../decisions/0022-lectura-de-documentos-con-ia.md), aceptado el 2026-10-22:
- qué se lee y qué no;
- qué se manda a la IA;
- las validaciones fijas;
- la confirmación de una persona;
- la retención;
- los costos.

## Cómo se encara

1. El ADR, ya aceptado: [0022](../decisions/0022-lectura-de-documentos-con-ia.md).
2. Los tableros de diseño, con el chat de diseño y en la maqueta (incluidos el formulario armado por la IA y la pestaña «Tablero»). DOC1–DOC7 quedaron listos el 2026-10-22, y el usuario eligió DOC5-A y DOC7-A. Cubren:
   - la biblioteca;
   - el formulario con lectura de documentos;
   - los tres casos en el celular;
   - la bandeja.
3. La evaluación de impacto contra el código, back y front.
4. La spec técnica, con el modelo de datos, los contratos y el árbol de archivos.

## Cómo se cierra

**Piloto real con Meta,** con comprobantes reales de varios bancos y billeteras (Galicia, Santander, Nación, Mercado Pago, Brubank y otros):
- reconocimiento seguro, con duda y sin reconocer;
- un CBU con el dígito verificador mal leído, que no pasa;
- un duplicado;
- una foto movida;
- la bandeja y las correcciones registradas.
