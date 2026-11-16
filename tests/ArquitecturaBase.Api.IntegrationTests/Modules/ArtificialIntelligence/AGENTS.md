# Inteligencia artificial: pruebas

Docker real para transacciones, auditoría, permisos, comprobantes y concurrencia entre hosts. AiTestModule aporta claves inventadas y reemplaza ILlmClient (catálogo/prueba); sus HTTP nombrados fallan si intentan red. Contratos HTTP de proveedores usan handlers ficticios, incluyendo GET autenticado, paginación completa, JSON/cursor inválidos, deadline y slots compartidos. Places usa doble HTTP y bloqueo de red en el factory; cubre permisos, validación, persistencia de PlaceId y huellas históricas. Criptografía usa claves inventadas. Nunca llamar API facturable; no confundir doble con prueba real.

El asistente prueba deduplicación, claim y capacidad desde hosts independientes con PostgreSQL;
los recibos conservan cuotas tras retirada/purga. Recovery usa fuentes de usuarios reales de la
base del arnés y un proveedor ficticio, falla una confirmación y verifica que sólo se repite
persistencia. El reloj de retry se despierta explícitamente con FakeTimeProvider. Retención
comprueba filtros propios, payloads retirados, purga física de metadata vencida con recibos
independientes, también de Inicio sin retirada, la cuenta borrada que retira las suyas aun sin contenido mientras una viva conserva la suya (I3) y
Running incierto sin regeneración. Workload cuenta aceptaciones confirmadas
una sola vez aunque se repita el UUID o se retire su conversación. ChannelQuota recorre con el reloj falso una tanda de turnos
de 80 s en cuatro carriles: los que vencen sin intentarse devuelven la cuota y siguen deduplicando, el que reservó un paso y
quedó incierto la conserva, y lo devuelto vuelve a aceptar (I1).
Migración prueba Up/Down desde el esquema previo y conserva conexión y hechos del negocio.
Los helpers/dobles nuevos viven en TestFeatures/Modules/ArtificialIntelligence.
AiAssistantHttpContractTests recorre Inicio con AiAssistantExecutionScenario y el proveedor
ficticio (bootstrap con `attachments`, `documents` y `documentReading`, 202, pending, completed, página de turnos, canceled y un turno con planilla,
con su 202 y el turno con `attachment`; la lectura de un PDF y los turnos con ese PDF y con un Word), fija `state` y
`stage` en minúsculas y deja los once cuerpos para la cohorte ai-assistant-http del exportador. Para los archivos usa
HomeAudioFiles, HomeAttachmentReader y DocumentReadingHandler.

ConversationHistoryTests comprueba título común de list/detail, propietario distinto, retirada,
cifrado corrupto (esa fila `unavailable` en listado, detalle, página y turno, las demás visibles y
el borrado, I8), bloqueo y vencimiento sin cambiar el reloj compartido del factory hacia atrás.
AssistantReadCommandRecorder verifica las dos consultas de una página, sin respuestas/tools/N+1
ni ordenamiento global con ROW_NUMBER. Es instrumentación de regresión, no un ensayo de capacidad.
AiAssistantRetentionPlanTests (I20) siembra miles de turnos purgados y pide el `EXPLAIN` de lo que corren la purga, la medición y las conversaciones a borrar (`Statements` del recorder, con sus parámetros) con `enable_seqscan` apagado, para no depender de las estadísticas de la base compartida: ningún plan lee en secuencia los turnos ni los recibos, y se usan los índices nuevos (sin ellos pasa por otros). Lo que eligen lo prueban los tests de retención (topes chicos, un turno por motivo) y de borrado.

Hereda las instrucciones del proyecto. Su fuente funcional es [inteligencia artificial](../../../../docs/features/inteligencia-artificial.md); operación y seguridad, [configurar IA](../../../../docs/guides/configurar-ia.md); independencia, [quitar IA](../../../../docs/guides/quitar-ia.md). No importar otros módulos. Actualizar mapa y fuentes con un cambio de responsabilidad.

Los escenarios del worker real aceptan por POST: el servicio sella el perfil Home y las cinco capacidades autorizadas antes del claim. No completar perfiles históricos desde la configuración actual para hacer ejecutables los seeds de retención o reconciliación. ProposalPersistence usa el motor y scopes nuevos para comprobar el perfil sellado, resultados protegidos y codec de capacidad/versión exactos, y que el lookup encuentra las propuestas de la conversación propia de un canal con su procedencia sellada: la continuación hereda la planilla, cuyo descriptor vuelve de PostgreSQL, y su propuesta reemplaza a la primera; el fixture de ProposalCoordinator sólo prueba la coordinación con servicios doblados y no acredita ese recorrido productivo.

Los tests técnicos AudioTranscriptionProvider/AudioProviderDispatch/AudioOptions/AudioCoordinator no usan ApiFactory, Collection, Docker ni proveedores reales: HTTP en memoria, bytes ficticios para protocolo, private store doblado y reloj falso. Verifican multipart, límites, cancelación/incertidumbre, no retries, hash exacto y el mismo cierre independiente del coordinador. No califican OGG/Opus ni calidad de transcripción: eso se comprueba por el motor real al cierre.

Apagado (I12): `AiAssistantCoordinatorShutdownTests`, técnico con reloj falso, el paso que termina, el turno sin paso devuelto, el corte del vaciado y el worker que vacía según `HostOptions`; `AiAssistantShutdownTests`, con PostgreSQL, el turno que otra copia completa y el incierto con `Interrupted`.

`AiAssistantProviderContractTests.Preloads` (Casos del asistente) fija, en los dos proveedores, las precargas como el primer mensaje del usuario y la lista cerrada de `assistant_proposal@2`.

ConversationScopeQuery verifica traducción SQL de scope explícito/null y modelo del índice con nulls no distintos, sin ApiFactory ni Docker. Las migraciones conservan contenido/sellos y rechazan bajar de versión si varios ámbitos activos no caben en el índice anterior; no cerrar ni borrar trabajo para hacer posible el Down.

AiAssistantHomeAudioTests recorre IA4 con PostgreSQL: el bootstrap con audio, bytes que no son audio rechazados, la subida, el reintento (la misma grabación, 202; otra, 409), la normalización con el cliente STT ficticio (también consultada en inglés, B2), el turno dictado con la transcripción del servidor, la grabación sólo para su persona y la retención. HomeAudioFiles es almacenamiento privado en memoria con WriteOnce; no acredita el almacenamiento real.

AiAssistantHomeAttachmentTests recorre el turno con planilla con PostgreSQL: `attachments` del bootstrap por persona (con el permiso del dueño, sin él y sin almacenamiento), el 403 de quien no puede adjuntar, el 400 por motivo (cada uno con su código, salvo `TooManyRows`, que sólo da la carga de filas de un canal) y el del tope, el 202, el reintento, el bloque que recibe el proveedor, la pregunta siguiente que la hereda, la página de turnos, un Excel que reemplaza la planilla en la misma conversación, el título, la retención (con la propia purgada y la conversación retirada) y audio más planilla. HomeAttachmentReader es el puerto del dueño, ficticio; no acredita la lectura de planillas reales.

AiAssistantHomeDocumentTests recorre la Etapa 3 con PostgreSQL: el bootstrap con `documents` y `documentReading` por persona, un cuerpo del tamaño de un documento que llega al servicio, los rechazos de la subida (403, un PDF con contraseña o de más páginas, una planilla) sin copia, el reintento (otro archivo, 409), la lectura con el cliente ficticio, el estado sólo para su persona (también en inglés, B2), el turno que la nombra (el bloque al final, `attachment` con páginas y el título), la pregunta que la hereda, la retención por la lectura y por los turnos, la lectura fallida que suelta su copia y el Word por file-turns.

AiAssistantHomeRemovalTests (I2) recorre con PostgreSQL el borrado de una conversación con foto y audio: su lectura y su transcripción quedan sin contenido en la base, la consulta ya no las trae, un turno nuevo que las nombra recibe `Expired` y la retención suelta la grabación, mientras otra conversación que tomó la misma foto conserva su copia sellada y la copia hasta que también se borra, y otra que nombró la misma grabación conserva su transcripción, pero la grabación se suelta, y la lectura se purga aunque el perfil sellado del turno no se lea, sin tocar la de otra persona bajo el mismo pedido; y, con un reloj falso propio del escenario, que una lectura y una grabación que ningún turno nombró siguen 24 horas menos un minuto y la purga siguiente a las 24 horas las vacía y suelta la copia, sin tocar la lectura que un turno nombró ni una de WhatsApp de la misma persona; también vacía lo que sólo nombró un turno de una conversación retirada antes de I2, y busca los turnos que nombran una lectura por la clave entera, nunca por un prefijo de la columna.

AiAssistantUploadConcurrencyTests (I9) prueba los lugares de subida con un lector que retiene o falla: sin sesión o sin permiso, 401 o 403 sin ocupar la cola; la segunda subida de una persona, 429 enseguida; con la copia llena, de otras dos personas una espera y la otra recibe 429, como el audio y la planilla, y el turno JSON no; con la ventana de envíos agotada, su 429 con su espera, sin cola; y los lugares vuelven después de un 400, un cuerpo sobre el límite, una excepción del lector y un cuerpo que no llega a tiempo (reloj falso).

AiAssistantHomeCatalogTests cuenta lo que ofrece Inicio con los módulos del host, para una cuenta con todos los permisos: con formularios, 12 definiciones sin planilla, 13 con ella y 12 con un documento (`propose-catalog@2` y `propose-form@2`, nunca la 1, y `propose-import@1` sólo con planilla). Sin el módulo de formularios se omite, como las composiciones de WhatsApp.

AiAssistantUnknownSealTests (I4) sella en PostgreSQL un turno con una terminal futura: el claim toma el siguiente y lo deja pendiente, Get da `UnknownVersion`, y vence sin un intento y devuelve la cuota (con el cupo justo, un pedido más entra recién entonces); vencido, Get da su estado en claro y Cancel no lo cambia.

AiAssistantSealedVersionTests (I4) sella en PostgreSQL una extracción de Formularios y un turno de WhatsApp, con sus ids y sin importar los módulos, con la versión 1 de una capacidad y los recupera con el contexto de una 2 ficticia (`RaisedVersionAssistantSource`): la consulta, el replay, el claim y la cancelación. Un turno de Inicio sellado antes de la suba lo corre la copia de después (otro host) con la versión sellada, y termina.

AiAssistantTraceTests (I10) escucha con un `ActivityListener`: el turno de Inicio guarda el `traceparent` del pedido y su actividad lo continúa con las etiquetas y la llamada al proveedor adentro (`Handler.Traces`); una falla la marca con su código, también la que cierra el motor (el límite de pasos, la conexión que cambia al cerrar); las consultas del sondeo de cancelación, con la llamada retenida y el reloj falso, no entran en la traza del turno (escucha también Npgsql); un contexto guardado ilegible no frena el turno; una operación guarda el de su vuelta y ninguno sin traza; y el log de una falla del proveedor lleva `AssistantTurnId` en el scope (`FakeLoggerProvider`).

AiConnectionUnsupportedProviderTests (I4) guarda en PostgreSQL una conexión y un turno con un proveedor futuro: la pantalla lo muestra sin 500, vacío y pausado con `unsupportedProvider`, el bootstrap y la disponibilidad dan `paused`, guardar con `Keep` lo guarda sin la clave y la prueba con `Keep` da `CredentialRequired`, el claim no lo toma, el turno se sigue leyendo y guardar con OpenAI lo reemplaza.

AiAssistantOperationContinuationTests recorre «Cambiar algo» con el motor real y PostgreSQL: la operación siguiente queda en la conversación de la anterior (ordinal 2) y el proveedor recibe la pregunta y la propuesta anteriores como historia; continuar una operación con turnos posteriores la suma al final (ordinal 3), y después de una cancelada (la que un canal reemplazó) espera mientras ésta está activa, y la historia trae la propuesta anterior sin el pedido cancelado.

Las pruebas de documentos espejan las del audio: el contrato de `OpenAiDocumentReadingClient` con un HTTP ficticio que fija el cuerpo exacto (`input_file` o `input_image`, `store:false` y el esquema, byte a byte igual al que escribía el serializador) y el idioma tal como llega, el ruteo con la copia privada, el coordinador y las opciones; con PostgreSQL, AiAssistantDocumentReadingTests recorre la lectura con el proveedor simulado, el incierto, la copia cambiada, los cupos y el CHECK, y la migración su Up/Down/Up.
