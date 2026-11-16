# ServiceDefaults: observabilidad y salud

`Extensions.cs` concentra los defaults compartidos de Aspire: OpenTelemetry, descubrimiento de servicios y endpoints de salud.

## Al modificar

- Esta capa contiene defaults técnicos comunes; no agregar lógica de negocio, consultas de datos ni referencias a áreas del producto.
- Readiness expresa dependencias disponibles; liveness, que el proceso sigue vivo. Una dependencia caída no se etiqueta como `live`.
- Los health checks concretos de PostgreSQL y Redis se registran con sus adaptadores en Infrastructure.
- Fuera de Development y Testing la consola escribe JSON con los scopes (N10): el `TraceId` que ve la persona en un error y el `UserId` de `Api/RequestContext/UserLogScope.cs`. El exportador OTLP se prende sólo con `OTEL_EXPORTER_OTLP_ENDPOINT` ([despliegue](../../docs/guides/despliegue.md#logs-trazas-y-alertas)).
- Los `HttpClient` no reciben resiliencia por defecto (N18): la estándar reintenta un POST, y un POST repetido duplica el efecto en el proveedor. Cada cliente declara la suya, como el de mensajes de WhatsApp (`Retry.DisableForUnsafeHttpMethods()`); `HttpClientRetryTests` recorre los clientes con nombre.
- La readiness (todos los checks) se calcula como mucho una vez cada 2 s (`CachedHealthCheckService`, N6): una ráfaga a `/health` comparte el cálculo y no llega a la base ni a Redis. Una consulta con filtro, como la liveness, corre siempre.
- Un cambio afecta métricas, trazas y salud de la Api: verificá `HealthCheckTests` y, cuando corresponda, el dashboard de Aspire.

## Referencias

- [Arquitectura y observabilidad](../../docs/architecture/backend.md).
- [Pruebas de salud](../../tests/ArquitecturaBase.Api.IntegrationTests/HealthCheckTests.cs).
