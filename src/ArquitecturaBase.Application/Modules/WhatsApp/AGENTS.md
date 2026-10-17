# WhatsApp: coordinación funcional

Agrupa casos de uso, helpers, contratos, modelos, validadores, recursos y adaptadores de canales propios del módulo.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md).
`WhatsAppInboundService` toma el contacto y el único límite; `WhatsAppInboundPolicy` clasifica metadata y selecciona acciones estructuradas; `WhatsAppAccessReplyPolicy` produce orientación pública sin leer identidad. `WhatsAppAccessActions` conserva IDs externos, y los textos viven en Access.resx/es/en. Perfil y registro conservan recorridos explícitos: `WhatsAppCodeIssuer` usa LoginCodeIssuer y la cuota común, con SignInCode o VerifyPhoneCode. Manual no interviene en acceso. El núcleo conserva el canje de enlaces pendientes.

## Al modificar

- ChatService coordina permisos HTTP, lectura propia y salida durable; DispatchService abre un límite separado para reservar/completar/recuperar. La llamada a Meta queda afuera de las transacciones. Las reglas están en «Chat de atención» de la fuente funcional.
- `WhatsAppPermissionCatalogSource` aporta códigos y textos del módulo al puerto genérico; el núcleo no los nombra.

- El procesamiento no crea cuentas, confirma teléfonos, vincula contactos ni emite enlaces secretos. No inferir acceso por frases. Policies y helpers no guardan.
- Los códigos usan el emisor común y una cuota propia por número; conservá propósito y límites aunque el pedido provenga de otro flujo.
- Los adaptadores de `Channels` implementan puertos del núcleo sin mover la implementación al núcleo.
- El módulo se registra en su bloque; no introducir referencias del núcleo a sus clases.

## Verificación y ejemplos

- [Pruebas de coordinación](../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp).
- [Independencia del núcleo](../../../../docs/guides/quitar-whatsapp.md).

- WhatsAppStructuredFormChannel aporta IStructuredFormChannel con ChannelId neutral phone. Consulta pura y disponible sin transporte habilitado; no acredita cuenta ni habilita aceptación.
- WhatsAppFlowCompiler es dueño del JSON 7.3, presupuesto, mapping y hash wire; perfil flow-static-7.3-r7 para compilaciones nuevas; choices con `source.catalog` usan un Dropdown dinámico con datos de pantalla declarados. Proyecciones selladas r3/r4 conservan sus bytes y codec versionado. ValueCodec preserva decimal exacto/string, DateOnly y ausencia distinta de false.
- Boolean usa RadioButtonsGroup Sí/No sin inicial; decimal TextInput text + gramática; fecha CalendarPicker single/required. es/en resuelve cada texto en su cultura exacta; nombre y ayudas presentes también completos, sin fallback ni traducción automática.
- Excesos se bloquean por identidad/ruta; no truncar ni dividir pantallas. Perfil conservador UTF-16, ayuda de Dropdown bloqueada sin variante calificada. Validación local no es upload/publicación/round-trip.

2b: canal también consume servicios propios Accept/Status/Preview. Accept verifica cuenta fuera de su UoW y sella proyecciones por es/en idempotentes. Dispatch reserva, marca el inicio del efecto y completa en UoW propios, con permisos actuales y fences; sólo marca InFlight justo antes del proveedor y libera rechazos seguros sin efecto. Nunca mantiene transporte dentro del límite. Sólo puertos neutrales hacia BusinessForms, IDs opacos sin tipos del otro módulo. Status/preview verifican owner/asset/perfil/health; diagnósticos por mapping, sin mensaje bruto. Revocación conserva IDs/Uncertain existentes.

WhatsAppFormService resuelve sólo vínculo explícito/cuenta/idioma actuales y consulta IStructuredFormSource neutral; no hereda permisos del operador. Revisión determinista de destinatario incluye actor/LinkRevision/estado/idioma/stamps con roles ordenados; no es autorización ni permite identidad/cultura cliente. Filtro sin resultados no altera elegibilidad general. Sin UoW para selección.

WhatsAppFlowReplyService valida token/sesión/proyección y decodifica sólo mediante el mapping sellado y ValueCodec antes de llamar a IStructuredRecordSink. Mensaje y sesión reciben estado final separado; errores de datos quedan terminales, fallas de infraestructura se reintentan. ChatQuery usa IStructuredRecordSource para valores y aplica formrecords.read sin nombrar BusinessForms.

K7: el GET de preparación exige Read+Send y deriva usuario/cultura del vínculo. Devuelve sólo campo, etiqueta, cantidad y problemas seguros; nunca las opciones del destinatario. Al encolar se resuelve de nuevo y el payload protegido fija la oferta; el transporte la lleva en los datos iniciales del Flow. El codec dinámico valida IDs de esa oferta. Un rechazo de opción Manual inactiva conserva sólo el nombre de pregunta; sin número ni valores, y sin reenvío automático.

Cierre de revisión: el adaptador del canal traduce UnsupportedConfiguration/PayloadMismatch del Accept a Failed sin reconciliación; no clasificar los errores de otros módulos por sus códigos. La captura registra un contador saturado en cinco fallas, incluidas respuestas Unavailable del sink; sigue reintentando al menos 15 minutos desde la primera falla persistida y sólo al cumplir ambos límites guarda Rejected/ProcessedAtUtc. Cancelar el worker no cuenta como falla. El rechazo sigue visible sin sesión y no muestra valores ni número.

F2: PhotoPicker/DocumentPicker abren pantalla, con preguntas posteriores en ella; el complete final referencia cada formulario por su pantalla. Nunca mandar archivos en navigate ni init-values. El mapping sellado conserva mínimos/máximos/origen; los diagnósticos usan pantalla + posición, no un índice global inventado. r3–r5 siguen sellados; el tope de instalación se fija al aceptar para que un cambio de configuración no altere una publicación en curso. Metadatos de archivos tienen un presupuesto separado de 64 KiB, sin ampliar el de respuestas escalares.

F3 toma metadatos de arrays del complete, los vincula al mapping sellado y descarga por IWhatsAppMediaClient antes del sink neutral. Sin bytes en Answers ni llamadas externas dentro de UoW. Una lectura no disponible usa el contador/edad existentes y conserva una razón segura; otros fallos de contenido rechazan. La marca HasFlowFiles permite mostrar Downloading/RetryingFiles sin valores.

WhatsAppFlowMediaRecorder acota a 90 segundos la captura completa. Los reintentos vencidos se filtran en el lector antes de Take, usando contador y primera falla existentes. El DTO de respuesta transporta cantidad de archivos y fecha recibida; el nombre de pregunta rechazada se obtiene de la proyección sellada, sin cambiar sus bytes ni el perfil.

Etapa 3: `WhatsAppStructuredRecordOriginSource` adapta el origen neutral por lote; sólo entrega ConversationId si conserva whatsapp.read antes y después. El canal, remitente e instante enriquecen respuestas ya autorizadas por el consumidor. No referencia BusinessForms ni envía mensajes al abrir una conversación.

G6: WhatsAppStructuredRecordActionChannel implementa el puerto neutral phone mediante WhatsAppFormActionService. Deduplica por RunId y usuario destinatario antes de resolver cambios de teléfono, y revalida cuenta activa, número confirmado, vínculo fijado y ventana de 24 h. Las salidas del sistema no requieren atención Manual ni permisos de un operador inexistente. ChatQuery oculta cuerpos FormAction sin lectura de registros; la lista siempre usa un preview genérico. Los límites del dispatch usan OperationLog y no llaman a Meta dentro de la transacción.

ReviewAsync aplica la decisión humana por ejecución/destinatario bajo su propio límite y lock de fila; no consulta Meta ni envía. Reviewed como salió satisface la acción por decisión humana y preserva la evidencia; no salió habilita sólo el retry explícito posterior.

El estado neutral conserva la revisión humana original en su salida por destinatario, separada de los receipts del proveedor. El consumidor decide si un fallo conocido admite retry: el aviso a un rol no ofrece reintento parcial.

WhatsAppFormActionService.ObserveAsync lee estados y receipts locales mediante IWhatsAppFormActionReader, un lote por canal. Reutiliza el mapeo neutral de estados, sin UoW ni contacto/proveedor; no transporta cuerpo, teléfono, wamid ni claves.
