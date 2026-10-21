# Inteligencia artificial: módulo opcional

Servicios independientes de conexión y de información del negocio; lectura, cambio, catálogo de modelos y prueba de conexión; helpers CandidatePolicy/ChangeRecorder. PUT usa un único OnSuccess con lock, deduplicación y comprobante atómico. Catálogo/prueba no guardan; comparten política de clave candidata/guardada y revisión, sin enviar la guardada a otro proveedor. Validar antes del límite; logs sin payload. Puertos propios, sin referencias al núcleo hacia este módulo ni a WhatsApp.

Las reglas de casos de uso y del aviso neutral postcommit están en [Services](Services/AGENTS.md).

Información del negocio valida details tipados antes del límite; omitirlos conserva los existentes.
No parsear los textos anteriores ni convertir automáticamente datos no confirmados. La huella
incluye los detalles normalizados cuando están presentes y conserva compatibilidad con pedidos
anteriores. Ubicación, horario y FAQ se guardan junto a los textos, revisión y comprobante.
BusinessPlacesService valida y consulta su puerto sin transacción ni conexión LLM; usa la
cultura de la petición. Sugerencias sólo para selección, sin guardar ni cachear su texto.

Hereda las instrucciones del proyecto. Su fuente funcional es [inteligencia artificial](../../../../docs/features/inteligencia-artificial.md); operación y seguridad, [configurar IA](../../../../docs/guides/configurar-ia.md); independencia, [quitar IA](../../../../docs/guides/quitar-ia.md). No importar otros módulos. Actualizar mapa y fuentes con un cambio de responsabilidad.

El Inicio consume IAiAssistantService; Dispatch y Retention tienen una UoW por método público
de escritura. Execution no guarda ni llama escritores: el coordinador técnico abre scopes nuevos
entre unidades; IAssistantCapabilityExecutor también aísla cada fuente. ReadPolicy autoriza antes de
descifrar, SubmissionPolicy acepta/deduplica bajo locks, ContextPolicy arma historia vigente y
catálogo autorizado y DispatchGuard vuelve a verificar cuenta, permisos y conexión antes de HTTP
y publicación. Los helpers no confirman. Mantener como máximo ocho dependencias por constructor.

El catálogo se reserva conservadoramente en RequiredPermissions antes del paso externo: esto
bloquea historia ante revocación incluso si un worker muere después de leer una fuente y antes
de registrar su resultado. Nunca publicar texto libre: assistant_answer exige schema cerrado e
IDs de evidencia de ese turno. Las filas críticas se proyectan desde evidencia, no del texto LLM.
Historia anterior a un hueco vencido no se envía; no resumirla con otra llamada.
El prompt versión 1 y la descripción terminal viven en Assistant.resx/en y se resuelven
con la cultura capturada del turno. Retención cancela y oculta conversaciones de cuentas
eliminadas, purga contenido y herramientas, y elimina físicamente Inicio sólo con Removed, sin turnos activos ni contenido dentro de su ventana;
los demás canales se eliminan al vencer también sus recibos, sin requerir retirada explícita. Los comprobantes son independientes;
un lote distribuye su límite total entre contenido, conversaciones y comprobantes.
Registrar también resultados de fuentes sin evidencia (por ejemplo notConnected): conservar
estado, CallId, observación UTC y resultado cifrado, sin inventar IDs de evidencia. Una lectura
recuperablemente fallida queda unavailable y permite otras lecturas sin acreditar propuestas ni
ausencia; validación, autorización y cancelación permanecen terminales.

Títulos de historial: ReadPolicy autoriza con permisos actuales; ResponsePolicy comprueba vigencia antes de descifrar la primera pregunta original. ConversationTitlePolicy es puro y limita grafemas. Bloqueo/purga/vencimiento usan genérico; corrupción sigue ContentUnavailable. No duplicar títulos en tablas, caché compartido ni logs. Listado y detalle comparten la política.

El motor único consume IAssistantCapabilitySource y un catálogo de pares ID/versión exactos; sin wrappers legacy. Inicio conserva sus cinco consultas y assistant_answer. Cada turno acepta ChannelId neutral, Actor/Subject explícitos, cultura/zona, perfil e instrucciones versionados y requisitos por identidad. No prestar permisos entre identidades ni exigir AssistantUse a otro canal. ContextPolicy y DispatchGuard revalidan el sello antes de descifrar, reservar cada paso y publicar. La reserva conserva sólo permisos de capacidades efectivamente ofrecidas.

Proposal sólo selecciona una propuesta observada del mismo turno. Sus datos pasan por el codec exacto del dueño; las lecturas autorizantes deben haber sido confirmadas en un paso anterior (no en el mismo batch). El resultado protegido referencia ordinales de tools existentes; no duplica snapshots ni expone observaciones o propuestas por el HTTP de Inicio. Answer y Proposal usan el mismo coordinador y cierre con fence; el decoder identifica el terminal persistido. Las fuentes no tienen escritores, transporte ni ServiceProvider.

AiAssistantOperations vive en Channels como adaptador del puerto real consumido por canales; no es un helper de Services. AiAssistantCapabilityCatalogPolicy decide el catálogo autorizado y mantiene el sufijo Policy. El adaptador ofrece: disponibilidad local sin permisoHome ni LLM, Submit y Cancel con una UoW por método, y Get sin escrituras. Cancel conserva el alcance capturado aunque se hayan revocado permisos. La unicidad activa es por conversación y, para interpretación fuera de Inicio, por persona y canal; NormalizeAudio no reserva ese par ni bloquea una interpretación, y conserva cuotas por actor/canal/propósito y capacidad global; ActiveTurn/CapacityLimited son transitorios para los consumidores. Home y operaciones comparten AcceptCore de SubmissionPolicy, locks/cuotas/recibos/cifrado/worker; no duplicar motores ni aceptación. Huella neutral incluye contexto/origen/expiry y conserva algoritmo Home. Get autoriza contexto durable, no exige conexión/perfil actuales para recuperar resultado conocido; no devuelve contenido vencido. El sello conserva OccurredAtUtc del dueño para interpretar fechas relativas y exponer procedencia real.

Complete conserva primero el uso conocido y verifica fence/vencimiento/autorización antes de guardar nuevas tools protegidas o la respuesta. Un resultado tardío puede conservar metadata de consumo y fallar, sin reponer contenido privado vencido.

NormalizeAudio es un propósito técnico del mismo turno, aceptación, cuotas, claim/fence y cierre: no usa capacidades, historia ni terminales del modelo textual. Sella perfil STT exacto e instrucciones audio-none-v1; su modelo se resuelve desde ese perfil sin cambiar AiConnection.Model. Revalida actor/Subject, proveedor/revisión, perfil, vigencia y clave actual antes de reservar efecto y publicar. El descriptor y la salida se cifran con los propósitos existentes; el resultado completo se valida sin truncar ni traducir. AudioSeconds conserva duración informada incluso al fallar un cierre tardío, sin inventar tokens; el consumo queda en el módulo.

Submission conserva replay por UUID antes de contar cuotas. El cupo del actor se separa por canal y propósito capturados; instalación y capacidad permanecen globales. Claim sólo necesita capacidad y no infiere un propósito para calcularla.

El perfil de texto exige Id explícito; no se infiere home. Al capturar el contexto, el catálogo valida un máximo total de doce herramientas, incluidos los terminales y después del filtrado de permisos, antes de aceptar trabajo.
