# WhatsApp: arnés de Flow

Dobles de HTTP/puertos con datos ficticios, incluida una fuente `IStructuredFormSource` neutral para selección/envío y puertos capturadores neutrales para probar el webhook/respuesta, sin referencias a BusinessForms ni red Meta real. No registrar en src. Preservar redacción y pruebas de fase/fence/privacidad. Retirar con el módulo.

export-k7-http.ps1 ejecuta la suite completa y, sólo en verde, copia al front cuerpos HTTP sintéticos de preparación y respuesta, con procedencia y hashes automáticos. Meta y el puerto de registros de ese productor son capturadores; la persistencia BusinessForms real y rollback de referencias se prueban por separado.

WhatsAppFormActionScenario prepara base propia, actores y contactos con puertos reales y dobles de transporte/almacenamiento; se retira con el módulo. WhatsAppFormActionContractCapture conserva bytes HTTP intactos sólo con WHATSAPP_FORM_ACTIONS_HTTP_OUTPUT. El exportador central incorpora los hashes de escenario, prueba y capturador en la cohorte whatsapp-form-actions-http.

ModeAssistantOperations devuelve únicamente disponibilidad ficticia para probar permisos HTTP de cambio de modo; no interpreta ni ejecuta operaciones IA.

WhatsAppAudioContractCapture conserva bytes HTTP reales de WhatsAppChatTests.Audio sólo al configurar WHATSAPP_AUDIO_HTTP_OUTPUT; el escenario usa texto sintético explícito y no invoca Meta ni IA. No completar ni reescribir los cuerpos exportados.

WhatsAppAssistantScenario crea una base PostgreSQL aislada, cuentas/teléfonos confirmados, publicación Flow por el canal real y un inbound de negocio procesado. WhatsAppAssistantOperationsDouble sólo encamina estados del contrato neutral junto con WhatsAppAssistantFormSource; no interpreta mensajes ni acredita decisiones IA. WhatsAppAssistantContractCapture escribe bytes HTTP intactos para WHATSAPP_ASSISTANT_HTTP_OUTPUT, sin completar o fabricar JSON.

El escenario Assistant permite modo automático explícito y resultados controlados para el dispatcher real; el doble mantiene Submit prohibido por defecto para las pruebas HTTP manuales. Sólo el caso de indisponibilidad habilita el intento y devuelve el error neutral, sin proveedor. La fuente neutral consulta el permiso actual del destinatario mediante IPermissionService del host y puede retirar valores en ForSending; no interpreta lenguaje ni fabrica cuerpos HTTP.

El AppPreview conjunto usa CreatePreviewAsync sobre la misma base de ApiFactory y conserva el borrado de base sólo para los escenarios aislados habituales. El adaptador neutral prepara una sugerencia Ready controlada, un audio OGG ficticio válido, un contacto Always mediante el capturador compartido, webhook firmado y procesador reales, y un contacto en modo Assistant para H1/H3. Always se prepara con ProposalId null y notInstalled, sin llamadas al modelo; el Hold empieza con disponibilidad available y el manager conserva ConfigureAssistant. HoldAsync mantiene Meta simulado y permite avanzar los efectos del módulo de formularios mediante callback neutral. El fixture languageDefinitionId agrega el recorrido I1–I3 con tres textos secundarios pendientes; el callback avanza publicaciones de esa definición y Hold avanza Flows de sus actores propios. Conserva el login normal por OTP, protocolo de estado, limpieza y duración de 35 minutos. Etapa 5 se habilita exclusivamente con STAGE5_APP_PREVIEW_WEB_ROOT/STATE; FORM_APP_PREVIEW_WEB_ROOT/STATE corresponde al preview anterior y no activa este recorrido. No se ejecuta en los cortes rápidos.

El preview de audio conserva el mensaje Completed y agrega otro Pending con copia privada y clave por MessageId; pendingAudioMessageId se expone mediante el contexto neutral en el archivo privado, sin solicitar ni completar STT.

El estado privado del Hold publica el archivo de control `.availability` y su lista cerrada de estados. Sólo admite available, paused, modelNotQualified, notConfigured y notInstalled, y altera únicamente el doble local. No agrega una API de producto ni cambia la conexión real. Al terminar por `.done` o plazo, retira estado y control; el test restaura el reloj compartido para no afectar la suite completa. Los tests técnicos del selector y los estados no construyen ApiFactory ni requieren Docker.

WhatsAppAssistantMigrationSeed usa SQL del contrato anterior y usuarios/sesión/receipt ficticios propios en una base aislada; omite las columnas de Etapa5 antes y después de Up para observar defaults SQL. No usa entidades actuales para insertar legado, imports BusinessForms ni datos de AppHost.

WhatsAppAssistantCaptureFixture reutiliza CapturingStructuredRecordPort: publica y manda el formulario por los servicios reales, recibe un webhook Flow firmado y procesa la captura. Sus helpers devuelven IDs confirmados, sin tokens. La fuente neutral de C4 verifica permisos y pertenencia de la captura real, y conserva IDs nullable de Always; no interpreta el mensaje ni acredita el modelo. Las tres capturas HTTP Always se agregan al capturador existente, sin completar cuerpos.

El Hold opt-in del navegador publica sólo actores ficticios (email y último código emitido por pedido UI) en su archivo privado, sin tokens ni login previo. CapturingEmailSender conserva el correo real del pipeline de pruebas. El reloj se ajusta al real antes de sembrar contactos y durante el Hold; las expiraciones y validaciones de autenticación no cambian. No imprimir el archivo ni poner credenciales en URLs.

WhatsAppRetentionBatchScenario verifica el scheduler de retención sin base: cola de lotes, transacciones y scopes independientes, sin simular SQL ni declarar integración acreditada.

WhatsAppAudioDispatchProbe reemplaza sólo Meta/archivos privados en el escenario PostgreSQL; el despacho, lector, repositorios, acceso y UoW siguen siendo productivos. Conserva bytes Ogg ficticios y contadores sin llamadas IA ni URLs reales.

FlowDeprecationProbe controla sólo el borde Meta de CapturingFlowProvider: cuenta POST/GET, coordina dos hosts mediante TCS y conserva el estado remoto ficticio aplicado aunque el POST responda incierto. No crea filas, reservas ni respuestas de formularios; los workers, leases/fences, selección y captura de la composición siguen siendo productivos. El comportamiento Published/Unknown permite observar que sólo una consulta confirmada habilita repetir el efecto.

WhatsAppAssistantLatencyScenario prepara publicación por HTTP y workers reales con reloj de setup propio; la medición usa otro host sobre esa base con TimeProvider.System. El handler ficticio de IA conserva el protocolo HTTP y toma referencias sólo de las tools observadas. Cloud captura transportes y timestamps, sin enviar a Meta. La base y los hosts se retiran al terminar; los artefactos contienen sólo duraciones y conteos.

WhatsAppFlowReplyWorkerFixture prueba el worker real con un servicio capturador y FakeTimeProvider; observa la creación del timer antes de enviar avisos o avanzar el tiempo. No construye ApiFactory ni requiere Docker. Conserva cancelación y avisos independientes de entrada y captura; la composición PostgreSQL se prueba con el escenario de latencia.
