
# Inteligencia artificial: módulo opcional

EF sólo en repositorios/lectores propios. AES-GCM y HMAC con anillo externo, ID y propósito. Clientes HTTP fijos sin redirects, loggers, retries ni hedging; respuesta/tope/timeout acotados y semáforo sin cola compartido por catálogo/prueba. Modelos por GET autenticado; Anthropic recorre páginas con plazo total y límites, rechazando catálogos parciales. Sólo ID/nombre validados, nunca cuerpo crudo del proveedor. Opciones/DI en el registro del módulo, migraciones en la cadena única.

El asistente persiste conversaciones, turnos, tools cifradas y recibos separados de su contenido.
El repositorio serializa presupuesto → actor → conversación después del lock de conexión cuando
corresponde; cuotas y capacidad se cuentan en PostgreSQL incluso para una conversación retirada.
Revision y Fence condicionan los UPDATE, y los índices únicos impiden activos duplicados por conversación y por actor/canal/ChannelConversationId para interpretación fuera de Inicio. Nulls no distintos conserva el grupo sin ámbito; el repositorio compara la misma clave nullable. La consulta y el índice excluyen NormalizeAudio y ReadDocument (1 y 2) desde el propósito numérico sellado con CASE/IS JSON OBJECT sobre ExecutionProfileJson text, sin columnas redundantes; un perfil ausente o sin un propósito técnico numérico conserva la restricción. El Down del índice rechaza duplicados activos no representables antes de modificarlo y nunca cierra ni borra operaciones.
Los filtros propios ocultan conversaciones y turnos retirados; la recuperación por UUID sólo
proyecta metadata y omite ciphertexts retirados antes de descifrar.
Mantenimiento sigue entidades y borra tools/recibos sin operaciones bulk. Una conversación
retirada conserva metadata durante la ventana de sus turnos; cuando todos vencieron y no hay
activos, se borra físicamente con cascade de turnos/tools. Los recibos no tienen FK al contenido.

[Workers](Workers/AGENTS.md) coordina scopes nuevos y HTTP sin UoW. Deadline total fijo,
cancelación cooperativa y reintento exclusivo del cierre ya obtenido; un Running abandonado
no vuelve a generar. Límites/perfiles explícitos en AiAssistantOptions; un catálogo de modelos
no acredita compatibilidad. Meters del módulo sin preguntas ni etiquetas de actor/conversación.
I10: `AddTurn` guarda en `TraceParent` el contexto W3C activo de quien acepta (sin uno, nada), que continúa el coordinador.

Los detalles del negocio usan JSONB nullable y ValueComparer para las colecciones; la migración
aditiva conserva los siete textos, revisión y comprobantes. La huella histórica sin details no
cambia; details conservan la proyección v1 y PlaceId agrega una extensión versionada.
Places usa un puerto/cliente independiente: origen fijo, clave externa de servidor, sin redirects,
loggers ni retries, con plazo/bytes/concurrencia acotados. Sólo autocomplete, sin Details ni caché.
Guardar conserva el ID, nunca etiquetas de Google. No compartir credenciales con LLM.
Leer [configurar Places](../../../../docs/guides/configurar-google-places.md).

Hereda las instrucciones del proyecto. Su fuente funcional es [inteligencia artificial](../../../../docs/features/inteligencia-artificial.md); operación y seguridad, [configurar IA](../../../../docs/guides/configurar-ia.md); independencia, [quitar IA](../../../../docs/guides/quitar-ia.md). No importar otros módulos. Actualizar mapa y fuentes con un cambio de responsabilidad.

El listado de conversaciones proyecta sólo ciphertext y contexto mínimo del primer turno mediante LEFT JOIN Ordinal=1, pertenencia compuesta e índice único existente. Mantener conteo y página acotados, sin N+1 ni respuestas/evidencias/tools para títulos. No añadir una tabla/columna de títulos al modificar el panel.

Las conversaciones agregan ChannelId; Inicio lista y consulta sólo home. El turno sella una vez ExecutionProfileJson acotado y no sustituye versiones aceptadas por configuración actual. Los perfiles modelan ID y versión exactos. El lector interno adjunta los resultados protegidos de tools por lote para un turno, y el listado de turnos de Inicio los adjunta por página para mostrar una propuesta. Proposal conserva el ordinal seleccionado y referencias a lecturas protegidas existentes; no hay tabla ni copia de observaciones.

I4: un valor nuevo del sello (propósito, archivo, modo, instrucciones, capacidad o terminal) o del proveedor, que se guarda como texto, se escribe un despliegue después del que ya lo lee ([despliegue](../../../../docs/guides/despliegue.md)). `ListPendingAsync` trae en una consulta los pendientes en orden, hasta el tope más uno: el claim toma el primero que esta versión entiende. Un proveedor guardado que no conoce se lee `AiProviders.Unsupported` (`AiProviderConverter`), nunca se escribe, y el lector da su conexión pausada.

El adaptador existente de proveedores despacha NormalizeAudio al transporte STT después de la reserva durable. Lee el objeto privado con límite exacto y verifica tamaño/hash/vencimiento antes del único multipart a OpenAI; usa la misma clave de conexión. El cliente STT no agrega cola, semáforo, worker, retries, redirects ni credencial propia. AudioModelProfiles declara formatos/bytes/modelo/ID/versión explícitos y separados de ModelProfiles; sin calificación explícita no hay normalización. AudioSeconds nullable se proyecta como metadata de consumo aunque se purgue el contenido.

El transporte admite hasta dieciséis definiciones en total entre las capacidades aplicables al contexto y sus terminales, independientemente de las invocaciones. Conserva el límite de respuesta ToolCalls+1, seis invocaciones del motor y cinco pasos por turno; agrandar un catálogo no cambia bytes, cuotas ni esos presupuestos.

Las aceptaciones del actor se cuentan por canal y propósito desde el recibo, no mediante JOIN al contenido borrable. Del turno que nombra sólo se mira si terminó `Expired` (venció sin reservar un paso): ese recibo devuelve la cuota y sigue deduplicando (I1); un turno ya borrado no la devuelve. La cuota de instalación y las capacidades siguen globales. Un legado desconocido cuenta en cada grupo compatible con sus dimensiones conocidas; backfill sólo desde conversación/turno/perfil válidos, sin default Home ni texto.

ReadDocument tiene su cliente (`OpenAiDocumentReadingClient`), que sólo llama el adaptador existente después de leer la copia privada igual que un audio: un POST a `/v1/responses` con el PDF como `input_file` o la foto como `input_image` en detalle alto, salida con esquema estricto y `store:false`, sin redirects, loggers ni reintentos, con su tope de cuerpo (el archivo en base64, sin escapar, más 64 KiB) y `DocumentTimeoutSeconds`. El cuerpo se escribe en una pasada con `Utf8JsonWriter` sobre un arreglo del pool que vuelve limpio, sin pasar el archivo por cadenas, y la lectura vuelve como la escribió el modelo: el idioma lo normaliza el motor. `DocumentModelProfiles` va aparte y vacío por defecto. `IsDocument` parte el cupo del recibo y su CHECK impide uno de audio y de documento a la vez.

NormalizeAudio convierte las excepciones de almacenamiento no canceladas (incluido AggregateException al agotar reintentos, apertura, lectura y cierre del stream) a ContentUnavailable sólo dentro del tramo previo al cliente STT. No transforma en incierto un archivo que no llegó al proveedor; cancelación y errores del POST conservan su clasificación propia.

Retención física (I3): ninguna conversación, de Inicio o de un canal, necesita retirada cuando vencieron contenido y recibos y no hay turnos activos; una de Inicio retirada no espera los recibos. La consulta usa ConversationId de recibos independientes, sin FK/cascade ni inferencia desde el vencimiento del turno. Las conversaciones de una cuenta borrada se listan por dueño para retirarlas aun sin contenido; como `IgnoreQueryFilters` vale para toda la consulta, el estado activo va explícito.

I2: la purga de contenido también toma una grabación o una lectura de Inicio enviada hace más de 24 horas que ningún turno de una conversación activa nombra: el audio por `AudioRequestId` y la lectura por la clave de su copia en `AttachmentKey`, que nombra su pedido, comparada entera (una por extensión) para que la responda el índice filtrado bajo el lock del presupuesto. Como `IgnoreQueryFilters` vale para toda la consulta, los turnos que nombran piden la conversación activa explícita: uno purgado de una retirada conserva `AudioRequestId` y `AttachmentKey`. Los canales técnicos, el prefijo y las extensiones de la clave y el corte los pasa Application; sin columna ni índice nuevos. Al borrar una conversación, el repositorio proyecta de sus turnos el `AudioRequestId` y el `AttachmentKey`, cuya clave nombra la lectura: un perfil que esta versión no lee no la esconde.

AttachmentKey del turno tiene un índice filtrado para la retención de la copia de una planilla; el repositorio proyecta, sin seguimiento, el último turno de la conversación con planilla para heredarla, y el lector da las claves que algún turno con contenido todavía tiene y los pedidos de un canal cuya operación todavía sirve (pendiente, en curso o completa, con contenido: la lectura de un documento de Inicio guarda su copia, y la normalización de un audio, la suya con un turno que la nombre). Esa consulta va por RequestId sin índice: la acotan el lote de la limpieza (hasta 128 claves por vez) y los turnos `home-document` o `home-audio` con contenido vigente; sin migración por ahora. Las filas de turnos y conversaciones no traen lo sellado: el lector lo da por turno pedido, sólo con la conversación activa.
