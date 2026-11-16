# Revisión de arquitectura: IA — 2026-11-14

Cuarta corrida de [revisar la arquitectura de un desarrollo](../guides/revisar-arquitectura.md) y la primera sobre el módulo
de IA: `Modules/ArtificialIntelligence` en cada capa, sus pantallas en el front y lo que usan de él WhatsApp y Formularios.
Es de solo lectura, sobre el backend en `a9f57202` y el front en `7b95095b`; no incluye los cambios sin commitear de otras
sesiones (ese día seguían los arreglos de WhatsApp y la revisión de Formularios). Después de `a9f57202` entró `42d8f47f`, que
aplicó N27 a la cola durable, a WhatsApp y al motivo de la lectura guardada de un documento, y escribió en `despliegue.md` la
regla general: un valor nuevo, también un formato sellado o un perfil, se escribe un despliegue después del que ya sabe
leerlo. No cambia el motor ni el front: lo que se registra acá sigue en pie, e I4 aplica esa regla al motor.

- **Revisión:** cinco revisores contestaron los 30 criterios, uno por pieza de la [ficha](#ficha).
- **Verificación:** cinco verificadores recibieron el escenario y el código, sin la conclusión, e intentaron refutar cada
  «No cumple» y «Con reservas». No refutaron ninguno; ajustaron horizontes, severidades y arreglos, ya incorporados abajo, y
  sumaron casos: el contexto que Formularios y WhatsApp vuelven a armar con las versiones del código actual, la clave
  ilegible que deja al asistente aceptando y cobrando cupo sin poder responder, y la grabación y la transcripción que siguen
  disponibles después de borrar la conversación.
- **Contraste:** un revisor independiente comparó después este informe con los originales y con el código; sus
  correcciones están aplicadas.
- **Sin ejecución:** no se corrió nada. Los tiempos y los techos salen del código, y las pruebas que los confirmarían están en
  [controles y pruebas](#controles-y-pruebas-que-conviene-sumar).
- **Lo que ya registraron WhatsApp y el núcleo** se cita y no se repite: H5, H8, H9, H19, H25 y H26 de la
  [revisión de WhatsApp](2026-11-12-arquitectura-whatsapp.md), y de la [revisión del núcleo](2026-11-14-arquitectura-nucleo.md)
  su B5 y N9, N10, N15, N17, N18, N27, N28, N33 y N34, con sus decisiones del 2026-11-14.

## Resumen

- **Lo fuerte.** El motor está bien resuelto donde más cuesta:
  - el recorrido durable: una sola copia toma cada consulta, con una marca que impide que otra la cierre; cada paso al
    proveedor se anota antes de salir, una respuesta ya pagada sólo se vuelve a guardar y nunca se repite un pedido que se
    cobra; lo prueban dos servidores a la vez;
  - la IA no puede escribir: sólo lee y propone, con los permisos de la persona revisados al ofrecer, en cada llamada y al
    publicar;
  - la deduplicación y las cuotas viven en PostgreSQL y valen entre copias;
  - el contenido se cifra con su contexto, y la clave del proveedor nunca se ve ni va a otro destino.
- **Hoy, con una sola copia**, lo que más pesa:
  - una tanda de pedidos largos deja aceptados que vencen sin intentarse y gastan cupo (I1);
  - borrar una conversación deja la foto, su texto leído, la grabación y su transcripción (I2), y la metadata de Inicio no
    vence, tampoco la de una cuenta borrada (I3);
  - cada despliegue que suma un valor al motor hace fallar trabajo ajeno, y Formularios y WhatsApp lo guardan como
    definitivo (I4);
  - las fallas no dicen qué pasó: un error de la base se culpa al proveedor (I5), el turno no distingue una clave revocada de
    un cupo agotado (I6) y una clave guardada ilegible deja al asistente cobrando cupo sin poder responder (I7);
  - una fila ilegible deja sin historial a su dueño (I8), y cuarenta subidas grandes pueden voltear una copia chica (I9);
  - y, en la operación, no se puede seguir una consulta a través del motor (I10) y una pestaña de Inicio abierta durante un
    despliegue deja de mostrar las respuestas (I11).
- **Con varias copias**, una copia con otro anillo de claves arranca igual y guarda lo que las demás nunca podrán leer (I17),
  y una copia vieja que sigue viva borra los archivos de los tipos nuevos (I18). Los límites de pedidos por copia siguen
  siendo H26.
- **A gran escala**, el techo está en los candados de toda la instalación por los que pasan todas las escrituras del
  asistente (I19) y en la limpieza, la medición y la apertura de Inicio, que recorren toda la historia (I20).
- **Fuera de los criterios:** la última propuesta puede quedar afuera de la historia que se le manda al modelo, justo cuando
  la persona pide cambiarla (B1).
- **Tus decisiones del 2026-11-14** ([decisiones](#decisiones-del-2026-11-14)): en las ocho, la recomendada. Entra «Hoy» más
  I17; lo que vence sin intentarse devuelve el cupo; borrar una conversación borra también sus lecturas y transcripciones, y
  la de Inicio se borra cuando venció todo; la regla de versiones de N27 llega al motor y a Inicio; las fallas del proveedor
  se distinguen; el apagado deja terminar el paso en curso; el instante llega a Inicio ya; y los dos costos se mantienen,
  escritos.

## Ficha

Confirmada por el usuario el 2026-11-14, sin cambios.

**Entradas**

- **Rutas** (`Api/Modules/ArtificialIntelligence/Controllers`, 23):
  - conexión, `api/ai/connection`: leer, guardar, consultar un guardado (`changes/{requestId}`), probar (`test`) y listar los
    modelos de la clave (`models`);
  - información del negocio, `api/ai/business-information`: leer, guardar, consultar un guardado y buscar lugares
    (`places/search`);
  - asistente de Inicio, `api/ai/assistant`: `bootstrap`, conversaciones (listado, detalle, turnos y borrado), turnos
    (aceptar, consultar y cancelar), `file-turns` (planilla o Word), `audio` (subir, estado y grabación) y `documents` (foto
    o PDF: subir y estado).
- **Límites de pedidos:** `ai-read`, `ai-write`, `ai-test`, `ai-places` y `ai-assistant-submit`, por operador y en memoria de
  cada copia (H26).
- **Workers:** `AiAssistantBackgroundService` con `AiAssistantCoordinator` (tomar un turno, preparar, reservar cada paso,
  llamar al proveedor y cerrar) y `AiAssistantRetentionBackgroundService` (reconciliar lo abandonado, purgar y medir). El
  aviso de cierre a los canales (`IAssistantOperationCompletionSignal`) es en memoria.
- **Pantallas** (`../ArquitecturaBaseFront/src`): `/configuracion/ia` (`AiSettingsPage`), `/configuracion/negocio`
  (`BusinessInformationPage`) e Inicio (`AiAssistantPage`), con `shared/ui/assistant`.

**Datos**

- **Tablas (8):** `AiConnections`, `AiConnectionChanges`, `AiBusinessInformation`, `AiBusinessInformationChanges`,
  `AiConversations`, `AiAssistantTurns`, `AiAssistantToolExecutions` y `AiAssistantRequestReceipts`. Las tocan 14 de las 113
  migraciones.
- **Redis:** ninguna clave propia; los candados son advisory de PostgreSQL (`AiAdvisoryLockKeys`).
- **Archivos privados:** `ai/assistant/audio/*`, `ai/assistant/attachments/*` (xlsx, csv y docx) y
  `ai/assistant/documents/*` (pdf, jpg y png).
- **Cifrado propio:** un anillo AES-GCM y HMAC fuera de la base
  ([ADR 0014](../decisions/0014-credenciales-de-ia-fuera-de-la-base.md)) para la clave del proveedor, los comprobantes y el
  contenido del asistente (`assistant-content:v1`).

**Terceros**

- OpenAI (Responses, modelos, transcripción de audio y lectura de documentos), Anthropic (Messages y modelos), Google Places
  (Autocomplete), el almacenamiento privado del núcleo (Azure Blob) y PostgreSQL.
- **Qué se les manda:** la pregunta, la historia, los hechos del negocio, los resultados de las herramientas, la planilla
  preparada (12 KiB), el documento (hasta 10 MiB) y el audio (hasta 25 MB); desde WhatsApp, mensajes de clientes.
- **Bordes:** WhatsApp usa `IAssistantOperations`, `IAssistantProposalLookup`, el aviso de cierre e `IBusinessKnowledgeSource`
  (asistente automático, redactado, audios y documentos recibidos). Formularios aporta siete fuentes de capacidades e
  `IStructuredAttachmentReader`, y busca propuestas para crear borradores. El núcleo aporta las consultas del sistema, el
  catálogo de permisos, las capacidades del cliente y la retención de archivos privados.

**Reglas:** [inteligencia artificial](../features/inteligencia-artificial.md); los ADR
[0014](../decisions/0014-credenciales-de-ia-fuera-de-la-base.md),
[0017](../decisions/0017-inicio-asistente-y-operaciones-durables.md),
[0018](../decisions/0018-robustez-proporcional-al-efecto.md),
[0020](../decisions/0020-motor-unico-de-ia-con-capacidades.md),
[0021](../decisions/0021-respuestas-redactadas-por-el-asistente.md),
[0022](../decisions/0022-lectura-de-documentos-con-ia.md) y
[0024](../decisions/0024-ia-arma-formularios-y-catalogos.md); las guías [configurar IA](../guides/configurar-ia.md),
[quitar IA](../guides/quitar-ia.md) y [configurar Google Places](../guides/configurar-google-places.md); los planes del
[panel de conversaciones](../plans/2026-10-04-panel-conversaciones-inicio.md), los
[casos del asistente](../plans/2026-10-21-casos-del-asistente.md),
[la IA arma formularios](../plans/2026-11-04-ia-arma-formularios-y-catalogos.md) y el piloto de
[verificación y producción](../plans/verificacion-y-produccion.md); y los `AGENTS.md` del módulo.

**Pruebas:** integración en `tests/ArquitecturaBase.Api.IntegrationTests/Modules/ArtificialIntelligence` (49 archivos, más 17
en `TestFeatures`), unitarias de Application (57, más 9) y de Domain (8), y arquitectura (4); en el front, 15 archivos de test
en `features/artificial-intelligence` y los de `shared/ui/assistant`.

**Tamaño:** 191 archivos `.cs` sin migraciones (Api 17, Application 113, Domain 17 e Infrastructure 44), unos 590 KB; en el
front, unos 45 archivos.

**Piezas:** A configuración (conexión, credenciales, prueba y catálogo de modelos, información del negocio y lugares);
B aceptación y datos del asistente (cuotas, recibos, huella, lecturas, historial, persistencia, migraciones y retención);
C ejecución y proveedores (worker, coordinador, despacho, contexto, catálogo, terminales, transportes y clientes); D Inicio
(archivos, rutas de escritura, retención de copias y front); E bordes y separabilidad (operaciones, búsqueda de propuestas,
hechos del negocio, contrato con las capacidades de otros módulos, versiones apagadas y quitar IA).

## Matriz

| # | Criterio | Hoy | Varias copias | Gran escala | Hallazgos |
| --- | --- | --- | --- | --- | --- |
| 1 | Estado por pedido | Cumple | Cumple | Cumple | — |
| 2 | Ediciones simultáneas | Cumple | Cumple | Cumple | — |
| 3 | Repeticiones | Con reservas | Con reservas | Con reservas | I13 |
| 4 | Orden | Cumple | Cumple | Cumple | — |
| 5 | Todo o nada | Cumple | Cumple | Cumple | — |
| 6 | Cada uno lo suyo | Cumple | Cumple | Cumple | O10 |
| 7 | Sin estado propio | Cumple | Cumple | Cumple | H26 |
| 8 | Trabajos de fondo | Cumple | Con reservas | No cumple | I18, I19, I20 |
| 9 | Arranque, apagado y configuración | Con reservas | Con reservas | Con reservas | I12, I17 |
| 10 | Despliegue sin corte | No cumple | No cumple | No cumple | I4, I11, I18 y N34 |
| 11 | Consultas | Cumple | Cumple | No cumple | I19, I20 |
| 12 | Paginado y exportaciones | Cumple | Cumple | Cumple | — |
| 13 | Filtros y búsqueda | Cumple | Cumple | Cumple | — |
| 14 | Caché | No aplica | No aplica | No aplica | Sin caché del servidor (`no-store` en el controller); el del front es por cuenta, idioma y permisos; los permisos son N9 |
| 15 | Trabajo pesado fuera del pedido | Con reservas | Con reservas | No cumple | I9, I14 |
| 16 | Filas únicas | Con reservas | Con reservas | No cumple | I1, I19 |
| 17 | Plazos y reintentos | Cumple | Cumple | Con reservas | I6; los plazos del almacenamiento privado son del núcleo y no se verificaron acá |
| 18 | Estado incierto | Cumple | Cumple | Cumple | I6 e I12 exageran lo incierto |
| 19 | Degradación | Con reservas | Con reservas | Con reservas | I5, I6, I7, I8 y N9 |
| 20 | Límites y costo | Con reservas | Con reservas | Con reservas | I1, I14, H26 y el gasto, costo aceptado |
| 21 | Acceso | Cumple | Cumple | Cumple | — |
| 22 | Entradas hostiles | Con reservas | Con reservas | Con reservas | I9 |
| 23 | Datos sensibles | Cumple | Cumple | Cumple | — |
| 24 | Integridad en la base | Cumple | Cumple | Cumple | — |
| 25 | Privacidad y respaldo | No cumple | No cumple | No cumple | I2, I3 y N15 |
| 26 | Idioma | Cumple | Cumple | Cumple | B2 |
| 27 | Fechas, zonas horarias y números | Con reservas | Con reservas | Con reservas | I15 y la cuota por día UTC, costo aceptado |
| 28 | Errores | Con reservas | Con reservas | Con reservas | I4, I5, I6, I7 |
| 29 | Observabilidad | Con reservas | No cumple | No cumple | I10, H19 y N10 |
| 30 | Separabilidad | Con reservas | Con reservas | Con reservas | I16 y el [inventario](#separar-el-módulo-inventario-no-defecto) |

Trece criterios se cumplen en los tres horizontes y uno no aplica: guardar, ordenar, autorizar, cifrar y paginar están bien
resueltos y probados. Ya hoy no se cumplen la convivencia de versiones (10) y la privacidad (25); las reservas se concentran
en cómo falla (qué ve la persona y qué queda en el log), y lo que crece sin límite pasa a «No cumple» a gran escala.

## Hallazgos

Cada hallazgo dice dónde empieza a fallar, el escenario, la evidencia (archivo y línea en `a9f57202`, o `front:` en
`7b95095b`) y el arreglo con su esfuerzo. Cuando el comportamiento viene de una decisión documentada, se cita la fuente:
cambiarla lo decidís vos.

### Fuera de los criterios

#### B1. La última propuesta puede quedar afuera de la historia, justo cuando la persona pide cambiarla

- **Dónde falla:** hoy, en conversaciones con respuestas largas o formularios grandes. Severidad media: rompe en silencio el
  ajuste de una propuesta, que es el uso central de «Armar formulario con la IA».
- **Escenario:** en Inicio, alguien hace tres preguntas con respuestas de unos 7.900 caracteres y después pide armar un
  formulario; el asistente lo propone. La persona escribe «cambiá la pregunta 3». La historia se arma del turno más viejo al
  más nuevo hasta llenar 24.000 caracteres: entran las tres respuestas y la propuesta, que es lo más nuevo, queda afuera. El
  modelo recibe el pedido de cambio sin el formulario. Con respuestas de 8.000 la tercera no entra y la propuesta sí, si
  mide menos de unos 7.900 caracteres: el resultado depende de cien caracteres. Una propuesta que sola pasa los 24.000 no
  entra nunca.
- **Evidencia:** `AiAssistantHistoryPolicy.cs:19` recorre del más viejo al más nuevo y `:36-37` saltea lo que no entra sin
  cortar; la última propuesta sólo decide si lleva su contenido (`:16,21-22,32-35`), aunque el comentario promete que viaja
  entera (`:11-13`); presupuesto de 24.000 caracteres y 12 turnos (`AiAssistantContextPolicy.cs:110`,
  `AiAssistantLimits.cs:11-12`). La regla dice que la historia lleva la última propuesta para ajustarla
  (`inteligencia-artificial.md:129,135`). Ningún test prueba la propuesta con el presupuesto lleno
  (`AiAssistantHistoryPolicyTests.cs:20-32,47-51`).
- **Arreglo:** reservar primero el turno de la última propuesta, llenar el resto del más nuevo al más viejo y mandarlo en
  orden cronológico; si la propuesta sola no entra, un error visible. Con su test. Chico.
- **Arreglado el 2026-11-15 en de3ccfb4:** la propuesta se reserva primero y el resto entra del más nuevo al más viejo hasta el
  primero que no cabe, en orden cronológico; si la propuesta sola no entra, el turno falla con `Ai.Assistant.ProposalTooLarge`.

#### B2. Cambiar el idioma con una lectura en curso deja el documento o el audio sin encontrar

- **Dónde falla:** hoy, poco frecuente. Severidad baja.
- **Escenario:** alguien sube un PDF en Inicio con el menú en español y, mientras se lee, lo cambia a inglés. La consulta
  siguiente responde «no existe»; el siguiente Enviar vuelve a subirlo con el mismo identificador y recibe un conflicto; el
  tercero empieza una lectura nueva, que gasta otra del cupo y otra llamada facturable. Con un audio, cada Enviar repite el
  conflicto hasta descartar la grabación o volver al español. Cambiar la zona en el perfil hace lo mismo, pero sólo con
  Inicio abierto en otra pestaña.
- **Evidencia:** la consulta arma el contexto con el idioma del pedido y la zona actual (`AiAssistantController.cs:103,109-110,133,139-140`,
  `AiAssistantDocumentService.cs:33,62`, `AiAssistantAudioService.cs:31,51`) y exige que coincidan con lo sellado
  (`AiAssistantOperations.cs:46-47`, `AiAssistantChannelPolicy.cs:69`); el front manda el idioma en cada pedido
  (`front:src/shared/api/httpClient.ts:58-61,77-78`) y ante el 404 vuelve a subir (`front:src/features/artificial-intelligence/hooks/useAssistantDocument.ts:72-81`).
- **Arreglo:** para la lectura y el audio de Inicio, consultar por persona e identificador sin exigir idioma ni zona; y que
  el audio descarte el pedido ante un conflicto, como el documento. Chico.
- **Arreglado el 2026-11-15 en 1db62122, 2f75ee9f y cd3ef78f (front bd8ded44 y 2b26f070):** `home-audio` y `home-document`
  se consultan y se recuperan por persona e identificador, sin idioma ni zona; otra persona con el mismo identificador no
  encuentra ni toma la operación, y el audio descarta su pedido ante un 409 como el documento.

#### B3. Una falla pasajera de la base cancela la consulta en curso

- **Dónde falla:** hoy, ante un corte breve de la base mientras se espera al modelo. Severidad baja.
- **Escenario:** mientras una consulta espera al modelo, un vigilante lee el turno cada 500 ms para ver si la persona canceló.
  Si una de esas lecturas falla por un corte breve, cancela la consulta entera: el pedido al proveedor se corta y queda
  «incierto», o «cancelada» si todavía se preparaba, aunque nadie canceló. Por WhatsApp, deriva (H5).
- **Evidencia:** `AiAssistantCoordinator.cs:129-151` (`:146-150`: cualquier excepción cancela); la confirmación final ya
  verifica fence, estado y cancelación (`AiAssistantTurn.cs:150-158`). Ningún test cubre una falla del vigilante.
- **Arreglo:** ante una excepción que no sea la cancelación, registrar y seguir con la lectura siguiente. Trivial.
- **Arreglado el 2026-11-15 en 07b57864:** el vigilante registra la lectura fallida y sigue; el turno termina con la respuesta
  del proveedor.

#### B4. Una instrucción que falta en los textos se reemplaza, sin aviso, por un mensaje de error

- **Dónde falla:** latente: sólo si un cambio renombra una clave. Severidad baja.
- **Escenario:** alguien renombra una clave de instrucciones del modelo en `Assistant.resx` y no actualiza el código. El
  sistema no avisa: usa en su lugar «El proveedor no está disponible…», y el modelo recibe esa frase como instrucciones, sin
  la regla de evidencias ni la de no seguir instrucciones de los datos. Sólo lo acotan los esquemas y las validaciones
  del servidor.
- **Evidencia:** `AssistantTexts.cs:9-10`; usos como instrucciones en `AiAssistantContextPolicy.cs:113,127-128` y
  `AiAssistantExecutionService.cs:204`; `ResourceParityTests.cs:28-46` compara sólo español con inglés, y
  `AiAssistantContextPolicyTests.cs:21-33` verifica `Prompt.SystemV1`, no las versiones en uso.
- **Arreglo:** separar un `AssistantTexts.Prompt(key)` que lance si falta la clave del `Get` con respaldo para los códigos de
  falla, y un test que recorra las claves de instrucciones usadas. Trivial.
- **Arreglado el 2026-11-15 en a0eeb4d3:** `AssistantTexts.Prompt` lanza si falta la clave, y `AiAssistantPromptKeysTests`
  comprueba que cada `Prompt.*` del código existe en los dos idiomas y que ninguno pasa por `Get`.

#### B5. Inicio y los canales arman los mismos hechos del negocio con reglas distintas

- **Dónde falla:** hoy, con días del horario sin definir. Severidad baja.
- **Escenario:** el horario del negocio tiene días sin definir. Inicio (`business.information`) agrega una fila por cada día,
  también los sin definir, y la zona horaria siempre que hay detalles; el asistente de WhatsApp omite esos días y toda la
  zona si ningún día está confirmado. La regla dice que es una sola.
- **Evidencia:** `AiBusinessSystemQuerySource.cs:45-47` frente a `AiBusinessKnowledgePolicy.cs:33-39`;
  `inteligencia-artificial.md:51`.
- **Arreglo:** que Inicio use el mismo armador, o un test que compare los hechos de los dos con los mismos datos. Trivial.
- **Arreglado el 2026-11-15 en 38648b9f:** `business.information` arma sus filas con `AiBusinessKnowledgePolicy`, y un test
  compara Inicio con los canales en los dos idiomas, con y sin días confirmados.

### Hoy, con una sola copia

#### I1. Una tanda de pedidos largos deja aceptados que vencen sin intentarse y gastan cupo

- **Dónde falla:** hoy, con una sola copia: alcanza una capacitación o un grupo que use a la vez una capacidad de varios
  pasos. Más copias no ayudan. Severidad media.
- **Escenario:** en una capacitación, 30 personas prueban a la vez «Armar formulario con la IA» en Inicio; cada consulta
  tarda entre 40 y 80 segundos. Se aceptan las 30, pero en toda la instalación corren 4 a la vez, y cada pendiente vence a los
  120 segundos contados desde que se aceptó. Empiezan entre 8 y 12; las otras 18 a 22 vencen sin haberse intentado. Esas
  personas esperan dos minutos, leen «venció» y perdieron una de sus 10 consultas del día; la tanda se lleva 30 de las 100 de
  la instalación. Si reintentan, gastan otra y vuelven al final de la fila. Desde la pantalla no se puede corregir, y por
  configuración sólo se puede bajar el tope de 50 pendientes: los 4 en curso y los 120 segundos tienen techo en el código.
- **Evidencia:** la aceptación sólo mira las cuotas y el tope de 50 pendientes (`AiAssistantSubmissionPolicy.cs:149-154`);
  el plazo corre desde la aceptación (`AiAssistantTurn.cs:187-188`) y lo aplican el claim y la reconciliación de cada segundo
  (`AiAssistantDispatchService.cs:32-33`, `AiAssistantRetentionService.cs:19-24`); cuatro en curso para toda la instalación
  (`AiAssistantDispatchService.cs:34-35`, `AiAssistantRepository.cs:64-69`); valores en `AiAssistantLimits.cs:25-29` y cotas
  fijas en `AiAssistantOptions.cs:30,32`; el cupo cuenta recibos sin mirar cómo terminó el turno
  (`AiAssistantRepository.cs:59-67`).
- **Costo aceptado, en parte:** los números son propuestas del piloto sin calibrar (`configurar-ia.md:196-210,215-216`,
  ADR 0017, `inteligencia-artificial.md:217`). Ninguna fuente acepta que se admita más de lo que se alcanza a empezar, ni que
  lo que vence sin intentarse consuma cupo: la regla sólo dice que borrar no libera la cuota.
- **Arreglo:**
  - lo que vence sin haber reservado ningún paso devuelve el cupo: su recibo sigue deduplicando, pero no cuenta (chico;
    cambia la regla);
  - el tope de pendientes, calibrado como dato del piloto a lo que cuatro carriles alcanzan a empezar en 120 segundos con
    turnos largos (trivial), o rechazar por capacidad cuando la fila no llega a empezar (mediano).

  **Decidido el 2026-11-14:** devuelve el cupo y el tope de pendientes se calibra ([decisiones](#decisiones-del-2026-11-14)).
- **Arreglado el 2026-11-15 en 734eac9a y f096d255:** el recibo de un turno que terminó vencido, que el dominio sólo alcanza
  sin ningún paso reservado, sigue deduplicando pero no cuenta en las cuotas; el tope de pendientes pasa a 4, lo que cuatro
  carriles con turnos de 80 segundos empiezan antes de que venza (`RunningCapacity × floor(120 / turno largo)`; 8 con
  turnos de menos de 60 segundos).

#### I2. Borrar una conversación deja la foto, su texto leído, la grabación y su transcripción

- **Dónde falla:** hoy, en cada conversación de Inicio con una foto, un PDF o un audio. Severidad media: datos sensibles que
  siguen guardados y reutilizables después de un borrado explícito; sólo los alcanza su dueño.
- **Escenario:** una persona adjunta en Inicio la foto de su documento de identidad y un audio, pregunta y después borra la
  conversación. La pantalla ya no muestra nada y la purga borra el contenido de esos turnos. Pero la lectura de la foto y la
  transcripción del audio son operaciones aparte, en conversaciones propias que el borrado no toca:
  - la foto sigue en el almacenamiento y su texto leído sigue cifrado en la base hasta 30 días; consultar ese documento
    sigue dando su nombre, tipo, páginas y legibilidad;
  - la transcripción sigue 30 días y consultar el audio la devuelve entera; la grabación se puede bajar hasta la limpieza
    siguiente, uno o dos días;
  - un turno nuevo que nombre esos identificadores vuelve a tomar la lectura o la transcripción, en otra conversación.

  El aviso dice «Vas a perder el acceso a sus preguntas, respuestas y fuentes»: no promete borrar los archivos, pero tampoco
  avisa que quedan.
- **Evidencia:** `AiAssistantService.cs:59-79` sólo retira la conversación de Inicio y cancela su turno activo; la purga toma
  los turnos de las conversaciones retiradas (`AiAssistantRepository.cs:82-86`); las lecturas y transcripciones van en
  canales propios (`AiAssistantChannelPolicy.cs:19-26`, `AiAssistantSubmissionPolicy.cs:172-176`); la lectura retiene la
  foto mientras tenga contenido (`AiAssistantDocumentRetentionSource.cs:18-21`, `AiAssistantReader.cs:82-89`); siguen
  accesibles por `GET documents/{id}` y `GET audio/{id}` (`AiAssistantAudioService.cs:45-53,63-65`, `AiAssistantHomeAudioPolicy.cs:47-63`,
  `AiAssistantDocumentService.cs:56-64`) y reutilizables (`AiAssistantSubmissionPolicy.cs:238-271`);
  `front:src/locales/es/assistant.json:37-38`.
- **Arreglo:** al borrar una conversación de Inicio, purgar en la misma unidad las lecturas y transcripciones que nombraron
  sus turnos (las otras conversaciones que las usaron tienen su propia copia sellada); ajustar el aviso y escribir la regla.
  Chico. **Decidido el 2026-11-14:** borrar una conversación purga sus lecturas y transcripciones, y lo que nunca llegó a un
  turno vence a las 24 horas ([decisiones](#decisiones-del-2026-11-14)).

#### I3. La metadata de Inicio no vence, tampoco la de una cuenta borrada

- **Dónde falla:** hoy. Una cuenta suele borrarse más de 30 días después de su último uso, así que es el caso común.
  Severidad media: no queda texto, pero sí metadata de uso ligada a la persona, y una regla escrita no se cumple.
- **Escenario:** se borra la cuenta de alguien que usó Inicio hace tres meses. Sus turnos ya no tienen texto, así que la
  purga no los mira y nunca se entera de que la cuenta se borró: sus conversaciones no se retiran y quedan para siempre, con
  la fecha y hora de cada consulta, el modelo, el consumo, la zona, el idioma, la configuración con que se atendió y la
  ubicación, una huella y el tamaño de cada archivo. Lo mismo pasa con toda conversación de Inicio que su dueño no borra, y con las de alguien al que le
  sacaron el permiso del asistente, que ya no las puede borrar.
- **Evidencia:** la purga elige sólo turnos con contenido antes de mirar al dueño (`AiAssistantRepository.cs:82-86`,
  `AiAssistantRetentionService.cs:40-53`); una conversación de Inicio sólo se borra retirada (`AiAssistantRepository.cs:94-100`),
  y lo fija el caso `home` de `AiAssistantRetentionTests.cs:102-129`; la purga deja la metadata (`AiAssistantTurn.cs:194-202`);
  sin `ai.assistant.use` no se puede borrar (`AiAssistantController.cs:20`, `AiAssistantService.cs:69-70`). La regla dice que
  «las cuentas borradas retiran y cancelan las suyas» (`inteligencia-artificial.md:217`); el único test usa un turno con
  contenido (`AiAssistantDispatchServiceTests.cs:98-109`).
- **Arreglo:** retirar las conversaciones de una cuenta borrada aunque ya no tengan contenido, con una prueba de tres meses
  después del último uso. Chico. **Decidido el 2026-11-14:** una conversación de Inicio se borra cuando venció todo su
  contenido y sus recibos ([decisiones](#decisiones-del-2026-11-14)).

#### I4. Cada despliegue que suma un valor al motor hace fallar trabajo ajeno, y otros módulos lo guardan como definitivo

- **Dónde falla:** hoy, en cada despliegue que agrega un propósito, un tipo de archivo, un modo, una versión de
  instrucciones, de una capacidad o de una terminal, o un proveedor; con varias copias, durante todo el despliegue gradual; y
  de lleno al volver a la imagen anterior. Una parte pasa aun sin convivencia. Severidad media. Es la clase de H25 y N27, por
  caminos del motor que esas revisiones no nombran.
- **Escenario:**
  - la copia nueva acepta un turno con un valor que la vieja no conoce. El worker de la vieja lo toma igual, no lo entiende y
    lo cierra como fallido sin llamar al proveedor: la persona lee «Ya no tenés todos los permisos que requiere esta
    conversación. Podés eliminarla o iniciar otra.», que la invita a borrar una conversación sana, y la consulta igual gasta
    su cupo;
  - si la copia vieja recibe la consulta de ese turno, contesta «no existe». Formularios descarta para siempre la extracción
    del documento y WhatsApp marca el documento recibido como «No disponible», aunque la copia nueva lo haya leído bien; el
    asistente de WhatsApp, en cambio, lo deja pendiente y reintenta;
  - Formularios y WhatsApp no guardan el contexto que mandaron: lo vuelven a armar con las versiones del código actual. Si una
    versión sube la de una capacidad o terminal, todo lo que la anterior dejó en vuelo falla en la nueva aunque ya no
    conviva nada;
  - si se agrega un proveedor (por ejemplo, Gemini) y se lo guarda, toda la IA de la copia vieja da 500: el proveedor se
    guarda como texto y la lectura lanza ante un nombre que no conoce.
- **Evidencia:** la lectura del sello rechaza un valor o unas instrucciones que no conoce
  (`AiAssistantChannelPolicy.cs:35-52,61-62`) y `SameScope` exige lo sellado dentro de lo pedido (`:64-74`); la consulta, la
  búsqueda y la cancelación lo convierten en «no existe» (`AiAssistantOperations.cs:43-47`, `AiAssistantProposalLookup.cs:26-30`,
  `AiAssistantSubmissionPolicy.cs:37-39`); el claim toma el pendiente más viejo sin mirar versiones y lo falla
  (`AiAssistantRepository.cs:29-31`, `AiAssistantDispatchService.cs:26-43`, `AiAssistantDispatchGuard.cs:37-49`,
  `Errors.resx:129,136`); Formularios (`FormReceivedDocumentExtractionService.cs:121-144`,
  `FormReceivedDocumentExtractionPolicy.cs:33-43`) y WhatsApp (`WhatsAppReceivedDocumentDispatchService.cs:171-181`,
  `WhatsAppAssistantAccessPolicy.cs:50,78,101,126,147`; la huella incluye las versiones,
  `HmacAiAssistantRequestFingerprint.cs:12-25`); el proveedor como texto (`AiConnectionConfiguration.cs:12`,
  `AiConnectionChangeConfiguration.cs:13`, `AiAssistantTurnConfiguration.cs:15`), como ya anota `despliegue.md:39`. La regla
  dice que un 404 de recuperación no demuestra rollback (`inteligencia-artificial.md:101`).
- **Arreglo, la regla de N27 extendida al motor:**
  - un valor nuevo del sello o del proveedor se escribe un despliegue después de la versión que ya lo lee: la regla general
    ya está en `despliegue.md` desde `42d8f47f`, y falta anotarla en `inteligencia-artificial.md` para el sello del motor y el
    proveedor;
  - la lectura distingue «no lo conozco» de «dañado»: la consulta devuelve una falla reintentable en lugar de «no existe», y
    el worker no toma lo que no entiende (lo vence su plazo);
  - los módulos aceptan la versión sellada de su propia capacidad mientras el catálogo la conozca, en vez de exigir la actual.

  Chico a mediano. **Decidido el 2026-11-14:** la regla de N27 llega al motor ([decisiones](#decisiones-del-2026-11-14)). Lo
  que hacen Formularios y WhatsApp con un 404 es de sus revisiones; quedó anotado para la de Formularios.

#### I5. Si una capacidad falla con un error de la base, el turno entero se pierde y se culpa al proveedor

- **Dónde falla:** hoy, ante un corte o un reinicio de PostgreSQL durante una lectura. Severidad media: el diagnóstico manda a
  revisar OpenAI cuando falló la base, y contradice la regla escrita.
- **Escenario:** un cliente escribe por WhatsApp en modo Asistente y, mientras el motor lee las candidatas de Formularios, la
  base corta la conexión. La lectura no queda «no disponible» para que el modelo siga sin ella: el turno entero termina con
  «El proveedor no está disponible» y la conversación pasa a una persona (H5). En WhatsApp esas lecturas se precargan, así que
  el turno muere sin llamar al modelo; si la lectura la pidió el modelo, ese paso ya se pagó. El log dice «stopped with
  NpgsqlException», sin la capacidad.
- **Evidencia:** sólo `IOException`, `TimeoutException` y el plazo local se vuelven «no disponible»
  (`AiAssistantExecutionService.cs:231-238`), aunque su comentario dice que toda falla recuperable lo es (`:211-212`); el
  ejecutor no atrapa nada (`ScopedAssistantCapabilityExecutor.cs:12-21`); el coordinador elige `ProviderUnavailable`
  (`AiAssistantCoordinator.cs:79-80,106-111`) y registra sólo el tipo (`:196-197`); las precargas de WhatsApp
  (`WhatsAppAssistantAccessPolicy.cs:71-75`). La regla: `inteligencia-artificial.md:127,147`. El test prueba sólo IO y plazo
  (`AiAssistantObservedReadTests.Unavailable.cs:85-98`).
- **Arreglo:** en el ejecutor, una excepción de la base no cancelada se vuelve un resultado fallido con un log que nombre la
  capacidad, la versión y el turno: una lectura queda «no disponible» y una propuesta corta con un código propio; el
  coordinador usa `ProviderUnavailable` sólo para lo que salió del proveedor. Con su test. Chico.
- **Arreglado el 2026-11-15 en 69ec0c45:** el ejecutor convierte una excepción de la base de la cadena en `System.Query.Unavailable`
  y la registra con su tipo y su `SqlState` (en Error si no es transitoria); la propuesta corta con
  `Ai.Assistant.ExecutionFailed`, y en WhatsApp responder o redactar exige haber leído el agente en el turno.

#### I6. El turno no distingue una clave revocada, un cupo agotado ni un modelo retirado, y llama «incierto» a lo que no salió

- **Dónde falla:** hoy, la primera vez que vence la tarjeta de la cuenta del proveedor, se rota la clave o se retira el
  modelo. Severidad media: la causa más común de una caída del asistente no se diagnostica desde la app ni desde el log.
- **Escenario:**
  - se vence la tarjeta de la cuenta de OpenAI (429), alguien revoca la clave (401) o se retira el modelo (400 o 404): las
    tres dan el mismo «El proveedor rechazó la consulta. Revisá la conexión y el modelo configurados.», y el log no tiene el
    estado HTTP. El administrador no sabe si cargar crédito, cambiar la clave o el modelo;
  - el servidor no resuelve `api.openai.com` o la conexión es rechazada: nada salió, pero la consulta queda «incierta» y pide
    revisar el posible consumo;
  - el proveedor no contesta: cada consulta espera sus 30 segundos ocupando uno de los cuatro lugares y queda incierta, lo
    correcto porque pudo cobrarse; nada corta antes para las siguientes.

  «Probar conexión» sí distingue cada caso; el turno, no. Por eso queda «Con reservas» y no «No cumple»: quien administra
  tiene cómo averiguar la causa.
- **Evidencia:** todo 4xx es `ProviderRejected` y todo error de transporte, `ProviderUncertain`
  (`LlmConversationHttpTransport.cs:39-41,62-64`; igual en audio, `OpenAiAudioTranscriptionClient.cs:57-59,81-83`, y en
  documentos, `OpenAiDocumentReadingClient.cs:84-86,107-109`); sin plazo de conexión propio
  (`AiAssistantProvidersRegistration.cs:33-34`); el coordinador no registra el estado (`AiAssistantCoordinator.cs:74-77`);
  `Errors.resx:137-139`. El test fija a propósito no leer los cuerpos (`AiAssistantProviderContractTests.cs:179-197`); tirar el
  estado no lo decide ninguna fuente, y la prueba de conexión sí lo usa (`LlmHttpTransport.cs:19-54`,
  `inteligencia-artificial.md:45`). Es la clase de H9, del lado del modelo.
- **Arreglo:** clasificar por estado, sin leer el cuerpo: 401 y 403, clave rechazada; 402 y 429, cupo o límite del proveedor;
  400 y 404, modelo o pedido rechazado, con textos para quien administra; la resolución de nombres, la conexión rechazada y
  el TLS, «no disponible» (no incierto), con un plazo de conexión; y el estado en un log. Chico. Un corte que falle rápido
  después de varios rechazos es opcional para hoy (mediano; para que valga entre copias, guardado en la fila de la
  conexión). **Decidido el 2026-11-14:** se clasifica por estado y el corte queda para más adelante
  ([decisiones](#decisiones-del-2026-11-14)).
- **Arreglado el 2026-11-15 en cd4b21db:** por estado, sin leer el cuerpo: 401 y 403 dan `ProviderCredentialRejected`, 402 y
  429 `ProviderLimited`, 400 y 404 `ProviderModelRejected`; la resolución de nombres, la conexión y el TLS, con un plazo de
  conexión de 10 segundos, dan `ProviderUnavailable` (nada salió); el estado va a `LogProviderFailed`. Vale para texto, audio
  y documentos.

#### I7. Una clave guardada ilegible deja al asistente cobrando cupo sin poder responder, y la pantalla dice «error inesperado»

- **Dónde falla:** hoy, después de restaurar la base sin una clave histórica del anillo, o de retirar una clave que la
  conexión todavía usa; la guía lo prohíbe, pero cuando pasa la pantalla engaña. Severidad media.
- **Escenario:** el administrador abre Configuración → Inteligencia artificial: cargar los modelos y «Probar conexión»
  responden 500, y la pantalla dice «Ocurrió un error inesperado» con un código. El servidor tenía un texto útil («No pudimos
  leer la credencial guardada…»), pero no llega, y nada dice que reemplazar la clave lo arregla, aunque lo arregla. Mientras
  tanto el asistente figura «disponible», acepta cada pregunta, le cobra el cupo a la persona y a la instalación y el worker
  la falla antes de llamar al proveedor, con un texto que la persona no puede resolver. Si se gastan así los 100 turnos de la
  instalación, el asistente queda bloqueado hasta el día siguiente aunque ya se haya arreglado. Lo mismo, menor: sin clave
  de Google, el buscador de lugares se muestra igual y cada búsqueda da un 500 con su aviso.
- **Evidencia:** `AiConnectionErrors.cs:14` se declara como falla interna y sale como 500 (`ProblemDetailsMapper.cs:30`);
  `AiConnectionCandidatePolicy.cs:22-31` (con «Reemplazar» no descifra la vieja); el front cambia todo 500 que no sea
  `Ai.Provider.*` por el texto genérico (`front:src/features/artificial-intelligence/errors.ts:5-8`) y ningún archivo del front
  nombra `CredentialUnavailable`; el texto del servidor en `Errors.resx:93`; la disponibilidad mira sólo que haya clave
  (`AiAssistantAccessGuard.cs:130`) y el contexto falla en el worker (`AiAssistantContextPolicy.cs:44-45`,
  `AiAssistantCoordinator.cs:58`); Places sin clave (`BusinessPlacesErrors.cs:5`, `GooglePlacesClient.cs:16`,
  `front:src/features/artificial-intelligence/components/BusinessPlaceSearch.tsx:35-36`). La recuperación está en la guía
  (`configurar-ia.md:109-111`), y el ADR 0014 pide fallas de descifrado explícitas (`:17`).
- **Arreglo:** que `CredentialUnavailable` llegue legible, con «reemplazá la clave»; que la disponibilidad pruebe el descifrado
  (local, sin proveedor) para no aceptar turnos que no pueden andar; y el buscador de lugares como capacidad apagada sin clave.
  Chico.
- **Arreglado el 2026-11-15 en a55bb418 y 80cd3f17 (front 6b8183c5):** `CredentialUnavailable` es un 409 que manda a
  «Reemplazar clave»; la disponibilidad prueba el descifrado después de los controles en memoria, una vez por pedido y sin
  texto en claro, y una clave ilegible da `notConfigured` y no acepta turnos; un turno ya aceptado cierra como
  `Ai.Assistant.Unavailable`. Sin clave de Google, `place-search` no se publica y la búsqueda da 409.

#### I8. Una conversación ilegible deja sin historial a su dueño y le gasta el cupo

- **Dónde falla:** hoy, ante una fila dañada o una clave del anillo borrada antes de tiempo, algo que la guía advierte. Raro;
  severidad media: la persona no lo puede arreglar sola.
- **Escenario:** una fila no se puede descifrar. La persona abre el historial de Inicio y, en vez de ver sus conversaciones
  con una marcada como no disponible, ve un error para toda la página, y no la puede borrar desde ahí. Si sigue esa
  conversación, el pedido se acepta, gasta cupo y falla al armar el contexto; vuelve a andar recién cuando la fila sale de los
  últimos 13 turnos, o sea, más de un día de cupo, y el error le dice que no repita. Si se borró una clave del anillo, falla
  casi todo el historial hasta que vence, y con I17 la lista alterna entre verse bien y dar error según la copia.
- **Evidencia:** el primer título que falla corta la página (`AiAssistantReadPolicy.cs:27-31`), igual el detalle y la página
  de turnos (`:46-47,77-80`); el test lo exige así (`AiAssistantConversationHistoryTests.cs:44-58`); el contexto falla con el
  primer turno ilegible (`AiAssistantContextPolicy.cs:95-107`); `AiAssistantLimits.cs:11,30,32`; `Errors.resx:130`; la guía
  (`configurar-ia.md:95-96`).
- **Arreglo:** que lo ilegible sea estado de la fila y no de la página: título genérico y acceso «no disponible», como lo
  bloqueado; el contexto lo saltea como vencido, con un log; se puede borrar; y el test exige ver las demás. Chico.
- **Arreglado el 2026-11-15 en a57d86f7 y e7d90a6e (front b6b90091 y 912d5182):** sólo lo que no se descifra queda
  `unavailable`, fila por fila, con un aviso por pedido que lleva la cantidad y el primer id; el contexto y el archivo
  heredado lo saltean como vencido, y otras fallas cortan como antes. Inicio muestra los turnos legibles y deja borrar la
  conversación.

#### I9. Cuarenta subidas grandes a la vez pueden voltear una copia chica

- **Dónde falla:** hoy en un pico, con una copia de 1 o 2 GiB de memoria; a gran escala, con cualquiera. Severidad media.
- **Escenario:** cuarenta personas suben a la vez planillas, Word, fotos o PDF de 10 MiB en Inicio. Cada subida queda entera
  en memoria tres veces (una copia que crece hasta 16 MiB, otra de 10 MiB y una tercera de hasta 16 MiB al releer lo
  guardado), más lo que use el lector de Formularios, que abre el archivo dentro del pedido: unos 40 MiB por subida, 1,6 GiB las
  cuarenta. La copia llega a su tope y, o los pedidos dan 500, o la plataforma la reinicia y caen todos los pedidos en curso.
  El único freno es de seis envíos por minuto por persona, que no limita cuántas subidas corren a la vez.
- **Evidencia:** `AiAssistantController.cs:83-88,101-104,131-134` (`MemoryStream` y `ToArray`), con cuerpos de hasta 10 MiB
  más 64 KiB (`:28-32`); la relectura entera (`AiAssistantHomeDocumentPolicy.cs:66-71`,
  `AiAssistantHomeAttachmentPolicy.cs:158-163`, `AiAssistantHomeAudioPolicy.cs:40-42`); el lector recibe bytes, nombre y token,
  sin topes de IA (`IStructuredAttachmentReader.cs:18`; los topes del puerto, en `StructuredAttachmentModels.cs:24-32`); sólo la
  ventana por persona (`ArtificialIntelligenceApiRegistration.cs:26-35`, `AiRateLimitOptions.cs:9`). La regla publica la
  lectura en el pedido (`inteligencia-artificial.md:197,203`), no su costo con concurrencia.
- **Arreglo:** un limitador de concurrencia por copia para las rutas de subida, con 429 y espera; una sola copia en memoria y
  la relectura con un hash incremental. Chico a mediano. Si el lector decodifica la foto entera (hasta 32 Mpx, unos 128 MiB),
  lo mira la revisión de Formularios.
- **Arreglado el 2026-11-15 en 2c9303c4 y f2c58ed5:** las tres rutas que reciben archivos toman un lugar después del
  permiso y de la ventana por persona: uno por persona y cuatro por copia, con una cola corta, 429 con espera y un plazo
  para recibir el cuerpo; el archivo se lee una vez a un arreglo de su largo y la copia guardada se compara por bloques con
  un hash incremental.

#### I10. No se puede seguir una consulta a través del motor, y el sondeo llena el log

- **Dónde falla:** hoy; con varias copias no hay con qué unir las partes de una consulta (ni el turno ni el log dicen qué
  copia la atendió), y a gran escala no se puede diagnosticar. Severidad media.
- **Escenario:**
  - un cliente reclama una respuesta rara del asistente de WhatsApp, o Ana dice que su consulta de las 10:32 falló. La traza de
    H19 une el webhook con el procesador de WhatsApp, pero se corta al entregar el pedido al motor: el worker, las capacidades
    y la llamada al proveedor quedan como trazas sueltas, y los logs del worker no dicen qué turno. Soporte busca el turno en
    la base y después los logs por hora;
  - sin que nadie use el asistente, cada copia escribe unas 24 líneas por segundo de registro normal (los cuatro carriles y
    la reconciliación preguntan cada segundo si hay trabajo, y cada pregunta deja también sus consultas a la base): unos 2
    millones por día que tapan los avisos útiles y se pagan al guardarlos.
- **Evidencia:** sin `ActivitySource` ni contexto de traza en el módulo, en el pedido neutro (`AssistantOperationModels.cs:15-26`)
  ni en el turno; el registro sólo suma medidores (`ArtificialIntelligenceInfrastructureRegistration.cs:48`); un solo log
  lleva el turno (`AiAssistantCoordinator.cs:194-199`); WhatsApp sí traza (`WhatsAppTelemetry.cs:12-33`,
  `WhatsAppInboundProcessor.cs:138-140`). El sondeo: `AiAssistantOptions.cs:15`, `AiAssistantBackgroundService.cs:13,17-25`,
  `AiAssistantDispatchService.cs:19-28`, `AiAssistantRetentionService.cs:15-21`, `OperationLog.cs:21-27,37-44` y
  `appsettings.json:2-7`. Agrega a H19 y N10 el tramo del motor.
- **Arreglo:** como H19: una fuente de trazas del asistente, el contexto de quien llama guardado en el turno al aceptar, una
  actividad por turno en el coordinador que lo continúe y el turno en el scope de los logs (chico a mediano); el sondeo vacío
  sin `OperationLog` y los comandos de EF en Warning (trivial).
- **El sondeo, arreglado el 2026-11-15 en 7d97d88d:** el claim y la reconciliación ya no pasan por `OperationLog` y los
  comandos de EF van en Warning fuera de Development. La traza es la tarea 25 del plan.

#### I11. Una pestaña de Inicio abierta durante un despliegue deja de mostrar las respuestas

- **Dónde falla:** hoy, en cada despliegue que suma un campo a un turno, a una lectura o a un audio, mientras la pestaña siga
  abierta: horas o días. Severidad media.
- **Escenario:** una persona tiene Inicio abierto mientras se despliega una versión que suma un campo a la respuesta de un
  turno. Su pestaña sigue con el front viejo, que rechaza cualquier campo que no conoce: la pregunta se procesa bien, pero la
  persona ve «Algo salió mal» y un «Reintentar» que falla igual; con un documento o un audio, avisos de «no sabemos si
  llegó» en cada Enviar. Nada le pide recargar, y si repite la pregunta gasta cupo. El arreglo de H25 no lo cubre: es el
  navegador contra la Api nueva, y pasa con una sola copia.
- **Evidencia:** `front:src/features/artificial-intelligence/api/assistant.ts:205-207` lanza «Invalid assistant contract.» con
  esquemas `z.strictObject` (`:52-58,109-115,118-133`); la regla del front lo pide
  (`front:src/features/artificial-intelligence/AGENTS.md:32-33`); los campos nuevos del bootstrap ya se hicieron opcionales para
  la transición (`:148-154`). Es un patrón de todo el front: hay 382 usos de `strictObject` fuera de los tests.
- **Arreglo:** leer las respuestas tolerando campos desconocidos y dejar lo estricto en los tests de contrato, que leen los
  ejemplos exportados; o que la Api publique la versión del build y el front pida recargar cuando no coincida. Chico.
  **Decidido el 2026-11-14:** Inicio tolera campos desconocidos; extenderlo a todo el front se decide con su regla
  ([decisiones](#decisiones-del-2026-11-14)).

#### I12. Cada despliegue corta las consultas en curso y las rotula mal

- **Dónde falla:** hoy, en cada despliegue con consultas en curso (cuatro como mucho). Severidad baja. Es H8, del lado del
  asistente.
- **Escenario:** se despliega a las 15. La consulta que Ana hizo hace 10 segundos espera al modelo: la copia vieja corta el
  pedido en el acto y la consulta queda «incierta», con la respuesta que estaba por llegar perdida y quizás cobrada. Otra que
  todavía se preparaba queda «cancelada», con «el proveedor puede haber consumido recursos»: nadie canceló ni hubo llamada, y
  la copia nueva la podría haber tomado. Por WhatsApp, las dos derivan (H5).
- **Evidencia:** el turno enlaza el token de apagado (`AiAssistantCoordinator.cs:46-48`); el corte llega como incierto (`:72-77`,
  `LlmConversationHttpTransport.cs:62`) o como cancelado (`AiAssistantCoordinator.cs:104-111`, `AiAssistantTurn.cs:160-166`);
  `Errors.resx:132,138`. Una copia que muere de golpe retiene su lugar hasta 105 segundos: lo acepta la regla
  (`AiAssistantCoordinator.cs:14`, `inteligencia-artificial.md:215`). El plazo de apagado es N17.
- **Arreglo:** al apagarse, dejar de tomar turnos y darle al paso en curso un plazo para terminar; lo que no reservó un paso
  vuelve a pendiente en vez de cancelarse. Chico, con el plazo de apagado de N17. **Decidido el 2026-11-14:** así
  ([decisiones](#decisiones-del-2026-11-14)).

#### I13. El mismo audio dos veces casi a la vez da «conflicto»

- **Dónde falla:** hoy, poco probable. Severidad baja.
- **Escenario:** alguien graba un audio en Inicio y toca Enviar; la red se corta y vuelve a tocar mientras el servidor acepta
  el primero. El segundo choca con el recibo del primero, porque la huella de una operación incluye el instante, y recibe un
  conflicto: ve un error y recién el tercer intento recupera. Con una foto o una planilla el mismo caso termina bien. Además,
  otra grabación con el mismo identificador devuelve la transcripción de la primera sin aviso (con un documento es un
  conflicto); el front de la app no llega a ese caso.
- **Evidencia:** `AiAssistantAudioService.cs:32-41`; la huella con el instante y los vencimientos
  (`HmacAiAssistantRequestFingerprint.cs:27,29`, `AiAssistantSubmissionPolicy.cs:89-93`); los documentos lo resuelven
  (`AiAssistantDocumentService.cs:35-37,50-53,66-70`); la regla lo promete para planillas y documentos
  (`inteligencia-artificial.md:197,203`), no para el audio (`:183`); sin test de superposición del audio
  (`AiAssistantHomeAudioTests.cs:150-162`).
- **Arreglo:** lo mismo que los documentos: ante el conflicto, volver a consultar y devolver esa transcripción si el archivo
  coincide, y comparar el archivo en el reintento; con los dos tests espejo. Chico.
- **Arreglado el 2026-11-15 en 45ef6b5e, 6bcac225 y cf1f056b:** el audio compara la grabación con el descriptor (SHA-256,
  tamaño y tipo) y da 409 si es otra mientras la normalización conserve su contenido; ante el choque de la aceptación
  vuelve a consultar y devuelve esa normalización, y una falla de esa consulta se responde tal cual.

#### I14. Con el cupo agotado, cada envío igual lee y guarda el archivo

- **Dónde falla:** hoy, sólo con un script o un cliente propio. Severidad baja: no gasta proveedor.
- **Escenario:** alguien agotó sus lecturas del día y un script con su sesión sigue mandando fotos de 10 MiB. Cada pedido pasa
  el permiso, el lector abre el archivo, la copia se guarda y recién la aceptación contesta «cupo agotado». Con seis por
  minuto son unas 8.640 copias por día y por copia de la Api, unos 84 GiB, que quedan hasta la limpieza del día siguiente. Un
  audio sin perfil de transcripción también se guarda antes de fallar.
- **Evidencia:** el orden en `AiAssistantDocumentService.cs:27-47`, `AiAssistantFileTurnService.cs:25-52` y
  `AiAssistantAudioService.cs:27-41`; el cupo y el perfil, recién en la aceptación (`AiAssistantSubmissionPolicy.cs:149-153,165-169`);
  la limpieza borra a las 24 horas (`PrivateFileCleanupService.cs:16-34`).
- **Arreglo:** una revisión barata, sin candados, del cupo, la capacidad y el perfil antes de leer y guardar, con el mismo
  error; la aceptación sigue mandando. Chico.
- **Arreglado el 2026-11-15 en 3993227a y 1881ab06:** las tres rutas revisan sin candados el cupo, la capacidad y, en el
  audio, el perfil, después del reintento y de los controles de permiso y almacenamiento y antes de leer o guardar; si
  rechaza, buscan otra vez el reintento, así dos subidas del mismo pedido terminan en una operación.

#### I15. En Inicio el modelo no sabe qué día es, y nadie le pide fechas con números

- **Dónde falla:** hoy. Severidad baja.
- **Escenario:** en Inicio, Marta pregunta «¿estamos abiertos ahora?» o «¿qué se cargó ayer?». A diferencia de los canales, la
  consulta de Inicio no lleva el instante ni la zona del negocio: el modelo pregunta o supone una fecha. Además, ninguna
  instrucción le pide fechas sólo con números ni números con la región del idioma: puede escribir «14 de noviembre» o
  «1,234.5» en castellano. Y cuando falta una herramienta, la instrucción le hace decir que la persona no tiene permiso,
  aunque ese permiso no exista.
- **Evidencia:** Inicio captura el perfil sin instante (`AiAssistantAccessGuard.cs:88-90`; los canales sí, `:92-94`) y el
  contexto agrega la hora sólo si existe (`AiAssistantContextPolicy.cs:111-114`); `Prompt.SystemV2` y `Prompt.CapabilitiesV1`
  no hablan de formatos (`Assistant.resx:65,67`). Atenúa que ninguna capacidad de Inicio recibe fechas.
- **Arreglo:** usar en Inicio el instante del pedido al armar el contexto, sin tocar el perfil sellado (no cambia las
  instrucciones); y pedir fechas y números como la app en una versión nueva de las instrucciones, que se vuelve a calificar
  (`inteligencia-artificial.md:139`). Trivial, más la calificación. **Decidido el 2026-11-14:** el instante ya; el formato,
  con la próxima calificación ([decisiones](#decisiones-del-2026-11-14)).
- **El instante, arreglado el 2026-11-15 en ed519cc7:** en Inicio, la hora del pedido y la zona del perfil van al final del
  mensaje actual con `Prompt.SourceTime`, sin tocar el perfil sellado ni las instrucciones. Ese texto dice «zona del negocio»:
  ajustarlo para Inicio va con el formato, en la próxima versión que se califique.

#### I16. La guía para quitar IA quedó atrás del código

- **Dónde falla:** hoy, para quien arme un derivado sin IA desde una instalación con datos. Severidad baja.
- **Escenario:** la guía dice que se retiran 63 claves `Ai.*` y hay 76; no nombra las copias de los documentos de Inicio
  (`ai/assistant/documents/…`), que también pierden su fuente de retención y la limpieza borra; y no dice qué pasa con lo que
  quedó a medio hacer: los chats de WhatsApp en modo Asistente siguen así y cada mensaje se deriva a una persona, y las
  extracciones pendientes de Formularios quedan trabadas sin poder pedir otra.
- **Evidencia:** `quitar-ia.md:25,46-49`; las 76 claves en `Errors.resx` y su par; `AiAssistantDocumentRetentionSource`
  registrada (`ArtificialIntelligenceApplicationRegistration.cs:26`); el puerto apagado (`DisabledAssistantOperations.cs:16-32`);
  el cambio de modo sólo impide pasar a Asistente (`WhatsAppChatModeService.cs:33-34`); Formularios
  (`FormReceivedDocumentExtractionService.cs:102,124-125,161-166`). Las tablas y las claves foráneas coinciden con la guía.
- **Arreglo:** corregir la guía: sin número fijo (remitir al test), con el prefijo de documentos y un paso previo que pase a
  Manual los chats en modo Asistente y cierre las extracciones pendientes. Chico.
- **Arreglado el 2026-11-15 en e1425a6e:** la guía remite a `ErrorCodeTests`, nombra los tres prefijos y las tres fuentes de
  retención, y suma «Antes de quitarlo» (chats a Manual, la conexión pausada y cómo comprobar que no quedan extracciones),
  además de las dos pruebas de WhatsApp, el arnés y las cohortes que también se quitan.

### Con varias copias

#### I17. Una copia con otro anillo de claves arranca igual y guarda lo que las demás nunca podrán leer

- **Dónde falla:** con varias copias, o con una sola durante un despliegue cuya revisión nueva trae otro anillo por error. La
  probabilidad es baja, porque exige una configuración que la guía prohíbe; el efecto es alto. Severidad media.
- **Escenario:** el negocio suma una réplica, o una revisión nueva, y le carga otras claves de cifrado, u otro contenido con
  el mismo nombre de clave. Arranca y la plataforma la da por lista, porque sólo se valida el formato. Desde ahí cada copia lee sólo lo que
  ella cifró: si el administrador guarda la clave del proveedor en esa copia, las demás no la leen y el asistente falla en la
  mitad de los pedidos; el reintento del mismo guardado en otra copia recibe un conflicto en vez del comprobante; una
  pregunta aceptada en una copia y tomada por el worker de otra falla; y la lista de conversaciones alterna entre verse bien
  y dar error (I8). Al corregir el anillo, lo que cifró la copia distinta queda ilegible para siempre.
- **Evidencia:** sólo se valida el formato (`AiSecretProtectionOptions.cs:8-17`, con `ValidateOnStart` en
  `ArtificialIntelligenceInfrastructureRegistration.cs:24-25`) y no hay chequeo de salud de IA (los registrados son `self`, la
  base y WhatsApp); el `InstallationId` entra en el dato asociado de la clave y del contenido y en la sal de todas las claves
  derivadas (`AesGcmAiSecretProtector.cs:47`, `AesGcmAiAssistantContentProtector.cs:52-53`, `AiKeyRing.cs:16-20`); los
  reintentos (`HmacAiChangeFingerprint.cs:18-21`, `AiAssistantSubmissionPolicy.cs:91-93`). Lo evita sólo la guía
  (`configurar-ia.md:16-17,88-92`); los tests prueban el aislamiento a propósito (`AiSecretProtectionTests.cs:56,76-80`), no que
  se detecte una copia desalineada.
- **Arreglo:** un valor de control por clave guardado en la base: la primera copia lo escribe y cada copia que arranca lo
  recalcula; si no coincide, falla readiness con un log claro. Sin descifrar datos ni llamar al proveedor. Chico a mediano.

#### I18. Una copia vieja que sigue viva más de un día borra los archivos de los tipos nuevos

- **Dónde falla:** con varias copias, cuando una revisión vieja sigue activa más de un día; con una sola, al volver a la imagen
  anterior por más de una hora. Severidad baja: hoy no se nota, el patrón queda latente.
- **Escenario:** se despliega la versión que trae los documentos de Inicio y una revisión vieja sigue activa con sus workers.
  Su limpieza horaria borra todo lo que tiene más de un día y ninguna de sus fuentes reclama, así que borra las copias de
  `ai/assistant/documents/*`, aunque la regla diga que viven mientras un turno las tenga. Hoy nadie lo nota, porque sólo se
  leen durante la lectura; pero con el próximo tipo de archivo que se vuelva a leer más tarde, como ya pasa con el audio y con
  la planilla, la persona perdería la grabación o la importación.
- **Evidencia:** el barrido (`PrivateFileCleanupService.cs:16-34`, cada hora y en cada copia,
  `PrivateFileCleanupBackgroundService.cs:11-18`); las copias de IA no tienen fila de archivo y sólo las protege su fuente
  (`AiAssistantDocumentRetentionSource.cs:12-21`, registrada en `ArtificialIntelligenceApplicationRegistration.cs:26`). Agrega a
  N28 la diferencia de versiones, que la lease de N28 no evita.
- **Arreglo:** que el barrido borre sólo bajo los prefijos que alguien declaró como propios y deje, con un log, lo de un prefijo
  desconocido: la diferencia de versiones pasa a conservar de más. Chico, en el núcleo.

Los límites de pedidos y los semáforos de la prueba y de lugares son de cada copia: ya es H26, y la regla lo acepta
(`inteligencia-artificial.md:47`).

### A gran escala

#### I19. Todas las escrituras del asistente hacen fila en un candado de toda la instalación, y las más frecuentes en dos

- **Dónde falla:** a gran escala; hoy y con varias copias no limita, porque antes frenan los topes de 50 pendientes y 4 en
  curso. Severidad media, alta a gran escala.
- **Escenario:** en un pico de cientos de pedidos por segundo, cada aceptación, cancelación y borrado, cada claim y cada paso
  del worker, la reconciliación de cada copia y cada lote de la purga hacen fila en el mismo candado de la instalación, y la
  aceptación, el claim y los pasos, también en el de la conexión. Mientras los tienen, autorizan a la persona (sus permisos
  piden otra conexión a la base, N33), descifran, cuentan y arman el catálogo. El techo queda en unas decenas de escrituras
  por segundo para toda la instalación, tenga una copia o diez; cuando se acaban las conexiones a la base, los pedidos se
  esperan entre sí, y si un paso espera el candado más de cinco segundos, una respuesta ya pagada no se llega a guardar y el
  turno termina incierto. Los
  candados por persona y por conversación no suman, porque siempre se toman después.
- **Evidencia:** las claves de la instalación (`AiAdvisoryLockKeys.cs:4-8`), exclusivas hasta el fin de la transacción
  (`AdvisoryLockExtensions.cs:34-38`); la aceptación (`AiAssistantSubmissionPolicy.cs:82-85`, con todo lo que sigue hasta
  `:195`), la cancelación y el borrado (`AiAssistantSubmissionPolicy.cs:25-41`, `AiAssistantService.cs:40-54,64-78`), el claim y
  los pasos (`AiAssistantDispatchService.cs:25-38,57,88,113,190`, `AiAssistantDispatchGuard.cs:25-56`) y la retención
  (`AiAssistantRetentionService.cs:19-24,32`); los permisos con otra conexión (`PermissionService.cs:17-27,46,71-75`); guardar una
  respuesta reintenta como mucho cinco segundos (`AiAssistantCoordinator.cs:92-95,172-191`, `AiAssistantOptions.cs:41`). El
  sondeo vacío toma los dos candados cada segundo por carril, y el claim cuenta todos los recibos del día para usar sólo los
  que están en curso (`AiAssistantRepository.cs:55-70`). Los tests prueban la corrección entre dos hosts, no el rendimiento
  (`AiAssistantConcurrencyTests.cs:19-90`). La coordinación en PostgreSQL es una decisión (`inteligencia-artificial.md:217`,
  ADR 0017); que todo pase por una sola fila, no. Con las decisiones de N9 y N33, los permisos se leerán con una sola
  consulta, sin Redis ni segunda conexión, pero siguen dentro del candado.
- **Arreglo:** autorizar, descifrar y armar el catálogo antes de los candados y, adentro, sólo contar e insertar; la conexión,
  compartida salvo al guardarla; sacar el de presupuesto de los pasos, que no cambian conteos; el claim, con el conteo de lo
  que está en curso y mirando primero si hay un pendiente; y, para destrabar capacidad, contadores por día en lugar de contar
  recibos. Mediano; no destraba capacidad mientras los cuatro en curso sigan fijos, pero acorta el tiempo bajo el candado,
  que es lo que deja sin guardar una respuesta pagada.

#### I20. La limpieza, la medición y cada apertura de Inicio recorren toda la historia

- **Dónde falla:** a gran escala, a la que se llega también porque la metadata de Inicio no vence (I3): 200 personas con cinco
  consultas por día suman un millón de turnos en unos cuatro años. Hoy aguanta. Severidad media, alta con millones de filas.
- **Escenario:**
  - cada hora, en cada copia, la purga busca qué limpiar con una condición que ningún índice resuelve («todavía tiene
    contenido y, además, venció, su conversación se retiró o su dueño se borró»), recorre toda la historia aunque no haya nada
    que limpiar y por
    cada lote hace hasta unas 500 consultas sueltas, con el candado de la instalación tomado (I19): mientras dura, el
    asistente no acepta ni avanza nada;
  - cada 30 segundos, cada copia calcula el contenido vencido más viejo con la misma condición y lee la tabla entera;
  - cada vez que alguien abre Inicio o vuelve a su pestaña, el bootstrap corre esa misma medición para usar sólo la cantidad
    de pendientes: mil aperturas a las 9 son mil recorridos de la tabla;
  - la retención de fotos y PDF busca por un dato sin índice y recorre los turnos vivos de todos los canales, no sólo los de
    documentos como dicen el comentario y la regla.
- **Evidencia:** `AiAssistantRepository.cs:82-86,91-100`; el lote en `AiAssistantRetentionService.cs:32-56`; las frecuencias en
  `AiAssistantRetentionBackgroundService.cs:17,33-44,56-69`; la medición en `AiAssistantReader.cs:25-41` (`:35-39`); los índices
  en `AiAssistantTurnConfiguration.cs:27-37` (el de vencimiento no es parcial); el bootstrap (`AiAssistantBootstrapPolicy.cs:32-36`;
  `front:src/features/artificial-intelligence/pages/AiAssistantPage.tsx:213-224`, con `staleTime: 0` y el foco de la pestaña
  prendido, `front:src/shared/api/queryClient.ts:24-27`); la retención de documentos (`AiAssistantReader.cs:80-89`,
  `inteligencia-artificial.md:207`). `AiAssistantRetentionQueryTests.cs:13-29` sólo mira el texto del SQL.
- **Arreglo:** índices parciales sobre lo que tiene contenido y sobre lo retirado, y la selección partida en consultas que los
  usen; las herramientas y los dueños del lote en una consulta; la purga sin el candado de presupuesto, que no cambia conteos;
  el bootstrap con un conteo de pendientes; un índice por canal y pedido para la retención de documentos, con el comentario y
  la regla corregidos. Chico cada uno. El plazo de la metadata de Inicio es la decisión de I3.

### Observaciones menores

- **O1.** El registro de los proveedores del asistente corre dos veces (`ArtificialIntelligenceInfrastructureRegistration.cs:35,60`);
  es idempotente: sólo limpieza.
- **O2.** El catálogo de OpenAI llega en una sola página que tiene que caber en 64 KiB (`OpenAiResponsesClient.cs:11-15`,
  `AiTransportOptions.cs:6`): una cuenta con muchos modelos propios vería «respuesta inválida» y tendría que escribir el
  modelo a mano. Falta probarlo con una cuenta real.
- **O3.** Una zona horaria que el servidor no conoce da el aviso general del horario, sin señalar la zona
  (`AiBusinessDetails.cs:71`).
- **O4.** Un corte de red antes de enviar «Probar conexión» se informa como «podría haberse cobrado»
  (`LlmHttpTransport.cs:59-61`), como en I6.
- **O5.** Conexión e Información del negocio guardan comprobantes con HMAC durante 30 días, más de lo que pide el ADR 0018
  para configuración interna (son anteriores a él); en Información del negocio sólo agrega depender del anillo, y con una
  clave retirada el reintento de un comprobante da 500 en vez de 409 (`HmacAiChangeFingerprint.cs:21`). Simplificarlo es
  opcional y no hace falta decidirlo ahora.
- **O6.** La ventana de seis envíos por minuto es una sola para turnos, planillas, audios y documentos
  (`AiAssistantController.cs:65,74,92,122`): un mensaje con foto y audio gasta tres. Calibrarla es del piloto.
- **O7.** `file-turns` abre el archivo antes de mirar su tamaño (`AiAssistantFileTurnService.cs:44` antes de `:48`); el audio y
  los documentos lo miran antes.
- **O8.** El nombre del archivo quita los controles pero no los bidireccionales, como U+202E
  (`AiAssistantHomeAttachmentPolicy.cs:53-61`); sólo lo ven su dueño y el modelo.
- **O9.** Sólo la ruta de documentos tiene test de su límite de cuerpo (`AiAssistantHomeDocumentTests.cs:270-277`).
- **O10.** La pantalla «Asistente» de WhatsApp muestra los hechos del negocio sin `ai.business.read`
  (`WhatsAppAssistantAgentService.cs:64-68`): son los que el asistente le dice a cualquier cliente, pero el comentario de
  `AiBusinessKnowledgeSource.cs:8-15` promete un control que deja al llamador.
- **O11.** El aviso de cierre es un solo puerto, que WhatsApp reemplaza (`WhatsAppAssistantOperationCompletionSignal.cs:10`): un
  segundo canal pisaría al primero, y las extracciones de Formularios no reciben aviso.
- **O12.** No se fuerza el uso de herramientas: si el modelo contesta con texto en vez de usar la terminal, la consulta da
  `InvalidOutput` (`OpenAiConversationClient.cs:30-35`, `AnthropicConversationClient.cs:31`); lo mitiga la calificación del
  perfil.
- **O13.** A confirmar con una prueba real: un paso tiene 30 segundos para hasta 4.096 tokens de salida, y una lectura 60 para
  8.192 (`AiAssistantLimits.cs:19-24,39-41`); con un modelo que escriba a menos de unos 140 tokens por segundo, la propuesta más
  larga vence.
- **O14.** `KnownInstructions` acepta «la 1 o la actual» (`AiAssistantChannelPolicy.cs:61-62,86-91`): si alguien sube las
  instrucciones de Inicio a la 3 sin anotar la 2, el historial con la 2 deja de leerse.

## Separar el módulo: inventario, no defecto

Si IA pasara a ser un servicio aparte con su propia base, habría que resolver:

- **Capacidades en proceso:** el motor ejecuta las capacidades de Formularios, WhatsApp y el núcleo en su mismo proceso, con
  su contexto de base (`ScopedAssistantCapabilityExecutor.cs:15-20`); cada llamada pasaría a ser remota (los contratos ya son
  JSON con esquema, lo que ayuda).
- **Propuestas tipadas del dueño:** vuelven a su módulo como objetos de ese módulo (`AssistantCapabilityModels.cs:110-114`).
- **Un catálogo que exige todas las capacidades del canal:** el motor rechaza el contexto entero si falta una
  (`AiAssistantCapabilityCatalogPolicy.cs:53-56`); ya es B5 del núcleo, por el que sin Formularios WhatsApp se queda sin
  asistente.
- **Lecturas de `Users` del núcleo** en dos consultas (`AiAssistantReader.cs:36-38`, `AiAssistantRepository.cs:85`),
  permitidas.
- **Disponibilidad leída en la transacción de otro:** WhatsApp pregunta si hay IA dentro de su cambio de modo
  (`WhatsAppChatModeService.cs:33`).
- **Avisos y limpieza en proceso:** el aviso de cierre (`DependencyInjection.cs:39`, `WhatsAppApplicationRegistration.cs:33`) y
  la limpieza de archivos privados del núcleo, que le pregunta a IA qué conservar y comparte el contenedor (`ai/assistant/*`).
- **Permisos del núcleo** por `IPermissionService`.

No hay guardados compartidos: `Submit` y `Cancel` abren su propio límite y ningún canal los llama dentro del suyo
(`AiAssistantOperations.cs:29,36`, `WhatsAppInboundProcessor.cs:314-331`, `FormDocumentAuthoringService.cs:43-45`).
`ModuleBoundaryTests` cuida los nombres y el contexto (`ModuleBoundaryTests.cs:38-53,124-132`). Separar el módulo pediría un
ADR nuevo.

## Costos aceptados por decisiones documentadas

- **Los topes del piloto:** cuatro consultas en curso y 50 pendientes para toda la instalación, y cuotas por persona y por
  instalación, sin calibrar (`configurar-ia.md:196-210`, ADR 0017); subirlos pide revisar código y pruebas
  (`configurar-ia.md:215-216`). Lo que agregan sobre eso es I1.
- **El día de las cuotas es el día UTC:** en Argentina se renuevan a las 21 (`configurar-ia.md:198`). La regla funcional no lo
  dice y la pantalla muestra una cuenta regresiva sin la hora.
- **Sin precios:** el gasto se limita en la cuenta del proveedor (`configurar-ia.md:5,218-219`, `inteligencia-artificial.md:47,213`);
  las cuotas no tienen techo en la validación (`AiAssistantOptions.cs:33,36`). El máximo diario se puede calcular en tokens:
  con las cuotas por defecto, unos 22 millones de entrada y 2 de salida, más 50 lecturas.
- **Los dos costos, escritos el 2026-11-15 en 02a474bc (front edd82618):** la regla dice que la cuota corta en el día UTC,
  Inicio muestra con el error de cupo la hora local en que se renueva y configurar IA escribe el máximo diario en tokens.
- **Los límites por copia:** los de pedidos y los semáforos de la prueba y de lugares (`inteligencia-artificial.md:47`; H26).
- **Sin la clave histórica,** la clave del proveedor, los comprobantes y el contenido quedan ilegibles, sin regeneración
  (`inteligencia-artificial.md:39,101`).
- **Los comprobantes vencidos de la configuración** quedan hasta el guardado siguiente (`inteligencia-artificial.md:35`); no
  tienen la clave ni los hechos.
- **Lo que no sobrevive a un reinicio:** un turno en curso en una copia que se cayó queda incierto si ya había reservado un
  paso, o vencido si no, y pide un intento nuevo (ADR 0017, `inteligencia-artificial.md:215`).
- **La lectura del archivo dentro del pedido,** para rechazarlo enseguida (`inteligencia-artificial.md:197,203`); su costo con
  concurrencia es I9.
- **El turno que nombra una lectura** no vuelve a revisar si la persona todavía puede adjuntar documentos
  (`inteligencia-artificial.md:207`).
- **La prueba de conexión y el catálogo** esperan al proveedor dentro del pedido, con plazo (`inteligencia-artificial.md:27,43`).

## Lo que está bien

- **El recorrido durable del motor.** Un turno se toma con fence, intento y plazo; cada paso se reserva antes del HTTP; una
  respuesta ya obtenida sólo se vuelve a guardar, con el mismo fence; lo incierto queda aparte de lo vencido
  (`AiAssistantCoordinator.cs:62-100,172-192`, `AiAssistantTurn.cs:105-191`). Lo prueban dos hosts: un claim por turno, la
  capacidad compartida y una cancelación que le gana a un worker tardío (`AiAssistantConcurrencyTests.cs:18-49,71-97,99-119`).
- **Ningún POST facturable se repite.** Los clientes del asistente y de la configuración quitan la resiliencia por defecto y
  no siguen redirecciones, probado con `AddServiceDefaults` (`AiAssistantProviderContractTests.cs:340-357`,
  `AiAudioTranscriptionProviderContractTests.cs:286-299`, `AiDocumentReadingProviderContractTests.cs:383-397`,
  `AiProviderContractTests.cs:109-129`): N18 no alcanza a IA.
- **La IA no puede escribir.** Las capacidades sólo leen o proponen y no reciben escritores ni transportes
  (`AiAssistantCapabilityBoundaryTests.cs:20-77`); la ejecución no guarda (`AiAssistantBoundaryTests.cs:35-44`); la terminal
  sólo elige propuestas observadas en el turno (`AiAssistantTerminalPolicy.cs:55-61`) y la respuesta sólo cita evidencias
  observadas (`AiAssistantEvidenceVerifier.cs:13-20`).
- **Instrucciones plantadas, acotadas.** Los resultados viajan como salida de función, los datos van rotulados como datos, la
  salida tiene esquema cerrado y el front muestra texto plano (`OpenAiConversationClient.cs:62`,
  `AnthropicConversationClient.cs:61`, `front:src/shared/ui/assistant/AssistantMessage.tsx:33-41`): lo peor que logra una
  instrucción plantada es una respuesta engañosa o una propuesta que una persona tiene que confirmar.
- **Permisos en cada momento.** Al ofrecer, en cada llamada, al tomar, antes de cada paso y al publicar
  (`AiAssistantCapabilityCatalogPolicy.cs:64-76`, `AiAssistantExecutionService.cs:217-218`, `AiAssistantDispatchGuard.cs:33-56`,
  `AiAssistantOperations.cs:48-50`); el actor de un turno automático de WhatsApp es la propia persona, con sus permisos
  (`WhatsAppAssistantAccessPolicy.cs:44-46`), y nadie presta permisos (`AiAssistantOperationsTests.cs:202-226`).
- **Deduplicación y cuotas entre copias.** Recibo con huella HMAC, 409 si el cuerpo cambia y recuperación antes de mirar la
  conexión o la cuota (`AiAssistantSubmissionPolicy.cs:87-98`); el recibo no depende del contenido, así que borrar no devuelve
  cupo (`AiAssistantConcurrencyTests.cs:19-69`).
- **Cifrado con contexto.** El contenido se ata a instalación, clave, dueño, conversación, turno, propósito y revisión
  (`AesGcmAiAssistantContentProtector.cs:10-53`, `AiAssistantProtectionTests.cs:25-40`); la clave del proveedor no se descifra
  para otro destino ni se devuelve (`AesGcmAiSecretProtector.cs:47`, `AiSecretProtectionTests.cs:39-46`,
  `AiConnectionEndpointsTests.cs:147-149,200-202`).
- **La configuración, idempotente.** Candado, comprobante por operador y pedido y revisión, probados con dos hosts y con
  rollback (`AiConnectionEndpointsTests.cs:125-185`).
- **Cada uno lo suyo.** Todo filtra por dueño y lo ajeno da 404, también desde otra copia, y se autoriza antes de descifrar
  (`AiAssistantConversationHistoryTests.cs:19-82`, `AiAssistantHomeAudioTests.cs:86-96`, `AiAssistantHomeDocumentTests.cs:123-133`);
  las copias se escriben una sola vez y se releen (`AiAssistantHomeAudioPolicy.cs:37-42`).
- **El historial, sin N+1.** Dos comandos por página (`AiAssistantConversationHistoryTests.cs:85-114`).
- **Migraciones cuidadosas.** Aditivas, con un `Down` que se niega a romper trabajo activo
  (`20261111233120_AddAssistantDocumentReading.cs`, `AiAssistantDocumentReadingMigrationTests.cs:26-90`).
- **Transporte defensivo.** Topes de contexto y de respuesta, JSON acotado y cuerpos de error nunca leídos
  (`LlmConversationHttpTransport.cs:33-60,76-90`); el catálogo de modelos nunca se muestra parcial
  (`AnthropicMessagesClient.cs:15-28`).
- **Contrato cerrado con las capacidades de otros módulos.** Esquemas cerrados, presupuestos e ids únicos al componer, y bytes,
  ítems, plazo y codec en cada llamada (`AiAssistantCapabilityCatalogPolicy.cs:85-109`, `AiAssistantExecutionService.cs:219-273`).
- **El front.** La clave candidata no entra en caché
  (`front:src/features/artificial-intelligence/pages/AiSettingsPage.test.tsx:239-251`); un guardado o una subida inciertos se
  repiten con el mismo cuerpo e identificador (`:288-298`, `front:src/features/artificial-intelligence/pages/AiAssistantPage.test.tsx:1621-1655`);
  y los números salen con la región del idioma (`front:src/shared/i18n/index.ts:33-56`).

## Controles y pruebas que conviene sumar

- **Controles automáticos, para que no vuelva:**
  - ningún cliente HTTP de un proveedor facturable tiene resiliencia ni sigue redirecciones, recorriendo todos los
    registrados: hoy hay un test por cliente, y los de Anthropic y Places de la configuración no lo tienen;
  - nada externo bajo los candados de la instalación: permisos, catálogo y descifrado, antes de tomarlos (I19);
  - las consultas de la purga, la medición, el bootstrap y la retención no recorren la tabla de turnos, con `EXPLAIN` sobre
    miles de turnos purgados (I20);
  - un turno con un valor del sello que la versión no conoce sigue pendiente después del claim, y su consulta no da «no
    existe» (I4);
  - una capacidad de lectura que lanza una excepción cualquiera queda «no disponible», nunca `ProviderUnavailable` (I5);
  - cada worker del módulo usa lease o figura en una lista de «idempotente bajo candado» con su motivo;
  - las claves de instrucciones que usa el código existen (B4);
  - las tres rutas de subida declaran su límite de cuerpo (O9), y cada fuente de retención declara sus prefijos (I18);
  - el catálogo que arma cada canal, con el DI real, no pasa de dieciséis capacidades: varios contextos de WhatsApp ya están
    justo en el tope (`WhatsAppAssistantAccessPolicy.cs:53-55,111-115`);
  - ningún error que un administrador puede arreglar se declara como falla interna, o el front tiene un texto para cada `Ai.*`
    (I7).
- **Pruebas que faltan:**
  - una tanda de pedidos de duración fija no deja aceptados vencidos sin intentar, o no cuentan en la cuota (I1);
  - borrar una conversación con foto y audio purga su lectura y su transcripción (I2);
  - una cuenta borrada tres meses después de su último uso queda sin conversaciones (I3);
  - una fila ilegible deja ver las demás (I8);
  - la última propuesta entra con el presupuesto lleno (B1);
  - dos subidas superpuestas del mismo audio terminan en una transcripción (I13);
  - con el cupo agotado, ninguna subida lee ni guarda (I14);
  - apagar el host durante un paso, que termina, y sin paso, que vuelve a pendiente (I12);
  - una copia con otro `InstallationId` no pasa readiness (I17);
  - un cuerpo exportado con un campo de más se sigue leyendo en el front (I11).
- **Pruebas reales:**
  - carga del asistente midiendo la espera de los candados y el pool de conexiones (I19);
  - la purga y la medición con millones de turnos, con `EXPLAIN ANALYZE` (I20);
  - cuarenta subidas de 10 MiB contra una copia del tamaño del despliegue (I9);
  - el tiempo de la propuesta más larga y de la lectura de diez páginas con el perfil calificado (O13);
  - el catálogo de OpenAI con una cuenta real (O2).

## Límites de esta revisión

- **Alcance.** Es de solo lectura sobre `a9f57202` y `7b95095b`, y no incluye los cambios sin commitear de ese día ni lo que
  entró después (`42d8f47f`).
- **Sin ejecución.** No se corrió nada: los tiempos, los techos y las frecuencias salen del código y se confirman con las
  pruebas de la sección anterior.
- **Despliegue.** El despliegue a Azure todavía no está activado: lo de varias copias es diseño, no algo observado.
- **Formularios.** Su lado de los bordes lo mira su revisión, que corría en paralelo; lo que se vio desde acá (el 404 que
  guarda como definitivo, el contexto rearmado con las versiones actuales, las extracciones trabadas sin IA y el lector dentro
  del pedido) se le pasó como dato, y acá se cita en I4, I9 e I16.
- **Front.** Se leyó en `7b95095b`, sólo donde hacía falta para un criterio.

## Decisiones del 2026-11-14

En las ocho elegiste la opción recomendada. Las reglas escritas cambian con su arreglo, no antes: I1, I2, I3 y el corte de la
cuota por día UTC, en [inteligencia artificial](../features/inteligencia-artificial.md), y «borrar no libera la cuota»
también en [configurar IA](../guides/configurar-ia.md); I4 suma al motor la regla de los dos despliegues que `despliegue.md`
ya tiene; I11 cambia la regla de zod estricto del front de IA (`front:src/features/artificial-intelligence/AGENTS.md`); I15
es una versión nueva de las instrucciones, que se califica; y el máximo diario en tokens va a
[configurar IA](../guides/configurar-ia.md).

- **Horizonte: «Hoy», más I17.** El plan lleva I1 a I16 y B1 a B5, y también I17: el valor de control del anillo es barato y
  evita una pérdida definitiva aunque haya una sola réplica, porque alcanza una revisión nueva con otro anillo.
  - I18, antes de pasar de una réplica (hoy `--max-replicas 1`, en [desplegar a Azure](../deploy/azure-setup.md)).
  - De gran escala entra sólo lo trivial que no cambia contratos: el conteo de pendientes del bootstrap y los índices
    parciales de I20, y el conteo del claim de I19.
  - El resto de I19 e I20, cuando haya volumen real que medir.
- **I1: lo que vence sin intentarse devuelve el cupo.** Su recibo sigue sirviendo para deduplicar, pero no cuenta en la
  cuota, y el tope de pendientes se calibra, como dato del piloto, a lo que cuatro carriles alcanzan a empezar antes de que
  venza. Cambia «borrar no libera la cuota» sólo para lo que nunca se intentó. Se descartó sólo calibrar el tope, porque una
  tanda seguiría gastando cupo sin respuesta, y también rechazar por capacidad según la fila estimada, que es mediano.
- **I2 e I3: qué se borra y cuánto vive.**
  - Borrar una conversación de Inicio purga también las lecturas y transcripciones que nombraron sus turnos, y el aviso de
    borrar lo dice.
  - Una lectura o una grabación que nunca llegó a un turno vence a las 24 horas, como una copia huérfana.
  - Una conversación de Inicio se borra como las de los canales, cuando venció todo su contenido y sus recibos: sale del
    panel, donde ya sólo mostraba un título genérico.
  - Las cuentas borradas retiran las suyas aunque ya no tengan contenido, como dice la regla.

  Se descartó sólo avisar que los archivos quedan 30 días y conservar la metadata de Inicio con su costo escrito.
- **I4 e I11: una sola regla de versiones, también en el motor y en Inicio.** La regla de N27 llega al motor: un valor nuevo
  del sello o del proveedor se escribe un despliegue después del que ya lo lee; la consulta devuelve una falla reintentable
  en vez de «no existe»; el worker no toma lo que no entiende; y los módulos aceptan la versión sellada de su capacidad
  mientras el catálogo la conozca. En el front, las respuestas de Inicio se leen tolerando campos desconocidos y lo estricto
  queda en los tests de contrato; extenderlo a todo el front, que tiene 382 esquemas estrictos, se decide con esa regla del
  front. Se descartó sólo anotar la regla de los dos despliegues, que no cubre el trabajo en vuelo al subir una versión ni
  la pestaña abierta.
- **I6: las fallas del proveedor se distinguen.** Clave rechazada, cupo o límite del proveedor y modelo o pedido rechazado,
  cada uno con su texto para quien administra; la falta de conexión, como «no disponible» y no como incierta; y el estado
  HTTP en el log, sin el cuerpo. El corte que falla rápido después de varios rechazos queda para más adelante.
- **I12: el apagado deja terminar el paso en curso.** Al apagarse, la copia deja de tomar turnos y le da al paso en curso un
  plazo para terminar; lo que no reservó un paso vuelve a pendiente en vez de cancelarse. Va con el plazo de apagado de N17.
  Se descartó corregir sólo el texto de «cancelada».
- **I15: el instante en Inicio ya; el formato, con la próxima calificación.** Inicio usa el instante del pedido al armar el
  contexto, que no cambia las instrucciones. El formato de fechas y números y la frase del permiso van en la próxima versión
  de las instrucciones que haya que calificar por otra razón. Se descartó una calificación propia ahora, que cuesta una
  corrida real con el proveedor.
- **Los dos costos se mantienen, escritos.** La cuota sigue contando por día UTC: se escribe en la regla e Inicio muestra la
  hora local en que se renueva. El gasto sigue limitado en la cuenta del proveedor, y la guía escribe el máximo diario en
  tokens. Se descartó contar el día en la zona del negocio, que es más trabajo por los cambios de horario, y un tope de
  tokens propio.

## Cómo seguir

- **Tus decisiones:** tomadas el 2026-11-14 ([decisiones](#decisiones-del-2026-11-14)).
- **El plan:** anexado el 2026-11-14 al del asistente de Inicio, como
  [arreglos de la revisión de arquitectura](../plans/2026-10-04-panel-conversaciones-inicio.md#arreglos-de-la-revisión-de-arquitectura):
  29 tareas en siete etapas, sin programar; las reglas que cambien van a su fuente con cada arreglo. I18 va antes de pasar
  de una réplica; el resto de gran escala, cuando haya volumen real que medir.
- **Para otras sesiones:** I4 extiende al motor la regla de N27, que `42d8f47f` aplicó a la cola durable y a WhatsApp; lo que
  se vio del lado de Formularios quedó para su revisión.
- **Lo que ya estaba anotado:** la traza de WhatsApp (H19), que I10 extiende al motor; el plazo de apagado (N17), que necesita
  I12; y la exportación de telemetría (N10).
