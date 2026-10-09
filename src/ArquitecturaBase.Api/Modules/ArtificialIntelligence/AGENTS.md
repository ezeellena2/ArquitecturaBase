# Inteligencia artificial: módulo opcional

Controller MVC y contratos redacted; mapeo manual a servicios propios. Conexión exige ai.read, y escritura/catálogo/prueba/comprobantes también ai.manage. Catálogo usa POST /connection/models para mantener claves fuera de URL y el rate limit de escritura; no persiste. Información del negocio exige ai.business.read, y escritura/comprobantes también ai.business.manage. Rate limit por operador después de autenticación, cuerpo acotado, inventario/OpenAPI y ProblemDetails.

El asistente vive bajo /api/ai/assistant y exige ai.assistant.use, independiente de administración. Submit acepta UUID/body/revisiones y devuelve 202; las lecturas no generan. Cultura del turno sale de la localización ya resuelta, nunca del body. Historial/estado/cancelación/eliminación son privados; respuestas con contenido no-store. ToString del contrato redacta pregunta; máximo16KiB. Herramientas, claves y protocolos quedan detrás de servicios Application.

El PUT de negocio admite details opcional con contratos HTTP anidados propios; máximo 256 KiB.
No aceptar un enlace arbitrario. El enlace se deriva de PlaceId o coordenadas históricas.
POST /business-information/places/search exige ambos permisos del negocio, cuerpo 4 KiB,
rate limit propio y no-store. Input redactado y clave sólo en Infrastructure, sin errores crudos
ni transacciones; errores de guardado por grupo en details.location/openingHours/faq.

Hereda las instrucciones del proyecto. Su fuente funcional es [inteligencia artificial](../../../../docs/features/inteligencia-artificial.md); operación y seguridad, [configurar IA](../../../../docs/guides/configurar-ia.md); independencia, [quitar IA](../../../../docs/guides/quitar-ia.md). No importar otros módulos. Actualizar mapa y fuentes con un cambio de responsabilidad.
