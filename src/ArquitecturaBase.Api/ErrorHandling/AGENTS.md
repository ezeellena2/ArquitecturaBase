# ErrorHandling: una respuesta de error consistente

Mapea `Result`, validación, excepciones y errores del framework a ProblemDetails. Los helpers de controllers seleccionan la respuesta de éxito correspondiente.

## Al modificar

- Los errores de negocio ya vienen declarados en Domain; no crear códigos funcionales acá.
- Toda falla lleva `code` y `traceId`, con `title` y `detail` traducidos. Validación agrega campos camelCase; cuerpo ilegible conserva `Request.Invalid` sin `errors`.
- Una excepción responde 500 genérico sin detalles internos. No filtrar mensajes de proveedores ni registrar secretos.
- El orden de `UseStatusCodePages` y los middlewares que cortan la petición es parte del contrato; probá también errores sin controller.

## Referencias

- [ProblemDetails y pipeline](../../../docs/architecture/backend.md).
- [Pruebas de errores HTTP](../../../tests/ArquitecturaBase.Api.IntegrationTests/Contracts).
