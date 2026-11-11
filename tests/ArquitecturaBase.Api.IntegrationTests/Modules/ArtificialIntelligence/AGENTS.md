# Inteligencia artificial: pruebas

Docker real para transacciones, auditoría, permisos, comprobantes y concurrencia entre hosts. AiTestModule aporta claves inventadas y reemplaza ILlmClient (catálogo/prueba); sus HTTP nombrados fallan si intentan red. Contratos HTTP de proveedores usan handlers ficticios, incluyendo GET autenticado, paginación completa, JSON/cursor inválidos, deadline y slots compartidos. Places usa doble HTTP y bloqueo de red en el factory; cubre permisos, validación, persistencia de PlaceId y huellas históricas. Criptografía usa claves inventadas. Nunca llamar API facturable; no confundir doble con prueba real.

El asistente prueba deduplicación, claim y capacidad desde hosts independientes con PostgreSQL;
los recibos conservan cuotas tras retirada/purga. Recovery usa fuentes de usuarios reales de la
base del arnés y un proveedor ficticio, falla una confirmación y verifica que sólo se repite
persistencia. El reloj de retry se despierta explícitamente con FakeTimeProvider. Retención
comprueba filtros propios, payloads retirados, purga física de metadata vencida con recibos
independientes y Running incierto sin regeneración. Workload cuenta aceptaciones confirmadas
una sola vez aunque se repita el UUID o se retire su conversación.
Migración prueba Up/Down desde el esquema previo y conserva conexión y hechos del negocio.
Los helpers/dobles nuevos viven en TestFeatures/Modules/ArtificialIntelligence.
AiAssistantHttpContractTests recorre Inicio con AiAssistantExecutionScenario y el proveedor
ficticio (bootstrap, 202, pending, completed, página de turnos y canceled), fija `state` y
`stage` en minúsculas y deja los seis cuerpos para la cohorte ai-assistant-http del exportador.

ConversationHistoryTests comprueba título común de list/detail, propietario distinto, retirada,
cifrado corrupto, bloqueo y vencimiento sin cambiar el reloj compartido del factory hacia atrás.
AssistantReadCommandRecorder verifica las dos consultas de una página, sin respuestas/tools/N+1
ni ordenamiento global con ROW_NUMBER. Es instrumentación de regresión, no un ensayo de capacidad.

Hereda las instrucciones del proyecto. Su fuente funcional es [inteligencia artificial](../../../../docs/features/inteligencia-artificial.md); operación y seguridad, [configurar IA](../../../../docs/guides/configurar-ia.md); independencia, [quitar IA](../../../../docs/guides/quitar-ia.md). No importar otros módulos. Actualizar mapa y fuentes con un cambio de responsabilidad.

Los escenarios del worker real aceptan por POST: el servicio sella el perfil Home y las cinco capacidades autorizadas antes del claim. No completar perfiles históricos desde la configuración actual para hacer ejecutables los seeds de retención o reconciliación. ProposalPersistence usa el motor y scopes nuevos para comprobar el perfil sellado, resultados protegidos y codec de capacidad/versión exactos, y que el lookup encuentra las propuestas de la conversación propia de un canal con su procedencia sellada: la continuación hereda la planilla, cuyo descriptor vuelve de PostgreSQL, y su propuesta reemplaza a la primera; el fixture de ProposalCoordinator sólo prueba la coordinación con servicios doblados y no acredita ese recorrido productivo.

Los tests técnicos AudioTranscriptionProvider/AudioProviderDispatch/AudioOptions/AudioCoordinator no usan ApiFactory, Collection, Docker ni proveedores reales: HTTP en memoria, bytes ficticios para protocolo, private store doblado y reloj falso. Verifican multipart, límites, cancelación/incertidumbre, no retries, hash exacto y el mismo cierre independiente del coordinador. No califican OGG/Opus ni calidad de transcripción: eso se comprueba por el motor real al cierre.

ConversationScopeQuery verifica traducción SQL de scope explícito/null y modelo del índice con nulls no distintos, sin ApiFactory ni Docker. Las migraciones conservan contenido/sellos y rechazan bajar de versión si varios ámbitos activos no caben en el índice anterior; no cerrar ni borrar trabajo para hacer posible el Down.

AiAssistantHomeAudioTests recorre IA4 con PostgreSQL: el bootstrap con audio, bytes que no son audio rechazados, la subida, la normalización con el cliente STT ficticio, el turno dictado con la transcripción del servidor, la grabación sólo para su persona y la retención. HomeAudioFiles es almacenamiento privado en memoria con WriteOnce; no acredita el almacenamiento real.

AiAssistantHomeAttachmentTests recorre el turno con planilla con PostgreSQL: el 403 de quien no puede adjuntar, el 400 por motivo y el del tope, el 202, el reintento, el bloque que recibe el proveedor, la pregunta siguiente que la hereda, la página de turnos, un Excel que reemplaza la planilla en la misma conversación, el título, la retención (con la propia purgada y la conversación retirada) y audio más planilla. HomeAttachmentReader es el puerto del dueño, ficticio; no acredita la lectura de planillas reales.

AiAssistantHomeCatalogTests cuenta lo que ofrece Inicio con los módulos del host, para una cuenta con todos los permisos: con formularios, 12 definiciones sin planilla y 13 con ella (`propose-catalog@2`, nunca la 1, y `propose-import@1` sólo con planilla). Sin el módulo de formularios se omite, como las composiciones de WhatsApp.

AiAssistantOperationContinuationTests recorre «Cambiar algo» con el motor real y PostgreSQL: la operación siguiente queda en la conversación de la anterior (ordinal 2) y el proveedor recibe la pregunta y la propuesta anteriores como historia; continuar una operación con turnos posteriores la suma al final (ordinal 3), y después de una cancelada (la que un canal reemplazó) espera mientras ésta está activa, y la historia trae la propuesta anterior sin el pedido cancelado.
