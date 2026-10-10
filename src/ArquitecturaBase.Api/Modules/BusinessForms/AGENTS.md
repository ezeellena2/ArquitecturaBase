# BusinessForms: borde HTTP

Leer [formularios](../../../../docs/features/formularios.md) y el [contrato](../../../../docs/plans/formularios-etapa-1-contrato.md).
- Seis rutas de borrador MVC; esos controllers sólo IFormDraftService. Contratos HTTP propios mapeados a mano y ToString redactado.
- Read en todas; Manage adicional al escribir. Recuperación sólo del actor, sin requerir Manage. Sin publicación, envío ni respuestas.
- Declarar esquemas OpenAPI, límite de cuerpo 512 KiB y rate limit por actor. Idempotencia no evita permisos actuales.
- Errors mediante ToActionResult/ToCreatedResult. Expiración 410 traducido; 404 de intento en vuelo no prueba rollback.
