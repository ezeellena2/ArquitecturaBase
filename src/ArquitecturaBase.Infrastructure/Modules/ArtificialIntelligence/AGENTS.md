
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
