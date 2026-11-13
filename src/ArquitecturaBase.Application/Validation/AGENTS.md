# Validation: validación de pedidos

Agrupa validadores FluentValidation por área. Los mecanismos comunes viven en `Common/Validation`; los mensajes, en `Resources`.

## Al modificar

- Validá forma y rangos del pedido con constantes del modelo o entidad. Cada regla dice un texto de `ValidationMessages` o del `<Entidad>Errors` de su área (`ErrorMessages.Get(<Error>.Code)`), con su `WithMessage(_ => …)` o con un helper de `ValidationRules` (`ValidationMessageTests`). Las decisiones que requieren datos viven en el servicio.
- Un pedido paginado hereda las reglas comunes de `PagedRequestValidator`; si lee menos filas por vez, le pasa su tope en vez de sumar otra regla de tamaño. Mantené su whitelist de orden coherente con el lector.
- No lanzar por una entrada inválida ni construir errores de negocio acá; usá el resultado común de validación.
- Probá campos, límites y mensajes en Application.UnitTests sin proveedores externos.

## Referencias

- [Validación y errores](../../../docs/architecture/backend.md).
- [Validadores de referencia](Roles).
- [Pruebas de validación y casos de uso](../../../tests/ArquitecturaBase.Application.UnitTests).
