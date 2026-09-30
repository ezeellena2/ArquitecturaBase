# Authentication: principal del protocolo

`OpenIdPrincipalFactory` transforma los datos de Application en el principal y destinos de claims que usa OpenIddict.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/identidad.md`](../../../docs/features/identidad.md).

## Al modificar

- Los claims necesarios para protocolo y cliente se construyen en el borde; el servicio de negocio no conoce `ClaimsPrincipal`.
- No convertir roles en permisos efectivos de la Api: la autorización consulta su servicio.
- Un claim nuevo debe revisar su destino y exposición en tokens y userinfo, con tests del flujo OIDC.

## Verificación y ejemplos

- [Pruebas de protocolo](../../../tests/ArquitecturaBase.Api.IntegrationTests/Auth/ConnectFlowTests.cs).
