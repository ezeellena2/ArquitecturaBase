# ServiceDefaults: observabilidad y salud

`Extensions.cs` concentra los defaults compartidos de Aspire: OpenTelemetry, descubrimiento de servicios, resiliencia HTTP y endpoints de salud.

## Al modificar

- Esta capa contiene defaults técnicos comunes; no agregar lógica de negocio, consultas de datos ni referencias a áreas del producto.
- Readiness expresa dependencias disponibles; liveness, que el proceso sigue vivo. Una dependencia caída no se etiqueta como `live`.
- Los health checks concretos de PostgreSQL y Redis se registran con sus adaptadores en Infrastructure.
- Un cambio afecta métricas, trazas y salud de la Api: verificá `HealthCheckTests` y, cuando corresponda, el dashboard de Aspire.

## Referencias

- [Arquitectura y observabilidad](../../docs/architecture/backend.md).
- [Pruebas de salud](../../tests/ArquitecturaBase.Api.IntegrationTests/HealthCheckTests.cs).
