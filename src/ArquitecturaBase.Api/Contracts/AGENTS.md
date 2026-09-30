# Contracts: entrada HTTP

Define cuerpos `*HttpRequest` y parámetros `*Query` por área. Los controllers los mapean a pedidos de Application.

## Al modificar

- Usá contratos del borde en acciones de negocio, sin binding sobre modelos de Application.
- Un contrato con datos personales, códigos o tokens sobrescribe `ToString()` para evitar que MVC los registre.
- Conservá tipos JSON, nombres y campos obligatorios del contrato. MVC y los componentes HTTP comparten opciones JSON.
- Un cambio se verifica en tests de cuerpos ilegibles, validación y OpenAPI; actualizá consumidores del front cuando corresponda.

## Referencias

- [Decisión de contratos HTTP](../../../docs/decisions/0002-contratos-http.md).
- [Borde HTTP](../../../docs/architecture/backend.md).
- [Pruebas del contrato](../../../tests/ArquitecturaBase.Api.IntegrationTests/Contracts).
