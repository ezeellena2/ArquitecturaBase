
# Inteligencia artificial: módulo opcional

EF sólo en repositorios/lectores propios. AES-GCM y HMAC con anillo externo, ID y propósito. Clientes HTTP fijos sin redirects, loggers, retries ni hedging; respuesta/tope/timeout acotados y semáforo sin cola compartido por catálogo/prueba. Modelos por GET autenticado; Anthropic recorre páginas con plazo total y límites, rechazando catálogos parciales. Sólo ID/nombre validados, nunca cuerpo crudo del proveedor. Opciones/DI en el registro del módulo, migraciones en la cadena única.

El asistente persiste conversaciones, turnos, tools cifradas y recibos separados de su contenido.
El repositorio serializa presupuesto → actor → conversación después del lock de conexión cuando
corresponde; cuotas y capacidad se cuentan en PostgreSQL incluso para una conversación retirada.
Revision y Fence condicionan los UPDATE, y los índices únicos impiden activos duplicados.
Los filtros propios ocultan conversaciones y turnos retirados; la recuperación por UUID sólo
proyecta metadata y omite ciphertexts retirados antes de descifrar.
Mantenimiento sigue entidades y borra tools/recibos sin operaciones bulk. Una conversación
retirada conserva metadata durante la ventana de sus turnos; cuando todos vencieron y no hay
activos, se borra físicamente con cascade de turnos/tools. Los recibos no tienen FK al contenido.

[Workers](Workers/AGENTS.md) coordina scopes nuevos y HTTP sin UoW. Deadline total fijo,
cancelación cooperativa y reintento exclusivo del cierre ya obtenido; un Running abandonado
no vuelve a generar. Límites/perfiles explícitos en AiAssistantOptions; un catálogo de modelos
no acredita compatibilidad. Meters del módulo sin preguntas ni etiquetas de actor/conversación.

Los detalles del negocio usan JSONB nullable y ValueComparer para las colecciones; la migración
aditiva conserva los siete textos, revisión y comprobantes. La huella histórica sin details no
cambia; details conservan la proyección v1 y PlaceId agrega una extensión versionada.
Places usa un puerto/cliente independiente: origen fijo, clave externa de servidor, sin redirects,
loggers ni retries, con plazo/bytes/concurrencia acotados. Sólo autocomplete, sin Details ni caché.
Guardar conserva el ID, nunca etiquetas de Google. No compartir credenciales con LLM.
Leer [configurar Places](../../../../docs/guides/configurar-google-places.md).

Hereda las instrucciones del proyecto. Su fuente funcional es [inteligencia artificial](../../../../docs/features/inteligencia-artificial.md); operación y seguridad, [configurar IA](../../../../docs/guides/configurar-ia.md); independencia, [quitar IA](../../../../docs/guides/quitar-ia.md). No importar otros módulos. Actualizar mapa y fuentes con un cambio de responsabilidad.

El listado de conversaciones proyecta sólo ciphertext y contexto mínimo del primer turno mediante LEFT JOIN Ordinal=1, pertenencia compuesta e índice único existente. Mantener conteo y página acotados, sin N+1 ni respuestas/evidencias/tools para títulos. No añadir una tabla/columna de títulos al modificar el panel.

Las conversaciones agregan ChannelId; Inicio lista y consulta sólo home. El turno sella una vez ExecutionProfileJson acotado y no sustituye versiones aceptadas por configuración actual. Los perfiles modelan ID y versión exactos. El lector interno adjunta los resultados protegidos de tools por lote para un turno; listados de Inicio no cargan tools. Proposal conserva el ordinal seleccionado y referencias a lecturas protegidas existentes; no hay tabla ni copia de observaciones.

El adaptador existente de proveedores despacha NormalizeAudio al transporte STT después de la reserva durable. Lee el objeto privado con límite exacto y verifica tamaño/hash/vencimiento antes del único multipart a OpenAI; usa la misma clave de conexión. El cliente STT no agrega cola, semáforo, worker, retries, redirects ni credencial propia. AudioModelProfiles declara formatos/bytes/modelo/ID/versión explícitos y separados de ModelProfiles; sin calificación explícita no hay normalización. AudioSeconds nullable se proyecta como metadata de consumo aunque se purgue el contenido.

El transporte admite hasta doce definiciones (once capacidades del canal y un terminal), independientemente de las invocaciones. Conserva el límite de respuesta ToolCalls+1, seis invocaciones del motor y cuatro pasos por turno; agrandar un catálogo no cambia bytes, cuotas ni esos presupuestos.

Las aceptaciones del actor se cuentan por canal y propósito desde el recibo, no mediante JOIN al contenido borrable. La cuota de instalación y las capacidades siguen globales. Un legado desconocido cuenta en cada grupo compatible con sus dimensiones conocidas; backfill sólo desde conversación/turno/perfil válidos, sin default Home ni texto.
