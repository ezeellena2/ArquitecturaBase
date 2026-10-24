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

Los escenarios del worker real aceptan por POST: el servicio sella el perfil Home y las cinco capacidades autorizadas antes del claim. No completar perfiles históricos desde la configuración actual para hacer ejecutables los seeds de retención o reconciliación. ProposalPersistence usa el motor y scopes nuevos para comprobar el perfil sellado, resultados protegidos y codec de capacidad/versión exactos; el fixture de ProposalCoordinator sólo prueba la coordinación con servicios doblados y no acredita ese recorrido productivo.

Los tests técnicos AudioTranscriptionProvider/AudioProviderDispatch/AudioOptions/AudioCoordinator no usan ApiFactory, Collection, Docker ni proveedores reales: HTTP en memoria, bytes ficticios para protocolo, private store doblado y reloj falso. Verifican multipart, límites, cancelación/incertidumbre, no retries, hash exacto y el mismo cierre independiente del coordinador. No califican OGG/Opus ni calidad de transcripción: eso se comprueba por el motor real al cierre.

ConversationScopeQuery verifica traducción SQL de scope explícito/null y modelo del índice con nulls no distintos, sin ApiFactory ni Docker. Las migraciones conservan contenido/sellos y rechazan bajar de versión si varios ámbitos activos no caben en el índice anterior; no cerrar ni borrar trabajo para hacer posible el Down.
