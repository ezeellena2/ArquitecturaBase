# Inteligencia artificial: módulo opcional

Controller MVC y contratos redacted; mapeo manual a servicios propios. Conexión exige ai.read, y escritura/catálogo/prueba/comprobantes también ai.manage. Catálogo usa POST /connection/models para mantener claves fuera de URL y el rate limit de escritura; no persiste. Información del negocio exige ai.business.read, y escritura/comprobantes también ai.business.manage. Rate limit por operador después de autenticación, cuerpo acotado, inventario/OpenAPI y ProblemDetails.

El asistente vive bajo /api/ai/assistant y exige ai.assistant.use, independiente de administración. Submit acepta UUID/body/revisiones y devuelve 202; las lecturas no generan. Cultura del turno sale de la localización ya resuelta, nunca del body. Historial/estado/cancelación/eliminación son privados; respuestas con contenido no-store. ToString del contrato redacta pregunta; máximo16KiB. Herramientas, claves y protocolos quedan detrás de servicios Application.

Audio de Inicio (IA4): POST /api/ai/assistant/audio recibe un multipart cerrado (`requestId` y `file`, 1.000.000 bytes más 64 KiB de envoltorio) con el cupo de envío y responde 202 con el estado; GET audio/{requestId} da el estado y la transcripción; GET audio/{requestId}/file, la grabación con nosniff. El contrato redacta el archivo y la cultura sale de la localización; el tipo, de los bytes.

Planillas de Inicio (Etapa 2): POST /api/ai/assistant/file-turns recibe un multipart con los campos del turno (`question` y `audioRequestId` opcionales) y `file`, 10 MiB más 64 KiB de envoltorio, con el cupo de envío, y responde 202 como el turno JSON, que no cambia. El contrato redacta pregunta y archivo; el descriptor y lo preparado los pone el servidor.

El PUT de negocio admite details opcional con contratos HTTP anidados propios; máximo 256 KiB.
No aceptar un enlace arbitrario. El enlace se deriva de PlaceId o coordenadas históricas.
POST /business-information/places/search exige ambos permisos del negocio, cuerpo 4 KiB,
rate limit propio y no-store. Input redactado y clave sólo en Infrastructure, sin errores crudos
ni transacciones; errores de guardado por grupo en details.location/openingHours/faq.

Hereda las instrucciones del proyecto. Su fuente funcional es [inteligencia artificial](../../../../docs/features/inteligencia-artificial.md); operación y seguridad, [configurar IA](../../../../docs/guides/configurar-ia.md); independencia, [quitar IA](../../../../docs/guides/quitar-ia.md). No importar otros módulos. Actualizar mapa y fuentes con un cambio de responsabilidad.
